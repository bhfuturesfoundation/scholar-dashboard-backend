using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// What the server remembers about a puzzle in progress — by handing it to the client and
    /// signing it, rather than by storing it.
    ///
    /// ── Why there is no session table ────────────────────────────────────────
    ///
    /// The obvious design keeps a dictionary of live puzzles server-side, as Comet Arena does.
    /// It is the right shape there, because an arena match is a continuous simulation that has
    /// to advance thirty times a second whether or not anyone sends input.
    ///
    /// A Sudoku grid does not advance on its own. The only things the server needs at scoring
    /// time are the seed the puzzle was generated from and the moment it was handed out — a few
    /// dozen bytes that never change. Signing them and letting the client carry them costs one
    /// HMAC and buys three things a dictionary cannot: puzzles survive a deploy mid-game (Railway
    /// restarts on every push, and a lost ten-minute Sudoku is a genuinely annoying way to find
    /// that out), nothing has to be evicted on a timer, and it works unchanged across instances.
    ///
    /// This is the same trade JWTs make against server-side sessions, for the same reason: the
    /// state is small, short-lived, and read far more often than it is written.
    /// </summary>
    public sealed record PuzzleTicket
    {
        public required string UserId { get; init; }
        public required string GameId { get; init; }
        public required uint Seed { get; init; }
        public required int Difficulty { get; init; }
        public required long DealtAtUnixMs { get; init; }

        /// <summary>Distinguishes two deals that share a millisecond, so replays stay unique.</summary>
        public required string Nonce { get; init; }
    }

    /// <summary>
    /// Signs and verifies <see cref="PuzzleTicket"/>s.
    ///
    /// The signature is what makes replay verification trustworthy. Without it the client could
    /// simply claim a different seed — one whose board it had already solved offline — or move
    /// the deal time backwards to fake a fast finish. With it, the two numbers the score depends
    /// on are both fixed by the server before play starts, and the client cannot touch either
    /// without invalidating the ticket.
    /// </summary>
    public sealed class PuzzleTicketSigner
    {
        private readonly byte[] _key;

        /// <summary>
        /// A puzzle nobody finishes within this window is not scoreable.
        ///
        /// This is a bound on how long a *ticket* stays valid, not a time limit on the game.
        /// Without it a signed ticket is a permanent licence to submit a score for that board:
        /// deal a puzzle, solve it at leisure with a solver, submit it a week later against a
        /// leaderboard you have had all week to study.
        /// </summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

        public PuzzleTicketSigner(string secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
                throw new ArgumentException("A signing secret is required.", nameof(secret));

            // Derived rather than used directly, so this key and the one signing access tokens
            // are different bytes even though they come from the same configured secret. Reusing
            // one key across two protocols is how a forgery in one becomes a forgery in the other.
            _key = SHA256.HashData(Encoding.UTF8.GetBytes($"puzzle-ticket-v1|{secret}"));
        }

        public string Sign(PuzzleTicket ticket)
        {
            var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(ticket));
            return $"{payload}.{Base64Url(Mac(payload))}";
        }

        /// <summary>
        /// Returns the ticket only when the signature is intact, it belongs to this caller, and
        /// it is still inside <see cref="MaxAge"/>. Anything else returns null — the caller has
        /// no use for the difference, and reporting it back would tell a forger which of their
        /// guesses was closest.
        /// </summary>
        public PuzzleTicket? Verify(string? token, string expectedUserId)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var separator = token.IndexOf('.');
            if (separator <= 0 || separator == token.Length - 1) return null;

            var payload = token[..separator];
            var signature = token[(separator + 1)..];

            byte[] provided;
            try
            {
                provided = FromBase64Url(signature);
            }
            catch (FormatException)
            {
                return null;
            }

            // Fixed-time compare. A byte-by-byte early return leaks how much of a guessed
            // signature was correct, which is enough to reconstruct one byte at a time.
            if (!CryptographicOperations.FixedTimeEquals(Mac(payload), provided)) return null;

            PuzzleTicket? ticket;
            try
            {
                ticket = JsonSerializer.Deserialize<PuzzleTicket>(FromBase64Url(payload));
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return null;
            }

            if (ticket is null) return null;

            // A valid signature only proves the server issued this ticket, not that it issued it
            // to whoever is presenting it. Without this check one player could hand another a
            // ticket for a board they had already solved.
            if (!string.Equals(ticket.UserId, expectedUserId, StringComparison.Ordinal)) return null;

            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(ticket.DealtAtUnixMs);
            if (age < TimeSpan.FromSeconds(-30) || age > MaxAge) return null;

            return ticket;
        }

        private byte[] Mac(string payload) =>
            HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
        }
    }
}
