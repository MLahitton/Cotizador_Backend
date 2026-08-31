using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementChatThreadConfiguration
    : IEntityTypeConfiguration<RequirementChatThread>
{
    public void Configure(EntityTypeBuilder<RequirementChatThread> builder)
    {
        builder.ToTable(
            "requirement_chat_threads",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_chat_threads_scope",
                    "\"scope\" IN ('Requirement', 'Item')");
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_chat_threads_locator",
                    "(\"scope\" = 'Requirement' AND \"technical_proposal_item_id\" IS NULL) " +
                    "OR (\"scope\" = 'Item' AND \"technical_proposal_item_id\" IS NOT NULL)");
            });

        builder.HasKey(thread => thread.Id);

        builder.Property(thread => thread.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(thread => thread.RequirementId)
            .HasColumnName("requirement_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(thread => thread.TechnicalProposalItemId)
            .HasColumnName("technical_proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(thread => thread.Scope)
            .HasColumnName("scope")
            .HasConversion<string>()
            .HasColumnType("varchar(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(thread => thread.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(thread => thread.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(thread => thread.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(thread => thread.Requirement)
            .WithMany()
            .HasForeignKey(thread => thread.RequirementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(thread => thread.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(thread => thread.Messages)
            .WithOne(message => message.ChatThread)
            .HasForeignKey(message => message.ChatThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(thread => thread.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(thread => thread.RequirementId)
            .HasDatabaseName("ix_requirement_chat_threads_requirement_id");

        builder.HasIndex(thread => thread.TechnicalProposalItemId)
            .HasDatabaseName(
                "ix_requirement_chat_threads_technical_proposal_item_id");

        builder.HasIndex(thread => thread.CreatedByUserId)
            .HasDatabaseName(
                "ix_requirement_chat_threads_created_by_user_id");

        builder.HasIndex(thread => new
            {
                thread.RequirementId,
                thread.Scope
            })
            .IsUnique()
            .HasFilter("\"scope\" = 'Requirement'")
            .HasDatabaseName(
                "ux_requirement_chat_threads_requirement_scope");

        builder.HasIndex(thread => new
            {
                thread.RequirementId,
                thread.TechnicalProposalItemId,
                thread.Scope
            })
            .IsUnique()
            .HasFilter("\"scope\" = 'Item'")
            .HasDatabaseName("ux_requirement_chat_threads_requirement_item_scope");
    }
}

public sealed class RequirementChatMessageConfiguration
    : IEntityTypeConfiguration<RequirementChatMessage>
{
    public void Configure(EntityTypeBuilder<RequirementChatMessage> builder)
    {
        builder.ToTable(
            "requirement_chat_messages",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_chat_messages_role",
                    "\"role\" IN ('User', 'Assistant')");
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_chat_messages_sequence",
                    "\"sequence\" > 0");
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_chat_messages_content",
                    "length(btrim(\"content\")) > 0");
            });

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(message => message.ChatThreadId)
            .HasColumnName("chat_thread_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(message => message.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasColumnType("varchar(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(message => message.Content)
            .HasColumnName("content")
            .HasColumnType("varchar(4000)")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(message => message.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(message => message.Sequence)
            .HasColumnName("sequence")
            .IsRequired();

        builder.Property(message => message.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.HasIndex(message => message.ChatThreadId)
            .HasDatabaseName("ix_requirement_chat_messages_thread_id");

        builder.HasIndex(message => new
            {
                message.ChatThreadId,
                message.Sequence
            })
            .IsUnique()
            .HasDatabaseName("ux_requirement_chat_messages_thread_sequence");

        builder.HasIndex(message => new
            {
                message.ChatThreadId,
                message.CreatedAtUtc,
                message.Id
            })
            .HasDatabaseName(
                "ix_requirement_chat_messages_thread_created_id");
    }
}
