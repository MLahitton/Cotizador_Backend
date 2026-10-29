using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class JobLeaseOperationConfiguration : IEntityTypeConfiguration<JobLeaseOperation>
{
    public void Configure(EntityTypeBuilder<JobLeaseOperation> b)
    {
        b.ToTable("job_lease_operations", "core", t =>
        {
            t.HasCheckConstraint("ck_lease_operation_ids", "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND requirement_id <> '00000000-0000-0000-0000-000000000000'::uuid AND execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND claim_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND (expected_processing_attempt_id IS NULL OR expected_processing_attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
            t.HasCheckConstraint("ck_lease_operation_owner", "length(lease_owner) = 78 AND lease_owner ~ '^exec:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' AND substring(lease_owner from 6 for 36) <> '00000000-0000-0000-0000-000000000000' AND substring(lease_owner from 43 for 36) = claim_operation_id::text");
            t.HasCheckConstraint("ck_lease_operation_shape", "kind IN ('Claim', 'Renew') AND lease_generation = 1 AND policy_version = 1 AND isfinite(assigned_at_utc) AND isfinite(applied_at_utc) AND isfinite(expires_at_utc) AND expires_at_utc > assigned_at_utc AND " +
                "((kind = 'Claim' AND claim_operation_id = operation_id AND expected_lease_expires_at_utc IS NULL AND expected_processing_attempt_id IS NULL AND assigned_at_utc = applied_at_utc AND expires_at_utc = applied_at_utc + interval '120 seconds') OR " +
                "(kind = 'Renew' AND claim_operation_id <> operation_id AND expected_lease_expires_at_utc IS NOT NULL AND isfinite(expected_lease_expires_at_utc) AND applied_at_utc >= assigned_at_utc AND expected_lease_expires_at_utc > applied_at_utc AND expires_at_utc = greatest(expected_lease_expires_at_utc, applied_at_utc + interval '120 seconds')))");
        });
        b.HasKey(x => x.OperationId).HasName("pk_job_lease_operations");
        b.Property(x => x.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.JobId).HasColumnName("job_id");
        b.Property(x => x.RequirementId).HasColumnName("requirement_id");
        b.Property(x => x.ExecutionRequestId).HasColumnName("execution_request_id");
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        b.Property(x => x.ClaimOperationId).HasColumnName("claim_operation_id");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200).IsRequired();
        b.Property(x => x.LeaseGeneration).HasColumnName("lease_generation");
        b.Property(x => x.PolicyVersion).HasColumnName("policy_version");
        b.Property(x => x.ExpectedLeaseExpiresAtUtc).HasColumnName("expected_lease_expires_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.ExpectedProcessingAttemptId).HasColumnName("expected_processing_attempt_id");
        b.Property(x => x.AssignedAtUtc).HasColumnName("assigned_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.AppliedAtUtc).HasColumnName("applied_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("timestamp with time zone");
        b.Ignore(x => x.IsEffectiveExtension);
        b.HasIndex(x => x.JobId).IsUnique().HasFilter("kind = 'Claim'").HasDatabaseName("ux_lease_operation_claim_job");
        b.HasIndex(x => x.LeaseOwner).IsUnique().HasFilter("kind = 'Claim'").HasDatabaseName("ux_lease_operation_claim_owner");
        b.HasOne<RequirementProcessingJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_lease_operation_job");
        b.HasOne<Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_lease_operation_requirement");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_lease_operation_actor");
        b.HasOne<JobLeaseOperation>().WithMany().HasForeignKey(x => x.ClaimOperationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_lease_operation_claim");
        foreach (var property in b.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
