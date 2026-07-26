# FAWRY-1 Foundation Certification

Date: 2026-07-14  
Scope: Fawry payment provider Foundation layer only  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Payment Runtime is certified, provider-neutral, and permanently frozen

## Executive Summary

FAWRY-1 implements only the Fawry Foundation layer. It does not implement payment execution, Runtime bridge integration, callbacks, webhooks, advanced operation execution, persistence, SQL, frontend behavior, or business rules.

The implementation adds immutable Fawry configuration, environment and capability models, credential validation, startup validation, configuration status models, a foundation health contributor, a configuration-only health check, official HTTPS API `HttpClient` registration, and duplicate DI registration rejection.

No Runtime modification was required. No Architecture Exception was needed.

## Official API Discovery Summary

Official source reviewed: FawryPay Developers Guide at `https://developer.fawrystaging.com/` and its Server Side APIs documentation.

Official integration surfaces discovered:

| Surface | Discovery |
|---|---|
| Official .NET server SDK | No official .NET server SDK was discovered for server-to-server payments. Fawry publishes HTTP API examples for multiple languages. |
| Official mobile SDKs | Android and iOS native payment SDKs are documented for mobile application integration. They are not used by this server Foundation layer. |
| Official server API | Official HTTPS server APIs are documented for card, 3DS card, authorize/capture, e-wallet, PayAtFawry reference number, MyFawry, installments, refund, cancel unpaid order, payment notifications, and payment status inquiry. |
| Third-party wrappers | Not used. Foundation registers only `System.Net.Http.HttpClient`. |

Official endpoints discovered:

| Operation | Staging endpoint documented |
|---|---|
| Charge / payment request | `https://atfawry.fawrystaging.com/ECommerceWeb/Fawry/payments/charge` |
| Refund | `https://atfawry.fawrystaging.com/ECommerceWeb/Fawry/payments/refund` |
| Cancel unpaid order | `https://atfawry.fawrystaging.com/ECommerceWeb/api/orders/cancel-unpaid-order` |
| Get payment status V2 | `https://atfawry.fawrystaging.com/ECommerceWeb/Fawry/payments/status/v2` |

Production endpoint text is described by the official docs as the production equivalent of the same paths. The Foundation model separates Sandbox and Production and uses separate base URIs:

| Environment | Base URI |
|---|---|
| Sandbox | `https://atfawry.fawrystaging.com/` |
| Production | `https://www.atfawry.com/` |

## Authentication Model

Fawry server API authentication is signature-based. Requests include merchant credentials and a request `signature` generated with SHA-256 over an operation-specific ordered concatenation and the merchant secure key.

Examples from official documentation:

| Operation | Signature material |
|---|---|
| PayAtFawry charge | `merchantCode + merchantRefNum + customerProfileId(if exists) + paymentMethod + amount(two decimals) + secureKey` |
| Refund | `merchantCode + referenceNumber + refundAmount(two decimals) + reason(if exists) + secureKey` |
| Cancel unpaid order | `orderRefNo + merchantAccount + lang + secureKey` |
| Get payment status V2 | `merchantCode + merchantRefNumber + secureKey` |
| Server notification V2 | `fawryRefNumber + merchantRefNum + paymentAmount(two decimals) + orderAmount(two decimals) + orderStatus + paymentMethod + paymentReferenceNumber(if exists) + secureKey` |

The Foundation layer validates only credential presence and configuration shape. It does not generate signatures, verify signatures, execute provider calls, or admit evidence.

## Merchant Credentials

The Foundation configuration requires:

| Credential | Required by Foundation | Purpose |
|---|---:|---|
| `MerchantCode` | Yes | Merchant account identifier supplied by FawryPay. |
| `ApiKey` | Yes | Merchant API credential required by governance for provider enablement. |
| `Secret` | Yes | Secure key used by official SHA-256 signatures. |

Credentials are never emitted by health status, health check data, logs, or documentation examples in source.

## Environment Model

`FawryEnvironment` is explicit:

| Environment | Required flag | Base URI |
|---|---|---|
| Sandbox | `SandboxMode = true`, `SandboxEnabled = true` | `https://atfawry.fawrystaging.com/` |
| Production | `SandboxMode = false`, `ProductionEnabled = true` | `https://www.atfawry.com/` |

Environment mismatch fails closed:

| Mismatch | Result |
|---|---|
| Sandbox with `SandboxMode = false` | Invalid configuration |
| Production with `SandboxMode = true` | Invalid configuration |
| Enabled Sandbox with sandbox feature disabled | Invalid configuration |
| Enabled Production with production feature disabled | Invalid configuration |

## Callback And Webhook Model

Fawry official documentation describes two notification models:

| Model | Official behavior | FAWRY-1 implementation |
|---|---|---|
| Merchant callback URL | Preconfigured merchant endpoint set during merchant account setup; Fawry sends HTTP POST status updates and expects HTTP 200 acknowledgement. | Configuration model only. No callback adapter implemented. |
| Per-order webhook URL | Some charge requests support `orderWebHookUrl` for order events. | Capability model only. No webhook adapter implemented. |

Official notification V2 includes `messageSignature` using SHA-256 over documented transaction fields and the secure key. Signature verification is a later webhook/evidence-adapter phase, not FAWRY-1.

## Capability Matrix

| Capability | Official support discovered | FAWRY-1 status |
|---|---:|---|
| Official HTTPS API | Yes | Modeled and registered through named `HttpClient`. |
| Official .NET server SDK | No | Explicitly false; no SDK package added. |
| SHA-256 signatures | Yes | Modeled as a capability. Execution/verification not implemented. |
| Sandbox | Yes | Modeled and validated. |
| Production | Yes | Modeled and validated. |
| PayAtFawry reference number | Yes | Capability only; no execution. |
| Card / 3DS card | Yes | Capability discovery only; no execution. |
| E-wallet | Yes | Capability discovery only; no execution. |
| Authorize and capture | Yes | Capability modeled; inactive. |
| Refund | Yes | Capability modeled; inactive. |
| Partial refund | Yes, official refund allows full or partial captured amount | Capability modeled; inactive. |
| Void / cancel unpaid order | Yes | Capability modeled; inactive. |
| Transaction inquiry | Yes, Get Payment Status V2 | Capability modeled; inactive. |
| Expiration | Yes, `paymentExpiry` and notification/status expiry fields | Capability modeled; inactive. |
| Provider idempotency | No explicit provider idempotency-key contract discovered | Not certified; later phases must use application replay/checkpoint authority. |

Execution flags fail closed unless their later certification capabilities are true. FAWRY-1 leaves all execution certifications false.

## Credential Governance

`FawryCredentialValidator` fails closed when enabled configuration is missing or malformed:

| Condition | Decision |
|---|---|
| Missing merchant code | Invalid |
| Missing API key | Invalid |
| Missing secret | Invalid |
| Credential values too short to be real credentials | Invalid |
| Missing callback/webhook configuration while enabled | Invalid |
| Invalid callback/webhook URL shape | Invalid |
| Environment mismatch | Invalid |
| Official HTTPS capability disabled | Invalid |
| SHA-256 signature capability disabled | Invalid |
| Payment execution flag enabled before certification | Invalid |
| Callback/webhook execution flag enabled before certification | Invalid |
| Advanced operation execution flag enabled before certification | Invalid |

The provider adapter must not resolve credentials in later phases. Tenant configuration authority and one-shot credential handoff remain governed by `PAYMENT_REFERENCE_FREEZE.md`.

## Dependency Injection

`AddFawryFoundation` registers:

| Registration | Lifetime / type |
|---|---|
| `FawryOptions` | Options binding from `CommerceHub:Payments:Fawry` with startup validation |
| `FawryOptionsValidator` | Singleton `IValidateOptions<FawryOptions>` |
| `FawryOfficialApi` | Named official `HttpClient` with environment base URI and configured timeout |
| `IFawryFoundationHealthContributor` | Singleton foundation contributor |

