# SHOPIXY Paymob P9 Real Declined Payment Retry Forensic Remediation

Date: 2026-08-16

Certification: **BLOCKED**

## 1. Executive Summary

The most recent browser-created Paymob order available in the development database is `ORD-83B3-2026-01016`, order `afdbb817-1740-467b-901d-2c69f1f9d8b1`. Read-only inspection found no Paymob webhook for this or any other order. The browser return therefore left canonical payment authority at pending, which is correct: a browser redirect is not financial failure evidence.

The application had a separate confirmed integration gap. P8.1 retry authority and the secured retry endpoint already existed, but canonical storefront status did not expose that authority and `CheckoutPendingPage` did not call the endpoint. P9 now projects the existing authority as `canRetryPayment`, renders `Pay Again` only when that value is true, calls the existing endpoint, validates the returned fresh Unified Checkout handoff, and redirects without supplying a payment-intent or provider identity.

Certification is blocked rather than passed because the exact sandbox decline cannot be verified while Paymob endpoints are configured as localhost, and the current backend test project has a pre-existing compile error at `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:798`. The API and both SPAs build, the full Storefront suite passes, EF has no pending model changes, and no payment-finality behavior was weakened.

## 2. Real Runtime Incident

Latest correlated browser evidence:

| Entity | Identity | Evidence |
|---|---|---|
| CheckoutSession | `16faf14b-f063-4027-96d4-685e067acacc` | Active; cart `cef05807-3d9e-4b1f-86f2-5c99049e6698` |
| Order | `afdbb817-1740-467b-901d-2c69f1f9d8b1` / `ORD-83B3-2026-01016` | Created 2026-08-14 19:00:39 UTC; PAYMOB; EGP 410.00 |
| Attempt 1 | `bebffb4e-b3ad-4fe0-a7b4-93a55ff0cfea` | Provider intention `587573231`; now Expired after session rollover |
| Attempt 2 | `25217cbf-75c6-485f-bd3b-3d5e6e0f9bfd` | Provider intention `587737362`; persisted Pending; protected handoff present |

The exact alleged card-decline sequence is not represented by server evidence. There is no provider transaction ID, signed failure event, response code, or webhook timestamp. Therefore it is not possible to assert that Paymob financially declined this exact local attempt. The first verified runtime failure is: **Paymob failure finality never reached SHOPIXY because zero Paymob webhooks were received, while development callback/webhook URLs are localhost** (`PointOfSale.API/appsettings.Development.json:23-24`).

## 3. Canonical Payment State

Immediately after the browser return captured by the prior P7 evidence, the state was Case B:

- Order status: Pending
- Order payment status: Pending
- Latest PaymentIntent status: Pending
- Checkout state: PaymentPending/PendingPaymentIntent during the observed sequence

Current read-only state after the later session-expiry remediation:

- Order status: OnHold (numeric persisted value 8)
- Order payment status: Pending
- Checkout state: PendingPaymentIntent
- Latest PaymentIntent: Pending, created `2026-08-14T23:41:04Z`, expired handoff timestamp `2026-08-15T00:41:04Z`
- Provider: Paymob
- Provider intent: present
- Provider transaction: absent
- Merchant attempt correlation: local attempt identity is used by provider creation (`PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs:195-241`)
- Protected handoff: present and redacted
- Client secret: present only inside protected handoff; never reported
- Attempt lineage: Expired attempt 1, Pending replacement attempt 2

The canonical answer after Paymob return is **PENDING**, not Failed. The UI must not offer failure retry for this incident.

## 4. Paymob Webhook Evidence

Read-only database inspection returned zero Paymob `PaymentWebhookLogs` and no relevant replay record. No attempt has a webhook event ID or provider transaction ID.

The canonical webhook chain remains:

