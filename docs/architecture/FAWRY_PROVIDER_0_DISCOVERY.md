# FAWRY-PROVIDER-0 Discovery and Compatibility Certification

Date: 2026-07-17

Status: DISCOVERY COMPLETE - COMPATIBILITY CONFIRMED FOR PROVIDER ADAPTER IMPLEMENTATION

Scope: Fawry provider discovery against the certified Shopixy Payment Platform.

Excluded: implementation, Runtime redesign, Lifecycle redesign, Workflow redesign, Workflow Execution Service redesign, Dispatcher redesign, Intent Handler redesign, Order Domain redesign, Event Pipeline redesign, Provider Contract redesign, and Canonical Runtime Contract changes.

## Executive Summary

Fawry can fit the certified Payment Platform as a pure provider adapter. No frozen platform component requires redesign for Fawry.

The compatible shape is:

```text
Trusted Payment Application Service
  -> tenant Fawry configuration and one-shot credential handoff
  -> Fawry Provider Adapter
  -> Fawry HTTPS APIs / SHA-256 signature primitive
  -> canonical ProviderEvidence or CanonicalProviderError
  -> existing PaymentRuntimeOrchestrator
  -> existing PaymentLifecycleService
  -> existing Workflow, Order Domain, and Event Pipeline
```

Fawry differs from Stripe and Paymob only at the provider protocol layer. Stripe is PaymentIntent/client-secret shaped. Paymob is intention/checkout or legacy order/payment-key/iframe shaped. Fawry is merchant-reference and Fawry-reference shaped, with SHA-256 ordered-field signatures, reference-number retail/cash payments, hosted checkout, card 3DS, wallet QR/R2P, installments, status pull, refund, capture, authorization cancel, unpaid-order cancellation, and Server Notification V2.

These differences can terminate inside the Fawry adapter, webhook evidence adapter, capability provider, and application-owned tenant credential handoff. No Runtime, Lifecycle, Workflow, Workflow Execution Service, Dispatcher, handler, Order Domain, Event Pipeline, provider contract, or canonical Runtime contract modification is required by this discovery.

## Mandatory Reading Basis

Reviewed local certified records:

| Record | Discovery use |
|---|---|
| `PAYMENT_REFERENCE_FREEZE.md` | Controlling frozen architecture, provider adapter rules, replay, credential handoff, and single-writer rules. |
| `PAYMENT_0_PLATFORM_ARCHITECTURE_CERTIFICATION.md` | Payment authority, lifecycle ownership, session model, transaction model, webhook ownership, and security boundaries. |
| `PAYMENT_END_TO_END_1_CERTIFICATION.md` | Workflow output, execution service, dispatcher, handlers, Order Domain, SQL, and event propagation boundaries. |
| `STRIPE_PROVIDER_1.md` | Reference adapter pattern for provider isolation, canonical evidence/error mapping, capability reporting, and webhook verification. |
| `PAYMOB_PROVIDER_0_DISCOVERY.md` | Comparable canonical discovery structure and non-Stripe provider compatibility analysis. |
| `PAYMOB_PROVIDER_1.md` | Reference implementation boundary for an external provider adapter against frozen runtime contracts. |
| `PAYMENT_0_5C_FAWRY_DISCOVERY.md` | Prior Fawry official API discovery, signature findings, capability inventory, and implementation gaps. |
| `FAWRY_2_PROVIDER_ADAPTER.md` | Existing Fawry translation-only adapter evidence for official charge initialization. |
| `FAWRY_4_WEBHOOK_EVIDENCE_ADAPTER.md` | Existing Fawry Server Notification V2 signature and evidence adapter evidence. |
| `FAWRY_5_ADVANCED_OPERATIONS.md` | Existing Fawry refund, partial refund, capture, void, and inquiry translation evidence. |

Reviewed Fawry public documentation:

| Source | Key facts |
|---|---|
| FawryPay Developer Guide | FawryPay exposes server APIs, Express/hosted checkout, card tokenization, plugins, Pay By Link, payment split, Postman collections, testing, and error-code documentation. |
| Reference-number charge API | `POST /ECommerceWeb/Fawry/payments/charge` creates PayAtFawry references using `merchantCode`, `merchantRefNum`, customer fields, amount/currency, expiry, charge items, optional order webhook URL, and SHA-256 signature. |
| Card / 3DS APIs | Fawry supports card payment and 3DS data returned in notifications where applicable. |
| Auth/capture APIs | Fawry supports authorization, capture, and cancel authorization for eligible card flows. |
| E-wallet APIs | Fawry supports mobile wallet payment modes including QR and request-to-pay style flows using `MWALLET`. |
| Bank installments / MyFawry Taqseet | Fawry supports installment surfaces subject to merchant/account enablement. |
| Refund API | `POST /ECommerceWeb/Fawry/payments/refund` supports full and partial refunds of paid/captured amounts using `merchantCode`, Fawry `referenceNumber`, refund amount, optional reason, and SHA-256 signature. |
| Cancel unpaid order API | Fawry supports cancellation of unpaid orders; paid orders require refund rather than unpaid cancellation. |
| Get Payment Status V2 | `GET /ECommerceWeb/Fawry/payments/status/v2` retrieves payment status by `merchantCode`, `merchantRefNumber`, and SHA-256 signature. |
| Server Notification V2 | Fawry posts status changes to merchant callback URLs, with `messageSignature` over ordered fields plus secure key; HTTP 200 marks delivery, non-200 triggers configured retries. |

## Provider Overview

FawryPay is a payment platform centered on Egypt's Fawry retail/cash collection network and online payment APIs. Public documentation exposes multiple integration products:

| Product surface | Discovery finding | Adapter relevance |
|---|---|---|
| Reference-number / PayAtFawry | Generates a Fawry reference that customers pay at Fawry POS retail locations. | Primary candidate for cash/reference checkout. |
| Hosted checkout / Express checkout | Redirect or embedded checkout surfaces for customer action. | Maps to canonical handoff evidence. |
| Card / 3DS | Server card payment APIs with 3DS-related notification fields when applicable. | Optional card capability; not required to redesign Runtime. |
| Mobile wallet / QR / R2P | E-wallet APIs using wallet-specific request fields and `MWALLET`. | Optional wallet/QR capability; adapter-local mapping. |
| Bank installments / MyFawry Taqseet | Installment surfaces are documented. | Capability must be tenant/account-specific. |
| Refund | Full and partial refund API. | Maps to canonical refund/partial refund evidence. |
| Authorization/capture/cancel auth | Card authorization and later capture/cancel. | Maps to canonical authorize/capture/void evidence. |
| Cancel unpaid order | Cancels unpaid orders before payment. | Maps to cancel/expiration evidence where product command is certified. |
| Status V2 pull | Pulls transaction status by merchant reference. | Maps to status inquiry/recovery evidence. |
| Server Notification V2 | Pushes status changes to merchant callback URL. | Maps to webhook/callback evidence. |

Supported countries: public developer material and reference-payment examples are Egypt-oriented, using FawryPay POS retail stores and EGP examples. Treat Egypt as the confirmed country for this discovery. Any additional country support must be tenant/account evidence, not a Runtime assumption.

Merchant onboarding: merchants register with FawryPay and coordinate account setup with Fawry operations. Merchant code, secure key/signature key, callback URLs for development and production, payment method entitlements, expiration defaults, and production activation are account setup facts.

Sandbox vs Production: Fawry public documentation distinguishes staging/development endpoint usage from production endpoint usage. Staging examples use `https://atfawry.fawrystaging.com`. Production endpoint values are environment-specific and must be configured per tenant/provider environment. Mode is a tenant configuration fact, not a Runtime fact.

SDKs and tooling: public docs include JavaScript/React examples, server-language snippets, plugins, hosted/Express checkout, Postman/testing material, and server APIs. The certified platform should use official HTTPS APIs and signature primitives inside backend adapters; browser SDKs or hosted checkout outputs may be emitted only as transient handoff evidence.

API versioning strategy: Fawry uses endpoint families and named V2 status/notification APIs rather than a Stripe-style pinned API-version header. Adapter configuration must explicitly select endpoint family, environment, and operation contract. Runtime contracts remain version-neutral.

## Authentication

