# Manual Wallet Storefront visibility remediation — 2026-10-20

## Executive verdict

**Verified in the primary development application: the missing-option condition is a canonical runtime-environment rejection, not a Storefront rendering or missing-recipient mapping defect. The misleading Admin readiness presentation is fixed.** A valid, enabled QA wallet configured for live mode disappeared from the public catalog because this development runtime permits **Sandbox**. The separate wallet configuration endpoint returned **409 `MANUAL_PAYMENT_ENVIRONMENT_MISMATCH`**. Aligning the tenant mode with the permitted runtime immediately exposed both COD and `MANUALWALLET`, without bypassing validation or changing global provider settings.

Admin previously reported persisted activation and configuration validity but omitted execution readiness. It now displays the existing canonical checkout-readiness result and gate separately, including actionable environment-mismatch guidance. **Sandbox is not activation**: a production wallet can be ready with Sandbox off when the production runtime is permitted; the new backend regression covers that case too.

Actual post-build **headed Microsoft Edge** testing completed Admin configuration, public availability, checkout selection/review, refresh, disable/re-enable, stale checkout rejection, a new no-coupon wallet checkout and real UI approval, accounting/projection readbacks, and independent COD checkout with wallet disabled. Latest focused backend tests: **129 passed, 0 failed, 0 skipped**. Separate source-contract tests: **43 passed, 2 existing failures**, detailed below.

**Final configuration safely restored: Manual Wallet disabled, Sandbox true, public configuration `{}`, encrypted recipient absent, version33. No synthetic recipient remains configured.** COD remains enabled/version13. Two clearly marked QA orders and one posted balanced journal are retained.

Certification boundary: this identifies the exact disappearance point in the authorized development reproduction. It does **not** establish the separate screenshot environment's runtime configuration. No tester-machine access or operations occurred.

## 1. Environment, preservation, and original state

Evidence paths below are relative to **`.ai/evidence/manual-wallet-visibility-20261020/`**.

| Concern | Observed identity |
|---|---|
| Workspace | `C:\Users\fadya\source\repos\PointOfSale`, primary workspace only |
| Admin | `http://localhost:5173/ecommerce/payment-methods`; supplied AhramStore Owner account |
| Storefront | **`http://ahramstore.localhost:5174`**, supplied existing customer; never Nissan |
| Tenant | AhramStore, `c9c5c1ae-949e-498e-bc63-07242daeb49d`, EGP |
| API | Development `http://localhost:5010` |
| Database | Existing configured PostgreSQL `192.168.1.5 / PosEcommerceDb_Baseline`; configured Mongo read projections retained |
| Initial runtime | API PID21300; Admin Vite PID16656; Storefront Vite PID16332, all executable/module paths in the primary workspace |
| Initial API DLL SHA256 | `867126D2BBCF4612528DA5348242082C00EE199C4C5A6739CA0AFDFF69EC6E37` |
| Final tested API | User stopped/restarted the normal API after its Debug build. PID14504 started after the new DLL was written |
| Tested/final API DLL SHA256 | `B4280A4CD5ED19BF7035EA990B05E5F67BA454BB7DF9B0AEA69B3BEDFA99BD6D`, identical in `runtime-loaded.json` and `runtime-final.json` |

Both frontend production builds completed before the final headed workflows. The actual browser uses the existing Vite development applications, reloading the final implementation after the API restart; this is not claimed to be a production-preview-server test. No alternative frontend/API/database was provisioned.

Before changes, inspected working-tree status and recorded the five existing modified registration files and SHA256 hashes. All five remain byte-identical (`existing-work-hashes.json`, `integrity.json`). Existing untracked registration helpers/report/evidence were not overwritten. Reviewed the preceding Manual Wallet lifecycle, coupon, shared checkout, theme/media, and registration reports and relevant source. Preserved shared recipient validation, net pricing/settlement, SQL checkout revalidation/locking, fresh-scope checkout/payment admission retries, pending-wallet order exclusion, theme fixes, and the current phone-free registration work.

