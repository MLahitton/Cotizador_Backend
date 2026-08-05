using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class DocumentProcessingDiagnosticsTests
{
    [Fact]
    public void ContractRejected_LogsGlassContractContext()
    {
        var logger = new RecordingLogger<DocumentProcessingDiagnostics>();
        var diagnostics = new DocumentProcessingDiagnostics(logger);
        var documentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var attemptId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var correlationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        diagnostics.ContractRejected(
            documentId,
            attemptId,
            correlationId,
            200,
            "glass_contract",
            "unknown_code",
            7,
            "UNKNOWN",
            ["LAM_5_5", "LAM_4_4"]);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(
            "Document processing response rejected. DocumentId={DocumentId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} HttpStatusCode={HttpStatusCode} Stage={Stage} Category={Category} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage} JsonPath={JsonPath} FieldName={FieldName} ItemSequence={ItemSequence} RejectedValue={RejectedValue} RejectedNormalizedCode={RejectedNormalizedCode} AcceptedNormalizedCodes={AcceptedNormalizedCodes}",
            entry.Properties["{OriginalFormat}"]);
        Assert.Equal(documentId, entry.Properties["DocumentId"]);
        Assert.Equal(attemptId, entry.Properties["ProcessingAttemptId"]);
        Assert.Equal(correlationId, entry.Properties["CorrelationId"]);
        Assert.Equal(200, entry.Properties["HttpStatusCode"]);
        Assert.Equal("glass_contract", entry.Properties["Stage"]);
        Assert.Equal("unknown_code", entry.Properties["Category"]);
        Assert.Equal(7, entry.Properties["ItemSequence"]);
        Assert.Equal("UNKNOWN", entry.Properties["RejectedNormalizedCode"]);
        Assert.Equal(
            "LAM_4_4,LAM_5_5",
            entry.Properties["AcceptedNormalizedCodes"]);
    }

    private sealed record LogEntry(
        LogLevel Level,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties =
                state as IReadOnlyList<KeyValuePair<string, object?>>;

            Entries.Add(new(
                logLevel,
                properties?.ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal)
                ?? new Dictionary<string, object?>(
                    StringComparer.Ordinal)));
        }
    }
}