Duplicate foundation registration throws `InvalidOperationException` and fails closed.

FAWRY-1 intentionally does not register a payment adapter, Runtime bridge, webhook adapter, callback adapter, advanced operations adapter, lifecycle service, persistence, SQL artifact, controller, or frontend integration.

## Health Model

`FawryFoundationHealthCheck` is configuration-only.

It reports non-secret operational facts:

| Field | Secret? |
|---|---:|
| enabled | No |
| environment | No |
| sandboxMode | No |
| credentialsConfigured | No |
| callbackConfigured | No |
| officialApiAvailable | No |
| transportName | No |
| baseUri | No |
| usesUnofficialWrapper | No |
| officialDotNetSdkAvailable | No |

It does not perform live Fawry calls. It does not expose merchant code, API key, secret, signatures, request payloads, customer data, or provider references.

## Architecture Preservation

Preserved boundaries:

| Boundary | FAWRY-1 decision |
|---|---|
| Payment Runtime | Unchanged. |
| Runtime Bridge | Not implemented. |
| Provider contracts | Not modified. |
| Runtime contracts | Not modified. |
| Provider neutrality | Preserved. |
| Controllers | Existing controllers not modified. |
| Frontend | Not modified. |
| Persistence / SQL | Not modified. |
| Business rules | Not modified. |
| CommerceHub runtime/payment flows | Not modified. |

Runtime independence guards verify `PointOfSale.Core/Payments/Runtime` contains no Fawry names or Fawry endpoints.

## Certification Evidence

Focused tests added in `PointOfSale.Tests/Architecture/FawryFoundationCertificationTests.cs`:

| Evidence | Coverage |
|---|---|
| Configuration binding | Immutable options, environment, callbacks, capabilities |
| Credential validation | Missing merchant code, API key, secret fail closed |
| Environment validation | Sandbox/Production mismatch and feature gates fail closed |
| Capability validation | Official HTTPS/SHA-256 supported, execution certifications false |
| Startup validation | Callback and execution flags fail closed |
| DI registration | Options, validator, named official client, health contributor only |
| Duplicate registration | Rejected |
| Health model | Configuration-only, serializable, no secrets |
| Architecture guards | Runtime independent from Fawry |
| Source guards | Foundation contains no execution, Runtime, lifecycle, or SQL authority |

## Certification Decision

| Question | Decision |
|---|---|
| Official SDK/API certified? | Yes for official HTTPS API discovery. No official .NET server SDK was discovered or added. |
| Configuration certified? | Yes for Foundation layer. |
| Credential governance certified? | Yes for Foundation validation and redaction. Tenant handoff is later scope. |
| Environment separation certified? | Yes. |
| Dependency Injection certified? | Yes for Foundation-only registrations. |
| Health model certified? | Yes, configuration-only and no live provider calls. |
| Architecture boundaries preserved? | Yes. |
| Runtime independence preserved? | Yes. |
| Foundation certified? | Yes. Focused FAWRY-1 tests passed 11/11 and API build passed. |
| Recommend next phase? | Yes. Proceed to FAWRY-2 Provider Adapter discovery/mapping only, without Runtime changes. |

## Explicit Exclusions

FAWRY-1 does not certify or implement:

- payment execution;
- Runtime bridge;
- callback adapter;
- webhook adapter;
- signature generation or verification;
- refund, partial refund, void, capture, or inquiry execution;
- persistence or SQL;
- controller changes;
- frontend changes;
- live Sandbox validation;
- production deployment readiness.

No `.ai-context` file was modified because no new permanent cross-provider architectural truth was discovered.

## Verification

Executed in this implementation session:

| Command | Result |
|---|---:|
| `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~PointOfSale.Tests.Architecture.FawryFoundationCertificationTests" -nologo -v minimal --no-restore` | 11 passed, 0 failed, 0 skipped |
| `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal --no-restore` | Passed, 0 warnings, 0 errors |
