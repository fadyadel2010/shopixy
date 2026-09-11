# GATE 2 — NOT YET COMPLETE

Date: 2026-09-04

## 1. Verdict

**CODE VERIFIED: PASS for the inspected Gate 2 authority changes. TEST VERIFIED: PASS for the broad CommerceHub slice. API VERIFIED: PARTIAL. DATABASE VERIFIED: PASS for measured CommerceHub financial readiness. SECURITY VERIFIED: PARTIAL. EVENT VERIFIED: PARTIAL. PROJECTION VERIFIED: PASS for the CommerceHub-specific projection boundary. RUNTIME VERIFIED: FAIL. HUMAN VERIFIED: PARTIAL. Overall Gate 2 certification: NOT YET COMPLETE.**

## 2. Decision Rule

Gate 2 can be certified only when every evidence class is independently green. A focused test pass, healthy derivative projection, or healthy liveness endpoint cannot override a broad test failure, unresolved canonical financial mismatch, failed readiness probe, missing role evidence, or missing human observation.

## 3. Certification Scope

This gate covers tenant CommerceHub publication and listings, marketplace discovery, split-order lifecycle, vendor finance, earnings, payout methods, payout execution identity, refunds, returns, accounting delivery, event transport, derivative projections, tenant isolation, and runtime readiness.

## 4. Evidence Time

The latest runtime probes in this report were captured on 2026-09-04 at approximately `16:44Z` from `http://localhost:5010`.

## 5. Runtime Process

The probed process was the restarted Development API at port `5010`, PID `16744`, executable `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`, started at local time `20:27:12`.

## 6. Authority Model

POS owns tenant products, inventory, variant pricing, Storefront configuration, generic POS orders, and accounting. CommerceHub owns marketplace listings, participation, marketplace suborders, and their lifecycle. Super Admin owns explicit marketplace governance and privileged financial execution, but not tenant merchant data.

## 7. Canonical Store

PostgreSQL remains canonical for CommerceHub business and financial state. MongoDB, Elasticsearch, Redis, RabbitMQ, MassTransit, and frontend caches are derivative or delivery mechanisms and are not accepted as canonical business authority.

## 8. Predecessor Evidence

The existing M-1A through M-1D reports remain predecessor evidence. This report does not silently promote their partial human verdicts or recreate M-1D.

## 9. Payout Controller Authorization

`PointOfSale.API/Controllers/CommerceHubPayoutsController.cs` now requires `[Authorize(Roles = "Owner,Admin")]` rather than authentication alone.

## 10. Refund Controller Authorization

`PointOfSale.API/Controllers/CommerceHubRefundsController.cs` now requires `[Authorize(Roles = "Owner,Admin")]` rather than authentication alone.

## 11. Payout Method Request Identity

`CommerceHubCreatePayoutRequestDto` now carries nullable `PayoutMethodId`, allowing the selected vendor-owned destination to remain explicit across the orchestration boundary.

## 12. Payout Method Ownership

`CommerceHubPayoutService.CreatePayoutAsync` resolves the selected method by both method ID and trusted vendor tenant ID inside the serializable payout transaction. Deleted or foreign methods fail with `PAYOUT_METHOD_NOT_FOUND`.

## 13. Payout Method Type

The service compares the stored method type with the requested payout method. A mismatch fails with `PAYOUT_METHOD_TYPE_MISMATCH` rather than allowing a destination/type split.

## 14. Payout Destination Snapshot

New payout rows persist `PayoutMethodId` and `DestinationValueSnapshot`. Later payout-method edits therefore cannot rewrite the destination evidence associated with a payout.

## 15. Payout Replay Binding

`SuperAdminPayoutOrchestrationService` propagates the payout method ID and rejects idempotency replay when the existing payout was created with a different method using `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD`.

## 16. Code Verdict

**CODE VERIFIED: PASS for the inspected Gate 2 delta.** The payout identity, ownership, snapshot, replay, and role boundaries fail closed in the reviewed source. This is not a claim that every unrelated dirty-worktree change has been certified.

## 17. Release Build

The Release API build completed with `0` warnings and `0` errors. The Release test-project build completed with `0` errors and `232` existing analyzer/compiler warnings. The POS/Super Admin Web and CommerceHub customer production builds both passed; their existing oversized-chunk warnings remain, and CommerceHub also reported stale Browserslist data.

## 18. Payout Focused Tests

The current focused payout, payout-identity, orchestration, and security selection passed `44/44`. The selected POS/Super Admin frontend authority tests passed `80/80`. Those focused passes do not supersede the broad CommerceHub result.

