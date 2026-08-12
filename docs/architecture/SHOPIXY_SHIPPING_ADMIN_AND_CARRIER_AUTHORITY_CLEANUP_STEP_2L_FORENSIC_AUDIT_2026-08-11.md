# SHOPIXY Shipping Admin and Carrier Authority Cleanup - Step 2L Forensic Audit

Date: 2026-08-11
Scope: audit only. No source, test, configuration, schema, migration, browser automation, carrier network calls, commits, pushes, resets, stashes, or cleanup actions were performed.

## 1. Executive Summary

Certification: **BLOCKED**

The Step 2A-2K authority chain is largely present in the final checkout and shipment paths:

ShippingMethod -> FulfillmentWarehouseId -> WarehouseResolution -> inventory reservation -> Order.WarehouseId / OrderItem.WarehouseId -> shipment origin.

However, the architecture is not yet production-certifiable because duplicate merchant-facing authority remains:

- `CarrierConfiguration` is the actual runtime credential/readiness source used by checkout admission, shipment creation, quote generation, and webhooks through `CarrierIntegrationAuthority`.
- `CarrierAccount` and `CarrierWarehouseAssignment` still expose a separate lifecycle, credential-reference, connection-test, policy, service-level, and warehouse-assignment model in Admin UI.
- `CarrierAccount` lifecycle health can report Draft/Blocked/NotReady/Missing Warehouse while `CarrierIntegrationAuthority` reports Manual Fulfillment as Ready/NotRequired/ProductionReady.
- Storefront has both canonical preview/select endpoints and a legacy GET path that can admit active methods without enforcing `ShippingMethod.FulfillmentWarehouseId` in the same way.
- Legacy quote generation is still carrier-configuration-driven and does not model customer-facing Shipping Methods or their fulfillment warehouse authority.

The final checkout path blocks most unsafe outcomes, but unresolved duplicate authority still changes runtime presentation/admission behavior and admin decisions. Under the certification rules, that requires **BLOCKED**.

## 2. Current Architecture

Current runtime layers found:

- Shipping methods are POS/admin-managed customer-facing options in `PointOfSale.Core/Entities/ShippingMethod.cs`.
- `ShippingMethod.FulfillmentWarehouseId` is persisted and mapped in `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`.
- Storefront preview and select are handled by `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`.
- `WarehouseResolution` is created and validated by `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`.
- Checkout requires `CheckoutSessionId`, `WarehouseResolutionId`, and `WarehouseResolutionVersion` in `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`.
- Checkout stamps `Order.WarehouseId` and each `OrderItem.WarehouseId` from the validated warehouse resolution in `PointOfSale.API/Services/Checkout/CheckoutService.cs`.
- Shipment creation restores/derives origin from `Order.WarehouseId` and the tenant warehouse, not carrier warehouse assignment, in `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`.
- Carrier runtime credentials and readiness are resolved through `PointOfSale.Infrastructure/Shipping/CarrierIntegrationAuthority.cs`.
- Carrier runtime capabilities are described by `PointOfSale.Infrastructure/Shipping/CarrierCapabilityRegistry.cs`.
- Legacy per-tenant carrier integration rows remain in `PointOfSale.Core/Entities/CarrierConfiguration.cs`.
- Carrier account lifecycle rows remain in `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`.
- Admin UI still exposes the Carrier Account model through `PointOfSale.Web/src/modules/shipping-administration`.

## 3. Canonical Authority Matrix

