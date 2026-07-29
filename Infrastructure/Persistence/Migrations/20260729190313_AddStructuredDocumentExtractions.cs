using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredDocumentExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "structured_document_extractions",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_extraction_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", nullable: false),
                    project_name = table.Column<string>(type: "text", nullable: true),
                    client_name = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    document_reference_count = table.Column<int>(type: "integer", nullable: false),
                    items_requiring_review = table.Column<int>(type: "integer", nullable: false),
                    known_quoteable_unit_count = table.Column<int>(type: "integer", nullable: false),
                    processing_method = table.Column<string>(type: "varchar(100)", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_document_extractions", x => x.id);
                    table.CheckConstraint("ck_structured_extractions_counts", "\"item_count\" >= 0 AND \"document_reference_count\" >= 0 AND \"items_requiring_review\" >= 0 AND \"known_quoteable_unit_count\" >= 0");
                    table.CheckConstraint("ck_structured_extractions_duration", "\"duration_ms\" >= 0");
                    table.ForeignKey(
                        name: "FK_structured_document_extractions_document_extraction_results~",
                        column: x => x.document_extraction_result_id,
                        principalSchema: "core",
                        principalTable: "document_extraction_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_conflicts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_document_extraction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "varchar(80)", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    item_sequences = table.Column<int[]>(type: "integer[]", nullable: false),
                    page_numbers = table.Column<int[]>(type: "integer[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "FK_structured_extraction_conflicts_structured_document_extract~",
                        column: x => x.structured_document_extraction_id,
                        principalSchema: "core",
                        principalTable: "structured_document_extractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_document_references",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_document_extraction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_document_references", x => x.id);
                    table.CheckConstraint("ck_structured_extraction_document_references_quantity_positive", "\"quantity\" IS NULL OR \"quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_structured_extraction_document_references_structured_docume~",
                        column: x => x.structured_document_extraction_id,
                        principalSchema: "core",
                        principalTable: "structured_document_extractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_issues",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_document_extraction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "varchar(80)", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    item_sequence = table.Column<int>(type: "integer", nullable: true),
                    page_numbers = table.Column<int[]>(type: "integer[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_structured_extraction_issues_structured_document_extraction~",
                        column: x => x.structured_document_extraction_id,
                        principalSchema: "core",
                        principalTable: "structured_document_extractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_document_extraction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    element_type = table.Column<string>(type: "varchar(30)", nullable: false),
                    raw_measurements = table.Column<string>(type: "text", nullable: true),
                    width_millimeters = table.Column<int>(type: "integer", nullable: true),
                    height_millimeters = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_items", x => x.id);
                    table.CheckConstraint("ck_structured_items_values", "(\"width_millimeters\" IS NULL AND \"height_millimeters\" IS NULL OR \"width_millimeters\" > 0 AND \"height_millimeters\" > 0) AND (\"quantity\" IS NULL OR \"quantity\" > 0) AND \"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_structured_extraction_items_structured_document_extractions~",
                        column: x => x.structured_document_extraction_id,
                        principalSchema: "core",
                        principalTable: "structured_document_extractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_requirements",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_document_extraction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "varchar(50)", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_requirements", x => x.id);
                    table.ForeignKey(
                        name: "FK_structured_extraction_requirements_structured_document_extr~",
                        column: x => x.structured_document_extraction_id,
                        principalSchema: "core",
                        principalTable: "structured_document_extractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_structured_document_extractions_document_extraction_result_~",
                schema: "core",
                table: "structured_document_extractions",
                column: "document_extraction_result_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_conflicts_structured_document_extract~",
                schema: "core",
                table: "structured_extraction_conflicts",
                columns: new[] { "structured_document_extraction_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_document_references_structured_docume~",
                schema: "core",
                table: "structured_extraction_document_references",
                columns: new[] { "structured_document_extraction_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_issues_structured_document_extraction~",
                schema: "core",
                table: "structured_extraction_issues",
                columns: new[] { "structured_document_extraction_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_items_structured_document_extraction_~",
                schema: "core",
                table: "structured_extraction_items",
                columns: new[] { "structured_document_extraction_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_requirements_structured_document_extr~",
                schema: "core",
                table: "structured_extraction_requirements",
                columns: new[] { "structured_document_extraction_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "structured_extraction_conflicts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_document_references",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_issues",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_items",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_requirements",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_document_extractions",
                schema: "core");
        }
    }
}
