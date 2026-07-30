using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;

namespace Infrastructure.Persistence.Repositories;

internal static class StructuredExtractionPayloadReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static StructuredExtractionDetailsReadModel Read(
        Guid expectedDocumentId,
        AvailableExtractionProjection persisted,
        Guid? latestAttemptId,
        IReadOnlyList<PersistedItem> items,
        IReadOnlyList<PersistedRequirement> requirements,
        IReadOnlyList<PersistedReference> references,
        IReadOnlyList<PersistedIssue> issues,
        IReadOnlyList<PersistedConflict> conflicts)
    {
        try
        {
            return ReadCore(
                expectedDocumentId,
                persisted,
                latestAttemptId,
                items,
                requirements,
                references,
                issues,
                conflicts);
        }
        catch (PreQuoteDocumentQueryException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            throw new PreQuoteDocumentQueryException(exception);
        }
    }

    private static StructuredExtractionDetailsReadModel ReadCore(
        Guid expectedDocumentId,
        AvailableExtractionProjection persisted,
        Guid? latestAttemptId,
        IReadOnlyList<PersistedItem> items,
        IReadOnlyList<PersistedRequirement> requirements,
        IReadOnlyList<PersistedReference> references,
        IReadOnlyList<PersistedIssue> issues,
        IReadOnlyList<PersistedConflict> conflicts)
    {
        var payload = JsonSerializer.Deserialize<Payload>(persisted.PayloadJson, Options)
            ?? throw Invalid();
        var structured = payload.StructuredExtraction ?? throw Invalid();

        if (payload.SchemaVersion != "2.0"
            || expectedDocumentId == Guid.Empty
            || payload.DocumentId == Guid.Empty
            || payload.DocumentId != expectedDocumentId
            || payload.ProcessingAttemptId == Guid.Empty
            || payload.ProcessingAttemptId != persisted.ProcessingAttemptId
            || payload.Document is null
            || payload.Pages is null
            || payload.Warnings is null
            || payload.ProcessingMetadata is null
            || MapStatus(structured.Status) != persisted.Status
            || structured.Project is null
            || structured.Requirements is null
            || structured.Items is null
            || structured.DocumentReferences is null
            || structured.Issues is null
            || structured.Conflicts is null
            || structured.Summary is null
            || structured.ProcessingMetadata is null
            || structured.Project.Name != persisted.ProjectName
            || structured.Project.ClientName != persisted.ClientName
            || structured.Project.Location != persisted.Location
            || structured.Summary.ItemCount != persisted.ItemCount
            || structured.Summary.DocumentReferenceCount
                != persisted.DocumentReferenceCount
            || structured.Summary.ItemsRequiringReview
                != persisted.ItemsRequiringReview
            || structured.Summary.KnownQuoteableUnitCount
                != persisted.KnownQuoteableUnitCount
            || structured.ProcessingMetadata.Method != persisted.ProcessingMethod
            || structured.ProcessingMetadata.DurationMs != persisted.DurationMs)
        {
            throw Invalid();
        }

        var project = new StructuredProjectReadModel(
            structured.Project.Name,
            structured.Project.ClientName,
            structured.Project.Location,
            ValidatePages(structured.Project.SourcePages, persisted.PageCount),
            MapEvidence(structured.Project.Evidence, persisted.PageCount));
        var payloadRequirements = FlattenRequirements(structured.Requirements);

        if (items.Count != structured.Items.Count
            || requirements.Count != payloadRequirements.Length
            || references.Count != structured.DocumentReferences.Count
            || issues.Count != structured.Issues.Count
            || conflicts.Count != structured.Conflicts.Count)
        {
            throw Invalid();
        }

        var mappedItems = items.Select((row, index) =>
            MapItem(row, structured.Items[index], persisted.PageCount)).ToArray();
        var mappedRequirements = requirements.Select((row, index) =>
            MapRequirement(row, payloadRequirements[index], persisted.PageCount))
            .ToArray();
        var mappedReferences = references.Select((row, index) =>
            MapReference(
                row,
                structured.DocumentReferences[index],
                persisted.PageCount)).ToArray();
        var mappedIssues = issues.Select((row, index) =>
            MapIssue(row, structured.Issues[index], persisted.PageCount)).ToArray();
        var mappedConflicts = conflicts.Select((row, index) =>
            MapConflict(row, structured.Conflicts[index], persisted.PageCount))
            .ToArray();

        return new StructuredExtractionDetailsReadModel(
            persisted.ExtractionId,
            persisted.ProcessingAttemptId,
            latestAttemptId == persisted.ProcessingAttemptId,
            persisted.Status,
            project,
            mappedRequirements,
            mappedItems,
            mappedReferences,
            mappedIssues,
            mappedConflicts,
            new StructuredSummaryReadModel(
                persisted.ItemCount,
                persisted.DocumentReferenceCount,
                persisted.ItemsRequiringReview,
                persisted.KnownQuoteableUnitCount,
                issues.Count,
                conflicts.Count),
            new StructuredProcessingMetadataReadModel(
                persisted.ProcessingMethod,
                persisted.DurationMs),
            persisted.CreatedAtUtc);
    }

    private static StructuredItemReadModel MapItem(
        PersistedItem row,
        ItemDto dto,
        int pageCount)
    {
        var reasons = dto.ReviewReasons?.Select(MapIssueCode).ToArray()
            ?? throw Invalid();

        if (dto.Sequence != row.Sequence
            || dto.Reference != row.Reference
            || dto.Description != row.Description
            || MapElementType(dto.ElementType) != row.ElementType
            || dto.RawMeasurements != row.RawMeasurements
            || dto.WidthMillimeters != row.WidthMillimeters
            || dto.HeightMillimeters != row.HeightMillimeters
            || dto.Quantity != row.Quantity
            || dto.RequiresReview != row.RequiresReview)
        {
            throw Invalid();
        }

        return new StructuredItemReadModel(
            row.Sequence, row.Reference, row.Description, row.ElementType,
            row.RawMeasurements, row.WidthMillimeters, row.HeightMillimeters,
            row.Quantity, row.RequiresReview, reasons,
            ValidatePages(dto.SourcePages, pageCount),
            MapEvidence(dto.Evidence, pageCount));
    }

    private static StructuredRequirementReadModel MapRequirement(
        PersistedRequirement row,
        RequirementPayload payload,
        int pageCount)
    {
        if (row.Category != payload.Category || row.Value != payload.Value)
        {
            throw Invalid();
        }

        return new StructuredRequirementReadModel(
            row.Sequence, row.Category, row.Value,
            MapEvidence(payload.Evidence, pageCount));
    }

    private static StructuredDocumentReferenceReadModel MapReference(
        PersistedReference row,
        ReferenceDto dto,
        int pageCount)
    {
        if (dto.Sequence != row.Sequence
            || dto.Reference != row.Reference
            || dto.Description != row.Description
            || dto.Detail != row.Detail
            || dto.Quantity != row.Quantity)
        {
            throw Invalid();
        }

        return new StructuredDocumentReferenceReadModel(
            row.Sequence, row.Reference, row.Description, row.Detail,
            row.Quantity, ValidatePages(dto.SourcePages, pageCount),
            MapEvidence(dto.Evidence, pageCount));
    }

    private static StructuredIssueReadModel MapIssue(
        PersistedIssue row,
        IssueDto dto,
        int pageCount)
    {
        var code = MapIssueCode(dto.Code);
        var pages = ValidatePages(dto.PageNumbers, pageCount);

        if (code != row.Code
            || dto.Message != row.Message
            || dto.ItemSequence != row.ItemSequence
            || !pages.SequenceEqual(row.PageNumbers))
        {
            throw Invalid();
        }

        return new StructuredIssueReadModel(
            row.Sequence, row.Code, row.Message, row.ItemSequence, pages);
    }

    private static StructuredConflictReadModel MapConflict(
        PersistedConflict row,
        ConflictDto dto,
        int pageCount)
    {
        var code = MapConflictCode(dto.Code);
        var pages = ValidatePages(dto.PageNumbers, pageCount);
        var itemSequences = ValidatePositive(dto.ItemSequences);

        if (code != row.Code
            || dto.Message != row.Message
            || !pages.SequenceEqual(row.PageNumbers)
            || !itemSequences.SequenceEqual(row.ItemSequences))
        {
            throw Invalid();
        }

        return new StructuredConflictReadModel(
            row.Sequence, row.Code, row.Message, itemSequences, pages);
    }

    private static RequirementPayload[] FlattenRequirements(
        RequirementsDto requirements)
    {
        if (requirements.GlassSpecifications is null
            || requirements.ProfileSpecifications is null
            || requirements.Finishes is null
            || requirements.AccessoriesAndSealants is null
            || requirements.GeneralNotes is null)
        {
            throw Invalid();
        }

        return requirements.GlassSpecifications
            .Select(value => new RequirementPayload(
                RequirementCategory.GlassSpecification,
                value.Value,
                value.Evidence))
            .Concat(requirements.ProfileSpecifications.Select(value =>
                new RequirementPayload(
                    RequirementCategory.ProfileSpecification,
                    value.Value,
                    value.Evidence)))
            .Concat(requirements.Finishes.Select(value =>
                new RequirementPayload(
                    RequirementCategory.Finish,
                    value.Value,
                    value.Evidence)))
            .Concat(requirements.AccessoriesAndSealants.Select(value =>
                new RequirementPayload(
                    RequirementCategory.AccessoriesAndSealants,
                    value.Value,
                    value.Evidence)))
            .Concat(requirements.GeneralNotes.Select(value =>
                new RequirementPayload(
                    RequirementCategory.GeneralNote,
                    value.Value,
                    value.Evidence)))
            .ToArray();
    }

    private static int[] ValidatePages(int[]? values, int pageCount)
    {
        if (values is null
            || values.Any(value => value < 1 || value > pageCount)
            || !values.SequenceEqual(values.Distinct().Order()))
        {
            throw Invalid();
        }

        return values;
    }

    private static int[] ValidatePositive(int[]? values)
    {
        if (values is null
            || values.Any(value => value < 1)
            || !values.SequenceEqual(values.Distinct().Order()))
        {
            throw Invalid();
        }

        return values;
    }

    private static StructuredEvidenceReadModel[] MapEvidence(
        EvidenceDto[]? evidence,
        int pageCount)
    {
        if (evidence is null)
        {
            throw Invalid();
        }

        return evidence.Select(value =>
        {
            if (value.PageNumber < 1
                || value.PageNumber > pageCount
                || value.Text is null)
            {
                throw Invalid();
            }

            return new StructuredEvidenceReadModel(
                value.PageNumber,
                value.SourceType switch
                {
                    "NATIVE" => EvidenceSourceType.Native,
                    "OCR" => EvidenceSourceType.Ocr,
                    _ => throw Invalid()
                },
                value.Text);
        }).ToArray();
    }

    private static StructuredExtractionStatus MapStatus(string? value) =>
        value switch
        {
            "COMPLETED" => StructuredExtractionStatus.Completed,
            "REQUIRES_REVIEW" => StructuredExtractionStatus.RequiresReview,
            _ => throw Invalid()
        };
    private static StructuredElementType MapElementType(string? value) =>
        value switch
        {
            "WINDOW" => StructuredElementType.Window,
            "DOOR" => StructuredElementType.Door,
            "FACADE" => StructuredElementType.Facade,
            "PARTITION" => StructuredElementType.Partition,
            "RAILING" => StructuredElementType.Railing,
            "SKYLIGHT" => StructuredElementType.Skylight,
            "OTHER" => StructuredElementType.Other,
            _ => throw Invalid()
        };
    private static StructuredIssueCode MapIssueCode(string? value) =>
        value switch
        {
            "PROJECT_NAME_NOT_FOUND" => StructuredIssueCode.ProjectNameNotFound,
            "NO_QUOTEABLE_ITEMS_FOUND" => StructuredIssueCode.NoQuoteableItemsFound,
            "INCOMPLETE_TABLE_ROW" => StructuredIssueCode.IncompleteTableRow,
            "MISSING_ITEM_REFERENCE" => StructuredIssueCode.MissingItemReference,
            "MISSING_OR_INVALID_MEASUREMENTS" => StructuredIssueCode.MissingOrInvalidMeasurements,
            "MISSING_OR_INVALID_QUANTITY" => StructuredIssueCode.MissingOrInvalidQuantity,
            "UNKNOWN_ELEMENT_TYPE" => StructuredIssueCode.UnknownElementType,
            "OCR_REVIEW_REQUIRED" => StructuredIssueCode.OcrReviewRequired,
            _ => throw Invalid()
        };
    private static StructuredConflictCode MapConflictCode(string? value) =>
        value switch
        {
            "CONFLICTING_PROJECT_NAME" => StructuredConflictCode.ConflictingProjectName,
            "CONFLICTING_CLIENT_NAME" => StructuredConflictCode.ConflictingClientName,
            "CONFLICTING_LOCATION" => StructuredConflictCode.ConflictingLocation,
            "DUPLICATE_ITEM_REFERENCE" => StructuredConflictCode.DuplicateItemReference,
            _ => throw Invalid()
        };
    private static InvalidDataException Invalid() =>
        new("El payload estructurado persistido no es coherente.");

    private sealed class Payload
    {
        public string? SchemaVersion { get; init; }
        public Guid DocumentId { get; init; }
        public Guid ProcessingAttemptId { get; init; }
        public string? Status { get; init; }
        public JsonElement? Document { get; init; }
        public JsonElement[]? Pages { get; init; }
        public JsonElement[]? Warnings { get; init; }
        public MetadataDto? ProcessingMetadata { get; init; }
        public StructuredDto? StructuredExtraction { get; init; }
    }
    private sealed class StructuredDto
    {
        public string? Status { get; init; }
        public ProjectDto? Project { get; init; }
        public RequirementsDto? Requirements { get; init; }
        public List<ItemDto>? Items { get; init; }
        public List<ReferenceDto>? DocumentReferences { get; init; }
        public List<IssueDto>? Issues { get; init; }
        public List<ConflictDto>? Conflicts { get; init; }
        public SummaryDto? Summary { get; init; }
        public MetadataDto? ProcessingMetadata { get; init; }
    }
    private sealed class ProjectDto
    {
        public string? Name { get; init; }
        public string? ClientName { get; init; }
        public string? Location { get; init; }
        public int[]? SourcePages { get; init; }
        public EvidenceDto[]? Evidence { get; init; }
    }
    private sealed class RequirementsDto
    {
        public List<RequirementDto>? GlassSpecifications { get; init; }
        public List<RequirementDto>? ProfileSpecifications { get; init; }
        public List<RequirementDto>? Finishes { get; init; }
        public List<RequirementDto>? AccessoriesAndSealants { get; init; }
        public List<RequirementDto>? GeneralNotes { get; init; }
    }
    private sealed class RequirementDto
    {
        public string? Value { get; init; }
        public EvidenceDto[]? Evidence { get; init; }
    }
    private sealed class ItemDto
    {
        public int Sequence { get; init; }
        public string? Reference { get; init; }
        public string? Description { get; init; }
        public string? ElementType { get; init; }
        public string? RawMeasurements { get; init; }
        public int? WidthMillimeters { get; init; }
        public int? HeightMillimeters { get; init; }
        public int? Quantity { get; init; }
        public bool RequiresReview { get; init; }
        public string[]? ReviewReasons { get; init; }
        public int[]? SourcePages { get; init; }
        public EvidenceDto[]? Evidence { get; init; }
    }
    private sealed class ReferenceDto
    {
        public int Sequence { get; init; }
        public string? Reference { get; init; }
        public string? Description { get; init; }
        public string? Detail { get; init; }
        public int? Quantity { get; init; }
        public int[]? SourcePages { get; init; }
        public EvidenceDto[]? Evidence { get; init; }
    }
    private sealed class IssueDto
    {
        public string? Code { get; init; }
        public string? Message { get; init; }
        public int? ItemSequence { get; init; }
        public int[]? PageNumbers { get; init; }
    }
    private sealed class ConflictDto
    {
        public string? Code { get; init; }
        public string? Message { get; init; }
        public int[]? ItemSequences { get; init; }
        public int[]? PageNumbers { get; init; }
    }
    private sealed class EvidenceDto
    {
        public int PageNumber { get; init; }
        public string? SourceType { get; init; }
        public string? Text { get; init; }
    }
    private sealed class SummaryDto
    {
        public int ItemCount { get; init; }
        public int DocumentReferenceCount { get; init; }
        public int ItemsRequiringReview { get; init; }
        public int KnownQuoteableUnitCount { get; init; }
    }
    private sealed class MetadataDto
    {
        public string? Method { get; init; }
        public int DurationMs { get; init; }
    }
    private sealed record RequirementPayload(
        RequirementCategory Category,
        string? Value,
        EvidenceDto[]? Evidence);
}
