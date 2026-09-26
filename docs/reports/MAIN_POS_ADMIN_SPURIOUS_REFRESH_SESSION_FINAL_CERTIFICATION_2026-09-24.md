# MAIN POS Admin / Super Admin Spurious Refresh and Session Restore Final Certification

Date: 2026-09-24

## 1. Exact Root Cause

The reported event was primarily a React protected-route unmount, not a browser document reload.

The first causal chain in POS Admin was:

1. Returning from another tab/application emitted `focus` and/or visible `visibilitychange`.
2. `AuthBootstrap` started `/api/auth/session` revalidation with route presentation withheld.
3. `beginRouteAuthorityValidation()` set `hasHydrated` to `false` before the request completed.
4. `ProtectedRoute` replaced its `<Outlet />` with `Restoring session...`.
5. Every protected page and form unmounted. Local React state, uncontrolled DOM state, and open modals were destroyed.
6. The route remounted after the session response, giving the appearance of a page refresh.

Super Admin independently implemented the same behavior with `isRouteAuthorityValidating`: lifecycle revalidation replaced its `<Outlet />` with `Checking secure Super Admin session...`.

A second root cause was proven in live Chrome after removing the route unmount. Successful background hydration wrote a new `user` object even when backend authority was unchanged. Screens such as `StorefrontSettings`, whose load effect depends on `user`, re-fetched, rendered a loading branch, remounted controls, and lost the candidate draft. Identity-preserving hydration fixed this separate state-reset path.

Contributing auth defects increased severity:

- Any background session error, including network/timeout/5xx, was converted to anonymous state.
- A POS `/auth/session` `401` did not attempt the still-valid refresh-cookie path.
- Super Admin focus plus visibility could start parallel session checks.
- Cross-tab refresh-token rotation was coordinated only within one JavaScript document.
- Super Admin treated ordinary resource `403` responses as session loss.
- Auth failure used `window.location.href`/`window.location.assign`, creating a genuine hard navigation after interceptor failure.

## 2. Evidence Proving It

Pre-fix source and regression behavior proved that lifecycle revalidation synchronously changed the route guard's presentation state before any network result. The previous BFCache test explicitly expected protected content to disappear and `Restoring session...` to replace it.

Post-fix authenticated Chrome 153 evidence recorded, for each scenario:

- `performance.getEntriesByType('navigation')[0].type === "navigate"` from the original page load.
- A stable disposable document UUID across the lifecycle cycle.
- An unchanged `performance.timeOrigin`.
- A stable identity attached to the actual input DOM node.
- Preserved controlled input values.
- No `Restoring session...` or Super Admin checking screen.
- No `beforeunload`, `pagehide`, `pageshow`, or main-frame navigation during the cycle.

The first post-fix browser pass exposed the separate Storefront Settings object-identity reset: document identity stayed stable, but textarea DOM identity and value changed. After identity-preserving auth hydration, the same browser scenario passed with both identities and the draft preserved.

## 3. Why Tab Switching Triggered It

`PointOfSale.Web/src/components/AuthBootstrap.jsx` and `PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx` subscribe to `focus`, visible `visibilitychange`, `online`, `popstate`, and persisted `pageshow`. A normal tab return commonly emits both visible `visibilitychange` and `focus` close together. Those events were incorrectly modeled as initial secure bootstrap instead of non-destructive background revalidation.

The fixed handlers still revalidate current backend authority, but they keep the established protected route mounted and deduplicate concurrent checks.

## 4. Why Snipping Tool Could Trigger It

Opening Windows Snipping Tool moves OS focus away from Chrome; returning emits window focus and can be accompanied by a visibility transition. The old global focus handler entered the same destructive route-validation path. No Snipping Tool-specific code exists.

The automated browser acceptance drove the exact `blur -> visibilitychange -> focus` listener sequence. Native Snipping Tool UI was not launched by the headless harness; this is recorded as a limitation, not represented as native automation.

## 5. Why “Restoring Session...” Appeared

