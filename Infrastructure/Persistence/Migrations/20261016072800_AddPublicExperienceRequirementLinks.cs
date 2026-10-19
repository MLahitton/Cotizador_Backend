using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicExperienceRequirementLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pre_quote_id",
                schema: "core",
                table: "public_experience_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "requirement_id",
                schema: "core",
                table: "public_experience_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requirement_preparation_error_code",
                schema: "core",
                table: "public_experience_submissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "requirement_prepared_at_utc",
                schema: "core",
                table: "public_experience_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_submissions_pre_quote_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "pre_quote_id");

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_submissions_requirement_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "requirement_id");

            migrationBuilder.AddForeignKey(
                name: "FK_public_experience_submissions_pre_quotes_pre_quote_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "pre_quote_id",
                principalSchema: "core",
                principalTable: "pre_quotes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_public_experience_submissions_requirements_requirement_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "requirement_id",
                principalSchema: "core",
                principalTable: "requirements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_public_experience_submissions_pre_quotes_pre_quote_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_public_experience_submissions_requirements_requirement_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropIndex(
                name: "IX_public_experience_submissions_pre_quote_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropIndex(
                name: "IX_public_experience_submissions_requirement_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "pre_quote_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "requirement_id",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "requirement_preparation_error_code",
                schema: "core",
                table: "public_experience_submissions");

            migrationBuilder.DropColumn(
                name: "requirement_prepared_at_utc",
                schema: "core",
                table: "public_experience_submissions");
        }
    }
}
