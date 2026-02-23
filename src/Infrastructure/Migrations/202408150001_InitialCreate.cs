using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Initial schema migration that creates ingestion jobs, raw events, and result tables.
/// </summary>
[DbContext(typeof(IngestionDbContext))]
[Migration("202408150001_InitialCreate")]
public partial class InitialCreate : Migration
{
    /// <summary>
    /// Applies schema objects required for the ingestion service.
    /// </summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ingestion_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                attempt = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                locked_by = table.Column<string>(type: "text", nullable: true),
                error = table.Column<string>(type: "text", nullable: true),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_ingestion_jobs", x => x.id));

        migrationBuilder.CreateTable(
            name: "raw_events",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                job_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                payload_json = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_raw_events", x => x.id);
                table.ForeignKey("fk_raw_events_ingestion_jobs_job_id", x => x.job_id, "ingestion_jobs", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ingestion_results",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                job_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ingestion_results", x => x.id);
                table.ForeignKey("fk_ingestion_results_ingestion_jobs_job_id", x => x.job_id, "ingestion_jobs", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("ix_ingestion_jobs_status_available_at", "ingestion_jobs", new[] { "status", "available_at" });
        migrationBuilder.CreateIndex("ix_ingestion_jobs_tenant_id_idempotency_key", "ingestion_jobs", new[] { "tenant_id", "idempotency_key" }, unique: true, filter: "idempotency_key IS NOT NULL");
        migrationBuilder.CreateIndex("ix_raw_events_job_id", "raw_events", "job_id");
        migrationBuilder.CreateIndex("ix_ingestion_results_job_id", "ingestion_results", "job_id");
    }

    /// <summary>
    /// Rolls back the schema objects created by this migration.
    /// </summary>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ingestion_results");
        migrationBuilder.DropTable("raw_events");
        migrationBuilder.DropTable("ingestion_jobs");
    }
}
