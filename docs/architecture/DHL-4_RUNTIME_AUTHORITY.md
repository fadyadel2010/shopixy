# DHL-4 - Runtime Authority

Status: Runtime authority certified after DHL webhook status hardening
Date: 2026-07-07
Scope: DHL runtime authority boundaries only

## Executive Summary

DHL runtime authority is preserved after one narrow hardening change.

DHL owns no Shipment, Order, Checkout, Warehouse, Inventory, Customer, Product, Geography, Customs, SQL Truth, Shipment Lifecycle, or Carrier Mapping authority. DHL owns only provider wire behavior: MyDHL authentication, request serialization, response deserialization, HTTP transport, bounded timeout/retry behavior, label/tracking response parsing, and official status mapping as evidence.

One runtime authority violation was found: `DhlAdapter.ParseWebhook` still admitted unknown DHL webhook status codes by defaulting them to `InTransit`. Tracking polling already failed unknown statuses closed, but generic webhook handling can call the DHL parser. That default could let provider evidence become lifecycle mutation input without an admitted mapping.

The DHL parser now rejects unknown webhook statuses by returning no parse result. Backend webhook admission then rejects the payload before SQL mutation. No Shipping, Geography, Customs, Shipment Lifecycle, Carrier Resolution, SQL, backend business-domain, or frontend redesign was performed.

## Authority Matrix

| Concern | Owner | DHL role | Verdict |
|---|---|---|---|
| Shipment identity and SQL row | Backend Shipment/SQL | Receives `CarrierRequestId`; returns provider evidence | DHL owns none |
| Order | Backend Order authority | Reads no order persistence | DHL owns none |
| Checkout | Checkout backend/frontend candidate flow | No checkout dependency | DHL owns none |
| Warehouse/origin | Warehouse plus Shipment Origin snapshot | Serializes `ShipmentOriginDto` fields | DHL owns none |
| Inventory | Inventory/fulfillment domains | No inventory dependency | DHL owns none |
| Customer | Customer/order recipient evidence | Serializes recipient contact fields | DHL owns none |
| Product | Product/Customs source evidence | Reads no product data | DHL owns none |
| Geography | Geographic SQL/catalog/mapping authorities | Serializes admitted address text/ISO country only | DHL owns none |
| Origin/Destination | Shipment snapshots | Serializes DTO/snapshot fields | DHL owns none |
| Carrier Mapping | `CarrierGeographicMappingService` and Shipment snapshot | Receives resolved evidence; does not query or serialize provider IDs | DHL owns none |
| Customs | Customs Evidence and Shipment Customs Snapshot | Serializes `CarrierNeutralCustomsDto` into DHL `exportDeclaration` | DHL owns none |
| Shipment Lifecycle | Backend SQL plus `ShipmentLifecycleTransitionPolicy` | Emits provider status evidence only | DHL owns none |
| SQL Truth | PostgreSQL backend services | No EF/SQL dependency | DHL owns none |
| Financial State | Order/payment/accounting authority | No financial mutation | DHL owns none |
| Labels | Backend carrier response admission | Parses label data returned by DHL | Evidence only |
| Tracking/status | Backend tracking/lifecycle admission | Parses known DHL status codes only | Evidence only |

## Runtime Ownership

DHL may own only:

- MyDHL Basic authentication header construction.
- MyDHL base URL and route serialization for rates, shipment creation, and tracking.
- JSON request serialization and response deserialization.
- HTTP transport through the named DHL client.
- Bounded timeout and existing retry behavior.
- Official response parsing for tracking, labels, provider identifiers, and DHL status codes.

DHL does not own business rules. It does not query `ApplicationDbContext`, Geographic services, Customs admission services, Carrier Mapping services, Product, Warehouse, Checkout, Inventory, Orders, or Shipment Lifecycle policy. It does not persist SQL state.

## Response Admission

Provider responses remain Evidence only.

Carrier create responses become SQL truth only after `CarrierShipmentService` validates durable provider shipment identity, tracking number, optional tracking URL, optional label URL, embedded label data, label format, provider metadata JSON, and initial status mapping.

Tracking responses are identity-bound to the requested tracking number. Unknown DHL polling statuses return `TRACKING_STATUS_UNADMITTED`. Unknown DHL webhook statuses now return no parse result and are rejected as invalid payload before webhook SQL/lifecycle admission.

Carrier cannot directly mutate Shipment, Orders, Inventory, Geography, or Customs.

## Lifecycle Authority

The lifecycle path remains:

```text
Provider status
  -> DHL known-status evidence mapping
  -> Backend response/webhook/tracking admission
  -> ShipmentLifecycleTransitionPolicy
  -> SQL Shipments
```

Unknown status, unknown lifecycle, invalid transition, backward transition, and skipped transition reject before SQL mutation. Carrier create initial status must transition from the current SQL shipment status through `ShipmentLifecycleTransitionPolicy`; otherwise `CARRIER_STATUS_TRANSITION_INVALID` is persisted as failure evidence.

## Recovery Authority

Recovery is snapshot-only.

Carrier registration and retry reuse:

- Shipment Origin Snapshot.
- Shipment Destination Snapshot.
- Shipment Customs Snapshot where international customs is required.
- Carrier Mapping Snapshot.
- Shipment identity and `CarrierRequestId`.

