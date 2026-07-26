# PAYMENT-0.5A Stripe Official API Discovery

Date: 2026-07-10
Scope: Official Stripe platform vs current project Stripe implementation vs PAYMENT-0 architecture
Status: DISCOVERY COMPLETE - NO RUNTIME CHANGE

## Executive Summary

Stripe was reviewed against the certified PAYMENT-0 provider-neutral architecture and the current PointOfSale Stripe runtime. The current implementation remains architecture-compatible because it uses Stripe as a backend-owned provider adapter/evidence source, not as payment authority. Stripe PaymentIntents, Stripe.js Payment Element, webhook signature verification, provider event idempotency, encrypted provider secrets, and backend payment lifecycle admission are consistent with PAYMENT-0.

Certification decision: **COMPATIBLE WITH MIGRATION OPPORTUNITIES**.

No runtime, backend, frontend, API, migration, refactor, or configuration change was applied.

Primary findings:

- Current Stripe integration uses Payment Intents + Payment Element, not Hosted Checkout, Embedded Checkout, Checkout Sessions, Customer Sessions, Customer Portal, or native Stripe subscription runtime.
- No direct Stripe Charges API usage was found.
- Stripe.net is present at `50.1.0`; official Stripe versioning docs now report current API version `2026-06-24.dahlia`.
- Stripe API version pinning is not visible in the inspected integration. Requests and webhooks therefore depend on account/SDK/webhook endpoint version behavior.
- Stripe docs now recommend restricted API keys for most use cases; current configuration still names and stores a tenant `secretKey`.
- Webhook handling is broadly compatible: raw body signature verification, tenant resolution from payment evidence, SQL duplicate collapse, serializable processing, and failure retry semantics are present.
- Webhook event coverage is narrow but compatible for the implemented flow: `payment_intent.succeeded`, `payment_intent.payment_failed`, `payment_intent.canceled`, and `charge.refunded`.
- Refund support is webhook reconciliation/local lifecycle recording, not a Stripe Refund API execution adapter.
- Capture/cancel are modeled by platform lifecycle concepts, but no Stripe capture/cancel API execution path was found for admin/runtime action.

## Current Implementation

Backend Stripe runtime:

- `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`
- `PointOfSale.API/Services/Payments/Stripe/IStripePaymentService.cs`
- `PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs`
- `PointOfSale.API/Controllers/StripeWebhooksController.cs`

Frontend Stripe runtime:

- `PointOfSale.Storefront/src/components/checkout/StripePaymentForm.tsx`
- `PointOfSale.CommerceHub/src/payments/EmbeddedStripePaymentForm.tsx`

Durable payment authority:

- `PointOfSale.Core/Entities/PaymentIntent.cs`
- `PointOfSale.Core/Entities/TenantPaymentMethod.cs`
- `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`

SDK and packages:

- Backend: `Stripe.net` version `50.1.0`
- Storefront: `@stripe/react-stripe-js` `^5.4.1`, `@stripe/stripe-js` `^8.6.1`
- CommerceHub: loads Stripe.js directly from `https://js.stripe.com/v3/`

Implemented Stripe flow:

```text
Existing backend order
  -> StripePaymentService validates tenant/order/customer/checkout token/currency/inventory
  -> tenant Stripe configuration is decrypted from TenantPaymentMethod.ConfigEncrypted
  -> Stripe PaymentIntent is created with automatic_payment_methods enabled
  -> internal PaymentIntent persists provider id, encrypted client_secret, idempotency key, metadata
  -> frontend renders Payment Element with client_secret and publishable key
  -> frontend calls confirmPayment but treats client-side result as pending evidence only
  -> Stripe webhook signature is verified
  -> Stripe event is admitted into SQL PaymentWebhookLog/idempotency
  -> IPaymentLifecycleService owns final status mutation
```

## Official Stripe APIs Reviewed

Official Stripe documentation reviewed:

