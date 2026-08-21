using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementNewPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirements",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirements", x => x.id);
                    table.CheckConstraint("ck_requirements_status", "\"status\" IN ('Pending', 'Processing', 'Processed', 'Failed')");
                    table.ForeignKey(
                        name: "FK_requirements_pre_quotes_pre_quote_id",
                        column: x => x.pre_quote_id,
                        principalSchema: "core",
                        principalTable: "pre_quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirements_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_files",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_files", x => x.id);
                    table.CheckConstraint("ck_requirement_files_size_bytes_positive", "\"size_bytes\" > 0");
                    table.ForeignKey(
                        name: "FK_requirement_files_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_processing_attempts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    error_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_processing_attempts", x => x.id);
                    table.CheckConstraint("ck_requirement_processing_attempts_lifecycle", "((\"processing_state\" = 'Pending' AND \"started_at_utc\" IS NULL AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Processing' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Finished' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"completed_at_utc\" IS NOT NULL AND \"completed_at_utc\" >= \"started_at_utc\" AND ((\"outcome\" IN ('Completed', 'RequiresReview') AND \"error_code\" IS NULL) OR (\"outcome\" = 'Failed' AND \"error_code\" IS NOT NULL AND \"error_code\" <> ''))))");
                    table.ForeignKey(
                        name: "FK_requirement_processing_attempts_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_processing_attempts_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requirement_files_requirement_id",
                schema: "core",
                table: "requirement_files",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_files_storage_key",
                schema: "core",
                table: "requirement_files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirement_processing_attempts_requested_by_user_id",
                schema: "core",
                table: "requirement_processing_attempts",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_processing_attempts_requirement_id",
                schema: "core",
                table: "requirement_processing_attempts",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_processing_attempts_state_created_id",
                schema: "core",
                table: "requirement_processing_attempts",
                columns: new[] { "processing_state", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_requirement_processing_attempts_correlation_id",
                schema: "core",
                table: "requirement_processing_attempts",
                column: "correlation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirements_created_by_user_id",
                schema: "core",
                table: "requirements",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirements_pre_quote_id",
                schema: "core",
                table: "requirements",
                column: "pre_quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirements_pre_quote_id_is_active",
                schema: "core",
                table: "requirements",
                columns: new[] { "pre_quote_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_files",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_processing_attempts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirements",
                schema: "core");
        }
    }
}
