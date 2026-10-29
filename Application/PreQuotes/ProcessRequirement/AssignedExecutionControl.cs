namespace Application.PreQuotes.ProcessRequirement;

// One invocation only. Admission orders local work, never grants durable authority.
public sealed class AssignedExecutionControl : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource<AssignedJobExecution?> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool stopped, attached, completed, disposed;
    public CancellationToken StopToken { get; }
    public Task<AssignedJobExecution?> Started => started.Task;
    public bool IsStopped { get { lock (gate) return stopped; } }

    public AssignedExecutionControl() => StopToken = stop.Token;

    public bool TryAdmit(AssignedExecutionStage stage)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        lock (gate) return !stopped && !completed && !disposed;
    }

    public void Stop()
    {
        lock (gate)
        {
            if (stopped || disposed) return;
            stopped = true;
        }
        // Callbacks may execute arbitrary cancellation code; never invoke them under the admission lock.
        try { stop.Cancel(); }
        catch (AggregateException) { /* A failing callback cannot undo Stop or suppress cancellation of the rest. */ }
        catch (ObjectDisposedException) { /* Disposal is only permitted after the invocation completes. */ }
    }

    internal void Attach()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (attached) throw new InvalidOperationException("Execution control is single-use.");
            attached = true;
        }
    }

    internal void NotifyStarted(AssignedJobExecution execution) => started.TrySetResult(execution);
    internal void Complete()
    {
        lock (gate) completed = true;
        started.TrySetResult(null);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (attached && !completed) throw new InvalidOperationException("Drain execution before disposing its control.");
            disposed = true;
        }
        started.TrySetResult(null);
        stop.Dispose();
    }
}
