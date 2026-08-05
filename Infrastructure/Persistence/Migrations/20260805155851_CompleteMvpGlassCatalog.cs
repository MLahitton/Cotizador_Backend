using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMvpGlassCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_glass_valuation_prices",
                schema: "core",
                table: "structured_extraction_item_glass_valuations");

            migrationBuilder.AddColumn<decimal>(
                name: "expected_amount",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_price_per_square_meter",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_amount_per_m2",
                schema: "core",
                table: "glass_price_range_versions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE core.glass_price_range_versions
                SET expected_amount_per_m2 = CASE
                    WHEN id = '20000000-0000-0000-0000-000000000001' THEN 100000
                    WHEN id = '20000000-0000-0000-0000-000000000003' THEN 130000
                    WHEN minimum_price_per_square_meter = maximum_price_per_square_meter
                        THEN minimum_price_per_square_meter
                    ELSE ROUND(
                        (minimum_price_per_square_meter + maximum_price_per_square_meter) / 2,
                        2)
                    END
                WHERE expected_amount_per_m2 = 0;
                """);

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                column: "expected_amount_per_m2",
                value: 100000m);

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000002"),
                columns: new[] { "expected_amount_per_m2", "status", "valid_to_utc" },
                values: new object[] { 95000m, "RETIRED", new DateTimeOffset(new DateTime(2026, 8, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000003"),
                column: "expected_amount_per_m2",
                value: 130000m);

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000004"),
                columns: new[] { "expected_amount_per_m2", "status", "valid_to_utc" },
                values: new object[] { 135000m, "RETIRED", new DateTimeOffset(new DateTime(2026, 8, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                column: "name",
                value: "Vidrio laminado 4+4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"),
                column: "name",
                value: "Vidrio laminado gris 4+4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"),
                column: "name",
                value: "Vidrio laminado 5+5");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"),
                column: "name",
                value: "Vidrio laminado gris 5+5");

            migrationBuilder.InsertData(
                schema: "core",
                table: "glass_types",
                columns: new[] { "id", "code", "created_at_utc", "description", "is_active", "name", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000005"), "TEMP_5", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vidrio templado monolitico 5 mm", null },
                    { new Guid("10000000-0000-0000-0000-000000000006"), "TEMP_6", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vidrio templado monolitico 6 mm", null },
                    { new Guid("10000000-0000-0000-0000-000000000007"), "TEMP_8", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vidrio templado monolitico 8 mm", null },
                    { new Guid("10000000-0000-0000-0000-000000000008"), "TEMP_10", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vidrio templado monolitico 10 mm", null },
                    { new Guid("10000000-0000-0000-0000-000000000009"), "UNKNOWN_GLASS", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Tipo de vidrio por confirmar", null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "glass_price_range_versions",
                columns: new[] { "id", "created_at_utc", "currency", "expected_amount_per_m2", "glass_type_id", "maximum_price_per_square_meter", "minimum_price_per_square_meter", "status", "valid_from_utc", "valid_to_utc", "version" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", 74000m, new Guid("10000000-0000-0000-0000-000000000005"), 74000m, 74000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("20000000-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", 86000m, new Guid("10000000-0000-0000-0000-000000000006"), 86000m, 86000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("20000000-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", 90000m, new Guid("10000000-0000-0000-0000-000000000007"), 90000m, 90000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("20000000-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", 126000m, new Guid("10000000-0000-0000-0000-000000000008"), 126000m, 126000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_glass_valuation_expected_amount",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                sql: "\"expected_amount\" IS NULL OR \"expected_amount\" >= \"minimum_amount\" AND \"expected_amount\" <= \"maximum_amount\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_glass_valuation_prices",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                sql: "\"minimum_price_per_square_meter\" IS NULL OR \"minimum_price_per_square_meter\" > 0 AND \"expected_price_per_square_meter\" >= \"minimum_price_per_square_meter\" AND \"expected_price_per_square_meter\" <= \"maximum_price_per_square_meter\" AND \"maximum_price_per_square_meter\" >= \"minimum_price_per_square_meter\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_glass_price_range_versions_expected_price",
                schema: "core",
                table: "glass_price_range_versions",
                sql: "\"expected_amount_per_m2\" >= \"minimum_price_per_square_meter\" AND \"expected_amount_per_m2\" <= \"maximum_price_per_square_meter\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_glass_valuation_expected_amount",
                schema: "core",
                table: "structured_extraction_item_glass_valuations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_structured_glass_valuation_prices",
                schema: "core",
                table: "structured_extraction_item_glass_valuations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_glass_price_range_versions_expected_price",
                schema: "core",
                table: "glass_price_range_versions");

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000008"));

            migrationBuilder.DropColumn(
                name: "expected_amount",
                schema: "core",
                table: "structured_extraction_item_glass_valuations");

            migrationBuilder.DropColumn(
                name: "expected_price_per_square_meter",
                schema: "core",
                table: "structured_extraction_item_glass_valuations");

            migrationBuilder.DropColumn(
                name: "expected_amount_per_m2",
                schema: "core",
                table: "glass_price_range_versions");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000002"),
                columns: new[] { "status", "valid_to_utc" },
                values: new object[] { "PRELIMINARY", null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_price_range_versions",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000004"),
                columns: new[] { "status", "valid_to_utc" },
                values: new object[] { "PRELIMINARY", null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                column: "name",
                value: "Laminado 4+4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"),
                column: "name",
                value: "Laminado 4+4 gris");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"),
                column: "name",
                value: "Laminado 5+5");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"),
                column: "name",
                value: "Laminado 5+5 gris");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structured_glass_valuation_prices",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                sql: "\"minimum_price_per_square_meter\" IS NULL OR \"minimum_price_per_square_meter\" > 0 AND \"maximum_price_per_square_meter\" >= \"minimum_price_per_square_meter\"");
        }
    }
}
