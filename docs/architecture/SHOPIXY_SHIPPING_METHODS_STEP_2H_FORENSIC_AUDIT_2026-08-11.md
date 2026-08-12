# SHOPIXY Shipping Methods Step 2H Forensic Audit

Date: 2026-08-11

Scope: Checkout Session -> Shipping Preview integration audit.

Mode: Audit-only. No source, configuration, migration, schema, test, or frontend behavior changes were made by this audit. This report is the only intended artifact.

Certification: BLOCKED

## 1. Executive Summary

The observed Storefront error is produced by the Storefront before the preview API is called.

Primary root cause: `CheckoutPage` invokes `fetchCart()` and `fetchShippingMethods()` in the same mount effect without awaiting cart hydration. `fetchShippingMethods()` correctly calls `ensureCheckoutSessionInitialized()`, but that helper returns `null` when `useCartStore.getState().cart?.cartId` is absent. `fetchShippingMethods()` then throws the exact observed local error: `Checkout session is required before shipping methods can be previewed.`

This is not primarily a shipping-method configuration failure. The failure happens before backend shipping-method eligibility is evaluated. The backend preview contract requires a valid server-owned `checkoutSessionId`; Storefront sometimes attempts preview before it has enough cart context to create or resume one.

Classification against the candidate causes:

| Candidate | Result | Evidence |
| --- | --- | --- |
| A. CheckoutSession is never created | Not primary | Creation endpoint exists and Storefront has `ensureCheckoutSessionInitialized()` posting to it. Failure path occurs before cart is available, so no session can be created for that invocation. |
| B. CheckoutSession exists but checkoutStore does not retain its ID | Not primary | Storefront stores `response.data.sessionId` into `checkoutSessionId`. It is in-memory only and intentionally not persisted across reload. |
| C. CheckoutSession exists but fetchShippingMethods executes before session creation | Primary | On checkout mount, `fetchShippingMethods()` can execute before `fetchCart()` completes and before `initCheckoutSession()` fires. |
| D. Race condition between session creation and shipping preview | Primary race class P1 | The race is between cart hydration/session creation prerequisites and shipping preview. Shared `checkoutSessionInitPromise` dedupes concurrent session creation but does not wait for cart hydration. |
| E. Cart changes invalidate/recreate the session and shipping preview uses stale/null state | Secondary risk | Existing `checkoutSessionId` is reused without checking current cart id. If cart id changes, backend rejects because session cart no longer matches authoritative cart. |
| F. Session creation depends on an event/action not triggered on this checkout path | Secondary contributor | `initCheckoutSession()` is fire-and-forget from begin-checkout analytics after cart load, but preview should not depend on analytics timing. |
| G. API contract changed during Step 2F/2G but Storefront integration was not fully aligned | Primary integration class | Storefront uses the new preview/select endpoints, but lifecycle ordering was not fully aligned with the new session-required preview contract. |
| H. Wrong session identifier/property is being passed | No | Storefront posts `{ checkoutSessionId }` using backend `SessionId` response. |
| I. Tenant/cart/session ownership rejects the session | Not for observed error | Backend would return `CHECKOUT_SESSION_STALE` messages. The observed exact text is local Storefront text. |
| J. Another lifecycle/order-of-operations problem | Yes | Checkout mount, ShippingStep mount, retry, cart refresh, and submit all have different paths into `fetchShippingMethods()`. |

## 2. Current Architecture

Current checkout shipping architecture is:

```text
Storefront cart authority
        ↓
CheckoutSession create/resume by tenant + authoritative cart + owner
        ↓
Storefront preview posts checkoutSessionId only
        ↓
Backend loads owned active CheckoutSession and current authoritative cart
        ↓
Shipping methods filtered by each method's configured FulfillmentWarehouseId
        ↓
Storefront selects shippingMethodId only
        ↓
Backend creates/reuses WarehouseResolution for that method's configured warehouse
        ↓
Final checkout submits checkoutSessionId + warehouseResolutionId + version + shippingMethodId
        ↓
Backend revalidates and locks resolution, reserves inventory, stamps order/order items
```

Relevant files:

| Area | File/function |
| --- | --- |
| Storefront checkout page | `PointOfSale.Storefront/src/pages/CheckoutPage.tsx:227-236`, `:254-271` |
| Storefront checkout store | `PointOfSale.Storefront/src/store/checkoutStore.ts:312-395`, `:1825-1961`, `:2381-2604` |
| Checkout session API | `PointOfSale.API/Controllers/CheckoutSessionController.cs:101-203` |
| Shipping preview/select API | `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs:136-312` |
| Preview/select DTOs | `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs:132-199` |
| Warehouse resolution | `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs:120-139`, `:192-255`, `:257-397`, `:864-935` |
| Shipping method eligibility | `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:184-274`, `:531-658` |
| Final checkout | `PointOfSale.API/Services/Checkout/CheckoutService.cs:864-874`, `:6403-6475`, `:6588-6592` |

Step-history evidence:

| Step | Contract evidence |
| --- | --- |
| Step 2A | Server response `tenantId` is canonical tenant scope; Storefront must not trust raw `X-Tenant-Id` as result authority. `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2A_TENANT_SCOPE_REMEDIATION_2026-08-11.md:31-47`. |
| Step 2F | Preview accepts `{ checkoutSessionId }`; Storefront should create/resume session and submit session/resolution/version/method, not `warehouseId`. `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2F_IMPLEMENTATION_2026-08-11.md:74-88`. |
| Step 2G | Preview validates session/cart without creating resolution; selection accepts `checkoutSessionId` + `shippingMethodId` and creates/reuses resolution. `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2G_IMPLEMENTATION_2026-08-11.md:9-12`, `:108-130`. |

## 3. CheckoutSession Lifecycle

Lifecycle diagram with current source-level transitions:

```text
Checkout route renders CheckoutPage
  PointOfSale.Storefront/src/pages/CheckoutPage.tsx:179
        ↓
Mount effect invokes fetchCart(), fetchCheckoutFields(), fetchShippingMethods(), fetchPaymentMethods()
  CheckoutPage.tsx:227-236
        ↓
fetchShippingMethods() attempts ensureCheckoutSessionInitialized()
  checkoutStore.ts:1864-1883
        ↓
ensureCheckoutSessionInitialized() reads useCartStore.getState().cart
  checkoutStore.ts:323-327
        ↓
If cart.cartId is missing, session creation returns null and preview throws local error
  checkoutStore.ts:324-327, 1880-1883
        ↓
When cart later exists, cart effect calls fetchShippingMethods() again
  CheckoutPage.tsx:254-260
        ↓
ensureCheckoutSessionInitialized() POSTs /api/storefront/checkout-session
  checkoutStore.ts:338-349
        ↓
Backend validates tenant, owner, authoritative cart, then creates/resumes session
  CheckoutSessionController.cs:112-183
        ↓
Backend returns CheckoutSessionResponse.SessionId and ManualWalletUploadToken
  CheckoutSessionController.cs:193-202; CheckoutSessionDtos.cs:67-75
        ↓
Storefront stores checkoutSessionId and manualWalletUploadToken in Zustand
  checkoutStore.ts:358-367
        ↓
fetchShippingMethods() POSTs /api/storefront/shipping-methods/preview with { checkoutSessionId }
  checkoutStore.ts:1885-1895
        ↓
Backend validates active owned session and cart match, then filters methods
  StorefrontShippingMethodsController.cs:173-196; WarehouseResolutionService.cs:120-139
        ↓
Storefront admits tenant-scoped preview response and auto-selects first method if needed
  checkoutStore.ts:798-814, 1901-1940
        ↓
setShippingMethod() POSTs /api/storefront/shipping-methods/select with { checkoutSessionId, shippingMethodId }
  checkoutStore.ts:1825-1861
        ↓
Backend creates/reuses WarehouseResolution for method's FulfillmentWarehouseId
  StorefrontShippingMethodsController.cs:267-283; WarehouseResolutionService.cs:192-255
        ↓
Final checkout submits checkoutSessionId + warehouseResolutionId + resolutionVersion + shippingMethodId
  checkoutStore.ts:2381-2604
        ↓
Backend validates and locks resolution, reserves inventory, stamps Order.WarehouseId and OrderItem.WarehouseId
  CheckoutService.cs:6403-6475
```

Session identifier inventory:

| Identifier | Meaning | Current usage |
| --- | --- | --- |
| `checkoutSessionId` | Server-generated `CheckoutSession.Id`; opaque checkout-session authority. | Stored in Zustand at `checkoutStore.ts:365`; posted to preview/select/final checkout. |
| `sessionId` in `CheckoutSessionResponse` | JSON camel-case projection of backend `SessionId` property, which is the checkout session row ID in response DTO. | Storefront reads `response.data?.sessionId` at `checkoutStore.ts:358`. |
| `X-Session-Id` | Browser/guest owner identity header. | Backend uses it to validate guest ownership at `StorefrontShippingMethodsController.cs:325-364`; not equivalent to checkoutSessionId. |
| `cartId` | Storefront cart identity. | Required to create/resume CheckoutSession; backend validates current cart matches session cart. |
| `checkoutToken` | Order/result token after successful checkout. | Stored in `orderResult`, not used for shipping preview. |
| `warehouseResolutionId` | Server-created shipping warehouse resolution selected after method selection. | Stored after `/select`, submitted only at final checkout. |

Identifier survival:

| Event | `checkoutSessionId` | Evidence |
| --- | --- | --- |
| Initial checkout load | Starts `null`; created only after cart exists. | Initial state `checkoutStore.ts:1782`; create requires cart at `:324-327`. |
| Address changes | Preserved; autosave PATCH uses existing/created session. | `setAddressField` and `setAddress` call autosave at `checkoutStore.ts:1801-1812`; PATCH at `:425-451`. |
| Cart changes | Preserved in Storefront even if cart id changes. | `ensureCheckoutSessionInitialized()` returns existing id when id+upload token exist at `checkoutStore.ts:312-317`; no cart-id check. |
| Shipping refresh | Preserved; warehouse resolution cleared before preview. | `fetchShippingMethods()` clears resolution metadata at `checkoutStore.ts:1869-1877`. |
| Payment changes | Preserved; autosaved. | `selectPaymentMethod()` calls autosave at `checkoutStore.ts:2062-2099`. |
| Checkout retry | Preserved after failure; refresh paths may call `fetchShippingMethods()`. | Error handling refreshes shipping for shipping/session/resolution codes at `checkoutStore.ts:2723-2734`. |
| Page refresh/reload | Not persisted; returns to `null`. | Persist partialize excludes `checkoutSessionId` at `checkoutStore.ts:2939-2945`. |
| Successful checkout | Cleared. | `checkoutStore.ts:2666-2668`. |
| Store reset/recovery | Cleared. | `checkoutStore.ts:2780-2792`; `RecoverCheckoutPage.tsx:194-219`. |

