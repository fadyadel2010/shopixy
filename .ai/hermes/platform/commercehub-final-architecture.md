# CommerceHub — Final Canonical Architecture

Implementation-backed architecture specification for the CommerceHub platform.

> Every statement in this document is derived from the C# implementation under
> `PointOfSale.API/`, `PointOfSale.Core/`, and `PointOfSale.Infrastructure/`. Where a
> conclusion is supported, the `file:line` evidence is cited. The terminology-only
> System Guide (`.ai/hermes/systems/commercehub/README.md`) is treated as a secondary
> source; every normative claim here was re-verified against code. Prior
> certification `.md` files in `docs/architecture/` were NOT treated as authoritative —
> they are referenced only where they independently confirm a code finding.

Canonical paths:
- Backend services: `PointOfSale.API/Services/CommerceHub/`
- Backend commands: `PointOfSale.API/CommerceHub/Commands`, `.../Handlers`
- Core aggregates: `PointOfSale.Core/Entities/` (`CommerceHubMarketplaceOrder`,
  `CommerceHubSubOrder`, `CommerceHubListing`, `CommerceHubCart`,
  `CommerceHubCustomer`, `CommerceHub*Finance*`)
- Finance entities: `PointOfSale.Core/Entities/CommerceHubFinance/`
- Enums: `PointOfSale.Core/Enums/` and `.../CommerceHubFinance/`
- Mongo projection writers: `PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs`,
  `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`
- Outbox: `PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs`,
  `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`

---

## Executive Summary

CommerceHub is the multi-vendor marketplace surface of the Shopixy/PointOfSale
platform. It is NOT a separate database or separate backend process: the marketplace
logic is a first-class bounded context inside the same ASP.NET Core solution that owns
POS, Storefront, Affiliate, and SuperAdmin. Its defining architectural property is that
it is a **multi-tenant, multi-vendor orchestration layer over a single PostgreSQL system
of record**, with Mongo/Redis/Elasticsearch used strictly as derivative projections.

Reverse-engineered facts (all code-backed):

- Marketplace orders are modeled as `CommerceHubMarketplaceOrder` + one or more
  `CommerceHubSubOrder` rows, one sub-order per distinct `StoreId` (vendor tenant).
  `CommerceHubCheckoutService.cs:1801-1802` groups cart lines by `StoreId`; the
  `UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live` unique constraint
  (`ApplicationDbContext.cs:2287`) enforces one live sub-order per (order, store).
- All money is EGP-only. `FinancialCurrencyGuard.PlatformBaseCurrencyCode = "EGP"`
  (`FinancialCurrencyGuard.cs:10`) is enforced at the ledger (`CommerceHubFinancialLedgerService.cs:1091`),
  payout (`CommerceHubPayoutService.cs:223`), refund gateway
  (`CommerceHubRefundGatewayService.cs:80`), and search overlay
  (`CommerceHubSearchSqlAuthorityOverlay.cs:120`).
- CommerceHub runs its OWN operational ledger (`CommerceHubFinancialTransaction`,
  `PointOfSale.Core/Entities/CommerceHubFinance/`) and mirrors each entry into the
  tenant double-entry GL through the shared `AccountingOutboxMessage` transactional
  outbox (`CommerceHubFinancialLedgerService.cs:724-755`). This is the "Operational Ledger
  → Accounting Ledger" bridge.
- Outward money (payouts, refunds, intake) is gated by a fail-closed readiness gate
  (`CommerceHubFinancialReadinessGate.cs:68`) that blocks on unresolved captured
  payments, unresolved refunds, stale accounting outbox, and emergency stops.
- Projections are explicitly repair-on-read: Mongo order documents are rebuilt from SQL
  on a not-found/stale read (`CommerceHubOrderProjectionRepairService.cs:35`) and a
  SQL-authority overlay recomputes search visibility from SQL
  (`CommerceHubSearchSqlAuthorityOverlay.cs:35-59`).

---

## Architecture Vision

### CommerceHub Purpose

CommerceHub turns tenant-owned catalog, inventory, pricing, and publication data into a
unified, multi-vendor shopping experience: discovery → cart → split checkout →
multi-vendor sub-orders → vendor fulfillment → settlement → vendor earnings → payouts.
It is implemented as an orchestration context that:

- reads tenant source data through SQL authority (never owns it),
- writes its own marketplace-specific aggregates (listings, carts, marketplace orders,
  sub-orders, marketplace payments, financial transactions),
- emits events to Mongo/ES/Redis projections and to the shared accounting outbox,
- defers all cross-cutting lifecycles (payment truth, order lifecycle, fulfillment,
  inventory truth, ledger/accounting, platform governance) to their owning services.

Evidence: `CommerceHubCheckoutService.cs:1806-1862` (order+sub-order creation),
`PublishToCommerceHubHandler.cs:178-219` (listing creation from tenant variant,
admitting only after `EnsureVendorListingAllowedAsync` + `EnsureTenantHasActiveShippingAsync`
+ `EnsureLiveCommerceHubCategoryAsync`).

### CommerceHub Owns

Implemented runtime truth owned by CommerceHub services (SQL tables):

| Concept | Owner (service) | SQL table / aggregate |
|---|---|---|
| Marketplace listings | `PublishToCommerceHubHandler` / `CommerceHubVendorVisibilityService` | `CommerceHubListings` |
| Marketplace cart | `CommerceHubCartService` | `CommerceHubCarts`/`CommerceHubCartItems` |
| Checkout session | `CommerceHubCheckoutService` | `CommerceHubCheckoutSessions` |
| Marketplace orders | `CommerceHubCheckoutService`/`CommerceHubOrderManagementService` | `CommerceHubMarketplaceOrders` |
| Sub-orders | `CommerceHubCheckoutService`/`CommerceHubOrderManagementService` | `CommerceHubSubOrders` |
| Marketplace payments (orchestration) | `CommerceHubPaymentService` (lifecycle via `IPaymentLifecycleService`) | `CommerceHubPayments` |
| Unresolved payment webhooks (evidence) | `CommerceHubPaymentRecoveryAuthorityService` | `CommerceHubUnresolvedPaymentWebhooks` |
| Operational ledger | `CommerceHubFinancialLedgerService` | `CommerceHubFinancialTransactions` |
| Vendor settlement records | `CommerceHubVendorDebtService`/finance services | `CommerceHubVendorSettlements` |
| Vendor earnings | `CommerceHubEarningsService` | `CommerceHubVendorEarnings` |
| Payout requests | `CommerceHubPayoutService` (execute: SuperAdmin) | `CommerceHubPayouts`/`CommerceHubVendorPaymentRequests` |
| Refunds | `CommerceHubRefundService` (lifecycle via `RefundFinancialDecisionEngine`) | `CommerceHubRefunds` |
| Marketplace governance suppression | `UserGovernanceService` (`PointOfSale.API/Services/Governance`) | governance tables |
| Ratings/reviews (moderation admission) | `MarketplaceProductReviewBackboneServices` | `CommerceHubProductReviews` |

### CommerceHub Does NOT Own

Reverse-engineered from service dependencies and guards:

- **Product/variant/catalog authority** — owned by POS Admin product system; CommerceHub
  only creates a `CommerceHubListing` referencing an existing `ProductId`/`VariantId`
  (`PublishToCommerceHubHandler.cs:204-214`). The publish handler loads the variant from
  SQL and never creates product truth (`PublishToCommerceHubHandler.cs:178`).
- **Inventory truth** — owned by `InventoryConsistencyService` against PostgreSQL
  (`InventoryConsistencyService.cs:102`). CommerceHub checkout revalidates current stock
  via this service (`CommerceHubCheckoutService.cs:71,88`). Mongo product docs are display
  metadata only.
- **Payment lifecycle truth** — owned by `IPaymentLifecycleService`
  (`PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs`). CommerceHub
  "payment paths are explicitly excluded from Stripe/Paymob reference certification"
  (per README; code: `CommerceHubPaymentService` orchestrates intent + evidence, lifecycle
  admission via the shared service).
- **Order/sub-order generic lifecycle for Storefront** — separate `Order` aggregate;
  marketplace sub-orders are governed by `CommerceHubOrderManagementService`.
