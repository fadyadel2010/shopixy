# R-1 Outbox Drain & Projection Recovery Certification

Date: 2026-08-28  
Scope: post O-4.7 recovery certification  
Method: Codex direct only; direct HTTP and read-only PostgreSQL, MongoDB, RabbitMQ, Redis, and Elasticsearch inspection; no browser automation

# Executive Summary

## Final verdict: FAIL

PostgreSQL remains internally consistent and authoritative for `ORD-83B3-2026-01021`, the O-4.7 source fixes compile, and the focused recovery gates are green. The release nevertheless cannot be certified because runtime convergence has not occurred:

- The custom outbox still contains 26 pending rows: 22 non-protected and 4 protected.
- Every pending row is now at retry count 5 and operator reconciliation, with no `ProcessedAtUtc`.
- The current source can deserialize the persisted camelCase payloads, but the live rows were last attempted by code that reported their payload `TenantId` as missing even though read-only JSON/column inspection proves it is present and consistent.
- The normal host cannot be started during this certification because `OutboxPublisherJob` has no supported protected-order exclusion and would automatically process all four protected rows.
- RabbitMQ still retains 16 `AffiliateCommissionEarnedEvent` projection failures, including 11 for `01021`. All 16 projection-queue failures are the diagnosed Mongo code 40 conflict.
- The fixed repository contains no supported error-queue redrive operation that can replay the original retained messages through a consumer-only host while guaranteeing that the custom outbox publisher remains stopped.
- Mongo therefore remains stale for `01021`: order/fulfillment lifecycle fields advanced, but commission gross and reversal remain zero.
- A second P1 financial-projection defect was found and fixed in source: item-level partial reversals were represented by a boolean and distinct reversals could be discarded by an order-level version fence. The projection now accumulates per-item reversal amounts, uses event-ID idempotency and an optimistic amount fence, and retains missing/out-of-order prerequisites for retry.

No database row, Mongo document, RabbitMQ message, protected order, Redis keyspace, financial rail, or carrier state was mutated by this certification.

# Initial Certification FAIL Cause

O-4.7 found an unregistered one-shot custom outbox processor and a Mongo earned-projection update that wrote `orderNumber` and `affiliateAccountId` through both `$set` and `$setOnInsert`. That left the checkout outbox stalled and earned events quarantined with Mongo code 40.

At R-1 baseline, before any runtime action here, the previously reported zero-attempt rows had already moved to retry count 5 at approximately `2026-08-28 12:52:25–26Z`. No normal host was started by this certification. The stored last error came from the old case-sensitive payload path; current source uses web JSON defaults and the persisted payload metadata is valid.

# Baseline

- Initial worktree: 132 tracked modified files and 36 untracked entries.
- Initial tracked diff: 132 files, 6,990 insertions, 1,474 deletions.
- Final tracked diff after the existing O-4.7 work plus R-1 hardening: 135 files, 7,092 insertions, 1,500 deletions; 36 untracked entries before this report.
- `git diff --check`: exit 0; only existing LF-to-CRLF conversion warnings.
- Unrelated dirty-worktree changes were preserved.
- Existing O-4.7 report: `docs/O47_POST_INTEGRATION_GAP_CLOSURE_2026-08-28.md`.

# Runtime Preflight

| Component | Configured endpoint | Direct forensic result | Safe HTTP host result | Classification |
|---|---|---|---|---|
| API | `localhost:5010` HTTP | Initially not running | Test host served HTTP; then stopped | Available only during bounded test-host check |
| PostgreSQL | `192.168.1.5:5432` | Connected; session forced read-only | Health process reported unavailable because its child process was socket-sandboxed | Directly healthy; HTTP-host result environment-limited |
| RabbitMQ | `192.168.1.5:5672` plus management API | Management 200; queues inspected with requeue semantics | MassTransit intentionally not started; health degraded/unhealthy | Broker reachable; bus unavailable by safe-host design |
| MongoDB | `192.168.109.128:27017` | Ping OK; documents read | Health process socket-sandboxed | Directly healthy; projection stale |
| Redis | `192.168.1.5:6379` | Ping OK, 11.2 ms final snapshot | Health process socket-sandboxed | Directly healthy |
| Elasticsearch | `192.168.1.5:9200` | HTTP 200 | Health process socket-sandboxed | Directly healthy |

The API was launched only with `REQUIRE_REAL_POSTGRES_TESTS=1`. Repository startup logic removes all `IHostedService` registrations in this mode, so neither the custom outbox publisher nor MassTransit consumers ran.

