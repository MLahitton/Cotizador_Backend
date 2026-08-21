using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementExtractionResultNewPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requirement_processing_attempts_requirement_id",
                schema: "core",
                table: "requirement_processing_attempts");

            migrationBuilder.CreateTable(
                name: "requirement_extraction_results",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_processing_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    items_requiring_review = table.Column<int>(type: "integer", nullable: false),
                    issue_count = table.Column<int>(type: "integer", nullable: false),
                    conflict_count = table.Column<int>(type: "integer", nullable: false),
                    processing_method = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_extraction_results", x => x.id);
                    table.CheckConstraint("ck_requirement_extraction_results_counts", "\"item_count\" >= 0 AND \"items_requiring_review\" >= 0 AND \"items_requiring_review\" <= \"item_count\" AND \"issue_count\" >= 0 AND \"conflict_count\" >= 0");
                    table.CheckConstraint("ck_requirement_extraction_results_duration", "\"duration_ms\" >= 0");
                    table.ForeignKey(
                        name: "FK_requirement_extraction_results_requirement_processing_attem~",
                        column: x => x.requirement_processing_attempt_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_requirement_processing_attempts_active_requirement_id",
                schema: "core",
                table: "requirement_processing_attempts",
                column: "requirement_id",
                unique: true,
                filter: "\"processing_state\" IN ('Pending', 'Processing')");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_extraction_results_processing_attempt_id",
                schema: "core",
                table: "requirement_extraction_results",
                column: "requirement_processing_attempt_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_extraction_results",
                schema: "core");

            migrationBuilder.DropIndex(
                name: "ux_requirement_processing_attempts_active_requirement_id",
                schema: "core",
                table: "requirement_processing_attempts");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_processing_attempts_requirement_id",
                schema: "core",
                table: "requirement_processing_attempts",
                column: "requirement_id");
        }
    }
}
