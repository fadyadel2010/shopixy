# Storefront Management — Phase 2 Content, Design & Publication

**Date:** 2026-10-16; evidence timestamps are UTC.

**Outcome:** Scoped remediation and safe QA executed for all seven Phase 2 groups. Final automated suites, controlled browsers, configured PostgreSQL rollback tests, and hosted read-only checks pass. **This is a qualified result, not certification of every live publication/projection/upload workflow or of the entire Storefront Management domain.** Remaining qualification gaps are explicit in section 10. Phases 3–8 were not started.

## 1. Scope, baseline and safety

- Mandatory Phase 1 report read: `SHOPIXY_STOREFRONT_MANAGEMENT_PHASE1_CORE_CONFIGURATION_20261016.md`. Relevant findings in the existing quality reports were investigated against current source rather than inherited as passes.
- Scope: Homepage; Catalog display/Product Template; CMS Pages; Header/Footer Navigation; independent Footer Content; Appearance/Theme Editor; StoreConfig Version History.
- Branch `main`, HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`. Extensive prior user/Phase 1 work was present. No staging, commit, stash, reset, checkout/discard, clean, broad deletion, schema migration/reset, or replacement application database was performed.
- Existing configured PostgreSQL `192.168.1.5:5432/PosEcommerceDb_Baseline` reused. Configured mutations use UUID-scoped synthetic tenants inside explicitly rolled-back transactions, controlled publishers/locks/audit boundaries, bounded SQL timeouts, deferred-constraint checks, and a fresh observer that verifies fixture absence.
- Test-local EF InMemory/SQLite fixtures are isolated test mechanisms, not a replacement application runtime/database. The two opt-in real Mongo tests were **not enabled**: they provision a disposable database, contrary to this task's database restriction.
- Hosted merchant authoring remained **read-only**. No actual AhramStore publish, restore, Footer save, CMS create/delete, theme activation, media upload, order, cart mutation, charge, shipment, inventory action, or provider command was initiated. Controlled-browser HTTP never reaches the API/database.
- Existing headed Edge session `opencode-ui-test` reused. Fresh controlled contexts and temporary public tabs were closed. Existing Admin, Storefront cart and unrelated Easy Orders tabs were preserved. No credentials, cookie/storage-state export or real preview tokens were written into task scripts/report/evidence.

**Evidence root:** `.ai/evidence/storefront-phase2-20261016/`, abbreviated **E/** below.

### Runtime identity

| Runtime | Observed identity |
|---|---|
| Admin / Storefront Vite | Existing PIDs 16844 / 21024, ports 5173 / 5174 |
| API baseline | No listener on 5010; hosted checks initially blocked |
| API activation | User started API; PID **10788**, start `2026-10-16T17:16:00.051Z`, normal Debug executable, Development environment, content root `PointOfSale.API` |
| Loaded API | `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.dll`; MVID `3e36f2d0-99c1-4152-bfaa-91e94eca66f1` |
| API SHA256 | `CB33C0FD9DA2740D64D568BE9B74B1B7BCC3095476D0A12EDBD7F0A0679C78DB` |
| Build correspondence | Loaded API, Infrastructure and Core hashes all match the isolated repaired build; `E/api-active.json`, `E/api-build-match.json` |
| Tenant | AhramStore, `c9c5c1ae-949e-498e-bc63-07242daeb49d`; existing Owner session recovered without saving credentials |

The user started the normal Debug executable, not the suggested isolated launch command. Its actual loaded artifacts were independently verified; another restart was unnecessary. No active API binary was overwritten by the Release test builds.

Readiness initially returned **503 / Unhealthy** at `17:17:25Z`, with **R2 unavailable**. A later actual observation at `17:21:44Z` returned **200 / Degraded**, with certificate-provider execution and Caddy integration disabled. The later recovery does not erase the R2 failure or certify uninterrupted provider availability. See `E/readiness-started.json` and `E/readiness-final.json`.

## 2. Evidence classes

- **H — Hosted:** real Owner UI/API/configured database and real public tenant reads; no merchant authoring writes.
- **D — Merchant SQL read-only:** actual persisted merchant records and non-secret fingerprints; not browser publication evidence.
- **P — Configured PostgreSQL:** actual provider and constraints, synthetic data, rollback-only; external/event boundaries controlled.
- **C — Controlled browser:** actual React source and actual clients/stores, intercepted application HTTP, synthetic authority; not live persistence/provider execution.
- **U — Isolated automation:** Vitest and backend service/controller/MVC tests. HTTP authorization tests control authentication and stop admitted requests before business mutation.
- **S — Source trace:** inspected wiring/ownership, not a substitute for external execution.

## 3. Coverage and authority trace

### Homepage

| Contract | Trace / current evidence | Qualification |
|---|---|---|
| Editor/read admission | `/storefront/homepage` → `StorefrontBuilderPage` → `storeConfigService` → Admin StoreConfig GET and runtime GET → tenant identity + accepted revision → builder store | H: existing authored sections load; C: synthetic backend admission and actual Save Draft client; U: rejected/missing authority does not create an editable replacement |
| Editing/serialization | Palette, section store/history, registry/strict settings contract, page-specific availability → `PUT /api/admin/store-config/pages/home` | C: add editorial section and save/readback; U: composition, media authoring, contracts, unsaved work, concurrency and discard regressions. Not every section field was browser-edited |
| Category/resource authority | `AdminStoreConfigService.UpdatePageLayoutAsync` and `PublishPageAsync` → strict composition → curated product/media validation and new category lineage validation | U red→green: foreign category rejected at save and publish for both grids; owned live lineage accepted; subsequently deleted ancestor blocks publication |
| Draft/live separation | `CustomizationJson.pages.home.draft/live` → accepted xmin → publication snapshot/event staging → live mapper | P: draft reload excluded from live; real publish and SQL mapper agree; synthetic fixture rolled back. H: public home composition exactly equals Admin `livePages.home`, not editable draft |
| Public render/recovery | SQL-authoritative public config → Storefront config store/runtime cache → `HomePage` / `SectionRenderer` / theme resolver/runtime containers | H: real Homepage renders. C: enabled editorial section renders, disabled section suppressed. U: late tenant/live response cannot replace current config or controlled preview |

### Catalog display / Product Template

There is **no independent Catalog Display settings screen** in the current route tree. Existing authoring is `/storefront/catalog/product-template`; public catalog controls belong to the Products runtime. No invented screen or setting was added.

| Contract | Trace / current evidence | Qualification |
|---|---|---|
| Product Template authoring | Builder URL/template selection → registry/composition → `pages/product-template:<id>` draft/publish storage and template deletion endpoint → StoreConfig | H: actual default template editor loads. C: built-in default deletion disabled. U/S: templates, section availability, mandatory Product Hero, supplemental section composition, serialization and page contracts |
| Public catalog display | `ProductListPage` → discovery hook/URL query → `GET /api/storefront/products/discover` → public product cards and sort control | H: **4 actual discovery items**, card text renders, sort enabled; not merely a nonempty loading surface |
| Public template consumer | `/products/cert-media-variable-20261007` → real Product Details read → Product Details runtime/ProductTemplateRegistry → runtime-owned core sections | H: real product heading and no unknown-template-section surface; network API responses captured, no console/page errors. U/S: runtime/catalog ownership tests ensure theme/template content does not replace commerce behavior |
| Mutation limits | No shared product/template creation, deletion, assignment or publication performed | Creation/deletion/publish and varied template assignments are U/S rather than hosted merchant mutations. No checkout/cart interaction used to manufacture evidence |

### CMS Pages

| Contract | Trace / current evidence | Qualification |
|---|---|---|
| List/editor identity | `/storefront/content/pages` and `/:pageId` → `cmsService` → `/api/admin/pages` → Owner/Admin + design permission + authenticated tenant → `CmsPageService` → versioned `CmsPage`/translations | H: list and actual record editor load. U: page/tenant identity admission and malformed/foreign response tests |
| Rich text / blocks / translations / SEO | Sanitized rich-text candidate or structured blocks; required English content, optional complete Arabic translation; backend content validator/slug/SEO authority | C: continuous Unicode typing, Ctrl+S before blur, duplicate save suppression and newer-edit retention. P: real create/sanitization, draft exclusion, foreign-tenant read. U/S: blocks, localized workflow, slug/reserved-route/SEO/content validation |
| Lifecycle/history | Create/update/publish/delete/rollback → expected page version → serializable mutation, uniqueness, published snapshot and staged CMS event | Existing U lifecycle/publication-reliability tests run. H only reads/cancelled inspection. **P does not claim to execute CMS lifecycle publish/rollback**: a controlled published SQL fixture is used for public read/navigation/footer integration |
| Preview/public consumption | Backend-issued preview access/token authority → public page service; normal public read resolves published slug/version → `CmsPageView` and sanitizing renderer/SEO | H: currently published route API/browser agree; Arabic/RTL at 390px. C: public SEO/content/XSS defense, locale reload, missing-page surface. U/S: signed preview contracts; no real preview token persisted |
| Failure truthfulness | Accepted delete/default-seed followed by failed list reload now says saved-but-readback-failed | U regression; no false complete-success message and no repeat mutation encouraged |

### Header / Footer Navigation

| Contract | Trace / current evidence | Qualification |
|---|---|---|
| Both views/authoring | `/storefront/navigation?view=header|footer` → `NavigationEditor`/CMS store → `/api/admin/cms/navigation` and `/targets` | H: both views and targets load. Auto-save is real; hosted editing deliberately avoided |
| Resource and URL authority | `NavigationAdmissionService` → tenant/live category lineage, product publication/visibility, CMS stable group identity and canonical published route, collection key, safe custom/external URL rules | Selected U admission tests cover unavailable/foreign targets and invalid contracts; P resolves an actual synthetic tenant CMS identity to its canonical slug |
| Persistence/publication | Revision-checked draft/live navigation in StoreConfig → monotonic snapshot → `NavigationPublishedEvent` → save/outbox boundary | P: draft/publish readback and stale xmin refusal; U: event-stage failure and actual actor attribution. Captured event verification is not RabbitMQ delivery proof |
| Publication races/lifetime | Save queue, request aborts, publication exclusion, accepted response versus current candidate, account/store workspace boundary | U/C: newer edits retained with the accepted revision; U: old published response cannot repopulate the next workspace after unmount |
| Consumer | Public navigation controller reads current live SQL state and resolves valid links; runtime navigation store → Header/Footer runtime/presentation | H public endpoint returns content. C actual Footer runtime consumes Navigation independently; U: pending old tenant/public chrome rejected after tenant/preview changes |

### Independent Footer Content

| Contract | Trace / current evidence | Qualification |
|---|---|---|
| Separate live surface | `/storefront/footer-content` → `StorefrontFooterSettings` → shared `FooterConfigScreen` → `GET/PUT /api/admin/footer` | H: existing editor loads and clearly says saving publishes immediately. This is **not** the draft Footer Navigation view |
| CMS destination | Published Navigation targets own the live route; draft-oriented page list no longer supplies a possibly renamed draft slug | U: renamed pending draft still offers the original published route; existing backend validator remains authoritative |
| Persistence/concurrency | `FooterAuthoringService` + validator/mapper → tenant-scoped separate footer tables → expected revision → serializable replacement + event | P: revision changes, real persistence and public SQL Footer reader resolve canonical CMS URL. U: revision/foreign/scope/error-recovery tests |
| In-flight edits | Save response advances baseline/revision without replacing a newer candidate | U/C: newer edits remain dirty; subsequent save uses accepted revision; submitted Footer and newer local work described separately |
| Public fallback/locale | `StorefrontFooterReadService` → Tenant/Global/Derived selection, localized links/social data → Footer store/runtime | H actual endpoint reports source. C real Footer runtime, CMS href and Arabic payload render; U late tenant/language/public-preview response regressions |

### Appearance / Theme Editor

| Contract | Trace / current evidence | Qualification |
|---|---|---|
| Theme catalog | `/storefront/appearance/themes` → current authoritative config + local available-theme registry → narrow activation endpoint | H: real active **Alizon** and available themes render. U/C: load failure is unavailable with working Retry, never fabricated “No active Theme”; duplicate activation suppressed |
| Colors/Typography | `/storefront/appearance/colors-typography` → materialized draft/live themes + presets → token validation → narrow draft overrides → explicit publish | H real editor loads. C invalid color blocks save; real client sends draft-only overrides. P: draft/live isolation, publish, stale version and event-stage failure on configured PostgreSQL |
| Runtime/style ownership | Live mapper/theme resolver → CSS tokens, layout variants, section resolver; runtime-owned Product Details sections cannot be replaced by theme executable content | Selected U runtime-token/draft-preview/contract tests and S trace; H observes existing live theme, not a shared merchant activation |
| Preview/lifetime | Signed preview contract and local editor candidate; chained publication stops if workspace closes during draft save | U/S; no live theme activation or merchant Appearance publish performed |

### StoreConfig Version History

| Contract | Trace / current evidence | Qualification |
|---|---|---|
| Read/admission | `/storefront/storeconfig-version-history` → authoritative config revision + `/api/admin/store-config/versions` → real StoreConfig snapshots | H: current v345 and history load. U malformed/nonpositive/duplicate IDs or invalid dates fail closed, not as fabricated empty/restorable data |
| Confirmation/concurrency | Review revision captured during load → warning + checkbox/reason → `rollback/<version>` with reviewed If-Match; synchronous duplicate guard | H: actual warning/disabled confirmation inspected and cancelled. U/C: no post-confirmation revision refresh that silently authorizes overwriting concurrent changes; conflict retains explicit retry surface |
| Restore semantics | Validate historical live/theme authority → restore published StoreConfig while preserving current drafts → append **new** monotonic snapshot → stage rollback/publication events | P: actual restore from a synthetic publication; new current version and restored-from metadata; CMS and separate Footer preserved. U selected rollback safety/reliability tests |
| Attribution/downstream | Navigation snapshots now use authenticated actor identity; StoreConfig/CMS history remain separate; projections/cache invalidation traced | U verifies actor fields; P captured events; no hosted historical restore or claim that every downstream cache consumed a new event |

## 4. Confirmed repairs

| ID | Confirmed cause / effect | Repair and regression evidence |
|---|---|---|
| HOME-01 | Curated category IDs lacked tenant/live-lineage admission at save and publish | Existing category authority now validates both category grids in both operations; five failing cases observed before repair, final backend green |
| HOME-02 | Conflict Load Latest cleared local candidate before a failed read and could announce successful replacement | Preserve candidate on failed read; announce latest-loaded only after admitted success; U regression |
| CMS-01 | Rich text updated React only on blur; keyboard save/unload guard could omit focused edits | Sanitized candidate updates on input without replacing focused DOM; accessible multiline textbox; U red→green and C typing/Ctrl+S |
| CMS-02 | React busy state alone did not synchronously suppress repeated save/publish | Synchronous save/publish guards; stop chained publication after unmount; late preview cannot open after closure; U/C |
| CMS-03 | A committed list mutation followed by readback failure still announced full success | Distinct saved-but-reload-failed outcome, duplicate mutation exclusion; U |
| NAV-01 | Accepted publish response replaced edits made while publication was pending | Preserve current candidate, rotate accepted revision, keep dirty state; publication exclusion and aborted/late-response admission; U red→green/C |
| NAV-02 | Navigation history always attributed publication to generic POS Admin | Resolve actual authenticated actor name/ID, explicit unknown fallback; U verifies stored snapshot |
| FOOT-01 | Live-save completion replaced newer Footer edits | Accept baseline/revision separately; retain candidate and truthful submitted-live/newer-unsaved message; U red→green/C |
| FOOT-02 | Footer target options used draft page slug even when published slug differed | Use published canonical Navigation target route; U renamed-draft regression |
| THEME-01 | Failed theme authority read fell through to apparent no-active-theme state without effective retry | Explicit unavailable state and retry; no substitute identity; synchronous activation guard; U/C |
| HIST-01 | Restore fetched a new revision after administrator confirmation, silently bypassing reviewed-state concurrency | Use reviewed revision; validate history identifiers/dates, duplicate guard, scoped exclusions and accessible reason; U/C/H |
| SCOPE-01 | Local candidates/dialogs could survive account/store or authoring-route replacement; pending shared-store responses could leak back | Keyed Storefront workspace boundary and scoped lifetime/request guards; U tenant/account remount, Navigation late-response and CMS late-preview regressions |
| RUNTIME-01 | Pending public config/Navigation/Footer could apply after tenant/language change or overwrite a newer authenticated preview | Response admission checks request generation, current identity and preview ownership; five added regressions, including three additional red→green chrome cases |

These are task-local additions, not claims of authorship over pre-existing authority, registry, validation, transaction, preview or projection infrastructure.

## 5. Executed results

| Check | Final result | Evidence |
|---|---|---|
| Entire Admin Vitest selection | **1,897 passed, 0 failed/skipped** | `E/admin-final.json` |
| Entire Storefront Vitest selection | **533 passed, 0 failed/skipped** | `E/storefront-final.json` |
| Selected backend | **498 passed, 2 skipped, 0 failed / 500** | `E/phase2-backend-final.trx` |
| Configured content + four theme scenarios | **5 passed, no skips**; additionally included in backend final | `E/phase2-configured-final.trx`, `E/configured-content-rollback.json` |
| Phase 2 MVC authority matrix | 140 cases, included in backend final | Seven controlled actor/authority cases across twenty request shapes; resource filter prevents mutations |
| Hosted seven-group read-only | **31/31 checks**, no uncaught errors, no attempted/forwarded authoring writes | `E/browser-hosted-first.json` |
| Hosted Catalog/Product Details | **6/6 checks**, no console/page/API errors captured | `E/browser-catalog-first.json` |
| Controlled Admin source/browser | **13/13 checks**, no uncaught errors | `E/browser-controlled-sixth.json` |
| Controlled public source/browser | **8/8 checks**, no uncaught errors | `E/browser-public-final.json` |
| Admin / Storefront typechecks | Passed; final production builds also typecheck | Terminal completion and build logs |
| Admin production build | Passed after final Admin source edits | `E/admin-build-readable.log` |
| Storefront production build | Passed after final Storefront source edits | `E/storefront-build-verified.log` |
| Repaired API isolated Debug build | Passed; loaded artifacts match | `E/api-build-match.json`, `E/api-active.json` |
| Focused Admin lint | **0 errors, 10 warnings** | `E/admin-lint-first.json` |
| Focused Storefront lint | **0 errors, 0 warnings** using explicit maintained Admin TypeScript lint configuration | `E/storefront-lint-final.json`; native Storefront has no discovered ESLint config |
| Scoped patch whitespace check | Passed; existing LF→CRLF notices only | `git diff --check` completion |

The two final skipped backend tests are `StoreConfigConvergenceTests.RealMongo_*`, intentionally not enabled. They are **not passes**, nor evidence of a failed configured PostgreSQL test.

### Reproducible commands

From `PointOfSale.Web`:

```powershell
npx vitest run --maxWorkers=2 --testTimeout=20000 --reporter=json --outputFile=../.ai/evidence/storefront-phase2-20261016/admin-final.json
npx tsc --noEmit
npm run build
```

Focused Admin lint used `node scripts/lint.cjs` on App, workspace boundary/tests, CMS pages, modified Builder/History/Theme files/tests and shared Footer source/test, with JSON output `E/admin-lint-first.json`. This is focused lint, not project-wide lint certification.

From `PointOfSale.Storefront`:

```powershell
npx vitest run --maxWorkers=1 --reporter=json --outputFile=../.ai/evidence/storefront-phase2-20261016/storefront-final.json
npx tsc --noEmit
npm run build
```

Storefront focused lint ran from Web, using `node scripts/lint.cjs` with the four changed Storefront cache/store/test paths, `--no-ignore --config .eslintrc.cjs --format json`, output `E/storefront-lint-final.json`. No rule suppression/config migration was introduced.

Backend final environment:

```powershell
$env:SHIPPING_CONFIGURED_ROLLBACK_QUALIFY='1'
$env:STOREFRONT_PHASE1_CONFIGURED='1'
$env:STOREFRONT_PHASE2_TENANT='c9c5c1ae-949e-498e-bc63-07242daeb49d'
$env:STOREFRONT_PHASE2_SNAPSHOT_STAGE='after-final-rollback'
$env:STOREFRONT_PHASE2_EVIDENCE=(Resolve-Path '.ai/evidence/storefront-phase2-20261016').Path
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AdminStoreConfig|FullyQualifiedName~PointOfSale.Tests.Cms|FullyQualifiedName~NavigationAdmissionServiceTests|FullyQualifiedName~StoreConfigConvergenceTests|FullyQualifiedName~HomepageMediaAuthoringTests|FullyQualifiedName~HomepageHeroHighlightContractTests|FullyQualifiedName~StorefrontThemeDraftPreviewTests|FullyQualifiedName~StorefrontThemeRuntimeTokenTests|FullyQualifiedName~AdminCmsPublishReliabilityTests|FullyQualifiedName~StorefrontPhase2HttpAuthorizationTests|FullyQualifiedName~StorefrontPhase2ReadOnlyConfiguredTests|FullyQualifiedName~PosCloseoutHttpAuthorizationTests|FullyQualifiedName~StorefrontPhase1HttpAuthorizationTests" --logger "trx;LogFileName=phase2-backend-final.trx" -v quiet -p:WarningLevel=0
```

Browser scripts execute using the existing named session:

```powershell
playwright-cli list
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase2-controlled.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase2-public-controlled.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase2-hosted-readonly.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase2-catalog-readonly.js
```

## 6. Baseline and non-green execution disclosure

- Scoped Admin baseline: **365/365**. Storefront baseline: **528/528**. Selected backend baseline: **196 passed, 4 skipped**; configured theme cases were not opted in at baseline.
- Rich-text, newer Footer/Navigation edits, public config races, category ownership/lineage, and additional Navigation/Footer chrome races have recorded pre-fix failing regressions. See `admin-red.json`, `storefront-red.json`, `storefront-chrome-red.json`, `phase2-category-red.trx`.
- An intermediate Admin run with two workers failed on timeouts; a later scoped one-worker run passed 376 tests. The first whole-Admin one-worker run exceeded its 600-second terminal limit and was **not counted** as a pass. Final two-worker run with a 1,200-second limit completed green.
- One Storefront run reported a worker-termination timeout warning despite green assertions; the final complete rerun finished without that warning after final edits.
- A temporary const refactor of deferred request records passed runtime tests but failed TypeScript definite-assignment checks. It was corrected with explicit nullable initialization and response-admission checks; final typecheck/build/full Storefront suite and focused lint were rerun green. `storefront-build-final.log` retains the intermediate failure; **`storefront-build-verified.log` is the final build**.
- Early controlled-browser scripts had harness errors: React client export shape, unavailable Node URL global, incomplete synthetic store authority, ambiguous Delete locator and duplicate Vite HMR module identities. Their intermediate files are diagnostics, not application passes. Final scripts use actual transformed dependency identities and all pass.
- Focused Admin warnings remain: hook dependency advisories and mixed-export Fast Refresh warnings. Existing build warnings include chunk size, circular/mixed imports, stale Browserslist and deprecated Vite esbuild options. No warning-free/global-lint claim is made.

## 7. Merchant-state preservation

- Hosted script compares full before/after Admin **config, CMS list, Navigation, Footer and StoreConfig history responses** in memory; all equal. Its write guard observed **zero attempted authoring mutations**.
- Independent D snapshots bracket the final rollback checks, API startup and hosted verification: StoreConfig, CMS records, Footer sections/links and social rows have equal SHA256 fingerprints. See `E/merchant-preservation-final-window.json`.
- Observed merchant state: **live**, StoreConfig **v345**, rowVersion **10584330**, **22 CMS version records**, **1 Footer section**, **4 Footer links**, **0 social links**. A version-record count is not a logical-page count.
- D bracket: `17:13:13Z` → `17:22:27Z`; this is a precisely bounded final-window preservation proof, **not a byte-level baseline for the whole day/shared database**. No task merchant writes occurred before this bracket either; earlier configured fixtures were separately rollback-verified.
- P fixtures leave no synthetic tenant/config/CMS/Footer section state behind. Controlled event verification does not prove delivery to the real bus. Normal unrelated background workers were not disabled or assumed frozen.

## 8. Files and workspace preservation

- **853** scoped pre-edit original copies: **23 intentionally changed, 830 unchanged, 0 missing**. `E/originals-comparison.json` and `E/task-existing-files.diff` distinguish task changes from prior user work.
- Two additional existing test-only edits were not separately captured as pre-edit original copies: make `HomepageMediaAuthoringTests` partial to reuse its harness, and extend the shared authorization host's controller allowlist. These narrow edits are listed explicitly rather than claiming complete original-snapshot coverage.
- **40 source/test/browser files** changed/added, including **15 new files**. Exact paths and final hashes: `E/task-files.txt`, `E/task-files-manifest.json`. Report/evidence/generated build files are excluded from that source-file count.
- New deliverables include category admission, configured rollback/read-only, HTTP authorization and editor/runtime regressions; workspace-boundary component/declarations; four browser scripts.
- All **579** baseline tracked deletions remain deleted; all **30,801** baseline file-level untracked entries remain present. HEAD unchanged; index remains HEAD-equivalent/unstaged. See `E/preservation-check.json`, `E/final-git-status.txt`. No whole-repository content-hash baseline exists, so byte-level preservation of every unrelated file is not asserted.

## 9. Security and authorization interpretation

The Phase 2 real-MVC matrix exercises Owner/Admin admission, restricted roles, anonymous and untrusted-owner authority across content/design endpoints without activating mutations. Hosted anonymous requests with only a raw tenant header returned **400** at trusted-tenant resolution; hosted foreign tenant spoof returned **403**. These hosted middleware results are not mislabeled as the controlled MVC layer's 401/403 outcomes. No alternate real staff/customer identities were created merely to exercise role tests.

## 10. Remaining limits / stopping point

1. **No live merchant publication or restore certification.** Positive/negative mutations are P/U/C where unsafe on shared state. Full UI → real HTTP mutation → real outbox → RabbitMQ → every projection/cache → already-open public tab convergence was not executed as one hosted workflow.
2. **CMS configured lifecycle gap.** Real PostgreSQL create/sanitization/public read were exercised, but lifecycle publish/update/delete/rollback uses isolated tests rather than a committed merchant fixture or nested service-owned transaction. P's published fixture is explicitly not a lifecycle-publish claim.
3. **Media/provider readiness remains qualified.** No actual upload/storage mutation; R2 returned an observed readiness 503 before recovery. Current 200/Degraded is not upload reliability or external DNS/TLS/Caddy certification.
4. **Catalog/theme breadth is bounded.** Actual catalog/products/default-template consumer and real active theme observed; all template assignments, every section/control, every marketplace theme/device and merchant activation were not exhaustively browser-tested. No commerce behavior was exercised.
5. **Preview/long-lived state breadth is bounded.** Signed preview contracts and late-response/tenant/locale cases have U/C evidence; exhaustive hosted token-expiry, role-switch and long-running tab convergence is not certified.
6. **Tooling debt remains.** Ten focused Admin warnings and existing build warnings; native Storefront ESLint configuration is absent, so its focused result explicitly uses the maintained Admin config. Two Mongo integration tests are deliberately skipped.

The API is running the repaired source and does not need another restart for these changes. Phase 2 stops at this qualified evidence boundary. **No Phase 3–8 work or whole-domain certification is claimed.**
