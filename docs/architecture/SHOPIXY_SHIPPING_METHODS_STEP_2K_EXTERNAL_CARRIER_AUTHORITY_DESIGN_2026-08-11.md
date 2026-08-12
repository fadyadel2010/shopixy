# SHOPIXY Shipping Methods Step 2K External Carrier Authority Design

Date: 2026-08-11

Step: 2K - External Carrier Integration Authority Forensic Remediation Design

Mode: design only. No source, test, frontend, backend, configuration, schema, migration, database, EF snapshot, or git-state mutation was performed for this report.

## 1. Executive Summary

The Step 2K investigation confirms the unresolved problem identified by `docs/architecture/SHOPIXY_SHIPPING_ADMIN_AND_CARRIER_FORENSIC_AUDIT_2026-08-11.md`: SHOPIXY currently has two carrier integration authorities.

The legacy authority is `CarrierConfiguration`. It stores runtime carrier credentials, encrypted API key/secret values, environment, carrier enablement, webhook trust fields, generic configuration JSON, pickup defaults, and older pricing/capability fields. It is consumed by quote generation, shipment creation, shipment cancellation, tracking sync, webhook trust validation, checkout shipping-method validation, CommerceHub shipping eligibility, and storefront external-carrier admission.

The newer administration authority is `CarrierAccount` plus `CarrierCredentialReference`, `CarrierWarehouseAssignment`, connection tests, lifecycle state, operational state, and health state. It powers the Admin Shipping Administration screen and is currently part of external-carrier storefront readiness. It does not provide the credentials used by runtime shipment or quote execution.

The safest minimal remediation is **Option C: introduce a single carrier integration authority/provider abstraction**, backed initially by the existing encrypted `CarrierConfiguration` rows for runtime credentials and webhook trust, while making the new Carrier Account experience an admin facade over that same authority. Runtime code and storefront admission should call the same provider. CarrierAccount should remain a lifecycle/readiness/audit surface, but it must not be a separate credential/readiness source that can contradict runtime execution.

This is safer than directly migrating runtime to CarrierAccount because the repository does not currently contain a secret resolver for `CarrierCredentialReference.SecretReference`, and CarrierAccount does not persist the carrier-specific credential payload required by Aramex, DHL, FedEx, webhook trust, or generic `ExtraConfig`. It is safer than simple dual-write because dual-write preserves two physical sources and can still drift unless every runtime read is forced through one provider.

Final certification: **DESIGN READY**.

## 2. Scope and Safety Boundary

This report is design-only.

Allowed action performed:

- Created this report only: `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2K_EXTERNAL_CARRIER_AUTHORITY_DESIGN_2026-08-11.md`

Explicitly not performed:

- No code edits.
- No frontend edits.
- No backend edits.
- No tests edited.
- No migrations created.
- No EF snapshot changes.
- No database reads requiring live mutation.
- No browser automation.
- No carrier API calls.
- No commits, pushes, resets, cleans, stashes, checkouts, or reverts.

Pre-audit worktree baseline captured:

- `git status --short` showed a pre-existing dirty worktree with 50 modified tracked files and multiple untracked Step 2 reports/migrations/tests.
- `git diff --stat` showed `50 files changed, 3471 insertions(+), 322 deletions(-)`.

## 3. Source Evidence

Primary baseline report:

- `docs/architecture/SHOPIXY_SHIPPING_ADMIN_AND_CARRIER_FORENSIC_AUDIT_2026-08-11.md`

Carrier identity and registry:

- `PointOfSale.Core/Constants/CarrierCodes.cs`
- `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs`
- `PointOfSale.API/Program.cs`

Legacy carrier configuration:

- `PointOfSale.Core/Entities/CarrierConfiguration.cs`
- `PointOfSale.API/Controllers/AdminCarrierConfigController.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierCredentialEncryptionService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

New carrier administration model:

- `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`
- `PointOfSale.Core/Interfaces/ShippingAdministration/CarrierAccountRuntimeContracts.cs`
- `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
- `PointOfSale.API/Controllers/AdminShippingAdministrationController.cs`
- `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs`
- `PointOfSale.API/DTOs/ShippingAdministration/ShippingAdministrationDtos.cs`

Runtime carrier execution:

- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.Infrastructure/Shipping/ShippingQuoteService.cs`
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.Infrastructure/Consumers/ShipmentExternalRegistrationRequestedConsumer.cs`
- `PointOfSale.API/Consumers/OrderShipmentOrchestrationConsumer.cs`
- `PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs`
- `PointOfSale.Infrastructure/Shipping/BackgroundJobs/TrackingSyncJob.cs`
- `PointOfSale.API/Controllers/ShippingWebhooksController.cs`

Checkout and storefront admission:

- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs`

Admin UI:

- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js`
- `PointOfSale.Web/src/modules/shipping/components/CarrierConfigurationModal.jsx`
- `PointOfSale.Web/src/modules/shipping/hooks/useShippingSetupApi.js`
- `PointOfSale.Web/src/modules/shipping/pages/ShippingSetupPage.jsx`

Carrier adapters:

- `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/BostaAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/AramexAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/MylerzAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs`

Local precedent for capability-driven provider admin:

- `PointOfSale.API/DTOs/Admin/PaymentProviderAdministrationDtos.cs`
- `PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs`

## 4. Current Architecture

### Preserved Checkout Architecture

The hardened checkout architecture should not be redesigned in Step 2K:

- `ShippingMethod.CarrierCode` is the canonical carrier identity.
- `ShippingMethod.FulfillmentWarehouseId` is the method's fulfillment origin.
- `WarehouseResolution` is the checkout-time server-authoritative warehouse audit record.
- Final checkout validates the warehouse resolution server-side.
- Inventory reservation and `Order.WarehouseId` / `OrderItem.WarehouseId` are derived from the locked warehouse resolution.
- The browser does not submit authoritative warehouse selection.

