using System.Text.Json;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Observability;
using IncidentLens.Api.Data;
using IncidentLens.Api.Realtime;
using IncidentLens.Api.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Services;

public interface IIncidentEventPublisher { Task PublishAsync(string tenantId, IncidentChangedEvent message, CancellationToken cancellationToken); }
public sealed class SignalRIncidentEventPublisher(IHubContext<IncidentHub> hub) : IIncidentEventPublisher
{
    public Task PublishAsync(string tenantId, IncidentChangedEvent message, CancellationToken token) =>
        hub.Clients.Group(IncidentHub.GroupName(tenantId, message.IncidentId)).SendAsync("IncidentUpdated", message, token);
}

/// <summary>Delivers committed messages and retries transient failures with bounded backoff.</summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, IIncidentEventPublisher publisher,
    TimeProvider clock, IncidentTelemetry telemetry, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchBatchAsync(stoppingToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { logger.LogError(exception, "The outbox dispatcher could not read its next batch."); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    internal async Task DispatchBatchAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var now = clock.GetUtcNow();
        var query = database.OutboxMessages.IgnoreQueryFilters()
            .Where(item => item.ProcessedAt == null);
        // Filter by due time BEFORE limiting the result. Previously, an old
        // retry in the first 100 rows could starve newer, ready messages.
        // SQLite cannot translate DateTimeOffset range ordering/comparisons;
        // its unbounded scan is for local development ONLY, never production.
        var messages = database.Database.IsSqlite()
            ? (await query.ToListAsync(token))
                .Where(item => item.NextAttemptAt <= now)
                .OrderBy(item => item.OccurredAt).Take(20).ToList()
            : await query.Where(item => item.NextAttemptAt <= now)
                .OrderBy(item => item.OccurredAt).Take(20).ToListAsync(token);
        foreach (var message in messages)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<IncidentChangedEvent>(message.Payload) ?? throw new JsonException("Outbox payload was empty.");
                await publisher.PublishAsync(message.TenantId, payload, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Min(60, Math.Pow(2, message.Attempts)));
                message.LastError = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
                logger.LogWarning(exception, "Outbox message {MessageId} will be retried.", message.Id);
                telemetry.OutboxRetries.Add(1);
                using (tenant.ForBackgroundTenant(message.TenantId))
                    await database.SaveChangesAsync(token);
                continue;
            }
            // Do NOT catch database commit failure as a publishing failure.
            // The next iteration may publish a duplicate if this commit fails;
            // consumers must treat notifications as at-least-once.
            message.ProcessedAt = clock.GetUtcNow();
            message.LastError = null;
            using (tenant.ForBackgroundTenant(message.TenantId))
                await database.SaveChangesAsync(token);
            telemetry.OutboxPublished.Add(1);
        }
    }
}
