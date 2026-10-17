# Shopixy POS Admin — comprehensive quality review

**Date:** 2026-10-15  
**Repository baseline:** `main`, `256339621c0ea49f3602097cdacdf89ffe71754f`  
**Disposition:** reproduced defects repaired with bounded browser, API, service and existing-database evidence. **Not unrestricted production certification.** Two backend assertions remain red, two earlier Storefront cart assertions remain open, external operations remain deliberately unexecuted, and the final permission changes require an API rebuild/restart.

## 1. Outcome

- Repaired theme-editor keyboard validation, same-tick duplicate mutations, failed conflict-reload feedback, and unsafe fallback from incomplete materialized merchant theme data.
- Reconciled the previous theme draft-before-publish failure using **rendered-component behavior**, not a weakened source regex. The editor's **22 tests** pass, including awaited draft acceptance, accepted revision forwarding, failed saves and publication retry.
- Made semantically unchanged theme publication idempotent: no extra version, write or publication event; stale revisions still fail.
- Repaired settings propagation/readback reporting, customer/supplier duplicate submissions, supplier stale-search response overwrite, and misleading shipping-health claims.
- Added homepage-media endpoints to the design permission catalog. Made parcel endpoint attributes explicitly match their existing runtime permissions. Preserved the intentional Owner/Admin attachment restriction.
- **Frontend:** **224 files / 1,744 tests passed**, no failures/skips; production build and `npx tsc --noEmit` passed.
- **Combined bounded backend closeout:** **557 passed / 2 failed / 0 skipped**, **559 total**. The failures are explicitly analyzed in section 7, not excluded or counted as repaired. Earlier passing selections overlap this run and must not be added to its total.
- **Actual configured infrastructure:** 12 synthetic PostgreSQL rollback scenarios, a read-only financial reconciliation and a read-only SQL/Mongo reconciliation all passed; configured SMTP TLS/authentication passed without submitting a message.
- **Browser:** surveyed **104 screens** with no recorded page exceptions or attempted blocked mutations; exercised theme persistence/restoration, negative guards, settings readback, party forms, real order detail/search, and warehouse stock reads. A screen survey is not full workflow certification.
- Original draft override payload is **exactly restored**, live override payload is **exactly unchanged**. No changed live theme, real party record, financial transaction, shipment submission or message was created by this review.
- Existing user work was preserved. No staging, commits, reset, schema migration, database provisioning or shared-record cleanup was performed.

## 2. Scope, environment and safety

This continues the [Storefront quality review](SHOPIXY_STOREFRONT_COMPREHENSIVE_QUALITY_REVIEW_20261015.md), rather than restarting it. Shipping investigation also respects [the carrier connection review](SHIPPING_CARRIER_CONNECTION_UI_FIX_20261015.md).

| Surface | Actual target | Evidence boundary |
|---|---|---|
| POS Admin | `http://localhost:5173/` | Existing Owner session, real hosted reads and bounded reversible/retention-only settings/theme operations |
| API | `http://localhost:5010` | Existing service, safe negative contracts, unchanged-content publication replay |
| Storefront | `http://ahramstore.localhost:5174/` | Browser read of actual published materialization; no renewed cart/checkout mutation audit |
| PostgreSQL | Existing `192.168.1.5:5432/PosEcommerceDb_Baseline` | Read-only observations or isolated synthetic rows inside transactions that roll back |
| WhatsApp bridge | Local port `3000` | Health/account/session reads only; no session activation or dispatch |
| SMTP | Existing configured STARTTLS endpoint | Certificate verification and authentication only; no `MAIL FROM`, `RCPT TO` or `DATA` |

Credentials supplied in conversation were not embedded in scripts or this report. Carrier secrets were neither replaced nor exposed. Existing carrier authentication checks can update test/audit metadata; they are not literally write-free. The settings retention save preserves authoritative values but can likewise update metadata. Draft save/restoration advances the concurrency revision; restoring content does not restore an old revision.

