using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandProductSystemCatalogFromHistoricalBdGn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ReleaseExistingProductSystemCodes(migrationBuilder);

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000001"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_APLIQUE_ACCESORIOS", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA DE APLIQUE CON ACCESORIOS INOX CON TUBO SUPERIOR EN ALUMINIO SG", false, true, null, "BARANDA DE APLIQUE CON ACCESORIOS INOX CON TUBO SUPERIOR EN ALUMINIO SG", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000002"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_APLIQUE_ACCESORI_2", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA DE APLIQUE CON ACCESORIOS INOX CON TUBO SUPERIOR INOX", false, true, null, "BARANDA DE APLIQUE CON ACCESORIOS INOX CON TUBO SUPERIOR INOX", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000003"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_APLIQUE_ACCESORI_3", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA DE APLIQUE CON ACCESORIOS INOX SIN TUBO SUPERIOR", false, true, null, "BARANDA DE APLIQUE CON ACCESORIOS INOX SIN TUBO SUPERIOR", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_EMBEBIDA_TUBO_SUPE", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA EMBEBIDA CON TUBO SUPERIOR EN ALUMINIO SG", false, true, null, "BARANDA EMBEBIDA CON TUBO SUPERIOR EN ALUMINIO SG", "TUBO_SUPERIOR" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000005"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "is_selectable", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_EMBEBIDA_TUBO_SU_2", "SPECIAL", "BARANDA", "BARANDA", true, "BARANDA EMBEBIDA CON TUBO SUPERIOR EN INOX", false, true, "BARANDA EMBEBIDA CON TUBO SUPERIOR EN INOX", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000006"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_EMBEBIDA_TUBO_SU_3", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA EMBEBIDA SIN TUBO SUPERIOR", false, true, null, "BARANDA EMBEBIDA SIN TUBO SUPERIOR", "TUBO_SUPERIOR" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000007"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_EMBEBIDA_TUBO_SU_4", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA EMBEBIDA SIN TUBO SUPERIOR EN ALUMINIO SG", false, true, null, "BARANDA EMBEBIDA SIN TUBO SUPERIOR EN ALUMINIO SG", "TUBO_SUPERIOR" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000008"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_BOTELLAS_ACCESORIO", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA EN BOTELLAS  CON ACCESORIOS INOX CON TUBO SUPERIOR", false, true, null, "BARANDA EN BOTELLAS  CON ACCESORIOS INOX CON TUBO SUPERIOR", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000009"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDA_BOTELLAS_ACCESOR_2", "SPECIAL", "BARANDA", "BARANDA", null, "BARANDA EN BOTELLAS  CON ACCESORIOS INOX SIN TUBO SUPERIOR", false, true, null, "BARANDA EN BOTELLAS  CON ACCESORIOS INOX SIN TUBO SUPERIOR", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000010"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDILLA_TUBO_SUPERIOR_I", null, null, null, null, "BARANDILLA CON TUBO SUPERIOR EN INOX", null, "BARANDILLA CON TUBO SUPERIOR EN INOX", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000011"),
                columns: new[] { "code", "is_selectable", "name", "technical_name", "variant" },
                values: new object[] { "SYS_BARANDILLA_TUBO_SUPERIOR", true, "BARANDILLA SIN TUBO SUPERIOR", "BARANDILLA SIN TUBO SUPERIOR", "TUBO_SUPERIOR" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000012"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SG_PRIM_SIENA_CASEMENT", "CLASSIC", "PRIMAVERA SIENA", "PRIMAVERA SIENA", "CASEMENT", true, "CUERPO BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", true, false, "SG 4", "CUERPO BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "SYS_CUERPO_BATIENTE_PREMIUM_VE", "PREMIUM", "VENECIA FERMO", "VENECIA FERMO", "CASEMENT", true, "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", true, false, "SERIE 40", "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000014"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name", "variant" },
                values: new object[] { "SG_PRIM_SIENA_DBL_CASE", "CLASSIC", "PRIMAVERA SIENA", "PRIMAVERA SIENA", "DOUBLE_CASEMENT", "CUERPO DOBLE BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "SG 4", "CUERPO DOBLE BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name" },
                values: new object[] { "SYS_CUERPO_DOBLE_BATIENTE_PREM", "PREMIUM", "VENECIA FERMO", "VENECIA FERMO", "DOUBLE_CASEMENT", "CUERPO DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "SERIE 40", "CUERPO DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000016"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name", "variant" },
                values: new object[] { "SYS_CUERPO_FIJO", null, null, null, "FIXED", "CUERPO FIJO", null, "CUERPO FIJO", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000017"),
                columns: new[] { "code", "commercial_line", "commercial_name", "functional_type", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "SYS_CUERPO_FIJO_ACCESORIOS_INO", null, null, "FIXED", "CUERPO FIJO CON ACCESORIOS INOX", true, false, "CUERPO FIJO CON ACCESORIOS INOX", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000018"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "SYS_CUERPO_FIJO_CLASSIC_3831", "CLASSIC", "SG 3831", "SG 3831", "FIXED", "CUERPO FIJO LINEA CLASSIC SISTEMA 3831", true, false, "CUERPO FIJO LINEA CLASSIC SISTEMA 3831", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000019"),
                columns: new[] { "code", "commercial_line", "commercial_name", "functional_type", "name", "priceable", "requires_review", "technical_name" },
                values: new object[] { "SYS_CUERPO_FIJO_CLASSIC_PRIMAV", "CLASSIC", null, "FIXED", "CUERPO FIJO LINEA CLASSIC SISTEMA PRIMAVERA SERIE      SG 3", true, false, "CUERPO FIJO LINEA CLASSIC SISTEMA PRIMAVERA SERIE      SG 3" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000020"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name" },
                values: new object[] { "SYS_CUERPO_FIJO_CLASSIC_PRIM_2", "CLASSIC", "PRIMAVERA SIENA", "PRIMAVERA SIENA", "FIXED", "CUERPO FIJO LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", true, false, "SG 4", "CUERPO FIJO LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000021"),
                columns: new[] { "code", "commercial_line", "commercial_name", "functional_type", "future_priceable", "is_selectable", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "SYS_CUERPO_FIJO_PREMIUM_LSA", "PREMIUM", null, "FIXED", true, true, "CUERPO FIJO LINEA PREMIUM EUROPEO SISTEMA LSA 0932", true, false, "CUERPO FIJO LINEA PREMIUM EUROPEO SISTEMA LSA 0932", "STANDARD" });

            migrationBuilder.InsertData(
                schema: "core",
                table: "product_systems",
                columns: new[] { "id", "active_for_recognition", "code", "commercial_line", "commercial_name", "created_at_utc", "family", "functional_type", "future_priceable", "is_active", "is_selectable", "name", "priceable", "requires_review", "series", "technical_name", "updated_at_utc", "variant" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000022"), true, "K40", "PREMIUM", "VENECIA FERMO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA FERMO", "FIXED", true, true, true, "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", true, false, "SERIE 40", "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000023"), true, "SYS_CUERPO_FIJO_TRADICIONAL_SG", "TRADITIONAL", "SG 3831", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 3831", "FIXED", true, true, true, "CUERPO FIJO LINEA TRADICIONAL SISTEMA SG 3831", true, false, "SG 3831", "CUERPO FIJO LINEA TRADICIONAL SISTEMA SG 3831", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000024"), true, "SYS_CUERPO_FIJO_TUBULAR_SG", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "FIXED", true, true, true, "CUERPO FIJO TUBULAR SG", true, false, null, "CUERPO FIJO TUBULAR SG", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000025"), true, "SYS_CUERPO_PLEGABLE_PREMIUM_VE", "PREMIUM", "VENECIA FERMO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA FERMO", "FOLDING_WINDOW", true, true, true, "CUERPO PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", true, false, "SERIE 40", "CUERPO PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000026"), true, "SYS_CUERPO_PROYECTANTE_CLASSIC", "CLASSIC", "SG 3831", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 3831", "PROJECTING", true, true, true, "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA 3831", true, false, null, "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA 3831", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000027"), true, "S35", "CLASSIC", "PRIMAVERA SIENA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA SIENA", "PROJECTING", true, true, true, "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", true, false, "SG 4", "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000028"), true, "SYS_CUERPO_PROYECTANTE_PREMIUM", "PREMIUM", null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "PROJECTING", true, true, true, "CUERPO PROYECTANTE LINEA PREMIUM EUROPEO SISTEMA LSA 0932", true, false, null, "CUERPO PROYECTANTE LINEA PREMIUM EUROPEO SISTEMA LSA 0932", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000029"), true, "SYS_CUERPO_PROYECTANTE_PREMI_2", "PREMIUM", "VENECIA FERMO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA FERMO", "PROJECTING", true, true, true, "CUERPO PROYECTANTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", true, false, "SERIE 40", "CUERPO PROYECTANTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000030"), true, "SYS_CUERPO_PROYECTANTE_TRADICI", "TRADITIONAL", "SG 3831", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 3831", "PROJECTING", true, true, true, "CUERPO PROYECTANTE LINEA TRADICIONAL SISTEMA SG 3831", true, false, "SG 3831", "CUERPO PROYECTANTE LINEA TRADICIONAL SISTEMA SG 3831", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000031"), true, "SG_BATH_DIV_INOX", "SPECIAL", null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SHOWER_DIVISION", true, true, true, "DIVISIONES DE BAÑO CON ACCESORIOS EN ACERO INOXIDABLE", false, false, null, "DIVISIONES DE BAÑO CON ACCESORIOS EN ACERO INOXIDABLE", null, "INOX" },
                    { new Guid("30000000-0000-0000-0000-000000000032"), true, "SG45", "STICK", "SG45", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG45", "FIXED", true, true, true, "FACHADA ENTRE PLACAS LINEA STICK SISTEMA SG45 PIEL DE VIDRIO", false, true, "SG45", "FACHADA ENTRE PLACAS LINEA STICK SISTEMA SG45 PIEL DE VIDRIO", null, "PIEL_DE_VIDRIO" },
                    { new Guid("30000000-0000-0000-0000-000000000033"), true, "SYS_FACHADA_ENTRE_PLACAS_STICK", "STICK", "SG45", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG45", "FIXED", true, true, true, "FACHADA ENTRE PLACAS LINEA STICK SISTEMA SG45 TAPA Y PISAVIDRIO", false, true, "SG45", "FACHADA ENTRE PLACAS LINEA STICK SISTEMA SG45 TAPA Y PISAVIDRIO", null, "TAPA_PISAVIDRIO" },
                    { new Guid("30000000-0000-0000-0000-000000000034"), true, "SYS_FACHADA_FLOTANTE_STICK_SG1", "STICK", "SG101", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG101", "FIXED", true, true, true, "FACHADA FLOTANTE LINEA STICK SISTEMA SG101", false, true, "SG101", "FACHADA FLOTANTE LINEA STICK SISTEMA SG101", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000035"), true, "SYS_FACHADA_FLOTANTE_STICK_S_2", "STICK", "SG103", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG103", "FIXED", true, true, true, "FACHADA FLOTANTE LINEA STICK SISTEMA SG103", false, true, "SG103", "FACHADA FLOTANTE LINEA STICK SISTEMA SG103", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000036"), true, "SYS_FACHADA_FLOTANTE_STICK_SG4", "STICK", "SG45", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG45", "FIXED", true, true, true, "FACHADA FLOTANTE LINEA STICK SISTEMA SG45 PIEL DE VIDRIO", false, true, "SG45", "FACHADA FLOTANTE LINEA STICK SISTEMA SG45 PIEL DE VIDRIO", null, "PIEL_DE_VIDRIO" },
                    { new Guid("30000000-0000-0000-0000-000000000037"), true, "SYS_FACHADA_FLOTANTE_STICK_S_3", "STICK", "SG45", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG45", "FIXED", true, true, true, "FACHADA FLOTANTE LINEA STICK SISTEMA SG45 TAPA Y PISAVIDRIO", false, true, "SG45", "FACHADA FLOTANTE LINEA STICK SISTEMA SG45 TAPA Y PISAVIDRIO", null, "TAPA_PISAVIDRIO" },
                    { new Guid("30000000-0000-0000-0000-000000000038"), true, "SG_SYS_NA", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, true, false, "N.A", false, true, null, "N.A", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000039"), true, "SYS_PUERTA_BATIENTE_ACCESORIOS", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SWING_DOOR", true, true, true, "PUERTA BATIENTE CON ACCESORIOS INOX", true, false, null, "PUERTA BATIENTE CON ACCESORIOS INOX", null, "INOX" },
                    { new Guid("30000000-0000-0000-0000-000000000040"), true, "SYS_PUERTA_BATIENTE_CLASSIC_PR", "CLASSIC", "PRIMAVERA SIENA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA SIENA", "SWING_DOOR", true, true, true, "PUERTA BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", true, false, "SG 4", "PUERTA BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000041"), true, "3890", "CLASSIC", "SG 3890", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 3890", "SWING_DOOR", true, true, true, "PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", true, false, "SG 3890", "PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000042"), true, "SYS_PUERTA_BATIENTE_PREMIUM_VE", "PREMIUM", "VENECIA FERMO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA FERMO", "SWING_DOOR", true, true, true, "PUERTA BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", true, false, "SERIE 40", "PUERTA BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000043"), true, "SYS_PUERTA_CORREDIZA_ACCESIORI", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA CON ACCESIORIOS INOX", true, false, null, "PUERTA CORREDIZA CON ACCESIORIOS INOX", null, "INOX" },
                    { new Guid("30000000-0000-0000-0000-000000000044"), true, "SYS_PUERTA_CORREDIZA_CLASSIC_8", "CLASSIC", "SG 8025", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 8025", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA CLASSIC SISTEMA 8025", true, false, null, "PUERTA CORREDIZA LINEA CLASSIC SISTEMA 8025", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000045"), true, "SYS_PUERTA_CORREDIZA_CLASSIC_P", "CLASSIC", "PRIMAVERA LAGO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA LAGO", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5", true, false, "SG 5", "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000046"), true, "SYS_PUERTA_CORREDIZA_CLASSIC_2", "CLASSIC", "PRIMAVERA LUCCA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA LUCCA", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8", true, false, "SG 8", "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000047"), true, "SYS_PUERTA_CORREDIZA_PREMIUM_L", "PREMIUM", null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9052", true, false, null, "PUERTA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9052", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000048"), true, "SYS_PUERTA_CORREDIZA_PREMIUM_V", "PREMIUM", "VENECIA MONACO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA MONACO", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100", true, false, "SERIE 100", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000049"), true, "SYS_PUERTA_CORREDIZA_PREMIUM_2", "PREMIUM", "VENECIA MONACO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA MONACO", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100 TIPO POKET", true, false, "SERIE 100", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100 TIPO POKET", null, "POCKET" },
                    { new Guid("30000000-0000-0000-0000-000000000050"), true, "K70", "PREMIUM", "VENECIA NAPOLES", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA NAPOLES", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", true, false, "SERIE 70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000051"), true, "SG_VEN70_POCKET_DOOR", "PREMIUM", "VENECIA NAPOLES", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA NAPOLES", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70 TIPO POKET", true, false, "SERIE 70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70 TIPO POKET", null, "POCKET" },
                    { new Guid("30000000-0000-0000-0000-000000000052"), true, "SYS_PUERTA_CORREDIZA_TRADICION", "TRADITIONAL", "SG 7038", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 7038", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 7038", true, false, "SG 7038", "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 7038", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000053"), true, "SYS_PUERTA_CORREDIZA_TRADICI_2", "TRADITIONAL", "SG 744", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 744", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 744", true, false, "SG 744", "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 744", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000054"), true, "SYS_PUERTA_CORREDIZA_TRADICI_3", "TRADITIONAL", "SG 8025", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 8025", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 8025", true, false, "SG 8025", "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 8025", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000055"), true, "SYS_PUERTA_CORREDIZA_SG_3890", null, "SG 3890", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 3890", "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA SG 3890", true, false, "SG 3890", "PUERTA CORREDIZA SG 3890", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000056"), true, "SYS_PUERTA_CORREDIZA_TUBULAR_S", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SLIDING_DOOR", true, true, true, "PUERTA CORREDIZA TUBULAR SG", true, false, null, "PUERTA CORREDIZA TUBULAR SG", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000057"), true, "SYS_PUERTA_DOBLE_BATIENTE_ACCE", null, null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SWING_DOOR", true, true, true, "PUERTA DOBLE BATIENTE CON ACCESORIOS INOX", true, false, null, "PUERTA DOBLE BATIENTE CON ACCESORIOS INOX", null, "INOX" },
                    { new Guid("30000000-0000-0000-0000-000000000058"), true, "SYS_PUERTA_DOBLE_BATIENTE_CLAS", "CLASSIC", "SG 3890", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 3890", "SWING_DOOR", true, true, true, "PUERTA DOBLE BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", true, false, "SG 3890", "PUERTA DOBLE BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000059"), true, "SYS_PUERTA_DOBLE_BATIENTE_PREM", "PREMIUM", "VENECIA FERMO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA FERMO", "SWING_DOOR", true, true, true, "PUERTA DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", true, false, "SERIE 40", "PUERTA DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000060"), true, "K55", "PREMIUM", "VENECIA PIEGA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA PIEGA", "FOLDING_DOOR", true, true, true, "PUERTA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55", true, false, "SERIE 55", "PUERTA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000061"), true, "SYS_APILABLE_SIGMA", "SPECIAL", "SIGMA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SIGMA", null, true, true, true, "SISTEMA APILABLE SIGMA", false, true, null, "SISTEMA APILABLE SIGMA", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000062"), true, "SYS_DESLIZANTE_TWIN_DN", "SPECIAL", "TWIN DN", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TWIN DN", "SLIDING_DOOR", true, true, true, "SISTEMA DESLIZANTE TWIN DN", false, true, null, "SISTEMA DESLIZANTE TWIN DN", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000063"), true, "SG_PERGOLA", "SPECIAL", "PERGOLA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PERGOLA", "PERGOLA", true, true, true, "SISTEMA PERGOLA SG", false, false, null, "SISTEMA PERGOLA SG", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000064"), true, "SYS_PLEGABLE_TAURO", "SPECIAL", "TAURO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TAURO", "FOLDING_WINDOW", true, true, true, "SISTEMA PLEGABLE TAURO", false, true, null, "SISTEMA PLEGABLE TAURO", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000065"), true, "SG_LOUVER", "SPECIAL", "REJILLA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "REJILLA", "GRILLE", true, true, true, "SISTEMA REJILLA", false, false, null, "SISTEMA REJILLA", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000066"), true, "SG_SKYLIGHT", "SPECIAL", "CLARABOYA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "CLARABOYA", "SKYLIGHT", true, true, true, "SISTEMA SG CLARABOYA", false, false, null, "SISTEMA SG CLARABOYA", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000067"), true, "SYS_VENTANA_CORREDIZA_CLASSIC", "CLASSIC", null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 3", true, false, null, "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 3", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000068"), true, "S50", "CLASSIC", "PRIMAVERA LAGO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA LAGO", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5", true, false, "SG 5", "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000069"), true, "S80", "CLASSIC", "PRIMAVERA LUCCA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PRIMAVERA LUCCA", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8", true, false, "SG 8", "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000070"), true, "SYS_VENTANA_CORREDIZA_PREMIUM", "PREMIUM", null, new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9060", true, false, null, "VENTANA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9060", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000071"), true, "K100", "PREMIUM", "VENECIA MONACO", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA MONACO", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100", true, false, "SERIE 100", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000072"), true, "K50", "PREMIUM", "VENECIA MONZA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA MONZA", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50", true, false, "SERIE 50", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000073"), true, "SYS_VENTANA_CORREDIZA_PREMIU_2", "PREMIUM", "VENECIA NAPOLES", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA NAPOLES", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", true, false, "SERIE 70", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000074"), true, "SYS_VENTANA_CORREDIZA_TRADICIO", "TRADITIONAL", "SG 5020", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 5020", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 5020", true, false, "SG 5020", "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 5020", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000075"), true, "SYS_VENTANA_CORREDIZA_TRADIC_2", "TRADITIONAL", "SG 744", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 744", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 744", true, false, "SG 744", "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 744", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000076"), true, "SYS_VENTANA_CORREDIZA_TRADIC_3", "TRADITIONAL", "SG 8025", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SG 8025", "SLIDING_WINDOW", true, true, true, "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 8025", true, false, "SG 8025", "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 8025", null, "STANDARD" },
                    { new Guid("30000000-0000-0000-0000-000000000077"), true, "SYS_VENTANA_PLEGABLE_PREMIUM_V", "PREMIUM", "VENECIA PIEGA", new DateTimeOffset(new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VENECIA PIEGA", "FOLDING_WINDOW", true, true, true, "VENTANA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55", true, false, "SERIE 55", "VENTANA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55", null, "STANDARD" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ReleaseExistingProductSystemCodes(migrationBuilder);

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000022"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000023"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000024"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000025"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000026"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000027"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000028"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000029"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000030"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000031"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000032"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000033"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000034"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000035"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000036"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000037"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000038"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000039"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000040"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000041"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000042"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000043"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000044"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000045"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000046"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000047"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000048"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000049"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000050"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000051"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000052"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000053"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000054"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000055"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000056"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000057"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000058"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000059"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000060"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000061"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000062"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000063"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000064"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000065"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000066"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000067"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000068"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000069"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000070"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000071"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000072"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000073"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000074"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000075"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000076"));

            migrationBuilder.DeleteData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000077"));

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000001"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "K40", "ESSENTIAL", "VENECIA FERMO", "VENECIA FERMO", "FIXED", "Sistema K40", true, false, "40", "CUERPO FIJO SISTEMA VENECIA SERIE 40", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000002"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "K50", "ESSENTIAL", "VENECIA MONZA", "VENECIA MONZA", "SLIDING_WINDOW", "Sistema K50", true, false, "50", "VENTANA CORREDIZA SISTEMA VENECIA SERIE 50", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000003"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "K55", "ESSENTIAL", "VENECIA PIEGA", "VENECIA PIEGA", "FOLDING_DOOR", "Sistema K55", true, false, "55", "PUERTA PLEGABLE SISTEMA VENECIA SERIE 55", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "K70", "ESSENTIAL", "VENECIA NAPOLES", "VENECIA NAPOLES", "SLIDING_DOOR", "Sistema K70", true, false, "70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000005"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "is_selectable", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "K90", null, null, null, false, "Sistema K90", true, false, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000006"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "K100", "ESSENTIAL", "VENECIA MONACO", "VENECIA MONACO", "SLIDING_WINDOW", "Sistema K100", true, false, "100", "VENTANA CORREDIZA SISTEMA VENECIA SERIE 100", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000007"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "S35", "CLASSIC", "PRIMAVERA SIENA", "PRIMAVERA SIENA", "PROJECTING", "Sistema S35", true, false, "SG 4", "CUERPO PROYECTANTE SISTEMA PRIMAVERA SG 4", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000008"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "S50", "CLASSIC", "PRIMAVERA LAGO", "PRIMAVERA LAGO", "SLIDING_WINDOW", "Sistema S50", true, false, "SG 5", "VENTANA CORREDIZA SISTEMA PRIMAVERA SG 5", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000009"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "S80", "CLASSIC", "PRIMAVERA LUCCA", "PRIMAVERA LUCCA", "SLIDING_WINDOW", "Sistema S80", true, false, "SG 8", "VENTANA CORREDIZA SISTEMA PRIMAVERA SG 8", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000010"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name", "variant" },
                values: new object[] { "3890", "CLASSIC", "SG 3890", "SG 3890", "SWING_DOOR", "Sistema 3890", "3890", "PUERTA BATIENTE SISTEMA SG 3890", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000011"),
                columns: new[] { "code", "is_selectable", "name", "technical_name", "variant" },
                values: new object[] { "SG45", false, "Sistema SG45", null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000012"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "BARANDA", null, null, null, null, false, "Sistema para barandas", false, true, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "is_selectable", "name", "priceable", "requires_review", "series", "technical_name", "variant" },
                values: new object[] { "DIVISION_BANO", null, null, null, null, false, "Sistema para divisiones de bano", false, true, null, null, null });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000014"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name", "variant" },
                values: new object[] { "SG_VEN70_POCKET_DOOR", "ESSENTIAL", "VENECIA NAPOLES POCKET", "VENECIA NAPOLES", "SLIDING_DOOR", "Puerta corrediza pocket Venecia serie 70", "70", "PUERTA CORREDIZA POCKET SISTEMA VENECIA SERIE 70", "POCKET" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name" },
                values: new object[] { "SG_PRIM_SIENA_CASEMENT", "CLASSIC", "PRIMAVERA SIENA", "PRIMAVERA SIENA", "CASEMENT", "Ventana batiente Primavera Siena", "SG 4", "VENTANA BATIENTE SISTEMA PRIMAVERA SG 4" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000016"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "series", "technical_name", "variant" },
                values: new object[] { "SG_PRIM_SIENA_DBL_CASE", "CLASSIC", "PRIMAVERA SIENA", "PRIMAVERA SIENA", "DOUBLE_CASEMENT", "Ventana doble batiente Primavera Siena", "SG 4", "VENTANA DOBLE BATIENTE SISTEMA PRIMAVERA SG 4", "DOUBLE" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000017"),
                columns: new[] { "code", "commercial_line", "commercial_name", "functional_type", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "SG_PERGOLA", "SPECIAL", "PERGOLA", "PERGOLA", "Pergola", false, true, "PERGOLA", "STANDARD" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000018"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "SG_BATH_DIV_INOX", "SPECIAL", "DIVISION DE BANO INOX", null, "BATHROOM_DIVISION", "Division de bano inox", false, true, "DIVISION DE BANO INOX", "INOX" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000019"),
                columns: new[] { "code", "commercial_line", "commercial_name", "functional_type", "name", "priceable", "requires_review", "technical_name" },
                values: new object[] { "SG_LOUVER", "SPECIAL", "PERSIANA", "LOUVER", "Persiana", false, true, "PERSIANA" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000020"),
                columns: new[] { "code", "commercial_line", "commercial_name", "family", "functional_type", "name", "priceable", "requires_review", "series", "technical_name" },
                values: new object[] { "SG_SKYLIGHT", "SPECIAL", "CLARABOYA", null, "SKYLIGHT", "Claraboia", false, true, null, "CLARABOYA" });

            migrationBuilder.UpdateData(
                schema: "core",
                table: "product_systems",
                keyColumn: "id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000021"),
                columns: new[] { "code", "commercial_line", "commercial_name", "functional_type", "future_priceable", "is_selectable", "name", "priceable", "requires_review", "technical_name", "variant" },
                values: new object[] { "SG_SYS_NA", null, "N.A", null, false, false, "N.A", false, true, "N.A", null });
        }

        private static void ReleaseExistingProductSystemCodes(
            MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE core.product_systems AS target
                SET code = source.code
                FROM (VALUES
                    ('30000000-0000-0000-0000-000000000001'::uuid, '__MIG_PRODUCT_SYSTEM_001'),
                    ('30000000-0000-0000-0000-000000000002'::uuid, '__MIG_PRODUCT_SYSTEM_002'),
                    ('30000000-0000-0000-0000-000000000003'::uuid, '__MIG_PRODUCT_SYSTEM_003'),
                    ('30000000-0000-0000-0000-000000000004'::uuid, '__MIG_PRODUCT_SYSTEM_004'),
                    ('30000000-0000-0000-0000-000000000005'::uuid, '__MIG_PRODUCT_SYSTEM_005'),
                    ('30000000-0000-0000-0000-000000000006'::uuid, '__MIG_PRODUCT_SYSTEM_006'),
                    ('30000000-0000-0000-0000-000000000007'::uuid, '__MIG_PRODUCT_SYSTEM_007'),
                    ('30000000-0000-0000-0000-000000000008'::uuid, '__MIG_PRODUCT_SYSTEM_008'),
                    ('30000000-0000-0000-0000-000000000009'::uuid, '__MIG_PRODUCT_SYSTEM_009'),
                    ('30000000-0000-0000-0000-000000000010'::uuid, '__MIG_PRODUCT_SYSTEM_010'),
                    ('30000000-0000-0000-0000-000000000011'::uuid, '__MIG_PRODUCT_SYSTEM_011'),
                    ('30000000-0000-0000-0000-000000000012'::uuid, '__MIG_PRODUCT_SYSTEM_012'),
                    ('30000000-0000-0000-0000-000000000013'::uuid, '__MIG_PRODUCT_SYSTEM_013'),
                    ('30000000-0000-0000-0000-000000000014'::uuid, '__MIG_PRODUCT_SYSTEM_014'),
                    ('30000000-0000-0000-0000-000000000015'::uuid, '__MIG_PRODUCT_SYSTEM_015'),
                    ('30000000-0000-0000-0000-000000000016'::uuid, '__MIG_PRODUCT_SYSTEM_016'),
                    ('30000000-0000-0000-0000-000000000017'::uuid, '__MIG_PRODUCT_SYSTEM_017'),
                    ('30000000-0000-0000-0000-000000000018'::uuid, '__MIG_PRODUCT_SYSTEM_018'),
                    ('30000000-0000-0000-0000-000000000019'::uuid, '__MIG_PRODUCT_SYSTEM_019'),
                    ('30000000-0000-0000-0000-000000000020'::uuid, '__MIG_PRODUCT_SYSTEM_020'),
                    ('30000000-0000-0000-0000-000000000021'::uuid, '__MIG_PRODUCT_SYSTEM_021')
                ) AS source(id, code)
                WHERE target.id = source.id;
                """);
        }
    }
}
