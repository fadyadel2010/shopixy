# PASS 16.5B Settlement & Merchant Balance Certification

Date: 2026-06-02

Verdict: CommerceHub settlement and merchant-balance runtime is production-certifiable for the inspected code paths, but public launch remains not ready until the broader PASS 15.6/16.1B deployment, evidence-retention, alert-pack, backup/restore, SMTP/session, and PostgreSQL-backed chaos blockers are closed.

## Financial Inventory

| Entity or surface | Authority owner | Source of truth | Derivative consumers |
|---|---|---|---|
| `CommerceHubFinancialTransaction` | CommerceHub finance ledger | PostgreSQL append-only ledger row with entity, bucket, reference, idempotency, correlation, currency, and amount | Wallets, finance dashboards, reconciliation, payout eligibility, commission debt |
| `CommerceHubVendorEarning` | Financial workflow and payout services | PostgreSQL earning rows tied to sub-orders, payouts, refunds, reservation and paid-out amounts | Payout queue, vendor finance UI |
| `CommerceHubVendorSettlement` | Super Admin financial control | PostgreSQL settlement evidence with vendor, idempotency key, external reference, actor, notes | Vendor debt dashboard, settlement history |
| `CommerceHubVendorPaymentRequest` / `CommerceHubVendorPaymentProof` | Vendor debt repayment workflow plus Super Admin review | PostgreSQL request, immutable method snapshots, proof content hash, review state, ledger correlation | Vendor payment dashboard, Super Admin review queue |
| `CommerceHubPayout` / `CommerceHubPayoutItem` | Payout service | PostgreSQL payout rows, earning allocation rows, external handoff and completion evidence | Payout read service, reconciliation, vendor dashboards |
| `CommerceHubRefund` / `CommerceHubRefundRequest` / `CommerceHubRefundExecution` | Refund and return lifecycle services | PostgreSQL refund/return records plus gateway execution evidence | Reconciliation, customer and Super Admin refund screens |
| `CommerceHubMarketplaceOrder` / `CommerceHubSubOrder` | Checkout, order lifecycle, and financial workflow | PostgreSQL order/sub-order financial snapshots: total, commission, vendor earning, payment method, status | Order projections, dashboards, reconciliation |
| `CommerceHubFinancialOperationAudit` / `CommerceHubFinancialAuditLog` / reconciliation reports | Finance audit and reconciliation services | Append-only SQL evidence guarded by EF and database evidence controls | Forensics, dashboards, incident review |
| `CommerceHubVendorPayoutMethod` | Merchant payout method service | Vendor-scoped PostgreSQL payout method rows | Merchant payout UI, Super Admin payout execution |
| `CommerceHubVendorPaymentMethod` | Super Admin collection method service | PostgreSQL platform collection method rows | Merchant debt-payment UI |

## COD Findings

COD flow:

Customer pays merchant cash. The merchant holds customer cash. The ledger records vendor cash collection and vendor commission receivable debt. Platform commission is credited to platform receivables. Vendor settlement payments or approved debt clearances reduce the debt.

Runtime hardening performed in this pass: `CommerceHubVendorDebtService` now treats `amountOwed >= threshold` as immediate suspension from new orders. The default threshold remains `1000m` EGP. The prior 3-day grace period remains operator metadata only; it no longer permits new CommerceHub orders above the hard limit.

Enforcement points:

- Checkout: `CommerceHubCheckoutService` calls `GetVendorsSuspendedFromNewOrdersAsync` and rejects with `VENDOR_SUSPENDED_FOR_SETTLEMENT`.
- Merchant visibility/product discovery: `CommerceHubPublicVendorVisibilityService` and `CommerceHubQueryService` filter suspended vendors.
- Fulfillment and settlement: no new-order bypass was found; existing orders can continue through lifecycle and settlement.

## Online Payment Findings

Online flow:

Customer pays platform through the online payment lifecycle. The platform holds customer money after capture. Ledger order recognition credits platform cash and creates vendor payable/commission facts from checkout snapshots. Payout services later move eligible ledger-confirmed vendor payable out to the merchant.

