# SHOPIXY Shipping Methods Step 2I Forensic Audit

Date: 2026-08-11

Scope: Storefront shipping-preview request cancellation and lifecycle audit.

Mode: Audit-only. No source, test, configuration, schema, migration, frontend behavior, backend behavior, or git-state changes were made by this audit. This report is the only intended artifact.

Certification: BLOCKED

## 1. Executive Summary

The observed browser console entries with `message: "canceled"` for `POST /api/storefront/shipping-methods/preview` are not evidence of a backend shipping-method failure.

Source evidence proves that the Storefront deliberately aborts any in-flight shipping preview whenever a newer `fetchShippingMethods()` call starts. The final/latest request can therefore succeed and populate the UI while older duplicate preview requests appear as Axios cancellations in the development console.

Classification:

| Candidate | Finding | Evidence |
| --- | --- | --- |
| A. Expected lifecycle cancellation | Yes, for stale preview requests replaced by newer requests. | `checkoutStore.ts:1934-1937`, `:1967-1977`, `:1980`, `:2028`. |
| B. Unnecessary duplicate preview requests | Yes. `CheckoutPage` and `ShippingStep` can both trigger preview for the same cart/session state. | `CheckoutPage.tsx:266-272`; `ShippingStep.tsx:565-570`. |
| C. Race condition | Yes, low-to-medium severity lifecycle race/noise. Multiple triggers compete; store abort/sequence guards prevent stale preview state. | Same call sites plus `checkoutStore.ts:1934-2042`. |
| D. Stale cart/session cancellation | Supported and intentional for cart-id/session-init changes. | `checkoutStore.ts:331-345`, `:347-360`, `:389-392`. |
| E. AbortController misuse | No shared-controller misuse found for preview itself. The preview controller is request-scoped but module-owned. | `checkoutStore.ts:127`, `:1934-1937`, `:2041`. |
| F. HTTP-client classification problem | Yes. Axios cancellation is logged as `[HTTP] Response error` and normalized as neither network nor retryable. | `httpClient.ts:38-57`, `:266-279`. |
| G. Combination | Yes. The live symptom is best explained by duplicate lifecycle triggers plus intentional stale-request abort plus misleading HTTP logging. | Complete trace below. |

Verdict: BLOCKED, because a production-relevant duplicate preview lifecycle is source-proven. The state guard appears to preserve correctness, but the duplicate request generation and cancellation logging are real defects/noise sources requiring a minimal implementation step.

## 2. Observed Runtime Symptom

Observed UI succeeded:

```text
Shipping Method
Standard Shipping
EGP 10.00

Order Summary
Shipping (Standard Shipping) EGP 10.00
Total EGP 410.00
```

Observed console error shape:

```text
[HTTP] Response error: {
  code: undefined,
  isAuthError: false,
  isNetworkError: false,
  message: "canceled",
  retryable: false,
  status: undefined,
  url: "/api/storefront/shipping-methods/preview"
}
```

This exact shape is consistent with Axios cancellation entering the response-error interceptor before checkout code ignores it. It is not the backend preview DTO error. Backend missing-session text would be `Checkout session is required.` from `ShippingMethodDto.cs:136-144`.

## 3. Complete Preview Call-Site Inventory

Production Storefront call sites that can start shipping preview all converge on `useCheckoutStore.getState().fetchShippingMethods()`.

