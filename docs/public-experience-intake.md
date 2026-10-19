# Public Experience Intake v1

## Current MVP contract (Block 5E)

The anonymous POST ends after intake -> Client/Project -> PreQuote/Requirement
and RequirementFiles persistence. It does not call the processing adapter, core
or AI2, and does not schedule background work.

201 / RECEIVED means data received, files stored, project created and Requirement
ready for internal review. It does NOT mean AI analysis, a TechnicalProposal or
system selection. New Requirements remain PENDING with zero processing attempts,
extractions and technical proposals. The public response omits processingAttemptId,
processingOutcome, requirementStatus and technicalProposalId.

RECEIVED is a public receipt status. The persisted submission retains its existing
PROCESSED meaning (Client/Project linked); no state machine or schema is changed.
Identical replays return 200 / RECEIVED and the same entity IDs without invoking AI2,
including historical processed, failed or cancelled Requirements. Existing analysis
records are not deleted or changed, and replay never retries analysis.

An authenticated owner can subsequently use the unchanged
POST /api/v2/requirements/{requirementId}/process endpoint. It consumes the existing
RequirementFiles without re-uploading. Existing authorization, locks, failure/retry
rules and the AI2 pipeline remain authoritative. Workspace is initially RESOLVED
with PENDING, no proposal and HasTechnicalProposal=false; after successful manual
processing it exposes PROCESSED, the proposal ID and HasTechnicalProposal=true.
Public Answers remain intact and are not copied into item preferences.

The block notes below describe implementation history; Block 5E supersedes the
synchronous public processing behavior of Block 4C.

POST /api/public/experience-submissions is anonymous. No other route is opened.
Send multipart/form-data with exactly one text part named payload (UTF-8 JSON)
and zero to five file parts all named files, in the same order as payload.files.
Do not send payload as an uploaded file.

Example payload:

~~~json
{
  "submissionId": "external-unique-id",
  "schemaVersion": "sng-public-experience-v1",
  "source": "SNG_WEB",
  "client": {
    "fullName": "Example",
    "whatsapp": "+570000000000",
    "advisorCode": "AD01",
    "advisorDisplayName": "Advisor"
  },
  "project": {
    "name": "Project",
    "city": "City",
    "subdivision": null,
    "lotOrApartment": null,
    "notes": null
  },
  "answers": { "termico": "alto", "score": 2, "choices": ["one", "two"] },
  "files": [{ "fileName": "plan.pdf", "contentType": "application/pdf", "sizeBytes": 123 }]
}
~~~

Unknown properties and duplicate JSON keys are rejected. IDs of internal entities
and storage paths are not part of this contract. Answers may have arbitrary keys
but values must be strings, JSON numbers, or arrays of strings. No business interpretation
or Level1 translation occurs. Answers are retained as a JSONB snapshot (not lexical JSON formatting).
Optional project fields omitted and explicit null are equivalent.

Limits: payload 256 KiB UTF-8, 5 files, 20 MiB per file, 100 MiB total.
The HTTP envelope allows one extra MiB for multipart overhead.
Required strings: submissionId/advisorCode 100, fullName/advisorDisplayName/project.name 200,
whatsapp 50, project.city/subdivision/lotOrApartment 250, notes 1000 characters.
Values are never silently truncated or normalized.
Files must have a plain filename (255 characters max), exact metadata agreement with
multipart (name, MIME, length and order), and actual nonempty byte length matching that metadata.
Supported extension/MIME pairs:
.pdf/application/pdf; .xlsx/application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;
.png/image/png; .jpg and .jpeg/image/jpeg.
Existing requirement upload does not validate binary signatures; this intake likewise
does not assert that MIME/extension proves the file format. No malware scanning is added.

201: {"submissionId":"...","status":"RECEIVED","isReplay":false,"clientId":"...","projectId":"...","preQuoteId":"...","requirementId":"..."}.
200: identical retry with isReplay:true, RECEIVED and the same four entity IDs.
409 ProblemDetails: public ID already exists with different material content.
400 ProblemDetails: invalid contract/file metadata.
HTTP envelope limits can additionally return 413 before the action.

Fingerprint: SHA-256 of canonical UTF-8 payload followed by ordered 32-byte SHA-256
file hashes. Canonicalization uses ordinal-sorted object keys recursively, retains
array order and string contents, and serializes optional absent fields as null.
JSON numbers retain their original numeric token, avoiding precision loss.
Numerically equivalent but differently spelled tokens (2 vs 2.0) conservatively conflict.
Whitespace, JSON escaping and object property order do not affect identity.

