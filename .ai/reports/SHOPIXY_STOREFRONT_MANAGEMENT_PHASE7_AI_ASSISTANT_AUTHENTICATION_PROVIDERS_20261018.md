# Storefront Management Phase 7 — AI Assistant and Authentication Providers

Date: **2026-10-18**, local UTC+03 (execution artifacts mostly use 2026-10-17 UTC).

## Outcome

**QUALIFIED PASS for the bounded application, configured-storage and controlled-provider workflows. Live-provider certification remains incomplete.** Confirmed defects were repaired and the final API build activated. Final results: **173 selected Backend tests, 1,958 Admin tests, 547 Storefront tests and 80 browser assertions passed**, with no failures/skips in those final selections. Builds, both typechecks and focused lint passed. The separate callback-host test passed; final independent observers passed 3/3, overlapping two tests in the selected suite.

**Important execution exception:** two early chat attempts used an interception pattern that missed the application's `?lang=` suffix. They may have reached the configured remote provider. Their fallback UI is not controlled-provider evidence, and zero external execution/cost cannot be asserted. The interception was corrected and its actual invocation verified on subsequent passes. No further deliberate remote-model requests or external Google/Facebook sign-ins were made. See §7.

Evidence root: `.ai/evidence/storefront-phase7-20261018/`; machine-readable totals: `verification-summary.json`. Filenames below are relative to it unless otherwise specified. Login passwords, bearer/refresh tokens, real provider secrets and decrypted configuration are not included in this report.

## 1. Scope, environment and preservation

- Only Commercial AI Assistant, Authentication Providers and directly related Storefront/API/persistence/security paths were addressed. **No Phase 8**, unrelated remediation, checkout/order creation, payment, shipment creation, advertising-provider changes or WhatsApp mutations.
- Required Phase 1–6 reports were reviewed. Their historical Phase 3 broad-Backend failures, Phase 4 scheduler-fixture failures and Phase 6 external-delivery limitations are **not certified resolved** by this selection.
- Admin: `http://localhost:5173/`; Storefront: `http://ahramstore.localhost:5174/`; API: `http://localhost:5010`. Authorized normal Admin/Storefront credentials were used, not impersonation.
- Existing merchant tenant: `c9c5c1ae-949e-498e-bc63-07242daeb49d`. Configured PostgreSQL: `192.168.1.5:5432/PosEcommerceDb_Baseline`; existing Redis and ordinary application dependencies were retained. No database/schema replacement, provisioning fallback, reset or migration was run.
- Synthetic provider/customer/session SQL scenarios were enclosed in rollback transactions. Hosted UI configuration changes used supported APIs with current-revision restoration. Existing financial history was observed read-only; earlier checkout/payment/shipping creation harnesses were not rerun.
- Baseline HEAD: `256339621c0ea49f3602097cdacdf89ffe71754f`, branch `main`, extensive pre-existing dirty work: 579 tracked deletions, 589 modified entries and 702 grouped untracked entries. The full untracked manifest contains **35,367 paths**; grouped status and full path counts are different measures.
- `baseline-originals.json` records 99 pre-edit source copies. **35 changed, 64 unchanged**, all snapshots retain their recorded SHA256. `task-captured-source.diff`, `task-supplemental-source.diff` and `task-source-paths.json` isolate the captured task changes from earlier dirty work.
- Attribution limit: `Program.cs` and some later shared test/type files lack pre-edit byte snapshots; the additional `chat.ts` copy was taken after its narrow change and is not a valid before-image. Supplemental HTTP/localization/authorization-host originals support their separate diffs. This is not a byte-for-byte preservation proof over the entire repository.
- `closeout-reconciliation.json` verifies unchanged HEAD/index/deleted manifests, all baseline untracked paths still present, unchanged API environment/configuration fingerprints, matching final application assemblies and unchanged protected financial/pixel/cost snapshots. `task-diff-check.json`: exit 0. No staging, commit, stash, reset, checkout, clean, broad delete or discard was performed.

## 2. Actual implemented contracts

### Assistant administration and inference

