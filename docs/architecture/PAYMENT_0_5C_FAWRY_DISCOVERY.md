# PAYMENT-0.5C Fawry Official API Discovery

Date: 2026-07-10
Scope: Official Fawry payment platform vs current project Fawry implementation vs certified PAYMENT-0 architecture
Status: DISCOVERY COMPLETE - NO RUNTIME CHANGE

## Executive Summary

Fawry was reviewed against the certified PAYMENT-0 provider-neutral payment architecture and the current PointOfSale Fawry runtime. The current implementation remains architecture-compatible because Fawry evidence is backend-owned, persisted in SQL `PaymentIntents`, admitted through backend checkout/payment services, and finalized only through backend lifecycle authority.

Certification decision: **ARCHITECTURE-COMPATIBLE, OFFICIAL-API-INCOMPLETE, MIGRATION REQUIRED BEFORE REAL FAWRY PRODUCTION PROCESSING**.

No runtime, backend, frontend, API, migration, refactor, test, or configuration change was applied.

Primary findings:

- Current Fawry runtime is a deterministic backend reference-code simulation, not an outbound implementation of official Fawry charge/session APIs.
- The existing architecture boundary is strong: Storefront cannot create accepted Fawry payment authority, reference evidence, checkout token, payment status, or final order status.
- Official Fawry APIs support reference-number payments, hosted/express checkout, card payments, wallet QR/R2P payments, bank installments, refunds including partial refunds, unpaid-order cancellation, auth/capture/cancel authorization, payment status pull V2, and server notification V2.
- Current project implements only the platform shape for PayAtFawry/reference presentation and webhook/lifecycle admission. It does not call Fawry charge, hosted checkout, card, wallet, refund, capture, cancel, void, or status APIs.
- Official Fawry signatures are SHA-256 concatenation signatures using `merchantCode`/reference/amount/status fields plus secure key. Current webhook implementation expects `X-Fawry-Signature` as an HMAC-SHA256 over the raw payload. This is likely incompatible with official Server Notification V2 unless a custom gateway/proxy signs that header.
- Current Fawry config requires `merchantCode`, `apiKey`, and `webhookSecret`; official docs use `merchantCode` plus secure key/signature key for request, response, notification, and status signatures. `apiKey` is treated in code as an alias for `securityKey`, but the naming is migration-sensitive.
- Fawry webhook tenant resolution currently uses `referenceNumber`; official Server Notification V2 names the provider reference `fawryRefNumber` and merchant reference `merchantRefNumber`. Current resolver/DTO field names need a mapping update before official notification ingestion.
- No immediate PAYMENT-0 architecture rewrite is required. A Fawry adapter/API migration is required before certifying live official Fawry processing.

## Current Implementation

Backend Fawry runtime:

- `PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs`
- `PointOfSale.API/Controllers/FawryWebhookController.cs`
- `PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs`

Frontend Fawry runtime:

- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/CheckoutSuccess.tsx`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`

Configuration and durable authority:

- `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`
- `PointOfSale.Core/Entities/TenantPaymentMethod.cs`
- `PointOfSale.Core/Entities/PaymentIntent.cs`
- `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`

Frozen certifications reviewed:

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/CODEX_RULES.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/LESSON_UPDATE_RULES.md`
- `.ai-context/LOCAL_DEV_STRIPE_HANDOFF_2026-05-12.md`
- `docs/architecture/PAYMENT_0_PLATFORM_ARCHITECTURE_CERTIFICATION.md`
- `docs/architecture/PAYMENT_0_5A_STRIPE_DISCOVERY.md`
- `docs/architecture/PAYMENT_0_5B_PAYMOB_DISCOVERY.md`
- `docs/architecture/fawry-payment-authority-certification-findings.md`
- `docs/architecture/payment-end-to-end-authority-certification-findings.md`

Implemented current flow:

```text
Storefront selects FAWRY
  -> CheckoutService validates tenant/session/cart/order/payment method/pricing
  -> PaymentService creates or replays SQL PaymentIntent
  -> FawryPaymentAdapter simulates provider initiation
  -> deterministic backend reference code is generated from tenant/order/idempotency evidence
  -> PaymentIntent stores ReferenceCode, ProviderIntentId, ExpiresAtUtc, amount, currency
  -> CheckoutService emits backend checkoutToken, paymentIntentId, referenceCode, expiration
  -> Storefront admission rejects malformed/mixed/terminal Fawry artifacts
  -> CheckoutSuccess presents reference code only after admission
  -> FawryWebhookController admits signed webhook, resolves tenant by stored reference, logs event
  -> IPaymentLifecycleService owns final payment/order status transition
