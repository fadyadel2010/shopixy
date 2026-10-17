# Shopixy targeted quality closeout

**Date:** 2026-10-15  
**Disposition:** **LIMITED — targeted repairs and hosted activation verified; not global or production certification.**

## 1. Outcome and scope

This closes the outstanding targeted findings in the [POS Admin review](SHOPIXY_POS_ADMIN_COMPREHENSIVE_QUALITY_REVIEW_20261015.md) and [Storefront review](SHOPIXY_STOREFRONT_COMPREHENSIVE_QUALITY_REVIEW_20261015.md), not a new application-wide audit.

| Finding | Closeout |
|---|---|
| Homepage-media/parcel permission edits not loaded by the host | Verified Debug build installed with user-coordinated stop/restart. Running API/Core/Infrastructure identities match the build; hosted Owner and guest contracts verified. |
| Generic order-read permission conflicts with Owner/Admin-only attachments | Added distinct `storefront.orders.attachments.download`, granted only through existing Owner/Admin permission catalogs. Kept explicit Owner/Admin role metadata and added its permission policy. UI uses effective permission and cancels/revokes preview resources when authority disappears. |
| Historical P2-GC artifact assertion | Corrected the diagnosis and replaced mandatory PASS registration in rolling summaries with evidence-qualified artifact checks. Historical conclusion preserved; current disposition explicitly LIMITED. |
| Stale cart represented as usable authority after refresh failure | Preserve only an explicitly unconfirmed reference on transient failure; discard missing/rejected state. A persistent authority flag blocks checkout even after error dismissal; a visible Retry action restores authority only after accepted server data. |
| Blanket source assertion rejects valid COD/manual-wallet cleanup | Replaced only the obsolete prohibition with stronger method-specific submission, failure, retry, response-admission and pending-payment behavior coverage. Existing offline/online business semantics were not weakened or rewritten. |
| ESLint 9 cannot load existing configuration/dependencies | Repaired the executable lint path using explicit legacy compatibility mode and two pinned missing TypeScript ESLint packages. No rule suppression or flat-config migration. Full source lint still reports debt, below. |

**Current passing evidence:** Storefront **500/500**; Admin focused reconciliation **173/173**; pending-page fixture after its typed cross-app bridge **6/6**; browser controlled-component checks **14/14**; hosted theme failure guards **5/5**; hosted API checks **15/15**. Runs overlap and are **not additive**.

Evidence directory throughout: [`.ai/evidence/quality-closeout-20261015/`](../evidence/quality-closeout-20261015/).

## 2. Safety and preserved state

- Reused the existing API on `5010`, POS on `5173`, AhramStore on `5174`, PostgreSQL/Mongo/Redis/RabbitMQ/Elasticsearch and WhatsApp bridge. No replacement database, schema recreation, migration or destructive fixture fallback.
- No staging, commit, reset, clean, stash, broad cleanup, customer-history rewrite, real order placement, charge/refund, financial posting, carrier shipment/quote for an existing shipment, or customer message submission.
- User stopped/restarted only the API. Installation copied compiled executable/assembly/debug/runtime dependency artifacts to its original output directory. Configuration files, logs and `secure-uploads` were excluded; source configuration hashes remained identical.
- Existing Owner session was reused. The supplied Storefront credentials were used only for normal login; login can perform the application's normal session-cart merge. No explicit cart mutation or order submission followed. The authenticated hosted cart was empty. No login password was embedded in a script or report.
- Controlled browser contexts intercept API calls; real hosted mutation probes use unknown IDs, an empty upload or rejected input. The theme guard intercepts its save failure and blocks publication; real draft/live payloads and revision are unchanged in that check.
- Configured SQL write scenarios use synthetic data and rollback with fresh-observer checks. Read-only reconciliation never repairs observed business history. These are not committed business-flow tests.
- The initial workspace was already extensively modified. See `baseline-git-status.txt`, selected `baseline-source/` snapshots, `owned-baseline-review.diff`, and `final-git-status.txt`. Shared files retain earlier user work; only the hunks in section 9 are claimed here.

## 3. Hosted activation and authorization

### Artifact identity

| | Before | Verified replacement |
|---|---|---|
| PID | `6684` | `13648` |
| API process start UTC | `2026-10-15T16:12:14.8710559Z` | `2026-10-15T16:34:01.7690556Z` |
| API MVID | `c9dd3113-d008-4978-a37d-34a3962bc332` | `989426d9-4897-4981-bd1b-e87d5223f0c8` |
| API SHA-256 | `E83EDEEE228359D8B77B46C0972FA3F8AE21F914DF709EFE9BF0D139D62BD7FB` | `574B462A5A1C790FE55E70EA14EFBFEDD5624ECF8F25009DBD4A4C31E317BB0F` |

