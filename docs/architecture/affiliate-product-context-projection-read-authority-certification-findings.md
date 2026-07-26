# PASS AF-PA2.2 Affiliate Product Context, Projection & Read Authority Certification Findings

Status: Production-certifiable after product-bound read admission hardening.

Date: 2026-06-25

Scope: POS Admin product workspace, route product identity, product affiliate settings reads, component state, read DTOs, frontend projection, cache/browser state, tenant/store isolation, failures, rapid navigation, browser back/forward, refresh, multiple tabs, and replayed read responses. Eligibility business rules, save logic, and mutation logic were intentionally out of scope.

## Authority Conclusion

Product Context Authority is backend-owned. Backend product identity, trusted tenant/store context, product ownership/scope predicates, role authorization, and `AdminProductAffiliateController` read authorization decide which product affiliate configuration can be read.

Frontend route params, `ProductWorkspacePage`, `ProductForm`, `ProductAffiliateSettingsCard`, React state, loading state, browser history, cache, and frontend projection are presentation or backend-bound read candidates only. They cannot fabricate product identity, tenant/store scope, eligibility, projection authority, or configuration truth.

## Runtime Hardening Performed

- `ProductAffiliateSettingsResponse` now carries backend-issued `ProductId` evidence on all product affiliate settings responses.
- `ProductAffiliateSettingsCard` now rejects missing or mismatched product identity before accepting backend settings into its authoritative baseline or visible form state.
- Replayed Product A settings while rendering Product B now fail closed with unavailable-current-settings presentation instead of displaying Product A affiliate configuration.

## Product Context Findings

- Product workspace identity flows from React Router `/products/:productId/edit` into `ProductWorkspacePage`, then into `ProductForm`, then into `ProductAffiliateSettingsCard`.
- Product affiliate reads use only the current `productId` prop as a backend-bound route candidate; the backend verifies product and tenant ownership before returning settings.
- On product changes, `ProductAffiliateSettingsCard` clears accepted settings, dirty state, success/error state, and mutation in-flight state before refetching.
- The affiliate card renders a loading or fail-closed unavailable state while current product settings authority is absent.
- Normal route-change stale completions are fenced by request sequence invalidation; replayed or substituted responses are now additionally rejected by backend `ProductId` evidence.

## Read Findings

- Every inspected product affiliate settings read enters `GET /admin/products/{productId}/affiliate` with no-store/no-cache request headers.
- Backend reads require `[Authorize(Roles = "Owner,Admin")]`, trusted tenant context, current tenant product ownership, and tenant-scoped affiliate policy lookup.
- Frontend does not store product affiliate reads in React Query, localStorage, sessionStorage, or POS runtime cache.
- Accepted read data becomes a derivative display baseline only after matching the current product identity.
- Failed or mismatched reads do not preserve previous product settings as current authority.

## Projection Findings

- Backend projection/read model for product affiliate settings is derived from SQL product ownership and `AffiliateProductPolicies` by tenant and product.
- Frontend projection normalizes commission values, category selection, template display, and enabled state for rendering only.
- Projection cannot become authority because current backend `ProductId` evidence and tenant/product read authorization are required before presentation.

## Cross-Product Findings

- Product A -> B -> C navigation remounts/refetches through the current route product candidate and clears previous accepted affiliate baseline.
- Rapid navigation is fenced by request sequence counters and cleanup invalidation.
- Browser back/forward re-enters the workspace route and backend product affiliate GET for the restored product id.
- Refresh reloads the app and re-enters backend session plus current product affiliate read.
- Multiple tabs keep independent derivative state and converge through focus/visibility/online refetch; one tab cannot authorize or inject another tab's product settings.

## Cache Findings

- Product affiliate settings are stored only in component memory.
- Admin product affiliate GETs carry `Cache-Control: no-store` and `Pragma: no-cache` hints.
- No React Query persistence, localStorage persistence, sessionStorage persistence, optimistic cache patching, or previous-data fallback was found for product affiliate settings.
- Cached or replayed settings for a different product now fail closed by `ProductId` mismatch.

## Tenant Findings

- Backend tenant scope comes from trusted `ITenantContext.RequiredTenantId`, not route, DTO, storage, or frontend state.
- Product ownership is proven by `Products.Where(p => p.Id == productId && p.TenantId == tenantId)` before policy read.
- Affiliate policy read is tenant and product scoped.
- Product-id route manipulation can only submit a backend-bound candidate; foreign tenant/product reads return not found/denied and cannot display foreign affiliate configuration.

## Failure Findings

- 401/403 denials trigger the previously certified POS authority-loss revalidation path.
- 404/foreign product responses fail closed and do not repair from old product state.
- Timeout/offline/network failures render unavailable-current-settings presentation when no current accepted backend settings exist.
- Mismatched backend `ProductId` evidence is treated as a failed read and withholds editable/product configuration controls.

## Adversarial Findings

- Modified route/product id: backend tenant/product predicates decide; frontend route is a candidate only.
- Modified component state/DOM: can alter local presentation only until backend re-entry; it cannot create backend product identity.
- Modified cache/storage: no product affiliate settings storage authority exists.
- Replayed previous Product A read while viewing Product B: rejected by frontend product evidence binding.
- Substituted/malformed response without current `productId`: rejected before presentation.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminProductAffiliateController.cs`
- `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.jsx`
- `PointOfSale.Web/src/services/adminProductAffiliateApi.js`

## Regression Freezes Added

- `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.test.tsx`
- `PointOfSale.Tests/Affiliate/AdminProductAffiliateTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Verification

- `npm test -- --run src/components/ProductAffiliateSettingsCard.test.tsx src/services/adminAffiliateApi.test.ts src/components/ProductForm.test.tsx src/auth/posVendorSessionAuthority.test.ts`
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~AdminProductAffiliateTests"`

Result: frontend 4 files passed, 21 tests passed. Backend filtered suite passed, 48 tests passed. Existing warnings were unrelated repository warnings and known React Router/act/list-key warnings.

## Explicit Certification Answers

- Who owns Product Context Authority? Backend product identity, tenant/store context, product ownership/scope predicates, and product affiliate endpoint authorization.
- Can frontend fabricate Product Identity? No. Route params and component props are backend-bound candidates only.
- Can Product A display Product B Affiliate configuration? No after product-bound response admission; mismatched product settings fail closed.
- Can cache survive product changes? Not as authority. Component memory is cleared/refetched, browser/HTTP cache is discouraged with no-store/no-cache, and mismatched replay is rejected.
- Can projection become authority? No. Projection is derivative and admitted only with current backend product evidence.
- Can route manipulation display another product? No. Route manipulation can request a backend candidate only; backend tenant/product checks and frontend response binding prevent wrong-product display.
- Can tenant leakage occur? No inspected path; tenant is backend-derived and product/policy reads are tenant scoped.
- Were runtime changes required? Yes. Backend product identity evidence and frontend product-bound admission were added.
- Is Affiliate Product Context & Read Authority production-certifiable? Yes.
