using System.Text.Json.Serialization;

namespace IncidentLens.Api.Domain;

public interface ITenantOwned
{
    string TenantId { get; set; }
}

public enum Severity
{
    [JsonStringEnumMemberName("SEV-1")]
    Sev1,
    [JsonStringEnumMemberName("SEV-2")]
    Sev2,
    [JsonStringEnumMemberName("SEV-3")]
    Sev3,
    [JsonStringEnumMemberName("SEV-4")]
    Sev4,
}

public enum IncidentStatus
{
    Investigating,
    Identified,
    Monitoring,
    Resolved,
}

public enum TimelineEventType
{
    Declared,
    Status,
    Note,
    Assignment,
    Responder,
    Tags,
    Command,
    Postmortem,
}

public enum PostmortemStatus { Draft, InReview, Published }
public enum ActionItemStatus { Open, InProgress, Done }
public enum ReliabilitySignalStatus { Open, Acknowledged, Promoted, Suppressed }

/// <summary>
/// Persistence model for an operational incident. Version is an application-
/// managed optimistic concurrency token shared by SQLite and PostgreSQL.
/// </summary>
public sealed class IncidentRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public long Version { get; set; } = 1;
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public Severity Severity { get; set; }
    public IncidentStatus Status { get; set; }
    public required string Service { get; set; }
    public required string OwnerTeam { get; set; }
    public required string Assignee { get; set; }
    public DateTimeOffset DeclaredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int AffectedCustomers { get; set; }
    public List<TimelineEventRecord> Timeline { get; set; } = [];
    public List<IncidentResponderRecord> Responders { get; set; } = [];
    public List<IncidentTagRecord> Tags { get; set; } = [];
    public List<AuditRecord> AuditRecords { get; set; } = [];
    public PostmortemRecord? Postmortem { get; set; }
}

public sealed class TimelineEventRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public IncidentRecord Incident { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public required string Actor { get; set; }
    public TimelineEventType Type { get; set; }
    public required string Message { get; set; }
    public string? CommandName { get; set; }
    public string? CommandArguments { get; set; }
    public string? MentionsJson { get; set; }
}

/// <summary>Records an accepted alert key so webhook retries cannot create duplicates.</summary>
public sealed class AlertIngestionRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid IncidentId { get; set; }
    public IncidentRecord Incident { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Durable hand-off between an incident transaction and real-time delivery.</summary>
public sealed class OutboxMessage : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Type { get; set; }
    public required string AggregateId { get; set; }
    public required string Payload { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Service-level targets used to correlate incidents with reliability objectives.</summary>
public sealed class ServiceObjectiveRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public long Version { get; set; } = 1;
    public required string Service { get; set; }
    public required string OwnerTeam { get; set; }
    public decimal AvailabilityTargetPercent { get; set; }
    public int AcknowledgementTargetMinutes { get; set; }
    public int ResolutionTargetMinutes { get; set; }
    public int MonthlyErrorBudgetMinutes { get; set; }
    public decimal FastBurnThreshold { get; set; } = 14m;
    public decimal SlowBurnThreshold { get; set; } = 6m;
    public bool Enabled { get; set; } = true;
}

public sealed class ServiceHealthSnapshotRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public required string Service { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public decimal AvailabilityPercent { get; set; }
    public decimal ErrorRatePercent { get; set; }
    public int LatencyP95Milliseconds { get; set; }
}

/// <summary>An approved period where expected service degradation should not page responders.</summary>
public sealed class MaintenanceWindowRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public long Version { get; set; } = 1;
    public required string Service { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A deduplicated SLO burn signal that can be reviewed before incident promotion.</summary>
public sealed class ReliabilitySignalRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public long Version { get; set; } = 1;
    public required string Fingerprint { get; set; }
    public required string Service { get; set; }
    public ReliabilitySignalStatus Status { get; set; } = ReliabilitySignalStatus.Open;
    public Severity SuggestedSeverity { get; set; }
    public int EvaluationWindowMinutes { get; set; }
    public decimal BurnRate { get; set; }
    public decimal ObservedAvailabilityPercent { get; set; }
    public decimal TargetAvailabilityPercent { get; set; }
    public required string Summary { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public Guid? IncidentId { get; set; }
    public IncidentRecord? Incident { get; set; }
    public string? SuppressionReason { get; set; }
}

public sealed class PostmortemRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public IncidentRecord Incident { get; set; } = null!;
    public long Version { get; set; } = 1;
    public PostmortemStatus Status { get; set; } = PostmortemStatus.Draft;
    public required string Owner { get; set; }
    public required string ExecutiveSummary { get; set; }
    public required string RootCause { get; set; }
    public required string Detection { get; set; }
    public required string Resolution { get; set; }
    public required string LessonsLearned { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<PostmortemActionItemRecord> ActionItems { get; set; } = [];
}

public sealed class PostmortemActionItemRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid PostmortemId { get; set; }
    public PostmortemRecord Postmortem { get; set; } = null!;
    public required string Title { get; set; }
    public required string Owner { get; set; }
    public ActionItemStatus Status { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class IncidentResponderRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public IncidentRecord Incident { get; set; } = null!;
    public required string Name { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class IncidentTagRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public IncidentRecord Incident { get; set; } = null!;
    public required string Value { get; set; }
}

/// <summary>
/// Append-only evidence of who changed what. Audit rows intentionally do not
/// cascade through the public IncidentResponse contract.
/// </summary>
public sealed class AuditRecord : ITenantOwned
{
    // Immutable organizational boundary, assigned by the authenticated server context.
    public string TenantId { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public IncidentRecord Incident { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public required string Actor { get; set; }
    public required string Action { get; set; }
    public required string Details { get; set; }
    public long IncidentVersion { get; set; }
}