## 19. Customer Refund Fixture

`CustomerRefundServiceTests.SaveCommerceHubFixtureAsync` now enters the payment-lifecycle mutation scope required by current canonical guards. The focused customer-refund class passed `6/6`.

## 20. Relational Finance Fixture

The in-memory SQLite finance fixture now supplies the PostgreSQL-compatible two-argument `btrim` and `char_length` functions needed by current queries. `CommerceHubVendorFinancialControlServiceRelationalTests` passed `5/5`.

## 21. Stock Convergence Fixture

The `STOCK_CON_05` scenario now seeds published product authority, tenant-aware search deletion, and authoritative affiliate eligibility. `STOCK_CON_05_Duplicate_CommerceHub_Return_Delivery_Converges_Safely` passed `1/1` in 41 seconds.

## 22. Price Policy Test Isolation

Price-override tests mutate a process-global rollout policy. The affected classes now share the non-parallel `CommerceHubPriceOverrideRuntimePolicy` xUnit collection, and `PricingAuthorityConsolidationCertificationTests` restores the default policy in `finally`.

## 23. Price Policy Verification

The focused price-policy selection passed all `34` relevant tests. One additional test selected by a broad substring remained red only because it references a retired migration file.

## 24. Broad CommerceHub Result

The latest Release run, `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore -p:RunAnalyzers=false -m:1 --filter "FullyQualifiedName~CommerceHub" --logger "console;verbosity=minimal"`, produced `797 passed`, `0 failed`, `0 skipped`, `797 total` in 1 minute 25 seconds. This includes the new PostgreSQL mixed-session checkout regression.

## 25. Broad Failure Classification

The prior 36 failures were classified and corrected without weakening production guards: 17 tests now read retired pre-consolidation migration evidence from the recovery archive, 15 source-shape assertions now follow the current canonical orchestration and authority boundaries, and four malformed non-EGP fixtures now assert rejection by the EGP-only creation or persistence boundary. The broad rerun verified all corrections.

## 26. Test Verdict

**TEST VERIFIED: PASS for the broad CommerceHub slice.** The focused payout selection passed `44/44`, selected frontend authority tests passed `80/80`, the financial reconciliation class passed `13/13`, the focused checkout selection passed `11/11`, and the broad CommerceHub selection passed `797/797`. This test verdict does not override the remaining runtime, security, event, or human-evidence gates.

## 27. Development Token Authority

`POST /api/test-auth/token` issued DB-bound Development `Admin` tokens for both tested active tenants. The endpoint currently issues only the `Admin` role.

## 28. Tenant A Identity

Tenant A was `a6c9e71e-dd48-49cb-947a-6f460423360e`. The token response repeated that tenant ID and reported role `Admin`.

## 29. Tenant B Identity

Tenant B was `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`. The token response repeated that tenant ID and reported role `Admin`.

## 30. Tenant A Reads

Tenant A received `200` from products, listings, finance dashboard, earnings, refunds, payouts, payout methods, and payments. Every returned `tenantId` or `vendorId` matched tenant A.

## 31. Tenant B Reads

Tenant B received `200` from the same eight routes. Every returned `tenantId` or `vendorId` matched tenant B.

## 32. Non-Empty Payout Scope Evidence

The original payout and payout-method responses were empty. Later frontend actions created one synthetic payout method for each vendor. Authenticated tenant-A and tenant-B API reads each returned exactly one method whose `VendorId` and destination matched the requesting tenant, with no foreign method present. The two frontend screenshots independently showed the same isolated destinations under the corresponding active Owner tenant.

## 33. Header Mismatch Rejection

A tenant-A bearer token sent with tenant B in `X-Tenant-Id` received `403`, proving the tested token/header mismatch fails closed.

## 34. Anonymous Boundary

Anonymous finance-dashboard requests returned `400` both without and with `X-Tenant-Id`. Tenant resolution rejects these requests before an authentication challenge, so this is fail-closed behavior but not a literal `401` assertion.

## 35. Role Boundary Gap

No Development non-Owner/non-Admin token path or supported POS user-administration workflow is available in the current runtime. Read-only SQL confirms that both tested vendor tenants contain only active Owner and Admin actors. POS token validation also compares the role claim with current SQL authority, so a manufactured Manager/Cashier/Warehouse claim would fail authentication and would not prove role authorization. The payout/refund role attributes are code-verified, but an honest live non-Admin `403` probe remains unavailable.

