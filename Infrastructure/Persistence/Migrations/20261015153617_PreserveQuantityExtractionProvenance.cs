using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveQuantityExtractionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "quantity_extraction_confidence",
                schema: "core",
                table: "requirement_extracted_items",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quantity_extraction_status",
                schema: "core",
                table: "requirement_extracted_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quantity_extraction_confidence",
                schema: "core",
                table: "requirement_extracted_items");

            migrationBuilder.DropColumn(
                name: "quantity_extraction_status",
                schema: "core",
                table: "requirement_extracted_items");
        }
    }
}
