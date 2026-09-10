using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Application.Common.Abstractions.Proposals;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Infrastructure.Proposals.FpPro;

public sealed partial class FpProReportParser : IFpProReportParser
{
    private const string PngContentType = "image/png";

    public async Task<FpProReportPreviewData> ParseAsync(
        FpProReportFile file,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await file.Content.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            throw new InvalidDataException("El archivo no es un PDF valido.");
        }

        var pages = ReadPages(bytes);
        var fullText = string.Join('\n', pages.Select(page => page.Text));
        if (!fullText.Contains("Lista Item", StringComparison.Ordinal)
            || !fullText.Contains("Pedido", StringComparison.Ordinal))
        {
            throw new InvalidDataException("El PDF no parece ser un reporte FP Pro.");
        }

        var tokens = ExtractTextTokens(ExtractPdfText(bytes)).ToArray();
        var orderId = FirstGroup(fullText, @"Pedido\s*:?(S&G\d+)")
            ?? ValueAfter(tokens, "Pedido");
        var description = FirstGroup(fullText, @"Descripción\s*([^\n]+?)\s+Cliente")
            ?? ExtractHeaderValue(fullText, "Descripción", @"[^\)]+")
            ?? ExtractHeaderValue(fullText, "Descripci", @"[^\)]+")
            ?? ValueAfter(tokens, "Descripción")
            ?? ValueAfter(tokens, "Descripci");
        var revision = TryParseInt(FirstGroup(fullText, @"Revisión\s*:?(\d+)")
            ?? ExtractHeaderValue(fullText, "Revisión", @"\d+")
            ?? ExtractHeaderValue(fullText, "Revisi", @"\d+")
            ?? ValueAfter(tokens, "Revisión")
            ?? ValueAfter(tokens, "Revisi"));
        var aluminumWastePercent = FirstDecimal(fullText, @"Retal\s+\S*til\s*\(\s*([0-9]+(?:[,.][0-9]+)?)\s*%\s*\)");

        var weights = ParseStructureWeights(pages);
        var items = ParseDetailedItems(pages, weights).ToArray();
        if (items.Length == 0)
        {
            items = ParseListItems(tokens).ToArray();
        }

