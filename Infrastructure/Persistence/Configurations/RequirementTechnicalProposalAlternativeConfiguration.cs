using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementTechnicalProposalSystemAlternativeConfiguration
    : IEntityTypeConfiguration<RequirementTechnicalProposalSystemAlternative>
{
    public void Configure(
        EntityTypeBuilder<RequirementTechnicalProposalSystemAlternative> builder)
    {
        ConfigureCommon(builder, "requirement_technical_proposal_system_alternatives");

        builder.Property(alternative => alternative.ProductSystemId)
            .HasColumnName("product_system_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<Domain.Catalogs.ProductSystem>()
            .WithMany()
            .HasForeignKey(alternative => alternative.ProductSystemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(alternative => new
            {
                alternative.ProposalItemId,
                alternative.Rank
            })
            .IsUnique()
            .HasDatabaseName(
                "ux_req_tech_proposal_system_alt_item_rank");
    }

    private static void ConfigureCommon<T>(
        EntityTypeBuilder<T> builder,
        string tableName)
        where T : class
    {
        builder.ToTable(
            tableName,
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    $"ck_{tableName}_rank",
                    "\"rank\" > 0");
                tableBuilder.HasCheckConstraint(
                    $"ck_{tableName}_confidence",
                    "\"confidence\" >= 0 AND \"confidence\" <= 1");
            });

        builder.HasKey("Id");
        builder.Property<Guid>("Id")
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property<Guid>("ProposalItemId")
            .HasColumnName("proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property<int>("Rank")
            .HasColumnName("rank")
            .IsRequired();
        builder.Property<decimal>("Confidence")
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired();
        builder.Property<string[]>("Reasons")
            .HasColumnName("reasons")
            .HasColumnType("text[]")
            .IsRequired();
    }
}

public sealed class RequirementTechnicalProposalGlassAlternativeConfiguration
    : IEntityTypeConfiguration<RequirementTechnicalProposalGlassAlternative>
{
    public void Configure(
        EntityTypeBuilder<RequirementTechnicalProposalGlassAlternative> builder)
    {
        builder.ToTable(
            "requirement_technical_proposal_glass_alternatives",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_req_tech_proposal_glass_alt_rank",
                    "\"rank\" > 0");
                tableBuilder.HasCheckConstraint(
                    "ck_req_tech_proposal_glass_alt_confidence",
                    "\"confidence\" >= 0 AND \"confidence\" <= 1");
            });

        builder.HasKey(alternative => alternative.Id);
        builder.Property(alternative => alternative.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(alternative => alternative.ProposalItemId)
            .HasColumnName("proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(alternative => alternative.GlassTypeId)
            .HasColumnName("glass_type_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(alternative => alternative.Rank)
            .HasColumnName("rank")
            .IsRequired();
        builder.Property(alternative => alternative.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired();
        builder.Property(alternative => alternative.Reasons)
            .HasColumnName("reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasOne<Domain.Catalogs.GlassType>()
            .WithMany()
            .HasForeignKey(alternative => alternative.GlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(alternative => new
            {
                alternative.ProposalItemId,
                alternative.Rank
            })
            .IsUnique()
            .HasDatabaseName("ux_req_tech_proposal_glass_alt_item_rank");
    }
}

public sealed class RequirementTechnicalProposalFinishAlternativeConfiguration
    : IEntityTypeConfiguration<RequirementTechnicalProposalFinishAlternative>
{
    public void Configure(
        EntityTypeBuilder<RequirementTechnicalProposalFinishAlternative> builder)
    {
        builder.ToTable(
            "requirement_technical_proposal_finish_alternatives",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_req_tech_proposal_finish_alt_rank",
                    "\"rank\" > 0");
                tableBuilder.HasCheckConstraint(
                    "ck_req_tech_proposal_finish_alt_confidence",
                    "\"confidence\" >= 0 AND \"confidence\" <= 1");
            });

        builder.HasKey(alternative => alternative.Id);
        builder.Property(alternative => alternative.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(alternative => alternative.ProposalItemId)
            .HasColumnName("proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(alternative => alternative.FinishTypeId)
            .HasColumnName("finish_type_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(alternative => alternative.Rank)
            .HasColumnName("rank")
            .IsRequired();
        builder.Property(alternative => alternative.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired();
        builder.Property(alternative => alternative.Reasons)
            .HasColumnName("reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasOne<Domain.Catalogs.FinishType>()
            .WithMany()
            .HasForeignKey(alternative => alternative.FinishTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(alternative => new
            {
                alternative.ProposalItemId,
                alternative.Rank
            })
            .IsUnique()
            .HasDatabaseName("ux_req_tech_proposal_finish_alt_item_rank");
    }
}
