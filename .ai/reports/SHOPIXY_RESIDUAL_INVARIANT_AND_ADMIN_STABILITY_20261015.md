# Shopixy — residual product invariants and Admin test stability

**Date:** 2026-10-15  
**Disposition:** **Scoped repairs and measured stability verified; not global/production certification.**  
**Scope:** the two residual backend assertions and seven Admin failures identified by the [quality closeout](SHOPIXY_QUALITY_CLOSEOUT_REPORT_20261015.md). No renewed general audit or lint remediation.

## Outcome

- Both original assertions relied on obsolete implementation details. The backend product/variant authority and central HTTP cache contract are intact. Replaced the obsolete source requirements with executable SQL-recomposition and HTTP-contract checks, while retaining the other authority assertions.
- Investigation also demonstrated frontend defects: incomplete/foreign DTOs were accepted without admission; late detail and variant responses could overwrite newer presentation. These are repaired, with retained failing-before/passing-after tests. Invalid variant feedback now uses an existing translated message rather than a missing localization key.
- The seven historical Admin errors are consistent with Vitest's five-second wall-clock timeout path, not seven demonstrated product failures. An otherwise unchanged serial broad run passes **1,755/1,755**; one previously failing workflow takes **4.91 seconds**. Dialog-query scoping and global-stub cleanup improve the test fixture without increasing timeouts, skipping tests or changing the Admin application.
- Final current-source evidence: **Admin 1,761/1,761**, **Storefront 523/523**, **both requested backend tests 2/2** and **related backend tests 78/78**; frontend builds and typechecks pass. The deliberately expanded backend/cache run remains **85 passed / 3 unrelated failures**. Global production certification and arbitrary-load stability are not claimed.

Evidence root: [`.ai/evidence/residual-invariants-20261015/`](../evidence/residual-invariants-20261015/); [machine-readable summary](../evidence/residual-invariants-20261015/final-summary.json). Runs overlap; counts are not additive.

## 1. Preserved runtime and safety

At entry, the existing listeners are:

| Application | Port | PID | Started UTC |
|---|---|---|---|
| API, original Debug apphost | 5010 | 13648 | 2026-10-15 16:34:01 |
| POS Admin Vite | 5173 | 14300 | 2026-10-15 11:06:32 |
| Storefront Vite | 5174 | 13352 | 2026-10-15 11:06:45 |

See `processes-before.json` and `api-before.json` for executable paths, parent processes, launch environment, configuration hashes and loaded assembly identities. This phase needed and performed no API replacement/restart: backend production code was not changed. Release test binaries were rebuilt separately; browser Vite serves the repaired frontend source.

The earlier read-only recheck (`api-final.json`, `processes-final.json`, `runtime-final-verification.json`) still found the original PID and identical identity. **At actual closeout, the API had restarted outside this agent's actions:** current PID **14876**, started **2026-10-15T17:42:35.4133376Z**. `listeners-at-closeout.json`, `api-at-closeout.json` and `runtime-at-closeout-comparison.json` confirm that the executable, content root, complete environment fingerprint, configuration hashes and all three loaded assembly MVIDs/hashes remain **identical** to the entry process. The original Vite PIDs remain unchanged. The agent neither requested nor performed this later restart; its cause is not established here. Hosted HTTP/browser observations were collected against PID 13648, not rerun against 14876. The separately built Release test assembly did not replace the running Debug artifacts.

Reused the existing AhramStore host and browser session. No credentials were added to source/evidence. No shared cart, order, payment, inventory or financial mutation; no provider operation, message, registration or OTP dispatch. Variant-resolution POST is a read-only contract. Invalid/spoofed requests fail before business mutation. Browser checks never click Add to Cart; a fixture guard blocks unexpected business writes. Ordinary page analytics and existing authentication refresh remain allowed; this is not a claim of zero telemetry/session activity.

No Git staging, commit, reset, clean or stash. No database provisioning/destructive fixtures, schema recreation or history repair. New backend behavior tests reuse the established isolated SQLite fixture, not a new database service; Kestrel HTTP tests control service/tenant boundaries and never connect to the business database. Real hosted HTTP evidence is distinguished from these controlled tests below.