`/ecommerce/commerce-ai` manages tenant enablement, widget style/position, optional greeting, personality and activity display. It is **not** a provider/model/key editor. Provider credentials, endpoint, model, temperature, token limit, timeouts, fallback and attempt limits come from server `CommerceAI` configuration. Unknown provider/model properties submitted to the narrow experience API are rejected, not silently accepted. Saving configuration and the appearance preview do not execute a model or prove readiness.

- New `PUT /api/admin/store-config/commerce-ai` updates only assistant fields and requires the reviewed StoreConfig revision. Unrelated design/content/operational configuration is not resubmitted by the screen.
- Supported styles: minimal/floating/sales/compact; positions: bottom-left/bottom-right/middle-right; personalities: friendly/professional/sales-focused; greeting limit: 120 characters. Configuration validation also protects the legacy full-store save.
- Runtime is configured for remote **OpenRouter / `deepseek/deepseek-chat`**, not a safe local inference endpoint. No key or endpoint configuration was changed for this task. Tenant AI is restored disabled.
- Actual route: widget → shared Storefront HTTP client → `StorefrontChatController` → tenant SQL enablement/customer authority → `CommerceContextBuilder` → legacy router or configured multi-model adapters → bounded HTTP transport → response validator.
- Responses preserve `isFallback`; inference fallback is HTTP503, not successful inference. Disabled tenant AI returns HTTP403 before context/model execution. Caller cancellation propagates; provider timeout and malformed/empty/truncated/oversized/non-success responses fail safely.
- Context serializes at most 65,536 combined system/user characters; provider HTTP body is bounded at 131,072 bytes. Structured AI output is bounded and draft product/variant/quantity references are validated. Draft output is a suggestion, not a financial order or authorization to submit one.
- Redis admission atomically reserves **provider attempts before execution**, including failed attempts and each fallback attempt. Existing minute/hour/day/30-day keys are retained; these are TTL-based fixed windows, not sliding windows or calendar-month dollar budgets. With rate limiting enabled, unavailable Redis fails closed. Explicitly disabling rate limiting still bypasses admission by configuration. Completed usage is separately recorded atomically; it is not provider billing reconciliation.
- Activity counts have inactivity expiry, not a rolling 30-day reporting window; unavailable counters display unavailable rather than successful zero. Model status now reports configured identities rather than fabricated ready model names, but is not a connectivity probe.

### Authentication providers and application sessions

`/ecommerce/auth-providers` implements only **Google and Facebook SDK credential-completion flows**, not a generic authorization-code/redirect-provider editor. Client ID, write-only encrypted stored secret, supported base scopes, enable/disable and stored-secret replacement are implemented. Arbitrary provider identifiers/scopes and invented request fields are rejected.

- Stored secrets are Data Protection purpose-bound; readback exposes presence only. Blank unchanged secret is omitted by the UI; an all-mask value cannot become a replacement. “Rotate” replaces the application's stored value only: it does not issue/revoke credentials at Google/Meta. The UI and API now say so.
- Provider settings return a timestamp-based revision. Current UI sends `If-Match`; PostgreSQL microsecond-normalized timestamps and EF concurrency checks reject stale tracked writers. **Legacy API callers may omit `If-Match`**; universal mandatory preconditions are not claimed. Existing uniqueness and event/outbox persistence contracts are preserved.
- Public selection requires enabled/configured settings. Google introspection results must match configured audience, accepted issuer, lifetime/not-before, subject/email and the browser nonce. String-form provider booleans are supported. Facebook debug-token app, user subject/type/lifetime and profile identity are checked; a returned Facebook email is not assumed verified.
- Protected five-minute HttpOnly correlation cookie + posted random state bind completion to browser, tenant and provider. Google also receives a nonce. Redis atomic single-use consumption rejects replay and fails closed on dependency failure. Cookies are Secure on HTTPS; local HTTP QA does not certify deployment TLS/domain/SameSite behavior.
- Existing-account auto-linking requires verified provider and local email and no conflicting provider subject. Inactive/corrupt cross-tenant identities cannot obtain a session. No new explicit account-link-resolution UI was introduced; `ACCOUNT_LINK_REQUIRED` directs users to their existing method.
- Account/provider-link/refresh-session persistence is transactional. Application tokens are HttpOnly cookies, not browser response-body/localStorage tokens. Disabling a provider blocks **new** login initiation/completion; existing application sessions survive until normal expiry/logout. It is not mass session revocation.