The working tree already contained extensive changes and temporary-file deletions. See [initial Git status](../evidence/pos-admin-quality-20261015/baseline-git-status.txt), initial scoped snapshots and [final scoped diff](../evidence/pos-admin-quality-20261015/final-owned-tracked.diff). Some shared files contain earlier shipping/financial changes: this report claims only the specific hunks listed in section 9. `baseline-adjacent-files.diff` is a mid-review inventory containing repairs, **not** proof of their pre-edit state. The initial owned-file diff was empty for files that were clean at task entry.

### Running-build qualification

The user rebuilt/restarted the API after the theme-publication repair. Process **13696** runs `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`; its adjacent DLL was built at **14:25:44 UTC**, before process start. Real unchanged-content publication replay passed against that host.

The homepage-media permission catalog and parcel attributes were edited later, at **15:19:33 UTC**. The Release API assembly used by automated tests was built afterward, but the observed Debug host still predates these edits. **Their compiled tests pass; their updated hosted enforcement is not claimed. Rebuild/restart is still required.** See [runtime closeout](../evidence/pos-admin-quality-20261015/runtime-closeout.json).

Closeout readiness briefly returned HTTP **503** through `Invoke-RestMethod`; its response body was not retained, so no specific dependency/root cause is assigned. A subsequent captured request and three additional samples returned HTTP **200 / Degraded**, with only disabled `certificate_provider` and `caddy_edge` entries non-healthy. Other reported dependencies were healthy in those samples. This is a bounded observation, not a continuous-availability guarantee. See [captured readiness](../evidence/pos-admin-quality-20261015/readiness-closeout.json) and [three samples](../evidence/pos-admin-quality-20261015/readiness-closeout-samples.json).

## 3. Theme editor and publication invariant

**Trace:** ThemeEditorPage → admitted store identity/materialized draft and live values → local candidate → draft PUT with `If-Match` → accepted response/revision → theme publication POST → SQL version/outbox → Storefront materialization.

### Previous assertion reconciled without relaxing the invariant

The old backend source assertion required exactly `persistDraftTheme({ silent: true })` and publication within the next 500 source characters. The implementation already awaited draft saving, but revision/conflict/concurrent-edit handling changed that source spelling and distance. Merely matching the old regex would not prove that a rejected save prevents publication.

Removed that one obsolete source test from `PointOfSale.Tests/Security/StorefrontProductionBlockerFixTests.cs`, leaving an explicit pointer to rendered coverage in `PointOfSale.Web/src/modules/storefront/pages/ThemeEditorPage.test.tsx`. The two separate cart assertions were not changed. The replacement checks prove:

- Persisted merchant **draft**, not preset defaults or live values, hydrates the editor.
- Missing draft, missing live theme or incomplete draft colors fail closed with recovery UI and no mutation controls; an incomplete nominally successful save response cannot authorize publication.
- Publication waits for draft acceptance and uses its accepted revision.
- HTTP 400, 409 and 503 draft-save rejection prevent publication and retain unsaved input.
- Edits made during save are retained and pause publication; edits made during publication are retained afterward.
- Publication failure is honestly reported and can be retried; duplicate same-tick saves/publications are suppressed.
- Invalid keyboard saves do not bypass field validation, and failed conflict reload does not announce “Latest version loaded”.

These are real rendered React tests with controlled service boundaries, **not hosted changed-content publication tests**. Red-run evidence is retained in `theme-red.json`, `theme-materialization-red.json` and `backend-theme-red.txt`.

### Confirmed repairs

| Defect | Repair | Evidence |
|---|---|---|
| Ctrl/Cmd+S bypassed disabled invalid Save UI | Validate at mutation entry, not only in button state | Component regression and hosted keyboard no-request assertion |
| State-only busy protection allowed same-tick duplicate calls | Synchronous in-flight ref for save/publish | Component tests; intercepted held-save browser check issues exactly one request |
| Failed conflict reload falsely announced success | Return/read actual load outcome before success feedback | Controlled failed-read regression |
| Missing materialized values silently became defaults/live values | Require complete draft/live materialization before accepting read/save response | Three intercepted invalid-read browser cases, each followed by successful real-read recovery |
| Replaying the accepted theme created another publication/version | Compare draft/live preset and semantic JSON before persisting; preserve revision checks | New backend red→green regression and two real HTTP-200 unchanged-content replays |

