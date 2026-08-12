# SHOPIXY Shipping Methods Step 2B Forensic Audit

Date: 2026-08-11

Scope: forensic reconstruction only. No source code, configuration, schema, migration, test, data, browser, git, or live infrastructure changes were performed.

Final certification: PASS WITH EXTERNAL VERIFICATION BLOCKED

## 1. Executive Summary

The repository already contains a canonical carrier-code architecture. The authoritative identity is `CarrierCode`, not `CarrierName`. Canonical codes are declared in `PointOfSale.Core/Constants/CarrierCodes.cs`, surfaced through `IShippingCarrierAdapter.CarrierCode`, registered through `ShippingCarrierRegistry`, and wired in `PointOfSale.API/Program.cs`.

The current Shipping Method admin form does not honor that architecture. `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx` exposes only a free-text carrier display field and does not submit `carrierCode`. The backend then normalizes missing carrier code to `manual`. Therefore a merchant can create a Shipping Method with `CarrierName = "Bosta"` while the persisted canonical `CarrierCode` is `manual`. That method is displayed as Bosta-like presentation data but executes as Manual Fulfillment.

Manual Fulfillment is canonical in backend code as `manual` and is registered as `ManualCarrierDefinition`. Its conceptual behavior is local fulfillment with no external carrier credentials, no external connection test, and no external readiness dependency. That is correctly represented in checkout, shipment registration, fulfillment ownership, and webhook rejection paths. It is incorrectly mixed into the newer CarrierAccount administration surface because the runtime accepts any registry-supported carrier, including `manual`, and then applies credential, connection-test, warehouse, mapping, provider-capability, and service-level activation gates.

`ShippingMethod` does not reference `CarrierAccount` or `Warehouse`. `CarrierAccount` is tenant-scoped by `CarrierCode`. `CarrierWarehouseAssignment` is carrier-account-level, not shipping-method-level. Current EF configuration enforces one non-deleted `CarrierAccount` per `(TenantId, CarrierCode)`, so multiple accounts per carrier are not supported by the current model.

No schema migration is required to change the admin Shipping Method contract from free-text carrier display name to canonical carrier selection because `ShippingMethod.CarrierCode` already exists. Existing data may still require normalization where `CarrierName` implies an external carrier but `CarrierCode` is null, empty, or `manual`.

## 2. Canonical Carrier Model

Authoritative identity: `CarrierCode`.

Evidence:

- `PointOfSale.Core/Constants/CarrierCodes.cs` defines `manual`, `bosta`, `aramex`, `mylerz`, `dhl`, and `fedex`.
- `CarrierCodes.Normalize` maps null or blank to `manual`.
- `CarrierCodes.IsManual` treats null, blank, or `manual` as Manual Fulfillment.
- `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs` defines adapter identity through `CarrierCode` and presentation through `DisplayName`.
- `PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs` builds an adapter registry keyed by `CarrierCode`.
- `PointOfSale.API/Program.cs` registers `ManualCarrierDefinition`, `BostaAdapter`, `AramexAdapter`, `MylerzAdapter`, `DhlAdapter`, and `FedExAdapter`.
- `PointOfSale.Tests/Architecture/CarrierIntegrationAuthorityCertificationTests.cs` certifies these constants and registrations.

Canonical answers:

| Question | Finding |
|---|---|
| What is canonical carrier identity? | `CarrierCode`. |
| Is it CarrierCode, CarrierId, enum, registry key, or other? | String carrier code, normalized by `CarrierCodes`, resolved through `ShippingCarrierRegistry`. |
| Where is the authoritative supported list? | Code constants plus DI-registered `IShippingCarrierAdapter` instances. Runtime API uses the registry. |
| Is Bosta canonical? | Yes: `bosta`. |
| Is Manual Fulfillment canonical? | Yes: `manual`. |
| Is there an enum or registry containing manual? | No enum. Manual is a constant and registered adapter. |
| Is CarrierName identity? | No. It is presentation metadata and currently arbitrary. |
| Does ShippingMethod store CarrierAccountId or WarehouseId? | No. It stores `CarrierName`, `CarrierCode`, and `ServiceLevel`, but no carrier-account or warehouse foreign key. |

End-to-end trace:

| Layer | Contract |
|---|---|
| Domain | `ShippingMethod.CarrierCode` is adapter link; null means manual in comments. `CarrierName` is display text. |
| EF/database | `CarrierCode` nullable text, `CarrierName` nullable text, no carrier FK, no warehouse FK, no carrier-account FK. |
| API DTO | Admin GET/POST/PUT includes `CarrierCode` and `CarrierName`. DTO lacks `ServiceLevel`. |
| Service/controller | `ShippingMethodsController` validates `CarrierCode` against registry and normalizes missing code to `manual`; stores arbitrary `CarrierName`. |
| Admin UI | Shipping administration tab edits only free-text `carrierName`; it does not submit `carrierCode`. |
| Storefront DTO | Exposes `CarrierName`, not `CarrierCode`. |
| Storefront runtime | Uses projected active shipping methods; warehouse filter uses hidden `CarrierCode`. |

