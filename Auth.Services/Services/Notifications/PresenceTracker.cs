using System.Collections.Concurrent;
using Auth.Services.Interfaces.Notifications;

namespace Auth.Services.Services.Notifications
{
    /// <inheritdoc cref="IPresenceTracker"/>
    public class PresenceTracker : IPresenceTracker
    {
        /// <summary>
        /// One entry per user, holding every connection that user currently has.
        ///
        /// A set of connection ids rather than a counter: a counter drifts permanently if a
        /// disconnect is ever missed or delivered twice, and the failure mode is a ghost who
        /// appears online forever. A set is idempotent — removing an id that is not there
        /// costs nothing and changes nothing.
        /// </summary>
        private sealed class Entry
        {
            public required PresenceUser User { get; set; }
            public HashSet<string> Connections { get; } = new(StringComparer.Ordinal);
        }

        private readonly ConcurrentDictionary<string, Entry> _online = new(StringComparer.Ordinal);

        public int OnlineCount => _online.Count;

        public bool IsOnline(string userId) => _online.ContainsKey(userId);

        public bool Connect(string userId, string connectionId, PresenceUser user)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(connectionId)) return false;

            var isFirst = false;

            _online.AddOrUpdate(
                userId,
                _ =>
                {
                    isFirst = true;
                    var entry = new Entry { User = user };
                    entry.Connections.Add(connectionId);
                    return entry;
                },
                (_, existing) =>
                {
                    // Locked because AddOrUpdate's update delegate can run concurrently for
                    // the same key, and HashSet is not thread-safe. The dictionary protects
                    // the mapping, not the value.
                    lock (existing.Connections)
                    {
                        isFirst = existing.Connections.Count == 0;
                        existing.Connections.Add(connectionId);

                        // Refresh the display data: the name may have changed since the
                        // first tab connected.
                        existing.User = user with { ConnectedAtUtc = existing.User.ConnectedAtUtc };
                    }

                    return existing;
                });

            return isFirst;
        }

        public bool Disconnect(string userId, string connectionId)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (!_online.TryGetValue(userId, out var entry)) return false;

            bool wasLast;

            lock (entry.Connections)
            {
                entry.Connections.Remove(connectionId);
                wasLast = entry.Connections.Count == 0;
            }

            if (!wasLast) return false;

            // Re-checked under the lock before removing, so a connection arriving between
            // the check and the removal is not silently dropped — which would show somebody
            // as offline while they still have a live socket.
            lock (entry.Connections)
            {
                if (entry.Connections.Count > 0) return false;
                _online.TryRemove(userId, out _);
            }

            return true;
        }

        public IReadOnlyList<PresenceUser> GetOnline() =>
            _online.Values
                .Select(e => e.User)
                .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }
}
