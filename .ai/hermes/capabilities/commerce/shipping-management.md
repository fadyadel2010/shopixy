# Shipping Management — Implementation-Backed Architecture Specification

> Authority: this document describes ONLY verified implementation (C# backend + React/TS frontend)
> in `C:\Users\fadya\source\repos\PointOfSale`. Where prose docs disagree with code, code wins and the
> discrepancy is recorded under "Documentation Validation". No behavior is inferred.

## 1. Business Purpose (verified)

Shipping Management exists to answer three runtime questions and nothing more:

1. **What shipping options can a storefront customer choose, and what does each cost?**
   (`ShippingMethod` config → storefront read model → `ShippingCalculationService`).
2. **How is the chosen option snapshotted onto the Order at checkout?**
   (`CheckoutService.ValidateShippingMethodAsync` + `Order.ShippingMethodId/ShippingMethodName/ShippingCost/FulfillmentMode/FulfillmentCarrierCode`).
3. **How is the physical `Shipment` created, tracked, and reconciled with carriers?**
   (`Shipment` entity, `IShippingCarrierAdapter` adapters, `CarrierShipmentService`, carrier webhooks).

Business responsibilities that ARE Shipping Management:
- Tenant-owned shipping-method catalog (rate model, free-shipping threshold, delivery estimate, carrier binding).
- Carrier adapter abstraction + carrier account/credential lifecycle (two competing config surfaces — see §3).
- Shipment creation, status machine, tracking, COD collection signalling, label retrieval.
- Carrier webhook ingestion (idempotent, signature-verified) that drives fulfillment sync.

Responsibilities that are NOT Shipping Management (owned elsewhere, Shipping only reads/feeds):
- **Order lifecycle & fulfillment status** — owned by `IOrderFulfillmentService`. Shipping calls
  `SynchronizeFromShipmentAsync`; it never writes `Order.FulfillmentStatus` directly.
- **Payment / COD money recognition** — owned by `IPaymentLifecycleService`. Shipping only emits
  `ShipmentCodCollectedEvent` / COD signals; it posts no ledger.
- **Pricing of the cart (subtotal/tax/discount)** — owned by the pricing/promotion engine. Shipping
  contributes only the shipping leg via `ShippingCalculationService`.
- **Inventory / reservation** — owned by inventory services.

Relationship with **Checkout**: Checkout is the single writer that validates a `ShippingMethodId` against
SQL authority, computes shipping cost from authoritative pricing, and snapshots it immutably onto the Order.
Checkout re-locks the `ShippingMethod` SQL row in the final order-creation transaction (`lockAuthorityRow: true`).

Relationship with **Orders**: The Order stores an immutable shipping snapshot (method id/name/cost) plus the
`FulfillmentMode` (Manual|Carrier) and `FulfillmentCarrierCode`, decided once at checkout from the method's
`CarrierCode` and never re-inferred.

Relationship with **Storefront**: Storefront is READ-ONLY for shipping. It lists active methods
(`/api/storefront/shipping-methods`) and optionally requests carrier quotes (`/api/storefront/shipping/quotes`).
It never manages configuration and never computes authoritative cost.

## 2. Authority Model (verified)

**Configuration ownership = POS Admin (tenant), NOT Super Admin.** Every write surface is tenant-scoped
through `ITenantContext` and gated by `[Authorize(Roles = "Owner,Admin,Manager")]`. There is no Super-Admin
(CommerceHub platform) controller for shipping configuration in the codebase.

| Concern | Writer (authority) | Reader | Auth |
|---|---|---|---|
| Shipping **methods** (rate model) | `ShippingMethodsController` → `/api/shippingmethods` (SQL write) | Storefront Mongo projection | Owner/Admin/Manager |
| Carrier **config (legacy)** | `AdminCarrierConfigController` → `/api/admin/carriers` (`CarrierConfiguration`) | checkout validation, quote service | Owner/Admin/Manager |
| Carrier **account lifecycle (new)** | `AdminShippingAdministrationController` → `/api/admin/shipping/carrier-accounts` (`CarrierAccount` + satellites) | admin workspace | Owner/Admin/Manager |
| **Shipment** operations | `AdminShipmentsController`/`ShipmentController` → `AdminShipmentService`/`ShipmentService` | storefront tracking read | Admin (write) / storefront (read) |
| Carrier **webhooks** | `ShippingWebhooksController` (unauthenticated, signature-verified) | — | `[AllowAnonymous]`+`[AllowAnonymousTenant]` |

## 3. Shipping Providers / Carriers (verified)

Canonical codes in `PointOfSale.Core/Constants/CarrierCodes.cs`:
`manual`, `bosta`, `aramex`, `mylerz`, `dhl`, `fedex`. `manual` is a first-class carrier (not a fallback);
`CarrierCodes.IsManual()` treats null/empty/"manual" as manual.

Adapters (`PointOfSale.Infrastructure/Shipping/Adapters/`, all implement `IShippingCarrierAdapter`):
`AramexAdapter`, `BostaAdapter`, `DhlAdapter`, `FedExAdapter`, `MylerzAdapter`, plus `ManualCarrierDefinition`.
Registered/resolved via `ShippingCarrierRegistry` (`IShippingCarrierRegistry.IsCarrierSupported`,
`GetSupportedCarrierCodes`). **FedEx is explicitly declared "runtime foundation only; operational behavior
is not certified"** in `CarrierCodes.cs` — see Technical Debt.

`IShippingCarrierAdapter` capabilities (per-adapter booleans and operations): create shipment, cancel,
track, quote, labels, webhook parse/verify. Per-tenant capability flags are also stored on
`CarrierConfiguration` (`SupportsCod/Tracking/Webhooks/Labels/Quotes/Cancellation`).

### CRITICAL: Two parallel carrier-configuration models (Runtime Authority Divergence)

There are **two coexisting carrier configuration systems**, and the runtime read/write paths diverge:

- **Legacy: `CarrierConfiguration`** (`/api/admin/carriers`). Single row per carrier per tenant. Holds
  `ApiKey`/`ApiSecret`/`AccountId` ("encrypted in database" per XML doc), `UseSandbox`, capability flags,
  pricing overrides (`FixedShippingCost`, `MarkupPercentage`, `FreeShippingThreshold`), pickup config.
  **This is the model the runtime checkout and quote paths actually read**:
  `CheckoutService.ValidateShippingMethodAsync` gates non-manual methods on
  `CarrierConfiguration.IsEnabled`; `ShippingQuoteService` reads carrier configs for quoting.
- **New: `CarrierAccount` aggregate** (`/api/admin/shipping/carrier-accounts`) in
  `Core/Entities/ShippingAdministration/`. Rich lifecycle: `CarrierAccountState`
  (Draft→CredentialsPending→ConnectionTestPending→ReadyForActivation→Active→Suspended→Disabled→Retired),
  `CarrierOperationalState`, `CarrierAccountHealthState` (Healthy/Warning/Blocked/Disabled/Retired),
  `CarrierCredentialReference` (secret-reference indirection, not raw secrets), `CarrierWarehouseAssignment`,
  `CarrierAccountPolicy` (Domestic/International), `CarrierServiceLevelEnablement`,
  `CarrierConnectionTestRecord`, `CarrierAdministrationAudit`. Managed by
  `ShippingAdministrationApiService` with full actor/correlation audit.
  **No verified runtime checkout/quote path consumes `CarrierAccount` state** — it is an admin-facing
  governance/health layer that the checkout gate does not yet read (checkout still keys off
  `CarrierConfiguration.IsEnabled`). This is a genuine divergence, documented in §14.

Credential/secret handling: `CarrierConfiguration.ApiKey/ApiSecret` are documented as encrypted-at-rest;
`CarrierCredentialReference.SecretReference` is an indirection handle (masked fingerprint, expiry) — the
newer model deliberately avoids storing raw secrets in the row. [Secrets never logged; treated as [REDACTED].]

## 4. Shipping Zones (verified)

There is **no rich zone/region/postal-rule engine**. Geographic coverage is expressed by:
- `ShippingMethod.AvailableCountries` (string; projected to `ShippingMethodDocument.AvailableCountries`).
- Destination validation in `StorefrontShippingQuotesController.GetQuotes`: country normalized via
  `CountryCodeHelper.TryNormalizeCountryCode` (must be ISO 3166-1 alpha-2), falling back to the tenant's
  default country (`Tenant.Country`) when omitted.
- Carrier-side geography is resolved at shipment registration through an admitted geographic catalog:
  `Shipment` carries `Destination*PlaceId`/`Origin*PlaceId`, `CarrierGeographicMappingReleaseId`, and an
  immutable origin (`OriginWarehouseId`, `OriginAddressAdmissionEvidenceId`; comment SHIP-ORIGIN-1:
  "Carrier configuration is never origin authority"). GEO-9 rule: provider geographic identifiers are
  shipment-registration evidence only and must never be copied to Order/CustomerAddress.

Fallback: if no destination country supplied, tenant country is used; postal code is optional throughout.

## 5. Shipping Methods (verified)

`ShippingMethod` (`Core/Entities/ShippingMethod.cs`) is the tenant method catalog. It does **not** model
distinct "Standard/Express/SameDay/Pickup" as an enum — method *type* is expressed via free-text
`ServiceLevel` (normalized strings `standard`/`express`/`same_day`/`next_day`) plus the rate fields below.
`ShipmentService`/`Shipment.ServiceLevel` default to `"standard"`.

Fields that define a method: `Name`/`NameAr`, `Description`, `BaseRate`, `PerKgRate`,
`FreeShippingThreshold`, `EstimatedDeliveryDaysMin/Max`, `IsActive`, `SortOrder`, `CarrierName`,
`CarrierCode` (binds method → carrier adapter / fulfillment mode), `TrackingUrlTemplate`, `AvailableCountries`.

## 6. Shipping Pricing Engine (verified)

`ShippingCalculationService` (`API/Services/Pricing/`) is the authoritative shipping-cost calculator:
- If `OrderSubtotal >= FreeShippingThreshold` (threshold set) → cost `0` (free shipping).
- Otherwise cost = `max(0, BaseRate + PerKgRate * max(0, weightKg))`, **rounded to 2 decimals**.
- No volume/dimension/distance factor is implemented — weight and subtotal are the only inputs.

Carrier quote pricing is a **separate** path: `ShippingQuoteService.GetQuotesAsync` returns normalized
`ShippingQuoteOption`s (per carrier × service level), applying `CarrierConfiguration.FixedShippingCost` or
carrier rates + `MarkupPercentage`, and carrier free-shipping threshold. Per the certification findings doc,
**carrier quote values are NOT an accepted order-cost source** — checkout never turns a client-supplied
carrier quote into order truth.

Authority guarantee (`docs/architecture/shipping-cost-authority-certification-findings.md`, PASS M8-E):
client-supplied `ShippingTotal`/`ShippingPrice`/method IDs are stale-detection candidates only; disagreement
returns `SHIPPING_CHANGED`. Backend SQL + calculation service own shipping cost.

## 7. Checkout Runtime (traced, end-to-end)

```
Customer (storefront) selects a shipping method
  → POST /api/storefront/shipping/quotes         (StorefrontShippingQuotesController, [AllowAnonymous] guest ok)
      → IShippingQuoteService.GetQuotesAsync       (normalized options, sorted cheapest-first, no-store headers)
  → checkout submits ShippingMethodId + address
  → CheckoutService.CheckoutAsync
      → STEP: shipping-address validation (name/phone/street/city/country required)
      → STEP: ShippingMethodValidation → ValidateShippingMethodAsync(ShippingMethodId, tenantId)
          → LoadShippingMethodAuthorityAsync (SQL ShippingMethods; final tx uses lockAuthorityRow:true)
          → reject if not found / not IsActive
          → if non-manual CarrierCode: require CarrierConfiguration.IsEnabled == true (else "Carrier not available")
          → MapAuthoritativeShippingMethodDocument
      → authoritative pricing recompute → pricing.Shipping (ShippingCalculationService)
      → snapshot onto Order:
          ShippingMethodId, ShippingMethodName (immutable snapshot), ShippingCost = pricing.Shipping,
          FulfillmentMode = DetermineFulfillmentMode(method)  (Carrier if CarrierCode non-empty & != "manual", else Manual)
          FulfillmentCarrierCode = ExtractCarrierCode(method) (null for manual)
      → Order persisted in PostgreSQL (system of record)
```

Read model layer: storefront method listing reads the **MongoDB** `ShippingMethodDocument` projection
(populated by the projection consumer, §11), cached in Redis (`pos:tenant:{tenantId:N}:shipping-methods`).
Checkout itself reads **PostgreSQL authority**, not the projection (CQRS: Mongo = storefront selector only).

## 8. Write vs Read Authority (verified)

- **Writes** shipping-method config: `ShippingMethodsController` → SQL `ShippingMethods` (POS Admin).
- **Reads** for storefront display: MongoDB `ShippingMethodDocument` (+ Redis cache).
- **Reads** for checkout authority: PostgreSQL `ShippingMethods` directly (never the projection).

They are **NOT identical** by design (CQRS). **Runtime Authority Divergence (config governance):** the
newer `CarrierAccount` lifecycle/health model is written and read by the admin workspace but is **not**
consulted by the checkout carrier-availability gate, which still reads legacy `CarrierConfiguration.IsEnabled`.
Two carrier-config write surfaces (`/api/admin/carriers` and `/api/admin/shipping/carrier-accounts`) can
disagree, and only the legacy one influences checkout. See §14.

## 9. Order Pipeline (traced)

```
Checkout → shipping selection validated (SQL) → Order created in PostgreSQL with immutable shipping snapshot
  → Order confirmation / payment (owned by payment lifecycle; COD supported)
  → Fulfillment: FulfillmentMode decides ownership:
       Manual  → admin transitions via IOrderFulfillmentService (AdminShipmentService / ShipmentService)
       Carrier → ONLY carrier webhooks drive fulfillment (Order.FulfillmentMode immutable, DB-trigger enforced)
  → Shipment created (Shipment entity, one per order) after order paid/confirmed
       - manual: ShipmentService.CreateShipmentAsync
       - carrier: CarrierShipmentService.CreateShipmentWithCarrierAsync (idempotent, post-commit registration worker)
  → Tracking: TrackingNumber/TrackingUrl/AwbNumber; carrier sync via webhook or SyncTrackingAsync polling
```

**Where shipping info is persisted:**
- Order snapshot: `Order.ShippingMethodId/ShippingMethodName/ShippingCost/FulfillmentMode/FulfillmentCarrierCode`
  + full `Order.Shipping*` address + admitted geographic place IDs (PostgreSQL, immutable).
- Physical shipment: `Shipment` table (PostgreSQL) — carrier code, service level, status, cost, COD,
  tracking, recipient, origin/destination geographic evidence, package details, timestamps, carrier metadata
  (`CarrierMetadata` JSON is the ONLY carrier-specific store; core domain never parses it), label data,
  external-registration reconciliation fields (`CarrierRequestId`, `ExternalRegistrationState`, attempt counts).

## 10. Storefront (verified)

- Selector/quotes: `POST /api/storefront/shipping/quotes` — guest (`[AllowAnonymous]`,`[AllowAnonymousTenant]`)
  and authenticated both work; requires tenant context; `no-store` cache headers; validates weight ≥ 0,
  packageCount > 0, COD amount ≥ 0, subtotal ≥ 0, ISO-2 country. Returns options sorted cheapest-first with
  estimated delivery, COD support, free-shipping flag, per-carrier service levels.
- Method listing: `StorefrontShippingMethodsController` → `ShippingMethodService` reads Mongo projection + Redis.
- Estimated delivery: from `EstimatedDeliveryDaysMin/Max` (and quote `EstimatedDelivery` string).
- Cost calc: never client-side; backend `ShippingCalculationService`/quote service only.
- Frontend never sees carrier API specifics (enforced by comment contract in `StorefrontShippingQuotesController`).
- Frontend modules: `PointOfSale.Web/src/modules/storefront/shipping/` (with `shipmentIdentityAuthority`),
  `modules/shipping/` (tracking, `shipmentActionAuthority`), `modules/shipping-administration/` (admin workspace).

## 11. Data Model & Projection Model (verified)

Entities (PostgreSQL, `ITenantOwned`): `ShippingMethod`, `CarrierConfiguration`, `Shipment`,
`ShippingWebhookLog`, and the ShippingAdministration aggregate (`CarrierAccount`, `CarrierCredentialReference`,
`CarrierWarehouseAssignment`, `CarrierAccountPolicy`, `CarrierServiceLevelEnablement`,
`CarrierConnectionTestRecord`, `CarrierAdministrationAudit`). Enums: `ShipmentStatus`
(PendingExternalRegistration=9, Pending=0, ReadyForPickup, PickedUp, InTransit, OutForDelivery, Delivered,
Failed, Cancelled, Returned), `FulfillmentMode` (Manual/Carrier), plus the CarrierAccount* state enums.

Projection/CQRS: **PostgreSQL = source of truth; MongoDB = storefront read model; Redis = cache/invalidation;
RabbitMQ/MassTransit = events.** `ShippingMethodProjectionConsumer` (queue `shipping-method-projection`,
retry 3×1s) consumes `ShippingMethodUpdatedEvent`/`ShippingMethodDeletedEvent`, **always re-fetches from
PostgreSQL** (never trusts payload, `IgnoreQueryFilters` + tenant match), maps to `ShippingMethodDocument`,
upserts via `UpsertIfFreshAsync` (monotonic version/event fence via `ProjectionEventFence`), tombstones on
soft-delete, then invalidates Redis key `pos:tenant:{tenantId:N}:shipping-methods` (non-fatal on failure).
Fails closed: missing TenantId/empty id → acknowledged-and-dropped (no retry storm); other errors rethrow to
trigger MassTransit retry. `Shipment` itself is not Mongo-projected for storefront display; storefront reads
shipment tracking through dedicated read DTOs from SQL.

## 12. Events (verified)

`Core/Events/ShippingMethodEvents.cs`: `ShippingMethodUpdatedEvent`, `ShippingMethodDeletedEvent`.

`Core/Events/ShipmentEvents.cs` (all extend `ShipmentEventBase : IntegrationEvent`): `ShipmentCreatedEvent`,
`ShipmentReadyForPickupEvent`, `ShipmentPickedUpEvent`, `ShipmentInTransitEvent`, `ShipmentOutForDeliveryEvent`,
`ShipmentDeliveredEvent`, `ShipmentFailedEvent`, `ShipmentCancelledEvent`, `ShipmentReturnedEvent`,
`ShipmentStatusChangedEvent`, `ShipmentExternalRegistrationRequestedEvent`, `ShipmentCodCollectedEvent`,
`ShipmentPackedEvent`, `ShipmentShippedEvent`. COD money signals: `OrderCodDeliveredEvent`,
`CodSettlementCompletedEvent` (`PaymentLifecycleEvents.cs`, owned by payment lifecycle).

Consumers: `ShipmentCreatedNotificationConsumer`, `ShipmentDeliveredNotificationConsumer`,
`OrderStatusSyncConsumer`, `ShippingMethodProjectionConsumer`. Publishers: `CarrierShipmentService`,
`AdminShipmentService`, `ShipmentService` (via `IEventPublisher`).

There is **no** `ShippingConfigured`/`ShippingProviderEnabled`/`ShippingSelected` event in the codebase —
those were speculative; shipping selection is captured as an Order snapshot, not an event.

## 13. Security (verified)

- **Tenant isolation:** every entity is `ITenantOwned`; controllers use `ITenantContext.RequiredTenantId`;
  projection consumer rejects events without TenantId and matches `x.TenantId == event.TenantId` under
  `IgnoreQueryFilters`. Quote controller requires non-empty tenant context.
- **Authorization:** admin surfaces `[Authorize(Roles="Owner,Admin,Manager")]`; storefront quotes/listing
  `[AllowAnonymous]` (guest checkout) but tenant-scoped; webhooks `[AllowAnonymous]`+`[AllowAnonymousTenant]`.
- **Webhook security:** per-carrier signature validation (`X-Webhook-Signature`, Bosta `Authorization`,
  `X-Aramex-Signature`, `X-Mylerz-Signature`, `X-DHL-Signature`), content-type filter,
  `RequestSizeLimit(WebhookRequestLimits.ShippingProviderMaxBytes)`, rate limiting
  (`WebhookRateLimiterPolicy`), unsupported-carrier → 404, empty payload → 400, `ShippingWebhookLog`
  persistence, telemetry (`RecordWebhookProcessing`), duplicate detection. Webhooks **only update Shipment,
  never Order** (enforced by contract; fulfillment sync goes through `IOrderFulfillmentService`).
- **Secrets/credentials:** legacy `CarrierConfiguration.ApiKey/ApiSecret` encrypted-at-rest; new model uses
  `CarrierCredentialReference.SecretReference` indirection (masked fingerprint, expiry). Connection tests
  recorded in `CarrierConnectionTestRecord`; all admin mutations audited in `CarrierAdministrationAudit`
  with actor + correlation id.

## 14. Failure Scenarios (verified)

- **Disabled provider:** checkout `ValidateShippingMethodAsync` rejects non-manual method whose
  `CarrierConfiguration.IsEnabled` is false → `CheckoutShippingMethodException("Carrier is not currently available")`.
- **Inactive method:** rejected with "Not currently available".
- **Method not found:** `CheckoutShippingMethodException(id,"Not found")`.
- **No shipping method supplied:** checkout guard "Checkout requires a shipping method."
- **Zone/country mismatch:** invalid ISO-2 destination → 400 in quote controller; missing country → tenant default.
- **Price calc failure / quote service failure:** `GetQuotes` returns 500 with error message; no-store headers.
- **Carrier unavailable / timeouts (registration):** carrier create is idempotent via `CarrierRequestId`;
  post-commit registration worker retries with `ExternalRegistrationState`/attempt counters; partial failures
  reconciled (`ReconcilePendingCarrierRequestAsync`, `ReconcileExistingExternalRegistrationAsync`) — never a
  second carrier shipment. Fulfillment sync failure → `LogCritical` + throw (fail closed), blocking illegal
  state advance (CHECKOUT-CON-13 double-execution rule).
- **Duplicate webhook:** idempotent — returns `{processed:false,duplicate:true}`.
- **Manual carrier webhook:** rejected (`manual_carrier_not_allowed`).
- **Checkout fallback:** none silently — invalid shipping fails the checkout step rather than defaulting.

## 15. Legacy / Dead Code / Debt / Divergence (verified findings)

- **DUPLICATE CONFIG RUNTIME PATHS (high signal):** `CarrierConfiguration` (legacy, `/api/admin/carriers`)
  vs `CarrierAccount` aggregate (new, `/api/admin/shipping/carrier-accounts`). Only `CarrierConfiguration`
  is read by the checkout availability gate and quote service. `CarrierAccount` health/lifecycle/state is
  admin governance that no verified runtime checkout path consumes. Genuine Runtime Authority Divergence.
- **DUPLICATE SHIPMENT SERVICES:** `ShipmentService` (storefront/general) and `AdminShipmentService` (admin)
  both create/update shipments and publish events with near-identical `EventTimestampSequence` helpers —
  parallel implementations of overlapping shipment lifecycle logic.
- **Shipment.ShippingMethod region marked "Legacy/Fallback":** `Shipment.ShippingMethodId` is explicitly a
  legacy/fallback link for non-carrier shipments; carrier-managed shipments may have none.
- **FedEx uncertified:** `CarrierCodes.FedEx` comment: "Runtime foundation only; operational behavior is not
  certified." Adapter exists but is not production-verified.
- **ServiceLevel is free-text, not an enum:** "standard/express/same_day/next_day" are magic strings
  (contradicts the strict no-magic-strings rule applied to carrier codes) across `Shipment`,
  `CarrierConfiguration.DefaultServiceLevel`, quotes.
- **No zone engine:** despite the mission's zone/postal expectations, coverage is only
  `AvailableCountries` + country normalization; no state/city/postal rule tables.
- No `ShippingSnapshotJson` column exists on `Order` (searched — absent); shipping snapshot is discrete columns.

## 16. Certification

**Verdict: PARTIALLY CERTIFIED (implementation-verified).**

Verified capabilities (backed by code):
- Tenant-owned shipping-method catalog with weight/subtotal/free-threshold pricing (`ShippingCalculationService`).
- CQRS projection (PostgreSQL→MongoDB→Redis) with monotonic freshness + fail-closed consumer.
- Checkout SQL-authority validation, immutable Order snapshot, FulfillmentMode determination + row locking.
- Carrier-agnostic Shipment model + adapter registry (bosta/aramex/mylerz/dhl + manual).
- Idempotent, signature-verified webhook ingestion; shipment→fulfillment sync via `IOrderFulfillmentService`.
- Full shipment status machine + event set; COD signalling; carrier registration reconciliation.
- New carrier-account lifecycle/health/audit governance model with credential-reference indirection.

Missing / not-implemented capabilities:
- Rich shipping zones (states/cities/postal codes/regions), volumetric/dimensional/distance pricing,
  price-band methods, explicit method-type enum, shipping-config domain events.
- Super-Admin (platform) shipping governance — configuration is tenant/POS-Admin only.

Technical debt / architectural observations:
- Two carrier-config models with divergent runtime authority (checkout reads legacy `CarrierConfiguration`
  only); recommend converging the availability gate onto `CarrierAccount` state or clearly demoting one model.
- Duplicated shipment-lifecycle services (`ShipmentService`/`AdminShipmentService`).
- Free-text `ServiceLevel` violates the codebase's own no-magic-strings discipline.
- FedEx adapter uncertified.

Runtime Authority verification:
- Shipping cost: backend-owned (PASS M8-E certification doc + `ShippingCalculationService`) — confirmed.
- Fulfillment: `IOrderFulfillmentService`-owned; shipping synchronizes, never writes directly — confirmed.
- Payment/COD: payment-lifecycle-owned; shipping only signals — confirmed.
- Config authority: DIVERGENT between `CarrierConfiguration` and `CarrierAccount` — flagged.

Implementation confidence: **High** for method catalog, pricing, projection, checkout snapshot, webhook,
and shipment lifecycle (read directly). **Medium** for carrier adapters' external behavior (FedEx
uncertified; external calls not exercised here) and for the intended vs actual role of the `CarrierAccount`
governance model in runtime.

---

## Documentation Validation

- Confirmed: PostgreSQL authoritative, Mongo/Redis derivative; TenantId isolation; fulfillment owned by
  `IOrderFulfillmentService`; payment/COD owned by payment lifecycle; events via `IEventPublisher`; fail-closed
  projections; stale-client totals recomputed (`SHIPPING_CHANGED`).
- Discrepancy vs mission assumptions: no zone/postal engine, no shipping-config events, no Super-Admin
  ownership, ServiceLevel is not an enum — corrected above from implementation.

---

## Forensic Audit Addendum — Shipping Runtime Authority (2026-07-17)

This addendum records a second implementation-only audit of the critical findings. It does not rely on the
previous report. Evidence below was verified from current implementation files.

### Runtime Authority Divergence

**Verdict: TRUE, with one important correction:** `CarrierAccount` is not purely administration-only; it is
also read by storefront shipping-method selection when a warehouse filter is supplied. It is still not read by
checkout, quotes, carrier shipment registration, carrier adapters, carrier webhooks, or carrier-registration
workers.

#### Verified architecture

Two carrier configuration systems exist:

1. **Legacy runtime carrier config: `CarrierConfiguration`**
   - Entity: `PointOfSale.Core/Entities/CarrierConfiguration.cs`.
   - Admin writer: `PointOfSale.API/Controllers/AdminCarrierConfigController.cs` (`/api/admin/carriers`).
   - Runtime readers:
     - `CheckoutService.ValidateShippingMethodAsync` lines 4787-4797 gates non-manual shipping methods on
       `CarrierConfiguration.IsEnabled`.
     - `ShippingQuoteService.GetQuotesAsync` lines 139-143 loads enabled `CarrierConfiguration` rows;
       `GetCarrierQuoteAsync` lines 199-250 uses the config to pick adapter, COD support, credentials,
       origin, fixed/carrier rates.
     - `CommerceHubShippingService.LoadStoreShippingOptionsAsync` lines 218-228 excludes non-manual methods
       without enabled `CarrierConfiguration`.
     - `ShipmentService.CreateShipmentAsync` lines 150-160 rejects non-manual shipping methods unless an enabled
       `CarrierConfiguration` exists.
     - `CarrierShipmentService` reads `CarrierConfiguration` for carrier create/registration, retry/reconcile,
       cancellation, sync, webhook trust policy, carrier geography resolution, and credentials (for example
       lines 317-333, 653-704, 1014-1038, 1481, 4128, 4338).

2. **Carrier lifecycle/account config: `CarrierAccount` aggregate**
   - Entities: `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`.
   - Admin writer/reader: `AdminShippingAdministrationController` + `ShippingAdministrationApiService`.
   - Runtime/admin implementation: `CarrierAccountAdministrationRuntime` supports registration, credential
     reference rotation, environment selection, warehouse assignments, policies, service levels, connection
     tests, activation/suspension/disable/retirement, health evaluation, and audit rows.
   - Non-admin runtime reader found: `ShippingMethodService.ApplyWarehouseAssignmentFilterAsync` lines 224-238
     joins `CarrierWarehouseAssignment` to `CarrierAccount` and requires `CarrierAccount.State == Active` and
     `CarrierAccount.OperationalState == Ready` to allow warehouse-scoped storefront method visibility.

#### Write authority

- `CarrierConfiguration` is written by `/api/admin/carriers` in `AdminCarrierConfigController`.
- `CarrierAccount`/satellite rows are written by `/api/admin/shipping/carrier-accounts` in
  `AdminShippingAdministrationController`, through `ShippingAdministrationApiService` and
  `CarrierAccountAdministrationRuntime`.

#### Runtime authority

- Checkout, quotes, CommerceHub shipping, shipment creation, carrier external registration, carrier cancellation,
  carrier tracking/sync, and webhook trust use **`CarrierConfiguration`**.
- Storefront shipping-method **display with warehouse filtering** uses **`CarrierAccount` + `CarrierWarehouseAssignment`**
  as an additional visibility gate after Mongo projection retrieval.
- Checkout does **not** use `CarrierAccount`; `git grep` over `*.cs` outside migrations finds no `CarrierAccount`
  reference in `CheckoutService`.
- `ShippingQuoteService` does **not** use `CarrierAccount`; it queries `Set<CarrierConfiguration>()` and builds
  `CarrierCredentials` from that row.

#### Root cause

The implementation contains two independently modeled configuration authorities: an older concrete carrier
credential/runtime table (`CarrierConfiguration`) and a newer lifecycle/governance aggregate (`CarrierAccount`)
with health, credentials-by-reference, policies, service levels, warehouse assignment, and audit. Runtime
integration was only partially wired: storefront warehouse filtering reads the new model, while the rest of
shipping runtime continues to read the legacy model.

#### Runtime behavior

A carrier can be:
- enabled in `CarrierConfiguration` but inactive/blocked/not-ready in `CarrierAccount`; checkout and quotes still
  see it as available, but warehouse-scoped storefront method listing may hide it.
- active/ready in `CarrierAccount` but disabled/missing in `CarrierConfiguration`; admin shipping lifecycle may
  show healthy governance state, but checkout/quotes/shipment registration fail or hide it.

#### Business impact

Merchants can observe inconsistent carrier availability between admin lifecycle screens, storefront method
visibility (when warehouse-filtered), checkout acceptance, quote results, and shipment registration. This can
surface as "available in admin but rejected at checkout", "visible in storefront selector but no quote", or
"checkout succeeds but carrier account health says blocked" depending on which system is updated.

#### Technical impact

Runtime authority is split across two tables with no adapter enforcing equivalence. Tests and monitoring must
cover both paths or risk certifying only one. Credential handling is duplicated conceptually: raw legacy config
fields feed actual carrier credentials, while the newer credential-reference model does not feed shipment or quote
credentials in verified code.

#### Recommended remediation

Pick one runtime authority and converge all readers:
1. Either promote `CarrierAccount` as authoritative and make checkout, quote, CommerceHub, shipment registration,
   cancellation, sync, and webhook trust resolve carrier availability/credentials from it; then demote or migrate
   `CarrierConfiguration`.
2. Or explicitly demote `CarrierAccount` to governance/visibility-only and rename/scope screens/API so operators
   cannot confuse it with carrier runtime availability.

Until convergence, document and test both tables as separate authorities and block admin UI from presenting one
as the source of truth for the other.

### Checkout Runtime / CQRS Verification

**Verdict: TRUE.** Checkout bypasses storefront projection and revalidates SQL authority.

Implementation path:
- `CheckoutService.ValidateShippingMethodAsync` loads the method through `LoadShippingMethodAuthorityAsync`, not
  through `IShippingMethodReadRepository`/Mongo. Non-locking reads query `_dbContext.ShippingMethods` with
  `IgnoreQueryFilters`, tenant id, method id, and `!IsDeleted`; final transaction reads use SQL row locks
  (`FOR UPDATE` for PostgreSQL, `UPDLOCK/ROWLOCK/HOLDLOCK` for SQL Server) when `lockAuthorityRow:true`.
- It rejects missing/inactive methods and gates non-manual carriers by enabled `CarrierConfiguration`.
- `CheckoutService` calls `_storefrontPricingService.CalculateCheckoutAsync(...)` and stores `pricing.Shipping`
  into `Order.ShippingCost`; it also stores `ShippingMethodId`, `ShippingMethodName`, `EstimatedDeliveryDays`,
  `FulfillmentMode`, and `FulfillmentCarrierCode` on the `Order`.
- Mongo/Redis are used by `ShippingMethodService` only for storefront selectors: Redis cache first, then
  `IShippingMethodReadRepository.GetActiveShippingMethodsAsync`, then Redis population. That service computes
  display prices with `IShippingCalculationService` but is not the checkout commit authority.
- `ShippingMethodProjectionConsumer` projects SQL `ShippingMethod` rows to Mongo and invalidates Redis. It always
  re-fetches from PostgreSQL, never trusting event payloads.

### CarrierAccount Usage Audit

**Verdict: PARTIALLY TRUE relative to the original claim.** `CarrierAccount` is consumed beyond admin, but only in
one storefront visibility path.

Repository-wide usage outside migrations/designer files:
- `ApplicationDbContext`: DbSets and EF mapping for `CarrierAccount` and satellite entities.
- `CarrierAccountAdministrationRuntime`: lifecycle mutation and validation implementation.
- `ShippingAdministrationApiService` / `AdminShippingAdministrationController`: admin API read/write/audit surface.
- `Program.cs`: DI registration for `ICarrierAccountAdministrationRuntime`.
- `ShippingMethodService.ApplyWarehouseAssignmentFilterAsync`: storefront warehouse-filter gate joins
  `CarrierWarehouseAssignment` to `CarrierAccount` and requires Active/Ready.
- Tests: `ShippingMethodServiceCurrencyConsistencyTests` seeds CarrierAccounts for the warehouse-filter path.

Absence verified by targeted `git grep`: no `CarrierAccount` references in `CheckoutService`,
`ShippingQuoteService`, `CarrierShipmentService`, carrier adapter files, shipment registration/retry paths,
webhook processing, or background service files outside the storefront method visibility filter and admin runtime.

### Shipment Service Duplication

**Verdict: PARTIALLY TRUE.** The two services have overlapping shipment-lifecycle logic, but they are not identical
and serve different API surfaces.

#### Responsibilities

- `ShipmentService` (`PointOfSale.API/Services/Shipping/ShipmentService.cs`): creates shipments from orders,
  validates order/payment readiness, validates selected shipping method equals the order snapshot, checks enabled
  `CarrierConfiguration`, emits `ShipmentCreatedEvent`/`ShipmentExternalRegistrationRequestedEvent`, provides
  storefront shipment reads, updates status, and performs authority reconciliation.
- `AdminShipmentService` (`PointOfSale.API/Services/Admin/AdminShipmentService.cs`): admin list/details/status,
  manual tracking updates, cancellations, serialization-retry loops, manual tracking mutation guards, and
  fulfillment synchronization for admin-driven status changes.

#### Differences

- `ShipmentService.CreateShipmentAsync` creates local shipment rows and emits external-registration request events;
  `AdminShipmentService` does not create shipments.
- `ShipmentService.UpdateStatusAsync` uses Redis idempotency keys and a reduced transition/update surface;
  `AdminShipmentService.UpdateShipmentStatusAsync` uses explicit serializable retry loops, richer admin DTOs,
  manual tracking mutation guards, cancellation handling, and Marketplace `OrderDeliveredEvent` emission.
- Admin service blocks manual tracking mutations on carrier-managed shipments; general service has a separate
  carrier-managed tracking guard for status updates.

#### Overlap

Both services implement status transition validation, timestamp stamping for shipment statuses, marketplace/order
delivery event emission, shipment status events, fulfillment synchronization through `IOrderFulfillmentService`,
DTO mapping, and event-version sequencing helpers (`EventTimestampSequence` appears in both).

#### Risk

Parallel lifecycle implementations can drift: a status transition, timestamp, event, or fulfillment-sync rule fixed
in one service may remain stale in the other. Because both mutate the same `Shipment` aggregate, divergence is a
runtime risk, not just code-style duplication.

#### Recommended remediation

Extract a single shipment lifecycle domain service for transition validation, timestamp application, event emission,
fulfillment synchronization, and tracking-mutation ownership checks. Keep admin/storefront controllers and DTOs
separate, but route all aggregate mutation through the shared domain service.

### Technical Debt Audit Updates

Additional verified debt signals:
- `CarrierCredentialEncryptionService` logs "Carrier credential is not encrypted (legacy value)" for legacy values.
- `CarrierShipmentService` contains a "legacy entry point" comment warning it must not replace authoritative
  checkout shipping method/cost and contains `ApplyLegacyProviderGeographyFields`.
- `Shipment.ShippingMethod` region is explicitly named `Shipping Method (Legacy/Fallback)`.
- `CarrierConfiguration` remains the runtime credential source while `CarrierCredentialReference` remains unused by
  quote/shipment runtime.
- `ShippingMethodService` class comment says "NO SQL ACCESS - PostgreSQL is forbidden here", but implementation
  reads SQL for warehouse existence and `CarrierAccount`/warehouse assignments when `warehouseId` is provided.
  That comment is stale relative to implementation.

### Certification Verdict Amendment

Certification remains **PARTIALLY CERTIFIED**. The key correction from the second audit is that `CarrierAccount`
should not be documented as strictly administration-only: it also participates in warehouse-scoped storefront
shipping-method visibility. Runtime authority divergence is still confirmed because checkout, quotes, shipment
registration, cancellation, webhook trust, and carrier credentials continue to use `CarrierConfiguration` rather
than `CarrierAccount`.

