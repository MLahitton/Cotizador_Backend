using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PreQuoteDocumentConfiguration
    : IEntityTypeConfiguration<PreQuoteDocument>
{
    public void Configure(
        EntityTypeBuilder<PreQuoteDocument> builder)
    {
        builder.ToTable(
            "pre_quote_documents",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_pre_quote_documents_size_bytes_positive",
                "\"size_bytes\" > 0"));

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(document => document.PreQuoteId)
            .HasColumnName("pre_quote_id")
            .IsRequired();

        builder.Property(document => document.OriginalFileName)
            .HasColumnName("original_file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(document => document.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(document => document.SizeBytes)
            .HasColumnName("size_bytes")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(document => document.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(document => document.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(document => document.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(document => document.PreQuote)
            .WithMany()
            .HasForeignKey(document => document.PreQuoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(document => document.CreatedByUser)
            .WithMany()
            .HasForeignKey(document => document.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(document => document.PreQuoteId)
            .HasDatabaseName(
                "ix_pre_quote_documents_pre_quote_id");

        builder.HasIndex(document => document.CreatedByUserId)
            .HasDatabaseName(
                "ix_pre_quote_documents_created_by_user_id");

        builder.HasIndex(document => document.StorageKey)
            .IsUnique()
            .HasDatabaseName(
                "ux_pre_quote_documents_storage_key");
    }
}