### Actual hosted journey and restoration

[Theme live evidence](../evidence/pos-admin-quality-20261015/browser-theme-live.json) records **10 passing assertions**: materialized hydration; missing-revision publication rejection (**428**); real draft save (**200**); draft changes without live changes; reload hydration; local preview; one explicitly intercepted failed-save publication guard; revision-safe restoration; real restoration; and unchanged live verification.

No changed-content live publication was attempted. After restoration, draft/live were already semantically identical, so two real publication requests were safe: both returned **200**, with no revision/history/content change ([replay evidence](../evidence/pos-admin-quality-20261015/browser-publish-replay.json)). This does not prove arbitrary changed-publication RabbitMQ/Mongo convergence.

Final [restoration comparison](../evidence/pos-admin-quality-20261015/theme-restoration-closeout.json): original draft JSON exactly restored; live JSON exactly unchanged; original revision `8284278`, current revision `10396258`. No later administrator revision was overwritten to restore the test.

Additional browser evidence:

- [20 guard/layout assertions](../evidence/pos-admin-quality-20261015/browser-theme-guards.json): validation, duplicate-save/error retention, dirty-navigation guard, English/Arabic at **1440 / 820 / 390 px**, candidate preview and fresh unauthenticated direct-route redirect. Two save-failure assertions use interception; no real save/publication occurs in this script.
- [Six materialization assertions](../evidence/pos-admin-quality-20261015/browser-theme-materialization.json): three controlled invalid reads and three real-read recoveries; zero mutations.
- Screenshots: [English mobile](../evidence/pos-admin-quality-20261015/theme-en-390.png), [Arabic tablet](../evidence/pos-admin-quality-20261015/theme-ar-820.png), [restored draft](../evidence/pos-admin-quality-20261015/theme-live-restored.png).

## 4. Other confirmed repairs and authorization findings

| Area | Demonstrated issue / action | Verification and limit |
|---|---|---|
| Settings | Settings page saved/read back its own state but left shared `SettingsContext` consumers stale. Reload shared settings before success. Context saves now return false when readback fails instead of unconditionally reporting success. | Two new rendered regressions; real retention-only save waits for both editor/context reads and preserves values. No intentional merchant settings value change. |
| Customers and suppliers | Rapid submissions could issue duplicate creates/updates; cancellation remained possible while pending. Added synchronous mutation guards, disabled Save/Cancel while pending, preserved input on failure. | Two controlled component creation/retry regressions; browser required-field/search checks plus four intercepted failed-create checks. **No real party was created or edited.** |
| Supplier reads | Slow older search could overwrite newer results or clear current loading state. Added request ownership and unmount invalidation. | New delayed-response regression proves newest result survives. |
| Shipping health | Static webhook liveness was presented as “All Systems Operational”, verified per-carrier webhook availability and a tracking schedule it did not measure. Failed refresh could retain stale healthy content. | State only what the response proves; English/Arabic scope warnings; suppress health display on failed refresh. Two rendered regressions, actual endpoint/UI checks and 1440/390 px screenshots in both languages. No carrier readiness or tracking execution inferred from liveness. |
| Homepage media | Upload/preview had role restrictions but no canonical design-permission mapping. Added `HomepageMedia` to the existing `StorefrontDesignManage` rule. | Manifest and homepage-media service tests pass against compiled code. This is authority consistency/hardening, not a demonstrated anonymous upload exploit; existing Owner/Admin restrictions remain. Updated hosted policy awaits restart. |
| Parcel endpoints | Three bare `[Authorize]` attributes failed admin hardening checks. MVC already assigned view/fulfill permission policies via its convention. Replaced attributes with those same explicit policies. | Manifest/attribute regressions and security hardening pass for these endpoints. Do not misrepresent this metadata repair as discovery of an unrestricted parcel mutation route. Updated host awaits restart. |
| Checkout attachments | Generic order-read manifest grants Managers access, while explicit endpoint role restriction and dedicated security test require Owner/Admin only. | Preserved the stronger restriction; five actual authorization-service role checks allow Owner/Admin and reject Manager/Cashier/Warehouse. Generic manifest assertion still fails and needs a deliberate permission-model reconciliation, not removal of the restriction. |
| Stripe test fixtures | Two existing fixtures supplied production keys but defaulted to sandbox. Corrected their environment assumptions, not provider validation. | Added positive production-mode and negative sandbox/production-key mismatch coverage; mismatched mode cannot enable the method or publish an event. No real Stripe request or enablement. |

