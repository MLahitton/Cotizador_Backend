# Document coordination 2E.2D.2b

Scope: transactional document references and replacement after confirmed cancellation.
Requirement remains the document version. This block does not activate processing or
certify stored byte identity. Verification results below determine closure status.

## Approved policy

Any job history for the exact Requirement freezes add/remove/replace-file operations,
including terminal jobs and jobs without attempts. Queued/Running remain active when
their lease expires or cancellation is requested. Pending/Processing attempts are active.
Current retains the domain definition, including activity, status and supersession links.

Current Processed/Failed Requirements retain replacement eligibility only without active
jobs or attempts. Pending gains replacement eligibility only with unambiguous terminal
cancellation evidence and no active execution. A queued cancellation needs no attempt.
Neither a failed job nor Pending without history gains that permission.

The user additionally approved blocking Requirement CancelAsync with ANY job history,
using RequirementNotMutable and CanCancel=false. It does not cancel a job or an attempt.

Foreign actors, including Admin, receive the existing nonrevealing not-found result
before history is queried under locks. Active owner, user, Project and Client are required.
Existing synchronous routes, DTO fields and success statuses remain unchanged. Editing
and replacement conflicts retain RequirementNotMutable / RequirementNotReplaceable (409),
not AlreadyProcessing. HTTP descriptions no longer claim replacement is Processed/Failed only.

## Implementation choices

RequirementDocumentPolicy is the shared evaluator. It requires complete persisted job
and attempt sets plus authorization; it never interprets unloaded navigations as empty.
For Pending, the supported evidence is one cancelled job and either no attempts (unlinked
job) or exactly its linked, finished, cancelled attempt. Multiple terminal executions or
unrelated historical attempts are ambiguous and remain ineligible. No latest-job ordering
by GUID or timestamps is invented. Defining additional histories remains a policy gap,
not a reason to infer permission.

BeginDocumentWriteAsync rejects a DbContext with pending changes before clearing stale
tracking. It opens a short transaction and locks User, Client, Project FOR SHARE, then
PreQuote, Requirement, Jobs, Attempts and Files FOR UPDATE, with stable ID ordering within
sets. It revalidates every discovered relationship after locks. No Submission is updated,
so no Submission lock is acquired. This follows the existing legacy/job ordering.

Initial authorization and normalization precede storage I/O. Uploaded files are prepared
outside writer locks. Final authorization, current/history/attempt eligibility, file
membership and final count/size/minimum checks happen inside the transaction. References,
supersession and quote activity commit together. No physical deletion, compensation or
automatic retry follows a rollback, cancelled token, lost response or uncertain commit.
This is not a distributed PostgreSQL/storage transaction.

SupersedeAfterConfirmedCancellationBy is a distinct local Pending transition. It validates
current, identity and dates without pretending to know job history. The coordinator proves
cancellation before invoking it. Ordinary CanReplace and SupersedeBy are not broadened.

Replacement targets exactly A. It creates one fresh Pending B with the existing inherited
fields and links, supersedes A atomically, and leaves A's files and processing/commercial
evidence intact. B receives no execution history. A repeated or concurrent request against
A cannot redirect to B or create C. Submission evidence remains linked to its original A.

## Capability consumers

Lifecycle responses, requirement detail, current requirement and creation responses use
the persisted evaluator with the real actor. Creation previously hardcoded three booleans;
only its response projection changes, not its storage/preparation workflow. The actorless
repository current query returns no authorized capabilities; GetCurrentRequirementService
supplies them after authorized reads. Workspace consumes only identity/status/proposal,
not these booleans. Chat consumes the detail service. Read capabilities are not reservations:
every mutation rechecks under locks.

Frontend inspection: requirement-api.ts preserves the existing conflict codes and
requirement-documents-lifecycle.tsx gates controls using the booleans. No frontend edit or
browser validation is part of this block. Unknown external consumers are not certified.

## Writer boundary

The future enqueuer MUST take the same Requirement lock and revalidate current and active
attempts before inserting a job. The tests implement only a transaction fixture, not a
production enqueue service. Arbitrary writers ignoring the protocol are not protected by
an absence query or by the advisory lock alone.

Inspected AddFile/RemoveFile callers: creation, public preparation and document lifecycle.
Creation inserts a new Requirement under the existing single-active-Requirement constraint;
it cannot replace an existing current silently. Public preparation locks Submission,
returns existing links on replay, and otherwise creates a new quote/Requirement. Its copy
I/O remains unchanged. No bypass that modifies existing references was found in these paths.

