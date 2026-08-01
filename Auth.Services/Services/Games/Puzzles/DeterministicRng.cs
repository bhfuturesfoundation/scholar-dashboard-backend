namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// A xorshift32 generator, used because it is reproducible forever.
    ///
    /// Every puzzle in this folder is verified by replay: the server hands out a seed, the
    /// client plays offline, and the server later re-runs the same move sequence from the same
    /// seed and checks it lands on the same board. That only works if "the same seed" produces
    /// the same numbers on every machine, in every process, for the lifetime of the leaderboard.
    ///
    /// <see cref="System.Random"/> cannot promise that. Its algorithm is explicitly an
    /// implementation detail and has already changed once between framework versions — so a
    /// routine .NET upgrade would silently invalidate every score ever recorded, and the failure
    /// would look like players cheating rather than like a runtime change. Fourteen lines of
    /// arithmetic we own is the cheaper side of that trade.
    ///
    /// This is not a cryptographic generator and does not need to be. The seed is what must be
    /// unguessable, and that comes from <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
    /// at deal time; the sequence itself only has to be uniform and stable.
    /// </summary>
    public struct DeterministicRng
    {
        private uint _state;

        public DeterministicRng(uint seed)
        {
            // Zero is a fixed point of xorshift — it would emit nothing but zeroes forever.
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        /// <summary>Uniform in [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => NextUInt() / 4294967296.0;

        /// <summary>
        /// Fisher-Yates, in place.
        ///
        /// Written out rather than composed from LINQ's OrderBy(random) — that idiom is not a
        /// shuffle, it is a sort with an inconsistent comparator, and its distribution is biased.
        /// Here the bias would be visible: it is what places digits in generated Sudoku grids.
        /// </summary>
        public void Shuffle<T>(IList<T> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