# Outbox Inventory

All rows have tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`, `ProcessedAtUtc=NULL`, retry count 5, `RequiresOperatorReconciliation=true`, and a last error stating that the payload `TenantId` is missing. Payload inspection proves the camelCase `tenantId`, aggregate/order ID, UTC occurrence, event ID, and event version are present and agree with the outbox columns.

| Order | Message ID | Event | Created UTC | Status | Attempts | Last error UTC | Protected? |
|---|---|---|---|---|---:|---|---|
| 01005 | `35211ce3-0047-0e60-f5f0-0f1335649e78` | InventoryReserved v1 | 2026-08-13 14:58:35 | Pending/reconcile | 5 | 12:52:26 | No |
| 01005 | `c98836dd-ab5f-4c62-e248-ac6a92c1174f` | PaymentIntentCreated v2 | 2026-08-13 14:58:35 | Pending/reconcile | 5 | 12:52:26 | No |
| 01006 | `364a5ffb-e42b-0242-cc4b-ad8f2cc87c52` | InventoryReserved v1 | 2026-08-13 15:10:56 | Pending/reconcile | 5 | 12:52:25 | No |
| 01006 | `8fb4c56a-55da-d4f8-35ac-fee522a2f8aa` | PaymentIntentCreated v2 | 2026-08-13 15:10:56 | Pending/reconcile | 5 | 12:52:25 | No |
| 01007 | `2b7e6f21-52b1-b9dc-be06-d363a9f1f189` | InventoryReserved v1 | 2026-08-13 15:58:00 | Pending/reconcile | 5 | 12:52:26 | No |
| 01007 | `32a45daa-4a43-0190-dc27-1b9ca6b9fbe7` | PaymentIntentCreated v2 | 2026-08-13 15:58:01 | Pending/reconcile | 5 | 12:52:26 | No |
| 01008 | `284e983a-5169-dfff-1894-225744804d18` | InventoryReserved v1 | 2026-08-13 16:50:45 | Pending/reconcile | 5 | 12:52:25 | No |
| 01008 | `396e0476-e8eb-c6cf-6b8d-a289758ba9bb` | PaymentIntentCreated v2 | 2026-08-13 16:50:45 | Pending/reconcile | 5 | 12:52:25 | No |
| 01009 | `48e5cc7b-0c41-ecf2-f800-d235e8cf1953` | InventoryReserved v1 | 2026-08-13 17:05:02 | Pending/reconcile | 5 | 12:52:25 | No |
| 01009 | `1ea1c427-db1b-70d8-9900-655a7b7a6557` | PaymentIntentCreated v2 | 2026-08-13 17:05:02 | Pending/reconcile | 5 | 12:52:25 | No |
| 01010 | `fd55541c-cfff-a339-0976-f2aabb9808f8` | InventoryReserved v1 | 2026-08-13 18:39:41 | Pending/reconcile | 5 | 12:52:25 | No |
| 01011 | `accb35fe-16b8-5e4b-834d-f3b7c04f8ce0` | InventoryReserved v1 | 2026-08-13 22:16:40 | Pending/reconcile | 5 | 12:52:25 | No |
| 01012 | `b6fcb610-eee0-a3ee-9de9-073e44fe8d55` | InventoryReserved v1 | 2026-08-13 22:26:29 | Pending/reconcile | 5 | 12:52:25 | No |
| 01013 | `27b545dc-e0af-aa4a-ac18-eca9b3991106` | InventoryReserved v1 | 2026-08-13 22:38:20 | Pending/reconcile | 5 | 12:52:25 | No |
| 01014 | `ca7229d1-a431-3c96-8b09-9e5790796fdd` | InventoryReserved v1 | 2026-08-14 13:51:52 | Pending/reconcile | 5 | 12:52:25 | No |
| 01015 | `5e559b03-3cc9-72fd-9d9f-94f1654d4ed9` | InventoryReserved v1 | 2026-08-14 18:48:29 | Pending/reconcile | 5 | 12:52:25 | No |
| 01016 | `2aa15803-c704-b69d-5d26-4e500ca05b4a` | InventoryReserved v1 | 2026-08-14 19:00:40 | Pending/reconcile | 5 | 12:52:25 | No |
| 01017 | `c2d1453e-ff98-49f1-f3e1-912306f1f492` | InventoryReserved v1 | 2026-08-16 17:05:10 | Pending/reconcile | 5 | 12:52:25 | No |
| 01018 | `c2d3a427-dc1a-f36a-9eb5-e86af6daf47d` | InventoryReserved v1 | 2026-08-26 14:15:22 | Safety blocked | 5 | 12:52:25 | Yes |
| 01018 | `d45bff36-597c-03cb-32e6-ce6b362f241b` | PaymentIntentCreated v2 | 2026-08-26 14:15:22 | Safety blocked | 5 | 12:52:25 | Yes |
| 01019 | `df6da62b-a454-2341-ae39-0aa5893af924` | InventoryReserved v1 | 2026-08-26 16:50:14 | Safety blocked | 5 | 12:52:26 | Yes |
| 01019 | `d3d83e10-c4b5-4a43-bda0-34686d63c268` | PaymentIntentCreated v2 | 2026-08-26 16:50:14 | Safety blocked | 5 | 12:52:26 | Yes |
| 01020 | `20e10cf7-cc6f-7400-5496-66ef61e4043e` | InventoryReserved v1 | 2026-08-27 19:21:16 | Pending/reconcile | 5 | 12:52:25 | No |
| 01020 | `636aafa9-2a10-9678-14b9-faa333a501b0` | PaymentIntentCreated v2 | 2026-08-27 19:21:16 | Pending/reconcile | 5 | 12:52:26 | No |
| 01021 | `643ed4e6-f175-94dc-d5d9-6ba29822fbcb` | InventoryReserved v1 | 2026-08-27 19:30:54 | Pending/reconcile | 5 | 12:52:25 | No |
| 01021 | `0ca00bd1-1ada-2727-422d-cd57dc397034` | PaymentIntentCreated v2 | 2026-08-27 19:30:54 | Pending/reconcile | 5 | 12:52:25 | No |

Aggregate counts: 17 order aggregates. Orders 01005–01009 and 01018–01021 have two events each; 01010–01017 have one each. Event counts are 17 `InventoryReservedEvent` and 9 `PaymentIntentCreatedEvent`.

# Pending Row Classification

| Event type | Expected | Producer | In-repository consumer | Routing/destination | Tenant/aggregate | Automatic safety |
|---|---|---|---|---|---|---|
| InventoryReservedEvent | Yes; durable checkout integration fact | `CheckoutService` | None discovered | MassTransit published event contract; no explicit routing key; intended bus/external subscriber fanout | Store tenant / order | Safe for non-protected rows in normal operation, but no supported per-order exclusion exists |
| PaymentIntentCreatedEvent | Yes, including COD compatibility fact | `CheckoutService` and payment services | None discovered | MassTransit published event contract; no explicit routing key; intended bus/external subscriber fanout | Store tenant / order | Same constraint |

The lack of an in-repository consumer does not make these rows disposable. They are supported integration facts and must not be deleted merely because they are old.

# Outbox Root Cause

There are two historical layers:

1. Zero attempts: the processor existed but was not registered as a hosted service.
2. Five failed attempts: a subsequently executed stale build used case-sensitive deserialization against camelCase JSON and concluded that `TenantId` was absent. The current source uses `JsonSerializerOptions(JsonSerializerDefaults.Web)` and deterministic publication tests prove the persisted shape is accepted.

The rows are valid. They are not poison payloads. Their current operator-reconciliation state is a deployment/runtime recovery issue.

# Outbox Worker Verification

- Registration: `AddHostedService<OutboxPublisherJob>()` is present.
- Execution: continuous `BackgroundService`, five-second polling interval, cancellation-aware delay.
- Batch: up to 100 candidates.
- Locking/concurrency: serializable per-message transaction plus PostgreSQL row lock and `xmin` concurrency checks.
- Ordering: older pending events for the same aggregate block newer events; unrelated aggregates continue independently.
- Acknowledgement: `ProcessedAtUtc` is set only after `IPublishEndpoint.Publish` succeeds.
- Duplicate publication: the design is at-least-once. A crash after broker publication but before SQL commit can republish; downstream event-ID idempotency is therefore mandatory.
- Transient failure: retry count and error metadata are persisted.
- Critical poison: after five failures the row requires operator reconciliation and enters slow replay; it remains unprocessed and blocks only its aggregate stream.
- Non-critical terminal policy: may be marked terminal after configured retry exhaustion; not applicable to these authoritative checkout events.
- Concurrent hosts: may select the same candidate list, but the row lock and recheck serialize final processing.

No protected-order exclusion/filter exists. Starting the normal host would violate the certification safety rule, so no live drain was attempted and no artificial filter was invented.

# RabbitMQ Retained Event Analysis

| Queue | Ready | Unacked | Relevant orders | Failure classification |
|---|---:|---:|---|---|
| `affiliate-commission-earned-projection_error` | 16 | 0 | 01020: 5; 01021: 11 | All 16 are Mongo code 40: conflicting `orderNumber` update path |
| `affiliate-commission-earned-accounting_error` | 4 | 0 | 01021: 1; three older unrelated events | 01021 is an idempotency-key/different-posting conflict; other reasons are missing COA, insufficient funds, and missing tenant |

For `01021`, the projection-error Event IDs are:

`8b04d837-0485-49bc-9643-c87311987f12`, `8fe1763d-418c-40f8-be93-ceff7c868e15`, `1d6b07b0-ea1f-42a0-aa29-395b833b117d`, `b8c2e9d5-c070-4f17-a4c5-f66dd66de443`, `caa04d09-4215-496b-897a-1bc0b695cd90`, `017970d2-fa89-4887-b2cd-6080979006b9`, `3feb8629-017f-4fc1-93f3-8a76a692fcef`, `1a1e59de-6177-4d99-8ec1-021c44b42ecf`, `79e9c57e-09d3-4b1b-8daf-c1442591253d`, `47e40af6-a8fc-4d6e-b1dd-f0bfb4776d31`, and `f38af23c-3fd4-4a8c-9e27-36aca11b8bf4`.

The last Event ID is also present in the accounting error queue. PostgreSQL proves the natural-key affiliate-earned journal already exists and is posted, so the accounting consumer is preventing a different duplicate posting rather than losing the canonical posting.

Inspection used RabbitMQ management `ack_requeue_true`. Counts were unchanged after inspection; no message was acknowledged, deleted, moved, or reconstructed.

# Safe Replay Plan

Repository search found no supported error-queue redrive endpoint, command, or consumer-only recovery host. Therefore:

**REPLAY NOT VERIFIED.**

Required operational action:

1. Deploy the current fixed consumer and publisher source.
2. Add or use an approved MassTransit/RabbitMQ operational redrive that moves the original retained message—not a reconstructed payload—to the original input queue.
3. Run a consumer-only host profile that cannot start `OutboxPublisherJob`, or obtain explicit permission for the protected orders before using the normal host.
4. Redrive earned-projection messages before allowing queued refund/reversal facts for those orders to progress.
5. Capture queue counts and SQL/Mongo snapshots before and after.
6. Verify duplicate Event IDs are no-ops and distinct reversal Event IDs all accumulate.

Manual RabbitMQ publication, acknowledgement, database repair, and synthetic financial events remain prohibited.

# Mongo Projection Root Cause

The `affiliate_orders` document exists because attribution and lifecycle projection paths succeeded. Earned projection never completed because its upsert rendered the same fields under both `$setOnInsert` and `$set`, which Mongo rejects with code 40. Consequently, later lifecycle events could update order/payment/fulfillment watermarks but could not supply commission items or totals.

The O-4.7 correction extracts a rendered update whose `$set` and `$setOnInsert` path sets are disjoint. Deterministic rendering and projection tests pass.

# Projection Recovery

Runtime recovery was not performed because there is no supported safe redrive path. Mongo remains unchanged.

R-1 also fixed two P1 reversal projection risks:

- Added cumulative `reversedAmount` per projected item instead of treating the first partial reversal as a full boolean reversal.
- Removed the order-level reversal-version rejection from the item-level additive financial path. Distinct Event IDs can now apply even when they share an aggregate version or arrive out of order.
- Retained event-ID duplicate protection.
- Added an optimistic fence on the expected per-item reversed amount; concurrent writers cause retry instead of lost updates.
- Missing earned order/item, non-positive amount, and over-reversal now throw, so the message is retained rather than acknowledged as a successful no-op.
- Watermarks use Mongo `$max` to avoid regression.
- The consumer remains projection-only and has no PostgreSQL dependency.

# ORD-83B3-2026-01021 Reconciliation

## SQL

| Authority | State |
|---|---|
| Order | Delivered; total EGP 400 |
| Payment | Refunded |
| Fulfillment | Delivered |
| Order item | Paid 400; refunded 400 |
| Snapshot | Rate 10%; base 400; commission 40 |
| Commission gross | Earned 40 |
| Commission approved | 40 |
| Commission reversed | 4 + 36 = 40 |
| Commission net | 0 |
| Refunds | 40 partial + 360 final remainder |
| Accounting | Posted COD journal, posted affiliate-earned journal, and two posted refund journals |

## Mongo

| Projection field | Actual |
|---|---|
| Order state | Delivered |
| Payment projection | Paid amount 0 after refund; last payment watermark v11 |
| Fulfillment | Delivered |
| Affiliate projection state | `AttributedPendingPayment` |
| Items | Empty |
| Commission gross | 0 |
| Approved | 0 |
| Reversal | 0 |
| Net presentation | 0 because both gross and reversal are absent, not because recovery converged |

## Events and outbox

- Produced: canonical earned/approved/two reversal ledger facts exist; lifecycle watermarks prove other events processed.
- Failed/retained: 11 original earned projection messages for 01021; one duplicate/conflicting accounting posting message.
- Custom checkout outbox: two pending rows, both retry 5, neither processed.
- Additional queues: `order-item-refunded-affiliate` and `order-refunded-affiliate` each have two ready messages globally; their bodies were not acknowledged or attributed during this certification.

Semantic convergence is **FAIL**. Mongo's zero net happens to equal SQL net, but gross and reversal are both wrong and projection-specific omission does not excuse stale values.

# Protected Orders

`ORD-83B3-2026-01018` and `ORD-83B3-2026-01019` were not mutated. Their four custom outbox rows remain pending. No associated message was acknowledged, replayed, deleted, edited, or marked processed.

# Checkout Outbox Semantics

- `InventoryReservedEvent` is expected after durable SQL inventory reservation.
- `PaymentIntentCreatedEvent` is an expected compatibility event for COD as well as online methods.
- New COD events explicitly set `IsOfflineCollection=true`; this does not assert provider capture or revenue recognition.
- Zero-attempt historical rows represented an unstarted worker, not invalid business facts.
- Current retry-five failures represent stale deserialization/runtime code, not missing tenant metadata.
- These events should naturally drain after safe deployment/recovery; they must not be deleted as old data.

# Consumer Failure Isolation

MassTransit uses separate receive endpoints/error queues. A projection poison message is retried and then moved to its consumer-specific error queue, so it does not block unrelated order, payment, fulfillment, or other-tenant queues. The custom outbox also processes messages individually; aggregate ordering blocks only later events for the same aggregate. Existing broker backlog in many unrelated queues remains an operational risk but was not changed here.

# Tenant Isolation

All inspected outbox and 01021 SQL/Mongo records agree on store tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`. Projection reads and writes fence by tenant plus order ID, and authority validation rejects conflicting `TenantId`/`StoreTenantId`.

