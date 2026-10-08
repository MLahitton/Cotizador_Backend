using FluentValidation;

namespace Application.Projects.GetProjectWorkspace;

public sealed class GetProjectWorkspaceQueryValidator
    : AbstractValidator<GetProjectWorkspaceQuery>
{
    public GetProjectWorkspaceQueryValidator()
    {
        RuleFor(query => query.ProjectId).NotEmpty();
    }
}
