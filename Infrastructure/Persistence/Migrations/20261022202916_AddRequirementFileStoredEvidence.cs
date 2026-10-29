using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementFileStoredEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stored_evidence_at_utc",
                schema: "core",
                table: "requirement_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "stored_evidence_version",
                schema: "core",
                table: "requirement_files",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "stored_length_bytes",
                schema: "core",
                table: "requirement_files",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stored_sha256",
                schema: "core",
                table: "requirement_files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_files_stored_evidence",
                schema: "core",
                table: "requirement_files",
                sql: "(stored_sha256 IS NULL AND stored_length_bytes IS NULL AND stored_evidence_at_utc IS NULL AND stored_evidence_version IS NULL) OR (stored_sha256 IS NOT NULL AND stored_length_bytes IS NOT NULL AND stored_evidence_at_utc IS NOT NULL AND stored_evidence_version IS NOT NULL AND stored_evidence_version = 1 AND length(stored_sha256) = 64 AND stored_sha256 ~ '^[0-9A-F]{64}$' AND stored_length_bytes > 0 AND stored_length_bytes = size_bytes)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_files_stored_evidence",
                schema: "core",
                table: "requirement_files");

            migrationBuilder.DropColumn(
                name: "stored_evidence_at_utc",
                schema: "core",
                table: "requirement_files");

            migrationBuilder.DropColumn(
                name: "stored_evidence_version",
                schema: "core",
                table: "requirement_files");

            migrationBuilder.DropColumn(
                name: "stored_length_bytes",
                schema: "core",
                table: "requirement_files");

            migrationBuilder.DropColumn(
                name: "stored_sha256",
                schema: "core",
                table: "requirement_files");
        }
    }
}
