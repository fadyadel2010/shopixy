# PASS AF-PA5.4 Commission Candidate Lifecycle, Pending State & Financial Authority Certification

## Candidate Findings

Commission Candidates are backend-owned `OrderItem` snapshots. A candidate exists only when checkout Order Line Admission persisted `OrderItem.AffiliateCommissionEligible = true` with immutable policy, rate, base amount, commission amount, and captured-at snapshot fields. Candidate creation still cannot occur from frontend state, cart cache, Redis, browser storage, order-level attribution alone, or another line in a mixed order.

Candidate can exist without Pending Commission. Candidate means "this order line was admitted at checkout"; it is not a ledger entry and does not automatically become pending.

## Pending Findings

Pending Commission begins only when `AffiliateService.RecordCommissionForOrderAsync` creates an append-only `AffiliateCommissionLedger` row with `EntryType = Earned` from the immutable order-item snapshot. Runtime now requires successful backend financial lifecycle authority before this can happen: payment status must be `Collected`, `Settled`, or `PartiallyRefunded`, and collection accounting must be `Posted` or explicitly `NotRequired`.

Cancelled and refunded terminal orders are blocked before pending creation. Pending is not created for unpaid, rejected, expired, pending, authorized, awaiting-verification, COD-not-collected, failed, cancelled, or fully refunded orders.

## Lifecycle Findings

Checkout, order-line admission, and candidate persistence are separate from pending commission. Paid backend financial lifecycle events, COD collection, wallet approval, accounting-posted replay, and guarded admin/recovery retries may all call the same backend service, but none bypass the financial gates.

Delivered order events approve existing earned commissions; they do not own pending creation. Returned/refunded flows can reverse existing earned entries, but fully refunded orders cannot newly create pending after this pass.

## Financial Findings

Financial Authority begins in `RecordCommissionForOrderAsync` at the successful payment plus accounting gate, immediately before the service creates `AffiliateCommissionLedger.CreateEarnedFromSnapshot`. The ledger row is append-only and idempotent through `AFFILIATE_COMM:EARNED:{OrderItemId}` plus the unique `IdempotencyKey` index.

Frontend, Mongo projections, Redis, browser cache, affiliate order placeholders, admin tables, and portal history are display or retry evidence only. They cannot fabricate an `Earned` ledger row.

## Snapshot Findings

Pending commission uses checkout-time immutable order-item snapshots only. `RecordCommissionForOrderAsync` does not re-query live `AffiliateProductPolicy`, `AffiliateCategory`, `PromotionLink`, product price, or current category state to calculate pending. Product changes, price changes, category changes, policy changes, promotion-link deletion, and product disablement after checkout cannot modify the existing candidate snapshot.

Affiliate suspension after checkout no longer erases admitted candidates. The pending writer requires the affiliate identity row to still exist, but it does not require current `AffiliateStatus.Active` after checkout admission.

## Failure Findings

Rollback prevents pending creation because ledger writes, event publication, refund reconciliation, and delivery approval reconciliation run in the backend transaction. Retry and replay are idempotent: duplicate `Earned` rows for the same `OrderItemId` are blocked by the unique idempotency key and treated as idempotent skips. Failed processing throws or returns before ledger creation.

## Adversarial Findings

Replayed orders, replayed candidates, modified candidate fields, modified snapshots, modified commission rates, modified promotion links, stale product/category/policy state, and frontend DTO tampering cannot promote a candidate to pending without `RecordCommissionForOrderAsync` passing backend payment/accounting/order terminal-state gates and writing the append-only ledger.

## Financial Lifecycle Pipeline

Checkout -> Order Line Admission -> Commission Candidate -> Backend Payment/Accounting Lifecycle Validation -> Pending Commission (`AffiliateCommissionLedger.EntryType = Earned`) -> later delivery approval and financial ledger stages.

Pending Commission begins at `AffiliateService.RecordCommissionForOrderAsync` when `AffiliateCommissionLedger.CreateEarnedFromSnapshot` is added and saved.

Financial Authority begins in the same method at the payment status plus accounting status gate, before any `Earned` row is constructed.

## Runtime Hardening Performed

- Added terminal order-state blocking for `OrderStatus.Cancelled` and `OrderStatus.Refunded` before pending commission creation.
- Added explicit pending-commission payment authority with `HasPendingCommissionPaymentAuthority`.
- Restricted pending creation to `PaymentStatus.Collected`, `PaymentStatus.Settled`, or `PaymentStatus.PartiallyRefunded`.
- Preserved the existing accounting gate requiring `FinancialPostingStatus.Posted` or `FinancialPostingStatus.NotRequired`.
- Removed current active-affiliate status as a pending creation gate so post-checkout suspension cannot erase an admitted candidate.
- Added architecture freeze tests covering pending creation, terminal/unpaid blockers, immutable snapshots, suspension survival, and frontend non-authority.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliateCommissionCandidatePendingAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Who owns Pending Commission? Backend financial lifecycle, specifically `AffiliateService.RecordCommissionForOrderAsync` writing append-only `AffiliateCommissionLedger` earned entries.
- Can frontend fabricate Pending Commission? No.
- Can Commission Candidates automatically become Pending? No.
- Can cancelled orders create Pending? No.
- Can refunded orders create Pending? No for fully refunded terminal orders.
- Can duplicate Pending records occur? No; earned idempotency is unique per order item.
- Can product changes modify existing snapshots? No.
- Where does Pending Commission begin? At `AffiliateCommissionLedger.CreateEarnedFromSnapshot` inside `RecordCommissionForOrderAsync` after payment/accounting gates pass.
- Where does Financial Authority begin? At the backend payment status and financial posting status gate inside `RecordCommissionForOrderAsync`.
- Were runtime changes required? Yes.
- Is Commission Candidate Lifecycle production-certifiable? Yes, after this hardening and freeze.