| Call site | File/line | Triggering event | Prerequisites | cartId available | checkoutSessionId available | Can overlap | AbortSignal | Can be canceled | Expected cancellation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Store implementation | `PointOfSale.Storefront/src/store/checkoutStore.ts:1919-2043` | Any caller invokes `fetchShippingMethods()` | Current cart must have `cartId`; session ensured before POST | Reads `useCartStore.getState().cart?.cartId` at `:1920`, `:1949-1965` | Ensured by `ensureCheckoutSessionInitialized()` at `:1949` | Yes | Yes, `signal: controller.signal` at `:1971` | Yes, previous controller aborted at `:1934` | Yes for stale/replaced previews |
| CheckoutPage cart lifecycle | `PointOfSale.Storefront/src/pages/CheckoutPage.tsx:266-272` | Cart refresh completed, cart not loading, cart id/lastUpdated changes | `checkoutCartRefreshCompleted && !isCartLoading && cart?.cartId` | Yes | Usually ensured by store | Yes, with ShippingStep and other callers | Indirect through store | Yes | Expected only if replacing older request |
| ShippingStep mount/update | `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx:565-570` | Shipping step effect sees ready cart and no methods | `cartId && isCartInitialized && !isCartLoading && shippingMethods.length === 0` | Yes | Usually ensured by store | Yes, with CheckoutPage | Indirect through store | Yes | Expected only if replacing older request |
| ShippingStep retry button | `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx:765-777` | User clicks retry after `UNAVAILABLE` or `ERROR` | Error UI visible | Likely yes | Ensured by store | Yes, if another refresh is in flight | Indirect through store | Yes | Expected if retry supersedes in-flight preview |
| Final checkout refresh | `PointOfSale.Storefront/src/store/checkoutStore.ts:2450-2477` | User submits checkout | Not already submitting; waits cart merge; awaits fresh cart | Yes after `fetchCart()` | Ensured by store | Yes, can overlap with UI-triggered refresh if already running before submit | Indirect through store | Yes | Expected if final authoritative refresh supersedes stale UI preview |
| Shipping-related checkout failure recovery | `PointOfSale.Storefront/src/store/checkoutStore.ts:2806-2818` | Backend final checkout returns stale/resolution/shipping error | Specific error codes | Usually yes | Ensured by store | Yes | Indirect through store | Yes | Expected if recovery supersedes stale preview |
| Tests only | `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts:480-822` | Unit/certification tests | Test setup | Test-controlled | Test-controlled | Test-controlled | Depends on adapter | Test-controlled | Not production |

Direct construction of the preview URL/payload appears only at:

| Source | Evidence |
| --- | --- |
| Endpoint constant | `checkoutStore.ts:59-65`, `SHIPPING_METHODS_PREVIEW: '/api/storefront/shipping-methods/preview'`. |
| Preview POST | `checkoutStore.ts:1967-1977`, payload `{ checkoutSessionId }`, no `warehouseId`, no `cartId`, no `tenantId`. |

Search terms covered: `fetchShippingMethods`, `shipping-methods/preview`, `previewShipping`, `shipping preview`, `shippingMethods`.

## 4. Checkout Lifecycle Trace

Current source-level lifecycle after Step 2H:

```text
CheckoutPage renders with initial checkout step = shipping
  checkoutStore.ts:1786; CheckoutPage.tsx:513-524
        ↓
CheckoutPage mount effect awaits fetchCart completion marker
  CheckoutPage.tsx:230-248
        ↓
CheckoutPage cart effect fires when cart refresh completed and cart is ready
  CheckoutPage.tsx:266-272
        ↓
ShippingStep is rendered for the shipping step and has its own ready-cart effect
  CheckoutPage.tsx:516-517; ShippingStep.tsx:565-570
        ↓
Both paths can call fetchShippingMethods() while shippingMethods.length is still 0
        ↓
fetchShippingMethods() aborts previous shipping preview controller
  checkoutStore.ts:1934
        ↓
Creates a new AbortController and increments shippingMethodsRequestSequence
  checkoutStore.ts:1935-1937
        ↓
Ensures CheckoutSession for the current cart
  checkoutStore.ts:1949; ensure function at :331-446
        ↓
POST /api/storefront/shipping-methods/preview { checkoutSessionId }
  checkoutStore.ts:1967-1977
        ↓
HTTP client attaches tenant/session headers and sends Axios request
  httpClient.ts:175-250
        ↓
If a newer preview starts, older Axios request is canceled by AbortController
  checkoutStore.ts:1934; httpClient.ts:266-279 logs it
        ↓
Store catch ignores abort/stale sequence and does not set shipping error
  checkoutStore.ts:2027-2029
        ↓
Latest successful preview admits methods, chooses/defaults selection, and selects method
  checkoutStore.ts:1979-2022
        ↓
Selection endpoint creates/reuses WarehouseResolution
  checkoutStore.ts:1880-1917; StorefrontShippingMethodsController.cs:228-284
```

Cancellation origin for the observed preview URL is the Storefront `shippingMethodsAbortController?.abort()` call in `fetchShippingMethods()` before a newer request starts. The HTTP client does not originate cancellation; it logs the Axios cancellation after it occurs.

## 5. AbortController Inventory

Checkout/shipping-relevant controllers:

| Controller | Owner | Creation | Replacement | Abort point | Cleanup | Associated request | Shared between requests | Newer aborts older | Component unmount abort | Cart/session change abort |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `shippingMethodsAbortController` | `checkoutStore.ts` module singleton | `checkoutStore.ts:1935-1936` | Every `fetchShippingMethods()` call | `:1921`, `:1934`, `:681-684`, `:2869` | `:2041` | Preview POST only | Not concurrently shared; reference is replaced | Yes | Only via store `reset`, not CheckoutPage/ShippingStep unmount | Indirectly, because cart/session lifecycle triggers new fetch |
| `checkoutSessionInitAbortController` | `checkoutStore.ts` module singleton | `:370-372` | New session init or cart-id mismatch | `:145-152`, `:359-360`, `:370`, `:2460`, `:2866` | `:425-431` | Checkout-session POST | Not concurrently shared for same cart; deduped by promise | Yes when cart id changes | Via store reset/submit, not page unmount | Yes, when init cart id differs |
| `checkoutAutosaveAbortController` | `checkoutStore.ts` module singleton | `:466-468` | New autosave | `:138-143`, `:558`, `:2459`, `:2865` | `:551-553` | Checkout-session PATCH | Not shared intentionally | Yes | Via reset/submit | Address/payment/manual wallet autosave changes |
| `pricingPreviewAbortController` | `checkoutStore.ts` module singleton | `:2227-2231` | New pricing preview | `:167-171`, `:2228`, `:2867` | `:2281-2284` | Pricing POST | Not shared intentionally | Yes | Via reset | Cart, shipping method, payment method, country/geography changes |
| `checkoutFieldsAbortController` | `checkoutStore.ts` module singleton | `:2293-2296` | New fields fetch | `:675-679`, `:2293`, `:2868` | `:2341-2343` | Checkout-fields GET | Not shared intentionally | Yes | Via reset | Force refresh/retry |
| `PhoneOtpVerification` controllers | Checkout component-local refs | `PhoneOtpVerification.tsx:76-87`, `:165-178`, `:224-236` | Per OTP action | `:54-56`, `:76`, `:165`, `:224` | Component cleanup/action replacement | Phone OTP status/request/verify | Separate refs | Yes within OTP only | Yes | No shipping relation |
| `CheckoutPendingPage` controller | Pending checkout page local ref | `CheckoutPendingPage.tsx:75-82` | Poll/check replacement | `:55`, `:75`, `:151` | Component cleanup | Payment status | Separate | Yes within pending page | Yes | No shipping relation |

Non-checkout Storefront controllers found in `AnalyticsClient`, `useVariantSelector`, `useProductSearch`, `useAffiliateFunnel`, and `searchStore` are unrelated to shipping preview and cannot cancel the preview request because they do not share `shippingMethodsAbortController` or the preview Axios config.

No source evidence shows one controller accidentally canceling an unrelated shipping preview. The preview cancellation controller is specific to `fetchShippingMethods()`.

## 6. Request Cancellation Origin

The observed cancellation originates here:

```text
fetchShippingMethods()
  shippingMethodsAbortController?.abort();
  const controller = new AbortController();
  shippingMethodsAbortController = controller;
  const sequence = ++shippingMethodsRequestSequence;
```

Source: `PointOfSale.Storefront/src/store/checkoutStore.ts:1934-1937`.

The canceled request is then rejected by Axios. The response interceptor logs all rejected responses, including cancellation:

| Step | Source |
| --- | --- |
| Normalize status/code/message | `httpClient.ts:38-57`. |
| Log `[HTTP] Response error` in development | `httpClient.ts:266-279`. |
| Reject cancellation to caller | `httpClient.ts:312-315`. |
| Store ignores cancellation/stale sequence | `checkoutStore.ts:2027-2029`. |

This is why the browser can show a scary HTTP console error while the user-visible shipping state remains healthy after the latest request succeeds.

## 7. Duplicate Request Analysis

Production duplicate sequence proven from source:

```text
T0 CheckoutPage renders shipping step by default
   checkoutStore.ts:1786; CheckoutPage.tsx:516-517

T1 CheckoutPage mount effect fetches cart and sets checkoutCartRefreshCompleted
   CheckoutPage.tsx:230-248

T2 Cart is ready and methods list is still empty

T3 CheckoutPage cart effect calls fetchShippingMethods()
   CheckoutPage.tsx:266-272

T4 ShippingStep ready-cart effect also sees cart ready and shippingMethods.length === 0
   ShippingStep.tsx:565-570

T5 Both calls enter checkoutStore.fetchShippingMethods()

T6 Later call aborts earlier shippingMethodsAbortController
   checkoutStore.ts:1934

T7 Axios logs earlier preview as canceled
   httpClient.ts:266-279

T8 Store ignores the canceled/stale request
   checkoutStore.ts:2027-2029

T9 Latest request succeeds and populates methods/order summary
   checkoutStore.ts:1979-2022
```

Events that can cause `fetchShippingMethods()`:

