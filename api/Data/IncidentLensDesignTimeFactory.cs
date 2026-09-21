using IncidentLens.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.FileProviders;

namespace IncidentLens.Api.Data;

/// <summary>
/// Keeps EF migration scripting independent of production startup authentication.
/// Runtime context creation continues to use request-scoped validated tenancy.
/// Supply ConnectionStrings__PostgreSql to run migrations against a real DB.
/// </summary>
public sealed class IncidentLensDesignTimeFactory : IDesignTimeDbContextFactory<IncidentLensDbContext>
{
    public IncidentLensDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql")
            ?? "Host=localhost;Port=5432;Database=incidentlens;Username=incidentlens;Password=change-me";
        var options = new DbContextOptionsBuilder<IncidentLensDbContext>()
            .UseNpgsql(connection).Options;
        var environment = new DesignTimeEnvironment();
        var tenant = new TenantContext(new HttpContextAccessor(), environment,
            new ConfigurationBuilder().Build());
        return new IncidentLensDbContext(options, tenant);
    }

    private sealed class DesignTimeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "IncidentLens.Api";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
