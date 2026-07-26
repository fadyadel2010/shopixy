# Vendor Earnings Financial Authority Certification Findings

Date: 2026-06-23

PASS M6-F

## Verdict

Vendor Earnings Financial Authority is production-certifiable for the inspected CommerceHub vendor earnings, vendor finance, statement, payout history, Super Admin payout queue, bootstrap, cache, recovery, and failure runtime paths. Backend SQL, ledger/workflow services, tenant and actor authorization, locks, idempotency, and endpoints own money, balances, settlement, payout, reserve, refund, and return effects. Frontend only renders validated backend evidence, formats it, and submits backend-bound payout-method or completed-transfer evidence intent.

## Financial Findings

- Financial truth is issued by backend reads: `/api/commercehub/earnings`, `/api/admin/commercehub/finance/dashboard`, `/api/admin/commercehub/finance/statement`, server CSV export, and the Super Admin payout queue/execution workflow.
- Actual vendor earnings vocabulary: `pendingTotal`, `availableTotal`, `paidOutTotal`, `inPayoutTotal`, `reversedTotal`, `totalEarnings`, `totalPayable`, `eligiblePayoutAmount`, `holdbackAmount`, `completedPayoutsTotal`, and `pendingPayoutsTotal`.
- Actual vendor-finance vocabulary: `currentBalance`, `amountOwedByVendor`, debt/readiness values, `totalSettlementPaymentsToPlatform`, `totalCommissions`, `grossCommissions`, `commissionReversals`, `totalRefunds`, `totalPayouts`, refund impacts, and statement `openingBalance`, `closingBalance`, `filteredCredits`, `filteredDebits`, `signedAmount`, and `runningBalance`.

## Runtime Authority Findings

Backend services calculate earning lifecycle totals and read the payout snapshot. `CommerceHubVendorPayoutReadService` derives payable, eligible, holdback, payout-reservation, reconciliation, currency-conflict, refund, delivery, and settlement effects. `CommerceHubEarningsService` projects those backend values; no frontend path writes an earnings, ledger, settlement, balance, reserve, commission, fee, refund, return, or payout record.

## Calculation Findings

The frontend has no money calculator, aggregation, chart, export, or optimistic financial write that becomes authority. Adapter arithmetic is strictly fail-closed evidence coherence validation: lifecycle totals must agree with backend earning rows; eligible may not exceed payable; holdback must equal backend payable less backend eligible; and consolidated earnings/statement rows must share the response currency. It never substitutes, persists, posts, or feeds a derived value to a mutation.

## Dashboard Findings

Vendor earnings and finance cards format accepted DTO fields only. The Super Admin payout queue validates backend currency summaries and only displays a queue-wide aggregate when the backend declares one authoritative currency; otherwise it splits presentation by currency. Dashboard cards, table counts, pagination calculations, and chart/table display are non-authoritative presentation.

## Statement Findings

Statement filters and paging are backend-bound intent. Opening/closing/current balances, filtered credits/debits, signed amounts, running balances, references, and CSV bytes are backend-issued. The adapter rejects foreign vendor evidence, malformed numbers/IDs/dates, incoherent page metadata, unsupported transaction states, and mixed-currency rows before rendering.

## Payout Findings

Vendor payout authority is backend-owned. Vendor UI has no payout execution action; direct vendor payout execution endpoints return `409 PAYOUT_EXECUTION_WORKFLOW_REQUIRED`. The Super Admin workflow may submit an already-executed transfer amount and provider/bank evidence, but its backend reauthorizes eligibility, method, tenant, actor, idempotency, and ledger effects. The browser cannot create available, reserved, in-payout, or paid balance authority.

## Settlement Findings

Settlement delay, settlement records, payable release, reserve, and reconciliation are backend payout-read/workflow evidence. A statement `Settlement` row is display/filter evidence only and cannot create an earning, balance, release, or payout authority in the frontend.

## Lineage Findings

Order, payment, refund, return, settlement, and payout identifiers remain backend lineage evidence. Current-tenant earning rows, payout rows/items, finance transactions, and statement references must validate before presentation. No frontend route, dialog, cache entry, query parameter, or mutation maps order/payment/refund/return/settlement/payout money into an earnings authority.

