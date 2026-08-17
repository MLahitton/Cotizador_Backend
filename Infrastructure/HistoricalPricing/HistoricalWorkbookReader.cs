using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Application.Common.Abstractions.HistoricalPricing;

namespace Infrastructure.HistoricalPricing;

public sealed partial class HistoricalWorkbookReader
{
    private const string QuotationSheet = "COTIZACION";
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public HistoricalWorkbookInspection Inspect(string path, string sourceIdentifier)
    {
        if (new FileInfo(path).Length == 0)
            return Failed(path, sourceIdentifier, "", HistoricalWorkbookContainerType.Empty, "HistoricalWorkbookEmpty", "El archivo esta vacio.");

        var hash = ComputeSha256(path);
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[8];
        _ = stream.Read(signature);
        if (signature.SequenceEqual(OleSignature))
            return Failed(path, sourceIdentifier, hash, HistoricalWorkbookContainerType.OleCdfV2, "HistoricalWorkbookOleUnsupported", "El contenedor OLE/CDFV2 no es procesable en esta fase.");
        if (signature[0] != 0x50 || signature[1] != 0x4B)
            return Failed(path, sourceIdentifier, hash, HistoricalWorkbookContainerType.Unknown, "HistoricalWorkbookUnknownContainer", "El contenedor no es OOXML ni OLE/CDFV2.");

        try
        {
            using var workbook = OoxmlWorkbook.Open(path);
            var names = workbook.SheetNames;
            var hasQuotation = names.Any(name => Normalize(name) == QuotationSheet);
            return new HistoricalWorkbookInspection(
                Path.GetFileName(path), sourceIdentifier, hash,
                HistoricalWorkbookContainerType.Ooxml, hasQuotation, names,
                hasQuotation,
                hasQuotation ? [] : [new HistoricalQuoteIssue("HistoricalQuotationSheetMissing", "No existe la hoja COTIZACION.")], []);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException)
        {
            return Failed(path, sourceIdentifier, hash, HistoricalWorkbookContainerType.Unknown, "HistoricalWorkbookCorrupt", exception.Message);
        }
    }

    public HistoricalQuote Parse(string path, HistoricalWorkbookInspection inspection)
    {
        using var workbook = OoxmlWorkbook.Open(path);
        var sheetName = workbook.SheetNames.First(name => Normalize(name) == QuotationSheet);
        var rows = workbook.ReadRows(sheetName);
        return new HistoricalQuote(
            inspection.Sha256,
            FindMetadata(rows, "ID PRESUPUESTO", "PRESUPUESTO"),
            FindMetadata(rows, "CLIENTE"),
            FindMetadata(rows, "PROYECTO", "OBRA"),
            FindMetadata(rows, "UBICACION", "CIUDAD"),
            ParseVersion(inspection.FileName), "COP", FindPublicDocumentTotal(rows),
            new HistoricalQuoteSource(inspection.FileName, inspection.SourceIdentifier,
                inspection.Sha256, sheetName, inspection.DuplicateFileNames),
            ParseItems(rows, sheetName, inspection.Sha256), []);
    }

    private static IReadOnlyList<HistoricalQuoteItem> ParseItems(IReadOnlyList<WorkbookRow> rows, string sheetName, string quoteHash)
    {
        var result = new List<HistoricalQuoteItem>();
        HeaderMap? header = null;
        ItemBuilder? active = null;
        foreach (var row in rows)
        {
            if (IsHeader(row))
            {
                Flush();
                header = HeaderMap.From(row);
                continue;
            }
            if (header is null) continue;

            var reference = Clean(row.Get(header.ItemColumn));
            var description = Clean(row.Get(header.DescriptionColumn));
            var hasStructure = HasValue(row.Get(header.WidthColumn))
                || HasValue(row.Get(header.HeightColumn))
                || HasValue(row.Get(header.AreaColumn))
                || HasValue(row.Get(header.QuantityColumn));
            var hasCommercialValue = HasValue(row.Get(header.UnitPriceColumn))
                || HasValue(row.Get(header.TotalColumn));
            if (reference is not null && reference != "0" && Normalize(reference) != "ITEM"
                && description is not null && hasStructure && hasCommercialValue)
            {
                Flush();
                active = ItemBuilder.From(row, header, result.Count + 1);
                continue;
            }
            if (active is null) continue;

            var value = description;
            switch (Normalize(row.Get(header.ConfigurationColumn)))
            {
                case "SISTEMA": active.SystemRaw = value; active.AddCell(row, header.DescriptionColumn); break;
                case "CRISTAL":
                case "VIDRIO": active.GlassRaw = value; active.AddCell(row, header.DescriptionColumn); break;
                case "ACABADO ALUMINIO":
                case "ACABADO": active.FinishRaw = value; active.AddCell(row, header.DescriptionColumn); break;
                case "OBSERVACIONES":
                case "OBSERVACION": active.Notes = value; active.AddCell(row, header.DescriptionColumn); break;
            }
        }
        Flush();
        return result;

        void Flush()
        {
            if (active is null) return;
            result.Add(active.Build(quoteHash, sheetName));
            active = null;
        }
    }

