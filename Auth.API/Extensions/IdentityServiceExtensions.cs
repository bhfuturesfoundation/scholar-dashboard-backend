using System.Security.Claims;
using Auth.API.Helpers;
using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Request;
using Auth.Services.Interfaces;
using Auth.Services.Settings;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Auth.API.Extensions
{
    public static class IdentityServiceExtensions
    {
        public static IServiceCollection AddIdentityServices(this IServiceCollection services, ConfigurationManager configuration)
        {
            services.AddIdentity<User, IdentityRole>(options =>
            {
                // Password settings
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;

                // Lockout settings
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;

                // User settings
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false; // Require confirmed email

                // Token provider settings
                options.Tokens.EmailConfirmationTokenProvider = "Default";
                options.Tokens.PasswordResetTokenProvider = "Default";
                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            var secret = Env.GetString("JWT_SECRET");
            var issuer = Env.GetString("JWT_ISSUER");
            var audience = Env.GetString("JWT_AUDIENCE");

            services.Configure<JWTSettings>(opts =>
            {
                opts.Secret = secret;
                opts.Issuer = issuer;
                opts.Audience = audience;
                opts.ExpirationInMinutes = 480; // 8 hours
                opts.RefreshTokenExpirationInDays = 7;
            });

            services.Configure<RefreshTokenSettings>(opts =>
            {
                opts.ExpirationInDays = 7;
                opts.MaxRefreshCount = 100;
                opts.MaxActiveSessionsPerUser = 5;
                opts.EnableTokenRotation = true;
                opts.DetectTokenReuse = true;
            });

            var key = Encoding.ASCII.GetBytes(secret);

            // Hoisted so the refresh path below can validate the newly minted token against
            // exactly the same rules the middleware uses.
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = tokenValidationParameters;

                options.Events = new JwtBearerEvents
                {
                    /// <summary>
                    /// Rejects an otherwise-valid token whose generation has been superseded.
                    ///
                    /// A JWT is, by design, valid until it expires — the server does not get a
                    /// say. That makes "sign out everywhere" a half-measure unless something
                    /// checks: revoking refresh tokens stops renewal, but the access token
                    /// already sitting in a shared browser keeps working. This closes that.
                    ///
                    /// The check is a memory-cache lookup in the steady state, so it costs a
                    /// dictionary read per request rather than a database round trip.
                    /// </summary>
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (principal is null) return;

                        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                                     ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

                        if (string.IsNullOrEmpty(userId)) return;

                        var versions = context.HttpContext.RequestServices
                            .GetRequiredService<ITokenVersionCache>();

                        // A token with no "tv" claim predates this mechanism. Treated as
                        // version 0, which is what every existing account holds, so tokens
                        // issued before this deployed keep working until they expire.
                        _ = int.TryParse(principal.FindFirstValue("tv"), out var tokenVersion);

                        if (await versions.IsTokenCurrentAsync(userId, tokenVersion)) return;

                        context.Fail("This session was ended. Please sign in again.");

                        context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>()
                            .LogInformation(
                                "Rejected a superseded access token for {UserId} (tv {Version}).",
                                userId, tokenVersion);
                    },

                    OnAuthenticationFailed = async context =>
                    {
                        /// Check if JWT is expired
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            var httpContext = context.HttpContext;
                            var refreshToken = httpContext.Request.Cookies["refresh_token"];

                            if (string.IsNullOrEmpty(refreshToken))
                                return;

                            try
                            {
                                var expiredToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
                                if (string.IsNullOrEmpty(expiredToken))
                                    return;

                                var authService = httpContext.RequestServices.GetRequiredService<IAuthService>();
                                var logger = httpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();

                                logger.LogInformation("Trying to refresh expired token");

                                var refreshRequest = new RefreshTokenRequest
                                {
                                    Token = expiredToken,
                                    RefreshToken = refreshToken
                                };

                                var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
                                var response = await authService.RefreshTokenAsync(refreshRequest, ipAddress);

                                CookieHelper.SetRefreshTokenCookie(httpContext, response.RefreshToken);

                                // Indexer, not .Add — Add throws ArgumentException if the header
                                // is already present, which turned a refresh into a 500.
                                httpContext.Response.Headers["X-New-Token"] = response.Token;

                                // Authenticate THIS request with the token we just minted.
                                //
                                // Previously the refresh only stashed the token and let the
                                // pipeline continue unauthenticated, so OnChallenge returned 401
                                // with the token in the body and every client was expected to
                                // notice, store it, and retry. Only apiClient did that — raw
                                // fetch callers logged the user out, and the SignalR client
                                // cannot participate in that handshake at all, which is why hub
                                // negotiation failed with "Status code '401'".
                                //
                                // Succeeding here means the request proceeds normally: no 401, no
                                // retry round-trip, and hubs connect. The X-New-Token header is
                                // then just an optimisation for clients that want to rotate their
                                // stored copy.
                                var handler = new JwtSecurityTokenHandler();
                                var principal = handler.ValidateToken(
                                    response.Token, tokenValidationParameters, out _);

                                context.Principal = principal;
                                context.Success();

                                logger.LogInformation("Expired token refreshed; request authenticated with the new token.");
                            }
                            catch (Exception ex)
                            {
                                var logger = httpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                                logger.LogWarning(ex, "Failed refreshing token");
                            }
                        }
                    },
                    OnChallenge = async context =>
                    {
                        var httpContext = context.HttpContext;

                        // A successful refresh now authenticates the request in
                        // OnAuthenticationFailed, so this event is only reached when the caller
                        // genuinely has no valid session. The old "401 + token_refreshed" branch
                        // is gone: it required every client to know the protocol, and returning
                        // 401 for a request the server had just authorised was the bug.
                        context.HandleResponse();
                        var logger = httpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogWarning("Unauthorized access. Token may be invalid or expired.");

                        httpContext.Response.StatusCode = 401;
                        httpContext.Response.ContentType = "application/json";

                        var errorResponse = new
                        {
                            success = false,
                            message = "You are not authorized, or token is expired."
                        };

                        await httpContext.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(errorResponse));
                    },

                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        // Any hub, not a named one.
                        //
                        // A browser cannot set an Authorization header on a WebSocket or an
                        // EventSource, so SignalR sends the token as ?access_token= instead.
                        // This check used to name /hubs/minigames explicitly, which meant the
                        // next hub anyone added would 401 on connect with no obvious cause —
                        // exactly what happened to /hubs/notifications.
                        //
                        // Still scoped to hub paths: accepting a token from the query string
                        // everywhere would put credentials in URLs, and therefore in access
                        // logs and Referer headers, across the whole API.
                        var isHubPath =
                            path.StartsWithSegments("/hubs") || path.StartsWithSegments("/api/hubs");

                        if (!string.IsNullOrEmpty(accessToken) && isHubPath)
                        {
                            context.Token = accessToken;
                            return Task.CompletedTask;
                        }

                        var token = context.Request.Headers["Authorization"].FirstOrDefault();
                        if (!string.IsNullOrEmpty(token))
                        {
                            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                            {
                                context.Token = token.Substring("Bearer ".Length).Trim();
                            }
                            else
                            {
                                context.Token = token;
                            }
                        }

                        return Task.CompletedTask;
                    }
                };
            });

            return services;
        }
    }
}