```

The adapter currently contains an explicit simulation comment and does not call Fawry endpoints. This is acceptable for architectural authority certification, but not sufficient for official Fawry API compatibility.

## Official Fawry APIs Reviewed

Official Fawry documentation reviewed from the FawryPay developer site on 2026-07-10:

- FawryPay Developer Guide: `https://developer.fawrystaging.com/docs-home`
- Reference-number charge: `https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis`
- Card/3DS payment: `https://developer.fawrystaging.com/docs/server-apis/create-payment-3ds-apis`
- Auth/capture/cancel authorization: `https://developer.fawrystaging.com/docs/server-apis/auth-capture-payment-apis`
- Wallet QR/R2P payments: `https://developer.fawrystaging.com/docs/server-apis/mobile-wallet-payment`
- Refund API: `https://developer.fawrystaging.com/docs/server-apis/refund-issue-api`
- Cancel unpaid order API: `https://developer.fawrystaging.com/docs/server-apis/cancel-payment-api`
- Express checkout button/self-hosted: `https://developer.fawrystaging.com/docs/express-checkout/self-hosted-checkout`
- Hosted checkout link: `https://developer.fawrystaging.com/docs/express-checkout/fawrypay-hosted-checkout`
- Legacy express checkout migration: `https://developer.fawrystaging.com/docs/express-checkout/migrate-from-old-plugin`
- Server Notification V2: `https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/server-notification-v2`
- Get Payment Status V2: `https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/get-payment-status-v2`

Key official Fawry facts used for this discovery:

- The docs list integration surfaces for Express Checkout, server-side APIs, mobile SDKs, plugins, Pay By Link, card tokenization, bank installments, payment split, postman collection, signature tool, end-to-end testing, and error codes.
- Reference-number payments use `POST /ECommerceWeb/Fawry/payments/charge` with `paymentMethod: PayAtFawry`, `merchantCode`, `merchantRefNum`, customer data, amount, currency, charge items, optional `paymentExpiry`, optional `orderWebHookUrl`, language, and SHA-256 signature.
- Reference-number response includes Fawry `referenceNumber`, merchant reference, order amount, payment amount, fees, payment method, order status, payment time, customer evidence, and response signature.
- Hosted checkout link returns a redirect URL from a charge request and supports return URL, charge items, customer information, expiration, language, and signature.
- Express Checkout supports popup, inside-page, side-page, and separated display modes through Fawry JavaScript checkout.
- Wallet APIs support QR-code payment and request-to-pay flow using mobile wallet number and `MWALLET`.
- Refund API supports full and partial refunds of captured/paid amounts and requires Fawry reference number, refund amount, optional reason, and SHA-256 signature.
- Cancel Unpaid Order API cancels only unpaid orders; paid orders must be refunded instead.
- Auth/capture docs include authorization, capture, and cancel authorization operations for eligible card payments.
- Server Notification V2 posts status updates to merchant callback URLs configured during merchant setup; callback endpoint configuration is environment-specific. It also documents optional per-order webhook URL use in reference-payment charge requests.
- Notification/status payloads include statuses such as `NEW`, `PAID`, `CANCELED`, `REFUNDED`, `PARTIAL_REFUNDED`, `EXPIRED`, and `FAILED`.
- Notification/status signature is `messageSignature`, a SHA-256 digest over ordered provider fields plus secure key. Get Payment Status V2 request signature is SHA-256 over `merchantCode + merchantRefNumber + secureKey`.
- Fawry expects a 200 response for delivered server notifications; otherwise Fawry retries for a configured retry count.
- Official docs warn that the merchant secure hash key must be stored server-side and must not be displayed or communicated to clients.
- Legacy express checkout plugin migration is documented; the renovated checkout plugin is recommended for legacy plugin users.

## Architecture Compatibility

