# Affiliate Phase 2 — Product discovery and promotion links

Date: 2026-10-21. Primary Windows workspace: `C:\Users\fadya\source\repos\PointOfSale`.

## A. Executive verdict

**PHASE 2 FUNCTIONAL PASS, with two pre-existing source-text test failures.** Cross-merchant discovery, durable promotion creation, My Links and the exercised negative/security scenarios passed headed-browser verification. Final quality gates and readback are complete; backend suite is not fully green (402/404), as detailed in E.

The existing Memoxp identity discovered and promoted Samsung Galaxy S26 Ultra from **Shopixy** and Dell Latitude 7420 Laptop from **Greatone**, without another merchant-specific affiliate account. Each actual Promote action returned `201`; repeat actions returned `200` with the same persisted link ID/code. The global profile code remained `MEMOXP155504`.

AhramStore has no enabled affiliate product policies in the observed catalog and correctly returned an empty list. Seven displayed main categories also legitimately have no eligible products; they were not populated artificially.

No order attribution, commission recognition, balance, settlement or payout implementation was changed. No order or financial transaction was submitted. Unrelated working-tree changes and Phase 1 work remain unstaged and uncommitted.

Evidence root: `.ai/evidence/affiliate-phase2-20261021/`. All links in the tables below are test-created public promotion metadata; codes are abbreviated here. Credentials/raw browser inputs are outside the repository in `%LOCALAPPDATA%\Temp\opencode\affiliate-phase2-20261021`.

## B. Source of truth and domain relationships

### Actual execution path

- Portal shell: `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx`, URL `/affiliate/portal?tab=discover`; My Links uses `tab=links`.
- Discovery UI: `DiscoverTabEnhanced.jsx` → `affiliateApi.discoverProductsIntelligence` → `AffiliatePortalController.DiscoverProducts` (`GET /api/affiliate/discover/products`). The active tab does **not** use the legacy Elasticsearch discovery service.
- Categories: `GET /api/affiliate/discover/categories`; authenticated detail: `GET /api/affiliate/discover/products/{productId}`.
- Detail/Promote: `ProductCard.jsx` → `ProductDrawer.jsx` → `affiliateApi.createPromotionLink(tenantId, productId)` → `AffiliatePromotionLinksController.Create` → PostgreSQL `AffiliatePromotionLinks`.
- My Links: `LinksTab.jsx` → `GET /api/affiliate/promotion-links`; owned detail/deactivation use `/{id}`.
- PostgreSQL `PosEcommerceDb_Baseline` at the application's configured development server is authoritative. No replacement database, schema migration or tester-machine access was used.
- `Products.TenantId`, active tenant lifecycle, product lifecycle, `AffiliateProductPolicies`, global affiliate taxonomy, ProductUnits, current-semantic ProductVariants, ProductVariantStocks and active same-tenant warehouses establish eligibility and current catalog data. `ProductSearchSqlAuthorityOverlay` recomposes pricing, inventory and media from SQL. Mongo intelligence is derivative hydration/ranking data, not ownership, eligibility or page-total authority. Elasticsearch remains the existing legacy search path, not this portal's current request path.

### Global affiliate authority reused from Phase 1

Primary headed login: `fadyadel2060@gmail.com` / Memoxp.

| Authority | Value |
|---|---|
| Affiliate credential/user | `01a03e11-e2b3-7875-9b59-6e25f59f03d1` |
| Global affiliate profile | `01a03e11-e234-7d40-b77e-99f9463c7a40` |
| Global profile referral code | `MEMOXP155504` — unchanged |
| Admission | Verified, Active; dedicated `AffiliateJwt` / `AffiliateOnly`, current SQL identity/session validation |

The authenticated user ID and profile claim must agree with the persisted credential/profile and profile owner. Client-supplied affiliate IDs and referral codes are not creation DTO fields and cannot override ownership. Public/eligible product access does not authorize private merchant APIs. Existing POS and storefront sessions were not used as affiliate authority; isolated private-route tests excluded POS cookies.

### Participation and eligibility — existing rules, not a new policy

