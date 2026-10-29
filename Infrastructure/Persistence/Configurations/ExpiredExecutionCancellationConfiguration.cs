using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ExpiredExecutionCancellationConfiguration : IEntityTypeConfiguration<ExpiredExecutionCancellation>
{
    public void Configure(EntityTypeBuilder<ExpiredExecutionCancellation> b)
    {
        b.ToTable("expired_execution_cancellations", "core", t =>
        {
            t.HasCheckConstraint("ck_expired_cancellation_ids", "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND requirement_id <> '00000000-0000-0000-0000-000000000000'::uuid AND execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND claim_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND closed_by_actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND (expected_processing_attempt_id IS NULL OR expected_processing_attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
            t.HasCheckConstraint("ck_expired_cancellation_owner", "length(lease_owner) = 78 AND lease_owner ~ '^exec:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' AND substring(lease_owner from 6 for 36) <> '00000000-0000-0000-0000-000000000000' AND substring(lease_owner from 43 for 36) = claim_operation_id::text");
            t.HasCheckConstraint("ck_expired_cancellation_shape", "lease_generation = 1 AND policy_version = 1 AND isfinite(assigned_at_utc) AND isfinite(expected_lease_expires_at_utc) AND isfinite(cancellation_requested_at_utc) AND isfinite(finished_at_utc) AND isfinite(guard_at_utc) AND assigned_at_utc < expected_lease_expires_at_utc AND expected_lease_expires_at_utc <= finished_at_utc AND finished_at_utc <= guard_at_utc AND cancellation_requested_at_utc <= finished_at_utc");
        });
        b.HasKey(x => x.OperationId).HasName("pk_expired_execution_cancellations");
        b.Property(x => x.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        b.Property(x => x.JobId).HasColumnName("job_id");
        b.Property(x => x.RequirementId).HasColumnName("requirement_id");
        b.Property(x => x.ExecutionRequestId).HasColumnName("execution_request_id");
        b.Property(x => x.ClaimOperationId).HasColumnName("claim_operation_id");
        b.Property(x => x.ClosedByActorUserId).HasColumnName("closed_by_actor_user_id");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200).IsRequired();
        b.Property(x => x.LeaseGeneration).HasColumnName("lease_generation");
        b.Property(x => x.AssignedAtUtc).HasColumnName("assigned_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.ExpectedLeaseExpiresAtUtc).HasColumnName("expected_lease_expires_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.ExpectedProcessingAttemptId).HasColumnName("expected_processing_attempt_id");
        b.Property(x => x.CancellationRequestedAtUtc).HasColumnName("cancellation_requested_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.FinishedAtUtc).HasColumnName("finished_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.GuardAtUtc).HasColumnName("guard_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.PolicyVersion).HasColumnName("policy_version");
        b.HasIndex(x => x.JobId).IsUnique().HasDatabaseName("ux_expired_cancellation_job");
        b.HasOne<RequirementProcessingJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_expired_cancellation_job");
        b.HasOne<Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_expired_cancellation_requirement");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ClosedByActorUserId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_expired_cancellation_actor");
        b.HasOne<JobLeaseOperation>().WithMany().HasForeignKey(x => x.ClaimOperationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_expired_cancellation_claim");
        b.HasOne<RequirementProcessingAttempt>().WithMany().HasForeignKey(x => x.ExpectedProcessingAttemptId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_expired_cancellation_attempt");
        foreach (var property in b.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
