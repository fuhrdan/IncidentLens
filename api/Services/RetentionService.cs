using IncidentLens.Api.Data;
using IncidentLens.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Services;

/// <summary>
/// Retains permanent incident evidence while allowing bounded cleanup of two
/// explicitly disposable data classes. Policies are deployment configuration,
/// not values supplied by callers. Tenant is derived from the validated token.
/// </summary>
public sealed class RetentionService(
    IncidentLensDbContext database,
    TenantContext tenant,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<RetentionService> logger)
{
    public sealed record Policy(
        string TenantId,
        bool PurgeEnabled,
        int HealthSnapshotsDays,
        int ProcessedOutboxDays,
        int MaxRowsPerRun,
        string[] PreservedData);

    public sealed record Preview(
        Policy Policy,
        DateTimeOffset AsOf,
        DateTimeOffset HealthSnapshotsBefore,
        DateTimeOffset ProcessedOutboxBefore,
        int EligibleHealthSnapshots,
        int EligibleProcessedOutbox);

    public sealed record RunResult(Preview Before, int DeletedHealthSnapshots,
        int DeletedProcessedOutbox, bool MoreMayRemain);

    public Policy GetPolicy()
    {
        var tenantId = tenant.TenantId;
        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("A validated tenant is required.");

        // This identifier is already limited to ASCII alphanumerics, '-' and '_'.
        // Configuration names are trusted deployment configuration, not user input.
        var prefix = $"Retention:Tenants:{tenantId}:";
        int Days(string name, int fallback, int minimum)
        {
            var raw = configuration[prefix + name] ?? configuration["Retention:" + name];
            if (raw is null) return fallback;
            if (!int.TryParse(raw, out var days) || days < minimum || days > 3650)
                throw new InvalidOperationException($"Invalid Retention:{name} configuration.");
            return days;
        }

        var rawEnabled = configuration["Retention:AllowPurge"] ?? "false";
        if (!bool.TryParse(rawEnabled, out var enabled))
            throw new InvalidOperationException("Invalid Retention:AllowPurge configuration.");
        var size = configuration.GetValue<int?>("Retention:MaxRowsPerRun") ?? 250;
        if (size is < 1 or > 5000)
            throw new InvalidOperationException("Retention:MaxRowsPerRun must be 1 through 5000.");

        return new Policy(tenantId, enabled,
            Days("HealthSnapshotsDays", 90, 30),
            Days("ProcessedOutboxDays", 30, 7), size,
            ["Incidents", "Timeline", "Audit", "Postmortems", "ActionItems",
             "AlertIdempotency", "UnprocessedOutbox", "ReliabilitySignals"]);
    }

    public async Task<Preview> PreviewAsync(CancellationToken cancellationToken)
    {
        var policy = GetPolicy();
        var now = clock.GetUtcNow();
        var healthBefore = now.AddDays(-policy.HealthSnapshotsDays);
        var outboxBefore = now.AddDays(-policy.ProcessedOutboxDays);
        // SQLite is only a development database; it cannot translate ordering or
        // range comparisons for DateTimeOffset. Compare timestamps on the client
        // in local SQLite, while PostgreSQL performs production filtering in SQL.
        // Both branches use tenant-filtered queries; never IgnoreQueryFilters.
        int health;
        int outbox;
        if (database.Database.IsSqlite())
        {
            var healthTimes = await database.ServiceHealthSnapshots.AsNoTracking()
                .Where(row => row.TenantId == policy.TenantId)
                .Select(row => row.CapturedAt).ToListAsync(cancellationToken);
            var deliveryTimes = await database.OutboxMessages.AsNoTracking()
                .Where(row => row.TenantId == policy.TenantId && row.ProcessedAt.HasValue)
                .Select(row => row.ProcessedAt).ToListAsync(cancellationToken);
            health = healthTimes.Count(at => at < healthBefore);
            outbox = deliveryTimes.Count(at => at < outboxBefore);
        }
        else
        {
            health = await database.ServiceHealthSnapshots.AsNoTracking()
                .CountAsync(row => row.TenantId == policy.TenantId &&
                    row.CapturedAt < healthBefore, cancellationToken);
            outbox = await database.OutboxMessages.AsNoTracking()
                .CountAsync(row => row.TenantId == policy.TenantId &&
                    row.ProcessedAt.HasValue && row.ProcessedAt.Value < outboxBefore,
                    cancellationToken);
        }
        return new Preview(policy, now, healthBefore, outboxBefore, health, outbox);
    }

    public async Task<RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var before = await PreviewAsync(cancellationToken);
        if (!before.Policy.PurgeEnabled)
            throw new InvalidOperationException("Retention cleanup is disabled by deployment policy.");

        // Fetch bounded IDs before deleting. PostgreSQL performs age checks in
        // SQL twice, protecting against concurrently modified rows. Development
        // SQLite evaluates DateTimeOffset cutoffs in memory because its EF
        // provider does not support these range operations in the SQL dialect.
        // In every path the tenant is explicitly included in the DELETE.
        Guid[] healthIds;
        Guid[] outboxIds;
        int deletedHealth;
        int deletedOutbox;
        if (database.Database.IsSqlite())
        {
            var healthRows = await database.ServiceHealthSnapshots.AsNoTracking()
                .Where(row => row.TenantId == before.Policy.TenantId)
                .Select(row => new { row.Id, row.CapturedAt })
                .ToListAsync(cancellationToken);
            healthIds = healthRows.Where(row => row.CapturedAt < before.HealthSnapshotsBefore)
                .OrderBy(row => row.Id).Take(before.Policy.MaxRowsPerRun)
                .Select(row => row.Id).ToArray();
            deletedHealth = healthIds.Length == 0 ? 0 : await database.ServiceHealthSnapshots
                .Where(row => row.TenantId == before.Policy.TenantId &&
                    healthIds.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);

            var outboxRows = await database.OutboxMessages.AsNoTracking()
                .Where(row => row.TenantId == before.Policy.TenantId && row.ProcessedAt.HasValue)
                .Select(row => new { row.Id, row.ProcessedAt })
                .ToListAsync(cancellationToken);
            outboxIds = outboxRows.Where(row => row.ProcessedAt < before.ProcessedOutboxBefore)
                .OrderBy(row => row.Id).Take(before.Policy.MaxRowsPerRun)
                .Select(row => row.Id).ToArray();
            deletedOutbox = outboxIds.Length == 0 ? 0 : await database.OutboxMessages
                .Where(row => row.TenantId == before.Policy.TenantId &&
                    row.ProcessedAt.HasValue && outboxIds.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            healthIds = await database.ServiceHealthSnapshots.AsNoTracking()
                .Where(row => row.TenantId == before.Policy.TenantId &&
                    row.CapturedAt < before.HealthSnapshotsBefore)
                .OrderBy(row => row.Id)
                .Select(row => row.Id).Take(before.Policy.MaxRowsPerRun)
                .ToArrayAsync(cancellationToken);
            deletedHealth = healthIds.Length == 0 ? 0 : await database.ServiceHealthSnapshots
                .Where(row => row.TenantId == before.Policy.TenantId &&
                    row.CapturedAt < before.HealthSnapshotsBefore && healthIds.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);

            outboxIds = await database.OutboxMessages.AsNoTracking()
                .Where(row => row.TenantId == before.Policy.TenantId &&
                    row.ProcessedAt.HasValue && row.ProcessedAt.Value < before.ProcessedOutboxBefore)
                .OrderBy(row => row.Id)
                .Select(row => row.Id).Take(before.Policy.MaxRowsPerRun)
                .ToArrayAsync(cancellationToken);
            deletedOutbox = outboxIds.Length == 0 ? 0 : await database.OutboxMessages
                .Where(row => row.TenantId == before.Policy.TenantId &&
                    row.ProcessedAt.HasValue && row.ProcessedAt.Value < before.ProcessedOutboxBefore &&
                    outboxIds.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        logger.LogWarning("Tenant retention cleanup: tenant={TenantId}, health={Health}, deliveredOutbox={Outbox}",
            before.Policy.TenantId, deletedHealth, deletedOutbox);
        return new RunResult(before, deletedHealth, deletedOutbox,
            before.EligibleHealthSnapshots > deletedHealth ||
            before.EligibleProcessedOutbox > deletedOutbox);
    }
}