| Area | Compatibility | Notes |
|---|---:|---|
| PAYMENT-0 provider neutrality | Compatible | Fawry is behind backend payment services/adapter and SQL PaymentIntent authority. |
| Backend payment authority | Compatible | Storefront presents only admitted reference evidence; lifecycle owns finality. |
| Payment session model | Compatible | Current `PaymentIntent` stores amount, currency, reference, expiration, provider intent, and checkout token handoff. |
| Provider adapter model | Compatible but incomplete | Adapter exists, but simulates Fawry instead of calling official APIs. |
| Reference-number flow | Architecturally compatible, API-incomplete | Current reference presentation matches the shape, but no official charge call or provider `referenceNumber` response handling exists. |
| Hosted checkout / redirect | Not implemented | Can fit canonical session handoff as provider checkout URL. |
| Card payments | Not implemented | Officially supported; project routes card payments through Stripe/Paymob, not Fawry card APIs. |
| Cash / retail PayAtFawry | Partial | Reference-code presentation exists; real Fawry charge/expiry/provider reference creation does not. |
| QR / wallet | Not implemented | Official wallet QR/R2P support is not modeled. |
| Refund / partial refund | Not implemented for Fawry outbound | PAYMENT-0 lifecycle can model refund states; Fawry API adapter is absent. |
| Capture / authorization cancel | Not implemented for Fawry | Lifecycle can model authorized/captured/cancelled states; Fawry outbound operations are absent. |
| Void / unpaid cancellation | Not implemented for Fawry | Official unpaid-order cancel maps cleanly to provider void/cancel operation. |
| Status inquiry | Not implemented | Current recovery relies on SQL/reference state and webhook, not Fawry Status V2 pull. |
| Webhook admission | Strong architecture, likely official-contract mismatch | Tenant resolution, HMAC, SQL logs, lifecycle are strong; official payload/signature names differ. |
| Credential isolation | Mostly compatible | Encrypted tenant config is strong; official secure-key naming and environment governance need update. |
| Idempotency | Platform-compatible | SQL active-intent uniqueness and webhook duplicate collapse exist; provider outbound idempotency is not applicable until official calls exist. |

## Capability Matrix

| Capability | Official Fawry Support | Current Project | Recommendation |
|---|---:|---:|---|
| Supports Redirect | Yes, hosted checkout link / separated checkout | No Fawry redirect session | Add as future provider session mode if desired. |
| Supports Hosted Checkout | Yes | No | Future adapter opportunity. |
| Supports Cash Collection | Yes via PayAtFawry/reference number | Simulated reference presentation only | Implement official reference charge before production. |
| Supports Retail Payments | Yes, Fawry POS retail stores | Simulated reference presentation only | Same as cash collection. |
| Supports Cards | Yes | No Fawry card path | Optional future capability; Stripe/Paymob already cover cards. |
| Supports QR | Yes through wallet API QR flow | No | Future wallet/QR adapter if product needs it. |
| Supports Wallets | Yes through MWALLET QR/R2P | No | Future account/method capability. |
| Supports Installments | Yes, bank installments docs present | No | Future capability; verify merchant entitlement. |
| Supports Refund | Yes | No outbound Fawry refund | Add provider-neutral refund adapter if Fawry payments go live. |
| Supports Partial Refund | Yes | No outbound Fawry partial refund | Add amount-aware refund adapter and lifecycle tests. |
| Supports Capture | Yes for authorization flow | No | Future auth/capture adapter. |
| Supports Void / Cancel | Yes for unpaid order and auth cancel | No outbound Fawry cancel | Add canonical cancel/void operation. |
| Supports Saved Cards | Yes via card tokenization docs | No | Future tokenization phase only. |
| Supports Installments | Yes | No | Verify terms/currency/account enablement before advertising. |
| Supports Recurring | Pay By Link recurring invoice docs exist; recurring payments need separate design | No | Treat as future invoice/recurring capability, not current checkout. |
| Supports Tokenization | Yes for cards | No | Future saved-card/token phase. |
| Supports Webhooks | Yes, Server Notification V2 | Partial, custom HMAC payload contract | Update to official notification contract. |
| Supports Signature Validation | Yes, SHA-256 ordered-field signatures | Current uses raw-payload HMAC | Must migrate/dual-validate for official. |
| Supports Async Payments | Yes | Partially ready through pending/reference/webhook lifecycle | Strong architecture; needs official API/status integration. |
| Supports Metadata | Merchant reference, customer profile, item fields, webhook URL | Internal idempotency/order metadata only | Map canonical metadata into official fields. |
| Supports Idempotency | Merchant reference uniqueness can serve as provider idempotency; explicit idempotency header not found | SQL idempotency only | Use stable merchantRefNum and reconcile duplicate charge attempts. |

