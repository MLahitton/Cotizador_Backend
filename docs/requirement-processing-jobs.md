# Requirement processing jobs 2E.1

Subsequent document coordination is specified in
[Document coordination 2E.2D.2b](document-transaction-coordination.md), including
the approved rule that any job history blocks cancellation of the Requirement itself.
Its verification record is separate from the historical results below.

The job records an execution request independently of RequirementStatus and the existing RequirementProcessingAttempt. This block is persistence and local domain validation only: no enqueue caller, worker, endpoint, heartbeat, recovery, automatic retry or historical backfill exists.

## Identity and local lifecycle

ExecutionRequestId is a nonempty globally unique UUID retained after termination. CorrelationId remains a UUID for tracing, not idempotency. SubmissionId and RequestedByUserId are optional real foreign keys; neither authorizes execution. Multiple jobs may reference one submission.

Triggers are PublicPreparation, ManualInitial and ManualRetry. Only ManualRetry requires RetryOfJobId. The future coordinator must verify the predecessor is an eligible terminal of the same Requirement (never Succeeded); the entity does not load other records.

Create produces Queued. AssignExecution produces Running, sets StartedAtUtc and lease generation 1 without starting an attempt. LinkStartedAttempt sets AttemptCount to 1; the same link is idempotent, another is rejected. No reassignment or second attempt is supported. A retry creates another job and execution request identity.

RequestCancellation terminates Queued without inventing a start. In Running it records the first request and blocks linking, success and ordinary failure. ConfirmCancellation finishes that request. Success requires an attempt. Failure can precede the attempt but requires a safe symbolic LastErrorCode (uppercase ASCII letters, digits, underscore, starts with a letter, maximum 64). No provider messages or exception text are accepted.

Terminal jobs release lease owner/expiration and preserve generation, attempt/count and timestamps. They cannot reopen. Dates must be UTC and no earlier than creation, assignment or a recorded cancellation request. Lease owner is at most 200 characters; expiration must follow assignment. These are local consistency checks, not proof of current execution authority.

## Database guarantees

All five foreign keys use RESTRICT, without changing legacy relationships. Checks protect identity, trigger/retry shape, count/link, dates, errors and lifecycle combinations.

Indexes:
- Unique ExecutionRequestId: identity survives terminal states and is global across Requirements.
- Unique ProcessingAttemptId: an attempt belongs to at most one job; nulls remain allowed.
- Partial unique RequirementId for Queued/Running: one active job.
- Unfiltered RequirementId: terminal history queries (separate named EF index).
- State/CreatedAtUtc/Id: future ordered work selection, no claim implemented.
- Conventional SubmissionId, RequestedByUserId and RetryOfJobId indexes: FK checks and reference traversal, not uniqueness.

Migration 20261022005851_AddRequirementProcessingJobs is additive; Up creates only the new table/constraints/indexes, Down drops only that table. It inserts no jobs or historical repairs.

## Boundaries for 2E.2

A future command must compare replay data (Requirement, trigger, submission, actor and retry reference), return the existing job even if terminal, reject conflicting reuse and preserve the stored correlation. A uniqueness exception alone is not an HTTP idempotency implementation.

The coordinator must enforce ownership/activity, matching Requirement/attempt/retry, DB-time lease validity and expected generation, cancellation races and atomic start/publication. No job method changes a Requirement, attempt, document, extraction or TechnicalProposal. Succeeded is a processing outcome, not commercial acceptance.

At the 2E.1 boundary, the legacy synchronous /process, two-step attempt start, in-memory cancellation and LocalFileStorage remained unchanged. See the 2E.2D.1 section below for subsequent legacy coordination. Worker deployment, shared storage, lease durations, recovery policy and asynchronous HTTP semantics remain undecided.

## Verification

Domain tests cover transitions, immutable identities, UTC/order, cancellation repetition, attempt counting, safe errors and terminal rejection. RequirementProcessingJobPostgreSqlTests uses the existing isolated PostgreSqlIntegrationFixture and real migrations. Concurrent writers open independent connections and rendezvous using TaskCompletionSource, with no sleep-based synchronization; the losing writer must name ux_processing_jobs_active_requirement and SQLSTATE 23505.

