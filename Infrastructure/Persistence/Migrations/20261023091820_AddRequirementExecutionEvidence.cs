using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementExecutionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_execution_evidence",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_generation = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_version = table.Column<short>(type: "smallint", nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    file_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_evidence", x => x.id);
                    table.CheckConstraint("ck_execution_evidence_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND requirement_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND processing_attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND primary_document_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_execution_evidence_shape", "manifest_version = 1 AND lease_generation = 1 AND file_count > 0 AND manifest_hash ~ '^[0-9A-F]{64}$'");
                    table.ForeignKey(
                        name: "fk_execution_evidence_actor",
                        column: x => x.actor_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_evidence_attempt",
                        column: x => x.processing_attempt_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_evidence_job",
                        column: x => x.job_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_evidence_requirement",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_execution_documents",
                schema: "core",
                columns: table => new
                {
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    declared_length_bytes = table.Column<long>(type: "bigint", nullable: false),
                    expected_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expected_length_bytes = table.Column<long>(type: "bigint", nullable: false),
                    stored_evidence_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stored_evidence_version = table.Column<short>(type: "smallint", nullable: false),
                    observed_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    observed_length_bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_documents", x => new { x.evidence_id, x.file_id });
                    table.CheckConstraint("ck_execution_document_ids", "evidence_id <> '00000000-0000-0000-0000-000000000000'::uuid AND file_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_execution_document_shape", "ordinal >= 0 AND stored_evidence_version = 1 AND declared_length_bytes > 0 AND declared_length_bytes = expected_length_bytes AND expected_length_bytes = observed_length_bytes AND expected_sha256 ~ '^[0-9A-F]{64}$' AND observed_sha256 = expected_sha256");
                    table.ForeignKey(
                        name: "fk_execution_document_evidence",
                        column: x => x.evidence_id,
                        principalSchema: "core",
                        principalTable: "requirement_execution_evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_document_file",
                        column: x => x.file_id,
                        principalSchema: "core",
                        principalTable: "requirement_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_requirement_execution_documents_file_id",
                schema: "core",
                table: "requirement_execution_documents",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ux_execution_document_ordinal",
                schema: "core",
                table: "requirement_execution_documents",
                columns: new[] { "evidence_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_execution_evidence_actor_user_id",
                schema: "core",
                table: "requirement_execution_evidence",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_execution_evidence_requirement_id",
                schema: "core",
                table: "requirement_execution_evidence",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ux_execution_evidence_attempt",
                schema: "core",
                table: "requirement_execution_evidence",
                column: "processing_attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_execution_evidence_job",
                schema: "core",
                table: "requirement_execution_evidence",
                column: "job_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_execution_documents",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_execution_evidence",
                schema: "core");
        }
    }
}
