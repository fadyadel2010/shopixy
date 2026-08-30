# O-4.7 Post-Integration Gap Closure Certification

Date: 2026-08-28

## Engineering Verdict

**FAIL**

The source defects identified during post-integration forensics have bounded fixes and deterministic regressions, but the currently inspected runtime is not release-certified:

- The normal hosted-service path was not started because 4 of the 26 pending custom outbox rows belong to protected orders `ORD-83B3-2026-01018` and `ORD-83B3-2026-01019`. Starting it would have violated the required read-only constraint.
- Failed `AffiliateCommissionEarnedEvent` messages remain retained in RabbitMQ. They were inspected without acknowledgement but were not manually replayed or republished.
- PostgreSQL remains the canonical financial authority for `ORD-83B3-2026-01021`, but its Mongo order projection remains stale at `AttributedPendingPayment` with zero commission.
- Development readiness is unhealthy because the custom outbox backlog is now correctly visible, R2 is not fully configured, and Fawry operational readiness is degraded. Disabled certificate integrations are degraded rather than unhealthy.
- Live data contains only one order tenant and one affiliate account, so live second-tenant isolation and payout execution cannot be proven without fabricating production records.
- Focused O-4.7 gates are green, but broader historical affiliate, payment-name, and delivery-name sweeps still contain unrelated fixture, deleted-artifact, and stale source-assertion failures.

No protected order, historical financial row, RabbitMQ message, or real financial rail was mutated during this closure.

## Root-Cause Matrix

| Gap | Severity | Root cause | Correction | Deterministic evidence | Runtime evidence | Status |
|---|---:|---|---|---|---|---|
| Checkout outbox rows remained at zero attempts | P1 | `OutboxPublisherJob` was the only `OutboxMessages` processor but was never registered with Hangfire or the host | Converted it into a continuous five-second `BackgroundService` and registered it in `Program.cs` | `GenericIntegrationOutbox_PublishesAndAcknowledgesPersistedMessage`; hosted-service registration regression | 26 existing pending rows found read-only; normal drain intentionally blocked by protected-order constraint | Source fixed, runtime pending |
| Readiness did not expose custom outbox stall | P1 | Existing readiness checks covered projection, MassTransit, accounting, and payment cancellation outboxes, but not `OutboxMessages` | Added `IntegrationOutboxReadinessHealthCheck` with stale-zero-attempt, repeated-failure, operator-reconciliation, and lag classification | Stale zero-attempt row deterministically returns `Unhealthy` | `/health/ready` returned `503` and named `integration_outbox_readiness` unhealthy | Fixed |
| Earned affiliate projection poisoned on every retry | P1 | Mongo update assigned `orderNumber` and `affiliateAccountId` through both `$setOnInsert` and `$set`, causing Mongo code 40 | Removed overlapping paths and extracted the update definition for rendering regression | Rendered update proves `$set` and `$setOnInsert` path sets are disjoint; affiliate projection suite `16/16` | Existing error queue retained; no blind replay performed | Source fixed, historical projection pending |
| OpenAPI generation failed | P1 operational | Public `IActionFilter` methods on `ReportsController` were interpreted as actions; duplicate `SessionDto` names also collided under default schema IDs | Added `[NonAction]` and fully-qualified Swagger schema IDs | Full API action/schema document generation passes | `/swagger/v1/swagger.json` returned `200` | Fixed |
| Raw referral code in projection log | P2 security/privacy | Attribution projection included the raw referral code in structured information logging | Removed the code value and placeholder | Source regression rejects `Code: {Code}` | No live secret/token logging exercised | Fixed |
| COD integration fact was semantically ambiguous | P2 | Checkout emitted `PaymentIntentCreatedEvent` for COD without setting its explicit offline-collection marker | Retained the compatibility event but set `IsOfflineCollection=true`; no provider collection authority is implied | COD source contract and actual outbox dispatch regression pass | Existing persisted COD rows predate the correction | Fixed for new events |
| Paymob reservation guard differed from generic and Stripe paths | P1 correctness | Initial Paymob creation called the internal all-order guard rather than the storefront-aware guard | Revalidate storefront SQL reservation authority before provider work while avoiding storefront requirements for unrelated orders | Checkout concurrency source suite passes | No real provider call made | Fixed |
| Fawry handoff did not require its order-bound checkout token at every admission boundary | P1 security/recovery | Backend issued the token, but backend handoff checks and frontend admission accepted Fawry evidence without it | Require non-empty token in backend replay/recovery checks and frontend authority validation | Fawry `148/148`; storefront authority `29/29`, including missing-token rejection | No real Fawry call made | Fixed |

## Endpoint Matrix

The API was started with `REQUIRE_REAL_POSTGRES_TESTS=1`. This repository mode removes all hosted services and migration/startup processors, allowing safe HTTP verification without draining protected outbox rows.

| Endpoint | Expected | Actual | Result | Notes |
|---|---:|---:|---|---|
| `/health/live` | `200` when process is serving | `200` | PASS | Liveness does not run dependencies |
| `/health/ready` | `503` while authoritative fanout/config is blocked | `503` | PASS | Unhealthy: integration outbox, R2, Fawry; degraded: certificate provider and Caddy |
| `/health` | Non-healthy while release dependencies are blocked | `503` | PASS | Full health surface preserves failure visibility |
| `/swagger/v1/swagger.json` | `200` and valid document | `200` | PASS | Action ambiguity and schema collision both closed |
| Affiliate authenticated portal APIs | `401/403` for wrong role/scheme; tenant-fenced reads | Not run live | NOT VERIFIED | Deterministic auth/session/tenant suite `83/83`; no safe second live tenant or credential set |
| Checkout/payment mutation APIs | Safe fixtures only | Not run live | NOT VERIFIED | Normal host would mutate protected outbox rows; real provider rails prohibited |
| Affiliate payout mutation APIs | Sandbox/local fake only | Not run | NOT VERIFIED | Only one live affiliate account and no approved payout fixture |