Evidence:

- `ShippingMethodsController.CreateShippingMethod` and `UpdateShippingMethod` normalize carrier code, validate service level, resolve display name from the registry, and persist `FulfillmentWarehouseId`.
- `ShippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync` admits methods through active method, configured warehouse, active warehouse, stock readiness, and external readiness for non-manual carriers.
- `WarehouseResolutionService.ResolveForSelectedShippingMethodAsync` and `ValidateSubmittedResolutionAsync` reload method authority from SQL and require the resolved warehouse to match `ShippingMethod.FulfillmentWarehouseId`.
- `CheckoutService` uses the final resolution warehouse for inventory reservation and order/item warehouse propagation.

### Legacy Runtime Integration

`CarrierConfiguration` is the runtime integration record:

- Written by `AdminCarrierConfigController.UpsertConfiguration`.
- Validated by `AdminCarrierConfigController.ValidateCredentials`.
- Read by `ShippingQuoteService.GetQuotesAsync`.
- Read by `CarrierShipmentService` for create, existing-shipment registration, cancellation, tracking, and webhook trust.
- Read by `ShipmentService.CreateShipmentAsync` as an external-carrier enabled gate.
- Read by `CheckoutService.ValidateShippingMethodAsync`.
- Read by `ShippingMethodService` and `WarehouseResolutionService` for storefront external-carrier admission.
- Read by `CommerceHubShippingService` for shipping option eligibility.

### New Carrier Administration

`CarrierAccount` is the newer admin lifecycle/readiness model:

- Written by `CarrierAccountAdministrationRuntime`.
- Exposed through `AdminShippingAdministrationController` and `ShippingAdministrationApiService`.
- Shown in `CarrierAccountCard`.
- Used by storefront preview/select admission only through readiness joins with `CarrierWarehouseAssignment`.
- Not used by `CarrierShipmentService` or `ShippingQuoteService` as the credential source.

### Existing Admin UI Split

There are currently two admin carrier setup surfaces:

- Legacy shipping setup UI:
  - `CarrierConfigurationModal.jsx`
  - `useShippingSetupApi.js`
  - writes `/api/admin/carriers`
  - writes `CarrierConfiguration`
- New shipping administration UI:
  - `CarrierAccountCard.jsx`
  - `shippingAdministrationApi.js`
  - writes `/api/admin/shipping/carrier-accounts`
  - writes `CarrierAccount`, `CarrierCredentialReference`, `CarrierWarehouseAssignment`, policies, service levels, and connection-test records

The Shipping Methods UI is already conceptually separate and close to the intended boundary.

## 5. Credential Authority Map

### CarrierConfiguration

Writer:

- `AdminCarrierConfigController.UpsertConfiguration`

Readers:

- `AdminCarrierConfigController.ValidateCredentials`
- `ShippingQuoteService.GetQuotesAsync`
- `CarrierShipmentService` create/register/cancel/tracking/webhook paths
- `ShipmentService.CreateShipmentAsync`
- `CheckoutService.ValidateShippingMethodAsync`
- `ShippingMethodService` preview/admission helpers
- `WarehouseResolutionService.ValidateSelectedShippingMethodAuthorityAsync`
- `CommerceHubShippingService.LoadStoreShippingOptionsAsync`
- background/event paths indirectly through `ICarrierShipmentService`

Persisted fields:

- `ApiKey`
- `ApiSecret`
- `AccountId`
- `ApiUrl`
- `UseSandbox`
- `Configuration` JSON
- `WebhookUrl`
- `WebhookSecret`
- `IsEnabled`
- capability flags and older pricing/default-service fields

Security:

- `ApiKey` and `ApiSecret` are encrypted by `CarrierCredentialEncryptionService` when saved through `AdminCarrierConfigController.UpsertConfiguration`.
- `CarrierCredentialEncryptionService` uses ASP.NET Core Data Protection with purpose `Shipping.CarrierCredentials.Secrets` and `ENC:` prefix.
- Decryption returns legacy plaintext unchanged for backward compatibility.
- `CarrierShipmentService.BuildCredentials` decrypts `ApiKey` and `ApiSecret` in memory.
- `AdminCarrierConfigController.ValidateCredentials` decrypts `ApiKey` and `ApiSecret` in memory.
- `ShippingQuoteService.BuildCredentials` currently does not inject/use `ICarrierCredentialEncryptionService` and therefore appears to pass stored values directly to adapters.
- `WebhookSecret` is used by `CarrierShipmentService` for webhook trust but no encryption/write path was found in the inspected admin config endpoint.

### CarrierAccount / CarrierCredentialReference

Writers:

- `CarrierAccountAdministrationRuntime.RegisterCarrierAccountAsync`
- `UpdateCredentialReferenceAsync`
- `SelectEnvironmentAsync`
- `AssignWarehouseAsync`
- `SetPolicyAsync`
- `SetServiceLevelAsync`
- `RunConnectionTestAsync`
- `TransitionStateAsync`
- `EvaluateHealthAsync`

Readers:

- `ShippingAdministrationApiService.MapAccountAsync`
- `ShippingMethodService.GetReadyExternalCarrierWarehousePairsAsync`
- `ShippingMethodService.ApplyWarehouseAssignmentFilterAsync`
- `WarehouseResolutionService.ValidateSelectedShippingMethodAuthorityAsync`
- tests/certification checks

Persisted fields:

- `CarrierAccount.CarrierCode`
- `CarrierAccount.Environment`
- `CarrierAccount.ActiveCredentialReferenceId`
- lifecycle, operational, and health states
- `CarrierCredentialReference.SecretReference`
- `CarrierCredentialReference.MaskedFingerprint`
- `CarrierWarehouseAssignment.WarehouseId`
- `CarrierWarehouseAssignment.OriginReadinessPublished`
- policies, service-level enablement, connection-test records, and audit rows

Security:

- `CarrierCredentialReference.SecretReference` is a reference string, not secret material.
- `CarrierCredentialReferenceDto` intentionally excludes `SecretReference`.
- `ConnectionTestCommand` accepts plaintext credential material but only the connection-test status/failure record is persisted.
- No repository evidence was found for resolving `SecretReference` to `CarrierCredentials`.
- No repository evidence was found that runtime shipment, quote, tracking, cancellation, or webhook processing consumes `CarrierCredentialReference`.

## 6. Readiness Authority Map

Current duplicated readiness states:

- `CarrierConfiguration.IsEnabled`
- `CarrierConfiguration.LastValidationSucceeded`
- `CarrierAccount.State`
- `CarrierAccount.OperationalState`
- `CarrierAccount.HealthState`
- `CarrierConnectionTestRecord.Status`
- `CarrierWarehouseAssignment.OriginReadinessPublished`
- `CarrierCredentialReference.State`

Current runtime/admission usage:

- Quote generation uses `CarrierConfiguration.IsEnabled`.
- Shipment creation API uses `CarrierConfiguration.IsEnabled`.
- Carrier shipment execution requires enabled/matching `CarrierConfiguration`.
- Cancellation and tracking require a `CarrierConfiguration` row, but not always `IsEnabled`.
- Webhook processing requires enabled `CarrierConfiguration` and `SupportsWebhooks`.
- Storefront preview/select for external carriers requires both enabled `CarrierConfiguration` and active/ready `CarrierAccount` plus published `CarrierWarehouseAssignment`.
- Manual Fulfillment bypasses both external readiness systems.

Correct target answer to "Can this merchant use this carrier integration for this shipping method?":

One authority should answer it:

```text
CarrierIntegrationAuthority.CanUseAsync(tenantId, shippingMethodId)
```

That authority should evaluate:

- shipping method active
- canonical carrier supported
- method service level supported by the adapter
- method fulfillment warehouse active
- cart stock readiness from the method warehouse when in checkout
- for manual: no external integration required
- for external: exactly one integration configuration/readiness source

It should not require callers to manually intersect `CarrierConfiguration` and `CarrierAccount` states.

## 7. Carrier-by-Carrier Capability Analysis

### Manual Fulfillment

- Carrier code: `manual`
- Required runtime credentials: none
- Optional credentials: none
- Environment: none
- Connection test: none
- Runtime credential consumer: none
- Shipment creation credential consumer: no-op adapter ignores credentials
- Tracking credential consumer: not supported
- Cancellation credential consumer: no-op success if called
- Quote credential consumer: not supported by adapter; checkout uses ShippingMethod commercial pricing
- Webhook credential requirements: none
- Production capability: yes, as manual/local fulfillment
- CarrierAccount sufficient: not applicable
- Generic CarrierAccount model sufficient: no; Manual should not be in Carrier Accounts
- Carrier-specific metadata needed: no

### Bosta

- Carrier code: `bosta`
- Required runtime credentials:
  - `ApiKey` as API key/token
  - `UseSandbox` to select staging or production base URL
- Optional credentials consumed by source: none found
- Environment: sandbox/production
- Connection test:
  - `BostaAdapter.ValidateCredentialsAsync` calls `GET {baseUrl}/cities` with `Authorization: credentials.ApiKey`
- Shipment creation:
  - `BostaAdapter.CreateShipmentAsync` uses `credentials.ApiKey` and `credentials.UseSandbox`
  - requires recipient district id, or city id plus district name
- Tracking:
  - `BostaAdapter.GetTrackingAsync` uses `credentials.ApiKey` and `credentials.UseSandbox`
- Cancellation:
  - `BostaAdapter.CancelShipmentAsync` uses `credentials.ApiKey` and `credentials.UseSandbox`
- Quote:
  - Adapter uses environment; source evidence shows no API secret/account-id/API-url consumption
- Webhook:
  - Adapter parses payload; trust is handled by `CarrierShipmentService` using `CarrierConfiguration.WebhookSecret` and trust settings
- Production capability: partially integrated and likely usable if geography mapping and credentials are valid
- CarrierAccount sufficient today: no, because it does not persist API key/token or resolve `SecretReference`
- Generic CarrierAccount model sufficient: no; API secret, account ID, and API URL are irrelevant for Bosta in current source
- Carrier-specific credential metadata needed: yes, to expose only environment and API key/token

### Aramex

- Carrier code: `aramex`
- Required runtime credentials:
  - `ApiKey` as username
  - `ApiSecret` as password
  - `AccountId` as account number
  - `ExtraConfig.AccountPin`
  - `ExtraConfig.AccountEntity`
  - `ExtraConfig.AccountCountryCode`
  - `UseSandbox`
- Optional credentials: no optional credential evidence found for core runtime
- Environment: sandbox/production
- Connection test:
  - `AramexAdapter.ValidateCredentialsAsync` validates client info and calls a fetch-countries API path
- Shipment creation:
  - uses `BuildClientInfo(credentials)`
- Tracking:
  - uses `BuildClientInfo(credentials)`
- Cancellation:
  - method exists; evidence suggests not implemented as a real cancellation call
- Quote:
  - uses `BuildClientInfo(credentials)`
- Webhook:
  - Adapter parses payload; trust handled by `CarrierConfiguration.WebhookSecret`/trust settings
- Production capability: partially integrated
- CarrierAccount sufficient today: no, because persistent extra config fields are absent except generic `SecretReference`
- Generic CarrierAccount model sufficient: no; Aramex needs explicit extra fields
- Carrier-specific credential metadata needed: yes

### DHL

- Carrier code: `dhl`
- Required runtime credentials:
  - `ApiKey`
  - `ApiSecret`
  - `AccountId`
  - `UseSandbox`
- Optional credentials: none found for core runtime
- Environment: sandbox/production
- Connection test:
  - `DhlAdapter.ValidateCredentialsAsync` performs local required-field validation and returns account name; no remote validation evidence was found
