# SIMPLE-03R POS Admin Storefront Guest Session Regression Report

## Scope

This investigation was limited to the POS Admin Storefront runtime path:

POS Admin Storefront -> Guest Customer -> Storefront Cart -> Storefront Cart API -> Redis-backed Guest Cart.

CommerceHub cart/storefront, Web Admin, Builder preview, POS Admin backoffice, and other storefront implementations were not used as root-cause inputs.

## Root Cause

The guest cart identity changed because the storefront Axios 401 interceptor did not classify `/api/storefront/auth/me` as an auth endpoint.

Source lineage:

- `initializeAuth()` runs after storefront bootstrap and calls `authStore.checkAuth()` (`PointOfSale.Storefront/src/auth/bootstrap/authBootstrap.ts:62-65`).
- `checkAuth()` calls `authApi.getMe()` (`PointOfSale.Storefront/src/auth/store/authStore.ts:305-307`).
- `getMe()` requests `/api/storefront/auth/me` (`PointOfSale.Storefront/src/auth/api/authApi.ts:89-93`).
- Before the fix, `isAuthEndpoint()` only matched login/register/refresh/logout, so `/auth/me` was treated as a normal protected API request.
- A guest `/auth/me` 401 entered the global response interceptor. On failed refresh, the interceptor called `triggerForcedLogout()` (`PointOfSale.Storefront/src/services/api/httpClient.ts:281-309`).
- `triggerForcedLogout()` invokes the configured `forceLogout()` callback (`PointOfSale.Storefront/src/services/api/httpClient.ts:112-123`).
- That callback called `authStore.logout()` (`PointOfSale.Storefront/src/auth/bootstrap/authBootstrap.ts:55-58`).
- `logout()` calls `clearSession()` (`PointOfSale.Storefront/src/auth/store/authStore.ts:259-266`).
- `clearSession()` removes the tenant-scoped `storefront_session_id:{tenantId}` localStorage entry and memory fallback (`PointOfSale.Storefront/src/bootstrap/sessionManager.ts:179-193`).

After the session is cleared, the next cart request calls `getSessionId()`, finds no tenant-scoped persisted value, and generates a new UUID (`PointOfSale.Storefront/src/bootstrap/sessionManager.ts:67-130`). That produces a different `X-Session-Id`, so the backend resolves a different guest cart owner.

## Runtime Evidence

Provided runtime evidence showed:

- POST `/api/storefront/cart/items` returned `ownerType: "session"`, `ownerId: "36234d57-b6fa-4554-9b93-7fd788119ba3"`, and cart `c83360be-d365-47e6-83fb-e3ce45fb7ebe`.
- Immediate GET `/api/storefront/cart` returned `ownerType: "session"`, `ownerId: "7e96c90b-2558-434a-b0bd-4be7656ba96f"`, and cart `0e8ca853-b713-4811-8738-bdaa4f656688`.
- The backend derives session `OwnerId` directly from `X-Session-Id`, so different returned `OwnerId` values prove different request session headers.

## Exact Session Lineage

Guest session creation:

- Bootstrap resolves tenant first (`PointOfSale.Storefront/src/bootstrap/bootstrap.ts:131-143`).
- Bootstrap initializes the guest session after tenant resolution (`PointOfSale.Storefront/src/bootstrap/bootstrap.ts:155-162`).
- `sessionManager` generates UUID v4 values via `crypto.randomUUID()` or a UUID fallback (`PointOfSale.Storefront/src/bootstrap/sessionManager.ts:18-30`).

Persistence:

- Guest cart session is stored in tenant-scoped localStorage under `storefront_session_id:{tenantId}` (`PointOfSale.Storefront/src/bootstrap/sessionManager.ts:13`, `PointOfSale.Storefront/src/bootstrap/tenantScopedStorage.ts:7-18`).
- It also keeps an in-memory fallback map by the same tenant-scoped key (`PointOfSale.Storefront/src/bootstrap/sessionManager.ts:32-35`, `105-130`).
- It is not stored as a cart cookie, sessionStorage, IndexedDB, or HTTP-only cookie.
- Auth refresh/access tokens are cookie based, but those are customer-auth cookies, not guest cart identity (`PointOfSale.Storefront/src/auth/api/authApi.ts:8-12`).

Outgoing storefront cart requests:

- `getRequestHeaders()` supplies `X-Tenant-Id` and `X-Session-Id` from tenant resolver and session manager (`PointOfSale.Storefront/src/bootstrap/requestContext.ts:67-88`).
- The Axios request interceptor attaches those headers to every API call (`PointOfSale.Storefront/src/services/api/httpClient.ts:200-231`).
- `fetchCart()` uses GET `/api/storefront/cart` through the shared `httpClient` (`PointOfSale.Storefront/src/services/api/storefrontApi.ts:203-205`).
- `addCartItem()` uses POST `/api/storefront/cart/items` through the same shared `httpClient` (`PointOfSale.Storefront/src/services/api/storefrontApi.ts:231-233`).
- `cartStore.addItem()` is the shared add pipeline used by Product Details and listing quick add (`PointOfSale.Storefront/src/store/cartStore.ts:389-417`).
- Product Details product hero calls `addItem(product.id, 1, variantId)` (`PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx:357-385`).
- Listing quick add calls `addItem(product.id, 1)` (`PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx:105-128`).
- Cart page fetches via `cartStore.fetchCart()` on mount when not initialized (`PointOfSale.Storefront/src/pages/CartPage.tsx:100-105`).

Backend owner resolution:

- GET `/api/storefront/cart` and POST `/api/storefront/cart/items` both call `ValidateHeadersAsync()` (`PointOfSale.API/Controllers/StorefrontCartController.cs:104-125`, `181-230`).
- `ValidateHeadersAsync()` reads `X-Session-Id` from request headers (`PointOfSale.API/Controllers/StorefrontCartController.cs:1829-1832`).
- If no authenticated customer resolves, it creates `CartOwner { OwnerType = "session", OwnerId = sessionId }` (`PointOfSale.API/Controllers/StorefrontCartController.cs:1865-1883`).
- Therefore POST and GET can only produce different guest `OwnerId` values when their request `X-Session-Id` values differ.

Analytics:

- Analytics builds events with `getAnalyticsSessionId()`, which delegates to the bootstrap session manager (`PointOfSale.Storefront/src/analytics/AnalyticsSession.ts:84-99`).
- Analytics sending separately calls `getRequestHeaders()` and rejects if event session and request session differ (`PointOfSale.Storefront/src/analytics/AnalyticsClient.ts:25-32`, `47-60`).
- The logged analytics mismatch is the same underlying bug: a session was cleared between event construction and event send, so the event actor session and request header session diverged. It is not a separate analytics storage bug.

## Fix Applied

Changed `isAuthEndpoint()` to classify the storefront auth namespace as auth-owned:

- `PointOfSale.Storefront/src/services/api/httpClient.ts:87-89`

This keeps `/api/storefront/auth/me` and `/api/storefront/auth/refresh` inside auth-store handling. For anonymous guests, `checkAuth()` and `refreshAuth()` can now settle to unauthenticated state without invoking `authStore.logout()` and without clearing the tenant-scoped guest cart session.

## Files Modified

- `PointOfSale.Storefront/src/services/api/httpClient.ts`
- `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts`
- `SIMPLE-03R_POS_ADMIN_STOREFRONT_GUEST_SESSION_REGRESSION_REPORT.md`

## Regression Evidence

Passed:

- `npm test -- --run src/commerceAuthority.certification.test.ts src/store/cartStore.persistence.test.ts`
- Result: 2 test files passed, 11 tests passed.
- `npm run build`
- Result: TypeScript and Vite production build completed successfully.

New regression coverage:

- `commerceAuthority.certification.test.ts:184-208` proves POST `/api/storefront/cart/items` and GET `/api/storefront/cart` receive the same `X-Tenant-Id`, `X-Session-Id`, `CartId`, and `OwnerId`.
- `commerceAuthority.certification.test.ts:210-214` proves `/api/storefront/auth/me` and `/api/storefront/auth/refresh` are auth endpoints, while `/api/storefront/cart` is not.

Existing focused cart coverage exercised in the same run:

- Product Details Add To Cart and immediate Cart page visibility: `PointOfSale.Storefront/src/store/cartStore.persistence.test.ts:112-128`.
- Variant add pipeline: `PointOfSale.Storefront/src/store/cartStore.persistence.test.ts:130-149`.
- Listing quick add pipeline: `PointOfSale.Storefront/src/store/cartStore.persistence.test.ts:151-164`.
- Browser refresh/direct Cart URL persisted fetch: `PointOfSale.Storefront/src/store/cartStore.persistence.test.ts:166-181`.

Backend verification observation:

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~StorefrontCartCouponRevalidationTests"` did not reach cart identity assertions. It failed during SQLite schema creation with `SQLite Error 1: 'no such function: btrim'`, an existing PostgreSQL-specific schema/test-environment issue unrelated to this frontend session fix.

Runtime observation:

- I did not run a live browser plus Redis-backed backend runtime in this workspace. The source proof and focused frontend regression verify that the two cart requests now construct with the same canonical `X-Session-Id`; live runtime should still be checked against actual network responses for matching returned `CartId` and `OwnerId`.

## Final Certification

SIMPLE-03R PASSED WITH OBSERVATIONS
