using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Domain.Clients;
using FluentValidation;

namespace Application.Projects.GetClientProjects;

public sealed class GetClientProjectsService(
    IValidator<GetClientProjectsQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IClientRepository clientRepository,
    IProjectRepository projectRepository)
{
    public async Task<GetClientProjectsResult> ExecuteAsync(
        GetClientProjectsQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.InactiveUser);
        }

        Client? client;

        try
        {
            client = await clientRepository.FindByIdAsync(
                query.ClientId,
                cancellationToken);
        }
        catch (ClientQueryException)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.QueryError);
        }

        if (client is null)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.ClientNotFound);
        }

        if (!client.IsActive)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.InactiveClient);
        }

        var search = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : query.Search.Trim();

        ProjectSearchPage projectsPage;

        try
        {
            projectsPage =
                await projectRepository.SearchActiveByClientAsync(
                    client.Id,
                    search,
                    query.Page,
                    query.PageSize,
                    cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return GetClientProjectsResult.Failed(
                GetClientProjectsFailure.QueryError);
        }

        var totalPages = projectsPage.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                projectsPage.TotalCount / (double)query.PageSize);

        var items = projectsPage.Items
            .Select(project => new ProjectListItemResult(
                project.Id,
                project.ClientId,
                project.Code,
                project.Name,
                project.Description,
                project.Location,
                project.IsActive,
                project.CreatedAtUtc,
                project.UpdatedAtUtc))
            .ToArray();

        return GetClientProjectsResult.Success(
            new ClientProjectsPageResult(
                items,
                query.Page,
                query.PageSize,
                projectsPage.TotalCount,
                totalPages));
    }
}
