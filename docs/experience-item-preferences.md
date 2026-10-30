# Initial Experience Preferences Per Item

## Contract

Published Experience inspected with fetch, without changing its worktree:
`44c94955198236ea9e650e9dc4ca12fd2db53eb2`.

The linked project must have exactly one SNG_WEB source with schema
`sng-public-experience-v1`. The existing diagnosis parser validates values without
altering `AnswersJson`. Invalid, absent, unknown or ambiguous input yields no
initial answer for the affected benefit (an ambiguous source yields none).

| Public key | Initial V4 answer |
| --- | --- |
| acustico, termico, seguridad, estetica | 1/2 LOW, 3 MEDIUM, 4/5 HIGH for the corresponding benefit |
| uv | si/ns UV_YES; no UV_NO |
| finishes and contextual answers | Informational only |

Business decision of 2026-10-30 explicitly maps design importance to the initial
AESTHETICS priority, despite the previous question wording. The current V4 prompt
now asks for aesthetic priority. No technical matrix or compatibility guarantee
changes. Original Experience answers and historical catalog options remain intact.

## Precedence And Persistence

`RequirementExperiencePreferences` projects effective answers for drafts reads,
Level 2 reads/selection validation and configuration resolution/confirmation.
An existing draft wins completely, even empty, partial or from an older catalog.
GET never materializes a draft. No new table, column or migration is needed.
`PreferenceOrigin` is NONE, EXPERIENCE or SAVED; `HasSavedDraft` follows the durable
draft revision, not the presence of prefilled answers. No original answers or
operational credentials are added to these responses.

Guardar prioridades accepts the displayed answers for one item with the existing
revision contract. Partial drafts remain legal under the existing API validation;
they are not a complete technical configuration. All five valid source answers
now satisfy the five-benefit input gate, without confirming products. An existing
partial draft with missing aesthetics remains partial and must be completed
manually. Saving an unchanged inherited draft is permitted.
This action is not product selection or configuration confirmation.

Saved preferences survive re-reading. New item/version identities can derive the
original project source; no prior item overrides are copied by label or position.
Historical catalog values are retained; the UI disables their editing with the
current catalog and Backend rejects implicit cross-catalog replacement.

## Existing Technical Rules

No Level 1/2 matrices, compatibility rules or product catalogs changed. Saving
answers recalculates Level 1; Frontend queries existing Level 2 afterwards, separately
from persistence. It shows pending/error rather than an older result during edits.
Alternative ambiguity stays unresolved. Finish remains PENDING_EXTERNAL_SOURCE.
The detail view retains its explicit product selection workflow; no automatic
product confirmation is introduced. Project diagnosis continues to show the
client's original answers, not commercial per-item changes.

## Verification And Boundaries

Focal tests cover scale boundaries and invalid types, UV ns, source ambiguity,
saved empty/partial/historical precedence and unchanged source. A PostgreSQL fixture
uses real repositories/services, READ ONLY reads, individual acceptance/update,
revision conflict, a new version with fresh item IDs, and compares displayed
Level 1 with actual Level 2 consumption. Replacement is a domain fixture transition,
not a new extraction/replacement integration test. Catalog inputs are test doubles.

Frontend tests cover unchanged acceptance, old catalog preservation, pending result
display, and existing configuration guards. `scripts/test-experience-inheritance-browser.mjs`
runs real compiled React against synthetic API responses, including mocked PUTs;
it does not prove authenticated integration or real commercial persistence.
Backend artifact root: Temp/sng-item-preferences-20261029. Browser artifacts:
Temp/sng-item-preferences-browser (separate runs), or WORKSPACE_BROWSER_OUTPUT.

No host, emission flag, execution authorization, job, extraction/publication protocol,
pricing, typology or original Experience payload was modified. Activation and real
API smoke remain outside this change.