PublicExperienceHttpPostgreSqlTests validates the real multipart intake/preparation before and after replay: stable links, Pending Requirement, no jobs or attempts and a controlled AI2 client with zero invocations. No Gemini call is needed.

Run tests with COTIZADOR_TEST_POSTGRES_ADMIN_CONNECTION_STRING targeting the administrative postgres database of an isolated test server, following Tests/README.PostgreSql.md. The fixture creates and drops only cotizador_backend_test_* databases. Missing configuration causes explicit skips, not passing database coverage.

## 2E.2D.1 legacy coordination

Any job history for the exact RequirementId rejects legacy start, success, failure and cancellation with
PROCESSING_MANAGED_BY_JOB (HTTP 409). This is not AlreadyProcessing. Replacement identity does not inherit
the old Requirement's veto; authorized reads and commercial edits are not globally blocked.

RequirementRepository.BeginLegacyWriteAsync clears stale tracking and opens a short transaction. It locks
User, Client and Project FOR SHARE, then PreQuote and Requirement FOR UPDATE, then attempts in ID order.
It checks active actor and current ownership before querying history. Start also requires active Client/Project;
cancellation retains its existing inactive-context policy. Admin does not bypass ownership. Finalizers receive
the explicit originating actor (or the cancellation caller), never a job's RequestedBy. A missing actor is rejected.

ProcessRequirementCoreService checks before acquiring its advisory lease and rechecks in the actual start transaction.
That transaction inserts and starts one attempt, updates Requirement/activity, saves once and commits. There is no
persisted Pending intermediate and no document/provider/similarity work under this transaction. Success preparation
remains outside locks; publication rechecks the exact attempt, current state and latest identity, then commits graph,
attempt, Requirement and activity together. Repository failure/cancellation do the corresponding guarded closure.
Controlled rejections cannot fall through to a compensating finalizer. Incompatible late calls cannot mutate a successor.

Cancellation preserves the initially selected attempt ID, reauthorizes under locks, finalizes with another guarded
transaction and only then calls TryCancel using RequirementOperationKeys.ProcessingAttempt(id). No DB lock is held
during callbacks. Unknown commit/disposal outcome returns existing PersistenceError (HTTP 500), never false success,
AlreadyProcessing, provider execution, automatic retry or a compensating write. This is fail-closed, not automatic recovery.

### Future enqueue obligation (not implemented)

The enqueuer must use compatible lock ordering and the same Requirement FOR UPDATE lock. Under that lock it must
reject any legacy Pending or Processing attempt before inserting a job. When legacy start wins, enqueue rejects;
when job insertion wins, legacy rejects without an attempt. The test-only enqueue fixture is not a production service.
Arbitrary SQL writers that ignore this protocol are not excluded by a mere absence query or by the advisory lock.
The finalization guard still rejects if such a writer inserts history during provider execution.

### Persistent builder authorization

The earlier implementation exposed an authorization gap in the persistent builder. Its correction was deferred
pending a decision; that was NOT a commercial rule permitting writes without an actor. The completion request
now explicitly authorizes `ExecuteAsync(Guid requirementId, Guid actorUserId, CancellationToken cancellationToken = default)`.
There is no actorless overload. Empty actor is InvalidRequest; missing/inactive user is rejected; foreign owner,
including Admin, receives RequirementNotFound before job history is queried. Client and Project must be active.
Actor is never inferred from historical authors, project ownership or RequestedByUserId.

Inventory: this is a concrete service, with no builder interface. Its scoped DI registration remains unchanged.
No production caller of ExecuteAsync was found across tracked/untracked sources. ProcessRequirementCoreService
and PrepareJobPublicationService use BuildAsync only. Test constructors in ProcessRequirementServiceTests and
PrepareJobPublicationTests continue unchanged; new LegacyBuilderPostgreSqlTests explicitly pass the real fixture actor.
The existing job-publication-contract.md reference describes BuildAsync and still applies. Any future internal
ExecuteAsync consumer must supply its authenticated/authorized actor and handle LegacyProcessingRejectedException;
the absence of present consumers is not itself protection. No endpoint or HTTP success contract was changed.

