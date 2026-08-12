# SHOPIXY Shipping Methods Step 2A Tenant Scope Remediation

Date: 2026-08-11

## 1. Problem

FACT: Storefront checkout could fail shipping-method admission with `Shipping method response could not be admitted without tenant scope.`

FACT: The failure occurred before per-method shipping evidence was admitted when the Storefront resolved tenant was a slug instead of a UUID.

## 2. Confirmed Root Cause

FACT: Storefront tenant resolution legitimately accepts UUIDs and slugs.

FACT: Backend `TenantResolutionMiddleware` strips inbound `X-Tenant-Id`, resolves tenant authority from trusted sources, rejects spoofing, and re-adds an authoritative GUID header for downstream backend code.

FACT: Previous shipping admission used the raw frontend request `X-Tenant-Id` as the expected tenant UUID. That failed for slug-resolved storefronts.

## 3. Security Constraints

FACT: `TenantResolutionMiddleware` remains authoritative.

FACT: Raw client `X-Tenant-Id` is not trusted as tenant authority.

FACT: Storefront shipping method reads remain backend tenant-scoped by GUID.

FACT: Cross-tenant shipping method evidence must be rejected by frontend admission.

## 4. Chosen Remediation

IMPLEMENTED: Added response-level `TenantId` to `ShippingMethodsListResponseDto`.

IMPLEMENTED: `ShippingMethodService.GetShippingMethodsAvailabilityAsync` sets response `TenantId` from the trusted `tenantId` argument supplied by controller/middleware authority.

IMPLEMENTED: Storefront shipping admission now requires the response-level `tenantId` to be a UUID and validates every returned shipping method against that canonical response GUID.

IMPLEMENTED: Bare array shipping responses are no longer admitted because they do not carry server-authoritative tenant scope.

IMPLEMENTED: Storefront shipping methods response now sets no-store headers.

## 5. Why This Is Safe

FACT: The client can still send a slug only as a tenant resolution hint.

FACT: The backend resolves that slug to a tenant row and GUID before the shipping service is called.

IMPLEMENTED: The frontend compares method `tenantId` evidence against the server response `tenantId`, not against the raw client header.

IMPLEMENTED: `EMPTY` and `NOT_APPLICABLE` responses still require response-level tenant authority but do not invent per-method tenant evidence.

## 6. Files Changed

IMPLEMENTED:

- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
- `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts`
- `PointOfSale.Tests/Storefront/ShippingMethodServiceCurrencyConsistencyTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontShippingMethodAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/AdminShippingMethodReadAuthorityCertificationTests.cs`

## 7. Tests Added/Changed

IMPLEMENTED:

- UUID tenant plus matching shipping method accepts.
- Slug-resolved tenant plus server GUID response accepts.
- Subdomain-style slug tenant plus server GUID response accepts.
- Foreign tenant shipping method rejects.
- Missing response tenant authority rejects.
- Malformed response tenant authority rejects.
- `EMPTY` shipping response classifies correctly with response tenant authority.
- `NOT_APPLICABLE` shipping response classifies correctly with response tenant authority.
- Backend availability response preserves canonical tenant GUID for `AVAILABLE`, `EMPTY`, and `NOT_APPLICABLE`.
- Tenant resolver test confirms subdomain resolution returns a slug, not an inferred GUID.
- Existing payment admission coverage remains in Storefront suite.
- Existing backend middleware spoofing tests remain unchanged and pass in focused run.

## 8. Verification Results

VERIFIED:

- `npm test -- --run src/commerceAuthority.certification.test.ts` passed: 29 tests.
- `npm test -- --run` passed: 27 files, 265 tests.
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ShippingMethodServiceCurrencyConsistencyTests"` passed: 8 tests.
- Focused middleware spoofing/host tests passed: 3 tests.
- Focused `StorefrontShippingMethodAuthorityCertificationTests` source checks passed: 2 tests.

NOT VERIFIED:

- Live API/Storefront runtime verification was not completed. I did not fabricate database, Mongo, Redis, R2, or production state.

## 9. Regression Results

VERIFIED:

- `npm run build` in `PointOfSale.Storefront` passed after removing the stale `getRequestHeaders` import.
- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore` passed.
- `dotnet build PointOfSale.sln --no-restore` passed.

FACT:

- A broader mixed backend filter surfaced unrelated/stale certification failures in payment artifact markers, admin shipping page source assertions, and AllowAnonymous inventory. These were not caused by the Step 2A implementation and were not remediated because they are outside this scope.

## 10. Remaining Shipping Issues

DEFERRED:

- Manual Fulfillment admin UX.
- Carrier selection UX.
- Warehouse assignment UX.
- CarrierAccount readiness.
- External carrier readiness convergence.
- Shipping method to carrier account or warehouse model redesign.

## 11. Explicitly Deferred P1/P2 Items

DEFERRED:

- SM-P1-002 full shipping/payment contract convergence beyond the P0 tenant authority bridge.
- SM-P1-003 Manual Fulfillment external-carrier workflow mismatch.
- SM-P1-004 carrier display name versus canonical carrier code mismatch.
- SM-P1-005 non-manual carrier readiness authority mismatch.
- SM-P2-006 warehouse ID text input.
- SM-P2-007 shipping method warehouse/account relationship design.
- SM-P2-008 projection documentation/comment cleanup.

## 12. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED — implementation and tests pass, but live runtime verification could not be completed.