Live second-tenant verification is **NOT VERIFIED**: the safe inventory found one distinct order tenant, one affiliate account, and one commission store tenant. No tenant or credentials were fabricated. Existing deterministic affiliate auth/session/tenant gates from O-4.7 remain green.

# AffiliateOnly Verification

**NOT VERIFIED.** One affiliate account exists, but no safe existing AffiliateOnly credential set was available. No credential was created and no browser automation was used.

# Stale Version

Live HTTP stale-concurrency mutation is **NOT VERIFIED** because no supported disposable authenticated fixture was available. Deterministic source/tests cover 409 concurrency behavior; no claim of live evidence is made.

# Historical Commission Integrity

SQL 01021 proves historical commission remains tied to the order-item snapshots: rate 10%, base 400, earned 40 despite later refund/current product state. The focused affiliate lifecycle, recovery, pricing snapshot, settlement, and payout suites pass 113/113. No live product price or policy was changed.

# Failed Delivery

Live scenario: **NOT VERIFIED**. No safe disposable fixture was available. No order/carrier mutation was attempted. COD and lifecycle deterministic gates pass, while the pre-existing broad delivery/return sweep still contains stale fixtures and missing historical artifacts described under Tests.

# Return

Live pre-delivery return-to-origin and post-delivery return: **NOT VERIFIED**. No fabricated live evidence.

