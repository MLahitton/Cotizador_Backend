using Infrastructure.Persistence.Repositories;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class DocumentProcessingClaimSqlTests
{
    [Fact]
    public void ClaimSql_UsesPendingDeterministicSkipLockedQuery()
    {
        var sql = DocumentProcessingRepository.ClaimPendingAttemptSql;

        Assert.Contains(
            "WHERE processing_state = 'Pending'",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY created_at_utc, id",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", sql, StringComparison.Ordinal);
        Assert.Contains(
            "FOR UPDATE SKIP LOCKED",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("outcome", sql, StringComparison.OrdinalIgnoreCase);
    }
}
