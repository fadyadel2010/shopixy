# SHOPIXY Shipping Admin And Carrier Forensic Audit

Date: 2026-08-11

Scope: forensic audit only. No source, schema, migration, configuration, test, frontend, backend, API, database, or git-state mutations were performed for this report.

## 1. Executive Summary

The current live checkout path now displays shipping methods and correctly derives warehouse authority from `ShippingMethod.FulfillmentWarehouseId` through `WarehouseResolution`. The browser does not submit a warehouse as authority during final checkout; it submits a `WarehouseResolutionId` and version, and the server revalidates the selected shipping method, warehouse, cart hash, and stock before reservation and order creation.

The architecture is not yet cleanly settled because carrier integration authority is duplicated:

- `CarrierConfiguration` is the legacy runtime integration record that stores the encrypted credentials actually used by quote/shipment runtime code.
- `CarrierAccount` is the newer administration/readiness model that drives Admin Shipping Administration and storefront admission for external carriers.
- `CarrierAccount.CredentialReference` stores only a secret reference and masked fingerprint; the runtime shipment code does not consume it.
- Storefront external-carrier admission currently requires both legacy `CarrierConfiguration` and new `CarrierAccount`/`CarrierWarehouseAssignment` readiness, while external shipment creation uses only `CarrierConfiguration`.

Manual Fulfillment is mostly modeled correctly now. The backend rejects Manual Fulfillment carrier accounts and warehouse assignments, the admin Carrier Accounts screen filters Manual Fulfillment out, and the preview/select checkout path does not require carrier credentials, connection tests, external readiness, or `CarrierWarehouseAssignment` for manual methods. A Manual Fulfillment shipping method is usable when it is active, has a valid fulfillment warehouse, the warehouse is active, pricing is valid, and the cart can be fulfilled from that warehouse.

Bosta is over-configured by the current generic Carrier Account UI. The runtime Bosta adapter consumes an API key/token and environment selection. It does not consume API secret, account ID, API URL, or extra config for credential validation, shipment creation, cancellation, or tracking. Bosta shipment creation also requires address/geography information: a district id, or a city id plus district name. The current admin UI still presents unrelated credential fields because it is generic rather than capability-driven.

Final certification: **BLOCKED**. The live checkout warehouse authority is substantially hardened, but architectural contradictions remain around external carrier integration authority, readiness gates, and duplicated warehouse/carrier configuration.

## 2. Current Architecture Map

### Admin UI To Carrier Account

- UI entry: `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
  - Tabs include Overview, Carrier Accounts, Shipping Methods, Policies, Warehouses, Health, and Audit.
- Carrier account UI: `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
  - Shows environment, credential reference, connection test credentials, warehouse assignment, service levels, policies, lifecycle actions, health, and audit actions.
  - The form is generic for all external carriers.
- Client API: `PointOfSale.Web/src/modules/shipping-administration/services/shippingAdministrationApi.js`
  - Calls `/api/admin/shipping/supported-carriers`, `/api/admin/shipping/carrier-accounts`, credential-reference, connection-test, warehouse-assignment, service-level, policy, and lifecycle endpoints.
- Backend API: `PointOfSale.API/Controllers/AdminShippingAdministrationController.cs`
  - Route: `/api/admin/shipping`.
  - DTOs include `SupportedCarrierSummaryDto`, `CarrierAccountDto`, `CreateCarrierAccountRequest`, `UpdateCarrierCredentialReferenceRequest`, `RunCarrierConnectionTestRequest`, `UpsertCarrierWarehouseAssignmentRequest`, and service-level/policy requests.
- API service: `PointOfSale.API/Services/ShippingAdministration/ShippingAdministrationApiService.cs`
  - `ListSupportedCarriersAsync` exposes registry adapters and marks Manual Fulfillment as not account-required and not readiness-required.
  - `CreateCarrierAccountAsync` rejects `manual`.
- Runtime service: `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
  - `RegisterCarrierAccountAsync` rejects Manual Fulfillment with `MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED`.
  - `AssignWarehouseAsync` rejects Manual Fulfillment.
  - `RunConnectionTestAsync` calls the registered carrier adapter with credentials submitted for the test.
  - `EvaluateActivationGatesAsync` computes blockers and warnings.
- Entities: `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`
  - `CarrierAccount`
  - `CarrierCredentialReference`
  - `CarrierWarehouseAssignment`
  - `CarrierAccountPolicy`
  - `CarrierServiceLevelEnablement`
  - `CarrierConnectionTestRecord`
  - `CarrierAdministrationAudit`

### Carrier Registry

- Constants: `PointOfSale.Core/Constants/CarrierCodes.cs`
  - Canonical carrier codes: `manual`, `bosta`, `aramex`, `mylerz`, `dhl`, `fedex`.
  - Empty or null carrier code normalizes to `manual`.
- Registry interface and implementation:
  - `PointOfSale.Infrastructure/Shipping/IShippingCarrierRegistry.cs`
  - `PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs`
  - Registry maps registered `IShippingCarrierAdapter` implementations by canonical `CarrierCode`.
- Adapter contract: `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs`
  - Methods: `GetQuoteAsync`, `CreateShipmentAsync`, `CancelShipmentAsync`, `GetTrackingAsync`, `ValidateCredentialsAsync`, `GetSupportedServiceLevelsAsync`, `ParseWebhookAsync`.
  - Credential contract: `CarrierCredentials` with `ApiKey`, `ApiSecret`, `AccountId`, `ApiUrl`, `UseSandbox`, and `ExtraConfig`.
- DI registration: `PointOfSale.API/Program.cs`
  - Registers `ManualCarrierDefinition`, `BostaAdapter`, `AramexAdapter`, `MylerzAdapter`, `DhlAdapter`, and `FedExAdapter`.

### Carrier Credentials

There are two credential paths:

1. Legacy runtime credentials:
   - Entity: `PointOfSale.Core/Entities/CarrierConfiguration.cs`
   - API: `PointOfSale.API/Controllers/AdminCarrierConfigController.cs`
   - Runtime consumption:
     - `PointOfSale.Infrastructure/Services/CarrierShipmentService.cs`
     - `PointOfSale.Infrastructure/Services/ShippingQuoteService.cs`
   - Stores encrypted API key/secret and account metadata.

2. New Carrier Account credential references:
   - Entity: `CarrierCredentialReference`
   - Contract: `CarrierConnectionTestCredentials`
   - Runtime: `CarrierAccountAdministrationRuntime.RunConnectionTestAsync`
   - Stores secret reference and masked fingerprint, not credential payload.
   - Evidence was not found that shipment creation consumes `CarrierCredentialReference`.

### Carrier Readiness

- New readiness source: `CarrierAccountAdministrationRuntime.EvaluateActivationGatesAsync`
  - Blocks activation for missing credential reference, missing environment, missing connection test, missing warehouse assignment, origin readiness missing, readiness problems, international customs readiness, unsupported carrier/provider capabilities, and unsupported enabled service levels.
- Storefront external admission source:
  - `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
  - `GetReadyExternalCarrierWarehousePairsAsync` requires:
    - legacy `CarrierConfiguration.IsEnabled`
    - `CarrierWarehouseAssignment.IsEnabled`
    - `CarrierWarehouseAssignment.OriginReadinessStatus == Published`
    - `CarrierAccount.LifecycleStatus == Active`
    - `CarrierAccount.OperationalState == Ready`
