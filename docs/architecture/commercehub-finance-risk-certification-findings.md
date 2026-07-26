# PASS 15.3 Marketplace Finance & Risk Certification

Date: 2026-06-01

## Financial authority inventory

- Order total authority: SQL `CommerceHubMarketplaceOrders` and `CommerceHubSubOrders`, created by checkout after server-side cart, listing, variant, stock, shipping, promotion, payment-method, and price authority checks.
- Item price authority: `CommerceHubPriceAuthority` over current SQL listing and variant pricing, including PASS 13.7 merchant override policy.
- Commission authority: persisted sub-order/item commission fields plus `CommissionSnapshotJson`; financial workflow validates snapshot fields and arithmetic before creating earnings or ledger entries.
- Marketplace revenue authority: append-only SQL `CommerceHubFinancialTransactions` for platform commission, promotion, cash, receivable, payable, refund, adjustment, settlement, and payout records.
- Vendor earnings authority: SQL `CommerceHubVendorEarnings`, created from validated sub-order financial snapshots and locked against non-pending repricing.
- Refund amount authority: SQL `CommerceHubRefunds` plus refund completion validation against sub-order and payment totals.
- Settlement and payout authority: SQL `CommerceHubPayouts`, `CommerceHubPayoutItems`, vendor earnings, external handoff token/evidence fields, and ledger-backed payable confirmation.
- Accounting authority: immutable SQL `CommerceHubFinancialTransactions` plus accounting outbox rows and core accounting ledger/journal services.
- Reconciliation authority: SQL reconciliation jobs and read services. MongoDB, Redis, Elasticsearch, provider payloads, and dashboard counts are evidence only.

## Money flow

Customer checkout computes current SQL-authoritative cart lines, shipping, promotion, payment method, commission snapshot, and sub-order totals. Order creation persists marketplace/sub-order snapshots. Online payment capture routes through payment lifecycle authority and `ApplyOnlinePaymentSuccessAsync`; COD routes through delivery financial workflow. Commission and vendor earning creation requires validated persisted snapshots. Ledger posting records platform cash, vendor payable, commission, and promotion entries idempotently. Vendor earnings become available only after delivery/delay rules. Payout creation reserves only available ledger-confirmed earnings and nets available negative adjustments. Payout processing requires readiness gates, no blocking refund/return state, a committed handoff token, and unique external evidence. Completion records payout ledger entries and marks earnings paid out. Accounting closure remains ledger/outbox based.

## Pricing findings

Checkout, cart, projection, and search pricing remain tied to PASS 13.7 `CommerceHubPriceAuthority`. Finance does not recalculate item price from projections or client payloads. Finance consumes the persisted order/sub-order snapshot and validates arithmetic before money movement.

## Commission findings

Commission is trustworthy for certified flows. The workflow rejects missing snapshots, malformed complete snapshots, final commission drift, promotion reversal drift, negative commission values, gross commission over subtotal, and non-pending earning repricing. Regression coverage freezes these rules in `CommerceHubFinancialWorkflowCommissionSnapshotTests` and `MarketplaceFinanceRiskCertificationTests`.

## Settlement findings

CommerceHub uses ledger-backed payable/cash buckets rather than a separate mutable settlement balance as financial truth. Payout creation and completion require ledger confirmation, serializable transactions, earning row locks, and readiness gates. Settlement-like payout ledger entries are idempotent by financial fingerprint.

## Payout findings

Payouts cannot exceed earned ledger-backed balances on certified paths. Creation selects available earnings, automatically nets available negative adjustments for the same vendor/currency, rejects non-positive net payout amounts, rejects mixed currencies, requires ledger confirmation, and reserves earnings into one payout. Processing/completion are serializable, externally fenced by handoff token and evidence uniqueness, and blocked by refund/return/reconciliation gates.

## Refund findings

Refunds are trustworthy for certified flows. Creation checks sub-order ownership, positive amount, idempotency, payment/refund limits, and existing non-failed refunds. Completion validates provider/manual confirmation, cumulative refund totals, payment totals, and blocks overlap with processing payouts. Post-payout refunds create vendor debt/negative adjustment evidence instead of silently reducing already paid money.

## Balance findings

Vendor balance reads derive from SQL earnings, payouts, refunds, and ledger transactions. Payout-read logic caps payable eligibility when ledger backing is absent, blocks mixed ledger currency, flags operational-vs-ledger currency conflicts, and does not treat pending payouts as available balance.

## Accounting findings

`CommerceHubFinancialTransaction` is append-only and currency-guarded. Ledger writes use idempotency keys plus a financial fingerprint over entity, reference, type, direction, bucket, amount, and currency. Duplicate replay is accepted only when the fingerprint matches; divergent idempotency collisions fail closed. Associated accounting outbox messages are detached on proven duplicate races.

## Recovery findings

Recovery cannot create or delete money on certified paths because it reloads SQL, uses reconciliation-required/processing states, and routes final ledger effects through the same idempotent ledger service. Duplicate ledger writes are safe only if already persisted as the same financial fact.

## Reconciliation findings

Existing reconciliation covers CommerceHub financial transactions, payouts, refunds, readiness/backlog gating, payout reconciliation-required states, refund recovery, and finance read diagnostics. Controls are sufficient for core production finance operation, but operators still need broader parity drill reports and alert packs.

