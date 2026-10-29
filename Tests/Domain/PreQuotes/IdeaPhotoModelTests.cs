using Domain.PublicExperience;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class IdeaPhotoModelTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("submission")]
    [InlineData("name")]
    [InlineData("extension")]
    [InlineData("mime")]
    [InlineData("key")]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("large")]
    [InlineData("utc")]
    public void FactoryRejectsInvalidEvidence(string scenario)
    {
        Assert.Throws<ArgumentException>(() => PublicExperienceIdeaPhoto.Create(
            scenario == "id" ? Guid.Empty : Guid.NewGuid(), scenario == "submission" ? Guid.Empty : Guid.NewGuid(),
            scenario == "name" ? "../a.png" : scenario == "extension" ? "a.txt" : "a.png", scenario == "mime" ? "image/gif" : "image/png",
            scenario == "key" ? "" : "photo/key", scenario == "hash" ? new string('a', 64) : new string('A', 64),
            scenario == "length" ? 0 : scenario == "large" ? 20971521 : 3,
            new DateTimeOffset(2026, 10, 23, 0, 0, 0, scenario == "utc" ? TimeSpan.FromHours(1) : TimeSpan.Zero)));
    }

    [Fact]
    public void NoPublicConstructorsOrMutableProperties()
    {
        Assert.Empty(typeof(PublicExperienceIdeaPhoto).GetConstructors());
        Assert.All(typeof(PublicExperienceIdeaPhoto).GetProperties(), p => Assert.False(p.SetMethod?.IsPublic ?? false));
    }
}