Original SQL observation (`sql-before.json`, REPEATABLE READ / READ ONLY):

| Method | Row ID | Enabled | Sandbox | Encrypted config | Version |
|---|---|---:|---:|---|---:|
| Manual Wallet | `8596894a-df67-44c5-8e0e-f64372bc1d74` | false | true | absent | 22 |
| COD | `5fd1c166-73cd-4235-a127-9efffad10b23` | true | true | absent, not required | 13 |

Manual Wallet public configuration was `{}`; shared validation returned **“Manual wallet number is required.”** No verified original recipient existed. Stripe and Paymob had no tenant rows; their configuration was not created or changed. Existing wallet display name `P5QA20261017-817 TEST ONLY Wallet` and other metadata were preserved.

This safe initial state is **not** used to diagnose the supplied screenshot. Supported Admin UI temporarily saved synthetic **`01000000000`**, tested missing/malformed input, and enabled the wallet. No screenshot recipient was used. Saving alone left activation false; the separate Enable action was required (`browser-config.json`).

## 2. Complete availability trace and confirmed cause

| Boundary | Evidence and conclusion |
|---|---|
| Admin authoritative enabled state | `PaymentMethodsAdminController` reads tenant-scoped SQL. QA readback had `isEnabled=true`, `isConfigurationValid=true`, public `walletNumber` present, Sandbox false. Activation was genuinely enabled, not inferred from the dialog toggle |
| Persisted configuration shape | SQL stores `ConfigEncrypted`, protected with purpose `PaymentMethodSecrets`. The configuration dictionary key is **`walletNumber`**. The shared `PaymentMethodConfigurationValidator` derives the allow-listed public recipient. SQL QA observations confirm encrypted presence; final SQL confirms absence. No private credentials were exposed |
| Shared validation | Existing `PaymentMethodConfigurationValidator.Validate` enforces the established 8–20 digit, phone-shaped contract; no country/operator prefix was added. Admin, read repository, runtime readiness, and checkout continue to reuse it |
| Tenant resolution | Actual StoreConfig response identifies AhramStore and the same tenant ID as SQL/Admin. Storefront Host is AhramStore. Foreign-header HTTP probes reject403; configured two-tenant rollback test passes |
| Read repository | `PaymentMethodReadRepository.GetEnabledPaymentMethodsAsync` first loads tenant-owned, enabled, nondeleted SQL rows, decrypts/validates configuration, then reconciles Mongo version/enabled/readiness/public fields. Stale or absent projection cannot invent authority |
| Code mapping | Canonical `MANUALWALLET` is explicitly supported by the public controller and checkout; code normalization trims/uppercases. Accounting's wallet alias is not the public catalog identity. No mapping/alias defect was found |
| Runtime prerequisites | `StorefrontPaymentRuntimeReadinessService.EvaluateManualPaymentAsync` verifies the recipient and existing initialization/evidence/upload/review/proof/approve/reject capabilities through the existing admission authority |
| **Exact disappearance point** | `ManualPaymentTenantConfigurationResolver.EnsureTenantEnvironment` rejects a tenant `IsSandbox=false` against configured `Environment=Sandbox`, `SandboxMode=true`. It throws `MANUAL_PAYMENT_ENVIRONMENT_MISMATCH`. The readiness service returns that blocked gate; `StorefrontPaymentMethodsController` records it in `unavailableRuntimeGates` and filters the wallet before DTO mapping |
| Direct runtime corroboration | Real `GET /api/storefront/payments/manual-wallet/config` returned **409 environment mismatch**, then200 with the same synthetic recipient after mode alignment (`browser-diagnose.json`) |
| Public response security | Catalog intentionally returns usable method metadata and `isAvailable=true`, not the recipient or encrypted/provider credentials. Storefront does not need a recipient in the catalog to render the option. The established dedicated wallet config endpoint provides permitted customer instructions |
| HTTP caching | Actual responses already carry `private, no-store, no-cache, max-age=0, must-revalidate`, Pragma/Expires and Surrogate-Control, set by `StorefrontHttpCachePolicyMiddleware`. No stale HTTP cache defect was reproduced; no extra cache or invalidation system was added |
| Projection/save freshness | Supported save/enable/disable publish versioned projection events; reads reconcile current SQL authority. Successive saves and enable/disable were immediately reflected in fresh public reads and Storefront reloads |
| Storefront admission/rendering | `admitStorefrontPaymentMethods` accepts the existing envelope and validates metadata; `PaymentStep` renders admitted methods and applies availability/amount limits. It does not require the recipient in the public list. Actual option radios contained COD and wallet when the backend returned them |
| Out-of-order frontend reads | Existing AbortController/request-sequence guards and reset cancellation remain. Added regression proves an older wallet response cannot overwrite a newer disabled/empty response. Payment methods are not persisted as eligibility in localStorage |
| Independent checkout authority | `CheckoutService.ValidatePaymentMethodAsync` reloads SQL, rejects disabled methods, reruns shared configuration validation and canonical runtime readiness, and retains locked commit authority. Direct stale browser-originated checkout returned400 `INVALID_PAYMENT_METHOD`; SQL confirmed zero task orders at that checkpoint |

