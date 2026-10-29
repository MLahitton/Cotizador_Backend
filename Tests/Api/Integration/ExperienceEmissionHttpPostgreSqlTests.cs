using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contracts.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using CotizadorBackend.Tests.Infrastructure.Persistence;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed partial class PublicExperienceHttpPostgreSqlTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task ExperienceEmission_Http_NewReplayConflict_WithPhoto_NoAutomaticCalls(bool enabled, bool photo)
    {
        await using var host = await StartAsync();
        host.Options.EmitAutomaticExecution = enabled;
        async Task<HttpResponseMessage> Post(string payload)
        {
            using var content = PublicExperienceSubmissionHttpTests.Multipart(payload, [PublicExperienceIntakeTests.Upload()]);
            if (photo)
            {
                var image = new ByteArrayContent(IdeaPhotoCaptureTests.Png);
                image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(image, "ideaPhoto", "idea.png");
            }
            return await host.Client.PostAsync("/api/public/experience-submissions", content, Token);
        }
        using var first = await Post(Payload());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var result = await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token);
        Assert.NotNull(result); Assert.Equal("RECEIVED", result.Status);
        var response = await first.Content.ReadAsStringAsync(Token);
        foreach (var hidden in new[] { "jobId", "executionRequestId", "authorizedActor", "dispatch", "permit" })
            Assert.DoesNotContain(hidden, response, StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.CreateDbContext();
        var jobs = await db.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token);
        Assert.Equal(enabled ? 1 : 0, jobs.Count);
        if (enabled)
        {
            Assert.Equal(RequirementProcessingJobState.Queued, jobs[0].State);
            Assert.Null(jobs[0].DispatchAdmittedAtUtc); Assert.Null(jobs[0].ProcessingAttemptId);
            Assert.Null(jobs[0].StartedAtUtc); Assert.Null(jobs[0].LeaseOwner);
        }
        var submission = await db.Set<PublicExperienceSubmission>().AsNoTracking().Include(s => s.IdeaPhoto).SingleAsync(Token);
        Assert.Equal(photo, submission.IdeaPhoto is not null);
        var file = Assert.Single(await db.RequirementFiles.AsNoTracking().ToListAsync(Token));
        if (photo) Assert.NotEqual(submission.IdeaPhoto!.StorageKey, file.StorageKey);
        Assert.Equal(RequirementStatus.Pending, (await db.Requirements.AsNoTracking().SingleAsync(Token)).Status);
        var before = JsonSerializer.Serialize(jobs);
        var writes = host.Storage.Writes;
        using var replay = await Post(Payload());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(result with { IsReplay = true }, await replay.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token));
        using var conflict = await Post(Payload().Replace("Example", "Changed"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(before, JsonSerializer.Serialize(await read.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token)));
        Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
        Assert.Empty(await read.JobLeaseOperations.ToListAsync(Token));
        Assert.Empty(await read.ExpiredExecutionCancellations.ToListAsync(Token));
        Assert.Empty(await read.Set<RequirementExecutionEvidence>().ToListAsync(Token));
        Assert.Equal(writes, host.Storage.Writes); Assert.Equal(0, host.Ai.Calls);
    }
}