The extensive pre-existing working tree is retained in `baseline-git-status.txt`, `baseline-working-tree.diff` and selected `baseline-source/` copies. `owned-baseline.diff` isolates this phase's edits to shared files; other modifications remain user work. `final-git-status.txt` and `status-delta.json` retain the final comparison; no prior status entry disappeared. Scoped `git diff --check` exits 0 (only existing Windows LF/CRLF advisories).

## 2. Product-detail authority

### Original assertion and history

`ProductDetailFieldsMustBeBackendComposedAndFrontendPresentationOnly` expected the literal `fetchProductBySlug(slug)`. Commit **32ffffc** (2026-09-24, `fix performance issues`) changed the page to `fetchProductByIdentifier(productIdentifier)`: canonical slugs still use the slug endpoint, while historical cards without slugs use the tenant-scoped by-ID endpoint. That change did not move product composition to the frontend. See `relevant-history.diff`.

The intended invariant is documented in `docs/architecture/catalog-presentation-authority-certification-findings.md`: current tenant-owned SQL product/category/variant/stock/media/promotion/publication state owns presentation-admissible product truth; MongoDB, Redis and Elasticsearch are derivative candidates.

### Actual trace

1. `ProductDetailsPage` receives the route identifier and calls the detail API, alongside category navigation loading.
2. `fetchProductByIdentifier` selects `/api/storefront/products/{slug}` or `/by-id/{id}`. It does not build a product from a listing/card or browser storage.
3. `StorefrontProductsController` enters the trusted `ITenantContext` and `StorefrontProductService`.
4. Cold Mongo candidates are tenant/identity checked and passed through `ProductSearchSqlAuthorityOverlay.ApplyAsync`. Warm Redis candidates are also recomposed against current SQL. Current slug, affiliate eligibility, tenant/publication/category/product visibility and variant ownership are checked before return.
5. Backend DTO mapping composes names/descriptions/category/media, variants, effective price, promotion pricing and availability. Stock/purchasability are backend decisions, not calculations from option counts or stale projection fields.
6. The page passes the admitted backend product unchanged to `SectionRenderer`. Runtime props override template-section candidate data. Product-detail executable sections use the fixed runtime implementation; narrative parsing/localization, media ordering and money formatting are presentation transformations, not new product/commercial authority.

### Demonstrated frontend defects and repair

The old API wrapper returned arbitrary `response.data`, including wrong-route products and incomplete pricing/availability. Incomplete data could reach fallback presentation instead of failing closed. New `requireProductDetailAuthority` checks route identity, required product/variant pricing and availability, name and collection shape; it returns the original object, never synthesizing price, stock, purchase permission or promotion. Missing optional description/empty media remain absence, not invented marketing content.

The page also lacked request ownership: a slow old route response could replace a newer product. An effect-lifetime fence now rejects stale completion and clears the prior product while loading. The new component regression run against retained pre-edit source shows the newer heading replaced by the older heading; the repaired implementation keeps the new product.

Backend regression coverage executes the real service and SQL overlay through cold and warm cache boundaries. Forged candidate names, narrative, price and stock are replaced by current SQL values; hidden products and wrong tenants fail closed. This tests behavior, rather than renaming the old source assertion.

## 3. Variant resolution and HTTP cache behavior

### Resolution trace and authority

`useVariantSelector` holds only option candidates. It submits `{ selectedOptions }` to `/api/storefront/products/by-id/{id}/variant-resolution`; the API wrapper now explicitly strips unrelated runtime request properties. The backend re-enters `GetProductByIdAsync`, normalizes option keys/values, and admits a unique active exact combination. Empty options ask the backend for its default. Partial, unknown or ambiguous combinations cannot be resolved locally.

The backend resolves product/variant identity, pricing, stock and availability from current SQL-owned detail evidence. An unavailable variant may be returned as an identified variant with purchase flags **false**; that is not an authorization to purchase. Inactive, foreign-product/tenant and ambiguous matches are suppressed. Tests cover exact/default/invalid/partial/inactive/unavailable/ambiguous/foreign-tenant/foreign-product scenarios.