The development Manual Payment configuration was already enabled with certified local capabilities, required instructions/proof/expiration, Vodafone Cash channel, Sandbox runtime, and production rollout disabled. **No global Manual Payment, Stripe, Paymob, or environment settings were changed to force availability.**

### Real public response comparison

Full response bodies/headers are retained in `browser-diagnose.json`, `browser-verify.json`, `browser-restore.json`, and `browser-closeout.json`.

```json
// Enabled + valid recipient, but tenant live mode against development Sandbox:
{"status":"AVAILABLE","paymentMethods":[{"code":"COD","isAvailable":true}],
 "configuredCount":2,"enabledCount":2,"availableCount":1,"totalCount":1}

// Same wallet enabled, valid, aligned Sandbox mode:
{"status":"AVAILABLE","paymentMethods":[{"code":"COD","isAvailable":true},
 {"code":"MANUALWALLET","isAvailable":true}],
 "configuredCount":2,"enabledCount":2,"availableCount":2,"totalCount":2}

// Final restored disabled/empty wallet:
{"status":"AVAILABLE","paymentMethods":[{"code":"COD","isAvailable":true}],
 "configuredCount":2,"enabledCount":1,"availableCount":1,"totalCount":1}
```

These are abbreviated real bodies, not proposed contracts or mocked responses. Backend suppression of an unready method remains correct. The code fix addresses Admin's missing runtime-readiness information, not an imaginary need to bypass that policy.

## 3. Exact source changes and rationale

