namespace Application.Projects.GetProjectWorkspace;

public enum ProjectWorkspaceResolutionState
{
    Empty = 1,
    Resolved = 2,
    Ambiguous = 3
}

public enum GetProjectWorkspaceFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    QueryError = 5
}

public sealed record ProjectWorkspaceProjectResult(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectWorkspaceClientResult(
    Guid Id,
    string ClientType,
    string LegalName,
    string? TradeName,
    string? Email,
    string? Phone,
    string? City);

public sealed record ProjectWorkspaceWorkflowResult(
    ProjectWorkspaceResolutionState ResolutionState,
    Guid? PreQuoteId,
    Guid? RequirementId,
    string? RequirementStatus,
    Guid? TechnicalProposalId,
    bool HasTechnicalProposal);

public sealed record ProjectWorkspaceResult(
    ProjectWorkspaceProjectResult Project,
    ProjectWorkspaceClientResult Client,
    ProjectWorkspaceWorkflowResult Workflow);

public sealed record GetProjectWorkspaceResult(
    GetProjectWorkspaceFailure Failure,
    ProjectWorkspaceResult? Workspace)
{
    public bool IsSuccess => Failure == GetProjectWorkspaceFailure.None;

    public static GetProjectWorkspaceResult Success(
        ProjectWorkspaceResult workspace) =>
        new(GetProjectWorkspaceFailure.None, workspace);

    public static GetProjectWorkspaceResult Failed(
        GetProjectWorkspaceFailure failure) =>
        new(failure, null);
}
