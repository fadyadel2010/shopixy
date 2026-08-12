# SHOPIXY Shipping Methods — Final Forensic Certification Audit

**Audit date:** 2026-08-12 (requested artifact name retained as `2026-08-11`)  
**Repository:** `PointOfSale`  
**Audit type:** source-level and local verification, audit-only  
**Final certification:** **BLOCKED**  
**Can Shipping Methods be closed?** **NO**

## 1. Executive Summary

The current implementation has a strong server-authoritative warehouse chain and materially improved carrier integration authority, but it cannot be certified or closed.

Two confirmed P0 defects and five P1 blockers remain:

1. **P0 — shipping money divergence:** shipping-method preview includes `PerKgRate * totalWeight`, while the canonical pricing service used by pricing preview, final checkout, and order persistence omits total weight and therefore prices with the default weight of zero.
2. **P0 — webhook credential security/configuration:** carrier webhook secrets are modeled and consumed as plaintext, are not part of the encrypted canonical update contract, and have no canonical admin persistence path.
3. **P1 — stale carrier verification:** credential/configuration changes do not invalidate prior successful connection evidence, so new credentials can inherit a previous `LastValidationSucceeded = true` state.
4. **P1 — service-level authority is incomplete:** runtime admission does not revalidate the stored service level or enforce `CarrierServiceLevelEnablement`; disabled, stale, or directly persisted arbitrary levels can reach checkout and shipment.
5. **P1 — post-order shipment depends on mutable method state:** shipment creation reloads the current shipping method and its current carrier/service level and requires it still be active, rather than consuming an immutable checkout/order snapshot.
6. **P1 — unsafe production upgrade behavior:** the fulfillment-warehouse migration adds a nullable column without preflight, backfill, or deactivation of existing active methods. Existing active methods can remain active in the database while silently disappearing from storefront eligibility.
7. **P1 — critical verification gate is red:** the focused .NET shipping suite failed 9 of 37 tests, including external-shipment retry/reconciliation and webhook trust cases. The full .NET run did not complete within the 60-second audit window.

Positive evidence: tenant identity is derived server-side; browser warehouse identity is not accepted by preview, selection, or final checkout; `WarehouseResolution` binds cart, session, method, warehouse, stock inputs, version, and expiry; final checkout revalidates and locks the resolution in a serializable transaction; inventory reservation receives the resolved warehouse; and `Order.WarehouseId` plus every `OrderItem.WarehouseId` are stamped from that resolution.

This is not a case where only live carrier credentials are missing. Known internal P0/P1 defects exist, so `PASS WITH EXTERNAL VERIFICATION BLOCKED` is not available.

## 2. Audit Scope

The audit traced all 50 requested areas across:

- `CarrierIntegrationAuthority`, `CarrierCapabilityRegistry`, `CarrierConfiguration`, credential encryption, validation/readiness, `CarrierAccount`, diagnostic test records, and legacy `CarrierWarehouseAssignment`.
- Manual, Bosta, Aramex, DHL Express, FedEx, and Mylerz adapters and capability declarations.
- Shipping-method entity, admin CRUD, price fields, delivery estimates, service level, carrier identity, tracking URL, activity, tenant and warehouse validation.
- Storefront cart hydration, checkout session, preview, selection, `WarehouseResolution`, stale-state detection, final checkout, idempotency, stock reservation, order propagation, and shipment runtime.
- Legacy GET/calculate/quote paths, browser/API boundaries, UI/API contracts, schema/migrations, and tests.

No source, test, configuration, frontend, database, or migration was changed. This report is the only intentional repository mutation.

## 3. Current Canonical Architecture

The intended architecture is present in the primary path:

```text
external carrier
  -> CarrierCapabilityRegistry
  -> CarrierIntegrationAuthority
  -> CarrierConfiguration (encrypted API key/secret/config JSON)
  -> persisted validation/readiness
  -> ShippingMethod eligibility

ShippingMethod.FulfillmentWarehouseId
  -> server-created WarehouseResolution
  -> final checkout revalidation + lock
  -> inventory reservation at resolved warehouse
  -> Order.WarehouseId
  -> OrderItem.WarehouseId
  -> Shipment.OriginWarehouseId
```

The single-runtime-authority intent is explicit in `PointOfSale.Infrastructure/Shipping/CarrierIntegrationAuthority.cs`. Storefront admission and shipment credential resolution use that authority. `CarrierAccount` and `CarrierConnectionTestRecord` are administration/diagnostic facades, not the checkout credential store. `CarrierWarehouseAssignment` is consulted only by capability-driven origin-readiness logic; all six current capability descriptors set `RequiresOriginReadiness: false`, so it is not shipping-method warehouse authority.

The architecture is nevertheless not coherent at every consumer: readiness evidence is not version-bound to credentials, service-level validation stops at admin CRUD, and shipment creation reopens mutable shipping-method configuration after order creation.

## 4. Complete End-to-End Lifecycle

1. Admin carrier configuration enters `AdminCarrierConfigController` and is stored through `CarrierIntegrationAuthority.ValidateAndStoreIntegrationAsync`.
2. API key, API secret, and carrier-specific configuration JSON are encrypted before `CarrierConfiguration` persistence (`CarrierIntegrationAuthority.cs:335-425`).
3. Provider validation is run separately by `ValidateResolvedCredentialsAsync`; runtime readiness later checks `LastValidationSucceeded` (`CarrierIntegrationAuthority.cs:601-609`).
4. Admin shipping-method CRUD validates canonical `carrierCode`, adapter service levels, tenant-owned active warehouse, pricing fields, delivery days, tracking template, and activity (`ShippingMethodsController.cs`).
5. Storefront preview accepts `checkoutSessionId`, loads the owned cart/session, calculates cart weight, filters active SQL methods by active configured warehouse, exact cart stock, and carrier readiness (`StorefrontShippingMethodsController`, `ShippingMethodService`).
6. Selection accepts only session and method IDs. The server reloads the method, derives `FulfillmentWarehouseId`, verifies warehouse/readiness/stock, and persists a versioned `WarehouseResolution` (`WarehouseResolutionService.cs`).
7. Final checkout requires session, resolution ID/version, and method ID; recalculates pricing server-side; revalidates method, payment, cart, stock, readiness, and resolution inside a serializable transaction; and rejects changed inputs.
8. Inventory is reserved with the resolution warehouse. The order and all items receive the same warehouse. Resolution conversion, idempotency/outbox records, and order/cart state are persisted transactionally.
9. Shipment creation derives origin from `Order.WarehouseId` and uses canonical carrier credentials, but incorrectly reloads mutable shipping-method carrier/service/activity rather than a durable order snapshot.

The lifecycle therefore proves the warehouse authority chain but fails money, credential-state, service-level, shipment immutability, migration, and test gates.

## 5. Carrier Integration Findings

`CarrierCapabilityRegistry.cs:83-203` is the canonical capability inventory.

- **Manual:** `ProductionReady`; no credentials, environment, connection test, origin assignment, geography mapping, or webhook requirement.
- **Bosta:** `Partial`; encrypted API token; remote validation; create/cancel/tracking implemented; geography mapping and shared-secret webhook policy declared.
- **Aramex:** `Partial`; username/password/account/PIN/entity/country requirements; remote validation; create/tracking implemented; cancellation explicitly unsupported.
- **DHL:** `Partial`; API key/secret/account; connection test is `LocalOnly`, not provider authentication; create/tracking implemented; cancellation unsupported.
- **FedEx:** `NotCertified`; no credential schema, validation, service levels, or admitted runtime.
- **Mylerz:** `Unavailable`; `OfficialPublicContractUnavailable() => true` fail-closes validation/create/cancel/tracking and advertises no service levels.

Carrier code, not free-text carrier name, drives runtime. Admin mapping derives display name from adapter metadata (`ShippingMethodsController.cs:513-545`). Storefront still returns the stored presentation name, which is non-authoritative.

Webhook metadata is contradictory: the capability registry declares Aramex and DHL webhooks supported (`CarrierCapabilityRegistry.cs:135,154`), while `AramexCarrierDefinition.cs:47` and `DhlCarrierDefinition.cs:49` report `SupportsWebhooks => false`. This is included in F-09.

## 6. Carrier State Machine Findings

The intended state machine is only partially enforced:

```text
not configured -> credentials saved -> connection verified -> ready
```

Missing configuration, disabled configuration, missing fields, unavailable/not-certified carrier, and unsuccessful connection evidence fail closed in `BuildReadinessAsync` (`CarrierIntegrationAuthority.cs:566-628`). Manual bypasses external configuration correctly.

The transition from verified credentials back to changed/unverified credentials is missing. `ValidateAndStoreIntegrationAsync` replaces encrypted credential fields and saves, but never clears `LastValidationSucceeded`, `LastValidatedAtUtc`, or `LastValidationError` (`CarrierIntegrationAuthority.cs:335-425`). `BuildReadinessAsync` subsequently trusts the retained boolean (`:601-609`). This permits the contradictory state **new credential material + old successful validation + Ready**.

The newer Shipping Administration “Save & Validate” flow persists through the authority and then validates, which reduces normal UI exposure, but it does not repair the underlying transition. The legacy canonical PUT endpoint saves without forcing validation, and a failure/crash between save and validate also leaves the stale-ready window.

`CarrierAccount` status is now projected against canonical readiness and is not itself used by checkout. Connection test records remain diagnostic. Those rules pass.

## 7. Manual Fulfillment Findings

Manual fulfills the critical acceptance behavior:

- Capability is `ProductionReady`, credentials are empty/not required, and connection testing is unsupported (`CarrierCapabilityRegistry.cs:85-97`).
- Authority returns manual readiness without a `CarrierConfiguration` (`CarrierIntegrationAuthority` manual branch).
- Shipping methods still require a tenant-owned active `FulfillmentWarehouseId` when active.
- Selection creates a normal `WarehouseResolution`; final checkout reserves inventory and propagates warehouse identity exactly as for external methods.
- Manual does not require `CarrierAccount` or `CarrierWarehouseAssignment`; legacy rows cannot block canonical readiness.

The focused architecture test named `ManualFulfillment_MustNotRequireCarrierWarehouseAssignment` failed because its source-text assertion expected the legacy token in the service. The runtime source is correct; the test is stale and contributes to the red verification gate.

## 8. Shipping Method Findings

Admin create/update/read/delete are tenant-scoped and cover name, Arabic name, description, base/per-kg rates, free threshold, min/max delivery days, sort order, carrier code, service level, tracking URL template, fulfillment warehouse, available countries, and active state.

- Active create/update requires a non-empty warehouse (`ShippingMethodsController.cs:129-134,210-215`).
- Warehouse validation explicitly matches `TenantId`, requires existence, not deleted, and active (`:549-578`).
- Unsupported adapter service levels are rejected during admin create/update (`:488-510`).
- Delete is a tenant-scoped soft delete.
- Negative prices, thresholds, weights, and invalid day ranges are rejected by DTO/controller validation.

The database allows `FulfillmentWarehouseId = NULL`, and the rollout migration did not remediate legacy active rows. Runtime hides/rejects those methods, but that is not the required invariant that active methods cannot silently remain invalid. See F-06.

## 9. Shipping Pricing Findings

The authoritative formula is implemented in `ShippingCalculationService.cs:80-98`:

```text
if subtotal >= free threshold: 0
else: max(0, baseRate + perKgRate * max(0, totalWeight))
round to 2 decimals, MidpointRounding.AwayFromZero
```

Shipping-method preview passes computed cart weight into this formula (`ShippingMethodService.cs:538-548`). Canonical pricing does not. `StorefrontPricingService.CalculateCartPreviewAsync` and `CalculateCheckoutAsync` construct totals without weight, and `BuildBreakdownAsync` calls `Calculate(shippingMethod, subtotal)` (`StorefrontPricingService.cs:63-124`); that overload defaults `totalWeight = 0` (`ShippingCalculationService.cs:24-26`). Final checkout and order persistence consume this canonical pricing breakdown.

Concrete reproduction:

```text
base rate = 10.00
per-kg rate = 2.00
cart weight = 3 kg
subtotal below free threshold

shipping-method preview = 10 + 2*3 = 16.00
pricing preview/final checkout/order = 10 + 2*0 = 10.00
```

The browser cannot submit an authoritative shipping amount. `CheckoutRequestDto` carries authoritative IDs and optional expected-pricing evidence; final checkout recalculates and rejects mismatch. That protects against client manipulation but does not make the server's undercharge correct.

Currency is server-derived and money is rounded consistently to two decimals. Tax is calculated from subtotal, not shipping, by the current tax policy. Free/zero and negative-rate safeguards are present.

## 10. Service Level Findings

Admin CRUD validates a requested service level against adapter metadata. Runtime does not preserve that guarantee:

- `WarehouseResolutionService.ValidateSelectedShippingMethodAuthorityAsync` validates method, warehouse, and carrier readiness but never calls `GetSupportedServiceLevels` or checks `method.ServiceLevel` (`WarehouseResolutionService.cs:772-838`).
- `CarrierIntegrationAuthority.BuildReadinessAsync` accepts `shippingMethodId` but only rejects `Guid.Empty`; it does not load the method or validate service level (`CarrierIntegrationAuthority.cs:623-625`).
- `CarrierServiceLevelEnablement` is an administration facade; storefront and checkout do not enforce disabled levels.
- Shipment creation normalizes and uses the current method value (`ShipmentService.cs:142-175,220`; `CarrierShipmentService.cs:266-301`).

Thus API-created valid values are protected, but stale/directly migrated values, later capability changes, or an administration-disabled level can pass selection/checkout. Invariant 11 fails.

The storefront `ShippingMethodDto` has no `ServiceLevel`, `CarrierCode`, or tracking URL (`PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs:17-89`), so the storefront cannot display the exact service level requested by the audit.

## 11. Warehouse Authority Findings

The canonical authority chain is correctly implemented:

```text
ShippingMethod.FulfillmentWarehouseId (method authority)
 -> WarehouseResolution.WarehouseId (checkout authority)
 -> ProductVariantStockRepository.ReserveAsync(warehouseId)
 -> Order.WarehouseId
 -> each OrderItem.WarehouseId
 -> Shipment.OriginWarehouseId
```

Classifications of alternate sources:

| Source | Classification | Result |
|---|---|---|
| Browser `warehouseId` | UNSAFE if accepted; it is not accepted in canonical DTOs | Cannot override |
| Raw `X-Tenant-Id` | DIAGNOSTIC after middleware rewrite | Cannot establish tenant |
| Cart/session warehouse-like state | DERIVED/input evidence | Cannot override selected method |
| Inventory warehouse search | DERIVED eligibility | Selected-method resolution narrows to method warehouse |
| `CarrierWarehouseAssignment` | LEGACY SAFE | Not current method authority |
| CarrierAccount warehouse policy | COMPATIBILITY | Not checkout authority |
| Legacy quote warehouse | LEGACY SAFE for quoting only | Not final checkout authority |
| `Order.WarehouseId` | DERIVED durable result | Shipment origin input |
| Shipment origin snapshot | DERIVED immutable retry evidence | Cannot change checkout warehouse |

The method-to-warehouse FK is composite on tenant plus ID and `Restrict` on delete (`ApplicationDbContext.cs:668-677`). Selection and final checkout also explicitly require the warehouse active and not deleted.

## 12. Storefront Preview Findings

The traced path is `CheckoutPage` -> `checkoutStore.fetchShippingMethods` -> ensure checkout session -> `POST /api/storefront/shipping-methods/preview` -> owned cart/session -> SQL shipping methods -> warehouse/stock/readiness filters.

Passes:

- Cart hydration precedes the normal preview trigger.
- A checkout session is ensured before the request.
- Preview request body contains only `checkoutSessionId` (`ShippingMethodDto.cs:132-145`).
- Request key binds cart ID, session ID, and cart update time; duplicate in-flight work is reused.
- A dedicated `AbortController` and sequence check discard stale previews; cancellation is not surfaced as a backend failure (`checkoutStore.ts:2011-2114`).
- Price/carrier/warehouse are server-derived; exact stock at the configured warehouse and carrier readiness filter methods.

Failure: shipping-method preview price is incompatible with canonical pricing/final checkout when per-kg pricing is non-zero (F-01).

## 13. Shipping Selection Findings

Selection accepts `checkoutSessionId` and `shippingMethodId` only (`ShippingMethodDto.cs:164-183`). The controller resolves the authenticated/guest owner; the service loads a same-tenant active method, derives its warehouse, checks active warehouse, stock, and carrier readiness, then stores the returned resolution ID/version.

Wrong tenant, inactive/deleted method, changed warehouse, changed readiness, changed cart/stock, expired resolution, and stale version are rejected on final revalidation.

Changed service level is not independently detected if the warehouse and other hashed inputs remain otherwise compatible. The resolution hash includes shipping-method ID/warehouse and cart/stock evidence, but the runtime authority does not validate or snapshot service level.

Direct rapid user selections are not protected by a dedicated selection abort/sequence. `setShippingMethod` only applies the preview sequence when invoked from preview (`checkoutStore.ts:1890-1928`), so a late response for method A can overwrite a later method B selection. Final checkout remains server coherent with the last stored resolution, but customer intent can be reversed (F-10).

## 14. WarehouseResolution Findings

`WarehouseResolution` is a retained, tenant-owned checkout authority with:

- tenant, checkout-session, cart, warehouse, algorithm, source, status, version, expiry, input hash/JSON, lock and converted-order evidence;
- composite tenant foreign keys to session, warehouse, and order;
- unique tenant/session/version and filtered unique active-resolution indexes;
- check constraints for positive version and status-dependent fields.

Selection invalidates stale active records and creates increasing versions. Concurrent creation handles the unique-index race. Final validation loads the exact tenant/id/version, verifies session/cart ownership, expiry/status/warehouse, re-derives current method warehouse/readiness, recomputes cart/stock hash, and locks the row when requested (`WarehouseResolutionService.cs:261-389`). PostgreSQL uses `FOR UPDATE`; final checkout also runs serializable.

Cart mutation, stock change, warehouse change/disable/delete, method activity change, readiness change, resolution expiry, and wrong session/version fail closed. Service-level mutation is the uncovered exception described in section 10.

## 15. Inventory Reservation Findings

Checkout calls `ReserveAsync` with the final resolution warehouse (`CheckoutService.cs:5799` and final transaction call path). `ProductVariantStockRepository.ReserveAsync` tenant-scopes the variant and warehouse, verifies inventory lineage, loads stock rows under deterministic database locks, verifies total available, increments reserved quantity, writes reservations/movements, enforces `0 <= ReservedQuantity <= Quantity`, and saves within a serializable mutation transaction (`ProductVariantStockRepository.cs:81-244,889-929,1274-1290`).

For canonical checkout, no first-warehouse or cross-warehouse fallback is used because a concrete resolution warehouse is supplied. Concurrent final-stock attempts are serialized/row-locked and cannot both reserve the same last units. Failures abort the surrounding checkout transaction.

## 16. Final Checkout Findings

Final checkout does not accept warehouse ID, carrier name/code, service level, or shipping price as authority. It requires method/session/resolution identity and version, builds the idempotency hash with those values (`CheckoutService.cs:2121-2149`), and recalculates cart/pricing server-side.

Inside the final serializable transaction (`CheckoutService.cs:6379`), it re-locks/revalidates the shipping method and payment method, validates expected pricing, validates and locks the warehouse resolution, stamps order/items, reserves stock, persists idempotency/outbox state, and marks the resolution ordered.

Final checkout is therefore strong against browser authority and stale warehouse/cart state. It is not money-correct for per-kg rates (F-01), and it does not close the service-level gap (F-04).

## 17. Order / OrderItem Propagation Findings

The order and all order items are assigned from the validated final resolution in the transaction. There is no documented split-order warehouse behavior in this path, so the certified intended invariant is:

```text
Order.WarehouseId
 == every OrderItem.WarehouseId
 == WarehouseResolution.WarehouseId
```

The order also persists shipping method ID/name, shipping cost, carrier/fulfillment fields, and pricing snapshot evidence. It does not persist an immutable service-level snapshot suitable for later shipment execution. This omission enables F-05.

## 18. Shipment Runtime Findings

Positive source evidence:

- One non-deleted shipment per tenant/order is enforced by a unique filtered index (`ApplicationDbContext.cs:3322-3347`).
- API shipment creation requires the method ID stored on the order, same tenant, paid/accounting-ready state, and canonical carrier readiness (`ShipmentService.cs:129-167`).
- `Shipment.OriginWarehouseId` starts from `Order.WarehouseId` (`:177-220`), and origin resolution persists a warehouse address/geography snapshot for retries.
- External credential resolution uses `CarrierIntegrationAuthority`; a stable carrier request ID, external reference uniqueness, outbox, reconciliation, and webhook-idempotency structures exist.

Blocking behavior:

- Both shipment entry points reload the current shipping method and require it still be active (`ShipmentService.cs:142-175`; `CarrierShipmentService.cs:266-301`). An admin deactivation/deletion after a paid order can make the order unfulfillable.
- They use the method's current carrier/service level. Changing express to standard after checkout causes shipment creation to execute standard, not what the customer purchased.
- Focused retry/reconciliation tests failed, so duplicate-suppression/recovery behavior is not presently test-certified.
- Aramex and DHL cancellation are explicitly unsupported; that is acceptable only if surfaced as a declared capability and operational fallback, not claimed as certified cancellation.
- Webhook trust is blocked by F-02/F-09 and failed tests.

## 19. Legacy Path Findings

| Path/structure | Classification | Evidence and risk |
|---|---|---|
| `CarrierAccount` | COMPATIBILITY/diagnostic | UI lifecycle facade; canonical readiness projection now wins |
| `CarrierConnectionTestRecord` | DIAGNOSTIC | Audit evidence only; not checkout authority |
| `CarrierWarehouseAssignment` | LEGACY SAFE currently | No current carrier requires origin-readiness; cannot override method warehouse |
| Anonymous `POST /api/ShippingMethods/calculate` | LEGACY DANGEROUS if reused | Accepts client subtotal/weight and omits canonical carrier/readiness checks; not used by final checkout |
| Legacy storefront GET shipping methods | LEGACY SAFE with caveat | Server tenant/readiness/active-warehouse filtered; lacks the canonical cart-stock/session context |
| `ShippingQuoteService` | LEGACY/quote-only | Must not be treated as checkout warehouse or final-price authority |
| Stored `CarrierName` | PRESENTATION | Runtime uses normalized `CarrierCode` |
| Raw `X-Tenant-Id` comments/contracts | STALE DOCUMENTATION | Middleware strips and replaces inbound value before controller execution |

No legacy path was found that overrides `WarehouseResolution` during canonical final checkout. The legacy calculate endpoint should remain clearly non-authoritative.

## 20. Tenant Isolation Findings

No confirmed cross-tenant bypass was found.

- `TenantResolutionMiddleware.cs:84-85` captures then removes inbound `X-Tenant-Id`; it derives the trusted tenant from authenticated claims and/or verified host, rejects mismatches, sets `ITenantContext`, and only then re-adds the verified ID (`:118-185`).
- Admin controllers use `ITenantContext.RequiredTenantId` and explicit tenant predicates.
- Storefront shipping endpoints validate authenticated customer tenant or guest session ownership.
- Carrier configuration, shipping method, warehouse, resolution, cart/session, inventory, order, and shipment queries use tenant predicates and/or global filters.
- Composite tenant keys/FKs protect method-to-warehouse and resolution relationships.

The controller comments that call `X-Tenant-Id` “required” are misleading but do not create authority because middleware rewrites it before the controller.

## 21. Idempotency / Concurrency Findings

Preview deduplication uses a cart/session/update request key and cancellation/sequence control. Resolution version uniqueness plus a filtered active index closes concurrent selection races server-side. Final checkout's request hash includes checkout session, resolution ID, resolved warehouse, resolution version, and method ID; the database also enforces one active order per tenant/cart. Cart/resolution rows are locked and the final transaction is serializable.

Inventory uses deterministic row locks and serializable mutation handling. Shipment tables have unique order, external reference, and carrier request identities.

The source design is strong, but the focused shipment retry/idempotency tests are red. Invariant 16 cannot be certified until those failures are resolved and rerun. UI selection ordering remains a non-authoritative but customer-visible race (F-10).

## 22. UI/API Contract Findings

Carrier Integration and Shipping Methods are conceptually separated in the admin UI. Manual is presented as credential-free. External accounts expose save/validate/readiness/diagnostic semantics, and production-blocked carriers are not activatable through the canonical API.

Contract gaps:

- The storefront API DTO omits service level, canonical carrier code, and tracking URL, while the requested UX requires correct service-level display (`ShippingMethodDto.cs:17-89`).
- `CarrierIntegrationUpdate` includes `SupportsWebhooks` but no webhook secret (`CarrierIntegrationAuthority.cs:84-109`); the UI/API can imply webhook support without being able to establish trust material.
- Webhook capability metadata conflicts between registry and carrier definitions.
- Storefront method-selection responses can arrive out of order and overwrite a newer choice.
- Internal error codes are generally translated into user-facing messages; diagnostics retain correlation IDs and structured logging.

## 23. Database / Migration Findings

`dotnet ef migrations has-pending-model-changes --no-build` reported: **No changes have been made to the model since the last migration.**

The warehouse-resolution migration supplies composite tenant FKs, status/version check constraints, unique session/version, and filtered active-resolution uniqueness. The subsequent shipping-method migration creates a composite tenant/warehouse FK and index.

Unsafe rollout evidence: `20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority.cs:14-31` only adds nullable `FulfillmentWarehouseId`, index, and FK. It performs no preflight, backfill, or update of existing active rows. The application prevents new active null assignments, but existing production rows remain database-active and are silently filtered by storefront (`ShippingMethodService.cs:243-246`).

There is also migration artifact drift: the prior warehouse-resolution designer already contains the later fulfillment-warehouse model even though its `Up` method did not create that column. The final snapshot and pending-model check are coherent, so this is maintainability evidence rather than the primary schema blocker.

`CarrierConfiguration.WebhookSecret` is a normal max-length string from the baseline schema, unlike encrypted credential fields; no encryption/value converter protects it.

## 24. Test Coverage Findings

Verification executed on 2026-08-12:

| Command | Result |
|---|---|
| `dotnet build PointOfSale.sln --no-restore` | PASS — 0 warnings, 0 errors |
| Full `dotnet test ... --no-build --no-restore` | INCOMPLETE — timed out at 64 seconds without a final result |
| Focused 37-test shipping/security/warehouse/pricing run | **FAIL — 28 passed, 9 failed** |
| Storefront `npm test -- --run` | PASS — 27 files, 283 tests |
| Storefront `npm run build` | PASS — warnings only |
| Admin Web focused shipping tests | PASS — 3 files, 19 tests |
| Admin Web `npm run build` | PASS |
| EF pending-model check | PASS — no pending changes |

Focused failures:

1. Three `ShipmentExternalRegistrationSafetyTests` retry/reconciliation cases.
2. `ShopixyWarehouseResolutionAuthorityCertificationTests.ManualFulfillment_MustNotRequireCarrierWarehouseAssignment` (stale source-token assertion).
3. Five `ShippingWebhookTrustPipelineTests` cases: valid signature, missing signature, uppercase signature, duplicate event, invalid signature.

Coverage strengths include admin carrier/service-level validation, tenant isolation, warehouse resolution behavior, stock reservation lifecycle, pricing primitives, idempotency structures, and frontend request contracts.

Material gaps/false positives:

- No end-to-end test compares weighted shipping-method preview with canonical pricing preview, final checkout, and persisted order.
- No test proves credential rotation clears prior success.
- Pricing unit tests prove the calculation primitive with weight but do not prove `StorefrontPricingService` supplies weight.
- Several “certification” tests assert source substrings rather than runtime business behavior; one is now demonstrably stale.
- No behavior test proves a disabled `CarrierServiceLevelEnablement` blocks storefront/final checkout.
- No test proves an order preserves carrier/service level across later shipping-method edits/deactivation.
- No migration test exercises existing active methods with null fulfillment warehouse.

## 25. Carrier Certification Matrix

| Carrier | Credential source | Save/encryption | Validation | Readiness | Storefront eligible | Runtime execution | Certification |
|---|---|---|---|---|---|---|---|
| Manual | None | N/A | Not required | Always ready | Yes with valid method/warehouse/stock | Internal fulfillment shipment | Code-supported; subsystem BLOCKED |
| Bosta | `CarrierConfiguration.ApiKey` | Canonical/encrypted | Remote `/cities`-style probe | Authority + persisted success | Yes when ready | Create/cancel/tracking; webhook intended | Code partial; tests not green; live blocked |
| Aramex | API key/secret/account + encrypted config extras | Canonical; secret/config encrypted | Remote country fetch | Authority + persisted success | Yes when ready | Create/tracking; cancel unsupported | Code partial; webhook contradiction; live blocked |
| DHL Express | API key/secret/account | Canonical; secrets encrypted | **Local-only** field check | Authority treats local success as verification | Yes when ready | Create/tracking; cancel unsupported | Not provider-validated; live blocked |
| FedEx | No certified schema | N/A | Unsupported | `NotCertified` blocker | No | Fail closed | Not certified |
| Mylerz | No available public contract | N/A | Unsupported/fail closed | `Unavailable` blocker | No | Fail closed | Unavailable |

No external carrier is live-certified by this audit. Even Bosta/Aramex/DHL cannot be classified merely “external verification blocked” at subsystem level because internal P0/P1 defects remain.

## 26. Production Scenario Matrix

| Scenario | Result | Exact evidence |
|---|---|---|
| A. Manual + one warehouse | PASS | Credential-free readiness; method warehouse -> resolution -> reservation/order |
| B. Manual + multiple warehouses | PASS | Selected method fixes one warehouse; no browser/multi-warehouse override |
| C. Bosta + one warehouse | BLOCKED | Code path exists; P0/P1 defects and no live verification |
| D. Bosta + multiple warehouses | BLOCKED | Warehouse chain coherent; carrier lifecycle not certifiable |
| E. Bosta credentials missing | PASS | Readiness returns missing-field/config blockers |
| F. Bosta saved but unverified | PASS with caveat | New config fails closed; rotated formerly-verified config does not (F-03) |
| G. Bosta verified | BLOCKED | Internal pricing/webhook/service/shipment defects remain |
| H. Bosta provider unreachable | PASS | Validation records failure and readiness blocks, except stale-save window |
| I. Bosta method inactive | PASS | Preview/selection/final reject inactive |
| J. Bosta method missing warehouse | FAIL | Runtime hides/rejects; migration leaves existing DB-active invalid rows |
| K. Warehouse disabled after method creation | PASS | Preview/selection/final require active warehouse |
| L. Method warehouse changed after preview | PASS | Re-derived warehouse differs; stale resolution rejected |
| M. Cart changed after preview | PASS | input/cart/stock hash mismatch requires re-resolution |
| N. Checkout session stale | PASS | owner/status/cart/expiry checks reject |
| O. Stock unavailable | PASS | preview filter and final locked reservation fail closed |
| P. Concurrent checkout | PASS (source) | serializable transaction, row locks, active-cart uniqueness; full suite incomplete |
| Q. Cross-tenant warehouse attempt | PASS | composite FK + tenant predicates |
| R. Cross-tenant method attempt | PASS | tenant-scoped selection/final queries |
| S. Invalid/disabled service level | FAIL | only admin adapter validation; runtime/enablement not enforced |
| T. Readiness changed after preview | FAIL for credential rotation | normal disable/failure blocks; rotated credentials inherit old success |
| U. Duplicate checkout submission | PASS (source) | expanded hash + idempotency record + active cart/order unique index |
| V. Shipment retry | FAIL certification | three focused safety tests failed |
| W. Webhook | FAIL | plaintext/unmanaged secret, metadata contradiction, five trust tests failed |

## 27. Business Invariant Certification

| Invariant | Result | Evidence |
|---|---|---|
| 1. Cross-tenant method cannot be selected | PASS | explicit tenant method/session predicates |
| 2. Customer cannot cause another warehouse | PASS | no warehouse in canonical request; server derives it |
| 3. Method fulfillment warehouse is server authoritative | PASS for runtime | selection derives `FulfillmentWarehouseId`; migration caveat F-06 |
| 4. WarehouseResolution is final checkout authority | PASS | exact ID/version/owner/hash revalidation and lock |
| 5. Inventory reservation uses resolution | PASS | resolved warehouse passed to `ReserveAsync` |
| 6. Order warehouse derives from resolution | PASS | order/items stamped from final resolution |
| 7. Readiness comes from CarrierIntegrationAuthority | **FAIL coherent-state requirement** | single caller authority exists, but stale validation corrupts its answer |
| 8. Connection success alone cannot make runtime ready | **FAIL** | old success survives credential replacement |
| 9. Manual never requires carrier credentials | PASS | manual authority branch/capability |
| 10. Shipping price cannot be client-controlled | PASS, but money incorrect | server recalculates; F-01 undercharges weighted shipping |
| 11. ServiceLevel cannot be arbitrary | **FAIL** | runtime does not revalidate or enforce enablement |
| 12. Stale resolution cannot finalize | PASS | expiry/status/version/hash checks |
| 13. Changed method warehouse cannot reuse old resolution | PASS | expected warehouse recomputed and compared |
| 14. Legacy carrier structures cannot override authority | PASS in canonical checkout | facades/assignments do not select final warehouse/readiness |
| 15. Preview/final use compatible eligibility | **FAIL** | weighted price differs; service-level gap |
| 16. Duplicate requests cannot create inconsistent state | NOT CERTIFIED | source defenses exist; shipment retry gate is red |

## 28. Complete Findings Register

| ID | Severity | Title | Lifecycle | Blocker |
|---|---|---|---|---|
| SHOP-SHIP-F-01 | P0 | Weighted preview and final checkout calculate different shipping totals | Preview -> pricing -> checkout/order | Yes |
| SHOP-SHIP-F-02 | P0 | Carrier webhook secret is plaintext and lacks canonical persistence | Config -> webhook trust | Yes |
| SHOP-SHIP-F-03 | P1 | Credential changes retain stale successful validation | Config -> readiness -> storefront/shipment | Yes |
| SHOP-SHIP-F-04 | P1 | Runtime does not enforce canonical/disabled service levels | Method -> selection -> checkout -> shipment | Yes |
| SHOP-SHIP-F-05 | P1 | Shipment execution depends on mutable current method state | Order -> shipment | Yes |
| SHOP-SHIP-F-06 | P1 | Warehouse-authority migration leaves legacy active methods invalid | Migration -> storefront eligibility | Yes |
| SHOP-SHIP-F-07 | P1 | Critical .NET shipping verification gate fails 9/37 | Test/production proof | Yes |
| SHOP-SHIP-F-08 | P2 | Storefront method contract omits service level/carrier code/tracking URL | UI/API | No alone |
| SHOP-SHIP-F-09 | P2 | Webhook capability metadata contradicts adapter definitions | Capability/webhook | No alone; compounds F-02/F-07 |
| SHOP-SHIP-F-10 | P2 | Rapid direct method selections can apply out of order | Storefront selection | No alone |
| SHOP-SHIP-F-11 | P2 | Legacy calculate/GET/quote paths remain ambiguous non-authorities | Legacy | No alone |
| SHOP-SHIP-F-12 | P3 | Stale comments and source-token certification tests create artifact drift | Docs/tests | No |

## 29. P0 Findings

### SHOP-SHIP-F-01 — Weighted shipping total divergence

- **Evidence/files/symbols:** `ShippingMethodService.ApplyCalculatedPrices` (`PointOfSale.API/Services/Storefront/ShippingMethodService.cs:538-548`) passes `totalWeight`; `StorefrontPricingService.CalculateCartPreviewAsync`, `CalculateCheckoutAsync`, and `BuildBreakdownAsync` (`PointOfSale.API/Services/Pricing/StorefrontPricingService.cs:63-124`) never calculate/carry weight and call the overload whose default is zero (`ShippingCalculationService.cs:24-26`).
- **Authority violated:** one server-authoritative price across preview, checkout, and order.
- **Runtime impact:** customers see one amount and are charged/persisted another; non-zero per-kg fees are omitted from final totals, causing deterministic undercharge and accounting mismatch.
- **Reproduction:** base 10, per kg 2, weight 3, below threshold -> method preview 16; final/order 10.
- **Recommended remediation:** make total cart weight part of the canonical pricing request and use the identical pricing function/input snapshot in method preview, pricing preview, final transaction, expected-pricing validation, and order snapshot. Add an end-to-end regression.
- **Certification impact:** P0 blocker.

### SHOP-SHIP-F-02 — Webhook secret plaintext and no canonical write path

- **Evidence/files/symbols:** `CarrierConfiguration.WebhookSecret` is a plain string (`PointOfSale.Core/Entities/CarrierConfiguration.cs:126-135`); the baseline migration creates a normal varchar; `ResolveWebhookTrustAsync` returns `config.WebhookSecret` directly (`CarrierIntegrationAuthority.cs:511-542`); `CarrierIntegrationUpdate` has no webhook-secret field (`:84-109`); repository search found no carrier assignment/write path.
- **Authority violated:** canonical encrypted credential persistence and complete webhook trust configuration.
- **Runtime impact:** any existing carrier webhook secret is readable as plaintext from the database; a merchant cannot establish/rotate it through the canonical carrier API. Webhook operation is either unavailable or dependent on out-of-band database writes.
- **Reproduction:** enable webhooks in carrier config through the canonical API; no request field can set trust secret. Insert a secret directly; authority reads it without decryption, confirming plaintext semantics.
- **Recommended remediation:** add an encrypted, write-only canonical webhook credential field with rotation semantics; never return it; migrate/re-encrypt existing values; bind webhook readiness to validated trust configuration.
- **Certification impact:** P0 security and lifecycle blocker.

## 30. P1 Findings

### SHOP-SHIP-F-03 — Stale successful validation survives credential rotation

- **Evidence:** `ValidateAndStoreIntegrationAsync` writes credential/config fields and saves without clearing validation fields (`CarrierIntegrationAuthority.cs:335-425`); readiness trusts `LastValidationSucceeded` (`:601-609`).
- **Authority violated:** saved credential version must be the version actually verified.
- **Runtime impact:** unverified/invalid rotated credentials can be storefront-eligible and reach shipment execution.
- **Reproduction:** validate credential A successfully; PUT credential B without validation; query readiness for storefront/shipment -> old success remains and no connection blocker is added.
- **Remediation:** invalidate validation atomically on any credential/environment/endpoint field change, or store a credential fingerprint/version and require validation evidence for the current version.
- **Certification impact:** blocker.

### SHOP-SHIP-F-04 — Service-level runtime authority incomplete

- **Evidence:** admin validates adapter levels (`ShippingMethodsController.cs:488-510`), but resolution/readiness do not validate the method value (`WarehouseResolutionService.cs:772-838`; `CarrierIntegrationAuthority.cs:623-625`) and do not enforce `CarrierServiceLevelEnablement`.
- **Authority violated:** service level must be canonical, enabled, carrier-compatible, and revalidated at selection/final execution.
- **Runtime impact:** stale/arbitrary/disabled levels can checkout and later fail at provider execution or execute unintended service.
- **Reproduction:** persist an unsupported level or disable a currently used enablement after method creation; selection and final checkout do not reject on service-level grounds.
- **Remediation:** centralize runtime service-level admission in carrier authority and invoke it from preview, selection, final checkout, and shipment; bind it into resolution/order evidence.
- **Certification impact:** blocker.

### SHOP-SHIP-F-05 — Mutable method governs historical order shipment

- **Evidence:** shipment creation reloads the method, requires current `IsActive`, and copies its current carrier/service (`ShipmentService.cs:142-175,220`; `CarrierShipmentService.cs:266-301`).
- **Authority violated:** fulfillment must execute the immutable shipping decision purchased at checkout.
- **Runtime impact:** deactivate/delete a method and a paid order cannot ship; edit express to standard and the historical order ships standard.
- **Reproduction:** place order with express; update same method to standard or inactive; create shipment.
- **Remediation:** persist immutable carrier code, service level, delivery promise, and required execution metadata on the order/shipping snapshot; execute from that snapshot while using current credentials/readiness only for connectivity/safety.
- **Certification impact:** blocker.

### SHOP-SHIP-F-06 — Unsafe existing-row warehouse migration

- **Evidence:** migration adds nullable field/index/FK only (`20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority.cs:14-31`); storefront filters null/invalid warehouses (`ShippingMethodService.cs:243-246`).
- **Authority violated:** active methods cannot silently remain without a valid fulfillment warehouse.
- **Runtime impact:** production upgrade can leave DB-active methods that disappear from checkout without remediation or operator preflight.
- **Reproduction:** apply migration to an existing active shipping method; field remains null; method remains `IsActive = true`; storefront omits it.
- **Remediation:** add deployment preflight and deterministic tenant-safe backfill or explicit deactivation with operator-visible diagnostics; verify no active null/invalid rows before activation.
- **Certification impact:** blocker.

### SHOP-SHIP-F-07 — Critical shipping tests are red

- **Evidence:** focused run: 37 total, 28 passed, 9 failed; failures listed in section 24. Full run timed out without verdict.
- **Authority violated:** final certification requires proven retry, webhook trust, manual compatibility, and complete critical runtime behavior.
- **Runtime impact:** shipment retry/reconciliation and webhook security/idempotency cannot be asserted production-safe; some failures may be stale fixtures, but that still invalidates the gate.
- **Reproduction:** run the exact focused command in section 24.
- **Remediation:** classify each failure as implementation vs stale-test defect, correct it in a separate fix task, add missing behavioral tests, then run the complete suite to completion.
- **Certification impact:** blocker.

## 31. P2/P3 Findings

- **F-08 (P2):** add service level, canonical carrier code, and tracking URL to the storefront read contract if they are customer-visible requirements; consume them without making them browser authority.
- **F-09 (P2):** reconcile registry webhook flags/policies with `AramexCarrierDefinition` and `DhlCarrierDefinition`; expose only tested capabilities. This compounds the P0/P1 webhook failures.
- **F-10 (P2):** add a selection-specific abort/token/sequence and only commit the response matching the latest customer intent.
- **F-11 (P2):** explicitly mark or retire legacy calculate/GET/quote routes; ensure no caller treats browser subtotal/weight or quote warehouse as checkout authority.
- **F-12 (P3):** correct stale `X-Tenant-Id` comments and replace source-substring “certification” tests with behavior tests.

## 32. Required Remediation Before Closure

Priority order:

1. Fix F-01 and prove identical weighted shipping totals through preview, pricing preview, final checkout, persisted order, retry/idempotent response, free threshold, rounding, and missing-weight behavior.
2. Fix F-02: encrypted canonical webhook-secret storage, write/rotate flow, migration, readiness, and trust tests.
3. Fix F-03 with credential-version-bound validation and atomic invalidation on every relevant configuration change.
4. Fix F-04 by enforcing canonical enabled service levels at preview/selection/final/shipment and snapshotting the chosen level.
5. Fix F-05 so shipment executes immutable order shipping evidence and remains operable after benign admin edits/deactivation.
6. Fix F-06 with an existing-data-safe migration/deployment preflight and explicit operator outcome.
7. Resolve all F-07 failures, add the missing tests listed in section 24, and complete the full suite.
8. Reconcile webhook capability declarations and storefront/UI contract gaps.
9. Re-run this certification against a cleanly identified commit/worktree and retain production migration/live-carrier evidence.

No new architecture is prescribed; these repairs close contradictions in the existing intended architecture.

## 33. External Verification Requirements

After internal P0/P1 remediation, external/live evidence is still required before any external carrier can be called live-certified:

- Bosta and Aramex: tenant-owned sandbox/live credentials; remote connection validation; create, label/AWB if applicable, tracking, cancellation support/error semantics, webhook registration/signature/event replay, geography mapping, and provider idempotency/reconciliation.
- DHL: replace or supplement local-only credential validation with an official provider-authenticated probe; verify create/tracking and explicitly certify cancellation/webhook support status.
- Provider outage, timeout, ambiguous create success, retry by carrier request ID, duplicate webhook, signature failure, credential rotation, and disabled configuration drills.
- Production database migration rehearsal with representative existing active shipping methods and warehouse data.
- FedEx remains `NotCertified`; Mylerz remains `Unavailable` until official contracts/capabilities are implemented and separately certified.
- Manual needs no carrier-live verification, but still needs the repaired internal pricing/service/order lifecycle suite.

## 34. Final Certification

**Certification: BLOCKED**

The required logic mandates `BLOCKED` when any P0/P1, authority contradiction, lifecycle incompleteness, or unproven critical runtime behavior exists. This audit confirms all four conditions.

**CAN SHIPPING METHODS BE CLOSED? NO**

Exact remaining blockers, in priority order:

1. P0 weighted shipping preview/final/order price divergence.
2. P0 plaintext and non-configurable carrier webhook secret.
3. P1 stale carrier readiness after credential/configuration changes.
4. P1 missing runtime service-level/enablement enforcement.
5. P1 mutable shipping method controls historical-order shipment execution.
6. P1 unsafe existing-row fulfillment-warehouse migration behavior.
7. P1 failed critical shipping/webhook/retry test gate and incomplete full-suite result.

The correct decision is not “pass with external verification blocked,” because the blockers are internal and reproducible without provider access.

## 35. Worktree Safety Evidence

The repository was already extensively dirty before the audit. The audit preserved that work and performed no reset, clean, restore, checkout, stash, commit, push, source fix, test edit, configuration edit, frontend edit, or migration edit.

### Before audit

- `git status --short`: **93 entries — 56 tracked files modified and 37 untracked paths**, including this report path already present as an untracked historical draft.
- `git diff --stat`: **56 files changed, 4,899 insertions, 1,333 deletions**.

### After audit

- `git status --short`: **93 entries — 56 tracked files modified and 37 untracked paths**.
- `git diff --stat`: **56 files changed, 4,899 insertions, 1,333 deletions**.
- Tracked status/stat counts are identical before and after. The report remained one of the same 37 untracked paths and is the only intentionally updated path. Build/test outputs were ignored artifacts. All other dirty entries pre-existed and were not modified by this audit.

### Commands used as evidence

```text
git status --short
git diff --stat
dotnet build PointOfSale.sln --no-restore
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-build --no-restore
dotnet test ... --filter <focused shipping/security/warehouse/pricing classes>
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build
npm test -- --run                         (Storefront)
npm run build                             (Storefront)
npx vitest run <3 shipping test files>    (Admin Web)
npm run build                             (Admin Web)
```

This document records source evidence and local verification only. It does not claim deployment, production database, provider sandbox, or live-carrier execution evidence.
