# Treasury T-08 — E-Commerce / CommerceHub Inventory & COGS Certification — 2026-09-14

> SINGLE AGENT. Implemented the §21 minimal safe design from `TREASURY_T08_ECOMMERCE_COMMERCEHUB_INVENTORY_COGS_INTEGRITY_2026-09-14.md` (discovery), certified it live. No commit. No direct-SQL business writes (all state changes through API/services; SQL access read-only via `default_transaction_read_only=on`). No historical back-posting. No T-06/T-07/1020/1110/revenue/clearing/settlement/Cash-Flow changes. Unrelated working-tree changes preserved; `git diff --check` clean.

## 1. Executive Verdict

**Verdict: PASS (certified, with documented boundaries).**

The T-08 question — *"How does Shopixy know the exact cost of merchandise sold through Storefront and CommerceHub, and how does that exact cost become COGS and Inventory GL, then reverse correctly on refund/return?"* — now has a live-proven answer:

1. **Cost authority (live-proven):** `ProductVariant.CostPrice` frozen to the immutable line snapshot at checkout commit (`OrderItem.CostPrice`; new `CommerceHubSubOrderItem.UnitCostSnapshot/CostAmountSnapshot/CostSnapshotCapturedAtUtc`).
2. **COGS posting (live-proven, Storefront):** fulfillment commit → durable outbox event → processor → `FinancialPostingService` → **Dr 5010 / Cr 1310** from the snapshot, never live cost. Live order `ORD-83B3-2026-01033`: snapshot 350 held while master moved 350→360; posted COGS = **700 = 2×350**, not 720.
3. **Reversal (live-proven, Storefront):** post-commit refund restored stock (5.00/0) and posted **Dr 1310 / Cr 5010 = 700** (`JE-2026-000067`) at the original snapshot.
4. **CommerceHub:** posting path implemented + service-tested (31/31 green incl. `T08CommerceHubCogsTests`); historical rows verified cost-unknown with zero spurious journals; full live marketplace delivery deferred (§12, reason recorded).
5. Trial Balance for the cert window is balanced (3020 = 3020, variance 0) with all four T-08 legs present; no cash/clearing leg in any COGS journal.

## 2. Scope And Environment

| Item | Detail |
| --- | --- |
| Mode | Single agent. Implementation + certification. No commit. |
| API | `http://127.0.0.1:5010`, Release build, `Development`, DB `PosEcommerceDb_Baseline` @ `192.168.1.5:5432` (health: postgres/redis/mongo/ES/RabbitMQ Healthy; `CommerceHubPlatform.postgres.pendingMigrations: []`; overall 503 only from pre-existing Unhealthy checks: payout-external-truth Critical reconciliation, CommerceHubSearchSync ratio, 1 stale reservation, projection drift — all Phase-2A families, none T-08) |
| Tenants | Live cert on Shopixy `83b39fe2-…`; isolation probes as greatone `c43aebf2-…`; AhramStore untouched |
| Auth | Owner login `fadyadel2060@gmail.com` verified 200 (Shopixy Owner); ops performed with dev-harness Admin JWT (`POST /api/test-auth/token`, sanctioned harness) |
| SQL | Read-only only (`psycopg`, `default_transaction_read_only=on`); every mutation via HTTP API |
| Test footprint | One guest cart/session, one COD order + full refund (both terminal), one product cost bump 350→360→350 (restored). COGS journals `JE-2026-000065`/`JE-2026-000067` remain posted as the proof (compensating-reversal discipline, nothing deleted) |

## 3. Storefront Live Lineage (Order ORD-83B3-2026-01033)

`466f734f-23b5-4977-9d45-da4cc5ff63e6` — Nokia N73 (32/yellow, variant `7356e0b1-…`), qty 2 @ 400 = 800 + shipping 10 = 810 EGP, COD. Path: `POST /api/storefront/cart/items` → checkout-session `4967610e-…` → shipping preview/select (Standard, resolution `6270fede-…` v1) → pricing preview → `POST /api/storefront/checkout` (PaymentPending) → pack → out-for-delivery → delivered (ref `T08-CERT-001` + POD) → `cod-delivered` (Collected) → complete (Completed) → full refund 810 (Refunded). Final DB: `PaidAmount=800, RefundedAmount=800`; reservation `Reserved 2 / Committed 2 / Returned 2`; stock `5.00/0` (started 5.00/0).

## 4. Historical Cost Snapshot (Live 350→360 Proof)

