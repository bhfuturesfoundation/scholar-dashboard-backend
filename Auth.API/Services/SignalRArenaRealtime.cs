using Auth.API.Hubs;
using Auth.Services.Interfaces.Games;
using Microsoft.AspNetCore.SignalR;

namespace Auth.API.Services
{
    /// <summary>
    /// Bridges the arena simulation onto SignalR.
    ///
    /// Never throws. A dropped snapshot costs one frame of smoothness — the next one is
    /// 66 ms away and the client interpolates across it — whereas an exception here would
    /// propagate into the tick loop and stop every match on the server.
    /// </summary>
    public class SignalRArenaRealtime : IArenaRealtime
    {
        private readonly IHubContext<ArenaHub> _hub;
        private readonly ILogger<SignalRArenaRealtime> _logger;

        public SignalRArenaRealtime(IHubContext<ArenaHub> hub, ILogger<SignalRArenaRealtime> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        public async Task SendSnapshotAsync(string sessionId, object snapshot, CancellationToken cancellationToken = default)
        {
            try
            {
                await _hub.Clients.Group(sessionId).SendAsync("Snapshot", snapshot, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Snapshot to {Session} failed.", sessionId);
            }
        }

        public async Task SendFinishedAsync(string sessionId, object result, CancellationToken cancellationToken = default)
        {
            try
            {
                await _hub.Clients.Group(sessionId).SendAsync("Finished", result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Final result for {Session} was not delivered.", sessionId);
            }
        }
    }
}
