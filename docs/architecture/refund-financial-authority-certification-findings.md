# PASS M4-F Refund Financial Authority Certification Findings

Date: 2026-06-22

Status: Refund Financial Authority is production-certifiable for the inspected frontend runtime paths after customer total-derivation removal and Super Admin financial DTO hardening.

## Implemented Financial Artifacts

- Customer refund evidence: `unitPriceSnapshot`, `amount`, `currency`, and `quantity` on backend-issued refund rows. The create intent contains `orderItemId` and `quantity`, never an amount, currency, total, payout amount, or financial refund ID.
- POS vendor refund-impact evidence: `amount`, `signedDebtAmount`, `currency`, `refundId`, and impact metadata in current vendor-financial scope.
- Super Admin monitoring: `amount`, `currency`, and `quantity` on backend-issued rows.
- Super Admin detail: `amount`, `unitPriceSnapshot`, `currency`, order `totalAmount` and `shippingTotal`, item `unitPrice`, `totalPrice`, `platformCommissionAmount`, and `vendorEarningAmount`.
- Super Admin financial preview: `commissionReversalAmount`, `vendorEarningChange`, `paymentRefundedAmountBefore/After`, vendor net earnings/payable/debt before/after, and signed platform/vendor/commission deltas.
- Super Admin execution: `financialRefundId`, `paymentId`, `commissionReversalAmount`, `vendorEarningReversalAmount`, execution status, and provider/reference evidence.

Not implemented in inspected frontend runtime: `refundAmount`, `approvedAmount`, `executedAmount`, `remainingAmount`, `refundTotal`, `refundSubtotal`, `refundTax`, `refundShipping`, `vendorImpact`, `settlementImpact`, or `payoutAmount` as frontend-owned refund financial fields.

## Findings

- Customer: refund creation submits only current backend-authorized item/quantity/payout intent. The modal no longer multiplies unit price by quantity or presents a frontend-generated refund total; the backend confirms the amount after review. Accepted history/detail rows require backend financial evidence.
- Vendor: refund impacts remain read-only, current vendor/session/tenant-scoped backend financial evidence. No vendor refund mutation or balance calculation path was found.
- Super Admin: monitoring is visibility only. Detail, financial preview, and execution fields fail closed unless all implemented money, signed-delta, currency, payment/refund identifiers, and execution evidence are well formed. Execution mutations re-enter current list and matching detail authority before intent submission.
- Payment, order, and return consistency: payment, order, return, provider, and execution fields are validated backend evidence only. The frontend neither reconciles contradictions nor derives a refund from any of those systems.
- Bootstrap, cache, and failure: React Query, selected rows, route/history state, dialogs, storage, and mutation responses are candidates only. Fetching/error states withhold customer rows and Super Admin dialogs/details require current validated list/detail authority. 401/403/404/409/422 and malformed DTOs cannot create replacement money.
- Adversarial: financial, storage, history, route, selection, mutation, actor, and tenant replay cannot restore a frontend-authoritative amount. Backend endpoint authorization and current DTO validation remain decisive.

## Runtime Hardening Performed

- Removed customer `unitPriceSnapshot * quantity` refund-total calculation and replaced it with backend-confirmation copy.
- Added Super Admin financial-preview and execution validation before any financial fields can reach presentation, including `financialRefundId`, payment-refund progression, signed deltas, payout execution evidence, and financial reversal values.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`
- `PointOfSale.Tests/Architecture/RefundFinancialAuthorityCertificationTests.cs`

## Verification

- `npm test -- src/super-admin/refunds/refundLifecycleAuthority.test.ts --run` — 12 passed.
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~RefundFinancialAuthorityCertificationTests` — 1 passed.
- `npm run build` passed in both `PointOfSale.CommerceHub` and `PointOfSale.Web`.

## Explicit Certification Answers

- Who owns refund financial authority? Backend refund/payment/order/return/ledger/settlement SQL authority and endpoint authorization.
- Can frontend or customer fabricate refund amounts? No.
- Can vendor fabricate balances or refund impact? No.
- Can monitoring create money? No.
- Can refunds exceed payment or order authority, or can return authority create refund money? No frontend path can make that true; backend remains authoritative.
- Can stale money, cache, or replay restore financial authority? No.
- Can tenant leakage occur? No inspected frontend path widens backend tenant scope.
- Were runtime changes required? Yes.
- Is Refund Financial Authority production-certifiable? Yes, for the inspected frontend runtime paths.