## Verification record

Baseline: feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79; 27 tracked modified, 52 untracked, zero staged.
The per-file baseline contains 828 paths and SHA-256 values, not a comparison to HEAD.
Artifacts: `%TEMP%/sng-d2b-761df44f739a4950973939ada214fb50`.

Preliminary executions overlap and must not be summed:

| Run | Passed | Failed | Skipped | Diagnosis |
|---|---:|---:|---:|---|
| preflight.trx | 103 | 12 | 0 | Nine mocks lacked the new capabilities; three save_response expectations assumed implicit commit |
| second.trx | 55 | 2 | 0 | Two new multi-file fixtures reused a unique storage key; corrected to distinct keys |

SaveChanges response failure now precedes explicit document commit and must roll back DB
references while retaining bytes. The create path still uses its original implicit commit.
No prior constraint or assertion about retained bytes was weakened.

Initial compilation: 0 errors, 110 warnings. build2.log: 2 test compilation errors for an
internal file-limit constant, corrected using the existing public limit's literal in the
test without widening production visibility. build3.log: 0 errors, 92 warnings (incremental).
build4.log: 0 errors, 110 warnings. Different compile scopes are not warning fixes.

Final verification executed on 2026-10-22:

| Run | Passed | Failed | Skipped | Scope |
|---|---:|---:|---:|---|
| broad.trx | 355 | 0 | 0 | Build4 sources; documentary, D.2a, affected legacy/job protocol, workspace and full intake/replay regressions |
| final-focal.trx | 226 | 0 | 0 | Final sources; documentary/read/create/HTTP/file-safety regressions, including all 54 coordination and 7 domain cases |

These runs overlap: 219 broad cases were rerun in final-focal. Do not sum totals.
The final run supersedes those cases. Between builds, final count/size validation was
strengthened; tests added HTTP, foreign-document, full commercial confirmation and size
coverage, and the legacy race participant was changed to the real core service. The
other 136 broad cases were not rerun after that strengthening. No production/test source
changed after final compilation; documentation alone was completed afterward.
No final failure or skipped critical scenario remains. This is not the entire repository suite.

Final build command: `dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore
--no-incremental --verbosity minimal`. Result: 0 errors, 111 warnings; full output is
`final-build.log`. One new CS8604 warning is in the legacy race test's NSubstitute
callback argument passed to CreateResponse (line 77); both real-core race cases passed.
It is not claimed fixed or pre-existing. Other diagnostics remain visible in the log.
The prior incremental 92-warning result is not comparable to a full rebuild.

Both test commands use `dotnet test Tests/CotizadorBackend.Tests.csproj --no-build
--filter <filter> --logger "trx;LogFileName=<run>.trx" --results-directory <root>/results`.
The final command additionally uses `--logger "console;verbosity=normal"`.
Broad filter:

```text
FullyQualifiedName~DocumentCoordination|FullyQualifiedName~RequirementDocumentTransition|FullyQualifiedName~DocumentPersistenceFileSafety|FullyQualifiedName~ReplaceDocumentFileSafety|FullyQualifiedName~ManageRequirementDocumentsServiceTests|FullyQualifiedName~GetCurrentRequirementServiceTests|FullyQualifiedName~GetRequirementDetailsServiceTests|FullyQualifiedName~CreateRequirementServiceTests|FullyQualifiedName~RequirementUploadProblemDetailsTests|FullyQualifiedName~LegacyJobExclusion|FullyQualifiedName~AssignedJobStartPostgreSqlTests|FullyQualifiedName~JobTerminationPostgreSqlTests|FullyQualifiedName~JobPublicationPostgreSqlTests|FullyQualifiedName~JobProtocolSequencePostgreSqlTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict|FullyQualifiedName~GetProjectWorkspaceServiceTests
```

Final focal filter:

```text
FullyQualifiedName~DocumentCoordination|FullyQualifiedName~RequirementDocumentTransition|FullyQualifiedName~DocumentPersistenceFileSafety|FullyQualifiedName~ReplaceDocumentFileSafety|FullyQualifiedName~ManageRequirementDocumentsServiceTests|FullyQualifiedName~GetCurrentRequirementServiceTests|FullyQualifiedName~GetRequirementDetailsServiceTests|FullyQualifiedName~CreateRequirementServiceTests|FullyQualifiedName~RequirementUploadProblemDetailsTests|FullyQualifiedName~GetProjectWorkspaceServiceTests
```

