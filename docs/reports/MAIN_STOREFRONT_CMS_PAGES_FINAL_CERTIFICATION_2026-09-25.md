# MAIN Storefront CMS Pages Final Certification - 2026-09-25

## 1. Root Causes

The certification found and closed four repository-owned problems:

- `CmsPageEditor` adopted every successful save or publish response into the form. If an administrator typed again while a request was in flight, the older accepted response replaced those newer local edits.
- The PostgreSQL publish and rollback implementation previously needed explicit release of filtered unique draft/published slots before activating replacement rows. The repaired two-phase transitions and failed-save tracker clearing already present in the working tree were re-certified against live PostgreSQL.
- CMS workflow in-memory tests constructed `ApplicationDbContext` without a tenant context, so the production tenant query filter hid the test rows and produced false failures such as `Created CMS page could not be loaded.`
- Broad CMS test setup exposed a PostgreSQL-only `~` regular-expression check constraint to SQLite `EnsureCreated`. Several source-certification assertions also described superseded cache invalidation, route guarding, preview URL, DTO admission, and search-overlay implementations.

## 2. Fixes

- The editor records the exact submitted form fingerprint and keeps a live `formRef`. It adopts the accepted backend page/version after save or publish but preserves the form candidate when a newer fingerprint exists.
- The accepted backend fingerprint still advances after a response. The retained candidate therefore remains visibly dirty and its next save uses the newly accepted backend version.
- The same preservation rule covers both save and publish responses. Discard still restores the last backend-accepted draft.
- `CmsPageWorkflowTests` now use a test DbContext whose page filter excludes deleted rows without requiring an absent tenant fixture.
- The `SupplierCardFundingInstrument.Last4` regex check remains installed for Npgsql but is omitted from SQLite runtime models. Production validation was not weakened.
- Architecture assertions now certify middleware-owned no-store policy, permission-gated Admin routes, helper-based DTO admission, SQL tenant activity checks, and the current batch search overlay.

## 3. Frontend Contract

The canonical Admin routes are `/storefront/content/pages` and `/storefront/content/pages/:pageId`, with legacy `/storefront/cms/pages` aliases retained by the existing information architecture.

The list reads admitted backend DTOs and never publishes directly. The editor owns a local candidate and an accepted backend fingerprint. Save sends slug, title, content type, content, schema version, SEO fields, and `expectedVersion`. Publish uses the admitted draft version. History and rollback use backend version evidence and `If-Match`; the browser does not invent page IDs, versions, publication timestamps, or history snapshots.

Dirty-state behavior is explicit:

- Save Draft persists the current candidate.
- Discard restores the accepted backend candidate without autosaving.
- Tab switches, blur, focus, and visibility changes do not reload the editor.
- Route/unload guards remain active for unsaved changes.
- An older response cannot overwrite text entered after its request started.
- Preview is disabled for unsaved changes and opens the backend-created signed `previewUrl`.

## 4. Backend Contract

| Operation | Canonical endpoint | Preconditions and result |
|---|---|---|
| List | `GET /api/admin/pages` | Owner/Admin plus tenant context; `200` |
| Read | `GET /api/admin/pages/{pageGroupId}` | Tenant-scoped logical page; `200` or `404` |
| Create draft | `POST /api/admin/pages` | Valid content, slug, SEO; `201` |
| Update draft | `PUT /api/admin/pages/{pageGroupId}` | Body `expectedVersion`; `200`, validation `400`, stale `409`, missing precondition `428` |
| Publish | `POST /api/admin/pages/{pageGroupId}/publish` | `If-Match`; `200`, conflict/fail-closed error, missing precondition `428`, missing target `404` |
| History | `GET /api/admin/pages/{pageGroupId}/versions` | Tenant and group scoped; descending immutable snapshots; `200` or `404` |
| Rollback | `POST /api/admin/pages/{pageGroupId}/rollback` | Target version, confirmation/reason, `If-Match`; appends a new published version |
| Preview link | `POST /api/admin/pages/{pageGroupId}/preview-link` | Authenticated tenant scope; returns a signed URL for an exact version |
| Delete | `DELETE /api/admin/pages/{pageGroupId}` | `If-Match`; soft delete and publication event; `204` |
| Public page | `GET /api/storefront/pages/{slug}` | Anonymous trusted-host tenant resolution; published active row only |

