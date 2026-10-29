using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PublicExperienceIdeaPhotoConfiguration : IEntityTypeConfiguration<PublicExperienceIdeaPhoto>
{
    public void Configure(EntityTypeBuilder<PublicExperienceIdeaPhoto> builder)
    {
        builder.ToTable("public_experience_idea_photos", "core", table =>
            table.HasCheckConstraint("ck_idea_photos_evidence",
                "id <> '00000000-0000-0000-0000-000000000000'::uuid AND submission_id <> '00000000-0000-0000-0000-000000000000'::uuid "
                + "AND identity_version = 1 AND stored_evidence_version = 1 "
                + "AND stored_sha256 ~ '^[0-9A-F]{64}$' AND length(stored_sha256) = 64 "
                + "AND stored_length_bytes > 0 AND stored_length_bytes <= 20971520 "
                + "AND content_type IN ('image/jpeg', 'image/png')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id");
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.StoredSha256).HasColumnName("stored_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.StoredLengthBytes).HasColumnName("stored_length_bytes");
        builder.Property(x => x.StoredEvidenceAtUtc).HasColumnName("stored_evidence_at_utc");
        builder.Property(x => x.StoredEvidenceVersion).HasColumnName("stored_evidence_version");
        builder.Property(x => x.IdentityVersion).HasColumnName("identity_version");
        builder.HasOne<PublicExperienceSubmission>().WithOne(x => x.IdeaPhoto)
            .HasForeignKey<PublicExperienceIdeaPhoto>(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SubmissionId).IsUnique();
        builder.HasIndex(x => x.StorageKey).IsUnique();
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