## 36. API Verdict

**API VERIFIED: PARTIAL.** Tenant-scoped reads, header mismatch rejection, and symmetric non-empty cross-tenant payout-method ownership passed. Live non-Admin role rejection remains unverified.

## 37. PostgreSQL Reachability

`/health/ready` reported PostgreSQL `Healthy` at `192.168.1.5:5432`, database `PosEcommerceDb_Baseline`.

## 38. Schema And Migrations

`/health/marketplace` reported no missing PostgreSQL tables, no missing columns, and no pending migrations.

## 39. Canonical Identity Integrity

Readiness reported `MissingCanonicalIdentityLinks=0` and `CanonicalIdentityConflicts=0`.

## 40. Financial Reconciliation

The latest append-only reconciliation report is `49860bbb-f802-4df1-90fa-69458731040f` at `2026-09-04T17:00:03.176687Z`, after the Gate 2 refund and return completed. It reported `Healthy`, `MismatchCount=0`, no errors or warnings, 14 checked suborders, 4 checked refunds, both Gate 2 vendors checked, 407 validated aggregate rows, zero invalid or missing aggregate rows, and zero compensation actions.

The prior false positives came from suborder `6656e584-6cde-412a-9f1a-6c38a2a1b397`, a pending COD sibling whose parent marketplace order was partially refunded because a different vendor suborder was refunded. `FinancialReconciliationService` incorrectly used parent payment state when deciding whether this pending suborder required commission ledger entries. Suborder financial validation now uses the suborder's own status and payment state. `FinancialReconciliationServiceTests` adds `RunAsync_ShouldNotRequireLedgerForPendingCodSiblingOfPartiallyRefundedOrder`; all 13 tests in that class passed after the change.

## 41. Payout State Counters

The same check reported zero completed payouts without evidence, zero completed payouts, zero stale processing payouts, and zero reconciliation-required payouts. Historical critical reports remain append-only evidence; the new report demonstrates that the corrected reconciliation logic clears the false positives without changing business data or creating compensation actions.

## 42. Database Verdict

**DATABASE VERIFIED: PASS for measured CommerceHub financial readiness.** Connectivity, schema, migrations, and canonical identity are healthy. The latest post-refund authoritative reconciliation is healthy with zero mismatches, and independent read-only PostgreSQL checks confirmed the Gate 2 refund, return, ledger, audit, stock, payout-method ownership, and projection state.

## 43. MassTransit Runtime

The `masstransit-bus` readiness entry was `Healthy`. CommerceHub projection, CommerceHub order projection, marketplace discovery, trust, order status, payment, delivery, refund, accounting, and related endpoints all reported ready.

## 44. Integration Outbox

`integration_outbox_readiness` was `Healthy` with `PendingCount=0`, `ZeroAttemptCriticalLagCount=0`, `RepeatedFailureCount=0`, `OperatorReconciliationCount=0`, and no warning or critical lag.

## 45. Projection Outboxes

`projection_outbox` was `Healthy` with zero stock, marketplace, and marketplace-projection pending, repeated-failure, warning-lag, or critical-lag counts.

## 46. Accounting Outbox

`accounting_outbox_readiness` was `Healthy` with zero critical pending, repeated failure, critical lag, and warning lag.

## 47. Other Delivery Outboxes

Payment cancellation, provider delivery, and MassTransit bus outbox readiness were all `Healthy` with zero pending, terminal/dead-letter, repeated-failure, warning-lag, and critical-lag counters reported by their respective checks.

## 48. Test Outbox Diagnostic

`GET /api/test-auth/outbox/status` returned `200` with `processedCount=25`, `pendingCount=0`, `failedCount=0`, `journalCount=25`, and `stockSyncCount=25`.

## 49. Event Verdict

**EVENT VERIFIED: PARTIAL.** Transport and measured durable delivery backlogs are healthy, and predecessor M-1D evidence captured an exact refund event. No new Gate 2 observer captured and validated a fresh exact event envelope, so transport health is not promoted to full event-semantic verification.

## 50. MongoDB Projection

`/health/marketplace` reported MongoDB collection `commercehub_products` exists with no missing indexes. General readiness also reported MongoDB reachable with `ok=1`.

## 51. Elasticsearch CommerceHub Boundary

The CommerceHub-specific marketplace check reported alias `commercehub_products` pointing to desired/write index `commercehub_products_v2`, three desired documents, no missing fields, attribute templates present, no legacy concrete index, and a passing smoke test.

## 52. Projection Verdict

