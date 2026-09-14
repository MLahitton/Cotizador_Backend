using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.Identity;
using FluentValidation;

namespace Application.PreQuotes.GetPreQuoteDocuments;

public sealed class GetPreQuoteDocumentsService(
    IValidator<GetPreQuoteDocumentsQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository,
    IPreQuoteRepository preQuoteRepository,
    IPreQuoteDocumentQueryRepository repository)
{
    public async Task<GetPreQuoteDocumentsResult> ExecuteAsync(
        GetPreQuoteDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.InactiveUser);
        }

        try
        {
            var preQuote = await preQuoteRepository.FindByIdAsync(
                query.PreQuoteId,
                cancellationToken);

            if (preQuote is null)
            {
                return GetPreQuoteDocumentsResult.Failed(
                    GetPreQuoteDocumentsFailure.NotFound);
            }

            /*
             * ADMIN puede consultar documentos de cualquier precotización.
             *
             * USER conserva la regla de ownership existente:
             * el proyecto de la precotización debe pertenecer al
             * usuario autenticado.
             *
             * El rol se toma del usuario leído desde la base de datos,
             * no únicamente del claim del JWT.
             */
            if (user.Role != UserRole.Admin)
            {
                var project = await projectRepository.FindByIdAsync(
                    preQuote.ProjectId,
                    cancellationToken);

                if (project is null
                    || project.CreatedByUserId != userId)
                {
                    return GetPreQuoteDocumentsResult.Failed(
                        GetPreQuoteDocumentsFailure.NotFound);
                }
            }

            var documents = await repository.GetDocumentsAsync(
                query.PreQuoteId,
                query.Page,
                query.PageSize,
                cancellationToken);

            return documents is null
                ? GetPreQuoteDocumentsResult.Failed(
                    GetPreQuoteDocumentsFailure.QueryError)
                : GetPreQuoteDocumentsResult.Success(
                    documents);
        }
        catch (PreQuoteDocumentQueryException)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.QueryError);
        }
        catch (ProjectQueryException)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.QueryError);
        }
        catch (PreQuoteQueryException)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.QueryError);
        }
    }
}