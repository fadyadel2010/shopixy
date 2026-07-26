# Vendor Earnings Lifecycle Authority Certification Findings

Date: 2026-06-23

PASS M6-C

## Lifecycle Findings

- Runtime vocabulary is backend-defined: earnings are `Pending`, `Available`, `InPayout`, `PaidOut`, or `Reversed`; payouts are `Pending`, `Processing`, `ReconciliationRequired`, `Completed`, or `Failed`.
- `Available` is the earning eligibility state. `InPayout` is a reservation state, not a settlement or payout completion state. `Reversed` is the earning reversal state. Holds are backend readiness/risk evidence (`VendorSuspended`, `DeliveryHold`, `RefundWindowOpen`, `SettlementDelayActive`, and related flags), not frontend lifecycle transitions.

## Runtime Authority Findings

- Backend SQL entities, financial workflow, payout/refund/compensation services, reconciliation worker, locks, idempotency, tenant context, actor authorization, and endpoints own lifecycle truth.
- The frontend only validates current backend DTO evidence before display. It creates no earning status, payout status, settlement status, eligibility, hold, release, reversal, or transition state.

## Transition Findings

- Backend transitions discovered: `Pending → Available` in the financial workflow; `Available → InPayout` on payout reservation; `InPayout → PaidOut` on completed payout; `InPayout → Available` on permitted payout failure/release; non-paid earnings may become `Reversed` through refund/compensation handling. Payout transitions are `Pending → Processing → Completed`, `Processing → ReconciliationRequired`, or permitted non-executed release to `Failed`.
- Frontend does not call any earnings lifecycle mutation. Payout-method creation is destination intent only and invalidates/refetches evidence.

## Settlement Findings

- Settlement delay and settlement ledger confirmation are backend eligibility gates. Settlement transaction rows and risk flags are display/filter evidence only and cannot become an earning state or move an earning.

## Payout Findings

- Active payout lifecycle is separate from earnings lifecycle. An `InPayout` earning must be bound to a current backend active payout row and payout item. A completed positive earning may have its active `PayoutId` cleared by backend runtime; immutable payout-history item lineage remains its completion evidence.
- The frontend rejects contradictory active reservation, payout-item, status, date, tenant, or lifecycle-total evidence.

## Replay Findings

- Refresh, reconnect, focus, visibility, navigation history, and BFCache restoration revalidate the backend session and re-read scoped data. They cannot replay a lifecycle transition or turn cached status into an accepted grant.

## Cache Findings

- React Query is in-memory and tenant/actor-scoped. Rendering withholds the dashboard during a current fetch; authority-loss handling removes the vendor-session earnings/finance/payment subtrees. Cache hydration, local storage, and session storage do not persist lifecycle truth.

## Tenant Findings

- Backend tenant filters and endpoint authorization own tenant scope. Client adapters require current vendor/tenant evidence for dashboard, earning, payout, and payout-item rows; foreign or malformed evidence fails closed.

## Actor Findings

- Actor/session replacement clears query state and session bootstrap revalidates on recovery events. Previous actor lifecycle evidence cannot become current presentation authority.

## Failure Findings

- 401/403 cause authority-loss purge and withholding. 404/409/422 remain backend lifecycle outcomes. Missing, malformed, conflicting, unsupported, or stale lifecycle fields fail closed in the adapter.

## Adversarial Findings

- React state, query cache, route/search/history state, dialogs, table state, selected rows, storage, mutation responses, refresh/reconnect/BFCache recovery, and forged DTO fields cannot fabricate, advance, repair, restore, or replay lifecycle authority. Lifecycle totals, dates, active payout bindings, payout-item lineage, canonical statuses, and current vendor scope must agree before presentation.

## Runtime Hardening Performed

- Added lifecycle-shape validation for every earning status and date/binding combination.
- Added lifecycle-total reconciliation against the backend earning item snapshot.
- Bound `InPayout` evidence to an active backend payout and payout item, while preserving the live backend’s completed-payout rule that clears an active positive-earning `PayoutId` after settlement.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`
- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`

## Regression Freezes Added

- Added `PointOfSale.Tests/Architecture/CommerceHubVendorEarningsLifecycleAuthorityCertificationTests.cs`.
- Updated existing M6-A/M6-B architecture freezes and focused frontend adapter regression coverage.

## Architecture Updates

- Added this findings document and recorded PASS M6-C in all five authority context files.

## Actual Lifecycle States Discovered

- Earnings: `Pending`, `Available`, `InPayout`, `PaidOut`, `Reversed`.
- Payouts: `Pending`, `Processing`, `ReconciliationRequired`, `Completed`, `Failed`.
- Eligibility/hold evidence: availability time, backend payout-readiness gates, and backend risk flags; these are not frontend-owned lifecycle states.

## Explicit Certification Answers

- Who owns Vendor Earnings Lifecycle Authority? Backend.
- What lifecycle states exist? The earnings and payout states listed above.
- Can frontend fabricate lifecycle state? No; malformed or contradictory state is withheld.
- Can lifecycle emerge, replay, advance itself, or repair itself? No.
- Can lifecycle survive refresh? Only as non-authoritative cached evidence pending backend revalidation; never as current authority.
- Can cached lifecycle become authoritative? No.
- Can settlement lifecycle become earnings lifecycle? No.
- Can payout lifecycle become earnings lifecycle? No; backend lineage binds them without conflating them.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes, frontend lifecycle coherence validation.
- Is Vendor Earnings Lifecycle Authority production-certifiable? Yes, for the inspected CommerceHub vendor earnings, finance, payout, bootstrap, cache, and recovery runtime paths.
