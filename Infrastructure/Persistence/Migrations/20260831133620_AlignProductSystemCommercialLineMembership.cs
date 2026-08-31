using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignProductSystemCommercialLineMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000021"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000022"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000028"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000029"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000042"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000047"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000048"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000049"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000050"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000051"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000052"),
                column: "commercial_line",
                value: "CLASSIC");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000054"),
                column: "commercial_line",
                value: "CLASSIC");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000059"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000060"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000070"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000071"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000072"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000073"),
                column: "commercial_line",
                value: "SIGNATURE");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000074"),
                column: "commercial_line",
                value: "CLASSIC");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000075"),
                column: "commercial_line",
                value: "CLASSIC");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000076"),
                column: "commercial_line",
                value: "CLASSIC");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000077"),
                column: "commercial_line",
                value: "SIGNATURE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000021"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000022"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000028"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000029"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000042"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000047"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000048"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000049"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000050"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000051"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000052"),
                column: "commercial_line",
                value: "TRADITIONAL");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000054"),
                column: "commercial_line",
                value: "TRADITIONAL");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000059"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000060"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000070"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000071"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000072"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000073"),
                column: "commercial_line",
                value: "PREMIUM");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000074"),
                column: "commercial_line",
                value: "TRADITIONAL");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000075"),
                column: "commercial_line",
                value: "TRADITIONAL");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000076"),
                column: "commercial_line",
                value: "TRADITIONAL");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000077"),
                column: "commercial_line",
                value: "PREMIUM");
        }
    }
}