- API Reference: `https://docs.stripe.com/api`
- API versioning: `https://docs.stripe.com/api/versioning`
- PaymentIntents: `https://docs.stripe.com/api/payment_intents`
- Checkout Sessions: `https://docs.stripe.com/api/checkout/sessions`
- Stripe Checkout / hosted and embedded UIs: `https://docs.stripe.com/payments/checkout`
- Payment Element / accept a payment variants: `https://docs.stripe.com/payments/accept-a-payment?platform=web&ui=elements`
- Webhooks: `https://docs.stripe.com/webhooks`
- Webhook signatures: `https://docs.stripe.com/webhooks/signature`
- Idempotent requests: `https://docs.stripe.com/api/idempotent_requests`
- API keys: `https://docs.stripe.com/keys`
- Customer Sessions: `https://docs.stripe.com/api/customer_sessions`
- Refunds: `https://docs.stripe.com/api/refunds`
- PaymentIntent capture: `https://docs.stripe.com/api/payment_intents/capture`
- PaymentIntent cancel: `https://docs.stripe.com/api/payment_intents/cancel`

Key official Stripe facts used for this discovery:

- Stripe API is REST-based, uses API keys for authentication, and the API key determines live vs sandbox/test execution.
- Current Stripe API version listed by Stripe docs is `2026-06-24.dahlia`.
- Major API releases can include backward-incompatible changes; monthly releases under the same major line are backward-compatible.
- Webhook events use the account default API version unless the webhook endpoint has an explicit API version.
- Stripe.net requests align with the API version current at the Stripe.net version release; upgrading/downgrading the SDK is the supported version control path for strongly typed .NET usage.
- PaymentIntents are the recommended object for collecting a payment, with one PaymentIntent per order/customer session.
- Checkout Sessions support hosted Checkout, embedded Checkout, payment links, expiration, and async payment events.
- Payment Element and Checkout can support dynamic payment methods, Link, wallets, saved payment methods, and local payment methods depending on account, country, currency, configuration, and integration mode.
- All Stripe POST requests accept idempotency keys.
- Webhook endpoints must verify the raw body and Stripe signature, handle retries/duplicates, and use HTTPS outside local development.
- Stripe recommends restricted API keys for most use cases; secret keys remain supported but are unrestricted and not recommended for new use cases.

## Architecture Compatibility

| Area | Compatibility | Notes |
|---|---:|---|
| PAYMENT-0 provider neutrality | Compatible | Stripe is isolated behind backend payment services and lifecycle admission. |
| Backend payment authority | Compatible | Frontend Stripe result is pending evidence; lifecycle service owns final mutation. |
| PaymentIntents | Compatible | Uses `PaymentIntentService.CreateAsync` with `automatic_payment_methods.enabled = true`. |
| Payment Element | Compatible | Storefront uses `@stripe/react-stripe-js`; CommerceHub uses direct Stripe.js. |
| Checkout Sessions | Not implemented | Compatible as future adapter/session mode, not current runtime. |
| Hosted Checkout | Not implemented | No conflict; would require new Stripe session handoff contract. |
| Embedded Checkout | Not implemented | No conflict; would require Checkout Sessions/embedded form runtime. |
| Customer Sessions | Not implemented | Migration opportunity for saved-card/customer frontend access. |
| Customer Portal | Not implemented | No current recurring/subscription portal integration. |
| Refund API execution | Not implemented | Local lifecycle records/refund webhook reconciliation exist; Stripe refund creation adapter not found. |
| Capture API execution | Not implemented | Platform can model authorized/captured states; Stripe manual capture execution not found. |
| Cancel API execution | Not implemented | Webhook canceled handling exists; outbound Stripe cancel/Checkout expire path not found. |
| Webhooks | Compatible | Signature verification, SQL log, duplicate handling, retry-on-500 semantics present. |
| Idempotency | Compatible | Stripe create uses `RequestOptions.IdempotencyKey`; platform also has SQL/Redis idempotency. |
| Secrets | Mostly compatible | Encrypted tenant config; recommend restricted key migration and explicit key-mode validation. |
| API versioning | Needs hardening | No explicit API/webhook version governance found in project configuration. |

## Capability Matrix

