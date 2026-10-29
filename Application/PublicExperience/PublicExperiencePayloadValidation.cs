using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.PublicExperience;

public static class PublicExperiencePayloadValidation
{
    public const int MaximumPayloadBytes = 256 * 1024;
    public const long MaximumFileBytes = 20L * 1024 * 1024;
    public const long MaximumTotalBytes = 100L * 1024 * 1024;
    public const int MaximumFiles = 5;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    public static PublicExperiencePayload Parse(string json, IReadOnlyList<PublicExperienceUpload> files)
    {
        Require(Encoding.UTF8.GetByteCount(json) <= MaximumPayloadBytes, "payload supera 256 KiB.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicateProperties(document.RootElement);
        var payload = JsonSerializer.Deserialize<PublicExperiencePayload>(json, Options);
        Require(payload is not null, "payload es obligatorio.");
        Required(payload!.SubmissionId, 100, "submissionId");
        Require(payload.SchemaVersion == "sng-public-experience-v1", "schemaVersion no admitido.");
        Require(payload.Source == "SNG_WEB", "source no admitido.");
        Require(payload.Client is not null && payload.Project is not null, "client y project son obligatorios.");
        Required(payload.Client!.FullName, 200, "client.fullName");
        Required(payload.Client.Whatsapp, 50, "client.whatsapp");
        Required(payload.Client.AdvisorCode, 100, "client.advisorCode");
        Required(payload.Client.AdvisorDisplayName, 200, "client.advisorDisplayName");
        Required(payload.Project!.Name, 200, "project.name");
        Required(payload.Project.City, 250, "project.city");
        Optional(payload.Project.Subdivision, 250, "project.subdivision");
        Optional(payload.Project.LotOrApartment, 250, "project.lotOrApartment");
        Optional(payload.Project.Notes, 1000, "project.notes");
        Require(payload.Answers.ValueKind == JsonValueKind.Object, "answers debe ser un objeto.");
        foreach (var answer in payload.Answers.EnumerateObject())
        {
            Require(answer.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                || (answer.Value.ValueKind == JsonValueKind.Array
                    && answer.Value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)),
                "answers solo admite string, number o string[].");
        }
        Require(payload.Files is not null, "files es obligatorio.");
        Require(payload.Files!.Count == files.Count, "files no coincide con multipart.");
        Require(files.Count <= MaximumFiles, "Maximo 5 archivos.");
        long total = 0;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var metadata = payload.Files[index];
            Require(metadata is not null && metadata.FileName == file.FileName
                && metadata.ContentType == file.ContentType && metadata.SizeBytes == file.SizeBytes,
                "Metadata de files no coincide con multipart en nombre, MIME, orden o tamano.");
            ValidateFileName(file.FileName);
            var mime = Path.GetExtension(file.FileName).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => null
            };
            Require(mime is not null && file.ContentType == mime, "Extension o MIME no admitido.");
            Require(file.SizeBytes > 0 && file.SizeBytes <= MaximumFileBytes, "Archivo vacio o mayor a 20 MiB.");
            total += file.SizeBytes;
            Require(total <= MaximumTotalBytes, "Archivos superan 100 MiB.");
        }
        return payload;
    }

    public static void ValidateFileName(string fileName)
    {
        Required(fileName, 255, "fileName");
        Require(fileName is not "." and not ".."
            && !fileName.Any(c => char.IsControl(c) || "/\\:<>|\"?*".Contains(c)),
            "fileName no admite rutas ni caracteres invalidos.");
    }

    public static byte[] Canonicalize(PublicExperiencePayload payload)
    {
        var element = JsonSerializer.SerializeToElement(payload, Options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, element);
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray()) WriteCanonical(writer, value);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), "Propiedad JSON duplicada.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicateProperties(value);
    }

    private static void Required(string? value, int max, string field) =>
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max,
            $"{field} es obligatorio y admite maximo {max} caracteres.");

    private static void Optional(string? value, int max, string field) =>
        Require(value is null || value.Length <= max, $"{field} admite maximo {max} caracteres.");

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new ArgumentException(error);
    }
}