`AdminCmsPagesController` is protected by `[Authorize(Roles = "Owner,Admin")]`. The Admin UI is additionally under `PosPermissionRouteGuard permission="storefront.design.manage"`.

## 5. PostgreSQL Authority

PostgreSQL `CmsPages` is the write and public-read authority. `PageGroupId` identifies one logical page; each row `Id` identifies an immutable version snapshot. `Version` is monotonic within the group.

Filtered unique indexes enforce at most one active draft and one active published row per page group, plus tenant-scoped active slug uniqueness. Publish and rollback run in serializable relational transactions and lock group rows with `FOR UPDATE`. Publication events are staged before commit.

The browser probe checked SQL after every important transition:

| Stage | Published | Draft | Max version |
|---|---:|---:|---:|
| Create | 0 | 1 | 1 |
| Publish A | 1 | 0 | 1 |
| Save B | 1 | 1 | 2 |
| Publish B | 1 | 0 | 2 |
| Rollback to A | 1 | 0 | 3 |
| Concurrent publish C | 1 | 0 | 4 |
| Delayed save | 1 | 1 | 5 |

Every SQL check also verified unique version identities. Final deletion and purge left zero rows for the disposable `cms-cert-*` slug.

## 6. Redis Role

Redis is not CMS Page authoring or public visibility authority. `CmsPageReadService` does not read a cached published scope, and publish correctness does not wait for Redis invalidation. Storefront HTTP responses are normalized by `StorefrontHttpCachePolicyMiddleware` to `private, no-store, no-cache, max-age=0, must-revalidate`, with `Pragma: no-cache`, `Expires: 0`, and `Surrogate-Control: no-store`.

The immediate A-to-B anonymous read after publication proved that stale Redis/browser HTTP content was not accepted as the live page.

## 7. Mongo Role

MongoDB CMS documents are derivative projections for projection/search concerns. They are populated through publication events and guarded by event freshness and SQL tenant lineage. Neither Admin page admission nor anonymous page rendering reads MongoDB as truth.

Projection lag therefore cannot make a draft public, keep an old page publicly authoritative, or authorize a tenant/page/version. No Mongo repair, wait, or manual synchronization was needed during browser certification.

## 8. Duplicate Authority Findings

No competing CMS Page authority was found.

- Admin list, editor, history, publish, rollback, and delete are PostgreSQL-backed.
- Public page visibility is PostgreSQL-backed.
- SEO and sitemap recomposition re-enter current SQL publication authority.
- Mongo/search documents are derivative and fail closed against SQL visibility.
- Redis and browser HTTP caches do not decide publication state.
- Client form state is only an unaccepted candidate until the backend returns an admitted DTO.

## 9. Duplicate Read Findings

No duplicate read path capable of returning an independently authoritative page was found. The storefront `CmsPage` component calls the Storefront API, which resolves the current published PostgreSQL row. Preview calls the same public controller in signed preview mode and resolves the token-bound SQL version by page group and version.

Search and sitemap paths may consume projections for discovery, but their visibility is overlaid or recomposed from current SQL state. They are not alternate page-body publication authorities.

## 10. Draft and Publish Model

Create produces draft v1 and no public page. Updating an existing draft replaces the active draft with a new version while preserving history. Publishing converts the active draft into the sole live row after first releasing the old live slot inside the transaction. A later draft coexists with the current published version and remains invisible publicly.

Rollback is group-scoped and append-only. Selecting v1 while v2 was live created published v3 containing v1 content and SEO; it did not mutate v1 or v2. A later C draft and concurrent publish produced one live v4. A delayed save then produced draft v5 while live v4 remained unchanged.

## 11. Validation Model

The backend is the final admission boundary. It normalizes slug and SEO, sanitizes rich text, validates schema versions, accepts only `RichText` or `Blocks`, and restricts blocks to `text`, `image`, and `faq` with type-specific shapes and limits. URLs must be safe relative paths or HTTP(S), and SEO text uses explicit length limits.

Authenticated browser-originated API requests proved that an unsupported `video` block and `javascript:` OpenGraph URL each returned `400 VALIDATION_ERROR`. SQL remained at draft v1 with no version mutation. Automated validator tests cover malformed content kinds, unsupported blocks, unsafe URLs, FAQ/text/image rules, schema constraints, and sanitization.