**PROJECTION VERIFIED: PASS for the measured CommerceHub projection boundary.** MongoDB structure and CommerceHub Elasticsearch alias/index checks are green, and projection outboxes are drained. This does not declare the entire shared Elasticsearch cluster healthy.

## 53. Liveness

`GET /health/live` returned `200 Healthy` with correlation ID `21049a6c-4fcc-4722-9215-f0cc4d9ee693` at `2026-09-04T17:30:30.0090081Z` after the corrected runtime restart.

## 54. Marketplace Health

`GET /health/marketplace` returned `200 Healthy`. Marketplace reported zero drift, and CommerceHub platform dependencies were healthy within that scoped check.

## 55. Aggregate Readiness

`GET /health/ready` returned `503 Unhealthy`, but payout external-truth readiness is now healthy with a zero-mismatch reconciliation. The remaining unhealthy dependency is the shared Elasticsearch cluster, which is RED with `64` active shards and `1` unassigned primary shard. Read-only allocation diagnostics identify that shard as `.ds-.kibana-event-log-ds-2026.09.02-000028`, unassigned after cluster recovery because the only node is above the `90%` disk high watermark: `8.9 GB` free, `91.7%` used, versus `10.8 GB` required free. The CommerceHub index `commercehub_products_v2` remains green with its primary active. Certificate provider and Caddy edge are degraded because they are disabled. The broader `/health` report also records one stale inventory reservation and isolated foreign-tenant projection documents.

## 56. Human Verification And Open Gates

**RUNTIME VERIFIED: FAIL. HUMAN VERIFIED: PARTIAL.** The operator observed successful two-vendor customer checkout, tenant-isolated fulfillment, customer refund submission, Super Admin approval and execution, vendor return logistics, receipt, Super Admin integrity review, and symmetric tenant-owned payout-method presentation. Static mobile-width observations now cover the Gate 2 customer refund, the unaffected shopixy vendor suborder, and the Super Admin refund queue/filter layout; the highquality return detail, Gate 2 Super Admin outcome/integrity view, and interactive mobile-state matrix remain outstanding. Other open gates are the `503` readiness result, RED shared Elasticsearch cluster, stale inventory reservation, missing live non-Admin `403` evidence, and a retained full order-lifecycle event envelope. No API, database, queue, test, or projection result is relabeled as human evidence.

## 57. Manual Operator Sequence

All business mutations in this sequence must originate from the running POS, CommerceHub customer, or Super Admin frontend. Direct API, SQL, service, seed-controller, or projection writes are prohibited.

1. Obtain two authenticated vendor browser sessions, one verified CommerceHub customer session, and one Super Admin session. Record actor IDs and tenant IDs without recording passwords or bearer tokens.
2. Capture a read-only baseline: liveness, marketplace health, aggregate readiness, latest reconciliation report ID, outbox counters, projection counts, and the absence of disposable test order numbers.
3. In each vendor POS session, create a uniquely prefixed disposable product and variant, add stock through the inventory UI, configure publication prerequisites, and publish one CommerceHub listing. Record product, variant, listing, warehouse, vendor, and tenant IDs from frontend responses or read-only follow-up reads.
4. In the CommerceHub customer frontend on desktop, discover both listings, add one item from each vendor, review the two-store shipping split, and submit one authenticated multi-vendor checkout. Use only a genuinely available backend-issued payment method; do not simulate online capture or provider confirmation.
5. In each vendor frontend, independently verify that only the owned suborder is visible, then process, ship, and deliver it through the offered actions. Record action availability, status transitions, timestamps, and the parent-order aggregation after each vendor transition.
6. In the customer frontend, request an item-level refund for one delivered line, optionally upload allowed evidence through the provided upload flow, and verify that the other vendor line remains unaffected.
7. In the owning vendor frontend, execute the offered return-logistics sequence through pickup, transit, delivery, and receipt. Do not skip required transitions or mutate return state from Super Admin.
8. In Super Admin, review and approve the refund, then execute it only after the configured return trigger is satisfied. Record actor, method, reference, amount, currency, status, audit, and ledger evidence. Execute a payout only when an eligible online-payment balance and genuine external handoff/completion evidence are available; otherwise record payout execution as externally blocked rather than fabricating success.
9. Run finance reconciliation from the authorized frontend action, then independently read the append-only report and verify order, refund, commission, payable, payout, and aggregate results at the captured snapshot boundary.
10. Repeat the required customer, vendor, and governance observations at mobile width. Record loading, empty, blocked, success, refresh, back-navigation, stale-session, and cross-tenant behavior as human observations, not inferred machine evidence.
11. Capture a fresh exact event envelope and correlate its event ID, aggregate ID, affected tenant scope, version, timestamp, outbox row, consumer result, and resulting projection or ledger state.

