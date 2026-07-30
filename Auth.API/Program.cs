using Auth.API.Extensions;
using Auth.API.HealthChecks;
using Auth.API.Middleware;
using Auth.API.Hubs;
using Auth.API.Seed;
using Auth.Models.Data;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.FLS;
using Auth.Services.Interfaces.Operations;
using Auth.Services.Interfaces.Storage;
using Auth.Services.Services.Operations;
using Auth.Services.Services.Storage;
using Auth.Services.Settings;
using Auth.Services.Services;
using Auth.Services.Services.FLS;
using DotNetEnv;
using Mapster;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ? Load local .env if exists (only for dev)
Env.TraversePath().Load();

// === Add services ===
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddIdentityServices(builder.Configuration);
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddEmailProviders(builder.Configuration);
builder.Services.AddRabbitMQServices(builder.Configuration);
builder.Services.AddAppRateLimiter(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy.WithOrigins(
                "https://scholar-dashboard-frontend.vercel.app",
                "http://localhost:3000"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            // Without this the browser hides X-New-Token from JavaScript, so the client can
            // never pick up a token rotated mid-request and keeps sending the expired one.
            .WithExposedHeaders("X-New-Token");
    });
});

// Add scoped/singleton services...
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IManagerService, ManagerService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddSingleton<IResendService, ResendEmailService>();

// Dropbox. Singleton so the minted access token is cached across calls rather than
// re-exchanged on every upload — the tokens last ~4 hours, the refresh token is permanent.
builder.Services.Configure<DropboxOptions>(opts =>
{
    opts.AppKey = builder.Configuration["DROPBOX_APP_KEY"];
    opts.AppSecret = builder.Configuration["DROPBOX_APP_SECRET"];
    opts.RefreshToken = builder.Configuration["DROPBOX_REFRESH_TOKEN"];
});
builder.Services.AddHttpClient(nameof(DropboxStorage), client =>
{
    // Seeding downloads a few hundred KB of CSV; without a timeout a hung connection would
    // stall startup indefinitely.
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<IDropboxStorage, DropboxStorage>();

builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<ISkillService, SkillService>();
builder.Services.AddScoped<IAnswerService, AnswerService>();
builder.Services.AddScoped<IJournalService, JournalService>();
builder.Services.AddScoped<IMentorMenteeService, MentorMenteeService>();
builder.Services.AddScoped<IVolunteeringService, VolunteeringService>();

// Gamification & Audit
builder.Services.AddScoped<IGameScoreService, GameScoreService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IOperationsService, OperationsService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// FLS Speaker Management
builder.Services.AddScoped<IFLSSpeakerService, FLSSpeakerService>();
builder.Services.AddScoped<IFLSUploadService, FLSUploadService>();
builder.Services.AddScoped<IFLSMeetingService, FLSMeetingService>();
builder.Services.AddScoped<IFLSDocumentService, FLSDocumentService>();
builder.Services.AddScoped<IFLSTaskService, FLSTaskService>();
builder.Services.AddScoped<IFLSAdminService, FLSAdminService>();
builder.Services.AddScoped<IFLSNotificationService, FLSNotificationService>();
builder.Services.AddScoped<IFLSCampaignService, FLSCampaignService>();

// Allow large file uploads (max 20 MB)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 20 * 1024 * 1024;
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddMapster();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
var redisConnection = builder.Configuration["REDIS_URL"] ?? builder.Configuration["REDIS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    var redisOptions = new ConfigurationOptions
    {
        AbortOnConnectFail = false,
    };

    if (Uri.TryCreate(redisConnection, UriKind.Absolute, out var redisUri) &&
        (redisUri.Scheme == "redis" || redisUri.Scheme == "rediss"))
    {
        redisOptions.EndPoints.Add(redisUri.Host, redisUri.Port);
        var userInfo = redisUri.UserInfo.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
        if (userInfo.Length == 2)
        {
            redisOptions.Password = userInfo[1];
        }
        redisOptions.Ssl = redisUri.Scheme == "rediss";
    }
    else
    {
        redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
    }

    builder.Services.AddSignalR()
        .AddStackExchangeRedis(redisConnection, options =>
        {
            options.Configuration.ChannelPrefix = RedisChannel.Literal("scholar-minigames");
            options.ConnectionFactory = async writer => await ConnectionMultiplexer.ConnectAsync(redisOptions, writer);
        });
}
else
{
    builder.Services.AddSignalR();
}
builder.Services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();
builder.Services.AddEndpointsApiExplorer();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.LoginPath = "/api/auth/login";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.Name = "AuthProject.Cookies";
    options.SlidingExpiration = true;
});