## Provider-Specific Constraints

- Fawry reference payments require a payment window. Official `paymentExpiry` is a timestamp in milliseconds; if omitted, merchant-level configured expiry applies.
- Reference payments are Egyptian retail/cash-network oriented and examples use EGP.
- Payment at retail/Fawry POS is asynchronous. The canonical runtime must keep the session pending until notification/status confirms payment or expiration/cancel/failure.
- Customer mobile and email are required for reference-number charge requests in official docs.
- Official callback URL setup is merchant-account/environment configuration, not purely per request. Reference-number charge also documents optional `orderWebHookUrl`.
- Paid orders cannot be cancelled through unpaid-order cancellation; they must use refund.
- Refunds apply to paid/captured amounts; authorized but uncaptured payments cannot be refunded.
- Wallet QR/R2P requires wallet-specific fields such as debit mobile wallet number and may have method-specific confirmation timing.
- Card 3DS responses include 3DS-specific data only when applicable.
- Capture/cancel authorization are separate from immediate sale/reference flows and should not be globally exposed unless the selected Fawry method supports authorization.
- Signature construction is field-order sensitive and amount-format sensitive. Amounts must be formatted to two decimals where specified.
- Notification retry depends on merchant endpoint HTTP response. Non-200 responses cause retries for the configured retry count.
- Merchant account activation, method enablement, production endpoint access, callback URLs, and secure keys must be coordinated with Fawry operations/account setup.

## Canonical Payment Runtime Mapping

| Canonical Runtime Concept | Fawry Equivalent | Current Project Mapping | Gap / Recommendation |
|---|---|---|---|
| Payment Session | Charge request, hosted checkout link/session, wallet request, authorization request | SQL `PaymentIntent` | Keep `PaymentIntent`; add official session subtype. |
| Provider Session Id | `referenceNumber` / `fawryRefNumber`, hosted `sessionId`, auth transaction id | `ProviderIntentId = FAWRY-{referenceCode}` | Store actual Fawry provider reference/session id. |
| Merchant Reference | `merchantRefNum` / `merchantRefNumber` | deterministic internal reference/idempotency evidence | Map stable payment/order reference to official merchant reference. |
| Customer Action | Pay at Fawry POS, redirect to hosted checkout, scan QR, approve wallet R2P, complete 3DS | Storefront displays reference code | Add customer-action type: reference, redirect, QR, wallet approval, 3DS. |
| Payment Confirmation | Server Notification V2, Status V2 pull | `FawryWebhookController` custom DTO | Implement official payload parsing and signature validation. |
| Pending | `NEW`, `PENDING`, unpaid reference awaiting customer action | `Pending` / `Processing` accepted for handoff | Compatible. |
| Reference Generated | Charge response `referenceNumber` | deterministic simulated `ReferenceCode` | Replace/supplement with official charge response. |
| Customer Paid | Notification/status `PAID` | lifecycle outcome `Succeeded` | Compatible after official parsing. |
| Authorized | Fawry card authorization response | lifecycle supports authorized state conceptually | Add Fawry auth adapter if needed. |
| Captured | Capture API response/status | lifecycle supports captured/paid | Add Fawry capture adapter if needed. |
| Cancelled / Void | Cancel unpaid order or cancel auth | lifecycle `Cancelled` | Add provider operation and status mapping. |
| Expired | Notification/status `EXPIRED` or local expiration | lifecycle failed/expired handling | Map `EXPIRED` to canonical `Expired`, not generic failed where possible. |
| Refund | Refund API / `REFUNDED` status | lifecycle refund states, no Fawry adapter | Add refund adapter. |
| Partial Refund | Refund API amount less than captured amount / `PARTIAL_REFUNDED` | lifecycle partial refund state, webhook currently lacks mapping | Add status mapping and amount-aware reconciliation. |
| Settlement | Batch/acquirer/payment references and future reconciliation data | Not Fawry-specific | Add settlement reference mapping if Fawry settlement reports are integrated. |
| Async Event | Server Notification V2 push | SQL `PaymentWebhookLogs` | Preserve SQL idempotency; use official event identity/signature. |
| Status Inquiry | Get Payment Status V2 | Not implemented | Add pull reconciliation for recovery/drift. |
| Provider Credentials | `merchantCode`, secure key/signature key, account setup data | `merchantCode`, `apiKey`, `webhookSecret` | Rename/govern secure key and environment fields. |

