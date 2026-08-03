using Application.PreQuotes;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class DocumentProcessingAttemptResultTests
{
    [Theory]
    [InlineData(CreateDocumentProcessingAttemptFailure.InvalidRequest)]
    [InlineData(CreateDocumentProcessingAttemptFailure.Unauthorized)]
    [InlineData(CreateDocumentProcessingAttemptFailure.InactiveUser)]
    [InlineData(CreateDocumentProcessingAttemptFailure.DocumentNotFound)]
    [InlineData(CreateDocumentProcessingAttemptFailure.InactiveProject)]
    [InlineData(CreateDocumentProcessingAttemptFailure.InactiveClient)]
    [InlineData(CreateDocumentProcessingAttemptFailure.QueryError)]
    [InlineData(CreateDocumentProcessingAttemptFailure.InitialPersistenceError)]
    [InlineData(CreateDocumentProcessingAttemptFailure.FinalPersistenceError)]
    [InlineData(CreateDocumentProcessingAttemptFailure.DocumentProcessingAlreadyActive)]
    public void CreateFailed_PreservesTypedFailureWithoutAttempt(
        CreateDocumentProcessingAttemptFailure failure)
    {
        var result = CreateDocumentProcessingAttemptResult.Failed(failure);

        Assert.False(result.IsSuccess);
        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Attempt);
    }

    [Fact]
    public void CreateFailed_WithNone_RejectsInvalidResult()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.None));

        Assert.Equal("failure", exception.ParamName);
    }

    [Fact]
    public void CreateSuccess_PreservesAttempt()
    {
        var attempt = CreateAttempt();

        var result = CreateDocumentProcessingAttemptResult.Success(attempt);

        Assert.True(result.IsSuccess);
        Assert.Equal(CreateDocumentProcessingAttemptFailure.None, result.Failure);
        Assert.Same(attempt, result.Attempt);
    }

    [Fact]
    public void CreateSuccess_WithNullAttempt_RejectsInvalidResult()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateDocumentProcessingAttemptResult.Success(null!));
    }

    [Theory]
    [InlineData(GetDocumentProcessingAttemptFailure.InvalidRequest)]
    [InlineData(GetDocumentProcessingAttemptFailure.Unauthorized)]
    [InlineData(GetDocumentProcessingAttemptFailure.InactiveUser)]
    [InlineData(GetDocumentProcessingAttemptFailure.AttemptNotFound)]
    [InlineData(GetDocumentProcessingAttemptFailure.QueryError)]
    public void GetFailed_PreservesTypedFailureWithoutAttempt(
        GetDocumentProcessingAttemptFailure failure)
    {
        var result = GetDocumentProcessingAttemptResult.Failed(failure);

        Assert.False(result.IsSuccess);
        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Attempt);
    }

    [Fact]
    public void GetSuccess_PreservesAttempt()
    {
        var attempt = CreateAttempt();

        var result = GetDocumentProcessingAttemptResult.Success(attempt);

        Assert.True(result.IsSuccess);
        Assert.Equal(GetDocumentProcessingAttemptFailure.None, result.Failure);
        Assert.Same(attempt, result.Attempt);
    }

    [Fact]
    public void GetFailed_WithNone_IsNotReportedAsSuccess()
    {
        var result = GetDocumentProcessingAttemptResult.Failed(
            GetDocumentProcessingAttemptFailure.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Attempt);
    }

    private static DocumentProcessingAttemptStatusData CreateAttempt()
    {
        return new DocumentProcessingAttemptStatusData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DocumentProcessingState.Pending,
            null,
            null,
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            null,
            null,
            null);
    }
}
