using Application.Common.Abstractions.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class RequirementProcessingLock(ApplicationDbContext context) : IRequirementProcessingLock
{
    public async Task<IRequirementProcessingLease?> TryAcquireAsync(Guid requirementId, CancellationToken cancellationToken)
    {
        // A dedicated nonpooled session owns the lock, not a long-running business transaction.
        var settings = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()) { Pooling = false };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@key, 0))";
            command.Parameters.AddWithValue("key", $"requirement-processing:{requirementId:D}");
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            {
                await connection.DisposeAsync();
                return null;
            }
            var tracked = context.ChangeTracker.Entries<Domain.PreQuotes.Requirement>()
                .SingleOrDefault(entry => entry.Entity.Id == requirementId);
            if (tracked is not null) await tracked.ReloadAsync(cancellationToken);
            return new Lease(requirementId, connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(Guid requirementId, NpgsqlConnection connection) : IRequirementProcessingLease
    {
        public Guid RequirementId => requirementId;
        // Closing the physical session releases its advisory locks even on exceptions/cancellation.
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