| Fawry credential or authentication item | Fawry behavior | Existing abstraction mapping | Runtime impact |
|---|---|---|---|
| `merchantCode` | Merchant identifier provided during account setup and included in API requests. | Tenant provider credential/configuration; explicit one-shot adapter handoff. | None. |
| Secure key / signature key | Server-side secret used to compute request, response, notification, refund, capture, cancel, and status signatures. | Tenant secret stored encrypted and passed only to adapter/verifier for one operation. | None. |
| API key | Prior project naming may use `apiKey` as a secure-key alias; official docs emphasize secure key/signature material. | Configuration naming concern inside tenant provider setup; adapter should consume a canonical secret input. | None. |
| Request signature | SHA-256 of ordered operation-specific fields plus secure key. | Adapter-local request signing primitive. | None. |
| Response signature | Some responses include signatures over response fields plus secure key. | Adapter verifies before mapping to `ProviderEvidence` where supplied. | None. |
| Callback/webhook authentication | Server Notification V2 uses `messageSignature` SHA-256 over ordered fields plus secure key. | Webhook evidence adapter verifies with fixed-time comparison after tenant resolution. | None. |
| Credential rotation | New secure key/merchant config applies to future operations; old evidence remains immutable. | Existing tenant credential authority and one-shot handoff. | None. |
| Tenant-specific credentials | Merchant code, secure key, callback URLs, and method entitlements are merchant/account-specific. | Existing tenant payment-method configuration and capability admission. | None. |

Credential conclusion: Fawry maps cleanly to the certified tenant configuration and one-shot credential handoff model. The adapter must not resolve credentials, read SQL, cache credentials, or fall back to global credentials.

## Capability Matrix

| Capability | Native Fawry support | Canonical mapping | Adapter responsibility | Runtime impact |
|---|---|---|---|---|
| Card Payments | Yes, including 3DS-related flow details. | Authorization/sale or customer-action evidence; 3DS facts remain metadata. | Create card request, sign, map response/status/3DS fields. | None. |
| Cash Payments | Yes via PayAtFawry reference number. | Pending customer-action/reference evidence; terminal callback/status evidence. | Create reference charge, expose reference/expiry as handoff metadata, map payment status. | None. |
| QR Payments | Yes through wallet/QR surfaces. | Customer-action QR evidence and later terminal provider evidence. | Generate wallet/QR request, map QR/reference/status facts. | None. |
| Wallet Payments | Yes through mobile wallet / `MWALLET`. | Wallet customer-action and async confirmation evidence. | Translate wallet request, R2P/QR response, terminal events. | None. |
| Installments | Yes through bank installments and MyFawry Taqseet surfaces, entitlement-dependent. | Installment capability plus payment evidence. | Report only when tenant/account proves enablement; map installment metadata. | None. |
| Authorization | Yes for eligible card flows. | Authorized evidence and lifecycle-owned authorization state. | Translate auth request/response/status. | None. |
| Capture | Yes for authorized payments. | Capture operation evidence. | Translate capture request/response; do not decide capture policy. | None. |
| Void | Yes as cancel authorization for eligible authorization. | Void/cancel evidence. | Translate cancel authorization response. | None. |
| Cancel | Yes for unpaid orders; paid orders use refund. | Cancel/expired evidence where canonical command is admitted. | Translate unpaid-order cancel; reject unsupported states. | None. |
| Refund | Yes for paid/captured payments. | Refund evidence. | Translate refund request/response and refund notifications. | None. |
| Partial Refund | Yes via refund amount below paid amount. | Partial refund evidence with amount/currency. | Translate amount-bound refund and provider restrictions. | None. |
| Payment Inquiry | Yes via Get Payment Status V2. | Status inquiry/recovery evidence. | Sign request, map status response. | None. |
| Recurring Payments | Not certified as a checkout capability by this discovery; Pay By Link/recurring invoice concepts may exist separately. | Future recurring/invoicing capability only after separate product certification. | Report unsupported unless official merchant entitlement and canonical command are separately certified. | None. |

Capability conclusion: Fawry capabilities fit the current provider capability and evidence model. Unsupported, unproven, or account-disabled features are adapter capability facts and application admission failures, not Runtime changes.

## Canonical Mapping