## 4. Storefront Shipping Preview Trace

Current `fetchShippingMethods()` control flow:

```text
fetchShippingMethods()
  checkoutStore.ts:1864
        ↓
abort previous shipping request, create controller, increment sequence
  checkoutStore.ts:1865-1868
        ↓
set LOADING and clear WarehouseResolution state
  checkoutStore.ts:1869-1877
        ↓
await ensureCheckoutSessionInitialized()
  checkoutStore.ts:1880
        ↓
if null, throw local Error('Checkout session is required before shipping methods can be previewed.')
  checkoutStore.ts:1881-1882
        ↓
POST /api/storefront/shipping-methods/preview { checkoutSessionId }
  checkoutStore.ts:1885-1895
        ↓
admit response tenant and methods
  checkoutStore.ts:1897; admission at :798-814
        ↓
ignore stale/aborted response if sequence changed
  checkoutStore.ts:1898
        ↓
preserve valid current method or default first method; mark stale if missing
  checkoutStore.ts:1901-1922
        ↓
store methods/status/selection and clear previous resolution
  checkoutStore.ts:1924-1936
        ↓
if valid selection exists, await setShippingMethod(nextShippingMethodId)
  checkoutStore.ts:1938-1940
        ↓
on error, classify and store shippingMethodsStatus/error
  checkoutStore.ts:1945-1957
```

Triggers:

| Trigger | File/function | Timing risk |
| --- | --- | --- |
| Checkout mount | `CheckoutPage.tsx:227-236` | Calls `fetchCart()` and `fetchShippingMethods()` without awaiting cart. Primary failure path. |
| Cart id/lastUpdated change | `CheckoutPage.tsx:254-260` | Can recover after cart exists, but may use stale session if cart id changed. |
| Begin-checkout analytics | `CheckoutPage.tsx:262-271` | Fire-and-forget `initCheckoutSession()` after cart is initialized and non-empty; not a preview gate. |
| ShippingStep mount | `ShippingStep.tsx:562-567` | Calls `fetchShippingMethods()` when list empty, also before guaranteed session/cart. |
| Retry button | `ShippingStep.tsx:762-774` | Manual retry can recover if cart now exists. |
| Final checkout | `checkoutStore.ts:2386-2395` | Awaits cart refresh then awaits shipping refresh before validation. |
| Shipping-related checkout failure | `checkoutStore.ts:2723-2734` | Refreshes shipping after backend stale/resolution errors. |

Important behavior:

| Question | Answer |
| --- | --- |
| What arguments does `fetchShippingMethods()` receive? | None. It reads state from stores. |
| Where does it obtain `checkoutSessionId`? | From `ensureCheckoutSessionInitialized()`, which reads existing `checkoutSessionId` or creates one from current cart. |
| Can `checkoutSessionId` be null at invocation? | Yes. Initial state is null and cart may also be null. |
| Does it await session creation? | Yes, but session creation returns `null` if cart is unavailable. |
| Does it call session creation itself? | Yes, indirectly through `ensureCheckoutSessionInitialized()`. |
| Can callers invoke concurrently? | Yes. ShippingStep mount, CheckoutPage mount, cart effect, retry, and submit can all call. Shipping preview abort/sequence guards handle stale preview responses. |
| Is there retry behavior? | No automatic retry inside `fetchShippingMethods()` for missing session. Later cart effect or manual retry may call again. |
| Does it use stale Zustand state? | It uses `get()` and `useCartStore.getState()` at call time, so not a closure-stale bug. The stale risk is retained session id across cart id changes. |
| Can first request race with initialization? | Yes. Mount preview can run before cart hydration and before analytics-created session. |

Observed error proof:

| Evidence | Meaning |
| --- | --- |
| `checkoutStore.ts:1881-1882` | Exact observed text exists only as a local Storefront error before preview POST. |
| `ShippingMethodDto.cs:138-140` | Backend absent-field message is `Checkout session is required.`, not the observed text. |
| `checkoutStore.ts:1947-1949` | Local error is classified as `{ status: 'ERROR', message: ... }`, matching observed log shape. |

## 5. Backend Preview Contract

Endpoint contract:

| Item | Current contract |
| --- | --- |
| Route | `POST /api/storefront/shipping-methods/preview` at `StorefrontShippingMethodsController.cs:136-144`. |
| Request body | `{ "checkoutSessionId": "GUID" }`, DTO at `ShippingMethodDto.cs:132-145`. |
| Required fields | `checkoutSessionId` must be non-empty GUID. |
| Tenant source | `ITenantContext.TenantId`, not raw browser result authority. `StorefrontTenantContextResolver.cs:24-33`. |
| Owner source | Authenticated customer claim or guest `X-Session-Id`. `StorefrontShippingMethodsController.cs:325-364`. |
| Cart source | Authoritative current cart loaded from `ICartService` after session validation. `WarehouseResolutionService.cs:120-139`. |
| Session source | Opaque server-generated checkout session ID supplied as `checkoutSessionId`. |
| Legacy preview support | No legacy request body on POST preview. Legacy GET `/api/storefront/shipping-methods` still exists and accepts optional `warehouseId`, but current checkoutStore does not use it. |