| Capability | Stripe Official Support | Current Project | Recommendation |
|---|---:|---:|---|
| Supports Redirect | Yes via Checkout/redirect flows | Partial, return URL used by Payment Element | Keep as evidence-only; add hosted Checkout only in later phase if desired. |
| Supports Embedded Checkout | Yes via Checkout Sessions UI variants | No | Future adapter option. |
| Supports Payment Element | Yes | Yes | Keep current approach. |
| Supports Saved Cards | Yes via Customer/Setup/Customer Sessions | No first-class support found | Future saved-card phase. |
| Supports Refund | Yes | Local lifecycle + webhook reconciliation; no outbound Stripe refund create found | Add provider refund adapter in later phase. |
| Supports Partial Refund | Yes | Local lifecycle supports partial refund states | Add Stripe Refund API execution if gateway refunds must be initiated by platform. |
| Supports Capture | Yes for `requires_capture` PaymentIntents | No outbound Stripe capture path found | Future manual capture phase. |
| Supports Cancel | Yes for cancelable PaymentIntents; Checkout Sessions can be expired | Webhook canceled handling only | Future cancellation adapter. |
| Supports Recurring | Yes via Billing/Subscriptions/Checkout | Not implemented | Future recurring readiness phase. |
| Supports Apple Pay | Yes through compatible Stripe flows | Not explicitly surfaced | Enable through Stripe dashboard/payment method policy after operational review. |
| Supports Google Pay | Yes through compatible Stripe flows | Not explicitly surfaced | Same as Apple Pay. |
| Supports Link | Yes | Possible through automatic payment methods; Storefront `paymentMethodOrder: ['card']` may limit visibility | Review frontend PaymentElement ordering. |
| Supports Webhooks | Yes | Yes | Expand event coverage/version governance. |
| Supports Manual Review | Yes through Radar/review signals | No Stripe review event handling found | Optional risk phase. |
| Supports Async Confirmation | Yes | Limited pending model exists; async Checkout events not used | Add async event coverage if enabling delayed methods. |
| Supports Multi-Currency | Yes | Tenant/order currency locked; Stripe amount conversion supports currency decimals | Compatible; operationally verify supported methods per currency/country. |
| Supports Metadata | Yes | Yes: order/tenant/customer/payment reference metadata | Compatible. |
| Supports Idempotency | Yes | Yes | Compatible. |

## Current Gaps

1. API version governance is not explicit.
   - No inspected code pins or records a Stripe API version for tenant configuration, outbound requests, or webhook endpoints.
   - Stripe docs warn major API releases can be breaking and webhook endpoint versions may differ from SDK/account defaults.

2. Restricted API key readiness is incomplete.
   - Project configuration requires `secretKey`, while Stripe now recommends restricted API keys for most use cases.
   - The config model can likely store an `rk_...` value under `secretKey`, but naming/validation does not distinguish `sk_` from `rk_`.

3. Stripe lifecycle execution is narrower than PAYMENT-0 conceptual lifecycle.
   - Capture, cancel, expire, refund creation, disputes, and settlement are not implemented as Stripe API operations.
   - Current runtime mainly creates PaymentIntents and admits webhooks.

4. Modern Checkout capabilities are not used.
   - Hosted Checkout, Embedded Checkout, Checkout Sessions, Customer Sessions, Checkout dynamic updates, and Customer Portal are not present.
   - This is not a compatibility failure because PAYMENT-0 is provider-neutral and current Payment Element flow remains valid.

5. Saved cards and customer-scoped Stripe frontend access are not first-class.
   - Current PaymentIntent metadata carries customer id, but no Stripe Customer/Customer Session/SetupFutureUsage flow was found.

6. Webhook event model is v1 snapshot-oriented and narrow.
   - Current code uses `EventUtility.ConstructEvent` and typed `Stripe.Event` data objects.
   - Stripe docs now distinguish snapshot and thin event models. Current use remains compatible for v1 resources, but future API v2/event destination work should be explicit.

7. Storefront Payment Element ordering may limit dynamic method exposure.
   - Storefront sets `paymentMethodOrder: ['card']`; this may reduce or hide automatic payment method benefits such as Link/local methods depending on Stripe.js behavior and dashboard settings.

