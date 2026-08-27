using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementExtractedItemSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assembly_type",
                schema: "core",
                table: "requirement_extracted_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "requirement_extracted_item_segments",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_extracted_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    width_millimeters = table.Column<int>(type: "integer", nullable: true),
                    height_millimeters = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    geometry_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    evidence_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    sheet_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cell_range = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    extraction_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_extracted_item_segments", x => x.id);
                    table.CheckConstraint("ck_requirement_extracted_item_segments_positive_values", "\"sequence\" > 0 AND (\"quantity\" IS NULL OR \"quantity\" > 0) AND (\"width_millimeters\" IS NULL OR \"width_millimeters\" > 0) AND (\"height_millimeters\" IS NULL OR \"height_millimeters\" > 0) AND (\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1))");
                    table.ForeignKey(
                        name: "FK_requirement_extracted_item_segments_requirement_extracted_i~",
                        column: x => x.requirement_extracted_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_extracted_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_requirement_extracted_item_segments_item_sequence",
                schema: "core",
                table: "requirement_extracted_item_segments",
                columns: new[] { "requirement_extracted_item_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_extracted_item_segments",
                schema: "core");

            migrationBuilder.DropColumn(
                name: "assembly_type",
                schema: "core",
                table: "requirement_extracted_items");
        }
    }
}