- Checkout selected-method validation source:
  - `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
  - `ValidateSelectedShippingMethodAuthorityAsync` applies the same external readiness pattern for non-manual methods.

### Warehouse Assignment And Shipping Method Warehouse

There are two warehouse relationships:

1. `ShippingMethod.FulfillmentWarehouseId`
   - Entity: `PointOfSale.Core/Entities/ShippingMethod.cs`
   - Migration: `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`
   - Used by storefront preview, method selection, warehouse resolution, checkout, inventory reservation, and order warehouse propagation.

2. `CarrierWarehouseAssignment`
   - Entity: `CarrierWarehouseAssignment`
   - Migration: `20260811150644_SHOPIXY_WarehouseResolutionAuthority`
   - Used as external-carrier readiness metadata and storefront admission gate.
   - Not the final warehouse authority for checkout.

### Shipping Method

- Admin API: `PointOfSale.API/Controllers/ShippingMethodsController.cs`
  - Create/update validates canonical carrier code through registry.
  - Rejects unknown carriers.
  - Ignores submitted carrier display text for persistence and resolves `CarrierName` from the registry.
  - Validates service level against adapter-supported service levels.
  - Validates optional `FulfillmentWarehouseId` as an active same-tenant warehouse.
- Entity: `PointOfSale.Core/Entities/ShippingMethod.cs`
  - Commercial and display fields: name, localized name, description, base rate, per-kg rate, free shipping threshold, estimated min/max days, sort order, active state, available countries, tracking URL template.
  - Carrier identity fields: `CarrierName`, `CarrierCode`, `ServiceLevel`.
  - Warehouse authority field: `FulfillmentWarehouseId`.
- Projection: `PointOfSale.Infrastructure/Services/MongoProjections/ShippingMethodProjectionConsumer.cs`
  - Refetches SQL source of truth and maps `FulfillmentWarehouseId`, carrier, pricing, and display fields into Mongo document.

### Storefront Preview And Selection

- Storefront API: `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
  - `POST /api/storefront/shipping-methods/preview`
    - Loads checkout-session-owned cart through `WarehouseResolutionService.LoadPreviewCartAsync`.
    - Calls `ShippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync`.
  - `POST /api/storefront/shipping-methods/select`
    - Calls `WarehouseResolutionService.ResolveForSelectedShippingMethodAsync`.
    - Returns `WarehouseResolutionId`, version, status, and expiry.
- Storefront client: `PointOfSale.Storefront/src/store/checkoutStore.ts`
  - Preview requests are deduplicated/cancelled by request key and abort controller.
  - Shipping selection stores `warehouseResolutionId`, `warehouseResolutionVersion`, status, and expiry.
  - Final checkout submits `checkoutSessionId`, `shippingMethodId`, `warehouseResolutionId`, and `warehouseResolutionVersion`, not a warehouse ID.

### WarehouseResolution To Checkout

