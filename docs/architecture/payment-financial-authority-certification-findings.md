# Payment Financial Authority Certification (PASS M3-G)

## Implemented Financial Artifacts

Actual frontend runtime money artifacts found in inspected code:

- Storefront checkout/status/order tracking: order total, total amount, currency, payment status amount evidence, Stripe/Paymob handoff amount/currency, manual wallet/COD presentation.
- CommerceHub customer checkout/orders/recovery/refunds: cart subtotal, shipping, total, promo discount total, split sub-order totals, item unit price and totals, payment initiation amount/currency, refund amount, refund unit-price snapshot, refund quantity, refund currency, payout details.
- POS vendor finance/payments/earnings/statements: current balance, amount owed by vendor, hard limit amount, minimum payment to restore ordering, total settlement payments, total earnings, commissions, gross commissions, commission reversals, refunds, payouts, payable, eligible payout amount, holdback, completed/pending payouts, payment request amount, statement credits/debits/opening/closing/current balances, signed amounts, running balance.
- Super Admin payments/refunds/payouts/finance: vendor payment request amount/currency, refund amount/currency, refund unit-price snapshot, order total/shipping, item total, platform commission, vendor earning, refund financial preview deltas, payout total payable, eligible amount, holdback, completed/pending payouts, currency summaries, ledger overview/cashflow/P&L/reconciliation, vendor adjustment/settlement typed amount.

No inspected frontend-created durable payment amount, fee, commission, refund amount, settlement amount, vendor earning, payout total, financial reconciliation, or financial status authority was found.

## Findings

- Financial findings: backend payment, order, refund, settlement, payout, commission, ledger, vendor earnings, debt, reconciliation, and provider services own financial truth. Frontend code may validate, format, filter, sort, chart, withhold, refetch, or submit backend-bound intent only.
- Customer findings: checkout, pending, payment status, order details, recovery, and refunds derive money from current backend DTOs. CommerceHub order financial boundaries validate parent/split/item totals, payment handoff amount/currency, and refund unit-price/quantity/amount evidence. Customers cannot fabricate payable/refund amounts through route, storage, cache, or retry state.
- Vendor findings: finance, payments, earnings, debt, statements, payout methods, payment proofs, and payout history require current backend vendor scope and DTO shape. Vendor balances and earnings are rendered from backend rows; cached rows are withheld during current refresh. Vendor payment submissions still send backend-bound intent and endpoint authorization remains decisive.
- Admin findings: monitoring rows are not financial authority. Super Admin payment/refund selections are current-list derived. Runtime hardening added Super Admin payout financial validation so queue totals, currency summaries, row breakdowns, payout methods, and execution responses fail closed before presentation/action.
- Refund findings: customer refund amount must equal backend unit-price snapshot times quantity. Super Admin refund detail validates amount/currency, item totals, commission/vendor earning evidence, payment-record status, execution status, return status, and completed-refund execution evidence. Refund execution remains backend-owned.
- Settlement findings: payout queue money is now accepted only as coherent backend queue evidence. Execution amount must be finite, positive, and no greater than current backend eligible amount. Mixed-currency queue aggregation cannot create a combined money total.
- Provider findings: Stripe/Paymob/client/provider artifacts are transient evidence only. Provider redirects/callbacks trigger backend status or recovery reads and cannot create amount, currency, fee, refund, settlement, or reconciliation truth.
- Order consistency findings: payment initiation requires exact current order/provider/amount/currency evidence. Order/split/item total contradictions fail closed in customer checkout/order/recovery paths. Refund amount contradictions fail closed in customer and Super Admin paths.
- Bootstrap findings: initial load, refresh, focus, visibility, reconnect, history, and BFCache paths re-enter backend authority or withhold financial presentation. Payout execution modals close when current queue authority disappears.
- Cache findings: React Query, React state, selected rows, route/history state, localStorage, sessionStorage, and mutation responses cannot restore payment financial authority. Payout queue data is validated as a complete current backend response; missing totals are no longer substituted with frontend zeroes.
- Failure findings: malformed DTOs, 401/403/404/409/422, failed refresh, and failed mutations do not create replacement totals. They fail closed, withhold presentation, or require backend refetch.
- Adversarial findings: financial replay, refund replay, payout replay, settlement replay, storage replay, history replay, route replay, actor replay, tenant replay, and mutation replay cannot fabricate, preserve, restore, override, infer, or execute payment financial authority on inspected frontend paths after M3-G hardening.

## Runtime Hardening Performed

- Added `payoutFinancialAuthority` for Super Admin payout queue and execution response DTOs.
- Removed frontend zero substitution for missing Super Admin payout queue totals.
- Reject duplicate/contradictory payout queue rows, malformed payout methods, invalid risk flags, mixed-currency aggregate authority, and currency summary contradictions.
- Bound Super Admin payout execution amount to the current backend queue row and rejected amounts above current `eligibleAmount`.
- Validated payout execution mutation responses against current vendor ID and submitted amount.
- Removed the Super Admin payment-monitoring frontend sum for "Approved In View"; the card now shows a row count only, while approved money remains per-row backend evidence.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts`
- `PointOfSale.Web/src/super-admin/payouts/payoutsService.ts`
- `PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx`
- `PointOfSale.Web/src/super-admin/payments/PaymentsPage.tsx`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.test.ts`
- `PointOfSale.Tests/Architecture/PaymentFinancialAuthorityCertificationTests.cs`

## Architecture Updates

- Added this M3-G findings file.
- Updated authority context, hardening context, known risks, project context, and Codex rules with M3-G payment financial authority truth and freeze.

## Certification Answers

- Who owns payment financial authority? Backend payment, order, refund, settlement, payout, commission, vendor earnings, debt, ledger, reconciliation, provider, tenant, and authorization services.
- Can frontend fabricate money? No.
- Can customers fabricate amounts? No inspected customer path can do so without current backend amount/currency evidence.
- Can vendors fabricate balances? No inspected vendor path can do so without current backend vendor financial DTOs and endpoint authorization.
- Can refunds exceed payment authority? No inspected frontend path can approve or execute such a refund; customer/Super Admin refund amount contradictions fail closed and backend remains decisive.
- Can payouts exceed earnings authority? No inspected frontend payout execution path can submit an amount above current backend eligible payout authority after M3-G hardening.
- Can provider callbacks create financial truth? No.
- Can payment totals contradict order totals? Inspected current-order payment handoffs and order financial DTOs reject amount/currency and split/item contradictions before presentation.
- Can stale money survive refresh? No on inspected paths; stale presentation is withheld or re-derived from current backend authority.
- Can cache restore money? No.
- Can replay restore money? No.
- Can tenant leakage occur? No inspected frontend financial path introduces tenant leakage; backend endpoint authorization remains final.
- Were runtime changes required? Yes.
- Is Payment Financial Authority production-certifiable? Yes, for the inspected frontend runtime paths, contingent on backend authorization, ledger, payout, refund, payment, and reconciliation boundaries remaining in force.

## Verification

- `npm test -- --run src/super-admin/payouts/payoutFinancialAuthority.test.ts`
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~PaymentFinancialAuthorityCertificationTests`
- `npm run build` in `PointOfSale.Web` passed after rerunning outside the sandbox when the first sandboxed build could not read the Vite config path.
