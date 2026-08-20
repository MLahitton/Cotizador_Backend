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
        IReadOnlyList<PersistedConflict> conflicts,
        IReadOnlyList<PersistedGlass> glasses,
        IReadOnlyList<PersistedTechnicalClassification>? technicalClassifications = null)
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
                conflicts,
                glasses,
                technicalClassifications ?? []);
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
        IReadOnlyList<PersistedConflict> conflicts,
        IReadOnlyList<PersistedGlass> glasses,
        IReadOnlyList<PersistedTechnicalClassification> technicalClassifications)
    {
        var payload = JsonSerializer.Deserialize<Payload>(persisted.PayloadJson, Options)
            ?? throw Invalid();
        var structured = payload.StructuredExtraction ?? throw Invalid();

        ValidateHeader(expectedDocumentId, persisted, payload, structured);

        ValidateItemsRequiringReview(
            persisted.ExtractionId,
            payload.SchemaVersion,
            structured.Summary.ItemsRequiringReview,
            persisted.ItemsRequiringReview,
            items,
            technicalClassifications);

        var project = new StructuredProjectReadModel(
            structured.Project.Name,
            structured.Project.ClientName,
            structured.Project.Location,
            ValidatePages(structured.Project.SourcePages, persisted.PageCount),
            MapEvidence(structured.Project.Evidence, persisted.PageCount));
        var payloadRequirements = FlattenRequirements(structured.Requirements);

        ValidateCollectionCounts(
            persisted.ExtractionId,
            items.Count,
            structured.Items.Count,
            requirements.Count,
            payloadRequirements.Length,
            references.Count,
            structured.DocumentReferences.Count,
            issues.Count,
            structured.Issues.Count,
            conflicts.Count,
            structured.Conflicts.Count);

        if (payload.SchemaVersion == "2.0" && glasses.Count != 0
            || payload.SchemaVersion == "3.0" && glasses.Count != items.Count)
            throw Invalid(
                "glass",
                persisted.ExtractionId,
                null,
                "count",
                payload.SchemaVersion == "3.0" ? items.Count : 0,
                glasses.Count);
        var glassByItem = glasses.ToDictionary(value => value.ItemSequence);
        var technicalByItem = technicalClassifications.ToDictionary(
            value => value.ItemSequence);
        var mappedItems = items.Select((row, index) =>
            MapItem(persisted.ExtractionId, row, structured.Items[index],
                persisted.PageCount,
                glassByItem.GetValueOrDefault(row.Sequence),
                technicalByItem.GetValueOrDefault(row.Sequence),
                payload.SchemaVersion)).ToArray();
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
                conflicts.Count,
                persisted.IdentifiedGlassItemCount,
                persisted.GlassItemsRequiringReview),
            new StructuredProcessingMetadataReadModel(
                persisted.ProcessingMethod,
                persisted.DurationMs),
            persisted.CreatedAtUtc);
    }

    private static StructuredItemReadModel MapItem(
        Guid structuredExtractionId,
        PersistedItem row,
        ItemDto dto,
        int pageCount,
        PersistedGlass? glass,
        PersistedTechnicalClassification? technical,
        string schemaVersion)
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
            || dto.AreaSquareMeters != row.AreaSquareMeters
            || dto.Configuration != row.Configuration
            || dto.FunctionalType != row.FunctionalType
            || dto.Operation != row.Operation
            || dto.PanelCount != row.PanelCount
            || dto.MovablePanelCount != row.MovablePanelCount
            || dto.FixedPanelCount != row.FixedPanelCount
            || dto.Modulation != row.Modulation
            || dto.OpeningDirection != row.OpeningDirection
            || !((dto.SpecialFeatures ?? []).SequenceEqual(row.SpecialFeatures ?? []))
            || dto.GeometryType != row.GeometryType
            || !ItemRequiresReviewMatches(
                schemaVersion,
                dto.RequiresReview,
                row.RequiresReview,
                technical))
        {
            throw Invalid(
                "items",
                structuredExtractionId,
                row.Sequence,
                "item",
                dto,
                row);
        }

        return new StructuredItemReadModel(
            row.Sequence, row.Reference, row.Description, row.ElementType,
            row.RawMeasurements, row.WidthMillimeters, row.HeightMillimeters,
            row.Quantity, row.RequiresReview, reasons,
            ValidatePages(dto.SourcePages, pageCount),
            MapEvidence(dto.Evidence, pageCount),
            schemaVersion == "2.0"
                ? dto.Glass is null && glass is null
                    ? null
                    : throw Invalid()
                : MapGlass(dto.Glass, glass, pageCount),
            null,
            MapTechnicalClassification(
                structuredExtractionId,
                dto.TechnicalClassification,
                technical,
                schemaVersion),
            row.AreaSquareMeters,
            row.Configuration,
            row.FunctionalType,
            row.Operation,
            row.PanelCount,
            row.MovablePanelCount,
            row.FixedPanelCount,
            row.Modulation,
            row.OpeningDirection,
            row.SpecialFeatures ?? [],
            row.GeometryType);
    }

    private static StructuredItemTechnicalClassificationReadModel?
        MapTechnicalClassification(
            Guid structuredExtractionId,
            TechnicalClassificationDto? dto,
            PersistedTechnicalClassification? persisted,
            string schemaVersion)
    {
        if (dto is null)
        {
            return persisted is null ? null : ToReadModel(persisted);
        }

        if (persisted is null)
        {
            throw Invalid();
        }

        var systemCode = NormalizeTechnicalCode(dto.SystemCode);
        var systemOriginalText = NormalizeTechnicalText(dto.SystemOriginalText);
        var systemSource = MapTechnicalSource(dto.SystemSource);
        var frameCode = NormalizeTechnicalCode(dto.FrameCode);
        var frameOriginalText = NormalizeTechnicalText(dto.FrameOriginalText);
        var frameSource = MapTechnicalSource(dto.FrameSource);
        var finishCode = NormalizeTechnicalCode(dto.FinishCode);
        var finishOriginalText = NormalizeTechnicalText(dto.FinishOriginalText);
        var finishSource = MapTechnicalSource(dto.FinishSource);
        var reasons = dto.ReviewReasons ?? throw Invalid();

        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "systemCode",
            systemCode,
            persisted.SystemCode);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "systemOriginalText",
            systemOriginalText,
            persisted.SystemOriginalText);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "systemSource",
            systemSource,
            persisted.SystemSource);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "systemConfidence",
            dto.SystemConfidence,
            persisted.SystemConfidence);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "frameCode",
            frameCode,
            persisted.FrameCode);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "frameOriginalText",
            frameOriginalText,
            persisted.FrameOriginalText);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "frameSource",
            frameSource,
            persisted.FrameSource);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "frameConfidence",
            dto.FrameConfidence,
            persisted.FrameConfidence);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "finishCode",
            finishCode,
            persisted.FinishCode);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "finishOriginalText",
            finishOriginalText,
            persisted.FinishOriginalText);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "finishSource",
            finishSource,
            persisted.FinishSource);
        ValidateTechnicalField(
            structuredExtractionId,
            persisted.ItemSequence,
            "finishConfidence",
            dto.FinishConfidence,
            persisted.FinishConfidence);

        var normalizedReasons = NormalizeTechnicalReviewReasons(reasons);
        if (normalizedReasons is null
            || !TechnicalReviewMatches(
                schemaVersion,
                dto.RequiresReview,
                normalizedReasons,
                persisted.RequiresReview,
                persisted.ReviewReasons))
        {
            throw Invalid(
                "technicalClassification",
                structuredExtractionId,
                persisted.ItemSequence,
                "technicalClassification.reviewReasons",
                normalizedReasons ?? reasons,
                persisted.ReviewReasons);
        }

        return ToReadModel(persisted);
    }

    private static StructuredItemTechnicalClassificationReadModel ToReadModel(
        PersistedTechnicalClassification persisted) =>
        new(
            persisted.SystemCode,
            persisted.SystemOriginalText,
            persisted.SystemSource,
            persisted.SystemConfidence,
            persisted.FrameCode,
            persisted.FrameOriginalText,
            persisted.FrameSource,
            persisted.FrameConfidence,
            persisted.FinishCode,
            persisted.FinishOriginalText,
            persisted.FinishSource,
            persisted.FinishConfidence,
            persisted.RequiresReview,
            persisted.ReviewReasons);

    private static StructuredItemGlassReadModel MapGlass(
        GlassDto? dto,
        PersistedGlass? persisted,
        int pageCount)
    {
        if (dto is null || persisted is null)
            throw Invalid();
        var scope = dto.AssignmentScope switch
        {
            "ITEM" => GlassAssignmentScope.Item,
            "SECTION" => GlassAssignmentScope.Section,
            "GENERAL" => GlassAssignmentScope.General,
            "UNASSIGNED" => GlassAssignmentScope.Unassigned,
            _ => throw Invalid()
        };
        var reasons = dto.ReviewReasons?.Select(value => value switch
        {
            "GLASS_TYPE_NOT_IDENTIFIED" => GlassReviewReason.GlassTypeNotIdentified,
            "GLASS_TYPE_AMBIGUOUS" => GlassReviewReason.GlassTypeAmbiguous,
            "GLASS_TYPE_CONFLICT" => GlassReviewReason.GlassTypeConflict,
            _ => throw Invalid()
        }).ToArray() ?? throw Invalid();
        var pages = ValidatePages(dto.SourcePages, pageCount);
        var evidence = MapEvidence(dto.Evidence, pageCount);
        if (dto.RawSpecification != persisted.RawSpecification
            || dto.NormalizedCode != persisted.NormalizedCode
            || scope != persisted.AssignmentScope
            || dto.RequiresReview != persisted.RequiresReview
            || !reasons.SequenceEqual(persisted.ReviewReasons)
            || !pages.SequenceEqual(persisted.SourcePages)
            || evidence.Length != persisted.Evidence.Length
            || evidence.Where((value, index) =>
                value.PageNumber != persisted.Evidence[index].PageNumber
                || value.SourceType != persisted.Evidence[index].SourceType
                || value.Text != persisted.Evidence[index].Text
                || value.SheetName != persisted.Evidence[index].SheetName
                || value.CellRange != persisted.Evidence[index].CellRange).Any())
            // TODO(BE-PAYLOAD-EVIDENCE-LOC): PersistedGlassEvidence aún no
            // transporta SheetName/CellRange desde EF legacy; se completa en el
            // próximo paso de configuración de persistencia.
            throw Invalid();
        return new(persisted.GlassTypeId, persisted.RawSpecification,
            persisted.NormalizedCode, persisted.AssignmentScope,
            persisted.RequiresReview, persisted.ReviewReasons,
            persisted.SourcePages, evidence);
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

    private static void ValidateItemsRequiringReview(
        Guid structuredExtractionId,
        string schemaVersion,
        int payloadValue,
        int persistedValue,
        IReadOnlyList<PersistedItem> items,
        IReadOnlyList<PersistedTechnicalClassification> technicalClassifications)
    {
        var itemRowsValue = items.Count(value => value.RequiresReview);
        if (persistedValue != itemRowsValue)
        {
            throw Invalid(
                "summary",
                structuredExtractionId,
                null,
                "itemsRequiringReview",
                itemRowsValue,
                persistedValue);
        }

        if (payloadValue == persistedValue)
        {
            return;
        }

        var technicalReviewValue = technicalClassifications.Count(
            value => value.RequiresReview);
        if (schemaVersion == "3.0"
            && payloadValue < persistedValue
            && persistedValue - payloadValue <= technicalReviewValue)
        {
            return;
        }

        throw Invalid(
            "summary",
            structuredExtractionId,
            null,
            "itemsRequiringReview",
            payloadValue,
            persistedValue);
    }

    private static void ValidateHeader(
        Guid expectedDocumentId,
        AvailableExtractionProjection persisted,
        Payload payload,
        StructuredDto structured)
    {
        if (payload.SchemaVersion != persisted.SchemaVersion)
            throw Invalid("summary", persisted.ExtractionId, null,
                "schemaVersion", payload.SchemaVersion, persisted.SchemaVersion);
        if (payload.SchemaVersion is not ("2.0" or "3.0"))
            throw Invalid("summary", persisted.ExtractionId, null,
                "schemaVersion", payload.SchemaVersion, "2.0|3.0");
        if (expectedDocumentId == Guid.Empty
            || payload.DocumentId == Guid.Empty
            || payload.DocumentId != expectedDocumentId)
            throw Invalid("summary", persisted.ExtractionId, null,
                "documentId", payload.DocumentId, expectedDocumentId);
        if (payload.ProcessingAttemptId == Guid.Empty
            || payload.ProcessingAttemptId != persisted.ProcessingAttemptId)
            throw Invalid("summary", persisted.ExtractionId, null,
                "processingAttemptId",
                payload.ProcessingAttemptId,
                persisted.ProcessingAttemptId);
        if (payload.Document is null)
            throw Invalid("summary", persisted.ExtractionId, null,
                "document", null, "present");
        if (payload.Pages is null)
            throw Invalid("summary", persisted.ExtractionId, null,
                "pages", null, "present");
        if (payload.Warnings is null)
            throw Invalid("summary", persisted.ExtractionId, null,
                "warnings", null, "present");
        if (payload.ProcessingMetadata is null)
            throw Invalid("summary", persisted.ExtractionId, null,
                "processingMetadata", null, "present");
        if (MapStatus(structured.Status) != persisted.Status)
            throw Invalid("summary", persisted.ExtractionId, null,
                "status", structured.Status, persisted.Status);
        if (structured.Project is null)
            throw Invalid("summary", persisted.ExtractionId, null,
                "project", null, "present");
        if (structured.Requirements is null)
            throw Invalid("requirements", persisted.ExtractionId, null,
                "requirements", null, "present");
        if (structured.Items is null)
            throw Invalid("items", persisted.ExtractionId, null,
                "items", null, "present");
        if (structured.DocumentReferences is null)
            throw Invalid("references", persisted.ExtractionId, null,
                "documentReferences", null, "present");
        if (structured.Issues is null)
            throw Invalid("issues", persisted.ExtractionId, null,
                "issues", null, "present");
        if (structured.Conflicts is null)
            throw Invalid("conflicts", persisted.ExtractionId, null,
                "conflicts", null, "present");
        if (structured.Summary is null)
            throw Invalid("summary", persisted.ExtractionId, null,
                "summary", null, "present");
        if (structured.ProcessingMetadata is null)
            throw Invalid("summary", persisted.ExtractionId, null,
                "structuredProcessingMetadata", null, "present");
        ValidateHeaderField(persisted.ExtractionId, "projectName",
            structured.Project.Name, persisted.ProjectName);
        ValidateHeaderField(persisted.ExtractionId, "clientName",
            structured.Project.ClientName, persisted.ClientName);
        ValidateHeaderField(persisted.ExtractionId, "location",
            structured.Project.Location, persisted.Location);
        ValidateHeaderField(persisted.ExtractionId, "itemCount",
            structured.Summary.ItemCount, persisted.ItemCount);
        ValidateHeaderField(persisted.ExtractionId, "documentReferenceCount",
            structured.Summary.DocumentReferenceCount,
            persisted.DocumentReferenceCount);
        ValidateHeaderField(persisted.ExtractionId, "knownQuoteableUnitCount",
            structured.Summary.KnownQuoteableUnitCount,
            persisted.KnownQuoteableUnitCount);
        ValidateHeaderField(persisted.ExtractionId, "identifiedGlassItemCount",
            structured.Summary.IdentifiedGlassItemCount,
            persisted.IdentifiedGlassItemCount);
        ValidateHeaderField(persisted.ExtractionId, "glassItemsRequiringReview",
            structured.Summary.GlassItemsRequiringReview,
            persisted.GlassItemsRequiringReview);
        ValidateHeaderField(persisted.ExtractionId, "processingMethod",
            structured.ProcessingMetadata.Method, persisted.ProcessingMethod);
        ValidateHeaderField(persisted.ExtractionId, "durationMs",
            structured.ProcessingMetadata.DurationMs, persisted.DurationMs);
    }

    private static void ValidateHeaderField<T>(
        Guid structuredExtractionId,
        string fieldName,
        T payloadValue,
        T persistedValue)
    {
        if (!EqualityComparer<T>.Default.Equals(payloadValue, persistedValue))
            throw Invalid("summary", structuredExtractionId, null, fieldName,
                payloadValue, persistedValue);
    }

    private static void ValidateCollectionCounts(
        Guid structuredExtractionId,
        int persistedItems,
        int payloadItems,
        int persistedRequirements,
        int payloadRequirements,
        int persistedReferences,
        int payloadReferences,
        int persistedIssues,
        int payloadIssues,
        int persistedConflicts,
        int payloadConflicts)
    {
        ValidateCollectionCount(structuredExtractionId, "items",
            payloadItems, persistedItems);
        ValidateCollectionCount(structuredExtractionId, "requirements",
            payloadRequirements, persistedRequirements);
        ValidateCollectionCount(structuredExtractionId, "references",
            payloadReferences, persistedReferences);
        ValidateCollectionCount(structuredExtractionId, "issues",
            payloadIssues, persistedIssues);
        ValidateCollectionCount(structuredExtractionId, "conflicts",
            payloadConflicts, persistedConflicts);
    }

    private static void ValidateCollectionCount(
        Guid structuredExtractionId,
        string stage,
        int payloadValue,
        int persistedValue)
    {
        if (payloadValue != persistedValue)
            throw Invalid(stage, structuredExtractionId, null, "count",
                payloadValue, persistedValue);
    }

    private static bool ItemRequiresReviewMatches(
        string schemaVersion,
        bool payloadValue,
        bool persistedValue,
        PersistedTechnicalClassification? technical)
    {
        return payloadValue == persistedValue
            || schemaVersion == "3.0"
            && !payloadValue
            && persistedValue
            && technical?.RequiresReview == true;
    }

    private static bool TechnicalReviewMatches(
        string schemaVersion,
        bool payloadRequiresReview,
        string[] payloadReviewReasons,
        bool persistedRequiresReview,
        string[] persistedReviewReasons)
    {
        if (payloadRequiresReview == persistedRequiresReview
            && payloadReviewReasons.SequenceEqual(persistedReviewReasons))
        {
            return true;
        }

        return schemaVersion == "3.0"
            && !payloadRequiresReview
            && persistedRequiresReview
            && payloadReviewReasons.Length == 0
            && persistedReviewReasons.Length > 0;
    }

    private static void ValidateTechnicalField<T>(
        Guid structuredExtractionId,
        int itemSequence,
        string fieldName,
        T payloadValue,
        T persistedValue)
    {
        if (!EqualityComparer<T>.Default.Equals(payloadValue, persistedValue))
            throw Invalid(
                "technicalClassification",
                structuredExtractionId,
                itemSequence,
                fieldName,
                payloadValue,
                persistedValue);
    }

    private static string? NormalizeTechnicalCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var code = value.Trim().ToUpperInvariant();
        return code.Length <= 30
            && code.All(character =>
                character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9'
                || character is '_' or '-')
            ? code
            : null;
    }

    private static string? NormalizeTechnicalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return text.Length <= 500 ? text : null;
    }

    private static string[]? NormalizeTechnicalReviewReasons(string[] reasons)
    {
        if (reasons.Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        return reasons
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
            if (value.Text is null || value.Text.Length > 500)
            {
                throw Invalid();
            }

            var sourceType = value.SourceType switch
            {
                "NATIVE" => EvidenceSourceType.Native,
                "OCR" => EvidenceSourceType.Ocr,
                "XLSX" => EvidenceSourceType.Xlsx,
                _ => throw Invalid()
            };

            var sheetName = string.IsNullOrWhiteSpace(value.SheetName)
                ? null
                : value.SheetName;
            var cellRange = string.IsNullOrWhiteSpace(value.CellRange)
                ? null
                : value.CellRange;

            if (sourceType is EvidenceSourceType.Native or EvidenceSourceType.Ocr)
            {
                if (value.PageNumber is not { } pageNumber
                    || pageNumber < 1 || pageNumber > pageCount
                    || sheetName is not null || cellRange is not null)
                {
                    throw Invalid();
                }

                return new StructuredEvidenceReadModel(
                    pageNumber, sourceType, value.Text, null, null);
            }

            if (value.PageNumber is not null || sheetName is null || cellRange is null)
            {
                throw Invalid();
            }

            return new StructuredEvidenceReadModel(
                null, sourceType, value.Text, sheetName, cellRange);
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
        "SHOWER_DIVISION" => StructuredElementType.ShowerDivision,
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
            "GLASS_TYPE_NOT_IDENTIFIED" => StructuredIssueCode.GlassTypeNotIdentified,
            "GLASS_TYPE_AMBIGUOUS" => StructuredIssueCode.GlassTypeAmbiguous,
            "GLASS_TYPE_CONFLICT" => StructuredIssueCode.GlassTypeConflict,
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
    private static TechnicalClassificationSource? MapTechnicalSource(
        string? value) => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant() switch
        {
            "EXPLICIT" => TechnicalClassificationSource.Explicit,
            "ALIAS" => TechnicalClassificationSource.Alias,
            "INFERRED" => TechnicalClassificationSource.Inferred,
            "UNRESOLVED" => TechnicalClassificationSource.Unresolved,
            _ => throw Invalid()
        };
    private static InvalidDataException Invalid() =>
        new("El payload estructurado persistido no es coherente.");

    private static InvalidDataException Invalid(
        string stage,
        Guid structuredExtractionId,
        int? itemSequence,
        string fieldName,
        object? payloadValue,
        object? persistedValue) =>
        new(
            "El payload estructurado persistido no es coherente. "
            + $"Stage={stage}; "
            + $"StructuredExtractionId={structuredExtractionId}; "
            + $"ItemSequence={itemSequence?.ToString() ?? "null"}; "
            + $"FieldName={fieldName}; "
            + $"PayloadValue={FormatDiagnosticValue(payloadValue)}; "
            + $"PersistedValue={FormatDiagnosticValue(persistedValue)}.");

    private static string FormatDiagnosticValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => text,
            Array array => string.Join(",", array.Cast<object?>()),
            _ => value.ToString() ?? "null"
        };
    }

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
    private sealed class GlassDto
    {
        public string? RawSpecification { get; init; }
        public string? NormalizedCode { get; init; }
        public string? AssignmentScope { get; init; }
        public bool RequiresReview { get; init; }
        public string[]? ReviewReasons { get; init; }
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
        public decimal? AreaSquareMeters { get; init; }
        public string? Configuration { get; init; }
        public string? FunctionalType { get; init; }
        public string? Operation { get; init; }
        public int? PanelCount { get; init; }
        public int? MovablePanelCount { get; init; }
        public int? FixedPanelCount { get; init; }
        public string? Modulation { get; init; }
        public string? OpeningDirection { get; init; }
        public string[]? SpecialFeatures { get; init; }
        public string? GeometryType { get; init; }
        public bool RequiresReview { get; init; }
        public string[]? ReviewReasons { get; init; }
        public int[]? SourcePages { get; init; }
        public EvidenceDto[]? Evidence { get; init; }
        public GlassDto? Glass { get; init; }
        public TechnicalClassificationDto? TechnicalClassification { get; init; }
    }
    private sealed class TechnicalClassificationDto
    {
        public string? SystemCode { get; init; }
        public string? SystemOriginalText { get; init; }
        public string? SystemSource { get; init; }
        public decimal? SystemConfidence { get; init; }
        public string? FrameCode { get; init; }
        public string? FrameOriginalText { get; init; }
        public string? FrameSource { get; init; }
        public decimal? FrameConfidence { get; init; }
        public string? FinishCode { get; init; }
        public string? FinishOriginalText { get; init; }
        public string? FinishSource { get; init; }
        public decimal? FinishConfidence { get; init; }
        public bool RequiresReview { get; init; }
        public string[]? ReviewReasons { get; init; }
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
        public int? PageNumber { get; init; }
        public string? SourceType { get; init; }
        public string? Text { get; init; }
        public string? SheetName { get; init; }
        public string? CellRange { get; init; }
    }
    private sealed class SummaryDto
    {
        public int ItemCount { get; init; }
        public int DocumentReferenceCount { get; init; }
        public int ItemsRequiringReview { get; init; }
        public int KnownQuoteableUnitCount { get; init; }
        public int? IdentifiedGlassItemCount { get; init; }
        public int? GlassItemsRequiringReview { get; init; }
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