Some theme authoring controls/feedback and shipping-health headings remain English in the Arabic UI. The recorded bilingual checks certify tested layout/scope text, **not complete localization or accessibility**.

## 5. Real database and integration evidence

### Existing PostgreSQL — no disposable substitute or destructive fixture fallback

Configured tests resolve the existing Development configuration, with opt-in gates. No `EnsureDeleted`, migration, schema creation or committed synthetic fixture was used by these configured scenarios. Managed/provisioning `ListOrders_Postgres_*` fixtures were excluded; new rollback-only order cases reuse their builders without invoking provisioning support.

**12 synthetic rollback cases passed in the combined run:**

- **6 pre-existing inventory/COD cases verified, not newly authored:** original allocation/consumption, return, mixed availability, expired-COD protection, live COD vs online-payment TTL, and delivered COD physical consumption. See the six parameterized results in `pos-admin-closeout.trx`.
- **4 new theme cases:** draft persistence without live change; changed publication through a controlled publisher; stale revision rejection; publication-event staging failure. Deferred constraints are checked before rollback; fresh observers verify no synthetic tenant/config survives. Publisher/audit/lock boundaries are controlled, so this does not certify distributed event delivery or competing Redis locks.
- **2 new order cases:** Storefront-only and Storefront+CommerceHub list isolation and foreign-tenant detail denial. Synthetic tenants, orders, payment intents and marketplace lineage roll back; fresh observers verify absence.

**Existing-tenant read-only financial test passed:** a RepeatableRead/READ ONLY transaction reconciles the real `FinancialReportService` trial balance to independent SQL, tenant-owned accounts and balanced totals: **34 ledger lines / 9 report accounts**. No journal or financial entry was posted. Other finance/catalog/bulk tests use their established controlled providers, not this existing PostgreSQL test.

**Read-only SQL/Mongo reconciliation passed:** the closeout observation records no discrepancy for shipment/order ownership, duplicate active shipment, origin warehouse ownership, shipping money, stock buckets, reservation balance/lineage, duplicate/foreign settlement membership, cash-clearing balance, journal balance or treasury account ownership. All **12 observed shipment projections match** SQL. The initial Storefront-only reservation join flags **27** rows; additional classification identifies valid CommerceHub lineage for all 27, so they were not deleted or called orphaned inventory. This is a bounded current observation, not full historical or Elasticsearch-product reconciliation. See [closeout reconciliation](../evidence/pos-admin-quality-20261015/current-reconciliation-pos-admin-closeout.json).

### Real external authentication, not external business execution

| Integration | Observed outcome | Not established |
|---|---|---|
| Bosta | Saved Production credential authentication succeeds, HTTP 200; persisted versioned authentication evidence agrees after reload | Shipment booking, rates, tracking, collection, cancellation or remittance; account remains disabled/non-executable |
| DHL | Saved Sandbox credentials return HTTP 400 `INVALID_CREDENTIALS`; UI/current evidence truthfully reports failure | Successful authentication or any shipment operation; account remains disabled/non-executable |
| Aramex / Mylerz | Existing accounts have no saved credentials and are NotTested/NotReady, disabled/non-executable | Provider authentication/execution |
| FedEx | No existing account selected for live verification | All live provider behavior |
| SMTP | Verified TLS certificate, encrypted STARTTLS, successful AUTH (`235`), QUIT | Application message acceptance, inbox receipt, bounce/retry behavior; zero message submission |
| WhatsApp | Bridge healthy, Redis ready; zero active sessions; configured account disabled/disconnected | Message/OTP delivery, receipt, active-channel positive workflow |
| Auth providers | Existing provider configuration read successfully | Google/Facebook handshakes or provider account creation |

