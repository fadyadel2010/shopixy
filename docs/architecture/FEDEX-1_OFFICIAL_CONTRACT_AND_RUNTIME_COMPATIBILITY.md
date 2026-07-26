# FEDEX-1 - Official Contract and Runtime Compatibility

Date: 2026-07-07
Scope: FedEx official public API contract and current FedEx runtime only
Status: COMPLETE

## Executive Summary

FEDEX-1 reviewed only FedEx official Developer Portal documentation and the
current PointOfSale carrier runtime surface.

The certified Shipping Platform architecture remains frozen. No Geography,
Customs, Shipment Origin, Carrier Mapping, Checkout, Warehouse, POS Admin,
Runtime Authority, backend platform, or frontend redesign was performed.

Final verdict: **OFFICIAL CONTRACT VERIFIED; RUNTIME FAILS CLOSED BECAUSE NO
FEDEX ADAPTER IS IMPLEMENTED OR REGISTERED**.

No FedEx adapter is currently registered in PointOfSale. The active carrier
runtime contains `manual`, `bosta`, `aramex`, `mylerz`, and `dhl`; `fedex` is
not present in `CarrierCodes`, `Program.cs` carrier DI registration, or
`PointOfSale.Infrastructure/Shipping/Adapters`.

Because there is no current FedEx adapter, there is no FedEx runtime serializer,
deserializer, OAuth client, shipment creator, tracker, validator, canceller,
label parser, pickup implementation, service-availability implementation,
address-validation implementation, or webhook handler to modify. The correct
minimal runtime hardening is regression coverage that prevents accidental
partial FedEx activation or invented FedEx endpoints before a certified adapter
phase.

## Official Contract Summary

Official sources reviewed:

- FedEx Developer Portal: https://developer.fedex.com/api/en-us/home.html
- API Authorization: https://developer.fedex.com/api/en-us/catalog/authorization/v1/docs.html
- Ship API Documentation: https://developer.fedex.com/api/en-us/catalog/ship/v1/docs.html
- Basic Integrated Visibility Documentation: https://developer.fedex.com/api/en-us/catalog/track/v1/docs.html
- Pickup Request API Documentation: https://developer.fedex.com/api/en-us/catalog/pickup/v1/docs.html
- Service Availability API Documentation: https://developer.fedex.com/api/en-us/catalog/service-availability/v1/docs.html
- Address Validation API Documentation: https://developer.fedex.com/api/en-us/catalog/address-validation/v1/docs.html
- Advanced Integrated Visibility Webhook: https://developer.fedex.com/api/en-us/catalog/shipment-visibility-webhook.html
- Quotas and Rate Limits Guide: https://developer.fedex.com/api/en-us/guides/ratelimits.html
- Integration Best Practices: https://developer.fedex.com/api/en-us/guides/best-practices.html
- Sandbox Virtualization Guide: https://developer.fedex.com/api/en-us/guides/sandboxvirtualization.html

Official base URLs:

| Environment | Base URL |
|---|---|
| Sandbox/Test | `https://apis-sandbox.fedex.com/` |
| Production | `https://apis.fedex.com/` |

FedEx REST APIs use HTTPS, OAuth 2.0 bearer tokens, JSON POST request bodies
where applicable, and official HTTP status/error payload handling. FedEx
documents that test and production URIs are separate and production code must
use production URIs only for production traffic.

## Authentication

FedEx APIs require OAuth 2.0 bearer-token authentication.

Required credential inputs:

- `client_id` / API Key
- `client_secret` / Secret Key
- `grant_type`

Official grant types:

- `client_credentials`
- `csp_credentials`
- `client_pc_credentials`

Additional child credential modes exist for FedEx Internal, Compatible,
Integrator, and Proprietary Parent Child customers. Those modes require child
credential evidence and must not be guessed or inferred.

Required runtime rule: a future FedEx adapter must generate a token through
`POST /oauth/token`, store the token server-side only, send
`Authorization: Bearer {access_token}` on FedEx API requests, and fail closed
when OAuth response fields are missing or malformed.

## OAuth Compatibility

Official token behavior:

- Endpoint: `POST /oauth/token`
- Request body: form-style OAuth credential fields
- Response fields include `access_token`, `token_type`, `expires_in`, and
  `scope`
- `token_type`: `bearer`
- `expires_in: 3600`
- Standard expiration: one hour

