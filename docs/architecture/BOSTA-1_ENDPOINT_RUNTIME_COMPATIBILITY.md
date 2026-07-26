# BOSTA-1 — Official Endpoint & Runtime Surface Compatibility Audit

Audit date: 5 July 2026  
Provider: Bosta only  
Status: COMPLETE

## Executive Summary

The pre-hardening Bosta runtime was not endpoint-compatible with the official Bosta Business API surface frozen by PCC-1.

Five concrete runtime differences were found:

1. Production traffic used `https://api.bosta.co/api/v2` instead of the official `https://app.bosta.co/api/v2`.
2. shipment creation omitted the required operation query `apiVersion=1`.
3. tracking used `GET /deliveries/{trackingNumber}` instead of `GET /deliveries/business/{trackingNumber}`.
4. cancellation used `DELETE /deliveries/{trackingNumber}` instead of `DELETE /deliveries/business/{trackingNumber}/terminate`.
5. credential validation could fall back to undocumented `GET /deliveries?pageNumber=1&pageSize=1`.

Minimum runtime hardening corrected those five differences. Four focused regression tests now freeze the production base URL, version query, methods, route templates, path escaping, authorization header, JSON content type, and removal of the undocumented fallback.

Post-hardening compatibility for the implemented Bosta production endpoint surface is **100% (4 of 4 retained outbound endpoint calls match the official surface)**. This percentage measures correctness of implemented outbound production calls, not coverage of every optional Bosta operation.

## Reviewed Scope

Reviewed:

- official and runtime base URLs;
- API version;
- outbound Bosta endpoints;
- HTTP methods;
- route templates;
- path and query parameters;
- authentication style;
- required headers;
- content type and accept behavior;
- endpoint availability;
- carrier adapter, registry, configuration flow, dependency injection, and named `HttpClient` registration.

Excluded:

- request and response bodies;
- DTO contracts;
- webhook and callback payloads;
- status mapping;
- retry, performance, security, business logic, authority, pricing, and lifecycle.

No Aramex, DHL, or Mylerz implementation was inspected.

## Official Endpoint Inventory

Official authority:

- PCC-1 official documentation freeze;
- Bosta documentation;
- Bosta published API definition at `https://docs.bosta.co/api/api.yaml`.

Official base URL: `https://app.bosta.co/api/v2`  
Official version: `/api/v2`, with `apiVersion=1` on delivery creation  
Authentication: API key directly in `Authorization`; the published definition also declares HTTP Bearer as an alternative on many operations  
JSON request content type: `application/json`

| Capability | Official Method | Official Route | Parameters | Runtime Support |
|---|---|---|---|---|
| Create shipment | POST | `/deliveries?apiVersion=1` | query `apiVersion=1` | Yes |
| Bulk create | POST | `/deliveries/bulk?apiVersion=1` | query `apiVersion=1` | No |
| Track/view shipment | GET | `/deliveries/business/{trackingNumber}` | required path `trackingNumber` | Yes |
| Update shipment | PUT | `/deliveries/business/{trackingNumber}` | required path `trackingNumber` | No |
| Cancel/terminate | DELETE | `/deliveries/business/{trackingNumber}/terminate` | required path `trackingNumber` | Yes |
| Search deliveries | POST | `/deliveries/search` | endpoint-specific | No |
| Generate AWB/label | POST | `/deliveries/mass-awb` | endpoint-specific | No |
| Create/list pickup | POST / GET | `/pickups` | endpoint-specific | No |
| Read/update/delete pickup | GET / PUT / DELETE | `/pickups/{id}` | required path `id` | No |
| Pickup search and availability | GET | `/pickups/search`, `/pickups/available-dates`, `/pickups/creationValidity` | endpoint-specific | No |
| Return order | POST | `/deliveries?apiVersion=1` | return behavior selected by order type; body excluded | No dedicated runtime operation |
| Cities | GET | `/cities` | none | Yes, credential-reachability call |
| Pricing | GET | `/pricing/calculator`, `/pricing/shipment/calculator` | endpoint-specific | No; runtime quote is synthetic |

The missing operations are endpoint-availability gaps, not evidence that an implemented call is incompatible. Pickup, label, and return support are classified MEDIUM because official endpoints exist and the common runtime adapter does not expose them. Bulk creation, shipment update/search, and pricing are LOW for this audit because they are optional relative to the runtime's current create/track/cancel responsibility.