| Canonical Runtime action | Fawry request | Fawry response | Canonical ProviderEvidence | Existing Runtime behavior |
|---|---|---|---|---|
| Create PayAtFawry reference checkout | `POST /ECommerceWeb/Fawry/payments/charge` with `paymentMethod=PayAtFawry`, merchant/customer/amount/items/expiry/webhook URL, signature. | `ChargeResponse`, `referenceNumber`, `merchantRefNumber`, amount, fees, method, `orderStatus`, payment time, status code, response signature. | Evidence source `fawry`, evidence reference `fawry:reference:{referenceNumber}`, merchant reference, amount/currency, payment method, status, expiry, payload hash, handoff reference metadata. | Existing admission, verification, decision, transition, transaction, and settlement stages remain unchanged. |
| Create hosted/Express checkout | Charge/hosted checkout request with return URL and signed merchant fields. | Redirect URL/session/reference and status response. | Handoff evidence with redirect/session metadata, no secret leakage. | Existing customer-action evidence model is sufficient. |
| Create card/3DS payment | Fawry card or 3DS request with signed merchant fields. | Authorization/payment response and possible 3DS session/status metadata. | Evidence containing provider reference, canonical status, 3DS metadata, amount/currency. | Runtime remains provider-neutral. |
| Create wallet/QR/R2P payment | Fawry wallet request with wallet method-specific fields. | QR/R2P/reference response and later status callback. | Wallet customer-action evidence and terminal callback/status evidence. | Existing async evidence flow is sufficient. |
| Webhook/callback success | Server Notification V2 POST with `messageSignature`. | `orderStatus=PAID` or `CAPTURED`, references, amount, method, optional 3DS/installment fields. | `EvidenceType.Webhook` or `Callback`, admitted evidence, verified signature metadata, references, amount/currency/method. | Existing replay and lifecycle admission model handles terminal success. |
| Webhook/callback negative | Server Notification V2 POST with `FAILED`, `EXPIRED`, or `CANCELED`. | Failure/cancellation/expiration facts and optional failure code/reason. | Evidence plus `CanonicalProviderError` metadata; failure reason maps to canonical failure. | Existing failure/expiration/cancel outcomes are sufficient. |
| Refund | `POST /ECommerceWeb/Fawry/payments/refund` with reference number, refund amount, optional reason, signature. | Status code/description and later notification/status update. | Refund evidence with provider reference, amount, reason, response status. | Existing refund lifecycle authority handles accepted refund evidence. |
| Partial refund | Same refund API with partial amount. | Status code/description and `PARTIAL_REFUNDED` notification/status where applicable. | Partial refund evidence with amount delta, currency, parent reference. | Existing partial-refund lifecycle model is sufficient. |
| Capture | Fawry capture API with merchant reference, optional capture amount, merchant code, signature. | Captured status or provider diagnostic. | Capture evidence with amount/currency and provider reference. | Existing advanced-operation pattern is sufficient. |
| Void / cancel authorization | Fawry cancel authorization API with merchant reference, merchant code, signature. | Cancelled/voided response. | Void/cancel evidence. | Existing cancellation/void handling is sufficient. |
| Cancel unpaid order | Fawry cancel unpaid order API. | Cancelled/unpaid-order response. | Cancel evidence only when command is admitted and provider state is unpaid. | Existing lifecycle remains authority. |
| Inquiry | `GET /ECommerceWeb/Fawry/payments/status/v2` by merchant reference and signature. | `PaymentStatusResponse`, provider reference, payment status, method, amount, dates, status code. | `EvidenceType.StatusInquiry`, provider status metadata, references, evidence hash. | Existing status refresh/recovery action is sufficient. |

Compatibility conclusion: Fawry operations can be translated to existing canonical `PaymentAction`, `ProviderEvidence`, metadata, provider errors, capability reports, and Runtime result concepts. Fawry does not require a new Runtime status, stage, transition, transaction artifact, settlement artifact, workflow intent, dispatcher behavior, handler behavior, order mutation path, or event pipeline path.

## Provider Evidence Mapping

Fawry provider DTOs and wire payloads must remain inside the adapter. Canonical evidence leaving the adapter should include only normalized, non-secret fields:

| Fawry fact | Canonical location |
|---|---|
| Provider name | `EvidenceSource` / metadata value `fawry`. |
| Fawry `referenceNumber` / `fawryRefNumber` | `EvidenceReference`, provider reference metadata, payload reference. |
| `merchantRefNum` / `merchantRefNumber` | Merchant correlation metadata and idempotency/replay identity input. |
| `paymentReferenceNumber` / documented misspelling `paymentRefrenceNumber` | Payment reference metadata. |
| `requestId` | Provider event id metadata for callbacks/status evidence. |
| `orderStatus` / `paymentStatus` | Provider status metadata plus normalized canonical-status metadata. |
| `statusCode` / `statusDescription` / `failureErrorCode` / `failureReason` | Bounded diagnostic metadata and canonical error classification. |
| Amounts and fees | `paymentAmount`, `orderAmount`, `fawryFees`, `shippingFees` as non-authoritative metadata for Runtime/application amount checks. |
| Currency | Metadata from request/response where present; application still validates expected currency. |
| `paymentMethod` | Bounded payment method metadata such as `PAYATFAWRY`, `CARD`, `MWALLET`, `VALU`. |
| Expiry/payment time | Evidence timestamp or metadata for expiration/recovery decisions. |
| 3DS / installment fields | Bounded provider metadata only; no Runtime-specific fields. |
| Signature verification result | Boolean/classification metadata; raw signature is not persisted. |
| Raw payload | Protected payload reference or encrypted audit reference outside Runtime. |
| Payload hash | `EvidenceHash`. |