- `POST /api/webhooks/paymob`: `PointOfSale.API/Controllers/PaymobWebhooksController.cs:33-79`
- tenant resolution: `PointOfSale.API/Services/Payments/PaymentWebhookRequestPipeline.cs:66-165`
- HMAC and payload admission: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:996-1070`
- lifecycle failure transition: `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:1780-1835`
- payment lifecycle: `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs:2291-2300`

No code was added to manufacture failure from the browser return.

## 5. Browser Return Evidence

The configured browser callback is `GET /api/webhooks/paymob/callback` (`PointOfSale.API/Controllers/PaymobWebhooksController.cs:206-284`). It validates provider routing evidence and redirects to `/checkout/pending?orderId=...` (`PaymobWebhooksController.cs:278-284`). The logged `success` value does not mutate financial state or choose a success/failure page.

The Storefront pending page immediately fetches canonical backend status (`PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx:80-133`). Browser return is therefore navigation evidence only. This remains correct for return-before-webhook, no-webhook, closed-browser, and network-loss cases.

## 6. Retry Authority Audit

The exact existing P8.1 authority is `PaymobPaymentService.EvaluatePaymobRetryAuthorityAsync` (`PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:618-653`). It requires:

- order Pending;
- order payment Failed;
- latest attempt is Paymob;
- latest attempt is Failed, Cancelled, Rejected, or Expired (`PaymobPaymentService.cs:701-705`);
- no other Pending, Authorized, or AwaitingVerification Paymob attempt.

On acceptance, `PrepareOrderForPaymobRetryAsync` rebuilds/validates reservation authority and changes payment authority back to Pending (`PaymobPaymentService.cs:655-699`). New attempt creation follows at `PaymobPaymentService.cs:460-490`.

The existing route is `POST /api/storefront/payments/paymob/intent/{orderId}` (`PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs:181-235`). No new retry authority or endpoint was created.

## 7. Storefront Retry UX Audit

Before P9, `CheckoutPendingPage` stopped polling on canonical `FAILED` but offered only Return to Checkout, Refresh Status, and Track Order. `paymentStatusApi` only exposed GET status. The backend status DTO had no retry decision.

After P9:

- canonical DTO includes `CanRetryPayment`: `PointOfSale.API/DTOs/Payments/PaymentDtos.cs:330-341`;
- the canonical controller asks Paymob's existing status path for the shared retry decision: `PointOfSale.API/Controllers/PaymentsController.cs:332-350`;
- Storefront rejects malformed or contradictory retry authority: `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts:58-112`;
- CTA is gated only by backend `canRetryPayment`: `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx:413-424`;
- CTA calls the existing route: `PointOfSale.Storefront/src/services/api/paymentStatusApi.ts:38-64`.

## 8. Root Cause

Two distinct links were missing:

1. Real incident: no authoritative Paymob failure reached SHOPIXY. The local development URLs are not externally reachable, so the order correctly remained pending.
2. Application integration: when a valid failure webhook does produce canonical Failed, the P8.1 retry decision was not projected into canonical storefront status and no Storefront component invoked the existing retry endpoint.

The first explains the observed pending state. The second explains why a legitimately canonical failed state had no direct retry UX.

## 9. Backend Remediation

`PaymobPaymentService.GetPaymentStatusAsync` now invokes the same `EvaluatePaymobRetryAuthorityAsync` method used by attempt creation and returns `CanRetryPayment` (`PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:930-993`). `PaymentsController.GetCanonicalPaymentStatus` copies that server decision into the canonical response only after existing order-access authorization (`PointOfSale.API/Controllers/PaymentsController.cs:313-352`).

No lifecycle transition, HMAC rule, correlation rule, terminal-state rule, provider finality rule, or schema was changed.

## 10. Storefront Remediation

`CheckoutPendingPage` now:

- refetches canonical status as before;
- displays `Pay Again` only for backend-authorized retry;
- has a synchronous in-flight guard against repeated clicks;
- posts only the canonical order route;
- validates order, amount, currency, new local attempt ID presence, provider intention presence, future expiry, and exact Paymob Unified Checkout origin/path;
- redirects directly to the fresh handoff;
- does not persist or resurrect the old handoff.

Sources: `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx:232-255`, `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts:40-80`.

## 11. Retry Authorization

The endpoint first resolves tenant context and calls `PaymentRequestAuthorizationService.AuthorizeOrderAccessAsync` (`StorefrontPaymobPaymentsController.cs:200-224`). Guest access requires matching session plus a tenant/order-bound checkout token or HttpOnly cookie (`PointOfSale.API/Services/Payments/PaymentRequestAuthorizationService.cs:120-194`). Customer access requires the authenticated customer to own the order (`PaymentRequestAuthorizationService.cs:199-216`).

The service revalidates tenant/customer/order ownership under the order lock (`PaymobPaymentService.cs:128-157`). Amount, currency, payment method, provider, and attempt identity are derived server-side. The browser submits no PaymentIntent ID, provider reference, client secret, amount, or currency.

## 12. Retry Idempotency

The retry route forwards the existing optional `Idempotency-Key` (`StorefrontPaymobPaymentsController.cs:226-233`). If absent, the existing deterministic tenant/order key is used (`PaymobPaymentService.cs:148-150`, `2367-2384`). Existing complete active sessions are replayed, not recreated (`PaymobPaymentService.cs:271-455`).

Database protection independently enforces one active attempt per tenant/order/provider with `IX_PaymentIntents_Active_OrderId_Provider_Unique` (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:754-767`). The UI also rejects a second local click while one request is in flight, but backend idempotency remains authoritative.

