# ARAMEX-1 — Official Contract and Runtime Compatibility

Audit date: 2026-07-06

## Executive Summary

The implemented Aramex runtime was audited against Aramex's current public
Developer Solutions Center, Shipping Services manual, and the public
`ShippingAPI.V2` / `Service_1_0` contract.

The pre-audit runtime was **not production compatible**. Four concrete
interoperability defects were found:

1. shipment cancellation returned success without calling Aramex, although the
   public contract exposes pickup cancellation, not shipment/AWB cancellation;
2. shipment labels were deserialized from the response root instead of each
   processed shipment, so valid labels could be discarded;
3. all creates were sent as `DOM` even when an international product type was
   selected;
4. mandatory Aramex contact fields were omitted.

Minimal adapter-only hardening was implemented. No Checkout, Geographic,
Shipment Authority, controller, recovery, audit, frontend, database, or shared
shipping architecture was redesigned.

The hardened create, label, authentication envelope, endpoint, tracking
request, error envelope, carrier resolution, and authority flow are compatible
with the reviewed public contract. Aramex shipment cancellation and general
webhooks are now explicitly unsupported rather than falsely advertised.

Production activation remains **conditional**, not unconditional: a
tenant-specific Aramex account must pass sandbox certification, current
provider status-code samples must be frozen, and cancellation must remain an
operational/manual workflow unless Aramex supplies an account-specific official
shipment-cancellation contract.

## Official Contract Summary

Official sources:

- [Aramex Developer Solutions Center](https://www.aramex.com/ag/en/developers-solution-center/aramex-apis)
- [Official Shipping Services API manual](https://www.aramex.com/docs/default-source/resourses/resourcesdata/shipping-services-api-manual.pdf)

The public authority exposes SOAP/WCF services plus REST/JSON and REST/XML
bindings under `ShippingAPI.V2/*/Service_1_0.svc`.

| Concern | Official public contract |
|---|---|
| Authentication | `ClientInfo`: `UserName`, `Password`, `Version`, `AccountNumber`, `AccountPin`, `AccountEntity`, `AccountCountryCode`, `Source` |
| Transport | HTTPS; SOAP/XML is primary, with documented REST/JSON bindings |
| Create | `Shipping/Service_1_0.svc/json/CreateShipments` |
| Tracking | pull-based Shipment Tracking service; no public general shipment webhook contract |
| Label | `LabelInfo` report `9201`; `URL` or `RPT`; label returned on the processed shipment as URL or file contents |
| Shipment identity | successful processed shipment returns `ID`, the generated AWB |
| Product pairing | `DOM` uses domestic products including `OND`; international parcel products include `PPX` and `EPX` under `EXP` |
| Error model | `HasErrors`, `Notifications`, and documented `ERRxx` codes |
| Cancellation | public Shipping Services operation is `CancelPickup`; no public shipment/AWB cancellation method |
| Pickup | create and cancel pickup are official operations; this adapter does not implement pickup runtime |
| Idempotency | no official request-id/idempotency guarantee is published |
| Retry/rate limit | no public retry schedule, quota, or rate-limit-header contract is published |
| Deprecation | no public structured changelog or retirement timetable was located; `service_1_0` remains published |

## Current Runtime Summary

`AramexAdapter` uses the documented REST/JSON binding and embeds all credentials
in `ClientInfo`; it does not add an invented authorization header. The named
HTTP client has a 15-second timeout through application registration.

Carrier execution remains behind `ShippingCarrierRegistry` and
`CarrierShipmentService`. The adapter does not query SQL, Canonical Geography,
Checkout, Orders, or Shipments. Carrier geographic resolution occurs before
adapter invocation, and registration/retry uses Shipment Authority and its
persisted mapping snapshot.

Tracking is pull-based through `TrackShipments`. The shared service binds the
result to the existing tenant shipment, applies lifecycle transition policy,
persists SQL authority, and emits guarded fulfillment/order synchronization.
Recovery re-enters these guarded services and does not make the adapter an
authority.

Pickup is advertised as a carrier capability but has no implemented adapter
operation in the shared runtime interface. It was reviewed as unimplemented and
was not invented in this phase.

## Compatibility Matrix

| Surface | Before | After | Verdict |
|---|---|---|---|
| Production create endpoint | documented JSON path | unchanged | compatible |
| Sandbox host | `ws.sbx.aramex.net` frozen by PCC baseline | unchanged | conditional on account certification |
| `ClientInfo` authentication | correct field shape | unchanged | compatible |
| Required contact data | missing `CompanyName`; shipper email missing | populated from existing trusted request/credential values | hardened |
| Domestic product pairing | service-dependent type but forced `DOM` | `DOM` + `OND` | compatible |
| International product pairing | international type forced under `DOM` | `EXP` + `PPX`/`EPX` | hardened |
| Shipment identity | processed shipment `ID` | unchanged | compatible |
| Label request | report `9201`, `URL` | unchanged | compatible |
| Label response | incorrectly read from response root | read from processed shipment; URL and file contents supported | hardened |
| Error envelope | `HasErrors` / first notification | unchanged | compatible, limited diagnostics |
| Tracking request | `TrackShipments` with AWB list | unchanged | compatible |
| Tracking statuses | local mapping table | unchanged | conditional; current account samples required |
| Shipment cancellation | fabricated success, no I/O | deterministic unsupported failure | hardened/fail-closed |
| General webhook | advertised and parsed without published trust contract | capability no longer advertised | hardened/fail-closed |
| Pickup | not implemented | unchanged | out of runtime |
| Retry/idempotency | no adapter retry; shared durable request evidence | unchanged | safe fail-closed, provider reconciliation limited |
| Geographic authority | pre-resolved mapping only | unchanged | compliant |
| Shipment Authority | shared registration/lifecycle boundary | unchanged | compliant |

## Breaking Changes

No official public Aramex endpoint retirement or replacement was found.

Application-visible corrections:

- `SupportsCancellation` changed from `true` to `false`.
- `SupportsWebhooks` changed from `true` to `false`.
- shipment cancellation now returns
  `ARAMEX_SHIPMENT_CANCELLATION_UNSUPPORTED` instead of a false success.
- international shipments now serialize an `EXP` product group.
- streamed label contents are now admitted through `LabelData`.

These are intentional correctness changes. Consumers must not interpret a
locally accepted cancellation as provider-confirmed cancellation.

## Runtime Hardening

Modified runtime:

- `PointOfSale.Infrastructure/Shipping/Adapters/AramexAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/AramexCarrierDefinition.cs`

Hardening was limited to:

- official product group/type pairing;
- mandatory contact field serialization;
- correct processed-shipment label deserialization;
- embedded label data support;
- truthful cancellation and webhook capability declarations;
- fail-closed unsupported shipment cancellation.

No retry was added because Aramex publishes no idempotency contract and blind
create retries can duplicate AWBs.

## Regression Tests

Added:

- `PointOfSale.Tests/Infrastructure/AramexOfficialContractCompatibilityTests.cs`

The tests freeze:

- exact production create URL;
- domestic `DOM` / `OND` pairing;
- mandatory contact company serialization;
- processed-shipment label URL and file-content admission;
- fail-closed cancellation without network I/O;
- truthful capability flags.

Existing Shipping and Geographic architecture tests continue to freeze carrier
resolution, Shipment Authority, published mapping resolution, recovery, and
the prohibition on adapter-owned geography.

## Verification

| Command | Result |
|---|---|
| `dotnet build PointOfSale.sln --no-restore` | passed; 0 warnings, 0 errors |
| focused Aramex compatibility tests | passed; 3/3 |
| relevant Shipping and Geographic regressions | passed; 59/59 |

No live Aramex call was made because production credentials were not present and
the audit must not mutate an external carrier account.

## Risks

1. Aramex does not publish a dated changelog, formal deprecation policy,
   idempotency guarantee, retry schedule, or public rate limit.
2. The public manuals are legacy documents. The exact tenant account,
   environment host, enabled products, and credentials must be certified with
   Aramex before activation.
3. The public tracking material does not provide a reliably versioned canonical
   status registry. The current mapping must be compared with real sandbox
   responses for the contracted account; unknown codes must be operationally
   quarantined.
4. The adapter cannot reconcile an uncertain create by
   `CarrierRequestId`; a timeout after provider acceptance therefore remains a
   manual reconciliation case.
5. Pickup creation/cancellation is official but not implemented in the shared
   adapter interface.
6. `ParseWebhook` remains legacy code reachable through a generic controller,
   but Aramex no longer advertises webhook capability. It must not be enabled
   without an official account-specific payload and authentication contract.

## Final Recommendation

Accept the minimum runtime hardening and keep Aramex disabled for unconditional
production rollout.

Proceed to an **Aramex account certification phase** using current
vendor-issued WSDL/sample artifacts and sandbox credentials. Freeze one
successful and one failing create, URL and streamed labels, tracking histories
for every operational state, account-specific product codes, and timeout
reconciliation. Do not enable shipment cancellation or webhooks without a
separate official Aramex contract.

## Explicit Success Answers

- Reviewed ONLY Aramex runtime? **Yes**, plus only the shared registration,
  tracking, cancellation, recovery, audit, carrier-resolution, and Geographic
  authority boundaries required to verify it.
- Runtime files modified: **2** — `AramexAdapter.cs`,
  `AramexCarrierDefinition.cs`.
- Backend files modified: **2 runtime files plus 1 backend test file**.
- Frontend files modified: **None**.
- Official contract verified? **Yes, against the current public Aramex portal,
  official Shipping Services manual, and published `service_1_0` surface;
  account-private terms remain unavailable**.
- Runtime compatible? **Compatible for hardened create/authentication/label and
  pull tracking request surfaces; cancellation/webhooks are explicitly
  unsupported; status admission remains conditional on account certification**.
- Breaking changes found? **Yes — four runtime contract defects and two false
  capability declarations**.
- Runtime hardening required? **Yes, completed minimally**.
- Production compatible? **Conditionally. Not approved for unconditional
  activation until account-specific sandbox and status certification passes**.
- Recommend next phase: **Aramex account-specific sandbox/WSDL/status
  certification and operational reconciliation drill**.

No new cross-system architecture truth was discovered. The five architecture
`.ai-context` files were therefore not updated.