Carrier evidence is [unmocked and retained](../evidence/pos-admin-quality-20261015/browser-carrier-live.json); it does not make disabled accounts operational. WhatsApp [bridge health](../evidence/pos-admin-quality-20261015/whatsapp-bridge-health.json) likewise does not certify delivery.

## 6. Coverage map

The source extraction contains **186 route declarations**, including public/super-admin/dynamic routes. The **104-screen** hosted survey targets existing POS Admin screens; it is not coverage of every extracted declaration or dynamic resource. [Full paths/results](../evidence/pos-admin-quality-20261015/browser-route-survey.json) are the auditable coverage list.

| Surface | Reached | Not certified |
|---|---|---|
| Entry/session/tenant/permissions/users | Existing authenticated navigation; fresh guest protected-route redirect; 13 safe hosted API checks; SQL-current-authority/cookie/tenant-interceptor/security tests | Live lower-role multi-account UI matrix, invitations, password reset, ownership transfer, all session revocation paths |
| Products/categories/units/barcodes | Hosted screens and real empty search; full frontend tests; selected clone/import/lifecycle/bulk authorization/validation tests | Actual product/media/price publication, import/export/print output and complete bulk-rollback/concurrency matrix |
| Customers/suppliers/sales reps | Real list/search/blank-validation screens; guarded submit/failure/retry and stale supplier results under controlled HTTP | Successful real create/update/delete and balance effects |
| POS/sales/purchases/returns | Navigation/read survey, existing full frontend regressions; tested stock/finance boundaries | Sale/invoice posting, return/refund/payment, receipt printing and hardware integration |
| Warehouses/stock/opening balances/transfers | Real warehouse stock UI agrees with backend quantity/reserved/available rows (**8 rows** in the available warehouse); unknown warehouse returns 404; rollback-only physical stock cases | Real transfer, adjustment, opening-balance posting, simultaneous fulfillment contention |
| Expenses/accounts/banks/statements/outstanding balances | Hosted screens, tenant-scope financial regressions and real trial balance/SQL reconciliation | Posting, bank/card operations, reconciliation imports, every report's arithmetic |
| Sales/purchase/inventory reports | Read-only screen survey | Every filter, date boundary, pagination/export/print and totals reconciliation |
| Ecommerce overview/orders/customers/blocked phones/recovery | Real list identities (**7 orders**), selected detail/number/tenant consistency, search empty convergence; configured rollback order queries; admin service tests | Order lifecycle/payment/refund/cancel/fulfillment mutation, actual recovery-message dispatch, every dynamic detail |
| Payment methods/manual payments | Existing configuration/status reads, negative provider/environment service tests; queue correctly reports 404 `MANUAL_PAYMENT_CONFIGURATION_MISSING` | Charge, capture, offline approval, refund, settlement or live provider webhooks |
| Promotions/coupons/smart offers/AI | Read-only pages plus existing frontend/admin tests | Committed campaign changes, exhaustive promotion arithmetic, AI generation/provider billing |
| Analytics/pixels/funnels/attribution | Read-only screens and existing tests | Real event ingestion, conversion accuracy, cross-device attribution, consent/deployment verification |
| Shipping methods/admin/shipments/health | Hosted screens, credential authentication checks, truthful liveness UI, permission contracts and SQL/Mongo lineage observation | Booking/quote execution, webhook delivery, live tracking, shipment cancellation/settlement, all country/carrier combinations |
| Storefront theme/status/CMS/navigation/footer/history | Read survey; strong theme behavior and real draft/restoration/replay journey | Changed-content live publication/projection convergence, CMS/navigation/footer edits, destructive rollback and all theme combinations |
| Domains/TLS | Domain list/add/activity reads; readiness examined | DNS change, domain verification, issuance/renewal, edge routing; certificate/Caddy integrations disabled |
| WhatsApp/email/auth-provider settings | Real status/config reads; SMTP TLS/auth; controlled regression boundaries | External message delivery or social login |
| Affiliate/CommerceHub/treasury | Existing read screens, safe lineage/query tests, financial read reconciliation | Affiliate settlement, vendor remittance, carrier cash, COD settlement or transfers |
| UX/recovery/localization/responsive | Added loading/error/duplicate/invalid-input regressions; targeted English/Arabic desktop/tablet/mobile theme and desktop/mobile health checks | All pages at all widths, keyboard/screen-reader/WCAG audit, arbitrary merchant contrast, Firefox/WebKit |
| Build/performance | Full frontend test suite, TypeScript, production assets build | Production preview/browser smoke, load testing, performance budget; large-bundle warning remains |

