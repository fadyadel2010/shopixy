# Storefront Management Phase 6 — Marketing, Attribution and ROI

Date: 2026-10-18 (local UTC+03; most execution timestamps are 2026-10-17 UTC).

## Outcome

**PASS WITH LIMITATIONS for the bounded Phase 6 workflows.** Confirmed defects were repaired, all five actual screens were exercised, and final API/database readbacks succeeded. This is not certification of live advertising delivery or of the entire Backend suite. Only Tracking Pixel, Conversion Funnels, Attribution Overview, Campaign Costs, Marketing ROI and their directly related tracking/order/reporting paths were addressed. No Phase 7 work.

Evidence root: `.ai/evidence/storefront-phase6-20261018/`. Paths below are relative to that directory unless fully qualified. No passwords, bearer tokens, provider access tokens or decrypted configuration are included.

Final reconciliation: `closeout-summary.json` records **Backend212/212, Admin1,946/1,946, Storefront542/542 and 117/117 browser assertions**, with zero final test failures/skips. Builds/typechecks pass; focused lint has zero errors and the nine warnings detailed below. Loaded-build, cleanup and preservation evidence are included in that summary.

## 1. Environment, authorization and preservation

- Admin: `http://localhost:5173/`; Storefront: `http://ahramstore.localhost:5174/`; API: `http://localhost:5010`.
- Configured PostgreSQL: `192.168.1.5:5432/PosEcommerceDb_Baseline`. Existing configured MongoDB, Redis and RabbitMQ were used. No replacement database, schema reset, database provisioning fixture or migration was run.
- Merchant: AhramStore, tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`. Admin signed in normally using authorized QA credentials. Storefront browser traffic used a fresh anonymous session; no customer impersonation or new paid checkout was required.
- All existing records were authorized as temporary QA data, but previous financial/order history was preserved. New synthetic SQL financial scenarios were enclosed in rollback transactions; hosted cost/config changes used supported Admin APIs and narrowly scoped cleanup.
- Baseline: branch `main`, HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`; substantial pre-existing dirty work. `baseline-git-status.txt`, `baseline-index.txt`, `baseline-deleted.txt`, `baseline-untracked.txt`, `baseline-scope.diff` and `baseline-originals.json` record the starting state. The final baseline capture contained 579 pre-existing tracked deletions and 34,204 untracked paths; 166 relevant files were copied before production edits.
- `task-local.diff` compares this task against those working-copy originals, not against HEAD. `task-files-manifest.json` identifies task files. Three later-discovered source/host files have separate original copies. The existing catalog-card test was read in full before its narrow extension but has no separate pre-edit byte copy.
- No stage, commit, stash, checkout, reset or discard was performed. Output builds used task-specific directories. API binaries were backed up and copied only after the user confirmed the listener was stopped. Application configuration files were not copied or changed.
- `preservation-final.json` checks unchanged HEAD, empty index, no recreated prior deletions, no missing baseline untracked paths and no missing scoped originals. This is not a byte-for-byte proof over every file in the enormous dirty repository.
- Final comparison: 38 of 166 scoped originals changed, 128 unchanged, zero missing; 64 task source/test/harness/report files are listed. All 579 prior deletions remain deleted; all 34,204 baseline untracked paths remain present. Normal repository `git diff --check` for the task paths exits 0 (`diff-check-closeout.json`); Git emits existing LF/CRLF normalization notices. No line-ending normalization sweep was performed.

### Prior reports reviewed

The required Phase 1–5 reports were read, including the Phase 3 unresolved broad-Backend failures and Phase 4 scheduler-fixture initialization failures. Relevant implementation history also included `docs/reports/TRACKING_PLATFORM_v1.0_FINAL_CERTIFICATION.md`, `PHASE-1-ANALYTICS-FOUNDATION.md`, `PHASE-2-ANALYTICS-SDK.md`, `PHASE-3-ANALYTICS-DISPATCHERS.md`, `PHASE-7-ATTRIBUTION-ENGINE.md` and `PHASE-8B-CAMPAIGN-COST-ROI.md`. `docs/PROJECTION_EVENT_RUNTIME_TRACE_REPORT.md` was reviewed for outbox/broker/projection evidence distinctions, and the chart-of-accounts/posting/reconciliation sections of `docs/reports/UNIFIED-FINANCIAL-ENGINE-ARCHITECTURE.md` for recognition versus settlement. Their historical/design completion claims are not current test evidence; current source and executed behavior take precedence.

