using Application.Common.Abstractions.Storage;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.ProcessRequirement;

public enum ExecutionDocumentPreparationState { Registered, AlreadyRegistered, Rejected, Conflict, Indeterminate }
public enum ExecutionDocumentPreparationFailure
{
    None, InvalidRequest, Unauthorized, InactiveUser, NotFound, InactiveClient, InactiveProject,
    IdentityConflict, ContextConflict, StateConflict, AttemptConflict, LeaseConflict, CancellationRequested,
    InvalidDocuments, MissingIncorporation, ContentMismatch, SourceUnavailable, SnapshotChanged,
    InconsistentEvidence, PersistenceError, AuthorizationConflict
}

public sealed record ExecutionDocumentPreparationResult(ExecutionDocumentPreparationState State,
    ExecutionDocumentPreparationFailure Failure, RequirementExecutionEvidence? Evidence = null,
    PreparedExecutionDocuments? Package = null, RequirementCommercialLine? CommercialLine = null);

public interface IExecutionDocumentPreparation
{
    Task<ExecutionDocumentPreparationResult> PrepareAsync(AssignedJobExecution execution, Guid actorUserId,
        CancellationToken cancellationToken);
}

// Owns only private copies. A package is not execution authority or a send receipt.
public sealed class PreparedExecutionDocuments : IAsyncDisposable
{
    private readonly IReadOnlyList<ICapturedContent> copies;
    private readonly ILogger logger;
    private int disposed;
    private int consumptionReserved;
    public RequirementExecutionEvidence Evidence { get; }

    internal PreparedExecutionDocuments(RequirementExecutionEvidence evidence, IEnumerable<ICapturedContent> copies, ILogger logger)
    {
        Evidence = evidence;
        this.copies = Array.AsReadOnly(copies.ToArray());
        this.logger = logger;
        if (this.copies.Count != evidence.FileCount || evidence.Files.Where((f, i) =>
            f.ObservedSha256 != this.copies[i].Sha256 || f.ObservedLengthBytes != this.copies[i].Length).Any())
            throw new ArgumentException("Copies must match the complete evidence.");
    }

    public Stream OpenRead(Guid fileId)
    {
        if (Volatile.Read(ref consumptionReserved) != 0)
            throw new InvalidOperationException("The package is reserved for consumption.");
        return OpenReader(fileId);
    }

    internal bool TryReserveConsumption() => Interlocked.CompareExchange(ref consumptionReserved, 1, 0) == 0;

    internal Stream OpenConsumptionReader(Guid fileId) => OpenReader(fileId);

    private Stream OpenReader(Guid fileId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var file = Evidence.Files.SingleOrDefault(f => f.FileId == fileId)
            ?? throw new ArgumentException("Unknown document.", nameof(fileId));
        return copies[file.Ordinal].OpenRead();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await DisposeCopiesAsync(copies, logger);
    }

    internal static async ValueTask DisposeCopiesAsync(IEnumerable<ICapturedContent> copies, ILogger logger)
    {
        foreach (var copy in copies)
        {
            try { await copy.DisposeAsync(); }
            catch (Exception error)
            {
                try { logger.LogWarning("Execution copy disposal failed: {ErrorType}", error.GetType().Name); }
                catch (Exception) { /* A failing diagnostic sink must not replace the persistence outcome. */ }
            }
        }
    }
}
