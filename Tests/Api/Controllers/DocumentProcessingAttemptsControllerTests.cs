using System.Reflection;
using System.Text.Json;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Contracts.PreQuotes;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class DocumentProcessingAttemptsControllerTests
{
    private static readonly Guid CorrelationId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static TheoryData<string, int, string> PersistedProcessingFailures =>
        new()
        {
            {
                "INVALID_REQUEST",
                StatusCodes.Status422UnprocessableEntity,
                "La solicitud enviada al servicio de procesamiento no fue válida."
            },
            {
                "INVALID_CORRELATION_ID",
                StatusCodes.Status422UnprocessableEntity,
                "La correlación del procesamiento no fue válida."
            },
            {
                "EMPTY_FILE",
                StatusCodes.Status422UnprocessableEntity,
                "El documento almacenado está vacío."
            },
            {
                "INVALID_PDF",
                StatusCodes.Status422UnprocessableEntity,
                "El documento almacenado no es un PDF válido."
            },
            {
                "PDF_PASSWORD_REQUIRED",
                StatusCodes.Status422UnprocessableEntity,
                "El documento PDF requiere una contraseña."
            },
            {
                "PDF_PAGE_LIMIT_EXCEEDED",
                StatusCodes.Status422UnprocessableEntity,
                "El documento supera la cantidad máxima de páginas permitida."
            },
            {
                "FILE_TOO_LARGE",
                StatusCodes.Status413PayloadTooLarge,
                "El documento PDF supera el tamaño máximo permitido."
            },
            {
                "UNSUPPORTED_FILE_TYPE",
                StatusCodes.Status415UnsupportedMediaType,
                "El tipo de archivo no es compatible con el procesamiento."
            },
            {
                "AI_INVALID_RESPONSE",
                StatusCodes.Status502BadGateway,
                "El servicio de procesamiento devolvió una respuesta inválida."
            },
            {
                "AI_SERVICE_ERROR",
                StatusCodes.Status502BadGateway,
                "El servicio de procesamiento presentó un error."
            },
            {
                "AI_SERVICE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable,
                "El servicio de procesamiento no está disponible."
            },
            {
                "AI_SERVICE_TIMEOUT",
                StatusCodes.Status504GatewayTimeout,
                "El servicio de procesamiento tardó demasiado en responder."
            },
            {
                "DOCUMENT_STORAGE_ERROR",
                StatusCodes.Status500InternalServerError,
                "No fue posible leer el documento almacenado."
            },
            {
                "UNKNOWN_PERSISTED_ERROR",
                StatusCodes.Status500InternalServerError,
                "No fue posible completar el procesamiento del documento."
            }
        };

    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid UserId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset FixedUtc =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public async Task Create_WithSuccessfulProcessing_ReturnsCreatedResponse(
        DocumentProcessingOutcome outcome)
    {
        var context = new ControllerActionContext();
        context.ConfigureSuccessfulProcessing(outcome);

        var actionResult = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        var response = Assert.IsType<CreateDocumentProcessingAttemptResponse>(
            objectResult.Value);
        Assert.Equal(outcome.ToString(), response.Outcome);
        Assert.IsNotType<ProblemDetails>(objectResult.Value);
    }

    [Theory]
    [MemberData(nameof(PersistedProcessingFailures))]
    public async Task Create_WithPersistedProcessingFailure_ReturnsExactProblemDetails(
        string errorCode,
        int expectedStatus,
        string expectedDetail)
    {
        var context = new ControllerActionContext();
        context.ConfigureProcessingFailure(errorCode);

        var actionResult = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.NotEqual(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal("No fue posible procesar el documento", problem.Title);
        Assert.Equal(expectedDetail, problem.Detail);
        Assert.Null(problem.Type);
        Assert.Null(problem.Instance);
        Assert.Equal(4, problem.Extensions.Count);
        Assert.Equal(errorCode, problem.Extensions["errorCode"]);
        Assert.Equal(DocumentId, problem.Extensions["documentId"]);
        Assert.Equal(
            context.AddedAttempt?.Id,
            problem.Extensions["processingAttemptId"]);
        Assert.Equal(
            context.AddedAttempt?.CorrelationId,
            problem.Extensions["correlationId"]);
    }

    [Theory]
    [InlineData("invalid_request", StatusCodes.Status400BadRequest)]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized)]
    [InlineData("inactive_user", StatusCodes.Status403Forbidden)]
    [InlineData("document_not_found", StatusCodes.Status404NotFound)]
    [InlineData("inactive_project", StatusCodes.Status409Conflict)]
    [InlineData("inactive_client", StatusCodes.Status409Conflict)]
    [InlineData("query_error", StatusCodes.Status500InternalServerError)]
    [InlineData(
        "initial_persistence",
        StatusCodes.Status500InternalServerError)]
    [InlineData(
        "final_persistence",
        StatusCodes.Status500InternalServerError)]
    public async Task Create_WithPreviousFailure_ReturnsProblemWithoutAttemptData(
        string scenario,
        int expectedStatus)
    {
        var context = new ControllerActionContext();
        context.ConfigurePreviousFailure(scenario);

        var actionResult = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.False(problem.Extensions.ContainsKey("errorCode"));
        Assert.False(problem.Extensions.ContainsKey("documentId"));
        Assert.False(problem.Extensions.ContainsKey("processingAttemptId"));
        Assert.False(problem.Extensions.ContainsKey("correlationId"));
    }

    [Fact]
    public async Task Create_WithActiveAttempt_ReturnsSafeConflict()
    {
        var context = new ControllerActionContext();
        context.ConfigurePreviousFailure("active_attempt");

        var actionResult = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Procesamiento ya activo", problem.Title);
        Assert.Equal(
            "El documento ya tiene un intento de procesamiento activo.",
            problem.Detail);
        Assert.Null(problem.Type);
        Assert.Null(problem.Instance);
        Assert.Equal(2, problem.Extensions.Count);
        Assert.Equal(
            "DOCUMENT_PROCESSING_ALREADY_ACTIVE",
            problem.Extensions["errorCode"]);
        Assert.Equal(DocumentId, problem.Extensions["documentId"]);
        Assert.False(
            problem.Extensions.ContainsKey("processingAttemptId"));
        Assert.False(problem.Extensions.ContainsKey("correlationId"));

        var json = JsonSerializer.Serialize(problem);
        Assert.DoesNotContain(
            "storageKey",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "prequotes/document.pdf",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "SELECT ",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "exception",
            json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithPersistedFailure_DoesNotExposeInternalInformation()
    {
        var context = new ControllerActionContext();
        context.ConfigureProcessingFailure("AI_SERVICE_ERROR");

        var actionResult = await context.Controller.Create(
            DocumentId,
            TestContext.Current.CancellationToken);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        var json = JsonSerializer.Serialize(problem);

        foreach (var forbiddenValue in new[]
        {
            "StorageKey",
            "prequotes/document.pdf",
            "BaseUrl",
            "http://localhost:8000",
            "remoteBody",
            "REMOTE_SECRET_MESSAGE",
            "stackTrace",
            "exception",
            "text",
            "bytes",
            "jwt",
            "fileName",
            "document.pdf"
        })
        {
            Assert.False(
                json.Contains(
                    forbiddenValue,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public void Post_DeclaresCreatedResponse_ForSuccessfulOutcome(
        DocumentProcessingOutcome outcome)
    {
        var action = GetPostAction();
        var createdResponse = action
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status201Created);

        Assert.Equal(StatusCodes.Status201Created, createdResponse.StatusCode);
        Assert.NotEqual(DocumentProcessingOutcome.Failed, outcome);
    }

    [Theory]
    [MemberData(nameof(PersistedProcessingFailures))]
    public void ProcessingFailure_MapsPersistedErrorToExactProblemDetails(
        string errorCode,
        int expectedStatus,
        string expectedDetail)
    {
        var attempt = CreateFailedAttemptResult(errorCode);

        var actionResult = InvokeProcessingFailureMapper(attempt);
        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal("No fue posible procesar el documento", problem.Title);
        Assert.Equal(expectedDetail, problem.Detail);
        Assert.Null(problem.Type);
        Assert.Null(problem.Instance);
        Assert.Equal(4, problem.Extensions.Count);
        Assert.Equal(errorCode, problem.Extensions["errorCode"]);
        Assert.NotNull(problem.Extensions["documentId"]);
        Assert.NotNull(problem.Extensions["processingAttemptId"]);
        Assert.Equal(CorrelationId, problem.Extensions["correlationId"]);
    }

    [Theory]
    [MemberData(nameof(PersistedProcessingFailures))]
    public void ProcessingFailure_DoesNotExposePayloadPathsOrRemoteBodies(
        string errorCode,
        int expectedStatus,
        string expectedDetail)
    {
        var attempt = CreateFailedAttemptResult(errorCode);
        var objectResult = Assert.IsType<ObjectResult>(
            InvokeProcessingFailureMapper(attempt));
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        var json = JsonSerializer.Serialize(problem);

        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(expectedDetail, problem.Detail);
        Assert.False(json.Contains("storageKey", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("filePath", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("payload", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("responseBody", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("stackTrace", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("CotizadorAi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Post_DeclaresAllPreviousAndProcessingFailureStatuses()
    {
        var statuses = GetPostAction()
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status201Created, statuses);
        Assert.Contains(StatusCodes.Status400BadRequest, statuses);
        Assert.Contains(StatusCodes.Status401Unauthorized, statuses);
        Assert.Contains(StatusCodes.Status403Forbidden, statuses);
        Assert.Contains(StatusCodes.Status404NotFound, statuses);
        Assert.Contains(StatusCodes.Status409Conflict, statuses);
        Assert.Contains(StatusCodes.Status413PayloadTooLarge, statuses);
        Assert.Contains(StatusCodes.Status415UnsupportedMediaType, statuses);
        Assert.Contains(StatusCodes.Status422UnprocessableEntity, statuses);
        Assert.Contains(StatusCodes.Status500InternalServerError, statuses);
        Assert.Contains(StatusCodes.Status502BadGateway, statuses);
        Assert.Contains(StatusCodes.Status503ServiceUnavailable, statuses);
        Assert.Contains(StatusCodes.Status504GatewayTimeout, statuses);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public void Post_DeclaresProblemDetails_ForPreviousFailureStatus(int statusCode)
    {
        var response = GetPostAction()
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == statusCode);

        Assert.Equal(typeof(ProblemDetails), response.Type);
    }

    [Fact]
    public void Post_AcceptsCancellationToken()
    {
        Assert.Contains(
            GetPostAction().GetParameters(),
            parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    private static MethodInfo GetPostAction()
    {
        return typeof(DocumentProcessingAttemptsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.GetCustomAttribute<HttpPostAttribute>() is not null);
    }

    private static IActionResult InvokeProcessingFailureMapper(
        CreatedDocumentProcessingAttemptResult attempt)
    {
        var mapper = typeof(DocumentProcessingAttemptsController).GetMethod(
            "CreateProcessingFailureResponse",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(mapper);

        var mapped = mapper.Invoke(null, [attempt]);
        return Assert.IsAssignableFrom<IActionResult>(mapped);
    }

    private static CreatedDocumentProcessingAttemptResult CreateFailedAttemptResult(
        string errorCode)
    {
        var constructor = typeof(CreatedDocumentProcessingAttemptResult)
            .GetConstructors()
            .Single();
        var createdAtUtc =
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var completedAtUtc =
            new DateTimeOffset(2026, 7, 25, 12, 1, 0, TimeSpan.Zero);
        var arguments = constructor
            .GetParameters()
            .Select<ParameterInfo, object?>(parameter =>
                parameter.Name?.ToUpperInvariant() switch
            {
                "ID" => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "DOCUMENTID" => Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                "CORRELATIONID" => CorrelationId,
                "OUTCOME" => DocumentProcessingOutcome.Failed,
                "ERRORCODE" => errorCode,
                "CREATEDATUTC" => createdAtUtc,
                "COMPLETEDATUTC" => completedAtUtc,
                _ => null
            })
            .ToArray();

        return Assert.IsType<CreatedDocumentProcessingAttemptResult>(
            constructor.Invoke(arguments));
    }

    private sealed class ControllerActionContext
    {
        private Action? beforeFinalSave;
        private int saveCount;

        public ControllerActionContext()
        {
            Validator = Substitute.For<
                IValidator<CreateDocumentProcessingAttemptCommand>>();
            CurrentUser = Substitute.For<ICurrentUser>();
            IdentityRepository = Substitute.For<IIdentityRepository>();
            Repository = Substitute.For<IDocumentProcessingRepository>();
            Storage = Substitute.For<IFileStorage>();
            Client = Substitute.For<IDocumentProcessingClient>();

            Validator.ValidateAsync(
                    Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ValidationResult());
            CurrentUser.IsAuthenticated.Returns(true);
            CurrentUser.UserId.Returns(UserId);
            IdentityRepository.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateActiveUser());
            Repository.FindDocumentSourceAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(_ => Source);
            Repository.HasActiveDocumentProcessingAttemptAsync(
                    DocumentId,
                    Arg.Any<CancellationToken>())
                .Returns(false);
            Repository.When(repository => repository.AddAttempt(
                    Arg.Any<DocumentProcessingAttempt>()))
                .Do(call => AddedAttempt =
                    call.Arg<DocumentProcessingAttempt>());
            Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    saveCount++;

                    if (saveCount == 3)
                    {
                        beforeFinalSave?.Invoke();
                    }

                    return Task.CompletedTask;
                });
            Storage.OpenReadAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<Stream>(
                    new MemoryStream([1, 2, 3, 4])));

            ConfigureSuccessfulProcessing(
                DocumentProcessingOutcome.Completed);

            var service = new CreateDocumentProcessingAttemptService(
                Validator,
                CurrentUser,
                IdentityRepository,
                Repository,
                Storage,
                Client);
            Controller = new DocumentProcessingAttemptsController(service);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddControllers();
            Controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services.BuildServiceProvider()
                }
            };
        }

        public IValidator<CreateDocumentProcessingAttemptCommand> Validator
        {
            get;
        }

        public ICurrentUser CurrentUser { get; }

        public IIdentityRepository IdentityRepository { get; }

        public IDocumentProcessingRepository Repository { get; }

        public IFileStorage Storage { get; }

        public IDocumentProcessingClient Client { get; }

        public DocumentProcessingAttemptsController Controller { get; }

        public DocumentProcessingAttempt? AddedAttempt { get; private set; }

        public DocumentProcessingSource Source { get; set; } = CreateSource();

        public void ConfigureSuccessfulProcessing(
            DocumentProcessingOutcome outcome)
        {
            Client.ProcessAsync(
                    Arg.Any<DocumentProcessingClientRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(
                    CreateSuccessfulClientResult(outcome)));
        }

        public void ConfigureProcessingFailure(string errorCode)
        {
            if (errorCode == "DOCUMENT_STORAGE_ERROR")
            {
                Storage.OpenReadAsync(
                        Arg.Any<string>(),
                        Arg.Any<CancellationToken>())
                    .Returns(
                        Task.FromException<Stream>(
                            new FileStorageReadException(
                                new IOException("Storage unavailable."))));
                return;
            }

            if (errorCode == "UNKNOWN_PERSISTED_ERROR")
            {
                Client.ProcessAsync(
                        Arg.Any<DocumentProcessingClientRequest>(),
                        Arg.Any<CancellationToken>())
                    .Returns(_ => Task.FromResult(
                        DocumentProcessingClientResult.Failed(
                            DocumentProcessingClientFailure.Timeout)));
                beforeFinalSave = () =>
                {
                    var errorCodeProperty =
                        typeof(DocumentProcessingAttempt).GetProperty(
                            nameof(DocumentProcessingAttempt.ErrorCode));

                    Assert.NotNull(errorCodeProperty);
                    Assert.NotNull(AddedAttempt);
                    errorCodeProperty.SetValue(AddedAttempt, errorCode);
                };
                return;
            }

            var clientResult = errorCode switch
            {
                "AI_INVALID_RESPONSE" =>
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.InvalidResponse),
                "AI_SERVICE_ERROR" =>
                    DocumentProcessingClientResult.RemoteFailure(
                        DocumentProcessingClientFailure.ServiceError,
                        new DocumentProcessingRemoteError(
                            500,
                            "1.0",
                            "INTERNAL_SERVER_ERROR",
                            "REMOTE_SECRET_MESSAGE")),
                "AI_SERVICE_UNAVAILABLE" =>
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.ServiceUnavailable),
                "AI_SERVICE_TIMEOUT" =>
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.Timeout),
                _ => DocumentProcessingClientResult.RemoteFailure(
                    DocumentProcessingClientFailure.RemoteRejection,
                    new DocumentProcessingRemoteError(
                        422,
                        "1.0",
                        errorCode,
                        "Remote message."))
            };

            Client.ProcessAsync(
                    Arg.Any<DocumentProcessingClientRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(clientResult));
        }

        public void ConfigurePreviousFailure(string scenario)
        {
            switch (scenario)
            {
                case "invalid_request":
                    Validator.ValidateAsync(
                            Arg.Any<CreateDocumentProcessingAttemptCommand>(),
                            Arg.Any<CancellationToken>())
                        .Returns(
                            new ValidationResult(
                            [
                                new ValidationFailure(
                                    "DocumentId",
                                    "DocumentId is required.")
                            ]));
                    break;
                case "unauthorized":
                    CurrentUser.IsAuthenticated.Returns(false);
                    break;
                case "inactive_user":
                    var user = CreateActiveUser();
                    user.Deactivate(FixedUtc.AddMinutes(1));
                    IdentityRepository.FindUserByIdAsync(
                            UserId,
                            Arg.Any<CancellationToken>())
                        .Returns(user);
                    break;
                case "document_not_found":
                    Repository.FindDocumentSourceAsync(
                            DocumentId,
                            Arg.Any<CancellationToken>())
                        .Returns((DocumentProcessingSource?)null);
                    break;
                case "inactive_project":
                    Source = CreateSource(projectIsActive: false);
                    break;
                case "inactive_client":
                    Source = CreateSource(clientIsActive: false);
                    break;
                case "query_error":
                    Repository.FindDocumentSourceAsync(
                            DocumentId,
                            Arg.Any<CancellationToken>())
                        .Returns(
                            Task.FromException<DocumentProcessingSource?>(
                                new DocumentProcessingQueryException(
                                    new InvalidOperationException(
                                        "Query failed."))));
                    break;
                case "active_attempt":
                    Repository.HasActiveDocumentProcessingAttemptAsync(
                            DocumentId,
                            Arg.Any<CancellationToken>())
                        .Returns(true);
                    break;
                case "initial_persistence":
                    Repository.SaveChangesAsync(
                            Arg.Any<CancellationToken>())
                        .Returns(
                            Task.FromException(
                                new DocumentProcessingPersistenceException(
                                    new InvalidOperationException(
                                        "Write failed."))));
                    break;
                case "final_persistence":
                    var currentSave = 0;
                    Repository.SaveChangesAsync(
                            Arg.Any<CancellationToken>())
                        .Returns(_ =>
                        {
                            currentSave++;
                            return currentSave < 3
                                ? Task.CompletedTask
                                : Task.FromException(
                                    new DocumentProcessingPersistenceException(
                                        new InvalidOperationException(
                                            "Write failed.")));
                        });
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        private static User CreateActiveUser()
        {
            return User.CreateFromGoogle(
                "user@example.com",
                "Test",
                "User",
                null,
                FixedUtc);
        }

        private static DocumentProcessingSource CreateSource(
            bool projectIsActive = true,
            bool clientIsActive = true)
        {
            return new DocumentProcessingSource(
                DocumentId,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "document.pdf",
                "application/pdf",
                4,
                "prequotes/document.pdf",
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                projectIsActive,
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                clientIsActive);
        }

        private static DocumentProcessingClientResult CreateSuccessfulClientResult(
            DocumentProcessingOutcome outcome)
        {
            var requiresOcr =
                outcome == DocumentProcessingOutcome.RequiresReview;
            var classification = requiresOcr
                ? PdfClassification.PdfScanned
                : PdfClassification.PdfText;
            IReadOnlyList<ProcessingWarningData> warnings = requiresOcr
                ? [
                    new ProcessingWarningData(
                        "OCR_REQUIRED",
                        "The document does not contain extractable text.",
                        [1])
                ]
                : [];

            return DocumentProcessingClientResult.Success(
                new DocumentProcessingResponseData(
                    "1.0",
                    DocumentId,
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    outcome,
                    new ProcessedDocumentData(
                        "document.pdf",
                        "application/pdf",
                        4,
                        1,
                        classification,
                        requiresOcr),
                    [
                        new ProcessedPageData(
                            1,
                            requiresOcr ? string.Empty : "Page 1",
                            requiresOcr ? 0 : 6,
                            !requiresOcr)
                    ],
                    warnings,
                    new ProcessingMetadataData("pymupdf", 15),
                    "{}"));
        }
    }
}
