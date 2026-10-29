using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequiredPublicationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "required_publication_version",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "sng-job-publication-v1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_required_publication",
                schema: "core",
                table: "requirement_processing_jobs",
                sql: "required_publication_version IN ('sng-job-publication-v1', 'sng-job-publication-v2') AND (publication_contract_version IS NULL OR publication_contract_version = required_publication_version) AND (required_publication_version <> 'sng-job-publication-v2' OR state <> 'Succeeded' OR publication_contract_version IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_required_publication",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "required_publication_version",
                schema: "core",
                table: "requirement_processing_jobs");
        }
    }
}
