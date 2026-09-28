# MAIN POS Admin and Storefront Authentication Final Certification

Date: 2026-09-28

## Verdict

**PASS for the POS Admin and AhramStore Storefront authentication, registration, login, logout, session-isolation, stale-session, localization, and mobile scope covered by this certification.**

The identified authentication defects were remediated and verified through focused automated tests, production builds, and headed Edge workflows against the MAIN Development runtime. The disposable Storefront customer created for certification was deactivated through the supported POS Admin security workflow after testing.

This verdict does not certify the aggregate platform health endpoint. `/health` remained `503` because `payout_external_truth_readiness` reported an unrelated reconciliation mismatch. Core dependencies and the authentication endpoints used by this certification remained reachable.

## Scope

- Repository: MAIN Windows working copy on branch `main`
- API runtime: MAIN Development API on port `5010`
- POS Admin: port `5173`
- AhramStore Storefront: tenant hostname on port `5174`
- Browser: headed Microsoft Edge, Playwright session `opencode-ui-test`
- Desktop and mobile Storefront authentication behavior
- English and Arabic/RTL behavior
- POS Admin and Storefront authority separation
- Storefront registration, duplicate registration, login, return paths, logout, stale cookies, and deactivated accounts
- CSRF enforcement for authenticated Storefront analytics

No credential, access token, refresh token, or CSRF token is recorded in this report.

## Architecture Findings

- POS Admin and Storefront customers use separate identities, JWT schemes, cookies, and authorization authorities.
- Tenant authority is hostname-derived. `TenantResolutionMiddleware` strips inbound tenant claims/headers and establishes the trusted tenant context.
- Storefront guest/cart session identifiers are tenant-scoped request context, not customer authentication state.
- Storefront registration intentionally creates an unauthenticated account. Authentication is established only by the separate login flow.
- POS Admin session authority is SQL-backed and remained scoped to the authenticated tenant and role.

## Defects Remediated

### Storefront return-path race and validation

- Login with `return=/products` previously raced the guest-page auth guard and could land on `/`.
- Login, registration, and auth guards now share local-only return-path validation.
- External, scheme-relative, backslash, control-character, and non-root-relative targets fall back to `/`.
- Headed verification confirmed successful login landed on `/products`.

### Storefront auth operation ordering

- Older bootstrap or refresh responses could overwrite or clear a newer successful login.
- Authentication operations now use a revision guard so stale responses cannot mutate newer authority.
- Automated race tests cover both late bootstrap success and late bootstrap failure.

### Analytics CSRF propagation

- Authenticated analytics requests previously used direct `fetch` without the CSRF header and returned `403`.
- Single and batch analytics requests now append the standard CSRF header.
- Headed verification after login returned `202 Accepted`.

### Registration policy and duplicate race

- Backend registration accepted passwords rejected by the published Storefront UI policy.
- Backend policy now requires at least eight characters with uppercase, lowercase, and a digit.
- PostgreSQL unique-index races now map deterministically to `EMAIL_EXISTS` instead of a generic registration failure.
- Headed verification confirmed weak-password rejection and localized duplicate-email handling.

### Failed-login lifecycle

- Rejected login attempts now expire prior Storefront access and refresh cookies.
- Backend infrastructure failures map to `503` rather than being presented as invalid credentials.
- Blocked/deactivated accounts remain explicit `403`/`401` outcomes as appropriate.

## Browser Evidence

### Storefront

- Guest bootstrap: `/auth/me` and refresh returned `401`; login page remained usable.
- Invalid credentials: login returned `401`, displayed the localized deterministic error, cleared form credentials, and established no authenticated authority.
- Weak registration password: blocked client-side with the localized policy message; no registration request was accepted.
- Successful registration: returned `200` and showed the intended unauthenticated registration-success flow.
- Duplicate registration: returned `400` with the localized existing-account message.
- Successful login: returned `200`, established customer authority, merged the guest cart, and `/auth/me` returned `200`.
- Safe return: `return=/products` landed on `/products` after login.
- Analytics: authenticated event POST returned `202`, replacing the previously observed CSRF `403`.
- Logout: returned `200`; subsequent `/auth/me` returned `401`.
- Stale session: forged stale access and refresh cookies were rejected; `/account` redirected to `/login?return=%2Faccount`.
- Arabic mobile: at `390x844`, the login and registration pages rendered `lang=ar`, `dir=rtl`, localized labels, and no horizontal overflow.
- Deactivated account: subsequent login returned `401` with the localized deactivated-account message.

