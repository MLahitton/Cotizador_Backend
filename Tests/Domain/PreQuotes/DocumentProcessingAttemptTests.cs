using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class DocumentProcessingAttemptTests
{
    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid UserId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid CorrelationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CompletedAtUtc =
        new(2026, 7, 24, 12, 0, 5, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_CreatesOpenAttempt()
    {
        // Arrange
        var documentId = DocumentId;
        var userId = UserId;
        var correlationId = CorrelationId;
        var createdAtUtc = CreatedAtUtc;

        // Act
        var attempt = DocumentProcessingAttempt.Create(
            documentId,
            userId,
            correlationId,
            createdAtUtc);

        // Assert
        Assert.NotEqual(Guid.Empty, attempt.Id);
        Assert.Equal(documentId, attempt.PreQuoteDocumentId);
        Assert.Equal(userId, attempt.RequestedByUserId);
        Assert.Equal(correlationId, attempt.CorrelationId);
        Assert.Equal(createdAtUtc, attempt.CreatedAtUtc);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.Outcome);
        Assert.Null(attempt.ErrorCode);
    }

    [Fact]
    public void Create_WithEmptyCorrelationId_ThrowsArgumentException()
    {
        // Arrange
        var correlationId = Guid.Empty;

        // Act
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentProcessingAttempt.Create(
                DocumentId,
                UserId,
                correlationId,
                CreatedAtUtc));

        // Assert
        Assert.Equal("correlationId", exception.ParamName);
    }

    [Fact]
    public void Fail_WithValidData_FinalizesAttemptAsFailed()
    {
        // Arrange
        var attempt = DocumentProcessingAttempt.Create(
            DocumentId,
            UserId,
            CorrelationId,
            CreatedAtUtc);

        // Act
        attempt.Fail(
            "AI_SERVICE_TIMEOUT",
            CompletedAtUtc);

        // Assert
        Assert.Equal(
            DocumentProcessingOutcome.Failed,
            attempt.Outcome);
        Assert.Equal("AI_SERVICE_TIMEOUT", attempt.ErrorCode);
        Assert.Equal(CompletedAtUtc, attempt.CompletedAtUtc);
    }
}