## 12. Auth Model

Admin endpoints require an authenticated Owner or Admin. The POS Admin route also requires the `storefront.design.manage` permission. The frontend uses the established cookie/session and auth replay infrastructure; this repair added no alternate token path and persisted no credentials.

An anonymous request to the Admin CMS list failed closed with `400` before exposing content in the active middleware pipeline. The controller remains explicitly authorization-protected. Real browser login established the Owner session and retained it while visiting all CMS and cross-impact routes.

## 13. Tenant Isolation Model

Admin tenant identity comes from `ITenantContext.RequiredTenantId`, not a client-selected body value. Public tenant identity comes from the trusted storefront host through tenant resolution middleware. CMS SQL queries constrain tenant, page group, deletion state, and draft/published state.

Browser evidence showed that a wrong-tenant public lookup did not return the disposable page. A forged `X-Tenant-Id` on the authenticated Admin request could not switch authority away from the authenticated tenant. Preview tokens also bind tenant ID, page group ID, slug, page version, and a 15-minute expiration.

## 14. Frontend and Backend Contract Alignment

The frontend service uses no-store requests and quoted `If-Match` values. DTO admission verifies tenant identity, page identity, slug/version coherence, and history snapshot coherence before rendering. The editor now centralizes accepted DTO adoption in `adoptAuthoritativePage`.

The delayed-response test proved both halves of optimistic concurrency alignment: the accepted backend version advanced from v4 to v5, while the newer browser title stayed dirty. The next action could therefore save against v5 rather than retrying stale v4 or losing the newer candidate.

## 15. Authenticated Browser Evidence

Real Chromium/Playwright drove `http://localhost:5173/storefront/content/pages` against API port 5010 with a clean browser context and real Owner login.

The final run passed:

- Four baseline pages rendered: About Us, Contact Us, Privacy Policy, and Terms & Conditions.
- New draft creation, reload persistence, dirty indication, Discard, and no-autosave behavior.
- Invalid content and unsafe SEO rejection without mutation.
- Signed preview popup with exact draft body and SEO.
- v1 and v2 publication, history, rollback, v4 concurrency, and v5 delayed-save race.
- Tab switch, blur, focus, and visibility return without lost input or `Restoring session...`.
- Homepage, Navigation, Footer, Themes, Colors/Typography, Storefront Status, and session routes loaded without losing authentication.
- No browser console/page errors remained at completion.

Harness result: `FINAL_BROWSER_CERTIFICATION PASS`.

Captured evidence is under `.opencode-tmp/cms-cert/`, including `01-pages-list.png`, `02-draft-preview.png`, `03-public-a.png`, `04-public-b.png`, `05-history-before-rollback.png`, `06-public-rollback-a.png`, and `07-final-restored-list.png`.

## 16. Public and Storefront Evidence

Before publication, anonymous `http://ahramstore.localhost:5174/pages/{slug}` returned `404`. Signed preview rendered draft A while SQL remained 0 published / 1 draft. Publishing v1 made A immediately visible; saving v2 left A live; publishing v2 immediately replaced A with B; rollback created v3 and restored A; concurrent publication made C live as v4.

Each public check validated visible heading/body and document metadata. After deletion and final SQL purge, the temporary slug returned `404`, the Admin list returned to the original four pages, and a second public check confirmed the page did not reappear.

## 17. SEO Evidence

Draft SEO never appeared on the anonymous public route. Signed preview used the draft meta title and description and emitted `robots=noindex, nofollow`. Published pages used the corresponding accepted meta title and description and emitted `robots=index, follow`.

The v2 draft changed title, description, keywords, and OpenGraph URL while public v1 SEO remained A. Publishing v2 exposed SEO B immediately. Rolling back v1 content created v3 and restored SEO A. Unsafe `javascript:` OpenGraph input was rejected before persistence.

## 18. Version History Evidence

The UI displayed v2 before v1 and exactly one `Published` marker. Rolling back v1 returned `200`, created monotonic v3, retained all prior snapshots, and restored the editor/public result.

After stale and concurrent operations, the history API returned versions `[4, 3, 2, 1]` in descending order with exactly one published entry. No failed contender created a phantom version. The delayed accepted save then created v5 as the sole draft while v4 stayed the sole published version.

## 19. Cross-Impact Regression Results