- Shipment creation:
  - uses required credentials and Basic Auth
- Tracking:
  - uses required credentials and Basic Auth
- Cancellation:
  - uses required credentials if real external state exists
- Quote:
  - uses required credentials and Basic Auth
- Webhook:
  - Adapter parses payload; trust handled by `CarrierConfiguration.WebhookSecret`/trust settings
- Production capability: partial; runtime code exists but validation is not a proven remote test
- CarrierAccount sufficient today: no, because it does not persist the actual credential payload
- Generic CarrierAccount model sufficient: partially, but UI should still be explicit: API key, API secret, account ID, environment
- Carrier-specific credential metadata needed: yes

### Mylerz

- Carrier code: `mylerz`
- Required runtime credentials:
  - Intended bearer API key, optional API secret, optional account ID, environment
  - Current adapter fails closed because `OfficialPublicContractUnavailable()` returns true
- Optional credentials: `ApiSecret`, `AccountId` appear optional in header helper
- Environment: sandbox/production if ever enabled
- Connection test: fails with official contract unavailable
- Shipment creation/tracking/cancellation/quote: fail closed while official contract unavailable
- Webhook: fail/none while official contract unavailable
- Production capability: no
- CarrierAccount sufficient today: no
- Generic CarrierAccount model sufficient: no, because the carrier should be shown as unavailable/not production-ready
- Carrier-specific credential metadata needed: yes, including production capability state

### FedEx

- Carrier code: `fedex`
- Required runtime credentials when a transport client is enabled:
  - `ApiKey` as client id
  - `ApiSecret` as client secret
  - `AccountId`
  - `UseSandbox`
- Optional credentials: evidence insufficient
- Environment: sandbox/production
- Connection test: fails with `FEDEX_NOT_YET_CERTIFIED`
- Shipment creation/tracking/cancellation/quote: not production-certified; many paths fail closed without transport/certification
- Webhook: no parse support found
- Production capability: no, scaffold/foundation only
- CarrierAccount sufficient today: no
- Generic CarrierAccount model sufficient: no; should be visibly unavailable/not certified
- Carrier-specific credential metadata needed: yes

## 8. CarrierWarehouseAssignment Analysis

Current meaning:

- `CarrierWarehouseAssignment` joins `CarrierAccount` to `Warehouse`.
- It stores `IsEnabled`, `OriginReadinessPublished`, and `ReadinessEvidenceReference`.
- It is used by `ShippingMethodService` and `WarehouseResolutionService` as a hard storefront external-carrier readiness gate.
- It is not used by `CarrierShipmentService` to choose the origin warehouse.
- Shipment origin comes from `Order.WarehouseId`, which comes from `WarehouseResolution`, which comes from `ShippingMethod.FulfillmentWarehouseId`.

Separated concepts:

1. Fulfillment warehouse authority:
   - `ShippingMethod.FulfillmentWarehouseId`
   - `WarehouseResolution`
   - `Order.WarehouseId`
   - inventory reservation
2. Carrier origin/readiness metadata:
   - possibly `CarrierWarehouseAssignment`
   - only relevant for carriers that require origin publication, pickup readiness, warehouse/carrier account enrollment, or provider-origin mapping
3. Carrier-specific warehouse mapping:
   - geography mapping exists elsewhere and is used by `CarrierShipmentService.ResolveOrRestoreCarrierGeographyAsync`
   - no evidence that `CarrierWarehouseAssignment` itself provides provider warehouse IDs
4. Pickup/origin publication:
   - `OriginReadinessPublished` represents this concept, but it is currently generic and required for every external carrier
5. External account readiness:
   - currently tied to `CarrierAccount.State == Active` and `OperationalState == Ready`

Recommendation:

`CarrierWarehouseAssignment` should be **kept but reclassified**.

It should become optional carrier-specific origin/readiness metadata, evaluated only when carrier capability metadata says the carrier requires origin readiness publication for the selected method warehouse. It must not be the checkout warehouse authority and should not be a universal hard gate for all external carriers.

Do not delete it now:

- It already has tenant-safe FK mapping.
- It may be needed for carrier origin-readiness evidence.
- The repository contains tests/certifications around keeping assignment at the carrier-account boundary.
- Deletion would require schema and data migration without evidence that every carrier can ignore origin readiness.

## 9. Credential Security Analysis

What is encrypted:

- `CarrierConfiguration.ApiKey` and `CarrierConfiguration.ApiSecret` are encrypted by `AdminCarrierConfigController.UpsertConfiguration` through `ICarrierCredentialEncryptionService`.
- Decryption happens in memory in `AdminCarrierConfigController.ValidateCredentials` and `CarrierShipmentService.BuildCredentials`.

What is not clearly encrypted:

- `CarrierConfiguration.AccountId`
- `CarrierConfiguration.ApiUrl`
- `CarrierConfiguration.Configuration`
- `CarrierConfiguration.WebhookSecret`

`AccountId` and `ApiUrl` are usually identifiers/configuration, not always secrets. `Configuration` can contain sensitive carrier-specific fields such as Aramex account pin. The current model stores it as JSON text. The remediation should treat carrier-specific credential fields inside `Configuration` as sensitive and should not expose them back to the frontend.

CarrierCredentialReference behavior:

- Stores `SecretReference`, masked fingerprint, state, expiry.
- DTOs omit `SecretReference` and return only masked metadata.
- No source evidence found for a resolver that turns `SecretReference` into plaintext credentials.
- Connection-test plaintext credentials are submitted to backend but not persisted as credential payload.

Secret exposure:

- `AdminCarrierConfigController.MapToDto` does not return API key or API secret values.
- `ShippingAdministrationApiService.MapAccountAsync` does not return `SecretReference`.
- Logs in inspected shipping runtime paths report carrier code, tenant id, error codes, and API response bodies in some adapter failures. Adapter response body logging should be reviewed separately, but source evidence did not show intentional logging of API key/API secret values.

