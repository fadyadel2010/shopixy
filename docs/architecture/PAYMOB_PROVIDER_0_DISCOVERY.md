# PAYMOB-PROVIDER-0 Discovery and Compatibility Certification

Date: 2026-07-17

Status: DISCOVERY COMPLETE - COMPATIBILITY CONFIRMED FOR PROVIDER ADAPTER IMPLEMENTATION

Scope: Paymob provider discovery against the certified Shopixy Payment Platform.

Excluded: implementation, Runtime redesign, Lifecycle redesign, Workflow redesign, Dispatcher redesign, Intent handler redesign, Order Domain redesign, Event Pipeline redesign, Provider Contract redesign, and Canonical Runtime Contract changes.

## Executive Summary

Paymob can fit the certified Payment Platform as a pure provider adapter. No frozen platform component requires redesign for Paymob.

The compatible shape is:

```text
Trusted Payment Application Service
  -> tenant Paymob configuration and one-shot credential handoff
  -> Paymob Provider Adapter
  -> Paymob HTTPS APIs / HMAC verification
  -> canonical ProviderEvidence or CanonicalProviderError
  -> existing PaymentRuntimeOrchestrator
  -> existing PaymentLifecycleService
  -> existing Order Domain and Event Pipeline
```

Paymob differs from Stripe at the protocol layer only. Stripe is PaymentIntent/client-secret shaped. Paymob is intention/checkout or legacy order/payment-key/iframe shaped, uses regional hosts, integration IDs, HMAC callbacks, and account-enabled payment methods. These differences can terminate inside the Paymob adapter, webhook evidence adapter, capability provider, and application-owned tenant credential handoff.

No Runtime, Lifecycle, Workflow, Dispatcher, handler, Order Domain, Event Pipeline, provider contract, or canonical Runtime contract modification is required by this discovery.

## Mandatory Reading Basis

Reviewed local certified records:

| Record | Discovery use |
|---|---|
| `PAYMENT_REFERENCE_FREEZE.md` | Controlling frozen architecture, provider adapter rules, replay, credential handoff, and single-writer rules. |
| `PAYMENT_END_TO_END_1_CERTIFICATION.md` | Workflow output to dispatcher, handlers, Order Domain, SQL, and event propagation boundaries. |
| `STRIPE_PROVIDER_1.md` | Reference adapter pattern for provider isolation and canonical evidence/error mapping. |
| `STRIPE_SANDBOX_1_CERTIFICATION.md` | Sandbox and source-operational certification separation. |
| `PAYMENT_0_PLATFORM_ARCHITECTURE_CERTIFICATION.md` | Payment authority, lifecycle ownership, session model, transaction model, and webhook ownership. |
| `PAYMENT_0_5B_PAYMOB_DISCOVERY.md` | Prior Paymob public API discovery and migration sensitivity. |
| `PAYMOB_1_FOUNDATION_CERTIFICATION.md` through `PAYMOB_5_ADVANCED_OPERATIONS.md` | Existing Paymob source-layer boundary evidence. |
| `PAYMOB_CONVERGENCE_1.md` | POS Admin and Storefront Paymob convergence evidence. |
| `PAYMOB_CERTIFICATION_CLOSURE_5.md` | Sensitive Paymob artifact protection evidence. |
| `PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md` | Source-vs-live sandbox boundary and deployment dependencies. |

Reviewed Paymob public documentation:

| Source | Key facts |
|---|---|
| Paymob overview, last updated July 16, 2026 | Regions, payment methods, hosted checkout, Pixel, SDKs, sandbox/live model. |
| Paymob API integration path, last updated June 1, 2026 | Backend creates Payment Intention; checkout uses Unified Checkout or Pixel; callbacks/webhooks communicate results; redirects are user experience. |
| Paymob credential FAQ, last updated June 1, 2026 | Secret Key, Public Key, API Key, Integration IDs, callback URLs; API key is same for Test/Live; integration IDs and keys control mode. |
| Paymob HMAC docs, last updated June 1, 2026 | Callback authenticity is verified by recalculating HMAC with the merchant HMAC secret and comparing to Paymob's HMAC value. |
| Paymob common issues FAQ, last updated June 1, 2026 | Integration ID/live-test mismatch, custom `notification_url` and `redirection_url`, transaction inquiry by transaction/order/special reference, IFrame new setup risk, card saving by dedicated Integration ID. |
| Paymob glossary, last updated July 16, 2026 | Auth/capture, refunds, voids, tokenization, saved cards, BNPL, webhooks, HMAC, settlement, and reconciliation concepts. |

