# M3-B1 StoreConfig Creation Path Audit — Shopixy Load Harness (AUDIT ONLY)

> No writes executed. No k6 load. No Mongo/ES writes. No SQL edits. Static code inspection only, with file:line references.
> Date (UTC): 2026-09-15. Scope: confirm the exact production-equivalent way to create a `StoreConfigs` row for the M2 loadtest tenants (`loadtest-tenant-a/b/c`), or STOP with BLOCKED.

## Verdict

**CONFIRMED — safe application path exists. No BLOCKED condition.**

The production-equivalent creation path is the authenticated tenant Owner/Admin **status workflow**, not registration and not SQL:

1. `GET /api/admin/store-config` (read; returns an unpersisted default candidate with `RowVersion "0"` when no row exists), then
2. `PUT /api/admin/store-config/status` with `{ "storefrontStatus": "live", "rowVersion": "<from GET>" }` — **creates the row if missing** (create-or-upsert) and runs the real publication workflow.

Registration was examined and **rejected** for M3-B (creates a whole new tenant + owner + warehouse + `StoreConfig(status=disabled)` — wrong tool, business side effects, duplicate-tenant risk).

---

## 1. Exact route

| Operation | Route | Source |
|---|---|---|
| Read (discover + obtain RowVersion) | `GET /api/admin/store-config` | `PointOfSale.API/Controllers/AdminStoreConfigController.cs:68-104`, `[Route("api/admin/store-config")] (:34)` |
| **Create/upsert + publish (CONFIRMED path)** | **`PUT /api/admin/store-config/status`** (`UpdateStorefrontStatus`) | `AdminStoreConfigController.cs:206-278` |
| Alternative create/upsert (full replacement; NOT preferred for M3-B) | `PUT /api/admin/store-config` (`UpdateStoreConfig`) | `AdminStoreConfigController.cs:122-200` |

Status endpoint is preferred because it binds only the publication domain (`StorefrontStatus`, `MaintenanceMessage`) and explicitly "does not bind unrelated StoreConfig domains such as Commerce AI" (`:202-205`), minimizing mutation surface.

## 2. HTTP method

- Read: `GET`.
- Create/publish: `PUT` (`[HttpPut("status")]`, `:206`). Full-replacement alternative is also `PUT` (`[HttpPut]`, `:122`).

## 3. Required auth

- Controller-level `[Authorize(Roles = "Owner,Admin")]` (`AdminStoreConfigController.cs:35`); status action inherits it (no per-action override).
- Staff POS/Admin JWT scheme (`PointOfSaleAPI` issuer, `tenant_id` GUID claim + `IPosSessionAuthority` validation; see discovery §4). Practically: `POST /api/auth/login` as `loadtest-owner-{a,b,c}` → `access_token` HttpOnly cookie + `Authorization: Bearer <token>`.
- Post-auth `AdminTenantAuthorityMiddleware` revalidates Owner/Admin against live SQL (pipeline order `… → Authorization → AdminTenantAuthority → PublicationGuard`, `Program.cs:3568-3637`).
- `TestAuth` (`api/test-auth/token`) is test-only, prod-stripped, and **forbidden** for M3-B.
- Proof required per tenant: HTTP 200 login, session bound to expected `tenantId`, owner role present (Part C).

## 4. Required body

`PUT /api/admin/store-config/status` binds `AdminStorefrontStatusRequest` (`DTOs/Admin/AdminStoreConfigDtos.cs:143-179`):

```json
{
  "storefrontStatus": "live",
  "maintenanceMessage": null,
  "rowVersion": "0"
}
```

- `storefrontStatus`: required, whitelist `live | maintenance | disabled` (case-insensitive). `Validate()` returns `StorefrontStatus must be one of…` otherwise → 400 `INVALID_STORE_CONFIG` (`:221-228`).
- `maintenanceMessage`: optional, max 500 chars.
- `rowVersion`: required optimistic-concurrency token (string form of uint). May alternatively be supplied as `If-Match` header (see §6). Omit both → `428 PRECONDITION_REQUIRED`.
- `GET` response shape (`AdminStoreConfigResponse`, `:101-137`) echoes `id`, `tenantId`, `rowVersion`, `storeName`, `storefrontStatus`, theme/behavior/commerceAi, timestamps, pages/templates, preset keys. The `rowVersion` from GET is the exact input for the subsequent PUT.
- Full-replacement `PUT /api/admin/store-config` instead requires `AdminStoreConfigRequest` (`:13-96`): `storeName` (1–100, required), `themeId` (required), `theme` + `behavior` objects (required, hex-color/font/button whitelists), `storefrontStatus` whitelist, `rowVersion`. Heavier contract; not needed for M3-B.

## 5. Required tenant context

