using Application.PreQuotes.ProcessRequirement;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class AssignedExecutionControlTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StopAndAdmission_DeterministicOrder(bool stopFirst)
    {
        using var control = new AssignedExecutionControl();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        if (stopFirst) control.Stop();
        var task = Task.Run(async () =>
        {
            if (!control.TryAdmit(AssignedExecutionStage.Documents)) return;
            Interlocked.Increment(ref calls); entered.SetResult();
            await release.Task;
            Assert.True(control.StopToken.IsCancellationRequested);
            Assert.False(control.TryAdmit(AssignedExecutionStage.Consumption));
        }, TestContext.Current.CancellationToken);
        try
        {
            if (!stopFirst) await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(control.Stop, TestContext.Current.CancellationToken)));
        }
        finally { release.TrySetResult(); await task; }
        Assert.Equal(stopFirst ? 0 : 1, calls); Assert.True(control.IsStopped);
        Assert.All(Enum.GetValues<AssignedExecutionStage>(), s => Assert.False(control.TryAdmit(s)));
    }

    [Fact]
    public void CancellationCallbacks_RunOutsideGate_AndCannotUndoStop()
    {
        using var control = new AssignedExecutionControl();
        var observedOutsideGate = false;
        using var callback = control.StopToken.Register(() =>
        {
            Assert.False(Task.Run(() => control.TryAdmit(AssignedExecutionStage.Publication)).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            observedOutsideGate = true;
            throw new IOException("callback failure");
        });
        control.Stop(); control.Stop();
        Assert.True(observedOutsideGate); Assert.True(control.StopToken.IsCancellationRequested); Assert.True(control.IsStopped);
    }
}
