# PASS M4-H Refund Final Authority Certification Findings

Date: 2026-06-22

Status: Production-certifiable for the inspected frontend runtime.

## Final findings

Backend services and endpoint authorization own refund identity, ownership, lifecycle, visibility, action, financial, and scope authority. Customer, POS vendor, and Super Admin frontends only validate, select, format, withhold, refetch, and submit backend-bound intent.

- Identity: all refund, return, execution, financial-refund, payment, order-item, customer, and vendor identifiers are validated as backend DTO evidence. No UI state creates or substitutes identity.
- Ownership and scope: customer creation/evidence paths re-enter the current backend order item; vendor refund impact is a current-tenant-bound read-only presentation; Super Admin detail must match the current monitoring row.
- Lifecycle, visibility, and actions: selected IDs, dialogs, cache entries, and route search parameters are candidates only. Super Admin refund actions and execution re-fetch the current list/detail before submission; return monitoring is read-only, and vendor logistics re-enters separate current tenant/order/return authority.
- Financial: the frontend does not submit refund amounts, currencies, execution amounts, financial refund IDs, or ledger facts. It only validates and displays coherent backend evidence.

## Cross-domain, bootstrap, cache, failure, and adversarial findings

Order, payment, return, vendor, and monitoring data never become refund authority: they are accepted only when a current backend refund DTO binds their identities and scope. Refresh, focus, reconnect, visibility changes, history/BFCache restoration, cross-tab state, selected rows, dialogs, React Query cache, and storage cannot restore authority because affected views withhold during refresh/failure and mutation paths re-enter backend authority. 401, 403, 404, 409, 422, and malformed DTOs fail closed; they create no replacement authority. Identity, actor, tenant, ownership, lifecycle, visibility, action, financial, and scope replay is therefore display-only or rejected.

## Runtime hardening performed

`RefundRequestModal` now requires its current-order-item resolver. Both refund creation and evidence upload always call it; no optional stale-modal fallback remains.

## Regression freezes

- `RefundFinalAuthorityCertificationTests`
- Updated `RefundActionAuthorityCertificationTests`
- Updated `RefundScopeIsolationAuthorityCertificationTests`

## Certification

Refund Authority is backend-owned and production-certifiable across the inspected frontend runtime. This is a frontend runtime certification; backend endpoint authorization remains the decisive enforcement boundary.