FedEx guidance recommends reusing an OAuth token for its lifetime and avoiding
excess token-generation calls. The rate-limit guide applies IP thresholds to
the Auth token-generation service: burst and average token-call thresholds can
trigger `403 Forbidden` penalties. A future runtime must cache tokens with
expiration awareness and must not request a token per carrier operation.

Current runtime compatibility: **not implemented**. No FedEx OAuth client exists
and no FedEx runtime path can send requests.

## Endpoint Matrix

The endpoint matrix below freezes the official FedEx REST surface required for
future adapter implementation. It is not an implemented PointOfSale runtime
matrix.

| Capability | Official method/path | FEDEX-1 runtime status |
|---|---|---|
| OAuth token | `POST /oauth/token` | Not implemented; must be implemented before FedEx activation |
| Create shipment | `POST /ship/v1/shipments` | Not implemented |
| Validate shipment | `POST /ship/v1/shipments/packages/validate` | Not implemented |
| Retrieve async shipment | `POST /ship/v1/shipments/results` | Not implemented |
| Cancel shipment | `PUT /ship/v1/shipments/cancel` | Not implemented |
| Track by tracking number | `POST /track/v1/trackingnumbers` | Not implemented |
| Track by reference | `POST /track/v1/referencenumbers` | Not implemented |
| Track by door tag | `POST /track/v1/doortags` | Not implemented |
| Track by FedEx Office order | `POST /track/v1/fedexofficeorders` | Not implemented |
| Track by TCN | `POST /track/v1/tcn` | Not implemented |
| Pickup availability | `POST /pickup/v1/pickups/availabilities` | Not implemented |
| Create pickup | `POST /pickup/v1/pickups` | Not implemented |
| Cancel pickup | `PUT /pickup/v1/pickups/cancel` | Not implemented |
| Services and packaging | `POST /availability/v1/packageandserviceoptions` | Not implemented |
| Special service options | `POST /availability/v1/specialserviceoptions` | Not implemented |
| Transit times | `POST /availability/v1/transittimes` | Not implemented |
| Address validation | `POST /address/v1/addresses/resolve` | Not implemented |
| Advanced visibility webhooks | Portal/project subscription capability, v2 | Not implemented |

## Shipment Creation

FedEx Ship API officially supports domestic, international, and return shipment
creation. The required request evidence includes account number, pickup type,
service type, packaging type, shipper information, recipient information,
shipping payment type, payer evidence where required, individual package
weights, and label specification.

Officially documented shipment-create output includes tracking number and label
information.

Current runtime compatibility: **no FedEx serializer exists**. Any future
FedEx adapter must fail closed before provider I/O unless it can serialize the
official `requestedShipment` contract from already-admitted Shipping Platform
snapshots and carrier-neutral DTO evidence.

## Tracking

FedEx Basic Integrated Visibility supports tracking by tracking number, door
tag number, FedEx Office order number, reference, and Transportation Control
Number. Official guidance limits track-by-tracking-number requests to up to 30
tracking numbers per request and recommends date ranges because tracking numbers
can be reused.

Current runtime compatibility: **no FedEx tracking implementation exists**.
Future tracking status mapping must fail closed on unknown provider statuses and
must not invent canonical lifecycle states.

## Labels

FedEx labels are produced through Ship API shipment creation. Official label
specification inputs include image type, printer format, and label stock.
FedEx documents PDF and PNG handling and label-printing constraints. FedEx also
warns not to send non-ASCII characters in Create Shipment requests.

Current runtime compatibility: **no FedEx label parser exists**. A future
adapter must accept only official label formats returned by FedEx and must fail
closed on missing or malformed label URL/data/format.

## Validation

FedEx shipment validation is an official Ship API capability. It validates
shipment details before final submission and returns errors, warnings, notes,
and alerts. FedEx states that shipment validation does not validate street
address details, is shipment-level validation, and supports single-piece
shipment validation.

Current runtime compatibility: **not implemented**. A future adapter must not
treat FedEx shipment validation as Address Validation or as proof of delivery
service availability.

## Pickup

FedEx officially supports Pickup Request API capabilities:

- Check pickup availability
- Create pickup
- Cancel pickup

FedEx pickup has Express/Ground rules, ready-time/access-time constraints,
carrier-specific cancellation limits, and no anonymous pickup support.

