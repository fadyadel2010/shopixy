# PASS AF-PA2.1 Affiliate Product Eligibility Entry & Navigation Authority Certification Findings

Status: Production-certifiable after product workspace entry-presentation hardening.

Date: 2026-06-25

Scope: POS Admin runtime entry and navigation into product-level Affiliate Product Eligibility configuration through Products, product edit workspace, product sidebar panels, product actions, direct URLs, bookmarks, browser history, protected routes, role presentation, tenant boundaries, failure states, and adversarial client presentation manipulation. Product eligibility business rules, commission logic, save logic, DTO mutation internals, and backend persistence were intentionally out of scope.

## Authority Conclusion

Affiliate Product Eligibility Entry Authority is backend-owned. Backend authentication/session, backend role authorization, trusted tenant context, product ownership checks, store/tenant resolution, and `AdminProductAffiliateController` endpoint authorization decide whether a user may enter product-level Affiliate management.

Frontend navigation is presentation only. Products list rows, edit buttons, workspace sidebar panels, hidden buttons, route state, browser history, bookmarks, deep links, CSS, DOM, and React state can only render or suppress candidate navigation. They cannot authorize entry or select product, tenant, store, permission, or eligibility authority.

## Runtime Hardening Performed

The backend product affiliate endpoints were already protected by `[Authorize(Roles = "Owner,Admin")]`, and `ProductAffiliateSettingsCard` already self-withheld and avoided product-affiliate reads for non-Owner/Admin roles. The product workspace sidebar still rendered the surrounding `Affiliate Marketing` panel for any authenticated POS role on `/products/:productId/edit`, creating a presentation discovery gap for lower-role employees through product-list row selection, manual URLs, bookmarks, or browser history.

Hardening:

- `ProductForm` now reads the current backend-hydrated POS role from `useAuthStore` and renders the product Affiliate Marketing sidebar panel only for `Owner` or `Admin` presentation roles.
- Lower roles no longer see the product-level Affiliate Marketing panel or mount `ProductAffiliateSettingsCard` from product edit workspace entry.
- The backend remains the security authority; this frontend change only aligns product workspace presentation with the existing backend and card-level role boundary.

## Navigation Findings

- Products are reached through the POS sidebar `/products` item under the authenticated POS layout.
- Product rows and Edit actions navigate to `/products/:productId/edit`; there is no separate frontend product-affiliate route.
- Product affiliate configuration is discoverable to Owner/Admin users through the product workspace right-side `Affiliate Marketing` sidebar panel.
- Browser back/forward, bookmarks, and manual `/products/:productId/edit` entry can reopen the product workspace, but product affiliate entry presentation is now withheld for lower roles and backend affiliate reads remain denied.

## Entry Findings

- Intended entry is: Products list -> product row/Edit -> product workspace -> Affiliate Marketing sidebar panel -> backend-authorized product affiliate settings read.
- Product-level Affiliate entry is not granted by the product list, row click, Edit button, sidebar panel, or card mount.
- `ProductAffiliateSettingsCard` still self-withholds for non-Owner/Admin roles and performs product affiliate reads only when the role presentation is accepted.
- Backend endpoint authorization remains decisive after frontend presentation.

## Product Discovery Findings

- Administrators can discover product affiliate configuration from the product detail workspace sidebar without a hidden URL or separate route.
- Search, filters, row clicks, and Edit actions remain product-discovery/navigation tools only.
- Lower-role employees can browse/edit products according to the broader product runtime, but they no longer discover product affiliate configuration through the workspace panel.
- Users are not routed to a dead-end product affiliate page because no standalone frontend route exists.

## Route Findings

- POS product routes are under `ProtectedRoute`, which withholds routes until backend session hydration completes.
- `/products/:productId/edit` is a product workspace route, not a product affiliate authority route.
- No implemented frontend route such as `/products/:id/affiliate` or `/admin/products/:id/affiliate` was found.
- Manual URLs, bookmarks, and history restore product workspace presentation only; backend product affiliate API calls still require `Owner,Admin` and trusted tenant context.

## Permission Rendering Findings

- Product Affiliate buttons, panel, and card rendering are presentation only.
- Hiding the Affiliate Marketing panel is not security and cannot replace backend authorization.
- Exposing the component through DevTools or DOM manipulation cannot create backend role, product ownership, tenant, store, or eligibility authority.

## Tenant Findings

- Product affiliate endpoints derive tenant from trusted backend context and route product identity from the endpoint path.
- URL product-id manipulation can only submit a backend-bound candidate product id; backend tenant/product predicates and authorization remain decisive.
- No inspected frontend route or query parameter can select tenant/store authority.
- Tenant leakage through product affiliate entry was not found after the presentation hardening.

## Failure Findings

- Missing authentication redirects through POS `ProtectedRoute` to login.
- Session hydration withholds protected presentation until backend session evidence is known.
- Backend 401/403 from product affiliate endpoints triggers the previously certified POS authority-loss revalidation path.
- Backend 404/403 for foreign or unauthorized product affiliate targets cannot be repaired by frontend navigation and does not preserve product affiliate entry authority.
- Expired sessions, role revocation, and tenant/actor replacement fail closed through backend session/API re-entry and presentation withholding.

## Adversarial Findings

- Manual URL entry: cannot bypass backend authorization; lower-role product workspace entry no longer displays the product affiliate panel.
- Browser history/bookmarks: can restore product workspace navigation only, not product affiliate authority.
- DOM/CSS manipulation: can expose local presentation only; backend endpoints remain `[Authorize(Roles = "Owner,Admin")]`.
- DevTools/React state edits: can alter client rendering only and cannot create trusted backend role or tenant context.
- Hidden buttons and mounted components cannot become security or authority.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/components/ProductForm.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/components/ProductForm.test.tsx`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Verification

- `npm test -- --run src/components/ProductForm.test.tsx src/components/ProductAffiliateSettingsCard.test.tsx src/components/PosRoleRouteGuard.test.tsx src/auth/posVendorSessionAuthority.test.ts`

Result: 4 test files passed, 19 tests passed. Console output included existing React Router future-flag/act warnings and the existing intentional affiliate-settings read failure log in the fail-closed card test.

## Explicit Certification Answers

- Who owns Affiliate Product Eligibility Entry Authority? Backend authentication/session, backend role authorization, trusted tenant/store context, product ownership checks, and backend product affiliate endpoint authorization.
- Can frontend navigation become authority? No.
- Can Product Affiliate routes be opened directly? No standalone frontend Product Affiliate route was found; direct product workspace URLs can be opened only as product navigation, and backend affiliate APIs still authorize entry.
- Can hidden Affiliate buttons become security? No.
- Can manual URL entry bypass authorization? No.
- Can bookmarks bypass intended navigation? No; bookmarks can restore product workspace presentation only and cannot bypass backend authorization.
- Can tenant leakage occur? No inspected path; tenant/product scope is backend-derived and URL manipulation remains a backend-bound candidate.
- Were runtime changes required? Yes, product workspace Affiliate Marketing panel presentation was restricted to Owner/Admin roles.
- Is Affiliate Product Eligibility Entry Authority production-certifiable? Yes.
