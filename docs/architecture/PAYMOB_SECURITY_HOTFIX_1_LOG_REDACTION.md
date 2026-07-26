# PAYMOB-SECURITY-HOTFIX-1 Log Redaction

Date: 2026-07-14

Scope: Paymob production execution logging for POS Admin and Storefront Paymob.

Status: SECURITY HOTFIX CERTIFIED AT SOURCE LEVEL

## Executive Summary

PAYMOB-SECURITY-HOTFIX-1 closes the remaining source-level security finding from `PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md`.

The Paymob create-payment success log no longer emits `PaymentKey`, `IframeUrl`, truncated payment keys, iframe URLs, sensitive redirect URLs, access tokens, authorization headers, API keys, HMAC secrets, raw callback payloads, or customer payment artifacts.

This hotfix changes logging only. Payment flow, provider protocol, Runtime behavior, provider adapter behavior, Runtime Bridge behavior, application contracts, DTOs, controllers, business rules, frontend behavior, persistence, and SQL are unchanged.

## Security Finding

`PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md` identified an open redaction issue in the Paymob create-payment path:

```text
Paymob intent created ... PaymentKey: {PaymentKey} | IframeUrl: {IframeUrl}
```

The logged values were payment-session artifacts rather than permanent credentials, but they are still sensitive operational artifacts and must not appear in production logs.

## Affected Log Sites

| File | Site | Finding | Disposition |
| --- | --- | --- | --- |
| `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs` | Paymob intent creation success log | Structured placeholders emitted `PaymentKey` and `IframeUrl`. | Closed. Placeholders and argument values removed. |
| `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs` | Incomplete payment-intent rehydration warning | Message template named missing `RedirectUrl/PaymentKey` fields. | Closed. Message now refers generically to Paymob session rehydration. |

No other Paymob production logging source was found to emit `{PaymentKey}` or `{IframeUrl}`.

## Changes Applied

The Paymob create-payment success log now records only safe operational identifiers and timing:

```text
TenantId
OrderId
PaymobOrderId
DurationMs
CorrelationId
```

The incomplete-intent warning now avoids naming payment-session artifact fields and continues to preserve the operational signal needed for recovery.

## Redaction Strategy

The preferred strategy was omission, not replacement. Sensitive payment-session artifacts are not passed to the logger and are not present as structured log placeholders.

Forbidden in Paymob production logs after this hotfix:

- `PaymentKey`
- `IframeUrl`
- API keys
- HMAC secrets
- Authorization headers
- Access tokens
- raw callback payloads
- customer payment artifacts
- sensitive redirect URLs

## Architecture Impact

| Boundary | Impact |
| --- | --- |
| Payment Runtime | Runtime unchanged. |
| Runtime Bridge | Runtime Bridge unchanged. |
| Provider Adapter | Provider Adapter unchanged. |
| Webhook Adapter | Webhook Adapter unchanged. |
| Application Contracts and DTOs | Unchanged. Payment artifacts still flow through authorized contracts where required for checkout execution. |
| Controllers | Unchanged. |
| Business Rules | Unchanged. |
| Frontend | Unchanged. |
| Persistence and SQL | Unchanged. |

This hotfix does not redesign logging, payment flow, provider protocol, tenant configuration, callback/webhook handling, Runtime admission, replay, duplicate handling, or lifecycle persistence.

## Certification Decision

Security finding closed.

Paymob source-level security certification is no longer blocked by create-payment logging of `PaymentKey` or `IframeUrl`. Live Sandbox certification remains separately withheld until official Paymob Sandbox credentials, public callback/webhook reachability, live Paymob event delivery, and latency evidence are available.

PAYMOB-FINAL should be rerun so the final certification can consume this closed redaction evidence.

Focused certification evidence on 2026-07-14:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~PaymobSecurityHotfix1LogRedactionTests|FullyQualifiedName~PaymobProductionSandboxValidationTests" -nologo -v minimal
Passed: 10, Failed: 0, Skipped: 0
```

Compilation evidence on 2026-07-14:

```text
dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore -nologo -v minimal
Build succeeded: 0 warnings, 0 errors
```

## Success Answers

| Question | Answer |
| --- | --- |
| Sensitive logging removed? | Yes. Paymob production logs no longer emit the identified payment-session artifacts. |
| PaymentKey redacted? | Yes. It is omitted from Paymob production log templates and logger arguments. |
| IframeUrl redacted? | Yes. It is omitted from Paymob production log templates and logger arguments. |
| Secrets unaffected? | Yes. No API key, HMAC secret, authorization header, access token, raw callback payload, or customer payment artifact logging was introduced. |
| Architecture preserved? | Yes. This is logging-only. |
| Runtime unchanged? | Yes. |
| Security finding closed? | Yes. |
| Compilation succeeded? | Yes. `PointOfSale.Tests` build succeeded with 0 warnings and 0 errors. |
| Certification tests passed? | Yes. Focused hotfix and Paymob sandbox validation tests passed 10/10. |
| Recommend rerunning PAYMOB-FINAL? | Yes. |