The desktop browser workflow has been executed through checkout, fulfillment, refund, return, refund execution, integrity review, and two-vendor payout-method isolation. Partial static mobile-width evidence is recorded in Section 66; remaining mobile views and interactive states, plus a live non-Admin role probe, remain outstanding. Credentials, passwords, bearer tokens, and customer contact details are intentionally not recorded in this report. Online-payment and payout completion steps also require real sandbox/provider and external-transfer evidence.

## 58. Pre-Mutation Manual Baseline

The read-only baseline was captured on `2026-09-04` before any disposable Gate 2 business mutation:

- POS Web `http://localhost:5173`, CommerceHub Customer `http://localhost:5175`, API liveness, and marketplace health each returned `200`.
- Aggregate readiness remained `503 Unhealthy` solely because the shared Elasticsearch cluster was RED with `64` active shards and `1` unassigned shard. The CommerceHub marketplace index remained green.
- Projection, integration, payment-cancellation, provider-delivery, MassTransit bus, and accounting outbox pending/failure/critical-lag counters were all zero.
- Current-tenant projection scope was SQL `17` versus MongoDB `18`, with `58` isolated foreign-tenant documents preserved out of `76` total MongoDB documents.
- CommerceHub search sync reported SQL-visible `3`, Elasticsearch `3`, synced-flag `3`, MongoDB-published `20`, sync ratio `1.0`, and auto-healing queue depth `0`.
- Latest reconciliation report `e2edf361-dfad-4296-bc5b-8d9dbbe19a77` remained `Healthy` with `MismatchCount=0` at `2026-09-04T13:00:32.034977Z`.
- Six marketplace orders existed. The newest was `ORD-CH-0012`, created `2026-09-02T21:09:37.917570Z`; no marketplace order was created during baseline capture.
- The selected disposable product prefix `G2-20260904-` had zero matches, and no CommerceHub listing had been created during the preceding hour.
- One stale inventory reservation still held `1.00` unit and was approximately `2441` minutes old. No direct repair was attempted.
- Paymob and Stripe operational readiness checks were green in sandbox mode, but no provider transaction or external money movement was executed or inferred from readiness.

## 59. Two-Vendor Publication Execution Evidence

The operator completed the frontend product, stock, and CommerceHub publication actions. Independent read-only verification found:

- Vendor `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` published listing `a0c32bf5-fd89-4705-a650-e32d1626fe56` for product `a621601a-bf86-89ab-23c9-5a281c62c575`, variant `6c80f0d0-5ec2-49eb-8d17-7d9507621a72`, SKU `G2-20260904-A`. The product is active, online-visible, published, EGP-priced at `1500.00`, and has `50.00` available units.
- Vendor `a6c9e71e-dd48-49cb-947a-6f460423360e` published listing `bb811ba0-1083-4742-90cf-6cb2fbed769d` for product `8909a11c-374c-b8c9-15dd-811f2593f9b2`, variant `cbb3a9e0-a683-4d36-bbfd-7dd26452fa18`, SKU `G2-20260904-B-128`. The product is active, online-visible, published, EGP-priced at `650.00`, and has `40.00` available units. Its disposable product name contains one leading space, which did not prevent discovery.
- The CommerceHub customer catalog endpoint returned both listings for `G2-20260904`; Elasticsearch search independently returned the same two listings and current stock values.
- CommerceHub platform health remained `200 Healthy`. The desired CommerceHub Elasticsearch document count increased from `3` to `5`, the alias remained on `commercehub_products_v2`, and its smoke check passed.
- Vendor A publication emitted event `3b3e518b-183f-476f-8550-31263640dad7`, `ProductUpdatedEvent`, aggregate/product `a621601a-bf86-89ab-23c9-5a281c62c575`, tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`, correlation `5880335c-5faf-4962-a345-38ef123cbbe6`, version/event-version `5`, occurred `2026-09-04T14:00:16.1513791Z`, changed field `CommerceHubListingCreated`, and was published successfully with zero attempts or errors.
- Vendor B publication emitted event `80a2f752-aaa1-4034-ba69-056984fdba3e`, `ProductUpdatedEvent`, aggregate/product `8909a11c-374c-b8c9-15dd-811f2593f9b2`, tenant `a6c9e71e-dd48-49cb-947a-6f460423360e`, correlation `ee6ecde0-dc2e-4a92-b346-c95beedc29e2`, version/event-version `6`, occurred `2026-09-04T14:02:47.7115846Z`, changed field `CommerceHubListingCreated`, and was published successfully with zero attempts or errors.
- Stock events for both vendors were published successfully with positive versions and no failures. Readiness outbox counters remained zero after delivery.
- No marketplace order or refund involving these disposable products existed at this checkpoint. Aggregate readiness remained `503` because of the pre-existing shared Elasticsearch RED condition.

This evidence certifies the two-vendor publication and machine-discovery checkpoint. It does not yet certify checkout, fulfillment, refund, return, payout, or human desktop/mobile behavior.

## 60. Two-Vendor Checkout And Mixed-Session Remediation

The first checkout attempt after publication accepted the CommerceHub customer JWT but inherited the host-scoped POS cookie tenant `a6c9e71e-dd48-49cb-947a-6f460423360e`. The tenant interceptor correctly rejected a reservation belonging to tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` with `Security Violation: Attempted to save data for a different tenant`. The checkout transaction rolled back; no marketplace order was committed.

