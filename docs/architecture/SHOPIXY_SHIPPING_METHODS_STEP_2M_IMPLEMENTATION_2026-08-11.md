# SHOPIXY Shipping Methods Step 2M Implementation

Date: 2026-08-11

## 1. Executive summary

Step 2M converged the merchant-facing Carrier Integration workflow onto `CarrierIntegrationAuthority` as the single runtime authority.

The Carrier Accounts connection-test path now performs Save & Validate semantics:

1. Persist entered credentials through `CarrierIntegrationAuthority.ValidateAndStoreIntegrationAsync`.
2. Resolve the persisted `CarrierConfiguration` credentials.
3. Validate those persisted credentials through the provider adapter.
4. Store runtime validation state on `CarrierConfiguration`.
5. Record `CarrierConnectionTestRecord` only as diagnostic evidence.
6. Present business-oriented merchant state in the carrier account card.

## 2. Exact root cause fixed

Before Step 2M, the Carrier Account UI sent Bosta API keys to `ConnectionTestCommand`, and `CarrierAccountAdministrationRuntime.RunConnectionTestAsync` validated those request-supplied credentials directly against `BostaAdapter`.

That path could create:

`Connection Test Status = Succeeded` + `Credential Availability = Missing` + `Authority Readiness = Blocked` + `CARRIER_CONFIGURATION_MISSING`.

The fix removes that normal merchant workflow split. The account validation endpoint now saves canonical encrypted runtime credentials first, then validates the persisted credentials.

## 3. Before/after carrier state machine

Before:

`CarrierAccount request credentials -> provider validation -> CarrierConnectionTestRecord`

separate from:

`CarrierConfiguration -> CarrierIntegrationAuthority readiness -> storefront eligibility`

After:

`CarrierIntegrationAuthority -> CarrierConfiguration -> encrypted runtime credentials -> provider validation -> runtime readiness -> storefront eligibility`

Merchant-facing state is now:

`Not configured -> Credentials saved -> Connection verified -> Ready for checkout`

## 4. CarrierIntegrationAuthority changes

`CarrierIntegrationAuthority` now:

- Encrypts `CarrierIntegrationUpdate.Configuration` before persisting it.
- Decrypts `CarrierConfiguration.Configuration` when building adapter credentials and webhook trust configuration.
- Preserves plaintext JSON compatibility for legacy stored rows through the existing encryption service fallback.
- Requires successful persisted provider validation for executable external-carrier readiness in storefront/checkout/shipment purposes.
- Does not treat `Partial` production capability as a blocker.

## 5. CarrierConfiguration persistence behavior

The Carrier Account Save & Validate endpoint writes `CarrierConfiguration` via `ValidateAndStoreIntegrationAsync` with `IsEnabled=true`, metadata-driven credential fields, sandbox/production selection, and extra config JSON.

Bosta persists only `apiKey`.

Aramex required extra fields continue to flow through `Configuration`, but the whole JSON payload is now encrypted at rest without schema changes.

## 6. Bosta workflow verification

Automated verification proves:

- Bosta API key entered through Carrier Integration creates `CarrierConfiguration`.
- Stored `ApiKey` is encrypted and not equal to plaintext.
- Validation uses the decrypted persisted credential.
- Successful validation updates `LastValidationSucceeded=true`.
- Readiness reports `CredentialAvailability=Available`.
- Readiness is executable and does not include `CARRIER_CONFIGURATION_MISSING`.
- A succeeded diagnostic connection record cannot coexist with missing runtime credentials in the canonical workflow.

## 7. CarrierAccount facade treatment

`CarrierAccount` remains a compatibility/admin facade.

The admin read model now presents facade health as healthy when canonical provider readiness is executable, so legacy facade blockers cannot override the primary provider readiness summary.

Activation endpoints and compatibility tables were not deleted.

## 8. Aramex credential protection

Aramex `ExtraConfig` values such as `AccountPin`, `AccountEntity`, and `AccountCountryCode` are encrypted as part of the whole `CarrierConfiguration.Configuration` JSON payload.

Regression coverage verifies the stored payload is encrypted and the adapter still receives decrypted extra config.

No migration was created.

## 9. UI changes

`CarrierAccountCard` now presents business-oriented state:

- Not configured
- Credentials saved
- Connected
- Ready for checkout
- Connection verification failed
- Provider could not be reached
- Production not certified / provider unavailable

Raw readiness blockers and facade details are moved to an Advanced diagnostics disclosure.

