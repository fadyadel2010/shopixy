# Storefront Homepage — Final Certification (2026-09-24)

## 1. Root causes

- The builder reloaded its admitted workspace on `focus` and `visibilitychange`. A routine tab switch could remount the canvas/settings and replace local editing context.
- A 1.5-second implicit auto-save meant **Discard** could no longer discard a user edit after the timer fired. An in-flight save response could also replace a newer local edit with an older accepted snapshot.
- A new tenant's `GET /api/admin/store-config` returned a read-only default candidate with RowVersion `0`, but the first page-layout PUT required an already-persisted StoreConfig, returning 500 instead of admitting the initial draft.
- Related preview/rollback test fixtures submitted obsolete or incomplete section contracts; a combined in-memory EF test run created too many distinct internal service providers. These were test setup issues, not reasons to weaken production section validation.

## 2. Fixes

- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`: load on route/workspace admission, not on focus; make **Save Draft** explicit; keep a dirty local candidate until the server accepts it; retain newer edits made during save or publish; avoid redundant page-layout PUTs for clean preview/publish. Preview still uses the authenticated draft endpoint, never a live-content fallback.
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`: accept RowVersion `0` as the first page-draft insert when StoreConfig does not yet exist; reject a nonzero stale precondition as a conflict. Existing-tenant writes remain row-versioned. Draft saves do not publish.
- Added behavioral tests for focus/visibility, explicit Save Draft, in-flight edit preservation and first-write admission; refreshed old source assertions and section-contract fixtures; shared the theme tests' EF in-memory root across uniquely named test databases.

## 3. Backend contract and authority

| Operation | Route | Authority and response |
| --- | --- | --- |
| Workspace | `GET /api/admin/store-config` | Authenticated Owner/Admin, trusted tenant context; PostgreSQL StoreConfig; returns `tenantId`, `rowVersion` and **draft** `pages.home.sections` (or read-only default with RowVersion `0`). |
| Draft | `PUT /api/admin/store-config/pages/home` | `{ "sections": [...] }`, `If-Match: "<rowVersion>"`; validates supported section types, settings and order/IDs; saves only `CustomizationJson.pages.home.draft` in PostgreSQL; returns accepted StoreConfig DTO with new `rowVersion`. New tenants may create the StoreConfig on this write. |
| Preview | `GET /api/storefront/config/preview?preview=true` | Authenticated tenant-scoped draft mapped from PostgreSQL by StorefrontPreviewService; never substitutes live sections. |
| Publish | `POST /api/admin/store-config/pages/home/publish` | `If-Match`; validates draft, copies its normalized composition to `CustomizationJson.pages.home.live`, appends a unique version snapshot in `VersionsJson`, increments `LiveVersion`, stages publication events with the SQL commit. |
| Public | `GET /api/storefront/store-config` | Tenant-scoped PostgreSQL **live** StoreConfig, used by `PointOfSale.Storefront/src/pages/HomePage.tsx` via `config.pages.home.sections`; Redis/Mongo projections and cache invalidation are derivative, not authoring authority. |
| History/restore | `GET /api/admin/store-config/versions`; `POST /api/admin/store-config/rollback/{version}` | SQL-backed publication snapshots. Rollback requires `If-Match` and `{ "confirm": true, "reason": "..." }`; creates a new, monotonic publication version while preserving current drafts. |

Successful reads, draft writes, publish and rollback return 200. Missing/invalid precondition returns 428; stale RowVersion/contended publish returns 409; invalid composition returns 422; missing publish target returns 404; authenticated request/tenant authority is enforced. The builder admits response IDs and RowVersion rather than treating generated browser IDs or Marketplace HomepageSections as Storefront Homepage authority. On publish/rollback, StoreConfig integration events are staged for the outbox; on draft save, no live publication event is emitted.

## 4. Frontend contract

`/storefront/homepage` renders the section palette, sortable canvas, section settings and top-bar actions from one Zustand editor candidate. Drag/drop adds or reorders; the panel changes section settings; remove and Discard update local candidate/accepted draft respectively. Route changes and unload use the authoring dirty/busy guard. Save submits only the current page's sections with the admitted RowVersion; the accepted DTO refreshes the baseline and IDs. A concurrent local edit keeps `isDirty` true and is never overwritten by an earlier response. Preview reads the authenticated draft DTO; Publish uses the just-accepted RowVersion and publishes only after a successful save when dirty. Request errors, conflicts, loading and retry states remain explicit.

