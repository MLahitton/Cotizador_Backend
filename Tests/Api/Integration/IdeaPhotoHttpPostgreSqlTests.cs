using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Contracts.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using CotizadorBackend.Tests.Infrastructure.Persistence;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed partial class PublicExperienceHttpPostgreSqlTests
{
    [Fact]
    public async Task IdeaPhoto_RequestAt121MiB_ReachesParser_TruncatedBodyRemains400()
    {
        await using var host = await StartAsync();
        var requestLength = global::Application.PublicExperience.IdeaPhotoLimits.MaximumRequestBytes;
        var body = new byte[checked((int)requestLength)];
        var prefix = System.Text.Encoding.ASCII.GetBytes("--photo-boundary\r\nContent-Disposition: form-data; name=\"unknown\"; filename=\"a.bin\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        var suffix = System.Text.Encoding.ASCII.GetBytes("\r\n--photo-boundary--\r\n");
        prefix.CopyTo(body, 0);
        suffix.CopyTo(body, body.Length - suffix.Length);
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=photo-boundary");
        using var response = await host.Client.PostAsync("/api/public/experience-submissions", content, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Set<PublicExperienceSubmission>().AnyAsync(Token));
        Assert.Equal(0, host.Storage.Writes);
        Assert.Equal(0, host.Ai.Calls);
    }

    [Fact]
    public async Task IdeaPhoto_RequestOver121MiB_RealKestrelRejectsBeforeBodyOrBusiness()
    {
        await using var host = await StartAsync();
        using var socket = new System.Net.Sockets.TcpClient();
        await socket.ConnectAsync("127.0.0.1", host.Client.BaseAddress!.Port, Token);
        await using var stream = socket.GetStream();
        var requestLength = global::Application.PublicExperience.IdeaPhotoLimits.MaximumRequestBytes + 1;
        var request = $"POST /api/public/experience-submissions HTTP/1.1\r\nHost: localhost\r\nContent-Type: multipart/form-data; boundary=photo-boundary\r\nContent-Length: {requestLength}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(request), Token);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var status = await reader.ReadLineAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(15), Token);
        Assert.Equal("HTTP/1.1 413 Payload Too Large", status);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Set<PublicExperienceSubmission>().AnyAsync(Token));
        Assert.Equal(0, host.Storage.Writes);
        Assert.Equal(0, host.Ai.Calls);
    }

    [Fact]
    public async Task IdeaPhoto_HistoricalNoPhoto_ProjectQueryAndHttpReplaySurviveMigration()
    {
        await using var host = await StartAsync();
        using var createdResponse = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("20261023091820_AddRequirementExecutionEvidence", Token);
            var historical = await db.Set<PublicExperienceSubmission>().AsNoTracking().SingleAsync(Token);
            await migrator.MigrateAsync(cancellationToken: Token);
            Assert.False(await db.Set<PublicExperienceIdeaPhoto>().AnyAsync(Token));
            using var replay = await host.PostAsync(Payload());
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(created with { IsReplay = true }, await replay.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token));
            await AssertChainAsync(created, host.Storage);
            var current = await db.Set<PublicExperienceSubmission>().AsNoTracking().SingleAsync(Token);
            Assert.Equal(historical.PayloadFingerprint, current.PayloadFingerprint);
            Assert.Equal(historical.AnswersJson, current.AnswersJson);
            Assert.Equal(historical.ProjectId, current.ProjectId);
            Assert.Equal(historical.RequirementId, current.RequirementId);
            Assert.Equal(2, host.Storage.Writes);
            Assert.Equal(0, host.Ai.Calls);
        }
        finally { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
    }

    [Fact]
    public async Task IdeaPhoto_CompleteHttp_PendingWorkspace_StableReplay_NoAutomaticProcessing()
    {
        await using var host = await StartAsync();
        async Task<HttpResponseMessage> Post(string name)
        {
            using var multipart = PublicExperienceSubmissionHttpTests.Multipart(Payload(), [PublicExperienceIntakeTests.Upload()]);
            var part = new ByteArrayContent(IdeaPhotoCaptureTests.Png);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(part, "ideaPhoto", name);
            return await host.Client.PostAsync("/api/public/experience-submissions", multipart, Token);
        }
        using var first = await Post("reference.png");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = (await first.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token))!;
        await AssertChainAsync(created, host.Storage, expectedFileCount: 3);
        await using var db = fixture.CreateDbContext();
        var photo = await db.Set<PublicExperienceIdeaPhoto>().AsNoTracking().SingleAsync(Token);
        Assert.Equal(IdeaPhotoCaptureTests.PngHash, photo.StoredSha256);
        Assert.Equal("reference.png", photo.OriginalFileName);
        Assert.Equal(1, await db.RequirementFiles.CountAsync(Token));
        using var retry = await Post("renamed.png");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(created with { IsReplay = true }, await retry.Content.ReadFromJsonAsync<PublicExperienceSubmissionResponse>(Token));
        using var remove = await host.PostAsync(Payload());
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
        await AssertChainAsync(created, host.Storage, expectedFileCount: 3);
        Assert.Equal(3, host.Storage.Writes);
        Assert.Equal(0, host.Ai.Calls);
        Assert.Equal(photo.Id, (await db.Set<PublicExperienceIdeaPhoto>().AsNoTracking().SingleAsync(Token)).Id);
    }

    [Theory]
    [InlineData("duplicate", 400)]
    [InlineData("unknown", 400)]
    [InlineData("corrupt", 400)]
    [InlineData("extension", 415)]
    [InlineData("mime", 415)]
    [InlineData("empty", 400)]
    [InlineData("large", 413)]
    public async Task IdeaPhoto_InvalidHttp_RejectsEntireSubmission(string scenario, int expected)
    {
        await using var host = await StartAsync();
        using var multipart = PublicExperienceSubmissionHttpTests.Multipart(Payload(), [PublicExperienceIntakeTests.Upload()]);
        var part = new ByteArrayContent(scenario == "large" ? new byte[20 * 1024 * 1024 + 1] : scenario == "empty" ? [] : scenario == "corrupt" ? "invalid"u8.ToArray() : IdeaPhotoCaptureTests.Png);
        if (scenario == "mime") part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(part, scenario == "unknown" ? "unexpected" : "ideaPhoto", scenario == "extension" ? "a.gif" : "a.png");
        if (scenario == "duplicate") multipart.Add(new ByteArrayContent(IdeaPhotoCaptureTests.Png), "ideaPhoto", "b.png");
        using var response = await host.Client.PostAsync("/api/public/experience-submissions", multipart, Token);
        Assert.Equal(expected, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("StorageKey", body);
        Assert.DoesNotContain("Magick", body);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Set<PublicExperienceSubmission>().AnyAsync(Token));
        Assert.False(await db.Projects.AnyAsync(Token));
        Assert.Equal(0, host.Storage.Writes);
        Assert.Equal(0, host.Ai.Calls);
    }
}
