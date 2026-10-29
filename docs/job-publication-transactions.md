# 2E.2C.2b: explicit preparation and protected publication

No DI registration, endpoint, worker, provider extraction call or productive caller
is added. The existing publication contract/version/hash coverage is unchanged.

## Verified behavior and approved policy

RequirementExtractionMapper is the legacy transformation moved verbatim (including
evidence grouping, invalid-location warning/review, first segment evidence and
fallback sequence). The core only delegates; it never calls the new preparer/store.
PrepareJobPublicationService consumes an already obtained canonical response and
explicit execution/document context. It sorts file metadata by CreatedAtUtc, Id,
checks the explicit primary, constructs transient extraction/items and calls the
existing BuildAsync, never ExecuteAsync. Catalog/history/similarity work happens
before publication, with no control transaction opened by this service. Builder
failure propagates without saving or failing an execution. Future callers must not
wrap preparation in their own control transaction.

JobPublicationGraph explicitly maps every prepared initial field in both directions.
The publication store only receives PreparedJobPublication, not tracked entities.
Transient preparation GUIDs/timestamps are discarded; PostgreSQL supplies publication
creation/closure time. No ranking, default, eligibility or mapping is added. Existing
builder defaults remain existing suggestions, not approved public mappings.

The audited start recognizer allows Admin and requires active Client/Project;
termination has operation-dependent activity rules. This block does not alter them.
The explicitly requested NEW publication policy is owner-only with active user,
Client and Project. Its historical recognizer is owner-only with active user, without
Client/Project activity, current-Requirement or live-lease requirements. This policy
is internal and is not a change to existing HTTP read permissions.

## Transaction protocol

Each call uses a fresh context and explicit transaction. Discovery is no-tracking;
links are revalidated after ordered locks: optional submission FOR SHARE, actor
FOR SHARE, Client FOR SHARE, Project FOR SHARE, PreQuote FOR UPDATE, Requirement
FOR UPDATE, detached job FOR UPDATE, attempts ordered by Id FOR UPDATE. File rows
are FOR SHARE and compared to the prepared manifest for NEW publication only.
CommercialLine and explicit primary must match. These locks coordinate participants
in this protocol; they do not certify protection from legacy writers or phantoms.

Identity includes job, execution request, Requirement, exact linked attempt and lease
generation; correlation comes from the job and must match the attempt. New publication
also requires Processing/current Requirement, Running job, Processing attempt,
AttemptCount=1, no competing active attempt, no cancellation and an exact live lease.

The transaction inserts extraction/items/evidence/segments/proposal/alternatives/history,
finishes the existing attempt, marks Requirement Processed and registers PreQuote
activity. Catalog IDs are not reselected; FK failure rolls everything back.
After SaveChanges, the only remaining write is the guarded job UPDATE: exact identities,
Running, owner/generation, attempt count, no cancellation/receipt, old expiry greater
than clock_timestamp(). Exactly one row is required. No SaveChanges follows the guard.
The guard, not the physical commit, is the lease-validity decision point.

## Recognition and uncertain commits

Succeeded requires matching persisted version/hash, the exact finished attempt and
correlation, matching outcome/closure and corresponding extraction/proposal identities
and creation timestamps. Current commercial values are not compared. Historical
receipt absence or inconsistent closure is Indeterminate, never adopted/repaired.
Different content is ResultMismatch. Failed/Cancelled are terminal conflicts.
Foreign users (including Admin) receive NotFound and no receipt.

Before commit, errors roll back and cancellation propagates without business cancel.
After CommitAsync is attempted, any exception triggers a fresh locked observation
with an independent ten-second cancellation budget. Only exact terminal evidence
returns AlreadyPublished; all other observations return Indeterminate. No mutation,
provider, preparation or transaction retry and no legacy compensation occurs.
Ten seconds is not a total response-time guarantee.

## Validation and remaining limits

PostgreSQL tests use real transactions and separate connections/PIDs. Race barriers
pause after a guarded write and observe the competing lock request before releasing
the first call. Expiry tests observe PostgreSQL time without modifying the locked job
from another connection. Interceptors inject post-save errors and commit-boundary
exceptions; they do not simulate an actual network failure. Independent reads compare
full graph rows and operational state after rollback/replay.

2E.2C.3 retains combined verification. 2E.2D must coordinate legacy writers and stable
document content before activation. Manifest identity/metadata cannot prove byte
equality. No document bytes are read here. jsonb may change numeric spelling; historical
recognition relies on the persisted receipt, never reconstruction from jsonb or edited
proposals. No new migration, recovery, retry or deployment is part of this block.