1. A merchant administrator opts a product into the program by enabling its `AffiliateProductPolicy` and assigning a live supported global affiliate category. Entry/re-entry is gated by the existing readiness service (live storefront, valid enabled payment method and active shipping method); it is not a new per-affiliate merchant enrollment requirement.
2. Current product admission requires a live, active/nondeleted merchant; active, published, online-visible and searchable/nondeleted product; enabled/nondeleted same-merchant product policy; live main category or supported child with a live main parent.
3. Current SQL storefront pricing must be positive and orderable under the existing track-stock/backorder policy. Available stock is derived from current semantic variant owners and same-merchant active warehouse inventory, minus reservations. A priced but unavailable variant cannot combine with a different unpriced stocked variant to bypass admission.
4. Existing commission metadata is displayed, not financially recognized. Policy domain/database bounds remain unchanged: rate `0.0001–0.50`, optional positive cap. No rates, caps or policies were invented or edited.
5. Low-stock readback uses the existing `AffiliateProductIntelligence:LowStockThreshold` configuration/default `10`, only for stock-constrained products. No new stock rule was introduced.
6. Current admission is checked again on Promote and link readback; a stale visible card does not establish eligibility. Existing links for removed/ineligible products are hidden/return `404`, not deleted or reassigned.

`storeTenantId` is a discovery/count **filter**, not a tenant authorization source. It is matched against SQL-admitted product owners. Create requires the existing `(TenantId, ProductId)` DTO and resolves that pair from SQL; a mismatched pair returns `404`. Forged tenant headers cannot change product ownership. The existing required body tenant field and route formats were preserved.

### Promotion link contract

- `AffiliatePromotionLink.AffiliateUserId` references the authenticated global credential. `AffiliateAccountId` is its canonical global profile bridge, not a new merchant-specific profile.
- `ProductId` references the real product; link `TenantId` is that product's validated merchant.
- One nondeleted logical link per `(AffiliateUserId, TenantId, ProductId)`, enforced by the existing PostgreSQL partial unique index `UQ_AffiliatePromotionLinks_User_Store_Product`. Repeated creation returns the existing record; an inactive owned link can be reactivated using the same code/ID when still eligible.
- The product link referral code is distinct from the profile code. Existing generation produces uppercase alphanumeric ten-character codes, checks both link and profile code namespaces, and relies on `UQ_AffiliatePromotionLinks_ReferralCode` for atomic uniqueness. No separate code-reservation record is created.
- Destination authority is `StorefrontUrlResolver`: tenant custom origin first, otherwise configured `{slug}` template. Existing supported format remains `/products/{slug-or-product-id}?ref={product-link-code}`. Production code contains no task merchant/product/localhost constants.
- Responses expose link/public product/store presentation data, not credentials or private merchant order/customer/payment data.

## C. Confirmed defects and fixes

