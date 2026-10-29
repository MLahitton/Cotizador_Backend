using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPublicationReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "publication_content_hash",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "publication_contract_version",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_publication_receipt",
                schema: "core",
                table: "requirement_processing_jobs",
                sql: "(publication_contract_version IS NULL AND publication_content_hash IS NULL) OR (publication_contract_version IS NOT NULL AND publication_content_hash IS NOT NULL AND publication_contract_version ~ '[^[:space:]]' AND publication_content_hash ~ '^[A-F0-9]{64}$' AND state = 'Succeeded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_publication_receipt",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "publication_content_hash",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "publication_contract_version",
                schema: "core",
                table: "requirement_processing_jobs");
        }
    }
}