## Provider Overview

Paymob provides online and in-person payment products, including hosted checkout, embedded Pixel checkout, mobile SDKs, ecommerce plugins, payment links, and dashboard operations. API integrations now center on backend Payment Intention creation and then customer checkout through Unified Checkout redirect or Pixel embedded checkout. Legacy Accept flows using authentication token, ecommerce order, payment key, and iframe can remain adapter-supported where a merchant is already provisioned, but should not become the new canonical provider template.

Supported public regions and hosts from current Paymob overview:

| Region | Host |
|---|---|
| Egypt | `https://accept.paymob.com/` |
| Oman | `https://oman.paymob.com/` |
| Saudi Arabia | `https://ksa.paymob.com/` |
| United Arab Emirates | `https://uae.paymob.com/` |

Sandbox and Production use the same regional base URL for a region. Mode is controlled by keys and Integration IDs. A global account is not supported; accounts are region-specific.

Supported integration surfaces include official HTTPS APIs, hosted checkout, Pixel embedded checkout, mobile SDKs for iOS, Android, Flutter, and React Native, ecommerce plugins, payment links, dashboard operations, and Postman/API reference material. For this platform, only official HTTPS APIs and HMAC verification are relevant to provider-adapter implementation.

API versioning strategy observed in public docs is documentation-path and endpoint evolution rather than a Stripe-style pinned API version header. The current API direction is Intention Creation plus Unified Checkout or Pixel. The adapter must treat endpoint family, region, checkout mode, and credential shape as tenant/provider configuration facts, not Runtime facts.

## Authentication

| Paymob credential or authentication item | Paymob behavior | Existing abstraction mapping | Runtime impact |
|---|---|---|---|
| API Key | Dashboard credential; legacy Accept authentication uses API key; FAQ says same API key for Test and Live. | Tenant credential configuration; consumed through one-shot adapter handoff when needed. | None. |
| Secret Key | Server-side API credential for API/SDK integrations; mode-specific. | Tenant credential configuration; server-side only; adapter receives for one operation. | None. |
| Public Key | Client initialization key; cannot authorize payments alone. | Handoff artifact for approved checkout mode only; never finality. | None. |
| Integration ID | Numeric method/environment binding; must match account and Test/Live mode. | Tenant capability/configuration fact; validated before adapter call. | None. |
| IFrame ID | Legacy iframe setup identifier; new setup requires Paymob handling. | Optional legacy checkout-mode configuration; adapter-local. | None. |
| HMAC Secret | Secret used to verify callback/webhook HMAC. | Tenant webhook credential; used by webhook evidence adapter after trusted tenant resolution. | None. |
| Callback authentication | Paymob sends HMAC query/value over transaction data. | Adapter verifies HMAC using official field rules and fixed-time comparison, then emits evidence or canonical error. | None. |
| Webhook authentication | Server-to-server transaction callback must be HMAC-verified. | Existing webhook admission plus Paymob evidence adapter. | None. |
| Credential rotation | Paymob glossary recommends rotating keys on go-live and incidents. | Existing tenant credential authority resolves current credential set per operation; prior evidence remains immutable. | None. |
| Tenant-specific credentials | Paymob accounts are regional and merchant-specific; methods are account-enabled. | Existing tenant payment-method configuration, capability admission, one-shot handoff. | None. |

Credential conclusion: Paymob maps cleanly to the certified tenant configuration and one-shot credential handoff model. The adapter must not resolve credentials, read SQL, cache credentials, or fall back to global credentials.

## Capability Matrix

