# CERT-1 B6 Payment Completion & Recovery Certification

Certification: CERT-1
Phase: B
Control: B6 - Payment Completion & Recovery Certification
Scope: Payment completion and recovery only
Certification Date: 2026-07-20
Source of Truth: Source code implementation only

## Final Certification

PASS WITH OBSERVATIONS

## Source Evidence

- `PointOfSale.API/Controllers/PaymentWebhooksController.cs`
- `PointOfSale.API/Controllers/StripeWebhooksController.cs`
- `PointOfSale.API/Controllers/PaymobWebhooksController.cs`
- `PointOfSale.API/Controllers/FawryWebhookController.cs`
- `PointOfSale.API/Services/Payments/PaymentWebhookRequestPipeline.cs`
- `PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs`
- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.API/Services/Payments/CheckoutPaymentIntentRecoveryService.cs`
- `PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationJob.cs`
- `PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationHostedService.cs`
- `PointOfSale.API/Services/Payments/RedirectUrlRecoveryHostedService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentCompensationOutboxWorker.cs`
- `PointOfSale.Infrastructure/Payments/StripeAdapter.cs`
- `PointOfSale.Infrastructure/Payments/PaymobAdapter.cs`
- `PointOfSale.Infrastructure/Payments/PaymobWebhookHmacVerifier.cs`
- `PointOfSale.Infrastructure/Payments/FawryWebhookSignatureVerifier.cs`
- `PointOfSale.Infrastructure/Payments/FawryWebhookRuntimeIntegrationService.cs`
- `PointOfSale.Infrastructure/Payments/StripeWebhookRuntimeIntegrationService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

## Executive Summary

Payment completion is owned by backend payment lifecycle services, not browser callbacks.

For storefront `Order` and `PaymentIntent` flows, canonical payment completion is applied by `IPaymentLifecycleService`, implemented by `PaymentLifecycleService`. Webhook controllers and provider orchestration services authenticate, deduplicate, resolve local payment authority, and then call the lifecycle service for state transition.

For CommerceHub Stripe and Paymob flows, `CommerceHubPaymentService` owns CommerceHub payment completion. It verifies provider webhook evidence through provider adapters, resolves the `CommerceHubPayment`, applies idempotent outcome processing, mutates payment and order state, and records completion evidence.

Fawry completion starts at `FawryWebhookController`, which performs runtime admission, duplicate protection, and durable webhook logging before calling `IPaymentLifecycleService` only when Fawry runtime evidence is lifecycle-eligible.

Payment recovery is owned by hosted services and recovery routines:

- `CheckoutPaymentIntentRecoveryHostedService` and `CheckoutPaymentIntentRecoveryService` repair missing or incomplete payment intents.
- `StorefrontUnresolvedPaymentReconciliationHostedService` and `StorefrontUnresolvedPaymentReconciliationJob` reconcile unresolved successful provider webhooks.
- `RedirectUrlRecoveryHostedService` recovers missing Stripe redirect/client-secret evidence.
- `CommerceHubPaymentService` performs provider session recovery for incomplete pending CommerceHub payments.
- `CommerceHubPaymentCompensationOutboxWorker` processes captured payments that could not be applied to the canonical order state.

Completion starts when a provider-specific signed webhook is received, verified, resolved to tenant/payment context, and admitted for lifecycle processing. Completion ends when payment intent, payment/order state, evidence logs, replay/idempotency state, and outbox events are durably committed.

Paymob browser callbacks are explicitly not payment completion. The implemented callback endpoint validates callback evidence and redirects the shopper to a pending checkout page for backend state polling.

## Payment Completion Entry

Implemented completion entry points are provider-specific webhook endpoints:

- Stripe: `POST /api/webhooks/stripe`
- Paymob: `POST /api/webhooks/paymob`
- Fawry: `POST /api/webhooks/fawry`

Each provider endpoint enters the shared request preparation boundary through `IPaymentWebhookRequestPipeline`, except Fawry, which then enters Fawry-specific runtime admission and lifecycle eligibility checks.

The generic endpoint `POST /api/payments/webhooks/{provider}` is implemented fail-closed. `PaymentService.VerifyWebhookSignature` rejects generic endpoint webhook processing because provider-specific endpoints are required for verifiable signatures. Therefore, the generic endpoint is not certified as an active payment completion path.

The implemented entry sequence is:

1. Provider webhook received.
2. Signature/HMAC header or query evidence extracted.
3. Raw JSON payload read within configured request limits.
4. Tenant resolved from provider payload to existing payment records.
5. Optional claimed tenant is checked against resolved tenant.
6. Provider-specific verification and parsing are executed.
7. Duplicate or replay status is checked.
8. Local payment authority is resolved.
9. Lifecycle or CommerceHub completion is applied.
10. Evidence and events are persisted.

