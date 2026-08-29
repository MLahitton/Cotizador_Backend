using System.Collections.Concurrent;
using Application.Common.Abstractions.Operations;

namespace Infrastructure.Operations;

public sealed class InMemoryOperationCancellationRegistry
    : IOperationCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource>
        sources = new(StringComparer.Ordinal);

    public CancellationToken Register(
        string operationKey,
        CancellationToken callerCancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        var source = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken);
        if (!sources.TryAdd(operationKey, source))
        {
            source.Dispose();
            throw new InvalidOperationException(
                "La operacion ya se encuentra registrada.");
        }

        return source.Token;
    }

    public bool TryCancel(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        if (!sources.TryGetValue(operationKey, out var source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    public void Complete(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        if (sources.TryRemove(operationKey, out var source))
        {
            source.Dispose();
        }
    }
}