ExecuteAsync first authorizes/reads using the shared short legacy scope, releases it, and prepares via BuildAsync.
Catalog/history/similarity work occurs outside writer locks. A second scope reauthorizes and checks any job history,
then revalidates commercial line, latest successful extraction, exact finished successful attempt and extracted item
identities/ownership. This does not add a Requirement Processing precondition. Existing proposal for that attempt
is rejected with controlled PersistenceError, never regenerated or upserted over commercial edits.
Under the same locks/transaction, fresh source items are attached as existing rows and only the new proposal graph
is added, saved and committed. The new repository method is separate from AddTechnicalProposal used by the core.
No pre-lock tracked graph is authority; the scope clears tracking. Unknown commit remains PersistenceError with
no retry or compensation. BuildAsync remains unchanged, callable with job history and non-persistent.

### Consumers and remaining boundary

Inspected Cotizador_Frontend use-requirement-workspace.ts and use-project-requirement-analysis.ts plus
project-requirement-analysis.ts: only REQUIREMENT_PROCESSING_ALREADY_ACTIVE enters the active/polling path;
the new conflict uses the generic error path without automatic retry. This is source inspection, not browser runtime
validation. Public adapter readonly recognitions remain unchanged; attempted legacy processing propagates the new code.
Unknown consumers are not certified. Document bytes, storage cleanup and coordination with other document writers
remain 2E.2D.2 work. The prior ReplaceDocumentAsync no-inline-delete correction is preserved.

### Change inventory for this block

Production changes (paths relative to this repository):

- `Application/Common/Abstractions/PreQuotes/IRequirementLegacyWrite.cs`: new guarded transaction scope/result exception.
- `Application/Common/Abstractions/PreQuotes/IRequirementRepository.cs`: scope and actor-bearing finalization contract.
- `Infrastructure/Persistence/Repositories/RequirementRepository.LegacyWrite.cs`: authorization, locks, history guard and commit boundary.
- `Infrastructure/Persistence/Repositories/RequirementRepository.cs`: exact-attempt failure/cancellation, atomic activity.
- `Application/PreQuotes/ProcessRequirement/ProcessRequirementCoreService.cs`: atomic start and success, controlled catches.
- `Application/PreQuotes/ProcessRequirement/CancelRequirementProcessingAttemptService.cs`: authorization/guard before exact-attempt signal.
- `Application/PreQuotes/ProcessRequirement/ProcessRequirementResult.cs`: distinct managed-by-job failure.
- `Application/PublicExperience/ProcessPublicExperienceRequirementService.cs`: controlled conflict propagation.
- `Api/ErrorHandling/ProcessRequirementProblemMapper.cs`: 409 mapping.
- `Api/Controllers/RequirementsProcessingController.cs`: cancellation mapping and 409 metadata.

Tests: updated ProcessRequirementServiceTests, ProcessRequirementCoreServiceTests,
ProcessPublicExperienceRequirementServiceTests, CancelRequirementProcessingAttemptServiceTests,
RequirementProcessingCancellationHttpTests and the actor argument in RequirementRepositoryTests' failure-finalization case.
New LegacyProcessingHttpTests and LegacyJobExclusionPostgreSqlTests cover routing, real transactions, both lock orders,
exact-attempt cancellation/successor, replacement identity and commit response loss. This document is also updated.
The prior extraction mapper delegation, job stores/migrations, intake assertions and file-safety correction are preserved.

### Earlier partial verification (2026-10-22)

Earlier final build: `dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --verbosity minimal`:
0 errors, 110 warnings. No warning names the new LegacyWrite scope or new legacy test files.
Full output: `%TEMP%/sng-legacy-final-build.log`. Earlier build logs are sng-legacy-build.log,
sng-legacy-build2.log and sng-legacy-build3.log. That run preceded the completion changes documented below.