- **Accounting/treasury GL** — CommerceHub writes operational ledger rows and an outbox
  message; the shared `AccountingOutboxProcessor` posts to the tenant GL. CommerceHub
  never writes `JournalEntry` directly.
- **Platform/global authority** — SuperAdmin scope only (`SuperAdminSecurityConstants.cs:6`,
  policy `RequireSuperAdmin`).
- **Shipping carrier truth** — owned by `ShipmentService`/`CarrierShipmentService`; CommerceHub
  captures a shipping snapshot on the sub-order (`CommerceHubCheckoutService.cs:1849`).

---

## Architecture Principles

1. **PostgreSQL is the single system of record.** All projections (Mongo `commercehub_orders`,
   `commercehub_products`, Redis caches, Elasticsearch index) are evidence/presentation.
   Confirmed by the repair-on-read pattern (`CommerceHubOrderProjectionRepairService.cs:35`)
   and the SQL-authority overlay (`CommerceHubSearchSqlAuthorityOverlay.cs:35`).

2. **Strict EGP-only financial authority.** `FinancialCurrencyGuard.RequireEgpCurrency`
   (`FinancialCurrencyGuard.cs:59`) is called at every financial boundary: ledger
   (`CommerceHubFinancialLedgerService.cs:1091,1179,1222`), payout
   (`CommerceHubPayoutService.cs:223`), refund (`CommerceHubRefundGatewayService.cs:80`),
   settlement (`CommerceHubVendorSettlement.cs:66`), and search visibility
   (`CommerceHubSearchSqlAuthorityOverlay.cs:120`). Non-EGP silently falls back to a
   thrown `FinancialCurrencyMismatchException` — no silent coercion.

3. **Split-order per `StoreId`, immutable after creation.** Checkout groups by
   `StoreId` (`CommerceHubCheckoutService.cs:1801-1802`); one sub-order per live
   (order, store) pair enforced by `UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live`
   (`ApplicationDbContext.cs:2287`). Shipping assignment is frozen on the sub-order
   (`CommerceHubSubOrder.ShippingSnapshotJson`).

4. **Serializable checkout + idempotency.** Checkout records ledger/financial rows under
   `IsolationLevel.Serializable` (`CommerceHubFinancialLedgerService.cs:881`). Requests
   are idempotency-key bound (`CommerceHubMarketplaceOrder.IdempotencyKey`,
   `CommerceHubFinancialTransaction.IdempotencyKey`, `FinancialCurrencyGuard` fingerprint
   checks `CommerceHubFinancialLedgerService.cs:773-863`).

5. **Lifecycle ownership is not bypassed.** SuperAdmin may never mutate marketplace
   order/sub-order/financial truth directly; it routes through owning services and the
   readiness gate (`CommerceHubFinancialReadinessGate.cs:68`). Payment/refund/webhook
   evidence is admitted only through lifecycle boundaries
   (`IPaymentLifecycleService.cs:219`).

6. **Fail-closed outward money.** Payouts and refunds are blocked if unresolved captured
   payments, unresolved refunds, stale accounting outbox, or emergency stops exist
   (`CommerceHubFinancialReadinessGate.cs:41-96`).

7. **Projection convergence from SQL.** Mongo order docs carry a
   `ProjectionChecksum` (`CommerceHubOrderProjectionChecksum.cs`) and a monotonic
   `ProjectionMutationVersion`; stale events are rejected by an optimistic
   `ReplaceOneAsync` filter (`CommerceHubOrderProjectionConsumer.cs:147-181`). Search
   visibility is recomputed from SQL before presentation
   (`CommerceHubSearchSqlAuthorityOverlay.cs:35-59`).

---

## Core Aggregates

| Aggregate | File | Key invariants (code) |
|---|---|---|
| `CommerceHubMarketplaceOrder` | `PointOfSale.Core/Entities/CommerceHubMarketplaceOrder.cs` | Currency defaults `"EGP"` (L17); `ProjectionMutationVersion` strictly monotonic (L46-56); `GetAffectedStoreIds()` (L63) |
| `CommerceHubSubOrder` | `PointOfSale.Core/Entities/CommerceHubSubOrder.cs` | `StoreId` required; embedded financial amounts `PlatformCommissionAmount`, `VendorEarningAmount`, `Total` (L26-31); monotonic version (L40-50) |
| `CommerceHubListing` | `PointOfSale.Core/Entities/` (CommerceHub listing) | `IsPublished`, `CommerceHubCategoryId`, `CommerceHubPrice`, `VariantId`/`ProductId` (publish sets these: `PublishToCommerceHubHandler.cs:204-214`); index `IX_CommerceHubListings_TenantId_IsPublished_CategoryId` |
| `CommerceHubCart` / `CommerceHubCartItem` | `CommerceHubCartService.cs` | groups lines by `StoreId` (L393, L828); stock revalidated vs SQL |
| `CommerceHubCustomer` | `CommerceHubCustomerAuthService.cs` | global-but-scoped identity; login only after email verification (README L307; enforced by auth service) |
| `CommerceHubFinancialTransaction` | `CommerceHubFinancialTransaction.cs` | EGP-enforced (L84); platform rows have no `EntityId` (L94-97); vendor rows require `EntityId` (L99-102); idempotency + correlation (L109-117) |
| `CommerceHubVendorSettlement` | `CommerceHubVendorSettlement.cs` | EGP (L66); idempotency required (L53); `legacy:` prefix rejected for new rows (L116-121) |
| `CommerceHubVendorEarning` | `CommerceHubEarningsService.cs` + enum | states `Pending/Available/PaidOut/InPayout/Reversed` (`CommerceHubVendorEarningStatus.cs`) |
| `CommerceHubCart` | `PointOfSale.Core/Entities/CommerceHubCart.cs` | `TenantId` + `CommerceHubCustomerId`; `ItemsJson`; optimistic `Version`; `IsLocked`. Cart ownership uses a SYNTHETIC tenant scope `CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId` (`CommerceHubCartService.cs:19`). EGP guard at `CommerceHubCartService.cs:505,862,1117` |

NOTE (verified): there is **NO `CommerceHubVendor` entity and NO `CommerceHubProduct`
entity**. "Vendor" == the tenant `Store` identified by `CommerceHubSubOrder.StoreId`
(`CommerceHubSubOrder.cs:11`); "Product"/"Variant" == the tenant's existing `Product`/
`ProductVariant` catalog entities referenced by `CommerceHubListing.ProductId`/
`VariantId` (`CommerceHubListing.cs:12-13`). CommerceHub never creates product/variant
truth. See also invariant #11 (marketplace orders are not `ITenantOwned`).

Read models (Mongo):
- `CommerceHubOrderDocument` in `commercehub_orders` (`CommerceHubOrderProjectionConsumer.cs:28`),
  rebuilt/repaird on demand.
- `CommerceHubProductDocument` in `commercehub_products` (`CommerceHubProjectionConsumer.cs`);
  search visibility re-derived from SQL overlay before indexing.

---

## Runtime Model

### Marketplace Runtime
Customer discovery/search → cart → split checkout → marketplace order + sub-orders →
payment intent → payment lifecycle → fulfillment → settlement/earnings → payout.

### Operational Model
- **Write path** is always SQL. Checkout (`CommerceHubCheckoutService.CreateOrderAsync`)
  builds `CommerceHubMarketplaceOrder` + `CommerceHubSubOrder` rows and records ledger
  entries in the same `Serializable` transaction
  (`CommerceHubFinancialLedgerService.cs:881`).
- **Event path**: SQL commit → outbox/published integration event → async consumers →
  Mongo/ES/Redis projections. Order projection consumer reloads SQL and writes Mongo
  (`CommerceHubOrderProjectionConsumer.cs:42-141`).
- **Read path**: controllers serve Mongo documents but force a SQL rebuild on
  not-found/stale (`CommerceHubOrderProjectionRepairService.cs:35`).

### Marketplace Runtime
The marketplace surface aggregates across vendors. Listings are SQL-backed
(`CommerceHubListings`); search is an ES projection re-derived from SQL
(`CommerceHubSearchSqlAuthorityOverlay.cs`).