| Event | Source | Duplicate/storm risk |
| --- | --- | --- |
| CheckoutPage cart ready/change | `CheckoutPage.tsx:266-272` | Yes, overlaps ShippingStep. |
| ShippingStep mount/update | `ShippingStep.tsx:565-570` | Yes, overlaps CheckoutPage because it ignores loading/status and only checks `shippingMethods.length`. |
| Shipping retry button | `ShippingStep.tsx:765-777` | Low; manual. |
| Final checkout submit | `checkoutStore.ts:2458-2477` | Medium; intentionally refreshes authoritative shipping before validation. |
| Shipping/session/resolution final-checkout errors | `checkoutStore.ts:2806-2818` | Low; recovery path. |
| Cart `lastUpdatedUtc` changes | `CheckoutPage.tsx:266-272` | Medium; legitimate if cart actually changed. |
| React StrictMode development effect replay | `main.tsx:20-24` | Development-only amplification. |

Single checkout initialization can generate at least two preview requests before the first successful response in production. In development StrictMode, effect replay can amplify adjacent lifecycle calls, especially when cart state is already hydrated or effects are replayed during development remount.

Impact: unnecessary backend load, unnecessary DB/Redis work, excessive browser logs, and possible UI loading churn. Source evidence shows stale preview state is guarded, so stale methods should not become authoritative from the canceled preview.

## 8. React StrictMode Analysis

React StrictMode is enabled:

| Source | Evidence |
| --- | --- |
| `PointOfSale.Storefront/src/main.tsx:20-24` | `<React.StrictMode><App /></React.StrictMode>`. |
| `PointOfSale.Storefront/vite.config.ts:42-56` | Vite dev server config; React refresh preamble only in serve mode. |

StrictMode can contribute to development-only duplicate effects. React 18 development StrictMode intentionally mounts, cleans up, and re-runs effects to expose lifecycle bugs. That can multiply CheckoutPage/ShippingStep effects and make cancellation logs more frequent.

StrictMode is not required to reproduce the core duplicate source path. The production-relevant duplicate exists because `CheckoutPage` and `ShippingStep` independently call the same store action for the same ready cart state.

Classification:

| Cause | Development | Production |
| --- | --- | --- |
| StrictMode double effects | Yes, can amplify. | No. |
| Vite React Refresh/HMR | Yes, can trigger remount/refetch during edits. | No. |
| CheckoutPage plus ShippingStep duplicate triggers | Yes. | Yes. |
| Store aborting older preview when newer starts | Yes. | Yes. |

## 9. HTTP Client Cancellation Classification

The Storefront HTTP client is Axios-based:

| Behavior | Source | Finding |
| --- | --- | --- |
| Axios instance | `httpClient.ts:14`, `:164-173` | Axios supports `AbortSignal` cancellation. |
| Error normalization | `httpClient.ts:38-57` | Uses HTTP response status and `message === 'network error'`; does not identify `ERR_CANCELED`, `CanceledError`, or `AbortError`. |
| DEV error log | `httpClient.ts:266-279` | Logs all rejected responses as `[HTTP] Response error`. |
| Retryability | `httpClient.ts:55` | Canceled request becomes `retryable: false`, not network/server. |
| Store abort detection | `checkoutStore.ts:197-205`, reused at `:2028` | Store recognizes `ERR_CANCELED` and `CanceledError` as abort-like and ignores them. |

The observed console representation exactly matches this classification gap:

```text
message: "canceled"
status: undefined
code: undefined
isNetworkError: false
retryable: false
```

Axios can expose cancellation with `code: 'ERR_CANCELED'` and `name: 'CanceledError'`, but `normalizeError()` only reports `response?.data?.code`, so the normalized `code` is `undefined`. This is why intentional cancellation is logged as an HTTP response error instead of a quiet/trace-level intentional cancellation.

No automatic retry is driven by this HTTP-client classification for shipping preview.

## 10. Cart/Session Transition Analysis

Step 2H added cart-aware session association:

| Invariant | Source |
| --- | --- |
| Missing cart id returns no session and no preview | `checkoutStore.ts:331-336`, `:1919-1932`. |
| Existing session reused only if `checkoutSessionCartId === currentCartId` and upload token exists | `checkoutStore.ts:338-341`. |
| Existing session cleared when cart id differs | `checkoutStore.ts:343-345`. |
| Concurrent init for same cart reuses promise | `checkoutStore.ts:347-356`. |
| Concurrent init for different cart aborts old init | `checkoutStore.ts:359-360`. |
| Session response ignored if current cart id changed | `checkoutStore.ts:388-392`. |
| Session id stored with cart id | `checkoutStore.ts:401-405`. |

