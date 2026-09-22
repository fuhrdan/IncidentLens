using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Realtime;
using IncidentLens.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace IncidentLens.Api.Tests;

public sealed class TenantIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Tenant_identifiers_and_realtime_groups_have_safe_boundaries()
    {
        Assert.Equal(string.Empty, TenantContext.Normalize("alpha:incident:INC-1"));
        Assert.Equal(string.Empty, TenantContext.Normalize(new string('a', 65)));
        Assert.NotEqual(IncidentHub.GroupName("alpha", "INC-1043"),
            IncidentHub.GroupName("beta", "INC-1043"));
    }

    [Fact]
    public async Task No_tenant_claim_is_forbidden_even_with_a_forged_header()
    {
        using var client = Authenticated("", "Commander");
        client.DefaultRequestHeaders.Add("X-Tenant-ID", "demo");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/incidents")).StatusCode);
    }

    [Fact]
    public async Task Tenants_cannot_read_or_modify_each_others_incidents_and_audit()
    {
        using var alpha = Authenticated("alpha", "Commander");
        using var beta = Authenticated("beta", "Commander");
        var title = $"Private {Guid.NewGuid():N}";
        var created = await alpha.PostAsJsonAsync("/api/incidents", new
        {
            title, summary = "Tenant-only content", severity = "SEV-2",
            service = "Only alpha", ownerTeam = "Security", assignee = "Test Commander",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var incident = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = incident.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NotFound,
            (await beta.GetAsync($"/api/incidents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await beta.GetAsync($"/api/incidents/{id}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await beta.GetAsync($"/api/incidents/{id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await beta.PatchAsJsonAsync($"/api/incidents/{id}/status",
                new { status = "Resolved", expectedVersion = 1 })).StatusCode);
        var queue = await beta.GetFromJsonAsync<JsonElement>("/api/incidents");
        Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("title").GetString() == title);
        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync($"/api/incidents/{id}")).StatusCode);
    }

    [Fact]
    public async Task The_same_idempotency_key_can_be_used_by_different_tenants()
    {
        using var alpha = Authenticated("alpha", "Commander");
        using var beta = Authenticated("beta", "Commander");
        var key = $"same-key-{Guid.NewGuid():N}";
        alpha.DefaultRequestHeaders.Add("Idempotency-Key", key);
        beta.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var payload = new { title = "Tenant-specific alert", summary = "alert test",
            severity = "SEV-2", service = "Test", ownerTeam = "Test",
            assignee = "Commander", affectedCustomers = 0, tags = Array.Empty<string>() };
        var first = await alpha.PostAsJsonAsync("/api/alerts", payload);
        var second = await beta.PostAsJsonAsync("/api/alerts", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var left = await first.Content.ReadFromJsonAsync<JsonElement>();
        var right = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(left.GetProperty("replayed").GetBoolean());
        Assert.False(right.GetProperty("replayed").GetBoolean());
        Assert.Equal(HttpStatusCode.OK,
            (await beta.PostAsJsonAsync("/api/alerts", payload)).StatusCode);
    }

    [Fact]
    public async Task Reliability_objectives_with_the_same_service_are_tenant_isolated()
    {
        var service = $"Shared service {Guid.NewGuid():N}";
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var database = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
            using (tenant.ForBackgroundTenant("objective-alpha"))
            {
                database.ServiceObjectives.Add(new ServiceObjectiveRecord
                {
                    Id = alphaId, Service = service, OwnerTeam = "Alpha operations",
                    AvailabilityTargetPercent = 99.9m,
                });
                await database.SaveChangesAsync();
            }
            using (tenant.ForBackgroundTenant("objective-beta"))
            {
                database.ServiceObjectives.Add(new ServiceObjectiveRecord
                {
                    Id = betaId, Service = service, OwnerTeam = "Beta operations",
                    AvailabilityTargetPercent = 99.9m,
                });
                await database.SaveChangesAsync();
            }
        }
        using var alpha = Authenticated("objective-alpha", "Viewer");
        using var beta = Authenticated("objective-beta", "Viewer");
        var alphaObjectives = await alpha.GetFromJsonAsync<JsonElement>("/api/reliability/objectives");
        var betaObjectives = await beta.GetFromJsonAsync<JsonElement>("/api/reliability/objectives");
        Assert.Contains(alphaObjectives.EnumerateArray(), x => x.GetProperty("id").GetGuid() == alphaId);
        Assert.DoesNotContain(alphaObjectives.EnumerateArray(), x => x.GetProperty("id").GetGuid() == betaId);
        Assert.Contains(betaObjectives.EnumerateArray(), x => x.GetProperty("id").GetGuid() == betaId);
        Assert.DoesNotContain(betaObjectives.EnumerateArray(), x => x.GetProperty("id").GetGuid() == alphaId);
    }

    [Fact]
    public async Task Another_tenant_cannot_join_an_incidents_SignalR_room()
    {
        using var alpha = Authenticated("room-alpha", "Commander");
        var created = await alpha.PostAsJsonAsync("/api/incidents", new
        {
            title = "Confidential realtime incident", summary = "Private channel",
            severity = "SEV-2", service = "Test", ownerTeam = "Test", assignee = "Commander",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var incident = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = incident.GetProperty("id").GetString()!;
        using var beta = Authenticated("room-beta", "Commander");
        var token = beta.DefaultRequestHeaders.Authorization!.Parameter!;
        await using var connection = new HubConnectionBuilder().WithUrl(
            "http://localhost/hubs/incidents", options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.Transports = HttpTransportType.LongPolling;
            }).Build();
        await connection.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinIncident", id));
    }

    [Fact]
    public async Task Direct_child_queries_fail_closed_without_a_tenant()
    {
        using var client = Authenticated("alpha", "Commander");
        var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = "Tenant audit", summary = "isolation check", severity = "SEV-2",
            service = "Test", ownerTeam = "Test", assignee = "Commander",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
        // No HTTP request in scope => development demo tenant; alpha child records cannot leak.
        Assert.False(await database.AuditRecords.AnyAsync(x => x.Actor == "Tenant test operator"));
    }

    [Fact]
    public async Task Cross_tenant_database_writes_are_blocked_even_if_a_row_is_tracked()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
        database.ServiceObjectives.Add(new ServiceObjectiveRecord
        {
            Id = Guid.NewGuid(), TenantId = "alpha", Service = $"Escaped {Guid.NewGuid():N}",
            OwnerTeam = "alpha", AvailabilityTargetPercent = 99m,
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Command_center_overview_is_tenant_scoped_and_reader_accessible()
    {
        using var alphaCommander = Authenticated("dashboard-alpha", "Commander");
        using var alphaViewer = Authenticated("dashboard-alpha", "Viewer");
        using var betaViewer = Authenticated("dashboard-beta", "Viewer");
        var privateTitle = $"Dashboard private {Guid.NewGuid():N}";
        var created = await alphaCommander.PostAsJsonAsync("/api/incidents", new
        {
            title = privateTitle, summary = "Confidential dashboard row",
            severity = "SEV-1", service = "Dashboard Test",
            ownerTeam = "Alpha", assignee = "Commander",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var mine = await alphaViewer.GetFromJsonAsync<JsonElement>("/api/dashboard/overview?days=7");
        var other = await betaViewer.GetFromJsonAsync<JsonElement>("/api/dashboard/overview?days=7");
        Assert.True(mine.GetProperty("summary").GetProperty("activeIncidents").GetInt32() >= 1);
        Assert.True(mine.GetProperty("summary").GetProperty("criticalIncidents").GetInt32() >= 1);
        Assert.Contains(mine.GetProperty("activeIncidents").EnumerateArray(),
            row => row.GetProperty("title").GetString() == privateTitle);
        Assert.DoesNotContain(other.GetProperty("activeIncidents").EnumerateArray(),
            row => row.GetProperty("title").GetString() == privateTitle);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/api/dashboard/overview")).StatusCode);
    }

[Fact]
public async Task Command_center_services_are_bounded_and_tenant_scoped()
{
    var alphaTenant = $"dashboard-{Guid.NewGuid():N}";
    var betaTenant = $"dashboard-{Guid.NewGuid():N}";

    // Seed ten objectives for alpha and a distinct objective for beta.
    // No health samples means availability is not fabricated by this test.
    await using (var scope = factory.Services.CreateAsyncScope())
    {
        var tenant = scope.ServiceProvider
            .GetRequiredService<TenantContext>();

        var database = scope.ServiceProvider
            .GetRequiredService<IncidentLensDbContext>();

        using (tenant.ForBackgroundTenant(alphaTenant))
        {
            for (var index = 0; index < 10; index++)
            {
                database.ServiceObjectives.Add(
                    new ServiceObjectiveRecord
                    {
                        Id = Guid.NewGuid(),
                        Service = $"S-{index:00}",
                        OwnerTeam = "Alpha operations",
                        AvailabilityTargetPercent = 99.9m,
                    });
            }

            await database.SaveChangesAsync();
        }

        using (tenant.ForBackgroundTenant(betaTenant))
        {
            database.ServiceObjectives.Add(
                new ServiceObjectiveRecord
                {
                    Id = Guid.NewGuid(),
                    Service = "Beta-only service",
                    OwnerTeam = "Beta operations",
                    AvailabilityTargetPercent = 99.9m,
                });

            await database.SaveChangesAsync();
        }
    }

    using var alpha = Authenticated(alphaTenant, "Viewer");
    using var beta = Authenticated(betaTenant, "Viewer");

    var alphaDashboard = await alpha.GetFromJsonAsync<JsonElement>(
        "/api/dashboard/overview?days=7");

    var betaDashboard = await beta.GetFromJsonAsync<JsonElement>(
        "/api/dashboard/overview?days=7");

    var alphaServices = alphaDashboard
        .GetProperty("services")
        .EnumerateArray()
        .ToArray();

    var betaServices = betaDashboard
        .GetProperty("services")
        .EnumerateArray()
        .ToArray();

    // The API, not only Angular, must enforce the eight-service limit.
    Assert.Equal(8, alphaServices.Length);

    // All seeded services have the same health classification, so
    // the secondary alphabetical sort determines their order.
    Assert.Equal(
        Enumerable.Range(0, 8)
            .Select(index => $"S-{index:00}"),
        alphaServices.Select(service =>
            service.GetProperty("service").GetString()));

    // Ensure the response contains the health fields the UI requires.
    Assert.All(alphaServices, service =>
    {
        Assert.True(service.TryGetProperty(
            "currentAvailabilityPercent", out _));

        Assert.True(service.TryGetProperty(
            "availabilityTargetPercent", out _));

        Assert.True(service.TryGetProperty(
            "errorBudgetConsumedPercent", out _));

        Assert.True(service.TryGetProperty("trend", out _));
    });

    // Neither tenant can see the other's service objectives.
    Assert.Single(betaServices);

    Assert.Equal(
        "Beta-only service",
        betaServices[0].GetProperty("service").GetString());

    Assert.DoesNotContain(
        alphaServices,
        service => service.GetProperty("service").GetString()
            == "Beta-only service");
}
    private HttpClient Authenticated(string tenant, string role)
    {
        var client = factory.CreateClient();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "Tenant test operator"),
            new(ClaimTypes.Role, role),
        };
        if (tenant.Length > 0) claims.Add(new("tenant_id", tenant));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            "development-only-key-change-before-deploying-incidentlens")), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("IncidentLens.Api", "IncidentLens.Web", claims,
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
}
