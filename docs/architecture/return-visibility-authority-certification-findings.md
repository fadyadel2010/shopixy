# PASS M5-D Return Visibility Authority Certification Findings

Date: 2026-06-22

Status: Return Visibility Authority is production-certifiable for the inspected frontend runtime.

## Visibility surfaces

Actual implemented return visibility surfaces:

- CommerceHub customer refund history and status presentation, including refund evidence dialog and order navigation.
- CommerceHub customer order details, including order-item existing refund rows and refund request entry.
- POS vendor CommerceHub finance dashboard refund impacts (`recentRefundImpacts` and `totalRefunds`) as read-only financial evidence.
- Super Admin refund monitoring list, filtering, selected refund detail drawer, embedded return details, return override dialog, and return failure dialog.
- POS sales return and purchase return list/create screens, which are separate POS return workflows bound to backend controllers.

No standalone customer return tracking page, standalone vendor return detail page, vendor return mutation surface, customer return mutation surface, return import/repair tool, cached return projection screen, or frontend return visibility transfer/merge/restore surface was implemented in the inspected runtime.

## Visibility findings

Backend owns Return Visibility Authority, Customer Return Visibility, Vendor Return Visibility, Admin Return Visibility, Tenant Visibility, Refund Visibility, Order Visibility, Payment Visibility, SQL lineage, actor scope, tenant scope, and endpoint authorization. Frontend owns display, selection, filtering, navigation, presentation, validation, withholding, refetching, and backend-bound intent only.

## Customer findings

Customer return visibility is refund/order-derived and current-backend-bound. Refund history rows pass refund financial and visibility validation before display, and cached refund rows are withheld during current fetching or error. Order-detail embedded refund rows pass the same visibility boundary and are capped against the current item total. Refund request and evidence upload re-enter current backend order-item refund authority. Customer routes, order links, selected evidence rows, dialog state, stale cache, and refresh cannot widen return visibility. Customer A cannot see Customer B returns through any inspected frontend path without backend authorization.

## Vendor findings

The implemented vendor path is read-only financial refund impact presentation. `deriveVendorFinancialDashboardPresentation` requires the current backend POS user/tenant scope and rejects scope mismatch before presenting `recentRefundImpacts`. Refund impact rows carry refund IDs, impact type, amount, currency, and timestamp only; they do not expose return IDs or return transition authority. Vendor A cannot see Vendor B returns through the inspected frontend runtime.

## Admin findings

Super Admin monitoring is visibility only. Selected refund IDs are candidates re-derived from the current validated monitoring list. The detail drawer and return dialogs render only accepted current detail, and accepted detail is withheld while list or detail authority is fetching, failed, absent, or mismatched. Embedded return evidence must match the current refund, order item, customer, and vendor lineage. Standalone return reads and mutation responses must match the exact requested `returnId`. Monitoring, selected rows, dialogs, and filters cannot preserve or widen return visibility.

## Refund consistency findings

Refund visibility cannot create return visibility. Return visibility cannot exceed current backend refund visibility because embedded return details must be bound to the current backend-backed refund detail. The frontend cannot reconcile refund/return contradictions into authority.

## Order consistency findings

Order visibility cannot create return visibility. Customer order details validate the exact route order and embedded refund rows before presentation. Super Admin return evidence treats order and order-item IDs as lineage evidence only. Route-selected orders, order links, item rows, and split-order evidence cannot create or restore return visibility.

## Payment consistency findings

Payment visibility cannot create return visibility. Payment status, payment record status, provider references, gateway references, execution IDs, and financial refund IDs remain payment/refund evidence inside current backend refund detail; they are never return visibility grants.

## Tenant findings

Customer surfaces are bounded by backend customer order/refund endpoints. Vendor finance surfaces require current vendor tenant scope. Super Admin surfaces require current platform endpoint authorization plus current list/detail validation. POS sales/purchase return controllers derive tenant and original invoice lineage server-side under `RequiredTenantId`. No inspected frontend path widens tenant scope or leaks cross-tenant return visibility.

## Bootstrap findings

Initial load, refresh, reconnect, focus, visibility, history, route changes, and BFCache cannot preserve return visibility. Current queries validate or withhold. Super Admin polling and manual refresh re-enter current monitoring and detail authority; stale selections close when current authority disappears.

## Cache findings

React Query, component stores, contexts, selected rows, dialogs, route state, history state, localStorage, and sessionStorage are visibility candidates only. Customer refund/order surfaces withhold during fetch/error. Super Admin return dialogs require accepted current detail. Cache cannot restore return visibility after authority loss.

## Failure findings

401, 403, 404, 409, 422, malformed DTOs, unsupported statuses, malformed timestamps, duplicate IDs, refund/order/return contradictions, failed refreshes, failed mutations, and substituted mutation responses fail closed. Failures cannot create replacement return visibility.

## Adversarial findings

Visibility replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, tenant replay, refund replay, order replay, payment replay, provider-reference replay, and malformed DTO injection cannot fabricate, widen, transfer, merge, repair, replay, restore, or leak return visibility without current backend authority.

## Runtime hardening performed

No runtime code change was required in M5-D. Existing M4-D, M5-A, M5-B, and M5-C runtime boundaries already enforce current customer refund/order validation, current vendor scope validation, Super Admin current-list/current-detail binding, exact return target validation, stale dialog withholding, and POS backend tenant/invoice derivation.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/ReturnVisibilityAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-visibility-authority-certification-findings.md`

## Explicit certification answers

- Who owns return visibility authority? Backend return/refund/order/payment/customer/vendor/admin/tenant/actor SQL and endpoint authorization authorities.
- Can frontend fabricate visibility? No.
- Can Customer A see Customer B returns? No inspected frontend path bypasses backend customer/order/refund authorization.
- Can Vendor A see Vendor B returns? No; vendor refund impacts are current vendor/session scoped and read-only.
- Can monitoring become visibility authority? No; monitoring rows are visibility evidence only and selections are re-derived from current backend list/detail authority.
- Can selected rows widen visibility? No.
- Can refund visibility create return visibility? No.
- Can order visibility create return visibility? No.
- Can payment visibility create return visibility? No.
- Can stale visibility survive refresh? No.
- Can cache restore visibility? No.
- Can replay restore visibility? No.
- Can tenant leakage occur? No inspected frontend path widens tenant scope or bypasses backend tenant authority.
- Were runtime changes required? No.
- Is Return Visibility Authority production-certifiable? Yes, for the inspected frontend runtime.
