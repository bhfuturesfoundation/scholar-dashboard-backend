using Auth.Models.Entities.Games;

namespace Auth.Services.Services.Games
{
    /// <summary>
    /// The Comet Arena rules, as a pure function over <see cref="ArenaState"/>.
    ///
    /// No SignalR, no database, no clock — <see cref="Tick"/> takes a state and advances it
    /// by exactly one fixed step. That is what lets the whole game be unit-tested: a match
    /// is just a loop over this, and a disputed score can be re-run from its seed.
    ///
    /// THE GAME
    /// --------
    /// A circular arena. Orbs spawn and are worth more the closer to the edge they are.
    /// Comets sweep across from the rim. Collecting orbs without being hit builds a combo
    /// multiplier; a comet hit resets it and scatters some of what you were carrying.
    ///
    /// The combo is the whole design. Without it the game is a collection-rate contest that
    /// tops out at how fast you can move, and every good player converges on the same score.
    /// With it, the interesting decision is whether to go for the high-value orb near the
    /// rim while carrying a 5× multiplier you would lose — which is a decision, and
    /// decisions are what make a high score worth chasing.
    /// </summary>
    public static class ArenaSimulation
    {
        public const float TickSeconds = 1f / 30f;
        public const int TicksPerSecond = 30;

        /// <summary>Ninety seconds. Long enough to build a combo, short enough to replay.</summary>
        public const int MatchTicks = 90 * TicksPerSecond;

        public const int CountdownTicks = 3 * TicksPerSecond;

        public const float ArenaRadius = 500f;
        public const float PlayerRadius = 18f;
        public const float OrbRadius = 12f;

        /// <summary>Pixels per second at full input.</summary>
        public const float PlayerSpeed = 300f;

        /// <summary>
        /// How quickly a player reaches full speed. Not instant — instant acceleration
        /// removes momentum from the game, and momentum is what makes dodging feel like a
        /// skill rather than a reaction test.
        /// </summary>
        public const float Acceleration = 14f;

        public const float Friction = 0.86f;

        public const int MaxOrbs = 14;
        public const int BaseOrbValue = 10;

        /// <summary>Combo multiplier caps here. Beyond 5× the numbers stop meaning anything.</summary>
        public const int MaxComboMultiplier = 5;

        /// <summary>Orbs per step of the multiplier.</summary>
        public const int ComboStep = 4;

        public const int StunTicks = TicksPerSecond;          // one second
        public const int DashCooldownTicks = 2 * TicksPerSecond;
        public const float DashImpulse = 620f;

        public const float CometRadius = 24f;
        public const float CometMinSpeed = 260f;
        public const float CometMaxSpeed = 430f;

        /// <summary>
        /// Comets get more frequent as the match runs on, so the last twenty seconds are
        /// where scores are actually decided rather than the whole match being flat.
        /// </summary>
        public const int CometSpawnStartInterval = 45;
        public const int CometSpawnEndInterval = 14;

        private static int _nextEntityId;

        // ── Setup ─────────────────────────────────────────────────────────────

        public static ArenaState CreateSession(string sessionId, ArenaMode mode, uint seed)
        {
            var state = new ArenaState
            {
                SessionId = sessionId,
                Mode = mode,
                Phase = ArenaPhase.Lobby,
                RandomState = seed == 0 ? 1u : seed,
                StartedAtUtc = DateTime.UtcNow,
            };

            for (var i = 0; i < MaxOrbs; i++) SpawnOrb(state);
            return state;
        }

        public static void AddPlayer(ArenaState state, string userId, string displayName)
        {
            if (state.Players.Any(p => p.UserId == userId)) return;

            // Spawned on a ring around the centre, evenly spaced, so nobody starts on top of
            // a comet or inside another player.
            var index = state.Players.Count;
            var angle = index * (MathF.PI * 2f / 4f);

            state.Players.Add(new ArenaPlayer
            {
                UserId = userId,
                DisplayName = displayName,
                ColorIndex = index % 4,
                X = MathF.Cos(angle) * 120f,
                Y = MathF.Sin(angle) * 120f,
            });
        }

        /// <summary>
        /// Records a player's input.
        ///
        /// The vector is normalised here, on the server, and that is not a formality: a
        /// client sending (100, 100) rather than a unit vector would move 140 times faster
        /// than everyone else. It is the cheapest cheat available and the only defence is
        /// to never use the client's magnitude.
        /// </summary>
        public static void SetInput(ArenaState state, string userId, float x, float y, bool dash)
        {
            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player is null || state.Phase != ArenaPhase.Running) return;

            var length = MathF.Sqrt(x * x + y * y);

