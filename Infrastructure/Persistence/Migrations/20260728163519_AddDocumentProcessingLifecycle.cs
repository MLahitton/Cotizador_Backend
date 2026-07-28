using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentProcessingLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_document_processing_attempts_pre_quote_document_id",
                schema: "core",
                table: "document_processing_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_processing_attempts_final_state",
                schema: "core",
                table: "document_processing_attempts");

            migrationBuilder.AddColumn<string>(
                name: "processing_state",
                schema: "core",
                table: "document_processing_attempts",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at_utc",
                schema: "core",
                table: "document_processing_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE core.document_processing_attempts
                SET processing_state = CASE
                        WHEN outcome IS NULL THEN 'Pending'
                        ELSE 'Finished'
                    END,
                    started_at_utc = CASE
                        WHEN outcome IS NULL THEN NULL
                        ELSE created_at_utc
                    END;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT pre_quote_document_id
                        FROM core.document_processing_attempts
                        WHERE processing_state IN ('Pending', 'Processing')
                        GROUP BY pre_quote_document_id
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION
                            'No se puede crear el índice de intento activo: existen documentos con múltiples intentos abiertos.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "processing_state",
                schema: "core",
                table: "document_processing_attempts",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_processing_attempts_processing_state_created_at_utc",
                schema: "core",
                table: "document_processing_attempts",
                columns: new[] { "processing_state", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_document_processing_attempts_active_pre_quote_document_id",
                schema: "core",
                table: "document_processing_attempts",
                column: "pre_quote_document_id",
                unique: true,
                filter: "\"processing_state\" IN ('Pending', 'Processing')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_processing_attempts_lifecycle",
                schema: "core",
                table: "document_processing_attempts",
                sql: "((\"processing_state\" = 'Pending' AND \"started_at_utc\" IS NULL AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Processing' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Finished' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"completed_at_utc\" IS NOT NULL AND \"completed_at_utc\" >= \"started_at_utc\" AND ((\"outcome\" IN ('Completed', 'RequiresReview') AND \"error_code\" IS NULL) OR (\"outcome\" = 'Failed' AND \"error_code\" IS NOT NULL AND \"error_code\" <> ''))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_document_processing_attempts_processing_state_created_at_utc",
                schema: "core",
                table: "document_processing_attempts");

            migrationBuilder.DropIndex(
                name: "ux_document_processing_attempts_active_pre_quote_document_id",
                schema: "core",
                table: "document_processing_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_processing_attempts_lifecycle",
                schema: "core",
                table: "document_processing_attempts");

            migrationBuilder.DropColumn(
                name: "processing_state",
                schema: "core",
                table: "document_processing_attempts");

            migrationBuilder.DropColumn(
                name: "started_at_utc",
                schema: "core",
                table: "document_processing_attempts");

            migrationBuilder.CreateIndex(
                name: "ix_document_processing_attempts_pre_quote_document_id",
                schema: "core",
                table: "document_processing_attempts",
                column: "pre_quote_document_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_processing_attempts_final_state",
                schema: "core",
                table: "document_processing_attempts",
                sql: "((\"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"outcome\" IS NOT NULL AND \"outcome\" IN ('Completed', 'RequiresReview') AND \"completed_at_utc\" IS NOT NULL AND \"error_code\" IS NULL) OR (\"outcome\" IS NOT NULL AND \"outcome\" = 'Failed' AND \"completed_at_utc\" IS NOT NULL AND \"error_code\" IS NOT NULL AND \"error_code\" <> ''))");
        }
    }
}
