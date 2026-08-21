using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementConfiguration
    : IEntityTypeConfiguration<Requirement>
{
    public void Configure(EntityTypeBuilder<Requirement> builder)
    {
        builder.ToTable(
            "requirements",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_requirements_status",
                "\"status\" IN ('Pending', 'Processing', 'Processed', 'Failed')"));

        builder.HasKey(requirement => requirement.Id);

        builder.Property(requirement => requirement.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(requirement => requirement.PreQuoteId)
            .HasColumnName("pre_quote_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(requirement => requirement.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(requirement => requirement.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasColumnType("varchar(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(requirement => requirement.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(requirement => requirement.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(requirement => requirement.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasOne(requirement => requirement.PreQuote)
            .WithMany()
            .HasForeignKey(requirement => requirement.PreQuoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(requirement => requirement.CreatedByUser)
            .WithMany()
            .HasForeignKey(requirement => requirement.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(requirement => requirement.Files)
            .WithOne(file => file.Requirement)
            .HasForeignKey(file => file.RequirementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(requirement => requirement.ProcessingAttempts)
            .WithOne(attempt => attempt.Requirement)
            .HasForeignKey(attempt => attempt.RequirementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(requirement => requirement.Files)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(requirement => requirement.ProcessingAttempts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(requirement => requirement.PreQuoteId)
            .HasDatabaseName("ix_requirements_pre_quote_id");

        builder.HasIndex(requirement => requirement.CreatedByUserId)
            .HasDatabaseName("ix_requirements_created_by_user_id");

        builder.HasIndex(requirement => new
            {
                requirement.PreQuoteId,
                requirement.IsActive
            })
            .HasDatabaseName("ix_requirements_pre_quote_id_is_active");
    }
}
