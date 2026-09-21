using System.ComponentModel.DataAnnotations;
using IncidentLens.Api.Domain;

namespace IncidentLens.Api.Contracts;

public sealed record ReliabilityMetricsResponse(
    decimal MeanTimeToAcknowledgeMinutes,
    decimal MeanTimeToResolveMinutes,
    decimal RecurrenceRatePercent,
    long CustomerImpactMinutes,
    int ResolvedIncidents,
    int ActiveIncidents);

public sealed record ServiceTrendPointResponse(
    DateTimeOffset CapturedAt,
    decimal AvailabilityPercent,
    decimal ErrorRatePercent,
    int LatencyP95Milliseconds);

public sealed record ServiceHealthResponse(
    string Service,
    decimal AvailabilityTargetPercent,
    decimal CurrentAvailabilityPercent,
    decimal ErrorBudgetConsumedPercent,
    int IncidentCount,
    int SloBreaches,
    int AffectedCustomers,
    string Health,
    IReadOnlyList<ServiceTrendPointResponse> Trend);

public sealed record AnalyticsOverviewResponse(
    DateTimeOffset GeneratedAt,
    int WindowDays,
    ReliabilityMetricsResponse Metrics,
    IReadOnlyList<ServiceHealthResponse> Services);

public sealed record UpsertPostmortemRequest(
    [property: Required, MaxLength(80)] string Owner,
    PostmortemStatus Status,
    [property: Required, MaxLength(1000)] string ExecutiveSummary,
    [property: Required, MaxLength(2000)] string RootCause,
    [property: Required, MaxLength(1000)] string Detection,
    [property: Required, MaxLength(2000)] string Resolution,
    [property: Required, MaxLength(2000)] string LessonsLearned,
    long? ExpectedVersion);

public sealed record AddActionItemRequest(
    [property: Required, MaxLength(240)] string Title,
    [property: Required, MaxLength(80)] string Owner,
    DateTimeOffset DueAt,
    long ExpectedPostmortemVersion);

public sealed record UpdateActionItemRequest(
    ActionItemStatus Status,
    [property: Required, MaxLength(80)] string Owner,
    DateTimeOffset DueAt,
    long ExpectedPostmortemVersion);

public sealed record PostmortemActionItemResponse(
    Guid Id, string Title, string Owner, ActionItemStatus Status,
    DateTimeOffset DueAt, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record PostmortemResponse(
    Guid Id, string IncidentId, long Version, PostmortemStatus Status,
    string Owner, string ExecutiveSummary, string RootCause, string Detection,
    string Resolution, string LessonsLearned, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, IReadOnlyList<PostmortemActionItemResponse> ActionItems);

public sealed record EvidencePackage(
    DateTimeOffset GeneratedAt,
    string TraceId,
    IncidentResponse Incident,
    IReadOnlyList<AuditResponse> Audit,
    PostmortemResponse? Postmortem);
