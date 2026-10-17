# Shopixy Storefront — comprehensive quality review

**Date:** 2026-10-15  
**Repository baseline:** `main`, `256339621c0ea49f3602097cdacdf89ffe71754f`  
**Disposition:** confirmed defects repaired and targeted regressions passing; **not an unrestricted production certification**. The user restarted the API and safe hosted contracts pass. Positive hosted registration/OTP delivery, disposable PostgreSQL integration, and external delivery remain incomplete.

## 1. Executive result

- Repaired account-navigation foreground/accessibility, a tablet navigation gap, configurable-product actions, registration verification dispatch/reporting, verification confirmation layout, phone-country submission, an unsafe phone-status fallback, and several recovery/retry defects.
- Storefront tests: **57 files / 473 tests passed**, versus **54 files / 459 tests passed** before these changes.
- Storefront production build: **passed**, including TypeScript. Existing circular-chunk, bundle-size, Browserslist and test-tool deprecation warnings remain.
- Focused backend: **96 passed, 0 failed, 0 skipped**. Includes an actual loopback SMTP exchange, EF InMemory service tests, and SQLite inventory/payment lifecycle tests. These are not PostgreSQL or real-provider delivery evidence.
- Broader backend: **174 passed, 3 failed, 0 skipped, 177 total**. The remaining failures are source assertions against files not edited by this review; details below. They were not disabled or weakened.
- Browser: **143 isolated real-component scenarios**, plus **10 hosted theme/product navigation and backend variant-resolution checks** and **6 safe hosted API contract checks after restart**. See machine-readable evidence for assertions and errors; these layers are intentionally distinguished.
- No staging, commits, shared customer/order/inventory cleanup, real payment execution, carrier submission, external email delivery, or WhatsApp dispatch was performed by the review.

## 2. Baseline, boundaries and safety

The working tree already contained extensive modified, deleted and untracked work. In particular, COD-expiration policies and integration regressions existed before this task. They were preserved and tested, not claimed as newly implemented fixes.

Baseline evidence:

- [`baseline-git-status.txt`](../evidence/storefront-quality-20261015/baseline-git-status.txt)
- [`baseline-scope.diff`](../evidence/storefront-quality-20261015/baseline-scope.diff)
- The attempted whole-working-tree diff timed out amid many tracked temporary-file deletions. Its incomplete, approximately 71 MB artifact was removed; it is not used as evidence.

Targets identified:

| Surface | Target | Scope used |
|---|---|---|
| Storefront | `http://ahramstore.localhost:5174/` | Hosted catalog reads, client navigation and memory-only theme overrides |
| POS Admin | `http://localhost:5173/` | Storefront-dependent source/configuration investigation only; no theme/settings publication |
| API | `http://localhost:5010` | Existing shared host; not silently replaced |
| WhatsApp bridge | local port `3000` | Traced integration; no message dispatch |

The original shared API was launched from an older DLL under `.opencode-tmp/shipping-cross-domain-20261015/`. The user subsequently restarted it. The new listener is process `8564`, running `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`; its adjacent API assembly was built at 16:57:57, after the final phone-service edit, and the process started at 16:57:59. Safe hosted contracts and all ten catalog/options checks were rerun successfully after restart. This establishes a refreshed build and bounded live HTTP evidence, **not successful hosted registration or real email/OTP delivery**.

Post-restart `/health/live` returns HTTP 200 / Healthy. `/health/ready` returns HTTP 200 / **Degraded**: `certificate_provider` and `caddy_edge` are degraded; PostgreSQL, Redis, MongoDB, Elasticsearch, RabbitMQ and the listed outbox/messaging checks report healthy. Health checks are operational indicators, not transaction correctness tests. The healthy shared PostgreSQL connection is not permission to bypass disposable-fixture safeguards.

Existing authenticated browser tabs were preserved. Browser fixtures used fresh contexts with synthetic auth/configuration state and intercepted HTTP. Credentials supplied in conversation were not written into test scripts or this report. Synthetic email tests use `example.test`; the SMTP test binds only to loopback on a dynamically allocated port.

## 3. Five reported areas

### A. Theme account navigation and icons — repaired

**Trace:** theme header → `LayoutRuntime` capability slot → `AuthNavItems` → auth store/router.

**Evidence:** desktop auth children forced `text-white/*`, overriding light-header foregrounds. Icon-only account/logout controls lacked accessible names. Two added SSR regressions failed before the repair. Cosmetics additionally hid its menu at `md` while desktop navigation/account controls appeared only at `lg`, leaving a 768–1023 px gap.

