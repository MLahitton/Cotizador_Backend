using System.Text.Json;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.ProcessRequirement;

// An explicit transport contract: await completion of all stream consumption before returning or throwing.
public interface IVerifiedExecutionDocumentClient : IAi2DocumentProcessingClient;

public sealed record ExecutionDocumentConsumptionAuthorization(ExecutionDocumentPreparationFailure Failure,
    Guid CorrelationId = default, Guid ProjectId = default)
{
    public bool IsAuthorized => Failure == ExecutionDocumentPreparationFailure.None;
}

public interface IExecutionDocumentConsumptionAuthorization
{
    Task<ExecutionDocumentConsumptionAuthorization> AuthorizePreparationAsync(VerifiedExecutionDocumentResult result,
        AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) =>
        Task.FromResult(new ExecutionDocumentConsumptionAuthorization(ExecutionDocumentPreparationFailure.AuthorizationConflict));

    Task<ExecutionDocumentConsumptionAuthorization> AuthorizeConsumptionAsync(PreparedExecutionDocuments package,
        AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken);
}

public enum ExecutionDocumentConsumptionState { AlreadyConsumed, Rejected, ValidResponse, InvalidResponse, ProviderFailure }

public sealed record ExecutionDocumentConsumptionResult(ExecutionDocumentConsumptionState State,
    bool ClientInvoked = false, ExecutionDocumentPreparationFailure Rejection = ExecutionDocumentPreparationFailure.None,
    DocumentProcessingClientFailure ProviderFailure = DocumentProcessingClientFailure.None,
    VerifiedExecutionDocumentResult? Result = null);

// No public constructor or deserialization entry: the association comes only from the winning consumer.
public sealed class VerifiedExecutionDocumentResult
{
    private readonly string responseJson;
    public RequirementExecutionEvidence Evidence { get; }
    internal AssignedJobExecution? Execution { get; }

    internal VerifiedExecutionDocumentResult(RequirementExecutionEvidence evidence, DocumentProcessingResponseData response,
        AssignedJobExecution? execution = null)
    {
        Evidence = evidence;
        Execution = execution;
        responseJson = JsonSerializer.Serialize(response);
    }

    // Each caller receives an independent graph, with no borrowed JsonDocument or streams.
    public DocumentProcessingResponseData ReadResponse() => JsonSerializer.Deserialize<DocumentProcessingResponseData>(responseJson)!;
}

public sealed class ConsumeExecutionDocumentsService(IExecutionDocumentConsumptionAuthorization authorization,
    IVerifiedExecutionDocumentClient client, ILogger<ConsumeExecutionDocumentsService> logger)
{
    public async Task<ExecutionDocumentConsumptionResult> ConsumeAsync(PreparedExecutionDocuments package,
        AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        // Reservation belongs to the package, not this service. Even a cancelled winner burns its opportunity.
        if (!package.TryReserveConsumption()) return new(ExecutionDocumentConsumptionState.AlreadyConsumed);
        var readers = new List<Stream>();
        var invoked = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authority = await authorization.AuthorizeConsumptionAsync(package, execution, actorUserId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!authority.IsAuthorized) return new(ExecutionDocumentConsumptionState.Rejected, Rejection: authority.Failure);
            var evidence = package.Evidence;
            var files = new List<DocumentProcessingFile>();
            try
            {
                foreach (var file in evidence.Files)
                {
                    var reader = new ExecutionDocumentReadStream(package.OpenConsumptionReader(file.FileId), file.ObservedLengthBytes);
                    readers.Add(reader);
                    files.Add(new(file.FileId, file.OriginalFileName, file.ContentType, file.ObservedLengthBytes, reader));
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return new(ExecutionDocumentConsumptionState.Rejected, Rejection: ExecutionDocumentPreparationFailure.InvalidDocuments);
            }
            var request = new DocumentProcessingClientRequest(evidence.PrimaryDocumentId, evidence.ProcessingAttemptId,
                authority.CorrelationId, files.AsReadOnly(), authority.ProjectId, evidence.RequirementId);
            cancellationToken.ThrowIfCancellationRequested();
            invoked = true;
            var response = await client.ProcessAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccess)
                return new(response.Failure == DocumentProcessingClientFailure.InvalidResponse
                    ? ExecutionDocumentConsumptionState.InvalidResponse : ExecutionDocumentConsumptionState.ProviderFailure,
                    true, ProviderFailure: response.Failure);
            if (!Matches(response.Response!, evidence)) return new(ExecutionDocumentConsumptionState.InvalidResponse, true);
            var result = new VerifiedExecutionDocumentResult(evidence, response.Response!, execution);
            cancellationToken.ThrowIfCancellationRequested();
            return new(ExecutionDocumentConsumptionState.ValidResponse, true, Result: result);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception)
        {
            return invoked
                ? new(ExecutionDocumentConsumptionState.ProviderFailure, true, ProviderFailure: DocumentProcessingClientFailure.ServiceUnavailable)
                : new(ExecutionDocumentConsumptionState.Rejected, Rejection: ExecutionDocumentPreparationFailure.PersistenceError);
        }
        finally
        {
            // Fence further reads and drain any read still in flight before disposing the package.
            foreach (var reader in readers)
            {
                try { await reader.DisposeAsync(); }
                catch (Exception error) { CleanupWarning(error); }
            }
            try { await package.DisposeAsync(); }
            catch (Exception error) { CleanupWarning(error); }
        }
    }

    private static bool Matches(DocumentProcessingResponseData response, RequirementExecutionEvidence evidence)
    {
        if (response.DocumentId != evidence.PrimaryDocumentId || response.ProcessingAttemptId != evidence.ProcessingAttemptId
            || response.Outcome is not (DocumentProcessingOutcome.Completed or DocumentProcessingOutcome.RequiresReview)
            || response.StructuredExtraction is null) return false;
        try
        {
            using var json = JsonDocument.Parse(response.PayloadJson);
            return json.RootElement.GetProperty("documentId").GetGuid() == evidence.PrimaryDocumentId
                && json.RootElement.GetProperty("processingAttemptId").GetGuid() == evidence.ProcessingAttemptId;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        { return false; }
    }

    private void CleanupWarning(Exception error)
    {
        try { logger.LogWarning("Execution reader disposal failed: {ErrorType}", error.GetType().Name); }
        catch (Exception) { /* Diagnostics must not replace the primary result. */ }
    }
}
