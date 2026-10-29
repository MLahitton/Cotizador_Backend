using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class RequirementExecutionEvidenceTests
{
    internal static readonly DateTimeOffset At = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero).AddTicks(1234560);
    internal const string Hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    internal static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
    internal static RequirementExecutionDocument File(Guid? evidence = null, Guid? file = null, int ordinal = 0,
        string name = "a.pdf", string mime = "application/pdf", string key = "documents/a",
        DateTimeOffset? created = null, DateTimeOffset? stored = null, long declared = 3,
        long expected = 3, long observed = 3, string? expectedHash = Hash, string? observedHash = Hash, short version = 1) =>
        RequirementExecutionDocument.Create(evidence ?? Id(1), file ?? Id(7), ordinal, name, mime, key,
            created ?? At, declared, expectedHash!, expected, stored ?? At, version, observedHash!, observed);
    internal static RequirementExecutionEvidence Build(IEnumerable<RequirementExecutionDocument>? files = null,
        Guid? id = null, Guid? requirement = null, Guid? job = null, Guid? request = null, Guid? attempt = null,
        Guid? actor = null, Guid? primary = null, long generation = 1, short version = 1, int count = 1,
        DateTimeOffset? verified = null, DateTimeOffset? registered = null) =>
        RequirementExecutionEvidence.Create(id ?? Id(1), requirement ?? Id(2), job ?? Id(3), request ?? Id(4),
            attempt ?? Id(5), generation, actor ?? Id(6), primary ?? Id(7), version,
            verified ?? At, registered ?? At, count, files ?? new[] { File(evidence: id) });

    [Fact]
    public void LiteralVector_Determinism_AndImmutableInput()
    {
        const string expected = """
{"actorUserId":"00000000-0000-0000-0000-000000000006","executionRequestId":"00000000-0000-0000-0000-000000000004","files":[{"contentType":"application/pdf","declaredLengthBytes":3,"expectedLengthBytes":3,"expectedSha256":"BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD","fileCreatedAtUtc":"2026-10-23T12:00:00.123456Z","fileId":"00000000-0000-0000-0000-000000000007","observedLengthBytes":3,"observedSha256":"BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD","ordinal":0,"originalFileName":"a.pdf","storageKey":"documents/a","storedEvidenceAtUtc":"2026-10-23T12:00:00.123456Z","storedEvidenceVersion":1}],"jobId":"00000000-0000-0000-0000-000000000003","leaseGeneration":1,"manifestVersion":1,"primaryDocumentId":"00000000-0000-0000-0000-000000000007","processingAttemptId":"00000000-0000-0000-0000-000000000005","requirementId":"00000000-0000-0000-0000-000000000002"}
""";
        var input = new List<RequirementExecutionDocument> { File() };
        var evidence = Build(input);
        Assert.Equal(expected, evidence.CanonicalManifest());
        Assert.Equal("40EA9D6F43D9F189485C06557375AD5C76C4FC84317E8D881C5666F196889BC2", evidence.ManifestHash);
        input.Clear();
        Assert.Single(evidence.Files);
        Assert.Throws<NotSupportedException>(() => ((IList<RequirementExecutionDocument>)evidence.Files).Clear());
        Assert.Equal(evidence.CanonicalManifest(), Build().CanonicalManifest());
        foreach (var type in new[] { typeof(RequirementExecutionEvidence), typeof(RequirementExecutionDocument) })
            Assert.All(type.GetProperties(), p => Assert.False(p.SetMethod?.IsPublic == true));
    }

    [Fact]
    public void RecordIdentityAndOperationalClocks_AreExcluded_WithoutClockOrdering()
    {
        Assert.Equal(Build().ManifestHash, Build(id: Id(8), verified: At.AddDays(1), registered: At.AddDays(-1)).ManifestHash);
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("gap")] [InlineData("order")] [InlineData("empty")]
    [InlineData("primary")] [InlineData("count")] [InlineData("evidence")] [InlineData("null")]
    public void RejectsIncompleteSet(string kind)
    {
        Assert.Throws<ArgumentException>(() => kind switch
        {
            "duplicate" => Build(new[] { File(), File(ordinal: 1) }, count: 2),
            "gap" => Build(new[] { File(ordinal: 1) }),
            "order" => Build(new[] { File(file: Id(8), ordinal: 1), File() }, count: 2),
            "empty" => Build(Array.Empty<RequirementExecutionDocument>(), count: 0),
            "primary" => Build(primary: Id(8)),
            "count" => Build(count: 2),
            "evidence" => Build(new[] { File(evidence: Id(8)) }),
            _ => Build(new RequirementExecutionDocument[] { null! })
        });
    }

    [Theory]
    [InlineData("null-hash")] [InlineData("lower")] [InlineData("invalid")] [InlineData("mismatch")]
    [InlineData("length")] [InlineData("observed-length")] [InlineData("zero")] [InlineData("version")]
    [InlineData("offset")] [InlineData("precision")] [InlineData("file-id")] [InlineData("evidence-id")]
    [InlineData("ordinal")] [InlineData("name")] [InlineData("mime")] [InlineData("key")]
    public void RejectsInvalidIncorporation(string kind)
    {
        Assert.Throws<ArgumentException>(() => kind switch
        {
            "null-hash" => File(expectedHash: null),
            "lower" => File(expectedHash: Hash.ToLowerInvariant()),
            "invalid" => File(expectedHash: new string('Z',64)),
            "mismatch" => File(observedHash: new string('A',64)),
            "length" => File(expected: 4),
            "observed-length" => File(observed: 4),
            "zero" => File(declared: 0, expected: 0, observed: 0),
            "version" => File(version: 2),
            "offset" => File(created: At.ToOffset(TimeSpan.FromHours(1))),
            "precision" => File(stored: At.AddTicks(1)),
            "file-id" => File(file: Guid.Empty),
            "evidence-id" => File(evidence: Guid.Empty),
            "ordinal" => File(ordinal: -1),
            "name" => File(name: ""),
            "mime" => File(mime: new string('x',101)),
            _ => File(key: "")
        });
    }

    [Fact]
    public void RejectsInvalidHeader()
    {
        foreach (var action in new Action[] {
            () => Build(id: Guid.Empty), () => Build(requirement: Guid.Empty), () => Build(job: Guid.Empty),
            () => Build(request: Guid.Empty), () => Build(attempt: Guid.Empty), () => Build(actor: Guid.Empty),
            () => Build(primary: Guid.Empty), () => Build(generation: 0), () => Build(generation: 2),
            () => Build(version: 2), () => Build(verified: At.AddTicks(1)),
            () => Build(registered: At.ToOffset(TimeSpan.FromHours(1))) })
            Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void CoveredIdentityAndDocumentChanges_ChangeHash_WithoutNormalization()
    {
        var baseline = Build().ManifestHash;
        foreach (var changed in new[] {
            Build(requirement: Id(20)), Build(job: Id(20)), Build(request: Id(20)),
            Build(attempt: Id(20)), Build(actor: Id(20)),
            Build(new[] { File(file: Id(20)) }, primary: Id(20)),
            Build(new[] { File(name: " a.pdf ") }), Build(new[] { File(mime: "APPLICATION/PDF") }),
            Build(new[] { File(key: "documents/b") }), Build(new[] { File(created: At.AddTicks(10)) }),
            Build(new[] { File(stored: At.AddTicks(10)) }), Build(new[] { File(declared: 4, expected: 4, observed: 4) }),
            Build(new[] { File(expectedHash: new string('A',64), observedHash: new string('A',64)) }) })
            Assert.NotEqual(baseline, changed.ManifestHash);
        var first = Build(new[] { File(), File(file: Id(8), ordinal: 1) }, count: 2);
        var reversed = Build(new[] { File(file: Id(8)), File(ordinal: 1) }, count: 2, primary: Id(8));
        Assert.NotEqual(first.ManifestHash, reversed.ManifestHash);
    }
}

