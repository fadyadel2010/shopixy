# Payment Visibility Authority Certification (PASS M3-E)

## Implemented runtime visibility paths

- Storefront checkout, pending payment status, order confirmation, order tracking, Paymob return, Stripe pending outcome, and authenticated order history.
- CommerceHub customer checkout, order list, order detail payment status/reference, payment retry/recovery, refund request history, refund evidence, and refund payout presentation.
- POS/Admin vendor CommerceHub finance, earnings, payments, manual payment proof, payout method, payout history, settlement/statement, and refund-impact presentation.
- POS customer/supplier payment modals and manual wallet/payment review paths.
- Super Admin vendor payment monitoring, payment-method management, proof access, refund monitoring/detail/execution, payout monitoring, finance dashboard, order monitoring, and user risk/financial snapshots.

## Findings

- Visibility findings: payment visibility is derivative from current backend DTOs only. Frontend route, search, selection, cache, storage, history, provider, recovery, and mutation state can select candidates or trigger reads, but cannot grant or widen payment visibility.
- Customer findings: customer checkout, order details, payment status, payment history, refund history, and recovery paths require current backend order/refund/payment evidence. Runtime hardening now validates customer refund list rows and embedded order-detail refund rows before payout/evidence/order-link presentation.
- Vendor findings: vendor finance, earnings, payments, manual proof, settlement, payout, and statement views remain scoped by current backend vendor/tenant/session evidence through the existing financial participation boundary.
- Admin findings: Super Admin payment/refund monitoring remains visibility only. Selected payment/refund actions are ID candidates re-derived from current validated backend lists, and payment-method mutation responses now validate as method rows.
- Recovery findings: recovery links/tokens are lookup/access candidates only. Retry re-fetches current backend retry authority and validates exact order/provider/amount/currency before handoff.
- Refund findings: refund visibility cannot exceed payment/order visibility. Customer refund rows now require backend refund/order/item/vendor/status/media/payout evidence; embedded existing refunds are bounded to the current visible order item amount.
- Order consistency findings: payment/refund visibility is accepted only inside current backend order/split/financial/lifecycle boundaries; frontend split/grouping does not infer payment visibility.
- Bootstrap findings: initial load, refresh, focus, reconnect, history, and BFCache paths re-enter backend authority or withhold presentation.
- Cache findings: React Query cache is derivative. Customer refund pages suppress cached rows while fetching or failed, and evidence selection resolves only against the current accepted refund list.
- Failure findings: 401/403/404/409/422, failed refresh, malformed DTOs, and mutation errors do not create replacement visibility.
- Adversarial findings: visibility replay, recovery replay, storage replay, history replay, route replay, actor replay, tenant replay, search replay, and selection replay cannot fabricate or restore payment visibility on inspected paths.

## Runtime hardening performed

- Added CommerceHub customer payment/refund visibility authority for refund list rows and embedded order-detail refund rows.
- Routed customer refund list/create responses through visibility validation after financial validation.
- Routed customer order detail embedded refund rows through visibility validation and duplicate rejection.
- Corrected Super Admin payment/refund GUID validators so valid backend GUIDs are accepted and malformed IDs fail closed.
- Corrected Super Admin payment-method creation response validation to use payment-method evidence, not payment-request evidence.

## Exact runtime files modified

- `PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts`
- `PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts`
- `PointOfSale.CommerceHub/src/services/customer-refunds-service.ts`
- `PointOfSale.Web/src/super-admin/payments/paymentOwnershipAuthority.ts`
- `PointOfSale.Web/src/super-admin/payments/paymentsService.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`

## Regression freezes added

- `PointOfSale.Web/src/payment/paymentVisibilityAuthority.test.ts`
- `PointOfSale.Tests/Architecture/PaymentVisibilityAuthorityCertificationTests.cs`

## Architecture updates

- Added this M3-E findings file.
- Updated authority context, hardening context, known risks, project context, and Codex rules with the M3-E freeze.

## Certification answers

- Who owns payment visibility authority? Backend payment, customer, vendor, admin, recovery, refund, settlement, tenant, order, provider, and authorization services.
- Can frontend fabricate visibility? No.
- Can Customer A see Customer B payments? No inspected frontend path can do so without current backend authority.
- Can Vendor A see Vendor B payments? No inspected frontend path can do so without current backend authority.
- Can recovery links grant visibility? No; they are backend lookup/access candidates only.
- Can refund visibility exceed payment visibility? No; refund visibility is bounded by current backend refund/order/payment evidence.
- Can payment visibility contradict order visibility? No inspected frontend path accepts payment/refund visibility outside current backend order visibility boundaries.
- Can stale visibility survive refresh? No.
- Can cache restore visibility? No.
- Can replay restore visibility? No.
- Can tenant leakage occur? No inspected frontend visibility path introduces tenant leakage.
- Were runtime changes required? Yes.
- Is Payment Visibility Authority production-certifiable? Yes, for the inspected frontend runtime paths, contingent on existing backend authorization remaining in force.

## Verification

- `npm test -- --run src/payment/paymentVisibilityAuthority.test.ts src/super-admin/payments/paymentOwnershipAuthority.test.ts src/super-admin/refunds/refundLifecycleAuthority.test.ts`
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~PaymentVisibilityAuthorityCertificationTests`
- `npm run build` in `PointOfSale.CommerceHub`
- `npm run build` in `PointOfSale.Web` passed when rerun outside the sandbox after sandbox ACL denial loading `vite.config.ts`.