## Runtime Endpoint Inventory

Runtime source: `PointOfSale.Infrastructure/Shipping/Adapters/BostaAdapter.cs`.

Post-hardening production calls:

| Runtime Purpose | Method | Runtime URL |
|---|---|---|
| Create shipment | POST | `https://app.bosta.co/api/v2/deliveries?apiVersion=1` |
| Track shipment | GET | `https://app.bosta.co/api/v2/deliveries/business/{escapedTrackingNumber}` |
| Cancel shipment | DELETE | `https://app.bosta.co/api/v2/deliveries/business/{escapedTrackingNumber}/terminate` |
| Credential reachability | GET | `https://app.bosta.co/api/v2/cities` |

Other runtime surface:

- `BostaAdapter` is registered as `IShippingCarrierAdapter`.
- `ShippingCarrierRegistry` discovers the adapter from dependency injection.
- named `HttpClient` registration is `"Bosta"` with a 15-second timeout.
- tenant `CarrierConfiguration` supplies credentials and `UseSandbox`.
- `CarrierConfiguration.ApiUrl` is propagated but not used by `BostaAdapter`.
- production and sandbox URLs remain adapter constants.
- no feature flag changes Bosta endpoint routes.

## Endpoint Comparison

| Operation | Pre-hardening Runtime | Official | Difference | Severity | Result |
|---|---|---|---|---|---|
| Production base | `https://api.bosta.co/api/v2` | `https://app.bosta.co/api/v2` | Wrong base URL | HIGH | Fixed |
| Create | `POST /deliveries` | `POST /deliveries?apiVersion=1` | Missing required version query | HIGH | Fixed |
| Tracking | `GET /deliveries/{trackingNumber}` | `GET /deliveries/business/{trackingNumber}` | Changed route | MEDIUM | Fixed |
| Cancellation | `DELETE /deliveries/{trackingNumber}` | `DELETE /deliveries/business/{trackingNumber}/terminate` | Changed route | MEDIUM | Fixed |
| Credential fallback | `GET /deliveries?pageNumber=1&pageSize=1` | No matching official GET route | Unsupported/unused runtime endpoint | MEDIUM | Removed |
| Cities | `GET /cities` | `GET /cities` | None | — | Compatible |

Methods now match the official operations. Tracking-number path values are URI-escaped before route composition.

## Base URL Comparison

| Environment | Official | Runtime Before | Runtime After | Verdict |
|---|---|---|---|---|
| Production | `https://app.bosta.co/api/v2` | `https://api.bosta.co/api/v2` | `https://app.bosta.co/api/v2` | Compatible |
| Sandbox | No public official sandbox authority located | `https://staging-api.bosta.co` | unchanged | Not independently certifiable |

The sandbox URL is not counted in the production compatibility percentage. No official public source establishes a Bosta sandbox URL or versioned sandbox path.

## Version Comparison

| Surface | Official | Runtime Before | Runtime After |
|---|---|---|---|
| API family | `/api/v2` | `/api/v2` only in production; absent from sandbox constant | `/api/v2` in production; sandbox remains undocumented |
| Create-operation version | `apiVersion=1` | Missing | Present |

Production version compatibility is restored.

## Authentication Comparison

Runtime sends the configured API key directly in the `Authorization` header for every outbound Bosta call. This matches PCC-1 and Bosta's API-key guide.

The published API definition declares:

- `ApiKey` in the `Authorization` header;
- HTTP Bearer as an alternative on many operations;
- only Bearer on the terminate operation.

The API-key guide nevertheless identifies “Business terminate delivery” as an example available to a full-access API key. This is a LOW documentation inconsistency, not a proven runtime authentication break. No authentication code was changed.

## Required Headers, Content-Type, and Accept

| Item | Official | Runtime | Verdict |
|---|---|---|---|
| `Authorization` | Direct API key for API-key flow | Present on all calls | Compatible |
| `Content-Type` for create | `application/json` | `JsonContent` emits `application/json` | Compatible |
| `Accept` | No universal mandatory value established by PCC-1 or the published operation metadata | Not explicitly set | Compatible |

GET and DELETE calls have no request body, so request `Content-Type` is not required.

## Endpoint Compatibility Matrix

