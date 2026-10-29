# Documentary file safety 2E.2D.2a

An exception does not prove database rollback or authorize physical deletion. The
affected Requirement/Submission paths now retain attempted destinations and complete
upload batches after failures. Logical document removal still removes its reference;
the physical file remains for future coordinated cleanup. This is a conservative
safety measure, not a permanent retention policy or a new document lifecycle.

## Inspected operations

| Operation | Bytes | Reference publication and confirmation | Prior deletion and correction |
|---|---|---|---|
| CreateRequirementService | New batch before save | Requirement and files in SaveChangesAsync | Generic catch deleted completed uploads even after an uncertain commit; retain batch |
| AddDocumentAsync | New destination before save | Add file and activity, save, then response query | Catch deleted attempted key, including storage collision or post-commit response failure; retain |
| RemoveDocumentAsync | No new bytes | Remove file reference and update activity, save, then response query | Inline physical deletion after save removed content without a coordinated reference protocol; remove only reference |
| ReplaceRequirementAsync | Full new batch | New Requirement/files, supersede old identity and activity in save, then response | Catch deleted new batch even after commit; retain new and historical files |
| PublicExperienceIntakeService | Submission upload batch | TryInsertAsync, unique SubmissionId; loser reads winner for replay/conflict | Catch and unique-insert loser deleted attempted batch; retain both winner and losing uploads |
| PreparePublicExperienceRequirementService | Validated copies of intake files | PrepareLockedAsync transaction/savepoint links Submission, PreQuote, Requirement and files | Repository already retained on commit uncertainty; failure callback still deleted attempted keys after earlier failures, including collisions; callback now retains |
| ReplaceDocumentAsync | New destination | Existing staged error handling around save and response | Already retained old/new bytes; implementation left unchanged and regression rerun |
| LocalFileStorage | Private CreateNew temporary file, flush, move without overwrite | Destination creation is not DB reference publication | Cleanup now requires successful temporary-file creation, not merely an assigned path |

The unsafe catch paths above are established by source inspection and the affected
failure boundaries are exercised by fault-injection tests. No production data loss
incident is claimed. The unique RequirementFile StorageKey constraint prevents two
such rows from sharing a key; it is not a cross-storage/reference deletion protocol.

Public preparation still performs copying within its existing transaction and locks.
Neither its repository nor transaction/savepoint order changed. Existing metadata,
Answers, owner assignment, SubmissionId replay/conflict behavior and HTTP contracts
remain. Authorization, minimum file count, replacement states and Requirement
identities are unchanged. No job, attempt or provider call is activated by intake.

## Failure boundaries

| Boundary | Database result | File treatment and response |
|---|---|---|
| Authorization, lifecycle or last-file rejection | No document change | Existing bytes unchanged; existing rejection |
| Destination collision | No reference to attempted upload published by this call | Existing destination remains byte-identical; no delete based on attempted key |
| Partial write or partial batch | Earlier files may exist without references | Retain completed destinations; LocalFileStorage cleans only its private created temporary file |
| Before save or before commit | Tests observe rollback/no new references | Still retain destinations; a generic failure is not a reusable proof of exclusive ownership |
| Exception after real commit | Independently observed references remain committed | Keep all referenced files; propagate/translate failure, not synthetic success or rollback |
| Response query after save | References remain committed | Keep old and new bytes; existing error/cancellation behavior |
| Cancellation before/after commit | Depends on the actual commit boundary | Token cancellation never grants deletion authority |
| Concurrent intake loser | One authoritative submission | Existing replay or conflict, or recognition-query error; losing upload batch retained |
| Successful logical removal/replacement | Authorized new reference set | Old physical bytes retained, including old Requirement history |
| Hypothetical cleanup failure | No application cleanup is called | Cannot trigger destructive compensation or change successful logical response |

## Retention debt and excluded route

Potential orphans include completed uploads from partial batches, destinations left
after failed saves, losing concurrent intake uploads, public preparation copies whose
transaction rolled back, removed documents and superseded file versions. Attempted
keys can also designate someone else's existing file and must not be presumed owned.

