using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddXlsxEvidenceSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_structured_extraction_item_glass_evidence_glass_detection_~1",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_item_glass_evidence_page_positive",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_item_glass_evidence_source_type",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropIndex(
                name: "IX_pre_quote_draft_item_glass_evidence_glass_snapshot_id_page_~",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_page_number",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.AlterColumn<int>(
                name: "page_number",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "cell_range",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sheet_name",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "page_number",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "cell_range",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sheet_name",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_structured_extraction_item_glass_evidence_pdf",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                columns: new[] { "glass_detection_id", "page_number", "source_type", "text" },
                unique: true,
                filter: "((source_type = 'Native') OR (source_type = 'Ocr'))");

            migrationBuilder.CreateIndex(
                name: "ix_structured_extraction_item_glass_evidence_xlsx",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                columns: new[] { "glass_detection_id", "sheet_name", "cell_range", "source_type", "text" },
                unique: true,
                filter: "(source_type = 'Xlsx')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_item_glass_evidence_cell_range",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                sql: "\"cell_range\" IS NOT NULL AND btrim(\"cell_range\") <> '' OR \"cell_range\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_item_glass_evidence_pdf",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                sql: "(\"source_type\" IN ('Native', 'Ocr') AND \"page_number\" IS NOT NULL AND \"page_number\" > 0 AND \"sheet_name\" IS NULL AND \"cell_range\" IS NULL) OR (\"source_type\" = 'Xlsx' AND \"page_number\" IS NULL AND \"sheet_name\" IS NOT NULL AND btrim(\"sheet_name\") <> '' AND \"cell_range\" IS NOT NULL AND btrim(\"cell_range\") <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_item_glass_evidence_sheet_name",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                sql: "\"sheet_name\" IS NOT NULL AND btrim(\"sheet_name\") <> '' OR \"sheet_name\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_item_glass_evidence_source_type",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                sql: "\"source_type\" IN ('Native', 'Ocr', 'Xlsx')");

            migrationBuilder.CreateIndex(
                name: "ix_pre_quote_draft_item_glass_evidence_pdf",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                columns: new[] { "glass_snapshot_id", "page_number", "source_type", "text" },
                unique: true,
                filter: "((source_type = 'Native') OR (source_type = 'Ocr'))");

            migrationBuilder.CreateIndex(
                name: "ix_pre_quote_draft_item_glass_evidence_xlsx",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                columns: new[] { "glass_snapshot_id", "sheet_name", "cell_range", "source_type", "text" },
                unique: true,
                filter: "(source_type = 'Xlsx')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_cell_range",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                sql: "\"cell_range\" IS NOT NULL AND btrim(\"cell_range\") <> '' OR \"cell_range\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_pdf_or_xlsx",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                sql: "(\"source_type\" IN ('Native', 'Ocr') AND \"page_number\" IS NOT NULL AND \"page_number\" > 0 AND \"sheet_name\" IS NULL AND \"cell_range\" IS NULL) OR (\"source_type\" = 'Xlsx' AND \"page_number\" IS NULL AND \"sheet_name\" IS NOT NULL AND btrim(\"sheet_name\") <> '' AND \"cell_range\" IS NOT NULL AND btrim(\"cell_range\") <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_sheet_name",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                sql: "\"sheet_name\" IS NOT NULL AND btrim(\"sheet_name\") <> '' OR \"sheet_name\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_source_type",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                sql: "\"source_type\" IN ('Native', 'Ocr', 'Xlsx')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_structured_extraction_item_glass_evidence_pdf",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropIndex(
                name: "ix_structured_extraction_item_glass_evidence_xlsx",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_item_glass_evidence_cell_range",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_item_glass_evidence_pdf",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_item_glass_evidence_sheet_name",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_item_glass_evidence_source_type",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropIndex(
                name: "ix_pre_quote_draft_item_glass_evidence_pdf",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropIndex(
                name: "ix_pre_quote_draft_item_glass_evidence_xlsx",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_cell_range",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_pdf_or_xlsx",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_sheet_name",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_source_type",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropColumn(
                name: "cell_range",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropColumn(
                name: "sheet_name",
                schema: "core",
                table: "structured_extraction_item_glass_evidence");

            migrationBuilder.DropColumn(
                name: "cell_range",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.DropColumn(
                name: "sheet_name",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence");

            migrationBuilder.AlterColumn<int>(
                name: "page_number",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "page_number",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_evidence_glass_detection_~1",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                columns: new[] { "glass_detection_id", "page_number", "source_type", "text" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_item_glass_evidence_page_positive",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                sql: "\"page_number\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_item_glass_evidence_source_type",
                schema: "core",
                table: "structured_extraction_item_glass_evidence",
                sql: "\"source_type\" IN ('Native', 'Ocr')");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_evidence_glass_snapshot_id_page_~",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                columns: new[] { "glass_snapshot_id", "page_number", "source_type", "text" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_quote_draft_item_glass_evidence_page_number",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                sql: "\"page_number\" > 0");
        }
    }
}
