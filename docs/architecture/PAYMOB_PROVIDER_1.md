# PAYMOB-PROVIDER-1 - Canonical Paymob Adapter

Date: 2026-07-17

Status: SOURCE IMPLEMENTED AND PROVIDER-BOUNDARY CERTIFIED

Scope: Paymob external provider adapter for the certified Shopixy Payment Platform. Runtime, Lifecycle, Workflow, Workflow Execution Service, Dispatcher, Intent Handlers, Order Domain, Event Pipeline, Provider Contracts, and Canonical Runtime Contracts remain unchanged.

## Architecture

Paymob is implemented as an external provider boundary only:

```text
Canonical Payment Runtime contracts
  -> PaymobPaymentProvider
  -> PaymobRequestMapper
  -> Paymob HTTPS API / HMAC primitive
  -> PaymobResponseMapper
  -> canonical ProviderEvidence / CanonicalProviderError
  -> existing Runtime/Application/Lifecycle owners
```

`PaymobPaymentProvider` delegates to the existing Paymob payment adapter, advanced operations adapter, capability provider, and webhook verifier. It does not load or mutate orders, invoke lifecycle, execute workflow, dispatch intents, publish events, read SQL, or call runtime stages.

## Adapter Boundary

Paymob-specific protocol details terminate inside `PointOfSale.API.Services.Payments.Paymob`:

- official Paymob auth/order/payment-key and advanced operation DTOs;
- HMAC SHA-512 payload construction and fixed-time verification;
- Paymob callback/webhook parsing;
- Paymob response and status normalization;
- Paymob error and HTTP failure classification;
- tenant-configuration-driven capability reporting.

Only canonical `ProviderEvidence`, `CanonicalProviderError`, capability reports, and bounded handoff facts leave the adapter.

## Mapping Strategy

Request mapping:

- `PaymobRequestMapper.ToCheckoutRequest` maps canonical `PaymentAction` metadata to Paymob auth, order registration, and payment key DTOs.
- `PaymobRequestMapper.WithOperation` maps canonical operation intent to adapter-local advanced operation metadata.
- `PaymobRequestMapper.ToAdvancedOperationRequest` maps capture, void, refund, partial refund, and transaction inquiry requests to Paymob DTOs.

Response mapping:

- `PaymobResponseMapper.ToProviderEvidence` maps checkout preparation responses to canonical provider evidence.
- `PaymobResponseMapper.ToOperationEvidence` maps advanced operation responses to canonical provider evidence.
- `PaymobResponseMapper.ToWebhookEvidence` maps HMAC-authenticated callbacks to canonical provider evidence.

No Paymob request DTO, response DTO, exception, HMAC value, API key, payment key, or raw provider object is a Runtime contract.

## Error Translation

`PaymobErrorMapper` translates provider failures to canonical errors and stable payment failure reasons:

| Paymob failure | Canonical provider error | Failure reason |
|---|---|---|
| Authentication / bad key | `Authentication` | `AuthenticationFailed` |
| Authorization / entitlement failure | `Authorization` | `AuthorizationFailed` |
| Validation / malformed request | `Validation` | `InvalidRequest` |
| Timeout / unknown HTTP state | `Unknown` or `Network` | `Unknown` or `NetworkFailure` |
| Provider 5xx / unavailable | `ProviderUnavailable` | `ProviderUnavailable` |
| Decline / failed transaction | `ProviderRejected` | `ProviderDeclined` |
| Duplicate / replay callback | `Validation` | `InvalidState` |
| Expired callback timestamp | `Validation` | `InvalidState` |
| Invalid HMAC/signature | `Authentication` | `AuthenticationFailed` |
| Unsupported capability | `Validation` | `InvalidRequest` |

Provider exceptions and wire bodies remain bounded and redacted before crossing the adapter boundary.

## Capability Strategy

`PaymobCapabilityProvider` is tenant-configuration-driven. It reads the supplied `PaymobCapabilityModel` through the existing options/configuration authority and reports supported and unsupported capabilities per tenant configuration.

Certified capabilities include checkout authorization, capture, void, refund, partial refund, transaction inquiry, webhook processing, HMAC verification, official HTTPS API use, tenant-scoped credentials, and capability reporting when the tenant configuration certifies them.

Unsupported capabilities are reported explicitly with reasons instead of being hardcoded as globally available.

## Webhook Verification

`PaymobWebhookVerifier` performs evidence-only webhook verification:

- validates required HMAC signature and HMAC secret;
- parses JSON or query-string callback payloads;
- computes Paymob SHA-512 HMAC using the Paymob field order;
- compares signatures in fixed time;
- validates optional timestamp tolerance;
- maps supported events to canonical `ProviderEvidence`;
- returns canonical errors for invalid signatures, malformed payloads, unsupported events, expired timestamps, and provider-negative outcomes.

The verifier stops at canonical evidence. It does not call `PaymentRuntimeOrchestrator`, mutate lifecycle, write SQL, publish events, or perform durable replay persistence. Replay compatibility is preserved through stable evidence identity and hash generation.

## Regression Results

Executed:

```text
dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter FullyQualifiedName~PaymobProvider1CertificationTests
```

Result:

```text
Build: 0 errors, existing warnings only
PaymobProvider1CertificationTests: Passed 13/13
```

Regression scope verified by source inspection and tests:

- Runtime registration remains unchanged.
- Paymob provider registration is additive inside `AddPaymobProviderAdapter`.
- Stripe canonical provider source remains compatible and is not required by Paymob runtime contracts.
- Existing Paymob application/runtime integration paths remain intact.

## Certification Summary

`PAYMOB-PROVIDER-1` implements the Paymob canonical provider adapter layer. Paymob behavior is isolated to provider code, and all provider-specific protocol, authentication, HMAC verification, request/response translation, error mapping, and capability logic remain inside the Paymob adapter boundary.

This certification is source/provider-adapter certification only. Live sandbox execution, production credentials, dashboard configuration, POS Admin UI changes, Storefront UI changes, OAuth, operational validation, and Grafana changes remain out of scope for a future `PAYMOB-SANDBOX-1` phase.

## Certification Questions

| Question | Answer |
|---|---|
| Does Paymob remain completely isolated inside the adapter? | Yes. Paymob protocol types and errors terminate in `PointOfSale.API.Services.Payments.Paymob`. |
| Were any Runtime changes required? | No. |
| Were any Workflow changes required? | No. |
| Were any Lifecycle changes required? | No. |
| Were any Dispatcher changes required? | No. |
| Were any Order Domain changes required? | No. |
| Were any Event Pipeline changes required? | No. |
| Does Stripe remain fully compatible? | Yes. The Paymob implementation is additive and does not require Stripe behavior changes. |
| Are Runtime contracts still sufficient? | Yes. Existing `PaymentAction`, `ProviderEvidence`, canonical errors, metadata, and capability contracts are sufficient. |
| Is Paymob certified as a canonical provider implementation? | Yes, for provider-adapter source scope. Sandbox/live validation remains a later phase. |
