# SHOPIXY Paymob Regression Session Expiry Forensic Audit

Date: 2026-08-15

Status: Remediated and focused suites certified

## 1. Executive Summary

The resumed object and the expired object were not the same kind of session.

- Checkout session `16faf14b-f063-4027-96d4-685e067acacc` was successfully resumed by `CheckoutSessionService`. A checkout session has no absolute `ExpiresAtUtc` field. Resume updates activity and can reactivate an abandoned row.
- Final checkout entered durable checkout replay for an existing order and loaded that order's latest Paymob `PaymentIntent`.
- The latest Paymob intent was `Pending` with non-null `ExpiresAtUtc <= DateTime.UtcNow`.
- The P7 fail-closed guard threw `PAYMOB_PAYMENT_SESSION_EXPIRED` before Paymob runtime execution.
- Checkout replay simultaneously classified that same expired pending intent as eligible for handoff regeneration. Regeneration called the Paymob service, which rejected it. This was a deterministic error latch.

The first causal change was the uncommitted P7 guard formerly at `PaymobPaymentService.cs:247-262`. It exists only in the dirty working tree; it is not in `HEAD`/`origin/main` commit `f5d796c`.

The remediation does not renew or reuse an expired provider session. It atomically terminalizes the old attempt, scrubs the protected handoff, preserves provider correlation, establishes inventory authority, creates one fresh attempt, and keeps ambiguous partial provider checkpoints fail-closed. Superseded captured-money evidence creates an idempotent accounting-suspense outbox entry and a durable manual-review webhook marker, and legacy order lines without a warehouse snapshot use their unambiguous reservation lineage rather than selecting new stock arbitrarily.

Confidence:

| Rank | Finding | Status |
|---|---|---|
| P0 | P7 expired-pending guard conflicted with checkout replay regeneration and latched every retry | Proven |
| P0 | Failing request stopped before Paymob HTTP | Proven by unique throw location and call ordering |
| P1 | Failing request was a durable existing-order/idempotency replay using an old pending intent | Strong source and error-state evidence; exact database row was not available |
| P2 | Concrete `CartId`, `OrderId`, `PaymentIntentId`, and `ExpiresAtUtc` of the historical request | Not recoverable from workspace artifacts |

## 2. Exact Failure

Pre-remediation first failure:

```csharp
if (existingIntent.ExpiresAtUtc.HasValue &&
    existingIntent.ExpiresAtUtc.Value <= DateTime.UtcNow)
{
    await transaction.RollbackAsync(cancellationToken);
    throw new PaymentException("PAYMOB_PAYMENT_SESSION_EXPIRED", ...);
}
```

The sole producer was `PaymobPaymentService.CreatePaymentIntentAsync`, in the P7 working-tree hunk formerly at `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:245-262`.

Required state at the throw:

- latest tenant/order Paymob intent selected by descending `CreatedAt`;
- `Order.Status == Pending`;
- `Order.PaymentStatus == Pending`;
- intent `Status == Pending`;
- `ExpiresAtUtc != null`;
- `ExpiresAtUtc <= DateTime.UtcNow`;
- inventory authority had already passed.

`CheckoutService` caught and rethrew the `PaymentException`. `CheckoutController` mapped it to HTTP 503 with:

```json
{
  "code": "PAYMOB_PAYMENT_SESSION_EXPIRED",
  "message": "The selected payment provider is temporarily unavailable."
}
```

The detailed internal message was intentionally replaced with a generic storefront message.

## 3. Request Correlation

Observed runtime sequence supplied with the incident:

| Request | Result |
|---|---|
| `/api/storefront/checkout/pricing` | 200 |
| `/api/storefront/checkout-session` | 200 |
| checkout session resume | `16faf14b-f063-4027-96d4-685e067acacc` |
| cart retrieval | success |
| shipping preview | 200 |
| shipping selection | 200 |
| final pricing | 200 |
| `POST /api/storefront/checkout?lang=en` | 503 `PAYMOB_PAYMENT_SESSION_EXPIRED` |

