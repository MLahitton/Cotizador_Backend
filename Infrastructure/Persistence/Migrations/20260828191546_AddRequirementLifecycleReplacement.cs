using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementLifecycleReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requirements_pre_quote_id_is_active",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_requirements_status",
                schema: "core",
                table: "requirements");

            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_requirement_id",
                schema: "core",
                table: "requirements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_requirement_id",
                schema: "core",
                table: "requirements",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY pre_quote_id
                            ORDER BY created_at_utc DESC, id DESC
                        ) AS rn
                    FROM core.requirements
                    WHERE is_active = TRUE
                )
                UPDATE core.requirements r
                SET is_active = FALSE
                FROM ranked x
                WHERE r.id = x.id
                  AND x.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_requirements_pre_quote_id_is_active",
                schema: "core",
                table: "requirements",
                columns: new[] { "pre_quote_id", "is_active" },
                unique: true,
                filter: "\"is_active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_requirements_supersedes_requirement_id",
                schema: "core",
                table: "requirements",
                column: "supersedes_requirement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_requirements_superseded_by_requirement_id",
                schema: "core",
                table: "requirements",
                column: "superseded_by_requirement_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirements_status",
                schema: "core",
                table: "requirements",
                sql: "\"status\" IN ('Pending', 'Processing', 'Processed', 'Failed', 'Cancelled', 'Superseded')");

            migrationBuilder.AddForeignKey(
                name: "FK_requirements_requirements_supersedes_requirement_id",
                schema: "core",
                table: "requirements",
                column: "supersedes_requirement_id",
                principalSchema: "core",
                principalTable: "requirements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_requirements_requirements_supersedes_requirement_id",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropIndex(
                name: "ix_requirements_pre_quote_id_is_active",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropIndex(
                name: "ix_requirements_supersedes_requirement_id",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropIndex(
                name: "ux_requirements_superseded_by_requirement_id",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_requirements_status",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropColumn(
                name: "superseded_by_requirement_id",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropColumn(
                name: "supersedes_requirement_id",
                schema: "core",
                table: "requirements");

            migrationBuilder.CreateIndex(
                name: "ix_requirements_pre_quote_id_is_active",
                schema: "core",
                table: "requirements",
                columns: new[] { "pre_quote_id", "is_active" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirements_status",
                schema: "core",
                table: "requirements",
                sql: "\"status\" IN ('Pending', 'Processing', 'Processed', 'Failed')");
        }
    }
}