The remediation adds a disposal-based `TenantContext.BeginGlobalScope()` and limits its use to the trusted CommerceHub checkout orchestration boundary. The previous ambient tenant is restored when checkout returns or throws. `CheckoutAsync_TwoStores_WithAmbientVendorTenant_UsesScopedGlobalAuthority` recreates the PostgreSQL save interceptor with an ambient vendor tenant, verifies both tenant-owned reservations persist, and verifies the original tenant context is restored. The focused checkout selection passed `11/11`, and the broad CommerceHub slice passed `797/797`.

After the corrected Debug API restarted, `/health/live` and `/health/marketplace` both returned `200 Healthy`; the CommerceHub Elasticsearch alias remained healthy with five desired documents. The first UI submission received a legitimate optimistic-concurrency `CART_CONFLICT` at server cart version `5`; read-only SQL confirmed no order was created. The refreshed retry then succeeded in the CommerceHub frontend, where the operator observed `Order placed successfully`, order `ORD-CH-0016`, and total EGP `2,150`.

Independent read-only persistence verification found:

- Marketplace order `434b9d1e-4941-4a1f-9d3c-ca5fee96b635`, `ORD-CH-0016`, customer `cb6dcd08-8297-4a4a-8c9b-33012270ebc1`, COD, EGP subtotal/total `2150.00`, shipping `0.00`, pending/CashOnDelivery state, idempotency key `e82b3bf8-f4e5-4914-87a3-38570f355e3d`, source projection version `1`, and `IsProjected=true`.
- Suborder `80fe2453-0abf-4d57-bd60-6e5dbe6a086d`, `ORD-CH-0016-A`, belongs to vendor `a6c9e71e-dd48-49cb-947a-6f460423360e`, totals EGP `650.00`, commission `65.00`, and vendor earning `585.00`.
- Suborder `cf29ce20-58a1-4c93-9374-d9ecb3fd6a7a`, `ORD-CH-0016-B`, belongs to vendor `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`, totals EGP `1500.00`, commission `150.00`, and vendor earning `1350.00`.
- Each suborder contains exactly its expected disposable listing and one unit. Two active reservations link the marketplace order, owning suborder, order item, product, variant, warehouse, and correct vendor tenant; each reserves `1.00`, commits `0.00`, releases `0.00`, and has no expiry.
- The checkout idempotency claim is customer-bound, has a persisted response, and uses the CommerceHub cart authority tenant `8a74c4f4-5c4a-492c-8f56-d1bbddcf4c11`. The marketplace-order count increased atomically from six to seven, with `ORD-CH-0016` newest.
- MongoDB `commercehub_orders` contains the matching two-suborder projection and totals. Its consumed event metadata is event `dcf4ed90-882f-49a7-a335-8c310c32cbc9`, version `1`, occurred `2026-09-04T14:43:07.172Z`, source mutation version `1`, checksum `F63F12E9B0B6D96B609C58A3C95C963ACBA15BF1D18B34F8FB0DA08CB1A3DAD4`.
- The MassTransit transactional outbox had already drained before inspection, and the separate marketplace outbox does not carry this integration event. The consumer result and exact event identity/version/timestamp are verified, but the full transient order-created envelope is not claimed as retained evidence; `EVENT VERIFIED` therefore remains partial.