The frontend final request includes:

- fresh `cartId`;
- current in-memory `checkoutSessionId`;
- warehouse resolution ID/version;
- shipping and payment selections;
- authoritative expected-pricing snapshot;
- guest identity in `X-Session-Id`, or authenticated identity.

It does not include:

- `paymentSessionId`;
- `PaymentIntentId`;
- `OrderId`;
- Paymob `client_secret`;
- `Idempotency-Key`;
- a browser-generated expiry timestamp.

The backend derives durable checkout idempotency from tenant, cart, canonical owner, cart snapshot, checkout-session ID, warehouse authority, payment method, shipping method, and request content. Therefore the old order/payment attempt is recovered server-side; it is not selected by a stale frontend `PaymentIntentId`.

Historical runtime values not present in the workspace:

- exact `CartId`;
- exact checkout idempotency record/key;
- exact `OrderId`;
- exact `PaymentIntentId`;
- exact persisted `ExpiresAtUtc`;
- exact old provider intention ID/client secret.

The exception itself proves the persisted expiry was non-null and not later than the API process UTC clock at evaluation time, assuming the running binary matched this source tree.

## 4. Session Lineage

Proven lineage shape:

```text
Browser
  -> current cart
  -> CheckoutSession 16faf14b-f063-4027-96d4-685e067acacc
  -> final checkout request.checkoutSessionId
  -> backend durable checkout idempotency / active-order replay
  -> existing Order
  -> latest Paymob PaymentIntent
  -> expired pending local Paymob attempt
  -> no new provider handoff before failure
```

The current frontend assigns the `/checkout-session` response `sessionId` directly to `checkoutSessionId`, uses it for shipping authority, and sends it in the final checkout body. Backend warehouse/session validation rejects a mismatched or stale checkout session rather than silently substituting another one.

The break was after checkout-session validation: the same resumed checkout session led to an already-created order whose independent payment attempt had elapsed.

No evidence shows stale localStorage payment identity:

- checkout-session ID is not persisted in localStorage;
- payment intent/order identity is not persisted there;
- only shipping method, payment method, and step are persisted;
- guest `X-Session-Id` is persisted and has no browser TTL, but it is ownership identity rather than payment-session authority.

## 5. Expiry Decision Source

Pre-remediation path:

```text
CheckoutController.Checkout
  -> CheckoutService.ProcessCheckoutAsync
  -> durable replay / existing active order
  -> TryRegenerateReplayOnlineProviderHandoffAsync
  -> CreateCheckoutPaymentSessionAsync
  -> PaymentGateway.CreateSessionAsync
  -> PaymobPaymentGateway.CreateSessionAsync
  -> PaymobPaymentService.CreatePaymentIntentAsync
  -> latest PaymentIntent.Status == Pending
  -> PaymentIntent.ExpiresAtUtc <= DateTime.UtcNow
  -> PaymentException(PAYMOB_PAYMENT_SESSION_EXPIRED)
  -> CheckoutController HTTP 503 mapping
```

Important current source locations after remediation:

- expiry classification: `PaymobPaymentService.cs:217-221`;
- safe/ambiguous state classification: `PaymobPaymentService.cs:265-310`;
- old attempt terminalization and secret scrubbing: `PaymobPaymentService.cs:313-327`;
- replacement audit log: `PaymobPaymentService.cs:329-336`;
- replay regeneration eligibility: `CheckoutService.cs:4008-4023`;
- controller 503 mapping: `CheckoutController.cs:377-386`;
- direct Paymob endpoint consistent mapping: `StorefrontPaymobPaymentsController.cs` `catch (PaymentException)`.

The field was `PaymentIntent.ExpiresAtUtc`, not a checkout-session expiry and not a provider-returned absolute expiry.

## 6. Time/Timezone Analysis