| Capability | Native Paymob support | Canonical mapping | Adapter responsibility | Runtime impact |
|---|---|---|---|---|
| Payment Authorization | Yes. Auth and auth/capture are Paymob concepts; card authorization depends on integration setup. | `PaymentActionType.Redirect`, `EmbeddedCheckout`, or `ClientConfirmation`; evidence status `Admitted` or recorded provider notification. | Create intention or legacy order/payment key; map authorized flags/status to evidence. | None. |
| Capture | Yes where auth/capture is enabled. | Advanced operation evidence; lifecycle activation remains application-owned. | Translate capture request/response and errors. | None. |
| Void | Yes before capture for eligible authorization. | Advanced operation evidence for cancel/void. | Translate void request/response and errors. | None. |
| Cancel | Supported as void/cancel-before-capture or abandoned/expired checkout depending method. | Canonical cancellation/expired/failed evidence according to admitted provider state. | Classify Paymob state; do not fabricate missing cancel support. | None. |
| Refund | Yes for captured transactions, subject to method/account/balance rules. | Refund evidence; lifecycle writer records refund. | Translate refund request/response and refund webhook facts. | None. |
| Partial Refund | Yes where method supports it; amount-specific restrictions apply. | Partial refund evidence with amount/currency. | Translate amount-bound refund and provider limitations. | None. |
| Tokenization | Yes through saved card/Card on File concepts. | Capability fact and future token handoff evidence; not local PAN storage. | Keep Paymob token models inside adapter; expose only canonical token reference if separately certified. | None. |
| Saved Cards | Yes via dedicated configuration/integration ID; customer card saving can be forced by account-manager setup. | Future saved-card capability and provider token reference. | Verify entitlement; map token reference only. | None. |
| Wallet Payments | Yes. Egypt wallets and KSA STC Pay are documented; account/method dependent. | Wallet capability with async confirmation evidence. | Create method-specific intention/request; map wallet statuses. | None. |
| Meeza | Region/account-dependent. Not explicitly proven in reviewed current overview except regional card/wallet ecosystem. | Do not certify globally; tenant capability only after Paymob account evidence. | Report unsupported unless account configuration proves enablement. | None. |
| Apple Pay | Yes as quick payment, region/setup dependent. | Wallet/quick-payment capability. | Preserve wallet-specific tokenization and domain/setup details inside adapter. | None. |
| Google Pay | Yes as quick payment, region/setup dependent. | Wallet/quick-payment capability. | Preserve gateway setup details inside adapter. | None. |
| Installments | Yes through BNPL/installment providers such as Tabby, Tamara, vaLU, Sympl, Souhoola, Halan, TRU, and MOGO, subject to region. | BNPL/installment capability; payment remains async/provider evidence. | Report per tenant/account/method; map terminal events. | None. |
| Buy Now Pay Later | Yes, same as installments. | Provider capability and terminal evidence. | Method-specific request and evidence translation. | None. |
| Payment Inquiry | Yes by Transaction ID, Order ID, or Special Reference Number. | `PaymentActionType.StatusRefresh` / `StatusInquiry` evidence. | Translate inquiry request/response. | None. |

Capability conclusion: Paymob's capabilities fit the current provider capability and evidence model. Unsupported or account-disabled features are adapter capability facts and application admission failures, not Runtime changes.

## Canonical Runtime Compatibility

| Canonical Runtime action | Paymob request | Paymob response | Canonical provider evidence | Existing Runtime behavior |
|---|---|---|---|---|
| Create redirect checkout | Intention Creation API with amount, currency, payment methods, merchant order/reference, notification URL, redirection URL; or legacy auth/order/payment-key/iframe for supported existing merchants. | Intention/client secret/checkout reference, or order ID/payment key/iframe URL. | `ProviderEvidence` with source `paymob`, reference `paymob:intention:{id}` or `paymob:order:{id}`, action correlation, payload reference, evidence hash, checkout handoff metadata. | Admission, verification, decision, transition, transaction, and settlement stages remain unchanged. |
| Create embedded checkout | Intention Creation API for Pixel checkout. | Client secret/reference and public initialization data. | Evidence with embedded checkout handoff metadata, no secret leakage into Runtime. | Existing action/evidence model supports embedded checkout. |
| Webhook/callback success | HMAC-authenticated processed transaction callback. | Transaction ID, order ID, success flags/status, amount, currency, integration ID, source data. | `EvidenceType.Webhook` or `Callback`, `EvidenceStatus.Admitted`, provider/transaction/order references, amount/currency metadata, HMAC verification result. | Existing Runtime admits verified provider evidence and produces canonical outcome; application validates exact required result before lifecycle writing. |
| Webhook/callback failure or decline | HMAC-authenticated negative transaction callback. | Decline/failure/error flags, transaction/order references. | Evidence plus `CanonicalProviderError` metadata; failure reason maps to provider rejection/decline. | Existing Runtime can reject/fail; lifecycle remains application-owned. |
| Refund | Refund API or refund webhook. | Refund transaction status/amount. | `EvidenceType.RefundEvidence` or `RefundReport`, amount/currency, parent transaction reference. | Existing refund lifecycle authority handles accepted refund evidence. |
| Partial refund | Refund API with amount or webhook showing cumulative/refunded amount. | Refunded amount/status. | Partial refund evidence with amount delta/currency and provider transaction reference. | Existing partial refund status and transaction artifact model are sufficient. |
| Capture | Capture API for eligible authorized transaction. | Captured transaction state. | Provider notification evidence with operation `capture`, amount/currency, transaction reference. | Existing advanced-operation pattern is sufficient; activation needs separate application command certification. |
| Void/cancel | Void API for eligible authorization or terminal voided callback. | Voided/cancelled state. | Provider notification evidence with operation `void` or terminal cancellation evidence. | Existing cancellation/void status handling is sufficient. |
| Inquiry | Transaction Inquiry API by transaction, order, or special reference. | Current transaction status and references. | `EvidenceType.StatusInquiry`, provider status metadata, evidence hash. | Existing status refresh/inquiry action is sufficient. |

