using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalProposalItemSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "selected_at_utc",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_finish_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_glass_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_system_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposal_items_selected_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposal_items_selected_finish_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_finish_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposal_items_selected_glass_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_glass_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposal_items_selected_system_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_system_id");

            migrationBuilder.AddForeignKey(
                name: "FK_requirement_technical_proposal_items_finish_types_selected_~",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_finish_type_id",
                principalSchema: "core",
                principalTable: "finish_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_requirement_technical_proposal_items_glass_types_selected_g~",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_glass_type_id",
                principalSchema: "core",
                principalTable: "glass_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_requirement_technical_proposal_items_product_systems_select~",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_system_id",
                principalSchema: "core",
                principalTable: "product_systems",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_requirement_technical_proposal_items_users_selected_by_user~",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "selected_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_requirement_technical_proposal_items_finish_types_selected_~",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropForeignKey(
                name: "FK_requirement_technical_proposal_items_glass_types_selected_g~",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropForeignKey(
                name: "FK_requirement_technical_proposal_items_product_systems_select~",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropForeignKey(
                name: "FK_requirement_technical_proposal_items_users_selected_by_user~",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropIndex(
                name: "ix_requirement_technical_proposal_items_selected_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropIndex(
                name: "ix_requirement_technical_proposal_items_selected_finish_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropIndex(
                name: "ix_requirement_technical_proposal_items_selected_glass_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropIndex(
                name: "ix_requirement_technical_proposal_items_selected_system_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "selected_at_utc",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "selected_by_user_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "selected_finish_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "selected_glass_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "selected_system_id",
                schema: "core",
                table: "requirement_technical_proposal_items");
        }
    }
}