Files use existing IFileStorage with server-generated UUID keys:
public-experience/{internalId}/{fileId}/original.ext.
File bytes are not stored in PostgreSQL. A completed retry hashes uploads but writes
no new files. A concurrent race may temporarily write separate keys; the unique
submission_id index decides the winner. The losing request cleans its own keys and
returns replay/conflict after reading the winner. One EF SaveChanges transaction
persists the submission and all file metadata.

Storage/persistence failures and cancellation attempt cleanup with a non-cancelled
token, including the key currently being written. Cleanup failures are logged for
operational recovery. As with existing compensated uploads, abrupt process termination
or an indeterminate DB commit can require operational reconciliation; storage and
PostgreSQL do not share a distributed transaction.

Migrations are generated, not applied to the user's DB by this task.
The public POST persists intake first, invokes Client/Project processing once,
then invokes requirement preparation once. It creates PreQuote + PENDING Requirement
with independent file copies; no AI2 or downstream selection.

## Block 2A: explicit internal processing

ProcessPublicExperienceSubmissionService.ExecuteAsync(publicSubmissionId, cancellationToken)
remains the internal processing service, now invoked by the existing intake POST.
There is no additional endpoint or worker.
A missing submission returns null. Configure through the existing Options pattern:

~~~json
{ "PublicExperience": { "OwnerEmail": "" } }
~~~

Alternatively set PublicExperience__OwnerEmail in the environment. No real email is
versioned. Empty configuration does not prevent startup; processing fails with
PUBLIC_EXPERIENCE_OWNER_NOT_AVAILABLE if the normalized email (Trim/ToLowerInvariant)
is absent, not found, or inactive. No user is created and no alternate owner is tried.
AdvisorCode/AdvisorDisplayName are metadata only, including DIRECT_CONTACT.

For SNG_WEB, ClientType is Person; LegalName and Phone come from the submission.
All other optional Client fields are null, including City. Project uses the new
Client.Id, ProjectName, ProjectNotes, and nonblank trimmed city/subdivision/lot joined
by a middle dot separator. Location over 250 characters fails PROJECT_LOCATION_INVALID
without truncation. Code is WEB- plus the first 24 uppercase hex digits of the
internal submission UUID (28 characters, deterministic, no counter). A code collision
fails transactionally; it is never resolved by reusing another project's code.

One PostgreSQL transaction locks the submission with SELECT FOR UPDATE. The service
transitions RECEIVED (or FAILED on explicit retry) through PROCESSING to PROCESSED.
PROCESSING is transaction-local, not a separately committed work claim.
Client, Project and the submission links commit together. PROCESSED replays return
the original IDs even if owner configuration changes; different submissions create
different clients, with no name/phone deduplication.

A savepoint after acquiring the row lock allows rollback of Client/Project writes
and persistence of FAILED/PUBLIC_EXPERIENCE_PROCESSING_FAILED while retaining that
lock. Cancellation rolls back the transaction and propagates; DB outages that prevent
recording FAILED also propagate rather than claiming success. A commit failure can
be retried: the persisted submission is authoritative.

AddPublicExperienceProcessingLinks adds nullable client_id/project_id (restrict-delete
foreign keys), processed_at_utc and processing_error_code. Generate only; do not apply
to the real DB as part of this task. Client/Project processing itself remains separate
from the requirement preparation described below.

## Block 2B: HTTP processing semantics

RECEIVED is processed; PROCESSED is replayed without creating another pair.
A persisted PROCESSING returns 409 PUBLIC_EXPERIENCE_PROCESSING_IN_PROGRESS without
restarting the processor. Concurrent requests serialize on the existing row lock.
FAILED is safely retryable on a subsequent identical HTTP request, under that lock,
with no existing entity links. There is exactly one processing attempt per request;
there are no automatic retry loops or silent resets.

After intake succeeds, processing errors retain the submission and uploaded files.
ProblemDetails includes submissionId and processingErrorCode:
- PUBLIC_EXPERIENCE_OWNER_NOT_AVAILABLE: 503.
- PROJECT_LOCATION_INVALID: 422.
- PROJECT_CODE_CONFLICT: 409 (unique project-code constraint).
- PUBLIC_EXPERIENCE_PROCESSING_FAILED: 500 (rolled back and recorded failure).
- PERSISTENCE_ERROR: 500 (processing repository exception or missing result).