Compatibility conclusion: Paymob operations can be translated to existing canonical `PaymentAction`, `ProviderEvidence`, `PaymentMetadata`, and Runtime result concepts. Paymob does not require a new Runtime status, stage, transition, transaction artifact, or settlement artifact.

## Provider Evidence Mapping

Paymob provider-specific DTOs and wire payloads must remain inside the adapter. Canonical evidence leaving the adapter should include only normalized fields:

| Paymob fact | Canonical location |
|---|---|
| Paymob provider name | `EvidenceSource` / metadata value `paymob` |
| Intention ID or client secret reference | `EvidenceReference` and protected provider metadata; client secret is treated as sensitive handoff material. |
| Legacy order ID | `EvidenceReference` as `paymob:order:{id}` and metadata `provider_order_id`. |
| Transaction ID | metadata `transaction_reference` and payload reference `paymob:transaction:{id}`. |
| Merchant order/reference/special reference | correlation metadata and idempotency/replay key input. |
| Amount/currency | evidence metadata and Runtime action/session amount comparison input. |
| Success/pending/error/void/refund/capture flags | normalized external status metadata. |
| Integration ID | metadata for tenant/account/method verification. |
| Payment method/source subtype | bounded metadata; no PAN or sensitive source data. |
| HMAC verification result | metadata boolean or reason; raw HMAC is not persisted. |
| Raw payload | protected `ProviderPayloadReference` or encrypted audit reference outside Runtime. |
| Payload hash | `EvidenceHash`. |

`ProviderMetadata` should contain only non-secret provider identifiers, normalized statuses, operation names, account/method facts, amount/currency, and protected artifact references. Payment keys, token-bearing iframe URLs, raw HMAC values, API keys, secret keys, auth tokens, and reusable checkout credentials must not appear in plaintext metadata.

## Error Mapping

| Paymob failure | Canonical provider error | PaymentFailureReason / reason code | Retry or ambiguity guidance |
|---|---|---|---|
| Authentication failure, invalid API/secret key, invalid auth token | `Authentication` | `AuthenticationFailed` | Non-retry until credentials fixed. |
| Authorization failure, account/method not entitled | `Authorization` | `AuthorizationFailed` | Non-retry until tenant/provider configuration fixed. |
| Invalid request, malformed amount/currency/integration ID | `Validation` | `InvalidRequest` | Non-retry after application validation failure. |
| Duplicate request or merchant reference conflict | `DuplicateRequest` or `InvalidState` | `DuplicateRequest` / `InvalidState` | Collapse through durable idempotency/replay; do not recreate blindly. |
| Timeout before provider acceptance known | `Timeout` | `ProviderTimeout` | Ambiguous; route to checkpoint recovery/inquiry. |
| Provider 5xx or unavailable | `ProviderUnavailable` | `ProviderUnavailable` | Retry only through application policy and idempotent checkpoint. |
| Rate limit | `RateLimited` | `RateLimited` | Retry with governed backoff; no local success. |
| Declined payment | `ProviderRejected` | `ProviderDeclined` | Terminal negative evidence after HMAC/admission. |
| Fraud/risk rejection | `ProviderRejected` or `FraudReview` | `FraudSuspected` / `ProviderDeclined` | Fail or manual review according to application policy. |
| Expired payment/checkout | `Expired` | `Expired` | Terminal or retry-safe according to local SQL state. |
| Invalid state, already captured/refunded/voided | `InvalidState`, `AlreadyCaptured`, `AlreadyRefunded`, or `AlreadyVoided` | matching reason code | Non-retry unless inquiry proves recoverable state. |
| HMAC failure | `Authentication` or `InvalidSignature` | `InvalidSignature` | Reject and do not enter lifecycle authority. |
| Amount or currency mismatch | `Validation` / `Mismatch` | `AmountMismatch` / `CurrencyMismatch` | Quarantine/reconciliation; never promote to success. |
| Unsupported event or operation | `UnsupportedOperation` | `UnsupportedOperation` | Fail closed; capability/configuration issue. |
| Malformed callback/webhook payload | `Validation` | `MalformedProviderPayload` | Reject; preserve bounded audit. |