- At checkout: `OrderItems.CostPrice = 350.00` (read-only SQL), `Variant.CostPrice = 350.00`, stock `5.00/2.00 reserved`.
- Master cost then raised 350 → **360** via `PUT /api/products/{id}` product-authoring path (variant PUT is deprecated by design; cost moves only through authoring + `Expected-Version` + idempotency key).
- At delivery/collection with live cost 360: staged and posted COGS = **700.00 = 2 × 350.00** (frozen snapshot). A naive live-cost reader would have posted 720. Invariant holds live.
- Reversal after refund used the same original 350 snapshot (reversal = 700).

## 5. Storefront COGS Journal (Live)

`JE-2026-000065`, `SourceType=Order_COGS`, key `ECOM_COGS:{tenant}:{order}:cod-delivery-commit`, Posted `2026-09-14 19:41:43+03:00`: **Dr 5010 700.00 / Cr 1310 700.00**. Only 5010/1310 lines — no cash/clearing/AR/AP/revenue touch.

## 6. Outbox Lineage (Live)

`AccountingOutboxMessages`: `EventType=EcommerceCogs`, `SourceType=Order_COGS`, same idempotency key, `ProcessedAtUtc` set, `JournalId=01a0a0cb-…` linked, `AttemptCount=0, IsFailed=False`, payload carries per-line snapshot (`OrderItemId`, qty, `UnitCostSnapshot`). Reversal: `EcommerceCogsReversal` / `Order_COGS_Reversal`, key `ECOM_COGS_REV:{tenant}:{order}:t08-cert-refund-001`, `JournalId=01a0a0cd-…`. Event staged in the same DB transaction as the stock commit (code path), processor posted asynchronously (~40 s lag observed, §18-OBS1).

## 7. Idempotency / Replay (Live)

- `cod-delivered` posted twice → still exactly **1** `Order_COGS` journal + **1** outbox row (journal shape-match + outbox conflict path).
- Full refund posted twice with same `refundId` → still exactly **1** `Order_COGS_Reversal` journal + **1** reversal outbox row.
- Keys include the commit/return event id, never the order id alone (`cod-delivery-commit`, `t08-cert-refund-001`).

## 8. Returns / Refunds (Live)

Post-commit full refund: money reversal (Dr 4020 / Cr 1130, pre-settlement COD path) + operational restore (reservation `ReturnedQuantity=2`, stock 3.00/0 → 5.00/0) + COGS reversal `JE-2026-000067` (**Dr 1310 700 / Cr 5010 700**). Pre-commit refund releases reservations with no reversal due (proven by `T08StorefrontCogsLifecycleTests.PreCommitRefund_ReleasesReservations_AndStagesNoReversal`, green). Over-return and cap behavior proven in unit tests (31/31).

## 9. Partial Fulfillment

Storefront commit is single-shot (`CommitOrderReservationsAsync` commits the whole order; no per-line fulfilled-quantity accounting exists — discovery F08 stands as designed constraint). Per-commit-event COGS keys + cumulative caps (posted-total and restored-total guards in the processor) make partial-capable semantics safe for CommerceHub multi-delivery; proven in `T08EcommerceCogsOutboxProcessorTests` (cap/replay/tenant-mismatch cases). No live partial split was exercised because the channel does not support partial commits.

## 10. Payment-Method Invariance

Live: COD end-to-end (§3–§8). Posting layer proves invariance: `T08EcommerceCogsPostingTests` posts identical Dr 5010 / Cr 1310 from the same snapshot for online, COD, and wallet collection shapes (method changes only the revenue debit leg). Online-gateway and wallet-approval live runs were not executed (sandbox credentials / manual-proof scaffolding out of scope); no code path distinguishes methods at the COGS leg (verified: `BuildEcommerceCogsCommitEventId` varies only the commit identity).

## 11. Tenant Isolation (Live)

With a greatone Admin JWT against the Shopixy order/variant: read order → **404**; refund attempt → **400 `ORDER_NOT_FOUND`** (no write, no data); read Shopixy variant → **404**. Cost input stays tenant-bound (checkout variant lookup filters `variant.TenantId + variant.Product.TenantId`); journal idempotency is per-tenant; processor verifies `message.TenantId == entity.TenantId`.

## 12. CommerceHub

