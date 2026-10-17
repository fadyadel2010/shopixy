# Shopixy Storefront Management — Phase 4: Orders and Customer Lifecycle

Run: 2026-10-16; closeout continued 2026-10-17. **Disposition: LIMITED CLOSURE. Confirmed scoped defects repaired; configured order/recovery/accounting workflows verified. Not unrestricted release or messaging certification.**

**Outstanding QA mutation:** WhatsApp account `138379f5-7754-4c20-8863-b3b7aad28888` remains **Disabled / Enabled=false**, changed by this run from **Connected / Enabled=true**. Supported transport status returns **503 / WHATSAPP_PROVIDER_UNAVAILABLE**; re-enabling requires a verified connection. No direct database restoration or fabricated connection status was applied. Restore through supported connection verification and Enable once the provider is available; coordinate this deliberately because it reopens outbound sending.

Evidence: `.ai/evidence/storefront-phase4-20261016/`. Backend results: `PointOfSale.Tests/TestResults/phase4-*.trx`. Counts describe separate, overlapping runs; do not add them.

## Scope and safeguards

- Phase 4 only: Operations Overview, Orders, Abandoned Checkouts, Cart Recovery, Blocked Phones, Online Customers, and WhatsApp Settings, with their direct checkout/recovery/lifecycle consumers. No Phase 5 implementation, staging, commit, configured-application schema reset/migration, replacement application database, or broad data cleanup. **Qualification:** selected legacy scheduler tests invoke their own managed test-database creation/migration/reset harness; this was discovered in the final rerun's failure stacks and is not an existing-application-database qualification. Do not reuse that fixture path for configured-only follow-up checks.
- Used the existing configured Admin `http://localhost:5173/`, Storefront `http://ahramstore.localhost:5174/`, and API `http://localhost:5010`; tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`. The user authorized existing data as temporary QA data and coordinated API stops/restarts.
- Reviewed Phase 1–3 and relevant quality/reliability reports before changes. Earlier certification language is not fresh execution evidence. Phase 3's **27 broad-backend failures** remain disclosed, not repaired or relabeled by this work.
- External WhatsApp sending was disabled before synthetic checkout. No payment gateway charge, carrier-provider booking, external message-delivery test, or real cash handover was performed. COD collection and delivery below are explicitly **synthetic internal business records**, not evidence of physical delivery or received cash.
- Reused registered headed Edge session `opencode-ui-test`; browser harnesses did not save credentials, cookies, access tokens or recovery tokens. Session closed before the last optional log-filter browser pass; it was not replaced with a fabricated authenticated session.

## Runtime, readiness, and preservation

| Boundary | Evidence / qualification |
|---|---|
| Activated API | `api-active-order-version.json`: PID **23080**, started `2026-10-16T19:21:40.8284868Z`, Development, port 5010; user restarted it. |
| Build identity | `api-build-match-order-version-final.json`: API, Infrastructure and Core installed SHA-256 hashes match `.opencode-tmp/storefront-phase4-20261016/api-built/` and the captured active assembly identities. API hash `789F70193CA6C750F1C280AF291CFB753C1492B2DA2160B4EF0D9049F3AE797B`. |
| Final readiness | `readiness-final.json`: HTTP **200**, overall **Degraded**; PostgreSQL, MongoDB, Redis, RabbitMQ and **R2 Healthy**. Certificate execution and Caddy integration remain disabled/degraded. Phase 3's recorded R2/503 failures are historical evidence, not the final current R2 result; neither result is silently erased. |
| Repository baseline | `main`, HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`; already heavily dirty: 579 deletions, 541 modifications, 635 grouped untracked entries. Index was empty. |
| Preservation | `preservation-final.json`: HEAD unchanged, index empty, all 579 baseline deleted paths still absent, all 635 grouped untracked paths still present. This proves grouped-path existence, not every untracked byte. |
| Attribution | 47 scoped copies: 36 unchanged / 11 changed in `originals-comparison-final.json`. Infrastructure `CheckoutSessionService.cs` copy was captured **after** its edit; it is not a valid pre-edit diff baseline. The pre-existing untracked `AdminOrdersConfiguredPostgresTests.cs` was strengthened without an independent full original copy. These limits are explicit. |
| Deliverables | `task-files.txt` identifies task-touched sources/tests/harnesses; `task-files-manifest.json` records final hashes. No wholesale dirty-worktree diff is attributed to this task. |