## Current Gaps

1. No official Fawry outbound API integration.
   - The adapter simulates Fawry latency, creates a deterministic 10-digit reference, and returns `FAWRY-{referenceCode}`.
   - Official charge, hosted checkout, card, wallet, refund, capture, cancel, and status endpoints are not called.

2. Signature model mismatch.
   - Current webhook code verifies `X-Fawry-Signature` with `HMACSHA256(rawPayload, webhookSecret)`.
   - Official notification/status docs use `messageSignature` as SHA-256 over ordered fields plus secure key.

3. Payload field mismatch for official notifications.
   - Current tenant resolver expects root `referenceNumber`.
   - Official Server Notification V2 uses `fawryRefNumber` and `merchantRefNumber`; response/reference charge uses `referenceNumber`.

4. Status coverage is narrower than official lifecycle.
   - Current webhook maps paid/success/completed, failed, expired as failed, and canceled/cancelled.
   - Official statuses include `NEW`, `PAID`, `CANCELED`, `REFUNDED`, `PARTIAL_REFUNDED`, `EXPIRED`, and `FAILED`.

5. Configuration model is migration-sensitive.
   - Current validation requires `merchantCode`, `apiKey`, `webhookSecret`.
   - Official docs center secure key/signature key, merchant code, callback URLs, environment endpoints, and method enablement.

6. Reference expiration is local, not provider-derived.
   - Current adapter always sets 24 hours.
   - Official charge supports `paymentExpiry`; merchant default applies if omitted.

7. Status inquiry/reconciliation is missing.
   - Get Payment Status V2 should be used for recovery where webhook delivery is delayed or ambiguous.

8. No Fawry refund/capture/cancel operation adapters.
   - PAYMENT-0 lifecycle can represent these states, but Fawry operations are not executed.

9. No hosted checkout, QR, wallet, installments, card, tokenization, or recurring capability contracts.
   - These should be capability flags per tenant/account/method, not global assumptions.

10. Source comments still say webhook is source of truth.
   - PAYMENT-0 certified language is more precise: backend Payment lifecycle is authority; webhooks are provider evidence admitted by Payment.

## Deprecated Features

Confirmed official migration-sensitive area:

- Legacy Express Checkout Plugin: Fawry documents migration from a legacy checkout plugin to a renovated checkout plugin.

Not confirmed as deprecated:

- Reference-number PayAtFawry charge API.
- Server-side card APIs.
- Wallet APIs.
- Refund API.
- Status V2 / Notification V2.

Current project does not use the legacy express checkout plugin, so no direct deprecated-plugin usage was found.

## Migration Opportunities

Recommended future phases:

1. **PAYMENT-0.6 Fawry Credential and Signature Governance**
   - Model `merchantCode`, secure key/signature key, callback URL, environment, production/staging endpoints, and method entitlements.
   - Replace ambiguous `apiKey`/`webhookSecret` naming with a backward-compatible secure-key migration.
   - Define official signature builders/verifiers with strict amount formatting and field ordering.

2. **PAYMENT-0.7 Fawry Reference-Number Charge Adapter**
   - Implement official PayAtFawry charge creation.
   - Use stable `merchantRefNum` for idempotency.
   - Persist provider `referenceNumber`/`fawryRefNumber`, expiry, response signature, amount, fees, and status.

