using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PublicExperienceSubmissionConfiguration : IEntityTypeConfiguration<PublicExperienceSubmission>
{
    public void Configure(EntityTypeBuilder<PublicExperienceSubmission> builder)
    {
        builder.ToTable("public_experience_submissions", "core");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SchemaVersion).HasColumnName("schema_version").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ClientFullName).HasColumnName("client_full_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ClientWhatsapp).HasColumnName("client_whatsapp").HasMaxLength(50).IsRequired();
        builder.Property(x => x.AdvisorCode).HasColumnName("advisor_code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.AdvisorDisplayName).HasColumnName("advisor_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProjectName).HasColumnName("project_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProjectCity).HasColumnName("project_city").HasMaxLength(250).IsRequired();
        builder.Property(x => x.ProjectSubdivision).HasColumnName("project_subdivision").HasMaxLength(250);
        builder.Property(x => x.ProjectLotOrApartment).HasColumnName("project_lot_or_apartment").HasMaxLength(250);
        builder.Property(x => x.ProjectNotes).HasColumnName("project_notes").HasMaxLength(1000);
        builder.Property(x => x.AnswersJson).HasColumnName("answers").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PayloadFingerprint).HasColumnName("payload_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(x => x.ClientId).HasColumnName("client_id");
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(x => x.ProcessingErrorCode).HasColumnName("processing_error_code").HasMaxLength(100);
        builder.Property(x => x.PreQuoteId).HasColumnName("pre_quote_id");
        builder.Property(x => x.RequirementId).HasColumnName("requirement_id");
        builder.Property(x => x.RequirementPreparedAtUtc).HasColumnName("requirement_prepared_at_utc");
        builder.Property(x => x.RequirementPreparationErrorCode).HasColumnName("requirement_preparation_error_code").HasMaxLength(100);
        builder.HasOne<Domain.PreQuotes.PreQuote>().WithMany().HasForeignKey(x => x.PreQuoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.PreQuotes.Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Clients.Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Projects.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SubmissionId).IsUnique()
            .HasDatabaseName(PublicExperienceSubmissionRepository.UniqueSubmissionIndex);
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PublicExperienceSubmissionFileConfiguration : IEntityTypeConfiguration<PublicExperienceSubmissionFile>
{
    public void Configure(EntityTypeBuilder<PublicExperienceSubmissionFile> builder)
    {
        builder.ToTable("public_experience_submission_files", "core", t =>
            t.HasCheckConstraint("ck_submission_files_stored_evidence",
                "(stored_length_bytes IS NULL AND stored_evidence_at_utc IS NULL AND stored_evidence_version IS NULL) OR "
                + "(stored_length_bytes IS NOT NULL AND stored_evidence_at_utc IS NOT NULL AND stored_evidence_version IS NOT NULL "
                + "AND stored_evidence_version = 1 AND stored_length_bytes > 0 AND stored_length_bytes = size_bytes "
                + "AND content_hash IS NOT NULL AND length(content_hash) = 64 AND content_hash ~ '^[0-9A-F]{64}$')"));
        builder.Property(x => x.StoredLengthBytes).HasColumnName("stored_length_bytes")
            .HasColumnType("bigint").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.StoredEvidenceAtUtc).HasColumnName("stored_evidence_at_utc")
            .HasColumnType("timestamp with time zone").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.StoredEvidenceVersion).HasColumnName("stored_evidence_version")
            .HasColumnType("smallint").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id");
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(x => new { x.SubmissionId, x.Sequence }).IsUnique();
        builder.HasIndex(x => x.StorageKey).IsUnique();
    }
}