## Screen and authority map

| Screen | Runtime authority and consumer |
|---|---|
| Operations Overview | `/ecommerce/overview`: six navigation tiles, **not an operational metrics dashboard**. No invented revenue/status aggregate semantics. |
| Orders | SQL-backed `/api/admin/orders`, detail/status/cancel; `/api/orders/{id}/fulfillment/*`; internal `/api/payments/lifecycle/{id}/*`; SQL reservations/journals/outboxes; Mongo order projection is independently observed. |
| Abandoned Checkouts | `/api/admin/abandoned-checkouts` list/detail/stats; SQL checkout session is current status authority; protected recovery link is consumed by Storefront recovery/checkout. |
| Cart Recovery | `/api/admin/ecommerce/abandoned-cart-recovery/{settings,eligibility,stats}`; tenant settings, native eligibility/scheduling/dispatcher and transport sending gates. This automation model is not conflated with the checkout-session list. |
| Blocked Phones | `/api/admin/phones`, block/unblock; canonical tenant phone identity; Storefront registration/checkout enforcement consumers. |
| Online Customers | Registered **Storefront accounts**, not POS customers/users or heartbeat presence. Counts are unexpired, unrevoked refresh-token sign-ins. `/api/admin/online-customers` and sessions endpoints. |
| WhatsApp Settings | `/api/admin/whatsapp/{accounts,templates,logs,analytics,killswitch/...}`; stored settings and actual provider status are distinct. Historical sent logs do not establish current transport readiness. |

## Confirmed defects and repairs

| ID | Defect and repair | Evidence |
|---|---|---|
| P4-01 | Phone-risk identity normalization fell back to unvalidated alternate text when strict normalization failed. Remove that fallback; malformed phone identities cannot become new risk keys. | Hosted baseline accepted `123`; fixture then unblocked. Invalid-input regression fails before repair, passes after; activated API returns 400. Configured equivalent-format/tenant-isolation/unblock tests. |
| P4-02 | Mixed/missing abandoned-checkout currencies could throw or imply an invalid aggregate. Return unavailable aggregate value rather than summing incompatible currencies. | Two failing-before currency regressions; final tests return null amount / unavailable flag. |
| P4-03 | Abandoned list/detail depended on potentially stale Mongo projection. Read current tenant SQL sessions and exclude invalidated sessions. | Projection-independent list/detail and foreign-tenant/invalidated regressions; hosted conversion readback agrees with SQL. |
| P4-04 | Abandonment selection could overwrite intervening activity and stage an event outside an atomic transition. Take shared cart lock, reload, recheck active/non-invalidated and 30-minute threshold, stage event within transaction. | Failing-before recent-activity regression; configured PostgreSQL stale tracked candidate, three competing writers/one event, staging-failure rollback and fresh cleanup observer. Controlled publisher, not message delivery. |
| P4-05 | Recovery could return a replacement/mismatched cart and historical totals. Require exact current tenant/cart/owner and return revalidated live totals. | Two failing-before exact-cart/current-total regressions; actual Admin link → Storefront checkout → intended cart without duplicate lines. |
| P4-06 | Recovery configuration allowed duplicate submissions and misleading success after failed readback. Add synchronous mutation exclusion, pending-field protection, latest-read/unmount admission, and saved-but-readback-failed error handling. | Same-tick duplicate and accepted-save/readback-failure component tests; hosted save/readback and scoped restoration. |
| P4-07 | Blocked Phones could admit late search responses or display failure as successful emptiness; customer directory also conflated failure with empty data. Add request admission and explicit unavailable states. | Component failure/stale-search regressions; actual unblock has fresh readback. |
| P4-08 | Lifecycle workspaces could retain account/store/route-owned state. Use the existing keyed Storefront workspace boundary for the seven scoped routes. | Existing boundary behavior plus route integration and full frontend tests. Not a two-real-account hosted switching certification. |
| P4-09 | WhatsApp reads could conflate unavailable/empty/ready states, analytics consumed wrong fields and invented unsupported metrics, and an enabled disconnected account could not be disabled in the UI. Separate read states, honor backend fields, show unavailable for unsupported metrics, retain safe disable access. | Failing-before contract/component tests, hosted nonempty analytics/logs and template lifecycle. No invented funnel/circuit-breaker history. |
| P4-10 | WhatsApp log DTO/filter/pagination differed from UI names; uppercase transport codes still fell through known UI labels. Map targetPhone/messageType/failureReason, supported query codes, page count and known status/type values; preserve unknown codes. | Hook regressions, including final red `whatsapp-log-label-red.json`. Last code-normalization repair has automated/build proof, **not a completed browser rerun** after the browser session closed. |
| P4-11 | **Hosted discovery:** detached order detail discarded PostgreSQL shadow `xmin`, returning rowVersion `0`; ordinary versioned status writes failed. Use tracked detail materialization so the concurrency token is present. | Two configured PostgreSQL failures before / passes after; activated API's competing status requests return **200 and 409**, not two invalid-version errors. |

