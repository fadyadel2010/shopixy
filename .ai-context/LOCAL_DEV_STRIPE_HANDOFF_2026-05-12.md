# Local Dev / Stripe Test Handoff - 2026-05-12

Status: MITIGATED

This file captures the local backend/frontend recovery work needed to run the POS stack for Stripe testing on a developer machine. It is operational memory for future Codex chats; do not treat it as production architecture authority.

## Environment

- Workspace: `C:\Users\fadya\source\repos\PointOfSale`
- API: `PointOfSale.API` on `http://localhost:5010`
- POS web: `PointOfSale.Web` on `http://localhost:5173`
- MongoDB reachable for this session at `192.168.131.128:27017`
- API must be launched with `ASPNETCORE_ENVIRONMENT=Development`, otherwise `SensitiveConfigurationValidator` intentionally rejects missing local secrets.

Run API locally:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project PointOfSale.API --no-build --no-launch-profile --urls http://localhost:5010
```

Run POS web locally:

```powershell
cd PointOfSale.Web
npm run dev
```

## Issues Encountered

### Missing local schema columns/tables

Status: MITIGATED

Symptoms:

- API startup/reconciliation loop failed with PostgreSQL `42703`:
  `column o.PromotionCalculationFailed does not exist`
- Later workers exposed missing/outdated local schema around projection recovery, outbox, and dead-letter archive paths.

Mitigations applied in code/local database during the session:

- Design-time EF factory now loads development user-secrets:
  `PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs`
- Infrastructure project has the same `UserSecretsId` as the API:
  `PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj`
- Local PostgreSQL was patched for missing migration artifacts and indexes:
  `IX_StockSyncOutbox_GlobalPendingOrdering`,
  `IX_AffiliateAttributions_Tenant_Order_Active`,
  `IX_InboxState_Delivered`,
  `PaymentCancellationOutboxMessages`,
  `MarketplaceEventOutboxDeadLetterArchives`

### Storefront order projection repair loop

Status: MITIGATED

Symptoms:

- `StorefrontOrderProjectionReconciliationService` repeatedly repaired the same storefront orders.

Mitigation:

- `PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionBuilder.cs` normalizes projection DateTime values and fingerprint DateTime values to Mongo millisecond precision, avoiding false projection drift.

### Local API startup rejected secrets

Status: ACCEPTED

Symptoms:

- Running API without the development environment produced:
  `SECURITY CRITICAL: Missing or unsafe configuration detected`

Reason:

- This is intentional production-safety behavior. Do not weaken it.

Correct local run command:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project PointOfSale.API --no-build --no-launch-profile --urls http://localhost:5010
```

### POS web blank page from Vite React CSP preamble

Status: MITIGATED

Symptoms:

- Browser console:
  `Executing inline script violates Content Security Policy`
- Then:
  `@vitejs/plugin-react can't detect preamble`

Mitigation:

- Added `'unsafe-inline'` to development/admin script CSP in:
  `PointOfSale.Web/vite.config.ts`
- Matched the same meta CSP for local HTML in:
  `PointOfSale.Web/index.html`

Notes:

- Browser warning `frame-ancestors is ignored when delivered via a <meta> element` remains expected and non-blocking.

### `/api/auth/session` returned `400 TENANT_REQUIRED`

Status: MITIGATED

Symptoms:

- POS web called `GET http://localhost:5173/api/auth/session`
- Vite proxy worked, but API returned:
  `{"error":"Tenant context could not be resolved securely","code":"TENANT_REQUIRED"}`

Root cause:

- `TenantResolutionMiddleware` relies on endpoint metadata for `[AllowAnonymousTenant]`.
- `Program.cs` did not call `app.UseRouting()` before tenant resolution, so `context.GetEndpoint()` was null and `[AllowAnonymousTenant]` was ignored.

Mitigation:

- Added `[AllowAnonymousTenant]` to `AuthController.GetSession`.
- Added `app.UseRouting()` before CORS/auth/tenant middleware in `PointOfSale.API/Program.cs`.

Expected behavior now:

- Logged-out `GET /api/auth/session` should return `401 Unauthorized`, not `400 TENANT_REQUIRED`.

### Login page triggered refresh/logout race

Status: MITIGATED

Symptoms:

- On `/login`, expected unauthenticated `GET /api/auth/session` returned `401`.
- Shared Axios interceptor then incorrectly posted `/api/auth/refresh`, received `401`, and ran logout/redirect handling.
- This could race with sign-in and produce a blank or redirected page.

Mitigation:

- `PointOfSale.Web/src/services/api.js` now treats `/auth/session` as an auth endpoint, so the expected login-page `401` is not refresh-retried and does not force logout.
- `PointOfSale.Web/src/pages/Login.jsx` now includes `autoComplete="username"` and `autoComplete="current-password"` to remove the Chrome form warning.

Expected browser console after fix:

- `GET /api/auth/session 401` on the login page is acceptable before signing in.
- `POST /api/auth/refresh 401` should no longer occur just because the login page bootstrapped an anonymous session.

### POS auth bootstrap could blank protected routes and wipe a newer login with a stale anonymous session response

Status: MITIGATED

Symptoms:

- User signs in successfully, then sees a blank white page or gets bounced back out.
- This was easiest to hit on cold boot when `/api/auth/session` was still pending during login.
- Protected routes rendered nothing while `hasHydrated === false`.

Root cause:

- `AuthBootstrap` starts an anonymous `GET /api/auth/session` on mount.
- If login succeeded before that request settled, `ProtectedRoute` could still render `null` because bootstrap had not marked the store hydrated yet.
- When the older anonymous session request later returned `401`, `AuthBootstrap` called `hydrateSession(null)` and could clear the newer authenticated state.

Mitigation:

- `PointOfSale.Web/src/store/authStore.js` now treats explicit login/logout/session hydration as authoritative hydration and sets `hasHydrated: true` immediately.
- `PointOfSale.Web/src/components/AuthBootstrap.jsx` now ignores stale bootstrap completions once session state changed after the request started.
- `PointOfSale.Web/src/components/ProtectedRoute.jsx` now renders authenticated content immediately when a user session already exists and shows a visible restore state instead of a blank route while auth is unresolved.
- Regression coverage added in `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`.

Expected behavior now:

- A successful login should not wait behind the earlier anonymous bootstrap before rendering the protected app shell.
- A stale `401` from an older `/api/auth/session` request must not log out a user who already completed login in the same tab.

### Dashboard and settings requests failed immediately after login

Status: MITIGATED

Symptoms:

- After sign-in, the POS shell rendered, but dashboard requests failed:
  `GET /api/dashboard/top-products?limit=5 400`
  `GET /api/dashboard/category-sales 400`
  `GET /api/dashboard/payment-methods 400`
- Settings bootstrap also failed:
  `GET /api/settings 500`

Root causes:

- `PointOfSale.API/Infrastructure/Mvc/ApiRequestNormalizationFilter.cs` treated omitted non-nullable reference-type action parameters as required even when the action parameter had a default value. That incorrectly rejected dashboard endpoints such as `string period = "month"` before controller execution.
- `PointOfSale.API/Controllers/SettingsController.cs` queried tenant settings with `IgnoreQueryFilters()` but did not reapply `!IsDeleted`, so soft-deleted duplicate `SystemSettings` rows for the same tenant/key could re-enter the result set and crash `ToDictionary(...)`.
- Some local databases can also carry multiple active tenant-scoped `SystemSettings` rows for the same key when older runtime artifacts or migration drift left the uniqueness invariant unenforced. The previous `ToDictionary(...)` path also crashed on those active duplicates.
- The Ubuntu PostgreSQL database at `192.168.1.5` was also missing the `20260510210000_TenantScopedSystemSettingsAuthority` migration entirely. `SystemSettings` still lacked the `TenantId` column, so the current tenant-scoped settings controller could only fail with `500` until the schema drift was repaired.

Mitigation:

- `ApiRequestNormalizationFilter` now treats `ParameterInfo.HasDefaultValue` as optional and no longer adds a validation error when a default-backed action parameter is omitted from the request.
- `SettingsController.GetSettings()` and `UpdateSettingAsync(...)` now explicitly filter out soft-deleted rows when bypassing global query filters.
- `SettingsController` now orders tenant settings deterministically by freshness (`UpdatedAt`, `CreatedAt`, `Id`), logs duplicate active keys, and uses the newest active row instead of crashing when local data drift left duplicate active rows.
- The missing tenant-scoping migration was applied directly to the Ubuntu PostgreSQL database after verifying the legacy global tenant-facing keys were unique. The repair added `SystemSettings.TenantId`, backfilled tenant-scoped copies of the legacy global settings rows, created the filtered unique index, added the tenant FK, and inserted the migration history row.
- Regression coverage added in:
  `PointOfSale.Tests/Infrastructure/ApiRequestNormalizationFilterTests.cs`
  `PointOfSale.Tests/Controllers/SettingsControllerCurrencyTests.cs`

Expected behavior now:

- Dashboard endpoints that rely on default query parameters should execute normally when the client omits `period`.
- `/api/settings` should ignore soft-deleted duplicate rows and should also tolerate duplicate active tenant setting rows by selecting the newest active value per key instead of crashing during tenant settings bootstrap.
- `/api/settings` should no longer fail on `column s.TenantId does not exist` because the Ubuntu PostgreSQL database now carries the tenant-scoped `SystemSettings` schema expected by the current API.

## Verification Completed

- `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal` passed after stopping the locked running API process.
- When API files were locked by a running process, building to `C:\tmp\pos-api-verify` also passed and confirmed compile correctness.
- `npm.cmd run build` passed in `PointOfSale.Web`.
- `npm.cmd test -- --run src/components/AuthBootstrap.test.tsx src/services/api.test.ts` passed in `PointOfSale.Web`.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ApiRequestNormalizationFilterTests -nologo -v minimal` passed.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~SettingsControllerCurrencyTests -nologo -v minimal` passed.
- `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal --no-restore` passed after the dashboard/settings fixes.
- Live PostgreSQL verification against `192.168.1.5` confirmed the pre-fix drift (`SystemSettings.TenantId` missing, `20260510210000_TenantScopedSystemSettingsAuthority` absent from `__EFMigrationsHistory`) and the post-fix state (`TenantId` present, migration history row present).

## Operational Notes For Next Chat

- If `dotnet build` fails with `MSB3021` or `MSB3027`, the API is likely still running and locking `PointOfSale.API.exe` or `PointOfSale.API.dll`; stop that process or build to a temp output directory.
- If the user runs `dotnet run --no-build`, ensure a normal build was completed after code edits; otherwise they may still be running old DLLs.
- If `/api/auth/session` returns `400 TENANT_REQUIRED`, check that the running API includes both the `AuthController.GetSession` attribute and `Program.cs` `UseRouting()` change.
- If `/api/auth/session` returns `401` on `/login`, that is normal.
- If the POS web is blank immediately after login, confirm the running frontend includes the `AuthBootstrap` stale-response guard and the `ProtectedRoute` hydration fallback/authenticated bypass. The CSP `frame-ancestors` warning and initial auth-session `401` are not blank-page root causes by themselves.
- If dashboard widgets return `400` immediately after login even though the code is patched, the running API is probably stale and still contains the older `ApiRequestNormalizationFilter` logic.
- If `/api/settings` still returns `500`, inspect live tenant `SystemSettings` rows for malformed duplicates or other corrupt values and confirm the running API includes both the `!IsDeleted` filter and the deterministic duplicate-key handling in `SettingsController`.
- If `/api/settings` fails with `column ... TenantId does not exist`, the Ubuntu PostgreSQL host is missing `20260510210000_TenantScopedSystemSettingsAuthority`; this is schema drift, not a frontend/runtime-auth bug.
- If affiliate recovery logs `InvalidCastException: Column 'TenantId' is null` from `AffiliateService`, inspect legacy/global `AffiliateAccounts` rows first. Recovery and commission paths that use `IgnoreQueryFilters()` must not materialize full `AffiliateAccount` entities when nullable legacy tenant state may still exist in the database.
- The hardened affiliate recovery path now uses nullable-safe projections and scalar existence checks in `AffiliateService` so local data drift with null affiliate `TenantId` does not crash attribution repair or commission skip logic.

## Files Changed In This Recovery Slice

- `PointOfSale.API/Controllers/AuthController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Web/index.html`
- `PointOfSale.Web/vite.config.ts`
- `PointOfSale.Web/src/contexts/SettingsContext.jsx`
- `PointOfSale.Web/src/pages/Login.jsx`
- `PointOfSale.Web/src/services/api.js`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/components/ProtectedRoute.jsx`
- `PointOfSale.Web/src/store/authStore.js`
- `PointOfSale.API/Infrastructure/Mvc/ApiRequestNormalizationFilter.cs`
- `PointOfSale.API/Controllers/SettingsController.cs`
- `PointOfSale.Tests/Infrastructure/ApiRequestNormalizationFilterTests.cs`
- `PointOfSale.Tests/Controllers/SettingsControllerCurrencyTests.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`
- `PointOfSale.Tests/Architecture/AffiliateRecoveryNullTenantSourceTests.cs`
- Earlier backend stabilization touched:
  `PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs`,
  `PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj`,
  `PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionBuilder.cs`,
  `PointOfSale.Infrastructure/Data/Configurations/StockSyncOutboxMessageConfiguration.cs`,
  `PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateAttributionConfiguration.cs`,
  `PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs`,
  `PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs`
