# Treasury T-09 Fawry Provider Authority Certification - 2026-09-14

## 1. Root Cause And Implemented Authority Decision

**T-09 verdict: PASS with documented repository-wide gate boundaries.**

Fawry transport code exists for credentials, checkout handoff, signatures, webhooks, inquiry, and refund/capture/void translation, but the application has no certified Fawry clearing account, accounting payment-method mapping, settlement feed, settlement inquiry, or durable settlement authority. Before T-09, Fawry could be advertised as executable, successful callbacks could enter the canonical money lifecycle, recovery could re-enter provider initiation, and unknown collection providers could fall through to Stripe clearing.

The implemented decision is **unsupported fail-closed**, not new Fawry settlement support. All known Fawry financial admission paths now use:

- Code: `FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE`
- Message: `Fawry is unavailable because financial settlement authority is not configured/certified.`

Read-only PostgreSQL discovery across `AhramStore`, `greatone`, and `shopixy` found zero Fawry tenant payment-method rows, zero Fawry accounting payment-method rows, and zero Fawry-named ledger or Treasury accounts. Existing mappings were `CASH -> 1010`, `CARD -> 1110`, `STRIPE -> 1120`, `COD -> 1130`, `WALLET -> 1140`, and `BANK_TRANSFER -> 1030`. There was no legitimate settlement authority to adopt.

T-09 creates no Fawry account, payment method, migration, seed, settlement batch shape, Cash Flow rule, backfill, historical rewrite, manual journal, or direct SQL business write. Stripe, Paymob/Card/Visa, COD, and Manual Wallet retain explicit existing paths. Unknown or blank providers no longer default to Stripe clearing.

## 2. Files Touched And Why

The following are T-09 files or existing dirty files into which narrow T-09 edits were layered. Large working-copy diffs in `CommerceAccountingService.cs` and `PaymentLifecycleService.cs` also contain preserved T-06/T-07/T-08 work and must not be attributed wholly to T-09.

| File | T-09 purpose |
| --- | --- |
| `PointOfSale.Core/Payments/FawryFinancialAuthorityPolicy.cs` | Defines the shared stable code, message, and normalized Fawry detection. |
| `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs` | Marks Fawry unsupported and rejects configure, upsert, and enable mutations server-side. |
| `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs` | Removes Fawry from the API-admitted storefront list, including stale projections. |
| `PointOfSale.API/Controllers/CheckoutController.cs` | Normalizes checkout and preview authority failures to HTTP 422 with the stable contract. |
| `PointOfSale.API/Controllers/PaymentsController.cs` | Blocks direct payment-intent requests with Fawry order lineage. |
| `PointOfSale.API/Controllers/FawryWebhookController.cs` | Rejects authorized/success-shaped callbacks with stable HTTP 422 while retaining diagnostic audit evidence. |
| `PointOfSale.API/DTOs/Payments/PaymentDtos.cs` | Adds `WebhookResponseDto.ErrorCode` for deterministic webhook rejection. |
| `PointOfSale.API/Services/Checkout/ICheckoutService.cs` | Defines the checkout financial-authority exception contract. |
| `PointOfSale.API/Services/Checkout/CheckoutService.cs` | Rejects new Fawry checkout before durable checkout side effects while preserving read-only completed replay. |
| `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs` | Prevents Fawry provider recovery and routes stale attempts through the existing expiry path. |
| `PointOfSale.API/Services/Payments/IPaymentService.cs` | Defines the payment-intent financial-authority exception contract. |
| `PointOfSale.API/Services/Payments/PaymentService.cs` | Blocks direct, resumed, disguised-order, and persisted-intent Fawry lineage before provider execution. |
| `PointOfSale.API/Services/Payments/StorefrontPaymentRuntimeReadinessService.cs` | Blocks Fawry before tenant credential/provider readiness lookup. |
| `PointOfSale.API/Services/Payments/Fawry/FawryPaymentGateway.cs` | Adds a defense-in-depth provider execution rejection. |
| `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs` | Removes unknown-to-Stripe fallback, preserves explicit supported aliases, and blocks Fawry capture, suspense clearance, and refund accounting. |
| `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` | Blocks Fawry/unknown webhook money admission, Fawry settlement, and Fawry refund lineage before mutation. |
| `PointOfSale.Web/src/pages/PaymentMethods.jsx` | Presents Fawry as unsupported, removes configure/enable/edit controls, and retains Disable only for a stale enabled row. |
| `PointOfSale.Web/src/pages/PaymentMethods.test.tsx` | Covers unsupported presentation, stale-row handling, blocked actions, and supported controls. |
| `PointOfSale.Tests/Payments/FawryFinancialAuthorityContainmentTests.cs` | Adds behavioral no-mutation and positive-control coverage across admin, readiness, accounting, settlement, refund, webhook, and recovery. |
| `PointOfSale.Tests/Payments/FawryWebhookReliabilityTests.cs` | Covers stable 422 callback rejection, duplicate replay, audit retention, and no lifecycle admission. |
| `PointOfSale.Tests/Payments/PaymentServiceCreatePaymentIntentGuardTests.cs` | Covers direct and disguised Fawry payment-intent rejection. |
| `PointOfSale.Tests/Architecture/FawryCertificationClosure5Tests.cs` | Updates architecture assertions to the fail-closed decision. |
| `PointOfSale.Tests/Architecture/FawryContractClosure2CertificationTests.cs` | Freezes the new public Fawry contract. |
| `PointOfSale.Tests/Architecture/FawryConvergence1CertificationTests.cs` | Freezes webhook-controller admission containment. |
| `PointOfSale.Tests/Architecture/FawryPaymentAuthorityCertificationTests.cs` | Freezes provider financial-authority behavior. |
| `PointOfSale.Tests/Architecture/FawryProduction2SandboxValidationTests.cs` | Freezes runtime-admission containment. |
| `PointOfSale.Tests/Architecture/PaymentMethodAuthorityCertificationTests.cs` | Updates provider-catalog authority assertions. |
| `docs/reports/TREASURY_T09_FAWRY_DELEGATION_BRIEF_2026-09-14.md` | Records the frozen implementation and verification contract. |
| `docs/reports/TREASURY_T09_FAWRY_PROVIDER_AUTHORITY_CERTIFICATION_2026-09-14.md` | Records this independent certification. |

