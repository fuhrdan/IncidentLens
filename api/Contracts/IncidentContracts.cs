using System.ComponentModel.DataAnnotations;
using IncidentLens.Api.Domain;

namespace IncidentLens.Api.Contracts;

public sealed record CreateIncidentRequest(
    [property: Required, MaxLength(120)] string Title,
    [property: Required, MaxLength(500)] string Summary,
    Severity Severity,
    [property: Required, MaxLength(80)] string Service,
    [property: Required, MaxLength(80)] string OwnerTeam,
    [property: Required, MaxLength(80)] string Assignee);

public sealed record UpdateStatusRequest(IncidentStatus Status, long ExpectedVersion);

public sealed record AddNoteRequest(
    [property: Required, MaxLength(500)] string Note,
    long ExpectedVersion);

public sealed record AddTimelineEntryRequest(
    [property: Required, MaxLength(500)] string Message,
    long ExpectedVersion);

public sealed record UpdateAssignmentRequest(
    [property: Required, MaxLength(80)] string Assignee,
    long ExpectedVersion);

public sealed record AddResponderRequest(
    [property: Required, MaxLength(80)] string Name,
    [property: Required, MaxLength(60)] string Role,
    long ExpectedVersion);

public sealed record UpdateTagsRequest(
    IReadOnlyList<string> Tags,
    long ExpectedVersion);

public sealed record IncidentResponse(
    string Id,
    long Version,
    string Title,
    string Summary,
    string Severity,
    IncidentStatus Status,
    string Service,
    string OwnerTeam,
    string Assignee,
    DateTimeOffset DeclaredAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    int AffectedCustomers,
    IReadOnlyList<ResponderResponse> Responders,
    IReadOnlyList<string> Tags,
    IReadOnlyList<TimelineEventResponse> Timeline);

public sealed record ResponderResponse(
    Guid Id,
    string Name,
    string Role,
    DateTimeOffset JoinedAt);

public sealed record TimelineEventResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string Type,
    string Message,
    TimelineCommandResponse? Command,
    IReadOnlyList<string> Mentions);

public sealed record TimelineCommandResponse(string Name, string Arguments);

public sealed class IncidentQueryRequest
{
    public int? Page { get; init; } = 1;
    public int? PageSize { get; init; } = 20;
    public string? Query { get; init; }
    public string? Severity { get; init; }
    public IncidentStatus? Status { get; init; }
    public string? OwnerTeam { get; init; }
}

public sealed record PagedIncidentResponse(IReadOnlyList<IncidentResponse> Items,
    int Total, int Page, int PageSize, int TotalPages);

public sealed record AlertIngestionRequest(
    [property: Required, MaxLength(120)] string Title,
    [property: Required, MaxLength(500)] string Summary,
    Severity Severity,
    [property: Required, MaxLength(80)] string Service,
    [property: Required, MaxLength(80)] string OwnerTeam,
    [property: Required, MaxLength(80)] string Assignee,
    [property: Range(0, int.MaxValue)] int AffectedCustomers,
    IReadOnlyList<string>? Tags);

public sealed record AlertIngestionResponse(IncidentResponse Incident, bool Replayed);
public sealed record IncidentChangedEvent(string IncidentId, long Version, string Kind, DateTimeOffset OccurredAt);

public sealed record AuditResponse(
    Guid Id,
    string IncidentId,
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string Details,
    long IncidentVersion);

public sealed record ConflictResponse(
    string Code,
    string Message,
    IncidentResponse Current);
