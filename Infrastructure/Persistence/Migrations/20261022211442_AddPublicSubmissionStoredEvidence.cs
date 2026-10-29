using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicSubmissionStoredEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stored_evidence_at_utc",
                schema: "core",
                table: "public_experience_submission_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "stored_evidence_version",
                schema: "core",
                table: "public_experience_submission_files",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "stored_length_bytes",
                schema: "core",
                table: "public_experience_submission_files",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_submission_files_stored_evidence",
                schema: "core",
                table: "public_experience_submission_files",
                sql: "(stored_length_bytes IS NULL AND stored_evidence_at_utc IS NULL AND stored_evidence_version IS NULL) OR (stored_length_bytes IS NOT NULL AND stored_evidence_at_utc IS NOT NULL AND stored_evidence_version IS NOT NULL AND stored_evidence_version = 1 AND stored_length_bytes > 0 AND stored_length_bytes = size_bytes AND content_hash IS NOT NULL AND length(content_hash) = 64 AND content_hash ~ '^[0-9A-F]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_submission_files_stored_evidence",
                schema: "core",
                table: "public_experience_submission_files");

            migrationBuilder.DropColumn(
                name: "stored_evidence_at_utc",
                schema: "core",
                table: "public_experience_submission_files");

            migrationBuilder.DropColumn(
                name: "stored_evidence_version",
                schema: "core",
                table: "public_experience_submission_files");

            migrationBuilder.DropColumn(
                name: "stored_length_bytes",
                schema: "core",
                table: "public_experience_submission_files");
        }
    }
}
