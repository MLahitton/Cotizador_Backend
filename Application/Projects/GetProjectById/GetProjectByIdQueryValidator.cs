using FluentValidation;

namespace Application.Projects.GetProjectById;

public sealed class GetProjectByIdQueryValidator
    : AbstractValidator<GetProjectByIdQuery>
{
    public GetProjectByIdQueryValidator()
    {
        RuleFor(query => query.ProjectId)
            .NotEmpty();
    }
}
