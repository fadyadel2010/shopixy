# Main Storefront Navigation Final Certification

Date: 2026-09-25

Status: **PASS**

## Scope

This certification covers the complete Storefront Navigation path:

- Admin Header and Footer Navigation authoring
- canonical target admission
- draft persistence and publication
- StoreConfig optimistic concurrency
- public Header and Footer Navigation projection
- CMS, category, product, collection, custom, and external link identity
- English and Arabic rendering
- desktop and mobile behavior
- RTL mobile-drawer placement
- cache visibility and cross-tab convergence
- failure handling and tenant isolation
- independence from theme presentation and the dedicated Footer surface
- exact restoration of the original AhramStore Navigation payload

## Authority

The certified ownership model is unchanged:

- PostgreSQL `StoreConfig.CustomizationJson.navigation.draft` owns the editable draft.
- PostgreSQL `StoreConfig.CustomizationJson.navigation.live` owns the public snapshot.
- Header and Footer Navigation publish together as one Navigation scope.
- `StoreConfig.RowVersion` is the optimistic-concurrency token.
- The public runtime reads only the live Navigation snapshot.
- The dedicated Footer authoring surface remains independently owned and immediate-live.
- Themes consume Navigation presentation models and do not own Navigation data.
- Public entity links are recomposed from current tenant-scoped SQL authority.

## Defects Repaired

### Public Entity Identity

The public projection previously collapsed authoring entity types into public link types and discarded canonical target identity. Runtime validation could therefore depend on stale slugs or paths after a target rename and could not reliably distinguish canonical links from legacy links.

The repair retains internal, non-serialized `AuthoringType`, `TargetId`, and `CollectionKey` values while reading the live snapshot. Public routes are then recomposed from current SQL authority:

- CMS links resolve the current published slug by `PageGroupId`.
- Category links resolve the current live tenant category by GUID.
- Duplicate, empty, or ambiguous category slugs fall back to a GUID route.
- Product links resolve the current published, active, online-visible tenant product by GUID.
- Hidden, deleted, unpublished, stale, or cross-tenant targets fail closed.
- Legacy identity-free links are accepted only through the existing exact backend warning/legacy validation path.

The internal identity fields are decorated with `JsonIgnore`; they never leak into the public API contract.

### Admin Editor

The Navigation editor now supports the complete admitted contract:

- changing backend-issued CMS, category, and product targets
- changing collection targets
- editing English and explicit Arabic labels
- enabling and disabling a link
- editing custom paths
- editing external URLs and new-tab behavior
- accessible edit, reorder, save, and cancel controls
- responsive wrapping and long-destination handling
- correct RTL spacing and direction indicators

Save operations are serialized. A response from an older in-flight save adopts the rotated RowVersion without overwriting newer local edits. Publication is disabled when draft and live are identical.

### Concurrency And Publication

- Navigation writes explicitly compare the current StoreConfig RowVersion before persistence.
- Stale mutations return `409` with the latest authoritative snapshot.
- Missing mutation preconditions return `428`.
- Publishing an already-identical draft/live snapshot is idempotent and does not create duplicate publication work.

### RTL Mobile Drawer

The shared default mobile drawer now uses logical direction placement:

- LTR: right edge
- RTL: left edge

## Browser Certification

The final Microsoft Playwright run completed with `CERTIFICATION PASS` against the running local Admin, Storefront, and API services.

### Authoring Lifecycle

The browser run performed the following disposable workflow through the Admin UI:

1. Loaded Navigation and backend-issued targets.
2. Added a CMS Header link targeting About Us.
3. Edited the target to Contact Us.
4. Added an explicit Arabic label.
5. Disabled the link.
6. Reordered it between Home and Shop.
7. Saved the draft and received `200`.
8. Reloaded the editor and proved target, order, Arabic label, and disabled state persisted.
9. Re-enabled and saved the link.
10. Published Header and Footer Navigation and received `200`.
11. Opened a second authenticated tab and proved immediate convergence.
12. Deleted the disposable link through the UI.
13. Saved and published restoration.

### Public Runtime

The published disposable link was verified in a fresh browser context:

- public Navigation API returned the link immediately
- desktop Header rendered the link
- the CMS identity recomposed to `/contact`
- clicking the link loaded the published Contact page with `200`
- Arabic mode selected the explicit `labelAr` value
- document direction changed to `rtl`
- the 390x844 mobile drawer anchored to the left edge
- the Arabic link was visible in the mobile drawer
- no unexpected page or console errors occurred

### Isolation

Navigation publication did not change:

- public Footer Navigation links
- dedicated Footer source, sections, social source, or social links
- StoreConfig storefront theme
- theme ID or theme tokens
- behavior settings
- homepage/product page configuration
- product templates

## Failure Matrix

The final browser/API run verified:

| Scenario | Expected | Result |
| --- | ---: | --- |
| Untrusted host without tenant context | 400 | PASS |
| Trusted tenant host without authentication | 401 | PASS |
| Forged `X-Tenant-Id` | 403 | PASS |
| Unknown authenticated Navigation endpoint | 404 | PASS |
| Stale RowVersion | 409 | PASS |
| Unknown backend entity target | 422 | PASS |
| Missing RowVersion and `If-Match` | 428 | PASS |
| Aborted Navigation load | fail-closed UI | PASS |
| Load after network recovery | normal editor | PASS |

The `422` response also matched the Navigation admission error contract.

## Restoration

The original pre-certification AhramStore payload was:

- Header: `Home` -> `/`
- Header: `Shop` -> `all-products` / `/products`
- Footer: About Us, Contact, Privacy Policy, Terms & Conditions
- draft and live identical
- no pending changes
- no validation warnings
- no broken links

The first read-only snapshot observed RowVersion `6047436`. Controlled certification writes correctly rotated the PostgreSQL concurrency token; RowVersion is intentionally not restored to a historical value.

After the final restoration publication:

- Admin draft exactly matched the captured original draft.
- Admin live exactly matched the captured original live snapshot.
- `hasPendingChanges` was `false`.
- validation warnings and broken links were empty.
- public Header exactly matched the original public Header.
- public Footer exactly matched the original public Footer.
- the disposable certification link was absent.
- a final direct public request returned only Home and Shop plus the original four Footer links.

## Automated Verification

### Admin

Command:

```text
npm test -- --run src/modules/storefront/cms/pages/NavigationEditor.test.tsx src/modules/storefront/cms/services/navigationAuthoringContract.test.ts src/architecture-tests/storefrontAuthoringPlatform.spec.ts
```

Result: `3` files passed, `26` tests passed.

Coverage includes duplicate-publication prevention, target/enabled/Arabic editing, in-flight save races, exact legacy-warning matching, and authoring-platform ownership.

### API And Backend

Command:

```text
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --filter "FullyQualifiedName~NavigationAdmissionServiceTests|FullyQualifiedName~StorefrontNavigationRuntimeTests|FullyQualifiedName~StorefrontNavigationAuthorityCertificationTests|FullyQualifiedName~AdminCmsPublishReliabilityTests|FullyQualifiedName~AdminCmsConflictControllerTests" --no-restore
```

Result: `38` tests passed.

Coverage includes target admission, public identity recomposition, hidden-target rejection, idempotent publication, concurrency responses, and authority architecture.

### Storefront

Command covered canonical Navigation runtime alignment, browser runtime cache recovery, theme runtime ownership, and Alizon/Hype/Pearl/Phoenix/Sharp layouts.

Result: `8` files passed, `117` tests passed.

### Production Builds

- API Release build: PASS, `0` warnings, `0` errors.
- Admin Vite production build: PASS.
- Storefront TypeScript and Vite production build: PASS.
- `git diff --check`: PASS.
- API `/health/live`: `200 Healthy`.

The frontend builds retain pre-existing bundle-size, mixed static/dynamic import, and Browserslist-age warnings. These warnings do not block Navigation certification.

## Evidence

Browser evidence is stored under `.opencode-tmp/navigation-cert/`:

- `01-admin-initial-desktop.png`
- `02-public-initial-desktop.png`
- `03-public-initial-mobile.png`
- `04-admin-published-cert-link.png`
- `05-public-arabic-mobile-published.png`
- `06-admin-final-restored.png`
- `Program.cs` contains the repeatable certification and failsafe-restoration harness.

## Primary Files

- `PointOfSale.API/Controllers/StorefrontNavigationController.cs`
- `PointOfSale.API/DTOs/Storefront/CmsPageDtos.cs`
- `PointOfSale.API/Services/Admin/AdminCmsService.cs`
- `PointOfSale.Storefront/src/components/layout/Header.tsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js`
- `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.test.tsx`
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.test.ts`
- `PointOfSale.Tests/Admin/AdminCmsPublishReliabilityTests.cs`
- `PointOfSale.Tests/Cms/StorefrontNavigationRuntimeTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontNavigationAuthorityCertificationTests.cs`

## Final Decision

Storefront Navigation is production-certifiable for the tested Admin, API, StoreConfig, public runtime, English/Arabic, desktop/mobile, tenant-isolation, concurrency, publication, and restoration paths. No open Navigation blocker remains.
