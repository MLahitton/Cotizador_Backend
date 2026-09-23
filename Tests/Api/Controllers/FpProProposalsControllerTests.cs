using Api.Controllers;
using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro;
using Application.Proposals.FpPro.Experimental;
using Contracts.Proposals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class FpProProposalsControllerTests
{
    [Fact]
    public async Task Preview_MapsGlassTreatmentInResponse()
    {
        var parser = Substitute.For<IFpProReportParser>();
        var resolver = Substitute.For<IFpProPreviewConfigurationResolver>();
        var moduleInference = Substitute.For<IFpProModuleInferenceService>();
        var catalogReader = Substitute.For<IQuotationTemplateCatalogReader>();
        var preview = new FpProReportPreviewData(
            new FpProReportData("S&G1085", "CASA", 1, 1, 20m, null, null),
            [new FpProPreviewItemData(
                "01",
                "V-1",
                ["KONCEPT40"],
                [],
                [],
                1500,
                2400,
                1.5m,
                2.4m,
                1,
                3.6m,
                null,
                [
                    new FpProGlassPaneData("06MM", "TEMPLADO", 6m, 1500, 2400, 1),
                    new FpProGlassPaneData("05MM", null, 5m, 1500, 2400, 1)
                ],
                6m,
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
                [],
                [])],
            []);
        parser.ParseAsync(Arg.Any<FpProReportFile>(), Arg.Any<CancellationToken>())
            .Returns(preview);
        resolver.ResolveAsync(preview, Arg.Any<CancellationToken>())
            .Returns(preview);
        moduleInference.Infer(Arg.Any<FpProPreviewItemData>())
            .Returns(new FpProModuleInferenceResult(
                "01",
                1,
                [],
                1,
                1,
                1,
                FpProModuleInferenceConfidence.High,
                [],
                false));
        var controller = new FpProProposalsController(
            new PreviewFpProReportService(parser, resolver, moduleInference),
            new GenerateFpProQuotationService(
                catalogReader,
                Substitute.For<IQuotationWorkbookGenerator>()),
            catalogReader);
        var fileBytes = new byte[] { 1, 2, 3 };
        var file = new FormFile(new MemoryStream(fileBytes), 0, fileBytes.Length, "file", "report.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        var files = new FormFileCollection { file };
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "multipart/form-data; boundary=test";
        httpContext.Request.Form = new FormCollection([], files);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Preview(
            new FpProPreviewForm { File = file },
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FpProPreviewResponse>(ok.Value);
        var glass = Assert.Single(response.Items).Glass;
        Assert.Equal("TEMPLADO", glass[0].Treatment);
        Assert.Null(glass[1].Treatment);
    }

    [Fact]
    public async Task Catalogs_ReturnsTemplateCatalogOptions()
    {
        var catalogReader = Substitute.For<IQuotationTemplateCatalogReader>();
        catalogReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new QuotationTemplateCatalog(
                [
                    new(
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40",
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO ",
                        "N.A"),
                    new(
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40",
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO ",
                        "N.A")
                ],
                [new("COMPOSICION MONOLITICO TEMPLADO 10 MM INC", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC ")],
                [new("ALUCOLOR POLIESTER NEGRO MATE PP13", "ALUCOLOR POLIESTER NEGRO MATE PP13")],
                [new("BGA", "BGA"), new("BTA", "BTA")]));
        var controller = new FpProProposalsController(
            new PreviewFpProReportService(
                Substitute.For<IFpProReportParser>(),
                Substitute.For<IFpProPreviewConfigurationResolver>(),
                Substitute.For<IFpProModuleInferenceService>()),
            new GenerateFpProQuotationService(
                catalogReader,
                Substitute.For<IQuotationWorkbookGenerator>()),
            catalogReader);

        var result = await controller.Catalogs(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<FpProCatalogsResponse>(ok.Value);
        var system = Assert.Single(response.Systems);
        Assert.Equal("CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", system.Label);
        Assert.Equal("CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO ", system.Value);
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 10 MM INC", Assert.Single(response.GlassDescriptions).Label);
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 10 MM INC ", Assert.Single(response.GlassDescriptions).Value);
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", Assert.Single(response.Finishes).Label);
        Assert.Equal(["BGA", "BTA"], response.Locations.Select(value => value.Value).ToArray());
        Assert.DoesNotContain(response.Systems, value => string.IsNullOrWhiteSpace(value.Label) || string.IsNullOrWhiteSpace(value.Value));
        Assert.DoesNotContain(response.GlassDescriptions, value => string.IsNullOrWhiteSpace(value.Label) || string.IsNullOrWhiteSpace(value.Value));
        Assert.DoesNotContain(response.Finishes, value => string.IsNullOrWhiteSpace(value.Label) || string.IsNullOrWhiteSpace(value.Value));
        Assert.DoesNotContain(response.Locations, value => string.IsNullOrWhiteSpace(value.Label) || string.IsNullOrWhiteSpace(value.Value));
    }
}
