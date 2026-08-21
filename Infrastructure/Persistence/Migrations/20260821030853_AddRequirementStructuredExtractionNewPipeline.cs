using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementStructuredExtractionNewPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_extracted_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_extraction_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai2_element_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    element_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    width_millimeters = table.Column<int>(type: "integer", nullable: true),
                    height_millimeters = table.Column<int>(type: "integer", nullable: true),
                    area_square_meters = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    extraction_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    review_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    functional_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    panel_count = table.Column<int>(type: "integer", nullable: true),
                    movable_panel_count = table.Column<int>(type: "integer", nullable: true),
                    fixed_panel_count = table.Column<int>(type: "integer", nullable: true),
                    arrangement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    modulation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    opening_direction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    special_features = table.Column<string[]>(type: "text[]", nullable: false),
                    geometry_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requested_system_raw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requested_profile_raw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    glass_raw_specification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    glass_type_raw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_type_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_thickness_mm = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                    glass_color_raw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_color_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_treatment_raw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_treatment_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_composition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_coating = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_transparency = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    glass_requires_review = table.Column<bool>(type: "boolean", nullable: true),
                    finish_raw_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    finish_normalized_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    finish_color_raw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    finish_color_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    finish_texture_raw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    finish_texture_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    finish_explicit_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    finish_requires_review = table.Column<bool>(type: "boolean", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_extracted_items", x => x.id);
                    table.CheckConstraint("ck_requirement_extracted_items_positive_values", "\"sequence\" > 0 AND (\"quantity\" IS NULL OR \"quantity\" > 0) AND (\"width_millimeters\" IS NULL OR \"width_millimeters\" > 0) AND (\"height_millimeters\" IS NULL OR \"height_millimeters\" > 0) AND (\"area_square_meters\" IS NULL OR \"area_square_meters\" > 0) AND (\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)) AND (\"glass_thickness_mm\" IS NULL OR \"glass_thickness_mm\" > 0)");
                    table.ForeignKey(
                        name: "FK_requirement_extracted_items_requirement_extraction_results_~",
                        column: x => x.requirement_extraction_result_id,
                        principalSchema: "core",
                        principalTable: "requirement_extraction_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_extracted_item_evidence",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_extracted_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sheet_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cell_range = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_extracted_item_evidence", x => x.id);
                    table.CheckConstraint("ck_requirement_extracted_item_evidence_location", "((\"source_type\" IN ('Native','Ocr') AND \"page_number\" IS NOT NULL AND \"page_number\" > 0 AND \"sheet_name\" IS NULL AND \"cell_range\" IS NULL) OR (\"source_type\" = 'Xlsx' AND \"page_number\" IS NULL AND \"sheet_name\" IS NOT NULL AND \"cell_range\" IS NOT NULL AND btrim(\"sheet_name\") <> '' AND btrim(\"cell_range\") <> '')) AND (\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1))");
                    table.ForeignKey(
                        name: "FK_requirement_extracted_item_evidence_requirement_extracted_i~",
                        column: x => x.requirement_extracted_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_extracted_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requirement_extracted_item_evidence_item_id",
                schema: "core",
                table: "requirement_extracted_item_evidence",
                column: "requirement_extracted_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_extracted_items_extraction_sequence",
                schema: "core",
                table: "requirement_extracted_items",
                columns: new[] { "requirement_extraction_result_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_extracted_item_evidence",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_extracted_items",
                schema: "core");
        }
    }
}
