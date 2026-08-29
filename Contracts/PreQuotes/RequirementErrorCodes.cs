namespace Contracts.PreQuotes;

public static class RequirementErrorCodes
{
    public const string InvalidRequest = "REQUIREMENT_INVALID_REQUEST";
    public const string UnsupportedFileType =
        "REQUIREMENT_UNSUPPORTED_FILE_TYPE";
    public const string EmptyFile = "REQUIREMENT_EMPTY_FILE";
    public const string FileTooLarge = "REQUIREMENT_FILE_TOO_LARGE";
    public const string TooManyFiles = "REQUIREMENT_TOO_MANY_FILES";
    public const string PreQuoteNotFound = "REQUIREMENT_PREQUOTE_NOT_FOUND";
    public const string RequirementNotFound = "REQUIREMENT_NOT_FOUND";
    public const string RequirementNotMutable = "REQUIREMENT_NOT_MUTABLE";
    public const string RequirementNotReplaceable =
        "REQUIREMENT_NOT_REPLACEABLE";
    public const string TechnicalProposalNotFound =
        "REQUIREMENT_TECHNICAL_PROPOSAL_NOT_FOUND";
    public const string TechnicalProposalIncomplete =
        "REQUIREMENT_TECHNICAL_PROPOSAL_INCOMPLETE";
    public const string TechnicalProposalNotConfirmed =
        "REQUIREMENT_TECHNICAL_PROPOSAL_NOT_CONFIRMED";
    public const string ProjectInactive = "REQUIREMENT_PROJECT_INACTIVE";
    public const string ClientInactive = "REQUIREMENT_CLIENT_INACTIVE";
    public const string ProcessingAlreadyActive =
        "REQUIREMENT_PROCESSING_ALREADY_ACTIVE";
    public const string ProcessingAttemptNotFound =
        "REQUIREMENT_PROCESSING_ATTEMPT_NOT_FOUND";
    public const string ProcessingCancelled =
        "REQUIREMENT_PROCESSING_CANCELLED";
    public const string PricingCancelled =
        "REQUIREMENT_PRICING_CANCELLED";
    public const string NoFiles = "REQUIREMENT_NO_FILES";
    public const string StorageError = "REQUIREMENT_STORAGE_ERROR";
    public const string AiServiceUnavailable =
        "REQUIREMENT_AI2_SERVICE_UNAVAILABLE";
    public const string AiTimeout = "REQUIREMENT_AI2_TIMEOUT";
    public const string AiRemoteRejected = "REQUIREMENT_AI2_REJECTED";
    public const string AiInvalidResponse = "AI_INVALID_RESPONSE";
    public const string AiServiceError = "REQUIREMENT_AI2_SERVICE_ERROR";
    public const string PersistenceError = "REQUIREMENT_PERSISTENCE_ERROR";
}
