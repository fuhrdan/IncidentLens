using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IncidentLens.Api.Endpoints;

public static class DemoAuthEndpoints
{
    /// <summary>
    /// Development-only token issuer. A deployed environment should replace
    /// this route with its enterprise identity provider.
    /// </summary>
    public static IEndpointRouteBuilder MapDemoAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/demo-token", (IConfiguration configuration) =>
        {
            var now = DateTime.UtcNow;
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "demo-commander"),
                new Claim(ClaimTypes.Name, "Dan Fuhr"),
                new Claim(ClaimTypes.Role, "Commander"),
                new Claim("tenant_id", "demo"),
            };
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
            var token = new JwtSecurityToken(
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                claims: claims,
                notBefore: now,
                expires: now.AddHours(8),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token) });
        }).WithTags("Authentication").AllowAnonymous();

        return endpoints;
    }
}
