using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace IncidentLens.Api.Tests;

public sealed class RetentionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Cleanup_is_confirmed_commander_only_and_preserves_other_tenants_and_evidence()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-150);
        var recent = DateTimeOffset.UtcNow.AddDays(-1);
        var alphaSnapshot = Guid.NewGuid();
        var alphaRecent = Guid.NewGuid();
        var betaSnapshot = Guid.NewGuid();
        var oldProcessed = Guid.NewGuid();
        var oldPending = Guid.NewGuid();
        var betaProcessed = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var database = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
            using (tenant.ForBackgroundTenant("retention-alpha"))
            {
                database.ServiceHealthSnapshots.AddRange(
                    Snapshot(alphaSnapshot, older), Snapshot(alphaRecent, recent));
                database.OutboxMessages.AddRange(Message(oldProcessed, older, older),
                    Message(oldPending, older, null));
                await database.SaveChangesAsync();
            }
            using (tenant.ForBackgroundTenant("retention-beta"))
            {
                database.ServiceHealthSnapshots.Add(Snapshot(betaSnapshot, older));
                database.OutboxMessages.Add(Message(betaProcessed, older, older));
                await database.SaveChangesAsync();
            }
        }
        using var viewer = Client("retention-alpha", "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/retention/preview")).StatusCode);
        using var commander = Client("retention-alpha", "Commander");
        var incidentResponse = await commander.PostAsJsonAsync("/api/incidents", new
        {
            title = "Evidence retention regression", summary = "Do not delete history",
            severity = "SEV-2", service = "Test", ownerTeam = "Test", assignee = "Commander",
        });
        Assert.Equal(HttpStatusCode.Created, incidentResponse.StatusCode);
        var incident = await incidentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var incidentId = incident.GetProperty("id").GetString()!;
        var preview = await commander.GetFromJsonAsync<JsonElement>("/api/retention/preview");
        Assert.True(preview.GetProperty("eligibleHealthSnapshots").GetInt32() >= 1);
        Assert.True(preview.GetProperty("eligibleProcessedOutbox").GetInt32() >= 1);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await commander.PostAsJsonAsync("/api/retention/run", new { confirm = "yes" })).StatusCode);
        var result = await commander.PostAsJsonAsync("/api/retention/run",
            new { confirm = "PURGE_ELIGIBLE_DATA" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await commander.GetAsync($"/api/incidents/{incidentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await commander.GetAsync($"/api/incidents/{incidentId}/audit")).StatusCode);
        await using var check = factory.Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<TenantContext>();
        var db = check.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
        using (context.ForBackgroundTenant("retention-alpha"))
        {
            Assert.False(await db.ServiceHealthSnapshots.AnyAsync(x => x.Id == alphaSnapshot));
            Assert.True(await db.ServiceHealthSnapshots.AnyAsync(x => x.Id == alphaRecent));
            Assert.False(await db.OutboxMessages.AnyAsync(x => x.Id == oldProcessed));
            Assert.True(await db.OutboxMessages.AnyAsync(x => x.Id == oldPending));
        }
        using (context.ForBackgroundTenant("retention-beta"))
        {
            Assert.True(await db.ServiceHealthSnapshots.AnyAsync(x => x.Id == betaSnapshot));
            Assert.True(await db.OutboxMessages.AnyAsync(x => x.Id == betaProcessed));
        }
    }

    private static ServiceHealthSnapshotRecord Snapshot(Guid id, DateTimeOffset at) => new()
    {
        Id = id, Service = "Retention-test", CapturedAt = at,
        AvailabilityPercent = 99m, ErrorRatePercent = 1m, LatencyP95Milliseconds = 100,
    };

    private static OutboxMessage Message(Guid id, DateTimeOffset at, DateTimeOffset? processed) => new()
    {
        Id = id, OccurredAt = at, Type = "retention-test", AggregateId = "test",
        Payload = "{}", ProcessedAt = processed, NextAttemptAt = DateTimeOffset.UtcNow.AddDays(1),
    };

    private HttpClient Client(string tenant, string role)
    {
        var client = factory.CreateClient();
        var claims = new[] { new Claim(ClaimTypes.Name, "Retention operator"),
            new Claim(ClaimTypes.Role, role), new Claim("tenant_id", tenant) };
        var key = Encoding.UTF8.GetBytes("development-only-key-change-before-deploying-incidentlens");
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("IncidentLens.Api", "IncidentLens.Web", claims,
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
}