| Run | Passed | Failed | Skipped | Interpretation |
|---|---:|---:|---:|---|
| Initial unit run | 202 | 2 | 1 | Old three-save expectation and old second-save abort fixture; corrected for atomic start. PostgreSQL unavailable for one case in that run. |
| First isolated focal run | 218 | 0 | 0 | Real PostgreSQL, including previously skipped case. |
| Expanded regressions | 494 | 11 | 0 | Includes existing job protocol suites, HTTP intake/replay, file safety and full RequirementRepositoryTests. Not a clean suite. |
| Final focused verification | 264 | 0 | 0 | Latest compiled sources; 20 LegacyJobExclusion cases, core/wrapper/public adapter, cancellation, routing, preparation, file safety and intake/replay. |

Runs overlap and must not be summed. The final run supersedes overlapping earlier cases. The expanded run's
11 failures are all in RequirementRepositoryTests: 3 fixtures violate the single-active-Requirement index,
3 omit referenced finish catalog rows, and 5 create duplicate proposal item sequences. They fail in setup outside
the new guarded operations. Those fixtures and corresponding constraints were not changed here; no separate
baseline checkout was executed, so this is not a claim of independently reproduced baseline results.
They were uncorrected in that partial run; the completion below diagnoses and fixes their preparation.
Full original failing names and stack traces are in broad.trx/broad-tests.log.

Final race evidence: legacy-first PID 27296 held the winner transaction while PID 33908 waited; job-first PID
23608 held it while PID 35684 waited. Each test required PostgreSQL Lock plus pg_blocking_pids identifying the
winner before release, then independently checked committed rows. The enqueue participant is test-only.
Commit-loss tests inject exceptions before commit and after actual commit; they are not physical crash tests.
Cancellation callback reenters persistence after lock release, starts a successor and confirms only the old key
is signalled. Late old failure/cancellation preserve the successor. No real provider was invoked.

Final command: `dotnet test Tests/CotizadorBackend.Tests.csproj --no-build --filter <below>`
with `--logger trx;LogFileName=verification.trx` and a temporary results directory:

```text
FullyQualifiedName~LegacyJobExclusion|FullyQualifiedName~ProcessRequirementServiceTests|FullyQualifiedName~CancelRequirementProcessingAttemptServiceTests|FullyQualifiedName~RequirementProcessingCancellationHttpTests|FullyQualifiedName~RequirementRepositoryTests.FinalizeProcessingFailureAsync|FullyQualifiedName~PrepareJobPublicationTests|FullyQualifiedName~ReplaceDocumentFileSafety|FullyQualifiedName~ManageRequirementDocumentsServiceTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict
```

The expanded filter used the full RequirementRepositoryTests class and additionally selected JobProtocol,
JobPublication, AssignedJobStart and JobTermination. Its log is broad-tests.log, result broad.trx.
Initial unit output is `%TEMP%/sng-legacy-unit.log` and `%TEMP%/sng-legacy-results/legacy-unit.trx`.

Isolated PostgreSQL 17 root:
`C:/Users/USUARIO/AppData/Local/Temp/sng-legacy-pg-2e944a69da38447a8ede846fb2c77f3e`.
Loopback port 55446, temporary audit user, fixture-created/dropped cotizador_backend_test_* databases.
Logs: tests.log, broad-tests.log, verification-tests.log; server.log, final-server.log, verification-server.log;
results/legacy.trx, results/broad.trx, results/verification.trx. Server stopped in finally for each run;
final pg_ctl status explicitly reports no server running. Connection variable was child-local and removed.
No usual database, AI2/Gemini, new migration, automatic processing, commit, push, merge or deployment.
Existing migrations were applied only by fixtures to isolated test databases.

Branch/HEAD preserved: feature/public-experience-intake / 56ace9c3845d2e3327a1ca811c27f8e1562e5a79.
Baseline 5 tracked modified, 44 untracked, 0 staged; final 18 tracked modified, 48 untracked, 0 staged.
Existing changes were retained. git diff --check passes (Git reports LF/CRLF conversion warnings).