## 2. Implemented contracts — not assumed business rules

### Tracking Pixel and provider boundary

`/ecommerce/pixel-tracking` edits **server-side purchase conversion-value overrides**, not browser pixel IDs or provider credentials. `IsEnabled=false` disables that override; it does not globally disable event collection or enable/disable a provider. Strategies are Percentage (0–100) and Fixed (0–1,000,000), including zero. Applies-to supports Google, Meta and TikTok. Order/ledger values are not edited by this configuration. Product-level overrides are a related server capability, not a newly introduced Admin workflow.

Actual provider adapters are Google Analytics 4 Measurement Protocol, Meta Conversions API and TikTok Events API, plus internal Admin realtime delivery. Provider settings and encrypted credentials belong to `/api/admin/analytics/settings`, separate from pixel-value configuration. No provider was enabled/configured for this merchant during hosted execution. Contract tests used recording outboxes/controlled HTTP handlers and synthetic credentials, never live advertising destinations. Hosted event IDs were independently checked against SQL provider-delivery keys: zero matches.

The Storefront also loads a Facebook **OAuth login SDK**. It is not `fbevents.js` or a browser purchase pixel; `SocialLoginButtons.tsx` initializes Facebook login on user interaction. An initial harness classifier incorrectly counted this SDK as advertising initialization; the failing evidence is retained and the classifier was narrowed to advertising collection endpoints. The OAuth integration was not modified.

### Events, identity and persistence

`RootLayout → usePageViewTracking/useAnalytics → AnalyticsEventBuilder → tenant-scoped AnalyticsQueue → AnalyticsClient → POST /api/storefront/analytics/events → validator → SQL outbox/event bus → AnalyticsEventConsumer`.

- Browser events have distinct UUID event IDs; replays keep the same ID. Actor session is the shared bootstrap/cart session and survives navigation/reloads. It is not authentication. Analytics inactivity bookkeeping does not itself rotate the shared cart identity; no new rotation rule was invented.
- Tenant comes from the resolved request context. The validator resolves identity/currency server-side, strips sensitive URL material, validates type/time/payload and extracts allowlisted UTM dimensions. The tested UTM fields are source, medium, campaign, term and content.
- Durable Mongo `analytics_events` uniqueness is `(TenantId, EventId)`; Redis is a best-effort hot projection. Replayed events do not increment counters or repeat external dispatch. Attribution-stage failures can retry their idempotent stage after durable event insertion.
- Browser `PurchaseCompleted` is rejected. `AuthoritativePurchaseAnalyticsConsumer` reads the persisted same-tenant, non-deleted Collected/Settled order and active tenant, takes session/currency/total from SQL and emits a deterministic order-derived event ID. Browser/bus-supplied revenue is not the financial report authority.
- `CheckoutService` persists the shared session on orders. The configured rollback test uses that persisted identity with the actual authoritative purchase consumer; it is not a new end-to-end paid checkout/payment certification.

### Attribution

The implemented models are **FirstTouch and LastTouch**, scoped to the order's session, not cross-device identity or multi-touch fractional credit. Contexts are stored in Mongo and cached in Redis; order dimensions are append-only/immutable per `(tenant, order, model)`. Event time, rather than queue arrival time, chooses first/last eligible context at or before conversion. Missing context gives direct/none. Internal same-origin navigation is not a new referral.

Eligible touches are the contexts actually captured by the resolver (initial visit, UTM/new external evidence or qualifying source change); an ordinary direct/internal visit after a campaign does not necessarily create a new touch. Thus LastTouch is not a claim that every later untagged page replaces campaign credit. Redis context TTL is 30 days; no separate fixed campaign lookback or cross-session stitching policy was introduced.

