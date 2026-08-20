using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSgTechnicalSignalsAndCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_items_values",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.AddColumn<decimal>(
                name: "area_square_meters",
                schema: "core",
                table: "structured_extraction_items",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "configuration",
                schema: "core",
                table: "structured_extraction_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fixed_panel_count",
                schema: "core",
                table: "structured_extraction_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "functional_type",
                schema: "core",
                table: "structured_extraction_items",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geometry_type",
                schema: "core",
                table: "structured_extraction_items",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "modulation",
                schema: "core",
                table: "structured_extraction_items",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "movable_panel_count",
                schema: "core",
                table: "structured_extraction_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "opening_direction",
                schema: "core",
                table: "structured_extraction_items",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation",
                schema: "core",
                table: "structured_extraction_items",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "panel_count",
                schema: "core",
                table: "structured_extraction_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "special_features",
                schema: "core",
                table: "structured_extraction_items",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000001"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "ESSENTIAL", "VENECIA FERMO", "VENECIA FERMO", "FIXED", true, "40", "CUERPO FIJO SISTEMA VENECIA SERIE 40", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000002"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "ESSENTIAL", "VENECIA MONZA", "VENECIA MONZA", "SLIDING_WINDOW", true, "50", "VENTANA CORREDIZA SISTEMA VENECIA SERIE 50", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000003"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "ESSENTIAL", "VENECIA PIEGA", "VENECIA PIEGA", "FOLDING_DOOR", true, "55", "PUERTA PLEGABLE SISTEMA VENECIA SERIE 55", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "ESSENTIAL", "VENECIA NAPOLES", "VENECIA NAPOLES", "SLIDING_DOOR", true, "70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000006"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "ESSENTIAL", "VENECIA MONACO", "VENECIA MONACO", "SLIDING_WINDOW", true, "100", "VENTANA CORREDIZA SISTEMA VENECIA SERIE 100", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000007"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "CLASSIC", "PRIMAVERA SIENA", "PRIMAVERA SIENA", "PROJECTING", true, "SG 4", "CUERPO PROYECTANTE SISTEMA PRIMAVERA SG 4", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000008"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "CLASSIC", "PRIMAVERA LAGO", "PRIMAVERA LAGO", "SLIDING_WINDOW", true, "SG 5", "VENTANA CORREDIZA SISTEMA PRIMAVERA SG 5", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000009"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "CLASSIC", "PRIMAVERA LUCCA", "PRIMAVERA LUCCA", "SLIDING_WINDOW", true, "SG 8", "VENTANA CORREDIZA SISTEMA PRIMAVERA SG 8", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000010"),
                columns: new[] { "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "series", "technical_name", "variant" },
                values: new object[] { "CLASSIC", "SG 3890", "SG 3890", "SWING_DOOR", true, "3890", "PUERTA BATIENTE SISTEMA SG 3890", "STANDARD" });

            migrationBuilder.InsertData(
                schema: "core",
                table: "product_systems",
                columns: new[] { "id", "active_for_recognition", "code", "commercial_line", "commercial_name", "created_at_utc", "family", "functional_type", "future_priceable", "is_active", "is_selectable", "name", "priceable", "requires_review", "series", "technical_name", "updated_at_utc", "variant" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000014"), true, "SG_VEN70_POCKET_DOOR", "ESSENTIAL", "VENECIA NAPOLES POCKET", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA NAPOLES", "SLIDING_DOOR", true, true, true, "Puerta corrediza pocket Venecia serie 70", true, false, "70", "PUERTA CORREDIZA POCKET SISTEMA VENECIA SERIE 70", null, "POCKET" },
                    { new Guid("30000000-0000-0000-0000-000000000015"), true, "SG_PRIM_SIENA_CASEMENT", "CLASSIC", "PRIMAVERA SIENA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA SIENA", "CASEMENT", true, true, true, "Ventana batiente Primavera Siena", true, false, "SG 4", "VENTANA BATIENTE SISTEMA PRIMAVERA SG 4", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000016"), true, "SG_PRIM_SIENA_DBL_CASE", "CLASSIC", "PRIMAVERA SIENA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA SIENA", "DOUBLE_CASEMENT", true, true, true, "Ventana doble batiente Primavera Siena", true, false, "SG 4", "VENTANA DOBLE BATIENTE SISTEMA PRIMAVERA SG 4", null, "DOUBLE" },
                    { new Guid("30000000-0000-0000-0000-000000000017"), true, "SG_PERGOLA", "SPECIAL", "PERGOLA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "PERGOLA", true, true, true, "Pergola", false, true, null, "PERGOLA", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000018"), true, "SG_BATH_DIV_INOX", "SPECIAL", "DIVISION DE BANO INOX", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "BATHROOM_DIVISION", true, true, true, "Division de bano inox", false, true, null, "DIVISION DE BANO INOX", null, "INOX" },
                    { new Guid("30000000-0000-0000-0000-000000000019"), true, "SG_LOUVER", "SPECIAL", "PERSIANA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LOUVER", true, true, true, "Persiana", false, true, null, "PERSIANA", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000020"), true, "SG_SKYLIGHT", "SPECIAL", "CLARABOYA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SKYLIGHT", true, true, true, "Claraboia", false, true, null, "CLARABOYA", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000021"), true, "SG_SYS_NA", null, "N.A", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, false, true, false, "N.A", false, true, null, "N.A", null, null }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_items_values",
                schema: "core",
                table: "structured_extraction_items",
                sql: "(\"width_millimeters\" IS NULL AND \"height_millimeters\" IS NULL OR \"width_millimeters\" > 0 AND \"height_millimeters\" > 0) AND (\"quantity\" IS NULL OR \"quantity\" > 0) AND (\"area_square_meters\" IS NULL OR \"area_square_meters\" > 0) AND (\"panel_count\" IS NULL OR \"panel_count\" > 0) AND (\"movable_panel_count\" IS NULL OR \"movable_panel_count\" >= 0) AND (\"fixed_panel_count\" IS NULL OR \"fixed_panel_count\" >= 0) AND \"sequence\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_items_values",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000019"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000020"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000021"));

            migrationBuilder.DropColumn(
                name: "area_square_meters",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "configuration",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "fixed_panel_count",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "functional_type",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "geometry_type",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "modulation",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "movable_panel_count",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "opening_direction",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "operation",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "panel_count",
                schema: "core",
                table: "structured_extraction_items");

            migrationBuilder.DropColumn(
                name: "special_features",
                schema: "core",
                table: "structured_extraction_items");

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_items_values",
                schema: "core",
                table: "structured_extraction_items",
                sql: "(\"width_millimeters\" IS NULL AND \"height_millimeters\" IS NULL OR \"width_millimeters\" > 0 AND \"height_millimeters\" > 0) AND (\"quantity\" IS NULL OR \"quantity\" > 0) AND \"sequence\" > 0");
        }
    }
}
