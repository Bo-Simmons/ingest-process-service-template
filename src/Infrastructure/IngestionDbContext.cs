using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

/// <summary>
/// EF Core database context for ingestion jobs, raw events, and aggregated results.
/// </summary>
public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
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
            e.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(256);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.Status, x.AvailableAt });
        });

        modelBuilder.Entity<RawEvent>(e =>
        {
            e.ToTable("raw_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(128).IsRequired();
            e.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => x.JobId);
            e.HasOne(x => x.Job).WithMany(x => x.RawEvents).HasForeignKey(x => x.JobId);
        });

        modelBuilder.Entity<IngestionResult>(e =>
        {
            e.ToTable("ingestion_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.JobId);
            e.HasOne(x => x.Job).WithMany(x => x.Results).HasForeignKey(x => x.JobId);
        });
    }
}
