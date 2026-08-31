using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_chat_threads",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_chat_threads", x => x.id);
                    table.CheckConstraint("ck_requirement_chat_threads_locator", "(\"scope\" = 'Requirement' AND \"technical_proposal_item_id\" IS NULL) OR (\"scope\" = 'Item' AND \"technical_proposal_item_id\" IS NOT NULL)");
                    table.CheckConstraint("ck_requirement_chat_threads_scope", "\"scope\" IN ('Requirement', 'Item')");
                    table.ForeignKey(
                        name: "FK_requirement_chat_threads_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_chat_threads_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_chat_messages",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_chat_messages", x => x.id);
                    table.CheckConstraint("ck_requirement_chat_messages_content", "length(btrim(\"content\")) > 0");
                    table.CheckConstraint("ck_requirement_chat_messages_role", "\"role\" IN ('User', 'Assistant')");
                    table.CheckConstraint("ck_requirement_chat_messages_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_requirement_chat_messages_requirement_chat_threads_chat_thr~",
                        column: x => x.chat_thread_id,
                        principalSchema: "core",
                        principalTable: "requirement_chat_threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requirement_chat_messages_thread_created_id",
                schema: "core",
                table: "requirement_chat_messages",
                columns: new[] { "chat_thread_id", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_requirement_chat_messages_thread_id",
                schema: "core",
                table: "requirement_chat_messages",
                column: "chat_thread_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_chat_messages_thread_sequence",
                schema: "core",
                table: "requirement_chat_messages",
                columns: new[] { "chat_thread_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirement_chat_threads_created_by_user_id",
                schema: "core",
                table: "requirement_chat_threads",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_chat_threads_requirement_id",
                schema: "core",
                table: "requirement_chat_threads",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_chat_threads_technical_proposal_item_id",
                schema: "core",
                table: "requirement_chat_threads",
                column: "technical_proposal_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_chat_threads_requirement_item_scope",
                schema: "core",
                table: "requirement_chat_threads",
                columns: new[] { "requirement_id", "technical_proposal_item_id", "scope" },
                unique: true,
                filter: "\"scope\" = 'Item'");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_chat_threads_requirement_scope",
                schema: "core",
                table: "requirement_chat_threads",
                columns: new[] { "requirement_id", "scope" },
                unique: true,
                filter: "\"scope\" = 'Requirement'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_chat_messages",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_chat_threads",
                schema: "core");
        }
    }
}
