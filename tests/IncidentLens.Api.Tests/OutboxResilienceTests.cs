using System.Text.Json;
using IncidentLens.Api.Contracts;
using IncidentLens.Api.Data;
using IncidentLens.Api.Domain;
using IncidentLens.Api.Observability;
using IncidentLens.Api.Realtime;
using IncidentLens.Api.Security;
using IncidentLens.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentLens.Api.Tests;

public sealed class OutboxResilienceTests
{
    [Fact]
    public async Task Delayed_retries_do_not_starve_ready_messages_and_publish_to_the_right_tenant()
    {
        await using var context = await TestContext.CreateAsync();
        var now = context.Clock.GetUtcNow();
        var ready = Guid.NewGuid();
        await context.InsertAsync(Enumerable.Range(0, 105).Select(index =>
            Message(Guid.NewGuid(), "alpha", now.AddMinutes(-5), now.AddMinutes(5)))
            .Append(Message(ready, "beta", now, now)));

        await context.Dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Single(context.Publisher.Delivered);
        Assert.Equal("beta", context.Publisher.Delivered[0]);
        await using var scope = context.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
        Assert.NotNull((await db.OutboxMessages.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == ready)).ProcessedAt);
        Assert.Equal(105, await db.OutboxMessages.IgnoreQueryFilters()
            .CountAsync(item => item.ProcessedAt == null));
    }

    [Fact]
    public async Task Publish_failure_is_retried_without_marking_an_event_delivered()
    {
        await using var context = await TestContext.CreateAsync();
        var now = context.Clock.GetUtcNow();
        var id = Guid.NewGuid();
        await context.InsertAsync([Message(id, "alpha", now, now)]);
        context.Publisher.FailNext = true;

        await context.Dispatcher.DispatchBatchAsync(CancellationToken.None);
        await using (var scope = context.Services.CreateAsyncScope())
        {
            var record = await scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>()
                .OutboxMessages.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
            Assert.Null(record.ProcessedAt);
            Assert.Equal(1, record.Attempts);
            Assert.True(record.NextAttemptAt > now);
        }
        context.Clock.Advance(TimeSpan.FromSeconds(3));
        await context.Dispatcher.DispatchBatchAsync(CancellationToken.None);
        await using (var scope = context.Services.CreateAsyncScope())
        {
            var record = await scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>()
                .OutboxMessages.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
            Assert.NotNull(record.ProcessedAt);
            Assert.Equal(1, record.Attempts);
        }
        Assert.Equal(new[] { "alpha" }, context.Publisher.Delivered);
    }

    private static OutboxMessage Message(Guid id, string tenant, DateTimeOffset occurred,
        DateTimeOffset due) => new()
    {
        Id = id,
        TenantId = tenant,
        OccurredAt = occurred,
        NextAttemptAt = due,
        Type = "incident.changed",
        AggregateId = "INC-1043",
        Payload = JsonSerializer.Serialize(new IncidentChangedEvent("INC-1043", 1, "test", occurred)),
    };

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class FakePublisher : IIncidentEventPublisher
    {
        public bool FailNext { get; set; }
        public List<string> Delivered { get; } = [];
        public Task PublishAsync(string tenantId, IncidentChangedEvent message,
            CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("Injected transient transport failure");
            }
            Delivered.Add(tenantId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "IncidentLens.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ServiceProvider Services { get; }
        public FakeClock Clock { get; } = new();
        public FakePublisher Publisher { get; } = new();
        public OutboxDispatcher Dispatcher { get; }

        private TestContext(SqliteConnection connection)
        {
            this.connection = connection;
            Services = new ServiceCollection()
                .AddLogging()
                .AddHttpContextAccessor()
                .AddSingleton<IHostEnvironment>(new FakeEnvironment())
                .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
                .AddSingleton<IncidentTelemetry>()
                .AddScoped<TenantContext>()
                .AddDbContext<IncidentLensDbContext>(options => options.UseSqlite(connection))
                .BuildServiceProvider();
            Dispatcher = new OutboxDispatcher(Services.GetRequiredService<IServiceScopeFactory>(),
                Publisher, Clock, Services.GetRequiredService<IncidentTelemetry>(),
                NullLogger<OutboxDispatcher>.Instance);
        }

        public static async Task<TestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var created = new TestContext(connection);
            await using var scope = created.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>()
                .Database.EnsureCreatedAsync();
            return created;
        }

        public async Task InsertAsync(IEnumerable<OutboxMessage> messages)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IncidentLensDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            foreach (var message in messages)
            {
                using (tenant.ForBackgroundTenant(message.TenantId))
                {
                    db.OutboxMessages.Add(message);
                    await db.SaveChangesAsync();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
