# Shopixy M-1C Multi-Vendor Marketplace Order Certification

Date: 2026-09-01

## Verdict

**CODE VERIFIED: PASS. TEST VERIFIED: PASS for the focused M-1C boundary. API VERIFIED: PASS. DATABASE VERIFIED: PASS. EVENT VERIFIED: PARTIAL. PROJECTION VERIFIED: PASS. HUMAN VERIFIED: NOT VERIFIED. Overall M-1C certification: NOT YET COMPLETE.**

One supported Development customer checkout created one SQL-authoritative marketplace parent order from one cart containing products from two vendors. The backend produced exactly one isolated `CommerceHubSubOrder` per `StoreId`, created one correctly store-owned inventory reservation per order item, reconciled parent and split totals in EGP, and returned the same parent and suborders on exact idempotent replay.

Overall certification remains open because no human checkout/order/vendor UI observation was performed, Storefront port `5174` was unavailable, the CommerceHub package has no automated frontend test script, and the transient MassTransit outbox had already cleaned the parent-correlated envelope before inspection. RabbitMQ delivery and consumer health were observed, but are not promoted to exact per-order event-envelope proof.

No source, test, build, HTTP response, datastore inspection, queue statistic, or projection is promoted to human UI evidence in this report.

## Certified Boundary

- PostgreSQL owns the marketplace parent order, vendor suborders, immutable item snapshots, customer-bound checkout idempotency, inventory reservations, payment state, shipping snapshots, and commission snapshots.
- `CommerceHubCheckoutService` recomposes current SQL cart, listing, product, variant, category, vendor, payment, shipping, promotion, price, and stock authority inside a serializable checkout boundary.
- Vendor allocation is derived by `GroupBy(line => line.StoreId)` after current SQL authority is resolved.
- Exactly one live suborder is allowed for each `(MarketplaceOrderId, StoreId)` pair.
- Parent and all suborders use EGP and reconcile from backend-derived line and shipping values.
- Inventory reservations are created before the checkout transaction commits and retain parent, suborder, item, store, variant, and warehouse lineage.
- Idempotent replay is bound to the customer, key, and request hash and returns the already committed SQL order.
- MongoDB order documents are derivative customer read models. RabbitMQ and outbox infrastructure provide delivery and convergence, not business authority.

## Verification Matrix

| Evidence class | Command/evidence | Result |
| --- | --- | --- |
| CODE VERIFIED | Checkout, cart, shipping, inventory, entities, mappings, events, and projection source inspection | PASS |
| TEST VERIFIED | Real PostgreSQL multi-vendor split, replay, and rollback integration suite | 2/2 passed |
| TEST VERIFIED | Checkout, cart, inventory, financial-control, API, runtime, and architecture selection | 67/67 passed |
| TEST VERIFIED | POS marketplace lifecycle and admin-order API presentation tests | 22/22 passed |
| API VERIFIED | Supported register, delivered email verification, login, cart save, pricing, COD checkout, and exact replay | PASS |
| DATABASE VERIFIED | Read-only PostgreSQL reconciliation for the same parent ID | PASS |
| EVENT VERIFIED | RabbitMQ projection queue running with one consumer, two published/delivered/acked messages, zero backlog, zero redelivery; MassTransit pending outbox count zero | PARTIAL; no retained parent-correlated envelope |
| PROJECTION VERIFIED | Read-only MongoDB reconciliation for the same parent and two suborder IDs | PASS |
| HUMAN VERIFIED | Desktop/mobile checkout, customer order detail, and both vendor order screens | NOT VERIFIED |
| Backend build | `dotnet build PointOfSale.sln --no-restore` | PASS, 0 warnings, 0 errors |
| CommerceHub build | `npm run build` in `PointOfSale.CommerceHub` | PASS; stale Browserslist and large-chunk warnings remain |
| POS Web build | `npm run build` in `PointOfSale.Web` | PASS; large-chunk warning remains |
| Marketplace health | `GET /health/marketplace` after checkout | Healthy; zero pending PostgreSQL migrations and no reported marketplace drift |

## Live Lineage

