using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementCommercialLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commercial_line",
                schema: "core",
                table: "requirements",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirements_commercial_line",
                schema: "core",
                table: "requirements",
                sql: "\"commercial_line\" IS NULL OR \"commercial_line\" IN ('CLASSIC', 'ESSENTIAL', 'BIOCONFORT', 'SIGNATURE')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requirements_commercial_line",
                schema: "core",
                table: "requirements");

            migrationBuilder.DropColumn(
                name: "commercial_line",
                schema: "core",
                table: "requirements");
        }
    }
}