A future cleanup protocol must establish creation ownership, absence of published
references without commit uncertainty, and exclusion of concurrent reference creation.
A one-time absence query alone is insufficient. No collector, staging table, automatic
retry, migration, object versioning or retention duration is introduced here.

**Explicitly excluded by user decision:** the independent legacy
`Application/PreQuotes/CreatePreQuoteDocument/CreatePreQuoteDocumentService.cs` route
still has destructive compensation after persistence errors. It does not participate
in the enumerated Requirement/Submission routes. It remains a known source-level risk
and is not certified safe by this block; it was not modified or runtime-tested here.

## Test method

DocumentPersistenceFileSafetyPostgreSqlTests uses the existing isolated PostgreSQL
fixture, real LocalFileStorage directories, independent post-operation DbContexts and
byte comparisons. Manual cases cover creation, addition, removal and Requirement
replacement. Public cases cover intake and preparation, plus three concurrent intake
cases (replay, conflicting payload, losing response query failure). No mock delete
counter substitutes for reading the actual files.

Existing ReplaceDocumentFileSafetyPostgreSqlTests interceptors are reused with one-shot
injections and additional before-save/after-save phases. Cancellation is injected at
save, commit and post-commit boundaries; document response reads are separately
intercepted. Exceptions after a real transaction commit simulate lost responses, not
physical network/process crashes. The deliberately throwing delete implementation is
never called. Each test removes only its isolated temporary directory in finally.

The internal random temporary-name collision guard in LocalFileStorage is verified
by inspection, not forced by a GUID interception. Destination collisions and partial
stream failures are exercised with actual files. External processes modifying storage
are outside the guarantee.

## Change inventory

Production paths relative to this repository:

- Application/PreQuotes/CreateRequirement/CreateRequirementService.cs
- Application/PreQuotes/ManageRequirementDocuments/ManageRequirementDocumentsService.cs
- Application/PublicExperience/PublicExperienceIntakeService.cs
- Application/PublicExperience/PreparePublicExperienceRequirementService.cs
- Application/PublicExperience/PublicExperienceRequirementPreparation.cs (comment only)
- Infrastructure/Storage/LocalFileStorage.cs

Existing test updates:

- Tests/Application/PreQuotes/CreateRequirementServiceTests.cs
- Tests/Application/PublicExperience/PublicExperienceIntakeTests.cs
- Tests/Infrastructure/Persistence/PublicExperienceRequirementPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/ReplaceDocumentFileSafetyPostgreSqlTests.cs (shared fault helpers)

New tests:

- Tests/Infrastructure/Persistence/DocumentPersistenceFileSafetyPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/PublicDocumentPersistenceFileSafetyPostgreSqlTests.cs

This document is the only new documentation file. Existing D.1 and ReplaceDocument
changes are preserved, not attributed to this block.

## Remaining boundaries

This block does not establish exclusion between document mutation and processing,
identity of upload/hash/extraction bytes, durable/shared production storage, a permanent
retention policy or safety against arbitrary external file writers. No rule is added
for freezing after job history, replacement during Running, or replacement from Pending
after cancellation. Those decisions remain reserved for subsequent work.

## Executed verification 2026-10-22

| Run | Passed | Failed | Skipped | Interpretation |
|---|---:|---:|---:|---|
| Preflight | 144 | 57 | 0 | All 57 failures were manual fixture setup, before the operation under test |
| Final focal selection | 202 | 0 | 0 | Corrected fixtures, added last-file rejection; supersedes overlapping preflight cases |

The preflight fixture incorrectly seeded two RequirementFile rows sharing a StorageKey.
All failures named PostgreSQL 23505 and ux_requirement_files_storage_key in Arrange.
The fixture now uses distinct keys and still checks that physically retained bytes are
intact after logical removal. No schema constraint was weakened and no test removed.
The final selection includes 58 manual cases, 24 public cases, three concurrent-intake
cases, eleven existing ReplaceDocument cases and two complete HTTP intake/replay cases.
The 85 new cases and 117 regressions total 202; overlapping runs must not be summed.
The HTTP cases retain stable links, Pending Requirement and zero automatic jobs,
attempts and AI2 calls. Test providers are controlled doubles, not real services.

