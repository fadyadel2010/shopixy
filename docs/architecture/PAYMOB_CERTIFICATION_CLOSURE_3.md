# PAYMOB Certification Closure 3 — Tenant Credential Authority

Date: 2026-07-14  
Scope: PAYMOB-FINAL-003 only  
Decision: **High finding closed**

## Finding

`PAYMOB-FINAL-003 — Deleted and environment-ungoverned webhook credentials remain admissible` was High because Paymob webhook/callback credential resolution used an unfiltered tenant lookup without `!IsDeleted`, and did not enforce provider enablement, operation capability, or Sandbox/Production authority.

This closure does not close PAYMOB-FINAL-004 (the frozen adapter's global-options credential handoff), PAYMOB-FINAL-005 (direct CommerceHub Paymob HTTP), or any sensitive-persistence finding.

## Root Cause

Credential admission was duplicated:

- create/retry/recovery validated tenant configuration inside `PaymobRuntimeApplicationService`;
- webhook/callback used `GetPaymobConfigurationUnfilteredAsync` inside `PaymobPaymentService`;
- the webhook/callback query bypassed tenant filters but omitted the explicit soft-delete predicate;
- operation flags were declared but startup validation made them unusable, while live webhook/callback execution did not consult them;
- no single application authority combined current tenant state, credential validation, capability validation, and environment validation.

## Execution-Path Audit

| Execution | Current path | Credential origin after Closure 3 | Closure disposition |
|---|---|---|---|
| Storefront/POS create | `PaymobPaymentService` → `PaymobRuntimeApplicationService` → Runtime Bridge → adapter | Fresh tenant authority admission; exact tenant/global equality remains required until PAYMOB-FINAL-004 | Governed by shared authority |
| Storefront/POS retry | Re-enters create application boundary | Fresh tenant authority admission on every attempt | Governed by shared authority |
| Checkout/background recovery | Re-enters `IPaymobPaymentService.CreatePaymentIntentAsync` | Fresh tenant authority admission before Runtime/provider work | Governed by shared authority |
| Storefront/POS webhook | `PaymobPaymentService.ProcessWebhookAsync` | Tenant authority with `Webhook` capability; returned tenant HMAC secret | Governed by shared authority |
| Storefront/POS callback | Candidate intent tenant → shared resolver | Tenant authority with `Callback` capability; returned tenant HMAC secret | Governed by shared authority |
| Storefront lifecycle refund evidence | Paymob webhook → lifecycle service | Same webhook tenant authority; no independent provider credential lookup | Governed at ingress |
| Certified advanced refund/void/capture/inquiry adapter | Registered but no live application caller found | Adapter remains dormant in the audited live graph | No new execution path opened |
| CommerceHub create/recovery/webhook/refund/inquiry | Direct CommerceHub HTTP and global CommerceHub options | Global platform configuration | Unchanged; PAYMOB-FINAL-005 remains separate |

The audit deliberately does not migrate CommerceHub or alter adapter protocol. Doing so would address a different PAYMOB_FINAL High and violate this closure's single-finding scope.

## Authority Model

`PaymobTenantConfigurationResolver` is now the only tenant Paymob credential-admission authority in the application layer.

```text
Provider operation request
  -> Paymob operational capability
  -> current tenant PAYMOB row (fresh SQL read)
  -> enabled + not deleted + encrypted configuration present
  -> decrypt + required-field validation
  -> capability validation
  -> Sandbox/Production validation
  -> exact tenant/global compatibility fence (temporary PAYMOB-FINAL-004 boundary)
  -> Runtime/provider execution or HMAC verification
```

The resolver uses `IgnoreQueryFilters()` only because webhook/callback resolution can begin outside an established tenant query scope. That bypass is paired with explicit `TenantId`, `Code == "PAYMOB"`, `IsEnabled`, `!IsDeleted`, ciphertext presence, and `AsNoTracking()` predicates.

No default tenant, global-only, shared-row, static, singleton, or cached credential fallback exists in the resolver. Each operation performs a fresh database read.

## Validation Flow

Execution is rejected with `PaymobNotConfiguredException` before Runtime/provider execution when any of the following is true:

- provider or foundation feature is disabled;
- credential or environment validation gates are disabled;
- requested operation capability is disabled or uncertified;
- tenant Paymob row is missing, disabled, deleted, or has no encrypted configuration;
- encrypted configuration cannot be decrypted or fails payment-method validation;
- API key, integration ID, iframe ID, or HMAC secret is missing;
- integration or iframe identifier is non-numeric;
- tenant `IsSandbox` disagrees with configured Sandbox/Production mode;
- tenant credentials disagree with the temporary global adapter compatibility fence.

Capability mapping is explicit:

- payment create and recovery require payment execution plus certified payment/adapter capability;
- webhook requires webhook execution plus certified callback/HMAC capability;
- callback requires callback execution plus certified callback/HMAC capability;
- refund, void, capture, and inquiry are declared fail-closed behind refund execution and certified adapter capability, but no new live caller was introduced.

## Architecture Preservation

Unchanged by this closure:

- Payment Runtime;
- Runtime Bridge;
- Paymob provider adapter;
- Paymob webhook evidence adapter;
- controllers and routes;
- frontend and DTO contracts;
- persistence model and migrations;
- payment lifecycle/business rules;
- provider protocol, endpoints, request/response models, and provider neutrality.

Only the application credential authority, its composition registration, capability validation, focused tests, and certification documentation changed.

## Evidence

Implementation evidence:

- `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Configuration/CommerceHubPaymobOptions.cs`
- `PointOfSale.API/Program.cs`

Certification evidence:

- `PointOfSale.Tests/Architecture/PaymobCertificationClosure3Tests.cs`
- `PointOfSale.Tests/Payments/PaymobCallbackValidationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobFoundationCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobContractClosureApplicationCertificationTests.cs`

Verification on 2026-07-14:

- API build: passed, 0 warnings, 0 errors.
- Test project build: passed, 0 warnings, 0 errors.
- Focused closure/callback/foundation/application-boundary tests: **29 passed, 0 failed**.
- Paymob regression excluding the unrelated pre-existing SF-PAY5 documentation-marker class: **115 passed, 0 failed**.
- Full Paymob-name slice: 118 passed, 1 failed only because `PaymobPaymentAuthorityCertificationTests.CertificationArtifacts_MustRecordSfPay5Truth` expects an older `PASS SF-PAY5` marker that is absent from all `.ai-context` files; this closure did not alter that unrelated certification record.

The focused tests prove current tenant authority selection, disabled/deleted rejection, environment mismatch rejection, capability rejection, missing credential rejection, no global-only fallback, no Runtime Bridge invocation after authority failure, and application-layer architecture guards.

## Certification Decision

PAYMOB-FINAL-003 is closed.

| Question | Decision |
|---|---|
| High finding closed? | Yes — PAYMOB-FINAL-003 only |
| Tenant authority enforced? | Yes, for the certified Storefront/POS create, retry, recovery, webhook, and callback paths |
| Global fallback removed? | Yes as an execution-admission fallback; valid global values cannot authorize a missing/disabled/deleted tenant. The separate adapter handoff remains PAYMOB-FINAL-004. |
| Environment validated? | Yes |
| Deleted configuration rejected? | Yes |
| Capability validation enforced? | Yes |
| Runtime preserved? | Yes |
| Architecture preserved? | Yes |
| Regression passed? | Yes for Closure 3 and the scoped Paymob regression (29/29 focused; 115/115 excluding the unrelated pre-existing SF-PAY5 documentation guard) |
| Recommend Closure 4? | Yes — close PAYMOB-FINAL-004 with a tenant-scoped adapter credential handoff, without changing Runtime semantics |