| File | Change |
|---|---|
| `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs` | Inject existing `IStorefrontPaymentRuntimeReadinessService`; enrich fresh list/single reads with additive `isAvailableForCheckout` / `checkoutUnavailableReasonCode`. Reuse shared configuration result and canonical runtime authority. No mutation semantics, secrets, schema, or public catalog contract changed |
| `PointOfSale.Web/src/pages/PaymentMethods.jsx` | Display checkout readiness separately from persisted Enabled and Valid badges; explain environment mismatch in list and wallet configuration dialog. Existing success path performs fresh reads after save/enable/disable, with existing stale-read guards |
| `PointOfSale.Web/src/i18n/locales/en.json`, `ar.json` | Localized readiness labels/environment guidance |
| `PointOfSale.Tests/Admin/ManualWalletCheckoutReadinessTests.cs` | New real canonical-readiness tests: Sandbox aligned, mismatch, production aligned with Sandbox off, disabled, missing and malformed recipient; fresh list/single parity and foreign-row isolation |
| `PointOfSale.Tests/Storefront/StorefrontPaymentMethodAvailabilityTests.cs` | Environment-blocked wallet is omitted while COD remains and enabled/available counts differ truthfully |
| `PointOfSale.Web/src/pages/PaymentMethods.test.tsx` | Enabled/valid versus checkout-unavailable presentation; explicit ready presentation without using Sandbox as activation |
| `PointOfSale.Storefront/src/store/checkoutStore.validation.test.ts` | Admit wallet metadata without recipient/private configuration; reject older in-flight response after newer disabled state |
| `PointOfSale.Tests/Infrastructure/PaymentMethodRuntimeTestStub.cs` | Shared explicit ready dependency for existing controller fixtures |
| `PointOfSale.Tests/Admin/{PaymentMethodsAdminSafetyTests,PaymentMethodsAdminSyncReliabilityTests,Phase5PaymentConfigurationTests,Phase5ConfiguredTests}.cs` | Constructor wiring only for the added canonical-readiness dependency; existing assertions unchanged |
| `PointOfSale.Tests/Payments/{ManualWalletConfiguredTenantTests,FawryFinancialAuthorityContainmentTests}.cs` | Constructor wiring only; existing assertions unchanged |

Task-only helpers: `tests/browser/manual-wallet-visibility-20261020-{baseline,config,diagnose,verify,prepare,refresh,stale,submit,approve,isolation,restore,cod,closeout}.js`; `tests/manual-wallet-visibility-20261020-{browser,observer,mongo,integrity}.py`; `tests/quality-closeout/manual-wallet-visibility-20261020-{backend,capture}.ps1` and `manual-wallet-visibility-20261020-lint.mjs`. Evidence and this report are task-owned additions.

Readiness fields are deliberately nullable on mutation DTOs: a successful write is not falsely presented as proof of runtime readiness. The established Admin mutation workflow immediately reloads authoritative list data. No second recipient-validation implementation was introduced. No migration was necessary or applied.

## 4. Mandatory headed-browser results

`playwright-cli list` confirmed registered **`opencode-ui-test`**, headed msedge with isolated in-memory profile. Reused that session and existing supplied-account sessions, verified the expected Storefront customer. All commands had finite timeouts. No mocked payment/configuration success, real transfer, external provider transaction, or carrier booking was used.