Reports use SQL-recognized ecommerce purchase IDs in the requested interval, then Mongo attribution dimensions, overriding cached/stored event values with SQL net ledger revenue. Recognition sources are `Order_Payment`, `Order_COD`, `Order_Wallet`, `Order_UnresolvedPaymentClearance`, sales account `4020`, Posted/Reversed journals with positive recognition credit. Net revenue is credits minus debits through the report end. Fully refunded recognition can remain one purchase with zero net revenue. Merely cancelling a previously recognized order does not erase its ledger revenue; this task did not invent or post reversal journals. Orders without attribution dimensions are not silently invented as attributed orders.

### Funnels

Definitions are purchase, discovery, checkout, cart and search. Purchase stages are PageView → ProductView → AddToCart → BeginCheckout → PurchaseCompleted. Nonfinancial stages are **event totals from approximate hourly Redis buckets**, not ordered unique-session cohorts; purchases use SQL accounting authority. The hourly floor includes boundary buckets; 15/30-minute windows are not exact minute-resolution paths. Counter TTL is about 25 hours. Conversion is current/previous or final/entry ×100, zero denominators produce zero, and rates can exceed 100%; negative drop-off is possible. The UI now discloses these semantics. No monotonicity clamp or invented session attribution was added.

### Campaign costs, dates, currency and ROI

- Spend is manually declared marketing cost, not an advertising API import. Unique active key: `(tenant, normalized source, medium, campaign, periodStartUtc)`. POST retains the implemented same-key upsert contract. PUT uses the selected ID, checks an optional reviewed `If-Match` revision, atomically archives the old row and inserts a successor; a target-key collision must not overwrite another row. DELETE is soft archive.
- Amounts are nonnegative, two-decimal values in the tenant currency (EGP here), including zero. No FX conversion policy exists; mismatched declared currencies are rejected, and incompatible existing spend makes ROI unavailable rather than being silently dropped or added to EGP. The cost table displays each stored row's currency; a mixed-currency total is unavailable.
- All report ranges are UTC, start-inclusive/end-exclusive. UI cost end dates represent exclusive midnight. An unchanged edited date retains its exact stored UTC timestamp. Last 7/30 days are exactly 7/30 UTC calendar days including today. Custom reporting end dates are expanded to the next midnight. Costs contribute their **full declared amount when their period overlaps** the report; there is no daily proration. Overlapping distinct period starts can therefore both contribute; this is the existing implemented contract, not a newly chosen allocation rule.
- `profit = attributed net revenue − marketing cost`; `ROI% = profit / cost ×100`, rounded to 2 decimals; zero cost → null/not infinite; `CPA = cost / attributed purchases`, null if no purchases. This is marketing contribution, **not accounting net profit**: COGS, tax, fulfillment and operating expense are not newly subtracted by the ROI service.
- Campaign reporting excludes blank-campaign direct traffic; source reporting includes it. Consequently Marketing ROI campaign totals need not equal Attribution Overview/source totals. Join keys are structured tuples, avoiding collisions when UTM text contains `|`.

## 3. Defects confirmed and remediated