- **Implemented:** `UnitCostSnapshot (18,6)` + `CostAmountSnapshot (18,2)` + `CostSnapshotCapturedAtUtc` on `CommerceHubSubOrderItem` (migration `T08_EcommerceCommerceHubCogsAuthority` applied; `pendingMigrations: []`); checkout freeze from tenant-filtered `Variant.CostPrice` incl. merge-conflict rule; `CommitDeliveryAsync` → `CH_COGS:{tenant}:{subOrder}:{deliveryCommitEventId}` → Dr 5010 / Cr 1310; `ReceiveReturnAsync` → resellable-only reversal Dr 1310 / Cr 5010; historical skip with warning when `CostSnapshotCapturedAtUtc IS NULL`; zero-amount skip.
- **Live boundary proof (read-only):** all 10 Shopixy sub-order lines (2026-09-01…06, statuses 0/3/4/7) have `CostSnapshotCapturedAtUtc IS NULL` (migration default wrote `UnitCostSnapshot=0`, correctly treated as unknown via the NULL-timestamp gate); **zero** `CommerceHub_COGS[_Reversal]` journals exist — no spurious posting, no backfill.
- **Service-proof:** `T08CommerceHubCogsTests` green (delivery posts from snapshot, redemption of historical rows skipped, damaged qty excluded, caps enforced).
- **Deferred:** a brand-new live marketplace delivery (needs customer JWT + sellable vendor listing; Shopixy vendor state is DebtBlocked/governance-locked). No live `CommerceHub_COGS` journal exists yet — first live delivery after this release will produce it through the certified path.

## 13. Historical Boundary

Held absolutely: no corrective/backfill journals; pre-T-08 committed Storefront orders have no COGS event and their refunds keep money-only behavior; CommerceHub historical lines stay cost-unknown; migration is additive-only (`ALTER TABLE … ADD`, reviewed script); master catalog restored (Nokia cost back to 350).

## 14. Reporting Impact (Live)

Trial Balance 2026-09-14 (Shopixy): `1130` Dr810/Cr810, `4020` Dr810/Cr810, **`5010` Dr700/Cr700**, **`1310` Dr700/Cr700**; totals 3020 = 3020, `isBalanced=true, variance=0`. COGS journals contain only 5010/1310 lines → cash and Cash-Flow classification untouched (no 10xx/11xx movement from T-08). P&L math per §16 of discovery holds: +800 revenue / +700 COGS at sale, fully reversed at refund.

## 15. Outbox / Accounting Event Lineage (End-To-End)

Reserve → commit+stage (1 tx) → `EcommerceCogs` → processor verify (snapshot re-read, tenant, cumulative cap, shape-match) → `Order_COGS` journal → `JournalId` linkage. Refund → restore+stage (1 tx) → `EcommerceCogsReversal` → `Order_COGS_Reversal`. No half-states observed live.

## 16. Inventory Consistency (Live)

Reservation lifecycle `Reserved 2 → Committed 2 → Returned 2`; stock `5 → 3 (committed) → 5 (restored)`; zero negatives; no stranded reservation (post-refund `ReservedQuantity=0`). Fulfillment state machine (Created→Confirmed→Packed→OutForDelivery→Delivered→Completed) and payment machine (Pending→Collected→Refunded) stayed consistent; COD collection correctly refused before delivery (`INVALID_REQUEST`).

## 17. POS vs Storefront vs CommerceHub (After T-08)

| Capability | POS | Storefront | CommerceHub |
| --- | --- | --- | --- |
| Cost snapshot | `CostPriceSnapshot`+`CostAmountSnapshot` | `OrderItem.CostPrice` (immutable) | NEW `UnitCostSnapshot`+`CostAmountSnapshot`+`CapturedAt` (immutable; historical rows NULL) |
| COGS journal | Dr 5010 / Cr 1310 | **NOW LIVE-PROVEN** (`Order_COGS`) | Implemented + service-tested (`CommerceHub_COGS`); no live journal yet |
| COGS reversal | Dr 1310 / Cr 5010 | **NOW LIVE-PROVEN** (`Order_COGS_Reversal`) | Implemented + service-tested |
| Outbox | `PosSale`/`PosRefund` | `EcommerceCogs[_Reversal]` (live) | `CommerceHubCogs[_Reversal]` |
| Idempotency | `SALE`/`COGS` keys | `ECOM_COGS[:_REV]:{t}:{order}:{event}` (live) | `CH_COGS[:_REV]:{t}:{subOrder}:{event}` |
| Zero-cost/unknown | `totalCost>0` guard | skip `CostPrice<=0` / zero-amount (live+unit) | skip NULL-timestamp (live-verified) + zero-amount |

