# Affiliate Phase 4 — live qualifying certification closure

Date: 2026-10-21. Evidence: `.ai/evidence/affiliate-phase4-closure-20261021/` (paths below are relative to that directory).

## Executive verdict

**BLOCKED — live qualifying certification is not closed. Do not start Phase 5.**

The completed commission engine was not reopened. This task verified existing merchant identities and the missing trusted-delivery contract, added executable evidence for several stale source assertions, and reran bounded regression selections. **No new live order reached Earned or Approved.** No qualifying checkout, coupon, delivery verification, collection, refund, payout or withdrawal was created.

Two independent prerequisites remain missing:

1. An authenticated, legitimately authorized POS operator for Shopixy and/or Greatone. Existing SQL owner/admin records are not permission to mint their tokens, reset their passwords, impersonate them or change tenant ownership.
2. An accepted, non-external delivery-verification mechanism. Manual proof is deliberately untrusted, no canonical Manual verification evaluator is implemented, and the participating merchants have no usable configured sandbox/local carrier simulator.

An isolated QA-adapter design is proposed in `TRUSTED_DELIVERY_QA_ADAPTER_DECISION.md`, **not implemented or activated**. It requires explicit product/security approval of verifier identity, immutable provenance, environment/database isolation and a canonical evaluator shared by fulfillment and commission. Disabling a guard or submitting a fake signed carrier response is not an acceptable substitute.

## Safety and baseline

The initial baseline script refused the non-loopback configured database host before connecting. The user explicitly confirmed **`192.168.1.5` is the authorized primary development database**, not the tester machine; only then were read-only queries performed. No tester-machine access occurred. The workspace workstation is `FadyAdel`; no remote operating-system access was used.

`capture.py` records relevant full-row SQL digests, all four protected orders, existing source hashes, working-tree inventory, and SHA-256 hashes of **414 previous Phase 1–4 report/evidence files**. `baseline.json` was captured before authentication checks/new test files. No production application source was changed. The first script file itself is task evidence, not an application/database change.

The API listener/process **15320** was preserved. Builds used isolated output paths, not a replacement running application. No API restart was needed because changes are tests/evidence only. The existing launcher documents process-local SMTP `127.0.0.1:2527`; no configuration or environment override was changed and no notification workflow was invoked. `api-process.json` records process identity. The headed `opencode-ui-test` session remains open on the user's AhramStore dashboard.

Temporary headed authentication/affiliate contexts were closed after their checks; the original user POS context was not closed or logged out. Initial helper mistakes in Fetch response handling are retained as evidence-tool errors, not application defects.

## Legitimate merchant access

### Authenticated application evidence

Actual headed POS login and official **GET `/api/auth/session`** establish:

| Account / attempt | Official result | Authority |
|---|---|---|
| `ahramco.software@gmail.com` | Login 200; session 200 | User `1bb114d3-a9a3-4a9d-ace3-3690361637a8`, **Owner**, tenant **`c9c5c1ae-949e-498e-bc63-07242daeb49d` (AhramStore)**. |
| `fadyadel2060@gmail.com`, supplied password, POS login | **401** | No authenticated Shopixy session established. No password retry guessing, reset, hash extraction or identity mutation. |
| User-opened original POS session | Session 200 | Same AhramStore user/tenant/Owner, independently reverified after the user logged in. |
| Existing affiliate credentials | Affiliate portal admitted | Global affiliate read-only balance/history returned 200; not merchant POS authority. |

Evidence: `pos-authenticated-authority.json`, `user-session-authority-verified.txt`, `supplied-ahramstore.png`, `affiliate-balance-readonly.json` / `.png`. Only selected session-authority fields were retained; no session token/cookie/password was copied into repository evidence.

AhramStore's official effective permissions include `storefront.orders.view/manage`, `shipping.view/manage/fulfill`, `storefront.payments.view/manage`, `storefront.refunds.manage` and `storefront.promotions.manage`. They **do not grant cross-tenant ownership**. Nothing in AhramStore was modified to substitute for the participating merchants.

### Already-configured operator candidates — SQL, not authenticated sessions

Read-only `access.sql` / `access.json` joined `Users.TenantId` to authoritative `Tenants.Id`. Enum mapping is `UserRole.Owner=0`, `Admin=1`:

| Tenant | Existing active, nondeleted identity | Current SQL role / user ID |
|---|---|---|
| Shopixy `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | `fadyadel2060@gmail.com` | Owner / `4ba67bd6-a7ca-4f6e-8825-ee9e025c9dc4` |
| Shopixy | `test-reliability-agent@testing.invalid` | Admin / `00000000-0000-0000-0000-000000000901` |
| Greatone `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | `anabhbk2060@gmail.com` | Owner / `b29cadaa-a016-46e4-aed3-171173fe08d2` |
| Greatone | `test-reliability-agent-c43aebf24cc4406aa2db4df06d40bce5@testing.invalid` | Admin / `7ea6054b-3205-42ed-aca5-fefe1b3967e8` |

Both participating tenants are active. These records identify **which existing identities could be supplied legitimately**; their passwords/current authenticated effective permissions were not assumed. Test-helper accounts were not used to manufacture credentials. The same email in POS, Storefront and Affiliate systems is not proof of a shared credential or authorization scope.

Required access: provide a legitimate authenticated session for an existing participating-tenant operator, then verify its own `/api/auth/session` and effective order, shipping, payment and promotion permissions. Coupon creation requires `storefront.promotions.manage`; payment/fulfillment routes additionally enforce their role and tenant boundaries. No account promotion, tenant assignment change, global management shortcut or header override was attempted.

## Trusted delivery contract and architecture gap

### Why Manual delivery is blocked

- `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs:895–902` checks a SQL affiliate obligation through `ManualDeliveryAffiliateBoundary.HasObligationAsync` (order affiliate/link, attribution or commission). Manual-source Delivered is rejected **before SQL mutation or downstream publication**, regardless of the legacy override switch.
- Typed delivery reference and proof URL are logistics claims, not independently verified evidence. Normal non-attributed Manual fulfillment trims them, carries them on `FulfillmentDeliveredEvent`, and persists them to the order at `:928–931`. Persistence is necessary, but does not make them trusted.
- `ManualCarrierDefinition.cs:90–100,137–146` returns tracking sync `NOT_SUPPORTED` and has no webhook parser. Searching API/Core/Infrastructure found no `DeliveryVerification`, `DeliveryAttestation`, synthetic-verifier endpoint or shared delivery-evidence evaluator.
- `AffiliateSettlementOptions.RequireVerifiedManualDeliveryEvidence` defaults true; override defaults false. Its production guard requires those safe values. `AffiliateService.EnsureAffiliateDeliveryApprovalAuthority` independently rejects attributed Manual approval when trusted verification is required. There is no persisted Manual verifier record that this method currently accepts.

**Policy interpretation:** unverified operator proof is intentionally excluded. The code/comments do not establish that every independently verified Manual delivery is permanently forbidden as a commercial policy. Supporting one requires a product/security decision and an implementation contract; the test blocker alone does not justify weakening the guard.

### Existing carrier trust path

`ShippingWebhooksController` delegates to `ShippingWebhookRequestPipeline` and `CarrierShipmentService`. The existing mechanism:

1. Resolves the existing shipment/originating tenant and carrier identity server-side.
2. Requires enabled integration/webhook capability; validates the configured HMAC, shared-secret or allowlisted trust method. A caller's tenant header or Delivered string is not authority.
3. Persists webhook event identity/payload hash/trust outcome, fences replay/claims and revalidates target/trust under order → shipment → inbox/configuration locking (`CarrierShipmentService.cs:1651–1734,1803–1856`). Manual orders reject carrier-webhook updates.
4. Persists shipment/tracking state, synchronizes through `IOrderFulfillmentService`, stamps order delivery state/time, and publishes established shipment/order delivery facts (`:4951–4980,5139–5207`). Carrier collection evidence and payment/accounting admission are separate; delivery alone is not cash collection.

Commission consumes delivery events as notifications and re-reads locked canonical SQL order, attribution, payment/accounting, line and earning authority. Manual mode additionally checks exact retained proof and original delivered manual shipment. **Carrier approval does not itself re-verify a provider signature or look up a shared Manual attestation ID**; it relies on the canonical state established by the trusted upstream carrier boundary. Do not claim that the missing Manual verifier already exists just because both services can read Delivered status.

### Configured safe-test availability

`readiness.sql` / `readiness.json` returned:

- Shopixy: one enabled Bosta configuration `61f7bab1-9d84-4d58-b10c-a21f82f46f1d`, **sandbox false, tracking false, webhooks false**.
- Greatone: **no carrier configuration**.
- Participating-merchant coupons: **none**.

No configured sandbox callback/local simulator was available. Real provider adapters and test-only deterministic fake adapters are not an authorized live simulator. Carrier secrets were not read or used to forge responses; no real booking, polling call, delivery, dispatch or collection occurred.

### Proposed safe adapter — decision, not implementation

See `TRUSTED_DELIVERY_QA_ADAPTER_DECISION.md` for the full boundary. Required properties include QA-only packaging, fail-closed Development/Test **and database identity** checks, host opt-in default off, separate platform verifier authorization plus legitimate merchant access, explicit allowlisting of **new task-owned** orders, immutable synthetic attestation binding tenant/order/original-shipment/run/verifier/nonce/time/proof hashes, atomic normal fulfillment/outbox transition and independent revalidation by the same canonical evaluator at commission approval. It must never authorize real provider/collection operations, arbitrary production orders or a tenant-controlled activation flag.

This architecture is not currently established sufficiently to implement safely under existing permissions. Exact decision required: approve the policy/provenance schema, non-production database identity enforcement and independently authorized QA verifier principal, and separately approve a no-real-money collection simulator. **No break-glass implementation was added.**

## Live scenarios and authoritative financial result

| Scenario | Closure result |
|---|---|
| Headed authenticated POS identity/tenant/permissions | **PASS**, AhramStore only; original user session retained. |
| Headed Affiliate Portal read-only balance/history | **PASS**, HTTP 200, net approved/paid/pending/available all zero; history empty. |
| Authenticate supplied Shopixy email/password to POS | **FAILED LOGIN (401)**; no operator session established. |
| Greatone authenticated operator | **BLOCKED**, legitimate credentials/session not supplied. |
| New product-specific referral → qualifying checkout | **UNEXECUTED**, prerequisites missing; no speculative pending order created. |
| Payment collection / exactly-one live Earned | **UNEXECUTED**, no safe accepted collection workflow/order admitted. |
| Trusted delivery → live Approved / availability | **BLOCKED**, Manual verifier absent; no configured non-external carrier path. |
| Cross-merchant positive global availability | **UNEXECUTED**, isolated engine tests only. |
| Live partial/full refund, replay/concurrent reconciliation | **UNEXECUTED**, isolated tests only; protected orders never used. |
| Successful live QA coupon | **BLOCKED**, no participating merchant session; no coupon created in AhramStore as a substitute. |
| Withdrawal/payout creation/execution | **NOT STARTED**, explicitly prohibited. |

Actual affiliate readback (`affiliate-balance-readonly.json`): `totalApproved=0`, `totalPaid=0`, `totalPendingRequests=0`, `availableForWithdrawal=0`, `currency=EGP`, `canWithdraw=false`, `history=[]`. The existing Withdrawals tab was read only; no Request Withdrawal action was clicked.

**New closure order / attribution / payment / delivery-verification / commission / balance IDs: none.** Consequently no expected-versus-actual positive live commission is claimed. Existing protected candidates remain EGP **154** (Shopixy base 770 × 20%) and **220** (Greatone base 2,200 × 10%), not earned balances. `protected-orders-final.json` records their exact snapshots and zero financial/fulfillment rows; they were not reused.

Established rules remain unchanged: attribution/unpaid COD do not earn; recognized payment states are Collected/Settled/PartiallyRefunded with Posted or existing NotRequired accounting; collected undelivered is Earned/pending, trusted delivered approval contributes net Approved availability. Treasury clearing transfer is not an added earning prerequisite. Rate/cap/base/tax/discount/refund semantics and historical snapshots were not modified.

## Source-contract failure-by-failure classification

All 13 original assertions were preserved. `source.trx` repeats the prior 88-case selection plus the separately documented payment-regex case: **75 passed / 14 failed**. Final non-overlapping selection excludes three passing cases already owned by other suites: `source-final.trx` **72 passed / 14 failed, 86 total**. No failed assertion was excluded to create a green count. Exact messages/stack locations: `test-summary.json`.

