using Application.PreQuotes.ProcessRequirement;
using static Application.PreQuotes.ProcessRequirement.ExecutionDocumentPreparationFailure;

namespace Infrastructure.Persistence.Repositories;

public sealed partial class ExecutionDocumentPreparationStore
{
    public async Task<ExecutionDocumentConsumptionAuthorization> AuthorizePreparationAsync(VerifiedExecutionDocumentResult result,
        AssignedJobExecution execution, Guid actorUserId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (result is null || execution is null || actorUserId == Guid.Empty) return new(InvalidRequest);
        try
        {
            await using var db = new ApplicationDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var snapshot = await ReadLockedAsync(db, execution, actorUserId, ct);
            var actual = snapshot.Existing;
            var expected = result.Evidence;
            if (actual is null || actual.Id != expected.Id || actual.VerifiedAtUtc != expected.VerifiedAtUtc
                || actual.RegisteredAtUtc != expected.RegisteredAtUtc || actual.ManifestHash != expected.ManifestHash
                || actual.CanonicalManifest() != expected.CanonicalManifest()) return new(InconsistentEvidence);
            await ValidateLiveAsync(db, snapshot, execution, ct);
            ct.ThrowIfCancellationRequested();
            return new(None, snapshot.Job.CorrelationId, snapshot.ProjectId);
        }
        catch (Denied error) { ct.ThrowIfCancellationRequested(); return new(error.Failure); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { ct.ThrowIfCancellationRequested(); return new(PersistenceError); }
    }

    public async Task<ExecutionDocumentConsumptionAuthorization> AuthorizeConsumptionAsync(PreparedExecutionDocuments package,
        AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        ct.ThrowIfCancellationRequested();
        if (package is null || execution is null || actorUserId == Guid.Empty) return new(InvalidRequest);
        try
        {
            await using var db = new ApplicationDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var snapshot = await ReadLockedAsync(db, execution, actorUserId, ct);
            var actual = snapshot.Existing;
            var expected = package.Evidence;
            if (actual is null || actual.Id != expected.Id || actual.VerifiedAtUtc != expected.VerifiedAtUtc
                || actual.RegisteredAtUtc != expected.RegisteredAtUtc || actual.ManifestHash != expected.ManifestHash
                || actual.CanonicalManifest() != expected.CanonicalManifest()) return new(InconsistentEvidence);
            await ValidateLiveAsync(db, snapshot, execution, ct);
            return new(None, snapshot.Job.CorrelationId, snapshot.ProjectId);
        }
        catch (Denied error) { ct.ThrowIfCancellationRequested(); return new(error.Failure); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { ct.ThrowIfCancellationRequested(); return new(PersistenceError); }
        // await-using scopes are gone before the consumer can open readers or call the client.
    }
}