## 3. Confirmed defects and repairs

| Confirmed problem | Repair / executed regression |
|---|---|
| Google wrong audience/issuer/expired/missing identity could be accepted; string `email_verified` failed parsing | Strict authority/lifetime/identity/nonce checks and boolean converter; `Phase7ProviderTests`, signed callback tests |
| Unverified email could auto-link an account; filtered navigation hid a corrupt foreign-tenant link | Verified-link policy, independent same-tenant customer lookup, inactive-account denial; dedicated identity regressions |
| Misconfigured enabled providers appeared selectable; numeric/unknown providers persisted | Readiness predicate, explicit supported identifiers and bounded allowlisted settings |
| Browser correlation and replay binding were absent | Protected challenge/state/nonce plus atomic replay store; unit, real Redis and real MVC/browser negatives |
| Provider stale settings could overwrite; masks/unsupported scope accepted; rotation messaging claimed external invalidation | Reviewed revision + SQL concurrency, secret/scope validation and truthful replacement semantics; real PostgreSQL encryption/replacement/uniqueness tests |
| AI experience save replaced unrelated StoreConfig fields; invalid experience values accepted | Narrow revisioned endpoint, DTO/service validation, actual hosted/API/SQL isolation checks |
| Fallback flag disappeared in JSON; failed inference returned success; provider bodies/debug output could be logged | Serialized fallback classification, HTTP503 and safe bounded transport/logging regressions |
| Missing credentials still contacted legacy provider; unbounded prompt/context; adapters ignored configured priority/temperature | Pre-request configuration/prompt guards; adapters consume configured priority/temperature/token limits; recording HTTP tests |
| Concurrent AI requests passed non-atomic limits; usage increments were lost; unavailable Redis failed open | Atomic admission/usage Lua and fail-closed admission; real Redis concurrency tests |
| Shared scoped DbContext was queried concurrently; context used stale/incompatible order/customer projections | Sequential readers and explicit tenant/customer-owned current SQL snapshots; canonical order-number regressions |
| Forged visible product IDs exposed private/missing SQL products; zero shipping quote became base rate | Reuse current SQL public-product authority overlay; preserve authoritative zero quote; red/green unit and configured SQL observer |
| Historical context invented integration activation or inferred “never recorded” from absent timelines | Nullable unknown activation facts and qualified absence/confidence text |
| Admin loading failure exposed defaults; duplicate/stale/unmounted work and saved/readback-failed states were unsafe | Load/retry states, request generations, mutation exclusion, fresh readback and workspace boundaries; maintained component tests |
| Provider list refresh silently gave an old editor a newer revision | Capture editor's reviewed revision when opened; regression verifies retained old revision |
| Social SDK duplicate/late/cancelled completions and stale provider reads could change UI/auth state | Single completion, abort/order guards, explicit provider-read failure/retry, scope cleanup; store and controlled actual-component tests |
| Final positive SDK test authenticated but lost success navigation because its own account transition aborted its callback | Store returns explicit accepted/stale outcome; mounted operation guards separate own completion from cancellation; positive, rejected, duplicate and provider-change browser regressions |
| Chat could accept late work across closure/account/config changes and represent unavailable responses as normal output | Abort/request/scope guards, shared cart session, response/fallback checks and transcript/draft clearing; controlled actual-widget tests |

Primary production areas: `PointOfSale.API/Services/AI/`, social/admin provider services and controllers, narrow Admin StoreConfig service/controller/DTO, `Program.cs` service/HTTP-client registrations, provider EF concurrency metadata, both Admin screens, Storefront social auth/chat and related translations/types. No schema migration or earlier financial behavior was introduced.

## 4. Browser evidence — real and controlled boundaries distinguished

Reused registered headed Microsoft Edge session **`opencode-ui-test`**. Task-created transient contexts/tabs were closed; pre-existing user/Admin/Storefront tabs were retained. Browser scripts: `tests/browser/storefront-phase7-*.js`. Neither local fixtures nor HTTP interception are presented as genuine external sign-in/inference.