**Changes:** inherit the owning theme's foreground; named account/logout controls and current-color focus rings; keep Cosmetics' menu available until the desktop controls appear.

**Verification:** two SSR regressions pass. Real header components pass 10 active themes × English/Arabic × guest/authenticated fixture × 1280/820/390 px = **120 combinations**, including opening mobile/tablet menus and guest login/register client routes. `electronics` is disabled/experimental and was deliberately excluded. This is not a claim of WCAG contrast certification for arbitrary merchant palettes, keyboard/screen-reader certification, or real authenticated sessions in every theme.

### B. “Select options” — repaired

**Trace:** backend availability DTO (`VARIANT_SELECTION_REQUIRED`) → shared `ProductCardRuntime` control panel → product detail → `useVariantSelector` → product variant-resolution API → current product authority.

**Reproduction:** the hosted configurable cards displayed passive “Select options” stock text beside a disabled Add to Cart control. Image/title navigation worked, but the apparent options action did not.

**Changes:** render a localized, affiliate-aware options link when the backend explicitly requires selection. Preserve the theme's action/focus styling through `ThemeActionLink`. Keep unavailable simple-product controls disabled and do not manufacture frontend purchasability. Add a synchronous in-flight guard to simple-product quick-add.

**Verification:** two SSR action regressions pass. In **all ten active themes**, the actual hosted options link navigates to the expected product detail and the backend resolves a variant with HTTP **200**. The resolution POST was inspected in `StorefrontProductsController` / `StorefrontProductService` and allowed as a read-only operation. All other unsafe API requests were blocked in that fixture. No shared cart addition or inventory reservation was attempted. Rapid-click quick-add protection was reviewed but not certified through a hosted cart mutation.

### C. Registration, verification email and confirmation layout — repaired

**Trace:** `RegisterForm` → auth store/API → `StorefrontAuthController` → `StorefrontAuthService` → customer persistence/registration event → `StorefrontEmailVerificationService` → hashed expiring token → `IEmailSender` → SMTP.

**Root causes:**

1. Account registration persisted the customer and published a registration event, but never requested verification email. The registration consumer projected account data; it did not send the missing message.
2. Storefront verification caught SMTP failures without rethrowing. Unconfigured/disabled email senders also returned successfully. Callers could falsely report “sent”.
3. The banner used a viewport `sm` horizontal layout inside a narrow registration card. Fixed-width icon/action elements squeezed the text.
4. Registration intentionally returned no authenticated tokens, but its banner offered a resend endpoint requiring authentication.
5. An unconditional three-second redirect removed the confirmation/recovery instructions.

**Changes:**

- Request verification after account persistence. Keep account-creation success separate from email-dispatch success through nullable `verificationEmailSent` response metadata. Registration remains unauthenticated; SMTP failure does not tell the customer that a committed account failed to exist.
- Propagate dispatch failure; disabled/unconfigured senders fail honestly. “Sent” means transport acceptance, never inbox delivery.
- Carry the response through frontend types/store/hook/form. Show dispatch failure/unknown state accurately.
- Use a container-safe grid with a separate action row; guest confirmation links to sign-in/account for authenticated resend. Keep the confirmation until the user chooses a next step.
- Clean up banner cooldown timers. Verify resend response success before showing success.

**Verification:** initial email tests were **3 failed / 2 passed**; final email service suite is **8 passed**. It covers success/failure dispatch while preserving unauthenticated account creation, token hashing, tenant mismatch, sequential single use, expiry, resend limits/cache failure, and disabled senders. The actual SMTP adapter separately passed a controlled loopback multipart SMTP test. Browser coverage includes six full registration submissions/confirmation layouts (two languages × three widths), six narrow-banner layouts, resend failure→success, and persistence beyond the former redirect timer. All registration HTTP in those browser cases is intercepted; no hosted customer was registered.

Screenshots include [`registration-en-390.png`](../evidence/storefront-quality-20261015/registration-en-390.png) and [`registration-ar-820.png`](../evidence/storefront-quality-20261015/registration-ar-820.png). These are captured test fixtures, not comparisons to user-supplied screenshots; none were supplied.

### D. WhatsApp OTP and phone validation — repaired demonstrated inconsistencies