- `ValidateTenantHeader` → `_tenantContext.RequiredTenantId` (`AdminStoreConfigController.cs:836-849`); missing tenant → 400 `TENANT_HEADER_MISSING`.
- Authority is `TenantResolutionMiddleware` (trusted-only, in order): (1) `tenant_id` claim → active-`Tenants` lookup; (2) domain mapping; (3) verified `Host → StorefrontBaseUrl`; (4) `{slug}` template. Inbound `X-Tenant-Id` is stripped and re-added server-side; spoof → `403 TENANT_FORBIDDEN`; auth-vs-host mismatch → `403`; `400 TENANT_REQUIRED` unless `[AllowAnonymousTenant]` (admin store-config has neither allowance — tenant is mandatory).
- Service scope is strictly per-tenant: `FirstOrDefaultAsync(c => c.TenantId == tenantId)` (`AdminStoreConfigService.cs:224-225`); no tenant id is accepted from the client body. Cross-tenant writes are structurally impossible through this handler.
- Runner must send per-tenant JWT + `X-Tenant-Id: <tenantGuid>` + `Host: loadtest-tenant-{k}.localhost:5174` (or at minimum the JWT claim + header; claim wins) and assert response `tenantId == expected`.

## 6. Concurrency / RowVersion semantics

- `StoreConfig.RowVersion` is a Postgres `xmin`-backed `uint`, serialized as decimal string (`MapToResponseAsync`: `RowVersion = config.RowVersion.ToString(...)`, `AdminStoreConfigService.cs:1284`; response default `"0"`).
- Every mutation requires a precondition: `RequireExpectedRowVersion` (`AdminStoreConfigController.cs:882-895`) reads body `rowVersion` first, else `If-Match` header (accepts quoted/weak `W/"…"` forms, `:914-930`). Missing/unparseable → `428 PRECONDITION_REQUIRED` + telemetry `precondition_missing/invalid` (`:890`).
- New-row creation rule (`UpdateStorefrontStatusAsync`, `AdminStoreConfigService.cs:229-247`; identical in `UpdateStoreConfigAsync`, `:146-166`): when no row exists, only `expectedRowVersion == 0` is accepted (the value GET reports for the unpersisted default candidate). Nonzero expectation on a missing row → `ConcurrencyConflictException` → `409 CONCURRENCY_CONFLICT`. Existing rows apply `ApplyExpectedRowVersion` (`:1338-1348`, sets EF `OriginalValue`) so a stale token → `DbUpdateConcurrencyException` → `409`.
- Success responses carry `ETag: "<rowVersion>"` + `X-Resource-Version` (`ApplyConcurrencyHeaders`, `:897-906`).
- M3-B is single-threaded sequential per tenant (A→B→C, no parallelization first pass), so no concurrent-write contention is expected; on 409, re-GET and retry once with the fresh token, never force.

## 7. Verified-email preconditions

- `EnsureStorefrontCanPublishLiveAsync` (`AdminStoreConfigService.cs:893-918`) runs on both PUT paths whenever normalized requested status == `live` (`:218-222`, `:131-139`). It loads the tenant's `Owner` (`Users WHERE TenantId + Role==Owner`) and throws `StorefrontVerificationException` ("Please verify your email before publishing your storefront.") if `owner.EmailVerificationStatus == EmailVerificationStatus.Unverified` (`Unverified = 0`, `Verified = 1`; `Core/Enums/EmailVerificationStatus.cs:11-22`). Controller maps it → `400 EMAIL_NOT_VERIFIED` (`AdminStoreConfigController.cs:248-258`).
- Non-`live` statuses bypass the check (`:899-902`).
- M2 seed inserts staff with `EmailVerificationStatus = 1` (`tests/load/python/seed.py:430`), i.e. **Verified**, so the precondition is satisfied by construction — but M3-B must still treat `EMAIL_NOT_VERIFIED` as a hard STOP (verify, never bypass; do not flip the flag by hand).

## 8. Side effects

`PUT …/status` → `UpdateStorefrontStatusAsync` (`AdminStoreConfigService.cs:205-264`):

1. Normalizes status (`NormalizeStorefrontStatus`, default `live` when blank, `:886-891`).
2. Verified-email gate (§7).
3. Load-or-create `StoreConfig` (new rows via `BuildDefaultConfig(tenantId)` — `StoreName "My Store"`, `ThemeId "default"`, `StorefrontStatus "live"`, default theme/behavior, `CustomizationJson {"storefrontTheme":"modern"}`, CommerceAI defaults; `:920-942` — then overwritten with requested `StorefrontStatus`/`MaintenanceMessage`, `:249-251`).
4. `PersistStoreConfigChangeAsync` (`:981-1014`): stages domain event in the **transactional outbox** (`StageStoreConfigEventsAsync`, `:1016-1089`) — `StoreConfigCreatedEvent` when new, else `StoreConfigUpdatedEvent`, plus always `TenantUpdatedEvent` — then `SaveChangesAsync` in one transaction; telemetry `store_config_created/updated → outbox_commit`. `DbUpdateConcurrencyException` → 409.
5. Downstream (async, production path): `StorefrontConfigProjectionConsumer` upserts Mongo `storefront_configs` + Redis invalidation (M3-A §3/§10). Publication guard (`StorefrontPublicationGuardMiddleware.cs:41-75`, `IStorefrontPublicationStatusService.IsPublishedAsync`) subsequently passes for anonymous `GET` storefront reads; before that they 404 `STOREFRONT_NOT_PUBLISHED`.
6. No audit row is written on this path (audit calls exist only on page/theme/rollback paths); no provider, webhook, order, payment, shipment, or email invocation occurs in this handler (verified: no provider/webhook/order service references in `UpdateStorefrontStatusAsync`).