Original executable remains `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`, content root remains `PointOfSale.API/`, and launch settings remain `Development`, profile `http`, `http://localhost:5010`. The replacement started after all three verified assembly files were built. Final inspection rechecks paths, MVIDs, hashes, file-before-process timestamps, full environment fingerprint and configuration hashes. This establishes running-build identity with process/start/file evidence, not by a source test alone.

See `api-before.json`, `api-built-artifacts.json`, `artifact-install.json`, `api-after.json`, `api-final.json` and `api-final-verification.json`. Backend regressions use a separately rebuilt Release test assembly; they are not misrepresented as tests executing inside the Debug service.

### Actual hosted API

[`browser-hosted-api-verified-host.json`](../evidence/quality-closeout-20261015/browser-hosted-api-verified-host.json): **15 passed / 0 failed**.

- Owner session returns the new effective attachment permission, HTTP **200**.
- Unknown homepage media preview and attachment download reach resource lookup, **404**.
- Unknown parcel read reaches resource lookup, **404**; rejected parcel update reaches validation, **400**; unknown parcel quote returns **409 `SHIPMENT_NOT_FOUND`**, without a carrier operation.
- Empty homepage upload is **400** before storage mutation; existing parcel evidence reads **200**.
- Six cookie-free requests on the verified tenant Host return **401**, covering homepage upload/preview, attachment download and all three parcel routes.
- WhatsApp account read returns **200**, account disabled/disconnected.

An earlier guest probe on bare `localhost` returned **400 `TENANT_REQUIRED`**, including when an inbound `X-Tenant-Id` was supplied. The middleware intentionally ignores untrusted inbound tenant authority. Those requests do not prove the authorization layer. The final guest check uses the actual tenant Host to reach authentication. Both observations are retained; the earlier incorrect expectation of 401 was a harness expectation, not a policy change.

### Controlled role matrix over HTTP

`PosCloseoutHttpAuthorizationTests` uses actual MVC routes, endpoint role/policy attributes, `PosPermissionApplicationModelConvention` and `PosPermissionAuthorizationHandler`: **42 cases** across six routes and seven identities. Authentication claims alone are controlled; a resource filter short-circuits admitted requests before controller activation/model binding, so there are no database/provider calls.

| Identity | Homepage media upload/preview | Attachment download | Parcel read/update/quote |
|---|---|---|---|
| Owner / Admin | Admitted | Admitted | Admitted |
| Manager / Warehouse | 403 | 403 | Admitted under existing view/fulfill grants |
| Cashier | 403 | 403 | 403 |
| Anonymous | 401 | 401 | 401 |
| Owner-shaped identity missing trusted SQL authority | 403 | 403 | 403 |

Admission is **204 from the test filter**, not evidence of a successful upload, business mutation or carrier quote. This is not a live lower-role-account/JWT issuance matrix; live POS verification used the existing Owner account plus a guest. No fake privileged token was issued against the shared host.

## 4. Cart and checkout contracts

`cartStore.requiresRefresh` is separate from dismissible error text. Accepted fetch/mutation responses can restore confirmation; fetch failures, rejected totals and recovered snapshots require refresh. Login merge success/failure and logout reset carry the same flag. `CartSummary` disables entry and shows bilingual recovery guidance; `submitCheckout()` independently refreshes and refuses `CART_REFRESH_REQUIRED` before order submission when authority is absent.

Behavioral evidence includes valid server state, no initial state, 404 and 400/401/403/422 rejection, network/503 retention, inconsistent item count, error dismissal, and successful refresh. The preserved reference is never called an authoritative saved cart.

`checkoutStore.closeout.test.ts` uses the real payment-response admission function, not a stub accepting arbitrary success data:

- COD/MANUALWALLET clear only after an accepted committed order response; manual proof awaits verification, not a fictional PAID state.
- STRIPE/PAYMOB pending responses retain the cart. Failed submission retains cart and request data; retry uses the same request data.
- Foreign-cart response authority is rejected for all four methods, without clearing.
- Same-tick submissions issue only one order request. Cleanup failure after a committed offline response does not falsely turn it into a retryable order-placement failure.
- The actual pending-payment page retains the cart for PENDING, PROCESSING, MANUAL_REVIEW and FAILED; polling failure/retry still retains it; only backend PAID finalizes once.
- Existing completion tests reject missing/mismatched active-order context and keep cleanup failure non-blocking. SQLite `CheckoutRequestIdempotencyCoordinatorTests` separately verify durable claim/replay behavior. A frontend HTTP stub alone is not proof of committed production-order idempotency.

