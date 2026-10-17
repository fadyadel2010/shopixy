# MYLERZ-1 Official Contract And Runtime Compatibility

> Historical review. The 2026-10-11 comprehensive review found public vendor
> integration Swagger schemas and supersedes this document's discovery result.
> The Egypt merchant operational gate remains necessary; see
> [MYLERZ-2 comprehensive contract remediation](MYLERZ-2_COMPREHENSIVE_CONTRACT_REMEDIATION.md)
> for current evidence, production corrections and verification limitations.

Date: 2026-07-07
Scope: Mylerz official public API contract and current runtime compatibility only
Status: FAIL CLOSED

## Executive Summary

The Mylerz adapter was reviewed only for official-contract and runtime compatibility. Geography, Customs, Shipment Origin, Runtime Authority, Checkout, Warehouse, POS Admin, and carrier mapping were not reviewed.

No public official Mylerz technical API contract was found. The official Mylerz corporate site exposes merchant sign-in/order and shipment tracking experiences, and describes logistics dashboards and integrations, but it does not publish official REST endpoints, authentication, request schemas, response schemas, enums, webhook payloads, cancellation semantics, labels, versioning, sandbox URLs, retry policy, or timeout policy.

Because the runtime was calling concrete Mylerz endpoints and accepting concrete payload/status/webhook assumptions without a public official contract, those operations were hardened to fail closed before outbound HTTP. Mylerz is therefore not production-compatible until a vendor-issued official contract is obtained, frozen, and implemented.

Sources reviewed:

- Official Mylerz site: <https://www.mylerz.com/>
- Official Mylerz merchant link exposed by the site: <https://mylerz.net/>
- Existing provider baseline: `docs/architecture/PCC-0_SHIPPING_PROVIDER_INVENTORY_AND_VERSION_FREEZE.md`
- Frozen platform architecture: `docs/architecture/SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE.md`

## Official Contract Summary

| Area | Official public evidence | Verdict |
|---|---|---|
| Developer/API portal | None found | Unknown |
| API version | Not published | Unknown |
| Production API base URL | Not published | Unknown |
| Sandbox API base URL | Not published | Unknown |
| Authentication | Not published | Unknown |
| Shipment creation | Not published | Unsupported by public contract |
| Tracking API | Public tracking UI exists; API contract not published | API unknown |
| Label retrieval | Not published | Unsupported by public contract |
| Cancellation | Not published | Unsupported by public contract |
| Webhooks | Not published | Unsupported by public contract |
| Retry policy | Not published | Unknown |
| Timeout policy | Not published | Unknown |
| Serialization | Not published | Unknown |
| Deserialization | Not published | Unknown |
| Official enums/status model | Not published | Unknown |

## Endpoint Matrix

| Runtime endpoint before hardening | Method | Official public support | Runtime action |
|---|---:|---|---|
| `https://api.mylerz.com/v2/shipping/rates` | POST | Not published | Blocked before HTTP |
| `https://api.mylerz.com/v2/shipments` | POST | Not published | Blocked before HTTP |
| `https://api.mylerz.com/v2/shipments/{trackingNumber}` | DELETE | Not published | Blocked before HTTP |
| `https://api.mylerz.com/v2/shipments/{trackingNumber}/tracking` | GET | Not published | Blocked before HTTP |
| `https://api.mylerz.com/v2/account` | GET | Not published | Blocked before HTTP |
| `https://sandbox-api.mylerz.com/v2/...` | mixed | Not published | Blocked before HTTP |

## Authentication

The previous runtime sent `Authorization: Bearer <ApiKey>` and optional `X-Api-Secret` / `X-Account-Id`. No official public Mylerz source was found that confirms this authentication mechanism or these headers.

Runtime hardening: credential validation and all outbound operations now return `MYLERZ_OFFICIAL_CONTRACT_UNAVAILABLE` before HTTP.

## Shipment Creation

The previous runtime assumed `POST /shipments` with a snake_case JSON body containing pickup, delivery, package, COD, and notes objects. No official public Mylerz shipment creation request or response contract was found.

Runtime hardening: shipment creation fails before outbound HTTP. No tracking number, carrier shipment id, label, status, or provider metadata is admitted from this unsupported contract.

## Tracking

The official public website includes a customer-facing tracking experience and visible tracking states such as pickup, in-transit, on-the-way, and delivered. It does not publish a tracking API endpoint, response schema, status enum, timeline model, or COD collection model.