var app = builder.Build();

// === Middlewares ===
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseRouting();
app.UseCors("AllowSpecificOrigin");
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/healthz");
app.MapHub<MinigamesHub>("/hubs/minigames").RequireRateLimiting("signalr-hub");
app.MapHub<MinigamesHub>("/api/hubs/minigames").RequireRateLimiting("signalr-hub");
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();

    // Detect whether any user tables already exist in the DB.
    // This is true for Railway (schema pre-dates migrations) and for local DBs
    // that were created via EnsureCreated before the migration chain was introduced.
    var hasTables = conn.GetSchema("Tables").Rows.Count > 0;

    await conn.CloseAsync();

    // GetPendingMigrationsAsync creates __EFMigrationsHistory if it doesn't exist yet.
    var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();

    if (hasTables && pendingMigrations.Contains("20250901000000_InitialSchema"))
    {
        // The schema was created before the InitialSchema migration existed (via EnsureCreated).
        // Fake-apply it so MigrateAsync doesn't try to CREATE TABLE on tables that already exist.
        // ON CONFLICT DO NOTHING makes this idempotent in case it was already recorded.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
            "VALUES ('20250901000000_InitialSchema', '8.0.5') ON CONFLICT DO NOTHING");
        Console.WriteLine("Pre-existing schema detected — recorded InitialSchema as applied.");
    }

    // MigrateAsync is always safe: it's a no-op when nothing is pending,
    // creates the DB from scratch on an empty DB, and applies only pending
    // migrations on an existing DB. No more EnsureCreated paths.
    await db.Database.MigrateAsync();
    Console.WriteLine("Database is up-to-date.");
}


// === Seeding ===
//
// Seeding runs after migrations and must NEVER prevent the app from starting. It was
// previously awaited unguarded here, and SeedUsersAsync/SeedMentorsAsync both reach out to
// Dropbox — to download a CSV over a share link whose signature expires, and to upload
// generated passwords using credentials that may be absent. Any of those throwing meant
// app.Run() was never reached and the container crash-looped, taking the whole API down
// over an optional CSV export.
//
// Each seeder is now isolated: one failing is logged and skipped, and the API still serves.
{
    using var seedScope = app.Services.CreateScope();
    var seedLogger = seedScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Seed");

    var dropbox = seedScope.ServiceProvider.GetRequiredService<IDropboxStorage>();
    if (!dropbox.IsConfigured)
        seedLogger.LogWarning("{Hint} Password exports will be skipped; everything else runs normally.", dropbox.ConfigurationHint);

    async Task RunSeederAsync(string name, Func<IServiceProvider, Task> seeder)
    {
        try
        {
            await seeder(seedScope.ServiceProvider);
        }
        catch (Exception ex)
        {
            seedLogger.LogError(ex, "Seeder {Seeder} failed. Startup continues without it.", name);
        }
    }

    await RunSeederAsync(nameof(SeedData.SeedRolesAsync), SeedData.SeedRolesAsync);
    await RunSeederAsync(nameof(SeedData.SeedStaffAccountsAsync), SeedData.SeedStaffAccountsAsync);
    await RunSeederAsync(nameof(SeedData.SeedQuestionsAsync), SeedData.SeedQuestionsAsync);
    await RunSeederAsync(nameof(SeedData.SeedUsersAsync), SeedData.SeedUsersAsync);
    await RunSeederAsync(nameof(SeedData.SeedMentorsAsync), SeedData.SeedMentorsAsync);
}

app.Run();
