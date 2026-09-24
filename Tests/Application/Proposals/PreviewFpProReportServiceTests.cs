using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro;
using Application.Proposals.FpPro.Experimental;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Proposals;

public sealed class PreviewFpProReportServiceTests
{
    private const string PdfContentType = "application/pdf";

    [Fact]
    public async Task ExecuteAsync_WithPdf_DelegatesToParser()
    {
        var parser = Substitute.For<IFpProReportParser>();
        var resolver = Substitute.For<IFpProPreviewConfigurationResolver>();
        var moduleInference = Substitute.For<IFpProModuleInferenceService>();
        var preview = new FpProReportPreviewData(
            new FpProReportData("S&G648", "CASA PS", 6, 1, 1, 20m, null, null),
            [new FpProPreviewItemData(
                "01",
                "V-1",
                ["KONCEPT40", "ALFAJIA"],
                [],
                [new FpProTechnicalProfile("KONCEPT40", "Marco", 12m, 12m, 1m)],
                4550,
                3200,
                4.55m,
                3.2m,
                1,
                14.56m,
                null,
                [], 
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new FpProPreviewImageData("image/png", "abc"),
                ["system"],
                [])],
            ["system"]);
        parser.ParseAsync(Arg.Any<FpProReportFile>(), Arg.Any<CancellationToken>())
            .Returns(preview);
        resolver.ResolveAsync(preview, Arg.Any<CancellationToken>())
            .Returns(preview);
        moduleInference.Infer(Arg.Any<FpProPreviewItemData>())
            .Returns(ModuleInference(5, FpProModuleInferenceConfidence.High));
        var service = new PreviewFpProReportService(parser, resolver, moduleInference);
        await using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.ExecuteAsync(
            new PreviewFpProReportCommand(new FpProReportFile(
                "report.PDF",
                PdfContentType,
                stream.Length,
                stream)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("S&G648", result.Preview!.Report.OrderId);
        var item = result.Preview.Items.Single();
        Assert.Equal(5, item.InferredModuleCount);
        Assert.Equal("High", item.ModuleConfidence);
        Assert.False(item.RequiresManualModule);
        Assert.DoesNotContain("module", item.PendingFields);
        Assert.Single(item.TechnicalProfiles);
        Assert.Equal("KONCEPT40", item.TechnicalProfiles[0].Code);
    }

    [Fact]
    public async Task ExecuteAsync_WithAmbiguousModuleInference_RequiresManualModule()
    {
        var parser = Substitute.For<IFpProReportParser>();
        var resolver = Substitute.For<IFpProPreviewConfigurationResolver>();
        var moduleInference = Substitute.For<IFpProModuleInferenceService>();
        var preview = CreatePreview(["module"]);
        parser.ParseAsync(Arg.Any<FpProReportFile>(), Arg.Any<CancellationToken>())
            .Returns(preview);
        resolver.ResolveAsync(preview, Arg.Any<CancellationToken>())
            .Returns(preview);
        moduleInference.Infer(Arg.Any<FpProPreviewItemData>())
            .Returns(ModuleInference(4, FpProModuleInferenceConfidence.Ambiguous, requiresManual: true));
        var service = new PreviewFpProReportService(parser, resolver, moduleInference);

        var result = await service.ExecuteAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = result.Preview!.Items.Single();
        Assert.Null(item.InferredModuleCount);
        Assert.Equal("Ambiguous", item.ModuleConfidence);
        Assert.True(item.RequiresManualModule);
        Assert.Contains("module", item.PendingFields);
    }

    [Fact]
    public async Task ExecuteAsync_WhenModuleInferenceFails_ContinuesPreviewWithManualModule()
    {
        var parser = Substitute.For<IFpProReportParser>();
        var resolver = Substitute.For<IFpProPreviewConfigurationResolver>();
        var moduleInference = Substitute.For<IFpProModuleInferenceService>();
        var preview = CreatePreview([]);
        parser.ParseAsync(Arg.Any<FpProReportFile>(), Arg.Any<CancellationToken>())
            .Returns(preview);
        resolver.ResolveAsync(preview, Arg.Any<CancellationToken>())
            .Returns(preview);
        moduleInference.Infer(Arg.Any<FpProPreviewItemData>())
            .Returns<FpProModuleInferenceResult>(_ => throw new InvalidOperationException("boom"));
        var service = new PreviewFpProReportService(parser, resolver, moduleInference);

        var result = await service.ExecuteAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = result.Preview!.Items.Single();
        Assert.Null(item.InferredModuleCount);
        Assert.Equal("Ambiguous", item.ModuleConfidence);
        Assert.True(item.RequiresManualModule);
        Assert.Contains("module", item.PendingFields);
    }

    [Theory]
    [InlineData("report.xlsx", "application/pdf", PreviewFpProReportFailure.UnsupportedFileType)]
    [InlineData("report.pdf", "application/octet-stream", PreviewFpProReportFailure.UnsupportedFileType)]
    public async Task ExecuteAsync_WithUnsupportedFile_ReturnsFailure(
        string fileName,
        string contentType,
        PreviewFpProReportFailure expected)
    {
        var service = new PreviewFpProReportService(
            Substitute.For<IFpProReportParser>(),
            Substitute.For<IFpProPreviewConfigurationResolver>(),
            Substitute.For<IFpProModuleInferenceService>());
        await using var stream = new MemoryStream([1]);

        var result = await service.ExecuteAsync(
            new PreviewFpProReportCommand(new FpProReportFile(
                fileName,
                contentType,
                stream.Length,
                stream)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyFile_ReturnsFailure()
    {
        var service = new PreviewFpProReportService(
            Substitute.For<IFpProReportParser>(),
            Substitute.For<IFpProPreviewConfigurationResolver>(),
            Substitute.For<IFpProModuleInferenceService>());
        await using var stream = new MemoryStream();

        var result = await service.ExecuteAsync(
            new PreviewFpProReportCommand(new FpProReportFile(
                "report.pdf",
                PdfContentType,
                0,
                stream)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreviewFpProReportFailure.EmptyFile, result.Failure);
    }

    private static PreviewFpProReportCommand CreateCommand()
    {
        var stream = new MemoryStream([1, 2, 3]);
        return new PreviewFpProReportCommand(new FpProReportFile(
            "report.pdf",
            PdfContentType,
            stream.Length,
            stream));
    }

    private static FpProReportPreviewData CreatePreview(IReadOnlyList<string> pendingFields)
    {
        var item = new FpProPreviewItemData(
            "01",
            "V-1",
            ["KONCEPT40", "ALFAJIA"],
            [],
            [new FpProTechnicalProfile("KONCEPT40", "Marco", 12m, 12m, 1m)],
            4550,
            3200,
            4.55m,
            3.2m,
            1,
            14.56m,
            null,
            [new FpProGlassPaneData("05MM",null, 5m, 4550, 3200, 1)],
            5m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            pendingFields,
            []);

        return new FpProReportPreviewData(
            new FpProReportData("S&G648", "CASA PS", 6, 1, 1, 20m, null, null),
            [item],
            pendingFields);
    }

    private static FpProModuleInferenceResult ModuleInference(
        int finalModules,
        FpProModuleInferenceConfidence confidence,
        bool requiresManual = false) =>
        new(
            "01",
            1,
            [],
            2,
            finalModules,
            finalModules,
            confidence,
            [],
            requiresManual);
}