## 9. Whether safe for loadtest tenant

**Yes — safe**, provided the runner obeys:

- Per-tenant Owner JWT (never super-admin, never TestAuth), sequential A→B→C.
- Tenant-scoped handler (no client tenant parameter; stale-deletes n/a on this path).
- Create-or-upsert only; no index/collection deletion, no global reindex, no alias flip.
- Idempotent retry via fresh RowVersion; duplicates impossible (lookup is by `TenantId`; `GET` before `PUT` avoids blind writes).
- Rejected alternative: `POST /api/auth/register`-family / `TenantRegistrationService` (`Services/Platform/TenantRegistrationService.cs:220-319`) creates tenant + owner user + `StoreConfig(disabled)` + warehouse + payment defaults and sends verification flows — conclusively **not** the mechanism (would mint duplicate tenants and business side effects). Do not call registration.
- Forbidden: direct SQL `INSERT/UPDATE` of `StoreConfigs`, hand-editing `StorefrontStatus`, global reindex, maintenance/debug/dev endpoints, webhooks/providers/orders/payments.

## 10. Exact command / request template

Prerequisites: API `http://localhost:5010`, `TEST_ENVIRONMENT=local|test`, `TEST_DB_PROOF` matching host/db, `LOADTEST_SEED_PASSWORD` in env (never in files). Per tenant `k ∈ {a,b,c}` with `TENANT_ID` from `tests/load/data/manifests/latest.json`:

```powershell
# 1) Login as tenant owner (session cookie + tenant binding proof; no TestAuth)
curl.exe -s -c cookies-k.txt -D headers-k.txt `
  -H "Content-Type: application/json" `
  -H "X-Request-Id: m3b-login-k" -H "X-Correlation-Id: m3b-login-k" `
  -d '{"username":"loadtest-owner-k","password":"$env:LOADTEST_SEED_PASSWORD"}' `
  http://localhost:5010/api/auth/login
# expect: HTTP 200, Set-Cookie access_token, body user.tenantId == TENANT_ID, role Owner

# 2) Read current config (discovers missing row; RowVersion "0" == create token)
curl.exe -s -b cookies-k.txt `
  -H "X-Tenant-Id: TENANT_ID" `
  -H "X-Request-Id: m3b-sc-get-k" -H "X-Correlation-Id: m3b-sc-get-k" `
  http://localhost:5010/api/admin/store-config
# expect: HTTP 200, body.tenantId == TENANT_ID; capture body.rowVersion -> RV

# 3) Create-or-publish via the production status workflow (idempotent)
curl.exe -s -b cookies-k.txt `
  -H "Content-Type: application/json" -H "X-Tenant-Id: TENANT_ID" `
  -H "X-Request-Id: m3b-sc-publish-k" -H "X-Correlation-Id: m3b-sc-publish-k" `
  -d '{"storefrontStatus":"live","rowVersion":"RV"}' `
  -X PUT http://localhost:5010/api/admin/store-config/status
# expect: HTTP 200, body.storefrontStatus == "live", fresh rowVersion in body + ETag
# new row  -> StoreConfigCreatedEvent staged; existing -> StoreConfigUpdatedEvent.
# failure EMAIL_NOT_VERIFIED -> STOP (do not bypass). 428 -> resend RV/If-Match. 409 -> re-GET, retry once.
```

Equivalent with `Authorization: Bearer <accessToken>` instead of `-b cookies-k.txt` is acceptable; do not log or commit either form. `X-Tenant-Id` must equal the JWT's tenant; `Host: loadtest-tenant-k.localhost:5174` should additionally be set on storefront (anonymous) calls, and may be set on admin calls for claim-vs-host parity.

---

*Evidence method: static inspection of `AdminStoreConfigController.cs`, `Services/Admin/AdminStoreConfigService.cs`, `DTOs/Admin/AdminStoreConfigDtos.cs`, `Core/Enums/EmailVerificationStatus.cs`, `Services/Platform/TenantRegistrationService.cs:220-319`, `Middleware/StorefrontPublicationGuardMiddleware.cs`, `Middleware/TenantResolution*`, `Program.cs` pipeline order. No traffic generated in this audit.*
