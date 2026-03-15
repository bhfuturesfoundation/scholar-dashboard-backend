using Auth.API.Extensions;
using Auth.API.Middleware;
using Auth.API.Hubs;
using Auth.API.Seed;
using Auth.Models.Data;
using Auth.Services.Interfaces;
using Auth.Services.Services;
using DotNetEnv;
using Mapster;
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

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowSpecificOrigin");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<MinigamesHub>("/hubs/minigames");
app.MapHub<MinigamesHub>("/api/hubs/minigames");
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();

    // Check if database has any tables
    var hasTables = conn.GetSchema("Tables").Rows.Count > 0;

    // Check for pending migrations
    var pendingMigrations = await db.Database.GetPendingMigrationsAsync();

    if (!hasTables)
    {
        // DB empty ? create schema
        await db.Database.EnsureCreatedAsync();
        Console.WriteLine("Database was empty. Created new schema.");
    }
    else if (pendingMigrations.Any())
    {
        // Apply any pending migrations
        await db.Database.MigrateAsync();
        Console.WriteLine("Applied pending migrations.");
    }
    else
    {
        Console.WriteLine("Database is up-to-date. No actions required.");
    }

    await conn.CloseAsync();
}


// ? Seed data after migrations
await SeedData.SeedRolesAsync(app.Services.CreateScope().ServiceProvider);
await SeedData.SeedQuestionsAsync(app.Services.CreateScope().ServiceProvider);
await SeedData.SeedUsersAsync(app.Services.CreateScope().ServiceProvider);
await SeedData.SeedMentorsAsync(app.Services.CreateScope().ServiceProvider);

app.Run();