| Files | Root cause and correction |
|---|---|
| `PointOfSale.API/Controllers/AffiliatePortalController.cs` | List already had SQL-first identity admission and missing-Mongo fallback, but detail required a Mongo document and selected its owner there. Detail now resolves SQL product owner and recomposes even without that projection. Both paths replace stale variant prices/stock and low-stock flags from current SQL. Baseline Nokia detail had variant stock totaling **7**, while authoritative/current total was **22**. Simple products no longer expose a projection-only automatic variant as a selectable product variant. Member reads are no-store; newest member ordering uses product creation authority. Optional merchant-filtered category counts match selected catalog context. |
| `PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs` | Creation could proceed with a nullable/unverified bridge and narrower eligibility than discovery; it did not fully enforce current searchable/product/category/merchant/price/stock/URL authority. It now validates canonical credential/profile ownership and current SQL catalog admission before persistence/readback. Concurrent duplicate inserts previously surfaced an unhandled unique error. Known link/code unique conflicts detach the failed insert, read the logical winner or retry a code, without reservations/orphans. Unrelated storage failures are not swallowed. Product/store names are returned for authoritative My Links display. Creation response timestamps are read back after persistence stamping. |
| `PointOfSale.Web/src/modules/affiliate/pages/portal/ProductDrawer.jsx` | Opening details automatically POSTed a link; failures were console-only, with no clear retry. Detail/link responses could overwrite a newly selected product or erase a newly created link. Browsing is now read-only; explicit localized Promote has pending/error/retry states and immediate duplicate-click guard. Existing owned links are restored by readback. Stale responses are discarded. Stock reads the actual `totalAvailableStock` DTO. Copy/reopen uses only backend-issued URLs. |
| `.../portal/DiscoverTabEnhanced.jsx`, `src/services/affiliateApi.js`, `affiliateApi.d.ts` | The UI ignored supported `storeTenantId`, and category counts had no corresponding merchant filter. URL merchant context is now passed to products/categories, pagination and selected detail reset when context changes, and category failures have a distinct retryable state. Existing product loading/empty/error/retry and stale-fetch guards are retained. |
| `.../pages/AffiliatePortal.jsx` | Local active-tab state could diverge from URL history. Active tab now derives from the URL, so browser Back restores discovery rather than leaving My Links rendered. |
| `.../portal/LinksTab.jsx` | API failures were misleading “Coming Soon” states, rows showed truncated product IDs instead of product/store names, and action failures were silent. It now offers actual retry, uses backend presentation metadata/destination, keeps genuine empty/session states separate, reports failed actions, and guards async readback after context teardown. |
| `.../portal/ProductCard.jsx`, `ProductDrawer.jsx`, `.../lib/affiliateProductMedia.js` | Store-relative Dell media was fetched from Portal/POS origin and returned `404`. Rendering resolves root-relative media against the **backend-issued product merchant URL**, without weakening media tenant authorization. External fixture images and missing media remain unchanged. A nonexistent `/img/placeholder.png` error loop was replaced by the established Package/icon container fallback. Price presentation no longer silently rounds away configured fractional amounts. |
| `PointOfSale.Web/vite.config.ts` | Headed verification exposed a second development-only image blocker: both CSP header and HTML meta disallowed HTTP local merchant images. Vite **serve only** adds `http://*.localhost:5174` to `img-src`; built HTML and preview keep existing remote HTTPS-only image policy. No production-wide HTTP allowance, media authorization bypass or hard-coded merchant identity was added. |
| `.../i18n/translations.js` | Added English/Arabic Promote, generation, retry/error and reopen labels; product-link display is LTR inside RTL layout. No catalog redesign or replacement imagery. |
| `PointOfSale.Tests/Affiliate/AffiliatePromotionWorkflowTests.cs` | Executable creation/readback/eligibility/ownership/duplicate/failure/metadata/SQL-detail regressions, plus known-constraint classification and configured stock-flag coverage. Existing source-contract tests were not rewritten to manufacture success. |
| `.../portal/PromotionWorkflow.test.tsx`, `.../lib/affiliateProductMedia.test.ts`, component declarations | Explicit/read-only browsing, rapid clicks, creation retry, stale detail/link responses, restored links, merchant filtering, distinct empty/error/category states and merchant media resolution. Existing hook-order regression only gained the read API mock needed by read-only restoration. |

**Intentional QA data:** the Shopixy products have no current SQL ProductMedia images; the Greatone media is an existing uploaded test image. Missing, screenshot-like and empty-category fixtures were not replaced or called policy defects. The defect was the request origin/fallback behavior, not the image's contents. Fictional marketing counters were untouched.

## D. Headed browser and persisted results

Reused registered headed Edge session `opencode-ui-test`. API `:5010`, Vite Portal `:5173`, configured storefront destinations `:5174`. Actual browser forms, network requests and configured PostgreSQL were used, not mocked successful product/link responses. Controlled error tests abort **one network request**, then retry against the real service.

### Products and creation