## 13. Failed -> Retry -> Success

Existing deterministic test `FailureThenRetryThenSuccess_CreatesNewAttemptAndFinalizesSameOrder` is at `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:187-311`. P9 added assertions that retry authority is true after failure and false once the replacement is active (`PaymobSuccessPathSimulationTests.cs:220-262`).

Current execution is blocked by the unrelated test-project compile error. The latest previously recorded run passed this suite before that compile regression.

## 14. Failed -> Retry -> Failed

P9 added `FailureThenRetryThenFailureThenRetryThenSuccess_PreservesOneOrderAndAttemptHistory` (`PaymobSuccessPathSimulationTests.cs:313-381`). Its middle checkpoint proves attempt 2 Failed and retry authority true. Current execution is blocked before test discovery by the unrelated compile error.

## 15. Multiple Retries

The same P9 test asserts one order, three attempts, status history `[Failed, Failed, Paid]`, three distinct provider intention references, one inventory commit, and final order confirmation. It is implemented but not executable until the pre-existing test compile error is repaired.

## 16. Concurrent Retry

Runtime controls are unchanged and layered:

- order transaction/lock: `PaymobPaymentService.cs:128-137`;
- deterministic idempotency: `PaymobPaymentService.cs:148-150`;
- active-attempt query: `PaymobPaymentService.cs:638-648`;
- unique active-attempt index: `ApplicationDbContext.cs:754-767`;
- unique-violation recovery: `PaymobPaymentService.cs:495-546`.

No fresh P9 concurrent backend test result can be certified because the test project does not compile.

## 17. Stale Attempt Protection

`PaymentLifecycleService.TryRejectSupersededTerminalAttemptAsync` is called for authorization, success, and failure paths (`PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs:1734-1743`, `1842-1851`, `2291-2300`). The implementation at lines `3379-3445` rejects outcomes against terminal-negative attempts when a newer sibling exists. Late success creates manual-review/suspense evidence rather than finalizing the order; late failure/cancellation is idempotently ignored.

Behavioral coverage exists at `PaymobSuccessPathSimulationTests.cs:839-886` and the expired-attempt tests. Current rerun is blocked by test compilation.

## 18. Session Expiry

Recent expiry behavior is preserved. Complete expired handoffs or provider-empty seeds can roll over; ambiguous checkpoints fail closed. Old protected handoff data is scrubbed before replacement (`PaymobPaymentService.cs:271-345`). The current database demonstrates historical attempt 1 Expired and replacement attempt 2 Pending.

The prior session-expiry audit recorded 28 Paymob simulation tests, 208 Paymob-filtered tests, 60 PaymentLifecycle tests, and 27 inventory reservation tests passing (`SHOPIXY_PAYMOB_REGRESSION_SESSION_EXPIRY_FORENSIC_AUDIT.md:434-437`). These are prior evidence, not current P9 execution.

