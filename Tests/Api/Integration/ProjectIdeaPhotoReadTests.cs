using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Api.Authentication;
using Api.Controllers;
using Api.ErrorHandling;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.Projects.GetProjectIdeaPhoto;
using CotizadorBackend.Tests.Infrastructure.Persistence;
using Domain.Clients;
using Domain.Identity;
using Domain.Projects;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed partial class ProjectIdeaPhotoReadTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Key = "photo-a2-local-test-signing-key-0123456789abcdef";
    private static readonly DateTimeOffset At = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("owner", false)]
    [InlineData("admin", false)]
    [InlineData("owner", true)]
    [InlineData("admin", true)]
    public async Task WorkspaceHttp_UnchangedBeforeAndAfterPhoto(string actor, bool photo)
    {
        await using var host = await Start(photo: photo, submission: photo);
        async Task<string> Workspace()
        {
            using var request = host.Request(actor, host.Project);
            request.RequestUri = new Uri($"/api/v2/projects/{host.Project}/workspace", UriKind.Relative);
            using var response = await host.Client.SendAsync(request, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(response.Headers.CacheControl);
            var body = await response.Content.ReadAsStringAsync(Token);
            Assert.DoesNotContain("ideaPhoto", body); Assert.DoesNotContain("StorageKey", body);
            return body;
        }
        var before = await Workspace();
        using var image = await host.Get(actor, host.Project);
        Assert.Equal(photo ? HttpStatusCode.OK : HttpStatusCode.NoContent, image.StatusCode);
        Assert.Equal(before, await Workspace()); Assert.Equal(0, host.Commands.Dml);
    }

    [Theory]
    [InlineData("owner", 200)]
    [InlineData("admin", 200)]
    [InlineData("stranger", 404)]
    [InlineData("missing", 404)]
    [InlineData("anonymous", 401)]
    [InlineData("invalid-token", 401)]
    [InlineData("inactive", 403)]
    public async Task HttpAuthorization_RealUsers_NoMetadataOrStorageForRejection(string actor, int status)
    {
        await using var host = await Start();
        if (actor == "inactive")
        {
            await using var change = fixture.CreateDbContext();
            var owner = await change.Users.SingleAsync(u => u.Id == host.Owner, Token);
            owner.Deactivate(At.AddSeconds(1)); await change.SaveChangesAsync(Token);
        }
        var before = await Snapshot();
        using var response = await host.Get(actor, actor == "missing" ? Guid.NewGuid() : host.Project);
        Assert.Equal(status, (int)response.StatusCode);
        Headers(response);
        if (status == 200) Assert.Equal(host.Bytes, await response.Content.ReadAsByteArrayAsync(Token));
        else
        {
            var body = await response.Content.ReadAsStringAsync(Token);
            Assert.DoesNotContain("secret-name", body); Assert.DoesNotContain(host.StorageKey, body);
            Assert.DoesNotContain(host.Owner.ToString(), body);
            Assert.Equal(0, host.Storage.Opens);
            Assert.Equal(0, host.Commands.PhotoQueries);
            if (status == 404)
            {
                using var missing = await host.Get("owner", Guid.NewGuid());
                var one = JsonDocument.Parse(body).RootElement;
                var two = JsonDocument.Parse(await missing.Content.ReadAsStringAsync(Token)).RootElement;
                foreach (var name in new[] { "type", "title", "status", "detail", "errorCode" })
                    Assert.Equal(one.GetProperty(name).ToString(), two.GetProperty(name).ToString());
            }
        }
        Assert.Equal(before, await Snapshot());
        Assert.Equal(0, host.Commands.Dml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpOriginalContent_RangeIgnored_NoReopen_AndNoEffects(bool jpeg)
    {
        await using var host = await Start(jpeg: jpeg);
        var before = await Snapshot();
        var replaced = false;
        host.Storage.OpenOverride = async ct => new ReplaceOnClose(
            await host.Storage.Inner.OpenReadAsync(host.StorageKey, ct), async () =>
            {
                await File.WriteAllBytesAsync(Path.Combine(host.Root, host.StorageKey), "changed"u8.ToArray(), Token);
                replaced = true;
            });
        using var request = host.Request("owner", host.Project);
        request.Headers.Range = new RangeHeaderValue(0, 1);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"anything\""));
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(jpeg ? "image/jpeg" : "image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Null(response.Content.Headers.ContentDisposition.FileName);
        Assert.Null(response.Headers.ETag); Assert.False(response.Headers.Contains("Accept-Ranges"));
        Assert.Equal(host.Bytes, await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(host.Bytes.Length, response.Content.Headers.ContentLength);
        Headers(response);
        Assert.Equal(1, host.Storage.Opens); Assert.Equal(0, host.Storage.Deletes);
        Assert.True(replaced);
        Assert.Equal("changed"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(host.Root, host.StorageKey), Token));
        Assert.Equal(before, await Snapshot()); Assert.Equal(0, host.Commands.Dml);
    }

    [Theory]
    [InlineData("manual", 204)]
    [InlineData("historical", 204)]
    [InlineData("other-project", 204)]
    [InlineData("ambiguous", 500)]
    [InlineData("cross-client", 500)]
    [InlineData("inactive-project-client", 200)]
    public async Task HttpRelationship_AbsenceIsNotCorruption(string scenario, int status)
    {
        await using var host = await Start(photo: scenario is not ("manual" or "historical"), submission: scenario != "manual");
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario == "ambiguous") db.Add(Submission(host.Project, host.ClientId));
            if (scenario is "cross-client" or "other-project")
            {
                var otherClient = NewClient(host.Owner); db.Add(otherClient);
                var otherProject = Project.Create(otherClient.Id, "OTHER", "Other", null, null, host.Owner, At); db.Add(otherProject);
                await db.SaveChangesAsync(Token);
                if (scenario == "cross-client")
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.public_experience_submissions SET client_id={otherClient.Id} WHERE project_id={host.Project}", Token);
                else
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.public_experience_submissions SET client_id={otherClient.Id},project_id={otherProject.Id} WHERE project_id={host.Project}", Token);
            }
            if (scenario == "inactive-project-client")
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET is_active=false WHERE id={host.Project}", Token);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.clients SET is_active=false WHERE id={host.ClientId}", Token);
            }
            await db.SaveChangesAsync(Token);
        }
        var before = await Snapshot();
        using var response = await host.Get("owner", host.Project);
        Assert.Equal(status, (int)response.StatusCode); Headers(response);
        if (status == 204) Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        if (status == 500) await SafeError(response);
        Assert.Equal(status == 200 ? 1 : 0, host.Storage.Opens);
        Assert.Equal(before, await Snapshot()); Assert.Equal(0, host.Commands.Dml);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("same-length")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("io")]
    [InlineData("query")]
    public async Task HttpContentFailure_Safe500_NotAbsence_NoPartialImage(string fault)
    {
        await using var host = await Start();
        var path = Path.Combine(host.Root, host.StorageKey);
        if (fault == "missing") File.Delete(path);
        if (fault == "same-length") await File.WriteAllBytesAsync(path, new byte[host.Bytes.Length], Token);
        if (fault == "short") await File.WriteAllBytesAsync(path, host.Bytes[..^1], Token);
        if (fault == "long") await File.WriteAllBytesAsync(path, [..host.Bytes, 1], Token);
        if (fault == "io") host.Storage.OpenOverride = _ => throw new IOException("secret path");
        if (fault == "query") host.Commands.FailPhotoQuery = true;
        var before = await Snapshot();
        using var response = await host.Get("owner", host.Project);
        await SafeError(response); Headers(response);
        Assert.Equal(before, await Snapshot()); Assert.Equal(0, host.Commands.Dml);
        Assert.Equal(0, host.Storage.Deletes);
    }

    private static void Headers(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }
    private static async Task SafeError(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("PROJECT_IDEA_PHOTO_UNAVAILABLE", body);
        Assert.DoesNotContain("secret", body); Assert.DoesNotContain("SELECT", body);
        Assert.DoesNotContain("StorageKey", body); Assert.DoesNotContain("PNG", body);
    }

    private async Task<string> Snapshot()
    {
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.RequirementProcessingJobs.AnyAsync(Token));
        Assert.False(await db.RequirementProcessingAttempts.AnyAsync(Token));
        Assert.False(await db.Set<global::Domain.PreQuotes.RequirementExecutionEvidence>().AnyAsync(Token));
        return JsonSerializer.Serialize(new {
            Submissions = await db.Set<PublicExperienceSubmission>().AsNoTracking().Include(s => s.Files).Include(s => s.IdeaPhoto).OrderBy(s => s.Id).ToListAsync(Token),
            Projects = await db.Projects.AsNoTracking().OrderBy(p => p.Id).ToListAsync(Token),
            Clients = await db.Clients.AsNoTracking().OrderBy(p => p.Id).ToListAsync(Token)
        });
    }

    private static Client NewClient(Guid owner) => Client.Create(ClientType.Person, "Client", null,
        null, null, null, null, null, "City", owner, At);
    private static PublicExperienceSubmission Submission(Guid project, Guid client)
    {
        var row = new PublicExperienceSubmission { SubmissionId=Guid.NewGuid().ToString(), SchemaVersion="sng-public-experience-v1",
            Source="SNG_WEB", ClientFullName="Client", ClientWhatsapp="1", AdvisorCode="A", AdvisorDisplayName="Advisor",
            ProjectName="Project", ProjectCity="City", AnswersJson="{}", PayloadFingerprint=new string('A',64), CreatedAtUtc=At, UpdatedAtUtc=At };
        row.BeginProcessing(At); row.CompleteProcessing(client, project, At); return row;
    }

    private async Task<Host> Start(bool jpeg = false, bool photo = true, bool submission = true)
    {
        fixture.RequireAvailable(); await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-photo-a2-").FullName;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var commands = new Commands();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(fixture.ConnectionString).AddInterceptors(commands));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<IIdentityRepository, IdentityRepository>();
        builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
        builder.Services.AddScoped<IClientRepository, ClientRepository>();
        builder.Services.AddScoped<IProjectIdeaPhotoRepository, ProjectIdeaPhotoRepository>();
        builder.Services.AddScoped<GetProjectIdeaPhotoService>();
        builder.Services.AddScoped<global::Application.PreQuotes.GetRequirementDetails.GetRequirementDetailsService>();
        builder.Services.AddScoped<global::Application.PreQuotes.GetRequirementDetails.DownloadRequirementDocumentService>();
        builder.Services.AddSingleton<IInitialContentCapture, InitialContentCapture>();
        builder.Services.AddScoped<global::Application.Projects.GetProjectWorkspace.GetProjectWorkspaceService>();
        builder.Services.AddScoped<FluentValidation.IValidator<global::Application.Projects.GetProjectWorkspace.GetProjectWorkspaceQuery>, global::Application.Projects.GetProjectWorkspace.GetProjectWorkspaceQueryValidator>();
        builder.Services.AddScoped<global::Application.Common.Abstractions.PreQuotes.IPreQuoteRepository, PreQuoteRepository>();
        builder.Services.AddScoped<global::Application.Common.Abstractions.PreQuotes.IRequirementRepository, RequirementRepository>();
        builder.Services.AddScoped<global::Application.PublicExperience.IPublicExperienceProjectContextRepository, PublicExperienceSubmissionRepository>();
        var storage = new ObservedStorage(new LocalFileStorage(new FileStorageOptions(root), builder.Environment));
        var copies = new ObservedCopies();
        builder.Services.AddSingleton<IFileStorage>(storage); builder.Services.AddSingleton<IVerifiedContentCopy>(copies);
        builder.Services.AddControllers().AddApplicationPart(typeof(ProjectIdeaPhotoController).Assembly);
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
            o.MapInboundClaims=false;
            o.TokenValidationParameters=new TokenValidationParameters { ValidIssuer="photo-test", ValidAudience="photo-test",
                IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), ValidateLifetime=true };
            o.Events=new JwtBearerEvents { OnChallenge=async c => { c.HandleResponse(); await ApiProblemDetailsFactory.WriteUnauthorizedAsync(c.HttpContext); } };
        });
        var app=builder.Build();
        app.UseRouting(); app.UseProjectIdeaPhotoHeaders(); app.UseDocumentDownloadHeaders(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        var owner=User.CreateFromGoogle("owner@photo.test","Owner",null,null,At);
        var admin=User.CreateFromGoogle("admin@photo.test","Admin",null,null,At); admin.ChangeRole(UserRole.Admin,At);
        var stranger=User.CreateFromGoogle("other@photo.test","Other",null,null,At);
        var client=NewClient(owner.Id);
        var project=Project.Create(client.Id,"PHOTO","Project",null,null,owner.Id,At);
        var bytes=jpeg ? IdeaPhotoCaptureTests.Jpeg : IdeaPhotoCaptureTests.Png;
        const string storageKey="reference/original";
        await using (var db=fixture.CreateDbContext())
        {
            db.AddRange(owner,admin,stranger,client,project);
            if(submission)
            {
                var row=Submission(project.Id,client.Id);
                if(photo)
                {
                    using var source=new MemoryStream(bytes);
                    var receipt=await storage.Inner.SaveVerifiedAsync(storageKey,source,bytes.Length,20*1024*1024,Token);
                    row.AttachIdeaPhoto(PublicExperienceIdeaPhoto.Create(Guid.NewGuid(),row.Id,jpeg ? "secret-name.jpg" : "secret-name.png",
                        jpeg ? "image/jpeg" : "image/png",storageKey,receipt.Sha256,receipt.Length,At));
                }
                db.Add(row);
            }
            await db.SaveChangesAsync(Token);
        }
        await app.StartAsync(Token);
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Host(app,new HttpClient {BaseAddress=new Uri(address)},root,owner.Id,admin.Id,stranger.Id,project.Id,client.Id,bytes,storageKey,storage,copies,commands,output);
    }

    private sealed record Host(WebApplication App,HttpClient Client,string Root,Guid Owner,Guid Admin,Guid Stranger,
        Guid Project,Guid ClientId,byte[] Bytes,string StorageKey,ObservedStorage Storage,ObservedCopies Copies,Commands Commands,ITestOutputHelper Output) : IAsyncDisposable
    {
        internal HttpRequestMessage Request(string actor,Guid project)
        {
            var request=new HttpRequestMessage(HttpMethod.Get,$"/api/v2/projects/{project}/idea-photo");
            if(actor != "anonymous")
            {
                var id=actor == "admin" ? Admin : actor == "stranger" ? Stranger : Owner;
                var jwt=new JwtSecurityToken("photo-test","photo-test",[new Claim("sub",id.ToString())],
                    expires:DateTime.UtcNow.AddMinutes(5),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256));
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",actor == "invalid-token" ? "invalid" : new JwtSecurityTokenHandler().WriteToken(jwt));
            }
            return request;
        }
        internal async Task<HttpResponseMessage> Get(string actor,Guid project)
        { using var request=Request(actor,project); return await Client.SendAsync(request,Token); }
        internal GetProjectIdeaPhotoService Service(ApplicationDbContext db, IProjectIdeaPhotoRepository? photos=null)
        {
            var actor=Substitute.For<ICurrentUser>(); actor.IsAuthenticated.Returns(true); actor.UserId.Returns(Owner);
            return new(actor,new IdentityRepository(db),new ProjectRepository(db),new ClientRepository(db),photos ?? new ProjectIdeaPhotoRepository(db),
                Storage,Copies,NullLogger<GetProjectIdeaPhotoService>.Instance);
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await App.StopAsync(CancellationToken.None); await App.DisposeAsync();
            foreach (var copy in Copies.All) await copy.DisposeAsync();
            Directory.Delete(Root,true); Output.WriteLine($"Removed storage: {Root}");
        }
    }

    private sealed class Commands : DbCommandInterceptor
    {
        internal int Dml,PhotoQueries; internal bool FailPhotoQuery;
        private void Check(DbCommand command)
        {
            if(System.Text.RegularExpressions.Regex.IsMatch(command.CommandText,@"\b(INSERT|UPDATE|DELETE)\b")) Interlocked.Increment(ref Dml);
            if(command.CommandText.Contains("public_experience_idea_photos"))
            { Interlocked.Increment(ref PhotoQueries); if(FailPhotoQuery) throw new IOException("secret SQL"); }
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        { Check(command); return ValueTask.FromResult(result); }
    }
    private sealed class ObservedStorage(LocalFileStorage inner) : IFileStorage
    {
        internal LocalFileStorage Inner=inner; internal int Opens,Deletes;
        internal Func<CancellationToken,Task<Stream>>? OpenOverride;
        public async Task<Stream> OpenReadAsync(string key,CancellationToken ct)
        { Interlocked.Increment(ref Opens); return OpenOverride is null ? await Inner.OpenReadAsync(key,ct) : await OpenOverride(ct); }
        public Task DeleteIfExistsAsync(string key,CancellationToken ct) { Deletes++; throw new InvalidOperationException(); }
        public Task SaveAsync(string key,Stream stream,CancellationToken ct) => throw new InvalidOperationException();
        public Task<StoredContentReceipt> SaveVerifiedAsync(string key,Stream stream,long length,long max,CancellationToken ct) => throw new InvalidOperationException();
    }
    private sealed class ObservedCopies : IVerifiedContentCopy
    {
        internal List<ObservedCopy> All=[];
        public async Task<ICapturedContent> PrepareAsync(Stream source,long length,string hash,long max,CancellationToken ct)
        {
            var copy=await new VerifiedContentCopy(NullLogger<VerifiedContentCopy>.Instance).PrepareAsync(source,length,hash,max,ct);
            var observed=new ObservedCopy(copy); All.Add(observed);
            return observed;
        }
    }
    private sealed class ObservedCopy(ICapturedContent inner) : ICapturedContent
    {
        internal bool Closed;
        public string Sha256 => inner.Sha256;
        public long Length => inner.Length;
        public Stream OpenRead() => inner.OpenRead();
        public async ValueTask DisposeAsync() { await inner.DisposeAsync(); Closed=true; }
    }
    private sealed class ReplaceOnClose(Stream inner, Func<Task> afterClose) : Stream
    {
        private bool closed;
        public override bool CanRead=>inner.CanRead; public override bool CanSeek=>false; public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)=>inner.ReadAsync(buffer,ct);
        public override async ValueTask DisposeAsync()
        { if(closed) return; closed=true; await inner.DisposeAsync(); await afterClose(); GC.SuppressFinalize(this); }
        public override int Read(byte[] b,int o,int c)=>inner.Read(b,o,c);
        public override void Flush()=>throw new NotSupportedException();
        public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long length)=>throw new NotSupportedException();
    }
}
