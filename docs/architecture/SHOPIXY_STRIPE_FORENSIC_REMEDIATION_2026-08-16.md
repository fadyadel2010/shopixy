# SHOPIXY Stripe Forensic Audit and Remediation

Date: 2026-08-16  
Tenant: `shopixy` (`83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`)  
Certification: **BLOCKED**

## 1. Executive Summary

The visibility defect was reproduced and remediated through the real Storefront endpoint. Before remediation, the authoritative tenant had four configured and enabled methods but the endpoint returned only COD, Paymob, and Manual Wallet. Stripe's first failed gate was `STRIPE_RUNTIME_DISABLED` in `StorefrontPaymentRuntimeReadinessService.cs:172-176`.

POS Admin and Storefront did not use the same predicate. Admin called Stripe valid when the tenant ciphertext decrypted and contained three nonblank fields. Storefront additionally required online capability, global runtime and environment gates, PaymentIntent and webhook feature flags, tenant/global mode agreement, and Stripe key prefixes. The committed Development configuration never enabled those Stripe runtime capabilities.

The remediation independently enables tenant-scoped Stripe sandbox execution without enabling the separate global CommerceHub Stripe credential lane. It preserves canonical backend readiness and tenant authorization. The post-remediation HTTP response contains all four methods, including `STRIPE`.

The full Stripe lifecycle is not production-certified. A real Stripe test payment and Stripe CLI webhook were not executed, the backend test project is blocked by an unrelated pre-existing compile error, and significant CommerceHub/advanced-operation risks remain. PASS is therefore prohibited.

## 2. Observed Defect

POS Admin displayed:

- Name: `ST Credit/Debit Card`
- Provider: `STRIPE`
- Type: Online
- Configured: Valid
- Status: Enabled

The Storefront returned only:

- `COD`
- `PAYMOB`
- `MANUALWALLET`

There is no selective Stripe filter in the Storefront frontend. Stripe was omitted by backend runtime-readiness authorization before DTO projection.

## 3. Runtime Baseline

Read-only baseline HTTP result from `GET /api/storefront/payment-methods`:

| Evidence | Baseline |
|---|---|
| HTTP status | 200 |
| Response status | `AVAILABLE` |
| Configured count | 4 |
| Enabled count | 4 |
| Available count | 3 |
| Returned codes | `COD`, `PAYMOB`, `MANUALWALLET` |
| Stripe row | Present, enabled, non-deleted |
| First failed gate | `STRIPE_RUNTIME_DISABLED` |

The probe used a Development-only, DB-verified tenant token. No credential or client secret was printed.

## 4. Configuration Audit

### Tenant Stripe configuration

| Property | Safe result |
|---|---|
| Configured | YES |
| Ciphertext present | YES |
| Decryptable | YES |
| Shared configuration valid | YES |
| Runtime configuration valid | YES |
| Mode | test |
| Publishable prefix | `pk_test_` validated |
| Secret prefix | valid test secret/restricted prefix validated |
| Webhook secret configured | YES, `whsec_` prefix validated |
| Currency | EGP |
| Provider | STRIPE |
| Enabled | YES |
| Deleted | NO |
| Supports online | YES |

Decryptability and runtime validity are proven by the post-remediation endpoint reaching both `ResolveCredentialsAsync(...PaymentIntent)` and `ResolveCredentialsAsync(...Webhook)` and returning Stripe.

### Global Stripe configuration

`StripeOptions` binds from `CommerceHub:Payments:Stripe` in `StripeOptions.cs:6-21`.

| Property | Baseline | Remediated Development |
|---|---:|---:|
| Global CommerceHub `Enabled` | NO | NO |
| Global credentials configured | NO | NO |
| Tenant runtime feature enabled | NO | YES |
| Sandbox enabled | YES | YES |
| Production enabled | NO | NO |
| PaymentIntents enabled | NO | YES |
| Webhooks enabled | NO | YES |
| Refund/capture/portal enabled | NO | NO |

No production/live credentials were changed. Production and Staging remain fail-closed.

