# Shopixy Storefront Management — Phase 5: Payments and Shipping

Execution date: **2026-10-17** (local UTC+03:00; artifact timestamps use UTC).

**Phase 5 completed with bounded local/application qualification. Eight confirmed defect groups repaired and activated; all four screen groups have actual hosted/configured-persistence lifecycle evidence. Final selected Backend 835/835, Admin 1,938/1,938 and Storefront 537/537 pass. This is not external payment/carrier certification, whole-domain certification or an unrestricted release verdict.** Scope is Phase 5 only: Payment Methods, Manual Payments, Shipping Methods and Shipments, and their direct checkout, fulfillment and accounting consumers. No Phase 6 work.

Evidence root: `.ai/evidence/storefront-phase5-20261017/` (**E/** below). Backend TRX: `PointOfSale.Tests/TestResults/phase5-*.trx`. Counts are per invocation and overlap; do not add them.

## Environment, precondition and preservation baseline

- Workspace `C:\Users\fadya\source\repos\PointOfSale`, branch `main`, HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`. Existing extensive changes/deletions/untracked work preserved; baseline status, HEAD, deleted and file-level untracked manifests are in E/. Baseline index was empty, independently derivable from porcelain-v2 index columns; the empty native-output pipe did not create `baseline-index.txt`, so that absent file is not claimed as a saved index artifact. No staging, commit, stash, reset, clean or checkout/discard.
- Applicable `AGENTS.md` read. Phase 1–4 reports read, including the updated Phase 4 final rerun disposition. Relevant Manual COD, carrier cancellation, shipping reliability/final closure and Bosta-native reports reviewed against current source; historical passes are not current proof.
- Phase 4 outstanding rerun **completed**, TRX finish `2026-10-17T00:16:56.2307266+03:00`: **404 passed / 4 failed / 0 skipped / 408**. Four scheduler fixture initialization failures remain failures. `E/phase4-completed-result.json` records completion independently. No active testhost/vstest run was found at baseline. No Phase 4 checkout/mutation harness was rerun; its read-only preservation observer was reused below.
- Phase 3's documented **27 broad-backend failures remain historical open items**; this task does not claim to repair or rerun them.
- Existing configured PostgreSQL `192.168.1.5:5432/PosEcommerceDb_Baseline`, Admin `http://localhost:5173`, Storefront `http://ahramstore.localhost:5174`, API `http://localhost:5010`; AhramStore tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`. All configured data is user-authorized temporary QA data, not presumed production.
- No replacement application database, database/schema reset or migration command. Test provisioning fixtures were excluded. Configured tests explicitly use the existing database, rollback or exact manifested nonfinancial fixture cleanup.
- Existing headed Edge session `opencode-ui-test` reused after `playwright-cli list`; normal account sign-in, no credentials/tokens/cookies/storage-state dumps saved as evidence. No unrelated browser processes terminated.
- WhatsApp account `138379f5-7754-4c20-8863-b3b7aad28888` remains Disabled / Enabled=false; read before new orders, never modified. Phase 4 orders and posted history preserved.
- No real charge, bank transfer, cash handover, carrier booking, label charge, pickup or outbound message initiated. The Manual Wallet test image explicitly states **SYNTHETIC QA / NOT A RECEIPT / NO MONEY TRANSFERRED**; it was rejected, not approved as financial proof. Internal COD events below represent synthetic application events only.

## Evidence classes

**H:** actual hosted UI/API/configured persistence. **P:** existing configured PostgreSQL services/constraints/transactions, controlled external/event boundaries. **C:** controlled provider transport or component dependency. **S:** source trace only. No controlled response is a genuine provider booking, authentication, delivery or settlement confirmation.

## Authority and coverage matrices

### A. Payment Methods

| Contract | Actual trace and evidence | Qualification |
|---|---|---|
| List/detail/provider catalog | `/ecommerce/payment-methods` → `PaymentMethods.jsx` → `/api/admin/payment-methods`, `/catalog` and `/{code}` → role/permission/tenant scope → SQL `TenantPaymentMethods` | H persisted COD and absent gateway/wallet configuration; catalog read creates no rows. Supported catalog COD/Stripe/Paymob/ManualWallet; Fawry remains hidden/blocked. No invented arbitrary payment types or deletion endpoint. |
| Save/configure/enable/disable | Upsert/configure/enabled/disabled events staged before SaveChanges; Data Protection purpose `PaymentMethodSecrets`; SQL version → event version → projection consumer | H COD UI metadata, disable/public exclusion/re-enable, fresh revision restoration; synthetic wallet UI configuration and enable. P stale SQL writes fail; H stale reviewed version 409. |
| Validation/secrets | Metadata lengths, numeric sign/range, fee enum and min/max consistency; provider-specific `PaymentMethodConfigurationValidator`; masked write-only credential fields | Eight reproduced metadata regressions repaired; existing configuration reset/merge/encryption/response tests rerun. No supplied provider secrets entered. Saved configuration is explicitly not connection or execution evidence. |
| Public/checkout authority | `PaymentMethodReadRepository` reconciles current SQL enabled/configured state with Mongo; `StorefrontPaymentRuntimeReadinessService`; CheckoutService SQL reload plus final amount limits | H disabling COD removes it from actual public response. C existing provider readiness/configuration and checkout guard tests. Country/currency gateway capability is provider-specific, not added as a fabricated universal rule. |
| Races/failure/lifetime | Synchronous mutation guard; latest-read admission; keyed tenant/account/path workspace; accepted write followed by failed read is not complete success | Three failing-before component cases repaired (including sortOrder=0). Reviewed version sent by current UI; legacy no-If-Match callers remain compatible, a disclosed limitation. |

### B. Manual Payments

| Contract | Actual trace and evidence | Qualification |
|---|---|---|
| Screen and readiness | `/ecommerce/manual-payments` → `GET /api/admin/payments/manual-wallet/pending` → capability/tenant configuration and expiration authorities → SQL intent/order/customer join | H initial 404 `MANUAL_PAYMENT_CONFIGURATION_MISSING` is shown as configuration required, not empty queue. After explicitly synthetic wallet setup, real pending queue loads. This screen reviews Manual Wallet, not arbitrary cash/COD receipts. |
| Proof and review | Storefront upload token/session → `StorefrontManualWalletController` file validation → actual R2 upload → retained intent evidence → protected Admin proof GET → `ManualPaymentReviewApplicationService` → runtime bridge → `PaymentLifecycleService` | H real synthetic image upload/protected read and UI rejection; no fabricated bank receipt or cash confirmation. Proof retention/ownership and review capabilities are C-tested. |
| Rejection/idempotency | Exact intent/order/tenant amount; review rejection → canonical Rejected payment, cancelled order, no recognition journal | H exact EGP32.35 order rejected through UI; repeated rejection 409 `MANUAL_PAYMENT_REQUEST_INVALID`, not falsely called idempotent success. Supported cancellation re-read remains Cancelled/Rejected. |
| Collection/accounting | Related internal COD route `/api/payments/lifecycle/{order}/cod-delivered` → `MarkOfflineCollectionDeliveredAsync` → canonical payment state + accounting outbox → `CommerceAccountingService` / `FinancialPostingService`; dedicated Manual COD settlement route | P actual configured financial posting and settlement rollback contracts; H committed synthetic collection and EGP32.35 Cashbox settlement/replay, independently ledger-verified below. Delivered, collected, posted and settled are distinct. No partial wallet payment policy invented. |
| UI races/errors | Single confirmation, unmount withdrawal, stable request headers, readback-qualified success, locale-aware amount formatting, tenant workspace remount | New behavioral component checks; original source-contract tests retained. Headers alone do not establish application review replay: authoritative state decides. |

### C. Shipping Methods

| Contract | Actual trace and evidence | Qualification |
|---|---|---|
| Management | `/ecommerce/shipping-methods` → `ShippingAdministrationWorkspace(initialTab=shipping-methods)` → `ShippingMethodsTab` → `/api/shippingmethods` | H create/save/reload unique Manual method; actual tenant warehouse; country JSON `["EG"]`; base7.35/per-kg2.25; threshold1000; min/max delivery0. No fabricated city/district rate policy. |
| Validation and provider separation | DTO validation → carrier registry/service level → active warehouse ownership → external `CarrierIntegrationAuthority` | H negative rate rejected, active disabled-Bosta method rejected. Carrier connection != method != quote != shipment. Bosta native destination is not forced into canonical mapping. |
| Shipping quote and checkout | Storefront shipping preview/select → opaque WarehouseResolution → CheckoutService SQL method/payment reload → shared `StorefrontPricingService`/`ShippingCalculationService` → persisted order snapshot | H live country-scope bypass discovered and repaired; final pricing US400/EG200, disabled stale selection400, stale method edit409 and actual checkout EGP32.35. Existing order retains7.35 shipping after method base rate changes to99. |
| Concurrency/readback | Reviewed timestamp revision + EF UpdatedAt concurrency token; events before save; current UI If-Match; latest-read/single-flight/save-readback UI repair | P stale writes rejected, rollback independently absent. Zero estimate/threshold preserved; failure not mislabeled empty. No schema migration needed for concurrency metadata. |

### D. Shipments

| Contract | Actual trace and evidence | Qualification |
|---|---|---|
| Entry/list/detail | `/admin/shipping/shipments[/id]` → hooks → `/api/admin/shipments` → tenant SQL `AdminShipmentService`; actions from current order fulfillment authority | H real list/detail and tracking/readback; generic client-selected status mutation remains withheld from UI. Manual progression uses supported local API; no claim of UI controls that do not exist. |
| Local creation | Order confirm → outbox/`OrderShipmentOrchestrationConsumer` → `ShipmentService.CreateShipmentAsync` serializable order lock → exact stored method/warehouse/price/currency snapshot → shipment/events | H actual worker-created Manual shipment, two concurrent repeat-create409 responses, sequential local progression and final Admin details; P durable external claim/cancel mutual exclusion. No external booking needed for Manual fulfillment. |
| External registration/recovery | `CarrierShipmentService` → frozen original account/request/origin/destination/measured parcel → durable claim → carrier adapter → fresh result revalidation | C official adapter serialization, errors/ambiguity/no blind repost; P real PostgreSQL claim/cancel concurrency, FKs and retained-request constraints. Not a genuine carrier booking. |
| Tracking/labels | Both local tracking writers now share safe HTTP(S), encoded reference and both supported placeholder spellings | Six failing-before regressions repaired. Labels are stored-only where advertised; no label purchase or external tracking operation performed. |
| Cancellation and delivery | `ShipmentCancellationService` permanent operation; original identity/external evidence barrier; local-only vs CarrierConfirmed/ReconciliationRequired; fulfillment and payment owners remain separate | P duplicate Manual cancellation, competing claim/cancel, transactional cancellation/delivery outbox, failure rollback/recovery. C Bosta exact cancellation contract and unsupported carrier behavior; no real external cancellation proof. |
| Security/webhooks | Trusted tenant/policy, parcel/action boundaries, webhook authenticated claim/dedup/out-of-order tests | Real MVC controlled-auth 18×7 matrix and selected webhook transport tests; not seven real hosted accounts or a new webhook provider event. |

## Provider readiness versus executable capability

| Provider | Current local evidence | External capability not established |
|---|---|---|
| COD / Manual shipping | Existing COD enabled; Manual requires no gateway/carrier account; internal QA lifecycle is supported | No physical cash receipt/delivery asserted. |
| Manual Wallet | Initially missing; task creates sandbox/test-only configuration with a synthetic wallet number for QA; actual review path used only for rejection | No transfer or settlement by a bank/wallet provider. Cleanup disables and clears this synthetic configuration. |
| Stripe / Paymob | Catalog exists, no tenant configuration at baseline; runtime credentials/capabilities independently gated | No known configured test credentials for external payment execution; no charge/authentication trial. |
| Fawry | Existing explicit unavailable financial authority, suppressed catalog/checkout | Not enabled or relabeled supported. |
| Bosta | Account ConnectionTestPending, integration disabled; blockers include pickup verification, activation, provider account eligibility. Native destination IDs supported independently of national mapping | Historical authenticated connection is not current booking entitlement. No production POST or activation. Controlled native/adapter and configured snapshot tests only. |
| Aramex | Draft/missing configuration and unpublished carrier mapping | Its canonical mapping/route contract remains distinct from Bosta native IDs. No account execution. |
| DHL | CredentialsPending, disabled, connection/remote-account certification blockers | No authentication/account-route eligibility or booking assertion. |
| FedEx | Source capability NotCertified; no configured account observed | Controlled official-wire tests do not certify the provider/account. |
| Mylerz | Unavailable, missing configuration and unresolved contract | No invented token, payload, geography or booking contract. |

Baseline readiness: HTTP200/Degraded with core stores and R2 healthy. First activated observation: **HTTP503, R2 unavailable**; certificate provider/Caddy remain disabled/degraded. These are separate real observations, not erased by retry or changed thresholds. Actual later proof upload/read succeeded, which is not uninterrupted R2 availability certification.

Final current readiness `E/readiness-final.json`, `2026-10-16T23:10:03.9394222Z`: **HTTP200 / Degraded**, core stores, bus and **R2 Healthy**; certificate-provider/Caddy disabled/degraded. No health thresholds or provider settings were changed to obtain this result.

## Confirmed defect register

| ID | Root cause and repair | Regression/evidence |
|---|---|---|
| P5-01 | Payment upsert admitted negative fees/limits/order, inverted limits, unsupported fee types and invalid text lengths. Validate before mutation/event staging. | Eight pre-fix failures; final selected tests. |
| P5-02 | Payment list sorted zero order as999. Use nullish default and deterministic code tie. | Failing-before zero-order regression. |
| P5-03 | Payment/shipping editors allowed same-tick duplicate mutations, late reads and misleading full success after failed authoritative refresh. Single-flight/lifetime/read admission and explicit saved-but-reload-failed state. | Payment three-case red; shipping zero/duplicate/readback cases; focused/full Admin green. |
| P5-04 | Shipping editor replaced supported zero-day estimates with defaults; zero free threshold displayed as absent. Preserve explicit zero values. | Behavior-based zero roundtrip/display test. |
| P5-05 | Current payment/shipping forms had no reviewed concurrency contract; payment Version was event-only and shipping UpdatedAt unguarded. Add optional reviewed If-Match and EF concurrency tokens; emit/read revision and send it in current UI. | Real configured PostgreSQL stale writer and reviewed-version refusals; hosted stale forms409. Legacy header omission remains compatible, not forced428. |
| P5-06 | Phase 5 workspaces could retain account/store-owned forms; Manual approval could execute after unmount and show success before fresh list. Keyed boundaries, confirmation withdrawal, one confirmation and readback-qualified feedback; localized amount. | New Manual behavioral regressions; inherited boundary tests; full Admin run. |
| P5-07 | Two tracking writers used incompatible `{tracking_number}` vs `{trackingNumber}` and unencoded reference substitution; unsafe authoring templates admitted. Shared validated HTTP(S)/credential-free template and encoded reference. | Six pre-fix failures, final green; no tracking-host policy invented. |
| P5-08 | **Hosted discovery:** saved shipping-country restrictions were ignored by authoritative pricing, admitting EG-only→US quote. Shared cart/checkout pricing now validates persisted scope and known destination. | Actual HTTP200 negative failure retained; final actual US400/EG200 and eight behavioral country checks, including supported/unrestricted/incomplete-preview compatibility. Initial automated red attempt failed compilation, not assertion proof. |

Only changes relative to captured scoped originals are task-attributed; existing broad work is not credited to Phase 5.

## Verification and incomplete attempts

| Invocation | Result |
|---|---|
| Scoped Admin baseline | 215 passed /0 failed /0 pending |
| Scoped backend baseline | 483 passed /0 failed /0 skipped |
| Payment validation red | 0 passed /8 failed |
| Payment editor red | 0 passed /3 failed |
| Tracking red | 0 passed /6 failed |
| First expanded backend | 615 passed /4 failed /0 skipped; test expected Manager payment writes, but established policy correctly denies them. Expected matrix corrected; no production permission broadened. |
| Configured shipping/finance | 19 passed /0 failed /0 skipped: nine manifested concurrency/delivery cases, nine receipt/posting rollback scenarios, one Bosta native snapshot rollback |
| Expanded backend before hosted country repair | 813 passed /0 failed /0 skipped |
| **Final expanded Backend after last production edit** | **835 passed /0 failed /0 skipped**, `phase5-backend-country-verified.trx`; includes the configured19 and Phase5 method-concurrency/observer2. Not the full repository Backend suite. |
| Final strengthened independent configured observer | **2 passed /0 failed /0 skipped**, `phase5-final-observer.trx`; SQL/Mongo/ledger assertions after cleanup, plus rollback stale-method test; overlaps the selected population. |
| Prior-phase read-only financial/stock preservation observer | **1 passed**, `phase5-prior-phase-preservation.trx`; exact existing Phase4 observer with evidence redirected to this task, no checkout/mutation harness. Old two orders,40 revenue/10 COGS and one committed physical unit remain as recorded. |
| Full Admin after final Admin source | 1,938 passed /0 failed /0 pending |
| Full Storefront, unchanged source | 537 passed /0 failed /0 pending |
| Admin/Storefront typechecks | Exit0 |
| Admin/Storefront production builds | Exit0, isolated output directories; bundle/advisory warnings retained |
| Focused lint | Exit0, 0 errors /7 warnings (hook dependencies and mixed component exports), no rule suppression |

Exact filters retained as `E/backend-*.filter.txt`; exact commands/results appear below. `country-red.log` and first `backend-country-final.log` are compilation failures caused by missing required CartSnapshot timestamp in the new fixture, **not test passes or product assertion failures**. Corrected test does not change production constraints.

Per-invocation TRX inventory (all current task invocations; overlapping populations):

| TRX stem (`phase5-*.trx`) | Passed /failed /skipped |
|---|---|
| `baseline` | 483 /0 /0 |
| `validation-red` | 0 /8 /0 |
| `validation-green` | 8 /0 /0 |
| `configured-baseline` | 1 /0 /0 |
| `backend-final` | 615 /4 /0 |
| `configured-shipping-finance` | 19 /0 /0 |
| `backend-verified` | 813 /0 /0 |
| `tracking-red` | 0 /6 /0 |
| `backend-country-verified` | 835 /0 /0 |
| `before-cod` | 2 /0 /0 |
| `lifecycle-observer` | 2 /0 /0 |
| `final-observer` | 2 /0 /0 |
| `prior-phase-preservation` | 1 /0 /0 |

Other pre-existing `phase5-*` TRXs dated October15 (`backend-broad`, `backend-broad-final`, `cache-red/green`, `history-read`, `order-persistence`) belong to earlier work and **are not current task results**. Their filenames alone do not make them this execution's evidence. Frontend intermediate focused run221/221 and final editor subset21/21 are also retained; shipping red13 passed/3 failed, Manual mock-defect run0/2. No timed-out Backend run is counted as green; compilation-only failures have no TRX.

Browser harness diagnostics are retained: first COD-disable observer raced its row; corrected exact-row wait passed. Initial customer login had expired; normal sign-in succeeded, then its result formatter hit CLI's unavailable URL global. Cart required a supported simple product rather than an unspecified variant. Shipping evaluation initially raced cart bootstrap. Manual Place Order response wait timed out after one committed order; independent order/queue observer recovered the identity and checkout was **not repeated**. `browser-manual-continuation-first.json`: six passing checks, no failures, real protected proof read/rejection/cancellation. `browser-config-second.json`: 16 passing checks, no failures.

The initial COD collection race returned **200 and400**, failing the harness's200/409 assumption; the compatibility lifecycle endpoint declares400 domain failures. Its error body was not retained, so this is **not** independently labeled a proven precise concurrency-code response. Fresh SQL showed one collection journal. Subsequent concurrent committed collection replays both200, and concurrent same-identity settlement replays both200 with the same journal. The failing invocation remains `browser-cod-second.json`; its completed pre-failure checks are partial evidence, not a successful whole-script pass. No checkout was repeated after it committed.

The first Manual component run failed2/2 because its translation mock returned a new `t` function every render, repeatedly restarting the effect. A stable test translator fixed the harness; final behavioral/full suites pass. The first cleanup script used the wrong reset-label selector and stopped before mutation; corrected actual UI reset/archive passed6/6. Preservation-helper attempts encountered an empty-output file assumption, PowerShell native-stderr line-ending behavior and decorated Get-Content string serialization. All34 old hashes matched after resolving the serialized path values; explicit scalar path serialization fixed the manifest, and the final helper completed with retained advisories. These are tooling defects, not passing application attempts.

### Exact reproducible commands

Commands run at repository root unless a working directory is noted. Configured concurrency/claim/rollback selection was run while the user-stopped API had **no listener on5010**, avoiding background-worker discovery of manifested temporary intents. Release tests, private Debug outputs and frontend outputs did not compete for the same build path.

```powershell
# Final Backend: existing configured database, no provisioning/reset fixture.
$env:PHASE5_CONFIGURED='1'
$env:PHASE5_EVIDENCE=(Resolve-Path '.ai/evidence/storefront-phase5-20261017').Path
$env:SHIPPING_CONFIGURED_SYNTHETIC_CONCURRENCY='1'
$env:SHIPPING_CONFIGURED_ROLLBACK_QUALIFY='1'
$env:SHOPIXY_PHASE8_ROLLBACK='1' # Existing opt-in variable for the Bosta native rollback test only.
$env:SHIPPING_FINAL_EVIDENCE_DIRECTORY=$env:PHASE5_EVIDENCE
$filter=(Get-Content .ai/evidence/storefront-phase5-20261017/backend-country-final.filter.txt -Raw).Trim()+'|FullyQualifiedName~ConfiguredShippingConcurrencyTests.ActualConfiguredPostgres|FullyQualifiedName~ConfiguredCarrierRemittanceRollbackTests|FullyQualifiedName~BostaNativeConfiguredPersistenceTests'
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter $filter --logger 'trx;LogFileName=phase5-backend-country-verified.trx' -v quiet -p:WarningLevel=0

# Final observer after actual lifecycle/configuration cleanup:
$env:PHASE5_STAGE='final'
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Phase5ConfiguredTests --logger 'trx;LogFileName=phase5-final-observer.trx' -v quiet -p:WarningLevel=0

# Final source-equivalent API build:
dotnet build PointOfSale.API/PointOfSale.API.csproj -c Debug --no-restore -o .opencode-tmp/storefront-phase5-20261017/api-built-country -p:UseSharedCompilation=false -nodeReuse:false -v quiet -p:WarningLevel=0
```

The native rollback variable's historical name does **not** mean a later phase was started. The exact retained Backend filter selects vetted existing suites, not disposable database fixtures. Controlled provider examples include Aramex73, Bosta wire35, DHL90, FedEx35, Mylerz8 and original-account/external-registration safety111 cases; `backend-final-class-counts.json` is the exact835 breakdown. Some “Architecture” suites combine runtime tests and source assertions; they are not all presented as live behavior.

In `PointOfSale.Web`:

```powershell
npx vitest run --maxWorkers=2 --reporter=json --outputFile=../.ai/evidence/storefront-phase5-20261017/admin-final.json
npx tsc --noEmit
npm run build -- --outDir ../.opencode-tmp/storefront-phase5-20261017/admin-dist
node scripts/lint.cjs src/pages/PaymentMethods.jsx src/pages/PaymentMethods.phase5.test.tsx src/pages/ManualPayments.jsx src/pages/ManualPayments.phase5.test.tsx src/App.jsx src/modules/shipping-administration/components/ShippingMethodsTab.jsx src/modules/shipping-administration/shippingMethodCountries.test.tsx --format json --output-file ../.ai/evidence/storefront-phase5-20261017/lint-final.json
```

In `PointOfSale.Storefront`:

```powershell
npx vitest run --maxWorkers=1 --reporter=json --outputFile=../.ai/evidence/storefront-phase5-20261017/storefront-final.json
npx tsc --noEmit
npm run build -- --outDir ../.opencode-tmp/storefront-phase5-20261017/storefront-dist
```

API builds' warning level was suppressed by the command: exit0 is not warning-free-source proof. Frontend production builds retain large-chunk warnings; Storefront retains Vite/esbuild/Browserslist advisories. No whole-repository clean lint claim.

The final prior-phase preservation observer command was:

```powershell
$env:STOREFRONT_PHASE4_CONFIGURED='1'
$env:STOREFRONT_PHASE4_EVIDENCE=(Resolve-Path '.ai/evidence/storefront-phase5-20261017').Path
$env:STOREFRONT_PHASE4_STAGE='final'
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~StorefrontPhase4HostedObserverTests.OwnedHostedLifecycle_IndependentSqlMongoFinancialAndMessageObserver_ReadOnly --logger 'trx;LogFileName=phase5-prior-phase-preservation.trx' -v quiet
```

Hosted commands use `playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/<recorded-file>.js`, with UTF8 result files in E/. **Do not blindly rerun checkout harnesses.** Final successful commands used `storefront-phase5-manual-continuation.js`, `storefront-phase5-cod-continuation.js`, `storefront-phase5-readonly.js`, `storefront-phase5-cleanup.js` and `storefront-phase5-final-readback.js`. The actual configuration command succeeded as recorded in `browser-config-second.json`.

| Final browser artifact | Result and boundary |
|---|---|
| `browser-config-second.json` | 16 checks,0 failures; real payment/shipping authoring and restored COD metadata |
| `browser-manual-continuation-first.json` | 6 checks,0 failures; exact committed wallet order, stored proof, UI rejection/cancellation |
| `browser-cod-continuation-first.json` | 10 checks,0 failures; exact committed order's Posted collection, Cashbox settlement, concurrency/replay, completion and Admin shipment detail |
| `browser-readonly-first.json` | 39 checks,0 failures,0 page exceptions,0 Phase5 writes; four routes, EN/AR,1440/390px, RTL/LTR, anonymous401 and tenant spoof403 |
| `browser-cleanup-second.json` | 6 checks,0 failures; actual UI wallet reset/shipping archive and fresh readbacks |
| `browser-final-readback-first.json` | 6 checks,0 failures; real customer-owned order API results, empty customer cart, public configuration exclusion |

Viewport checks are smoke coverage, not exhaustive accessibility/translation/visual certification. Reused pre-existing Admin/Storefront tabs remain; task-created transient tabs were closed.

## Synthetic records, cleanup and final disposition

Marker **P5QA20261017-817**. Exact starting states and mutations captured in hosted and configured observer artifacts.

- Existing COD `5fd1c166-73cd-4235-a127-9efffad10b23`: original name/metadata and enabled state restored using current revision after synthetic rename/disable/re-enable. Versions/audit/events legitimately advance; not byte-identical physical restoration.
- New Manual Wallet `8596894a-df67-44c5-8e0e-f64372bc1d74`: synthetic internal configuration, no prior credential overwritten.
- New Manual shipping method `3953e808-a29b-4003-ba7c-ab74827125fc`: EG-only, actual owned warehouse; task-owned config only.
- Manual Wallet order `45772d30-62df-4982-883e-852608e9f61f`, **ORD-C9C5-2026-01056**, EGP **32.35** (25 merchandise +7.35 shipping); intent `9ca01977-f535-4dd5-8860-6bc6c17831bd`. AwaitingVerification→Rejected; order Cancelled; no approved receipt/recognized money. Proof retained as supported review evidence, not erased.
- Cart fixture IDs and failed pre-order attempts are retained in browser artifacts. Successful scopes use supported cart DELETE and fresh customer-owned GET. No broad cleanup of previous phase baskets/orders.
- Configured financial tests roll back and independently verify absence; manifested nonfinancial concurrency fixtures are narrowly cleaned. No posted hosted history is deleted or overwritten.

### Committed COD lifecycle and exact retained effects

Order `a0755b91-1f50-442b-9c16-df9d32e11cfd`, **ORD-C9C5-2026-01057**, EGP **32.35** (25 merchandise +7.35 shipping), was created once through actual customer checkout API/warehouse selection. Pending/unpaid→Confirmed→local Manual shipment→Packed→delivery sequence. The actual Manual shipment API traversed **ReadyForPickup→PickedUp→InTransit→OutForDelivery→Delivered**, with synthetic `P5QA20261017-817-LOCAL` tracking and an explicit example.test QA proof reference. This is no physical carrier delivery assertion. Unpaid completion409; synthetic collection→Collected/Posted; Manual settlement→Settled; supported completion→Completed. Completed cancellation409.

Shipment `4ebb25ac-0727-494c-9322-38942dfb9877` remains **Delivered**, original shipping7.35, Manual, **zero external registration attempts**, no external registration state/reference. `CodCollected=false` is retained: that field represents carrier collection evidence and is not rewritten to fabricate external cash observation after internal Manual collection. The order's separate payment authority is Settled.

Settlement operation `a640b716-b5ef-4eba-a7ce-9ff4b498cdb4`; batch `6a557a6b-dc8c-4d0c-afc8-d934a569b457`; one whole-order member, **32.35 gross /0 fees /EGP**; actual destination Cashbox1010. The authenticated operator GUID is persisted on batch/journal. Two simultaneous same-operation retries return the same journal; another operation on the allocated order409 `COD_MANUAL_ALREADY_SETTLED`.

| Journal | ID | Exact balanced entry |
|---|---|---|
| Collection `Order_COD` | `01a146f5-8f60-79cd-aa77-c9b923c3cdab` | Dr1130 COD Clearing32.35 /Cr4020 E-commerce Sales32.35 |
| Settlement `ManualCodSettlement` /JE-2026-000019 | `01a146f6-a1cb-70f8-9930-a6cb3e9e5089` | Dr1010 Cashbox32.35 /Cr1130 COD Clearing32.35 |

Each has one permanent idempotency key and one processed accounting-outbox receipt. **Exactly two journals**, not duplicate collection revenue; the rejected wallet order has **zero journals**. Independent before-COD→final SQL balance observation: Cashbox1010 **45→77.35**, E-commerce Sales4020 **75→107.35**; every other tenant ledger balance unchanged, including COD Clearing. This synthetic32.35 Cashbox/revenue effect is deliberately retained, not “cleaned” by deleting history or a compensating adjustment.

Both newly created orders use non-reserving merchandise in this scenario: exact scoped SQL finds **zero inventory reservations and zero stock movements**. No COGS journal was generated. This is not a claim that physical-stock checkout was exercised here; configured rollback/concurrency contracts are separate evidence. Phase4's physical reservation, EGP40 revenue/EGP10 COGS and prior terminal statuses remain unchanged.

### Cleanup and independent observer

- Actual Payment Methods UI explicit reset cleared the **new** wallet configuration and disabled it. SQL confirms `ConfigEncrypted` absent; original credential-free absence is not claimed physically restored: the new disabled test-labeled method row remains, as do review/proof/audit history. Public payment response now contains COD only.
- Actual Shipping Methods UI supported delete **soft-archived** the new inactive method; fresh Admin404 and public exclusion; original three shipping methods remain. Order/shipment snapshots and their archived method association are retained. No historical row was hard-deleted.
- Customer-owned cart independently reads empty after scoped supported cleanup. Converted sessions, failed pre-order QA session activity, uploaded retained proof, order/shipment/outbox/audit/financial history remain as legitimate QA records; no blanket zero-remnant claim.
- `configured-observer-final.json`: repeatable-read **READ ONLY** SQL and separate Mongo Find observer verifies exact two orders, one shipment, one batch/member, two balanced Posted journals, two processed receipts, no stock/reservation movements, cleared wallet/archived method, unchanged prior Phase4 orders and disabled WhatsApp. Both Mongo order projections agree on status/payment/total: Cancelled/Rejected32.35 and Completed/Settled32.35; cancelled unfulfilled order's legacy shipping projection remains Pending rather than a fictitious shipment.
- **Zero WhatsApp message logs linked to the two new order IDs**, independently queried. This is scoped negative persistence evidence, not proof about every external system globally.
- No new provider charge/booking/pickup/refund or transport activation occurred. Configured financial fixtures leave no tenant-owned rows after rollback; nonfinancial fixtures use exact per-run manifests/tenant/IDs, not prefix-wide history deletion.

## Runtime identity, final diff and acceptance

- User performed both API stop/restart windows. Nine runtime artifacts were backed up and hash-verified on installation; configuration files were not edited. Final API PID **12792**, start `2026-10-16T23:03:05.5037747Z`, normal Debug executable/Development/5010.
- `api-active-final.json` captures actual process/module paths, MVIDs and on-disk loaded-module artifact hashes; `api-build-match-final.json` confirms API/Core/Infrastructure equal final isolated build. API SHA256 **E3D4F96A9A63C85CEBE6CBE610E08E286D7B4A233C0D7F8E02C852DEF01A7E90**, MVID `3145ce16-2c7b-4f26-9359-47002ca7b0ad`. This is process/module/disk equivalence with new hosted behavior, **not a managed-memory MVID or long-duration native stability certification**.
- Six hundred eighty-six scoped baseline originals: **676 unchanged /10 intentionally changed /0 missing**, plus six separately captured existing originals used for later scoped changes. Final `task-files.txt` / manifest identifies **34 source/test/browser/helper files** (16 edited existing and18 new; report/evidence/build artifacts excluded). The original manifest alone is not claimed to cover all later files.
- Final `task-local-utf8.diff` inspected for task-only production/test deltas; existing dirty changes retained. Earlier native-output diff had console-encoding corruption only in generated evidence; UTF8 diff is the review artifact, application locale source remains valid Unicode. `git diff --check` on task paths exits0; no unrelated source edits identified. New-file source/test types also pass builds/typechecks/lint as applicable.
- `preservation-final.json`: HEAD unchanged, **index0→0**, all **579 baseline deleted paths remain absent**, all **32,883 file-level baseline untracked paths remain present**. This is path preservation plus scoped source hashes, **not byte-level proof for every unrelated file**. No staging or commit. Task artifacts remain uncommitted for review.

## Remaining limits and classification

1. **External provider/account prerequisite:** Stripe/Paymob test credentials/remote execution and all external carrier account/route/billing entitlement are unverified; no real charge, carrier booking/label/pickup/cancellation confirmation. Bosta-native compatibility is preserved, not generalized to other adapters. Continue provider qualification only with verified sandbox/known test credentials.
2. **Not fully reproduced/qualified:** every multi-account hosted tenant/warehouse switch, every popup/offline/cross-tab ordering, complete accessibility/translation, physical-stock hosted lifecycle, full webhook/projection/retry chaos, provider-created/local-read-failed real reconciliation and long-duration runtime stability. Selected controlled/SQL tests do not replace those external/hosted boundaries.
3. **Compatibility/business policy:** current UI supplies reviewed versions; legacy callers omitting If-Match retain last-write behavior across sequential stale forms, although simultaneous EF writes conflict. Header enforcement for all legacy clients is not silently made mandatory. Missing destination in an incomplete preview remains provisional; actual checkout requires its address and uses repaired shared country validation. Shipping rates still follow existing base+per-kg/free-threshold formula; no city/district, product exclusion, split shipment, partial payment, fee/remittance or new fallback policy is invented.
4. **Test/tooling defects disclosed:** four corrected authorization-matrix assumptions, initial fixture timestamp compile failures, unstable translation mock, locator/bootstrap/response-wait errors and first collection-race status assumption. Timed-out browser response observation is incomplete even though independent persistence proves its committed order; it is never counted as a full pass.
5. **Historical unresolved results remain:** Phase4 final404/4 scheduler-fixture failures, Phase3 broad27 failures, earlier readiness503/R2 and broader historical reports are not repaired/relabelled by Phase5. Current readiness is separately200/Degraded. Seven lint warnings and production bundle advisories remain.
6. **Confirmed and fixed:** P5-01–P5-08 above. No known reproduced Phase5 defect is deliberately masked by a weakened financial/tenant/provider assertion. This bounded completion does not imply exhaustive defect absence or whole Storefront Management certification.

**Stop at Phase5. No Phase6 or future-phase report/work was begun.**
