using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Operations;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class HostOutstandingReader : IVerifiedExecutionDocumentClient
    {
        internal Stream? Reader;
        internal Task<int>? Reading;
        internal int Calls;
        public Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest request, CancellationToken ct)
        {
            Calls++; Reader = request.Files[0].Content;
            Reading = Reader.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).AsTask();
            return Task.FromResult(ConsumingClient.Response(request));
        }
    }

    [Fact]
    public async Task ExperienceHost_ShutdownOutstandingPhysicalRead_BudgetDoesNotDisposeOwnedCopies()
    {
        await using var files = new Files(); await HostQueued(files);
        var copies = new BlockingReadCopies(); var client = new HostOutstandingReader();
        var probe = new HostProbe(); probe.Release.TrySetResult();
        using var host = DatabaseHost(files, new(), new(), probe, s =>
        {
            s.AddScoped<IVerifiedExecutionDocumentClient>(_ => client);
            s.AddScoped<IVerifiedContentCopy>(_ => copies);
            s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMilliseconds(100));
        });
        var worker = Assert.Single(host.Services.GetServices<IHostedService>().OfType<ExperienceExecutionHost>());
        await host.StartAsync(Token);
        try
        {
            await copies.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (client.Reader is null || client.Reader.CanRead) { deadline.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
            await host.StopAsync(Token);
            Assert.False(worker.ExecuteTask!.IsCompleted); Assert.Equal(0, copies.Disposals);
            Assert.False(client.Reading!.IsCompleted); Assert.Equal(1, client.Calls);
        }
        finally
        {
            copies.Release.TrySetResult();
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), Token);
            if (client.Reading is not null) await client.Reading;
            await host.StopAsync(Token);
        }
        Assert.Equal(2, copies.Disposals); Assert.Equal(0, files.Deletes);
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
        var job = await CombinedJob(); Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.FinishedAtUtc);
    }

    [Fact]
    public async Task ExperienceHost_RenewDuringPhysicalCopy_NoSecondHeartbeat()
    {
        await using var files = new Files(); await HostQueued(files);
        var copies = new Copies(); var client = new ConsumingClient(); var clock = new ManualSupervisionClock();
        var entered = Signal(); var release = Signal();
        copies.AfterCopy = async () => { copies.AfterCopy = null; entered.TrySetResult(); await release.Task.WaitAsync(Token); };
        var calls = SupervisionProbe(); var probe = new HostProbe(); probe.Release.TrySetResult();
        using var host = DatabaseHost(files, client, clock, probe, s =>
        { s.AddScoped<IVerifiedContentCopy>(_ => copies); s.AddScoped<IActiveLeaseRenewalStore>(_ => calls); });
        await host.StartAsync(Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal(LeaseOperationState.Renewed, (await SupervisionRead(calls.Renewed)).State);
            Assert.Equal(1, calls.Renewals); Assert.Equal(0, client.Calls);
            release.TrySetResult(); await SupervisionRead(probe.Finished);
            Assert.Equal(RequirementProcessingJobState.Succeeded, (await CombinedJob()).State);
            Assert.Equal(1, client.Calls); Released(files, copies, client);
        }
        finally { release.TrySetResult(); await host.StopAsync(Token); }
    }

    [Fact]
    public async Task ExperienceHost_ShutdownAfterRealPublicationCommit_PreservesSucceeded()
    {
        await using var files = new Files(); await HostQueued(files); var client = new ConsumingClient();
        var entered = Signal(); var release = Signal(); var probe = new HostProbe(); probe.Release.TrySetResult();
        var hook = new SupervisionAfterCommit(async () => { entered.TrySetResult(); await release.Task.WaitAsync(Token); });
        using var host = DatabaseHost(files, client, new(), probe, s =>
        {
            s.AddScoped<IJobPublicationStore>(_ => new JobPublicationStore(H.Options(hook)));
            s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMilliseconds(100));
        });
        var worker = Assert.Single(host.Services.GetServices<IHostedService>().OfType<ExperienceExecutionHost>());
        await host.StartAsync(Token);
        string? committed = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.Equal(RequirementProcessingJobState.Succeeded, (await CombinedJob()).State);
            committed = await CombinedSnapshot();
            await host.StopAsync(Token); Assert.False(worker.ExecuteTask!.IsCompleted);
        }
        finally { release.TrySetResult(); await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), Token); await host.StopAsync(Token); }
        Assert.NotNull(committed); Assert.Equal(committed, await CombinedSnapshot()); Assert.Equal(1, client.Calls);
        Assert.Null((await CombinedJob()).CancellationRequestedAtUtc);
    }
}
