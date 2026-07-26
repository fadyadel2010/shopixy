# FEDEX-TRANSPORT-2 - HTTP Transport Runtime

Date: 2026-07-08
Scope: FedEx authenticated HTTP transport execution only
Status: IMPLEMENTED

## Executive Summary

FEDEX-TRANSPORT-2 implements only authenticated FedEx HTTP request execution.

Implemented:

- named FedEx `HttpClient` usage;
- OAuth bearer header injection through FEDEX-TRANSPORT-1;
- JSON request serialization for certified FedEx DTOs only;
- `Content-Type: application/json` and `Accept: application/json`;
- correlation ID propagation through `x-customer-transaction-id`;
- raw HTTP status, headers, body, URI, duration, retry count, and correlation metadata return;
- one explicit 401 OAuth refresh retry;
- transient-only retry for timeout, `408`, `429`, and `5xx`;
- transport failure normalization for timeout, network/TLS/DNS/connectivity failures, and HTTP status failures;
- secret-safe transport logging.

Not implemented:

- shipment creation decisions;
- tracking admission;
- label admission;
- pickup admission;
- business rules;
- carrier mapping;
- geography;
- customs;
- shipment lifecycle;
- response interpretation;
- frontend.

## Transport Flow

The transport caller supplies a certified FedEx request DTO, endpoint path,
HTTP method, OAuth request evidence, environment, and optional correlation ID.
The transport runtime serializes the DTO losslessly, requests a bearer token
from the OAuth runtime, sends the HTTP request through the named FedEx
`HttpClient`, and returns the raw provider response envelope.

The transport layer does not decide whether a shipment should be created, a
tracking response should be admitted, a label should be trusted, or a pickup
should be accepted.

Official FedEx references reviewed:

- Authorization: https://developer.fedex.com/api/en-us/catalog/authorization/docs.html
- Ship: https://developer.fedex.com/api/en-us/catalog/ship/docs.html
- Track: https://developer.fedex.com/api/en-us/catalog/track/docs.html
- Address Validation: https://developer.fedex.com/api/en-us/catalog/address-validation/docs.html
- Pickup: https://developer.fedex.com/api/en-us/catalog/pickup/docs.html
- Rate Limits: https://developer.fedex.com/api/en-us/guides/ratelimits.html
- Best Practices: https://developer.fedex.com/api/en-us/guides/best-practices.html

## HTTP Pipeline

Runtime class:

- `PointOfSale.Infrastructure/Shipping/Adapters/FedExTransport.cs`

DI registration:

- `IFedExTransportClient -> FedExTransportClient`
- named `HttpClient`: `FedEx`

The transport runtime never creates `new HttpClient()`. It calls
`IHttpClientFactory.CreateClient("FedEx")` through `FedExOptions.TransportHttpClientName`.

## Request Pipeline

The request pipeline:

1. Rejects non-certified FedEx DTOs before HTTP execution.
2. Serializes the DTO with `FedExContractSerializer.SerializeJson`.
3. Creates an `HttpRequestMessage` for the supplied official endpoint path.
4. Injects `Authorization: Bearer <token>` from `IFedExOAuthClient`.
5. Adds `Accept: application/json`.
6. Adds `Content-Type: application/json`.
7. Adds `x-customer-transaction-id` with supplied or generated correlation ID.
8. Sends with the caller cancellation token.

Certified DTOs currently accepted:

- `FedExShipmentRequest`
- `FedExTrackingNumbersRequest`
- `FedExPickupAvailabilityRequest`
- `FedExCreatePickupRequest`
- `FedExAddressValidationRequest`
- `FedExServiceAvailabilityRequest`

## Response Pipeline

The response pipeline returns transport evidence only:

- HTTP status;
- response headers and content headers;
- raw JSON/body string;
- correlation ID;
- request URI;
- duration;
- retry count.

