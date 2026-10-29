using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Common.Abstractions.Storage;
using Application.PublicExperience;
using Domain.PublicExperience;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorBackend.Tests.Application.PublicExperience;

public sealed class PublicExperienceIntakeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EmptyFiles_Received_AnswersAndAdvisorPreserved()
    {
        var repository = new IntakeRepository();
        var service = Service(repository, new IntakeStorage());
        var json = Payload();
        var result = await service.ExecuteAsync(json, [], Token);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("RECEIVED", result.Status);
        var saved = Assert.Single(repository.Rows.Values);
        Assert.Equal("AD01", saved.AdvisorCode);
        Assert.Equal("Advisor Example", saved.AdvisorDisplayName);
        using var answers = JsonDocument.Parse(saved.AnswersJson);
        using var input = JsonDocument.Parse(json);
        Assert.True(JsonElement.DeepEquals(input.RootElement.GetProperty("answers"), answers.RootElement));
    }

    [Fact]
    public async Task Pdf_Retry_WritesOnce()
    {
        var repository = new IntakeRepository();
        var storage = new IntakeStorage();
        var service = Service(repository, storage);
        var file = Upload();
        var first = await service.ExecuteAsync(Payload([file]), [file], Token);
        var retry = await service.ExecuteAsync(Payload([file]), [file], Token);
        Assert.Equal(201, first.StatusCode);
        Assert.Equal(200, retry.StatusCode);
        Assert.True(retry.IsReplay);
        Assert.Single(repository.Rows);
        Assert.Single(storage.Files);
        Assert.Equal(1, storage.Writes);
        var metadata = Assert.Single(Assert.Single(repository.Rows.Values).Files);
        Assert.Equal(64, metadata.ContentHash.Length);
        Assert.StartsWith("public-experience/", metadata.StorageKey);
    }

    [Theory]
    [InlineData("schemaVersion", "other")]
    [InlineData("source", "OTHER")]
    [InlineData("submissionId", "")]
    [InlineData("ClientId", "internal")]
    [InlineData("storageKey", "path")]
    public async Task InvalidTopLevel_Returns400(string property, string value)
    {
        var json = JsonNode.Parse(Payload())!;
        json[property] = value;
        Assert.Equal(400, (await Service().ExecuteAsync(json.ToJsonString(), [], Token)).StatusCode);
    }

    [Theory]
    [InlineData("answers", "null")]
    [InlineData("answers", "[]")]
    [InlineData("answers", "{\"uv\":true}")]
    [InlineData("files", "null")]
    [InlineData("client", "null")]
    public async Task InvalidRequiredShape_Returns400(string property, string value)
    {
        var json = JsonNode.Parse(Payload())!;
        json[property] = JsonNode.Parse(value);
        Assert.Equal(400, (await Service().ExecuteAsync(json.ToJsonString(), [], Token)).StatusCode);
    }

    [Fact]
    public async Task TooManyFiles_Returns400()
    {
        var files = Enumerable.Range(0, 6).Select(_ => Upload()).ToArray();
        Assert.Equal(400, (await Service().ExecuteAsync(Payload(files), files, Token)).StatusCode);
    }

    [Fact]
    public async Task IndividualLimit_Returns400()
    {
        var file = Upload() with { SizeBytes = 20L * 1024 * 1024 + 1 };
        Assert.Equal(400, (await Service().ExecuteAsync(Payload([file]), [file], Token)).StatusCode);
    }

    [Fact]
    public async Task AggregateOver100MiB_Returns400()
    {
        // Any total over 100 MiB necessarily also exceeds file count or individual size.
        var files = Enumerable.Range(0, 5).Select(_ => Upload() with
            { SizeBytes = 20L * 1024 * 1024 + 1 }).ToArray();
        Assert.Equal(400, (await Service().ExecuteAsync(Payload(files), files, Token)).StatusCode);
    }

    [Theory]
    [InlineData("file.exe", "application/pdf")]
    [InlineData("file.pdf", "image/png")]
    [InlineData("../file.pdf", "application/pdf")]
    [InlineData("file.pdf", "application/octet-stream")]
    public async Task InvalidFile_Returns400(string name, string mime)
    {
        var file = Upload() with { FileName = name, ContentType = mime };
        Assert.Equal(400, (await Service().ExecuteAsync(Payload([file]), [file], Token)).StatusCode);
    }

    [Theory]
    [InlineData("fileName", "other.pdf")]
    [InlineData("contentType", "image/png")]
    [InlineData("sizeBytes", "999")]
    public async Task MetadataMismatch_Returns400(string property, string value)
    {
        var file = Upload();
        var json = JsonNode.Parse(Payload([file]))!;
        json["files"]![0]![property] = property == "sizeBytes" ? JsonValue.Create(999) : JsonValue.Create(value);
        Assert.Equal(400, (await Service().ExecuteAsync(json.ToJsonString(), [file], Token)).StatusCode);
    }

    [Fact]
    public async Task MetadataCountAndOrder_MustMatch()
    {
        var a = Upload();
        var b = Upload() with { FileName = "second.pdf" };
        Assert.Equal(400, (await Service().ExecuteAsync(Payload([a]), [], Token)).StatusCode);
        Assert.Equal(400, (await Service().ExecuteAsync(Payload([a, b]), [b, a], Token)).StatusCode);
    }

    [Fact]
    public async Task ActualLengthAndEmptyFile_AreValidated()
    {
        var file = Upload() with { SizeBytes = 1 };
        Assert.Equal(400, (await Service().ExecuteAsync(Payload([file]), [file], Token)).StatusCode);
        file = Upload() with { SizeBytes = 0, OpenRead = () => new MemoryStream() };
        Assert.Equal(400, (await Service().ExecuteAsync(Payload([file]), [file], Token)).StatusCode);
    }

    [Fact]
    public async Task DifferentAnswers_Conflict()
    {
        var service = Service();
        await service.ExecuteAsync(Payload(), [], Token);
        var json = JsonNode.Parse(Payload())!;
        json["answers"]!["uv"] = "different";
        Assert.Equal(409, (await service.ExecuteAsync(json.ToJsonString(), [], Token)).StatusCode);
    }

    [Fact]
    public async Task SameNameSizeDifferentBytes_Conflict()
    {
        var service = Service();
        var first = Upload();
        var second = Upload([9, 8, 7, 6]);
        await service.ExecuteAsync(Payload([first]), [first], Token);
        Assert.Equal(409, (await service.ExecuteAsync(Payload([second]), [second], Token)).StatusCode);
    }

    [Fact]
    public async Task PropertyOrderWhitespaceAndOptionalNull_DoNotBreakReplay()
    {
        var service = Service();
        await service.ExecuteAsync(Payload(), [], Token);
        var node = JsonNode.Parse(Payload())!.AsObject();
        node["project"]!["notes"] = null;
        node["answers"] = new JsonObject { ["list"] = new JsonArray("a", "b"), ["number"] = 2, ["uv"] = "yes" };
        var reversed = new JsonObject(node.Reverse().Select(x =>
            new KeyValuePair<string, JsonNode?>(x.Key, x.Value?.DeepClone())));
        Assert.Equal(200, (await service.ExecuteAsync(reversed.ToJsonString(), [], Token)).StatusCode);
    }

    [Fact]
    public async Task DuplicatePropertiesAndLongStrings_AreRejected()
    {
        var json = Payload().Replace("\"SNG_WEB\"", "\"SNG_WEB\",\"source\":\"SNG_WEB\"");
        Assert.Equal(400, (await Service().ExecuteAsync(json, [], Token)).StatusCode);
        var node = JsonNode.Parse(Payload())!;
        node["client"]!["fullName"] = new string('x', 201);
        Assert.Equal(400, (await Service().ExecuteAsync(node.ToJsonString(), [], Token)).StatusCode);
    }

    [Fact]
    public async Task DifferentHighPrecisionNumbers_DoNotCollapseToSameFingerprint()
    {
        var service = Service();
        var first = Payload().Replace("\"number\":2", "\"number\":0.1234567890123456789012345678901");
        var second = Payload().Replace("\"number\":2", "\"number\":0.1234567890123456789012345678902");
        Assert.Equal(201, (await service.ExecuteAsync(first, [], Token)).StatusCode);
        Assert.Equal(409, (await service.ExecuteAsync(second, [], Token)).StatusCode);
    }

    [Fact]
    public async Task StorageFailure_RetainsSuccessfulAndPartialWrites()
    {
        var repository = new IntakeRepository();
        var storage = new IntakeStorage { FailOnWrite = 2 };
        var files = new[] { Upload(), Upload() };
        await Assert.ThrowsAsync<IOException>(() => Service(repository, storage).ExecuteAsync(Payload(files), files, Token));
        Assert.Equal(2, storage.Files.Count);
        Assert.Equal(0, storage.Deletes);
        Assert.Empty(repository.Rows);
    }

    [Fact]
    public async Task PersistenceFailure_RetainsFiles()
    {
        var storage = new IntakeStorage();
        var repository = new IntakeRepository { FailInsert = true };
        var file = Upload();
        await Assert.ThrowsAsync<IOException>(() => Service(repository, storage).ExecuteAsync(Payload([file]), [file], Token));
        Assert.Single(storage.Files);
        Assert.Equal(0, storage.Deletes);
        Assert.Empty(repository.Rows);
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 409)]
    public async Task ConcurrentInsert_OneSubmission_LoserRetainsBytes(bool different, int loserStatus)
    {
        var repository = new IntakeRepository { SynchronizeInserts = true };
        var storage = new IntakeStorage();
        var file = Upload();
        var other = different ? Upload([9, 8, 7, 6]) : file;
        var results = await Task.WhenAll(
            Service(repository, storage).ExecuteAsync(Payload([file]), [file], Token),
            Service(repository, storage).ExecuteAsync(Payload([other]), [other], Token));
        Assert.Contains(results, x => x.StatusCode == 201);
        Assert.Contains(results, x => x.StatusCode == loserStatus);
        Assert.Single(repository.Rows);
        Assert.Equal(2, storage.Files.Count);
        Assert.Equal(0, storage.Deletes);
    }

    public static PublicExperienceIntakeService Service(IntakeRepository? repository = null, IntakeStorage? storage = null) =>
        new(repository ?? new(), storage ?? new(), TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance, Capture());

    public static IInitialContentCapture Capture() => new global::Infrastructure.Storage.InitialContentCapture(
        NullLogger<global::Infrastructure.Storage.InitialContentCapture>.Instance);

    public static PublicExperienceUpload Upload(byte[]? bytes = null)
    {
        bytes ??= [1, 2, 3, 4];
        return new("plan.pdf", "application/pdf", bytes.Length, () => new MemoryStream(bytes, writable: false));
    }

    public static string Payload(IReadOnlyList<PublicExperienceUpload>? files = null) =>
        JsonSerializer.Serialize(new
        {
            submissionId = "public-test-id", schemaVersion = "sng-public-experience-v1", source = "SNG_WEB",
            client = new { fullName = "Example", whatsapp = "+5700", advisorCode = "AD01", advisorDisplayName = "Advisor Example" },
            project = new { name = "Project", city = "City" },
            answers = new { uv = "yes", number = 2, list = new[] { "a", "b" } },
            files = (files ?? []).Select(f => new { fileName = f.FileName, contentType = f.ContentType, sizeBytes = f.SizeBytes })
        });
}