## Completion Pipeline

The storefront payment completion pipeline is:

1. Webhook controller receives provider event.
2. `PaymentWebhookRequestPipeline` reads payload, extracts signature evidence, resolves tenant, and rejects tenant mismatch.
3. Provider-specific parser/verifier authenticates the event.
4. Duplicate event protection is applied using `PaymentWebhookLog`, replay records, or provider event idempotency.
5. `PaymentIntent` and `Order` are resolved under tenant scope.
6. Provider outcome is mapped to `HandlePaymentResultRequest`.
7. `PaymentLifecycleService` validates current state and provider authority.
8. Valid success transitions set `PaymentIntentStatus.Paid`, set captured provider identifiers, set order payment status to collected, commit reservations/coupons, and publish completion events.
9. Valid failure, cancellation, rejection, or expiration transitions set the corresponding payment intent and order payment states, release reservations/coupons, and publish failure events.
10. Webhook evidence is marked completed, skipped, failed, or routed to suspense.

The CommerceHub Stripe and Paymob completion pipeline is:

1. Provider webhook controller prepares request and tenant context.
2. `CommerceHubPaymentService.ProcessWebhookAsync` routes to Stripe or Paymob only.
3. Provider adapter parses and verifies webhook evidence.
4. `CommerceHubPayment` is resolved by provider reference, intent id, or transaction id.
5. Missing successful provider outcomes are persisted as unresolved payment webhooks for reconciliation.
6. A distributed payment processing lock is acquired.
7. A serializable transaction reloads payment and order state.
8. Amount, currency, order linkage, and provider identity are validated.
9. Duplicate already-processed payments are ignored idempotently.
10. Successful outcomes mark the CommerceHub payment paid, record canonical completion evidence, finalize reservations, and mark online payment collected.
11. Failed outcomes mark the payment failed, move the order back to retry hold, release reservations, and stage canonical events.
12. Events and evidence are committed transactionally.

The Fawry completion pipeline is:

1. `FawryWebhookController` receives callback/webhook evidence.
2. Fawry runtime admission evaluates signature authority, provider event id, provider reference, and outcome.
3. `PaymentWebhookLog` is prepared inside a serializable transaction.
4. Completed or skipped duplicate events return success without reapplying lifecycle state.
5. In-flight duplicate events return retryable failure to the provider.
6. Lifecycle persistence is allowed only for admitted Fawry evidence whose runtime session, transaction, and settlement admission states match the implemented eligibility policy.
7. Eligible PAID/CAPTURED outcomes are converted to `HandlePaymentResultRequest` and applied by `PaymentLifecycleService`.
8. Replay records and webhook logs are persisted.

## Callback Processing

Paymob callback processing is implemented as validation and shopper routing only.

`PaymobWebhooksController.Callback` parses callback query parameters, verifies callback evidence through `CommerceHubPaymentService.ValidateCallbackAsync`, validates tenant routing, and redirects the shopper to the storefront pending page.

The callback endpoint does not mark a payment paid, failed, cancelled, collected, settled, refunded, or expired. Final status is determined by backend payment state and webhook processing.

## Recovery Pipeline

Pending payment recovery is implemented by `CheckoutPaymentIntentRecoveryService`.

The recovery service scans pending orders whose checkout state indicates that a payment intent should exist. It repairs orders with incomplete or missing active payment intents after a configured grace period. It can resume existing durable manual wallet evidence, recreate provider-accurate payment intents for Stripe, Paymob, Fawry, or COD, and move expired unrecoverable orders to cancelled/expired state after the reservation window elapses.

Unknown provider outcome recovery is implemented in CommerceHub and storefront paths.

`CommerceHubPaymentService.RecordInitiationUnknownOutcomeAsync` leaves the payment pending while clearing non-durable checkout URL/token evidence and recording a reconciliation-required failure reason. `TryRecoverIncompletePendingPaymentAsync` attempts to recover reusable or renewed provider sessions for incomplete Stripe and Paymob pending payments.

Unresolved successful provider webhook recovery is implemented by `StorefrontUnresolvedPaymentReconciliationJob`.

The reconciliation job scans completed webhook logs routed to suspense, recovers protected raw payload evidence, reparses successful provider events, resolves tenant/payment/order context, calls `IPaymentLifecycleService` to apply success, and enqueues resolved accounting outbox evidence when the order becomes paid.

Redirect/client-secret recovery is implemented by `RedirectUrlRecoveryHostedService`.

The worker retries missing Stripe client-secret recovery with bounded retry attempts and exponential backoff. It persists recovered encrypted client secret evidence when the provider returns the same persisted payment intent id. Its terminal dead-letter evidence is currently log-based rather than a durable dead-letter table.

