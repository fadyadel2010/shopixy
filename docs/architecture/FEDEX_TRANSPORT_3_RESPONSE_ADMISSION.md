# FEDEX-TRANSPORT-3 - Response Admission Runtime

Date: 2026-07-08
Scope: FedEx provider response admission only
Status: IMPLEMENTED

## Executive Summary

FEDEX-TRANSPORT-3 implements only FedEx response admission.

Implemented:

- strict JSON parsing for FedEx response envelopes;
- response schema allowlists for shipment, tracking, label, and address-validation evidence;
- fail-closed required-field checks;
- fail-closed null and unexpected-field rejection;
- official enum checks for admitted label content and alerts;
- certified FedEx tracking status mapping into carrier-neutral shipment statuses;
- carrier-neutral DTO creation for admitted shipment evidence, label evidence, address-validation provider evidence, and tracking presentation results.

Not implemented:

- shipment creation decisions;
- adapter activation;
- order, inventory, warehouse, checkout, geography, customs, carrier mapping, SQL, frontend, retry, OAuth, or HTTP transport changes;
- shipment lifecycle mutation;
- tracking lifecycle truth;
- pickup or service-availability runtime behavior.

Official FedEx references reviewed:

- Ship API: `https://developer.fedex.com/api/en-us/catalog/ship/docs.html`
- Track API / Basic Integrated Visibility: `https://developer.fedex.com/api/en-us/catalog/track/docs.html`
- Address Validation API: `https://developer.fedex.com/api/en-us/catalog/address-validation/docs.html`
- Pickup Request API: `https://developer.fedex.com/api/en-us/catalog/pickup/docs.html`
- Service Availability API: `https://developer.fedex.com/api/en-us/catalog/service-availability/docs.html`

## Admission Flow

Transport remains frozen as raw HTTP evidence. Response admission begins only after a transport-success response body is available.

Flow:

1. Parse JSON.
2. Reject malformed JSON or non-object roots.
3. Validate only the expected official envelope fields.
4. Validate operation-specific output shape.
5. Validate required identifiers, labels, timestamps, locations where present, and enum/status values.
6. Map only certified provider evidence into carrier-neutral DTOs.
7. Return admitted DTOs or a fail-closed admission error.

Admission never calls FedEx, never retries, never obtains OAuth tokens, never writes SQL, and never mutates shipments.

## Shipment Admission

Runtime:

- `PointOfSale.Infrastructure/Shipping/Adapters/FedExResponseAdmission.cs`

Admitted shipment evidence:

- `transactionId`;
- `masterTrackingNumber`;
- piece `trackingNumber`;
- label evidence from `packageDocuments`;
- warning evidence from official `alerts`;
- provider references needed for reconciliation.

Shipment admission rejects:

- malformed JSON;
- missing `transactionId`;
- missing `output.transactionShipments`;
- zero or multiple transaction shipments;
- missing `masterTrackingNumber`;
- missing `pieceResponses`;
- missing piece tracking number;
- missing or malformed `packageDocuments`;
- unexpected fields;
- unknown alert enum values.

Shipment admission does not create or mutate a platform `Shipment`, does not select an initial lifecycle state, and does not expose provider JSON outside admission.

## Tracking Admission

Tracking admission accepts only official tracking result evidence:

- `completeTrackResults`;
- matching complete-result and track-result tracking identifiers;
- `latestStatusDetail.derivedCode`;
- `scanEvents`;
- scan event timestamps;
- scan event location evidence where supplied.

Certified FedEx derived/event status mapping:

| FedEx code | Carrier-neutral status |
|---|---|
| `OC` | `ReadyForPickup` |
| `PU` | `PickedUp` |
| `IT` | `InTransit` |
| `OD` | `OutForDelivery` |
| `DL` | `Delivered` |
| `DE` | `Failed` |
| `SE` | `Failed` |
| `CA` | `Cancelled` |
| `RS` | `Returned` |

Unknown tracking status or event codes fail closed.

Tracking admission produces `ShipmentTrackingResult` and `TrackingEvent` DTOs only. It does not update shipment lifecycle, order state, inventory, fulfillment, payment, projections, or SQL.

## Label Admission

Label admission accepts only official FedEx label evidence from package documents.

Accepted:

- `contentType = LABEL`;
- valid base64 `encodedLabel`, or absolute HTTP(S) `url`;
- canonical media evidence: `application/pdf`;
- canonical encoding evidence: `base64` or `url`.

Rejected:

- unknown `contentType`;
- missing both `encodedLabel` and `url`;
- malformed base64;
- non-HTTP(S) label URLs;
- null label fields;
- unexpected label structure.

Admission never invents a label URL, label payload, media type, encoding, or copies count.

## Validation Matrix

| Response concern | Admission behavior |
|---|---|
| Malformed JSON | Reject |
| Empty body | Reject |
| Non-object root | Reject |
| Missing `transactionId` | Reject |
| Missing operation output | Reject |
| Unexpected response field | Reject |
| Unexpected null | Reject |
| Unknown alert enum | Reject |
| Unknown label content type | Reject |
| Malformed label payload | Reject |
| Missing tracking identifiers | Reject |
| Tracking identifier disagreement | Reject |
| Missing tracking timestamp | Reject |
| Unknown tracking status | Reject |
| Raw provider JSON exposure | Forbidden |
| SQL mutation | Forbidden |
| Shipment lifecycle mutation | Forbidden |

## Authority Matrix

| Concern | Response admission owns | Response admission does not own |
|---|---|---|
| JSON parsing | Yes | HTTP transport |
| Response schema allowlist | Yes | OAuth |
| Provider required fields | Yes | Retry |
| Provider enum validation | Yes | Shipment creation decision |
| Label payload validation | Yes | Shipment lifecycle |
| Carrier-neutral DTO creation | Yes | Tracking truth persistence |
| Address validation response evidence | Provider evidence only | Geography authority |
| Tracking status mapping | Certified DTO mapping only | Order, inventory, warehouse, checkout |
| Raw provider body | Input only | Output or persistence |

## Failure Handling

Admission fails closed and returns error codes:

- `FEDEX_RESPONSE_MALFORMED_JSON`
- `FEDEX_RESPONSE_INVALID_SCHEMA`
- `FEDEX_RESPONSE_UNKNOWN_ENUM`
- `FEDEX_RESPONSE_UNKNOWN_STATUS`
- `FEDEX_RESPONSE_INVALID_LABEL`

No fallback value is invented for shipment identifiers, tracking numbers, label URLs, label data, warning text, timestamps, locations, statuses, pickup confirmations, estimated delivery dates, or reference numbers.

## Regression Coverage

Added:

- `PointOfSale.Tests/Infrastructure/FedExResponseAdmissionTests.cs`

Coverage freezes:

- malformed JSON rejection;
- missing-field rejection;
- unexpected response field rejection;
- unknown enum rejection;
- unknown tracking status rejection;
- tracking identifier mismatch rejection;
- missing tracking timestamp rejection;
- carrier-neutral shipment DTO creation;
- carrier-neutral tracking DTO creation;
- label evidence admission;
- malformed label rejection;
- address validation as provider evidence only;
- no adapter/transport wiring;
- no SQL, order, inventory, or shipment mutation inside admission.

## Verification

Command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExResponseAdmissionTests -nologo -v minimal
```

Result:

- PASS
- 11 passed
- 0 failed

Existing unrelated project warnings remain.

## Final Recommendation

Approve FEDEX-TRANSPORT-3 as the FedEx Response Admission Runtime.

Production FedEx activation remains blocked until later credentialed sandbox proof, provider response samples, operational readiness, mapping/origin/destination/customs coverage, monitoring, audit, rollback, and smoke-test gates are certified.

Recommended next phase: credentialed FedEx sandbox response-sample certification and operational activation gate review, without modifying Shipment Runtime until the explicit activation phase.

## Explicit Success Answers

- Reviewed ONLY Response Admission? **Yes.**
- Runtime modified? **Yes, response admission runtime only.**
- Backend modified? **Yes, backend infrastructure admission boundary and focused tests only.**
- Frontend modified? **No.**
- Shipment admission implemented? **Yes, evidence-only shipment response admission.**
- Tracking admission implemented? **Yes, response DTO admission and certified status mapping only.**
- Label admission implemented? **Yes.**
- Carrier-neutral DTOs produced? **Yes.**
- Business logic inside admission? **No.**
- Shipment Runtime modified? **No.**
- OAuth modified? **No.**
- Transport modified? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable? **Yes for response admission runtime; FedEx production activation remains gated.**
- Recommend next phase: **Credentialed FedEx sandbox response-sample certification and operational activation gate review.**

## AI Context Update Decision

No `.ai-context` files were updated. This phase implemented provider-specific FedEx response admission and did not discover new permanent cross-provider architectural truth.
