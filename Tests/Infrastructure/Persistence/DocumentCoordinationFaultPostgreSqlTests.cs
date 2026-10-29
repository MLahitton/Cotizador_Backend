using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;
using static CotizadorBackend.Tests.Infrastructure.Persistence.ReplaceDocumentFileSafetyPostgreSqlTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class DocumentCoordinationPostgreSqlTests
{
    [Theory]
    [InlineData("before_save", false)]
    [InlineData("save_response", false)]
    [InlineData("persistence", false)]
    [InlineData("commit_response", true)]
    [InlineData("cancel_save", false)]
    [InlineData("cancel_commit", false)]
    [InlineData("cancel_committed", true)]
    [InlineData("response", true)]
    [InlineData("cancel_response", true)]
    public async Task PostCancellationReplacement_RollbackAndUncertainCommitRetainFiles(string fault, bool committed)
    {
        var seed = await SeedState("cancelled_running");
        using var storage = await Files(seed);
        var before = await Protocol.P.Snapshot();
        var graph = await HistoricalGraph();
        var oldFiles = await FileSnapshot();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var hook = new Faults(fault, cancel);
        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var db = new ApplicationDbContext(Protocol.H.Options(hook, new SaveFault(hook), new ResponseFault(hook)));
            using var content = new MemoryStream([8, 9]);
            var result = await Service(db, seed.Actor, storage).ReplaceRequirementAsync(
                new(seed.Execution.RequirementId, [new("next.pdf", "application/pdf", 2, content)]), cancel.Token);
            Assert.False(result.IsSuccess);
        });
        if (fault.StartsWith("cancel_", StringComparison.Ordinal)) Assert.IsAssignableFrom<OperationCanceledException>(exception);
        else Assert.Null(exception);
        Assert.True(hook.Injected);
        Assert.Equal(committed, hook.Committed);
        Assert.Equal(graph, await HistoricalGraph());
        Assert.Equal(oldFiles, await FileSnapshot(seed.Execution.RequirementId));
        if (!committed) Assert.Equal(before, await Protocol.P.Snapshot());
        await using var read = fixture.CreateDbContext();
        Assert.Equal(committed ? 2 : 1, await read.Requirements.CountAsync(Token));
        Assert.Equal(1, await read.Requirements.CountAsync(r => r.IsActive, Token));
        Assert.Equal(committed ? 2 : 1, await read.RequirementFiles.CountAsync(Token));
        Assert.Equal(2, Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(new byte[10], await storage.Bytes("tests/no-read.pdf"));
        Assert.Equal(0, storage.Deletes);
        if (committed)
        {
            var successor = await read.Requirements.AsNoTracking().SingleAsync(r => r.Id != seed.Execution.RequirementId, Token);
            var original = await read.Requirements.AsNoTracking().SingleAsync(r => r.Id == seed.Execution.RequirementId, Token);
            Assert.Equal(successor.Id, original.SupersededByRequirementId);
            Assert.Equal(original.Id, successor.SupersedesRequirementId);
            Assert.True(successor.IsCurrent);
            var file = await read.RequirementFiles.AsNoTracking().SingleAsync(f => f.RequirementId == successor.Id, Token);
            Assert.Equal(new byte[] { 8, 9 }, await storage.Bytes(file.StorageKey));
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 8, 9 })), file.StoredSha256);
            Assert.Equal(2, file.StoredLengthBytes);
            Assert.Equal((short)1, file.StoredEvidenceVersion);
            Assert.NotNull(file.StoredEvidenceAtUtc);
        }
    }
}