| # | Original failure | Asserted behavior, investigation and classification |
|---|---|---|
| 1 | `AffiliateCommissionLifecycleCertificationTests.ReversalBehavior_MustUseRefundLineAmountsAndBoundPartialReversals` | Refund allocation and bounded reversals are correct obligations; literal `Math.Min(refundedBaseAmount, remainingBase)` is the **obsolete delta formula**. Actual service subtracts prior reversals from SQL cumulative base/commission, bounds totals and links OriginalEntryId. Existing partial/full/forged/event-replay and real PostgreSQL concurrency tests pass in `phase4.trx`. **Stale arithmetic shape; no rollback of the completed fix.** |
| 2 | `AffiliateCommissionApprovalWithdrawableBalanceCertificationTests.ManualDeliveryEvidence_MustBePersistedForLaterFinancialRevalidation` | Requires durable proof, but expects direct assignment from `normalized…`. Runtime persists normalized **event-carried** proof. `ManualDeliveryEvidence_SurvivesReload_AndCannotBeReplaced` and `ManualDeliveryConvergenceTests` pass. **Stale assignment shape; genuine trusted-verification gap remains separately BLOCKED.** |
| 3 | `CrossSystemCriticalRiskHardeningTests.AffiliateAttributionPlaceholder_MustNotExposePrePaymentOrderAsPaid` | Expects empty PaymentMethod at insert; `AffiliateOrderAttributionConsumer.cs:173–187` stores actual SQL event method while keeping `AttributedPendingPayment`, PendingPayment statuses, PaidAmount 0 and monetary totals 0. `AffiliateA11GapClosureCertificationTests.PaymentMethodProjection_MustUseSqlDerivedEventValueAcrossPlaceholderPaths` expressly requires this newer `.Set` contract and passes in the source selection. Phase 4 unpaid tests and SQL protected-order readback prove no earnings. **Contradictory/stale method-field expectation, not demonstrated premature payment; full live projection-to-paid certification not claimed.** |
| 4 | `DataAuthorityViolationHardeningTests.AffiliatePayoutDecisions_MustUseSqlLedgersNotMongoMoneyProjections` | Expects a direct controller balance call. Controller delegates to existing `RequestPayoutAsync`; service owns Serializable transaction and `GetAvailableBalanceAsync`/RawAvailable guard (`AffiliatePayoutService.cs:430–478`). Actual SQL-backed availability/merchant isolation are exercised in Phase 4 tests; headed readback agrees. **Stale abstraction location. No live request/payout action exercised or authorized.** |
| 5 | `AffiliateGovernanceCertificationTests.AffiliateTokenGeneration_MustNotEmitTenantIdClaim` | Fails extracting source at removed `IssueVerificationEmailAsync` marker before checking claims. New `ActualAffiliateTokenGenerator_EmitsGlobalProfileClaimsWithoutTenantAuthority` invokes the actual private generator and decodes the issued isolated JWT, verifying no tenant claim, correct profile/sub, issuer/audience. **Stale extraction marker; executable proof added**, not a fabricated live session. |
| 6 | `AffiliateWithdrawalRequestAuthorityCertificationTests.CertificationArtifacts_MustRecordPassAfPa61` | Historical artifact lacks `PASS AF-PA6.1`. **Historical evidence deficit**, not grounds to add a false PASS marker or rewrite prior reports. Unresolved certification evidence; no withdrawal certification claimed. |
| 7 | `AffiliateOrderLineageAdminPresentationAuthorityCertificationTests.FrontendOrders_MustAdmitAffiliateEvidenceBeforeRendering` | Expected inline English “Affiliate Attribution” and referral-template text moved to i18n (`Orders.jsx:1180–1207,1918–1920`; actual English “Affiliate attribution”). New actual drawer tests render admitted evidence, reject cross-order substitution and suppress incomplete evidence with warning; existing identity-admission tests also pass. **Stale localization/source shape**, executable rendering/security evidence added. |
| 8 | `AffiliateA11GapClosureCertificationTests.InventoryReservationOutbox_MustBeGuardedAndCommittedWithOrderTransaction` | Fails specifically at `:62` expecting nullable guard `inventoryReservation != null && …`. Successful final transaction has a nonnull returned reservation and checks physical items at CheckoutService `:7190`; stages deterministic inventory outbox `:7192`, saves `:7215`, commits `:7240`, clears reservation on rollback `:7247`. **Stale nullable guard shape**, not a demonstrated out-of-transaction publication. Existing retry/outbox tests pass, but **a newly fault-injected complete checkout reservation/outbox rollback test was not added**; full fault-path evidence remains limited, not silently waived. Supplemental inventory outbox fingerprint starts later than the initial baseline and is labeled accordingly. |
| 9 | `SearchSeoPublicIndexCertificationTests.LegacyAffiliatePortalDiscoveryStaysSqlAuthorityBound` | Expects `doc = doc == null ? …` over a Mongo candidate. Current detail obtains owner from SQL, builds placeholder and calls the same SQL recomposition (`AffiliatePortalController.cs:715–720`), supporting missing/stale Mongo. New tests call actual detail with no Mongo dependency and prove active category admission and inactive/deleted/missing parent rejection. **Stale candidate-source shape; executable current-authority proof added.** |
| 10 | `GlobalReliabilityInfrastructureExtractionTests.SearchAndAffiliateRepairPaths_MustRecomposeCurrentAuthorityBeforeVisibilityMutation` | Failure stack pinpoints `:601`, the inline `parent.Level == 1` check expected in **AffiliateProductDiscoveryService**, not other unrelated rebuild assertions. This service delegates to `ProductSearchSqlAuthorityOverlay.ApplyBatchByTenantAsync`, which validates active/nondeleted root/parent lineage. New tests invoke actual discovery recomposition across active/inactive/deleted/missing lineage. **Extracted-abstraction expectation**, scoped executable proof added; not blanket certification of every unrelated search repair path. |
| 11 | `AffiliateProductEligibilityLifecycleCertificationTests.AffiliateDiscovery_ReadAdmission_RequiresActiveCategoryAuthority` | Fails expecting inline `AffiliateCategories` in discovery service; enforcement is in the shared SQL overlay. Actual legacy controller retains its category joins; discovery uses shared active-category map. Same new six-case discovery/detail tests prove the abstraction. **Stale inline-source expectation**, not missing category authority in tested paths. |
| 12 | `AdminAffiliateCategoryGovernanceTests.GlobalAffiliateCategoryMutations_MustRequireSuperAdminPolicy` | Fails at `:24` because controller-wide `[Authorize]` is absent. **Every exposed mutation** has explicit `RequireSuperAdmin` and each read has Owner/Admin. Existing `EveryExposedCategoryMutation_MustRequireSuperAdmin_AndNoDeleteOrPatchRouteExists` and read-policy tests pass in `authority.trx`. Adding default POS authorization may interfere with the separate SuperAdmin scheme and is not needed to repair an unprotected current action. **Stale controller-level shape; no current unauthenticated mutation found.** Full new HTTP SuperAdmin penetration certification not claimed. |
| 13 | `AffiliateFrontendAuthorityCertificationTests.AffiliateFrontendSessionCache_MustRemainProfileCacheOnly` | Expects exact `/affiliate/login` navigation; current terminal-auth redirect preserves a validated local return URL. Profile authority starts null and comes from restoreSession, not localStorage. New portal-shell tests verify 401/403 redirect and no cached-profile admission, with 503 kept distinct; auth API tests cover no-store reads and bounded return URLs. **Stale URL literal; executable proof added.** |