            if (length > 0.001f)
            {
                player.InputX = x / length;
                player.InputY = y / length;
            }
            else
            {
                player.InputX = 0;
                player.InputY = 0;
            }

            // Dash is server-gated on the cooldown for the same reason: a client that could
            // decide when it dashed would dash every tick.
            if (dash && player.DashCooldown <= 0 && player.StunTicks <= 0)
            {
                player.VelocityX += player.InputX * DashImpulse;
                player.VelocityY += player.InputY * DashImpulse;
                player.DashCooldown = DashCooldownTicks;
            }
        }

        // ── The tick ──────────────────────────────────────────────────────────

        public static void Tick(ArenaState state)
        {
            switch (state.Phase)
            {
                case ArenaPhase.Countdown:
                    state.Tick++;
                    if (state.Tick >= CountdownTicks)
                    {
                        state.Phase = ArenaPhase.Running;
                        state.Tick = 0;
                    }
                    return;

                case ArenaPhase.Running:
                    break;

                default:
                    return;
            }

            state.Tick++;

            MovePlayers(state);
            MoveComets(state);
            SpawnComets(state);
            ResolveOrbs(state);
            ResolveCometHits(state);

            if (state.Mode == ArenaMode.Versus) ResolvePlayerCollisions(state);

            if (state.Tick >= MatchTicks)
            {
                state.Phase = ArenaPhase.Finished;
                state.FinishedAtUtc = DateTime.UtcNow;
            }
        }

        private static void MovePlayers(ArenaState state)
        {
            foreach (var player in state.Players)
            {
                if (player.DashCooldown > 0) player.DashCooldown--;

                if (player.StunTicks > 0)
                {
                    player.StunTicks--;

                    // Still slides while stunned, so a hit throws you rather than nailing
                    // you to the spot — being frozen mid-arena feels like a bug.
                    player.VelocityX *= Friction;
                    player.VelocityY *= Friction;
                }
                else
                {
                    player.VelocityX += player.InputX * PlayerSpeed * Acceleration * TickSeconds;
                    player.VelocityY += player.InputY * PlayerSpeed * Acceleration * TickSeconds;

                    player.VelocityX *= Friction;
                    player.VelocityY *= Friction;
                }

                player.X += player.VelocityX * TickSeconds;
                player.Y += player.VelocityY * TickSeconds;

                // The rim is solid and slightly springy, so being pushed into it in versus
                // costs you position rather than ending the round.
                var distance = MathF.Sqrt(player.X * player.X + player.Y * player.Y);
                var limit = ArenaRadius - PlayerRadius;

                if (distance > limit && distance > 0)
                {
                    player.X = player.X / distance * limit;
                    player.Y = player.Y / distance * limit;
                    player.VelocityX *= -0.35f;
                    player.VelocityY *= -0.35f;
                }
            }
        }

        private static void MoveComets(ArenaState state)
        {
            for (var i = state.Comets.Count - 1; i >= 0; i--)
            {
                var comet = state.Comets[i];
                comet.X += comet.VelocityX * TickSeconds;
                comet.Y += comet.VelocityY * TickSeconds;

                // Culled generously outside the rim rather than exactly at it, so a comet
                // that clips the edge does not vanish in view.
                var distance = MathF.Sqrt(comet.X * comet.X + comet.Y * comet.Y);
                if (distance > ArenaRadius * 1.6f) state.Comets.RemoveAt(i);
            }
        }

        private static void SpawnComets(ArenaState state)
        {
            // Linear ramp from the opening interval to the closing one.
            var progress = (float)state.Tick / MatchTicks;
            var interval = (int)(CometSpawnStartInterval +
                (CometSpawnEndInterval - CometSpawnStartInterval) * progress);

            if (interval < 1) interval = 1;
            if (state.Tick % interval != 0) return;

            // Enters at a random point on the rim and aims at a random point near the
            // centre, so comets sweep through the play area rather than skimming the edge.
            var entryAngle = NextFloat(state) * MathF.PI * 2f;
            var x = MathF.Cos(entryAngle) * ArenaRadius * 1.2f;
            var y = MathF.Sin(entryAngle) * ArenaRadius * 1.2f;

            var targetAngle = NextFloat(state) * MathF.PI * 2f;
            var targetRadius = NextFloat(state) * ArenaRadius * 0.55f;
            var tx = MathF.Cos(targetAngle) * targetRadius;
            var ty = MathF.Sin(targetAngle) * targetRadius;

            var dx = tx - x;
            var dy = ty - y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 0.001f) return;

            var speed = CometMinSpeed + NextFloat(state) * (CometMaxSpeed - CometMinSpeed);