| Final artifact | Checks | Boundary and exercised behavior |
|---|---:|---|
| `browser-hosted-verified-final.json` | 25 | Actual running Admin/API/configured SQL: AI save/revision/readback/unrelated-field preservation, public widget style/position/Unicode greeting, stale/invalid/unsupported inputs; actual Google settings save/readback without enabling or adding secrets; rejected mask/scope/missing-credential enable; fresh guarded restoration. Exactly one recorded intercepted chat HTTP503. |
| `browser-readonly-verified-final.json` | 25 | Both actual screens in EN/AR, 1440px/390px, correct direction/no document overflow; anonymous401/foreign-tenant403; controlled injected GET503 on each screen followed by normal Retry against real API; zero configuration writes/page errors. |
| `browser-callback-verified-final.json` | 12 | Browser → real MVC/controller/services → configured SQL transaction, with local signed introspection authority: missing cookie/wrong nonce/audience/issuer/expiry rejection; valid application `/me` identity and no elevated role; empty body tokens/HttpOnly cookies; provider disable/session policy/logout/replay. **Not Google sign-in.** |
| `browser-controlled-expanded-verified-final.json` | 11 | Actual source chat/social components, all internal HTTP and SDK boundaries intercepted: fallback, close/cancel/late response, account transcript clearing, nonce, dismissed prompt, duplicate callback, rejection without success, unmounted callback, accepted completion/success once, provider-change loading recovery. **Not hosted credential validation/model inference.** |
| `browser-password-login-closed-final.json` | 5 | Actual hosted Storefront password login for authorized persisted account; safe return path, disabled social selection, HttpOnly tokens, real logout, `/me`401 and stale refresh401. Isolated browser context; no credentials in result. |
| `browser-disabled-chat-final.json` | 2 | Fresh public disabled configuration followed by actual chat HTTP403 before the model route. |

Total: **80 passing final browser assertions**; no failed assertions/uncaught page errors in these final artifacts. Screenshots: `commerce-ai-ar-mobile.png`, `auth-providers-ar-mobile.png`. Responsive/direction checks are bounded layout checks, not a complete visual or assistive-technology audit. Hosted provider secret replacement/enable/disable were not performed on the merchant; configured rollback tests cover those service/persistence paths instead.

## 5. Configured persistence and independent observers

- `configured-provider-rollback.json`: real PostgreSQL stores ciphertext rather than the synthetic plaintext; purpose-bound decryption agrees inside the test, omitted secret preserved, explicit replacement works, revisions advance, stale DTO and stale tracked writer rejected, duplicate tenant/provider unique index enforced, foreign tenant excluded, enable/disable readback agrees. Ephemeral test protection keys and synthetic credentials are used only inside rollback scope; this is not deployment key-rotation certification. Fresh observer confirms synthetic tenant/settings absent.
- `configured-ai-rollback.json`: narrow AI change preserves unrelated business fields, rejects stale revision, stages persistence/events atomically and rolls back; fresh observer confirms absence.
- `configured-signed-callback.json`: locally RSA-signed fixture is verified at the controlled introspection boundary, then actual service/controller validates authority and issues a real SQL-backed application session. Negative nonce/audience/issuer/expiry/signature/not-before/inactive cases, replay, cookie/body handling and logout covered. `configured-callback-cleanup.json` confirms rollback absence.
- `callback-host-ready.json` / `callback-host-cleanup.json`: final loopback browser host used synthetic tenant `0436dacb-c3cc-4acc-bcc0-ec43d98f33c3`, customer `3f44c23a-e623-4cfa-be6c-ab53d35f65da`; fresh observer finds the tenant absent after rollback. Browser fixture has its own real MVC host, not the merchant's production OAuth settings or live provider.
- `configured-redis.json`: 20 concurrent AI admissions admit **exactly2**; 10 concurrent usage writes retain **10 requests /30 tokens**; 20 replay consumes admit **exactly1**. `configured-redis-cleanup.json` verifies exact manifested keys removed; no merchant counters/broad Redis flush.
- `configured-context-public-authority.json`: read-only existing SQL recomposes a real public product's stale projected values and excludes a missing SQL product. Unit cases separately cover private/draft/missing products and zero shipping.
- Invocation evidence is archived under `configured-invocations/<UTC>/`; top-level configured files are the latest invocation. No earlier-phase fixture cleanup was performed.

