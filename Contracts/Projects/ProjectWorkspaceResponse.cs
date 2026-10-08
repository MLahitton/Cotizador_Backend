namespace Contracts.Projects;

public sealed record ProjectWorkspaceProjectResponse(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectWorkspaceClientResponse(
    Guid Id,
    string ClientType,
    string LegalName,
    string? TradeName,
    string? Email,
    string? Phone,
    string? City);

public sealed record ProjectWorkspaceWorkflowResponse(
    string ResolutionState,
    Guid? PreQuoteId,
    Guid? RequirementId,
    string? RequirementStatus,
    Guid? TechnicalProposalId,
    bool HasTechnicalProposal);

public sealed record ProjectWorkspaceResponse(
    ProjectWorkspaceProjectResponse Project,
    ProjectWorkspaceClientResponse Client,
    ProjectWorkspaceWorkflowResponse Workflow);
