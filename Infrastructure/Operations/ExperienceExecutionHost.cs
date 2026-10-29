using Application.PreQuotes.ProcessRequirement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Operations;

public sealed class ExperienceExecutionHostOptions
{
    public const string SectionName = "ExperienceExecutionHost";
    public bool Enabled { get; set; }
    public int BatchSize { get; set; } = 16;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}

public sealed record ExperienceCandidate(Guid Id, DateTimeOffset CreatedAtUtc);
public interface IExperienceCandidateReader
{
    Task<IReadOnlyList<ExperienceCandidate>> ReadAsync(ExperienceCandidate? after, int limit, CancellationToken ct);
}

public interface IExperienceInitialExecution
{
    Task ExecuteAsync(Guid jobId, CancellationToken ct);
}

// This scope owns the whole invocation, including the supervisor's cooperative drain.
public sealed class ExperienceInitialExecution(ExperienceAdmissionService admission,
    SuperviseAssignedExecutionService supervisor, TimeProvider time, ILogger<ExperienceInitialExecution> logger)
    : IExperienceInitialExecution
{
    public async Task ExecuteAsync(Guid jobId, CancellationToken ct)
    {
        var phase = "Admission";
        var started = time.GetTimestamp();
        logger.LogInformation("Experience {Phase} started JobId={JobId} AtUtc={AtUtc}", phase, jobId, time.GetUtcNow());
        try
        {
            var result = await admission.AdmitAsync(jobId, ct);
            logger.LogInformation("Experience admission JobId={JobId} State={State} Failure={Failure} AtUtc={AtUtc} ElapsedMs={ElapsedMs}",
                jobId, result.State, result.Failure, time.GetUtcNow(), time.GetElapsedTime(started).TotalMilliseconds);
            ct.ThrowIfCancellationRequested();
            if (result.State != ExperienceAdmissionState.Admitted || result.Permit is null) return;
            phase = "Supervision";
            started = time.GetTimestamp();
            logger.LogInformation("Experience {Phase} started JobId={JobId} InstanceId={InstanceId} AtUtc={AtUtc}",
                phase, jobId, result.Permit.Admission.InstanceId, time.GetUtcNow());
            var execution = await supervisor.ExecuteAsync(result.Permit, ct);
            logger.LogInformation("Experience supervision JobId={JobId} Local={Local} Stop={Stop} Coordinator={Coordinator} CoordinatorCancelled={CoordinatorCancelled} Durable={Durable} ObservationFailure={ObservationFailure} AtUtc={AtUtc} ElapsedMs={ElapsedMs}",
                jobId, execution.Outcome, execution.StopReason, execution.Coordinator?.Outcome, execution.CoordinatorCancelled,
                execution.Observation?.Snapshot?.JobState, execution.Observation?.Failure, time.GetUtcNow(), time.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Experience {Phase} cancelled JobId={JobId} AtUtc={AtUtc} ElapsedMs={ElapsedMs}; durable outcome not inferred",
                phase, jobId, time.GetUtcNow(), time.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception error)
        {
            // Exception messages may contain provider payloads or connection details.
            logger.LogError("Experience {Phase} fault JobId={JobId} ErrorType={ErrorType} AtUtc={AtUtc} ElapsedMs={ElapsedMs}; no retry",
                phase, jobId, error.GetType().Name, time.GetUtcNow(), time.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}

public sealed class ExperienceExecutionHost : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly TimeProvider time;
    private readonly ILogger<ExperienceExecutionHost> logger;
    private readonly bool enabled;
    private readonly int batchSize;
    private readonly TimeSpan interval;

    public ExperienceExecutionHost(IServiceScopeFactory scopes, IOptions<ExperienceExecutionHostOptions> options,
        TimeProvider time, ILogger<ExperienceExecutionHost> logger)
    {
        this.scopes = scopes; this.time = time; this.logger = logger;
        enabled = options.Value.Enabled; batchSize = options.Value.BatchSize; interval = options.Value.PollInterval;
        if (batchSize is < 1 or > 100 || interval <= TimeSpan.Zero || interval > TimeSpan.FromDays(1))
            throw new OptionsValidationException(ExperienceExecutionHostOptions.SectionName, typeof(ExperienceExecutionHostOptions),
                ["BatchSize must be 1..100 and PollInterval must be positive and at most one day."]);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Experience host started Enabled={Enabled} BatchSize={BatchSize} Interval={Interval} AtUtc={AtUtc}",
            enabled, batchSize, interval, time.GetUtcNow());
        if (!enabled) return;
        ExperienceCandidate? cursor = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var started = time.GetTimestamp();
                try
                {
                    IReadOnlyList<ExperienceCandidate> candidates;
                    logger.LogInformation("Experience discovery started AtUtc={AtUtc}", time.GetUtcNow());
                    await using (var scope = scopes.CreateAsyncScope())
                        candidates = await scope.ServiceProvider.GetRequiredService<IExperienceCandidateReader>()
                            .ReadAsync(cursor, batchSize, stoppingToken);
                    logger.LogInformation("Experience discovery finished Count={Count} AtUtc={AtUtc} ElapsedMs={ElapsedMs}",
                        candidates.Count, time.GetUtcNow(), time.GetElapsedTime(started).TotalMilliseconds);
                    foreach (var candidate in candidates)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        cursor = candidate;
                        try
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            await scope.ServiceProvider.GetRequiredService<IExperienceInitialExecution>().ExecuteAsync(candidate.Id, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        {
                            logger.LogError("Experience invocation ended JobId={JobId} ErrorType={ErrorType} AtUtc={AtUtc}; no retry",
                                candidate.Id, error.GetType().Name, time.GetUtcNow());
                        }
                    }
                    if (candidates.Count < batchSize) cursor = null;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    logger.LogError("Experience discovery fault ErrorType={ErrorType} AtUtc={AtUtc} ElapsedMs={ElapsedMs}",
                        error.GetType().Name, time.GetUtcNow(), time.GetElapsedTime(started).TotalMilliseconds);
                }
                await Task.Delay(interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { logger.LogInformation("Experience host execution task completed AtUtc={AtUtc}", time.GetUtcNow()); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        logger.LogInformation("Experience shutdown requested AtUtc={AtUtc}", time.GetUtcNow());
        try { await base.StopAsync(cancellationToken); }
        finally
        {
            var completed = ExecuteTask?.IsCompleted != false;
            logger.LogWarning("Experience shutdown DrainCompleted={DrainCompleted} BudgetExpired={BudgetExpired} AtUtc={AtUtc} ElapsedMs={ElapsedMs}",
                completed, cancellationToken.IsCancellationRequested, time.GetUtcNow(), time.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
