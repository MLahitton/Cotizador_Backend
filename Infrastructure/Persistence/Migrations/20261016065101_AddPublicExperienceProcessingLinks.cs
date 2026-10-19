using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicExperienceProcessingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                schema: "core",
                table: "public_experience_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processed_at_utc",
                schema: "core",
                table: "public_experience_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processing_error_code",
                schema: "core",
                table: "public_experience_submissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                schema: "core",
                table: "public_experience_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_submissions_client_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_submissions_project_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "FK_public_experience_submissions_clients_client_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "client_id",
                principalSchema: "core",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_public_experience_submissions_projects_project_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "project_id",
                principalSchema: "core",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_public_experience_submissions_clients_client_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_public_experience_submissions_projects_project_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropIndex(
                name: "IX_public_experience_submissions_client_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropIndex(
                name: "IX_public_experience_submissions_project_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "client_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "processed_at_utc",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "processing_error_code",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "project_id",
                schema: "core",
                table: "public_experience_submissions");
        }
    }
}