No T-09 dependency, configuration, migration, model snapshot, generated asset, or Storefront source file was added or changed.

## 3. Exact No-Mutation Boundaries Established

- Admin: normalized `FAWRY` configure, upsert, and enable requests return structured non-success with the stable code before tenant-row persistence or payment-method event publication. Existing rows remain readable and are not rewritten. A stale enabled row may only be disabled.
- Storefront catalog: Fawry is filtered from the SQL/runtime-authoritative response even if stale SQL and Mongo state says enabled/configured. Existing disabled, invalid, and runtime-readiness filtering remains intact.
- Checkout: a new Fawry request is rejected after basic request/tenant validation but before order creation, inventory reservation, payment intent, provider checkpoint, provider HTTP execution, idempotency completion, or outbox creation. Historical completed-checkout replay remains read-only and does not grant new authority.
- Direct intent: request provider, order `PaymentMethodCode`, order `PaymentGateway`, and persisted `PaymentIntent.Provider` lineage are checked so a Stripe-shaped request cannot disguise a Fawry order or intent. Rejection occurs before a new intent/checkpoint or provider call.
- Recovery: Fawry attempts never invoke provider initiation or resume. Stale attempts enter the existing expiry path; that path may record expiry/failure state but creates no new provider or financial fact.
- Webhook: valid `PAID` and `AUTHORIZED` Fawry callbacks return HTTP 422 with the stable code and never enter `HandlePaymentResultAsync`. Order/payment terminal state, inventory, collection accounting, COGS accounting, suspense clearance, outboxes, and journals remain unchanged. A single `Skipped` webhook audit row is intentionally retained as diagnostic transport evidence, and duplicate callbacks replay the same stable rejection without duplicating that row.
- Generic webhook lifecycle: unknown gateways fail closed before lifecycle state mutation rather than being treated as a supported provider.
- Settlement: Fawry request, order, or intent lineage is rejected before selector resolution, settlement batch/member creation, order status changes, event publication, accounting outbox, or journal posting.
- Refund: Fawry order or intent lineage is rejected before provider refund execution, `OrderRefund` or allocation creation, payment/fulfillment/stock mutation, affiliate reversal, accounting/COGS outbox, or journal posting.
- Accounting: accepted provider aliases are explicit: Stripe to `STRIPE`, Paymob/Card/Visa to `CARD`, COD to `COD`, and Manual Wallet to `WALLET`. Fawry and unknown/blank providers fail without calling account resolution or financial posting. Suspense clearance requires explicit supported provider lineage.

## 4. Tests And Builds Run With Counts

| Gate | Result |
| --- | --- |
| Pre-implementation Fawry baseline | 154 passed, 0 failed. |
| Focused corrected backend gate: `FawryFinancialAuthorityContainmentTests`, `PaymentServiceCreatePaymentIntentGuardTests`, `FawryWebhookReliabilityTests` | 54 passed, 0 failed. |
| Broad T-09 backend gate from the brief | 303 passed, 3 failed, 0 skipped, 306 total. The three failures are independently confirmed baseline/dirty-worktree failures listed in section 5; no corrected T-09 test failed. |
| `dotnet build PointOfSale.sln -c Release --no-restore` | Passed with 0 errors. |
| `npm test -- --run src/pages/PaymentMethods.test.tsx` in `PointOfSale.Web` | 6 passed, 0 failed. |
| `npm run build` in `PointOfSale.Web` | Passed; existing large-chunk warning only. |
| Full `npm test -- --run` in `PointOfSale.Web` | 1,279 passed and 13 failed across 163 files; 155 files passed and 8 failed. No Fawry or `PaymentMethods.test.tsx` failure occurred. Unrelated failures are separated in section 5. |
| `npm run build` in `PointOfSale.Storefront` | Passed; existing Browserslist, mixed-import, and chunk-size warnings only. |
| Full `npm test -- --run` in `PointOfSale.Storefront` | 346 passed, 0 failed across 34 files. |
| Full backend `dotnet test` | Attempted, but the run ended without a terminal summary after emitting numerous repository-wide failures. It is not claimed as passed or completed. |
| `git diff --check` | No whitespace errors; only existing LF-to-CRLF working-copy warnings. |