## 6. Final verification and reproducible commands

| Check | Result / artifact |
|---|---|
| Relevant legacy Backend baseline | **29 passed /11 failed /0 skipped**, `PointOfSale.Tests/TestResults/phase7-baseline.trx`; failures include legacy source/documentation certification assertions |
| Initial new defect regressions | **0 passed /12 failed**, `phase7-provider-red.trx` |
| Final selected Backend/configured run | **173 passed /0 failed /0 skipped**, `PointOfSale.Tests/TestResults/phase7-backend-closeout.trx` |
| Separate browser callback host | **1/1**, `PointOfSale.Tests/TestResults/phase7-browser-callback-verified-final.trx`, plus the separate 12 browser assertions |
| Final independent observers | **3/3**, `PointOfSale.Tests/TestResults/phase7-preservation-final.trx`; two overlap selected Phase7 tests, one is the earlier read-only financial observer |
| Full Admin baseline → final | **1,946 →1,958 passed**, zero failed/pending; `admin-baseline.json`, `admin-closeout.json` |
| Full Storefront baseline → final | **542 →547 passed**, zero failed/pending; `storefront-baseline.json`, `storefront-closed-final.json` |
| Typechecks | `npx tsc --noEmit`, exit0 in both frontends; `admin-typecheck-closed-final.log`, `storefront-typecheck-closed-final.log` (empty success logs) |
| API isolated Debug build | exit0; `api-build-closeout.log` (warning output suppressed by command) |
| Production frontend builds | exit0; `admin-build-closeout.log`, `storefront-build-closed-final.log` |
| Focused Admin lint | 5 files, **0 errors /0 warnings**, `admin-lint-closeout.json` |
| Focused Storefront lint | 8 files, **0 errors /0 warnings**, `storefront-lint-closed-final.json`; Storefront-specific transport policy, not the POS-only apiClient rule |
| Final browser groups | **80/80**, classified above |

Final Backend command (saved filter: `backend-closeout.filter.txt`):

```powershell
$env:PHASE7_CONFIGURED='1'
$env:PHASE7_EVIDENCE=(Resolve-Path '.ai/evidence/storefront-phase7-20261018').Path
$filter='(FullyQualifiedName~Phase7&FullyQualifiedName!~Phase7BrowserCallbackHostTests)|FullyQualifiedName~StorefrontAuthProviderAdminServiceOutboxTests|FullyQualifiedName~StorefrontAuthenticationLifecycleTests|FullyQualifiedName~ApiRequestNormalizationFilterTests|(FullyQualifiedName~AdminStoreConfigThemeContractTests&FullyQualifiedName!~ConfiguredPostgres&FullyQualifiedName!~Phase1Configured)'
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter $filter --logger 'trx;LogFileName=phase7-backend-closeout.trx' -v quiet -p:WarningLevel=0
```

The explicit legacy theme exclusions avoid rerunning prior-phase opt-in mutation fixtures; they are not Phase7 failures repaired by exclusion. Earlier `phase7-backend-final.trx` had 164 passed/3 such skips; it is superseded by the final selection above. This is **not the entire Backend suite**, nor a rerun proving the 11 baseline certification failures or historical Phase3/4 failures fixed.

