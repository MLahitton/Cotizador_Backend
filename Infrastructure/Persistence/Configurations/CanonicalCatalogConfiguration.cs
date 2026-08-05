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
        builder.Property(value => value.ActiveForRecognition).HasColumnName("active_for_recognition").IsRequired();
        builder.Property(value => value.Priceable).HasColumnName("priceable").IsRequired();
        builder.Property(value => value.FuturePriceable).HasColumnName("future_priceable").IsRequired();
        builder.Property(value => value.RequiresReview).HasColumnName("requires_review").IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.Code).IsUnique().HasDatabaseName("ux_product_systems_code");

        builder.HasData(
            Seed(1, "K40", "Sistema K40", true, true, true, false),
            Seed(2, "K50", "Sistema K50", true, true, true, false),
            Seed(3, "K55", "Sistema K55", true, true, true, false),
            Seed(4, "K70", "Sistema K70", true, true, true, false),
            Seed(5, "K90", "Sistema K90", true, true, true, false),
            Seed(6, "K100", "Sistema K100", true, true, true, false),
            Seed(7, "S35", "Sistema S35", true, true, true, false),
            Seed(8, "S50", "Sistema S50", true, true, true, false),
            Seed(9, "S80", "Sistema S80", true, true, true, false),
            Seed(10, "3890", "Sistema 3890", true, true, true, false),
            Seed(11, "SG45", "Sistema SG45", true, true, true, false),
            Seed(12, "BARANDA", "Sistema para barandas", true, false, true, true),
            Seed(13, "DIVISION_BANO", "Sistema para divisiones de bano", true, false, true, true));
    }

    private static object Seed(
        int sequence,
        string code,
        string name,
        bool activeForRecognition,
        bool priceable,
        bool futurePriceable,
        bool requiresReview) => new
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
        UpdatedAtUtc = (DateTimeOffset?)null
    };
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
        builder.Property(value => value.RequiresReview).HasColumnName("requires_review").IsRequired();
        builder.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.Code).IsUnique().HasDatabaseName("ux_finish_types_code");

        builder.HasData(
            Seed(1, "STANDARD_NATURAL", "Acabado natural estandar", false),
            Seed(2, "ANODIZED_GRAY", "Anodizado gris", false),
            Seed(3, "BLACK_MATTE", "Negro mate", false),
            Seed(4, "SPECIAL", "Acabado especial", true),
            Seed(5, "UNKNOWN", "Acabado por confirmar", true));
    }

    private static object Seed(
        int sequence,
        string code,
        string name,
        bool requiresReview) => new
    {
        Id = Guid.Parse($"50000000-0000-0000-0000-{sequence:000000000000}"),
        Code = code,
        Name = name,
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
            Alias(15, CatalogAliasCategory.Finish, "ALUCOLOR POLIESTER NEGRO MATE", "BLACK_MATTE", CatalogAliasMatchPolicy.TechnicalPhrase, false));
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