Order totals, item price, commission, promo, shipping, and payment method are fixed by checkout SQL snapshots. Finance does not recalculate customer-visible price from derivative state.

## Merchant Balance Findings

Merchant balance authority is ledger-derived:

- Current wallet balance: vendor ledger credits minus debits.
- Payable balance: vendor ledger payable bucket.
- COD debt/receivable balance: vendor ledger receivable bucket plus legacy-safe settlement compatibility.
- Pending/available payout amounts: payout read service, ledger-confirmed earnings, payout reservations, refund locks, currency checks, and configured payout delays.

Balances can drift only if a future path bypasses ledger append/idempotency, direct-mutates evidence, or introduces a dashboard-only shadow balance. Existing inspected paths derive from SQL ledger and fail closed on mixed currency.

## Settlement Record Findings

Settlement records require vendor scope, amount, currency, idempotency key, external reference, notes, and actor evidence. Duplicate idempotency replays must match original values. External references are unique. Settlement/debt-clearance posting writes mirrored vendor/platform ledger transactions under serializable financial state capture and verifies expected deltas before committing audit evidence.

No duplicate settlement path was validated on inspected runtime paths.

## Commission Request Findings

Commission obligations are created from COD order ledger commission debits and reduced only by settlement/debt-clearance or approved vendor payment requests. Vendor-submitted payment requests remain pending until Super Admin approval. Approval records ledger correlation/idempotency and writes audit evidence.

Commission obligations can drift if a future commission payment path skips debt snapshot validation or ledger posting; the new freezes guard the current approved workflow.

## Payment Method Findings

Merchant payout methods are vendor-scoped through `CommerceHubVendorPayoutMethods`. Supported methods are Vodafone Cash, InstaPay, and Bank Transfer. Super Admin collection methods for vendor debt repayment are platform-owned rows in `CommerceHubVendorPaymentMethods`.

No inspected path exposes another tenant's payout method as merchant authority. Future list/create/update paths must keep explicit vendor filters and must not rely on frontend tenant ids.

## Reconciliation Findings

`FinancialReconciliationService` validates marketplace orders, sub-orders, promo cross-checks, refunds, orphan ledger rows, external money lifecycle, and daily aggregates. It writes a SQL reconciliation report, emits finance audit evidence, and triggers alert hooks on warning/critical results.

Reconciliation can prove correctness for inspected SQL facts: orders, ledger rows, balances derived from ledger, settlements, refunds, and aggregate validity. It cannot prove external bank/gateway outcomes without provider or bank evidence, so unresolved states remain first-class reconciliation states.

## Refund Impact Findings

Refund execution validates cumulative overrun, payout overlap, return/refund policy, commission reversal, promo reversal, vendor earning reversal, and post-payout debt impact. Refunds can move to `ReconciliationRequired` when gateway or payout certainty is incomplete. Existing guards prevent refund reversal from pushing vendor debt below zero.

No double reversal or missing reversal path was validated in inspected services.

## Dashboard Findings

Merchant and platform finance dashboards derive from SQL ledger, wallet service, vendor debt snapshots, and reconciliation read models. Dashboard values are not authority by themselves. Mixed-currency balances fail closed instead of collapsing multiple currencies.

Remaining dashboard debt: production alert packs and drill dashboards for reconciliation, parity, payout, debt-threshold, and refund anomalies remain launch prerequisites.

## Recovery Findings

Replay is bounded by ledger financial fingerprints and idempotency checks. Duplicate ledger writes must match the original financial fact or fail closed. Payout and refund hosted recovery services preserve `ReconciliationRequired` for uncertain external outcomes.

Remaining debt: no full PostgreSQL-backed concurrent chaos harness for settlement, payout, refund, commission-debt, and recovery races.

## Auditability Findings

Financial operations append SQL audit evidence with actor/system identity, correlation id, before/after financial state, reference type/id, idempotency, reason, and outcome. Settlement, commission repayment, payout, refund, and reconciliation facts are reconstructable from SQL evidence on inspected paths.

