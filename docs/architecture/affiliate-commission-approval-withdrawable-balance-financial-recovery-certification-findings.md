# PASS AF-PA5.5 Commission Approval, Withdrawable Balance & Financial Recovery Certification

Date: 2026-06-26

## Approval Findings

Commission Approval is backend-owned. Approval occurs only when `AffiliateOrderDeliveredConsumer` consumes `OrderDeliveredEvent` or `FulfillmentDeliveredEvent` and calls `AffiliateService.ApproveCommissionForDeliveredOrderAsync`, or when backend recovery/reconciliation re-enters the same approval authority guard.

The approval gate locks the current SQL order, verifies tenant/event agreement, physical delivery, manual delivery evidence when required, successful payment authority, and collection accounting authority before appending `AffiliateCommissionLedger` `Approved` rows copied from prior `Earned` rows.

Runtime hardening added the same `EnsureAffiliateDeliveryApprovalAuthority` guard to `ReconcileDeliveredCommissionApprovalsAsync`, closing a replay/recovery gap where record-time delivery reconciliation previously checked only `order.IsPhysicallyDelivered`.

## Withdrawable Balance Findings

Withdrawable balance begins only at backend `AffiliatePayoutService.GetAvailableBalanceCoreAsync` after `Approved` commission rows exist. `Earned`/Pending rows are excluded from the balance formula and cannot become withdrawable automatically.

The backend formula is: net approved commission per order item (`Approved - Reversed`, floored at zero) plus negative payout reductions plus negative withdrawals minus pending payout requests. `RawAvailable` is the guard value; `AvailableForWithdrawal` is display-only clamped presentation.

## Ledger Findings

`AffiliateCommissionLedger` remains append-only for commerce lifecycle rows: `Earned`, `Approved`, `Reversed`, and `Paid`. `AffiliateBalanceLedger` remains append-only for account-level money movements such as withdrawals and negative corrections. Both ledgers have globally unique idempotency keys.

Balances can be recomputed from append-only SQL ledger rows. `Paid` commission rows are lifecycle evidence only and do not reduce withdrawable balance; actual money leaving the wallet is represented by `AffiliateBalanceLedger.Withdrawal`.

## Recovery Findings

Hosted attribution reconciliation repairs missing attribution rows only from backend order snapshots and then re-enters `RecordCommissionForOrderAsync`. Recording may append `Earned` rows, reconcile refunds, and now re-enters the same delivery approval authority guard before appending `Approved` rows.

Recovery cannot fabricate withdrawable balance because balance calculation ignores pending/earned rows and derives withdrawable authority only from approved ledger rows net of reversals and account-level deductions.

## Replay Findings

Delivery replay and approval replay are idempotent through `AFFILIATE_COMM:APPROVED:{OrderItemId}`. Earned, reversed, paid, and balance withdrawal paths also use stable idempotency keys and unique indexes. Duplicate delivery, accounting, refund, and payout-related replays cannot duplicate approved balances.

## Failure Findings

Crash, timeout, rollback, duplicate events, and hosted-service restart converge through append-only SQL and idempotency. If accounting is not posted, commission recording defers. If earned commission is missing when delivery or refund arrives, backend throws for retry rather than silently fabricating terminal ledger state.

## Adversarial Findings

Replay Delivery: idempotent; duplicate `Approved` rows are blocked.

Replay Approval: idempotent; concurrent approval races are filtered and unique violations are treated as already-completed authority.

Replay Ledger: append-only unique idempotency prevents duplicate lifecycle rows.

Modify Balance: frontend/cache values are ignored; backend recomputes from SQL.

Modify Approval: approval requires backend order delivery/payment/accounting authority and existing `Earned` rows.

## Financial Lifecycle Pipeline

Commission Candidate: immutable checkout-admitted `OrderItem` snapshot.

Pending Commission: `AffiliateService.RecordCommissionForOrderAsync` creates `AffiliateCommissionLedger.Earned` after backend payment and accounting authority.

Delivery Validation: `AffiliateService.ApproveCommissionForDeliveredOrderAsync` locks and validates order delivery, payment, accounting, tenant, and manual evidence authority.

Commission Approval: `AffiliateCommissionLedger.CreateApproved(earned)` appends `Approved` rows.

Withdrawable Balance: `AffiliatePayoutService.GetAvailableBalanceCoreAsync` includes net `Approved - Reversed` amounts and excludes `Earned`.

Withdrawal Request: portal may request only against backend `RawAvailable`.

Paid: payout execution appends `AffiliateBalanceLedger.Withdrawal` and `AffiliateCommissionLedger.Paid` through the unified settlement orchestrator.

## Runtime Hardening Performed

`ReconcileDeliveredCommissionApprovalsAsync` now calls `EnsureAffiliateDeliveryApprovalAuthority` before any recovery/replay approval rows are appended.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliateCommissionApprovalWithdrawableBalanceCertificationTests.cs`

## Architecture Updates

Updated all five authority context files with PASS AF-PA5.5 truth, risk closure, hardening, and freeze rules.