| Defect/evidence | Repair and regression evidence |
|---|---|
| Browser baseline: home emitted no PageView; product emitted two; campaign tags were absent | One root navigation owner; ordinary hooks no longer auto-emit; allowlisted bounded UTM query survives, arbitrary tokens/fragments/credentials do not. Browser tracking plus Storefront tests. |
| Catalog quick-add persisted a cart item without AddToCart | Emit once only after successful quick-add; failed/repeated in-flight action behavior tested. |
| Early token-removal telemetry sent before CSRF initialization, causing permanent 403 | Use the existing buffered analytics path; dedicated telemetry regression. |
| Late old-tenant queue response could mutate a newly loaded tenant queue | Verify active queue storage scope after await; unload also refreshes tenant scope. Deferred-response behavioral test. |
| Funnel PurchaseCompleted read raw Redis counter despite financial authority | Use `IAnalyticsMetricReader.GetMetricValueAsync(PurchaseCompleted)`; configured counts deliberately diverge from earlier-stage events. |
| Hour bucket traversal could miss the first boundary hour; dependency failures became successful zeros | Floor both endpoints to hourly bucket keys; propagate core metric/report failures so the UI can show unavailable. |
| Pixel UI exposed editable defaults on load failure; zero preview skipped; late previews/saves could win | Failure/retry state, request generations, save exclusion, disabled editor, zero preview, fresh readback and qualified save/read failure. Eight maintained Admin regressions cover these and cost/ROI states. |
| Numeric undefined enums, unknown providers and invalid override values accepted | Defined-enum/provider and strategy-specific bounds enforced; preview/product endpoint validation aligned. |
| Nested background scopes could read a filtered-out pixel configuration as disabled | Explicit tenant-predicated config/product reads and updates across scope boundaries. Cold-cache test updates the intended row without creating another. |
| Provider order-item lookups lacked same-tenant authority; Meta/TikTok used fallback totals after lookup failure | Explicit same-tenant SQL order/item checks and fail-closed purchase mapping. Three provider isolation/disabled-state regressions and golden payload contracts. |
| Out-of-order touches and delivery time could change first/last credit; later visits could win earlier purchase | Select eligible durable contexts by event time; conversion timestamp carried into immutable dimensions; stable event-derived context identity permits stage retries. |
| Internal Storefront referrer could replace paid campaign credit | Ignore same-origin navigation as a new referral, retain external organic/referral behavior; dedicated regressions. Historical immutable attributions were not rewritten. |
| Attribution query used a second, delivery-time date filter and silently capped totals at 10,000 | Join dimensions to SQL recognition IDs without the conflicting timestamp filter; aggregate full selected results. Core store errors no longer pretend to be empty reports. |
| Cost UI always POSTed even when editing, could leave an old active cost, lost zero, concealed errors | Versioned selected-ID PUT, atomic archive/successor, conflict handling, zero/date preservation, visible errors and readback status; real UI create/edit/reload verified. |
| Inclusive cost overlap duplicated boundary spend; report presets selected 8/31 days | Half-open cost overlap, exact 7/30-day UTC presets and boundary regressions. |
| ROI campaign response omitted overall ROI/CPA; card recalculated CPA client-side | Supply server aggregates consumed directly by cards, including zero/null semantics; UI/API arithmetic tests. |
| Mixed currencies silently inflated ROI; concatenated UTM keys could merge distinct campaigns | Fail unavailable on unsupported currency combinations; structured joins; dedicated regressions. |
| Analytics views could retain account/store-scoped state; stale request failure left old ROI visible | Existing workspace boundary now wraps marketing routes; latest-request guards clear failed data. |
| Relevant baseline tests had unsafe shared SQLite connection and obsolete migration file assertion | Independent worker connections to a named shared-memory unit-test DB; assertion targets the existing recovery baseline migration. No production migration was created or applied. |

Primary changed areas: `PointOfSale.Storefront/src/analytics/`, catalog quick-add and checkout telemetry; `PointOfSale.Infrastructure/{Analytics,Attribution,CampaignCost}/`; `PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs`; the three Admin controllers; `PointOfSale.Web/src/modules/analytics/` and `src/modules/storefront/config/PixelTrackingConfigPage.jsx`. `ApplicationDbContext` adds cost optimistic-concurrency metadata only, no new column/schema. Existing dirty code was retained.

## 4. Actual browser coverage

Named headed Edge session: `opencode-ui-test`, reused through Playwright CLI. The harness used actual running screens/APIs, not mocked pages. Browser scripts are maintained under `tests/browser/storefront-phase6-*.js`.

| Screen/path | Exercised behavior | Evidence |
|---|---|---|
| Tracking Pixel `/ecommerce/pixel-tracking` | Load, enable, zero percentage, provider selection, save/readback/reload, preview, invalid enum/provider, disable and candidate-owned restoration | `browser-marketing-final.json`; unit failure/retry/race tests supplement hosted success |
| Conversion Funnels `/analytics/funnels`, `/analytics/funnels/purchase_funnel` | Definition list, actual stage table/counts, read-only refresh/render, responsive/RTL; UI counts matched API, then independent Redis/SQL observer | `browser-readonly-closed-final.json`, `browser-report-readback-final.json`, `hosted-report-independent-observer.json` |
| Attribution Overview `/analytics/attribution` | Actual source totals/campaign data, read-only responsive/RTL, displayed total/order equality with API; independent Mongo/SQL dimensions and revenue comparison | Same readback/observer evidence |
| Campaign Costs `/analytics/attribution/costs` | Create three synthetic rows, including zero; selected-ID edit; reload; invalid amount/currency; stale revision; UI delete cancellation/confirmation, exact-ID archive and fresh readback | `browser-marketing-final.json`, `merchant-observer-final.json` |
| Marketing ROI `/analytics/attribution/roi` | Real fixture rows, overall -100% card, independent spend/revenue/profit/ROI checks; archive removes fixture results; RTL/mobile | `browser-marketing-final.json`, `roi-hosted.png`, `roi-ar-mobile.png` |