**Trace:** shipping country/prefix/input → checkout address state → OTP status/request/verify APIs → tenant-aware phone normalizer → tenant/session/phone OTP store → feature gate/account selection → sensitive WhatsApp event → delivery consumer/bridge. Checkout independently enforces phone verification before order creation when policy requires it.

**Findings and changes:**

- The input displayed the address country's dialing code while storing raw national digits. Backend normalization used the tenant default country, so the displayed and submitted identities could disagree. Valid national inputs now become E.164 using the displayed country on blur and continuation; incomplete input remains unchanged. A foreign explicit international number does not receive a contradictory prefix.
- `CheckVerificationRequiredAsync` fell back to stripping characters and prepending `+2` after authoritative normalization rejected input. A new regression seeded a cached identity produced by that fallback and reproduced incorrect verified status (**1 failed before repair**). Removed the fallback and require a valid normalized identity before accepting persisted customer phone verification.
- Manual OTP verification could race the scheduled automatic verification. Cancel the scheduled callback and use an in-flight request guard. Associate the code field with its label.

**Verification:** frontend phone regressions cover EG, SA, AE, US, explicit GB international input, `00` international input and incomplete/invalid input. Five actual ShippingStep browser cases confirm canonical checkout state. The real OTP component's manual/automatic overlap produces exactly **one** verification request. Focused backend tests cover normalizer country behavior plus OTP persistence/event publication, unavailable delivery channel, resend cooldown/rotation, rate limits, invalid/expired codes, attempt exhaustion, and rejected-normalization status.

**Limits:** the precise tester phone/country/configuration was not supplied, so its individual rejection cannot be diagnosed conclusively. No actual WhatsApp message, bridge receipt or delivery acknowledgement was generated. Service tests use controlled OTP stores/feature gates, not a production Redis/WhatsApp stack. The restarted hosted tenant currently returns `isRequired: false, isVerified: true`, matching the existing no-required-channel policy; the enabled-channel workflow is covered only by controlled tests. Disabled/unavailable OTP-channel policy was preserved rather than changed into unconditional verification.

### E. Overnight order cancellation — baseline protection verified; historical incident unproven

**Trace:** checkout/order reservation → inventory reservation expiry → `InventoryReservationCleanupJob` → current order/payment authority → `PaymentLifecycleService.MarkPaymentExpiredAsync` → release/payment/order transitions, coupon effects and downstream events.

The dangerous distinction is a live COD order versus a genuinely expiring online payment. Applying payment TTL cancellation to live COD can incorrectly cancel an otherwise valid delivery order. Substantial guards against this already existed in the working tree, including `CodReservationExpiryPolicy`; this review did not author them.

**SQLite-backed verification in the focused suite:**

- Live COD, normalized/whitespace COD code, and CashOnDelivery-status fallback survive stale reservations and **two cleanup executions**, followed by a lifecycle-expiration call. Pending order/payment and original reserved stock remain intact.
- Terminal cancelled/refunded COD is still eligible for explicit reservation release.
- An open online payment intent can expire, cancel its pending order and release stock through the proper lifecycle.
- Success/failure/replay/late-capture scenarios check reservation commit/release and quarantine behavior without external financial execution.
- Existing collection-after-checkout-TTL coverage consumes the original physical COD reservation once.

These are real SQLite persistence assertions with stale timestamps, **not a clock-advanced hosted overnight run or PostgreSQL-locking proof**. No original customer order's authoritative audit trail was correlated to a cancellation job/webhook/admin action, and no historical orders were repaired. Consequently the tester's particular overnight cancellation remains **not conclusively attributed**, despite the demonstrated TTL risk and passing baseline safeguards.

## 4. Additional proactive findings

