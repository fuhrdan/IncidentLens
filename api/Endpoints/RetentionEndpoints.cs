using IncidentLens.Api.Services;

namespace IncidentLens.Api.Endpoints;

/// <summary>Commander-only, tenant-local preview and explicitly confirmed cleanup.</summary>
public static class RetentionEndpoints
{
    public sealed record CleanupRequest(string? Confirm);

    public static IEndpointRouteBuilder MapRetentionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/retention").WithTags("Data protection")
            .RequireAuthorization("IncidentCommander");
        group.MapGet("/policy", (RetentionService service) => Results.Ok(service.GetPolicy()))
            .WithName("GetRetentionPolicy");
        group.MapGet("/preview", async (RetentionService service, CancellationToken token) =>
            Results.Ok(await service.PreviewAsync(token))).WithName("PreviewRetention");
        group.MapPost("/run", async Task<IResult> (CleanupRequest request, RetentionService service,
            CancellationToken token) =>
        {
            if (!string.Equals(request.Confirm, "PURGE_ELIGIBLE_DATA", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Confirm must be PURGE_ELIGIBLE_DATA." });
            if (!service.GetPolicy().PurgeEnabled)
                return Results.Conflict(new { error = "Retention cleanup is disabled by deployment policy." });
            return Results.Ok(await service.RunAsync(token));
        }).WithName("RunRetention");
        return endpoints;
    }
}