No stack traces are sent. Conflicting payloads return intake 409 before processing.
OwnerEmail, internal IDs, Project.Code and storageKey are not accepted from public
payloads. Only backend PublicExperience:OwnerEmail determines ownership.
HTTP integration tests run Kestrel against an isolated temporary PostgreSQL database
with both intake migrations, using a controlled file-storage double.

## Block 3A: internal requirement preparation

PreparePublicExperienceRequirementService.ExecuteAsync(publicSubmissionId, cancellationToken)
remains an internal service with no additional endpoint/worker. Block 3B invokes it
from the existing public POST after Client/Project processing succeeds.

Prerequisites: persisted ClientId/ProjectId, an existing active Project with the
same ClientId. Ownership comes exclusively from Project.CreatedByUserId; this phase
does not resolve OwnerEmail, impersonate ICurrentUser or invoke public create services.
PreQuote.Create uses ReserveNextSerialAsync (PC-YYYY-XXXX) and Name=null.
Requirement.Create uses that PreQuote and the same persisted owner.

PUBLIC_EXPERIENCE_REQUIREMENT_COMMERCIAL_LINE_BRIDGE = ESSENTIAL

This is exclusively a legacy technical MVP bridge, not a public commercial choice.
Public answers/advisor/project type do not select CommercialLine. Later Experience
Level2 uses item preferences, not this bridge, to decide CLASSIC/PREMIUM.

Zero documents returns PUBLIC_EXPERIENCE_DOCUMENTS_REQUIRED before reserving a serial
or creating either entity. Errors are persisted in requirement_preparation_error_code,
separate from Client/Project processing_error_code; status remains PROCESSED.
Successful preparation sets pre_quote_id, requirement_id and requirement_prepared_at_utc.
Both links replay unchanged; partial links are rejected explicitly, never duplicated.

The repository holds SELECT FOR UPDATE on the submission through one transaction:
serial reservation, PreQuote, Requirement, RequirementFiles and submission links.
Concurrent processors serialize; the second sees the committed links and replays.
No AI2 client, processing attempt, technical proposal, selection or pricing is invoked.
Workspace can resolve the new PreQuote/Requirement as RESOLVED / PENDING without a
technical proposal, using its existing read service unchanged.

Files are read in intake Sequence order, using the existing Requirement metadata helper
and storage-key generator (requirements/{requirementId}/...). Every original is read
once into a bounded per-file buffer (maximum existing 20 MiB policy), checking exact
length and SHA-256 against intake ContentHash before saving a separate copy.
Mismatch returns PUBLIC_EXPERIENCE_FILE_INTEGRITY_ERROR. Originals are never deleted.
RequirementFile has no Sequence; timestamps increase by one PostgreSQL microsecond
per file so existing CreatedAtUtc/Id readers retain the intake order.

Before COMMIT, exceptions roll back database changes to a savepoint, compensate every
new storage key including partial writes, and persist the preparation error. Cancellation
also compensates, then propagates and rolls back. A later invocation can retry safely.
Cleanup uses CancellationToken.None and logs failures with the exact key for recovery.
As with intake, storage and PostgreSQL are not a distributed transaction: process death,
unavailable cleanup or an indeterminate COMMIT may require operational reconciliation.
In particular, an indeterminate COMMIT retains copies rather than risking deletion of
files already referenced by committed Requirement rows.

AddPublicExperienceRequirementLinks is incremental: nullable links with Restrict FKs,
prepared timestamp and separate error code. Prior migrations are not edited.
It is applied only to an isolated temporary PostgreSQL database in integration tests,
never to the user's real database by this task.

## Block 3B: full HTTP orchestration

The controller orchestrates intake -> Client/Project processor -> requirement
preparation, without reproducing domain mapping or copying multipart bytes again.
It returns success only when preparation succeeds and both new links are present.
No TechnicalProposalId, ProcessingAttemptId or ESSENTIAL is exposed in the response.

An existing intake with Client/Project but no preparation is not an early success:
an identical POST continues preparation and returns 200 with isReplay=true plus all
four IDs. Completed retries do not create entities or new Requirement copies.
The existing row locks serialize concurrent requests in each phase.

Preparation failures use ProblemDetails with submissionId and processingErrorCode:
- PUBLIC_EXPERIENCE_DOCUMENTS_REQUIRED: 422.
- PUBLIC_EXPERIENCE_FILE_INTEGRITY_ERROR: 500.
- PUBLIC_EXPERIENCE_REQUIREMENT_PREPARATION_FAILED: 500.
- Other preparation failures or repository exceptions: 500 (no invented retries).