Additional payment source assertion: `PaymentLifecycleTests.Controllers_ShouldNotDirectlySetPaymentStatus` mistakes `pi.Order.PaymentStatus == …` at `AdminManualPaymentController.cs:112` for assignment. Its permissive assignment regex consumes the first `=` while the equality whitelist does not cover this identifier. **False-positive source regex at the reported expression**, preserved and still failed. No source regex was weakened to change the count.

No production implementation defect was established by these source mismatches that justified changing completed commission semantics. The missing trusted Manual authority and historical/fault-path evidence deficits are not classified as harmless.

## Files added and quality gates

Runtime application source changes: **none**. Added tests and a type-only declaration:

- `PointOfSale.Tests/Affiliate/AffiliatePhase4ClosureAuthorityTests.cs`: six actual SQL discovery/detail recomposition cases plus actual JWT-generator claim test; isolated InMemory/generated identity only.
- `PointOfSale.Web/src/pages/Orders.affiliateClosure.test.tsx`: actual localized drawer/admission behavior, three cases.
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.closure.test.tsx`: terminal-auth/cache rejection and infrastructure-failure distinction, three cases.
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.d.ts`: type-only declaration for the existing JSX component, required by the new TypeScript rendering test; no runtime change.
- This closure report and separate evidence/scripts/adapter decision. Existing assertions and Phase 1–4 artifacts were not edited.

