using System.Text.Json;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Services;

public sealed class PostmortemService(
    IncidentLensDbContext database,
    TimeProvider clock,
    IncidentTelemetry telemetry,
    ILogger<PostmortemService> logger)
{
    public async Task<PostmortemResponse?> GetAsync(string incidentId, CancellationToken token)
    {
        var incident = await FindIncidentAsync(incidentId, false, token);
        return incident?.Postmortem is null ? null : Map(incident.Sequence, incident.Postmortem);
    }

    public async Task<PostmortemResponse?> UpsertAsync(string incidentId, UpsertPostmortemRequest request,
        string actor, CancellationToken token)
    {
        using var activity = telemetry.Activities.StartActivity("postmortem.upsert");
        var incident = await FindIncidentAsync(incidentId, true, token);
        if (incident is null) return null;
        var now = clock.GetUtcNow();
        var postmortem = incident.Postmortem;
        if (postmortem is null)
        {
            if (request.ExpectedVersion is not null) throw new PostmortemConflictException(null);
            postmortem = new PostmortemRecord { Id = Guid.NewGuid(), IncidentId = incident.Id,
                Owner = request.Owner.Trim(), ExecutiveSummary = request.ExecutiveSummary.Trim(),
                RootCause = request.RootCause.Trim(), Detection = request.Detection.Trim(),
                Resolution = request.Resolution.Trim(), LessonsLearned = request.LessonsLearned.Trim(),
                Status = request.Status, CreatedAt = now, UpdatedAt = now, Version = 1 };
            incident.Postmortem = postmortem; database.Postmortems.Add(postmortem);
        }
        else
        {
            if (request.ExpectedVersion != postmortem.Version) throw new PostmortemConflictException(Map(incident.Sequence, postmortem));
            postmortem.Owner = request.Owner.Trim(); postmortem.Status = request.Status;
            postmortem.ExecutiveSummary = request.ExecutiveSummary.Trim(); postmortem.RootCause = request.RootCause.Trim();
            postmortem.Detection = request.Detection.Trim(); postmortem.Resolution = request.Resolution.Trim();
            postmortem.LessonsLearned = request.LessonsLearned.Trim(); postmortem.UpdatedAt = now; postmortem.Version++;
        }
        await RecordChangeAsync(incident, actor, "postmortem.updated", $"Postmortem saved as {postmortem.Status}.", now, token);
        telemetry.PostmortemsUpdated.Add(1, new KeyValuePair<string, object?>("status", postmortem.Status.ToString()));
        logger.LogInformation("Postmortem {PostmortemId} updated for {IncidentId} by {Actor}", postmortem.Id, incidentId, actor);
        return Map(incident.Sequence, postmortem);
    }

    public async Task<PostmortemResponse?> AddActionItemAsync(string incidentId, AddActionItemRequest request,
        string actor, CancellationToken token)
    {
        var incident = await FindIncidentAsync(incidentId, true, token);
        if (incident?.Postmortem is null) return null;
        EnsureVersion(incident.Sequence, incident.Postmortem, request.ExpectedPostmortemVersion);
        var now = clock.GetUtcNow();
        incident.Postmortem.Version++; incident.Postmortem.UpdatedAt = now;
        var actionItem = new PostmortemActionItemRecord { Id = Guid.NewGuid(),
            PostmortemId = incident.Postmortem.Id, Title = request.Title.Trim(), Owner = request.Owner.Trim(),
            DueAt = request.DueAt, Status = ActionItemStatus.Open, CreatedAt = now };
        incident.Postmortem.ActionItems.Add(actionItem);
        database.PostmortemActionItems.Add(actionItem);
        await RecordChangeAsync(incident, actor, "postmortem.action.added", $"Action item assigned to {request.Owner.Trim()}.", now, token);
        return Map(incident.Sequence, incident.Postmortem);
    }

    public async Task<PostmortemResponse?> UpdateActionItemAsync(string incidentId, Guid actionId,
        UpdateActionItemRequest request, string actor, CancellationToken token)
    {
        var incident = await FindIncidentAsync(incidentId, true, token);
        if (incident?.Postmortem is null) return null;
        EnsureVersion(incident.Sequence, incident.Postmortem, request.ExpectedPostmortemVersion);
        var action = incident.Postmortem.ActionItems.SingleOrDefault(item => item.Id == actionId);
        if (action is null) return null;
        var now = clock.GetUtcNow(); action.Status = request.Status; action.Owner = request.Owner.Trim(); action.DueAt = request.DueAt;
        action.CompletedAt = request.Status == ActionItemStatus.Done ? now : null;
        incident.Postmortem.Version++; incident.Postmortem.UpdatedAt = now;
        await RecordChangeAsync(incident, actor, "postmortem.action.updated", $"Action item moved to {request.Status}.", now, token);
        return Map(incident.Sequence, incident.Postmortem);
    }

    private async Task RecordChangeAsync(IncidentRecord incident, string actor, string action,
        string message, DateTimeOffset now, CancellationToken token)
    {
        incident.Version++; incident.UpdatedAt = now;
        var timelineEvent = new TimelineEventRecord { Id = Guid.NewGuid(), IncidentId = incident.Id,
            Actor = actor, Type = TimelineEventType.Postmortem, Message = message, OccurredAt = now };
        var auditRecord = new AuditRecord { Id = Guid.NewGuid(), IncidentId = incident.Id,
            Actor = actor, Action = action, Details = message, IncidentVersion = incident.Version, OccurredAt = now };
        incident.Timeline.Add(timelineEvent);
        incident.AuditRecords.Add(auditRecord);
        database.TimelineEvents.Add(timelineEvent);
        database.AuditRecords.Add(auditRecord);
        var changed = new IncidentChangedEvent($"INC-{incident.Sequence}", incident.Version, action, now);
        database.OutboxMessages.Add(new OutboxMessage { Id = Guid.NewGuid(), OccurredAt = now,
            Type = "incident.changed", AggregateId = changed.IncidentId, Payload = JsonSerializer.Serialize(changed), NextAttemptAt = now });
        try { await database.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear(); var current = await FindIncidentAsync($"INC-{incident.Sequence}", false, token);
            throw new PostmortemConflictException(current?.Postmortem is null ? null : Map(incident.Sequence, current.Postmortem));
        }
    }

    private Task<IncidentRecord?> FindIncidentAsync(string id, bool tracking, CancellationToken token)
    {
        if (!int.TryParse(id.Replace("INC-", "", StringComparison.OrdinalIgnoreCase), out var sequence)) return Task.FromResult<IncidentRecord?>(null);
        IQueryable<IncidentRecord> query = database.Incidents.Include(item => item.Postmortem)!
            .ThenInclude(item => item!.ActionItems).Include(item => item.Timeline).Include(item => item.AuditRecords).AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(item => item.Sequence == sequence, token);
    }

    private static void EnsureVersion(int sequence, PostmortemRecord record, long expected)
    { if (record.Version != expected) throw new PostmortemConflictException(Map(sequence, record)); }
    public static PostmortemResponse Map(int sequence, PostmortemRecord record) => new(record.Id,
        $"INC-{sequence}", record.Version, record.Status, record.Owner, record.ExecutiveSummary,
        record.RootCause, record.Detection, record.Resolution, record.LessonsLearned,
        record.CreatedAt, record.UpdatedAt, record.ActionItems.OrderBy(item => item.DueAt)
            .Select(item => new PostmortemActionItemResponse(item.Id, item.Title, item.Owner,
                item.Status, item.DueAt, item.CreatedAt, item.CompletedAt)).ToList());
}

public sealed class PostmortemConflictException(PostmortemResponse? current) : Exception
{ public PostmortemResponse? Current { get; } = current; }
