# SHIPPING.PLATFORM.FINAL - Shipping Platform Architecture Certification Freeze

Date: 2026-07-07
Scope: Shipping Platform architecture across implemented carrier providers
Status: FINAL

## Executive Summary

The Shipping Platform was reviewed as one unified architecture across Bosta,
Aramex, and DHL. This certification did not re-audit provider business
contracts and did not certify operational rollout.

The approved platform foundation is now consistent:

```text
Shipment / Order SQL authority
  -> Shipment Origin Snapshot
  -> Shipment Destination Snapshot
  -> Carrier Mapping Snapshot
  -> Shipment Customs Snapshot when applicable
  -> Carrier-neutral DTOs
  -> Carrier adapter serialization / transport
  -> Backend carrier response admission
  -> ShipmentLifecycleTransitionPolicy
  -> SQL persistence, audit, and snapshot-only recovery
```

One genuine cross-provider architecture inconsistency was found: Bosta mapped
unknown provider tracking and webhook statuses to `InTransit`, while Aramex and
DHL fail closed. This violated the platform-wide Fail Closed rule. The runtime
was hardened so Bosta rejects unknown create/tracking statuses and returns no
webhook parse result for unknown webhook events. Focused regression coverage was
added.

Final architecture verdicts:

| Certification Area | Result |
|---|---|
| Platform Architecture | CERTIFIED |
| Provider Consistency | CERTIFIED |
| Development Platform | CERTIFIED |
| Operational Rollout | Outside Scope |

No new permanent cross-provider architecture truth was discovered. Existing
truth already required fail-closed provider status admission, so the `.ai-context`
files were not updated by this freeze.

## Platform Architecture

The platform owns shipment identity, lifecycle, authority, SQL truth, snapshots,
carrier request identity, recovery, and audit. Providers are registered
transport adapters behind `IShippingCarrierAdapter`.

Every external carrier registration must pass through `CarrierShipmentService`,
which resolves tenant carrier configuration, shipment identity, origin snapshot,
destination snapshot, published carrier mapping, customs snapshot when required,
carrier request idempotency, carrier result admission, lifecycle transition
policy, and SQL persistence.

Adapters may serialize admitted DTO evidence and parse official provider
responses only. They do not query SQL, rebuild business state, resolve
geography, admit customs, own shipment lifecycle, or mutate persistence.

## Provider Comparison Matrix

| Rule | Bosta | Aramex | DHL |
|---|---|---|---|
| Registered adapter | Yes | Yes | Yes |
| Uses shared `IShippingCarrierAdapter` | Yes | Yes | Yes |
| Invoked through `CarrierShipmentService` | Yes | Yes | Yes |
| Consumes origin snapshot | Yes | Yes | Yes |
| Consumes destination snapshot | Yes | Yes | Yes |
| Consumes carrier mapping snapshot | Yes | Yes | Yes |
| Consumes customs snapshot when applicable | Not applicable for current domestic Bosta path | Yes where applicable | Yes for cross-border |
| Owns only wire/auth/transport/parsing | Yes | Yes | Yes |
| Unknown statuses fail closed | Yes, after this freeze | Yes | Yes |
| Recovery uses persisted snapshots | Yes | Yes | Yes |

## Authority Matrix

| Concern | Platform Authority | Provider Authority |
|---|---|---|
| Shipment identity | SQL `Shipment` and backend services | None |
| Order | SQL `Order` and order services | None |
| Checkout | Backend checkout/session authority | None |
| Inventory / Warehouse lifecycle | Owning backend domains | None |
| Product | Product/customs source evidence only | None |
| Customer | Backend customer/order ownership | None |
| Geography | Geographic SQL/catalog/mapping releases and snapshots | None |
| Origin | Warehouse-owned admitted origin, snapshotted by Shipment | None |
| Destination | Order-derived admitted destination, snapshotted by Shipment | None |
| Carrier Mapping | Published mapping release and Shipment mapping snapshot | None |
| Customs | Customs Evidence and Shipment Customs Snapshot | None |
| Shipment Lifecycle | `ShipmentLifecycleTransitionPolicy` and SQL | Status evidence only |
| Financial State | Backend financial/payment/accounting authority | None |
| SQL Truth | PostgreSQL | None |

## Snapshot Matrix

| Snapshot | Platform Rule | Bosta | Aramex | DHL |
|---|---|---|---|---|
| Origin Snapshot | Required before carrier I/O | Consumed | Consumed | Consumed |
| Destination Snapshot | Required before carrier I/O | Consumed | Consumed | Consumed |
| Carrier Mapping Snapshot | Resolved from published release and persisted on Shipment | Consumed for provider geography | Consumed as resolved evidence | Consumed as resolved evidence |
| Customs Snapshot | Required for cross-border carrier registration | Not required in current domestic path | Consumed when required | Required for cross-border |
| Recovery Snapshot | Reused; never rebuilt from current domains | Reused | Reused | Reused |

## Recovery Matrix

