using Auth.API.Extensions;
using Auth.API.HealthChecks;
using Auth.API.Middleware;
using Auth.API.Hubs;
using Auth.API.Services;
using Auth.Services.Interfaces.News;
using Auth.Services.Services.News;
using Auth.Services.Interfaces.Notifications;
using Auth.Services.Interfaces.Suggestions;
using Auth.Services.Services.Suggestions;
using Auth.Services.Services.Notifications;
using Auth.Services.Interfaces.Games;
using Auth.Services.Services.Games;
using Auth.API.Seed;
using Auth.Models.Data;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Engagement;
using Auth.Services.Interfaces.FLS;
using Auth.Services.Interfaces.Mailing;
using Auth.Services.Interfaces.Operations;
using Auth.Services.Interfaces.Scholars;
using Auth.Services.Interfaces.Storage;
using Auth.Services.Services.Mailing;
using Auth.Services.Services.Seed;
using Auth.Services.Services.Operations;
using Auth.Services.Services.Scholars;
using Auth.Services.Services.Storage;
using Auth.Services.Settings;
using Auth.Services.Services;
using Auth.Services.Services.Engagement;
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
builder.Services.AddExternalAuthentication(builder.Configuration);
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

// Self-service account operations: profile, sessions, and a personal data export.
builder.Services.AddScoped<IAccountService, AccountService>();

// Profile pictures. Scoped because it writes through the request's DbContext; the image
// pipeline inside it is stateless and holds nothing between calls.
builder.Services.AddScoped<IAvatarService, AvatarService>();

// Checked on every authenticated request, so it must be cheap: an in-memory cache in
// front of Users.TokenVersion. See TokenVersionCache for the multi-instance caveat.
builder.Services.AddScoped<ITokenVersionCache, TokenVersionCache>();

// The suggestion board.
builder.Services.AddScoped<ISuggestionService, SuggestionService>();
builder.Services.AddMemoryCache();
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
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();

// Twice-monthly database backups. Hosted so it runs regardless of whether anyone opens
// the operations console.
builder.Services.AddHostedService<ScheduledBackupService>();
builder.Services.AddScoped<IScholarExportService, ScholarExportService>();
builder.Services.AddScoped<IScholarLifecycleService, ScholarLifecycleService>();
builder.Services.AddScoped<IMentorAssignmentService, MentorAssignmentService>();

// Scholar engagement: progress, badges, peer recognition.
builder.Services.AddScoped<IScholarProgressService, ScholarProgressService>();
builder.Services.AddScoped<IKudosService, KudosService>();

// Partnerships mailing module.
builder.Services.AddSingleton<IContactNameExtractor, ContactNameExtractor>();
builder.Services.AddSingleton<IFirmCategorizer, FirmCategorizer>();
builder.Services.AddScoped<IFirmDirectoryService, FirmDirectoryService>();
builder.Services.AddScoped<IFirmImportExportService, FirmImportExportService>();
builder.Services.AddScoped<IMailingTaxonomyService, MailingTaxonomyService>();
builder.Services.AddScoped<IMailingCampaignService, MailingCampaignService>();
builder.Services.AddScoped<IMailingScheduleService, MailingScheduleService>();

// ── Notifications ────────────────────────────────────────────────────────────
//
// The bell menu, delivery preferences, push, and the journal submission window.
// The window lives here rather than in the frontend because the reminder service needs
// the same rule, and a rule that exists in two places drifts.
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IJournalWindowService, JournalWindowService>();
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();

// Singleton: it holds nothing but IHubContext, which is itself a singleton. Scoping it
// would allocate one per request for no reason.
builder.Services.AddSingleton<INotificationRealtime, SignalRNotificationRealtime>();

// Singleton, and it has to be: presence is the live connection state of the whole
// process. A scoped tracker would be a fresh empty dictionary per request and would
// always report nobody online.
builder.Services.AddSingleton<IPresenceTracker, PresenceTracker>();

// ── Comet Arena ──────────────────────────────────────────────────────────────
//
// Singletons, and they have to be: a match is live in-memory state shared by everyone
// in it. A scoped ArenaService would hand each request its own empty world.
//
// This is what makes the leaderboard real. The server owns the simulation and does the
// arithmetic, so a score was never in the client's hands to forge — see GameScore.Verified.
builder.Services.AddSingleton<IArenaRealtime, SignalRArenaRealtime>();
builder.Services.AddSingleton<IArenaService, ArenaService>();
builder.Services.AddHostedService<ArenaTickService>();

// Typed client so the push service gets connection pooling and the standard handler
// lifetime rather than a socket per send.
builder.Services.AddHttpClient<IPushSender, WebPushSender>();

// Reminders, the weekly digest, and the outbox that actually delivers email and push.
// Hosted, because the whole point is reaching people who have NOT opened the app.
builder.Services.AddHostedService<NotificationSchedulerService>();

