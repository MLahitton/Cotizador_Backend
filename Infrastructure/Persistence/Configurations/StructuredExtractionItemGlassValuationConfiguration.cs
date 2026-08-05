using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class StructuredExtractionItemGlassValuationConfiguration
    : IEntityTypeConfiguration<StructuredExtractionItemGlassValuation>
{
    public void Configure(EntityTypeBuilder<StructuredExtractionItemGlassValuation> b)
    {
        b.ToTable("structured_extraction_item_glass_valuations", "core", t =>
        {
            t.HasCheckConstraint("ck_structured_glass_valuation_areas", "\"unit_area_square_meters\" IS NULL OR \"unit_area_square_meters\" >= 0 AND \"total_area_square_meters\" >= 0");
            t.HasCheckConstraint("ck_structured_glass_valuation_amounts", "\"minimum_amount\" IS NULL OR \"minimum_amount\" >= 0 AND \"maximum_amount\" >= \"minimum_amount\"");
            t.HasCheckConstraint("ck_structured_glass_valuation_prices", "\"minimum_price_per_square_meter\" IS NULL OR \"minimum_price_per_square_meter\" > 0 AND \"expected_price_per_square_meter\" >= \"minimum_price_per_square_meter\" AND \"expected_price_per_square_meter\" <= \"maximum_price_per_square_meter\" AND \"maximum_price_per_square_meter\" >= \"minimum_price_per_square_meter\"");
            t.HasCheckConstraint("ck_structured_glass_valuation_expected_amount", "\"expected_amount\" IS NULL OR \"expected_amount\" >= \"minimum_amount\" AND \"expected_amount\" <= \"maximum_amount\"");
            t.HasCheckConstraint("ck_structured_glass_valuation_currency", "\"currency\" IS NULL OR char_length(\"currency\") = 3");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.StructuredExtractionItemId).HasColumnName("structured_extraction_item_id");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasColumnName("reason").HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.GlassTypeId).HasColumnName("glass_type_id");
        b.Property(x => x.GlassPriceRangeVersionId).HasColumnName("glass_price_range_version_id");
        b.Property(x => x.PriceRangeVersion).HasColumnName("price_range_version");
        b.Property(x => x.PriceRangeStatus).HasColumnName("price_range_status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.UnitAreaSquareMeters).HasColumnName("unit_area_square_meters").HasPrecision(18, 6);
        b.Property(x => x.TotalAreaSquareMeters).HasColumnName("total_area_square_meters").HasPrecision(18, 6);
        b.Property(x => x.MinimumPricePerSquareMeter).HasColumnName("minimum_price_per_square_meter").HasPrecision(18, 2);
        b.Property(x => x.ExpectedPricePerSquareMeter).HasColumnName("expected_price_per_square_meter").HasPrecision(18, 2);
        b.Property(x => x.MaximumPricePerSquareMeter).HasColumnName("maximum_price_per_square_meter").HasPrecision(18, 2);
        b.Property(x => x.MinimumAmount).HasColumnName("minimum_amount").HasPrecision(18, 2);
        b.Property(x => x.ExpectedAmount).HasColumnName("expected_amount").HasPrecision(18, 2);
        b.Property(x => x.MaximumAmount).HasColumnName("maximum_amount").HasPrecision(18, 2);
        b.Property(x => x.CalculatedAtUtc).HasColumnName("calculated_at_utc").HasColumnType("timestamp with time zone");
        b.HasIndex(x => x.StructuredExtractionItemId).IsUnique();
        b.HasIndex(x => x.GlassTypeId);
        b.HasIndex(x => x.GlassPriceRangeVersionId);
        b.HasOne(x => x.StructuredExtractionItem).WithOne(x => x.GlassValuation)
            .HasForeignKey<StructuredExtractionItemGlassValuation>(x => x.StructuredExtractionItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.GlassType).WithMany().HasForeignKey(x => x.GlassTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.GlassPriceRangeVersion).WithMany().HasForeignKey(x => x.GlassPriceRangeVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
