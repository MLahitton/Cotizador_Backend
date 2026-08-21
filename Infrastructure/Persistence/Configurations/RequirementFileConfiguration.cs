using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementFileConfiguration
    : IEntityTypeConfiguration<RequirementFile>
{
    public void Configure(EntityTypeBuilder<RequirementFile> builder)
    {
        builder.ToTable(
            "requirement_files",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_requirement_files_size_bytes_positive",
                "\"size_bytes\" > 0"));

        builder.HasKey(file => file.Id);

        builder.Property(file => file.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(file => file.RequirementId)
            .HasColumnName("requirement_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(file => file.OriginalFileName)
            .HasColumnName("original_file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(file => file.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(file => file.SizeBytes)
            .HasColumnName("size_bytes")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(file => file.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(file => file.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(file => file.RequirementId)
            .HasDatabaseName("ix_requirement_files_requirement_id");

        builder.HasIndex(file => file.StorageKey)
            .IsUnique()
            .HasDatabaseName("ux_requirement_files_storage_key");
    }
}
