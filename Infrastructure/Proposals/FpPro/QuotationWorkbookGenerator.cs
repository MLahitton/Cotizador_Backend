using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Application.Common.Abstractions.Proposals;
using ClosedXML.Excel;

namespace Infrastructure.Proposals.FpPro;

public sealed class QuotationWorkbookGenerator : IQuotationWorkbookGenerator
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string TemplateRelativePath = "Infrastructure/Resources/Templates/FormatoCotizacion.xlsx";
    private const int FirstItemRow = 15;
    private const int ItemBlockHeight = 7;
    private const int ImageRows = 4;
    private const int ImageStartRowOffset = 2;
    private const string SheetName = "COTIZACI\u00d3N";
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace MainDrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace PictureNamespace = "http://schemas.openxmlformats.org/drawingml/2006/picture";

        public Task<GeneratedQuotationWorkbook> GenerateAsync(
        QuotationWorkbookRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = File.ReadAllBytes(ResolveTemplatePath());
        using var output = new MemoryStream();
        output.Write(bytes);
        output.Position = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetEntryName = ResolveWorksheetEntryName(archive, SheetName);
            var worksheet = ReadXml(archive, worksheetEntryName);
            var capacity = DetectTemplateCapacity(worksheet);
            if (request.Items.Count == 0 || request.Items.Count > capacity)
            {
                throw new QuotationTemplateCapacityExceededException(capacity);
            }

            WriteGlobalHeader(worksheet, request);
            WriteGlobalPercentages(worksheet, request.Report);
            WriteGlobalCounts(worksheet, request.Report);

            for (var index = 0; index < request.Items.Count; index++)
            {
                WriteItem(worksheet, request.Items[index], ItemBaseRow(index));
            }

            SetTransportGlobalCorrection(worksheet, archive, request.Report.Location, request.Items);
            SetViaticsAndIntermunicipalValues(worksheet, archive, request.Report.Location, request.Items);
            NormalizeFormulaCellsForRecalculation(worksheet, request.Items.Count);
            ReplaceXml(archive, worksheetEntryName, worksheet);
            AddImages(archive, worksheetEntryName, request.Items);
            ForceFullCalculation(archive);
        }

        WriteCalculatedFormulaCachedValues(output);

        return Task.FromResult(new GeneratedQuotationWorkbook(
            BuildFileName(request.ProposalName),
            ContentType,
            output.ToArray()));
    }

    private static void WriteGlobalPercentages(XDocument worksheet, FpProQuotationReportInput report)
    {
        SetNumber(worksheet, "T13", ToExcelPercent(report.AluminumWastePercent));
        SetNumber(worksheet, "AG13", ToExcelPercent(report.BenefitPercent));
        SetNumber(worksheet, "AI13", ToExcelPercent(report.CommissionPercent));
    }

    private static void WriteGlobalCounts(XDocument worksheet, FpProQuotationReportInput report)
{
    if (report.ProfileBarCount is not null)
    {
        SetNumber(worksheet, "T369", report.ProfileBarCount.Value);
    }

    if (report.DoorCount is not null)
    {
        SetNumber(worksheet, "V369", report.DoorCount.Value);
    }
}

        

    private static void WriteGlobalHeader(XDocument worksheet, QuotationWorkbookRequest request)
    {
        SetString(worksheet, "C10", request.ClientName);
        SetString(worksheet, "C11", request.ProjectName);
        SetString(worksheet, "C12", request.Report.Location);
        SetString(worksheet, "I10", request.ProductionLine);
        SetString(worksheet, "I12", request.PreparedBy);
        SetString(worksheet, "O10", request.BudgetId);
    }

    private static void WriteItem(XDocument worksheet, FpProQuotationItemInput item, int row)
    {
        SetString(worksheet, Cell("A", row), item.ItemNumber);
        SetString(worksheet, Cell("B", row), item.Typology);
        SetString(worksheet, Cell("D", row), item.System);
        SetString(worksheet, Cell("D", row + 1), item.GlassDescription);
        SetString(worksheet, Cell("D", row + 2), item.Finish);
        SetString(worksheet, Cell("D", row + 5), item.Notes ?? "N.A");
        SetNumber(worksheet, Cell("J", row), item.WidthM);
        SetNumber(worksheet, Cell("K", row), item.HeightM);
        SetNumber(worksheet, Cell("M", row), item.Quantity);
        SetNumber(worksheet, Cell("Q", row + 1), item.AccessoriesBase);
        SetNumber(worksheet, Cell("R", row + 1), item.AluminumBase);
        SetNumber(worksheet, Cell("S", row + 1), item.GlassPrice);
        SetNumber(worksheet, Cell("BE", row), item.Module);
        SetNumber(worksheet, Cell("BJ", row), item.SelectedThicknessMm);
        SetNumber(worksheet, Cell("BK", row), item.StructureWeightKg);
    }

    private static void AddImages(
        ZipArchive archive,
        string worksheetEntryName,
        IReadOnlyList<FpProQuotationItemInput> items)
    {
        EnsurePngContentType(archive);
        var worksheetRelationshipsName = WorksheetRelationshipsEntryName(worksheetEntryName);
        var worksheetRelationships = ReadXml(archive, worksheetRelationshipsName);
        var drawingTarget = worksheetRelationships.Root?.Elements(PackageRelationshipNamespace + "Relationship")
            .FirstOrDefault(value => value.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing")
            ?.Attribute("Target")?.Value
            ?? throw new InvalidDataException("La hoja COTIZACIÃƒâ€œN no tiene drawing asociado.");
        var drawingEntryName = ResolveRelativeEntryName(worksheetEntryName, drawingTarget);
        var drawingRelationshipsName = DrawingRelationshipsEntryName(drawingEntryName);
        var drawingRelationships = ReadXml(archive, drawingRelationshipsName);
        var worksheet = ReadXml(archive, worksheetEntryName);
        var drawing = ReadXml(archive, drawingEntryName);
        for (var index = 0; index < items.Count; index++)
        {
            var imageBytes = Convert.FromBase64String(items[index].ImageBase64);
            var mediaEntryName = NextMediaEntryName(archive, index + 1);
            var mediaEntry = archive.CreateEntry(mediaEntryName, CompressionLevel.Optimal);
            using (var stream = mediaEntry.Open())
            {
                stream.Write(imageBytes);
            }

            var imageRelationshipId = NextRelationshipId(drawingRelationships);
            drawingRelationships.Root!.Add(new XElement(PackageRelationshipNamespace + "Relationship",
                new XAttribute("Id", imageRelationshipId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", RelativePath("xl/drawings", mediaEntryName))));
            drawing.Root!.Add(CreateImageAnchor(
                worksheet,
                imageRelationshipId,
                imageBytes,
                ItemBaseRow(index),
                items[index].ItemNumber,
                1001 + index));
        }

        ReplaceXml(archive, drawingRelationshipsName, drawingRelationships);
        ReplaceXml(archive, drawingEntryName, drawing);
    }

    private static XElement CreateImageAnchor(
        XDocument worksheet,
        string relationshipId,
        byte[] imageBytes,
        int row,
        string itemNumber,
        int drawingId)
    {
        var (width, height) = ReadPngDimensions(imageBytes);
        var areaWidthPixels = ColumnWidthToPixels(ReadColumnWidth(worksheet, 1))
            + ColumnWidthToPixels(ReadColumnWidth(worksheet, 2));
        var imageStartRow = row + ImageStartRowOffset;
        var areaHeightPixels = Enumerable.Range(imageStartRow, ImageRows)
            .Select(value => RowHeightToPixels(ReadRowHeight(worksheet, value)))
            .Sum();
        var scale = Math.Min(areaWidthPixels / width, areaHeightPixels / height);
        var renderedWidth = width * scale;
        var renderedHeight = height * scale;
        var horizontalOffset = PixelsToEmu((areaWidthPixels - renderedWidth) / 2);
        var verticalOffset = PixelsToEmu((areaHeightPixels - renderedHeight) / 2);
        var extCx = PixelsToEmu(renderedWidth);
        var extCy = PixelsToEmu(renderedHeight);

        return new XElement(DrawingNamespace + "oneCellAnchor",
            new XElement(DrawingNamespace + "from",
                new XElement(DrawingNamespace + "col", 0),
                new XElement(DrawingNamespace + "colOff", horizontalOffset),
                    new XElement(DrawingNamespace + "row", imageStartRow - 1),
                new XElement(DrawingNamespace + "rowOff", verticalOffset)),
            new XElement(DrawingNamespace + "ext",
                new XAttribute("cx", extCx),
                new XAttribute("cy", extCy)),
            new XElement(DrawingNamespace + "pic",
                new XElement(DrawingNamespace + "nvPicPr",
                    new XElement(DrawingNamespace + "cNvPr",
                        new XAttribute("id", drawingId.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("name", $"FP Pro Item {itemNumber}")),
                    new XElement(DrawingNamespace + "cNvPicPr")),
                new XElement(DrawingNamespace + "blipFill",
                    new XElement(MainDrawingNamespace + "blip",
                        new XAttribute(RelationshipNamespace + "embed", relationshipId)),
                    new XElement(MainDrawingNamespace + "stretch",
                        new XElement(MainDrawingNamespace + "fillRect"))),
                new XElement(DrawingNamespace + "spPr",
                    new XElement(MainDrawingNamespace + "xfrm",
                        new XElement(MainDrawingNamespace + "off",
                            new XAttribute("x", 0),
                            new XAttribute("y", 0)),
                        new XElement(MainDrawingNamespace + "ext",
                            new XAttribute("cx", extCx),
                            new XAttribute("cy", extCy))),
                    new XElement(MainDrawingNamespace + "prstGeom",
                        new XAttribute("prst", "rect"),
                        new XElement(MainDrawingNamespace + "avLst")))),
            new XElement(DrawingNamespace + "clientData"));
    }

    private static void SetString(XDocument worksheet, string reference, string value)
    {
        var cell = GetOrCreateCell(worksheet, reference);
        EnsureWritableCell(cell, reference);
        cell.Attribute("t")?.Remove();
        cell.SetAttributeValue("t", "inlineStr");
        cell.Element(SpreadsheetNamespace + "f")?.Remove();
        cell.Element(SpreadsheetNamespace + "v")?.Remove();
        cell.Element(SpreadsheetNamespace + "is")?.Remove();
        cell.Add(new XElement(SpreadsheetNamespace + "is",
            new XElement(SpreadsheetNamespace + "t", value)));
    }

    private static void SetNumber(XDocument worksheet, string reference, decimal value)
    {
        var cell = GetOrCreateCell(worksheet, reference);
        EnsureWritableCell(cell, reference);
        cell.Attribute("t")?.Remove();
        cell.Element(SpreadsheetNamespace + "f")?.Remove();
        cell.Element(SpreadsheetNamespace + "is")?.Remove();
        var valueElement = cell.Element(SpreadsheetNamespace + "v");
        if (valueElement is null)
        {
            valueElement = new XElement(SpreadsheetNamespace + "v");
            cell.Add(valueElement);
        }

        valueElement.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    private static decimal ToExcelPercent(decimal humanPercent) =>
        humanPercent / 100m;

    private static void EnsureWritableCell(XElement cell, string reference)
    {
        if (cell.Element(SpreadsheetNamespace + "f") is not null)
        {
            throw new InvalidDataException($"La celda {reference} contiene formula y no debe sobrescribirse.");
        }
    }

    private static void ForceFullCalculation(ZipArchive archive)
    {
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var calcPr = workbook.Root!.Element(SpreadsheetNamespace + "calcPr");
        if (calcPr is null)
        {
            calcPr = new XElement(SpreadsheetNamespace + "calcPr");
            workbook.Root.Add(calcPr);
        }

        calcPr.SetAttributeValue("calcMode", "auto");
        calcPr.SetAttributeValue("fullCalcOnLoad", "1");
        calcPr.SetAttributeValue("forceFullCalc", "1");
        ReplaceXml(archive, "xl/workbook.xml", workbook);
        RemoveCalcChain(archive);
    }

    private static void RemoveCalcChain(ZipArchive archive)
    {
        archive.GetEntry("xl/calcChain.xml")?.Delete();
        var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        relationships.Root!.Elements(PackageRelationshipNamespace + "Relationship")
            .Where(value => value.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain")
            .Remove();
        ReplaceXml(archive, "xl/_rels/workbook.xml.rels", relationships);

        var contentTypes = ReadXml(archive, "[Content_Types].xml");
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        contentTypes.Root!.Elements(ns + "Override")
            .Where(value => value.Attribute("PartName")?.Value == "/xl/calcChain.xml")
            .Remove();
        ReplaceXml(archive, "[Content_Types].xml", contentTypes);
    }

    private static void WriteCalculatedFormulaCachedValues(MemoryStream workbookStream)
    {
        var workbookBytes = workbookStream.ToArray();
        var cachedValues = CalculateFormulaCachedValues(workbookBytes);
        workbookStream.SetLength(0);
        workbookStream.Write(workbookBytes);
        workbookStream.Position = 0;
        using var archive = new ZipArchive(workbookStream, ZipArchiveMode.Update, leaveOpen: true);
        var worksheetEntries = ResolveWorksheetEntries(archive);
        foreach (var (sheetName, entryName) in worksheetEntries)
        {
            var sheetValues = cachedValues
                .Where(value => string.Equals(value.SheetName, sheetName, StringComparison.Ordinal))
                .ToDictionary(value => value.Reference, StringComparer.OrdinalIgnoreCase);
            if (sheetValues.Count == 0)
            {
                continue;
            }

            var worksheet = ReadXml(archive, entryName);
            foreach (var cell in worksheet.Descendants(SpreadsheetNamespace + "c")
                .Where(cell => cell.Element(SpreadsheetNamespace + "f") is not null))
            {
                var reference = cell.Attribute("r")?.Value;
                if (reference is null || !sheetValues.TryGetValue(reference, out var cachedValue))
                {
                    continue;
                }

                WriteFormulaCachedValue(cell, cachedValue);
            }

            ReplaceXml(archive, entryName, worksheet);
        }

        ForceFullCalculation(archive);
    }

    private static IReadOnlyList<FormulaCachedValue> CalculateFormulaCachedValues(byte[] workbookBytes)
    {
        using var stream = new MemoryStream(PrepareCalculationWorkbookBytes(workbookBytes));
        using var workbook = new XLWorkbook(stream);
        var values = new List<FormulaCachedValue>();
        foreach (var worksheet in workbook.Worksheets)
        {
            foreach (var cell in worksheet.CellsUsed(value => value.HasFormula))
            {
                try
                {
                    values.Add(FormulaCachedValue.FromCell(worksheet.Name, cell.Address.ToStringRelative(), cell.Value));
                }
                catch (NotImplementedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return values;
    }

    private static byte[] PrepareCalculationWorkbookBytes(byte[] workbookBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(workbookBytes);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetEntryNames = archive.Entries
                .Select(entry => entry.FullName)
                .Where(entryName => entryName.StartsWith("xl/worksheets/", StringComparison.Ordinal)
                    && entryName.EndsWith(".xml", StringComparison.Ordinal)
                    && !entryName.Contains("/_rels/", StringComparison.Ordinal))
                .ToArray();
            foreach (var worksheetEntryName in worksheetEntryNames)
            {
                var worksheet = ReadXml(archive, worksheetEntryName);
                worksheet.Root!.Elements(SpreadsheetNamespace + "drawing").Remove();
                RemoveLogisticsFormulaCachedValues(worksheet);
                ReplaceXml(archive, worksheetEntryName, worksheet);

                var relationshipsEntryName = WorksheetRelationshipsEntryName(worksheetEntryName);
                if (archive.GetEntry(relationshipsEntryName) is null)
                {
                    continue;
                }

                var relationships = ReadXml(archive, relationshipsEntryName);
                relationships.Root!.Elements(PackageRelationshipNamespace + "Relationship")
                    .Where(value => value.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing")
                    .Remove();
                ReplaceXml(archive, relationshipsEntryName, relationships);
            }
        }

        return stream.ToArray();
    }

    private static void RemoveLogisticsFormulaCachedValues(XDocument worksheet)
    {
        foreach (var cell in worksheet.Descendants(SpreadsheetNamespace + "c")
            .Where(cell => cell.Element(SpreadsheetNamespace + "f") is not null))
        {
            var reference = cell.Attribute("r")?.Value;
            if (reference is null || !ShouldRecalculateLogisticsFormula(reference))
            {
                continue;
            }

            cell.Attribute("t")?.Remove();
            cell.Element(SpreadsheetNamespace + "v")?.Remove();
            cell.Element(SpreadsheetNamespace + "is")?.Remove();
        }
    }

    private static bool ShouldRecalculateLogisticsFormula(string cellReference)
    {
        var column = ReadCellColumn(cellReference);
        var row = ReadRowNumber(cellReference);
        return column == "F" && row == 339
            || row == 14 && (column is "BR" or "BS" or "BT")
            || row <= 14 && (column is "CC" or "CD" or "CI" || row == 11 && column is "CH");
    }

    private static void WriteFormulaCachedValue(XElement cell, FormulaCachedValue cachedValue)
    {
        cell.Element(SpreadsheetNamespace + "is")?.Remove();
        var valueElement = cell.Element(SpreadsheetNamespace + "v");
        if (valueElement is null)
        {
            valueElement = new XElement(SpreadsheetNamespace + "v");
            cell.Add(valueElement);
        }

        switch (cachedValue.Kind)
        {
            case FormulaCachedValueKind.Number:
                cell.Attribute("t")?.Remove();
                valueElement.Value = cachedValue.Value;
                break;
            case FormulaCachedValueKind.Text:
                cell.SetAttributeValue("t", "str");
                valueElement.Value = cachedValue.Value;
                break;
            case FormulaCachedValueKind.Boolean:
                cell.SetAttributeValue("t", "b");
                valueElement.Value = cachedValue.Value;
                break;
            case FormulaCachedValueKind.Error:
                cell.SetAttributeValue("t", "e");
                valueElement.Value = cachedValue.Value;
                break;
            case FormulaCachedValueKind.Blank:
                cell.Attribute("t")?.Remove();
                valueElement.Value = string.Empty;
                break;
            default:
                throw new InvalidOperationException($"Unsupported formula cached value kind {cachedValue.Kind}.");
        }
    }

    private static void NormalizeFormulaCellsForRecalculation(XDocument worksheet, int itemCount)
    {
        var usedRows = Enumerable.Range(0, itemCount)
            .SelectMany(index => Enumerable.Range(ItemBaseRow(index), ItemBlockHeight))
            .Select(row => row.ToString(CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var cell in worksheet.Descendants(SpreadsheetNamespace + "c")
            .Where(cell => cell.Element(SpreadsheetNamespace + "f") is not null)
            .Where(cell => usedRows.Contains(ReadRowNumber(cell.Attribute("r")!.Value).ToString(CultureInfo.InvariantCulture))))
        {
            cell.Attribute("t")?.Remove();
            cell.Element(SpreadsheetNamespace + "v")?.Remove();
            cell.Element(SpreadsheetNamespace + "is")?.Remove();
        }
    }

    private static void SetTransportGlobalCorrection(
        XDocument cotizacionWorksheet,
        ZipArchive archive,
        string location,
        IReadOnlyList<FpProQuotationItemInput> items)
    {
        var (locationColumn, minimumColumn, rateColumn) = ResolveTransportColumns(archive);
        var (minimumTransport, ratePerKg) = ResolveTransportValues(archive, location, locationColumn, minimumColumn, rateColumn);
        var totalStructures = items.Sum(item => item.Quantity);
        if (totalStructures == 0m)
        {
            SetNumber(cotizacionWorksheet, "BR338", 0m);
            return;
        }

        var baseTransportTotal = items.Sum(item => ResolveTransportUnitWeight(item) * item.Quantity * ratePerKg);
        var correctionPerStructure = baseTransportTotal < minimumTransport
            ? (minimumTransport - baseTransportTotal) / totalStructures
            : 0m;
        SetNumber(cotizacionWorksheet, "BR338", correctionPerStructure);
    }

    private static void SetViaticsAndIntermunicipalValues(
        XDocument cotizacionWorksheet,
        ZipArchive archive,
        string location,
        IReadOnlyList<FpProQuotationItemInput> items)
    {
        var projectDays = CalculateProjectDays(items);
        var viaticsDays = decimal.Round(projectDays, 0, MidpointRounding.AwayFromZero);

        ApplyLogisticsValue(
            cotizacionWorksheet,
            archive,
            "VIATICOS",
            location,
            new[] { "CIUDAD", "UBICACION", "UBICACION", "ZONA", "MUNICIPIO", "CITY" },
            new[] { "N\u00ba DIAS", "N \u00ba DIAS", "N D\u00cdAS", "N\u00ba D\u00cdAS", "DIAS", "D\u00cdAS", "N\u00ba D\u00cdA" },
            viaticsDays);

        SetViaticsPeopleValue(cotizacionWorksheet, archive, CalculateViaticsPeople(location));

        ApplyLogisticsValue(
            cotizacionWorksheet,
            archive,
            "TRANSPORTES INTERMUNICIPALES",
            location,
            new[] { "CIUDAD", "UBICACION", "UBICACION", "ZONA", "MUNICIPIO", "CITY" },
            new[] { "N\u00ba RETORNOS", "N \u00ba RETORNOS", "RETORNOS", "N\u00ba RETORNO" },
            1m);
    }

    private static decimal CalculateProjectDays(IReadOnlyList<FpProQuotationItemInput> items)
    {
        var totalArea = items.Sum(item => item.WidthM * item.HeightM * item.Quantity);
        var br14 = decimal.Ceiling(totalArea * 0.05m) + 7m + 1.5m;
        return decimal.Ceiling(br14) * 0.6m + 2m;
    }

    private static decimal CalculateViaticsPeople(string location) =>
        string.Equals(NormalizeHeaderForLogistics(location), "BGA", StringComparison.Ordinal)
            ? 2m
            : 1m;

    private static void SetViaticsPeopleValue(
        XDocument cotizacionWorksheet,
        ZipArchive archive,
        decimal value)
    {
        var targetCell = ResolveCellBelowHeader(
            cotizacionWorksheet,
            archive,
            new[] { "N\u00ba PERSONAS", "N \u00ba PERSONAS", "N PERSONAS", "PERSONAS" });
        SetNumber(cotizacionWorksheet, targetCell, value);
    }

    private static string ResolveCellBelowHeader(
        XDocument worksheet,
        ZipArchive archive,
        IReadOnlyList<string> headerCandidates)
    {
        var normalizedHeaderCandidates = headerCandidates
            .Select(NormalizeHeaderForLogistics)
            .ToHashSet(StringComparer.Ordinal);
        var sharedStrings = ReadSharedStrings(archive);
        foreach (var cell in worksheet.Descendants(SpreadsheetNamespace + "c"))
        {
            var header = NormalizeHeaderForLogistics(ReadCellValueFromElement(cell, sharedStrings));
            if (!normalizedHeaderCandidates.Contains(header))
            {
                continue;
            }

            var cellReference = cell.Attribute("r")!.Value;
            return $"{ReadCellColumn(cellReference)}{ReadRowNumber(cellReference) + 1}";
        }

        throw new InvalidDataException("No se encontro la celda N PERSONAS en la hoja COTIZACION.");
    }

    private static void ApplyLogisticsValue(
        XDocument cotizacionWorksheet,
        ZipArchive archive,
        string blockTitle,
        string location,
        IReadOnlyList<string> locationHeaderCandidates,
        IReadOnlyList<string> valueHeaderCandidates,
        decimal value)
    {
        var (locationColumn, targetColumn, headerRowNumber) = ResolveLogisticsColumns(
            cotizacionWorksheet,
            archive,
            blockTitle,
            locationHeaderCandidates,
            valueHeaderCandidates);

        var normalizedLocation = NormalizeHeaderForLogistics(location);
        var sharedStrings = ReadSharedStrings(archive);
        var rowNumbers = cotizacionWorksheet.Descendants(SpreadsheetNamespace + "row")
            .Select(row => int.Parse(row.Attribute("r")!.Value, CultureInfo.InvariantCulture))
            .OrderBy(rowNumber => rowNumber)
            .Where(rowNumber => rowNumber > headerRowNumber && rowNumber <= headerRowNumber + 140)
            .ToArray();

        foreach (var rowNumber in rowNumbers)
        {
            var row = cotizacionWorksheet.Descendants(SpreadsheetNamespace + "row")
                .First(row => int.Parse(row.Attribute("r")!.Value, CultureInfo.InvariantCulture) == rowNumber);
            var rowLocation = ReadCellValueFromRow(row, locationColumn, sharedStrings);
            if (!string.Equals(NormalizeHeaderForLogistics(rowLocation), normalizedLocation, StringComparison.Ordinal))
            {
                continue;
            }

            SetNumber(cotizacionWorksheet, $"{targetColumn}{rowNumber}", value);
            return;
        }

        throw new InvalidDataException($"No se encontr\u00f3 la ciudad {location} en el bloque {blockTitle}.");
    }

    private static (string LocationColumn, string TargetColumn, int HeaderRowNumber) ResolveLogisticsColumns(
        XDocument cotizacionWorksheet,
        ZipArchive archive,
        string blockTitle,
        IReadOnlyList<string> locationHeaderCandidates,
        IReadOnlyList<string> valueHeaderCandidates)
    {
        var normalizedBlockTitle = NormalizeHeaderForLogistics(blockTitle);
        var normalizedLocationHeaderCandidates = locationHeaderCandidates
            .Select(NormalizeHeaderForLogistics)
            .ToHashSet(StringComparer.Ordinal);
        var normalizedValueHeaderCandidates = valueHeaderCandidates
            .Select(NormalizeHeaderForLogistics)
            .ToHashSet(StringComparer.Ordinal);
        var sharedStrings = ReadSharedStrings(archive);
        var rows = cotizacionWorksheet.Descendants(SpreadsheetNamespace + "row").ToArray();
        foreach (var row in rows)
        {
            var rowTexts = row.Elements(SpreadsheetNamespace + "c")
                .Select(cell => NormalizeHeaderForLogistics(ReadCellValueFromElement(cell, sharedStrings)))
                .ToArray();
            if (!rowTexts.Any(text => text.Contains(normalizedBlockTitle, StringComparison.Ordinal)))
            {
                continue;
            }

            var rowIndex = rows.ToList().IndexOf(row);
            for (var headerOffset = 1; headerOffset <= 20; headerOffset++)
            {
                if (rowIndex + headerOffset >= rows.Length)
                {
                    break;
                }

                var headerRow = rows[rowIndex + headerOffset];
                var headers = headerRow.Elements(SpreadsheetNamespace + "c")
                    .Select(cell => (Column: ReadCellColumn(cell.Attribute("r")!.Value), Header: NormalizeHeaderForLogistics(ReadCellValueFromElement(cell, sharedStrings))))
                    .ToDictionary(value => value.Column, value => value.Header, StringComparer.Ordinal);

                var locationColumn = headers
                    .Where(value => normalizedLocationHeaderCandidates.Contains(value.Value))
                    .Select(value => value.Key)
                    .FirstOrDefault();

                var valueColumn = headers
                    .Where(value => normalizedValueHeaderCandidates.Contains(value.Value))
                    .Select(value => value.Key)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(locationColumn) && !string.IsNullOrEmpty(valueColumn))
                {
                    var headerRowNumber = int.Parse(headerRow.Attribute("r")!.Value, CultureInfo.InvariantCulture);
                    return (locationColumn, valueColumn, headerRowNumber);
                }
            }
        }

        throw new InvalidDataException($"No se encontr\u00f3 el bloque {blockTitle} en la hoja COTIZACI\u00d3N.");
    }

    private static decimal? ReadCellDecimalValue(XDocument worksheet, string reference)
    {
        var cell = worksheet.Descendants(SpreadsheetNamespace + "c")
            .FirstOrDefault(cell => cell.Attribute("r")?.Value == reference);
        if (cell is null)
        {
            return null;
        }

        var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : null;
    }

    private static decimal ResolveTransportUnitWeight(FpProQuotationItemInput item)
    {
        return item.StructureWeightKg + (item.WidthM * item.HeightM * 2.5m * item.SelectedThicknessMm);
    }

    private static (string LocationColumn, string MinimumColumn, string RateColumn) ResolveTransportColumns(ZipArchive archive)
    {
        var bdSheet = ReadXml(archive, ResolveWorksheetEntryName(archive, "BD GN"));
        var sharedStrings = ReadSharedStrings(archive);
        var headerRow = DetectTransportHeaderRow(bdSheet, sharedStrings);
        if (headerRow is null)
        {
            throw new InvalidDataException("No se encontrÃƒÂ³ la fila de encabezados de transporte en BD GN.");
        }

        var headers = ReadRowCells(headerRow)
            .Select(cell => (Column: ReadCellColumn(cell.Attribute("r")!.Value), Header: NormalizeHeader(ReadCellValueFromElement(cell, sharedStrings))))
            .ToDictionary(item => item.Column, item => item.Header, StringComparer.Ordinal);

        var locationColumn = headers
            .Where(value => value.Value.Equals("CIUDAD", StringComparison.Ordinal))
            .Select(value => value.Key)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(locationColumn))
        {
            throw new InvalidDataException("No se encontrÃƒÂ³ la columna 'CIUDAD' en BD GN.");
        }

        var minimumColumn = headers
            .Where(value => value.Value.Equals("VR VIAJE", StringComparison.Ordinal)
                || value.Value.Equals("TOTAL MINIMO", StringComparison.Ordinal)
                || value.Value.Equals("MINIMO", StringComparison.Ordinal))
            .Select(value => value.Key)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(minimumColumn))
        {
            throw new InvalidDataException("No se encontrÃƒÂ³ la columna del mÃƒÂ­nimo de transporte en BD GN.");
        }

        var rateColumn = headers
            .Where(value => value.Value.Equals("VR KG", StringComparison.Ordinal))
            .Select(value => value.Key)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(rateColumn))
        {
            throw new InvalidDataException("No se encontrÃƒÂ³ la columna del valor por kilo de transporte en BD GN.");
        }

        return (locationColumn, minimumColumn, rateColumn);
    }

    private static XElement? DetectTransportHeaderRow(XDocument worksheet, string[] sharedStrings)
    {
        foreach (var row in worksheet.Descendants(SpreadsheetNamespace + "row"))
        {
            if (!int.TryParse(row.Attribute("r")?.Value, out var rowNumber) || rowNumber > 20)
            {
                break;
            }

            var headers = ReadRowCells(row)
                .Select(cell => NormalizeHeader(ReadCellValueFromElement(cell, sharedStrings)))
                .ToArray();
            if (headers.Contains("CIUDAD")
                && (headers.Contains("VR VIAJE") || headers.Contains("TOTAL MINIMO") || headers.Contains("MINIMO"))
                && headers.Contains("VR KG"))
            {
                return row;
            }
        }

        return null;
    }

    private static IReadOnlyList<XElement> ReadRowCells(XElement row) =>
        row.Elements(SpreadsheetNamespace + "c").ToArray();

    private static (decimal MinimumTransport, decimal RatePerKg) ResolveTransportValues(
        ZipArchive archive,
        string location,
        string locationColumn,
        string minimumColumn,
        string rateColumn)
    {
        var bdSheet = ReadXml(archive, ResolveWorksheetEntryName(archive, "BD GN"));
        var sharedStrings = ReadSharedStrings(archive);
        foreach (var row in bdSheet.Descendants(SpreadsheetNamespace + "row"))
        {
            if (!int.TryParse(row.Attribute("r")?.Value, out var rowNumber) || rowNumber <= 1)
            {
                continue;
            }

            var locationValue = ReadCellValueFromRow(row, locationColumn, sharedStrings);
            if (!string.Equals(locationValue.Trim(), location, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var minimumRaw = ReadCellValueFromRow(row, minimumColumn, sharedStrings);
            var rateRaw = ReadCellValueFromRow(row, rateColumn, sharedStrings);
            if (string.IsNullOrWhiteSpace(minimumRaw) || string.IsNullOrWhiteSpace(rateRaw))
            {
                throw new InvalidDataException($"No se encontrÃƒÂ³ tarifa o mÃƒÂ­nimo para la ubicaciÃƒÂ³n {location} en BD GN.");
            }

            if (!decimal.TryParse(minimumRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var minimumTransport))
            {
                throw new InvalidDataException($"El mÃƒÂ­nimo de transporte de la ubicaciÃƒÂ³n {location} no es numÃƒÂ©rico.");
            }

            if (!decimal.TryParse(rateRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var ratePerKg))
            {
                throw new InvalidDataException($"La tarifa por kilo de transporte de la ubicaciÃƒÂ³n {location} no es numÃƒÂ©rica.");
            }

            return (minimumTransport, ratePerKg);
        }

        throw new InvalidDataException($"No se encontrÃƒÂ³ la ubicaciÃƒÂ³n {location} en BD GN.");
    }

    private static string ReadCellValueFromRow(XElement row, string columnReference, IReadOnlyList<string> sharedStrings) =>
        ReadCellValueFromElement(
            row.Elements(SpreadsheetNamespace + "c")
                .FirstOrDefault(cell => string.Equals(ReadCellColumn(cell.Attribute("r")!.Value), columnReference, StringComparison.Ordinal)),
            sharedStrings);

    private static string ReadCellValueFromElement(XElement? cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (cell.Attribute("t")?.Value == "s")
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < sharedStrings.Count
                    ? sharedStrings[index]
                    : string.Empty;
        }

        return value;
    }

    private static string ReadCellColumn(string reference) =>
        new string(reference.TakeWhile(char.IsLetter).ToArray());

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
        {
            return [];
        }

        var sharedStringsDocument = ReadXml(archive, "xl/sharedStrings.xml");
        return sharedStringsDocument.Descendants(SpreadsheetNamespace + "si")
            .Select(value => string.Concat(value.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string NormalizeHeader(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Replace("ÃƒÂ", "I").Replace("Ãƒâ€œ", "O");
    }


    private static string NormalizeHeaderForLogistics(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Normalize(NormalizationForm.FormD)
            .ToUpperInvariant()
            .Trim();
        var normalizedBuilder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                normalizedBuilder.Append(character);
            }
        }

        return normalizedBuilder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace("\u00b0", string.Empty)
            .Replace("\u00ba", string.Empty)
            .Replace(".", string.Empty)
            .Replace("/", " ")
            .Trim();
    }

    private static int DetectTemplateCapacity(XDocument worksheet)
    {
        var formulaRows = worksheet.Descendants(SpreadsheetNamespace + "c")
            .Where(cell => cell.Attribute("r")?.Value.StartsWith('L') == true)
            .Where(cell => cell.Element(SpreadsheetNamespace + "f")?.Value.Contains("*K", StringComparison.Ordinal) == true)
            .Select(cell => ReadRowNumber(cell.Attribute("r")!.Value))
            .Where(row => row >= FirstItemRow && (row - FirstItemRow) % ItemBlockHeight == 0)
            .Order()
            .ToArray();

        return formulaRows.Length;
    }

    private static int ItemBaseRow(int index) =>
        FirstItemRow + (index * ItemBlockHeight);

    private static string Cell(string column, int row) =>
        $"{column}{row}";

    private static XElement GetOrCreateCell(XDocument worksheet, string reference)
    {
        var rowNumber = ReadRowNumber(reference);
        var sheetData = worksheet.Root!.Element(SpreadsheetNamespace + "sheetData")
            ?? throw new InvalidDataException("La hoja no contiene sheetData.");
        var row = sheetData.Elements(SpreadsheetNamespace + "row")
            .FirstOrDefault(value => value.Attribute("r")?.Value == rowNumber.ToString(CultureInfo.InvariantCulture));
        if (row is null)
        {
            row = new XElement(SpreadsheetNamespace + "row", new XAttribute("r", rowNumber));
            sheetData.Add(row);
        }

        var cell = row.Elements(SpreadsheetNamespace + "c")
            .FirstOrDefault(value => value.Attribute("r")?.Value == reference);
        if (cell is not null)
        {
            return cell;
        }

        cell = new XElement(SpreadsheetNamespace + "c", new XAttribute("r", reference));
        row.Add(cell);
        return cell;
    }

    private static string ResolveTemplatePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "Templates", "FormatoCotizacion.xlsx"),
            Path.GetFullPath(TemplateRelativePath),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", TemplateRelativePath))
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("No se encontro la plantilla oficial de cotizacion.", TemplateRelativePath);
    }

    private static string ResolveWorksheetEntryName(ZipArchive archive, string sheetName)
    {
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        var sheet = workbook.Descendants(SpreadsheetNamespace + "sheet")
            .FirstOrDefault(value => string.Equals(value.Attribute("name")?.Value, sheetName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"La plantilla no contiene la hoja {sheetName}.");
        var relationshipId = sheet.Attribute(RelationshipNamespace + "id")?.Value
            ?? throw new InvalidDataException($"La hoja {sheetName} no tiene relacion.");
        var relationship = relationships.Root?.Elements()
            .FirstOrDefault(value => string.Equals(value.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"No se encontro la relacion de la hoja {sheetName}.");
        var target = relationship.Attribute("Target")?.Value
            ?? throw new InvalidDataException($"La relacion de {sheetName} no tiene destino.");

        return target.StartsWith("xl/", StringComparison.Ordinal)
            ? target
            : $"xl/{target.TrimStart('/')}";
    }

    private static IReadOnlyList<(string SheetName, string EntryName)> ResolveWorksheetEntries(ZipArchive archive)
    {
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        return workbook.Descendants(SpreadsheetNamespace + "sheet")
            .Select(sheet =>
            {
                var relationshipId = sheet.Attribute(RelationshipNamespace + "id")?.Value
                    ?? throw new InvalidDataException($"La hoja {sheet.Attribute("name")?.Value} no tiene relacion.");
                var relationship = relationships.Root?.Elements()
                    .FirstOrDefault(value => string.Equals(value.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
                    ?? throw new InvalidDataException($"No se encontro la relacion de la hoja {sheet.Attribute("name")?.Value}.");
                var target = relationship.Attribute("Target")?.Value
                    ?? throw new InvalidDataException($"La relacion de {sheet.Attribute("name")?.Value} no tiene destino.");
                var entryName = target.StartsWith("xl/", StringComparison.Ordinal)
                    ? target
                    : $"xl/{target.TrimStart('/')}";

                return (sheet.Attribute("name")!.Value, entryName);
            })
            .ToArray();
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"La plantilla no contiene {entryName}.");
        using var stream = entry.Open();
        return XDocument.Load(stream, System.Xml.Linq.LoadOptions.PreserveWhitespace);
    }

    private static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static void EnsurePngContentType(ZipArchive archive)
    {
        var document = ReadXml(archive, "[Content_Types].xml");
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        if (!document.Root!.Elements(ns + "Default")
            .Any(value => value.Attribute("Extension")?.Value == "png"))
        {
            document.Root.Add(new XElement(ns + "Default",
                new XAttribute("Extension", "png"),
                new XAttribute("ContentType", "image/png")));
            ReplaceXml(archive, "[Content_Types].xml", document);
        }
    }

    private static string NextMediaEntryName(ZipArchive archive, int itemIndex)
    {
        var suffix = itemIndex.ToString("00", CultureInfo.InvariantCulture);
        var candidate = $"xl/media/fp-pro-item-{suffix}.png";
        if (archive.GetEntry(candidate) is null)
        {
            return candidate;
        }

        var index = itemIndex;
        do
        {
            index++;
            suffix = index.ToString("00", CultureInfo.InvariantCulture);
            candidate = $"xl/media/fp-pro-item-{suffix}.png";
        } while (archive.GetEntry(candidate) is not null);

        return candidate;
    }

    private static string WorksheetRelationshipsEntryName(string worksheetEntryName)
    {
        var fileName = Path.GetFileName(worksheetEntryName);
        return $"xl/worksheets/_rels/{fileName}.rels";
    }

    private static string DrawingRelationshipsEntryName(string drawingEntryName)
    {
        var fileName = Path.GetFileName(drawingEntryName);
        return $"xl/drawings/_rels/{fileName}.rels";
    }

    private static string ResolveRelativeEntryName(string sourceEntryName, string target)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceEntryName)!.Replace('\\', '/');
        var combined = Path.GetFullPath(Path.Combine(sourceDirectory, target.Replace('/', Path.DirectorySeparatorChar)))
            .Replace('\\', '/');
        var root = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
        return combined.StartsWith(root, StringComparison.Ordinal)
            ? combined[root.Length..]
            : combined;
    }

    private static string RelativePath(string sourceDirectory, string targetEntryName)
    {
        var source = sourceDirectory.TrimEnd('/') + "/";
        return Path.GetRelativePath(source, targetEntryName)
            .Replace('\\', '/');
    }

    private static string NextRelationshipId(XDocument relationships)
    {
        var max = relationships.Root!.Elements(PackageRelationshipNamespace + "Relationship")
            .Select(value => value.Attribute("Id")?.Value)
            .Select(value => value is not null && value.StartsWith("rId", StringComparison.Ordinal)
                && int.TryParse(value[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0)
            .DefaultIfEmpty()
            .Max();
        return $"rId{max + 1}";
    }

    private static (int Width, int Height) ReadPngDimensions(byte[] bytes)
    {
        if (bytes.Length < 24
            || bytes[0] != 137
            || bytes[1] != 80
            || bytes[2] != 78
            || bytes[3] != 71)
        {
            throw new InvalidDataException("La imagen del item debe ser PNG.");
        }

        return (
            ReadBigEndianInt32(bytes.AsSpan(16, 4)),
            ReadBigEndianInt32(bytes.AsSpan(20, 4)));
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static long PixelsToEmu(double pixels) =>
        Convert.ToInt64(Math.Round(pixels * 9525, MidpointRounding.AwayFromZero));

    private static double ReadColumnWidth(XDocument worksheet, int columnIndex)
    {
        foreach (var column in worksheet.Descendants(SpreadsheetNamespace + "col"))
        {
            var min = int.Parse(column.Attribute("min")!.Value, CultureInfo.InvariantCulture);
            var max = int.Parse(column.Attribute("max")!.Value, CultureInfo.InvariantCulture);
            if (columnIndex >= min && columnIndex <= max)
            {
                return double.Parse(column.Attribute("width")!.Value, CultureInfo.InvariantCulture);
            }
        }

        return 8.43d;
    }

    private static double ReadRowHeight(XDocument worksheet, int rowIndex)
    {
        var row = worksheet.Descendants(SpreadsheetNamespace + "row")
            .FirstOrDefault(value => value.Attribute("r")?.Value == rowIndex.ToString(CultureInfo.InvariantCulture));
        return row?.Attribute("ht") is { } height
            ? double.Parse(height.Value, CultureInfo.InvariantCulture)
            : 15d;
    }

    private static double ColumnWidthToPixels(double width) =>
        Math.Truncate(((256d * width) + Math.Truncate(128d / 7d)) / 256d * 7d);

    private static double RowHeightToPixels(double heightPoints) =>
        heightPoints * 96d / 72d;

    private static int ReadRowNumber(string reference) =>
        int.Parse(new string(reference.Where(char.IsAsciiDigit).ToArray()), CultureInfo.InvariantCulture);

    private static string BuildFileName(string proposalName)
    {
        var safeName = string.Concat(proposalName.Where(value => !IsInvalidProposalFileNameChar(value))).Trim();
        if (safeName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return safeName;
        }

        return $"{safeName}.xlsx";
    }

    private static bool IsInvalidProposalFileNameChar(char value) =>
        value is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|';

    private enum FormulaCachedValueKind
    {
        Blank,
        Number,
        Text,
        Boolean,
        Error
    }

    private sealed record FormulaCachedValue(
        string SheetName,
        string Reference,
        FormulaCachedValueKind Kind,
        string Value)
    {
        public static FormulaCachedValue FromCell(string sheetName, string reference, XLCellValue value)
        {
            if (value.IsBlank)
            {
                return new FormulaCachedValue(sheetName, reference, FormulaCachedValueKind.Blank, string.Empty);
            }

            if (value.IsNumber)
            {
                return new FormulaCachedValue(
                    sheetName,
                    reference,
                    FormulaCachedValueKind.Number,
                    value.GetNumber().ToString("G15", CultureInfo.InvariantCulture));
            }

            if (value.IsText)
            {
                return new FormulaCachedValue(sheetName, reference, FormulaCachedValueKind.Text, value.GetText());
            }

            if (value.IsBoolean)
            {
                return new FormulaCachedValue(
                    sheetName,
                    reference,
                    FormulaCachedValueKind.Boolean,
                    value.GetBoolean() ? "1" : "0");
            }

            if (value.IsDateTime)
            {
                return new FormulaCachedValue(
                    sheetName,
                    reference,
                    FormulaCachedValueKind.Number,
                    value.GetDateTime().ToOADate().ToString("G15", CultureInfo.InvariantCulture));
            }

            if (value.IsTimeSpan)
            {
                return new FormulaCachedValue(
                    sheetName,
                    reference,
                    FormulaCachedValueKind.Number,
                    value.GetTimeSpan().TotalDays.ToString("G15", CultureInfo.InvariantCulture));
            }

            if (value.IsError)
            {
                return new FormulaCachedValue(sheetName, reference, FormulaCachedValueKind.Error, value.GetError().ToString());
            }

            throw new InvalidDataException($"Unsupported formula value type for {sheetName}!{reference}.");
        }
    }
}