| Scenario | Actual result and evidence |
|---|---|
| Original Admin/SQL state | Disabled/version22, recipient absent. `sql-before.json`, `browser-baseline-corrected.txt` |
| Missing/malformed recipient UI | Empty, malformed and too-short values blocked before save; edits remain. `browser-config.json` |
| Save/reopen/activation | Synthetic recipient saved and accurately reopened; save alone leaves disabled; separate Enable persists true. Sandbox toggled independently. `admin-qa-config.png`, `browser-config.json` |
| Actual post-fix mismatch | Admin Enabled/Valid remain truthful while new readiness says unavailable with exact environment gate; public only COD. `admin-runtime-mismatch.png`, `browser-verify.json` |
| Actual post-fix aligned readiness | Admin reports checkout available; public returns both methods; no secrets/recipient added to catalog. `admin-runtime-ready.png`, `browser-verify.json` |
| Real checkout option list/review | Both radios visible/selectable. Wallet selected, real marked PNG uploaded, review total35 confirmed before submit. `checkout-both-options.png`, `wallet-review-before-submit.png`, `wallet-final-review.png` |
| Checkout refresh | Wallet remains in the refreshed option list. Reload requires reentry/reupload of transient proof fields under the established flow; no proof-persistence redesign claimed. `wallet-after-checkout-refresh.png`, `browser-refresh.json` |
| Save/config freshness | Changing only Sandbox through supported UI flips actual public availability immediately without API/frontend restart. `browser-verify.json` |
| Disable/re-enable | Admin disable → fresh public/UI excludes wallet, COD remains. Re-enable valid aligned wallet → refreshed UI includes it again. `checkout-wallet-disabled.png`, `browser-stale.json` |
| Stale direct checkout | Actual candidate with valid owned cart/session, warehouse authority, expected35 and proof selected before disable →400 `INVALID_PAYMENT_METHOD`; no committed task order. `browser-stale.json`, `sql-stale-refused.json` |
| Stale normal UI submit | Place Order refreshes authoritative eligibility, marks selection stale, refuses order submission. `stale-wallet-refused.png`, `browser-stale.json` |
| No-coupon wallet checkout | Real UI POST200, **01081**, authoritative quote/order/intent35, no page exception. `browser-submit.json`, `wallet-pending-result.png`, `sql-pending.json` |
| Pending Manual Payments / ordinary Orders | Real pending row visible; ordinary exact-order search empty before approval. `manual-payment-pending.png`, `ordinary-orders-before-approval.png`, `browser-approve.json` |
| Real Admin approval | Approve→Confirm200; intent Paid, order Confirmed/Collected then Processing/Collected; ordinary Orders visible after approval. `ordinary-orders-after-approval.png`, SQL/Mongo final observations |
| Actual proof readback | Initial modal screenshot was captured while loading, **not** certified as loaded proof. Subsequently fetched the same retained PNG via actual authorized proof endpoint200 and rendered it in a headed browser tab, verified image decoding and screenshot. `actual-retained-proof-readback.png`, `browser-isolation.json`; visibly TEST-ONLY — NOT A REAL RECEIPT |
| Invalid active replacement | Empty/malformed API configuration replacements return400, preserving enabled valid recipient/version. `browser-isolation.json` |
| Tenant mismatch | Five Admin configuration/order/decision/proof requests403; three raw Storefront foreign-header requests403; wallet unchanged. `browser-isolation.json`. Actual two-tenant configured rollback test additionally passes |
| Restore original safe state | Supported confirmed reset200 → disabled, empty public config, missing-recipient Enable400. `final-wallet-disabled-empty.png`, `browser-restore.json` |
| COD independent with wallet disabled | Real UI checkout200, **01082**, total35, Pending/unpaid; no wallet intent or journal. Ordinary Orders row visible. `browser-cod.json`, `cod-review-wallet-disabled.png`, `cod-completed-wallet-disabled.png`, `cod-ordinary-orders.png` |
| Subsequent navigation/readback | Normal catalog navigation and fresh public read still onlyCOD, Admin wallet disabled/empty. `browser-closeout.json`, `runtime-final.json` |

No failed/timed-out order submission was blindly repeated. The only deliberately rejected checkout preceded creation of either new order; SQL independently recorded zero task orders then. Each successful new wallet/COD checkout was submitted once.

## 5. Automated tests, builds, typechecks, and lint

### Passed

| Check | Result | Evidence |
|---|---|---|
| Latest focused backend configuration/readiness/catalog/Admin safety/read authority, tenant/security, storage, lifecycle/settlement/refund guards, checkout retry and configured two-tenant rollback | **129 passed,0failed,0skipped** | `backend-final-tests.log`, `trx/backend-final-tests.trx`, exact filter/result JSON |
| Additional Manual Payment runtime/environment, wallet/COD admission and payment-selection source tests | **43 passed**; two failures separately below | `architecture.log`, `trx/architecture.trx` |
| Admin PaymentMethods + ManualPayments selected regressions | **25 passed,3files** | `admin-tests.log` |
| Storefront checkout validation/closeout including new privacy/race tests | **41 passed,2files** | `storefront-tests.log` |
| Backend isolated initial build | PASS,0errors;492 existing warnings | `backend-build.log` |
| Normal development API Debug build actually loaded for UI | PASS,0errors;87 warnings | `backend-development-build.log` |
| Final isolated backend/test build after production-mode test addition | PASS,0errors;291 warnings | `backend-final-tests-build.log` |
| Admin production build / separate typecheck | PASS / PASS | `admin-build.log`, `admin-typecheck.log` |
| Storefront production build (`tsc && vite build`) / separate typecheck | PASS / PASS | `storefront-build.log`, `storefront-typecheck.log` |
| Admin changed-file lint | PASS,0errors;5 existing hook/Fast Refresh warnings | `admin-lint.log` |
| Changed Storefront test + all13 task browser helpers | PASS,14files,0errors/0warnings | `focused-lint.json` |
| Backend changed controller whitespace verification | PASS, verify-only, no edits | `backend-format.log` |
| Python helper compilation | PASS; only four task-generated `.pyc` files subsequently removed by exact path | executed `python -m py_compile` |
| Git diff whitespace | PASS, actual Git exit0; line-ending warnings retained | `diff-check-result.json`, `diff-check.log` |
| Independent configuration/financial/projection/preservation checks | **21 passed,0failed** | `integrity.json` |