8. Stale comments remain in Stripe/payment service surfaces.
   - Several comments still state "webhooks are the ONLY source of truth." PAYMENT-0 certified wording is more precise: backend Payment lifecycle is authority; webhooks are provider evidence admitted by Payment.

## Deprecated Features

No direct use of deprecated Stripe Charges API was found.

Potentially stale or migration-sensitive areas:

- Secret-key-first configuration terminology is misaligned with Stripe's restricted-key recommendation.
- Webhook comments and flow language should be updated in a later documentation-only or source-comment cleanup phase to match PAYMENT-0.
- Direct `<script src="https://js.stripe.com/v3/">` loading in CommerceHub is still a valid Stripe.js usage pattern, but Storefront's package-based integration is easier to keep typed/tested.

No confirmed deprecated Stripe SDK version was identified from official docs during this discovery. Stripe.net `50.1.0` should be compared against the Stripe.net changelog in an operational upgrade phase to determine its pinned API version and upgrade path.

## Migration Opportunities

Recommended future phases, in priority order:

1. **PAYMENT-0.5B Stripe Version and Key Governance**
   - Record intended Stripe API version.
   - Record webhook endpoint API version.
   - Validate sandbox/live key pairing.
   - Support restricted API keys explicitly.
   - Add operational key rotation checklist.

2. **PAYMENT-0.6 Stripe Lifecycle Adapter Expansion**
   - Add provider-neutral outbound operations for Stripe refund creation, partial refund, capture, cancel, and Checkout Session expiration if product requirements need them.
   - Keep all operations behind `IPaymentLifecycleService`/provider-neutral contracts.

3. **PAYMENT-0.7 Stripe Customer and Saved Payment Methods**
   - Introduce Stripe Customer mapping, Customer Sessions, and saved-card policy if recurring or one-click checkout is required.
   - Keep saved payment method ownership backend-bound.

4. **PAYMENT-0.8 Stripe Checkout Modes Evaluation**
   - Decide whether Hosted Checkout or Embedded Checkout should coexist with Payment Element.
   - Treat Checkout Session ids/URLs as transient payment-session evidence only.

5. **PAYMENT-0.9 Stripe Webhook Coverage and Replay Drills**
   - Add explicit handling policy for `payment_intent.processing`, `payment_intent.requires_action`, `payment_intent.amount_capturable_updated`, `checkout.session.*` if Checkout Sessions are adopted, `refund.*`, `charge.dispute.*`, and Radar/manual review events as needed.

## Breaking Changes

No immediate breaking change was found in the current implementation.

Breaking-change exposure exists because:

- API version is not explicitly governed in project configuration.
- Stripe.net strongly typed request/response behavior depends on the SDK version.
- Webhook endpoint API version may differ from outbound request/API account version.
- Future enabling of async/local payment methods can introduce delayed success/failure events that current UX/lifecycle policies must deliberately handle.

## Security Review

Compatible controls found:

- Tenant-scoped payment configuration is stored in `TenantPaymentMethod.ConfigEncrypted`.
- Stripe secrets are decrypted only server-side.
- Client secret is encrypted at rest in `PaymentIntent.ClientSecretEncrypted`.
- Storefront receives only publishable key and PaymentIntent client secret.
- Webhook endpoint uses raw body processing pipeline, request size limit, rate limiting, signature verification, and tenant resolution from payment evidence.
- Webhook duplicate handling uses SQL `PaymentWebhookLog` and Redis best-effort caching.
- Frontend does not treat Stripe.js success as final order/payment authority.

Security gaps/recommendations:

- Add explicit validation that sandbox mode uses `pk_test_` plus `sk_test_` or `rk_test_`, and live mode uses `pk_live_` plus `sk_live_` or `rk_live_`.
- Prefer restricted API keys for runtime operations and reserve unrestricted secret keys for exceptional/admin use.
- Track webhook endpoint secret per environment and Stripe account; never reuse local CLI `whsec_...` in production.
- Record and monitor Stripe webhook endpoint API version.
- Add key rotation and dual-key rollout runbook.
- Avoid putting personal data in Stripe idempotency keys; current deterministic keys use tenant/order identifiers, which is acceptable but should remain non-PII.

