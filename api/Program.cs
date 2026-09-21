using System.Text;
using System.Text.Json.Serialization;
using System.Diagnostics;
using IncidentLens.Api.Data;
using IncidentLens.Api.Endpoints;
using IncidentLens.Api.Observability;
using IncidentLens.Api.Realtime;
using IncidentLens.Api.Services;
using IncidentLens.Api.Security;
using IncidentLens.Api.Hardening;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
// Bound incoming JSON bodies before model binding or persistence.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<IncidentLensDbContext>(tags: ["ready"]);
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<PostmortemService>();
builder.Services.AddScoped<EvidenceExportService>();
builder.Services.AddScoped<ReliabilityAutomationService>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddSingleton<IncidentTelemetry>();
builder.Services.AddIncidentLensRateLimiting(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<IIncidentEventPublisher, SignalRIncidentEventPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();

// SQLite is for local development only. Production requires PostgreSQL.
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<IncidentLensDbContext>(options =>
{
    if (databaseProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSql"));
    }
    else
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
    }
});

var jwtKey = builder.Configuration["Jwt:Key"];
var oidcAuthority = builder.Configuration["Authentication:Authority"];
if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(oidcAuthority) ||
        !Uri.TryCreate(oidcAuthority, UriKind.Absolute, out var authorityUri) ||
        authorityUri.Scheme != Uri.UriSchemeHttps ||
        string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Audience"]) ||
        string.IsNullOrWhiteSpace(builder.Configuration["Authentication:ClientId"]))
        throw new InvalidOperationException("Production requires HTTPS Authentication:Authority, Authentication:Audience and public Authentication:ClientId.");
    if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Production requires PostgreSQL.");
}
else if (string.IsNullOrWhiteSpace(oidcAuthority) &&
    (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32))
    throw new InvalidOperationException("Development JWT:Key must contain at least 32 bytes.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrWhiteSpace(oidcAuthority))
        {
            options.Authority = oidcAuthority;
            options.Audience = builder.Configuration["Authentication:Audience"];
            options.RequireHttpsMetadata = true;
        }
        else
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!)),
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        }
        options.TokenValidationParameters ??= new TokenValidationParameters();
        options.TokenValidationParameters.RoleClaimType = builder.Configuration["Authentication:RoleClaimType"]
            ?? System.Security.Claims.ClaimTypes.Role;
        options.Events = new JwtBearerEvents { OnMessageReceived = context => {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/incidents")) context.Token = token;
            return Task.CompletedTask; } };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("IncidentReader", policy => policy.RequireAuthenticatedUser()
        .RequireRole("Viewer", "Commander")
        .RequireAssertion(context => ValidTenant(context.User)))
    .AddPolicy("IncidentCommander", policy => policy.RequireAuthenticatedUser()
        .RequireRole("Commander")
        .RequireAssertion(context => ValidTenant(context.User)));

bool ValidTenant(System.Security.Claims.ClaimsPrincipal user)
{
    var claimType = builder.Configuration["Authentication:TenantClaimType"] ?? "tenant_id";
    return TenantContext.Normalize(user.FindFirst(claimType)?.Value).Length > 0;
}

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment() ? ["http://localhost:4200"] : []);
builder.Services.AddCors(options => options.AddPolicy("IncidentLensWeb", policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: IncidentTelemetry.SourceName,
        serviceVersion: "1.0.0"));
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
telemetry.WithTracing(options =>
{
    options.AddSource(IncidentTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation();
    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        options.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
});
telemetry.WithMetrics(options =>
{
    options.AddMeter(IncidentTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation();
    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        options.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
});

var app = builder.Build();

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    // Never echo or log unbounded or control-character-bearing client identifiers.
    var incoming = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    var correlationId = incoming is { Length: > 0 and <= 64 } &&
        incoming.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
            or >= '0' and <= '9' or '-' or '_')
        ? incoming : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers["Cache-Control"] = "no-store";
    Activity.Current?.SetTag("incidentlens.correlation_id", correlationId);
    using (app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["CorrelationId"] = correlationId,
    }))
    {
        await next();
    }
});
// Only disable this when every external route is HTTPS-enforced by a trusted
// edge proxy or Kubernetes ingress and Kestrel is inaccessible from outside.
if (builder.Configuration.GetValue("ReverseProxy:RedirectToHttps", true))
    app.UseHttpsRedirection();
app.UseCors("IncidentLensWeb");
app.UseAuthentication();
// Authentication MUST precede rate limiting: valid tenants share their own quota.
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(oidcAuthority))
{
    app.MapOpenApi();
    app.MapDemoAuthEndpoints();
}

// Liveness intentionally excludes dependencies. A database outage should make
// pods unready, not create restart loops and extra load on a struggling DB.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
// Preserve the established legacy health URL as a readiness check.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
app.MapClientAuthEndpoints();
app.MapIncidentEndpoints();
app.MapAlertEndpoints();
app.MapAnalyticsEndpoints();
app.MapPostmortemEndpoints();
app.MapReliabilityEndpoints();
app.MapRetentionEndpoints();
app.MapHub<IncidentHub>("/hubs/incidents");

await DatabaseInitializer.InitializeAsync(app.Services, app.Environment.IsDevelopment());
await app.RunAsync();

// Exposes Program to WebApplicationFactory in integration tests.
public partial class Program;