Every provider error must cross the adapter boundary only as canonical redacted error data. Paymob HTTP exceptions, SDK/wire objects, raw response bodies, authorization headers, keys, and signatures must not escape the adapter.

## Webhook Model

Paymob sends server-to-server Transaction Processed callbacks and browser Transaction Response callbacks. Public docs also call callbacks webhooks and require HMAC validation. Redirects are user experience only and must not be treated as lifecycle finality.

| Webhook concern | Compatibility finding |
|---|---|
| Event model | Transaction-centric. Key identities are transaction ID, order ID, merchant order/reference, integration ID, and status/flags. Compatible with canonical provider-event identity. |
| Signature validation | HMAC verification using merchant HMAC secret. Compatible with provider-specific webhook evidence adapter. |
| HMAC verification | Adapter recalculates HMAC from Paymob fields and compares with received HMAC. Runtime remains unaware. |
| Replay behavior | Paymob docs require idempotent handling; platform replay store can use tenant + provider + transaction/event/reference + payload hash. |
| Retry policy | Paymob docs expect safe retry/idempotency. Exact retry cadence should be environment-verified, but durable replay model is sufficient. |
| Event ordering | Webhook-before-redirect, redirect-before-webhook, duplicate, late, and out-of-order events are admissible through existing outcome admission and prior-history verification. |
| Terminal events | Success/captured, declined/failed, voided/cancelled, refunded/partially refunded, expired where reported. |
| Non-terminal events | Pending, authorized awaiting capture, customer-action/3DS, processing. These remain evidence, not finality. |
| Duplicate delivery | Existing durable replay and `PaymentWebhookLog` duplicate collapse are sufficient. |

Webhook conclusion: Paymob webhook semantics are compatible with the existing Runtime admission and replay model. The application layer must resolve tenant/configuration before verification, reserve durable replay identity, verify HMAC, classify event support, and validate exact Runtime outcome before lifecycle mutation.

## Security Review

| Security area | Compatibility decision |
|---|---|
| Secret storage | Compatible. API key, secret key, public key, integration IDs, iframe IDs, and HMAC secret belong in encrypted tenant/provider configuration. |
| Credential isolation | Compatible through tenant-scoped configuration and one-shot adapter handoff. No adapter credential resolution is allowed. |
| Tenant isolation | Compatible. Tenant identity must scope configuration, payment intent/session, order reference, webhook replay, and provider account/integration IDs. |
| HMAC verification | Compatible. Paymob HMAC is provider-specific primitive inside webhook evidence adapter. |
| Replay prevention | Compatible. Use durable tenant/provider/event uniqueness plus Runtime prior-evidence rejection. |
| Secret rotation | Compatible. Rotation creates new tenant configuration for future operations; prior evidence remains immutable. |
| Logging restrictions | Compatible if logs omit keys, auth tokens, payment keys, token-bearing URLs, raw HMAC, raw payloads, PAN/source data, and client secrets. |
| Sensitive field handling | Compatible. Reusable checkout material must be encrypted or transient; raw webhook payloads need encryption or protected audit references; signatures should be digested, not persisted raw. |
| Card data | Compatible. Hosted checkout/Pixel keep PCI-sensitive entry with Paymob; platform stores no PAN. |
| Public key | Public key may be client-visible only for allowed checkout initialization and never authorizes payment alone. |

