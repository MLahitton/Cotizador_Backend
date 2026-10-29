using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementProcessingJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_processing_jobs",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    execution_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retry_of_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trigger_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_generation = table.Column<long>(type: "bigint", nullable: false),
                    cancellation_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_processing_jobs", x => x.id);
                    table.CheckConstraint("ck_processing_jobs_attempt", "(attempt_count = 0 AND processing_attempt_id IS NULL) OR (attempt_count = 1 AND processing_attempt_id IS NOT NULL)");
                    table.CheckConstraint("ck_processing_jobs_dates", "(started_at_utc IS NULL OR started_at_utc >= created_at_utc) AND (cancellation_requested_at_utc IS NULL OR (cancellation_requested_at_utc >= created_at_utc AND (started_at_utc IS NULL OR cancellation_requested_at_utc >= started_at_utc))) AND (finished_at_utc IS NULL OR (finished_at_utc >= created_at_utc AND (started_at_utc IS NULL OR finished_at_utc >= started_at_utc) AND (cancellation_requested_at_utc IS NULL OR finished_at_utc >= cancellation_requested_at_utc)))");
                    table.CheckConstraint("ck_processing_jobs_error", "(state = 'Failed' AND last_error_code IS NOT NULL AND last_error_code ~ '^[A-Z][A-Z0-9_]{0,63}$') OR (state <> 'Failed' AND last_error_code IS NULL)");
                    table.CheckConstraint("ck_processing_jobs_identity", "execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_processing_jobs_lifecycle", "(state = 'Queued' AND started_at_utc IS NULL AND finished_at_utc IS NULL AND lease_owner IS NULL AND lease_expires_at_utc IS NULL AND lease_generation = 0 AND attempt_count = 0 AND cancellation_requested_at_utc IS NULL) OR (state = 'Running' AND started_at_utc IS NOT NULL AND finished_at_utc IS NULL AND lease_owner IS NOT NULL AND btrim(lease_owner) <> '' AND lease_expires_at_utc IS NOT NULL AND lease_expires_at_utc > started_at_utc AND lease_generation = 1) OR (state IN ('Succeeded', 'Failed', 'Cancelled') AND finished_at_utc IS NOT NULL AND lease_owner IS NULL AND lease_expires_at_utc IS NULL AND ((started_at_utc IS NOT NULL AND lease_generation = 1) OR (state = 'Cancelled' AND started_at_utc IS NULL AND lease_generation = 0 AND attempt_count = 0)) AND ((state = 'Cancelled' AND cancellation_requested_at_utc IS NOT NULL) OR (state IN ('Succeeded', 'Failed') AND cancellation_requested_at_utc IS NULL AND started_at_utc IS NOT NULL)) AND (state <> 'Succeeded' OR attempt_count = 1))");
                    table.CheckConstraint("ck_processing_jobs_trigger", "trigger_kind IN ('PublicPreparation', 'ManualInitial', 'ManualRetry') AND ((trigger_kind = 'ManualRetry' AND retry_of_job_id IS NOT NULL AND retry_of_job_id <> id) OR (trigger_kind <> 'ManualRetry' AND retry_of_job_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_requirement_processing_jobs_public_experience_submissions_s~",
                        column: x => x.submission_id,
                        principalSchema: "core",
                        principalTable: "public_experience_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_processing_jobs_requirement_processing_attempts~",
                        column: x => x.processing_attempt_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_processing_jobs_requirement_processing_jobs_ret~",
                        column: x => x.retry_of_job_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_processing_jobs_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_processing_jobs_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_requirement",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_state_created_id",
                schema: "core",
                table: "requirement_processing_jobs",
                columns: new[] { "state", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_requirement_processing_jobs_requested_by_user_id",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_processing_jobs_retry_of_job_id",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "retry_of_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_processing_jobs_submission_id",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ux_processing_jobs_active_requirement",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "requirement_id",
                unique: true,
                filter: "state IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ux_processing_jobs_attempt",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "processing_attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_processing_jobs_execution_request",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "execution_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_processing_jobs",
                schema: "core");
        }
    }
}
