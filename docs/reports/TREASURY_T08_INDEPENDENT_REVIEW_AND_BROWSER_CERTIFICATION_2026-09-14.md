# Treasury T-08 Independent Review and Browser Certification - 2026-09-14

> Independent single-agent review. No commit was made. All controlled business mutations used the real Storefront and owner browser interfaces. Database reconciliation was read-only. Existing reports were treated as untrusted context, not as certification evidence. Unrelated working-tree changes were preserved.

## 1. Executive Verdict

**Verdict: PASS with documented boundaries.**

The Storefront merchandise-cost lifecycle was independently proven end to end for a real Shopixy order: tenant-authoritative variant cost was frozen on the order line, inventory commit staged one durable COGS event, the event posted **Dr 5010 / Cr 1310**, a real resellable return restored stock, and the refund staged one reversal posting **Dr 1310 / Cr 5010** at the original cost. CommerceHub has the same immutable-cost and posting controls in source and focused tests; a live marketplace delivery was not manufactured because Shopixy's marketplace participation was governance-blocked.

## 2. Review Independence

The review traced application source, ran focused and regression gates, drove headed Microsoft Edge through the real customer and owner UIs, and reconciled the resulting rows and journals. `TREASURY_T08_ECOMMERCE_COMMERCEHUB_INVENTORY_COGS_CERTIFICATION_2026-09-14.md` was consulted only for document shape. Its order `ORD-83B3-2026-01033`, API-driven workflow, and conclusions are not evidence for this report.

## 3. Scope and Constraints

- In scope: Storefront and CommerceHub cost authority, inventory commit/return, COGS/reversal, idempotency, atomicity, tenant isolation, historical behavior, migration state, tests, builds, and browser presentation.
- No direct SQL business writes, manual journals, outbox insertion, balance patching, historical COGS backfill, or governance bypass occurred.
- PostgreSQL reconciliation used a repeatable-read transaction with `SET TRANSACTION READ ONLY`.
- Credentials were used only for authorized browser login and are intentionally omitted.
- Temporary browser and reconciliation harnesses were created outside the repository under `C:\Users\fadya\AppData\Local\Temp\opencode`.

## 4. Environment and Health

The certified applications were API `http://localhost:5010`, admin `http://localhost:5173`, Storefront `http://shopixy.localhost:5174`, and CommerceHub port `5175`. The final aggregate API health response was HTTP 503 because `payout_external_truth_readiness` and `CommerceHubSearchSync` were unhealthy. T-08 dependencies reported `postgresql=Healthy`, `accounting_outbox_readiness=Healthy`, and `CommerceHubPlatform=Healthy`; the aggregate result is disclosed as an unrelated environmental issue, not hidden or reclassified.

## 5. Cost Authority

`ProductVariant.CostPrice` is the tenant-authoritative current merchandise cost. Storefront reloads the variant under both variant and product tenant predicates and copies that value into checkout state (`CheckoutService.cs:5053-5057,5119-5136`). CommerceHub resolves the same tenant-scoped authority and freezes `UnitCostSnapshot`, `CostAmountSnapshot`, and `CostSnapshotCapturedAtUtc` on each sub-order item (`CommerceHubCheckoutService.cs:1843-1866`). Purchase price, sales price, current catalog cost at fulfillment, stock valuation, and payment method are not fallback COGS authorities.

## 6. Immutable Snapshot Controls

Storefront persists the captured value as `OrderItem.CostPrice`; later COGS and reversal code reads that order-line value. CommerceHub persists unit cost at precision `(18,6)`, amount at `(18,2)`, and a nullable captured-at discriminator. `ApplicationDbContext` guards snapshot mutation after persistence. CommerceHub producer and processor independently require non-negative values and `CostAmountSnapshot == round(UnitCostSnapshot * Quantity, 2)` before accepting an event.

## 7. Controlled Browser Order

The customer selected the requested real product through the Storefront UI and submitted one COD checkout:

| Field | Certified value |
| --- | --- |
| Order | `ORD-83B3-2026-01034` |
| Order ID | `1346b8fa-87c2-4ce3-aa60-032115c7ff6e` |
| Tenant ID | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Product / variant | `Nokia N73`, `yellow / 32` |
| SKU / variant ID | `NOKIA-N73-YELLOW-32` / `7356e0b1-eb33-4ae1-9a5b-74fca2ed2a0c` |
| Quantity | `1` |
| Sale / shipping / total | `400.00 / 10.00 / 410.00 EGP` |
| Frozen unit cost | `350.00 EGP` |

Playwright lost the checkout response body during navigation. The unique customer marker was used to recover the order from the real owner Orders UI; checkout was not resubmitted and the database contains the single certified order.

## 8. Fulfillment Browser Flow

The owner UI advanced the same order through Confirmed, Packed, Out for Delivery, and Delivered with delivery proof, then recorded COD collection. Screenshots `11-admin-order-created.png` through `16-admin-cod-collected.png` preserve each browser state. No service endpoint or database row was invoked directly to bypass the workflow.

## 9. Sale Inventory Reconciliation

Read-only reconciliation found reservation `09363052-9c53-4283-af7a-cdbaa24e116e` with reserved quantity `1`, committed quantity `1`, returned quantity `0`, and no released or damaged quantity after sale. Sellable stock moved `5 -> 4`; stock reserved quantity ended at `0`. The order line retained `CostPrice=350.00`, `PaidAmount=400.00`, and `RefundedAmount=0.00` at this point.

## 10. Sale Outbox Lineage

Exactly one processed `EcommerceCogs` message existed for the order, with no failure or retry:

`ECOM_COGS:83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5:1346b8fa-87c2-4ce3-aa60-032115c7ff6e:cod-delivery-commit`

Its `JournalId` was `01a0a11f-969b-7170-8751-82324a24a47b`. Cardinality was one outbox row, one distinct key, one processed row, and one journal. This is durable stock-to-accounting lineage rather than an inferred report value.

## 11. Sale COGS Journal

Journal `JE-2026-000069`, source `Order_COGS`, posted exactly:

| Line | Account | Debit | Credit |
| --- | --- | ---: | ---: |
| 1 | `5010 Cost of Goods Sold` | `350.00` | `0.00` |
| 2 | `1310 Inventory` | `0.00` | `350.00` |

The journal is balanced and contains no cash, clearing, receivable, payable, revenue, shipping, or settlement account. Amount `350.00` equals frozen cost `350.00 * quantity 1`.

## 12. Return and Refund Browser Flow

The owner UI first recorded a physical, resellable return and then submitted the full `410.00 EGP` refund. Screenshots `17-admin-pre-return.png`, `18-admin-returned.png`, and `19-admin-refunded.png` preserve the sequence. Final state was fulfillment `Returned`, payment `Refunded`, line paid/refunded `400.00/400.00`, returned quantity `1`, and damaged returned quantity `0`.

## 13. Return Inventory Reconciliation

The same reservation retained committed quantity `1` and advanced returned quantity to `1`. Sellable stock moved `4 -> 5`; stock reserved quantity remained `0`; damaged quantity remained `0`. The immutable order-line cost remained `350.00`. This proves stock restoration was tied to the real return rather than achieved by an adjustment.

## 14. COGS Reversal Journal

Exactly one processed `EcommerceCogsReversal` message posted journal `JE-2026-000071`:

| Line | Account | Debit | Credit |
| --- | --- | ---: | ---: |
| 1 | `1310 Inventory` | `350.00` | `0.00` |
| 2 | `5010 Cost of Goods Sold` | `0.00` | `350.00` |

The reversal key is `ECOM_COGS_REV:83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5:1346b8fa-87c2-4ce3-aa60-032115c7ff6e:refund-e2dcbacd-8272-4eb0-a082-d05c76f3c759`. Sale plus reversal nets both 5010 and 1310 to zero for the certified merchandise-cost cycle.

## 15. Atomicity

Storefront stages COGS in the transaction that commits inventory reservations, and stages the reversal in the refund/return transaction (`PaymentLifecycleService.cs:2087,2363,2567,2629,4761-5056`). CommerceHub stages delivery COGS before its stock transaction saves and stages reversal before return-receipt save (`InventoryConsistencyService.cs:1514-1524,1539-1647`). Posting then occurs from the durable accounting outbox through the normal processor. No independent best-effort journal write was found.