Survey records no page exceptions and no unsafe-request interceptions firing. Its only failed data request is the manual-payment configuration 404. Expected negative tests in other scripts intentionally generate validation/authentication/failure responses; those must not be conflated with survey errors. [Interaction checks](../evidence/pos-admin-quality-20261015/browser-interactions.json) contain **14 hosted / 4 intercepted** assertions; [operations reads](../evidence/pos-admin-quality-20261015/browser-operations-read.json) contain **6 real-hosted** assertions. [API checks](../evidence/pos-admin-quality-20261015/browser-api-live.json) contain **13** safe checks. Do not add screen count and assertions as a single functional-coverage percentage.

## 7. Exact test outcomes and residual failures

| Run | Result | Interpretation |
|---|---|---|
| Frontend initial | **1,719 passed / 4 timeout failures**, 221 files / 1,723 tests | Two architecture files pass unchanged when rerun with one worker; no invariant weakened |
| Frontend final | **1,744 passed**, 224 files, zero failures/skips | `--maxWorkers=4`; 21 additional cases net |
| Frontend build/typecheck | **Passed / Passed** | Existing large-chunk warning remains; not runtime deployment certification |
| Initial bounded backend | **142 passed / 2 failed**, 144 total | Initial filter; see TRX summary for exact assertions |
| Expanded admin baseline | **157 passed / 2 failed**, 159 total | Stripe fixture/environment mismatches corrected without changing enforcement |
| Earlier main backend selection | **257 passed / 0 failed/skipped** | Overlaps final combined run; excludes historical artifact assertion |
| Security initial | **152 passed / 3 failed**, 155 total | Media mapping, bare parcel metadata and attachment-role conflict |
| Security after fixes | **176 passed / 1 failed**, 177 total | Only preserved attachment-role conflict remains |
| Catalog/finance/bulk selection | **154 passed / 0 failed/skipped** | Includes configured financial read; overlaps closeout |
| Admin + configured PostgreSQL final | **176 passed / 0 failed/skipped** | Includes SMTP/configured reads/rollback scenarios; overlaps closeout |
| SQL/Mongo + rollback order selection | **3 passed / 0 failed/skipped** | Prior concurrent build attempt hit Windows testhost DLL lock; serialized rerun passes |
| **Combined closeout** | **557 passed / 2 failed / 0 skipped**, **559 total** | Latest compiled bounded selection; final command exits **1**, intentionally reported |
| Earlier original-filter rerun | **143 passed / 1 failed / 1 skipped**, 145 total | Historical artifact failure; SMTP opt-in skipped in that run, then actually executed separately/at closeout |

[Backend summary](../evidence/pos-admin-quality-20261015/backend-summary.json) derives skips from actual `UnitTestResult` outcomes: TRX counter `notExecuted=0` alone incorrectly hides the earlier opt-in skip. [Frontend JSON](../evidence/pos-admin-quality-20261015/frontend-final.json), [build log](../evidence/pos-admin-quality-20261015/build-final.txt) and [typecheck log](../evidence/pos-admin-quality-20261015/typecheck-final.txt) retain evidence.

### Two failing assertions in the combined backend run

