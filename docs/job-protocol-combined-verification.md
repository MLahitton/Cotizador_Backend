# 2E.2C.3: combined protocol verification

Scope: tests, test helpers and documentation only. No production coordinator, registration, provider call,
worker, enqueue, claim, retry service, recovery or migration.

## Coverage plan

| Property | Existing evidence reused | Focused addition |
|---|---|---|
| Guard, expiry, graph rollback, manifest, terminal identity | JobPublicationPostgreSqlTests and JobPublicationConcurrencyPostgreSqlTests | No duplicate scenarios |
| Mapper behavior | Comparison with HEAD and ProcessRequirementServiceTests | Not reopened |
| Start and publication individually | AssignedJobStartPostgreSqlTests and preparation tests | Real start -> adapter stub -> preparation -> publication -> recognition, public/manual |
| Cancellation and failure individually | JobTerminationPostgr eSqlTests | Started execution, repeated requests/confirmation, failure then late publish |
| Two-party publication races | JobPublicationConcurrencyPostgreSqlTests | Start/request both orders; three-party publish/request/fail, each first |
| Replacement recognition | JobPublicationPostgreSqlTests | Cancelled execution followed by valid ManualRetry fixture on same Requirement |
| Commercial replay | Existing manual-location replay | Domain revision increment and valid non-null commercial confirmation |
| Uncertain commit | Existing commit interceptors | External PostgreSQL lock throughout real ten-second observation budget |
| Receipt validation/hash | Existing 2a tests | Exact exception types and literal canonical document/digest |
| No automatic activation | HTTP complete intake/replay | Rerun existing focused HTTP cases |

## Test mechanisms and boundaries

Fixtures use existing factories and real start/publication/termination services.
The ManualRetry successor is fixture preparation after cancellation has returned
Requirement to Pending; no operational retry implementation or Failed -> Pending
rule is introduced. Commercial edits use existing domain operations, and confirmation
uses ConfirmRequirementTechnicalProposalSelectionService with real repositories and
seeded catalog references, including access/readiness checks. No SQL/reflection bypass.

Races hold the winner after its guarded SQL write inside its open transaction.
Every competitor has a distinct PID; pg_stat_activity must report Lock and
pg_blocking_pids must identify blocking before the winner is released. Polling
delays alone are not accepted as synchronization. Independent context snapshots
include graph rows, job, attempts, Requirement and PreQuote activity. Replay, late calls
and reconciliation also count the stores' INSERT/UPDATE/DELETE commands (including
rolled-back writes); the counter must stay zero and does not replace independent reads.

Reconciliation has three independent implementations, not a shared coordinator:
AssignedJobStartStore, JobTerminationStore and JobPublicationStore. The test covers
start, request, confirm, fail and publish separately. After a real commit, another
connection locks PreQuote; an injected lost response enters reconciliation while
that lock is retained. The observed production token must expire and the result
must be Indeterminate with the committed snapshot unchanged. The 45-second test
safety bound is not the production ten-second token or a response-time promise.
All locks, transactions and pending tasks are released/drained in finally.

The fixed v1 empty-publication vector is hand-assembled from the contract. Its
UTF-8 SHA-256 was calculated separately using PowerShell/.NET SHA256, without
calling the publication canonicalizer or factory. Tests assert literal bytes and
literal digest. Six receipt cases retain full before/after serialization checks.

## Execution

Executed on 2026-10-22. All properties below are validated by executed tests,
not merely by inspection.

| Scenario | Expected and observed | Evidence |
|---|---|---|
| Public/manual complete sequence | One linked attempt and graph, count 1, stable identities/correlation, preparation read-only and recognition with zero writes | 2 sequence cases |
| Started cancellation and failure | First dates/activity preserved, Pending/Cancelled or Failed closure, no graph and late publication rejected | 2 sequence cases |
| Old execution / successor | Valid ManualRetry fixture, distinct attempts, current Processing successor untouched by late calls, zero writes | 1 sequence case |
| Commercial replay | Revision 2, confirmation through real service, override and timestamps preserved, AlreadyPublished and zero writes | 1 sequence case |
| Start/request race | Both decisive lock orders, no invented attempt when cancellation wins, same attempt cancelled when start wins | 2 race cases |
| Three participants | Publish/request/fail each wins first once; no mixed or duplicate outcome | 3 race cases |
| Blocked reconciliation | Actual production token cancellation, Indeterminate, unchanged committed snapshot, zero writes | 5 reconciliation cases |
| Canonical vector / receipt guards | Literal bytes and independently calculated digest; six exact exception assertions with full state preservation | PreparedJobPublicationTests / JobPublicationReceiptTests |
| Regression / HTTP | Existing guards, rollback, concurrency and full intake/replay remain valid; Pending with no automatic jobs, attempts or AI2 calls | Broad run below, including 2 HTTP cases |

### Commands and results

Both builds used `dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore
--verbosity minimal`, with full output retained. Final build: 0 errors, 92 warnings.
No diagnostic points to a file changed in this block. The final build precedes the
final `--no-build` run; no test source was changed afterward.

| Run | Passed | Failed | Skipped | Interpretation |
|---|---:|---:|---:|---|
| Literal vector and receipt preflight | 9 | 0 | 0 | Overlaps broad run |
| Broad focal regressions and combined scenarios | 247 | 0 | 0 | Includes 16 preliminary combined cases |
| Final combined scenarios after test-only strengthening | 16 | 0 | 0 | Supersedes those 16, not an additional distinct test count |

