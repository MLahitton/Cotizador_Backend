using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalProposalCommercialConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "commercial_confirmed_at_utc",
                schema: "core",
                table: "requirement_technical_proposals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "commercial_confirmed_by_user_id",
                schema: "core",
                table: "requirement_technical_proposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposals_commercial_confirmed_by_user_id",
                schema: "core",
                table: "requirement_technical_proposals",
                column: "commercial_confirmed_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_requirement_technical_proposals_users_commercial_confirmed_~",
                schema: "core",
                table: "requirement_technical_proposals",
                column: "commercial_confirmed_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_requirement_technical_proposals_users_commercial_confirmed_~",
                schema: "core",
                table: "requirement_technical_proposals");

            migrationBuilder.DropIndex(
                name: "ix_requirement_technical_proposals_commercial_confirmed_by_user_id",
                schema: "core",
                table: "requirement_technical_proposals");

            migrationBuilder.DropColumn(
                name: "commercial_confirmed_at_utc",
                schema: "core",
                table: "requirement_technical_proposals");

            migrationBuilder.DropColumn(
                name: "commercial_confirmed_by_user_id",
                schema: "core",
                table: "requirement_technical_proposals");
        }
    }
}
