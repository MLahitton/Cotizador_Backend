using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandFinishCatalogFromHistoricalBdGn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                schema: "core",
                table: "finish_types",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commercial_code",
                schema: "core",
                table: "finish_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_selectable",
                schema: "core",
                table: "finish_types",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "material",
                schema: "core",
                table: "finish_types",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_type",
                schema: "core",
                table: "finish_types",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "process",
                schema: "core",
                table: "finish_types",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "texture",
                schema: "core",
                table: "finish_types",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.InsertData(
                schema: "core",
                table: "catalog_aliases",
                columns: new[] { "id", "alias", "canonical_code", "category", "confidence", "created_at_utc", "is_active", "match_policy", "normalized_alias", "requires_context", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("60000000-0000-0000-0000-000000000016"), "NEGRO PINTURA AL HORNO", "BLACK_MATTE", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "NEGRO PINTURA AL HORNO", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000017"), "PP13", "BLACK_MATTE", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "PP13", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000018"), "BLANCO", "FINISH_PP003", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "BLANCO", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000019"), "PP003", "FINISH_PP003", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "PP003", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000020"), "GRIS", "FINISH_GRAY_POLYESTER", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "GRIS", true, null },
                    { new Guid("60000000-0000-0000-0000-000000000021"), "CHAMPAÑA", "FINISH_CHAMPAGNE_POLY", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "CHAMPANA", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000022"), "CHAMPAGNE", "FINISH_CHAMPAGNE_POLY", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "CHAMPAGNE", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000023"), "ANODIZADO BLANCO", "FINISH_AN001", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "ANODIZADO BLANCO", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000024"), "AN001", "FINISH_AN001", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "AN001", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000025"), "INOX", "FINISH_INOX", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "INOX", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000026"), "ACERO INOXIDABLE", "FINISH_INOX", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "ACERO INOXIDABLE", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000027"), "STAINLESS STEEL", "FINISH_INOX", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "TECHNICAL_PHRASE", "STAINLESS STEEL", false, null },
                    { new Guid("60000000-0000-0000-0000-000000000028"), "N.A", "FINISH_NA", "FINISH", 1.0m, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "EXACT_NORMALIZED", "N.A", false, null }
                });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000001"),
                columns: new[] { "color", "commercial_code", "material", "normalized_type", "process", "texture" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000002"),
                columns: new[] { "color", "commercial_code", "material", "normalized_type", "process", "texture" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000003"),
                columns: new[] { "color", "commercial_code", "is_selectable", "material", "name", "normalized_type", "process", "texture" },
                values: new object[] { "BLACK", "PP13", true, "ALUMINUM", "ALUCOLOR POLIESTER NEGRO MATE PP13", "PAINTED", "POLYESTER", "MATTE" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000004"),
                columns: new[] { "color", "commercial_code", "material", "normalized_type", "process", "texture" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000005"),
                columns: new[] { "color", "commercial_code", "material", "normalized_type", "process", "texture" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.InsertData(
                schema: "core",
                table: "finish_types",
                columns: new[] { "id", "code", "color", "commercial_code", "created_at_utc", "is_active", "is_selectable", "material", "name", "normalized_type", "process", "requires_review", "texture", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("50000000-0000-0000-0000-000000000006"), "FINISH_PP003", "WHITE", "PP003", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "ALUMINUM", "ALUCOLOR POLIESTER BLANCO PP003", "PAINTED", "POLYESTER", false, null, null },
                    { new Guid("50000000-0000-0000-0000-000000000007"), "FINISH_GRAY_POLYESTER", "GRAY", null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "ALUMINUM", "ALUCOLOR POLIESTER PINTURA GRIS", "PAINTED", "POLYESTER", false, null, null },
                    { new Guid("50000000-0000-0000-0000-000000000008"), "FINISH_CHAMPAGNE_POLY", "CHAMPAGNE", null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "ALUMINUM", "ALUCOLOR POLIESTER PINTURA CHAMPAÑA", "PAINTED", "POLYESTER", false, null, null },
                    { new Guid("50000000-0000-0000-0000-000000000009"), "FINISH_AN001", "WHITE", "AN001", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "ALUMINUM", "ANODIZADO BLANCO MATE AN001", "ANODIZED", null, false, "MATTE", null },
                    { new Guid("50000000-0000-0000-0000-000000000010"), "FINISH_INOX", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "STAINLESS_STEEL", "INOX", "STAINLESS_STEEL", null, false, null, null },
                    { new Guid("50000000-0000-0000-0000-000000000011"), "FINISH_NA", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, null, "N.A", "NOT_APPLICABLE", null, true, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "ux_finish_types_name",
                schema: "core",
                table: "finish_types",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_finish_types_name",
                schema: "core",
                table: "finish_types");

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000019"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000020"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000021"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000022"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000023"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000024"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000025"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000026"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000027"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "catalog_aliases",
                keyColumn: "id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000028"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000011"));

            migrationBuilder.DropColumn(
                name: "color",
                schema: "core",
                table: "finish_types");

            migrationBuilder.DropColumn(
                name: "commercial_code",
                schema: "core",
                table: "finish_types");

            migrationBuilder.DropColumn(
                name: "is_selectable",
                schema: "core",
                table: "finish_types");

            migrationBuilder.DropColumn(
                name: "material",
                schema: "core",
                table: "finish_types");

            migrationBuilder.DropColumn(
                name: "normalized_type",
                schema: "core",
                table: "finish_types");

            migrationBuilder.DropColumn(
                name: "process",
                schema: "core",
                table: "finish_types");

            migrationBuilder.DropColumn(
                name: "texture",
                schema: "core",
                table: "finish_types");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "finish_types",
                keyColumn: "id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000003"),
                column: "name",
                value: "Negro mate");
        }
    }
}
