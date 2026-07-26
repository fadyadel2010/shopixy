# PASS P2-D Storefront Payment Authority Certification Findings

Date: 2026-06-08

## Verdict

PASS P2-D is production-certifiable for the inspected Storefront Payment Authority runtime paths.
No runtime code hardening was required in this pass. Existing runtime controls already bind payment
collection, verification, confirmation, reconciliation, status transitions, settlement, and
order-payment integration to current tenant-owned SQL payment/order authority, provider-verified
evidence, lifecycle state machines, idempotency, and accounting outbox boundaries.

Payment Authority owns payment collection truth, verification truth, confirmation truth, status
truth, and reconciliation truth. It may advance order payment state through
`IPaymentLifecycleService`, but it does not own order lifecycle, treasury ledger authority,
inventory authority, promotion authority, or reporting authority.

## Findings

- Payment method findings: checkout validates requested payment methods from current SQL
  `TenantPaymentMethods`, requires enabled/non-deleted/configuration-valid methods, applies amount
  limits, and maps the authoritative method snapshot to the order. Storefront read models expose
  only enabled valid methods and no secrets.
- Payment intent findings: intent creation locks the tenant-owned order, requires pending
  order/payment state, derives amount from `Order.Total`, derives currency from order/tenant
  authority, validates active reservations, rejects duplicate active intents, and persists the
  local intent before provider initiation.
- Provider verification findings: Stripe uses signed `EventUtility.ConstructEvent`; Paymob uses
  tenant-resolved HMAC verification. Provider success must carry provider amount/currency and is
  compared to stored SQL intent/order truth before money is recognized.
- Webhook findings: webhook ingress requires signatures, resolves tenant from stored payment
  records, rejects claimed-tenant mismatch, records provider event IDs in SQL, collapses duplicate
  and in-flight events, and returns retryable failures when processing is unresolved.
- Manual payment findings: customers can upload tenant-scoped proof only through an authenticated
  checkout-session upload token. Customers cannot approve. Admin approval/rejection is role-gated,
  state-machine checked, and routed through `IPaymentLifecycleService`.
- COD findings: COD intent creation can confirm the order for fulfillment but does not recognize
  collected money. COD becomes `Collected` only through delivery collection authority and settles
  only from collected same-tenant same-currency orders with accounting readiness.
- Payment state machine findings: `PaymentStatusSaveChangesInterceptor` blocks direct order
  payment-status mutation. Lifecycle code collapses terminal replays, quarantines amount/currency
  mismatches, prevents stale success from overwriting terminal SQL truth, and records failure or
  expiration without ledger recognition.
- Order integration findings: payment status changes and compatible order status changes are
  emitted from the lifecycle boundary. Fulfillment cannot forge payment success or replace
  cancellation/refund/payment lifecycle authority.
- Treasury integration findings: collection, refund, COD settlement, and gateway settlement stage
  accounting outbox rows and post from committed SQL truth. Payment Authority cannot create ledger
  truth outside certified treasury/accounting boundaries.
- Currency findings: intent creation and provider callbacks validate currency against current
  tenant/order/payment-intent authority. Mixed-currency provider or settlement evidence is rejected
  or quarantined instead of relabeled.
- Tenant isolation findings: payment APIs, manual proof access, webhook tenant resolution,
  settlement, and manual approval paths all reapply tenant ownership. Webhooks derive tenant from
  stored payment records and reject foreign claimed tenant evidence.
- Concurrency findings: order locks, serializable lifecycle/settlement transactions, unique active
  intent indexes, SQL webhook uniqueness, idempotency keys, terminal replay collapse, and row locks
  prevent duplicate collection or duplicate settlements on inspected paths.
- Historical consistency findings: completed payment, refund, settlement, webhook, outbox, and
  event evidence is durable SQL history; reruns converge to idempotent no-op, retry, quarantine, or
  reconciliation-required behavior rather than rewriting history.
- Adversarial findings: payment spoofing, webhook spoofing, callback tampering, manual-payment
  forgery, duplicate collection, replay attacks, payment-state forcing, cross-tenant mutation,
  provider bypass, currency manipulation, and stale terminal resurrection are blocked or routed to
  explicit failure/manual-review/reconciliation behavior on inspected paths.

## Runtime Hardening Performed

No runtime code changes were required. The inspected runtime already contained the required payment
authority controls.

## Files Modified

- `PointOfSale.Tests/Architecture/StorefrontPaymentAuthorityCertificationTests.cs`
- `docs/architecture/storefront-payment-authority-certification-findings.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Regression Freezes

Added `StorefrontPaymentAuthorityCertificationTests`, covering SQL-derived payment intent amount
and currency, SQL payment method eligibility, provider signature and event idempotency, provider
amount/currency validation, lifecycle-only state transitions, manual wallet admin authority, COD
collection authority, settlement idempotency, tenant isolation, and certification artifacts.

## Explicit Answers

- Who owns payment authority? `IPaymentLifecycleService` plus tenant-owned PostgreSQL
  `Orders`, `PaymentIntents`, webhook logs, settlement batches, refund rows, idempotency records,
  accounting outbox rows, and current SQL state.
- Can payment success be forged? No on inspected paths; provider success requires signed provider
  evidence and stored intent/order amount/currency match, while manual/COD success requires
  privileged or delivery authority.
- Can webhook events be spoofed? No on inspected provider paths; unsigned or mismatched-tenant
  webhooks are rejected.
- Can manual payments self-approve? No; customer proof upload is evidence only and approval is
  admin role-gated through the lifecycle.
- Can duplicate collection occur? Inspected paths collapse duplicates through active-intent
  uniqueness, webhook uniqueness, lifecycle idempotency, terminal-state checks, and outbox keys.
- Can payment states regress? No on inspected paths; direct writes are intercepted and terminal SQL
  truth collapses stale outcomes.
- Can order payment state diverge? Payment can temporarily await projection/accounting convergence,
  but authoritative order payment state is mutated only by lifecycle SQL truth and replay/recovery
  converges.
- Can payments bypass treasury authority? No; recognized money stages accounting outbox and posts
  from committed SQL truth under certified treasury/accounting boundaries.
- Can mixed currencies corrupt payments? No on inspected paths; mismatches reject or quarantine.
- Can tenant leakage occur? No inspected tenant-leakage path was found; payment evidence is
  tenant-scoped and claimed tenant mismatches are rejected.
- Can replay attacks duplicate settlements? No on inspected paths; settlement IDs, batch content
  matching, collected-state requirements, and accounting readiness/idempotency block duplicates.
- Were runtime code changes required? No.
- Is Storefront Payment Authority production-certifiable? Yes, for the inspected runtime scope,
  with operational chaos/monitoring proof remaining as production-readiness debt.