Not every new test was separately run against the original implementation; red artifacts establish only their recorded cases.

## Actual hosted lifecycle evidence

Synthetic scope: `P4QA1792177832429`; registered customer `3e6129bf-5bac-4d26-86da-c7c42cb53833` was an existing QA account, not newly provisioned by this task.

### Recovered order → cancellation

- Recorded cart `619abb28-bf76-48c3-94e8-ed0e07dd969c`, checkout `1efa806b-9dd3-4118-a40e-df51b2e6f025`. API progressive save ignored forged snapshot totals/currency and used current cart values.
- Only this recorded synthetic session was deliberately aged for abandonment, using the real configured service/database with a controlled event publisher. This is not proof of an unattended scheduler firing after a real 30-minute wait.
- Actual Admin abandoned-checkout detail supplied a recovery link. Storefront landing resumed the same customer cart without duplicate lines; repeated pre-conversion recovery remained resumable and did not mark payment or conversion.
- Actual **Place Order UI** created exactly one order: `ee42f429-fa3d-46b6-988a-b900cc3e8648`, **ORD-C9C5-2026-01054**, EGP **35** (25 merchandise + 10 shipping), Pending / Payment Pending.
- Initial harness failed on unsupported Admin `pageSize=100` **after** this commit. Independent readback found the order; Place Order was not repeated. Continuation used that exact order.
- Premature completion and direct Completed-status bypass were rejected. After the row-version repair, competing same-version OnHold requests admitted one writer and rejected one with 409.
- Supported cancellation first returned queued/OnHold while its durable payment-cancellation worker converged. The initial short poll was a harness failure, not a permanently uncancelled order. Fresh SQL/Mongo/API readbacks show **Cancelled / Payment Failed / Financial NotRequired**; repeat cancellation is idempotent.
- This first line was non-stock-reserving; there is no physical reservation to release for this order. Configured physical cancellation/release tests are separate evidence, not inferred from an empty reservation set.

### Physical Manual COD order → completion

- Second order: `258be5dc-92d9-4489-a1c4-0872d7f8b2b0`, **ORD-C9C5-2026-01055**, EGP **40** (30 merchandise + 10 shipping), using existing Manual shipping method `0f1a22f9-e017-4443-b94e-d37053fa7bca`.
- The harness copied the pricing response into expectedPricing, changing `total` but retaining legacy `finalTotal=40`. The DTO's legacy alias set Total back to 40. Request succeeded and committed this second order at **40**, not 0.01. The harness's claimed forged-total rejection therefore **failed**. This is not evidence of an underpriced order, nor a passing canonical forged-total negative test. No third checkout was created to hide/retry that result.
- Existing Orders UI **Confirm**, **Pack**, and final **Complete** actions were executed. A first Pack attempt correctly returned `SHIPMENT_CREATION_PENDING` until asynchronous Manual shipment creation completed; later Pack succeeded.
- Manual dispatch and delivery used actual APIs with explicitly synthetic tracking/reference and `https://example.test/...` evidence. No carrier-provider shipment or real delivery is claimed.
- Delivered but unpaid completion returned **409**. Internal COD collection admitted financial processing; completion became available only after authoritative posting. Final order is **Completed / Payment Collected / Financial Posted / Fulfillment Completed**.
- Repeat completion and repeat internal collection returned success without duplicate stock/revenue effects. Completed order cancellation returned **409**. `browser-completion-second.json`: **11 checks, no failures**.
- Independent configured READ ONLY SQL/Mongo observer: exactly two orders and two converted checkout sessions; one physical reservation **1 committed / 0 released**, exactly one stock-decrease movement of **1 unit**; Manual non-carrier-managed shipment; cancelled order has zero journals.
- Completed order has **two legitimate balanced posted journals**, not duplicate revenue: **Order_COD debit=credit EGP 40** and **Order_COGS debit=credit EGP 10**, each with its own idempotency key and processed accounting outbox. The observer's original one-journal assertion was corrected after inspecting the established COGS authority; production accounting rules were not relaxed.
- Zero WhatsApp Mongo message-log records related to either synthetic order. This is scoped negative persistence evidence, not a global claim about every possible external system.

