using System.Net;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ApprovePreQuoteDraft;
using Application.PreQuotes.CreatePreQuoteDraft;
using Application.PreQuotes.GetPreQuoteDraft;
using Application.PreQuotes.UpdatePreQuoteDraft;
using Contracts.PreQuotes;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class PreQuoteDraftHttpContractTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PreQuoteId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Put_DeserializesPublicFindingIdentifiers()
    {
        var draft = CreateSimpleDraft();
        await using var host = await ControlledHost.StartAsync(draft);

        using var response = await host.Client.PutAsync(
            $"/api/v1/prequotes/{PreQuoteId}/draft",
            JsonContent(BuildSimpleBody(
                draft,
                issueStatus: "RESOLVED",
                conflictStatus: "RESOLVED")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            PreQuoteDraftResolutionStatus.Resolved,
            draft.Issues.Single().ResolutionStatus);
        Assert.Equal(
            PreQuoteDraftResolutionStatus.Resolved,
            draft.Conflicts.Single().ResolutionStatus);
        await host.Repository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("empty_issue")]
    [InlineData("empty_conflict")]
    [InlineData("duplicate_issue")]
    [InlineData("duplicate_conflict")]
    [InlineData("legacy_issue_id")]
    public async Task Put_InvalidFindingIdentifiers_ReturnsBadRequest(
        string scenario)
    {
        var draft = CreateSimpleDraft();
        await using var host = await ControlledHost.StartAsync(draft);

        using var response = await host.Client.PutAsync(
            $"/api/v1/prequotes/{PreQuoteId}/draft",
            JsonContent(BuildSimpleBody(draft, scenario: scenario)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<
            Microsoft.AspNetCore.Mvc.ProblemDetails>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Solicitud inválida", problem.Title);
        Assert.NotEqual("Borrador incompleto", problem.Title);
        await host.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_SolarisRequest_ReturnsExpectedSummary()
    {
        var draft = CreateSolarisDraft();
        await using var host = await ControlledHost.StartAsync(draft);

        using var response = await host.Client.PutAsync(
            $"/api/v1/prequotes/{PreQuoteId}/draft",
            JsonContent(BuildSolarisBody(draft)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<
            PreQuoteDraftDetailsResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("IN_REVIEW", body.Status);
        Assert.Equal(2, body.Version);
        Assert.Equal(7, body.Summary.TotalItemCount);
        Assert.Equal(1, body.Summary.ManualItemCount);
        Assert.Equal(19, body.Summary.IncludedKnownQuoteableUnitCount);
        Assert.Equal(13, body.Summary.TotalRequirementCount);
        Assert.Equal(12, body.Summary.IncludedRequirementCount);
        Assert.Equal(3, body.Summary.TotalDocumentReferenceCount);
        Assert.Equal(0, body.Summary.PendingIssueCount);
        Assert.Equal(1, body.Summary.ResolvedIssueCount);
        await host.Repository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    private static string BuildSimpleBody(
        PreQuoteDraft draft,
        string issueStatus = "PENDING",
        string conflictStatus = "PENDING",
        string? scenario = null)
    {
        var issueId = scenario == "empty_issue"
            ? Guid.Empty
            : draft.Issues.Single().Id;
        var conflictId = scenario == "empty_conflict"
            ? Guid.Empty
            : draft.Conflicts.Single().Id;
        object issue = scenario == "legacy_issue_id"
            ? new
            {
                id = issueId,
                resolutionStatus = issueStatus,
                resolutionNote = Note(issueStatus)
            }
            : new
            {
                draftIssueId = issueId,
                resolutionStatus = issueStatus,
                resolutionNote = Note(issueStatus)
            };
        object conflict = new
        {
            draftConflictId = conflictId,
            resolutionStatus = conflictStatus,
            resolutionNote = Note(conflictStatus)
        };
        var issues = scenario == "duplicate_issue"
            ? new[] { issue, issue }
            : [issue];
        var conflicts = scenario == "duplicate_conflict"
            ? new[] { conflict, conflict }
            : [conflict];
        return JsonSerializer.Serialize(new
        {
            expectedVersion = 1,
            project = new
            {
                name = "Centro Empresarial Solaris",
                clientName = "Constructora Horizonte Urbano SAS",
                location = "Bogota, Cundinamarca"
            },
            items = draft.Items.Select(Item),
            requirements = draft.Requirements.Select(Requirement),
            documentReferences = draft.DocumentReferences.Select(Reference),
            issues,
            conflicts
        });
    }

    private static string BuildSolarisBody(PreQuoteDraft draft)
    {
        var items = draft.Items.OrderBy(x => x.Sequence)
            .Select(Item)
            .Append(new
            {
                draftItemId = (Guid?)null,
                sequence = 7,
                reference = "L-01",
                description = "Lucernario manual",
                elementType = "SKYLIGHT",
                rawMeasurements = (string?)null,
                widthMillimeters = (int?)2000,
                heightMillimeters = (int?)1400,
                quantity = (int?)2,
                isIncluded = true
            });
        var requirements = draft.Requirements.OrderBy(x => x.Sequence)
            .Select(x => new
            {
                draftRequirementId = (Guid?)x.Id,
                sequence = x.Sequence,
                category = "GENERAL_NOTE",
                value = x.Value,
                isIncluded = x.Sequence != 8
            })
            .Append(new
            {
                draftRequirementId = (Guid?)null,
                sequence = 13,
                category = "GENERAL_NOTE",
                value = "Requirement manual",
                isIncluded = true
            });
        var references = draft.DocumentReferences.OrderBy(x => x.Sequence)
            .Select(Reference)
            .Append(new
            {
                draftDocumentReferenceId = (Guid?)null,
                sequence = 3,
                reference = "R-03",
                description = "Referencia manual",
                detail = (string?)null,
                quantity = (int?)1,
                isIncluded = true
            });
        return JsonSerializer.Serialize(new
        {
            expectedVersion = 1,
            project = new
            {
                name = "Centro Empresarial Solaris",
                clientName = "Constructora Horizonte Urbano SAS",
                location = "Bogota, Cundinamarca"
            },
            items,
            requirements,
            documentReferences = references,
            issues = new[]
            {
                new
                {
                    draftIssueId = draft.Issues.Single().Id,
                    resolutionStatus = "RESOLVED",
                    resolutionNote =
                        "Se verificaron visualmente los datos extraidos mediante OCR."
                }
            },
            conflicts = Array.Empty<object>()
        });
    }

    private static object Item(PreQuoteDraftItem value) => new
    {
        draftItemId = (Guid?)value.Id,
        sequence = value.Sequence,
        reference = value.Reference,
        description = value.Description,
        elementType = value.ElementType.ToString().ToUpperInvariant(),
        rawMeasurements = value.RawMeasurements,
        widthMillimeters = value.WidthMillimeters,
        heightMillimeters = value.HeightMillimeters,
        quantity = value.Quantity,
        isIncluded = true
    };

    private static object Requirement(PreQuoteDraftRequirement value) => new
    {
        draftRequirementId = (Guid?)value.Id,
        sequence = value.Sequence,
        category = "GENERAL_NOTE",
        value = value.Value,
        isIncluded = true
    };

    private static object Reference(
        PreQuoteDraftDocumentReference value) => new
    {
        draftDocumentReferenceId = (Guid?)value.Id,
        sequence = value.Sequence,
        reference = value.Reference,
        description = value.Description,
        detail = value.Detail,
        quantity = value.Quantity,
        isIncluded = true
    };

    private static string? Note(string status) =>
        status == "PENDING" ? null : "Conflicto verificado.";

    private static StringContent JsonContent(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private static PreQuoteDraft CreateSimpleDraft() => PreQuoteDraft.Create(
        PreQuoteId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Centro Empresarial Solaris",
        "Constructora Horizonte Urbano SAS",
        "Bogota, Cundinamarca",
        UserId,
        At,
        [new(
            Guid.NewGuid(), 1, null, "Item",
            StructuredElementType.Window, null, 100, 100, 1)],
        [new(
            Guid.NewGuid(), 1,
            RequirementCategory.GeneralNote, "Requirement")],
        [new(
            Guid.NewGuid(), 1, "R-01", "Reference", null, 1)],
        [new(
            Guid.NewGuid(), 1,
            StructuredIssueCode.OcrReviewRequired,
            "Issue", 1, [1])],
        [new(
            Guid.NewGuid(), 1,
            StructuredConflictCode.DuplicateItemReference,
            "Conflict", [1], [1])]);

    private static PreQuoteDraft CreateSolarisDraft()
    {
        var quantities = new[] { 1, 2, 3, 4, 3, 4 };
        return PreQuoteDraft.Create(
            PreQuoteId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Centro Empresarial Solaris",
            "Constructora Horizonte Urbano SAS",
            "Bogota, Cundinamarca",
            UserId,
            At,
            quantities.Select((quantity, index) =>
                new PreQuoteDraftItemSource(
                    Guid.NewGuid(),
                    index + 1,
                    $"I-{index + 1:00}",
                    $"Item {index + 1}",
                    StructuredElementType.Window,
                    null,
                    1000,
                    1000,
                    quantity)).ToArray(),
            Enumerable.Range(1, 12).Select(sequence =>
                new PreQuoteDraftRequirementSource(
                    Guid.NewGuid(),
                    sequence,
                    RequirementCategory.GeneralNote,
                    $"Requirement {sequence}")).ToArray(),
            Enumerable.Range(1, 2).Select(sequence =>
                new PreQuoteDraftReferenceSource(
                    Guid.NewGuid(),
                    sequence,
                    $"R-{sequence:00}",
                    $"Reference {sequence}",
                    null,
                    1)).ToArray(),
            [new(
                Guid.NewGuid(),
                1,
                StructuredIssueCode.OcrReviewRequired,
                "Issue", 1, [1])],
            []);
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private ControlledHost(
            WebApplication application,
            HttpClient client,
            IPreQuoteDraftRepository repository)
        {
            Application = application;
            Client = client;
            Repository = repository;
        }

        public WebApplication Application { get; }
        public HttpClient Client { get; }
        public IPreQuoteDraftRepository Repository { get; }

        public static async Task<ControlledHost> StartAsync(
            PreQuoteDraft draft)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName =
                        typeof(PreQuoteDraftsController).Assembly
                            .GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var currentUser = Substitute.For<ICurrentUser>();
            var identity = Substitute.For<IIdentityRepository>();
            var repository = Substitute.For<IPreQuoteDraftRepository>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(UserId);
            identity.FindUserByIdAsync(
                    UserId,
                    Arg.Any<CancellationToken>())
                .Returns(User.CreateFromGoogle(
                    "user@example.com", "Test", "User", null, At));
            repository.FindForUpdateAsync(
                    PreQuoteId,
                    Arg.Any<CancellationToken>())
                .Returns(draft);
            repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(PreQuoteDraftsController).Assembly);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(currentUser);
            builder.Services.AddSingleton(identity);
            builder.Services.AddSingleton(repository);
            builder.Services.AddSingleton<TimeProvider>(
                new FixedProvider(At.AddMinutes(1)));
            builder.Services.AddSingleton<
                IValidator<CreatePreQuoteDraftCommand>,
                CreatePreQuoteDraftCommandValidator>();
            builder.Services.AddSingleton<
                IValidator<GetPreQuoteDraftQuery>,
                GetPreQuoteDraftQueryValidator>();
            builder.Services.AddSingleton<
                IValidator<UpdatePreQuoteDraftCommand>,
                UpdatePreQuoteDraftCommandValidator>();
            builder.Services.AddSingleton<
                IValidator<ApprovePreQuoteDraftCommand>,
                ApprovePreQuoteDraftCommandValidator>();
            builder.Services.AddScoped<CreatePreQuoteDraftService>();
            builder.Services.AddScoped<GetPreQuoteDraftService>();
            builder.Services.AddScoped<UpdatePreQuoteDraftService>();
            builder.Services.AddScoped<ApprovePreQuoteDraftService>();

            var application = builder.Build();
            application.UseRouting();
            application.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [new Claim(
                        ClaimTypes.NameIdentifier,
                        UserId.ToString())],
                    "Test"));
                await next(context);
            });
            application.UseAuthorization();
            application.MapControllers();
            var started = false;
            HttpClient? client = null;
            try
            {
                await application.StartAsync();
                started = true;
                var addresses = application.Services
                    .GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses;
                Assert.NotNull(addresses);
                client = new HttpClient
                {
                    BaseAddress = new Uri(Assert.Single(addresses))
                };
                return new(application, client, repository);
            }
            catch (Exception originalException)
            {
                try { client?.Dispose(); } catch { }
                try
                {
                    if (started)
                    {
                        await application.StopAsync(
                            TestContext.Current.CancellationToken);
                    }
                }
                catch { }
                finally
                {
                    try { await application.DisposeAsync(); } catch { }
                }
                ExceptionDispatchInfo.Capture(originalException).Throw();
                throw new InvalidOperationException("Unreachable.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Application.StopAsync();
            await Application.DisposeAsync();
        }
    }

    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