## Replay Findings

Refresh, focus, visibility, reconnect, browser history, periodic session validation, and BFCache restoration re-enter backend session authority and scoped reads. Stale or replayed financial DTOs can only be display candidates; they are withheld during revalidation and rejected when scope, lifecycle, currency, payout breakdown, or page coherence is invalid.

## Cache Findings

React Query is in-memory only and vendor queries are tenant/actor scoped. Actor replacement clears query state; authority loss removes the CommerceHub vendor subtree. There is no localStorage/sessionStorage financial cache, React Query hydration, optimistic financial patch, or mutation response merge that can preserve financial truth after backend revocation.

## Tenant Findings

Backend trusted tenant context owns scope. The frontend accepts dashboard, earning, payout, payout-item, method, and statement evidence only when backend vendor/entity evidence matches the current tenant. Cross-tenant query reuse, foreign financial rows, and tenant transfer fail closed.

## Actor Findings

Backend authentication/authorization owns actor scope. The client submits no actor or tenant selector for financial reads. Actor, role, or tenant transitions clear current query state and revalidate session evidence before protected presentation returns.

## Failure Findings

401/403 trigger authority-loss purge and session revalidation. 404/409/422 remain backend outcomes. Missing, malformed, conflicting, forged, negative/incoherent payout-readiness totals, foreign rows, mixed currencies, unsupported states, and replayed financial DTOs fail closed and do not become money or balance truth.

## Adversarial Findings

React state, cache tampering, mutation responses, route/search params, browser history, dialogs, tables, charts, local/session storage, refresh, reconnect, and BFCache cannot fabricate money or balances, compute authoritative earnings/payouts, restore revoked authority, or convert lineage evidence into authority. A forged amount can at most affect an untrusted browser view; it cannot pass backend mutation authority, ledger posting, or payout eligibility checks.

## Runtime Hardening Performed

- Added fail-closed payout-readiness coherence validation for backend `totalPayable`, `eligiblePayoutAmount`, `holdbackAmount`, completed payouts, and pending payouts.
- Added single-currency validation for consolidated vendor earnings and statement presentation, preventing rows in a different currency from being displayed under a single backend aggregate.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`

## Regression Freezes Added

- Updated `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`.
- Added `PointOfSale.Tests/Architecture/CommerceHubVendorEarningsFinancialAuthorityCertificationTests.cs`.

## Architecture Updates

- Added this findings document and recorded PASS M6-F in the required authority context files.

## Actual Financial Artifacts Discovered

- Earnings states: `Pending`, `Available`, `InPayout`, `PaidOut`, `Reversed`.
- Payout states: `Pending`, `Processing`, `ReconciliationRequired`, `Completed`, `Failed`.
- Backend financial transactions: `OrderPayment`, `Commission`, `Refund`, `Payout`, `Adjustment`, `Settlement` with credit/debit direction, signed amount, balance, reference, correlation, currency, and timestamp.
- No separate frontend-created gross/net/platform-fee/reserve/available/paid-balance artifact exists.

## Explicit Certification Answers

- Who owns Vendor Earnings Financial Authority? Backend.
- What financial artifacts actually exist? The backend-issued earnings, payout, finance-dashboard, statement, refund-impact, settlement, and ledger vocabulary listed above.
- Can frontend fabricate money or balances? No; it cannot create backend authority, and malformed/contradictory presentation evidence is rejected.
- Can frontend calculate earnings or payouts? No authoritative calculation. It only performs fail-closed coherence checks and formatting.
- Can financial authority emerge or replay? No. Replay may trigger a backend read only.
- Can stale financial values, dashboard totals, or frontend-derived aggregates become authoritative? No.
- Can order, payment, refund, return, settlement, or payout money become earnings authority? No; each remains backend lineage evidence.
- Can tenant leakage occur? No on inspected runtime paths.
- Were runtime changes required? Yes, payout-breakdown and consolidated-currency coherence validation.
- Is Vendor Earnings Financial Authority production-certifiable? Yes, for the inspected runtime paths.
