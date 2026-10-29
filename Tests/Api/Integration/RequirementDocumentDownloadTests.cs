using System.Net;
using System.Net.Http.Headers;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetRequirementDetails;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed partial class ProjectIdeaPhotoReadTests
{
    private async Task<(Guid Requirement, RequirementFile File)> Document(Host host, bool historical = false,
        string mime = "application/pdf", bool cancelled = false)
    {
        await using var db = fixture.CreateDbContext();
        var quote = PreQuote.Create(host.Project, host.Owner, PreQuote.FormatSerial(2026, await db.PreQuotes.CountAsync(Token) + 1), null, At);
        var requirement = Requirement.Create(quote.Id, host.Owner, RequirementCommercialLine.Essential, At);
        using var source = new MemoryStream(host.Bytes);
        var key = "documents/" + Guid.NewGuid().ToString("N");
        var receipt = await host.Storage.Inner.SaveVerifiedAsync(key, source, source.Length, 20 * 1024 * 1024, Token);
        var file = historical ? RequirementFile.Create(requirement.Id, "plano ñ especial.pdf", mime, receipt.Length, key, At)
            : RequirementFile.CreateWithStoredEvidence(requirement.Id, "plano ñ especial.pdf", mime, receipt.Length, key, At, receipt.Sha256, receipt.Length, At, 1);
        if (cancelled) requirement.Cancel(At.AddSeconds(1));
        db.AddRange(quote, requirement, file);
        await db.SaveChangesAsync(Token);
        return (requirement.Id, file);
    }

    private static HttpRequestMessage DownloadRequest(Host host, string actor, Guid requirement, Guid file)
    {
        var request = host.Request(actor, host.Project);
        request.RequestUri = new Uri($"/api/v2/requirements/{requirement}/documents/{file}/download", UriKind.Relative);
        request.Headers.Range = new RangeHeaderValue(0, 1);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"ignored\""));
        return request;
    }

    [Theory]
    [InlineData("owner", 200)] [InlineData("admin", 200)] [InlineData("stranger", 404)]
    [InlineData("inactive", 403)] [InlineData("anonymous", 401)] [InlineData("invalid-token", 401)]
    [InlineData("missing", 404)] [InlineData("foreign-file", 404)] [InlineData("deleted", 404)]
    [InlineData("inactive-project", 404)] [InlineData("inactive-client", 404)]
    public async Task Download_Authorization_ExactMembership_NoWrites(string actor, int expected)
    {
        await using var host = await Start(photo: false, submission: false);
        var doc = await Document(host);
        var requestedFile = actor == "foreign-file" ? (await Document(host)).File.Id : doc.File.Id;
        await using (var db = fixture.CreateDbContext())
        {
            if (actor == "inactive") (await db.Users.SingleAsync(u => u.Id == host.Owner, Token)).Deactivate(At.AddSeconds(1));
            if (actor == "inactive-project") (await db.Projects.SingleAsync(p => p.Id == host.Project, Token)).SetActive(false, host.Owner, At.AddSeconds(1));
            if (actor == "inactive-client") (await db.Clients.SingleAsync(c => c.Id == host.ClientId, Token)).SetActive(false, host.Owner, At.AddSeconds(1));
            if (actor == "deleted") db.Remove(await db.RequirementFiles.SingleAsync(f => f.Id == doc.File.Id, Token));
            await db.SaveChangesAsync(Token);
        }
        var before = await DownloadSnapshot();
        using var request = DownloadRequest(host, actor, actor == "missing" ? Guid.NewGuid() : doc.Requirement,
            requestedFile);
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(expected, (int)response.StatusCode); Headers(response);
        if (expected == 200)
        {
            Assert.Equal(host.Bytes, await response.Content.ReadAsByteArrayAsync(Token));
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Contains("especial.pdf", response.Content.Headers.ContentDisposition.FileNameStar!);
            Assert.Equal(host.Bytes.Length, response.Content.Headers.ContentLength);
            Assert.Null(response.Headers.ETag);
        }
        else
        {
            Assert.Equal(0, host.Storage.Opens);
            var body = await response.Content.ReadAsStringAsync(Token);
            Assert.DoesNotContain(doc.File.StorageKey, body); Assert.DoesNotContain("especial", body);
            Assert.Contains("traceId", body);
        }
        Assert.Equal(before, await DownloadSnapshot()); Assert.Equal(0, host.Commands.Dml); Assert.Equal(0, host.Storage.Deletes);
        Assert.True(File.Exists(Path.Combine(host.Root, doc.File.StorageKey)));
    }

    [Theory]
    [InlineData(false, "application/pdf")] [InlineData(true, "application/pdf")]
    [InlineData(false, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(false, "image/jpeg")] [InlineData(false, "image/png")]
    [InlineData(true, "unknown/type")]
    public async Task Download_Capture_HistoricalVersion_OriginalReplaced_NoBackfill(bool historical, string mime)
    {
        await using var host = await Start(photo: false, submission: false);
        var doc = await Document(host, historical, mime, cancelled: true);
        host.Storage.OpenOverride = async ct => new ReplaceOnClose(await host.Storage.Inner.OpenReadAsync(doc.File.StorageKey, ct),
            () => File.WriteAllBytesAsync(Path.Combine(host.Root, doc.File.StorageKey), "replaced"u8.ToArray(), Token));
        using var request = DownloadRequest(host, "owner", doc.Requirement, doc.File.Id);
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(host.Bytes, await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(mime == "unknown/type" ? "application/octet-stream" : mime, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(1, host.Storage.Opens); Assert.Equal(0, host.Commands.Dml);
        await using var read = fixture.CreateDbContext();
        var persisted = await read.RequirementFiles.SingleAsync(f => f.Id == doc.File.Id, Token);
        Assert.Equal(doc.File.StoredSha256, persisted.StoredSha256);
        Assert.Equal(historical, persisted.StoredEvidenceVersion is null);
        Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
        Assert.False(read.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("missing")] [InlineData("hash")] [InlineData("short")] [InlineData("long")]
    public async Task Download_StorageFailures_Are500_Not404(string failure)
    {
        await using var host = await Start(photo: false, submission: false);
        var doc = await Document(host);
        var path = Path.Combine(host.Root, doc.File.StorageKey);
        if (failure == "missing") File.Delete(path);
        else await File.WriteAllBytesAsync(path, failure == "hash" ? new byte[host.Bytes.Length]
            : failure == "short" ? [1] : new byte[host.Bytes.Length + 1], Token);
        using var request = DownloadRequest(host, "owner", doc.Requirement, doc.File.Id);
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("REQUIREMENT_DOCUMENT_UNAVAILABLE", body); Assert.DoesNotContain(path, body);
        Assert.Equal(0, host.Commands.Dml); Assert.Equal(0, host.Storage.Deletes);
    }

    [Theory]
    [InlineData("version")] [InlineData("partial")] [InlineData("length")] [InlineData("digest")]
    public async Task Download_InvalidDetachedEvidence_NoStorage(string mutation)
    {
        await using var host = await Start(photo: false, submission: false);
        var doc = await Document(host);
        // Detached corrupted input tests validation without weakening database constraints.
        var property = mutation switch { "version" => "StoredEvidenceVersion", "partial" => "StoredEvidenceAtUtc", "length" => "StoredLengthBytes", _ => "StoredSha256" };
        object? value = mutation switch { "version" => (short)2, "partial" => null, "length" => 1L, _ => new string('z', 64) };
        typeof(RequirementFile).GetProperty(property)!.SetValue(doc.File, value);
        await using var db = fixture.CreateDbContext();
        var repository = Substitute.For<IRequirementRepository>();
        repository.ListFilesByRequirementIdAsync(doc.Requirement, Token).Returns(new[] { doc.File });
        var result = await DownloadService(host, db, repository).ExecuteAsync(doc.Requirement, doc.File.Id, Token);
        Assert.Equal(500, result.Status); Assert.Equal(0, host.Storage.Opens);
    }

    private static DownloadRequirementDocumentService DownloadService(Host host, global::Infrastructure.Persistence.ApplicationDbContext db, IRequirementRepository? files = null)
    {
        var actor = Substitute.For<ICurrentUser>(); actor.IsAuthenticated.Returns(true); actor.UserId.Returns(host.Owner);
        var requirements = new RequirementRepository(db);
        var details = new GetRequirementDetailsService(actor, new IdentityRepository(db), new PreQuoteRepository(db), new ProjectRepository(db), new ClientRepository(db), requirements);
        return new(details, files ?? requirements, host.Storage, new InitialContentCapture(NullLogger<InitialContentCapture>.Instance), host.Copies,
            NullLogger<DownloadRequirementDocumentService>.Instance);
    }

    private async Task<string> DownloadSnapshot()
    {
        await using var db = fixture.CreateDbContext();
        return System.Text.Json.JsonSerializer.Serialize(new {
            Existing = await Snapshot(),
            Requirements = await db.Requirements.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.PreQuoteId, r.Status, r.IsActive, r.CreatedAtUtc, r.UpdatedAtUtc }).ToListAsync(Token),
            Files = await db.RequirementFiles.AsNoTracking().OrderBy(f => f.Id).Select(f => new { f.Id, f.RequirementId, f.OriginalFileName, f.ContentType, f.SizeBytes, f.StorageKey, f.StoredSha256, f.StoredLengthBytes, f.StoredEvidenceVersion, f.StoredEvidenceAtUtc }).ToListAsync(Token)
        });
    }

    [Fact]
    public async Task Download_OutputFailureAndCancellation_ReleaseCopiesAndSlots()
    {
        await using var host = await Start(photo: false, submission: false);
        var doc = await Document(host);
        for (var i = 0; i < 4; i++)
        {
            await using var db = fixture.CreateDbContext();
            var result = await DownloadService(host, db).ExecuteAsync(doc.Requirement, doc.File.Id, Token);
            Assert.Equal(200, result.Status);
            var http = new DefaultHttpContext();
            http.Response.Body = new FailingOutput();
            var action = new RequirementDocumentDownloadResponse(result.Content!);
            await Assert.ThrowsAsync<IOException>(() => action.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor())));
            Assert.All(host.Copies.All, copy => Assert.True(copy.Closed));
            await result.Content!.DisposeAsync();
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await using var context = fixture.CreateDbContext();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadService(host, context).ExecuteAsync(doc.Requirement, doc.File.Id, cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
        Assert.True(File.Exists(Path.Combine(host.Root, doc.File.StorageKey))); Assert.Equal(0, host.Storage.Deletes);
    }
    private sealed class FailingOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException(new IOException("output failed"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Download_DrainsOutputBeforeDispose_AndWaitingCancellationKeepsWinners(bool abortOutput)
    {
        await using var host = await Start(photo: false, submission: false);
        var doc = await Document(host);
        await using var firstDb = fixture.CreateDbContext();
        await using var secondDb = fixture.CreateDbContext();
        await using var thirdDb = fixture.CreateDbContext();
        var first = await DownloadService(host, firstDb).ExecuteAsync(doc.Requirement, doc.File.Id, Token);
        var second = await DownloadService(host, secondDb).ExecuteAsync(doc.Requirement, doc.File.Id, Token);
        await using var firstContent = first.Content!;
        await using var secondContent = second.Content!;
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var third = DownloadService(host, thirdDb).ExecuteAsync(doc.Requirement, doc.File.Id, waiting.Token);
        // Both slots are owned by returned contents: the third cannot open storage.
        Assert.False(third.IsCompleted); Assert.Equal(2, host.Storage.Opens);
        waiting.Cancel();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        Assert.Equal(waiting.Token, cancelled.CancellationToken);
        Assert.All(host.Copies.All, c => Assert.False(c.Closed));
        using var outputCancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var outputStream = new GatedOutput();
        var http = new DefaultHttpContext { RequestAborted = outputCancellation.Token };
        http.Response.Body = outputStream;
        var response = new RequirementDocumentDownloadResponse(firstContent).ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        try
        {
            await outputStream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.False(response.IsCompleted); Assert.False(host.Copies.All[0].Closed);
            if (abortOutput) outputCancellation.Cancel(); else outputStream.Release.TrySetResult();
            if (abortOutput) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response); else await response;
            Assert.True(host.Copies.All[0].Closed); Assert.False(host.Copies.All[1].Closed);
        }
        finally { outputStream.Release.TrySetResult(); try { await response; } catch (OperationCanceledException) { } }
        await secondContent.DisposeAsync();
        await using var read = fixture.CreateDbContext();
        var next = await DownloadService(host, read).ExecuteAsync(doc.Requirement, doc.File.Id, Token);
        Assert.Equal(200, next.Status); await next.Content!.DisposeAsync();
        Assert.Equal(host.Bytes, await File.ReadAllBytesAsync(Path.Combine(host.Root, doc.File.StorageKey), Token));
    }
    private sealed class GatedOutput : MemoryStream
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); await base.WriteAsync(buffer, cancellationToken); }
    }

    [Theory]
    [InlineData("pdf")] [InlineData("xlsx")] [InlineData("jpg")] [InlineData("png")]
    public async Task Download_RealFormatFixtures_ByteForByte(string extension)
    {
        await using var host = await Start(photo: false, submission: false, jpeg: extension == "jpg");
        var doc = await Document(host);
        byte[] bytes;
        string mime;
        if (extension == "pdf")
        {
            using var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
            builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            bytes = builder.Build(); mime = "application/pdf";
            using var parsed = UglyToad.PdfPig.PdfDocument.Open(bytes);
            Assert.Equal(1, parsed.NumberOfPages);
        }
        else if (extension == "xlsx")
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            workbook.AddWorksheet("Documents").Cell(1, 1).Value = "Download fixture";
            using var buffer = new MemoryStream(); workbook.SaveAs(buffer); bytes = buffer.ToArray();
            mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            using var parsed = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
            Assert.Equal("Download fixture", parsed.Worksheet(1).Cell(1, 1).GetString());
        }
        else { bytes = host.Bytes; mime = extension == "jpg" ? "image/jpeg" : "image/png"; }
        var key = "documents/" + Guid.NewGuid().ToString("N");
        using var source = new MemoryStream(bytes);
        var receipt = await host.Storage.Inner.SaveVerifiedAsync(key, source, bytes.Length, 20 * 1024 * 1024, Token);
        var file = RequirementFile.CreateWithStoredEvidence(doc.Requirement, "fixture." + extension, mime, bytes.Length, key, At, receipt.Sha256, receipt.Length, At, 1);
        await using (var db = fixture.CreateDbContext()) { db.Add(file); await db.SaveChangesAsync(Token); }
        using var request = DownloadRequest(host, "owner", doc.Requirement, file.Id);
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mime, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(host.Root, key), Token));
        Assert.Equal(0, host.Commands.Dml); Assert.Equal(0, host.Storage.Deletes);
    }

    [Theory]
    [InlineData("../../a\r\n.pdf", "a.pdf")] [InlineData("..\\report.pdf", "report.pdf")]
    [InlineData("", "documento")] [InlineData("ñ plano.pdf", "ñ plano.pdf")]
    public void Download_SafeNames(string value, string expected) => Assert.Equal(expected, DocumentDownloadContent.SafeName(value));
}