1. **`PosEndpointPermissionManifestTests.LegacyRoleAttributes_MustNotNarrowCanonicalPermissionGrants`**: `AdminOrdersController.DownloadCheckoutAttachment` has Owner/Admin-only role metadata but inherits general `storefront.orders.view`, which includes Manager. Dedicated `CheckoutAttachmentDownloadSecurityArchitectureTests` explicitly requires Manager denial. Five new role-policy cases verify the stronger restriction. The generic manifest remains red rather than expanding sensitive attachment access to satisfy it. Follow-up should define distinct attachment authority or a justified resource restriction and align UI/policy/tests; do not delete the restriction blindly.
2. **`StorefrontThemeAuthorityCertificationTests.CertificationArtifacts_MustRecordP2GcTruth`**: historical certification document lacks the literal **`PASS P2-GC`**. Its document and assertion were not changed, and no fabricated certification was added. This remains a suite failure, not a runtime defect adjudication.

### Two carried-forward Storefront cart assertions — not reopened or edited

- `StorefrontCatalogPresentationAuthorityCertificationTests.StorefrontCartIntentAndAuthorityMustRemainBackendOwnedAndFailClosed`: source assertion rejects “Showing your last saved cart”.
- `StorefrontProductionBlockerFixTests.CheckoutStore_MustNotClearCartBeforePaymentConfirmation`: source assertion rejects existing COD/MANUALWALLET `clearCart()`; this alone did not demonstrate early online-payment clearing in the previous review.

They remain unresolved under the previous report; they were **not** included as passing tests in this Admin closeout, not silently fixed, and not restarted as a separate Storefront audit. The prior **third** theme assertion is the one reconciled through stronger behavior in this review.

### Tooling and evidence limitations

- Global `npm run lint` fails because ESLint 9 finds no flat `eslint.config.*`; the repository has legacy `.eslintrc.cjs` and references missing TypeScript ESLint dependencies. No package/config migration was made just to green the audit.
- One concurrent backend build collided with an active `testhost` DLL lock. That attempt did not produce a meaningful application result; sequential rerun passed.
- Early browser fixture runs needed harness fixes: avoid an already-handled intercepted response; recognize the Orders empty-state row; wait for an option to be attached rather than visible. Final JSON files reflect rerun assertions, not invented application repairs.
- Full unrestricted backend/integration suite was not run because some fixtures provision/reset databases or perform operations outside the approved safe scope. Passing selected tests are not proof of all production behavior.

## 8. Reproduction commands

From `PointOfSale.Web/`:

```powershell
npx vitest run --maxWorkers=4 --reporter=json --outputFile=../.ai/evidence/pos-admin-quality-20261015/frontend-final.json
npx tsc --noEmit
npm run build
npm run lint
```

The exact combined backend filter/environment is retained in [closeout-command.ps1](../evidence/pos-admin-quality-20261015/closeout-command.ps1). It reproduces the `--no-build` run against the already compiled Release assembly. Supply the explicitly approved existing tenant; compile the current test project first if binaries are missing/stale. It does **not** authorize managed/provisioning fixtures.

```powershell
dotnet build PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore
& .ai/evidence/pos-admin-quality-20261015/closeout-command.ps1 -ObserveTenant <approved-existing-tenant-guid>
```

Browser scripts reuse the existing isolated headed Edge session. They are not a generic destructive E2E runner. Review mutation/restoration boundaries before re-execution; never parallelize commands against the shared session.

```powershell
playwright-cli list
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-routes.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-theme-live.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-publish-replay.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-interactions.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-api-live.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-theme-guards.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-theme-materialization.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/pos-admin-quality-operations-read.js
playwright-cli -s=opencode-ui-test route-list
```

Live carrier evidence reuses `tests/browser/carrier-connection-live-read.js`. Browser context ends with [no active route overrides](../evidence/pos-admin-quality-20261015/browser-closeout-routes.txt); existing session remains open. Individual TRX files are under `PointOfSale.Tests/TestResults/pos-admin*.trx`, especially **`pos-admin-closeout.trx`**. Full logs/JSON/screenshots are under `.ai/evidence/pos-admin-quality-20261015/`.