3. **PAYMENT-0.8 Fawry Notification V2 and Status V2 Reconciliation**
   - Parse official `fawryRefNumber`, `merchantRefNumber`, `orderStatus`, `paymentMethod`, `paymentRefrenceNumber`, and `messageSignature`.
   - Add Status V2 pull for recovery, delayed webhook, and admin reconciliation.

4. **PAYMENT-0.9 Fawry Canonical Runtime Session Modes**
   - Add capability/session modes for reference, hosted checkout redirect, QR, wallet R2P, card 3DS, auth/capture, and installments.
   - Emit customer-action evidence as canonical runtime output.

5. **PAYMENT-1.0 Fawry Lifecycle Operations**
   - Add provider-neutral Fawry refund, partial refund, unpaid cancel/void, capture, and cancel authorization operations where product requirements need them.
   - Keep all money recognition behind `IPaymentLifecycleService`.

6. **PAYMENT-1.1 Fawry Operational Readiness**
   - Run sandbox end-to-end tests for charge creation, reference payment, expiration, paid notification, duplicate notification, invalid signature, status pull, refund, partial refund, unpaid cancellation, and retry behavior.

## Breaking Changes

No immediate runtime breaking change was found because the current Fawry integration is internally simulated and architecture-contained.

Breaking-change exposure exists before real official Fawry rollout because:

- Current webhook signature verification likely does not match official Server Notification V2.
- Current webhook field names likely do not match official notification payloads.
- Current adapter does not call official Fawry endpoints.
- Current config does not explicitly model secure key/signature key, callback URL, endpoint mode, or provider method entitlements.
- Current status mapping does not cover refund/partial refund/new/pending semantics from official payloads.
- Current 24-hour expiration may not match Fawry account defaults or per-charge `paymentExpiry`.

## Security Review

Strong alignments:

- Fawry configuration is tenant-scoped and encrypted in `TenantPaymentMethod.ConfigEncrypted`.
- Storefront receives only admitted reference/expiration/paymentIntent/checkoutToken evidence.
- Storefront admission rejects incomplete Fawry handoffs, terminal reusable artifacts, mixed provider artifacts, stale expiration, provider mismatch, and missing checkout token.
- Webhook endpoint uses request size limits, rate limiting, raw body pipeline, tenant resolution, signature verification, fixed-time comparison, SQL webhook logs, serializable processing, duplicate collapse, failure retry behavior, and lifecycle-owned state transitions.
- Reference resolution is based on persisted payment intent evidence, not caller-trusted tenant headers.

Security gaps/recommendations:

- Replace or augment raw-payload HMAC validation with official Fawry ordered-field SHA-256 verification.
- Verify response signatures from charge/status/refund APIs before trusting provider payloads.
- Do not expose secure key/signature key to browser. Hosted checkout and express checkout signatures should be backend-generated.
- Add explicit staging/production endpoint and callback URL governance.
- Use amount/currency validation through lifecycle for every official event/status response.
- Add replay-window and duplicate policy using official request id/reference/payment reference fields.
- Add operational secret rotation and environment separation runbook.
- Preserve fail-closed behavior for unknown references, unsupported statuses, invalid signatures, amount mismatch, currency mismatch, and terminal-state replays.

## Runtime Impact

Immediate runtime update required: **No** for the discovery phase and no change was applied.

Runtime update required before real official Fawry production processing: **Yes**.

Expected future impact:

- Fawry provider adapter must become an outbound official API client.
- Payment-session DTOs may need provider-action variants beyond `referenceCode`.
- Webhook controller/resolver must support official Notification V2 field names and signatures.
- Recovery jobs/services should support Status V2 pull.
- Lifecycle operations should add Fawry refund/partial refund/cancel/capture adapters only where business flows require them.

## Compatibility Scores

Scores are intentionally separate; no overall score is calculated.

