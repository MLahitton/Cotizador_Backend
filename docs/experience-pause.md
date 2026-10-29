# Experience pause point

IV is closed, including H2's coherent workflow snapshot and F1/H1's request-owned
loading state. This is an audited implementation pause, not production activation
or an integrated smoke test. Emission and the host remain independently off by
default. Existing legacy provider settings are not those two flags.

The next Configuration, Moments and Summary changes must preserve:

- Workspace GET and Consultar estado are read-only, never execution commands.
- Version/status/proposal form one coherent workflow; project/publicContext are
  not promised to share its snapshot. Do not retain another version's cards.
- Loading belongs to an opaque request/controller identity; old completion must
  neither apply data nor clear a newer request's loading state.
- Serial bounded polling, explicit renewal, draft/filter/local-edit preservation,
  and response rejection after project/version/session/token changes.
- Capabilities do not replace write guards. Admin reading does not grant editing.
- Complete dimensions, saved priorities and confirmed configuration are distinct.
  Pending local edits must not become a fictitious commercial confirmation.
- Expiry, revocation, silence or HTTP failure do not manufacture Failed/Cancelled;
  Running does not prove a live supervisor. No automatic alternative execution.
- Idea photo remains separate from execution documents; downloads stay authorized.

Pending: authorized owner/Admin/API smoke, real authentication refresh and draft
persistence; shutdown/process policy after budget exhaustion, uncertain dispatch,
stopped Running jobs, ABA transfer, capacity/quotas/latency, retention and alerts.
Activating either flag requires a separate decision. Disabling emission does not
revoke already issued intent. Push/deployment automation also requires confirmation.

Resume from [authorization and integration](experience-internal-authorization-contract.md)
(sections 16, 17 and 17.1), [supervision](assigned-execution-supervision-contract.md),
[leases](assigned-job-lease-contract.md), [explicit closure](stopped-execution-resolution-contract.md),
[byte identity](document-byte-identity-contract.md),
[document coordination](document-transaction-coordination.md) and
[publication](job-publication-contract.md). Do not redesign those protocols through UI copy.

Historical reports/TRX/manifests remain private evidence outside the repository.
The local precommit archive has a hash index and exclusion register; it is not a
remote backup. Private fixture prerequisites are documented in
[Tests/README.PrivateFixtures.md](../Tests/README.PrivateFixtures.md).
