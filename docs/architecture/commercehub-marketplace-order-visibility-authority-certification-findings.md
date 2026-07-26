# PASS M2-F Marketplace Order Visibility Authority Certification Findings

## Runtime Findings

- Visibility findings: backend order, customer, vendor, tenant, split-order, visibility, governance, lifecycle, payment, settlement, and recovery authorities own visibility truth and grants. Frontend order visibility remains derivative after runtime hardening.
- Customer findings: customer order list, dashboard, detail, tracking, and recovery paths route through customer order identity and lifecycle boundaries; route-selected detail responses must match the backend order ID and fail closed on malformed split/lifecycle evidence.
- Vendor findings: POS/Admin vendor order list/detail paths use current backend vendor/session/tenant scope. Runtime hardening now requires POS vendor detail DTOs to match the route-selected order ID exactly before drawer presentation.
- Admin findings: Super Admin order search/list/detail paths route through Super Admin order identity/lifecycle/split boundaries, with selected detail withheld during current refresh/failure and cleared when the accepted current list no longer contains it.
- Recovery findings: payment recovery links submit backend read intent only; token/search parameters are removed after capture and recovery DTOs must match the backend order target before presentation.
- Split findings: split-order visibility requires backend-issued sub-order IDs, vendor/store evidence, item evidence, lifecycle/payment evidence, and item-to-sub-order binding. Frontend grouping cannot fabricate split visibility.
- Bootstrap findings: customer, vendor, and Super Admin session bootstraps re-enter current backend session authority; order visibility caches are scoped and purged/revalidated on protected authority loss.
- Cache findings: customer and Super Admin order rows are withheld during current refresh/failure where stale cache could otherwise preserve visibility. POS vendor details now fail closed on route-target mismatch even when the returned DTO is current-tenant scoped.
- Failure findings: 401/403 protected order endpoints purge or revalidate current actor authority. Malformed DTOs, missing tenant evidence, lifecycle contradictions, split contradictions, duplicate identities, and route-target mismatches fail closed.
- Adversarial findings: local storage, session storage, React state, contexts, query caches, mutation responses, route state, history, selected rows, cross-tab events, URL/search parameters, recovery links, tracking links, malformed DTOs, and stale rows cannot fabricate, widen, infer, repair, preserve, replay, restore, escalate, or transfer order visibility on inspected paths.

## Runtime Hardening

- Added POS/Admin vendor `requireCurrentVendorOrderVisibilityAuthority` to validate current backend vendor/tenant evidence and exact selected `orderId` equality.
- Routed POS/Admin `/admin/orders/{orderId}` detail responses through lifecycle validation and vendor visibility target validation before rendering.
- Added regression coverage for route-target mismatch, foreign tenant evidence, and missing current vendor session evidence.

## Modified Files

- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/services/adminOrdersApi.js`
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/commercehub-marketplace-order-visibility-authority-certification-findings.md`

## Certification Answers

- Who owns order visibility authority? Backend order, customer, vendor, tenant, split-order, visibility, governance, lifecycle, payment, settlement, and recovery authorities.
- Can frontend fabricate visibility? No.
- Can Customer A see Customer B orders? No on inspected paths; customer order visibility derives from backend customer/session/order authority.
- Can Vendor A see Vendor B orders? No on inspected paths; POS vendor visibility derives from current backend vendor/tenant authority and now exact selected order target validation.
- Can Super Admin visibility become ownership? No; Super Admin visibility remains platform visibility, not ownership.
- Can recovery links widen visibility? No; they can only request backend recovery authority.
- Can tracking links bypass visibility? No; tracking presentation remains bounded by backend order/tracking visibility authority.
- Can split visibility be fabricated? No; split presentation requires backend-issued split and item binding evidence.
- Can stale visibility survive refresh? No on inspected paths; stale rows are withheld or purged during refresh/failure/authority loss.
- Can cache restore visibility? No; caches remain derivative and cannot replace current backend authority.
- Can tenant leakage occur? No on inspected paths after current vendor tenant validation and route-target hardening.
- Were runtime changes required? Yes, POS/Admin vendor order detail route-target visibility validation was required.
- Is Marketplace Order Visibility Authority production-certifiable? Yes, with residual deployed-browser chaos evidence retained as operational proof.