The browser visited canonical Homepage, Navigation, Footer, Themes, Colors/Typography, Storefront Status, and session-dependent Admin routes after the CMS concurrency/race sequence. Every route returned `200`, stayed out of `/login`, and retained the authenticated session.

The full Storefront Vitest suite covered routing, CMS rendering dependencies, navigation runtime, themes, runtime caches, editor bridge, checkout, auth bootstrap, and commerce authority. The targeted Admin suite covered CMS pages, the shared authoring platform, auth bootstrap/replay, and cross-tab refresh locking. No cross-impact failure remained.

## 20. Test Results

| Test gate | Result |
|---|---|
| Admin CMS/authoring/auth targeted Vitest | PASS: 9 files, 62 tests |
| Public Storefront full Vitest suite | PASS: 40 files, 366 tests |
| Backend CMS/storefront/tenant/auth/session Release matrix | PASS: 186 tests |
| Preliminary focused CMS backend workflow suite | PASS: 17 tests |
| Focused CMS editor/list tests | PASS: 2 files, 8 tests; included in the 62-test matrix |
| `git diff --check` | PASS: no whitespace errors; line-ending conversion notices only |

The broad backend matrix initially exposed 26 test defects: 16 SQLite setup failures from the PostgreSQL regex constraint and 10 stale source-certification assertions. After the provider-specific model correction and assertion refresh, all 186 passed.

## 21. Build Results

| Build | Result |
|---|---|
| `PointOfSale.API` Release | PASS: 0 warnings, 0 errors in the final incremental build |
| `PointOfSale.Web` production Vite build | PASS |
| `PointOfSale.Storefront` TypeScript plus production Vite build | PASS |
| Earlier API Debug build | PASS: 0 warnings, 0 errors |

Admin retains its existing large-chunk warning. Storefront retains existing Browserslist age, mixed dynamic/static import, and chunk-size notices. None blocked output generation or involved CMS correctness.

## 22. Files Changed

Product and regression changes for this certification:

- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.test.tsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.test.tsx`
- `PointOfSale.Tests/Cms/CmsPageWorkflowTests.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/SupplierCardFundingInstrumentConfiguration.cs`
- `PointOfSale.Tests/Architecture/CmsContentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsEndToEndAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsPageIdentityAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsPreviewAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsPublishAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsSeoAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsVersionHistoryRollbackAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemConvergenceCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- `docs/reports/MAIN_STOREFRONT_CMS_PAGES_FINAL_CERTIFICATION_2026-09-25.md`

The existing in-worktree `PointOfSale.API/Services/Cms/CmsPageService.cs` PostgreSQL transition repair was re-certified but not overwritten during this continuation. Extensive unrelated modified and untracked work was preserved.

## 23. Remaining Limitations

- The complete repository-wide backend suite was not run. The selected 186-test backend matrix covered CMS, CMS architecture, Storefront tenant isolation/publication guards, Admin tenant/route authorization, and POS session lifecycle; all passed.
- The live browser run used the user-managed API process on port 5010. A Debug rebuild attempt was blocked by that process's assembly lock, so automated backend reruns and the final explicit API build used separate Release outputs instead of stopping the live server.
- Under two simultaneous publish requests, one request returned `200` and the loser returned `400`; SQL and history remained correct with one live version and no phantom snapshot. The loser is fail-closed, although it is not surfaced as the same `409` stale-version response used by a stale update.
- Browser screenshots and the disposable probe are untracked temporary evidence under `.opencode-tmp/cms-cert/`.
- Frontend bundle-size/import warnings and repository compiler/analyzer warnings seen during the first Release test compilation are pre-existing non-blocking debt outside CMS Pages scope.

## 24. FINAL VERDICT

**PASS - CERTIFIED for the tested MAIN tenant, trusted host, and complete CMS Pages lifecycle.**

Storefront CMS Pages now have a coherent backend-owned contract for draft save, signed preview, publish, immutable history, append-only rollback, SEO, optimistic concurrency, tenant isolation, and immediate anonymous visibility. The confirmed in-flight response race is repaired, PostgreSQL remains the sole authority, Redis/Mongo remain non-authoritative, invalid and unauthorized inputs fail closed, cross-impact tests and production builds pass, and all disposable database/public/Admin state was removed. Final Admin state is the original four pages, and the certification slug is absent from PostgreSQL and returns `404` publicly.