Validation order for preview:

| Order | Step | Evidence |
| --- | --- | --- |
| 1 | Trace/no-store setup. | `StorefrontShippingMethodsController.cs:146-147`. |
| 2 | Resolve tenant from trusted tenant context. | `:149-155`; resolver at `StorefrontTenantContextResolver.cs:11-22`. |
| 3 | Resolve owner from auth or `X-Session-Id`. | `StorefrontShippingMethodsController.cs:156-160`, `:325-364`. |
| 4 | Reject null request body. | `:162-165`. |
| 5 | Validate `checkoutSessionId` non-empty. | `:167-171`; DTO at `ShippingMethodDto.cs:136-144`. |
| 6 | Load owned active session and authoritative cart. | `StorefrontShippingMethodsController.cs:175-180`; `WarehouseResolutionService.cs:120-139`. |
| 7 | Filter shipping methods by configured fulfillment warehouses. | `StorefrontShippingMethodsController.cs:182-188`; `ShippingMethodService.cs:184-274`. |
| 8 | Return tenant-scoped preview response. | `StorefrontShippingMethodsController.cs:190-196`. |

Backend absent `checkoutSessionId` error:

```json
{
  "code": "CHECKOUT_SESSION_REQUIRED",
  "message": "Checkout session is required.",
  "traceId": "..."
}
```

This differs from the observed Storefront text, proving the observed error is local to Storefront lifecycle, not backend DTO validation.

## 6. Tenant/Cart/Session Authority

Authority chain:

| Authority | Current implementation |
| --- | --- |
| Tenant | `StorefrontTenantContextResolver.GetTenantId()` reads `ITenantContext.TenantId`; raw `X-Tenant-Id` is not used as result authority. |
| Cart | Backend loads current authoritative cart for tenant + owner through `ICartService`; browser does not supply cart id to preview. |
| CheckoutSession | `WarehouseResolutionService.LoadOwnedActiveSessionAsync()` requires tenant, session id, `Active` status, and `InvalidatedAtUtc == null`. |
| Customer/session ownership | Authenticated customer must match `OnlineCustomerId`; guest `X-Session-Id` must match `CheckoutSession.SessionId`. |
| Warehouse | Preview/select do not accept Storefront-supplied warehouse id. Selection derives warehouse from `ShippingMethod.FulfillmentWarehouseId`. |

Security assessment:

| Check | Result |
| --- | --- |
| Step 2A tenant spoofing reintroduced? | No. Preview controller validates via tenant context and response admission uses server `tenantId`. |
| Storefront-supplied `X-Tenant-Id` still incorrectly used as result authority? | No evidence in preview path. It remains a resolution hint/header, not admitted result authority. |
| Storefront-supplied `warehouseId` used as authority? | No on current checkout preview/select/final path. Architecture tests assert no `warehouseId:` in Storefront checkout store. |
| Can preview be called without valid CheckoutSession? | No. Backend rejects missing/stale/unowned session; Storefront also locally blocks if it cannot obtain one. |

## 7. Race/Concurrency Analysis

Primary race: P1.

```text
CheckoutPage mount effect
  fetchCart();              // starts async cart load, not awaited
  fetchShippingMethods();   // immediately attempts session ensure

fetchShippingMethods()
  ensureCheckoutSessionInitialized()
    cart = useCartStore.getState().cart
    if (!cart?.cartId) return null
  throw local preview-session-required error
```

Evidence:

| Evidence | Meaning |
| --- | --- |
| `CheckoutPage.tsx:229-236` | `fetchCart()` and `fetchShippingMethods()` are called in the same effect without ordering. |
| `checkoutStore.ts:323-327` | Session creation is impossible without a current cart id. |
| `checkoutStore.ts:1880-1883` | Null session becomes exact observed error. |

Concurrency properties:

| Pattern | Result |
| --- | --- |
| `Promise.all([createCheckoutSession(), fetchShippingMethods()])` | Not found. The effective behavior is unordered async calls in a single React effect. |
| `fetchShippingMethods()` before `await ensureCheckoutSession()` | No. It awaits ensure, but ensure can return null because cart is missing. |
| `setState({ checkoutSessionId })` then immediate stale read | Not primary. `ensureCheckoutSessionInitialized()` returns the session id directly after setting state. |
| Duplicate session creation | Mitigated by module-level `checkoutSessionInitPromise`; backend create/resume is cart-scoped. |
| Duplicate shipping preview | Possible, but preview has abort controller and sequence guard. |
| Duplicate shipping select | Possible P2. `setShippingMethod()` lacks request sequence/abort guard; overlapping selections can resolve out of order. |
| Cart id swap with retained session | P1 secondary. `ensureCheckoutSessionInitialized()` reuses existing id without checking cart id, causing backend `CHECKOUT_SESSION_STALE`. |

## 8. Cart Change Semantics

Traced behavior by scenario:

| Scenario | CheckoutSession | WarehouseResolution | Shipping preview | Evidence |
| --- | --- | --- | --- | --- |
| Cart item added | Preserved if same cart id; stale risk if cart id changes. | Cleared on shipping fetch; recreated/reused on select. | Refreshed by CheckoutPage cart effect. | `cartStore.ts:389-417`; `CheckoutPage.tsx:254-260`; `checkoutStore.ts:1869-1877`, `:1938-1940`. |
| Cart item removed | Preserved if same cart id; stale risk if cart id changes. | Cleared/refreshed through same effect. | Refreshed by CheckoutPage cart effect. | `cartStore.ts:561-595`; `CheckoutPage.tsx:254-260`. |
| Quantity changed | Preserved if same cart id; stale risk if cart id changes. | Cleared/refreshed through same effect. | Refreshed by CheckoutPage cart effect. | `cartStore.ts:468-514`; `CheckoutPage.tsx:254-260`. |
| Address changed | Preserved. | Preserved unless shipping refresh is independently triggered. | Not refreshed by address change; pricing refresh observes country/geography. | `checkoutStore.ts:1801-1812`; `CheckoutPage.tsx:238-252`. |
| Shipping method changed | Preserved. | Cleared, then `/select` creates/reuses resolution. | Not previewed; selection endpoint resolves. | `checkoutStore.ts:1825-1861`. |
| Checkout page reloaded | Not preserved. Recreated/resumed only after cart exists. | Not preserved. | Initial mount can race; later cart effect may recover. | `checkoutStore.ts:1782`, `:2939-2945`; `CheckoutPage.tsx:227-260`. |
| Customer changes shipping information | Preserved and autosaved. | Preserved unless method/cart changes trigger refresh. | Not refreshed from address alone. | `checkoutStore.ts:1801-1812`, `:425-451`. |

Important implementation distinction: current preview eligibility does not inspect address/geography. Therefore the absence of address-triggered shipping preview is not a current backend-contract mismatch, but it is a future risk if address zones become part of shipping eligibility.

## 9. Final Checkout Consistency

Current final checkout is aligned with Step 2G authority:

| Flow | Evidence |
| --- | --- |
| Storefront preview sends only `checkoutSessionId`. | `checkoutStore.ts:1885-1895`. |
| Storefront selection sends `checkoutSessionId` + `shippingMethodId`. | `checkoutStore.ts:1841-1850`. |
| Storefront stores `warehouseResolutionId` and `warehouseResolutionVersion` from selection. | `checkoutStore.ts:1851-1860`. |
| Storefront final checkout sends session, resolution id/version, and shipping method id. | `checkoutStore.ts:2591-2604`. |
| Backend validates submitted resolution before idempotency. | `CheckoutService.cs:864-874`. |
| Backend validates and locks resolution inside final transaction. | `CheckoutService.cs:6403-6413`. |
| Backend stamps order and order items with resolved warehouse. | `CheckoutService.cs:6451-6462`. |
| Backend reserves inventory from resolved warehouse. | `CheckoutService.cs:6467-6475`. |
| Backend marks resolution ordered after order creation. | `CheckoutService.cs:6588-6592`. |

Contract mismatch found: comments in `CheckoutDtos.cs:33-40` still say resolution/version are returned by shipping-method preview. Current Step 2G code returns them from `/shipping-methods/select`. This is documentation/comment drift, not runtime failure.

## 10. Manual Fulfillment Analysis

Manual fulfillment is not the cause of the observed preview failure.

| Requirement | Result | Evidence |
| --- | --- | --- |
| `carrierCode = manual` | Supported. Null/empty normalizes to `manual`. | `CarrierCodes.cs:59-70`. |
| CarrierAccount required? | No for manual. | Preview gate permits manual at `ShippingMethodService.cs:254-257`; selection returns warehouse before external checks at `WarehouseResolutionService.cs:815-819`. |
| CarrierWarehouseAssignment required? | No for manual. | Same evidence. |
| `ShippingMethod.FulfillmentWarehouseId` sufficient for fulfillment origin? | Yes, if warehouse is active and stock-capable. | `ShippingMethod.cs:91-97`; `WarehouseResolutionService.cs:791-819`. |
| Manual can pass preview when configured? | Yes, if session/cart exists, method is active, warehouse active, and stock gate passes. | `ShippingMethodService.cs:243-257`, `:531-612`. |
| Blocked by external carrier readiness? | No. External readiness is only required when `CarrierCodes.IsManual(carrierCode)` is false. | `ShippingMethodService.cs:254-257`; `WarehouseResolutionService.cs:815-859`. |
| Shipment behavior | Manual shipment status is `Pending`; external registration event only for carrier-managed shipments. | `ShipmentService.cs:166-233`. |

## 11. Shipping Method Eligibility Matrix

Global preview prerequisites before eligibility:

| Gate | Required for preview to execute? | Evidence |
| --- | --- | --- |
| Trusted tenant context | Yes | `StorefrontShippingMethodsController.cs:149-155`; `StorefrontTenantContextResolver.cs:24-33`. |
| Owner identity | Yes | `StorefrontShippingMethodsController.cs:325-364`. |
| Request body | Yes | `StorefrontShippingMethodsController.cs:162-165`. |
| Non-empty `checkoutSessionId` | Yes | `ShippingMethodDto.cs:136-144`. |
| Active owned checkout session | Yes | `WarehouseResolutionService.cs:864-918`. |
| Current cart matches session cart and owner | Yes | `WarehouseResolutionService.cs:920-935`. |

