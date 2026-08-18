# SHOPIXY Checkout C5.2 Order Number Sequence Remediation

Date: 2026-08-13  
Status: REMEDIATION COMPLETE — RUNTIME VERIFIED

## 1. Executive Summary

Storefront checkout was failing after inventory validation/reservation and order allocation because `DocumentNumberingSupport.GetNextSequenceValueAsync(...)` requested PostgreSQL sequence `order_number_seq`, and the runtime database did not contain that relation.

The active source tree had no applied current migration owning this sequence. The original document-numbering migration was present only in recovery archive material. A new current EF repair migration was created and applied to the verified runtime database. After that, checkout progressed past document numbering; two additional runtime blockers surfaced and were remediated in the checkout path: idempotency cleanup no longer saves unrelated dirty immutable snapshots, and checkout snapshot/event ordering now preserves snapshot immutability and authoritative event versions.

A fresh COD checkout now returns HTTP 200 and created order `c481589a-1a46-4c3e-a8e6-33bf8c44dbc3` with order number `ORD-83B3-2026-01005`.

## 2. Runtime DB Identity

Verified runtime identity:

- Host: `192.168.1.5`
- Port: `5432`
- Database: `PosEcommerceDb_Baseline`
- Schema: `public`
- Server: PostgreSQL `18.4`
- Time zone: `Africa/Cairo`
- Environment: Development
- Credentials: not recorded

The runtime database matched the expected target before mutation.

## 3. Document Numbering Contract

`PointOfSale.API.Infrastructure.DocumentNumbers.DocumentNumberingSupport` defines:

- `order_number_seq`
- `return_number_seq`
- `stock_transfer_number_seq`

For PostgreSQL, the implementation manually executes `SELECT nextval('<sequence>')` inside the current EF transaction when one exists. Missing sequence SQLSTATE `42P01` or `42704` is converted into an explicit application error requiring the document-numbering migration.

Checkout order numbers are formatted as:

- `ORD-{tenantPrefix}-{yyyy}-{sequenceValue:D5}`
- Example: `ORD-83B3-2026-01005`

The sequence is global. Tenant isolation is enforced by the formatted tenant prefix and by the existing filtered unique index on active orders: `IX_Orders_TenantId_OrderNumber`.

## 4. Responsible Migration

Archived authoritative source found:

- `docs/recovery/archive/RECOVERY-4_PHASE_A/PointOfSale.Infrastructure/Migrations/20260409164612_AddReliableDocumentNumberSequences.sql`
- Created `order_number_seq` with `START WITH 1000`, `INCREMENT BY 1`, `MINVALUE 1000`
- Aligned state to the maximum parseable existing order suffix using `setval`

Active-source repair migration created:

- `PointOfSale.Infrastructure/Migrations/20260813140629_SHOPIXY_OrderNumberSequenceRemediationC52.cs`
- `PointOfSale.Infrastructure/Migrations/20260813140629_SHOPIXY_OrderNumberSequenceRemediationC52.Designer.cs`

The repair migration creates only `public.order_number_seq` when missing and aligns it to existing order numbers.

## 5. EF Migration History

Before remediation, runtime latest migration was:

- `20260811215156_SHOPIXY_ShippingClosureRemediationStep2N`

After remediation, runtime latest migration is:

- `20260813140629_SHOPIXY_OrderNumberSequenceRemediationC52`

Archived migration IDs `20260409164612_AddReliableDocumentNumberSequences` and `20260409210000_AddTenantScopedOrderNumberIndex` were not present in the active runtime history. The cause was source/runtime convergence drift: the sequence contract survived in application code, while the owning source migration was only in recovery archive material and not represented in current runtime schema.

## 6. Sequence Catalog Forensics

Post-remediation catalog verification:

- Schema: `public`
- Name: `order_number_seq`
- Relation type: sequence, `relkind = S`
- Data type: `bigint`
- Start value: `1000`
- Min value: `1000`
- Max value: `9223372036854775807`
- Increment: `1`
- Cache: `1`
- Cycle: `false`
- Ownership: no owned table/column dependency
- Explicit `information_schema.usage_privileges` rows: none

