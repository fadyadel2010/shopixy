# STOCK-CON-07 TestSprite Report

Date: 2026-05-18

## Result

- TestSprite case: TC044 `stock_con_07_concurrent_sales_return_against_same_historical_sale_quantity`
- Status: Passed
- TestSprite test id: `f856d200-2518-4776-8117-11bc996ae69d`
- Project id: `5ccea778-dffd-4ee5-bb02-2dcd58e20dc8`
- Local runner: `POST /api/test-auth/stock-con-07/run` then `GET /api/test-auth/stock-con-07/run/{JobId}`

## Validation Boundary

TC044 used only the dedicated local runner endpoint. The remote TestSprite code did not synthesize concurrent return timing, call sales/inventory/projection endpoints directly, or replace the deterministic PostgreSQL overlap with sleeps. The local runner executed the authoritative STOCK-CON-07 xUnit scenario on the Windows machine against the real integration stack.

The TestSprite node process completed TC044 and wrote a passed repo-local result. It also logged an `EPERM` while updating its user-level AppData config after execution, but `testsprite_tests/tmp/test_results.json` records TC044 as `PASSED`.

## Coverage Confirmed

- concurrent overlapping sales returns against the same historical sold quantity
- invoice-row `FOR UPDATE` boundary protection with a different request identity
- fresh-reader no-partial-restoration visibility during active overlap
- exactly one authoritative SQL stock restoration
- exactly one sales-return restoration movement
- historical sold/returned quantity boundary preservation
- rejected overlapping return replay fails closed without mutation
- refund/accounting reversal linkage remains one-to-one with authoritative return truth
- processed-event/idempotency completion remains one-to-one with the committed return
- storefront, affiliate, and CommerceHub availability convergence
- duplicate return stock event delivery safety
- stale sale replay rejection after restoration
- SQL-truth rebuild convergence
- inventory reconciliation convergence
