using Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ExternalIdentityConfiguration
    : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.ToTable("external_identities", "identity");

        builder.HasKey(identity => identity.Id);

        builder.Property(identity => identity.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(identity => identity.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(identity => identity.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(identity => identity.ProviderSubject)
            .HasColumnName("provider_subject")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(identity => identity.ProviderEmail)
            .HasColumnName("provider_email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(identity => identity.EmailVerified)
            .HasColumnName("email_verified")
            .IsRequired();

        builder.Property(identity => identity.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(identity => identity.LastUsedAtUtc)
            .HasColumnName("last_used_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(identity => identity.User)
            .WithMany()
            .HasForeignKey(identity => identity.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(identity => new
            {
                identity.Provider,
                identity.ProviderSubject
            })
            .IsUnique()
            .HasDatabaseName(
                "ux_external_identities_provider_subject");
    }
}