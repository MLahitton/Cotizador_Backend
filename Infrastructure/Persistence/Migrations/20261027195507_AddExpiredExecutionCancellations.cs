using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiredExecutionCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expired_execution_cancellations",
                schema: "core",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_by_actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lease_generation = table.Column<long>(type: "bigint", nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_processing_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    guard_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    policy_version = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expired_execution_cancellations", x => x.operation_id);
                    table.CheckConstraint("ck_expired_cancellation_ids", "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND requirement_id <> '00000000-0000-0000-0000-000000000000'::uuid AND execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND claim_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND closed_by_actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND (expected_processing_attempt_id IS NULL OR expected_processing_attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("ck_expired_cancellation_owner", "length(lease_owner) = 78 AND lease_owner ~ '^exec:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' AND substring(lease_owner from 6 for 36) <> '00000000-0000-0000-0000-000000000000' AND substring(lease_owner from 43 for 36) = claim_operation_id::text");
                    table.CheckConstraint("ck_expired_cancellation_shape", "lease_generation = 1 AND policy_version = 1 AND isfinite(assigned_at_utc) AND isfinite(expected_lease_expires_at_utc) AND isfinite(cancellation_requested_at_utc) AND isfinite(finished_at_utc) AND isfinite(guard_at_utc) AND assigned_at_utc < expected_lease_expires_at_utc AND expected_lease_expires_at_utc <= finished_at_utc AND finished_at_utc <= guard_at_utc AND cancellation_requested_at_utc <= finished_at_utc");
                    table.ForeignKey(
                        name: "fk_expired_cancellation_actor",
                        column: x => x.closed_by_actor_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expired_cancellation_attempt",
                        column: x => x.expected_processing_attempt_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expired_cancellation_claim",
                        column: x => x.claim_operation_id,
                        principalSchema: "core",
                        principalTable: "job_lease_operations",
                        principalColumn: "operation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expired_cancellation_job",
                        column: x => x.job_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expired_cancellation_requirement",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expired_execution_cancellations_claim_operation_id",
                schema: "core",
                table: "expired_execution_cancellations",
                column: "claim_operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_expired_execution_cancellations_closed_by_actor_user_id",
                schema: "core",
                table: "expired_execution_cancellations",
                column: "closed_by_actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_expired_execution_cancellations_expected_processing_attempt~",
                schema: "core",
                table: "expired_execution_cancellations",
                column: "expected_processing_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_expired_execution_cancellations_requirement_id",
                schema: "core",
                table: "expired_execution_cancellations",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ux_expired_cancellation_job",
                schema: "core",
                table: "expired_execution_cancellations",
                column: "job_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expired_execution_cancellations",
                schema: "core");
        }
    }
}