Required invariant:

```text
preview(cart A, session A) MUST NOT become authoritative after Storefront transitions to cart B.
```

Source result: PASS for preview response authority. A changed cart triggers a new session path, older session initialization is canceled/ignored, and preview response admission checks `controller.signal.aborted || sequence !== shippingMethodsRequestSequence` before mutating methods (`checkoutStore.ts:1980`). Error state is also skipped for aborted/stale requests (`checkoutStore.ts:2028`).

Safe cancellation:

```text
preview(cart A, session A) can be canceled when cart B/session B becomes authoritative.
```

Source result: PASS. The store aborts the older preview and sequence-guards it. This is intentional stale-result protection.

The live console cancellation may represent this cart/session transition class if a cart refresh or cart mutation happened while preview was in flight. However, the source-proven same-state duplicate between CheckoutPage and ShippingStep can produce the same console symptom without any real cart/session change.

## 11. ShippingStep Analysis

`ShippingStep` independently initiates preview:

| Question | Finding | Evidence |
| --- | --- | --- |
| Has `useEffect` preview trigger | Yes. | `ShippingStep.tsx:565-570`. |
| Requires ready cart | Yes. | `cartId && isCartInitialized && !isCartLoading`. |
| Requires empty methods | Yes. | `shippingMethods.length === 0`. |
| Checks `shippingMethodsLoading` before fetching | No. | Effect does not include loading/status guard. |
| Checks `shippingMethodsStatus === 'LOADING'` | No. | Effect only checks length. |
| Retry behavior | Manual button calls `fetchShippingMethods()` | `ShippingStep.tsx:765-777`. |
| Selected method changes trigger preview | No direct preview; `setShippingMethod` selects/creates resolution. | `ShippingStep.tsx:555`, rendered radios below `:805`; store selection at `checkoutStore.ts:1880-1917`. |
| Address changes trigger preview | No direct preview; address autosaves and pricing country/geography can refresh pricing. | `checkoutStore.ts:1856-1868`; `CheckoutPage.tsx:250-264`. |

Conclusion: `CheckoutPage` and `ShippingStep` can both trigger the same preview. This is the strongest source-level explanation for canceled preview entries with successful latest shipping methods.

## 12. Preview State Authority Analysis

Expected scenario:

```text
request A starts
request B starts
request A is canceled
request B succeeds
state = B
```

Source result: PASS for preview methods. `request B` aborts `request A` at `checkoutStore.ts:1934`, increments sequence at `:1937`, and only the latest non-aborted sequence mutates methods at `:1980-2018`. Canceled/stale errors return without setting error state at `:2027-2029`.

Unusual timing scenario:

```text
request A succeeds after request B due to timing
request A must not overwrite newer state
```

Source result: PASS for preview methods because sequence comparison blocks stale response mutation at `checkoutStore.ts:1980`.

Residual risk: automatic `setShippingMethod(nextShippingMethodId)` after preview has no AbortSignal or request sequence (`checkoutStore.ts:1880-1917`, called at `:2020-2022`). If request A passes the preview sequence check and starts selection, then request B later starts and succeeds, selection A can still complete out of order. In the common observed case with the same default method, this is unlikely to be user-visible. It remains a secondary race risk if eligible methods or selected method differ between overlapping previews.

Cancellation does not directly clear valid final shipping state. The latest successful preview plus selection is authoritative.

## 13. Retry Analysis

No automatic retry loop was found for canceled shipping preview.

| Path | Behavior | Evidence |
| --- | --- | --- |
| Preview cancellation | Store catch returns before setting error | `checkoutStore.ts:2027-2029`. |
| Shipping error UI retry | Manual only | `ShippingStep.tsx:765-777`. |
| Retryable classification | Cancellation normalized as `retryable: false` | `httpClient.ts:38-57`. |
| Checkout submit recovery | Calls `fetchShippingMethods()` only for specific backend final-checkout error codes | `checkoutStore.ts:2806-2818`. |

No `request -> cancel -> error -> automatic retry -> cancel` loop is source-proven. The problem is duplicate trigger generation and logging noise, not an infinite retry storm.

## 14. Backend Impact Analysis

Backend preview endpoint:

| Step | Source |
| --- | --- |
| Route | `StorefrontShippingMethodsController.cs:136-144`. |
| Validates tenant/owner/body/session id | `:149-180`. |
| Loads session/current cart | `WarehouseResolutionService.cs:120-139`. |
| Filters configured warehouse shipping methods | `ShippingMethodService.cs:184-274`. |
| Returns preview response | `StorefrontShippingMethodsController.cs:190-196`. |