## 16. Idempotency and Replay

Keys bind tenant, business aggregate, and commit/return identity. The live certified rows have one distinct sale key and one distinct reversal key. Processor envelope validation reconstructs the expected key and verifies event type, source type, aggregate ID, and version. Focused tests cover duplicate delivery, duplicate refund/return, changed payload under an existing key, and tenant mismatch. No order-only idempotency key or duplicate COGS journal was found.

## 17. Quantity and Amount Caps

The processor re-reads immutable source rows and rejects payload/source mismatches. Storefront caps posting and reversal by order-line quantity and posted cost. CommerceHub additionally caps cumulative delivery quantity by ordered and committed inventory quantity, cumulative delivery amount by the line snapshot, reversal quantity by confirmed resellable return and physical returned stock, and reversal amount by previously posted COGS (`AccountingOutboxProcessor.cs:3117-3159,3271-3327`). Over-fulfillment and over-reversal fail closed.

## 18. Zero-Cost Semantics

A captured cost of zero is valid authority, not the same as unknown history. CommerceHub now stages a zero-cost outbox event, validates it, and processes it as `SKIPPED_ZERO_COGS` with durable processed lineage and no zero-value journal (`InventoryConsistencyService.cs:1568-1593`; `AccountingOutboxProcessor.cs:3161-3165`). `CommitDeliveryAsync_WithCapturedZeroCost_StagesProcessedLineageEvent` covers the producer behavior. Negative or internally inconsistent captured values fail closed.

## 19. Historical Boundary

CommerceHub rows with `CostSnapshotCapturedAtUtc == null` are pre-remediation cost-unknown even if additive migration defaults contain decimal zero. Delivery/return does not guess a cost, read current catalog cost, or create corrective COGS for those rows. No historical snapshots, journals, outbox rows, balances, or inventory values were backfilled during this review.

## 20. CommerceHub Delivery Path

For a captured line, delivery loads the sub-order item, verifies sub-order binding, validates the immutable amount, and enqueues `CommerceHubCogs` using the per-delivery event identity (`InventoryConsistencyService.cs:1539-1593`). The processor verifies tenant, marketplace order, currency, snapshot, payload amount, committed stock, and cumulative caps before requesting **Dr 5010 / Cr 1310** through `ICommerceAccountingService` (`AccountingOutboxProcessor.cs:3054-3187`).

## 21. CommerceHub Return Path

Only resellable returned quantity receives a COGS reversal; damaged goods remain in COGS under the stated impairment boundary. Return identity is shared by receipt and reversal. The processor verifies the confirmed return request, vendor/tenant, line, currency, original posted COGS, physical returned quantity, and cumulative reversal caps before requesting **Dr 1310 / Cr 5010** (`InventoryConsistencyService.cs:1595-1647`; `AccountingOutboxProcessor.cs:3190-3349`).

## 22. CommerceHub Governance Boundary

Shopixy's marketplace participation was `DebtBlocked`, which excludes it from new marketplace orders while preserving existing-order processing. This review did not bypass that authority, seed a fake marketplace delivery, or write a CommerceHub journal directly. CommerceHub certification is therefore source plus focused-service/processor evidence, not a claimed live marketplace journal. This boundary is acceptable because the path is implemented and tested and the inability to create a new live order is an intentional governance control.

## 23. Tenant Isolation

Checkout variant reads require both `ProductVariant.TenantId` and `Product.TenantId` to match the active tenant. COGS keys include tenant ID. Both Storefront and CommerceHub processors re-read source entities with query filters deliberately bypassed only to perform explicit ownership checks; a message cannot use another tenant's order/sub-order line. Focused tests cover cross-tenant source binding and verify no journal is written on mismatch.

## 24. Accounting Authority and Reporting

COGS services build requests, but journal creation remains exclusively under `FinancialPostingService`; no alternate direct journal insertion was introduced. Sale and reversal shapes contain only accounts 5010 and 1310, so they do not mutate cash or settlement classification. `FinancialReportService` consumes posted journal lines, meaning P&L and inventory reporting inherit the certified entries rather than recomputing COGS from current product cost. The two certified journals are individually balanced and net to zero after full return.

