using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Features.Dashboard;

/// <summary>
/// Composes existing read models into one bounded dashboard response. Every query
/// inherits the DbContext's authenticated tenant filter; NEVER IgnoreQueryFilters.
/// The totals cover the tenant's entire incident backlog; rows are the newest 12.
/// </summary>
public sealed class DashboardOverviewService(
    IncidentLensDbContext database,
    AnalyticsService analytics,
    TimeProvider clock)
{
    public async Task<DashboardOverview> GetAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 90);
        var active = database.Incidents.AsNoTracking()
            .Where(incident => incident.Status != IncidentStatus.Resolved);

        var total = await active.CountAsync(cancellationToken);
        var critical = await active.CountAsync(incident =>
            incident.Severity == Severity.Sev1, cancellationToken);

        // Order by sequence works consistently on SQLite and PostgreSQL.
        var newest = await active.Include(incident => incident.Responders)
            .OrderByDescending(incident => incident.Sequence)
            .Take(12)
            .ToListAsync(cancellationToken);
        var rows = newest.Select(incident => new DashboardIncident(
            $"INC-{incident.Sequence}", incident.Title,
            incident.Severity == Severity.Sev1 ? "SEV-1" :
            incident.Severity == Severity.Sev2 ? "SEV-2" :
            incident.Severity == Severity.Sev3 ? "SEV-3" : "SEV-4",
            incident.Status.ToString(), incident.OwnerTeam, incident.Assignee,
            incident.Service, incident.Responders.Count, incident.DeclaredAt))
            .ToList();

        // Reuse the established reliability calculations instead of diverging
        // from the existing /api/analytics/overview endpoint.
        var overview = await analytics.GetOverviewAsync(days, cancellationToken);
        // SQLite does not translate DateTimeOffset range comparisons uniformly;
        // evaluate the date range over the already tenant-filtered timestamps.
        var acknowledgedDates = await database.Incidents.AsNoTracking()
            .Where(incident => incident.AcknowledgedAt != null)
            .Select(incident => incident.DeclaredAt)
            .ToListAsync(cancellationToken);
        var mtta = acknowledgedDates.Any(date => date >= clock.GetUtcNow().AddDays(-days))
            ? (decimal?)overview.Metrics.MeanTimeToAcknowledgeMinutes : null;
        var mttr = overview.Metrics.ResolvedIncidents == 0
            ? (decimal?)null : overview.Metrics.MeanTimeToResolveMinutes;

        return new DashboardOverview(clock.GetUtcNow(), days,
            new DashboardSummary(total, critical, mtta, mttr,
                overview.Services.Count(service => service.Health != "Healthy")), rows);
    }
}