The frontend previously accepted `response.variant` even when `productId`, `resolvedVariantId` or required commercial fields were foreign/incomplete. It also checked abort only in rejection/finalization, not successful completion, allowing an old resolved promise to replace a newer selection. New response admission binds product/variant identity, validates pricing/availability/active shape and checks returned attributes against submitted candidates. The successful-completion abort fence and clearing of previous selection prevent stale responses from becoming authority. No local variant matching or price/stock calculation was introduced.

### Original no-store assertion was obsolete

Commit **d68b0b5** (2026-08-05) removed the controller-local `ApplyNoStoreHeaders()` invocation. The current contract is owned by `StorefrontHttpCachePolicyMiddleware`, registered once before tenant/routing/authorization failure boundaries. It applies to `/api/storefront` and reasserts headers in `OnStarting`, stripping ETag and Last-Modified. The domain runtime constitution documents this central ownership. See `cache-history.diff` and `docs/architecture/DOMAIN-1.6_STOREFRONT_CUSTOM_DOMAINS_RUNTIME_CONSTITUTION.md`.

No cache middleware/controller behavior was changed, and no unrelated endpoint received new no-store treatment. The replacement test uses real MVC serialization/model binding and real HTTP through Kestrel, including success, invalid JSON shape, missing product/tenant, unresolved variant and controlled service outage; an unrelated cacheable route remains cacheable. It asserts parsed HTTP semantics, not a private helper name or directive serialization order.

### Actual hosted headers and status evidence

`hosted-final.json` records **24 actual API responses** against the running Debug API, not mocks:

| Response group | Observed status |
|---|---|
| Product discovery; three slug/by-ID detail pairs; valid variant resolutions; forged-extra-field probes; conditional detail read | **14 × 200** |
| Invalid selected-options shape | **3 × 400** |
| Spoofed tenant header on existing tenant host | **1 × 403 `TENANT_FORBIDDEN`** |
| Missing slug/by-ID product | **2 × 404 `PRODUCT_NOT_FOUND`** |
| Unknown option combination and missing-product resolution | **4 × 409 `CONFLICT`** |

Every captured response has:

```http
Cache-Control: private, no-store, no-cache, max-age=0, must-revalidate
Pragma: no-cache
Expires: 0
Surrogate-Control: no-store
```

**ETag and Last-Modified are absent in all 24 responses.** A stale `If-None-Match` does not produce a 304; the detail read returns 200. Extra forged price/availability/variant-ID fields are ignored in all three actual resolution probes: identity, pricing and availability remain equal to the canonical response.

The initial browser fetch harness omitted the existing CSRF token and received 403 `CSRF_VALIDATION_FAILED`. Those are not counted as product/variant service rejection tests. The corrected same-origin requests include the existing token without recording it. The actual UI's HTTP client already handles it.

### Customer-visible browser checks

`browser-product-closeout.json`: **11 passed / 0 failed**, no page exceptions or attempted business mutation.

- Real hosted product name/category/price and both A/B option selections; outgoing payload contains only candidates, and backend response controls displayed price/stock.
- Controlled foreign variant response is rejected, cannot display the forged price or enable purchase; a subsequent real backend read recovers.
- Controlled missing-pricing and wrong-slug detail responses show Product not found with no product/purchase context.
- Actual missing hosted product does not revive the prior product.

These controlled rejection cases are explicitly intercepted HTTP responses, not claims that the backend emits malformed DTOs. The tests do not place an order, mutate stock/cart, or establish every live tenant/product combination.

## 4. Seven Admin failures: diagnosis and reproduction

### Retained evidence and timeout mechanism

The prior serial run retained six Products feed workflow failures at **5,019.77–5,500.59 ms**, plus the synchronous English Header announcement case at **5,751.78 ms**. All carry a suite-collected `Error: STACK_TRACE_ERROR`, not an application assertion message. Exact names/durations/stacks are copied to `retained-admin-failures.json`.

Vitest configuration: **forks**, **isolate=true**, **testTimeout=5,000 ms**, hookTimeout=10,000 ms. React Testing Library registers normal cleanup with the existing global hook setup. No fake timers occur in either affected file. File processes are isolated; no reproducible cross-file mutable application state was found.

