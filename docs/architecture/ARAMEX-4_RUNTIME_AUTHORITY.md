# ARAMEX-4 — Runtime Authority

Audit date: 2026-07-06  
Scope: Aramex runtime authority only

## Executive Summary

Aramex remains a carrier transport adapter. Shipment, tracking, geography,
pricing, order, customer, warehouse, inventory, financial, and SQL authority
remain backend-owned.

The audit found three real adapter-boundary violations:

1. unknown Aramex tracking/webhook statuses were converted to canonical
   `InTransit`;
2. failed or incomplete Aramex rate responses produced invented local prices;
3. create and tracking responses were not explicitly rejected when their
   returned order/shipment/tracking identity disagreed with the request.

Minimal adapter-only hardening now rejects those inputs. No shared Shipping,
Geographic, Warehouse, Inventory, Payment, Checkout, CommerceHub, Affiliate,
frontend, schema, or recovery redesign was performed.

Runtime authority is preserved after hardening. Production activation remains
conditional on the account-specific Aramex and GEO-10 certification blockers
already recorded by ARAMEX-1 through ARAMEX-3.

## Authority Matrix

| Concern | Authority owner | Aramex role |
|---|---|---|
| Shipment identity | Backend SQL `Shipment` | Returns candidate carrier identifiers for an existing shipment |
| Shipment status | Backend lifecycle policy and SQL `Shipment` | Supplies status evidence only |
| Carrier shipment ID | SQL shipment registration evidence after response admission | Supplies candidate ID |
| Tracking number | SQL `Shipment` after exact response binding | Supplies candidate tracking value |
| Tracking URL | Backend-persisted admitted carrier metadata | Serializes provider URL candidate |
| Carrier status | Aramex wire evidence; not business authority | Translates allowlisted code to candidate canonical status |
| Shipment lifecycle | `ShipmentLifecycleTransitionPolicy` plus current SQL lineage | No authority |
| Carrier events | Verified backend carrier boundary | Transport evidence only |
| Recovery | Backend recovery service using current SQL and immutable snapshots | Lookup/transport only where supported |
| Audit | Backend SQL evidence, outbox/status events, geographic audit, telemetry, structured logs | Emits diagnostic transport context |
| SQL truth | PostgreSQL shipment/order/configuration/webhook rows | No authority |
| Projection | SQL-derived consumers | No authority |
| Presentation | Backend DTOs/frontend rendering | No authority |

Aramex does not own Order, Customer, Warehouse, Inventory, Financial State,
Canonical Geography, Shipment Origin, or Destination. The request is assembled
from admitted backend snapshots; provider responses cannot rewrite those
snapshots.

## Runtime Pipeline

```text
trusted tenant + current SQL order/shipment
  -> shipping method and registered carrier admission
  -> immutable origin snapshot
  -> immutable destination snapshot
  -> immutable carrier mapping snapshot
  -> stable carrier request claim
  -> Aramex adapter transport
  -> response identity/shape/status admission
  -> locked SQL shipment/order recomposition
  -> lifecycle policy
  -> SQL persistence
  -> fulfillment/order synchronization and events
```

Registration, tracking, webhook, and recovery inputs never bypass current
backend scope and lifecycle checks.

## Status Ownership

The adapter's mapping table is a translation allowlist, not lifecycle
authority. Known Aramex codes become candidate `ShipmentStatus` values. The
backend then applies the canonical transition policy before persistence.

Unknown, empty, or malformed status evidence now fails closed. It is no longer
converted to `InTransit`. Tracking events must also contain timestamps, and a
tracking response must identify exactly the requested tracking number.

Aramex does not advertise webhook support. The legacy parser remains callable
through the generic interface, but now rejects unknown status and missing or
malformed time evidence; production enablement still requires a separately
certified Aramex webhook trust contract.

## Recovery Model

Recovery is backend-owned and re-enters the same guarded services.

- Shipment identity and state are reloaded from SQL.
- Origin uses `Shipment.OriginSnapshotJson`.
- Destination uses the immutable shipment snapshot copied from Order.
- Carrier geography uses
  `Shipment.CarrierGeographicMappingSnapshotJson`.
- A stored mapping snapshot is validated and reused; the current mapping
  release is not queried to rewrite history.
- A completed registration with missing or malformed origin evidence fails
  closed.
- Carrier configuration supplies credentials/environment eligibility only. It
  cannot rebuild Shipment Origin or carrier mapping authority.
- Provider responses can reconcile admitted carrier identifiers but cannot
  rebuild shipment, order, geography, warehouse, inventory, or financial
  authority.

## Audit Model

| Activity | Evidence |
|---|---|
| Registration | carrier request ID, request/attempt timestamps, attempt count, state, persisted raw response, structured logs |
| Tracking | persisted raw tracking response, last-sync time, status event when changed, structured logs |
| Status transition | canonical shipment status event/outbox and SQL timestamps |
| Carrier response | admitted raw create/tracking evidence on Shipment |
| Recovery | reconciliation telemetry, state/error evidence, structured worker logs |
| Failure | external-registration state/error/timestamp plus structured logs |
| Retry | stable carrier request ID, durable attempt count/state, duplicate-prevention and reconciliation telemetry |
| Geographic admission | append-only `GeographicAudit` success/failure evidence |

The runtime has no standalone append-only Aramex transport-attempt ledger.
Existing SQL shipment evidence, webhook logs where applicable, outbox events,
geographic audit, telemetry, and logs are sufficient for current authority
preservation, but a dedicated carrier-attempt audit ledger remains an
observability improvement rather than an authority blocker.