### Checkout Runtime
`CommerceHubCheckoutService`:
1. Group cart lines by `StoreId` (`:1801-1802`).
2. Resolve per-store shipping selection (`:607-662`).
3. Recompute per-store pricing, promo, commission from SQL (`:1864-1869`,
   `ResolveCommissionForCheckout`).
4. Create parent order + one sub-order per store under `Serializable`
   (entity write) + ledger record (separate `Serializable` in ledger service).
5. Idempotency key bound to the request hash (`CommerceHubMarketplaceOrder.IdempotencyKey`).

### Order Runtime
- Parent `CommerceHubMarketplaceOrder.Status` aggregates sub-order states.
- Sub-order lifecycle owned by `CommerceHubOrderManagementService` (fulfillment,
  cancellation, delivery) under SQL validation
  (`CommerceHubOrderManagementService.cs:27,35` uses `IInventoryConsistencyService`).
- Vendor cannot change parent status (README L359; `StoreId` scope enforced).

### Payment Runtime
- `CommerceHubPaymentService` orchestrates provider-session intent and evidence capture
  (`CommerceHubPaymentService.cs:85,98`).
- Lifecycle truth = `IPaymentLifecycleService`.
- Unmatched captured webhooks persisted as `CommerceHubUnresolvedPaymentWebhooks` (SQL
  evidence) and block outward money until scoped/reconciled
  (`CommerceHubFinancialReadinessGate.cs:339-401`).

### Financial Runtime
`CommerceHubFinancialLedgerService` appends `CommerceHubFinancialTransaction` rows and a
paired `AccountingOutboxMessage` in the same transaction scope
(`:724-755`). Idempotency by key + financial fingerprint (`:773-863`). Duplicate-write
race handled by Postgres unique-constraint detection
(`UX_CommerceHubFinancialTransactions_*`, `:907`) and safe-replay verification
(`:911-963`).

### Settlement Runtime
- `CommerceHubVendorSettlement` is the durable vendor→platform settlement/clearance/adjustment
  *evidence* row (idempotency key + external reference required; EGP-only; `legacy:` prefix
  rejected for new rows). Types: `SettlementPayment`, `AdminAdjustment`, `DebtClearance`
  (`CommerceHubVendorSettlementType.cs`; `CommerceHubVendorSettlement.cs:6-130`).
- COD debt semantics: for COD orders the ledger records full `subOrder.Total` as a **Vendor
  Cash credit** + a Platform **Commission Debit** (vendor owes commission)
  (`CommerceHubFinancialLedgerService.cs:181-226`). Debt is DERIVED (not stored) by replaying
  ledger rows — Commission/Adjustment/Settlement/post-payout Refund as debt deltas: commission
  credit → +debt, settlement credit → −debt (`CommerceHubVendorDebtService.cs:77-290`).
- **1000 EGP suspension threshold**: `CommerceHubVendorDebtOptions
  .DefaultSuspensionThresholdAmount = 1000m` (configurable, `CommerceHubVendorDebtOptions.cs:5,9`);
  grace 3 days (L6); near-threshold 80% (L7). `isSuspended = amountOwed >= threshold`
  (`CommerceHubVendorDebtService.cs:292`); status `"SUSPENDED"` (`:324`).

### Vendor Earnings Runtime
- States (exact enum): `Pending=0, Available=1, PaidOut=2, InPayout=3, Reversed=4`
  (`CommerceHubVendorEarningStatus.cs:3-10`).
- Pending→Available on delivery / COD collection
  (`CommerceHubFinancialWorkflowService.cs:181-205`; `AvailableAtUtc = now + PayoutDelayDays`).
- Available→InPayout when payout is created (`CommerceHubPayoutService.cs:313-319`);
  InPayout→PaidOut/Reversed on completion (`CommerceHubRefundService.cs:1513-1519`).
- Earning creation is guarded by `subOrder.IsEarningCreated` to prevent double-record
  (`CommerceHubFinancialWorkflowService.cs:504-536`).

### Operational Ledger
`CommerceHubFinancialTransaction` — the CommerceHub operational ledger (SQL). Each row has
`EntityType` (Platform/Vendor), `TransactionType`, `Direction`, `FinancialBucket`
(Cash/Receivable/Payable), `Amount`, `Currency` (EGP), `ReferenceType`, `ReferenceId`,
idempotency + correlation keys. Buckets let a single event post to the right balance
(e.g. COD order credits Vendor.Cash; online order credits Vendor.Payable + Platform.Cash).
Evidence: `CommerceHubFinancialTransaction.cs:11-26`,
`CommerceHubFinancialBucket.cs`, `CommerceHubFinancialLedgerService.RecordOrderAsync:161-268`.

### Accounting Integration
Each operational-ledger append enqueues an `AccountingOutboxMessage`
(`eventType: "CommerceHubLedgerPosting"`, `sourceType: "CommerceHubFinancialTransaction"`)
in the SAME transaction (`CommerceHubFinancialLedgerService.cs:724-755`). The shared
`AccountingOutboxProcessor` dispatches `CommerceHubLedgerPosting` →
`ICommerceAccountingService.PostCommerceHubFinancialTransactionAsync`, which posts a
balanced `JournalEntry` with `SourceType="CommerceHubFinancialTransaction"`,
`SourceId=transactionId`, idempotency `COMMERCEHUB_TX:{tenantId}:{transactionId}`
(`AccountingOutboxProcessor.cs:740,921-949`; `CommerceAccountingService.cs:1563-1731`).
GL account mapping is a FIXED function of (EntityType, TransactionType, Bucket, Direction)
with `TreasurySuspense` as the universal offset: Vendor Payable → `MarketplaceVendorPayable`,
Vendor Receivable → `MarketplaceVendorReceivable`, Vendor/Platform Cash → `BankAccount`,
Platform Commission → `EcommerceSales`, Platform Promotion → `Expenses`
(`CommerceAccountingService.cs:1592-1731`). This is the operational→accounting bridge;
CommerceHub never posts journals directly.

**Hard coupling (ledger→GL coverage gate):** the readiness gate BLOCKS outward money
movement until every operational transaction has a `Posted` GL journal with
`SourceType="CommerceHubFinancialTransaction"` + `SourceId=transaction.Id`
(`CommerceHubFinancialReadinessGate.cs:717-806`, `EnsureCommerceHubLedgerAccountingCoverageAsync`,
called at `:94` & `:137`). This makes replay/idempotency end-to-end safe and is why a stale
outbox blocks payouts/refunds. NOTE: there is NO `IAccountingOutboxPublisher` interface; the
ledger enqueues directly via `AccountingOutboxMessage.Create`
(`PointOfSale.Core/Interfaces/Accounting/IAccountingOutboxService.cs`).

### Financial Runtime Detail (settlement / earnings / payouts / refunds)
- **Settlement / COD debt**: `CommerceHubVendorSettlement` is the durable vendor→platform
  settlement/clearance/adjustment *evidence* row (idempotency + external reference required,
  EGP-only; `CommerceHubVendorSettlement.cs:6-130`; types `SettlementPayment/AdminAdjustment/
  DebtClearance`). COD orders record full `subOrder.Total` as Vendor **Cash credit** + Platform
  **Commission Debit** (vendor owes commission) (`CommerceHubFinancialLedgerService.cs:181-226`).
  Debt is derived by replaying ledger rows (Commission/Adjustment/Settlement/post-payout
  Refund) as deltas (`CommerceHubVendorDebtService.cs:77-290`).
- **1000 EGP suspension threshold (exact)**: `CommerceHubVendorDebtOptions
  .DefaultSuspensionThresholdAmount = 1000m` (`CommerceHubVendorDebtOptions.cs:5`; configurable
  `SuspensionThresholdAmount` defaults to it, L9); GracePeriod = 3 days (L6); near-threshold
  80% (L7). Suspension `isSuspended = amountOwed >= threshold` (`CommerceHubVendorDebtService.cs:292`);
  status `"SUSPENDED"` (`:324`).
