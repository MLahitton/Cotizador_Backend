using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementItemOccurrenceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "occurrence_context",
                schema: "core",
                table: "structured_extraction_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "occurrence_context",
                schema: "core",
                table: "requirement_extracted_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "occurrence_context",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "occurrence_context",
                schema: "core",
                table: "requirement_extracted_items");
        }
    }
}