Both builds used `dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --verbosity minimal`.
Initial build: zero errors, 111 warnings. One new xUnit2031 warning in the new concurrent
test was corrected by using Assert.Single's predicate overload. Final incremental build:
zero errors, 92 warnings. The remaining count difference reflects incremental compilation
scope, not removal of existing warnings. No final diagnostic names the six changed
production paths or new file-safety tests. No production/test source changed after that
build and before/after the final no-build test run; only documentation was finalized.

Test command: `dotnet test Tests/CotizadorBackend.Tests.csproj --no-build --filter <below>`
with `--logger "trx;LogFileName=second.trx"` and the isolated artifact results directory.
Preflight used the same filter and preflight.trx. Final duration: 8 m 31 s.

```text
FullyQualifiedName~DocumentPersistenceFileSafety|FullyQualifiedName~ReplaceDocumentFileSafety|FullyQualifiedName~CreateRequirementServiceTests|FullyQualifiedName~ManageRequirementDocumentsServiceTests|FullyQualifiedName~PublicExperienceIntakeTests|FullyQualifiedName~PreparePublicExperienceRequirementTests|FullyQualifiedName~PublicExperienceRequirementPostgreSqlTests|FullyQualifiedName~RequirementUploadProblemDetailsTests|FullyQualifiedName~PublicExperienceSubmissionHttpTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict
```

## Artifacts and integrity

Current block artifacts:
`C:/Users/USUARIO/AppData/Local/Temp/sng-d2a-1cb4c944c9a64315a1cc80a5f9063d3b`.
Files: baseline.csv (initial path/content SHA-256), build.log, second-build.log,
preflight-tests.log, second-tests.log, preflight-server.log, second-server.log,
results/preflight.trx and results/second.trx. The second run is final evidence.

The existing isolated PostgreSQL 17 cluster was reused, not the usual database:
`C:/Users/USUARIO/AppData/Local/Temp/sng-legacy-close-1237d3bb7c3c4f509a8b9c431c4fb156/data`.
Loopback 127.0.0.1:55447, temporary audit user, fixture-created cotizador_backend_test_*
databases only. Each run started/stopped the server in try/finally and removed the
child-local COTIZADOR_TEST_POSTGRES_ADMIN_CONNECTION_STRING variable. The final
administrative SELECT counted zero remaining test databases; subsequent pg_ctl status
reported no server running. Existing migrations ran only inside isolated test fixtures;
none was added or changed. Cluster files and logs remain as evidence.

Real file roots were `%TEMP%/sng-d2a-files-<guid>` and the existing regression's
`%TEMP%/sng-replace-safety-<guid>`. All 191 distinct roots recorded across both TRX
outputs were checked after execution: zero remain. Their removal is test-resource
cleanup, not a production orphan-deletion policy.

Branch/HEAD unchanged: feature/public-experience-intake /
56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Initial worktree: 19 tracked modified,
49 untracked, zero staged. Final: 27 tracked modified, 52 untracked, zero staged.
The baseline content comparison identifies only the ten existing files and three new
files listed above; no baseline path is missing. Prior unrelated changes, job protocol,
legacy coordination and migrations are preserved. git diff --check passes with Git's
LF/CRLF conversion warnings. No usual database, real AI2/Gemini, worker, enqueue,
activation, commit, push, merge or deployment was used.

Verdict: **implemented and focal verification passed for 2E.2D.2a**. No in-scope test
failure remains. The explicitly excluded PreQuoteDocument compensation risk and the
remaining coordination/content-stability boundaries are not resolved by this result.

Subsequent document-reference coordination is described in
[document-transaction-coordination.md](document-transaction-coordination.md).
Its explicit transaction changes the document-mutation SaveChanges response-loss boundary:
a failure before explicit commit rolls back references, while all attempted bytes remain.
The D.2a byte-retention guarantee and excluded PreQuoteDocument risk are unchanged.
