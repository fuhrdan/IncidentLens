using System.Collections.Concurrent;

namespace IncidentLens.Api.Realtime;

/// <summary>Ephemeral browser presence; durable responders remain in the incident record.</summary>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PresenceUser>> groups = new(StringComparer.OrdinalIgnoreCase);
    public void Join(string incidentId, PresenceUser user) => groups.GetOrAdd(incidentId, _ => new())[user.ConnectionId] = user;
    public void Leave(string incidentId, string connectionId)
    {
        if (!groups.TryGetValue(incidentId, out var users)) return;
        users.TryRemove(connectionId, out _);
        if (users.IsEmpty) groups.TryRemove(incidentId, out _);
    }
    public IReadOnlyList<PresenceUser> List(string incidentId) => groups.TryGetValue(incidentId, out var users)
        ? users.Values.OrderBy(item => item.JoinedAt).ToList() : [];
    public IReadOnlyList<string> LeaveEverywhere(string connectionId)
    {
        var changed = new List<string>();
        foreach (var group in groups)
        {
            if (group.Value.TryRemove(connectionId, out _)) changed.Add(group.Key);
            if (group.Value.IsEmpty) groups.TryRemove(group.Key, out _);
        }
        return changed;
    }
}