| Product | Merchant | Actual result | Persisted new link |
|---|---|---|---|
| Samsung Galaxy S26 Ultra, `606a46bc-24b5-76af-834d-34933906d5f2` | Shopixy, `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | UI Promote `201`, repeat `200` same link; price `770 EGP`, existing rate `20%` | `01a15f25-4119-7d7e-a868-e1e38d8e3c75`, code `K8YU…CRWF` |
| Dell Latitude 7420 Laptop, `8cff905d-5513-3c64-3698-e3fededc8287` | Greatone, `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | UI Promote `201`, repeat `200` same link; price `2200 EGP`, existing rate `10%` | `01a15f25-746b-72c1-b20d-c29516b4e3c4`, code `2T9L…8E2I` |
| Nokia N73, `580b30bd-1829-131c-2cc4-5f6cf3994e09` | Shopixy | Four concurrent requests under retained Phase 1 QA identity: one `201`, three `200`, one link ID | `01a15f28-886f-7abf-ba0f-2f4cf4f57bf4`; separate QA owner |

Both primary links have Memoxp user/profile IDs from B and exact SQL product-owner tenant IDs. Product codes differ from the unchanged `MEMOXP155504` profile code. The existing Memoxp Nokia link `01a03e6b-3ff1-7c1b-9cf2-a340244c0f25` and C57 link `01a05795-8024-735e-9146-51ac931d5ab4` were neither repurposed nor deactivated.

Primary evidence: `existing-login-final.json`, `discovery-ui-results.json`, `promotion-ui-results.json`, `links-after-promote.json`, `links-final.json`, `concurrency-ui-results.json`, `security-ui-results.json`, `nonactive-results.json`, `copy-history-ui-results.json`, `responsive-ui-final.json`.

### Exercised scenarios

- Default global discovery: three products. Shopixy context: two; Greatone: one; AhramStore: zero. Corresponding category counts agree with the selected merchant. Profile identity remains the same in each context.
- Search by supported product name, Electronics category, genuine empty Fashion category, in-stock control, price sort, invalid-sort rejection, search with no match and API pagination (page size one, including beyond-last page). Only three catalog products exist, so the normal twenty-item UI correctly has no pagination controls.
- Product/list/detail title, merchant, price, stock and category were compared with narrow SQL/catalog evidence. Existing missing-image data remained eligible under policy.
- Actual Promote, persisted ownership/code/URL, repeat idempotency, My Links, refresh, clipboard copy and reopen were exercised. Clipboard equality and history assertions were repeated after awaiting the relevant browser state, rather than interpreting an in-flight animation/promise as a failure.
- Desktop `1440×1000`, mobile `390×844`, English and Arabic/RTL action usability, LTR URL display, existing image containers and horizontal category scrolling were checked.
- Creation failure: one browser-side POST abort displayed an error without a new record; actual retry produced the intended `201`. Product/list/category failures are distinct from legitimate empty results.

### Security/negative results

| Scenario | Actual outcome |
|---|---|
| Foreign affiliate user/profile query on My Links | `200` with **only authenticated owner's links**; unsupported client identity hints cannot replace the principal. |
| Get or deactivate C57-owned link under Memoxp | `404` for both; original record unchanged. |
| Dell product with Shopixy body tenant | `404`; no new record. |
| Foreign affiliate/profile IDs, C57 referral code and forged merchant header supplied on repeat creation | `200` returning **Memoxp's existing Samsung link**, never foreign owner/code/tenant. These extra DTO fields/headers have no authority; no duplicate was created. |
| Nonparticipating AhramStore product | `400`; no link. |
| Pending, Suspended, Closed and unverified QA principal with prior session | All promotion requests `401`; every checkpoint's complete link fingerprints unchanged. Exact task-owned state restored in `finally`; existing real identity was not mutated. |
| Affiliate-only context versus private Admin affiliates/orders/customers/manual payments/payment proof routes | Context first proved profile `200`; each private route returned `401`, no private data. |
| Logout | Protected My Links GET, promotion POST and profile GET all returned `401` after actual UI logout (`final-ui-complete.json`). |

The four-state live tests used only the pre-existing task-owned Phase 1 synthetic identity and guarded state fixtures, not a merchant admin bypass or a new supported Closed→Active lifecycle operation. Actual inactive/removed policy-product variants were not manufactured in the live catalog; focused executable tests cover those changes and split priced/stocked-variant admission.