Earlier verdict: **2E.2D.1 NOT CLOSED**, pending builder protection and expanded regression resolution.
The completion below supersedes that verdict only after its final verification. Neither stage authorizes
job activation or certifies document-content stability.

### Completion: isolated diagnosis of eleven failures

Before fixture edits, the ten failing methods (eleven cases) were run in ten separate test processes against    
the isolated server, using the prior compiled assembly. All eleven failed in isolation, zero passed/skipped.
This rules out a dependency on other test classes for these failures. There is no independently established
pre-block snapshot: HEAD excludes relevant uncommitted work and was not used as a substitute baseline.
These are demonstrated invalid fixtures against the current domain/schema, not claims of historical provenance.

All test names below have the full prefix
`CotizadorBackend.Tests.Infrastructure.Persistence.RequirementRepositoryTests.`.
Locations refer to the source at isolated reproduction, before fixture edits; TRX files retain full stacks.

| Test | Original purpose | Observed failure / point | Phase | Constraint or exception | Demonstrated cause | Correction |
|---|---|---|---|---|---|---|
| FindProcessingAttemptByIdAsync_ReturnsPersistedAttempt | Find exact persisted attempt among other attempts | Save line 210 | Arrange | PG 23505, ix_requirements_pre_quote_id_is_active | Two active Requirements on same quote | Valid historical/current replacement pair on the SAME quote; retain all attempts and assertions |
| Requirements_WithSamePreQuote_CanPersistMultipleRows | Multiple rows for one quote | Save line 63 | Arrange | PG 23505, ix_requirements_pre_quote_id_is_active | Both rows active | Historical/current pair, retain count 2 and add active count 1 |
| ListFilesByRequirementIdAsync_ReturnsOnlyRequirementFiles | Filter files by exact Requirement | Save line 111 | Arrange | PG 23505, ix_requirements_pre_quote_id_is_active | Two active Requirements on same quote | Historical/current pair on same quote; preserve files, query and assertions |
| FindTechnicalProposalForUpdateAsync_LoadsSegmentsUsedByConfirmationReadiness (GRILLE, FIXED, FIXED) | Loaded segment controls readiness | SeedSegmentedTechnicalProposalAsync save line 774 | Arrange | PG 23503, FK_requirement_technical_proposal_items_finish_types_suggested~ | Random nonexistent finish, glass and system IDs | Persist explicit TEST_SYSTEM/TEST_GLASS/TEST_FINISH references; functional type FIXED; retain readiness assertion |
| FindTechnicalProposalForUpdateAsync_LoadsSegmentsUsedByConfirmationReadiness (FIXED, CASEMENT, CASEMENT) | Loaded segment controls readiness | Same helper/save | Arrange | Same PG 23503 FK | Same invalid references | Valid test catalog references, functional type CASEMENT; preserve segment and readiness assertions |
| FindTechnicalProposalForUpdateAsync_WithNoSegments_UsesExistingFallbackBehavior | No-segment fallback blocks incompatible GRILLE/FIXED | Same helper/save | Arrange | Same PG 23503 FK | Same invalid references | Valid test catalog references with FIXED; preserve no segments and blocked readiness |
| PricingSnapshotUpdate_WithConcurrentTransactions_SerializesGrandTotalRecalculation | Serialize concurrent pricing totals | SeedPricedRequirementAsync AddItem line 816 | Arrange | InvalidOperationException: duplicate proposal sequence | Both helper-created proposal items have sequence 1 | Pass sequence 2/reference B for second item; unchanged pricing operations/assertions |
| ReplacePricingSnapshot_WithNewTechnicalProposal_ReusesCurrentRequirementSnapshotRow | Reuse snapshot row for new proposal | Same helper/AddItem | Arrange | Same duplicate sequence exception | Same helper duplication | Same valid sequence correction; snapshot replacement assertions unchanged |
| RequirementExperienceDrafts_ReplacingAnswerForSameBenefit_UpdatesExistingRowWithoutUniqueViolation | Update answer, retain ID and revision | SeedExperienceTechnicalProposalAsync AddItem line 1057 | Arrange | Same duplicate sequence exception | Two source items but proposal helper hardcodes sequence 1 | Second proposal item gets sequence 2/reference V-02; answer assertions unchanged |
| RequirementExperienceDrafts_RoundTripThroughPersistenceAndKeepItemsIndependent | Independent drafts for separate items | Same helper/AddItem | Arrange | Same duplicate sequence exception | Same helper duplication | Same correction; independent draft assertions retained |
| RequirementExperienceDrafts_WithDuplicateItemCreation_IsRejectedByPersistence | Reject second draft for SAME item | Same helper/AddItem | Arrange | Same duplicate sequence exception, before intended Act | Duplicate proposal sequence is NOT this test's intended duplicate draft | Fix proposal sequence only; retain duplicate draft for same item and exact RequirementPersistenceException assertion |

