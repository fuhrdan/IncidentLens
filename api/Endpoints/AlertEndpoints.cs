using System.Security.Claims;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Services;

namespace IncidentLens.Api.Endpoints;
public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/alerts", async (AlertIngestionRequest request, HttpRequest httpRequest,
            ClaimsPrincipal principal, IncidentService service, CancellationToken token) =>
        {
            var key = httpRequest.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 120) return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Supply an Idempotency-Key header of 120 characters or fewer."] });
            if (new[] { request.Title, request.Summary, request.Service, request.OwnerTeam, request.Assignee }.Any(string.IsNullOrWhiteSpace))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["alert"] = ["Title, summary, service, owner team, and assignee are required."] });
            if (request.Tags is { Count: > 8 } || request.Tags?.Any(item => item.Length > 32) == true)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["tags"] = ["Use at most 8 tags of 32 characters or fewer."] });
            var result = await service.IngestAlertAsync(request, key, principal.Identity?.Name ?? "Alert integration", token);
            return result.Replayed ? Results.Ok(result) : Results.Created($"/api/incidents/{result.Incident.Id}", result);
        }).RequireAuthorization("IncidentCommander").WithTags("Alerts").WithName("IngestAlert");
        return endpoints;
    }
}