## 5. Draft/publish and version integrity

In the authenticated tenant, baseline draft and live each had four identical sections. A Text Block with a unique harmless heading became the fifth **draft** section after Save Draft, while live stayed at four. Reload restored five draft sections; draft preview rendered the heading. Publish made five sections live and rendered the same heading publicly. The original draft was saved back, then History rolled the publication back to the original version; both Admin draft and public live returned to the original four sections. The final history check found one current snapshot, unique version identifiers, version 24 for the test publication and monotonic version 25 for rollback from version 23.

## 6. Authenticated browser evidence

Chromium/Playwright drove the real React UIs through `http://localhost:5173/storefront/homepage` and the tenant's public `http://ahramstore.localhost:5174/` against API port 5010. Login and StoreConfig reads succeeded. Browser actions verified: initial palette/canvas; drag-reorder and Discard restoring original order; palette drag to add Text Block; heading edit; browser tab switch, blur/focus and visibilitychange without a workspace reload, lost draft or “Restoring session”; Save Draft (PUT 200); reload; Preview Saved Draft (draft GET 200); Publish Homepage (POST 200); live SQL response; public page showing the exact heading; Admin History and rollback (POST 200); public page restored and heading absent. Final post-build browser smoke confirmed four Admin sections, the original public hero, no test heading, and no session-restoration message. The temporary probe's final checks reported `draftRestored=true`, `liveRestored=true` and `sharedUnchanged=true`.

## 7. Cross-impact regression

- Baseline versus restored runtime StoreConfig matched for tenant identity, store name, currency, theme, navigation and footer. Theme/color tokens and typography remained unchanged; public Homepage was read from the correct tenant URL.
- Targeted Admin tests covered Storefront Settings, Navigation, Footer, theme registry, authoring information architecture, auth bootstrap/replay and tab refresh coordination. Public runtime tests covered sections, routing, navigation, theme/CSS, runtime ownership and cache recovery. Backend tests covered theme contracts, draft preview, publish reliability, rollback, controller status/preconditions and projection.
- Pre-existing changes in auth/session and other unrelated files in the working tree were retained.

## 8. Tests

- Admin/authoring/session targeted Vitest run: **113 passed / 113** (12 files).
- Additional Settings/Footer/Navigation/theme Vitest run: **67 passed / 67** (5 files).
- Public Storefront targeted Vitest run: **42 passed / 42** (8 files); an additional catalog ownership run passed 3/3.
- Relevant backend StoreConfig/preview/publish/rollback/controller/theme/projection xUnit run: **31 passed / 31**. First-draft insert and stale-first-write tests passed; failed fixture tests were corrected and rerun.
- `git diff --check`: passed (Git emitted only line-ending conversion notices).

## 9. Builds

- `PointOfSale.Web`: `npm run build` passed.
- `PointOfSale.Storefront`: `npm run build` passed (TypeScript and Vite).
- `PointOfSale.API`: Release `dotnet build --no-restore` passed with 0 errors.

## 10. Remaining limitations

- History rollback is a **whole StoreConfig live publication restore**, not a Homepage-only rollback; it deliberately preserves the current draft. To restore both states in this verification, the original draft was saved first, then the original publication was selected in History. Operators should account for concurrent StoreConfig changes before using global rollback.
- A broader, initially attempted architecture filter contained existing source-string/certification-artifact assertions unrelated to the Homepage repair; it did not pass and was not used as certification evidence. All focused, executable shared-path regressions above passed.
- `localhost:5174` alone cannot resolve a tenant; the public browser check used the tenant's supplied `ahramstore.localhost:5174` host.

## 11. FINAL VERDICT

**CERTIFIED for the tested tenant and workflow.** Homepage editing, explicit draft save, reload, authenticated preview, publish, public rendering, History restoration and session/tab stability worked end to end with matching PostgreSQL-backed frontend/backend contracts. The original live and draft Homepage states were restored, version history has a single current publication, and targeted shared-dependency checks and production builds passed.