        return new FpProReportPreviewData(
            new FpProReportData(orderId, description, revision, items.Length, aluminumWastePercent),
            items,
            items.SelectMany(item => item.PendingFields)
                .Concat(aluminumWastePercent is null ? ["aluminumWastePercent"] : [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    internal static decimal ParseLatinDecimal(string value)
    {
        var normalized = value.Trim().Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return decimal.Parse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    internal static (int Width, int Height)? ParseDimension(string value)
    {
        var match = DimensionRegex().Match(value.Trim());
        return match.Success
            ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }

    internal static IReadOnlyList<FpProGlassPaneData> ParseGlass(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return GlassRegex().Matches(value)
            .Select(match => new FpProGlassPaneData(
                match.Groups[1].Value.Trim(),
                TryThickness(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static IReadOnlyList<FpProPreviewItemData> ParseDetailedItems(
        IReadOnlyList<PdfPageData> pages,
        IReadOnlyDictionary<string, decimal> structureWeights)
    {
        var result = new List<FpProPreviewItemData>();
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var header = DetailHeaderRegex().Match(page.Text);
            if (!header.Success)
            {
                continue;
            }

            var itemNumber = header.Groups[1].Value.PadLeft(2, '0');
            var detailText = BuildDetailSection(pages, index, itemNumber);
            var totalText = BuildTotalSection(pages, index, itemNumber);
            var quantity = TryParseInt(FirstGroup(detailText, @"Número de estructuras(\d+)"));
            var dimension = FirstGroup(detailText, @"Dimension(\d{2,5}x\d{2,5})");
            var parsedDimension = dimension is null ? null : ParseDimension(dimension);
            var widthMm = parsedDimension?.Width;
            var heightMm = parsedDimension?.Height;
            decimal? widthM = widthMm is null ? null : widthMm.Value / 1000m;
            decimal? heightM = heightMm is null ? null : heightMm.Value / 1000m;
            decimal? nominalArea = widthM is null || heightM is null || quantity is null
                ? null
                : Math.Round(widthM.Value * heightM.Value * quantity.Value, 4, MidpointRounding.AwayFromZero);
            var divisor = quantity is > 1 ? quantity.Value : 1;
            var rawAluminumBase = FirstDecimal(detailText, @"Tot\. Perfiles\s*([0-9.]+,[0-9]+)");
            var accessories = SectionTotal(detailText, "Accesorios Marca", "Acc. ml Marca", "Guarniciones Marca", "Vidrios Código");
            var accessoryMl = SectionTotal(detailText, "Acc. ml Marca", "Guarniciones Marca", "Vidrios Código");
            var gaskets = SectionTotal(detailText, "Guarniciones Marca", "Vidrios Código");
            var rawAccessoriesBase = SumNullable(accessories, accessoryMl, gaskets);
            var rawWeight = structureWeights.GetValueOrDefault(itemNumber);
            var glass = ParseGlass(FirstGroup(detailText, @"Vidrios(.*?)Coste unitario"));
            var pending = PendingFields();
            var technicalProfileDescriptions = ParseTechnicalProfileDescriptions(detailText);
            var profileCodes = SplitProfiles(FirstGroup(detailText, @"Perfiles(.*?)Dimension"));
            var technicalProfiles = ParseTechnicalProfiles(detailText, divisor);
            if (technicalProfiles.Count == 0)
            {
                technicalProfiles = profileCodes
                    .Select(value => new FpProTechnicalProfile(value, value, null, null, null))
                    .ToArray();
            }

            result.Add(new FpProPreviewItemData(
                itemNumber,
                FirstGroup(detailText, @"Tipología\s*(.*?)Número de estructuras"),
                profileCodes,
                technicalProfileDescriptions,
                technicalProfiles,
                widthMm,
                heightMm,
                widthM,
                heightM,
                quantity,
                nominalArea,
                FirstGroup(detailText, @"Notas:\s*(.*?)\s*Perfiles ml")
                    ?? FirstGroup(totalText, @"Notas:\s*(.*?)Costo materiales"),
                glass,
                glass.Count == 0 ? null : glass.Max(value => value.ThicknessMm),
                null,
                null,
                null,
                null,
                null,
                Divide(rawAluminumBase, divisor),
                Divide(rawAluminumBase, divisor),
                Divide(rawAccessoriesBase, divisor),
                Divide(rawAccessoriesBase, divisor),
                rawWeight == 0 ? null : NormalizeStructureWeight(rawWeight, divisor),
                rawWeight == 0 ? null : NormalizeStructureWeight(rawWeight, divisor),
                page.ItemImage,
                pending,
                page.ItemImage is null
                    ? [new FpProPreviewWarningData("image_not_found", "image", "No se encontro una imagen embebida para el item.")]
                    : []));
        }

        return result
            .GroupBy(item => item.ItemNumber, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => int.Parse(item.ItemNumber, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static string BuildDetailSection(IReadOnlyList<PdfPageData> pages, int startIndex, string itemNumber)
    {
        var builder = new StringBuilder(pages[startIndex].Text);
        for (var i = startIndex + 1; i < pages.Count; i++)
        {
            var text = pages[i].Text;
            if (TotalHeaderRegex(itemNumber).IsMatch(text) || DetailHeaderAnyRegex().IsMatch(text))
            {
                break;
            }

            builder.Append(' ').Append(text);
        }

        return builder.ToString();
    }

    private static string BuildTotalSection(IReadOnlyList<PdfPageData> pages, int detailIndex, string itemNumber)
    {
        var builder = new StringBuilder();
        for (var i = detailIndex + 1; i < pages.Count; i++)
        {
            var text = pages[i].Text;
            if (!TotalHeaderRegex(itemNumber).IsMatch(text))
            {
                continue;
            }

            builder.Append(text);
            for (var j = i + 1; j < pages.Count; j++)
            {
                var continuation = pages[j].Text;
                if (DetailHeaderAnyRegex().IsMatch(continuation)
                    || ValuedProfilesRegex().IsMatch(continuation))
                {
                    break;
                }

                builder.Append(' ').Append(continuation);
            }

            break;
        }

        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, decimal> ParseStructureWeights(IReadOnlyList<PdfPageData> pages)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var index = 0; index < pages.Count; index++)
        {
            var text = pages[index].Text;
            if (!ValuedProfilesRegex().IsMatch(text))
            {
                continue;
            }

            var section = BuildValuedProfilesSection(pages, index);
            var profilesSection = TrimValuedProfilesSection(section);
            var structure = FirstGroup(section, @"Estructura:\s*(\d+)\s*-\s*Etiqueta:\s*\d+");
            if (structure is null)
            {
                continue;
            }

            var weight = ParseValuedProfilesTotalWeight(profilesSection);
            if (weight is not null)
            {
                result[structure.PadLeft(2, '0')] = weight.Value;
            }
        }

        return result;
    }

    private static string BuildValuedProfilesSection(IReadOnlyList<PdfPageData> pages, int startIndex)
    {
        var builder = new StringBuilder(pages[startIndex].Text);
        for (var index = startIndex + 1; index < pages.Count; index++)
        {
            var text = pages[index].Text;
            if (ValuedProfilesRegex().IsMatch(text)
                || DetailHeaderAnyRegex().IsMatch(text))
            {
                break;
            }

            builder.Append(' ').Append(text);
            if (ParseValuedProfilesTotalWeight(TrimValuedProfilesSection(builder.ToString())) is not null)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string TrimValuedProfilesSection(string section)
    {
        var profilesIndex = ValuedProfilesRegex().Match(section);
        var profilesSection = profilesIndex.Success ? section[profilesIndex.Index..] : section;
        var glassIndex = ValuedGlassRegex().Match(profilesSection);
        return glassIndex.Success ? profilesSection[..glassIndex.Index] : profilesSection;
    }

    private static decimal? ParseValuedProfilesTotalWeight(string section)
    {
        var match = ValuedProfilesTotalWeightRegex().Match(section);
        return match.Success ? ParseLatinDecimal(match.Groups["weight"].Value) : null;
    }

    private static decimal NormalizeStructureWeight(decimal rawWeight, int divisor)
    {
        var value = rawWeight;
        if (divisor > 1 && rawWeight > 10m)
        {
            value = rawWeight / divisor;
        }

        return Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<PdfPageData> ReadPages(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        return document.GetPages()
            .Select(page => new PdfPageData(
                page.Number,
                page.Text,
                ExtractItemImage(page)))
            .ToArray();
    }

    private static FpProPreviewImageData? ExtractItemImage(Page page)
    {
        var image = page.GetImages()
            .Where(value => value.Bounds.Width >= 80 && value.Bounds.Height >= 80 && value.Bounds.Bottom < page.Height - 150)
            .OrderByDescending(value => value.Bounds.Width * value.Bounds.Height)
            .FirstOrDefault();
        if (image is null)
        {
            return null;
        }

        if (!image.TryGetPng(out var png) || png.Length == 0)
        {
            return null;
        }

        return new FpProPreviewImageData(PngContentType, Convert.ToBase64String(FlipPngVertically(png)));
    }

    private static byte[] FlipPngVertically(byte[] png)
    {
        if (png.Length < 33
            || png[0] != 137
            || png[1] != 80
            || png[2] != 78
            || png[3] != 71
            || png[4] != 13
            || png[5] != 10
            || png[6] != 26
            || png[7] != 10)
        {
            return png;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        var bitDepth = png[24];
        var colorType = png[25];
        var interlace = png[28];
        if (bitDepth != 8 || interlace != 0)
        {
            return png;
        }

        var bytesPerPixel = colorType switch
        {
            2 => 3,
            6 => 4,
            _ => 0
        };
        if (bytesPerPixel == 0)
        {
            return png;
        }

        using var idat = new MemoryStream();
        var chunks = ReadPngChunks(png).ToArray();
        foreach (var chunk in chunks.Where(value => value.Type == "IDAT"))
        {
            idat.Write(chunk.Data);
        }

        byte[] decompressed;
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        using (var raw = new MemoryStream())
        {
            zlib.CopyTo(raw);
            decompressed = raw.ToArray();
        }

        var stride = width * bytesPerPixel;
        if (decompressed.Length < (stride + 1) * height)
        {
            return png;
        }

        var rows = UnfilterPngRows(decompressed, width, height, bytesPerPixel);
        Array.Reverse(rows);

        using var filtered = new MemoryStream();
        foreach (var row in rows)
        {
            filtered.WriteByte(0);
            filtered.Write(row);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            filtered.Position = 0;
            filtered.CopyTo(zlib);
        }

        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, 8));
        var wroteImageData = false;
        foreach (var chunk in chunks)
        {
            if (chunk.Type == "IDAT")
            {
                if (!wroteImageData)
                {
                    WritePngChunk(output, "IDAT", compressed.ToArray());
                    wroteImageData = true;
                }

                continue;
            }

            WritePngChunk(output, chunk.Type, chunk.Data);
        }

        return output.ToArray();
    }

    private static byte[][] UnfilterPngRows(byte[] decompressed, int width, int height, int bytesPerPixel)
    {
        var stride = width * bytesPerPixel;
        var rows = new byte[height][];
        var previous = new byte[stride];
        for (var rowIndex = 0; rowIndex < height; rowIndex++)
        {
            var offset = rowIndex * (stride + 1);
            var filter = decompressed[offset];
            var row = decompressed.AsSpan(offset + 1, stride).ToArray();
            for (var index = 0; index < stride; index++)
            {
                var left = index >= bytesPerPixel ? row[index - bytesPerPixel] : 0;
                var up = previous[index];
                var upLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : 0;
                row[index] = filter switch
                {
                    0 => row[index],
                    1 => unchecked((byte)(row[index] + left)),
                    2 => unchecked((byte)(row[index] + up)),
                    3 => unchecked((byte)(row[index] + ((left + up) / 2))),
                    4 => unchecked((byte)(row[index] + PaethPredictor(left, up, upLeft))),
                    _ => row[index]
                };
            }

            rows[rowIndex] = row;
            previous = row;
        }

        return rows;
    }

    private static int PaethPredictor(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upLeftDistance = Math.Abs(estimate - upLeft);
        if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
        {
            return left;
        }

        return upDistance <= upLeftDistance ? up : upLeft;
    }

    private static IEnumerable<PngChunk> ReadPngChunks(byte[] png)
    {
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (length < 0 || offset + 12 + length > png.Length)
            {
                yield break;
            }

            yield return new PngChunk(type, png.AsSpan(offset + 8, length).ToArray());
            offset += 12 + length;
            if (type == "IEND")
            {
                yield break;
            }
        }
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput.AsSpan(typeBytes.Length));
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static IReadOnlyList<FpProPreviewItemData> ParseListItems(IReadOnlyList<string> tokens)
    {
        var result = new List<FpProPreviewItemData>();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!string.Equals(tokens[index], "Tipo", StringComparison.Ordinal)
                || index + 8 >= tokens.Count)
            {
                continue;
            }

            var itemNumber = tokens[index + 1].PadLeft(2, '0');
            if (!int.TryParse(itemNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var dimension = FindFollowingValue(tokens, index, "Dimension", 8);
            var quantity = TryParseInt(FindFollowingValue(tokens, index, "Número de", 10));
            var profilesRaw = FindFollowingValue(tokens, index, "Perfiles", 12);
            var parsedDimension = dimension is null ? null : ParseDimension(dimension);
            var widthMm = parsedDimension?.Width;
            var heightMm = parsedDimension?.Height;
            decimal? widthM = widthMm is null ? null : widthMm.Value / 1000m;
            decimal? heightM = heightMm is null ? null : heightMm.Value / 1000m;
            decimal? nominalArea = widthM is null || heightM is null || quantity is null
                ? null
                : Math.Round(widthM.Value * heightM.Value * quantity.Value, 4, MidpointRounding.AwayFromZero);
            var glass = ParseGlass(FindFollowingValue(tokens, index, "Vidrios", 40));
            result.Add(new FpProPreviewItemData(
                itemNumber,
                null,
                SplitProfiles(profilesRaw),
                [],
                SplitProfiles(profilesRaw)
                    .Select(value => new FpProTechnicalProfile(value, value, null, null, null))
                    .ToArray(),
                widthMm,
                heightMm,
                widthM,
                heightM,
                quantity,
                nominalArea,
                null,
                glass,
                glass.Count == 0 ? null : glass.Max(value => value.ThicknessMm),
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
                PendingFields(),
                []));
        }

        return result
            .GroupBy(item => item.ItemNumber, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.ItemNumber, StringComparer.Ordinal)
            .ToArray();
    }

    private static decimal? SumNullable(params decimal?[] values)
    {
        var present = values.Where(value => value is not null).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Sum();
    }

    private static decimal? Divide(decimal? value, int divisor) =>
        value is null ? null : Math.Round(value.Value / divisor, 4, MidpointRounding.AwayFromZero);

    private static string[] PendingFields() =>
    [
        "system",
        "glassDescription",
        "finish",
        "lock",
        "glassPrice"
    ];

    private static IReadOnlyList<string> SplitProfiles(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<string> ParseTechnicalProfileDescriptions(string content)
    {
        var indicators = new[]
        {
            "MARCO VENTANA PROYECTANTE",
            "NAVE HORIZ/VERT",
            "PISAVIDRIO PUERTA BATIENTE",
            "NAVE2295",
            "NAVE CAMARA EUROPEA"
        };

        return indicators
            .Where(indicator => content.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IReadOnlyList<FpProTechnicalProfile> ParseTechnicalProfiles(string content, int divisor)
    {
        var profilesSection = FirstGroup(content, @"Perfiles ml(.*?)Accesorios Marca")
            ?? FirstGroup(content, @"Perfiles ml(.*?)Vidrios Código");
        if (string.IsNullOrWhiteSpace(profilesSection))
        {
            return [];
        }

        var profiles = new List<FpProTechnicalProfile>();
        foreach (Match match in TechnicalProfileRegex().Matches(profilesSection))
        {
            var code = match.Groups["code"].Value.Trim();
            var description = NormalizeSpaces(match.Groups["description"].Value);
            var quantity = ParseLatinDecimal(match.Groups["quantity"].Value);
            var totalLength = ParseLatinDecimal(match.Groups["length"].Value);
            profiles.Add(new FpProTechnicalProfile(
                code,
                description,
                totalLength,
                divisor > 1 ? Math.Round(totalLength / divisor, 4, MidpointRounding.AwayFromZero) : totalLength,
                quantity));
        }

        return profiles;
    }

    private static string NormalizeSpaces(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", " ", RegexOptions.CultureInvariant);
    }

    private static string? FirstGroup(string content, string pattern)
    {
        var match = Regex.Match(content, pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static decimal? FirstDecimal(string content, string pattern)
    {
        var value = FirstGroup(content, pattern);
        return value is null ? null : ParseLatinDecimal(value);
    }

    private static decimal? SectionTotal(string content, string sectionName, params string[] followingSections)
    {
        var sectionIndex = content.IndexOf(sectionName, StringComparison.Ordinal);
        if (sectionIndex < 0)
        {
            return null;
        }

        var endIndex = followingSections
            .Select(section => content.IndexOf(section, sectionIndex + sectionName.Length, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(content.Length)
            .Min();
        if (endIndex <= sectionIndex)
        {
            return null;
        }

        var section = content[sectionIndex..endIndex];
        var matches = LatinDecimalRegex().Matches(section);
        return matches.Count == 0
            ? null
            : ParseLatinDecimal(matches[^1].Value);
    }

    private static string ExtractPdfText(byte[] bytes)
    {
        var raw = Encoding.Latin1.GetString(bytes);
        var builder = new StringBuilder();
        foreach (Match match in StreamRegex().Matches(raw))
        {
            var streamBytes = Encoding.Latin1.GetBytes(match.Groups[1].Value.Trim('\r', '\n'));
            try
            {
                using var input = new MemoryStream(streamBytes);
                using var deflate = new ZLibStream(input, CompressionMode.Decompress);
                using var reader = new StreamReader(deflate, Encoding.Latin1);
                builder.AppendLine(reader.ReadToEnd());
            }
            catch (InvalidDataException)
            {
                builder.AppendLine(match.Groups[1].Value);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> ExtractTextTokens(string content)
    {
        foreach (Match match in TextTokenRegex().Matches(content))
        {
            var value = UnescapePdfString(match.Groups[1].Value).Trim();
            if (value.Length > 0)
            {
                yield return value;
            }
        }
    }

    private static string? FindFollowingValue(
        IReadOnlyList<string> tokens,
        int start,
        string label,
        int maxDistance)
    {
        for (var i = start; i < Math.Min(tokens.Count - 1, start + maxDistance); i++)
        {
            if (tokens[i].StartsWith(label, StringComparison.Ordinal))
            {
                return tokens[i + 1];
            }
        }

        return null;
    }

    private static string? ValueAfter(IReadOnlyList<string> tokens, string label)
    {
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].StartsWith(label, StringComparison.Ordinal))
            {
                return tokens[i + 1].Trim();
            }
        }

        return null;
    }

    private static string? ExtractHeaderValue(string content, string label, string valuePattern)
    {
        var match = Regex.Match(
            content,
            $@"\({Regex.Escape(label)}[^)]*\)\s*Tj.*?\(({valuePattern})\)\s*Tj",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? UnescapePdfString(match.Groups[1].Value).Trim() : null;
    }

    private static int? TryParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static decimal? TryThickness(string value)
    {
        var match = ThicknessRegex().Match(value);
        return match.Success
            ? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;
    }

    private static string UnescapePdfString(string value) =>
        value.Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private sealed record PdfPageData(
        int Number,
        string Text,
        FpProPreviewImageData? ItemImage);

    private sealed record PngChunk(
        string Type,
        byte[] Data);

    [GeneratedRegex("Costos por Item\\s+(\\d{1,2})\\s+-\\s+Piezas", RegexOptions.IgnoreCase)]
    private static partial Regex DetailHeaderRegex();

    [GeneratedRegex("Costos por Item\\s+\\d{1,2}\\s+-\\s+Piezas", RegexOptions.IgnoreCase)]
    private static partial Regex DetailHeaderAnyRegex();

    private static Regex TotalHeaderRegex(string itemNumber) =>
        new($@"Costos Total por Item\s+{int.Parse(itemNumber, CultureInfo.InvariantCulture)}\s+-\s+Piezas", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex("Lista valorizada - Perfiles - Piezas", RegexOptions.IgnoreCase)]
    private static partial Regex ValuedProfilesRegex();

    [GeneratedRegex("Lista valorizada - Vidrios", RegexOptions.IgnoreCase)]
    private static partial Regex ValuedGlassRegex();

    [GeneratedRegex(@"\bTotal\s*(?<weight>[0-9]+,[0-9]{1,3})(?:[0-9.]*,[0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValuedProfilesTotalWeightRegex();

    [GeneratedRegex("stream\\r?\\n(.*?)\\r?\\nendstream", RegexOptions.Singleline)]
    private static partial Regex StreamRegex();

    [GeneratedRegex("\\(((?:\\\\.|[^\\\\)])*)\\)\\s*Tj", RegexOptions.Singleline)]
    private static partial Regex TextTokenRegex();

    [GeneratedRegex("^(\\d{2,5})x(\\d{2,5})$", RegexOptions.IgnoreCase)]
    private static partial Regex DimensionRegex();

    [GeneratedRegex("([A-Z0-9]+MM)\\s*\\((\\d+)\\s*x\\s*(\\d+)\\)\\s*x\\s*(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GlassRegex();

    [GeneratedRegex("(\\d+(?:[.,]\\d+)?)\\s*MM", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessRegex();

    [GeneratedRegex("[0-9.]+,[0-9]+")]
    private static partial Regex LatinDecimalRegex();

    [GeneratedRegex(
        @"(?<code>[A-Z0-9._-]{2,})\s+(?<description>.*?)(?<quantity>\d+(?:[.,]\d+)?)\s+(?<length>\d+(?:[.,]\d+)?)\s+(?:[0-9.]+,[0-9]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TechnicalProfileRegex();
}

