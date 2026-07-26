# PASS P2-I Storefront WhatsApp Authority Certification Findings

Date: 2026-06-09

## Certified Runtime Boundary

PostgreSQL customer, order, payment, tenant, and account state remains authoritative. WhatsApp
owns only outbound dispatch state, provider acceptance evidence, tenant account/session identity,
approved template rendering, and communication history. MongoDB message logs and Baileys session
metadata are durable communication records, not commerce authority.

The inspected runtime has no inbound WhatsApp message webhook, Meta delivery/read webhook,
customer conversation API, or WhatsApp shipping notification path. Those absent capabilities are
not implicitly certified. Adding any of them requires a renewed P2-I runtime review.

## Findings

### Conversation Ownership

- No runtime customer conversation aggregate or conversation API exists.
- Account/session ownership is tenant-scoped and a Baileys session is connected only when the
  live socket-attested phone matches the configured account phone.
- The administrative route that could manually mark an account connected was removed.
- A database uniqueness fence prevents multiple enabled connected accounts from owning the same
  tenant use case.

Conclusion: the implemented outbound session cannot be adopted through account ID guessing or
manual connection-state forgery. Customer conversation hijacking is absent because conversations
are not implemented.

### Message Authority

- Order and payment messages are recomposed from current same-tenant PostgreSQL state immediately
  before dispatch.
- Event DTO text, phone, customer, amount, status, and item snapshots do not own dispatch truth.
- Message history uses deterministic tenant/entity idempotency keys and an atomic dispatch lease.
- Provider message IDs are retained as acceptance evidence.
- WhatsApp dispatch performs no order, payment, shipping, promotion, refund, or customer writes.

### Template Authority

- Persisted templates are tenant-owned and restricted to supported template types.
- Template variables are allowlisted per type; malformed, unknown, and cross-domain placeholders
  fail validation.
- Current templates are selected at dispatch time. Stale event-rendered text cannot survive.
- Templates can communicate certified state but cannot create or mutate commerce state.

### Order Notifications

- Order confirmation reloads the current same-tenant order, items, tenant currency, customer
  contact, and status from PostgreSQL.
- Deleted, cancelled, and refunded orders fail closed.
- The message idempotency key is deterministic per tenant/order.

### Payment Notifications

- Approved and rejected messages reload the current same-tenant payment intent and owning order.
- Dispatch requires the current authoritative payment status to match the notification type.
- Payment-intent identity, not stale event text, owns idempotency and recomposition.
- Payment approval requests are fail closed because no authoritative administrative destination
  phone exists in the current model.

### Shipping Notifications

- No WhatsApp shipping/tracking notification runtime path exists.
- WhatsApp cannot set shipment, tracking, delivery, or fulfillment state.
- Any future shipping message must recompose current P2-F SQL state and requires renewed review.

### Webhook Authority

- No inbound Meta webhook, delivery callback, read receipt, or inbound-message route exists.
- Consequently, external webhook DTOs cannot currently mutate message or commerce state.
- Future webhook support must add provider signature verification, tenant/account resolution,
  durable provider-event replay fences, monotonic transition rules, and regression freezes before
  deployment.

### Delivery State

- Runtime states distinguish queued, dispatching, provider-accepted sent, failed, skipped, and
  delivery-unknown.
- Atomic leases prevent concurrent workers from claiming the same dispatch.
- Ambiguous network/timeout failures become terminal `DELIVERY_UNKNOWN`; they are not blindly
  retried into duplicate customer messages.
- Delivered and read are not represented. They therefore cannot be forged into platform state.

### Opt-In

- Recovery/marketing WhatsApp requires the current SQL cart phone to be verified and not opted out.
- Transactional order/payment notices use current customer-provided contact and honor durable
  opt-out state.
- OTP is limited to the user-requested ephemeral code path and is not persisted as reusable
  template data.

### Tenant Isolation

- Tenant lineage is revalidated for account selection and dispatch.
- Account lookup, session status consumption, order/payment recomposition, message completion, and
  cart recovery are tenant-scoped.
- Foreign event DTOs, account IDs, order IDs, payment IDs, and message-log IDs fail closed.

### Concurrency And Idempotency

- MongoDB unique idempotency keys plus atomic dispatch leases prevent concurrent duplicate claims.
- Lease-bound completion prevents a stale worker from replacing a newer worker's result.
- Deterministic keys cover order confirmation, payment status, and OTP event identity.
- Expired dispatch leases can recover only within the bounded attempt policy.

### Historical Consistency

- Commerce text is rebuilt from current SQL authority at send time.
- Terminal sent, skipped, and delivery-unknown records are not replayed.
- Provider acceptance evidence and attempt history remain durable.
- Stale events cannot resurrect invalid orders, payment states, contacts, accounts, or carts.

### Adversarial Review

