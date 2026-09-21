using IncidentLens.Api.Observability;
using System.Threading.RateLimiting;
using IncidentLens.Api.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace IncidentLens.Api.Hardening;

/// <summary>
/// Single-instance guardrails. These are intentionally NOT a distributed quota:
/// keep one API replica until shared rate limits, SignalR and outbox leasing exist.
/// Do not use untrusted X-Forwarded-For or X-Tenant-ID headers as partition keys.
/// </summary>
public static class RequestLimits
{
    public const string SectionName = "RateLimiting";

public static IServiceCollection AddIncidentLensRateLimiting(
    this IServiceCollection services,
    IConfiguration configuration)
{
    return services.AddRateLimiter(options =>
    {
        // No queuing: requests must not accumulate
        // and exhaust a troubled API.
        options.GlobalLimiter =
            PartitionedRateLimiter.Create<HttpContext, string>(
                context =>
                {
                    var currentConfiguration =
                        context.RequestServices
                            .GetRequiredService<IConfiguration>();

                    var read = Limit(
                        currentConfiguration,
                        "ReadPerMinute", 600);

                    var write = Limit(
                        currentConfiguration,
                        "WritePerMinute", 120);

                    var anonymous = Limit(
                        currentConfiguration,
                        "AnonymousPerMinute", 60);

                    var realtime = Limit(
                        currentConfiguration,
                        "RealtimeConnectPerMinute", 60);

                    var login = Limit(
                        currentConfiguration,
                        "DemoAuthPerMinute", 10);

                    var tenantClaim =
                        currentConfiguration[
                            "Authentication:TenantClaimType"]
                        ?? "tenant_id";

                    // Keep the existing partitioning code
                    // beginning with:
                var path = context.Request.Path;
                if (path.StartsWithSegments("/health"))
                    return RateLimitPartition.GetNoLimiter("health");

                var isDemoLogin = path.StartsWithSegments("/api/auth");
                var isRealtime = path.StartsWithSegments("/hubs/incidents");
                var isWrite = !HttpMethods.IsGet(context.Request.Method) &&
                    !HttpMethods.IsHead(context.Request.Method) &&
                    !HttpMethods.IsOptions(context.Request.Method);
                var tenant = TenantContext.Normalize(context.User.FindFirst(tenantClaim)?.Value);
                var authenticated = context.User.Identity?.IsAuthenticated == true && tenant.Length > 0;
                // Tenant-wide quotas prevent bypass via many user identities; anonymous
                // requests are keyed by the *connection* IP, not caller-supplied headers.
                var identity = authenticated ? $"tenant:{tenant}" :
                    $"remote:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
                var category = isDemoLogin ? "demo-auth" : isRealtime ? "realtime" :
                    !authenticated ? "anonymous" : isWrite ? "write" : "read";
                var allowance = isDemoLogin ? login : isRealtime ? realtime :
                    !authenticated ? anonymous : isWrite ? write : read;
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{category}:{identity}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = allowance,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (rejection, _) =>
            {
                var response = rejection.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                response.Headers["Retry-After"] = rejection.Lease.TryGetMetadata(
                    MetadataName.RetryAfter, out var retry)
                    ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : "60";
                response.Headers["Cache-Control"] = "no-store";
                rejection.HttpContext.RequestServices.GetRequiredService<
                    IncidentTelemetry>().RateLimitRejections.Add(1);
                return ValueTask.CompletedTask;
            };
        });
    }

    private static int Limit(IConfiguration config, string name, int defaultValue)
    {
        var setting = config[$"{SectionName}:{name}"];
        if (string.IsNullOrWhiteSpace(setting)) return defaultValue;
        if (!int.TryParse(setting, out var value) || value < 1 || value > 100000)
            throw new InvalidOperationException($"{SectionName}:{name} must be from 1 through 100000.");
        return value;
    }
}
