using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentLens.Api.Tests;

public sealed class IncidentWorkflowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Incident_list_requires_authentication()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Commander_can_manage_team_workflow_and_read_audit()
    {
        using var client = await CreateCommanderClientAsync();
        var created = await CreateIncidentAsync(client, "Team workflow test");
        var id = created.GetProperty("id").GetString()!;
        var version = created.GetProperty("version").GetInt64();

        var assignment = await client.PatchAsJsonAsync($"/api/incidents/{id}/assignment", new
        {
            assignee = "Elena Ortiz",
            expectedVersion = version,
        });
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        version = (await ReadRootAsync(assignment)).GetProperty("version").GetInt64();

        var responder = await client.PostAsJsonAsync($"/api/incidents/{id}/responders", new
        {
            name = "Noah Williams",
            role = "Subject matter expert",
            expectedVersion = version,
        });
        Assert.Equal(HttpStatusCode.OK, responder.StatusCode);
        version = (await ReadRootAsync(responder)).GetProperty("version").GetInt64();

        var tags = await client.PutAsJsonAsync($"/api/incidents/{id}/tags", new
        {
            tags = new[] { "customer-impact", "us-west" },
            expectedVersion = version,
        });
        Assert.Equal(HttpStatusCode.OK, tags.StatusCode);
        var tagged = await ReadRootAsync(tags);
        Assert.Equal(2, tagged.GetProperty("tags").GetArrayLength());

        var audit = await client.GetAsync($"/api/incidents/{id}/audit");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        var auditRoot = await ReadRootAsync(audit);
        Assert.True(auditRoot.GetArrayLength() >= 4);
    }

    [Fact]
    public async Task Stale_version_returns_current_incident_as_conflict()
    {
        using var client = await CreateCommanderClientAsync();
        var created = await CreateIncidentAsync(client, "Concurrency test");
        var id = created.GetProperty("id").GetString()!;
        var staleVersion = created.GetProperty("version").GetInt64();

        var firstUpdate = await client.PatchAsJsonAsync($"/api/incidents/{id}/status", new
        {
            status = "Identified",
            expectedVersion = staleVersion,
        });
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        var staleUpdate = await client.PostAsJsonAsync($"/api/incidents/{id}/notes", new
        {
            note = "This editor did not receive the status update.",
            expectedVersion = staleVersion,
        });
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var conflict = await ReadRootAsync(staleUpdate);
        Assert.Equal("version_conflict", conflict.GetProperty("code").GetString());
        Assert.Equal(staleVersion + 1,
            conflict.GetProperty("current").GetProperty("version").GetInt64());
    }

    [Fact] public async Task Alert_ingestion_is_idempotent_and_writes_an_outbox_event()
    {
        using var client=await CreateCommanderClientAsync(); client.DefaultRequestHeaders.Add("Idempotency-Key",$"monitor-{Guid.NewGuid():N}");
        var alert=new{title="Regional synthetic checks failing",summary="Three probes report elevated connection failures.",severity="SEV-1",service="Edge Gateway",ownerTeam="Traffic Engineering",assignee="Dan Fuhr",affectedCustomers=82,tags=new[]{"synthetic","edge"}};
        var first=await client.PostAsJsonAsync("/api/alerts",alert); Assert.Equal(HttpStatusCode.Created,first.StatusCode);var root=await ReadRootAsync(first);var id=root.GetProperty("incident").GetProperty("id").GetString()!;
        var replay=await client.PostAsJsonAsync("/api/alerts",alert);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);var rr=await ReadRootAsync(replay);Assert.True(rr.GetProperty("replayed").GetBoolean());Assert.Equal(id,rr.GetProperty("incident").GetProperty("id").GetString());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();Assert.True(await db.OutboxMessages.AnyAsync(x=>x.AggregateId==id));
    }
    [Fact] public async Task Timeline_commands_are_structured_and_mentions_are_extracted()
    {
        using var client=await CreateCommanderClientAsync();var created=await CreateIncidentAsync(client,"Timeline command test");var id=created.GetProperty("id").GetString()!;var version=created.GetProperty("version").GetInt64();
        var response=await client.PostAsJsonAsync($"/api/incidents/{id}/timeline",new{message="/note Paging @Maya and @Ravi",expectedVersion=version});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var timeline=(await ReadRootAsync(response)).GetProperty("timeline")[0];Assert.Equal("command",timeline.GetProperty("type").GetString());Assert.Equal("note",timeline.GetProperty("command").GetProperty("name").GetString());Assert.Equal(2,timeline.GetProperty("mentions").GetArrayLength());
    }
    [Fact] public async Task Incident_queue_supports_server_filtering_and_pagination()
    {
        using var client=await CreateCommanderClientAsync();var title=$"Pagination marker {Guid.NewGuid():N}";await CreateIncidentAsync(client,title);
        var response=await client.GetAsync($"/api/incidents?query={Uri.EscapeDataString(title)}&page=1&pageSize=1&severity=SEV-2");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var root=await ReadRootAsync(response);Assert.Equal(1,root.GetProperty("items").GetArrayLength());Assert.Equal(1,root.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Analytics_correlates_reliability_metrics_and_service_health()
    {
        using var client = await CreateCommanderClientAsync();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "analytics-test-correlation");
        var response = await client.GetAsync("/api/analytics/overview?days=30");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("analytics-test-correlation",
            response.Headers.GetValues("X-Correlation-ID").Single());
        var root = await ReadRootAsync(response);
        Assert.True(root.GetProperty("services").GetArrayLength() >= 4);
        Assert.True(root.GetProperty("metrics").GetProperty("resolvedIncidents").GetInt32() >= 2);
        Assert.True(root.GetProperty("services")[0].GetProperty("trend").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Commander_can_publish_a_postmortem_with_owned_action_items()
    {
        using var client = await CreateCommanderClientAsync();
        var created = await CreateIncidentAsync(client, "Postmortem workflow test");
        var id = created.GetProperty("id").GetString()!;
        var postmortemResponse = await client.PutAsJsonAsync($"/api/incidents/{id}/postmortem", new
        {
            owner = "Maya Chen",
            status = "InReview",
            executiveSummary = "A dependency timeout exhausted the checkout connection pool.",
            rootCause = "The retry budget exceeded the downstream concurrency limit.",
            detection = "The latency SLO alert opened the incident.",
            resolution = "Responders reduced retries and recycled the pool.",
            lessonsLearned = "Retry budgets need an explicit concurrency guard.",
            expectedVersion = (long?)null,
        });
        Assert.Equal(HttpStatusCode.OK, postmortemResponse.StatusCode);
        var postmortem = await ReadRootAsync(postmortemResponse);
        var version = postmortem.GetProperty("version").GetInt64();

        var actionResponse = await client.PostAsJsonAsync($"/api/incidents/{id}/postmortem/actions", new
        {
            title = "Add a concurrency guard to the retry budget",
            owner = "Noah Williams",
            dueAt = DateTimeOffset.UtcNow.AddDays(7),
            expectedPostmortemVersion = version,
        });
        Assert.Equal(HttpStatusCode.OK, actionResponse.StatusCode);
        var withAction = await ReadRootAsync(actionResponse);
        Assert.Equal(1, withAction.GetProperty("actionItems").GetArrayLength());
        Assert.Equal("Noah Williams", withAction.GetProperty("actionItems")[0].GetProperty("owner").GetString());

        var export = await client.GetAsync($"/api/incidents/{id}/export?format=json");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var evidence = await ReadRootAsync(export);
        Assert.Equal(id, evidence.GetProperty("incident").GetProperty("id").GetString());
        Assert.Equal(1, evidence.GetProperty("postmortem").GetProperty("actionItems").GetArrayLength());
    }

    [Fact]
    public async Task Stale_postmortem_version_returns_a_conflict()
    {
        using var client = await CreateCommanderClientAsync();
        var created = await CreateIncidentAsync(client, "Postmortem concurrency test");
        var id = created.GetProperty("id").GetString()!;
        var payload = new
        {
            owner = "Maya Chen", status = "Draft", executiveSummary = "Summary",
            rootCause = "Root cause", detection = "Detection", resolution = "Resolution",
            lessonsLearned = "Lessons", expectedVersion = (long?)null,
        };
        var initial = await client.PutAsJsonAsync($"/api/incidents/{id}/postmortem", payload);
        var initialRoot = await ReadRootAsync(initial);
        var version = initialRoot.GetProperty("version").GetInt64();
        var update = new
        {
            owner = "Maya Chen", status = "Published", executiveSummary = "Updated summary",
            rootCause = "Root cause", detection = "Detection", resolution = "Resolution",
            lessonsLearned = "Lessons", expectedVersion = (long?)version,
        };
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/incidents/{id}/postmortem", update)).StatusCode);
        var conflict = await client.PutAsJsonAsync($"/api/incidents/{id}/postmortem", update);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("postmortem_version_conflict",
            (await ReadRootAsync(conflict)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Csv_evidence_export_contains_incident_and_audit_rows()
    {
        using var client = await CreateCommanderClientAsync();
        var created = await CreateIncidentAsync(client, "CSV evidence test");
        var id = created.GetProperty("id").GetString()!;
        var response = await client.GetAsync($"/api/incidents/{id}/export?format=csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("recordType,timestamp,actor,action,details,owner,status", csv);
        Assert.Contains("\"incident\"", csv);
        Assert.Contains("\"audit\"", csv);
    }

    [Fact]
    public async Task Commander_can_update_versioned_service_objectives()
    {
        using var client = await CreateCommanderClientAsync();
        var response = await client.GetAsync("/api/reliability/objectives");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var objectives = await ReadRootAsync(response);
        var objective = objectives.EnumerateArray().First(item =>
            item.GetProperty("service").GetString() == "Payments API");
        var id = objective.GetProperty("id").GetGuid();
        var version = objective.GetProperty("version").GetInt64();
        var request = new
        {
            ownerTeam = "Payments Reliability", availabilityTargetPercent = 99.97m,
            acknowledgementTargetMinutes = 4, resolutionTargetMinutes = 40,
            monthlyErrorBudgetMinutes = 18, fastBurnThreshold = 14m,
            slowBurnThreshold = 6m, enabled = true, expectedVersion = version,
        };
        var updated = await client.PutAsJsonAsync($"/api/reliability/objectives/{id}", request);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Payments Reliability", (await ReadRootAsync(updated)).GetProperty("ownerTeam").GetString());
        var stale = await client.PutAsJsonAsync($"/api/reliability/objectives/{id}", request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Burn_evaluation_suppresses_signals_during_maintenance()
    {
        var service = $"Maintenance Service {Guid.NewGuid():N}";
        await SeedReliabilityScenarioAsync(service, withMaintenance: true);
        using var client = await CreateCommanderClientAsync();
        var evaluation = await client.PostAsync("/api/reliability/evaluate", null);
        Assert.Equal(HttpStatusCode.OK, evaluation.StatusCode);
        var signals = await client.GetFromJsonAsync<JsonElement>("/api/reliability/signals?status=Suppressed");
        var serviceSignals = signals.EnumerateArray().Where(item =>
            item.GetProperty("service").GetString() == service).ToList();
        Assert.Equal(2, serviceSignals.Count);
        Assert.All(serviceSignals, signal =>
            Assert.Contains("Maintenance:", signal.GetProperty("suppressionReason").GetString()));
    }

    [Fact]
    public async Task Commander_can_acknowledge_a_burn_signal()
    {
        var service = $"Acknowledge Service {Guid.NewGuid():N}";
        await SeedReliabilityScenarioAsync(service, withMaintenance: false);
        using var client = await CreateCommanderClientAsync();
        await client.PostAsync("/api/reliability/evaluate", null);
        var signals = await client.GetFromJsonAsync<JsonElement>("/api/reliability/signals?status=Open");
        var signal = signals.EnumerateArray().First(item => item.GetProperty("service").GetString() == service);
        var response = await client.PatchAsJsonAsync(
            $"/api/reliability/signals/{signal.GetProperty("id").GetGuid()}/acknowledge",
            new { expectedVersion = signal.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var acknowledged = await ReadRootAsync(response);
        Assert.Equal("Acknowledged", acknowledged.GetProperty("status").GetString());
        Assert.Equal("Dan Fuhr", acknowledged.GetProperty("acknowledgedBy").GetString());
    }

    [Fact]
    public async Task Commander_can_promote_a_signal_to_an_incident()
    {
        var service = $"Promotion Service {Guid.NewGuid():N}";
        await SeedReliabilityScenarioAsync(service, withMaintenance: false);
        using var client = await CreateCommanderClientAsync();
        await client.PostAsync("/api/reliability/evaluate", null);
        var signals = await client.GetFromJsonAsync<JsonElement>("/api/reliability/signals?status=Open");
        var signal = signals.EnumerateArray().First(item => item.GetProperty("service").GetString() == service);
        var response = await client.PostAsJsonAsync(
            $"/api/reliability/signals/{signal.GetProperty("id").GetGuid()}/promote",
            new { assignee = "Maya Chen", expectedVersion = signal.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var promoted = await ReadRootAsync(response);
        Assert.Equal("Promoted", promoted.GetProperty("signal").GetProperty("status").GetString());
        Assert.StartsWith("INC-", promoted.GetProperty("incident").GetProperty("id").GetString());
        Assert.Equal(service, promoted.GetProperty("incident").GetProperty("service").GetString());
    }

    [Fact] public async Task Authenticated_operator_can_join_a_realtime_presence_room()
    {
        var token=await GetCommanderTokenAsync();await using var connection=new HubConnectionBuilder().WithUrl("http://localhost/hubs/incidents",options=>{options.HttpMessageHandlerFactory=_=>factory.Server.CreateHandler();options.AccessTokenProvider=()=>Task.FromResult<string?>(token);options.Transports=HttpTransportType.LongPolling;}).Build();
        var received=new TaskCompletionSource<PresenceUser[]>(TaskCreationOptions.RunContinuationsAsynchronously);connection.On<string,PresenceUser[]>("PresenceChanged",(_,users)=>received.TrySetResult(users));
        await connection.StartAsync();await connection.InvokeAsync("JoinIncident","INC-1042");var users=await received.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Contains(users,u=>u.Name=="Dan Fuhr"&&u.Role=="Commander");
    }

    private async Task<HttpClient> CreateCommanderClientAsync()
    {
        var client = factory.CreateClient();
        var token = await GetCommanderTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", token);
        return client;
    }

    private async Task SeedReliabilityScenarioAsync(string service, bool withMaintenance)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
        var now = DateTimeOffset.UtcNow;
        database.ServiceObjectives.Add(new ServiceObjectiveRecord
        {
            Id = Guid.NewGuid(), Service = service, OwnerTeam = "Reliability Engineering",
            AvailabilityTargetPercent = 99.9m, AcknowledgementTargetMinutes = 5,
            ResolutionTargetMinutes = 45, MonthlyErrorBudgetMinutes = 43,
            FastBurnThreshold = 14m, SlowBurnThreshold = 6m, Enabled = true,
        });
        database.ServiceHealthSnapshots.Add(new ServiceHealthSnapshotRecord
        {
            Id = Guid.NewGuid(), Service = service, CapturedAt = now.AddMinutes(-5),
            AvailabilityPercent = 95m, ErrorRatePercent = 5m, LatencyP95Milliseconds = 1800,
        });
        if (withMaintenance)
        {
            database.MaintenanceWindows.Add(new MaintenanceWindowRecord
            {
                Id = Guid.NewGuid(), Service = service, Title = "Planned database failover",
                StartsAt = now.AddMinutes(-30), EndsAt = now.AddMinutes(30),
                CreatedBy = "Dan Fuhr", CreatedAt = now.AddHours(-1),
            });
        }
        await database.SaveChangesAsync();
    }
    private async Task<string> GetCommanderTokenAsync(){using var client=factory.CreateClient();var response=await client.PostAsync("/api/auth/demo-token",null);response.EnsureSuccessStatusCode();return (await ReadRootAsync(response)).GetProperty("accessToken").GetString()!;}

    private static async Task<JsonElement> CreateIncidentAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            summary = "Integration test incident.",
            severity = "SEV-2",
            service = "Test Service",
            ownerTeam = "Quality Engineering",
            assignee = "Dan Fuhr",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadRootAsync(response);
    }

    private static async Task<JsonElement> ReadRootAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