Runtime hardening: tracking API calls fail before outbound HTTP. The adapter no longer admits invented Mylerz tracking statuses or maps unknown status evidence to canonical shipment lifecycle state.

## Labels

No official public label retrieval or label-in-create response contract was found.

Runtime hardening: because shipment creation is blocked, label URL/base64/format evidence is not admitted.

## Cancellation

No official public cancellation endpoint, method, eligibility rule, response schema, or state model was found.

Runtime hardening: cancellation fails before outbound HTTP.

## Webhook Support

No official public webhook registration, signing, header, payload, retry, or event contract was found.

Runtime hardening: `ParseWebhook` returns `null` for Mylerz payloads until an official webhook contract is supplied and frozen.

## Serialization

The previous runtime used snake_case JSON request/response models. Official public Mylerz serialization requirements were not found.

Runtime hardening: no Mylerz request body is serialized for outbound HTTP while the contract is unavailable.

## Breaking Issues

| Issue | Severity | Resolution |
|---|---:|---|
| Runtime called unpublished Mylerz API base URLs and paths. | High | Blocked before HTTP |
| Runtime assumed bearer plus custom headers without official public confirmation. | High | Blocked before HTTP |
| Runtime assumed shipment create, tracking, cancellation, account, rate, label, and webhook schemas. | High | Blocked before HTTP / webhook rejected |
| Runtime advertised adapter service levels from local assumptions. | Medium | Adapter returns no service levels |
| Runtime mapped unknown Mylerz statuses to `InTransit`. | High | No Mylerz status is admitted while contract unavailable |
| Runtime returned synthetic quote success on API failure. | High | Quote now fails closed |

## Runtime Hardening

Implemented minimal provider-specific hardening:

- `MylerzAdapter.GetQuoteAsync` fails closed before HTTP.
- `MylerzAdapter.CreateShipmentAsync` fails closed before HTTP.
- `MylerzAdapter.GetTrackingAsync` fails closed before HTTP.
- `MylerzAdapter.CancelShipmentAsync` fails closed before HTTP.
- `MylerzAdapter.ValidateCredentialsAsync` fails closed before HTTP.
- `MylerzAdapter.ParseWebhook` rejects payloads by returning `null`.
- `MylerzAdapter.GetSupportedServiceLevels` returns no adapter-advertised service levels.
- The Mylerz DI registration comment was corrected from production-ready language to fail-closed contract language.

No platform redesign was performed.

## Regression Tests

Added focused tests in `PointOfSale.Tests/Infrastructure/MylerzOfficialContractCompatibilityTests.cs`:

- Create shipment fails before outbound HTTP.
- Quote fails before outbound HTTP and returns no synthetic quotes.
- Tracking fails before outbound HTTP.
- Cancellation fails before outbound HTTP.
- Credential validation fails before outbound HTTP.
- Webhook payloads are not admitted.
- Adapter service levels are not advertised without an official public contract.

## Verification

Verification command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~MylerzOfficialContractCompatibilityTests -nologo -v minimal
```

Result: see implementation session notes. The expected result is that all focused Mylerz compatibility tests pass and no outbound HTTP is recorded.

Actual result: PASS, 7 passed, 0 failed.

## Final Recommendation

Do not enable Mylerz production runtime. Obtain a current vendor-issued official Mylerz API package covering environments, authentication, shipment creation, tracking, label retrieval, cancellation, webhooks, status enums, serialization, response schemas, retries, timeouts, and versioning. Freeze that artifact before re-enabling outbound Mylerz calls.

Recommended next phase: MYLERZ-2 vendor contract implementation and credentialed sandbox verification, only after official Mylerz documentation is supplied.

## Success Answers

- Reviewed ONLY Mylerz? Yes.
- Official contract verified? No; no public official technical contract was available.
- Breaking issues found? Yes; runtime used unsupported/invented endpoints and payload assumptions.
- Runtime modified? Yes; Mylerz operations now fail closed before outbound HTTP.
- Backend modified? Yes, minimally: one DI registration comment was corrected. No API/controller/service redesign.
- Frontend modified? No.
- Runtime compatibility? Fail-closed compatible with the absence of an official public contract; not operation-compatible.
- Production compatible? No.
- Recommend next phase: obtain/freeze vendor-issued Mylerz API documentation, then implement MYLERZ-2 against that contract.
