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

        // Show services needing attention first.
        // Reuse the tenant-scoped analytics results so the dashboard
        // and reliability workspace report consistent measurements.
        var services = overview.Services
            .OrderBy(service => service.Health switch
            {
                "Critical" => 0,
                "At risk" => 1,
                _ => 2,
            })
            .ThenBy(service => service.Service)
            .Take(8)
            .ToList();

        // Both sides of this join retain their DbContext tenant filters.
        // Do not use IgnoreQueryFilters here: the dashboard must never
        // display another tenant's incident activity.
        var activityQuery = database.TimelineEvents
            .AsNoTracking()
            .Join(
                database.Incidents.AsNoTracking(),
                entry => entry.IncidentId,
                incident => incident.Id,
                (entry, incident) => new
                {
                    entry.Id,
                    entry.OccurredAt,
                    entry.Actor,
                    entry.Type,
                    entry.Message,
                    incident.Sequence,
                    incident.Title,
                    incident.Service,
                });

        var activityCutoff = clock.GetUtcNow().AddDays(-days);

        // PostgreSQL can filter and order DateTimeOffset values in SQL.
        // SQLite development databases need the timestamp comparison
        // performed in .NET, as elsewhere in the existing analytics code.
        var activityRows = database.Database.IsSqlite()
            ? (await activityQuery.ToListAsync(cancellationToken))
                .Where(entry => entry.OccurredAt >= activityCutoff)
                .OrderByDescending(entry => entry.OccurredAt)
                .ThenByDescending(entry => entry.Id)
                .Take(5)
                .ToList()
            : await activityQuery
                .Where(entry => entry.OccurredAt >= activityCutoff)
                .OrderByDescending(entry => entry.OccurredAt)
                .ThenByDescending(entry => entry.Id)
                .Take(5)
                .ToListAsync(cancellationToken);

        var recentActivity = activityRows
            .Select(entry => new DashboardActivity(
                entry.Id,
                $"INC-{entry.Sequence}",
                entry.Title,
                entry.Service,
                entry.Actor,
                entry.Type.ToString().ToLowerInvariant(),
                entry.Message,
                entry.OccurredAt))
            .ToList();

        return new DashboardOverview(
            clock.GetUtcNow(),
            days,
            new DashboardSummary(
                total,
                critical,
                mtta,
                mttr,
                overview.Services.Count(
                    service => service.Health != "Healthy")),
            rows,
            services,
            recentActivity);
    }
}
