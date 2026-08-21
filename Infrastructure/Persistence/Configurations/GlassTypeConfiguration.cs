using Domain.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class GlassTypeConfiguration
    : IEntityTypeConfiguration<GlassType>
{
    internal static readonly Guid Tempered5Id =
        Guid.Parse("10000000-0000-0000-0000-000000000005");
    internal static readonly Guid Tempered6Id =
        Guid.Parse("10000000-0000-0000-0000-000000000006");
    internal static readonly Guid Tempered8Id =
        Guid.Parse("10000000-0000-0000-0000-000000000007");
    internal static readonly Guid Tempered10Id =
        Guid.Parse("10000000-0000-0000-0000-000000000008");
    internal static readonly Guid Laminated44Id =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid Laminated44GrayId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");
    internal static readonly Guid Laminated55Id =
        Guid.Parse("10000000-0000-0000-0000-000000000003");
    internal static readonly Guid Laminated55GrayId =
        Guid.Parse("10000000-0000-0000-0000-000000000004");
    internal static readonly Guid UnknownGlassId =
        Guid.Parse("10000000-0000-0000-0000-000000000009");
    internal static readonly Guid Tempered4Id =
        Guid.Parse("10000000-0000-0000-0000-00000000000a");
    internal static readonly Guid Raw4Id =
        Guid.Parse("10000000-0000-0000-0000-00000000000b");
    internal static readonly Guid Raw4MiniBorealId =
        Guid.Parse("10000000-0000-0000-0000-00000000000c");
    internal static readonly Guid Raw5Id =
        Guid.Parse("10000000-0000-0000-0000-00000000000d");
    internal static readonly Guid Raw6Id =
        Guid.Parse("10000000-0000-0000-0000-00000000000e");
    internal static readonly Guid LaminatedRaw4Pvb0386Id =
        Guid.Parse("10000000-0000-0000-0000-00000000000f");
    internal static readonly Guid LaminatedRaw4Pvb0766Id =
        Guid.Parse("10000000-0000-0000-0000-000000000010");
    internal static readonly Guid LaminatedRaw4Pvb1146Id =
        Guid.Parse("10000000-0000-0000-0000-000000000011");
    internal static readonly Guid LaminatedRaw6Pvb076Acoustic8Id =
        Guid.Parse("10000000-0000-0000-0000-000000000012");
    internal static readonly Guid LaminatedTempered5Pvb1145Id =
        Guid.Parse("10000000-0000-0000-0000-000000000013");
    internal static readonly Guid LaminatedTempered6Pvb1526Id =
        Guid.Parse("10000000-0000-0000-0000-000000000014");
    internal static readonly Guid IguTempered5Chamber12Tempered6Id =
        Guid.Parse("10000000-0000-0000-0000-000000000015");
    internal static readonly Guid QualityGlassPremiumCl120Id =
        Guid.Parse("10000000-0000-0000-0000-000000000016");
    internal static readonly Guid QualityGlassPremiumCl150Id =
        Guid.Parse("10000000-0000-0000-0000-000000000017");
    internal static readonly Guid QualityGlassPremiumCl167Id =
        Guid.Parse("10000000-0000-0000-0000-000000000018");
    internal static readonly Guid QualityGlassClassicBlueId =
        Guid.Parse("10000000-0000-0000-0000-000000000019");
    internal static readonly Guid QualityGlassClassicBronzeId =
        Guid.Parse("10000000-0000-0000-0000-00000000001a");
    internal static readonly Guid QualityGlassClassicGreenId =
        Guid.Parse("10000000-0000-0000-0000-00000000001b");
    internal static readonly Guid NotApplicableGlassId =
        Guid.Parse("10000000-0000-0000-0000-00000000001c");
    internal static readonly DateTimeOffset SeededAtUtc =
        new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<GlassType> builder)
    {
        builder.ToTable("glass_types", "core");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(value => value.Code)
            .HasColumnName("code")
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(value => value.Name)
            .HasColumnName("name")
            .HasMaxLength(GlassType.NameMaximumLength)
            .IsRequired();
        builder.Property(value => value.Description)
            .HasColumnName("description")
            .HasMaxLength(500);
        builder.Property(value => value.Family)
            .HasColumnName("family")
            .HasMaxLength(40);
        builder.Property(value => value.Composition)
            .HasColumnName("composition")
            .HasMaxLength(40);
        builder.Property(value => value.Treatment)
            .HasColumnName("treatment")
            .HasMaxLength(40);
        builder.Property(value => value.OuterThicknessMm)
            .HasColumnName("outer_thickness_mm")
            .HasColumnType("numeric(8,3)")
            .HasPrecision(8, 3);
        builder.Property(value => value.InnerThicknessMm)
            .HasColumnName("inner_thickness_mm")
            .HasColumnType("numeric(8,3)")
            .HasPrecision(8, 3);
        builder.Property(value => value.PvbThicknessMm)
            .HasColumnName("pvb_thickness_mm")
            .HasColumnType("numeric(8,3)")
            .HasPrecision(8, 3);
        builder.Property(value => value.PvbType)
            .HasColumnName("pvb_type")
            .HasMaxLength(40);
        builder.Property(value => value.PvbColor)
            .HasColumnName("pvb_color")
            .HasMaxLength(40);
        builder.Property(value => value.ChamberThicknessMm)
            .HasColumnName("chamber_thickness_mm")
            .HasColumnType("numeric(8,3)")
            .HasPrecision(8, 3);
        builder.Property(value => value.ProductLine)
            .HasColumnName("product_line")
            .HasMaxLength(80);
        builder.Property(value => value.ProductToken)
            .HasColumnName("product_token")
            .HasMaxLength(40);
        builder.Property(value => value.Pattern)
            .HasColumnName("pattern")
            .HasMaxLength(80);
        builder.Property(value => value.Color)
            .HasColumnName("color")
            .HasMaxLength(40);
        builder.Property(value => value.IsSelectable)
            .HasColumnName("is_selectable")
            .HasDefaultValue(true)
            .IsRequired();
        builder.Property(value => value.RequiresReview)
            .HasColumnName("requires_review")
            .IsRequired();
        builder.Property(value => value.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(value => value.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(value => value.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.Code)
            .IsUnique()
            .HasDatabaseName("ux_glass_types_code");
        builder.HasIndex(value => value.Name)
            .IsUnique()
            .HasDatabaseName("ux_glass_types_name");
        builder.HasMany(value => value.PriceRangeVersions)
            .WithOne(value => value.GlassType)
            .HasForeignKey(value => value.GlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            Seed(Tempered5Id, "TEMP_5", "COMPOSICION MONOLITICO TEMPLADO 5 MM INC", family: "MONOLITHIC", composition: "TEMPERED", outerThicknessMm: 5m, color: "INC"),
            Seed(Tempered6Id, "TEMP_6", "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", family: "MONOLITHIC", composition: "TEMPERED", outerThicknessMm: 6m, color: "INC"),
            Seed(Tempered8Id, "TEMP_8", "COMPOSICION MONOLITICO TEMPLADO 8 MM INC", family: "MONOLITHIC", composition: "TEMPERED", outerThicknessMm: 8m, color: "INC"),
            Seed(Tempered10Id, "TEMP_10", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC", family: "MONOLITHIC", composition: "TEMPERED", outerThicknessMm: 10m, color: "INC"),
            Seed(Laminated44Id, "LAM_4_4", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC"),
            Seed(Laminated44GrayId, "LAM_4_4_GRAY", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM GRIS + 4 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "GRIS"),
            Seed(Laminated55Id, "LAM_5_5", "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM INC + 5 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 5m, innerThicknessMm: 5m, pvbThicknessMm: 0.38m, pvbColor: "INC"),
            Seed(Laminated55GrayId, "LAM_5_5_GRAY", "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM GRIS + 5 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 5m, innerThicknessMm: 5m, pvbThicknessMm: 0.38m, pvbColor: "GRIS"),
            Seed(UnknownGlassId, "UNKNOWN_GLASS", "Tipo de vidrio por confirmar", isSelectable: false, requiresReview: true),
            Seed(Tempered4Id, "TEMP_4", "COMPOSICION MONOLITICO TEMPLADO 4 MM INC", family: "MONOLITHIC", composition: "TEMPERED", outerThicknessMm: 4m, color: "INC"),
            Seed(Raw4Id, "RAW_4_INC", "COMPOSICION MONOLITICO CRUDO 4 MM INC", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 4m, color: "INC"),
            Seed(Raw4MiniBorealId, "RAW_4_MINI_BOREAL", "COMPOSICION MONOLITICO CRUDO 4 MM MINI BOREAL", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 4m, pattern: "MINI_BOREAL"),
            Seed(Raw5Id, "RAW_5_INC", "COMPOSICION MONOLITICO CRUDO 5 MM INC", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 5m, color: "INC"),
            Seed(Raw6Id, "RAW_6_INC", "COMPOSICION MONOLITICO CRUDO 6 MM INC", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 6m, color: "INC"),
            Seed(LaminatedRaw4Pvb0386Id, "LAM_4_038_6_INC", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 6 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 6m, pvbThicknessMm: 0.38m, pvbColor: "INC"),
            Seed(LaminatedRaw4Pvb0766Id, "LAM_4_076_6_INC", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,76 MM INC + 6 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 6m, pvbThicknessMm: 0.76m, pvbColor: "INC"),
            Seed(LaminatedRaw4Pvb1146Id, "LAM_4_114_6_INC", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 1,14 MM INC + 6 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 6m, pvbThicknessMm: 1.14m, pvbColor: "INC"),
            Seed(LaminatedRaw6Pvb076Acoustic8Id, "LAM_6_076_AC_8_INC", "COMPOSICION LAMINADO CRUDO 6 MM INC + PVB 0,76 MM ACÚSTICO + 8 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 6m, innerThicknessMm: 8m, pvbThicknessMm: 0.76m, pvbType: "ACOUSTIC", pvbColor: "INC"),
            Seed(LaminatedTempered5Pvb1145Id, "LAMT_5_114_5_INC", "COMPOSICION LAMINADO TEMPLADO 5 MM INC + PVB 1,14 MM INC + 5 MM INC", family: "LAMINATED", composition: "TEMPERED", outerThicknessMm: 5m, innerThicknessMm: 5m, pvbThicknessMm: 1.14m, pvbColor: "INC"),
            Seed(LaminatedTempered6Pvb1526Id, "LAMT_6_152_6_INC", "COMPOSICION LAMINADO TEMPLADO 6 MM INC + PVB 1,52 MM INC + 6 MM INC", family: "LAMINATED", composition: "TEMPERED", outerThicknessMm: 6m, innerThicknessMm: 6m, pvbThicknessMm: 1.52m, pvbColor: "INC"),
            Seed(IguTempered5Chamber12Tempered6Id, "IGU_T5_CAM12_T6", "COMPOSICION TEMPLADO 5 MM INC + CÁMARA 12 MM + TEMPLADO 6 MM INC", family: "IGU", composition: "TEMPERED", outerThicknessMm: 5m, innerThicknessMm: 6m, chamberThicknessMm: 12m, color: "INC"),
            Seed(QualityGlassPremiumCl120Id, "QG_PREMIUM_CL120", "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL120", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_PREMIUM", productToken: "CL120"),
            Seed(QualityGlassPremiumCl150Id, "QG_PREMIUM_CL150", "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL150", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_PREMIUM", productToken: "CL150"),
            Seed(QualityGlassPremiumCl167Id, "QG_PREMIUM_CL167", "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL167", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_PREMIUM", productToken: "CL167"),
            Seed(QualityGlassClassicBlueId, "QG_CLASSIC_BLUE", "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BLUE", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_CLASSIC", productToken: "BLUE", color: "BLUE"),
            Seed(QualityGlassClassicBronzeId, "QG_CLASSIC_BRONZE", "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BRONZE", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_CLASSIC", productToken: "BRONZE", color: "BRONZE"),
            Seed(QualityGlassClassicGreenId, "QG_CLASSIC_GREEN", "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM GREEN", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_CLASSIC", productToken: "GREEN", color: "GREEN"),
            Seed(NotApplicableGlassId, "GLASS_NA", "N.A.", family: "NOT_APPLICABLE", isSelectable: true, requiresReview: true));
    }

    private static object Seed(
        Guid id,
        string code,
        string name,
        string? family = null,
        string? composition = null,
        string? treatment = null,
        decimal? outerThicknessMm = null,
        decimal? innerThicknessMm = null,
        decimal? pvbThicknessMm = null,
        string? pvbType = null,
        string? pvbColor = null,
        decimal? chamberThicknessMm = null,
        string? productLine = null,
        string? productToken = null,
        string? pattern = null,
        string? color = null,
        bool isSelectable = true,
        bool requiresReview = false) => new
    {
        Id = id,
        Code = code,
        Name = name,
        Description = (string?)null,
        Family = family,
        Composition = composition,
        Treatment = treatment,
        OuterThicknessMm = outerThicknessMm,
        InnerThicknessMm = innerThicknessMm,
        PvbThicknessMm = pvbThicknessMm,
        PvbType = pvbType,
        PvbColor = pvbColor,
        ChamberThicknessMm = chamberThicknessMm,
        ProductLine = productLine,
        ProductToken = productToken,
        Pattern = pattern,
        Color = color,
        IsSelectable = isSelectable,
        RequiresReview = requiresReview,
        IsActive = true,
        CreatedAtUtc = SeededAtUtc,
        UpdatedAtUtc = (DateTimeOffset?)null
    };
}

public sealed class GlassPriceRangeVersionConfiguration
    : IEntityTypeConfiguration<GlassPriceRangeVersion>
{
    public void Configure(EntityTypeBuilder<GlassPriceRangeVersion> builder)
    {
        builder.ToTable("glass_price_range_versions", "core", table =>
        {
            table.HasCheckConstraint(
                "ck_glass_price_range_versions_version",
                "\"version\" > 0");
            table.HasCheckConstraint(
                "ck_glass_price_range_versions_minimum_price",
                "\"minimum_price_per_square_meter\" > 0");
            table.HasCheckConstraint(
                "ck_glass_price_range_versions_maximum_price",
                "\"maximum_price_per_square_meter\" >= \"minimum_price_per_square_meter\"");
            table.HasCheckConstraint(
                "ck_glass_price_range_versions_expected_price",
                "\"expected_amount_per_m2\" >= \"minimum_price_per_square_meter\" AND \"expected_amount_per_m2\" <= \"maximum_price_per_square_meter\"");
            table.HasCheckConstraint(
                "ck_glass_price_range_versions_validity",
                "\"valid_to_utc\" IS NULL OR \"valid_to_utc\" > \"valid_from_utc\"");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(value => value.GlassTypeId)
            .HasColumnName("glass_type_id")
            .IsRequired();
        builder.Property(value => value.Version)
            .HasColumnName("version")
            .IsRequired();
        builder.Property(value => value.MinimumPricePerSquareMeter)
            .HasColumnName("minimum_price_per_square_meter")
            .HasColumnType("numeric(18,2)")
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(value => value.ExpectedAmountPerM2)
            .HasColumnName("expected_amount_per_m2")
            .HasColumnType("numeric(18,2)")
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(value => value.MaximumPricePerSquareMeter)
            .HasColumnName("maximum_price_per_square_meter")
            .HasColumnType("numeric(18,2)")
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(value => value.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();
        builder.Property(value => value.Status)
            .HasColumnName("status")
            .HasConversion(
                value => value == GlassPriceRangeStatus.Preliminary
                    ? "PRELIMINARY"
                    : value == GlassPriceRangeStatus.Active
                        ? "ACTIVE"
                        : "RETIRED",
                value => value == "PRELIMINARY"
                    ? GlassPriceRangeStatus.Preliminary
                    : value == "ACTIVE"
                        ? GlassPriceRangeStatus.Active
                        : GlassPriceRangeStatus.Retired)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(value => value.ValidFromUtc)
            .HasColumnName("valid_from_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(value => value.ValidToUtc)
            .HasColumnName("valid_to_utc")
            .HasColumnType("timestamp with time zone");
        builder.Property(value => value.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.GlassTypeId,
                value.ValidToUtc
            })
            .HasDatabaseName(
                "ix_glass_price_range_versions_glass_type_id_valid_to_utc");
        builder.HasIndex(value => new { value.GlassTypeId, value.Version })
            .IsUnique()
            .HasDatabaseName("ux_glass_price_range_versions_type_version");
        builder.HasIndex(value => value.GlassTypeId)
            .IsUnique()
            .HasFilter("\"valid_to_utc\" IS NULL")
            .HasDatabaseName("ux_glass_price_range_versions_open_type");

        builder.HasData(
            Seed("20000000-0000-0000-0000-000000000005", GlassTypeConfiguration.Tempered5Id, 74000m, 74000m, 74000m),
            Seed("20000000-0000-0000-0000-000000000006", GlassTypeConfiguration.Tempered6Id, 86000m, 86000m, 86000m),
            Seed("20000000-0000-0000-0000-000000000007", GlassTypeConfiguration.Tempered8Id, 90000m, 90000m, 90000m),
            Seed("20000000-0000-0000-0000-000000000008", GlassTypeConfiguration.Tempered10Id, 126000m, 126000m, 126000m),
            Seed("20000000-0000-0000-0000-000000000001", GlassTypeConfiguration.Laminated44Id, 90000m, 100000m, 110000m),
            RetiredSeed("20000000-0000-0000-0000-000000000002", GlassTypeConfiguration.Laminated44GrayId, 95000m, 95000m, 95000m),
            Seed("20000000-0000-0000-0000-000000000003", GlassTypeConfiguration.Laminated55Id, 120000m, 130000m, 140000m),
            RetiredSeed("20000000-0000-0000-0000-000000000004", GlassTypeConfiguration.Laminated55GrayId, 125000m, 135000m, 145000m));
    }

    private static object Seed(
        string id,
        Guid glassTypeId,
        decimal minimum,
        decimal expected,
        decimal maximum) => new
    {
        Id = Guid.Parse(id),
        GlassTypeId = glassTypeId,
        Version = 1,
        MinimumPricePerSquareMeter = minimum,
        ExpectedAmountPerM2 = expected,
        MaximumPricePerSquareMeter = maximum,
        Currency = "COP",
        Status = GlassPriceRangeStatus.Preliminary,
        ValidFromUtc = GlassTypeConfiguration.SeededAtUtc,
        ValidToUtc = (DateTimeOffset?)null,
        CreatedAtUtc = GlassTypeConfiguration.SeededAtUtc
    };

    private static object RetiredSeed(
        string id,
        Guid glassTypeId,
        decimal minimum,
        decimal expected,
        decimal maximum) => new
    {
        Id = Guid.Parse(id),
        GlassTypeId = glassTypeId,
        Version = 1,
        MinimumPricePerSquareMeter = minimum,
        ExpectedAmountPerM2 = expected,
        MaximumPricePerSquareMeter = maximum,
        Currency = "COP",
        Status = GlassPriceRangeStatus.Retired,
        ValidFromUtc = GlassTypeConfiguration.SeededAtUtc,
        ValidToUtc = GlassTypeConfiguration.SeededAtUtc.AddDays(1),
        CreatedAtUtc = GlassTypeConfiguration.SeededAtUtc
    };
}
