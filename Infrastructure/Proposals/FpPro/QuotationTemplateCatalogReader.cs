using System.IO.Compression;
using System.Xml.Linq;
using Application.Common.Abstractions.Proposals;

namespace Infrastructure.Proposals.FpPro;

public sealed class QuotationTemplateCatalogReader : IQuotationTemplateCatalogReader
{
    private const string TemplateRelativePath = "Infrastructure/Resources/Templates/FormatoCotizacion.xlsx";
    private const string SheetName = "BD GN";
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public Task<QuotationTemplateCatalog> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveTemplatePath();
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheet = ReadWorksheet(archive);

        var systems = new List<QuotationTemplateSystemOption>();
        var glassDescriptions = new List<QuotationTemplateCatalogOption>();
        var finishes = new List<QuotationTemplateCatalogOption>();
        var locations = new List<QuotationTemplateCatalogOption>();

        foreach (var row in worksheet.Descendants(SpreadsheetNamespace + "row"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = row.Elements(SpreadsheetNamespace + "c")
                .ToDictionary(
                    cell => ReadColumn(cell.Attribute("r")?.Value),
                    cell => ReadCellValue(cell, sharedStrings),
                    StringComparer.Ordinal);

            AddSystem(cells, systems);
            AddSelectableValue(cells.GetValueOrDefault("G"), glassDescriptions, "CRISTALES");
            AddSelectableValue(cells.GetValueOrDefault("I"), finishes, "ACABADO ALUMINIO");
            AddLocation(row, cells, locations);
        }

        return Task.FromResult(new QuotationTemplateCatalog(
            systems,
            glassDescriptions,
            finishes,
            locations));
    }

    private static void AddSystem(
        IReadOnlyDictionary<string, string> cells,
        ICollection<QuotationTemplateSystemOption> systems)
    {
        var technicalName = cells.GetValueOrDefault("B")?.Trim();
        var excelValue = cells.GetValueOrDefault("C");
        var label = excelValue?.Trim();
        if (string.IsNullOrWhiteSpace(technicalName)
            || string.IsNullOrWhiteSpace(excelValue)
            || string.Equals(technicalName, "SISTEMAS", StringComparison.OrdinalIgnoreCase)
            || systems.Any(value => string.Equals(value.Label, label, StringComparison.Ordinal)))
        {
            return;
        }

        systems.Add(new QuotationTemplateSystemOption(
            technicalName,
            label!,
            excelValue,
            cells.GetValueOrDefault("E")?.Trim()));
    }

    private static void AddSelectableValue(
        string? value,
        ICollection<QuotationTemplateCatalogOption> values,
        params string[] excludedValues)
    {
        var label = value?.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || excludedValues.Contains(label, StringComparer.OrdinalIgnoreCase)
            || values.Any(option => string.Equals(option.Label, label, StringComparison.Ordinal)))
        {
            return;
        }

        values.Add(new QuotationTemplateCatalogOption(label!, value));
    }

    private static void AddLocation(
        XElement row,
        IReadOnlyDictionary<string, string> cells,
        ICollection<QuotationTemplateCatalogOption> locations)
    {
        if (row.Attribute("r")?.Value != "1")
        {
            return;
        }

        foreach (var column in new[] { "CC", "CD", "CE", "CF", "CG" })
        {
            AddSelectableValue(cells.GetValueOrDefault(column), locations);
        }
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

    private static XDocument ReadWorksheet(ZipArchive archive)
    {
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        var sheet = workbook.Descendants(SpreadsheetNamespace + "sheet")
            .FirstOrDefault(value => string.Equals(value.Attribute("name")?.Value, SheetName, StringComparison.Ordinal))
            ?? throw new InvalidDataException("La plantilla no contiene la hoja BD GN.");
        var relationshipId = sheet.Attribute(RelationshipNamespace + "id")?.Value
            ?? throw new InvalidDataException("La hoja BD GN no tiene relacion.");
        var relationship = relationships.Root?.Elements()
            .FirstOrDefault(value => string.Equals(value.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
            ?? throw new InvalidDataException("No se encontro la relacion de la hoja BD GN.");
        var target = relationship.Attribute("Target")?.Value
            ?? throw new InvalidDataException("La relacion de BD GN no tiene destino.");
        var entryName = target.StartsWith("xl/", StringComparison.Ordinal)
            ? target
            : $"xl/{target.TrimStart('/')}";

        return ReadXml(archive, entryName);
    }

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(SpreadsheetNamespace + "si")
            .Select(value => string.Concat(value.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"La plantilla no contiene {entryName}.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
        if (value is null)
        {
            return string.Empty;
        }

        return string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.Ordinal)
            ? sharedStrings[int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)]
            : value;
    }

    private static string ReadColumn(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return string.Empty;
        }

        var index = 0;
        while (index < reference.Length && char.IsAsciiLetterUpper(reference[index]))
        {
            index++;
        }

        return reference[..index];
    }
}