| Surface | Type/source | Result |
|---|---|---|
| `CheckoutSession.CreatedAtUtc` | application `DateTime.UtcNow`, PostgreSQL `timestamptz` | UTC |
| `CheckoutSession.LastActivityAtUtc` | application `DateTime.UtcNow`, PostgreSQL `timestamptz` | UTC; refreshed on resume |
| `CheckoutSession.ExpiresAtUtc` | no field exists | not the failure source |
| recovery/payment/upload token expiry | `DateTimeOffset.UtcNow`, default 20 minutes | separate signed-token authority |
| `PaymentIntent.CreatedAt` | application `DateTime.UtcNow`, PostgreSQL `timestamptz` | UTC; SaveChanges sets insert time |
| `PaymentIntent.ExpiresAtUtc` | application `DateTime.UtcNow + 60 minutes`, PostgreSQL `timestamptz` | exact failure authority |
| Paymob Intention expiration | integer `3600` seconds | relative provider request value |
| Paymob `client_secret` expiry | no provider absolute timestamp stored | local intent expiry is used |
| Redis cart TTL | 24 hours | not involved in this error |
| Redis checkout execution locks | 2 minutes with heartbeat | not involved |
| browser handoff check | `Date.parse(expiresAtUtc) > Date.now()` | admission/UI guard only |
| database `NOW()` | not used on this path | no DB/app clock mixing at decision |

No proven UTC/local, seconds/milliseconds, or double-conversion defect exists on the throwing path. Current writes are UTC and relevant columns are PostgreSQL `timestamp with time zone`.

Residual operational clock risk exists because expiry creation/checking use process wall clocks rather than an injected clock. It was not the regression: the deterministic replay/guard conflict reproduces with correct clocks.

## 7. Resume Semantics

Answers to the requested resume questions:

| Question | Answer |
|---|---|
| A. Does resume extend checkout `ExpiresAt`? | No such checkout-session field exists. |
| B. Does resume return the old row? | Yes, for the same tenant/cart/owner, including an abandoned row. |
| C. Does resume update one expiry while checkout validates another? | Resume updates `LastActivityAtUtc`; final payment replay validates `PaymentIntent.ExpiresAtUtc`. |
| D. Can API say resumed while payment is expired? | Yes. They are independent authorities. |
| E. Does final checkout load the same checkout-session ID? | Frontend sends it verbatim and backend validates it. |
| F. Does final checkout load another payment session? | It loads the existing order's latest Paymob intent through replay. |
| G. Did retry logic reuse an old attempt? | Pre-fix replay attempted regeneration, but Paymob rejected the old expired pending row before creating a replacement. |
| H. Does P8.1 interact with checkout-session expiry? | No. P8.1 handled terminal failed attempts; the P7 row remained `Pending`, so it was outside retry authority. |

## 8. Payment Attempt Analysis

Three concepts are distinct:

1. Checkout session: cart/owner/form activity authority, no absolute expiry column.
2. Local Paymob payment attempt: SQL `PaymentIntent`, with local `ExpiresAtUtc` and protected provider artifacts.
3. Provider handoff: Paymob Intention plus `client_secret`/Unified Checkout URL, requested for 3600 seconds.

`PAYMOB_PAYMENT_SESSION_EXPIRED` was thrown for concept 2. The name was broadly defensible but the public 503 message was misleading because provider availability had not been tested on that invocation.

Pre-fix defect:

- old row remained `Pending` after rejection;
- expiry remained elapsed;
- provider artifacts remained attached;
- replay repeatedly selected the same latest row;
- no state transition made it eligible for P8.1 terminal retry;
- every retry reached the same exception.

Remediated behavior:

- complete expired handoff or proven provider-empty seed is eligible for replacement;
- old row becomes `Expired`;
- old protected handoff and redirect are scrubbed;
- provider ID/reference remains for audit and late-webhook correlation;
- one fresh pending row is inserted after the old row leaves the filtered active index;
- ambiguous checkpoint/provider state remains rejected with `PAYMOB_PAYMENT_SESSION_EXPIRED` and no provider I/O.

## 9. Idempotency Analysis

Frontend does not send an explicit final-checkout idempotency key. Backend checkout idempotency is deterministic and includes checkout-session and warehouse authority.

