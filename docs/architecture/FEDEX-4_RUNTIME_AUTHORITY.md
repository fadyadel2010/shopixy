# FEDEX-4 - Runtime Authority

Date: 2026-07-07
Scope: FedEx Runtime Authority only
Status: COMPLETE

## Executive Summary

FEDEX-4 reviewed only FedEx Runtime Authority against the frozen Shipping,
Geography, Customs, Shipment Origin, and Runtime Architecture certifications.

FedEx is registered as a first-class carrier foundation, but FedEx runtime
execution is not certified. The `FedExAdapter` has no HTTP transport, no OAuth
token execution, no status mapper, no tracking execution, no label admission,
no pickup execution, no address-validation execution, no webhook admission, and
no provider-response admission path. Every implemented FedEx operation fails
closed with `FEDEX_NOT_YET_CERTIFIED` or returns no webhook parse result.

No true FedEx runtime authority violation was found. Runtime modified: No.
Backend production code modified: No. Frontend modified: No. Focused
architecture regression coverage was added.

The certified Shipping Platform fully models FedEx runtime authority. FedEx
requires no new Shipping Platform capability.

## Runtime Authority Matrix

| Concern | Authority | FedEx role | Runtime status |
|---|---|---|---|
| Shipment lifecycle | Shipping Platform, SQL `Shipment`, `ShipmentLifecycleTransitionPolicy` | Provider evidence only | FedEx cannot mutate lifecycle |
| Shipment identity | SQL `Shipment` and backend shipment services | None | Consumes existing carrier request id only |
| Shipment persistence | PostgreSQL through backend services | None | Adapter has no DB access |
| Order state | Order domain | None | Adapter never sees `Order` |
| Checkout | Checkout domain | None | No FedEx checkout path |
| Customer Address | Customer/Checkout plus Geographic admission | None | No customer profile lookup |
| Warehouse | Warehouse domain plus Shipment Origin snapshot | None | Consumes `ShipmentOriginDto` only through shared request |
| Geography | Geographic Platform and published carrier mappings | Consumer only | No mapping lookup in adapter |
| Carrier Mapping | Published mapping release and Shipment mapping snapshot | Consumer only | Shared service resolves before adapter |
| Country Catalog | Geographic Platform | None | No catalog lookup |
| Customs | Customs Platform and Shipment Customs Snapshot | Consumer only | Future serializer must consume DTO only |
| Tracking Truth | Shipping Platform after admitted carrier evidence | Evidence only | Current FedEx tracking fails closed |
| Order Truth | Order domain SQL | None | No order authority |
| SQL Truth | PostgreSQL backend services | None | No SQL in FedEx runtime files |
| Business Rules | Platform policies/services | None | Adapter contains no business policy |
| Platform Decisions | Certified platform domains | None | No redesign needed |

FedEx owns none of Shipment lifecycle, Shipment identity, Shipment persistence,
Order state, Checkout, Customer Address, Warehouse, Geography, Carrier Mapping,
Country Catalog, Customs, Tracking Truth, Order Truth, SQL Truth, Business
Rules, or Platform Decisions.

## Snapshot Consumption

FedEx may consume only:

- Shipment Snapshot.
- Shipment Origin Snapshot.
- Shipment Destination Snapshot.
- Shipment Customs Snapshot.
- Carrier Mapping Snapshot.
- Carrier-neutral DTOs.
- OAuth configuration.
- FedEx endpoint configuration.

The shared carrier registration pipeline already enforces the snapshot order:

```text
Shipment / Order SQL authority
  -> Shipment Origin Snapshot
  -> Shipment Destination Snapshot
  -> Carrier Mapping Snapshot
  -> Shipment Customs Snapshot when cross-border
  -> Carrier-neutral DTO
  -> Carrier adapter
```

Current FedEx code does not serialize those DTOs to FedEx wire traffic. It is
therefore authority-preserving and not production-active.

## Recovery Model

