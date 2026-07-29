using Auth.API.Extensions;
using Auth.API.HealthChecks;
using Auth.API.Middleware;
using Auth.API.Hubs;
using Auth.API.Seed;
using Auth.Models.Data;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.FLS;
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
            .AllowCredentials();
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

builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<ISkillService, SkillService>();
builder.Services.AddScoped<IAnswerService, AnswerService>();
builder.Services.AddScoped<IJournalService, JournalService>();
builder.Services.AddScoped<IMentorMenteeService, MentorMenteeService>();
builder.Services.AddScoped<IVolunteeringService, VolunteeringService>();

// Gamification & Audit
builder.Services.AddScoped<IGameScoreService, GameScoreService>();
builder.Services.AddScoped<IAuditService, AuditService>();

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


// ? Seed data after migrations
await SeedData.SeedRolesAsync(app.Services.CreateScope().ServiceProvider);
await SeedData.SeedStaffAccountsAsync(app.Services.CreateScope().ServiceProvider);
await SeedData.SeedQuestionsAsync(app.Services.CreateScope().ServiceProvider);
await SeedData.SeedUsersAsync(app.Services.CreateScope().ServiceProvider);
await SeedData.SeedMentorsAsync(app.Services.CreateScope().ServiceProvider);

app.Run();
