using Auth.Models.Data;
using Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Auth.Services.Services
{
    /// <summary>
    /// Answers "is this access token still current?" on every authenticated request.
    ///
    /// The naive implementations are both bad. Reading <c>Users.TokenVersion</c> from the
    /// database per request doubles the round trips on every call in the app for the sake of
    /// an action almost nobody performs. Keeping a denylist of revoked JWT ids means holding
    /// state proportional to traffic and checking it on the same hot path.
    ///
    /// This is the middle: an in-memory cache keyed by user id, so the steady-state cost is
    /// a dictionary lookup, and the database is consulted at most once per user per
    /// <see cref="Ttl"/>. Revocation evicts the entry directly, so on a single instance it
    /// takes effect on the very next request.
    ///
    /// KNOWN LIMIT, and the reason the TTL is short: with more than one instance running,
    /// only the instance that performed the revocation evicts its own copy. Others keep
    /// serving a stale version until their entry expires — up to <see cref="Ttl"/>. That is
    /// a bounded window measured in seconds rather than the access-token lifetime, which is
    /// what it replaced. Making it exact needs the Redis backplane to publish evictions;
    /// worth doing if this ever runs multi-instance.
    /// </summary>
    public class TokenVersionCache : ITokenVersionCache
    {
        private static TimeSpan DefaultTtl => TimeSpan.FromSeconds(60);

        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;

        public TokenVersionCache(
            ApplicationDbContext context,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _context = context;
            _cache = cache;
            _configuration = configuration;
        }

        private TimeSpan Ttl =>
            int.TryParse(_configuration["TOKEN_VERSION_CACHE_SECONDS"], out var seconds) && seconds is > 0 and <= 3600
                ? TimeSpan.FromSeconds(seconds)
                : DefaultTtl;

        private static string Key(string userId) => $"tokenversion:{userId}";

        public async Task<int> GetCurrentVersionAsync(string userId, CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue<int>(Key(userId), out var cached)) return cached;

            var version = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.TokenVersion)
                .FirstOrDefaultAsync(cancellationToken);

            _cache.Set(Key(userId), version, Ttl);
            return version;
        }

        public void Invalidate(string userId) => _cache.Remove(Key(userId));

        public async Task<bool> IsTokenCurrentAsync(
            string userId, int tokenVersion, CancellationToken cancellationToken = default)
        {
            var current = await GetCurrentVersionAsync(userId, cancellationToken);

            // Strictly less-than, not inequality. A token carrying a *higher* version than we
            // have cached is one minted by another instance that has already bumped the
            // counter — rejecting it would sign somebody out immediately after they signed in.
            return tokenVersion >= current;
        }
    }
}
