using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobPublicationReceiptPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private JobTerminationTestSupport H => new(fixture);

    [Fact]
    public async Task Roundtrip_StoresReceipt_WithoutPublishingGraph()
    {
        var seed = await H.Create(attempt: true);
        await using (var db = fixture.CreateDbContext())
        {
            var job = await db.RequirementProcessingJobs.SingleAsync(Token);
            job.SucceedWithPublicationReceipt(PreparedJobPublication.Version, new string('A', 64), await H.Now());
            await db.SaveChangesAsync(Token);
        }
        await using var read = fixture.CreateDbContext();
        var saved = await read.RequirementProcessingJobs.SingleAsync(Token);
        Assert.Equal(PreparedJobPublication.Version, saved.PublicationContractVersion);
        Assert.Equal(new string('A', 64), saved.PublicationContentHash);
        Assert.Equal(seed.Execution.ProcessingAttemptId, saved.ProcessingAttemptId);
        Assert.False(await read.RequirementExtractionResults.AnyAsync(Token));
        Assert.False(await read.RequirementTechnicalProposals.AnyAsync(Token));
    }

    [Theory]
    [InlineData("v1", null)]
    [InlineData(null, "valid")]
    [InlineData("", "valid")]
    [InlineData(" \t", "valid")]
    [InlineData("v1", "invalid")]
    [InlineData("v1", "short")]
    public async Task DatabaseRejectsInvalidReceiptPair(string? version, string? hashKind)
    {
        await H.Create(attempt: true);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(Token);
        job.Succeed(await H.Now());
        await db.SaveChangesAsync(Token);
        var before = await H.Snapshot();
        var hash = hashKind switch { "valid" => new string('A', 64), "invalid" => new string('g', 64), "short" => "ABC", _ => null };
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE core.requirement_processing_jobs SET publication_contract_version = {version}, publication_content_hash = {hash}", Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_processing_jobs_publication_receipt", error.ConstraintName);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    [InlineData("Cancelled")]
    [InlineData("Failed")]
    public async Task DatabaseRejectsReceiptInNonSuccessfulState(string state)
    {
        await H.Create(running: state != "Queued");
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(Token);
        if (state == "Cancelled") { job.RequestCancellation(await H.Now()); job.ConfirmCancellation(await H.Now()); }
        if (state == "Failed") job.Fail("TEST", await H.Now());
        await db.SaveChangesAsync(Token);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE core.requirement_processing_jobs SET publication_contract_version = 'v1', publication_content_hash = repeat('A',64)", Token));
        Assert.Equal("ck_processing_jobs_publication_receipt", error.ConstraintName);
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    [InlineData("Succeeded")]
    [InlineData("Cancelled")]
    [InlineData("Failed")]
    public async Task Migration_PreservesPriorRows_WithoutBackfill(string state)
    {
        await H.Create(running: state != "Queued", attempt: state == "Succeeded");
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(Token);
        if (state == "Succeeded") job.Succeed(await H.Now());
        if (state == "Failed") job.Fail("TEST", await H.Now());
        if (state == "Cancelled") { job.RequestCancellation(await H.Now()); job.ConfirmCancellation(await H.Now()); }
        await db.SaveChangesAsync(Token);
        var before = await H.Snapshot(false);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.EndsWith("AddJobPublicationReceipt", migrations[^1]);
        await migrator.MigrateAsync(migrations[^2], Token);
        try
        {
            var oldRow = await db.Database.SqlQuery<string>($"SELECT row_to_json(j)::text AS \"Value\" FROM core.requirement_processing_jobs j").SingleAsync(Token);
            await migrator.MigrateAsync(migrations[^1], Token);
            var newRow = await db.Database.SqlQuery<string>($"SELECT (to_jsonb(j) - 'publication_contract_version' - 'publication_content_hash')::text AS \"Value\" FROM core.requirement_processing_jobs j").SingleAsync(Token);
            Assert.Equal(PublicationCanonicalJson.Canonicalize(oldRow), PublicationCanonicalJson.Canonicalize(newRow));
            await using var read = fixture.CreateDbContext();
            var saved = await read.RequirementProcessingJobs.SingleAsync(Token);
            Assert.Null(saved.PublicationContractVersion);
            Assert.Null(saved.PublicationContentHash);
            Assert.Equal(before, await H.Snapshot(false));
        }
        finally { await migrator.MigrateAsync(migrations[^1], Token); }
    }
}
