using Infrastructure.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class SchedulingProbe
    {
        internal int Queries, Disposals;
        internal readonly List<Guid> Jobs = [];
        internal readonly TaskCompletionSource Entered = Signal(), Release = Signal();
        internal bool Hold;
        internal bool FailDiscovery;
        internal readonly Guid First = Guid.NewGuid(), Second = Guid.NewGuid();
    }
    private sealed class SchedulingReader(SchedulingProbe probe) : IExperienceCandidateReader
    {
        public Task<IReadOnlyList<ExperienceCandidate>> ReadAsync(ExperienceCandidate? after, int limit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Assert.Equal(1, limit); probe.Queries++;
            if (probe.FailDiscovery) throw new IOException("test discovery fault");
            IReadOnlyList<ExperienceCandidate> rows = after is null ? [new(probe.First, DateTimeOffset.UnixEpoch)]
                : after.Id == probe.First ? [new(probe.Second, DateTimeOffset.UnixEpoch.AddSeconds(1))] : [];
            return Task.FromResult(rows);
        }
    }
    private sealed class SchedulingExecution(SchedulingProbe probe) : IExperienceInitialExecution, IAsyncDisposable
    {
        public async Task ExecuteAsync(Guid id, CancellationToken ct)
        {
            probe.Jobs.Add(id); probe.Entered.TrySetResult();
            if (probe.Hold) await probe.Release.Task;
            if (id == probe.First && !probe.Hold) throw new IOException("test invocation fault");
        }
        public ValueTask DisposeAsync() { probe.Disposals++; return ValueTask.CompletedTask; }
    }
    private IHost SchedulingHost(SchedulingProbe probe, ManualSupervisionClock clock, bool enabled = true)
    {
        var builder = Host.CreateApplicationBuilder(); builder.Logging.ClearProviders();
        builder.Services.AddExperienceExecutionHost(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ExperienceExecutionHost:Enabled"] = enabled.ToString(), ["ExperienceExecutionHost:BatchSize"] = "1" }).Build());
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddScoped<IExperienceCandidateReader>(_ => new SchedulingReader(probe));
        builder.Services.AddScoped<IExperienceInitialExecution>(_ => new SchedulingExecution(probe));
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMilliseconds(100));
        return builder.Build();
    }

    [Fact]
    public async Task ExperienceHost_KeysetAdvancesPastRejection_OneBatchPerDelay()
    {
        var probe = new SchedulingProbe(); var clock = new ManualSupervisionClock(); using var host = SchedulingHost(probe, clock);
        await host.StartAsync(Token);
        try
        {
            Assert.Equal(TimeSpan.FromSeconds(5), await SupervisionRead(clock.Scheduled));
            Assert.Equal(1, probe.Queries); Assert.Equal(new[] { probe.First }, probe.Jobs);
            clock.Advance(TimeSpan.FromSeconds(4)); Assert.Equal(1, probe.Queries);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(TimeSpan.FromSeconds(5), await SupervisionRead(clock.Scheduled));
            Assert.Equal(new[] { probe.First, probe.Second }, probe.Jobs); Assert.Equal(2, probe.Disposals);
            clock.Advance(TimeSpan.FromSeconds(5)); await SupervisionRead(clock.Scheduled);
            Assert.Equal(3, probe.Queries); Assert.Equal(2, probe.Jobs.Count);
        }
        finally { await host.StopAsync(Token); }
    }

    [Fact]
    public async Task ExperienceHost_DiscoveryFault_WaitsBeforeNextQuery()
    {
        var probe = new SchedulingProbe { FailDiscovery = true }; var clock = new ManualSupervisionClock();
        using var host = SchedulingHost(probe, clock); await host.StartAsync(Token);
        try
        {
            await SupervisionRead(clock.Scheduled); Assert.Equal(1, probe.Queries);
            clock.Advance(TimeSpan.FromSeconds(4)); Assert.Equal(1, probe.Queries); Assert.Empty(probe.Jobs);
            clock.Advance(TimeSpan.FromSeconds(1)); await SupervisionRead(clock.Scheduled); Assert.Equal(2, probe.Queries);
        }
        finally { await host.StopAsync(Token); }
    }

    [Fact]
    public async Task ExperienceHost_RealHostingBudgetExpires_ScopeLivesUntilTaskDrains()
    {
        var probe = new SchedulingProbe { Hold = true }; var clock = new ManualSupervisionClock();
        using var host = SchedulingHost(probe, clock);
        var worker = Assert.Single(host.Services.GetServices<IHostedService>().OfType<ExperienceExecutionHost>());
        await host.StartAsync(Token);
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await host.StopAsync(Token);
            Assert.NotNull(worker.ExecuteTask); Assert.False(worker.ExecuteTask.IsCompleted);
            Assert.Equal(0, probe.Disposals); Assert.Single(probe.Jobs);
        }
        finally
        {
            probe.Release.TrySetResult();
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token);
        }
        Assert.Equal(1, probe.Disposals); Assert.Equal(1, probe.Queries);
    }

    [Theory]
    [InlineData(0, 5)] [InlineData(101, 5)] [InlineData(1, 0)] [InlineData(1, -1)]
    public void ExperienceHost_InvalidOptionsFailClosed(int batch, int seconds)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => new ExperienceExecutionHost(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExperienceExecutionHostOptions { BatchSize = batch, PollInterval = TimeSpan.FromSeconds(seconds) }),
            TimeProvider.System, services.GetRequiredService<ILogger<ExperienceExecutionHost>>()));
    }
}