The primary action is now `Save & validate` / `Save & validate locally`.

## 10. Manual Fulfillment behavior

Manual Fulfillment remains non-external:

- No credentials.
- No `CarrierConfiguration`.
- No connection test.
- No `CarrierAccount`.
- No `CarrierWarehouseAssignment`.
- Runtime readiness remains valid.

## 11. Storefront eligibility impact

Storefront admission still requires:

- Active `ShippingMethod`.
- Valid active tenant-owned `ShippingMethod.FulfillmentWarehouseId`.
- Stock availability.
- `CarrierIntegrationAuthority` executable readiness for external carriers.
- Manual special-case validity.

The Step 2M readiness change means an external carrier method is not admitted before canonical configuration and successful persisted validation exist.

## 12. Tests

Added:

- `CarrierIntegrationStateMachineConvergenceTests`

Covered:

- Bosta credentials saved through authority and encrypted.
- Bosta save + validation produces ready authority state.
- Succeeded diagnostic test cannot coexist with missing credentials.
- Validation failure keeps credentials saved but not verified.
- Network failure remains distinct from invalid credentials.
- Manual Fulfillment creates no external runtime artifacts.
- CarrierAccount facade cannot override canonical readiness presentation.
- Aramex extra credential configuration is encrypted/decrypted.
- Storefront Bosta method hidden before canonical runtime config and admitted after readiness.

Updated:

- `PosAdminShippingAdministrationApiTests` helper now constructs a real `CarrierIntegrationAuthority` with deterministic fake encryption.

## 13. Builds

Passed:

- `dotnet build PointOfSale.sln --no-restore`
- `npm test -- --run src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx`
- `npm run build` in `PointOfSale.Web`
- `npm run build` in `PointOfSale.Storefront`

Targeted backend tests passed:

- `CarrierIntegrationStateMachineConvergenceTests`
- `CarrierIntegrationAuthorityCertificationTests`
- `PosAdminShippingAdministrationApiTests`
- `PosAdminShippingAdministrationRuntimeTests`
- `ShippingMethodServiceCurrencyConsistencyTests`
- `WarehouseResolutionServiceTests`

One broader targeted run including `StorefrontShippingMethodAuthorityCertificationTests` failed on pre-existing dirty-worktree assertions unrelated to Step 2M:

- Missing `PASS SF-PA5.3` in an existing certification artifact.
- Missing frontend source string `setShippingMethod: (id) =>`.

## 14. EF/migration status

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext --no-build`

Result:

`No changes have been made to the model since the last migration.`

No migration was created.

## 15. Runtime verification status

Live browser/API/database verification was not available in this environment.

The exact workflow remains externally pending:

Carrier Integration -> configure Bosta -> enter real API key -> Save & Validate -> inspect live `CarrierConfiguration` -> create active Bosta Shipping Method -> select fulfillment warehouse -> checkout storefront admission -> warehouse resolution -> final checkout.

## 16. Worktree safety evidence

Pre-implementation worktree:

- Repository was already heavily dirty.
- `git status --short` showed many modified shipping/checkout/admin/storefront files and many untracked Step 2 artifacts.
- `git diff --stat` before Step 2M: 56 files changed, 4603 insertions, 1310 deletions.

Post-implementation worktree:

- Dirty tree preserved.
- No reset, clean, checkout, stash, commit, or push was performed.
- `git diff --stat` after Step 2M: 56 files changed, 4899 insertions, 1333 deletions, plus untracked Step 2M test/report files.

Step 2M-specific files changed/added:

- `PointOfSale.Infrastructure/Shipping/CarrierIntegrationAuthority.cs`
- `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs`
- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
- `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationApiTests.cs`
- `PointOfSale.Tests/Architecture/CarrierIntegrationStateMachineConvergenceTests.cs`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2M_IMPLEMENTATION_2026-08-11.md`

## 17. Remaining external verification items

- Live Bosta production/sandbox credential validation.
- Live Bosta shipment creation certification.
- Live Aramex and DHL validation with real carrier credentials.
- Production database inspection for existing `CarrierConfiguration` rows and Data Protection key continuity.
- Browser verification of the exact merchant workflow on the deployed/admin runtime.

## 18. Final certification

Certification: PASS WITH EXTERNAL VERIFICATION BLOCKED

Reason: the canonical lifecycle split is fixed in implementation and covered by automated regression tests, but live carrier/browser/database verification was not available in this environment.