Migration safety:

- Do not move secrets into plaintext columns.
- Do not return stored secrets to the browser for migration.
- Do not require decrypting all secrets client-side.
- A server-side provider can decrypt legacy `CarrierConfiguration` secrets in memory and pass `CarrierCredentials` to adapters.
- A future secret-store migration requires a server-side secret writer/resolver. That does not exist today for carrier accounts.

## 10. Backward Compatibility Analysis

Greenfield:

- New tenants can use the target Carrier Integration UI while the provider writes the existing encrypted `CarrierConfiguration` backing row.
- Manual Fulfillment requires no integration setup.

Existing tenants:

- Existing tenants may already have valid `CarrierConfiguration` rows because the table exists from the baseline migration and legacy UI/API still manage it.
- Deleting or bypassing `CarrierConfiguration` would break quote generation, shipment creation, cancellation, tracking, webhooks, checkout gates, and CommerceHub eligibility.

Backfill:

- A non-destructive backfill may create missing `CarrierAccount` rows for existing external `CarrierConfiguration` rows.
- It should not move or expose secret material.
- It may set a redacted credential reference such as an internal legacy reference, but that reference must not become runtime-authoritative unless it is resolvable.

Dual-write:

- Direct dual-write from CarrierAccount and legacy config is riskier unless every read goes through one provider.
- If dual-write is used during transition, it must be implementation detail behind the single authority provider/facade.

Dual-read:

- The provider may read existing `CarrierConfiguration` first for runtime credentials.
- A future version may read CarrierAccount-backed secret store first and fall back to `CarrierConfiguration` during cutover.
- Callers should not perform dual-read themselves.

Read-through fallback:

- Recommended for transition:
  - Provider resolves credentials/readiness from `CarrierConfiguration`.
  - If a future resolvable CarrierAccount secret store is added, provider can read it first and fall back to legacy config.

Cutover:

- Move each runtime caller to the provider.
- Move each admin caller to the provider/facade.
- Remove direct storefront intersections of `CarrierConfiguration` and CarrierAccount readiness after equivalent provider tests pass.

Deprecation:

- Keep `/api/admin/carriers` temporarily for existing UI/backward compatibility.
- Mark it as legacy and route it through the same provider/facade.
- Hide or replace the legacy generic modal in favor of capability-driven Carrier Integration UI.

Eventual removal:

- Remove direct CarrierConfiguration entity access only after all reads/writes go through the provider and a real CarrierAccount secret resolver or equivalent backing store exists.
- Do not remove the table in Step 2K remediation.

Required migration:

- No required schema migration for the minimal provider-backed transition.
- Optional data backfill for CarrierAccount rows can be done later.
- Secret re-encryption is not required for the minimal transition.

Rollback:

- Because `CarrierConfiguration` remains the backing store, rollback can restore direct legacy reads with no data loss if the provider implementation is faulty.

## 11. Option A Analysis

Option A: migrate runtime shipment/quote execution from `CarrierConfiguration` to CarrierAccount credential resolution.

Pros:

- Conceptually clean final state.
- CarrierAccount would be the true admin and runtime authority.
- Removes legacy runtime dependency eventually.

Repository blockers:

- `CarrierAccount` does not store API key/token, API secret, account ID, API URL, webhook secret, or carrier-specific `ExtraConfig`.
- `CarrierCredentialReference.SecretReference` has no resolver implementation in the repository.
- Connection-test plaintext credentials are not persisted as runtime credentials.
- Aramex needs `ExtraConfig.AccountPin`, `AccountEntity`, and `AccountCountryCode`; CarrierAccount cannot represent those values today except as an opaque unresolved reference.
- Webhook trust uses `CarrierConfiguration.WebhookSecret` and JSON trust settings; CarrierAccount has no equivalent runtime trust material.
- Quote generation, shipment execution, tracking, cancellation, webhook processing, async retries, and CommerceHub eligibility currently read `CarrierConfiguration`.

Conclusion:

Option A is not safe as the next minimal remediation. It would require secret-store design, schema/API changes, backfill, runtime rewiring, and webhook trust migration. That is larger than Step 2K's minimality requirement and risks breaking existing tenants.

## 12. Option B Analysis

Option B: keep `CarrierConfiguration` temporarily as the runtime store, but make CarrierAccount administration authoritative and synchronize/update `CarrierConfiguration` from CarrierAccount.

Pros:

- Preserves existing runtime paths.
- Can keep existing encrypted secrets.
- Moves merchant experience toward Carrier Account UI.

Problems:

- CarrierAccount currently receives only credential references for persistence, not secret payload.
- Connection-test credentials are transient and not stored.
- Without a secret resolver, CarrierAccount cannot authoritatively write all runtime credentials to `CarrierConfiguration`.
- Direct legacy UI still writes `CarrierConfiguration`, so authority can still drift.
- Dual-write can create two competing sources unless all reads are centralized.

Conclusion:

Option B is useful as an implementation tactic behind a provider/facade, but it is not sufficient as the architecture. By itself it preserves duplicated authority.

## 13. Option C Analysis

Option C: introduce a single credential/readiness abstraction/provider so runtime code no longer knows whether credentials come from legacy `CarrierConfiguration` or future CarrierAccount secret storage.

Pros:

- Minimal runtime disruption.
- Preserves existing encrypted secrets.
- Preserves existing tenants.
- Allows callers to stop manually intersecting `CarrierConfiguration` and CarrierAccount readiness.
- Makes quote, shipment, tracking, cancellation, webhook, checkout, CommerceHub, and storefront admission use the same authority.
- Allows future migration to a CarrierAccount secret store without touching every runtime caller again.
- Keeps Manual Fulfillment external-readiness-free.
- Does not alter warehouse authority.

Required design:

- Introduce one provider/facade, conceptually:
  - `ICarrierIntegrationAuthority`
  - `ResolveCredentialsAsync(tenantId, carrierCode, purpose)`
  - `GetIntegrationReadinessAsync(tenantId, carrierCode, shippingMethodId?, warehouseId?, purpose)`
  - `ValidateAndStoreIntegrationAsync(...)`
  - `ResolveWebhookTrustAsync(tenantId, carrierCode)`
- Initial backing store:
  - `CarrierConfiguration`, decrypted through `ICarrierCredentialEncryptionService`.
- CarrierAccount:
  - admin lifecycle/readiness/audit facade
  - optionally synchronized/read-model status
  - not a separate runtime credential source until secret resolution exists
- CarrierWarehouseAssignment:
  - optional origin-readiness metadata only when carrier capability requires it

Conclusion:

Option C is the safest minimal architecture.

## 14. Alternative Architecture Analysis if needed

Alternative D: delete CarrierAccount and return to only `CarrierConfiguration`.

Rejected:

- It would discard the newer admin/readiness/audit model.
- It would not solve UX capability problems by itself.
- It would weaken the future expansion path.
- It would require removing code/tests and UI that already model carrier-account lifecycle.

Alternative E: delete CarrierConfiguration immediately and replace it with new CarrierAccount credential columns.

Rejected:

- It requires schema migration, secret migration, webhook trust migration, quote/shipment rewiring, and data backfill.
- It risks breaking existing tenants.
- It conflicts with minimality.

Alternative F: leave current architecture as-is and only simplify the UI.

Rejected:

- It would not eliminate duplicate authority.
- Storefront could still be blocked by one store while runtime uses another.

## 15. Recommended Architecture

**RECOMMENDED ARCHITECTURE: Option C - Single Carrier Integration Authority Provider with `CarrierConfiguration` as the transitional encrypted backing store.**

Why it is safest:

- It preserves the only credential store that runtime can actually use today.
- It avoids inventing a secret resolver that does not exist.
- It avoids exposing or migrating plaintext secrets.
- It lets every runtime/admission path ask one authority.

Why it is minimal:

- No immediate schema migration is required.
- Existing encrypted `CarrierConfiguration` rows remain valid.
- Existing adapters still receive the same `CarrierCredentials` object.
- Checkout warehouse authority is untouched.

Why it preserves backward compatibility:

- Existing tenants with `CarrierConfiguration` rows continue to work.
- Legacy endpoints can be kept as compatibility shims.
- Async carrier registration, retries, cancellation, tracking, and webhook processing can use the provider while still resolving from legacy rows.

Why it eliminates duplicate authority:

- Storefront admission, checkout validation, quote runtime, shipment runtime, tracking, cancellation, and webhooks use the same provider decision.
- CarrierAccount state cannot independently green-light or block runtime unless the provider includes it as carrier-specific metadata for the requested purpose.

Why it does not destabilize checkout:

- It does not change `ShippingMethod.FulfillmentWarehouseId`.
- It does not change `WarehouseResolution`.
- It does not let the browser submit warehouse authority.
- It does not change inventory reservation or order warehouse propagation.

Why it works for Bosta:

- Provider-backed Bosta config needs only environment plus API key/token for credentials.
- Bosta-specific UI can hide API secret, account ID, API URL, and unrelated extra config.
- Provider can still enforce Bosta geography readiness separately where shipment creation actually requires district/city evidence.

How it extends to Aramex/DHL:

- Carrier capability metadata can specify Aramex's required credential fields and DHL's required fields.
- The provider can map those fields into the existing `CarrierCredentials` contract.
- Existing adapters do not need a credential-contract rewrite.

What happens to CarrierConfiguration:

- It remains the physical encrypted runtime backing store in the first remediation phase.
- Direct ad hoc reads should be replaced by provider calls.
- Direct legacy writes should be routed through the provider/facade.
- It is deprecated as a public/domain authority, not deleted.

What happens to CarrierAccount:

- It remains the merchant-facing integration account/lifecycle/audit concept for external carriers.
- Manual Fulfillment remains excluded.
- It becomes an admin facade/read model over the single integration authority, not a parallel runtime credential source.

What happens to CarrierCredentialReference:

- It remains redacted metadata until a real secret resolver exists.
- It must not be used alone as proof that runtime credentials exist.
- If retained during the transitional phase, it should point to a resolvable internal legacy/backing reference or be explicitly labeled as metadata only.

What happens to CarrierWarehouseAssignment:

- It remains carrier-specific origin/readiness metadata.
- It becomes optional/capability-driven.
- It is removed from universal storefront warehouse authority.
- It never selects or overrides the fulfillment warehouse.

What happens to readiness state:

- A provider result becomes the authoritative answer.
- `CarrierConfiguration.IsEnabled`, last validation, CarrierAccount lifecycle, connection tests, and origin readiness become inputs to one provider result, not independent caller-side gates.

What happens to Admin UI:

- Carrier Integration screen becomes capability-driven.
- Bosta sees environment, API key/token, and test connection.
- Manual does not appear as a carrier account.
- Mylerz/FedEx are visible as unavailable/not certified unless explicitly enabled later.

What happens to runtime shipment execution:

- `CarrierShipmentService`, `ShippingQuoteService`, tracking, cancellation, and webhook trust resolve credentials/trust through the provider.
- Adapters still receive `CarrierCredentials`.

## 16. Target Authority Model

### Shipping Method Authority

`ShippingMethod` remains responsible for:

- `carrierCode`
- `serviceLevel`
- name/localized name/description
- base rate
- rate per kg
- free shipping threshold
- delivery estimate
- fulfillment warehouse
- active state
- tracking URL template

`ShippingMethod` must not hold:

- API keys
- API secrets
- account credentials
- carrier environment
- connection-test state
- webhook secrets
- external carrier readiness

### Carrier Integration Authority

The target provider owns:

- enabled/disabled external carrier integration state
- carrier environment
- credential resolution
- adapter credential validation
- webhook trust resolution
- carrier production capability state
- carrier-specific origin readiness only when required

### Warehouse Authority

Unchanged:

```text
ShippingMethod.FulfillmentWarehouseId
  -> WarehouseResolution
  -> Order.WarehouseId / OrderItem.WarehouseId
  -> inventory reservation
```

`CarrierWarehouseAssignment` is not in this chain.

### Manual Fulfillment

Manual path:

```text
ShippingMethod carrierCode = manual
  -> FulfillmentWarehouseId
  -> WarehouseResolution
  -> Order/Inventory
```

No carrier integration authority is required.

## 17. Migration Strategy

Phase 1: centralize reads without changing storage.

- Add a carrier integration authority/provider.
- Provider reads `CarrierConfiguration` and decrypts through `ICarrierCredentialEncryptionService`.
- Provider exposes readiness and credentials to all runtime/admission callers.
- Replace direct reads in quote, shipment, tracking, cancellation, webhook, checkout, storefront, and CommerceHub paths with provider calls.
- Fix quote credential resolution by using the same decryption path.

Phase 2: make admin writes use the same authority.

- Route legacy `/api/admin/carriers` writes through the provider/facade.
- Route new Carrier Account UI writes through the provider/facade.
- Keep old endpoint as compatibility shim.
- Keep responses redacted.

Phase 3: capability-driven UI.

- Add carrier capability metadata.
- Render credential fields by carrier capability.
- Hide Manual from Carrier Accounts.
- Mark Mylerz/FedEx unavailable/not certified.

Phase 4: optional backfill.

- For existing enabled external `CarrierConfiguration` rows, create missing `CarrierAccount` rows as admin read models.
- Do not copy plaintext secrets into CarrierAccount.
- Use redacted metadata only.

Phase 5: future secret-store cutover.

- Only after a real secret resolver/writer exists, provider may read CarrierAccount-backed secrets first.
- Keep `CarrierConfiguration` fallback until operational verification passes.
- Later deprecate/remove direct legacy table usage.

No immediate schema migration is required for the recommended first remediation.

## 18. Admin UX Target

Carrier Integration / Carrier Account screen:

- Manual:
  - not shown as a carrier account
  - no credentials
  - no test connection
  - no warehouse assignment
- Bosta:
  - environment
  - API key/token
  - test connection
  - redacted credential status
  - no API secret/account ID/API URL fields
- Aramex:
  - environment
  - username/API key
  - password/API secret
  - account number
  - account pin
  - account entity
  - account country code
  - test connection
- DHL:
  - environment
  - API key
  - API secret
  - account ID
  - test connection, clearly marked as local validation until remote validation exists
- Mylerz:
  - unavailable/not production-ready because official contract is unavailable
  - do not present as a normal configurable production integration
- FedEx:
  - unavailable/not certified
  - do not present as a normal configurable production integration

Minimum carrier capability metadata:

- `carrierCode`
- `displayName`
- `productionCapability`: production-ready, partial, unavailable, not-certified
- `credentialFields`: key, label, secret/non-secret, required, target mapping (`ApiKey`, `ApiSecret`, `AccountId`, `ApiUrl`, `ExtraConfig[key]`, webhook secret)
- `environmentSupported`
- `connectionTestSupported`
- `connectionTestMode`: remote, local-only, unsupported
- `supportedServiceLevels`
- `requiresOriginReadiness`
- `requiresGeographyMapping`
- `supportsWebhooks`
- `webhookTrustModes`

Where metadata belongs:

- Prefer a separate carrier capability descriptor adjacent to adapters, exposed by the registry.
- Do not overload `IShippingCarrierAdapter` with UI-only labels unless kept as a small runtime capability interface.
- Repository precedent: payment provider administration exposes capability DTOs and redacted secret metadata separately from runtime provider execution.

## 19. Runtime Execution Target

Every external runtime path should resolve through the same provider:

- quote generation
- shipment creation
- existing shipment external registration retry
- cancellation
- tracking sync
- webhook trust
- checkout final shipping method validation
- storefront preview/select external admission
- CommerceHub shipping option eligibility

Provider result should include:

- `CarrierCode`
- `DisplayName`
- `Enabled`
- `Environment`
- `ProductionCapability`
- `CanExecute`
- `Blockers`
- `CarrierCredentials` only for server runtime callers
- `WebhookTrust` only for webhook pipeline
- redacted credential metadata for admin UI

Runtime callers must not receive secrets unless they are server-side execution paths that immediately call adapters.

## 20. Required Implementation Steps

1. Define the carrier integration authority/provider contract.
2. Implement the first provider using `CarrierConfiguration` as backing storage.
3. Move credential construction/decryption into the provider.
4. Fix `ShippingQuoteService` to use provider-decrypted credentials.
5. Replace `CarrierShipmentService` direct `CarrierConfiguration` credential reads with provider calls.
6. Replace cancellation/tracking/webhook trust direct reads with provider calls.
7. Replace `ShipmentService`, `CheckoutService`, `ShippingMethodService`, `WarehouseResolutionService`, and `CommerceHubShippingService` external-carrier gates with provider readiness calls.
8. Preserve Manual Fulfillment bypass.
9. Convert CarrierAccount readiness into provider input only where applicable.
10. Make `CarrierWarehouseAssignment` capability-driven and not universally required.
11. Route legacy carrier config API through the provider/facade.
12. Route new Carrier Account integration actions through the provider/facade.
13. Add carrier capability metadata and expose it through Admin Shipping Administration.
14. Update Carrier Account UI to render credential fields by capability.
15. Hide or de-emphasize legacy `/admin/carriers` UI after compatibility is proven.
16. Add optional non-secret backfill for CarrierAccount rows from existing external configurations.

## 21. Required Tests

Authority tests:

- One provider result drives storefront admission and runtime execution.
- External carrier with provider unavailable is not shown in preview and cannot execute shipment.
- External carrier with provider ready is admitted and shipment execution receives credentials from same provider result.
- No direct `CarrierConfiguration` reads remain in shipping runtime/admission callers except inside provider/repository.

Manual tests:

- Manual preview succeeds with active method, active warehouse, and stock.
- Manual preview/select/final checkout require no CarrierAccount.
- Manual shipment creation requires no CarrierConfiguration.

Bosta tests:

- Bosta credential schema exposes only environment and API key/token.
- Bosta provider maps API key/token to `CarrierCredentials.ApiKey`.
- Bosta provider maps environment to `UseSandbox`.
- API secret/account ID/API URL are not required for Bosta.
- Bosta shipment fails closed when geography identity is missing.

Aramex/DHL tests:

- Aramex required credential fields include account pin/entity/country code.
- DHL required credential fields include API key, API secret, and account ID.
- DHL validation status is marked local-only unless remote validation is added.

Security tests:

- No secret values returned in admin DTOs.
- No plaintext credentials stored in ordinary new columns.
- Provider decrypts only server-side.
- Quote path receives decrypted credentials.
- Webhook trust does not expose webhook secret.

Compatibility tests:

- Existing `CarrierConfiguration` rows still enable runtime execution through the provider.
- Legacy `/api/admin/carriers` endpoint remains compatible.
- Existing async shipment external registration retry works through provider.
- Cancellation and tracking still work through provider.

Warehouse tests:

- Browser cannot submit authoritative warehouse.
- `WarehouseResolution` warehouse must match `ShippingMethod.FulfillmentWarehouseId`.
- `CarrierWarehouseAssignment` cannot override fulfillment warehouse.
- Inventory reservation uses locked resolution warehouse.

## 22. Live Verification Plan

No live verification was performed in this design step.

When implementation is allowed:

1. Use an existing tenant with a legacy Bosta `CarrierConfiguration`.
2. Verify provider reports Bosta ready from that existing config.
3. Verify preview shows Bosta method only when method warehouse and cart stock are valid.
4. Verify selection creates `WarehouseResolution` from `ShippingMethod.FulfillmentWarehouseId`.
5. Complete checkout and verify order/item warehouses and reservations.
6. Trigger external shipment registration and verify adapter credentials came from provider.
7. Run Bosta connection test from new UI with only API key/token and environment.
8. Verify no secret is returned to browser responses.
9. Repeat with Manual Fulfillment and confirm no integration setup exists.
10. Verify Aramex/DHL forms expose only required fields.
11. Verify Mylerz/FedEx are unavailable/not certified.
12. Exercise shipment retry hosted service path on a pending external registration.
13. Exercise tracking sync and cancellation on an external shipment.
14. Exercise shipping webhook trust path for a configured carrier.

## 23. Rollback Strategy

Because the recommended first phase retains `CarrierConfiguration` as backing storage:

- Rollback can restore direct legacy reads if provider implementation fails.
- No data migration rollback is required for phase 1.
- Existing encrypted credentials remain in place.
- Existing legacy admin endpoint can remain available during rollout.
- Feature flagging provider usage is optional but useful for runtime cutover.

If a later CarrierAccount secret-store cutover is attempted:

- Keep read-through fallback to `CarrierConfiguration`.
- Keep old encrypted rows until external shipment creation, quote, cancellation, tracking, and webhook trust are verified.
- Roll back by changing provider precedence back to legacy config.

## 24. Risks

- `ShippingQuoteService` currently appears not to decrypt encrypted `CarrierConfiguration` credentials; provider centralization must fix this.
- `CarrierConfiguration.Configuration` may contain sensitive carrier-specific fields such as Aramex account pin; it should be treated as secret-bearing.
- `WebhookSecret` is used for trust but no clear encrypted write path was found.
- Legacy UI and new Shipping Administration UI both exist and can confuse operators until routed through one authority.
- Mylerz/FedEx are registered adapters but not production-capable; capability metadata must prevent accidental production presentation.
- If CarrierAccount state remains displayed as independent readiness, merchants may still see contradictory status unless UI labels are adjusted.
- Removing the universal `CarrierWarehouseAssignment` gate must be done carefully for carriers that really require origin readiness.

## 25. Explicit Non-Goals

- No Step 2K implementation.
- No schema migration design requiring immediate execution.
- No deletion of `CarrierConfiguration`.
- No deletion of `CarrierAccount`.
- No migration of secrets to plaintext columns.
- No browser exposure of secrets.
- No checkout warehouse authority redesign.
- No dynamic browser-selected warehouse selection.
- No merge of carrier credentials into `ShippingMethod`.
- No rewrite of carrier adapters.
- No payment-domain changes.
- No order/inventory model changes.

## 26. Final Certification

**DESIGN READY**

Repository evidence is sufficient to choose a safe implementable architecture:

- Direct CarrierAccount runtime authority is not currently possible because no secret resolver or persisted credential payload exists.
- `CarrierConfiguration` is the only proven runtime credential/trust source and must be preserved for compatibility.
- A single provider/facade can remove caller-side duplicate authority while preserving existing encrypted secrets and existing runtime behavior.
- Checkout warehouse authority remains untouched.
- Manual Fulfillment remains free of carrier accounts, credentials, connection tests, and external readiness.

The recommended architecture is:

```text
ShippingMethod
  carrierCode
  serviceLevel
  FulfillmentWarehouseId
    -> WarehouseResolution
    -> Order / Inventory

External carriers only:
  CarrierIntegrationAuthority
    -> transitional backing store: CarrierConfiguration
    -> admin facade/read model: CarrierAccount
    -> optional carrier-specific origin metadata: CarrierWarehouseAssignment
    -> adapter credentials: CarrierCredentials
```

This design eliminates duplicate runtime/admission authority by making all callers use one provider result, while avoiding an unnecessary shipping rewrite.
