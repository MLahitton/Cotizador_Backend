using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;

namespace Infrastructure.DocumentProcessing;

public sealed class CotizadorAiDocumentProcessingClient(
    HttpClient httpClient,
    CotizadorAiOptions options,
    IDocumentProcessingDiagnostics? diagnostics = null)
    : IDocumentProcessingClient
{
    private const string DocumentExtractionPath =
        "api/v3/prequotes/document-extractions";

    private const string CorrelationHeaderName = "X-Correlation-ID";
    private const long MaximumPdfSizeBytes = 20_971_520;

    private static readonly UTF8Encoding StrictUtf8Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = false
    };

    public async Task<DocumentProcessingClientResult> ProcessAsync(
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutSource.CancelAfter(
            TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            using var requestMessage = CreateRequestMessage(request);
            using var responseMessage = await httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            return await ProcessResponseAsync(
                responseMessage,
                request,
                timeoutSource.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && timeoutSource.IsCancellationRequested)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.Timeout);
        }
        catch (ContractValidationException exception)
        {
            diagnostics?.ContractRejected(
                request.DocumentId,
                request.ProcessingAttemptId,
                request.CorrelationId,
                exception.HttpStatusCode,
                exception.Stage,
                exception.Category);
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }
        catch (InvalidDataException)
        {
            diagnostics?.ContractRejected(
                request.DocumentId, request.ProcessingAttemptId,
                request.CorrelationId, null,
                "response_contract", "invalid_data");
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }
        catch (JsonException)
        {
            diagnostics?.ContractRejected(
                request.DocumentId, request.ProcessingAttemptId,
                request.CorrelationId, null,
                "root_shape", "invalid_json");
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }
        catch (DecoderFallbackException)
        {
            diagnostics?.ContractRejected(
                request.DocumentId, request.ProcessingAttemptId,
                request.CorrelationId, null,
                "response_body", "invalid_utf8");
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }
        catch (HttpRequestException)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable);
        }
        catch (IOException)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable);
        }
    }

    private static void ValidateRequest(
        DocumentProcessingClientRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador del documento es obligatorio.",
                nameof(request));
        }

        if (request.ProcessingAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador del intento de procesamiento es obligatorio.",
                nameof(request));
        }

        if (request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador de correlación es obligatorio.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException(
                "El nombre del archivo es obligatorio.",
                nameof(request));
        }

        if (!string.Equals(
                request.FileName,
                request.FileName.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "El nombre del archivo no puede contener espacios externos.",
                nameof(request));
        }

        if (request.FileName.Length > 255)
        {
            throw new ArgumentException(
                "El nombre del archivo no puede superar 255 caracteres.",
                nameof(request));
        }

        if (request.SizeBytes <= 0
            || request.SizeBytes > MaximumPdfSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.SizeBytes,
                "El tamaño del archivo debe estar entre 1 y 20971520 bytes.");
        }

        if (request.Content is null)
        {
            throw new ArgumentNullException(
                nameof(request),
                "El contenido del archivo es obligatorio.");
        }

        if (!request.Content.CanRead)
        {
            throw new ArgumentException(
                "El contenido del archivo debe ser legible.",
                nameof(request));
        }
    }

    private static HttpRequestMessage CreateRequestMessage(
        DocumentProcessingClientRequest request)
    {
        var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            DocumentExtractionPath);

        requestMessage.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        requestMessage.Headers.Add(
            CorrelationHeaderName,
            request.CorrelationId.ToString("D"));

        var multipartContent = new MultipartFormDataContent();

        multipartContent.Add(
            new StringContent(
                request.DocumentId.ToString("D"),
                Encoding.UTF8),
            "documentId");

        multipartContent.Add(
            new StringContent(
                request.ProcessingAttemptId.ToString("D"),
                Encoding.UTF8),
            "processingAttemptId");

        var fileContent = new StreamContent(
            new NonDisposingStream(request.Content));

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/pdf");

        multipartContent.Add(
            fileContent,
            "file",
            request.FileName);

        requestMessage.Content = multipartContent;

        return requestMessage;
    }

    private async Task<DocumentProcessingClientResult> ProcessResponseAsync(
        HttpResponseMessage response,
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        try
        {
            ValidateCorrelationHeader(response, request.CorrelationId);
        }
        catch (InvalidDataException exception)
        {
            throw Contract(
                "correlation_header", "invalid_correlation",
                statusCode, exception);
        }

        if (statusCode is 502 or 503)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable);
        }

        if (statusCode is 408 or 504)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.Timeout);
        }

        if (statusCode is not 200 and not 413 and not 415 and not 422 and not 500)
        {
            diagnostics?.ContractRejected(
                request.DocumentId, request.ProcessingAttemptId,
                request.CorrelationId, statusCode,
                "http_status", "unsupported_status");
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }

        try
        {
            ValidateContentType(response);
        }
        catch (InvalidDataException exception)
        {
            throw Contract(
                "content_type", "invalid_content_type",
                statusCode, exception);
        }

        string payloadJson;
        try
        {
            payloadJson = await ReadResponseBodyAsync(
                response.Content,
                options.MaximumResponseBytes,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw Contract(
                "response_body", "invalid_size",
                statusCode, exception);
        }

        if (statusCode == 200)
        {
            var responseData = ParseSuccessResponse(
                payloadJson,
                request);

            return DocumentProcessingClientResult.Success(responseData);
        }

        return ParseErrorResponse(payloadJson, statusCode);
    }

    private static void ValidateCorrelationHeader(
        HttpResponseMessage response,
        Guid expectedCorrelationId)
    {
        if (!response.Headers.TryGetValues(
                CorrelationHeaderName,
                out var values))
        {
            throw new InvalidDataException();
        }

        var correlationValues = values.ToArray();

        if (correlationValues.Length != 1
            || string.IsNullOrWhiteSpace(correlationValues[0])
            || !Guid.TryParse(
                correlationValues[0],
                out var correlationId)
            || correlationId == Guid.Empty
            || correlationId != expectedCorrelationId)
        {
            throw new InvalidDataException();
        }
    }

    private static void ValidateContentType(
        HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues(
                "Content-Type",
                out var values))
        {
            throw new InvalidDataException();
        }

        var contentTypeValues = values.ToArray();

        if (contentTypeValues.Length != 1
            || !MediaTypeHeaderValue.TryParse(
                contentTypeValues[0],
                out var contentType)
            || !string.Equals(
                contentType.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException();
        }
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpContent content,
        long maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength
            && contentLength > maximumResponseBytes)
        {
            throw new InvalidDataException();
        }

        await using var responseStream =
            await content.ReadAsStreamAsync(cancellationToken);

        using var bodyBuffer = new MemoryStream();
        var readBuffer = new byte[81_920];
        var maximumBytesToRead = maximumResponseBytes + 1;
        long totalBytesRead = 0;

        while (totalBytesRead < maximumBytesToRead)
        {
            var remainingBytes = maximumBytesToRead - totalBytesRead;
            var bytesToRead = (int)Math.Min(
                readBuffer.Length,
                remainingBytes);
            var bytesRead = await responseStream.ReadAsync(
                readBuffer.AsMemory(0, bytesToRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            await bodyBuffer.WriteAsync(
                readBuffer.AsMemory(0, bytesRead),
                cancellationToken);

            totalBytesRead += bytesRead;
        }

        if (totalBytesRead == 0
            || totalBytesRead > maximumResponseBytes)
        {
            throw new InvalidDataException();
        }

        return StrictUtf8Encoding.GetString(
            bodyBuffer.GetBuffer(),
            0,
            checked((int)totalBytesRead));
    }

    private DocumentProcessingResponseData ParseSuccessResponse(
        string payloadJson,
        DocumentProcessingClientRequest request)
    {
        using var jsonDocument = JsonDocument.Parse(
            payloadJson,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

        var payloadSchemaVersion =
            jsonDocument.RootElement.TryGetProperty(
                "schemaVersion", out var schemaElement)
            && schemaElement.ValueKind == JsonValueKind.String
                ? schemaElement.GetString()
                : null;
        try
        {
            ValidateSuccessJsonShape(
                jsonDocument.RootElement,
                payloadSchemaVersion);
        }
        catch (InvalidDataException exception)
        {
            throw Contract(
                "root_shape", "invalid_shape", 200, exception);
        }

        var response = JsonSerializer.Deserialize<SuccessResponseDto>(
            payloadJson,
            SerializerOptions)
            ?? throw new InvalidDataException();

        if (response.SchemaVersion is not ("2.0" or "3.0")
            || response.DocumentId == Guid.Empty
            || response.DocumentId != request.DocumentId
            || response.ProcessingAttemptId == Guid.Empty
            || response.ProcessingAttemptId != request.ProcessingAttemptId
            || response.Document is null
            || response.Pages is null
            || response.Warnings is null
            || response.ProcessingMetadata is null
            || response.StructuredExtraction is null)
        {
            throw new InvalidDataException();
        }

        var outcome = MapOutcome(response.Status);
        var classification = MapClassification(
            response.Document.Classification);

        if (!string.Equals(
                response.Document.FileName,
                request.FileName,
                StringComparison.Ordinal)
            || !string.Equals(
                response.Document.ContentType,
                "application/pdf",
                StringComparison.Ordinal)
            || response.Document.SizeBytes != request.SizeBytes
            || response.Document.PageCount < 1
            || response.Document.PageCount > options.MaximumPageCount
            || response.Pages.Count != response.Document.PageCount)
        {
            throw new InvalidDataException();
        }

        var pages = new ProcessedPageData[response.Pages.Count];

        for (var index = 0; index < response.Pages.Count; index++)
        {
            var page = response.Pages[index];
            var expectedPageNumber = index + 1;

            if (page is null
                || page.PageNumber != expectedPageNumber
                || page.Text is null
                || page.CharacterCount < 0)
            {
                throw new InvalidDataException();
            }

            var characterCount = page.Text.EnumerateRunes().Count();
            var hasExtractableText =
                !string.IsNullOrWhiteSpace(page.Text);

            if (page.CharacterCount != characterCount
                || page.HasExtractableText != hasExtractableText)
            {
                throw new InvalidDataException();
            }

            pages[index] = new ProcessedPageData(
                page.PageNumber,
                page.Text,
                page.CharacterCount,
                page.HasExtractableText);
        }

        ValidateClassificationInvariants(
            outcome,
            classification,
            response.Document.RequiresOcr,
            pages);

        var warnings =
            new ProcessingWarningData[response.Warnings.Count];

        for (var index = 0; index < response.Warnings.Count; index++)
        {
            var warning = response.Warnings[index];

            if (warning is null
                || string.IsNullOrWhiteSpace(warning.Code)
                || string.IsNullOrWhiteSpace(warning.Message)
                || warning.PageNumbers is null)
            {
                throw new InvalidDataException();
            }

            var pageNumbers = warning.PageNumbers.ToArray();

            for (var pageIndex = 0;
                 pageIndex < pageNumbers.Length;
                 pageIndex++)
            {
                var pageNumber = pageNumbers[pageIndex];

                if (pageNumber < 1
                    || pageNumber > response.Document.PageCount
                    || (pageIndex > 0
                        && pageNumber <= pageNumbers[pageIndex - 1]))
                {
                    throw new InvalidDataException();
                }
            }

            warnings[index] = new ProcessingWarningData(
                warning.Code,
                warning.Message,
                pageNumbers);
        }

        ValidateWarnings(
            classification,
            pages,
            warnings);

        if (!string.Equals(
                response.ProcessingMetadata.Method,
                "pymupdf",
                StringComparison.Ordinal)
            || response.ProcessingMetadata.DurationMs < 0)
        {
            throw new InvalidDataException();
        }

        var canonicalPayloadJson = JsonSerializer.Serialize(
            response,
            SerializerOptions);
        var structuredExtraction = ValidateStructuredExtraction(
            response.StructuredExtraction,
            response.Document.PageCount,
            response.Document.RequiresOcr,
            response.SchemaVersion);

        return new DocumentProcessingResponseData(
            response.SchemaVersion!,
            response.DocumentId,
            response.ProcessingAttemptId,
            outcome,
            new ProcessedDocumentData(
                response.Document.FileName!,
                response.Document.ContentType!,
                response.Document.SizeBytes,
                response.Document.PageCount,
                classification,
                response.Document.RequiresOcr),
            pages,
            warnings,
            new ProcessingMetadataData(
                response.ProcessingMetadata.Method!,
                response.ProcessingMetadata.DurationMs),
            canonicalPayloadJson,
            structuredExtraction);
    }

    private static DocumentProcessingClientResult ParseErrorResponse(
        string payloadJson,
        int statusCode)
    {
        using var jsonDocument = JsonDocument.Parse(
            payloadJson,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

        ValidateExactObjectProperties(
            jsonDocument.RootElement,
            "schemaVersion",
            "errorCode",
            "message");

        var errorResponse = JsonSerializer.Deserialize<ErrorResponseDto>(
            payloadJson,
            SerializerOptions)
            ?? throw new InvalidDataException();

        if (errorResponse.SchemaVersion is not { } schemaVersion
            || errorResponse.ErrorCode is not { } errorCode
            || errorResponse.Message is not { } message)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }

        if (!string.Equals(
                schemaVersion,
                "1.0",
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(errorCode)
            || string.IsNullOrWhiteSpace(message)
            || !IsValidRemoteError(
                statusCode,
                errorCode,
                message))
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }

        var remoteError = new DocumentProcessingRemoteError(
            statusCode,
            schemaVersion,
            errorCode,
            message);

        var failure = statusCode == 500
            ? DocumentProcessingClientFailure.ServiceError
            : DocumentProcessingClientFailure.RemoteRejection;

        return DocumentProcessingClientResult.RemoteFailure(
            failure,
            remoteError);
    }

    private static void ValidateSuccessJsonShape(
        JsonElement root,
        string? schemaVersion)
    {
        ValidateExactObjectProperties(
            root,
            "schemaVersion",
            "documentId",
            "processingAttemptId",
            "status",
            "document",
            "pages",
            "warnings",
            "processingMetadata",
            "structuredExtraction");

        ValidateExactObjectProperties(
            root.GetProperty("document"),
            "fileName",
            "contentType",
            "sizeBytes",
            "pageCount",
            "classification",
            "requiresOcr");

        var pages = root.GetProperty("pages");

        if (pages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException();
        }

        foreach (var page in pages.EnumerateArray())
        {
            ValidateExactObjectProperties(
                page,
                "pageNumber",
                "text",
                "characterCount",
                "hasExtractableText");
        }

        var warnings = root.GetProperty("warnings");

        if (warnings.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException();
        }

        foreach (var warning in warnings.EnumerateArray())
        {
            ValidateExactObjectProperties(
                warning,
                "code",
                "message",
                "pageNumbers");
        }

        ValidateExactObjectProperties(
            root.GetProperty("processingMetadata"),
            "method",
            "durationMs");

        ValidateStructuredJsonShape(
            root.GetProperty("structuredExtraction"),
            schemaVersion);
    }

    private static void ValidateExactObjectProperties(
        JsonElement element,
        params string[] expectedPropertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException();
        }

        var expectedProperties = new HashSet<string>(
            expectedPropertyNames,
            StringComparer.Ordinal);
        var observedProperties = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!observedProperties.Add(property.Name)
                || !expectedProperties.Contains(property.Name))
            {
                throw new InvalidDataException();
            }
        }

        if (!observedProperties.SetEquals(expectedProperties))
        {
            throw new InvalidDataException();
        }
    }

    private static void ValidateStructuredJsonShape(
        JsonElement root,
        string? schemaVersion)
    {
        ValidateExactObjectProperties(root, "status", "project",
            "requirements", "items", "documentReferences", "issues",
            "conflicts", "summary", "processingMetadata");
        ValidateExactObjectProperties(root.GetProperty("project"),
            "name", "clientName", "location", "sourcePages", "evidence");
        ValidateExactObjectProperties(root.GetProperty("requirements"),
            "glassSpecifications", "profileSpecifications", "finishes",
            "accessoriesAndSealants", "generalNotes");
        if (schemaVersion == "2.0")
            ValidateExactObjectProperties(root.GetProperty("summary"),
                "itemCount", "documentReferenceCount", "itemsRequiringReview",
                "knownQuoteableUnitCount");
        else if (schemaVersion == "3.0")
            ValidateExactObjectProperties(root.GetProperty("summary"),
                "itemCount", "documentReferenceCount", "itemsRequiringReview",
                "knownQuoteableUnitCount", "identifiedGlassItemCount",
                "glassItemsRequiringReview");
        else
            throw Contract(
                "structured_shape", "unsupported_schema", 200);
        ValidateExactObjectProperties(root.GetProperty("processingMetadata"),
            "method", "durationMs");
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            var properties = new List<string>
            {
                "sequence", "reference", "description", "elementType",
                "rawMeasurements", "widthMillimeters", "heightMillimeters",
                "quantity", "requiresReview", "reviewReasons",
                "sourcePages", "evidence"
            };
            if (schemaVersion == "3.0") properties.Add("glass");
            ValidateExactObjectProperties(item, [.. properties]);
            if (schemaVersion == "3.0")
            {
                var glass = item.GetProperty("glass");
                ValidateExactObjectProperties(glass, "rawSpecification",
                    "normalizedCode", "assignmentScope", "requiresReview",
                    "reviewReasons", "sourcePages", "evidence");
                foreach (var evidence in glass.GetProperty("evidence").EnumerateArray())
                    ValidateExactObjectProperties(evidence, "pageNumber",
                        "sourceType", "text");
            }
        }
        foreach (var item in root.GetProperty("documentReferences").EnumerateArray())
            ValidateExactObjectProperties(item, "sequence", "reference",
                "description", "detail", "quantity", "sourcePages", "evidence");
        foreach (var item in root.GetProperty("issues").EnumerateArray())
            ValidateExactObjectProperties(item, "code", "message",
                "itemSequence", "pageNumbers");
        foreach (var item in root.GetProperty("conflicts").EnumerateArray())
            ValidateExactObjectProperties(item, "code", "message",
                "itemSequences", "pageNumbers");
    }

    private static StructuredExtractionData ValidateStructuredExtraction(
        StructuredExtractionDto dto,
        int pageCount,
        bool requiresOcr,
        string schemaVersion)
    {
        var status = dto.Status switch
        {
            "COMPLETED" => StructuredExtractionStatus.Completed,
            "REQUIRES_REVIEW" => StructuredExtractionStatus.RequiresReview,
            _ => throw new InvalidDataException()
        };
        if (dto.Project is null || dto.Requirements is null || dto.Items is null
            || dto.DocumentReferences is null || dto.Issues is null
            || dto.Conflicts is null || dto.Summary is null
            || dto.ProcessingMetadata is null
            || dto.ProcessingMetadata.Method
                != GetExpectedStructuredExtractionMethod(schemaVersion)
            || dto.ProcessingMetadata.DurationMs < 0)
            throw Contract(
                "structured_metadata", "method_mismatch", 200);

        ValidateNumbers(dto.Project.SourcePages, pageCount);
        var requirements = new List<StructuredRequirementData>();
        AddRequirements(requirements, dto.Requirements.GlassSpecifications,
            RequirementCategory.GlassSpecification, pageCount);
        AddRequirements(requirements, dto.Requirements.ProfileSpecifications,
            RequirementCategory.ProfileSpecification, pageCount);
        AddRequirements(requirements, dto.Requirements.Finishes,
            RequirementCategory.Finish, pageCount);
        AddRequirements(requirements, dto.Requirements.AccessoriesAndSealants,
            RequirementCategory.AccessoriesAndSealants, pageCount);
        AddRequirements(requirements, dto.Requirements.GeneralNotes,
            RequirementCategory.GeneralNote, pageCount);
        var projectEvidence = MapEvidence(dto.Project.Evidence, pageCount);

        var items = new List<StructuredItemData>();
        for (var index = 0; index < dto.Items.Count; index++)
        {
            var x = dto.Items[index] ?? throw new InvalidDataException();
            if (x.Sequence != index + 1 || string.IsNullOrWhiteSpace(x.Description)
                || (x.WidthMillimeters is null) != (x.HeightMillimeters is null)
                || x.WidthMillimeters is <= 0 || x.HeightMillimeters is <= 0
                || x.Quantity is <= 0 || x.ReviewReasons is null
                || schemaVersion == "3.0" && x.Glass is null
                || schemaVersion == "2.0" && x.Glass is not null)
                throw new InvalidDataException();
            ValidateNumbers(x.SourcePages, pageCount);
            var glass = x.Glass is null
                ? null
                : MapGlass(x.Glass, pageCount);
            items.Add(new StructuredItemData(x.Sequence, x.Reference,
                x.Description, MapElementType(x.ElementType), x.RawMeasurements,
                x.WidthMillimeters, x.HeightMillimeters, x.Quantity,
                x.RequiresReview, x.ReviewReasons.Select(MapIssueCode).ToArray(),
                x.SourcePages!, MapEvidence(x.Evidence, pageCount), glass));
        }

        var references = new List<StructuredDocumentReferenceData>();
        for (var index = 0; index < dto.DocumentReferences.Count; index++)
        {
            var x = dto.DocumentReferences[index] ?? throw new InvalidDataException();
            if (x.Sequence != index + 1 || string.IsNullOrWhiteSpace(x.Description)
                || x.Quantity is <= 0) throw new InvalidDataException();
            ValidateNumbers(x.SourcePages, pageCount);
            references.Add(new(x.Sequence, x.Reference, x.Description,
                x.Detail, x.Quantity, x.SourcePages!,
                MapEvidence(x.Evidence, pageCount)));
        }

        var issues = dto.Issues.Select((x, index) =>
        {
            if (x is null || string.IsNullOrWhiteSpace(x.Message)
                || x.ItemSequence is <= 0
                || x.ItemSequence > items.Count) throw new InvalidDataException();
            ValidateNumbers(x.PageNumbers, pageCount);
            return new StructuredIssueData(index + 1, MapIssueCode(x.Code),
                x.Message, x.ItemSequence, x.PageNumbers!);
        }).ToArray();
        var conflicts = dto.Conflicts.Select((x, index) =>
        {
            if (x is null || string.IsNullOrWhiteSpace(x.Message))
                throw new InvalidDataException();
            ValidateNumbers(x.PageNumbers, pageCount);
            ValidateNumbers(x.ItemSequences, items.Count);
            return new StructuredConflictData(index + 1,
                MapConflictCode(x.Code), x.Message, x.ItemSequences!,
                x.PageNumbers!);
        }).ToArray();
        var summary = dto.Summary;
        if (summary.ItemCount != items.Count
            || summary.DocumentReferenceCount != references.Count
            || summary.ItemsRequiringReview != items.Count(x => x.RequiresReview)
            || summary.KnownQuoteableUnitCount != items.Sum(x => x.Quantity ?? 0))
            throw new InvalidDataException();
        if (schemaVersion == "3.0"
            && (summary.IdentifiedGlassItemCount
                    != items.Count(x => x.Glass?.NormalizedCode is not null)
                || summary.GlassItemsRequiringReview
                    != items.Count(x => x.Glass?.RequiresReview == true))
            || schemaVersion == "2.0"
                && (summary.IdentifiedGlassItemCount is not null
                    || summary.GlassItemsRequiringReview is not null))
            throw new InvalidDataException();

        var requiresReview = requiresOcr
            || string.IsNullOrWhiteSpace(dto.Project.Name)
            || items.Count == 0
            || issues.Length > 0
            || conflicts.Length > 0
            || items.Any(x => x.RequiresReview)
            || summary.ItemsRequiringReview > 0;

        if ((status == StructuredExtractionStatus.RequiresReview)
                != requiresReview
            || status == StructuredExtractionStatus.Completed
                && items.Any(x =>
                    string.IsNullOrWhiteSpace(x.Reference)
                    || x.WidthMillimeters is null
                    || x.HeightMillimeters is null
                    || x.Quantity is null
                    || x.ElementType == StructuredElementType.Other
                    || x.ReviewReasons.Count != 0))
        {
            throw new InvalidDataException();
        }

        return new(status, dto.Project.Name, dto.Project.ClientName,
            dto.Project.Location, dto.Project.SourcePages!, projectEvidence,
            requirements, items, references, issues, conflicts,
            summary.ItemCount, summary.DocumentReferenceCount,
            summary.ItemsRequiringReview, summary.KnownQuoteableUnitCount,
            dto.ProcessingMetadata.Method!, dto.ProcessingMetadata.DurationMs,
            summary.IdentifiedGlassItemCount,
            summary.GlassItemsRequiringReview);
    }

    private static StructuredItemGlassData MapGlass(
        GlassDto dto, int pageCount)
    {
        if (dto.RawSpecification is { } raw
            && (string.IsNullOrWhiteSpace(raw) || raw.Length > 500
                || raw != raw.Trim()))
            throw Contract("glass_contract", "invalid_raw_specification", 200);
        if (dto.NormalizedCode is not null
            && dto.NormalizedCode is not ("LAM_4_4" or "LAM_4_4_GRAY"
                or "LAM_5_5" or "LAM_5_5_GRAY"))
            throw Contract("glass_contract", "unknown_code", 200);
        var scope = dto.AssignmentScope switch
        {
            "ITEM" => GlassAssignmentScope.Item,
            "SECTION" => GlassAssignmentScope.Section,
            "GENERAL" => GlassAssignmentScope.General,
            "UNASSIGNED" => GlassAssignmentScope.Unassigned,
            _ => throw Contract("glass_contract", "unknown_scope", 200)
        };
        if (dto.ReviewReasons is null)
            throw Contract("glass_contract", "missing_review_reasons", 200);
        var reasons = dto.ReviewReasons.Select(value => value switch
        {
            "GLASS_TYPE_NOT_IDENTIFIED" =>
                GlassReviewReason.GlassTypeNotIdentified,
            "GLASS_TYPE_AMBIGUOUS" => GlassReviewReason.GlassTypeAmbiguous,
            "GLASS_TYPE_CONFLICT" => GlassReviewReason.GlassTypeConflict,
            _ => throw Contract("glass_contract", "unknown_review_reason", 200)
        }).ToArray();
        ValidateNumbers(dto.SourcePages, pageCount);
        var evidence = MapEvidence(dto.Evidence, pageCount);
        var evidencePages = evidence.Select(value => value.PageNumber)
            .Distinct().Order().ToArray();
        var unassigned = scope == GlassAssignmentScope.Unassigned;
        var validUnassigned = unassigned
            && dto.RawSpecification is null
            && dto.NormalizedCode is null
            && dto.RequiresReview
            && reasons.SequenceEqual(
                [GlassReviewReason.GlassTypeNotIdentified])
            && dto.SourcePages!.Count == 0
            && evidence.Length == 0;
        var validAssigned = !unassigned
            && dto.RawSpecification is not null
            && evidence.Length > 0
            && dto.SourcePages!.SequenceEqual(evidencePages)
            && (dto.NormalizedCode is not null
                || dto.RequiresReview && reasons.Length > 0);
        if (reasons.Distinct().Count() != reasons.Length
            || dto.RequiresReview != (reasons.Length > 0)
            || !validUnassigned && !validAssigned)
            throw Contract("glass_contract", "inconsistent_assignment", 200);
        return new(dto.RawSpecification, dto.NormalizedCode, scope,
            dto.RequiresReview, reasons, dto.SourcePages!, evidence);
    }

    private static void AddRequirements(List<StructuredRequirementData> target,
        List<RequirementDto?>? values, RequirementCategory category, int pages)
    {
        if (values is null) throw new InvalidDataException();
        foreach (var value in values)
        {
            if (value is null || string.IsNullOrWhiteSpace(value.Value))
                throw new InvalidDataException();
            target.Add(new(category, value.Value,
                MapEvidence(value.Evidence, pages)));
        }
    }

    private static SourceEvidenceData[] MapEvidence(
        List<EvidenceDto?>? values, int pageCount)
    {
        if (values is null) throw new InvalidDataException();
        var result = new SourceEvidenceData[values.Count];
        var seen = new HashSet<(int, string, string)>();
        for (var i = 0; i < values.Count; i++)
        {
            var x = values[i] ?? throw new InvalidDataException();
            if (x.PageNumber < 1 || x.PageNumber > pageCount
                || string.IsNullOrWhiteSpace(x.Text) || x.Text.Length > 500
                || !seen.Add((x.PageNumber, x.SourceType ?? "", x.Text)))
                throw new InvalidDataException();
            if (i > 0 && x.PageNumber < result[i - 1].PageNumber)
                throw new InvalidDataException();
            result[i] = new(x.PageNumber, x.SourceType switch
            {
                "NATIVE" => EvidenceSourceType.Native,
                "OCR" => EvidenceSourceType.Ocr,
                _ => throw new InvalidDataException()
            }, x.Text);
        }
        return result;
    }

    private static void ValidateNumbers(List<int>? values, int maximum)
    {
        if (values is null) throw new InvalidDataException();
        for (var i = 0; i < values.Count; i++)
            if (values[i] < 1 || values[i] > maximum
                || i > 0 && values[i] <= values[i - 1])
                throw new InvalidDataException();
    }

    private static StructuredElementType MapElementType(string? value) => value switch
    {
        "WINDOW" => StructuredElementType.Window, "DOOR" => StructuredElementType.Door,
        "FACADE" => StructuredElementType.Facade, "PARTITION" => StructuredElementType.Partition,
        "RAILING" => StructuredElementType.Railing, "SKYLIGHT" => StructuredElementType.Skylight,
        "OTHER" => StructuredElementType.Other, _ => throw new InvalidDataException()
    };
    private static StructuredIssueCode MapIssueCode(string? value) => value switch
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
        _ => throw new InvalidDataException()
    };
    private static StructuredConflictCode MapConflictCode(string? value) => value switch
    {
        "CONFLICTING_PROJECT_NAME" => StructuredConflictCode.ConflictingProjectName,
        "CONFLICTING_CLIENT_NAME" => StructuredConflictCode.ConflictingClientName,
        "CONFLICTING_LOCATION" => StructuredConflictCode.ConflictingLocation,
        "DUPLICATE_ITEM_REFERENCE" => StructuredConflictCode.DuplicateItemReference,
        _ => throw new InvalidDataException()
    };

    private static DocumentProcessingOutcome MapOutcome(
        string? status)
    {
        return status switch
        {
            "COMPLETED" => DocumentProcessingOutcome.Completed,
            "REQUIRES_REVIEW" =>
                DocumentProcessingOutcome.RequiresReview,
            _ => throw new InvalidDataException()
        };
    }

    private static PdfClassification MapClassification(
        string? classification)
    {
        return classification switch
        {
            "PDF_TEXT" => PdfClassification.PdfText,
            "PDF_SCANNED" => PdfClassification.PdfScanned,
            "PDF_MIXED" => PdfClassification.PdfMixed,
            _ => throw new InvalidDataException()
        };
    }

    private static void ValidateClassificationInvariants(
        DocumentProcessingOutcome outcome,
        PdfClassification classification,
        bool requiresOcr,
        IReadOnlyList<ProcessedPageData> pages)
    {
        var isValid = classification switch
        {
            PdfClassification.PdfText =>
                outcome == DocumentProcessingOutcome.Completed
                && !requiresOcr
                && pages.All(page => page.HasExtractableText),
            PdfClassification.PdfScanned =>
                outcome == DocumentProcessingOutcome.RequiresReview
                && requiresOcr,
            PdfClassification.PdfMixed =>
                outcome == DocumentProcessingOutcome.RequiresReview
                && requiresOcr
                && pages.Count >= 2,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidDataException();
        }
    }

    private static bool IsValidRemoteError(
        int statusCode,
        string errorCode,
        string message)
    {
        return statusCode switch
        {
            413 =>
                errorCode == "FILE_TOO_LARGE"
                && message ==
                "The uploaded file exceeds the maximum allowed size.",
            415 =>
                errorCode == "UNSUPPORTED_FILE_TYPE"
                && message ==
                "Only application/pdf files are supported.",
            422 => errorCode switch
            {
                "INVALID_REQUEST" =>
                    message == "The request is invalid.",
                "INVALID_CORRELATION_ID" =>
                    message ==
                    "A valid X-Correlation-ID header is required.",
                "EMPTY_FILE" =>
                    message == "The uploaded file is empty.",
                "INVALID_PDF" =>
                    message == "The uploaded file is not a valid PDF.",
                "PDF_PASSWORD_REQUIRED" =>
                    message == "The PDF requires a password.",
                "PDF_PAGE_LIMIT_EXCEEDED" =>
                    message ==
                    "The PDF exceeds the maximum allowed page count.",
                _ => false
            },
            500 =>
                errorCode == "INTERNAL_SERVER_ERROR"
                && message == "An unexpected error occurred.",
            _ => false
        };
    }

    private static void ValidateWarnings(
        PdfClassification classification,
        IReadOnlyList<ProcessedPageData> pages,
        IReadOnlyList<ProcessingWarningData> warnings)
    {
        switch (classification)
        {
            case PdfClassification.PdfText:
                if (warnings.Count != 0)
                {
                    throw new InvalidDataException();
                }

                break;

            case PdfClassification.PdfScanned:
                var scannedWarning = ValidateSingleWarning(
                    warnings,
                    "OCR_REQUIRED",
                    "The document does not contain extractable text.");

                if (!scannedWarning.PageNumbers.SequenceEqual(
                        Enumerable.Range(1, pages.Count)))
                {
                    throw new InvalidDataException();
                }

                break;

            case PdfClassification.PdfMixed:
                var mixedWarning = ValidateSingleWarning(
                    warnings,
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.");
                var warnedPageNumbers =
                    mixedWarning.PageNumbers.ToHashSet();

                if (warnedPageNumbers.Count == 0
                    || warnedPageNumbers.Count >= pages.Count
                    || pages.Any(
                        page =>
                            !warnedPageNumbers.Contains(page.PageNumber)
                            && !page.HasExtractableText))
                {
                    throw new InvalidDataException();
                }

                break;

            default:
                throw new InvalidDataException();
        }
    }

    private static ProcessingWarningData ValidateSingleWarning(
        IReadOnlyList<ProcessingWarningData> warnings,
        string expectedCode,
        string expectedMessage)
    {
        if (warnings.Count != 1)
        {
            throw new InvalidDataException();
        }

        var warning = warnings[0];

        if (!string.Equals(
                warning.Code,
                expectedCode,
                StringComparison.Ordinal)
            || !string.Equals(
                warning.Message,
                expectedMessage,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException();
        }

        return warning;
    }

    private sealed class SuccessResponseDto
    {
        public string? SchemaVersion { get; init; }

        public Guid DocumentId { get; init; }

        public Guid ProcessingAttemptId { get; init; }

        public string? Status { get; init; }

        public DocumentDto? Document { get; init; }

        public List<PageDto?>? Pages { get; init; }

        public List<WarningDto?>? Warnings { get; init; }

        public ProcessingMetadataDto? ProcessingMetadata { get; init; }

        public StructuredExtractionDto? StructuredExtraction { get; init; }
    }

    private sealed class DocumentDto
    {
        public string? FileName { get; init; }

        public string? ContentType { get; init; }

        public long SizeBytes { get; init; }

        public int PageCount { get; init; }

        public string? Classification { get; init; }

        public bool RequiresOcr { get; init; }
    }

    private sealed class PageDto
    {
        public int PageNumber { get; init; }

        public string? Text { get; init; }

        public int CharacterCount { get; init; }

        public bool HasExtractableText { get; init; }
    }

    private sealed class WarningDto
    {
        public string? Code { get; init; }

        public string? Message { get; init; }

        public List<int>? PageNumbers { get; init; }
    }

    private sealed class ProcessingMetadataDto
    {
        public string? Method { get; init; }

        public int DurationMs { get; init; }
    }

    private sealed class ErrorResponseDto
    {
        public string? SchemaVersion { get; init; }

        public string? ErrorCode { get; init; }

        public string? Message { get; init; }
    }

    private sealed class StructuredExtractionDto
    {
        public string? Status { get; init; }
        public ProjectDto? Project { get; init; }
        public RequirementsDto? Requirements { get; init; }
        public List<ItemDto?>? Items { get; init; }
        public List<DocumentReferenceDto?>? DocumentReferences { get; init; }
        public List<IssueDto?>? Issues { get; init; }
        public List<ConflictDto?>? Conflicts { get; init; }
        public SummaryDto? Summary { get; init; }
        public ProcessingMetadataDto? ProcessingMetadata { get; init; }
    }
    private sealed class ProjectDto { public string? Name { get; init; } public string? ClientName { get; init; } public string? Location { get; init; } public List<int>? SourcePages { get; init; } public List<EvidenceDto?>? Evidence { get; init; } }
    private sealed class RequirementsDto { public List<RequirementDto?>? GlassSpecifications { get; init; } public List<RequirementDto?>? ProfileSpecifications { get; init; } public List<RequirementDto?>? Finishes { get; init; } public List<RequirementDto?>? AccessoriesAndSealants { get; init; } public List<RequirementDto?>? GeneralNotes { get; init; } }
    private sealed class EvidenceDto { public int PageNumber { get; init; } public string? SourceType { get; init; } public string? Text { get; init; } }
    private sealed class RequirementDto { public string? Value { get; init; } public List<EvidenceDto?>? Evidence { get; init; } }
    private sealed class ItemDto { public int Sequence { get; init; } public string? Reference { get; init; } public string? Description { get; init; } public string? ElementType { get; init; } public string? RawMeasurements { get; init; } public int? WidthMillimeters { get; init; } public int? HeightMillimeters { get; init; } public int? Quantity { get; init; } public bool RequiresReview { get; init; } public List<string?>? ReviewReasons { get; init; } public List<int>? SourcePages { get; init; } public List<EvidenceDto?>? Evidence { get; init; } public GlassDto? Glass { get; init; } }
    private sealed class GlassDto { public string? RawSpecification { get; init; } public string? NormalizedCode { get; init; } public string? AssignmentScope { get; init; } public bool RequiresReview { get; init; } public List<string?>? ReviewReasons { get; init; } public List<int>? SourcePages { get; init; } public List<EvidenceDto?>? Evidence { get; init; } }
    private sealed class DocumentReferenceDto { public int Sequence { get; init; } public string? Reference { get; init; } public string? Description { get; init; } public string? Detail { get; init; } public int? Quantity { get; init; } public List<int>? SourcePages { get; init; } public List<EvidenceDto?>? Evidence { get; init; } }
    private sealed class IssueDto { public string? Code { get; init; } public string? Message { get; init; } public int? ItemSequence { get; init; } public List<int>? PageNumbers { get; init; } }
    private sealed class ConflictDto { public string? Code { get; init; } public string? Message { get; init; } public List<int>? ItemSequences { get; init; } public List<int>? PageNumbers { get; init; } }
    private sealed class SummaryDto { public int ItemCount { get; init; } public int DocumentReferenceCount { get; init; } public int ItemsRequiringReview { get; init; } public int KnownQuoteableUnitCount { get; init; } public int? IdentifiedGlassItemCount { get; init; } public int? GlassItemsRequiringReview { get; init; } }

    private static string GetExpectedStructuredExtractionMethod(
        string schemaVersion) => schemaVersion switch
    {
        "2.0" => "rule_based_v1",
        "3.0" => "rule_based_v2",
        _ => throw Contract(
            "structured_metadata", "unsupported_schema", 200)
    };

    private static ContractValidationException Contract(
        string stage,
        string category,
        int? httpStatusCode,
        Exception? innerException = null) =>
        new(stage, category, httpStatusCode, innerException);

    private sealed class ContractValidationException(
        string stage,
        string category,
        int? httpStatusCode,
        Exception? innerException = null)
        : Exception(
            $"Contract validation failed at {stage}: {category}.",
            innerException)
    {
        public string Stage { get; } = stage;
        public string Category { get; } = category;
        public int? HttpStatusCode { get; } = httpStatusCode;
    }

    private sealed class NonDisposingStream(Stream innerStream) : Stream
    {
        public override bool CanRead => innerStream.CanRead;

        public override bool CanSeek => innerStream.CanSeek;

        public override bool CanWrite => innerStream.CanWrite;

        public override long Length => innerStream.Length;

        public override long Position
        {
            get => innerStream.Position;
            set => innerStream.Position = value;
        }

        public override void Flush()
        {
            innerStream.Flush();
        }

        public override Task FlushAsync(
            CancellationToken cancellationToken)
        {
            return innerStream.FlushAsync(cancellationToken);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            return innerStream.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return innerStream.Read(buffer);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return innerStream.ReadAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return innerStream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            return innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            innerStream.SetLength(value);
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            innerStream.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            innerStream.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return innerStream.WriteAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return innerStream.WriteAsync(buffer, cancellationToken);
        }

        public override Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            return innerStream.CopyToAsync(
                destination,
                bufferSize,
                cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