public sealed class IntakeRepository : IPublicExperienceSubmissionRepository
{
    public ConcurrentDictionary<string, PublicExperienceSubmission> Rows { get; } = new();
    public bool FailInsert { get; init; }
    public bool SynchronizeInserts { get; init; }
    private readonly TaskCompletionSource _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inserts;

    public Task<PublicExperienceSubmission?> FindAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public async Task<bool> TryInsertAsync(PublicExperienceSubmission submission, CancellationToken cancellationToken)
    {
        if (FailInsert) throw new IOException("Simulated persistence failure");
        if (SynchronizeInserts)
        {
            if (Interlocked.Increment(ref _inserts) == 2) _barrier.SetResult();
            await _barrier.Task.WaitAsync(cancellationToken);
        }
        return Rows.TryAdd(submission.SubmissionId, submission);
    }
}

public sealed class IntakeStorage : IFileStorage
{
    public async Task<StoredContentReceipt> SaveVerifiedAsync(string key, Stream content, long expectedLength,
        long maximumLength, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        await content.CopyToAsync(bytes, cancellationToken);
        bytes.Position = 0;
        var receipt = await global::CotizadorBackend.Tests.Infrastructure.Persistence.StoredReceiptTestWriter.Write(
            bytes, expectedLength, maximumLength, cancellationToken);
        Files[key] = bytes.ToArray();
        if (Interlocked.Increment(ref Writes) == FailOnWrite) throw new IOException("Simulated partial write");
        return receipt;
    }

    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public int Writes;
    public int Deletes;
    public int FailOnWrite { get; init; }
    public bool FailRequirementReads { get; set; }
    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref Writes);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[key] = buffer.ToArray();
        if (count == FailOnWrite) throw new IOException("Simulated partial write");
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        FailRequirementReads && key.StartsWith("requirements/", StringComparison.Ordinal)
            ? Task.FromException<Stream>(new FileStorageReadException(new IOException("Simulated requirement read failure")))
            : Task.FromResult<Stream>(new MemoryStream(Files[key]));
    public Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Deletes);
        Files.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
