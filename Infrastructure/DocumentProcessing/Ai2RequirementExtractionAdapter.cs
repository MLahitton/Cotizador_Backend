using System.Globalization;
using System.Text.Json;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;

namespace Infrastructure.DocumentProcessing;

public sealed class Ai2RequirementExtractionAdapter
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public DocumentProcessingResponseData Adapt(
        string payloadJson,
        DocumentProcessingClientRequest request)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new InvalidDataException();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Cotizador_AI2 devolvio JSON invalido.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Cotizador_AI2 devolvio una raiz {root.ValueKind}; se esperaba Object.");
            }

            var requirement = RequiredObject(root, "requirement");
            var elements = RequiredArray(root, "elements");
            var evidenceValues = RequiredArray(root, "evidence");
            var metadata = RequiredObject(root, "extraction_metadata");

            var evidenceById = evidenceValues.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.Object)
                .Select(value => new
                {
                    Id = String(value, "id"),
                    Value = value
                })
                .Where(value => !string.IsNullOrWhiteSpace(value.Id))
                .ToDictionary(
                    value => value.Id!,
                    value => value.Value,
                    StringComparer.Ordinal);

            var mappedItems = elements.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.Object)
                .Select((value, index) => MapItem(
                    value,
                    index + 1,
                    evidenceById,
                    HasWarningForElement(
                        root,
                        value,
                        "MEASUREMENT_AREA_MISMATCH")))
                .ToArray();

            var warningData = MapWarnings(root, evidenceById);
            var issueData = MapIssues(root, mappedItems, evidenceById);
            var conflictData = MapConflicts(root, mappedItems, evidenceById);
            var requiresReview = Bool(metadata, "partial")
                || !string.Equals(
                    String(metadata, "status") ?? "completed",
                    "completed",
                    StringComparison.OrdinalIgnoreCase)
                || mappedItems.Any(value => value.RequiresReview)
                || warningData.Count > 0
                || conflictData.Count > 0;

            var allXlsx = request.Files.All(file => string.Equals(
                file.ContentType,
                XlsxContentType,
                StringComparison.Ordinal));
            var pageCount = allXlsx
                ? 0
                : Math.Max(1, MapPageCount(root));
            var classification = allXlsx
                ? DocumentClassification.Xlsx
                : DocumentClassification.PdfText;
            var documentMethod = allXlsx ? "openpyxl" : "pymupdf";
            var duration = Int(metadata, "processing_time_ms") ?? 0;
            var schemaVersion = String(metadata, "schema_version") ?? "1.0";

            var projectEvidence = MapEvidence(
                EvidenceIds(requirement),
                evidenceById);
            var projectPages = projectEvidence
                .Where(value => value.PageNumber.HasValue)
                .Select(value => value.PageNumber!.Value)
                .Distinct()
                .Order()
                .ToArray();
            var requirements = MapRequirements(requirement, evidenceById);
            var identifiedGlass = mappedItems.Count(
                item => item.Glass?.NormalizedCode is not null);
            var glassReview = mappedItems.Count(
                item => item.Glass?.RequiresReview == true);

            var structured = new StructuredExtractionData(
                requiresReview
                    ? StructuredExtractionStatus.RequiresReview
                    : StructuredExtractionStatus.Completed,
                TraceableString(requirement, "project_name"),
                TraceableString(requirement, "client_name"),
                TraceableString(requirement, "location"),
                projectPages,
                projectEvidence,
                requirements,
                mappedItems,
                [],
                issueData,
                conflictData,
                mappedItems.Length,
                0,
                mappedItems.Count(value => value.RequiresReview),
                mappedItems.Count(IsKnownQuoteable),
                String(metadata, "pipeline_version") ?? "ai2-v1",
                duration,
                identifiedGlass,
                glassReview);

            var primary = request.PrimaryFile;
            var canonicalPayload = BuildCanonicalPayload(
                request,
                primary,
                structured,
                warningData,
                classification,
                pageCount,
                documentMethod,
                duration,
                requiresReview);
            return new DocumentProcessingResponseData(
                "3.0",
                request.DocumentId,
                request.ProcessingAttemptId,
                requiresReview
                    ? DocumentProcessingOutcome.RequiresReview
                    : DocumentProcessingOutcome.Completed,
                new ProcessedDocumentData(
                    primary.FileName,
                    primary.ContentType,
                    request.Files.Sum(file => file.SizeBytes),
                    pageCount,
                    classification,
                    false),
                [],
                warningData,
                new ProcessingMetadataData(documentMethod, duration),
                canonicalPayload,
                structured,
                DocumentProcessingProvider.Ai2,
                RequiresResolvedGlassCatalog: true,
                SupportsPreliminaryValuation: false);
        }
    }

    private static string BuildCanonicalPayload(
        DocumentProcessingClientRequest request,
        DocumentProcessingFile primary,
        StructuredExtractionData structured,
        IReadOnlyList<ProcessingWarningData> warnings,
        DocumentClassification classification,
        int pageCount,
        string documentMethod,
        int duration,
        bool requiresReview)
    {
        static string EvidenceSource(EvidenceSourceType sourceType) =>
            sourceType switch
            {
                EvidenceSourceType.Native => "NATIVE",
                EvidenceSourceType.Ocr => "OCR",
                EvidenceSourceType.Xlsx => "XLSX",
                _ => throw new InvalidDataException()
            };

        static object Evidence(SourceEvidenceData value) => new
        {
            pageNumber = value.PageNumber,
            sourceType = EvidenceSource(value.SourceType),
            text = value.Text,
            sheetName = value.SheetName,
            cellRange = value.CellRange
        };

        static string TechnicalSource(TechnicalClassificationSource? value) =>
            value switch
            {
                TechnicalClassificationSource.Explicit => "EXPLICIT",
                TechnicalClassificationSource.Inferred => "INFERRED",
                TechnicalClassificationSource.Alias => "ALIAS",
                _ => "UNRESOLVED"
            };

        static string ElementType(StructuredElementType value) => value switch
        {
            StructuredElementType.Window => "WINDOW",
            StructuredElementType.Door => "DOOR",
            StructuredElementType.Facade => "FACADE",
            StructuredElementType.Partition => "PARTITION",
            StructuredElementType.Railing => "RAILING",
            StructuredElementType.Skylight => "SKYLIGHT",
            StructuredElementType.ShowerDivision => "SHOWER_DIVISION",
            _ => "OTHER"
        };

        static string IssueCode(StructuredIssueCode value) => value switch
        {
            StructuredIssueCode.ProjectNameNotFound => "PROJECT_NAME_NOT_FOUND",
            StructuredIssueCode.NoQuoteableItemsFound => "NO_QUOTEABLE_ITEMS_FOUND",
            StructuredIssueCode.IncompleteTableRow => "INCOMPLETE_TABLE_ROW",
            StructuredIssueCode.MissingItemReference => "MISSING_ITEM_REFERENCE",
            StructuredIssueCode.MissingOrInvalidMeasurements => "MISSING_OR_INVALID_MEASUREMENTS",
            StructuredIssueCode.MissingOrInvalidQuantity => "MISSING_OR_INVALID_QUANTITY",
            StructuredIssueCode.UnknownElementType => "UNKNOWN_ELEMENT_TYPE",
            StructuredIssueCode.OcrReviewRequired => "OCR_REVIEW_REQUIRED",
            StructuredIssueCode.GlassTypeNotIdentified => "GLASS_TYPE_NOT_IDENTIFIED",
            StructuredIssueCode.GlassTypeAmbiguous => "GLASS_TYPE_AMBIGUOUS",
            StructuredIssueCode.GlassTypeConflict => "GLASS_TYPE_CONFLICT",
            _ => throw new InvalidDataException()
        };

        static string ConflictCode(StructuredConflictCode value) => value switch
        {
            StructuredConflictCode.ConflictingProjectName => "CONFLICTING_PROJECT_NAME",
            StructuredConflictCode.ConflictingClientName => "CONFLICTING_CLIENT_NAME",
            StructuredConflictCode.ConflictingLocation => "CONFLICTING_LOCATION",
            StructuredConflictCode.DuplicateItemReference => "DUPLICATE_ITEM_REFERENCE",
            _ => throw new InvalidDataException()
        };

        static string GlassScope(GlassAssignmentScope value) => value switch
        {
            GlassAssignmentScope.Item => "ITEM",
            GlassAssignmentScope.Section => "SECTION",
            GlassAssignmentScope.General => "GENERAL",
            _ => "UNASSIGNED"
        };

        static string GlassReason(GlassReviewReason value) => value switch
        {
            GlassReviewReason.GlassTypeNotIdentified => "GLASS_TYPE_NOT_IDENTIFIED",
            GlassReviewReason.GlassTypeAmbiguous => "GLASS_TYPE_AMBIGUOUS",
            GlassReviewReason.GlassTypeConflict => "GLASS_TYPE_CONFLICT",
            _ => throw new InvalidDataException()
        };

        var requirementGroups = structured.Requirements.GroupBy(value => value.Category)
            .ToDictionary(group => group.Key, group => group.Select(value => new
            {
                value = value.Value,
                evidence = value.Evidence.Select(Evidence).ToArray()
            }).ToArray());
        object[] Requirements(RequirementCategory category) =>
            requirementGroups.TryGetValue(category, out var values)
                ? values.Cast<object>().ToArray()
                : [];

        var payload = new
        {
            schemaVersion = "3.0",
            documentId = request.DocumentId,
            processingAttemptId = request.ProcessingAttemptId,
            status = requiresReview ? "REQUIRES_REVIEW" : "COMPLETED",
            document = new
            {
                fileName = primary.FileName,
                contentType = primary.ContentType,
                sizeBytes = request.Files.Sum(file => file.SizeBytes),
                pageCount,
                classification = classification switch
                {
                    DocumentClassification.Xlsx => "XLSX",
                    DocumentClassification.PdfScanned => "PDF_SCANNED",
                    DocumentClassification.PdfMixed => "PDF_MIXED",
                    _ => "PDF_TEXT"
                },
                requiresOcr = false
            },
            pages = Enumerable.Range(1, pageCount).Select(page => new
            {
                pageNumber = page,
                text = string.Empty,
                characterCount = 0,
                hasExtractableText = false
            }).ToArray(),
            warnings = warnings.Select(value => new
            {
                code = value.Code,
                message = value.Message,
                pageNumbers = value.PageNumbers
            }).ToArray(),
            processingMetadata = new { method = documentMethod, durationMs = duration },
            structuredExtraction = new
            {
                status = structured.Status == StructuredExtractionStatus.RequiresReview
                    ? "REQUIRES_REVIEW"
                    : "COMPLETED",
                project = new
                {
                    name = structured.ProjectName,
                    clientName = structured.ClientName,
                    location = structured.Location,
                    sourcePages = structured.ProjectSourcePages,
                    evidence = structured.ProjectEvidence.Select(Evidence).ToArray()
                },
                requirements = new
                {
                    glassSpecifications = Requirements(RequirementCategory.GlassSpecification),
                    profileSpecifications = Requirements(RequirementCategory.ProfileSpecification),
                    finishes = Requirements(RequirementCategory.Finish),
                    accessoriesAndSealants = Requirements(RequirementCategory.AccessoriesAndSealants),
                    generalNotes = Requirements(RequirementCategory.GeneralNote)
                },
                items = structured.Items.Select(item => new
                {
                    sequence = item.Sequence,
                    reference = item.Reference,
                    description = item.Description,
                    elementType = ElementType(item.ElementType),
                    rawMeasurements = item.RawMeasurements,
                    widthMillimeters = item.WidthMillimeters,
                    heightMillimeters = item.HeightMillimeters,
                    quantity = item.Quantity,
                    requiresReview = item.RequiresReview,
                    reviewReasons = item.ReviewReasons.Select(IssueCode).ToArray(),
                    sourcePages = item.SourcePages,
                    evidence = item.Evidence.Select(Evidence).ToArray(),
                    glass = item.Glass is null ? null : new
                    {
                        rawSpecification = item.Glass.RawSpecification,
                        normalizedCode = item.Glass.NormalizedCode,
                        assignmentScope = GlassScope(item.Glass.AssignmentScope),
                        requiresReview = item.Glass.RequiresReview,
                        reviewReasons = item.Glass.ReviewReasons.Select(GlassReason).ToArray(),
                        sourcePages = item.Glass.SourcePages,
                        evidence = item.Glass.Evidence.Select(Evidence).ToArray()
                    },
                    technicalClassification = item.TechnicalClassification is null
                        ? null
                        : new
                        {
                            systemCode = item.TechnicalClassification.SystemCode,
                            systemOriginalText = item.TechnicalClassification.SystemOriginalText,
                            systemSource = TechnicalSource(item.TechnicalClassification.SystemSource),
                            systemConfidence = item.TechnicalClassification.SystemConfidence,
                            frameCode = item.TechnicalClassification.FrameCode,
                            frameOriginalText = item.TechnicalClassification.FrameOriginalText,
                            frameSource = TechnicalSource(item.TechnicalClassification.FrameSource),
                            frameConfidence = item.TechnicalClassification.FrameConfidence,
                            finishCode = item.TechnicalClassification.FinishCode,
                            finishOriginalText = item.TechnicalClassification.FinishOriginalText,
                            finishSource = TechnicalSource(item.TechnicalClassification.FinishSource),
                            finishConfidence = item.TechnicalClassification.FinishConfidence,
                            requiresReview = item.TechnicalClassification.RequiresReview,
                            reviewReasons = item.TechnicalClassification.ReviewReasons
                        }
                }).ToArray(),
                documentReferences = System.Array.Empty<object>(),
                issues = structured.Issues.Select(issue => new
                {
                    code = IssueCode(issue.Code),
                    message = issue.Message,
                    itemSequence = issue.ItemSequence,
                    pageNumbers = issue.PageNumbers
                }).ToArray(),
                conflicts = structured.Conflicts.Select(conflict => new
                {
                    code = ConflictCode(conflict.Code),
                    message = conflict.Message,
                    itemSequences = conflict.ItemSequences,
                    pageNumbers = conflict.PageNumbers
                }).ToArray(),
                summary = new
                {
                    itemCount = structured.ItemCount,
                    documentReferenceCount = structured.DocumentReferenceCount,
                    itemsRequiringReview = structured.ItemsRequiringReview,
                    knownQuoteableUnitCount = structured.KnownQuoteableUnitCount,
                    identifiedGlassItemCount = structured.IdentifiedGlassItemCount,
                    glassItemsRequiringReview = structured.GlassItemsRequiringReview
                },
                processingMetadata = new
                {
                    method = structured.ProcessingMethod,
                    durationMs = structured.DurationMs
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static StructuredItemData MapItem(
        JsonElement element,
        int sequence,
        IReadOnlyDictionary<string, JsonElement> evidenceById,
        bool hasMeasurementAreaMismatch)
    {
        var reference = TraceableString(element, "reference");
        var description = FirstNonEmpty(
            TraceableString(element, "name"),
            String(element, "description"),
            reference,
            NormalizedString(element, "category"));
        if (description is null)
        {
            description = "Elemento sin descripcion";
        }

        var measurements = Array(element, "measurements");
        var width = MeasurementMillimeters(measurements, "width");
        var height = MeasurementMillimeters(measurements, "height");
        var area = MeasurementArea(measurements);
        var quantity = TraceableInt(element, "quantity");
        var category = NormalizedOrRawString(element, "category");
        var itemEvidence = MapEvidence(EvidenceIds(element), evidenceById);
        var glass = MapGlass(element, evidenceById);
        var technical = MapTechnicalClassification(element);
        var status = ItemStatus(element);
        var missingFields = StringArray(element, "missing_fields");
        var reviewReasons = new List<StructuredIssueCode>();
        if (width is null || height is null)
        {
            reviewReasons.Add(
                StructuredIssueCode.MissingOrInvalidMeasurements);
        }
        if (hasMeasurementAreaMismatch)
        {
            reviewReasons.Add(
                StructuredIssueCode.MissingOrInvalidMeasurements);
        }
        if (quantity is null)
        {
            reviewReasons.Add(
                StructuredIssueCode.MissingOrInvalidQuantity);
        }
        if (MapElementType(category) == StructuredElementType.Other
            && string.IsNullOrWhiteSpace(category))
        {
            reviewReasons.Add(StructuredIssueCode.UnknownElementType);
        }
        if (glass?.RequiresReview == true)
        {
            reviewReasons.Add(glass.NormalizedCode is null
                ? StructuredIssueCode.GlassTypeNotIdentified
                : StructuredIssueCode.GlassTypeAmbiguous);
        }

        var requiresReview = status is not CanonicalExtractionValueStatus.Explicit
            || missingFields.Count > 0
            || reviewReasons.Count > 0
            || technical?.RequiresReview == true;
        var sourcePages = itemEvidence
            .Where(value => value.PageNumber.HasValue)
            .Select(value => value.PageNumber!.Value)
            .Distinct()
            .Order()
            .ToArray();

        return new StructuredItemData(
            sequence,
            reference,
            Limit(description, 500),
            MapElementType(category),
            BuildRawMeasurements(width, height, area),
            width,
            height,
            quantity,
            requiresReview,
            reviewReasons.Distinct().ToArray(),
            sourcePages,
            itemEvidence,
            glass,
            technical,
            area,
            ConfigurationText(element),
            Decimal(element, "confidence"),
            status);
    }

    private static StructuredItemGlassData? MapGlass(
        JsonElement element,
        IReadOnlyDictionary<string, JsonElement> evidenceById)
    {
        var values = Array(element, "glass");
        if (values.GetArrayLength() == 0)
        {
            return null;
        }

        var glass = values[0];
        if (glass.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = Object(glass, "type");
        var status = Status(glass);
        var normalized = type is { } typeValue
            ? FirstNonEmpty(
                Normalized(typeValue),
                IsUsable(Status(typeValue), status)
                    ? Raw(typeValue)
                    : null)
            : null;
        var raw = type is { } rawType
            ? Raw(rawType)
            : null;
        raw ??= String(glass, "notes");
        raw = BuildGlassSpecification(raw, glass);
        var evidence = MapEvidence(EvidenceIds(glass), evidenceById);
        var requiresReview = status is
            CanonicalExtractionValueStatus.Ambiguous
            or CanonicalExtractionValueStatus.Unknown
            || string.IsNullOrWhiteSpace(normalized);
        var reasons = requiresReview
            ? new[] { string.IsNullOrWhiteSpace(normalized)
                ? GlassReviewReason.GlassTypeNotIdentified
                : GlassReviewReason.GlassTypeAmbiguous }
            : [];

        return new StructuredItemGlassData(
            raw,
            normalized,
            GlassAssignmentScope.Item,
            requiresReview,
            reasons,
            evidence.Where(value => value.PageNumber.HasValue)
                .Select(value => value.PageNumber!.Value)
                .Distinct().Order().ToArray(),
            evidence);
    }

    private static StructuredItemTechnicalClassificationData?
        MapTechnicalClassification(JsonElement element)
    {
        var profiles = Array(element, "profiles");
        JsonElement? profile = profiles.GetArrayLength() > 0
            && profiles[0].ValueKind == JsonValueKind.Object
                ? profiles[0]
                : null;
        var finish = Object(element, "finish");
        if (profile is null && finish is null)
        {
            return null;
        }

        var systemCode = profile is { } profileValue
            ? TraceableString(profileValue, "code")
                ?? TraceableString(profileValue, "name")
            : null;
        var systemRaw = profile is { } rawProfile
            ? String(rawProfile, "raw_description")
            : null;
        var finishCode = finish is { } finishValue
            ? TraceableString(finishValue, "code")
                ?? String(finishValue, "normalized_type")
                ?? NormalizedString(finishValue, "color")
            : null;
        var finishRaw = finish is { } rawFinish
            ? String(rawFinish, "raw_description")
            : null;
        var systemStatus = profile is { } statusProfile
            ? Status(statusProfile)
            : CanonicalExtractionValueStatus.Unknown;
        var finishStatus = finish is { } statusFinish
            ? Status(statusFinish)
            : CanonicalExtractionValueStatus.Unknown;
        if (string.IsNullOrWhiteSpace(finishCode)
            && IsUsable(finishStatus))
        {
            finishCode = finishRaw;
        }
        var requiresReview = systemStatus is
                CanonicalExtractionValueStatus.Ambiguous
                or CanonicalExtractionValueStatus.Unknown
            || finishStatus is CanonicalExtractionValueStatus.Ambiguous;

        return new StructuredItemTechnicalClassificationData(
            systemCode,
            systemRaw,
            MapTechnicalSource(systemStatus),
            profile is { } confidenceProfile
                ? Decimal(confidenceProfile, "confidence")
                : null,
            null,
            null,
            null,
            null,
            finishCode,
            finishRaw,
            MapTechnicalSource(finishStatus),
            finish is { } confidenceFinish
                ? Decimal(confidenceFinish, "confidence")
                : null,
            requiresReview,
            requiresReview ? ["AI2_REQUIRES_REVIEW"] : []);
    }

    private static IReadOnlyList<StructuredRequirementData> MapRequirements(
        JsonElement requirement,
        IReadOnlyDictionary<string, JsonElement> evidenceById)
    {
        var evidence = MapEvidence(EvidenceIds(requirement), evidenceById);
        return StringArray(requirement, "general_technical_notes")
            .Select(value => new StructuredRequirementData(
                RequirementCategory.GeneralNote,
                Limit(value, 1000),
                evidence))
            .ToArray();
    }

    private static IReadOnlyList<ProcessingWarningData> MapWarnings(
        JsonElement root,
        IReadOnlyDictionary<string, JsonElement> evidenceById) =>
        Array(root, "warnings").EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new ProcessingWarningData(
                Limit(String(value, "code") ?? "AI2_WARNING", 100),
                Limit(String(value, "message") ?? "Cotizador_AI2 reporto una advertencia.", 500),
                MapEvidence(EvidenceIds(value), evidenceById)
                    .Where(item => item.PageNumber.HasValue)
                    .Select(item => item.PageNumber!.Value)
                    .Distinct().Order().ToArray()))
            .ToArray();

    private static IReadOnlyList<StructuredIssueData> MapIssues(
        JsonElement root,
        IReadOnlyList<StructuredItemData> items,
        IReadOnlyDictionary<string, JsonElement> evidenceById)
    {
        var result = new List<StructuredIssueData>();
        foreach (var item in items)
        {
            foreach (var reason in item.ReviewReasons)
            {
                result.Add(new StructuredIssueData(
                    result.Count + 1,
                    reason,
                    "Cotizador_AI2 requiere revision del campo extraido.",
                    item.Sequence,
                    item.SourcePages));
            }
        }

        foreach (var warning in Array(root, "warnings").EnumerateArray())
        {
            if (warning.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var code = String(warning, "code") ?? string.Empty;
            var mappedCode = code.Contains("MEASUREMENT", StringComparison.OrdinalIgnoreCase)
                ? StructuredIssueCode.MissingOrInvalidMeasurements
                : code.Contains("GLASS", StringComparison.OrdinalIgnoreCase)
                    ? StructuredIssueCode.GlassTypeAmbiguous
                    : StructuredIssueCode.UnknownElementType;
            result.Add(new StructuredIssueData(
                result.Count + 1,
                mappedCode,
                Limit(String(warning, "message") ?? code, 500),
                null,
                MapEvidence(EvidenceIds(warning), evidenceById)
                    .Where(value => value.PageNumber.HasValue)
                    .Select(value => value.PageNumber!.Value)
                    .Distinct().Order().ToArray()));
        }

        return result;
    }

    private static IReadOnlyList<StructuredConflictData> MapConflicts(
        JsonElement root,
        IReadOnlyList<StructuredItemData> items,
        IReadOnlyDictionary<string, JsonElement> evidenceById)
    {
        var referenceToSequences = items
            .Where(item => item.Reference is not null)
            .GroupBy(item => item.Reference!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Sequence).ToArray(),
                StringComparer.Ordinal);
        var result = new List<StructuredConflictData>();
        foreach (var conflict in Array(root, "conflicts").EnumerateArray())
        {
            if (conflict.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var sequences = new List<int>();
            foreach (var candidate in Array(conflict, "candidates").EnumerateArray())
            {
                var sourceId = String(candidate, "source_entity_id");
                if (sourceId is not null
                    && referenceToSequences.TryGetValue(
                        sourceId,
                        out var matchedSequences))
                {
                    sequences.AddRange(matchedSequences);
                }
            }

            sequences = sequences.Distinct().Order().ToList();
            if (sequences.Count < 2)
            {
                continue;
            }

            result.Add(new StructuredConflictData(
                result.Count + 1,
                StructuredConflictCode.DuplicateItemReference,
                Limit(String(conflict, "notes")
                    ?? $"Conflicto AI2 en {String(conflict, "field") ?? "campo"}.", 500),
                sequences,
                MapEvidence(EvidenceIds(conflict), evidenceById)
                    .Where(value => value.PageNumber.HasValue)
                    .Select(value => value.PageNumber!.Value)
                    .Distinct().Order().ToArray()));
        }

        return result;
    }

    private static IReadOnlyList<SourceEvidenceData> MapEvidence(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, JsonElement> evidenceById)
    {
        var result = new List<SourceEvidenceData>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (!evidenceById.TryGetValue(id, out var evidence))
            {
                continue;
            }

            var text = String(evidence, "extracted_text")
                ?? String(evidence, "visual_description");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var page = Int(evidence, "page_number");
            var sheet = String(evidence, "sheet_name");
            var cellRange = String(evidence, "cell_range");
            if (page is null && (sheet is null || cellRange is null))
            {
                continue;
            }

            result.Add(new SourceEvidenceData(
                page,
                page.HasValue ? EvidenceSourceType.Native : EvidenceSourceType.Xlsx,
                Limit(text, 500),
                sheet,
                cellRange,
                String(evidence, "source_id"),
                Decimal(evidence, "confidence"),
                Status(evidence)));
        }

        return result;
    }

    private static int MapPageCount(JsonElement root) =>
        Array(root, "sources").EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Sum(value => Int(value, "page_count") ?? 0);

    private static bool IsKnownQuoteable(StructuredItemData item) =>
        item.WidthMillimeters.HasValue
        && item.HeightMillimeters.HasValue
        && item.Quantity.HasValue
        && item.Glass?.NormalizedCode is not null;

    private static StructuredElementType MapElementType(string? category) =>
        category?.Trim().ToUpperInvariant() switch
        {
            "WINDOW" or "VENTANA" or "VENTANAL" or "VENTANAL TIPO" =>
                StructuredElementType.Window,
            "DOOR" or "PUERTA" or "PUERTA VIDRIERA" =>
                StructuredElementType.Door,
            "FACADE" or "FACHADA" => StructuredElementType.Facade,
            "PARTITION" or "DIVISION" => StructuredElementType.Partition,
            "RAILING" or "BARANDA" => StructuredElementType.Railing,
            "SKYLIGHT" or "CLARABOYA" => StructuredElementType.Skylight,
            "SHOWER_DIVISION" or "DIVISION_BANO" =>
                StructuredElementType.ShowerDivision,
            _ => StructuredElementType.Other
        };

    private static TechnicalClassificationSource MapTechnicalSource(
        CanonicalExtractionValueStatus status) => status switch
        {
            CanonicalExtractionValueStatus.Explicit =>
                TechnicalClassificationSource.Explicit,
            CanonicalExtractionValueStatus.Inferred =>
                TechnicalClassificationSource.Inferred,
            _ => TechnicalClassificationSource.Unresolved
        };

    private static CanonicalExtractionValueStatus ItemStatus(JsonElement item)
    {
        var statuses = new[]
        {
            Status(Object(item, "reference")),
            Status(Object(item, "name")),
            Status(Object(item, "category"))
        };
        return statuses.Contains(CanonicalExtractionValueStatus.Ambiguous)
            ? CanonicalExtractionValueStatus.Ambiguous
            : statuses.Contains(CanonicalExtractionValueStatus.Unknown)
                ? CanonicalExtractionValueStatus.Unknown
                : statuses.Contains(CanonicalExtractionValueStatus.Inferred)
                    ? CanonicalExtractionValueStatus.Inferred
                    : CanonicalExtractionValueStatus.Explicit;
    }

    private static CanonicalExtractionValueStatus Status(JsonElement? value) =>
        value is null ? CanonicalExtractionValueStatus.Unknown
        : String(value.Value, "status")?.ToLowerInvariant() switch
        {
            "explicit" => CanonicalExtractionValueStatus.Explicit,
            "inferred" => CanonicalExtractionValueStatus.Inferred,
            "ambiguous" => CanonicalExtractionValueStatus.Ambiguous,
            "not_applicable" => CanonicalExtractionValueStatus.NotApplicable,
            _ => CanonicalExtractionValueStatus.Unknown
        };

    private static int? MeasurementMillimeters(
        JsonElement measurements,
        string type)
    {
        foreach (var measurement in measurements.EnumerateArray())
        {
            if (!string.Equals(
                    String(measurement, "type"),
                    type,
                    StringComparison.OrdinalIgnoreCase)
                || Double(measurement, "value") is not { } value)
            {
                continue;
            }

            var millimeters = (String(measurement, "unit")?.ToLowerInvariant()) switch
            {
                "m" or "meter" or "meters" => value * 1000d,
                "cm" => value * 10d,
                _ => value
            };
            return millimeters is > 0 and <= int.MaxValue
                ? checked((int)Math.Round(millimeters, MidpointRounding.AwayFromZero))
                : null;
        }

        return null;
    }

    private static decimal? MeasurementArea(JsonElement measurements)
    {
        foreach (var measurement in measurements.EnumerateArray())
        {
            if (string.Equals(
                    String(measurement, "type"),
                    "area",
                    StringComparison.OrdinalIgnoreCase)
                && Decimal(measurement, "value") is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static string? BuildRawMeasurements(
        int? width,
        int? height,
        decimal? area)
    {
        if (width.HasValue && height.HasValue)
        {
            return $"{width.Value} x {height.Value} mm";
        }
        return area.HasValue
            ? $"{area.Value.ToString(CultureInfo.InvariantCulture)} m2"
            : null;
    }

    private static string? ConfigurationText(JsonElement element)
    {
        var configuration = Object(element, "configuration");
        return configuration is null
            ? null
            : FirstNonEmpty(
                String(configuration.Value, "raw_description"),
                String(configuration.Value, "normalized_type"),
                String(configuration.Value, "arrangement"));
    }

    private static string? BuildGlassSpecification(
        string? raw,
        JsonElement glass)
    {
        var result = raw?.Trim();
        var thickness = Object(glass, "thickness");
        if (thickness is { } thicknessValue
            && Decimal(thicknessValue, "value") is { } value)
        {
            var unit = String(thicknessValue, "unit") ?? "mm";
            var text = $"{value.ToString(CultureInfo.InvariantCulture)} {unit}";
            if (result is null
                || !result.Contains(
                    value.ToString(CultureInfo.InvariantCulture),
                    StringComparison.OrdinalIgnoreCase))
            {
                result = FirstNonEmpty(result is null ? null : $"{result} {text}", text);
            }
        }

        var composition = FirstNonEmpty(
            String(glass, "composition"),
            Object(glass, "composition") is { } compositionValue
                ? FirstNonEmpty(Normalized(compositionValue), Raw(compositionValue))
                : null);
        if (composition is not null
            && (result is null
                || !result.Contains(composition, StringComparison.OrdinalIgnoreCase)))
        {
            result = result is null ? composition : $"{result} {composition}";
        }

        return result;
    }

    private static bool HasWarningForElement(
        JsonElement root,
        JsonElement element,
        string warningCode)
    {
        var elementId = String(element, "id");
        var reference = TraceableString(element, "reference");
        var elementEvidenceIds = EvidenceIds(element);
        foreach (var warning in Array(root, "warnings").EnumerateArray())
        {
            if (warning.ValueKind != JsonValueKind.Object
                || !string.Equals(
                    String(warning, "code"),
                    warningCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (MatchesElement(warning, elementId, reference)
                || Object(warning, "details") is { } details
                    && MatchesElement(details, elementId, reference)
                || EvidenceIds(warning).Intersect(
                    elementEvidenceIds,
                    StringComparer.Ordinal).Any())
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesElement(
        JsonElement value,
        string? elementId,
        string? reference)
    {
        if (elementId is not null
            && (string.Equals(String(value, "element_id"), elementId, StringComparison.Ordinal)
                || string.Equals(String(value, "entity_id"), elementId, StringComparison.Ordinal)
                || ContainsAny(value, elementId,
                    "element_ids", "related_element_ids", "affected_element_ids")))
        {
            return true;
        }

        return reference is not null
            && (string.Equals(String(value, "reference"), reference, StringComparison.OrdinalIgnoreCase)
                || ContainsAny(value, reference, "references"));
    }

    private static bool ContainsAny(
        JsonElement value,
        string expected,
        params string[] propertyNames) =>
        propertyNames.Any(name => StringArray(value, name).Contains(
            expected,
            StringComparer.OrdinalIgnoreCase));

    private static bool IsUsable(
        CanonicalExtractionValueStatus status,
        CanonicalExtractionValueStatus fallbackStatus =
            CanonicalExtractionValueStatus.Unknown) =>
        status is CanonicalExtractionValueStatus.Explicit
            or CanonicalExtractionValueStatus.Inferred
        || fallbackStatus is CanonicalExtractionValueStatus.Explicit
            or CanonicalExtractionValueStatus.Inferred;

    private static string? TraceableString(JsonElement parent, string name) =>
        Object(parent, name) is { } value ? ScalarString(value, "value") : null;

    private static int? TraceableInt(JsonElement parent, string name)
    {
        var value = Object(parent, name);
        if (value is null
            || !value.Value.TryGetProperty("value", out var scalar))
        {
            return null;
        }
        return scalar.ValueKind == JsonValueKind.Number
            && scalar.TryGetInt32(out var integer)
                ? integer
                : null;
    }

    private static string? NormalizedString(JsonElement parent, string name) =>
        Object(parent, name) is { } value ? Normalized(value) : null;
    private static string? NormalizedOrRawString(JsonElement parent, string name) =>
        Object(parent, name) is { } value
            ? FirstNonEmpty(
                Normalized(value),
                IsUsable(Status(value)) ? Raw(value) : null)
            : null;
    private static string? Normalized(JsonElement value) =>
        ScalarString(value, "normalized") ?? ScalarString(value, "value");
    private static string? Raw(JsonElement value) =>
        ScalarString(value, "raw") ?? ScalarString(value, "value");

    private static IReadOnlyList<string> EvidenceIds(JsonElement value) =>
        StringArray(value, "evidence_ids");

    private static IReadOnlyList<string> StringArray(
        JsonElement value,
        string name) =>
        TryArray(value, name, out var array)
            ? array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray()
            : [];

    private static JsonElement Array(JsonElement value, string name) =>
        TryArray(value, name, out var result)
            ? result
            : JsonDocument.Parse("[]").RootElement.Clone();

    private static JsonElement? Object(JsonElement value, string name) =>
        TryObject(value, name, out var result) ? result : null;

    private static bool TryArray(
        JsonElement value,
        string name,
        out JsonElement result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(name, out result)
            && result.ValueKind == JsonValueKind.Array;
    }

    private static bool TryObject(
        JsonElement value,
        string name,
        out JsonElement result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(name, out result)
            && result.ValueKind == JsonValueKind.Object;
    }

    private static JsonElement RequiredArray(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var result))
        {
            throw new InvalidDataException(
                $"Cotizador_AI2 omitio la propiedad requerida $.{name}.");
        }
        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Cotizador_AI2 devolvio $.{name} como {result.ValueKind}; se esperaba Array.");
        }
        return result;
    }

    private static JsonElement RequiredObject(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var result))
        {
            throw new InvalidDataException(
                $"Cotizador_AI2 omitio la propiedad requerida $.{name}.");
        }
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Cotizador_AI2 devolvio $.{name} como {result.ValueKind}; se esperaba Object.");
        }
        return result;
    }

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ScalarString(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty(name, out var property))
        {
            return null;
        }
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool Bool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var result)
            ? result
            : null;

    private static double? Double(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDouble(out var result)
            ? result
            : null;

    private static decimal? Decimal(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDecimal(out var result)
            ? result
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
