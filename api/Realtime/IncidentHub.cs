using IncidentLens.Api.Data;
using IncidentLens.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Realtime;

public sealed record PresenceUser(string ConnectionId, string Name, string Role, DateTimeOffset JoinedAt);

/// <summary>Only members of the incident's tenant can join its collaboration room.</summary>
[Authorize(Policy = "IncidentReader")]
public sealed class IncidentHub(PresenceTracker presence, IncidentLensDbContext database,
    IConfiguration configuration) : Hub
{
    public static string GroupName(string tenantId, string incidentId) =>
        $"tenant:{tenantId}:incident:{incidentId.ToUpperInvariant()}";

    private string TenantId => TenantContext.Normalize(Context.User?.FindFirst(
        configuration["Authentication:TenantClaimType"] ?? "tenant_id")?.Value);

    public async Task JoinIncident(string incidentId)
    {
        var tenantId = TenantId;
        if (string.IsNullOrEmpty(tenantId) ||
            !int.TryParse(incidentId.Replace("INC-", "", StringComparison.OrdinalIgnoreCase), out var sequence) ||
            !await database.Incidents.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                item => item.TenantId == tenantId && item.Sequence == sequence))
            throw new HubException("Incident not found.");

        var normalized = $"INC-{sequence}";
        var key = GroupName(tenantId, normalized);
        await Groups.AddToGroupAsync(Context.ConnectionId, key);
        presence.Join(key, new PresenceUser(Context.ConnectionId,
            Context.User?.Identity?.Name ?? "Operator",
            Context.User?.IsInRole("Commander") == true ? "Commander" : "Viewer",
            DateTimeOffset.UtcNow));
        await Clients.Group(key).SendAsync("PresenceChanged", normalized, presence.List(key));
    }

    public async Task LeaveIncident(string incidentId)
    {
        var normalized = incidentId.ToUpperInvariant();
        var key = GroupName(TenantId, normalized);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
        presence.Leave(key, Context.ConnectionId);
        await Clients.Group(key).SendAsync("PresenceChanged", normalized, presence.List(key));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var key in presence.LeaveEverywhere(Context.ConnectionId))
        {
            // Room keys are private and only generated after an ownership check.
            var incidentId = key[(key.LastIndexOf(":incident:", StringComparison.Ordinal) + 10)..];
            await Clients.Group(key).SendAsync("PresenceChanged", incidentId, presence.List(key));
        }
        await base.OnDisconnectedAsync(exception);
    }
}
