using IncidentLens.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool seedDemo)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
        // PostgreSQL deployments advance through reviewed migrations. SQLite
        // remains a zero-infrastructure developer option and creates its schema
        // directly because provider-specific migration SQL is intentionally not
        // shared between production and the local convenience database.
        if (database.Database.IsNpgsql())
        {
            await database.Database.MigrateAsync();
        }
        else
        {
            await database.Database.EnsureCreatedAsync();
        }

        if (!seedDemo) return;

        if (!await database.Incidents.AnyAsync())
        {
            var incidents = new[]
            {
                CreateIncident(1042, "Checkout authorization latency", "Card authorization p95 is above 4 seconds for North American traffic.", Severity.Sev1, IncidentStatus.Investigating, "Payments API", "Payments Platform", "Maya Chen", 1842, DateTimeOffset.Parse("2026-09-18T18:42:00Z"), ["customer-impact", "latency"], [("Ravi Shah", "Communications"), ("Elena Ortiz", "Operations")]),
                CreateIncident(1041, "Search index lag in us-west", "Product updates are taking up to 22 minutes to appear in search results.", Severity.Sev2, IncidentStatus.Identified, "Catalog Search", "Discovery Engineering", "Noah Williams", 624, DateTimeOffset.Parse("2026-09-18T17:58:00Z"), ["us-west", "backlog"], [("Priya Raman", "Subject matter expert")]),
                CreateIncident(1039, "Elevated webhook delivery failures", "A retry policy update caused excess 429 responses for partner webhooks.", Severity.Sev2, IncidentStatus.Monitoring, "Event Delivery", "Integration Platform", "Elena Ortiz", 137, DateTimeOffset.Parse("2026-09-18T15:21:00Z"), ["partners", "rate-limit"], [("Maya Chen", "Observer")]),
                CreateIncident(1038, "Edge routing instability", "A route advertisement caused intermittent gateway failures in Europe.", Severity.Sev1, IncidentStatus.Resolved, "Edge Gateway", "Traffic Engineering", "Ravi Shah", 2190, DateTimeOffset.Parse("2026-09-15T10:04:00Z"), ["eu-central", "network"], [("Maya Chen", "Incident commander")]),
                CreateIncident(1036, "Payment token refresh errors", "An expired signing key reduced successful token refreshes.", Severity.Sev2, IncidentStatus.Resolved, "Payments API", "Payments Platform", "Maya Chen", 430, DateTimeOffset.Parse("2026-09-11T08:14:00Z"), ["authentication", "customer-impact"], [("Elena Ortiz", "Operations")]),
            };

            database.Incidents.AddRange(incidents);
        }

        if (!await database.ServiceObjectives.AnyAsync())
        {
            database.ServiceObjectives.AddRange(
                Objective("Payments API", "Payments Platform", 99.95m, 5, 45, 22),
                Objective("Catalog Search", "Discovery Engineering", 99.90m, 10, 90, 43),
                Objective("Event Delivery", "Integration Platform", 99.90m, 10, 60, 43),
                Objective("Edge Gateway", "Traffic Engineering", 99.99m, 3, 30, 4));
        }

        if (!await database.ServiceHealthSnapshots.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow.Date;
            var profiles = new[]
            {
                ("Payments API", 99.93m, 0.21m, 890),
                ("Catalog Search", 99.96m, 0.08m, 420),
                ("Event Delivery", 99.89m, 0.31m, 680),
                ("Edge Gateway", 99.995m, 0.02m, 160),
            };
            for (var day = 6; day >= 0; day--)
            {
                foreach (var profile in profiles)
                {
                    var variation = (6 - day) % 3 * 0.006m;
                    database.ServiceHealthSnapshots.Add(new ServiceHealthSnapshotRecord
                    {
                        Id = Guid.NewGuid(),
                        Service = profile.Item1,
                        CapturedAt = now.AddDays(-day),
                        AvailabilityPercent = Math.Min(100m, profile.Item2 + variation),
                        ErrorRatePercent = Math.Max(0m, profile.Item3 - variation),
                        LatencyP95Milliseconds = profile.Item4 + (day % 3 * 24),
                    });
                }
            }
            database.ServiceHealthSnapshots.AddRange(
                Snapshot("Payments API", DateTimeOffset.UtcNow.AddMinutes(-25), 99.20m, 0.80m, 1240),
                Snapshot("Catalog Search", DateTimeOffset.UtcNow.AddMinutes(-25), 99.96m, 0.08m, 420),
                Snapshot("Event Delivery", DateTimeOffset.UtcNow.AddMinutes(-25), 98.30m, 1.70m, 970),
                Snapshot("Edge Gateway", DateTimeOffset.UtcNow.AddMinutes(-25), 99.995m, 0.02m, 160));
        }

        if (!await database.MaintenanceWindows.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            database.MaintenanceWindows.Add(new MaintenanceWindowRecord
            {
                Id = Guid.NewGuid(), Service = "Event Delivery", Title = "Partner retry-policy rollout",
                StartsAt = now.AddMinutes(-30), EndsAt = now.AddHours(2), CreatedBy = "Elena Ortiz",
                CreatedAt = now.AddHours(-1),
            });
        }

        await database.SaveChangesAsync();
    }

    private static ServiceObjectiveRecord Objective(string service, string ownerTeam, decimal availability,
        int acknowledgementMinutes, int resolutionMinutes, int errorBudgetMinutes) => new()
    {
        Id = Guid.NewGuid(),
        Service = service,
        OwnerTeam = ownerTeam,
        AvailabilityTargetPercent = availability,
        AcknowledgementTargetMinutes = acknowledgementMinutes,
        ResolutionTargetMinutes = resolutionMinutes,
        MonthlyErrorBudgetMinutes = errorBudgetMinutes,
    };

    private static ServiceHealthSnapshotRecord Snapshot(string service, DateTimeOffset capturedAt,
        decimal availability, decimal errorRate, int latency) => new()
    {
        Id = Guid.NewGuid(), Service = service, CapturedAt = capturedAt,
        AvailabilityPercent = availability, ErrorRatePercent = errorRate,
        LatencyP95Milliseconds = latency,
    };

    private static IncidentRecord CreateIncident(
        int sequence,
        string title,
        string summary,
        Severity severity,
        IncidentStatus status,
        string service,
        string ownerTeam,
        string assignee,
        int affectedCustomers,
        DateTimeOffset declaredAt,
        IReadOnlyList<string> tags,
        IReadOnlyList<(string Name, string Role)> responders)
    {
        var incident = new IncidentRecord
        {
            Id = Guid.NewGuid(),
            Sequence = sequence,
            Version = 1,
            Title = title,
            Summary = summary,
            Severity = severity,
            Status = status,
            Service = service,
            OwnerTeam = ownerTeam,
            Assignee = assignee,
            AffectedCustomers = affectedCustomers,
            DeclaredAt = declaredAt,
            UpdatedAt = status == IncidentStatus.Resolved ? declaredAt.AddMinutes(42) : declaredAt,
            AcknowledgedAt = status == IncidentStatus.Investigating ? null : declaredAt.AddMinutes(4),
            ResolvedAt = status == IncidentStatus.Resolved ? declaredAt.AddMinutes(42) : null,
        };
        incident.Timeline.Add(new TimelineEventRecord
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            OccurredAt = declaredAt,
            Actor = "Alert Router",
            Type = TimelineEventType.Declared,
            Message = "Incident declared from an operational alert.",
        });
        incident.Tags.AddRange(tags.Select(value => new IncidentTagRecord
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            Value = value,
        }));
        incident.Responders.AddRange(responders.Select(responder => new IncidentResponderRecord
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            Name = responder.Name,
            Role = responder.Role,
            JoinedAt = declaredAt,
        }));
        incident.AuditRecords.Add(new AuditRecord
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            OccurredAt = declaredAt,
            Actor = "Alert Router",
            Action = "incident.declared",
            Details = $"Declared {severity} incident for {service}.",
            IncidentVersion = 1,
        });
        return incident;
    }
}
