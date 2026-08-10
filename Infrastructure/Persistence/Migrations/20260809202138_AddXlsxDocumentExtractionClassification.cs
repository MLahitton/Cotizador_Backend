using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddXlsxDocumentExtractionClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_extraction_results_classification_ocr",
                schema: "core",
                table: "document_extraction_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_extraction_results_page_count_positive",
                schema: "core",
                table: "document_extraction_results");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_extraction_results_classification_ocr",
                schema: "core",
                table: "document_extraction_results",
                sql: "((\"classification\" = 'PdfText' AND \"requires_ocr\" = false) OR (\"classification\" = 'PdfScanned' AND \"requires_ocr\" = true) OR (\"classification\" = 'PdfMixed' AND \"requires_ocr\" = true) OR (\"classification\" = 'Xlsx' AND \"requires_ocr\" = false))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_extraction_results_page_count_by_classification",
                schema: "core",
                table: "document_extraction_results",
                sql: "(\"classification\" = 'PdfText' AND \"page_count\" >= 1) OR (\"classification\" = 'PdfScanned' AND \"page_count\" >= 1) OR (\"classification\" = 'PdfMixed' AND \"page_count\" >= 1) OR (\"classification\" = 'Xlsx' AND \"page_count\" = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_extraction_results_classification_ocr",
                schema: "core",
                table: "document_extraction_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_extraction_results_page_count_by_classification",
                schema: "core",
                table: "document_extraction_results");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_extraction_results_classification_ocr",
                schema: "core",
                table: "document_extraction_results",
                sql: "((\"classification\" = 'PdfText' AND \"requires_ocr\" = false) OR (\"classification\" = 'PdfScanned' AND \"requires_ocr\" = true) OR (\"classification\" = 'PdfMixed' AND \"requires_ocr\" = true))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_extraction_results_page_count_positive",
                schema: "core",
                table: "document_extraction_results",
                sql: "\"page_count\" >= 1");
        }
    }
}