    private static bool IsHeader(WorkbookRow row)
    {
        var values = row.Cells.Values.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        return values.Contains("ITEM") && values.Contains("DESCRIPCION")
            && values.Any(value => value is "VR UNT" or "VALOR UNITARIO")
            && values.Any(value => value is "VR TOTAL" or "VALOR TOTAL");
    }

    private static string? FindMetadata(IReadOnlyList<WorkbookRow> rows, params string[] labels)
    {
        var accepted = labels.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows.Take(40))
        foreach (var cell in row.Cells.OrderBy(pair => pair.Key))
        {
            if (!accepted.Contains(Normalize(cell.Value))) continue;
            var value = row.Cells.Where(pair => pair.Key > cell.Key).OrderBy(pair => pair.Key)
                .Select(pair => Clean(pair.Value)).FirstOrDefault(candidate => candidate is not null);
            if (value is not null) return value;
        }
        return null;
    }

    private static decimal? FindPublicDocumentTotal(IReadOnlyList<WorkbookRow> rows)
    {
        foreach (var row in rows)
        foreach (var cell in row.Cells.Where(pair => pair.Key <= 16).OrderBy(pair => pair.Key))
        {
            if (Normalize(cell.Value) != "VALOR TOTAL") continue;
            var value = row.Cells.Where(pair => pair.Key > cell.Key && pair.Key <= 16)
                .OrderBy(pair => pair.Key).Select(pair => ParseDecimal(pair.Value))
                .FirstOrDefault(candidate => candidate is > 0);
            if (value is > 0) return value;
        }
        return null;
    }

    private static HistoricalWorkbookInspection Failed(string path, string identifier, string hash, HistoricalWorkbookContainerType type, string code, string message) =>
        new(Path.GetFileName(path), identifier, hash, type, false, [], false, [new HistoricalQuoteIssue(code, message)], []);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder();
        foreach (var character in value.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(character));
        return SpacesRegex().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Normalize(value) == "CONTRATADO") return null;
        var cleaned = value.Trim().Replace("$", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)) return invariant;
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out var local) ? local : null;
    }

    private static string? ParseVersion(string fileName)
    {
        var match = VersionRegex().Match(fileName);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
    [GeneratedRegex(@"\bV\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    private sealed record HeaderMap(int ItemColumn, int LocationColumn, int ConfigurationColumn, int DescriptionColumn, int WidthColumn, int HeightColumn, int AreaColumn, int QuantityColumn, int UnitPriceColumn, int TotalColumn)
    {
        public static HeaderMap From(WorkbookRow row) => new(
            Find(row, "ITEM"), Find(row, "UBICACION"), Find(row, "CONFIGURACION"), Find(row, "DESCRIPCION"),
            Find(row, "AN", "AN (MT)", "ANCHO"), Find(row, "AL", "AL (MT)", "ALTO"), Find(row, "M2", "AREA"),
            Find(row, "CNT", "CANTIDAD"), Find(row, "VR UNT", "VALOR UNITARIO"), Find(row, "VR TOTAL", "VALOR TOTAL"));

        private static int Find(WorkbookRow row, params string[] labels)
        {
            var accepted = labels.Select(Normalize).ToHashSet(StringComparer.Ordinal);
            return row.Cells.FirstOrDefault(pair => accepted.Contains(Normalize(pair.Value))).Key;
        }
    }

    private sealed class ItemBuilder
    {
        private readonly int _ordinal;
        private readonly List<string> _sourceCells = [];
        public string? Reference { get; private init; }
        public string? Description { get; private init; }
        public string? Location { get; private init; }
        public string? Configuration { get; private init; }
        public string? WidthRaw { get; private init; }
        public string? HeightRaw { get; private init; }
        public string? AreaRaw { get; private init; }
        public string? QuantityRaw { get; private init; }
        public string? UnitPriceRaw { get; private init; }
        public string? TotalRaw { get; private init; }
        public string? SystemRaw { get; set; }
        public string? GlassRaw { get; set; }
        public string? FinishRaw { get; set; }
        public string? Notes { get; set; }

        private ItemBuilder(int ordinal) => _ordinal = ordinal;

        public static ItemBuilder From(WorkbookRow row, HeaderMap header, int ordinal)
        {
            var builder = new ItemBuilder(ordinal)
            {
                Reference = Clean(row.Get(header.ItemColumn)), Location = Clean(row.Get(header.LocationColumn)),
                Configuration = Clean(row.Get(header.ConfigurationColumn)), Description = Clean(row.Get(header.DescriptionColumn)),
                WidthRaw = Clean(row.Get(header.WidthColumn)), HeightRaw = Clean(row.Get(header.HeightColumn)),
                AreaRaw = Clean(row.Get(header.AreaColumn)), QuantityRaw = Clean(row.Get(header.QuantityColumn)),
                UnitPriceRaw = Clean(row.Get(header.UnitPriceColumn)), TotalRaw = Clean(row.Get(header.TotalColumn))
            };
            foreach (var column in new[] { header.ItemColumn, header.DescriptionColumn, header.WidthColumn, header.HeightColumn, header.AreaColumn, header.QuantityColumn, header.UnitPriceColumn, header.TotalColumn })
                builder.AddCell(row, column);
            return builder;
        }

        public void AddCell(WorkbookRow row, int column)
        {
            if (column > 0 && row.Cells.ContainsKey(column)) _sourceCells.Add(ToColumnName(column) + row.Number.ToString(CultureInfo.InvariantCulture));
        }

        public HistoricalQuoteItem Build(string quoteHash, string sheetName)
        {
            var width = ParseDecimal(WidthRaw);
            var height = ParseDecimal(HeightRaw);
            var quantity = ParseDecimal(QuantityRaw);
            var reportedArea = ParseDecimal(AreaRaw);
            var derivedArea = width is not null && height is not null ? width * height * (quantity is > 0 ? quantity : 1m) : null;
            var unitPrice = ParseDecimal(UnitPriceRaw);
            var total = ParseDecimal(TotalRaw);
            if (unitPrice is null && total is > 0 && quantity is > 0) unitPrice = total / quantity;
            var issues = new List<HistoricalQuoteIssue>();
            if (reportedArea is not null && derivedArea is not null && Math.Abs(reportedArea.Value - derivedArea.Value) > Math.Max(0.01m, reportedArea.Value * 0.05m))
                issues.Add(new HistoricalQuoteIssue("HistoricalAreaMismatch", "El area reportada difiere materialmente del area derivada."));
            if (unitPrice is <= 0)
            {
                issues.Add(new HistoricalQuoteIssue("HistoricalPriceNotUsable", "El precio unitario no es utilizable para comparables."));
                unitPrice = null;
            }
            var systemRaw = SystemRaw ?? (Normalize(Configuration) == "SISTEMA" ? Description : null);
            return new HistoricalQuoteItem(
                $"{quoteHash}:{_ordinal}", Reference, Description, Location, Configuration,
                HistoricalQuoteNormalizer.NormalizeText(Configuration), width, height, WidthRaw, HeightRaw,
                reportedArea, derivedArea, quantity, QuantityRaw,
                HistoricalQuoteNormalizer.InferCategory(Description, Location), systemRaw,
                HistoricalQuoteNormalizer.NormalizeText(systemRaw), GlassRaw,
                HistoricalQuoteNormalizer.GlassFamily(GlassRaw), HistoricalQuoteNormalizer.GlassThickness(GlassRaw),
                HistoricalQuoteNormalizer.GlassComposition(GlassRaw), FinishRaw,
                HistoricalQuoteNormalizer.NormalizeText(FinishRaw), unitPrice, total is > 0 ? total : null,
                Notes, sheetName, _sourceCells.Distinct(StringComparer.Ordinal).ToArray(), issues);
        }
    }

    private static string ToColumnName(int column)
    {
        var name = "";
        while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
        return name;
    }

    private sealed record WorkbookRow(int Number, IReadOnlyDictionary<int, string> Cells)
    {
        public string? Get(int column) => column > 0 && Cells.TryGetValue(column, out var value) ? value : null;
    }

    private sealed class OoxmlWorkbook : IDisposable
    {
        private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private readonly ZipArchive _archive;
        private readonly IReadOnlyDictionary<string, string> _sheetTargets;
        private readonly IReadOnlyList<string> _sharedStrings;

        private OoxmlWorkbook(ZipArchive archive, IReadOnlyDictionary<string, string> targets, IReadOnlyList<string> strings) { _archive = archive; _sheetTargets = targets; _sharedStrings = strings; }
        public IReadOnlyList<string> SheetNames => _sheetTargets.Keys.ToArray();

        public static OoxmlWorkbook Open(string path)
        {
            var archive = ZipFile.OpenRead(path);
            try
            {
                var workbook = LoadXml(archive, "xl/workbook.xml");
                var relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
                var targetsById = relationships.Root!.Elements(PackageRelationships + "Relationship")
                    .ToDictionary(node => (string)node.Attribute("Id")!, node => (string)node.Attribute("Target")!);
                var targets = workbook.Root!.Descendants(Spreadsheet + "sheet").ToDictionary(
                    node => (string)node.Attribute("name")!,
                    node => NormalizeTarget(targetsById[(string)node.Attribute(OfficeRelationships + "id")!]));
                var shared = archive.GetEntry("xl/sharedStrings.xml");
                IReadOnlyList<string> strings = shared is null ? [] : XDocument.Load(shared.Open()).Root!
                    .Elements(Spreadsheet + "si").Select(node => string.Concat(node.Descendants(Spreadsheet + "t").Select(text => text.Value))).ToArray();
                return new OoxmlWorkbook(archive, targets, strings);
            }
            catch { archive.Dispose(); throw; }
        }

        public IReadOnlyList<WorkbookRow> ReadRows(string sheetName)
        {
            var document = LoadXml(_archive, _sheetTargets[sheetName]);
            return document.Descendants(Spreadsheet + "row").Select(row => new WorkbookRow(
                (int?)row.Attribute("r") ?? 0,
                row.Elements(Spreadsheet + "c").Select(cell => (Column: ParseColumn((string?)cell.Attribute("r")), Value: ReadCell(cell)))
                    .Where(cell => cell.Column > 0 && cell.Value is not null).ToDictionary(cell => cell.Column, cell => cell.Value!))).ToArray();
        }

        private string? ReadCell(XElement cell)
        {
            var type = (string?)cell.Attribute("t");
            if (type == "inlineStr") return string.Concat(cell.Descendants(Spreadsheet + "t").Select(node => node.Value));
            var value = cell.Element(Spreadsheet + "v")?.Value;
            return type == "s" && int.TryParse(value, out var index) && index >= 0 && index < _sharedStrings.Count ? _sharedStrings[index] : value;
        }

        private static int ParseColumn(string? reference)
        {
            var result = 0;
            foreach (var character in reference ?? "") { if (!char.IsLetter(character)) break; result = result * 26 + char.ToUpperInvariant(character) - 'A' + 1; }
            return result;
        }

        private static XDocument LoadXml(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Falta la parte OOXML {name}.");
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        private static string NormalizeTarget(string target)
        {
            var normalized = target.Replace('\\', '/').TrimStart('/');
            while (normalized.StartsWith("../", StringComparison.Ordinal)) normalized = normalized[3..];
            return normalized.StartsWith("xl/", StringComparison.Ordinal) ? normalized : "xl/" + normalized;
        }
        public void Dispose() => _archive.Dispose();
    }
}
