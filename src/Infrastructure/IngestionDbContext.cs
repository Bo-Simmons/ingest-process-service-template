using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public DbSet<IngestionJob> IngestionJobs => Set<IngestionJob>();
    public DbSet<RawEvent> RawEvents => Set<RawEvent>();
    public DbSet<IngestionResult> IngestionResults => Set<IngestionResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionJob>(e =>
        {
            e.ToTable("ingestion_jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(256);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique().HasFilter("\"idempotency_key\" IS NOT NULL");
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
