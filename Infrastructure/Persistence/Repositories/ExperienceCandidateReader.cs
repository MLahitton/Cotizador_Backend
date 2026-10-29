using Infrastructure.Operations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class ExperienceCandidateReader(DbContextOptions<ApplicationDbContext> options) : IExperienceCandidateReader
{
    public async Task<IReadOnlyList<ExperienceCandidate>> ReadAsync(ExperienceCandidate? after, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = new ApplicationDbContext(options);
        var hasCursor = after is not null;
        var date = after?.CreatedAtUtc ?? DateTimeOffset.UnixEpoch;
        var id = after?.Id ?? Guid.Empty;
        return await db.Database.SqlQuery<ExperienceCandidate>($"""
            SELECT id AS "Id", created_at_utc AS "CreatedAtUtc"
            FROM core.requirement_processing_jobs
            WHERE automatic_authorization_policy_version = 1
              AND required_publication_version = 'sng-job-publication-v2'
              AND state = 'Queued' AND dispatch_admitted_at_utc IS NULL
              AND dispatch_instance_id IS NULL AND dispatch_claim_operation_id IS NULL
              AND authorization_revoked_at_utc IS NULL AND cancellation_requested_at_utc IS NULL
              AND authorized_at_utc <= clock_timestamp() AND authorization_expires_at_utc > clock_timestamp()
              AND (NOT {hasCursor} OR (created_at_utc, id) > ({date}, {id}))
            ORDER BY created_at_utc, id LIMIT {limit}
            """).ToListAsync(ct);
    }
}