Current runtime compatibility: **not implemented**. The certified Shipping
Platform already has shipment action and carrier capability boundaries, so
pickup can be represented as a provider capability in a future phase without
creating a new platform domain. No pickup runtime was added here.

## Address Validation

FedEx Address Validation API validates and resolves recipient address input,
normalizes/standardizes address components, returns annotations, and can classify
addresses as unknown, business, residential, or mixed.

FedEx explicitly states Address Validation should not be used to determine
deliverability and that returned data is suggested use only. FedEx best
practices also warn not to make shipping dependent on optional Address
Validation availability.

Current runtime compatibility: **not implemented**. No Geography redesign or
address-admission redesign was performed. Future FedEx Address Validation must
remain suggestion/evidence only and cannot become Shipping Platform Geography
Authority.

## Webhook Capability

FedEx Advanced Integrated Visibility webhooks are officially supported as a
premium integration capability for near-real-time in-transit shipment events.
The official page identifies version `v2`, near-real-time event updates, and
subscriptions currently available for U.S.-based shipment accounts only.

Current runtime compatibility: **not implemented**. Unsupported webhook
assumptions: rejected. A future FedEx webhook implementation must be separately
certified for subscription entitlement, trust validation, idempotency, event
identity, unknown status fail-closed behavior, and existing-shipment resolution.

## Serialization

Current runtime serialization: **none for FedEx**.

Future serialization requirements:

- Use `https://apis-sandbox.fedex.com/` for test and
  `https://apis.fedex.com/` for production.
- Use official FedEx paths and HTTP methods only.
- Use `Authorization: Bearer` from FedEx OAuth.
- Use JSON content type for JSON POST APIs.
- Do not send empty elements.
- Keep weight and currency values to two explicit decimal places.
- Do not send decimal dimensions where FedEx requires integer dimensions.
- Avoid non-ASCII create-shipment data.
- Serialize only admitted platform snapshots and carrier-neutral DTO evidence.

## Deserialization

Current runtime deserialization: **none for FedEx**.

Future deserialization requirements:

- Treat HTTP status code and response payload together.
- Parse official errors, warnings, notes, and alerts.
- Code to error codes, not mutable message text.
- Fail closed on missing tracking number, missing durable carrier shipment
  identity, malformed label evidence, unknown status values, unsupported label
  formats, malformed metadata, identity mismatches, and unexpected payload
  shapes.

## Breaking Issues

| Issue | Severity | Resolution in FEDEX-1 |
|---|---:|---|
| No FedEx adapter exists despite FedEx being a roadmap/provider candidate | Blocking for FedEx runtime activation | Documented; fail closed |
| No FedEx OAuth token cache/client exists | Blocking | Documented; no invented client added |
| No FedEx serializers/deserializers exist | Blocking | Documented; no partial serializer added |
| No FedEx official status mapping exists in runtime | Blocking | Future adapter must fail closed |
| No FedEx label parser exists | Blocking | Future adapter must implement official label admission |
| No FedEx shipment validation implementation exists | Blocking | Future adapter must implement official validation endpoint |
| No FedEx pickup/service-availability/address-validation runtime exists | Capability gap | Documented only |
| FedEx Advanced Integrated Visibility is premium and U.S.-limited | Operational/capability limitation | Unsupported webhook assumptions: rejected |
| Cancellation has official limits and service-specific behavior | Runtime risk | Unsupported cancellation assumptions: rejected |

## Runtime Hardening

Runtime modified: No.

Reason: no FedEx runtime implementation exists. Adding a FedEx adapter, carrier
code, DTOs, OAuth client, endpoint constants, webhook handler, or frontend
surface in FEDEX-1 would introduce new implementation rather than harden a
violating current FedEx adapter.

Focused hardening performed:

- Added regression coverage proving FedEx remains unregistered until a certified
  adapter exists.
- Added regression coverage freezing the FEDEX-1 official base URLs, OAuth
  token behavior, endpoint matrix, fail-closed unsupported capability decisions,
  and no `.ai-context` update decision.

## Regression Tests

Added:

- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`

Frozen checks:

- FedEx is absent from `CarrierCodes`.
- FedEx adapter is absent from carrier DI registration.
- No FedEx adapter file exists under `PointOfSale.Infrastructure/Shipping/Adapters`.
- FEDEX-1 document freezes official sandbox and production base URLs.
- FEDEX-1 document freezes OAuth token endpoint and one-hour `expires_in: 3600`.
- FEDEX-1 document freezes official endpoint compatibility matrix.
- FEDEX-1 document explicitly rejects unsupported webhook and cancellation
  assumptions.

## Verification

Repository verification:

- `PointOfSale.Core/Constants/CarrierCodes.cs`: no `fedex` carrier code.
- `PointOfSale.API/Program.cs`: no FedEx `IShippingCarrierAdapter`
  registration.
- `PointOfSale.Infrastructure/Shipping/Adapters`: no FedEx adapter.
- Existing carrier registry is adapter-backed and fail-closed for unsupported
  carrier codes.

Official contract verification:

- FedEx official Developer Portal confirms OAuth 2.0 bearer authentication,
  `POST /oauth/token`, one-hour token expiration, and credential modes.
- FedEx official best practices confirm sandbox and production base URLs.
- FedEx official Ship API confirms create shipment, validate shipment, retrieve
  async ship, cancel shipment, labels, and return/tag behaviors.
- FedEx official Basic Integrated Visibility documentation confirms tracking
  methods and up-to-30 tracking numbers guidance.
- FedEx official Pickup Request API confirms availability, create pickup, and
  cancel pickup capabilities.
- FedEx official Service Availability API confirms services/package options,
  special service options, and transit time retrieval.
- FedEx official Address Validation API confirms address resolve/validation and
  suggestion-only limitations.
- FedEx official Advanced Integrated Visibility page confirms v2 webhook
  capability, premium status, near-real-time events, and U.S.-based account
  limitation.
- FedEx official quotas/rate-limits guide confirms daily quotas, 1,400
  transactions per 10 seconds project rate limit, OAuth token-generation IP
  thresholds, `429` throttling, and `403` threshold penalties.
- FedEx official sandbox virtualization guide confirms virtualized/predefined
  sandbox responses and negative test cases, but not production entitlement
  proof.

## Final Recommendation

Proceed to **FEDEX-2 - Request Contract Compatibility** only as a documentation
and request/response schema freeze unless the user explicitly authorizes a FedEx
adapter implementation phase.

Do not register `fedex` as a carrier and do not expose FedEx runtime capability
until a certified adapter implements OAuth, official endpoints, request
serialization, response admission, labels, tracking, validation, cancellation,
and fail-closed status/error handling.

## Special Review

Does FedEx introduce any runtime capability not already represented by the
certified Shipping Platform? No.

FedEx requires capabilities already represented by the frozen platform:

- OAuth-backed carrier authentication fits Carrier Authentication.
- Shipment creation fits Carrier Registration.
- Tracking fits Shipment Tracking Authority.
- Labels fit Carrier Response/Label admission.
- Shipment validation fits provider pre-submission validation evidence.
- Cancellation fits Shipment Action Authority with provider constraints.
- Pickup fits carrier capability/action evidence.
- Service Availability fits carrier service availability evidence.
- Address Validation fits provider suggestion evidence and does not become
  Geography Authority.
- Webhooks fit existing verified carrier webhook/idempotency/tracking authority.

The certified Shipping Platform fully represents FedEx runtime requirements.
No new platform domain is required.

## AI Context Update Decision

No `.ai-context` files were updated.

FEDEX-1 discovered provider-specific FedEx contract and current-runtime absence
findings only. It did not discover new permanent cross-provider architectural
truth.

## Explicit Success Answers

- Reviewed ONLY FedEx? Yes.
- Official contract verified? Yes.
- Breaking issues found? Yes: FedEx runtime is not implemented, so FedEx
  production activation is blocked.
- Runtime modified? No.
- Backend modified? No production backend code modified; architecture
  regression tests were added.
- Frontend modified? No.
- OAuth compatible? Official contract yes; current runtime no FedEx OAuth
  implementation exists.
- Shipment creation compatible? Official contract verified; current runtime has
  no FedEx shipment creation implementation.
- Tracking compatible? Official contract verified; current runtime has no FedEx
  tracking implementation.
- Label compatible? Official contract verified; current runtime has no FedEx
  label implementation.
- Validation compatible? Official contract verified; current runtime has no
  FedEx shipment validation implementation.
- Production compatible? Official production base URL verified; current runtime
  is not production-compatible for FedEx because no adapter exists.
- Does FedEx introduce any new platform capability? No.
- Recommend next phase: FEDEX-2 Request Contract Compatibility, then a separate
  authorized FedEx adapter implementation phase.
