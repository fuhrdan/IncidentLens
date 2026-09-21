namespace IncidentLens.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/overview", async (
            int? days, DashboardOverviewService dashboard, CancellationToken token) =>
            Results.Ok(await dashboard.GetAsync(days ?? 7, token)))
            .RequireAuthorization("IncidentReader")
            .WithTags("Dashboard")
            .WithName("GetDashboardOverview");
        return endpoints;
    }
}