The earlier blanket source prohibition was incompatible with valid existing offline semantics. Its replacement retains online confirmation source guards and adds the behavior above; the COD/manual-wallet implementation was not removed to make the assertion pass.

### Browser evidence

- [`browser-components-final.json`](../evidence/quality-closeout-20261015/browser-components-final.json): **14/14**, actual components/stores with controlled HTTP. Cart confirmation/dismissal/retry/missing/rejected cases; Manager/Cashier/Warehouse no attachment controls or request; Owner/Admin protected preview and download; no page exceptions.
- [`browser-theme-guards-closeout.json`](../evidence/quality-closeout-20261015/browser-theme-guards-closeout.json): **5/5**, actual hosted editor with intercepted failure. Invalid keyboard save makes no request; failed draft cannot publish, retains input; hosted draft/live/revision unchanged. One intercepted save, zero publication requests.
- [`browser-storefront-read-final.json`](../evidence/quality-closeout-20261015/browser-storefront-read-final.json): authenticated Account link, real empty-cart heading and backend **200 / 0 items**, no checkout button. It does not prove nonempty hosted checkout or payment completion.

Early fixtures failed on Node's missing URL/Buffer globals, React default-export shape, duplicate React dispatchers, dialog controls, cross-app TypeScript alias resolution and Node DNS for `*.localhost`. These are retained as harness failures, not passing application checks. HMR-query module introspection did not identify the live cart store reliably; final hosted cart evidence uses DOM plus same-origin HTTP instead. One generated verbose DNS-error log contained a cookie header and was replaced by a sanitized error description, not retained as credential-bearing evidence.

## 5. Certification artifact truth

The historical architecture document **already contained `PASS P2-GC`**. The prior report's missing-document-literal diagnosis was inaccurate. The failing assertion actually required that literal in five rolling `.ai-context` files.

No new PASS was inserted into those summaries. `CertificationArtifacts_MustRecordP2GcTruth` now requires the retained historical title, an explicit **LIMITED** current disposition, named unverified boundaries and nonempty retained report/browser evidence. The historical source conclusion remains intact but cannot be read as renewed production approval.

Unverified: changed-content publication/projection convergence, competing distributed publication locks, exhaustive tenant/browser combinations and unrestricted production readiness. Current source-test success cannot certify these. See `docs/architecture/storefront-theme-authority-certification-findings.md`.

## 6. Existing infrastructure and messaging

Final liveness/readiness and bridge requests return **200**. Readiness is **Degraded**, not fully healthy: `certificate_provider` and `caddy_edge` remain degraded/disabled. PostgreSQL, Redis, MongoDB, Elasticsearch, RabbitMQ, R2 and the reported outbox/payment readiness checks are Healthy in this sample. Health labels are not charge, delivery, settlement or production-availability certification. See `readiness-final.json`, `liveness-final.json`, `whatsapp-health-final.json`.

Configured backend verification passed in the **812 passed / 2 failed / 0 skipped** bounded run. The configured tests use the existing Development configuration without printing secrets:

- SMTP: TLS certificate/STARTTLS/AUTH/QUIT only; **no MAIL FROM, RCPT TO or DATA**, therefore no application message submission or inbox-receipt claim.
- Application messaging contract: validates enabled/TLS/sender configuration and invokes the actual `BaileysWhatsAppTransport` status GET with configured authentication. A status-only handler forbids any send/activation operation. The bridge returns **404** and the application records **Success=false / Unknown** for the missing session. This is an honestly handled negative contract, **not** a successful active-channel status read or positive delivery readiness. See `configured-messaging-contract.json`.
- No explicitly approved safe recipient was established. Login emails are not delivery authorization. No registration/resend/OTP action was used to send to a customer.
- Bridge remains healthy, Redis ready, **0 active sessions / 0 active sockets**; account remains disabled/disconnected. Positive WhatsApp/OTP delivery and receipts remain blocked by channel/session/recipient prerequisites.
- **12 synthetic PostgreSQL rollback cases pass:** six existing inventory/COD/online-TTL cases, four existing theme cases and two existing admin-order isolation cases. The existing-tenant financial read and SQL/Mongo read-only reconciliation pass. All **12** observed Mongo shipment projections match SQL. The Storefront-only join's **27** missing-order/line rows classify as valid CommerceHub lineage, not orphan records to delete. No cleanup/repair of real records was attempted. See `current-reconciliation-quality-closeout-final.json` and individual rollback evidence.
- Prior carrier authentication evidence remains historical: Bosta authenticated; DHL sandbox invalid credentials; other accounts not ready. No shipment, financial/provider operation or new carrier authentication test is inferred from current health or permission probes.

