# Treasury T-08 — E-Commerce / CommerceHub Inventory & COGS Integrity (Discovery Only) — 2026-09-14

> SINGLE-AGENT / READ-ONLY. No business writes, no migrations, no journals, no refunds, no stock adjustments, no T-06/T-07 or 1020/1110 changes were made. All unrelated working-tree changes preserved (verified via `git status` before writing; this report file is the only addition).

## 1. Executive Verdict

**Verdict: FAIL (discovery complete, lifecycle not closed).**

The exact T-08 question — *"How does Shopixy know the exact cost of merchandise sold through Storefront and CommerceHub, and how does that exact cost become COGS and Inventory GL, then reverse correctly on refund/return?"* — has a proven answer at the **cost-snapshot half** and a proven **absence** at the **GL-posting half**:

1. **Cost authority IS proven (source-level):** `ProductVariant.CostPrice` is the canonical merchandise cost. It is maintained as a **moving weighted average** on purchase receipt/return (`VariantCostAuthority.ApplyPurchaseReceipt/ApplyPurchaseReturn`), and it is **snapshotted immutably** onto `OrderItem.CostPrice` at Storefront checkout from the authoritative variant row. `OrderItem.CostPrice` is in the EF immutable-guard list. POS uses the stronger pattern (`SalesInvoiceItem.CostPriceSnapshot` + `CostAmountSnapshot`, consumed by COGS logic with a current-cost fallback only for legacy rows).
2. **COGS/Inventory GL lifecycle is ABSENT for both e-commerce channels (source-proven):** none of `PostOnlinePaymentCapturedAsync`, `PostCodDeliveredAsync`, `PostWalletApprovedAsync`, `PostEcommerceRefundAsync` emits a `5010`/`1310` line; `PostCommerceHubFinancialTransactionAsync` only posts suspense/payable/cash/revenue/fee shapes and never `5010`/`1310`. No `EcommerceCogs` / `CommerceHubCogs` outbox event, consumer, or idempotency key exists.
3. **Operational inventory DOES move without GL cost:** Storefront reservations commit on payment success (`CommitOrderReservationsAsync` inside the payment lifecycle transaction); CommerceHub delivery commits `StockMovementType.Sale` movements (`CommitDeliveryAsync`) and returns receive stock (`ReceiveReturnAsync`) — all without a cost/GL leg. `StockMovement.Price` exists but is not populated on these sale/delivery movements.
4. **CommerceHub has no merchandise cost field at all:** `CommerceHubSubOrderItem` carries `UnitPrice`, commission snapshots, promo discount, and totals — **no `CostPrice` / unit-cost / cost-snapshot column**. There is therefore no original-cost basis available for a future CommerceHub COGS reversal without a design decision.
5. **Refunds reverse revenue only:** Storefront refund = `Dr 4020 / Cr clearing-or-bank`; CommerceHub refund = platform bucket reversal (`Dr 2030 / Cr 1030` observed shape). Neither restores `1310` nor reverses `5010`. Refund allocation (`AllocateRefundAcrossItems` → `OrderItem.RefundedAmount` / `OrderRefundLine`) is monetary and has no stock-return or cost-reversal linkage.
6. **Historical boundary must stand:** per Phase 2A evidence (Shopixy: 3 posted COD orders EGP 1,230; greatone/Shopixy CommerceHub finance + inventory movements live; zero settlement batches/payouts), unposted COGS for already-fulfilled e-commerce volume is unquantified in this session and must not be synthesized.

Minimal safe design is defined in §21. Nothing was implemented.

## 2. Scope And Evidence

| Item | Detail |
| --- | --- |
| Mode | Discovery only. Single agent. No commit. |
| Source inspection | Current working-tree source (primary authority), read-only |
| Prior certified evidence reused (not re-queried) | `docs/reports/TREASURY_PHASE_2A_GLOBAL_SOURCE_MATRIX_2026-09-14.md` (tenant-scoped `REPEATABLE READ … READ ONLY` capture 2026-09-14T13:11:38Z against `PosEcommerceDb_Baseline`; tenants AhramStore `c9c5c1ae-…`, greatone `c43aebf2-…`, Shopixy `83b39fe2-…`) |
| Live SQL in this session | None executed (by task constraint: read-only inspection; no new SQL capture taken). All "live" rows cited below are Phase 2A observations, explicitly attributed |
| Boundaries respected | No order/refund/fulfillment/stock/journal/outbox/balance mutation; no T-06/T-07, 1020/1110 changes; no synthetic COGS journals |
| Key source files | `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`; `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs`; `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`; `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`; `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`; `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`; `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs`; `PointOfSale.API/Services/Checkout/CheckoutService.cs`; `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`, `CommerceHubOrderManagementService.cs`, `InventoryConsistencyService.cs`; `PointOfSale.Core/Entities/{Order,OrderItem,OrderRefund,StockMovement,InventoryReservation,SalesInvoiceItem,CommerceHubSubOrderItem}.cs`; `PointOfSale.Core/Inventory/VariantCostAuthority.cs`; `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` |
| Account codes | `1310` Inventory, `5010` COGS, `4010` POS sales, `4020` e-commerce sales, `1110` Visa/Card clearing, `1120` Stripe clearing, `1130` COD clearing, `1140` Wallet clearing, `2030` Treasury suspense, `2040` marketplace vendor payable (`IAccountResolver.cs`, `SystemAccountTemplate.cs`) |
| Method | Every journal shape below is quoted from its posting method; every "absent" claim was verified by reading the full method body and its `PostingLine` list, plus a repo-wide grep for `5010`/`CostOfGoodsSold` posting sites |

## 3. Order-To-Inventory Lineage

### A. Storefront order lineage (proven from source)

