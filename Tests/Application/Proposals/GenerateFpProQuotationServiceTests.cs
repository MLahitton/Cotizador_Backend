using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Proposals;

public sealed class GenerateFpProQuotationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithOneCompleteItem_GeneratesWorkbook()
    {
        var generator = Substitute.For<IQuotationWorkbookGenerator>();
        var workbook = new GeneratedQuotationWorkbook(
            "Cotizacion_S&G648.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [1, 2, 3]);
        generator.GenerateAsync(Arg.Any<QuotationWorkbookRequest>(), Arg.Any<CancellationToken>())
            .Returns(workbook);
        var service = new GenerateFpProQuotationService(CreateCatalogReader(), generator);

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(CreateRequest()),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(workbook, result.Workbook);
    }

    [Fact]
    public async Task ExecuteAsync_UsesReceivedModuleWithoutRecalculating()
    {
        var generator = Substitute.For<IQuotationWorkbookGenerator>();
        var workbook = new GeneratedQuotationWorkbook(
            "Cotizacion_S&G648.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [1, 2, 3]);
        generator.GenerateAsync(Arg.Any<QuotationWorkbookRequest>(), Arg.Any<CancellationToken>())
            .Returns(workbook);
        var service = new GenerateFpProQuotationService(CreateCatalogReader(), generator);
        var request = CreateRequest(CreateItem() with { Module = 6m });

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await generator.Received(1).GenerateAsync(
            Arg.Is<QuotationWorkbookRequest>(value => value.Items.Single().Module == 6m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithoutGlassPrice_ReturnsInvalidRequest()
    {
        var service = new GenerateFpProQuotationService(
            CreateCatalogReader(),
            Substitute.For<IQuotationWorkbookGenerator>());
        var request = CreateRequest(CreateItem() with { GlassPrice = 0 });

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateFpProQuotationFailure.InvalidRequest, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutModule_ReturnsInvalidRequest()
    {
        var service = new GenerateFpProQuotationService(
            CreateCatalogReader(),
            Substitute.For<IQuotationWorkbookGenerator>());
        var request = CreateRequest(CreateItem() with { Module = 0 });

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateFpProQuotationFailure.InvalidRequest, result.Failure);
    }

    [Theory]
    [InlineData(-1, 60, 0)]
    [InlineData(20, -1, 0)]
    [InlineData(20, 60, -1)]
    [InlineData(1001, 60, 0)]
    [InlineData(20, 1001, 0)]
    [InlineData(20, 60, 1001)]
    public async Task ExecuteAsync_WithInvalidGlobalPercent_ReturnsInvalidRequest(
        decimal aluminumWastePercent,
        decimal benefitPercent,
        decimal commissionPercent)
    {
        var service = new GenerateFpProQuotationService(
            CreateCatalogReader(),
            Substitute.For<IQuotationWorkbookGenerator>());
        var request = CreateRequest() with
        {
            Report = new FpProQuotationReportInput(
                "S&G648",
                "CASA PS",
                "BGA",
                aluminumWastePercent,
                benefitPercent,
                commissionPercent)
        };

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateFpProQuotationFailure.InvalidRequest, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithMoreThanOneItem_GeneratesWorkbook()
    {
        var generator = Substitute.For<IQuotationWorkbookGenerator>();
        var workbook = new GeneratedQuotationWorkbook(
            "Cotizacion_S&G648.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [1, 2, 3]);
        generator.GenerateAsync(Arg.Any<QuotationWorkbookRequest>(), Arg.Any<CancellationToken>())
            .Returns(workbook);
        var service = new GenerateFpProQuotationService(
            CreateCatalogReader(),
            generator);
        var item = CreateItem();
        var request = new QuotationWorkbookRequest(
            new FpProQuotationReportInput("S&G648", "CASA PS", "BGA", 20m, 60m, 0m),
            "Casa PS",
            "Cliente",
            "Casa PS",
            "SG ESENCIAL",
            "Luisa",
            "S&G648",
            [item, item with { ItemNumber = "02" }]);

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(workbook, result.Workbook);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTemplateCapacityIsExceeded_ReturnsUnsupportedItemCount()
    {
        var generator = Substitute.For<IQuotationWorkbookGenerator>();
        generator.GenerateAsync(Arg.Any<QuotationWorkbookRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GeneratedQuotationWorkbook>>(_ => throw new QuotationTemplateCapacityExceededException(1));
        var service = new GenerateFpProQuotationService(CreateCatalogReader(), generator);
        var item = CreateItem();
        var request = new QuotationWorkbookRequest(
            new FpProQuotationReportInput("S&G648", "CASA PS", "BGA", 20m, 60m, 0m),
            "Casa PS",
            "Cliente",
            "Casa PS",
            "SG ESENCIAL",
            "Luisa",
            "S&G648",
            [item, item with { ItemNumber = "02" }]);

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateFpProQuotationFailure.UnsupportedItemCount, result.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public async Task ExecuteAsync_WithInvalidProposalName_ReturnsInvalidRequest(string proposalName)
    {
        var service = new GenerateFpProQuotationService(
            CreateCatalogReader(),
            Substitute.For<IQuotationWorkbookGenerator>());
        var request = CreateRequest() with { ProposalName = proposalName };

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateFpProQuotationFailure.InvalidRequest, result.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithInvalidHeaderField_ReturnsInvalidRequest(string invalidValue)
    {
        var service = new GenerateFpProQuotationService(
            CreateCatalogReader(),
            Substitute.For<IQuotationWorkbookGenerator>());
        var request = CreateRequest() with { ClientName = invalidValue };

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GenerateFpProQuotationFailure.InvalidRequest, result.Failure);
    }

    [Theory]
    [InlineData("BGA", true)]
    [InlineData("BTA", true)]
    [InlineData("ANTQ", true)]
    [InlineData("VALLE", true)]
    [InlineData("COSTA", true)]
    [InlineData("622000", false)]
    public async Task ExecuteAsync_ValidatesLocationAgainstTemplateCatalog(
        string location,
        bool expectedSuccess)
    {
        var generator = Substitute.For<IQuotationWorkbookGenerator>();
        var workbook = new GeneratedQuotationWorkbook(
            "Cotizacion_S&G648.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [1, 2, 3]);
        generator.GenerateAsync(Arg.Any<QuotationWorkbookRequest>(), Arg.Any<CancellationToken>())
            .Returns(workbook);
        var service = new GenerateFpProQuotationService(CreateCatalogReader(), generator);
        var request = CreateRequest() with
        {
            Report = CreateRequest().Report with { Location = location }
        };

        var result = await service.ExecuteAsync(
            new GenerateFpProQuotationCommand(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (!expectedSuccess)
        {
            Assert.Equal(GenerateFpProQuotationFailure.UnknownCatalogValue, result.Failure);
        }
    }

    private static IQuotationTemplateCatalogReader CreateCatalogReader()
    {
        var reader = Substitute.For<IQuotationTemplateCatalogReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new QuotationTemplateCatalog(
                [
                    new(
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40",
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
                        "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
                        "N.A")
                ],
                [new("COMPOSICION MONOLITICO TEMPLADO 10 MM INC", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC")],
                [new("ALUCOLOR POLIESTER NEGRO MATE PP13", "ALUCOLOR POLIESTER NEGRO MATE PP13")],
                [
                    new("BGA", "BGA"),
                    new("BTA", "BTA"),
                    new("ANTQ", "ANTQ"),
                    new("VALLE", "VALLE"),
                    new("COSTA", "COSTA")
                ]));

        return reader;
    }

    private static QuotationWorkbookRequest CreateRequest(FpProQuotationItemInput? item = null) =>
        new(
            new FpProQuotationReportInput("S&G648", "CASA PS", "BGA", 20m, 60m, 0m),
            "Casa PS",
            "Cliente",
            "Casa PS",
            "SG ESENCIAL",
            "Luisa",
            "S&G648",
            [item ?? CreateItem()]);

    private static FpProQuotationItemInput CreateItem() =>
        new(
            "01",
            "V-1",
            4.55m,
            3.20m,
            1,
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
            "COMPOSICION MONOLITICO TEMPLADO 10 MM INC",
            "ALUCOLOR POLIESTER NEGRO MATE PP13",
            100000m,
            101352.1m,
            629905.1m,
            10m,
            18.967m,
            3m,
            "INCLUYE MARCO SG0058",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADElEQVR42mP8z8BQDwAFgwJ/lH6yFgAAAABJRU5ErkJggg==");
}
