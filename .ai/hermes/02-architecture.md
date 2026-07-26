================================================================
SHOPIXY / PointOfSale — QA ENGINEER ONBOARDING GUIDE
Architectural Truths, Authoritative Sources, Boundaries & QA Rules
================================================================

SCOPE OF THIS GUIDE
This is a read-only distillation of .ai-context/ARCHITECTURE_TRUTH.md,
PROJECT_CONTEXT.md, KNOWN_RISKS.md, CROSS_SYSTEM_AUTHORITY_HARDENING.md,
and CODEX_RULES.md — trimmed to what a QA engineer needs to test safely.
The README is STALE and describes an early-stage POS. Do not trust it for
current behavior. When in doubt, the .ai-context files are the truth.

----------------------------------------------------------------
0. THE ONE RULE THAT EXPLAINS EVERYTHING
----------------------------------------------------------------
PostgreSQL is the AUTHORITATIVE WRITE MODEL for all business truth
(orders, payments, inventory, accounting, refunds, vendor balances,
payouts, commissions). The SQL commit boundary is the only durability
boundary. MongoDB, Redis, Elasticsearch, RabbitMQ, and every projection
are DERIVATIVE — they lag, they are caches, they are NOT truth.

Test implication: if two systems disagree, PostgreSQL wins. If a test
asserts on a Mongo/Redis/Elasticsearch value, that value can be stale by
design. Critical customer reads are allowed to self-heal from SQL; most
other reads may briefly show older state. This is accepted, not a bug.

----------------------------------------------------------------
1. ARCHITECTURAL TRUTHS (the foundation)
----------------------------------------------------------------
- Multi-tenant commerce operating platform: POS/Admin + tenant Storefront
  + multi-vendor marketplace (CommerceHub) + Affiliate network + WhatsApp
  channel, all under one backend authority model.
- Layered Clean Architecture:
  * PointOfSale.Core        — domain entities, enums, interfaces, events.
  * PointOfSale.Infrastructure — EF Core, MongoDB, Redis, Elasticsearch,
    MassTransit consumers, outboxes, reconciliation.
  * PointOfSale.API         — REST host, auth, webhooks, SignalR, Hangfire,
    hosted workers (the orchestration boundary for authoritative writes).
- Async backbone: RabbitMQ + MassTransit. Outboxes: integration (EF outbox),
  accounting, marketplace, stock-sync, payment-compensation.
- SQL commit -> outbox intent is ATOMIC. Outbox -> downstream projection is
  ASYNCHRONOUS. Read-side lag is expected and accepted.
- External providers (Stripe/Paymob/Fawry/COD/ManualWallet, banks, carriers)
  own only their own SIDE EFFECTS. A provider success/failure callback is
  EVIDENCE, never authority. Local SQL always reconciles it.
- Integrity is preferred over availability whenever external-money truth or
  authoritative backlog scope is uncertain. The system FAILS CLOSED.

----------------------------------------------------------------
2. AUTHORITATIVE DATA SOURCES (per subsystem)
----------------------------------------------------------------
Use this table to decide what you trust in a test:

  Subsystem                         Authority (SQL)            Derivative
  --------------------------------- -------------------------- ---------------------------
  Tenant config/catalog/pricing     PostgreSQL                 Mongo/Search/cache
  Category hierarchy/lineage        PostgreSQL Categories      Mongo, breadcrumbs, search
  Product media/gallery/primary img PostgreSQL ProductMedia +  Mongo, CDN, search,
                                    ProductVariants.MediaId     affiliate/CommerceHub imgs
  Product publication/online-visible PostgreSQL Products.*      Mongo, Search, cache,
                                                              homepage, affiliate docs
  Storefront simple-product price    PostgreSQL ProductUnits +  legacy product-level Price,
                                    current default unit        Mongo ProductDocument
  Variant SKU/barcode/identity       PostgreSQL ProductVariants unique indexes
  Variant lifecycle/purchasability   PostgreSQL ProductVariants + same-tenant parent Products
  Inventory stock/reservation/move   PostgreSQL ProductVariantStocks, Warehouses,
                                    InventoryReservations, movements
  Payment lifecycle state            PostgreSQL via IPaymentLifecycleService
  Order status lifecycle             PostgreSQL via authorized lifecycle boundary
  Fulfillment lifecycle             PostgreSQL via IOrderFulfillmentService
  Accounting / financial reporting   PostgreSQL immutable double-entry ledger
  CommerceHub earnings/refunds/      PostgreSQL
  payouts/vendor balances
  Affiliate commission/balance/       PostgreSQL
  payout/allocation
  Search/discovery                  Elasticsearch projection  (non-authoritative)
  Cache/locks/sessions              Redis                      (infrastructure only)
  WhatsApp session/transport        Baileys service            (transport only)
  Payment gateway execution          External provider          (reconciled into SQL)

