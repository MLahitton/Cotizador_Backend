using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandGlassCatalogFromHistoricalBdGn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "core",
                table: "glass_types",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<decimal>(
                name: "chamber_thickness_mm",
                schema: "core",
                table: "glass_types",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color",
                schema: "core",
                table: "glass_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "composition",
                schema: "core",
                table: "glass_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "family",
                schema: "core",
                table: "glass_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "inner_thickness_mm",
                schema: "core",
                table: "glass_types",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_selectable",
                schema: "core",
                table: "glass_types",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "outer_thickness_mm",
                schema: "core",
                table: "glass_types",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pattern",
                schema: "core",
                table: "glass_types",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_line",
                schema: "core",
                table: "glass_types",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_token",
                schema: "core",
                table: "glass_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pvb_color",
                schema: "core",
                table: "glass_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pvb_thickness_mm",
                schema: "core",
                table: "glass_types",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pvb_type",
                schema: "core",
                table: "glass_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_review",
                schema: "core",
                table: "glass_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "treatment",
                schema: "core",
                table: "glass_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, null, "RAW", "LAMINATED", 4m, true, "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM INC", 4m, null, null, null, "INC", 0.38m, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, null, "RAW", "LAMINATED", 4m, true, "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM GRIS + 4 MM INC", 4m, null, null, null, "GRIS", 0.38m, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, null, "RAW", "LAMINATED", 5m, true, "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM INC + 5 MM INC", 5m, null, null, null, "INC", 0.38m, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, null, "RAW", "LAMINATED", 5m, true, "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM GRIS + 5 MM INC", 5m, null, null, null, "GRIS", 0.38m, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000005"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, "INC", "TEMPERED", "MONOLITHIC", null, true, "COMPOSICION MONOLITICO TEMPLADO 5 MM INC", 5m, null, null, null, null, null, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000006"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, "INC", "TEMPERED", "MONOLITHIC", null, true, "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m, null, null, null, null, null, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000007"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, "INC", "TEMPERED", "MONOLITHIC", null, true, "COMPOSICION MONOLITICO TEMPLADO 8 MM INC", 8m, null, null, null, null, null, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000008"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, "INC", "TEMPERED", "MONOLITHIC", null, true, "COMPOSICION MONOLITICO TEMPLADO 10 MM INC", 10m, null, null, null, null, null, null, false, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000009"),
                columns: new[] { "chamber_thickness_mm", "color", "composition", "family", "inner_thickness_mm", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment" },
                values: new object[] { null, null, null, null, null, null, null, null, null, null, null, null, true, null });

            migrationBuilder.InsertData(
                schema: "core",
                table: "glass_types",
                columns: new[] { "id", "chamber_thickness_mm", "code", "color", "composition", "created_at_utc", "description", "family", "inner_thickness_mm", "is_active", "is_selectable", "name", "outer_thickness_mm", "pattern", "product_line", "product_token", "pvb_color", "pvb_thickness_mm", "pvb_type", "requires_review", "treatment", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-00000000000a"), null, "TEMP_4", "INC", "TEMPERED", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "MONOLITHIC", null, true, true, "COMPOSICION MONOLITICO TEMPLADO 4 MM INC", 4m, null, null, null, null, null, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000000b"), null, "RAW_4_INC", "INC", "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "MONOLITHIC", null, true, true, "COMPOSICION MONOLITICO CRUDO 4 MM INC", 4m, null, null, null, null, null, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000000c"), null, "RAW_4_MINI_BOREAL", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "MONOLITHIC", null, true, true, "COMPOSICION MONOLITICO CRUDO 4 MM MINI BOREAL", 4m, "MINI_BOREAL", null, null, null, null, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000000d"), null, "RAW_5_INC", "INC", "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "MONOLITHIC", null, true, true, "COMPOSICION MONOLITICO CRUDO 5 MM INC", 5m, null, null, null, null, null, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000000e"), null, "RAW_6_INC", "INC", "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "MONOLITHIC", null, true, true, "COMPOSICION MONOLITICO CRUDO 6 MM INC", 6m, null, null, null, null, null, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000000f"), null, "LAM_4_038_6_INC", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 6m, true, true, "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 6 MM INC", 4m, null, null, null, "INC", 0.38m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000010"), null, "LAM_4_076_6_INC", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 6m, true, true, "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,76 MM INC + 6 MM INC", 4m, null, null, null, "INC", 0.76m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000011"), null, "LAM_4_114_6_INC", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 6m, true, true, "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 1,14 MM INC + 6 MM INC", 4m, null, null, null, "INC", 1.14m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000012"), null, "LAM_6_076_AC_8_INC", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 8m, true, true, "COMPOSICION LAMINADO CRUDO 6 MM INC + PVB 0,76 MM ACÚSTICO + 8 MM INC", 6m, null, null, null, "INC", 0.76m, "ACOUSTIC", false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000013"), null, "LAMT_5_114_5_INC", null, "TEMPERED", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 5m, true, true, "COMPOSICION LAMINADO TEMPLADO 5 MM INC + PVB 1,14 MM INC + 5 MM INC", 5m, null, null, null, "INC", 1.14m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000014"), null, "LAMT_6_152_6_INC", null, "TEMPERED", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 6m, true, true, "COMPOSICION LAMINADO TEMPLADO 6 MM INC + PVB 1,52 MM INC + 6 MM INC", 6m, null, null, null, "INC", 1.52m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000015"), 12m, "IGU_T5_CAM12_T6", "INC", "TEMPERED", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "IGU", 6m, true, true, "COMPOSICION TEMPLADO 5 MM INC + CÁMARA 12 MM + TEMPLADO 6 MM INC", 5m, null, null, null, null, null, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000016"), null, "QG_PREMIUM_CL120", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 4m, true, true, "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL120", 4m, null, "QUALITY_GLASS_PREMIUM", "CL120", "INC", 0.38m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000017"), null, "QG_PREMIUM_CL150", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 4m, true, true, "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL150", 4m, null, "QUALITY_GLASS_PREMIUM", "CL150", "INC", 0.38m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000018"), null, "QG_PREMIUM_CL167", null, "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 4m, true, true, "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL167", 4m, null, "QUALITY_GLASS_PREMIUM", "CL167", "INC", 0.38m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-000000000019"), null, "QG_CLASSIC_BLUE", "BLUE", "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 4m, true, true, "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BLUE", 4m, null, "QUALITY_GLASS_CLASSIC", "BLUE", "INC", 0.38m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000001a"), null, "QG_CLASSIC_BRONZE", "BRONZE", "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 4m, true, true, "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BRONZE", 4m, null, "QUALITY_GLASS_CLASSIC", "BRONZE", "INC", 0.38m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000001b"), null, "QG_CLASSIC_GREEN", "GREEN", "RAW", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "LAMINATED", 4m, true, true, "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM GREEN", 4m, null, "QUALITY_GLASS_CLASSIC", "GREEN", "INC", 0.38m, null, false, null, null },
                    { new Guid("10000000-0000-0000-0000-00000000001c"), null, "GLASS_NA", null, null, new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "NOT_APPLICABLE", null, true, true, "N.A.", null, null, null, null, null, null, null, true, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "ux_glass_types_name",
                schema: "core",
                table: "glass_types",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_glass_types_name",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000000a"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000000b"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000000c"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000000d"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000000e"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000000f"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000019"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000001a"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000001b"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-00000000001c"));

            migrationBuilder.DropColumn(
                name: "chamber_thickness_mm",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "color",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "composition",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "family",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "inner_thickness_mm",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "is_selectable",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "outer_thickness_mm",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "pattern",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "product_line",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "product_token",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "pvb_color",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "pvb_thickness_mm",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "pvb_type",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "requires_review",
                schema: "core",
                table: "glass_types");

            migrationBuilder.DropColumn(
                name: "treatment",
                schema: "core",
                table: "glass_types");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "core",
                table: "glass_types",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

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

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000005"),
                column: "name",
                value: "Vidrio templado monolitico 5 mm");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000006"),
                column: "name",
                value: "Vidrio templado monolitico 6 mm");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000007"),
                column: "name",
                value: "Vidrio templado monolitico 8 mm");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "glass_types",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000008"),
                column: "name",
                value: "Vidrio templado monolitico 10 mm");
        }
    }
}