The installed Vitest runner explains the misleading stack: `makeTimeoutError` creates a real timeout message but assigns the collection-time `STACK_TRACE_ERROR` stack. The JSON reporter preserves the stack as `failureMessages`, hiding the useful message. `withTimeout` also rejects a **synchronous** test after it returns if elapsed wall time exceeded the deadline, explaining why the Header case can exhibit the same signature without an asynchronous hang. See `PointOfSale.Web/node_modules/@vitest/runner/dist/chunk-artifact.js`, lines 2261–2319 and 2404–2410. A diagnostic reporter now retains both message and stack, resolved configuration and per-case durations. No installed package was edited.

A separate, intentionally failing **60 ms synchronous diagnostic with a 30 ms per-test deadline** confirms the mechanism and reporter: ordinary JSON records `STACK_TRACE_ERROR`, while `timeout-probe-final-diagnostics.json` retains **`Test timed out in 30ms`** plus that same stack. This is an artificial bounded runner probe, not reproduction of the original application's process contention. Its file/config live outside maintained `src/**` discovery, and it contributes **zero cases** to either full application-suite count. The first probe attempt found no test outside the configured root; the corrected isolated root runs the single diagnostic.

### Controlled comparison and fixture changes

- Isolated unchanged affected files: **134 passed / 0 failed / 0 skipped**.
- Otherwise unchanged serial full suite, with no concurrent build: **1,755 passed / 0 failed / 0 skipped**, **225 files**. All seven previously failing cases pass. The feed isolation case takes **4,914.65 ms** and rotate takes **4,677.55 ms**, close to the existing deadline. English Header takes **46.09 ms**; Arabic takes **6.09 ms**.
- Products feed tests repeatedly queried the entire document while a portaled modal makes the catalog aria-hidden. Queries now use `within(dialog)` for the six affected long workflows. This reduces unnecessary accessible-tree traversal without bypassing visibility, pointer checks or assertions.
- Tests stubbed global `navigator` but did not restore it between feed tests. Added suite-local `vi.unstubAllGlobals()` cleanup. This is genuine fixture hygiene, **not** claimed as proven historical root cause.
- No Admin application behavior, global worker setting, timeout, retry policy or test exclusion was changed. The Header test/application remains unchanged.

All three post-repair serial repetitions pass **134/134** across both affected files. Measured durations of the seven historical failures follow (rounded milliseconds; runs are not additive):

| Previously failing case | Unchanged isolated | Unchanged broad | Repaired isolated range, three runs | Final repaired broad |
|---|---:|---:|---:|---:|
| Retains created URL after dialog reopen | 2,692 | 2,666 | 733–810 | 691 |
| Direct close without Done | 2,316 | 3,639 | 717–801 | 675 |
| Rotate replaces old URL | 1,471 | 4,678 | 535–561 | 512 |
| Retained URLs isolated by channel | 1,224 | 4,915 | 518–542 | 473 |
| Revoke removes Copy/Open | 1,290 | 2,329 | 499–561 | 503 |
| Unmount clears retained URLs | 1,501 | 2,429 | 611–683 | 614 |
| English Header announcement | 291 | 46 | 45–46 | 53 |

See `admin-affected-timings.json` for exact names and unrounded values. These measurements demonstrate additional deadline headroom in the repaired feed fixtures; they do not establish the cause of every historical scheduling delay. The six extra baseline cases relative to the old 1,749-case broad run are the prior closeout's pending-payment page fixture; this phase adds six product component cases, not a replacement for the original seven tests.

Both post-repair broad executions pass **1,761/1,761** across **226 files**, with no skips, retries or raised deadlines. The final current-source run is **326.23 seconds** wall time; it includes the test-only array-index compatibility correction and all earlier closeout fixtures. The agent launched no concurrent build/test workload during either stability run; the later API process change is recorded in section 1. The seven original cases are present and passing; focused success is not substituted for broad results.