Mismatch: the repository has a canonical `CarrierCode` architecture, but the current admin Shipping Method form still treats carrier display name as the merchant-facing selection contract.

## 3. ShippingMethod Domain Contract

`PointOfSale.Core/Entities/ShippingMethod.cs` carrier-related properties:

- `CarrierName`: optional presentation carrier/provider name.
- `CarrierCode`: optional canonical adapter code; comments state null means manual.
- `ServiceLevel`: required string defaulting to `standard`.
- `TrackingUrlTemplate`: optional presentation/template field.

No direct relationships exist to:

- `CarrierAccount`
- `CarrierConfiguration`
- `CarrierWarehouseAssignment`
- `Warehouse`

Other key properties:

- `TenantId`: required and tenant-owned.
- `Name`: required.
- `BaseRate`, `PerKgRate`, `FreeShippingThreshold`.
- `EstimatedDeliveryDaysMin`, `EstimatedDeliveryDaysMax`.
- `IsActive`, `SortOrder`, `AvailableCountries`.

EF findings:

- `ApplicationDbContext` applies tenant and soft-delete query filters to `ShippingMethod`.
- Current model snapshot shows `CarrierCode` and `CarrierName` as nullable columns on `ShippingMethods`.
- No carrier-related index, FK, or constraint is present for `ShippingMethod`.
- Global seed data for `ShippingMethod` was removed because global methods violated tenant isolation.

Validation findings:

- `ShippingMethodsController.ValidateCarrierCode` validates only `CarrierCode`.
- `CarrierName` is not validated against the registry.
- Missing or blank `CarrierCode` is accepted and normalized to `manual`.
- DTO validation covers name, rates, delivery-day ranges, sort order, and free-shipping threshold. It does not validate `CarrierName` as authority.

Explicit answer:

Yes, a merchant can currently create `CarrierName = "Bosta"` without connecting the method to Bosta integration. In the current admin Shipping Methods tab, the form sends `carrierName` but no `carrierCode`. The backend create/update path accepts that, normalizes missing `CarrierCode` to `manual`, and persists arbitrary `CarrierName`. The result can display "Bosta" while the canonical runtime path treats it as Manual Fulfillment.

Representation safety:

| Case | Current model capable? | Assessment |
|---|---:|---|
| Manual Fulfillment | Yes, if `CarrierCode` is `manual`, null, or blank. | Safe in backend runtime, unsafe in admin UI because free text can contradict it. |
| Bosta | Yes, if `CarrierCode = "bosta"`. | Unsafe through current shipping-administration UI because it omits `carrierCode`. |
| Other external carrier | Yes, if registry-supported `CarrierCode` is persisted. | Ambiguous if only `CarrierName` is populated. |

## 4. Manual Fulfillment Forensics

Manual Fulfillment is represented canonically as `manual`.

Evidence:

- `CarrierCodes.Manual = "manual"`.
- `ManualCarrierDefinition.CarrierCode` returns `manual`.
- `ManualCarrierDefinition.DisplayName` returns `Manual Fulfillment`.
- `ManualCarrierDefinition` returns no external shipping label, no external reference, no tracking number, no webhook parsing, and a local pending shipment state.
- `CheckoutService.DetermineFulfillmentMode` maps null, blank, or `manual` to `FulfillmentMode.Manual`.
- `FulfillmentMode.Manual` is documented as admin-controlled fulfillment with no carrier integration and no carrier webhooks.
- `Order` fulfillment snapshot invariants require manual orders to have no fulfillment carrier code.
- `OrderFulfillmentService` rejects manual actions against carrier-managed orders and rejects carrier webhooks against manual-owned fulfillment.
- `ShipmentService.CreateShipmentAsync` skips CarrierConfiguration dependency for manual methods and creates non-carrier-managed shipments.
- `CarrierShipmentService` skips external carrier execution for non-carrier-managed manual shipments.
- Webhook resolution rejects manual carrier codes.

Field requirements:

| Field | Required for Manual? | Required for External Carrier? | Why | Authoritative source |
|---|---:|---:|---|---|
| Carrier code | Yes, conceptually `manual`; current backend also accepts null/blank as manual. | Yes, registry-supported external code. | Runtime identity. | `CarrierCodes`, `ShippingMethod`, `ShippingMethodsController`. |
| Carrier name | Display only, should derive to `Manual Fulfillment`. | Display only, should derive from adapter display name. | Not validated; not identity. | `ShippingMethod`, adapters. |
| Carrier account | No. | Not directly on ShippingMethod; required by new carrier-account readiness surfaces. | ShippingMethod has no FK. | `ShippingMethod`, `CarrierAccountRuntimeEntities`. |
| Credential reference | No. | Yes for CarrierAccount activation. | Activation gate requires active credential reference. | `CarrierAccountAdministrationRuntime`. |
| Warehouse assignment | No external readiness dependency. | Required by CarrierAccount activation and storefront warehouse filter. | Assignment is account-level. | `CarrierWarehouseAssignment`, `ShippingMethodService`. |
| API key | No. | Required by new connection-test API and generally external adapters. | API service fails blank API key. | `ShippingAdministrationApiService`, adapter credentials. |
| API secret | No. | Carrier-specific optional/required. | Credential metadata. | `CarrierCredentials`, legacy `CarrierConfiguration`. |
| API URL | No. | Optional/depends on carrier/environment. | Credential metadata. | `CarrierCredentials`, admin carrier config DTO. |
| Account ID | No. | Optional/depends on carrier. | Credential metadata. | `CarrierCredentials`, admin carrier config DTO. |
| Connection test | No. | Required for CarrierAccount activation. | Activation gate requires successful connection test. | `CarrierAccountAdministrationRuntime`. |
| Readiness evidence | No. | Required for CarrierAccount activation. | Mapping/provider readiness gates. | `CarrierReadinessEvidence`. |
| Enabled services | Manual exposes `standard`; no external dependency. | Required only if service levels are enabled and supported. | Unsupported enabled levels block activation. | adapters, runtime service-level gate. |
| Base rate | Yes. | Yes. | Shipping price contract belongs to ShippingMethod. | `ShippingMethod`, DTO validation. |
| Rate per kg | Yes if weight pricing is used. | Yes if weight pricing is used. | Shipping price contract. | `ShippingMethod`, calculation service. |
| Free shipping threshold | Optional. | Optional. | Shipping price contract. | `ShippingMethod`, DTO validation. |
| Min days | Yes. | Yes. | Storefront delivery presentation. | `ShippingMethod`, storefront DTO. |
| Max days | Yes. | Yes. | Storefront delivery presentation. | `ShippingMethod`, storefront DTO. |
| Tracking URL | Optional. | Optional; carrier may supply tracking URL later. | Presentation/template field. | `ShippingMethod`, shipment/tracking services. |

Manual defect: the new CarrierAccount administration path can create a `manual` account because it accepts any registry-supported carrier. Once created, it applies external readiness gates to manual. Manual should bypass CarrierAccount entirely.

## 5. CarrierAccount Relationship

Current relationship:

- `ShippingMethod` does not reference `CarrierAccount`.
- `CarrierAccount` references `TenantId` and `CarrierCode`.
- `CarrierCredentialReference`, `CarrierWarehouseAssignment`, `CarrierAccountPolicy`, `CarrierServiceLevelEnablement`, and `CarrierConnectionTestRecord` reference `CarrierAccountId`.
- EF has a unique filtered index on `(TenantId, CarrierCode)` for `CarrierAccount`.

Answers:

| Question | Finding |
|---|---|
| Does ShippingMethod directly reference CarrierAccount? | No. |
| Does CarrierAccount reference a carrier code? | Yes: `CarrierAccount.CarrierCode`. |
| Is there a join/assignment table? | Yes, for account-to-warehouse and account-to-service-level. Not for shipping-method-to-account. |
| Can multiple CarrierAccounts exist for the same carrier? | Not as active/non-deleted rows for one tenant under current EF unique index. |
| Can different warehouses use different accounts? | Not for the same carrier under the current uniqueness rule. One carrier account can have multiple warehouse assignments. |
| Is account selection supposed to happen when creating ShippingMethod? | Current model says no. ShippingMethod selects carrier code only. |
| Or is account selection separate carrier configuration? | Yes, current architecture separates account/configuration from method pricing/display. |
| What happens if no CarrierAccount is ready? | Storefront with warehouse filter excludes external methods. Checkout still uses legacy `CarrierConfiguration` enabled state, not CarrierAccount readiness. |
| Does Manual bypass CarrierAccount entirely? | Runtime shipping/checkout paths do, but CarrierAccount admin currently allows manual account creation and external gates. |

Defect: two readiness/configuration authorities exist for external carriers: legacy `CarrierConfiguration` and newer `CarrierAccount`. Storefront warehouse eligibility uses `CarrierAccount`; checkout/shipment carrier registration uses `CarrierConfiguration`.

## 6. Warehouse Relationship

Existing intended architecture:

- Warehouse assignment is carrier-account-level.
- It is not shipping-method-level.
- It is not order-level in the ShippingMethod contract.
- It is tenant-scoped because warehouses, carrier accounts, and assignments all carry `TenantId`.

Evidence:

- `CarrierWarehouseAssignment` has `CarrierAccountId`, `WarehouseId`, `IsEnabled`, `OriginReadinessPublished`, and readiness evidence.
- `ShippingAdministrationApiService.AssignWarehouseAsync` validates a non-empty warehouse id and delegates to runtime.
- Runtime verifies the warehouse exists for the same tenant before creating/updating assignment.
- `AdminShippingAdministrationController` endpoint is `PUT /api/admin/shipping/carrier-accounts/{carrierAccountId}/warehouse-assignments/{warehouseId}`.
- `CarrierAccountCard.jsx` asks for a raw "Warehouse ID" because the API command is a `Guid WarehouseId` and the component does not load a warehouse selector.
- `Storefront/ShippingMethodService.ApplyWarehouseAssignmentFilterAsync` filters external methods by active/ready carrier accounts with enabled origin-ready warehouse assignments.

Classification:

| Work item | Classification |
|---|---|
| Replace raw Warehouse Assignment ID with a selector | Step 2C. |
| Keep ShippingMethod free of direct WarehouseId for Step 2B | Safe for Step 2B. |
| Allow multiple accounts per carrier/warehouse routing | Requires domain redesign. |
| Decide whether manual methods survive warehouse-specific storefront filtering | Step 2B policy decision or Step 2C if tied to warehouse/origin redesign. Current tests encode exclusion. |

## 7. Carrier Readiness Authority

Carrier account states:

- `Draft`
- `CredentialsPending`
- `ConnectionTestPending`
- `ReadyForActivation`
- `Active`
- `Suspended`
- `Disabled`
- `Retired`

Operational states:

- `NotReady`
- `Ready`
- `Suspended`
- `Disabled`
- `Retired`

Health states:

- `Healthy`
- `Warning`
- `Blocked`
- `Disabled`
- `Retired`

Activation gates:

- Active credential reference.
- Environment selected.
- Successful connection test.
- At least one enabled warehouse assignment.
- Every enabled warehouse assignment has origin readiness published.
- Carrier mapping readiness is explicitly published.
- Provider capability readiness is explicitly supported.
- International customs readiness if international policy is enabled.
- Enabled service levels must be supported by adapter.

ShippingMethod readiness:

- ShippingMethod has only `IsActive`; it does not have Draft, Ready, Blocked, or NotReady.
- Storefront availability status is aggregate: `AVAILABLE`, `EMPTY`, or `NOT_APPLICABLE`.
- Checkout external readiness uses legacy `CarrierConfiguration.IsEnabled`, not CarrierAccount operational state.
- Storefront warehouse-filter external readiness uses CarrierAccount `Active` plus `Ready` plus origin-ready warehouse assignment.

Manual Fulfillment should not be blocked by:

- missing credentials
- unreachable API
- failed connection test
- missing CarrierAccount
- missing external readiness evidence

Backend runtime mostly proves this in checkout, shipment, fulfillment, and webhook paths. The incorrect coupling is in carrier-account administration, where `manual` can be registered and then blocked by external readiness gates.

## 8. Supported Carrier Registry

Canonical table discovered from constants, adapter registrations, and adapter display names:

| Code | Display Name | External/Internal | Account Required | Readiness Required | Warehouse Dependency |
|---|---|---|---:|---:|---|
| `manual` | Manual Fulfillment | Internal | No | No | No external warehouse readiness dependency. |
| `bosta` | Bosta | External | Yes for external carrier account/configuration. | Yes for CarrierAccount activation; legacy checkout requires enabled CarrierConfiguration. | CarrierAccount warehouse assignment used by storefront warehouse filter. |
| `aramex` | Aramex | External | Yes for external carrier account/configuration. | Yes for CarrierAccount activation; legacy checkout requires enabled CarrierConfiguration. | CarrierAccount warehouse assignment used by storefront warehouse filter. |
| `mylerz` | Mylerz | External | Yes for external carrier account/configuration. | Yes for CarrierAccount activation; legacy checkout requires enabled CarrierConfiguration. | CarrierAccount warehouse assignment used by storefront warehouse filter. |
| `dhl` | DHL Express | External | Yes for external carrier account/configuration. | Yes for CarrierAccount activation; legacy checkout requires enabled CarrierConfiguration. | CarrierAccount warehouse assignment used by storefront warehouse filter. |
| `fedex` | FedEx | External | Yes for external carrier account/configuration. | Yes for CarrierAccount activation; legacy checkout requires enabled CarrierConfiguration. | CarrierAccount warehouse assignment used by storefront warehouse filter. |

No additional supported carrier should be invented in Step 2B.

## 9. Admin UI Contract

Current shipping-administration UI findings:

- `ShippingMethodsTab.jsx` uses `carrierName` as a free-text input labeled through `shipping.carrier` and placeholder `Carrier display name`.
- Its form state does not include `carrierCode`.
- Its submit payload does not include `carrierCode`.
- Therefore backend creates/updates shipping methods as `manual` unless some other caller supplies `CarrierCode`.
- `ShippingAdministrationWorkspace.jsx` already fetches `supportedCarriers` for the Carrier Accounts tab.
- `shippingAdministrationApi.js` exposes `fetchSupportedCarriers()` from `/api/admin/shipping/supported-carriers`.
- Legacy shipping setup UI has a carrier selector in `ShippingOptionFormModal.jsx`, including `carrierCode`, but this is not wired into the newer shipping-administration Shipping Methods tab.
- Legacy `CarrierConfigurationModal.jsx` correctly special-cases `manual` by hiding credentials and tests.
- New `CarrierAccountCard.jsx` exposes credential reference, environment, connection test, warehouse assignment, policies, services, activation, health, and audit for every account, with no manual-specific bypass.
- Frontend duplicates carrier display metadata in `useShippingSetupApi.js` `CARRIER_INFO`, including `manual`, `bosta`, `aramex`, `mylerz`, and `dhl`; it omits `fedex`.

Desired UI contract:

| Carrier selection | Shipping Method fields | Carrier account/config fields | Readiness display |
|---|---|---|---|
| Manual Fulfillment | Name, rates, free threshold, delivery days, tracking template optional, active/sort/countries. Persist `CarrierCode = manual`; display name derived. | None. No API key, secret, URL, account ID, credential reference, connection test, external warehouse assignment. | Always no external readiness dependency. |
| Bosta | Same Shipping Method pricing/display fields plus `CarrierCode = bosta`; service level should be selectable from Bosta adapter metadata if API supports it. | Separate Bosta carrier account/configuration surface. | Show readiness summary from carrier account/configuration, but do not ask for credentials in the Shipping Method form. |
| Generic external carrier | Same as Bosta, using registry-supported `CarrierCode`; display name derived from adapter metadata. | Separate account/configuration surface. | External readiness may block availability depending on resolved Step 2B/2C authority. |

The admin Shipping Method form should never allow the merchant to author arbitrary carrier identity through `CarrierName`.

## 10. Storefront Contract

Endpoint:

- `GET /api/storefront/shipping-methods`

Controller behavior:

- Anonymous storefront endpoint.
- Resolves and validates tenant through storefront tenant context.
- Requires session/customer authority.
- Loads the cart, subtotal, total weight, and optional `warehouseId`.
- Calls `ShippingMethodService.GetShippingMethodsAvailabilityAsync`.

DTO:

- Exposes `TenantId`, `Id`, `Name`, `NameAr`, `Description`, `BaseRate`, `Price`, `Currency`, `RatePerKg`, `FreeShippingThreshold`, `DeliveryDays`, `EstimatedDays`, `CarrierName`, and `SortOrder`.
- Does not expose `CarrierCode`.

Runtime:

- Without warehouse filter, projected active methods are returned from the read repository/cache after tenant and currency consistency validation.
- With warehouse filter, cache is bypassed and external carrier-backed methods are filtered by account/warehouse readiness.
- The filter excludes manual methods because it only keeps non-manual carrier codes present in the allowed carrier set.
- Storefront display uses shipping method name and price; carrier code does not appear in storefront type definitions.

Findings:

- Manual Fulfillment can appear in storefront when no warehouse filter is supplied and the method is active/projected.
- Manual Fulfillment is excluded when `warehouseId` is supplied under current filter behavior.
- External carrier methods can appear without CarrierAccount readiness when no warehouse filter is supplied.
- External carrier methods can be shown in storefront and later rejected by checkout if the legacy `CarrierConfiguration` is missing or disabled.
- Step 2A tenant scope appears intact in the observed controller/service paths: tenant resolution, SQL counts, read repository calls, and DTO admission all carry the tenant id.

## 11. API Contract

Admin Shipping Methods:

| Operation | Contract | Validation/authority |
|---|---|---|
| GET `/api/shippingmethods` | Returns `ShippingMethodDto` with `CarrierName` and `CarrierCode`. | Tenant-scoped query, optional active-only filter. |
| GET `/api/shippingmethods/{id}` | Returns one tenant-owned method. | Tenant and soft-delete scoped. |
| POST `/api/shippingmethods` | Accepts `CreateShippingMethodDto`. | DTO validation plus registry validation of `CarrierCode`; normalizes missing code to `manual`; arbitrary `CarrierName` stored. |
| PUT `/api/shippingmethods/{id}` | Accepts `UpdateShippingMethodDto`. | Same carrier-code validation; tenant-scoped update. |
| DELETE `/api/shippingmethods/{id}` | Soft delete. | Tenant-scoped. |

Admin Carrier Configuration:

| Operation | Contract | Validation/authority |
|---|---|---|
| GET `/api/admin/carriers/supported` | Returns registry adapters with service levels. | Registry authority. |
| GET `/api/admin/carriers` | Returns tenant carrier configurations plus unsupported templates. | Tenant-scoped `CarrierConfiguration`. |
| PUT `/api/admin/carriers/{carrierCode}` | Upserts legacy carrier config and credentials. | Registry validates carrier. |
| POST `/api/admin/carriers/{carrierCode}/validate` | Tests legacy CarrierConfiguration credentials. | Adapter validation. |

Admin Shipping Administration:

| Operation | Contract | Validation/authority |
|---|---|---|
| GET `/api/admin/shipping/supported-carriers` | Returns registry adapters with service levels. | Registry authority. |
| GET `/api/admin/shipping/carrier-accounts` | Returns tenant-scoped carrier accounts. | Tenant-scoped read. |
| POST `/api/admin/shipping/carrier-accounts` | Creates account by `CarrierCode`. | Runtime validates against registry. |
| PUT credential/environment/warehouse/policy/service level | Mutates account-owned readiness records. | Runtime validates tenant-owned warehouse/account and enum/service level. |
| POST connection-tests | Requires API key at API layer. | Adapter validation through runtime. |
| POST activate/health | Applies readiness gates. | Runtime state machine. |

Storefront:

| Operation | Contract | Validation/authority |
|---|---|---|
| GET `/api/storefront/shipping-methods` | Returns availability wrapper and storefront shipping method DTOs. | Tenant/session/cart authority, read model tenant scope, optional warehouse filter. |

Migration assessment for `carrierName -> carrierCode`:

- No schema migration is required for Step 2B carrier selection because `ShippingMethod.CarrierCode` already exists.
- API/UI changes can use the existing field.
- Data normalization may be required for existing rows whose `CarrierName` implies an external carrier but whose `CarrierCode` is null, empty, or `manual`.

## 12. Existing Data / Backward Compatibility

Historical artifacts:

- `DB/sql/add_carrier_code_to_shipping_methods.sql` adds nullable `CarrierCode` and documents null as manual.
- `DB/sql/fix_shipping_carrier_integration.sql` shows a remediation path where a "Standard Shipping" method with Bosta display data is updated to `CarrierCode = 'bosta'`.
- Current migrations/model snapshot include nullable `CarrierCode` on `ShippingMethods`.
- Some DB snapshots contain historical drift around fulfillment fields on shipping methods; current EF entity and model snapshot do not contain `ShippingMethod.FulfillmentMode`.

Risk classification:

| Item | Classification | Reason |
|---|---|---|
| Existing canonical codes | SAFE | Constants and registry are explicit. |
| Existing rows with `CarrierCode = bosta/aramex/mylerz/dhl/fedex` | SAFE | Canonical external identity is present. |
| Existing rows with null/blank/manual and `CarrierName = Manual Fulfillment` | SAFE | Runtime semantics are manual. |
| Existing rows with null/blank/manual and external-looking `CarrierName` | NEEDS NORMALIZATION | They display as external but run as manual. |
| Existing arbitrary carrier names | NEEDS NORMALIZATION | They cannot be mapped without data review. |
| Live production distribution | UNKNOWN | Live infrastructure/data was not inspected. |
| Schema change for carrier selector | SAFE, no schema migration | `CarrierCode` already exists. |
| Data backfill | MAY REQUIRE DATA MIGRATION | Depends on live rows and accepted normalization rules. |

## 13. Tenant Isolation / Security

Verified from code paths:

- Carrier definitions are global code/adapter definitions, not tenant-owned.
- `CarrierConfiguration` is tenant-scoped.
- `CarrierAccount` is tenant-scoped.
- `CarrierWarehouseAssignment` is tenant-scoped and references tenant-owned warehouse/account.
- `ShippingMethod` is tenant-scoped and soft-delete filtered.
- Admin shipping-method reads and writes require tenant context.
- Admin carrier-account APIs use tenant context and do not accept tenant id from request body.
- Storefront shipping-method reads resolve tenant through storefront authority and keep tenant id in read model admission.
- Warehouse assignment runtime checks the warehouse belongs to the same tenant.

Security risks to preserve against in Step 2B:

- Carrier selector must use global registry metadata only and must not allow tenant-authored carrier codes outside the registry.
- Admin must not select another tenant's CarrierAccount or Warehouse.
- Storefront must not reveal another tenant's shipping methods through cache keys, read projections, or warehouse filters.
- `CarrierName` must not remain a tenant-authored authority field that can spoof external carrier identity.

## 14. Test Contract

Existing coverage:

- `CarrierIntegrationAuthorityCertificationTests.cs` certifies canonical codes, adapter registration, backend carrier authority, manual webhook rejection, and frontend cache non-authority.
- `PosAdminShippingAdministrationRuntimeTests.cs` certifies carrier-account state machine, activation gates, connection test boundary, audit behavior, and runtime isolation from shipment runtime.
- `PosAdminShippingAdministrationApiTests.cs` certifies admin shipping administration route, DTO secrecy, command/query separation, activation fail-closed, and validation failures.
- `ShippingMethodServiceCurrencyConsistencyTests.cs` certifies storefront currency consistency, warehouse carrier filter, fail-closed warehouse behavior, and availability status.
- Step 2A-related architecture tests cover admin/storefront shipping method tenant read authority.
- Shipping-method authority tests cover identity, ownership, eligibility, final authority, and calculation/session behavior.

Missing or stale coverage:

- No test certifies that the current shipping-administration Shipping Methods tab submits `carrierCode`.
- No test prevents `CarrierName = "Bosta"` with missing `CarrierCode` from persisting as manual.
- No test certifies a canonical carrier selector for Shipping Methods.
- No test certifies that Manual Fulfillment is excluded from CarrierAccount credential/connection/readiness UX.
- Current warehouse storefront test encodes manual exclusion when `warehouseId` is supplied. That may be stale if Manual Fulfillment should be warehouse-independent.
- No test resolves the dual authority between legacy `CarrierConfiguration` and newer `CarrierAccount` readiness.
- DTO tests do not cover `ShippingMethod.ServiceLevel` omission from create/update API.

Step 2B tests should be changed or added after implementation planning, not in this audit.

## 15. Exact Defect List

1. `ShippingMethodsTab.jsx` uses free-text `carrierName` as the merchant carrier contract.
2. `ShippingMethodsTab.jsx` does not submit `carrierCode`, causing backend normalization to `manual`.
3. `CarrierName` can contradict `CarrierCode`, allowing Bosta display with manual runtime behavior.
4. `CreateShippingMethodDto` and `UpdateShippingMethodDto` omit `ServiceLevel` even though `ShippingMethod` has `ServiceLevel` and legacy UI submits it.
5. Manual Fulfillment can be represented as a CarrierAccount and then blocked by external credentials, connection-test, warehouse, and readiness gates.
6. `ConnectionTestCommand` requires `ApiKey`, which is wrong for any manual account path.
7. Carrier readiness authority is split: legacy `CarrierConfiguration` gates checkout/shipment external carrier use, while `CarrierAccount` gates admin readiness and storefront warehouse filtering.
8. Storefront warehouse filtering excludes manual methods when `warehouseId` is supplied.
9. Admin warehouse assignment UI asks for raw Warehouse ID because no selector/warehouse lookup is integrated.
10. Frontend carrier metadata is duplicated in `CARRIER_INFO` and is missing `fedex`.
11. Supported carrier APIs exist in two places: legacy `/api/admin/carriers/supported` and newer `/api/admin/shipping/supported-carriers`.
12. ShippingMethod has no CarrierAccount or Warehouse relation; this is acceptable for current single-account-per-carrier architecture but ambiguous for future multi-account routing.
13. Existing data may contain arbitrary or misleading `CarrierName` values that require normalization before canonical behavior is reliable.

## 16. Recommended Step 2B Changes

Implementation sequence for Step 2B:

1. Keep schema unchanged.
2. Treat `ShippingMethod.CarrierCode` as the only carrier identity.
3. Update the shipping-administration Shipping Methods form to load supported carriers from backend metadata and render a canonical carrier selector.
4. Persist selected `carrierCode` on create/update.
5. Derive or validate `CarrierName` from adapter display name for backward-compatible response display; do not allow arbitrary authoring of carrier identity.
6. Default new methods to `manual` / Manual Fulfillment explicitly.
7. Hide all external carrier-account and credential fields for Manual Fulfillment.
8. Prevent or bypass manual CarrierAccount creation in the newer carrier administration surface.
9. Add targeted tests for manual selection, Bosta selection, arbitrary carrier-name spoofing prevention, and supported-carrier selector behavior.
10. Decide whether Step 2B will only fix ShippingMethod carrier selection or also align storefront external readiness. If readiness unification is included, explicitly choose between legacy `CarrierConfiguration` and `CarrierAccount` authority first.

## 17. Explicitly Deferred Step 2C/2D Items

Deferred to Step 2C:

- Replace raw Warehouse Assignment ID with a warehouse selector.
- Clarify whether Manual Fulfillment should be included when storefront requests a warehouse-specific shipping method list.
- Improve origin/warehouse readiness UX.
- Normalize carrier account readiness display across admin screens.

Deferred to Step 2D or domain redesign:

- Merge or retire the dual `CarrierConfiguration` and `CarrierAccount` readiness authorities.
- Support multiple carrier accounts for one carrier in the same tenant.
- Support carrier-account selection per warehouse, per method, per order, or per origin routing policy.
- Add data backfill/migration after live-data inspection.
- Reconcile historical DB snapshot drift around fulfillment fields if it still affects environments.