## Idempotency

| Duplicate | Collapse mechanism |
|---|---|
| Registration/create | stable `{tenant, shipment}` carrier request ID, SQL claim/attempt state, post-call locked recomposition |
| Carrier response | existing external registration is reconciled; persistence uniqueness and current SQL state win |
| Tracking sync | same status is a no-op; lifecycle regression is rejected |
| Webhook/callback | durable event identity/payload hash, exact tenant/carrier/shipment resolution, status-change no-op |
| Recovery | re-enters current SQL authority and guarded registration/reconciliation |
| Shipment creation | serializable creation and one-active-shipment invariant |

Aramex publishes no create-idempotency guarantee. After an uncertain external
create, blind retry remains blocked unless reconciliation proves the existing
registration.

## Breaking Issues

Closed in ARAMEX-4:

1. Unknown carrier status could invent `InTransit`.
2. Rate failure/missing amount could invent EGP prices.
3. A mismatched processed-shipment reference was not explicitly rejected.
4. A tracking result was not explicitly required to match exactly the
   requested tracking number.
5. Missing tracking timestamps could be replaced with local current time.

No carrier overwrite of Shipment, Order, Customer, Warehouse, Inventory,
Financial State, Canonical Geography, Shipment Origin, Destination, or SQL
Truth was found after these changes.

## Runtime Hardening

Modified runtime:

- `PointOfSale.Infrastructure/Shipping/Adapters/AramexAdapter.cs`

Hardening:

- removed invented fallback quote amounts;
- rejected missing/invalid rate amount or currency;
- rejected mismatched create response order/shipment references;
- required one exact tracking-result identity;
- rejected unknown tracking and webhook statuses;
- rejected tracking events without provider timestamps.

No shared backend runtime file required modification.

## Regression Tests

Updated:

- `PointOfSale.Tests/Infrastructure/AramexOfficialContractCompatibilityTests.cs`

New freezes cover mismatched create references, mismatched tracking identity,
unknown tracking status, and prohibition on invented quote prices. Existing
architecture tests continue to freeze registration response admission,
lifecycle policy, tenant/order scope, webhook deduplication, recovery snapshot
reuse, and canonical Geographic isolation.

## Verification

| Command | Result |
|---|---|
| `dotnet build PointOfSale.sln --no-restore` | Passed; 75 pre-existing warnings, 0 errors |
| focused `AramexOfficialContractCompatibilityTests` | Passed; 14/14 |
| relevant Shipping/Shipment/Geographic slice | 58/60 passed |

The two failing broad-slice tests are existing
`ShipmentExternalRegistrationSafetyTests` fixtures that do not provide the
SHIP-ORIGIN-1 admitted warehouse origin now required by runtime. They fail
before adapter I/O with `SHIPMENT_ORIGIN_NOT_ADMITTED`; neither failure is
caused by ARAMEX-4 hardening. This fixture drift means the full requested
regression gate is not completely green.

## Risks

1. Account-specific Aramex status samples remain uncertified. Unknown codes now
   stop mutation safely but require operational mapping review.
2. Aramex create has no published idempotency/reconciliation-by-request-ID
   guarantee; uncertain create outcomes can require manual reconciliation.
3. General Aramex webhooks remain unsupported and must stay disabled without an
   official payload/authentication contract.
4. GEO-10 still records no repository-proven published Aramex mapping coverage.
5. Production warehouse-origin admission coverage and the additive
   SHIP-ORIGIN-1 migration remain deployment prerequisites.
6. Two shipment external-registration regression fixtures need admitted origin
   setup before the complete Shipping regression gate can be certified green.

## Final Recommendation

Accept the minimal ARAMEX-4 hardening. Authority separation is correct and the
adapter is now fail-closed for the discovered status, identity, timestamp, and
pricing violations.

Do not approve unconditional Aramex production activation yet. Repair the two
stale origin test fixtures, certify tenant warehouse-origin coverage, publish
and certify GEO-10 Aramex mappings, and complete account-specific sandbox/WSDL,
status, and uncertain-create reconciliation certification.

## Explicit Success Answers

- Reviewed ONLY Runtime Authority? **Yes.**
- Runtime files modified: **1 — `AramexAdapter.cs`.**
- Backend files modified: **1 runtime file and 1 backend test file.**
- Frontend files modified: **None.**
- Authority preserved? **Yes, after minimal hardening.**
- Carrier owns Shipment? **NO.**
- Carrier owns Tracking? **NO.**
- Carrier owns Geography? **NO.**
- Carrier owns Warehouse? **NO.**
- Carrier owns SQL Truth? **NO.**
- Recovery verified? **Yes, structurally and by existing snapshot authority
  tests; the broad slice exposed two stale origin fixtures.**
- Idempotency verified? **Yes for the implemented registration, tracking,
  webhook/callback, recovery, shipment creation, and response-collapse
  controls; Aramex provider-side create idempotency is not published.**
- Breaking issues found? **Yes — five, closed in the adapter.**
- Runtime hardening required? **Yes, completed minimally.**
- Production compatible? **Conditionally; not approved for unconditional
  activation until the listed account, mapping, origin-coverage, and regression
  prerequisites pass.**
- Recommend next phase: **Aramex account-specific sandbox/status and
  uncertain-create reconciliation certification, paired with GEO-10 mapping
  and warehouse-origin rollout certification.**

No new cross-system architecture truth was discovered. The five architecture
`.ai-context` files were not updated.
