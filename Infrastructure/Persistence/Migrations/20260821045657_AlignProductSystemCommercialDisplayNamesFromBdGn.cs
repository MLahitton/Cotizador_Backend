using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignProductSystemCommercialDisplayNamesFromBdGn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000012"),
                column: "name",
                value: "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
                column: "name",
                value: "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000014"),
                column: "name",
                value: "CUERPO DOBLE BATIENTE LINEA CLASSIC PRIMAVERA SIENA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
                column: "name",
                value: "CUERPO DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000020"),
                column: "name",
                value: "CUERPO FIJO LINEA CLASSIC PRIMAVERA SIENA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000022"),
                column: "name",
                value: "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000025"),
                column: "name",
                value: "CUERPO PLEGABLE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000027"),
                column: "name",
                value: "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000029"),
                column: "name",
                value: "CUERPO PROYECTANTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000040"),
                column: "name",
                value: "PUERTA BATIENTE LINEA CLASSIC PRIMAVERA SIENA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000042"),
                column: "name",
                value: "PUERTA BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000045"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000046"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA CLASSIC PRIMAVERA LUCCA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000048"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000050"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000051"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000059"),
                column: "name",
                value: "PUERTA DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000060"),
                column: "name",
                value: "PUERTA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA PIEGA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000068"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000069"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LUCCA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000071"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000072"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000073"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000077"),
                column: "name",
                value: "VENTANA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA PIEGA");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000012"),
                column: "name",
                value: "CUERPO BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
                column: "name",
                value: "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000014"),
                column: "name",
                value: "CUERPO DOBLE BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
                column: "name",
                value: "CUERPO DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000020"),
                column: "name",
                value: "CUERPO FIJO LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000022"),
                column: "name",
                value: "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000025"),
                column: "name",
                value: "CUERPO PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000027"),
                column: "name",
                value: "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000029"),
                column: "name",
                value: "CUERPO PROYECTANTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000040"),
                column: "name",
                value: "PUERTA BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000042"),
                column: "name",
                value: "PUERTA BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000045"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000046"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000048"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000050"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000051"),
                column: "name",
                value: "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70 TIPO POKET");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000059"),
                column: "name",
                value: "PUERTA DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000060"),
                column: "name",
                value: "PUERTA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000068"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000069"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000071"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000072"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000073"),
                column: "name",
                value: "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70");

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000077"),
                column: "name",
                value: "VENTANA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55");
        }
    }
}