KEY TENANT-ISOLATION RULE: tenant-id on a row is NECESSARY but not
SUFFICIENT when that row references tenant-owned parties, warehouses,
products, variants, units, invoices, or accounts. Cross-tenant foreign-key
edges are rejected at commit (TenantSaveChangesInterceptor /
ApplicationDbContext). Treat any cross-tenant data leakage as CRITICAL.

----------------------------------------------------------------
3. SYSTEM BOUNDARIES (what may NOT be bypassed)
----------------------------------------------------------------
- Lifecycle services are the ONLY legal mutation path for protected states:
  * PaymentStatus       -> IPaymentLifecycleService (impl: PaymentLifecycleService)
  * OrderStatus         -> authorized order lifecycle boundary
  * FulfillmentStatus   -> IOrderFulfillmentService
  Direct writes to these states (raw SQL, ExecuteUpdate/Delete, convenience
  shortcuts) are ARCHITECTURAL VIOLATIONS unless behind a reviewed guard.
- Authoritative integration events MUST go through IEventPublisher, never
  raw MassTransit publish (IPublishEndpoint/IBus/ISendEndpointProvider).
- Event ordering is only guaranteed PER AGGREGATE (event versioning). There
  is NO global total order. Consumers must be idempotent.
- Webhook ingress is tenantless at host layer; controllers are marked
  [AllowAnonymousTenant] and derive tenant from the SIGNED payload + SQL
  payment/shipment rows. Do not assume webhook requests carry tenant context.
- /health/live and /metrics are OUTSIDE tenant/CSRF/rate-limit middleware
  (liveness only) — test runners depend on this.
- Tenant resolution: trusted middleware first (authenticated tenant claim,
  then verified host/custom domain). Raw inbound X-Tenant-Id is STRIPPED;
  never trust it as authority. Admin/repair code must use
  ITenantContext.RequiredTenantId, not the header.

----------------------------------------------------------------
4. QA-CRITICAL RULES (test these, defend these)
----------------------------------------------------------------
R1. STALE-CLIENT FAILS CLOSED.
    Client-supplied totals, discounts, shipping/tax amounts, payment-method
    eligibility, payment split values are stale-client assertions ONLY.
    Backend recomputes authoritative values, enforces payment-method limits,
    and REJECTS mismatches before order/payment/accounting. A test that sends
    a tampered client total must expect server rejection, not a successful
    order.

R2. PAYMENT STATE IS PROTECTED.
    All PaymentStatus transitions go through IPaymentLifecycleService.
    Provider callbacks never bypass it. Suspense / manual-review /
    amount-mismatch / late-success QUARANTINE states must NOT be reported as
    terminal FAILED to customers. Unresolved captured-payment rows are
    GLOBALLY BLOCKING for outward money (payout/refund readiness fail closed).