Canonical status metadata mapping:

| Fawry status | Canonical metadata status |
|---|---|
| `NEW`, `UNPAID`, `PENDING` | `AwaitingCustomer` |
| `PAID`, `CAPTURED` | `Completed` |
| `AUTHORIZED` | `Authorized` |
| `CANCELED`, `CANCELLED` | `Cancelled` |
| `EXPIRED` | `Expired` |
| `FAILED`, `DECLINED` | `Failed` |
| `REFUNDED` | `Refunded` |
| `PARTIAL_REFUNDED`, `PARTIALLY_REFUNDED` | `PartiallyRefunded` |
| Unknown | `ReconciliationRequired` with fail-closed handling |

`ProviderMetadata` should contain only non-secret provider identifiers, normalized statuses, operation names, method facts, amount/currency, verification outcome, and protected artifact references. Merchant secure keys, request signatures, response signatures, raw callback signatures, API keys, card data, reusable token material, and token-bearing URLs must not appear in plaintext metadata.

## Runtime Compatibility

| Frozen component | Compatibility finding |
|---|---|
| Payment Runtime | Compatible unchanged. Fawry facts become canonical evidence/errors before Runtime admission. |
| Payment Lifecycle | Compatible unchanged. Lifecycle remains the single durable writer after exact Runtime outcome admission. |
| Payment Workflow | Compatible unchanged. Provider protocol does not require workflow intent changes. |
| Payment Workflow Execution Service | Compatible unchanged. It consumes canonical workflow output only and has no provider dependency. |
| Payment Intent Dispatcher | Compatible unchanged. It routes canonical intent types only. |
| Payment Intent Handlers | Compatible unchanged. Handlers mutate Order Domain only through canonical intents. |
| Order Domain | Compatible unchanged. Fawry never mutates orders directly. |
| Event Pipeline | Compatible unchanged. Fawry notifications are evidence, not domain events. |
| Provider Contracts | Compatible unchanged. Existing action, evidence, error, metadata, and capability contracts are sufficient. |
| Canonical Runtime Contracts | Compatible unchanged. No Fawry DTO, signature, status, endpoint, or credential becomes a Runtime contract. |

No architecture review is required because discovery did not identify a necessary frozen-component change. If future implementation discovers an operation that cannot be expressed through existing canonical actions/evidence/errors, implementation must stop and open an architecture review before any platform modification.

## Error Mapping

| Fawry failure | Canonical provider error | PaymentFailureReason / reason code | Retry or ambiguity guidance |
|---|---|---|---|
| Authentication failure, invalid merchant code, invalid secure key | `Authentication` | `AuthenticationFailed` | Non-retry until credentials are fixed. |
| Authorization failure, merchant/method not enabled | `Authorization` | `AuthorizationFailed` | Non-retry until tenant/provider setup is fixed. |
| Invalid request, missing required field, bad amount format, bad currency, invalid method | `Validation` | `InvalidRequest` | Non-retry after validation correction. |
| Duplicate merchant reference or already existing provider order | `DuplicateRequest` or `InvalidState` | `DuplicateRequest` / `InvalidState` | Collapse through durable idempotency and status inquiry. |
| Timeout before provider acceptance is known | `Timeout` or `Network` | `ProviderTimeout` / `NetworkFailure` | Ambiguous; route to checkpoint recovery/status inquiry. |
| Provider 5xx or unavailable | `ProviderUnavailable` | `ProviderUnavailable` | Retry only through governed application policy and idempotent checkpoint. |
| Declined card/wallet/installment payment | `ProviderRejected` | `ProviderDeclined` | Terminal negative evidence after signature/admission. |
| Expired reference/order | `Expired` | `Expired` | Terminal or retry-safe according to local SQL state and product policy. |
| Invalid state: not paid for refund, refund greater than paid, paid order cancellation | `InvalidState` | `InvalidState` / `AmountMismatch` | Non-retry unless inquiry proves recoverable state. |
| Signature failure, `Wrong Signature`, invalid `messageSignature` | `InvalidSignature` or `Authentication` | `InvalidSignature` | Reject; no lifecycle authority. |
| Network failure | `Network` | `NetworkFailure` | Ambiguous; recover via checkpoint/status inquiry. |
| Malformed response/payload | `Validation` | `MalformedProviderPayload` | Reject; preserve bounded audit. |
| Unsupported operation/method | `UnsupportedOperation` | `UnsupportedOperation` | Capability/configuration issue; fail closed. |
| Amount or currency mismatch | `Mismatch` or `Validation` | `AmountMismatch` / `CurrencyMismatch` | Quarantine/reconciliation; never promote to success. |