No test was removed, skipped or excluded; no constraint, business rule or expected behavior was weakened.
Catalog entries are test references, not new product recommendations. Historical pairs use domain lifecycle
operations, not a different quote or SQL bypass. Source fixture failures do not arise from repository tracking:
five throw in domain AddItem before persistence; the other six name the violated database constraints.

### Completion verification

Completion edits are limited to the builder service, IRequirementRepository, RequirementRepository,
RequirementRepositoryTests, new LegacyBuilderPostgreSqlTests, and this document. Earlier changes remain intact.

The builder adds 25 real PostgreSQL cases: owner success in Pending/Processed/Failed; six authorization denials
including foreign Admin before history; all five history states plus pure BuildAsync; revalidation after a job,
inactive user/project, newer extraction or changed item identities; save rollback; commit failures before and
after commit; preservation of commercial override, revision and non-null domain-valid confirmation; and two
barrier-controlled races. The new confirmation fixture exercises domain confirmation, not service readiness;
the latter remains covered by the existing combined protocol tests.

Both races require distinct PIDs, PostgreSQL Lock and pg_blocking_pids before releasing the winner, then read
committed rows from independent contexts. One holds the builder's saved graph transaction; the other inserts
a job under compatible legacy locks while BuildAsync is paused, then observes the builder's final transaction
blocked before it rejects. The job insertion is a test fixture only, not an enqueuer. Zero-write assertions count
INSERT/UPDATE/DELETE, including rolled-back commands, and compare independent operational/full-graph snapshots.
All test transactions and pending tasks are disposed/drained in finally. Commit-response loss is injected,
not a physical network/process crash.

Preliminary completion results (overlap; do not sum):

| Run | Passed | Failed | Skipped | Interpretation |
|---|---:|---:|---:|---|
| Eleven failures, isolated before corrections | 0 | 11 | 0 | Ten processes, eleven cases; isolated-1 through isolated-10 TRX/logs |
| First repository/builder run | 31 | 9 | 0 | Repository class 16/16 passed, including all eleven; builder 15/24 passed |
| Second repository/builder run | 41 | 0 | 0 | Repository 16 and strengthened builder 25; second.trx |

The first builder run exposed seven failures caused by EF trying to insert existing extracted items through
the proposal navigation. The focal production correction attaches only freshly reread sources as existing
rows via AddTechnicalProposalForExistingExtraction; it does not change BuildAsync or the core's graph insertion.
Historical provenance of that tracking defect is not asserted. Two other failures were test injections running
twice because both builder and historical resolution read the system catalog; injections now occur once.
The rollback test now explicitly verifies its save-failure injection ran, not merely that some save failed.
Initial compilation had four test errors (method arity, callback type, qualified namespace and asynchronous
assertion analyzer), then one remaining analyzer error; all were corrected before execution. Logs remain intact.

Final source compilation:
`dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental --verbosity minimal`:
0 errors, 110 warnings, full log `%TEMP%/sng-legacy-close-final-build.log`.
Earlier build logs are sng-legacy-close-build.log and sng-legacy-close-build2.log through
sng-legacy-close-build5.log in the same temp directory.
Incremental test builds reported 92 warnings; this is a compilation-scope difference, not a warning fix claim.
No diagnostic names the builder service, new builder tests, IRequirementRepository or RequirementRepositoryTests.
RequirementRepository's existing nullable diagnostics remain outside the added methods.