`run-suite.ps1` uses isolated assembly output, at most two xUnit worker threads, no collection parallelism, VSTest MaxCpuCount 1 and 120-second per-test hang detection. Related selections are disjoint in the final manifest; prior overlapping diagnostic runs are retained but not summed. TestDatabaseScope uses disposable test databases; isolated fixture writes are not mutations of merchant development history. No test bus/provider was used to dispatch a real event.

| Check | Result | Evidence |
|---|---|---|
| Focused Phase 4, including seven new closure backend cases | **38 passed / 0 failed / 0 skipped** | `phase4.log` / `.trx`; includes original real PostgreSQL recognition/outbox/refund test. |
| Phase 1–3 selection, final compatible isolated path | **260 passed / 0 failed / 0 skipped** | `phase123-final.log` / `.trx`. |
| Affiliate financial/domain/consumer/accounting bridge | **207 passed / 0 failed / 0 skipped** | `financial.log` / `.trx`. |
| Fulfillment/webhook/collection parser trust selection | **76 passed / 0 failed / 0 skipped** | `fulfillment-final.log` / `.trx`. |
| Accounting/outbox selection | **83 passed / 0 failed / 0 skipped** | `accounting.log` / `.trx`. |
| SQL/category/POS/affiliate authorization selection | **58 passed / 0 failed / 0 skipped** | `authority.log` / `.trx`. |
| Real PostgreSQL Bosta collection evidence selection | **49 passed / 2 failed / 0 skipped**, completed in 2m44s | `carrier-sql.log` / `.trx`; failures discussed below. |
| Payment/refund/settlement selection | **71 passed / 0 failed / 0 skipped**, completed in 3m53s | `payments.log` / `.trx`. |
| Original source set + payment regex case | **75 passed / 14 failed**, 89 total | `source.log` / `.trx`. |
| Final disjoint source selection | **72 passed / 14 failed**, 86 total | `source-final.log` / `.trx`. Three already-covered passing cases excluded only to avoid overlap. |
| Admin orders/lineage rendering | **61 tests / 5 files passed** | `admin-orders-tests-final.log`. |
| Affiliate portal, final expanded selection | **74 tests / 16 files passed** | `portal-tests-final.log`; includes three new shell cases. |
| Storefront checkout/store/cart/API/bootstrap | **403 tests / 43 files passed** | `storefront-tests.log`. |
| Backend build, isolated output | **PASS, 180 warnings / 0 errors** | `backend-build.log`; existing analyzer/EF warnings retained, not represented as warning-free. |
| Storefront build/typecheck; Admin production build | **PASS** | `storefront-build.log` (`tsc && vite build`), `admin-build.log`. |
| Admin TypeScript, verified | **PASS**, no diagnostics | `admin-typecheck-verified.log`; added component declaration resolved the new test import's missing JSX type declaration. |
| Focused lint, final | **PASS**, exit 0; 0 errors / 8 existing fast-refresh warnings | `focused-lint-final.log`, `lint-exit-final.json`; maintained legacy wrapper. |
| Diff whitespace | PASS native Git exit 0 | `diff-check-verified.json`; PowerShell's initial stderr/CRLF-warning status is not a whitespace defect. |

The first isolated test output lacked the normal extra framework directory depth, breaking a legacy source test's fixed-relative-path helper: **230 passed / 30 path failures**, not application failures. Rebuilt in `PointOfSale.Tests/bin/affiliate-phase4-closure/net9.0`; unchanged assertions then passed 260/260. Earlier new-fixture compile/type and UI text/contradiction expectation mistakes are retained in initial logs, corrected in the newly added tests, not counted as application regressions. The late portal test initially lacked a JSX module declaration; a type-only declaration resolved it, followed by a clean typecheck.