Related document-numbering sequence drift:

- `return_number_seq` missing from runtime
- `stock_transfer_number_seq` missing from runtime

Those were documented but not remediated because this step was scoped to Storefront Order creation.

## 7. Existing Order Number Safety

Before sequence repair, runtime `Orders` had no active rows, so there was no collision risk at `1000`.

After the successful live checkout:

- Total orders: `1`
- Active orders: `1`
- Parseable order numbers: `1`
- Minimum suffix: `1005`
- Maximum suffix: `1005`
- Duplicate active `(TenantId, OrderNumber)` rows: none
- Sequence state: `last_value = 1005`, `is_called = true`
- Inferred next value: `1006`

Sequence gaps from failed/rolled-back attempts are acceptable under the current `nextval`-based contract.

## 8. Migration SQL Inspection

Generated idempotent SQL was inspected before applying:

- `.codex-temp/C5_2_order_number_sequence.sql`

Expected change was limited to:

- `CREATE SEQUENCE IF NOT EXISTS public.order_number_seq START WITH 1000 INCREMENT BY 1 MINVALUE 1000 NO MAXVALUE CACHE 1 NO CYCLE`
- Safe alignment to the maximum existing parseable `Orders.OrderNumber` suffix

No manual `CREATE SEQUENCE` was executed outside EF migration application.

## 9. Preflight

Preflight checks confirmed:

- Correct database identity: `192.168.1.5:5432 / PosEcommerceDb_Baseline / public`
- Responsible active repair migration existed before application
- Runtime history did not already include the repair migration
- `order_number_seq` was absent from runtime catalogs before repair
- Existing order numbers could not collide with start value
- Worktree was already dirty from broader C-series work and was preserved

## 10. Remediation Applied

Applied via EF:

```powershell
dotnet ef database update 20260813140629_SHOPIXY_OrderNumberSequenceRemediationC52 --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build
```

Additional runtime blockers exposed after sequence repair and remediated:

- `CheckoutRequestIdempotencyCoordinator.DeleteClaimAsync` now uses provider-native guarded delete for uncompleted owned claims, avoiding unrelated tracked snapshot writes during cleanup.
- `CheckoutService` marks warehouse resolution ordered before immutable checkout snapshots are refreshed.
- `CheckoutService` explicitly marks newly refreshed checkout snapshot entities as added.
- `OrderCreatedEvent` now uses the tracked aggregate mutation version instead of emitting version `2` against resolved version `1`.

## 11. Sequence Verification

Post-migration verification matched source intent:

- `public.order_number_seq` exists
- Increment `1`
- Start/min `1000`
- No cycle
- Cache `1`
- Bigint max
- No owner dependency
- Runtime `nextval` contract is satisfied

The API no longer logs the missing-sequence/`42P01` document-numbering failure.

## 12. EF Convergence

Command:

```powershell
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build
```

Result:

```text
No changes have been made to the model since the last migration.
```

Source migration state, runtime `__EFMigrationsHistory`, and runtime schema now converge for `order_number_seq`.

## 13. API Startup

API restarted against the verified runtime database and listened on:

- `http://localhost:5010`

Startup completed without migration errors and without `DocumentNumberingSupport` or `order_number_seq` missing-sequence errors.

Observed unrelated background error after order success:

- `ShipmentAuthorityRecoveryHostedService` repeatedly fails on a separate shipping projection query: `42703 column p.xmin does not exist`

Shipping was explicitly out of scope for this remediation and was not modified.

## 14. Live COD Place Order

Fresh Storefront checkout session:

- Session: `c5-2-existing-ad991ca5d8af4d6d94b2ae4e75b5830b`
- Cart: `c8626610-e27e-4648-b8c6-b31e0f44194c`
- Checkout session: `3b464cef-df04-4781-b21d-293f1320c621`
- Shipping method: `1cd5518d-757c-4f1e-be9e-691b8275dd7d`
- Warehouse resolution: `ffec7ae1-f1a4-4555-8e17-02d81b840465`

