# Tenant, Store Identity, and Settings Deep Audit - 2026-09-22

## 1. Executive Verdict

The tenant/store identity and settings boundary is remediated and certified for
the audited paths.

The system now treats these values as distinct authorities:

| Value | Authority | Mutability |
|---|---|---|
| Storefront host identity | `Tenant.Slug` | Immutable through Settings |
| Tenant display name | `Tenant.Name` | Tenant/platform lifecycle |
| Storefront display name | `StoreConfig.StoreName` | Storefront configuration |
| Settings company name | `SystemSettings["Company.Name"]` | Settings write |
| Storefront origin | `Tenant.StorefrontBaseUrl`, then `Storefront:UrlTemplate` | Backend-resolved |

The reproduced `PUT /api/settings` failure was caused by request-model
validation requiring `CurrencyCode` before controller fallback could run. The
DTO now accepts legacy requests that omit the code, while the controller derives
and validates the canonical currency from tenant authority.

The live development database passed the slug preflight, migration
`20260922141030_TenantSlugCanonicalIdentity` was applied successfully, and EF
reports no pending migrations.

## 2. Scope

The audit covered:

- Tenant registration and subdomain availability.
- Canonical slug validation and duplicate-race behavior.
- Settings GET/PUT contracts and the reported HTTP 400.
- Tenant, storefront, and company display-name separation.
- Storefront URL generation in API, Infrastructure, affiliate projections,
  recovery links, verification links, preview links, and the POS frontend.
- Settings read/write permissions and tenant isolation.
- Migration preflight, generated SQL, live data compatibility, and application.
- Focused backend/frontend tests and live runtime checks.

## 3. Reproduced Incident

The original request failed before reaching `SettingsController.UpdateSettings`:

```http
PUT http://localhost:5010/api/settings
HTTP/1.1 400 Bad Request

{
  "code": "VALIDATION_ERROR",
  "message": "The request payload is invalid.",
  "data": {
    "CurrencyCode": ["Currency code is required"]
  }
}
```

Root cause:

- `SettingsDto.CurrencyCode` had `[Required]`.
- Existing clients could send only the display symbol.
- ASP.NET model validation rejected the request before the controller's currency
  fallback and normalization logic ran.

Remediation:

- `CurrencyCode` is nullable at admission for legacy compatibility.
- `Tenant.Currency` remains the currency authority.
- Missing codes are resolved from the submitted symbol or tenant currency.
- Unsupported or mismatched currencies return the explicit `INVALID_CURRENCY`
  contract.
- The current supported settings currency is `EGP`.

## 4. Canonical Slug Contract

`PointOfSale.Core.Tenancy.StoreSlug` is the shared server authority.

Canonical slugs:

- Are trimmed and lowercased.
- Are 3 to 63 characters.
- Contain only ASCII lowercase letters, digits, and hyphens.
- Cannot start or end with a hyphen.
- Cannot contain consecutive hyphens.
- Cannot contain control characters.
- Cannot use reserved platform names.

Registration, availability checks, and platform tenant create/update flows use
this authority. Registration also translates unique-index races for tenant slug
and normalized owner email into stable conflict responses.

Settings writes never update `Tenant.Slug`. A company display name can contain
spaces or non-Latin text without changing storefront host identity.

## 5. Registration Atomicity

Tenant registration keeps the following operations in one transaction:

- Tenant creation.
- Owner creation.
- Store configuration bootstrap.
- Warehouse/category/checklist bootstrap.
- Owner session and refresh-token persistence.
- Authoritative storefront URL resolution.

Missing URL authority, duplicate slug races, duplicate normalized-email races,
or bootstrap failures roll back the registration instead of leaving partial
tenant state.

The availability response now includes a backend-resolved `StorefrontUrl` for a
valid available slug. Live evidence:

| Field | Result |
|---|---|
| Requested slug | `audit-runtime-20260922` |
| Canonical response slug | `audit-runtime-20260922` |
| Available | `true` |
| Storefront URL | `http://audit-runtime-20260922.localhost:5174` |

No live tenant was created solely for this audit. Fresh-database registration
tests cover successful bootstrap and every audited rollback path without
polluting the development database.

## 6. Storefront URL Authority

`PointOfSale.Core.Tenancy.StorefrontUrlAuthority` and
`PointOfSale.API.Services.StorefrontUrlResolver` are the URL authority.

Resolution order:

1. `Tenant.StorefrontBaseUrl`.
2. `Storefront:UrlTemplate` with canonical `Tenant.Slug` substitution.
3. Fail closed when neither authority is available.

Accepted origins must:

- Be absolute HTTP or HTTPS URLs.
- Have a host.
- Have no credentials.
- Have no non-root path, query, or fragment.
- Use a template containing `{slug}` when template fallback is required.

`StorefrontUrlAuthority.BuildUrl` validates the origin and percent-encodes every
path segment. Affiliate referral values and recovery/verification tokens are
also encoded.

The following consumers were centralized or hardened:

- Settings responses.
- Registration and subdomain availability.
- Product links in the POS frontend.
- Theme and CMS preview responses.
- Storefront email verification.
- Abandoned-cart recovery.
- Affiliate promotion links.
- Affiliate product policy projections.
- Affiliate intelligence projections.
- Affiliate product discovery SQL recomposition.
- AF16E affiliate reindexing.
- Affiliate portal landing and product URL responses.

Affiliate projection repair no longer derives a technical tenant slug from
`Tenant.Name`. It re-reads current SQL tenant authority and rejects unavailable
or invalid origins.

The remaining direct template parsing is intentional:

- `TenantResolutionMiddleware` parses the configured host template to resolve
  inbound tenant requests.