| Finding | Evidence and change | Verification/limit |
|---|---|---|
| Order-history Retry was a no-op | `setPage(page)` did not change state, so it did not rerun the loading effect. Retry now calls `loadOrders`. | Actual component recovers from controlled 503 to empty order history; exactly two reads. |
| Verification-page error contract mismatch | API errors use `code`; page checked only `error`. Accept the backend code for expired/used links. | Actual page displays specific expired/used messages from controlled 400 responses. |
| Single-use link posted twice under Strict Mode/effect replay | Page effect initiated requests without retaining the in-flight promise. Reuse a token/tenant-keyed promise and suppress stale completion updates. | Actual StrictMode page sends one successful verification POST. Together with expired/used cases, final fixture records three verification POSTs. Concurrent database token consumption is not certified. |
| Missing verification-page localization | This app's translator returns the key for missing entries; `defaultValue` did not supply the intended text. Added actual English/Arabic verification-page entries. | Success and expired/used page rendering exercised in browser; confirmation layouts in both languages. |
| Email resend abuse protection failed open and was non-atomic | GET/check then later INCR permitted races, and cache errors allowed sending. Reserve a bounded attempt with a single Redis Lua operation; cache failure stops the request before token rotation. | Controlled database mocks cover rejection/delay and cache outage. Actual Redis Lua concurrency remains untested. Failed dispatch attempts intentionally consume a resend attempt. |
| Unsafe email HTML/logging | Encode customer display name before HTML interpolation; remove recipient-bearing SMTP success/failure logs and redact unconfigured-sender recipient logging. Dispose MailMessage resources. | Source-reviewed hardening and successful SMTP sink test, not a reproduced external exploit or full log-retention audit. |

## 5. Coverage map and omissions

| Area | Evidence reached | Not certified |
|---|---|---|
| Bootstrap, tenant resolution and configuration | Source trace; hosted existing bootstrap; selected tenant-isolation/rate-limit backend regressions | All deployment/custom-domain combinations; full Mongo projection convergence |
| Header/theme/account entry | 120 real-component combinations; SSR red→green tests | Arbitrary custom palettes, full accessibility audit, all real auth/provider permutations |
| Catalog/card/options | Hosted reads and ten detail-navigation + real variant-resolution checks; shared-action tests | Full category/search/filter/media inventory, every option combination, real cart mutation |
| Auth registration/email | Real services with isolated EF store and sink; actual SMTP loopback; intercepted browser forms; restarted-host weak-password/invalid-token/guest-resend contracts | Positive hosted persistence/cookies, real inbox delivery, concurrent token use |
| Auth lifecycle/tenant isolation | Existing focused and broader controlled suites; source guard review | Live Google/Facebook handshakes; complete account-security UI mutation matrix |
| Cart/pricing/coupons | Existing broader backend and full frontend suites; source authority review | Shared-cart manipulation and exhaustive promotion arithmetic/Redis concurrency |
| Checkout shipping/phone | Full frontend regressions, country-input browser cases, normalizer/OTP service tests | Real checkout submission with disposable PostgreSQL, all carrier/country combinations |
| Payments/orders/inventory | SQLite lifecycle/reservation assertions; source enforcement review | Real charges/refunds, provider webhooks, PostgreSQL races, original overnight incident attribution |
| Account order history | Controlled 503/retry/empty-state browser workflow | Exhaustive hosted pagination/status history for real customers |
| Recovery/session boundaries | Existing broader tests; examined offline cart-clearing assertion failure | Complete hosted recovery and pending-payment browser workflow |
| Email/WhatsApp integrations | SMTP adapter loopback; controlled event/store tests; integration tracing | External email/WhatsApp delivery, bounce/receipt processing |
| POS Admin dependencies | Theme/account/channel/configuration source investigation | Full POS audit; no publication/provider/account mutation |
| Performance and production assets | Production build completes | Bundle-cycle cleanup, load/performance budgets, preview-server runtime smoke, browser-engine matrix |

No assumption is made that a passing mocked/service test certifies hosted persistence or external integration. The complete repository/backend suite was not run because integration fixtures require explicit disposable infrastructure; using shared configuration as a fallback was deliberately avoided.

## 6. Exact test outcomes

