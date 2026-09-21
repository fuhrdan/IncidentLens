using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IncidentLens.Api.Tests;

public sealed class ClientAuthConfigTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Development_config_exposes_only_the_development_login_mode()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/auth/client-config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var config = document.RootElement;
        Assert.Equal("development", config.GetProperty("mode").GetString());
        Assert.Equal("", config.GetProperty("clientId").GetString());
        Assert.False(config.TryGetProperty("jwtKey", out _));
        Assert.False(config.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Incident_data_still_requires_authorization()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/incidents/")).StatusCode);
    }
}