Client cancellation impact in source:

| Backend behavior | Finding |
| --- | --- |
| CancellationToken passed through | Yes, controller receives `CancellationToken` and passes it to cart/session/shipping queries (`StorefrontShippingMethodsController.cs:142-188`). |
| Can abort DB/Redis/API work | Yes, because token is passed to EF and cart/service calls. Actual cancellation timing depends on server/runtime. |
| Dedicated `OperationCanceledException` handling | Not present. Generic `catch (Exception ex)` at `StorefrontShippingMethodsController.cs:214-225` can log cancellation as an error if cancellation is thrown there. |
| Transaction rollback | Preview itself does not create `WarehouseResolution`; Step 2G preview uses `LoadPreviewCartAsync`, not `ResolveForPreviewAsync` (`StorefrontShippingMethodsController.cs:175-188`; `WarehouseResolutionService.cs:120-139`). No preview transaction/warehouse-resolution write is expected. |
| Unnecessary DB work | Yes, duplicate previews can repeat session/cart/method/warehouse/stock/carrier readiness reads before one is canceled. |
| Misleading server logs | Possible if cancellation bubbles into generic catch. |

Backend architecture remains correct; the load/logging concern is caused by duplicate client lifecycle requests and lack of explicit cancellation classification.

## 15. Development vs Production Analysis

| Cause | Development-only | Production-relevant | Notes |
| --- | --- | --- | --- |
| React StrictMode effect replay | Yes | No | Enabled at `main.tsx:20-24`; can amplify duplicate effects. |
| Vite React Refresh/HMR | Yes | No | `vite.config.ts:42-56`. |
| HTTP DEV console logging | Yes | No for console noise | `[HTTP] Response error` log is inside `import.meta.env.DEV` at `httpClient.ts:269-279`. |
| Axios request cancellation | No | Yes | AbortSignal works in both. |
| CheckoutPage plus ShippingStep duplicate previews | No | Yes | Source-proven independent triggers. |
| Backend duplicate work from overlapping previews | No | Yes | If production duplicate requests occur before abort reaches server. |

The live console symptom is primarily development-visible because the HTTP client logs only under `import.meta.env.DEV`. The underlying duplicate preview generation is production-relevant even if production users do not see the console error.

## 16. Security/Authority Regression Check

No authority regression was found.

| Forbidden regression | Result | Evidence |
| --- | --- | --- |
| `warehouseId` added to preview | Not found | Preview payload is `{ checkoutSessionId }` at `checkoutStore.ts:1967-1977`; architecture test asserts no `warehouseId:` in store at `ShopixyWarehouseResolutionAuthorityCertificationTests.cs:47-65`. |
| `cartId` added as preview authority | Not found | Preview payload only `{ checkoutSessionId }`; cart id used only to create/resume session at `checkoutStore.ts:375-385`. |
| Browser tenant authority in preview body | Not found | Tenant travels through headers/context; preview body has no tenant id. |
| Browser-controlled warehouse selection | Not found | Selection payload is `{ checkoutSessionId, shippingMethodId }` at `checkoutStore.ts:1896-1904`; backend derives warehouse from selected method at `WarehouseResolutionService.cs:192-255`. |
| CheckoutSession bypass | Not found | Backend validates non-empty checkout session at `ShippingMethodDto.cs:132-144` and loads owned active session/cart at `StorefrontShippingMethodsController.cs:173-180`. |
| WarehouseResolution bypass | Not found | Final checkout requires session/resolution/version and validates them at `checkoutStore.ts:2519-2534`, `CheckoutService` per prior Step 2H. |
| Carrier readiness bypass | Not found | External readiness remains in `ShippingMethodService.cs:234-257`; manual carrier bypass remains intentional. |

Step 2I introduced no source changes, so no new regression was introduced by this audit.

## 17. Test Coverage Matrix

