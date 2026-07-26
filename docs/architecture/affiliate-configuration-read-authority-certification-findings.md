# PASS AF-PA1.2 Affiliate Configuration Read Authority Certification Findings

Status: Production-certifiable after read-runtime hardening.

Date: 2026-06-25

Scope: POS Admin Affiliate Configuration read paths only: affiliate management list/detail, readiness reads, product affiliate settings reads, category-tree reads, component state, React Query, memory caches, browser refresh, lifecycle refetch, multi-tab convergence, tenant switching, loading/error states, and browser/cache adversarial paths. Save/edit/mutation/DTO submission/business-rule authority was intentionally out of scope.

## Authority Conclusion

Affiliate Configuration Read Authority is backend-owned. Current backend runtime reads from trusted tenant context, backend role authorization, PostgreSQL configuration/ledger/state, and backend projection/read services own configuration truth, version, tenant configuration, feature state, commission defaults, approval policy, and authoritative read models.

Frontend state is derivative presentation only. React component state, loading state, route state, React Query, POS runtime cache, browser storage, and browser HTTP cache cannot create, preserve, restore, or replace Affiliate Configuration truth.

## Runtime Hardening Performed

- Added explicit `Cache-Control: no-store` and `Pragma: no-cache` request headers to admin affiliate configuration GETs.
- Hardened `useAffiliateReadiness` with request-sequence fencing and focus/visibility/online revalidation.
- Hardened `AdminAffiliatesList` read state with request-sequence fencing, readiness failure clearing, and focus/visibility/online refetch.
- Hardened `AdminAffiliateDetail` header/orders/earnings/settlement/withdrawal read surfaces with request-sequence fencing and lifecycle refetch.
- Hardened `ProductAffiliateSettingsCard` product settings/category-tree reads with request-sequence fencing and lifecycle refetch.

These changes do not make frontend state authoritative; they reduce stale presentation windows and force convergence through backend GETs.

## Findings

- Read authority findings: every inspected Affiliate Configuration read enters backend GET endpoints protected by current POS authentication, `[Authorize(Roles = "Owner,Admin")]`, and trusted `ITenantContext.RequiredTenantId`.
- API read findings: admin affiliate list/detail/readiness/product-settings/category-tree reads trust only backend responses; frontend code does not fabricate configuration values when backend data is absent.
- State findings: React state only stores last accepted backend payload for rendering. Request-sequence fencing prevents older completions from repainting over newer reads.
- Cache findings: affiliate admin reads are not stored in localStorage/sessionStorage, not routed through POS runtime cache, and not written to React Query. GETs now carry no-store/no-cache request headers.
- Refresh findings: hard refresh reloads the app and re-enters backend session plus backend GETs. Soft navigation/remount re-runs GETs; focus/online/visible lifecycle now refetches current backend evidence.
- React Query findings: the inspected affiliate configuration surfaces do not use React Query for configuration reads. Shared query-client defaults therefore cannot become affiliate configuration authority.
- Loading findings: loading and skeleton states render no configuration truth. Readiness failures render a setup warning and clear affiliate list presentation.
- Multi-tab findings: tabs have independent derivative state and converge on focus/visibility/online through backend refetch. Cross-tab storage carries terminal session invalidation only, not configuration truth.
- Tenant findings: backend reads derive tenant from trusted server context; frontend tenant storage is cleared/de-authoritized by existing POS auth runtime. Actor/tenant replacement clears query/runtime caches.
- Failure findings: 401 triggers refresh or terminal logout, 403 remains backend denial, 404 clears detail presentation, 500/timeout/offline fail to error/empty presentation and retry only through backend GETs. Failed reads cannot promote stale data to authority.
- Adversarial findings: modifying React state, React Query, context, Redux-like stores, browser storage, browser cache, or intercepted/replayed responses can only alter local presentation until the next backend read. It cannot mutate backend configuration or become trusted authority.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/services/adminAffiliateApi.js`
- `PointOfSale.Web/src/services/adminProductAffiliateApi.js`
- `PointOfSale.Web/src/hooks/useAffiliateReadiness.js`
- `PointOfSale.Web/src/modules/affiliate/pages/AdminAffiliatesList.jsx`
- `PointOfSale.Web/src/modules/affiliate/pages/AdminAffiliateDetail.jsx`
- `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/services/adminAffiliateApi.test.ts`
- `PointOfSale.Web/src/hooks/useAffiliateReadiness.test.tsx`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Verification

- `npm test -- --run src/services/adminAffiliateApi.test.ts src/hooks/useAffiliateReadiness.test.tsx`

## Explicit Certification Answers

- Who owns Affiliate Configuration Read Authority? Backend runtime: trusted tenant context, backend role authorization, PostgreSQL configuration/ledger/state, and backend read services.
- Can frontend become configuration authority? No.
- Can React Query become authority? No; inspected read paths do not store affiliate configuration in React Query, and query caches are derivative only.
- Can React state become authority? No; it is fenced derivative presentation.
- Can Context become authority? No.
- Can Redux become authority? No Redux authority path was found; frontend stores remain derivative.
- Can cache become authority? No.
- Can Local Storage become authority? No.
- Can Session Storage become authority? No.
- Can browser refresh restore stale configuration? No; hard refresh re-enters backend session and backend GETs.
- Can multiple tabs diverge from backend truth? They can temporarily display older derivative state while idle, but lifecycle refetch converges them through backend truth and stale tabs cannot become authority.
- Can tenant switching leak configuration? No inspected path; tenant scope is backend-derived and actor/tenant transitions purge derivative caches.
- Were runtime changes required? Yes, read-lifecycle fencing, lifecycle refetch, and no-store request hardening were added.
- Is Affiliate Configuration Read Authority production-certifiable? Yes.