Payment attempt idempotency findings:

- existing complete active session is returned idempotently;
- an incomplete checkpoint cannot be taken over by a different payment idempotency key;
- pre-fix expired pending state was neither replayable nor transitionable;
- repeated checkout replay therefore latched on the same row.

Remediation:

- old and new local attempts have separate IDs and payment references;
- Paymob `merchant_order_id`/special reference is now the local `PaymentIntent.Id`, not the shared `Order.Id`;
- provider creation identity is stable for one attempt and distinct across retries;
- tenant/webhook fallback resolves local payment-intent ID first and supports legacy order-ID references only when unambiguous;
- latest-intent queries add an `Id` tie-break after `CreatedAt`.

No retry loop was added.

## 10. Retry Interaction

P8.1 accepted retries only when:

- order is pending;
- order payment status is failed;
- latest Paymob intent is terminal (`Failed`, `Cancelled`, `Rejected`, or `Expired`);
- no other active Paymob attempt exists;
- inventory can be authoritatively reserved.

The regressed row was still `Pending` and the order was still payment-pending. Therefore P8.1 could not admit it.

The new rollover performs the missing transition inside the existing order-lock transaction. It does not falsely mark the order failed and does not route through lifecycle expiration, which would cancel the order and release inventory.

Related finality hardening now rejects every outcome from a superseded terminal attempt when a newer authoritative attempt exists:

- late success: manual-review evidence, no current-order mutation;
- late authorization: collapsed, no current-order mutation;
- late failure/void: collapsed, no current-order mutation or reservation release.

Source: `PaymentLifecycleService.cs:1734`, `:1923`, `:2291`, and `:3379-3437`.

## 11. P7/P8/P8.1/P8.2 Diff Analysis

Repository state:

- `HEAD == origin/main == f5d796c`;
- no P7/P8/P8.1/P8.2 commit exists in inspected history;
- relevant phase reports and principal P8 tests are untracked;
- relevant implementation is unstaged working-tree content;
- `git blame` reports `00000000 (Not Committed Yet)` for the regression/remediation lines.

Phase attribution:

| Phase | Relevant behavior |
|---|---|
| P7 | Added fail-closed expired stored-session rejection and the error literal. This was the first causal change. |
| P8 | Proved fresh application success lifecycle and Unified Checkout handoff; did not exercise an expired pending replay. |
| P8.1 | Added terminal failed-attempt retry lineage; did not classify elapsed `Pending` attempts. |
| P8.2 | Hardened finality and stale success behavior; source assertion claimed stale-session protection but no behavioral expired-pending test existed. |

The former P7 branch was conclusively on the actual execution path because it was the repository's only producer of the runtime error code. The 503 controller preserved that exact `ErrorCode`.

## 12. Provider HTTP Evidence

PAYMOB HTTP CALLED FOR THE FAILING INVOCATION: **NO**

Ordering proof:

```text
expired guard / rollback / throw
  before CreatePaymobProviderIntentAsync
  before PaymobRuntimeApplicationService.CreatePaymentIntentAsync
  before runtime bridge
  before PaymobPaymentAdapter
  before HttpClient.SendAsync(POST ../v1/intention/)
```

The configured provider operation resolves to:

```text
POST https://accept.paymob.com/v1/intention/
```

An earlier request may have created the old stored intention. That does not change the conclusion for the failing 503 invocation.

## 13. Root Cause

**P0 proven root cause:** P7 added an unconditional fail-closed rejection for any elapsed `Pending` Paymob intent, while checkout replay later explicitly classified the same elapsed `Pending` intent as recoverable and called Paymob session creation to regenerate its handoff. The Paymob service rolled back and threw without terminalizing the old attempt, so the same row remained latest, active, and expired forever.

This was not caused by:

- Paymob credentials;
- Integration ID `3153058`;
- checkout-session resume failure;
- browser-provided payment identity;
- a proven timezone/unit conversion defect;
- provider HTTP availability.