Broad TRX records start 12:05:10 and finish 13:39:38 local time. Final focal duration is
11.2859 minutes. Neither is a production response-time guarantee. Full logs are broad.log
and final-focal.log. Earlier failing logs/TRX remain available and are not final evidence.

## Acceptance evidence map

All rows below have executed passing evidence from the runs identified above. Tests named
here belong to DocumentCoordinationPostgreSqlTests unless another class is specified.

| Criterion | Focused evidence |
|---|---|
| A: any history freezes exact references | Eleven history cases, including queued/no attempt, terminal states, expired lease and requested cancellation; no storage writes/deletes on rejection |
| B: capabilities and locked revalidation | Repository/detail/current projections; authorization cases; dirty-context rejection; real HTTP conflicts; unit regressions for creation and lifecycle responses |
| C: approved Pending extension only | Queued/running terminal cancellation; no-history and failed-without-attempt negatives; mixed history rejected; confirmation must commit first |
| D: one successor, evidence preserved | Independent operational/full-graph snapshots; repeated and concurrent replacement; original Submission answers/links; commercial revision and real service confirmation preserved |
| E: serializable competing operations | Ten barrier-controlled races: job and real legacy start in both orders, postcancel replacement/job both orders, double replacement, minimum/count/size limits; distinct PIDs and server lock evidence |
| F: file safety on errors | Nine replacement fault boundaries plus D.2a regressions; known rollback versus committed/lost-response results; real temporary bytes retained and zero physical deletes |
| G: authorization and existing contracts | Owner/foreign Admin/inactive contexts, changed activity after storage, foreign document rejection, unchanged conflict codes, representative commercial confirmation |
| H: no activation or byte guarantee | Intake/replay regression and successor with no execution history; no worker/enqueue/migration or fingerprint changes; byte identity explicitly excluded |

## Per-file change inventory

Relative to initial.csv, not HEAD, the block changes these existing files:

- `Api/Controllers/PreQuoteRequirementsController.cs`: persisted creation capabilities and accurate conflict text.
- `Application/Common/Abstractions/PreQuotes/IRequirementRepository.cs`: documentary scope and capability queries.
- `Application/PreQuotes/CreateRequirement/CreateRequirementResult.cs`: capability result properties.
- `Application/PreQuotes/CreateRequirement/CreateRequirementService.cs`: persisted response capabilities after creation.
- `Application/PreQuotes/GetCurrentRequirement/GetCurrentRequirementService.cs`: actor-aware capability projection.
- `Application/PreQuotes/GetRequirementDetails/GetRequirementDetailsService.cs`: same policy for details.
- `Application/PreQuotes/ManageRequirementDocuments/ManageRequirementDocumentsService.cs`: coordinated mutations, final limits and replacement.
- `Domain/PreQuotes/Requirement.cs`: explicit postcancel supersession transition.
- `Infrastructure/Persistence/Repositories/RequirementRepository.cs`: actorless current query no longer asserts authorized permissions.
- `Tests/Api/Integration/RequirementUploadProblemDetailsTests.cs`: creation capability mock.
- `Tests/Application/PreQuotes/CreateRequirementServiceTests.cs`: creation capability mock.
- `Tests/Application/PreQuotes/GetCurrentRequirementServiceTests.cs`: capability projection and record comparison.
- `Tests/Application/PreQuotes/GetRequirementDetailsServiceTests.cs`: owner versus foreign Admin capabilities.
- `Tests/Application/PreQuotes/ManageRequirementDocumentsServiceTests.cs`: scope/capability mocks.
- `Tests/Infrastructure/Persistence/DocumentPersistenceFileSafetyPostgreSqlTests.cs`: explicit transaction boundary expectations, retained bytes unchanged.
- `docs/document-file-safety.md`: subsequent explicit-transaction boundary note.
- `docs/requirement-processing-jobs.md`: link to this block and approved Cancel guard.

Nine new files:

- `Application/Common/Abstractions/PreQuotes/IRequirementDocumentWrite.cs`: small shared evaluator and scope contract.
- `Infrastructure/Persistence/Repositories/RequirementRepository.DocumentWrite.cs`: persisted reads and compatible ordered locks.
- `Tests/Domain/PreQuotes/RequirementDocumentTransitionTests.cs`: local transition guards.
- `Tests/Infrastructure/Persistence/DocumentCoordinationPostgreSqlTests.cs`: policy, preservation and shared fixture helpers.
- `Tests/Infrastructure/Persistence/DocumentCoordinationBoundaryPostgreSqlTests.cs`: authorization, confirmation, foreign documents and late execution.
- `Tests/Infrastructure/Persistence/DocumentCoordinationRacePostgreSqlTests.cs`: real competing writers and final limits.
- `Tests/Infrastructure/Persistence/DocumentCoordinationFaultPostgreSqlTests.cs`: rollback and uncertain response boundaries.
- `Tests/Infrastructure/Persistence/DocumentCoordinationHttpPostgreSqlTests.cs`: HTTP 409/404 contract.
- `docs/document-transaction-coordination.md`: this implementation/evidence record.

## Runtime and integrity

Final race evidence (winner PID / blocked PID): job first 31156/35324, document before job
18884/4040; legacy first 30784/35508, document before real legacy core 17564/27176;
replacement before job 17840/11020, job before replacement 35632/27212; double replacement
32016/11532; remove 35432/17820; count 28360/11652; total size 29672/25328.
Every race asserted distinct connections, PostgreSQL Lock and pg_blocking_pids before
releasing the winner, then checked committed state with independent contexts. Barriers
and finally cleanup are explicit. Providers are stubs; commit response loss is injected,
not a physical process/network crash. Commercial confirmation uses the real service.

Only the previously isolated PostgreSQL 17 cluster was used:
`C:/Users/USUARIO/AppData/Local/Temp/sng-legacy-close-1237d3bb7c3c4f509a8b9c431c4fb156/data`,
loopback port 55447, temporary audit user. This is not the usual database. Child-local
COTIZADOR_TEST_POSTGRES_ADMIN_CONNECTION_STRING was removed in finally, and each run
stopped its server. Final SELECT against pg_database found zero cotizador_backend_test_*
databases; pg_ctl status after shutdown reported no server running. All temporary storage
roots logged by final coordination tests were confirmed absent. Cluster and evidence logs
are retained, not deleted. New server logs are preflight-server.log, second-server.log,
broad-server.log, final-server.log and cleanup-server.log under this block's artifact root.

Initial/final branch and HEAD are unchanged. Final worktree: 36 tracked modified,
61 untracked, zero staged. Against the 828-file initial manifest: 17 existing files changed,
9 new files, zero missing files, and the remaining 811 baseline files unchanged. The
inventory above accounts for every delta; no unrelated/concurrent content change was
detected. In particular, legacy/job stores, publication fingerprints, migrations, intake,
public preparation, LocalFileStorage and the excluded legacy document service are unchanged
from the task baseline. git diff --check passes, with Git's LF/CRLF notices. Prior uncommitted
work remains intact. Existing migrations ran only inside fixture-created test databases.

Verdict: **APTO para revision de cierre de 2E.2D.2b**. Criteria A-H are supported within
the tested protocol and explicit boundaries. Mixed terminal histories remain deliberately
ineligible pending policy; no critical in-scope test is left unexecuted. This does not
authorize automatic processing, certify arbitrary uncoordinated writers or verify D.2c bytes.

## Excluded risks

Byte hashing, immutable snapshots/spool and document-content proof remain D.2c. File
retention/orphan cleanup remain separate. CreatePreQuoteDocumentService retains its
explicitly excluded destructive-compensation risk. No migrations, worker, enqueue,
claim, heartbeat, retry, recovery, activation, real AI2/Gemini, usual DB, commit, push,
merge or deployment are introduced by this block.

## Focused cancellation race complement (2026-10-22)

The closure audit reported 201 passed / 0 failed / 0 skipped and one missing-evidence
blocker: Requirement CancelAsync versus job insertion. This complement addresses only
that blocker; it is not a rerun of the audit or of the historical 226/355 selections.
No production defect was demonstrated and no production correction was made.

Added `Tests/Infrastructure/Persistence/DocumentCancellationRacePostgreSqlTests.cs`,
a partial of DocumentCoordinationPostgreSqlTests. Existing shared helpers and the ten
earlier races are unchanged, so those races were not rerun. Both new cases use the real
ManageRequirementDocumentsService.CancelAsync resolved through application DI and a
test-only insertion transaction using the compatible document write scope.