The prior validator incorrectly required global credentials when only `FeatureFlags.Enabled` activated tenant execution. `StripeOptionsValidator.RequiresGlobalCredentials` now restricts that requirement to the global `Enabled` lane. Tenant execution still resolves encrypted tenant keys.

### Configuration sources

Precedence is base JSON, environment JSON, optional local JSON, Development user-secrets, then environment variables (`Program.cs:59-73`). No Stripe override was found in Development user-secrets or process environment during baseline. Secrets remain external to committed configuration.

## 5. Admin Contract

Admin reads tenant rows directly from PostgreSQL in `PaymentMethodsAdminController.cs:84-105`. Its catalog always contains Stripe (`PaymentMethodsAdminController.cs:41-49`), so catalog visibility alone does not prove tenant configuration.

Baseline Admin validity was:

`row exists AND ciphertext decrypts AND publishableKey/secretKey/webhookSecret are nonblank`

This was weaker than runtime validity. Remediation adds shared Stripe prefix/environment consistency validation in `PaymentMethodConfigurationValidator` and selected sandbox/live validation in `PaymentMethodsAdminController.ApplyEnvironmentValidation`.

Admin “Valid + Enabled” still describes tenant configuration and administrative state, not deployment runtime availability. Runtime availability remains the Storefront readiness service's responsibility. This distinction is intentional, but operational status should eventually be shown separately in Admin.

## 6. Storefront Availability Contract

Runtime path:

`CheckoutPage.tsx:223-244` -> `checkoutStore.ts:2353-2377` -> `GET /api/storefront/payment-methods` -> tenant/publication middleware -> `StorefrontPaymentMethodsController.GetPaymentMethods` -> `PaymentMethodReadRepository.GetEnabledPaymentMethodsAsync` -> `IStorefrontPaymentRuntimeReadinessService.EvaluateAsync` -> `StripeTenantConfigurationResolver` -> response DTO -> frontend structural admission and rendering.

### Stripe exclusion gates

| Source | Condition | Baseline value | Excluded | Purpose/correctness |
|---|---|---|---:|---|
| `PaymentMethodReadRepository.cs:50-55` | SQL row enabled and non-deleted | true | No | Correct SQL authority |
| `PaymentMethodReadRepository.cs:332-361` | Ciphertext decrypts and shared config validates | true | No | Correct fail-closed check |
| `StorefrontPaymentMethodsController.cs:114-118` | Supported code and valid config | true | No | Correct allowlist/canonical code check |
| `StorefrontPaymentRuntimeReadinessService.cs:93-105` | Exact tenant/code row exists | true | No | Correct tenant isolation |
| `StorefrontPaymentRuntimeReadinessService.cs:149-165` | Not deleted, enabled, online capable | true | No | Correct capability check |
| `StorefrontPaymentRuntimeReadinessService.cs:172-176` | Runtime enabled | false | **Yes** | Correct gate; Development activation missing |
| `StorefrontPaymentRuntimeReadinessService.cs:178-182` | Sandbox mode and flag enabled | true after remediation | No | Correct test/live separation |
| `StorefrontPaymentRuntimeReadinessService.cs:190-193` | PaymentIntent capability enabled | false before remediation | Yes downstream | Correct capability gate |
| `StorefrontPaymentRuntimeReadinessService.cs:195-198` | Webhook capability enabled | false before remediation | Yes downstream | Correct finality requirement |
| `StripeTenantConfigurationResolver.cs:68-87` | Current tenant encrypted row resolves | true after remediation | No | Correct tenant authority |
| `StripeTenantConfigurationResolver.cs:159-184` | Mode and safe prefixes agree | true after remediation | No | Correct credential-mode gate |
| `StorefrontPaymentMethodsController.cs:139-145` | No failed runtime gate | false before, true after | Yes before | Correct final response filter |
| `checkoutStore.ts:1043-1124` | Structurally valid response list | true | No | No Stripe-specific filtering |
| `PaymentStep.tsx:350-381` | Available response rendered | true after API inclusion | No | Correct presentation-only behavior |

