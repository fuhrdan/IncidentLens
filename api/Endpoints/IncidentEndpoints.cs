using System.Security.Claims;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace IncidentLens.Api.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents").WithTags("Incidents");

        group.MapGet("/", async ([AsParameters] IncidentQueryRequest query, IncidentService service, CancellationToken token) =>
        {
            try { return Results.Ok(await service.ListAsync(query, token)); }
            catch (IncidentQueryException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = [exception.Message] }); }
        })
            .RequireAuthorization("IncidentReader").WithName("ListIncidents");

        group.MapGet("/{id}", async (string id, IncidentService service, CancellationToken token) =>
        {
            var incident = await service.GetAsync(id, token);
            return incident is null ? Results.NotFound() : Results.Ok(incident);
        }).RequireAuthorization("IncidentReader").WithName("GetIncident");

        group.MapGet("/{id}/audit", async (
            string id, IncidentService service, CancellationToken token) =>
        {
            var audit = await service.ListAuditAsync(id, token);
            return audit is null ? Results.NotFound() : Results.Ok(audit);
        }).RequireAuthorization("IncidentCommander").WithName("ListIncidentAudit");

        group.MapPost("/", async (
            CreateIncidentRequest request,
            ClaimsPrincipal principal,
            IncidentService service,
            CancellationToken token) =>
        {
            if (IsBlank(request.Title, request.Summary, request.Service,
                    request.OwnerTeam, request.Assignee))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["incident"] = ["Title, summary, service, owner team, and assignee are required."],
                });
            }
            var created = await service.CreateAsync(request, Actor(principal), token);
            return Results.Created($"/api/incidents/{created.Id}", created);
        }).RequireAuthorization("IncidentCommander").WithName("CreateIncident");

        group.MapPatch("/{id}/status", (
            string id, UpdateStatusRequest request, ClaimsPrincipal principal,
            IncidentService service, CancellationToken token) =>
            RunMutationAsync(() => service.UpdateStatusAsync(
                id, request.Status, request.ExpectedVersion, Actor(principal), token)))
            .RequireAuthorization("IncidentCommander").WithName("UpdateIncidentStatus");

        group.MapPost("/{id}/notes", (
            string id, AddNoteRequest request, ClaimsPrincipal principal,
            IncidentService service, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Note))
            {
                return Task.FromResult<IResult>(Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["note"] = ["A note is required."] }));
            }
            return RunMutationAsync(() => service.AddNoteAsync(
                id, request.Note, request.ExpectedVersion, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("AddIncidentNote");

        group.MapPost("/{id}/timeline", (string id, AddTimelineEntryRequest request, ClaimsPrincipal principal,
            IncidentService service, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message)) return Task.FromResult<IResult>(Results.ValidationProblem(
                new Dictionary<string, string[]> { ["message"] = ["A timeline entry is required."] }));
            return RunMutationAsync(() => service.AddTimelineEntryAsync(id, request.Message, request.ExpectedVersion, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("AddTimelineEntry");

        group.MapPatch("/{id}/assignment", (
            string id, UpdateAssignmentRequest request, ClaimsPrincipal principal,
            IncidentService service, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Assignee))
            {
                return Task.FromResult<IResult>(Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["assignee"] = ["An assignee is required."] }));
            }
            return RunMutationAsync(() => service.UpdateAssignmentAsync(
                id, request.Assignee, request.ExpectedVersion, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("UpdateIncidentAssignment");

        group.MapPost("/{id}/responders", (
            string id, AddResponderRequest request, ClaimsPrincipal principal,
            IncidentService service, CancellationToken token) =>
        {
            if (IsBlank(request.Name, request.Role))
            {
                return Task.FromResult<IResult>(Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["responder"] = ["Name and role are required."] }));
            }
            return RunMutationAsync(() => service.AddResponderAsync(id, request.Name, request.Role,
                request.ExpectedVersion, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("AddIncidentResponder");

        group.MapDelete("/{id}/responders/{responderId:guid}", (
            string id, Guid responderId, long expectedVersion, ClaimsPrincipal principal,
            IncidentService service, CancellationToken token) =>
            RunMutationAsync(() => service.RemoveResponderAsync(id, responderId,
                expectedVersion, Actor(principal), token)))
            .RequireAuthorization("IncidentCommander").WithName("RemoveIncidentResponder");

        group.MapPut("/{id}/tags", (
            string id, UpdateTagsRequest request, ClaimsPrincipal principal,
            IncidentService service, CancellationToken token) =>
        {
            if (request.Tags.Count > 8 || request.Tags.Any(item => item.Length > 32))
            {
                return Task.FromResult<IResult>(Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["tags"] = ["Use at most 8 tags of 32 characters or fewer."] }));
            }
            return RunMutationAsync(() => service.UpdateTagsAsync(
                id, request.Tags, request.ExpectedVersion, Actor(principal), token));
        }).RequireAuthorization("IncidentCommander").WithName("UpdateIncidentTags");

        return endpoints;
    }

    private static async Task<IResult> RunMutationAsync(
        Func<Task<IncidentResponse?>> operation)
    {
        try
        {
            var updated = await operation();
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (IncidentConflictException exception)
        {
            return Results.Conflict(new ConflictResponse(
                "version_conflict",
                "The incident changed after it was loaded. Refresh and retry your change.",
                exception.Current));
        }
        catch (IncidentCommandException exception)
        { return Results.ValidationProblem(new Dictionary<string, string[]> { ["command"] = [exception.Message] }); }
    }

    private static string Actor(ClaimsPrincipal principal) =>
        principal.Identity?.Name ?? "Incident commander";

    private static bool IsBlank(params string[] values) => values.Any(string.IsNullOrWhiteSpace);
}
