using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementExecutionEvidenceConfiguration : IEntityTypeConfiguration<RequirementExecutionEvidence>
{
    public void Configure(EntityTypeBuilder<RequirementExecutionEvidence> b)
    {
        b.ToTable("requirement_execution_evidence", "core", t =>
        {
            t.HasCheckConstraint("ck_execution_evidence_shape", "manifest_version = 1 AND lease_generation = 1 AND file_count > 0 AND manifest_hash ~ '^[0-9A-F]{64}$'");
            t.HasCheckConstraint("ck_execution_evidence_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND requirement_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND processing_attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND primary_document_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        b.HasKey(x => x.Id).HasName("pk_execution_evidence");
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RequirementId).HasColumnName("requirement_id");
        b.Property(x => x.JobId).HasColumnName("job_id");
        b.Property(x => x.ExecutionRequestId).HasColumnName("execution_request_id");
        b.Property(x => x.ProcessingAttemptId).HasColumnName("processing_attempt_id");
        b.Property(x => x.LeaseGeneration).HasColumnName("lease_generation");
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        b.Property(x => x.PrimaryDocumentId).HasColumnName("primary_document_id");
        b.Property(x => x.ManifestVersion).HasColumnName("manifest_version");
        b.Property(x => x.ManifestHash).HasColumnName("manifest_hash").HasMaxLength(64);
        b.Property(x => x.VerifiedAtUtc).HasColumnName("verified_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.RegisteredAtUtc).HasColumnName("registered_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.FileCount).HasColumnName("file_count");
        b.HasIndex(x => x.JobId).IsUnique().HasDatabaseName("ux_execution_evidence_job");
        b.HasIndex(x => x.ProcessingAttemptId).IsUnique().HasDatabaseName("ux_execution_evidence_attempt");
        b.HasOne<Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_execution_evidence_requirement");
        b.HasOne<RequirementProcessingJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_execution_evidence_job");
        b.HasOne<RequirementProcessingAttempt>().WithMany().HasForeignKey(x => x.ProcessingAttemptId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_execution_evidence_attempt");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_execution_evidence_actor");
        b.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.EvidenceId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_execution_document_evidence");
        b.Navigation(x => x.Files).HasField("files").UsePropertyAccessMode(PropertyAccessMode.Field);
        foreach (var property in b.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class RequirementExecutionDocumentConfiguration : IEntityTypeConfiguration<RequirementExecutionDocument>
{
    public void Configure(EntityTypeBuilder<RequirementExecutionDocument> b)
    {
        b.ToTable("requirement_execution_documents", "core", t =>
        {
            t.HasCheckConstraint("ck_execution_document_shape", "ordinal >= 0 AND stored_evidence_version = 1 AND declared_length_bytes > 0 AND declared_length_bytes = expected_length_bytes AND expected_length_bytes = observed_length_bytes AND expected_sha256 ~ '^[0-9A-F]{64}$' AND observed_sha256 = expected_sha256");
            t.HasCheckConstraint("ck_execution_document_ids", "evidence_id <> '00000000-0000-0000-0000-000000000000'::uuid AND file_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        b.HasKey(x => new { x.EvidenceId, x.FileId }).HasName("pk_execution_documents");
        b.Property(x => x.EvidenceId).HasColumnName("evidence_id");
        b.Property(x => x.FileId).HasColumnName("file_id");
        b.Property(x => x.Ordinal).HasColumnName("ordinal");
        b.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255);
        b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100);
        b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500);
        b.Property(x => x.FileCreatedAtUtc).HasColumnName("file_created_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.DeclaredLengthBytes).HasColumnName("declared_length_bytes");
        b.Property(x => x.ExpectedSha256).HasColumnName("expected_sha256").HasMaxLength(64);
        b.Property(x => x.ExpectedLengthBytes).HasColumnName("expected_length_bytes");
        b.Property(x => x.StoredEvidenceAtUtc).HasColumnName("stored_evidence_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.StoredEvidenceVersion).HasColumnName("stored_evidence_version");
        b.Property(x => x.ObservedSha256).HasColumnName("observed_sha256").HasMaxLength(64);
        b.Property(x => x.ObservedLengthBytes).HasColumnName("observed_length_bytes");
        b.HasIndex(x => new { x.EvidenceId, x.Ordinal }).IsUnique().HasDatabaseName("ux_execution_document_ordinal");
        b.HasOne<RequirementFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_execution_document_file");
        foreach (var property in b.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

