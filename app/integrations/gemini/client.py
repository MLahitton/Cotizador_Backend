from __future__ import annotations

import json
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Protocol


GEMINI_GENERATE_CONTENT_URL_TEMPLATE = (
    "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
)


@dataclass(frozen=True, slots=True)
class GeminiHttpResponse:
    status_code: int
    body: str


class GeminiHttpTransportError(RuntimeError):
    pass


class GeminiTimeoutError(GeminiHttpTransportError):
    pass


class GeminiResponseError(GeminiHttpTransportError):
    def __init__(
        self,
        message: str,
        *,
        status_code: int,
        error_status: str | None = None,
        error_message: str | None = None,
        error_details: list[object] | None = None,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.error_status = error_status
        self.error_message = error_message
        self.error_details = error_details


class GeminiAuthenticationError(GeminiResponseError):
    pass


class GeminiRateLimitError(GeminiResponseError):
    pass


class GeminiServerError(GeminiResponseError):
    pass


class GeminiHttpTransport(Protocol):
    def post_json(
        self,
        *,
        url: str,
        api_key: str,
        payload: dict[str, object],
        timeout_seconds: float,
    ) -> GeminiHttpResponse:
        pass


class UrllibGeminiHttpTransport:
    def post_json(
        self,
        *,
        url: str,
        api_key: str,
        payload: dict[str, object],
        timeout_seconds: float,
    ) -> GeminiHttpResponse:
        request_url = f"{url}?key={api_key}"
        request = urllib.request.Request(
            request_url,
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
                return GeminiHttpResponse(
                    status_code=response.status,
                    body=response.read().decode("utf-8"),
                )
        except TimeoutError as exc:
            raise GeminiTimeoutError("Gemini request timed out.") from exc
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            return GeminiHttpResponse(status_code=exc.code, body=body)
        except urllib.error.URLError as exc:
            if isinstance(exc.reason, TimeoutError):
                raise GeminiTimeoutError("Gemini request timed out.") from exc
            raise GeminiHttpTransportError("Gemini request failed.") from exc


def build_generate_content_url(model: str) -> str:
    return GEMINI_GENERATE_CONTENT_URL_TEMPLATE.format(model=model)


def decode_gemini_json_object(text: str) -> dict[str, object]:
    decoded = json.loads(text)
    if not isinstance(decoded, dict):
        raise ValueError("Gemini response must be an object.")
    return decoded


def extract_gemini_error_fields(body: str) -> tuple[str | None, str | None, list[object] | None]:
    try:
        decoded = json.loads(body)
    except json.JSONDecodeError:
        return None, body.strip() or None, None
    if not isinstance(decoded, dict):
        return None, None, None
    error = decoded.get("error")
    if not isinstance(error, dict):
        return None, None, None
    status = error.get("status")
    message = error.get("message")
    details = error.get("details")
    return (
        status if isinstance(status, str) else None,
        message if isinstance(message, str) else None,
        details if isinstance(details, list) else None,
    )


def extract_gemini_candidate_text(decoded: dict[str, object]) -> str:
    candidates = decoded.get("candidates")
    if not isinstance(candidates, list) or not candidates:
        raise GeminiCandidateTextError("Gemini response has no candidates.")
    first_candidate = candidates[0]
    if not isinstance(first_candidate, dict):
        raise GeminiCandidateTextError("Gemini candidate is invalid.")
    content = first_candidate.get("content")
    if not isinstance(content, dict):
        raise GeminiCandidateTextError("Gemini candidate has no content.")
    parts = content.get("parts")
    if not isinstance(parts, list) or not parts:
        raise GeminiCandidateTextError("Gemini candidate has no parts.")
    first_part = parts[0]
    if not isinstance(first_part, dict):
        raise GeminiCandidateTextError("Gemini candidate part is invalid.")
    text = first_part.get("text")
    if not isinstance(text, str) or not text.strip():
        raise GeminiCandidateTextError("Gemini candidate has no text.")
    return text


class GeminiCandidateTextError(ValueError):
    pass


def extract_gemini_usage_metadata(decoded: dict[str, object]) -> dict[str, object] | None:
    usage_metadata = decoded.get("usageMetadata")
    if isinstance(usage_metadata, dict):
        return usage_metadata
    return None


def extract_gemini_finish_reason(decoded: dict[str, object]) -> str | None:
    candidates = decoded.get("candidates")
    if not isinstance(candidates, list) or not candidates:
        return None
    first_candidate = candidates[0]
    if not isinstance(first_candidate, dict):
        return None
    finish_reason = first_candidate.get("finishReason")
    if isinstance(finish_reason, str):
        return finish_reason
    return None


def elapsed_ms(started: float) -> int:
    return int((time.perf_counter() - started) * 1000)

