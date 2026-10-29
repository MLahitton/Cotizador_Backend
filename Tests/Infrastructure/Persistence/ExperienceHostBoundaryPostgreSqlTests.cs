using System.Data.Common;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Operations;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private async Task<Seed> HostQueued(Files files)
    {
        var (seed, _) = await PermitSetup(files);
        // Fixture only: prepare a fresh unadmitted intention with valid incorporation metadata.
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET dispatch_admitted_at_utc=NULL, dispatch_instance_id=NULL, dispatch_claim_operation_id=NULL", Token);
        return seed;
    }
    private IHost DatabaseHost(Files files, ConsumingClient client, ManualSupervisionClock clock, HostProbe probe,
        Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(); builder.Logging.ClearProviders();
        HostServices(builder.Services, files, client, clock, probe);
        configure?.Invoke(builder.Services);
        return builder.Build();
    }
    private sealed class HostCommitLost : DbTransactionInterceptor
    {
        internal bool Injected;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { Injected = true; throw new IOException("Simulated admission response loss"); }
    }
    private sealed class AdmissionInterrupted(IExperienceAuthorizationStore inner) : IExperienceAuthorizationStore
    {
        internal readonly TaskCompletionSource Admitted = Signal();
        public async Task<ExperienceAdmissionWriteResult> AdmitAsync(ExperienceAdmissionCandidate c, CancellationToken ct)
        {
            var result = await inner.AdmitAsync(c, ct);
            if (result.State == ExperienceAdmissionState.Admitted)
            { Admitted.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            return result;
        }
        public Task<ExperienceRevocationResult> RevokeAsync(Guid id, Guid actor, CancellationToken ct) => inner.RevokeAsync(id, actor, ct);
    }
    private sealed class RecordedHostAdmission(IExperienceAuthorizationStore inner) : IExperienceAuthorizationStore
    {
        internal ExperienceAdmissionWriteResult? Result;
        public async Task<ExperienceAdmissionWriteResult> AdmitAsync(ExperienceAdmissionCandidate c, CancellationToken ct)
        { Result = await inner.AdmitAsync(c, ct); return Result; }
        public Task<ExperienceRevocationResult> RevokeAsync(Guid id, Guid actor, CancellationToken ct) => inner.RevokeAsync(id, actor, ct);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperienceHost_AdmissionCommitLostOrInterrupted_NoClaim_NoResume(bool interrupted)
    {
        await using var files = new Files(); var seed = await HostQueued(files); var client = new ConsumingClient();
        var clock = new ManualSupervisionClock(); var probe = new HostProbe(); probe.Release.TrySetResult();
        var fault = new HostCommitLost(); var boundary = new AdmissionInterrupted(new ExperienceAuthorizationStore(H.Options()));
        using (var host = DatabaseHost(files, client, clock, probe, services => services.AddScoped<IExperienceAuthorizationStore>(_ =>
            interrupted ? boundary : new ExperienceAuthorizationStore(H.Options(fault)))))
        {
            await host.StartAsync(Token);
            try
            {
                if (interrupted) await boundary.Admitted.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
                else { Assert.Equal(seed.Execution.JobId, await SupervisionRead(probe.Finished)); Assert.True(fault.Injected); }
            }
            finally { await host.StopAsync(Token); }
        }
        var committed = await CombinedJob(); Assert.NotNull(committed.DispatchAdmittedAtUtc);
        Assert.Null(committed.StartedAtUtc); Assert.Equal(RequirementProcessingJobState.Queued, committed.State);
        var before = await CombinedSnapshot();
        var next = new HostProbe(); next.Release.TrySetResult(); var nextClock = new ManualSupervisionClock();
        using var restarted = DatabaseHost(files, client, nextClock, next);
        await restarted.StartAsync(Token);
        try { await SupervisionRead(nextClock.Scheduled); Assert.Equal(0, next.Invocations); Assert.Equal(0, client.Calls); }
        finally { await restarted.StopAsync(Token); }
        Assert.Equal(before, await CombinedSnapshot()); Assert.Equal(0, files.Opens);
    }

    [Fact]
    public async Task ExperienceHost_TwoHosts_AdmissionExclusion_PostgreSqlBlocking()
    {
        await using var files = new Files(); var seed = await HostQueued(files); var client = new ConsumingClient();
        var firstProbe = new HostProbe(); firstProbe.Release.TrySetResult(); var secondProbe = new HostProbe(); secondProbe.Release.TrySetResult();
        var gate = new PermitCommitGate(); var quote = new JobProtocolTestSupport.QuoteProbe();
        var firstAdmission = new RecordedHostAdmission(new ExperienceAuthorizationStore(H.Options(gate)));
        var secondAdmission = new RecordedHostAdmission(new ExperienceAuthorizationStore(H.Options(quote)));
        var firstCalls = SupervisionProbe(); var secondCalls = SupervisionProbe();
        using var first = DatabaseHost(files, client, new(), firstProbe, s =>
        { s.AddScoped<IExperienceAuthorizationStore>(_ => firstAdmission); s.AddScoped<IInitialLeaseClaimStore>(_ => firstCalls); });
        using var second = DatabaseHost(files, client, new(), secondProbe, s =>
        { s.AddScoped<IExperienceAuthorizationStore>(_ => secondAdmission); s.AddScoped<IInitialLeaseClaimStore>(_ => secondCalls); });
        await first.StartAsync(Token);
        try
        {
            var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            await second.StartAsync(Token);
            var loser = await quote.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.NotEqual(winner, loser); await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"Host admission winner={winner}; blocked={loser}; pg_blocking_pids observed before release");
            gate.Release.TrySetResult();
            Assert.Equal(seed.Execution.JobId, await SupervisionRead(firstProbe.Finished));
            Assert.Equal(seed.Execution.JobId, await SupervisionRead(secondProbe.Finished));
            Assert.NotNull(firstAdmission.Result); Assert.Equal(ExperienceAdmissionState.Admitted, firstAdmission.Result.State);
            Assert.NotNull(firstAdmission.Result.Confirmed);
            Assert.NotNull(secondAdmission.Result); Assert.Equal(ExperienceAdmissionState.AlreadyAdmitted, secondAdmission.Result.State);
            Assert.Null(secondAdmission.Result.Confirmed); Assert.Equal(1, firstCalls.Claims); Assert.Equal(0, secondCalls.Claims);
            Assert.Equal(1, client.Calls); Assert.Equal(RequirementProcessingJobState.Succeeded, (await CombinedJob()).State);
            await using var db = fixture.CreateDbContext(); Assert.Single(await db.RequirementProcessingAttempts.ToListAsync(Token));
            Assert.Single(await db.JobLeaseOperations.Where(x => x.Kind == JobLeaseOperationKind.Claim).ToListAsync(Token));
        }
        finally { gate.Release.TrySetResult(); await first.StopAsync(Token); await second.StopAsync(Token); }
    }

    [Theory]
    [InlineData("shutdown")] [InlineData("revoke")] [InlineData("request")]
    public async Task ExperienceHost_SupervisorRenewAndStop_NoHostFinalizerOrRestart(string stop)
    {
        await using var files = new Files(); var seed = await HostQueued(files); var client = new ConsumingClient();
        var clock = new ManualSupervisionClock(); var probe = new HostProbe(); probe.Release.TrySetResult();
        var entered = Signal(); var release = Signal(); var cancelled = Signal();
        client.Action = async (request, ct) =>
        {
            using var registration = ct.Register(() => cancelled.TrySetResult());
            entered.TrySetResult(); await release.Task.WaitAsync(Token); ct.ThrowIfCancellationRequested();
            return ConsumingClient.Response(request);
        };
        var calls = SupervisionProbe();
        using var host = DatabaseHost(files, client, clock, probe, s => s.AddScoped<IActiveLeaseRenewalStore>(_ => calls));
        var worker = Assert.Single(host.Services.GetServices<IHostedService>().OfType<ExperienceExecutionHost>());
        await host.StartAsync(Token); Task? shutdown = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal(LeaseOperationState.Renewed, (await SupervisionRead(calls.Renewed)).State);
            Assert.Equal(TimeSpan.FromSeconds(30), await SupervisionRead(clock.Scheduled));
            if (stop == "shutdown") shutdown = host.StopAsync(Token);
            else
            {
                if (stop == "revoke") Assert.Equal(ExperienceRevocationState.Revoked,
                    (await new ExperienceAuthorizationStore(H.Options()).RevokeAsync(seed.Execution.JobId, seed.Actor, Token)).State);
                else Assert.Equal(JobTerminationState.CancellationRequested, (await H.Service().RequestJobCancellationAsync(
                    new(seed.Execution.JobId, seed.Execution.ExecutionRequestId, seed.Execution.RequirementId), seed.Actor, Token)).State);
                clock.Advance(TimeSpan.FromSeconds(30));
            }
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.False(worker.ExecuteTask!.IsCompleted); Assert.Empty(await new ExperienceCandidateReader(H.Options()).ReadAsync(null, 1, Token));
            release.TrySetResult(); await SupervisionRead(probe.Finished);
            if (shutdown is not null) await shutdown;
            var job = await CombinedJob(); Assert.Equal(RequirementProcessingJobState.Running, job.State);
            Assert.Equal(stop == "request", job.CancellationRequestedAtUtc.HasValue); Assert.Null(job.FinishedAtUtc);
            Assert.Equal(1, client.Calls); Assert.All(client.Readers, x => Assert.False(x.CanRead));
        }
        finally { release.TrySetResult(); await host.StopAsync(Token); if (shutdown is not null) await shutdown; }
        var before = await CombinedSnapshot(); var restartedProbe = new HostProbe(); restartedProbe.Release.TrySetResult(); var nextClock = new ManualSupervisionClock();
        using var next = DatabaseHost(files, client, nextClock, restartedProbe); await next.StartAsync(Token);
        try { await SupervisionRead(nextClock.Scheduled); Assert.Equal(0, restartedProbe.Invocations); }
        finally { await next.StopAsync(Token); }
        Assert.Equal(before, await CombinedSnapshot()); Assert.Equal(1, client.Calls);
    }
}