This certifies frontend-originated two-vendor checkout persistence, split ownership, inventory reservation, idempotency, and derivative order projection. Fulfillment, refund, and return evidence is recorded in the following sections; complete mobile human behavior and external payout execution remain uncertified.

## 61. Two-Vendor Fulfillment And COD Financial Evidence

The operator used each authenticated vendor frontend independently at `http://localhost:5173/commercehub/orders`. The highquality tenant saw only its owned `ORD-CH-0016-A` line for EGP `650.00`; the shopixy tenant saw only its owned `ORD-CH-0016-B` line for EGP `1500.00`. Each detail view reported payment and inventory readiness as `Ready`. Each vendor executed only backend-authorized `Pending -> Processing -> Shipped -> Delivered` actions. At `Delivered`, COD changed to `Collected` and no further fulfillment action remained.

Independent read-only verification found:

- Six append-only status-history rows preserve all three transitions for both suborders with their vendor tenant, timestamp, and request correlation ID. Highquality transitions occurred at local times `17:57:41.892254`, `17:59:52.872897`, and `18:00:34.059499`; shopixy transitions occurred at `18:03:57.830501`, `18:04:30.209708`, and `18:05:01.735804`.
- The parent marketplace order reached `Delivered / Collected`, projection mutation version `7`. Both suborders reached `Delivered / Collected`, each at suborder version `4`; both item rows are delivered with independent delivery timestamps.
- Each reservation moved from active to committed with `ReservedQuantity=1.00`, `CommittedQuantity=1.00`, and `ReleasedQuantity=0.00`. Vendor stock converged from `50.00` to `49.00` and from `40.00` to `39.00`, with no residual reserved quantity for either delivered line.
- Highquality earning `e42ae7d5-f01b-4d12-b188-74046a724ac9` is EGP `585.00`; shopixy earning `0c6c7851-4492-42d9-96bd-8903b3ee9e17` is EGP `1350.00`. Both are available after the configured seven-day delay and have no paid-out or reserved payout amount.
- Highquality delivery created an EGP `650.00` vendor COD credit, EGP `65.00` vendor commission debit, and EGP `65.00` platform commission credit. Shopixy delivery created the corresponding EGP `1500.00`, `150.00`, and `150.00` entries. All six entries are tied to the correct suborder reference and deterministic idempotency key.
- MongoDB converged to `Delivered / Collected` for the parent and both suborders. The final projection consumed event `e422cbbe-3488-4398-88af-a952c70ad193`, occurred `2026-09-04T15:05:02.066Z`, event/source version `7`, checksum `129EA545A7BAF6355172922CE1B69DFF3AB7EEB3DB576584C328E35EF4404E45`.
- Reconciliation report `64f395ab-f00f-4f79-a67f-dd9a8942cdf8` at `2026-09-04T15:00:11.936240Z` remained `Healthy` with zero mismatches after the first vendor delivery. The later post-refund result is recorded in Section 40.

This certifies tenant-isolated frontend fulfillment, parent aggregation, COD collection, inventory commitment, vendor earnings, commission ledger creation, and final derivative order convergence for `ORD-CH-0016`.

## 62. Customer Refund And Return Evidence

The customer frontend submitted refund request `23994c3b-1d54-44eb-92be-592881fbec60` for one EGP `650.00` highquality item in `ORD-CH-0016`. The sibling shopixy line was not included. Evidence upload was optional and no evidence media row was attached.

The Super Admin frontend approved the request at `2026-09-04T19:03:28.321514+03:00`. Approval atomically created return request `1f68efd8-89d9-4b54-93e4-cb8397eeb353` with the configured `OnPickup` refund trigger and moved the refund to `WaitingForReturn`.

The highquality vendor frontend advanced the owned return through pickup scheduling, pickup, transit, delivery to vendor, and receipt confirmation. Confirmation at `2026-09-04T19:17:27.616633+03:00` recorded requested quantity `1`, resellable quantity `0`, and damaged quantity `1`. The returned item was not restocked: highquality stock remained `39.00` with zero reserved quantity. No return transition affected the shopixy item or its stock.

## 63. Refund Execution And Accounting Evidence

Pickup changed the refund to `AwaitingPayout`. The Super Admin frontend then used the dedicated Development COD debt-reduction simulation. The refund completed at `2026-09-04T19:11:44.739614+03:00` with one successful EGP `650.00` execution and reference `DEV-SIM:M1D-23994c3b1d5444eb92be592881fbec60`. Its persisted note explicitly states that no external money movement occurred.

