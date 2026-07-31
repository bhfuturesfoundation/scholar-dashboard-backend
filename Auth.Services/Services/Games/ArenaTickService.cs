using Auth.Services.Interfaces.Games;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Games
{
    /// <summary>
    /// Drives every live match at a fixed 30 Hz.
    ///
    /// One loop for all sessions rather than a timer per match: at four players a match the
    /// per-tick work is trivial, and a timer per session would mean dozens of thread-pool
    /// wake-ups a second competing with the request pipeline for no benefit.
    ///
    /// <see cref="PeriodicTimer"/> rather than Task.Delay in a loop, because Delay drifts —
    /// it waits *at least* the interval and the error accumulates, so a ninety-second match
    /// would quietly run long. PeriodicTimer schedules against a fixed period instead.
    /// </summary>
    public class ArenaTickService : BackgroundService
    {
        private static readonly TimeSpan TickInterval =
            TimeSpan.FromSeconds(ArenaSimulation.TickSeconds);

        private readonly IArenaService _arena;
        private readonly ILogger<ArenaTickService> _logger;

        public ArenaTickService(IArenaService arena, ILogger<ArenaTickService> logger)
        {
            _arena = arena;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Arena tick loop running at {Hz} Hz.", ArenaSimulation.TicksPerSecond);

            using var timer = new PeriodicTimer(TickInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _arena.TickAllAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A BackgroundService that throws is torn down permanently and silently.
                    // One bad tick must not end every match on the server.
                    _logger.LogError(ex, "Arena tick failed. Continuing.");
                }
            }
        }
    }
}
