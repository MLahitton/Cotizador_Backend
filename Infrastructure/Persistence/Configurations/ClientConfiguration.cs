using Domain.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ClientConfiguration
    : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients", "core");

        builder.HasKey(client => client.Id);

        builder.Property(client => client.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(client => client.ClientType)
            .HasColumnName("client_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(client => client.LegalName)
            .HasColumnName("legal_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(client => client.TradeName)
            .HasColumnName("trade_name")
            .HasMaxLength(200);

        builder.Property(client => client.DocumentType)
            .HasColumnName("document_type")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(client => client.DocumentNumber)
            .HasColumnName("document_number")
            .HasMaxLength(50);

        builder.Property(client => client.Email)
            .HasColumnName("email")
            .HasMaxLength(320);

        builder.Property(client => client.Phone)
            .HasColumnName("phone")
            .HasMaxLength(50);

        builder.Property(client => client.Address)
            .HasColumnName("address")
            .HasMaxLength(300);

        builder.Property(client => client.City)
            .HasColumnName("city")
            .HasMaxLength(100);

        builder.Property(client => client.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(client => client.UpdatedByUserId)
            .HasColumnName("updated_by_user_id")
            .IsRequired();

        builder.Property(client => client.StatusChangedByUserId)
            .HasColumnName("status_changed_by_user_id");

        builder.Property(client => client.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(client => client.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(client => client.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(client => client.StatusChangedAtUtc)
            .HasColumnName("status_changed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(client => client.CreatedByUser)
            .WithMany()
            .HasForeignKey(client => client.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(client => client.UpdatedByUser)
            .WithMany()
            .HasForeignKey(client => client.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(client => client.StatusChangedByUser)
            .WithMany()
            .HasForeignKey(client => client.StatusChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(client => new
            {
                client.DocumentType,
                client.DocumentNumber
            })
            .IsUnique()
            .HasFilter(
                "\"document_type\" IS NOT NULL " +
                "AND \"document_number\" IS NOT NULL")
            .HasDatabaseName(
                "ux_clients_document_type_number");

        builder.HasIndex(client => client.CreatedByUserId)
            .HasDatabaseName("ix_clients_created_by_user_id");

        builder.HasIndex(client => client.UpdatedByUserId)
            .HasDatabaseName("ix_clients_updated_by_user_id");

        builder.HasIndex(client => client.StatusChangedByUserId)
            .HasDatabaseName("ix_clients_status_changed_by_user_id");
    }
}
