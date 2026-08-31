using Domain.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProductSystemConfiguration
    : IEntityTypeConfiguration<ProductSystem>
{
    internal static readonly DateTimeOffset SeededAtUtc =
        new(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<ProductSystem> builder)
    {
        builder.ToTable("product_systems", "core");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(value => value.TechnicalName).HasColumnName("technical_name").HasMaxLength(100);
        builder.Property(value => value.CommercialName).HasColumnName("commercial_name").HasMaxLength(100);
        builder.Property(value => value.FunctionalType).HasColumnName("functional_type").HasMaxLength(60);
        builder.Property(value => value.Family).HasColumnName("family").HasMaxLength(60);
        builder.Property(value => value.Series).HasColumnName("series").HasMaxLength(60);
        builder.Property(value => value.CommercialLine).HasColumnName("commercial_line").HasMaxLength(60);
        builder.Property(value => value.Variant).HasColumnName("variant").HasMaxLength(60);
        builder.Property(value => value.IsSelectable).HasColumnName("is_selectable").IsRequired();
        builder.Property(value => value.ActiveForRecognition).HasColumnName("active_for_recognition").IsRequired();
        builder.Property(value => value.Priceable).HasColumnName("priceable").IsRequired();
        builder.Property(value => value.FuturePriceable).HasColumnName("future_priceable").IsRequired();
        builder.Property(value => value.RequiresReview).HasColumnName("requires_review").IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.Code).IsUnique().HasDatabaseName("ux_product_systems_code");
        builder.HasMany(value => value.Constraints)
            .WithOne(value => value.ProductSystem)
            .HasForeignKey(value => value.ProductSystemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(SystemSeeds.Select(value => Seed(
            value.Sequence,
            value.Code,
            value.DisplayName,
            activeForRecognition: true,
            priceable: IsPriceable(value.DisplayName),
            futurePriceable: true,
            requiresReview: RequiresSystemReview(value.DisplayName),
            technicalName: value.TechnicalName,
            commercialName: CommercialName(value.TechnicalName),
            functionalType: FunctionalType(value.TechnicalName),
            family: Family(value.TechnicalName),
            series: Series(value.TechnicalName),
            commercialLine: CommercialLine(value.DisplayName),
            variant: Variant(value.TechnicalName),
            isSelectable: IsSelectableSystem(value.DisplayName))).ToArray());
    }

    private static readonly SystemSeed[] SystemSeeds =
    [
        new(1, "SYS_BARANDA_APLIQUE_ACCESORIOS", "BARANDA DE APLIQUE CON ACCESORIOS INOX CON TUBO SUPERIOR EN ALUMINIO SG"),
        new(2, "SYS_BARANDA_APLIQUE_ACCESORI_2", "BARANDA DE APLIQUE CON ACCESORIOS INOX CON TUBO SUPERIOR INOX"),
        new(3, "SYS_BARANDA_APLIQUE_ACCESORI_3", "BARANDA DE APLIQUE CON ACCESORIOS INOX SIN TUBO SUPERIOR"),
        new(4, "SYS_BARANDA_EMBEBIDA_TUBO_SUPE", "BARANDA EMBEBIDA CON TUBO SUPERIOR EN ALUMINIO SG"),
        new(5, "SYS_BARANDA_EMBEBIDA_TUBO_SU_2", "BARANDA EMBEBIDA CON TUBO SUPERIOR EN INOX"),
        new(6, "SYS_BARANDA_EMBEBIDA_TUBO_SU_3", "BARANDA EMBEBIDA SIN TUBO SUPERIOR"),
        new(7, "SYS_BARANDA_EMBEBIDA_TUBO_SU_4", "BARANDA EMBEBIDA SIN TUBO SUPERIOR EN ALUMINIO SG"),
        new(8, "SYS_BARANDA_BOTELLAS_ACCESORIO", "BARANDA EN BOTELLAS  CON ACCESORIOS INOX CON TUBO SUPERIOR"),
        new(9, "SYS_BARANDA_BOTELLAS_ACCESOR_2", "BARANDA EN BOTELLAS  CON ACCESORIOS INOX SIN TUBO SUPERIOR"),
        new(10, "SYS_BARANDILLA_TUBO_SUPERIOR_I", "BARANDILLA CON TUBO SUPERIOR EN INOX"),
        new(11, "SYS_BARANDILLA_TUBO_SUPERIOR", "BARANDILLA SIN TUBO SUPERIOR"),
        new(12, "SG_PRIM_SIENA_CASEMENT", "CUERPO BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA"),
        new(13, "SYS_CUERPO_BATIENTE_PREMIUM_VE", "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO"),
        new(14, "SG_PRIM_SIENA_DBL_CASE", "CUERPO DOBLE BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "CUERPO DOBLE BATIENTE LINEA CLASSIC PRIMAVERA SIENA"),
        new(15, "SYS_CUERPO_DOBLE_BATIENTE_PREM", "CUERPO DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "CUERPO DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO"),
        new(16, "SYS_CUERPO_FIJO", "CUERPO FIJO"),
        new(17, "SYS_CUERPO_FIJO_ACCESORIOS_INO", "CUERPO FIJO CON ACCESORIOS INOX"),
        new(18, "SYS_CUERPO_FIJO_CLASSIC_3831", "CUERPO FIJO LINEA CLASSIC SISTEMA 3831"),
        new(19, "SYS_CUERPO_FIJO_CLASSIC_PRIMAV", "CUERPO FIJO LINEA CLASSIC SISTEMA PRIMAVERA SERIE      SG 3"),
        new(20, "SYS_CUERPO_FIJO_CLASSIC_PRIM_2", "CUERPO FIJO LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "CUERPO FIJO LINEA CLASSIC PRIMAVERA SIENA"),
        new(21, "SYS_CUERPO_FIJO_PREMIUM_LSA", "CUERPO FIJO LINEA PREMIUM EUROPEO SISTEMA LSA 0932"),
        new(22, "K40", "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO"),
        new(23, "SYS_CUERPO_FIJO_TRADICIONAL_SG", "CUERPO FIJO LINEA TRADICIONAL SISTEMA SG 3831"),
        new(24, "SYS_CUERPO_FIJO_TUBULAR_SG", "CUERPO FIJO TUBULAR SG"),
        new(25, "SYS_CUERPO_PLEGABLE_PREMIUM_VE", "CUERPO PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "CUERPO PLEGABLE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO"),
        new(26, "SYS_CUERPO_PROYECTANTE_CLASSIC", "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA 3831"),
        new(27, "S35", "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA"),
        new(28, "SYS_CUERPO_PROYECTANTE_PREMIUM", "CUERPO PROYECTANTE LINEA PREMIUM EUROPEO SISTEMA LSA 0932"),
        new(29, "SYS_CUERPO_PROYECTANTE_PREMI_2", "CUERPO PROYECTANTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "CUERPO PROYECTANTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO"),
        new(30, "SYS_CUERPO_PROYECTANTE_TRADICI", "CUERPO PROYECTANTE LINEA TRADICIONAL SISTEMA SG 3831"),
        new(31, "SG_BATH_DIV_INOX", "DIVISIONES DE BAÑO CON ACCESORIOS EN ACERO INOXIDABLE"),
        new(32, "SG45", "FACHADA ENTRE PLACAS LINEA STICK SISTEMA SG45 PIEL DE VIDRIO"),
        new(33, "SYS_FACHADA_ENTRE_PLACAS_STICK", "FACHADA ENTRE PLACAS LINEA STICK SISTEMA SG45 TAPA Y PISAVIDRIO"),
        new(34, "SYS_FACHADA_FLOTANTE_STICK_SG1", "FACHADA FLOTANTE LINEA STICK SISTEMA SG101"),
        new(35, "SYS_FACHADA_FLOTANTE_STICK_S_2", "FACHADA FLOTANTE LINEA STICK SISTEMA SG103"),
        new(36, "SYS_FACHADA_FLOTANTE_STICK_SG4", "FACHADA FLOTANTE LINEA STICK SISTEMA SG45 PIEL DE VIDRIO"),
        new(37, "SYS_FACHADA_FLOTANTE_STICK_S_3", "FACHADA FLOTANTE LINEA STICK SISTEMA SG45 TAPA Y PISAVIDRIO"),
        new(38, "SG_SYS_NA", "N.A"),
        new(39, "SYS_PUERTA_BATIENTE_ACCESORIOS", "PUERTA BATIENTE CON ACCESORIOS INOX"),
        new(40, "SYS_PUERTA_BATIENTE_CLASSIC_PR", "PUERTA BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "PUERTA BATIENTE LINEA CLASSIC PRIMAVERA SIENA"),
        new(41, "3890", "PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890"),
        new(42, "SYS_PUERTA_BATIENTE_PREMIUM_VE", "PUERTA BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "PUERTA BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO"),
        new(43, "SYS_PUERTA_CORREDIZA_ACCESIORI", "PUERTA CORREDIZA CON ACCESIORIOS INOX"),
        new(44, "SYS_PUERTA_CORREDIZA_CLASSIC_8", "PUERTA CORREDIZA LINEA CLASSIC SISTEMA 8025"),
        new(45, "SYS_PUERTA_CORREDIZA_CLASSIC_P", "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5", "PUERTA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO"),
        new(46, "SYS_PUERTA_CORREDIZA_CLASSIC_2", "PUERTA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8", "PUERTA CORREDIZA LINEA CLASSIC PRIMAVERA LUCCA"),
        new(47, "SYS_PUERTA_CORREDIZA_PREMIUM_L", "PUERTA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9052"),
        new(48, "SYS_PUERTA_CORREDIZA_PREMIUM_V", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO"),
        new(49, "SYS_PUERTA_CORREDIZA_PREMIUM_2", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100 TIPO POKET"),
        new(50, "K70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES"),
        new(51, "SG_VEN70_POCKET_DOOR", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70 TIPO POKET", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET"),
        new(52, "SYS_PUERTA_CORREDIZA_TRADICION", "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 7038"),
        new(53, "SYS_PUERTA_CORREDIZA_TRADICI_2", "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 744"),
        new(54, "SYS_PUERTA_CORREDIZA_TRADICI_3", "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA  SG 8025"),
        new(55, "SYS_PUERTA_CORREDIZA_SG_3890", "PUERTA CORREDIZA SG 3890"),
        new(56, "SYS_PUERTA_CORREDIZA_TUBULAR_S", "PUERTA CORREDIZA TUBULAR SG"),
        new(57, "SYS_PUERTA_DOBLE_BATIENTE_ACCE", "PUERTA DOBLE BATIENTE CON ACCESORIOS INOX"),
        new(58, "SYS_PUERTA_DOBLE_BATIENTE_CLAS", "PUERTA DOBLE BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890"),
        new(59, "SYS_PUERTA_DOBLE_BATIENTE_PREM", "PUERTA DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "PUERTA DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO"),
        new(60, "K55", "PUERTA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55", "PUERTA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA PIEGA"),
        new(61, "SYS_APILABLE_SIGMA", "SISTEMA APILABLE SIGMA"),
        new(62, "SYS_DESLIZANTE_TWIN_DN", "SISTEMA DESLIZANTE TWIN DN"),
        new(63, "SG_PERGOLA", "SISTEMA PERGOLA SG"),
        new(64, "SYS_PLEGABLE_TAURO", "SISTEMA PLEGABLE TAURO"),
        new(65, "SG_LOUVER", "SISTEMA REJILLA"),
        new(66, "SG_SKYLIGHT", "SISTEMA SG CLARABOYA"),
        new(67, "SYS_VENTANA_CORREDIZA_CLASSIC", "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 3"),
        new(68, "S50", "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5", "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO"),
        new(69, "S80", "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 8", "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LUCCA"),
        new(70, "SYS_VENTANA_CORREDIZA_PREMIUM", "VENTANA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9060"),
        new(71, "K100", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO"),
        new(72, "K50", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA"),
        new(73, "SYS_VENTANA_CORREDIZA_PREMIU_2", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES"),
        new(74, "SYS_VENTANA_CORREDIZA_TRADICIO", "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 5020"),
        new(75, "SYS_VENTANA_CORREDIZA_TRADIC_2", "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 744"),
        new(76, "SYS_VENTANA_CORREDIZA_TRADIC_3", "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 8025"),
        new(77, "SYS_VENTANA_PLEGABLE_PREMIUM_V", "VENTANA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 55", "VENTANA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA PIEGA")
    ];

    private static readonly string[] ConfirmedClassicSystemNames =
    [
        "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA",
        "CUERPO DOBLE BATIENTE LINEA CLASSIC PRIMAVERA SIENA",
        "CUERPO FIJO LINEA CLASSIC SISTEMA 3831",
        "CUERPO FIJO LINEA CLASSIC PRIMAVERA SERIE SG 3",
        "CUERPO FIJO LINEA CLASSIC PRIMAVERA SIENA",
        "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA 3831",
        "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA",
        "PUERTA BATIENTE LINEA CLASSIC PRIMAVERA SIENA",
        "PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890",
        "PUERTA DOBLE BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890",
        "PUERTA CORREDIZA LINEA CLASSIC SISTEMA 8025",
        "PUERTA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO",
        "PUERTA CORREDIZA LINEA CLASSIC PRIMAVERA LUCCA",
        "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA SERIE SG 3",
        "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO",
        "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA SG 7038",
        "PUERTA CORREDIZA LINEA TRADICIONAL SISTEMA SG 8025",
        "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 5020",
        "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 744",
        "VENTANA CORREDIZA LINEA TRADICIONAL SISTEMA SG 8025"
    ];

    private static readonly string[] ConfirmedSignatureSystemNames =
    [
        "PUERTA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9052",
        "CUERPO FIJO LINEA PREMIUM EUROPEO SISTEMA LSA 0932",
        "CUERPO PROYECTANTE LINEA PREMIUM EUROPEO SISTEMA LSA 0932",
        "VENTANA CORREDIZA LINEA PREMIUM EUROPEO SISTEMA LSA 9060",
        "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
        "CUERPO DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
        "CUERPO PROYECTANTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
        "CUERPO PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA PIEGA",
        "PUERTA BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
        "PUERTA DOBLE BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
        "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO",
        "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO TIPO POKET",
        "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100 TIPO POKET",
        "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
        "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET",
        "PUERTA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA PIEGA",
        "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO",
        "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA",
        "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
        "VENTANA PLEGABLE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA PIEGA"
    ];

    private sealed record SystemSeed(
        int Sequence,
        string Code,
        string TechnicalName,
        string? CommercialDisplayName = null)
    {
        public string DisplayName => CommercialDisplayName ?? TechnicalName;
    }

    private static string? FunctionalType(string name)
    {
        var value = NormalizeSystemText(name);
        if (value == "N.A") return null;
        if (value.Contains("DIVISIONES DE BANO")) return "SHOWER_DIVISION";
        if (value.Contains("SISTEMA REJILLA")) return "GRILLE";
        if (value.Contains("PERGOLA")) return "PERGOLA";
        if (value.Contains("CLARABOYA")) return "SKYLIGHT";
        if (value.Contains("PLEGABLE") && value.Contains("PUERTA")) return "FOLDING_DOOR";
        if (value.Contains("PLEGABLE")) return "FOLDING_WINDOW";
        if (value.Contains("PUERTA CORREDIZA") || value.Contains("DESLIZANTE")) return "SLIDING_DOOR";
        if (value.Contains("VENTANA CORREDIZA")) return "SLIDING_WINDOW";
        if (value.Contains("DOBLE BATIENTE") && value.Contains("CUERPO")) return "DOUBLE_CASEMENT";
        if (value.Contains("CUERPO BATIENTE")) return "CASEMENT";
        if (value.Contains("PUERTA BATIENTE") || value.Contains("PUERTA DOBLE BATIENTE")) return "SWING_DOOR";
        if (value.Contains("PROYECTANTE")) return "PROJECTING";
        if (value.Contains("FIJO") || value.Contains("FACHADA")) return "FIXED";
        return null;
    }

    private static string? Family(string name)
    {
        var value = NormalizeSystemText(name);
        if (value.Contains("VENECIA SERIE 40")) return "VENECIA FERMO";
        if (value.Contains("VENECIA SERIE 50")) return "VENECIA MONZA";
        if (value.Contains("VENECIA SERIE 55")) return "VENECIA PIEGA";
        if (value.Contains("VENECIA SERIE 70")) return "VENECIA NAPOLES";
        if (value.Contains("VENECIA SERIE 100")) return "VENECIA MONACO";
        if (value.Contains("PRIMAVERA") && value.Contains("SG 4")) return "PRIMAVERA SIENA";
        if (value.Contains("PRIMAVERA") && value.Contains("SG 5")) return "PRIMAVERA LAGO";
        if (value.Contains("PRIMAVERA") && value.Contains("SG 8")) return "PRIMAVERA LUCCA";
        if (value.Contains("3890")) return "SG 3890";
        if (value.Contains("3831")) return "SG 3831";
        if (value.Contains("8025")) return "SG 8025";
        if (value.Contains("7038")) return "SG 7038";
        if (value.Contains("744")) return "SG 744";
        if (value.Contains("5020")) return "SG 5020";
        if (value.Contains("SG45")) return "SG45";
        if (value.Contains("SG101")) return "SG101";
        if (value.Contains("SG103")) return "SG103";
        if (value.Contains("PERGOLA")) return "PERGOLA";
        if (value.Contains("REJILLA")) return "REJILLA";
        if (value.Contains("CLARABOYA")) return "CLARABOYA";
        if (value.Contains("TWIN DN")) return "TWIN DN";
        if (value.Contains("TAURO")) return "TAURO";
        if (value.Contains("SIGMA")) return "SIGMA";
        if (value.Contains("BARANDA")) return "BARANDA";
        return null;
    }

    private static string? CommercialName(string name) => Family(name);

    private static string? Series(string name)
    {
        var value = NormalizeSystemText(name);
        foreach (var token in new[] { "SERIE 100", "SERIE 70", "SERIE 55", "SERIE 50", "SERIE 40", "SG 3890", "SG 3831", "SG 8025", "SG 7038", "SG 5020", "SG 744", "SG 4", "SG 5", "SG 8", "SG45", "SG101", "SG103" })
        {
            if (value.Contains(token)) return token;
        }
        return null;
    }

    private static string? CommercialLine(string name)
    {
        var value = NormalizeSystemText(name);
        if (ContainsSystemName(ConfirmedClassicSystemNames, value)) return "CLASSIC";
        if (ContainsSystemName(ConfirmedSignatureSystemNames, value)) return "SIGNATURE";
        if (value.Contains("CLASSIC")) return "CLASSIC";
        if (value.Contains("PREMIUM")) return "PREMIUM";
        if (value.Contains("TRADICIONAL")) return "TRADITIONAL";
        if (value.Contains("STICK")) return "STICK";
        if (value.StartsWith("SISTEMA") || value.Contains("DIVISIONES DE BANO") || value.Contains("BARANDA")) return "SPECIAL";
        return null;
    }

    private static bool ContainsSystemName(
        IEnumerable<string> systemNames,
        string normalizedName) =>
        systemNames.Any(name =>
            NormalizeSystemText(name) == normalizedName);

    private static string? Variant(string name)
    {
        var value = NormalizeSystemText(name);
        if (value.Contains("POKET") || value.Contains("POCKET")) return "POCKET";
        if (value.Contains("INOX")) return "INOX";
        if (value.Contains("PIEL DE VIDRIO")) return "PIEL_DE_VIDRIO";
        if (value.Contains("TAPA Y PISAVIDRIO")) return "TAPA_PISAVIDRIO";
        if (value.Contains("TUBO SUPERIOR")) return "TUBO_SUPERIOR";
        return "STANDARD";
    }

    private static bool IsSelectableSystem(string name) => NormalizeSystemText(name) != "N.A";

    private static bool RequiresSystemReview(string name)
    {
        var value = NormalizeSystemText(name);
        return value == "N.A"
            || value.Contains("BARANDA")
            || value.Contains("FACHADA")
            || value.Contains("TWIN DN")
            || value.Contains("TAURO")
            || value.Contains("SIGMA");
    }

    private static bool IsPriceable(string name)
    {
        var value = NormalizeSystemText(name);
        return IsSelectableSystem(name)
            && !value.Contains("BARANDA")
            && !value.Contains("FACHADA")
            && !value.StartsWith("SISTEMA")
            && !value.Contains("DIVISIONES DE BANO");
    }

    private static string NormalizeSystemText(string value) =>
        CatalogAliasNormalizer.Normalize(value);
    private static object Seed(
        int sequence,
        string code,
        string name,
        bool activeForRecognition,
        bool priceable,
        bool futurePriceable,
        bool requiresReview,
        string? technicalName = null,
        string? commercialName = null,
        string? functionalType = null,
        string? family = null,
        string? series = null,
        string? commercialLine = null,
        string? variant = null,
        bool isSelectable = false) => new
    {
        Id = Guid.Parse($"30000000-0000-0000-0000-{sequence:000000000000}"),
        Code = code,
        Name = name,
        ActiveForRecognition = activeForRecognition,
        Priceable = priceable,
        FuturePriceable = futurePriceable,
        RequiresReview = requiresReview,
        IsActive = true,
        CreatedAtUtc = SeededAtUtc,
        UpdatedAtUtc = (DateTimeOffset?)null,
        TechnicalName = technicalName,
        CommercialName = commercialName,
        FunctionalType = functionalType,
        Family = family,
        Series = series,
        CommercialLine = commercialLine,
        Variant = variant,
        IsSelectable = isSelectable
    };
}

public sealed class ProductSystemConstraintConfiguration
    : IEntityTypeConfiguration<ProductSystemConstraint>
{
    public void Configure(EntityTypeBuilder<ProductSystemConstraint> builder)
    {
        builder.ToTable("product_system_constraints", "core", table =>
        {
            table.HasCheckConstraint(
                "ck_product_system_constraints_values",
                "\"min_value\" IS NULL OR \"max_value\" IS NULL OR \"min_value\" <= \"max_value\"");
            table.HasCheckConstraint(
                "ck_product_system_constraints_hard_verified",
                "\"severity\" <> 'Hard' OR \"knowledge_class\" = 'VerifiedTechnical'");
            table.HasCheckConstraint(
                "ck_product_system_constraints_effective_range",
                "\"effective_from_utc\" IS NULL OR \"effective_to_utc\" IS NULL OR \"effective_from_utc\" <= \"effective_to_utc\"");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.ProductSystemId).HasColumnName("product_system_id").IsRequired();
        builder.Property(value => value.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        builder.Property(value => value.ConstraintType).HasColumnName("constraint_type")
            .HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(value => value.Scope).HasColumnName("scope")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.EvaluationStage).HasColumnName("evaluation_stage")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.Severity).HasColumnName("severity")
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(value => value.KnowledgeClass).HasColumnName("knowledge_class")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.MinValue).HasColumnName("min_value")
            .HasPrecision(18, 6);
        builder.Property(value => value.MaxValue).HasColumnName("max_value")
            .HasPrecision(18, 6);
        builder.Property(value => value.TextValue).HasColumnName("text_value")
            .HasMaxLength(100);
        builder.Property(value => value.AllowedValues).HasColumnName("allowed_values");
        builder.Property(value => value.Unit).HasColumnName("unit").HasMaxLength(20);
        builder.Property(value => value.RequiresReviewWhenUnknown)
            .HasColumnName("requires_review_when_unknown").IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(value => value.EffectiveFromUtc).HasColumnName("effective_from_utc")
            .HasColumnType("timestamp with time zone");
        builder.Property(value => value.EffectiveToUtc).HasColumnName("effective_to_utc")
            .HasColumnType("timestamp with time zone");
        builder.Property(value => value.SourceType).HasColumnName("source_type")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.SourceReference).HasColumnName("source_reference")
            .HasMaxLength(200);
        builder.Property(value => value.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(value => new { value.ProductSystemId, value.Code })
            .IsUnique()
            .HasDatabaseName("ux_product_system_constraints_system_code");
        builder.HasIndex(value => new
            {
                value.ProductSystemId,
                value.IsActive,
                value.EvaluationStage
            })
            .HasDatabaseName("ix_product_system_constraints_system_active_stage");
    }
}

public sealed class FrameTypeConfiguration : IEntityTypeConfiguration<FrameType>
{
    public void Configure(EntityTypeBuilder<FrameType> builder)
    {
        builder.ToTable("frame_types", "core");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.Code).IsUnique().HasDatabaseName("ux_frame_types_code");

        builder.HasData(
            Seed(1, "MARCO_47", "Marco 47 mm"),
            Seed(2, "MARCO_58", "Marco 58 mm"));
    }

    private static object Seed(int sequence, string code, string name) => new
    {
        Id = Guid.Parse($"40000000-0000-0000-0000-{sequence:000000000000}"),
        Code = code,
        Name = name,
        IsActive = true,
        CreatedAtUtc = ProductSystemConfiguration.SeededAtUtc,
        UpdatedAtUtc = (DateTimeOffset?)null
    };
}

public sealed class FinishTypeConfiguration : IEntityTypeConfiguration<FinishType>
{
    public void Configure(EntityTypeBuilder<FinishType> builder)
    {
        builder.ToTable("finish_types", "core");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(value => value.NormalizedType).HasColumnName("normalized_type").HasMaxLength(60);
        builder.Property(value => value.Color).HasColumnName("color").HasMaxLength(60);
        builder.Property(value => value.Texture).HasColumnName("texture").HasMaxLength(60);
        builder.Property(value => value.Process).HasColumnName("process").HasMaxLength(60);
        builder.Property(value => value.CommercialCode).HasColumnName("commercial_code").HasMaxLength(40);
        builder.Property(value => value.Material).HasColumnName("material").HasMaxLength(60);
        builder.Property(value => value.IsSelectable).HasColumnName("is_selectable").HasDefaultValue(true).IsRequired();
        builder.Property(value => value.RequiresReview).HasColumnName("requires_review").IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.Code).IsUnique().HasDatabaseName("ux_finish_types_code");
        builder.HasIndex(value => value.Name).IsUnique().HasDatabaseName("ux_finish_types_name");

        builder.HasData(
            Seed(1, "STANDARD_NATURAL", "Acabado natural estandar", false, isSelectable: false),
            Seed(2, "ANODIZED_GRAY", "Anodizado gris", false, isSelectable: false),
            Seed(3, "BLACK_MATTE", "ALUCOLOR POLIESTER NEGRO MATE PP13", false,
                normalizedType: "PAINTED", color: "BLACK", texture: "MATTE",
                process: "POLYESTER", commercialCode: "PP13",
                material: "ALUMINUM"),
            Seed(4, "SPECIAL", "Acabado especial", true, isSelectable: false),
            Seed(5, "UNKNOWN", "Acabado por confirmar", true, isSelectable: false),
            Seed(6, "FINISH_PP003", "ALUCOLOR POLIESTER BLANCO PP003", false,
                normalizedType: "PAINTED", color: "WHITE",
                process: "POLYESTER", commercialCode: "PP003",
                material: "ALUMINUM"),
            Seed(7, "FINISH_GRAY_POLYESTER", "ALUCOLOR POLIESTER PINTURA GRIS", false,
                normalizedType: "PAINTED", color: "GRAY",
                process: "POLYESTER", material: "ALUMINUM"),
            Seed(8, "FINISH_CHAMPAGNE_POLY", "ALUCOLOR POLIESTER PINTURA CHAMPAÑA", false,
                normalizedType: "PAINTED", color: "CHAMPAGNE",
                process: "POLYESTER", material: "ALUMINUM"),
            Seed(9, "FINISH_AN001", "ANODIZADO BLANCO MATE AN001", false,
                normalizedType: "ANODIZED", color: "WHITE",
                texture: "MATTE", commercialCode: "AN001",
                material: "ALUMINUM"),
            Seed(10, "FINISH_INOX", "INOX", false,
                normalizedType: "STAINLESS_STEEL",
                material: "STAINLESS_STEEL"),
            Seed(11, "FINISH_NA", "N.A", true,
                normalizedType: "NOT_APPLICABLE", isSelectable: true));
    }

    private static object Seed(
        int sequence,
        string code,
        string name,
        bool requiresReview,
        string? normalizedType = null,
        string? color = null,
        string? texture = null,
        string? process = null,
        string? commercialCode = null,
        string? material = null,
        bool isSelectable = true) => new
    {
        Id = Guid.Parse($"50000000-0000-0000-0000-{sequence:000000000000}"),
        Code = code,
        Name = name,
        NormalizedType = normalizedType,
        Color = color,
        Texture = texture,
        Process = process,
        CommercialCode = commercialCode,
        Material = material,
        IsSelectable = isSelectable,
        RequiresReview = requiresReview,
        IsActive = true,
        CreatedAtUtc = ProductSystemConfiguration.SeededAtUtc,
        UpdatedAtUtc = (DateTimeOffset?)null
    };
}

public sealed class CatalogAliasConfiguration
    : IEntityTypeConfiguration<CatalogAlias>
{
    public void Configure(EntityTypeBuilder<CatalogAlias> builder)
    {
        builder.ToTable("catalog_aliases", "core", table =>
        {
            table.HasCheckConstraint("ck_catalog_aliases_confidence", "\"confidence\" >= 0 AND \"confidence\" <= 1");
            table.HasCheckConstraint("ck_catalog_aliases_non_numeric", "\"normalized_alias\" !~ '^[0-9]+$'");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.Category).HasColumnName("category")
            .HasConversion(
                value => value == CatalogAliasCategory.System
                    ? "SYSTEM"
                    : value == CatalogAliasCategory.Frame
                        ? "FRAME"
                        : "FINISH",
                value => value == "SYSTEM"
                    ? CatalogAliasCategory.System
                    : value == "FRAME"
                        ? CatalogAliasCategory.Frame
                        : CatalogAliasCategory.Finish)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(value => value.Alias).HasColumnName("alias").HasMaxLength(200).IsRequired();
        builder.Property(value => value.NormalizedAlias).HasColumnName("normalized_alias").HasMaxLength(200).IsRequired();
        builder.Property(value => value.CanonicalCode).HasColumnName("canonical_code").HasMaxLength(30).IsRequired();
        builder.Property(value => value.MatchPolicy).HasColumnName("match_policy")
            .HasConversion(
                value => value == CatalogAliasMatchPolicy.ExactNormalized
                    ? "EXACT_NORMALIZED"
                    : "TECHNICAL_PHRASE",
                value => value == "EXACT_NORMALIZED"
                    ? CatalogAliasMatchPolicy.ExactNormalized
                    : CatalogAliasMatchPolicy.TechnicalPhrase)
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(value => value.RequiresContext).HasColumnName("requires_context").IsRequired();
        builder.Property(value => value.Confidence).HasColumnName("confidence").HasColumnType("numeric(5,4)").HasPrecision(5, 4).IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => new { value.Category, value.NormalizedAlias })
            .IsUnique()
            .HasDatabaseName("ux_catalog_aliases_category_normalized_alias");
        builder.HasIndex(value => new { value.Category, value.CanonicalCode })
            .HasDatabaseName("ix_catalog_aliases_category_canonical_code");

        builder.HasData(
            Alias(1, CatalogAliasCategory.System, "VENECIA SERIE 40", "K40", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(2, CatalogAliasCategory.System, "VENECIA_SERIE_40", "K40", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(3, CatalogAliasCategory.System, "SERIE 40", "K40", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(4, CatalogAliasCategory.System, "VENECIA SERIE 50", "K50", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(5, CatalogAliasCategory.System, "VENECIA_SERIE_50", "K50", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(6, CatalogAliasCategory.System, "SERIE 50", "K50", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(7, CatalogAliasCategory.System, "VENECIA SERIE 70", "K70", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(8, CatalogAliasCategory.System, "VENECIA_SERIE_70", "K70", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(9, CatalogAliasCategory.System, "SERIE 70", "K70", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(10, CatalogAliasCategory.Frame, "SG0047", "MARCO_47", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(11, CatalogAliasCategory.Frame, "MARCO SG0047", "MARCO_47", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(12, CatalogAliasCategory.Frame, "SG0058", "MARCO_58", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(13, CatalogAliasCategory.Frame, "MARCO SG0058", "MARCO_58", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(14, CatalogAliasCategory.Finish, "NEGRO MATE", "BLACK_MATTE", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(15, CatalogAliasCategory.Finish, "ALUCOLOR POLIESTER NEGRO MATE", "BLACK_MATTE", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(16, CatalogAliasCategory.Finish, "NEGRO PINTURA AL HORNO", "BLACK_MATTE", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(17, CatalogAliasCategory.Finish, "PP13", "BLACK_MATTE", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(18, CatalogAliasCategory.Finish, "BLANCO", "FINISH_PP003", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(19, CatalogAliasCategory.Finish, "PP003", "FINISH_PP003", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(20, CatalogAliasCategory.Finish, "GRIS", "FINISH_GRAY_POLYESTER", CatalogAliasMatchPolicy.TechnicalPhrase, true),
            Alias(21, CatalogAliasCategory.Finish, "CHAMPAÑA", "FINISH_CHAMPAGNE_POLY", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(22, CatalogAliasCategory.Finish, "CHAMPAGNE", "FINISH_CHAMPAGNE_POLY", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(23, CatalogAliasCategory.Finish, "ANODIZADO BLANCO", "FINISH_AN001", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(24, CatalogAliasCategory.Finish, "AN001", "FINISH_AN001", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(25, CatalogAliasCategory.Finish, "INOX", "FINISH_INOX", CatalogAliasMatchPolicy.ExactNormalized, false),
            Alias(26, CatalogAliasCategory.Finish, "ACERO INOXIDABLE", "FINISH_INOX", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(27, CatalogAliasCategory.Finish, "STAINLESS STEEL", "FINISH_INOX", CatalogAliasMatchPolicy.TechnicalPhrase, false),
            Alias(28, CatalogAliasCategory.Finish, "N.A", "FINISH_NA", CatalogAliasMatchPolicy.ExactNormalized, false));
    }

    private static object Alias(
        int sequence,
        CatalogAliasCategory category,
        string alias,
        string canonicalCode,
        CatalogAliasMatchPolicy matchPolicy,
        bool requiresContext) => new
    {
        Id = Guid.Parse($"60000000-0000-0000-0000-{sequence:000000000000}"),
        Category = category,
        Alias = alias,
        NormalizedAlias = CatalogAliasNormalizer.Normalize(alias),
        CanonicalCode = canonicalCode,
        MatchPolicy = matchPolicy,
        RequiresContext = requiresContext,
        Confidence = 1.0m,
        IsActive = true,
        CreatedAtUtc = ProductSystemConfiguration.SeededAtUtc,
        UpdatedAtUtc = (DateTimeOffset?)null
    };
}