## Runtime Scenario Matrix

| Scenario | Evidence | Result |
|---|---|---|
| PostgreSQL commission authority for `01021` | Append-only Earned `40`, Approved `40`, partial Reversed `4`, remainder Reversed `36`; net zero | PASS |
| Mongo convergence for `01021` | Order document remains `AttributedPendingPayment`, empty items, zero commission/reversal | FAIL |
| RabbitMQ earned projection failure diagnosis | Retained messages identify Mongo code 40 conflicting `orderNumber` update path | PASS |
| RabbitMQ safe handling | Inspection used requeue semantics; no acknowledgement, deletion, or publication | PASS |
| Custom checkout outbox diagnosis | 26 pending rows, all zero-attempt; four rows belong to protected orders | PASS |
| Custom outbox actual execution | In-memory persisted event is published and marked processed with retry count zero | PASS |
| Custom outbox live drain | Not executed because it would mutate protected rows | NOT VERIFIED |
| Health classification | Stale zero-attempt backlog returns unhealthy and live readiness reports it | PASS |
| Swagger generation | In-process complete document and HTTP document both succeed | PASS |
| Wrong-role / token-scheme rejection | Affiliate authentication/session and tenant-scope focused suite `83/83` | PASS, deterministic |
| Live cross-tenant isolation | Runtime has one order tenant and one affiliate account | NOT VERIFIED |
| COD event semantics | New checkout event explicitly marks offline collection | PASS, deterministic |
| Paymob/Fawry/manual/COD provider contracts | Provider-focused suites listed below | PASS, deterministic |
| Failed delivery / return convergence | Broad sweep hit stale fixtures, missing test sequence/migrations, and deprecated endpoint assumptions | FAIL baseline gate |
| Payout execution | No safe local approved payout fixture; no real financial rail used | NOT VERIFIED |

## Test Matrix

| Gate | Result |
|---|---|
| `dotnet build PointOfSale.sln --no-restore` | PASS, 0 errors |
| `npm run build` in `PointOfSale.Storefront` | PASS; existing chunk-size/dynamic-import warnings remain |
| `O47GapClosureRegressionTests` | PASS `6/6` |
| `AffiliateProjectionTests` | PASS `16/16` |
| Projection/payment-cancellation/outbox architecture checks | PASS `41/41` |
| Checkout concurrency and O-4.7 combined gate | PASS `33/33` |
| Paymob architecture/certification prefix | PASS `142/142` |
| Fawry architecture/certification prefix | PASS `148/148` |
| Manual Payment/Manual Wallet architecture prefix | PASS `151/151` |
| COD architecture prefix | PASS `75/75` |
| Accounting/outbox/health focused gate | PASS `77/77` |
| Affiliate auth/session/multi-tenant/admin scope gate | PASS `83/83` |
| Storefront payment-session authority | PASS `29/29` |
| Broad affiliate prefix | FAIL `711/720`; failures include historical documentation markers, one frontend source marker, and stale governance slicing |
| Broad payment-name sweep | FAIL/TIMEOUT; crossed into unrelated CommerceHub vendor finance, purchase integration, deleted migration artifacts, and stale frontend assertions |
| Broad delivery/return-name sweep | FAIL/TIMEOUT; missing `return_number_seq`, deprecated mutation fixtures, missing headers/catalog authority, and SQLite lock contention |

Focused green totals are not summed because several filters overlap.

## Required Re-Certification Sequence

1. Obtain explicit permission to process the protected orders, or exclude them through an approved operational change outside this certification.
2. Deploy the fixed earned projection consumer, hosted custom outbox publisher, health check, and Swagger corrections.
3. Allow the normal host to drain `OutboxMessages`; verify `ProcessedAtUtc`, retry state, RabbitMQ publication, and `integration_outbox_readiness` recovery without direct SQL edits.
4. Re-drive only the retained earned-projection messages whose failure is the diagnosed Mongo code 40 path conflict. Capture queue counts and SQL/Mongo snapshots before and after; do not republish reconstructed events.
5. Verify `01021` converges to SQL-authoritative gross earned/reversed totals and net zero without duplicate ledger entries or duplicate Mongo item increments.
6. Configure or explicitly classify R2 and Fawry for the target deployment environment; do not weaken readiness merely to obtain `200`.
7. Provision disposable second-tenant, wrong-role, failed-delivery, return, and payout sandbox fixtures and execute the remaining live matrix.
8. Repair or formally retire the historical broad-suite failures, then rerun all focused and broad gates before changing this verdict.

## Files Changed By This Closure

- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProjectionConsumers.cs`
- `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs`
- `PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs`
- `PointOfSale.Infrastructure/Extensions/ObservabilityServiceExtensions.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Controllers/ReportsController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.test.ts`
- `PointOfSale.Tests/Architecture/O47GapClosureRegressionTests.cs`
- `PointOfSale.Tests/Architecture/FawryPaymentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Affiliate/AdminAffiliateTenantScopeTests.cs`
- `PointOfSale.Tests/Affiliate/AdminAffiliateApiTests.cs`
- `.codex-tmp/o1-forensics/Program.cs` (disposable read-only evidence utility)
