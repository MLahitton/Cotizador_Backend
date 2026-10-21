# Project Diagnosis v1

`GET /api/v2/projects/{projectId}/diagnosis` is an authenticated, derived read model of what the client answered, not a technical recommendation. Read authorization follows Workspace: active owner or active Admin; inaccessible projects return 404. Inactive users receive 403.

Source: only PublicExperienceSubmission linked by ProjectId with Source SNG_WEB. The repository projects SubmissionId, Source, SchemaVersion and AnswersJson, no files or related entities. Zero rows yields HTTP 200 with status NO_PUBLIC_EXPERIENCE and diagnosis null. Two or more yield AMBIGUOUS_PUBLIC_EXPERIENCE, never an arbitrary source. Supported schema: sng-public-experience-v1; formatter: diagnosis-v1. Unknown schema yields UNSUPPORTED_SCHEMA; malformed JSON/non-object/duplicate keys yields INVALID_SOURCE_DATA (200, null diagnosis).

AVAILABLE contains metadata and seven ordered sections: general, priorities, preferences, livingContext, needs, decision, supplementalNotes. Entries contain questionId, displayLabel, availability and typed code/codes/numericValue/textValue/displayValue/displayValues. No Answers object, dynamic dictionaries or domain entities are serialized. nombre and wa are explicitly excluded; asesor is the public answer, not an owner assignment.

The centralized ProjectDiagnosisSchema defines all 37 current question IDs, option labels, conditions and exclusions. Backend owns this version; it does not load frontend sources at runtime. Update the schema and its contract inventory tests explicitly when the public contract evolves. A new public schema must not be silently treated as v1.

Availability:
- AVAILABLE: known valid value.
- NOT_PROVIDED: absent/null/empty answer. An unknown or absent parent does not prove non-applicability.
- NOT_APPLICABLE: a known valid parent proves the condition false; stale child values are not exposed.
- UNKNOWN_CODE: preserve the raw string code, use a safe unknown label. Mixed multi-choice keeps known and unknown codes.
- INVALID_VALUE: wrong shape, invalid scale, duplicate/blank multi-codes, exclusive-option conflict or excessive selections. Do not silently repair inconsistent historical values; no arbitrary JSON is returned.

Multi-choice order follows the versioned option order, then unknown codes ordinally. Scale is the original integer 1-5 displayed as `5 de 5`, not HIGH. UV ns stays ns. Public negro displays Negro, never PP13 or BLACK_MATTE. Date ranges remain ranges. Textareas are plain JSON strings, not HTML; clients must render them as text (no HTML injection).

This GET has no SaveChanges, lazy materialization or persistent cache. It does not invoke Level1/Level2, selectors, finish mapping or AI2, create drafts or fill 5/5. Future suggestion/acceptance layers remain separate. No migration or domain change is required.