There were no preliminary build/test failures. Review-driven strengthening added
actual job correlation, explicit outcomes and successor assertions, command-level
zero-write checks, confirmation through the real service, and uncancelled cleanup.
The other 231 broad-run cases were unchanged by that strengthening. No production
defect was demonstrated; no severity finding or dependent scenario was suspended.

Broad filter (single line, `dotnet test Tests/CotizadorBackend.Tests.csproj --no-build --filter`):

```text
FullyQualifiedName~JobProtocol|FullyQualifiedName~JobPublication|FullyQualifiedName~PublicationModelCoverageTests|FullyQualifiedName~AssignedJobStart|FullyQualifiedName~StartAssignedJob|FullyQualifiedName~JobTermination|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict
```

Final filter: `FullyQualifiedName~JobProtocol`. TRX logs use `combined.trx` and
`final-combined.trx`. These overlapping runs must not be added together.

### Runtime evidence

Final reconciliation measurements include disposal, not an exact response SLA:

| Route | Observer PID | Blocker PID | Observed elapsed |
|---|---:|---:|---:|
| start | 32220 | 18352 | 10.52 s |
| request | 25644 | 27272 | 10.76 s |
| confirm | 7032 | 27984 | 10.67 s |
| fail | 14404 | 24744 | 10.52 s |
| publish | 30388 | 29380 | 11.02 s |

Each route observed PostgreSQL Lock blocking before token cancellation. All three
independent store implementations were exercised, including all termination routes.
Start/request decisive orders observed PID 18500 blocked by 26608 and PID 28744
blocked by 8488. Three-participant PID sets were publish: 20424/14800/25392,
request: 32708/2808/26992, fail: 9188/24184/25416. Both competitors were observed
waiting on server locks before each winner was released.

Canonical vector SHA-256:
`57B2056869B3A94C62D746CBB617DAC5E2AE7AE2450940CE8987D62987CABC42`.

### Artifacts and cleanup

Isolated PostgreSQL 17 cluster root:
`C:/Users/USUARIO/AppData/Local/Temp/sng-2c3-2c79514908b840dfb83f3e2b7926b56b`.
Loopback port 55444, temporary audit user, no usual database connection. Fixture
databases are created and dropped; the final server log records CREATE and DROP
of `cotizador_backend_test_a498691caab4411d81ce66265ecc69e0`.
Both runs stopped the server in finally. Temporary cluster files and logs remain
for evidence; no running database is left. Connection environment was child-local
and removed in finally. Providers were stubs; no real AI2/Gemini invocation.

Artifacts under that root: `tests.log`, `final-tests.log`, `server.log`,
`final-server.log`, `results/combined.trx`, `results/final-combined.trx`.
Build logs: `%TEMP%/sng-2c3-build.log` and `%TEMP%/sng-2c3-final-build.log`.

### Change inventory and integrity

Four existing untracked files edited:

- `Tests/Domain/PreQuotes/JobPublicationReceiptTests.cs`: exact exception types.
- `Tests/Application/PreQuotes/PreparedJobPublicationTests.cs`: fixed vector.
- `Tests/Application/PreQuotes/PrepareJobPublicationTests.cs`: shared internal test builder.
- `Tests/Infrastructure/Persistence/JobTerminationTestSupport.cs`: optional public fixture.

Five new files:

- `Tests/Infrastructure/Persistence/JobProtocolTestSupport.cs`.
- `Tests/Infrastructure/Persistence/JobProtocolSequencePostgreSqlTests.cs`.
- `Tests/Infrastructure/Persistence/JobProtocolRacePostgreSqlTests.cs`.
- `Tests/Infrastructure/Persistence/JobProtocolReconciliationPostgreSqlTests.cs`.
- `docs/job-protocol-combined-verification.md`.

Initial/final branch: `feature/public-experience-intake`.
Initial/final HEAD: `56ace9c3845d2e3327a1ca811c27f8e1562e5a79`.
Initial: 4 tracked modified, 38 untracked, 0 staged.
Final: 4 tracked modified, 43 untracked, 0 staged. Existing changes preserved.
The sorted path/content SHA-256 aggregate of all 617 files outside Tests/docs is
unchanged from baseline:
`ED7591A1E812AE9F61DD2CEAECF7936FE2035F9B69B57B87D8CFEB3AA2DC9BB4`.
No production, contract, domain, migration, endpoint, DI or frontend change.
`git diff --check` passes, with only Git's pre-existing LF/CRLF warning for the
HTTP test file. No commit, push, merge or deployment.

## Remaining boundary

No test certifies behavior against unadapted legacy writers or immutable document
bytes. Those remain 2E.2D work. Exceptions after real commit simulate response loss,
not physical network/process crashes. No real AI2/Gemini or usual database access.

These excluded behaviors are not runtime-validated here. The legacy mapper
comparison is reused prior audit evidence, not a newly executed legacy suite.
No in-scope scenario remains unvalidated. Writer coordination and verifiable
document-content stability remain prerequisites for 2E.2D; worker, enqueue,
claim, heartbeat, recovery and operational retry remain outside this block.

Verdict: **APTO para cerrar 2E.2C.3 y preparar 2E.2D**.
This result does not authorize automatic processing.
