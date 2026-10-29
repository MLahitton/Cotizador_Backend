using Application.Projects.GetProjectIdeaPhoto;
using Api.Controllers;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed partial class ProjectIdeaPhotoReadTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SlowResponse_HoldsSlotUntilDrained_CompletionAndAbortRelease(bool abort, bool transportFailure)
    {
        await using var host = await Start();
        await using var db1 = fixture.CreateDbContext();
        await using var db2 = fixture.CreateDbContext();
        await using var db3 = fixture.CreateDbContext();
        await using var first = (await host.Service(db1).ExecuteAsync(host.Project, Token)).Content!;
        await using var second = (await host.Service(db2).ExecuteAsync(host.Project, Token)).Content!;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var sink = new PausedWrite { Fail = transportFailure };
        var http = new DefaultHttpContext { RequestAborted = cancel.Token };
        http.Response.Body = sink;
        var send = new ProjectIdeaPhotoResponse(first).ExecuteResultAsync(new ActionContext(http,new RouteData(),new ActionDescriptor()));
        try
        {
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.False(send.IsCompleted);
            Assert.All(host.Copies.All, c => Assert.False(c.Closed));
            var probe = new PhotoProbe(new ProjectIdeaPhotoRepository(db3));
            using var waitingCancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var waiting = host.Service(db3, probe).ExecuteAsync(host.Project, waitingCancellation.Token);
            await probe.Returned.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.False(waiting.IsCompleted); Assert.Equal(2, host.Storage.Opens);
            waitingCancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(waitingCancellation.Token, error.CancellationToken);
            Assert.All(host.Copies.All, c => Assert.False(c.Closed));
            if (abort) cancel.Cancel(); else sink.Release.TrySetResult();
            if (abort) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
            else if (transportFailure) await Assert.ThrowsAsync<IOException>(() => send);
            else { await send; Assert.Equal(host.Bytes, sink.Bytes.ToArray()); }
            Assert.True(host.Copies.All[0].Closed); Assert.False(host.Copies.All[1].Closed);
            await second.DisposeAsync();
            await using var again1 = (await host.Service(db1).ExecuteAsync(host.Project, Token)).Content!;
            await using var again2 = (await host.Service(db2).ExecuteAsync(host.Project, Token)).Content!;
            Assert.Equal(4, host.Storage.Opens);
            Assert.Equal(host.Bytes, await File.ReadAllBytesAsync(Path.Combine(host.Root,host.StorageKey), Token));
        }
        finally
        {
            sink.Release.TrySetResult();
            try { await send; } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (IOException) when (transportFailure) { }
        }
    }

    [Fact]
    public async Task CancellationDuringCapture_DisposesSourceAndRestoresBothSlots()
    {
        await using var host = await Start();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var source = new PausedRead(await host.Storage.Inner.OpenReadAsync(host.StorageKey, Token));
        host.Storage.OpenOverride = _ => Task.FromResult<Stream>(source);
        await using var db = fixture.CreateDbContext();
        var work = host.Service(db).ExecuteAsync(host.Project, cancel.Token);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            await using (var locks = fixture.CreateDbContext())
            {
                await using var tx = await locks.Database.BeginTransactionAsync(Token);
                await locks.Database.ExecuteSqlRawAsync("SELECT id FROM core.projects FOR UPDATE NOWAIT", Token);
                await locks.Database.ExecuteSqlRawAsync("SELECT id FROM core.clients FOR UPDATE NOWAIT", Token);
                await locks.Database.ExecuteSqlRawAsync("SELECT id FROM core.public_experience_submissions FOR UPDATE NOWAIT", Token);
                await locks.Database.ExecuteSqlRawAsync("SELECT id FROM core.public_experience_idea_photos FOR UPDATE NOWAIT", Token);
            }
            cancel.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.Equal(cancel.Token,error.CancellationToken); Assert.True(source.Closed);
            host.Storage.OpenOverride=null;
            await using var first=(await host.Service(db).ExecuteAsync(host.Project,Token)).Content!;
            await using var second=(await host.Service(db).ExecuteAsync(host.Project,Token)).Content!;
            Assert.Equal(3,host.Storage.Opens);
            Assert.Equal(host.Bytes,await File.ReadAllBytesAsync(Path.Combine(host.Root,host.StorageKey),Token));
        }
        finally { cancel.Cancel(); try { await work; } catch (OperationCanceledException) { } }
    }

    [Theory]
    [InlineData("StoredSha256")]
    [InlineData("StoredLengthBytes")]
    [InlineData("ContentType")]
    [InlineData("StoredEvidenceVersion")]
    [InlineData("IdentityVersion")]
    [InlineData("StorageKey")]
    public async Task InvalidMetadata_GuardBeforeStorage_WithoutDisablingDatabaseConstraints(string property)
    {
        await using var host=await Start();
        await using var db=fixture.CreateDbContext();
        var photo=await db.Set<PublicExperienceIdeaPhoto>().AsNoTracking().SingleAsync(Token);
        var before=await Snapshot();
        object bad=property switch { "StoredLengthBytes" => 20L*1024*1024+1,
            "StoredEvidenceVersion" or "IdentityVersion" => (short)2,
            "ContentType" => "text/html", "StorageKey" => "", _ => "bad" };
        // DB CHECK forbids these rows. Corrupt only the detached reader output to exercise the guard.
        typeof(PublicExperienceIdeaPhoto).GetProperty(property)!.SetValue(photo,bad);
        var repository=Substitute.For<IProjectIdeaPhotoRepository>();
        repository.FindAsync(host.Project,host.ClientId,Arg.Any<CancellationToken>()).Returns(photo);
        var result=await host.Service(db,repository).ExecuteAsync(host.Project,Token);
        Assert.Equal(ProjectIdeaPhotoFailure.Unavailable,result.Failure); Assert.Null(result.Content);
        Assert.Equal(0,host.Storage.Opens); Assert.Equal(before,await Snapshot());
    }

    [Fact]
    public async Task RequirementReplacement_DoesNotResolveOrChangePhoto()
    {
        await using var host=await Start();
        Guid requirementId;
        await using (var db=fixture.CreateDbContext())
        {
            var quote=PreQuote.Create(host.Project,host.Owner,"PC-2026-0001",null,At);
            var requirement=Requirement.Create(quote.Id,host.Owner,RequirementCommercialLine.Essential,At);
            requirement.StartProcessing(At);
            requirement.MarkFailed(At);
            db.AddRange(quote,requirement); await db.SaveChangesAsync(Token); requirementId=requirement.Id;
            var submission=await db.Set<PublicExperienceSubmission>().SingleAsync(Token);
            submission.CompleteRequirementPreparation(quote.Id,requirement.Id,At); await db.SaveChangesAsync(Token);
        }
        using(var response=await host.Get("owner",host.Project)) Assert.Equal(host.Bytes,await response.Content.ReadAsByteArrayAsync(Token));
        await using (var db=fixture.CreateDbContext())
        {
            var previous=await db.Requirements.SingleAsync(Token);
            var replacement=Requirement.Create(previous.PreQuoteId,host.Owner,RequirementCommercialLine.Essential,At.AddSeconds(1));
            replacement.MarkAsReplacementOf(previous.Id);
            await using var tx=await db.Database.BeginTransactionAsync(Token);
            // Release the current-row unique index before inserting its successor; FKs stay enabled.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirements SET is_active=false WHERE id={previous.Id}",Token);
            db.Add(replacement); await db.SaveChangesAsync(Token);
            previous.SupersedeBy(replacement.Id,At.AddSeconds(1)); await db.SaveChangesAsync(Token);
            await tx.CommitAsync(Token);
        }
        var before=await Snapshot();
        using var after=await host.Get("owner",host.Project);
        Assert.Equal(host.Bytes,await after.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(before,await Snapshot()); Assert.Equal(0,host.Commands.Dml);
        await using var read=fixture.CreateDbContext();
        Assert.Equal(requirementId,(await read.Set<PublicExperienceSubmission>().SingleAsync(Token)).RequirementId);
        Assert.Equal(RequirementStatus.Pending,(await read.Requirements.SingleAsync(r=>r.IsActive,Token)).Status);
    }

    private sealed class PhotoProbe(IProjectIdeaPhotoRepository inner) : IProjectIdeaPhotoRepository
    {
        internal TaskCompletionSource Returned=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PublicExperienceIdeaPhoto?> FindAsync(Guid project,Guid client,CancellationToken ct)
        { var value=await inner.FindAsync(project,client,ct); Returned.TrySetResult(); return value; }
    }

    private sealed class PausedWrite : Stream
    {
        internal TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal MemoryStream Bytes=new();
        internal bool Fail;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); if (Fail) throw new IOException("Injected response transport failure."); await Bytes.WriteAsync(buffer,ct); }
        public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken ct) => WriteAsync(buffer.AsMemory(offset,count),ct).AsTask();
        public override bool CanRead=>false; public override bool CanSeek=>false; public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();
        public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush() { }
        public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long length)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if(disposing) Bytes.Dispose(); base.Dispose(disposing); }
    }
    private sealed class PausedRead(Stream inner) : Stream
    {
        internal TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Closed;
        public override async ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default)
        { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); return 0; }
        public override bool CanRead=>!Closed; public override bool CanSeek=>false; public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush()=>throw new NotSupportedException();
        public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long length)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if(disposing) inner.Dispose(); Closed=true; base.Dispose(disposing); }
    }
}
