using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentProcessingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_processing_attempts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    error_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_processing_attempts", x => x.id);
                    table.CheckConstraint("ck_document_processing_attempts_final_state", "((\"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"outcome\" IS NOT NULL AND \"outcome\" IN ('Completed', 'RequiresReview') AND \"completed_at_utc\" IS NOT NULL AND \"error_code\" IS NULL) OR (\"outcome\" IS NOT NULL AND \"outcome\" = 'Failed' AND \"completed_at_utc\" IS NOT NULL AND \"error_code\" IS NOT NULL AND \"error_code\" <> ''))");
                    table.ForeignKey(
                        name: "FK_document_processing_attempts_pre_quote_documents_pre_quote_~",
                        column: x => x.pre_quote_document_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_processing_attempts_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_extraction_results",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_processing_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    classification = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    requires_ocr = table.Column<bool>(type: "boolean", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    processing_method = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_extraction_results", x => x.id);
                    table.CheckConstraint("ck_document_extraction_results_classification_ocr", "((\"classification\" = 'PdfText' AND \"requires_ocr\" = false) OR (\"classification\" = 'PdfScanned' AND \"requires_ocr\" = true) OR (\"classification\" = 'PdfMixed' AND \"requires_ocr\" = true))");
                    table.CheckConstraint("ck_document_extraction_results_duration_ms_non_negative", "\"duration_ms\" >= 0");
                    table.CheckConstraint("ck_document_extraction_results_page_count_positive", "\"page_count\" >= 1");
                    table.ForeignKey(
                        name: "FK_document_extraction_results_document_processing_attempts_do~",
                        column: x => x.document_processing_attempt_id,
                        principalSchema: "core",
                        principalTable: "document_processing_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_document_extraction_results_processing_attempt_id",
                schema: "core",
                table: "document_extraction_results",
                column: "document_processing_attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_processing_attempts_created_at_utc",
                schema: "core",
                table: "document_processing_attempts",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_document_processing_attempts_pre_quote_document_id",
                schema: "core",
                table: "document_processing_attempts",
                column: "pre_quote_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_processing_attempts_requested_by_user_id",
                schema: "core",
                table: "document_processing_attempts",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_processing_attempts_correlation_id",
                schema: "core",
                table: "document_processing_attempts",
                column: "correlation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_extraction_results",
                schema: "core");

            migrationBuilder.DropTable(
                name: "document_processing_attempts",
                schema: "core");
        }
    }
}
