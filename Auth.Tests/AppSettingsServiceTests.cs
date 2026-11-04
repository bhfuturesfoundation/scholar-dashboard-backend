using Auth.Models.Data;
using Auth.Models.DTOs.Settings;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Auth.Services.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Auth.Tests;

/// <summary>
/// Settings saved from Admin → App settings. What matters is that the rest of the app sees
/// the change through IConfiguration straight away, that a cleared value falls back to the
/// server's, and that nothing outside the short list can be written from a browser.
/// </summary>
public class AppSettingsServiceTests : IDisposable
{
    private const string AdminId = "admin-1";

    private readonly ApplicationDbContext _context = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"app-settings-{Guid.NewGuid()}")
        .Options);

    private readonly AppSettingsConfigurationProvider _provider = new();
    private readonly IConfiguration _configuration;

    public AppSettingsServiceTests()
    {
        // Mirrors Program.cs: environment first, saved settings last so they win.
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JOURNAL_WINDOW_CLOSE_DAY"] = "12",
                ["JWT_SECRET"] = "server-only"
            });
        builder.Add(_provider);
        _configuration = builder.Build();
    }

    public void Dispose() => _context.Dispose();

    private AppSettingsService Service() => new(_context, _provider, _configuration, Mock.Of<IAuditService>());

    private static UpdateAppSettingsRequest Set(string key, string? value) =>
        new() { Values = new Dictionary<string, string?> { [key] = value } };

    [Fact]
    public async Task Get_ReportsWhereEachValueComesFrom()
    {
        var settings = await Service().GetAllAsync();

        var closeDay = settings.Single(s => s.Key == "JOURNAL_WINDOW_CLOSE_DAY");
        Assert.Equal("12", closeDay.Value);
        Assert.Equal("server", closeDay.Source);

        var digestDay = settings.Single(s => s.Key == "DIGEST_DAY");
        Assert.Equal("Monday", digestDay.Value);
        Assert.Equal("default", digestDay.Source);
    }

    [Fact]
    public async Task Update_OverridesTheServerValueImmediately()
    {
        await Service().UpdateAsync(AdminId, Set("JOURNAL_WINDOW_CLOSE_DAY", "15"));

        Assert.Equal("15", _configuration["JOURNAL_WINDOW_CLOSE_DAY"]);
        Assert.Equal("app", (await Service().GetAllAsync()).Single(s => s.Key == "JOURNAL_WINDOW_CLOSE_DAY").Source);
    }

    [Fact]
    public async Task Update_WithEmptyValueFallsBackToTheServer()
    {
        await Service().UpdateAsync(AdminId, Set("JOURNAL_WINDOW_CLOSE_DAY", "15"));
        await Service().UpdateAsync(AdminId, Set("JOURNAL_WINDOW_CLOSE_DAY", ""));

        Assert.Equal("12", _configuration["JOURNAL_WINDOW_CLOSE_DAY"]);
        Assert.Empty(_context.AppSettings);
    }

    [Theory]
    [InlineData("JOURNAL_WINDOW_CLOSE_DAY", "31")]
    [InlineData("JOURNAL_REMINDER_DAYS", "5,abc")]
    [InlineData("DIGEST_HOUR_UTC", "24")]
    [InlineData("BACKUP_SCHEDULE_FORMAT", "Bacpac")]
    [InlineData("JOURNAL_ENFORCE_WINDOW", "maybe")]
    public async Task Update_RejectsInvalidValues(string key, string value)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateAsync(AdminId, Set(key, value)));
        Assert.Empty(_context.AppSettings);
    }

    [Fact]
    public async Task Update_RefusesKeysOutsideTheList()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateAsync(AdminId, Set("JWT_SECRET", "hijacked")));
        Assert.Equal("server-only", _configuration["JWT_SECRET"]);
    }

    [Fact]
    public async Task Update_StoresCanonicalValues()
    {
        await Service().UpdateAsync(AdminId, new UpdateAppSettingsRequest
        {
            Values = new Dictionary<string, string?>
            {
                ["DIGEST_DAY"] = "friday",
                ["JOURNAL_REMINDER_DAYS"] = " 7, 3 ,3,0 ",
                ["BACKUP_SCHEDULE_ENABLED"] = "False"
            }
        });

        Assert.Equal("Friday", _configuration["DIGEST_DAY"]);
        Assert.Equal("7,3,0", _configuration["JOURNAL_REMINDER_DAYS"]);
        Assert.Equal("false", _configuration["BACKUP_SCHEDULE_ENABLED"]);
    }
}
