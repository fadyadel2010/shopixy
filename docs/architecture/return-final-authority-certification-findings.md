# PASS M5-H Return Final Authority Certification Findings

Date: 2026-06-22

Status: Updated 2026-09-03. Return logistics authority belongs to the owning POS vendor; Super Admin return access is read-only.

## Final Findings

- Backend owns Return Identity, Ownership, Lifecycle, Visibility, Action, Financial, Scope Isolation, Transition, Tenant, and Governance Authority.
- Frontend owns display, formatting, selection, dialogs, navigation, filtering, sorting, presentation, validation, withholding, refetching, and backend-bound intent only.
- Final runtime reinspection covered CommerceHub customer refund/order paths, POS vendor refund-impact and return-action presentation, Super Admin refund actions/read-only return presentation, POS Admin backend return endpoints, POS sales/purchase returns, cross-domain evidence, cache/replay, tenant isolation, and adversarial paths.

## Findings By Domain

- Identity findings: `returnId`, refund linkage, payment linkage, execution linkage, tracking evidence, and POS `returnNumber` remain backend-issued evidence. Frontend cannot create, replay, restore, substitute, or infer return identity.
- Ownership findings: customer ownership, vendor ownership, and admin visibility are endpoint/current-detail derived. Frontend cannot create, replay, transfer, or leak ownership.
- Lifecycle findings: implemented lifecycle remains exactly `Pending`, `PickupScheduled`, `PickedUp`, `InTransit`, `DeliveredToVendor`, `Confirmed`, and `Failed`. Frontend cannot advance, regress, repair, replay, or restore lifecycle.
- Visibility findings: customer, vendor, and admin visibility remain backend scoped. Frontend selection, filters, dialogs, caches, and malformed DTOs cannot widen or restore visibility.
- Action findings: vendor logistics, failure, and receipt require current backend grants, current tenant scope, and locked transition preconditions. Super Admin has no return mutation endpoint. Frontend cannot create, replay, or preserve revoked actions.
- Financial findings: amounts, refund impacts, inventory impacts, settlement impacts, and `financialRefundId` remain backend/refund/ledger evidence. Frontend cannot create, replay, recompute, or restore return money.
- Scope findings: customer, vendor, admin, and tenant scopes remain current-backend-derived. Frontend cannot transfer, merge, widen, leak, or replay scope.
- Cross-domain findings: refund, order, payment, settlement, inventory, execution, and provider evidence cannot become return authority.
- Bootstrap findings: refresh, reconnect, focus, visibility, history, BFCache, React Query, state, dialogs, storage, and replay validate, withhold, refetch, or clear only.
- Cache findings: cache is accepted backend evidence only after DTO/current-lineage validation; it cannot restore authority after backend loss.
- Failure findings: 401, 403, 404, 409, 422, failed refreshes, failed mutations, unsupported statuses, malformed action booleans, and malformed DTOs fail closed.
- Adversarial findings: identity, ownership, lifecycle, visibility, action, financial, scope, actor, tenant, route, history, storage, cache, and mutation replay cannot bypass backend ownership.

## Runtime Hardening Performed

- Removed override fields and Super Admin return mutation controls; added coherent vendor return-grant validation and current-order re-entry.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/PosAdminReturnsController.cs`
- `PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminReturnsController.cs`
- `PointOfSale.Web/src/services/commerceHubVendorOrdersApi.js`
- `PointOfSale.Web/src/pages/CommerceHubOrders.jsx`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-final-authority-certification-findings.md`

## Explicit Certification Answers

- Who owns Return Authority? Backend.
- Can frontend fabricate return authority? No.
- Can identity emerge? No.
- Can ownership emerge? No.
- Can lifecycle emerge? No.
- Can visibility emerge? No.
- Can action authority emerge? No.
- Can financial authority emerge? No.
- Can scope authority emerge? No.
- Can refund authority become return authority? No.
- Can order authority become return authority? No.
- Can payment authority become return authority? No.
- Can stale authority survive refresh? No.
- Can cache restore authority? No.
- Can replay restore authority? No.
- Can tenant leakage occur? No inspected runtime path permits it without backend authorization.
- Were runtime changes required? Yes, final DTO hardening was required.
- Is Return Authority production-certifiable? Yes, for the inspected runtime.