The checkout POST re-runs SQL configuration and the same runtime readiness in `CheckoutService.cs:5895-5939`; forged frontend selection cannot bypass backend authorization.

## 7. Tenant Resolution

`TenantResolutionMiddleware` removes untrusted inbound `X-Tenant-Id`, resolves authenticated claims or verified host/domain authority, rejects disagreement, and writes scoped `TenantContext` (`TenantResolutionMiddleware.cs:81-187`). The endpoint then requires a nonempty trusted tenant (`StorefrontPaymentMethodsController.cs:76-81`).

Payment-method SQL reads and Stripe credential resolution both constrain by tenant ID. Webhooks derive tenant authority from a stored Stripe PaymentIntent/provider reference; advisory tenant query/header values cannot select another tenant. Existing tenant-isolation tests cover the boundary.

## 8. Provider Readiness

The canonical storefront readiness service is registered in `Program.cs:787` and is reused by payment-method listing and checkout. Stripe requires PaymentIntent and webhook readiness because exposing a provider that cannot receive authoritative finality would be unsafe.

The remediated Development configuration activates only the tenant-scoped Stripe sandbox capabilities. Global CommerceHub Stripe remains disabled and has no committed credentials.

`StripeOperationalReadinessHealthCheck` remains weaker than tenant readiness: it checks for any enabled Stripe ciphertext but does not decrypt every tenant row or call the resolver (`StripeOperationalReadinessHealthCheck.cs:56-65`). This is a P2 observability risk.

## 9. Stripe Registration

Stripe is consistently registered as:

- enum: `PaymentProvider.Stripe`
- tenant/catalog/gateway code: `STRIPE`
- canonical evidence name: `stripe`
- storefront aliases normalized by checkout: `STRIPE`, `CARD`, `CREDIT_CARD`

DI registers the options/foundation, tenant resolver, tenant PaymentIntent client, runtime bridge/application service, payment service, payment gateway, and webhook integration (`Program.cs:718-738`, `Program.cs:780-790`).

Stripe is absent from Super Admin provider administration registrations, which currently contain Paymob only (`PaymentProviderAdministrationService.cs:324-338`). This is a P2 management/rotation gap, not the Storefront exclusion cause.

## 10. Checkout Initialization

The active path is:

`CheckoutController` -> `CheckoutService` -> canonical `IPaymentGateway` -> `StripePaymentGateway` -> `StripePaymentService` -> `StripeRuntimeApplicationService` -> `StripePaymentIntentAdapter` -> tenant-scoped Stripe SDK client.

Controls verified in source:

- immutable order amount and ISO currency are used;
- currency converts to minor units before Stripe;
- order/customer/tenant ownership is checked;
- local PaymentIntent is committed before provider I/O;
- provider idempotency is derived from the durable local attempt;
- client secret is encrypted with Data Protection;
- active provider uniqueness is database-enforced;
- browser receives publishable key and client secret only as handoff evidence;
- Stripe.js result never marks the order paid; browser navigates to pending status.

The adapter previously converted `orderId` to `canonical_orderId`, while webhook handlers required plain `orderId`. New requests now emit both canonical and plain correlation keys. Consumers accept both forms because already-created Stripe PaymentIntents may later deliver webhooks.

Three-decimal/Stripe-special currency handling remains inconsistent between `StripeMoneyMapper` and shared `CurrencyHelper`. EGP is not affected. Multi-currency capability should not be certified until this is resolved.

## 11. Webhook Contract

Canonical endpoint: `POST /api/webhooks/stripe`.

Verified controls:

- `Stripe-Signature` is extracted by the common request pipeline;
- raw body is captured before model binding;
- Stripe SDK signature construction is mandatory;
- invalid signature returns 401;
- body size and rate limits are enabled;
- tenant is resolved from stored provider intent/payment identity;
- local lookup constrains provider and tenant;
- success supplies provider amount and currency to canonical lifecycle validation;
- webhook audit stores redacted payload and signature digest;
- SQL unique indexes protect event idempotency;
- serializable processing and processing-state checks protect concurrent delivery;
- lifecycle finality rejects stale reversal of terminal SQL truth;
- refunds correlate by charge or nested PaymentIntent and use cumulative-delta handling.

