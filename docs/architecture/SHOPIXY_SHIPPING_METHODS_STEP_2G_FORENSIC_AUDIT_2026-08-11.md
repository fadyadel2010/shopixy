# SHOPIXY Shipping Methods Step 2G Forensic Audit

Date: 2026-08-11

Scope: audit only. No source, configuration, schema, migration, test, frontend, database, or git mutation was performed except adding this report.

Certification: DESIGN READY WITH BUSINESS DECISION REQUIRED

## 1. Executive Summary

Current source proves this shipping relationship:

```text
ShippingMethod
  -> CarrierCode
  -> tenant CarrierAccount
  -> CarrierWarehouseAssignment
  -> Warehouse
```

This is not a strict `ShippingMethod -> Warehouse` relationship. `ShippingMethod` stores tenant, customer-facing method data, pricing, `CarrierCode`, and `ServiceLevel`, but no `WarehouseId`, `CarrierAccountId`, or `CarrierWarehouseAssignmentId` (`PointOfSale.Core/Entities/ShippingMethod.cs:8`, `:75`, `:82`, `:89`). `CarrierWarehouseAssignment` stores `TenantId`, `CarrierAccountId`, `WarehouseId`, `IsEnabled`, and `OriginReadinessPublished` (`PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs:115-122`). EF maps it as a tenant-safe carrier-account-to-warehouse readiness relation (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:3241-3260`).

Therefore `CarrierWarehouseAssignment` can represent "this external carrier account can operate from this warehouse". It can only accidentally derive "this shipping method is fulfilled from this warehouse" when a non-manual method's carrier has exactly one enabled, origin-ready assignment. It cannot support `Bosta Express -> Cairo` and `Bosta Standard -> Alexandria`, because both methods share the same `CarrierCode` and current EF enforces one non-deleted `CarrierAccount` per `(TenantId, CarrierCode)` (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:3214-3235`). It cannot support Manual Fulfillment, because manual carrier accounts and manual warehouse assignments are rejected (`PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs:111-124`; `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs:181-185`).

The smallest safe architectural change is to add one tenant-owned fulfillment warehouse reference directly to `ShippingMethod`, preferably named `FulfillmentWarehouseId` to avoid confusing carrier-account readiness with the method's fulfillment origin. After the customer selects a method, the server loads the method's configured fulfillment warehouse, validates warehouse activity, stock, and external carrier assignment if needed, then creates/locks `WarehouseResolution`. Step 2F's `WarehouseResolution`, explicit reservation, `Order.WarehouseId`, `OrderItem.WarehouseId`, shipment origin propagation, tenant checks, idempotency, and transaction guarantees should be preserved.

Business decisions still required: legacy data backfill behavior, whether single-warehouse tenants should be auto-defaulted at authoring time, and what to do with existing methods whose carrier can operate from multiple warehouses but no method-level warehouse is configured.

## 2. Evidence Reviewed

Historical reports reviewed as evidence and cross-checked against source:

- `docs/architecture/SHOPIXY_SHIPPING_METHODS_FORENSIC_AUDIT_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2A_TENANT_SCOPE_REMEDIATION_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2B_FORENSIC_AUDIT_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2B_IMPLEMENTATION_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2C_IMPLEMENTATION_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2D_FORENSIC_AUDIT_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2E_WAREHOUSE_RESOLUTION_DESIGN_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2F_FORENSIC_IMPLEMENTATION_REPORT_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2F_IMPLEMENTATION_2026-08-11.md`
- `docs/architecture/SHOPIXY_DYNAMIC_CHECKOUT_SHIPPING_WAREHOUSE_FINAL_FORENSIC_AUDIT_2026-08-11.md`

Baseline commands were captured before analysis:

- `git status --short`: existing dirty worktree with many modified/untracked Step 2A-2F files.
- `git diff --stat`: existing source/test/configuration diff across 43 tracked files before this Step 2G report.

End commands were captured after this report was added:

- `git status --short`: same pre-existing modified/untracked source, test, schema, and Step 2A-2F report files, plus exactly `?? docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2G_FORENSIC_AUDIT_2026-08-11.md`.
- `git diff --stat`: unchanged tracked-file stat from the starting baseline, still 43 tracked files with 2010 insertions and 221 deletions; untracked docs do not appear in `git diff --stat`.

Worktree certification for this audit: no source mutation, no config mutation, no schema/migration mutation, no test mutation, no frontend mutation, no git mutation, and only the Step 2G report was added by this audit.

The source files most relevant to this audit were `ShippingMethod.cs`, `CarrierAccountRuntimeEntities.cs`, `Warehouse.cs`, `WarehouseResolution.cs`, `ApplicationDbContext.cs`, `ShippingMethodsController.cs`, `ShippingAdministrationApiService.cs`, `CarrierAccountAdministrationRuntime.cs`, `ShippingMethodService.cs`, `StorefrontShippingMethodsController.cs`, `WarehouseResolutionService.cs`, `CheckoutService.cs`, `ProductVariantStockRepository.cs`, `Order.cs`, `OrderItem.cs`, `Shipment.cs`, `ShipmentService.cs`, `CarrierShipmentService.cs`, and the shipping administration/Storefront checkout frontend files.

## 3. Current Shipping Domain

Source-proven relationship graph:

```text
ShippingMethod
  TenantId
  Name / description / rate / delivery days / active / sort order
  CarrierCode
  ServiceLevel
  CarrierName display metadata

CarrierCode
  -> IShippingCarrierRegistry adapter identity

CarrierAccount
  TenantId
  CarrierCode
  State / OperationalState / health

CarrierWarehouseAssignment
  TenantId
  CarrierAccountId
  WarehouseId
  IsEnabled
  OriginReadinessPublished

Warehouse
  TenantId
  Name / Code / Address / Phone / IsActive / IsDefault
```

This is option D from the task's relationship list for external carriers: `ShippingMethod -> CarrierCode -> CarrierAccount -> CarrierWarehouseAssignment -> Warehouse`. It is not option B or C because there is no direct `ShippingMethod -> Warehouse` or `ShippingMethod -> CarrierAccount` FK.

Carrier identity is a normalized string code. `CarrierCodes.Normalize` maps null/blank to `manual`, and `CarrierCodes.IsManual` treats null/blank/manual as Manual Fulfillment (`PointOfSale.Core/Constants/CarrierCodes.cs:59-70`). The registry is keyed by adapter `CarrierCode` (`PointOfSale.Infrastructure/Shipping/ShippingCarrierRegistry.cs:18-45`, `:59-70`, `:85-102`).

## 4. ShippingMethod Semantics

`ShippingMethod` currently means a tenant-owned customer-facing delivery option with pricing, delivery promise, carrier adapter code, and service level. It is not currently an origin authority. Evidence:

- Entity summary says "Shipping method configuration for e-commerce orders" (`PointOfSale.Core/Entities/ShippingMethod.cs:5-8`).
- Pricing and display fields are method-owned (`ShippingMethod.cs:17-68`).
- `CarrierName` is display metadata (`ShippingMethod.cs:72-75`).
- `CarrierCode` links the method to a carrier adapter and null means manual (`ShippingMethod.cs:78-82`).
- `ServiceLevel` is delivery speed and carrier service-level metadata (`ShippingMethod.cs:85-89`).
- Admin create/update persists canonical carrier name/code/service level and does not accept warehouse (`PointOfSale.API/Controllers/ShippingMethodsController.cs:121-146`, `:191-220`, `:556-624`).

Conclusion: SHOPIXY shipping methods are currently a mixture of A and B from the task: a customer-facing delivery option plus carrier service metadata. They are not yet C, carrier service plus fulfillment origin. Adding a clearly named fulfillment warehouse would intentionally make them C. That does not violate an existing FK boundary because no source currently assigns a different warehouse owner to `ShippingMethod`; it fills an explicit gap.

## 5. Carrier Semantics

Carrier terms are distinct:

- `CarrierCode`: global adapter identity such as `manual`, `bosta`, `aramex`, `mylerz`, `dhl`, `fedex` (`PointOfSale.Core/Constants/CarrierCodes.cs:19-54`).
- `CarrierName`: display metadata, derived on admin read from registry (`PointOfSale.API/Controllers/ShippingMethodsController.cs:493-514`).
- `CarrierAccount`: tenant-owned operational account for an external carrier (`PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs:88-104`).
- `CarrierWarehouseAssignment`: tenant-owned origin readiness relation between a carrier account and a warehouse (`CarrierAccountRuntimeEntities.cs:115-122`).
- `ServiceLevel`: method and carrier service speed code (`ShippingMethod.cs:85-89`; `ShippingMethodsController.cs:460-482`).
- `Warehouse`: tenant-owned inventory/origin location (`PointOfSale.Core/Entities/Warehouse.cs:8-21`).

Explicit semantic boundary:

```text
Carrier != CarrierAccount != ShippingMethod != Warehouse
```

The current carrier account model supports one non-deleted account per tenant/carrier code, not multiple Bosta accounts for the same tenant (`ApplicationDbContext.cs:3214-3235`). Service-level enablement is account-level, not assignment-level (`CarrierAccountRuntimeEntities.cs:135-140`; `ApplicationDbContext.cs:3277`).

## 6. CarrierWarehouseAssignment Semantics

`CarrierWarehouseAssignment` means:

```text
This tenant carrier account is enabled/origin-ready for this tenant warehouse.
```

Evidence:

- Entity fields are `TenantId`, `CarrierAccountId`, `WarehouseId`, `IsEnabled`, `OriginReadinessPublished`, and readiness evidence (`CarrierAccountRuntimeEntities.cs:115-122`).
- Admin route is account-scoped: `PUT /api/admin/shipping/carrier-accounts/{carrierAccountId}/warehouse-assignments/{warehouseId}` (`PointOfSale.API/Controllers/AdminShippingAdministrationController.cs:66-69`).
- Runtime validates the carrier account, rejects manual, verifies warehouse exists for same tenant, active, not deleted, then creates/updates the assignment (`CarrierAccountAdministrationRuntime.cs:168-232`).
- Activation gates require at least one enabled assignment and origin readiness (`CarrierAccountAdministrationRuntime.cs:547-558`).
- Storefront warehouse filtering joins assignments to accounts and requires tenant, warehouse, enabled, origin-ready, active account, and ready operational state (`PointOfSale.API/Services/Storefront/ShippingMethodService.cs:184-250`).

It does not mean "this shipping method is fulfilled from this warehouse". There is no `ShippingMethodId`, no service-level code, no method FK, and no manual support.

## 7. Manual Fulfillment Semantics

Manual Fulfillment is `carrierCode = manual`, an internal/no-external adapter. Evidence:

- `CarrierCodes.Manual = "manual"` (`CarrierCodes.cs:19-23`).
- `ManualCarrierDefinition.CarrierCode` returns manual and display name is "Manual Fulfillment" (`PointOfSale.Infrastructure/Shipping/Adapters/ManualCarrierDefinition.cs:18-24`).
- Manual shipment creation returns a local pending result with no external tracking (`ManualCarrierDefinition.cs:47-59`).
- Manual credential validation succeeds without external credentials (`ManualCarrierDefinition.cs:106`).
- Supported-carrier DTO marks manual as not external, not account required, and not readiness required (`ShippingAdministrationApiService.cs:25-50`).
- Manual carrier account creation is rejected (`ShippingAdministrationApiService.cs:111-124`).
- Manual warehouse assignment through carrier-account runtime is rejected (`CarrierAccountAdministrationRuntime.cs:181-185`).
- Checkout classifies null/blank/manual carrier codes as `FulfillmentMode.Manual` (`CheckoutService.cs:9444-9456`).

Therefore Manual Fulfillment has no source-supported way to derive a warehouse from `CarrierWarehouseAssignment`. Its warehouse must come from either method-level fulfillment warehouse or a new manual-specific relation. The smallest clean solution is the same method-level fulfillment warehouse relation used by external shipping methods.

## 8. Current Warehouse Resolution Flow

Step 2F current source resolves warehouse before shipping method selection:

```text
CheckoutSession + cart
  -> WarehouseResolutionService.ResolveForPreviewAsync
  -> exact-one inventory candidate
  -> ShippingMethodService.GetShippingMethodsAvailabilityAsync(... warehouseId)
  -> customer receives filtered methods
```

Evidence:

- Preview request accepts only `CheckoutSessionId` (`PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs:132-147`).
- Preview controller calls `ResolveForPreviewAsync`, then calls shipping method availability with `resolution.Resolution.WarehouseId` (`PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs:142-199`).
- Resolution algorithm code is `STOREFRONT_SINGLE_WAREHOUSE_EXACT_ONE_V1` (`PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs:85-105`).
- Candidate warehouses come from active same-tenant warehouses with enough stock for all stock-tracked required variants (`WarehouseResolutionService.cs:462-575`).
- Exactly one candidate resolves, zero fails `NO_ELIGIBLE_WAREHOUSE`, multiple fails `WAREHOUSE_SELECTION_POLICY_REQUIRED` (`WarehouseResolutionService.cs:340-378`).

This is safe but warehouse-first. It does not use selected `ShippingMethodId` as a fulfillment-context input.

## 9. Inventory Flow

Current Storefront checkout preserves Step 2F's important reservation invariant:

```text
WarehouseResolution.WarehouseId -> explicit reservation warehouse
```

Evidence:

- Checkout validates submitted resolution before idempotency and again inside the final serializable transaction (`CheckoutService.cs:864-880`, `:6403-6413`).
- Final transaction sets `order.WarehouseId`, each `orderItem.WarehouseId`, and then calls inventory reservation with the resolved warehouse (`CheckoutService.cs:6451-6475`).
- `ValidateAndReserveInventoryAsync` now accepts `Guid warehouseId`, queries stock where `stock.WarehouseId == warehouseId`, and calls `ReserveAsync(..., warehouseId, ...)` (`CheckoutService.cs:5696-5795`).
- `InventoryReservation.WarehouseId` is required (`PointOfSale.Core/Entities/InventoryReservation.cs:6-15`).

The repository still has a null-warehouse fallback that loads stock across warehouses and orders by `WarehouseId`, then stock row `Id` (`PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:81-177`, `:892-946`). Storefront checkout should continue not to use that fallback.

## 10. Order/Shipment Flow

Order and shipment are compatible with a single authoritative warehouse selected before order creation:

- `Order.WarehouseId` is nullable (`PointOfSale.Core/Entities/Order.cs:651`).
- `OrderItem.WarehouseId` is nullable (`PointOfSale.Core/Entities/OrderItem.cs:52`).
- EF maps both to warehouse FKs with `SetNull` delete behavior (`ApplicationDbContext.cs:1955-1979`, `:1990-2058`).
- EF immutability guards include `Order.WarehouseId` and `OrderItem.WarehouseId` (`ApplicationDbContext.cs:3918-3965`).
- Storefront checkout stamps the resolved warehouse onto the order and every order item (`CheckoutService.cs:6451-6462`).
- `ShipmentService.CreateShipmentAsync` copies `OriginWarehouseId = order.WarehouseId` (`PointOfSale.API/Services/Shipping/ShipmentService.cs:170-176`).
- Carrier shipment origin restoration/derivation uses `order.WarehouseId` and writes `shipment.OriginWarehouseId` from the admitted snapshot (`PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs:3843-3986`).

The final invariant can be preserved after an architectural inversion:

```text
Selected ShippingMethod
  -> method fulfillment warehouse
  -> WarehouseResolution
  -> reservation
  -> Order.WarehouseId
  -> OrderItem.WarehouseId
  -> Shipment.OriginWarehouseId
```

## 11. Storefront Preview Flow

Current endpoint:

```http
POST /api/storefront/shipping-methods/preview
```

Current behavior requires warehouse resolution before shipping selection. The reason is source-level: the controller resolves first and passes the resolved warehouse into `ShippingMethodService` (`StorefrontShippingMethodsController.cs:175-190`). The service only applies warehouse assignment filtering when a warehouse ID is supplied (`ShippingMethodService.cs:54-122`, `:184-250`).

Target-compatible behavior can keep the same endpoint path but change responsibility:

```text
Preview:
  load active methods
  validate each method's configured fulfillment warehouse server-side
  check stock at that method warehouse
  check external carrier assignment for that method warehouse
  return customer-facing methods and availability status
  do not expose warehouse IDs

Selection:
  customer submits selected method
  server creates/locks WarehouseResolution from method.FulfillmentWarehouseId
```

The current `ShippingMethodDto` does not expose `CarrierCode` or `WarehouseId` (`ShippingMethodDto.cs:17-76`). That is good for the customer boundary.

## 12. Admin UX Flow

Current admin shipping method UI already selects canonical carrier and service level:

- Form state has `carrierCode` and `serviceLevel` (`PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx:135-143`).
- Save payload sends normalized `carrierCode` and `serviceLevel` (`ShippingMethodsTab.jsx:228-248`).
- UI renders carrier selector from backend supported carriers (`ShippingMethodsTab.jsx:530-552`) and service levels (`ShippingMethodsTab.jsx:573-586`).

Current shipping administration also already loads active warehouses and renders account warehouse selectors:

- Hook fetches warehouses along with supported carriers/accounts (`PointOfSale.Web/src/modules/shipping-administration/hooks/useShippingAdministrationApi.js:6-31`).
- API fetches `/warehouses` and admits `id`, `name`, `code`, `isActive`, and `isDefault` (`PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js:170-200`).
- Carrier account card renders warehouse select by name/code (`CarrierAccountCard.jsx:245-263`).

Smallest merchant-facing change: add one `Fulfillment Warehouse` selector to the Shipping Method form using the already loaded active tenant warehouse list. Avoid duplicate warehouse selectors by keeping the carrier-account assignment selector only for external carrier readiness and making the method selector the order fulfillment origin.

## 13. Single-Warehouse Scenario

For a tenant with only Cairo active:

Current Step 2F exact-one inventory resolver usually works if Cairo can fulfill the cart. However the merchant still cannot author "Bosta Express -> Cairo", "Bosta Standard -> Cairo", and "Manual -> Cairo" directly on methods, because `ShippingMethod` has no warehouse field.

With the recommended method-level fulfillment warehouse:

- New method authoring can default/select Cairo.
- Manual uses Cairo without carrier account/assignment.
- External methods use Cairo as the fulfillment origin and separately validate that the external carrier account has an origin-ready Cairo assignment.
- The merchant does not need to understand candidate resolution, warehouse resolution versions, or stock routing algorithms.

Business decision required: whether to auto-default the single active warehouse in admin create/edit, or require explicit merchant confirmation while preselecting it.

## 14. Multi-Warehouse Scenario

Current source supports these cases only indirectly:

- Case 1, `Bosta Express -> Cairo`: not representable if another Bosta method must use a different warehouse, because method has no warehouse FK.
- Case 2, `Bosta Standard -> Alexandria`: not representable separately from Express under the same tenant/carrier account.
- Case 3, `Manual -> Cairo`: not representable through `CarrierWarehouseAssignment`; manual has no carrier assignment.
- Case 4, `Bosta Express -> Cairo + Alexandria`: current `CarrierWarehouseAssignment` can express Bosta account readiness for both warehouses, but no legitimate rule selects one for the method.

For Case 4, do not invent a winner. Current Step 2F safely fails multiple inventory candidates with `WAREHOUSE_SELECTION_POLICY_REQUIRED` (`WarehouseResolutionService.cs:362-378`). If using `CarrierWarehouseAssignment` as derivation, multiple ready assignments for the selected method's carrier must be treated as `BUSINESS DECISION REQUIRED` or rejected until merchant config sets one fulfillment warehouse.

## 15. Architecture Options

Option A, `ShippingMethod.FulfillmentWarehouseId`: one method has one fulfillment warehouse. Manual and external carriers share the same simple rule. External carrier assignment is validation, not origin selection.

Option B, dedicated `ShippingMethodWarehouseAssignment`: semantically correct but heavier than needed if one method must have exactly one warehouse. Useful only if the domain wants history/evidence or future multi-warehouse-per-method.

Option C, `ShippingMethod -> CarrierWarehouseAssignment`: too tightly couples shipping methods to external carrier readiness and excludes manual unless a parallel manual path is invented.

Option D, current `CarrierAccount -> CarrierWarehouseAssignment -> Warehouse`: safe readiness model, not a method-origin model. Ambiguous for multiple assignments and same-carrier methods.

Option E, keep Step 2F warehouse-first resolution: preserves strong authority but blocks multi-warehouse tenants without a warehouse winner policy even when the selected shipping method could have made the warehouse obvious.

Option F, derive from `CarrierWarehouseAssignment`: no schema change, but only safe in the exact-one external-carrier-assignment case and unsafe as general architecture.

## 16. Decision Matrix

| Option | Domain correctness | Cardinality | Manual support | External carrier support | Multi-warehouse | Single-warehouse UX | Tenant safety | Migration impact | Runtime complexity | Future extensibility | Compatibility | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A: Warehouse directly on ShippingMethod | High if named fulfillment warehouse | Exact one | High | High with assignment validation | High for per-method origin | Best | Strong with composite tenant FK | One small migration | Low | Good; can later add richer relation | Nullable/backfill policy needed | SELECTED |
| B: Dedicated ShippingMethodWarehouseAssignment | High | Can enforce one or many | High | High | High | Medium | Strong with tenant FKs | New table/indexes | Medium | Best | More moving parts | Not selected now |
| C: ShippingMethod -> CarrierWarehouseAssignment | Medium-low | One assignment | Poor | Medium | Medium | Poor for manual | Strong if tenant FK | One FK column | Medium | Poor; readiness and origin coupled | Breaks manual | Reject |
| D: CarrierAccount -> Assignment -> Warehouse | Medium as readiness only | One account to many warehouses | None | Medium | Ambiguous | Medium | Already strong | None | Medium-high due ambiguity | Poor for method-specific origin | Current behavior only | Preserve as validation |
| E: Keep Step 2F warehouse-first | Medium for warehouse authority | Exact-one inventory candidate | Medium | Medium | Blocks legitimate per-method cases | Good for exact-one stock | Strong | Existing Step 2F migration | Medium | Needs routing policy | Existing current source | Reposition |

## 17. Recommended Architecture

Recommended architecture:

```text
Admin:
  ShippingMethod.FulfillmentWarehouseId is required for active Storefront checkout methods.

Storefront preview:
  Shipping methods are filtered by each method's configured fulfillment warehouse.
  Warehouse IDs are not exposed to the customer.

Customer selection:
  Customer selects shippingMethodId only.

Server:
  Load selected ShippingMethod.
  Validate method tenant, active, carrier code, service level, configured warehouse.
  Validate warehouse tenant, active, not deleted.
  Validate stock at that warehouse.
  For external carriers, validate matching CarrierWarehouseAssignment.
  Create/lock WarehouseResolution from the method warehouse.
  Reserve inventory from that warehouse.
  Stamp Order.WarehouseId and OrderItem.WarehouseId.
  Shipment origin derives from Order.WarehouseId.
```

`CarrierWarehouseAssignment` remains external carrier account origin readiness. It should not become the customer-facing shipping method origin relation.

## 18. Required Changes

Minimum source changes for later implementation:

1. Add nullable `ShippingMethod.FulfillmentWarehouseId` or `ShippingMethod.WarehouseId`.
2. Add EF composite FK `{TenantId, FulfillmentWarehouseId} -> Warehouses {TenantId, Id}` and index.
3. Add admin DTO field and validation requiring same-tenant active warehouse when method is active or used for Storefront.
4. Add read projection field if Storefront preview needs method warehouse without extra SQL per method.
5. Update preview to validate each method's fulfillment warehouse and stock, not to run exact-one cart-wide resolver first.
6. Update selection/final checkout to create/load `WarehouseResolution` from selected method warehouse.
7. Preserve Step 2F final checkout transaction, explicit warehouse reservation, order/order item stamping, idempotency, and shipment origin propagation.

## 19. Migration Impact

Migration required: yes, one small additive migration.

Recommended shape:

```text
ShippingMethods
  FulfillmentWarehouseId uuid null

FK:
  (TenantId, FulfillmentWarehouseId) -> Warehouses(TenantId, Id)

Index:
  IX_ShippingMethods_TenantId_FulfillmentWarehouseId
```

Do not make the column globally non-null immediately because legacy rows exist and live data was not inspected. Do not silently backfill multi-warehouse tenants.

Possible safe backfill policy, pending business approval:

- Tenants with exactly one active warehouse: optionally assign that warehouse to active shipping methods.
- External methods whose carrier account has exactly one enabled origin-ready assignment: optionally assign that warehouse only after confirming this is intended semantics.
- Manual methods in multi-warehouse tenants: require merchant selection.
- Any ambiguous case: leave null and fail authoring/preview with a clear admin action.

## 20. API Impact

Minimum admin API impact:

- Add `fulfillmentWarehouseId` to create/update/read shipping method DTOs.
- Validate same tenant, active, not deleted through existing warehouse authority.
- Return warehouse display metadata for admin reads if needed.

Minimum Storefront API impact:

- Preview can remain `POST /api/storefront/shipping-methods/preview`, but should no longer require a `WarehouseResolutionId` before method selection.
- Add or repurpose a method-selection/checkout validation step that creates `WarehouseResolution` after selected `shippingMethodId`.
- Final checkout may continue accepting `checkoutSessionId`, `warehouseResolutionId`, and `warehouseResolutionVersion`, preserving Step 2F's final validation.

Do not add `warehouseId` to Storefront request DTOs.

## 21. Storefront Impact

Storefront should show shipping methods exactly as today: customer chooses a shipping method, not a warehouse.

Behavior by failure:

- Method has no configured warehouse: server hides/rejects method; admin action required.
- Warehouse inactive/deleted: server hides/rejects method; admin action required.
- Multiple warehouses are eligible through carrier assignment but no method warehouse exists: `BUSINESS DECISION REQUIRED`, do not choose.
- Warehouse lacks stock: method unavailable or checkout conflict if stock changed after preview.
- Carrier assignment unavailable: external method unavailable or checkout conflict.
- Manual has no warehouse: design gap until method-level warehouse is configured.

The current Storefront type includes resolution ID/version in checkout request (`PointOfSale.Storefront/src/types/checkout.ts:165-184`) and submit sends those IDs without warehouse ID (`checkoutStore.ts:2545-2558`). That shape can remain if selection creates the resolution before final submit.

## 22. Admin Impact

Minimum Admin UI change:

```text
Shipping Method:
  Carrier: [Bosta]
  Service: [Express]
  Fulfillment Warehouse: [Cairo Warehouse]

Manual Method:
  Fulfillment: [Manual Fulfillment]
  Warehouse: [Cairo Warehouse]
```

Use the existing warehouse API and display names (`WarehousesController.cs:47-57`; `shippingAdministrationApi.js:170-200`). Keep the carrier-account warehouse assignment UI for external carrier readiness only. Do not expose raw warehouse IDs as merchant workflow. Do not duplicate assignment controls inside carrier account and method forms as if they mean the same thing.

## 23. Security Impact

A future method-level warehouse relation must enforce:

- Tenant-owned shipping method (`ApplicationDbContext.cs:666-668`).
- Tenant-owned active warehouse (`WarehousesController.cs:47-57`; `ApplicationDbContext.cs:584-591`).
- Composite tenant FK to prevent cross-tenant warehouse references.
- Admin authorization as current shipping-method controller does.
- Browser never submits authoritative warehouse ID.
- Storefront selected method is reloaded by tenant and active state.
- External carrier assignment joins tenant account and tenant warehouse as current source already does (`ShippingMethodService.cs:216-250`; `WarehouseResolutionService.cs:608-629`).

Current assignment infrastructure already provides same-tenant validation for carrier account warehouse readiness, but not method-level warehouse authority.

## 24. Performance Impact

Current Step 2F:

```text
Cart -> scan active warehouses -> grouped stock by warehouse/variant -> exact-one resolution -> shipping methods
```

Proposed method-first:

```text
Preview methods -> validate each method's configured warehouse and stock eligibility
Selection -> known warehouse -> WarehouseResolution -> reservation/order/shipment
```

The proposed path can require fewer candidate scans after selection because the warehouse is known from the method. Preview may check stock per method warehouse, but that can be grouped by distinct configured warehouse IDs rather than scanning all warehouses as routing candidates. It also avoids locking/routing semantics before the customer has chosen a method. Do not optimize prematurely; the primary value is simpler authority and less business-policy invention.

## 25. Backward Compatibility

Existing rows:

- Valid manual methods remain manual by `CarrierCode`.
- Valid Bosta/external methods remain carrier methods by `CarrierCode`.
- Existing methods have no fulfillment warehouse until migration/backfill/merchant update.
- Existing `CarrierWarehouseAssignment` rows remain external carrier readiness and should not be reinterpreted wholesale as method origins.
- Existing Step 2F `WarehouseResolution` rows remain historical authority records.
- Existing orders with `Order.WarehouseId` null remain legacy; do not make order warehouse globally non-null.

Do not propose a migration that silently changes production semantics. Any backfill must be explicit and auditable, especially for multi-warehouse tenants and external methods with more than one ready origin.

## 26. What We Preserve From Step 2F

Preserve:

- `WarehouseResolution` entity and EF tenant/status/version/immutability structure (`WarehouseResolution.cs:11-45`; `ApplicationDbContext.cs:1638-1702`, `:4028-4051`).
- Server-owned resolution IDs/version, not browser warehouse IDs (`ShippingMethodDto.cs:132-165`; `CheckoutDtos.cs:21-38`).
- Final checkout row lock and validation (`WarehouseResolutionService.cs:700-760`).
- Explicit warehouse reservation (`CheckoutService.cs:5696-5795`).
- `Order.WarehouseId` and `OrderItem.WarehouseId` stamping (`CheckoutService.cs:6451-6462`).
- Shipment origin deriving from order warehouse (`ShipmentService.cs:170-176`).
- Manual does not require carrier account assignment (`ShippingMethodService.cs:250`; `WarehouseResolutionService.cs:598-606`).
- Multiple warehouse ambiguity is not resolved by lowest ID or lock order.

Reposition:

- `WarehouseResolution` should become the immutable server-side record of the warehouse selected by the shipping method's fulfillment context, not the independent pre-selection routing engine for all methods.

## 27. What We Should NOT Change

Do not:

- Add customer-facing warehouse selection.
- Add `warehouseId` to Storefront checkout authority.
- Use lowest/highest GUID, nearest warehouse, default warehouse, carrier priority, or stock optimization as implicit policy.
- Reinterpret `CarrierWarehouseAssignment` as method origin generally.
- Require carrier accounts for Manual Fulfillment.
- Remove `WarehouseResolution`.
- Reopen null-warehouse Storefront reservation.
- Let shipment or inventory independently choose a different origin after method selection.
- Build split fulfillment or a routing engine unless business requirements explicitly demand it.

## 28. Business Decisions Required

BUSINESS DECISION REQUIRED:

1. Legacy backfill: what should happen to existing active shipping methods without `FulfillmentWarehouseId`?
2. Single-warehouse authoring: should the only active warehouse be auto-selected, preselected, or explicitly confirmed?
3. Ambiguous external carrier assignments: if one Bosta account has Cairo and Alexandria, should existing Bosta methods be blocked until each method gets a warehouse?
4. Existing Step 2F exact-one behavior: should it remain as a fallback for legacy methods, or should preview fail closed until method warehouse is configured?
5. Future multi-warehouse methods: is one warehouse per shipping method a permanent rule, or should a richer assignment table be planned later?

## 29. Exact Next Implementation Step

Exact next implementation step:

```text
Add nullable ShippingMethod.FulfillmentWarehouseId with tenant-safe FK to Warehouse,
surface it in admin shipping method create/update/read,
and make Storefront preview/final checkout require it for active checkout methods
while preserving WarehouseResolution as the post-selection lock/audit record.
```

Implementation should start with source and tests proving:

- `ShippingMethod` can carry exactly one fulfillment warehouse.
- Manual methods require that warehouse but no carrier assignment.
- External methods require that warehouse plus matching carrier assignment/configuration.
- Storefront never receives or submits warehouse IDs.
- Existing Step 2F final propagation remains intact.

## 30. Final Certification

DESIGN READY WITH BUSINESS DECISION REQUIRED

The source supports a minimal safe architecture: add one method-level fulfillment warehouse relation and reposition `WarehouseResolution` after shipping method selection. `CarrierWarehouseAssignment` cannot safely serve as the general `ShippingMethod -> Warehouse` model because it is account-level, external-carrier-only readiness, ambiguous with multiple assignments, and incompatible with Manual Fulfillment.

The design is ready, but legacy backfill and ambiguous multi-warehouse production behavior require explicit business decisions before implementation.