Security conclusion: Paymob fits the platform security model. The adapter implementation must preserve the Closure 5 artifact-protection posture for any new Intention/Unified Checkout artifacts.

## Operational Review

| Operational concern | Compatibility decision |
|---|---|
| Metrics | Compatible. Provider-specific meter/tags can record attempts, successes, failures, webhook receipt, replay rejection, runtime admission/rejection, refund/capture/void/inquiry, and latency. |
| Logging | Compatible with redacted, correlation-aware, low-cardinality logs. |
| Correlation IDs | Compatible through canonical action correlation, merchant reference, Paymob transaction/order/intention ID, and request trace IDs. |
| Telemetry | Compatible. Provider-specific telemetry remains outside Runtime and uses bounded labels. |
| Replay Store | Compatible. Existing durable replay model can key by tenant/provider/event/transaction/reference/hash. |
| Payment Timeline | Compatible. Timeline consumes lifecycle/order events, not Paymob wire events directly. |
| Alerting | Compatible. Alerts can be provider-specific but do not require observability redesign. |
| Dashboarding | Compatible. Dashboards show provider metrics and lifecycle outcomes from existing telemetry/events. |
| Sandbox/live validation | Source compatibility is separate from live certification. Official credentials, tenant enablement, public callback/webhook, signed delivery, and live drills remain required before deployment/live certification. |

Operational conclusion: No observability redesign is required. Paymob requires provider-specific operational artifacts and live drills, not Runtime changes.

## Adapter Boundary

The Paymob adapter boundary may:

- communicate with Paymob official HTTPS APIs;
- authenticate using credential material supplied by the application handoff;
- create payment intentions or legacy order/payment-key sessions;
- create hosted checkout, Pixel, or legacy iframe handoff evidence;
- verify Paymob HMAC signatures;
- translate Paymob requests, responses, webhooks, callbacks, inquiry, refund, partial refund, capture, and void responses;
- translate provider errors to canonical provider errors;
- report capabilities and constraints per tenant/account/region/method.

The Paymob adapter boundary must not:

- modify Orders;
- execute Workflow;
- mutate Lifecycle;
- publish Events;
- access SQL or EF Core;
- access Runtime internals or call Runtime stages directly;
- resolve tenant configuration or credentials;
- cache or rotate credentials;
- know Storefront, CommerceHub, POS Admin, Affiliate, MassTransit, RabbitMQ, SignalR, dashboards, or frontend DTOs;
- decide business eligibility, fraud policy, refund policy, capture policy, retry policy, or order lifecycle;
- leak Paymob DTOs, exceptions, secrets, HMAC values, raw payloads, or SDK/wire types into canonical contracts.

## Known Differences From Stripe

| Area | Stripe | Paymob | Architecture effect |
|---|---|---|---|
| Primary object | PaymentIntent | Payment Intention or legacy order/payment key/transaction | Adapter mapping only. |
| Checkout handoff | Client secret / Stripe.js | Unified Checkout redirect, Pixel client secret/reference, or legacy iframe/payment key | Handoff evidence only. |
| Authentication | Secret/restricted key, publishable key, webhook secret | API key, secret key, public key, integration IDs, HMAC secret | Tenant credential model only. |
| Region | Stripe account/API region abstraction | Region-specific host and account; no global account | Tenant provider configuration only. |
| Webhook signature | Stripe signature header and SDK verification | HMAC query/field verification | Webhook evidence adapter only. |
| Idempotency | Explicit Stripe idempotency key | Merchant references, provider identities, platform replay/idempotency; explicit outbound idempotency must be verified per API | Application checkpoint/replay; no Runtime change. |
| Method enablement | Account/payment method configuration | Account manager/dashboard enabled methods and Integration IDs | Capability admission only. |
| Current API direction | PaymentIntent remains native | Intention API plus Unified Checkout/Pixel; iframe is legacy/new-setup sensitive | Adapter migration decision only. |

## Risks

