using IncidentLens.Api.Services;

namespace IncidentLens.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/analytics/overview", async (
            int? days, AnalyticsService service, CancellationToken token) =>
            Results.Ok(await service.GetOverviewAsync(days ?? 30, token)))
            .RequireAuthorization("IncidentReader")
            .WithTags("Analytics")
            .WithName("GetAnalyticsOverview");

        return endpoints;
    }
}
