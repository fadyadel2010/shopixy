# Shopixy Shipping Methods Step 2B Implementation

Date: 2026-08-11

## 1. Executive Summary

Step 2B implemented canonical carrier selection for Shipping Methods and hardened the backend so carrier identity is established only by `ShippingMethod.CarrierCode`.

The Shipping Methods administration UI no longer asks merchants to type a carrier display name. It now loads supported carriers from the backend supported-carrier registry API and submits canonical `carrierCode` values such as `manual` and `bosta`.

Manual Fulfillment remains `carrierCode = "manual"`, requires no external carrier account, credentials, warehouse assignment, connection test, or readiness evidence, and is excluded from the carrier-account creation path.

No database schema change was required. EF confirmed no pending model changes.

Final certification: PASS WITH EXTERNAL VERIFICATION BLOCKED.

## 2. Original Defects

- The new Shipping Methods admin form exposed a free-text `Carrier display name` input.
- Create/update payloads did not reliably submit `carrierCode`.
- A merchant could create a Shipping Method visually named `Bosta` while the persisted/runtime carrier identity remained `manual`.
- The server accepted `CarrierName` as mutable presentation data and did not consistently derive it from the registry.
- `ServiceLevel` existed in the domain but was omitted from parts of the admin DTO/UI path.
- The newer CarrierAccount administration path could represent `carrierCode = manual`, even though Manual Fulfillment is not an external integration.
- Frontend carrier metadata was partially duplicated and could drift from the backend registry.

## 3. Root Cause

The UI treated `CarrierName` as if it were carrier identity, while the domain model already had the correct authority: `CarrierCode`. The backend did not fully enforce the registry-backed `CarrierCode` contract on create/update/read projection, so display text could contradict runtime identity.

## 4. Files Changed

Step 2B implementation changed:

- `PointOfSale.API/Controllers/ShippingMethodsController.cs`
- `PointOfSale.API/DTOs/ShippingAdministration/ShippingAdministrationDtos.cs`
- `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs`
- `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs`
- `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js`
- `PointOfSale.Tests/Admin/ShippingMethodsCarrierAuthorityTests.cs`
- `PointOfSale.Tests/Architecture/AdminShippingMethodReadAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CarrierIntegrationAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationApiTests.cs`

Pre-existing dirty storefront/Step 2A files were present before this step and were not reverted or cleaned.

## 5. Canonical Carrier Contract

- `CarrierCode` is the only carrier identity authority.
- `CarrierName` is presentation metadata only.
- Supported carriers are resolved through the existing backend registry/API.
- Unknown `CarrierCode` values are rejected by the admin Shipping Methods API.
- Admin read responses derive display name from registered carrier metadata.
- No `CarrierId`, enum, `ShippingMethod -> CarrierAccount` FK, or `ShippingMethod -> Warehouse` FK was added.

## 6. Admin UI Changes

The Shipping Methods form now:

- fetches supported carriers from the backend supported-carrier API.
- renders a carrier selector using canonical `carrierCode` option values.
- defaults new methods to `manual`.
- preserves valid edit carrier codes.
- warns on unsupported legacy carrier codes instead of inventing options.
- submits `carrierCode` and `serviceLevel`.
- does not submit `carrierName` as carrier identity.
- derives table/form carrier display names from supported-carrier metadata.
- handles carrier metadata loading and failure states.

## 7. Backend Hardening

`ShippingMethodsController` now:

- normalizes requested `CarrierCode`.
- validates `CarrierCode` through `IShippingCarrierRegistry`.
- rejects unknown carrier codes.
- derives `CarrierName` from registry display metadata.
- defaults missing/blank carrier code to `manual` through the existing `CarrierCodes.Normalize` behavior.
- validates `ServiceLevel` against the selected carrier adapter's advertised service levels.
- maps admin read DTOs through a canonical mapper instead of trusting persisted display metadata.

## 8. Manual Fulfillment Semantics

Manual Fulfillment is canonical when:

```text
carrierCode = manual
```

Manual Shipping Methods retain normal method fields:

- name and localized name
- description and localized description
- base rate
- rate per kg
- free shipping threshold
- min/max delivery days
- tracking URL template
- active flag
- sort order
- available countries
- supported service level

Manual Shipping Methods do not require:

- API key
- API secret
- API URL
- external account ID
- credential reference
- connection test
- CarrierAccount
- external readiness evidence
- external warehouse assignment

## 9. ServiceLevel Handling

`ServiceLevel` is now present in the admin Shipping Method DTO path and UI form path.

The UI populates available service levels from the selected carrier metadata returned by the backend. When a carrier changes, incompatible service levels are replaced with the selected carrier's valid default where available, or `standard` where the backend contract permits it.

The backend validates requested service levels against the carrier adapter. Unsupported values are rejected.

## 10. Legacy Data Compatibility

No data rewrite was performed.

Compatibility behavior:

- Valid existing `carrierCode` rows remain safe.
- Missing/blank carrier code continues to normalize to `manual`.
- Admin read responses show canonical display names from registry metadata.
- Rows where `carrierCode` is `manual` but `carrierName` says `Bosta` are no longer allowed to spoof runtime identity.
- Historical rows that require normalization remain a separate review/backfill decision.

Classification:

- Valid `carrierCode`: SAFE.
- Manual/null/blank code with Manual display name: SAFE manual semantics.
- Manual/null/blank code with external-looking display name: NEEDS NORMALIZATION / REVIEW.
- Arbitrary display name with no deterministic code evidence: UNKNOWN.

