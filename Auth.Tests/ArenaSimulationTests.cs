using Auth.Models.Entities.Games;
using Auth.Services.Services.Games;

namespace Auth.Tests;

/// <summary>
/// Tests for the Comet Arena rules.
///
/// These matter more than most tests in this repo, because this simulation *is* the
/// leaderboard. A score is not validated after the fact — it is whatever this code
/// computed — so a bug here is not a wrong pixel, it is a wrong high score that stands
/// permanently and that nobody can prove wrong.
///
/// The simulation is a pure function over state precisely so this file can exist without
/// a hub, a socket or a database.
/// </summary>
public class ArenaSimulationTests
{
    private static ArenaState Running(ArenaMode mode = ArenaMode.Solo, uint seed = 12345)
    {
        var state = ArenaSimulation.CreateSession("test", mode, seed);
        ArenaSimulation.AddPlayer(state, "u1", "Player One");
        state.Phase = ArenaPhase.Running;
        state.Tick = 0;
        return state;
    }

    // ── The anti-cheat that actually matters ──────────────────────────────────

    [Fact]
    public void InputIsNormalised_SoAnOversizedVectorCannotMoveFaster()
    {
        // The cheapest cheat available: send (100, 100) instead of a unit vector and move
        // 140x faster than everyone else. The server must never use the client's magnitude.
        var honest = Running();
        var cheat = Running();

        ArenaSimulation.SetInput(honest, "u1", 1, 0, false);
        ArenaSimulation.SetInput(cheat, "u1", 1000, 0, false);

        for (var i = 0; i < 30; i++)
        {
            ArenaSimulation.Tick(honest);
            ArenaSimulation.Tick(cheat);
        }

        Assert.Equal(honest.Players[0].X, cheat.Players[0].X, 1);
    }

    [Fact]
    public void ZeroInputIsAccepted_AndDoesNotProduceNaN()
    {
        // Normalising by a zero length is the obvious division-by-zero, and a NaN position
        // propagates into every collision check for the rest of the match.
        var state = Running();

        ArenaSimulation.SetInput(state, "u1", 0, 0, false);
        for (var i = 0; i < 10; i++) ArenaSimulation.Tick(state);

        Assert.False(float.IsNaN(state.Players[0].X));
        Assert.False(float.IsNaN(state.Players[0].Y));
    }

    [Fact]
    public void InputIsIgnoredOutsideTheRunningPhase()
    {
        // Otherwise a client could steer during the countdown and be moving at full speed
        // the instant the match starts.
        var state = ArenaSimulation.CreateSession("test", ArenaMode.Solo, 1);
        ArenaSimulation.AddPlayer(state, "u1", "Player One");
        state.Phase = ArenaPhase.Countdown;

        ArenaSimulation.SetInput(state, "u1", 1, 0, false);

        Assert.Equal(0, state.Players[0].InputX);
    }

    [Fact]
    public void DashRespectsItsCooldown()
    {
        // Dash is gated server-side, or a client that decided when it dashed would dash
        // every single tick.
        var state = Running();

        ArenaSimulation.SetInput(state, "u1", 1, 0, dash: true);
        var afterFirst = state.Players[0].VelocityX;

        ArenaSimulation.SetInput(state, "u1", 1, 0, dash: true);

        Assert.Equal(afterFirst, state.Players[0].VelocityX);
        Assert.True(state.Players[0].DashCooldown > 0);
    }

    // ── Containment ───────────────────────────────────────────────────────────

    [Fact]
    public void APlayerCannotLeaveTheArena_EvenUnderSustainedInput()
    {
        var state = Running();
        ArenaSimulation.SetInput(state, "u1", 1, 1, dash: true);

        for (var i = 0; i < ArenaSimulation.TicksPerSecond * 10; i++) ArenaSimulation.Tick(state);

        var player = state.Players[0];
        var distance = MathF.Sqrt(player.X * player.X + player.Y * player.Y);

        Assert.True(distance <= ArenaSimulation.ArenaRadius,
            $"Player escaped the arena at radius {distance:F1}.");
    }

    [Fact]
    public void OrbsAlwaysSpawnInsideTheArena()
    {
        // An orb outside the rim is unreachable, and a player would chase it forever.
        var state = Running();

        for (var i = 0; i < 400; i++) ArenaSimulation.Tick(state);

        foreach (var orb in state.Orbs)
        {
            var distance = MathF.Sqrt(orb.X * orb.X + orb.Y * orb.Y);
            Assert.True(distance <= ArenaSimulation.ArenaRadius, $"Orb spawned at radius {distance:F1}.");
        }
    }