Non-success statuses are normalized into transport errors while preserving the
raw response envelope. No provider status, shipment ID, tracking number, label,
pickup confirmation, warning, alert, or error payload is interpreted or admitted.

## Retry Policy

Retry occurs only for transient transport failures:

- timeout;
- HTTP `408`;
- HTTP `429`;
- HTTP `5xx`;
- `HttpRequestException` transport failures such as DNS, TLS, or connection
  refusal.

No retry occurs for:

- `400`;
- `403`;
- `404`;
- `409`;
- validation failures;
- unsupported request DTOs.

HTTP `401` is handled only through the OAuth refresh path: the cached token is
refreshed once and the same request is retried once. Continued `401` failure is
returned as transport failure evidence.

## Timeout Strategy

Timeout is owned by the named FedEx `HttpClient` configuration in `Program.cs`,
currently aligned with the existing external carrier timeout of 15 seconds.
The transport layer honors the caller cancellation token and does not convert
caller cancellation into timeout. Non-caller `TaskCanceledException` is
normalized as `FEDEX_TRANSPORT_TIMEOUT`.

## Logging Policy

Transport logs include only:

- correlation ID;
- URI;
- duration;
- retry count;
- HTTP status.

Transport logs never include:

- bearer token;
- Authorization header;
- client secret;
- child secret;
- customer payload;
- raw provider body.

## Authority Matrix

| Concern | Transport owns | Transport does not own |
|---|---|---|
| HTTP request creation | Yes | Shipment decisions |
| Named `HttpClient` | Yes | Tracking admission |
| Bearer injection | Yes | Label admission |
| JSON serialization | Certified DTOs only | Business value mutation |
| Content negotiation | `Content-Type` and `Accept` | Carrier mapping |
| Correlation propagation | Yes | Geography |
| Timeout/cancellation | Transport only | Customs |
| Retry | Transient transport only | Shipment lifecycle |
| HTTP status capture | Yes | Response interpretation |
| Raw response delivery | Yes | SQL persistence |

## Regression Coverage

Added:

- `PointOfSale.Tests/Infrastructure/FedExTransportRuntimeTests.cs`

Coverage freezes:

- named FedEx `HttpClient`;
- bearer injection;
- JSON serialization without invented fields;
- correlation ID propagation;
- raw response body/header/status return;
- transient retry;
- no retry for `400`;
- explicit 401 OAuth refresh path;
- timeout normalization;
- caller cancellation preservation;
- unsupported DTO fail-closed behavior;
- no response admission or shipment logic in transport.

## Verification

Planned command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExTransportRuntimeTests -nologo -v minimal
```

## Final Recommendation

Approve FEDEX-TRANSPORT-2 as the FedEx authenticated HTTP transport runtime.

Recommended next phase: FedEx response admission certification as a separate
phase. Do not activate shipment creation, tracking, label, pickup, or lifecycle
behavior until response admission, credentialed sandbox proof, operational
readiness, mapping, origin/destination, customs, monitoring, rollback, and smoke
test gates are separately certified.

## Explicit Success Answers

- Reviewed ONLY HTTP Transport? **Yes.**
- Runtime modified? **Yes, FedEx HTTP transport runtime only.**
- Backend modified? **Yes, backend infrastructure and DI only.**
- Frontend modified? **No.**
- Named HttpClient used? **Yes, `FedEx`.**
- OAuth integrated? **Yes, through `IFedExOAuthClient`.**
- Bearer injected? **Yes.**
- Transport execution implemented? **Yes.**
- Shipment logic inside transport? **No.**
- Response admission implemented? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable? **Transport runtime is certifiable; production FedEx activation remains blocked by later response-admission and operational gates.**
- Recommend next phase: **FedEx response admission certification.**

## AI Context Update Decision

No `.ai-context` files were updated. This phase implemented provider-specific
FedEx HTTP transport execution and did not discover new permanent
cross-provider architectural truth.
