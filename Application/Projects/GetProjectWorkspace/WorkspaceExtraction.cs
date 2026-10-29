namespace Application.Projects.GetProjectWorkspace;

// Read projection, not a persisted lifecycle or an execution permission.
public sealed record WorkspaceExtraction(string State, bool RefreshRecommended, bool CanAnalyze,
    Guid? TechnicalProposalId = null);

public interface IWorkspaceExtractionReader
{
    Task<ProjectWorkspaceWorkflowResult?> ReadCurrentAsync(Guid projectId, Guid preQuoteId, Guid actor,
        CancellationToken cancellationToken);
    Task<WorkspaceExtraction?> ReadAsync(Guid projectId, Guid requirementId, Guid actor,
        CancellationToken cancellationToken);
}
