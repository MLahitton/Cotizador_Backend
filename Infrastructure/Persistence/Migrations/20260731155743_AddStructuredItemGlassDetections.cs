using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredItemGlassDetections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "glass_items_requiring_review",
                schema: "core",
                table: "structured_document_extractions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "identified_glass_item_count",
                schema: "core",
                table: "structured_document_extractions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "structured_extraction_item_glass_detections",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_extraction_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    raw_specification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    normalized_code_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    assignment_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_item_glass_detections", x => x.id);
                    table.CheckConstraint("ck_structured_item_glass_detection_identity", "(\"normalized_code_snapshot\" IS NULL AND \"glass_type_id\" IS NULL) OR (\"normalized_code_snapshot\" IS NOT NULL AND \"glass_type_id\" IS NOT NULL)");
                    table.CheckConstraint("ck_structured_item_glass_detection_scope", "\"assignment_scope\" IN ('Item', 'Section', 'General', 'Unassigned')");
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_detections_glass_types_gla~",
                        column: x => x.glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_detections_structured_extr~",
                        column: x => x.structured_extraction_item_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_item_glass_evidence",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_detection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_item_glass_evidence", x => x.id);
                    table.CheckConstraint("ck_structured_item_glass_evidence_page_positive", "\"page_number\" > 0");
                    table.CheckConstraint("ck_structured_item_glass_evidence_sequence", "\"sequence\" > 0");
                    table.CheckConstraint("ck_structured_item_glass_evidence_source_type", "\"source_type\" IN ('Native', 'Ocr')");
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_evidence_structured_extrac~",
                        column: x => x.glass_detection_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_item_glass_detections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_item_glass_review_reasons",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_detection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_item_glass_review_reasons", x => x.id);
                    table.CheckConstraint("ck_structured_item_glass_review_reason_code", "\"code\" IN ('GlassTypeNotIdentified', 'GlassTypeAmbiguous', 'GlassTypeConflict')");
                    table.CheckConstraint("ck_structured_item_glass_review_reason_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_review_reasons_structured_~",
                        column: x => x.glass_detection_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_item_glass_detections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_item_glass_source_pages",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_detection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_item_glass_source_pages", x => x.id);
                    table.CheckConstraint("ck_structured_item_glass_source_page_positive", "\"page_number\" > 0");
                    table.CheckConstraint("ck_structured_item_glass_source_page_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_source_pages_structured_ex~",
                        column: x => x.glass_detection_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_item_glass_detections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_extractions_glass_counts",
                schema: "core",
                table: "structured_document_extractions",
                sql: "(\"identified_glass_item_count\" IS NULL AND \"glass_items_requiring_review\" IS NULL) OR (\"identified_glass_item_count\" >= 0 AND \"glass_items_requiring_review\" >= 0)");

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_detections_glass_type_id",
                schema: "core",
                table: "structured_extraction_item_glass_detections",
                column: "glass_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_detections_structured_extr~",
                schema: "core",
                table: "structured_extraction_item_glass_detections",
                column: "structured_extraction_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_evidence_glass_detection_~1",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                columns: new[] { "glass_detection_id", "page_number", "source_type", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_evidence_glass_detection_i~",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                columns: new[] { "glass_detection_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_review_reasons_glass_dete~1",
                schema: "core",
                table: "structured_extraction_item_glass_review_reasons",
                columns: new[] { "glass_detection_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_review_reasons_glass_detec~",
                schema: "core",
                table: "structured_extraction_item_glass_review_reasons",
                columns: new[] { "glass_detection_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_source_pages_glass_detect~1",
                schema: "core",
                table: "structured_extraction_item_glass_source_pages",
                columns: new[] { "glass_detection_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_source_pages_glass_detecti~",
                schema: "core",
                table: "structured_extraction_item_glass_source_pages",
                columns: new[] { "glass_detection_id", "page_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "structured_extraction_item_glass_evidence",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_item_glass_review_reasons",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_item_glass_source_pages",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_item_glass_detections",
                schema: "core");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_extractions_glass_counts",
                schema: "core",
                table: "structured_document_extractions");

            migrationBuilder.DropColumn(
                name: "glass_items_requiring_review",
                schema: "core",
                table: "structured_document_extractions");

            migrationBuilder.DropColumn(
                name: "identified_glass_item_count",
                schema: "core",
                table: "structured_document_extractions");
        }
    }
}
