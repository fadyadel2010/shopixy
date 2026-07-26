# PASS M1-A CommerceHub Vendor Identity Authority Certification

## Vendor Identity Findings
CommerceHub vendor identity presentation is derivative frontend runtime authority only. Current backend POS session, vendor, profile, identity, tenant, governance, and participation authority own vendor truth. The frontend now derives current vendor scope from the backend-hydrated POS session user and rejects vendor/profile/store-shaped DTOs that carry a foreign tenant or vendor identity.

## Store Identity Findings
Store name, storefront status, preview configuration, and builder store presentation derive from current backend store configuration and current session tenant authority. Store identity fails closed for malformed store name/status, foreign tenant evidence, missing backend session scope, failed store-config reads, and stale preview responses.

## Ownership Findings
Vendor-owned listing, catalog, finance, payments, payout-method, statement, and analytics presentation is accepted only as current backend endpoint output scoped by the current backend session. Mixed ownership arrays fail closed as a unit when any row carries foreign tenant evidence, so foreign vendor data is not silently merged into the current vendor surface.

## Bootstrap Findings
Initial load, refresh, focus, visibility, reconnect, and BFCache behavior continue to re-enter `/api/auth/session` and scoped backend endpoints. Bootstrap state, React state, route state, history, and local storage cannot choose vendor identity. Actor or tenant replacement purges query/runtime authority before replacement presentation.

## Mutation Findings
Listing, store, CMS, payment, payout-method, and storefront mutations remain derivative workflow intent. Mutation responses are not accepted as vendor identity authority; listing/storefront mutation paths converge through current backend reads, and payment/payout-method mutations invalidate/refetch current scoped authority.

## Cache Findings
React Query keys remain current backend tenant/user scoped for inspected vendor finance, earnings, payments, catalog, listing, and storefront surfaces. Cached DTOs that carry foreign tenant evidence are withheld, mixed row sets fail closed, and route-lifecycle revalidation can only refetch or purge.

## Transition Findings
Vendor A to Vendor B, tenant replacement, logout/login, role replacement, governance loss, and participation loss purge prior vendor-scoped caches and runtime cache state. POS storefront preview links now use current backend-hydrated session tenant evidence rather than `localStorage.tenantSlug`.

## Failure Findings
401/403 protected CommerceHub vendor failures dispatch authority-loss revalidation and purge vendor-scoped caches. 404, malformed DTOs, failed store config, failed preview, failed finance/dashboard/statement reads, and missing session scope withhold presentation rather than preserving stale vendor or store identity.

## Adversarial Findings
Identity replay, profile replay, store replay, ownership replay, cache replay, storage replay, history replay, mutation replay, actor replay, and tenant replay cannot create frontend vendor authority on inspected paths. Foreign tenant DTO injection into store config, listings, finance, payment methods, payment history, or statement envelopes fails closed.

## Runtime Hardening Performed
Added a shared frontend vendor identity boundary for current backend session scope, scoped DTOs, scoped arrays, and store identity presentation. Routed storefront status, CommerceHub listings/KPIs/analytics/notifications, storefront builder config/preview, finance dashboards/statements, earnings dashboards/payout methods, and payments dashboards/history/methods through the boundary. Removed POS storefront CMS preview dependence on `localStorage.tenantSlug`.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/components/StorefrontStatusCard.jsx`
- `PointOfSale.Web/src/pages/CommerceHubSettings.jsx`
- `PointOfSale.Web/src/pages/CommerceHubFinance.jsx`
- `PointOfSale.Web/src/pages/CommerceHubEarnings.jsx`
- `PointOfSale.Web/src/pages/CommerceHubPayments.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`

## Regression Freezes Added
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts`
- `CommerceHubFrontendAuthorityCertificationTests.CommerceHubVendorIdentityAuthority_MustDeriveProfileStoreAndOwnershipPresentationFromCurrentBackendScope`

## Architecture Updates
Vendor Identity Authority is recorded as derivative frontend presentation only. Backend vendor, profile, identity, tenant, governance, and participation authorities own truth. Frontend code may validate, suppress, purge, invalidate, refetch, and format current backend DTOs only.

## Explicit Answers
Who owns vendor identity authority? Backend vendor, profile, identity, tenant, governance, and participation authorities.

Can frontend fabricate vendors? No.

Can store identity drift? No on inspected paths; foreign or malformed store identity is withheld.

Can ownership drift? No on inspected paths; foreign or mixed ownership DTOs fail closed.

Can stale vendors survive refresh? No; refresh/revalidation re-enters backend session and scoped endpoint authority.

Can mutation responses become authority? No; mutation paths invalidate/refetch or require current backend reads.

Can cache restore vendor identity? No; caches are scoped and DTOs are boundary-checked.

Can tenant leakage occur? Not on inspected paths after current-scope validation and storage-scope removal.

Were runtime changes required? Yes.

Is Vendor Identity Authority production-certifiable? Yes, for inspected CommerceHub vendor identity runtime paths, with residual deployed-browser chaos evidence retained for multi-tab, offline/reconnect, BFCache, delayed storage events, governance revocation, and malformed gateway DTOs.
