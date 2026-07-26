# PASS M5-G Return Scope Isolation Authority Certification Findings

Date: 2026-06-22

Status: Return Scope Isolation Authority is production-certifiable for the inspected frontend runtime after current-refund return-mutation response binding and POS purchase stale-response suppression.

## Scope Artifacts

Actual implemented scope artifacts inspected: `returnId`, `refundId`, `orderId`, `orderNumber`, `orderItemId`, `subOrderId`, `customerId`, `vendorId`, `tenantId` as current vendor/session scope evidence, `paymentId`, `executionId`, `financialRefundId`, `trackingNumber`, POS `returnNumber`, original invoice IDs/numbers, customer/supplier IDs, warehouse IDs, product IDs, variant IDs, and unit IDs. Frontend idempotency and request-attempt IDs are replay guards only.

## Scope Findings

- Backend owns Return Scope Authority, Customer Return Scope, Vendor Return Scope, Admin Return Scope, Tenant Scope, Refund Scope, Order Scope, Payment Scope, SQL lineage, locks, idempotency, and endpoint authorization.
- Frontend owns display, selection, filtering, navigation, presentation, validation, withholding, refetching, and backend-bound intent only.
- Runtime hardening was required to bind Super Admin return mutation responses to the current refund lineage and suppress stale POS purchase invoice/supplier reads.

## Domain Findings

- Customer findings: Customer refund history, order detail refund rows, evidence dialogs, and refund creation paths derive from current backend refund/order evidence. Refund creation and evidence upload re-enter current backend order-item authority. Customer A cannot access Customer B returns through inspected frontend paths without backend authorization.
- Vendor findings: Vendor finance refund impacts are current vendor/session scoped and read-only. `recentRefundImpacts` carries refund impact evidence, not return ownership or mutation scope. Vendor A cannot access Vendor B returns through inspected frontend paths.
- Admin findings: Super Admin monitoring is visibility only. Selected rows are re-derived from the current list; details and dialogs require current accepted detail. Return mutation responses are now rebound to the current refund detail's `refundId`, `orderItemId`, `customerId`, and `vendorId` before success handling.
- Customer-Vendor findings: Customer refund/order scope cannot become vendor scope. Vendor refund-impact scope cannot become customer scope. Frontend has no merge surface.
- Vendor-Admin findings: Vendor scope cannot become admin scope. Admin visibility cannot become vendor ownership. Frontend cannot transfer scope between roles.
- Refund consistency findings: Refund scope cannot create return scope. Return scope cannot exceed current refund/detail lineage, and contradictions fail closed.
- Order consistency findings: Order scope cannot create return scope. Order IDs, order-item IDs, and sub-order IDs are backend lineage evidence only.
- Payment consistency findings: Payment scope cannot create return scope. Payment IDs, provider references, execution IDs, and financial refund IDs are payment/refund evidence only.
- Tenant findings: Customer endpoints, current vendor tenant validation, Super Admin endpoint authorization, and POS `RequiredTenantId` controller derivation keep tenant scope backend-owned. No inspected frontend path leaks cross-tenant return scope.
- Bootstrap findings: Initial load, refresh, reconnect, focus, visibility, history, and BFCache cannot preserve return scope. Current queries validate or withhold, and stale selections/dialogs close.
- Cache findings: React Query, component state, dialogs, selected rows, route/search/history state, localStorage, and sessionStorage are candidates only. Cache cannot restore return scope.
- Failure findings: 401, 403, 404, 409, 422, malformed DTOs, failed reads, failed mutations, and substituted responses fail closed or require refetch. Failures cannot create replacement scope.
- Adversarial findings: Scope replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, and tenant replay cannot fabricate, restore, transfer, merge, substitute, leak, or bypass return scope without current backend authority.

## Runtime Hardening Performed

- Bound Super Admin return override and mark-failed mutation responses to the current backend-backed refund detail via `requireSuperAdminReturnForCurrentRefund`.
- Added POS purchase return request-id guards for initial data, purchase invoice lists, and invoice detail reads.
- Invalidated outstanding POS purchase invoice reads when supplier selection changes.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx`
- `PointOfSale.Web/src/pages/PurchaseReturns.jsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ReturnScopeIsolationAuthorityCertificationTests.cs`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-scope-isolation-authority-certification-findings.md`

## Explicit Certification Answers

- Who owns return scope authority? Backend.
- Can frontend fabricate scope? No.
- Can Customer A access Customer B returns? No inspected frontend path bypasses backend customer/refund/order authorization.
- Can Vendor A access Vendor B returns? No; vendor refund-impact evidence is current vendor/session scoped and read-only.
- Can customer scope become vendor scope? No.
- Can vendor scope become admin scope? No.
- Can monitoring widen scope? No; monitoring is visibility only.
- Can selected rows preserve scope? No; selections are candidates re-derived from the current backend list/detail.
- Can refund scope create return scope? No.
- Can order scope create return scope? No.
- Can payment scope create return scope? No.
- Can stale scope survive refresh? No inspected authority path permits stale scope to remain authoritative.
- Can cache restore scope? No.
- Can replay restore scope? No.
- Can tenant leakage occur? No inspected frontend path permits tenant leakage without backend authorization.
- Were runtime changes required? Yes.
- Is Return Scope Isolation Authority production-certifiable? Yes, for the inspected frontend runtime paths.
