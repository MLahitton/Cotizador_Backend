using System.Collections;
using System.Linq.Expressions;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Npgsql;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class DocumentProcessingRepositoryTests
{
    private const string ActiveAttemptIndexName =
        "ux_document_processing_attempts_active_pre_quote_document_id";

    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid OtherDocumentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid UserId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid CorrelationId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HasActiveAttempt_WithPendingAttempt_ReturnsTrue()
    {
        var attempt = CreatePendingAttempt(DocumentId);
        using var context = CreateQueryContext([attempt], out var provider);
        var repository = new DocumentProcessingRepository(context);
        using var cancellationSource = new CancellationTokenSource();

        var result = await repository.HasActiveDocumentProcessingAttemptAsync(
            DocumentId,
            cancellationSource.Token);

        Assert.True(result);
        Assert.Equal(cancellationSource.Token, provider.CancellationToken);
        Assert.Empty(
            context.ChangeTracker.Entries<DocumentProcessingAttempt>());
    }

    [Fact]
    public async Task HasActiveAttempt_WithProcessingAttempt_ReturnsTrue()
    {
        var attempt = CreatePendingAttempt(DocumentId);
        attempt.Start(CreatedAtUtc.AddSeconds(1));
        using var context = CreateQueryContext([attempt], out _);
        var repository = new DocumentProcessingRepository(context);

        var result = await repository.HasActiveDocumentProcessingAttemptAsync(
            DocumentId,
            TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    [InlineData(DocumentProcessingOutcome.Failed)]
    public async Task HasActiveAttempt_WithFinishedAttempt_ReturnsFalse(
        DocumentProcessingOutcome outcome)
    {
        var attempt = CreateFinishedAttempt(DocumentId, outcome);
        using var context = CreateQueryContext([attempt], out _);
        var repository = new DocumentProcessingRepository(context);

        var result = await repository.HasActiveDocumentProcessingAttemptAsync(
            DocumentId,
            TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task HasActiveAttempt_ForDifferentDocument_ReturnsFalse()
    {
        var attempt = CreatePendingAttempt(OtherDocumentId);
        using var context = CreateQueryContext([attempt], out _);
        var repository = new DocumentProcessingRepository(context);

        var result = await repository.HasActiveDocumentProcessingAttemptAsync(
            DocumentId,
            TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task HasActiveAttempt_WithTerminalAndActiveAttempts_ReturnsTrue()
    {
        var attempts = new[]
        {
            CreateFinishedAttempt(
                DocumentId,
                DocumentProcessingOutcome.Completed),
            CreateFinishedAttempt(
                DocumentId,
                DocumentProcessingOutcome.RequiresReview),
            CreateFinishedAttempt(
                DocumentId,
                DocumentProcessingOutcome.Failed),
            CreatePendingAttempt(DocumentId)
        };
        using var context = CreateQueryContext(attempts, out _);
        var repository = new DocumentProcessingRepository(context);

        var result = await repository.HasActiveDocumentProcessingAttemptAsync(
            DocumentId,
            TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task SaveChanges_WithActiveAttemptUniqueViolation_ThrowsTypedConflict()
    {
        var postgresException = CreatePostgresException(
            PostgresErrorCodes.UniqueViolation,
            ActiveAttemptIndexName);
        var dbUpdateException = new DbUpdateException(
            "Controlled persistence failure.",
            postgresException);
        using var context = CreateSaveContext(dbUpdateException);
        var repository = new DocumentProcessingRepository(context);

        var exception =
            await Assert.ThrowsAsync<
                DocumentProcessingActiveAttemptConflictException>(
                () => repository.SaveChangesAsync(
                    TestContext.Current.CancellationToken));

        Assert.Same(dbUpdateException, exception.InnerException);
        Assert.Same(postgresException, dbUpdateException.InnerException);
        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            postgresException.SqlState);
        Assert.Equal(
            ActiveAttemptIndexName,
            postgresException.ConstraintName);
    }

    [Theory]
    [InlineData("different_constraint")]
    [InlineData("different_sql_state")]
    [InlineData("non_postgres_inner")]
    [InlineData("without_inner")]
    [InlineData("unrelated_postgres")]
    public async Task SaveChanges_WithUnrelatedFailure_ThrowsGeneralPersistenceError(
        string scenario)
    {
        var dbUpdateException = scenario switch
        {
            "different_constraint" => new DbUpdateException(
                "Controlled persistence failure.",
                CreatePostgresException(
                    PostgresErrorCodes.UniqueViolation,
                    "ux_other_constraint")),
            "different_sql_state" => new DbUpdateException(
                "Controlled persistence failure.",
                CreatePostgresException(
                    PostgresErrorCodes.ForeignKeyViolation,
                    ActiveAttemptIndexName)),
            "non_postgres_inner" => new DbUpdateException(
                "Controlled persistence failure.",
                new InvalidOperationException("Not PostgreSQL.")),
            "without_inner" => new DbUpdateException(
                "Controlled persistence failure."),
            "unrelated_postgres" => new DbUpdateException(
                "Controlled persistence failure.",
                CreatePostgresException(
                    PostgresErrorCodes.DeadlockDetected,
                    null)),
            _ => throw new InvalidOperationException()
        };
        using var context = CreateSaveContext(dbUpdateException);
        var repository = new DocumentProcessingRepository(context);

        var exception =
            await Assert.ThrowsAsync<DocumentProcessingPersistenceException>(
                () => repository.SaveChangesAsync(
                    TestContext.Current.CancellationToken));

        Assert.Same(dbUpdateException, exception.InnerException);
        Assert.IsNotType<
            DocumentProcessingActiveAttemptConflictException>(exception);
    }

    private static ApplicationDbContext CreateQueryContext(
        IReadOnlyCollection<DocumentProcessingAttempt> attempts,
        out TestAsyncQueryProvider queryProvider)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=query_double;Username=metadata")
            .Options;
        var context = new ApplicationDbContext(options);
        var queryable = attempts.AsQueryable();
        queryProvider = new TestAsyncQueryProvider(queryable.Provider);
        var dbSet = Substitute.For<
            DbSet<DocumentProcessingAttempt>,
            IQueryable<DocumentProcessingAttempt>,
            IAsyncEnumerable<DocumentProcessingAttempt>>();
        var substitutedQueryable =
            (IQueryable<DocumentProcessingAttempt>)dbSet;
        substitutedQueryable.Provider.Returns(queryProvider);
        substitutedQueryable.Expression.Returns(queryable.Expression);
        substitutedQueryable.ElementType.Returns(queryable.ElementType);
        substitutedQueryable.GetEnumerator()
            .Returns(_ => queryable.GetEnumerator());
        ((IAsyncEnumerable<DocumentProcessingAttempt>)dbSet)
            .GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(call => new TestAsyncEnumerator(
                queryable.GetEnumerator(),
                call.Arg<CancellationToken>()));

        _ = context.DocumentProcessingAttempts;
        var setsField = typeof(DbContext).GetField(
            "_sets",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(setsField);
        var sets = Assert.IsAssignableFrom<IDictionary>(
            setsField.GetValue(context));
        var key = Assert.Single(sets.Keys.Cast<object>());
        sets[key] = dbSet;

        return context;
    }

    private static ApplicationDbContext CreateSaveContext(
        DbUpdateException exception)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=save_double;Username=metadata")
            .AddInterceptors(new ThrowingSaveChangesInterceptor(exception))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static DocumentProcessingAttempt CreatePendingAttempt(
        Guid documentId)
    {
        return DocumentProcessingAttempt.Create(
            documentId,
            UserId,
            CorrelationId,
            CreatedAtUtc);
    }

    private static DocumentProcessingAttempt CreateFinishedAttempt(
        Guid documentId,
        DocumentProcessingOutcome outcome)
    {
        var attempt = CreatePendingAttempt(documentId);
        attempt.Start(CreatedAtUtc.AddSeconds(1));

        if (outcome == DocumentProcessingOutcome.Failed)
        {
            attempt.Fail("AI_SERVICE_TIMEOUT", CreatedAtUtc.AddSeconds(2));
        }
        else
        {
            attempt.Complete(outcome, CreatedAtUtc.AddSeconds(2));
        }

        return attempt;
    }

    private static PostgresException CreatePostgresException(
        string sqlState,
        string? constraintName)
    {
        return new PostgresException(
            messageText: "Controlled PostgreSQL failure.",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            constraintName: constraintName);
    }

    private sealed class ThrowingSaveChangesInterceptor(
        DbUpdateException exception)
        : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<InterceptionResult<int>>(exception);
        }
    }

    private sealed class TestAsyncQueryProvider(IQueryProvider innerProvider)
        : IAsyncQueryProvider
    {
        public CancellationToken CancellationToken { get; private set; }

        public IQueryable CreateQuery(Expression expression)
        {
            return innerProvider.CreateQuery(expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(
            Expression expression)
        {
            return innerProvider.CreateQuery<TElement>(expression);
        }

        public object? Execute(Expression expression)
        {
            return innerProvider.Execute(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            return innerProvider.Execute<TResult>(expression);
        }

        public TResult ExecuteAsync<TResult>(
            Expression expression,
            CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            var resultType = typeof(TResult).GenericTypeArguments.Single();
            var executeMethod = typeof(IQueryProvider)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(IQueryProvider.Execute)
                    && method.IsGenericMethod
                    && method.GetParameters().Length == 1)
                .MakeGenericMethod(resultType);
            var executionResult = executeMethod.Invoke(
                innerProvider,
                [expression]);
            var task = typeof(Task)
                .GetMethod(nameof(Task.FromResult))
                ?.MakeGenericMethod(resultType)
                .Invoke(null, [executionResult]);

            return Assert.IsAssignableFrom<TResult>(task);
        }
    }

    private sealed class TestAsyncEnumerator(
        IEnumerator<DocumentProcessingAttempt> inner,
        CancellationToken cancellationToken)
        : IAsyncEnumerator<DocumentProcessingAttempt>
    {
        public DocumentProcessingAttempt Current => inner.Current;

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(inner.MoveNext());
        }
    }
}
