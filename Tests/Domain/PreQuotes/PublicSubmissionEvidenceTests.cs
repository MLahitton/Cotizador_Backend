using Domain.PublicExperience;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class PublicSubmissionEvidenceTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    [Theory]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("version")]
    [InlineData("utc")]
    public void InvalidEvidenceIsRejected(string kind)
    {
        var error = Record.Exception(() => PublicExperienceSubmissionFile.CreateWithStoredEvidence(
            Guid.NewGuid(), Guid.NewGuid(), 0, "a.pdf", "application/pdf", 3, "a",
            kind == "hash" ? new string('a', 64) : new string('A', 64), At,
            kind == "length" ? 2 : 3, kind == "utc" ? At.ToOffset(TimeSpan.FromHours(1)) : At,
            kind == "version" ? (short)2 : (short)1));
        if (kind is "length" or "version") Assert.IsType<ArgumentOutOfRangeException>(error);
        else Assert.IsType<ArgumentException>(error);
        foreach (var property in new[] { "StoredLengthBytes", "StoredEvidenceAtUtc", "StoredEvidenceVersion" })
            Assert.False(typeof(PublicExperienceSubmissionFile).GetProperty(property)!.SetMethod!.IsPublic);
    }
}