R3. PROVIDER GATING IS PER-TENANT AND EXACT.
    Online providers gated by per-tenant TenantPaymentMethod rows. ZERO
    enabled rows = fail-closed "provider unavailable" — never falls back to
    COD/any provider. CommerceHub must validate the EXACT requested online
    provider per store set; STRIPE and PAYMOB are NOT interchangeable.

R4. CHECKOUT IS A SERIALIZABLE SQL TRANSACTION.
    Final storefront/CommerceHub order creation re-locks StoreConfigs (must
    be live), ShippingMethods, TenantPaymentMethods, variant/product rows;
    recomputes pricing; re-runs stale-client + payment-method-limit + coupon/
    promotion + affiliate-commission-policy from the IN-TRANSACTION snapshot;
    rebuilds OrderItem money snapshots; then commits. Idempotency coordinator
    binds OrderId only after proving claim-token ownership inside the EF unit
    of work. Concurrent promotion/affiliate-policy activation must NOT slip
    past this refresh.

R5. INVENTORY IS SQL-AUTHORITATIVE.
    Mutations only via InventoryConsistencyService against PostgreSQL.
    Reservations consumed on fulfillment must be ACTIVE, UNEXPIRED, backed by
    current variant/product/warehouse/stock lineage. An inactive warehouse or
    superseded variant is NOT reservable inventory even if a stock row exists.
    Reservation CLEANUP may discover stale reservations to repair them, but
    CONSUMPTION (checkout/payment/fulfillment) must filter to current lineage
    and fail closed otherwise. Never sum raw undeleted stock rows — recompose
    through current stock-owner lineage.

R6. PROJECTION / DERIVATIVE REPLAY FAILS CLOSED.
    Mongo/Search/cache/affiliate/CommerceHub/rebuild paths are TRIGGERS only.
    Before mutating derivative visibility they must reload current SQL
    authority and TOMBSTONE/DELETE when SQL no longer proves visibility.
    Stale publish/unpublish replay must not resurrect hidden/deleted products;
    stale variant/media/category/promotion replay must converge from SQL truth.
    Duplicate EventId delivery must collapse (ProjectionEventFence) before
    side effects. Tests should replay stale events and assert no resurrection.

R7. TENANT ISOLATION IS NON-NEGOTIABLE.
    Never trust client tenant ids. Verify no cross-tenant read/write/cache/
    projection/event leakage. Host resolution, event scope, cache keying, and
    projection bugs are the classic leak vectors. A tenant-admin endpoint that
    resolves scope from X-Tenant-Id, query string, or body is a defect.

R8. IDENTITY/SCHEME CLAIMS ARE EVIDENCE ONLY.
    JWT role/tenant claims, storefront/CommerceHub/customer/affiliate/super-
    admin tokens are EVIDENCE, not authority. Current SQL Users/Tenant/Super-
    Admin/CommerceHubCustomer rows decide real authority at request time.
    Stale/revoked/role-downgraded/disabled sessions must FAIL CLOSED. Token
    cutoff (TokensValidAfterUtc) rejects tokens issued at/before cutoff.

R9. RECONCILIATION STATES ARE INTENTIONAL, NOT LIMBO.
    Processing, ReconciliationRequired, unknown-outcome, suspense, manual-
    review are explicit safe states. Never force uncertain external outcomes
    into false terminal states to "restore availability." Never bypass
    readiness gates.

R10. DANGEROUS PRIMITIVES ARE GOVERNED, NOT BANNED.
    Direct Mongo full-replace (ReplaceOneAsync), raw Elasticsearch writes,
    Redis pattern invalidation, direct SQL/bulk mutation, raw bus publish are
    reviewed primitives behind guards (DerivativeProjectionMutationGuard,
    ProjectionEventFence, SqlTruthRebuildGuard). If a change adds one, it must
    also add runtime guards + focused tests + doc. A failing source guard is a
    real governance defect.

