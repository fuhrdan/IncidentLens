using IncidentLens.Api.Domain;
using IncidentLens.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentLens.Api.Data;

public sealed class IncidentLensDbContext(DbContextOptions<IncidentLensDbContext> options, TenantContext tenant)
    : DbContext(options)
{
    public string CurrentTenantId => tenant.TenantId;

    public DbSet<IncidentRecord> Incidents => Set<IncidentRecord>();
    public DbSet<TimelineEventRecord> TimelineEvents => Set<TimelineEventRecord>();
    public DbSet<IncidentResponderRecord> Responders => Set<IncidentResponderRecord>();
    public DbSet<IncidentTagRecord> Tags => Set<IncidentTagRecord>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<AlertIngestionRecord> AlertIngestions => Set<AlertIngestionRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ServiceObjectiveRecord> ServiceObjectives => Set<ServiceObjectiveRecord>();
    public DbSet<ServiceHealthSnapshotRecord> ServiceHealthSnapshots => Set<ServiceHealthSnapshotRecord>();
    public DbSet<PostmortemRecord> Postmortems => Set<PostmortemRecord>();
    public DbSet<PostmortemActionItemRecord> PostmortemActionItems => Set<PostmortemActionItemRecord>();
    public DbSet<MaintenanceWindowRecord> MaintenanceWindows => Set<MaintenanceWindowRecord>();
    public DbSet<ReliabilitySignalRecord> ReliabilitySignals => Set<ReliabilitySignalRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTenant<IncidentRecord>(modelBuilder);
        ConfigureTenant<TimelineEventRecord>(modelBuilder);
        ConfigureTenant<IncidentResponderRecord>(modelBuilder);
        ConfigureTenant<IncidentTagRecord>(modelBuilder);
        ConfigureTenant<AuditRecord>(modelBuilder);
        ConfigureTenant<AlertIngestionRecord>(modelBuilder);
        ConfigureTenant<OutboxMessage>(modelBuilder);
        ConfigureTenant<ServiceObjectiveRecord>(modelBuilder);
        ConfigureTenant<ServiceHealthSnapshotRecord>(modelBuilder);
        ConfigureTenant<PostmortemRecord>(modelBuilder);
        ConfigureTenant<PostmortemActionItemRecord>(modelBuilder);
        ConfigureTenant<MaintenanceWindowRecord>(modelBuilder);
        ConfigureTenant<ReliabilitySignalRecord>(modelBuilder);
        var incident = modelBuilder.Entity<IncidentRecord>();
        incident.HasKey(item => item.Id);
        incident.HasIndex(item => new { item.TenantId, item.Sequence }).IsUnique();
        incident.Property(item => item.Version).IsConcurrencyToken();
        incident.Property(item => item.Title).HasMaxLength(120);
        incident.Property(item => item.Summary).HasMaxLength(500);
        incident.Property(item => item.Service).HasMaxLength(80);
        incident.Property(item => item.OwnerTeam).HasMaxLength(80);
        incident.Property(item => item.Assignee).HasMaxLength(80);
        incident.Property(item => item.Severity).HasConversion<string>().HasMaxLength(12);
        incident.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
        incident.HasOne(item => item.Postmortem).WithOne(item => item.Incident)
            .HasForeignKey<PostmortemRecord>(item => item.IncidentId).OnDelete(DeleteBehavior.Cascade);

        ConfigureChild<TimelineEventRecord>(modelBuilder, item => item.Timeline);
        ConfigureChild<IncidentResponderRecord>(modelBuilder, item => item.Responders);
        ConfigureChild<IncidentTagRecord>(modelBuilder, item => item.Tags);
        ConfigureChild<AuditRecord>(modelBuilder, item => item.AuditRecords);

        var timelineEvent = modelBuilder.Entity<TimelineEventRecord>();
        timelineEvent.HasIndex(item => new { item.IncidentId, item.OccurredAt });
        timelineEvent.Property(item => item.Actor).HasMaxLength(80);
        timelineEvent.Property(item => item.Message).HasMaxLength(500);
        timelineEvent.Property(item => item.Type).HasConversion<string>().HasMaxLength(20);
        timelineEvent.Property(item => item.CommandName).HasMaxLength(32);
        timelineEvent.Property(item => item.CommandArguments).HasMaxLength(400);
        timelineEvent.Property(item => item.MentionsJson).HasMaxLength(1000);

        var responder = modelBuilder.Entity<IncidentResponderRecord>();
        responder.HasIndex(item => new { item.IncidentId, item.Name });
        responder.Property(item => item.Name).HasMaxLength(80);
        responder.Property(item => item.Role).HasMaxLength(60);

        var tag = modelBuilder.Entity<IncidentTagRecord>();
        tag.HasIndex(item => new { item.IncidentId, item.Value }).IsUnique();
        tag.Property(item => item.Value).HasMaxLength(32);

        var audit = modelBuilder.Entity<AuditRecord>();
        audit.HasIndex(item => new { item.IncidentId, item.OccurredAt });
        audit.Property(item => item.Actor).HasMaxLength(80);
        audit.Property(item => item.Action).HasMaxLength(60);
        audit.Property(item => item.Details).HasMaxLength(500);

        var alert = modelBuilder.Entity<AlertIngestionRecord>();
        alert.HasKey(item => item.Id);
        alert.HasIndex(item => new { item.TenantId, item.IdempotencyKey }).IsUnique();
        alert.Property(item => item.IdempotencyKey).HasMaxLength(120);
        alert.HasOne(item => item.Incident).WithMany().HasForeignKey(item => item.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.HasKey(item => item.Id);
        outbox.HasIndex(item => new { item.ProcessedAt, item.NextAttemptAt });
        outbox.Property(item => item.Type).HasMaxLength(80);
        outbox.Property(item => item.AggregateId).HasMaxLength(80);
        outbox.Property(item => item.Payload).HasColumnType("text");
        outbox.Property(item => item.LastError).HasMaxLength(1000);

        var objective = modelBuilder.Entity<ServiceObjectiveRecord>();
        objective.HasKey(item => item.Id);
        objective.HasIndex(item => new { item.TenantId, item.Service }).IsUnique();
        objective.Property(item => item.Version).IsConcurrencyToken();
        objective.Property(item => item.Service).HasMaxLength(80);
        objective.Property(item => item.OwnerTeam).HasMaxLength(80);
        objective.Property(item => item.AvailabilityTargetPercent).HasPrecision(6, 3);
        objective.Property(item => item.FastBurnThreshold).HasPrecision(8, 2);
        objective.Property(item => item.SlowBurnThreshold).HasPrecision(8, 2);

        var health = modelBuilder.Entity<ServiceHealthSnapshotRecord>();
        health.HasKey(item => item.Id);
        health.HasIndex(item => new { item.Service, item.CapturedAt });
        health.Property(item => item.Service).HasMaxLength(80);
        health.Property(item => item.AvailabilityPercent).HasPrecision(6, 3);
        health.Property(item => item.ErrorRatePercent).HasPrecision(6, 3);

        var maintenance = modelBuilder.Entity<MaintenanceWindowRecord>();
        maintenance.HasKey(item => item.Id);
        maintenance.Property(item => item.Version).IsConcurrencyToken();
        maintenance.HasIndex(item => new { item.Service, item.StartsAt, item.EndsAt });
        maintenance.Property(item => item.Service).HasMaxLength(80);
        maintenance.Property(item => item.Title).HasMaxLength(160);
        maintenance.Property(item => item.CreatedBy).HasMaxLength(80);

        var signal = modelBuilder.Entity<ReliabilitySignalRecord>();
        signal.HasKey(item => item.Id);
        signal.Property(item => item.Version).IsConcurrencyToken();
        signal.HasIndex(item => new { item.TenantId, item.Fingerprint }).IsUnique();
        signal.HasIndex(item => new { item.Status, item.LastObservedAt });
        signal.Property(item => item.Fingerprint).HasMaxLength(180);
        signal.Property(item => item.Service).HasMaxLength(80);
        signal.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        signal.Property(item => item.SuggestedSeverity).HasConversion<string>().HasMaxLength(12);
        signal.Property(item => item.BurnRate).HasPrecision(10, 2);
        signal.Property(item => item.ObservedAvailabilityPercent).HasPrecision(6, 3);
        signal.Property(item => item.TargetAvailabilityPercent).HasPrecision(6, 3);
        signal.Property(item => item.Summary).HasMaxLength(500);
        signal.Property(item => item.AcknowledgedBy).HasMaxLength(80);
        signal.Property(item => item.SuppressionReason).HasMaxLength(240);
        signal.HasOne(item => item.Incident).WithMany().HasForeignKey(item => item.IncidentId)
            .OnDelete(DeleteBehavior.SetNull);

        var postmortem = modelBuilder.Entity<PostmortemRecord>();
        postmortem.HasKey(item => item.Id);
        postmortem.HasIndex(item => item.IncidentId).IsUnique();
        postmortem.Property(item => item.Version).IsConcurrencyToken();
        postmortem.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        postmortem.Property(item => item.Owner).HasMaxLength(80);
        postmortem.Property(item => item.ExecutiveSummary).HasMaxLength(1000);
        postmortem.Property(item => item.RootCause).HasMaxLength(2000);
        postmortem.Property(item => item.Detection).HasMaxLength(1000);
        postmortem.Property(item => item.Resolution).HasMaxLength(2000);
        postmortem.Property(item => item.LessonsLearned).HasMaxLength(2000);
        postmortem.HasMany(item => item.ActionItems).WithOne(item => item.Postmortem)
            .HasForeignKey(item => item.PostmortemId).OnDelete(DeleteBehavior.Cascade);

        var action = modelBuilder.Entity<PostmortemActionItemRecord>();
        action.HasKey(item => item.Id);
        action.HasIndex(item => new { item.Owner, item.Status, item.DueAt });
        action.Property(item => item.Title).HasMaxLength(240);
        action.Property(item => item.Owner).HasMaxLength(80);
        action.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
    }

    private void ConfigureTenant<TEntity>(ModelBuilder builder)
        where TEntity : class, ITenantOwned
    {
        builder.Entity<TEntity>().Property(item => item.TenantId)
            .HasMaxLength(64).IsRequired();
        // The filter captures this *DbContext instance*, not the startup tenant.
        builder.Entity<TEntity>().HasQueryFilter(item => item.TenantId == CurrentTenantId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceTenantOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceTenantOwnership()
    {
        var current = CurrentTenantId;
        if (string.IsNullOrWhiteSpace(current))
            throw new InvalidOperationException("A validated tenant is required for database writes.");
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = current;
            if (!string.Equals(entry.Entity.TenantId, current, StringComparison.Ordinal))
                throw new InvalidOperationException("Cross-tenant database writes are forbidden.");
            if ((entry.State is EntityState.Modified or EntityState.Deleted) &&
                !string.Equals(entry.OriginalValues[nameof(ITenantOwned.TenantId)] as string,
                    current, StringComparison.Ordinal))
                throw new InvalidOperationException("The tenant of an existing record cannot change.");
        }
    }

    private static void ConfigureChild<TChild>(
        ModelBuilder modelBuilder,
        System.Linq.Expressions.Expression<Func<IncidentRecord, IEnumerable<TChild>?>> navigation)
        where TChild : class
    {
        modelBuilder.Entity<IncidentRecord>()
            .HasMany(navigation)
            .WithOne("Incident")
            .HasForeignKey("IncidentId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
