using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementPricingSnapshotConfiguration
    : IEntityTypeConfiguration<RequirementPricingSnapshot>
{
    public void Configure(EntityTypeBuilder<RequirementPricingSnapshot> builder)
    {
        builder.ToTable("requirement_pricing_snapshots", "core");

        builder.HasKey(snapshot => snapshot.Id);

        builder.Property(snapshot => snapshot.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(snapshot => snapshot.RequirementId)
            .HasColumnName("requirement_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(snapshot => snapshot.TechnicalProposalId)
            .HasColumnName("technical_proposal_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(snapshot => snapshot.Currency)
            .HasColumnName("currency")
            .HasColumnType("varchar(10)")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(snapshot => snapshot.PricingBasis)
            .HasColumnName("pricing_basis")
            .HasColumnType("varchar(80)")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(snapshot => snapshot.OriginalGrandTotal)
            .HasColumnName("original_grand_total")
            .HasColumnType("numeric(18,2)")
            .IsRequired(false);

        builder.Property(snapshot => snapshot.CurrentGrandTotal)
            .HasColumnName("current_grand_total")
            .HasColumnType("numeric(18,2)")
            .IsRequired(false);

        builder.Property(snapshot => snapshot.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(snapshot => snapshot.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(snapshot => snapshot.Requirement)
            .WithMany()
            .HasForeignKey(snapshot => snapshot.RequirementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(snapshot => snapshot.TechnicalProposal)
            .WithMany()
            .HasForeignKey(snapshot => snapshot.TechnicalProposalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(snapshot => snapshot.Items)
            .WithOne(item => item.PricingSnapshot)
            .HasForeignKey(item => item.PricingSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(snapshot => snapshot.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(snapshot => snapshot.RequirementId)
            .IsUnique()
            .HasDatabaseName("ux_requirement_pricing_snapshots_requirement_id");

        builder.HasIndex(snapshot => snapshot.TechnicalProposalId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_pricing_snapshots_technical_proposal_id");
    }
}

public sealed class RequirementPricingItemSnapshotConfiguration
    : IEntityTypeConfiguration<RequirementPricingItemSnapshot>
{
    public void Configure(EntityTypeBuilder<RequirementPricingItemSnapshot> builder)
    {
        builder.ToTable("requirement_pricing_item_snapshots", "core");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(item => item.PricingSnapshotId)
            .HasColumnName("pricing_snapshot_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.TechnicalProposalItemId)
            .HasColumnName("technical_proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.OriginalSystemId)
            .HasColumnName("original_system_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.OriginalGlassTypeId)
            .HasColumnName("original_glass_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.OriginalFinishTypeId)
            .HasColumnName("original_finish_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.CurrentSystemId)
            .HasColumnName("current_system_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.CurrentGlassTypeId)
            .HasColumnName("current_glass_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.CurrentFinishTypeId)
            .HasColumnName("current_finish_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.OriginalStatus)
            .HasColumnName("original_status")
            .HasColumnType("varchar(40)")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(item => item.CurrentStatus)
            .HasColumnName("current_status")
            .HasColumnType("varchar(40)")
            .HasMaxLength(40)
            .IsRequired();

        ConfigureMoney(builder, item => item.OriginalUnitMinimum, "original_unit_minimum");
        ConfigureMoney(builder, item => item.OriginalUnitExpected, "original_unit_expected");
        ConfigureMoney(builder, item => item.OriginalUnitMaximum, "original_unit_maximum");
        ConfigureMoney(builder, item => item.OriginalLineMinimum, "original_line_minimum");
        ConfigureMoney(builder, item => item.OriginalLineExpected, "original_line_expected");
        ConfigureMoney(builder, item => item.OriginalLineMaximum, "original_line_maximum");
        ConfigureMoney(builder, item => item.CurrentUnitMinimum, "current_unit_minimum");
        ConfigureMoney(builder, item => item.CurrentUnitExpected, "current_unit_expected");
        ConfigureMoney(builder, item => item.CurrentUnitMaximum, "current_unit_maximum");
        ConfigureMoney(builder, item => item.CurrentLineMinimum, "current_line_minimum");
        ConfigureMoney(builder, item => item.CurrentLineExpected, "current_line_expected");
        ConfigureMoney(builder, item => item.CurrentLineMaximum, "current_line_maximum");

        builder.Property(item => item.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(item => item.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(item => item.TechnicalProposalItem)
            .WithMany()
            .HasForeignKey(item => item.TechnicalProposalItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.ProductSystem>()
            .WithMany()
            .HasForeignKey(item => item.OriginalSystemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.GlassType>()
            .WithMany()
            .HasForeignKey(item => item.OriginalGlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.FinishType>()
            .WithMany()
            .HasForeignKey(item => item.OriginalFinishTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.ProductSystem>()
            .WithMany()
            .HasForeignKey(item => item.CurrentSystemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.GlassType>()
            .WithMany()
            .HasForeignKey(item => item.CurrentGlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.FinishType>()
            .WithMany()
            .HasForeignKey(item => item.CurrentFinishTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => item.PricingSnapshotId)
            .HasDatabaseName(
                "ix_requirement_pricing_item_snapshots_pricing_snapshot_id");

        builder.HasIndex(item => item.TechnicalProposalItemId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_pricing_item_snapshots_proposal_item_id");
    }

    private static void ConfigureMoney(
        EntityTypeBuilder<RequirementPricingItemSnapshot> builder,
        System.Linq.Expressions.Expression<Func<RequirementPricingItemSnapshot, decimal?>>
            property,
        string columnName) =>
        builder.Property(property)
            .HasColumnName(columnName)
            .HasColumnType("numeric(18,2)")
            .IsRequired(false);
}
