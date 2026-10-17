# Storefront Management — Phase 1 Core Configuration

**Execution date:** 2026-10-16 (local UTC+03:00; evidence timestamps use UTC).

**Outcome:** Phase 1 investigation, scoped remediation, and the authorized verification are complete. Dashboard, Settings, Checkout Fields, and Domains received fixes and regressions. **This is not closure of the entire Storefront Management domain. Phase 2 was not started.** External DNS/TLS/edge activation and real order/payment execution remain explicitly unqualified.

## 1. Scope, baseline, and safety

- Repository: `C:\Users\fadya\source\repos\PointOfSale`, branch `main`, HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`.
- Existing extensive tracked modifications, 579 tracked deletions, and 583 untracked entries were present before this task. They were not reset, stashed, staged, committed, cleaned, or treated as disposable.
- The four applicable 20261015 quality reports were read: `SHOPIXY_END_TO_END_QUALITY_AND_INTEGRATION_CLOSURE`, `SHOPIXY_QUALITY_CLOSEOUT_REPORT`, `SHOPIXY_POS_ADMIN_COMPREHENSIVE_QUALITY_REVIEW`, and `SHOPIXY_STOREFRONT_COMPREHENSIVE_QUALITY_REVIEW`. No matching `SHOPIXY_STOREFRONT_MANAGEMENT_DOMAIN_CERTIFICATION_*.md` report was found. Historical passes were not substituted for current tests.
- Existing PostgreSQL `192.168.1.5:5432/PosEcommerceDb_Baseline` was reused. No database provisioning, schema reset, replacement database, or migration command was run. EF InMemory tests use isolated test-local databases, not a replacement application database.
- Existing headed Edge session `opencode-ui-test` was reused. No browser profile was replaced. The existing Owner session was sufficient; supplied passwords were not embedded in scripts or this report. Other user tabs and unrelated processes were not closed.
- No real customer order, charge, shipment, inventory change, financial transaction, outbound message, merchant activation, DNS mutation, certificate request, Caddy change, or primary-domain activation was initiated by these checks.
- Normal application background workers remained part of the existing runtime. This report does not claim the whole shared database was frozen or that unrelated background activity ceased.

**Evidence root:** `.ai/evidence/storefront-phase1-20261016/` (abbreviated **E/** below).

### Runtime identity and activation

| Item | Observed identity |
|---|---|
| Original API | PID 12572, Debug executable, start `2026-10-16T13:44:16Z`, port 5010; `E/api-before.json` |
| Admin dev server | Vite PID 16844, `PointOfSale.Web`, port 5173 |
| Storefront dev server | Vite PID 21024, `PointOfSale.Storefront`, port 5174; tenant URL `http://ahramstore.localhost:5174/` |
| Activated API | User explicitly confirmed the API was stopped. Isolated Debug build launched with content root `PointOfSale.API`, Development environment, and `http://localhost:5010` |
| Final API | PID **21272**, start `2026-10-16T16:10:17Z`; actual loaded assemblies captured in `E/api-active.json` |
| API assembly | `.opencode-tmp/storefront-phase1-20261016/api-built/PointOfSale.API.dll`; MVID `5342d397-5574-4a8e-b8de-b678481c3488`; SHA256 `CFEAC46E9CA5D9B6E92F73E5F56E574C44F2FF0640DACD0C9BB1078AD3AABC93` |

Configuration-file hashes match the original capture; known launch environment values and the existing database were retained. Full environment fingerprints differ from the original launcher, so byte-identical process environments are **not** claimed. The earlier API background launch hit a terminal lifetime limit; a later process disappeared during the pause. The final launch uses `Start-Process`, with logs under `.opencode-tmp/storefront-phase1-20261016/`, rather than a finite-lived shell invocation. No second API listener was run concurrently.