| Field | Source table/entity | Authority | Mutable? | Snapshot? | Tenant scoped? | Historical? |
| --- | --- | --- | --- | --- | --- | --- |
| Order | `Orders` (`PointOfSale.Core/Entities/Order.cs`) | `CheckoutService` (creation) → `PaymentLifecycleService` (payment status only) → `OrderFulfillmentService` (fulfillment status only), each gated by save-changes interceptors | Status fields via authorized services only | Payment/financial snapshots (`PaymentMethodCode/Name`, `PricingSnapshotJson`) immutable after creation (guarded) | Yes (`TenantId`, `ITenantOwned`) | Yes, rows persist |
| Order line | `OrderItems` (`OrderItem.cs`) | Checkout authoritative variant lookup | No — SKU/pricing/variant/tax/discount/affiliate snapshots in immutable-guard list (`ApplicationDbContext.cs:4147-4171`, incl. `CostPrice`) | Yes — `UnitPrice` (from `Variant.OnlinePrice ?? Variant.Price`), `CostPrice` (from `Variant.CostPrice`), SKU/name/attributes/image | Via parent `Order.TenantId`; variant lookup filters `variant.TenantId == tenantId && variant.Product.TenantId == tenantId` (`CheckoutService.cs:5119-5147`) | Yes |
| Product / variant | `Products` / `ProductVariants` | Catalog authority (`ProductVariant.CostPrice` canonical; `Product.PurchasePrice` legacy mirror) | Yes (cost moves on purchase) | N/A (live master) | Yes | Current only |
| Warehouse | `OrderItem.WarehouseId` + `Order.WarehouseId` (nullable) + `InventoryReservations.WarehouseId` + `ProductVariantStocks` | Warehouse-resolution service at checkout; reservation repository fans out across warehouses | Reservation-time binding | Warehouse id persisted on reservation | Yes | Yes |
| Quantity | `OrderItem.Quantity` (int, immutable) | Checkout snapshot | No | Yes | Via order | Yes |
| Unit cost | `OrderItem.CostPrice` (decimal, immutable) | `Variant.CostPrice` at checkout commit | No | **Yes — the e-commerce cost snapshot** | Via order | Yes |
| Fulfillment | `Order.FulfillmentStatus` (`Created→Confirmed→Packed→OutForDelivery→Delivered→Completed`, plus `Returned/Cancelled/FailedDelivery`) | `IOrderFulfillmentService` exclusively (interceptor-enforced); `FulfillmentMode` (Manual/Carrier) immutable, decides who may transition | Via authorized transitions only | Delivery evidence (`DeliveryReference`, `ProofOfDeliveryUrl`) immutable once captured | Via order | Yes + status history via events |
| Stock movement | `StockMovements` + `ProductVariantStocks` (`Quantity/ReservedQuantity`) + `InventoryReservations` (`Reserved/Committed/Released/Returned` quantities) | `ProductVariantStockRepository` (Storefront) | Stock rows mutate; movements append-only audit | Movement carries `ConversionFactor`, `QuantityAfter/AvailableAfter`, `IdempotencyKey`, `CorrelationId`, `InventoryEventId/Type`; **no cost snapshot on sale movements** | Yes (`StockMovement.TenantId`, `InventoryReservation.TenantId` non-null) | Yes |
| Reservation | `InventoryReservations` | Checkout reserve → payment commit/release; expiry cleanup job | `Status`, quantity buckets via authorized methods | `ReservationEventId/LastMutationEventId`, `ExpiresAtUtc` | Yes | Yes |
| Shipment/delivery | `Order.ShippedAt/DeliveredAt/TrackingNumber/Carrier`, fulfillment events (`FulfillmentDeliveredEvent`) | Fulfillment service / shipment sync | Evidence-immutable | Yes | Via order | Yes |
| Refund/return | `OrderRefunds` + `OrderRefundLines` (money) — **no stock-return table linked** | `PaymentLifecycleService.RecordRefundAsync` | Refund rows immutable (guarded list, `ApplicationDbContext.cs:4173-4189`) | `Amount/Currency/IsFullRefund/Settlement*` immutable; per-item split via `AllocateRefundAcrossItems` → `OrderItem.RefundedAmount` | Yes | Yes |

Cost-freeze chain (Storefront): authoritative variant read (`LoadAuthoritativeCheckoutVariantLookupAsync` selects `variant.CostPrice`, `CheckoutService.cs:5127-5135`) → cart item refresh (`item.CostPrice = authoritativeVariant.CostPrice`, `:5056`) → order-item creation (`CostPrice = item.CostPrice`, `:6857`) → final in-transaction refresh (`orderItem.CostPrice = cartItem.CostPrice`, `:7996`). Unit cost is therefore frozen at **checkout commit**, before payment and before fulfillment.

Payment-vs-fulfillment boundary (Storefront, explicitly separate transactions): payment success (`PaymentLifecycleService`, `:2259-2349`) sets `PaymentStatus=Collected`, allocates money across items, and **commits reservations** (`CommitOrderReservationsAsync`, `:2289`) in the *same* lifecycle transaction, then enqueues collection accounting. Fulfillment progression (`OrderFulfillmentService.TransitionAsync`, serializable transaction + domain event) is a *different* transaction family and posts **no accounting**. COD is the special case where collection accounting itself is triggered by delivery (`MarkCodDeliveredAsync` → `EnqueueCodDeliveredAsync` → `PostCodDeliveredAsync`), but still with no COGS leg. So: **payment capture and inventory deduction are coupled (same commit); payment capture and fulfillment status are not; fulfillment status and accounting are not.**

### B. CommerceHub marketplace order lineage (proven from source)

| Field | Source table/entity | Authority | Mutable? | Snapshot? | Tenant scoped? | Historical? |
| --- | --- | --- | --- | --- | --- | --- |
| Marketplace order | `CommerceHubMarketplaceOrders` | `CommerceHubCheckoutService` | Status machine (`CommerceHubOrderStateHelper`) | Order number, totals, promo/commission summaries, `CommissionSnapshotJson` | Store/tenant scope via `SubOrder.StoreId` | Yes |
| Sub-order / line | `CommerceHubSubOrders` / `CommerceHubSubOrderItems` (`CommerceHubSubOrderItem.cs`) | Checkout + `CommerceHubOrderManagementService` | Status via allowed transitions; delivered vendor orders locked | `UnitPrice`, `PlatformCommissionRate/Amount`, `FinalPlatformCommissionAmount`, `PromoDiscountAmount`, `Total`, commission JSON — **no cost snapshot field exists** | Via `StoreId` | Yes |
| Product/variant/warehouse | Same catalog + `ProductVariantStocks`; reservation carries `WarehouseId` | `InventoryConsistencyService` | As Storefront | Reservation/move records only | Yes | Yes |
| Quantity | `CommerceHubSubOrderItem.Quantity` (int) | Checkout | Status-driven | Yes | Via sub-order | Yes |
| Unit cost | **ABSENT** — no `CostPrice`/unit-cost column on sub-order item; `CommitDeliveryAsync` request carries `TenantId/OrderId/SubOrderId/OrderItemId/ProductId/VariantId/Quantity` but **no cost** (`CommerceHubOrderManagementService.cs:160-175`; `InventoryConsistencyService.cs:1143-1321`) | N/A | N/A | N/A | N/A | UNKNOWN — REQUIRES EVIDENCE (whether any out-of-band cost is stored; none found in entity) |
| Fulfillment | Sub-order `Status` (`CanTransition`, payment-readiness gate: online-collected or COD before fulfillment, `:138-145`) | `CommerceHubOrderManagementService` | Via service only | `CommerceHubSubOrderStatusHistory` | Via store | Yes |
| Stock movement | `StockMovements` (`MovementType.Sale`, `ReferenceType="CommerceHubDelivery"`) + reservations | `InventoryConsistencyService.CommitDeliveryAsync` (delivery) / `ReceiveReturnAsync` (returns) / cancel-release path (`:177-190`) | Movements append-only | `InventoryEventInbox` idempotency (`TryBeginEventAsync`), `EventId = InventoryEventIdFactory.Create("commercehub-delivery-commit", item.Id)` | Yes | Yes |
| Finance | `CommerceHubFinancialTransactions` (immutable factory `Create`, idempotency `ledger:{scope}:{type}:{direction}:{refType}:{refId}`) → `CommerceHubLedgerPosting` outbox → `CommerceHubFinancialTransaction` journal | `CommerceHubFinancialWorkflowService.ApplyDeliveredFinancialsAsync` (on Delivered) + payment/refund services | Immutable transaction rows | `IdempotencyKey/CorrelationId/Currency=EGP` | **Weak point:** transaction has no `AccountingTenantId` column; attribution derived via outbox `BusinessEntityId + TenantId` (Phase 2A §14) | Yes |

