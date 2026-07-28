using System.Reflection;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class DocumentProcessingAttemptModelTests
{
    [Fact]
    public void Model_ConfiguresLifecycleColumnsAndIndexes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=metadata_only;Username=metadata")
            .Options;
        using var context = new ApplicationDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = Assert.IsAssignableFrom<IReadOnlyEntityType>(
            model.FindEntityType(
                typeof(DocumentProcessingAttempt)));
        var table = StoreObjectIdentifier.Table(
            "document_processing_attempts",
            "core");

        var processingState = Assert.IsAssignableFrom<IReadOnlyProperty>(
            entity.FindProperty(
                nameof(DocumentProcessingAttempt.ProcessingState)));
        Assert.False(processingState.IsNullable);
        Assert.Equal(
            "processing_state",
            processingState.GetColumnName(table));
        Assert.Equal("varchar(20)", processingState.GetColumnType());
        var converter = processingState.GetTypeMapping().Converter;
        Assert.NotNull(converter);
        Assert.Equal(
            "Pending",
            converter.ConvertToProvider(DocumentProcessingState.Pending));
        Assert.Equal(
            DocumentProcessingState.Processing,
            converter.ConvertFromProvider("Processing"));

        var startedAtUtc = Assert.IsAssignableFrom<IReadOnlyProperty>(
            entity.FindProperty(
                nameof(DocumentProcessingAttempt.StartedAtUtc)));
        Assert.True(startedAtUtc.IsNullable);
        Assert.Equal(
            "started_at_utc",
            startedAtUtc.GetColumnName(table));

        var claimIndex = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName()
                == "ix_document_processing_attempts_processing_state_created_at_utc");
        Assert.Equal(
            [
                nameof(DocumentProcessingAttempt.ProcessingState),
                nameof(DocumentProcessingAttempt.CreatedAtUtc)
            ],
            claimIndex.Properties.Select(property => property.Name));
        Assert.False(claimIndex.IsUnique);

        var activeIndex = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName()
                == "ux_document_processing_attempts_active_pre_quote_document_id");
        Assert.True(activeIndex.IsUnique);
        Assert.Equal(
            "\"processing_state\" IN ('Pending', 'Processing')",
            activeIndex.GetFilter());
        Assert.Equal(
            [nameof(DocumentProcessingAttempt.PreQuoteDocumentId)],
            activeIndex.Properties.Select(property => property.Name));

        var lifecycleConstraint = Assert.Single(
            entity.GetCheckConstraints(),
            constraint => constraint.Name
                == "ck_document_processing_attempts_lifecycle");
        Assert.Contains(
            "\"processing_state\" = 'Pending'",
            lifecycleConstraint.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"processing_state\" = 'Processing'",
            lifecycleConstraint.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"processing_state\" = 'Finished'",
            lifecycleConstraint.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"completed_at_utc\" >= \"started_at_utc\"",
            lifecycleConstraint.Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_BackfillsBeforeMakingProcessingStateRequired()
    {
        var migration = new AddDocumentProcessingLifecycle();
        var builder = new MigrationBuilder(
            "Npgsql.EntityFrameworkCore.PostgreSQL");
        var upMethod = typeof(AddDocumentProcessingLifecycle).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(upMethod);
        upMethod.Invoke(migration, [builder]);

        var addState = Assert.Single(
            builder.Operations.OfType<AddColumnOperation>(),
            operation => operation.Name == "processing_state");
        Assert.True(addState.IsNullable);

        var sqlOperations =
            builder.Operations.OfType<SqlOperation>().ToArray();
        Assert.Contains(
            sqlOperations,
            operation =>
                operation.Sql.Contains(
                    "WHEN outcome IS NULL THEN 'Pending'",
                    StringComparison.Ordinal)
                && operation.Sql.Contains(
                    "ELSE 'Finished'",
                    StringComparison.Ordinal)
                && operation.Sql.Contains(
                    "ELSE created_at_utc",
                    StringComparison.Ordinal));
        Assert.Contains(
            sqlOperations,
            operation =>
                operation.Sql.Contains(
                    "HAVING COUNT(*) > 1",
                    StringComparison.Ordinal));

        var makeStateRequired = Assert.Single(
            builder.Operations.OfType<AlterColumnOperation>(),
            operation => operation.Name == "processing_state");
        Assert.False(makeStateRequired.IsNullable);
        Assert.True(
            builder.Operations.IndexOf(makeStateRequired)
            > builder.Operations.IndexOf(sqlOperations[0]));

        var activeIndex = Assert.Single(
            builder.Operations.OfType<CreateIndexOperation>(),
            operation => operation.Name
                == "ux_document_processing_attempts_active_pre_quote_document_id");
        Assert.True(activeIndex.IsUnique);
        Assert.Equal(
            "\"processing_state\" IN ('Pending', 'Processing')",
            activeIndex.Filter);
    }

    [Fact]
    public void Migration_DownRestoresPreviousModel()
    {
        var migration = new AddDocumentProcessingLifecycle();
        var builder = new MigrationBuilder(
            "Npgsql.EntityFrameworkCore.PostgreSQL");
        var downMethod = typeof(AddDocumentProcessingLifecycle).GetMethod(
            "Down",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(downMethod);
        downMethod.Invoke(migration, [builder]);

        var droppedIndexes = builder.Operations
            .OfType<DropIndexOperation>()
            .Select(operation => operation.Name)
            .ToArray();
        Assert.Contains(
            "ix_document_processing_attempts_processing_state_created_at_utc",
            droppedIndexes);
        Assert.Contains(
            "ux_document_processing_attempts_active_pre_quote_document_id",
            droppedIndexes);
        Assert.Contains(
            builder.Operations.OfType<DropCheckConstraintOperation>(),
            operation => operation.Name
                == "ck_document_processing_attempts_lifecycle");
        Assert.Equal(
            ["processing_state", "started_at_utc"],
            builder.Operations
                .OfType<DropColumnOperation>()
                .Select(operation => operation.Name)
                .ToArray());
        var restoredIndex = Assert.Single(
            builder.Operations.OfType<CreateIndexOperation>());
        Assert.Equal(
            "ix_document_processing_attempts_pre_quote_document_id",
            restoredIndex.Name);
        Assert.False(restoredIndex.IsUnique);
        Assert.Contains(
            builder.Operations.OfType<AddCheckConstraintOperation>(),
            operation => operation.Name
                == "ck_document_processing_attempts_final_state");
        Assert.All(
            builder.Operations,
            operation =>
            {
                var tableOperation = operation as TableOperation;
                if (tableOperation is not null)
                {
                    Assert.Equal(
                        "document_processing_attempts",
                        tableOperation.Name);
                    Assert.Equal("core", tableOperation.Schema);
                }
            });
    }

    [Fact]
    public void ModelSnapshot_ContainsLifecycleModel()
    {
        var snapshotType = typeof(AddDocumentProcessingLifecycle).Assembly
            .GetType(
                "Infrastructure.Persistence.Migrations." +
                "ApplicationDbContextModelSnapshot");
        Assert.NotNull(snapshotType);
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(
            Activator.CreateInstance(snapshotType, nonPublic: true));
        var entity = Assert.IsAssignableFrom<IReadOnlyEntityType>(
            snapshot.Model.FindEntityType(
                typeof(DocumentProcessingAttempt)));
        var processingState = Assert.IsAssignableFrom<IReadOnlyProperty>(
            entity.FindProperty(
                nameof(DocumentProcessingAttempt.ProcessingState)));
        var startedAtUtc = Assert.IsAssignableFrom<IReadOnlyProperty>(
            entity.FindProperty(
                nameof(DocumentProcessingAttempt.StartedAtUtc)));

        Assert.False(processingState.IsNullable);
        Assert.True(startedAtUtc.IsNullable);
        Assert.Contains(
            entity.GetIndexes(),
            index => index.GetDatabaseName()
                == "ix_document_processing_attempts_processing_state_created_at_utc"
                && !index.IsUnique);
        Assert.Contains(
            entity.GetIndexes(),
            index => index.GetDatabaseName()
                == "ux_document_processing_attempts_active_pre_quote_document_id"
                && index.IsUnique
                && index.GetFilter()
                    == "\"processing_state\" IN ('Pending', 'Processing')");
        Assert.Contains(
            entity.GetCheckConstraints(),
            constraint => constraint.Name
                == "ck_document_processing_attempts_lifecycle");
    }
}