| Question | Current authority | Evidence | Audit classification |
|---|---|---|---|
| External carrier credentials | `CarrierConfiguration` via `CarrierIntegrationAuthority.BuildCredentials` | `CarrierIntegrationAuthority.ResolveCredentialsAsync`, `ValidateAndStoreIntegrationAsync`, `ValidateResolvedCredentialsAsync` | CANONICAL runtime authority, with legacy admin API still exposed |
| Carrier readiness for checkout/shipment | `CarrierIntegrationAuthority` plus `CarrierCapabilityRegistry` plus enabled `CarrierConfiguration` | `GetIntegrationReadinessAsync`, `BuildReadinessAsync`, shipment credential resolution | CANONICAL runtime authority |
| Carrier account lifecycle readiness | `CarrierAccountAdministrationRuntime` | activation gates require credential reference, environment, connection test, warehouse assignment, mapping, customs, service level | ADMIN lifecycle authority only; conflicts with runtime readiness if shown as operational authority |
| Fulfillment warehouse | `ShippingMethod.FulfillmentWarehouseId` | Shipping method CRUD, projections, preview/select, warehouse resolution, checkout stamping | CANONICAL business authority |
| Storefront shipping admission | Canonical: POST preview/select. Legacy: GET `/api/storefront/shipping-methods` | `StorefrontShippingMethodsController`, `ShippingMethodService` | Mixed: canonical path is sound, legacy GET is runtime-unsafe presentation path |
| Shipment creation | `Order.ShippingMethodId`, `Order.WarehouseId`, `CarrierIntegrationAuthority`, adapter | `ShipmentService`, `CarrierShipmentService` | CANONICAL |
| Inventory reservation | `WarehouseResolution.WarehouseId` carried into checkout reservation call | `CheckoutService.ValidateAndReserveInventoryAsync` call | CANONICAL |
| Carrier origin readiness | Dormant `CarrierWarehouseAssignment` check if registry enables `RequiresOriginReadiness` | `CarrierIntegrationAuthority.IsOriginReadinessSatisfiedAsync`; registry currently false for supported carriers | COMPATIBILITY / dormant support relation |
| Webhook trust | `CarrierConfiguration` webhook fields through `CarrierIntegrationAuthority.ResolveWebhookTrustAsync` | CarrierIntegrationAuthority webhook methods | PROVIDER-INTERNAL |

## 4. CarrierWarehouseAssignment Classification

Classification: **LEGACY COMPATIBILITY STRUCTURE**

Not classified as dead and not safe to delete blindly.

Evidence:

- Entity exists in `CarrierAccountRuntimeEntities.cs` with `CarrierAccountId`, `WarehouseId`, `IsEnabled`, `OriginReadinessPublished`, and `ReadinessEvidenceReference`.
- EF mapping has tenant-scoped composite FKs to `CarrierAccount` and `Warehouse`.
- `CarrierAccountAdministrationRuntime.AssignWarehouseAsync` creates/enables assignments and rejects manual fulfillment.
- `CarrierAccountAdministrationRuntime.EvaluateActivationGatesAsync` blocks activation when no assignment exists or origin readiness is not published.
- `CarrierIntegrationAuthority.IsOriginReadinessSatisfiedAsync` joins assignment to active/ready carrier account, but that check is only reached when a carrier capability has `RequiresOriginReadiness == true` and a warehouse context is provided.
- `CarrierCapabilityRegistry` currently sets `RequiresOriginReadiness: false` for Manual, Bosta, Aramex, DHL, FedEx, and Mylerz.
- Checkout, warehouse resolution, inventory reservation, order stamping, and shipment origin do not use `CarrierWarehouseAssignment`.

Runtime effect:

- It affects Admin carrier-account lifecycle state and health.
- It does not currently select fulfillment warehouse for customer checkout.
- It does not currently provide shipment origin.
- It can become runtime-relevant if a future carrier capability flips `RequiresOriginReadiness` to true.

Removal impact:

- Removing it from merchant-facing UI can be done without schema changes if the internal rows and APIs remain.
- Deleting the table/entity would require migrations and changes to activation gates, DTOs, Admin APIs, UI, and tests.
- Deleting it now is not evidence-safe because it still supports carrier-account activation health and dormant origin-readiness logic.

Recommended treatment:

- Hide/demote from merchant-facing shipping method setup.
- Keep internally until the CarrierAccount lifecycle is retired or explicitly re-scoped as non-warehouse operational evidence.
- Do not let it compete with `ShippingMethod.FulfillmentWarehouseId`.

## 5. ShippingMethod.FulfillmentWarehouseId Analysis

`ShippingMethod.FulfillmentWarehouseId` is the intended merchant-configured warehouse authority and is implemented across the main runtime path.

Evidence:

- Entity: `ShippingMethod` has `Guid? FulfillmentWarehouseId` and `Warehouse? FulfillmentWarehouse`.
- Create/update: `ShippingMethodsController` accepts and persists `FulfillmentWarehouseId`.
- Validation: `ValidateFulfillmentWarehouseAsync` verifies same-tenant, existing, not-deleted, active warehouse when a value is supplied.
- Admin UI: `ShippingMethodsTab.jsx` displays and edits "Fulfillment Warehouse" using active tenant warehouses.
- Projection: shipping method projection carries `FulfillmentWarehouseId` into Mongo documents.
- Storefront preview: `GetShippingMethodsAvailabilityForConfiguredWarehousesAsync` filters out methods without a configured, active, stock-ready warehouse.
- Storefront selection: `WarehouseResolutionService.ResolveForSelectedShippingMethodAsync` validates the selected method and derives the warehouse from the method.
- Checkout: `ValidateSubmittedResolutionAsync` revalidates the selected method and requires the resolution warehouse to match the method warehouse.
- Reservation and order stamping: `CheckoutService` passes the validated warehouse into inventory reservation and stamps `Order.WarehouseId` and `OrderItem.WarehouseId`.
- Shipment origin: `CarrierShipmentService.ResolveOrRestoreShipmentOriginAsync` uses `Order.WarehouseId` to load the warehouse origin snapshot.

Warehouse path classifications:

| Path | Classification | Notes |
|---|---|---|
| `ShippingMethod.FulfillmentWarehouseId` | AUTHORITATIVE | Merchant-configured source for shipping warehouse |
| Storefront preview configured-warehouse filter | DERIVED | Derived from methods and cart stock |
| WarehouseResolution row | DERIVED/AUTHORITATIVE FOR CHECKOUT | Derived from method, then becomes checkout proof |
| Checkout request `WarehouseResolutionId` and version | DERIVED PROOF | Browser sends opaque proof, not warehouse id |
| `Order.WarehouseId` / `OrderItem.WarehouseId` | DERIVED | Stamped from validated resolution |
| Shipment `OriginWarehouseId` / origin snapshot | DERIVED | From order warehouse |
| Browser `warehouseId` query on legacy GET shipping methods | UNSAFE / LEGACY | Can filter by a browser-supplied warehouse without checking each method's `FulfillmentWarehouseId` equals that warehouse |
| `X-Warehouse-Id` | DEAD/ABSENT for storefront checkout | No checkout authority found |
| Cart warehouse authority | DEAD/ABSENT for storefront checkout | Cart does not own final fulfillment warehouse in the audited flow |
| `CarrierWarehouseAssignment` | LEGACY | Not used for current fulfillment warehouse selection |
| CarrierConfiguration pickup city/country | LEGACY/FALLBACK | Used by legacy quote flow, not checkout warehouse authority |
| Generic multi-warehouse resolver without method selection | FALLBACK | Fails when multiple warehouses can fulfill; not the final customer method path |

Gap:

- `FulfillmentWarehouseId` remains nullable. Active methods with null warehouse can still be created, although canonical storefront preview/select/final checkout filters or rejects them. This is a certification gap because Admin can save an active method that cannot be selected at checkout.

## 6. Manual Fulfillment Forensic Findings

Manual Fulfillment is correctly modeled as a carrier adapter for execution mode, but the Admin carrier-account surface can still show contradictory legacy state for existing manual account rows.

Correct evidence:

- `CarrierCodes.Manual` is canonical code `manual`.
- `ManualCarrierDefinition` has no external API requirement. It returns success for shipment creation without a carrier call and does not support external tracking, cancellation, quote, or webhooks.
- `CarrierCapabilityRegistry` marks manual as `ProductionReady`, no credential fields, no environment, no connection test, no origin readiness, no geography mapping, no webhooks.
- `CarrierIntegrationAuthority.GetIntegrationReadinessAsync` returns Manual as enabled, can execute, `CredentialAvailability = NotRequired`, and `ProductionCapability = ProductionReady`.
- `CarrierAccountAdministrationRuntime.RegisterCarrierAccountAsync` rejects manual with `MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED`.
- `CarrierAccountAdministrationRuntime.AssignWarehouseAsync` rejects manual warehouse assignment.
- Shipping method CRUD allows manual methods without carrier-account setup.
- Storefront and checkout readiness treat manual as executable when the Shipping Method and warehouse are valid.
- Shipment execution skips external carrier calls when `shipment.IsCarrierManaged` is false.
- Webhook processing rejects manual carrier codes.

Contradiction source:

- `CarrierAccountCard.jsx` renders lifecycle fields from `CarrierAccount` (`Activation State`, `Operational Health`, `Credential Status`, `Connection Test Status`, `Assigned Warehouse`) next to `account.integrationReadiness`.
- `ShippingAdministrationApiService.MapAccountAsync` attaches provider authority readiness from `CarrierIntegrationAuthority` to each returned carrier account.
- If a legacy/manual `CarrierAccount` row exists, the card can show Draft/NotReady/Blocked/Missing/Warehouse Missing from the account lifecycle while simultaneously showing Ready/NotRequired/ProductionReady from provider authority.
- The card also displays a correct note that Manual Fulfillment is managed as a Shipping Method and does not require credentials, connection tests, warehouse assignments, or external readiness actions.

Canonical Manual UI status:

- Manual Fulfillment UI should be driven by `CarrierIntegrationAuthority` and `CarrierCapabilityRegistry` only:
  - Ready
  - Credential Availability: NotRequired
  - Production Capability: ProductionReady
  - No account required
  - No connection test
  - No warehouse assignment
  - Warehouse configured only on Shipping Method

## 7. External Carrier Capability Matrix

Repository evidence only. Unknown means the repository does not prove it.

| Carrier | Required runtime credentials | Optional credentials/config | Environment/endpoint authority | Connection test | Service levels | Production readiness | UI/runtime mismatch |
|---|---|---|---|---|---|---|---|
| Bosta | `ApiKey` | Webhook shared secret through config if webhooks enabled; `ApiUrl` stored but not consumed by adapter | Hard-coded adapter endpoints: sandbox `https://staging-api.bosta.co`, production `https://app.bosta.co/api/v2`; selected by `UseSandbox` | Remote `GET {baseUrl}/cities` | standard, express, same_day | Partial | UI/API may expose ApiSecret, AccountId, ApiUrl in legacy surfaces, but adapter consumes ApiKey and UseSandbox only |
| Aramex | `ApiKey` as username, `ApiSecret` as password, `AccountId`, `ExtraConfig.AccountPin`, `ExtraConfig.AccountEntity`, `ExtraConfig.AccountCountryCode` | Webhook shared secret if enabled; `ApiUrl` stored but not consumed | Hard-coded adapter endpoints: sandbox `https://ws.sbx.aramex.net/ShippingAPI.V2`, production `https://ws.aramex.net/ShippingAPI.V2`; selected by `UseSandbox` | Remote FetchCountries call | standard, express, international, economy | Partial | New capability metadata exposes required ExtraConfig fields; legacy `AdminCarrierConfigController` response does not cleanly expose carrier-specific ExtraConfig fields except raw configuration on write |
| DHL Express | `ApiKey`, `ApiSecret`, `AccountId` | HMAC webhook mode; customs data comes from shipment/checkout snapshots, not account policy | Hard-coded adapter endpoints: sandbox `https://express.api.dhl.com/mydhlapi/test`, production `https://express.api.dhl.com/mydhlapi`; selected by `UseSandbox` | Local-only credential presence validation | express, economy, same_day, domestic | Partial | UI must not imply a remote DHL connection test; `ApiUrl` is unused |
| FedEx | Adapter internals require client id/API key, client secret/API secret, account id if transport is enabled | FedEx options contain endpoint paths/base URIs | Options: sandbox `https://apis-sandbox.fedex.com/`, production `https://apis.fedex.com/` | Unsupported through normal adapter; `ValidateCredentialsAsync` returns not certified | Empty from adapter | NotCertified | Capability registry exposes no credential fields because carrier is not certified; if enabled later, registry and UI must be updated |
| Mylerz | No active runtime credential requirement because adapter fails closed before external contract use | Adapter contains prospective ApiKey/ApiSecret/AccountId usage but public contract is unavailable | Hard-coded prospective endpoints: sandbox `https://sandbox-api.mylerz.com/v2`, production `https://api.mylerz.com/v2` | Unsupported/fail-closed | Empty while contract unavailable | Unavailable | Capability registry correctly exposes no fields while unavailable; real runtime requirements need external verification before enabling |

## 8. Bosta Connectivity Findings

Live error under review:

`Cannot reach Bosta API: No such host is known. (staging-api.bosta.co:443)`

Endpoint construction path:

- Bosta adapter declares `SandboxUrl = "https://staging-api.bosta.co"`.
- Bosta adapter declares `ProductionUrl = "https://app.bosta.co/api/v2"`.
- Every Bosta adapter runtime call selects endpoint from `credentials.UseSandbox ? SandboxUrl : ProductionUrl`.
- `CarrierIntegrationAuthority.BuildCredentials` sets `UseSandbox` from `CarrierConfiguration.UseSandbox`.
- Bosta adapter ignores `CarrierCredentials.ApiUrl`.
- Admin connection-test path can call adapter `ValidateCredentialsAsync` directly with UI-supplied credentials and `UseSandbox`.
- Runtime provider path resolves credentials from `CarrierConfiguration` and calls the same adapter validation method for credential validation.