Every provider error must cross the adapter boundary only as canonical redacted error data. Fawry HTTP exceptions, raw response bodies, signatures, secure keys, card data, and provider DTOs must not escape the adapter.

## Callback/Webhook Model

Fawry Server Notification V2 posts transaction status changes to a merchant callback URL configured during merchant setup. Reference charge requests may also include `orderWebHookUrl` for order-level notifications. Redirects or browser returns are user experience only and must not be treated as lifecycle finality.

| Webhook concern | Compatibility finding |
|---|---|
| Callback flow | Fawry POST body -> tenant resolution from trusted payment/provider reference -> current tenant webhook credential -> `messageSignature` verification -> replay lookup/reservation -> webhook evidence adapter -> canonical evidence/error -> Runtime admission -> lifecycle writer. |
| Signature verification | SHA-256 over ordered Fawry fields plus secure key, fixed-time comparison. Compatible with provider-specific evidence adapter. |
| Retry behavior | Fawry treats HTTP 200 as delivered; non-200 responses trigger configured retries. Existing webhook audit/replay can absorb retries. |
| Duplicate delivery | Existing durable replay store can key by tenant, provider, request id, Fawry reference, merchant reference, status, and payload hash. |
| Event ordering | Webhook-before-redirect, redirect-before-webhook, duplicate, late, and out-of-order delivery are compatible with outcome admission and prior-history verification. |
| Terminal events | `PAID`, `CAPTURED`, `FAILED`, `EXPIRED`, `CANCELED`, `REFUNDED`, `PARTIAL_REFUNDED`. |
| Non-terminal events | `NEW`, pending/unpaid/customer-action, authorized awaiting capture, 3DS processing. These remain evidence, not finality. |
| Replay compatibility | Compatible. Fawry supplies stable references and payload signatures/hashes sufficient for durable replay identity. |

Webhook conclusion: Fawry callback/webhook semantics are compatible with the existing replay model. The application layer must resolve tenant/configuration before verification, reserve durable replay identity, verify `messageSignature`, classify event support, and validate exact Runtime outcome before lifecycle mutation.

## Security Review

| Security area | Compatibility decision |
|---|---|
| Secret storage | Compatible. Merchant code, secure key/signature key, environment endpoint, method entitlements, and callback URLs belong in encrypted tenant/provider configuration. |
| Merchant isolation | Compatible. Tenant identity must scope credentials, payment sessions, provider references, callback replay, and method capabilities. |
| Credential rotation | Compatible. Rotation affects future operations; prior evidence remains immutable and verified against configured credential policy. |
| Signature verification | Compatible. Request signing and response/webhook verification stay adapter-local. |
| Replay protection | Compatible. Durable replay plus Runtime prior-evidence rejection are sufficient. |
| Sensitive fields | Compatible if secure keys, signatures, card data, token material, raw payloads, and token-bearing URLs are redacted or protected. |
| Logging restrictions | Compatible with low-cardinality, correlation-aware logs that omit secrets and raw provider payloads. |
| PCI posture | Compatible when card entry/tokenization remains in Fawry/hosted surfaces or separate PCI-certified flow; platform stores no PAN. |
| Browser exposure | Compatible only for transient customer-action artifacts. Secure key and server signatures must never be displayed or shipped to clients. |

Security conclusion: Fawry fits the platform security model if all signing, verification, credential handling, and provider DTO handling remain inside adapter/application boundaries.

## Operational Review

| Operational concern | Compatibility decision |
|---|---|
| Metrics | Compatible. Provider-specific tags can record attempts, latency, outcomes, webhook receipt, signature failure, replay rejection, inquiry, refund, capture, cancel, and timeout. |
| Logging | Compatible with redacted structured logs and correlation ids. |
| Correlation IDs | Compatible through canonical action correlation, merchant reference, Fawry reference, request id, and trace ids. |
| Telemetry | Compatible. Provider labels remain bounded and outside Runtime internals. |
| Replay Store | Compatible. Existing replay model can key by tenant/provider/request/reference/status/hash. |
| Payment Timeline | Compatible. Timeline consumes admitted lifecycle/order events, not Fawry wire events directly. |
| Alerting | Compatible. Provider-specific alerts require configuration, not observability redesign. |
| Dashboards | Compatible. Existing payment/provider dashboards can add Fawry metrics without Runtime changes. |
| Sandbox/live validation | Not certified by this discovery. Official credentials, callback URLs, signed delivery, test scenarios, latency, and rollback remain separate operational gates. |

