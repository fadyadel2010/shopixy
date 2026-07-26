# Payment Scope Isolation Authority Certification (PASS M3-H)

## Implemented Payment Scope Boundaries

- Storefront checkout/payment-status scope: backend checkout/order/payment status DTOs and checkout/tracking tokens.
- CommerceHub customer payment scope: customer checkout, order detail, payment retry/recovery, payment status, and refund request/list/detail evidence.
- CommerceHub customer refund/payout scope: customer refund rows, refund item evidence, refund payout details, and saved payout-method choices.
- POS vendor financial/payment scope: current backend POS vendor session, tenant/vendor-scoped finance, earnings, payments, statements, proof, and payout-method DTOs.
- Super Admin payment monitoring scope: backend payment-request rows, payment-method rows, proof access, and ID-only selected payment candidates.
- Super Admin refund monitoring scope: backend refund list/detail/action rows and current-list-derived selected refund candidates.
- Super Admin payout queue scope: backend vendor payout queue rows, payout methods, currency summaries, and current-row payout execution targets.
- Super Admin finance/settlement monitoring scope: backend finance dashboard/vendor finance rows and settlement/adjustment intent endpoints.

## Findings

- Scope findings: payment scope authority is backend-owned. Frontend payment scopes are derivative presentation/filter/navigation/selection only and must be re-derived from current backend evidence.
- Customer findings: Customer A cannot see Customer B payments/refunds through inspected checkout, orders, payments, refunds, recovery, or status paths without current backend authority. Recovery links are lookup candidates only and cannot widen customer scope.
- Vendor findings: Vendor A cannot see Vendor B payments, earnings, settlements, statements, payout methods, or payout requests through inspected POS vendor paths without current backend vendor/session/tenant authority.
- Admin findings: Super Admin payment/refund/payout monitoring remains visibility only. Search/filter/selected rows cannot become ownership or action authority; selected rows are re-derived from current backend-backed lists.
- Customer to vendor findings: customer order/payment/refund DTOs do not become vendor scope. Vendor finance/payment DTOs do not become customer scope. Frontend does not infer ownership across those roles.
- Vendor to admin findings: vendor scope cannot become admin scope. Super Admin visibility cannot become vendor ownership. Selected rows cannot transfer scope after M3-H hardening.
- Tenant findings: inspected payment, refund, payout, and settlement paths derive tenant scope from backend session/endpoints/DTO evidence. Tenant IDs in frontend state, filters, route/search params, storage, or selected rows cannot create tenant authority.
- Bootstrap findings: initial load, refresh, reconnect, focus, visibility, history, and BFCache paths re-enter backend authority or withhold stale payment presentation.
- Cache findings: React Query, React state, route/history state, localStorage, sessionStorage, and selected rows cannot restore foreign payment scope. M3-H additionally binds Super Admin refund details/mutation responses to the current monitoring row and payout method selection to the current queue row.
- Failure findings: 401, 403, 404, 409, 422, malformed DTOs, failed refreshes, and failed mutations fail closed, clear selection, withhold presentation, or require backend refetch; they cannot create replacement scope.
- Adversarial findings: scope replay, actor replay, tenant replay, storage replay, history replay, route replay, selection replay, and mutation replay cannot transfer, expand, preserve, restore, merge, leak, or fabricate payment scope without current backend authority.

## Runtime Hardening Performed

- Added Super Admin refund detail scope validation that binds detail and mutation-response DTOs to the current backend-backed monitoring row's refund, order, vendor, customer, amount, and currency evidence.
- Routed Super Admin refund detail reads through current selected-list scope validation.
- Validated Super Admin refund approve/reject/retry/execute mutation responses against the current selected monitoring row before detail-cache presentation.
- Added Super Admin payout method selection validation requiring the selected payout method to exist in the current backend-backed payout queue row before execution.
- Disabled Super Admin payout execution presentation when the selected payout method is absent from the current queue row.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundsService.ts`
- `PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx`
- `PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts`
- `PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`
- `PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.test.ts`
- `PointOfSale.Tests/Architecture/PaymentScopeIsolationAuthorityCertificationTests.cs`

## Architecture Updates

- Added this M3-H findings file.
- Updated authority context, hardening context, known risks, project context, and Codex rules with M3-H payment scope isolation authority truth and freeze.

## Certification Answers

- Who owns payment scope authority? Backend payment, customer, vendor, admin, refund, settlement, payout, tenant, actor, provider, order, and endpoint authorization authorities.
- Can frontend fabricate scope? No.
- Can Customer A access Customer B payments? No inspected frontend path can do so without current backend authority.
- Can Vendor A access Vendor B payments? No inspected frontend path can do so without current backend authority.
- Can customer scope become vendor scope? No.
- Can vendor scope become admin scope? No.
- Can admin visibility become ownership? No.
- Can tenant leakage occur? No inspected frontend path introduces tenant leakage; backend endpoint authorization remains final.
- Can stale scope survive refresh? No on inspected paths after current-list/current-queue rebinding.
- Can cache restore scope? No.
- Can replay restore scope? No.
- Were runtime changes required? Yes.
- Is Payment Scope Isolation Authority production-certifiable? Yes, for inspected frontend runtime paths, contingent on backend authorization and certified payment/order/refund/payout/settlement authorities remaining in force.