- Conversation hijacking: no conversation surface; tenant/account/session identity fails closed.
- Message spoofing: event text and contact DTOs are discarded for commerce notifications.
- Template abuse: supported types and variables are allowlisted.
- Tenant guessing: all authority lookups include tenant lineage and ownership.
- Replay: deterministic keys, unique storage, and atomic leases fence duplicate dispatch.
- Route tampering: manual connected-state forgery was removed.
- Delivery spoofing: no callback surface exists; provider acceptance is transport-produced only.
- Commerce mutation: the dispatch authority is read-only over commerce state.

## Runtime Hardening Performed

- Added SQL-backed dispatch recomposition for order and payment notifications.
- Added tenant/account/use-case eligibility validation before every send.
- Added durable opt-out checks and current verified-contact checks for cart recovery.
- Added atomic dispatch claims, bounded attempts, lease-bound completion, provider IDs, and
  `DELIVERY_UNKNOWN`.
- Added deterministic idempotency keys.
- Added Baileys live connected-phone attestation.
- Removed manual administrative connection-state authority.
- Added template type and variable allowlists.
- Added database uniqueness fences for enabled connected use-case ownership.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/Admin/WhatsAppAdminController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Core/Interfaces/WhatsApp/IWhatsAppPaymentNotificationService.cs`
- `PointOfSale.Infrastructure/Consumers/AbandonedCart/RecoveryOrchestrationConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppOrderConfirmationConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppOtpConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppPaymentApprovalRequestedConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppPaymentApprovedNotificationConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppPaymentRejectedNotificationConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppSessionMonitorConsumer.cs`
- `PointOfSale.Infrastructure/Migrations/20260609210000_StorefrontWhatsAppAuthorityHardening.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/WhatsAppMessageLogDocument.cs`
- `PointOfSale.Infrastructure/WhatsApp/Interfaces/IWhatsAppMessageDispatcher.cs`
- `PointOfSale.Infrastructure/WhatsApp/Interfaces/IWhatsAppTransport.cs`
- `PointOfSale.Infrastructure/WhatsApp/Jobs/WhatsAppSessionHeartbeatJob.cs`
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppAccountService.cs`
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppDispatchAuthority.cs`
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppMessageDispatcher.cs`
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppMessageLogService.cs`
- `PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppPaymentNotificationService.cs`
- `PointOfSale.Infrastructure/WhatsApp/Transport/BaileysWhatsAppTransport.cs`
- `PointOfSale.WhatsApp.Baileys/src/models/Session.ts`
- `PointOfSale.WhatsApp.Baileys/src/routes/sessions.ts`
- `PointOfSale.WhatsApp.Baileys/src/services/SessionManager.ts`

## Regression Freeze

`PointOfSale.Tests/Architecture/StorefrontWhatsAppAuthorityCertificationTests.cs`

The focused .NET run passed 15/15 WhatsApp and P2-I tests. The Baileys production TypeScript build
passed. Its pre-existing Jest harness does not execute because of `uuid` ESM transformation,
stale `MessageSender` constructor usage, and unrelated strict test TypeScript errors.

## Explicit Answers

- Who owns WhatsApp authority? Tenant-owned WhatsApp account/session configuration, the
  SQL-recomposed dispatch boundary, and durable message dispatch history own communication truth.
- Can conversations be hijacked? Not in the implemented runtime; no customer conversation surface
  exists and account/session ownership is tenant-scoped and phone-attested.
- Can messages be spoofed? Commerce messages cannot be supplied by event DTOs; they are recomposed
  from current SQL authority.
- Can templates bypass authority? No. Types and variables are allowlisted and rendering remains
  derivative.
- Can webhooks be spoofed? No webhook surface exists. Future webhooks are uncertified until signed
  and replay-fenced.
- Can delivery state be forged? Provider-accepted state is transport-produced; delivered/read are
  absent and cannot be asserted.
- Can WhatsApp mutate order authority? No.
- Can WhatsApp mutate payment authority? No.
- Can WhatsApp mutate shipping authority? No.
- Can tenant leakage occur? No inspected runtime path permits it after hardening.
- Can replay corrupt messaging state? Deterministic keys and atomic leases prevent it.
- Were runtime code changes required? Yes.
- Is Storefront WhatsApp Authority production-certifiable? Yes, for the inspected outbound-only
  runtime after applying migration `20260609210000_StorefrontWhatsAppAuthorityHardening`.

## Residual Conditions

- Positive-consent policy beyond verified contact plus durable opt-out is jurisdiction and product
  policy. If explicit positive WhatsApp consent is required, add a tenant/customer-scoped consent
  ledger before enabling affected message classes.
- Production transport/load/failover evidence remains an operational deployment proof.
- Inbound messages, conversations, Meta webhooks, delivered/read receipts, marketing campaigns,
  and shipping notifications require renewed P2-I certification.
