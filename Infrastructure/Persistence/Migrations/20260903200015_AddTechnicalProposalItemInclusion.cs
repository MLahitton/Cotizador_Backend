using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalProposalItemInclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "excluded_at_utc",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "excluded_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exclusion_reason",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inclusion_state",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Included");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposal_items_excluded_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "excluded_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_requirement_technical_proposal_items_users_excluded_by_user~",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "excluded_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_requirement_technical_proposal_items_users_excluded_by_user~",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropIndex(
                name: "ix_requirement_technical_proposal_items_excluded_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "excluded_at_utc",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "excluded_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "exclusion_reason",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "inclusion_state",
                schema: "core",
                table: "requirement_technical_proposal_items");
        }
    }
}