Business events:

- `payment_intent.succeeded`: canonical success/finality path.
- `payment_intent.payment_failed`: now records retry diagnostics while preserving pending order/payment/inventory authority; Stripe permits reconfirming the same intent.
- `payment_intent.canceled`: canonical cancellation terminal path.
- `charge.refunded`: canonical refund recording/reconciliation path.

No signature secret, raw client secret, or PII was printed.

## 12. Payment Lifecycle

Success enters `IPaymentLifecycleService.HandlePaymentResultAsync` with provider amount/currency and webhook event identity. The lifecycle owns collected state, accounting/outbox effects, inventory outcome publication, idempotency, quarantine, and terminal-state protection.

The prior failure handler incorrectly treated every `payment_intent.payment_failed` event as terminal and released inventory even though Stripe may return the same PaymentIntent to `requires_payment_method` for another confirmation. This was a P1 lifecycle defect. It now remains pending until explicit cancellation, expiration/exhaustion authority, operator action, or success.

Cancellation and refund remain terminal/provider-evidence paths. A real provider success/failure/refund sequence was not executed in this audit.

## 13. Retry/Attempt Lineage

Creation uses a deterministic Stripe idempotency key tied to the durable local payment attempt. Database uniqueness prevents concurrent active provider attempts for the same order/provider. Existing pending client-secret handoff can be replayed after ownership validation.

Residual risks:

- the local Stripe presentation TTL can expire while the provider PaymentIntent remains reusable, and regeneration currently re-enters code that may return the same expired local record;
- ambiguous provider creation exceptions mark the local attempt failed without a provider retrieval/reconciliation proof;
- retry-exhaustion authority for repeated Stripe declines is not explicitly modeled.

These are P1 follow-up items and prevent full lifecycle PASS.

## 14. Security/Finality

Positive controls include encrypted tenant credentials, encrypted storefront Stripe client secrets, signature verification, trusted tenant resolution, amount/currency checks, SQL idempotency, serializable webhook processing, and canonical finality protection.

Critical residual risk: the separate CommerceHub `StripeAdapter` places a Stripe client secret in both `PaymentToken` and `RawResponse` (`StripeAdapter.cs:85-104`), and CommerceHub persists those fields (`CommerceHubPaymentService.cs:370-383`). Unlike the tenant storefront path and Paymob CommerceHub path, this material is not protected before persistence. Runtime presence of affected historical rows was not queried. This is classified P0 until encrypted storage, redacted raw response, and backfill are implemented.

Advanced Stripe capture/cancel/refund clients also use global credentials rather than tenant-scoped credentials. They are not currently wired into active POS Admin operations, but connecting them as-is would create cross-account authority risk.

## 15. Paymob vs Stripe Comparative Audit

| Layer | Difference | Classification |
|---|---|---|
| Configuration | Both store tenant credentials encrypted; fields and provider validation differ | CORRECT PROVIDER-SPECIFIC BEHAVIOR |
| Admin | Paymob previously had mode checks; Stripe did not | POSSIBLE BUG, remediated |
| Tenant resolution | Both use tenant SQL authority and no global credential fallback for storefront | REQUIRED SAME ARCHITECTURE |
| Runtime enablement | Paymob Development enabled its tenant runtime; Stripe did not | ROOT CAUSE, remediated |
| Global credentials | Stripe tenant feature activation incorrectly required global credentials | LEGACY DIFFERENCE, remediated |
| Availability | Both now use canonical readiness and require finality capability | CORRECT SHARED BEHAVIOR |
| Handoff | Paymob uses hosted Unified Checkout URL; Stripe uses Payment Element client secret | REQUIRED DIFFERENCE |
| Metadata | Stripe adapter prefixed correlation keys not consumed by webhook path | POSSIBLE BUG, remediated |
| Failure retry | Paymob has explicit declined-payment retry authority; Stripe treated failed confirmation as terminal | POSSIBLE BUG, remediated in lifecycle classification |
| Webhook auth | Paymob HMAC-SHA512 vs Stripe signed timestamp/header | REQUIRED DIFFERENCE |
| Replay | Both use provider event identity plus SQL uniqueness | CORRECT PROVIDER-SPECIFIC BEHAVIOR |
| Refund | Provider payloads and cumulative semantics differ | REQUIRED DIFFERENCE |
| Sensitive session storage | Paymob CommerceHub protects sessions; Stripe CommerceHub does not | P0 POSSIBLE BUG, unresolved |
| Operational health | Paymob resolves actual tenant candidates; Stripe checks ciphertext presence | LEGACY DIFFERENCE / P2 |
| Super Admin | Paymob registered; Stripe absent | LEGACY DIFFERENCE / P2 |
| Inventory/finality | Both depend on canonical lifecycle authority | CORRECT SHARED BEHAVIOR |

