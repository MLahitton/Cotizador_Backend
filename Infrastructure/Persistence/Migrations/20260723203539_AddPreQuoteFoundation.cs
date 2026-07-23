using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreQuoteFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pre_quotes",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quotes", x => x.id);
                    table.ForeignKey(
                        name: "FK_pre_quotes_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "core",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quotes_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_documents",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_documents", x => x.id);
                    table.CheckConstraint("ck_pre_quote_documents_size_bytes_positive", "\"size_bytes\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_documents_pre_quotes_pre_quote_id",
                        column: x => x.pre_quote_id,
                        principalSchema: "core",
                        principalTable: "pre_quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_documents_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pre_quote_documents_created_by_user_id",
                schema: "core",
                table: "pre_quote_documents",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_quote_documents_pre_quote_id",
                schema: "core",
                table: "pre_quote_documents",
                column: "pre_quote_id");

            migrationBuilder.CreateIndex(
                name: "ux_pre_quote_documents_storage_key",
                schema: "core",
                table: "pre_quote_documents",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pre_quotes_created_by_user_id",
                schema: "core",
                table: "pre_quotes",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_quotes_project_id",
                schema: "core",
                table: "pre_quotes",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_quotes_updated_at_utc",
                schema: "core",
                table: "pre_quotes",
                column: "updated_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_quote_documents",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quotes",
                schema: "core");
        }
    }
}