Counts are not added to overlapping intermediate runs. The first backend run was128/128; the final129 includes the added production-mode case. Some initial frontend command arguments named a nonexistent optional mapping file; only the actual discovered **2files/41tests** are counted, not an invented third file.

### Failed — retained, not rewritten

Separate source-contract run: **2failed /43passed /0skipped**, unchanged assertions:

1. `AdminPaymentMethodReadAuthorityCertificationTests.PaymentMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped`: expects the already-absent `SetNoStoreHeaders();` in unchanged `StorefrontPaymentMethodsController`, line39. Actual common middleware response headers are explicitly no-store and were captured; no controller helper was added merely to satisfy this string assertion.
2. `StorefrontPaymentMethodSelectionAuthorityCertificationTests.StorefrontPaymentMethodFrontend_MustSubmitCandidatesOnly`: expects literal `selectedPaymentMethod.displayName` in unchanged `ReviewStep.tsx`, line43. Existing review presentation does not contain that literal. No unrelated presentation/source assertion was rewritten.

Historical coupon/report-source failures described by prior tasks were not rerun or edited. Full-repository green certification is not claimed.

Harness/setup observations retained rather than attributed to production defects: an early baseline used nonexistent `/api/storefront/config` and received401; corrected to `/store-config`. An initial checkbox click hit the styled overlay and timed out before persistence; switched to its actual label and verified original state before continuing. An initial wallet-config diagnostic omitted `/payments` and received401; the corrected endpoint produced the decisive409/200. Early direct import of an unversioned checkout module read a second HMR module instance; subsequent diagnostics import the actual resource URL. An initial SQL observer assumed OrderItems had TenantId; corrected to join the tenant-owned Orders parent, still read-only. None was a committed-order retry or a hidden application500.

### Blocked / unexecuted — not passes

- Original screenshot tenant/build/configuration diagnosis: not accessible within this primary-workspace-only authorization. No tester machine, remote shell, remote browser, or tester database copy was accessed.
- Separate second-tenant browser account sign-in: no such credentials provided. Executed configured two-tenant rollback/service boundaries and real HTTP mismatch tests instead.
- Full disposable-database wallet/COD concurrency integration fixtures: not run, honoring the prohibition. No disposable database was provisioned/reset. Real current-database wallet approval/COD and safe selected regressions ran.
- A new live in-flight availability response deliberately delayed across an Admin disable was not separately injected in browser. Added deterministic Storefront deferred-response regression covers the existing sequence/cancellation guard; actual browser disable/refresh and server stale-candidate rejection were exercised.
- Missing-recipient legacy **enabled** row was not manufactured by bypassing Admin or SQL constraints. Missing/invalid configuration is covered by shared-validator/readiness tests, real invalid-write/enable400s, safe reset, and disabled stale-checkout rejection.
- No real transfer, external Stripe/Paymob transaction, carrier booking, full provider certification, new coupon scenario, exhaustive runtime fault injection, or full repository lint/suite was executed. Instapay is not an enabled channel in the configured development runtime; Vodafone Cash was the actual channel exercised.

