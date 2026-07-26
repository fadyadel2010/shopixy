# CommerceHub CMS Ownership Separation Certification Findings

Date: 2026-06-15

## Scope

Audited Vendor Storefront CMS and Super Admin CommerceHub Platform CMS runtime ownership separation across routes, capabilities, APIs, DTOs, query keys, caches, navigation, storage/context state, and adversarial replay paths.

Reviewed vendor runtime: `CmsPagesList`, `CmsPageEditor`, Storefront Builder/Themes/Navigation/Footer/Pages/Preview, vendor CMS service/store, POS sidebar, direct routes, and POS API authority-loss handling.

Reviewed Super Admin runtime: marketplace CMS pages/editor/footer, storefront footer defaults, homepage builder/manager/configuration, Super Admin layout/navigation, capability routes, Super Admin API client, and backend Super Admin controllers/services.

## Findings

- Route separation holds: vendor CMS routes are `/storefront/cms/*` under POS protected routes and `CommerceHubVendorRouteGuard` with `commercehub.storefront.cms.manage`; Super Admin CMS routes are `/super-admin/homepage`, `/super-admin/storefront/footer`, `/super-admin/marketplace/pages`, and `/super-admin/marketplace/footer` under Super Admin session and capability guards.
- Capability separation holds: vendor capabilities grant tenant storefront CMS presentation only. Super Admin capabilities grant platform CMS, marketplace footer, homepage, and global storefront-default presentation only.
- API separation holds: vendor CMS uses POS-admin `/api/admin/pages`, `/api/admin/cms/*`, and `/api/admin/footer`. Platform CMS uses `/api/super-admin/marketplace/*`, `/api/super-admin/homepage/*`, and `/api/super-admin/storefront/footer`.
- DTO separation holds: vendor DTOs live under Admin/storefront CMS contracts; platform marketplace CMS DTOs live under Super Admin marketplace CMS contracts; homepage DTOs are explicit Super Admin platform/tenant-scoped homepage contracts.
- Cache/state separation holds: Super Admin homepage query keys are `['super-admin', 'homepage', ...]`; marketplace CMS pages/footer use Super Admin local state/client; vendor CMS uses component/Zustand draft state without persisted storage and no platform query hydration path.
- Navigation separation holds: POS sidebar and direct vendor routes consume backend-issued vendor capabilities; Super Admin layout and direct routes consume backend-issued Super Admin capabilities.
- Storage/context separation holds: no inspected localStorage/sessionStorage key creates CMS ownership authority; preview URLs use current backend vendor tenant scope and backend-issued preview tokens.

## Runtime Hardening

Closed runtime gap: POS vendor protected-endpoint authority-loss detection did not include vendor CMS and tenant footer endpoints. This could preserve stale vendor CMS component/draft presentation after a protected CMS 401/403 until broader route/session convergence.

Runtime change:
- Added `/admin/pages`, `/admin/cms`, and `/admin/footer` to `COMMERCEHUB_VENDOR_AUTHORITY_ENDPOINTS` in `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`.

Regression freeze:
- Updated `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts` to assert vendor CMS endpoints trigger POS vendor authority loss and `/super-admin/marketplace/pages` does not.

## Certification Answer

Vendor Storefront CMS is isolated from CommerceHub Platform CMS on inspected runtime paths after the authority-loss hardening. Vendors cannot mutate platform CMS through inspected routes/APIs. Super Admin runtime cannot accidentally hydrate vendor CMS through inspected client/query/cache paths. No shared route, API, DTO, cache, capability, or authority boundary remains that grants cross-ownership CMS authority.