Failure classification:

- The observed message is a **NETWORK/ENDPOINT failure**, not a credential failure.
- DNS failed before an HTTP response was available, so the repository does not prove whether the API key was valid or invalid.
- Possible causes supported by evidence:
  - stale or incorrect hard-coded sandbox hostname,
  - DNS/network environment issue,
  - wrong environment selection to sandbox.
- Causes not supported by this error:
  - invalid API credential, because there was no HTTP 401/403 response,
  - runtime use of `ApiUrl`, because the adapter ignores it.

Checkout/storefront readiness:

- Storefront admission uses `CarrierIntegrationAuthority` readiness and does not call the Bosta endpoint.
- Connection test and shipment creation call the Bosta adapter endpoint.
- Therefore a carrier can pass local configuration readiness and still fail a live connection test or external shipment call if DNS/endpoint is wrong.

External verification required:

- Verify Bosta's current official sandbox and production URLs outside this audit.
- No external carrier calls were performed.

## 9. Admin UI Field Classification

| Field/concept | Classification | Should remain merchant-facing? | Evidence/notes |
|---|---|---|---|
| Carrier Account itself | LEGACY / BUSINESS DECISION | Only if re-scoped to external integration readiness | Runtime uses `CarrierConfiguration`, not carrier account, for credentials |
| Warehouse / Assign Warehouse | LEGACY | No for shipping setup | Competes with Shipping Method warehouse authority; only affects account activation health |
| Origin readiness published | LEGACY / DORMANT SUPPORT | No by default | Runtime origin readiness is dormant because registry requires origin readiness for no current carrier |
| Readiness evidence reference | LEGACY / PRESENTATION | No by default | Stored on assignment/policy; not checkout/shipment authority |
| Connection test | CONDITIONALLY REQUIRED | Yes, carrier-specific external integrations only | Bosta/Aramex remote; DHL local-only; manual/Mylerz/FedEx unsupported |
| API key/token | CARRIER-SPECIFIC | Yes where capability says required | Required for Bosta, Aramex, DHL; not Manual/Mylerz current; FedEx only when certified |
| API secret | CARRIER-SPECIFIC | Yes where capability says required | Required for Aramex/DHL; not Bosta/Manual/Mylerz current |
| Account number / Account ID | CARRIER-SPECIFIC | Yes where capability says required | Required for Aramex/DHL; FedEx when certified |
| Account PIN | CARRIER-SPECIFIC | Yes for Aramex only | `ExtraConfig.AccountPin` |
| Account entity | CARRIER-SPECIFIC | Yes for Aramex only | `ExtraConfig.AccountEntity` |
| Account country | CARRIER-SPECIFIC | Yes for Aramex only | `ExtraConfig.AccountCountryCode` |
| API URL | UNUSED / MISLEADING | No unless adapters are changed to consume it | Stored in `CarrierConfiguration`, ignored by Bosta/Aramex/DHL/Mylerz adapters |
| Policy scope/mode | LEGACY / BUSINESS DECISION | Not required for current runtime | Used by CarrierAccount activation health, not shipment execution |
| Customs readiness | LEGACY / MISLEADING | No as carrier-account gate | DHL customs readiness is derived from shipment/customs snapshot, not this account policy |
| Activation State | ADMIN LIFECYCLE | No as runtime readiness | Can conflict with provider readiness |
| Operational Health | ADMIN LIFECYCLE | No as runtime readiness | Can conflict with provider readiness |
| Credential Status | ADMIN LIFECYCLE | No unless account model becomes the credential source | Runtime credentials are `CarrierConfiguration` |
| Authority Readiness | CANONICAL | Yes | Comes from `CarrierIntegrationAuthority` |
| Credential Availability | CANONICAL | Yes | Comes from `CarrierIntegrationAuthority` |
| Production Capability | CANONICAL | Yes | Comes from `CarrierCapabilityRegistry` |

## 10. Shipping Method UI Findings

The Shipping Method UI is mostly aligned with the intended business model.

Evidence:

- `ShippingMethodsTab.jsx` owns customer-facing English name, Arabic name, description, pricing, delivery days, tracking URL template, active state, carrier, service level, and fulfillment warehouse.
- Carrier selection uses backend-supported `carrierCode` values.
- The UI normalizes carrier code before save.
- Service level is selected from backend capability metadata and normalized before save.
- Unsupported legacy carrier codes are shown as unsupported and cannot be resaved unchanged.
- Fulfillment warehouse select uses active tenant warehouses from the shipping administration API.
- If there is exactly one active warehouse, the add form defaults the method warehouse to that warehouse.
- Manual Fulfillment can be selected as a carrier without requiring a carrier account.

Gaps:

- The UI still allows "Not configured" and the API accepts null `FulfillmentWarehouseId`. Canonical storefront checkout rejects it, but active methods can be saved in a non-admissible state.
- The single-warehouse case is improved by defaulting, but the warehouse control remains visible. That may be acceptable for transparency, but if the business requirement is "no unnecessary decision machinery visible," the UI should hide or lock the field when there is exactly one active warehouse.

## 11. Legacy/Duplicate Authority Scan

| Object/path | Classification | Notes |
|---|---|---|
| `CarrierConfiguration` entity | CANONICAL runtime credential authority | Used by `CarrierIntegrationAuthority` |
| `AdminCarrierConfigController` | ADMIN-FACADE / LEGACY | Direct carrier config API still exposes generic fields and can duplicate newer shipping administration surface |
| `CarrierIntegrationAuthority` | PROVIDER-INTERNAL / CANONICAL | Correct boundary for credentials/readiness/webhook trust |
| `CarrierAccount` entity | LEGACY / ADMIN lifecycle | Not runtime credential source |
| `CarrierAccountAdministrationRuntime` | ADMIN-FACADE / LEGACY | Separate activation gates can contradict runtime readiness |
| `CarrierWarehouseAssignment` entity | LEGACY COMPATIBILITY STRUCTURE | Not current warehouse authority; still used by account health and dormant origin readiness |
| `CarrierServiceLevelEnablement` | LEGACY / DUPLICATE | Shipping Method service level is the customer-facing service authority |
| `CarrierAccountPolicy` | LEGACY / DUPLICATE | Not used by shipment customs execution path |
| `ShippingMethod` | CANONICAL | Customer-facing shipping option |
| `FulfillmentWarehouseId` | CANONICAL | Merchant-configured warehouse authority |
| `WarehouseResolution` | CANONICAL checkout proof | Required at final checkout |
| `ShippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync` | CANONICAL storefront preview | Enforces configured active stock-ready warehouse and external readiness |
| `ShippingMethodService.GetShippingMethodsAvailabilityAsync` with legacy GET | RUNTIME-UNSAFE / LEGACY | Can expose methods without the same fulfillment warehouse enforcement |
| `ShippingQuoteService` | LEGACY | Generates carrier quotes from carrier configurations, not customer-facing Shipping Methods |
| Shipment origin from `Order.WarehouseId` | CANONICAL | Correct final shipment origin authority |

No direct `X-Warehouse-Id` storefront checkout authority was found.

## 12. Database/Migration Findings

Model consistency:

- `ShippingMethod.FulfillmentWarehouseId` has tenant-scoped FK behavior in `ApplicationDbContext`.
- `WarehouseResolution` has tenant-scoped relationships to checkout session, cart, warehouse, and order.
- `CarrierWarehouseAssignment` has tenant-scoped FK behavior to carrier account and warehouse.
- Query filters exist for tenant-scoped runtime entities.

Migration chain:

- `20260811150644_SHOPIXY_WarehouseResolutionAuthority` creates `WarehouseResolutions` and adds tenant-safe assignment FKs with preflight checks for missing/cross-tenant assignments.
- `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority` adds nullable `FulfillmentWarehouseId` to `ShippingMethods` and adds tenant-scoped FK/index.
- The ordering is coherent: warehouse resolution authority exists before shipping method warehouse authority.

Data/backfill impact:

- No backfill is present for existing shipping methods.
- Legacy active shipping methods can therefore have null `FulfillmentWarehouseId`.
- Canonical storefront preview/select/final checkout rejects or filters those rows, but Admin can still save active methods with no warehouse.
- A migration is not required to hide carrier warehouse assignment from UI.
- A migration would be required only if making `ShippingMethod.FulfillmentWarehouseId` non-null, deleting carrier-account tables, or deleting carrier warehouse assignment.

Orphan/cross-tenant risk:

- New FK/index structure is tenant-safe.
- The warehouse-resolution migration contains preflight SQL to block known bad carrier assignment rows before FK creation.
- Existing nullable shipping method warehouse rows are semantically incomplete but not FK-orphaned.

## 13. Business Model Validation

Example:

- Warehouse: Cairo
- Carrier Account / integration: Bosta API key configured and enabled in `CarrierConfiguration`
- Shipping Method: Bosta Express, carrier `bosta`, service `express`, fulfillment warehouse Cairo, price 50 EGP, delivery 1-2 days
- Customer selects Bosta Express

Expected flow vs implementation:

| Expected step | Current behavior |
|---|---|
| Customer selects Shipping Method | Storefront select endpoint accepts `ShippingMethodId` |
| Shipping Method determines carrier/service/warehouse | Yes, method carries carrier code, service level, and `FulfillmentWarehouseId` |
| Bosta readiness checked | Yes, external readiness uses `CarrierIntegrationAuthority` with method and warehouse context |
| Cairo warehouse resolved | Yes, from `ShippingMethod.FulfillmentWarehouseId` |
| WarehouseResolution created | Yes, select endpoint creates/reuses resolution row |
| Checkout submits proof | Yes, checkout requires resolution id/version |
| Inventory reservation uses Cairo | Yes, checkout passes final resolution warehouse |
| Order warehouse stamped Cairo | Yes |
| Shipment execution uses Cairo origin | Yes, via `Order.WarehouseId` and tenant warehouse |

Single-warehouse case:

- The admin Shipping Method form defaults to the sole active warehouse.
- The field remains visible and can be set to not configured. This is not clean enough if the product requirement is to hide unnecessary warehouse decision machinery.

Multi-warehouse case:

- Different Shipping Methods can point to different warehouses:
  - Bosta Express -> Cairo
  - Bosta Standard -> Alexandria
  - Manual Standard -> Cairo
  - Aramex Express -> Giza
- Canonical preview/select/final checkout supports this.
- Legacy GET and quote paths can still present choices outside this model.

## 14. P0/P1/P2 Findings

### P0 - Must Fix Before Production

1. Remove duplicate merchant-facing readiness authority between `CarrierAccount` lifecycle and `CarrierIntegrationAuthority`.
2. Stop showing or using carrier-account warehouse assignment as merchant-facing shipping warehouse authority.
3. Ensure Manual Fulfillment cannot display carrier-account lifecycle blockers; drive Manual status from provider capability/readiness only.
4. Retire or harden legacy storefront GET shipping-method admission so it cannot expose methods inconsistent with `ShippingMethod.FulfillmentWarehouseId`.
5. Retire or isolate legacy `ShippingQuoteService` if it remains reachable in customer checkout; it is carrier-configuration-driven, not ShippingMethod-driven.

### P1 - Should Fix Before Shipping Certification

1. Decide whether active Shipping Methods must require non-null `FulfillmentWarehouseId` at save time.
2. Backfill or merchant-migrate legacy active methods that lack `FulfillmentWarehouseId`.
3. Align Admin carrier config fields to `CarrierCapabilityRegistry` and actual adapter consumption.
4. Remove or hide `ApiUrl` unless adapters are changed to consume it.
5. Fix DHL connection-test presentation to show local-only validation, not a remote connectivity test.
6. Verify Bosta sandbox/production endpoints externally.
7. Ensure Aramex ExtraConfig fields are cleanly exposed in any surviving configuration UI.

### P2 - Cleanup

1. Keep `CarrierWarehouseAssignment` internally only until CarrierAccount lifecycle is retired or re-scoped.
2. Remove misleading comments that claim no SQL access where SQL is now intentionally used.
3. Consolidate old carrier configuration APIs and new shipping administration APIs.
4. Add diagnostics showing why a Shipping Method is not storefront-admissible.

## 15. Minimal Safe Remediation Plan

1. Declare `CarrierIntegrationAuthority` and `CarrierCapabilityRegistry` as the only runtime readiness/status authority.
2. Treat `CarrierAccount` as optional external integration administration metadata, or retire it from merchant UX.
3. Remove Carrier Account warehouse assignment from merchant-facing screens; do not delete schema yet.
4. Hide manual from carrier-account lists, and suppress lifecycle cards for legacy manual account rows.
5. Make the Shipping Method screen the only place where fulfillment warehouse is configured.
6. Harden the legacy storefront GET endpoint or route storefront exclusively through preview/select.
7. Decide whether active Shipping Methods require `FulfillmentWarehouseId`; if yes, add API validation and backfill path.
8. Align carrier credential UI fields to capability metadata and adapter evidence.
9. Externally verify Bosta endpoint metadata before changing endpoints.

This avoids a rewrite and preserves WarehouseResolution, tenant isolation, idempotency, provider architecture, and current carrier adapters.

## 16. Exact Files/Modules Likely To Change

Likely P0/P1 change surface:

- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js`
- `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs`
- `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.Infrastructure/Shipping/ShippingQuoteService.cs`
- `PointOfSale.API/Controllers/AdminCarrierConfigController.cs`
- `PointOfSale.API/Controllers/ShippingMethodsController.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierCapabilityRegistry.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/BostaAdapter.cs` only after external endpoint verification
- UI tests under `PointOfSale.Web/src/modules/shipping-administration`
- API/infrastructure tests covering checkout, warehouse resolution, carrier readiness, and shipment creation

## 17. Migration Impact

No migration is required for the minimal merchant-facing cleanup if:

- Carrier warehouse assignment is hidden/demoted but retained internally.
- Carrier account lifecycle tables remain for compatibility.
- `ShippingMethod.FulfillmentWarehouseId` remains nullable.

Migration may be required if:

- `ShippingMethod.FulfillmentWarehouseId` is made non-null.
- Existing active shipping methods are backfilled by data migration.
- Carrier account or carrier warehouse assignment tables are removed.
- Carrier configuration schema is changed to remove generic unused fields.

Recommendation:

- Avoid deletion migrations in Step 2L implementation.
- Prefer UI/API cleanup and validation first.
- Add a backfill/data-quality audit before any non-null FK migration.

## 18. Test Coverage Gaps

Required test additions before certification:

- Manual Fulfillment does not require carrier account, credentials, connection test, warehouse assignment, or readiness evidence.
- Legacy manual carrier-account rows do not produce contradictory merchant status.
- Shipping Method with null `FulfillmentWarehouseId` is either rejected when active or filtered with explicit diagnostics.
- Legacy storefront GET cannot admit a method for a browser-supplied warehouse that differs from the method's configured warehouse.
- Preview/select/final checkout flow stamps the selected method warehouse into order, order items, inventory reservation, and shipment origin.
- CarrierAccount lifecycle state does not control runtime storefront admission or shipment execution unless explicitly retained as an internal gate.
- Bosta DNS/endpoint failure is reported separately from invalid credentials.
- DHL connection test is presented and asserted as local-only.
- Aramex required ExtraConfig fields are enforced from capability metadata.
- FedEx/Mylerz remain unavailable/not-certified and cannot be activated as production carriers.

## 19. External Verification Requirements

External verification is required for:

- Current official Bosta sandbox hostname.
- Current official Bosta production hostname/path.
- Whether Bosta staging endpoint `staging-api.bosta.co` is still valid and resolvable in target hosting networks.
- Current Mylerz official API contract and credential requirements.
- FedEx certification requirements before exposing credentials or production activation.
- Aramex and DHL account-specific production onboarding requirements beyond repository-proven fields.

No external carrier calls were made during this audit.

## 20. Final Certification

Certification: **BLOCKED**

Reason:

The final checkout authority chain is substantially correct, but unresolved duplicate authority remains in merchant-facing and runtime-adjacent paths:

- `CarrierAccount` lifecycle readiness competes visually and administratively with `CarrierIntegrationAuthority` readiness.
- `CarrierWarehouseAssignment` remains merchant-facing despite not being fulfillment warehouse authority.
- Manual Fulfillment can show contradictory Carrier Account lifecycle blockers if legacy manual account rows exist.
- Legacy storefront GET and quote paths can present/admit shipping options outside the canonical ShippingMethod plus WarehouseResolution model.

Step 2L implementation is ready to proceed only as a minimal cleanup implementation, not as production certification.

Worktree safety:

- Baseline `git status --short` and `git diff --stat` were captured before audit.
- The repository was already dirty with pre-existing Step 2 changes and untracked reports/tests/migrations.
- This audit created only this report file.
- No source, schema, test, config, migration, git, browser, or external carrier mutation was performed by this step.