# Payout

Live payout: **NOT VERIFIED**. No real payout was executed. Deterministic settlement/payout tests in the 113-test financial gate pass and use net-of-reversal amounts.

# Swagger

`GET /swagger/v1/swagger.json` returned HTTP 200, OpenAPI 3.0.1, 910 paths, and 2,778,901 bytes. JSON parsing succeeded and no stack trace was returned.

# Health

The safe host returned:

- `/health/live`: 200, Healthy.
- `/health/ready`: 503, Unhealthy.
- `/health`: 503, Unhealthy.

The child API process was intentionally prevented from running hosted services and was socket-sandboxed, so dependency failures in that process are not substituted for direct reachability evidence. Direct read-only probes independently proved PostgreSQL, RabbitMQ management, MongoDB, Redis, and Elasticsearch reachable.

Required/operational blockers remained visible: MassTransit not started in safe mode, integration/outbox checks unavailable or unhealthy, R2 not configured, DNS/certificate renewal unavailable, and Paymob/Stripe/Fawry operational readiness unhealthy. Optional disabled certificate provider and Caddy checks were degraded. Fawry foundation was healthy but the provider was disabled/unconfigured; manual payment checks were healthy.

# Security

- Raw referral-code structured logging remains removed.
- No token, provider secret, payment credential, webhook signature, or referral secret was printed.
- RabbitMQ bodies were not reproduced in the report; only operational IDs and failure reasons are listed.
- Financial logging remains limited to identifiers, amount/currency, and count-level diagnostics rather than full event payloads.
- Read-only SQL explicitly enabled `default_transaction_read_only`.
- No global Redis flush or destructive command was used.