- Service: `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
  - `ResolveForSelectedShippingMethodAsync`
    - Reads selected active shipping method from SQL.
    - Requires `FulfillmentWarehouseId`.
    - Validates active warehouse.
    - For external carriers, validates carrier configuration plus account/warehouse readiness.
    - Builds a hash over selected method, fulfillment warehouse, cart, candidate warehouse, and stock conditions.
    - Creates or reuses an active resolution.
  - `ValidateSubmittedResolutionAsync`
    - Validates session/cart ownership.
    - Requires unexpired resolution in `Resolved` or `Locked` status.
    - Reloads selected shipping method authority from SQL.
    - Requires resolution warehouse to match `ShippingMethod.FulfillmentWarehouseId`.
    - Recomputes hash and candidate warehouse.
    - Locks the resolution for checkout when requested.
- Entity: `PointOfSale.Core/Entities/WarehouseResolution.cs`
  - Tracks tenant, checkout session, cart, selected shipping method, warehouse, source, status, version, algorithm version, input hash/json, expiry, and optional order.
- EF guard: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
  - `GuardWarehouseResolutionAuthority` prevents mutation of authority fields and delete/soft-delete of warehouse resolution rows.

### Checkout To Inventory And Order

- Checkout DTO: `PointOfSale.API/Models/CheckoutDtos.cs`
  - `CheckoutRequestDto` requires checkout session and warehouse resolution fields.
- Checkout service: `PointOfSale.API/Services/Checkout/CheckoutService.cs`
  - Reloads and validates shipping method.
  - Calls `ValidateSubmittedResolutionAsync(lockForCheckout: true)`.
  - Uses `finalWarehouseResolution.WarehouseId` for:
    - `Order.WarehouseId`
    - each `OrderItem.WarehouseId`
    - inventory reservation
  - Calls `MarkOrderedAsync` after order save.
  - Fulfillment mode is derived from canonical carrier code: empty/manual means manual; other carrier codes mean carrier fulfillment.

### Shipment And Fulfillment Implications

- API shipment service: `PointOfSale.API/Services/Shipments/ShipmentService.cs`
  - Uses `Order.WarehouseId` as `OriginWarehouseId`.
  - Uses order shipping method carrier and service level as authoritative.
  - Manual methods create local pending shipments.
  - External methods require enabled legacy `CarrierConfiguration`.
- Runtime carrier shipment service: `PointOfSale.Infrastructure/Services/CarrierShipmentService.cs`
  - Uses order shipping method carrier and service level.
  - Loads legacy `CarrierConfiguration`.
  - Builds `CarrierCredentials` from encrypted `CarrierConfiguration` values.
  - Calls the adapter.
  - Evidence was not found that it consumes `CarrierAccount.CredentialReference`.

## 3. Carrier Account Findings

### Current Carrier Account Model

`CarrierAccount` is an external-carrier administration/readiness model. It should not be interpreted as the sole runtime integration record because runtime shipment creation currently uses `CarrierConfiguration`.

Important code evidence:

- Entity and state model: `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`
- Contracts: `PointOfSale.Core/Interfaces/ShippingAdministration/CarrierAccountRuntimeContracts.cs`
- Runtime: `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
- Admin API: `PointOfSale.API/Controllers/AdminShippingAdministrationController.cs`
- API facade: `PointOfSale.API/Services/ShippingAdministration/ShippingAdministrationApiService.cs`

### Supported Carrier Classification

#### Manual Fulfillment

- Canonical code: `manual`
- Display name: `Manual Fulfillment`
- Adapter: `ManualCarrierDefinition`
- External integration exists: no
- Credential type required: none
- Runtime credential fields consumed: none
- Environment required: no
- Connection test required: no
- Readiness required: no external readiness
- Service levels: `standard`
- Warehouse requirement: shipping method must have active `FulfillmentWarehouseId` for checkout preview/select/finalization
- Credentials used during shipment/order fulfillment: no
- Classification: **Fully integrated for manual local fulfillment; not an external carrier integration**

CarrierAccount conclusion: Manual Fulfillment should not have a CarrierAccount. The current backend and Admin Carrier Accounts UI largely enforce this.

#### Bosta

- Canonical code: `bosta`
- Display name: `Bosta`
- Adapter: `BostaAdapter`
- External integration exists: yes
- Credential type required by runtime: API key/token and environment selection
- Runtime credential fields consumed:
  - `ApiKey`: used as the `Authorization` header
  - `UseSandbox`: chooses staging vs production base URL
  - `ApiSecret`: not consumed
  - `AccountId`: not consumed
  - `ApiUrl`: not consumed
  - `ExtraConfig`: not consumed by observed adapter runtime
- Connection test: `ValidateCredentialsAsync` calls `GET {baseUrl}/cities` with the API key/token.
- Service levels: `standard`, `express`, `same_day`
- Warehouse requirement:
  - Business/checkout authority: `ShippingMethod.FulfillmentWarehouseId`
  - Current external admission additionally requires `CarrierWarehouseAssignment` for the carrier account and same warehouse.
- Additional runtime requirement:
  - Shipment creation requires Bosta geography/address identity: either district id, or city id plus district name.
- Credentials used during shipment/order fulfillment:
  - Used from legacy `CarrierConfiguration`, not from `CarrierAccount.CredentialReference`.
- Classification: **Partially integrated**
  - The Bosta adapter has working runtime paths and simple credential needs.
  - The current administration/runtime split is inconsistent because CarrierAccount readiness is required for admission but CarrierConfiguration credentials are used for shipment runtime.

#### Aramex

- Canonical code: `aramex`
- Display name: `Aramex`
- Adapter: `AramexAdapter`
- External integration exists: yes
- Credential type required by runtime:
  - `ApiKey` as username
  - `ApiSecret` as password
  - `AccountId` as account number
  - `ExtraConfig.AccountPin`
  - `ExtraConfig.AccountEntity`
  - `ExtraConfig.AccountCountryCode`
  - `UseSandbox`
- Connection test: API request to fetch countries with built client info.
- Service levels: `standard`, `express`, `international`, `economy`
- Warehouse requirement:
  - `ShippingMethod.FulfillmentWarehouseId` for checkout authority.
  - Current storefront external admission also requires `CarrierWarehouseAssignment`.
- Credentials used during shipment/order fulfillment:
  - Used from legacy `CarrierConfiguration`.
- Classification: **Partially integrated**
  - Runtime adapter exists and consumes real credential fields.
  - CarrierAccount credentials are not the runtime credential source.

#### Mylerz

- Canonical code: `mylerz`
- Display name: `Mylerz`
- Adapter: `MylerzAdapter`
- External integration exists: not production-usable in current code.
- Credential type required: evidence of intended bearer/API credential handling exists, but the adapter reports official public contract unavailable.
- Runtime credential fields consumed: unavailable path prevents production use.
- Connection test: fails with official contract unavailable.
- Service levels: evidence not available while contract is unavailable; adapter returns no usable service-level evidence.
- Warehouse requirement:
  - `ShippingMethod.FulfillmentWarehouseId` if a method is configured.
  - Current external admission would additionally require account assignment/readiness, but activation should be blocked by connection-test/runtime unavailability.