## 7. Exact regression results and remaining failures

Results overlap. Never add reruns or focused subsets to a whole-suite total. [Machine-readable final summary](../evidence/quality-closeout-20261015/final-summary.json) links the exact counts to their run artifacts.

| Run | Passed / failed / skipped | Interpretation |
|---|---|---|
| Final Storefront suite | **500 / 0 / 0**, 58 files | `storefront-closeout.json`, final auth/cart repairs included |
| Admin broad, 3 workers | **1,713 / 36 / 0**, 224 files | `admin-final.json`; not a green run |
| Admin broad, 1 worker | **1,742 / 7 / 0**, 224 files | `admin-final-serial.json`; six Products feed cases, one Header announcement case |
| Admin focused reconciliation | **173 / 0 / 0**, 6 files | `admin-closeout-focused.json`; affected attachment/theme/cart/pending tests plus all tests in both broad-run failing files |
| Pending page after typed fixture bridge | **6 / 0 / 0** | `pending-page-closeout.json`; same six cases, not additional coverage |
| First expanded backend | **811 / 11 / 0**, 822 total | `backend-first-summary.json`: nine explicit disposable-cluster prerequisites, two unrelated architecture assertions |
| Final safe bounded backend | **812 / 2 / 0**, 814 total | `backend-closeout-summary.json`, `quality-closeout-bounded.trx`; excludes only the nine managed PostgreSQL cases from the first selection, retains both unrelated failing assertions |
| Four targeted backend assertions | **4 / 0 / 0** | Attachment manifest, P2-GC evidence, cart authority and payment-clearing assertions all included in the bounded run |
| Controlled HTTP role matrix | **42 / 0 / 0** | Included in bounded run, not added to its total |
| Debug API build / Release test build | **Pass / Pass** | `api-build.txt`, `backend-build-final.txt` |
| Storefront production build | **Pass** | `storefront-build-closeout.txt` includes its TypeScript check |
| Admin production build | **Pass** | `admin-build.txt`; final test-only fixture bridge does not change application bundle |
| Final standalone Admin typecheck | **Pass** | `admin-typecheck-final.txt` |
| Focused lint | **0 errors / 1 warning**, 7 files, exit **0** | `lint-final-focused.json`; retained attachment fast-refresh warning, includes new typed fixture; no rule suppression |
| Full maintained lint | **314 errors / 188 warnings**, 1,121 files, exit **1** | `lint-closeout-project.json`; **0 fatal parsing errors**. Tool executes, source-level findings remain; not called lint-passed |

The seven serial Admin failures had `STACK_TRACE_ERROR` at suite-collected test locations; an isolated unchanged rerun of their files passes within the **173-case** focused run. This supports a run/timing sensitivity diagnosis, not a reproducible repaired product defect. The whole-suite red result is preserved; focused success is not silently substituted for a full green rerun.

Two additional architecture failures in the expanded backend selection remain outside this targeted closeout:

1. `StorefrontCatalogPresentationAuthorityCertificationTests.ProductDetailFieldsMustBeBackendComposedAndFrontendPresentationOnly`: expects the literal `fetchProductBySlug(slug)`.
2. `StorefrontCatalogPresentationAuthorityCertificationTests.ProductVariantResolutionMustBeginAtBackendAuthorityAndFrontendSubmitsOptionCandidatesOnly`: expects `ApplyNoStoreHeaders`.

Neither assertion was changed or excluded. No claim is made that both are harmless stale tests: their intended product/no-store invariants require a separate bounded investigation. The targeted cart assertion in that same class is included and passing.

The nine managed `InventoryReservationLifecycleIntegrationTests.Postgres_*` cases fail before provisioning with `Explicit disposable cluster required before managed shipping fixtures; no configuration fallback.` They were not pointed at shared data. The final safe selection excludes that prerequisite-blocked group explicitly; their first-run failures remain visible, not counted as passes/skips. Rollback-only configured inventory tests are separate and remain included. The unrestricted backend suite was not run.

Initial shell timeouts and failed test-harness collection attempts are not application assertions. Full logs retain intermediate failures. Source lint errors remain source debt, not a reason to disable rules.

## 8. Reproduction and evidence

From `PointOfSale.Storefront/`:

```powershell
npx vitest run --maxWorkers=2 --reporter=json --outputFile=../.ai/evidence/quality-closeout-20261015/storefront-closeout.json
npm run build
```

