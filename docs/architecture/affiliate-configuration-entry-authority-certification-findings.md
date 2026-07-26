# PASS AF-PA1.1 Affiliate Configuration Entry Authority Certification Findings

Status: Production-certifiable after frontend route-entry hardening.

Date: 2026-06-25

Scope: POS Admin runtime entry into Affiliate Management / Affiliate Configuration through sidebar, direct routes, browser history, bookmarks, deep links, protected routes, and role-based presentation guards. Save logic, API validation, DTO shape, React Query behavior, and affiliate business rules were intentionally out of scope except where they participate in entry authority.

## Authority Conclusion

Affiliate Configuration Entry Authority is backend-owned. The decisive runtime authority is the POS backend session, role authorization, trusted tenant context, and the affiliate admin endpoints protected by `[Authorize(Roles = "Owner,Admin")]`.

Frontend navigation owns presentation only. The sidebar can hide the affiliate management link and the route wrapper can suppress the page shell for non-Owner/Admin users, but neither creates security authority. Direct URL entry, bookmarks, browser history, and deep links still re-enter the same backend-owned session and endpoint authorization boundaries.

## Runtime Hardening Performed

The sidebar already hid Affiliate Management from non-Owner/Admin users, and `AdminAffiliateController` already enforced backend `Owner,Admin` role authorization plus trusted tenant context. The route table, however, mounted `/admin/affiliates` and `/admin/affiliates/:id` directly under the generic POS authenticated layout. A non-Owner/Admin user could deep-link to the affiliate page shell and then be rejected by backend API calls.

Hardening:

- Added `PosRoleRouteGuard`, a presentation-only POS route wrapper driven by the current backend-hydrated session role.
- Wrapped `/admin/affiliates` and `/admin/affiliates/:id` with `roles={['Owner', 'Admin']}`.
- Added a focused regression proving bookmarked/direct route entry does not render Affiliate Configuration for a `Cashier`, while `Admin` presentation still renders.
- Updated the existing POS auth bootstrap fixture so the existing session lifecycle regression suite continues using the current backend session authority DTO shape.

The backend remains the enforcement authority. The new frontend route guard is not an authorization substitute; it prevents route presentation from diverging from sidebar presentation.

## Navigation Findings

- Main sidebar includes a single Affiliate Management entry at `/admin/affiliates`.
- The sidebar filters that entry to `Owner` and `Admin` roles only.
- No quick link, search navigation, or breadcrumb builder was found that independently exposes Affiliate Management.
- Public `/affiliate/*` routes are the external affiliate portal and are separate from POS Admin Affiliate Configuration.
- Unknown POS authenticated routes are redirected to `/`; unknown public `/affiliate/*` routes are redirected to `/affiliate`.

## Sidebar Findings

- Sidebar visibility is presentation only and uses the current hydrated POS user role.
- Hidden sidebar items are not treated as security enforcement.
- Removing CSS, showing hidden DOM, or editing menu state cannot create backend access because affiliate admin APIs are backend role-gated.

## Route Findings

- POS authenticated routes are withheld by `ProtectedRoute` until backend session hydration completes.
- `/admin/affiliates` and `/admin/affiliates/:id` now require `PosRoleRouteGuard` presentation role evidence before rendering.
- Direct route entry, browser back/forward, bookmarks, and deep links cannot render the affiliate configuration shell for users without the accepted presentation role.
- Backend affiliate admin endpoints remain protected by `[Authorize(Roles = "Owner,Admin")]`.

## Permission Rendering Findings

- Frontend role checks hide menus and suppress route presentation only.
- Frontend does not decide security permission or tenant authority.
- Hidden buttons and hidden pages cannot become backend authority; endpoint authorization still controls reads and mutations.

## Entry Authority Findings

- Entry authority composes as: backend POS session -> backend-issued user role in frontend session presentation -> sidebar/route rendering -> backend affiliate endpoint authorization.
- The backend remains decisive at every data boundary.
- Frontend route presentation now matches sidebar visibility for direct-entry attempts.

## Tenant Isolation Findings

- `AdminAffiliateController` derives tenant from trusted `ITenantContext.RequiredTenantId`.
- Affiliate list/detail/order/earnings reads filter current store evidence by `StoreTenantId` or current tenant projection predicates.
- Cross-tenant affiliate IDs return not found rather than exposing foreign tenant data.
- URL manipulation cannot select another tenant for Affiliate Configuration.

## Failure Handling Findings

- Missing POS authentication redirects to `/login`.
- Hydration and route lifecycle revalidation withhold protected presentation while backend session authority is unresolved.
- Backend 401/403 remains terminal for affiliate admin API access.
- Logout, actor replacement, tenant replacement, and BFCache/history revalidation converge through the existing POS auth bootstrap route authority controls.

## Adversarial Findings

- Manual URL entry: non-Owner/Admin route shell is suppressed; backend APIs remain role-gated.
- Deep links/bookmarks/browser history: cannot bypass frontend presentation guard or backend authorization.
- Multiple tabs/BFCache: existing POS route lifecycle revalidation withholds protected routes during backend revalidation.
- Developer tools/CSS/DOM manipulation: can only affect client presentation, not backend endpoint authorization.
- Client-side variable edits: cannot create trusted backend role, tenant, or affiliate access; backend APIs re-authorize.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/components/PosRoleRouteGuard.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/components/PosRoleRouteGuard.test.tsx`
- Revalidated existing route/session freeze: `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Verification

- `npm test -- --run src/components/PosRoleRouteGuard.test.tsx`
- `npm test -- --run src/components/PosRoleRouteGuard.test.tsx src/components/AuthBootstrap.test.tsx`
- `npm run build`

The first build attempt in the restricted sandbox failed because Vite/esbuild could not read the config path under sandbox restrictions. The approved unsandboxed `npm run build` passed.

## Explicit Certification Answers

- Who owns Affiliate Configuration Entry Authority? Backend POS authentication/session, backend role authorization, trusted tenant context, and backend affiliate admin endpoint authorization.
- Can frontend navigation become authority? No.
- Can menus become security? No.
- Can hidden routes be accessed? The page shell is now suppressed for non-Owner/Admin presentation roles; backend APIs remain inaccessible without backend authorization.
- Can deep links bypass authorization? No.
- Can bookmarks bypass authorization? No.
- Can browser history expose protected pages? No inspected path; route lifecycle revalidation and role presentation guard withhold unauthorized presentation.
- Can manual URL entry bypass security? No.
- Can frontend determine permissions? No. It can only render or suppress presentation from current backend-hydrated session evidence.
- Can tenant leakage occur through navigation? No inspected path; tenant scope is backend-derived and URL manipulation cannot select it.
- Were runtime changes required? Yes, frontend route-entry presentation hardening was required.
- Is Affiliate Configuration Entry Authority production-certifiable? Yes.