**Diagnosis:** the retained signature/durations identify wall-clock deadline sensitivity; no deterministic seven-case product defect or shared-state contamination was reproduced. Prior runs overlapped substantial builds/tests, so process/resource contention is plausible, but no historical scheduler/CPU trace exists to prove a specific competing process. A passing rerun does not prove indefinite stability under arbitrary load. Repeated and final broad outcomes below are the measured boundary, not a dismissal as “flaky.”

## 5. Exact results

| Run | Passed / failed / skipped | Evidence |
|---|---|---|
| Response-admission regression against old API | **4 / 19 / 0** | `product-admission-red.json`; demonstrated missing admission, not production HTTP failures |
| Route/selection regressions against retained old source | **4 / 2 / 0** | `product-races-baseline.json`; retained-source loader never replaces working files |
| Product API regressions after repair | **25 / 0 / 0** | `product-admission-green.json` |
| Product page/hook component regressions | **6 / 0 / 0** | `product-components-verified.json`, after test-target compatibility correction |
| Entire Storefront suite | **523 / 0 / 0**, 59 files | `storefront-final.json` |
| Expanded related backend/cache selection | **85 / 3 / 0**, 88 total | `residual-product-verified.trx`; three pre-existing cache source assertions retained below |
| Two requested backend architecture tests | **2 / 0 / 0** | `residual-two-exact.trx`, separate exact selection |
| Final related backend selection | **78 / 0 / 0** | `residual-related-final.trx`; does not silently replace the expanded red run |
| Affected Admin files, repeated | **134 / 0 / 0** in **each** of three runs | `admin-repeat-{1,2,3}.json`, unchanged 5-second deadline |
| Runner-only timeout diagnostic, deliberately red | **0 / 1 / 0** | `timeout-probe-final.json` and `timeout-probe-final-diagnostics.json`; not an application-suite failure |
| Broad Admin suite before test-only compatibility correction | **1,761 / 0 / 0**, 226 files | `admin-broad-final.json`, full suite, not focused substitution |
| Final current-source broad Admin suite | **1,761 / 0 / 0**, 226 files | `admin-broad-verified.json`, full suite after correction |
| Storefront build/typecheck | **Pass** | `storefront-build.txt`, build invokes `tsc` before Vite |
| Admin build/typecheck | **Pass / Pass** | `admin-build-final.txt`, `admin-typecheck-verified.txt`; build excludes tests, standalone typecheck includes them |

Three **unrelated existing cache architecture assertions** in the deliberately expanded selection still fail; this phase does not rewrite or exclude them from that reported run:

1. `CacheCoreAndCleanupArchitectureTests.LiveStorefrontRedisFamiliesHaveOneExplicitOwnerAndGenerationFence`: missing source literal `GetOrCreateOwnedAsync`.
2. `RequiredInvalidationAndOwnedPopulationStayFailClosedAndRaceFenced`: missing `Condition.StringEqual`.
3. `HttpCachePolicyIsRegisteredOnceAndControllersCannotOverrideIt`: another Storefront controller source contains `Response.Headers.CacheControl`.

Their source files were not changed by this phase. Actual product HTTP tests prove the scoped product contract, not every unrelated cache family's correctness. No unrestricted backend/database-provisioning suite was run.

Early new test-harness attempts required controlled `ITenantContext` injection, semantic header comparison because HttpClient reorders directives, JSON dictionary casing, accounting for the selector's initial reset effect, query-string-aware browser URL matching and use of the existing translated error key. The first final Admin typecheck caught `.at()` in the new test under the project's older library target; replaced it with ordinary array indexing, retaining the assertion without changing compiler settings. The fresh component run and standalone typecheck pass; the failing typecheck is retained in `admin-typecheck-final.txt`. Intermediate red runs are retained; they are not misreported as fixed backend production defects.

## 6. Commands and reproducibility

From repository root, build tests serially before `--no-build`:

```powershell
dotnet build PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore -v minimal -p:WarningLevel=0 -nodeReuse:false
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~ProductDetailFieldsMustBeBackendComposedAndFrontendPresentationOnly|FullyQualifiedName~ProductVariantResolutionMustBeginAtBackendAuthorityAndFrontendSubmitsOptionCandidatesOnly' --logger 'trx;LogFileName=residual-two-exact.trx' -v quiet
$relatedFilter = 'FullyQualifiedName~StorefrontCatalogPresentationAuthorityCertificationTests|FullyQualifiedName~ProductSearchSqlAuthorityBatchTests|FullyQualifiedName~ResidualProductHttpContractTests|FullyQualifiedName~StorefrontProductIdentityMappingTests|FullyQualifiedName~SimpleProductStorefrontPricingTests|FullyQualifiedName~ProductInventorySqlAuthorityFallbackTests'
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-build --filter $relatedFilter --logger 'trx;LogFileName=residual-related-final.trx' -v quiet
```

The expanded **85/3/0** run used the same `$relatedFilter` with `|FullyQualifiedName~CacheCoreAndCleanupArchitectureTests` appended and `residual-product-verified.trx` as the logger output. The final related subset is explicitly narrower; it does not reclassify those three failures or imply the expanded selection is green. TRX files are under `PointOfSale.Tests/TestResults/`.

From `PointOfSale.Storefront/`:

```powershell
npm run build
npx vitest run --maxWorkers=2 --reporter=json --outputFile=../.ai/evidence/residual-invariants-20261015/storefront-final.json
```

From `PointOfSale.Web/`, run performance comparisons without concurrent builds/test suites:

```powershell
npx vitest run src/pages/Products.test.tsx src/modules/storefront/cms/components/HeaderAnnouncementSettings.test.tsx --maxWorkers=1
npx vitest run --maxWorkers=1 --reporter=verbose --reporter=json --outputFile.json=../.ai/evidence/residual-invariants-20261015/admin-broad-verified.json
npx tsc --noEmit
npm run build
```

`admin-diagnostic-reporter.mjs` can be added with `--reporter=../.ai/evidence/residual-invariants-20261015/admin-diagnostic-reporter.mjs` and an absolute `PHASE4_ADMIN_DIAGNOSTICS` output path to retain error messages as well as stacks. Exact commands/environment and exit codes are retained in the phase logs; JSON and TRX are the count sources.

Browser scripts: `tests/browser/residual-invariants-hosted.js` and `residual-product-browser.js`. Reuse the registered `opencode-ui-test` after `playwright-cli list`; never parallelize shared-session commands. The product fixture intentionally uses the already-existing certification product and never mutates it.

## 7. Files changed and remaining limits

Production frontend changes only:

- `PointOfSale.Storefront/src/services/api/productAuthority.ts` — new fail-closed contract admission; `storefrontApi.ts` — enforce it and send option candidates only.
- `src/pages/ProductDetailsPage.tsx` — active-request fencing and prior-product clearing.
- `src/hooks/useVariantSelector.ts` — clear previous resolution, ignore aborted successful completions.
- `src/storefront/runtime/containers/RuntimeSectionContainer.tsx` — use existing translated invalid-combination message.

Tests/diagnostics:

- `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs` — replace the two obsolete implementation details with executable checks; other invariants retained.
- `Infrastructure/ProductSearchSqlAuthorityBatchTests.cs` — partial declaration only; new `.Residual.cs` reuses its fixture for current-SQL product/variant behavior.
- New `Storefront/ResidualProductHttpContractTests.cs` — actual MVC/Kestrel canonical cache semantics and controlled backend DTO forwarding.
- Storefront `services/api/productAuthority.test.ts` and updated complete `storefrontProductDetailApi.test.ts` fixtures.
- Web `src/tests/storefrontProductAuthority.residual.test.tsx`, typed `src/tests/fixtures/storefrontProductAuthority.fixture.{js,d.ts}`.
- `PointOfSale.Web/src/pages/Products.test.tsx` — dialog-scoped queries and navigator-stub cleanup only. Vitest's maintained config and Header test remain unchanged.
- Browser scripts, scoped diagnostics/evidence and this report.

No backend production source, schema, auth grant, cache policy, merchant setting, external-provider state or installed dependency was changed. No shared cart/order/payment/inventory/financial operation was submitted. Pre-existing work was neither discarded nor staged. Existing degraded certificate/Caddy readiness, global lint debt and unverified production/payment/shipping/messaging workflows are carried forward, not reopened here.
