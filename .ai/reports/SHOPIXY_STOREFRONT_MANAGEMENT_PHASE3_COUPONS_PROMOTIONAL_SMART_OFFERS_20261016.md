# Shopixy Storefront Management — Phase 3: Coupons, Promotional Offers, Smart Offers

Date: 2026-10-16. **Disposition: LIMITED CLOSURE — confirmed Phase 3 defects repaired and activated; focused behavioral/configured checks pass. The broad backend selection remains red and this is not unrestricted end-to-end certification.**

Evidence directory: `.ai/evidence/storefront-phase3-20261016/`. Test results: `PointOfSale.Tests/TestResults/phase3-*.trx`. Counts below are independent runs, with overlap; do not add them together.

## Scope, authority, and safeguards

- Phase 3 only: Coupons, Promotions, Smart Offers, generated coupons, recommendation display, and their direct cart/checkout-pricing consumers. No Phases 4–8, staging, commit, database replacement, schema reset, or migration/reset operation.
- The user explicitly authorized synthetic writes against the configured application/database, classifying its records as temporary QA data. Tests used unique synthetic names/IDs and supported lifecycle operations; SQL fixtures were rollback-only or narrowly cleaned after committed concurrency tests.
- Actual configured paths: Admin `http://localhost:5173/`, Storefront `http://ahramstore.localhost:5174/`, API `http://localhost:5010`, AhramStore tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`.
- No credentials, cookies, access tokens, protected redeem tokens, or connection strings are included in this report. Browser scripts use the existing signed-in session and do not save authentication state.
- Hosted tests submit **zero checkout orders** and invoke no payment, notification, carrier, AI/model, or other external business provider. The configured capacity tests create controlled SQL order snapshots, not complete checkout/payment/inventory workflows.
- Phase 1 and Phase 2 reports and relevant historical quality/discount authority material were reviewed as context. Historical PASS wording and source-literal assertions are not treated as fresh runtime proof.

## Runtime and preservation evidence

| Boundary | Observation |
|---|---|
| Active API | `api-active-final.json`: PID 6788, started `2026-10-16T18:05:29.1174329Z`, Development, port 5010. User performed the restart. |
| Built vs active | `api-build-match-final.json`: active API, Infrastructure and Core SHA-256 hashes match the isolated final Debug build in `.opencode-tmp/storefront-phase3-20261016/api-built/`. |
| Readiness | `/health/ready` returned **503**: R2 unavailable; certificate-provider and Caddy integrations disabled/degraded. This is not a healthy-environment claim. Discount APIs remained usable independently. |
| Browser | Reused registered headed Edge session `opencode-ui-test`. Preserved existing Admin, Storefront and unrelated Easy Orders tabs; task-created Storefront tabs are closed by the harness. |
| Storefront identity | Normal logout/sign-in was reconciled before authoring checks; cart API confirmed `ownerType: customer`, empty basket. Earlier Account/Logout UI alone had not proved customer cart ownership. |
| Repository | `baseline-git-status.txt`, `baseline-head.txt`, `baseline-scope.diff`, original copies and manifests capture the entry state. HEAD unchanged; index remains empty. |
| Existing work | `preservation-check.json` confirms all **579** baseline deleted paths remain absent and all **31,805** baseline untracked entries remain present. `originals-comparison-final.json`: of 526 original hash entries, **513 unchanged / 13 changed**, with changed files in the task scope. This is not byte-level proof for every untracked file. |
| Attribution limits | `task-files.txt` identifies task-touched files. Thirteen have separate pre-edit originals under `originals/`; two later test edits have `additional-test-originals/` copies. Some shared files were already modified and lack an independent complete pre-task copy. `task-local-originals.diff` covers available originals, not the entire dirty repository. |

## Screen/API/consumer map

| Screen | Authoring/read authority | Storefront consumer |
|---|---|---|
| `/ecommerce/coupons` | `/api/coupons`, detail/update/delete, `PATCH /{id}/toggle`; SQL coupon validation/reservation/usage | Cart coupon input, coupon revalidation, checkout pricing/final expected-total guard |
| `/ecommerce/promotions` | `/api/promotions`, detail/update/delete/toggle and stats; SQL offer engine, order-ledger capacity | Product/category/cart candidates; cart authority and checkout's single-winner evaluator |
| `/ecommerce/smart-offers` | `/api/admin/smart-offers`, detail/update/delete and stats; linked generated coupon | Public `/api/storefront/smart-offers`, display engine/hooks/popup, protected `/redeem`, token bridge → cart coupon input |

## Confirmed findings and repairs

| ID | Defect / impact | Repair and evidence |
|---|---|---|
| P3-01 | An archived same-code coupon could mask an active replacement. | SQL code lookup excludes archived records before selection. Failing-before/passing-after regression and configured lifecycle validation. |
| P3-02 | Basket changes could invalidate an otherwise valid coupon because revalidation compared against an old savings preview. | Recompute current coupon savings instead of asserting the old preview. `CartCouponRevalidation_RepricesChangedBasketInsteadOfRejectingOldPreview`. |
| P3-03 | Unsupported promotion scope/negative priority and Smart Offer enum values could be persisted; coupon validation converted unknown discount enums into the fixed-value branch. | Validate before writes; pass the actual coupon enum to shared validation. Negative/unsupported-value regressions assert rejection and no row creation. |
| P3-04 | Cart promotion preview favored priority even when checkout selected a larger saving. | Use greatest savings with deterministic tie handling, consistent with checkout. Preview-vs-checkout regression. |
| P3-05 | Promotion arithmetic reapplied wall-clock lifecycle/derivative counter checks after admission with an explicit evaluation clock and SQL usage ledger. | Separate admitted arithmetic from those stale checks. Future-clock/stale-counter regression. |
| P3-06 | Product, cart and coupon derivative previews could stack into totals inconsistent with checkout. | New mandatory `ICartDiscountAuthority` reconciles cart responses using the existing checkout evaluator: exactly one winning SQL discount. Clears derivative price reductions before composing totals. Unit, configured SQL and hosted pricing checks. |
| P3-07 | Public Smart Offer listing/redeem admitted expired, scheduled or globally exhausted generated coupons. | Check linked coupon lifecycle and SQL confirmed/reserved usage; deterministic ID tie-break and no-store listing. Security/behavioral regressions. |
| P3-08 | Editing a Smart Offer replaced explicit zero usage limits with defaults. | Preserve zero limits rather than truthy/default substitution. Regression preserves both zero limits. |
| P3-09 | Concurrent unique-code conflicts could become unfriendly persistence failures. | Map PostgreSQL unique violations to conflict responses for coupon/promotion/Smart Offer creation and coupon update. Normalized duplicate rejection also verified against the hosted API. A simultaneous hosted create-conflict race was not exercised. |
| P3-10 | Coupon editor dropped persisted eligibility restrictions; coupon/promotion editors truncated timestamps. | Preserve product/category/exclusion fields, expose schedules explicitly in UTC with `datetime-local`, and retain exact original ISO values when unchanged. Component regressions plus actual promotion edit/readback. |
| P3-11 | Duplicate saves, out-of-order reads and responses from an old mounted workspace could change the wrong screen state. | Synchronous mutation exclusion, per-channel latest-read admission, mount/account/store fences, pending-form disabling; keyed workspace boundary on all three routes. Component tests cover same-tick duplicate saves and stale searches on all three screens. |
| P3-12 | Committed save followed by failed refresh could be announced as full success, encouraging duplicate retries. | Distinguish saved-but-readback-failed, close committed editor, expose retry/error state. Three component regressions. |
| P3-13 | Removing Smart Offer coupon code left disabled coupon values in its submitted payload; list/stat refresh and pagination were incomplete; preview was hardcoded RTL. | Clear dependent coupon fields; add pagination and refresh stats after writes; use current document direction for preview. Editor regression and actual hosted lists/empty pagination; not a full preview accessibility audit. |
| P3-14 | Smart Offer cache could leak across tenant switches, admit late replies, reuse persisted candidates indefinitely or duplicate recommendations. | Tenant/generation-fenced in-memory candidates, 60-second TTL, deterministic de-duplication/ranking; navigation/focus/timer refresh and active-popup invalidation on updated lists. Four behavioral cache regressions. |
| P3-15 | **Hosted-only discovery:** token redemption succeeded but token-only cart application failed before the controller: global request normalizer required `Code`. | Make `ApplyCouponRequest.Code` nullable; keep conditional code-or-token validation. Actual normalizer regression failed 2/5 before repair and passes 5/5 after. Activated build completes real popup → token → cart application. |

Additional display correction: scheduled and exhausted coupon states no longer fall through to Active/Expired presentation. The exhaustion display still uses the existing derivative `CurrentUsage` value; financial admission uses the SQL reservation/usage ledger, so the badge is not capacity authority.

Eight initial backend regressions were observed failing before their repairs. Separate editor/cache red runs are retained (`admin-red.json`, `storefront-red.json`). Do not interpret every new test as a separately reproduced pre-fix failure.

## Coverage matrix and evidence strength

Legend: **H** = actual hosted UI/API/configured database; **SQL** = configured PostgreSQL with controlled service dependencies; **B** = behavioral test with controlled/in-memory dependencies; **MVC** = real MVC routing/policies with controlled authentication and business-operation short-circuit; **R** = source review, not execution.

| Area | Exercised | Evidence / remaining boundary |
|---|---|---|
| Coupon lifecycle | H: normalized UI create/edit/toggle, fresh detail readback; duplicate/invalid percentage rejection, stale `If-Match` rejection; expiry rejection; cancel delete then UI delete/404. SQL: min-order/cap/tenant separation/inactive lifecycle. | Hosted script and configured lifecycle transaction. Full every-field lifecycle combinations not exhaustively hosted. |
| Promotion lifecycle | H: UI create/edit preserving UTC schedule, disable/re-enable, pricing consumer, cancel delete then UI delete/404. B: invalid scope/priority and inclusive timing boundary. | Product/category targeting and arithmetic are behavioral tests, not a hosted matrix of every scope/variant. |
| Smart Offer lifecycle | H: inactive UI create/edit preserving generated coupon identity, enable, actual public display, protected redemption, repeated application, disable/revalidation, cancel delete then UI delete/404. B: unsupported enums, zero-limit preservation, scheduled/expired/exhausted admission. | Actual popup evidence is ExitIntent/All; other triggers/page scopes are not all browser-executed. |
| Financial arithmetic | B/SQL: fixed and percentage, cap including zero, discount bounded by subtotal, rounding, item-allocation sum, category/product matching, coupon tie winner, clock boundaries. H: non-stacked 20% offer and 30% Smart Offer coupon. | Does not establish every tax jurisdiction, shipping/payment fee or currency combination. |
| Cart/checkout consistency | H: cart UI estimated total; actual checkout-pricing recomputation ignores forged subtotal/discount/total/tenant body fields. B: each of ten financial expected-total echoes rejected on final checkout guard. | No hosted final order submission/payment/inventory reservation; guard invocation is controlled service execution. |
| Coupon concurrency | SQL: eight independent connections, capacity two → exactly two accepted; five retries of one accepted order share its reservation; confirmation twice yields one usage and counter=1. | Actual SQL locks/ledger/service methods, controlled publisher; not concurrent full HTTP checkout. Targeted cleanup and fresh observer. |
| Promotion concurrency | SQL: six transactions, capacity two → exactly two committed synthetic order snapshots; exhausted offer no longer selected. | Actual checkout lock/capacity methods invoked with controlled assignments; no claim of full concurrent checkout. |
| Role/permission boundary | MVC: 19 endpoints × seven personas = **133** cases, Owner/Admin/Manager admitted, Cashier/Warehouse/untrusted owner denied, anonymous unauthorized. H: three cookie-free list requests each return 401. | Controlled MVC authentication; admitted requests short-circuit before business work. Not seven real provisioned hosted accounts. |
| Tenant/customer ownership | H: authenticated foreign-tenant header spoof returns 403. SQL/B: foreign tenant validation and existing token/customer/tenant security tests; display-cache tenant races. | Real two-account/two-store browser switching is not independently completed; server ownership assertions are not replaced by a UI label. |
| Stale and concurrent UI state | B: duplicate-save exclusion, old-search suppression across all screens, committed-save/readback failure, editor preservation, cache tenant/generation invalidation. | Full browser network-chaos, offline, cross-tab account change and every async completion ordering remain unverified. |
| English/Arabic | H: all three list screens readable in EN and AR, Arabic document RTL, required/percentage input constraints, empty next-page disabled; no authoring writes or uncaught errors. | List/RTL/input smoke, not every modal/error message or full accessibility audit. |
| Recommendation system | H: rule-based popup and real token bridge. B: ranking/de-duplication/cache race behavior. | No AI/provider execution was requested or performed. Frequency storage is a browser display hint, never financial authority. |

## Hosted execution details and failed attempts

- `tests/browser/storefront-phase3-hosted.js` uses normal Admin UI authoring and the actual signed-in Axios clients for readback and pricing. Its `finally` block clears only its confirmed-empty-at-start basket and deletes its recorded IDs. No business endpoints are mocked in this lifecycle pass.
- Final lifecycle pass: `browser-hosted-closed-final.json`, **50 checks / 0 failed checks / no harness failures / no uncaught page errors**. Includes all three UI edit/delete/cancel flows, popup-to-token application, independent token-only application/retry, actual checkout pricing for both coupon types, disabled-coupon repricing, expired-coupon rejection and fresh 404 cleanup. Earlier 40- and 47-check passes are superseded, not added to this count.
- `browser-readonly-final.json`: **24/24** actual EN/AR list/RTL/invalid-form/authorization checks, zero authoring writes and page exceptions. Includes three cookie-free 401 responses and authenticated tenant-header spoof 403.
- Financial examples in the final actual configured basket: EGP 25 subtotal → EGP 5 promotion discount / EGP 20 payable; Smart Offer 30% → EGP 7.50 coupon discount / EGP 17.50 payable; after disabling the promotion, manual 12.5% → EGP 3.13 coupon discount / EGP 21.87 payable. Current tax, shipping and payment fee were zero; these figures do not certify nonzero-charge interactions.
- Initial Storefront request without `/api` returned Vite HTML/HTTP 200; it was a harness path error and is not an API pass. Fixed script requests use `/api`.
- Early scripts had ambiguous form locators, a browser `datetime-local.fill` format error and a readback-before-save race. They are retained as failed attempts, not passed lifecycle evidence. The raced synthetic promotion was specifically deleted using a newly read row version; `hosted-raced-cleanup-final.json` verifies 204 then 404.
- Popup diagnostic imports initially used an unversioned Vite module, creating a second empty cache rather than inspecting the app's HMR-versioned module. Corrected inspection uses the actual loaded resource URL. Also, a hidden-tab ExitIntent correctly consumed the once-per-session hint before a test reload; the passing test enables the offer after that reload. No product workaround was applied for these harness errors.
- Earlier real token-only HTTP 400 (`request.Code: Value is required`) was a product defect, not a locator/timing failure, and produced P3-15's regression and repair.

## Verification results

| Check | Final recorded result | Artifact / qualification |
|---|---|---|
| Selected Phase 3/backend financial/security/checkout regressions | **277 passed, 0 failed, 0 skipped** | `phase3-backend-accepted-final.trx`; configured tests explicitly opted in; financial and pipeline additions included. |
| Updated rate-limit/cache test contracts | **13 passed** | `phase3-test-contracts-final.trx`; overlaps existing test populations. Uses attribute reflection instead of a method-signature regex; cache literal assertion reconciled with new behavioral cache tests. |
| Broad discount-related backend selection | **200 passed, 27 failed, 3 skipped, 230 total** | `phase3-broad-verified-final.trx`, `backend-broad-final-failures.json`. **Not green.** Configured tests not opted into this broad run. |
| Full Admin tests after final component-test additions | **1,909 passed, 0 failed, 0 pending** | `admin-verified-final.json`. Earlier full run 1,904; do not add counts. |
| Full Storefront tests | **537 passed, 0 failed** | `storefront-final.json`. |
| Admin and Storefront standalone typechecks | Exit 0 | `npx tsc --noEmit` in each project; `admin-typecheck-final.json` and `storefront-typecheck-final.json`, each after final relevant source/test edits. |
| Admin production build | Exit 0 | `admin-build.log`; large chunk warning (>5000 kB), not warning-free. |
| Storefront production build | Exit 0 | `storefront-build.log`; large chunk warning (>500 kB). |
| Final isolated API Debug build | Exit 0 | `api-build-final.log`; warning level suppressed by command, so its printed zero warnings is not proof of warning-free source. Runtime hashes verified. |
| Focused Admin lint | Exit 0, **0 errors / 4 hook-dependency warnings** | `lint-final.json`; added test lint 0 errors/0 warnings in `lint-added-final.json`. |
| Focused Storefront lint | Exit 0, **0 errors / 0 warnings** | `storefront-lint-final.json`. No whole-repository clean-lint claim. |
| Final hosted cleanup observer | **1 passed** | `phase3-hosted-final-cleanup.trx`; independently rerun after the final lifecycle pass, configured PostgreSQL READ ONLY transaction. |
| Task-scoped whitespace check | Exit 0 | `diff-check-final.json` and `diff-check-final.log`; Git line-ending advisories are separately retained. |

The broad run's 27 failures comprise **23 documentation/source-shape assertions**, **three distributed promotion reliability cases**, and **one POS role-assignment architecture assertion**. The distributed cases encounter missing-required-`If-Match` preconditions/timeouts rather than completing their intended scenario. These results remain failures; no missing PASS markers were inserted, no pricing/authorization rule was relaxed, and no historical document was relabeled certified. Source assertions may still identify real coverage/contract drift; passing functional tests do not cancel them. Baseline broad evidence was 175 passed/30 failed/205 total; selection/population differs, so that is not a direct numerical improvement claim.

## Cleanup and retained data

- Every completed hosted attempt records exact synthetic IDs and supported lifecycle deletion/readback. Basket cleanup uses actual DELETE + fresh GET and restores the original empty-basket state; it does not restore an invented replacement basket.
- A fresh final Admin observer returned 200 with zero visible coupons/promotions/Smart Offers (`browser-cleanup-observer-final.json`). That observer's request from the old Storefront tab returned an empty **session** cart, which is not evidence of customer-cart cleanup. A separate new-tab app bootstrap using its actual loaded client independently confirmed **customer** cart, zero items/subtotal/discount at `2026-10-16T18:34:53.239Z` (`browser-customer-cart-observer-final.json`). All three original tabs remained open.
- Coupon deletion is **soft deletion**. Synthetic coupon rows remain archived with zero usage by design; the report does not claim physical absence of these rows. Generated coupons are archived by supported Smart Offer deletion. Promotion and Smart Offer rows are removed through their supported APIs.
- `Configured_ExistingTenantSyntheticScopeCleanup_IndependentReadOnly` separately observes only tenant-scoped `P3QA` rows in a repeatable-read, **READ ONLY** PostgreSQL transaction. Final observation at `2026-10-16T18:32:50.0008029Z`, after the last lifecycle pass: **21 retained coupon rows, all archived, all usage zero; zero synthetic Smart Offers and promotions remaining; zero observer writes**. Artifact: `hosted-synthetic-scope-sql-cleanup.json`.
- Rollback lifecycle fixtures have a new DbContext observer confirming their tenant/coupon/promotion/Smart Offer rows are absent. Capacity fixtures commit only uniquely scoped temporary tenant/order/customer/ledger rows; targeted SQL cleanup and a fresh observer confirm the recorded scope is gone. Controlled event publishers avoid external delivery.
- Supported hosted API mutations may leave legitimate audit/outbox history. That history is not erased or misrepresented as no writes; complete downstream audit-consumer delivery/replay was not certified here.

## Open contracts / explicit limitations

1. **Mixed-basket coupon eligibility:** existing product/category/exclusion checks are basket admission gates; this task preserved them rather than inventing eligible-line-only discount arithmetic. Business policy must explicitly decide eligible-base arithmetic for mixed baskets before claiming that behavior certified.
2. **No stacking:** cart now matches existing checkout's greatest-saving single-winner policy; equal saving favors coupon, then offer ordering is deterministic. This is alignment with existing authority, not a newly authorized stacking policy.
3. Schedules are labeled/edited in **UTC**; unchanged precision is preserved. Browser-created minute values and exact fractional original preservation were tested separately. No tenant-timezone/DST policy was invented.
4. Smart Offer candidates can remain visible until a successful bounded refresh; TTL is 60 seconds and timer/focus/navigation request opportunities are not instantaneous invalidation. Every redemption/application still revalidates against server truth. Offline refresh/long-suspended tabs are not guaranteed current display.
5. The broad suite is red; R2 readiness is red; four focused Admin lint warnings and bundle-size warnings remain. This is not release/staging/production certification.
6. Not independently covered end-to-end: every trigger/page-scope/variant combination, two real hosted customer accounts, cross-store account switching, full multi-page populated search/filter matrix, all modal accessibility/localization/responsive cases, scheduler/projection/outbox replay, audit attribution delivery, hosted quota races, payment confirmation, refunds/release, or final order creation. Existing lower-level tests cover some of these boundaries but are not substituted for fresh hosted proof.

## Delivery boundary

Task-local sources, tests and harnesses are listed in `task-files.txt`; hashes in `task-files-manifest.json` identify final evidence inputs. No changes were staged or committed. Stop at Phase 3; remaining failures and contract questions above require explicit follow-up rather than an unqualified PASS.