- Credentials used during shipment/order fulfillment: not production-usable.
- Classification: **Unsupported at runtime**

#### DHL

- Canonical code: `dhl`
- Display name: `DHL Express`
- Adapter: `DhlAdapter`
- External integration exists: partial
- Credential type required by runtime:
  - `ApiKey`
  - `ApiSecret`
  - `AccountId`
  - `UseSandbox`
- Runtime credential fields consumed:
  - API key and secret are used for basic authentication.
  - Account ID is required by validation.
- Connection test: local required-field validation; evidence of a real remote validation call was not found.
- Service levels: `express`, `economy`, `same_day`, `domestic`
- Warehouse requirement:
  - `ShippingMethod.FulfillmentWarehouseId`
  - Current external admission additionally requires `CarrierWarehouseAssignment`.
- Credentials used during shipment/order fulfillment:
  - Used from legacy `CarrierConfiguration`.
- Classification: **Partially integrated**
  - Runtime code exists, but connection validation is not a full external verification.

#### FedEx

- Canonical code: `fedex`
- Display name: `FedEx`
- Adapter: `FedExAdapter`
- External integration exists: foundation only
- Credential type required by intended runtime:
  - `ApiKey` as client id
  - `ApiSecret` as client secret
  - `AccountId` as account number
  - `UseSandbox`
- Connection test: returns not certified.
- Service levels: adapter exposes no usable service levels while not certified.
- Warehouse requirement:
  - `ShippingMethod.FulfillmentWarehouseId` if configured.
  - Current external readiness would still require CarrierAccount readiness, but readiness cannot be achieved with failing certification.
- Credentials used during shipment/order fulfillment: not production-capable in current code.
- Classification: **Unsupported at runtime / partially scaffolded**

## 4. Manual Fulfillment Findings

Manual Fulfillment means: "The merchant will fulfill or deliver this order manually."

Answers:

- Should Manual Fulfillment have a CarrierAccount? **No.**
- Should it appear in Carrier Accounts? **No.**
- Should it have credentials? **No.**
- Should it have connection tests? **No.**
- Should it have CarrierWarehouseAssignment? **No.**
- Should it have external readiness? **No.**
- What should determine whether a Manual Fulfillment Shipping Method is usable?
  - Active shipping method.
  - `carrierCode` canonicalized to `manual`.
  - Supported service level, currently `standard`.
  - Valid pricing fields.
  - Valid `FulfillmentWarehouseId`.
  - Active same-tenant warehouse.
  - Cart fulfillable from that warehouse.

Current implementation evidence:

- `ShippingAdministrationApiService.CreateCarrierAccountAsync` rejects manual accounts.
- `CarrierAccountAdministrationRuntime.RegisterCarrierAccountAsync` rejects manual accounts.
- `CarrierAccountAdministrationRuntime.AssignWarehouseAsync` rejects manual warehouse assignments.
- `CarrierAccountsTab` filters out supported carriers where `accountRequired === false`, so Manual Fulfillment is not offered in the Carrier Accounts add flow.
- `ShippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync` admits manual methods without external readiness when the method warehouse is active and stock-ready.
- `WarehouseResolutionService.ValidateSelectedShippingMethodAuthorityAsync` returns the method warehouse for manual methods without requiring `CarrierConfiguration`, `CarrierAccount`, or `CarrierWarehouseAssignment`.

Remaining violations or risks:

- Manual Fulfillment still appears in the carrier registry, which is acceptable as a canonical carrier identity for shipping methods, but the UI must continue treating it as a shipping-method carrier option rather than an integration account.
- Older storefront `GET /api/storefront/shipping-methods` can still return active methods from the Mongo projection without enforcing configured warehouse/stock/readiness unless a warehouse filter is supplied. The live checkout path uses preview/select and is stricter, but this endpoint is less aligned with the hardened authority model.

## 5. Shipping Method Findings

`ShippingMethod` is the customer-facing and merchant commercial configuration for shipping options.

Properties that belong on `ShippingMethod`:

- `Name`: customer/admin-facing shipping option name.
- `NameAr`: localized name.
- `Description`: customer/admin-facing explanation.
- `CarrierCode`: canonical carrier/integration identity.
- `ServiceLevel`: service level within the carrier.
- `BaseRate`: fixed charge.
- `PerKgRate`: variable charge by weight.
- `FreeShippingThreshold`: commercial pricing threshold.
- `EstimatedDeliveryDaysMin`: lower delivery estimate.
- `EstimatedDeliveryDaysMax`: upper delivery estimate.
- `SortOrder`: storefront ordering.
- `TrackingUrlTemplate`: customer tracking display template, if relevant.
- `IsActive`: shipping-method activation.
- `FulfillmentWarehouseId`: server-side fulfillment origin authority.

Properties with caveats:

- `CarrierName`
  - It is redundant identity because `CarrierCode` is authoritative.
  - Current backend protects it by resolving display text from the registry on create/update.
  - It is acceptable as a denormalized display cache only if never user-authoritative.
- `AvailableCountries`
  - This is commercial/geographic eligibility, but current preview evidence focused on warehouse and stock; country filtering should be verified before relying on it.

Properties that do not belong on `ShippingMethod`:

- API keys, API secrets, account numbers, API URLs, carrier environment, webhook secrets, customs readiness, pickup scheduling credentials, and origin-carrier API readiness.

Necessary missing or weak properties:

- No explicit persisted currency was observed on `ShippingMethod`; this may be tenant/store currency elsewhere. If multiple currencies are supported per tenant, shipping method pricing needs clear currency authority.
- Weight-unit assumptions for `PerKgRate` should be documented or enforced.
- Service-level metadata is adapter-driven, but per-carrier capability rules are not expressed cleanly enough for Admin UX.

## 6. Carrier Identity Findings

Authoritative relationship intended and mostly implemented:

```text
ShippingMethod
  carrierCode
    Carrier Registry / Integration
```

Examples:

- `Bosta Express`: `carrierCode = bosta`, `serviceLevel = express`
- `Standard Delivery`: `carrierCode = manual`, `serviceLevel = standard`

Current guarantees:

- `CarrierCodes.Normalize` canonicalizes empty/null to `manual`.
- `ShippingMethodsController` rejects unknown carrier codes.
- `ShippingMethodsController` validates service level against the selected adapter.
- `ShippingMethodsController` resolves `CarrierName` from registry display name rather than trusting client display text.
- Admin Shipping Methods UI presents carrier codes from supported carrier summaries and service levels from registry metadata.

Detected spoofing/identity risks:

- The entity still stores both `CarrierName` and `CarrierCode`; `CarrierName` is redundant and must remain non-authoritative.
- Unknown carrier creation through the normal admin backend path is blocked.
- Saving display text such as "Bosta" while `carrierCode` remains `manual` is blocked in the normal backend create/update path because the backend computes `CarrierName` from `CarrierCode`.
- Unsupported service levels are blocked when an adapter reports supported service levels. If an adapter reports no levels, the backend permits only `standard`. This affects unavailable carriers such as Mylerz/FedEx and can create awkward UI states.
- Manual Fulfillment does not require external integration in preview/select checkout. This part is correct.

## 7. Warehouse Findings

The current checkout authority is:

```text
ShippingMethod.FulfillmentWarehouseId
  -> WarehouseResolution
  -> Order.WarehouseId
  -> OrderItem.WarehouseId
  -> Inventory reservation
```

Evidence:

- `ShippingMethod.FulfillmentWarehouseId`: `PointOfSale.Core/Entities/ShippingMethod.cs`
- FK/index mapping: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- Migration: `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority`
- Preview filtering: `ShippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync`
- Selection authority: `WarehouseResolutionService.ResolveForSelectedShippingMethodAsync`
- Final validation: `WarehouseResolutionService.ValidateSubmittedResolutionAsync`
- Checkout use: `CheckoutService` assigns order/item warehouse and reserves inventory using `finalWarehouseResolution.WarehouseId`.

Browser authority:

- The storefront client submits `shippingMethodId`, `checkoutSessionId`, `warehouseResolutionId`, and `warehouseResolutionVersion`.
- It does not submit a warehouse ID as fulfillment authority.
- The server reloads the selected shipping method and validates the warehouse match.

CarrierWarehouseAssignment status:

- Not final warehouse authority.
- Currently required for external-carrier storefront admission and selected-method validation.
- Currently optional/irrelevant for Manual Fulfillment.
- Best classified as **external-carrier readiness metadata**, not fulfillment authority.
- It is partially redundant with `ShippingMethod.FulfillmentWarehouseId` because both name a warehouse relationship for a carrier/method path.

## 8. Readiness Findings

Carrier account activation blockers include:

- Credential reference missing.
- Environment missing.
- Connection test missing.
- Warehouse assignment missing.
- Origin readiness missing.
- Unknown readiness.
- Carrier mapping unpublished.
- International customs readiness problems.
- Provider capability unsupported.
- Unsupported enabled service level.

Storefront external admission blockers include:

- Shipping method inactive.
- Missing `FulfillmentWarehouseId`.
- Fulfillment warehouse inactive or missing.
- Cart not fulfillable from method warehouse.
- Legacy `CarrierConfiguration` missing or disabled.
- Missing ready `CarrierAccount` for carrier.
- Missing enabled `CarrierWarehouseAssignment` for the method warehouse.
- Origin readiness not published.
- Carrier account not active.
- Carrier account operational state not ready.

Contradictions:

- A valid external method can be blocked by `CarrierAccount` readiness even though runtime shipment credentials live in `CarrierConfiguration`.
- A valid Bosta method can be blocked by generic CarrierAccount conditions unrelated to Bosta adapter credential needs.
- `CarrierWarehouseAssignment` is still a hard external-carrier gate even though `ShippingMethod.FulfillmentWarehouseId` is now the checkout warehouse authority.
- `CarrierAccount.CredentialReference` can make an account appear credential-ready while shipment runtime has no usable legacy `CarrierConfiguration` credentials.
- Conversely, legacy `CarrierConfiguration` can contain runtime credentials while CarrierAccount readiness blocks storefront admission.

Manual-specific readiness answer:

- A valid Manual Fulfillment shipping method should not become blocked because Carrier Account UI says "Warehouse Missing."
- Current preview/select code does not apply CarrierAccount warehouse readiness to manual methods.
- Manual methods still require `ShippingMethod.FulfillmentWarehouseId` and stock readiness.

## 9. Storefront Admission Findings

Live checkout path:

1. Storefront calls `POST /api/storefront/shipping-methods/preview`.
2. Controller validates checkout session ownership and loads cart through `WarehouseResolutionService.LoadPreviewCartAsync`.
3. `ShippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync` queries active SQL shipping methods.
4. Method must have `FulfillmentWarehouseId`.
5. Fulfillment warehouse must be active.
6. Cart must be fulfillable from that warehouse.
7. Manual methods are admitted after warehouse and stock checks.
8. External methods require ready external carrier/warehouse pair.
9. Storefront calls `POST /api/storefront/shipping-methods/select` for the chosen method.
10. `WarehouseResolutionService.ResolveForSelectedShippingMethodAsync` locks the method-to-warehouse decision into a `WarehouseResolution`.
11. Final checkout submits the resolution id/version.
12. Checkout revalidates and locks the resolution before order creation and inventory reservation.