Final expanded command (same test project, `--no-build --logger "trx;LogFileName=final-expanded.trx"`,
temporary `--results-directory`), with this single-line filter:

```text
FullyQualifiedName~LegacyBuilderPostgreSqlTests|FullyQualifiedName~LegacyJobExclusion|FullyQualifiedName~ProcessRequirementServiceTests|FullyQualifiedName~CancelRequirementProcessingAttemptServiceTests|FullyQualifiedName~RequirementProcessingCancellationHttpTests|FullyQualifiedName~RequirementRepositoryTests|FullyQualifiedName~PrepareJobPublicationTests|FullyQualifiedName~ReplaceDocumentFileSafety|FullyQualifiedName~ManageRequirementDocumentsServiceTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict|FullyQualifiedName~JobProtocol|FullyQualifiedName~JobPublication|FullyQualifiedName~AssignedJobStart|FullyQualifiedName~JobTermination|FullyQualifiedName~StartAssignedJob|FullyQualifiedName~PublicationModelCoverageTests
```

The filter includes the previous expanded selection, not just its green subset. Core, public adapter and
LegacyProcessingHttpTests are partial declarations of ProcessRequirementServiceTests and are included.
Final expanded result: **543 passed, 0 failed, 0 skipped**, duration 16 m 35 s. No production/test source
changed after the final full build. Documentation alone was finalized after execution. TRX comparison confirms
all 505 cases from the previous expanded run are present, with zero missing. All eleven rows in the diagnosis
table now have final outcome **Passed**. The complete repository class is 16/16 and the builder class 25/25.
The final set also includes additional start/model checks. These counts overlap earlier runs and must not be summed.

Final race evidence: builder winner PID 35288 / competitor 31096; job-fixture winner PID 26192 / builder final
transaction 31572. Both tests observed server Lock blocking and the named blocker before release. No mixed,
duplicate or partial proposal was observed. The second run's PIDs (30492/28664 and 22852/30436) are earlier
overlapping evidence, not additional scenarios.

### Completion artifacts and integrity

Isolated PostgreSQL 17 cluster:
`C:/Users/USUARIO/AppData/Local/Temp/sng-legacy-close-1237d3bb7c3c4f509a8b9c431c4fb156`.
Loopback port 55447, temporary audit user. Only fixture databases named cotizador_backend_test_* were used.
The connection environment variable was child-local and removed in finally; every run stopped its server in
finally. A final temporary startup for SELECT against pg_database reported **0 remaining test databases**;
the server was stopped again and pg_ctl status reported **no server running**. Cluster files/logs remain as evidence.

Artifacts under that root: isolated-1.log through isolated-10.log and matching results/isolated-N.trx;
preflight.log/results/preflight.trx; second.log/results/second.trx;
final-expanded.log/results/final-expanded.trx; isolation-server.log, preflight-server.log, second-server.log,
final-server.log and cleanup-verification-server.log. Full build logs are in %TEMP% as named above.
Earlier partial-run artifacts remain in their separately documented root and are not represented as final evidence.

Branch/HEAD unchanged: feature/public-experience-intake / 56ace9c3845d2e3327a1ca811c27f8e1562e5a79.
Completion baseline: 18 tracked modified, 48 untracked, 0 staged. Final: **19 tracked modified, 49 untracked,
0 staged**. The new tracked modification is the builder; the new untracked file is LegacyBuilderPostgreSqlTests.
The other four completion-edited files already had changes/untracked status. Existing unrelated work was preserved.
git diff --check passes, with Git's LF/CRLF conversion warnings. No new migration, fingerprint change,
document lifecycle/replacement change, worker, enqueuer, activation, retry or recovery implementation.
No normal database, real AI2/Gemini call, commit, push, merge or deployment.

