using Domain.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class GlassTypeConfiguration
    : IEntityTypeConfiguration<GlassType>
{
    internal static readonly Guid Laminated44Id =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid Laminated44GrayId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");
    internal static readonly Guid Laminated55Id =
        Guid.Parse("10000000-0000-0000-0000-000000000003");
    internal static readonly Guid Laminated55GrayId =
        Guid.Parse("10000000-0000-0000-0000-000000000004");
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
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(value => value.Description)
            .HasColumnName("description")
            .HasMaxLength(500);
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
        builder.HasMany(value => value.PriceRangeVersions)
            .WithOne(value => value.GlassType)
            .HasForeignKey(value => value.GlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            Seed(Laminated44Id, "LAM_4_4", "Laminado 4+4"),
            Seed(Laminated44GrayId, "LAM_4_4_GRAY", "Laminado 4+4 gris"),
            Seed(Laminated55Id, "LAM_5_5", "Laminado 5+5"),
            Seed(Laminated55GrayId, "LAM_5_5_GRAY", "Laminado 5+5 gris"));
    }

    private static object Seed(Guid id, string code, string name) => new
    {
        Id = id,
        Code = code,
        Name = name,
        Description = (string?)null,
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
            Seed("20000000-0000-0000-0000-000000000001", GlassTypeConfiguration.Laminated44Id, 90000m, 110000m),
            Seed("20000000-0000-0000-0000-000000000002", GlassTypeConfiguration.Laminated44GrayId, 95000m, 95000m),
            Seed("20000000-0000-0000-0000-000000000003", GlassTypeConfiguration.Laminated55Id, 120000m, 140000m),
            Seed("20000000-0000-0000-0000-000000000004", GlassTypeConfiguration.Laminated55GrayId, 125000m, 145000m));
    }

    private static object Seed(
        string id,
        Guid glassTypeId,
        decimal minimum,
        decimal maximum) => new
    {
        Id = Guid.Parse(id),
        GlassTypeId = glassTypeId,
        Version = 1,
        MinimumPricePerSquareMeter = minimum,
        MaximumPricePerSquareMeter = maximum,
        Currency = "COP",
        Status = GlassPriceRangeStatus.Preliminary,
        ValidFromUtc = GlassTypeConfiguration.SeededAtUtc,
        ValidToUtc = (DateTimeOffset?)null,
        CreatedAtUtc = GlassTypeConfiguration.SeededAtUtc
    };
}