Readiness after activation was HTTP **200 / Degraded**. Certificate-provider execution and Caddy integration are disabled. A later observation returned a genuine **503**: server log at `2026-10-16T16:13:18.206Z` identifies unhealthy R2. The next captured payload returned **200 / Degraded** at `16:13:29Z`; this retry does not erase the transient failure. See `E/readiness-after.json`, `E/readiness-transient-log.json`, and `E/readiness-final-observation.json`. No R2/certificate configuration was changed to force green health.

## 2. Evidence classification

- **H — Hosted:** actual Admin UI / API / existing database, authenticated Owner; public tenant Storefront reads; safe negative HTTP checks.
- **P — PostgreSQL integration:** actual configured provider, UUID-scoped synthetic data in an explicit transaction; deferred constraints checked, rollback performed, fresh observer verifies absence. Event publishers/external boundaries are controlled.
- **C — Controlled browser:** actual React source components and stores, fresh isolated browser contexts, all application HTTP intercepted. Not evidence of live provider execution.
- **U — Automated isolated tests:** Vitest, EF InMemory, and in-process real MVC HTTP tests. MVC tests control authentication/authority and stop admitted requests before business mutations.
- **S — Source trace:** inspected implementation, not a substitute for executing an external workflow.

## 3. Coverage matrix — Dashboard

| Workflow / contract | Trace and verification | Result / limit |
|---|---|---|
| Entry and compatibility route | `/storefront`, `/storefront/overview` → `StorefrontDashboard.jsx` → current tenant status/analytics cards | H: both render current AhramStore overview |
| Store identity/status/theme | `StorefrontStatusCard` → `storeConfigService.getStoreConfig` → `GET /api/admin/store-config` → tenant/permission boundary → `AdminStoreConfigService` → SQL StoreConfig → tenant-scoped presentation admission | H: correct live tenant; C/U: load failure is not mislabeled “Not configured”; effective retry |
| Analytics totals | `WishlistAnalyticsCard` → `GET /api/admin/wishlist-analytics` → Analytics permission and Owner/Admin/Manager role boundary → `StorefrontWishlistService.GetWishlistAnalyticsAsync` | H: 0 items, 0 customers, average 0; independent read-only SQL agrees |
| Analytics aggregation/detail | Tenant-explicit SQL counts/grouping/recent activity; top product details from tenant-filtered Mongo projection; tenant currency from SQL | S traced. Nonempty shared-tenant analytics were not manufactured; live nonempty product-ranking enrichment is not certified |
| Empty/error/retry | Previously invisible failed card and unrefreshable empty card | C/U: visible failure and retry; H: empty analytics can refresh |
| Overlapping requests | Latest-request guard and tenant dependency in analytics | U: later result wins when refreshes finish out of order |
| Navigation | Status Manage opens `/storefront/status`; existing card navigation remains under existing route/authority rules | H actual status navigation; U permission-aware manage visibility; other authoring pages are outside Phase 1 |

## 4. Coverage matrix — Settings

| Workflow / contract | Trace and verification | Result / limit |
|---|---|---|
| Read and candidate state | `/storefront/status` → `StorefrontSettings` → baseline/candidate/dirty guard → no-store Admin config read | H correct persisted tenant; C retry after initial failure; U preserves tenant authority |
| Validation/localization | Status selection; maintenance-message maximum 500; labeled textarea; confirmation; English/Arabic resources | H Unicode save and Arabic/RTL rendering, 390px viewport without page-wide overflow; no raw `storefront.*` keys |
| Narrow status write | `updateStorefrontStatus` → `PUT /api/admin/store-config/status`, CSRF cookie/header and `If-Match` → Owner/Admin + Storefront design permission → expected xmin → status/message-only service mutation | H save succeeds, fresh readback changes version, every unrelated Admin response property stays equal |
| Availability transitions | Normalization/admission → service persistence → SQL readback | P: maintenance → disabled → live on synthetic tenant; **shared AhramStore stayed live throughout** |
| Concurrency | Optimistic token, 409 conflict dialog, latest reload / explicit retry | H stale version returns 409; U failed reload retains unsaved candidate and does not announce success |
| In-flight user edits | Accepted baseline separated from latest candidate | U: edits made during save remain dirty instead of being overwritten; duplicate save suppressed |
| Atomicity/events | `PersistStoreConfigChangeAsync` transaction/savepoint → event staging → commit → cache invalidation; StoreConfig/Tenant update events | P: controlled event-stage failure leaves persisted status unchanged. H public read proves immediate SQL-authoritative config, not every downstream projection |
| Storefront consumption | `StorefrontStoreConfigController` SQL tenant/config join → live mapper → Storefront runtime config; `App.tsx` status branch selects maintenance/disabled screen | H public consumer sees and then loses scoped message after restoration; status screen switching traced in S and synthetic service transitions in P, not hosted downtime testing |
| Shared link state | `SettingsContext` → `/settings` → configured storefront URL | U: tenant switch reloads; late old response cannot repopulate old store URL after logout/tenant change |

