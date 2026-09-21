using System.ComponentModel.DataAnnotations;
using IncidentLens.Api.Domain;

namespace IncidentLens.Api.Contracts;

public sealed record ServiceObjectiveResponse(
    Guid Id, long Version, string Service, string OwnerTeam,
    decimal AvailabilityTargetPercent, int AcknowledgementTargetMinutes,
    int ResolutionTargetMinutes, int MonthlyErrorBudgetMinutes,
    decimal FastBurnThreshold, decimal SlowBurnThreshold, bool Enabled);

public sealed record UpdateServiceObjectiveRequest(
    [property: Required, MaxLength(80)] string OwnerTeam,
    [property: Range(90, 100)] decimal AvailabilityTargetPercent,
    [property: Range(1, 1440)] int AcknowledgementTargetMinutes,
    [property: Range(1, 10080)] int ResolutionTargetMinutes,
    [property: Range(1, 44640)] int MonthlyErrorBudgetMinutes,
    [property: Range(1, 1000)] decimal FastBurnThreshold,
    [property: Range(1, 1000)] decimal SlowBurnThreshold,
    bool Enabled,
    long ExpectedVersion);

public sealed record MaintenanceWindowResponse(
    Guid Id, long Version, string Service, string Title,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string CreatedBy, DateTimeOffset CreatedAt, bool Active);

public sealed record CreateMaintenanceWindowRequest(
    [property: Required, MaxLength(80)] string Service,
    [property: Required, MaxLength(160)] string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record ReliabilitySignalResponse(
    Guid Id, long Version, string Service, ReliabilitySignalStatus Status,
    string SuggestedSeverity, int EvaluationWindowMinutes, decimal BurnRate,
    decimal ObservedAvailabilityPercent, decimal TargetAvailabilityPercent,
    string Summary, DateTimeOffset FirstObservedAt, DateTimeOffset LastObservedAt,
    DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy,
    string? IncidentId, string? SuppressionReason);

public sealed record EvaluateReliabilityResponse(
    DateTimeOffset EvaluatedAt, int ServicesEvaluated, int SignalsCreated,
    int SignalsUpdated, int SignalsSuppressed,
    IReadOnlyList<ReliabilitySignalResponse> Signals);

public sealed record AcknowledgeReliabilitySignalRequest(long ExpectedVersion);

public sealed record PromoteReliabilitySignalRequest(
    [property: Required, MaxLength(80)] string Assignee,
    long ExpectedVersion);

public sealed record PromoteReliabilitySignalResponse(
    ReliabilitySignalResponse Signal,
    IncidentResponse Incident);