# Production Fix Table

| Fix | Severity | Root cause | Production files | Regression | Runtime verified |
|---|---:|---|---|---|---|
| Earned Mongo code 40 fix (O-4.7) | P1 | Same paths emitted in `$set` and `$setOnInsert` | `AffiliateProjectionConsumers.cs` | Rendered path sets disjoint; projection suite | No; retained original messages not redriven |
| Continuous custom outbox worker (O-4.7) | P1 | Processor not hosted | `OutboxPublisherJob.cs`, `Program.cs` | Persisted publish/processed/retry/registration gates | In-memory yes; live drain no |
| Integration outbox readiness (O-4.7) | P1 | Custom backlog absent from readiness | Health check and registration | Stale zero-attempt returns unhealthy | HTTP 503 truthful; component query socket-limited |
| Partial reversal accumulation (R-1) | P1 | Boolean marked first partial reversal as fully reversed | `AffiliateProjectionConsumers.cs`, `AffiliateDocuments.cs` | 4 then 36 produces cumulative 40/full reversed | Source/deterministic only |
| Distinct reversal retention (R-1) | P1 | Order-level version fence could discard distinct item-level money events | `AffiliateProjectionConsumers.cs` | Same-version distinct-event filter regression | Source/deterministic only |

# Final Production Forensic Review

R-1 production changes were limited to the affiliate projection consumer and Mongo affiliate document model; the existing O-4.7 production changes were reviewed through their focused diffs and gates.

| Concern | Review result |
|---|---|
| Transaction boundary | Each affiliate order update is atomic in Mongo; dashboard/summary are independently idempotent and recomputed from order documents after partial failures |
| Event ownership | Consumer remains event-driven; no SQL authority was introduced into the projection process |
| Tenant | Authority metadata is validated; all order/item updates fence tenant + order (+ item) |
| Concurrency | Expected cumulative reversal amount is an optimistic fence; lost races throw for retry |
| Idempotency | Event IDs are stored with the same atomic financial update; duplicates recompute and no-op |
| Retry/error handling | Missing prerequisite, invalid amount, over-reversal, and lost fence throw instead of acknowledging |
| Ordering | Distinct financial facts are not discarded only because their aggregate version is equal/lower; watermarks advance via `$max` |
| Security/logging | No referral secret; operational IDs and bounded financial amount diagnostics only |
| Backward compatibility | Missing `reversedAmount` deserializes as decimal zero; no relational migration or package change required |
| Performance | One point read and at most one retry read per reversal; dashboard/summary recomputation already existed and is bounded by affiliate order rows |

# Tests

## New R-1 regressions

1. `ReversalProjection_MustAccumulatePartialThenFullAmounts`
2. `ReversalProjection_MustNotDiscardDistinctEventsAtTheSameAggregateVersion`

## Current focused results

| Gate | Result |
|---|---|
| O47/R-1 gap closure regression class | PASS 8/8 |
| Affiliate projection class | PASS 16/16 |
| Affiliate event pipeline, lifecycle recovery, pricing snapshot, settlement, payout | PASS 113/113 |
| Outbox consistency, projection/payment-cancellation health, O47/R-1 | PASS 49/49 |
| COD lifecycle/webhook/orchestrator/admission | PASS 57/57 |
| Paymob reservation guard + Fawry payment authority | PASS 5/5 |
| Financial projection durable IDs/version/counter source guards | PASS 2/2 |
| Solution build | PASS |
| Storefront production build | PASS with existing dynamic-import and >500 kB chunk warnings |
| Admin Web production build | PASS with existing >5,000 kB chunk warning |

## Broad historical sweep

A deliberately broad combined filter returned 214 passed and 41 failed out of 255. Failures were not hidden. They are pre-existing historical architecture debt: tests reading deleted migration artifacts, stale source-string assertions, unrelated CommerceHub/payout/CMS expectations, and one Paymob frontend source assertion that now conflicts with the intentional Fawry checkout-token admission guard. The R-1-specific projection/outbox gates above remain green. These broad failures must be repaired or retired before a whole-repository release claim.

# Builds

- `dotnet build PointOfSale.sln --no-restore`: PASS, 0 errors, 0 warnings on the final incremental build.
- `npm run build` in `PointOfSale.Storefront`: PASS; existing module chunking and large-chunk warnings.
- `npm run build` in `PointOfSale.Web`: PASS; existing large main-chunk warning.
- Migrations added by R-1: none.
- Package changes added by R-1: none.

# Runtime Evidence

Final read-only snapshot after all checks:

- PostgreSQL read-only ping: OK.
- Pending custom outbox: 26.
- Protected pending: 4.
- Non-protected pending: 22.
- MongoDB ping: OK; 01021 commission projection unchanged.
- Redis ping: OK.
- Elasticsearch: HTTP 200.
- RabbitMQ management: HTTP 200.
- Earned projection error queue: 16 ready, 0 unacked.
- Earned accounting error queue: 4 ready, 0 unacked.
- Safe API test process: stopped after HTTP evidence.

# Outbox Matrix

| Event Type | Pending Before | Processed | Failed/reconcile | Protected | Pending After | Status |
|---|---:|---:|---:|---:|---:|---|
| OrderCreated | 0 | 0 | 0 | 0 | 0 | None |
| InventoryReserved | 17 | 0 | 17 | 2 | 17 | FAIL — valid rows pending |
| PaymentIntentCreated | 9 | 0 | 9 | 2 | 9 | FAIL — valid rows pending |
| AffiliateAttribution | 0 | 0 | 0 | 0 | 0 | None in custom outbox |
| CODCollected | 0 | 0 | 0 | 0 | 0 | None in custom outbox |
| AccountingPosted | 0 | 0 | 0 | 0 | 0 | None in custom outbox |
| CommissionEarned | 0 | 0 | 0 | 0 | 0 | 16 retained Rabbit projection errors tracked separately |
| CommissionApproved | 0 | 0 | 0 | 0 | 0 | None in custom outbox |
| Refund | 0 | 0 | 0 | 0 | 0 | Rabbit refund queues have unrelated ready backlog |
| CommissionReversal | 0 | 0 | 0 | 0 | 0 | None in custom outbox |

# Projection Matrix

| State | SQL Canonical | Mongo Before | Recovery | Mongo After | Semantic Match |
|---|---|---|---|---|---|
| Order | Delivered | Delivered | Not replayed | Delivered | Yes for exposed order state |
| Payment | Refunded | Paid amount 0, payment watermark v11 | Not replayed | Unchanged | Partial/contract-specific |
| Fulfillment | Delivered | Delivered | Not replayed | Delivered | Yes |
| Commission Gross | 40 | 0 | Not replayed | 0 | No |
| Commission Reversal | 40 | 0 | Not replayed | 0 | No |
| Commission Net | 0 | Apparent 0 from absent gross/reversal | Not replayed | Same | No; components are stale |

# Health Matrix

| Health | Expected | Actual | Status | Reason |
|---|---|---|---|---|
| Liveness | 200 | 200 Healthy | PASS | Process served requests |
| Readiness | 503 while blockers remain | 503 Unhealthy | PASS/truthful | Safe-host bus stopped; outbox/config/dependency blockers |
| Full Health | 503 | 503 Unhealthy | PASS/truthful | Multiple real and safe-host-specific blockers |
| Integration Outbox | Unhealthy with 26 pending/reconcile rows | Component query unavailable in socket-sandboxed host; independent DB evidence 26 | FAIL operationally | Backlog unresolved |
| Mongo Projection | Gross/reversal converge to SQL | Gross 0, reversal 0 | FAIL | Earned messages retained |
| RabbitMQ | Reachable; retained counts stable | Management 200; 16 projection errors, 0 unacked | Reachable, recovery FAIL | Broker healthy; messages not redriven |
| PostgreSQL | Reachable/canonical | Direct read-only OK | PASS | Canonical state consistent |
| Redis | Reachable | Direct ping OK, 11.2 ms | PASS | Safe-host socket result excluded |
| Elasticsearch | Reachable | Direct HTTP 200 | PASS | Safe-host socket result excluded |
| R2 | Configured if required | Unhealthy, not fully configured | Environment blocker | Do not weaken check |
| Fawry | Honest disabled/unconfigured state | Foundation healthy; operational unhealthy | Environment blocker | Provider disabled and credentials/callback absent |

# Security Matrix

| Area | Result |
|---|---|
| Tenant isolation | Source/deterministic PASS; live second tenant NOT VERIFIED |
| Protected-order safety | PASS; four rows untouched |
| RabbitMQ replay safety | PASS for non-action; replay itself NOT VERIFIED |
| Outbox safety | No direct updates; live drain blocked safely |
| Mongo tenant safety | PASS source; tenant + order/item filters |
| Referral logging | PASS; raw referral code absent |
| Provider secrets | PASS; none exposed |
| Financial data exposure | PASS with bounded totals/IDs only; no full payloads |
| Auth tokens | PASS; none exposed or created |

# Release Blocker Matrix

| Issue | Severity | Production Defect? | Fixed? | Runtime Verified? | Release Blocking? | Reason |
|---|---:|---|---|---|---|---|
| Valid custom outbox rows failed under stale deserializer/runtime | P1 | Yes | Current source yes | No live drain | Yes | 22 non-protected valid rows still pending; 4 additional protected rows intentionally untouched |
| No certification-safe normal-host exclusion | Safety constraint | Not inherently; certification operational gap | No change allowed | N/A | Blocks this certification drain | Normal host would process protected rows |
| Earned Mongo code 40 | P1 | Yes | Source yes | No redrive | Yes | 01021 remains stale and 16 original messages retained |
| No repository-supported original-message redrive/consumer-only recovery path | P1 operational | Yes for recoverability | No | No | Yes | Valid retained events cannot yet recover through a supported path |
| Partial reversal boolean loss | P1 | Yes | Source yes | Deterministic only | Yes until deployed/replayed | 4 + 36 could otherwise project only the first reversal/full flag |
| Distinct reversal events rejected by order-level version fence | P1 | Yes | Source yes | Deterministic only | Yes until deployed/replayed | Could acknowledge a unique money fact as stale |
| 01021 accounting error message | Guarded duplicate | No missing canonical posting proven | No data change needed | SQL verified | No by itself | Posted natural-key journal exists; conflicting duplicate remains quarantined |
| Protected four-row backlog | Safety exclusion | No | Intentionally not processed | Yes unchanged | No by itself | Must not be counted as application failure under this policy |
| Second tenant/AffiliateOnly/failed delivery/return/payout live scenarios | Verification gaps | Not proven defects | N/A | No | Conditional after P1 closure | No safe fixture/credentials/sandbox |
| Broad historical test failures | Test debt | Mixed/undetermined | No | Yes, 41 failures | Release-process blocker | Deleted artifacts and stale assertions require triage |

# Remaining Risks

1. Deployability does not equal recovery: source gates are green but no original retained event has passed through the deployed fixed consumer.
2. Custom outbox at-least-once publication requires every downstream subscriber to remain event-ID idempotent.
3. A production recovery run needs explicit control over protected orders or explicit permission to process them.
4. Earned, refund, and reversal message ordering must be controlled during redrive so prerequisite projections exist; the hardened reversal consumer now retains premature messages instead of losing them.
5. RabbitMQ has substantial unrelated ready/error backlog. Consumer isolation prevents direct starvation across endpoints, but operational capacity and historical poison queues need separate remediation.
6. Optional/provider readiness must be configured or explicitly accepted for the target environment; health logic must not be weakened merely to obtain 200.
7. Live tenant isolation, AffiliateOnly portal reads, stale HTTP concurrency, failed delivery, return, and payout remain NOT VERIFIED.

# Final Verdict

## FAIL

The canonical SQL ledger is correct, the code 40 and reversal-loss defects are fixed in source, focused tests and builds pass, protected orders stayed untouched, and health reports failure honestly. However, valid outbox events remain pending, original earned events cannot yet be safely redriven through a repository-supported path, and Mongo has not converged for `ORD-83B3-2026-01021`. Those are explicit R-1 P1/recovery blockers and prevent PASS, CONDITIONAL PASS, or PASS WITH NOTES.