## 6. Retained synthetic QA and financial readbacks

Both orders use existing `CERT-MEDIA-S-20261007`, one item25 + Standard Shipping10, **EGP35**, no coupon/discount/tax/payment fee. No catalog prices or stock quantities were directly edited. Customer notes clearly mark `MWVIS20261020`, TEST ONLY, no transfer/no shipment.

| Purpose | Order | ID | Payment intent | Final observed state |
|---|---|---|---|---|
| Wallet UI checkout/approval | `ORD-C9C5-2026-01081` | `f17895bc-b5b0-43fb-87c9-cdb9da69494d` | `82d45236-e358-4415-a129-f9a804e7c2d1`,35EGP | Processing/Collected; intentPaid |
| COD after restoration | `ORD-C9C5-2026-01082` | `b6cff444-d225-458e-808d-48e0cc3faaab` | none | Pending/Pending, unpaid |

Wallet proof object:

`tenants/c9c5c1ae-949e-498e-bc63-07242daeb49d/manual-wallet-proofs/checkout-sessions/79ab01e6-7d2a-4ee1-9c5a-255d7c4ba4d9/7fc9f85c52684b928ca3a7987120c6f5.png`

Actual uploaded file: `TEST-ONLY-NOT-A-REAL-RECEIPT.png`, visibly **TEST-ONLY — NOT A REAL RECEIPT**, states no transfer/no delivery. Earlier test uploads to that same QA checkout session were superseded during refresh/stale tests and left to established retention, not broadly deleted. Retained earlier keys include `36b59b5cc8d9449ea44f98bd2576a2a3.png` from `browser-refresh.json`; the accepted order binds the exact final key above.

Accounting: exactly one posted `Order_Wallet` journal **`01a15a6a-7c5f-71c0-89e8-3af13abe486d`**, debit1140=35 / credit4020=35. Ledger lines `01a15a6a-7c7c-74cc-865b-1630406243c6` and `01a15a6a-7c8d-77c7-960b-aef299c69aa4`. Processed WalletApproved outbox **`01a15a6a-79a6-784b-b952-8afa272ec8ab`** points to this same journal. Net item PaidAmount25; shipping is covered at order level, not overallocated to merchandise. COD has zero payment intents, settlement journals, and approval accounting outbox records.

`sql-pending.json`, `sql-approved.json`, `sql-final.json`, `mongo-final.json`, and `integrity.json` independently confirm quote/order/intent equality, balanced journal, pending exclusion/accepted progression, and both tenant-owned projection totals/statuses. Posted accounting history was not deleted, rewritten, or reversed to hide QA.

## 7. Restoration and final stop state

| Stage | Enabled | Sandbox | Public recipient | Encrypted config | Version |
|---|---:|---:|---|---|---:|
| Original | false | true | absent /`{}` | absent | 22 |
| QA mismatch reproduction | true | false | synthetic only | present | 24; later28 |
| Final aligned QA checkout | true | true | synthetic only | present | 31 |
| **Final confirmed reset** | **false** | **true** | **absent /`{}`** | **absent** | **33** |

Original metadata preserved; legitimate version/audit progression was not rewritten backward. Failed missing-recipient Enable did not change the final version. Public catalog remains COD only across final navigation/readback. The five pre-existing registration edits are byte-preserved. No staging, commit, reset, clean, stash, checkout-discard, broad restore, database replacement/recreation, schema migration, unrelated record mutation, or historical evidence redaction was performed.

The configured development API remains running on PID14504 with the tested DLL. Frontend processes remain the original primary-workspace Vite instances. This task stops here: merchant activation requires deliberate valid configuration and alignment with the permitted runtime, not a bypass or automatic enable.
