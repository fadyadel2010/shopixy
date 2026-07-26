# PASS M3 CommerceHub Payment Authority Certification Findings

Date: 2026-06-10

Status: CommerceHub Payment Authority is production-certifiable for the inspected runtime paths. No runtime code hardening was required; existing SQL, provider-evidence, lifecycle, idempotency, refund, payout, and reconciliation controls already satisfy the M3 authority model.

## Payment identity findings

CommerceHub payment identity is owned by PostgreSQL `CommerceHubPayments`, `CommerceHubPaymentItems`, `CommerceHubPaymentAttempts`, and `CommerceHubPaymentLogs`, tied to the SQL `CommerceHubMarketplaceOrder`. Provider identity is linked through unique SQL provider reference, provider intent, and provider transaction boundaries. Paid records require processed state, paid timestamp, and provider transaction evidence, and only one live paid payment may exist per marketplace order.

Payment identity cannot be spoofed from DTOs, routes, frontend state, projections, caches, or stale events. Initiation reloads the SQL marketplace order, validates customer/order-access authority, checks current payment-method and reservation authority, acquires a payment lock, and creates or reuses SQL payment rows only inside the CommerceHub payment mutation boundary.

## Collection findings

Collection truth derives from signed provider evidence plus stored SQL payment/order truth. Stripe webhooks use `EventUtility.ConstructEvent`; Paymob webhooks use HMAC verification. Provider amount and currency are compared to the SQL payment record before collection is recognized. Provider identity is checked against other payment rows before mutation.

Payment success runs under the payment boundary, payment-lifecycle boundary, order lifecycle boundary, row reloads, distributed payment lock, and serializable SQL transaction. Duplicate provider success for an already paid order is not recognized again; it is routed to compensation/refund handling. Unmatched captured money is persisted as unresolved payment evidence for suspense/reconciliation rather than discarded or silently accepted.

## Vendor earnings findings

Vendor earnings are generated only by `CommerceHubFinancialWorkflowService` after online payment success or COD delivery collection. Earnings derive from immutable SQL sub-order financial snapshots and the collected payment id for online orders. Live original earnings are database-unique per sub-order through the filtered `CommerceHubVendorEarnings.SubOrderId` index where `OriginalEarningId IS NULL AND IsDeleted = false`.

Earnings cannot inflate through replay because existing non-pending earnings must match the locked financial snapshot and cannot be reassigned to another payment id. Earnings cannot suppress refunds incorrectly because refunded or partially refunded payment states cannot create or unlock vendor earnings.

## Marketplace fee findings

Marketplace fee truth derives from SQL sub-order commission snapshots: platform commission, promo discount, final commission, and vendor earning fields. `CommerceHubFinancialWorkflowService` validates commission snapshot JSON against persisted fields and verifies arithmetic before ledger or earning creation.

Marketplace fees cannot diverge from collected value on inspected paths because provider collection must match payment amount/currency, order/sub-order financial snapshots are checkout-created SQL truth, and ledger posting consumes those snapshots rather than projection or frontend totals.

## Escrow findings

CommerceHub Payment Authority may create collected value truth but does not create settlement truth. Online captured funds become platform-held SQL/ledger facts; vendor earnings remain pending/available according to delivery, payout delay, ledger confirmation, payout reservation, and payout status. Payout services, not payment collection, own outward release.

Escrow/holding state cannot release early on inspected paths because payout creation requires available earnings, settlement delay, ledger confirmation, currency consistency, payout readiness, and payout reservation. Payment collection does not bypass payout, settlement, treasury, or readiness gates.

## Refund interaction findings

Refund authority remains separate. Provider refund webhooks update provisional refund evidence only after payment/currency matching, duplicate refund detection, remaining-refundable bounds, and immutable payment-item checks. Refund services own completed refund lifecycle, reversal amounts, payout-overlap handling, and reconciliation-required states.

Refunds cannot corrupt payment authority on inspected paths because over-refunds fail closed, duplicate provider refund events collapse, missing immutable payment-item authority is flagged for manual review, and vendor earning reversals are modeled as refund-owned adjustments rather than rewriting payment identity or completed payout history.

## Reconciliation findings

Reconciliation is derivative. It may discover, persist, classify, route to suspense, or report unresolved external-money evidence, but it cannot create collected value without re-entering the SQL payment authority boundary. Unresolved captured payment and refund evidence remains explicit and readiness-blocking until reconciled.

Financial reconciliation validates payments, refunds, vendor earnings, ledger rows, payouts, and aggregate financial consistency from SQL. It can prove internal SQL correctness, not external gateway truth without provider evidence.

## Currency findings