Primary artifacts: `browser-lifecycle-second.json`, `hosted-order-after-readback-error.json`, `browser-order-continuation-third.json`, `browser-order-continuation-fourth.json`, `orders-after-alias-echo.json`, `browser-completion-second.json`, `hosted-orders-observer-final.json`.

## Other workflow and security coverage

| Area | Evidence strength and limits |
|---|---|
| Recovery settings | Actual UI delay change → fresh authoritative readback → restore prior settings; automation kept off. Hosted operational sending/scheduler delivery was not enabled. |
| Phone block/unblock | Actual API canonical block; equivalent registration formats return `PHONE_BLOCKED` before account creation; additional requests hit real rate limiting. Actual Admin unblock confirmation removes the fixture from fresh blocked-list readback. No synthetic registration account persisted. Not a full hosted blocked-checkout-order matrix. |
| Phone isolation | Existing PostgreSQL transaction: equivalent local/international/formatted identities share one row; foreign tenant remains unblocked; unblock works. Rollback and new-context absence proof. |
| Online Customers | Hosted list/detail agree on existing account session count: 11 at cleanup observation, five registered accounts. PostgreSQL controlled fixture has two admitted sign-ins, excludes one expired and one revoked token, then zero after revocation; rollback observed independently. No heartbeat policy invented; real-user logout/revocation/activation was not performed. |
| WhatsApp templates | Actual inactive UI template creation, API edit/fresh readback, supported delete and 404; default override was absent before authoring. Synthetic content prefixed `P4QAADMIN...`; no sending enabled. |
| WhatsApp logs/analytics | Actual pre-existing nonempty logs and five historical sent messages read; no `undefined` DTO fields. Canonical code/filter/page mappings have automated tests. Last optional populated filter/detail browser harness failed with **Session closed**, so it is not a browser PASS. Message content/retry history not supplied by summary DTO is not certified. |
| Authorization | Real MVC policy host: **32 routes × seven personas = 224 cases**. Controlled authentication and admitted-action short-circuit, not seven real hosted accounts. Manager COD collection denied; Warehouse fulfillment mutation follows existing policy. Actual anonymous six API reads return 401; authenticated foreign-tenant header spoof returns 403. |
| Recovery security | Selected existing malformed/tampered/cross-tenant/owner-change and current-cart rejection tests; hosted converted checkout rejects reused recovery link with **400 / SESSION_CONVERTED**. Not all two-real-customer browser combinations. |
| Localization/responsive | `browser-readonly-second.json`: **69 checks**, EN/AR, 1440px/390px, all seven routes, RTL/LTR, bounded document width, Overview navigation, authorization, zero domain writes/page exceptions. Screenshots `orders-ar-mobile.png`, `whatsapp-ar-mobile.png`. Smoke coverage, not complete modal/translation/accessibility/visual certification. |
| UI races/failure | Controlled component tests cover pending save exclusion, readback failure, unavailable-vs-empty, old searches and workspace remounting. Not a full browser offline/network-chaos/cross-tab/account-switch matrix. |

`browser-admin-workflows-final.json`: **17 checks / no failures / no uncaught errors**. `browser-cleanup-final.json`: **9 checks / no failures**, plus an explicit unresolved account-restoration record. Earlier failed harness attempts remain retained; their partial checks are not counted as complete successful runs.

## Verification results