| Score Area | Score | Rationale |
|---|---:|---|
| Architecture Compatibility | 86/100 | Backend authority, provider neutrality, SQL PaymentIntent, DTO admission, checkout token, lifecycle ownership, idempotency, and fail-closed presentation are strong. Points lost because adapter simulation and official-contract mismatch are close to provider boundary. |
| Official API Compatibility | 34/100 | Current code does not call official Fawry APIs and likely mismatches official notification signature/payload fields. Reference-payment shape is conceptually aligned. |
| Implemented Feature Coverage | 20/100 | Only reference-code presentation, expiration, webhook admission shape, and lifecycle status admission exist. Hosted checkout, card, wallet, QR, refund, partial refund, capture, void/cancel, and status inquiry are absent. |
| Security Alignment | 68/100 | Tenant encryption, backend ownership, DTO admission, rate limits, SQL logs, and fixed-time comparison are strong. Official Fawry signature model, response-signature validation, secure-key naming, and environment governance need migration. |
| Future Runtime Readiness | 72/100 | PAYMENT-0 can absorb Fawry official APIs cleanly through provider-neutral session/lifecycle contracts, but DTOs/config/webhook/status mapping need planned updates. |

## Recommendations

1. Do not change runtime in PAYMENT-0.5C.
2. Keep the current Fawry boundary frozen as backend-owned and presentation-only until the official adapter is designed.
3. Treat current Fawry as **architecture-certified simulation**, not official Fawry production readiness.
4. Start the next phase with Fawry credential/signature governance and official reference-number charge adapter design.
5. Implement official Notification V2 signature and field mapping before accepting real Fawry webhooks.
6. Add Status V2 pull reconciliation before production rollout.
7. Add capability flags per Fawry tenant/account/method for reference, hosted checkout, card, wallet, QR, installments, refund, partial refund, capture, void, tokenization, recurring, and webhooks.
8. Keep Storefront pending-only and evidence-only. Do not let browser redirects, reference displays, or client state mark Fawry paid.
9. Verify Fawry merchant activation, production callback URLs, method entitlements, expiry rules, currency support, and refund/cancel restrictions with the actual merchant account before enabling capabilities.
10. Run sandbox operational drills before any production Fawry release.

## Certification Decision

**PAYMENT-0.5C Fawry Discovery Decision: ARCHITECTURE-COMPATIBLE, OFFICIAL-API-INCOMPLETE, MIGRATION REQUIRED BEFORE REAL FAWRY PRODUCTION PROCESSING.**

The current Fawry implementation still aligns with PAYMENT-0 architecture and provider-neutral enterprise payment runtime. It does not fully align with latest official Fawry APIs because the adapter simulates reference creation and the webhook contract appears custom rather than official Notification V2. This is not a provider-neutral architecture failure. It is a provider adapter/API migration requirement.

## Success Answers

| Question | Answer |
|---|---|
| Reviewed latest official Fawry APIs? | Yes. Official FawryPay developer docs were reviewed on 2026-07-10, including reference payments, hosted/express checkout, card, wallet/QR, refund, cancel unpaid order, auth/capture, Notification V2, Status V2, and legacy checkout migration pages. |
| Reviewed current implementation? | Yes. Fawry adapter, checkout/payment creation, webhook controller, tenant resolver, configuration validator, storefront DTO admission/presentation, frozen tests, and frozen architecture docs were reviewed. |
| Compared both? | Yes. Official Fawry APIs were compared against current code and PAYMENT-0 architecture. |
| Architecture still compatible? | Yes. Current Fawry remains backend-owned, provider-neutral, SQL/lifecycle-authoritative, and presentation-only at Storefront. |
| Breaking changes found? | No immediate runtime breaking change; official rollout blockers were found in adapter absence, signature mismatch, payload field mismatch, and config governance. |
| Deprecated APIs found? | Legacy Express Checkout Plugin migration is documented by Fawry. Current project does not use that plugin. |
| Security issues found? | No immediate critical architecture security failure; official signature verification, response-signature validation, secure-key naming, environment separation, replay policy, and callback governance must be fixed before live official Fawry processing. |
| Migration recommended? | Yes. Fawry official adapter, signature, webhook, status, and lifecycle operation migration is recommended. |
| Runtime update required? | Not for this discovery. Yes before real official Fawry production processing. |
| Immediate action required? | No immediate code action required unless Fawry is being used against real provider APIs today. |
| Canonical Runtime Mapping completed? | Yes. Fawry concepts were mapped to canonical session, provider session id, customer action, confirmation, refund, capture, void/cancel, settlement, async event, and status inquiry concepts. |
| Recommended next phase? | PAYMENT-0.6 Fawry Credential and Signature Governance plus Official Reference-Number Charge Adapter Design. |

