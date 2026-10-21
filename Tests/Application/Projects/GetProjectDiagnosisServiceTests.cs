using System.Text.Json;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Projects;
using Application.Projects.GetProjectDiagnosis;
using Contracts.Projects;
using Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace CotizadorBackend.Tests.Application.Projects;

public sealed class GetProjectDiagnosisServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OwnerOrAdmin_CanReadPublicDiagnosis(bool admin)
    {
        var context = Create(admin: admin, otherOwner: admin);
        var result = await context.Service.ExecuteAsync(context.Project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(GetProjectDiagnosisFailure.None, result.Failure);
        Assert.Equal("AVAILABLE", result.Result!.Status);
        Assert.Equal("casa", result.Result.Diagnosis!.General.Single(x => x.QuestionId == "tipo").Code);
        await context.Sources.Received(1).FindByProjectIdAsync(context.Project.Id, Arg.Any<CancellationToken>());
        Assert.Equal(new[] { "FindByIdAsync" }, context.Projects.ReceivedCalls().Select(c => c.GetMethodInfo().Name));
        Assert.Equal(new[] { "FindByProjectIdAsync" }, context.Sources.ReceivedCalls().Select(c => c.GetMethodInfo().Name));
    }

    [Theory]
    [InlineData("other", GetProjectDiagnosisFailure.NotFound)]
    [InlineData("inactive", GetProjectDiagnosisFailure.InactiveUser)]
    [InlineData("anonymous", GetProjectDiagnosisFailure.Unauthorized)]
    [InlineData("missing-user", GetProjectDiagnosisFailure.Unauthorized)]
    [InlineData("missing-project", GetProjectDiagnosisFailure.NotFound)]
    [InlineData("empty-id", GetProjectDiagnosisFailure.InvalidRequest)]
    public async Task AccessFailures_NeverReadPublicAnswers(string scenario, GetProjectDiagnosisFailure failure)
    {
        var context = Create(otherOwner: scenario == "other");
        if (scenario == "inactive") context.User.Deactivate(At.AddMinutes(1));
        if (scenario == "anonymous") context.Current.IsAuthenticated.Returns(false);
        if (scenario == "missing-user") context.Identity.FindUserByIdAsync(context.User.Id, Arg.Any<CancellationToken>()).Returns((User?)null);
        if (scenario == "missing-project") context.Projects.FindByIdAsync(context.Project.Id, Arg.Any<CancellationToken>()).Returns((global::Domain.Projects.Project?)null);
        var result = await context.Service.ExecuteAsync(scenario == "empty-id" ? Guid.Empty : context.Project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Result);
        Assert.Empty(context.Sources.ReceivedCalls());
    }

    [Theory]
    [InlineData(0, "NO_PUBLIC_EXPERIENCE")]
    [InlineData(1, "AVAILABLE")]
    [InlineData(2, "AMBIGUOUS_PUBLIC_EXPERIENCE")]
    [InlineData(3, "AMBIGUOUS_PUBLIC_EXPERIENCE")]
    public async Task SourceCardinality_IsExplicitAndNeverChoosesArbitrarily(int count, string status)
    {
        var context = Create();
        context.Sources.FindByProjectIdAsync(context.Project.Id, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, count).Select(i => ProjectDiagnosisBuilderTests.Source() with { SubmissionId = i.ToString() }).ToArray());
        var result = await context.Service.ExecuteAsync(context.Project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(status, result.Result!.Status);
        if (count != 1) Assert.Null(result.Result.Diagnosis);
    }

    [Theory]
    [InlineData("{}", "sng-public-experience-v1", "AVAILABLE")]
    [InlineData("bad", "sng-public-experience-v1", "INVALID_SOURCE_DATA")]
    [InlineData("{}", "v2", "UNSUPPORTED_SCHEMA")]
    public async Task PartialMalformedOrFutureSource_ReturnsSafeResult(string json, string schema, string status)
    {
        var context = Create();
        context.Sources.FindByProjectIdAsync(context.Project.Id, Arg.Any<CancellationToken>()).Returns([ProjectDiagnosisBuilderTests.Source(json, schema)]);
        var result = await context.Service.ExecuteAsync(context.Project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(GetProjectDiagnosisFailure.None, result.Failure);
        Assert.Equal(status, result.Result!.Status);
    }

    [Fact]
    public async Task RepositoryFailure_IsSafe500_AndCancellationPropagates()
    {
        var context = Create();
        context.Sources.FindByProjectIdAsync(context.Project.Id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProjectDiagnosisQueryException(new Exception("PRIVATE_DETAIL")));
        var response = await Controller(context).Get(context.Project.Id, TestContext.Current.CancellationToken);
        var failure = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(500, failure.StatusCode);
        Assert.DoesNotContain("PRIVATE_DETAIL", JsonSerializer.Serialize(failure.Value));
        context.Sources.FindByProjectIdAsync(context.Project.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => context.Service.ExecuteAsync(context.Project.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Controller_MapsTypedContract_NoAnswersPiiOrTechnicalRecommendations()
    {
        var context = Create();
        context.Sources.FindByProjectIdAsync(context.Project.Id, Arg.Any<CancellationToken>()).Returns([
            ProjectDiagnosisBuilderTests.Source("""{"tipo":"casa","termico":5,"preferencia_acabado":"negro","nombre":"PRIVATE_NAME","wa":"PRIVATE_PHONE","unknownObject":{"secret":true}}""")]);
        var action = await Controller(context).Get(context.Project.Id, TestContext.Current.CancellationToken);
        var contract = Assert.IsType<ProjectDiagnosisResponse>(Assert.IsType<OkObjectResult>(action.Result).Value);
        var json = JsonSerializer.Serialize(contract, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var detail = document.RootElement.GetProperty("diagnosis");
        Assert.Equal(new[] { "source", "submissionId", "schemaVersion", "interpretationVersion", "general", "priorities", "preferences", "livingContext", "needs", "decision", "supplementalNotes" }, detail.EnumerateObject().Select(x => x.Name));
        foreach (var forbidden in new[] { "answers", "AnswersJson", "PRIVATE_", "unknownObject", "PP13", "BLACK_MATTE", "PREMIUM", "LAMINADO", "HIGH" })
            Assert.DoesNotContain(forbidden, json);
        Assert.Equal("Negro", contract.Diagnosis!.Preferences.Single(x => x.QuestionId == "preferencia_acabado").DisplayValue);
        Assert.NotEmpty(typeof(ProjectDiagnosisController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.Equal("api/v2/projects/{projectId:guid}/diagnosis", Assert.Single(typeof(ProjectDiagnosisController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
    }

    [Theory]
    [InlineData(false, false, true, 401)]
    [InlineData(true, true, true, 404)]
    [InlineData(true, false, false, 403)]
    public async Task Controller_MapsAccessStatus(bool authenticated, bool otherOwner, bool active, int status)
    {
        var context = Create(otherOwner: otherOwner);
        context.Current.IsAuthenticated.Returns(authenticated);
        if (!active) context.User.Deactivate(At.AddMinutes(1));
        var response = await Controller(context).Get(context.Project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(status, Assert.IsType<ObjectResult>(response.Result).StatusCode);
    }

    private static ProjectDiagnosisController Controller(Context context) => new(context.Service)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static Context Create(bool admin = false, bool otherOwner = false)
    {
        var current = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var sources = Substitute.For<IProjectDiagnosisSourceRepository>();
        var user = User.CreateFromGoogle("user@example.test", "User", null, null, At);
        if (admin) user.ChangeRole(UserRole.Admin, At.AddMinutes(1));
        var project = global::Domain.Projects.Project.Create(Guid.NewGuid(), "P-1", "Project", null, "City", otherOwner ? Guid.NewGuid() : user.Id, At);
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(user.Id);
        identity.FindUserByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        sources.FindByProjectIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns([ProjectDiagnosisBuilderTests.Source("""{"tipo":"casa"}""")]);
        return new(new(current, identity, projects, sources), current, identity, projects, sources, user, project);
    }

    private sealed record Context(GetProjectDiagnosisService Service, ICurrentUser Current,
        IIdentityRepository Identity, IProjectRepository Projects, IProjectDiagnosisSourceRepository Sources,
        User User, global::Domain.Projects.Project Project);
}
