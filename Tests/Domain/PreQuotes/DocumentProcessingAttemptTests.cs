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

    private static readonly DateTimeOffset StartedAtUtc =
        CreatedAtUtc.AddSeconds(1);

    private static readonly DateTimeOffset CompletedAtUtc =
        StartedAtUtc.AddSeconds(4);

    [Fact]
    public void Create_WithValidData_CreatesPendingAttempt()
    {
        var attempt = CreateAttempt();

        Assert.NotEqual(Guid.Empty, attempt.Id);
        Assert.Equal(DocumentId, attempt.PreQuoteDocumentId);
        Assert.Equal(UserId, attempt.RequestedByUserId);
        Assert.Equal(CorrelationId, attempt.CorrelationId);
        Assert.Equal(CreatedAtUtc, attempt.CreatedAtUtc);
        Assert.Equal(DocumentProcessingState.Pending, attempt.ProcessingState);
        Assert.Null(attempt.StartedAtUtc);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.Outcome);
        Assert.Null(attempt.ErrorCode);
    }

    [Fact]
    public void Create_WithEmptyCorrelationId_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentProcessingAttempt.Create(
                DocumentId,
                UserId,
                Guid.Empty,
                CreatedAtUtc));

        Assert.Equal("correlationId", exception.ParamName);
    }

    [Fact]
    public void Create_WithNonUtcDate_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentProcessingAttempt.Create(
                DocumentId,
                UserId,
                CorrelationId,
                CreatedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Equal("createdAtUtc", exception.ParamName);
    }

    [Fact]
    public void Start_FromPending_MarksAttemptAsProcessing()
    {
        var attempt = CreateAttempt();

        attempt.Start(StartedAtUtc);

        Assert.Equal(
            DocumentProcessingState.Processing,
            attempt.ProcessingState);
        Assert.Equal(StartedAtUtc, attempt.StartedAtUtc);
        Assert.Null(attempt.Outcome);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.ErrorCode);
    }

    [Fact]
    public void Start_Twice_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();

        Assert.Throws<InvalidOperationException>(
            () => attempt.Start(StartedAtUtc.AddSeconds(1)));
    }

    [Fact]
    public void Start_AfterFinished_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();
        attempt.Fail("AI_SERVICE_TIMEOUT", CompletedAtUtc);

        Assert.Throws<InvalidOperationException>(
            () => attempt.Start(CompletedAtUtc.AddSeconds(1)));
    }

    [Fact]
    public void Start_WithNonUtcDate_ThrowsArgumentException()
    {
        var attempt = CreateAttempt();

        var exception = Assert.Throws<ArgumentException>(
            () => attempt.Start(
                StartedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Equal("startedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Start_BeforeCreation_ThrowsArgumentException()
    {
        var attempt = CreateAttempt();

        var exception = Assert.Throws<ArgumentException>(
            () => attempt.Start(CreatedAtUtc.AddTicks(-1)));

        Assert.Equal("startedAtUtc", exception.ParamName);
    }

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public void Complete_FromProcessing_FinalizesAttempt(
        DocumentProcessingOutcome outcome)
    {
        var attempt = CreateStartedAttempt();

        attempt.Complete(outcome, CompletedAtUtc);

        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(outcome, attempt.Outcome);
        Assert.Equal(StartedAtUtc, attempt.StartedAtUtc);
        Assert.Equal(CompletedAtUtc, attempt.CompletedAtUtc);
        Assert.Null(attempt.ErrorCode);
    }

    [Fact]
    public void Complete_FromPending_ThrowsInvalidOperationException()
    {
        var attempt = CreateAttempt();

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(
                DocumentProcessingOutcome.Completed,
                CompletedAtUtc));
    }

    [Fact]
    public void Complete_Twice_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();
        attempt.Complete(
            DocumentProcessingOutcome.Completed,
            CompletedAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(
                DocumentProcessingOutcome.Completed,
                CompletedAtUtc.AddSeconds(1)));
    }

    [Fact]
    public void Fail_FromProcessing_FinalizesAttemptAsFailed()
    {
        var attempt = CreateStartedAttempt();

        attempt.Fail("AI_SERVICE_TIMEOUT", CompletedAtUtc);

        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Failed, attempt.Outcome);
        Assert.Equal("AI_SERVICE_TIMEOUT", attempt.ErrorCode);
        Assert.Equal(CompletedAtUtc, attempt.CompletedAtUtc);
    }

    [Fact]
    public void Fail_FromPending_ThrowsInvalidOperationException()
    {
        var attempt = CreateAttempt();

        Assert.Throws<InvalidOperationException>(
            () => attempt.Fail("AI_SERVICE_TIMEOUT", CompletedAtUtc));
    }

    [Fact]
    public void Fail_AfterCompleted_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();
        attempt.Complete(
            DocumentProcessingOutcome.Completed,
            CompletedAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Fail(
                "AI_SERVICE_TIMEOUT",
                CompletedAtUtc.AddSeconds(1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_WithInvalidErrorCode_ThrowsArgumentException(
        string? errorCode)
    {
        var attempt = CreateStartedAttempt();

        Assert.Throws<ArgumentException>(
            () => attempt.Fail(errorCode!, CompletedAtUtc));
    }

    [Fact]
    public void Fail_NormalizesErrorCode()
    {
        var attempt = CreateStartedAttempt();

        attempt.Fail(" AI_SERVICE_TIMEOUT ", CompletedAtUtc);

        Assert.Equal("AI_SERVICE_TIMEOUT", attempt.ErrorCode);
    }

    [Fact]
    public void Complete_WithNonUtcDate_ThrowsArgumentException()
    {
        var attempt = CreateStartedAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.Complete(
                DocumentProcessingOutcome.Completed,
                CompletedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Equal("completedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Complete_BeforeStarted_ThrowsArgumentException()
    {
        var attempt = CreateStartedAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.Complete(
                DocumentProcessingOutcome.Completed,
                StartedAtUtc.AddTicks(-1)));

        Assert.Equal("completedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Fail_WithNonUtcDate_ThrowsArgumentException()
    {
        var attempt = CreateStartedAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.Fail(
                "AI_SERVICE_TIMEOUT",
                CompletedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Equal("completedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Fail_BeforeStarted_ThrowsArgumentException()
    {
        var attempt = CreateStartedAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.Fail(
                "AI_SERVICE_TIMEOUT",
                StartedAtUtc.AddTicks(-1)));

        Assert.Equal("completedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Complete_AfterFailed_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();
        attempt.Fail("AI_SERVICE_TIMEOUT", CompletedAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(
                DocumentProcessingOutcome.Completed,
                CompletedAtUtc.AddSeconds(1)));
    }

    private static DocumentProcessingAttempt CreateAttempt()
    {
        return DocumentProcessingAttempt.Create(
            DocumentId,
            UserId,
            CorrelationId,
            CreatedAtUtc);
    }

    private static DocumentProcessingAttempt CreateStartedAttempt()
    {
        var attempt = CreateAttempt();
        attempt.Start(StartedAtUtc);
        return attempt;
    }
}