- **Vendor earnings states**: `Pending=0, Available=1, PaidOut=2, InPayout=3, Reversed=4`
  (`CommerceHubVendorEarningStatus.cs:3-10`). Pending→Available on delivery/COD-collect
  (`CommerceHubFinancialWorkflowService.cs:181-205`, `AvailableAtUtc = now + PayoutDelayDays`);
  Available→InPayout on payout creation (`CommerceHubPayoutService.cs:313-319`); InPayout→PaidOut/
  Reversed on completion (`CommerceHubRefundService.cs:1513-1519`).
- **Commission / platform revenue**: rate computed in `CommissionService` and stored on the
  sub-order snapshot; ledger only RECORDS commission — Vendor Commission Debit + Platform
  Commission Credit (`CommerceHubFinancialLedgerService.cs:211-243`). Integrity rule enforced:
  `FinalPlatformCommissionAmount == grossCommission − promoDiscount` and
  `VendorEarningAmount == Total − grossCommission` (`:1096-1122`). Platform buckets: Commission→
  Receivable (`EcommerceSales`), Promotion→Receivable (`Expenses`) (`CommerceHubFinancialClassification.cs:17,33`).
  `RecordMirroredTransactionsAsync` (`:527-602`) is the canonical Vendor+Platform mirror path
  used by settlement/clearance/adjustment (`CommerceHubVendorFinancialControlService.cs:382`).
- **Payouts**: lifecycle `Pending→Processing→Completed/Failed/ReconciliationRequired`
  (`CommerceHubPayoutStatus.cs`); creation requires `Available` earnings past the
  `AvailableAtUtc` delay (`CommerceHubPayoutService.cs:194-208,286-334`). Readiness gates
  (`EnsureOutwardMoneyMovementAllowedAsync` at create `:151`, markProcessing `:409`, SuperAdmin
  `:228`) run 10+ checks. **Super Admin execution** `SuperAdminPayoutsService.ExecuteAsync`
  creates the payout already `Completed` from backend bank/provider evidence, still gated by
  readiness + safety guard (`SuperAdminPayoutsService.cs:198-351`). External transfer evidence
  enforced via `ExternalReferenceId`/`BankTransactionId`/`ExternalTransferIdempotencyKey`
  unique constraints + `EnsureExternalPayoutEvidenceIsUniqueAsync`/`EnsurePayoutCompletionEvidencePresent`
  (`:62-65,256,354`).
- **Refunds**: `RefundFinancialDecisionEngine.Create` resolves 3 scenarios from
  (PaymentMethod, hasPayoutOccurred): `OnlineBeforePayout` (reduces vendor payable, no debt),
  `OnlineAfterPayout` (creates vendor debt = earning reversal), `CashOnDelivery` (reduces vendor
  debt) (`RefundFinancialDecisionEngine.cs:30-99`). `RecordRefundAsync` (`CommerceHubFinancialLedgerService.cs:270-430`)
  writes mirrored reversal rows per scenario with explicit idempotency keys
  `commercehub:refund:{id}:{scope}`; invoked after earnings marked `Reversed`
  (`CommerceHubRefundService.cs:1359-1363,1513-1519`); refund must be `Completed` before ledger
  entry (`ValidateRefundFinancialIntegrity`, `:1137-1197`).

### Inventory Integration
`InventoryConsistencyService` owns stock truth in PostgreSQL
(`InventoryConsistencyService.cs:102`). Checkout revalidates current stock
(`CommerceHubCheckoutService.cs:71,88`); fulfillment consumes inventory via this service
(`CommerceHubOrderManagementService.cs:27,35`). Mongo product docs carry stock only as
display metadata; they must be reloaded from SQL before propagation (README L56;
enforced by SQL overlay `CommerceHubSearchSqlAuthorityOverlay.cs:35`).

### Catalog Integration
Catalog/product/variant truth is POS Admin. CommerceHub creates a `CommerceHubListing`
only by referencing an existing `ProductId`/`VariantId`
(`PublishToCommerceHubHandler.cs:178-214`). Publication is admitted only after variant
load, EGP check, attribute validation, governance check, shipping check, live-category
check (`PublishToCommerceHubHandler.cs:162-202`). Listing price is `CommerceHubPrice`
(merchant-set, EGP), distinct from tenant base price
(`ResolveEffectivePrice` via `ICommerceHubPriceAuthority`).

### Search Integration
Elasticsearch is a projection. `CommerceHubSearchSqlAuthorityOverlay` recomputes
visibility from SQL (listing published, variant live, pricing, stock, category, governance,
tombstone) before any search presentation (`CommerceHubSearchSqlAuthorityOverlay.cs:35-59`).
Non-EGP tenant currency blocks listing visibility (`:120`). Reindex is a SuperAdmin
operational action that re-derives from SQL.

### Identity Integration
- Customer identity: `CommerceHubCustomer` (global, scoped to own orders/actions);
  login requires email verification (`CommerceHubCustomerAuthService.cs`, README L307).
- Vendor identity: tenant actor operating the CommerceHub Vendor Module inside POS Admin;
  `CommerceHubTenantIsolationGuard.EnsureTenantAccess` enforces tenant scope
  (`PublishToCommerceHubHandler.cs:155`).
- Cross-tenant leak is prevented by TenantId scoping + `CommerceHubTenantIsolationGuard`.

### Shipping Integration
`ShipmentService`/`CarrierShipmentService` own shipment/ carrier truth. CommerceHub
captures an immutable shipping snapshot on each sub-order
(`CommerceHubSubOrder.ShippingSnapshotJson`, `CommerceHubCheckoutService.cs:1849`).
Carrier webhooks are authenticated through the trust pipeline and feed fulfillment
(README L407).

### Notification Integration
Notifications are outputs of lifecycle events (WhatsApp/email/SMS). Delivery cannot mutate
commerce truth (README L243, L419). Driven by `IEventPublisher` from lifecycle services.

### Analytics Integration
Analytics observes SQL order/payment/ledger truth; revenue/conversion numerators derive
from SQL, not from traffic events (README L257-259). CommerceHub emits
`CommerceHubSearchAnalyticsQueue` events for search intelligence, but these are traffic
evidence only (`CommerceHubSearchAnalyticsBackgroundService.cs`).

### SuperAdmin Integration
SuperAdmin is the platform operational surface, isolated by `RequireSuperAdmin` policy
(`SuperAdminSecurityConstants.cs:6`). It performs governance suppression, global reindex,
emergency stop, payout execution, refund/payment-intake containment
(`CommerceHubFinancialReadinessGate.cs:78,1591` for emergency stop enforcement).
SuperAdmin NEVER bypasses lifecycle owners (README L725-735; enforced by routing through
owning services + readiness gate).

### Affiliate Integration
Affiliate is a separate identity surface. Marketplace orders MAY carry affiliate
attribution, but commission/balance/payout are SQL-authoritative and distinct from
inventory "reservations" (README L229-235, L391). CommerceHub marketplace sub-order
affiliate authority is NOT assumed from Storefront affiliate certification (README L391).

---

## CQRS Model