## Risk findings

Double commission, settlement, payout, refund, replay money creation, replay money deletion, stale payout completion, refund/payout overlap, mixed currency, and ledger drift paths are actively fenced by snapshot validation, serializable transactions, row locks, external evidence uniqueness, handoff tokens, financial readiness gates, and immutable ledger fingerprints.

## Governance findings

Tenant merchants own product/listing pricing decisions under PASS 13.7. Marketplace finance owns commissions, settlements, payouts, marketplace revenue, financial readiness gates, payout evidence requirements, and reconciliation. Marketplace governance remains visibility authority and may not mutate merchant-owned listing prices.

## Audit findings

Financial workflow, payout, refund, commission, COD, vendor earning, and reversal events write CommerceHub audit records. Ledger rows and accounting outbox rows provide immutable financial evidence. Audit is complete for core finance paths; SIEM-grade export and immutable operator notification inbox remain operational maturity debt.

## Operations findings

Merchant and Super Admin finance surfaces expose earnings, payments, refunds, payouts, payout methods, wallet/balance snapshots, readiness flags, and reconciliation states. Core operator control is production-usable. Remaining operations work is richer drill-down parity reporting, long-running finance operation history, and alert dashboards.

## Chaos findings

Replay cannot create money unless a new path bypasses `CommerceHubFinancialLedgerService`; the certification test freezes that boundary. Recovery cannot delete money because append-only transactions are never updated or deleted as correction. Payouts cannot complete without the committed processing handoff and unique terminal evidence. Refunds cannot complete while cumulative totals overrun or while matching earnings are in processing payout states.

## Monitoring findings

Financial readiness gates, reconciliation-required states, payout risk flags, ledger drift flags, and CommerceHub reliability telemetry provide baseline observability. Monitoring is adequate for production launch with manual operations, but dedicated finance alert rules, reconciliation dashboards, and parity drill reports remain debt.

## Runtime hardening performed

Runtime hardening added the missing CommerceHub payment lifecycle boundary around delivered financial workflow so COD collection/payment-field mutations made by `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected` persist through the same reviewed boundary as other CommerceHub payment-state changes. The certification also added regression freeze coverage in `MarketplaceFinanceRiskCertificationTests` and documented the authority model. Existing runtime hardening verified in this pass includes serializable payout/refund/ledger transactions, ledger fingerprint idempotency, external payout evidence uniqueness, refund overrun validation, payout-read ledger drift caps, and readiness-gated outward money movement.

## Regression freeze added

Added `PointOfSale.Tests/Architecture/MarketplaceFinanceRiskCertificationTests.cs` to freeze:

- SQL/ledger money authority.
- Commission snapshot validation.
- Earning repricing locks.
- Ledger replay fingerprinting.
- Payout ledger confirmation and handoff/evidence fences.
- Refund overrun and payout-overlap fences.
- Readiness gates and reconciliation-required states.
- Required production-readiness answers in this document.

## Architecture updates

PASS 15.3 updates `.ai-context` finance authority guidance to make CommerceHub finance SQL/ledger authority explicit, preserve PASS 13.7 merchant price ownership, and require future finance changes to update this certification test and document.

## Remaining debt

- Dedicated SQL-vs-ledger-vs-payout-vs-refund parity drill reports.
- Finance alert pack and dashboard panels for reconciliation-required, ledger drift, evidence reuse rejection, and readiness gate blocks.
- Immutable operator notification inbox for high-risk finance actions.
- Production query-plan/index review for finance queue and reconciliation queries.
- Full chaos harness that executes concurrent payout/refund/replay/recovery scenarios against PostgreSQL, not only source and in-memory tests.

## Explicit answers

- Who owns money authority? PostgreSQL SQL rows and the immutable CommerceHub/core ledger own internal money authority; external gateways and banks own external execution evidence only.
- Can money be created through replay? No on certified paths. Ledger replay requires matching idempotency/fingerprint evidence and duplicate races detach pending rows/outbox messages.
- Can money be destroyed through recovery? No on certified paths. Recovery appends or reconciles through the same ledger services and cannot mutate/delete existing financial transactions.
- Can payouts exceed earned balances? No on certified paths. Payouts require available earnings, ledger confirmation, positive net amount after adjustments, currency unity, and row-locked reservation.
- Can refunds drift from settlements? Core drift is fenced. Refunds block processing payout overlap, validate cumulative totals, and create post-payout vendor debt/adjustments rather than rewriting paid earnings.
- Are commissions trustworthy? Yes for certified paths. Commission snapshots and stored fields must agree and pass arithmetic validation before earnings or ledger rows are created.
- Are balances trustworthy? Yes for core production use. Balance reads derive from SQL/ledger and flag or cap eligibility on ledger/currency drift.
- Are reconciliation controls sufficient? Sufficient for core production finance operation with manual oversight; richer parity drill reporting and alert packs remain debt.
- Are financial audits complete? Complete for core finance paths through audit rows, ledger rows, and accounting outbox evidence; SIEM/export and immutable notification maturity remains debt.
- Is CommerceHub finance production-ready? Yes for core production finance with the documented controls and operational debt. Do not scale outward money movement automation beyond these gates without closing the monitoring and chaos-harness debt.
