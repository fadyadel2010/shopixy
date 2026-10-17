# Shopixy end-to-end quality and integration closure — 2026-10-15

**Disposition: LIMITED CLOSURE — repaired code activated; selected regressions and one real unpaid COD journey verified. Residual lint, incident attribution and production shipping/TLS prerequisites remain open.**

Evidence: `.ai/evidence/e2e-closure-20261015/`. Earlier reports and results remain historical evidence; overlapping counts will not be added.

## Remaining-findings ledger

| Item | Status | Evidence / next boundary |
|---|---|---|
| A1 initial pending Admin run | verified | Independently inspected completed historical JSON and exit record: **1,761 passed /0 failed /0 pending**, 226 files, exit 0. No active Vitest/testhost at entry. |
| A1 final current-source Admin/build/typecheck | verified | **Admin 1,764/1,764**, 229 files; **Storefront 528/528**, 61 files; both typechecks and builds exit 0. |
| A2 cache failures | repaired and activated | Strict generation admission; obsolete source assertion reconciled to actual owned API/Lua behavior; redundant controller headers removed. Focused **21/21**, expanded backend **903/903**; user-coordinated Debug restart verified. |
| B original overnight cancellation | unproven | The tenant's retained cancelled order predates this run and was cancelled before its reservation TTL. No matching payment intent, webhook, audit or retained outbox attribution. Exact incident order ID/time is still required. |
| B current expiration policy | verified within selection | Four new two-day-old reservation cases and existing cleanup/lifecycle/replay coverage pass. No configured-history cleanup was run. |
| C real customer journey | verified, pending/unpaid | One new authorized customer and one COD order, **EGP 40**; correct SQL/Mongo tenant/customer/variant/reservation linkage; history and Admin read 200; cart empty; zero journal/accounting-outbox/shipment rows. |
| D WhatsApp OTP | live success | Exactly one OTP request; provider log **SENT**; user confirmed receipt and entered code directly; scoped verification true; fresh guest scope unverified and foreign tenant rejected. |
| E live verification email | delivery/consumption verified; UI repair verified under controlled startup | Registration reports submitted mail; user confirmed inbox receipt; SQL token used and email verified. Token reuse **400 TOKEN_USED**, verified-account resend **400 ALREADY_VERIFIED**. Actual startup with synthetic success preserves the token, issues one verification POST and shows Email Verified. |
| F Bosta authentication/readiness | authentication verified; execution blocked | Saved production credentials pass non-booking connection test. Configuration disabled, geography mapping unpublished, account NotReady; no activation. |
| F one shipment | blocked, zero bookings | Actual parcel measurements/contents and explicit pickup selection still missing. Random data is offline-only. Provider/account/geography readiness also required. |
| G maintained lint | improved; not closed | **266 errors /188 warnings**, down 48 errors; full maintained command exits 1. Exact current file/line/rule ledger retained. No rule suppression. |
| H certificate/Caddy/readiness | diagnosed, operational prerequisite | Both integrations explicitly disabled in base Development configuration; latest five ready reads 200/Degraded. Earlier R2 timeout caused a real 503; not erased by later success. |
| Cross-domain integrity | observed, not declared globally clean | SQL/Mongo reads and six configured synthetic rollback cases pass. Aggregate exceptions are classified below, not silently repaired. |

## Entry environment and preservation

Entry listeners: API 5010 PID1784, Admin 5173 PID14300, Storefront 5174 PID13352, WhatsApp bridge 3000 PID12444. API started 2026-10-15T17:57:03.675765Z, Development, original Debug apphost/content root. `dotnet run` and its MSBuild node were present, but no active testhost/Vitest suite was found. Assembly/configuration hashes are in `api-before.json`; Git inventory/statistics in `baseline-git-status.txt` and `baseline-diff-stat.txt`. Existing named Edge session and merchant portal tab are present. The user later explicitly stopped only the API for coordinated activation; final identity follows below.

## Evidence boundaries and inherited work

Reviewed the POS Admin, Storefront, quality-closeout, residual-invariant, cross-domain-integrity and cumulative shipping-final reports, plus the carrier-connection report. Earlier conclusions remain historical evidence, not additional cases in this run. Test counts below are **per invocation and overlap; never sum them**.