- Admin: `npx vitest run --maxWorkers=2 --testTimeout=30000 --reporter=json --outputFile=../.ai/evidence/storefront-phase7-20261018/admin-closeout.json`.
- Storefront: `npx vitest run --maxWorkers=1 --reporter=json --outputFile=../.ai/evidence/storefront-phase7-20261018/storefront-closed-final.json`.
- API: `dotnet build PointOfSale.API/PointOfSale.API.csproj -c Debug --no-restore -o .opencode-tmp/storefront-phase7-20261018/api-built-closeout -p:UseSharedCompilation=false -nodeReuse:false -v quiet -p:WarningLevel=0`.
- Frontends: `npm run build -- --outDir ../.opencode-tmp/storefront-phase7-20261018/admin-dist-closeout` / `storefront-dist-closed-final` in the corresponding project directories. No existing output directory was recursively emptied.
- Focused lint: Admin `node scripts/lint.cjs src/pages/CommerceAI.jsx src/pages/Phase7Settings.test.tsx src/modules/storefront/pages/AuthProvidersPage.jsx src/modules/storefront/services/authProvidersApi.js src/App.jsx --format json --output-file ../.ai/evidence/storefront-phase7-20261018/admin-lint-closeout.json`. Storefront `node ../PointOfSale.Web/scripts/lint.cjs src/auth/components/SocialLoginButtons.tsx src/auth/api/authApi.ts src/auth/hooks/useAuth.ts src/auth/store/authStore.ts src/auth/store/authStore.test.ts src/components/chat/ChatWidget.tsx src/types/chat.ts src/services/api/httpClient.ts --no-ignore --config .eslintrc.cjs --resolve-plugins-relative-to ../PointOfSale.Web --format json --output-file ../.ai/evidence/storefront-phase7-20261018/storefront-lint-closed-final.json`.
- Callback fixture: explicit `PHASE7_CONFIGURED=1`, `PHASE7_BROWSER_HOST=1`, `PHASE7_EVIDENCE` and `dotnet test ... -c Release --no-build --no-restore --filter 'FullyQualifiedName~Phase7BrowserCallbackHostTests' --logger 'trx;LogFileName=phase7-browser-callback-verified-final.trx' -v quiet`; while bounded host is ready, run `playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-phase7-callback.js`. It is not part of ordinary test execution.
- Final read-only observer: set `PHASE6_CONFIGURED=1`, `PHASE6_STAGE=final`, `PHASE6_EVIDENCE` to this Phase7 evidence directory, plus Phase7 opt-in variables; run `dotnet test ... -c Release --no-build --no-restore --filter 'FullyQualifiedName~ExistingMerchantAndPriorFinancialHistory_IndependentReadOnlyObserver|FullyQualifiedName~Phase7FinalObserverTests' --logger 'trx;LogFileName=phase7-preservation-final.trx' -v quiet`. **Do not run the other Phase6 mutation tests.**
- Workspace/runtime reconciliation: `node tests/quality-closeout/phase7-closeout.mjs`. **Do not rerun `phase7-baseline.mjs` after edits**, because it overwrites the before-images.

Build warnings remain: large bundles, existing Storefront circular/mixed imports, outdated Browserslist data and tooling deprecations. Focused lint is not full-repository lint. Initial Storefront lint under the Admin configuration reported two inherited direct-Axios policy violations; auth error detection was moved behind its existing HTTP boundary and a Storefront-specific configuration preserves the import restriction. Its first run required explicit shared plugin resolution, then found an inherited `any` at that boundary, narrowly typed before the final pass. No security rule was disabled.

## 7. Failed attempts, limitations and external prerequisites

Failed evidence is retained, not replaced by an earlier green run:

- Initial provider/AI red tests, followed by additional red credential/context/adapter tests (**3 failed /1 passed**), private-product cases (**2 failed**) and zero-quote case (**1 failed**), all covered by the final green selection.
- Configured tests exposed a real PostgreSQL timestamp precision mismatch, repaired with microsecond revisions; another failure was a test observer missing explicit tenant predicates. A subsequent corrupt-link regression reproduced the production required-navigation filtering defect and was repaired.
- Callback browser first attempt lacked a required test-host service registration; final corrected host/browser passes. A host test pass alone was not treated as proof that browser assertions ran.
- Hosted first attempt used an over-exact emoji-bearing locator; later attempts had the wrong chat textbox and/or an interception mismatch. `browser-hosted-second.json` and `browser-hosted-third.json` show fallback but no interception receipt. The corrected `**/api/storefront/chat**` pattern records one receipt on both later successful hosted passes. The old unconditional `noExternalInference` property in failed harness output is **not reliable proof** for those attempts.
- `merchant-ai-usage-observation.json` reads **1 completed request /5,018 tokens**, model `deepseek/deepseek-chat`, from the merchant's current daily usage key. There is **no before-snapshot**, so provenance, provider receipt and charge cannot be attributed or excluded. No provider billing/log API was queried and no counter was erased. Thus the intended zero-external-call safeguard failed for the early attempts; the report does not claim zero spend or a successful live inference test.
- Expanded actual-component positive SDK test exposed the task's own callback-cancellation regression. The intermediate fix also suppressed its rejection callback. Both failures remain in `browser-controlled-expanded-first.json` / `browser-controlled-expanded-final.json`; final corrected expanded artifact passes11/11 and the final full Storefront suite/build/typecheck/lint were rerun.
- Initial baseline collection encountered unsupported PowerShell path APIs and then Node buffer limits. Valid nested source originals/manifests were subsequently captured before production edits; stray flattened copies are not the authoritative originals.