Manual Fulfillment should not require and currently does not require in this path:

- CarrierAccount.
- API credentials.
- Connection test.
- External carrier readiness.
- CarrierWarehouseAssignment.

Manual Fulfillment does require:

- Active `ShippingMethod`.
- `carrierCode = manual` or empty normalized to manual.
- Valid `FulfillmentWarehouseId`.
- Active warehouse.
- Cart fulfillable from that warehouse.
- Valid pricing/configuration.

External carrier methods additionally require in current code:

- Enabled legacy `CarrierConfiguration`.
- Active/ready `CarrierAccount`.
- Enabled/published `CarrierWarehouseAssignment` for the method warehouse.

Important exception:

- The older `GET /api/storefront/shipping-methods` path uses a less strict Mongo/cache flow when no warehouse filter is supplied. It does not provide the same hard admission guarantees as preview/select. The live checkout path should continue using preview/select.

## 10. Carrier Capability Matrix

| Carrier | External API | Credential Type | Connection Test | Service Levels | Warehouse Requirement | Runtime Integration |
|---|---|---|---|---|---|---|
| Manual Fulfillment (`manual`) | No | None | None | `standard` | `ShippingMethod.FulfillmentWarehouseId`, active warehouse, stock-ready cart | Fully integrated as manual/local fulfillment |
| Bosta (`bosta`) | Yes | API key/token plus environment | `GET /cities` with API key/token | `standard`, `express`, `same_day` | Method warehouse; current code also requires ready CarrierAccount assignment for external admission | Partially integrated; shipment adapter exists, uses legacy CarrierConfiguration credentials; needs Bosta address/geography identity |
| Aramex (`aramex`) | Yes | API key/username, API secret/password, account id, account pin, account entity, account country code, environment | Fetch countries API request | `standard`, `express`, `international`, `economy` | Method warehouse; current code also requires ready CarrierAccount assignment | Partially integrated; runtime adapter exists but credentials come from legacy CarrierConfiguration |
| Mylerz (`mylerz`) | Evidence of intended API, but disabled | Evidence insufficient for production; intended bearer-style credentials are not usable while contract unavailable | Fails: official contract unavailable | Evidence not found while unavailable | Method warehouse if configured; readiness cannot become production-usable | Unsupported at runtime |
| DHL (`dhl`) | Yes, partial | API key, API secret, account id, environment | Local required-field validation; no remote validation evidence found | `express`, `economy`, `same_day`, `domestic` | Method warehouse; current code also requires ready CarrierAccount assignment | Partially integrated; runtime code exists, production verification incomplete |
| FedEx (`fedex`) | Foundation only | Intended client id/API key, client secret/API secret, account id, environment | Fails: not certified | Evidence not found as usable levels | Method warehouse if configured; readiness cannot become production-usable | Unsupported at runtime / scaffolded only |

## 11. Admin UX Findings

### Screen A: Carrier Accounts

Carrier Accounts should configure external integrations only.

Minimum UI by carrier:

- Manual Fulfillment:
  - Should not appear.
  - No credentials.
  - No connection test.
  - No warehouse assignment.
  - No external readiness.
- Bosta:
  - Environment if required by deployment mode.
  - API key/token.
  - Connection test using the API key/token.
  - Possibly an account label and health state.
  - Do not show API secret, account ID, or API URL unless the adapter actually consumes them.
- Aramex:
  - Environment.
  - Username/API key.
  - Password/API secret.
  - Account number.
  - Account pin.
  - Account entity.
  - Account country code.
  - Connection test.
- DHL:
  - Environment.
  - API key.
  - API secret.
  - Account ID.
  - Connection test should be clearly marked if only local validation exists.
- Mylerz:
  - Should not be presented as production-configurable while official contract is unavailable.
- FedEx:
  - Should not be presented as production-configurable while certification blocks validation/runtime use.

Current issue:

- `CarrierAccountCard.jsx` uses a large generic form for all external carriers.
- Bosta merchants see fields that runtime does not consume.
- Capability metadata from adapters is not rich enough to drive a minimal carrier-specific credential UI.

### Screen B: Shipping Methods

Shipping Methods should configure customer-facing and commercial behavior:

- Name.
- Localized name.
- Description.
- Carrier code.
- Service level.
- Base rate.
- Rate per kg.
- Free shipping threshold.
- Min/max delivery days.
- Sort order.
- Tracking URL/template if customer-facing.
- Fulfillment warehouse.
- Active state.

Current UI is broadly aligned:

- `ShippingMethodsTab.jsx` exposes carrier, service level, pricing, ETA, warehouse, tracking template, and activation.
- It separates shipping commercial configuration from Carrier Account integration fields.
- It should continue not exposing editable `CarrierName`.

## 12. Complexity / Simplification Findings

Necessary complexity:

- Canonical carrier registry and `carrierCode`.
- Shipping methods as customer/commercial options.
- `ShippingMethod.FulfillmentWarehouseId`.
- WarehouseResolution as checkout-time authority and audit record.
- Server-side stock validation and inventory reservation from the resolved warehouse.
- Tenant isolation and same-tenant FK checks.
- External carrier adapter abstraction.
- Secure credential storage for external integrations.
- Shipment/order snapshots of carrier, service level, shipping method, and warehouse.

Accidental complexity:

- Two carrier integration stores: `CarrierConfiguration` and `CarrierAccount`.
- Credential reference in CarrierAccount not consumed by shipment runtime.
- Connection test credentials submitted separately from the stored credential reference.
- Generic Carrier Account UI fields shown for carriers that do not use them.
- CarrierAccount service-level enablement duplicates `ShippingMethod.ServiceLevel` constraints.
- `CarrierWarehouseAssignment` overlaps with `ShippingMethod.FulfillmentWarehouseId`.
- Legacy storefront GET path has weaker admission semantics than preview/select.
- Registry exposure conflates "known adapter" with "production-capable carrier."

