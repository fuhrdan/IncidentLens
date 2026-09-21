using System.Security.Claims;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Services;

namespace IncidentLens.Api.Endpoints;

public static class ReliabilityEndpoints
{
    public static IEndpointRouteBuilder MapReliabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reliability").WithTags("Reliability automation");

        group.MapGet("/objectives", async (ReliabilityAutomationService service, CancellationToken token) =>
            Results.Ok(await service.ListObjectivesAsync(token)))
            .RequireAuthorization("IncidentReader").WithName("ListServiceObjectives");

        group.MapPut("/objectives/{id:guid}", (Guid id, UpdateServiceObjectiveRequest request,
            ReliabilityAutomationService service, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.OwnerTeam))
                return Task.FromResult<IResult>(Validation("ownerTeam", "An owning team is required."));
            if (request.AvailabilityTargetPercent is < 90m or > 100m ||
                request.AcknowledgementTargetMinutes < 1 || request.ResolutionTargetMinutes < 1 ||
                request.MonthlyErrorBudgetMinutes < 1 || request.FastBurnThreshold < 1 ||
                request.SlowBurnThreshold < 1)
                return Task.FromResult<IResult>(Validation("objective", "Targets and thresholds must be within their documented positive ranges."));
            if (request.SlowBurnThreshold > request.FastBurnThreshold)
                return Task.FromResult<IResult>(Validation("slowBurnThreshold",
                    "The slow-burn threshold cannot exceed the fast-burn threshold."));
            return RunMutationAsync(async () =>
            {
                var objective = await service.UpdateObjectiveAsync(id, request, token);
                return objective is null ? Results.NotFound() : Results.Ok(objective);
            });
        }).RequireAuthorization("IncidentCommander").WithName("UpdateServiceObjective");

        group.MapGet("/maintenance-windows", async (ReliabilityAutomationService service,
            CancellationToken token) => Results.Ok(await service.ListMaintenanceWindowsAsync(token)))
            .RequireAuthorization("IncidentReader").WithName("ListMaintenanceWindows");

        group.MapPost("/maintenance-windows", (CreateMaintenanceWindowRequest request,
            ClaimsPrincipal principal, ReliabilityAutomationService service, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Service) || string.IsNullOrWhiteSpace(request.Title))
                return Task.FromResult<IResult>(Validation("maintenanceWindow", "Service and title are required."));
            if (request.EndsAt <= request.StartsAt)
                return Task.FromResult<IResult>(Validation("endsAt", "The end must be after the start."));
            if (request.EndsAt - request.StartsAt > TimeSpan.FromDays(14))
                return Task.FromResult<IResult>(Validation("endsAt", "A maintenance window cannot exceed 14 days."));
            return RunMutationAsync(async () => Results.Created("/api/reliability/maintenance-windows",
                await service.CreateMaintenanceWindowAsync(request, Actor(principal), token)));
        }).RequireAuthorization("IncidentCommander").WithName("CreateMaintenanceWindow");

        group.MapDelete("/maintenance-windows/{id:guid}", (Guid id, long expectedVersion,
            ReliabilityAutomationService service, CancellationToken token) =>
            RunMutationAsync(async () => await service.DeleteMaintenanceWindowAsync(id, expectedVersion, token) is null
                ? Results.NotFound() : Results.NoContent()))
            .RequireAuthorization("IncidentCommander").WithName("DeleteMaintenanceWindow");

        group.MapGet("/signals", async (ReliabilitySignalStatus? status,
            ReliabilityAutomationService service, CancellationToken token) =>
            Results.Ok(await service.ListSignalsAsync(status, token)))
            .RequireAuthorization("IncidentReader").WithName("ListReliabilitySignals");

        group.MapPost("/evaluate", async (ReliabilityAutomationService service, CancellationToken token) =>
            Results.Ok(await service.EvaluateAsync(token)))
            .RequireAuthorization("IncidentCommander").WithName("EvaluateReliability");

        group.MapPatch("/signals/{id:guid}/acknowledge", (Guid id,
            AcknowledgeReliabilitySignalRequest request, ClaimsPrincipal principal,
            ReliabilityAutomationService service, CancellationToken token) =>
            RunMutationAsync(async () =>
            {
                var signal = await service.AcknowledgeAsync(id, request.ExpectedVersion, Actor(principal), token);
                return signal is null ? Results.NotFound() : Results.Ok(signal);
            })).RequireAuthorization("IncidentCommander").WithName("AcknowledgeReliabilitySignal");

        group.MapPost("/signals/{id:guid}/promote", (Guid id,
            PromoteReliabilitySignalRequest request, ClaimsPrincipal principal,
            ReliabilityAutomationService service, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Assignee))
                return Task.FromResult<IResult>(Validation("assignee", "An incident commander is required."));
            return RunMutationAsync(async () =>
            {
                var promoted = await service.PromoteAsync(id, request, Actor(principal), token);
                return promoted is null ? Results.NotFound() : Results.Ok(promoted);
            });
        }).RequireAuthorization("IncidentCommander").WithName("PromoteReliabilitySignal");

        return endpoints;
    }

    private static async Task<IResult> RunMutationAsync(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (ReliabilityConflictException exception)
        {
            return Results.Conflict(new { code = "reliability_version_conflict",
                message = "The reliability record changed after it was loaded. Refresh and retry.",
                current = exception.Current });
        }
        catch (ReliabilityCommandException exception)
        { return Validation("signal", exception.Message); }
    }

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string Actor(ClaimsPrincipal principal) =>
        principal.Identity?.Name ?? "Incident commander";
}
