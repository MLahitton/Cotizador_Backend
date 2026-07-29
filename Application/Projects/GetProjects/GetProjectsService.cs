using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Projects;
using Domain.Clients;
using FluentValidation;

namespace Application.Projects.GetProjects;

public sealed class GetProjectsService(
    IValidator<GetProjectsQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository)
{
    public async Task<GetProjectsResult> ExecuteAsync(
        GetProjectsQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validation.IsValid)
        {
            return GetProjectsResult.Failed(
                GetProjectsFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetProjectsResult.Failed(
                GetProjectsFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetProjectsResult.Failed(
                GetProjectsFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetProjectsResult.Failed(
                GetProjectsFailure.InactiveUser);
        }

        var status = string.IsNullOrWhiteSpace(query.Status)
            ? "all"
            : query.Status.Trim().ToLowerInvariant();

        bool? isActive = status switch
        {
            "active" => true,
            "inactive" => false,
            "all" => null,
            _ => throw new InvalidOperationException(
                "El estado de proyecto validado no es reconocido.")
        };

        AdministrativeProjectSearchPage projects;

        try
        {
            projects = await projectRepository.SearchAsync(
                new ProjectSearchCriteria(
                    Normalize(query.Search),
                    isActive,
                    query.ClientId,
                    ParseOptional<ClientType>(query.ClientType),
                    ParseOptional<ClientDocumentType>(
                        query.DocumentType),
                    query.Page,
                    query.PageSize),
                cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return GetProjectsResult.Failed(
                GetProjectsFailure.QueryError);
        }

        var totalPages = projects.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                projects.TotalCount / (double)query.PageSize);

        var items = projects.Items
            .Select(project =>
                new AdministrativeProjectListItemResult(
                    project.Id,
                    project.ClientId,
                    project.Code,
                    project.Name,
                    project.Description,
                    project.Location,
                    project.IsActive,
                    project.CreatedAtUtc,
                    project.UpdatedAtUtc,
                    new ProjectClientSummaryResult(
                        project.ClientId,
                        project.ClientType,
                        project.ClientLegalName,
                        project.ClientTradeName,
                        project.ClientDocumentType,
                        project.ClientDocumentNumber)))
            .ToArray();

        return GetProjectsResult.Success(
            new ProjectsPageResult(
                items,
                query.Page,
                query.PageSize,
                projects.TotalCount,
                totalPages));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static TEnum? ParseOptional<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.Parse<TEnum>(value.Trim(), true);
    }
}