## 19. Browser/Webhook Race

| Race | Authority result |
|---|---|
| Failure webhook before return | Canonical Failed; retry may be exposed only if P8.1 authority accepts |
| Return before failure webhook | Pending until webhook; polling later converges to Failed |
| Return and no webhook | Remains Pending; no failure CTA |
| Failure after retry starts | New active attempt makes old terminal outcome stale/idempotent |
| Old failure after attempt 2 | Superseded attempt guard prevents regression |

The no-webhook case is the only one observed in the real database. Other rows are source/test-backed but not freshly executed due the test compile blocker.

## 20. Inventory Safety

Retry calls `IPaymobRetryInventoryAuthority.EnsureRetryReservationAuthorityAsync` and `CheckoutInventoryAuthorityGuard.EnsureOrderReservationsActiveAsync` before creating the replacement (`PaymobPaymentService.cs:655-669`). The authority verifies exact unconsumed reservation quantity and rebuilds stale/partial sets (`PointOfSale.API/Services/Payments/Paymob/PaymobRetryInventoryAuthority.cs:22-324`). Payment lifecycle remains the sole final inventory commit boundary.

No inventory logic changed in P9.

## 21. Admin Observability

POS Admin list/detail mappings continue to expose backend order payment state, including `AdminOrderService` payment projection (`PointOfSale.API/Services/Admin/AdminOrderService.cs:100-145`, `1214`). No secret or reusable handoff was added to admin DTOs. Admin build passes. Fresh backend admin test execution is blocked with the test project.

## 22. Storefront Observability

Storefront now distinguishes:

- Pending/Processing: waiting/reconciliation UI and no retry;
- Manual Review: explicit no-retry guidance;
- Failed with `canRetryPayment=false`: failed status but no unsafe CTA;
- Failed with `canRetryPayment=true`: Pay Again;
- Paid: confirmation navigation.

Canonical response validation rejects `canRetryPayment=true` unless state is Failed and method is Paymob (`PointOfSale.Storefront/src/features/checkout/paymentFlow.ts:104-112`).

## 23. Regression Results

PRE-EXISTING evidence:

- prior P8 simulation: 28 passed, 0 failed;
- prior Paymob filter: 208 passed, 0 failed;
- prior PaymentLifecycle filter: 60 passed, 0 failed;
- prior inventory filter: 27 passed, 0 failed.

P9 current results:

- full Storefront: 34 files, 344 tests passed, 0 failed;
- focused P9 Storefront: 3 files, 37 tests passed, 0 failed;
- API build: passed, 0 warnings, 0 errors;
- Storefront build: passed with existing bundle/Browserslist warnings;
- Admin build: passed;
- EF model check: clean;
- backend tests: 0 executed because test compilation failed first.

NEW REGRESSIONS:

- none detected in executable API/frontend checks.

PRE-EXISTING BLOCKER:

- `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs:798` references nonexistent `AccountingOutboxMessage.Payload`; solution/test build reports CS1061. P9 did not modify that assertion.

## 24. Test Results

