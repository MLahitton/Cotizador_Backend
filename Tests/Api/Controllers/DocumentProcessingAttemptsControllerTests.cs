using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Contracts.PreQuotes;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class DocumentProcessingAttemptsControllerTests
{
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AttemptId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_WithSuccess_ReturnsAcceptedAtGetWithPendingBody()
    {
        var context = new Context();

        var action = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var accepted = Assert.IsType<AcceptedAtActionResult>(action);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.Equal(nameof(DocumentProcessingAttemptsController.GetById), accepted.ActionName);
        Assert.Equal(DocumentId, accepted.RouteValues?["documentId"]);
        var body = Assert.IsType<DocumentProcessingAttemptStatusResponse>(
            accepted.Value);
        Assert.Equal(
            body.ProcessingAttemptId,
            accepted.RouteValues?["processingAttemptId"]);
        Assert.Equal(DocumentId, body.DocumentId);
        Assert.Equal("PENDING", body.ProcessingState);
        Assert.Null(body.Outcome);
        Assert.Null(body.ErrorCode);
        Assert.Null(body.StartedAtUtc);
        Assert.Null(body.CompletedAtUtc);
        Assert.Null(body.Result);
    }

    [Fact]
    public async Task Create_WithActiveAttempt_ReturnsSafeConflictWithoutLocation()
    {
        var context = new Context();
        context.Repository.HasActiveDocumentProcessingAttemptAsync(
                DocumentId,
                Arg.Any<CancellationToken>())
            .Returns(true);

        var action = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(
            "DOCUMENT_PROCESSING_ALREADY_ACTIVE",
            problem.Extensions["errorCode"]);
        Assert.False(problem.Extensions.ContainsKey("processingAttemptId"));
    }

    [Theory]
    [InlineData(
        "invalid_request",
        StatusCodes.Status400BadRequest,
        "Solicitud invalida",
        "El identificador del documento no es valido.",
        null)]
    [InlineData(
        "unauthorized",
        StatusCodes.Status401Unauthorized,
        "No autorizado",
        "No fue posible identificar al usuario autenticado.",
        null)]
    [InlineData(
        "inactive_user",
        StatusCodes.Status403Forbidden,
        "Usuario inactivo",
        "El usuario no tiene acceso para procesar documentos.",
        null)]
    [InlineData(
        "missing_document",
        StatusCodes.Status404NotFound,
        "Documento no encontrado",
        "No existe el documento de precotizacion indicado.",
        null)]
    [InlineData(
        "inactive_project",
        StatusCodes.Status409Conflict,
        "Proyecto inactivo",
        "No se pueden procesar documentos de un proyecto inactivo.",
        null)]
    [InlineData(
        "inactive_client",
        StatusCodes.Status409Conflict,
        "Cliente inactivo",
        "No se pueden procesar documentos para un cliente inactivo.",
        null)]
    [InlineData(
        "source_query",
        StatusCodes.Status500InternalServerError,
        "Error al consultar el documento",
        "No fue posible consultar el documento y su contexto.",
        null)]
    [InlineData(
        "active_query",
        StatusCodes.Status500InternalServerError,
        "Error al consultar el documento",
        "No fue posible consultar el documento y su contexto.",
        null)]
    [InlineData(
        "initial_persistence",
        StatusCodes.Status500InternalServerError,
        "Error al crear el intento",
        "No fue posible registrar el intento de procesamiento.",
        null)]
    [InlineData(
        "concurrent_active",
        StatusCodes.Status409Conflict,
        "Procesamiento ya activo",
        "El documento ya tiene un intento de procesamiento activo.",
        "DOCUMENT_PROCESSING_ALREADY_ACTIVE")]
    public async Task Create_WithFailure_ReturnsExactSafeProblem(
        string scenario,
        int statusCode,
        string title,
        string detail,
        string? errorCode)
    {
        var context = new Context();
        ConfigureCreateFailure(context, scenario);

        var action = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(statusCode, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(statusCode, problem.Status);
        Assert.Equal(title, problem.Title);
        Assert.Equal(detail, problem.Detail);
        Assert.False(problem.Extensions.ContainsKey("processingAttemptId"));

        if (errorCode is null)
        {
            Assert.False(problem.Extensions.ContainsKey("errorCode"));
            Assert.False(problem.Extensions.ContainsKey("documentId"));
        }
        else
        {
            Assert.Equal(errorCode, problem.Extensions["errorCode"]);
            Assert.Equal(DocumentId, problem.Extensions["documentId"]);
        }

        var serialized = System.Text.Json.JsonSerializer.Serialize(problem);
        Assert.DoesNotContain(
            "correlationId",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "storageKey",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "sql",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "exception",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "stackTrace",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "constraint",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<AcceptedAtActionResult>(action);
    }

    [Theory]
    [InlineData(DocumentProcessingState.Pending, null, "PENDING", null, false)]
    [InlineData(DocumentProcessingState.Processing, null, "PROCESSING", null, false)]
    [InlineData(DocumentProcessingState.Finished, DocumentProcessingOutcome.Completed, "FINISHED", "COMPLETED", true)]
    [InlineData(DocumentProcessingState.Finished, DocumentProcessingOutcome.RequiresReview, "FINISHED", "REQUIRES_REVIEW", true)]
    [InlineData(DocumentProcessingState.Finished, DocumentProcessingOutcome.Failed, "FINISHED", "FAILED", false)]
    public async Task GetById_ReturnsExactPublicState(
        DocumentProcessingState state,
        DocumentProcessingOutcome? outcome,
        string stateValue,
        string? outcomeValue,
        bool hasResult)
    {
        var context = new Context();
        context.Repository.FindAttemptStatusAsync(
                DocumentId,
                AttemptId,
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(new DocumentProcessingAttemptStatusSnapshot(
                AttemptId,
                DocumentId,
                state,
                outcome,
                outcome == DocumentProcessingOutcome.Failed ? "AI_SERVICE_TIMEOUT" : null,
                CreatedAt,
                state == DocumentProcessingState.Pending ? null : CreatedAt.AddSeconds(1),
                state == DocumentProcessingState.Finished ? CreatedAt.AddSeconds(2) : null,
                hasResult ? """{"schemaVersion":"1.0","pages":[{"text":"á😀"}]}""" : null));

        var action = await context.Controller.GetById(
            DocumentId,
            AttemptId,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(action);
        var body = Assert.IsType<DocumentProcessingAttemptStatusResponse>(
            ok.Value);
        Assert.Equal(stateValue, body.ProcessingState);
        Assert.Equal(outcomeValue, body.Outcome);
        Assert.Equal(hasResult, body.Result is not null);
        if (hasResult)
        {
            Assert.Equal(
                "á😀",
                body.Result?.GetProperty("pages")[0].GetProperty("text")
                    .GetString());
        }
    }

    [Theory]
    [InlineData("not_found", StatusCodes.Status404NotFound)]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized)]
    [InlineData("inactive", StatusCodes.Status403Forbidden)]
    [InlineData("query", StatusCodes.Status500InternalServerError)]
    public async Task GetById_WithFailure_ReturnsSafeProblem(
        string scenario,
        int statusCode)
    {
        var context = new Context();

        if (scenario == "unauthorized")
        {
            context.CurrentUser.IsAuthenticated.Returns(false);
        }
        else if (scenario == "inactive")
        {
            var user = Context.CreateUser();
            user.Deactivate(CreatedAt.AddSeconds(1));
            context.IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(user);
        }
        else if (scenario == "query")
        {
            context.Repository.FindAttemptStatusAsync(
                    DocumentId,
                    AttemptId,
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException<DocumentProcessingAttemptStatusSnapshot?>(
                    new DocumentProcessingQueryException(
                        new InvalidOperationException())));
        }

        var action = await context.Controller.GetById(
            DocumentId,
            AttemptId,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(statusCode, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Empty(problem.Extensions);
    }

    private static void ConfigureCreateFailure(
        Context context,
        string scenario)
    {
        switch (scenario)
        {
            case "invalid_request":
                context.Validator.ValidateAsync(
                        Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                        Arg.Any<CancellationToken>())
                    .Returns(new ValidationResult(
                    [
                        new ValidationFailure("DocumentId", "Required")
                    ]));
                break;
            case "unauthorized":
                context.CurrentUser.IsAuthenticated.Returns(false);
                break;
            case "inactive_user":
                var user = Context.CreateUser();
                user.Deactivate(CreatedAt.AddSeconds(1));
                context.IdentityRepository.FindUserByIdAsync(
                        UserId,
                        Arg.Any<CancellationToken>())
                    .Returns(user);
                break;
            case "missing_document":
                context.Repository.FindDocumentSourceAsync(
                        DocumentId,
                        Arg.Any<CancellationToken>())
                    .Returns((DocumentProcessingSource?)null);
                break;
            case "inactive_project":
                context.Repository.FindDocumentSourceAsync(
                        DocumentId,
                        Arg.Any<CancellationToken>())
                    .Returns(Context.CreateSource(
                        projectIsActive: false));
                break;
            case "inactive_client":
                context.Repository.FindDocumentSourceAsync(
                        DocumentId,
                        Arg.Any<CancellationToken>())
                    .Returns(Context.CreateSource(
                        clientIsActive: false));
                break;
            case "source_query":
                context.Repository.FindDocumentSourceAsync(
                        DocumentId,
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<DocumentProcessingSource?>(
                        new DocumentProcessingQueryException(
                            new InvalidOperationException())));
                break;
            case "active_query":
                context.Repository.HasActiveDocumentProcessingAttemptAsync(
                        DocumentId,
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<bool>(
                        new DocumentProcessingQueryException(
                            new InvalidOperationException())));
                break;
            case "initial_persistence":
                context.Repository.SaveChangesAsync(
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromException(
                        new DocumentProcessingPersistenceException(
                            new InvalidOperationException())));
                break;
            case "concurrent_active":
                context.Repository.SaveChangesAsync(
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromException(
                        new DocumentProcessingActiveAttemptConflictException(
                            new InvalidOperationException())));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    "Escenario no soportado.");
        }
    }

    private sealed class Context
    {
        public Context()
        {
            Validator = Substitute.For<IValidator<CreateDocumentProcessingAttemptCommand>>();
            CurrentUser = Substitute.For<ICurrentUser>();
            IdentityRepository = Substitute.For<IIdentityRepository>();
            Repository = Substitute.For<IDocumentProcessingRepository>();
            Validator.ValidateAsync(
                    Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ValidationResult());
            CurrentUser.IsAuthenticated.Returns(true);
            CurrentUser.UserId.Returns(UserId);
            IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateUser());
            Repository.FindDocumentSourceAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(new DocumentProcessingSource(
                    DocumentId,
                    Guid.NewGuid(),
                    "document.pdf",
                    "application/pdf",
                    100,
                    "prequotes/document.pdf",
                    Guid.NewGuid(),
                    true,
                    Guid.NewGuid(),
                    true));
            Repository.HasActiveDocumentProcessingAttemptAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(false);
            Repository.FindAttemptStatusAsync(
                    DocumentId,
                    AttemptId,
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns((DocumentProcessingAttemptStatusSnapshot?)null);
            Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var create = new CreateDocumentProcessingAttemptService(
                Validator,
                CurrentUser,
                IdentityRepository,
                Repository,
                new FixedTimeProvider(CreatedAt));
            var get = new GetDocumentProcessingAttemptService(
                CurrentUser,
                IdentityRepository,
                Repository);
            Controller = new DocumentProcessingAttemptsController(create, get);
        }

        public IValidator<CreateDocumentProcessingAttemptCommand> Validator { get; }
        public ICurrentUser CurrentUser { get; }
        public IIdentityRepository IdentityRepository { get; }
        public IDocumentProcessingRepository Repository { get; }
        public DocumentProcessingAttemptsController Controller { get; }

        public static User CreateUser() => User.CreateFromGoogle(
            "user@example.com",
            "Test",
            "User",
            null,
            CreatedAt);

        public static DocumentProcessingSource CreateSource(
            bool projectIsActive = true,
            bool clientIsActive = true)
        {
            return new DocumentProcessingSource(
                DocumentId,
                Guid.NewGuid(),
                "document.pdf",
                "application/pdf",
                100,
                "prequotes/document.pdf",
                Guid.NewGuid(),
                projectIsActive,
                Guid.NewGuid(),
                clientIsActive);
        }
    }
}
