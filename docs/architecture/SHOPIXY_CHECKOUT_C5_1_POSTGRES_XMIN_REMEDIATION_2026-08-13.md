# SHOPIXY Checkout C5.1 PostgreSQL xmin Remediation

Date: 2026-08-13

## 1. Exact runtime failure

Live Storefront checkout failed on:

- Endpoint: `POST /api/storefront/checkout`
- Result: `HTTP 500`, `ORDER_CREATION_FAILED`
- Trace: `00-2fb2826a4f557c235ef9cea3bcf873e1-5737d0e116ee2f96-01`
- First exception: `System.InvalidOperationException: The required column 'xmin' was not present in the results of a 'FromSql' operation.`

The confirmed path was:

`CreateOrderAsync -> RevalidateCheckoutAuthorityInsideTransactionAsync -> RecalculatePricesInternalAsync -> LoadAuthoritativeCheckoutVariantLookupAsync -> LockCheckoutCatalogAuthorityRowsAsync -> LockCheckoutProductAuthorityRowsBatchAsync`

The prior checkout stages had already succeeded: durable idempotency replay, storefront availability, lock acquisition, cart snapshot, idempotency claim, phone block validation, and address geographic admission.

## 2. Exact raw SQL

The failing SQL in `CheckoutService.LockCheckoutProductAuthorityRowsBatchAsync` was:

```sql
SELECT *
FROM "Products"
WHERE "TenantId" = '{tenantId}'
  AND "Id" IN ({productIdsList})
  AND "IsDeleted" = false
ORDER BY "Id"
FOR UPDATE
```

It materialized `_dbContext.Products` with `FromSqlRaw(...).IgnoreQueryFilters().ToListAsync(...)`.

The fixed SQL is:

```sql
SELECT xmin, *
FROM "Products"
WHERE "TenantId" = '{tenantId}'
  AND "Id" IN ({productIdsList})
  AND "IsDeleted" = false
ORDER BY "Id"
FOR UPDATE
```

## 3. Why EF required xmin

`Product.RowVersion` is mapped as a PostgreSQL `xmin` row version in `ApplicationDbContext`. PostgreSQL system columns are not returned by `SELECT *`, so EF full entity materialization could not populate the mapped concurrency token. This is a SQL/entity projection contract violation, not a concurrency design problem.

## 4. Repository-wide xmin scan

Scanned for `FromSql`, `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteSql`, `SELECT *`, `FOR UPDATE`, `FOR NO KEY UPDATE`, `FOR UPDATE SKIP LOCKED`, and `xmin`.

Classification:

- Missing xmin, fixed: checkout product lock, checkout promotion capacity lock, coupon capacity lock, payment promotion usage lock, order lock helper, payment-intent recovery order lock, order status sync order lock, unresolved payment reconciliation order/payment intent locks, fulfillment order lock, inventory cleanup order authority lock, carrier shipment order lock, accounting order posting lock.
- Safe: PaymentLifecycleService order/payment intent locks already used `SELECT xmin, *`; AdminOrderService order lock already used `SELECT xmin, *`; product variant mutation locks already selected `xmin`.
- Intentionally projection-only: checkout variant authority lock composes `ProductVariants.FromSqlRaw(...)` into `{ Id, ProductId }` and does not full-materialize the entity.
- Not entity materialization or non-xmin entity: scalar `SqlQuery` availability checks, inventory reservation locks, product stock locks, payment webhook log locks, shipment locks, payment settlement batch locks, and SQL Server lock branches.
- Out of C5.1 scope: unrelated admin/product/purchase/sales/CMS raw SQL paths still use `SELECT *`; they were not changed unless they were a confirmed xmin-backed checkout/order/payment authority path.

## 5. Product authority fix

`LockCheckoutProductAuthorityRowsBatchAsync` still:

- Resolves product IDs from locked canonical variants.
- Applies tenant and deletion predicates.
- Orders locks deterministically by product ID.
- Uses PostgreSQL `FOR UPDATE`.
- Locks inside the existing checkout transaction.
- Materializes `Product` with `xmin`.

No optimistic concurrency mapping, inventory path, price authority, transaction boundary, or lock semantics were weakened.

## 6. Other fixes

Added explicit `xmin` to proven xmin-backed PostgreSQL full entity lock queries:

- `Promotions`
- `Coupons`
- `Orders`
- `PaymentIntents`

No schema or migration changes were made.

## 7. Tests

Added regression coverage in `CheckoutConcurrencyHardeningSourceTests` for:

- Product authority raw SQL includes `xmin`.
- Promotion/coupon capacity locks include `xmin`.
- Order/payment lifecycle locks include `xmin`.
- Inventory locks preserve `FOR UPDATE` and are classified as no mapped xmin.

Verification run:

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests` - PASS, 27/27.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~CheckoutSessionConsistencyIntegrationTests|FullyQualifiedName~CodPaymentLifecycleTests|FullyQualifiedName~InventoryReservationLifecycleIntegrationTests|FullyQualifiedName~ManualWalletPaymentConcurrencyIntegrationTests"` - PASS, 39/39.

## 8. PostgreSQL verification

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj` passed with:

`No changes have been made to the model since the last migration.`

A dedicated live PostgreSQL COD checkout was not completed in this run.

## 9. Live COD Place Order verification

Blocked in this run. The code fix and focused backend verification are complete, but the requested live browser/API COD Place Order flow was not executed here.

Required live evidence remains:

- Successful `POST /api/storefront/checkout`
- Created order and order item
- Correct totals
- Correct warehouse
- Correct inventory reservation
- Correct COD payment lifecycle
- No Paymob execution

## 10. EF/migration

Code-only remediation. No migration was created.

EF pending-model check: PASS.

## 11. Worktree

Before C5.1 work:

- Worktree was already dirty across API, infrastructure, Storefront, Web, tests, and docs.
- Initial diff stat: 67 files changed, 2862 insertions, 652 deletions.
- Several untracked files and prior architecture reports were already present.

After C5.1 work:

- Diff stat includes the pre-existing dirty worktree plus this remediation.
- No reset, clean, restore, stash, commit, or push was performed.

## 12. Remaining limitations

- `PointOfSale.Web` `npm run test` is red in unrelated frontend/i18n/warehouse certification tests.
- `PointOfSale.Web` `npm run build` passes.
- `PointOfSale.Storefront` `npm run test` and `npm run build` pass.
- Live COD Place Order verification remains pending.
- Paymob remains intentionally untested.

## 13. Final certification

BLOCKED

Reason: the xmin SQL/entity materialization issue is fixed and focused backend/storefront verification passes, but the requested live COD Place Order verification was not completed and `PointOfSale.Web` test verification remains red in unrelated areas.
