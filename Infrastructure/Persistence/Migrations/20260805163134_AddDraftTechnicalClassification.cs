using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftTechnicalClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_quote_draft_item_valuation_snapshot_status",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.CreateTable(
                name: "catalog_aliases",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    alias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_alias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    canonical_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    match_policy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requires_context = table.Column<bool>(type: "boolean", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_aliases", x => x.id);
                    table.CheckConstraint("ck_catalog_aliases_confidence", "\"confidence\" >= 0 AND \"confidence\" <= 1");
                    table.CheckConstraint("ck_catalog_aliases_non_numeric", "\"normalized_alias\" !~ '^[0-9]+$'");
                });

            migrationBuilder.CreateTable(
                name: "finish_types",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finish_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "frame_types",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frame_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_systems",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    active_for_recognition = table.Column<bool>(type: "boolean", nullable: false),
                    priceable = table.Column<bool>(type: "boolean", nullable: false),
                    future_priceable = table.Column<bool>(type: "boolean", nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_systems", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "structured_extraction_item_technical_classifications",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_extraction_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    system_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    system_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    system_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    frame_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    frame_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    frame_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    frame_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    finish_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    finish_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    finish_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    finish_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    review_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_item_technical_classifications", x => x.id);
                    table.CheckConstraint("ck_structured_item_technical_confidence", "(\"system_confidence\" IS NULL OR \"system_confidence\" >= 0 AND \"system_confidence\" <= 1) AND (\"frame_confidence\" IS NULL OR \"frame_confidence\" >= 0 AND \"frame_confidence\" <= 1) AND (\"finish_confidence\" IS NULL OR \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1)");
                    table.CheckConstraint("ck_structured_item_technical_review", "(\"requires_review\" = false AND cardinality(\"review_reasons\") = 0) OR (\"requires_review\" = true AND cardinality(\"review_reasons\") > 0)");
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_technical_classifications_struct~",
                        column: x => x.structured_extraction_item_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_item_technical_snapshots",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_draft_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_structured_item_technical_classification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    system_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    system_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    system_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    frame_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    frame_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    frame_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    frame_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    finish_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    finish_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    finish_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    finish_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    review_reasons = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_item_technical_snapshots", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_item_technical_snapshot_confidence", "(\"system_confidence\" IS NULL OR \"system_confidence\" >= 0 AND \"system_confidence\" <= 1) AND (\"frame_confidence\" IS NULL OR \"frame_confidence\" >= 0 AND \"frame_confidence\" <= 1) AND (\"finish_confidence\" IS NULL OR \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1)");
                    table.CheckConstraint("ck_pre_quote_draft_item_technical_snapshot_review", "(\"requires_review\" = false AND cardinality(\"review_reasons\") = 0) OR (\"requires_review\" = true AND cardinality(\"review_reasons\") > 0)");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_technical_snapshots_pre_quote_draft_it~",
                        column: x => x.pre_quote_draft_item_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_draft_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_technical_snapshots_structured_extract~",
                        column: x => x.source_structured_item_technical_classification_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_item_technical_classifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "catalog_aliases",
                columns: new[] { "id", "alias", "canonical_code", "category", "confidence", "created_at_utc", "is_active", "match_policy", "normalized_alias", "requires_context", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("60000000-0000-0000-0000-000000000001"), "VENECIA SERIE 40", "K40", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "VENECIA SERIE 40", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000002"), "VENECIA_SERIE_40", "K40", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "VENECIA_SERIE_40", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000003"), "SERIE 40", "K40", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "SERIE 40", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000004"), "VENECIA SERIE 50", "K50", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "VENECIA SERIE 50", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000005"), "VENECIA_SERIE_50", "K50", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "VENECIA_SERIE_50", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000006"), "SERIE 50", "K50", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "SERIE 50", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000007"), "VENECIA SERIE 70", "K70", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "VENECIA SERIE 70", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000008"), "VENECIA_SERIE_70", "K70", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "VENECIA_SERIE_70", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000009"), "SERIE 70", "K70", "SYSTEM", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "SERIE 70", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000010"), "SG0047", "MARCO_47", "FRAME", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "SG0047", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000011"), "MARCO SG0047", "MARCO_47", "FRAME", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "MARCO SG0047", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000012"), "SG0058", "MARCO_58", "FRAME", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "SG0058", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000013"), "MARCO SG0058", "MARCO_58", "FRAME", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "MARCO SG0058", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000014"), "NEGRO MATE", "BLACK_MATTE", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "NEGRO MATE", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000015"), "ALUCOLOR POLIESTER NEGRO MATE", "BLACK_MATTE", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "ALUCOLOR POLIESTER NEGRO MATE", false, null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "finish_types",
                columns: new[] { "id", "code", "created_at_utc", "is_active", "name", "requires_review", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("50000000-0000-0000-0000-000000000001"), "STANDARD_NATURAL", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Acabado natural estandar", false, null },
                    { new Guid("50000000-0000-0000-0000-000000000002"), "ANODIZED_GRAY", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Anodizado gris", false, null },
                    { new Guid("50000000-0000-0000-0000-000000000003"), "BLACK_MATTE", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Negro mate", false, null },
                    { new Guid("50000000-0000-0000-0000-000000000004"), "SPECIAL", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Acabado especial", true, null },
                    { new Guid("50000000-0000-0000-0000-000000000005"), "UNKNOWN", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Acabado por confirmar", true, null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "frame_types",
                columns: new[] { "id", "code", "created_at_utc", "is_active", "name", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-0000-0000-000000000001"), "MARCO_47", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Marco 47 mm", null },
                    { new Guid("40000000-0000-0000-0000-000000000002"), "MARCO_58", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Marco 58 mm", null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "product_systems",
                columns: new[] { "id", "active_for_recognition", "code", "created_at_utc", "future_priceable", "is_active", "name", "priceable", "requires_review", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), true, "K40", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema K40", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000002"), true, "K50", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema K50", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000003"), true, "K55", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema K55", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000004"), true, "K70", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema K70", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000005"), true, "K90", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema K90", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000006"), true, "K100", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema K100", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000007"), true, "S35", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema S35", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000008"), true, "S50", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema S50", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000009"), true, "S80", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema S80", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000010"), true, "3890", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema 3890", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000011"), true, "SG45", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema SG45", true, false, null },
                    { new Guid("30000000-0000-0000-0000-000000000012"), true, "BARANDA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema para barandas", false, true, null },
                    { new Guid("30000000-0000-0000-0000-000000000013"), true, "DIVISION_BANO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Sistema para divisiones de bano", false, true, null }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_quote_draft_item_valuation_snapshot_status",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                sql: "\"status\" IN ('NotApplicable', 'Pending', 'Valued', 'Stale', 'RequiresReview', 'NotPriceable')");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_aliases_category_canonical_code",
                schema: "core",
                table: "catalog_aliases",
                columns: new[] { "category", "canonical_code" });

            migrationBuilder.CreateIndex(
                name: "ux_catalog_aliases_category_normalized_alias",
                schema: "core",
                table: "catalog_aliases",
                columns: new[] { "category", "normalized_alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_finish_types_code",
                schema: "core",
                table: "finish_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_frame_types_code",
                schema: "core",
                table: "frame_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_technical_snapshots_pre_quote_draft_it~",
                schema: "core",
                table: "pre_quote_draft_item_technical_snapshots",
                column: "pre_quote_draft_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_technical_snapshots_source_structured_~",
                schema: "core",
                table: "pre_quote_draft_item_technical_snapshots",
                column: "source_structured_item_technical_classification_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_systems_code",
                schema: "core",
                table: "product_systems",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_technical_classifications_struct~",
                schema: "core",
                table: "structured_extraction_item_technical_classifications",
                column: "structured_extraction_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_aliases",
                schema: "core");

            migrationBuilder.DropTable(
                name: "finish_types",
                schema: "core");

            migrationBuilder.DropTable(
                name: "frame_types",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pre_quote_draft_item_technical_snapshots",
                schema: "core");

            migrationBuilder.DropTable(
                name: "product_systems",
                schema: "core");

            migrationBuilder.DropTable(
                name: "structured_extraction_item_technical_classifications",
                schema: "core");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_quote_draft_item_valuation_snapshot_status",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_quote_draft_item_valuation_snapshot_status",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                sql: "\"status\" IN ('NotApplicable', 'Pending', 'Valued', 'Stale', 'RequiresReview')");
        }
    }
}
