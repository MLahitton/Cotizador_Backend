"""Shared Gemini integration primitives."""

from .client import (
    GEMINI_GENERATE_CONTENT_URL_TEMPLATE,
    GeminiCandidateTextError,
    GeminiHttpResponse,
    GeminiHttpTransport,
    GeminiHttpTransportError,
    GeminiRateLimitError,
    GeminiResponseError,
    GeminiServerError,
    GeminiTimeoutError,
    UrllibGeminiHttpTransport,
    build_generate_content_url,
    decode_gemini_json_object,
    extract_gemini_candidate_text,
    extract_gemini_error_fields,
    extract_gemini_finish_reason,
    extract_gemini_usage_metadata,
    elapsed_ms,
)

__all__ = [
    "GEMINI_GENERATE_CONTENT_URL_TEMPLATE",
    "GeminiCandidateTextError",
    "GeminiHttpResponse",
    "GeminiHttpTransport",
    "GeminiHttpTransportError",
    "GeminiRateLimitError",
    "GeminiResponseError",
    "GeminiServerError",
    "GeminiTimeoutError",
    "UrllibGeminiHttpTransport",
    "build_generate_content_url",
    "decode_gemini_json_object",
    "extract_gemini_candidate_text",
    "extract_gemini_error_fields",
    "extract_gemini_finish_reason",
    "extract_gemini_usage_metadata",
    "elapsed_ms",
]