Remaining limits: frontend verification is the earlier static inspection, not browser execution. No unadapted
arbitrary writer, immutable document bytes, physical crash or real provider behavior is certified here. Document
mutations, orphan-file handling and byte stability remain 2E.2D.2. Unknown external consumers are not certified;
there is no identified internal persistent-builder consumer left without an actor. No regression in the requested
expanded selection remains unresolved; this is not a claim that every repository test suite was executed.

Verdict: **APTO para revision de cierre de 2E.2D.1**. Builder authorization/protection and the eleven diagnosed
fixture failures are resolved and final affected regressions pass. This result does not authorize automatic processing.

### Closure supplement: explicit public adapter actor and source preservation

The subsequent closure audit rejected the inferred actor in the public adapter and required complete source
snapshots for successful builder writes. This supplement addresses those two points without extending D.2.
The previous 543-case result is historical evidence, not verification of the changed adapter.

`ProcessPublicExperienceRequirementService.ExecuteAsync(string submissionId, Guid actorUserId,
CancellationToken cancellationToken)` now requires an explicit actor. There is no actorless overload.
The service uses IIdentityRepository to reject empty, missing or inactive actors, then checks current project
ownership before acquiring the processing lease or disclosing Requirement state. A foreign Admin has no bypass.
Public links must be coherent before any operational metadata is returned. Authorization failures return no
project, quote, Requirement, attempt, proposal, status or outcome metadata. The core receives actorUserId unchanged;
its authorization failures also discard the adapter's earlier context. A rejection never invokes a finalizer.

This is a trusted internal argument, not an HTTP identity credential. The concrete service retains its existing
scoped DI registration; there is no interface or identified production caller. Tests supply their explicit fixture
actor. Future consumers must obtain the actor from their authenticated/authorized context. No endpoint was added,
and anonymous intake/preparation were not changed or connected to this operation.

Authorized read recognitions remain available without imposing new Client/Project activity gates. New execution
still requires the core's active-context checks and transactionally revalidated authorization; the adapter's read
check is not a replacement for those locks. PROCESSING_MANAGED_BY_JOB propagation and synchronous success remain.
Tests cover invalid actors in pending and processed states, foreign Admin, a previous owner, authorized replay,
inactive-project recognition, and authorization lost at the core guard. The previous-owner test simulates a changed
persisted owner in its in-memory fixture; it does not introduce a production ownership-transfer operation.

The three successful builder cases now read complete rows independently before and after publication from:
core.requirement_extraction_results, core.requirement_extracted_items, core.requirement_extracted_item_evidence,
and core.requirement_extracted_item_segments (physical names resolved from EF metadata). Each table must be nonempty.
Scalar row_to_json reads have no tracking and include all persisted columns, IDs, foreign keys, timestamps, arrays
and jsonb as returned by PostgreSQL. Equality checks include row counts and order by ID, not just operational state.
The fixture includes an extraction payload, evidence and segment. The new proposal and source references are checked
separately. An SQL interceptor asserts only proposal/item INSERTs, with source entries Unchanged after saving.
No builder production change, general mapper change or factory relaxation was needed for this supplement.

Integrity artifacts are under
`%TEMP%/sng-d1-completion-baadade1f8344cb6b3d493b70c13ea9d`.
baseline.csv records each file before this supplement, categorized as Production, Tests, Model or Documentation.
verification-before.csv and verification-after.csv bind the final build/test execution to individual file hashes;
comparison reports distinguish intentional changes from any later changes. No earlier per-file audit manifest was
found, so the exact historical document difference and its authorship are not asserted or reconstructed.
Final execution counts and digest comparisons are reported with the completion, not inferred from older runs.

The only intended edits in this supplement are this document, the public adapter, its existing test file and
LegacyBuilderPostgreSqlTests. Earlier file-safety changes, model/migrations, publication fingerprint, document
lifecycle and commercial rules remain outside this edit. Registry concurrency, disposal-failure injection, browser
validation, physical crashes and document-byte stability retain their previous evidence limits. No worker, enqueue,
retry, recovery, automatic activation or D.2 implementation is introduced.