## 16. First Real Failure

`StorefrontPaymentRuntimeReadinessService.EvaluateStripeAsync` evaluated `StripeOptionsValidator.IsRuntimeEnabled(options)` as false and returned `STRIPE_RUNTIME_DISABLED` at `StorefrontPaymentRuntimeReadinessService.cs:172-176`.

This occurred after SQL presence, enabled/deleted, configuration-valid, supported-code, and online-capability gates had passed.

## 17. Root Cause

Development activated Paymob tenant runtime but never activated Stripe tenant runtime. Stripe tenant execution and global CommerceHub configuration shared one options object. The validator treated either global `Enabled` or tenant `FeatureFlags.Enabled` as requiring global credentials, making correct tenant-only activation impossible without duplicating tenant secrets into global configuration.

Admin did not call runtime readiness and validated only tenant field presence, so it truthfully showed the tenant row as configured and enabled while Storefront correctly failed closed on deployment runtime readiness.

Exact answer: **Admin said Valid + Enabled because the encrypted tenant row decrypted and contained the required fields. Storefront excluded Stripe because the deployment's Stripe tenant runtime feature was disabled; the first failed gate was `STRIPE_RUNTIME_DISABLED`.**

## 18. Secondary Findings

| Severity | Finding | Disposition |
|---|---|---|
| P0 | CommerceHub Stripe client secret can be persisted unprotected in token/raw response | Unresolved; blocks production certification |
| P1 | Stripe failed-confirmation webhook terminalized order/payment and released inventory | Remediated to retryable pending evidence |
| P1 | Adapter/consumer metadata key mismatch broke webhook metadata fallback | Remediated with dual producer/consumer contract |
| P1 | Expired local Stripe handoff can latch to same pending intent | Unresolved |
| P1 | Ambiguous creation failure lacks provider retrieval reconciliation | Unresolved |
| P1 | POS cancel/refund flows do not execute tenant-scoped provider operations | Unresolved |
| P2 | Admin validity previously accepted malformed/mixed-mode Stripe keys | Remediated |
| P2 | Stripe health check can report false readiness | Unresolved |
| P2 | Stripe absent from Super Admin provider administration | Unresolved |
| P2 | One deployment cannot host mixed test/live Stripe tenants | Current architectural limitation |
| P2 | Multi-currency report exceeds mapper behavior | Unresolved |
| P3 | Configured API version is validated but not explicitly applied to clients | Unresolved |
| P3 | Staging environment template omits Stripe variable documentation | Unresolved |

## 19. Remediation

Implemented changes:

- separated global credential requirement from tenant feature activation;
- enabled Development tenant Stripe sandbox PaymentIntent and webhook capabilities only;
- retained global CommerceHub Stripe disabled with empty committed credentials;
- strengthened shared Stripe prefix/environment validation;
- aligned Admin selected sandbox/live validation;
- emitted and consumed stable webhook correlation metadata, with compatibility for existing provider intents;
- changed `payment_intent.payment_failed` from terminal failure to retryable attempt evidence;
- added focused availability, validation, metadata, and lifecycle regression tests;
- preserved Paymob, COD, and Manual Wallet behavior.

No schema change or migration was required.

