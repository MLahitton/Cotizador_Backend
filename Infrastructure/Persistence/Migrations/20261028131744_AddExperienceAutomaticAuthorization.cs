using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExperienceAutomaticAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "authorization_expires_at_utc",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "authorization_revoked_at_utc",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "authorized_actor_user_id",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "authorized_at_utc",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "automatic_authorization_policy_version",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatch_admitted_at_utc",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_claim_operation_id",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_instance_id",
                schema: "core",
                table: "requirement_processing_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_processing_jobs_authorized_actor_user_id",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "authorized_actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_processing_jobs_automatic_submission",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "submission_id",
                unique: true,
                filter: "automatic_authorization_policy_version IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_automatic_authorization",
                schema: "core",
                table: "requirement_processing_jobs",
                sql: "(automatic_authorization_policy_version IS NULL AND authorized_actor_user_id IS NULL AND authorized_at_utc IS NULL AND authorization_expires_at_utc IS NULL) OR (automatic_authorization_policy_version IS NOT NULL AND automatic_authorization_policy_version = 1 AND authorized_actor_user_id IS NOT NULL AND authorized_actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND authorized_at_utc IS NOT NULL AND authorization_expires_at_utc IS NOT NULL AND isfinite(authorized_at_utc) AND isfinite(authorization_expires_at_utc) AND authorization_expires_at_utc - authorized_at_utc = interval '24 hours' AND trigger_kind = 'PublicPreparation' AND required_publication_version = 'sng-job-publication-v2' AND submission_id IS NOT NULL AND submission_id <> '00000000-0000-0000-0000-000000000000'::uuid AND retry_of_job_id IS NULL AND requested_by_user_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_automatic_dispatch",
                schema: "core",
                table: "requirement_processing_jobs",
                sql: "(dispatch_admitted_at_utc IS NULL AND dispatch_instance_id IS NULL AND dispatch_claim_operation_id IS NULL) OR (automatic_authorization_policy_version IS NOT NULL AND dispatch_admitted_at_utc IS NOT NULL AND dispatch_instance_id IS NOT NULL AND dispatch_claim_operation_id IS NOT NULL AND dispatch_instance_id <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatch_claim_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(dispatch_admitted_at_utc) AND dispatch_admitted_at_utc >= authorized_at_utc AND dispatch_admitted_at_utc < authorization_expires_at_utc)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_automatic_revocation",
                schema: "core",
                table: "requirement_processing_jobs",
                sql: "authorization_revoked_at_utc IS NULL OR (automatic_authorization_policy_version IS NOT NULL AND isfinite(authorization_revoked_at_utc) AND authorization_revoked_at_utc >= authorized_at_utc AND (dispatch_admitted_at_utc IS NULL OR authorization_revoked_at_utc >= dispatch_admitted_at_utc))");

            migrationBuilder.AddForeignKey(
                name: "FK_requirement_processing_jobs_users_authorized_actor_user_id",
                schema: "core",
                table: "requirement_processing_jobs",
                column: "authorized_actor_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_requirement_processing_jobs_users_authorized_actor_user_id",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropIndex(
                name: "IX_requirement_processing_jobs_authorized_actor_user_id",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropIndex(
                name: "ux_processing_jobs_automatic_submission",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_automatic_authorization",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_automatic_dispatch",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_automatic_revocation",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "authorization_expires_at_utc",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "authorization_revoked_at_utc",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "authorized_actor_user_id",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "authorized_at_utc",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "automatic_authorization_policy_version",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "dispatch_admitted_at_utc",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "dispatch_claim_operation_id",
                schema: "core",
                table: "requirement_processing_jobs");

            migrationBuilder.DropColumn(
                name: "dispatch_instance_id",
                schema: "core",
                table: "requirement_processing_jobs");

        }
    }
}
