# Cross-System Critical Risk Handoff

Date: 2026-05-07

## Purpose

This file captures the cross-system integrity issues identified during the adversarial audit and the production hardening changes applied in this session.

Use this as a restart point in later chats or future engineering work. The model does not retain memory across new chats by itself, but a repo-local file like this can be reopened and used as context.

## System Framing

The POS Admin system is the authority for:

- money recognition
- order/payment lifecycle
- inventory authority
- marketplace vendor balances and payouts
- accounting truth

The main audit question was:

Can this system commit local truth while leaving Storefront, Marketplace, payouts, or accounting-adjacent consumers in a divergent state?

## Critical Risks Found

### 1. Projection-only downstreams can diverge after SQL authority commits

Storefront and some marketplace-facing flows read from Mongo/read models rather than directly from SQL authority.

Risk:

- SQL commit succeeds
- outbox/projection publish fails
- downstream Mongo/read models lag or never observe the original transition

Impact:

- stale storefront order state
- stale storefront content/product state
- marketplace/backbone consumers observing repaired state instead of original transition semantics

### 2. Late or duplicate provider success can create temporary external cash vs local order divergence

CommerceHub preserves old payment reference data during retry so late webhooks can still route.

Risk:

- payment attempt A is retried or reservations expire
- provider success for old attempt arrives later
- system can only compensate after the fact

Impact:

- external provider cash may be captured for an order that is internally unpaid, already paid, or no longer inventory-valid

### 3. Refund compensation can enter "external truth unknown" state

Risk:

- refund request started
- gateway outcome not durably recorded
- automatic retry blocked to avoid double refund

Impact:

- provider/gateway refund state can diverge from POS payment/refund state until operator reconciliation

### 4. Payout handoff can diverge from bank/provider truth

Risk:

- payout marked `Processing`
- external transfer may already have happened
- completion/failure evidence not durably reconciled

Impact:

- vendor payout truth can differ between POS ledger and external transfer system

### 5. Backlog-scope uncertainty can freeze healthy tenants

Risk:

- unresolved outbox/webhook/compensation backlog exceeds bounded inspection windows

Impact:

- system fails closed and blocks outward money movement
- this preserves integrity but can create broad operational desynchronization/freeze at scale

## Fixes Applied In This Session

### Fix A. Preserve original marketplace backbone payload before rebuilding from current SQL

Problem:

Terminal marketplace projection compensation was capable of rebuilding failed events from present-day SQL state. That is safer than dropping the event, but it can collapse historical transition semantics by replaying an old event id/version with newer state.

Change:

- updated `PointOfSale.Infrastructure/Eventing/ProjectionFailureCompensation.cs`
- compensation now prefers the original serialized marketplace payload when it is still valid
- current-SQL rebuild is now a fallback only when the original payload cannot be safely deserialized

Why this is safer:

- preserves original event semantics whenever possible
- reduces "historical event rewritten as current snapshot" drift
- still keeps a recovery path for corrupted/unreadable payloads

### Fix B. Do not let reservation cleanup release CommerceHub stock while provider cash authority is still unresolved

Problem:

`InventoryReservationCleanupJob` could release reservations after local pending/grace checks even when CommerceHub still had signs of unresolved provider cash authority.

Examples:

- unresolved captured-payment webhook exists for the order
- pending CommerceHub payment still has provider identity
- payment failure reason indicates unknown provider-session outcome

Change:

- updated `PointOfSale.Infrastructure/Jobs/InventoryReservationCleanupJob.cs`
- added blocking unresolved webhook checks before releasing CommerceHub reservations
- added provider-cash-authority gap checks for pending CommerceHub payments
- added a dedicated reconciliation grace window: `ProviderReconciliationGraceMinutes`

Related files:

- `PointOfSale.Infrastructure/Observability/InventoryReservationMetricsService.cs`
- `PointOfSale.API/Program.cs`

Why this is safer:

- prevents TTL cleanup from releasing inventory while provider-side payment truth may still be unresolved
- reduces the "stock released first, provider success arrives later" divergence path

## Test Coverage Added

Architecture coverage was added in:

- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

New assertions cover:

- marketplace projection compensation preferring original payload before SQL rebuild
- reservation cleanup holding CommerceHub reservations while provider cash authority remains unresolved

Focused verification run:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~CrossSystemCriticalRiskHardeningTests"
```

Result:

- passed

## Files Changed In This Session

- `PointOfSale.Infrastructure/Eventing/ProjectionFailureCompensation.cs`
- `PointOfSale.Infrastructure/Jobs/InventoryReservationCleanupJob.cs`
- `PointOfSale.Infrastructure/Observability/InventoryReservationMetricsService.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`

## Remaining Work Not Fixed In This Session

These still need larger architectural treatment:

## Follow-up Audit Findings (Not Fixed)

These are still live cross-system integrity risks after the hardening in this file.

### 1. Provider payment intent can exist before POS authority commit is durable

Flow:

- Storefront/checkout asks POS to create an online payment intent
- POS opens a SQL transaction and inserts the local `PaymentIntent`
- POS then calls Stripe/Paymob before the transaction is committed
- local event staging and final SQL commit happen after the provider call

Break point:

- external provider call succeeds
- later local save/commit or event staging fails
- SQL transaction rolls back, but provider-side payment state may already exist

Impact:

- gateway has a live payment session or provider-side authority record that POS did not durably commit
- later webhooks/retries become a repair path instead of a guaranteed primary path
- customer/payment provider truth can temporarily exist without matching local authority truth

Primary evidence:

- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`

### 2. Storefront and CommerceHub still depend on asynchronous projection repair after SQL truth commits

Flow:

- POS commits SQL authority for storefront or marketplace order/content state
- downstream Mongo/read-model projection publish or consumer application fails
- background reconciliation later repairs or rebuilds the read model

Break point:

- the authoritative write succeeds, but the read-side event path does not complete immediately
- downstream systems read from Mongo/Redis rather than SQL authority

Impact:

- Storefront and CommerceHub can show stale or missing order/content state while POS SQL is already correct
- downstream consumers can observe repaired snapshots later instead of the original transition timing
- affiliate and marketplace read-side consumers can lag behind payment/order truth even when SQL is already final

Primary evidence:

- `docs/STOREFRONT-ARCHITECTURE.md`
- `PointOfSale.API/Services/Storefront/StorefrontOrderProjectionReconciliationService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubProjectionReconciliationService.cs`

### 3. Refund and payout flows still have explicit "external truth unknown" states

Flow:

- POS hands off a refund to a gateway or a payout to a bank/provider
- provider accepts or partially executes the external action
- local completion/reversal/reconciliation step fails or cannot prove final outcome

Break point:

- refund code can mark gateway outcome as unknown and keep the refund in processing/reconciliation territory
- payout code can move stale `Processing` payouts into `ReconciliationRequired`

Impact:

- customer refund truth or vendor payout truth can diverge from POS until manual/automated reconciliation proves what happened externally
- POS correctly fails closed for further outward money movement, but the affected refund/payout itself is still desynchronized until repaired
- accounting and operational status can disagree with bank/gateway truth during the gap

Primary evidence:

- `PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubRefundRecoveryHostedService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs`
- `PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs`

### 4. Tenant-scope backlog gating can still freeze healthy tenants when inspection windows overflow

Flow:

- readiness gate checks unresolved compensation/webhook/outbox backlog before allowing outward money movement
- tenant impact is inferred by inspecting bounded windows of unresolved rows
- if backlog exceeds the inspection window, the system blocks movement because tenant scope cannot be proven safely

Break point:

- backlog is larger than the inspection cap
- the relevant tenant may not actually be affected, but the gate cannot prove safety

Impact:

- healthy tenants can be blocked from refunds/payouts/intake because unrelated backlog elsewhere overflowed the bounded inspection set
- this preserves integrity, but creates operational desynchronization and throughput collapse at scale
- a platform-wide backlog can become a control-plane outage for outward money movement

Primary evidence:

- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs`


### 1. Backlog tenant-scope readiness gating

Current readiness checks intentionally fail closed when backlog inspection exceeds bounded limits.

Needed next:

- stronger tenant-scope indexing/materialization for unresolved webhook/outbox rows
- avoid global 500-row scans as a steady-state control plane mechanism

### 2. External outcome unknown reconciliation flows

Refund and payout paths still have explicit operator-reconciliation states when external truth cannot be proven.

Needed next:

- deeper automated reconciliation with providers/banks
- stronger durable evidence model for transfer/refund completion
- clearer operational tooling for reconciliation-required records

### 3. Broader projection integrity strategy

This session improved marketplace event compensation, but full cross-system hardening still benefits from:

- stronger projection freshness/version contracts across all read models
- narrower use of "rebuild from current SQL" recovery paths
- more explicit distinction between replaying original domain events vs emitting snapshot-repair events

## Later Session Fixes Applied

The following cross-system hardening work was implemented after the audit findings above.

### 1. Payment authority is now committed before Stripe/Paymob side effects

Implemented:

- POS now persists the local `PaymentIntent` authority and order gateway reference first
- SQL commit happens before Stripe or Paymob session creation on the first-create path
- provider response fields and `PaymentIntentCreatedEvent` are then persisted in a follow-up transaction
- replay/resume paths were kept so incomplete local intents can still be finalized safely

Impact:

- removes the main case where provider-side payment truth could exist without a durably committed POS authority row
- narrows recovery to post-commit provider-field persistence instead of pre-commit authority loss

Primary files:

- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`

### 2. Critical storefront order reads now self-repair projections before serving Mongo reads

Implemented:

- added targeted storefront order projection repair service
- account order history, order detail, tracking, guest tracking recovery, and order-by-number paths now trigger repair from SQL authority before Mongo read
- controllers still serve Mongo read models, but critical order surfaces no longer rely only on background reconciliation timing

Impact:

- closes the common "order exists in SQL but missing/stale in storefront read model" failure for critical customer-facing order reads
- keeps CQRS read flow while reducing customer-visible desynchronization

Primary files:

- `PointOfSale.API/Services/Storefront/StorefrontOrderProjectionRepairService.cs`
- `PointOfSale.API/Controllers/StorefrontAccountController.cs`
- `PointOfSale.API/Controllers/StorefrontOrdersReadController.cs`
- `PointOfSale.API/Program.cs`

### 3. CommerceHub readiness gating now prefilters backlog to affected tenant scope

Implemented:

- unresolved compensation, unresolved payment/refund webhooks, authoritative outbox, MassTransit outbox, and marketplace outbox checks now prefilter by tenant-related scope before bounded inspection
- fail-closed behavior remains, but unrelated platform backlog is less likely to freeze healthy tenants

Impact:

- reduces control-plane collateral damage where one global backlog previously blocked outward money movement for tenants not actually affected
- preserves conservative integrity behavior for tenants whose scope does match unresolved records

Primary files:

- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs`

### 4. Architecture guards were updated to lock these invariants in place

Implemented:

- added/updated architecture tests for post-commit payment authority ordering
- added/updated architecture tests for storefront critical order read repair
- added/updated architecture tests for tenant-scoped readiness gating

Primary files:

- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- `PointOfSale.Tests/Security/GuestOrderTrackingSecurityTests.cs`
- `PointOfSale.Tests/Security/StorefrontProductionBlockerFixTests.cs`

### Verification from the later session

Executed:

```powershell
dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -nologo -v minimal -m:1
dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore -nologo -v minimal -m:1
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build -nologo -v minimal --filter CrossSystemCriticalRiskHardeningTests
```

Result:

- passed

### Still not fully eliminated

These still need more production work even after the fixes above:

- refund flows can still enter explicit external-outcome-unknown reconciliation states when provider truth cannot be proven
- payout flows still rely on `ReconciliationRequired` when external bank/provider completion cannot be proven automatically
- broader read-model consistency outside critical storefront order reads still depends on projection freshness and reconciliation jobs

## Recommended Next Chat Prompt

If you want to continue this work in a new chat, use something close to:

> Read `docs/architecture/cross-system-critical-risk-handoff-2026-05-07.md` and continue the remaining cross-system integrity work, starting with backlog tenant-scope readiness gating and external outcome unknown reconciliation.
