# PASS P1-EC POS Purchasing Authority Certification

Date: 2026-06-07

Verdict: Production-certifiable after migration
`20260607230000_PosPurchasingAuthorityHardening` is applied.

## Supplier Authority Findings

- Supplier ownership is tenant-bound through trusted tenant context, query filters, and save-time
  co-ownership validation.
- Tenant A cannot use Tenant B suppliers.
- Soft-deleted suppliers are excluded from purchase authority and cannot receive purchases.
- Supplier create/update and financial mutation require `Owner,Admin,Manager`.
- Supplier has no explicit inactive state. That is lifecycle debt, not an authority bypass.

## Purchase Lifecycle Findings

- A valid purchase requires current supplier, active warehouse, current product/variant/unit
  lineage, positive quantity and cost, valid tax, and current tenant authority.
- Purchase creation is the receiving transition and creates `InvoiceStatus.Completed`.
- Updates require durable idempotency and `ExpectedMutationVersion`.
- Returned, partially returned, fully returned, and cancelled purchases cannot be rewritten.
- Completed purchase deletion/cancellation is blocked; correction uses a locked compensating
  reversal and replacement transaction.

## Receiving Findings

- Receiving cannot execute twice for the same idempotency authority.
- Product cost rows and variant stock rows are locked in deterministic order.
- Receiving changes stock and appends `StockMovementType.Purchase` in the same transaction.
- Multi-line same-product receipts use an in-transaction stock-total ledger, preventing stale
  average-cost calculation.
- Partial receiving is not a runtime lifecycle in this model. Each purchase creation is a complete
  receipt document.

## Cost Authority Findings

- DTO cost is validated input, not unchecked authority: unit cost must be positive and SQL checks
  reject invalid historical rows.
- Unit conversion requires a current ProductUnit binding. Unknown unit IDs do not fall back to a
  conversion factor of one.
- Average cost mutation is product-row serialized and uses locked current stock.
- Purchase returns reverse historical receipt economics, not caller-supplied unit price or tax.
- Receipt correction and reversal account for each preceding same-product line in the transaction.

## Purchase Return Findings

- Every purchase return requires and locks an original same-tenant PurchaseInvoice.
- Supplier and warehouse are derived from the original purchase; mismatched DTO IDs are rejected.
- Product, variant, unit conversion, subtotal, tax, and base cost derive from historical
  PurchaseInvoiceItem rows.
- Duplicate product/variant request lines are rejected.
- Prior committed returns plus the current request cannot exceed aggregate received base quantity.
- Returns append `StockMovementType.PurchaseReturn`, update stock under lock, advance original
  invoice mutation version, and transition it to partially or fully returned.
- Purchase return lineage now uses `OriginalPurchaseInvoiceId`, not the SalesInvoice FK.

## Inventory Boundary Findings

- Purchases cannot create stock without locked ProductVariantStock mutation and an append-only
  movement.
- Warehouse, product, and variant lineage reuse PASS P1-EB inventory authority.
- Purchases cannot bypass warehouse or variant lineage.
- Inventory itself was not recertified; PASS P1-EB remains authoritative truth.

## Concurrency Findings

- Simultaneous receives are separated by durable request identity and serialize on product and
  stock authority.
- Simultaneous returns serialize on the original purchase invoice, product, and stock rows.
- Cumulative return checks execute while the purchase invoice is locked.
- Cancellation/update races fail through required mutation version and lifecycle gates.
- No inspected race can create duplicate stock or duplicate reversal.

## Adversarial Findings

- Supplier ID guessing and purchase ID guessing cannot cross tenant query authority.
- Route and DTO supplier/warehouse tampering is rejected or replaced by original SQL lineage.
- DTO unit price and tax cannot manipulate purchase-return liability reversal.
- Duplicate lines cannot evade quantity bounds.
- Replay is rejected by durable purchase and purchase-return idempotency records.
- Sales and sales-return observations are `Deferred To P1-ED`.

## Runtime Hardening Performed

- Added privileged roles to supplier, purchase, and purchase-return mutation.
- Required active warehouse authority and valid ProductUnit binding.
- Required positive receipt cost and added SQL financial checks.
- Serialized average-cost mutation and fixed same-product multi-line cost calculations.
- Required purchase update mutation version and blocked updates after returns.
- Bound returns to original purchase SQL lineage and historical economics.
- Added cumulative return bounds, duplicate-line rejection, and authoritative return response.
- Added tenant-composite purchase-return FK and save-time co-ownership validation.

## Exact Runtime Files Modified

- `PointOfSale.Core/Entities/Return.cs`
- `PointOfSale.API/Controllers/PurchasesController.cs`
- `PointOfSale.API/Controllers/PurchaseReturnsController.cs`
- `PointOfSale.API/Controllers/SuppliersController.cs`
- `PointOfSale.API/Mappers/SafeResponseMapper.cs`
- `PointOfSale.API/Services/ValidationService.cs`
- `PointOfSale.API/Services/FinancialIntegrityService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.Infrastructure/Migrations/20260607230000_PosPurchasingAuthorityHardening.cs`
- `PointOfSale.Web/src/pages/PurchaseInvoice.jsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosPurchasingAuthorityCertificationTests.cs`
- Updated `PurchaseReturnReliabilityHardeningTests`.
- Updated purchase-return integration lineage assertions.
- Repaired purchase financial guard fixtures to satisfy certified tenant co-ownership.

## Architecture Updates

- Updated `PROJECT_CONTEXT.md`, `ARCHITECTURE_TRUTH.md`, `KNOWN_RISKS.md`,
  `CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `CODEX_RULES.md`.

## Remaining Debt

- Apply and prove the new migration in staging and production.
- Add PostgreSQL-backed simultaneous receive/return chaos runs to deployment evidence.
- Add production reconciliation for purchase quantities, movements, supplier liability, and cost.
- Consider an explicit Supplier inactive lifecycle instead of soft deletion only.

## Required Answers

- Can Tenant A use Tenant B suppliers? No.
- Can deleted suppliers receive purchases? No.
- Can receiving execute twice? No for the same replay identity.
- Can purchases inflate inventory? No through the certified runtime path.
- Can purchase returns execute twice? No for the same replay identity.
- Can returns exceed received quantity? No.
- Can purchases bypass inventory authority? No.
- Can race conditions create inventory drift? No known inspected path.
- Were runtime code changes required? Yes.
- Is Purchasing Authority production-certifiable? Yes, after the migration is applied.