Legacy complexity:

- `CarrierConfiguration` still owns actual runtime credentials, carrier enablement, default service levels, pricing overrides, and validation state.
- `ShippingMethod.AvailableCountries` and carrier-level pricing/default fields may overlap with method commercial configuration.
- Nullable `FulfillmentWarehouseId` exists for migration/backward compatibility and must be treated as not checkout-usable.

Duplicate authority:

- `CarrierName` vs `CarrierCode`.
- `CarrierConfiguration` credentials vs `CarrierAccount.CredentialReference`.
- `CarrierConfiguration.IsEnabled` vs `CarrierAccount.LifecycleStatus/OperationalState`.
- `CarrierServiceLevelEnablement` vs `ShippingMethod.ServiceLevel`.
- `CarrierWarehouseAssignment.WarehouseId` vs `ShippingMethod.FulfillmentWarehouseId`.

Simplification safety review:

- Removing Manual Fulfillment CarrierAccount concepts would not break tenant isolation, checkout correctness, inventory correctness, order warehouse propagation, or manual fulfillment. Current code already mostly does this.
- Making `ShippingMethod.FulfillmentWarehouseId` the only checkout warehouse authority must be preserved; removing it would break checkout and inventory correctness.
- Removing external readiness entirely would be unsafe because external shipment creation can fail without credentials and carrier-specific setup.
- Reclassifying `CarrierWarehouseAssignment` as optional readiness metadata rather than warehouse authority would not break warehouse propagation if storefront/final checkout continue using `FulfillmentWarehouseId`.
- Replacing `CarrierConfiguration` runtime credentials with CarrierAccount secret resolution would require careful migration and is out of scope for this audit. Until then, both stores create operational inconsistency.

## 13. Exact Root Causes

1. The new CarrierAccount readiness architecture was layered beside the legacy `CarrierConfiguration` runtime architecture instead of replacing or adapting it.
2. Storefront external admission depends on both systems, but shipment runtime depends on only the legacy system.
3. CarrierAccount credential references do not provide the credentials consumed by adapters at shipment runtime.
4. The readiness model is generic rather than carrier-capability-driven.
5. CarrierWarehouseAssignment remained a hard external admission gate after warehouse authority moved to `ShippingMethod.FulfillmentWarehouseId`.
6. Supported-carrier registry output means "DI-registered adapter," not "merchant can safely use this carrier in production."
7. Admin UI follows the generic CarrierAccount abstraction, causing Bosta and other carriers to expose irrelevant fields.
8. The live checkout path is hardened, but the older storefront shipping-method GET path still has weaker filtering semantics.

## 14. Minimal Safe Target Architecture

Evidence supports the preferred target with one correction: external carrier readiness is still needed, but it must be tied to the actual runtime credential source.

Target:

```text
Carrier Registry
  carrierCode
    Shipping Method
      price
      service level
      fulfillment warehouse
        Warehouse
```

External carriers additionally:

```text
Carrier Registry
  carrierCode
    Carrier Account / Integration
      credential secret
      environment
      carrier-specific readiness
```

Manual Fulfillment:

```text
Shipping Method
  carrierCode = manual
  serviceLevel = standard
  FulfillmentWarehouseId = selected origin
```

Manual Fulfillment should have:

- No CarrierAccount.
- No credentials.
- No connection test.
- No CarrierWarehouseAssignment.
- No external readiness.

External carrier readiness should answer only:

- Can SHOPIXY communicate with this carrier for this tenant and environment?
- Are the exact credentials required by this carrier present and valid?
- Is any carrier-specific operational prerequisite satisfied?
- Is the shipping method's service level supported by that carrier?

External carrier readiness should not decide the checkout warehouse. `ShippingMethod.FulfillmentWarehouseId` and `WarehouseResolution` should remain authoritative.

## 15. Minimal Remediation Plan

1. Declare `ShippingMethod.FulfillmentWarehouseId` as the only checkout warehouse authority.
2. Keep Manual Fulfillment out of Carrier Accounts permanently.
3. Introduce or expose carrier capability metadata sufficient for Admin Carrier Account UI:
   - required credential fields
   - optional credential fields
   - whether a real connection test exists
   - production capability state
   - supported service levels
4. For Bosta, reduce the integration UI to environment plus API key/token unless new repository evidence shows more fields are consumed.
5. Decide the carrier integration source of truth:
   - either migrate runtime shipment code from `CarrierConfiguration` to CarrierAccount secret resolution
   - or make CarrierAccount administration write/synchronize the legacy runtime `CarrierConfiguration`
6. Until that decision is implemented, clearly label external carrier readiness as blocked by duplicate authority.
7. Reclassify `CarrierWarehouseAssignment`:
   - Manual: not applicable.
   - External carriers: optional readiness/origin metadata only if the carrier actually requires origin publication/mapping.
   - Not checkout warehouse authority.
8. Gate storefront external carrier admission on the same credential/readiness source used by shipment runtime.
9. Keep preview/select as the live checkout admission path.
10. Either deprecate or harden the older storefront GET shipping-method endpoint so it cannot advertise methods that preview/select would reject.

## 16. Risks

