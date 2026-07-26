# PAYMENT-0.5B Paymob Official API Discovery

Date: 2026-07-10
Scope: Official Paymob platform vs current project Paymob implementation vs PAYMENT-0 architecture
Status: DISCOVERY COMPLETE - NO RUNTIME CHANGE

## Executive Summary

Paymob was reviewed against the certified PAYMENT-0 provider-neutral payment architecture and the current PointOfSale Paymob runtime. The current implementation remains architecture-compatible because Paymob is used as backend-owned provider evidence, not as payment authority. Backend Payment owns tenant configuration resolution, payment-session creation, provider handoff evidence, callback/webhook verification, lifecycle admission, idempotency, persistence, recovery, and audit.

Certification decision: **COMPATIBLE WITH REQUIRED MIGRATION PLANNING**.

No runtime, backend, frontend, API, migration, refactor, or configuration change was applied.

Primary findings:

- Official Paymob docs now center the API flow on the **Intention Creation API** and checkout experiences: **Unified Checkout (redirect)** and **Pixel (embedded)**.
- Current storefront and CommerceHub Paymob implementation use the older Paymob Accept flow: `auth/tokens` -> `ecommerce/orders` -> `acceptance/payment_keys` -> `/acceptance/iframes/{iframeId}?payment_token=...`.
- Official Paymob FAQ says new merchants are encouraged to use Unified Checkout, and new IFrame setup requires Paymob team/account-manager handling.
- Current implementation is still compatible with PAYMENT-0 because the provider-specific iframe/payment key is admitted as backend-issued session evidence and final status is backend lifecycle owned.
- Current project supports cards through Paymob iframe/payment key handoff, callback validation, webhook HMAC validation, payment status polling, webhook duplicate collapse, and CommerceHub Paymob refund execution/status refresh.
- Current project does not implement Paymob Intention API, Unified Checkout, Pixel embedded checkout, public-key/client-secret flow, Wallet/Meeza/Kiosk/QR-specific adapter branches, saved-card tokenization, subscriptions, outbound capture, outbound void, or explicit Paymob idempotency headers.
- Security alignment is strong for current flow: encrypted tenant Paymob config, backend-only secrets, HMAC verification, SQL webhook logs, replay collapse, tenant resolution, and frontend pending-only behavior are present.
- Migration is recommended, but no immediate runtime-breaking change was found.

## Current Implementation

Backend storefront Paymob runtime:

- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/IPaymobPaymentService.cs`
- `PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs`
- `PointOfSale.API/Controllers/PaymobWebhooksController.cs`

CommerceHub Paymob runtime:

- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubRefundGatewayService.cs`
- `PointOfSale.API/Configuration/CommerceHubPaymobOptions.cs`

Frontend Paymob runtime:

- `PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx`

Durable payment authority:

- `PointOfSale.Core/Entities/PaymentIntent.cs`
- `PointOfSale.Core/Entities/TenantPaymentMethod.cs`
- `PointOfSale.Core/Entities/PaymentWebhookLog.cs`
- `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs`

Implemented storefront flow:

```text
Existing backend order
  -> PaymobPaymentService validates tenant/order/customer/session/currency/payment method
  -> tenant Paymob config is decrypted from TenantPaymentMethod.ConfigEncrypted
  -> backend authenticates with Paymob API key
  -> backend creates Paymob ecommerce order
  -> backend creates Paymob payment key
  -> backend persists SQL PaymentIntent with provider id, payment key, iframe URL, expiry, and routing metadata
  -> storefront receives admitted iframe URL/payment key as handoff evidence
  -> storefront redirects to backend-issued Paymob iframe URL
  -> Paymob callback redirects user only to pending route
  -> Paymob webhook HMAC is verified
  -> SQL webhook/idempotency admission occurs
  -> IPaymentLifecycleService owns final status mutation
```

Current Paymob endpoint constants found in code:

| Current code endpoint | Purpose |
|---|---|
| `https://accept.paymob.com/api/` | Egypt Paymob Accept API base URL |
| `auth/tokens` | API-key authentication token |
| `ecommerce/orders` | Legacy Paymob order creation |
| `acceptance/payment_keys` | Legacy payment key creation |
| `https://accept.paymob.com/api/acceptance/iframes/{0}?payment_token={1}` | IFrame handoff URL |
| `acceptance/void_refund/refund` | CommerceHub Paymob refund execution |
| `acceptance/transactions/{0}` | CommerceHub Paymob transaction/refund status refresh |

Current configuration shape:

| Surface | Current fields |
|---|---|
| Storefront tenant method | encrypted Paymob config under `TenantPaymentMethod.ConfigEncrypted` |
| CommerceHub options | `Enabled`, `ApiKey`, `IntegrationId`, `IframeId`, `HmacSecret` |

## Official Paymob APIs Reviewed

Official Paymob documentation reviewed:

- Paymob Developer Portal: `https://developers.paymob.com/`
- Paymob docs hub via Theneo: `https://app.theneo.io/paymob-solutions-s-a-e/docs`
- Paymob docs JSON: `https://api.theneo.io/documents-hub/workspace/paymob-solutions-s-a-e`
- LLM-optimized official docs index: `https://developers.paymob.com/paymob-docs/getting-started/overview/llms.txt`
- Overview: `https://developers.paymob.com/paymob-docs/getting-started/overview.md`
- API integration path: `https://developers.paymob.com/paymob-docs/integration-paths/apis.md`
- Integration credentials: `https://developers.paymob.com/paymob-docs/need-help/faq/getting-integration-credentials.md`
- Test credentials: `https://developers.paymob.com/paymob-docs/need-help/faq/test-credentials.md`
- Common issues and inquiries: `https://developers.paymob.com/paymob-docs/need-help/faq/common-issues-and-inquires.md`
- Glossary: `https://developers.paymob.com/paymob-docs/need-help/glossary.md`

Key official Paymob facts used for this discovery:

- Paymob docs were current in the reviewed pages with Last Updated dates of June 1, 2026 for overview/API/credentials/test pages and June 16, 2026 for glossary.
- Paymob supports cards, mobile wallets, quick payments such as Apple Pay and Google Pay, BNPL/installments, and in-person Tap to Pay, subject to merchant-account enablement.
- API integrations now start by creating a payment intention from the backend.
- Intention response includes a client secret/reference used to launch checkout.
- Checkout options include Unified Checkout redirect and Pixel embedded checkout.
- Paymob states callbacks/webhooks should confirm final payment status and redirects are mainly user experience.
- Paymob supports HMAC verification for callbacks.
- Required API/SDK credentials include API Secret Key, Public Key, Integration IDs, webhook endpoint, and success/failure redirect URLs.
- Secret Key and Public Key are mode-specific; the older API key is documented as the same key for test and live.
- Test and live use the same regional base URL for a region; mode is controlled by keys and integration IDs.
- Regional base URLs include Egypt `https://accept.paymob.com/`, Oman, KSA, and UAE hosts.
- Dashboard/new dashboard transaction actions include refund, void, and capture where supported by status/type.
- Paymob transaction inquiry can use Transaction ID, Order ID, or Special Reference Number.
- Paymob FAQ recommends Unified Checkout as the latest checkout experience for new merchants; new IFrame setup for existing merchants requires Paymob team handling.
- Paymob supports custom callback URL override in intention creation with `notification_url` for server-to-server processed callbacks and `redirection_url` for transaction response redirects. The FAQ notes redirection URL support for card and wallet methods.
- Paymob glossary defines saved-card tokens, tokenization, CIT/MIT, subscriptions, auth/capture, refund, void, settlement/payout, and webhook retry/idempotency expectations.

## Architecture Compatibility

| Area | Compatibility | Notes |
|---|---:|---|
| PAYMENT-0 provider neutrality | Compatible | Paymob remains behind backend payment services and provider-specific code paths. |
| Backend payment authority | Compatible | Storefront redirect/callback values are evidence only; backend status polling and lifecycle own final state. |
| SQL payment/session authority | Compatible | `PaymentIntent` is the durable payment session/attempt authority. |
| Provider secret ownership | Compatible | Secrets are backend-held and encrypted in tenant config; CommerceHub config is server-side options. |
| Current Paymob legacy Accept flow | Compatible but migration-sensitive | Auth/order/payment-key/iframe still works architecturally but is no longer the preferred official integration path. |
| Intention API | Not implemented | Required for alignment with latest Paymob API direction. |
| Unified Checkout | Not implemented | Recommended migration path for hosted checkout. |
| Pixel embedded checkout | Not implemented | Future adapter/session mode if embedded checkout is required. |
| IFrame | Implemented, deprecated for new setup | Official FAQ recommends Unified Checkout and says new IFrame setup requires Paymob team handling. |
| Callback/webhook authority | Compatible | HMAC validation, tenant resolution, SQL logs, duplicate handling, and lifecycle admission are present. |
| Refund execution | Partial | CommerceHub has Paymob refund execution/status refresh; storefront payment runtime mainly reconciles provider refund webhook/lifecycle. |
| Capture execution | Not implemented | Status fields are parsed in CommerceHub webhook HMAC payload, but outbound capture API path was not found. |
| Void execution | Not implemented | Void status/evidence is parsed; outbound void API path was not found. |
| Transaction inquiry | Partial | CommerceHub refund status refresh uses transaction retrieval by id. |
| Idempotency | Platform-compatible, provider gap | SQL/request/provider-event idempotency exists; Paymob-specific outbound idempotency header/field was not found. |
| Environment separation | Mostly compatible | Config separates tenant/CommerceHub credentials, but current code hardcodes Egypt Accept base URL. |

