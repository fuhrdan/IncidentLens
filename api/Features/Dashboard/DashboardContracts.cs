using IncidentLens.Api.Contracts;
namespace IncidentLens.Api.Features.Dashboard;

/// <summary>Read-only, tenant-scoped snapshot consumed by the operations command center.</summary>
public sealed record DashboardSummary(
    int ActiveIncidents,
    int CriticalIncidents,
    decimal? MttaMinutes,
    decimal? MttrMinutes,
    int ServicesAtRisk);

public sealed record DashboardIncident(
    string Id,
    string Title,
    string Severity,
    string Status,
    string OwnerTeam,
    string Assignee,
    string Service,
    int ResponderCount,
    DateTimeOffset DeclaredAt);

/// <summary>
/// A recent incident timeline event for the Operations Command Center.
/// IncidentId is retained so the UI can link to the incident workspace.
/// </summary>
public sealed record DashboardActivity(
    Guid Id,
    string IncidentId,
    string IncidentTitle,
    string Service,
    string Actor,
    string Type,
    string Message,
    DateTimeOffset OccurredAt);

public sealed record DashboardOverview(
    DateTimeOffset GeneratedAt,
    int WindowDays,
    DashboardSummary Summary,
    IReadOnlyList<DashboardIncident> ActiveIncidents,
    IReadOnlyList<ServiceHealthResponse> Services,
    IReadOnlyList<DashboardActivity> RecentActivity);