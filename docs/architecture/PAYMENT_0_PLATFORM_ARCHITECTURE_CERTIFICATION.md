# PAYMENT-0 Payment Platform Architecture Certification

Date: 2026-07-10
Scope: Payment Platform architecture only
Status: CERTIFIED

## Executive Summary

Payment was reviewed as the provider-neutral enterprise payment authority across payment methods, provider configuration, payment intent/session evidence, provider verification, lifecycle transitions, refunds, settlements, webhook admission, audit, recovery, and failure handling.

Certification decision: **CERTIFIED**.

One true inconsistency was found and fixed: `IPaymentService` contained stale phase commentary saying the order was payment source of truth, webhooks were source of truth, and refunds were not in scope. That no longer matched the already-certified runtime. One stale architecture comment was corrected only; runtime behavior was not changed.

No new permanent cross-platform architecture truth was discovered. The `.ai-context` files remain unchanged.

## Architecture Overview

Payment is the backend authority for payment orchestration and lifecycle. It consumes final payable amount, currency, tenant, order reference, customer/session access evidence, payment method configuration, and provider evidence. It emits admitted payment status, session handoff evidence, audit, lifecycle events, refund/settlement fanout, and SQL-backed projection inputs.

Payment does not own checkout, order totals, pricing, taxes, coupons, products, inventory, warehouse, shipping, addresses, customers, or fulfillment. Those domains provide already-admitted evidence or consume Payment outcomes through their own authorities.

The implemented Payment runtime boundary is composed of:

- `IPaymentService` for payment intent/session orchestration, provider initiation, webhook admission, and payment status reads.
- `IPaymentLifecycleService` for lifecycle transitions, refunds, settlement fanout, expiration, manual wallet approval/rejection, and guarded status mutation.
- `IPaymentAdapter` for provider-neutral initiation contracts used by adapter-backed providers.
- Provider services for Stripe and Paymob verification/session creation behind backend Payment ownership.
- SQL `PaymentIntents`, `TenantPaymentMethods`, `PaymentWebhookLogs`, settlement/refund rows, outbox rows, and audit as durable authority.

## Authority Boundaries

Payment owns:

- Payment Methods.
- Payment Providers and provider configuration.
- Payment Sessions and payment intent handoff evidence.
- Payment Attempts through retryable payment intents and provider event handling.
- Payment Transactions through lifecycle outcomes, captures, refunds, settlements, failures, and reconciliation evidence.
- Payment Lifecycle and status.
- Provider Resolution.
- Refund, capture, void/cancellation, settlement, and expiration requests where implemented.
- Webhook authentication, idempotency, duplicate collapse, and audit.
- Payment receipts, verification, events, metadata, and failure classification.

Payment never owns:

- Checkout orchestration beyond payment session creation/verification boundaries.
- Orders or order lifecycle authority.
- Inventory, Warehouse, Shipping, Shipment, Carrier Mapping, Customs, Geography, Pricing, Taxes, Coupons, Discounts, Catalog, Products, Customers, Addresses, or Merchant Fulfillment.
- Order totals. Payment consumes final payable amount and immutable order reference evidence only.

## Domain Model

Current implementation uses the following durable model:

```text
TenantPaymentMethod
  -> Payment provider/configuration evidence
  -> PaymentIntent
  -> Provider event / webhook log
  -> IPaymentLifecycleService outcome
  -> Refund / settlement / capture / cancellation fanout
```

The platform supports multiple attempts for a single checkout through payment intent idempotency, active provider uniqueness, recovery paths, checkout re-entry, provider event idempotency, and terminal-state collapse. Current code names the durable session aggregate `PaymentIntent`; this certification treats it as the implemented payment session/attempt authority.

The requested conceptual model is satisfied as:

```text
PaymentMethod
  -> PaymentProvider configuration
  -> PaymentIntent / PaymentSession evidence
  -> Payment attempt / provider event
  -> Payment lifecycle transaction
  -> Refund / Capture / Void-Cancel / Settlement
```

## Payment Lifecycle

Lifecycle is owned by `IPaymentLifecycleService`.

Certified states and outcomes include:

- Pending.
- Requires customer action / awaiting verification through payment intent handoff evidence.
- Authorized.
- Captured / Collected.
- Paid.
- Partially Refunded.
- Refunded.
- Cancelled.
- Expired.
- Failed.
- Rejected / AmountMismatch quarantine where applicable.

Direct `PaymentStatus` mutation is guarded by `PaymentStatusSaveChangesInterceptor`; status changes must flow through the lifecycle service authorized context.

## Provider Architecture

The platform is provider-neutral at the authority boundary:

- `IPaymentAdapter` exposes a common initiation contract.
- Stripe, Paymob, Fawry, and Manual Wallet stay backend-owned provider implementations or backend adapters.
- Checkout may receive transient handoff artifacts issued by Payment, but final provider verification and lifecycle transitions remain backend Payment-owned.
- Frontend provider SDK/redirect results are evidence only and remain pending until backend canonical status confirms the outcome.

Provider-specific compatibility artifacts still exist in DTO names and checkout handoff fields for backward compatibility. They do not own final payment truth and are regression-frozen as transient Payment-issued session evidence.

## Session Model

Certified payment session evidence includes:

- Session/payment intent id.
- Correlation id through request trace and lifecycle/outbox flow.
- Order reference.
- External/provider reference.
- Provider reference.
- Customer/session access reference through payment authorization.
- Merchant/tenant reference.
- Currency.
- Amount.
- Expiration.
- Idempotency key.
- Retry and duplicate handling through request id, provider event id, active provider uniqueness, and recovery.
- Metadata.
- Audit trail.