### Hosted restoration procedure

The UI selected maintenance only as a **local candidate**, entered a unique Unicode message, then reselected live **before save**. Only the message changed on the backend. Fresh readback admitted the accepted version; a deliberately stale request returned 409. Restoration fetched a fresh version, verified the message still belonged to this check and status still matched baseline, then restored only status/message via the narrow endpoint with `If-Match`. Public readback verified the original message/status. Error paths have guarded `finally` restoration; they do not blindly PUT an old full configuration or overwrite concurrent administrator changes.

## 5. Coverage matrix — Checkout Fields

| Workflow / contract | Trace and verification | Result / limit |
|---|---|---|
| Load/default initialization | `/storefront/checkout-fields` → `useCheckoutFields` → `GET /api/admin/storefront/checkout-fields`; explicit system-field initialize endpoint when needed | H: original 10 fields, 7 enabled; P/U: initialization is idempotent; stale/malformed load protection added |
| Editor validation/serialization | `CheckoutFieldsPage` draft → required label, type-specific configuration, options, required/enabled rules, upload configuration | H blank label blocked; full `Large` option typing keeps focus; U configuration regressions including EMAIL length constraints |
| Create/edit/archive | Service client → POST/PATCH/DELETE Admin controller → Owner/Admin + design permission → authenticated tenant → `CheckoutFieldDefinitionService` → SQL definitions | H: scoped optional SELECT created disabled, enabled via edit, then archived via real UI; original active definitions are byte-for-byte equal on fresh API readback |
| Ordering | Move/drag → PUT `/order` → service tenant/duplicate validation → persisted order; Storefront orders by display order, created time, ID | U existing service/editor reorder tests; client no longer applies an incompatible label tie-breaker. Existing shared fields were not reordered merely to generate evidence |
| Request overlap/recovery | Synchronous mutation/order exclusion and request sequencing | U duplicate same-tick submit issues one request; C accepted create + failed readback closes editor and reports “saved, but … reloaded”, not unqualified success |
| Public field contract | `GET /api/storefront/checkout-fields` → verified host/tenant → `StorefrontCheckoutFieldDefinitionService` → enabled, active supported definitions | H disabled synthetic field excluded, enabled optional SELECT/options included, archived field excluded; P fresh context readback also exercised |
| Actual rendering/validation | `checkoutStore.fetchCheckoutFields` → `ShippingStep` / field rules → custom field state and validation | C: actual SELECT/options render; required missing value rejected, configured option accepted; unavailable dependency fails closed rather than silently falling back |
| Server admission | `CheckoutService.ValidateCheckoutFieldDefinitionsAsync` reloads tenant definitions and evaluates required/conditional/type/option authority | P executes the real admission method directly: required, invalid option, valid value, duplicate ID, unknown ID, disabled behavior; no order method invoked |
| Tenant/invariant boundaries | Service explicit tenant predicates, system type/archive protections, rule validation | P foreign update yields FIELD_NOT_FOUND; U existing system/rules regressions; hosted header spoof rejected |
| Uploads/order snapshots | Existing attachment/rule/snapshot contracts included where selected; authoring preserves upload settings | U/S only for upload types/snapshots. No real file upload, customer order, or payment was performed |

