using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IncidentLens.Api.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"incidentlens-tests-{Guid.NewGuid():N}.db");

    // Per-suite overrides allow isolated rate-limit tests without changing other
    // integration suites' shared quota and database fixtures.
    protected virtual IDictionary<string, string?> ConfigurationOverrides =>
        new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = $"Data Source={databasePath}",
                ["Authentication:Authority"] = string.Empty,
                ["Retention:AllowPurge"] = "true",
                ["Retention:MaxRowsPerRun"] = "100",
                ["RateLimiting:DemoAuthPerMinute"] = "10000",
            });
            configuration.AddInMemoryCollection(ConfigurationOverrides);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