## 25. Browser Presentation Certification

Headed Microsoft Edge checks passed on the Storefront after refund:

| View | Result |
| --- | --- |
| English desktop | `lang=en`, `dir=ltr`, no horizontal overflow |
| Arabic desktop | `lang=ar`, `dir=rtl`, no horizontal overflow |
| English mobile `390x844` | `dir=ltr`, no horizontal overflow |
| Arabic mobile `390x844` | `dir=rtl`, no horizontal overflow |
| Customer tracking | Displayed `ORD-83B3-2026-01034` |
| Browser page errors | `0` |

Evidence is stored as `20-storefront-en-desktop.png` through `24-storefront-order-tracking-mobile.png` under the temporary evidence directory.

## 26. Focused Test Evidence

| Gate | Result |
| --- | ---: |
| `T08CommerceHubCogsTests` | `9/9 PASS` |
| `T08EcommerceCogsOutboxProcessorTests` | `11/11 PASS` |
| Complete T-08 suite | `37/37 PASS` |
| Touched inventory/payment lifecycle regression filter | `120/120 PASS` |

The T-08 suite includes direct posting shapes, Storefront fulfillment/refund behavior, CommerceHub delivery/return behavior, historical unknown-cost handling, zero-cost lineage, tampered snapshots, caps, idempotency, and tenant mismatch.

## 27. Build and Migration Evidence

- `dotnet build PointOfSale.API\PointOfSale.API.csproj -c Release --no-restore`: succeeded with 0 warnings and 0 errors.
- Web admin, Storefront, and CommerceHub production builds: succeeded.
- `dotnet ef migrations has-pending-model-changes ...`: `No changes have been made to the model since the last migration.`
- Migration list includes `20260914152619_T08_EcommerceCommerceHubCogsAuthority` with no pending marker.
- `git diff --check`: no whitespace errors; repository line-ending warnings are disclosed separately from failures.

## 28. Unrelated Baseline Failures

Broader buckets are not fully green, and this report does not suppress that fact:

| Bucket | Result | Isolated cause |
| --- | --- | --- |
| Accounting | `201/202` | Missing pre-existing `20260424150000_S28_PosSalesAccountingMutationVersion.sql` |
| Payments | `232/237` | Includes pre-existing SQLite `near "~": syntax error` and stale assertions |
| CommerceHub | `862/870` | Includes stale architecture assertion and SQLite `~` incompatibility |

These failures do not execute the certified T-08 path and were reproduced as baseline/environment defects. They remain release-health debt and are not relabeled as T-08 passes.

## 29. Evidence Index and Residual Risks

- Sale reconciliation: `C:\Users\fadya\AppData\Local\Temp\opencode\t08-browser\evidence\sale-db-evidence.txt`.
- Return reconciliation: `C:\Users\fadya\AppData\Local\Temp\opencode\t08-browser\evidence\return-db-evidence.txt`.
- Browser screenshots: the same evidence directory, files `01` through `24`.
- Residual risk: the first post-remediation live CommerceHub marketplace delivery remains unobserved because governance correctly blocked order creation.
- Residual risk: online-gateway and wallet browser cycles were not run; COGS payment-method independence is supported by source and focused posting tests.
- Residual risk: aggregate API health remains 503 for the two unrelated readiness checks in section 4.
- Residual risk: the admin order presentation does not expose immutable merchandise cost; accounting proof currently requires authorized reconciliation/reporting access.

## 30. Final Certification

**T-08 PASS.** Independent headed-browser and read-only database evidence proves the Storefront chain for `ORD-83B3-2026-01034`: immutable cost `350.00` -> inventory commit -> one processed outbox event -> `JE-2026-000069` Dr 5010 / Cr 1310 `350.00` -> resellable return and full refund -> stock restoration -> one processed reversal -> `JE-2026-000071` Dr 1310 / Cr 5010 `350.00`. Source and green focused tests prove equivalent guarded CommerceHub semantics without violating its `DebtBlocked` participation authority. Historical cost-unknown rows remain untouched, the migration model is current, no alternate journal authority was introduced, and no unrelated working-tree changes were reverted or committed.
