using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class PublicExperienceSubmissionPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    [Fact]
    public async Task ConcurrentRealRepositories_OneRow_Jsonb_NoCommercialEntities()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var token = TestContext.Current.CancellationToken;
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var storage = new IntakeStorage();
        var first = new PublicExperienceIntakeService(new PublicExperienceSubmissionRepository(firstContext),
            storage, TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance);
        var second = new PublicExperienceIntakeService(new PublicExperienceSubmissionRepository(secondContext),
            storage, TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance);
        var file = PublicExperienceIntakeTests.Upload();
        var payload = PublicExperienceIntakeTests.Payload([file]);
        var results = await Task.WhenAll(first.ExecuteAsync(payload, [file], token),
            second.ExecuteAsync(payload, [file], token));
        Assert.Contains(results, x => x.StatusCode == 201);
        Assert.Contains(results, x => x.StatusCode == 200);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(1, await read.Set<PublicExperienceSubmission>().CountAsync(token));
        Assert.Equal(1, await read.Set<PublicExperienceSubmissionFile>().CountAsync(token));
        Assert.Single(storage.Files);
        Assert.False(await read.Clients.AnyAsync(token));
        Assert.False(await read.Projects.AnyAsync(token));
        Assert.False(await read.PreQuotes.AnyAsync(token));
        Assert.False(await read.Requirements.AnyAsync(token));
        var saved = await read.Set<PublicExperienceSubmission>().SingleAsync(token);
        Assert.Contains("yes", saved.AnswersJson);
        Assert.Equal("AD01", saved.AdvisorCode);
    }
}