The combined fulfillment/payment/carrier run exceeded its **300-second command timeout**, with no complete TRX result. It is not a full-suite pass. Subsequent non-overlapping selections isolate those populations. The completed carrier run's two failures occur during fixture mutation at `BostaCodCollectionEvidencePostgresTests.cs:434`: existing immutable shipment guards reject changing carrier identity or soft-deleting retained shipment evidence **before** the intended financial-revalidation assertion. This is a **test setup incompatible with an earlier fail-closed guard**, not reproduced unauthorized money recognition. Tests/guards were left intact; final status remains failed for those two cases.

No full repository pass is claimed. The existing project excludes several accounting/WhatsApp test files by project configuration; this task did not change that configuration. Build chunk-size/Browserslist/fast-refresh/legacy lint warnings remain unrelated work.

## Preservation and retained records

Final full-row comparison is generated by `review.py` from `baseline.json` and `final.json`; `protected-orders-final.json` provides readable canonical state. The values below are unchanged:

| Authority | Baseline → final |
|---|---|
| Orders / items / attribution | **64 → 64 / 64 → 64 / 11 → 11** |
| Links / affiliate profiles / product policies | **5 → 5 / 4 → 4 / 3 → 3** |
| Commission / balance / payout requests | **10 → 10 / 0 → 0 / 0 → 0** |
| Journals / ledger lines / accounting outbox | **220 → 220 / 459 → 459 / 30 → 30** |
| Reservations / variant stocks | **57 → 57 / 127 → 127** |
| Shipments / payment intents | **28 → 28 / 34 → 34** |
| Refunds / refund lines | **6 → 6 / 6 → 6** |
| EF bus OutboxMessage / OutboxState | **0 → 0 / 0 → 0** |

All four protected orders remain Pending / unpaid / unfulfilled, with exactly one original attribution and reservation each; zero released/committed units, payment intent, shipment, refund or commission rows:

| Order | Order ID |
|---|---|
| `ORD-83B3-2026-01091` | `d1102ed5-4256-4cef-80ca-41fe57c6163e` |
| `ORD-C43A-2026-01093` | `32a82d41-2ae6-47f9-aa4f-cd5d17b1e8d6` |
| `ORD-83B3-2026-01094` | `40b22f7f-09ac-4c60-b779-f14cf3734fe2` |
| `ORD-C43A-2026-01095` | `2654c8f2-c859-41ad-b637-212affd7b1b4` |

Inventory `OutboxMessages` has a **supplemental later** before/after readback, not an initial pre-checkout transaction fault-injection baseline. No closure checkout occurred. Uninventoried session/login/security audit records may change through legitimate authentication and were retained; no global zero-write claim is made for authentication metadata. No session tokens are exposed in evidence. No new closure order/reservation/coupon/verification/financial/audit-business event needs cleanup; isolated test records belong to managed disposable test databases, not live merchant history.

`preservation.json` confirms **no changed/missing original row and no added row in every initially inventoried table**, all **414 previous artifacts unchanged**, all four protected orders unchanged, no change to previously modified source-file hashes and no pre-existing working-tree path removed. `evidence-scan.json` reports no JWT/password-fill patterns in scanned closure text files; this is a scoped pattern scan, not a repository-wide secret audit.

Previous reports/evidence and existing source work remain preserved; no Git stage/commit/stash/reset/clean/checkout-discard/restore, broad backup operation, historical SQL rewrite or reservation release was performed. Do not delete financial history or mutate protected pending orders to obtain a visual baseline.

## Exact handoff / stop condition

Provide legitimate Shopixy/Greatone POS sessions and an approved independently authorized, non-external verification/payment QA contract. If the QA adapter proposal is chosen, approve its policy/isolation/provenance design before implementation; do not enable the existing override. Then create **separate new** task-owned synthetic orders through the saved product links, with approved safe payment/delivery transitions and SQL readback after each step. Only afterward can headed positive Earned/Approved/coupon/cross-merchant certification proceed.

**This task stops at documented blockers and safe test evidence. Live closure remains BLOCKED; Phase 5, withdrawal and payout work are not authorized.**

### Post-report runtime status

After handoff, the previously running safe API launcher reported completion. A fresh read-only listener check found **no listener on port 5010**. The API was not deliberately stopped by this task and was not restarted following this notification. The earlier process/listener evidence describes its verification-time state, not current availability. No order or financial operation was attempted after the launcher exited.