Eligibility matrix after preview can execute:

| Gate | Manual Fulfillment | Bosta | Generic external carrier | Evidence |
| --- | --- | --- | --- | --- |
| Method exists, same tenant, not deleted | Required | Required | Required | `ShippingMethodService.cs:192-208`. |
| IsActive | Required | Required | Required | `ShippingMethodService.cs:197-208`. |
| FulfillmentWarehouseId | Required | Required | Required | `ShippingMethodService.cs:210-215`, `:246-252`. |
| Warehouse active, same tenant, not deleted | Required | Required | Required | `ShippingMethodService.cs:217-226`. |
| Cart stock availability at configured warehouse | Required | Required | Required | `ShippingMethodService.cs:228-232`, `:531-612`. |
| Carrier configuration enabled | Not required | Required | Required | `ShippingMethodService.cs:254-257`, `:624-637`. |
| Carrier account active/ready | Not required | Required | Required | `ShippingMethodService.cs:638-652`; manual bypass at `WarehouseResolutionService.cs:815-819`. |
| CarrierWarehouseAssignment enabled/origin ready | Not required | Required | Required | `ShippingMethodService.cs:638-657`. |
| Service level | Present as method data; not a preview gate in traced code. | Present; not a preview gate. | Present; not a preview gate. | `ShippingMethod.cs:84-89`. |
| Currency/price | Tenant currency resolved and price calculated backend-side. | Same. | Same. | `ShippingMethodService.cs:239-263`, `:518-529`. |

Status distinction:

| Status/failure | Meaning |
| --- | --- |
| Local Storefront `ERROR` with `Checkout session is required before shipping methods can be previewed.` | Preview did not execute because Storefront could not obtain session. |
| Backend `CHECKOUT_SESSION_REQUIRED` | Preview request reached backend without non-empty `checkoutSessionId`. |
| Backend `CHECKOUT_SESSION_STALE` | Session was missing, inactive, unowned, or no longer matches cart. |
| Preview `EMPTY` | No configured methods or no active methods. |
| Preview `NOT_APPLICABLE` | Methods exist and are active, but none pass warehouse/stock/carrier gates. |
| Preview `AVAILABLE` | At least one method passed all gates. |

## 12. Root Cause

ROOT CAUSE:

The Storefront shipping preview lifecycle is not gated on cart hydration and CheckoutSession creation. `CheckoutPage` starts cart loading and shipping preview concurrently on mount. `fetchShippingMethods()` correctly requires a checkout session, but its session ensure helper returns `null` when the cart is not yet loaded. That produces the exact observed local Storefront error before the backend preview contract can be satisfied.

EVIDENCE:

| Evidence | Source |
| --- | --- |
| Unordered mount calls | `CheckoutPage.tsx:229-236`: `fetchCart(); fetchCheckoutFields(); fetchShippingMethods(); fetchPaymentMethods();`. |
| Session creation requires current cart | `checkoutStore.ts:323-327`: returns `null` when no `cart?.cartId`. |
| Exact observed error text | `checkoutStore.ts:1880-1883`. |
| Local classification shape | `checkoutStore.ts:1947-1949`. |
| Backend contract requires `checkoutSessionId` | `ShippingMethodDto.cs:132-145`; `StorefrontShippingMethodsController.cs:167-180`. |
| Backend missing-field text differs | `ShippingMethodDto.cs:138-140`: `Checkout session is required.` |
| Storefront integration partially aligned with Step 2G | `checkoutStore.ts:1885-1895` preview; `:1841-1850` select; `:2591-2604` final checkout. |

FAILURE SEQUENCE:

1. Customer enters checkout with cart not yet hydrated in `useCartStore`.
2. `CheckoutPage` mount effect calls `fetchCart()` and then immediately calls `fetchShippingMethods()` without awaiting the cart request.
3. `fetchShippingMethods()` calls `ensureCheckoutSessionInitialized()`.
4. `ensureCheckoutSessionInitialized()` sees no `cart.cartId` and returns `null` without POSTing `/api/storefront/checkout-session`.
5. `fetchShippingMethods()` throws `Checkout session is required before shipping methods can be previewed.` and stores `shippingMethodsStatus: 'ERROR'`.

WHY THE CURRENT ERROR APPEARS:

The error is the exact local Storefront string thrown when `ensureCheckoutSessionInitialized()` returns `null`. It is not the backend `CHECKOUT_SESSION_REQUIRED` response because the backend message differs and the local failure happens before the preview POST.

WHY THIS IS NOT A SHIPPING-METHOD CONFIGURATION FAILURE:

Shipping-method configuration gates run only after tenant, owner, request body, checkout session, and cart/session ownership validation. The observed local error occurs before preview is sent, so method configuration, manual fulfillment, Bosta readiness, active warehouses, and inventory gates are not evaluated in that failing invocation.

SECONDARY RISKS:

| Risk | Severity | Evidence |
| --- | --- | --- |
| Existing checkoutSessionId reused after cart id changes. | P1 | `ensureCheckoutSessionInitialized()` returns existing id without cart-id check at `checkoutStore.ts:312-317`; backend rejects cart mismatch at `WarehouseResolutionService.cs:927-935`. |
| ShippingStep can trigger preview independently before cart/session readiness. | P1 | `ShippingStep.tsx:562-567`. |
| `initCheckoutSession()` is fire-and-forget from analytics, not a lifecycle gate. | P2 | `CheckoutPage.tsx:262-271`; `checkoutStore.ts:2898-2905`. |
| `setShippingMethod()` lacks abort/sequence guard. | P2 | `checkoutStore.ts:1825-1861`. |
| Page reload does not persist session or resolution but may persist selection. | P2 | `checkoutStore.ts:2939-2945`. |
| Stale DTO comments say preview returns resolution. | P3 | `CheckoutDtos.cs:33-40`. |

## 13. Evidence

Source evidence table:

| Claim | File/line |
| --- | --- |
| Storefront defines preview/select/session endpoints. | `PointOfSale.Storefront/src/store/checkoutStore.ts:59-74`. |
| Storefront session response DTO expects `sessionId`. | `checkoutStore.ts:108-111`. |
| Storefront session creation POST body uses current cart. | `checkoutStore.ts:338-349`. |
| Storefront stores session id. | `checkoutStore.ts:358-367`. |
| Storefront preview posts `{ checkoutSessionId }`. | `checkoutStore.ts:1885-1895`. |
| Storefront selection posts `{ checkoutSessionId, shippingMethodId }`. | `checkoutStore.ts:1841-1850`. |
| Storefront final checkout includes session/resolution/version/method. | `checkoutStore.ts:2591-2604`. |
| CheckoutSession backend response has `SessionId`. | `PointOfSale.API/DTOs/Checkout/CheckoutSessionDtos.cs:67-75`. |
| CheckoutSession controller returns `SessionId`. | `PointOfSale.API/Controllers/CheckoutSessionController.cs:193-202`. |
| Preview request DTO requires `CheckoutSessionId`. | `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs:132-145`. |
| Preview controller validates DTO and loads session/cart. | `StorefrontShippingMethodsController.cs:167-180`. |
| Session ownership validation. | `WarehouseResolutionService.cs:864-918`. |
| Cart/session match validation. | `WarehouseResolutionService.cs:920-935`. |
| Preview eligibility gates. | `ShippingMethodService.cs:184-274`, `:531-658`. |
| Manual carrier bypass. | `CarrierCodes.cs:59-70`; `ShippingMethodService.cs:254-257`; `WarehouseResolutionService.cs:815-819`. |
| Backend final checkout resolution validation. | `CheckoutService.cs:864-874`, `:6403-6413`. |
| Backend order/stock propagation. | `CheckoutService.cs:6451-6475`, `:6588-6592`. |

## 14. Minimal Fix Design

Do not implement in this audit.

Smallest safe fix design:

```text
Checkout lifecycle
        ↓
Await cart hydration or require existing cart
        ↓
Ensure CheckoutSession for that cart
        ↓
Store session id and upload token
        ↓
Fetch shipping preview with checkoutSessionId
        ↓
Select method to create WarehouseResolution
```

Likely files to change:

| File | Function/change area |
| --- | --- |
| `PointOfSale.Storefront/src/pages/CheckoutPage.tsx` | Mount effect should not call `fetchShippingMethods()` until cart is available and/or session ensure succeeds. |
| `PointOfSale.Storefront/src/store/checkoutStore.ts` | `fetchShippingMethods()` or `ensureCheckoutSessionInitialized()` should distinguish `cart not ready` from terminal failure; session retention should be cart-aware. |
| `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx` | Optional guard to avoid triggering shipping preview before cart/session readiness. |
| `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts` or a new Storefront store/page test | Add regression coverage for session ensure before preview. |

Contract changes:

| Area | Required? |
| --- | --- |
| Backend preview contract | No. Keep requiring `checkoutSessionId`. |
| DTOs | No runtime contract change required. Optional comment cleanup only. |
| Database/migrations | No. |
| Frontend API payloads | No. Continue sending only `checkoutSessionId` for preview and method-only selection. |

Recommended implementation shape:

| Item | Design |
| --- | --- |
| Initial checkout load | Await cart load before first shipping preview, or remove initial unconditional preview and rely on `cart?.cartId` effect. |
| Session ensure | If cart is loading/uninitialized, return a non-error pending/idle state instead of storing shipping `ERROR`. |
| Cart id consistency | Track the cart id associated with `checkoutSessionId` in memory and recreate/resume session when cart id changes. |
| ShippingStep | Do not call `fetchShippingMethods()` until cart has a non-empty id. |
| Security | Do not accept `warehouseId` from browser, do not weaken backend session validation, do not restore raw `X-Tenant-Id` authority. |

Required regression tests:

| Test | Expected assertion |
| --- | --- |
| Storefront store: `fetchShippingMethods()` with no cart loaded. | Does not call preview with null session and does not surface terminal shipping configuration error. |
| Storefront checkout lifecycle: checkout mount with delayed cart fetch. | Cart fetch completes, checkout session is created, then preview is called with `checkoutSessionId`. |
| Storefront cart id swap. | Existing session for old cart is not reused for new cart; preview uses a session for current cart. |
| Backend preview missing `checkoutSessionId`. | Returns `400 CHECKOUT_SESSION_REQUIRED` with message `Checkout session is required.` |
| Backend/Storefront happy path. | Create/resume session -> preview -> select -> final checkout submits resolution id/version. |

