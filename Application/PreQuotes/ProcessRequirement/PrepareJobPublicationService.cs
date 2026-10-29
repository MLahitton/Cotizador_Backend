using System.Text.Json;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.BuildRequirementTechnicalProposal;
using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed record JobPublicationPreparationContext(Guid RequirementId, Guid ProcessingAttemptId,
    RequirementCommercialLine CommercialLine, Guid PrimaryDocumentId, IReadOnlyList<PublicationFile> Files);

public sealed class ExecutionPreparationRejectedException(ExecutionDocumentPreparationFailure failure) : Exception("Execution preparation was rejected.")
{
    public ExecutionDocumentPreparationFailure Failure { get; } = failure;
}

// Explicitly invoked only: no extraction client, state writes or publication dependency.
public sealed class PrepareJobPublicationService(RequirementExtractionMapper mapper, BuildRequirementTechnicalProposalService builder,
    IExecutionDocumentConsumptionAuthorization? authorization = null)
{
    public async Task<PreparedJobPublication> PrepareAsync(VerifiedExecutionDocumentResult result,
        RequirementCommercialLine commercialLine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Execution is { Permit: not null } execution)
        {
            if (authorization is null) throw new ExecutionPreparationRejectedException(ExecutionDocumentPreparationFailure.AuthorizationConflict);
            var allowed = await authorization.AuthorizePreparationAsync(result, execution, result.Evidence.ActorUserId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowed.IsAuthorized) throw new ExecutionPreparationRejectedException(allowed.Failure);
        }
        var evidence = result.Evidence;
        var context = new JobPublicationPreparationContext(evidence.RequirementId, evidence.ProcessingAttemptId,
            commercialLine, evidence.PrimaryDocumentId, evidence.Files.Select(f => new PublicationFile(f.FileId,
                f.OriginalFileName, f.ContentType, f.DeclaredLengthBytes, f.StorageKey, f.FileCreatedAtUtc)).ToArray());
        var graph = await PrepareAsync(context, result.ReadResponse(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return PreparedJobPublication.FromVerifiedResult(graph, result);
    }

    public async Task<PreparedJobPublication> PrepareAsync(JobPublicationPreparationContext context,
        DocumentProcessingResponseData response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        if (context.RequirementId == Guid.Empty || context.ProcessingAttemptId == Guid.Empty
            || response.Outcome is not (DocumentProcessingOutcome.Completed or DocumentProcessingOutcome.RequiresReview)
            || response.StructuredExtraction is null)
            throw new ArgumentException("A started execution and successful canonical response are required.");
        using var payload = JsonDocument.Parse(response.PayloadJson);
        if (response.ProcessingAttemptId != context.ProcessingAttemptId || response.DocumentId != context.PrimaryDocumentId
            || payload.RootElement.GetProperty("processingAttemptId").GetGuid() != context.ProcessingAttemptId
            || payload.RootElement.GetProperty("documentId").GetGuid() != context.PrimaryDocumentId)
            throw new ArgumentException("Canonical response belongs to another execution/document.");
        var files = context.Files.OrderBy(f => f.CreatedAtUtc).ThenBy(f => f.FileId).ToArray();
        if (files.Length == 0 || files[0].FileId != context.PrimaryDocumentId)
            throw new ArgumentException("Principal document differs from the existing file ordering.");
        var s = response.StructuredExtraction;
        // These transient identities/times are never stored in the prepared contract.
        var extraction = RequirementExtractionResult.Create(context.ProcessingAttemptId, response.SchemaVersion,
            response.Provider.ToString(), response.PayloadJson, s.ItemCount, s.ItemsRequiringReview,
            s.Issues.Count, s.Conflicts.Count, s.ProcessingMethod, s.DurationMs, DateTimeOffset.UnixEpoch);
        var items = mapper.CreateExtractedItems(context.RequirementId, extraction.Id, s.Items, DateTimeOffset.UnixEpoch);
        var proposal = await builder.BuildAsync(context.RequirementId, context.CommercialLine, extraction,
            items.Select(i => i.Item).ToArray(), cancellationToken);
        var outcome = response.Outcome == DocumentProcessingOutcome.RequiresReview
            || proposal.Status == RequirementTechnicalProposalStatus.RequiresReview
                ? DocumentProcessingOutcome.RequiresReview : DocumentProcessingOutcome.Completed;
        return PreparedJobPublication.Create(new(PreparedJobPublication.Version, files, context.PrimaryDocumentId,
            context.CommercialLine, response.Provider, response.Outcome, outcome,
            new(response.SchemaVersion, payload.RootElement, s.ItemCount, s.ItemsRequiringReview, s.Issues.Count,
                s.Conflicts.Count, s.ProcessingMethod, s.DurationMs, items.Select(i => JobPublicationGraph.Snapshot(i.Item)).ToArray()),
            new(proposal.Status, proposal.Items.Select(JobPublicationGraph.Snapshot).ToArray())));
    }
}