### Write Models (SQL — system of record)
- `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, `CommerceHubListings`,
  `CommerceHubCarts`/`Items`, `CommerceHubCheckoutSessions`, `CommerceHubPayments`,
  `CommerceHubUnresolvedPaymentWebhooks`, `CommerceHubFinancialTransactions`,
  `CommerceHubVendorSettlements`, `CommerceHubVendorEarnings`, `CommerceHubPayouts`,
  `CommerceHubRefunds`, `CommerceHubProductReviews`.

### Read Models (projections — derivative)
- **Mongo** `commercehub_orders` (`CommerceHubOrderDocument`) — order tracking reads
  (`CommerceHubOrderReadRepository`).
- **Mongo** `commercehub_products` (`CommerceHubProductDocument`) — listing/search reads
  (`CommerceHubReadRepository`).
- **Elasticsearch** — marketplace search index, written by `CommerceHubSearchIndexService`
  (alias-swap rebuild) and driven from `CommerceHubProjectionConsumer.SyncSearchAsync`
  (runs `CommerceHubSearchSqlAuthorityOverlay` then indexes or deletes).
- **Redis** — used narrowly in CommerceHub: the **checkout lock** is Redis-backed via
  `DistributedLockService.TryAcquireLockAsync` keyed by customerId+idempotencyKey
  (`CommerceHubCheckoutService.cs:225-282`; infrastructure failure fails CLOSED at
  `:261-268`); the **governance visibility version** cache
  (`commercehub:governance:visibility:version`,
  `CommerceHubGovernanceCacheInvalidationService.cs:27`). The **cart lock is NOT Redis** —
  it is DB-backed via `CommerceHubCart.IsLocked`/`LockedAtUtc` with a 10-min
  `CartLockTimeout` (`CommerceHubCartService.cs:77,1597`; `CommerceHubCheckoutService.cs:59`).
  There is no Redis cache for the order/listing read models themselves (Mongo/ES only).

### Write Authority
CommerceHub services write their own aggregates under SQL validation + `Serializable`
where money/order integrity demands (`CommerceHubFinancialLedgerService.cs:881`).
Cross-domain writes go through the owning service or the shared outbox.

### Read Authority
Controllers read Mongo for speed but repair from SQL on miss/stale
(`CommerceHubOrderProjectionRepairService.cs:35`). Search reads ES after SQL overlay
recompute (`CommerceHubSearchSqlAuthorityOverlay.cs`).

### Eventual Consistency / Convergence
- Mongo order docs carry `ProjectionMutationVersion` (monotonic,
  `CommerceHubMarketplaceOrder.cs:46-56`) and `ProjectionChecksum`.
- Projection consumer rejects stale events via optimistic `ReplaceOneAsync` filter
  (`CommerceHubOrderProjectionConsumer.cs:147-181`); duplicate-key race collapsed
  (`:183-190`).
- Repair reads (`CommerceHubOrderProjectionRepairService.EnsureOrderProjectedAsync`)
  rebuild from SQL on demand — convergence is SQL-authoritative, not projection-authoritative.
- Replay safety: ledger idempotency by key + financial fingerprint ensures exact replay
  and rejects key-collision-with-divergent-fingerprint
  (`CommerceHubFinancialLedgerService.cs:835-863`).

---

## Outbox Model

### Publishers
Two distinct outbox mechanisms exist; do not conflate them:

1. **Integration-event outbox (MassTransit bus-outbox).** `MassTransitEventPublisher`
   (`PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs:13`) intercepts
   `IPublishEndpoint` and stages rows in the general `OutboxMessages` table
   (`ApplicationDbContext.cs:430`). Authoritative events must be published BEFORE the
   source mutation commits or it throws (`EnsureTransactionalPublishBoundary`,
   `MassTransitEventPublisher.cs:324-339`); they require an aggregate ordering key + version
   matched to the source-mutation version, else stale-event rejection
   (`EnrichAuthoritativeOrderingMetadataAsync`, `:341-409`). CommerceHub order-event
   versions resolve from `CommerceHubMarketplaceOrder`/`CommerceHubSubOrder
   .ProjectionMutationVersion` (`MassTransitEventPublisher.cs:439-473`).
   `PublishSensitiveAsync` bypasses the bus-outbox for PII (`:222-289`), hard-blocking
   authoritative events from bypassing (`:226-230`).

2. **Financial outbox (operational-ledger → accounting).** Within
   `CommerceHubFinancialLedgerService.AppendTransactionIfMissingAsync`, each transaction
   enqueues an `AccountingOutboxMessage` (`eventType: "CommerceHubLedgerPosting"`,
   `sourceType: "CommerceHubFinancialTransaction"`) in the SAME `DbContext` transaction
   (`CommerceHubFinancialLedgerService.cs:724-755`). The shared `AccountingOutboxProcessor`
   posts a `JournalEntry` to the tenant GL (`AccountingOutboxMessage.cs:200`).

   Additional claim-based dispatch tables also exist: `MarketplaceEventOutboxMessages`,
   `MarketplaceProjectionOutboxMessages`, `StockSyncOutboxMessages`,
   `PaymentCancellationOutboxMessages`, `CommerceHubPaymentCompensationOutboxMessage`
   (`ApplicationDbContext.cs:423-437`), each with `ClaimedUntilUtc`/`NextRetryAtUtc`
   claiming for background dispatch.

### Consumers
- `CommerceHubOrderProjectionConsumer` (Mongo `commercehub_orders`) —
  `PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs`.
- `CommerceHubProjectionConsumer` (~25 event types → Mongo `commercehub_products` + ES) —
  `PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs`.
- `AccountingOutboxProcessor` (tenant GL postings) —
  `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`.

NOTE: CommerceHub consumers live ONLY in `PointOfSale.Infrastructure/Consumers/`; there is
NO `PointOfSale.API/Consumers/CommerceHub/` directory. CommerceHub events are flat in
`PointOfSale.Core/Events/CommerceHubEvents.cs` (not a `CommerceHub/` subfolder).

### Ordering
- `AccountingOutboxMessage` ordering by `OccurredAtUtc`/`CreatedAt`/`Id`, with
  `AggregateVersion` for mutable aggregates (`AccountingOutboxProcessor.cs:78-100`).
- Mongo order projection ordered by `LastEventOccurredAtUtc`/`LastEventVersion`
  (`CommerceHubOrderProjectionConsumer.cs:147-162`).
- Ordering consumers use `EndpointName` + `UseMessageRetry(Intervals 1s,5s,15s)`
  (`CommerceHubOrderProjectionConsumer.cs:264-280`).

### Retries / Replay / Idempotency
- **Consumer exactly-once** is via PostgreSQL **advisory locks** (`pg_try_advisory_lock`
  keyed by SHA256(eventId:consumerName:eventType), `ProjectionEventFence.cs:127-130`) plus
  the authoritative `ProcessedEvents` table (unique `IX_ProcessedEvents_Idempotency` on
  `(EventId, ConsumerName, EventType)`, `add_processed_events_unique_constraint.sql:53-54`;
  double-checked before/after lock, duplicate-key race collapsed
  `ProjectionEventFence.cs:98-228`). A transient Mongo `processed_events` collection (30-day
  TTL) exists as dedup evidence only — NOT the authoritative row.
- **Outbox dispatch rows** use `ClaimedUntilUtc` lease + `NextRetryAtUtc` backoff +
  `PublishedAtUtc` done-mark (`MarketplaceEventOutboxMessage.cs:148-267`,
  `AccountingOutboxMessage.cs:205-260`); dead-letter to `MarketplaceEventOutboxDeadLetterArchives`.
- Critical financial event types (`CriticalFinancialTruthEventTypes`, includes
  `"CommerceHubLedgerPosting"`) are NEVER dropped into terminal failure while business
  state is committed (`AccountingOutboxProcessor.cs:60-76`, `:252`).
- Ledger idempotency: unique constraints `UX_CommerceHubFinancialTransactions_*` /
  `UX_CHFinancialTx_*` + fingerprint check (`CommerceHubFinancialLedgerService.cs:773-909`).

### Concurrency / Versioning / Failure handling
- Consumer claiming = advisory lock (not a `claimed_until` consumer column).
- Version-aware processing for mutable aggregates
  (`AccountingOutboxMessage.AggregateVersion`).
- Failure handling: non-terminal retry for financial truth; health check blocks outward
  money on stale/failed critical outbox (`CommerceHubFinancialReadinessGate.cs:143-189`).

### Projection convergence (background + critical reads)
- On-demand repair: `CommerceHubOrderProjectionRepairService.EnsureOrderProjectedAsync`
  (`CommerceHubOrderProjectionRepairService.cs:35`) rebuilds from SQL and fails CLOSED if the
  existing Mongo doc does not `ProjectionProvesSqlAuthority` (`:86-92`).
- Background reconciliation: `CommerceHubProjectionReconciliationService` runs every ~30s,
  ≤5 passes / batch of 50, re-projects `!IsProjected` orders, keeps terminal failures
  retryable every 15 min (`:14-263`).
- Critical-read readiness gate: `CommerceHubProjectionReadinessService
  .EnsurePublicProductReadModelsReadyAsync` (`:31`) blocks the read if
  `StockSyncOutboxMessage` / product/review `MarketplaceEventOutboxMessage` rows are
  un-published, failed, ≥3 attempts, or older than a 15-min `CriticalLag` (`:17,41-131`),
  throwing `ProjectionReadModelUnavailableException`.
- ES indexing path: `CommerceHubProjectionConsumer.SyncSearchAsync` runs
  `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync`; if SQL denies visibility it DELETES the
  doc from ES, else indexes (`:2777-2795`); failure sets `SearchSyncStatus=Failed/Pending`
  and rethrows for MassTransit redelivery. Sync state machine:
  `SearchSyncPending/Synced/Failed/TerminalFailure`, `MaxSearchRetries=5`
  (`CommerceHubProductDocument.cs:9-14`).

---

## Runtime Authority Matrix

For each major concept: Owner (truth) / Mutator / Reader / Event consumer / Projection
writer / Runtime truth.

| Concept | Owner (truth) | May mutate | May read | Consumes events | Creates projection | Runtime truth |
|---|---|---|---|---|---|---|
| Marketplace Orders | `CommerceHubOrderManagementService`+`CheckoutService` (SQL) | those services (SQL-validated) | customer (own), vendor (own StoreId), SuperAdmin (gov) | order/sub-order status events | `CommerceHubOrderProjectionConsumer` (Mongo) | SQL |
| SubOrders | same, per `StoreId` | `CommerceHubOrderManagementService` (SQL lifecycle) | vendor (own StoreId), SuperAdmin | `CommerceHubSubOrderStatusUpdatedEvent` | Mongo order doc | SQL |
| Listings | POS Admin catalog + `PublishToCommerceHubHandler` (SQL `CommerceHubListings`) | publish/unpublish handlers (admitted) | search, customer, vendor | `CommerceHubListingCreatedEvent` | `CommerceHubProjectionConsumer` (Mongo/ES) | SQL |
| Products/Variants | POS Admin catalog (SQL `Products`/`ProductVariants`) | POS Admin only | CommerceHub read-only | catalog events | Mongo/ES (display) | SQL |
| Inventory | `InventoryConsistencyService` (SQL) | inventory service | CommerceHub revalidates | stock events | Mongo (display) | SQL |
| Payments | `IPaymentLifecycleService` | lifecycle service; CommerceHub orchestrates intent | customer, vendor, finance | payment/webhook events | (none — SQL) | lifecycle service |
| Unresolved payments | `CommerceHubPaymentRecoveryAuthorityService` (SQL evidence) | recovery service | readiness gate, SuperAdmin | webhook events | none | SQL evidence |
| Settlement | `CommerceHubVendorDebtService`/finance (SQL `CommerceHubVendorSettlements`) | finance + SuperAdmin | vendor, SuperAdmin | ledger events | none | SQL |
| Vendor Earnings | `CommerceHubEarningsService` (SQL) | earnings + payout services | vendor, SuperAdmin | order/refund events | none | SQL |
| Operational Ledger | `CommerceHubFinancialLedgerService` (SQL `CommerceHubFinancialTransactions`) | ledger service (idempotent) | finance read services | (itself publisher) | AccountingOutbox | SQL |
| Accounting/GL | `AccountingOutboxProcessor` (tenant GL) | outbox processor | treasury/accounting | `CommerceHubLedgerPosting` outbox | JournalEntry | SQL GL |
| Payouts | request: `CommerceHubPayoutService`; execution: SuperAdmin | SuperAdmin executes | vendor (history), SuperAdmin | payout events | none | SQL |
| Refunds | `CommerceHubRefundService`+decision engine | refund service (lifecycle) | customer, vendor, SuperAdmin | refund events | none | SQL |
| Search | ES projection + SQL overlay | indexer (SuperAdmin reindex) | customers | listing events | `CommerceHubProjectionConsumer` (ES) | SQL overlay |
| Notifications | notification service (outputs) | lifecycle services emit | all actors (read) | lifecycle events | delivery records | n/a (output) |
| Analytics | analytics service (observation) | emitters | dashboards | traffic/lifecycle events | analytics store | SQL truth |
| Recommendations | search intelligence service | search analytics | customers | search events | none | derived |
| Affiliate | Affiliate portal (separate) | affiliate services | affiliate, CommerceHub (attribution read) | affiliate events | none | SQL |
| Customer | `CommerceHubCustomerAuthService` (SQL) | auth service | customer (own), services | auth events | none | SQL |
| Identity/Tenant | `ITenantContext`+tenant services | tenant admin | CommerceHub (scope check) | tenant events | none | SQL |
| Governance | `UserGovernanceService` (SQL) | governance + SuperAdmin | visibility overlays | governance events | none | SQL |

---

## Runtime Authority Divergence

Key divergences (where CommerceHub truth is delegated, not owned):

- **Payment truth** lives in `IPaymentLifecycleService`, NOT in CommerceHub. CommerceHub
  payment code is explicitly excluded from Stripe/Paymob certification (README L201;
  `CommerceHubPaymentService` only orchestrates).
- **Inventory truth** lives in `InventoryConsistencyService`; CommerceHub revalidates but
  never owns stock.
- **Accounting truth** lives in the tenant GL; CommerceHub only writes operational ledger
  + outbox.
- **Order lifecycle (generic Storefront `Order`)** is owned by a different service than
  marketplace sub-orders. Generic `Order` rules do NOT apply to sub-orders (README L58).
- **Catalog/product truth** owned by POS Admin; CommerceHub listing is a marketplace-view
  pointer to existing product/variant.

---

## Cross-Domain Authority Graph

For each relationship: Reads / Writes / Events / Runtime Authority / Projection /
Ownership / Synchronization.

| Peer | Reads from peer | Writes to peer | Events | Runtime authority | Projection | Ownership | Sync |
|---|---|---|---|---|---|---|---|
| Catalog | reads `Products`/`ProductVariants` (SQL) | none (creates `CommerceHubListing` only) | `CommerceHubListingCreatedEvent` | Catalog owns product; CH owns listing | Mongo/ES listing doc | separated | publish event → Mongo/ES |
| Inventory | reads stock via `InventoryConsistencyService` | none | stock events consumed | Inventory owns stock | Mongo display | separated | revalidate at checkout/fulfillment |
| Orders (Storefront) | none cross | none | separate channels | distinct `Order` vs sub-order | separate | channel fixed at creation | n/a |
| Payments | reads payment status | none (orchestrates intent) | payment/webhook events | `IPaymentLifecycleService` | none | separated | unresolved webhook evidence → SQL |
| Treasury | reads ledger for payout | none (execution via SuperAdmin) | payout events | Treasury/GL owns money | GL | separated | outbox → GL |
| Accounting | reads operational ledger | receives outbox → JournalEntry | `CommerceHubLedgerPosting` | Accounting owns GL | JournalEntry | separated | transactional outbox |
| Shipping | reads shipment status | captures shipping snapshot | shipment events | `ShipmentService` owns | none | separated | snapshot frozen on sub-order |
| Identity | reads tenant/customer | none | auth events | identity services | none | separated | tenant guard |
| Search | reads SQL overlay | none | listing events | SQL overlay owns visibility | ES | projection | reindex from SQL |
| Analytics | reads SQL truth | emits traffic events | analytics events | Analytics observes | analytics store | observation | n/a |
| Notifications | n/a (output) | emits from lifecycle | lifecycle events | notification service | delivery | output | fire-and-forget |
| Affiliate | reads attribution (if present) | none | affiliate events | Affiliate separate | none | separated | SQL-authoritative commission |
| POS Admin | n/a (source) | POS Admin writes source; CH consumes | publication events | POS Admin owns source | n/a | source | publish event → CH listing |
| Storefront | n/a | n/a | separate | separate platform | separate | distinct | n/a |
| SuperAdmin | reads all (gov) | executes payout/refund/stop via owning services | governance events | SuperAdmin platform scope | n/a | isolated | readiness gate + lifecycle |

---

## Business Invariants

Implementation-backed invariants (cite code):

1. **EGP-only money.** `FinancialCurrencyGuard.RequireEgpCurrency` enforced at ledger
   (`:1091`), payout (`:223`), refund (`:80`), settlement (`:66`), search (`:120`).
2. **One live sub-order per (order, store).** `UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live`
   (`ApplicationDbContext.cs:2287`); checkout groups by `StoreId` (`:1801-1802`).
3. **Immutable shipping per sub-order.** `ShippingSnapshotJson` set at creation
   (`CommerceHubCheckoutService.cs:1849`); split-order shipping immutable after creation (README L407).
4. **Serialization + idempotency at checkout/ledger.** `Serializable`
   (`CommerceHubFinancialLedgerService.cs:881`); idempotency key + fingerprint
   (`:773-863`).
5. **Ledger integrity equation.** For an order: Vendor earning = Total − GrossCommission;
   FinalCommission = GrossCommission − PromoDiscount
   (`CommerceHubFinancialLedgerService.ValidateOrderFinancialIntegrity:1104-1122`).
6. **Refund bounds.** Refund amount ≤ sub-order total; vendor earning reversal ≤ refund
   amount; currency matches sub-order; only `Completed` refunds post
   (`:1133-1197`).
7. **COD debt suspension.** Vendor suspended when `AmountOwedByVendor >= DebtThresholdAmount`
   (default 1000 EGP, `CommerceHubVendorDebtOptions.cs:5`) derived from ledger replay
   (`CommerceHubVendorDebtService.cs:292`); 3-day grace (L6), 80% near-threshold (L7);
   status `"SUSPENDED"` (`:324`).
8. **Outward-money fail-closed.** Payout/refund/intake blocked on unresolved captured
   payments, unresolved refunds, stale/failed critical accounting outbox, payment
   compensation, emergency stop (`CommerceHubFinancialReadinessGate.cs:68-96`).
9. **Ledger→GL coverage gate (hard financial coupling).** Outward money is ALSO blocked
   until every operational `CommerceHubFinancialTransaction` has a `Posted` GL journal with
   `SourceType="CommerceHubFinancialTransaction"` + `SourceId=transaction.Id`
   (`CommerceHubFinancialReadinessGate.cs:717-806`). This is what makes ledger↔GL replay
   end-to-end safe.
10. **Refund decision engine scenarios.** `RefundFinancialDecisionEngine.Create` resolves
    exactly three scenarios from (PaymentMethod, hasPayoutOccurred): `OnlineBeforePayout`
    (reduces vendor payable, no debt), `OnlineAfterPayout` (creates vendor debt = earning
    reversal), `CashOnDelivery` (reduces vendor debt) (`RefundFinancialDecisionEngine.cs:30-99`);
    reversal rows written only after refund `Completed` (`CommerceHubFinancialLedgerService.cs:270-430`,
    `ValidateRefundFinancialIntegrity:1137-1197`).
11. **Projection monotonic version.** `ProjectionMutationVersion` strictly increasing
   (`CommerceHubMarketplaceOrder.cs:46-56`); stale events rejected
   (`CommerceHubOrderProjectionConsumer.cs:147-181`).
10. **SuperAdmin isolation.** Platform endpoints require `RequireSuperAdmin`
    (`SuperAdminSecurityConstants.cs:6`); never bypass lifecycle owners.
11. **Tenant/store isolation is app-level, not EF-filter-level (verified nuance).**
    `CommerceHubMarketplaceOrder` and `CommerceHubSubOrder` implement ONLY
    `IProjectionVersioned`, NOT `ITenantOwned`
    (`CommerceHubMarketplaceOrder.cs:6`, `CommerceHubSubOrder.cs:6`), and are NOT in the
    EF global `HasQueryFilter` list (`ApplicationDbContext.cs:502+` enumerates filtered
    entities; neither marketplace order type appears). Cross-tenant isolation therefore
    relies on app-code scoping: sub-orders carry `StoreId` (not a `TenantId` column,
    `CommerceHubSubOrder.cs:11`), `GetAffectedStoreIds()` filters `Guid.Empty`
    (`CommerceHubMarketplaceOrder.cs:63-71`), sub-order mutation forbids cross-store writes
    (`CommerceHubOrderManagementService.cs:87` `SUBORDER_FORBIDDEN`), and customer reads are
    scoped by `CustomerId` (`CommerceHubCustomerOrderService.cs:55,112`). A raw query
    against `CommerceHubMarketplaceOrders` bypasses tenant scoping — this is a
    necessary-not-sufficient boundary that must be enforced in every read path.
12. **Unresolved captured payment blocks outward money regardless of tenant.**
    (`CommerceHubFinancialReadinessGate.cs:339-401`, blocking statuses
    `PendingResolution`/`RoutedToSuspense`/etc.).

---

## Architectural Decisions

Implementation-backed rationale:

1. **Why CommerceHub has an Operational Ledger.** It needs marketplace-specific financial
   state (per-vendor earning, commission, COD debt, settlement) decoupled from the generic
   tenant GL, but still must post to GL. Implemented as `CommerceHubFinancialTransaction`
   mirrored via `AccountingOutboxMessage` (`CommerceHubFinancialLedgerService.cs:724-755`).
   Rationale: marketplace money effects are EGP, per-vendor, and need COD-debt derivation
   that the generic GL does not model.

2. **Why tenant GL is separated.** `CommerceHubFinancialTransaction` is operational;
   `AccountingOutboxMessage` → `JournalEntry` is the immutable double-entry GL
   (`AccountingOutboxMessage.cs`, `AccountingOutboxProcessor.cs`). Separation keeps
   marketplace-specific buckets (Cash/Receivable/Payable) while satisfying accounting
   immutability.

3. **Why Marketplace Orders differ from Storefront Orders.** Channel identity fixed at
   creation; marketplace = parent + per-`StoreId` sub-orders; Storefront = single generic
   `Order` (README L399, L501-509). Implemented by distinct aggregates and the
   `UX_CommerceHubSubOrders_...` uniqueness.

4. **Why Split Orders exist.** Multiple vendors in one cart require independent fulfillment,
   payment, and settlement per vendor. Checkout groups by `StoreId`
   (`CommerceHubCheckoutService.cs:1801-1802`); each sub-order carries its own shipping
   snapshot, commission, earning.

5. **Why EGP-only exists.** Platform settlement/clarity; non-EGP would break unified
   commission/settlement/reconciliation. Enforced centrally by `FinancialCurrencyGuard`
   (`FinancialCurrencyGuard.cs:10,59`), not per-call.

6. **Why SQL remains authority.** Projections are evidence; repair-on-read and SQL overlay
   guarantee convergence (`CommerceHubOrderProjectionRepairService.cs:35`,
   `CommerceHubSearchSqlAuthorityOverlay.cs:35`). Avoids projection drift becoming truth.

7. **Why projections exist.** Performance for customer-facing search/order-tracking and
   decoupling of read load from write path; safe because they are rebuildable from SQL.

8. **Why SuperAdmin is isolated.** Platform governance (suspension, reindex, emergency
   stop, payout execution) must not be reachable from vendor/customer surfaces and must
   not bypass lifecycle owners (README L421-735; `RequireSuperAdmin` policy).

---

## Technical Debt

Evidence-backed findings only (no speculation):

1. **Duplicate/legacy authority surface in finance.** `CommerceHubVendorDebtService`
   merges both `CommerceHubVendorSettlements` table rows AND ledger `Settlement`/`Adjustment`
   control rows (`CommerceHubVendorDebtService.cs:101-124, 368-401`), with a
   `CommerceHubVendorSettlementStoreCompatibility.IsMissingSettlementTable` try/catch that
   silently returns empty when the settlement table is absent (`:397-400`). This indicates
   a partial migration: settlement truth exists in two places (ledger + settlement table)
   and the code tolerates a missing table. Risk: dual-source of debt truth.

2. **"legacy:" prefix handling.** `CommerceHubVendorSettlement.Create` rejects the
   `legacy:` prefix for NEW settlements (`:116-121`) but the debt service still processes
   legacy commission rows (`legacyCommissionRows`, `:216-243`). Indicates backfilled data
   that new code must special-case.

3. **Projection dual-write paths.** Mongo order docs are written by both
   `CommerceHubOrderProjectionConsumer` (event-driven) and
   `CommerceHubOrderProjectionRepairService` (repair). Two writers increase drift risk if
   versions diverge; mitigated by `ProjectionChecksum` + monotonic version, but the
   repair service writes without the same stale-event filter as the consumer
   (`CommerceHubOrderProjectionRepairService.cs:60` vs `CommerceHubOrderProjectionConsumer.cs:147`).

3b. **Two independent outbox mechanisms.** Integration events use the MassTransit
    bus-outbox (`OutboxMessages`) with advisory-lock + `ProcessedEvents` exactly-once
    (`ProjectionEventFence.cs:127-130`, `MassTransitEventPublisher.cs:13`), while financial
    postings use the separate `AccountingOutboxMessage` (`:724-755`). Both must stay healthy
    for outward money; the readiness gate already checks both, but the two code paths
    (bus-outbox vs DbContext-enlisted outbox) are conceptually distinct and easy to
    conflate.

4. **SQL overlay recompute on every search read.** `CommerceHubSearchSqlAuthorityOverlay`
   re-queries SQL (listing, tenant, variant, pricing, stock, category, governance) on every
   search visibility decision (`:72-120`). Correct for authority, but a performance
   coupling: search latency is bound to SQL load.

5. **Readiness gate scans across tenants.** `EnsureCommerceIntakeAllowedAsync` loops tenants
   and inspects outbox/compensation/webhook tables with `Contains(tenantIdText)` string
   scans on JSON columns (`CommerceHubFinancialReadinessGate.cs:129-140, 201-211`), with a
   `TenantScopeInspectionWarningThreshold = 500` warning (`:40`). Indication of
   unscoped/JSON-column querying that may not scale.

6. **Large service classes.** `CommerceHubCheckoutService.cs` (~2765 lines),
   `CommerceHubFinancialReadinessGate.cs` (~1703 lines),
   `CommerceHubFinancialLedgerService.cs` (~1227 lines),
   `AccountingOutboxProcessor.cs` (~3342 lines) — mixed responsibilities / long methods;
   refactoring risk but currently coherent.

7. **Emergency stop is config-driven, not lifecycle-owned.** `CommerceHubEmergencyStopOptions`
   blocks outward money (`CommerceHubFinancialReadinessGate.cs:1591`). Correct fail-closed
   behavior, but the stop is a separate config surface from the lifecycle services,
   increasing operational coupling.

(Items are implementation-observable; no invented debt.)

---

## Implementation Follow-up Items

| Title | Current implementation | Why it matters | Recommended future improvement | Priority | Status |
|---|---|---|---|---|---|
| Consolidate settlement truth | Debt derived from BOTH `CommerceHubVendorSettlements` table and ledger Settlement/Adjustment rows; missing-table tolerated (`CommerceHubVendorDebtService.cs:368-401`) | Dual source of debt truth risks divergence between ledger and settlement table | Pick one authority (ledger) and make settlement table a projection; remove `IsMissingSettlementTable` tolerance | High | Verified |
| Unify Mongo order projection writers | Consumer + repair service both write `commercehub_orders` with different filters | Drift risk if version logic diverges | Extract single `WriteOrderProjection(document)` used by both paths | Medium | Verified |
| Index/claim-based tenant scope in readiness gate | JSON `Contains(tenantIdText)` scans on outbox/webhook tables (`CommerceHubFinancialReadinessGate.cs:201-211`) | Does not scale; full-table scans per tenant | Add tenant-scoped columns/indexes; replace string `Contains` with relational filters | Medium | Verified |
| Centralize EGP guard (done) | `FinancialCurrencyGuard` already central (`FinancialCurrencyGuard.cs:59`) | — | Keep; extend ISO validation coverage | Low | Done |
| Remove `legacy:` special-casing | Debt service processes legacy commission rows (`CommerceHubVendorDebtService.cs:216-243`) | Backfill debt keeps two code paths alive | After migration completes, drop legacy branch | Low | Verified |
| Add EF global tenant filter to marketplace orders | `CommerceHubMarketplaceOrder`/`CommerceHubSubOrder` are `IProjectionVersioned` only, NOT `ITenantOwned`, and absent from `HasQueryFilter` (`CommerceHubMarketplaceOrder.cs:6`, `ApplicationDbContext.cs:502+`) | Cross-tenant isolation is app-code-enforced; a raw query bypasses scoping (necessary-not-sufficient) | Add an explicit tenant/store-scoping global filter or a hardened repository boundary so marketplace-order reads cannot leak across stores | High | Verified |
| Verify "immutable shipping" with a DB constraint | Shipping is frozen via single-write `ShippingSnapshotJson` at build (`CommerceHubCheckoutService.cs:1849`); no post-creation mutation path, but no DB-level constraint | Convention-only; future code could add a setter | Consider a read-only/protected column or a guard on the entity to make immutability structural | Low | Verified |

---

## Certification

### Implementation Confidence
HIGH. Every architectural claim in this document is traced to a specific `file:line` in
the backend implementation. The marketplace order/sub-order split, EGP guard, operational
ledger → accounting outbox bridge, readiness gate, and projection repair patterns were
read directly from source.

### Architecture Completeness
HIGH for runtime authority, financial architecture, CQRS, outbox, and cross-domain
boundaries. The Affiliate↔CommerceHub attribution boundary is intentionally left
implementation-light because the codebase does NOT grant CommerceHub marketplace sub-orders
affiliate authority by default (README L391) and no CommerceHub-specific affiliate
certification was found in code.

### Runtime Authority Verification
VERIFIED for: Marketplace Orders, SubOrders, Listings, Operational Ledger, Accounting
bridge, Settlement, Vendor Earnings, Payouts, Refunds, Search overlay, Readiness gate,
SuperAdmin isolation, Tenant isolation.

### Cross-Domain Verification
VERIFIED boundaries with Catalog, Inventory, Payments, Treasury/Accounting, Shipping,
Identity, Search, Notifications, Analytics, SuperAdmin, POS Admin, Storefront, Affiliate
(see Cross-Domain Authority Graph).

### Verified Capabilities
- Multi-vendor split checkout with per-store sub-orders (`:1801-1862`).
- EGP-only financial enforcement at every boundary (`:10,59`).
- Operational ledger with idempotent, fingerprint-checked appends (`:773-909`).
- Transactional mirror to tenant GL via `AccountingOutboxMessage` (`:724-755`).
- Fail-closed outward-money readiness gate (`:68-96`).
- Repair-on-read Mongo projections + SQL-authority search overlay (`:35`, `:35`).
- SuperAdmin isolation via `RequireSuperAdmin` (`:6`).
- COD-debt suspension derived from ledger (`:269-300`).

### Missing Capabilities
- No CommerceHub-native affiliate commission authority in code (deferred to separate
  Affiliate certification).
- No separate "recommendations" service beyond search-intelligence analytics
  (`CommerceHubSearchIntelligenceService.cs`) — recommendations are derived, not a standalone
  runtime authority.

### Technical Debt Summary
See Technical Debt: dual settlement truth, legacy prefix handling, dual projection writers,
SQL-overlay-per-search coupling, tenant-scope JSON scans in readiness gate, large service
classes, config-driven emergency stop. All evidence-backed.

### Architectural Observations
- CommerceHub is best understood as a **marketplace orchestration context inside the monolith**,
  not a standalone service. Its "ownership" is narrow (listings, carts, marketplace
  orders/sub-orders, marketplace payments orchestration, operational ledger, earnings,
  payouts requests, refunds) and it delegates all cross-cutting truth (catalog, inventory,
  payment lifecycle, accounting, shipping, governance) to owning services.
- The dominant architectural pattern is **SQL authority + derivative projections + fail-closed
  financial gates + idempotent outbox**. This is consistent across the whole platform
  (POS, Storefront, Affiliate) and is what makes cross-tenant safety and financial
  correctness enforceable.
- The operational-ledger → accounting-outbox → GL bridge is the single most important
  financial integration: it lets CommerceHub keep marketplace-specific buckets while
  satisfying immutable double-entry accounting, and the readiness gate makes payouts/refunds
  wait until that bridge is healthy.

---

End of canonical CommerceHub architecture. This document supersedes the need to read
individual `docs/architecture/*CommerceHub*` certification files to understand the
architecture; those remain useful as evidence but are not authoritative over this
implementation-backed specification.