----------------------------------------------------------------
5. RISK CLASSIFICATION (how to report findings)
----------------------------------------------------------------
Use the CODEX_Rules vocabulary so your reports match the repo's language:
- CONFIRMED_RISK   — direct evidence (code/schema/runtime/test/doc gap).
- THEORETICAL_RISK — plausible but not found active in the inspected path.
- MITIGATED_RISK   — closed by a design control; verify it holds.
- ACCEPTED_TRADEOFF — intentional consistency/latency/availability cost
                     (e.g. read-model lag, fail-closed financial gating under
                     uncertainty, reconciliation states). Do NOT re-report
                     these as bugs. Read KNOWN_RISKS.md first; extending an
                     existing entry is better than duplicating it.
Always cite concrete code/doc evidence. Lead with confirmed risks.

----------------------------------------------------------------
6. HIGH-RISK AREAS TO PRIORITIZE IN TESTING
----------------------------------------------------------------
1. Payment lifecycle bypass (direct PaymentStatus/Order/Fulfillment writes).
2. Stale-client authority (server recompute; never trust client arithmetic).
3. Outbox/reconciliation fail-closed (unresolved money blocks outward motion;
   suspense/quarantine must not surface as terminal FAILED/success).
4. Tenant isolation leakage (esp. affiliate/CommerceHub/payout scope).
5. Idempotency/replay (webhooks, checkout retries, stock-sync, marketplace
   outbox must be safely replayable — ProcessedEvents, claims, fences).
6. Serializable checkout transaction (concurrent promotion/affiliate-policy
   activation must not slip past in-transaction refresh).
7. Inventory reservation vs unresolved external payment truth.
8. Variant semantic lifecycle (merged/split/superseded variants must not
   resurrect purchasability/stock/SKU ownership from stale projections).

----------------------------------------------------------------
7. EXISTING TEST ANCHORS (reuse, don't reinvent)
----------------------------------------------------------------
The repo already has large certification suites. Before writing new tests,
check these to avoid duplicating frozen invariants:
- PointOfSale.Tests/Architecture/*  (authority/lifecycle/payment/affiliate/
  commercehub certification + regression-freeze tests)
- CommerceHub checkout/order/fulfillment/discovery certification tests
- InventoryConsistencyServiceTests, InventoryReservationLifecycleIntegrationTests
- StorefrontTenantIsolationTests, StorefrontProductionBlockerFixTests
- FinancialIntegrityRiskHardeningTests, AffiliatePayoutRecoveryCertificationTests
- Chaos validation: SemanticConvergenceChaosValidationTests,
  SuperAdminOperationalChaosCertificationTests
Regression-freeze tests (CrossSystemAuthorityRegressionGuardTests,
TenantIsolationCertificationFreezeTests, SuperAdminPlatformAuthority-
CertificationTests) failing after a change = governance defect until reviewed.

----------------------------------------------------------------
8. QA MENTAL MODEL (one paragraph)
----------------------------------------------------------------
When you test any mutation path, ask: (a) Is PostgreSQL the thing that
actually decided truth? (b) Did the change go through the correct lifecycle
service / authority boundary / outbox, or did it sneak around it? (c) Is the
tenant scope proven by trusted resolution, not by a client header? (d) If a
provider callback or a replayed event arrives late or stale, does the system
recompose current SQL truth and fail closed instead of resurrecting old
visibility or faking a terminal state? If the answer to any is "no", that is
the bug — not the read-model lag you might first notice.

----------------------------------------------------------------
SOURCE FILES (read before working any area)
----------------------------------------------------------------
.ai-context/ARCHITECTURE_TRUTH.md          (authoritative spec)
.ai-context/PROJECT_CONTEXT.md             (subsystems, tech, boundaries)
.ai-context/KNOWN_RISKS.md                 (residual/accepted risks)
.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md (fixed gaps + rules)
.ai-context/CODEX_RULES.md                 (mandatory rules + workflow)
docs/architecture/*                        (per-subsystem deep dives)
================================================================
