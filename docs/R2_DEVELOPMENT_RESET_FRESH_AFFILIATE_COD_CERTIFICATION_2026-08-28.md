# R-2 Development Reset and Fresh Affiliate COD Certification

Date: 2026-08-28  
Scope: disposable Development transaction reset, normal-host recovery, and one fresh Affiliate COD lifecycle  
Method: Codex direct only; direct HTTP plus PostgreSQL, MongoDB, RabbitMQ, Redis, and Elasticsearch inspection; no browser automation

# Executive Summary

## Final verdict: CONDITIONAL PASS

The R-2 reset and fresh-event objective passes within its Development certification scope. The historical R-1 transaction backlog is gone, preserved master/configuration records remain available, the normal host drains new custom outbox rows, and a new Affiliate COD order converges from PostgreSQL through RabbitMQ into the affiliate Mongo projection. Duplicate COD collection and commission approval attempts do not duplicate financial effects.

This is not a whole-repository release approval. The broad historical regression command exceeded its ten-minute limit after 137 observed failures, readiness correctly remains HTTP 503 for environment/configuration blockers, shared Mongo collections contain unrelated SQL-ahead documents, and return plus POS Admin live visibility were not verified.

No production source change was required for R-2. R-2 added only this report and ignored `.tmp` certification utilities/logs. Existing O-4.x/R-1 worktree changes were preserved.

# Scope and Safety

- The reset was limited to disposable Development transaction/event data. It did not recreate the schema or delete master/configuration tables.
- Historical order fixtures `ORD-83B3-2026-01005` through `ORD-83B3-2026-01021` were removed under the R-2 reset authorization; a second reset was not performed after the clean state was observed.
- RabbitMQ topology was not deleted. Only confirmed disposable retained messages were purged; final queue inspection found no ready or unacknowledged message in the relevant queues.
- No global Redis flush was used. Final matching-key inventory returned zero.
- Shared Mongo documents outside the known R-2 order identities were preserved rather than guessed to be disposable.
- No real payment, payout, provider callback, carrier transaction, or browser automation was used. The order used COD and a manual fulfillment path.
- Read-only SQL probes explicitly set `default_transaction_read_only=on`.
- No credential, provider secret, auth token, webhook signature, or raw referral secret is included in this report.

# Environment

| Component | Endpoint/result | Final classification |
|---|---|---|
| PostgreSQL | `PosEcommerceDb_Baseline` on `192.168.1.5:5432`; probe role `postgres`; read-only session on | Reachable and canonical |
| Schema | 21 migrations; latest `20260828105623_O47_OrderDeliveryEvidenceAuthority` | Current for this worktree |
| MongoDB | `ecommerce_read_db`; 38 collections | Reachable; fresh projection converged; unrelated shared drift preserved |
| RabbitMQ | Remote broker and management API reachable | Relevant queues empty after processing |
| Redis | Database 0; 4.026 ms final ping | Reachable; no known-order key matches |
| Elasticsearch | Cluster `green` | Reachable |
| API | Bounded normal host on `localhost:5010` | Live during certification, then stopped |

# Reset Evidence

The before-state is the signed R-1 snapshot in `docs/R1_OUTBOX_DRAIN_PROJECTION_RECOVERY_CERTIFICATION_2026-08-28.md`. The after-state is the final read-only R-2 inventory.

| Resource | R-1 before | R-2 final | Result |
|---|---:|---:|---|
| Historical SQL orders `01005`-`01021` | 17 | 0 | PASS |
| Fresh SQL order `01022` | 0 | 1 | PASS |
| Other SQL orders | Not part of reset proof | 0 | PASS for isolated baseline |
| Custom outbox pending | 26 | 0 | PASS |
| Custom outbox reconciliation | 26 | 0 | PASS |
| Earned projection error queue | 16 ready | 0 | PASS |
| Earned accounting error queue | 4 ready | 0 | PASS |
| Old target core Mongo orders `01018`-`01021` | Present before reset/recovery scope | 0 | PASS |
| Known old affiliate Mongo orders | Included stale `01021` | 0; only fresh `01022` remains | PASS |
| Known-order Redis keys | No authoritative reset count | 0 | PASS |
| RabbitMQ topology | Existing | Existing | PASS; no topology deletion |

# Master Data Preservation

| Resource | Final count | Result |
|---|---:|---|
| Affiliate categories | 33 | Preserved; canonical taxonomy startup verification passed |
| Carrier configurations | 1 | Preserved |
| Currencies | 1 | Preserved |
| Ledger accounts | 30 | Preserved |
| Products | 17 | Preserved |
| Product variants | 23 | Preserved |
| Store configurations | 1 | Preserved |
| System settings | 11 | Preserved |
| Tenant payment methods | 4 | Preserved |
| Tenants | 1 | Preserved |
| Users | 2 | Preserved |

Final integrity checks found zero missing affiliate-category parents, attribution authorities, commission authorities, refund orders, or shipment orders.

# Fresh Fixture

| Field | Value |
|---|---|
| Order number | `ORD-83B3-2026-01022` |
| Order ID | `0ef9b1db-d174-45fa-aab8-5c609e2b96b8` |
| Store tenant | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Affiliate account | `01a03e11-e234-7d40-b77e-99f9463c7a40` |
| Promotion link | `01a03e6b-3ff1-7c1b-9cf2-a340244c0f25` |
| Product/variant | Nokia N73, yellow/32, quantity 1 |
| Payment | COD |
| Item/subtotal | EGP 400 |
| Shipping | EGP 10 |
| Order total | EGP 410 |
| Commission snapshot | 10% of EGP 400 = EGP 40 |
| Projection mutation version | 14 |

# Lifecycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| Affiliate entry | Cart accepted the referral and returned affiliate account/promotion lineage | PASS |
| Checkout session | Guest checkout session created through direct HTTP | PASS |
| Shipping authority | Backend preview admitted the selected method and issued warehouse resolution/version | PASS |
| Pricing | Backend pricing used for the checkout request | PASS |
| Order creation | One COD Ecommerce order created with idempotency/request IDs | PASS |
| Fulfillment | Confirmed, packed, out for delivery | PASS |
| Failed delivery | Attempt 1 recorded with retry allowed | PASS |
| Retry | Retry-delivery transition accepted | PASS |
| Delivery | Delivered with reference and proof URL present on the order | PASS |
| COD collection | Operational collection produced the canonical posted COD journal | PASS |
| Commission approval | One approval entry for EGP 40 | PASS |
| Partial refund | EGP 200, refund journal created | PASS |
| Final refund | EGP 210, refund journal created; aggregate order status Refunded | PASS |
| Return/RTO | No distinct return endpoint scenario executed | NOT VERIFIED |
| POS Admin live visibility | No browser/authenticated UI session used | NOT VERIFIED |

The SQL `Shipments` record remains carrier status 0 with no carrier-delivered timestamp. This does not assert a carrier delivery: the certified path used the separate manual order-fulfillment lifecycle, whose order delivery reference/proof and fulfillment status are present. A real carrier path was intentionally not exercised.

# PostgreSQL Financial Reconciliation

| Authority | Final state |
|---|---|
| Order | Delivered operational status; Refunded payment status; Delivered fulfillment status; total EGP 410 |
| Order item | Paid EGP 400; refunded EGP 400 |
| Attribution | One row with matching store tenant, affiliate account, promotion link, order, and item lineage |
| Earned commission | 1 entry, EGP 40, 1 idempotency key |
| Approved commission | 1 entry, EGP 40, 1 idempotency key |
| Reversed commission | 2 append-only entries, EGP 40 total, 2 idempotency keys |
| Net commission | EGP 0 |
| Refunds | 2 rows and 2 lines; EGP 410 total order refund, EGP 400 item refund |
| Accounting outbox | 3 total, 0 pending, 0 failed |
| Journals | 1 posted `Order_COD`; 2 posted `Order_Refund` |

# Outbox Matrix

| Event type | Fresh produced | Processed | Pending | Reconciliation | Result |
|---|---:|---:|---:|---:|---|
| `InventoryReservedEvent` | 1 | 1 | 0 | 0 | PASS |
| `PaymentIntentCreatedEvent` | 1 | 1 | 0 | 0 | PASS |
| Custom outbox aggregate total | 2 | 2 | 0 | 0 | PASS |
| Accounting posts | 3 | 3 | 0 | 0 | PASS |

The normal host registered and ran the custom outbox worker and MassTransit consumers. Custom rows were marked processed only after publication; the final snapshot has no R-1 backlog and no fresh pending row.

# RabbitMQ Matrix

| Queue class | R-1 before | R-2 final | Result |
|---|---:|---:|---|
| `affiliate-commission-earned-projection_error` | 16 ready | 0 | PASS |
| `affiliate-commission-earned-accounting_error` | 4 ready | 0 | PASS |
| `affiliate-order-delivered_error` | Historical/disposable | 0 | PASS |
| Affiliate refund/order projection queues | Historical ready messages in R-1 | 0 relevant ready/unacked | PASS |
| Other inspected error queues | Not part of R-1 count | 0 | PASS |

Consumer count was positive while the bounded normal host ran. The final read-only inventory was taken after shutdown, so zero consumers in that snapshot is expected.

# Projection Matrix

| State | SQL canonical | Core Mongo `orders` | Mongo `affiliate_orders` | Semantic result |
|---|---|---|---|---|
| Identity | Order/tenant/number match | Match | Match | PASS |
| Order | Delivered | Delivered, event v14 | Delivered, order watermark v10 | PASS |
| Payment | Refunded | Refunded, payment v14 | Paid amount 0, payment v14 | PASS |
| Fulfillment | Delivered | Represented through order state | Delivered, fulfillment v10 | PASS |
| Affiliate identity | Account and promotion lineage present | Affiliate flag/account present | Account present | PASS |
| Commission gross | EGP 40 | `AffiliateCommissionAmount` null | EGP 40, earned v12 | PASS through dedicated affiliate projection |
| Commission approved | EGP 40 | Not the financial read model | EGP 40, approved v12 | PASS |
| Commission reversed | EGP 40 across 2 entries | Not the financial read model | EGP 40, reversed v14; item cumulative reversed EGP 40 | PASS |
| Commission net | EGP 0 | Not the financial read model | EGP 0 by gross/reversed components | PASS |
| Shipping | Manual fulfillment path; carrier shipment remains pending | `ShippingStatus=Pending` | Fulfillment Delivered | PASS for separate contracts; no carrier claim |

The dedicated `affiliate_orders` projection is the evidence for commission convergence. The core `orders` document did not populate its optional affiliate commission field or commission watermark; R-2 does not claim those fields as the affiliate financial authority.

# Duplicate and Replay Safety

| Replay | Result | Post-replay evidence |
|---|---|---|
| Duplicate COD collection | HTTP 400 rejected | One COD journal remains |
| Duplicate approval run | Processed 0 | One approval entry remains |
| Commission counts | Unchanged | Earned 1, Approved 1, Reversed 2 |
| Refund counts | Unchanged | 2 refunds totaling EGP 410 |
| Custom outbox | Unchanged/drained | 2 total, 0 pending |
| Accounting outbox | Unchanged/drained | 3 total, 0 pending, 0 failed |

# Health Matrix

| Endpoint/check | Actual | Classification |
|---|---|---|
| `/health/live` | HTTP 200, Healthy | PASS |
| `/health/ready` | HTTP 503, Unhealthy | Truthful environment block |
| `/health` | HTTP 503, Unhealthy | Truthful environment/shared-data block |
| PostgreSQL | Connected in direct final probe | PASS |
| RabbitMQ | Connected; queues inspected | PASS |
| MongoDB | Connected; fresh projections read | PASS for fresh path |
| Redis | Connected; final ping 4.026 ms | PASS |
| Elasticsearch | Green | PASS |
| Cloudflare R2 | Incomplete configuration | Environment blocker |
| DNS resolver | NXDOMAIN | Environment blocker |
| Certificate provider/Caddy | Disabled/degraded | Environment blocker |
| Fawry operational readiness | Unavailable/unconfigured | Environment blocker; not used by COD |
| Mongo projection drift | 56 shared Mongo-ahead documents reported by full health | Preserved/unknown shared-data drift |

Health behavior was not weakened to obtain a 200. The optional/provider failures do not invalidate the COD event-path result, but they prevent an unconditional deployment-readiness claim.

# Tests

| Gate | Result |
|---|---|
| Focused O-4.7/R-1/Affiliate/projection/settlement/COD/fulfillment/outbox/health/payment-authority gate | PASS 204/204 |
| Broad requested keyword sweep | INCOMPLETE/FAIL: terminated at 10 minutes with 137 observed failures and no final pass/fail/skip summary |

The broad failures are mixed and are not hidden or declared harmless:

- 51 observed source-string/certification substring assertion failures indicate stale artifacts or source-shape contracts.
- 11 observed CommerceHub fixture failures attempt to create marketplace orders with a payment state now blocked by lifecycle-authority guards.
- 58 log occurrences reference EF `ManyServiceProvidersCreatedWarning`; this warning becomes an exception and cascades after many independently constructed test providers.
- 3 storefront cart coupon tests fail because SQLite lacks PostgreSQL `btrim`.
- Additional failures include financial posting authority guards, missing Fawry checkpoint fixtures, CMS behavior, trust-engine data, and changed API/error expectations.

Because the command timed out, no total executed/passed/skipped count is claimed. The output is retained at `C:\Users\fadya\.local\share\opencode\tool-output\tool_0498e469c001KAJZkPtJTrr7er` for triage.

# Builds

| Build | Result |
|---|---|
| `dotnet build PointOfSale.sln --no-restore` | PASS; 0 warnings, 0 errors |
| `npm run build` in `PointOfSale.Storefront` | PASS; existing dynamic/static import and >500 kB chunk warnings |
| `npm run build` in `PointOfSale.Web` | PASS; existing >5,000 kB main-chunk warning |

# Worktree and Forensic Review

- Final tracked diff before this report: 138 files, 7,164 insertions, 1,510 deletions, plus existing untracked O-4.x/R-1 files.
- `git diff --check` returned no whitespace error; only LF-to-CRLF conversion warnings.
- No existing worktree change was reverted or reformatted.
- No migration or package was added by R-2.
- R-2 production-code delta: none.
- Transaction ownership remains PostgreSQL for order/payment/commission/accounting truth and Mongo for derivative read models.
- Tenant/order identities agree for the fresh SQL, core Mongo, affiliate Mongo, and attribution records.
- Financial facts are append-only and event/idempotency fenced; duplicate probes did not change counts.
- Retry/error isolation is demonstrated by drained custom/accounting outboxes and zero relevant RabbitMQ error messages.
- Logs and evidence use bounded IDs/counts and omit secrets and message bodies.

# Release Blocker Matrix

| Issue | Severity/class | Production defect proven? | R-2 status | Whole-release blocking? |
|---|---|---|---|---|
| R-1 custom outbox backlog | Prior P1 | Yes historically | Cleared; fresh rows processed | No for R-2 path |
| R-1 earned projection code 40/backlog | Prior P1 | Yes historically | Queues empty; fresh gross/reversal converge | No for R-2 path |
| Broad regression failures/time limit | Test/repository debt, mixed | Undetermined/mixed | 137 failures observed; no summary | Yes for whole-repository green claim |
| Readiness HTTP 503 | Environment/configuration | No COD-path defect proven | Truthful and unresolved | Yes for target environment readiness |
| 56 shared Mongo-ahead documents | Shared-data drift | Undetermined | Preserved, not repaired | Requires owner decision before broad projection claim |
| Core Mongo optional commission field null | Projection contract gap | Not proven | Dedicated affiliate projection converged | Follow-up contract clarification |
| Carrier shipment remains pending | Scenario boundary | No | Manual fulfillment only | Blocks real-carrier certification only |
| Return/RTO live path | Verification gap | No | NOT VERIFIED | Blocks return certification only |
| POS Admin live visibility | Verification gap | No | NOT VERIFIED | Blocks UI visibility certification only |
| Real payout/provider/carrier actions | Safety exclusion | No | Not executed | Expected exclusion |

# Remaining Risks

1. The broad suite needs fixture modernization and source-assertion retirement before a whole-repository release can be called green.
2. The target environment needs explicit decisions/configuration for R2 storage, DNS, certificates/Caddy, Fawry, and shared Mongo drift before readiness can become healthy.
3. Core order and dedicated affiliate projection ownership for commission presentation should be documented so a null core commission field cannot be misread as financial loss.
4. Return/RTO, real-carrier delivery, and authenticated POS Admin visibility require separate disposable fixtures and certification.
5. At-least-once event publication still depends on every financial/projection consumer preserving event-ID and natural-key idempotency.

# Certification Artifacts

- `.tmp/r2-probe/Program.cs`: read-only inventory and fresh-order convergence probe; its queue-purge mode was narrowly allow-listed and topology-preserving.
- `.tmp/r2-fresh-checkout.ps1`: direct Affiliate COD checkout fixture.
- `.tmp/r2-fulfillment.ps1`: confirm/pack/out-for-delivery/failed/retry/delivered transitions.
- `.tmp/r2-cod-collect.ps1`: COD collection.
- `.tmp/r2-refund.ps1`: partial and final refunds.
- `.tmp/r2-cod-replay.ps1`: duplicate COD rejection.
- `.tmp/r2-approve-replay.ps1`: duplicate approval no-op.
- `.tmp/r2-runtime-cert.ps1`: bounded normal host, health, replay, and final inventory harness.
- `.tmp/r2-cert-api-out.log`: normal-host startup, consumer, index, worker, and health evidence.
- `docs/R1_OUTBOX_DRAIN_PROJECTION_RECOVERY_CERTIFICATION_2026-08-28.md`: signed before-state.

# Final Verdict

## CONDITIONAL PASS

R-2 certifies the scoped Development reset and one fresh Affiliate COD lifecycle: preserved master data remains intact, historical R-1 backlog is absent, fresh custom and accounting outboxes are drained, relevant RabbitMQ queues are empty, PostgreSQL financial truth is internally consistent, dedicated Mongo affiliate financial fields converge, and duplicate actions do not duplicate money.

The condition is explicit: this result must not be promoted to whole-repository or target-environment release approval until the broad regression failures are triaged and deployment readiness/shared Mongo drift are resolved. Return, real-carrier, and authenticated POS Admin scenarios remain NOT VERIFIED.