Deployment-level evidence immutability and external WORM/SIEM retention are still governed by PASS 15.6 blockers.

## Chaos Findings

Validated controls cover double settlement, double payout evidence, double ledger replay, payout/refund overlap, negative debt after refund, mixed-currency balance collapse, COD debt threshold bypass through checkout, public vendor visibility, and search/query visibility.

Remaining chaos debt: a database-backed concurrency harness for simultaneous checkout, COD debt crossing, vendor settlement, refund, payout, replay, and reconciliation remains required before final public-launch approval.

## Monitoring Findings

Reconciliation reports, audit logs, readiness gates, finance dashboards, and alert hooks exist. Production alert packs and dashboards for settlement telemetry, balance telemetry, payout telemetry, commission telemetry, and reconciliation telemetry remain deployment blockers inherited from PASS 15.6.

## Required Answers

1. Who owns customer money in COD flows? The merchant physically owns the cash after customer payment; internally SQL ledger records vendor cash collection and platform commission receivable debt.
2. Who owns customer money in online-payment flows? The platform owns captured online funds until ledger-confirmed vendor payable is paid out.
3. Can merchant balances drift? Inspected runtime balances are ledger-derived and should not drift; future non-ledger shadow balances or direct mutations would reintroduce drift.
4. Can settlements be duplicated? Inspected settlement paths use vendor idempotency and unique external evidence to prevent duplicate settlement.
5. Can commission obligations drift? Inspected obligations are ledger-derived and reduced through settlement/payment approval; drift is possible only if future paths bypass ledger/debt validation.
6. Can refunds corrupt balances? Inspected refund paths validate cumulative totals, payout overlap, commission reversal, and debt non-negativity; no active corruption path was validated.
7. Can reconciliation prove financial correctness? It can prove internal SQL financial correctness for inspected facts; it cannot prove external outcomes without external evidence.
8. Is the 1000 EGP debt rule implemented? Yes. This pass hardened the rule so `amountOwed >= 1000 EGP` immediately suspends new orders.
9. Can the debt rule be bypassed? No inspected checkout, public visibility, or query path bypass was found after hardening. Existing order fulfillment remains allowed and is not new-order acceptance.
10. Is CommerceHub settlement lifecycle production-ready? Runtime settlement/merchant-balance lifecycle is production-certifiable for inspected paths, but public launch remains blocked by broader deployment, monitoring, retention, backup/restore, and chaos-proof prerequisites.

## Runtime Hardening Performed

- Changed `CommerceHubVendorDebtService` so hard-threshold debt suspends immediately at `amountOwed >= threshold`.
- Updated vendor debt messaging so operators see that new CommerceHub orders are suspended at the hard limit.

## Regression Freeze Added

- `CommerceHubSettlementCertificationTests`
- `CommerceHubMerchantBalanceCertificationTests`
- `CommerceHubFinancialReconciliationCertificationTests`
- Updated `CommerceHubVendorFinancialControlServiceTests`

Frozen behavior: merchant balances, settlement authority, commission authority, COD lifecycle, online-payment lifecycle, refund financial impact, reconciliation behavior, payout ownership, financial recovery behavior, dashboard authority, and COD debt-threshold enforcement.

## Architecture Updates

This finding file, `.ai-context` PASS 16.5B entries, and the regression tests are the current authority record for settlement and merchant-balance certification.

## Remaining Debt

- PASS 15.6 public-launch blockers remain: deployment-applied evidence roles, external WORM/SIEM retention, outage-surviving audit handoff, alert packs, dashboards, backup/restore, and operator rehearsal evidence.
- Production alert packs for settlement, balance, payout, commission, refund, debt threshold, and reconciliation are still required.
- PostgreSQL-backed chaos tests for concurrent settlement, payout, refund, replay, recovery, and COD debt crossing remain required.
- Production query-plan/capacity review for finance dashboards and reconciliation jobs remains required.