## 14. Regression Commit/Change

FIRST REGRESSION: **uncommitted P7 working-tree change**, formerly `PaymobPaymentService.CreatePaymentIntentAsync` lines `247-262`, adding:

```csharp
throw new PaymentException("PAYMOB_PAYMENT_SESSION_EXPIRED", ...);
```

There is no commit hash for this change. It is absent from `HEAD` `f5d796c`; `git blame` identifies it as `Not Committed Yet`.

The P8/P8.1 replay/retry additions exposed the P7 logic conflict but were not the first producer of the failure code.

## 15. Why Previous Working Flow Worked

Fresh checkout behavior:

- checkout created or seeded a new pending intent;
- a seed initially had no expiry/provider artifact;
- Paymob service assigned a future local expiry;
- provider runtime created an Intention;
- returned `client_secret` and Unified Checkout URL were protected and persisted;
- the browser received a future handoff and opened Unified Checkout.

Before P7, the new error code and unconditional expired-pending throw did not exist. Fresh P8 simulations also always used future expirations, so they did not cover the regression state.

## 16. Why Current Flow Failed

The browser resumed an old checkout-session row for the same cart/owner. Durable checkout idempotency then found an existing order. That order's latest Paymob attempt had elapsed while still `Pending`.

Checkout replay cleared stale response fields and requested regeneration. Paymob creation loaded the same latest row and rejected it before provider execution. Because rejection rolled back without changing intent state, every retry selected the same row and failed identically.

## 17. Security Impact

Pre-fix impact:

- availability: valid customers were permanently blocked before provider handoff;
- observability: provider-unavailable messaging blamed a provider that was not called;
- stale secret retention: expired protected handoff remained stored on the active row;
- lineage: elapsed attempt never became terminal;
- latent finality issue: after safe rollover, late non-success outcomes from an old terminal attempt could have mutated the current order without generalized supersession checks.

Remediation security properties:

- expiry validation remains enforced;
- expired secret/URL is never returned or renewed;
- ambiguous provider checkpoints remain fail-closed;
- old provider identity remains correlatable;
- one active-attempt filtered unique index remains enforced;
- provider calls occur only after local transaction commit;
- late old outcomes cannot mutate the newer attempt/order/inventory authority;
- late captured money from an old attempt leaves a durable manual-review marker and unresolved-payment accounting outbox entry;
- retry inventory stays bound to an explicit item/order warehouse or one unambiguous historical reservation warehouse;
- active retry reservations must exactly match authoritative order lines and quantities; surplus rows are released before rebuilding;
- no credential, Integration ID, schema, or migration change was made.

## 18. Recommended Minimal Fix

Implemented recommendation:

1. Under the existing order row lock, classify elapsed pending state.
2. Permit automatic replacement only for a complete verifiable handoff or a seed with no durable provider state.
3. Keep partial/ambiguous provider checkpoints fail-closed.
4. Establish/extend inventory authority for the full Paymob session lifetime.
5. Mark old attempt `Expired`, scrub secret/redirect, and save it first.
6. Insert one fresh pending attempt in the same transaction.
7. Use attempt-scoped provider merchant identity.
8. Reject late outcomes from superseded attempts.
9. Recheck handoff expiry immediately before browser redirect.

No database migration is required.

## 19. Tests Required

Implemented and passing:

- expired complete session creates one replacement attempt;
- stale key/URL is not reused;
- old attempt is terminal and secret scrubbed;
- provider ID is preserved for correlation;
- replacement has fresh ID/reference/expiry/handoff;
- replay returns replacement and creates no third attempt;
- inventory reservation covers the replacement session lifetime;
- partial reservations are replaced without over-reservation;
- legacy null-warehouse order lines retain immutable snapshots and recover through unambiguous reservation warehouse lineage;
- ambiguous checkpoint remains fail-closed;
- ambiguous checkpoint makes zero provider calls;
- late old success cannot finalize;
- a second captured success after the replacement is paid creates accounting suspense evidence and remains durably locked for manual review;
- late old authorization cannot authorize;
- late old failure cannot fail the order or release current inventory;
- P8 failure/retry/success lineage remains green;
- Paymob focused suite remains green;
- payment lifecycle suite remains green;
- frontend payment-session authority remains green;
- frontend production build remains green.

