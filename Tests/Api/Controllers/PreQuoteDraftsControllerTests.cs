using System.Text.Json;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ApprovePreQuoteDraft;
using Application.PreQuotes.CreatePreQuoteDraft;
using Application.PreQuotes.GetPreQuoteDraft;
using Application.PreQuotes.UpdatePreQuoteDraft;
using Contracts.Common;
using Contracts.PreQuotes;
using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class PreQuoteDraftsControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid ExtractionId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("success", 201)]
    [InlineData("invalid", 400)]
    [InlineData("unauthorized", 401)]
    [InlineData("inactive", 403)]
    [InlineData("not_found", 404)]
    [InlineData("duplicate", 409)]
    [InlineData("query", 500)]
    [InlineData("persistence", 500)]
    public async Task Create_MapsApplicationResult(string scenario, int status)
    {
        var context = CreateContext();
        ConfigureCommon(context, scenario);
        if (scenario == "duplicate")
        {
            context.Repository.ExistsAsync(
                    PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(true);
        }
        if (scenario == "not_found")
        {
            context.Repository.FindSourceAsync(
                    PreQuoteId, DocumentId, ExtractionId,
                    Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((PreQuoteDraftSourceContext?)null);
        }
        if (scenario == "query")
        {
            context.Repository.ExistsAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns<Task<bool>>(_ => throw QueryException());
        }
        if (scenario == "persistence")
        {
            context.Repository.SaveChangesAsync(
                    Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw PersistenceException());
        }

        var result = await context.Controller.Create(
            scenario == "invalid" ? Guid.Empty : PreQuoteId,
            new CreatePreQuoteDraftRequest(DocumentId, ExtractionId),
            TestContext.Current.CancellationToken);

        AssertStatus(result, status);
    }

    [Theory]
    [InlineData("success", 200)]
    [InlineData("unauthorized", 401)]
    [InlineData("inactive", 403)]
    [InlineData("not_found", 404)]
    [InlineData("query", 500)]
    public async Task Get_MapsApplicationResult(string scenario, int status)
    {
        var context = CreateContext();
        ConfigureCommon(context, scenario);
        if (scenario == "not_found")
        {
            context.Repository.FindReadAsync(
                    PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((PreQuoteDraft?)null);
        }
        if (scenario == "query")
        {
            context.Repository.FindReadAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns<Task<PreQuoteDraft?>>(_ => throw QueryException());
        }

        var result = await context.Controller.Get(
            PreQuoteId,
            TestContext.Current.CancellationToken);

        AssertStatus(result, status);
    }

    [Theory]
    [InlineData("success", 200)]
    [InlineData("invalid", 400)]
    [InlineData("unauthorized", 401)]
    [InlineData("inactive", 403)]
    [InlineData("not_found", 404)]
    [InlineData("version", 409)]
    [InlineData("approved", 409)]
    [InlineData("query", 500)]
    [InlineData("persistence", 500)]
    public async Task Update_MapsApplicationResult(string scenario, int status)
    {
        var draft = scenario == "approved" ? CreateApprovedDraft() : CreateDraft();
        var context = CreateContext(draft);
        ConfigureCommon(context, scenario);
        if (scenario == "not_found")
        {
            context.Repository.FindForUpdateAsync(
                    PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((PreQuoteDraft?)null);
        }
        if (scenario == "query")
        {
            context.Repository.FindForUpdateAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns<Task<PreQuoteDraft?>>(
                    _ => throw QueryException());
        }
        if (scenario == "persistence")
        {
            context.Repository.SaveChangesAsync(
                    Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw PersistenceException());
        }
        var request = UpdateRequest(
            draft,
            scenario == "version" ? 99 : draft.Version);

        var result = await context.Controller.Update(
            scenario == "invalid" ? Guid.Empty : PreQuoteId,
            request,
            TestContext.Current.CancellationToken);

        AssertStatus(result, status);
        if (scenario == "version")
        {
            var problem = AssertProblem(result, 409);
            Assert.Equal("Conflicto de concurrencia", problem.Title);
            Assert.Equal(
                "El borrador fue modificado por otro usuario. Consulte nuevamente la version actual antes de guardar.",
                problem.Detail);
        }
    }

    [Fact]
    public async Task Update_ResolvingPendingIssue_ReturnsInReviewVersionTwo()
    {
        var draft = CreateDraft();
        var context = CreateContext(draft);

        var result = await context.Controller.Update(
            PreQuoteId,
            UpdateRequest(draft, 1),
            TestContext.Current.CancellationToken);

        var response = Assert.IsType<PreQuoteDraftDetailsResponse>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("IN_REVIEW", response.Status);
        Assert.Equal(2, response.Version);
        Assert.Equal("RESOLVED", response.Issues.Single().ResolutionStatus);
    }

    [Theory]
    [InlineData("success", 200)]
    [InlineData("invalid", 400)]
    [InlineData("unauthorized", 401)]
    [InlineData("inactive", 403)]
    [InlineData("not_found", 404)]
    [InlineData("version", 409)]
    [InlineData("inactive_project", 409)]
    [InlineData("inactive_client", 409)]
    [InlineData("pending_issue", 409)]
    [InlineData("query", 500)]
    [InlineData("persistence", 500)]
    public async Task Approve_MapsApplicationResult(string scenario, int status)
    {
        var draft = scenario == "pending_issue"
            ? CreateDraft()
            : CreateReadyDraft();
        var context = CreateContext(draft);
        ConfigureCommon(context, scenario);
        if (scenario == "not_found")
        {
            context.Repository.FindForUpdateAsync(
                    PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((PreQuoteDraft?)null);
        }
        if (scenario == "inactive_project")
        {
            context.Repository.FindActivityAsync(
                    PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new PreQuoteDraftActivityContext(false, true));
        }
        if (scenario == "inactive_client")
        {
            context.Repository.FindActivityAsync(
                    PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new PreQuoteDraftActivityContext(true, false));
        }
        if (scenario == "query")
        {
            context.Repository.FindActivityAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns<Task<PreQuoteDraftActivityContext?>>(
                    _ => throw QueryException());
        }
        if (scenario == "persistence")
        {
            context.Repository.SaveChangesAsync(
                    Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw PersistenceException());
        }

        var result = await context.Controller.Approve(
            scenario == "invalid" ? Guid.Empty : PreQuoteId,
            new ApprovePreQuoteDraftRequest(
                scenario == "version" ? 99 : draft.Version),
            TestContext.Current.CancellationToken);

        AssertStatus(result, status);
    }

    [Fact]
    public async Task Get_SerializesOnlyPublicContract()
    {
        var draft = CreateReadyDraft();
        var context = CreateContext(draft);

        var action = await context.Controller.Get(
            PreQuoteId,
            TestContext.Current.CancellationToken);
        var response = Assert.IsType<PreQuoteDraftDetailsResponse>(
            Assert.IsType<OkObjectResult>(action).Value);
        var json = JsonSerializer.Serialize(response);

        Assert.Equal("IN_REVIEW", response.Status);
        Assert.Equal("AI", response.Items[0].Origin);
        Assert.Equal("RESOLVED", response.Issues[0].ResolutionStatus);
        Assert.Equal(2, response.Version);
        Assert.NotNull(response.Summary);
        Assert.NotNull(response.Audit);
        foreach (var forbidden in new[]
        {
            "payloadJson", "storageKey", "sourceStructuredItemId",
            "sourceStructuredRequirementId",
            "sourceStructuredDocumentReferenceId",
            "sourceStructuredIssueId", "sourceStructuredConflictId"
        })
        {
            Assert.DoesNotContain(
                forbidden,
                json,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task QueryError_ReturnsGenericProblemWithoutInternals()
    {
        var context = CreateContext();
        context.Repository.FindReadAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<PreQuoteDraft?>>(_ => throw QueryException());

        var result = await context.Controller.Get(
            PreQuoteId,
            TestContext.Current.CancellationToken);
        var problem = AssertProblem(result, 500);
        var json = JsonSerializer.Serialize(problem);

        Assert.Equal("Error al consultar borrador", problem.Title);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sql", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
    }

    private static Context CreateContext(PreQuoteDraft? draft = null)
    {
        draft ??= CreateDraft();
        var current = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var repository = Substitute.For<IPreQuoteDraftRepository>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(CreateUser());
        repository.ExistsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.FindSourceAsync(
                PreQuoteId, DocumentId, ExtractionId,
                Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(CreateSource());
        repository.FindReadAsync(
                PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(draft);
        repository.FindForUpdateAsync(
                PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(draft);
        repository.FindActivityAsync(
                PreQuoteId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDraftActivityContext(true, true));
        var clock = new FixedProvider(At.AddHours(1));
        var controller = new PreQuoteDraftsController(
            new CreatePreQuoteDraftService(
                new CreatePreQuoteDraftCommandValidator(),
                current, identity, repository, clock),
            new GetPreQuoteDraftService(
                new GetPreQuoteDraftQueryValidator(),
                current, identity, repository),
            new UpdatePreQuoteDraftService(
                new UpdatePreQuoteDraftCommandValidator(),
                current, identity, repository, clock),
            new ApprovePreQuoteDraftService(
                new ApprovePreQuoteDraftCommandValidator(),
                current, identity, repository, clock));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return new Context(current, identity, repository, controller);
    }

    private static void ConfigureCommon(Context context, string scenario)
    {
        if (scenario == "unauthorized")
        {
            context.Current.IsAuthenticated.Returns(false);
        }
        else if (scenario == "inactive")
        {
            var user = CreateUser();
            user.Deactivate(At.AddMinutes(1));
            context.Identity.FindUserByIdAsync(
                    UserId, Arg.Any<CancellationToken>())
                .Returns(user);
        }
    }

    private static UpdatePreQuoteDraftRequest UpdateRequest(
        PreQuoteDraft draft,
        int expectedVersion) =>
        new(
            expectedVersion,
            new("Project", "Client", "Location"),
            draft.Items.OrderBy(x => x.Sequence).Select(x =>
                new PreQuoteDraftItemRequest(
                    x.Id, x.Sequence, x.Reference, x.Description,
                    "WINDOW", x.RawMeasurements, 100, 100, 1, true)).ToArray(),
            draft.Requirements.OrderBy(x => x.Sequence).Select(x =>
                new PreQuoteDraftRequirementRequest(
                    x.Id, x.Sequence, "GENERAL_NOTE", x.Value, true)).ToArray(),
            draft.DocumentReferences.OrderBy(x => x.Sequence).Select(x =>
                new PreQuoteDraftDocumentReferenceRequest(
                    x.Id, x.Sequence, x.Reference, x.Description,
                    x.Detail, 1, true)).ToArray(),
            draft.Issues.Select(x => new PreQuoteDraftIssueResolutionRequest(
                x.Id, "RESOLVED", "resolved")).ToArray(),
            draft.Conflicts.Select(x => new PreQuoteDraftConflictResolutionRequest(
                x.Id, "DISMISSED", "dismissed")).ToArray());

    private static PreQuoteDraft CreateReadyDraft()
    {
        var draft = CreateDraft();
        var request = UpdateRequest(draft, 1);
        draft.Update(
            1,
            request.Project.Name,
            request.Project.ClientName,
            request.Project.Location,
            request.Items.Select(x => new PreQuoteDraftItemEdit(
                x.DraftItemId, x.Sequence, x.Reference, x.Description,
                StructuredElementType.Window, x.RawMeasurements,
                x.WidthMillimeters, x.HeightMillimeters, x.Quantity,
                x.IsIncluded)).ToArray(),
            request.Requirements.Select(x => new PreQuoteDraftRequirementEdit(
                x.DraftRequirementId, x.Sequence,
                RequirementCategory.GeneralNote, x.Value,
                x.IsIncluded)).ToArray(),
            request.DocumentReferences.Select(x => new PreQuoteDraftReferenceEdit(
                x.DraftDocumentReferenceId, x.Sequence, x.Reference,
                x.Description, x.Detail, x.Quantity, x.IsIncluded)).ToArray(),
            request.Issues.Select(x => new PreQuoteDraftResolutionEdit(
                x.DraftIssueId, PreQuoteDraftResolutionStatus.Resolved,
                x.ResolutionNote)).ToArray(),
            request.Conflicts.Select(x => new PreQuoteDraftResolutionEdit(
                x.DraftConflictId, PreQuoteDraftResolutionStatus.Dismissed,
                x.ResolutionNote)).ToArray(),
            UserId,
            At.AddMinutes(1));
        return draft;
    }

    private static PreQuoteDraft CreateApprovedDraft()
    {
        var draft = CreateReadyDraft();
        draft.Approve(2, UserId, At.AddMinutes(2));
        return draft;
    }

    private static PreQuoteDraft CreateDraft() => PreQuoteDraft.Create(
        PreQuoteId, DocumentId, ExtractionId, "Project", "Client",
        "Location", UserId, At,
        [new(Guid.NewGuid(), 1, "I-1", "Item",
            StructuredElementType.Window, null, 100, 100, 1)],
        [new(Guid.NewGuid(), 1, RequirementCategory.GeneralNote, "Note")],
        [new(Guid.NewGuid(), 1, "R-1", "Reference", null, 1)],
        [new(Guid.NewGuid(), 1, StructuredIssueCode.OcrReviewRequired,
            "Issue", 1, [1])],
        [new(Guid.NewGuid(), 1,
            StructuredConflictCode.DuplicateItemReference,
            "Conflict", [1], [1])]);

    private static PreQuoteDraftSourceContext CreateSource()
    {
        var draft = CreateDraft();
        return new(
            PreQuoteId, DocumentId, ExtractionId, true, true,
            "Project", "Client", "Location",
            draft.Items.Select(x => new PreQuoteDraftItemSource(
                x.SourceStructuredItemId!.Value, x.Sequence, x.Reference,
                x.Description, x.ElementType, x.RawMeasurements,
                x.WidthMillimeters, x.HeightMillimeters, x.Quantity)).ToArray(),
            draft.Requirements.Select(x => new PreQuoteDraftRequirementSource(
                x.SourceStructuredRequirementId!.Value, x.Sequence,
                x.Category, x.Value)).ToArray(),
            draft.DocumentReferences.Select(x => new PreQuoteDraftReferenceSource(
                x.SourceStructuredDocumentReferenceId!.Value, x.Sequence,
                x.Reference, x.Description, x.Detail, x.Quantity)).ToArray(),
            draft.Issues.Select(x => new PreQuoteDraftIssueSource(
                x.SourceStructuredIssueId, x.Sequence, x.Code, x.Message,
                x.ItemSequence, x.PageNumbers)).ToArray(),
            draft.Conflicts.Select(x => new PreQuoteDraftConflictSource(
                x.SourceStructuredConflictId, x.Sequence, x.Code, x.Message,
                x.ItemSequences, x.PageNumbers)).ToArray());
    }

    private static User CreateUser() => User.CreateFromGoogle(
        "user@example.com", "Test", "User", null, At);
    private static PreQuoteDraftQueryException QueryException() =>
        new(new InvalidOperationException("database"));
    private static PreQuoteDraftPersistenceException PersistenceException() =>
        new(new InvalidOperationException("database"));

    private static void AssertStatus(IActionResult result, int expected)
    {
        var status = result switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            StatusCodeResult statusResult => statusResult.StatusCode,
            _ => null
        };
        Assert.Equal(expected, status);
    }

    private static ProblemDetails AssertProblem(
        IActionResult result,
        int status)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        return Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public void Methods_DeclaresStableProblemDetailsContract()
    {
        var create = typeof(PreQuoteDraftsController).GetMethod(
            nameof(PreQuoteDraftsController.Create));
        var get = typeof(PreQuoteDraftsController).GetMethod(
            nameof(PreQuoteDraftsController.Get));
        var update = typeof(PreQuoteDraftsController).GetMethod(
            nameof(PreQuoteDraftsController.Update));
        var approve = typeof(PreQuoteDraftsController).GetMethod(
            nameof(PreQuoteDraftsController.Approve));

        Assert.NotNull(create);
        Assert.NotNull(get);
        Assert.NotNull(update);
        Assert.NotNull(approve);
        Assert.Contains(create.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>(), response =>
                response.StatusCode == StatusCodes.Status201Created
                && response.Type == typeof(PreQuoteDraftDetailsResponse));
        Assert.Contains(create.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>(), response =>
                response.StatusCode == StatusCodes.Status400BadRequest
                && response.Type == typeof(ApiProblemDetailsResponse));
        foreach (var method in new[] { create, get, update, approve })
        {
            var responses = method!.GetCustomAttributes(
                    typeof(ProducesResponseTypeAttribute),
                    true)
                .Cast<ProducesResponseTypeAttribute>()
                .ToArray();
            foreach (var expected in new[]
                     {
                         StatusCodes.Status400BadRequest,
                         StatusCodes.Status401Unauthorized,
                         StatusCodes.Status403Forbidden,
                         StatusCodes.Status404NotFound,
                         StatusCodes.Status500InternalServerError
                     })
            {
                Assert.Contains(responses, response =>
                    response.StatusCode == expected
                    && response.Type == typeof(ApiProblemDetailsResponse));
            }
        }

        var status404ForGet = get.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>()
            .Any(response => response.StatusCode == StatusCodes.Status404NotFound);
        Assert.True(status404ForGet);
        var hasStatus409ForUpdate = update.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>()
            .Any(response => response.StatusCode == StatusCodes.Status409Conflict);
        var hasStatus409ForApprove = approve.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>()
            .Any(response => response.StatusCode == StatusCodes.Status409Conflict);
        Assert.True(hasStatus409ForUpdate);
        Assert.True(hasStatus409ForApprove);
    }

    private sealed record Context(
        ICurrentUser Current,
        IIdentityRepository Identity,
        IPreQuoteDraftRepository Repository,
        PreQuoteDraftsController Controller);
    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