- Customer: `f1935cb0-600f-4454-8cc8-41d71d111816`, created and verified through supported customer APIs and the delivered verification email.
- Cart: `36e31f3e-6efb-4b36-861b-ab6e6b64a558`, SQL-backed version `1`, two items, two distinct stores.
- Parent order: `f5b0edaa-3fa0-4d95-a7fe-1a02ada0de58`, `ORD-CH-0002`.
- Idempotency key: `m1c-live-315b6d9601964c83bafb547b566249ef`.
- Store A: `a6c9e71e-dd48-49cb-947a-6f460423360e` (`highquality`).
- Store A listing/product/variant: `f0c24626-73a8-4f18-9464-3655d6044d6f` / `022ad414-e75c-67de-f8bc-9b52899a46a3` / `240bd2e6-d2de-4ba9-a0f8-eb3e058e0309` (`OPPO Reno 5 Pro`).
- Store A suborder: `80689536-fee3-4a92-b6ac-a9ca739fa8b6`, `ORD-CH-0002-A`, `1320.01 EGP`.
- Store B: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` (`shopixy`).
- Store B listing/product/variant: `884794fc-550b-45e1-9890-3fd309e008ba` / `580b30bd-1829-131c-2cc4-5f6cf3994e09` / `e5857268-db63-4806-9af8-f5706bbda5cf` (`Nokia N73`).
- Store B suborder: `1800f814-562b-4a51-9113-2bef95da43b6`, `ORD-CH-0002-B`, `400.00 EGP`.

## API Results

- Public detail APIs returned both current listings in EGP with the expected store, product, variant, price, and stock lineage.
- Customer registration returned `200` and sent a real Development verification email to the disposable mailbox.
- Email verification returned `200`; normal login returned `200` and issued the customer session.
- Customer profile returned the same customer ID and `IsEmailVerified=true`.
- Cart save returned a SQL-backed authenticated cart at version `1`, with two items and two distinct `StoreId` values.
- Pricing returned subtotal `1720.01 EGP`, shipping `0.00 EGP`, total `1720.01 EGP`, and one `Free Shipping` selection per store.
- COD was available for the entire cart. PAYMOB and STRIPE correctly failed availability because they were not enabled for every store.
- Checkout returned parent `ORD-CH-0002`, exactly two suborders, `Pending` order state, and `CashOnDelivery` payment state.
- Exact replay returned the same parent ID, order number, suborder IDs, stores, items, amounts, shipping snapshots, and payment snapshots with `IsIdempotentReplay=true`.

## Database Results

- PostgreSQL contained exactly one non-deleted parent row for `f5b0edaa-3fa0-4d95-a7fe-1a02ada0de58`.
- PostgreSQL contained exactly two non-deleted suborders and exactly two distinct `StoreId` values for that parent.
- The parent subtotal and total were both `1720.01 EGP`; summed suborder subtotal and total were also `1720.01 EGP`.
- Each suborder contained exactly one item and its item total equaled its suborder subtotal.
- The parent and both suborders had `Pending` order state, `CashOnDelivery` payment state, and `COD` payment method authority.
- Exactly two active inventory reservations existed for the parent, each reserving quantity `1` for its own order item.
- For both reservations, `TenantId` equaled the owning suborder `StoreId`, `SubOrderId` equaled the item suborder, and the parent `OrderId` matched.
- The checkout idempotency table contained one key row owned by the CommerceHub customer, with no legacy `OnlineCustomerId`, a request hash, a persisted response, and the same parent order ID.
- SQL marked the order projected with projection mutation version `1`.
- Inspection used read-only SQL only. No direct SQL business mutation was performed.

## Event And Projection Results

- The `commercehub-order-projection` RabbitMQ queue was running with one active consumer.
- Queue statistics showed two publishes, two deliveries, two acknowledgements, zero ready messages, zero unacknowledged messages, and zero redeliveries.
- MassTransit had zero pending SQL outbox messages at inspection time and scoped marketplace health reported the platform Healthy.
- The exact parent-correlated outbox/event envelope was no longer retained after delivery cleanup. Queue counters alone do not prove which message belonged to the certified parent, so EVENT VERIFIED remains PARTIAL.
- MongoDB contained one `commercehub_orders` document for the same marketplace parent ID and order number.
- The Mongo document matched SQL currency, subtotal, shipping, total, order state, payment state, both suborder IDs, both store IDs, both order numbers, both suborder totals, and one item per suborder.
- MongoDB recorded projection checksum `85F88C62BFD814F1007CDE717F41F18E23291DEAE1E7ACACA0422F90B08D433E` and source mutation version `1`.
- The projection had no retained `LastEventId`, consistent with repair/reconciliation being allowed to recompose the derivative document from SQL. Projection correctness is verified independently; exact event lineage is not inferred from it.

## Rollback And Replay

- The real PostgreSQL integration suite verified a successful two-store COD checkout with one parent, two isolated suborders, and two store-owned reservations.
- The integration fixture reconciled `450 + 30 = 480 EGP` for its independent test data.
- Exact idempotency replay returned the same semantic checkout and only one persisted idempotency claim.
- A forced reservation failure for the second store rolled back parent order, suborders, items, reservations, inventory inbox/movements/outbox, idempotency claim, and cart lock.
- This rollback scenario proves the marketplace split is one transaction, not a sequence of independently committed vendor orders.

## Broader Suite Status

The focused M-1C executable boundary is clean. Repository-wide suites are not clean and are not represented as M-1C passes.

- Six rerun architecture failures were classified as source-freeze drift: three referenced historical migration files removed by the consolidated baseline, two expected stale `.ai-context` PASS markers, and one expected the obsolete literal `RETURN_OVERRIDE_CONFIRMED_NOT_ALLOWED` while current policy still rejects the disallowed override with `RETURN_OVERRIDE_STATUS_NOT_ALLOWED`.
- The broad backend suite exceeded the 600-second command limit after surfacing unrelated failures in Storefront SQLite setup, social login, payment-admin fixtures, schema-dependent admin-order tests, refund test infrastructure, deterministic admin-order filtering, and documentation freezes. It produced no complete final total.
- The broad POS Web suite completed with `1194/1206` tests passing across `145` files. Its 12 failures were in i18n, Storefront payment/cart/auth state, warehouse frontend certification, and footer-authoring feedback, not the focused marketplace-order presentation files.
- The focused POS marketplace lifecycle and admin-order API files passed `22/22` after that broad run.
- `PointOfSale.CommerceHub/package.json` has no test script. Its TypeScript and production build passed, but no automated CommerceHub component test result is claimed.

## Residual Risks

- Human checkout, customer order detail, vendor A order visibility, vendor B order visibility, and responsive presentation remain unobserved.
- Storefront `http://localhost:5174` was not listening. POS Web `5173`, CommerceHub `5175`, and API `5010` were reachable.
- The aggregate `/health` and `/health/ready` endpoints remained Degraded because of broader disabled integrations and bounded projection data. Scoped `/health/marketplace` remained Healthy.
- Exact parent-correlated event evidence was ephemeral and unavailable after outbox cleanup. Future certification should capture the outbox envelope or broker trace before retention cleanup.
- Existing broad backend and POS Web regressions remain outside the M-1C focused boundary and prevent a repository-wide clean-suite claim.
- Existing large frontend bundles remain a performance concern.
- M-1A remains not deployment-certified in its existing report and M-1B remains incomplete for human UI evidence. Those predecessor verdicts are not silently promoted by this M-1C runtime exercise.

## Exit Criteria

1. Sign in through the CommerceHub customer UI and repeat a two-store cart and checkout at desktop and mobile widths using supported Development data.
2. Confirm one parent order is shown with two clearly isolated vendor segments, correct EGP totals, one item per segment, and no duplicate or cross-store item.
3. Sign in to each vendor/POS scope and confirm each vendor sees only its own suborder, item, shipping snapshot, payment state, and fulfillment actions.
4. Confirm the customer order-detail route survives refresh and shows the same parent and split identifiers without stale cart or duplicate-order behavior.
5. Capture a parent-correlated outbox/event envelope before cleanup and reconcile its event ID, aggregate ID, affected store IDs, delivery acknowledgement, and Mongo projection metadata.
6. Re-run scoped marketplace health and the focused M-1C suites after the human observation without mutating the certified order lifecycle.
7. Resolve or formally baseline the repository-wide backend and POS Web failures before claiming whole-repository regression cleanliness.

Until the human and exact event-envelope evidence is recorded, the final operational verdict remains **NOT YET COMPLETE**.