**Persistence qualification:** Hosted checks deliberately used only optional synthetic fields; no required shared field was introduced. The two successful hosted mutation runs each archived their own uniquely labeled custom field. Soft-deleted synthetic rows and normal audit/outbox/update timestamps may remain; no hard-delete cleanup was attempted. “Restored” means original active definitions/configuration semantics, **not** a byte-identical physical database or unchanged xmin. Transactional P fixtures leave no synthetic tenants/definitions behind.

## 6. Coverage matrix — Domains

| Workflow / contract | Trace and verification | Result / limit |
|---|---|---|
| Overview/list/activity | `/storefront/domains`, `/list`, `/activity` → read hooks/service → no-store GET controller → domains-manage permission → explicit tenant SQL read service | H legitimate 0-domain portfolio and empty activity; independent SQL agrees. C query failure stays an error, not a fabricated empty portfolio |
| Filtering/sorting/navigation | List parameters → API canonical sort names → backend query; hostname opens detail | U: type/status/created sort tokens corrected, unsupported SSL sort removed, row navigation wired |
| Overview truthfulness/actions | Saved-domain summary, SSL review and redirects navigation | U: no assertion that saved means DNS/TLS connected; no-op buttons replaced with safe owning-screen navigation; promotion offered only at SslActive, matching backend |
| Wizard | Seven-step draft → POST create → PUT verification/start → guarded navigation | H reviewed/cancelled without creating anything; C/U: accepted create remembered, failed verification setup can retry without another create; success navigation waits for busy guard release; discard dialog is rendered |
| Domain SQL lifecycle | `DomainWriteService` + repository → normalized global hostname uniqueness → optimistic version → audit + staged domain events → SQL | P: synthetic Draft create, duplicate rejection, update, stale version rejection, foreign-tenant rejection, archive/restore/delete; audit and captured events checked; whole transaction rolled back |
| HTTP authorization/negative inputs | Admin roles + `storefront.domains.manage` + trusted tenant boundary | H malformed hostname 400, unknown ID 404, spoofed tenant header 403. U real MVC checks exercise restricted roles/anonymous/untrusted authority without invoking mutations |
| Detail/verification/SSL | Tenant-scoped GETs, actual missing-domain readbacks | H: each unavailable resource gives 404 and an explanatory missing-domain surface. No verification/certificate command clicked |
| Redirects/history | Read-only auxiliary surfaces, tenant-scoped contracts | H/U: 404 is no longer a blank screen; safe return to portfolio. UUID is labeled Domain ID, not a fabricated hostname |
| DNS/TLS/edge/canonical routing | Verification service, certificate lifecycle and host-authority boundaries traced; existing isolated domain tests run | U uses controlled resolvers/providers/edge contracts. Certificate provider and Caddy execution remain disabled. **No live DNS ownership, TLS issuance, SNI proof, redirect activation, canonical promotion or merchant routing certification** |

The domain P fixture never starts verification, issues a certificate, promotes a domain, or publishes events to the actual bus. Fresh observer confirms synthetic tenant/domain/audit absence after rollback. Fake publisher evidence is not RabbitMQ-delivery evidence.

## 7. Confirmed defects and repairs