## 9. Changes owned by this review

Only these changes are claimed; unrelated existing hunks, declarations and shipping fixtures remain user work:

- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`: unchanged-theme publication guard.
- `PointOfSale.API/Security/PosEndpointPermissionCatalog.cs`: **HomepageMedia mapping only**; other mapping edits predate this review.
- `PointOfSale.API/Controllers/AdminShipmentsController.cs`: **three parcel authorization attributes only**.
- `PointOfSale.Web/src/modules/storefront/pages/ThemeEditorPage.jsx` and `.test.tsx`: validation/in-flight/reload/materialization repairs and behavioral regressions.
- `PointOfSale.Web/src/contexts/SettingsContext.jsx`, `src/pages/Settings.jsx`, `Customers.jsx`, `Suppliers.jsx`: settings readback/propagation, duplicate-submit and stale-read repairs.
- `PointOfSale.Web/src/modules/shipping/pages/ShippingHealthPage.jsx`: truthful liveness scope and stale-state suppression; new scope strings only in already-modified `src/i18n/locales/{en,ar}.json`.
- New `Settings.quality.test.tsx`, `PartyEditors.quality.test.tsx`, `ShippingHealthPage.quality.test.tsx` and their required new page declarations (`Settings.d.ts`, `Customers.d.ts`, `Suppliers.d.ts`, `ShippingHealthPage.d.ts`). Existing `SettingsContext.d.ts`/`ThemeEditorPage.d.ts` were not newly authored here.
- `PointOfSale.Tests/Admin/AdminStoreConfigThemeContractTests.cs`: replay regression and partial class for new configured theme tests; `PaymentMethodsAdminSafetyTests.cs`: corrected environment fixtures/negative coverage; `AdminOrderListPostgresTests.cs`: partial declaration only.
- New `AdminStoreConfigThemeConfiguredPostgresTests.cs`, `AdminOrdersConfiguredPostgresTests.cs`, `PosAdminConfiguredFinancialReadTests.cs`, `PosAdminConfiguredSmtpTests.cs`, `Security/PosAdminQualityAuthorizationTests.cs`; new representative cases in `PosEndpointPermissionManifestTests.cs`.
- `PointOfSale.Tests/Security/StorefrontProductionBlockerFixTests.cs`: replacement-coverage explanation in place of the obsolete theme regex; cart assertions untouched.
- `tests/browser/pos-admin-quality-*.js`, this report and task evidence. Existing configured inventory/read-only shipping tests were exercised, not claimed as new implementation.

## 10. Required follow-up

1. **Rebuild/restart the API** to load the final homepage-media/parcel policy edits, then verify appropriate lower-role denial using controlled accounts. The already completed theme-replay host check does not cover later edits.
2. Deliberately reconcile attachment-specific security with the generic order-read permission model and corresponding UI. Preserve Owner/Admin restriction unless an authorized business/security decision changes it.
3. Resolve the historical certification-artifact failure truthfully, and carry forward the two Storefront cart assertions without claiming a green global suite.
4. Investigate the transient readiness 503 if it recurs, retaining its response/dependency details. Address certificate/Caddy configuration before domain/TLS deployment sign-off.
5. Repair lint tooling separately. Retain the successful test/build/typecheck evidence without calling lint passed.
6. Validate successful message delivery, positive provider handshakes, changed publication/projection convergence, and financial/shipping workflows only with expressly approved controlled data/recipients/accounts. DHL credentials need correction; disabled accounts and zero WhatsApp sessions are not operating integrations.
7. Use an approved disposable environment for destructive/committed CRUD, financial/order workflows, concurrent transactions, full carrier/webhook/recovery flows, printing and exhaustive role/browser/layout testing.

**Conclusion:** the demonstrated Admin defects have repairs and explicit passing regression evidence, including safe existing-PostgreSQL verification. The entire application and every live integration are **not** certified defect-free; the remaining failures, restart requirement and unexecuted operations above remain visible.
