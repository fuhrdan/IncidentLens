namespace IncidentLens.Api.Endpoints;

/// <summary>Public, non-secret SPA OIDC settings. Never expose server credentials.</summary>
public static class ClientAuthEndpoints
{
    public static IEndpointRouteBuilder MapClientAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/client-config", IResult (IConfiguration configuration, IHostEnvironment environment) =>
        {
            var authority = configuration["Authentication:Authority"];
            var clientId = configuration["Authentication:ClientId"];
            if (environment.IsDevelopment() && string.IsNullOrWhiteSpace(authority))
                return Results.Ok(new { mode = "development", authority = "", clientId = "", scope = "" });
            if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId))
                return Results.Problem("Browser identity is not configured.", statusCode: 503);
            return Results.Ok(new
            {
                mode = "oidc",
                authority,
                clientId,
                scope = configuration["Authentication:Scope"] ?? "openid profile"
            });
        }).WithTags("Authentication").AllowAnonymous();
        return endpoints;
    }
}