- External methods can appear blocked despite having runtime credentials because CarrierAccount readiness is missing.
- External methods can appear administratively ready while runtime shipment creation lacks legacy `CarrierConfiguration` credentials.
- Bosta merchant setup is more complex than the adapter requires.
- Mylerz and FedEx can appear in supported-carrier UI despite being unavailable/not certified for runtime use.
- CarrierWarehouseAssignment can be misread as warehouse authority, creating confusion with `ShippingMethod.FulfillmentWarehouseId`.
- The older storefront GET path can expose shipping methods with weaker checks than live checkout preview/select.
- Nullable legacy `FulfillmentWarehouseId` rows must be migrated or kept inactive for checkout safety.

## 17. Test Plan

Recommended tests after remediation:

- Manual method appears in preview when active, warehouse active, and stock available.
- Manual method does not require CarrierAccount, CarrierConfiguration, CarrierWarehouseAssignment, credential reference, or connection test.
- Manual method selection creates a `WarehouseResolution` using `ShippingMethod.FulfillmentWarehouseId`.
- Final checkout rejects missing, expired, wrong-version, wrong-session, wrong-cart, or mismatched-warehouse resolution.
- Final checkout assigns `Order.WarehouseId` and `OrderItem.WarehouseId` from the locked resolution.
- Inventory reservation uses the locked resolution warehouse.
- Bosta method requires supported service level and method warehouse.
- Bosta connection UI/API accepts only fields consumed by runtime unless a new adapter requirement is added.
- Bosta external admission and shipment runtime use one consistent credential/readiness source.
- Aramex required extra credential fields are enforced.
- DHL local-only validation is surfaced accurately.
- Mylerz and FedEx are not presented as production-ready.
- Unknown carrier codes are rejected.
- Display-name spoofing cannot persist an inconsistent `CarrierName`.
- Unsupported service levels are rejected.
- Older storefront GET endpoint is either deprecated or brought into parity with preview admission.

## 18. Live Verification Plan

No live mutations or browser automation were performed for this audit.

Recommended live verification when allowed:

1. Seed or use an active warehouse with stock.
2. Create a Manual Fulfillment shipping method with `carrierCode = manual`, `serviceLevel = standard`, pricing, and `FulfillmentWarehouseId`.
3. Verify storefront preview shows the manual method.
4. Select the method and verify a warehouse resolution is returned.
5. Complete checkout and verify `Order.WarehouseId`, order item warehouses, and reservation warehouse match the method warehouse.
6. Configure Bosta with only the actual required runtime credentials: environment and API key/token.
7. Verify the Bosta connection test.
8. Create a Bosta shipping method with supported service level and fulfillment warehouse.
9. Verify preview/select/final checkout all use the shipping method warehouse.
10. Verify shipment creation uses the same Bosta credential source that admin integration setup wrote.

## 19. Explicit Out-of-Scope Items

- No schema changes.
- No migrations.
- No source-code changes.
- No frontend changes.
- No backend/API changes.
- No test changes.
- No database mutations.
- No browser automation.
- No carrier API live calls.
- No git commits.
- No git pushes.
- No redesign of payment, checkout fields, order creation, inventory model, or shipment lifecycle beyond audit recommendations.

## 20. Final Certification

**BLOCKED**

Reason:

The live checkout flow now has a sound server-side warehouse authority model based on `ShippingMethod.FulfillmentWarehouseId` and `WarehouseResolution`. However, architectural contradictions remain:

- External carrier admission depends on CarrierAccount readiness and CarrierWarehouseAssignment.
- External shipment runtime consumes legacy CarrierConfiguration credentials.
- CarrierAccount credential references are not the runtime credential source.
- CarrierWarehouseAssignment still acts as a hard external admission gate even though the shipping method now owns fulfillment warehouse authority.
- Carrier UI is not capability-driven and over-configures simple carriers such as Bosta.

The architecture should not be certified as pass until external carrier integration authority is unified or explicitly synchronized.

## Final Business Answer

### A. Manual Fulfillment

Merchant configures only a Shipping Method:

- Carrier: `manual`
- Service level: `standard`
- Name/localized name/description
- Pricing: base rate, per-kg rate, free-shipping threshold
- Delivery estimate: min/max days
- Fulfillment warehouse: active warehouse with stock
- Active state

Manual Fulfillment does not need a Carrier Account, credentials, connection test, CarrierWarehouseAssignment, or external readiness.

### B. Bosta

Merchant configures a Shipping Method:

- Carrier: `bosta`
- Service level: `standard`, `express`, or `same_day`
- Commercial pricing
- Delivery estimate
- Fulfillment warehouse
- Active state

Merchant configures a carrier integration:

- Environment/sandbox mode
- API key/token

Repository evidence shows Bosta runtime consumes only API key/token and environment for adapter credential validation and API calls. API secret, account ID, API URL, and extra config were not found to be consumed by the Bosta adapter. Shipment creation also requires Bosta address/geography identity: district id, or city id plus district name.

Current code additionally requires legacy `CarrierConfiguration` plus CarrierAccount readiness and CarrierWarehouseAssignment for storefront admission. That is an implementation contradiction, not a Bosta business requirement proven by adapter evidence.

### C. Another External Carrier

Merchant configures a Shipping Method:

- Carrier code from the registry.
- Service level supported by that carrier.
- Commercial pricing.
- Delivery estimate.
- Fulfillment warehouse.
- Active state.

Merchant configures the carrier integration with only the credentials that carrier actually consumes:

- Aramex: username/API key, password/API secret, account number, account pin, account entity, account country code, environment.
- DHL: API key, API secret, account ID, environment.
- Mylerz: not production-usable in current code; official contract unavailable.
- FedEx: not production-usable in current code; adapter reports not certified.

In the current implementation, external carrier credentials belong to the legacy `CarrierConfiguration` runtime path, while CarrierAccount stores readiness metadata and a credential reference that shipment runtime does not consume. The minimal safe target is to put integration credentials/readiness in Carrier Account, keep commercial shipping option fields in Shipping Method, and make the runtime consume the same Carrier Account credential source.