| ID | Root cause / consequence | Repair and regression |
|---|---|---|
| DASH-01 | Status load error rendered “Not configured”, encouraging setup after an outage | Distinct unavailable message, effective retry; C/U |
| DASH-02 | Analytics errors removed the entire card; empty state had no refresh; overlapping responses could regress state | Visible error/retry, refreshable empty state, current-request guard and tenant dependency, basic response validation; C/H/U |
| SET-01 | Save completion replaced a candidate edited while the request was pending | Snapshot submitted values, accept backend baseline, retain newer local edits/dirty state; U red→green |
| SET-02 | Conflict “load latest” cleared candidate before fetch and announced success even when fetch failed | Explicit fetch result, replace only after accepted read, retain conflict/candidate on failure; U red→green |
| SET-03 | Initial failure had no retry; missing labels exposed translation keys; message had no DOM length constraint | Retry, English/Arabic status/confirmation labels, accessible bounded textarea; H/C/U |
| SET-04 | Shared settings read could complete after logout or tenant change and repopulate an old storefront URL | Scope/request guards and tenant-aware reload; U red→green |
| FIELD-01 | Option row React key included the text being edited; each keystroke remounted input and lost focus | Stable row-position key; real browser continuous typing and U regression |
| FIELD-02 | EMAIL/PHONE configuration excluded length fields during serialization | Preserve/render text-length configuration for those supported types; U regression |
| FIELD-03 | Busy React state alone did not synchronously suppress duplicate submissions; ordering and mutation requests could overlap | Synchronous refs, disabled editing during submission, stale-read invalidation, guarded ordering; U |
| FIELD-04 | Committed write followed by failed list readback still announced success | Distinguish committed outcome from readback failure, close accepted editor, show explicit retry/readback warning; C/U |
| DOMAIN-01 | Domain list service caught query exceptions and returned an empty page, yielding false HTTP-200 portfolio data | Rethrow to controller's existing 500 error contract; disposed-context regression fails before fix and passes after |
| DOMAIN-02 | Wizard blocked its own success navigation; did not render unsaved dialog; lost accepted domain ID on verification setup failure | Release busy before navigation, render shared discard dialog, remember accepted domain, retry only setup/open saved domain; H/C/U |
| DOMAIN-03 | List sort used DTO keys not backend tokens; overview actions were no-ops; Verified could offer promotion rejected by server | Correct sort contract, real safe navigation, align SslActive eligibility, truthful saved-domain/expiring-certificate wording; U |
| DOMAIN-04 | List mutation rejection escaped async click handler | Consume rejection while hook renders error; retain confirmation rather than implying completion; U |
| DOMAIN-05 | History/redirects omitted NOT_FOUND handling and labeled route UUID as hostname | Explicit not-found surface/portfolio return and Domain ID label; two failing regressions repaired; H/U |
| TEST-01 | Certificate renewal tests used fixed August 2026 time while the service uses the real UTC clock; “valid” fixtures had expired by execution date | Derive fixture time from current UTC. Certificate policies/production lifecycle code unchanged; two baseline failures now pass |

Baseline source copies distinguish these additions from prior user changes. Existing domain fixes, authority infrastructure, lint runner, project exclusions, and storefront runtime work were not claimed as new repairs here.

## 8. Commands and results

Commands ran from the repository root unless a working directory is specified. Tool stdout, JSON reports and TRX files are retained in E/; empty successful typecheck output is also represented by terminal completion evidence.

### Backend

Initial command:

```powershell
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PointOfSale.Tests.CustomDomains|FullyQualifiedName~CheckoutFieldDefinitionServiceTests|FullyQualifiedName~CheckoutFieldRuleEngineTests|FullyQualifiedName~OrderCheckoutFieldSnapshotTests|FullyQualifiedName~AdminStoreConfigThemeContractTests" --logger "trx;LogFileName=phase1-baseline.trx" -v quiet -p:WarningLevel=0
```

Result: **292 passed, 2 failed, 1 skipped / 295**. Both failures were expired-date certificate test fixtures; configured rollback theory was not opted in at baseline. The new domain-query regression alone failed before production repair (`phase1-domain-red.trx`).

Final environment and command:

```powershell
$env:STOREFRONT_PHASE1_CONFIGURED='1'
$env:STOREFRONT_PHASE1_TENANT='c9c5c1ae-949e-498e-bc63-07242daeb49d'
$env:STOREFRONT_PHASE1_EVIDENCE=(Resolve-Path '.ai/evidence/storefront-phase1-20261016').Path
$env:SHIPPING_CONFIGURED_ROLLBACK_QUALIFY='1'
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PointOfSale.Tests.CustomDomains|FullyQualifiedName~CheckoutFieldDefinitionServiceTests|FullyQualifiedName~CheckoutFieldRuleEngineTests|FullyQualifiedName~OrderCheckoutFieldSnapshotTests|FullyQualifiedName~AdminStoreConfigThemeContractTests|FullyQualifiedName~StorefrontPhase1ConfiguredTests|FullyQualifiedName~StorefrontPhase1HttpAuthorizationTests|FullyQualifiedName~PosCloseoutHttpAuthorizationTests|FullyQualifiedName~DynamicAddressContractTests" --logger "trx;LogFileName=phase1-backend-final-plus-domain.trx" -v quiet -p:WarningLevel=0
dotnet build PointOfSale.API/PointOfSale.API.csproj -c Debug --no-restore -o .opencode-tmp/storefront-phase1-20261016/api-built -p:UseSharedCompilation=false -nodeReuse:false -v quiet -p:WarningLevel=0
```

- Final selected backend: **453 passed / 453, no skipped or failed tests**. Not the whole backend suite. Intermediate enhanced selections passed 302/302 and 452/452 before later test additions.
- Isolated API build: **passed**, no errors at the command's configured warning level. Build occurred before activation; later additions are test/frontend-only. Active assembly identity independently verified.
- Release tests and isolated API output were not built concurrently into the same output path. Active API binaries were not overwritten by test builds.

### Frontend

In `PointOfSale.Web`:

```powershell
npx vitest run src/pages/StorefrontSettings.test.tsx src/modules/storefront/checkoutFields src/modules/storefront/domains src/components/StorefrontStatusCard.test.tsx --maxWorkers=1 --reporter=json --outputFile=../.ai/evidence/storefront-phase1-20261016/admin-baseline.json
npx vitest run --maxWorkers=2 --reporter=json --outputFile=../.ai/evidence/storefront-phase1-20261016/admin-final-complete.json
npx tsc --noEmit
npm run build
node scripts/lint.cjs src/pages/StorefrontDashboard.jsx src/pages/StorefrontSettings.jsx src/pages/StorefrontSettings.test.tsx src/components/StorefrontStatusCard.jsx src/components/WishlistAnalyticsCard.jsx src/components/StorefrontDashboard.phase1.test.tsx src/contexts/SettingsContext.jsx src/contexts/SettingsContext.phase1.test.tsx src/modules/storefront/checkoutFields src/modules/storefront/domains --format json --output-file ../.ai/evidence/storefront-phase1-20261016/lint-final-complete.json
```

Results: scoped baseline **110/110**; final entire Admin Vitest selection **1,884/1,884**, no skipped/failed tests; typecheck **passed**; final production build **passed** after the last source edits. Focused lint: **0 errors, 2 warnings** (existing mixed-export Fast Refresh warning in SettingsContext and dynamic hook dependency-list warning in useDomainReadQuery). No lint rules were suppressed. Project-wide historical lint debt was not relabeled clean.

In `PointOfSale.Storefront`:

```powershell
npx vitest run --maxWorkers=1 --reporter=json --outputFile=../.ai/evidence/storefront-phase1-20261016/storefront-final.json
npx tsc --noEmit
npm run build
```

Results: **528/528**, no skipped/failed tests; typecheck and production build **passed**. No Storefront source was edited in this task. Existing circular chunk/re-export, mixed static/dynamic import, Browserslist, and bundle-size warnings remain. Admin also retains a large-bundle warning; builds passing is not a claim of warning-free production optimization.

The first whole-Admin run exceeded a 120-second tool limit and was rerun with a 600-second limit; timed-out output was not counted as a pass. Source changes after an earlier pass were followed by the final 1,884-test run/typecheck/build above.

