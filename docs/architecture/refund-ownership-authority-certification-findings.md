# PASS M4-B Refund Ownership Authority Certification Findings

Date: 2026-06-21

Status: Refund Ownership Authority is production-certifiable for inspected frontend runtime paths after customer refund creation revalidation hardening.

## Implemented Ownership Artifacts

- `refundId`: customer refund history, embedded customer order refunds, POS vendor refund-impact evidence, Super Admin refund monitoring/details/actions.
- `orderId`: customer refund history order navigation, customer order detail ownership parent, Super Admin refund monitoring/detail order binding.
- `orderItemId`: customer refund creation target, customer order item embedding parent, Super Admin refund detail/return binding.
- `vendorId`: customer refund history vendor evidence, POS vendor financial/refund-impact scope, Super Admin refund monitoring/detail/return binding.
- `customerId`: Super Admin refund monitoring/detail/return binding.
- `tenantId`: POS vendor current financial scope evidence via current backend-hydrated vendor session.
- `paymentId`: Super Admin financial preview/execution evidence only.
- `returnId`: owning-vendor return mutation target and Super Admin read-only return/refund lifecycle evidence.
- `financialRefundId`: Super Admin execution financial lineage evidence.

Not found as frontend refund ownership authority fields in inspected runtime: `RefundOwnerId`, `RefundCustomerOwnerId`, `RefundVendorOwnerId`, `RefundTenantId`, `SplitRefundOwnerId`, `ProviderRefundOwnerId`, `RefundOwnershipToken`.

## Ownership Findings

Backend owns Refund Ownership Authority, Customer Ownership, Order Ownership, Payment Ownership, Split Refund Ownership, Vendor Visibility, Admin Visibility, tenant scope, actor scope, and endpoint authorization. Frontend owns display, filtering, selection, navigation, validation, withholding, refetching, and backend-bound intent only.

## Customer Findings

Customer refund history accepts only backend-returned refund rows passing financial and visibility boundaries with `refundId`, `orderId`, `orderItemId`, `vendorId`, amount, currency, status, payout, and evidence validation. Customer order details render embedded refund rows only inside the current backend-owned order/item response. Refund creation now re-enters current backend order detail authority immediately before mutation and submits the current backend-backed `orderItemId` only if `canRefund` and `maxRefundableQuantity` still authorize it. Saved payout selection is revalidated against the current backend payout-method list before submission.

## Vendor Findings

Vendor refund-impact evidence is financial participation presentation only. POS vendor finance dashboards require current backend vendor/session/tenant scope before `recentRefundImpacts` can render, and refund-impact rows carry `refundId`, amount, currency, date, and impact type only as backend evidence. Vendor runtime does not create, transfer, or widen refund ownership.

## Admin Findings

Super Admin refund monitoring is visibility only. List rows validate `refundId`, `orderId`, `vendorId`, and `customerId`; selected `refundId` is a candidate re-derived from the current accepted list. Details and mutation responses must match the current selected list row's refund/order/vendor/customer/amount/currency evidence before presentation. Search/filter state and URL state cannot become ownership.

## Order Consistency Findings

Customer refund creation is bound to the current backend-owned order item. Customer refund history order links are navigation candidates only. Super Admin details bind refund ownership to the current monitoring row's `orderId`, item `vendorId`, and `customerId`; embedded return evidence must match the same refund/item/customer/vendor lineage. Frontend does not infer refund ownership from split grouping or order presentation.

## Payment Consistency Findings

Payment identifiers and references are evidence only. Super Admin financial preview/execution can display `paymentId` and `financialRefundId`, but payment ownership cannot become refund ownership. Customer retry/recovery/provider paths re-enter payment/order authority and do not create refund authority.

## Customer-Vendor Findings

Customer-owned refund presentation cannot become vendor ownership. Vendor refund-impact rows cannot become customer ownership. The only bridge is backend-issued refund/order/item/vendor evidence already accepted by current endpoint authorization.

## Vendor-Admin Findings

Vendor refund-impact evidence cannot become admin ownership. Admin monitoring cannot become vendor ownership. Selected admin rows remain current-list candidates and mutation responses are re-bound to current backend selection.

## Bootstrap Findings

Initial load, refresh, focus, visibility, reconnect, history, and BFCache paths re-enter backend query authority or withhold stale presentation. Customer refund rows are hidden while fetching or on error. Customer order details withhold while fetching. Super Admin refund lists/details use current-list/current-selection binding and close stale selected state.

## Cache Findings

React Query, component state, URL/search params, and selected IDs are derivative. Customer `activeRefundId` selects only from current accepted rows. Customer refund modal state cannot submit until current order and payout authority are refetched. Super Admin selected refund is re-derived from current accepted monitoring rows.

## Failure Findings

401, 403, 404, 409, 422, failed refresh, failed mutation, and malformed DTO paths fail closed through endpoint authorization, DTO boundaries, current-refetch withholding, and selected-state clearing. Failure cannot create replacement refund ownership.

## Adversarial Findings

Ownership replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, tenant replay, stale payout replay, and malformed DTO replay cannot create, transfer, expand, restore, replay, merge, substitute, or fabricate refund ownership without current backend authority.

## Runtime Hardening Performed

- Customer refund creation now refetches current backend order detail authority before mutation and submits only the current backend-backed `orderItemId` when current `canRefund` and `maxRefundableQuantity` still allow it.
- Saved payout-method selection now refetches current backend payout-method authority before mutation. Newly saved payout details must appear in the current backend payout-method list before their ID can be used in the refund request.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/RefundOwnershipAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/refund-ownership-authority-certification-findings.md`

## Explicit Certification Answers

- Who owns refund ownership authority? Backend refund/order/payment/customer/vendor/tenant/actor SQL and endpoint authorization authorities.
- Can frontend fabricate ownership? No.
- Can Customer A access Customer B refunds? No inspected frontend path bypasses backend customer refund/order authority.
- Can Vendor A access Vendor B refund evidence? No inspected frontend path bypasses current vendor/session/tenant financial scope.
- Can monitoring become ownership? No; monitoring is visibility and selected-row candidate state only.
- Can refund ownership contradict order ownership? No inspected path can establish that contradiction as frontend authority; customer creation revalidates current order item and Super Admin details bind to current order/item/customer/vendor evidence.
- Can payment ownership become refund ownership? No.
- Can stale ownership survive refresh? No inspected path preserves stale ownership as current authority.
- Can cache restore ownership? No.
- Can replay restore ownership? No.
- Can tenant leakage occur? No inspected frontend path widens tenant scope; backend authorization remains final.
- Were runtime changes required? Yes.
- Is Refund Ownership Authority production-certifiable? Yes, for inspected frontend runtime paths.