Recovery uses only persisted Shipment, Origin, Destination, Customs, Carrier
Mapping, and carrier-request snapshots.

Recovery uses only persisted Shipment, Origin, Destination, Customs, Carrier Mapping, and carrier-request snapshots.

Provider lookups, Geographic SQL, Warehouse SQL, Checkout, Customer profile,
Redis, and Mongo are forbidden recovery authorities for FedEx runtime.

Provider lookups, Geographic SQL, Warehouse SQL, Checkout, Customer profile, Redis, and Mongo are forbidden recovery authorities for FedEx runtime.

The shared carrier service preserves this boundary:

- Origin recovery reuses `OriginSnapshotJson` and fails closed after external
  registration if the snapshot is missing or malformed.
- Carrier mapping recovery reuses `CarrierGeographicMappingSnapshotJson` and
  never resolves against a newer release after a snapshot exists.
- Cross-border customs recovery reuses `ShipmentCustomsSnapshot` evidence.
- Carrier request recovery asks the adapter only by persisted carrier request
  id; FedEx currently returns `null`, which blocks duplicate create.

## Tracking Authority

FedEx official tracking supports tracking by tracking number and other methods
such as door tag, reference, FedEx Office order, and TCN. The current runtime
implements none of those FedEx tracking calls.

Tracking remains backend-owned. A future FedEx tracking mapper must resolve one
existing tenant shipment, validate provider trust and idempotency, map only
known official FedEx status evidence, and pass `ShipmentLifecycleTransitionPolicy`
before persistence.

Unknown shipment status and unknown tracking event must fail closed.

## Label Authority

FedEx official labels are produced through Ship API shipment creation. FedEx
documents label image type, printer format, label stock, and successful
shipment response label information.

Current FedEx label runtime fails closed. A future label parser must admit only
known FedEx label evidence and must reject missing label data, malformed URL,
malformed encoded label, unknown label type, unsupported label format, and
unexpected response shape before SQL persistence.

## OAuth Authority

FedEx official OAuth uses `POST /oauth/token`, bearer tokens, `expires_in:
3600`, client credentials, and documented child credential grant types.

Current `FedExOAuthClient` does not request tokens. It returns
`FEDEX_NOT_YET_CERTIFIED`.

OAuth authority is limited to FedEx credential exchange and token caching in a
future transport phase. Unknown OAuth response, missing access token, unknown
token type, missing expiration, malformed scope, child credential mismatch, or
token-generation response schema drift must fail closed.

## Carrier Mapping Authority

FedEx is a registered provider identity, so the GEO-9 carrier mapping boundary
applies before any future FedEx shipment registration.

FedEx does not own provider mapping releases. It must consume only the resolved
mapping snapshot passed in carrier-neutral request evidence. The FedEx adapter
contains no `ICarrierGeographicMappingService`, no Geographic SQL access, no
catalog lookup, no alias lookup, no hierarchy lookup, and no provider-ID repair
logic.

No published FedEx mapping coverage is proven in the repository, so production
FedEx registration remains data-blocked even if transport is later implemented.

## Failure Handling

These conditions must fail closed:

| Unknown or malformed input | Required behavior |
|---|---|
| Unknown shipment status | Reject provider evidence; no lifecycle mutation |
| Unknown tracking event | Reject event; no timeline or status persistence |
| Unknown label type | Reject label evidence |
| Unknown service | Reject before serialization or persistence |
| Unknown customs field | Reject carrier customs mapping; no provider call |
| Unknown OAuth response | Reject token; no provider call |
| Unknown provider payload | Reject provider response; no SQL admission |
| Unknown webhook payload | Return no parse result; no mutation |
| Unknown response schema | Reject response admission |

Current FedEx runtime satisfies this by having no transport path and no parser
that can admit provider payloads.

## Breaking Issues