Sensitive provider credentials and secrets remain server-side. Storefront receives only admitted transient handoff evidence.

## Transaction Model

Certified transaction coverage:

- Authorization.
- Capture / sale / collection.
- Refund.
- Partial refund.
- Void/cancellation/expiration where lifecycle-supported.
- Failure.
- Retry.
- Reconciliation.
- Settlement reference.

Payment transaction truth is SQL-backed and lifecycle-owned. Provider payloads are evidence until admitted.

## Webhook Ownership

Payment owns webhook processing:

- Authentication and signature/HMAC validation.
- Tenant resolution from payment evidence, not trusted caller headers.
- Replay protection.
- Duplicate detection.
- SQL idempotency.
- Event ordering/terminal-state collapse.
- Retry processing.
- Audit logging through `PaymentWebhookLog`.
- Suspense/unresolved payment recovery.

No provider webhook logic was found outside backend Payment provider services/controllers.

## Security Review

Certified controls:

- Tenant isolation through tenant-scoped payment methods, intents, logs, and authorization.
- Credential isolation through encrypted configuration and server-side provider services.
- Secret ownership remains backend-only.
- Payment action authorization validates customer/session/guest access.
- Unified `ApiErrorResponse` usage on Payment and checkout-facing APIs.
- Sensitive client secrets are encrypted at rest and emitted only as transient handoff evidence where required.
- Webhook bodies are size-limited, rate-limited, signature-checked, and audited.
- Logs avoid raw secret ownership and use trace/correlation identifiers.

## Failure Matrix

| Failure | Certified Behavior |
|---|---|
| Unknown Provider | Reject through provider-not-supported handling. |
| Disabled Provider | Payment method unavailable or configuration invalid. |
| Disabled Method | Hidden from storefront and rejected during checkout revalidation. |
| Invalid Credentials | Provider/configuration validation fails closed. |
| Expired Session | Lifecycle expiration/recovery clears stale payable artifacts. |
| Duplicate Payment | Request/provider idempotency and SQL uniqueness collapse duplicates. |
| Webhook Replay | Provider event id and webhook log idempotency collapse replay. |
| Invalid Signature | Rejected as invalid signature. |
| Timeout | Provider retry/recovery remains pending or fails closed. |
| Provider Unavailable | Payment initiation fails without creating final success. |
| Tenant Mismatch | Tenant resolution and payment authorization fail closed. |
| Currency Mismatch | Lifecycle/provider amount-currency mismatch quarantine/failure. |
| Amount Mismatch | Lifecycle authority quarantines or rejects provider mismatch. |

## Cross-Platform Integration

Frozen boundaries preserved:

- POS Admin Checkout remains orchestrator-only and consumes Payment-issued methods/session evidence.
- Affiliate Checkout remains an attribution overlay and never owns Payment.
- Marketplace Checkout remains planning/presentation and reuses CommerceHub Payment authority.
- Warehouse and Inventory consume lifecycle outcomes but do not own Payment.
- Shipping and Shipment consume payment readiness/evidence where required but do not own Payment.
- Accounting/settlement consume Payment lifecycle/refund/settlement authority through SQL/outbox/reconciliation paths.

## Regression Risks

- Provider-specific handoff fields remain for compatibility and must not be expanded into checkout-owned provider logic.
- Existing legacy names such as `PaymentIntent` serve as implemented payment session/attempt authority; future renaming must preserve backward compatibility.
- New providers must enter through backend Payment contracts and provider configuration, not checkout branching or frontend finality.
- Webhook recovery must continue to converge against SQL Payment authority, not provider payload or projection state alone.

## Certification Decision

Architecture Certified: Yes.

Authority Boundaries Certified: Yes.

Provider Abstraction Certified: Yes.

Payment Lifecycle Certified: Yes.

Session Model Certified: Yes.

Transaction Model Certified: Yes.

Webhook Ownership Certified: Yes.

Checkout Boundary Preserved: Yes.

Provider Neutral: Yes.

Backward Compatible: Yes.

Production-certifiable: Yes for architecture; operational provider rollout evidence remains outside this certification.

Runtime modified: No.

Backend modified: Yes, one architecture comment only.

Frontend modified: No.

## Regression Coverage

Added focused architecture certification tests:

```text
PointOfSale.Tests/Architecture/Payment0PlatformArchitectureCertificationTests.cs
```

Coverage verifies:

- Authority ownership.
- Provider abstraction.
- Lifecycle ownership.
- Session model.
- Transaction model.
- Checkout boundaries.
- Provider neutrality.
- Thin API behavior.
- Security boundaries.
- Failure behavior.
- No final provider leakage outside Payment authority.

## Explicit Success Answers

- Reviewed ONLY Payment Platform Architecture? Yes.
- Runtime modified? No.
- Backend modified? Yes, one architecture comment only.
- Frontend modified? No.
- Architecture Certified? Yes.
- Authority Boundaries Certified? Yes.
- Provider Abstraction Certified? Yes.
- Payment Lifecycle Certified? Yes.
- Session Model Certified? Yes.
- Transaction Model Certified? Yes.
- Webhook Ownership Certified? Yes.
- Checkout Boundary Preserved? Yes.
- Provider Neutral? Yes.
- Backward Compatible? Yes.
- Production-certifiable? Yes, architecture-certifiable; operational rollout evidence remains separate.

Recommend next phase: Payment operational readiness certification with live/sandbox provider credentials, webhook replay drills, settlement/refund reconciliation evidence, dashboards, alerts, runbooks, and rollback rehearsal.