CommerceHub boundary (same-transaction coupling, proven): `Delivered` transition commits delivery stock **and** applies delivered financials inside one `ReadCommitted` transaction (`CommerceHubOrderManagementService.cs:147-236`: `commerceHubFinancialBoundary` + `paymentLifecycleBoundary` scopes, `CommitDeliveryAsync` per item `:158-176`, then `ApplyDeliveredFinancialsAsync` `:213-220`). Cancellation releases reservations (`:177-190`) with no financial leg in that branch. Finance posting itself is via the ledger-posting outbox/processor afterward.

## 4. Inventory Cost Authority

**Proven single authority: `ProductVariant.CostPrice` (moving weighted-average cost), snapshotted per sale line.**

Evidence chain (no inference from names — read behavior):

1. **Master:** `ProductVariant.CostPrice` (`ProductVariant.cs:73`) is canonical for variant products; `Product.PurchasePrice` is a legacy compatibility mirror (`Product.cs:54-56`; `CatalogPricingAuthority.ResolveSimplePurchasePrice`; `ProductPricingCompatibilityMirror`). `ProductUnit.PurchasePrice` feeds the mirror.
2. **Maintenance (weighted average, not FIFO/LIFO):** purchase receipt sets `variant.CostPrice = VariantCostAuthority.ApplyPurchaseReceipt(currentQty, currentCost, receivedQty, receivedUnitCost)` (`PurchasesController.cs:237-239`, `:1186-1188`; formula `(q1·c1 + q2·c2)/(q1+q2)`, `VariantCostAuthority.cs:5-23`). Purchase return reverses with `ApplyPurchaseReturn` (`:1514-1516`, `PurchaseReturnsController.cs:685-695`; formula `(q·c − qr·cr)/(q−qr)`, floored at 0, `VariantCostAuthority.cs:25-44`). No `ProductCostHistory` table was found (repo-wide search: no such entity; history, if needed, must be reconstructed from purchase invoices + stock movements — UNKNOWN — REQUIRES EVIDENCE for a dedicated history table).
3. **Snapshot (Storefront):** `OrderItem.CostPrice` ← `Variant.CostPrice` at checkout commit (`CheckoutService.cs:5056`, `:6857`, `:7996`; DTO comment "CostPrice comes from Variant.CostPrice", `CheckoutDtos.cs:932`). Immutable thereafter (EF guard list `:4164`).
4. **Snapshot (POS, stronger):** `SalesInvoiceItem.CostPriceSnapshot` (precision 18,6) + `CostAmountSnapshot` (18,2) captured at sale commit (`SalesController.cs:280-298`, `:1492-1511`, `:2584-2585`; `PosCartRuntimeService.cs:606-623`); returns carry `ReturnItem.CostPriceSnapshot` and must equal the original sale line's snapshot (`SalesReturnsController.cs:991-993`, `:1549-1577`). COGS consumes the snapshot first (`CalculateTotalCost`/`CalculateReturnCost`, `CommerceAccountingService.cs:1672-1707`).
5. **Snapshot (CommerceHub): ABSENT.** `CommerceHubSubOrderItem` has no cost column; `InventoryDeliveryCommitRequest` carries no cost; `CreateMovement` for `CommerceHubDelivery` sets quantities/reservation linkage/notes but no `Price` (`InventoryConsistencyService.cs:1267-1286`). `StockMovement.Price` ("for opening balance and valuation", `StockMovement.cs:86-89`) is populated for opening-balance flows (`InventoryService.cs:328`) but **not** for sale/delivery commits — verified by reading both movement constructors.
6. **Freeze moment:** cost is frozen at **order-line creation (Storefront checkout commit)** and at **sale-line commit (POS)** — i.e., *before* payment capture, *before* stock deduction, *before* shipment/delivery. Which state "owns" accounting cost today: **POS sale commit owns it** (snapshot → COGS journal in the same posting flow). For Storefront/CommerceHub, **no state owns it yet** — the snapshot exists (Storefront) or doesn't (CommerceHub), but no consumer converts it into `5010`/`1310`.

Consequence for the "cost 50 → cost 60" test (§9 in task = §9 here): architecturally correct COGS is **50** (frozen snapshot), but today's e-commerce paths would post **no COGS at all**, and any future naive implementation reading live `Variant.CostPrice` would wrongly post 60.

## 5. POS Reference Model

Certified reference (read from `CommerceAccountingService.cs:47-225` sale, `:754-923` refund):

| Step | Journal | Lines | SourceType / key |
| --- | --- | --- | --- |
| Sale revenue | Journal 1 | Dr cash (warehouse-resolved cashbox) / `1110` / `1410` by tender (Cash/Card/Mixed/Credit) ; Cr `4010` net revenue + sales-tax payable | `SalesInvoice` / `SALE:{tenant}:{invoice}[:opKey]` |
| Sale COGS (if `totalCost > 0`) | Journal 2 | **Dr `5010` / Cr `1310`** for `totalCost = Σ CostAmountSnapshot ?? (Variant.CostPrice × BaseQuantity)` | `SalesInvoice_COGS` / `COGS:{tenant}:{invoice}[:opKey]` |
| Refund revenue reversal | Journal 1 | Dr `4010` + tax payable; Cr cash/`1110`/`1410` per explicit `RefundAllocation` (account-credit refunds must have zero payout) | `Return` / `REFUND:{tenant}:{return}` |
| Refund COGS reversal (if `returnCost > 0`) | Journal 2 | **Dr `1310` / Cr `5010`** for `Σ CostAmountSnapshot ?? (Variant.CostPrice × BaseQuantity)` | `Return_COGS` / `REFUND_COGS:{tenant}:{return}` |

Controls: multi-tender allocation through payment/Treasury authority; change kept on cash leg; credit sales use `1410`; immutable compensating journals (never line mutation); outbox (`PosSale`/`PosRefund` → `AccountingOutboxProcessor.cs:1115`, `:1623`, `:2027`); journal-level idempotency with shape-match guard (`FinancialPostingService.EnsureIdempotentRequestMatches`).

### Cost-accounting comparison (summary; full matrix in §17)

| | POS | Storefront | CommerceHub |
| --- | --- | --- | --- |
| Unit-cost authority | `Variant.CostPrice` → immutable line snapshot | `Variant.CostPrice` → immutable `OrderItem.CostPrice` (proven) | `Variant.CostPrice` exists as master but **never snapshotted** (no column) |
| COGS journal | Dr `5010` / Cr `1310` (Journal 2) | **Absent** | **Absent** |
| COGS reversal | Dr `1310` / Cr `5010` | **Absent** | **Absent** |
| Outbox | `PosSale`/`PosRefund` | Revenue/refund only | Ledger-posting only |

## 6. Storefront Accounting

Methods traced: `PostOnlinePaymentCapturedAsync` (`CommerceAccountingService.cs:1076-1115`), `PostCodDeliveredAsync` (`:1344-1379`), `PostWalletApprovedAsync` (`:1381-1416`), `PostEcommerceRefundAsync` (`:1418-1475`), plus suspense pair `PostUnresolvedPaymentAsync` / `PostResolvedUnresolvedPaymentAsync` (`:1117-1271`) and settlements `PostGatewaySettlementAsync` / COD settlement (`:1518+`).

Exact journal shapes (all verified line-by-line; **no `5010`/`1310` line in any**):