Real-browser certification used Playwright 1.63 with installed Microsoft Edge against API port `5010`, admin port `5173`, and `shopixy.localhost:5174`. Credentials, cookies, authorization headers, and tokens are not retained in repository evidence.

- Desktop `1440x1000`: Fawry rendered `Not Configured / Unsupported`, showed the exact stable code/message, and exposed no action button. COD exposed `Disable` and `Edit`.
- Mobile `390x844`: the same Fawry and COD assertions passed.
- Shopixy Storefront: Nokia N73 rendered in real search results. The browser received HTTP 200 from the read-only payment-method endpoint with exactly `COD`, `STRIPE`, `PAYMOB`, and `MANUALWALLET`; `FAWRY` was absent.
- No checkout submission or payment-method mutation was performed. Screenshots are outside the repository at `C:\Users\fadya\AppData\Local\Temp\opencode\t09-browser\admin-payment-methods-desktop.png`, `admin-payment-methods-mobile.png`, and `storefront-nokia-search.png`.

## 5. Pre-Existing Failures And Dirty-Worktree Interactions

The brief froze HEAD at `0de1c542bd316026de06bd5cc8f7111878b1b0e6`. The worktree already contained extensive uncommitted T-06/T-07/T-08 and unrelated work. No reset, restore, clean, stage, commit, or wholesale formatting was performed.

The broad T-09 gate's three failures were separated from the T-09 result:

- `PaymentEndToEndAuthorityCertificationTests.BackendReadCheckoutSessionAndFinancialRuntime_MustOwnPaymentMethodSessionIntentPricingAndSqlPersistence`: an existing source-string assertion expects the former Fawry webhook runtime shape/`ReferenceCode` text.
- `PaymentMethodsAdminSafetyTests.EnablePaymentMethod_WhenConfigurationIsValid_EnablesMethod`: test data uses `pk_live_*` Stripe credentials under sandbox defaults and fails existing environment validation.
- `PaymentMethodsAdminSafetyTests.UpsertPaymentMethod_WhenConfigurationPayloadIsEmpty_PreservesExistingSecrets`: the same live-key/sandbox fixture conflict.

The full Web suite's 13 failures occur in unrelated i18n, generic payment-flow, cart-state, checkout-token, auth-stability, warehouse architecture, footer authoring, and checkout-fields tests. None is in the T-09 admin test or a T-09-modified Storefront file. Because no clean full-Web baseline was established in this dirty worktree, they are disclosed as unclassified repository-wide failures rather than asserted away as pre-existing.

The attempted full backend run emitted many unrelated architecture, concurrency, migration-file, sanitizer, and SQLite/PostgreSQL-syntax failures and did not produce a final count. One emitted payment authority failure is the known baseline source-string failure above. The incomplete full run is not evidence against a corrected T-09 behavioral test, but it prevents any claim that the repository-wide backend suite is green.

`PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` is modified in the surrounding dirty worktree, but T-09 did not touch it or any migration. Production builds did not leave tracked `dist` changes. Browser login produced expected unauthenticated session probes before successful login, and Storefront analytics returned an unrelated HTTP 403; the certified payment-method read itself returned HTTP 200.

## 6. Deviations, Risks, And Decisions Needed

- Delegation occurred exactly once using `opencode/muse-spark-1.3-contributor-free`, session `ses_f5ebddee8ffebJvYR0YnaLvV5e`. The resulting implementation was independently reviewed and corrected before certification.
- Review corrections closed disguised Fawry lineage, unknown webhook mutation, missing Visa/Manual Wallet aliases, unsafe suspense clearance, post-side-effect checkout rejection, unstable 422 normalization, webhook replay, recovery re-entry, stale-intent poisoning, Paymob/Card/Visa settlement aliases, and whitespace-normalized admin codes.
- Browser certification intentionally remained read-only apart from authentication/session activity. Server behavioral tests, rather than a real tenant mutation, prove that rejected admin, checkout, intent, settlement, and refund operations do not write financial/business state.
- Shopixy has no stale enabled Fawry tenant row, so the stale-row Disable-only UI was not manufactured in production data. Behavioral and component tests cover it without rewriting real data.
- The repository-wide backend and Web suites are not green. This verdict certifies the bounded T-09 authority decision because all focused T-09 tests, the corrected broad T-09 tests apart from independently separated baseline failures, both builds, full Storefront tests, and real-browser assertions passed. It does not certify unrelated dirty-worktree features.
- Dormant Fawry transport and historical presentation code remain readable by design. Enabling Fawry later requires a separate authority design with a real clearing account, durable settlement source, reconciliation, refund lineage, migrations, and independent certification; credentials or a successful webhook alone must never remove this boundary.
- No unresolved T-09 defect or decision request remains.