Independent read-only verification found one financial-refund row with EGP `65.00` commission reversal and EGP `585.00` vendor-earning reversal. The highquality earning is `Reversed`, has no paid-out or payout-reserved amount, and records the same execution time. Two deterministic refund ledger entries reverse the EGP `65.00` vendor debt and platform commission. The committed financial-operation audit records vendor debt changing from EGP `197.00` to EGP `132.00`, with platform cash and vendor payable both unchanged at zero. Replaying the frontend execution action did not create an additional execution, financial-refund, ledger, or audit effect.

The final canonical order state is parent `Delivered / PartiallyRefunded`; highquality suborder `Refunded / Refunded`; shopixy suborder `Delivered / Collected`. MongoDB converged to the same state at event/source version `9`, last event `77113390-c4f7-41bd-8274-ec61d06838d7`, with projection checksum `FAEFD8ED94753C36E9376A105E78F7CD911C9FE2E6C8CCE91625A53587448097`.

## 64. Super Admin Integrity Remediation

The Super Admin finance integrity view initially reported a false `ORDER_LEDGER_MISMATCH` for a pending COD sibling under a different partially refunded parent. The live issue query treated the parent refund state as financial recognition for every child. `BuildIntegrityOrderLedgerMismatchScope` now reads each suborder's own status and payment status and requires ledger equality only when that suborder is financially recognized or already has order-ledger entries.

`GetIntegrityCheckAsync_ShouldNotRequireLedgerForPendingCodSiblingOfPartiallyRefundedOrder` protects this boundary. The focused `SuperAdminFinancialDashboardServiceTests` selection passed `8/8`. After the corrected API restart, the Super Admin frontend reported integrity status `OK`, zero order-flow, refund-flow, missing-reversal, payable, and receivable findings, zero total flags, and no sampled inconsistencies. This live integrity result is not represented as a post-refund append-only reconciliation report.

## 65. Payout-Method Isolation Evidence

The highquality frontend screenshot shows one default, unverified Bank Transfer method. A read-only PostgreSQL transaction confirmed method `407f8314-4104-4e23-bd40-0d6754f2f560`, vendor `a6c9e71e-dd48-49cb-947a-6f460423360e`, destination `GATE2-HQ-DESTINATION`, created at `2026-09-04T19:38:45.930037+03:00`.

The shopixy frontend screenshot identifies shopixy as the active Owner tenant and shows only its default, unverified Bank Transfer destination `GATE2-SHOPIXY-DESTINATION`. PostgreSQL confirmed method `87b6bbc8-3f61-4497-add7-ec4d84592018`, vendor `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`, created at `2026-09-04T20:29:32.935568+03:00`.

Independent authenticated API probes returned `200` for both vendors. Highquality received exactly its `GATE2-HQ-DESTINATION` method, while shopixy received exactly its `GATE2-SHOPIXY-DESTINATION` method. Neither response contained the other vendor's method. This certifies the required non-empty payout-method read isolation. No payout was created or executed, and no external transfer is claimed.

## 66. Mobile-Width Human Evidence

At an emulated phone viewport, the customer order UI displayed the Gate 2 EGP `650.00` damaged-item refund, quantity `1`, the submitted reason, and the completed Development refund simulation. The visible warning explicitly stated that no external money movement occurred, and the deterministic `DEV-SIM:M1D-...` reference was presented without exposing credentials or customer contact details.

The shopixy vendor order list rendered at mobile width and displayed owned suborder `ORD-CH-0016-B`, parent `ORD-CH-0016`, product `G2-20260904-A`, EGP `1500.00`, and `Delivered / Collected`. This is human evidence that the unaffected sibling remains visible and correctly represented on the vendor mobile layout. It does not substitute for a mobile highquality return-detail observation.

The Super Admin refund page rendered its responsive header, status/vendor/date filters, reset action, and current-queue indicator at mobile width without horizontal clipping in the captured viewport. The visible row was not the Gate 2 refund, and the screenshot did not show the finance integrity panel; it therefore certifies responsive queue/filter presentation only, not the Gate 2 governance outcome or integrity result at mobile width.

These screenshots do not cover the full requested loading, empty, blocked, success, refresh, back-navigation, stale-session, and cross-tenant interaction matrix. `HUMAN VERIFIED` remains partial.

## 67. Final Declaration

The inspected Gate 2 code hardening is valid and the broad CommerceHub machine-test slice is green, but the runtime, security, event-semantic, external-provider, and human-observation criteria are not all met. The only honest declaration is **GATE 2 — NOT YET COMPLETE**.
