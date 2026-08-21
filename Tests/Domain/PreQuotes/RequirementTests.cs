using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class RequirementTests
{
    private static readonly Guid PreQuoteId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid UserId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid RequirementId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid CorrelationId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset StartedAtUtc =
        CreatedAtUtc.AddSeconds(1);

    private static readonly DateTimeOffset CompletedAtUtc =
        StartedAtUtc.AddSeconds(4);

    [Fact]
    public void Requirement_Create_WithValidData_CreatesPendingActiveRequirement()
    {
        var requirement = Requirement.Create(
            PreQuoteId,
            UserId,
            CreatedAtUtc);

        Assert.NotEqual(Guid.Empty, requirement.Id);
        Assert.Equal(PreQuoteId, requirement.PreQuoteId);
        Assert.Equal(UserId, requirement.CreatedByUserId);
        Assert.Equal(RequirementStatus.Pending, requirement.Status);
        Assert.Equal(CreatedAtUtc, requirement.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, requirement.UpdatedAtUtc);
        Assert.True(requirement.IsActive);
        Assert.Empty(requirement.Files);
        Assert.Empty(requirement.ProcessingAttempts);
    }

    [Theory]
    [InlineData("preQuoteId")]
    [InlineData("createdByUserId")]
    public void Requirement_Create_WithRequiredEmptyId_ThrowsArgumentException(
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Requirement.Create(
                parameterName == "preQuoteId" ? Guid.Empty : PreQuoteId,
                parameterName == "createdByUserId" ? Guid.Empty : UserId,
                CreatedAtUtc));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Requirement_Create_WithNonUtcDate_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Requirement.Create(
                PreQuoteId,
                UserId,
                CreatedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Equal("createdAtUtc", exception.ParamName);
    }

    [Fact]
    public void RequirementFile_Create_WithValidData_CreatesFile()
    {
        var file = RequirementFile.Create(
            RequirementId,
            " planos.pdf ",
            " APPLICATION/PDF ",
            123,
            "requirements/1/original.pdf",
            CreatedAtUtc);

        Assert.NotEqual(Guid.Empty, file.Id);
        Assert.Equal(RequirementId, file.RequirementId);
        Assert.Equal("planos.pdf", file.OriginalFileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(123, file.SizeBytes);
        Assert.Equal("requirements/1/original.pdf", file.StorageKey);
        Assert.Equal(CreatedAtUtc, file.CreatedAtUtc);
    }

    [Fact]
    public void RequirementFile_Create_WithEmptyRequirementId_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RequirementFile.Create(
                Guid.Empty,
                "planos.pdf",
                "application/pdf",
                123,
                "requirements/1/original.pdf",
                CreatedAtUtc));

        Assert.Equal("requirementId", exception.ParamName);
    }

    [Theory]
    [InlineData(null, "application/pdf", "storage", "originalFileName")]
    [InlineData("planos.pdf", null, "storage", "contentType")]
    [InlineData("planos.pdf", "application/pdf", null, "storageKey")]
    [InlineData("", "application/pdf", "storage", "originalFileName")]
    [InlineData("planos.pdf", "", "storage", "contentType")]
    [InlineData("planos.pdf", "application/pdf", "", "storageKey")]
    public void RequirementFile_Create_WithRequiredTextMissing_ThrowsArgumentException(
        string? originalFileName,
        string? contentType,
        string? storageKey,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RequirementFile.Create(
                RequirementId,
                originalFileName!,
                contentType!,
                123,
                storageKey!,
                CreatedAtUtc));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RequirementFile_Create_WithInvalidSize_ThrowsArgumentException(
        long sizeBytes)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RequirementFile.Create(
                RequirementId,
                "planos.pdf",
                "application/pdf",
                sizeBytes,
                "requirements/1/original.pdf",
                CreatedAtUtc));

        Assert.Equal("sizeBytes", exception.ParamName);
    }

    [Fact]
    public void RequirementFile_Create_WithNonUtcDate_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RequirementFile.Create(
                RequirementId,
                "planos.pdf",
                "application/pdf",
                123,
                "requirements/1/original.pdf",
                CreatedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Equal("createdAtUtc", exception.ParamName);
    }

    [Fact]
    public void RequirementProcessingAttempt_Create_WithValidData_CreatesPendingAttempt()
    {
        var attempt = CreateAttempt();

        Assert.NotEqual(Guid.Empty, attempt.Id);
        Assert.Equal(RequirementId, attempt.RequirementId);
        Assert.Equal(UserId, attempt.RequestedByUserId);
        Assert.Equal(CorrelationId, attempt.CorrelationId);
        Assert.Equal(CreatedAtUtc, attempt.CreatedAtUtc);
        Assert.Equal(DocumentProcessingState.Pending, attempt.ProcessingState);
        Assert.Null(attempt.StartedAtUtc);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.Outcome);
        Assert.Null(attempt.ErrorCode);
    }

    [Theory]
    [InlineData("requirementId")]
    [InlineData("requestedByUserId")]
    [InlineData("correlationId")]
    public void RequirementProcessingAttempt_Create_WithRequiredEmptyId_ThrowsArgumentException(
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RequirementProcessingAttempt.Create(
                parameterName == "requirementId" ? Guid.Empty : RequirementId,
                parameterName == "requestedByUserId" ? Guid.Empty : UserId,
                parameterName == "correlationId" ? Guid.Empty : CorrelationId,
                CreatedAtUtc));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void RequirementProcessingAttempt_Create_WithNonUtcDate_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RequirementProcessingAttempt.Create(
                RequirementId,
                UserId,
                CorrelationId,
                CreatedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Equal("createdAtUtc", exception.ParamName);
    }

    [Fact]
    public void RequirementProcessingAttempt_Start_FromPending_MarksAsProcessing()
    {
        var attempt = CreateAttempt();

        attempt.Start(StartedAtUtc);

        Assert.Equal(DocumentProcessingState.Processing, attempt.ProcessingState);
        Assert.Equal(StartedAtUtc, attempt.StartedAtUtc);
        Assert.Null(attempt.Outcome);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.ErrorCode);
    }

    [Fact]
    public void RequirementProcessingAttempt_Start_Twice_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Start(StartedAtUtc.AddSeconds(1)));
    }

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public void RequirementProcessingAttempt_Complete_FromProcessing_FinalizesAttempt(
        DocumentProcessingOutcome outcome)
    {
        var attempt = CreateStartedAttempt();

        attempt.Complete(outcome, CompletedAtUtc);

        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(outcome, attempt.Outcome);
        Assert.Equal(CompletedAtUtc, attempt.CompletedAtUtc);
        Assert.Null(attempt.ErrorCode);
    }

    [Fact]
    public void RequirementProcessingAttempt_Fail_FromProcessing_FinalizesAttemptAsFailed()
    {
        var attempt = CreateStartedAttempt();

        attempt.Fail(" AI_INVALID_RESPONSE ", CompletedAtUtc);

        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Failed, attempt.Outcome);
        Assert.Equal("AI_INVALID_RESPONSE", attempt.ErrorCode);
        Assert.Equal(CompletedAtUtc, attempt.CompletedAtUtc);
    }

    [Fact]
    public void RequirementProcessingAttempt_Complete_FromPending_ThrowsInvalidOperationException()
    {
        var attempt = CreateAttempt();

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(DocumentProcessingOutcome.Completed, CompletedAtUtc));
    }

    [Fact]
    public void RequirementProcessingAttempt_Fail_FromPending_ThrowsInvalidOperationException()
    {
        var attempt = CreateAttempt();

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Fail("AI_INVALID_RESPONSE", CompletedAtUtc));
    }

    [Fact]
    public void RequirementProcessingAttempt_Complete_AfterFinished_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();
        attempt.Complete(DocumentProcessingOutcome.Completed, CompletedAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(
                DocumentProcessingOutcome.RequiresReview,
                CompletedAtUtc.AddSeconds(1)));
    }

    [Fact]
    public void RequirementProcessingAttempt_Fail_AfterFinished_ThrowsInvalidOperationException()
    {
        var attempt = CreateStartedAttempt();
        attempt.Complete(DocumentProcessingOutcome.Completed, CompletedAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Fail("AI_INVALID_RESPONSE", CompletedAtUtc.AddSeconds(1)));
    }

    [Fact]
    public void RequirementProcessingAttempt_Start_BeforeCreation_ThrowsArgumentException()
    {
        var attempt = CreateAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.Start(CreatedAtUtc.AddTicks(-1)));

        Assert.Equal("startedAtUtc", exception.ParamName);
    }

    [Fact]
    public void RequirementProcessingAttempt_Complete_BeforeStarted_ThrowsArgumentException()
    {
        var attempt = CreateStartedAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.Complete(
                DocumentProcessingOutcome.Completed,
                StartedAtUtc.AddTicks(-1)));

        Assert.Equal("completedAtUtc", exception.ParamName);
    }

    private static RequirementProcessingAttempt CreateAttempt() =>
        RequirementProcessingAttempt.Create(
            RequirementId,
            UserId,
            CorrelationId,
            CreatedAtUtc);

    private static RequirementProcessingAttempt CreateStartedAttempt()
    {
        var attempt = CreateAttempt();
        attempt.Start(StartedAtUtc);
        return attempt;
    }
}