Read-only pass covered six routes at 1440px and 390px in English and Arabic: correct direction, no document overflow, useful rendered content, no marketing writes and no uncaught page exceptions. It also exercised anonymous 401 and foreign-tenant header 403. Real-MVC authorization tests cover 16 routes/methods ×7 caller roles (112 cases): Owner/Admin admitted; Manager may read marketing data but not configure pixel/write costs; Warehouse/Cashier/untrusted owner denied. These tests stop after policy admission and do not certify controller mutation behavior by themselves.

`browser-controls-final.json` adds actual funnel-window, attribution-model/date and combined ROI-model/date selections. Each of the five real screens was then given an explicitly **harness-injected GET503**, displayed its failure state and recovered through its normal Retry/Refresh against the real API. This was controlled UI failure testing, not an observed infrastructure outage. All 17 checks passed, with zero marketing mutations. The marketing mutation pass additionally exercised UI delete cancel/confirm; the cancelled action left the selected row unchanged.

Hosted Storefront journey used home with all five UTM tags → catalog quick-add → product detail → cart → checkout entry, then cart cleanup. It submitted **zero orders** and no purchases. Batch ingress returned 202; replayed event ID was accepted at ingress but independently appeared only once in durable Mongo. Distinct per-navigation PageViews and one ProductView/AddToCart/BeginCheckout were recorded. Security/authentication 401 console messages in a fresh guest context are retained; no uncaught page errors were observed. No advertising collection request or provider queue entry for those IDs was observed.

## 5. Independent calculation datasets

### A. Configured SQL/Mongo/Redis rollback fixture

Generated tenant/session/order IDs are in `configured-manifest-start.json` and per-invocation archived evidence. SQL rows never commit. Actual `AnalyticsEventConsumer`, cold/hot stores, resolvers, authoritative paid-order consumer, financial reader, funnel and ROI services were exercised. Only the event-publisher boundary was captured; external dispatchers were an empty controlled set. Mongo/Redis fixture rows committed temporarily and were deleted by the new manifested tenant only, followed by fresh observers.

Six orders: five paid recognitions (100, 20, 30, 100, 50 EGP), one cancelled/failed without recognition; one full 100-EGP refund. Duplicate deliveries do not duplicate event documents, counters or attributed purchases. Abandoned/partial sessions, direct traffic, first/last campaign changes and post-conversion visits are included.

| Campaign/source | Net revenue | Cost | Profit | ROI |
|---|---:|---:|---:|---:|
| profit | 100 | 40 | 60 | 150% |
| loss | 20 | 40 | -20 | -50% |
| free (revenue-only/zero spend) | 30 | 0 | 30 | null |
| refunded | 0 | 10 | -10 | -100% |
| cost-only | 0 | 25 | -25 | -100% |
| direct / none (source total only) | 50 | 0 | 50 | null |

Campaign totals: **150 revenue −115 cost =35 profit; 35/115×100 =30.43%**. Source totals include direct: **200−115=85; 85/115×100 =73.91%**. These percentages are independently calculated here; individual service results and amounts are asserted in the fixture. Full refund contributes zero net revenue and one recognized purchase, cancelled/unpaid contributes no purchase/attribution. Other earlier stages intentionally count **4 →2 →2 →1**, SQL purchases **5**, so overall aggregate rate is **125%**, not a unique-customer conversion rate.

`configured-calculations.json`, `configured-cleanup-keys.json`, `configured-cleanup-observer.json` and archived invocation files contain actual results and cleanup proof. Constraints were forced immediate before rollback. Earlier fixtures were not repaired/deleted by this test.

### B. Hosted AhramStore cost-only fixture