Checkout result:

- `POST /api/storefront/checkout` returned `200`
- Order ID: `c481589a-1a46-4c3e-a8e6-33bf8c44dbc3`
- Order number: `ORD-83B3-2026-01005`
- Status: `PaymentPending`
- Payment status: `Pending`
- Currency: `EGP`
- Subtotal: `400.00`
- Shipping: `10.00`
- Tax: `0.00`
- Discount: `0.00`
- Total: `410.00`
- Payment method: `COD`
- Payment intent provider: `Cod`
- Payment intent amount: `410.00 EGP`

Order data verification:

- One `OrderItems` row for `Nokia N73`, quantity `1`, unit `400.00`, total `400.00`
- Warehouse resolution marked `Ordered`
- Checkout field snapshots captured for enabled system/custom fields
- No checkout attachments were submitted in this test, so no attachment rows were expected

Inventory note:

- Tested product `TrackStock = false`, `AllowBackorder = false`
- No persistent `InventoryReservations` row is expected for this specific checkout item
- Product variant stock remained `Quantity = 3.00`, `ReservedQuantity = 0.00`

## 15. Transaction/Rollback

The earlier failed attempts left no partial active orders:

- Runtime active orders after successful proof: `1`
- Duplicate active order numbers: none
- Failed sequence allocations produced acceptable gaps

The successful checkout idempotency record completed with:

- HTTP status `200`
- `IsDeleted = false`
- Bound `OrderId = c481589a-1a46-4c3e-a8e6-33bf8c44dbc3`

## 16. Tests

Focused regression coverage run:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CheckoutRequestIdempotencyCoordinatorTests|FullyQualifiedName~OrderCheckoutFieldSnapshotTests|FullyQualifiedName~EventOutboxConsistencyArchitectureTests"
```

Result:

- Passed: `62`
- Failed: `0`
- Skipped: `0`

Coverage includes idempotency cleanup behavior around dirty immutable checkout snapshots, checkout snapshot immutability, and integration event version consistency.

## 17. Builds

Build and static checks:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore`: passed
- `dotnet build PointOfSale.sln --no-restore`: passed
- `npm run build` in `PointOfSale.Storefront`: passed
- `git diff --check`: passed, with existing line-ending warnings only
- `dotnet ef migrations has-pending-model-changes`: passed

## 18. Remaining Limitations

Remaining items are outside this step's order-numbering scope:

- `return_number_seq` and `stock_transfer_number_seq` are still absent from runtime; they should be handled only when their document paths are in scope.
- `ShipmentAuthorityRecoveryHostedService` logs a separate `p.xmin` shipping recovery error after checkout success.
- COD payment intent recovery updated the order/payment reference after initial checkout response; COD remained pending/reviewable, but the recovery behavior should be audited in the payment workstream.
- Paymob was not tested in this step.

## 19. Worktree Safety

No commit, push, reset, clean, stash, or unrelated restore was performed.

The worktree was dirty before C5.2 remediation from broader C1-C5.1, Shipping, Payment, and Checkout work. Those changes were preserved. C5.2 touched only the active order-number migration, checkout runtime fixes required to prove placement, focused tests, forensic helpers, and this report.

## 20. Final Certification

REMEDIATION COMPLETE — RUNTIME VERIFIED

Certified facts:

- Runtime database identity was verified before mutation.
- Runtime EF history now includes the active C5.2 migration.
- `public.order_number_seq` exists with the verified sequence definition.
- Existing order-number state is safe and duplicate-free.
- API starts successfully and no missing-sequence error remains.
- Fresh COD checkout succeeds with HTTP 200.
- Order number generation works through `DocumentNumberingSupport`.
- Order, order item, checkout snapshots, warehouse resolution, idempotency, and COD payment intent were verified in runtime PostgreSQL.
- Focused tests, EF convergence, API build, solution build, Storefront build, and diff check passed.
- No unrelated architecture was weakened.
