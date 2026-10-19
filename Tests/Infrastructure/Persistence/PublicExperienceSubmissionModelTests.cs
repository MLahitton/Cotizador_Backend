using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class PublicExperienceSubmissionModelTests
{
    [Fact]
    public void Model_HasUniquePublicId_Jsonb_OrderedFiles_AndNullableProcessingLinks()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused_model_only;Username=unused;Password=unused").Options);
        var submission = context.Model.FindEntityType(typeof(PublicExperienceSubmission))!;
        Assert.Equal("core", submission.GetSchema());
        Assert.Equal("public_experience_submissions", submission.GetTableName());
        Assert.Equal("jsonb", submission.FindProperty(nameof(PublicExperienceSubmission.AnswersJson))!.GetColumnType());
        Assert.Contains(submission.GetIndexes(), index => index.IsUnique
            && index.GetDatabaseName() == PublicExperienceSubmissionRepository.UniqueSubmissionIndex);
        Assert.Equal(4, submission.GetForeignKeys().Count());
        Assert.All(submission.GetForeignKeys(), fk =>
        {
            Assert.False(fk.IsRequired);
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        });
        var file = context.Model.FindEntityType(typeof(PublicExperienceSubmissionFile))!;
        Assert.Equal(typeof(PublicExperienceSubmission), Assert.Single(file.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Contains(file.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(p => p.Name).SequenceEqual(["SubmissionId", "Sequence"]));
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
