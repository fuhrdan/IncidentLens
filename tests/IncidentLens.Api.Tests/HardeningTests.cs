using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IncidentLens.Api.Tests;

public sealed class RateLimitFactory : ApiFactory
{
    protected override IDictionary<string, string?> ConfigurationOverrides =>
        new Dictionary<string, string?>
        {
            ["RateLimiting:ReadPerMinute"] = "2",
            ["RateLimiting:WritePerMinute"] = "2",
            ["RateLimiting:AnonymousPerMinute"] = "2",
            ["RateLimiting:RealtimeConnectPerMinute"] = "2",
            ["RateLimiting:DemoAuthPerMinute"] = "2",
        };
}

public sealed class HardeningTests(RateLimitFactory factory) : IClassFixture<RateLimitFactory>
{
    [Fact]
    public async Task Anonymous_budget_is_bounded_but_health_probes_stay_available()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/incidents")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/incidents")).StatusCode);
        var limited = await client.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Tenant_read_budgets_are_isolated_and_ignore_client_tenant_header()
    {
        using var alpha = Authenticated("limit-alpha");
        using var beta = Authenticated("limit-beta");
        alpha.DefaultRequestHeaders.Add("X-Tenant-ID", "limit-beta");
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync("/api/incidents")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await alpha.GetAsync("/api/incidents")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await beta.GetAsync("/api/incidents")).StatusCode);
    }

    [Fact]
    public async Task Write_budget_rejects_extra_changes_but_allows_read_only_work()
    {
        using var client = Authenticated("limit-write");
        for (var i = 0; i < 2; i++)
        {
            var created = await client.PostAsJsonAsync("/api/incidents", new
            {
                title = $"Load admission {i}", summary = "Safe mutation",
                severity = "SEV-2", service = "Test", ownerTeam = "Test",
                assignee = "Test operator",
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/incidents", new { title = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/incidents")).StatusCode);
    }

    [Fact]
    public async Task Invalid_correlation_ids_are_not_reflected_or_logged_verbatim()
    {
        using var client = Authenticated("limit-correlation");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "not-valid!$#");
        var response = await client.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("not-valid!$#", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    private HttpClient Authenticated(string tenant)
    {
        var client = factory.CreateClient();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "Rate limit tester"),
            new Claim(ClaimTypes.Role, "Commander"),
            new Claim("tenant_id", tenant),
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            "development-only-key-change-before-deploying-incidentlens")), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken("IncidentLens.Api", "IncidentLens.Web", claims,
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(jwt));
        return client;
    }
}