| Scenario | Expected | Actual | PASS/FAIL |
|---|---|---|---|
| 1. Paymob success | Signed captured webhook finalizes once | Prior P8 pass; current rerun blocked | BLOCKED |
| 2. Paymob failure webhook | Canonical Failed, retry authority evaluated | Source and prior P8 pass; no real webhook | BLOCKED |
| 3. Paymob browser return | Navigate/poll only | Observed Pending; no mutation | PASS |
| 4. Failure before browser return | Return reads Failed | Implemented; current backend rerun blocked | BLOCKED |
| 5. Browser return before failure | Pending then converges | Implemented; current backend rerun blocked | BLOCKED |
| 6. No webhook | Remain Pending; no retry | Real runtime observed | PASS |
| 7. Failed -> Retry | New attempt, same order | Existing test plus P9 authority assertions; rerun blocked | BLOCKED |
| 8. Failed -> Retry -> Success | Attempt 2 Paid, attempt 1 historical | Existing test; rerun blocked | BLOCKED |
| 9. Failed -> Retry -> Failed | Retry remains available | P9 test added; rerun blocked | BLOCKED |
| 10. Multiple retries | Three attempts, one order, only third finalizes | P9 test added; rerun blocked | BLOCKED |
| 11. Concurrent retry | Exactly one active attempt/provider init | Layered lock/index/idempotency; fresh test blocked | BLOCKED |
| 12. Duplicate retry | Replay active handoff | Existing service behavior; fresh test blocked | BLOCKED |
| 13. Stale attempt success | Cannot finalize newer lineage | Existing test/source; rerun blocked | BLOCKED |
| 14. Stale attempt failure | Cannot regress | Existing test/source; rerun blocked | BLOCKED |
| 15. Expired retry attempt | Safe rollover or fail closed | Prior session-expiry suite passed; current rerun blocked | BLOCKED |
| 16. Wrong customer retry | Denied | Existing authorization source/tests; current rerun blocked | BLOCKED |
| 17. Wrong tenant retry | Denied | Existing tenant resolution/source/tests; current rerun blocked | BLOCKED |
| 18. Terminal order retry | Denied | Existing P8.1 predicate | PASS (source) |
| 19. Inventory safety | One final commit | Existing authority and prior suite; current rerun blocked | BLOCKED |
| 20. Admin visibility | Canonical backend state visible | Admin build passed; fresh test blocked | BLOCKED |
| 21. Storefront visibility | Pending vs Failed/retry vs Paid | 344 Storefront tests pass; production build passes | PASS |

## 25. Build Results

| Command | Result |
|---|---|
| `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore` | PASS, 0 warnings, 0 errors |
| `dotnet build PointOfSale.sln --no-restore` | FAIL, 214 warnings, 1 pre-existing CS1061 test error |
| Storefront `npm run build` | PASS |
| Admin `npm run build` | PASS |
| `git diff --check` | PASS; line-ending warnings only |

No output-locking process was encountered.

## 26. EF Migration Status

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build` returned:

> No changes have been made to the model since the last migration.

**No EF model changes required.**

## 27. Remaining Risks

- The real Paymob Sandbox decline/failure webhook has not been delivered to this environment.
- A pending attempt with no webhook has no authoritative terminal state and must remain non-retryable.
- Current backend acceptance tests cannot execute until the unrelated `AccountingOutboxMessage.Payload` compile error is resolved by its owner.
- The database's latest replacement handoff is elapsed but still persisted Pending; existing expiry authority handles replacement only when the customer re-enters the canonical creation path.
- Admin attempt-history presentation was source-audited but not live-browser verified in P9.

## 28. External Paymob Sandbox Limitation

Development Paymob URLs are `http://localhost:5010/...` (`PointOfSale.API/appsettings.Development.json:23-24`). Paymob's external Sandbox cannot deliver a server-to-server webhook to that address. A public HTTPS tunnel/deployment and matching tenant Paymob configuration are required to execute the exact decline, callback, webhook, retry, and final success sequence. P9 intentionally does not replace missing provider finality with browser query parameters.

## 29. Final Certification

**P9 Certification: BLOCKED**

Application remediation is implemented and frontend/API checks are green, but certification cannot be promoted to PASS or PASS WITH CONDITIONS because both of these required proofs are unavailable in the current worktree/environment:

1. a real signed Paymob decline webhook through a publicly reachable endpoint;
2. a successful current backend P8/P8.1/P8.2/P9 rerun after the pre-existing test compile error is repaired.

The architectural result is nevertheless fail-closed and correct:

```text
browser return without webhook
  -> canonical Pending
  -> no Pay Again

signed failed attempt
  -> canonical Failed
  -> existing backend retry authority
  -> authorized Pay Again
  -> existing retry endpoint
  -> new PaymentIntent and provider intention
  -> fresh Unified Checkout handoff
  -> same Order
```

Late outcomes for an older terminal attempt remain unable to override the newer attempt.