From `PointOfSale.Web/`:

```powershell
npx vitest run --maxWorkers=1
npx tsc --noEmit
npm run build
npm run lint -- --format json --output-file ../.ai/evidence/quality-closeout-20261015/lint-closeout-project.json
```

The exact final backend filter is `backend-closeout-filter.txt`; the complete environment/command is retained in `run-safe-backend.ps1`. Build current Release tests serially before `--no-build`; do not build into an active testhost's output. These gates **do not authorize managed/provisioning fixtures**. TRX files are in `PointOfSale.Tests/TestResults/`.

```powershell
& .ai/evidence/quality-closeout-20261015/run-safe-backend.ps1 -ObserveTenant <approved-existing-tenant-guid>
```

Browser scripts under `tests/browser/quality-closeout-*.js` reuse `opencode-ui-test`. Read their safety boundaries before rerunning and never parallelize them against the shared session. Use `playwright-cli list` first. Component fixtures intercept every business API request; hosted API checks deliberately avoid real mutation targets.

## 9. Changes owned here

- `PointOfSale.Core/Security/PosPermissions.cs`: distinct attachment permission and canonical registration.
- `PointOfSale.API/Security/PosEndpointPermissionCatalog.cs`: attachment action override only; earlier homepage/parcel catalog work retained. `Controllers/AdminOrdersController.cs`: role-plus-policy attachment attribute.
- `PointOfSale.Web/src/auth/posPermissions.js`, `src/pages/OrderCheckoutAttachmentPreview.jsx` and its tests: permission registration, UI guard, cancellation/cleanup and role regressions.
- `PointOfSale.Web/package.json`, `package-lock.json`, `scripts/lint.cjs`: missing `@typescript-eslint/parser@8.71.2` and `@typescript-eslint/eslint-plugin@8.71.2`, plus explicit legacy lint runner. Existing package versions were not changed by installation; no existing lock packages removed.
- `PointOfSale.Storefront/src/store/{cartStateResolver,cartStore,checkoutStore}.ts`, `components/cart/CartSummary.tsx`, `auth/store/authStore.ts`: cart confirmation/failure state and entry/submit/reset/merge guards. Existing offline clearing preserved.
- Storefront `cartStore.persistence.test.ts`, `checkoutStore.closeout.test.ts`, `checkoutStore.validation.test.ts`, `auth/store/authStore.test.ts`, `commerceAuthority.certification.test.ts`: regressions and confirmed-cart fixture setup. Only new cart recovery strings in the already-modified English/Arabic locale files.
- `PointOfSale.Web/src/tests/storefrontCartStateResolver.test.ts`, new `storefrontCheckoutPending.closeout.test.tsx` and typed test fixture: explicit unconfirmed wording and real pending-page lifecycle tests. The fixture bridges application-local TypeScript aliases; each application's implementation remains checked with its own TypeScript configuration.
- Backend `Security/{PosAdminQualityAuthorizationTests,StorefrontProductionBlockerFixTests}.cs`, new `Security/PosCloseoutHttpAuthorizationTests.cs`, new `Admin/PosCloseoutConfiguredIntegrationTests.cs`: stronger authorization/lifecycle assertions and safe HTTP/status contracts.
- `Architecture/StorefrontThemeAuthorityCertificationTests.cs` and historical architecture document: evidence-qualified artifact contract. Rolling `.ai-context` files untouched.
- `tests/quality-closeout/RuntimeIdentity/`, browser fixtures, scoped evidence and this report. Runtime identity utility is read-only; unused restart/kill logic was removed.

## 10. Remaining deployment limits

1. Resolve full-project source lint debt without disabling rules; treat global lint as failed until then.
2. Stabilize/reproduce the seven broad Admin run failures under controlled load; the focused rerun is green, the whole run was not.
3. Investigate the two additional architecture assertions without weakening product authority or no-store intent.
4. Use an explicitly approved disposable PostgreSQL cluster for the nine managed lifecycle/concurrency cases and unrestricted business-flow tests.
5. Establish approved delivery recipients/channels and an active WhatsApp session before positive application mail/OTP/receipt testing. SMTP authentication and bridge health do not certify delivery.
6. Complete changed-content publication/projection convergence, real provider financial/shipping flows and domain/TLS readiness only under their separate safety prerequisites.

**Conclusion:** targeted security/cart/certification/tooling defects have bounded closeout evidence and the compiled authorization changes are active on the original service. Global lint, broad-suite stability, additional architecture findings and positive external delivery/production workflows remain explicitly unapproved or unverified.