Migration impact: none.

Recommended step classification: Step 2H Implementation, narrowly scoped to Storefront checkout lifecycle/session gating and regression tests. Backend changes are not necessary for the primary fix.

## 15. Test Coverage Gaps

Existing coverage:

| Area | Current tests |
| --- | --- |
| Storefront shipping admission | `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts:375-597`. |
| Storefront tests seed session id manually | `commerceAuthority.certification.test.ts:175-187`. |
| WarehouseResolution behavior | `PointOfSale.Tests/Storefront/WarehouseResolutionServiceTests.cs:25-196`. |
| Preview/select authority architecture | `PointOfSale.Tests/Architecture/ShopixyWarehouseResolutionAuthorityCertificationTests.cs:46-97`. |
| Fulfillment warehouse authority | `PointOfSale.Tests/Architecture/ShopixyShippingMethodFulfillmentWarehouseAuthorityTests.cs:22-34`. |
| Shipping eligibility authority | `PointOfSale.Tests/Architecture/ShippingMethodEligibilityAuthorityCertificationTests.cs:5-79`. |
| Final checkout authority | `PointOfSale.Tests/Architecture/StorefrontShippingMethodAuthorityCertificationTests.cs:111-150`. |

Missing coverage:

| Missing regression | Why it matters |
| --- | --- |
| `fetchShippingMethods()` cannot execute preview before CheckoutSession exists. | Current tests seed `checkoutSessionId`, hiding the lifecycle failure. |
| Storefront automatically ensures a valid CheckoutSession before requesting shipping methods. | Current page/store tests do not simulate delayed cart hydration. |
| Storefront cart-id swap invalidates/recreates session before shipping preview. | Current code reuses existing session without cart-id check. |
| Backend preview missing `checkoutSessionId` exact 400. | DTO source shows behavior; no direct test found. |

## 16. Migration Impact

No migration is required for the minimal safe fix.

Current Step 2G migration is additive and tenant-safe for `ShippingMethod.FulfillmentWarehouseId`; tests assert no backfill in `ShopixyShippingMethodFulfillmentWarehouseAuthorityTests.cs:36-51`. The Step 2H issue is lifecycle/order-of-operations in Storefront, not schema.

## 17. Security Assessment

Security posture remains sound on the backend contract:

| Security property | Assessment |
| --- | --- |
| Tenant spoofing | Not reintroduced. Backend uses `ITenantContext`; Storefront admits server `tenantId` evidence. |
| Cart spoofing | Preview does not accept cart id; backend loads authoritative cart by tenant + owner and compares it to session cart. |
| Session spoofing | Preview requires opaque active checkout session id and validates owner. |
| Warehouse spoofing | Preview/select do not accept browser warehouse id. |
| Manual fulfillment external readiness | Manual bypasses CarrierAccount/CarrierWarehouseAssignment; still requires active configured warehouse and stock. |

Security warning for implementation: do not fix the Storefront lifecycle by weakening backend validation, accepting arbitrary `warehouseId`, trusting raw `X-Tenant-Id`, or making preview work without a valid checkout session.

## 18. Risk Classification

| Risk | Classification | Rationale |
| --- | --- | --- |
| Initial checkout shipping preview before cart/session | P1 | Blocks shipping options on affected checkout entries and produces observed error. |
| Cart id changes while session id retained | P1 | Can make later preview/final checkout fail with stale session. |
| Duplicate shipping previews | P2 | Abort/sequence guards reduce state corruption risk. |
| Duplicate shipping selections | P2 | No select abort/sequence guard; out-of-order resolution state possible. |
| Backend tenant/session/warehouse authority | No issue | Strong validation present. |
| Manual fulfillment carrier readiness | No issue | Manual path correctly bypasses external carrier requirements. |
| Shipping method configuration | Not primary | Only relevant after preview contract is satisfied. |

## 19. Recommended Step 2H Implementation Plan

Recommended narrow Step 2H implementation:

1. Make checkout mount sequence cart-aware: do not call shipping preview until `cart?.cartId` exists or `fetchCart()` has completed successfully.
2. Make `fetchShippingMethods()` treat missing cart/session prerequisites as not-ready instead of terminal shipping-method load failure.
3. Associate in-memory `checkoutSessionId` with the cart id it was created for; recreate/resume when current cart id differs.
4. Keep backend preview/select/final checkout contracts unchanged.
5. Add Storefront regression tests for delayed cart hydration, session ensure-before-preview, and cart-id swap.
6. Add backend test for missing preview `checkoutSessionId` returning `CHECKOUT_SESSION_REQUIRED`.
7. Optionally clean stale DTO comments that still say preview returns resolution metadata.

This is Step 2H Implementation, not a backend contract rollback.

## 20. Final Certification

Certification: BLOCKED

Reason: the current Storefront shipping flow has a source-proven path where shipping preview is invoked before cart hydration allows CheckoutSession creation, so the flow cannot reliably satisfy the backend preview contract.

Backend responsibility: the backend contract is correct and server-authoritative. No backend change is necessary for the primary root cause.

Storefront responsibility: Storefront checkout lifecycle must ensure a valid checkout session for the current cart before requesting shipping preview.

Readiness for Step 2H Implementation: yes, with the narrow Storefront lifecycle fix and regression tests described above.
