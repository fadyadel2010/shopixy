# PAYMOB Certification Closure 4 — Tenant-Scoped Adapter Credential Handoff

Date: 2026-07-14  
Scope: `PAYMOB-FINAL-004` only  
Payment Runtime posture: frozen and unchanged  
Decision: **HIGH FINDING CLOSED**

## Finding

`PAYMOB-FINAL-004 — Provider execution is global-credential-bound, not tenant-credential-bound` was High because the application resolved and validated encrypted tenant Paymob credentials, discarded the result, and then invoked provider adapters that constructed requests from globally injected `PaymobOptions`.

Exact tenant/global credential equality prevented some fail-open behavior, but global options remained the adapter execution authority and distinct tenant merchant accounts could not govern their own provider calls.

This closure addresses only `PAYMOB-FINAL-004`. It does not converge CommerceHub, activate advanced operations, change provider protocol, alter sensitive persistence, or close any other PAYMOB-FINAL finding.

## Root Cause

The pre-closure create path was:

```text
Tenant configuration resolver
  -> validate tenant credentials
  -> discard validated credentials
  -> Runtime application service
  -> Runtime Bridge
  -> PaymobPaymentAdapter
  -> injected global PaymobOptions
  -> provider
```

`PaymobPaymentAdapter` and `PaymobAdvancedOperationsAdapter` both accepted `PaymobOptions` in their constructors. Their request factories read `ApiKey`, `IntegrationId`, and `IframeId` from that global object. DI also registered the raw global options value as a scoped adapter dependency.

The tenant resolver therefore acted only as an admission comparison, not as the credential source for provider execution.

## Credential Handoff Model

The certified application flow is now:

```text
Paymob operation request
  -> PaymobTenantConfigurationResolver
  -> fresh current tenant PAYMOB row
  -> enabled + not deleted + encrypted configuration present
  -> decrypt + required-field validation
  -> capability + Sandbox/Production validation
  -> PaymobTenantRuntimeCredentials
  -> PaymobRuntimeApplicationService
  -> scoped one-shot PaymobAdapterCredentialHandoff
  -> unchanged Runtime Bridge
  -> PaymobPaymentAdapter
  -> tenant identity match + one-time consume
  -> provider
```

Handoff invariants:

1. Only the application service begins the handoff, after the certified resolver returns validated tenant credentials.
2. The handoff is scoped, never singleton or static.
3. Only one handoff can be active in a scope. Overlap fails closed.
4. The adapter requires canonical `tenantId` metadata and checks it against the credential tenant.
5. Consumption is single-use and clears the handoff before provider I/O begins.
6. Application disposal clears an unconsumed handoff on every exception or early exit.
7. Missing, incomplete, mismatched, concurrent, or stale handoff state fails before authentication, order registration, payment-key creation, refund, void, capture, or inquiry execution.
8. The handoff performs no SQL, configuration, decryption, fallback, inference, or caching.

Global Paymob options remain foundation/environment/capability governance for the existing application host, but their credential fields are no longer read by either provider adapter and no longer require equality with tenant credentials. They are not adapter execution authority.

## Execution-Path Audit

| Adapter | Production invocation path | Credential source after Closure 4 | Decision |
|---|---|---|---|
| `PaymobPaymentAdapter` | `PaymobPaymentService` -> `PaymobRuntimeApplicationService` -> unchanged `PaymobRuntimeBridge` -> adapter | Fresh validated tenant credentials, supplied through one-shot application handoff | Certified for this finding |
| `PaymobPaymentAdapter` retry/recovery | Existing intent retry and background recovery re-enter `PaymobRuntimeApplicationService` | Fresh resolver execution and a new one-shot handoff on every invocation | Certified for this finding |
| `PaymobAdvancedOperationsAdapter` | No live application caller found; DI registration only | Adapter now requires the same one-shot handoff and fails closed when none is supplied | Dormant and safely fenced; not activated |
| `PaymobWebhookEvidenceAdapter` | `PaymobWebhookRuntimeIntegrationService` -> webhook evidence adapter -> Runtime | No provider credentials are used; tenant HMAC admission occurs earlier through `PaymobTenantConfigurationResolver` | Not a provider credential consumer |
| CommerceHub direct Paymob paths | CommerceHub-local services | Existing CommerceHub configuration | Unchanged; `PAYMOB-FINAL-005` remains separate |

Repository search found no controller, frontend, persistence component, or business service directly invoking the payment or advanced-operation adapters outside these audited paths.

## Architecture Preservation

Unchanged by this closure:

- `PointOfSale.Core/Payments/Runtime/**`;
- `PaymobRuntimeBridge` source and contracts;
- Runtime sequencing and outcome semantics;
- Paymob provider endpoints, wire requests, responses, error translation, and checkpoint protocol;
- `PaymobWebhookEvidenceAdapter` and webhook protocol;
- controllers, routes, DTOs, and frontend;
- `PaymentLifecycleService` and business rules;
- persistence entities, EF model, migrations, and SQL authority;
- Marketplace and CommerceHub behavior.

The adapter redesign boundary was not expanded. Request mapping and provider calls remain in the existing adapters; only their credential input authority changed.

## Evidence

Implementation evidence:

- `PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdapterCredentialHandoff.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobAdvancedOperationsAdapter.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs`

Focused certification coverage in `PointOfSale.Tests/Architecture/PaymobCertificationClosure4Tests.cs` proves:

- tenant API key, integration ID, and iframe ID reach the payment adapter and provider request;
- distinct tenant credentials no longer require equality with global credential fields;
- missing tenant handoff stops before provider execution;
- tenant mismatch stops before provider execution;
- a consumed credential cannot be reused by a later adapter invocation;
- the advanced-operation adapter also fails closed without a tenant handoff;
- payment and advanced adapters contain no `IConfiguration`, `IOptions`, `PaymobOptions`, `CommerceHubPaymobOptions`, tenant persistence, or resolver lookup;
- DI no longer exposes raw global `PaymobOptions` as an adapter dependency;
- resolve -> handoff -> bridge ordering is enforced at the application boundary;
- Runtime Bridge and webhook evidence adapter remain credential-handoff free.

Existing Closure 3 tests continue to prove deleted/disabled configuration rejection, environment mismatch rejection, capability rejection, missing credential rejection, and no global-only fallback.

Validation executed on 2026-07-14:

| Validation | Result |
|---|---:|
| API build | **Passed; 0 warnings, 0 errors** |
| Test project build | **Passed; 0 errors**; existing repository warnings remain |
| Closure 4 plus affected adapter/Closure 2/Closure 3 tests | **39 passed, 0 failed** |
| Applicable Paymob + frozen Payment Runtime regression excluding the known unrelated artifact-guard class | **215 passed, 0 failed** |
| Unmodified Paymob + Payment Runtime aggregate | **218 passed, 1 failed** |

The sole unmodified aggregate failure remains `PaymobPaymentAuthorityCertificationTests.CertificationArtifacts_MustRecordSfPay5Truth`, which expects the unrelated historical text marker `PASS SF-PAY5` in `.ai-context`. It predates Closure 4, does not exercise adapter credentials, and remains recorded by `PAYMOB_FINAL.md` and Closures 1 through 3. This single-finding closure does not manufacture that unrelated marker.

## Certification Decision

`PAYMOB-FINAL-004` is closed for the audited Storefront/POS Paymob application and provider-adapter boundary.

The payment adapter now executes only after consuming the exact validated tenant credential set supplied by the application layer. It cannot independently resolve credentials, access configuration, use global or default credentials, fall back, cache credentials, or reuse a stale credential. The dormant advanced-operation adapter is fenced by the same rule.

This decision does not grant overall Paymob Software, Security, Operations, Migration, Deployment, CommerceHub, template, or Live Operational certification. Every other PAYMOB-FINAL finding retains its prior disposition.

## Success Answers

| Question | Answer |
|---|---|
| High finding closed? | **Yes — `PAYMOB-FINAL-004` only.** |
| Tenant-scoped adapter handoff implemented? | **Yes. Fresh validated tenant credentials cross a scoped one-shot application handoff.** |
| Adapter independent credential lookup removed? | **Yes. Neither provider adapter accesses configuration, tenant persistence, or the resolver.** |
| Global fallback removed? | **Yes. Global credential fields are not adapter inputs and distinct tenant credentials are accepted.** |
| Runtime preserved? | **Yes. The frozen Payment Runtime is unchanged.** |
| Architecture preserved? | **Yes. Runtime Bridge, provider/webhook protocols, lifecycle, controllers, frontend, persistence, business rules, and CommerceHub are unchanged.** |
| Regression passed? | **Yes for Closure 4 and the applicable regression: 39/39 focused and 215/215 applicable Paymob/Runtime tests.** The unmodified aggregate retains the one documented unrelated `PASS SF-PAY5` artifact failure. |
| Recommend PAYMOB-FINAL Recertification? | **Yes, for re-audit of the current finding matrix while retaining every other open finding and live/deployment prerequisite.** |

**FINAL DECISION: PAYMOB-FINAL-004 CLOSED. TENANT-SCOPED ONE-SHOT ADAPTER CREDENTIAL HANDOFF IS ENFORCED. PAYMENT RUNTIME AND ALL FORBIDDEN SURFACES REMAIN UNCHANGED.**