### POS Admin

- Authorized login returned `200` and `/api/auth/session` returned `200`.
- Authenticated identity resolved as tenant Owner with the expected effective permission set.
- Reload and protected Settings/dashboard APIs remained authorized.
- Storefront and POS Admin sessions coexisted without cookie or authority collision.
- The disposable Storefront customer was located in Online Customers, deactivated through the Security tab, and the supported endpoint returned `204 No Content`.
- Refreshing the customer detail returned `200`, showed `Inactive`, and showed zero active sessions.

## Automated Verification

### Storefront tests

Command:

```text
npm test -- --run
```

Result: **43 test files passed; 385 tests passed; 0 failed.**

Focused auth/authority execution before the full suite also passed 55 tests across return-path, auth-store race, bootstrap, and commerce-authority coverage.

### Backend auth/security tests

Command scope:

```text
StorefrontAuthenticationLifecycleTests
PosAuthenticationLifecycleTests
AdminAuthenticationSessionCertificationTests
CookieAuthSecurityTests
```

Result: **37 passed; 0 failed.**

The new Storefront lifecycle class independently passed **6/6** cases.

### Builds

- Storefront production build: **passed**.
- POS Admin production build: **passed**.
- API and test projects compiled successfully before focused test execution.
- `git diff --check` for the changed authentication surface: **passed**.

## Non-Blocking Observations

- The aggregate `/health` endpoint returned `503` because `payout_external_truth_readiness` was unhealthy from one external-reconciliation mismatch. PostgreSQL, Redis, MongoDB, Elasticsearch, RabbitMQ, MassTransit, and the exercised auth endpoints were healthy.
- `StorefrontCustomerAuthorityCertificationTests.StorefrontCustomerAuthority_DocumentationMustBeFrozen` failed in a broader mixed filter because an older report marker, `PASS P2-E Storefront Customer Authority`, is absent. The implementation tests in that run passed; this stale documentation assertion is outside the remediated runtime behavior.
- Guest auth bootstrap `401` responses are logged as browser-console errors by the shared HTTP client. They are expected authority outcomes but create avoidable console noise.
- Existing build warnings remain for large frontend bundles, dynamic/static import overlap, and stale Browserslist data. They do not affect this auth verdict.

## Changed Files

- `PointOfSale.Storefront/src/auth/returnPath.ts`
- `PointOfSale.Storefront/src/auth/returnPath.test.ts`
- `PointOfSale.Storefront/src/auth/pages/LoginPage.tsx`
- `PointOfSale.Storefront/src/auth/pages/RegisterPage.tsx`
- `PointOfSale.Storefront/src/auth/hooks/useAuthGuard.ts`
- `PointOfSale.Storefront/src/auth/store/authStore.ts`
- `PointOfSale.Storefront/src/auth/store/authStore.test.ts`
- `PointOfSale.Storefront/src/analytics/AnalyticsClient.ts`
- `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts`
- `PointOfSale.API/Services/Storefront/StorefrontAuthService.cs`
- `PointOfSale.API/Controllers/StorefrontAuthController.cs`
- `PointOfSale.Tests/Security/StorefrontAuthenticationLifecycleTests.cs`

## Final Certification

The MAIN POS Admin and AhramStore Storefront authentication surfaces are certified **PASS** for the tested scope on 2026-09-28. Login authority, safe return navigation, registration policy, duplicate handling, logout, stale-session rejection, CSRF-protected analytics, tenant/cookie isolation, Arabic RTL mobile behavior, and supported test-account cleanup all produced the expected outcomes.
