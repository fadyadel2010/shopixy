# SHOPIXY Shipping Methods Forensic Audit

Date: 2026-08-11  
Scope: STEP 1 only. No source, schema, migration, configuration, UI, test, database, git, or runtime changes were made.

## 1. Executive Summary

FACT: The reported checkout message, `Shipping method response could not be admitted without tenant scope.`, is thrown in the Storefront frontend admission guard at `PointOfSale.Storefront/src/store/checkoutStore.ts:695-702`.

FACT: That specific message is thrown before individual shipping methods are inspected. It means `getRequestHeaders()['X-Tenant-Id']` is not a UUID matching `UUID_REGEX`; it does not prove that the backend omitted `tenantId` from each shipping method.

FACT: Storefront tenant resolution accepts both UUIDs and slugs. `tenantResolver.ts` accepts `UUID_PATTERN` or `SLUG_PATTERN` at `PointOfSale.Storefront/src/bootstrap/tenantResolver.ts:53-59`, resolves subdomains to a slug at `PointOfSale.Storefront/src/bootstrap/tenantResolver.ts:168-175`, stores the resolved value unchanged at `PointOfSale.Storefront/src/bootstrap/tenantResolver.ts:234-240`, and `requestContext.ts` sends it as `X-Tenant-Id` at `PointOfSale.Storefront/src/bootstrap/requestContext.ts:64-76`.

FACT: The backend does not trust raw `X-Tenant-Id`. `TenantResolutionMiddleware` strips the inbound header at `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs:81-85`, resolves tenant authority from trusted sources, rejects spoofing at `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs:109-132`, sets `TenantContext`, and re-adds an authoritative GUID header at `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs:182-183`.

INFERENCE: The highest-priority runtime defect is a Storefront contract mismatch: the frontend admission guard compares the response against the raw frontend request tenant value, which may be a slug, while the backend and shipping DTOs operate on authoritative tenant GUIDs. This is why Shipping can fail while Payment succeeds.

FACT: Payment methods already use a semantic availability envelope and do not require per-method tenant evidence in the frontend admission path. Shipping has a similar semantic envelope but also requires per-method `tenantId` and a UUID expected tenant in frontend admission.

FACT: Manual Fulfillment is represented as a first-class carrier code/adapter (`manual`) and as a shipping-method carrier code, not as a required external carrier account for checkout. Backend shipping-method creation does not require credentials, API URL, connection tests, readiness evidence, carrier account, or warehouse assignment.

INFERENCE: The current Shipping Administration UI/domain exposes external-carrier-style fields for Manual Fulfillment because the newer carrier-account administration flow consumes the carrier registry, includes `manual`, and renders one generic `CarrierAccountCard` for credential, connection-test, and warehouse assignment workflows.

Certification: BLOCKED due to one P0 checkout blocker and multiple P1 architecture/contract issues requiring Step 2 remediation.

## 2. Current Architecture Map

FACT:

Admin shipping method lifecycle:

`PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx`
-> `/api/shippingmethods`
-> `PointOfSale.API/Controllers/ShippingMethodsController.cs`
-> SQL `ShippingMethods`
-> `ShippingMethodUpdatedEvent` / `ShippingMethodDeletedEvent`
-> `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs`
-> Mongo `shipping_methods`
-> Redis cache invalidation.

Storefront checkout read lifecycle:

`PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`
-> `checkoutStore.fetchShippingMethods`
-> `GET /api/storefront/shipping-methods`
-> `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
-> `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
-> `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs`
-> Mongo `shipping_methods` with SQL fallback
-> `ShippingMethodsListResponseDto`
-> `admitStorefrontShippingMethodList`
-> checkout state.

Carrier administration lifecycle:

`PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
-> `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
-> `/api/admin/shipping`
-> `PointOfSale.API/Controllers/AdminShippingAdministrationController.cs`
-> `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs`
-> `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
-> SQL `CarrierAccounts`, `CarrierCredentialReferences`, `CarrierWarehouseAssignments`, policies, service levels, connection tests, audit.

Warehouse lifecycle:

`PointOfSale.Core/Entities/Warehouse.cs`
-> SQL `Warehouses`
-> `PointOfSale.API/Controllers/WarehousesController.cs`
-> `/api/warehouses`.

## 3. Shipping Method Domain Model

FACT: `ShippingMethod` is the SQL write model at `PointOfSale.Core/Entities/ShippingMethod.cs:8-95`.

FACT: Its properties are `TenantId`, `Name`, `NameAr`, `Description`, `BaseRate`, `PerKgRate`, `FreeShippingThreshold`, `EstimatedDeliveryDaysMin`, `EstimatedDeliveryDaysMax`, `IsActive`, `SortOrder`, `AvailableCountries`, `CarrierName`, `CarrierCode`, `ServiceLevel`, and `TrackingUrlTemplate`.

FACT: `CarrierCode` is a nullable string. The entity comment says null means manual/no carrier integration at `PointOfSale.Core/Entities/ShippingMethod.cs:77-82`.

FACT: There is no `WarehouseId`, no `CarrierAccountId`, and no navigation property from `ShippingMethod` to `Warehouse`, `CarrierAccount`, or a carrier entity in the inspected model.

FACT: Admin create normalizes missing `CarrierCode` to `manual` at `PointOfSale.API/Controllers/ShippingMethodsController.cs:156-174`.

FACT: Admin DTO validation requires name, non-negative rates, valid delivery day bounds, and non-negative sort order at `PointOfSale.API/Controllers/ShippingMethodsController.cs:554-596`. It does not require credential reference, API key/token, API URL, connection test, readiness evidence, carrier account, or warehouse ID.

INFERENCE: The existing shipping method model is salvageable for simple flat-rate/manual methods, but it lacks an explicit relationship to carrier accounts and warehouses for external-carrier operational readiness.

## 4. Manual Fulfillment Forensics

FACT: `CarrierCodes.Manual` is the canonical code `manual` at `PointOfSale.Core/Constants/CarrierCodes.cs:19-23`.

FACT: `CarrierCodes.Normalize(null or empty)` returns `manual` at `PointOfSale.Core/Constants/CarrierCodes.cs:66-70`.

FACT: `CarrierCodes.IsManual` treats null/empty/manual as manual at `PointOfSale.Core/Constants/CarrierCodes.cs:59-62`.

FACT: `ManualCarrierDefinition` is a first-class no-op adapter at `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs:7-24`.

FACT: Manual shipment creation does not call an external API at `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs:47-66`.

FACT: Manual credential validation succeeds without credentials and returns account name `Manual Fulfillment` at `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs:104-115`.

FACT: Manual quote and tracking sync are not supported by the adapter at `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs:27-38` and `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs:87-98`.

FACT: The older carrier configuration modal explicitly special-cases manual at `PointOfSale.Web/src/modules/shipping/components/CarrierConfigurationModal.jsx:301` and switches the footer behavior at `PointOfSale.Web/src/modules/shipping/components/CarrierConfigurationModal.jsx:615`.

FACT: The newer carrier-account card exposes generic credential, warehouse, and connection-test forms at `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx:224-255` and does not show a source-level manual exception in the inspected component.

FACT: `ListSupportedCarriersAsync` returns all registry adapters at `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs:26-31`. Since manual is a registered adapter, it is eligible to appear in supported carriers unless filtered elsewhere. No filtering was verified in available source.

FACT: Carrier-account activation gates require credential reference, successful connection test, active origin warehouse assignment, readiness flags, capability support, and supported service levels at `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs:495-579`. No manual exception was verified there.

FACT: Checkout validates selected manual shipping methods without carrier configuration because the non-manual guard is bypassed by `CarrierCodes.IsManual` at `PointOfSale.API/Services/Checkout/CheckoutService.cs:5320-5362`.

Answers:

- Why Manual exposes these fields: INFERENCE: the newer Shipping Administration carrier-account UI renders generic external-carrier account forms for registry carriers, and manual is in that registry.
- Is Manual a carrier account, carrier integration, shipping method, fulfillment provider, or something else: FACT: in current source it is a first-class carrier code/adapter and can be selected on a `ShippingMethod`; it is not required to be a carrier account for checkout.
- What domain entity represents Manual Fulfillment: FACT: primarily `ShippingMethod.CarrierCode == "manual"` or null/empty normalized to manual; also represented by `ManualCarrierDefinition`.
- Is current representation correct: INFERENCE: partially. Manual as a carrier code/no-op adapter is reasonable, but treating it as an external carrier account with readiness gates is architecturally incorrect.
- Backend-required fields: FACT: name, rates, delivery days, sort order validation, active flag, tenant from `ITenantContext`; carrier code may be null/empty/manual.
- Generic inherited UI fields: INFERENCE: credential reference, API key/token, API secret, API URL, account ID, connection test, readiness evidence, origin readiness, carrier mapping readiness.
- External API required: FACT: no.
- Carrier-account readiness required to create manual shipping method: FACT: no.
- Checkout treats manual differently: FACT: yes, order fulfillment mode becomes manual and carrier configuration validation is bypassed at `CheckoutService.cs:9390-9423` and `CheckoutService.cs:5346-5359`.
- Minimum correct configuration: RECOMMENDATION: active tenant-owned shipping method, manual carrier code or null normalized to manual, name, pricing, delivery days, currency from tenant/projection, and optionally a validated warehouse only if the business requires an origin for fulfillment operations.

## 5. Warehouse Assignment Forensics

FACT: Canonical warehouse entity is `Warehouse` at `PointOfSale.Core/Entities/Warehouse.cs:8-26`.

FACT: Warehouse has `TenantId`, `Name`, `Code`, `Address`, `Phone`, `IsActive`, `IsDefault`, and stock/movement navigation properties at `PointOfSale.Core/Entities/Warehouse.cs:14-25`.

FACT: Warehouses are stored in SQL via `ApplicationDbContext.Warehouses` at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:94-95`.

FACT: Warehouses are tenant-filtered by EF query filter at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:585-586`.

FACT: Admin API `GET /api/warehouses` is implemented by `[Route("api/[controller]")]` and `GetWarehouses` at `PointOfSale.API/Controllers/WarehousesController.cs:16-18` and `PointOfSale.API/Controllers/WarehousesController.cs:46-57`.

FACT: `GetWarehouses` uses `_tenantContext.RequiredTenantId` and returns active tenant warehouses at `PointOfSale.API/Controllers/WarehousesController.cs:49-55`.

FACT: `ShippingMethod` has no warehouse assignment property.

FACT: Carrier-origin warehouse assignment is represented by `CarrierWarehouseAssignment.WarehouseId`, a `Guid`, at `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs:115-119`.

FACT: Carrier warehouse assignment is persisted independently from `ShippingMethod` via `ApplicationDbContext.CarrierWarehouseAssignments` at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:227`.

FACT: Assignment endpoint route has `warehouseId:guid` and normalizes the route ID into the command at `PointOfSale.API/Controllers/AdminShippingAdministrationController.cs:66-69`.

FACT: Backend validates that assigned warehouse exists, belongs to the tenant, is active, and is not deleted at `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs:174-180`.

FACT: Storefront shipping-method filtering uses warehouse assignment only when a `warehouseId` query parameter is supplied. Then `ApplyWarehouseAssignmentFilterAsync` validates the warehouse and joins carrier accounts/assignments at `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:182-252`.

FACT: That warehouse filter excludes manual carriers with `.Where(code => !CarrierCodes.IsManual(code))` at `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:221-223`.

FACT: The inspected `ShippingStep` calls `fetchShippingMethods()` without a warehouse argument at `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx:553-567`; `checkoutStore` calls `GET /api/storefront/shipping-methods` at `PointOfSale.Storefront/src/store/checkoutStore.ts:1730-1739`.

FACT: `CarrierAccountCard` asks for free-text `Warehouse ID` at `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx:232-240`.

FACT: Other admin pages already fetch warehouses from `/warehouses`, for example `PointOfSale.Web/src/pages/Warehouses.jsx:33-37` and warehouse-opening stock selector code at `PointOfSale.Web/src/pages/WarehouseOpeningStock.jsx:71-85`.

INFERENCE: The UI asks merchants to manually enter an ID because the shipping-administration carrier-account component does not consume the existing warehouse API/list selector, not because the backend lacks a warehouse domain.

NOT VERIFIED FROM AVAILABLE SOURCE: live SQL warehouse rows, live Mongo documents, and any production data-specific warehouse assignments.

## 6. Carrier Relationship Forensics

FACT: Canonical carrier registry is `IShippingCarrierRegistry` / `ShippingCarrierRegistry` at `PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs:18-34` and `PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs:45-94`.

FACT: Stable carrier identifiers are string carrier codes such as `manual`, `bosta`, `aramex`, `mylerz`, `dhl`, and `fedex` in `CarrierCodes.AllCodes` at `PointOfSale.Core/Constants/CarrierCodes.cs:54`.

FACT: Bosta is represented by `BostaCarrierDefinition`, display name `Bosta`, at `PointOfSale.Infrastructure/Shipping/Adapters/BostaCarrierDefinition.cs:103-115`.

FACT: Manual is represented by `ManualCarrierDefinition`, display name `Manual Fulfillment`, at `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs:18-24`.

FACT: Carrier accounts are represented by `CarrierAccount` with `TenantId`, `CarrierCode`, `Environment`, `ActiveCredentialReferenceId`, `State`, `OperationalState`, and health fields at `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs:88-104`.

FACT: Carrier accounts are unique by `(TenantId, CarrierCode)` where not deleted at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:3142-3149`, so multiple active/non-deleted carrier accounts of the same carrier for one tenant are not supported by the current model.

FACT: `ShippingMethod` references a carrier by nullable string `CarrierCode`, not by FK to a carrier row or carrier account. `CarrierName` is present as display metadata at `PointOfSale.Core/Entities/ShippingMethod.cs:72-82`.

FACT: The newer `ShippingMethodsTab` form contains `carrierName` but no editable `carrierCode` in `formData` at `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx:102-115`, submits `formData` to `/shippingmethods` at `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx:158-177`, and renders `Carrier display name` as a text input at `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx:443-446`.

FACT: Because backend create normalizes missing `CarrierCode` to manual, typing `Bosta` into `Carrier display name` does not bind the shipping method to the Bosta adapter.

FACT: The older `ShippingOptionFormModal` does select `carrierCode` from `carriers` and maps `carrierName` only for backward compatibility at `PointOfSale.Web/src/modules/shipping/components/ShippingOptionFormModal.jsx:91-111` and `PointOfSale.Web/src/modules/shipping/components/ShippingOptionFormModal.jsx:217-236`.

Answers:

- Carrier display name is presentation metadata, not authority.
- ShippingMethod references carrier implicitly through `CarrierCode` string.
- ShippingMethod does not reference CarrierAccount.
- Current relationship between shipping method and carrier account is missing/implicit.
- The system knows Bosta only if `CarrierCode == "bosta"`; free text `CarrierName = "Bosta"` is insufficient.
- Carrier should be selected from the canonical backend carrier registry/catalog in Step 2.

## 7. Storefront Tenant Scope Failure

FACT: Exact message source is `PointOfSale.Storefront/src/store/checkoutStore.ts:700-702`:

`const expectedTenantId = getRequestHeaders()['X-Tenant-Id'];`

If `expectedTenantId` is not a UUID, it throws `Shipping method response could not be admitted without tenant scope.`

FACT: The per-method tenant validation happens later at `PointOfSale.Storefront/src/store/checkoutStore.ts:712-720`, where each candidate method must have a UUID tenant matching `expectedTenantId`.

FACT: `fetchShippingMethods` calls `/api/storefront/shipping-methods` and then admission at `PointOfSale.Storefront/src/store/checkoutStore.ts:1730-1775`.

FACT: `ShippingStep` calls `fetchShippingMethods` on render when methods are absent at `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx:553-567`, and renders `shippingMethodsError` in the unavailable/error branch at `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx:762-770`.

FACT: Backend storefront shipping controller obtains tenant from trusted server context through `ValidateTenantHeader` at `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs:69-81` and `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs:138-143`.

FACT: Backend returns DTOs with per-method `TenantId` at `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs:17-23`, and service maps `TenantId = doc.TenantId` at `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:367-387`.

Root cause:

INFERENCE: Tenant scope is not lost in the inspected backend controller/service/repository/projection path. The frontend expected tenant is not normalized to backend authoritative GUID when the Storefront resolved tenant is a slug. Shipping admission requires UUID tenant scope from the frontend request context, so it fails before accepting an otherwise valid semantic response.

Smallest safe correction for Step 2:

RECOMMENDATION: Align shipping admission with canonical Storefront tenant authority. Either expose/consume an authoritative GUID tenant scope during bootstrap/API response admission, or add response-level tenant scope that the frontend can validate against a server-authoritative context. Do not make raw client-provided `X-Tenant-Id` authoritative.

## 8. Shipping Response Contract

FACT: `ShippingMethodDto` fields are `TenantId`, `Id`, `Name`, `NameAr`, `Description`, `BaseRate`, `Price`, `Currency`, `RatePerKg`, `FreeShippingThreshold`, `DeliveryDays`, `EstimatedDays`, `CarrierName`, and `SortOrder` at `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs:17-90`.

FACT: `ShippingMethodsListResponseDto` fields are `Status`, `ShippingMethods`, `TotalCount`, `ConfiguredCount`, `ActiveCount`, and `ApplicableCount` at `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs:94-125`.

FACT: No response-level tenant ID, response-level currency, carrier code, warehouse metadata, carrier-account metadata, or per-method applicability reason is present in the DTO.

FACT: Frontend shipping admission supports both legacy raw arrays and the semantic envelope at `PointOfSale.Storefront/src/store/checkoutStore.ts:748-768`.

Classification: D) incompatible with Storefront admission in slug tenant contexts, and C) internally inconsistent with Payment because Shipping uses stricter tenant evidence than Payment without a normalized authoritative tenant GUID in the frontend.

## 9. Projection / SQL / Mongo Trace

FACT: SQL `ShippingMethods` is the write authority. `ShippingMethodProjectionConsumer` comments state PostgreSQL is source of truth and Mongo is the read model at `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs:14-24`.

FACT: Projection consumer rejects missing tenant events at `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs:61-72`, re-fetches from SQL by shipping method ID and tenant ID at `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs:105-110`, resolves tenant currency at `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs:142`, maps `TenantId` and `Currency` into `ShippingMethodDocument` at `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs:333-353`, then upserts and invalidates cache.

FACT: `ShippingMethodReadRepository` reads Mongo collection `shipping_methods` at `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs:28-36`.

FACT: `GetActiveShippingMethodsAsync` filters by `TenantId`, `IsActive`, and non-deleted state and falls back to SQL when Mongo is unhealthy/empty/fails at `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs:64-140`.

FACT: SQL fallback maps `TenantId` and tenant currency into documents at `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs:357-394`.

FACT: `AdminDataSyncController.SyncShippingMethods` also rebuilds Mongo from SQL by current tenant only at `PointOfSale.API/Controllers/AdminDataSyncController.cs:173-195`, sets `TenantId` and `Currency` in the upsert at `PointOfSale.API/Controllers/AdminDataSyncController.cs:317-342`, and deletes stale documents only under the same tenant filter at `PointOfSale.API/Controllers/AdminDataSyncController.cs:421-435`.

INFERENCE: The earlier projected-currency inconsistency and current tenant-scope checkout failure are related only in that both cross the projection/response contract. Source evidence indicates currency and tenant ID are generated/preserved together in projection and SQL fallback; the reported tenant-scope failure is a separate frontend admission contract defect.

NOT VERIFIED FROM AVAILABLE SOURCE: live Mongo `shipping_methods` contents and live SQL/Mongo drift.

## 10. Applicability Rules

FACT: Storefront availability counts are computed in `GetShippingMethodsAvailabilityAsync` at `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:141-177`.

FACT: `configuredCount` means tenant-owned, non-deleted SQL shipping methods.

FACT: `activeCount` means tenant-owned, non-deleted, `IsActive` SQL shipping methods.

FACT: `applicableCount` means number of DTOs returned by `GetShippingMethodsAsync` after read-model fetch, optional warehouse filter, tenant/currency validation, mapping, and price calculation.

FACT: Status is `AVAILABLE` when applicable methods exist; `EMPTY` when configured or active count is zero; otherwise `NOT_APPLICABLE`.

FACT: Currency is resolved from tenant currency in projection/repository fallback and used in DTO mapping at `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:367-387`.

FACT: Deleted/archived semantics are represented by `IsDeleted`; active is `IsActive`.

FACT: Manual methods can be available without carrier account or external API when no warehouse filter removes them.

FACT: With a `warehouseId` query, current code excludes manual from allowed carrier codes at `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:221-223`; this can make manual not applicable in that filtered path.

INFERENCE: Manual can be AVAILABLE with only active shipping method, valid tenant, tenant currency, and configured pricing when checkout does not supply a warehouse filter. A valid warehouse is not currently part of the `ShippingMethod` model.

## 11. Admin to Storefront Lifecycle

FACT:

1. Merchant creates or edits a shipping method in the newer tab.
2. `ShippingMethodsTab` submits to `/shippingmethods` with `carrierName` but no editable `carrierCode`.
3. `ShippingMethodsController` validates DTO, validates carrier code, normalizes missing code to `manual`, and persists SQL.
4. Controller publishes shipping method event through the EF/outbox path.
5. Projection consumer re-fetches SQL by tenant and method ID and upserts Mongo `shipping_methods`.
6. Storefront shipping endpoint reads active Mongo methods or SQL fallback and maps response DTOs.
7. Frontend admission expects a UUID request tenant and per-method tenant evidence.
8. Checkout state is set only after admission succeeds.

Possible disappearance points:

- `carrierCode` can disappear in the newer UI because it is not edited/submitted.
- warehouse metadata cannot appear in shipping-method DTO because it is not on `ShippingMethod` or response DTO.
- carrier-account readiness cannot appear in shipping-method DTO because `ShippingMethod` has no `CarrierAccountId`.
- tenant scope is rejected at frontend admission when the frontend tenant is a slug.
- live projection drift is possible but not verified; source paths preserve tenant ID and currency.

## 12. Payment Methods Comparison

FACT: Payment endpoint is `GET /api/storefront/payment-methods` in `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:68-125`.

FACT: Payment uses `_tenantContext.TenantId is not Guid tenantId` fail-closed validation at `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:68-76`.

FACT: Payment reads enabled payment documents from `GetEnabledPaymentMethodsAsync` at `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:80`.

FACT: Payment response DTO includes `Status`, `PaymentMethods`, `TotalCount`, `ConfiguredCount`, `EnabledCount`, and `AvailableCount` at `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs:198-205`.

FACT: Payment frontend admission is at `PointOfSale.Storefront/src/store/checkoutStore.ts:854-874` and does not require per-method tenant ID.

FACT: Shipping tests use UUID tenant fixtures and verify semantic `AVAILABLE`, `EMPTY`, and `NOT_APPLICABLE` states at `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts:330-421`.

FACT: Payment tests fetch and classify payment methods without tenant evidence in DTOs at `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts:453-460`.

INFERENCE: Shipping should reuse Payment's semantic availability-state pattern but must either remove frontend dependence on raw request tenant UUID or provide a canonical response-level tenant authority. Shipping may still keep per-method tenant evidence if the expected tenant is normalized to server GUID.

## 13. Security / Tenant Isolation Audit

FACT: Backend tenant middleware intentionally strips raw `X-Tenant-Id` and rejects spoofed tenant headers when a trusted tenant is resolved at `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs:81-132`.

FACT: Admin shipping methods use `_tenantContext.RequiredTenantId`, tenant filters, and no-store headers; architecture tests freeze this behavior in `PointOfSale.Tests/Architecture/AdminShippingMethodReadAuthorityCertificationTests.cs`.

FACT: Warehouse assignment validates warehouse existence, tenant ownership, active state, and non-deleted state at `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs:174-180`.

FACT: Storefront shipping repository filters Mongo and SQL fallback by tenant ID at `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs:64-140`.

FACT: Projection sync/upsert/tombstone paths use tenant-scoped filters at `PointOfSale.API/Controllers/AdminDataSyncController.cs:189-195`, `PointOfSale.API/Controllers/AdminDataSyncController.cs:317-342`, and `PointOfSale.API/Controllers/AdminDataSyncController.cs:421-435`.

P0/P1 security issue:

FACT: No source-proven cross-tenant data leakage was found in inspected backend reads/writes.

INFERENCE: The main P0 is production checkout availability, not proven cross-tenant leakage.

P1 correctness/security-adjacent risks:

- Carrier-account assignment lacks an explicit verified EF relationship/FK to `Warehouse` in the inspected `OnModelCreating` block; runtime validates ownership, but database-enforced referential integrity was NOT VERIFIED FROM AVAILABLE SOURCE.
- Newer shipping UI free-text carrier name can misclassify Bosta as manual, creating operational correctness risk.
- Shipping frontend tenant evidence currently depends on raw frontend tenant format, not server-authoritative GUID context.

## 14. Root Cause Matrix

| ID | Severity | Layer | Symptom | Root Cause | Evidence | Affected Component | Recommended Fix | Risk |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SM-P0-001 | P0 | Storefront frontend contract | Checkout shows `Shipping method response could not be admitted without tenant scope.` and methods do not appear | Shipping admission requires `X-Tenant-Id` from frontend request headers to be a UUID, but Storefront tenant resolver can store slug tenants | `checkoutStore.ts:700-702`; `tenantResolver.ts:53-59`, `168-175`, `234-240`; `requestContext.ts:64-76` | Storefront checkout shipping admission | Normalize expected tenant to authoritative GUID or return/consume server-authoritative tenant scope; do not trust raw client tenant header | Blocks checkout shipping selection |
| SM-P1-002 | P1 | API/frontend response contract | Shipping and Payment availability behave differently under same tenant context | Shipping requires per-method tenant evidence and a UUID expected tenant; Payment uses semantic envelope without tenant DTO admission | `ShippingMethodDto.cs:17-23`; `checkoutStore.ts:695-768`; `StorefrontPaymentMethodsController.cs:198-205`; `checkoutStore.ts:854-874` | Storefront availability contracts | Align Shipping with Payment semantic contract plus explicit server tenant authority | Regressions if one endpoint is fixed independently |
| SM-P1-003 | P1 | Carrier/admin domain | Manual Fulfillment appears to require external carrier readiness/account setup | Manual is included in carrier registry and generic CarrierAccount workflow applies external-carrier gates without manual exception | `ShippingAdministrationApiService.cs:26-31`; `CarrierAccountCard.jsx:224-255`; `CarrierAccountAdministrationRuntime.cs:495-579`; `ManualCarrierDefinition.cs:104-115` | Shipping Administration carrier accounts | Exclude manual from external carrier-account setup or add manual-specific no-readiness workflow | Merchants cannot understand or may misconfigure manual fulfillment |
| SM-P1-004 | P1 | Carrier relationship | Typing `Bosta` into carrier display name does not select Bosta integration | Newer shipping method UI edits `carrierName`, not `carrierCode`; backend normalizes missing carrier code to manual | `ShippingMethodsTab.jsx:102-177`, `443-446`; `ShippingMethodsController.cs:156-174` | Shipping Administration shipping methods | Use backend supported-carrier selector and persist canonical `carrierCode` | External carrier methods silently become manual |
| SM-P1-005 | P1 | Runtime authority mismatch | Non-manual method may appear in Storefront list but fail final checkout validation | Storefront list returns active shipping methods; final checkout requires enabled legacy `CarrierConfiguration` for non-manual carrier; newer CarrierAccount readiness is separate | `ShippingMethodService.cs:64-140`; `CheckoutService.cs:5346-5359`; `CarrierAccountAdministrationRuntime.cs:495-579` | Storefront checkout and carrier admin | Define one canonical carrier readiness authority for Storefront list and final submit | Available option can be rejected at order placement |
| SM-P2-006 | P2 | Warehouse admin UX | Merchant must type `Warehouse ID` | Shipping Administration does not consume existing warehouse API selector | `CarrierAccountCard.jsx:232-240`; `WarehousesController.cs:46-57`; `WarehouseOpeningStock.jsx:71-85` | Shipping Administration warehouse assignment | Replace text input with tenant warehouse selector | Misentry and support burden |
| SM-P2-007 | P2 | Data model clarity | Shipping method cannot express warehouse assignment | Assignment is on carrier account only; `ShippingMethod` has no `WarehouseId` or `CarrierAccountId` | `ShippingMethod.cs:8-95`; `CarrierAccountRuntimeEntities.cs:115-119` | Domain model | Decide whether warehouse is carrier-account origin only or shipping-method applicability dimension | Ambiguous fulfillment origin semantics |
| SM-P2-008 | P2 | Projection documentation/code consistency | Service comments say Mongo/Redis only, but service computes SQL counts and has SQL fallback through repository | Read path is dual-read/projection-based, not purely Mongo-only | `ShippingMethodService.cs:141-177`; `ShippingMethodReadRepository.cs:64-140` | Architecture documentation and service comments | Update architecture docs/comments in Step 2 after behavior is settled | Misleading maintenance assumptions |

## 15. Severity Classification

P0:

- SM-P0-001 blocks checkout shipping methods in slug-tenant Storefront contexts.

P1:

- SM-P1-002 shipping/payment contract mismatch.
- SM-P1-003 manual fulfillment external-carrier workflow mismatch.
- SM-P1-004 carrier display name vs canonical carrier code mismatch.
- SM-P1-005 non-manual carrier readiness authority mismatch.

P2:

- SM-P2-006 warehouse ID text input despite canonical warehouse API.
- SM-P2-007 missing/ambiguous warehouse assignment relationship on shipping method.
- SM-P2-008 misleading projection architecture comments.

P3:

- No cosmetic-only findings were material to this audit.

## 16. Recommended Architecture

RECOMMENDATION:

1. Keep backend tenant authority server-side. Do not trust client-supplied `X-Tenant-Id`.
2. Give Storefront a canonical tenant GUID for response admission, either during bootstrap or in availability response envelopes.
3. Align Shipping response semantics with Payment: `AVAILABLE`, `EMPTY`, `NOT_APPLICABLE`, and `UNAVAILABLE/ERROR` should be admitted based on the envelope before per-method evidence checks.
4. Keep Manual Fulfillment as `CarrierCodes.Manual` / no-op adapter for shipment lifecycle, but do not expose external-carrier credential, connection-test, carrier-account readiness, or API URL requirements to merchants.
5. Use `IShippingCarrierRegistry` as the carrier catalog source for UI selectors. Persist `carrierCode` as authority; treat `carrierName` as display metadata only.
6. Decide one canonical external-carrier readiness authority. Either converge checkout on `CarrierAccount` readiness or keep legacy `CarrierConfiguration` as the active runtime authority, but do not let admin configure one while checkout validates another.
7. Use the existing `/api/warehouses` API for a tenant-scoped active warehouse selector.
8. Clarify whether warehouse assignment is carrier-account origin, shipping-method applicability, or both. Persist relationships accordingly only in Step 2.

## 17. Minimal Remediation Plan

RECOMMENDATION for Step 2, smallest safe order:

1. Fix Storefront shipping tenant admission so slug-resolved storefronts can admit server-authoritative GUID responses.
2. Add regression tests for slug tenant resolution plus shipping availability admission.
3. Align Shipping availability contract with Payment's semantic EMPTY/AVAILABLE handling while preserving tenant isolation evidence.
4. Fix Shipping Administration `ShippingMethodsTab` to select canonical `carrierCode` from supported carriers and display `carrierName` only as metadata.
5. Hide or bypass carrier-account credential/readiness workflow for manual fulfillment.
6. Replace `Warehouse ID` free-text input with active tenant warehouse selector backed by `/api/warehouses`.
7. Decide and implement the runtime readiness authority for non-manual carriers so Storefront list and final checkout submission agree.

## 18. What MUST NOT Be Changed

RECOMMENDATION:

- Do not make raw frontend `X-Tenant-Id` authoritative.
- Do not weaken `TenantResolutionMiddleware` spoofing protection.
- Do not remove tenant filters from SQL or Mongo reads.
- Do not require external API credentials for Manual Fulfillment.
- Do not require carrier-account readiness for manual shipping methods unless a separate explicit manual account concept is designed.
- Do not treat `CarrierName` as carrier authority.
- Do not remove SQL authority/projection fallback without a broader read-model migration plan.
- Do not create database migrations or schema changes until Step 2 design is approved.

## 19. Phase 2 Implementation Plan

RECOMMENDATION:

Phase 2A, checkout unblock:

- Introduce a canonical Storefront tenant scope for shipping admission.
- Add tests where `setResolvedTenant('shop-slug')` or subdomain slug produces successful shipping admission against GUID DTO evidence.
- Verify Payment remains unchanged or is explicitly aligned.

Phase 2B, admin carrier/method model:

- Modify shipping-method editor to consume supported carriers and submit `carrierCode`.
- Make `manual` contextual: no credentials, no API URL, no connection test, no external readiness.
- Ensure Bosta selection persists `carrierCode: 'bosta'`.

Phase 2C, warehouse UX and domain:

- Add tenant warehouse selector to carrier-account warehouse assignment.
- Keep backend Guid route validation.
- If shipping methods need warehouse applicability, add explicit model/API contract only after deciding relationship ownership.

Phase 2D, readiness convergence:

- Decide whether `CarrierConfiguration` or `CarrierAccount` is final checkout authority.
- Make Storefront list and checkout submission use the same readiness predicate.
- Add tests for active non-manual shipping method with missing/unready carrier authority.

## 20. Verification Plan

RECOMMENDATION:

- Unit/frontend test: slug tenant plus shipping envelope with GUID method tenant is admitted.
- Unit/frontend test: foreign tenant GUID in shipping method is rejected.
- Unit/frontend test: EMPTY and NOT_APPLICABLE shipping envelopes require no per-method tenant evidence.
- API test: Storefront shipping endpoint returns semantic counts and tenant-owned methods only.
- API test: manual shipping method is available without carrier account, credentials, or external API.
- API test: non-manual method list and final checkout submission agree on readiness.
- Admin UI test: selecting Bosta sends `carrierCode: 'bosta'`.
- Admin UI test: manual selection hides external credential/connection/readiness controls.
- Admin UI test: warehouse assignment uses existing tenant active warehouse list.
- Projection test: shipping method sync/upsert preserves `TenantId` and tenant currency.
- Security test: cross-tenant warehouse assignment is rejected.
- Security test: spoofed `X-Tenant-Id` remains rejected by middleware.

## 21. Open Questions

- NOT VERIFIED FROM AVAILABLE SOURCE: live production/staging SQL `ShippingMethods` rows.
- NOT VERIFIED FROM AVAILABLE SOURCE: live Mongo `shipping_methods` documents.
- NOT VERIFIED FROM AVAILABLE SOURCE: whether current affected tenant is reached by slug subdomain, custom domain, theme preview, or dev query/env tenant.
- NOT VERIFIED FROM AVAILABLE SOURCE: whether the reported store has active SQL shipping methods that are not projected to Mongo.
- NOT VERIFIED FROM AVAILABLE SOURCE: database-enforced FK constraints from `CarrierWarehouseAssignment.WarehouseId` to `Warehouses.Id`; runtime ownership validation is verified.
- NOT VERIFIED FROM AVAILABLE SOURCE: whether merchants are using older shipping module, newer shipping-administration module, or both in production.

## 22. Final Certification

BLOCKED — Material P0/P1 issues require remediation before certification.