| Test | Result | Winner PID / blocked PID | Observed outcome |
|---|---|---|---|
| JobFirst_BlocksRequirementCancellation_WithoutLoserEffects | Passed | 31000 / 33308 | Exact inserted job retained; CancelAsync returns RequirementNotMutable; Requirement Pending/current |
| RequirementCancellationFirst_RejectsJobBecauseNotCurrent | Passed | 4040 / 16908 | Requirement Cancelled/noncurrent; insertion explicitly returns not_current; zero jobs |

Both participants were initially eligible on a Pending/current Requirement without jobs
or attempts. The winner pauses after SaveChanges inside its open transaction. Release
requires distinct PIDs, PostgreSQL Lock and pg_blocking_pids naming the winner, not merely
a polling delay. Protective cancellation, barrier release and task draining use finally.

Full row snapshots of Requirement, PreQuote, files, jobs and attempts taken inside the
winner transaction equal independent final reads: the loser adds no persistent effect.
Before/after comparisons additionally permit only cancellation status/activity/timestamps
when cancellation wins; job-first preserves the full Requirement and quote rows. Both
cases retain file metadata and real bytes, zero attempts, zero storage writes/deletes,
and unchanged authorized capabilities before the race. No exception is accepted as the
fixture's not_current rejection. These are persistent-effect checks, not a claim of
command-level zero-write instrumentation for these new races.

Signal observation uses a delegating counter around the real in-memory registry installed
in the actual DI container, with a live registered sentinel and callback. Neither case
calls TryCancel, adds registry operations or cancels the sentinel. The provider factory
in that same container is never resolved. These runtime observations cover this container
and sentinel; source inspection additionally establishes that the documentary service's
current dependency graph does not use the registry/provider. A disconnected mock is not
used to claim absence of signals, nor is process-wide instrumentation claimed.

Final-source full build used `dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore
--no-incremental --verbosity minimal`: **0 errors, 111 warnings**. No diagnostic names the
new test file. The earlier CS8604 in the existing legacy race test remains unchanged and
outside scope. No test source changed after this build. No preliminary failures occurred.

One focused run: **45 passed / 0 failed / 0 skipped**, about 2.75 minutes. It includes the
two races plus directly related documentary cancellation, history, authorization, HTTP
conflicts and capability projections. Counts are not added to earlier overlapping runs.
Command: `dotnet test Tests/CotizadorBackend.Tests.csproj --no-build --filter <below>
--logger "trx;LogFileName=focused.trx" --logger "console;verbosity=normal"
--results-directory <artifact-root>/results`.

```text
FullyQualifiedName~JobFirst_BlocksRequirementCancellation|FullyQualifiedName~RequirementCancellationFirst_RejectsJob|FullyQualifiedName~History_FreezesDocumentsAndCancellation|FullyQualifiedName~NoJobs_PreservesLocalEligibility|FullyQualifiedName~Authorization_IsRecheckedBeforeHistory|FullyQualifiedName~FrozenHttp|FullyQualifiedName~ManageRequirementDocumentsServiceTests|FullyQualifiedName~GetCurrentRequirementServiceTests|FullyQualifiedName~GetRequirementDetailsServiceTests
```

Artifact root: `C:/Users/USUARIO/AppData/Local/Temp/sng-d2b-cancel-67985084fca942d7aa0d5cce529abd8c`.
Retained evidence: initial.csv, final.csv, build.log, tests.log, results/focused.trx,
initdb.log and server.log. A new isolated PostgreSQL 17 cluster under this root used
loopback port 55450 and temporary audit user; no usual database was accessed. The run
removed its child-local connection variable and stopped the server in finally. Final
database check returned zero fixture databases; pg_ctl reported no server running.
Its expected no-server exit status is not a test failure. Logged temporary storage roots
were checked absent; cluster files and evidence logs are retained, not running services.

Branch and HEAD remain feature/public-experience-intake /
56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Initial manifest: 837 files; final: 838.
Only this existing document changed and one new test file was added; all 836 other
baseline files, including every production source and migration, remain byte-identical.
Worktree: initial 36 tracked modified / 61 untracked / 0 staged; final 36 / 62 / 0.
git diff --check passes with existing LF/CRLF notices. Previous uncommitted work is intact.

Complemento concurrente validado; resuelto el unico bloqueante senalado por la auditoria
de 2E.2D.2b. This is focused evidence, not a fresh complete closure audit. Arbitrary
uncoordinated writers, document byte identity (D.2c), real providers and physical crashes
remain outside scope. No production enqueue, worker, automatic processing, migration,
commit, push, merge or deployment was introduced.
