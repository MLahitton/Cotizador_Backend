using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class RequirementFileStoredEvidenceTests
{
    private const string Hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Factories_PreserveDeclaredLength_AndAllowUnknownEvidence()
    {
        var old = RequirementFile.Create(Guid.NewGuid(), "a.pdf", "application/pdf", 3, "a", At);
        Assert.Null(old.StoredSha256);
        Assert.Null(old.StoredLengthBytes);
        Assert.Null(old.StoredEvidenceAtUtc);
        Assert.Null(old.StoredEvidenceVersion);
        var file = Create(Hash, 3, At, 1);
        Assert.Equal(Hash, file.StoredSha256);
        Assert.Equal(3, file.StoredLengthBytes);
        Assert.Equal(file.SizeBytes, file.StoredLengthBytes);
        Assert.Equal(At, file.StoredEvidenceAtUtc);
        Assert.Equal((short)1, file.StoredEvidenceVersion);
        foreach (var name in new[] { "StoredSha256", "StoredLengthBytes", "StoredEvidenceAtUtc", "StoredEvidenceVersion" })
            Assert.False(typeof(RequirementFile).GetProperty(name)!.SetMethod!.IsPublic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("ZA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    public void InvalidDigest_IsRejected(string? hash) =>
        Assert.Throws<ArgumentException>(() => Create(hash!, 3, At, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(4)]
    public void InvalidLength_IsRejected(long length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(Hash, length, At, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnknownVersion_IsRejected(short version) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(Hash, 3, At, version));

    [Fact]
    public void NonUtc_IsRejected() =>
        Assert.Throws<ArgumentException>(() => Create(Hash, 3, At.ToOffset(TimeSpan.FromHours(1)), 1));

    private static RequirementFile Create(string hash, long length, DateTimeOffset at, short version) =>
        RequirementFile.CreateWithStoredEvidence(Guid.NewGuid(), "a.pdf", "application/pdf", 3, "a", At,
            hash, length, at, version);
}