Recovery must never re-read Product, Checkout, Warehouse origin configuration, live Carrier Geography, current mapping release after snapshot acquisition, provider responses, labels, invoices, Mongo, Redis, browser state, or projections to rebuild DHL registration authority.

## Fail-Closed Behavior

DHL runtime rejects or withholds admission for:

- Unknown provider statuses.
- Malformed tracking response identity.
- Missing or malformed carrier shipment identity.
- Missing or malformed tracking number.
- Malformed tracking URL.
- Malformed label URL.
- Malformed embedded label data.
- Unsupported label format.
- Malformed provider metadata.
- Invalid initial status mapping.
- Invalid lifecycle transition.
- Missing customs snapshot DTO for international shipment creation.
- Incomplete customs snapshot data.
- Unsupported customs units/enums.
- Missing credentials.
- Unsupported shipment cancellation.

## Regression Summary

Regression coverage now freezes:

- Runtime Authority: `DhlRuntimeAuthorityTests`.
- Response Admission: `CarrierShipmentService` carrier-result admission guards.
- Recovery: origin, customs, carrier mapping, and shipment snapshots before adapter invocation.
- Lifecycle Admission: `ShipmentLifecycleTransitionPolicy` for create, sync, webhook, cancellation, and admin/direct writes.
- Tracking Admission: unknown DHL tracking and webhook statuses fail closed.
- Status Mapping: known DHL status codes map only to canonical shipment statuses.
- Serializer Purity: DHL adapter has no SQL, Geographic, Customs admission, lifecycle policy, Product, Checkout, Warehouse, or Inventory dependencies.

## Verification

Commands executed:

- `dotnet build PointOfSale.sln --no-restore -nologo -v quiet`: PASSED.
- `dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore -nologo -v quiet`: PASSED with existing warnings only.
- Focused DHL tests (`DhlOfficialContractCompatibilityTests`, `DhlGeographicCompatibilityTests`, `DhlRuntimeAuthorityTests`): PASSED, 20 passed.
- Shipping tests (`FullyQualifiedName~Shipping`): FAILED, 82 passed and 5 failed. The failures were outside the DHL runtime adapter boundary: one CommerceHub finance shipping-only refund test, two ShippingWebhookTrustPipeline signature/persistence tests, and two ShipmentExternalRegistrationSafety retry/reconciliation tests.
- Shipment tests (`FullyQualifiedName~Shipment`): FAILED, 121 passed and 2 failed. Both failures were the same ShipmentExternalRegistrationSafety retry/reconciliation tests observed in the Shipping slice.
- Lifecycle tests (`FullyQualifiedName~Lifecycle`): FAILED, 225 passed and 50 failed. Failures were broad existing payment, inventory reservation, COD, return, product lifecycle, and frontend presentation authority failures; they do not indicate DHL adapter ownership of shipment lifecycle authority.
- Authority tests (`FullyQualifiedName~Authority`): FAILED, 1191 passed and 64 failed. Failures were broad existing cross-system authority regressions outside the DHL runtime adapter boundary.

Earlier frozen verification from DHL-1 through DHL-3 and GEO-9/GEO-10 remains relevant. DHL-4 adds a passing focused DHL runtime authority slice and documents wider repository-suite failures as residual risks, not as DHL ownership findings.

## Risks

- DHL webhooks remain capability-disabled because no official MyDHL REST webhook contract is certified. The parser is now fail-closed if invoked through generic webhook endpoints.
- DHL external production activation remains blocked by GEO-10: repository-proven published mapping coverage is absent and DHL sandbox/account/country evidence is still required.
- DHL live sandbox replay was not performed in this phase.
- Existing broad Shipping, Shipment, Lifecycle, and Authority-filter failures remain outside the DHL runtime authority change and should be resolved in their owning domains.

## Final Recommendation

Approve DHL-4 runtime authority with the narrow webhook unknown-status hardening included. DHL-specific runtime authority regression tests pass. Do not treat the broad-suite failures as approval for DHL to own Shipping, Shipment Lifecycle, Finance, Inventory, Product, Geography, Customs, or frontend authority.

Recommended next phase: DHL credentialed sandbox certification and provider-country rollout, limited to live MyDHL replay, official contract evidence, and governed mapping coverage proof. Do not redesign Shipping, Geography, Customs, Shipment Lifecycle, Carrier Resolution, Checkout, Warehouse, Product, or frontend flows.

## Explicit Success Answers

- Reviewed ONLY DHL Runtime Authority? Yes.
- Runtime modified? Yes, DHL adapter status-admission hardening only.
- Backend modified? Yes, DHL adapter plus focused backend/architecture tests and this report.
- Frontend modified? No.
- Shipment owned by DHL? NO.
- Geography owned by DHL? NO.
- Customs owned by DHL? NO.
- Lifecycle owned by DHL? NO.
- Carrier Mapping owned by DHL? NO.
- Business logic inside adapter? NO.
- Recovery snapshot-only? Yes.
- Authority preserved? Yes.
- Production compatible? Authority-compatible, but external DHL production activation remains conditional on GEO-10 mapping coverage and DHL sandbox/account evidence.
- Recommend next phase: DHL credentialed sandbox certification and governed provider-country rollout.

## AI Context Update Decision

The `.ai-context` files were not updated. DHL-4 discovered and fixed a provider-specific runtime admission defect; it did not change permanent cross-system architecture ownership.