| Coverage area | Existing coverage | Evidence | Gap |
| --- | --- | --- | --- |
| Preview happy path/admission | Yes | `commerceAuthority.certification.test.ts:459-485`, `:701-736`. | Does not simulate overlapping preview cancellation. |
| Missing cart before preview | Yes | `commerceAuthority.certification.test.ts:683-699`. | Covered. |
| Delayed cart hydration/session before preview | Yes | `commerceAuthority.certification.test.ts:701-736`. | Covered. |
| Cart-id swap invalidates session | Yes | `commerceAuthority.certification.test.ts:738-762`. | Covered. |
| Concurrent session initialization dedupe | Yes | `commerceAuthority.certification.test.ts:764-788`. | Covered for session, not preview. |
| ShippingStep mount guard | Partial static test | `commerceAuthority.certification.test.ts:790-795`. | Does not prove CheckoutPage + ShippingStep duplicate prevention. |
| Preview cancellation | No | Search found no cancellation/AbortController preview test. | Missing. |
| Stale preview cancellation | No | No test with two overlapping `fetchShippingMethods()` and one abort. | Missing. |
| Concurrent preview calls | No | No controlled slow adapter asserting first is aborted and latest wins. | Missing. |
| Request sequence/generation | Partial by source only | `checkoutStore.ts:1937`, `:1980`, `:2028`. | No direct test. |
| HTTP cancellation classification | No | `httpClient.ts` has no visible dedicated cancellation test. | Missing. |
| Real errors vs cancellation | Partial | Network failure test at `commerceAuthority.certification.test.ts:657-667`; session failure at `:797-827`. | Cancellation path not covered. |
| Backend missing session | Yes | `StorefrontShippingMethodsControllerContractTests.cs:18-72`. | Covered. |
| Backend client cancellation | No | No OperationCanceled/request-aborted test found. | Missing. |
| Preview does not create WarehouseResolution | Yes static | `ShopixyWarehouseResolutionAuthorityCertificationTests.cs:47-65`. | Covered statically. |

Recommended tests in a future implementation step: concurrent preview cancellation, CheckoutPage/ShippingStep duplicate prevention, HTTP cancellation normalization/logging, and backend cancellation logging behavior.

## 18. Runtime Evidence

LIVE RUNTIME FORENSIC VERIFICATION NOT PERFORMED.

No browser automation/runtime tooling was used in this audit. Network timing, exact request counts, and live request order were not captured. The conclusions above are source-forensic conclusions only.

Safe runtime data to capture in external verification:

| Data | Purpose |
| --- | --- |
| Number of preview POSTs per checkout entry | Confirm 1 vs 2 vs 3+ requests. |
| Start order and initiator stack | Confirm CheckoutPage vs ShippingStep vs submit/retry. |
| Cancellation order | Confirm older request canceled when newer request starts. |
| Latest successful response | Confirm final request populates UI. |
| Redacted cart/session ids | Confirm same-state duplicate vs cart/session transition. |

Do not expose secrets, full session identifiers, auth tokens, or customer PII in runtime evidence.

## 19. Root Cause

Root cause of observed canceled preview console entries:

```text
Independent checkout lifecycle paths generate overlapping shipping preview requests.
checkoutStore intentionally aborts the older preview when a newer preview starts.
Axios reports that intentional abort as message "canceled".
httpClient logs it as [HTTP] Response error in development.
checkoutStore ignores the cancellation, so the latest preview succeeds and UI remains correct.
```

Primary source causes:

| Cause | Evidence |
| --- | --- |
| Duplicate preview generation | `CheckoutPage.tsx:266-272` and `ShippingStep.tsx:565-570`. |
| Intentional stale request abort | `checkoutStore.ts:1934-1937`. |
| State authority guard | `checkoutStore.ts:1980`, `:2028`. |
| Misleading HTTP cancellation log | `httpClient.ts:266-279`; normalizer at `:38-57`. |

The final/latest preview succeeded because the abort mechanism is designed to make the newest request authoritative. The canceled requests are not backend shipping failures.

## 20. Severity

Severity: P2.

Rationale:

| Dimension | Assessment |
| --- | --- |
| User-visible checkout failure | Not proven; observed UI succeeded. |
| Backend shipping failure | Not proven; cancellation is client-originated. |
| Production duplicate backend load | Proven possible. |
| State corruption from preview response | Not proven; preview has sequence/abort protection. |
| Secondary selection race | Possible; `setShippingMethod` lacks abort/sequence. |
| Developer/operator noise | Proven in development console. |
| Security/authority regression | Not found. |

Certification remains BLOCKED because PASS requires proving no defect and healthy cancellation only. Source proves duplicate production preview generation and cancellation misclassification defects.

## 21. Recommended Minimal Fix

Do not implement in this audit.

Smallest implementation boundary:

1. Choose one owner for automatic shipping preview lifecycle. Prefer `CheckoutPage` as the cart/session lifecycle owner and remove or narrow the `ShippingStep` automatic fetch, or make `ShippingStep` guard on `shippingMethodsStatus !== 'LOADING'` and a request-owned freshness flag.
2. Add an in-flight/loading guard or request-key dedupe in `fetchShippingMethods()` for the same `cartId + checkoutSessionId + cart.lastUpdatedUtc` state, while still allowing explicit force refresh for submit/retry.
3. Teach `httpClient.normalizeError()` or the response interceptor to distinguish intentional Axios cancellation (`ERR_CANCELED`, `CanceledError`, `AbortError`, `message === 'canceled'`) from actual response failures and avoid logging it as `[HTTP] Response error` in DEV.
4. Keep preview payload unchanged: `{ checkoutSessionId }` only.
5. Add tests for overlapping preview calls and cancellation classification.

Do not change backend preview authority, WarehouseResolution, carrier authority, payment architecture, or database schema.

## 22. Explicit Non-Goals

This audit did not and must not:

| Non-goal | Status |
| --- | --- |
| Modify source code | Not done. |
| Modify tests | Not done. |
| Modify configuration | Not done. |
| Modify schema/migrations | Not done. |
| Commit, stash, reset, clean, revert, checkout, or push | Not done. |
| Change preview contract | Not done. |
| Add browser-controlled `warehouseId`/`tenantId`/`cartId` authority | Not done. |
| Change WarehouseResolution architecture | Not done. |
| Change carrier readiness/manual fulfillment authority | Not done. |
| Fabricate runtime timing | Not done. Runtime verification was not performed. |

## 23. Implementation Readiness

Ready for a narrow implementation step.

Recommended file boundary:

| File | Reason |
| --- | --- |
| `PointOfSale.Storefront/src/pages/CheckoutPage.tsx` | Primary lifecycle owner candidate. |
| `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx` | Remove/narrow duplicate automatic fetch or add loading/status guard. |
| `PointOfSale.Storefront/src/store/checkoutStore.ts` | Optional same-state in-flight dedupe and selection sequence hardening. |
| `PointOfSale.Storefront/src/services/api/httpClient.ts` | Cancellation classification/logging. |
| `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts` or focused tests | Concurrent preview and cancellation coverage. |

Backend changes are not required for the primary observed console symptom. Optional backend cancellation-log hardening can be considered separately if server logs show `OperationCanceledException` noise.

## 24. Worktree Evidence

Initial commands executed before audit:

```text
git status --short
git diff --stat
```

Initial status summary: repository was heavily dirty before Step 2I, including tracked modifications in Storefront checkout/shipping files, backend shipping/checkout files, tests, and Web shipping administration files, plus untracked Step 2A-2H reports and warehouse-resolution migration/artifact files.

Initial diff-stat summary: `48 files changed, 2940 insertions(+), 253 deletions(-)` before this audit report was created.

Representative initial dirty files:

```text
M PointOfSale.Storefront/src/pages/CheckoutPage.tsx
M PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx
M PointOfSale.Storefront/src/store/checkoutStore.ts
M PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs
M PointOfSale.API/Services/Storefront/ShippingMethodService.cs
?? docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2H_FORENSIC_AUDIT_2026-08-11.md
?? docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2H_IMPLEMENTATION_2026-08-11.md
```

Final commands executed after audit:

```text
git status --short
git diff --stat
```

Final status summary: the same pre-existing dirty tracked files remained, and this report appeared as the only intended new Step 2I artifact:

```text
?? docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2I_FORENSIC_AUDIT_2026-08-11.md
```

Final diff-stat summary: tracked-file diff-stat remained `48 files changed, 2940 insertions(+), 253 deletions(-)`. The new report is untracked, so it does not appear in `git diff --stat`.

No destructive git operation was performed.

## 25. Final Certification

Certification: BLOCKED

Reason:

The canceled preview requests are not backend shipping failures. They are source-proven intentional cancellations of superseded Storefront preview requests. However, source also proves production-relevant duplicate preview generation between `CheckoutPage` and `ShippingStep`, and the HTTP client misclassifies/logs intentional Axios cancellation as `[HTTP] Response error` in development.

Final answer to Step 2I:

```text
WHY ARE SHIPPING PREVIEW REQUESTS BEING CANCELED?

Because checkoutStore aborts the previous shipping preview request whenever a newer
fetchShippingMethods() call starts. Duplicate lifecycle triggers can start overlapping
previews for the same checkout state, so older previews are canceled while the latest
preview succeeds.

IS THAT CANCELLATION HEALTHY OR A REAL BUG?

The stale-request cancellation mechanism is healthy and protects preview state authority.
The duplicate preview generation and HTTP cancellation logging/classification are real
defects/noise sources. The observed successful UI proves the latest preview path worked,
but the lifecycle should still be narrowed so normal checkout initialization does not
generate avoidable canceled preview requests.
```