| Check | Result | Evidence / qualification |
|---|---|---|
| Exact selected backend, earlier completed run | **408 passed, 0 failed, 0 skipped** | `phase4-backend-verified-exact-final.trx`; historical passing run, not a substitute for the later red rerun. |
| Latest selected backend rerun | **404 passed, 4 failed, 0 skipped** | `phase4-backend-closed-final.trx`; four native scheduler tests time out while connecting/initializing their managed PostgreSQL fixtures, before the intended scheduler assertions. Failure details in `backend-closed-final-failures.json`. **Latest run is not green.** |
| Configured PostgreSQL subset | **14 passed** | `phase4-configured-verified-final.trx`; overlaps above. Existing DB, rollback/narrow synthetic cleanup, independent observers. |
| Strengthened final independent observers | **2 passed** | `phase4-cleanup-strengthened-final.trx`, after added one-stock-decrease/two-journal/two-session assertions; no production backend edit after activated build. |
| Admin full suite | **1,920 passed, 0 failed, 0 pending** | `admin-closed-final.json`, bounded `--maxWorkers=2`, after final label source/regression edit. Earlier bounded run 1,919; do not add counts. |
| Storefront full suite | **537 passed, 0 failed, 0 pending** | `storefront-closed-final.json`; no Storefront source modified by this task. Final Vite/esbuild and stale Browserslist advisories retained. |
| Standalone typechecks | Admin and Storefront exit **0** | `npx tsc --noEmit`; Admin repeated after final label source/test edit. |
| Admin production build | Exit **0** | `admin-build-closed-final.log`, after final log-label edit; large-chunk warning retained. |
| Storefront production build | Exit **0** | `storefront-build.log`; unchanged source; chunk-size warning retained. |
| API isolated build | Exit **0** | `api-build-order-version.log`; matching installed/active identity. Warning level suppressed, not a warning-free-source claim. |
| Scoped Admin lint | Exit **0**, **0 errors / 3 hook-dependency warnings** | `lint-closed-final.json`; ESLint legacy-config deprecation advisory also retained. Not whole-repository lint certification. |
| Scoped whitespace | Exit **0** | `diff-check-final.json`, `diff-check-final.log`; baseline-relative tracked check, not attribution for unrelated dirty changes. |

### Failed attempts and test-selection qualifications

- Unrestricted final Admin runs produced **1,918/1** and **1,916/3** passing/failing counts with slow source-scanning architecture checks (`STACK_TRACE_ERROR` after exceeding test budgets). Focused architecture recheck passed, and bounded-worker full run passed without weakening assertions. These red attempts are retained in `admin-verified-final*.json`.
- A class-wide backend rerun accidentally selected additional disposable-cluster PostgreSQL cases beyond the original method selection. Nine displayed failures required an explicit isolated-cluster environment and were guarded before fixture provisioning; run timed out at 300 seconds and has no completed TRX. No cluster/database was provisioned to make them pass. The exact final method selection is **not** an unrestricted full-backend pass.
- First configured closeout rerun omitted the pre-existing inventory tests' required `SHIPPING_FINAL_EVIDENCE_DIRECTORY`, causing six harness `Path.Combine(path1=null)` failures before fixture writes. Supplying the evidence directory yielded **14/14**, without changing application code or assertions.
- Browser harness corrections included unsupported Admin page size, wrong heading scope (`POS System` shell vs main), breadcrumb included in tile count, ambiguous Analytics selector, expected 403 vs actual documented PHONE_BLOCKED/400, real registration rate limits, asynchronous cancellation/shipment delays, and the conflicting expected-total aliases described above. None is silently presented as passing product behavior.
- Phase 3's **27** broad-backend failures remain open historical context: 23 documentation/source-shape cases, three distributed reliability cases and one POS role-architecture assertion. They were not repaired or rerun here. Earlier R2 readiness failure is retained, while the current readiness observation is separately recorded.
- The last backend rerun finished after the user requested a narrower Customer trust/Arabic follow-up: **404 passed / four native scheduler fixture failures**. Stacks show `ManagedTestDatabase.CreateDatabaseIfMissingAsync` / baseline EF migration initialization and connection/read timeouts. That legacy fixture targets generated test database names, not the configured application database; its creation/migration attempts mean a blanket "no database provisioning/migrations anywhere" claim would be incorrect. No broad database cleanup/drop was performed. Auxiliary fixture residue has not been independently certified absent. Existing configured-application observer evidence and earlier green counts remain distinct from this failed fixture initialization.

