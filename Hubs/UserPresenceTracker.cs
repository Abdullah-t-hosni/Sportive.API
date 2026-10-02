using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Sportive.API.Hubs;

public static class UserPresenceTracker
{
    // Mapping of UserId -> active SignalR ConnectionIds
    private static readonly ConcurrentDictionary<string, HashSet<string>> _userConnections = new();

    public static bool UserConnected(string userId, string connectionId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        var connections = _userConnections.GetOrAdd(userId, _ => new HashSet<string>());
        lock (connections)
        {
            var wasOffline = connections.Count == 0;
            connections.Add(connectionId);
            return wasOffline;
        }
    }

    public static bool UserDisconnected(string userId, string connectionId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        if (_userConnections.TryGetValue(userId, out var connections))
        {
            lock (connections)
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    _userConnections.TryRemove(userId, out _);
                    return true; // was online, now completely offline
                }
            }
        }
        return false;
    }

    public static List<string> GetOnlineUsers()
    {
        return _userConnections.Keys.ToList();
    }

    public static bool IsUserOnline(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        return _userConnections.ContainsKey(userId);
    }
}