**Still unverified:** intentional genuine remote-model success/quality/billing, live Google/Facebook consent/SDK/credentials and production callback-domain behavior, deployment Data Protection key lifecycle and complete repository-wide regression status. These require separately authorized provider-ready runs with explicit budget/network containment and real provider access; do not infer them from saved configuration, a synthetic signed authority or intercepted HTTP. No external OAuth console changes, arbitrary third-party sign-ins or uncontrolled follow-up spending were performed.

## 8. Restoration, retained rows/history and loaded API

- Original merchant AI business fields: **disabled /minimal /bottom-right /greeting off /empty greeting /friendly**. Final hosted restoration compared the current candidate to the owned marker, read a fresh revision, wrote only original AI fields and independently reread. Final SQL observer agrees; rowVersion **10663116**. Timestamps/revisions/audit activity advanced legitimately and were not backdated.
- Both providers began absent/unconfigured/disabled. Hosted Google editing created row **`ad8d2c4a-9f8c-4799-b936-8616216fc4bb`**. It is intentionally **retained**, disabled, empty Client ID/scopes, no secret; final revision **639278764407145590**. Facebook remains absent/default-disabled. Behavioral restoration is not restoration of physical absence. Supported update history/outbox/audit metadata is retained; no undocumented physical-delete cleanup was invented.
- Normal browser analytics, assistant-open telemetry, operational audit/outbox history, login/logout metadata and revoked application-session history may remain. Merchant counters/usage were not reset. Configured synthetic SQL tenants/settings/customers/sessions rolled back and fresh observers confirm their scoped absence; exact synthetic Redis keys were removed.
- Independent existing-history observer confirms four protected prior orders and four associated journals unchanged and balanced, including legitimate COD recognition/settlement distinction. Fingerprint remains **`3BB567CD03E17A5BF139FDCCE5937E140600D91A8A8D93E5D14DB1147D342979`**. Prior Phase6 cost/pixel snapshots also match. This is a scoped observer, not a claim of a whole-database checksum.
- WhatsApp account **`138379f5-7754-4c20-8863-b3b7aad28888` remains disabled**, status4; never modified.
- User stopped/restarted the API for each authorized activation. Nine backed-up artifacts were copied and SHA256-verified, with **no configuration-file copy/change**. Final process **PID13212**, started **2026-10-17T23:26:43.2233243Z**, normal `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`, Development, `http://localhost:5010`.
- `api-active-closeout.json`, `api-built-closeout.json`, `api-install-closeout.json` and reconciliation verify all three observed application assemblies' SHA256/MVID against the final isolated build, plus unchanged baseline environment fingerprint and all seven configuration-file hashes. Build success alone was not treated as loaded-build proof.

| Assembly | Final MVID | SHA256 |
|---|---|---|
| API | `0ab547e2-87de-46b6-baae-c4548d8c1e26` | `6A1B7D7307433867925C728CBADC1B75BBCCA920B6B20CC84FB8750A1191229B` |
| Infrastructure | `941a114d-b8f6-4d76-9cc1-3d69781c15f2` | `D30FB0063ADB8419103AC22CB96EC63BED3EA7B546A3E849CA33F0E7533F1C23` |
| Core | `87d15ce7-620c-4b7e-8462-9f1c6f91b0c7` | `51238140396685BCE3F97607F97322471BB61A08A9C581F791C3BC881DDF0645` |

`/health/ready`: HTTP200, **Degraded**, not fully Healthy; existing disabled certificate-provider/Caddy-edge integrations remain qualified. No threshold/integration was changed to turn health green. All task changes remain uncommitted and unstaged. Stop at Phase7; no Phase8 work is authorized by this report.