## Capability Matrix

| Capability | Official Paymob Support | Current Project | Recommendation |
|---|---:|---:|---|
| Supports Redirect | Yes, Unified Checkout | Yes, via iframe URL redirect | Migrate to Unified Checkout in a later phase. |
| Supports IFrame | Legacy/existing merchant support | Yes | Keep stable short term; plan deprecation exit. |
| Supports Embedded Checkout | Yes, Pixel | No | Future adapter option. |
| Supports Cards | Yes | Yes, via payment key/iframe | Compatible. |
| Supports Bank Cards | Yes | Yes, card-oriented flow | Compatible. |
| Supports Wallets | Yes | Not first-class in adapter | Add only if product requires wallet-specific handling. |
| Supports Meeza | Regional/account-dependent; not separately confirmed in current docs sample | No explicit branch | Treat as account/payment-method enablement; verify with Paymob account manager before implementation. |
| Supports Kiosk | Dashboard docs mention test integrations for Kiosk payments | No explicit branch | Future method-specific adapter if required. |
| Supports QR | Not confirmed in reviewed official pages | No | Do not certify until official account/API docs are obtained. |
| Supports Apple Pay | Yes | No explicit support | Future wallet/quick-payment phase. |
| Supports Google Pay | Yes | No explicit support | Future wallet/quick-payment phase. |
| Supports Installments/BNPL | Yes | No first-class Paymob branch | Future method capability phase. |
| Supports Refund | Yes | Partial: CommerceHub outbound refund and webhook reconciliation | Expand provider-neutral refund adapter if needed beyond CommerceHub. |
| Supports Partial Refund | Official lifecycle supports refunds; restrictions vary by method | Partial platform lifecycle support | Verify per Paymob method before enabling. |
| Supports Capture | Yes, dashboard/API action where eligible | No outbound capture path found | Future auth/capture phase. |
| Supports Void | Yes, dashboard/API action where eligible | No outbound void path found | Future auth/capture/void phase. |
| Supports Saved Cards | Yes, saved-card tokens | No first-class support | Future tokenization phase. |
| Supports Recurring | Yes, subscriptions/CIT/MIT | No | Future recurring readiness phase. |
| Supports Tokenization | Yes | No first-class support | Future saved-card/token phase. |
| Supports Webhooks | Yes | Yes | Compatible. |
| Supports HMAC | Yes | Yes | Compatible. |
| Supports Async Payments | Yes by webhook/pending model | Partially ready | Current pending model is compatible; add method-specific statuses as methods expand. |
| Supports Metadata | Intention/reference fields support merchant references | Partial via provider/order metadata | Preserve backend routing metadata in migration. |
| Supports Idempotency | Official glossary expects retry-safe idempotent webhooks | Platform yes; outbound Paymob unknown | Add Paymob outbound idempotency review in migration. |

## Current Gaps

1. Latest Paymob API direction is not implemented.
   - Current project uses `auth/tokens`, `ecommerce/orders`, `acceptance/payment_keys`, and iframe URL handoff.
   - Official docs now describe payment initiation through Intention Creation API and checkout launch through Unified Checkout or Pixel.

2. IFrame dependency is migration-sensitive.
   - Official FAQ says Paymob recommends Unified Checkout as the latest checkout experience and new IFrame setup must be handled by Paymob team/account manager.
   - Existing merchants may continue to need IFrame support during migration, but new merchant onboarding should not assume self-service IFrame creation.

3. Secret/public-key model is incomplete for v2 Paymob.
   - Current config centers on API key, integration ID, iframe ID, and HMAC secret.
   - Official docs require API Secret Key and Public Key for API/SDK integrations, plus Integration IDs and callback URLs.

4. Environment governance needs tightening.
   - Official docs say test/live mode is controlled by the keys and integration IDs, while regional base URL stays the same.
   - Current code hardcodes Egypt `accept.paymob.com` base URL in storefront and CommerceHub paths. Regional expansion would need an explicit provider-region base URL policy.