            state.Comets.Add(new ArenaComet
            {
                Id = NextId(),
                X = x,
                Y = y,
                VelocityX = dx / length * speed,
                VelocityY = dy / length * speed,
                Radius = CometRadius,
            });
        }

        private static void ResolveOrbs(ArenaState state)
        {
            foreach (var player in state.Players)
            {
                if (player.StunTicks > 0) continue;

                for (var i = state.Orbs.Count - 1; i >= 0; i--)
                {
                    var orb = state.Orbs[i];

                    if (!Overlaps(player.X, player.Y, PlayerRadius, orb.X, orb.Y, OrbRadius)) continue;

                    player.Combo++;
                    if (player.Combo > player.BestCombo) player.BestCombo = player.Combo;
                    player.OrbsCollected++;

                    player.Score += orb.Value * Multiplier(player.Combo);

                    state.Orbs.RemoveAt(i);
                    SpawnOrb(state);
                }
            }
        }

        /// <summary>1× up to the first step, then one more per <see cref="ComboStep"/> orbs.</summary>
        public static int Multiplier(int combo) =>
            Math.Clamp(1 + combo / ComboStep, 1, MaxComboMultiplier);

        private static void ResolveCometHits(ArenaState state)
        {
            foreach (var player in state.Players)
            {
                if (player.StunTicks > 0) continue;

                foreach (var comet in state.Comets)
                {
                    if (!Overlaps(player.X, player.Y, PlayerRadius, comet.X, comet.Y, comet.Radius)) continue;

                    player.StunTicks = StunTicks;
                    player.CometHits++;

                    // The combo is the punishment, not the points. Taking score away feels
                    // arbitrary and makes a bad run unrecoverable; losing the multiplier
                    // costs you the *next* thirty seconds, which is the interesting loss.
                    player.Combo = 0;

                    // Knocked along the comet's path, so a hit visibly comes from somewhere.
                    player.VelocityX += comet.VelocityX * 0.55f;
                    player.VelocityY += comet.VelocityY * 0.55f;
                    break;
                }
            }
        }

        /// <summary>
        /// Versus only: players shove each other.
        ///
        /// Elastic separation rather than damage — knocking a rival off a high-value orb at
        /// the rim is a much better interaction than removing their points, because it is
        /// contestable rather than punitive.
        /// </summary>
        private static void ResolvePlayerCollisions(ArenaState state)
        {
            for (var i = 0; i < state.Players.Count; i++)
            {
                for (var j = i + 1; j < state.Players.Count; j++)
                {
                    var a = state.Players[i];
                    var b = state.Players[j];

                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);

                    if (distance >= PlayerRadius * 2 || distance < 0.001f) continue;

                    var nx = dx / distance;
                    var ny = dy / distance;
                    var overlap = PlayerRadius * 2 - distance;

                    a.X -= nx * overlap * 0.5f;
                    a.Y -= ny * overlap * 0.5f;
                    b.X += nx * overlap * 0.5f;
                    b.Y += ny * overlap * 0.5f;

                    var push = 180f;
                    a.VelocityX -= nx * push;
                    a.VelocityY -= ny * push;
                    b.VelocityX += nx * push;
                    b.VelocityY += ny * push;
                }
            }
        }

        private static void SpawnOrb(ArenaState state)
        {
            // Biased toward the rim by squaring the random radius, then valued by how far
            // out it landed — the risk and the reward come from the same number.
            var angle = NextFloat(state) * MathF.PI * 2f;
            var t = NextFloat(state);
            var radius = MathF.Sqrt(t) * (ArenaRadius - 60f);

            var normalised = radius / (ArenaRadius - 60f);

            state.Orbs.Add(new ArenaOrb
            {
                Id = NextId(),
                X = MathF.Cos(angle) * radius,
                Y = MathF.Sin(angle) * radius,
                Value = BaseOrbValue + (int)(normalised * 20f),
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool Overlaps(float ax, float ay, float ar, float bx, float by, float br)
        {
            var dx = ax - bx;
            var dy = ay - by;
            var radii = ar + br;

            // Squared comparison: a square root per pair per tick is the one obviously
            // wasteful thing in a loop this hot.
            return dx * dx + dy * dy <= radii * radii;
        }

        /// <summary>
        /// xorshift32 on the state's own cursor.
        ///
        /// Deliberately not System.Random: the whole point is that a match is reproducible
        /// from its seed, so a contested score can be re-run rather than argued about.
        /// </summary>
        private static float NextFloat(ArenaState state)
        {
            var x = state.RandomState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state.RandomState = x;

            return (x & 0xFFFFFF) / (float)0x1000000;
        }

        private static int NextId() => Interlocked.Increment(ref _nextEntityId);
    }
}
