using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

/// <summary>
/// EF Core database context for ingestion jobs, raw events, and aggregated results.
/// </summary>
public sealed class IngestionDbContext : DbContext
{
    public IngestionDbContext(DbContextOptions<IngestionDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Table access for ingestion job records.
    /// </summary>
    public DbSet<IngestionJob> IngestionJobs => Set<IngestionJob>();

    /// <summary>
    /// Table access for raw event records.
    /// </summary>
    public DbSet<RawEvent> RawEvents => Set<RawEvent>();

    /// <summary>
    /// Table access for aggregated ingestion result records.
    /// </summary>
    public DbSet<IngestionResult> IngestionResults => Set<IngestionResult>();

    /// <summary>
    /// Configures table names, keys, indexes, and relationships.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionJob>(e =>
        {
            e.ToTable("ingestion_jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Attempt).HasColumnName("attempt");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.AvailableAt).HasColumnName("available_at");
            e.Property(x => x.LockedAt).HasColumnName("locked_at");
            e.Property(x => x.LockedBy).HasColumnName("locked_by");
            e.Property(x => x.Error).HasColumnName("error");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.Status, x.AvailableAt });
        });

        modelBuilder.Entity<RawEvent>(e =>
        {
            e.ToTable("raw_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.JobId).HasColumnName("job_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(128).IsRequired();
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.Payload).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.JobId);
            e.HasOne(x => x.Job).WithMany(x => x.RawEvents).HasForeignKey(x => x.JobId);
        });

        modelBuilder.Entity<IngestionResult>(e =>
        {
            e.ToTable("ingestion_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.JobId).HasColumnName("job_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128).IsRequired();
            e.Property(x => x.Count).HasColumnName("count");
            e.HasIndex(x => x.JobId);
            e.HasOne(x => x.Job).WithMany(x => x.Results).HasForeignKey(x => x.JobId);
        });
    }
}
