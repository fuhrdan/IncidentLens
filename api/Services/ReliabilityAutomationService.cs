using System.Globalization;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Services;

public sealed class ReliabilityAutomationService(
    IncidentLensDbContext database,
    IncidentService incidents,
    TimeProvider clock,
    IncidentTelemetry telemetry,
    ILogger<ReliabilityAutomationService> logger)
{
    public async Task<IReadOnlyList<ServiceObjectiveResponse>> ListObjectivesAsync(CancellationToken token) =>
        (await database.ServiceObjectives.AsNoTracking().ToListAsync(token))
            .OrderBy(item => item.Service).Select(Map).ToList();

    public async Task<ServiceObjectiveResponse?> UpdateObjectiveAsync(Guid id,
        UpdateServiceObjectiveRequest request, CancellationToken token)
    {
        var objective = await database.ServiceObjectives.SingleOrDefaultAsync(item => item.Id == id, token);
        if (objective is null) return null;
        EnsureVersion(objective.Version, request.ExpectedVersion, Map(objective));
        objective.OwnerTeam = request.OwnerTeam.Trim();
        objective.AvailabilityTargetPercent = request.AvailabilityTargetPercent;
        objective.AcknowledgementTargetMinutes = request.AcknowledgementTargetMinutes;
        objective.ResolutionTargetMinutes = request.ResolutionTargetMinutes;
        objective.MonthlyErrorBudgetMinutes = request.MonthlyErrorBudgetMinutes;
        objective.FastBurnThreshold = request.FastBurnThreshold;
        objective.SlowBurnThreshold = request.SlowBurnThreshold;
        objective.Enabled = request.Enabled;
        objective.Version++;
        await SaveObjectiveAsync(objective, token);
        logger.LogInformation("Reliability objective {Service} updated to version {Version}",
            objective.Service, objective.Version);
        return Map(objective);
    }

    public async Task<IReadOnlyList<MaintenanceWindowResponse>> ListMaintenanceWindowsAsync(
        CancellationToken token)
    {
        var now = clock.GetUtcNow();
        return (await database.MaintenanceWindows.AsNoTracking().ToListAsync(token))
            .OrderByDescending(item => item.StartsAt).Select(item => Map(item, now)).ToList();
    }

    public async Task<MaintenanceWindowResponse> CreateMaintenanceWindowAsync(
        CreateMaintenanceWindowRequest request, string actor, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        if (!await database.ServiceObjectives.AsNoTracking()
                .AnyAsync(item => item.Service == request.Service.Trim(), token))
            throw new ReliabilityCommandException("The maintenance service must have a configured objective.");
        var window = new MaintenanceWindowRecord
        {
            Id = Guid.NewGuid(), Version = 1, Service = request.Service.Trim(),
            Title = request.Title.Trim(), StartsAt = request.StartsAt, EndsAt = request.EndsAt,
            CreatedBy = actor, CreatedAt = now,
        };
        database.MaintenanceWindows.Add(window);
        await database.SaveChangesAsync(token);
        logger.LogInformation("Maintenance window {WindowId} created for {Service} by {Actor}",
            window.Id, window.Service, actor);
        return Map(window, now);
    }

    public async Task<bool?> DeleteMaintenanceWindowAsync(Guid id, long expectedVersion,
        CancellationToken token)
    {
        var window = await database.MaintenanceWindows.SingleOrDefaultAsync(item => item.Id == id, token);
        if (window is null) return null;
        EnsureVersion(window.Version, expectedVersion, Map(window, clock.GetUtcNow()));
        database.MaintenanceWindows.Remove(window);
        try { await database.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ReliabilityConflictException(null); }
        return true;
    }

    public async Task<IReadOnlyList<ReliabilitySignalResponse>> ListSignalsAsync(
        ReliabilitySignalStatus? status, CancellationToken token)
    {
        var query = database.ReliabilitySignals.AsNoTracking().Include(item => item.Incident).AsQueryable();
        if (status is not null) query = query.Where(item => item.Status == status);
        return (await query.ToListAsync(token)).OrderByDescending(item => item.LastObservedAt)
            .Select(Map).ToList();
    }

    public async Task<EvaluateReliabilityResponse> EvaluateAsync(CancellationToken token)
    {
        using var activity = telemetry.Activities.StartActivity("reliability.evaluate");
        var now = clock.GetUtcNow();
        var objectives = await database.ServiceObjectives.AsNoTracking()
            .Where(item => item.Enabled).ToListAsync(token);
        var snapshots = await database.ServiceHealthSnapshots.AsNoTracking().ToListAsync(token);
        // DateTimeOffset range predicates are evaluated in memory so the same
        // workflow remains portable across the SQLite demo and PostgreSQL.
        var windows = (await database.MaintenanceWindows.AsNoTracking().ToListAsync(token))
            .Where(item => item.StartsAt <= now && item.EndsAt >= now).ToList();
        var emitted = new List<ReliabilitySignalRecord>();
        var created = 0; var updated = 0; var suppressed = 0;

        foreach (var objective in objectives)
        {
            var serviceSnapshots = snapshots.Where(item => item.Service == objective.Service).ToList();
            foreach (var definition in new[]
            {
                new BurnWindow(60, objective.FastBurnThreshold),
                new BurnWindow(360, objective.SlowBurnThreshold),
            })
            {
                var samples = serviceSnapshots.Where(item => item.CapturedAt >= now.AddMinutes(-definition.Minutes)).ToList();
                if (samples.Count == 0) continue;
                var observedAvailability = samples.Average(item => item.AvailabilityPercent);
                var allowedError = Math.Max(0.001m, 100m - objective.AvailabilityTargetPercent);
                var burnRate = Math.Round(Math.Max(0m, 100m - observedAvailability) / allowedError, 2);
                if (burnRate < definition.Threshold) continue;

                var maintenance = windows.FirstOrDefault(item => item.Service == objective.Service);
                var status = maintenance is null ? ReliabilitySignalStatus.Open : ReliabilitySignalStatus.Suppressed;
                var fingerprint = $"{objective.Service}|{definition.Minutes}|{now:yyyyMMddHH}";
                var signal = await database.ReliabilitySignals.Include(item => item.Incident)
                    .SingleOrDefaultAsync(item => item.Fingerprint == fingerprint, token);
                if (signal is null)
                {
                    signal = new ReliabilitySignalRecord
                    {
                        Id = Guid.NewGuid(), Fingerprint = fingerprint, Service = objective.Service,
                        Status = status, SuggestedSeverity = burnRate >= objective.FastBurnThreshold * 2
                            ? Severity.Sev1 : Severity.Sev2,
                        EvaluationWindowMinutes = definition.Minutes, BurnRate = burnRate,
                        ObservedAvailabilityPercent = Math.Round(observedAvailability, 3),
                        TargetAvailabilityPercent = objective.AvailabilityTargetPercent,
                        Summary = $"{definition.Minutes}-minute SLO burn is {burnRate.ToString("0.00", CultureInfo.InvariantCulture)}x for {objective.Service}.",
                        FirstObservedAt = now, LastObservedAt = now,
                        SuppressionReason = maintenance is null ? null : $"Maintenance: {maintenance.Title}",
                    };
                    database.ReliabilitySignals.Add(signal); created++;
                    if (status == ReliabilitySignalStatus.Suppressed) suppressed++;
                    telemetry.ReliabilitySignalsCreated.Add(1,
                        new KeyValuePair<string, object?>("status", status.ToString()));
                }
                else
                {
                    signal.LastObservedAt = now; signal.BurnRate = burnRate;
                    signal.ObservedAvailabilityPercent = Math.Round(observedAvailability, 3);
                    signal.Version++; updated++;
                }
                emitted.Add(signal);
            }
        }

        await database.SaveChangesAsync(token);
        telemetry.ReliabilityEvaluations.Add(1,
            new KeyValuePair<string, object?>("services", objectives.Count));
        activity?.SetTag("reliability.signals.created", created);
        return new EvaluateReliabilityResponse(now, objectives.Count, created, updated, suppressed,
            emitted.Select(Map).ToList());
    }

    public async Task<ReliabilitySignalResponse?> AcknowledgeAsync(Guid id, long expectedVersion,
        string actor, CancellationToken token)
    {
        var signal = await database.ReliabilitySignals.Include(item => item.Incident)
            .SingleOrDefaultAsync(item => item.Id == id, token);
        if (signal is null) return null;
        EnsureVersion(signal.Version, expectedVersion, Map(signal));
        if (signal.Status == ReliabilitySignalStatus.Open)
        {
            signal.Status = ReliabilitySignalStatus.Acknowledged;
            signal.AcknowledgedAt = clock.GetUtcNow(); signal.AcknowledgedBy = actor; signal.Version++;
            await SaveSignalAsync(signal, token);
        }
        return Map(signal);
    }

    public async Task<PromoteReliabilitySignalResponse?> PromoteAsync(Guid id,
        PromoteReliabilitySignalRequest request, string actor, CancellationToken token)
    {
        var signal = await database.ReliabilitySignals.Include(item => item.Incident)
            .SingleOrDefaultAsync(item => item.Id == id, token);
        if (signal is null) return null;
        EnsureVersion(signal.Version, request.ExpectedVersion, Map(signal));
        if (signal.Status == ReliabilitySignalStatus.Suppressed)
            throw new ReliabilityCommandException("A maintenance-suppressed signal cannot be promoted.");
        if (signal.Incident is not null)
            return new PromoteReliabilitySignalResponse(Map(signal),
                await incidents.GetAsync($"INC-{signal.Incident.Sequence}", token)
                    ?? throw new InvalidOperationException("The promoted incident no longer exists."));

        var objective = await database.ServiceObjectives.AsNoTracking()
            .SingleAsync(item => item.Service == signal.Service, token);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var incident = await incidents.CreateAsync(new CreateIncidentRequest(
            $"SLO burn detected for {signal.Service}", signal.Summary,
            signal.SuggestedSeverity, signal.Service, objective.OwnerTeam,
            request.Assignee.Trim()), actor, token);
        signal.Status = ReliabilitySignalStatus.Promoted;
        signal.IncidentId = await database.Incidents.Where(item => item.Sequence ==
            int.Parse(incident.Id.Replace("INC-", "", StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.Id).SingleAsync(token);
        signal.AcknowledgedAt ??= clock.GetUtcNow(); signal.AcknowledgedBy ??= actor;
        signal.Version++;
        await SaveSignalAsync(signal, token);
        await transaction.CommitAsync(token);
        telemetry.ReliabilitySignalsPromoted.Add(1,
            new KeyValuePair<string, object?>("severity", incident.Severity));
        logger.LogInformation("Reliability signal {SignalId} promoted to {IncidentId} by {Actor}",
            signal.Id, incident.Id, actor);
        signal.Incident = await database.Incidents.AsNoTracking()
            .SingleAsync(item => item.Id == signal.IncidentId, token);
        return new PromoteReliabilitySignalResponse(Map(signal), incident);
    }

    private async Task SaveObjectiveAsync(ServiceObjectiveRecord objective, CancellationToken token)
    {
        try { await database.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            var current = await database.ServiceObjectives.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == objective.Id, token);
            throw new ReliabilityConflictException(current is null ? null : Map(current));
        }
    }

    private async Task SaveSignalAsync(ReliabilitySignalRecord signal, CancellationToken token)
    {
        try { await database.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            var current = await database.ReliabilitySignals.AsNoTracking().Include(item => item.Incident)
                .SingleOrDefaultAsync(item => item.Id == signal.Id, token);
            throw new ReliabilityConflictException(current is null ? null : Map(current));
        }
    }

    private static void EnsureVersion(long current, long expected, object response)
    { if (current != expected) throw new ReliabilityConflictException(response); }

    private static ServiceObjectiveResponse Map(ServiceObjectiveRecord item) => new(item.Id, item.Version,
        item.Service, item.OwnerTeam, item.AvailabilityTargetPercent, item.AcknowledgementTargetMinutes,
        item.ResolutionTargetMinutes, item.MonthlyErrorBudgetMinutes, item.FastBurnThreshold,
        item.SlowBurnThreshold, item.Enabled);

    private static MaintenanceWindowResponse Map(MaintenanceWindowRecord item, DateTimeOffset now) =>
        new(item.Id, item.Version, item.Service, item.Title, item.StartsAt, item.EndsAt,
            item.CreatedBy, item.CreatedAt, item.StartsAt <= now && item.EndsAt >= now);

    private static ReliabilitySignalResponse Map(ReliabilitySignalRecord item) => new(item.Id,
        item.Version, item.Service, item.Status, SeverityLabel(item.SuggestedSeverity),
        item.EvaluationWindowMinutes, item.BurnRate, item.ObservedAvailabilityPercent,
        item.TargetAvailabilityPercent, item.Summary, item.FirstObservedAt, item.LastObservedAt,
        item.AcknowledgedAt, item.AcknowledgedBy,
        item.Incident is null ? null : $"INC-{item.Incident.Sequence}", item.SuppressionReason);

    private static string SeverityLabel(Severity severity) => severity switch
    { Severity.Sev1 => "SEV-1", Severity.Sev2 => "SEV-2", Severity.Sev3 => "SEV-3", _ => "SEV-4" };

    private sealed record BurnWindow(int Minutes, decimal Threshold);
}

public sealed class ReliabilityConflictException(object? current) : Exception
{ public object? Current { get; } = current; }

public sealed class ReliabilityCommandException(string message) : Exception(message);