The worktree was already extensively modified. All existing source, untracked work, configuration, logs, uploads, browser sessions and business history are retained. Baseline copies of every shared source file edited here are under `baseline-source/`. No staging, commit, stash, reset, clean, database reset, migration, history repair, real charge, capture, refund, settlement, pickup or physical dispatch was authorized or performed.

The explicit live additions are **one customer, its normal authentication/email-verification records, one cart, one quantity-one COD order and its reservation**, plus normal analytics/session/provider diagnostic records. The first authorized mailbox already existed and correctly returned `EMAIL_EXISTS`; it was not modified. The replacement mailbox and generated password remain private. Original Storefront/Admin login contexts were not replaced by the test identity.

## A. Cache ownership and remaining backend failures

### Findings and repairs

1. **Real defect:** an invalid Redis generation was treated as generation zero, and negative generation text could match a forged/old envelope. `RedisJsonCacheService` now admits only a complete two-value owned read and invariant, non-negative integer evidence. Only a genuinely missing generation key means initial zero. Corrupt/incomplete evidence misses/fails closed; it does not revive stale cache entries.
2. **Obsolete assertion:** product reads intentionally use `GetOrCreateOwnedWithStatusAsync`, which distinguishes a freshly SQL-composed DTO from a cached candidate. The assertion now checks the actual owned boundary, not the older method spelling.
3. **Obsolete assertion:** Lua atomically compares generation and fills cache; requiring the old `Condition.StringEqual` transaction API did not describe current behavior. Replacement coverage executes corrupt/negative rejection, required invalidation failure and late-fill fencing. The actual existing Redis test advances eight generations while a fill is delayed, proves it cannot persist, then proves fresh population/warm hit/subsequent invalidation. Only UUID-owned test keys are removed; no flush or historical cache cleanup.
4. **Actual ownership duplication:** page-preview/payment/shipping controllers also wrote cache headers. Those redundant writes/helpers are removed; `StorefrontHttpCachePolicyMiddleware` remains the sole owner. Seven real-Kestrel HTTP cases prove a late handler cannot restore public caching, ETag or Last-Modified across 200/400/401/403/404/503 responses. These are controlled HTTP handlers, not seven actual hosted business endpoints.

The initial focused red run was **9 passed /5 failed /0 skipped** (three existing architecture failures plus two new malformed-generation cases). Rebuilt focused green: **21 passed /0 failed /0 skipped**, exit 0, `phase5-cache-green.trx`. Expanded current Release selection: **903 passed /0 failed /0 skipped**, exit 0, `phase5-backend-broad-final.trx`; exact selection is `backend-final-filter.txt`.

The first expanded invocation reached one failed observer assertion: it incorrectly equated checkout Redis phone proof with SQL profile `PhoneVerified`. Source tracing established these are different authorities. The corrected observer retains SQL `PhoneVerified=false` explicitly; `otp-scope-proof.json` separately establishes the actual checkout verification response. This is not a production change or a claim that the permanent profile flag was updated. A PowerShell native-stderr handling issue also prevented that invocation's normal exit summary; the final runner records independent process exit codes and complete stdout/stderr.

**Deployment boundary:** backend tests use newly built Release assemblies; the original real registration/OTP/checkout used the entry Debug API. After the user stopped it, current Debug artifacts were built to a separate output and only nine named executable/assembly/debug/dependency artifacts were copied to the original output. Configurations, logs, uploads and other content were excluded. The first build succeeded in 69.54 seconds but the PowerShell process-tree wait remained attached to the reusable compiler server and the wrapper timed out; no install was claimed from it. The corrected runner waits for the build process itself, rebuilds successfully, records exit 0 and verifies installed hashes. The user then restarted normally.

`api-after.json` and `runtime-activation-verification.json` verify PID **18304**, started **2026-10-15T18:54:55.1499333Z**; original executable/content root/Development/http launch environment and full environment fingerprint unchanged; all source configuration hashes unchanged; all three loaded assembly hashes match the installed build and predate the new process. API SHA-256 is **300ECA3C7CFA1DC9E3760C0235A782A9264468D26B4AA5B12DC09D575CC210A2**; Infrastructure **109E5576AA7EDC0766AC3F332306FE4390EC0C75C40C8F3F0990142220E348E3**; unchanged Core **460FB7E7F24DAF07F47548B54E88A1406381355A17E81B37281F9BE26AB14D82**.

