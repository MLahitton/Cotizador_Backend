using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreQuoteDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pre_quote_drafts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_structured_extraction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    project_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    client_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_drafts", x => x.id);
                    table.CheckConstraint("ck_pre_quote_drafts_approval", "(\"status\" = 'Approved' AND \"approved_by_user_id\" IS NOT NULL AND \"approved_at_utc\" IS NOT NULL) OR (\"status\" <> 'Approved' AND \"approved_by_user_id\" IS NULL AND \"approved_at_utc\" IS NULL)");
                    table.CheckConstraint("ck_pre_quote_drafts_version", "\"version\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_drafts_pre_quote_documents_source_document_id",
                        column: x => x.source_document_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_drafts_pre_quotes_pre_quote_id",
                        column: x => x.pre_quote_id,
                        principalSchema: "core",
                        principalTable: "pre_quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_drafts_structured_document_extractions_source_str~",
                        column: x => x.source_structured_extraction_id,
                        principalSchema: "core",
                        principalTable: "structured_document_extractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_drafts_users_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_drafts_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_drafts_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_conflicts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_structured_conflict_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_conflict_sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    item_sequences = table.Column<int[]>(type: "integer[]", nullable: false),
                    page_numbers = table.Column<int[]>(type: "integer[]", nullable: false),
                    pre_quote_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    resolution_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_conflicts", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_conflicts_resolution", "(\"resolution_status\" = 'Pending' AND \"resolution_note\" IS NULL AND \"resolved_by_user_id\" IS NULL AND \"resolved_at_utc\" IS NULL) OR (\"resolution_status\" IN ('Resolved','Dismissed') AND \"resolution_note\" IS NOT NULL AND \"resolved_by_user_id\" IS NOT NULL AND \"resolved_at_utc\" IS NOT NULL)");
                    table.CheckConstraint("ck_pre_quote_draft_conflicts_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_conflicts_pre_quote_drafts_pre_quote_draft_~",
                        column: x => x.pre_quote_draft_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_conflicts_structured_extraction_conflicts_s~",
                        column: x => x.source_structured_conflict_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_conflicts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_conflicts_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_document_references",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_structured_document_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_document_reference_sequence = table.Column<int>(type: "integer", nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    is_included = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_document_references", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_document_references_origin", "(\"origin\" = 'Ai' AND \"source_structured_document_reference_id\" IS NOT NULL AND \"source_document_reference_sequence\" IS NOT NULL) OR (\"origin\" = 'Manual' AND \"source_structured_document_reference_id\" IS NULL AND \"source_document_reference_sequence\" IS NULL)");
                    table.CheckConstraint("ck_pre_quote_draft_document_references_quantity", "\"quantity\" IS NULL OR \"quantity\" > 0");
                    table.CheckConstraint("ck_pre_quote_draft_document_references_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_document_references_pre_quote_drafts_pre_qu~",
                        column: x => x.pre_quote_draft_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_document_references_structured_extraction_d~",
                        column: x => x.source_structured_document_reference_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_document_references",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_issues",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_structured_issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_issue_sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    item_sequence = table.Column<int>(type: "integer", nullable: true),
                    page_numbers = table.Column<int[]>(type: "integer[]", nullable: false),
                    pre_quote_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    resolution_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_issues", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_issues_resolution", "(\"resolution_status\" = 'Pending' AND \"resolution_note\" IS NULL AND \"resolved_by_user_id\" IS NULL AND \"resolved_at_utc\" IS NULL) OR (\"resolution_status\" IN ('Resolved','Dismissed') AND \"resolution_note\" IS NOT NULL AND \"resolved_by_user_id\" IS NOT NULL AND \"resolved_at_utc\" IS NOT NULL)");
                    table.CheckConstraint("ck_pre_quote_draft_issues_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_issues_pre_quote_drafts_pre_quote_draft_id",
                        column: x => x.pre_quote_draft_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_issues_structured_extraction_issues_source_~",
                        column: x => x.source_structured_issue_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_issues_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_structured_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_item_sequence = table.Column<int>(type: "integer", nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    element_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    raw_measurements = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    width_millimeters = table.Column<int>(type: "integer", nullable: true),
                    height_millimeters = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    is_included = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_items", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_items_origin", "(\"origin\" = 'Ai' AND \"source_structured_item_id\" IS NOT NULL AND \"source_item_sequence\" IS NOT NULL) OR (\"origin\" = 'Manual' AND \"source_structured_item_id\" IS NULL AND \"source_item_sequence\" IS NULL)");
                    table.CheckConstraint("ck_pre_quote_draft_items_sequence", "\"sequence\" > 0");
                    table.CheckConstraint("ck_pre_quote_draft_items_values", "(\"width_millimeters\" IS NULL AND \"height_millimeters\" IS NULL OR \"width_millimeters\" > 0 AND \"height_millimeters\" > 0) AND (\"quantity\" IS NULL OR \"quantity\" > 0)");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_items_pre_quote_drafts_pre_quote_draft_id",
                        column: x => x.pre_quote_draft_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_items_structured_extraction_items_source_st~",
                        column: x => x.source_structured_item_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_requirements",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_structured_requirement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_requirement_sequence = table.Column<int>(type: "integer", nullable: true),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_included = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_requirements", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_requirements_origin", "(\"origin\" = 'Ai' AND \"source_structured_requirement_id\" IS NOT NULL AND \"source_requirement_sequence\" IS NOT NULL) OR (\"origin\" = 'Manual' AND \"source_structured_requirement_id\" IS NULL AND \"source_requirement_sequence\" IS NULL)");
                    table.CheckConstraint("ck_pre_quote_draft_requirements_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_requirements_pre_quote_drafts_pre_quote_dra~",
                        column: x => x.pre_quote_draft_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_requirements_structured_extraction_requirem~",
                        column: x => x.source_structured_requirement_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_conflicts_pre_quote_draft_id_sequence",
                schema: "core",
                table: "pre_quote_draft_conflicts",
                columns: new[] { "pre_quote_draft_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_conflicts_resolved_by_user_id",
                schema: "core",
                table: "pre_quote_draft_conflicts",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_conflicts_source_structured_conflict_id",
                schema: "core",
                table: "pre_quote_draft_conflicts",
                column: "source_structured_conflict_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_document_references_pre_quote_draft_id_sequ~",
                schema: "core",
                table: "pre_quote_draft_document_references",
                columns: new[] { "pre_quote_draft_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_document_references_source_structured_docum~",
                schema: "core",
                table: "pre_quote_draft_document_references",
                column: "source_structured_document_reference_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_issues_pre_quote_draft_id_sequence",
                schema: "core",
                table: "pre_quote_draft_issues",
                columns: new[] { "pre_quote_draft_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_issues_resolved_by_user_id",
                schema: "core",
                table: "pre_quote_draft_issues",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_issues_source_structured_issue_id",
                schema: "core",
                table: "pre_quote_draft_issues",
                column: "source_structured_issue_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_items_pre_quote_draft_id_sequence",
                schema: "core",
                table: "pre_quote_draft_items",
                columns: new[] { "pre_quote_draft_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_items_source_structured_item_id",
                schema: "core",
                table: "pre_quote_draft_items",
                column: "source_structured_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_requirements_pre_quote_draft_id_sequence",
                schema: "core",
                table: "pre_quote_draft_requirements",
                columns: new[] { "pre_quote_draft_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_requirements_source_structured_requirement_~",
                schema: "core",
                table: "pre_quote_draft_requirements",
                column: "source_structured_requirement_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_drafts_approved_by_user_id",
                schema: "core",
                table: "pre_quote_drafts",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_drafts_created_by_user_id",
                schema: "core",
                table: "pre_quote_drafts",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_drafts_pre_quote_id",
                schema: "core",
                table: "pre_quote_drafts",
                column: "pre_quote_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_drafts_source_document_id",
                schema: "core",
                table: "pre_quote_drafts",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_drafts_source_structured_extraction_id",
                schema: "core",
                table: "pre_quote_drafts",
                column: "source_structured_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_drafts_updated_by_user_id",
                schema: "core",
                table: "pre_quote_drafts",
                column: "updated_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_quote_draft_conflicts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_document_references",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_issues",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_items",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_requirements",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_drafts",
                schema: "core");
        }
    }
}