| Run | Result | Notes |
|---|---|---|
| Baseline Storefront tests | 54 files, **459 passed** | Before application edits |
| Baseline Storefront build | **Passed** | Existing warnings |
| Baseline selected backend | **49 passed / 9 failed / 2 skipped**, 60 total | Nine fail at explicit disposable PostgreSQL prerequisite, not an application assertion |
| New navigation tests before fix | **2 failed** | Forced white foreground; missing names |
| New email tests before fix | **3 failed / 2 passed** | Missing registration dispatch; swallowed sender failure |
| Phone fallback reproduction | **1 failed** | Corrected fixture uses the exact `+2` fallback identity; this red run is `phone-fallback-red.trx` |
| Final Storefront tests | 57 files, **473 passed** | 14 additional cases |
| Final Storefront production build | **Passed** | An intermediate build failed on three unused React imports in new tests; corrected, then rebuilt successfully |
| Final focused backend | **96 passed**, 0 failed/skipped | `focused-final.trx`; includes loopback SMTP and SQLite lifecycle |
| Broader backend, relocated artifacts | **161 passed / 14 failed**, 175 total | Eleven failures were source-root assumptions caused by the artifacts location; rerun in standard output corrected those |
| Broader backend, standard output | **172 passed / 3 failed**, 175 total | Before the additional phone/SMTP cases |
| Final broader backend, standard output | **174 passed / 3 failed**, 177 total | `broader-final.trx`; unchanged three failures below |
| Isolated browser regression | **143 scenarios, 0 recorded failures, 0 page errors** | Real React components with intercepted HTTP; see JSON |
| Hosted browser catalog/options | **10/10 expected routes and HTTP-200 variant resolutions, 0 page errors** | Fresh context; only catalog reads and inspected read-only resolution POST allowed |
| Hosted API smoke after user restart | **6/6 passed, 0 page errors** | Weak registration rejected; random invalid email token rejected; guest resend unauthorized; empty OTP request/verification rejected; phone-policy status read |

The empty-phone cases return the automatic model-validation code `VALIDATION_ERROR` before reaching the controller's explicit missing-field branch. The first smoke assertion expected the latter branch; its expectation was corrected to accept either safe validation layer, retaining the required HTTP 400. No valid registration, successful resend, OTP dispatch or order mutation was admitted by that script. Ordinary diagnostics/rate-limit bookkeeping can still occur for validation requests.

### Remaining broader failures

1. `StorefrontCatalogPresentationAuthorityCertificationTests.StorefrontCartIntentAndAuthorityMustRemainBackendOwnedAndFailClosed`: source assertion rejects the existing phrase “Showing your last saved cart” in `src/store/cartStateResolver.ts`.
2. `StorefrontProductionBlockerFixTests.CheckoutStore_MustNotClearCartBeforePaymentConfirmation`: source assertion rejects an existing `clearCart()` call. Inspection locates it in the completed **COD/MANUALWALLET** branch, not the online-payment branch. The literal assertion alone does not establish a real premature online-payment clear, but the suite remains red and requires reconciliation.
3. `StorefrontProductionBlockerFixTests.ThemeEditorPage_MustHydrateMaterializedThemes_And_SaveDraftBeforePublish`: regex expects a specific silent-draft-save/publish source sequence in POS Admin's existing `ThemeEditorPage.jsx`. That file was not changed here; no publish was performed to adjudicate runtime behavior.

These failures involve files untouched by this task; they are not counted as repaired or dismissed as harmless. No baseline execution of this exact broader filter was captured before edits, so their attribution rests on unchanged affected files/source inspection, not a claimed pre-edit green run.

### Commands and artifacts

From `PointOfSale.Storefront/`:

```powershell
npm test -- --run
npm run build
```

Focused backend, from repository root:

```powershell
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter "(FullyQualifiedName~StorefrontQuality|FullyQualifiedName~StorefrontPhoneVerificationReliabilityTests|FullyQualifiedName~PhoneNumberNormalizerTests|FullyQualifiedName~StorefrontAuthenticationLifecycleTests|FullyQualifiedName~InventoryReservationLifecycleIntegrationTests)&FullyQualifiedName!~Postgres" --logger "trx;LogFileName=focused-final.trx" -v quiet -p:WarningLevel=0
```

Broader controlled backend selection:

```powershell
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-build --filter "(FullyQualifiedName~PointOfSale.Tests.Storefront.|FullyQualifiedName~StorefrontTenantIsolationTests|FullyQualifiedName~StorefrontProductionBlockerFixTests|FullyQualifiedName~StorefrontRateLimitSessionTests|FullyQualifiedName~StorefrontCart|FullyQualifiedName~StorefrontCoupon|FullyQualifiedName~StorefrontPricingAuthorityTests)&FullyQualifiedName!~StoreConfigConvergenceTests" --logger "trx;LogFileName=broader-final.trx" -v quiet
```

Browser scripts, reusing the registered isolated Edge session:

```powershell
playwright-cli list
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-quality-components.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-quality-hosted.js
playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/storefront-quality-api-smoke.js
```

Evidence:

- [`browser-components.json`](../evidence/storefront-quality-20261015/browser-components.json)
- [`browser-hosted.json`](../evidence/storefront-quality-20261015/browser-hosted.json)
- [`browser-api-smoke.json`](../evidence/storefront-quality-20261015/browser-api-smoke.json)
- [`restarted-api-readiness.json`](../evidence/storefront-quality-20261015/restarted-api-readiness.json)
- [`backend-test-summary.json`](../evidence/storefront-quality-20261015/backend-test-summary.json)
- [`storefront-build-final.txt`](../evidence/storefront-quality-20261015/storefront-build-final.txt)
- TRX results in `PointOfSale.Tests/TestResults/`: `baseline.trx`, `email-red.trx`, `phone-fallback-red.trx`, `focused-final.trx`, `broader-final.trx` and intermediate runs.
- `tests/browser/storefront-quality-baseline.js` is an early read-only observation script, **not** the final assertion suite or a complete pre-fix matrix. Its completed output after initial edits is not misrepresented as an entirely pre-edit browser baseline.
- Early browser-fixture runs failed on test-harness setup (broad API route matching source-module URLs, Vite-versioned context duplication, fixture locator/timing issues). These were repaired in the fixture; they are not counted as application defects or passing scenarios.

## 7. Changes owned by this review

Paths below describe this task's edits only; existing unrelated hunks in the same files remain user work.

- Backend: `PointOfSale.API/Services/Storefront/{StorefrontAuthService,StorefrontEmailVerificationService,StorefrontPhoneVerificationService}.cs`; `Controllers/StorefrontAuthController.cs`; `Services/Auth/{EmailVerificationService,SmtpEmailSender}.cs`.
- Auth frontend: `PointOfSale.Storefront/src/auth/components/{AuthNavItems,RegisterForm,VerifyEmailBanner,OrderHistorySection}.tsx`; `pages/{RegisterPage,VerifyEmailPage}.tsx`; `hooks/useAuth.ts`; `store/authStore.ts`; `types/auth.types.ts`.
- Catalog/theme: `src/components/products/ProductCardRuntime.tsx`; `src/components/theme-components/{ThemeComponents.tsx,index.ts}`; `src/storefront/themes/cosmetics/layout/header.tsx`.
- Checkout/localization: `src/components/checkout/{ShippingStep,PhoneOtpVerification}.tsx`; `src/utils/{phone.ts,phone.test.ts}`; `src/i18n/locales/{en,ar}.json`.
- New regressions: three `*.quality.test.tsx` files; `PointOfSale.Tests/Storefront/{StorefrontQualityEmailTests,StorefrontQualitySmtpTests}.cs`; rejected-normalization regression in `StorefrontPhoneVerificationReliabilityTests.cs`; constructor dependency adaptation in `Security/StorefrontAuthenticationLifecycleTests.cs`.
- New browser scripts and this report/evidence. No schema migration, provider credentials, tenant settings, COD policy implementation, or financial/job configuration was changed.

## 8. Required follow-up before deployment sign-off

1. The API restart and safe negative HTTP-contract smoke are complete. Still complete **positive** hosted registration/verification/resend and enabled-OTP workflows using isolated data and controlled delivery infrastructure; six negative/policy checks do not certify these paths. Automated backend tests used the Release build, while the user's restarted host is a freshly compiled Debug build. The earlier temporary quality-build path predates the final phone-status repair.
2. Provide the explicitly approved disposable PostgreSQL cluster required by the managed shipping fixtures. Nine baseline integration tests stop with `Explicit disposable cluster required before managed shipping fixtures; no configuration fallback.` Do not point them at shared customer data. PostgreSQL transaction/lock/idempotency behavior remains open.
3. Configure an isolated email sink and controlled WhatsApp/test delivery channel for hosted integration. Current local SMTP configuration can target an external server, so no hosted registration/resend was used to trigger it. Validate actual receipt, retries, provider outages and channel policy there.
4. Correlate the reported cancelled order's tenant/order ID, payment method, creation/expiry/cancellation timestamps, lifecycle reason, audit actor, cleanup-job log and webhook history. Preserve records; do not bulk reopen or rewrite orders.
5. Reconcile the three existing broader source assertions and separately investigate whether their intended invariants need runtime regression coverage. No global green-suite claim is justified while they fail.
6. Investigate the degraded certificate-provider/Caddy-edge readiness separately before custom-domain/TLS deployment sign-off. No edge/certificate settings were modified here.

**Conclusion:** the reproduced frontend/auth/phone defects have bounded repairs with passing targeted evidence. Important deployment/integration/history checks remain explicit blockers; this review does not claim every Storefront feature or live integration is defect-free.