Screenshots include `empty-category.png`, `promote-retry-error.png`, `promoted-0.png`, `promoted-1.png`, `my-links-desktop.png`, `mobile-promotion-english.png`, `mobile-promotion-arabic.png`, `mobile-links-arabic.png`. Final loaded-destination/media/readback screenshots and outcomes are listed in E.

## E. Automated quality results

**Final selected results (not a claim of full-repository certification):**

| Gate | Final result / evidence |
|---|---|
| Backend selected regression suite | **402 passed / 404 executed; 2 failed**, no skipped/aborted results in TRX; `phase2-verified.trx`, `backend-verified.log`. |
| New backend workflow suite within that run | **29/29 passed**, including configured stock threshold and split-variant eligibility. |
| Frontend affiliate/API selection | **79/79 passed**; `frontend-verified.json/log`, one worker, finite 20-second per-test limit. No unrelated assertion was changed. |
| Production frontend build after final media/CSP changes | **Passed**, `frontend-build-complete.log`. |
| Typecheck after final changes | **Passed**, `typecheck-complete-final.log`. |
| Focused lint | **Passed**, `lint-verified.log` and `lint-config-final.log`; existing ESLint configuration deprecation warning only. |
| Whitespace check | **Passed**, `diff-check-final.log`. |

The two remaining backend failures are source-text assertions, not silently excluded tests:

1. `AffiliateFrontendSessionCache_MustRemainProfileCacheOnly` expects an old literal redirect; original HEAD already used the supported `returnUrl` redirect. That behavior is preserved.
2. `AffiliateDiscovery_ReadAdmission_RequiresActiveCategoryAuthority` expects `AffiliateCategories` directly in legacy `AffiliateProductDiscoveryService.cs`. That file is **identical to original HEAD**, which also lacks the literal (current authority is composed through existing helpers). The test stops at this pre-existing assertion. No legacy service or test was modified to manufacture success.

`quality-summary.json` records both original-source proofs, exact test counts and final preservation comparison. Current runtime category/product lifecycle coverage is provided by the passing executable selected suites and live empty/participation checks; the source-text failures remain visible limitations.

Historical attempts, not final gate results:

- Executable workflow tests initially passed **28/28** before additional configured-threshold and split-variant regressions were added.
- A selected frontend run passed **74/74**. A later heavily concurrent run had eight failures (six default five-second test timeouts, one one-second public-page wait and one new category locator/wait); final reruns retain the failures as historical evidence and do not rewrite unrelated assertions.
- Source-string failure details are recorded above; neither test was rewritten or removed.
- Initial compilation of the new fixture required explicit ID assignment before dependent factories, and qualifying the `Unit` entity against the test namespace. These were test-fixture corrections, not changes to domain validation.
- An initial type declaration omitted the already supported null product ID; its original contract was restored. No unrelated test was deleted or excluded to make a result green.
- Some builds/typechecks exceeded initial finite time budgets under concurrent compilation. Final commands use longer finite limits and bounded frontend workers. Aborted/timed-out attempts are not counted as passes.
- `backend-final-complete.log` was blocked copying a DLL held by the earlier interrupted task-owned testhost (`MSB3027/MSB3021`), so it produced no final TRX. Once that process exited, the nonoverlapping `phase2-verified.trx` run compiled and executed all 404 selected tests.

### Final headed verification and image/destination audit