Cancellation and expiration recovery are implemented by lifecycle failure handling and checkout intent recovery.

`PaymentLifecycleService` maps cancelled, rejected, expired, and failed outcomes to terminal payment intent states and order failure states. `MarkPaymentExpiredAsync` expires active pending, authorized, or awaiting-verification payment intents. `CheckoutPaymentIntentRecoveryService` expires unrecoverable pending checkout orders after the reservation recovery window and releases stock and coupon authority.

Outbox recovery is implemented through transactional outbox and compensation workers.

Payment lifecycle completion stages canonical payment events through outbox mechanisms. CommerceHub compensation outbox processing exists for captured provider payments that cannot be safely applied to order state and requires operator reconciliation when gateway outcome uncertainty is detected.

## State Machine

Implemented `PaymentIntentStatus` states include:

- `Pending`
- `Authorized`
- `Paid`
- `Failed`
- `Cancelled`
- `Expired`
- `Skipped`
- `AwaitingVerification`
- `Rejected`
- `AmountMismatch`
- `Suspicious`

Implemented allowed lifecycle transitions include:

- `Pending` or `Authorized` to `Paid` for valid provider success.
- `Pending` to `Authorized` for authorization.
- `Pending`, `Authorized`, or `AwaitingVerification` to `Failed`, `Cancelled`, `Rejected`, or `Expired` for negative outcomes.
- Provider amount or currency mismatch to `AmountMismatch` with order hold/quarantine behavior.
- Late successful provider evidence after reservation expiry or cancellation to `Suspicious`/manual-review handling instead of normal paid completion.
- Already successful terminal order states are handled idempotently and may repair checkout state without duplicating payment completion.

Implemented invalid transition handling includes:

- Success against stale incompatible terminal SQL truth is routed to manual review or quarantine.
- Failure after successful terminal SQL truth is collapsed idempotently and does not reverse collected payment state.
- Paid payment intent with non-finalized order state requires manual review.
- Unsupported external `AmountMismatch` lifecycle outcome is rejected; mismatch is derived internally from provider evidence.
- COD/cash webhook provider strings are blocked from generic webhook processing.

Terminal SQL payment truth is explicitly recognized for collected/paid/settled/refunded/partially refunded/failed/rejected/expired/amount mismatch order payment states. Settlement and refund states are recognized only as terminal truth for replay/staleness decisions in this certification scope.

## Idempotency

Webhook deduplication is implemented through durable unique event evidence.

`PaymentWebhookLog` has a unique index on `(TenantId, Provider, ProviderEventId)`. Duplicate completed or skipped Fawry events return successful no-op responses. In-flight duplicates return retryable failure. Previously failed Fawry events are reopened for retry processing.

The generic storefront `PaymentService` builds idempotency keys from tenant, provider, and provider event id, checks Redis when available, and falls back to database duplicate detection through `PaymentWebhookLog`.

Provider replay record entities exist for Stripe, Paymob, and Fawry with unique tenant/provider/event indexes. Fawry actively persists replay records after successful lifecycle admission. Stripe and Paymob runtime evidence services implement replay record persistence, while active CommerceHub controller paths primarily deduplicate through provider event handling, payment processed state, unique provider identifiers, and unresolved webhook uniqueness.

CommerceHub payment completion is idempotent at the payment state level. If a payment is already processed, duplicate success/failure webhook application is ignored or reconciled according to the implemented earlier-failure-to-success policy. Database uniqueness also enforces single paid CommerceHub payment per order and unique provider references, provider intent ids, and provider transaction ids.

Paymob browser callbacks are validated but do not mutate payment state, so duplicate callbacks cannot duplicate completion.

## Verification

Webhook signature verification is implemented as provider-specific verification.

Stripe CommerceHub webhooks are parsed through `StripeAdapter.ParseWebhookAsync`, which uses Stripe SDK event construction with the configured webhook secret. A separate Stripe runtime evidence verifier defines a 300-second default timestamp tolerance, but the active Stripe controller path delegates to the adapter.

Paymob webhooks and callbacks are verified through `PaymobWebhookHmacVerifier`, using HMAC-SHA512 over canonicalized payload evidence and fixed-time signature comparison. Timestamp tolerance is supported by the verifier when supplied, but the active Paymob adapter call does not supply a timestamp tolerance.

Fawry runtime evidence verification uses `FawryWebhookSignatureVerifier`, secure-key hashing, and fixed-time comparison. Timestamp tolerance is supported by the verifier when supplied.

The generic payment webhook endpoint is intentionally not verified and not active for completion. It rejects processing because generic webhook signature verification is fail-closed.

