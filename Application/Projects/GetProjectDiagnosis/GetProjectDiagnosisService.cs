using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Projects;
using Domain.Identity;

namespace Application.Projects.GetProjectDiagnosis;

public sealed class GetProjectDiagnosisService(
    ICurrentUser currentUser, IIdentityRepository identityRepository,
    IProjectRepository projectRepository, IProjectDiagnosisSourceRepository sources)
{
    public async Task<GetProjectDiagnosisResult> ExecuteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty) return new(GetProjectDiagnosisFailure.InvalidRequest);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return new(GetProjectDiagnosisFailure.Unauthorized);

        var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null) return new(GetProjectDiagnosisFailure.Unauthorized);
        if (!user.IsActive) return new(GetProjectDiagnosisFailure.InactiveUser);
        try
        {
            var project = await projectRepository.FindByIdAsync(projectId, cancellationToken);
            // Same read policy as Workspace: conceal projects belonging to another user.
            if (project is null || (user.Role != UserRole.Admin && project.CreatedByUserId != userId))
                return new(GetProjectDiagnosisFailure.NotFound);

            var rows = await sources.FindByProjectIdAsync(projectId, cancellationToken);
            var result = rows.Count switch
            {
                0 => new ProjectDiagnosisResult("NO_PUBLIC_EXPERIENCE"),
                1 => ProjectDiagnosisBuilder.Build(rows[0]),
                _ => new ProjectDiagnosisResult("AMBIGUOUS_PUBLIC_EXPERIENCE")
            };
            return new(GetProjectDiagnosisFailure.None, result);
        }
        catch (ProjectQueryException) { return new(GetProjectDiagnosisFailure.QueryError); }
        catch (ProjectDiagnosisQueryException) { return new(GetProjectDiagnosisFailure.QueryError); }
    }
}