Unique prefix per invocation, `p6qa20261018-<timestamp>`. Create main 40, cost-only 15 and zero 0; edit main to a new selected-ID successor of 25. Active spend =**25+15+0=40**, campaign revenue/orders =0, profit =**-40**, aggregate ROI =**-100%**; zero row's ROI is null/omitted by API serialization. UI reload and actual card display agreed. All original/successor IDs are recorded in each `browser-marketing-*.json`; supported DELETE archives them, preserving replacement history. Fresh API/SQL reads find no active fixture cost, and ROI no longer contains fixture campaigns.

### C. Hosted report reconciliation

The independent read-only observer uses direct Redis bucket reads, SQL recognition IDs/ecommerce orders/net ledger lines and Mongo attribution dimensions, **not** a second call to the production reporting API as its oracle. `browser-report-readback-final.json` also reads actual table/card text. Final source totals are **EGP107.35 across three attributed purchases**, not zero and not the campaign-only ROI amount. Final hosted funnel counts **26 →5 →4 →4 →0** matched all five displayed/API counts and direct storage reads. Attributed order IDs, source purchase totals and net revenue are retained in `hosted-report-independent-observer.json`. Later UI-only control tests create no Storefront events, so do not alter these values.

The first version of this observer incorrectly used a design-time SQL context with no tenant-scoped filter context, producing expected zero against 107.35. That test failure was retained and corrected with explicitly tenant-predicated independent queries; production revenue was not changed to fit the test.

## 6. Verification results and commands

Final Backend selection and loaded-build verification completed after the last production edits. No test failure is hidden by counting an earlier successful run.

| Check | Executed result/evidence |
|---|---|
| Relevant Backend baseline | **67 passed /2 failed /0 skipped**, 69 total; `PointOfSale.Tests/TestResults/phase6-baseline.trx` |
| Final relevant Backend/configured tests | **212 passed /0 failed /0 skipped**, 212 total; `PointOfSale.Tests/TestResults/phase6-backend-closeout.trx` |
| Final independent observer after hosted UI delete/cleanup | **1/1**, 0 failed/skipped; `PointOfSale.Tests/TestResults/phase6-final-fresh-observer.trx` (overlaps the selected suite; not an additional unique test) |
| Full maintained Admin baseline | **1,938/1,938**; `admin-full-baseline.json` |
| Full maintained Admin final | **1,946/1,946**, 0 failed/skipped; `admin-closeout.json` (also passed in `admin-closed-final.json`) |
| Full Storefront baseline/final | **537/537 →542/542**, 0 failed/skipped; `storefront-baseline.json`, `storefront-closeout.json` |
| Admin / Storefront typechecks | `npx tsc --noEmit`: exit 0 for both after edits |
| Builds | Final API Debug isolated build and Admin/Storefront production builds: exit 0; `api-build-final-activation.log`, `admin-build-closeout.log`, `storefront-build-closeout.log` |
| Focused Admin lint | 30 files, **0 errors /3 warnings**, exit 0; `admin-lint-closeout.json` |
| Focused Storefront lint | 18 files, **0 errors /6 warnings**, exit 0; `storefront-lint-closeout.json`, including the additional queue test |
| Hosted marketing mutation/readback | **26 checks passed**, no failures or page errors; `browser-marketing-final.json`, after final activation |
| Hosted tracking | **12 checks passed**, no page errors; `browser-tracking-final.json`, after final activation |
| Hosted read-only/auth/RTL/mobile | **56 checks passed**, no marketing writes/page errors; `browser-readonly-closed-final.json` |
| Hosted model/date controls + controlled failure/retry | **17 checks passed**, no marketing writes/page errors; `browser-controls-final.json` |
| Actual report table/card versus API | **6 checks passed**; `browser-report-readback-final.json`; independent SQL/Mongo/Redis observer also passed |

The five browser groups total **117 successful assertions**, with zero failed assertions/uncaught page errors in their final invocations. This count is distinct from automated unit-test totals and does not turn controlled injected responses into real-outage evidence.

Backend command family:

```powershell
$env:PHASE6_CONFIGURED='1'
$env:PHASE6_STAGE='final'
$env:PHASE6_EVIDENCE=(Resolve-Path '.ai/evidence/storefront-phase6-20261018').Path
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Phase6MarketingTests|FullyQualifiedName~Phase6ConfiguredTests|FullyQualifiedName~Phase6HttpAuthorizationTests|FullyQualifiedName~StorefrontAnalyticsHardeningTests|FullyQualifiedName~StorefrontAnalyticsAuthorityCertificationTests|FullyQualifiedName~ProviderDeliveryContractTests|FullyQualifiedName~ProviderDeliveryInfrastructureTests|FullyQualifiedName~ProviderDeliveryHardeningArchitectureTests|FullyQualifiedName~PixelValueResolverTests|FullyQualifiedName~AdminOrderSessionIntelligenceTests" --logger "trx;LogFileName=phase6-backend-closeout.trx" -v quiet -p:WarningLevel=0
```

Configured tests require explicit opt-in and use the existing configuration with **no provisioning fallback**. Unit contracts may use InMemory or controlled SQLite; those are not substitutes for the configured-database scenarios. WarningLevel=0 suppressed build warning output for the Backend commands, not test failures.

Admin: `npx vitest run --maxWorkers=2 --testTimeout=30000 --reporter=json --outputFile=../.ai/evidence/storefront-phase6-20261018/admin-closeout.json`. Storefront: `npx vitest run --maxWorkers=1 --reporter=json --outputFile=../.ai/evidence/storefront-phase6-20261018/storefront-closeout.json`. Builds used `npm run build -- --outDir ../.opencode-tmp/storefront-phase6-20261018/<specific-output>` and isolated `dotnet build ... -o ...` directories; existing output paths were not recursively emptied.

Intermediate evidence is deliberately retained: initial Admin marketing-directory test selection found zero tests and exited 1; new tests first hit missing dependencies/types, resolved without installing packages; broader Storefront exposed a browser-global import issue, repaired with a queue initialization guard. An Admin full run had one architecture scan timeout (1,944 passed/1 failed), then passed with explicit 30-second per-test timeout. Early browser failures were locator/runtime-harness issues, with configuration ownership/restoration checked. A broad `FullyQualifiedName~Phase6` exploratory selection passed 215 with two opt-in skips: unrelated R2 and Bosta qualification read-only fixtures; neither was enabled or counted in the final focused 212. These failed/skipped attempts are not counted as final passes. The 27 historical Phase 3 broad-Backend failures and four Phase 4 scheduler-fixture initialization failures are **not certified resolved** by this selected run.

## 7. Cleanup, retained history and runtime

- Hosted pixel business configuration restored using a **fresh current-state comparison** against the recorded candidate before writing the narrow original values: disabled, Percentage100, applies-to empty. Update timestamps/operator audit metadata legitimately advanced; they were not backdated. The default was not blindly overwritten from a stale whole-store snapshot.
- The first `merchant-observer-baseline.json` financial snapshot was acquired while a hosted pixel candidate was temporarily active; its config field is not the original pixel baseline. Original business values are recorded in `browser-baseline.json` and each marketing invocation's `originalPixel`. Final SQL independently asserts disabled/Percentage100/empty providers. No claim is made that the original config-row existence/audit metadata were restored byte-for-byte.
- Hosted costs are archived, not physically deleted. Guest carts are cleared only after their new session's empty baseline was confirmed, followed by fresh cart readback. No order/payment/shipment was submitted by the hosted tracking workflow.
- Browser analytics events/contexts and ordinary operational telemetry are intentionally retained as QA evidence; they affect aggregate event totals until normal retention expires. No broad counter reset, old analytics cleanup, ledger deletion or earlier-phase cleanup was performed.
- Configured financial fixtures roll back; new fixture Mongo/Redis entries are deleted by exact manifested tenant scope. Fresh SQL/Mongo/Redis checks report absence. Per-invocation evidence is archived; early repeated configured runs reused top-level evidence names, so those top-level files are the latest invocation, not a full archive of every early generated ID.
- Independent final snapshot protects four Phase 4/5 orders and their four associated journals, including both legitimate COD collection/settlement journals. Fingerprint: `3BB567CD03E17A5BF139FDCCE5937E140600D91A8A8D93E5D14DB1147D342979`. Orders `ee42f429-fa3d-46b6-988a-b900cc3e8648`, `258be5dc-92d9-4489-a1c4-0872d7f8b2b0`, `45772d30-62df-4982-883e-852608e9f61f`, `a0755b91-1f50-442b-9c16-df9d32e11cfd` remain unchanged in that snapshot. Settlement is not duplicate revenue.
- WhatsApp account `138379f5-7754-4c20-8863-b3b7aad28888` remains disabled; no WhatsApp/payment/shipping/provider-readiness/health threshold was altered. Phase 3 coupon/earlier financial history was not targeted.
- `/health/ready` returned HTTP200 with **Degraded**, not fully Healthy: disabled certificate-provider execution and disabled Caddy-edge integration. RabbitMQ analytics endpoint, SQL, Mongo and Redis readiness were available. These existing disabled integrations were not changed to make the health result pass.
- Final API process **PID1604**, started `2026-10-17T22:21:56.333972Z`, Development, `http://localhost:5010`, normal `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`. `api-active-closeout.json` and `api-build-match-closeout.json` show all three API/Infrastructure/Core assembly hashes match `api-built-final-activation`; `api-install-closeout.json` records all nine installed artifacts. Environment fingerprint and all seven configuration-file hashes match the baseline. A build success alone was not treated as proof of the loaded assembly.