- `FeedStorefrontReadinessService` parses the development host pattern for
  readiness classification; it does not emit storefront links.

No `VITE_STOREFRONT_URL`, hardcoded `localhost:5174`, or frontend-composed
storefront origin remains under `PointOfSale.Web/src`.

## 7. Settings Contract

Settings GET returns:

- Company and receipt display settings.
- Canonical currency code and display symbol.
- A backend-resolved, read-only `StorefrontUrl`.

Settings PUT:

- Trims and validates display fields.
- Allows blank optional email but validates nonblank email.
- Rejects unsupported control characters.
- Normalizes currency against tenant authority.
- Ignores client-submitted `StorefrontUrl`.
- Does not mutate `Tenant.Name` or `Tenant.Slug` when `CompanyName` changes.

Authorization is classified by the POS permission authority:

- Settings reads require `dashboard.view`.
- Settings writes require `settings.manage`.
- The UI uses the same `settings.manage` capability to disable mutation.

## 8. Live Runtime Evidence

Environment:

- API: `http://localhost:5010`.
- API liveness: `Healthy`.
- Primary tenant: `c9c5c1ae-949e-498e-bc63-07242daeb49d`.
- Alternate tenant: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.

Settings evidence:

| Assertion | Result |
|---|---|
| Normal POS login | HTTP `200` |
| Authenticated role | `Owner` |
| Server-issued capability | `settings.manage` present |
| GET currency | `EGP` |
| PUT with omitted `currencyCode` | HTTP `200` |
| Currency returned after omitted-code PUT | `EGP` |
| Submitted `storefrontUrl` | `https://attacker.invalid` |
| Returned `storefrontUrl` | `http://ahramstore.localhost:5174` |
| SQL tenant slug after PUT | `ahramstore` |
| SQL tenant name after PUT | `AhramStore` |
| Settings company name after PUT | `AhramStore` |
| Primary token plus alternate `X-Tenant-Id` | HTTP `403` |
| Cross-tenant error code | `TENANT_FORBIDDEN` |

The final certification used the supplied POS administrator through the normal
`POST /api/auth/login` flow, not the development test-token endpoint. Credentials
and issued tokens are intentionally omitted. The temporary audit session was
revoked through `POST /api/auth/logout` after verification.

The live test replayed the existing settings rather than changing business
display data. The submitted attacker origin was not persisted or reflected.

## 9. Migration Evidence

Migration `20260922141030_TenantSlugCanonicalIdentity` contains:

- A fail-closed PostgreSQL preflight for non-canonical non-null slugs.
- Check constraint `CK_Tenants_Slug_Canonical`.
- An `ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"` guard in both
  `Up` and `Down`.

Live preflight result:

```text
InvalidTenantSlugCount=0
```

Migration state before application:

- `20260917135014_PosUserManagementAuthority`: applied.
- `20260922141030_TenantSlugCanonicalIdentity`: pending.

Application result:

```text
Applying migration '20260922141030_TenantSlugCanonicalIdentity'.
Done.
```

Migration state after application: all listed migrations are applied; none are
reported as pending.

## 10. Verification

Backend compilation:

- `PointOfSale.Tests` build: succeeded, 0 errors.
- `PointOfSale.API` build: succeeded, 0 errors.
- Existing repository warnings remain and were not introduced as blockers by
  this audit.

Focused backend suites:

| Scope | Result |
|---|---|
| Slug, URL authority, settings, registration, previews, abandoned cart | 83 passed |
| Affiliate public boundary and SQL-authority projection fallback | 26 passed |
| Earlier focused identity/settings slice | 56 passed |

Frontend verification:

| Scope | Result |
|---|---|
| Registration slug suite | 19 passed |
| CMS/store-config preview services | 26 passed |
| Focused product storefront links | 2 passed |
| Production build | Passed |

The production build retains the existing warning for a JavaScript chunk larger
than 5000 kB.

## 11. Residual Risks and Testing Gaps

The expanded backend run produced 11 failures in `CmsSeedRuntimeTests` while 82
other tests passed. These failures occur during SQLite `EnsureCreatedAsync`,
before CMS behavior executes:

```text
SQLite Error 1: 'near "~": syntax error'.
```

The failing runtime model contains the older PostgreSQL-only
`CK_SupplierCardFundingInstrument_Last4` regex operator from
`SupplierCardFundingInstrumentConfiguration`. The new tenant slug constraint is
provider-gated, and the new migration is also provider-gated. This is a separate
pre-existing SQLite test-harness compatibility issue, not a tenant slug
migration failure.

The storefront Vite document was not reachable during the final runtime pass;
the user confirmed only the API listener at port 5010. Backend URL generation,
frontend unit tests, and the frontend production build passed, but final browser
navigation was therefore not recertified in this pass.

The broader `Products.test.tsx` suite has unrelated existing permission/delete
fixture failures. The focused storefront URL assertions pass.

## 12. Final Certification

The audited implementation now satisfies these invariants:

- Display-name updates cannot rename storefront host identity.
- Registration uses one canonical slug contract and rolls back partial state.
- Database enforcement rejects future non-canonical PostgreSQL tenant slugs.
- Legacy settings requests can omit `CurrencyCode` without producing the
  reported validation failure.
- Client-provided storefront origins are not trusted.
- Backend services and affiliate projections resolve current tenant URL
  authority and fail closed when authority is unavailable.
- Frontends consume backend URLs instead of inventing tenant origins.
- Settings writes and tenant overrides remain permission- and tenant-scoped.

Certification status: **PASS**, with the unrelated SQLite check-constraint
harness issue and unavailable final storefront browser listener recorded above.