5. Method-specific capability modeling is narrow.
   - Cards are effectively supported through iframe/payment key.
   - Wallets, Meeza, Kiosk, QR, BNPL/installments, Apple Pay, Google Pay, saved cards, and recurring/subscriptions are not exposed as provider capabilities in the current Paymob adapter.

6. Lifecycle execution is narrower than official/architecture capability.
   - Payment lifecycle can represent pending/collected/refunded/voided/failure evidence.
   - Outbound Paymob capture and void APIs were not found.
   - CommerceHub refund execution exists, but storefront/general provider-neutral refund execution should be reviewed separately.

7. Provider outbound idempotency is not explicit.
   - PAYMENT-0 SQL idempotency, active intent uniqueness, and webhook duplicate collapse are strong.
   - No Paymob-specific idempotency key/header/field was found on outbound auth/order/payment-key/refund calls.

8. Deprecated/stale DTO language remains.
   - DTOs still expose `PaymobIframeUrl` and `PaymobPaymentKey`.
   - This is accurate for the current runtime but not aligned with the future Intention/Unified Checkout naming.

## Deprecated Features

Confirmed migration-sensitive feature:

- **IFrame setup for new merchants**: Official Paymob FAQ recommends Unified Checkout and says new IFrame setup requires contacting Paymob. This should be treated as deprecated for new self-service implementation planning, even if still operational for existing configured merchants.

No official page reviewed explicitly stated that `auth/tokens`, `ecommerce/orders`, or `acceptance/payment_keys` are fully retired or unusable. They should be classified as **legacy/currently implemented**, not hard-deprecated, until Paymob publishes an explicit retirement notice for the merchant account/region.

Potentially stale project surfaces:

- `PaymobIframeUrl`
- `PaymobPaymentKey`
- `IframeId`
- legacy Accept base URL constants
- docs/tests expecting payment-key/iframe as the primary Paymob contract

## Migration Opportunities

Recommended future phases:

1. **PAYMENT-0.6 Paymob Intention/Unified Checkout Adapter Design**
   - Add a provider-neutral Paymob session mode for Intention API.
   - Persist Paymob intention id/client secret as provider evidence.
   - Keep backend Payment lifecycle authority unchanged.

2. **PAYMENT-0.7 Paymob Credential and Environment Governance**
   - Model API Secret Key, Public Key, API Key, HMAC Secret, Integration IDs, mode, and region explicitly.
   - Validate test/live key and integration pairing.
   - Add regional base URL configuration.

3. **PAYMENT-0.8 Paymob Checkout Mode Migration**
   - Decide supported modes: legacy iframe, Unified Checkout redirect, Pixel embedded.
   - Provide a dual-run transition plan for existing IFrame merchants.
   - Keep `PaymobIframeUrl` backward-compatible until all existing sessions expire.

4. **PAYMENT-0.9 Paymob Capability Registry**
   - Add Paymob capability flags for cards, wallets, Meeza, kiosk, QR, BNPL/installments, Apple Pay, Google Pay, saved cards, tokenization, recurring, refund, partial refund, capture, void, webhook, HMAC, and async payments.
   - Capabilities should be account/region/method enabled, not globally assumed.

5. **PAYMENT-1.0 Paymob Lifecycle Operations**
   - Add provider-neutral outbound operations for refund, partial refund, capture, void/cancel, and transaction inquiry where product requirements need them.
   - Keep all money-recognition and order mutation behind `IPaymentLifecycleService`.

6. **PAYMENT-1.1 Paymob Webhook Replay and Status Drill**
   - Exercise duplicate webhooks, callback-only return, webhook-before-redirect, redirect-before-webhook, invalid HMAC, mismatched amount/currency, refund webhook, void webhook, and delayed settlement events.

## Breaking Changes

No immediate breaking change was found in the current implementation.

Breaking-change exposure exists because:

- Current code depends on legacy Paymob order/payment-key/IFrame setup.
- New merchant onboarding may not be able to create new IFrames without Paymob team support.
- Official current docs present Intention API as the main backend initiation flow.
- Current config does not model Public Key or Secret Key separately.
- Regional expansion is blocked by Egypt-only base URL constants.

## Security Review

Strong alignments:

- Backend-only Paymob session creation.
- Encrypted tenant payment configuration.
- HMAC validation for webhook and callback flows.
- Raw payload/canonical HMAC handling.
- Tenant resolution before webhook processing.
- SQL webhook logs and duplicate collapse.
- Serializable/locked processing paths in payment lifecycle.
- Storefront treats Paymob redirect values as pending evidence only.
- Payment success/failure depends on backend status polling and lifecycle persistence.

Security gaps/recommendations:

- Add explicit Paymob v2 credential model: Secret Key, Public Key, API Key, HMAC Secret, mode, region, and integration IDs.
- Add key rotation/runbook requirements.
- Add explicit sandbox/live mismatch rejection.
- Add provider outbound idempotency review for Paymob Intention/refund/capture/void calls.
- Add replay-window policy if Paymob provides timestamp/event ids in v2 callback payloads.
- Continue rejecting callback-only success and frontend success claims.

## Runtime Impact

Immediate runtime update required: **No**.

Recommended runtime update: **Yes, planned migration**.

Current runtime can remain operational for already-configured IFrame merchants. A future runtime update is recommended to align new Paymob integrations with Intention API and Unified Checkout/Pixel. This should be planned as an adapter migration, not a rewrite of PAYMENT-0 authority.

## Compatibility Scores

Scores are intentionally separate; no overall score is calculated.

| Score Area | Score | Rationale |
|---|---:|---|
| Architecture Compatibility | 88/100 | Backend authority, provider neutrality, HMAC, lifecycle admission, SQL idempotency, and pending-only frontend are strong. IFrame dependency is migration-sensitive but not architecture-breaking. |
| Official API Compatibility | 58/100 | Current implementation uses legacy Accept order/payment-key/IFrame flow while official docs now emphasize Intention API, Unified Checkout, Pixel, Secret/Public keys, and callback override fields. |
| Implemented Feature Coverage | 42/100 | Cards/IFrame/webhooks/status/refund-partial-CommerceHub are present; wallets, Meeza, kiosk, QR, BNPL, saved cards, tokenization, recurring, capture, and void are not first-class. |
| Security Alignment | 82/100 | HMAC, encrypted secrets, backend-owned status, duplicate collapse, and tenant resolution are strong. Credential model and environment/mode validation need v2 hardening. |
| Future Runtime Readiness | 64/100 | PAYMENT-0 can absorb Intention/Unified Checkout cleanly, but DTOs/config/session contracts need migration design. |

## Recommendations

1. Do not change runtime in PAYMENT-0.5B.
2. Keep current IFrame flow operational for existing merchants until a planned migration is built.
3. Start the next phase with Paymob Intention/Unified Checkout adapter design.
4. Add a Paymob credential/environment governance document before implementation.
5. Treat IFrame as legacy/new-merchant-risk in future onboarding.
6. Preserve current backend lifecycle authority and frontend pending-only behavior during migration.
7. Add capability flags per tenant/account/method instead of assuming all Paymob-supported methods are enabled.
8. Verify Meeza, Kiosk, QR, BNPL, Apple Pay, Google Pay, and tokenization entitlements with the actual Paymob merchant account before advertising support.
9. Add Paymob webhook replay/status drills before production rollout of any new Paymob flow.

## Certification Decision

**PAYMENT-0.5B Paymob Discovery Decision: COMPATIBLE WITH REQUIRED MIGRATION PLANNING.**

The current Paymob implementation still aligns with PAYMENT-0 architecture and provider-neutral enterprise payment runtime. It does not fully align with the latest official Paymob API direction because it uses the legacy Paymob Accept payment-key/IFrame flow rather than Intention API plus Unified Checkout/Pixel. This is a migration opportunity, not an immediate architecture failure.

## Success Answers

| Question | Answer |
|---|---|
| Reviewed latest official Paymob APIs? | Yes. Official Paymob docs portal, LLM index, credentials, API integration, test credentials, FAQ/common issues, dashboard/new dashboard, and glossary content were reviewed. |
| Reviewed current implementation? | Yes. Storefront Paymob service/controller/webhook/form, CommerceHub Paymob session/refund paths, configuration, DTOs, entities, and certified architecture tests/docs were reviewed. |
| Compared both? | Yes. Official Paymob capabilities were compared against current code and PAYMENT-0 architecture. |
| Architecture still compatible? | Yes. Compatible with PAYMENT-0 provider-neutral backend authority. |
| Breaking changes found? | No immediate breaking change; migration-sensitive IFrame/new-merchant risk found. |
| Deprecated APIs found? | IFrame is effectively legacy for new setup per Paymob FAQ; no explicit hard retirement found for current Accept endpoints. |
| Security issues found? | No immediate critical security failure; credential/mode/region/idempotency hardening is recommended. |
| Migration recommended? | Yes. Migrate toward Intention API and Unified Checkout/Pixel. |
| Runtime update required? | Not immediately. Planned runtime update is recommended. |
| Immediate action required? | No immediate production action unless onboarding a new Paymob merchant or region. |
| Recommended next phase? | PAYMENT-0.6 Paymob Intention/Unified Checkout Adapter Design and Credential Governance. |