## 18. Findings (Certification Phase)

| ID | Severity | Observation | Disposition |
| --- | --- | --- | --- |
| T-08-C01 | Info | Outbox→journal lag ~40 s on dev (reversal appeared on second poll) | Normal async behavior; processor healthy, no failure |
| T-08-C02 | Info | Admin order-detail DTO omits `costPrice` (null in presentation) | Known F11 UI gap; snapshot verified at DB; read-only lineage view deferred |
| T-08-C03 | Info | Migration default `UnitCostSnapshot=0` for historical rows | Harmless: NULL-timestamp gate is the discriminator; verified in code + live |
| T-08-C04 | Note | SQLite stores decimals as TEXT (`'10'>='2'` false) — test-only quirk | Adopted repo convention shim (drop all CKs on SQLite); production Postgres unaffected |
| T-08-C05 | Note | Unrelated dev-DB drift observed mid-cert (transient stock 8/3) | Attributed only T-08-owned rows (order id / idempotency keys); shared-DB caveat recorded |

No new P0/P1. No T-08 defect found live.

## 19. Regression Evidence

| Suite | Result | Notes |
| --- | --- | --- |
| T-08 (4 new files) | **31/31 PASS** | posting, processor, storefront lifecycle, CommerceHub |
| Accounting (`~Accounting`) | 199/200 | 1 pre-existing: missing `20260424150000_S28_PosSalesAccountingMutationVersion.sql` (file absent from tree; untouched by T-08) |
| Payments (`~Payments`) | 232/237 | 5 pre-existing: T-06 `~` SQLite (coupon), source-text assertions (payment handoff, payoutMethodId), Fawry state-machine test — none touch T-08 paths |
| CommerceHub (`~CommerceHub`) | 859/867 | 8 pre-existing: 2 static search tests, 5 T-06 `~` relational, 1 payout static |
| `~InventoryReservationLifecycle` | 19/19 | Untouched path still green |
| `~InventoryConsistency` | 26/26 | Touched service, green |
| `~CommerceHubCheckoutFinancialControlTests` | 8/8 | Real checkout service, green |
| `~PaymentLifecycle` | 67/67 | Touched service, green |
| `~EcommerceRefundLifecycle` | 0/7 | All fail at fixture `EnsureCreated` on T-06 `~` (pre-existing, pre-my-change; never reach T-08 code) |
| Full `~Integration` bucket | Not run to completion | Requires live Postgres for Npgsql suites; long TCP timeouts in this env — scoped subsets run instead (above) |

## 20. Browser Certification (Limitation)

No browser-automation tool exists in this environment, so the Parts 23–29 "browser" matrix was executed as **API-driven live certification** against the same backend the Web app calls, plus a real Owner login (`fadyadel2060@gmail.com`, 200, Shopixy Owner) proving the credential path. Presentation-layer read behavior (order/finance screens) was not inspected at runtime; the one presentation fact observed (cost hidden in admin order DTO) is recorded as C02. No COGS/inventory-cost UI was added (out of scope by design).

## 21. Migration & Schema Verification

- `20260914152619_T08_EcommerceCommerceHubCogsAuthority` (additive `ALTER TABLE CommerceHubSubOrderItems ADD …`) applied to dev; live `pendingMigrations: []`; `dotnet ef migrations has-pending-model-changes` → `No changes`.
- EF precisions: `UnitCostSnapshot numeric(18,6)`, `CostAmountSnapshot numeric(18,2)`; NULL timestamps on all 10 historical lines confirmed live.

## 22. Build Evidence

- `PointOfSale.API` Release: **Build succeeded, 0 errors** (191 pre-existing warnings).
- `PointOfSale.Web` production (`npm run build`): **✓ built in 25.40s**, exit 0.
- `PointOfSale.Core/Infrastructure/Tests`: 0 errors. `git diff --check`: clean (CRLF notices only). Nothing committed.

## 23. Multi-Tenant Certification

Shopixy: full live cycle (§3–§8, §14). greatone: isolation probes deny reads/writes cross-tenant (§11). AhramStore: untouched (no T-08 rows: zero `Order_COGS` journals outside Shopixy — verified tenant-scoped queries). Cross-tenant cost consumption is structurally impossible (tenant-filtered variant read is the sole cost input).

## 24. Refund-Before-Fulfillment