`PointOfSale.Web/src/components/ProtectedRoute.jsx` renders `Restoring session...` whenever `hasHydrated` is false. The old lifecycle path deliberately set that initial-bootstrap flag false on every focus/visibility check. A session request can run for up to the configured 30-second timeout, so the whole route could be absent for that duration. Duplicate lifecycle events and transient network conditions made it feel intermittent and long-running.

The restore screen now applies only to initial document/session bootstrap. Normal live session checks completed without showing it; captured local responses were sub-second to approximately 1.4 seconds under the running development environment.

## 6. Root Architectural Fix

- Separated initial blocking bootstrap from background revalidation.
- Kept protected `<Outlet />` trees mounted during focus, visibility, BFCache, online, popstate, and interval checks.
- Retained the last proven session on transport, timeout, and server failures.
- Failed closed only for explicit `401`/session-governance rejection or malformed authority.
- Restored POS and Super Admin sessions through refresh when access authority is expired but refresh authority remains valid.
- Added per-document single-flight refresh and named Web Locks for cross-tab refresh serialization.
- Added a one-time POS session reconciliation for browsers without Web Locks after a refresh rotation race; no retry loop was introduced.
- Preserved auth-store user object identity when backend authority is structurally unchanged.
- Kept authority updates active when identity, permissions, lifecycle authority, or Super Admin capabilities actually change.
- Replaced auth-client hard navigation with auth-store invalidation and existing React Router guards.
- Stopped converting ordinary Super Admin `403` resource denials into logout.
- Limited Super Admin storage logout propagation to the explicit logout key instead of obsolete auth-storage activity.
- Did not persist any form draft, API key, secret key, access token, or refresh token in browser storage.

## 7. Files Changed

Runtime:

- `PointOfSale.Web/src/auth/crossTabRefreshLock.ts`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/services/api.js`
- `PointOfSale.Web/src/store/authStore.js`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx`
- `PointOfSale.Web/src/super-admin/auth/superAdminAuthStore.ts`
- `PointOfSale.Web/src/super-admin/auth/superAdminSessionCoordinator.ts`
- `PointOfSale.Web/src/super-admin/auth/useSuperAdminSessionBootstrap.ts`
- `PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts`

Regression tests:

- `PointOfSale.Web/src/auth/crossTabRefreshLock.test.ts`
- `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`
- `PointOfSale.Web/src/pages/PaymentMethods.test.tsx`
- `PointOfSale.Web/src/services/api.authReplay.test.ts`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.test.tsx`
- `PointOfSale.Web/src/super-admin/auth/superAdminSessionCoordinator.test.ts`
- `PointOfSale.Web/src/super-admin/core/superAdminApiClient.test.ts`

Certification:

- `docs/reports/MAIN_POS_ADMIN_SPURIOUS_REFRESH_SESSION_FINAL_CERTIFICATION_2026-09-24.md`

## 8. Auth / Session Impact

POS Admin authority remains server-authoritative through HTTP-only session cookies and `/api/auth/session`. Legacy `localStorage` access/refresh tokens continue to be removed. The explicit `pos_vendor_session_logout_at` key is only a terminal cross-tab signal.

Super Admin credentials remain HTTP-only cookies. `localStorage` contains only non-secret expiry/token-version coordination metadata and the explicit logout signal. Refresh-token rotation remains enforced by the backend.

The request flow is now:

`API request -> 401 -> one coordinated refresh -> authority session read -> idempotent request replay`

Security mutations are still not automatically replayed. Refresh/session authority rejection clears client authority and lets the protected route navigate to login without a document reload. Temporary network failure rejects the operation but does not destroy a previously proven session.

There is no infinite retry path: normal GET retries remain capped at two, request auth replay remains guarded by `_retry`, and rotation-race reconciliation is one session read.

## 9. Form-State Impact

Affected forms use ordinary local React state and controlled/uncontrolled inputs. This is appropriate while the user remains on the route; the defect was the global auth layer unmounting or indirectly reinitializing them.

The fix preserves route component and DOM identity. No secret draft storage was added. Explicit user navigation, logout, actor/authority replacement, or a real browser discard/reload can still remove unsaved state by design.

## 10. POS Admin Results

Passed in authenticated Chrome and deterministic tests:

- Focus return does not replace the protected route.
- Hidden visibility does no session work; visible transition performs one revalidation.
- Simultaneous focus/visibility signals are deduplicated.
- A temporary background network failure retains the proven user and form.
- A lifecycle `401` uses refresh authority.
- Definitive refresh rejection fails closed.
- BFCache `pageshow.persisted` revalidates without unmounting.
- Product form draft survives the lifecycle sequence.
- Storefront Settings candidate survives without control remount after identity-preserving hydration.

## 11. Super Admin Results

Passed in authenticated Chrome and deterministic tests:

- Protected content remains mounted during background checks.
- Focus/visibility checks are single-flight.
- Temporary network failure retains the proven session.
- Explicit `401` authority loss redirects through React Router state.
- `403` resource denial no longer logs out the platform actor.
- Unrelated/obsolete storage activity is ignored.
- Explicit cross-tab logout remains terminal.
- Super Admin refresh and session authority use a shared coordinator and cross-tab Web Lock.

## 12. Payment-Method Form Results

The exact POS Paymob sequence passed:

1. Open configuration.
2. Enter an API-key draft.
3. Switch to an external page and place a secret value on the clipboard.
4. Return and paste the secret.
5. Execute another `blur -> visibilitychange -> focus` cycle.
6. Verify API key, secret key, modal, input DOM identity, document identity, and route presentation remain unchanged.

The deterministic integration test also verifies both masked fields across focus plus visibility while session revalidation is in flight. Stripe/other provider controls share the same mounted Payment Methods component and root guard; provider secrets were not persisted.

## 13. Tests

- Final full frontend suite with one worker and a 30-second timeout: 195 files and 1,457 tests passed.
- An earlier heavily parallel final run produced three timing-only failures in unrelated product/affiliate tests. Each affected file passed in isolation (21, 132, and 2 tests respectively), and the complete constrained-worker rerun passed all 1,457 tests.
- Focused final suite: 37 tests passed across auth bootstrap, payment configuration, POS replay, Web Locks, Super Admin route/session coordination, and `403` policy.
- Final `AuthBootstrap` suite: 16 tests passed, including hidden-to-visible behavior.
- Relevant backend auth/session suite: 30 tests passed for POS authentication lifecycle, Super Admin auth service, and Super Admin session-security architecture.
- Architecture timeout checks: all 15 passed when run with a 30-second test timeout. The repository's default 5-second timeout had produced two timing-only failures during a heavily parallel full run.

Required regression coverage is present for tab visibility, focus, valid refresh, refresh failure, concurrent refresh, one-time `401` retry, multi-tab storage, initial restoration, generic protected form preservation, Paymob API/secret preservation, Super Admin preservation, and authorization-denial behavior.

## 14. Builds

- `PointOfSale.Web` production Vite build passed: 5,233 modules transformed.
- POS Admin and Super Admin are routes in the same `PointOfSale.Web` bundle; there is no separate Super Admin build artifact.
- Build emitted the existing large-chunk warning for the approximately 5.3 MB main chunk; it did not fail.
- `git diff --check` passed. Output contained only Git's existing LF-to-CRLF working-copy notices.
- `npm run lint` could not run because the repository uses ESLint 9 while only `.eslintrc.cjs` exists; ESLint 9 requires `eslint.config.*`. This pre-existing tooling configuration was not changed as part of the session fix.

## 15. Browser Evidence

Environment:

- Chrome `153.0.8010.53`
- Live `PointOfSale.Web` at `http://localhost:5173`
- Live API at `http://localhost:5010`
- Real local POS Admin and Super Admin authentication
- Disposable browser harness removed after execution
- Credentials were not written to repository files, browser evidence, or this report

Final results:

| Scenario | Document reload | Input remount | Draft preserved | Restore UI | Lifecycle session check |
|---|---:|---:|---:|---:|---:|
| POS Paymob API + secret | No | No | Yes | No | One request/response |
| POS product form | No | No | Yes | No | One request/response |
| POS storefront settings | No | No | Yes | No | One request/response |
| Super Admin payment form | No | No | Yes | No | Requests completed without route replacement |

Representative payment timeline in the final run:

- T0 `blur` at `performance.now() ~= 4627.1 ms`.
- T1 visible `visibilitychange` at `~= 5358.8 ms`.
- T2 `focus` at `~= 5359.0 ms`.
- T3 one POS session GET was initiated by the deduplicated lifecycle handler.
- T4 the session response completed successfully without presenting bootstrap UI.
- T5 document UUID, `performance.timeOrigin`, input DOM identity, API-key value, and secret-key value remained unchanged.

No hard navigation, unload, pagehide, pageshow, React route replacement, auth reset, or form loss occurred.

## 16. Explicit Navigation, Lifecycle, Service Worker, and Remaining Limitations

Explicit navigation audit after the fix:

- `AppErrorBoundary.jsx` and `MarketplaceErrorBoundary.tsx`: reload only after an explicit user click on an error-recovery button.
- `BarcodeDisplay.jsx`: opens a user-requested print window.
- Product feed, storefront preview, CMS/theme preview, payment proof, and landing-page code: explicit `window.open` actions only.
- Affiliate provider/auth files: intentional hard transition into the separate affiliate login/runtime.
- `ProductAffiliateSettingsCard.jsx`: `history.pushState` supports its explicit unsaved-change guard and does not reload.
- POS and Super Admin auth clients: no remaining `location.href`, `location.assign`, `location.replace`, `location.reload`, `navigate(0)`, router reload, or hard-navigation call.

Lifecycle-handler audit:

- Root POS and Super Admin handlers now perform deduplicated background session checks only.
- Affiliate readiness/detail/list handlers re-fetch domain readiness only.
- Marketplace authority uses focus/visibility/BroadcastChannel for marketplace query authority only.
- Storefront builder/domain handlers synchronize preview/read state only.
- Product, variant, CMS, homepage, and authoring `beforeunload` handlers protect dirty drafts; they do not cause navigation.
- POS cart/runtime storage handlers update scoped caches only.

Service-worker audit:

- No service-worker file, registration, Workbox dependency, Vite PWA plugin, `skipWaiting`, or `controllerchange` reload path exists in `PointOfSale.Web`.
- Vite HMR is development-server behavior only and is absent from the production bundle bootstrap.

Remaining limitations:

- Native Windows Snipping Tool was not controlled from the headless Chrome harness. Its relevant browser event sequence was driven directly in real Chrome and passed.
- Chromium headless tab activation does not consistently emit OS-native visibility events, so the final run explicitly drove production `blur`, `visibilitychange`, and `focus` listeners after opening an external page. Deterministic jsdom tests separately cover hidden-to-visible state.
- Browser memory-pressure tab discard was not forced. A genuine browser discard would be an environment document reload and remains distinguishable by a new document UUID/time origin and navigation entry.
- The API `/health` endpoint returned `503` because at least one configured dependency was degraded, while both auth stacks and tested application endpoints remained operational.

## 17. FINAL VERDICT

**CERTIFIED FOR THE PROVEN FAILURE PATHS.**

- **Browser/document reload:** Not the first causal event in the reported `Restoring session...` behavior. Post-fix live browser evidence proves no document reload. Old auth-interceptor hard navigations were separate possible reload paths and have been removed.
- **React remount:** Yes, this was the primary pre-fix form-loss mechanism. Protected outlets were conditionally removed during lifecycle session checks. A second control remount was caused by unnecessary unchanged-user object replacement.
- **Auth/session reset:** Yes, transient failures could unnecessarily clear auth, and expired access authority was not restored correctly from refresh authority in lifecycle bootstrap. Both are fixed while definitive authority loss still fails closed.
- **Browser lifecycle event:** Yes, focus/visible lifecycle events were the trigger, not the root defect. They now initiate non-destructive, deduplicated validation.
- **Browser/environment behavior:** Browser discard was not reproduced and service-worker behavior is excluded. Native OS focus changes can emit the trigger events, but no longer cause route replacement or form loss.

Final state: no unexpected app-controlled refresh, no unnecessary session restoration screen during background lifecycle checks, no form-state loss in the verified POS Admin/Super Admin scenarios, and no weakening of authentication, tenant, governance, CSRF, refresh rotation, or secret-storage controls.
