# DHL-1 — Official Contract and Runtime Compatibility

Date: 2026-07-06  
Audit scope: implemented DHL runtime only  
Official authority: DHL Express MyDHL REST API v3.3.1, released 2026-06-28

## Executive Summary

The DHL adapter was not production-compatible as implemented. It used the correct MyDHL REST authentication and base URLs, and its rate, shipment-create, tracking, label-response, timeout, carrier-resolution, origin-snapshot, destination-snapshot, mapping-snapshot, recovery, and SQL-authority boundaries were structurally aligned. However, the audit found real contract drift:

- `DELETE /shipments/{trackingNumber}` is not a published MyDHL REST v3.3.1 endpoint. The official REST cancellation operation cancels a pickup through `DELETE /pickups/{dispatchConfirmationNumber}`.
- The adapter advertised pickup scheduling and webhooks without implementing an official MyDHL REST pickup or webhook contract.
- International create requests set `isCustomsDeclarable=true` but supplied no item-level `exportDeclaration`; the carrier-neutral request does not currently provide enough authoritative customs data to construct one safely.
- DHL tracking code `OK` was mapped to both `PickedUp` and `Delivered`; switch precedence made it `PickedUp`.
- Unknown DHL statuses silently became `InTransit`, allowing unverified carrier data to advance shipment lifecycle.
- Rate failures returned invented successful USD quotes.
- Tracking responses were not bound back to the requested tracking identity.
- Optional notification, dimensions, declared value, credential, and error-detail handling were not fail-closed.

Minimum hardening was implemented. Domestic/non-customs shipment creation, authentication, tracking admission, embedded PDF label processing, and authority/recovery boundaries are now contract-safe under deterministic tests. International shipment creation is intentionally blocked with `DHL_CUSTOMS_DATA_REQUIRED` until authoritative customs line-item data exists. Therefore the adapter is safer, but DHL as a global production provider is **not yet production-compatible**.

## Official Contract Summary

Official sources:

- [MyDHL API v3.3.1](https://developer.dhl.com/api-reference/dhl-express-mydhl-api?lang=en&language_content_entity=en)
- [MyDHL API Reference Data Guide v3.3.1](https://developer.dhl.com/sites/default/files/2026-06/MyDHL%20API%20Reference%20data%20guide%20%283.3.1%29.pdf)

Contract:

| Area | Official MyDHL REST v3.3.1 |
|---|---|
| Protocol | HTTPS REST, JSON, OAS3 |
| Authentication | Pre-emptive HTTP Basic authentication |
| Test base URL | `https://express.api.dhl.com/mydhlapi/test` |
| Production base URL | `https://express.api.dhl.com/mydhlapi` |
| Rates | `GET /rates`, `POST /rates` |
| Shipment create | `POST /shipments` |
| Tracking | `GET /tracking` or `GET /shipments/{shipmentTrackingNumber}/tracking` |
| Labels | Returned by shipment create; shipment image retrieval is also published |
| Pickup | `POST /pickups`, `PATCH /pickups/{dispatchConfirmationNumber}`, `DELETE /pickups/{dispatchConfirmationNumber}` |
| Shipment cancellation | No shipment-delete operation is published in the REST v3.3.1 catalog |
| Error model | HTTP status plus structured JSON error/result details |
| Current release | v3.3.1, 2026-06-28 |

The separately published MyDHL SOAP/XML products are different contracts and do not authorize mixing SOAP shipment-delete behavior into this REST adapter.

## Current Runtime Summary

The runtime is a singleton `DhlAdapter` using the named `DHL` `HttpClient` with a 15-second timeout. GET requests receive the shared bounded transient retry policy; POST shipment create is not automatically retried because it has no provider-recognized idempotency header.

`CarrierShipmentService` remains authoritative for carrier resolution and persistence:

- resolves the registered adapter through `ShippingCarrierRegistry`;
- derives origin from admitted warehouse evidence and stores an immutable origin snapshot;
- consumes the immutable destination snapshot already persisted on `Shipment`;
- resolves a published carrier geographic mapping and stores its immutable mapping snapshot;
- persists `CarrierRequestId` before the external call;
- performs external calls outside SQL transactions;
- admits carrier identity, tracking, label, metadata, and initial status before SQL persistence;
- quarantines ambiguous external success as reconciliation-required evidence;
- re-enters current SQL shipment/order authority during recovery.

The DHL adapter neither queries Geographic tables nor writes shipment/SQL state.

## Compatibility Matrix

| Area | Before | After hardening | Verdict |
|---|---|---|---|
| Basic authentication | Correct | Credential completeness checked before calls | Compatible |
| Base URLs | Correct | Unchanged | Compatible |
| `POST /rates` | Correct route | Provider errors no longer become invented quotes | Compatible |
| `POST /shipments` domestic/non-customs | Mostly aligned | Request validation, real optional dimensions/value, safe notification omission | Conditionally compatible |
| International shipment create | Missing `exportDeclaration` | Fails before network with `DHL_CUSTOMS_DATA_REQUIRED` | Safe but not implemented |
| Tracking route | Published route | Unchanged | Compatible |
| Tracking identity | Unchecked | Response tracking number must match request | Compatible |
| Tracking status | Duplicate `OK`; unknown became `InTransit` | `OK` maps to `Delivered`; unknown fails closed | Compatible for admitted mappings |
| Labels | Base64 PDF document consumed | Unchanged; protected by downstream label admission | Compatible |
| Shipment cancellation | Guessed unpublished route | Fails before network as unsupported | Contract-safe |
| Pickup | Advertised, not implemented | Capability disabled | Honest capability surface |
| Webhooks | Advertised with an unverified HMAC shape | Capability disabled | No official runtime certification |
| Timeout | 15 seconds | Unchanged | Acceptable bounded timeout |
| Retries | GET only without idempotency key | Unchanged | Safe |
| Recovery/idempotency | Durable local key; DHL lookup unsupported | Unchanged fail-closed reconciliation | Safe, operational limitation remains |
| Carrier resolution/snapshots | Present | Unchanged | Compatible |
| SQL authority | Service-owned | Unchanged | Compatible |

## Breaking Changes

Detected:

1. Unpublished shipment cancellation route.
2. Missing international customs declaration contract.
3. Incorrect `OK` status precedence.
4. Unsafe unknown-status default.
5. Fabricated rate fallback.
6. Advertised but unimplemented pickup/webhook capabilities.
7. Missing tracking response identity check.

No deprecated authentication or base URL was found. The v3.3.1 release additions do not break the inspected domestic request; they primarily extend reference data and international customs fields.

## Runtime Hardening

Modified:

- `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/DhlCarrierDefinition.cs`
- `PointOfSale.Tests/Infrastructure/DhlOfficialContractCompatibilityTests.cs`

Changes:

- validate MyDHL key, secret, and account number;
- stop fabricated quote success;
- reject invalid package/weight/country request shapes;
- fail international create before network until authoritative customs data is available;
- use supplied dimensions and declared value rather than an invented declared value;
- omit email notification when no email exists;
- reject malformed returned tracking identity;
- fail shipment cancellation before network;
- bind tracking response identity to the requested shipment;
- fail unknown tracking states closed;
- map `OK` to `Delivered`;
- parse official additional error details;
- disable unsupported cancellation, pickup, and webhook capability claims.

No Shipping, Geographic, Checkout, frontend, controller, registration, recovery, audit, or SQL model redesign was performed.

## Regression Tests

Added eight focused DHL tests covering:

- official shipment-create endpoint, Basic authentication, request shape, dimensions, optional notification/value omission, label, and initial status;
- international customs fail-closed behavior;
- unpublished cancellation fail-closed behavior;
- capability truth;
- no invented rate fallback;
- delivered status mapping;
- unknown status rejection;
- tracking identity mismatch rejection.

## Verification

- `dotnet build PointOfSale.sln --no-restore`: **PASS**, 0 warnings, 0 errors.
- Focused DHL tests: **PASS**, 8/8.
- Relevant Shipping/Geographic selection: **73 passed, 2 failed**.

The two failures are in the already-modified `ShipmentExternalRegistrationSafetyTests` and are unrelated to DHL adapter behavior:

- `ExecuteCarrierForExistingShipmentAsync_RetryAfterSuccess_DoesNotCreateDuplicateCarrierShipment`
- `ExecuteCarrierForExistingShipmentAsync_WhenExternalSuccessWasNotPersisted_ReconcilesByCarrierRequestIdWithoutDuplicateCreate`

Both fixtures use Mylerz/deterministic adapters and fail current mandatory shipment-origin admission before the asserted registration outcome. The audit did not alter that user-owned test file or weaken origin authority.

## Risks

1. **Blocker — international DHL create:** canonical carrier requests lack authoritative customs line items, commodity/HS evidence, quantity, manufacture origin, and export declaration evidence. Guessing these values would violate both DHL schema/business validation and platform authority.
2. **High — recovery after ambiguous create:** MyDHL REST does not provide a proven adapter implementation for lookup by the local `CarrierRequestId`; timeout-after-success therefore remains reconciliation-required rather than safely retryable.
3. **High — tracking schema/live proof:** deterministic contract tests freeze the implemented response model, but production credentials and DHL test-environment evidence are still required for a live certification packet.
4. **Medium — no official webhook integration:** tracking is polling-based for this certified runtime.
5. **Medium — pickup not implemented:** the official API supports pickup booking, update, and cancellation, but this runtime does not.
6. **Operational:** two unrelated Shipping regression fixtures currently fail mandatory origin admission and should be repaired separately without weakening the authority invariant.

## Final Recommendation

Do not certify the DHL provider as globally production-compatible yet.

Certify the hardened adapter only for authenticated, account-enabled, domestic/non-customs MyDHL shipment creation with polling tracking and embedded PDF labels. Keep international creation, shipment cancellation, pickup, and webhook capability disabled.

Recommend next phase: **DHL-2 — International Request Contract and Customs Authority**, limited to adding authoritative item-level customs evidence to the carrier-neutral request and implementing the exact MyDHL `exportDeclaration` schema, followed by test-environment replay evidence. A later operational phase may add official pickup APIs and reference-based reconciliation if DHL account capabilities prove them.

## Explicit Success Answers

- Reviewed ONLY DHL runtime? **Yes.** Non-DHL reports were read only as mandatory implementation guidance; non-DHL runtime was not audited.
- Runtime files modified: **2** — `DhlAdapter.cs`, `DhlCarrierDefinition.cs`.
- Backend files modified: **2 runtime files plus 1 backend test file**.
- Frontend files modified: **None**.
- Official contract verified? **Yes**, against official MyDHL REST v3.3.1 and its v3.3.1 reference guide.
- Runtime compatible? **Partially** — compatible for the hardened domestic/non-customs subset; international create is intentionally blocked.
- Breaking changes found? **Yes**, seven concrete compatibility issues listed above.
- Runtime hardening required? **Yes, completed** for safe fail-closed operation.
- Production compatible? **No, not as a global DHL Express integration**. Conditionally compatible for the certified subset.
- Recommend next phase: **DHL-2 — International Request Contract and Customs Authority**.

## AI Context Update Decision

The five architecture-truth `.ai-context` files were not changed. This audit discovered provider-specific contract truth and capability limits, not a new cross-system architecture truth. Existing Carrier Resolution, immutable Origin/Destination/Mapping Snapshot, Shipment Authority, recovery, and SQL Truth rules remain correct.
