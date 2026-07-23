using Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration
    : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", "core");

        builder.HasKey(project => project.Id);

        builder.Property(project => project.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(project => project.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(project => project.Code)
            .HasColumnName("code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(project => project.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(project => project.Description)
            .HasColumnName("description")
            .HasMaxLength(1000);

        builder.Property(project => project.Location)
            .HasColumnName("location")
            .HasMaxLength(250);

        builder.Property(project => project.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(project => project.UpdatedByUserId)
            .HasColumnName("updated_by_user_id")
            .IsRequired();

        builder.Property(project => project.StatusChangedByUserId)
            .HasColumnName("status_changed_by_user_id");

        builder.Property(project => project.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(project => project.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(project => project.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(project => project.StatusChangedAtUtc)
            .HasColumnName("status_changed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(project => project.Client)
            .WithMany()
            .HasForeignKey(project => project.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(project => project.CreatedByUser)
            .WithMany()
            .HasForeignKey(project => project.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(project => project.UpdatedByUser)
            .WithMany()
            .HasForeignKey(project => project.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(project => project.StatusChangedByUser)
            .WithMany()
            .HasForeignKey(project => project.StatusChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(project => project.Code)
            .IsUnique()
            .HasDatabaseName("ux_projects_code");

        builder.HasIndex(project => project.ClientId)
            .HasDatabaseName("ix_projects_client_id");

        builder.HasIndex(project => project.CreatedByUserId)
            .HasDatabaseName("ix_projects_created_by_user_id");

        builder.HasIndex(project => project.UpdatedByUserId)
            .HasDatabaseName("ix_projects_updated_by_user_id");

        builder.HasIndex(project => project.StatusChangedByUserId)
            .HasDatabaseName("ix_projects_status_changed_by_user_id");
    }
}