## 11. Manual CarrierAccount Handling

The supported-carrier API now exposes account/readiness metadata. Manual Fulfillment is returned as not account-required.

The carrier-account creation UI filters out carriers where `accountRequired === false`, so Manual Fulfillment is not offered as an account candidate.

The admin service and CarrierAccount administration runtime reject manual CarrierAccount creation with `MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED`.

No external carrier readiness state machine was redesigned.

## 12. Tenant Isolation

Step 2B preserves existing tenant boundaries:

- Shipping Methods remain tenant-owned.
- CarrierAccount remains tenant-owned.
- Warehouse assignment remains separate and was not redesigned.
- POS Owner/Admin authorization was preserved.
- Storefront shipping tenant admission from Step 2A was not changed by Step 2B.

No carrier selector authority was moved to tenant-owned frontend metadata.

## 13. Step 2A Compatibility

Step 2A storefront admission policy was preserved.

This step did not alter:

- storefront tenant resolution
- no-store response headers
- EMPTY/AVAILABLE/NOT_APPLICABLE semantics
- warehouse filtering policy
- checkout redesign

Focused Step 2A compatibility tests passed after Step 2B adjustments.

## 14. Tests

Backend regression coverage added or updated for:

- valid manual carrier code accepted.
- valid Bosta carrier code accepted.
- unknown carrier code rejected.
- missing carrier code behavior normalizes safely to manual.
- `CarrierName` cannot override `CarrierCode`.
- `carrierCode = manual` with `CarrierName = Bosta` remains canonical manual.
- external carrier display identity derives from registry.
- manual Shipping Method does not require CarrierAccount.
- manual CarrierAccount creation is rejected.
- service level validation rejects unsupported carrier service levels.
- architecture guard verifies no unintended ShippingMethod CarrierAccount/Warehouse FK.

Frontend regression coverage added for:

- Shipping Methods uses backend supported-carrier API.
- selector submits canonical carrier codes.
- free-text carrier display field is removed.
- `carrierName` is not submitted as carrier identity.
- Manual Fulfillment is excluded from account creation through backend metadata.
- FedEx or any other registry-returned carrier can be displayed without hardcoding a frontend authority list.

Known unrelated/stale failures encountered:

- `CarrierIntegrationAuthorityCertificationTests.CertificationArtifacts_MustRecordCarrierIntegrationTruth` expects frozen documentation text containing `PASS SF-SHP4`.
- `StorefrontShippingMethodAuthorityCertificationTests.CertificationArtifacts_MustRecordSfPa53Truth` expects frozen documentation text containing `PASS SF-PA5.3`.
- `AdminShippingMethodReadAuthorityCertificationTests.ShippingMethodReadAuthorityDocumentation_MustBeFrozen` expects frozen documentation text containing `PASS SF-SHIP.READ Shipping Method Read Authority Certification`.

Those artifact freezes were not silently fixed because they were outside Step 2B implementation scope.

## 15. Build Verification

Executed:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "ShippingMethodsCarrierAuthorityTests|CreateCarrierAccount_ShouldRejectManualFulfillmentCarrier|ShippingMethodsStep2B_MustUseCarrierCodeAndBackendRegistryForCarrierIdentity"
```

Result: PASS, 6 tests.

Executed:

```powershell
npx vitest run src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx --pool=threads
```

Result: PASS, 9 tests.

Executed:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "ShippingMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped|ShippingMethodServiceCurrencyConsistencyTests"
```

Result: PASS, 9 tests.

Executed:

```powershell
dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore
```

Result: PASS.

Executed:

```powershell
npm run build
```

Result: PASS.

Executed:

```powershell
dotnet build PointOfSale.sln --no-restore
```

Result: PASS.

Note: `npm test -- --run ShippingAdministrationWorkspace` initially hit a Vitest fork worker timeout. The same focused test file passed with the threads pool.

## 16. EF/Migration Verification

Executed:

```powershell
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext
```

Result:

```text
No changes have been made to the model since the last migration.
```

NO MIGRATION REQUIRED.

## 17. Remaining Issues

- Existing historical rows may still contain contradictory `CarrierName` values. Runtime/admin identity is now protected by `CarrierCode`, but data normalization remains a separate decision.
- Frozen certification documentation tests still expect unrelated prior audit markers.
- Live infrastructure verification was not performed.
- Browser automation was intentionally not performed per Step 2B constraints.

## 18. Deferred Step 2C/2D Items

Deferred to Step 2C:

- Warehouse selector UX.
- Warehouse assignment display/routing improvements.
- Policy decision for whether Manual Fulfillment should appear when a storefront request supplies `warehouseId`.

Deferred to Step 2D/future domain work:

- Multi-account-per-carrier routing.
- CarrierAccount and CarrierConfiguration authority unification.
- CarrierAccount readiness state-machine redesign.
- Deterministic backfill/migration policy for legacy contradictory carrier rows.

## 19. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED.

Carrier identity is now registry-backed and enforced through `CarrierCode` from Admin UI through API/domain/persistence projection. `CarrierName` can no longer spoof carrier identity on create/update/read. Manual Fulfillment remains internal and does not require external carrier account readiness. No schema migration was required.

External/live verification was not performed, and browser automation was intentionally not run.
