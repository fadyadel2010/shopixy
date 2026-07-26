# STOCK-CON-04 TestSprite Report

Date: 2026-05-18

## Result

- TestSprite case: TC041 `stock_con_04_replay_during_sales_return_stock_restoration`
- Status: Passed
- TestSprite test id: `35a2a813-e698-4769-952d-28a4601c028d`
- Project id: `672364d7-f535-4971-acf1-e9872e75f610`
- Local runner: `POST /api/test-auth/stock-con-04/run` then `GET /api/test-auth/stock-con-04/run/{JobId}`

## Validation Boundary

TC041 used only the dedicated local runner endpoint. The remote TestSprite code did not synthesize replay timing, call sales/inventory/projection endpoints directly, or replace the deterministic xUnit overlap with sleeps. The local runner executed the authoritative STOCK-CON-04 xUnit scenario on the Windows machine against the real integration stack.

## Coverage Confirmed

- replay overlap during active sales-return stock restoration
- fresh-reader no-partial-restoration visibility
- exactly one SQL stock restoration
- exactly one sales-return restoration movement
- historical returned-quantity boundary preservation
- accounting-linked refund/COGS reversal linkage
- storefront, affiliate, and CommerceHub availability convergence
- duplicate return stock event delivery safety
- stale sale replay rejection after restoration
- SQL-truth rebuild convergence
- inventory reconciliation convergence