Executed results:

| Command/suite | Result |
|---|---|
| `PaymobSuccessPathSimulationTests` | 28 passed, 0 failed |
| all tests matching `Paymob` | 208 passed, 0 failed |
| all tests matching `PaymentLifecycle` | 60 passed, 0 failed |
| all tests matching `InventoryReservation` | 27 passed, 0 failed |
| frontend payment authority/flow/checkout validation | 38 passed, 0 failed |
| `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore` | PASS, 0 warnings, 0 errors on final incremental run |
| `npm run build` in Storefront | PASS, non-fatal existing bundle/Browserslist warnings |
| `git diff --check` | PASS; line-ending notices only |

Recommended production verification after deployment:

- correlate one real cart through checkout session, order, old intent, replacement intent, provider intention, and Integration ID `3153058`;
- verify exactly one `POST /v1/intention/` occurs for replacement;
- verify the returned Unified Checkout URL opens;
- verify a repeated final checkout replays the replacement without a second provider call;
- verify old attempt secrets are absent and old provider ID remains queryable.

## 20. Certification

FIRST REAL FAILURE:

`PaymobPaymentService.CreatePaymentIntentAsync` rejected the latest elapsed `Pending` local Paymob `PaymentIntent` and threw `PAYMOB_PAYMENT_SESSION_EXPIRED` before provider runtime execution.

PROVEN ROOT CAUSE:

The P7 expired-pending guard left the stale attempt active and unchanged while checkout replay classified it as regenerable, creating a deterministic replay/expiry error latch.

FIRST REGRESSION:

Uncommitted P7 working-tree hunk formerly at `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs:247-262`; no commit hash exists; absent from `HEAD` `f5d796c`.

PAYMOB HTTP CALLED:

**NO** for the failing invocation.

SAFE MINIMAL FIX:

Atomically terminalize and scrub a verifiably expired completed/empty Paymob attempt, preserve its correlation evidence, establish inventory authority, and create exactly one fresh attempt; keep ambiguous checkpoints fail-closed.

FILES THAT MUST CHANGE:

- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/IPaymobRetryInventoryAuthority.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobRetryInventoryAuthority.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobWebhookRuntimeIntegrationService.cs`
- `PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs`
- `PointOfSale.API/Services/Payments/CheckoutInventoryAuthorityGuard.cs`
- `PointOfSale.API/Services/Payments/StorefrontSuspenseRecoveryPolicy.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs`
- `PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Tests/Payments/PaymobSuccessPathSimulationTests.cs`
- `PointOfSale.Tests/Architecture/PaymobPaymentAuthorityCertificationTests.cs`

FILES THAT MUST NOT CHANGE:

- Paymob credential values or secret stores
- Integration ID `3153058`
- `PointOfSale.Core/Entities/CheckoutSession.cs`
- `PointOfSale.Core/Entities/PaymentIntent.cs`
- database schema/model snapshot
- any migration
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts` expiry policy

TESTS REQUIRED:

- expired complete attempt replacement and idempotent replay;
- ambiguous checkpoint fail-closed behavior;
- inventory lifetime authority;
- warehouse-scoped reservation authority, including immutable legacy null-warehouse lines;
- late superseded success/authorization/failure finality, durable second-charge manual review, and accounting-suspense evidence;
- provider attempt identity and legacy fallback;
- Paymob focused, lifecycle, frontend authority, and production builds;
- one post-deployment real Unified Checkout handoff with request correlation.

CERTIFICATION:

**PASS** for source root cause, remediation behavior, security invariants, focused automated tests, and builds.

Runtime data limitation: the historical failing row's concrete `CartId`, `OrderId`, `PaymentIntentId`, and timestamp were not present in the workspace, so those values are not fabricated in this report.