| Event | Journal | SourceType / idempotency key |
| --- | --- | --- |
| Online capture (Stripe→`1120`-family, Paymob/Card→`1110`, COD→`1130`, Wallet→`1140`, unknown→Stripe fallback) | Dr payment/clearing account (`ResolveEcommercePaymentAccountIdAsync`, `:1497-1512`) / Cr `4020` for `order.Total` | `Order_Payment` / `ONLINE_PAYMENT:{tenant}:{order}:{paymentIntentId}` |
| COD delivered | Dr `1130` / Cr `4020` for `order.Total` | `Order_COD` / `COD_DELIVERED:{tenant}:{order}` |
| Wallet approved | Dr `1140` / Cr `4020` for `order.Total` | `Order_Wallet` / `WALLET_APPROVED:{tenant}:{order}` |
| Unmatched capture | Dr provider clearing / Cr `2030` | `UnresolvedPayment` / `UNRESOLVED_PAYMENT:{tenant}:{gateway}:{eventId}` |
| Suspense resolution | Dr `2030` / Cr `4020` | `Order_UnresolvedPaymentClearance` / `RESOLVED_UNRESOLVED_PAYMENT:{tenant}:{order}:{eventId}[:gateway]` |
| Refund pre-settlement | **Dr `4020` / Cr original clearing** | `Order_Refund` / `ECOM_REFUND:{tenant}:{order}:{refundId}` |
| Refund post-settlement | **Dr `4020` / Cr `1030` Bank** | Same key family (context `IsPostSettlementRefund`) |
| Gateway/COD settlement | Dr `1030` net + Dr fee (`6010`/`6020`) / Cr clearing gross | `GatewaySettlement`/`CODSettlement` |

Absence proof: repo-wide grep for `CostOfGoodsSold`/`5010`-as-account shows posting sites only in `PostPosSaleAsync` (`:81`, `:206`), `PostPosRefundAsync` (`:809`, `:909`), and purchase flows — zero hits inside `:1076-1475`. The four e-commerce methods resolve only `EcommerceSales` + payment/clearing/bank/suspense accounts. Fulfillment/inventory services (`OrderFulfillmentService`, `ProductVariantStockRepository`) contain no `FinancialPostingService`/`CommerceAccountingService` call. **Therefore: Sale has no `Dr 5010 / Cr 1310`; refund has no `Dr 1310 / Cr 5010`. Proven absent from source, consistent with Phase 2A live finding ("no e-commerce COGS journal found").**

Related fulfillment/inventory path: reserve at checkout → commit on payment success → release on cancel/failure (`PaymentLifecycleService.cs:2289`, `:2657`, `:3781-3794`, `:3939`; `CheckoutService.cs:6383`; `CheckoutPaymentIntentRecoveryService.cs:374`). Delivery-gated accounting (`RequiresPostedCollectionAccountingBeforeFulfillment`, `OrderFulfillmentService.cs:613-618`) orders the flows but adds no cost leg.

## 7. CommerceHub Accounting

Entry: `PostCommerceHubFinancialTransactionAsync` (`CommerceAccountingService.cs:1786-1852`): parses type/direction/entity/bucket enums, resolves accounts (`ResolveCommerceHubAccountCodes` `:1866-1913`), picks debit/credit by cash-vs-non-cash rule (`:1915-1931`), posts with `SourceType="CommerceHubFinancialTransaction"` (literal required by coverage check) and key `COMMERCEHUB_TX:{tenant}:{tx}` (`:1824-1835`).

Exact journal pattern table (code-derived; live shapes from Phase 2A §14 match):

| Shape (entity/type/bucket) | Debit | Credit | Notes |
| --- | --- | --- | --- |
| Platform collection (Cash) | `1030` Bank | `2030` Suspense | Greatone + Shopixy live |
| Vendor payable | `2030` | `2040` Marketplace vendor payable | Live |
| Commission/revenue | `2030` | `4020` E-commerce sales | Platform `Commission` type |
| Promotion | `6130`-family `Expenses` | `2030` | Platform `Promotion` type |
| Platform/vendor cash move | `1030` or `2040`/`2050`-receivable per bucket | `2030` (offset) | Direction rule `:1924-1930` |
| Payout | `2040` cleared vs cash/control | per transaction | Zero live payouts in audited tenants |
| Platform refund (observed) | `2030` | `1030` | One live refund each in greatone, Shopixy |
| **Merchandise COGS** | — | — | **No branch resolves `5010`/`1310`; `ResolveCommerceHubPrimaryAccount` (`:1933-1958`) can only return Bank / `2040` / vendor-receivable / `4020` / Expenses / `2030`. Proven absent.** |

Do-not-assume check: CommerceHub does **not** reuse the POS cost model — it has no cost input at all (§4.5), its revenue account is `4020` via commission split (not `4010` POS sale), its offset discipline is suspense-centered (`2030`), and its inventory path (`Sale` movements on delivery, return receipts) is quantity-only. Vendor payable/commission/platform-fee/payout/refund sub-ledgers (`CommerceHubVendorEarning`, `CommerceHubPayout*`, `CommerceHubFinancialTransaction` buckets) move money buckets; none moves merchandise cost.

## 8. Real Database Evidence

No new SQL was run in this session (task forbids business writes; read-only re-query was out of scope for this pass). The representative examples below are **Phase 2A attributed observations** (`PosEcommerceDb_Baseline`, capture 2026-09-14T13:11:38Z) plus **source-proven lineage** for the same entities. Anything not covered by either is marked UNKNOWN.

**Storefront example (Shopixy, attributed):** 3 posted COD orders totaling EGP 1,230 (`Order_COD`: Dr `1130` / Cr `4020`) + 5 pending/unposted orders totaling EGP 2,050. Lineage per order: `Order → OrderItem(s) (qty, UnitPrice, CostPrice snapshot) → InventoryReservation(s) → (on collect) committed stock → Order_Payment/Order_COD journal → (missing) COGS → (if refund) Order_Refund journal`. Missing links: stock-movement↔journal tie (no shared cost/idempotency key), COGS amount, refund cost reversal — all UNKNOWN — REQUIRES EVIDENCE at row level (source proves the columns/keys don't exist, so row re-query can only confirm absence, never produce a hidden link).

**CommerceHub examples (attributed):** greatone 3 marketplace orders / 8 payments / 1 refund / 7 GL transactions EGP 10,780; Shopixy 10 / 3 / 1 / 14 EGP 12,570.12; zero payouts/vendor settlements in all three tenants. Lineage per marketplace order: `MarketplaceOrder → SubOrder(s) → SubOrderItem(s) (qty, UnitPrice, commission — NO cost) → reservation → (on Delivered) Sale StockMovement + delivered-financials transaction(s) → CommerceHubFinancialTransaction journal(s) → (if refund) refund transaction journal`. Missing links: unit-cost source, movement↔journal cost tie, COGS/1310 effect, payout clearance — UNKNOWN — REQUIRES EVIDENCE (row level) / absent by design (source level).

**Cost values:** never invented. No per-row quantity×cost→GL reconciliation is possible today because the e-commerce GL has no cost leg to reconcile to. Tenant scoping of the Phase 2A capture was per-tenant (`TenantId` on every business/journal/ledger/outbox/payment/mapping query); this session adds the source-level tenant analysis in §13.

## 9. Historical Cost Snapshot

Architecture answer (source-proven, no mutation performed): **the correct COGS basis is the frozen line snapshot (50 in the task's example), never the later live cost (60).**

- POS already implements this: `CalculateTotalCost`/`CalculateReturnCost` prefer `CostAmountSnapshot` and only fall back to live `Variant.CostPrice` for legacy rows lacking snapshots (`CommerceAccountingService.cs:1672-1707`). Returns must match the original line snapshot (`SalesReturnsController.cs:1549-1577`).
- Storefront already captures the needed input (`OrderItem.CostPrice`, immutable) but has **no consumer**, so today the answer is "no COGS posted at all" rather than "50 vs 60".
- CommerceHub captures **nothing**, so even the 50 is unrecoverable from the sub-order row — the eventual design must either backfill authority from `Variant.CostPrice`-at-delivery (weak: not frozen at checkout) or add a checkout-time cost snapshot column (preferred: matches POS/Storefront pattern).
- Master-cost movement after the order (purchase receipt changing the weighted average) must not retroactively change posted COGS; reversal must reuse the original snapshot (POS return pattern). This is the invariant the §21 design encodes.

## 10. Partial Fulfillment

**Largely unsupported as an accounting concept; partially supported as an inventory concept.**

- Storefront fulfillment status is **order-level** (`Order.FulfillmentStatus`); no `FulfilledQuantity` / per-line fulfillment state exists on `OrderItem`. Repo-wide grep for `PartialFulfillment|FulfilledQuantity` returns nothing. The 5-vs-2 example therefore has no defined "first 2 / remaining 3" accounting split: payment allocation is per-item money (`PaidAmount`), reservation commit is per-reservation quantity (`CommittedQuantity`), but **COGS (missing) has no granularity to split**.
- Inventory reservations do track `Reserved/Committed/Released/Returned` quantities per reservation (`InventoryReservation.cs:15-28`), and CommerceHub delivery commits `remainingToCommit = request.Quantity − alreadyCommitted` (`InventoryConsistencyService.cs:1190-1200`) with a hard invariant requiring prior reservation (`:1301-1306`) — so a second fulfillment can commit a remainder operationally. But without a per-fulfillment cost event, each commit is quantity-only.
- Refund path is monetary (full vs partial by `totalRefundedAfterThisRefund + tolerance >= refundableAmount`, `PaymentLifecycleService.cs:964-966`; per-item split via `AllocateRefundAcrossItems`) with no fulfillment-quantity cross-check found. Cancellation of a remainder releases reservations (Storefront) / cancel-release (CommerceHub) with no revenue/COGS split because revenue was already recognized in full at collection.
- Verdict for the matrix: partial-fulfillment COGS/revenue/refund semantics are **undefined** — mark UNSUPPORTED — REQUIRES DESIGN (see findings F-08).

## 11. Returns / Refunds

| Case | Financial refund? | Inventory movement? | Stock return? | COGS reversal? | GL inventory increase? | Return-cost basis? |
| --- | --- | --- | --- | --- | --- | --- |
| Storefront refund before fulfillment (Collected, undelivered) | Yes (`EcommerceRefund`: Dr `4020` / Cr clearing or bank) | Reservations releasable via cancel/failure paths, but **refund itself calls no stock release** (`RecordRefundAsync` `:866-1015` contains outbox + status + allocation; no `_stockRepository` call in the refund branch) | No dedicated return receipt | **No** (absent) | **No** | N/A — nothing to reverse |
| Storefront refund after fulfillment (Collected + committed/delivered) | Yes, same journal | Committed stock stays deducted; no return movement emitted by refund | **No** | **No** | **No** | UNKNOWN — REQUIRES DESIGN (should be original `OrderItem.CostPrice`; no consumer exists to apply it) |
| CommerceHub refund | Yes (`CommerceHubFinancialTransaction` refund shape; observed Dr `2030` / Cr `1030`) | Separate: `ReceiveReturnAsync` (resellable/damaged split) exists in inventory service but is **not called by the refund/finance path** in the traced delivered→refund flow | Only if return flow invoked separately | **No** | **No** | UNKNOWN — REQUIRES DESIGN (no cost column to reuse) |
| POS return (reference) | Yes + tender/AR split | Yes (operational restore) | Yes | Yes (Dr `1310` / Cr `5010` from original snapshot) | Yes | Original `CostAmountSnapshot` |

Returned quantity therefore does **not** restore operational and financial inventory consistently in either e-commerce channel today: operational restore is possible but disconnected from the financial refund, and financial restore of cost is absent in both.

## 12. Cancellation

Cancellation and refund are distinct authorities and must not be conflated:

- **Storefront cancellation before fulfillment (uncollected/pending):** authoritative path is cancellation + `ReleaseOrderReservationsAsync` (checkout compensation `:6383`; payment-intent recovery `:374`; reservation cleanup job for expired/active-cutoff orders). No revenue was recognized (no collection journal), so no revenue reversal is needed; no COGS exists to reverse; stock restoration = reservation release (reserved quantity freed, on-hand unchanged because nothing was committed). Payment cancellation and inventory restoration are required to commit together — `OrderFulfillmentService.CancelAsync` refuses direct fulfillment-cancel and redirects to the authoritative order-cancellation service (`:535-555`).
- **Storefront cancellation after fulfillment (Collected + committed/delivered):** operationally a return, financially a refund. Current code has no combined "cancel-after-delivery restores stock + reverses COGS" path: `RecordRefundAsync` requires `Collected/Settled/PartiallyRefunded` (`:915-922`) and posts revenue reversal only; stock must be returned through a separate, unlinked step. COGS reversal and `1310` restoration are absent.
- **CommerceHub cancellation before delivery:** cancel branch releases reservations per item (`CommerceHubOrderManagementService.cs:177-190`), no delivered-financials applied (those apply only on `Delivered`, `:213-220`). Clean.
- **CommerceHub cancellation after delivery:** refund transaction reverses money buckets; return receipt (`ReceiveReturnAsync`) is a separate inventory event family (`StockIncreased/StockDamaged`); no COGS/1310 leg in either.
- Order-status vs fulfillment-status vs payment-status are three independent state machines with gated transitions (checkout-state machine `Order.cs:689-709`; payment interceptor; fulfillment interceptor); cancellation must coordinate all three, which only the authoritative cancellation service is allowed to do.

## 13. Tenant Isolation

Source-verified, tenant-scoped at every layer:

- Data: `Order.TenantId`, `OrderRefund.TenantId`, `StockMovement.TenantId` (non-null, DB-enforced), `InventoryReservation.TenantId`, `JournalEntry.TenantId` (idempotency lookup is always `TenantId + key`, `FinancialPostingService.cs:60-63`, `:109-112`; account load filters `a.TenantId == request.TenantId`, `:132-136`).
- Catalog authority: variant lookup constrains both `variant.TenantId` and `variant.Product.TenantId` (`CheckoutService.cs:5119-5126`).
- Outbox: `AccountingOutboxMessage.TenantId` + `BusinessEntityId`; order aggregate version resolved per-tenant (`ResolveRequiredOrderAggregateVersionAsync`); processor marks posting status only when `candidate.TenantId == message.TenantId && candidate.Id == message.BusinessEntityId` (`AccountingOutboxProcessor.cs:2957-2962`).
- Known weak point (carried from Phase 2A, not re-tested): `CommerceHubFinancialTransactions` has no `AccountingTenantId` column — attribution depends on the outbox hop. Cross-tenant join risk is therefore concentrated in CommerceHub finance reconciliation queries, which must always join via the outbox tenant key, never by transaction/reference id alone.
- **Regression design (for next phase, not implemented):** (i) attempt `PostCommerceHubFinancialTransactionAsync` with tenant A's transaction id under tenant B → must fail account resolution / coverage check; (ii) Storefront checkout with tenant A order id + tenant B variant id → must fail authoritative lookup (empty set → invalid-cart); (iii) journal idempotency probe: same key under different tenants → two distinct journals (no cross-tenant idempotent hit); (iv) outbox processor test: message tenant ≠ entity tenant → skip, no status write. Tenant A order must never consume Tenant B `CostPrice`: enforced because the only legal cost input is the tenant-filtered variant read at checkout.

## 14. Idempotency / Retry

| Boundary | Existing key / mechanism | Retry-safe? | COGS duplicate risk today |
| --- | --- | --- | --- |
| Payment capture (online) | Outbox `ONLINE_PAYMENT:{t}:{order}:{intent}` (`AccountingOutboxService.cs:316`) + journal `ONLINE_PAYMENT:{t}:{order}:{intent}` (`CommerceAccountingService.cs:1099`); aggregate version (`MutationVersion`) carried | Yes — duplicate intent replays hit journal idempotency (`EnsureIdempotentRequestMatches` also rejects same-key/different-shape) | None (no COGS leg) |
| COD delivered | `COD_DELIVERED:{t}:{order}` both layers (`:469`; `:1363`) | Yes | None |
| Wallet approved | `WALLET_APPROVED:{t}:{order}` (`:480`; `:1401`) | Yes | None |
| Stock deduction (Storefront) | Reservation event ids (`ReservationEventId/LastMutationEventId`); serializable transactions; deterministic lock order | Yes — commit/release are reservation-idempotent | N/A (quantity-only) |
| Stock deduction (CommerceHub) | `InventoryEventInbox` via `TryBeginEventAsync` + `InventoryEventIdFactory.Create("commercehub-delivery-commit", item.Id)`; `alreadyCommitted` remainder math; `Applied` flag | Yes — replay returns early (`AlreadyHandled`, `remainingToCommit<=0`) | N/A |
| COGS posting (e-commerce) | **No key, no event, no consumer** | N/A | Future risk: a naive fulfillment-triggered COGS post keyed only on order id would double-post on delivery-replay; design must key on fulfillment/reservation-commit event id (§21) |
| Refund | `ECOM_REFUND:{t}:{order}:{refundId}` both layers (`:491`, `:510`, `:1459`); existing-refund lookup by hash + legacy form (`PaymentLifecycleService.cs:902-913`); amount/currency invariant on legacy match | Yes — duplicate `refundId` returns idempotent success without state change | None (no cost leg to duplicate) |
| Return (inventory) | `ReceiveReturnAsync` event id + resellable/damaged split validation | Yes | N/A |
| Marketplace finance tx | `ledger:{scope}:{type}:{direction}:{refType}:{refId}` + `COMMERCEHUB_TX:{t}:{tx}` journal key | Yes — one tx → one message → one journal | None |
| Outbox processing | Claim lease (`TryClaim`), attempt count, exponential backoff / non-terminal financial retry (`RecordNonTerminalFinancialFailure`), `JournalId` linkage on success | Yes | A future COGS message must reuse this envelope (new `EventType`, same lease/version semantics) |

A retry today cannot create duplicate COGS/stock-movement/journal/outbox/refund **for the merchandise-cost legs because those legs do not exist**; the existing money/quantity legs are idempotent per the table above.

## 15. Outbox / Accounting Event Lineage

Canonical lineage: source transaction → durable message (same DB transaction) → processor dispatch → idempotent journal → source posting-status write.

| Source event | Outbox `EventType` | `BusinessEntityId` | Aggregate version | Tenant | Journal linkage | Processor |
| --- | --- | --- | --- | --- | --- | --- |
| Online collection | `OnlinePayment` | Order id | Required order version | Yes | `Order_Payment` journal; `Order.FinancialPosting{Status,JournalId,PostedAtUtc}` (`AccountingOutboxProcessor.cs:2945-3009`) | `:2510` → `PostOnlinePaymentCapturedAsync` |
| COD delivery | `CodDelivered` | Order id | Required | Yes | `Order_COD` journal; same status write | `:2560` → `PostCodDeliveredAsync` |
| Wallet approval | `WalletApproved` | Order id | Required | Yes | `Order_Wallet` journal; same status write | `:2610` → `PostWalletApprovedAsync` |
| Refund | `EcommerceRefund` (+ settled variant payload) | Order id | Required | Yes | `Order_Refund` journal; `OrderRefund.FinancialPosting{Status,JournalId}` | `:2671` → `PostEcommerceRefundAsync` |
| Settlement | `GatewaySettlement` / `CodSettlement` | Batch id | None (batch-scoped) | Yes | Settlement journals | Settlement handlers |
| CommerceHub finance | `CommerceHubLedgerPosting` (`SourceType=CommerceHubFinancialTransaction`) | Finance tx id | Per registration | Yes (via message) | `CommerceHubFinancialTransaction` journal, key `COMMERCEHUB_TX:{t}:{tx}` | Ledger-posting handler |
| POS sale/refund | `PosSale` / `PosRefund` | Invoice/return id | Versioned | Yes | `SalesInvoice[_COGS]` / `Return[_COGS]` journals | `:1115`, `:1623`, `:2027` |
| **E-commerce COGS** | **None** | — | — | — | **No durable accounting event today (verified: no `EventType` containing "Cogs", no COGS enqueue method on `IAccountingOutboxService`, no COGS case in processor dispatch `:2911-2943`)** | — |

Replay safety for existing events: journal idempotency + outbox claim/lease + aggregate-version staleness rejection. The e-commerce COGS gap is therefore also an **event-durability gap**: a crash between fulfillment commit and any future COGS post would have no outbox row to recover from — the §21 design closes exactly this.

## 16. Reporting Impact

Expected merchandise math for a sale at cost 50 / revenue 100 (payment-method invariant):

- P&L: Revenue +100 (`4020`), COGS +50 (`5010`), Gross profit +50. Card/Cash/COD/Wallet change only the debit leg (clearing/cash), never the `5010` amount — same cost authority for all methods (§task-16; POS already behaves this way across Cash/Card/Mixed/Credit, §5).
- Balance Sheet: Inventory (`1310`) −50; clearing/cash/bank +100 per collection path; unsettled clearing sits in the clearing bucket until settlement moves it to bank.
- Trial Balance: balanced pairs today (revenue pair only); with the fix, each sale contributes two balanced pairs (revenue + COGS), each return contributes their reversals.
- Cash Flow (current classifier, `FinancialReportService.cs:892-897` prefix `10`/`11`): collection into clearing is already treated as operating cash increase; settlement nets to fee-only movement. COGS itself is non-cash (inventory relief), affecting operating profit via working-capital change, not the cash line. Adding COGS journals therefore changes reported gross profit and inventory valuation but must not change reported cash — any implementation that routes COGS through a cash/clearing account would corrupt Cash Flow and is prohibited.
- Inventory valuation: operational reports value stock at `Variant.CostPrice × (Quantity + Damaged)` (`DashboardController.cs:175`; `ReportsController.cs:279-285`) — a live-cost valuation. GL inventory (`1310`) moves only via purchase journals (Dr `1310`) and POS COGS (Cr `1310`). E-commerce sales therefore create a **systematic operational-vs-GL divergence**: operational quantity falls with no GL relief, and the dashboard valuation (live cost) drifts from any future snapshot-based GL. The fix must reconcile these two valuations explicitly rather than pretend they already agree.

## 17. POS vs Storefront vs CommerceHub

| Capability | POS | Storefront | CommerceHub |
| --- | --- | --- | --- |
| Revenue | Yes — Dr cash/`1110`/`1410` / Cr `4010` + tax (`PostPosSaleAsync`) | Yes — Dr clearing / Cr `4020` (online/COD/wallet + suspense pair) | Yes — commission/revenue via `2030`↔`4020`; collection Dr `1030`/Cr `2030` |
| Inventory movement | Yes — sale deduction + return restore (operational + GL together) | Yes (operational only) — reserve → commit on collect → release on cancel | Yes (operational only) — reserve → `Sale` movement on Delivered → return receipt / cancel-release |
| COGS | Yes — Dr `5010` / Cr `1310` Journal 2 | **Absent (proven)** | **Absent (proven)** |
| Inventory GL | Yes — Cr `1310` on sale | **Absent** | **Absent** |
| Cost snapshot | Yes — `CostPriceSnapshot` + `CostAmountSnapshot`, immutable | Yes (captured, unused) — immutable `OrderItem.CostPrice` | **Absent — no column (proven)** |
| Refund | Yes — revenue + COGS reversal | Revenue only (Dr `4020` / Cr clearing-or-bank) | Money-bucket only (observed Dr `2030` / Cr `1030`) |
| Return stock | Yes | Disconnected (refund posts no stock move) | Disconnected (refund posts no stock move) |
| COGS reversal | Yes — Dr `1310` / Cr `5010` | **Absent** | **Absent** |
| Outbox | `PosSale` / `PosRefund` (+ versioned) | Revenue/refund/settlement only | `CommerceHubLedgerPosting` only |
| Idempotency | `SALE`/`COGS`/`REFUND`/`REFUND_COGS` keys + journal shape-match | Money/quantity keys (no COGS key) | Tx/inbox keys (no COGS key) |

No UNKNOWN remains in this table at the source level; row-level live amounts remain UNKNOWN — REQUIRES EVIDENCE per §8.

## 18. Findings

DO NOT FIX — classification only. IDs are T-08 scoped (T-08-Fxx) to avoid colliding with Phase 2A P2A-xx.

| ID | Severity | Channel | Event | Observed | Expected | Evidence | Financial impact | Scope | Recommended next phase |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T-08-F01 | P0 | Storefront | Sale COGS | Collection posts Dr clearing / Cr `4020` with no `5010`/`1310` leg | Every fulfilled merchandise sale posts Dr `5010` / Cr `1310` from immutable `OrderItem.CostPrice × qty` | `CommerceAccountingService.cs:1076-1416` (no COGS lines); Phase 2A: Shopixy 3 posted COD orders, no COGS journal | Gross profit overstated; `1310` overstated by full unrelieved cost of all fulfilled e-commerce sales | All fulfilled Storefront sales, all payment methods | T-09 design/build: snapshot→event→posting→outbox (see §21) |
| T-08-F02 | P0 | CommerceHub | Sale COGS | Delivered financials + `Sale` stock movement with no cost leg; sub-order item has no cost field | Same COGS expectation, with a defined cost authority (new snapshot column or explicitly adopted alternative) | `CommerceAccountingService.cs:1866-1958` (no COGS branch); `CommerceHubSubOrderItem.cs` (no cost); `InventoryConsistencyService.cs:1267-1286` (no Price) | Same overstatement classes as F01, plus unquantifiable basis | All delivered CommerceHub sub-orders | T-09: cost-authority decision (snapshot column preferred) then posting |
| T-08-F03 | P0 | Storefront | Refund after fulfillment | Dr `4020` / Cr clearing-or-bank; committed stock stays deducted; no `1310`/`5010` reversal | Valid return restores operational stock and posts Dr `1310` / Cr `5010` at original snapshot cost | `PaymentLifecycleService.cs:866-1015` (no stock call); `PostEcommerceRefundAsync` `:1424-1475` (revenue-only lines) | Net inventory understated operationally; `1310` understated and `5010` overstated after returns | All post-fulfillment refunds | T-09: linked return→stock-receipt→reversal event |
| T-08-F04 | P0 | CommerceHub | Refund/return after delivery | Money-bucket reversal only; return receipt is a separate unlinked event | Same linked expectation at original cost basis | §7 shapes; `ReceiveReturnAsync` vs refund path separation | Same as F03 | All post-delivery refunds/returns | T-09: same linkage |
| T-08-F05 | P1 | Both | Reporting | P&L gross profit wrong; BS inventory wrong; operational valuation (live cost) vs GL (stale/unrelieved) diverge | §16 math; disclosed reconciliation between valuations | §16 + Phase 2A §§17-18 | Misstated margin/inventory; analyst confusion | All e-commerce volume | T-09: reporting-note + reconciliation query (read-only) |
| T-08-F06 | P1 | CommerceHub | Cost-column absence | No unit-cost column on sub-order item; delivery request carries no cost | Immutable cost snapshot per marketplace line (or formally adopted WAC-at-delivery with freeze proof) | `CommerceHubSubOrderItem.cs` full read | Blocks any correct COGS/reversal; forces current-cost fallback if built naively | Schema + checkout | T-09: migration design (not this phase) |
| T-08-F07 | P1 | Storefront | Refund-before-fulfillment stock | Refund posts money reversal but releases no reservation | Refund-before-commit should release the linked reservation (or explicitly document release-by-cancellation ordering) | Refund branch has no `_stockRepository` call | Reservations stranded until expiry/cleanup; available-stock understated in window | Pre-fulfillment refunds | T-09: refund→release linkage or ordered-cancel requirement |
| T-08-F08 | P2 | Both | Partial fulfillment | No per-line fulfilled-quantity accounting; revenue recognized in full at collection | Defined partial semantics (per-commit COGS event; remainder/cancel/refund rules) or explicit single-shot-delivery constraint | Grep: no `FulfilledQuantity`/`PartialFulfillment`; order-level `FulfillmentStatus` | Undefined margin on partials; second-fulfillment double/missing risk in future build | Fulfillment + refund | T-09: specify before building COGS consumer |
| T-08-F09 | P2 | Both | COGS idempotency design | No COGS key/event exists, so no duplicate can occur today — but no safe key is reserved | Future COGS event keyed on fulfillment/commit event id (not order id alone), with journal shape-match | §14 table; `InventoryEventIdFactory` pattern available | Future double-COGS risk if keyed wrong | Next-phase build | T-09: key spec (§21) |
| T-08-F10 | P2 | CommerceHub | Tenant attribution | Finance tx lacks `AccountingTenantId`; attribution via outbox hop | Either add tenant column or certify the outbox-hop join as the exclusive path with a guard test | Phase 2A §14 + §13 analysis | Cross-tenant cost/join risk in ad-hoc queries | Finance reconciliation | T-09: guard test + query standard |
| T-08-F11 | P3 | Both | UI/evidence gap | No COGS/inventory-cost surface on Storefront order / CommerceHub finance screens (presentation shows revenue/stock separately) | Read-only cost-lineage view (line → snapshot → movement → journal-or-absent) once posting exists; today: explicit "no COGS posted" disclosure | Phase 2A §20 route map; no COGS UI strings found | Operators cannot see the gap | Admin/CommerceHub UI | T-10 or later: read-only lineage view (no posting UI) |

## 19. Historical Boundary

- **Authoritative (immutable, must not be rewritten):** all posted revenue/clearing/suspense/settlement journals; all `CommerceHubFinancialTransaction` rows and their journals; all `StockMovements` / reservation rows; all `OrderItem.CostPrice` snapshots (Storefront); all POS COGS pairs. Reversal discipline is compensating-journal-only (POS pattern); line mutation and history rewrite are prohibited.
- **Missing lineage (acknowledged, not repaired):** every fulfilled-but-unrelieved e-commerce sale (Storefront all methods; CommerceHub all delivered sub-orders) and every post-fulfillment return lacks its `5010`/`1310` pair. The EGP magnitude is unquantified in this session (requires a read-only quantity×snapshot valuation query in a later phase — no cost invention, no corrective journals in discovery).
- **Correction workflow:** no approved application-level historical-COGS correction workflow was found (manual inventory adjustment explicitly rejects GL posting without an authoritative SQL workflow, `CommerceAccountingService.cs:1627-1637` containment; supplier-card funding outbox omission in Phase 2A remains an unrepaired lineage exception by policy). If remediation is ever authorized, it must follow the POS compensating-journal pattern through `FinancialPostingService` with distinct correction idempotency keys and full tenant scoping — designed in T-09 at the earliest, never in T-08.

## 20. Browser Discovery

**Limitation documented:** no legitimate browser session was available/used in this single-agent discovery pass. No Storefront order pages, CommerceHub finance/order pages, stock-history screens, or refund screens were inspected at runtime, and no presentation/API read behavior beyond source/route evidence (Phase 2A §20 route map) is claimed. Required read-only browser verification (revenue vs stock vs — absent — COGS presentation; refund-screen behavior; clearing-aging drill-down) is deferred to the next phase's certification step and does not block the source-proven verdict above.

## 21. Minimal Safe Design

Recommended smallest architectural correction (design only — DO NOT BUILD in T-08). Follows the POS two-journal pattern and the existing outbox+idempotent-journal discipline; adopts **weighted-average `Variant.CostPrice` frozen at checkout/delivery-commit** because source evidence supports it (§4), not FIFO/LIFO (no evidence for either).

**A. Fulfilled merchandise sale (Storefront):**
`OrderItem.CostPrice (existing immutable snapshot) × fulfilled qty` → new durable event `EcommerceCogs` (outbox `EventType`, `BusinessEntityId=OrderId`, order aggregate version, tenant, `IdempotencyKey=ECOM_COGS:{tenant}:{order}:{fulfillmentCommitEventId}`, payload = per-line snapshot costs) → new processor branch → `FinancialPostingService` → **Journal: Dr `5010` / Cr `1310`** (`SourceType=Order_COGS`, same idempotency key; shape-match guard on) → `AccountingOutboxMessage.JournalId` linkage → order/financial status extension (new status field or sidecar — must not overload `FinancialPostingStatus`, which today means collection). Trigger: reservation-commit (payment success for prepaid; delivery for COD), exactly once per commit event id — never on checkout-create, never on fulfillment-status alone.

**B. Valid return (Storefront):** return receipt (new or existing return-request flow) → `EcommerceCogsReversal` event (`IdempotencyKey=ECOM_COGS_REV:{tenant}:{order}:{returnEventId}`, payload reuses the **original per-line snapshot**, never live cost) → **Journal: Dr `1310` / Cr `5010`** (`SourceType=Order_COGS_Reversal`). Wire refund→return linkage (close F07): pre-commit refund releases reservations; post-commit refund requires (or auto-creates) the return receipt before the reversal posts.

**C. CommerceHub:** first add the missing authority — new immutable `UnitCostSnapshot` (+ `CostAmountSnapshot`) columns on `CommerceHubSubOrderItem`, populated at checkout from tenant-filtered `Variant.CostPrice` (same lookup discipline as Storefront); backfill policy for existing rows: **none** (historical boundary, §19 — old rows stay cost-unknown). Then mirror A/B with `CH_COGS:{tenant}:{subOrder}:{deliveryCommitEventId}` keys and `SourceType=CommerceHub_COGS[_Reversal]`, reusing the `2030`-free `5010`/`1310` pair. Delivery-commit (`CommitDeliveryAsync` event id) is the trigger, matching the existing delivered-financials boundary.

**D. Non-goals enforced:** no changes to revenue/clearing/settlement journals; no Cash-Flow classifier change (COGS journals touch only `5010`/`1310`); no FIFO/LIFO switch; no historical back-posting; no generic-inventory-adjustment GL path (containment stands); no T-06/T-07/1020/1110 touch; idempotency keys include the commit/return event id (never order id alone) to survive delivery replays and partial commits (F08/F09); all queries/keys tenant-scoped (F10 regression tests in §13 become build acceptance).

If later evidence ever proves a different authoritative method (e.g., batch-level actual cost), substitute only the snapshot-population step — the event→posting→outbox→idempotency skeleton is unchanged.

## 22. Final Verdict

| Criterion | Result |
| --- | --- |
| Order-to-inventory lineage fully understood | **Pass** — §3 tables + payment/fulfillment boundary proof |
| Cost authority explicitly proven | **Pass** — `Variant.CostPrice` (WAC) → immutable line snapshot; CommerceHub absence proven (§4) |
| Storefront accounting mapped | **Pass** — exact shapes + absence proof (§6) |
| CommerceHub accounting mapped | **Pass** — exact pattern table + no-COGS-branch proof (§7) |
| Real DB examples support the mapping | **Partial pass** — Phase 2A attributed live examples (§8); no new row capture in this session (marked where UNKNOWN) |
| COGS/inventory gaps classified | **Pass** — F01/F02/F05/F06 (§18) |
| Refunds/returns classified | **Pass** — F03/F04/F07 + §11 matrix |
| Cancellation behavior classified | **Pass** — §12 (before vs after fulfillment, both channels) |
| Idempotency mapped | **Pass** — §14 (existing keys + reserved future-key spec) |
| Outbox lineage mapped | **Pass** — §15 (durable-event gap proven) |
| Reporting impact mapped | **Pass** — §16 (method-invariant math + valuation divergence) |
| Tenant isolation understood | **Pass** — §13 + regression design |
| Minimal safe design defined | **Pass** — §21 (no build) |
| No hidden assumptions remain | **Pass** — every UNKNOWN is labeled UNKNOWN — REQUIRES EVIDENCE; no cost values invented; no guessed flows |

**Overall: FAIL (as a financial-integrity gate) / COMPLETE (as a discovery phase).** The system must not be certified for e-commerce merchandise margin or GL inventory until F01–F04 are remediated through the §21 design in a later phase. T-08 ends here: discovered, mapped, reconciled, classified, designed — **nothing implemented.**