Pre-commit refund path releases reservations and stages no reversal (code + `PreCommitRefund_ReleasesReservations_AndStagesNoReversal` green). Live COD flow commits at delivery, so the live refund exercised the post-commit branch; pre-commit branch covered at service level.

## 25. Cancellation Semantics

Unchanged: pre-fulfillment cancel releases reservations (no revenue/COGS exists); post-fulfillment cancel is refund+return through the certified linked path. `OrderFulfillmentService.CancelAsync` redirection untouched.

## 26. Returns / Refunds Matrix (Final)

| Case | Money | Stock | COGS reversal | Evidence |
| --- | --- | --- | --- | --- |
| Storefront post-commit full refund | Dr4020/Cr1130 | restored +2 (5.00/0) | Dr1310/Cr5010 700, JE-2026-000067 | LIVE |
| Storefront pre-commit refund | revenue reversal only | reservations released | none due | UNIT (green) |
| Storefront over-return / double refund | idempotent single | capped | single reversal | LIVE replay + UNIT |
| CommerceHub resellable return | bucket reversal (existing) | return receipt (existing) | Dr1310/Cr5010 at snapshot | UNIT (green); live deferred §12 |
| CommerceHub damaged return | existing | damaged split (existing) | stays in COGS (policy) | UNIT (green) |

## 27. Idempotency Matrix (Final)

| Replay | Result | Evidence |
| --- | --- | --- |
| `cod-delivered` ×2 | 1 journal, 1 outbox row | LIVE count queries |
| refund same `refundId` ×2 | 1 reversal journal, 1 outbox row | LIVE count queries |
| changed COGS payload, same key | processor conflict, fail closed | UNIT |
| tenant-mismatched message | skip, no status write | UNIT |

## 28. Acceptance Matrix (Task Scenarios)

| # | Scenario | Result | Evidence |
| --- | --- | --- | --- |
| 1 | Exact cost of Storefront merchandise known | PASS | `OrderItem.CostPrice=350` frozen (SQL) |
| 2 | Cost 50→60 posts 50 (here 350→360 posts 350×qty) | PASS | COGS 700 live, master 360 ignored |
| 3 | COGS becomes Dr5010/Cr1310 + inventory relief | PASS | JE-2026-000065 live |
| 4 | Return reverses correctly | PASS | JE-2026-000067 + stock 5.00/0 live |
| 5 | Partial-fulfillment semantics | PASS (by construction) | per-commit keys + caps (UNIT); single-commit channel |
| 6 | CommerceHub cost authority + posting | PASS (impl+unit+boundary) | snapshot columns, gates, tests; live delivery deferred |
| 7 | Multi-tenant certification | PASS | Shopixy live cycle + greatone deny probes |
| 8 | Refund-before/after fulfillment | PASS | live post-commit + unit pre-commit |
| 9 | Payment-method invariance | PASS | live COD + posting tests all methods |
| 10 | Trial Balance / P&L / cash | PASS | TB balanced, COGS cash-free |
| 11 | No historical rewrite | PASS | NULL-timestamp gates, zero backfill journals |
| 12 | Regression + builds | PASS | §19 + §22 (failures all pre-existing, attributed) |

## 29. Residual Risks & Deferred Work

1. First live `CommerceHub_COGS` journal pending a real marketplace delivery (path certified short of that event).
2. Online-gateway/wallet live COGS legs unexercised against sandboxes (posting layer method-invariant by test).
3. Read-only cost-lineage UI (F11) still absent; admin DTO hides `costPrice`.
4. Shared dev-DB drift (C05): future certs should use isolated tenant or seeded fixtures.
5. Pre-existing repo failures listed in §19 remain with their owners (T-06 `~` SQLite, S28 SQL file, source-text certs, Fawry).

## 30. Final Verdict

**T-08: PASS — certified.** Storefront merchandise-cost lifecycle is closed end-to-end and proven on live data (freeze → commit → outbox → Dr 5010 / Cr 1310 → replay-safe → return → Dr 1310 / Cr 5010 → balanced books). CommerceHub authority and posting are implemented, unit-proven, and boundary-guarded live; its first live journal will flow through this certified path. Discovery findings F01 (Storefront) is remediated and certified; F02–F04 remediated in code with Storefront certified live and CommerceHub certified short of a live delivery (§12); F05–F11 unchanged by design except F06 (schema authority added). Historical boundary intact. No commit made; working tree left with implementation + tests + this report uncommitted.