Post-restart actual product by-ID, product slug, shipping-method and payment-method reads return **4×200**; missing CMS page **404 CMS_PAGE_NOT_FOUND**. Every response has the complete central private/no-store policy and no ETag/Last-Modified. Test `/me`, order history and empty cart remain **200**; Admin order remains **200 /Pending /Payment Pending**. Additional real UI reads show the owned order in both the Admin detail route and customer account, with zero mutation clicks. The original customer's raw expired-access-token `/me` first returned 401; its normal `checkAuth` refresh flow restored authenticated status and proves it was not replaced by the test customer. See `hosted-post-restart.json`, `owned-order-final-ui.json` and `original-session-preservation.json`.

## B. Overnight cancellation and current expiry policy

Read-only repeatable-read SQL observation covers the selected tenant's cancelled orders, intent timestamps/status, reservation expiry/release, verified webhook metadata, audit actor-presence/source and outbox event metadata; no payloads or personal cancellation text are retained.

The only retained cancelled order observed is `ORD-C9C5-2026-01037`: created **2026-09-28 19:54:29.732902 UTC**, cancelled **19:59:20.098685 UTC**; its reservation expired **20:04:24.738438 UTC**. It is COD with one released unit. There are no matched payment intents, payment webhooks, audit records or retained aggregate outbox events. Cancellation occurred about five minutes **before** the reservation expiry, not overnight. This does **not** establish who cancelled it or identify the user's reported incident. Required remaining evidence: original order ID/tenant, approximate incident UTC/local time and retained API/job/provider traces or audit identity. No age-based attribution is invented.

Current source uses `DateTime.UtcNow` and UTC cutoffs, protects live COD independently of reservation age, protects confirmed/processing/shipped/delivered/completed/on-hold orders, collected/paid/settled payments and paid-intent evidence, reloads authority before cleanup, and enters the canonical locked payment lifecycle for expiry. Provider reconciliation and duplicate/replayed transition handling remain in the existing lifecycle. A stale reservation timestamp is not payment or cancellation authority.

Four new isolated-SQLite cases execute cleanup twice after making reservations two days old: live pending COD; confirmed authorized payment; confirmed paid payment; and pending-order state with a paid intent. Each retains order/payment state, all reserved quantity and zero release/commit. Existing lifecycle/expiry/concurrency/idempotency coverage and six configured rollback cases are included in the expanded selection. Configured PostgreSQL tests address only synthetic UUID-scoped rows inside rollback transactions and check fresh observers afterward; global cleanup is **never** run against historical data.

The real new COD order also remains Pending/Payment Pending with its reservation active **after** its stored TTL. That is expected COD protection, not an instruction to expire or cancel it.

## C. Real customer and COD checkout

