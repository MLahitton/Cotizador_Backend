using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobLeaseOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_lease_operations",
                schema: "core",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lease_generation = table.Column<long>(type: "bigint", nullable: false),
                    policy_version = table.Column<short>(type: "smallint", nullable: false),
                    expected_lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_processing_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_lease_operations", x => x.operation_id);
                    table.CheckConstraint("ck_lease_operation_ids", "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND requirement_id <> '00000000-0000-0000-0000-000000000000'::uuid AND execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND claim_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND (expected_processing_attempt_id IS NULL OR expected_processing_attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("ck_lease_operation_owner", "length(lease_owner) = 78 AND lease_owner ~ '^exec:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' AND substring(lease_owner from 6 for 36) <> '00000000-0000-0000-0000-000000000000' AND substring(lease_owner from 43 for 36) = claim_operation_id::text");
                    table.CheckConstraint("ck_lease_operation_shape", "kind IN ('Claim', 'Renew') AND lease_generation = 1 AND policy_version = 1 AND isfinite(assigned_at_utc) AND isfinite(applied_at_utc) AND isfinite(expires_at_utc) AND expires_at_utc > assigned_at_utc AND ((kind = 'Claim' AND claim_operation_id = operation_id AND expected_lease_expires_at_utc IS NULL AND expected_processing_attempt_id IS NULL AND assigned_at_utc = applied_at_utc AND expires_at_utc = applied_at_utc + interval '120 seconds') OR (kind = 'Renew' AND claim_operation_id <> operation_id AND expected_lease_expires_at_utc IS NOT NULL AND isfinite(expected_lease_expires_at_utc) AND applied_at_utc >= assigned_at_utc AND expected_lease_expires_at_utc > applied_at_utc AND expires_at_utc = greatest(expected_lease_expires_at_utc, applied_at_utc + interval '120 seconds')))");
                    table.ForeignKey(
                        name: "fk_lease_operation_actor",
                        column: x => x.actor_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lease_operation_claim",
                        column: x => x.claim_operation_id,
                        principalSchema: "core",
                        principalTable: "job_lease_operations",
                        principalColumn: "operation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lease_operation_job",
                        column: x => x.job_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lease_operation_requirement",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_lease_operations_actor_user_id",
                schema: "core",
                table: "job_lease_operations",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_lease_operations_claim_operation_id",
                schema: "core",
                table: "job_lease_operations",
                column: "claim_operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_lease_operations_requirement_id",
                schema: "core",
                table: "job_lease_operations",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ux_lease_operation_claim_job",
                schema: "core",
                table: "job_lease_operations",
                column: "job_id",
                unique: true,
                filter: "kind = 'Claim'");

            migrationBuilder.CreateIndex(
                name: "ux_lease_operation_claim_owner",
                schema: "core",
                table: "job_lease_operations",
                column: "lease_owner",
                unique: true,
                filter: "kind = 'Claim'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_lease_operations",
                schema: "core");
        }
    }
}
