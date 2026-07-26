# Audit: identity-access-management.md vs Real Code

> Generated: 2026-07-21
> Scope: Cross-reference of `.ai/hermes/platform/identity-access-management.md` claims against code at `PointOfSale/`

---

## Summary

| Category | Count |
|----------|-------|
| Report inaccuracies (claims contradicted by code) | 2 |
| Report omissions (code facts missing from report) | 1 |
| Code issues CORRECTLY identified by report | 8 |

---

## REPORT INACCURACIES

### I-1. CustomerTokenRevocationService governance claim

**Report says** (§3, §10, matrix): `CustomerTokenRevocationService.EnsureTokenSessionStillValidAsync` checks governance (ban/suspension).

**Code says** (`PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs:254-295`): The method checks only:
- `CommerceHubCustomer.IsEmailVerified` (line 280-283)
- `CommerceHubCustomer.TokensValidAfterUtc` against token `iat` (lines 285-294)

There is **no** governance profile query, no `Banned`/`Suspended` check, and no call to `UserGovernanceService` in this method. Governance gating for CommerceHub customers happens at **login time** in `CommerceHubCustomerAuthService`, not per-request in the token revalidation path.

**Impact:** Medium — overstates per-request re-validation depth for the CommerceHub realm.

---

### I-2. RefreshTokenHasher class line reference

**Report says** (§2.6): `RefreshTokenSecurity.cs:25` as the location of the hasher.

**Code says** (`PointOfSale.Core/Security/RefreshTokenSecurity.cs`):
- Line 17: `RefreshTokenGenerator` class begins
- Line 25: `RefreshTokenGenerator.Generate()` method (64-byte CSPRNG → URL-safe Base64)
- Line 55: `RefreshTokenHasher` class begins (SHA-256 hash + constant-time verify)

**Impact:** Low — minor line-number drift; the functional description is correct.

---

## REPORT OMISSIONS

### O-1. SuperAdminJwt configuration override not documented

**Report says** (§7.1): SuperAdminJwt Issuer/Audience/expiry are "(config)" — implying they must be manually configured.

**Code says** (`Program.cs:2221-2238`): When `SuperAdminJwt:Issuer` is empty, it defaults to `"PointOfSaleAPI:super-admin"`; when `SuperAdminJwt:Audience` is empty, it defaults to `"PointOfSaleClient:super-admin"`; zero expirations fall back to POS `JwtSettings` values. Only `Secret` is strictly required.

**Impact:** Low — the config is partially auto-derived, not fully manual.

---

## CODE ISSUES CORRECTLY IDENTIFIED (confirmed)

These are real divergences between the report and ideal architecture that the report correctly flags and the code confirms:

| # | Issue | Severity | Code Confirmation |
|---|-------|----------|-------------------|
| C-1 | Ghost role `"Accountant"` in controller `[Authorize]` metadata + `AdminTenantAuthority.TenantActorRoles` but absent from `UserRole` enum | **High** | `AdminCommerceHubFinanceController.cs:9`, `AdminCommerceHubPaymentsController.cs:10`, `AdminTenantAuthority.cs:42` — literal `"Accountant"` present; `UserRole.cs` has no such value |
| C-2 | Overlapping customer identity models require background reconciliation | **High** | `OnlineCustomer` (tenant-scoped) vs `CommerceHubCustomer` (global); `CanonicalIdentityReconciliationHostedService` runs every 5 min |
| C-3 | No centralized permission system — authorization logic spread across controllers, middleware, authorities, governance | **Medium** | Confirmed across 6+ authority/middleware files |
| C-4 | Four independent revocation mechanisms with no shared abstraction | **Medium** | POS (session-binding hash), Storefront (`IsRevoked`), CommerceHub (jti blacklist), SuperAdmin (`TokenVersion`) |
| C-5 | Capability strings are client UI hints, not server-enforced permissions | **Medium** | `NavigationCapabilities` in `AuthController.cs:202-245` |
| C-6 | Legacy `auth_token` cookie retained for compatibility | **Low** | `AuthCookieConfiguration.cs:13,73,91` |
| C-7 | Test-auth issuer coexists with production issuers | **Low** | `TestAuthController.cs` protected by `TestingOnlyControllerFeatureProvider` |
| C-8 | Capability computation duplicated across auth services (DRY violation) | **Medium** | Separate paths in `AuthController`, `CommerceHubCustomerAuthService`, `StorefrontAuthController` |

---

## VERIFICATION METHODOLOGY

Each claim in the report was traced to the cited file and line. Where no citation was given, the nearest equivalent code path was examined. Confirmed claims are not re-listed here — only discrepancies and confirmed issues are reported.

Core files examined:
- `PointOfSale.API/Program.cs` — middleware pipeline, DI registrations
- `PointOfSale.API/Controllers/AuthController.cs` — POS auth
- `PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs`
- `PointOfSale.API/Security/PosSessionAuthority.cs`
- `PointOfSale.API/Security/AdminTenantAuthority.cs`
- `PointOfSale.API/Configuration/JwtMiddlewareConfiguration.cs`
- `PointOfSale.API/Configuration/SensitiveConfigurationValidator.cs`
- `PointOfSale.Core/Enums/UserRole.cs`
- `PointOfSale.Core/Security/RefreshTokenSecurity.cs`
- `PointOfSale.API/appsettings.json`

---

## RECOMMENDATIONS

1. **Fix I-1**: Update the report to remove governance from `CustomerTokenRevocationService.EnsureTokenSessionStillValidAsync` description, or add governance checks to the code.
2. **Fix I-2**: Correct the line reference from `:25` to `:55` for `RefreshTokenHasher`.
3. **Add O-1**: Document the fallback logic for `SuperAdminJwt` configuration in the report.
4. **Resolve C-1 (ghost Accountant role)**: Either add `Accountant` to `UserRole` enum with proper assignment flows, or remove `"Accountant"` from both controllers and `AdminTenantAuthority.TenantActorRoles`.

---

*End of audit. Raw verification traces archived in task outputs.*
