using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicExperienceIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "public_experience_submissions",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    schema_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    client_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    client_whatsapp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    advisor_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    advisor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    project_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    project_city = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    project_subdivision = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    project_lot_or_apartment = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    project_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    payload_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_experience_submissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "public_experience_submission_files",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_experience_submission_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_public_experience_submission_files_public_experience_submis~",
                        column: x => x.submission_id,
                        principalSchema: "core",
                        principalTable: "public_experience_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_submission_files_storage_key",
                schema: "core",
                table: "public_experience_submission_files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_submission_files_submission_id_sequence",
                schema: "core",
                table: "public_experience_submission_files",
                columns: new[] { "submission_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_submissions_status_created_at_utc",
                schema: "core",
                table: "public_experience_submissions",
                columns: new[] { "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_public_experience_submissions_submission_id",
                schema: "core",
                table: "public_experience_submissions",
                column: "submission_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "public_experience_submission_files",
                schema: "core");

            migrationBuilder.DropTable(
                name: "public_experience_submissions",
                schema: "core");
        }
    }
}
