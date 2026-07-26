# PASS M5-H Return Final Authority Certification Findings

Date: 2026-06-22

Status: Return Authority is production-certifiable for the inspected runtime after final DTO hardening.

## Final Findings

- Backend owns Return Identity, Ownership, Lifecycle, Visibility, Action, Financial, Scope Isolation, Transition, Tenant, and Governance Authority.
- Frontend owns display, formatting, selection, dialogs, navigation, filtering, sorting, presentation, validation, withholding, refetching, and backend-bound intent only.
- Final runtime reinspection covered CommerceHub customer refund/order paths, POS vendor refund-impact presentation, Super Admin refund/return presentation and actions, POS Admin backend return endpoints, POS sales/purchase returns, cross-domain refund/order/payment/settlement/inventory evidence, bootstrap/cache/replay, tenant isolation, and failure/adversarial paths.

## Findings By Domain

- Identity findings: `returnId`, refund linkage, payment linkage, execution linkage, tracking evidence, and POS `returnNumber` remain backend-issued evidence. Frontend cannot create, replay, restore, substitute, or infer return identity.
- Ownership findings: customer ownership, vendor ownership, and admin visibility are endpoint/current-detail derived. Frontend cannot create, replay, transfer, or leak ownership.
- Lifecycle findings: implemented lifecycle remains exactly `Pending`, `PickupScheduled`, `PickedUp`, `InTransit`, `DeliveredToVendor`, `Confirmed`, and `Failed`. Frontend cannot advance, regress, repair, replay, or restore lifecycle.
- Visibility findings: customer, vendor, and admin visibility remain backend scoped. Frontend selection, filters, dialogs, caches, and malformed DTOs cannot widen or restore visibility.
- Action findings: override and mark-failed require current backend grants; vendor receipt confirmation is backend tenant-scoped; platform logistics transitions are backend policy-gated. Frontend cannot create, replay, or preserve revoked actions.
- Financial findings: amounts, refund impacts, inventory impacts, settlement impacts, and `financialRefundId` remain backend/refund/ledger evidence. Frontend cannot create, replay, recompute, or restore return money.
- Scope findings: customer, vendor, admin, and tenant scopes remain current-backend-derived. Frontend cannot transfer, merge, widen, leak, or replay scope.
- Cross-domain findings: refund, order, payment, settlement, inventory, execution, and provider evidence cannot become return authority.
- Bootstrap findings: refresh, reconnect, focus, visibility, history, BFCache, React Query, state, dialogs, storage, and replay validate, withhold, refetch, or clear only.
- Cache findings: cache is accepted backend evidence only after DTO/current-lineage validation; it cannot restore authority after backend loss.
- Failure findings: 401, 403, 404, 409, 422, failed refreshes, failed mutations, unsupported statuses, malformed action booleans, and malformed DTOs fail closed.
- Adversarial findings: identity, ownership, lifecycle, visibility, action, financial, scope, actor, tenant, route, history, storage, cache, and mutation replay cannot bypass backend ownership.

## Runtime Hardening Performed

- Super Admin return DTO validation now requires `canOverrideStatus` to be an explicit backend boolean for both standalone and embedded return evidence before `allowedOverrideStatuses` can be accepted.

## Exact Runtime Files Modified

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