Operational conclusion: No observability redesign is required. Fawry needs provider-specific operational dashboards, alerts, runbooks, and sandbox/live drills, not Runtime changes.

## Adapter Boundary

The Fawry adapter may:

- communicate with official Fawry HTTPS APIs;
- authenticate and sign requests using explicit credential material supplied by the application handoff;
- verify official response and webhook signatures;
- create reference-number, hosted checkout, card, wallet, QR, installment, authorization, capture, cancel, refund, partial refund, and status inquiry requests where capabilities are enabled;
- translate Fawry requests, responses, callbacks, webhooks, and errors;
- translate provider statuses into canonical evidence metadata;
- translate provider failures into canonical provider errors and stable failure reasons;
- report capabilities and constraints by tenant, account, environment, method, and operation.

The Fawry adapter must not:

- modify Orders;
- execute Workflow;
- invoke Lifecycle;
- publish Events;
- access SQL or EF Core;
- access Runtime internals or call Runtime stages directly;
- resolve tenant configuration or credentials;
- cache or rotate credentials;
- know Storefront, CommerceHub, POS Admin, Affiliate, MassTransit, RabbitMQ, SignalR, dashboards, or frontend DTOs;
- decide business eligibility, fraud policy, refund policy, capture policy, retry policy, or order lifecycle;
- leak Fawry DTOs, exceptions, secure keys, API keys, signatures, raw payloads, card data, token material, or wire types into canonical contracts.

## Comparison With Stripe And Paymob

| Area | Stripe | Paymob | Fawry | Architecture effect |
|---|---|---|---|---|
| Primary payment object | PaymentIntent | Payment Intention or legacy order/payment key/transaction | Merchant reference plus Fawry reference/session/transaction | Adapter mapping only. |
| Checkout handoff | Client secret / Stripe.js | Unified Checkout, Pixel, or legacy iframe/payment key | PayAtFawry reference, hosted/Express redirect, QR/R2P, 3DS/session | Handoff evidence only. |
| Authentication | Secret/restricted key, publishable key, webhook secret | API key, secret key, public key, integration IDs, HMAC secret | Merchant code and secure/signature key using SHA-256 ordered-field signatures | Tenant credential model only. |
| Webhook signature | Stripe signature header and SDK verification | HMAC over Paymob field set | `messageSignature` SHA-256 over ordered Fawry fields plus secure key | Webhook evidence adapter only. |
| Async behavior | Webhooks and PaymentIntent status | Webhooks/callbacks and transaction inquiry | Reference payments, webhooks, status V2, delayed retail cash payment | Existing replay/admission model. |
| Advanced operations | Capture, refund, cancel through Stripe APIs | Capture, void, refund, inquiry where enabled | Refund, partial refund, capture, cancel auth, unpaid cancel, inquiry | Advanced adapter translation only. |
| Region/account setup | Stripe account/platform configuration | Regional hosts and integration IDs | Egypt-oriented Fawry merchant setup, staging/prod endpoints, callback URLs | Tenant/provider configuration only. |
| Idempotency | Explicit idempotency keys | Merchant references/provider identities plus platform replay | Stable `merchantRefNum` plus provider references/status inquiry | Application checkpoint/replay; no Runtime change. |
| Method enablement | Account/payment method configuration | Account manager/dashboard integration IDs | Fawry merchant method entitlement and account setup | Capability admission only. |

## Risks

| Risk | Severity | Disposition |
|---|---|---|
| Confirmed country support is Egypt-oriented; other geographies are not proven. | Medium | Advertise only tenant/account-proven country and currency support. |
| Fawry public docs contain inconsistent snippets and field spellings such as `paymentRefrenceNumber`. | Medium | Adapter must support documented wire variants while emitting one canonical metadata name. |
| SHA-256 signatures are field-order and amount-format sensitive. | High | Implement strict invariant formatting and provider-specific signature tests inside adapter. |
| Merchant callback URL is configured by Fawry operations and not always per request. | Medium | Treat callback URL as deployment/tenant setup evidence. |
| Method entitlements vary by merchant and environment. | Medium | Capability provider must be tenant/account-specific. |
| Reference/cash payments are asynchronous and can expire after local checkout. | Medium | Keep pending until verified terminal evidence or admitted expiration. |
| Duplicate charge creation with same merchant reference needs provider behavior confirmation. | Medium | Use durable checkpoints and status inquiry before retrying unknown outcomes. |
| Advanced operations are method/state-dependent. | Medium | Fail closed when operation is unsupported for current provider state. |
| Recurring/invoicing is not certified as current checkout. | Low | Keep unsupported until separate recurring command/product certification. |
| Sandbox/live behavior, retries, latency, callback ordering, and production credentials are not source-discoverable. | Medium | Require separate sandbox/live validation phase. |