## Cleanup and retained effects

Independent final SQL observer: `hosted-cleanup-independent-sql.json`; lifecycle SQL/Mongo/financial observer: `hosted-orders-observer-final.json`; actual API/customer-cart observer: `browser-cleanup-final.json`.

| Scope | Final disposition |
|---|---|
| Orders/checkouts | The two recorded orders, two converted checkout sessions, audit/outbox/payment history and Manual shipment are intentionally retained. Terminal business/financial records were not hard-deleted or rewritten as if no work occurred. |
| Physical stock/accounting | One QA unit consumed; EGP 40 synthetic COD revenue and EGP 10 COGS posted. Movement snapshots retain an additional reserved unit outside this synthetic reservation; it was not globally cleared or reconciled. No compensating inventory reset, cash adjustment, refund or broad reconciliation was fabricated. |
| Customer basket | One recorded second-order fixture line remained after API checkout; supported DELETE then fresh **customer-owned** GET confirmed zero items/subtotal. Initial customer cart was empty. This is not a session-cart-only cleanup claim. |
| Phones | `123` and canonical `+201000000099` profiles remain **unblocked**, zero spam reports. Supported unblock intentionally retains profile/reason/audit history; no physical-absence claim. No P4QA block remains active. |
| Registration/template fixtures | Independent SQL: zero `p4qaadmin...` accounts and zero P4QA WhatsApp template rows; API template 404 and blocked-list fresh readback also checked. |
| Recovery settings | Prior disabled automation restored; first delay 60, second 1440, Africa/Cairo. No external recovery delivery enabled. |
| WhatsApp account | **Not restored**; QA-disabled account and provider blocker identified prominently above. No delete/recreate or SQL status overwrite was used. |
| Prior Phase 3 coupons | **21 archived P3QA coupons, zero active, zero usage**, independently observed and preserved. |
| Controlled SQL fixtures | Session/phone transactions rolled back; abandonment concurrency fixtures narrowly cleaned by recorded IDs/tenant; new-context absence asserted. No global history cleanup. |

## Remaining boundaries and handoff

1. Restore the QA-paused WhatsApp account through supported transport verification when its provider is available. Until then, this is **not clean restoration or messaging readiness closure**.
2. No external delivery/OTP/recovery-notification proof, full scheduler/retry/replay chaos test, real payment settlement/refund, real carrier operation, cash handover, full two-store/two-customer browser security matrix, or complete accessibility/localization certification.
3. Do not rerun prepare/checkout harnesses blindly: they can commit additional business records. Existing synthetic order IDs above are the continuation authority. The last optional log-filter script has no successful hosted result.
4. Unknown log codes remain unknown rather than coerced into success; unsupported analytics/content fields are not invented. Legacy expectedPricing alias precedence remains existing API behavior; this run proves the saved amount stayed server-priced, not that conflicting duplicate echoes are rejected.
5. HEAD/index remain unchanged; task files are uncommitted for review. Stop at Phase 4. The account blocker and explicit test/coverage limitations prevent an unqualified PASS.

## User-reported follow-up

The subsequent Arabic/Customer trust follow-up independently reproduced that `/ecommerce/blocked-phones` still fell back to English because its translation namespace was absent, and that a manually blocked phone could retain a heuristic SAFE result. Thus the earlier EN/AR route smoke is **not proof of translation completeness or complete trust/block integration**. Follow-up evidence is isolated under `.ai/evidence/storefront-trust-20261017/`; its changes/results must be reviewed separately rather than retroactively counted in the original passing runs.

Those focused defects and additional order-phone/history issues are now repaired and activated. See [Customer trust and Blocked Phones follow-up](SHOPIXY_STOREFRONT_CUSTOMER_TRUST_AND_BLOCKED_PHONES_20261017.md): 26 live EN/AR round-trip checks, 66 focused backend cases and 1,930 full Admin tests pass. Manual blocking overrides the displayed Safe badge; Safe remains a heuristic, not a guarantee. This does not erase the wider red runs or WhatsApp restoration issue above.
