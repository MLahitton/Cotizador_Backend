using Api.Controllers;
using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro;
using Application.Proposals.FpPro.Experimental;
using Contracts.Proposals;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class FpProProposalsControllerTests
{
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
