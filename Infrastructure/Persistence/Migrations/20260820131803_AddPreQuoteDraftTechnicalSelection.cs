using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreQuoteDraftTechnicalSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commercial_line",
                schema: "core",
                table: "product_systems",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commercial_name",
                schema: "core",
                table: "product_systems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "family",
                schema: "core",
                table: "product_systems",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "functional_type",
                schema: "core",
                table: "product_systems",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_selectable",
                schema: "core",
                table: "product_systems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "series",
                schema: "core",
                table: "product_systems",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "technical_name",
                schema: "core",
                table: "product_systems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "variant",
                schema: "core",
                table: "product_systems",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pre_quote_draft_item_technical_selections",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_quote_draft_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_system_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_system_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suggested_system_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    selected_system_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_glass_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_glass_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suggested_glass_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    selected_glass_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_finish_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_finish_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suggested_finish_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    selected_finish_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_hardware_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_hardware_original_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suggested_hardware_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    selected_hardware_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    selection_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    review_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    requested_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    suggested_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    selected_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_draft_item_technical_selections", x => x.id);
                    table.CheckConstraint("ck_pre_quote_draft_item_technical_selection_confidence", "\"confidence\" IS NULL OR \"confidence\" >= 0 AND \"confidence\" <= 1");
                    table.CheckConstraint("ck_pre_quote_draft_item_technical_selection_review", "(\"requires_review\" = false AND cardinality(\"review_reasons\") = 0) OR (\"requires_review\" = true AND cardinality(\"review_reasons\") > 0)");
                    table.ForeignKey(
                        name: "FK_pre_quote_draft_item_technical_selections_pre_quote_draft_i~",
                        column: x => x.pre_quote_draft_item_id,
                        principalSchema: "core",
                        principalTable: "pre_quote_draft_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000001"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000002"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000003"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000005"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000006"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000007"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000008"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000009"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000010"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000011"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000012"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { null, null, null, null, false, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_pre_quote_draft_item_technical_selections_pre_quote_draft_i~",
                schema: "core",
                table: "pre_quote_draft_item_technical_selections",
                column: "pre_quote_draft_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_quote_draft_item_technical_selections",
                schema: "core");

            migrationBuilder.DropColumn(
                name: "commercial_line",
                schema: "core",
                table: "product_systems");

            migrationBuilder.DropColumn(
                name: "commercial_name",
                schema: "core",
                table: "product_systems");

            migrationBuilder.DropColumn(
                name: "family",
                schema: "core",
                table: "product_systems");

            migrationBuilder.DropColumn(
                name: "functional_type",
                schema: "core",
                table: "product_systems");

            migrationBuilder.DropColumn(
                name: "is_selectable",
                schema: "core",
                table: "product_systems");

            migrationBuilder.DropColumn(
                name: "series",
                schema: "core",
                table: "product_systems");

            migrationBuilder.DropColumn(
                name: "technical_name",
                schema: "core",
                table: "product_systems");

            migrationBuilder.DropColumn(
                name: "variant",
                schema: "core",
                table: "product_systems");
        }
    }
}