| Boundary | Observed result |
|---|---|
| Storefront registration UI | **200**, new customer `e829b8e0-7c97-41cb-8cfa-3c9644ac69ad`, verification-mail submission true, confirmation visible; correlation `a29419e6-726e-49fa-9654-1c8bdd9e31f1` |
| Normal login UI | **200**, separate context; correlation `ff02a95e-9cc2-4846-b370-0f2cdcb458da` |
| Product/variant | `cert-media-variable-20261007`, option A; backend says In Stock (5) and enables Add to Cart |
| Cart add | **200**, cart `654f87c5-14f2-439d-8bbc-43ffb209e62c`, one unit, variant `1435b729-5fbe-48f1-a306-2e12051ad24b`, EGP 30 |
| Checkout UI | Authorized recipient/contact/address entered privately; standard shipping EGP 10; only COD offered; Review initially blocks order until phone proof |
| Submission | One Place Order click; normal saved-COD pending screen displays `ORD-C9C5-2026-01053` |
| Persisted order | `ac89be7f-5e8c-4834-bcec-a1d14d5af2e6`; correct tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d` and online customer; **EGP 40**, Pending /Payment Pending /COD |
| SQL inventory | One physical line and one exact order-item/product/variant/warehouse-bound active reservation; reserved 1, committed 0, released 0 |
| Payment/accounting/shipping | No payment intent, order-linked journal, accounting-outbox row or shipment; no charge, capture or settlement |
| Mongo projection | Exactly one tenant/customer-bound order document; status/payment/total/currency/variant match SQL |
| Storefront history/cart | Both **200**; exactly the owned order appears; cart has zero items |
| POS Admin order | **200**, matching order number, tenant, Pending /Payment Pending and EGP 30 quantity-one line |

The submission listener lost its response body when the page navigated (`Network.getResponseBody` protocol error). **No retry was made.** The actual UI, Storefront/Admin reads and authoritative read-only SQL—not a fabricated success response—establish the saved result. Live duplicate submission was not replayed; one owned order is observed and deterministic same-tick/durable coordinator/retry coverage remains in the maintained tests. The owned pending order/reservation is retained; no unrequested cancellation or stock cleanup follows.

A live cart read also exposed links built from the variant SKU rather than an admitted product route identity. Both links now use the canonical product ID and have a render regression; existing product-detail by-ID routing owns the actual data lookup.

## D. WhatsApp evidence

Existing account was **enabled /Connected**, OTP=true, order-confirmation=true, actual account status **200 /isConnected=true**. No account/session was activated or replaced.

- Exactly one genuine UI request to the designated number ending **3408**: **200 /success=true**, 300-second validity, correlation `309c960c-6457-4c0a-8409-70a89d87f504` at **18:19:13 UTC**.
- Provider message log `87e8d326-6518-4858-aa5d-8c9d07ab4049` becomes **SENT**, timestamp **18:19:21 UTC**. SENT is provider submission evidence, not a delivery/read receipt.
- User explicitly confirms receiving and directly entering the code. The OTP is neither requested in chat nor retained. Order submission becomes enabled.
- Actual phone-verification-status **200 /isVerified=true** in the customer context. This is time-limited, normalized-phone-specific Redis session/customer proof. SQL profile `PhoneVerified=false` remains visible and is not misreported as true.
- Independent fresh guest scope: **200 /isRequired=true /isVerified=false**; no issued code plus an invalid verification attempt returns **400 OTP_EXPIRED**; forged foreign tenant returns **403 TENANT_FORBIDDEN**. No extra message is dispatched for these checks. This negative request does not prove a live wrong-code attempt against the issued OTP; controlled service tests cover that path.

The inherited configured bridge-contract test queries a tenant-named session and accepts its **404 /Unknown** as a supported negative contract. It is **not** positive messaging readiness and does not override the actual account status, real SENT log and user's receipt above. The subsequent Admin log read also shows one new **ORDER_CONFIRMATION /SENT** record `c38ff486-348a-4978-ac71-0d20d2558ce6`, timestamp **18:21:09 UTC**. Its skinny Admin DTO omits order correlation; the final read-only Mongo observer verifies **exactly one log for the owned order, SENT, SendAttempts=1, provider message ID present**. No manual confirmation-message replay or resend was requested. Inbox receipt/read of this second message is not claimed from SENT alone. Total actual message paths observed for this journey: one OTP and one automatic order confirmation, both only to the authorized contact.

## E. Verification email and token ownership

Registration returned `verificationEmailSent=true`, and the user confirmed real inbox delivery and supplied the received verification link. SQL shows one email-verification record created **18:10:31 UTC**, expires next day, used **18:13:28 UTC**; the customer is EmailVerified=true. Subsequent real link consumption and direct reuse each return **400 TOKEN_USED**, verified-account resend **400 ALREADY_VERIFIED**, and `/auth/me` **200 /emailVerified=true**. No second email was sent.

The first consumption exposed a real frontend defect: the checkout startup URL scrubber treated all `token` parameters as checkout tokens. Depending on router timing, verification could complete and then render Invalid Link, or the token could be erased before routing. Authentication verification/reset routes now keep ownership of their own one-time token. Analytics independently removes all query/fragment data. Actual tracking-route token scrubbing remains intact.

`token-ownership-before-after.json` runs identical synthetic URL checks against preserved baseline and current source without swapping any user file: baseline incorrectly strips both auth tokens; current preserves both while continuing to strip tracking tokens. Maintained regression **4/4**. Initial Vitest attempts used an unavailable DOM environment and are retained as harness failures, not claimed as defect reproductions. `auth-startup-browser.json` additionally starts the actual hosted app/router/StrictMode with a synthetic token and controlled success response: **one verification POST**, intact token, **Email Verified!**, success action present, no Invalid Link, zero real verification requests or business writes. The already-used real token cannot certify a second fresh post-fix live-email success; the persisted actual consumption and single-use result are separately verified. Token-bearing URLs are removed from the browser after checks and never copied into evidence.

## F. Bosta production readiness, no booking

The saved encrypted production API credential is reused; no secret is printed, copied into source or replaced. Connection-test endpoint returns **200 /succeeded=true**. Existing authenticated Bosta merchant portal independently returns **200 /success=true** for the JSON pickup-location list: one location, default=true. No location was created or changed.

Current application account remains **ConnectionTestPending /NotReady**. Readiness is `enabled=false`, `canExecute=false`, production capability Partial, blockers **CARRIER_CONFIGURATION_DISABLED** and **CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED**. The saved credential field list contains only `apiKey`; the adapter also requires an explicitly bound provider `businessLocationId` for booking. Presence of one portal default does not authorize silently selecting it or assert that its address matches the order warehouse.

**Zero shipment submissions, labels, pickups, dispatches or provider cancellations.** The user's random-parcel suggestion is treated only as permission for synthetic offline tests, not for fabricated production parcel declarations.

Remaining booking prerequisites:

1. Actual parcel contents, measured weight, measured length/width/height and admissible package/declared-value/COD data, with one controlled-order scope.
2. Explicit selection and confirmation of the provider pickup location against the order's warehouse/origin; retained origin and recipient canonical address evidence.
3. Authorized carrier configuration/service activation and published compatible country/city/district mapping; successful current connection and operational readiness.
4. Confirm no existing shipment/binding/idempotency outcome for the order and confirm the request does not schedule pickup, create unapproved charges/collection or trigger dispatch.
5. Only then submit at most the single authorized shipment and verify provider ID/tracking/label against SQL, Mongo and portal. Do not retry an ambiguous timeout blindly.

## G. Lint and frontend correctness

Retained full lint baseline: **314 errors /188 warnings**. Largest rules: unused-vars 186, exhaustive-deps 145, explicit-any 73, fast-refresh 43, restricted-imports 34, control-regex 12. These are rule occurrence counts across both severities, not disjoint test cases.

Bounded repairs preserve surrounding behavior and do not disable or weaken any rule:

- `ProductDrawer`: moved the early empty-product return after `useMemo` so open/close of the same mount cannot change hook order; mounted transition regression.
- `CustomerSelectModal`: removed browser-invalid `process.env` from failure handling and stopped logging Axios error/config/response/contact/token material; failure regression retains inputs, reports the toast and re-enables retry, logging only HTTP status.
- Removed the unreachable duplicate SSL-tab return, unused imports/bindings in touched domain/theme/history pages, and expression-only Set mutations in affiliate detail (equivalent explicit if/else).
- Replaced security control-character regexes with a shared typed **identical ASCII code-unit policy**, preserving all other URL/path/length/credential rules and both slash rejections for attachment names. Exhaustive 65,536-code-unit classification checks retain NUL–31 and DEL rejection and non-ASCII SKU handling, including lone surrogates. This is not a wider Unicode normalization or weaker validation.
- Authentication-token ownership and cart-product navigation regressions described above.

Final maintained command: `node scripts/lint.cjs --format json --output-file ../.ai/evidence/e2e-closure-20261015/lint-final-project.json`, exit **1**, **266 errors /188 warnings** across 238 files with findings. The exact remaining file/line/column/rule/message ledger is `lint-residual-ledger.json` and `lint-residual-ledger.md`; the full original ESLint JSON is also retained. Current rule totals:

| Rule | Errors | Warnings |
|---|---:|---:|
| `@typescript-eslint/no-unused-vars` | 156 | 0 |
| `@typescript-eslint/no-explicit-any` | 73 | 0 |
| `no-restricted-imports` | 34 | 0 |
| `@typescript-eslint/ban-ts-comment` | 1 | 0 |
| `no-regex-spaces` | 1 | 0 |
| `no-empty` | 1 | 0 |
| `react-hooks/exhaustive-deps` | 0 | 145 |
| `react-refresh/only-export-components` | 0 | 43 |

The conditional-hook, undefined `process`, unreachable return, unused-expression, control-regex and useless-escape findings addressed here are absent in the final lint. Remaining warnings/errors are not presented as closed merely because builds pass. Priorities: lifecycle-safe dependency reviews (not blindly adding dependencies), typed replacement of boundary `any`, deliberate resolution of API-client import ownership, and remaining dead bindings. The FAQ JSON empty catch, repeated-space regex and TypeScript-comment finding remain explicit in the ledger. Those broad changes need their own behavior coverage; no large client rewrite or blanket autofix was applied.

Final broad Admin run: **1,764 passed /0 failed /0 pending**, 229 files, success=true, process exit 0; **18:41:54–18:48:33 UTC**. Final Storefront run: **528 passed /0 failed /0 pending**, 61 files, exit 0. Both strict `tsc --noEmit` checks and both Vite production builds exit 0. Builds go to separate `.opencode-tmp/e2e-closure-20261015/*-dist` paths; they do not replace the live Vite processes. Exact commands/times/exits are in `frontend-command-results.json`.

Broad stability runs use one worker without concurrent heavy builds/tests; no increased timeout, retries, excluded case or replacement suite is introduced. The extra three Admin cases versus historical 1,761 are the drawer transition, customer failure/privacy and exhaustive character-policy regressions; the extra five Storefront cases versus historical 523 are four token-ownership cases plus one cart-link regression.

## H. Readiness and certificate/Caddy diagnosis

The API starts in Development with configuration order: base JSON → Development JSON → Development.local JSON → Development user-secrets → environment variables. Base configuration explicitly has **CertificateProvider.Enabled=false** and **CaddyEdge.Enabled=false**; inspected Development/local/user-secrets sources provide no enabled override. Live health independently confirms both execution gates disabled. Required provider account/directory and Caddy managed-route root/storefront root/trusted upstream/validate/reload settings are absent in those inspected files. Presence-only evidence is retained; secrets are not dumped.

This is an intentional disabled/unconfigured local execution state, not evidence of expired production certificates or an authorization to turn on ACME/Caddy. Custom-domain TLS activation remains an operational prerequisite requiring the correct deployment environment, provider account/directory/contact, trusted edge paths/commands/upstream, DNS/domain admission and an explicitly authorized activation. No certificate issue/revoke/renew or edge reload was run.

Earlier full readiness sample: **503 /Unhealthy**, correlation `74faafb2-4401-46bc-b6b2-1480c6bf6f32`, R2 unavailable after approximately five seconds; PostgreSQL, Redis, Mongo and bus healthy in that sample. R2's read-only `ListObjectsV2(MaxKeys=1)` uses a five-second dependency timeout. Five subsequent independent live/ready pairs: live **5×200 Healthy**; ready **5×200 Degraded** with only disabled certificate/Caddy entries non-healthy; R2 reachable in **177.7–406.8 ms**. Full payloads/correlation IDs are retained. This establishes intermittent dependency behavior, **not** proven root cause or an uninterrupted healthy SLA. No timeout/gate was relaxed to make readiness green.

After coordinated restart, five further live/ready pairs again return **5×200 Healthy /5×200 Degraded**, only certificate/Caddy disabled; full post-restart payloads are in `health-post-restart.json`. These are separate samples, not a claim of continuous readiness between observations.

## Cross-domain data truth

Current configured observation reads existing PostgreSQL/Mongo without repair: **53 applied migrations, 0 pending**, 16 aggregate relationship checks, 853 constraint metadata records and 15 triggers. All 12 shipment/order projection comparisons match canonical SQL state/totals, with no mismatch fields. Historical fulfillment overrides are compared using the canonical progression, not raw shipment labels.

Two nonzero aggregate observations require explanation, not a false clean assertion:

- `reservation_missing_order_or_line=27`: the Storefront-only join excludes CommerceHub ownership. All 27 are classified as existing CommerceHub orders with valid corresponding CommerceHub line/variant lineage (15+6+5 terminal and one active). This is an observer-domain limitation, not demonstrated orphan corruption. No records were removed.
- `expired_open_reservations=1`: the new live COD order's active quantity-one reservation has crossed its stored timestamp. Current COD lifecycle deliberately protects it. Stock/open-reservation balance still matches; no invalid stock buckets, cross-tenant product/warehouse lineage, duplicate active shipment, money mismatch, duplicate settlement membership, unbalanced journal or cash-clearing mismatch was observed.

The aggregate outbox observation has no unprocessed/permanently failed rows in its two returned event groups. This does not claim every bus queue or every historical message family has been exhausted.

## Final verification and preservation

`final-summary.json` records per-command results without adding overlapping selections. The strengthened final Release rebuild and backend selection again finish **903 passed /0 failed /0 skipped**, exit **0**, including exact SQL/Mongo order, no accounting-outbox/shipment and order-confirmation evidence. The previous 903-case TRX is preserved as `backend-broad-before-strengthened-observer.trx`; the first failed observer run remains `phase5-backend-broad.trx`. This is a selected safe backend run, **not the entire repository suite**: managed/destructive PostgreSQL fixture selections are excluded explicitly in `backend-final-filter.txt`, not skipped and counted as passes.

| Final invocation | Passed | Failed | Pending/skipped | Exit |
|---|---:|---:|---:|---:|
| Expanded safe backend, Release | 903 | 0 | 0 | 0 |
| Full maintained Admin, 229 files | 1,764 | 0 | 0 | 0 |
| Full maintained Storefront, 61 files | 528 | 0 | 0 | 0 |
| Focused cache (overlaps backend) | 21 | 0 | 0 | 0 |
| Both frontend typechecks/builds, Debug API build | n/a | n/a | n/a | all 0 |
| Full maintained Admin lint | n/a | **266 errors** | **188 warnings** | **1** |

`preservation-comparison.json` records **zero missing baseline status entries** out of 1,350; shared-file `git diff --check` exits **0**. Twenty-three shared-file baseline copies and `owned-baseline.diff` isolate this phase's edits; `final-git-status.txt` and `final-diff-stat.txt` preserve the full worktree. Existing Windows LF/CRLF advisories are not whitespace-check failures. The final private-input scan covers **168 text artifacts including this report**, with **zero authorized email/phone/password/address/verification-token matches**; the private input itself is outside evidence/source. No staging or commit. Final listeners retain the original Admin **14300**, Storefront **13352** and bridge **12444**; only API changes to the user-coordinated **18304**. `api-final.json` and `runtime-final-verification.json` recheck that same process, all installed loaded hashes, content root, complete environment fingerprint and configuration hashes successfully at closeout.

### Remaining prerequisites, prioritized

1. **Original incident attribution:** supply the actual overnight-cancelled order ID/tenant/time and retained API/job/provider/audit evidence. Current protection and a different historical cancellation do not prove the original cause.
2. **Bosta live booking:** valid real parcel/contents and explicit pickup/origin/address confirmation; enabled configuration/service, published compatible geography, operational activation and duplicate/irreversible-effect check. **No shipment was booked.**
3. **Maintained lint closure:** resolve the exact 266-error/188-warning ledger with behavior-specific reviews; no exclusions, suppressed rules or broad API-client rewrite was used here.
4. **Production/custom-domain TLS:** deliberately configure and authorize certificate-provider/Caddy execution in the intended deployment, including domain/DNS and edge validation; local disabled state is not live TLS certification.
5. **R2 reliability:** investigate/monitor the retained five-second timeout and correlate dependency/network/provider telemetry. Later success does not erase the earlier 503.
6. **Coverage limits:** no charge/capture/refund/settlement, provider shipment/label/pickup/dispatch, live OTP abuse/exhaustion sequence or order-confirmation inbox/read receipt was exercised. No fresh second verification email was sent after the UI repair; controlled startup covers that success screen, while the real consumed token proves persistence/single use.

The owned customer and pending COD order/reservation remain intentionally in the existing environment. Any later cleanup/cancellation requires separate authorization. **No global or production certification is claimed.**
