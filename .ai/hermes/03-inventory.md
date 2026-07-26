# Shopixy AI QA Onboarding — Inventory

Audience: a Senior QA Engineer (human or AI) joining the Shopixy / PointOfSale team and
owning inventory quality. This guide distills the durable inventory business rules from
`.ai-context/*` and `docs/architecture/*`. It is an onboarding/training artifact, not a
summary of the code. When in doubt, the `.ai-context` files and the certification
findings are the truth; the README is STALE and must not be trusted for current behavior.

Authoritative inputs used to build this guide:
- `.ai-context/ARCHITECTURE_TRUTH.md` (Inventory Authority Flow, ownership boundaries, global rules)
- `.ai-context/PROJECT_CONTEXT.md` (subsystem inventory authority table, S76 hardening)
- `.ai-context/KNOWN_RISKS.md` (residual/accepted inventory risks, PASS 12.5 freeze)
- `.ai-context/CODEX_RULES.md` (POS-ADMIN-CERTIFICATION-01 PASS 3, PASS 12.5 freezes; 9F variant inventory/reservation freeze)
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` (reservation cleanup authority recomposition)
- `docs/architecture/product-inventory-reservation-certification-findings.md` (PASS 3)
- `docs/architecture/pos-warehouse-inventory-authority-certification-findings.md` (PASS P1-EB)
- `docs/architecture/marketplace-variant-inventory-certification-findings.md` (PASS 12.5)
- `docs/architecture/treasury-reservation-authority-certification-findings.md` (PASS T4 — financial, see note)

NOTE ON TERMINOLOGY: there are TWO different "reservations" in this codebase.
- Inventory stock reservations (`InventoryReservations`) — the subject of THIS guide.
- Treasury/payout reservations (`CommerceHubVendorEarnings.PayoutReservedAmount`,
  `AffiliatePayoutRequests`) — covered by PASS T4 and owned by the financial/payout
  workflows, NOT the inventory authority. Do not conflate them. See "High Risk Areas".

# Purpose

Define the durable business rules that govern inventory, stock, warehouses, reservations,
stock movements, and their projection/replay behavior, so a QA engineer can test inventory
mutations safely and recognize an authority violation versus an accepted eventual-consistency
tradeoff. The single organizing principle: PostgreSQL is the authoritative write model for
inventory; every other representation (Mongo, Redis, Elasticsearch, cache, projections,
event payloads, frontend state) is derivative evidence only.

# Business Goals

- Keep inventory truth in one place (SQL) and make all derivative surfaces converge to it.
- Prevent oversell, negative stock, over-reservation, and cross-tenant inventory leakage.
- Make stock mutations, transfers, reservations, releases, commits, returns, and adjustments
  safe under concurrency, replay, duplicate delivery, stale evidence, and adversarial input.
- Preserve fail-closed behavior: when current SQL lineage cannot prove a mutation is valid,
  reject/repair/no-op/tombstone rather than reusing stale or derivative state.
- Keep inventory auditability: every authoritative mutation writes a movement/event/inbox row.

# Authoritative Data Sources

Inventory truth lives in PostgreSQL. These are the authoritative tables/columns:

- `ProductVariantStocks` — current physical / reserved / damaged quantity per
  (tenant, variant, warehouse). The unique tenant/variant/warehouse key prevents duplicate rows.
- `Warehouses` — warehouse ownership; must be active, same-tenant to be inventory-authoritative.
- `InventoryReservations` — reservation rows; only current, active, unexpired,
  lineage-backed reservations are consumption authority.
- `StockMovements` — append-only movement history (after-state computed from SQL).
- `ProductVariants` — variant semantic ownership (`SemanticLifecycleState`), SKU/barcode,
  parent product lineage. Only current variants own stock/reservation authority.
- `Products` — parent product lifecycle (active, online-visible, published) that variant
  stock authority depends on.

Derivative (NOT authority) sources, treated as evidence only:
- MongoDB product/stock documents, Elasticsearch `AvailableStock`/`InStock`/stock timestamps,
  Redis caches, CommerceHub listings, storefront cart snapshots, stock-sync event payloads,
  replayed/out-of-order events, and frontend DTOs/route IDs.

Key rule from the architecture table: availability is DERIVED from constrained SQL quantities
(physical, reserved, damaged). Clients cannot submit an authoritative available balance.

# Ownership Boundaries

- `PointOfSale.API` is the orchestration boundary for authoritative inventory writes, transfers,
  reservation lifecycle, hosted workers, and cross-system recovery jobs.
- `InventoryConsistencyService` is the authoritative mutation owner for stock, reservations,
  and movements against PostgreSQL. Do not introduce a second inventory write path.
- `ProductVariantInventoryLineageAuthority` is the SHARED helper that decides whether a stock
  row or reservation row is current, consumable, and externally publishable. Use it; do not
  re-implement lineage checks inline.
- `ProductVariantLifecycleAuthority` decides whether a variant is publicly/purchasably/reservation
  eligible (used before stock acceptance).
- MongoDB/Elasticsearch/Redis consumers own projection freshness only. They do not own the
  inventory decision and must recompose SQL authority before mutating derivative visibility.
- External providers own only their own side effects; they never alter local SQL stock truth.
- Tenant co-ownership is mandatory: every referenced warehouse, product variant, stock row,
  transfer item, reservation, and movement must be tenant-co-owned. A valid `TenantId` on the
  row is NECESSARY but NOT SUFFICIENT when foreign keys can point at tenant-owned records.
  PostgreSQL S76 hardening enforces this through composite tenant foreign keys plus constraint
  triggers for soft-delete, variant-product, and transfer-item checks. Cross-tenant inventory
  leakage is a CRITICAL defect.

# Inventory Lifecycle

The inventory authority flow (durable lifecycle of an inventory-affecting operation):

1. Operation requested through a backend service (storefront checkout, POS admin, CommerceHub
   checkout, transfer, adjustment, return, etc.).
2. `InventoryConsistencyService` performs the authoritative mutation against PostgreSQL stock,
   reservations, and movement tables.
3. The service records inventory inbox / idempotency state BEFORE applying the mutation.
4. The service writes authoritative stock movement history (append-only).
5. A stock-sync outbox message is created to project updated availability outward (async).
6. SQL commit establishes the new inventory truth.
7. Read-side consumers (Mongo/search/cache) update asynchronously and MAY LAG.
8. Cleanup/reconciliation jobs repair stale reservations, missing commits, missing returns, or
   lagging projections — without ever questioning that SQL remained authoritative.

Physical/available quantity invariants (enforced by SQL constraints, the final guard):
- Physical, reserved, and damaged quantities must be non-negative.
- Physical quantity must be >= reserved quantity.
- Available inventory = derived from physical, reserved, damaged; never client-submitted.
- Opening stock is one-time, positive-only, duplicate-free, tenant-explicit, row-locked, and
  rejected after any prior balance or movement authority exists.
- Stock movement rows are APPEND-ONLY: `StockMovement` update/delete are rejected in
  `ApplicationDbContext` and by a PostgreSQL trigger (application and direct-SQL paths).
  Invoice reverts append compensating movements instead of deleting history.

# Reservation Flow

A reservation is a claim on current SQL stock lineage. It is NOT a number copied from a row.

Creation:
- Reserve inside a locked SQL transaction; cannot reserve beyond current SQL available quantity.
- CommerceHub reservation creation must filter candidate stock rows through
  `ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync(...)` before
  accepting raw available quantity.
- Reviewed multi-row stock locks are deterministic by `WarehouseId`/`Id` order (reduces deadlock
  risk; no second inventory authority model is created).
- Admin order creation claims durable request idempotency before reservation and commits order +
  reservations + idempotency binding transactionally. Storefront checkout uses durable checkout
  idempotency, serializable transactions, deterministic stock locking, rollback-before-retry, and
  a database active-cart order uniqueness guard.

Consumption (checkout / payment / fulfillment / commit / return-receipt) — STRICTER than cleanup:
A reservation is consumable ONLY when ALL hold at evaluation time:
- current same-tenant current variant ownership,
- active parent product lineage,
- active warehouse lineage,
- non-deleted current stock ownership,
- active reservation status,
- positive remaining reserved quantity,
- unexpired reservation timing.
Reservation rows that are expired, foreign-owner, orphaned, tied to inactive warehouses, tied to
detached/superseded variants, or no longer backed by current stock ownership are REPAIR EVIDENCE
ONLY — not payment/checkout/fulfillment consumption authority.

Release / cleanup — more permissive:
Cleanup MAY discover stale/expired reservations to repair them, but release authority must be
current SQL truth at mutation time. Before direct stock/coupon release, cleanup re-omposes current
reservation, order, payment-intent, CommerceHub payment, and unresolved-webhook evidence under a
serializable transaction and row locks; changed or uncertain truth must collapse to skip or route
through the payment lifecycle expiration path. Cleanup is intentionally conservative and must NOT
release stock when provider cash authority is unresolved.

Release and commit use cumulative applied quantities, so duplicate delivery cannot release or
consume more than the reservation authority.

Marketplace specifics (PASS 3 / CHECKOUT-CON-07):
- Active SQL reservation quantity must EXACTLY match current SQL CommerceHub order-item authority
  before marketplace payment initiation, payment recovery, reusable-session return, provider-session
  creation, provider-success recognition, or fulfillment commit. "At least enough reserved" is NOT
  sufficient.
- Marketplace orders can span multiple tenant-owned suborders; reservation validation must use
  cross-tenant current consumable reservation filtering (`FilterCurrentConsumableReservationsAcrossTenantsAsync`),
  NOT `expectedItems.First().TenantId`.
- `EnsureMarketplaceOrderReservationsActiveAsync` rejects foreign-owner active reservations and
  over-reserved replay, and distinguishes raw active reservation rows from current lineage-backed
  reservation authority.

# Commit Flow

- Delivery commit for an ALREADY-AUTHORIZED order item is inventory-lineage-authoritative, NOT
  publication-authoritative. Current public unpublish/offline state may block NEW reservation-backed
  payment retry, but it must NOT block stock consumption when current SQL order-item and inventory
  lineage still prove the reserved stock owner.
- CommerceHub delivery commit uses current SQL order-item + inventory-lineage authority rather than
  the reservation-creation publication guard (this was a closed residual gap).
- After SQL inventory consumption, fulfillment state is high-risk irreversible authority. Fulfillment
  transitions, shipment mutation, and warehouse release side effects must reload current SQL
  order/payment/reservation/inventory truth under serializable or locked boundaries before emitting
  events.
- Commit uses cumulative applied quantities; duplicate delivery cannot release/consume beyond the
  reservation authority.

# Projection Rules

- Stock-sync outbox, projection consumers, search overlays, affiliate/CommerceHub visibility, cache,
  and rebuild are TRIGGERS ONLY. They are not authority.
- Before mutating derivative availability/visibility, consumers must reload current SQL variant
  semantic ownership, warehouse ownership, stock-row authority, and event freshness, and must
  TOMBSTONE/DELETE when SQL no longer proves visibility.
- Duplicate EventId delivery must collapse via `ProjectionEventFence` (or equivalent) BEFORE side
  effects. Stale publish/unpublish replay must NOT resurrect hidden/deleted product/variant stock.
- Detached or stale stock-sync messages collapse to completed no-op rather than restoring stale
  Mongo/Search/cache/CommerceHub stock visibility.
- Affiliate/CommerceHub inventory consumers MAY use Mongo product documents for DISPLAY METADATA,
  but `InStock`, stock totals, and stock timestamps must be reloaded from PostgreSQL
  `ProductVariants`/`ProductVariantStocks` before propagation.
- Reviewed availability reads and derivative stock-total recomposition must recompose through current
  stock-owner lineage — never sum raw undeleted `ProductVariantStocks` rows directly. This covers:
  cart/checkout availability, stock-sync/event fanout totals, storefront variant projections,
  CommerceHub variant projections, storefront cart SQL fallback, affiliate intelligence SQL snapshots,
  stock-compensation replay totals, `ProductSearchSqlAuthorityOverlay`, and `ProductVariantReadRepository`
  SQL fallback.
- CommerceHub search overlay and storefront/search SQL overlays must be warehouse-lineage-aware:
  inactive-warehouse rows are excluded from stock totals, not merely hidden.

# Warehouse Rules

- Warehouse ownership is tenant-bound (controller queries + stock mutation lineage checks).
- Stock mutation (opening stock, adjustment, reservation, transfer) requires a CURRENT ACTIVE
  SAME-TENANT Warehouse, ProductVariant, and parent Product.
- Inactive warehouses CANNOT receive opening stock, adjustment, reservation, or transfer mutation.
- Soft-deleted or cross-tenant warehouses fail current-lineage checks; no warehouse delete path
  bypasses stock authority.
- Warehouse activity is part of inventory authority, not a projection hint. An inactive warehouse:
  - collapses reservation-backed payment retry and fulfillment reuse (consumption paths),
  - is NOT reservable inventory even if a raw stock row still has quantity (reservation-creation path),
  - cannot receive returned stock (return replay fails closed if the warehouse is inactive).
- Transfer rules (PASS P1-EB): create/approve/reject/complete require Owner/Admin/Manager; reject
  nonpositive quantities, same-warehouse transfers, invalid lineage, and inactive source/target
  warehouses; completion locks transfer state and stock rows in deterministic order inside one
  transaction; source decrement + target increment + movements + final transfer state commit
  atomically; a completed transfer cannot be applied twice (concurrent/replayed completion safe).

# Stock Movement Rules

- Every inspected authoritative mutation writes a movement with SQL-computed after-state.
- `StockMovement` is append-only: rejected in `ApplicationDbContext` and by a PostgreSQL trigger
  across application and direct-SQL paths.
- Direct adjustments require a durable idempotency key and reject payload reuse; opening movements
  have deterministic identities.
- Sales and purchase invoice inventory reverts append compensating movements instead of deleting
  history (only their inventory boundary was reviewed).
- Negative-result adjustments fail before commit.
- Adjustment endpoints require Owner/Admin/Manager; variant/product/warehouse/tenant lineage is
  resolved from SQL.
- Adjustments are replay-safe: claimed by tenant + operation + request key + request fingerprint;
  completed responses replay without a second mutation.

# Business Invariants

1. PostgreSQL is the only inventory truth. If two systems disagree, PostgreSQL wins.
2. Available quantity is derived (physical - reserved - damaged), never client-submitted.
3. Physical, reserved, damaged quantities are non-negative; physical >= reserved.
4. The unique (tenant, variant, warehouse) key prevents duplicate stock rows.
5. Stock movements are append-only; never updated or deleted.
6. Do not sum raw undeleted `ProductVariantStocks` rows. Always recompose through current
   stock-owner lineage (`WhereCurrentInventoryStockOwner` / `FilterCurrentInventoryStockOwnersAsync`).
7. A reservation is consumable only with full current lineage (variant + product + warehouse +
   stock + active + unexpired). Otherwise it is repair evidence only.
8. Exact reservation ownership and exact reserved quantity are required for marketplace order items;
   "at least enough" is insufficient.
9. Inactive warehouse, superseded/detached/inactive variant, or detached stock owner = NOT reservable
   and NOT consumable, even if a row exists with quantity.
10. Opening stock is one-time and positive-only; rejected after prior balance/movement exists.
11. Transfers complete atomically and exactly once (locked, state-gated, idempotent).
12. Adjustments require privileged roles + SQL lineage + locked balances + durable idempotency.
13. Tenant co-ownership is mandatory on every inventory row referenced; cross-tenant leakage is critical.
14. Reservation cleanup is conservative: never release stock while provider cash authority is unresolved.
15. Stock-consuming writes from any surface (storefront, POS, admin, background) must lock and validate
    SQL stock authority before decrement/reservation. Client/derivative visibility is advisory only.
16. Reservation release/commit uses cumulative applied quantities — duplicate delivery cannot exceed
    the reservation authority.
17. A projected stock row is NOT evidence that stock was authoritatively changed; the SQL
    movement/reservation history is.
18. Derivative stock totals/timestamps visible to customers MAY be temporarily stale (accepted).

# Never Assume

- Do NOT assume a `ProductVariantStocks` row with quantity is reservable/available. Check warehouse
  activity, variant semantic state, and stock-owner lineage.
- Do NOT assume an `InventoryReservation` row is consumable. Verify active + unexpired + current
  lineage; otherwise it is only cleanup evidence.
- Do NOT assume Mongo/Elasticsearch/Redis stock values are current. They lag by design; they are
  triggers/evidence, not truth.
- Do NOT assume a marketplace reservation "is enough" if it is >= order quantity. Exact ownership and
  exact quantity per current order item are required.
- Do NOT assume a single tenant scope when validating marketplace reservations across suborders.
- Do NOT assume checkout reservation publication guard applies at delivery commit — inventory lineage
  governs commit, not public publication state.
- Do NOT assume a returned item can always restore stock. Current warehouse + variant + stock owner
  must still prove inventory authority; otherwise return replay fails closed.
- Do NOT assume deleting a warehouse removes its stock authority silently — inactive/soft-deleted
  warehouses fail current-lineage checks but existing rows may still exist.
- Do NOT treat treasury/payout reservations as inventory reservations (see High Risk Areas).
- Do NOT trust frontend DTOs, route IDs, or client totals as inventory authority. Backend revalidates.
- Do NOT assume read-model lag, fail-closed financial gating under uncertainty, or reconciliation
  states are bugs. They are accepted tradeoffs unless the implementation violates a documented boundary.

# QA Checklist

Inventory mutation / reservation testing:
- [ ] Did the mutation go through `InventoryConsistencyService` (or an authorized boundary), not a
      second write path or raw SQL / bulk update?
- [ ] Is PostgreSQL the system that actually decided the new stock truth?
- [ ] Was the inventory inbox / idempotency state recorded BEFORE the mutation?
- [ ] Was an append-only `StockMovement` written with SQL-computed after-state?
- [ ] Did available quantity stay derived (not client-submitted)? Non-negative? physical >= reserved?
- [ ] Was a current stock-owner lineage filter used instead of summing raw undeleted stock rows?
- [ ] Was the warehouse active, same-tenant, and did variant/product lineage pass current checks?

Reservation testing:
- [ ] Reservation creation filtered candidate stock rows through `ProductVariantInventoryLineageAuthority`.
- [ ] Stock locks were deterministic by `WarehouseId`/`Id`.
- [ ] Consumable reservation required full current lineage (variant + product + warehouse + stock +
      active + unexpired). Expired/inactive-warehouse/superseded/orphaned = rejected.
- [ ] Marketplace: exact reservation ownership + exact reserved quantity matched current SQL order item.
- [ ] Marketplace: cross-tenant consumable reservation filtering used (not `First().TenantId`).
- [ ] Release/commit used cumulative applied quantities so duplicate delivery cannot over-release/over-consume.
- [ ] Cleanup did NOT release stock while provider cash authority was unresolved.

Projection / replay testing:
- [ ] Replayed stale stock events reloaded SQL authority and collapsed to no-op/tombstone when SQL no
      longer proves visibility (no resurrection).
- [ ] Duplicate EventId delivery collapsed via fence before side effects.
- [ ] CommerceHub/search overlay excluded inactive-warehouse stock from totals.
- [ ] Affiliate/CommerceHub consumers reloaded `InStock`/totals/timestamps from PostgreSQL, not Mongo.

Tenant isolation testing:
- [ ] No cross-tenant warehouse/product/variant/stock/movement/reservation read or write leaked.
- [ ] Tenant co-ownership enforced on every referenced inventory row (not just `TenantId` present).

Warehouse / transfer / adjustment testing:
- [ ] Inactive warehouse rejected for opening stock, adjustment, reservation, transfer.
- [ ] Transfer completion atomic, locked, state-gated, exactly-once.
- [ ] Adjustment required privileged role + SQL lineage + durable idempotency + non-negative result.
- [ ] Stock movement update/delete rejected (append-only enforceable in app and DB trigger).

# Common Failure Scenarios

1. Inactive-warehouse raw stock looks reservable.
   Symptom: a stock row in a disabled warehouse still has quantity and an old path accepts it.
   Correct behavior: fail closed with reservation/stock authority lost; not reservable.

2. Superseded/detached variant stock resurrected by replay.
   Symptom: a merged/split source variant's stock reappears as available after replay.
   Correct behavior: only `SemanticLifecycleState == Current` variants own stock; replay fails closed.

3. Marketplace reservation "at least enough" accepted.
   Symptom: active reservation quantity >= order quantity but ownership/quantity don't exactly match
   current SQL order item.
   Correct behavior: rejected; exact ownership + exact quantity required.

4. Single-tenant reservation filter on a multi-suborder marketplace order.
   Symptom: reservation validation uses `expectedItems.First().TenantId`, leaking or mis-scoping.
   Correct behavior: use cross-tenant consumable reservation filter.

5. Stale stock-sync event restores deleted product/variant/inactive-warehouse availability.
   Symptom: a delayed outbox message re-indexes stock after SQL truth changed.
   Correct behavior: re-read SQL authority, collapse to no-op/tombstone, exclude inactive warehouse.

6. Return receipt restores stock onto inactive warehouse / superseded variant.
   Symptom: historical delivered reservation drives stock restoration after lineage changed.
   Correct behavior: revalidate current stock ownership first; fail closed.

7. Delivery commit blocked by later public unpublish/offline.
   Symptom: commit reuses reservation-creation publication guard and refuses an already-reserved item.
   Correct behavior: inventory-lineage authority governs commit; publication drift does not block it.

8. Reservation cleanup releases stock while provider payment is unresolved.
   Symptom: direct local release from a stale snapshot.
   Correct behavior: recompute current authority under serializable tx; skip or route to payment
   lifecycle expiration; conservative release.

9. Read-model shows wrong stock briefly.
   Symptom: Mongo/Redis/Elasticsearch stock lags SQL after a commit.
   Correct behavior: ACCEPTED tradeoff (eventual consistency), not a defect — unless the projection
   path violates a SQL-recomposition rule.

10. Duplicate delivery double-releases/reserves.
    Symptom: same EventId or completion processed twice.
    Correct behavior: cumulative applied quantities + projection/idempotency fences prevent over-release.

# High Risk Areas

1. Inventory reservation vs unresolved external payment truth. Cleanup/recovery must not release stock
   while provider cash authority is unresolved; payment recovery must recompose current SQL lineage,
   not trust raw active reservations/stock rows. (PASS 3, CHECKOUT-CON-05/07.)

2. Variant semantic lifecycle. Merged/split/superseded variants must not resurrect stock/SKU/purchasability
   from stale projections or replayed events. Only current semantic owner is authoritative.
   (`ProductVariantMergeSplitAuthority`, `ProductVariantLifecycleAuthority`.)

3. Stock-owner lineage recomposition. NEVER sum raw undeleted `ProductVariantStocks`. Every availability
   and derivative stock-total path must recompose through current stock-owner lineage or it can overstate
   availability from inactive-warehouse/detached stock. This is a recurring residual gap class.

4. Warehouse authority as inventory authority (not a hint). Inactive warehouse must collapse reservation
   creation, payment retry, fulfillment reuse, AND return restoration.

5. Marketplace cross-tenant reservation composition. Multi-suborder orders need cross-tenant current
   consumable reservation filtering and exact per-tenant ownership/quantity.

6. Tenant isolation leakage on inventory rows. Composite tenant foreign keys + S76 triggers are the guard;
   verify no cross-tenant warehouse/stock/movement/reservation read/write/cache/projection/event leak.

7. CONFLATION OF RESERVATION TYPES (call this out explicitly). Treasury/payout reservations (PASS T4:
   `CommerceHubVendorEarnings.PayoutReservedAmount`, `CommerceHubPayoutItems`, `AffiliatePayoutRequests`)
   are FINANCIAL authority owned by payout/refund/settlement workflows, bounded by ledger truth. They are
   a DIFFERENT concept from inventory `InventoryReservations`. Tests and reviews that mix them up will
   misclassify defects. Inventory QA owns `InventoryReservations`; treasury reservation QA is a separate
   financial concern.

8. Append-only movement enforcement. Any path that updates/deletes `StockMovement` bypasses a hard guard.
   Treat as a critical authority violation.

9. Opening-stock one-time constraint. Re-opening or duplicating opening stock after balance/movement exists
   must be rejected; verify idempotency/row-lock behavior.

10. Dangerous primitives on inventory projections. Direct Mongo full-replace, raw Elasticsearch writes,
    Redis fanout, direct SQL/bulk mutation used by inventory consumers must sit behind reviewed guards
    (`DerivativeProjectionMutationGuard`, `ProjectionEventFence`, `SqlTruthRebuildGuard`) with SQL
    authority declared. A failing source guard is a governance defect.

# Source Documents

Primary (read before any inventory work):
- `.ai-context/ARCHITECTURE_TRUTH.md` — Inventory Authority Flow (authoritative), ownership boundaries,
  global authority rules, variant inventory/reservation lineage rule (line ~115).
- `.ai-context/PROJECT_CONTEXT.md` — inventory authority table, S76 tenant co-ownership hardening.
- `.ai-context/KNOWN_RISKS.md` — PASS 12.5 freeze, CommerceHub cart/checkout inventory containment.
- `.ai-context/CODEX_RULES.md` — POS-ADMIN-CERTIFICATION-01 PASS 3 (Inventory/Reservation Freeze),
  PASS 12.5 (Variant & Inventory Marketplace Certification Freeze), rule 9F (Variant Inventory/Reservation
  Lineage Authority).
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` — Reservation cleanup stale-candidate authority
  recomposition; checkout/certification context.

Certification findings (deep dives):
- `docs/architecture/product-inventory-reservation-certification-findings.md` (PASS 3)
- `docs/architecture/pos-warehouse-inventory-authority-certification-findings.md` (PASS P1-EB)
- `docs/architecture/marketplace-variant-inventory-certification-findings.md` (PASS 12.5)
- `docs/architecture/treasury-reservation-authority-certification-findings.md` (PASS T4 — financial, not inventory stock)

Existing test anchors (reuse, do not reinvent; these freeze invariants):
- `PointOfSale.Tests/Architecture/PosWarehouseInventoryAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/MarketplaceVariantInventoryCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ReadModelStockConsistencyArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs`
- `InventoryConsistencyServiceTests`, `InventoryReservationLifecycleIntegrationTests`
- `CommerceHubPaymentRecoveryAuthorityServiceTests`
- `StockTransfersConcurrencyHardeningTests`
- `CommerceHubCartInventoryCertificationTests`

Conflict note: PASS P1-EB (POS warehouse) and PASS 3 / PASS 12.5 (CommerceHub/marketplace/storefront)
were certified in separate passes against the SAME SQL-authoritative inventory model. No disagreement on
the core rule (SQL truth, lineage filters, fail-closed). The PASS P1-EB doc is authoritative for POS Admin
warehouse/transfer/adjustment runtime boundaries; PASS 3 / PASS 12.5 are authoritative for CommerceHub
marketplace reservation/payment-recovery/cart/checkout/search. Where a rule appears in both (e.g. "do not
sum raw stock rows", "inactive warehouse not reservable"), they reinforce each other. The treasury
reservation document (PASS T4) intentionally covers a different reservation concept and is NOT inventory
authority; treat it as adjacent financial scope.
