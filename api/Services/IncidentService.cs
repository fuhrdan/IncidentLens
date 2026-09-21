using IncidentLens.Api.Contracts;
using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Observability;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IncidentLens.Api.Services;

/// <summary>
/// Owns incident workflow rules. Every successful mutation increments Version,
/// adds a human-readable timeline event, and appends a durable audit record.
/// </summary>
public sealed partial class IncidentService(
    IncidentLensDbContext database,
    TimeProvider clock,
    IncidentTelemetry telemetry,
    ILogger<IncidentService> logger)
{
    public async Task<PagedIncidentResponse> ListAsync(IncidentQueryRequest request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page ?? 1);
        var pageSize = Math.Clamp(request.PageSize ?? 20, 1, 100);
        var query = IncidentQuery(tracking: false);
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var text = request.Query.Trim().ToLowerInvariant();
            var sequenceMatch = TryParseSequence(text, out var sequence);
            query = query.Where(item => (sequenceMatch && item.Sequence == sequence) ||
                item.Title.ToLower().Contains(text) || item.Service.ToLower().Contains(text) ||
                item.OwnerTeam.ToLower().Contains(text) || item.Assignee.ToLower().Contains(text) ||
                item.Tags.Any(tag => tag.Value.ToLower().Contains(text)));
        }
        if (!string.IsNullOrWhiteSpace(request.Severity))
        {
            var severity = ParseSeverity(request.Severity) ?? throw new IncidentQueryException("Severity must be SEV-1, SEV-2, SEV-3, or SEV-4.");
            query = query.Where(item => item.Severity == severity);
        }
        if (request.Status is not null) query = query.Where(item => item.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.OwnerTeam))
        {
            var team = request.OwnerTeam.Trim().ToLowerInvariant();
            query = query.Where(item => item.OwnerTeam.ToLower() == team);
        }
        var total = await query.CountAsync(cancellationToken);
        var ordered = database.Database.IsSqlite() ? query.OrderByDescending(item => item.Sequence)
            : query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Sequence);
        var records = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedIncidentResponse(records.Select(Map).ToList(), total, page, pageSize,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<IncidentResponse?> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (!TryParseSequence(id, out var sequence))
        {
            return null;
        }
        var record = await FindAsync(sequence, tracking: false, cancellationToken);
        return record is null ? null : Map(record);
    }

    public async Task<IncidentResponse> CreateAsync(
        CreateIncidentRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        using var activity = telemetry.Activities.StartActivity("incident.create");
        var now = clock.GetUtcNow();
        var lastSequence = await database.Incidents.MaxAsync(
            item => (int?)item.Sequence, cancellationToken) ?? 1042;
        var record = new IncidentRecord
        {
            Id = Guid.NewGuid(),
            Sequence = lastSequence + 1,
            Version = 1,
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Severity = request.Severity,
            Status = IncidentStatus.Investigating,
            Service = request.Service.Trim(),
            OwnerTeam = request.OwnerTeam.Trim(),
            Assignee = request.Assignee.Trim(),
            DeclaredAt = now,
            UpdatedAt = now,
            AffectedCustomers = 0,
        };
        record.Responders.Add(new IncidentResponderRecord
        {
            Id = Guid.NewGuid(),
            IncidentId = record.Id,
            Name = record.Assignee,
            Role = "Incident commander",
            JoinedAt = now,
        });
        record.Timeline.Add(CreateEvent(record.Id, actor, TimelineEventType.Declared,
            "Incident declared from the IncidentLens console.", now));
        record.AuditRecords.Add(CreateAudit(record.Id, actor, "incident.declared",
            $"Declared {SeverityLabel(record.Severity)} incident for {record.Service}.", 1, now));
        RecordOutbox(record, "declared", now);

        database.Incidents.Add(record);
        await database.SaveChangesAsync(cancellationToken);
        telemetry.Mutations.Add(1, new KeyValuePair<string, object?>("kind", "declared"));
        activity?.SetTag("incident.id", $"INC-{record.Sequence}");
        logger.LogInformation("Incident {IncidentId} declared for {Service} by {Actor}",
            $"INC-{record.Sequence}", record.Service, actor);
        return Map(record);
    }

    public async Task<AlertIngestionResponse> IngestAlertAsync(AlertIngestionRequest request,
        string idempotencyKey, string actor, CancellationToken cancellationToken)
    {
        var key = idempotencyKey.Trim();
        var replay = await database.AlertIngestions.AsNoTracking().Where(item => item.IdempotencyKey == key)
            .Select(item => (Guid?)item.IncidentId).SingleOrDefaultAsync(cancellationToken);
        if (replay is not null) return new(Map(await FindByIdAsync(replay.Value, false, cancellationToken)), true);
        using var activity = telemetry.Activities.StartActivity("alert.ingest");
        var now = clock.GetUtcNow();
        var last = await database.Incidents.MaxAsync(item => (int?)item.Sequence, cancellationToken) ?? 1042;
        var record = new IncidentRecord { Id = Guid.NewGuid(), Sequence = last + 1, Version = 1,
            Title = request.Title.Trim(), Summary = request.Summary.Trim(), Severity = request.Severity,
            Status = IncidentStatus.Investigating, Service = request.Service.Trim(), OwnerTeam = request.OwnerTeam.Trim(),
            Assignee = request.Assignee.Trim(), DeclaredAt = now, UpdatedAt = now, AffectedCustomers = request.AffectedCustomers };
        record.Responders.Add(new() { Id = Guid.NewGuid(), IncidentId = record.Id, Name = record.Assignee, Role = "Incident commander", JoinedAt = now });
        foreach (var tag in NormalizeTags(request.Tags ?? [])) record.Tags.Add(new() { Id = Guid.NewGuid(), IncidentId = record.Id, Value = tag });
        record.Timeline.Add(CreateEvent(record.Id, actor, TimelineEventType.Declared, "Incident declared by the alert ingestion API.", now));
        record.AuditRecords.Add(CreateAudit(record.Id, actor, "alert.ingested", $"Accepted alert key {key} for {record.Service}.", 1, now));
        database.AlertIngestions.Add(new() { Id = Guid.NewGuid(), IdempotencyKey = key, IncidentId = record.Id, Incident = record, ReceivedAt = now });
        database.Incidents.Add(record); RecordOutbox(record, "alert.ingested", now);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            telemetry.AlertsIngested.Add(1, new KeyValuePair<string, object?>("service", record.Service));
            activity?.SetTag("incident.id", $"INC-{record.Sequence}");
            logger.LogInformation("Alert {IdempotencyKey} created incident {IncidentId}", key, $"INC-{record.Sequence}");
            return new(Map(record), false);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var existing = await database.AlertIngestions.AsNoTracking().Where(item => item.IdempotencyKey == key)
                .Select(item => (Guid?)item.IncidentId).SingleOrDefaultAsync(cancellationToken);
            if (existing is null) throw;
            return new(Map(await FindByIdAsync(existing.Value, false, cancellationToken)), true);
        }
    }

    public async Task<IncidentResponse?> UpdateStatusAsync(
        string id,
        IncidentStatus status,
        long expectedVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var record = await FindForMutationAsync(id, cancellationToken);
        if (record is null) return null;
        EnsureVersion(record, expectedVersion);

        var previous = record.Status;
        ApplyStatusTimestamps(record, previous, status);
        record.Status = status;
        return await SaveMutationAsync(record, actor, TimelineEventType.Status,
            $"Status changed from {previous} to {status}.", "incident.status.changed",
            $"{previous} -> {status}", cancellationToken);
    }

    public async Task<IncidentResponse?> AddNoteAsync(
        string id,
        string note,
        long expectedVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var record = await FindForMutationAsync(id, cancellationToken);
        if (record is null) return null;
        EnsureVersion(record, expectedVersion);

        return await SaveMutationAsync(record, actor, TimelineEventType.Note,
            note.Trim(), "incident.note.added", "Operational note added.", cancellationToken);
    }

    public async Task<IncidentResponse?> AddTimelineEntryAsync(string id, string message,
        long expectedVersion, string actor, CancellationToken cancellationToken)
    {
        var record = await FindForMutationAsync(id, cancellationToken);
        if (record is null) return null;
        EnsureVersion(record, expectedVersion);
        var text = message.Trim();
        var mentions = MentionPattern().Matches(text).Cast<Match>().Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!text.StartsWith('/')) return await SaveMutationAsync(record, actor, TimelineEventType.Note,
            text, "incident.note.added", "Operational note added.", cancellationToken, mentions: mentions);
        var parts = text[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts.ElementAtOrDefault(0)?.ToLowerInvariant() ?? string.Empty;
        var arguments = parts.ElementAtOrDefault(1) ?? string.Empty;
        string auditDetails;
        switch (name)
        {
            case "status" when Enum.TryParse<IncidentStatus>(arguments, true, out var status):
                var oldStatus = record.Status; ApplyStatusTimestamps(record, oldStatus, status);
                record.Status = status; auditDetails = $"{oldStatus} -> {status}"; break;
            case "assign" when !string.IsNullOrWhiteSpace(arguments):
                var oldAssignee = record.Assignee; record.Assignee = arguments; auditDetails = $"{oldAssignee} -> {record.Assignee}"; break;
            case "note" when !string.IsNullOrWhiteSpace(arguments): auditDetails = "Structured note recorded."; break;
            default: throw new IncidentCommandException("Use /status <Investigating|Identified|Monitoring|Resolved>, /assign <name>, or /note <text>.");
        }
        return await SaveMutationAsync(record, actor, TimelineEventType.Command, text,
            "incident.command.executed", auditDetails, cancellationToken, name, arguments, mentions);
    }

    public async Task<IncidentResponse?> UpdateAssignmentAsync(
        string id,
        string assignee,
        long expectedVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var record = await FindForMutationAsync(id, cancellationToken);
        if (record is null) return null;
        EnsureVersion(record, expectedVersion);

        var previous = record.Assignee;
        record.Assignee = assignee.Trim();
        return await SaveMutationAsync(record, actor, TimelineEventType.Assignment,
            $"Incident command transferred from {previous} to {record.Assignee}.",
            "incident.assignment.changed", $"{previous} -> {record.Assignee}", cancellationToken);
    }

    public async Task<IncidentResponse?> AddResponderAsync(
        string id,
        string name,
        string role,
        long expectedVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var record = await FindForMutationAsync(id, cancellationToken);
        if (record is null) return null;
        EnsureVersion(record, expectedVersion);

        var responderName = name.Trim();
        if (record.Responders.Any(item =>
            item.Name.Equals(responderName, StringComparison.OrdinalIgnoreCase)))
        {
            return Map(record);
        }

        var responder = new IncidentResponderRecord
        {
            Id = Guid.NewGuid(),
            IncidentId = record.Id,
            Name = responderName,
            Role = role.Trim(),
            JoinedAt = clock.GetUtcNow(),
        };
        record.Responders.Add(responder);
        database.Responders.Add(responder);
        return await SaveMutationAsync(record, actor, TimelineEventType.Responder,
            $"{responder.Name} joined as {responder.Role}.", "incident.responder.added",
            $"Added {responder.Name} ({responder.Role}).", cancellationToken);
    }

    public async Task<IncidentResponse?> RemoveResponderAsync(
        string id,
        Guid responderId,
        long expectedVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var record = await FindForMutationAsync(id, cancellationToken);
        if (record is null) return null;
        EnsureVersion(record, expectedVersion);

        var responder = record.Responders.SingleOrDefault(item => item.Id == responderId);
        if (responder is null)
        {
            return Map(record);
        }
        record.Responders.Remove(responder);
        database.Responders.Remove(responder);
        return await SaveMutationAsync(record, actor, TimelineEventType.Responder,
            $"{responder.Name} left the response team.", "incident.responder.removed",
            $"Removed {responder.Name} ({responder.Role}).", cancellationToken);
    }

    public async Task<IncidentResponse?> UpdateTagsAsync(
        string id,
        IReadOnlyList<string> tags,
        long expectedVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var record = await FindForMutationAsync(id, cancellationToken);
        if (record is null) return null;
        EnsureVersion(record, expectedVersion);

        var normalized = NormalizeTags(tags);
        database.Tags.RemoveRange(record.Tags);
        record.Tags.Clear();
        foreach (var value in normalized)
        {
            var tag = new IncidentTagRecord
            {
                Id = Guid.NewGuid(),
                IncidentId = record.Id,
                Value = value,
            };
            record.Tags.Add(tag);
            database.Tags.Add(tag);
        }
        var display = normalized.Count == 0 ? "none" : string.Join(", ", normalized);
        return await SaveMutationAsync(record, actor, TimelineEventType.Tags,
            $"Incident tags updated: {display}.", "incident.tags.changed",
            $"Tags set to: {display}.", cancellationToken);
    }

    public async Task<IReadOnlyList<AuditResponse>?> ListAuditAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!TryParseSequence(id, out var sequence)) return null;
        var incidentId = await database.Incidents.AsNoTracking()
            .Where(item => item.Sequence == sequence)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (incidentId is null) return null;

        var records = await database.AuditRecords.AsNoTracking()
            .Where(item => item.IncidentId == incidentId.Value)
            .ToListAsync(cancellationToken);
        return records.OrderByDescending(item => item.OccurredAt)
            .Select(item => new AuditResponse(item.Id, id.ToUpperInvariant(), item.OccurredAt,
                item.Actor, item.Action, item.Details, item.IncidentVersion))
            .ToList();
    }

    private async Task<IncidentResponse> SaveMutationAsync(
        IncidentRecord record,
        string actor,
        TimelineEventType eventType,
        string message,
        string action,
        string auditDetails,
        CancellationToken cancellationToken, string? commandName = null,
        string? commandArguments = null, IReadOnlyList<string>? mentions = null)
    {
        using var activity = telemetry.Activities.StartActivity("incident.mutate");
        var now = clock.GetUtcNow();
        record.Version++;
        record.UpdatedAt = now;
        var timelineEvent = CreateEvent(record.Id, actor, eventType, message, now, commandName, commandArguments, mentions);
        var audit = CreateAudit(record.Id, actor, action, auditDetails, record.Version, now);
        record.Timeline.Add(timelineEvent);
        record.AuditRecords.Add(audit);
        database.TimelineEvents.Add(timelineEvent);
        database.AuditRecords.Add(audit);
        RecordOutbox(record, action, now);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var sequence = record.Sequence;
            database.ChangeTracker.Clear();
            var current = await FindAsync(sequence, tracking: false, cancellationToken)
                ?? throw new InvalidOperationException("The conflicting incident no longer exists.");
            throw new IncidentConflictException(Map(current));
        }
        telemetry.Mutations.Add(1, new KeyValuePair<string, object?>("kind", action));
        activity?.SetTag("incident.id", $"INC-{record.Sequence}");
        activity?.SetTag("incident.version", record.Version);
        logger.LogInformation("Incident {IncidentId} mutation {Action} committed at version {Version} by {Actor}",
            $"INC-{record.Sequence}", action, record.Version, actor);
        return Map(record);
    }

    private void ApplyStatusTimestamps(
        IncidentRecord record,
        IncidentStatus previous,
        IncidentStatus next)
    {
        var now = clock.GetUtcNow();
        if (record.AcknowledgedAt is null && next != IncidentStatus.Investigating)
        {
            record.AcknowledgedAt = now;
        }
        if (next == IncidentStatus.Resolved)
        {
            record.ResolvedAt ??= now;
        }
        else if (previous == IncidentStatus.Resolved)
        {
            record.ResolvedAt = null;
        }
    }

    private async Task<IncidentRecord?> FindForMutationAsync(
        string id,
        CancellationToken cancellationToken)
    {
        return TryParseSequence(id, out var sequence)
            ? await FindAsync(sequence, tracking: true, cancellationToken)
            : null;
    }

    private Task<IncidentRecord?> FindAsync(
        int sequence,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = IncidentQuery(tracking);
        return query.SingleOrDefaultAsync(item => item.Sequence == sequence, cancellationToken);
    }

    private Task<IncidentRecord> FindByIdAsync(Guid id, bool tracking, CancellationToken token) =>
        IncidentQuery(tracking).SingleAsync(item => item.Id == id, token);

    private IQueryable<IncidentRecord> IncidentQuery(bool tracking)
    {
        IQueryable<IncidentRecord> query = database.Incidents
            .Include(item => item.Timeline)
            .Include(item => item.Responders)
            .Include(item => item.Tags)
            .AsSplitQuery();
        return tracking ? query : query.AsNoTracking();
    }

    private static void EnsureVersion(IncidentRecord record, long expectedVersion)
    {
        if (record.Version != expectedVersion)
        {
            throw new IncidentConflictException(Map(record));
        }
    }

    private static TimelineEventRecord CreateEvent(
        Guid incidentId,
        string actor,
        TimelineEventType type,
        string message,
        DateTimeOffset occurredAt, string? commandName = null, string? commandArguments = null,
        IReadOnlyList<string>? mentions = null) => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = incidentId,
        Actor = actor,
        Type = type,
        Message = message,
        OccurredAt = occurredAt,
        CommandName = commandName,
        CommandArguments = commandArguments,
        MentionsJson = mentions is { Count: > 0 } ? JsonSerializer.Serialize(mentions) : null,
    };

    private static AuditRecord CreateAudit(
        Guid incidentId,
        string actor,
        string action,
        string details,
        long incidentVersion,
        DateTimeOffset occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = incidentId,
        Actor = actor,
        Action = action,
        Details = details,
        IncidentVersion = incidentVersion,
        OccurredAt = occurredAt,
    };

    private static bool TryParseSequence(string id, out int sequence) =>
        int.TryParse(id.Replace("INC-", string.Empty, StringComparison.OrdinalIgnoreCase), out sequence);

    private static string SeverityLabel(Severity severity) => severity switch
    {
        Severity.Sev1 => "SEV-1",
        Severity.Sev2 => "SEV-2",
        Severity.Sev3 => "SEV-3",
        _ => "SEV-4",
    };

    private static Severity? ParseSeverity(string value) => value.Trim().ToUpperInvariant() switch
    { "SEV-1" or "SEV1" => Severity.Sev1, "SEV-2" or "SEV2" => Severity.Sev2,
      "SEV-3" or "SEV3" => Severity.Sev3, "SEV-4" or "SEV4" => Severity.Sev4, _ => null };
    private static List<string> NormalizeTags(IEnumerable<string> tags) => tags.Select(item => item.Trim().ToLowerInvariant())
        .Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    private void RecordOutbox(IncidentRecord record, string kind, DateTimeOffset occurredAt)
    {
        var message = new IncidentChangedEvent($"INC-{record.Sequence}", record.Version, kind, occurredAt);
        database.OutboxMessages.Add(new() { Id = Guid.NewGuid(), OccurredAt = occurredAt, Type = "incident.changed",
            AggregateId = message.IncidentId, Payload = JsonSerializer.Serialize(message), Attempts = 0, NextAttemptAt = occurredAt });
    }
    private static IReadOnlyList<string> DeserializeMentions(string? json) => string.IsNullOrWhiteSpace(json)
        ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
    [GeneratedRegex(@"(?:^|\s)@([A-Za-z0-9._-]+)")]
    private static partial Regex MentionPattern();

    private static IncidentResponse Map(IncidentRecord record) => new(
        $"INC-{record.Sequence}",
        record.Version,
        record.Title,
        record.Summary,
        SeverityLabel(record.Severity),
        record.Status,
        record.Service,
        record.OwnerTeam,
        record.Assignee,
        record.DeclaredAt,
        record.UpdatedAt,
        record.AcknowledgedAt,
        record.ResolvedAt,
        record.AffectedCustomers,
        record.Responders.OrderBy(item => item.JoinedAt)
            .Select(item => new ResponderResponse(item.Id, item.Name, item.Role, item.JoinedAt))
            .ToList(),
        record.Tags.OrderBy(item => item.Value).Select(item => item.Value).ToList(),
        record.Timeline.OrderByDescending(item => item.OccurredAt)
            .Select(item => new TimelineEventResponse(item.Id, item.OccurredAt, item.Actor,
                item.Type.ToString().ToLowerInvariant(), item.Message,
                item.CommandName is null ? null : new TimelineCommandResponse(item.CommandName, item.CommandArguments ?? string.Empty),
                DeserializeMentions(item.MentionsJson)))
            .ToList());
}

public sealed class IncidentConflictException(IncidentResponse current) : Exception
{
    public IncidentResponse Current { get; } = current;
}
public sealed class IncidentCommandException(string message) : Exception(message);
public sealed class IncidentQueryException(string message) : Exception(message);