## 18. Migration Requirement Assessment

Schema migration required for Step 2B carrier selector: No.

Reason:

- `ShippingMethods.CarrierCode` already exists.
- `ShippingMethod` entity already contains `CarrierCode`.
- Admin GET/POST/PUT DTOs already contain `CarrierCode`.
- Backend validation already supports canonical carrier code validation.

Data migration or normalization may be required:

- If existing rows have external display names but null/blank/manual carrier codes.
- If existing rows use arbitrary names that cannot be mapped to canonical carrier codes.
- If historical environments lack the `CarrierCode` column despite current migrations/snapshot.

Current classification: NEEDS NORMALIZATION, with live-data state UNKNOWN.

## 19. Verification Plan

Step 2B verification after implementation should include:

1. Unit/API test: POST ShippingMethod with `carrierCode = manual` stores manual and does not require carrier account.
2. Unit/API test: POST ShippingMethod with `carrierCode = bosta` stores `bosta` and display name derives from registry.
3. Unit/API test: POST with missing carrier code and `CarrierName = "Bosta"` cannot create misleading external-looking manual method, or is normalized to a safe manual display.
4. UI test: Shipping Methods form renders canonical carrier selector using backend carrier metadata.
5. UI test: Saving Bosta sends `carrierCode = bosta`.
6. UI test: Saving Manual sends `carrierCode = manual` and hides credentials/account/readiness fields.
7. Storefront test: Manual method appears under intended eligibility conditions.
8. Storefront test: External carrier method eligibility matches chosen readiness authority.
9. Tenant test: admin cannot select or infer another tenant's carrier account or warehouse.
10. Regression test: existing tenant-scoped shipping-method read authority from Step 2A remains intact.

No browser automation or live infrastructure verification was performed in this audit.

## 20. Final Certification

Certification: PASS WITH EXTERNAL VERIFICATION BLOCKED

The architecture can be reconstructed confidently from repository evidence. The canonical carrier identity is `CarrierCode`; Manual Fulfillment is canonical as `manual`; Bosta is canonical as `bosta`; ShippingMethod does not directly reference CarrierAccount or Warehouse; and the current admin UI is mismatched because it edits free-text `CarrierName` while failing to submit `CarrierCode`.

External verification remains blocked because this step intentionally did not inspect live infrastructure, production data, browser behavior, or deployed services.

## Files Inspected

- `PointOfSale.Core/Constants/CarrierCodes.cs`
- `PointOfSale.Core/Entities/ShippingMethod.cs`
- `PointOfSale.Core/Entities/CarrierConfiguration.cs`
- `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`
- `PointOfSale.Core/Enums/FulfillmentMode.cs`
- `PointOfSale.Core/Entities/Order.cs`
- `PointOfSale.Core/Interfaces/ShippingAdministration/CarrierAccountRuntimeContracts.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs`
- `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/BostaAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/AramexAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/MylerzAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Controllers/ShippingMethodsController.cs`
- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
- `PointOfSale.API/Controllers/AdminCarrierConfigController.cs`
- `PointOfSale.API/Controllers/AdminShippingAdministrationController.cs`
- `PointOfSale.API/Controllers/ShippingWebhooksController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.API/Services/Admin/AdminShipmentService.cs`
- `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs`
- `PointOfSale.API/DTOs/ShippingAdministration/ShippingAdministrationDtos.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/ShippingMethodDocument.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs`
- `PointOfSale.Infrastructure/Messaging/Consumers/ShippingMethodProjectionConsumer.cs`
- `PointOfSale.Web/src/modules/shipping-administration/components/ShippingAdministrationWorkspace.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/api/shippingAdministrationApi.js`
- `PointOfSale.Web/src/modules/shipping/components/ShippingOptionFormModal.jsx`
- `PointOfSale.Web/src/modules/shipping/components/CarrierConfigurationModal.jsx`
- `PointOfSale.Web/src/modules/shipping/hooks/useShippingSetupApi.js`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `DB/sql/add_carrier_code_to_shipping_methods.sql`
- `DB/sql/fix_shipping_carrier_integration.sql`
- `DB/sql/add_fulfillment_ownership_snapshot.sql`
- `DB/snapshots/schema-23-7-2026.sql`
- `PointOfSale.Tests/Architecture/CarrierIntegrationAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationRuntimeTests.cs`
- `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationApiTests.cs`
- `PointOfSale.Tests/Storefront/ShippingMethodServiceCurrencyConsistencyTests.cs`
- `PointOfSale.Tests/Architecture/AdminShippingMethodReadAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontShippingMethodAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ShippingMethodEligibilityAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ShippingCalculationAuthorityCertificationTests.cs`