- `final-relogin-verified.json`: fresh login using the supplied existing Memoxp credentials; no credential changes.
- `final-ui-complete.json`: latest backend repeated Promote `200` with identical Samsung/Dell IDs/codes, new-session My Links returning both plus the preserved Nokia link, real transport-failure/retry for My Links and categories, unchanged global profile code, and three post-logout `401` checks.
- `media-destinations-final.json`: Dell card and drawer loaded the existing screenshot-like PNG from **Greatone**, actual response `200 image/png`, decoded dimensions **1590×917**. Intentional source image contents were retained.
- Both storefront pages finished rendering the expected product: actual product IDs match the originating merchant/product, actual product endpoints returned `200`, destination URL retained its own product-link code. Dell media also returned `200` on its storefront. Samsung has no SQL media and correctly uses the existing missing-image presentation.
- Loaded evidence: `final-dell-media.png`, `final-loaded-storefront-0.png`, `final-loaded-storefront-1.png`, `final-my-links.png`, and `final-stable-catalog.png`. The latter was captured after card animation settled with **three cards and no drawer**. Earlier `discovery-desktop.png` / `final-discovery-desktop.png` caught animation/exit transitions and are not the stable catalog proof. Earlier `storefront-destination-*.png` only captured bootstrap and are superseded by the loaded screenshots.
- `responsive-latest.json` repeats the English/Arabic `390×844` checks after the media fix; no horizontal document overflow, RTL dialog and LTR promotion URL verified.
- Earlier final-UI script attempts exposed test-harness issues: StrictMode's second GET could mask a one-request abort, and exact text matching on a category alert also included its Retry child. The final script holds transport failure until the error is visible, removes the route before real retry and selects the actual alert. No successful response was mocked. Windows Node DNS cannot resolve the development wildcard hostname, while Edge can; media status proof comes from actual browser responses, not a substituted host/header request.

## F. Data and workspace preservation

Baseline was captured before source or link changes in `baseline.sql/json`, `catalog.sql/json`, `preservation-before.json`, `financial-before.json` and `working-tree-before.txt`. Live SQL evidence connections use read-only transactions; only exact task-owned affiliate state fixtures use guarded updates.

Final preservation: **27/27 baseline fingerprints match**, covering all four existing profiles/credentials (excluding expected credential-session fields), the two existing promotion links, participating products/policies, commission rows and order summary. No profile referral code was changed. Evidence: `preservation-final.json`, `links-final-verified.json`, `quality-summary.json`.

`financial-before.json` and `financial-final.json` are identical, including content hashes, not just counts:

| Data | Count, unchanged |
|---|---:|
| Orders | 60 |
| Affiliate commission ledger | 10 |
| Affiliate balance ledger | 0 |
| Payout requests | 0 |
| Accounting journals | 220 |
| Accounting ledger lines | 459 |
| Accounting outbox | 30 |

Exactly three task-created links were added: two intended Memoxp links from real Promote actions and one QA concurrency link. They are retained, owned and readable, not deleted to conceal test activity. No orders/conversions/commissions/payouts or financial postings were created. Login/session audits and existing referral-entry behavior were not erased.

No staging/commit, database reset/replacement, broad restoration, Git reset/clean/stash/discard, tester-machine access, fixture-image replacement, marketing-statistics remediation or later financial-phase implementation occurred. Existing unrelated payment/accounting/navigation changes were left intact.

## G. Remaining limitations and stop boundary

- AhramStore and seven displayed empty categories have no eligible products in the observed catalog; their empty state is valid.
- Name search is the implemented member-discovery search contract; identifier/SKU search was not invented. All discovery results are already orderable under stock policy, so the retained in-stock parameter/control cannot broaden eligibility.
- There is no My Links pagination/search contract in this current UI/API; its owned list is returned as a list. No new analytics/reporting surface was added.
- Full repository suites, external SMTP delivery, real storage-outage injection and destructive catalog-state mutation tests are not claimed. Browser transport-abort/retry, executable storage-failure tests, real concurrent PostgreSQL creation, and task-owned live identity-state tests cover the exercised failure boundaries.
- Existing historical failed attempts remain in evidence. Final successful outputs are identified explicitly in E rather than treating every intermediate file as a pass.

Runtime handoff: API was relaunched from the latest compiled output for final verification using the existing safe Phase 1 launcher, same configured database and process-local loopback SMTP overrides. After the final report, that launcher exited; a subsequent port check confirmed **no API listener on :5010**. Restart the API before further manual testing. Verification results above were captured while it was running and are unchanged. Portal/storefront Vite servers and the registered headed Edge session were reused; unrelated browser tabs/processes were not terminated. No changes are staged or committed.

**Stop boundary:** product discovery/selection, eligibility and correctly owned product-specific links only. Order attribution, commission recognition/calculation/reversals, balance accounting and payouts remain for later phases.