| Classification | Count | Details |
|---|---:|---|
| Breaking runtime differences found | 5 | Base URL, create version query, tracking route, cancellation route, undocumented fallback |
| Deprecated runtime endpoints | 0 | No official deprecation classification was published |
| Removed/unsupported runtime endpoints | 1 | Undocumented delivery-list fallback, removed |
| Renamed/changed runtime routes | 2 | Tracking and cancellation |
| New official endpoints missing in runtime | 8 capability groups | Bulk, update, search, label, pickup, return workflow, pricing, dedicated POD absent |
| Unused official endpoints | Multiple | Outside current adapter capabilities |
| Compatible retained runtime endpoints after hardening | 4/4 | Create, track, cancel, cities |

## Breaking Changes

Pre-hardening, the wrong production host and missing creation version query were HIGH-impact breaking differences. Tracking and cancellation used route templates not present in the current official API definition. The credential fallback targeted an unsupported GET collection route.

No dated Bosta changelog exists, so this audit cannot establish when these differences became breaking changes.

## Deprecated Endpoints

No structured official Bosta deprecation register was found. The incorrect runtime routes are classified as unsupported current routes, not formally deprecated routes.

## Runtime Hardening Performed

Only `BostaAdapter` endpoint composition changed:

- production base URL changed to `https://app.bosta.co/api/v2`;
- create now appends `?apiVersion=1`;
- tracking now uses `/deliveries/business/{trackingNumber}`;
- cancellation now uses `/deliveries/business/{trackingNumber}/terminate`;
- tracking-number path values are URI-escaped;
- undocumented delivery-list credential fallback was removed.

No request-body, response-body, DTO, webhook, status-mapping, retry, security, pricing, lifecycle, frontend, or unrelated refactor was performed.

## Regression Tests

Added `PointOfSale.Tests/Infrastructure/BostaEndpointSurfaceTests.cs`.

The four tests freeze:

- official production create URL and `apiVersion=1`;
- official tracking route;
- official terminate route;
- HTTP methods;
- tracking-number path escaping;
- direct `Authorization` API-key header;
- create `application/json` content type;
- absence of the undocumented credential fallback.

## Verification

Command:

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~BostaEndpointSurfaceTests`

Result: **PASS — 4 passed, 0 failed, 0 skipped**.

The focused test command rebuilt the referenced Core, Infrastructure, API, and test projects successfully. Existing unrelated compiler and analyzer warnings remain.

No live Bosta call was made, and no production credential or tenant database configuration was inspected.

## Explicit Compatibility Answers

- Does production use the official base URL? **YES**, after hardening.
- Does runtime use official API version `/api/v2`? **YES** for production.
- Does create include `apiVersion=1`? **YES**, after hardening.
- Are implemented production HTTP methods correct? **YES**.
- Are implemented production route templates correct? **YES**, after hardening.
- Are required path/query parameters present? **YES**.
- Does runtime use the PCC-1 API-key authentication style? **YES**.
- Is create sent as `application/json`? **YES**.
- Is an explicit `Accept` header required by the frozen official surface? **NO requirement was established**.
- Are pickup, label, and dedicated return operations implemented? **NO**; official capability gaps remain.
- Is the configured sandbox surface officially certifiable? **NO**; no public official sandbox authority was located.
- Is `CarrierConfiguration.ApiUrl` runtime routing authority? **NO**; Bosta ignores it.
- Were request/response bodies, DTOs, webhooks, or status mapping reviewed? **NO**.

## Final Verdict

**PRODUCTION COMPATIBLE — YES, for the implemented Bosta endpoint surface after runtime hardening.**

Implemented production outbound calls are now **100% endpoint-compatible (4/4)** with the current official surface used by this adapter.

This verdict does not claim:

- live credential validation;
- live provider acceptance;
- sandbox compatibility;
- complete official feature coverage;
- request/response contract compatibility;
- webhook or status compatibility.

Missing pickup, label, and dedicated return capabilities do not invalidate the implemented create/track/cancel endpoint surface, but they remain documented endpoint-availability gaps for future product work.

## Completion Summary

- Runtime files modified: **1**
- Backend files modified: **1**
- Frontend files modified: **0**
- Tests added: **4**
- Compatibility: **100% of retained implemented production endpoint calls**
- Breaking Changes Found: **5**
- Runtime Hardening Required: **YES — completed**
- Production Compatible: **YES**

## Next Recommended Certification

BOSTA-2

Request Contract Compatibility Audit

Status

READY