| Loaded assembly | MVID | SHA256 |
|---|---|---|
| API | `50d7da8a-c42a-4c83-95a9-f3721bce79b2` | `C3DAE007B1FB9D4B8746D7436859B51BB5CD3AE8AB8EB8DCF1607DC0F8EF16DE` |
| Infrastructure | `8bd67a18-8b73-42ca-a0ab-eff6b2d6be22` | `C24CADB036DF22B246787C030ABE4A3C7142F861A78885791A41C906FAF98342` |
| Core | `87d15ce7-620c-4b7e-8462-9f1c6f91b0c7` | `51238140396685BCE3F97607F97322471BB61A08A9C581F791C3BC881DDF0645` |

## 8. Remaining limitations / not certified

1. **No live provider/property receipt was verified.** No verified test advertising property was supplied; provider contracts/transports were controlled and merchant providers stayed unconfigured. No ad spend was incurred. Production external acceptance and provider dashboard deduplication remain unverified.
2. Funnels are approximate aggregate event/SQL purchase ratios, not ordered unique-session funnels. Redis projection failure/TTL can undercount nonfinancial stages. Fine-grained cohort semantics, durable counter replay/reconciliation and historical minute-resolution funnels were not invented here.
3. Immutable order attribution can still reflect genuinely missing/delayed page evidence at first resolution; no settling window, late-evidence rewrite, cross-device model or backfill was introduced. Existing self-referral dimensions were preserved rather than retroactively rewritten.
   Durable event insertion is not a transaction with Redis projection or provider outbox enqueue. The changed attribution stage is retryable; this task does not certify full end-to-end exactly-once delivery or recovery from every cross-store partial failure. Full-session durable context reads also need load testing at high volume; removing the 10,000-row reporting truncation is correctness work, not a scalability certification.
4. No new hosted paid/refund checkout was performed. Paid/refund/cancelled financial math was verified through configured transaction fixtures and existing real merchant history. This is not another payment/shipping lifecycle certification or a complete business-accounting profit calculation.
5. Cost-overlap allocation remains full-amount/no-proration; this must be understood when choosing report periods. No unsupported currency conversion or period allocation rule was added.
6. Full broad Backend/reliability suites were not rerun indiscriminately because historical managed-database provisioning fixtures and unrelated failures remain. Final counts are for the explicitly listed relevant selection. Prior Phase 3/4 failures remain separate.
7. Builds retain warnings about bundle sizes, Storefront circular barrel/chunk imports, outdated Browserslist and tool deprecations. Focused lint has three pixel effect-ref warnings, two analytics callback dependency warnings and four Fast Refresh mixed-export warnings, not a zero-warning claim. Fresh guest authentication emits expected 401 console noise. No claim is made of a clean entire-repository lint/build-warning baseline.

Stop after final Phase 6 verification/report reconciliation; do not start Phase 7.