Replay protection is implemented through event id uniqueness, replay records, processed payment checks, and callback non-mutation. Explicit timestamp replay-window enforcement is only certified where the active implementation supplies a tolerance or provider SDK verification enforces one.

## Audit Evidence

Implemented audit evidence includes:

- `PaymentWebhookLog` records tenant, provider, provider event id, event type, raw/protected payload evidence, signature evidence or digest, signature verification status, processing status, processing errors, processing duration, source IP, user agent, payment intent id, order id, and processed timestamp.
- CommerceHub payment logs record provider webhook, callback, completion, and failure evidence for payment attempts.
- `PaymentIntent` records provider identity, provider payment id, reference code, payment reference, last webhook event id, webhook count, captured/failed/authorized timestamps, amount, currency, and terminal status.
- Fawry replay persistence stores provider event replay evidence after successful runtime admission.
- Stripe and Paymob replay record entities are configured with unique event evidence and provider-reference indexes.
- Unresolved successful provider outcomes are persisted in `CommerceHubUnresolvedPaymentWebhook` or routed through storefront suspense evidence for reconciliation.
- Compensation outbox messages preserve idempotency keys, processing attempts, lease state, reconciliation-required flags, and provider evidence for captured payments that require recovery.
- Lifecycle services publish canonical payment success/failure events and compatibility events through transactional outbox paths.

Audit evidence is durable for core webhook completion, payment lifecycle transition, unresolved payment routing, replay detection, and compensation outbox processing. Redirect URL recovery terminal dead-letter evidence is log-based in the current implementation.

## Runtime Boundary

Payment completion responsibility starts at verified provider webhook admission and ends after durable payment state transition, order payment state update, evidence persistence, and event staging.

Payment completion does not create checkout pricing, recalculate order totals, admit new orders, or execute provider adapter session creation. Those behaviors occur before completion and are outside this certification scope.

Payment completion does not use browser callbacks as payment authority. Callback processing only validates and redirects.

Payment completion does not own settlement, refunds, accounting, marketplace settlement, or fulfillment in this certification scope. The implemented services may publish outbox events or call downstream workflow services after payment collection, but this certification only certifies the implemented payment completion and recovery boundary up to durable payment/order state, evidence, and event staging.

## Critical Invariants

Implementation-backed invariants are:

- Provider webhook evidence must resolve to tenant context before lifecycle processing.
- Provider-specific signature or HMAC verification is required for active completion paths.
- Generic webhooks are fail-closed and are not an active completion authority.
- Browser callbacks do not complete payments.
- Duplicate provider events cannot reapply completed Fawry lifecycle transitions.
- Duplicate CommerceHub completion cannot create a second paid payment for the same order because processed-state checks and unique database indexes enforce idempotency.
- Completion validates amount and currency before applying successful provider outcomes.
- Completion does not recalculate checkout pricing.
- Completion does not recreate admitted orders.
- Completion does not mutate inventory authority except to finalize or release existing reservations as part of payment lifecycle state.
- Stale provider failure cannot reverse already successful terminal payment truth.
- Late provider success after unsafe terminal state is quarantined or routed to manual review instead of silently collecting payment.
- Missing successful provider payment authority is routed to suspense/reconciliation instead of being discarded.

## Architecture Boundaries

Completion owns:

- Webhook request admission for provider-specific endpoints.
- Signature/HMAC verification for active webhook completion paths.
- Tenant resolution from provider evidence.
- Duplicate event and replay protection.
- Payment intent and CommerceHub payment state transitions.
- Order payment state transition coupled to payment outcome.
- Evidence persistence.
- Unresolved successful payment routing.
- Recovery and reconciliation of pending or unresolved payment outcomes.

Completion does not own:

- Checkout validation.
- Pricing.
- Inventory availability authority.
- Coupon calculation authority.
- Order admission.
- Provider adapter session execution.
- Settlement.
- Refunds.
- Accounting.
- Marketplace settlement.
- Fulfillment.

## Architectural Observations

- Active completion authority is provider-specific. The generic webhook endpoint is intentionally fail-closed and should not be treated as a working completion path.
- Paymob callback handling is validation and redirect only; it is not a payment completion path.
- Timestamp replay-window enforcement is not uniformly visible in every active provider path. Replay protection is implemented through signed/HMAC evidence and durable event idempotency.
- Redirect URL recovery dead-letter handling is currently log-based rather than persisted to a dedicated durable dead-letter table.

## Architecture Risks

No architectural violations were identified.

## Certification Checklist

| Control | Result |
| --- | --- |
| Completion Entry | PASS |
| Completion Pipeline | PASS |
| Recovery Pipeline | PASS |
| State Machine | PASS |
| Idempotency | PASS |
| Verification | PASS |
| Audit Evidence | PASS |
| Runtime Boundary | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS |

