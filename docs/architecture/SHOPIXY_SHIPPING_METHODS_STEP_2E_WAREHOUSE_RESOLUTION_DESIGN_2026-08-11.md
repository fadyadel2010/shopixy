# SHOPIXY Shipping Methods Step 2E Warehouse Resolution Design

Date: 2026-08-11
Scope: Architecture and implementation design only
Mode: Design only. No implementation performed.

## 1. Executive Summary

Step 2D established that Storefront checkout has no central warehouse
resolution authority. Step 2E selects the minimum coherent target architecture
for the current single-warehouse Storefront order model.

Chosen architecture:

```text
Cart
  -> Server Warehouse Resolution
  -> Persisted WarehouseResolution authority
  -> Shipping Method Eligibility
  -> Checkout
  -> Serializable Final Validation
  -> Inventory Reservation
  -> Order.WarehouseId
  -> OrderItem.WarehouseId
  -> Shipment.OriginWarehouseId
```

The chosen persistence boundary is a dedicated `WarehouseResolution` entity
linked to `CheckoutSession`, `CartId`, `TenantId`, and `WarehouseId`.

`CheckoutSession.WarehouseId` alone is not sufficient. Checkout sessions are
durable and tenant-owned, but they are also recovery/autosave records with
duplicate-session reconciliation and critical-data merge behavior. The warehouse
decision needs its own status, source, version, input hash/snapshot,
expiration/invalidation, and final lock semantics.

Final certification:

DESIGN READY WITH BUSINESS DECISION REQUIRED

The technical authority boundary is clear. The missing business decision is the
winner rule when more than one active warehouse can fulfill the entire cart.
Source evidence does not justify nearest warehouse, customer city, first
available warehouse, lowest GUID, or default warehouse as the canonical policy.

## 2. Step 2D Findings Recap

Step 2D found:

- NO CENTRAL WAREHOUSE RESOLUTION AUTHORITY FOUND for Storefront checkout.
- Storefront loads shipping methods without warehouse context.
- Checkout request does not contain warehouse context.
- `CheckoutService` reserves inventory with `warehouseId = null`.
- `ProductVariantStockRepository` chooses stock rows ordered by `WarehouseId`,
  then stock row `Id`.
- Reservation can span multiple warehouses.
- Storefront `Order.WarehouseId` remains null.
- Storefront `OrderItem.WarehouseId` remains null.
- `Shipment.OriginWarehouseId` is not set during shipment creation.
- Carrier shipment origin later derives from `Order.WarehouseId` and fails
  closed when it is null.
- `CarrierWarehouseAssignment` filters shipping methods only when a warehouse ID
  is supplied to the Storefront shipping-method service.

The invariant to implement in Step 2F is:

```text
Resolved warehouse == shipping eligibility warehouse
Resolved warehouse == reservation warehouse
Resolved warehouse == Order.WarehouseId
Resolved warehouse == OrderItem.WarehouseId
Resolved warehouse == Shipment.OriginWarehouseId source
```

## 3. Current CheckoutSession Architecture

Source findings:

- `CheckoutSession` is a tenant-owned entity and inherits tenant identity through
  `TenantEntity` (`PointOfSale.Core/Entities/CheckoutSession.cs:23`).
- EF maps `CheckoutSessions` with tenant query filter, active-session uniqueness
  by `(TenantId, CartId, Status)`, recovery token uniqueness, and lifecycle
  indexes (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1585`-`:1620`).
- It is durable PostgreSQL write-side state.
- It stores `CartId`, `RecoveryToken`, `SessionId`, `OnlineCustomerId`, customer
  contact/address fields, cart snapshot JSON, cart total, currency,
  `ShippingMethodId`, payment method, wallet fields, status, activity timestamps,
  invalidation timestamp, and converted order fields
  (`PointOfSale.Core/Entities/CheckoutSession.cs:18`-`:205`).
- It does not store warehouse context.
- It has lifecycle/status: Active, Abandoned, ConvertedToOrder
  (`CheckoutSession.cs:141`, `:197`).
- It survives reload/recovery through signed recovery token and storefront
  session/customer ownership (`CheckoutSessionTokenService.cs:71`,
  `CheckoutSessionController.cs:456`-`:515`).
- `CheckoutSessionController.CreateOrResume` verifies tenant, caller identity,
  and authoritative cart ownership before creating/resuming a session
  (`PointOfSale.API/Controllers/CheckoutSessionController.cs:106`-`:198`).
- `CheckoutSessionController.Update` verifies session ownership by
  `X-Session-Id` or authenticated `OnlineCustomerId`, then verifies the caller's
  authoritative cart matches the session cart (`CheckoutSessionController.cs:224`-`:324`).
- `CheckoutSessionService` uses cart-scoped locks and active-session uniqueness
  to prevent duplicate active sessions (`CheckoutSessionService.cs:31`,
  `:840`-`:1006`).
- Duplicate sessions can be reconciled. Critical data is merged into a canonical
  session, and duplicates are invalidated (`CheckoutSessionService.cs:1131`-`:1492`).
- Final checkout does not currently accept `CheckoutSessionId` in the checkout
  DTO. It uses cart, owner/session header/customer identity, and checkout
  idempotency (`CheckoutService.cs:849`-`:930`).
- Checkout conversion is asynchronous and non-blocking: an `OrderCreatedEvent`
  consumer marks sessions converted by `CartId`
  (`PointOfSale.Infrastructure/Consumers/CheckoutSessionOrderConversionConsumer.cs:10`-`:48`).

Answers:

1. Is `CheckoutSession` tenant-owned? Yes.
2. Is it durable? Yes, PostgreSQL write-side.
3. Does it survive checkout retries? The active session can survive; final
   checkout idempotency is separate and cart-snapshot based.
4. Does it survive page reload? Yes through stored session ID/state and recovery.
5. Is it already authoritative for selected shipping method? It stores
   `ShippingMethodId` for autosave/recovery, but final checkout validates
   `CheckoutRequestDto.ShippingMethodId` independently.
6. Does it already participate in pricing preview? Indirectly through autosaved
   selected method and cart snapshot, but shipping method/pricing APIs do not use
   it as final authority.
7. Does it already participate in final checkout? Not as a required submit
   contract. Conversion is after order commit by cart ID.
8. Does it have lifecycle/status? Yes.
9. Can it safely hold `WarehouseId`? It can hold a reference, but that alone is
   unsafe because duplicate-session merge and invalidation semantics must be
   explicit.
10. Can adding `WarehouseId` create a hidden second authority? Yes, if inventory,
    order, shipment, and a future resolution record also carry warehouse state.
11. Can a session be reused by another cart/customer? Active uniqueness is by
    tenant/cart/status, and create/update verify cart and owner. Mismatched-owner
    sessions are invalidated.
12. How is session ownership enforced? Tenant header, authenticated customer
    tenant validation, `X-Session-Id`, session owner match, and authoritative cart
    owner checks.

Conclusion: `CheckoutSession` is the correct parent/scope for resolution, but
not the complete authority record by itself.

## 4. Option A - CheckoutSession.WarehouseId

Concept:

```text
CheckoutSession
  CartId
  Customer/Session owner
  ShippingMethodId
  WarehouseId
```

Evaluation:

- Simplicity: highest. One nullable FK and a few service changes.
- Transaction safety: acceptable only if final checkout locks and validates the
  active session before reservation/order creation.
- Retry stability: better than current behavior, but only if `WarehouseId` is
  included in final idempotency authority.
- Auditability: weak unless additional fields are added, such as source,
  version, input hash, resolved timestamp, expiry, invalidation reason, and
  locked timestamp. Once those are added, Option A stops being a simple field.
- Lifecycle: inherits Active/Abandoned/ConvertedToOrder but needs its own
  Unresolved/Resolved/Locked/Invalidated state.
- Stale decision handling: weak with just `WarehouseId`.
- Concurrency: must integrate with existing cart-scoped checkout-session locks.
- Idempotency: must extend checkout hash and final validation to include the
  resolved warehouse.
- Observability: weak unless more columns/events are added.
- Tenant isolation: can reuse CheckoutSession tenant and Warehouse same-tenant
  validation.
- Migration cost: lower than Option B.
- Future multi-warehouse support: poor. A scalar `WarehouseId` on session bakes
  the single-warehouse assumption into the recovery aggregate.

Option A is not selected because this task requires answering who decided the
warehouse, by what versioned rule, when it became stale, and how final checkout
locked the decision. A bare `CheckoutSession.WarehouseId` is too implicit.

## 5. Option B - WarehouseResolution Entity

Concept:

```text
WarehouseResolution
  TenantId
  CheckoutSessionId
  CartId
  WarehouseId
  ResolutionSource
  ResolutionVersion
  InputsSnapshotHash
  InputsSnapshotJson
  ResolvedAtUtc
  ExpiresAtUtc
  LockedAtUtc
  InvalidatedAtUtc
  Status
```

Evaluation:

- Auditability: strong. Captures source, version, inputs, timestamps, status,
  and failure/invalidation reasons.
- Replay: strong. Final checkout can require the same active/locked resolution
  and reject stale preview state.
- Retry stability: strong. Same session/cart uses the same active resolution
  until invalidated by defined rules.
- Versioning: strong. Algorithm changes are explicit.
- Concurrency: strong if resolution writes are protected by the same
  tenant/cart/session lock pattern used by checkout sessions.
- Stale decisions: strong. Status and input hashes can detect cart/address/stock
  drift.
- Multi-warehouse future: best. A future `WarehouseResolution` can evolve into
  allocation records without overloading `CheckoutSession`.
- Schema complexity: moderate.
- Operational complexity: moderate but justified by commerce authority risk.
- Tenant isolation: strong with tenant FK/indexes and same-tenant warehouse
  validation.

Option B is selected.

## 6. Option C - Ephemeral Resolution

Concept:

Resolve on every request from cart, inventory, customer address, and carrier
readiness without persisting the decision.

Evaluation:

- Preview vs submit divergence: unacceptable. Shipping methods can be previewed
  for one warehouse and reservation can occur in another.
- Checkout retry: unacceptable. A retry can silently choose a different
  warehouse after stock changes.
- Idempotency: weak unless the ephemeral result is added to a durable hash,
  which makes it non-ephemeral.
- Shipping method stability: weak.
- Concurrency: weak.
- Observability: weak.
- Carrier consistency: weak.

Option C is not acceptable for Shopixy.

## 7. Comparative Decision Matrix

| Criterion | Option A: session field | Option B: entity | Option C: ephemeral |
| --- | --- | --- | --- |
| Minimum implementation size | Best | Medium | Best |
| Authority clarity | Medium | Best | Poor |
| Auditability | Poor unless expanded | Best | Poor |
| Retry stability | Medium | Best | Poor |
| Stale decision handling | Medium | Best | Poor |
| Final checkout locking | Possible | Best | Poor |
| Prevents split-brain | Possible with many rules | Best | No |
| Future multi-warehouse path | Poor | Best | Poor |
| Tenant isolation | Good | Good | Depends per request |
| Business-rule versioning | Poor | Best | Poor |

Decision: Option B.

## 8. Chosen Architecture

Canonical target:

```text
Storefront enters checkout
  -> POST create/resume CheckoutSession
  -> POST shipping-methods preview for CheckoutSession
  -> server resolves one warehouse
  -> server persists WarehouseResolution
  -> server returns eligible shipping methods for that resolution
  -> customer submits checkout with CheckoutSessionId + WarehouseResolutionId/version
  -> final checkout locks cart/session/resolution
  -> final checkout revalidates cart, address, stock, warehouse, and shipping method
  -> reservation uses resolved WarehouseId explicitly
  -> Order.WarehouseId = resolved WarehouseId
  -> every Storefront OrderItem.WarehouseId = resolved WarehouseId
  -> Shipment.OriginWarehouseId derives from Order.WarehouseId
```

Chosen authority: `WarehouseResolution`.

Chosen persistence boundary: PostgreSQL write model, tenant-scoped, linked to
`CheckoutSession` and cart.

Chosen transaction boundary:

- Resolution preview writes under cart/session-scoped lock.
- Final checkout revalidates and locks the active resolution inside the existing
  serializable order transaction before inventory reservation.

Chosen API propagation:

- Storefront never sends a warehouse ID as authority.
- Storefront sends server-owned `CheckoutSessionId` and
  `WarehouseResolutionId`/version.
- API loads and validates `WarehouseResolution.WarehouseId`.

Chosen carrier validation:

- Shipping methods are filtered by the resolved warehouse.
- Final checkout validates the selected external carrier method has a ready
  `CarrierWarehouseAssignment` for the same warehouse.
- Manual fulfillment requires no carrier assignment but still requires the same
  resolved warehouse for inventory/order/shipment authority.

Chosen reservation behavior:

- Storefront reservation must call `ReserveAsync` with explicit
  `resolvedWarehouseId`.
- Null warehouse reservation is forbidden in Storefront checkout.
- If that warehouse cannot fulfill the order, checkout fails. It does not fall
  back to another warehouse.

## 9. Warehouse Resolution Inputs

Candidate evaluation:

| Input | Exists | Currently used for Storefront warehouse selection | Authoritative | Safe to use | Requires new data/API | Business decision |
| --- | --- | --- | --- | --- | --- | --- |
| Cart contents | Yes | No | Yes for checkout snapshot | Yes | No | No |
| Variant availability | Yes | Aggregate only | Yes from `ProductVariantStocks` | Yes | Needs per-warehouse resolver query | No |
| Warehouse stock | Yes | Reservation uses it | Yes from `ProductVariantStocks` | Yes | Resolver query needed | No |
| Customer shipping address | Yes | No | Yes as checkout destination | Safe as input hash | No | Yes to route by it |
| City | Yes | No | Destination only | Safe as input hash | No | Yes |
| Area | Yes | No | Destination only | Safe as input hash | No | Yes |
| Postal code | Yes | No | Destination only | Safe as input hash | No | Yes |
| Shipping zones | Geographic zone snapshots exist | No | Address admission authority | Safe as input hash | Possibly no | Yes |
| Warehouse service area | No source authority found | No | No | No | Yes | Yes |
| Warehouse active state | Yes | Used by shipping filter/cart stock checks | Yes | Yes | No | No |
| Carrier warehouse readiness | Yes | Shipping method filter only | Eligibility authority | Yes after warehouse resolution | No | No |
| Shipping method | Yes | Not warehouse selection | Shipping selection authority | Safe as constraint after resolution | No | No for selection; yes for ordering if changed |
| Carrier account | Yes | Not warehouse selection | Carrier readiness authority | Safe as eligibility after resolution | No | No |
| Warehouse priority | No source authority found | No | No | No | Yes | Yes |
| Default warehouse | `Warehouse.IsDefault` exists | Not used for Storefront routing | Weak without policy | Safe only if policy adopted | Maybe no | Yes |
| Fulfillment mode | Yes | Derived from shipping method | Order ownership authority | Not a warehouse selector | No | No |
| Merchant configuration | No routing config found | No | No | No | Yes | Yes |

Source-supported inputs for Step 2F:

- tenant
- checkout session/cart ownership
- cart variant IDs and quantities
- active same-tenant warehouses
- per-warehouse `ProductVariantStock.AvailableQuantity`
- warehouse active/deleted state
- optional address fingerprint for invalidation
- carrier assignment readiness after resolution

Missing business rule:

Which warehouse wins when multiple active warehouses can fulfill all quantities?

## 10. Resolution Algorithm

The algorithm must not use repository lock order as business policy.

Required algorithm skeleton:

1. Load active same-tenant checkout session for caller/cart.
2. Load authoritative cart snapshot.
3. Normalize cart into required variant quantities.
4. Exclude non-stock-tracked or backorderable variants from stock requirement,
   using the same product/variant authority as checkout reservation.
5. Load active, non-deleted tenant warehouses.
6. Load per-warehouse available quantities from current `ProductVariantStocks`.
7. Build candidate warehouses that can fulfill every stock-tracked variant in
   the cart.
8. If no candidate exists, fail: "No single warehouse can fulfill this order."
9. If exactly one candidate exists, resolve that warehouse.
10. If multiple candidates exist, apply the configured business rule.

Current source does not define the configured business rule.

Therefore Step 2F must stop at one of these explicit choices:

- Business adopts `Warehouse.IsDefault` as winner if it can fulfill the cart,
  with failure/secondary policy defined when default cannot fulfill.
- Business adds merchant-defined warehouse priority.
- Business adds destination/service-area routing.
- Business allows explicit customer selection.
- Business chooses "single candidate only; multiple candidates require merchant
  configuration before checkout can proceed."

Recommended minimum for correctness without inventing policy:

- Implement the resolver so it succeeds when exactly one warehouse can fulfill
  the cart.
- If multiple warehouses can fulfill the cart and no configured policy exists,
  return `WAREHOUSE_SELECTION_POLICY_REQUIRED`.
- Before production rollout, choose a business policy. The likely lowest-schema
  policy is `Warehouse.IsDefault` plus deterministic tie validation, but source
  does not currently certify it as a checkout routing rule.

## 11. Single-Warehouse Enforcement

The current Storefront order/shipment model is single-origin.

Resolution must guarantee:

```text
For every stock-tracked cart line:
  available quantity at resolved warehouse >= requested quantity
```

Detection point:

- During warehouse resolution preview.
- Again during final checkout inside the serializable order transaction.

Future reservation behavior:

```text
ReserveAsync(tenantId, variantId, resolvedWarehouseId, quantity, orderId, ...)
```

If the resolved warehouse lacks stock at final validation time:

- Do not reserve from another warehouse.
- Invalidate or expire the resolution.
- Return a conflict/out-of-stock response.
- Require re-resolution and shipping-method refresh.

Repository behavior requirement:

- Storefront checkout must never call `ReserveAsync` with null warehouse.
- Repository may keep null behavior for non-Storefront legacy flows only if those
  flows are explicitly outside Storefront single-origin authority.
- Step 2F should add a Storefront guard or overload that makes null warehouse
  impossible at compile/runtime boundary.

## 12. Shipping Method Integration

Ordering must be:

```text
Warehouse Resolution
  -> Shipping Method Eligibility
  -> Customer Shipping Method Selection
```

This is valid for Shopixy because current shipping method warehouse filtering
already accepts a warehouse and filters through `CarrierWarehouseAssignment`
(`PointOfSale.API/Services/Storefront/ShippingMethodService.cs:183`-`:250`).

Target preview behavior:

- Load or create active `WarehouseResolution`.
- Use `WarehouseResolution.WarehouseId` for shipping-method eligibility.
- Return only methods compatible with that warehouse.
- Return resolution metadata (`warehouseResolutionId`, `version`, `status`,
  `expiresAtUtc`) but not treat client-returned warehouse ID as authoritative.

Final checkout behavior:

- Re-load `WarehouseResolution`.
- Revalidate selected shipping method against the same `WarehouseId`.
- Reject mismatches before reservation.

## 13. CarrierWarehouseAssignment Integration

For external carriers:

```text
resolvedWarehouse
  + selected ShippingMethod.CarrierCode
  -> enabled origin-ready CarrierWarehouseAssignment
  -> active ready CarrierAccount
```

Final checkout must verify:

- `CarrierWarehouseAssignment.TenantId == tenantId`
- `CarrierAccount.TenantId == tenantId`
- `assignment.WarehouseId == resolvedWarehouseId`
- `assignment.IsEnabled`
- `assignment.OriginReadinessPublished`
- assignment/account not deleted
- `account.State == Active`
- `account.OperationalState == Ready`
- normalized account carrier code equals selected shipping method carrier code

This is the same eligibility shape already used by
`ShippingMethodService.ApplyWarehouseAssignmentFilterAsync`.

CarrierWarehouseAssignment constrains carrier eligibility. It does not choose the
warehouse.

## 14. Manual Fulfillment Integration

Manual fulfillment remains `carrierCode = manual` from Step 2B.

Manual fulfillment should not require:

- `CarrierAccount`
- `CarrierWarehouseAssignment`
- external credentials
- connection test
- external origin readiness

Manual fulfillment still must require a resolved warehouse because:

- inventory reservation is warehouse-scoped;
- `Order.WarehouseId` is the single warehouse authority;
- `OrderItem.WarehouseId` must match the order warehouse in the single-warehouse
  model;
- shipment origin derives from `Order.WarehouseId` if a shipment is created.

Therefore manual shipping method eligibility should be allowed for any active
resolved warehouse that can fulfill the cart. It should not be excluded merely
because no carrier assignment exists.

Note: current warehouse-filtered shipping method service excludes manual when
`warehouseId` is supplied. Step 2F must deliberately change that behavior for
the new preview path or add a dedicated preview service that includes manual
methods after warehouse resolution.

## 15. Checkout Session Lifecycle

Target warehouse resolution lifecycle:

```text
UNRESOLVED
  -> RESOLVED
  -> LOCKED
  -> ORDERED

RESOLVED
  -> INVALIDATED
  -> RESOLVED

RESOLVED
  -> EXPIRED
```

Recommended columns:

- `ResolvedAtUtc`
- `ExpiresAtUtc`
- `LockedAtUtc`
- `InvalidatedAtUtc`
- `InvalidationReason`
- `ConvertedOrderId`
- `ResolutionVersion`
- `InputsSnapshotHash`

Assignment time:

- On shipping-method preview after checkout session exists and cart can be
  authoritatively loaded.

Mutability:

- `WarehouseResolution.WarehouseId` is immutable while `Status` is `Resolved`,
  `Locked`, or `Ordered`.
- Re-resolution creates a new row/version and invalidates the previous active
  row.
- Do not update the warehouse in-place after shipping methods have been previewed.

Re-resolution triggers:

- cart contents/quantity changes;
- address fingerprint changes if business policy uses address, or as a
  conservative invalidation until policy is chosen;
- warehouse becomes inactive/deleted;
- resolved warehouse cannot fulfill required quantities;
- selected carrier assignment becomes unavailable;
- resolution expires.

## 16. Idempotency

Existing checkout idempotency hashes:

- cart items;
- discounts;
- shipping method;
- payment method;
- address fingerprint;
- cart updated timestamp.

Target:

- Include `WarehouseResolutionId`, `WarehouseId`, and `ResolutionVersion` in the
  server-side canonical checkout idempotency snapshot.
- A retry for the same checkout must load the same active/locked resolution.
- If the active resolution is invalidated, retry must fail with a conflict that
  instructs the client to refresh/re-resolve. It must not silently choose another
  warehouse.
- Do not invent a new checkout idempotency mechanism. Extend the existing
  snapshot builder and idempotency coordinator inputs.

Retry rule:

```text
Same cart + same checkout session + same resolution version -> same warehouse.
Different resolution version -> not an idempotent replay; require refresh.
```

## 17. Inventory Reservation

Current:

```csharp
ReserveAsync(tenantId, variantId, null, quantity, orderId, ...)
```

Future:

```csharp
ReserveAsync(tenantId, variantId, resolvedWarehouseId, quantity, orderId, ...)
```

Semantic change:

- Availability check becomes per resolved warehouse, not aggregate across all
  warehouses.
- Reservation cannot split across warehouses.
- Failure at the resolved warehouse is checkout failure, not fallback.

Required final validation:

- Before reservation, re-query `ProductVariantStocks` for the resolved warehouse
  under the final transaction/lock.
- Ensure every stock-tracked variant has enough available quantity at that
  warehouse.
- Then call reservation with explicit warehouse.

## 18. Order Propagation

Target propagation:

```text
WarehouseResolution.WarehouseId
  -> Order.WarehouseId
  -> every Storefront OrderItem.WarehouseId
```

Rules:

- `Order.WarehouseId` should be mandatory for Storefront orders.
- `OrderItem.WarehouseId` should equal `Order.WarehouseId` for every Storefront
  item in the single-warehouse model.
- Non-Storefront/admin/manual legacy flows can be handled separately, but
  Storefront checkout must fail if no resolved warehouse exists.
- Current EF immutability guards already protect `Order.WarehouseId` and
  `OrderItem.WarehouseId` after creation
  (`ApplicationDbContext.cs:3870`, `:3872`-`:3879`).

## 19. Shipment Origin

Current source derives shipment origin from `Order.WarehouseId` during carrier
registration:

- if stored origin snapshot exists, reuse it;
- otherwise require `order.WarehouseId`;
- load active same-tenant warehouse;
- require admitted warehouse address evidence;
- write `Shipment.OriginWarehouseId` and origin snapshot.

Target architecture should preserve:

```text
Order.WarehouseId -> Shipment.OriginWarehouseId
```

Do not create another warehouse resolver at shipment time.

Shipment layer rule:

- Shipment creation/registration may validate the order warehouse and origin
  evidence.
- Shipment layer must never select a different warehouse.

This prevents:

```text
Order = Alexandria
Shipment origin = Cairo
```

## 20. Concurrency

Case A: warehouse stock disappears after resolution.

- Expected result: final checkout fails with insufficient stock at resolved
  warehouse.
- Validation point: final serializable checkout transaction before reservation.
- Transaction boundary: lock session/resolution and stock rows.
- Resolution validity: invalidated or expired.
- Re-resolution: required.

Case B: warehouse becomes inactive after resolution.

- Expected result: final checkout fails.
- Validation point: shipping preview refresh and final checkout.
- Transaction boundary: final resolution validation.
- Resolution validity: invalidated.
- Re-resolution: required.

Case C: carrier assignment becomes not-ready after resolution.

- Expected result: selected external method rejected; manual may still be
  available if configured.
- Validation point: shipping-method preview and final checkout.
- Transaction boundary: final validation before reservation.
- Resolution validity: warehouse may remain valid, but selected shipping method
  becomes invalid.
- Re-resolution: not necessarily; shipping method refresh required.

Case D: customer changes address.

- Expected result: invalidate resolution if address participates in the chosen
  policy or conservatively until policy is finalized.
- Validation point: checkout-session update and final idempotency address hash.
- Transaction boundary: session update lock.
- Resolution validity: invalidated if input hash changes.
- Re-resolution: required before shipping methods.

Case E: cart changes quantity.

- Expected result: invalidate resolution.
- Validation point: create/resume, autosave cart snapshot refresh, shipping
  preview, final checkout hash.
- Transaction boundary: cart/session lock.
- Resolution validity: invalidated.
- Re-resolution: required.

Case F: two checkout attempts use same session.

- Expected result: one active/locked resolution. Competing attempts either replay
  same resolution or receive in-progress/conflict.
- Validation point: preview and final checkout locks.
- Transaction boundary: cart/session lock plus final checkout idempotency.
- Resolution validity: unchanged unless inputs changed.
- Re-resolution: no silent re-resolution during retry.

Case G: two customers attempt last stock.

- Expected result: first final reservation wins; second fails at resolved
  warehouse.
- Validation point: final stock lock/reservation.
- Transaction boundary: `ProductVariantStocks` row locks.
- Resolution validity: second resolution invalid due stock.
- Re-resolution: allowed only after refresh; may still fail.

## 21. Invalidation Rules

Invalidate active resolution when:

- cart item set changes;
- cart quantity changes;
- cart price/variant authority refresh changes the checkout snapshot in a way
  included in idempotency;
- resolved warehouse becomes inactive/deleted;
- resolved warehouse can no longer fulfill every stock-tracked variant;
- resolution expires;
- selected shipping method is external and no matching ready assignment exists
  at final validation;
- session is abandoned/invalidated/converted;
- caller/cart ownership no longer matches session;
- address hash changes, unless the chosen business policy explicitly declares
  address irrelevant.

Do not mutate an existing resolved row to a new warehouse. Create a new
resolution row/version.

## 22. Tenant Security

Reuse existing mechanisms:

- tenant admission from Storefront request headers;
- authenticated customer tenant validation;
- `X-Session-Id` guest ownership;
- authoritative cart load by tenant + owner;
- checkout-session tenant query filter and owner checks;
- same-tenant warehouse lookup;
- same-tenant `ProductVariantStocks` queries;
- same-tenant `CarrierWarehouseAssignment` and `CarrierAccount` joins.

Security rules:

- Client never submits authoritative `WarehouseId`.
- Any client-supplied `CheckoutSessionId` or `WarehouseResolutionId` is an
  opaque reference only.
- API must verify resolution belongs to tenant, checkout session, cart, and
  caller owner.
- Warehouse FK must be validated against same tenant and active state.
- Cross-tenant warehouse IDs must fail closed.

## 23. Observability

Use existing structured logging and reliability telemetry patterns.

Safe fields:

- tenant ID;
- checkout session ID;
- cart ID;
- warehouse resolution ID;
- warehouse ID;
- resolution source;
- resolution version;
- status;
- success/failure;
- failure reason code;
- invalidation reason;
- duration;
- correlation ID.

Do not log:

- customer name;
- phone/email;
- full address;
- cart contents;
- recovery/payment/manual-wallet tokens;
- credentials;
- object keys;
- carrier secrets;
- input snapshot JSON if it contains PII.

Recommended events/metrics:

- `WarehouseResolutionCreated`
- `WarehouseResolutionInvalidated`
- `WarehouseResolutionLocked`
- `WarehouseResolutionFailed`
- `WarehouseResolutionPolicyRequired`
- `WarehouseResolutionCarrierMismatch`
- `WarehouseResolutionStockConflict`

## 24. Failure Semantics

Canonical errors:

| Error | HTTP | Meaning |
| --- | --- | --- |
| `NO_ELIGIBLE_WAREHOUSE` | 422 | No active same-tenant warehouse can fulfill all stock-tracked cart lines. |
| `WAREHOUSE_INACTIVE` | 409 | Previously resolved warehouse is inactive/deleted at validation. |
| `INSUFFICIENT_STOCK_AT_RESOLVED_WAREHOUSE` | 409 | Resolved warehouse no longer has enough stock. |
| `WAREHOUSE_CHANGED` | 409 | Submitted resolution does not match active server resolution. |
| `CARRIER_ASSIGNMENT_UNAVAILABLE` | 409 | External carrier method lacks ready assignment for resolved warehouse. |
| `SHIPPING_METHOD_INCOMPATIBLE_WITH_WAREHOUSE` | 422 | Selected method is not eligible for resolved warehouse. |
| `CHECKOUT_SESSION_STALE` | 409 | Session is not active/canonical for caller/cart. |
| `WAREHOUSE_RESOLUTION_EXPIRED` | 409 | Resolution expired before submit. |
| `CART_CHANGED_RE_RESOLUTION_REQUIRED` | 409 | Cart snapshot/hash no longer matches resolution input. |
| `TENANT_MISMATCH` | 404 or 403 | Keep existing fail-closed/access-denied behavior; do not reveal foreign IDs. |
| `WAREHOUSE_SELECTION_POLICY_REQUIRED` | 422 | Multiple warehouses can fulfill but no business winner rule is configured. |
| `WAREHOUSE_RESOLUTION_SERVICE_UNAVAILABLE` | 503 | Infrastructure failure, not business validation. |

Avoid generic 500s for all expected resolution failures.

## 25. Database Design

Chosen: new `WarehouseResolution` table.

Proposed schema:

```text
WarehouseResolutions
  Id uuid PK
  TenantId uuid not null
  CheckoutSessionId uuid not null
  CartId uuid not null
  WarehouseId uuid null
  Status int/string not null
  ResolutionSource string not null
  ResolutionVersion int not null
  AlgorithmCode string not null
  InputsSnapshotHash string not null
  InputsSnapshotJson text/jsonb null
  FailureCode string null
  InvalidationReason string null
  ResolvedAtUtc timestamp null
  ExpiresAtUtc timestamp null
  LockedAtUtc timestamp null
  InvalidatedAtUtc timestamp null
  ConvertedOrderId uuid null
  CreatedAtUtc timestamp not null
  UpdatedAtUtc timestamp null
```

Recommended status values:

- `Unresolved`
- `Resolved`
- `Locked`
- `Ordered`
- `Invalidated`
- `Expired`
- `Failed`

Foreign keys:

- `(TenantId, CheckoutSessionId)` logical ownership; EF may use simple FK to
  `CheckoutSessions.Id` plus explicit tenant validation.
- `WarehouseId -> Warehouses.Id`.
- `ConvertedOrderId -> Orders.Id` nullable.

Indexes:

- `(TenantId, CheckoutSessionId, Status)`
- unique active filter for one active resolution per tenant/session:
  `Status in (Resolved, Locked)`
- `(TenantId, CartId, Status)`
- `(TenantId, WarehouseId, Status)`
- `(TenantId, ExpiresAtUtc, Status)`
- `InputsSnapshotHash`

Retention:

- Keep through checkout/order audit retention.
- Expired/failed rows can be retained with no PII in snapshots or with redacted
  snapshots.

Migration required: yes.

## 26. API Contract Design

Do not use:

```http
GET /api/storefront/shipping-methods?warehouseId=...
```

as the Storefront authority path. It trusts a client-provided warehouse ID unless
wrapped by server-side session validation.

Chosen preview API:

```http
POST /api/storefront/shipping-methods/preview
```

Request:

```json
{
  "checkoutSessionId": "server-session-guid",
  "addressFingerprint": "optional-client-observed-version"
}
```

The server must load authoritative cart/session/address state; client address
data is not authority.

Response:

```json
{
  "warehouseResolutionId": "server-resolution-guid",
  "resolutionVersion": 1,
  "status": "RESOLVED",
  "expiresAtUtc": "2026-08-11T...",
  "methods": []
}
```

Final checkout request should add:

```json
{
  "checkoutSessionId": "server-session-guid",
  "warehouseResolutionId": "server-resolution-guid",
  "warehouseResolutionVersion": 1
}
```

No `warehouseId` should be accepted from Storefront checkout as authority.

Existing `GET /api/storefront/shipping-methods` can remain for legacy/unresolved
read behavior, but Step 2F target Storefront checkout should move to the preview
contract.

## 27. Storefront Contract

Storefront responsibilities:

- create/resume checkout session on checkout page entry;
- call shipping-method preview with `checkoutSessionId`;
- store returned `warehouseResolutionId`, version, status, and expiry;
- display eligible methods returned by server;
- autosave selected shipping method as today;
- submit checkout with checkout session and resolution references;
- refresh preview if resolution expires, cart changes, address changes, or
  selected method becomes stale.

Storefront must not:

- choose warehouse;
- submit authoritative `warehouseId`;
- cache eligible shipping methods across resolution versions;
- continue checkout after server reports stale/expired resolution.

## 28. Test Strategy

Warehouse Resolution:

- deterministic single-candidate selection;
- tenant isolation;
- inactive warehouse excluded;
- deleted warehouse excluded;
- insufficient stock;
- single-warehouse enforcement;
- no fallback warehouse;
- multiple candidates without policy returns business-decision error;
- resolution invalidates on cart change;
- resolution invalidates on expiry.

Shipping:

- resolved warehouse filters carrier assignments;
- manual fulfillment remains available without assignment;
- external carrier requires ready assignment;
- shipping method mismatch fails final checkout;
- warehouse mismatch fails final checkout.

Checkout:

- same warehouse across preview/submit;
- retry stability;
- cart change invalidation;
- address change invalidation according to policy;
- shipping method change revalidated;
- final transaction locks active resolution.

Reservation:

- explicit resolved warehouse passed to reservation;
- null warehouse forbidden for Storefront checkout;
- no fallback;
- concurrency: two users last stock.

Order:

- `Order.WarehouseId` set;
- `OrderItem.WarehouseId` set for all Storefront items;
- immutability guards remain effective.

Shipment:

- `Shipment.OriginWarehouseId` derives from `Order.WarehouseId`;
- shipment layer does not resolve warehouse independently.

Idempotency:

- same session/retry returns same warehouse;
- duplicate checkout cannot create conflicting warehouse authority;
- resolution version participates in idempotency.

## 29. Performance

Expected preview query shape:

- one authoritative cart load;
- one active checkout-session lookup;
- one active resolution lookup;
- one warehouse candidate query;
- one per-warehouse stock availability query grouped by warehouse/variant;
- one shipping method read;
- one carrier assignment join for external methods.

Expected final checkout impact:

- one resolution lookup/lock;
- one warehouse active-state validation;
- one per-resolved-warehouse stock validation;
- one carrier assignment validation for external method;
- existing reservation/order transaction continues.

N+1 risks:

- per-cart-line stock queries;
- per-method carrier assignment checks;
- per-warehouse readiness checks;
- repeated session canonicalization outside lock.

Avoid by:

- grouping required quantities by variant;
- loading stock availability for all required variants in one grouped query;
- loading ready carrier codes for resolved warehouse in one join;
- caching read-only shipping method projection as current code does, while
  keeping resolution and carrier readiness SQL-authoritative.

Do not optimize prematurely. Correctness is the Step 2F priority.

## 30. Future Multi-Warehouse Compatibility

Current:

```text
Checkout -> one Warehouse -> one Order origin
```

Future:

```text
Order
  -> OrderLineAllocation
       Warehouse A
       Warehouse B
  -> Multiple Shipments
```

Stable abstraction:

- `WarehouseResolution` should represent the resolution attempt and algorithm.
- In future, it can have child `WarehouseResolutionAllocation` rows.
- `Order.WarehouseId` can remain the single-origin shortcut for current
  Storefront orders.
- Do not make `CheckoutSession.WarehouseId` the only durable design; it would
  become awkward when one checkout has multiple warehouse allocations.

## 31. Security Review

P0 risks prevented by design:

- tenant warehouse spoofing by client-submitted warehouse ID;
- cross-tenant inventory reservation;
- carrier account substitution across tenants;
- order/reservation/shipment warehouse split-brain causing wrong origin.

P1 risks addressed:

- stale resolution after stock changes;
- carrier assignment removed after preview;
- duplicate checkout attempts racing resolution/order creation;
- final checkout choosing different warehouse than preview.

P2 risks:

- UX stale method states after resolution expiry;
- admin/read DTOs not exposing resolution/order warehouse audit fields.

P3 risks:

- incomplete observability;
- stale documentation/tests.

Required security controls:

- validate tenant header as existing Storefront APIs do;
- validate authenticated customer tenant;
- validate `X-Session-Id` for guests;
- validate cart ownership;
- validate resolution belongs to session/cart/tenant/owner;
- validate warehouse same tenant/active;
- validate carrier assignment same tenant/warehouse/carrier;
- fail closed without revealing foreign resource existence.

## 32. Decision Gates

GATE 1: Do we need a new `WarehouseResolution` entity?

Yes. The decision needs audit/version/status/invalidation/lock semantics beyond
a scalar checkout-session field.

GATE 2: Is `CheckoutSession` enough?

No as the sole authority. It is the correct parent scope, but the authority
should be a child resolution record.

GATE 3: How is the warehouse selected?

Technically: choose an active same-tenant warehouse that can fulfill all
stock-tracked cart quantities from `ProductVariantStocks`.

Business decision required: when more than one warehouse can fulfill the cart,
source does not define the winner rule.

GATE 4: Can one warehouse fulfill the entire order?

The resolver must prove this before shipping methods and again before
reservation. If not, fail with "No single warehouse can fulfill this order."

GATE 5: Does customer address affect selection?

Not currently. Address can be an input hash and future routing input, but using
it to choose warehouse is a business decision.

GATE 6: Does `ShippingMethod` affect selection?

No. Warehouse resolution precedes shipping method selection. Shipping method
constrains carrier eligibility after warehouse resolution.

GATE 7: Does `CarrierWarehouseAssignment` constrain selection?

It constrains external carrier eligibility for a resolved warehouse. It does not
choose the warehouse.

GATE 8: When is the decision locked?

At final checkout, inside the serializable order transaction before inventory
reservation and order persistence.

GATE 9: How does retry preserve the decision?

Retry references the same checkout session and warehouse resolution/version.
Resolution participates in the checkout idempotency snapshot. Stale/invalidated
resolution conflicts; no silent re-resolution.

GATE 10: What exact data crosses Storefront -> API -> CheckoutSession ->
Reservation -> Order -> Shipment?

- Storefront -> API preview: `checkoutSessionId`.
- API -> Storefront preview response: `warehouseResolutionId`, version, expiry,
  eligible methods.
- Storefront -> API checkout: `checkoutSessionId`, `warehouseResolutionId`,
  version, selected `shippingMethodId`.
- API -> `WarehouseResolution`: server-owned `WarehouseId`.
- API -> reservation: explicit `WarehouseId`.
- API -> order: `Order.WarehouseId` and `OrderItem.WarehouseId`.
- Order -> shipment: shipment origin derives from `Order.WarehouseId`.

GATE 11: Is a migration required?

Yes, for `WarehouseResolution`. Additional columns may be needed on
`CheckoutSession` only for denormalized current resolution pointer if desired,
but not as the primary authority.

GATE 12: What becomes Step 2F implementation?

Implement the `WarehouseResolution` authority, preview API, Storefront contract,
final checkout validation/propagation, explicit warehouse reservation, carrier
assignment final validation, and tests.

## 33. Step 2F Implementation Plan

1. Add `WarehouseResolution` domain entity, EF configuration, migration, and
   status/source enums.
2. Add warehouse resolution service with single-warehouse candidate detection.
3. Add a temporary failure for multiple candidates until business winner policy
   is selected, or implement the selected policy if decided before Step 2F.
4. Add shipping methods preview API that resolves/loads active resolution and
   filters methods using server-owned warehouse.
5. Adjust Storefront checkout store to call preview with checkout session ID and
   carry resolution ID/version into submit.
6. Extend checkout DTO and idempotency snapshot with resolution references.
7. In final checkout, lock/validate resolution before reservation.
8. Validate external carrier method against `CarrierWarehouseAssignment` for the
   resolved warehouse.
9. Include manual fulfillment after warehouse resolution without requiring
   carrier assignment.
10. Call `ReserveAsync` with explicit resolved warehouse.
11. Set `Order.WarehouseId`.
12. Set every Storefront `OrderItem.WarehouseId` to the same value.
13. Preserve shipment origin derivation from `Order.WarehouseId`.
14. Add regression tests listed in this report.

## 34. Migration Requirement

Migration required: yes.

Minimum migration:

- create `WarehouseResolutions`;
- add FK/indexes;
- add filtered unique index for active resolution per tenant/session;
- no backfill needed for historical orders as part of Step 2F unless reporting
  requires it.

Optional migration:

- add `CurrentWarehouseResolutionId` to `CheckoutSession` as a convenience
  pointer. This is not the authority; the `WarehouseResolution` row remains the
  authority.

Do not make `Order.WarehouseId` globally non-null until legacy/admin data policy
is separately decided. Instead, enforce non-null for Storefront checkout service
path.

## 35. Final Recommendation

Use a dedicated persisted `WarehouseResolution` authority scoped to
`CheckoutSession` and cart.

Do not accept Storefront `warehouseId` as authority.

Do not use inventory repository null-warehouse allocation for Storefront
checkout.

Do not let shipping method choose warehouse.

Do not let shipment choose warehouse.

The only allowed warehouse flow for Storefront single-origin checkout should be:

```text
WarehouseResolution.WarehouseId
  -> Shipping method eligibility
  -> ReserveAsync(... WarehouseId ...)
  -> Order.WarehouseId
  -> OrderItem.WarehouseId
  -> Shipment.OriginWarehouseId
```

Business decision required before production implementation:

Choose the winner rule when multiple active warehouses can fulfill the whole
cart. Until that policy is chosen, Step 2F should either fail with
`WAREHOUSE_SELECTION_POLICY_REQUIRED` or implement only the exact-single-candidate
case.

## 36. Final Certification

DESIGN READY WITH BUSINESS DECISION REQUIRED

The architecture, persistence boundary, propagation path, transaction boundary,
security model, and test strategy are clear.

The unresolved business rule is warehouse winner selection among multiple valid
candidate warehouses. Source evidence does not establish a legitimate current
rule for that case.

