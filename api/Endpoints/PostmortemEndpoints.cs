using System.Security.Claims;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Services;

namespace IncidentLens.Api.Endpoints;

public static class PostmortemEndpoints
{
    public static IEndpointRouteBuilder MapPostmortemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents/{id}").WithTags("Postmortems");

        group.MapGet("/postmortem", async (string id, PostmortemService service, CancellationToken token) =>
        {
            var postmortem = await service.GetAsync(id, token);
            return postmortem is null ? Results.NotFound() : Results.Ok(postmortem);
        }).RequireAuthorization("IncidentReader").WithName("GetPostmortem");

        group.MapPut("/postmortem", (string id, UpsertPostmortemRequest request,
            ClaimsPrincipal principal, PostmortemService service, CancellationToken token) =>
        {
            if (IsBlank(request.Owner, request.ExecutiveSummary, request.RootCause,
                    request.Detection, request.Resolution, request.LessonsLearned))
            {
                return Task.FromResult<IResult>(Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["postmortem"] = ["Owner and every postmortem narrative field are required."],
                }));
            }
            return RunMutationAsync(() => service.UpsertAsync(id, request, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("UpsertPostmortem");

        group.MapPost("/postmortem/actions", (string id, AddActionItemRequest request,
            ClaimsPrincipal principal, PostmortemService service, CancellationToken token) =>
        {
            if (IsBlank(request.Title, request.Owner))
            {
                return Task.FromResult<IResult>(Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["actionItem"] = ["Title and owner are required."],
                }));
            }
            return RunMutationAsync(() => service.AddActionItemAsync(id, request, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("AddPostmortemActionItem");

        group.MapPatch("/postmortem/actions/{actionId:guid}", (string id, Guid actionId,
            UpdateActionItemRequest request, ClaimsPrincipal principal, PostmortemService service,
            CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Owner))
            {
                return Task.FromResult<IResult>(Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["owner"] = ["An action item owner is required."],
                }));
            }
            return RunMutationAsync(() => service.UpdateActionItemAsync(
                id, actionId, request, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("UpdatePostmortemActionItem");

        group.MapGet("/export", async (string id, string? format,
            EvidenceExportService service, CancellationToken token) =>
        {
            var normalized = format?.ToLowerInvariant() ?? "json";
            if (normalized is not ("json" or "csv"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["format"] = ["Format must be json or csv."],
                });
            }
            var export = await service.ExportAsync(id, normalized, token);
            return export is null
                ? Results.NotFound()
                : Results.File(export.Content, export.ContentType, export.FileName);
        }).RequireAuthorization("IncidentCommander").WithName("ExportIncidentEvidence");

        return endpoints;
    }

    private static async Task<IResult> RunMutationAsync(Func<Task<PostmortemResponse?>> operation)
    {
        try
        {
            var updated = await operation();
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (PostmortemConflictException exception)
        {
            return Results.Conflict(new
            {
                code = "postmortem_version_conflict",
                message = "The postmortem changed after it was loaded. Refresh and retry your change.",
                current = exception.Current,
            });
        }
    }

    private static string Actor(ClaimsPrincipal principal) =>
        principal.Identity?.Name ?? "Incident commander";

    private static bool IsBlank(params string[] values) => values.Any(string.IsNullOrWhiteSpace);
}
