using System.Diagnostics;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Data;
using IncidentLens.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Services;

public sealed class AnalyticsService(IncidentLensDbContext database, TimeProvider clock, IncidentTelemetry telemetry)
{
    public async Task<AnalyticsOverviewResponse> GetOverviewAsync(int days, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = telemetry.Activities.StartActivity("analytics.overview");
        days = Math.Clamp(days, 1, 365);
        var cutoff = clock.GetUtcNow().AddDays(-days);
        var allIncidents = await database.Incidents.AsNoTracking().ToListAsync(token);
        var incidents = allIncidents.Where(item => item.DeclaredAt >= cutoff).ToList();
        var objectives = await database.ServiceObjectives.AsNoTracking().ToListAsync(token);
        var snapshots = (await database.ServiceHealthSnapshots.AsNoTracking().ToListAsync(token))
            .Where(item => item.CapturedAt >= cutoff).ToList();

        var acknowledged = incidents.Where(item => item.AcknowledgedAt is not null).ToList();
        var resolved = incidents.Where(item => item.ResolvedAt is not null).ToList();
        var mtta = acknowledged.Count == 0 ? 0 : acknowledged.Average(item =>
            (item.AcknowledgedAt!.Value - item.DeclaredAt).TotalMinutes);
        var mttr = resolved.Count == 0 ? 0 : resolved.Average(item =>
            (item.ResolvedAt!.Value - item.DeclaredAt).TotalMinutes);
        var recurrence = incidents.Count == 0 ? 0 :
            Math.Max(0, incidents.Count - incidents.Select(item => item.Service).Distinct().Count()) * 100m / incidents.Count;
        var impactMinutes = incidents.Sum(item => (long)item.AffectedCustomers * (long)Math.Max(1,
            ((item.ResolvedAt ?? clock.GetUtcNow()) - item.DeclaredAt).TotalMinutes));

        var services = objectives.Select(objective =>
        {
            var serviceIncidents = incidents.Where(item => item.Service == objective.Service).ToList();
            var trend = snapshots.Where(item => item.Service == objective.Service)
                .OrderBy(item => item.CapturedAt).Select(item => new ServiceTrendPointResponse(
                    item.CapturedAt, item.AvailabilityPercent, item.ErrorRatePercent,
                    item.LatencyP95Milliseconds)).ToList();
            var availability = trend.LastOrDefault()?.AvailabilityPercent ?? 100m;
            var downtime = serviceIncidents.Sum(item => Math.Max(0,
                ((item.ResolvedAt ?? clock.GetUtcNow()) - item.DeclaredAt).TotalMinutes));
            var budget = objective.MonthlyErrorBudgetMinutes == 0 ? 0 :
                (decimal)downtime / objective.MonthlyErrorBudgetMinutes * 100m;
            var breaches = serviceIncidents.Count(item =>
                ((item.ResolvedAt ?? clock.GetUtcNow()) - item.DeclaredAt).TotalMinutes > objective.ResolutionTargetMinutes);
            var health = availability < objective.AvailabilityTargetPercent || budget >= 100 ? "Critical"
                : budget >= 75 || breaches > 0 ? "At risk" : "Healthy";
            return new ServiceHealthResponse(objective.Service, objective.AvailabilityTargetPercent,
                availability, Math.Round(budget, 1), serviceIncidents.Count, breaches,
                serviceIncidents.Sum(item => item.AffectedCustomers), health, trend);
        }).OrderBy(item => item.Health).ThenBy(item => item.Service).ToList();

        stopwatch.Stop(); telemetry.AnalyticsDurationMilliseconds.Record(stopwatch.Elapsed.TotalMilliseconds);
        return new AnalyticsOverviewResponse(clock.GetUtcNow(), days,
            new ReliabilityMetricsResponse(Math.Round((decimal)mtta, 1), Math.Round((decimal)mttr, 1),
                Math.Round(recurrence, 1), impactMinutes, resolved.Count,
                incidents.Count(item => item.ResolvedAt is null)), services);
    }
}