Status=PROCESSED continues to mean that Client/Project processing succeeded.
If preparation fails, those links and intake originals remain intact, while
RequirementPreparationErrorCode records the failure and RequirementPreparedAtUtc
remains null. The HTTP response is an error, not a successful partial replay.
An explicit later retry may complete preparation; success clears its error.
No state machine change or automatic background retry is introduced.

HTTP PostgreSQL integration asserts one submission/client/project/prequote/requirement,
independent file keys and identical bytes, stable replay IDs, concurrent uniqueness,
missing-document 422, stored-byte integrity 500, and compensation/retry after DB failure.
The existing Workspace service is also exercised as the persisted owner after POST:
RESOLVED with PENDING Requirement and no TechnicalProposal. AI2 and
ProcessRequirementService are not invoked by the public flow. The test host registers
them to assert zero AI calls at receipt and exercise later authenticated processing.

## Block 4B: internal processing adapter (not connected to HTTP)

ProcessPublicExperienceRequirementService loads the persisted submission by its existing
public submission ID and validates Project -> PreQuote -> Requirement links. Its actor
is exclusively Project.CreatedByUserId; no current user, owner email or advisor mapping
is consulted. Processing delegates to the single ProcessRequirementCoreService pipeline.

Only Pending starts processing. Processing returns AlreadyProcessing; Processed returns
the existing current proposal without AI2, or an explicit missing-proposal inconsistency.
Failed returns PROCESSING_FAILED plus the latest attempt error without automatic retry.
Cancelled/Superseded are terminal. RequiresReview is successful, including review
introduced by the TechnicalProposal builder. Core failures retain their original result.
No duplicate processing state is stored on the submission.

The former active-attempt unique index alone could not protect a stale Pending decision
after another invocation completed. A shared PostgreSQL session advisory lease now covers
the status read and complete core execution. Both authenticated processing and this adapter
use it; the adapter passes its held lease into the core, avoiding double acquisition.
Contenders return AlreadyProcessing without AI2. Tracked requirements are reloaded after
acquisition. A dedicated nonpooled connection releases the lease on disposal, including
exceptions; no business transaction spans AI2. This coordinates backend instances using
the same database, without an in-memory-only lock or a new schema object.

The normal authenticated flow still permits explicit reprocessing after completion.
The stricter replay/failed behavior belongs only to the public adapter. Integration tests
exercise independent PostgreSQL lock connections with the real core and mocked AI/storage
repositories, plus the existing HTTP PostgreSQL regression suite. The public POST remains
unchanged and ends with a Pending Requirement. No worker, HTTP processing contract or
migration is introduced by this block.

## Block 4C: synchronous HTTP processing (historical, superseded by Block 5E)

The public POST now awaits intake -> Client/Project -> preparation -> the internal
processing adapter. There is no worker, Task.Run or fire-and-forget operation.
Success returns 201 for a new intake and 200 for an identical replay, including
requirementStatus, processingAttemptId, processingOutcome and technicalProposalId.
The existing status field still belongs to the submission, not the Requirement.
RequiresReview is successful; review indicated by the persisted proposal is included
even if extraction itself completed without review. The same read applies to replay.

The authenticated and public controllers share ProcessRequirementProblemMapper:
AI timeout -> 504; unavailable/rejected/invalid/service error -> 502;
storage/persistence -> 500; processing active/cancelled -> 409.
Public ProblemDetails adds the submission/project/prequote/requirement/attempt IDs,
processingErrorCode and requirementStatus without raw extraction or stack traces.
A Failed replay maps its persisted attempt error without retrying AI2. Invalid links,
missing preparation and a Processed requirement without a proposal return 500.
The adapter reloads the Requirement after the core, because failure finalization
clears EF tracking and the prior entity can retain an obsolete Processing status.

HTTP PostgreSQL tests use real repositories, core and proposal builder with an AI2
mock, fake storage and empty catalog/history providers in a dedicated temporary DB.
They verify entity counts, single attempts/extractions/proposals, simultaneous POSTs,
partial Pending replay, review, failure replay and Workspace PROCESSED/resolved.
Experience read services remain available with zero answers per item and pending
system/glass resolution; public answers remain submission context only.

No migration or public-answer mapping to Level1/Level2 is introduced. Existing
preparation, ownership, catalog rules and the AI2 pipeline are unchanged.