| Issue | Severity | Status |
|---|---:|---|
| FedEx HTTP transport is not implemented | Blocking for production activation | Accepted; fail closed |
| FedEx OAuth execution and token cache are not implemented | Blocking | Accepted; fail closed |
| FedEx shipment creation/status/label response admission is not implemented | Blocking | Accepted; fail closed |
| FedEx tracking/status mapping is not implemented | Blocking | Accepted; fail closed |
| FedEx pickup/address-validation/service-availability execution is not implemented | Blocking | Accepted; fail closed |
| FedEx webhook trust/admission is not implemented | Blocking | Accepted; no parse result |
| FedEx published carrier mapping coverage is not proven | Blocking for production registration | Accepted; data rollout required |

No breaking platform-domain issue was found.

## Regression Coverage

Updated:

- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`

Coverage added:

- Snapshot consumption before FedEx adapter invocation.
- Authority isolation from SQL, Geography lookup, Checkout, Warehouse, Redis,
  Mongo, and carrier mapping service access in FedEx runtime files.
- Fail-closed FedEx operation, OAuth, webhook, and response-boundary decisions.
- Recovery boundaries: snapshot-only, no provider/domain/cache reconstruction.
- Explicit FEDEX-4 final recommendation and no-new-platform-capability decision.

Existing coverage already freezes:

- FedEx carrier registration and DI.
- Official endpoint and OAuth configuration surface.
- No HTTP execution in FedEx adapter/OAuth/contract code.
- Contract-only DTO validation and unknown enum fail-closed behavior.
- Address Validation as provider evidence only.
- Geographic compatibility and provider-ID isolation.

## Verification

Official FedEx documentation reviewed:

- FedEx API Authorization documentation confirms OAuth 2.0 bearer token
  authentication, required credential fields, `POST /oauth/token`, one-hour
  token expiration, and `expires_in: 3600`.
- FedEx Ship API documentation confirms create shipment, validation, required
  shipment inputs, label specification, tracking number and label output, async
  retrieval, and cancellation.
- FedEx Basic Integrated Visibility documentation confirms tracking methods and
  up to 30 tracking numbers per request.
- FedEx Pickup Request API documentation confirms availability, create pickup,
  and cancel pickup operations with Express/Ground constraints.
- FedEx Address Validation API documentation confirms address resolution and
  suggestion-only use; it does not become deliverability, customer-address, or
  Geography authority.

Repository verification:

- `FedExAdapter` has no HTTP transport and all runtime operations fail closed.
- `FedExOAuthClient` has no token request implementation and fails closed.
- `FedExContract` is standalone DTO/validation code and has no transport or SQL.
- `CarrierShipmentService` resolves/restores origin, carrier mapping, and
  customs snapshots before adapter invocation.
- Carrier response admission and lifecycle transition policy remain in shared
  Shipping Platform code.
- No frontend FedEx runtime was added.
- No `.ai-context` updates were made because no new permanent cross-provider
  architectural truth was discovered.

## Final Recommendation

Approve FEDEX-4 as a runtime-authority certification.

Do not activate FedEx production traffic. The next phase should be FedEx
transport/response-admission implementation planning only after provider
mapping coverage, OAuth execution, token caching, request serialization
admission, response admission, tracking status mapping, labels, pickup,
address-validation, customs mapping, webhook trust, sandbox proof, and
operational rollout are separately certified.

## Explicit Success Answers

- Reviewed ONLY FedEx Runtime Authority? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No production backend code modified; regression tests and this architecture report were added.**
- Frontend modified? **No.**
- Shipment owned by FedEx? **No.**
- Tracking owned by FedEx? **No.**
- Geography owned by FedEx? **No.**
- Customs owned by FedEx? **No.**
- Carrier Mapping owned by FedEx? **No.**
- Business logic inside adapter? **No.**
- Recovery snapshot-only? **Yes.**
- Authority preserved? **Yes.**
- Production compatible? **No. FedEx runtime remains fail-closed and operational/data coverage is not proven.**
- Does FedEx require any new Shipping Platform capability? **No.**
- Recommend next phase: **FedEx transport and response-admission planning after mapping/credential/operational prerequisites; do not redesign frozen platform domains.**
