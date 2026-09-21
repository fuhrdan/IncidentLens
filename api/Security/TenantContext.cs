using System.Security.Claims;

namespace IncidentLens.Api.Security;

/// <summary>
/// Tenant identity is taken ONLY from a validated identity token, never from
/// a request header, query, payload, or URL. Missing claims fail closed.
/// A development-only fallback allows existing local demos and test fixtures.
/// </summary>
public sealed class TenantContext(IHttpContextAccessor accessor, IHostEnvironment environment, IConfiguration configuration)
{
    private string? backgroundTenant;

    public string TenantId
    {
        get
        {
            var request = accessor.HttpContext;
            if (request is not null)
            {
                var claimType = configuration["Authentication:TenantClaimType"] ?? "tenant_id";
                return Normalize(request.User.FindFirst(claimType)?.Value);
            }
            return backgroundTenant ?? (environment.IsDevelopment() ? "demo" : string.Empty);
        }
    }

    public static string Normalize(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? value : string.Empty;

    /// <summary>Use only from background code (not from HTTP requests).</summary>
    public IDisposable ForBackgroundTenant(string tenantId)
    {
        if (accessor.HttpContext is not null || Normalize(tenantId) != tenantId)
            throw new InvalidOperationException("Invalid background tenant context.");
        var previous = backgroundTenant;
        backgroundTenant = tenantId;
        return new Restore(() => backgroundTenant = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