| Risk | Severity | Disposition |
|---|---|---|
| Legacy iframe flow is not the preferred current Paymob API direction. | Medium | Implement PAYMOB-PROVIDER-1 against Intention/Unified Checkout unless an explicit legacy merchant requirement exists. |
| Account/method entitlements vary by region and merchant. | Medium | Capability reporting must be tenant/account-specific. |
| Paymob outbound idempotency guarantees vary by endpoint and must be confirmed. | Medium | Use platform durable checkpoints and provider inquiry; do not blindly recreate provider sessions. |
| Live webhook retry/order behavior needs real environment evidence. | Medium | Sandbox validation gate required. |
| Public docs sometimes use callback and webhook terms interchangeably. | Low | Treat all inbound Paymob state changes as provider evidence requiring HMAC, replay, and admission. |
| Meeza support was not independently proven from the reviewed current overview. | Low | Do not advertise Meeza until account/API docs or merchant enablement prove it. |
| Advanced operations are method/status/account dependent. | Medium | Keep adapter translation separate from active command certification. |

## Recommendations

1. Proceed to `PAYMOB-PROVIDER-1` as a provider-adapter implementation phase, not a platform redesign phase.
2. Prefer Paymob Intention API plus Unified Checkout as the new implementation target; keep legacy iframe only behind explicit tenant capability where required.
3. Model Paymob region, environment, integration IDs, secret key, public key, API key, HMAC secret, checkout mode, and method capabilities as tenant configuration.
4. Enforce one-shot credential handoff and forbid adapter credential lookup or global fallback.
5. Treat callbacks, redirects, and webhooks as evidence only; final lifecycle mutation must remain behind Runtime outcome admission and `PaymentLifecycleService`.
6. Certify advanced operations as translation first; activate capture/refund/partial refund/void/inquiry only through separate application-command certification.
7. Require live Sandbox drills before deployment certification: success, decline, expiration/cancel, duplicate webhook, invalid HMAC, delayed webhook, refund, partial refund, void, capture, inquiry, timeout, retry, tenant isolation, and latency.

## Certification Questions

| Question | Answer |
|---|---|
| Can Paymob fit the certified Runtime unchanged? | **Yes.** Paymob maps to canonical actions, evidence, metadata, and errors without Runtime changes. |
| Can Paymob reuse the Lifecycle unchanged? | **Yes.** Lifecycle remains the single durable writer and consumes admitted canonical outcomes. |
| Can Paymob reuse the Workflow unchanged? | **Yes.** Paymob provider protocol does not require Workflow changes. |
| Can Paymob reuse the Dispatcher unchanged? | **Yes.** Dispatcher remains canonical intent routing only. |
| Can Paymob reuse the Intent Infrastructure unchanged? | **Yes.** Provider-specific behavior remains before canonical evidence or after application-owned lifecycle admission, not inside handlers. |
| Can Paymob reuse the Order Domain unchanged? | **Yes.** Paymob never mutates Orders directly; Order Domain changes flow through existing payment/lifecycle/workflow outcomes and event pipeline. |
| Can Paymob reuse the Event Pipeline unchanged? | **Yes.** Existing Order Domain and payment lifecycle events remain authoritative. Paymob events are provider evidence, not domain events. |
| Can all Paymob-specific behavior remain inside the Adapter? | **Yes.** Paymob API calls, HMAC, request/response mapping, provider errors, capabilities, and protocol DTOs can terminate inside adapter/evidence boundaries. |
| Are existing Runtime contracts sufficient? | **Yes.** `PaymentAction`, `ProviderEvidence`, evidence metadata, provider/session references, provider capabilities, and Runtime results are sufficient. |
| Are webhook semantics compatible with the existing replay model? | **Yes.** HMAC-authenticated transaction callbacks can be keyed by tenant, provider, transaction/order/reference identity, and payload hash. Duplicate/late/out-of-order delivery is handled by existing admission and replay rules. |
| Does Paymob require any architectural redesign? | **No.** Paymob requires provider adapter implementation, tenant configuration, credential handoff, evidence/error translation, and operational validation only. |
| Is the architecture certified for `PAYMOB-PROVIDER-1` implementation? | **Yes.** `PAYMOB-PROVIDER-1` may proceed as adapter implementation under the frozen platform constraints and scoped POS Admin + Storefront boundary. |

## Final Certification Decision

Compatibility confirmed.

Paymob can be implemented entirely as a provider adapter while reusing the certified Payment Platform unchanged. Any future discovery that appears to require Runtime, Lifecycle, Workflow, Dispatcher, Intent Infrastructure, Order Domain, Event Pipeline, Provider Contract, or Canonical Runtime Contract changes must stop implementation and open an architecture review instead of modifying the frozen platform.
