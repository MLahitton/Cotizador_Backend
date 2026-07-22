using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientCreatedByUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                schema: "core",
                table: "clients",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_clients_created_by_user_id",
                schema: "core",
                table: "clients",
                column: "created_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_clients_users_created_by_user_id",
                schema: "core",
                table: "clients",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clients_users_created_by_user_id",
                schema: "core",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "ix_clients_created_by_user_id",
                schema: "core",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                schema: "core",
                table: "clients");
        }
    }
}
