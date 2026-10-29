using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicExperienceIdeaPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "public_experience_idea_photos",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stored_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stored_length_bytes = table.Column<long>(type: "bigint", nullable: false),
                    stored_evidence_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stored_evidence_version = table.Column<short>(type: "smallint", nullable: false),
                    identity_version = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_experience_idea_photos", x => x.id);
                    table.CheckConstraint("ck_idea_photos_evidence", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND submission_id <> '00000000-0000-0000-0000-000000000000'::uuid AND identity_version = 1 AND stored_evidence_version = 1 AND stored_sha256 ~ '^[0-9A-F]{64}$' AND length(stored_sha256) = 64 AND stored_length_bytes > 0 AND stored_length_bytes <= 20971520 AND content_type IN ('image/jpeg', 'image/png')");
                    table.ForeignKey(
                        name: "FK_public_experience_idea_photos_public_experience_submissions~",
                        column: x => x.submission_id,
                        principalSchema: "core",
                        principalTable: "public_experience_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_idea_photos_storage_key",
                schema: "core",
                table: "public_experience_idea_photos",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_experience_idea_photos_submission_id",
                schema: "core",
                table: "public_experience_idea_photos",
                column: "submission_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "public_experience_idea_photos",
                schema: "core");
        }
    }
}
