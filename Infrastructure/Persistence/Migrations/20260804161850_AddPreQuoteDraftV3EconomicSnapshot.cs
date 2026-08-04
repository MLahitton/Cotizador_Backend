using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreQuoteDraftV3EconomicSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValuationStatus",
                schema: "core",
                table: "pre_quote_draft_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_item_glass_snapshots",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_draft_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_structured_item_glass_id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    raw_specification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    normalized_code_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    assignment_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_item_glass_snapshots", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_snapshot_identity", "(\"normalized_code_snapshot\" IS NULL AND \"glass_type_id\" IS NULL) OR (\"normalized_code_snapshot\" IS NOT NULL AND \"glass_type_id\" IS NOT NULL)");
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_snapshot_requirements", "\"requires_review\" IS NOT NULL");
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_snapshot_scope", "\"assignment_scope\" IN ('Item', 'Section', 'General', 'Unassigned')");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_glass_snapshots_pre_quote_draft_items_~",
                        column: x => x.pre_quote_draft_item_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_draft_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_item_valuation_snapshots",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_draft_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_structured_item_valuation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    glass_price_range_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    width_millimeters_used = table.Column<int>(type: "integer", nullable: true),
                    height_millimeters_used = table.Column<int>(type: "integer", nullable: true),
                    quantity_used = table.Column<int>(type: "integer", nullable: true),
                    unit_area_square_meters = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    total_area_square_meters = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    unit_price_per_square_meter = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    unit_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    valued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invalidated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invalidation_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_item_valuation_snapshots", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_amounts", "\"unit_amount\" IS NULL OR \"unit_amount\" >= 0 AND \"total_amount\" >= \"unit_amount\"");
                    table.CheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_areas", "\"unit_area_square_meters\" IS NULL OR \"unit_area_square_meters\" >= 0 AND \"total_area_square_meters\" >= 0");
                    table.CheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_currency", "\"currency\" IS NULL OR char_length(\"currency\") = 3");
                    table.CheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_prices", "\"unit_price_per_square_meter\" IS NULL OR \"unit_price_per_square_meter\" > 0");
                    table.CheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_status", "\"status\" IN ('NotApplicable', 'Pending', 'Valued', 'Stale', 'RequiresReview')");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_valuation_snapshots_glass_price_range_~",
                        column: x => x.glass_price_range_version_id,
                        principalSchema: "core",
                        principalTable: "glass_price_range_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_valuation_snapshots_glass_types_glass_~",
                        column: x => x.glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_valuation_snapshots_pre_quote_draft_it~",
                        column: x => x.pre_quote_draft_item_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_draft_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_item_glass_evidence",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_item_glass_evidence", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_evidence_page_number", "\"page_number\" > 0");
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_evidence_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_glass_evidence_pre_quote_draft_item_gl~",
                        column: x => x.glass_snapshot_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_draft_item_glass_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_item_glass_review_reasons",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_item_glass_review_reasons", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_review_reason_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_glass_review_reasons_pre_quote_draft_i~",
                        column: x => x.glass_snapshot_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_draft_item_glass_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_item_glass_source_pages",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_item_glass_source_pages", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_source_page_page_number", "\"page_number\" > 0");
                    table.CheckConstraint("ck_pre_quote_draft_item_glass_source_page_sequence", "\"sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_glass_source_pages_pre_quote_draft_ite~",
                        column: x => x.glass_snapshot_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_draft_item_glass_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_evidence_glass_snapshot_id_page_~",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                columns: new[] { "glass_snapshot_id", "page_number", "source_type", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_evidence_glass_snapshot_id_seque~",
                schema: "core",
                table: "pre_quote_draft_item_glass_evidence",
                columns: new[] { "glass_snapshot_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_review_reasons_glass_snapshot_i~1",
                schema: "core",
                table: "pre_quote_draft_item_glass_review_reasons",
                columns: new[] { "glass_snapshot_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_review_reasons_glass_snapshot_id~",
                schema: "core",
                table: "pre_quote_draft_item_glass_review_reasons",
                columns: new[] { "glass_snapshot_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_source_pages_glass_snapshot_id_p~",
                schema: "core",
                table: "pre_quote_draft_item_glass_source_pages",
                columns: new[] { "glass_snapshot_id", "page_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_source_pages_glass_snapshot_id_s~",
                schema: "core",
                table: "pre_quote_draft_item_glass_source_pages",
                columns: new[] { "glass_snapshot_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_glass_snapshots_pre_quote_draft_item_id",
                schema: "core",
                table: "pre_quote_draft_item_glass_snapshots",
                column: "pre_quote_draft_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_valuation_snapshots_glass_price_range_~",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                column: "glass_price_range_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_valuation_snapshots_glass_type_id",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                column: "glass_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_valuation_snapshots_pre_quote_draft_it~",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                column: "pre_quote_draft_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_quote_draft_item_glass_evidence",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_item_glass_review_reasons",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_item_glass_source_pages",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_item_valuation_snapshots",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_item_glass_snapshots",
                schema: "core");

            migrationBuilder.DropColumn(
                name: "ValuationStatus",
                schema: "core",
                table: "pre_quote_draft_items");
        }
    }
}