// Executes due schedules. Hosted, so it runs whether or not anyone opens the UI.
builder.Services.AddHostedService<MailingSchedulerService>();

// ── News ─────────────────────────────────────────────────────────────────────
//
// Mirrors the foundation's public news page into our own table, replacing what used to be
// a hardcoded array in the frontend. The widget reads only our copy, so the dashboard does
// not depend on a third-party site being up during a page load.
builder.Services.AddHttpClient(NewsScraperService.HttpClientName, client =>
{
    // Well short of the hourly poll, so a hung connection cannot occupy the scraper until
    // the next tick would have started. The page is ~170 KB and normally arrives in under
    // a second; 30 seconds is slack, not a budget.
    client.Timeout = TimeSpan.FromSeconds(30);

    // A descriptive User-Agent is manners, and it is also self-interest. Default .NET
    // agents are widely rate-limited or blocked outright by CDNs, and if this scraper ever
    // does misbehave, whoever runs that server should be able to tell who we are and reach
    // us rather than having to block an anonymous client.
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "BHFF-ScholarDashboard/1.0 (+https://www.bhfuturesfoundation.org; news widget)");

    // Squarespace serves Brotli/gzip by default; asking for HTML explicitly keeps a content
    // negotiator from handing us a JSON or AMP variant of the same page.
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,image/webp,*/*");
});
builder.Services.AddScoped<INewsScraperService, NewsScraperService>();

// Daily re-scrape. Hosted so the news stays current whether or not a member of staff ever
// opens the operations console — the whole failure this replaces was a list nobody updated.
builder.Services.AddHostedService<NewsScraperBackgroundService>();

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

var ProcessStart = DateTime.UtcNow;
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

// Which build is actually running. Unauthenticated and dependency-free on purpose.
//
// This exists because a broken image build is silent: the platform keeps serving the last
// good image, so the API stays up and healthy while running weeks-old code. That went
// unnoticed until endpoints that had been merged and pushed returned 404 in production.
// One curl now answers "is my code deployed?" without needing a login or the dashboard.
app.MapGet("/version", () => Results.Ok(new
{
    commit = Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA") ?? "unknown",
    branch = Environment.GetEnvironmentVariable("RAILWAY_GIT_BRANCH") ?? "unknown",
    deploymentId = Environment.GetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID") ?? "unknown",
    assemblyVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),

    // Stamped into the image at build time, so it is accurate even when the platform
    // exposes no git metadata at all.
    builtAtUtc = Environment.GetEnvironmentVariable("BUILD_TIMESTAMP") ?? "unknown",

    startedAtUtc = ProcessStart,

    // A cheap fingerprint of what this build can actually serve. If /api/operations 404s
    // but this says the controller is present, the problem is routing rather than a stale
    // image — and vice versa.
    hasOperationsApi = true,
    hasMailingApi = true
}));
app.MapHub<MinigamesHub>("/hubs/minigames").RequireRateLimiting("signalr-hub").RequireCors("AllowSpecificOrigin");
app.MapHub<MinigamesHub>("/api/hubs/minigames").RequireRateLimiting("signalr-hub").RequireCors("AllowSpecificOrigin");

// Both paths for the same reason as the minigames hub: the frontend's API base URL
// already carries /api on some deployments and not on others.
app.MapHub<NotificationsHub>("/hubs/notifications").RequireRateLimiting("signalr-hub").RequireCors("AllowSpecificOrigin");
app.MapHub<NotificationsHub>("/api/hubs/notifications").RequireRateLimiting("signalr-hub").RequireCors("AllowSpecificOrigin");

app.MapHub<ArenaHub>("/hubs/arena").RequireRateLimiting("signalr-hub").RequireCors("AllowSpecificOrigin");
app.MapHub<ArenaHub>("/api/hubs/arena").RequireRateLimiting("signalr-hub").RequireCors("AllowSpecificOrigin");
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
    // SeedUsersAsync and SeedMentorsAsync are deliberately NOT run.
    //
    // They downloaded two hand-maintained Dropbox CSVs on every boot and reconciled them
    // against the database. That coupling failed quietly: the mentors sheet referenced 22
    // scholar addresses with no matching account, so 22 scholars were left unmentored on
    // every single start and the only evidence was a log line nobody reads. The share
    // links also carry an expiring signature, so the whole thing was one rotation away
    // from silently seeding nothing.
    //
    // Both jobs now live in /admin/scholars, where a person sees what didn't match and can
    // fix it: intake creates accounts from an uploaded sheet, and mentor pairing reports
    // unmatched rows instead of logging them. The seeders below are local, idempotent and
    // network-free, which is why they still run.
    await RunSeederAsync(nameof(MailingSeedData.SeedAsync), MailingSeedData.SeedAsync);
}

app.Run();