    [Fact]
    public void TheOrbCountIsConstant_SoTheArenaNeverEmptiesOrFloods()
    {
        var state = Running();
        Assert.Equal(ArenaSimulation.MaxOrbs, state.Orbs.Count);

        for (var i = 0; i < 600; i++) ArenaSimulation.Tick(state);

        Assert.Equal(ArenaSimulation.MaxOrbs, state.Orbs.Count);
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    [InlineData(100, ArenaSimulation.MaxComboMultiplier)]
    public void TheMultiplierStepsAndThenCaps(int combo, int expected)
    {
        // Capped because beyond 5x the numbers stop meaning anything, and an uncapped
        // multiplier makes one lucky run permanently unbeatable.
        Assert.Equal(expected, ArenaSimulation.Multiplier(combo));
    }

    [Fact]
    public void CollectingAnOrbScoresAndAdvancesTheCombo()
    {
        var state = Running();
        var player = state.Players[0];

        // Put an orb exactly on the player.
        state.Orbs.Clear();
        state.Orbs.Add(new ArenaOrb { Id = 1, X = player.X, Y = player.Y, Value = 10 });

        ArenaSimulation.Tick(state);

        Assert.Equal(10, player.Score);
        Assert.Equal(1, player.Combo);
        Assert.Equal(1, player.OrbsCollected);
    }

    [Fact]
    public void ACometHitResetsTheComboButNeverTakesPoints()
    {
        // Deliberate design: losing the multiplier costs you the next thirty seconds, which
        // is recoverable. Taking points away makes a bad run unrecoverable and people stop
        // playing.
        var state = Running();
        var player = state.Players[0];

        player.Score = 500;
        player.Combo = 12;

        state.Comets.Clear();
        state.Comets.Add(new ArenaComet
        {
            Id = 1, X = player.X, Y = player.Y,
            Radius = ArenaSimulation.CometRadius, VelocityX = 0, VelocityY = 0,
        });

        ArenaSimulation.Tick(state);

        Assert.Equal(0, player.Combo);
        Assert.Equal(500, player.Score);
        Assert.True(player.StunTicks > 0);
    }

    [Fact]
    public void AStunnedPlayerCannotCollect()
    {
        // Otherwise being hit while sitting on a cluster is a reward.
        var state = Running();
        var player = state.Players[0];
        player.StunTicks = ArenaSimulation.StunTicks;

        state.Orbs.Clear();
        state.Orbs.Add(new ArenaOrb { Id = 1, X = player.X, Y = player.Y, Value = 10 });

        ArenaSimulation.Tick(state);

        Assert.Equal(0, player.Score);
        Assert.Single(state.Orbs);
    }

    [Fact]
    public void BestComboIsRetainedAfterItIsBroken()
    {
        var state = Running();
        var player = state.Players[0];

        for (var i = 0; i < 5; i++)
        {
            state.Orbs.Clear();
            state.Orbs.Add(new ArenaOrb { Id = i, X = player.X, Y = player.Y, Value = 10 });
            ArenaSimulation.Tick(state);
        }

        var peak = player.BestCombo;
        Assert.True(peak >= 5);

        state.Comets.Add(new ArenaComet
        {
            Id = 99, X = player.X, Y = player.Y, Radius = ArenaSimulation.CometRadius,
        });
        ArenaSimulation.Tick(state);

        Assert.Equal(0, player.Combo);
        Assert.Equal(peak, player.BestCombo);
    }

    // ── Match lifecycle ───────────────────────────────────────────────────────

    [Fact]
    public void TheMatchFinishesExactlyAtTheTickLimit()
    {
        // The duration is what makes scores comparable. A match that ran long would produce
        // a high score nobody else could ever match.
        var state = Running();

        for (var i = 0; i < ArenaSimulation.MatchTicks; i++) ArenaSimulation.Tick(state);

        Assert.Equal(ArenaPhase.Finished, state.Phase);
    }

    [Fact]
    public void TickingAFinishedMatchChangesNothing()
    {
        var state = Running();
        for (var i = 0; i < ArenaSimulation.MatchTicks; i++) ArenaSimulation.Tick(state);

        var score = state.Players[0].Score;
        var tick = state.Tick;

        for (var i = 0; i < 50; i++) ArenaSimulation.Tick(state);

        Assert.Equal(score, state.Players[0].Score);
        Assert.Equal(tick, state.Tick);
    }

    [Fact]
    public void TheCountdownRunsItsFullLengthBeforePlayStarts()
    {
        var state = ArenaSimulation.CreateSession("test", ArenaMode.Solo, 7);
        ArenaSimulation.AddPlayer(state, "u1", "Player One");
        state.Phase = ArenaPhase.Countdown;

        for (var i = 0; i < ArenaSimulation.CountdownTicks - 1; i++) ArenaSimulation.Tick(state);
        Assert.Equal(ArenaPhase.Countdown, state.Phase);

        ArenaSimulation.Tick(state);
        Assert.Equal(ArenaPhase.Running, state.Phase);
        Assert.Equal(0, state.Tick);
    }

    [Fact]
    public void AddingTheSamePlayerTwiceIsIgnored()
    {
        // Reconnects call this path, and a duplicated player would score twice.
        var state = Running();
        ArenaSimulation.AddPlayer(state, "u1", "Player One");

        Assert.Single(state.Players);
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public void TheSameSeedAndInputsProduceTheSameMatch()
    {
        // This is what makes a contested score re-runnable rather than arguable. It is also
        // why the RNG is a seeded xorshift on the state rather than System.Random.
        static int Play(uint seed)
        {
            var state = ArenaSimulation.CreateSession("test", ArenaMode.Solo, seed);
            ArenaSimulation.AddPlayer(state, "u1", "Player One");
            state.Phase = ArenaPhase.Running;

            for (var i = 0; i < 900; i++)
            {
                // A deterministic input pattern, so the only variable is the seed.
                var angle = i * 0.05f;
                ArenaSimulation.SetInput(state, "u1", MathF.Cos(angle), MathF.Sin(angle), false);
                ArenaSimulation.Tick(state);
            }

            return state.Players[0].Score;
        }

        Assert.Equal(Play(999), Play(999));
    }

    [Fact]
    public void DifferentSeedsProduceDifferentMatches()
    {
        // Guards against the RNG being accidentally constant, which would make every match
        // identical and the leaderboard a typing test.
        var a = ArenaSimulation.CreateSession("a", ArenaMode.Solo, 1);
        var b = ArenaSimulation.CreateSession("b", ArenaMode.Solo, 2);

        Assert.NotEqual(
            a.Orbs.Select(o => (int)o.X).ToArray(),
            b.Orbs.Select(o => (int)o.X).ToArray());
    }
}
