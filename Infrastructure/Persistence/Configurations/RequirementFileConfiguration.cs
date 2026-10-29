using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
            tableBuilder => { tableBuilder.HasCheckConstraint(
                "ck_requirement_files_size_bytes_positive",
                "\"size_bytes\" > 0");
                tableBuilder.HasCheckConstraint("ck_requirement_files_stored_evidence",
                    "(stored_sha256 IS NULL AND stored_length_bytes IS NULL AND stored_evidence_at_utc IS NULL AND stored_evidence_version IS NULL) OR "
                    + "(stored_sha256 IS NOT NULL AND stored_length_bytes IS NOT NULL AND stored_evidence_at_utc IS NOT NULL AND stored_evidence_version IS NOT NULL "
                    + "AND stored_evidence_version = 1 AND length(stored_sha256) = 64 AND stored_sha256 ~ '^[0-9A-F]{64}$' "
                    + "AND stored_length_bytes > 0 AND stored_length_bytes = size_bytes)");
            });

        builder.Property(file => file.StoredSha256).HasColumnName("stored_sha256")
            .HasMaxLength(64).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(file => file.StoredLengthBytes).HasColumnName("stored_length_bytes")
            .HasColumnType("bigint").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(file => file.StoredEvidenceAtUtc).HasColumnName("stored_evidence_at_utc")
            .HasColumnType("timestamp with time zone").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(file => file.StoredEvidenceVersion).HasColumnName("stored_evidence_version")
            .HasColumnType("smallint").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

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