## Runtime Impact

Runtime impact of this phase: **None**.

Recommended changes are future-only. If implemented later, expected impact is:

- API version/key governance: configuration and validation impact only.
- Refund/capture/cancel execution: new provider adapter operations and lifecycle tests.
- Customer Sessions/saved cards: new customer identity mapping, privacy, and PCI-adjacent frontend access controls.
- Hosted/Embedded Checkout: new payment session handoff contract and webhook event coverage.

## Compatibility Score

Overall compatibility score: **82 / 100**.

Score rationale:

- +25 provider-neutral architecture preserved.
- +20 PaymentIntents and Payment Element are modern and supported.
- +15 webhook signature/idempotency/lifecycle admission are strong.
- +10 encrypted secrets/client secret handling are compatible.
- +7 automatic payment methods and metadata are present.
- -8 API/webhook version governance not explicit.
- -5 restricted key migration not explicit.
- -5 lifecycle API execution gaps for refund/capture/cancel.
- -3 saved-card/Customer Session gap.
- -2 stale source comments and narrow event coverage.

## Recommendations

Do not change runtime in PAYMENT-0.5A.

Recommended next actions:

1. Create PAYMENT-0.5B for Stripe API version, webhook endpoint version, restricted-key, and environment-separation governance.
2. Add source-level architecture tests or documentation checks that forbid Charges API usage and require PaymentIntents/Payment lifecycle authority.
3. Plan provider-neutral Stripe lifecycle execution only if business flows require platform-initiated refunds, partial refunds, captures, or cancellations.
4. Evaluate whether current Payment Element is the desired long-term UX, or whether Hosted Checkout/Embedded Checkout should be introduced as optional provider capabilities.
5. Add Stripe operational readiness drills: webhook replay, duplicate event, invalid signature, provider timeout, delayed payment method, refund webhook, key rotation, sandbox/live mismatch, and API version upgrade rehearsal.
6. Update stale comments in a later documentation/source-comment-only phase so they align with PAYMENT-0: webhooks are provider evidence, while Payment lifecycle is final authority.

## Certification Decision

Architecture still compatible: **Yes**.

PAYMENT-0 provider-neutral design preserved: **Yes**.

Enterprise Payment Runtime alignment: **Yes, for current PaymentIntent/Payment Element flow**.

Modern Stripe APIs alignment: **Mostly yes; version/key governance and newer Checkout/Customer Session capabilities are migration opportunities**.

Future Payment Runtime design compatibility: **Yes, if new Stripe capabilities enter through provider-neutral lifecycle/session contracts**.

Certification result: **COMPATIBLE WITH MIGRATION OPPORTUNITIES**.

Immediate runtime update required: **No**.

Operational rollout update recommended: **Yes**.

## Explicit Success Answers

- Reviewed latest official Stripe APIs? **Yes. Official Stripe docs reviewed on 2026-07-10; current API version listed by Stripe docs is `2026-06-24.dahlia`.**
- Reviewed current implementation? **Yes. Stripe services, controllers, frontend forms, DTO/entity/configuration surfaces, lifecycle/webhook paths, SDK/package references, and frozen architecture docs were reviewed.**
- Compared both? **Yes.**
- Architecture still compatible? **Yes.**
- Breaking changes found? **No immediate breaking change found; API/webhook version governance is a future breaking-change risk.**
- Deprecated APIs found? **No direct deprecated Charges API usage found.**
- Security issues found? **No immediate critical runtime security issue found; restricted key migration, environment key validation, API/webhook version recording, and key rotation governance are recommended.**
- Migration recommended? **Yes.**
- Runtime update required? **No immediate runtime update required.**
- Immediate action required? **No immediate code action required.**
- Recommended next phase? **PAYMENT-0.5B Stripe Version, Key, Webhook, and Environment Governance Certification.**