### Browser and runtime

```powershell
playwright-cli list
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase1-hosted.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase1-controlled.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase1-readonly.js
dotnet run --project tests/quality-closeout/RuntimeIdentity -- capture 21272 .ai/evidence/storefront-phase1-20261016/api-active.json
curl.exe --silent --show-error --max-time 30 --output .ai/evidence/storefront-phase1-20261016/readiness-final-observation.json --write-out "%{http_code}" http://localhost:5010/health/ready
```

| Evidence | Final result |
|---|---|
| `E/browser-hosted-final.json` | **28/28 checks**, no failures/uncaught page errors; actual narrow settings/field workflows and restoration |
| `E/browser-controlled-sixth.json` | **13/13 checks**, no failures/uncaught page errors; actual components with controlled HTTP, not live providers |
| `E/browser-readonly-final.json` | **15/15 checks**, no failures/uncaught page errors; final state, compatibility route, all five missing-domain surfaces, list/activity, security negatives |

Earlier script iterations are retained as harness diagnostics, not application passes: wrong heading/button locators, Node's inability to resolve `*.localhost`, and an overbroad test intercept that matched `/src/services/api/`. Public Node requests now use localhost with the explicit tenant Host header. A verbose Playwright resolution error included cookie metadata; that generated evidence was replaced with a non-secret diagnostic (`browser-hosted-third.json`). No credential-bearing browser storage state was saved.

Anonymous requests with only a raw tenant header return **400 / TENANT_REQUIRED** in the hosted middleware, before controller authentication. This is expected secure tenant resolution, not a 401 claim. Controlled real-MVC tests independently assert anonymous 401 and role/authority 403 at their own layer. Hosted authenticated foreign-header spoof returns **403**.

## 9. Preservation and changed files

`E/baseline-git-status.txt`, `E/baseline-scope.diff`, and 62 pre-edit source snapshots preserve the scoped starting point. `E/snapshot-comparison.json` shows **23 intentionally changed existing snapshots, 39 unchanged, 0 missing**. The dashboard was initially clean and has only the scoped logical text-alignment change. New tests/declarations are listed separately in the task manifest.

Preservation check: all **579** pre-existing tracked deletions remain deleted; all **583** baseline untracked entries remain present; index was and remains unstaged/HEAD-equivalent; HEAD unchanged. See `E/preservation-check.json` and `E/final-git-status.txt`. No whole-repository content-hash baseline was taken, so these checks do **not** claim byte-level proof for every unrelated user file. Scoped originals and task-only diffs are the review boundary.

**41 source/test/browser files** were changed or added by this task (report/evidence/build artifacts excluded); full paths and SHA256 values are in `E/task-files.txt` and `E/final-task-files-manifest.json`:

```text
PointOfSale.API/Services/Storefront/DomainReadService.cs
PointOfSale.Tests/Admin/StorefrontPhase1ConfiguredTests.cs
PointOfSale.Tests/Admin/StorefrontPhase1StatusConfiguredTests.cs
PointOfSale.Tests/Domain/Domain56CertificateRenewalSchedulerTests.cs
PointOfSale.Tests/Domain/DomainReadServiceTests.cs
PointOfSale.Tests/Domain/StorefrontPhase1DomainConfiguredTests.cs
PointOfSale.Tests/Security/PosCloseoutHttpAuthorizationTests.cs
PointOfSale.Tests/Security/StorefrontPhase1HttpAuthorizationTests.cs
PointOfSale.Web/src/components/StorefrontDashboard.phase1.test.tsx
PointOfSale.Web/src/components/StorefrontStatusCard.d.ts
PointOfSale.Web/src/components/StorefrontStatusCard.jsx
PointOfSale.Web/src/components/WishlistAnalyticsCard.d.ts
PointOfSale.Web/src/components/WishlistAnalyticsCard.jsx
PointOfSale.Web/src/contexts/SettingsContext.d.ts
PointOfSale.Web/src/contexts/SettingsContext.jsx
PointOfSale.Web/src/contexts/SettingsContext.phase1.test.tsx
PointOfSale.Web/src/i18n/locales/ar.json
PointOfSale.Web/src/i18n/locales/en.json
PointOfSale.Web/src/modules/storefront/checkoutFields/checkoutFields.test.tsx
PointOfSale.Web/src/modules/storefront/checkoutFields/hooks/useCheckoutFields.js
PointOfSale.Web/src/modules/storefront/checkoutFields/pages/CheckoutFieldsPage.jsx
PointOfSale.Web/src/modules/storefront/domains/hooks/useDomainMutations.js
PointOfSale.Web/src/modules/storefront/domains/hooks/useDomainMutations.test.tsx
PointOfSale.Web/src/modules/storefront/domains/pages/AddDomainWizardPage.d.ts
PointOfSale.Web/src/modules/storefront/domains/pages/AddDomainWizardPage.jsx
PointOfSale.Web/src/modules/storefront/domains/pages/AddDomainWizardPage.phase1.test.tsx
PointOfSale.Web/src/modules/storefront/domains/pages/DomainAuxiliary.phase1.test.tsx
PointOfSale.Web/src/modules/storefront/domains/pages/DomainDetailPage.jsx
PointOfSale.Web/src/modules/storefront/domains/pages/DomainHistoryPage.jsx
PointOfSale.Web/src/modules/storefront/domains/pages/DomainListPage.d.ts
PointOfSale.Web/src/modules/storefront/domains/pages/DomainListPage.jsx
PointOfSale.Web/src/modules/storefront/domains/pages/DomainPortfolio.phase1.test.tsx
PointOfSale.Web/src/modules/storefront/domains/pages/DomainsOverviewPage.d.ts
PointOfSale.Web/src/modules/storefront/domains/pages/DomainsOverviewPage.jsx
PointOfSale.Web/src/modules/storefront/domains/pages/RedirectsPage.jsx
PointOfSale.Web/src/pages/StorefrontDashboard.jsx
PointOfSale.Web/src/pages/StorefrontSettings.jsx
PointOfSale.Web/src/pages/StorefrontSettings.test.tsx
tests/browser/storefront-phase1-controlled.js
tests/browser/storefront-phase1-hosted.js
tests/browser/storefront-phase1-readonly.js
```

`git diff --check` passed on the scoped tracked files. Git's existing LF→CRLF notices are not patch errors. Build outputs are generated artifacts, not staged deliverables. No commit was created.

## 10. Remaining limits and stopping point

1. **DNS/TLS/edge readiness remains open:** certificate provider/Caddy disabled; no external issuance, authoritative DNS proof, live redirects/canonical routing, merchant activation, or production-domain cutover was performed.
2. **Runtime reliability remains qualified:** an actual R2 readiness 503 was observed; a later 200 does not certify uninterrupted readiness.
3. **No real order/payment test:** server field admission was executed safely without order creation; inventory, payment, attachment-storage and customer-notification side effects were not exercised.
4. **No shared-store downtime:** maintenance/disabled transitions were confined to rolled-back synthetic SQL state; public message propagation was tested while shared status stayed live.
5. **No global closure claim:** nonempty live analytics enrichment, all role identities on the shared hosted runtime, every downstream cache/projection consumer, exhaustive field-type browser interactions, and long-lived client refresh convergence are not certified by these scoped passes.
6. **Tooling debt remains:** two focused lint warnings and existing build/bundle warnings; no broad lint/config migration was attempted.

Final hosted state: AhramStore **live**, no active QA message, **10** original active checkout definitions (**7 enabled**), **0** custom domains. All configured synthetic transactions rolled back. Phase 1 stops here; **Phase 2 and whole-domain certification remain unstarted/open**.