CommerceHub payment currency is SQL payment/order authority and platform-base marketplace currency. Provider callbacks must match the persisted payment currency; mismatches reject, quarantine, flag manual review, or route unresolved evidence instead of relabeling money. Vendor earnings, fees, refunds, ledger rows, and payout eligibility remain currency-scoped.

No inspected path lets provider payloads, tenant display currency, frontend state, projections, dashboards, or stale snapshots corrupt CommerceHub payment currency.

## Tenant-isolation findings

Payment initiation requires authenticated customer ownership or signed order-access evidence. Webhook ingress is tenantless at HTTP host level but resolves authority from signed provider evidence plus stored SQL payment/order rows. Vendor earnings and payout/read surfaces filter by trusted tenant/store id.

No inspected path allows foreign payments, earnings, allocations, or refunds to be viewed or mutated through route ids, DTO tenant ids, projections, or cache state.

## Concurrency findings

Payment initiation uses per-order/provider locks, unresolved payment checks, active reservation checks, provider-session persistence fences, provider identity uniqueness, and SQL uniqueness. Webhook processing uses per-payment locks and serializable SQL transactions. Paid payment uniqueness, provider reference uniqueness, provider transaction uniqueness, webhook duplicate checks, and compensation routing prevent duplicate collection from creating duplicate value.

Concurrent callbacks, payment retries, and reconciliation attempts converge to collected, ignored, compensation-required, retry, or reconciliation-required states rather than duplicate earnings or duplicate fees.

## Historical-consistency findings

Payment records, payment items, attempts, logs, sub-order financial snapshots, vendor earnings, refund evidence, and ledger transactions are SQL history. EF guards block direct mutation of payment, refund, earning, and financial lifecycle fields outside their boundaries. Reruns use existing SQL rows and idempotency/fingerprint evidence rather than rebuilding financial history from projections.

Stale snapshots cannot resurrect payment authority because payment recognition reloads current SQL order/payment/reservation state at mutation time.

## Adversarial findings

Payment spoofing is blocked by signed provider evidence and SQL amount/currency/provider identity matching. Duplicate collection is blocked or routed to compensation. Vendor earning inflation is blocked by SQL snapshot validation, live original earning uniqueness, payment-id locking, and payout ledger confirmation. Vendor earning suppression through refund replay is blocked by refund-owned bounded adjustments and payment states that cannot unlock earnings after refund.

Fee manipulation is blocked by immutable SQL commission snapshots and arithmetic validation. Escrow bypass is blocked by payout readiness, ledger confirmation, and payout reservations. Refund corruption is blocked by cumulative amount bounds, duplicate refund detection, and reconciliation-required states. Currency manipulation is blocked by SQL/provider currency comparison. Tenant guessing, DTO tampering, route tampering, projection poisoning, and stale event replay are derivative-only and fail closed on inspected paths.

## Runtime hardening performed

No runtime code changes were required for PASS M3. The inspected runtime already contained the required controls.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/CommerceHubPaymentAuthorityCertificationTests.cs`

## Architecture updates

Updated this findings file and all five `.ai-context` authority files with PASS M3 payment authority truth and freeze rules.

## Explicit answers

- Who owns CommerceHub payment authority? PostgreSQL `CommerceHubPayments`, `CommerceHubPaymentItems`, `CommerceHubPaymentAttempts`, provider logs, unresolved webhook evidence, marketplace order payment fields under `CommerceHubPaymentLifecycleAuthority`, and financial workflow/ledger rows under their certified boundaries.
- Can payments be replayed? Provider and initiation replay cannot duplicate collected value on inspected paths; replay collapses to existing SQL state, no-op, compensation, retry, or reconciliation-required handling.
- Can vendor earnings diverge from payment truth? No on inspected paths; online earnings are payment-id-linked and SQL snapshot-derived, COD earnings require delivery/COD collection authority, and live original earnings are unique per sub-order.
- Can marketplace fees diverge from collected value? No on inspected paths; fees derive from SQL financial snapshots validated before ledger/earning creation and collection must match SQL payment amount/currency.
- Can escrow state drift? No inspected drift path was found; held/pending/releasable value remains SQL/ledger/payout-readiness derived and payment collection cannot create settlement truth.
- Can refunds corrupt payment authority? No on inspected paths; refund lifecycle is separate, bounded, idempotent, and reconciliation-aware.
- Can reconciliation create value? No. Reconciliation is derivative and must re-enter payment/refund authority before mutation.
- Can tenant leakage occur? No inspected path exposed or mutated foreign payment, earning, refund, or allocation authority.
- Can currency corruption occur? No inspected path allowed provider/frontend/projection currency to override SQL payment currency.
- Were runtime code changes required? No.
- Is CommerceHub Payment Authority production-certifiable? Yes, for the inspected runtime authority paths, with broader launch monitoring, alerting, external retention, and PostgreSQL-backed chaos evidence remaining operational maturity debt.
