using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Auth.Tests;

/// <summary>
/// Tests for immediate access-token revocation.
///
/// The bug these exist for: "sign out everywhere" revoked refresh tokens, which stops
/// renewal but does nothing to the access token already sitting in a browser on a shared
/// computer. That token kept working until it expired on its own — so the button did not
/// mean what it said.
/// </summary>
public class TokenVersionTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tokenversion-{Guid.NewGuid()}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static TokenVersionCache NewCache(ApplicationDbContext context, IMemoryCache? cache = null) =>
        new(context,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().AddInMemoryCollection().Build());

    private static async Task<ApplicationDbContext> WithUser(string id, int version)
    {
        var context = NewContext();
        context.Users.Add(new User { Id = id, Email = $"{id}@example.com", TokenVersion = version });
        await context.SaveChangesAsync();
        return context;
    }

    [Fact]
    public async Task TokenMintedAtTheCurrentVersion_IsAccepted()
    {
        await using var context = await WithUser("u1", version: 0);
        var cache = NewCache(context);

        Assert.True(await cache.IsTokenCurrentAsync("u1", 0));
    }

    [Fact]
    public async Task TokenMintedBeforeARevocation_IsRejected()
    {
        // The headline case: the account signed out everywhere, so a token carrying the
        // old generation must stop working immediately rather than at its own expiry.
        await using var context = await WithUser("u1", version: 1);
        var cache = NewCache(context);

        Assert.False(await cache.IsTokenCurrentAsync("u1", 0));
    }

    [Fact]
    public async Task TokenMintedAtAHigherVersion_IsAccepted()
    {
        // Another instance bumped the counter and issued a fresh token before this
        // instance's cache caught up. Rejecting it would sign somebody out at the exact
        // moment they signed in.
        await using var context = await WithUser("u1", version: 1);
        var cache = NewCache(context);

        Assert.True(await cache.IsTokenCurrentAsync("u1", 2));
    }

    [Fact]
    public async Task TokenWithNoVersionClaim_IsTreatedAsZero_AndStillWorks()
    {
        // Tokens issued before this mechanism shipped carry no "tv" claim. They parse to 0,
        // which matches every existing account, so nobody is signed out by the deploy.
        await using var context = await WithUser("legacy", version: 0);
        var cache = NewCache(context);

        Assert.True(await cache.IsTokenCurrentAsync("legacy", 0));
    }

    [Fact]
    public async Task UnknownUser_ReadsAsVersionZero_RatherThanThrowing()
    {
        // A deleted account's token should fail authorisation elsewhere, not blow up here.
        await using var context = NewContext();
        var cache = NewCache(context);

        Assert.Equal(0, await cache.GetCurrentVersionAsync("does-not-exist"));
    }

    [Fact]
    public async Task TheVersionIsCached_SoRepeatedChecksDoNotReReadTheDatabase()
    {
        await using var context = await WithUser("u1", version: 0);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = NewCache(context, memory);

        await cache.GetCurrentVersionAsync("u1");

        // Change the row behind the cache's back. A cached read must not see it.
        var user = await context.Users.FirstAsync(u => u.Id == "u1");
        user.TokenVersion = 5;
        await context.SaveChangesAsync();

        Assert.Equal(0, await cache.GetCurrentVersionAsync("u1"));
    }

    [Fact]
    public async Task InvalidateForcesAReRead_SoRevocationTakesEffectAtOnce()
    {
        // This is what makes the button immediate on the instance that handled it, rather
        // than waiting out the cache TTL.
        await using var context = await WithUser("u1", version: 0);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = NewCache(context, memory);

        await cache.GetCurrentVersionAsync("u1");

        var user = await context.Users.FirstAsync(u => u.Id == "u1");
        user.TokenVersion = 1;
        await context.SaveChangesAsync();

        cache.Invalidate("u1");

        Assert.Equal(1, await cache.GetCurrentVersionAsync("u1"));
        Assert.False(await cache.IsTokenCurrentAsync("u1", 0));
    }
}

/// <summary>
/// Pins the two production faults that shipped with the settings screen, so neither can
/// come back quietly.
/// </summary>
public class AccountOverviewRegressionTests
{
    [Fact]
    public void IdentityUserLogin_IsNotPartOfTheModel()
    {
        // This is why AccountService must never call UserManager.GetLoginsAsync: the
        // context calls Ignore<IdentityUserLogin<string>>(), so that call queries an
        // unmapped entity and throws — which is what turned GET /api/auth/me into a 500.
        //
        // If someone later maps it, this test fails and points at the comment in
        // AccountService that will then be out of date.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"model-{Guid.NewGuid()}")
            .Options;

        using var context = new ApplicationDbContext(options);

        var mapped = context.Model
            .GetEntityTypes()
            .Any(e => e.ClrType.Name.StartsWith("IdentityUserLogin", StringComparison.Ordinal));

        Assert.False(mapped,
            "IdentityUserLogin is mapped again — AccountService.GetOverviewAsync can now use " +
            "GetLoginsAsync, and its comment about external logins is stale.");
    }
}
