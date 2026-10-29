using Domain.Identity;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementProcessingJobConfiguration : IEntityTypeConfiguration<RequirementProcessingJob>
{
    public void Configure(EntityTypeBuilder<RequirementProcessingJob> builder)
    {
        builder.ToTable("requirement_processing_jobs", "core", table =>
        {
            table.HasCheckConstraint("ck_processing_jobs_automatic_authorization",
                "(automatic_authorization_policy_version IS NULL AND authorized_actor_user_id IS NULL AND authorized_at_utc IS NULL AND authorization_expires_at_utc IS NULL) OR " +
                "(automatic_authorization_policy_version IS NOT NULL AND automatic_authorization_policy_version = 1 AND authorized_actor_user_id IS NOT NULL AND authorized_actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "authorized_at_utc IS NOT NULL AND authorization_expires_at_utc IS NOT NULL AND isfinite(authorized_at_utc) AND isfinite(authorization_expires_at_utc) AND " +
                "authorization_expires_at_utc - authorized_at_utc = interval '24 hours' AND " +
                "trigger_kind = 'PublicPreparation' AND required_publication_version = 'sng-job-publication-v2' AND " +
                "submission_id IS NOT NULL AND submission_id <> '00000000-0000-0000-0000-000000000000'::uuid AND retry_of_job_id IS NULL AND requested_by_user_id IS NULL)");
            table.HasCheckConstraint("ck_processing_jobs_automatic_dispatch",
                "(dispatch_admitted_at_utc IS NULL AND dispatch_instance_id IS NULL AND dispatch_claim_operation_id IS NULL) OR " +
                "(automatic_authorization_policy_version IS NOT NULL AND dispatch_admitted_at_utc IS NOT NULL AND dispatch_instance_id IS NOT NULL AND dispatch_claim_operation_id IS NOT NULL AND " +
                "dispatch_instance_id <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatch_claim_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "isfinite(dispatch_admitted_at_utc) AND dispatch_admitted_at_utc >= authorized_at_utc AND dispatch_admitted_at_utc < authorization_expires_at_utc)");
            table.HasCheckConstraint("ck_processing_jobs_automatic_revocation",
                "authorization_revoked_at_utc IS NULL OR (automatic_authorization_policy_version IS NOT NULL AND isfinite(authorization_revoked_at_utc) AND " +
                "authorization_revoked_at_utc >= authorized_at_utc AND (dispatch_admitted_at_utc IS NULL OR authorization_revoked_at_utc >= dispatch_admitted_at_utc))");
            table.HasCheckConstraint("ck_processing_jobs_required_publication",
                "required_publication_version IN ('sng-job-publication-v1', 'sng-job-publication-v2') AND " +
                "(publication_contract_version IS NULL OR publication_contract_version = required_publication_version) AND " +
                "(required_publication_version <> 'sng-job-publication-v2' OR state <> 'Succeeded' OR publication_contract_version IS NOT NULL)");
            table.HasCheckConstraint("ck_processing_jobs_publication_receipt",
                "(publication_contract_version IS NULL AND publication_content_hash IS NULL) OR " +
                "(publication_contract_version IS NOT NULL AND publication_content_hash IS NOT NULL " +
                "AND publication_contract_version ~ '[^[:space:]]' AND publication_content_hash ~ '^[A-F0-9]{64}$' AND state = 'Succeeded')");
            table.HasCheckConstraint("ck_processing_jobs_identity",
                "execution_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_processing_jobs_trigger",
                "trigger_kind IN ('PublicPreparation', 'ManualInitial', 'ManualRetry') AND ((trigger_kind = 'ManualRetry' AND retry_of_job_id IS NOT NULL AND retry_of_job_id <> id) OR (trigger_kind <> 'ManualRetry' AND retry_of_job_id IS NULL))");
            table.HasCheckConstraint("ck_processing_jobs_attempt",
                "(attempt_count = 0 AND processing_attempt_id IS NULL) OR (attempt_count = 1 AND processing_attempt_id IS NOT NULL)");
            table.HasCheckConstraint("ck_processing_jobs_dates",
                "(started_at_utc IS NULL OR started_at_utc >= created_at_utc) AND " +
                "(cancellation_requested_at_utc IS NULL OR (cancellation_requested_at_utc >= created_at_utc AND (started_at_utc IS NULL OR cancellation_requested_at_utc >= started_at_utc))) AND " +
                "(finished_at_utc IS NULL OR (finished_at_utc >= created_at_utc AND (started_at_utc IS NULL OR finished_at_utc >= started_at_utc) AND (cancellation_requested_at_utc IS NULL OR finished_at_utc >= cancellation_requested_at_utc)))");
            table.HasCheckConstraint("ck_processing_jobs_error",
                "(state = 'Failed' AND last_error_code IS NOT NULL AND last_error_code ~ '^[A-Z][A-Z0-9_]{0,63}$') OR (state <> 'Failed' AND last_error_code IS NULL)");
            table.HasCheckConstraint("ck_processing_jobs_lifecycle",
                "(state = 'Queued' AND started_at_utc IS NULL AND finished_at_utc IS NULL AND lease_owner IS NULL AND lease_expires_at_utc IS NULL AND lease_generation = 0 AND attempt_count = 0 AND cancellation_requested_at_utc IS NULL) OR " +
                "(state = 'Running' AND started_at_utc IS NOT NULL AND finished_at_utc IS NULL AND lease_owner IS NOT NULL AND btrim(lease_owner) <> '' AND lease_expires_at_utc IS NOT NULL AND lease_expires_at_utc > started_at_utc AND lease_generation = 1) OR " +
                "(state IN ('Succeeded', 'Failed', 'Cancelled') AND finished_at_utc IS NOT NULL AND lease_owner IS NULL AND lease_expires_at_utc IS NULL AND " +
                "((started_at_utc IS NOT NULL AND lease_generation = 1) OR (state = 'Cancelled' AND started_at_utc IS NULL AND lease_generation = 0 AND attempt_count = 0)) AND " +
                "((state = 'Cancelled' AND cancellation_requested_at_utc IS NOT NULL) OR (state IN ('Succeeded', 'Failed') AND cancellation_requested_at_utc IS NULL AND started_at_utc IS NOT NULL)) AND " +
                "(state <> 'Succeeded' OR attempt_count = 1))");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.AutomaticAuthorizationPolicyVersion).HasColumnName("automatic_authorization_policy_version")
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.Property(x => x.AuthorizedActorUserId).HasColumnName("authorized_actor_user_id")
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.Property(x => x.AuthorizedAtUtc).HasColumnName("authorized_at_utc")
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.Property(x => x.AuthorizationExpiresAtUtc).HasColumnName("authorization_expires_at_utc")
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.Property(x => x.AuthorizationRevokedAtUtc).HasColumnName("authorization_revoked_at_utc");
        builder.Property(x => x.DispatchAdmittedAtUtc).HasColumnName("dispatch_admitted_at_utc");
        builder.Property(x => x.DispatchInstanceId).HasColumnName("dispatch_instance_id");
        builder.Property(x => x.DispatchClaimOperationId).HasColumnName("dispatch_claim_operation_id");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorizedActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SubmissionId);
        builder.HasIndex(x => x.SubmissionId, "AutomaticSubmission").IsUnique()
            .HasFilter("automatic_authorization_policy_version IS NOT NULL")
            .HasDatabaseName("ux_processing_jobs_automatic_submission");
        builder.Property(x => x.RequirementId).HasColumnName("requirement_id");
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id");
        builder.Property(x => x.ExecutionRequestId).HasColumnName("execution_request_id");
        builder.Property(x => x.RetryOfJobId).HasColumnName("retry_of_job_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.ProcessingAttemptId).HasColumnName("processing_attempt_id");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.FinishedAtUtc).HasColumnName("finished_at_utc");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200);
        builder.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
        builder.Property(x => x.LeaseGeneration).HasColumnName("lease_generation");
        builder.Property(x => x.CancellationRequestedAtUtc).HasColumnName("cancellation_requested_at_utc");
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(64);
        builder.Property(x => x.PublicationContractVersion).HasColumnName("publication_contract_version").HasMaxLength(100);
        builder.Property(x => x.PublicationContentHash).HasColumnName("publication_content_hash").HasMaxLength(64);
        builder.Property(x => x.RequiredPublicationVersion).HasColumnName("required_publication_version")
            .HasMaxLength(100).HasDefaultValue(RequirementProcessingJob.PublicationV1)
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.TriggerKind).HasColumnName("trigger_kind").HasConversion<string>().HasMaxLength(30);
        builder.HasOne<Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublicExperienceSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RequirementProcessingAttempt>().WithMany().HasForeignKey(x => x.ProcessingAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RequirementProcessingJob>().WithMany().HasForeignKey(x => x.RetryOfJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ExecutionRequestId).IsUnique().HasDatabaseName("ux_processing_jobs_execution_request");
        builder.HasIndex(x => x.ProcessingAttemptId).IsUnique().HasDatabaseName("ux_processing_jobs_attempt");
        builder.HasIndex(x => x.RequirementId, "JobRequirementHistory").HasDatabaseName("ix_processing_jobs_requirement");
        builder.HasIndex(x => x.RequirementId, "JobActiveRequirement").IsUnique()
            .HasFilter("state IN ('Queued', 'Running')").HasDatabaseName("ux_processing_jobs_active_requirement");
        builder.HasIndex(x => new { x.State, x.CreatedAtUtc, x.Id }).HasDatabaseName("ix_processing_jobs_state_created_id");
    }
}