| Recovery Input | Platform Result |
|---|---|
| Persisted shipment identity | Required |
| Persisted origin snapshot | Required and reused |
| Persisted destination snapshot | Required and reused |
| Persisted carrier mapping snapshot | Required and reused; no remapping to newer release |
| Persisted customs snapshot | Required for cross-border recovery |
| Persisted carrier request identity | Required for idempotent reconciliation |
| Product / Checkout / Warehouse / Geography current state | Forbidden as recovery authority |
| Redis / Mongo / cache / browser state | Forbidden as recovery authority |

## Fail Closed Matrix

| Condition | Platform Behavior |
|---|---|
| Unknown provider status | Reject as unadmitted evidence |
| Unknown provider enum | Reject before persistence |
| Unknown geography or mapping | Reject before provider I/O |
| Unknown customs or incomplete customs snapshot | Reject before provider I/O |
| Unknown provider id | Reject mapping/admission |
| Invalid shipment identity | Reject |
| Malformed provider response | Reject |
| Unexpected lifecycle transition | Reject through `ShipmentLifecycleTransitionPolicy` |

## Carrier Neutral Matrix

| Evidence | Carrier-neutral Boundary |
|---|---|
| Origin | `ShipmentOriginDto` |
| Destination | Shipment destination snapshot fields and resolved provider geography DTOs |
| Geography mapping | `ResolvedProviderGeography` values from Shipment mapping evidence |
| Customs | `CarrierNeutralCustomsDto` |
| Carrier request identity | `CarrierRequestId` |
| Carrier response | `CreateShipmentResult`, `ShipmentTrackingResult`, `WebhookParseResult` as evidence only |

## Architectural Consistency Review

The unified architecture is consistent after the Bosta fail-closed hardening.
Bosta, Aramex, and DHL now all follow the same principles:

- Evidence before Truth
- Admission before Persistence
- Snapshot before Carrier
- Carrier Neutral DTO
- Adapter Purity
- Fail Closed
- Append-only Audit
- Immutable Snapshots
- No Guessing
- No Provider Authority

Provider-specific operational limitations remain outside this certification:
credentialed sandbox replay, production account entitlement, provider mapping
coverage, admitted warehouse-origin coverage, customs evidence coverage, live
carrier status samples, telemetry, alerting, and rollout proof.

## Cross-Provider Findings

| Finding | Classification | Status |
|---|---|---|
| Bosta unknown create/tracking/webhook statuses defaulted to `InTransit` while Aramex and DHL fail closed. | Platform Architecture | Fixed in `BostaAdapter`; tests added |
| Aramex final provider freeze previously withheld due an aggregate regression gate, not an Aramex architecture defect. | Provider Specific | Not a platform architecture blocker for this freeze |
| GEO-10 repository-proven external-provider mapping coverage is 0%. | Operational | Outside platform architecture certification; blocks production rollout |
| Live credentials, provider account entitlement, mapping publication, migration deployment, and smoke tests remain unproven. | Operational | Outside scope |
| CommerceHub finance test selected by broad `Shipping` name filters. | Out Of Scope | Not Shipping Platform architecture |

## Regression Classification

Focused certification commands:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~BostaEndpointSurfaceTests -nologo -v minimal
```

Result: PASS, 9 passed, 0 failed.

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~BostaEndpointSurfaceTests|FullyQualifiedName~AramexOfficialContractCompatibilityTests|FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~CarrierGeographicResolutionIntegrationTests|FullyQualifiedName~ShipmentOriginAuthorityTests|FullyQualifiedName~Customs" -nologo -v minimal
```

Result: PASS, 74 passed, 0 failed.

Existing compiler/analyzer warnings remain unrelated to this platform
certification.

## Final Platform Certification

### Platform Architecture

CERTIFIED

The implemented platform foundations are shared and authoritative. Providers
consume admitted snapshots and carrier-neutral DTO evidence through the shared
carrier service boundary.

### Provider Consistency

CERTIFIED

Bosta, Aramex, and DHL now follow identical architectural rules for authority,
snapshot consumption, recovery, adapter purity, and fail-closed provider
evidence admission.

### Development Platform

CERTIFIED

The development architecture is certified after the Bosta fail-closed
correction and focused cross-provider regression pass.

### Operational Rollout

Outside Scope

Operational rollout remains blocked until provider credentials, published
mapping coverage, warehouse-origin evidence, customs evidence, deployment
migrations, live smoke tests, monitoring, and rollback procedures are proven.

## Explicit Success Answers

- Reviewed ONLY Shipping Platform? Yes.
- Providers audited? No independent provider business-contract re-audit was performed; Bosta, Aramex, and DHL were compared only as platform consumers.
- Cross-provider inconsistencies? Yes, one: Bosta unknown status fallback. Fixed.
- Architecture Certified? Yes, CERTIFIED.
- Provider Consistency Certified? Yes, CERTIFIED.
- Development Platform Certified? Yes, CERTIFIED.
- Operational excluded? Yes, Operational Rollout is Outside Scope.
- Recommend next phase: Operational provider data and rollout certification: publish provider mappings, prove warehouse-origin/customs coverage, run credentialed sandbox/live smoke tests, and rerun GEO-10/operational readiness gates.