## 20. Tests

Added/updated backend regression coverage for:

- configured and ready Stripe included with COD, Paymob, and Manual Wallet unchanged;
- runtime-disabled, wrong-mode/capability, and unresolved Stripe excluded;
- invalid and mixed-mode Stripe keys rejected;
- tenant feature activation does not require duplicated global credentials;
- provider metadata carries webhook correlation keys;
- failed Stripe confirmation is nonterminal/retryable;
- stale architecture assertions match HttpOnly checkout-token authority.

Executed frontend payment tests:

- Storefront payment authority: 78 passed, 0 failed across 3 files.
- POS Admin PaymentMethods: 4 passed, 0 failed.

Backend focused execution is blocked before test discovery by the pre-existing untracked `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:798` compile error: `AccountingOutboxMessage` has no `Payload` member. This file was not modified because it is unrelated concurrent work.

The required Stripe, payment availability, webhook, lifecycle, checkout, and Admin backend test groups therefore cannot honestly be reported as executed after this remediation.

## 21. Build Verification

| Gate | Result |
|---|---|
| API build | PASS, 0 warnings, 0 errors |
| Storefront production build | PASS; existing bundle/import advisories only |
| POS Admin production build | PASS |
| Storefront focused payment tests | PASS, 78/78 |
| POS Admin PaymentMethods tests | PASS, 4/4 |
| Solution build | BLOCKED by unrelated untracked Paymob test compile error |
| Backend focused Stripe tests | BLOCKED by same compile error |
| EF pending model check | PASS, no pending model changes |
| `git diff --check` | PASS; line-ending notices only |

The API build proves the remediated production source compiles. The solution failure is not introduced by a changed Stripe source or test; it originates in an unrelated untracked Paymob simulation file.

## 22. Runtime Verification

Post-remediation real endpoint result:

| Evidence | Result |
|---|---|
| HTTP status | 200 |
| Response status | `AVAILABLE` |
| Configured count | 4 |
| Enabled count | 4 |
| Available count | 4 |
| Returned codes | `COD`, `STRIPE`, `PAYMOB`, `MANUALWALLET` |
| Stripe display | `ST Credit/Debit Card` |
| Stripe available | YES |

This proves tenant configuration decryptability, test-mode consistency, safe prefixes, PaymentIntent readiness, webhook readiness, and preserved neighboring payment methods.

No browser selection/order creation, Stripe Payment Element confirmation, test card payment, Stripe CLI forwarding, provider webhook, payment finality, inventory finality, or POS Admin order-state verification was performed.

## 23. Remaining External Limitations

- No real Stripe sandbox payment was made.
- No Stripe CLI listener was attached to `http://localhost:5010/api/webhooks/stripe`.
- No correctly correlated success/failure/cancel/refund provider event was received.
- No live Stripe account/network credential test was performed.
- Backend test execution is blocked by unrelated working-tree compilation.
- Production/live mode was intentionally not enabled or changed.

Required final drill:

```powershell
stripe listen --events payment_intent.succeeded,payment_intent.payment_failed,payment_intent.canceled,charge.refunded --forward-to http://localhost:5010/api/webhooks/stripe
```

The drill must use an actual Shopixy-created order and PaymentIntent; arbitrary fixtures do not prove tenant/order/payment correlation.

## 24. Risk Assessment

The P1 Storefront availability defect is fixed and runtime-proven. Stripe can now be selected only when backend tenant/runtime authorization passes. Retryable decline and metadata correlation defects were also corrected.

Residual risk remains unacceptable for production-wide Stripe certification because CommerceHub Stripe sensitive session storage is not protected, external cancel/refund authority is incomplete, stale-session and ambiguous-creation recovery are unresolved, and no real provider finality drill was completed.

## 25. Final Certification

**BLOCKED**

The primary question is conclusively answered and the Storefront visibility remediation is runtime-proven. Full Stripe certification is blocked by unexecuted external sandbox finality, a pre-existing backend test compilation blocker, and unresolved P0/P1 findings listed above. This result must not be interpreted as production Stripe PASS.