## Recommendations

1. Proceed to `FAWRY-PROVIDER-1` as a provider-adapter implementation phase, not a platform redesign phase.
2. Implement Fawry reference-number PayAtFawry first if the product priority is cash/retail payment, because it is the most Fawry-native Shopixy checkout fit.
3. Model Fawry merchant code, secure key/signature key, environment, endpoint base URL, callback URL, method entitlements, expiration policy, and supported operations as tenant/provider configuration.
4. Enforce one-shot credential handoff and forbid adapter credential lookup, SQL access, global fallback, or credential caching.
5. Treat callbacks, redirects, reference displays, QR displays, and browser returns as evidence only; final lifecycle mutation must remain behind Runtime outcome admission and `PaymentLifecycleService`.
6. Verify all Fawry response and webhook signatures before translating positive provider facts into admitted evidence.
7. Activate refund, partial refund, capture, void/cancel, unpaid-order cancellation, and inquiry only through separate application-command certification if they become live commands.
8. Require sandbox drills before deployment certification: reference created, paid, expired, failed, duplicate callback, invalid signature, status pull, timeout recovery, refund, partial refund, capture, cancel authorization, unpaid cancel, wallet/QR if enabled, card/3DS if enabled, tenant isolation, and latency.

## Certification Questions

| Question | Answer |
|---|---|
| Can Fawry fit the certified Runtime unchanged? | **Yes.** Fawry maps to canonical actions, evidence, metadata, errors, and capability reports without Runtime changes. |
| Can Fawry reuse the Lifecycle unchanged? | **Yes.** Lifecycle remains the single durable writer and consumes only admitted canonical outcomes. |
| Can Fawry reuse the Workflow unchanged? | **Yes.** Fawry provider protocol does not require workflow changes. |
| Can Fawry reuse the Dispatcher unchanged? | **Yes.** Dispatcher remains canonical intent routing only. |
| Can Fawry reuse the Intent Infrastructure unchanged? | **Yes.** Provider-specific behavior remains before canonical evidence or after application-owned lifecycle admission, not inside handlers. |
| Can Fawry reuse the Order Domain unchanged? | **Yes.** Fawry never mutates Orders directly; Order Domain changes flow through existing payment/lifecycle/workflow outcomes and event pipeline. |
| Can Fawry reuse the Event Pipeline unchanged? | **Yes.** Existing Order Domain and payment lifecycle events remain authoritative. Fawry notifications are provider evidence, not domain events. |
| Can all Fawry-specific behavior remain inside the Adapter? | **Yes.** Fawry API calls, SHA-256 signatures, request/response mapping, provider errors, capabilities, statuses, and protocol DTOs can terminate inside adapter/evidence boundaries. |
| Are existing Runtime contracts sufficient? | **Yes.** `PaymentAction`, `ProviderEvidence`, provider metadata, provider/session references, canonical provider errors, failure reasons, and capability contracts are sufficient. |
| Are callback/webhook semantics compatible with the replay model? | **Yes.** Server Notification V2 can be keyed by tenant, provider, request id, Fawry reference, merchant reference, status, and payload hash; duplicate/late/out-of-order delivery is handled by existing admission and replay rules. |
| Does Fawry require any architectural redesign? | **No.** Fawry requires provider adapter implementation, tenant configuration, credential handoff, evidence/error translation, signature verification, and operational validation only. |
| Is the architecture certified for `FAWRY-PROVIDER-1` implementation? | **Yes.** `FAWRY-PROVIDER-1` may proceed as adapter implementation under the frozen platform constraints. |

## Final Certification Decision

Compatibility confirmed.

Fawry can be implemented entirely as a provider adapter while reusing the certified Payment Platform unchanged. Any future discovery that appears to require Runtime, Lifecycle, Workflow, Workflow Execution Service, Dispatcher, Intent Infrastructure, Order Domain, Event Pipeline, Provider Contract, or Canonical Runtime Contract changes must stop implementation and open an architecture review instead of modifying the frozen platform.
