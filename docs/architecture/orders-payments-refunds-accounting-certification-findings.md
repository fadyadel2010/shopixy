# POS-ADMIN-CERTIFICATION-01 PASS 4 Orders / Payments / Refunds / Accounting Certification Findings

Date: 2026-05-27

## Scope

This certification reviewed the existing SQL-first order, payment, refund, settlement, and accounting boundaries after the earlier ORDER-CON-01, PAYMENT-CON-01, and ACCOUNTING-CON-01 hardening.

The objective was not a payment-stack rewrite. The objective was to validate replay-safe financial convergence under callback replay, retry overlap, recovery, rebuild, and accounting outbox pressure, then close any remaining runtime gap that still trusted derivative or stale financial evidence.

## Boundaries Audited

- Order lifecycle and storefront order payment visibility: SQL `Orders`, `OrderProjectionConsumer`, `OrderPaymentLineageAuthority`, lifecycle fanout, rebuild/repair behavior.
- Payment callback, retry, and settlement authority: `PaymentLifecycleService`, settlement fanout, replay collapse, terminal-state handling, callback recovery.
- Refund and settlement correctness: SQL `OrderRefunds`, `PaymentSettlementBatches`, `PaymentSettlementOrders`, refund/settlement idempotency lineage, storefront visibility fanout.
- Accounting replay and duplicate-posting protection: `AccountingOutboxProcessor`, `AccountingLedgerAuthority`, `FinancialPostingService`, journal idempotency, retry behavior.
- Recovery/runbook boundaries: payment reconciliation, accounting replay, stale callback suppression, dangerous financial primitives, operator recovery semantics.

## Certification Result

- The previously hardened financial model held on the inspected slices: payment/refund/settlement lifecycle authority remained SQL-first, callback replay remained fail-closed against terminal truth, storefront payment visibility still recomposed from current SQL lineage, and accounting posting still converged through idempotent SQL-ledger boundaries.
- One concrete settlement-accounting gap remained: settlement posting still trusted the denormalized `PaymentSettlementBatch.OrderCount` header as settlement-membership proof instead of revalidating the current `PaymentSettlementOrders` lineage.
- That gap affected both replayed accounting outbox settlement posting and the immediate post-commit settlement accounting fast path in `PaymentLifecycleService`.
- A second adjacent gap shared the same root cause on the refund side: post-settlement refund accounting still trusted the stored refund settlement snapshot after creation instead of revalidating that the refunding order still belonged to the current settlement batch lineage when immediate or replayed accounting posted.
- A third adjacent write-side gap remained on subsequent refunds: once a first post-settlement refund changed the order to `PartiallyRefunded`, a later refund could incorrectly fall back to pre-settlement refund handling if the live `PaymentSettlementOrders` mapping disappeared before the next refund attempt.
- A residual storefront projection gap remained: when payment replay no longer had current SQL order authority, the consumer attempted rehydrate and returned, allowing an existing Mongo order payment projection to remain resident. A replay whose payment status conflicted with SQL truth could also append payload-derived financial timeline narrative while status itself was recomposed from SQL.

## Runtime Hardening Added

- `AccountingLedgerAuthority.LoadCurrentSettlementAsync(...)` now loads both the recorded batch header count and the current live `PaymentSettlementOrders` count for the settlement batch.
- Settlement replay now explicitly rejects soft-deleted settlement batches as missing current SQL authority even when historical `PaymentSettlementOrders` rows still exist.
- `AccountingLedgerAuthority.EvaluateSettlementPosting(...)` now retries instead of posting when:
  - current settlement-order lineage is empty, or
  - the recorded `PaymentSettlementBatch.OrderCount` does not match the current live `PaymentSettlementOrders` count.
- `OperationalConvergenceTelemetry` now classifies this condition as `SettlementOrderLineageMismatch`.
- `PaymentLifecycleService` immediate settlement accounting paths now call `EnsureSettlementBatchRetainsCurrentOrderLineageAsync(...)` before journal posting so the same invariant holds for the fast path, not only for replayed outbox work.
- Gateway/COD settlement recording now rejects non-positive gross amounts and fees outside `[0, gross]` before persisting settlement authority or mutating order payment status.
- `ResolveRefundAccountingContextAsync(...)` now rejects settled-order refunds when the current settlement batch no longer retains the order or the batch header count has drifted from current live lineage.
- `ResolveRefundAccountingContextAsync(...)` now also treats current post-settlement refund history as proof that later refunds must keep revalidating live settlement lineage even after the order has moved from `Settled` to `PartiallyRefunded`.
- `AccountingLedgerAuthority.LoadCurrentRefundAsync(...)` now reloads the current settlement batch for post-settlement refunds, verifies live `PaymentSettlementOrders` membership for the refunding order, and surfaces missing or drifted settlement lineage as retry/reconciliation-required refund authority failures.
- `PaymentLifecycleService` immediate refund accounting now uses the same `AccountingLedgerAuthority` refund check as replay before bank-account refund posting.
- `PaymentLifecycleService` immediate gateway/COD settlement posting now reloads `AccountingLedgerAuthority.LoadCurrentSettlementAsync(...)` and enforces `EvaluateSettlementPosting(...)` before ledger mutation, so the fast path and replay path use the same gross/fees/lineage authority gate instead of only a local lineage check.
- `OrderProjectionConsumer` now deletes tenant-scoped storefront order projection state and invalidates its cache when `OrderPaymentLineageAuthority` finds no current SQL order authority during payment replay.
- When replayed payment status conflicts with current SQL status, the consumer recomposes SQL status and freshness without appending callback- or event-derived timeline narrative.
- Missing-SQL payment projection deletion is recorded through `OperationalConvergenceTelemetry` as an order-payment authority deletion with a stable missing-SQL-authority reason.

## Settlement And Accounting Semantics

- `PaymentSettlementBatch` remains the SQL authority row for settlement identity, provider, gross amount, fees, currency, settled timestamp, and lifecycle ownership.
- `PaymentSettlementBatch.OrderCount` is recorded batch metadata, not sufficient replay authority for journal posting by itself.
- Current settlement membership authority is the live `PaymentSettlementOrders` lineage for the batch.
- Stored settlement snapshot fields on `OrderRefund` are durable audit evidence, not standalone refund-posting authority.
- Settlement accounting is therefore allowed only when:
  - the settlement batch still exists as current non-deleted SQL authority,
  - the current settlement gross amount is positive and current fees stay within `[0, gross]`,
  - current live settlement-order lineage exists,
  - the current live settlement-order count matches the recorded batch count, and
  - normal journal idempotency and replay guards still hold.
- Post-settlement refund accounting is therefore allowed only when:
  - the refund still exists as current SQL authority,
  - the current non-deleted settlement batch still exists,
  - the current settlement batch still contains the refunding order in live `PaymentSettlementOrders` lineage,
  - the current live settlement-order count still matches the recorded batch count, and
  - normal journal idempotency and replay guards still hold.
- A lineage mismatch is treated as retryable/reconciliation-required state, not as permission to post from the batch snapshot.

## Replay / Recovery Guarantees Validated

- Delayed or duplicated settlement replay cannot post from stale payload amount, stale provider metadata, or stale batch header membership alone.
- Immediate settlement posting now rejects the same invalid gross/fees authority that replay rejects, instead of allowing the post-commit fast path to drift into a looser settlement-accounting rule.
- Deleted settlement-batch replay cannot post from historical settlement-order mappings after current SQL settlement authority is gone.
- Accounting outbox retry storms still converge deterministically: current SQL truth posts once, existing journal truth is reused, stale work no-ops, and inconsistent settlement lineage remains retryable.
- Immediate post-commit settlement accounting now uses the same SQL-lineage rule as replayed accounting recovery.
- Immediate post-commit refund accounting now uses the same post-settlement refund-lineage rule as replayed accounting recovery.
- A later refund on a previously post-settlement order cannot silently downgrade into pre-settlement handling just because the current order status is already `PartiallyRefunded`.
- Storefront payment visibility remains payment-lifecycle derived and does not become settlement/accounting authority. Missing or deleted SQL payment lineage no longer leaves a stale storefront order payment document resident after a payment replay trigger, and a SQL-conflicting replay cannot create misleading payment timeline narrative while recomposing current status.
- Operator recovery remains SQL-first: replay may retry or escalate, but must not manually override settlement membership from projections, payloads, or stale snapshots.

## Regression Freeze Invariants

- Settlement accounting must never trust `PaymentSettlementBatch.OrderCount` as sole posting authority.
- Settlement accounting must never post from a soft-deleted settlement batch.
- Settlement write authority must reject non-positive gross amounts and fees outside `[0, gross]` before persisting settlement batches or mutating order payment status.
- Current `PaymentSettlementOrders` lineage must be recomposed before gateway or COD settlement journal posting.
- Empty or drifted settlement-order lineage must fail closed to retry/reconciliation-required behavior.
- Immediate lifecycle posting and replayed accounting posting must enforce the same settlement-accounting authority rule, including gross/fees bounds and settlement-order lineage.
- Post-settlement refunds must never post from stored refund settlement snapshot fields alone.
- Post-settlement refund creation and refund accounting must revalidate that the refunding order still belongs to the current settlement batch lineage, including later refunds after the order has already transitioned to `PartiallyRefunded`.
- Storefront payment replay must delete tenant-scoped derivative order visibility and cache state when current SQL order authority is missing, and must not append payload-owned payment timeline narrative when replayed status conflicts with SQL truth.
- New settlement repair, import, replay, export, or reconciliation tooling must not bypass `AccountingLedgerAuthority` or the equivalent lifecycle settlement-lineage check.

## Regression Coverage Added

- `AccountingLedgerAuthorityTests` cover:
  - live settlement-order lineage recomposition,
  - deleted settlement-batch rejection,
  - post-settlement refund settlement-lineage recomposition,
  - retry on recorded-versus-current settlement count drift,
  - acceptance when current settlement lineage matches.
- `AccountingOutboxProcessorTests` cover replay retry when settlement batch header count drifts from current live settlement-order mappings.
- `AccountingOutboxProcessorTests` also cover retry when a post-settlement refund no longer has current settlement-order membership.
- `EcommerceRefundLifecycleTests` cover fail-closed rejection when a settled-order refund sees settlement-lineage drift before refund creation.
- `PaymentLifecycleRefundAuthorityTests` cover fail-closed rejection when a later refund sees existing post-settlement refund history but no longer has current settlement-lineage authority.
- `FinancialIntegrityRiskHardeningTests` freeze the source-level invariants that:
  - settlement replay must recompose `PaymentSettlementOrders`,
  - mismatch must route through `SettlementOrderLineageMismatch`, and
  - `PaymentLifecycleService` fast-path settlement/refund posting must validate current settlement-order lineage before ledger mutation, including subsequent refunds on `PartiallyRefunded` orders with post-settlement history.
- `FinancialIntegrityRiskHardeningTests` also freeze fail-closed storefront payment projection deletion and suppression of conflicting replay timeline additions.
- `PaymentLifecycleSettlementAuthorityTests` cover fail-closed rejection of invalid gateway/COD settlement amount bounds before settlement or ledger mutation.

## Operational Auditability Findings

- The new telemetry reason `SettlementOrderLineageMismatch` makes settlement replay drift explicit instead of surfacing only as a generic accounting retry.
- The new refund settlement-lineage retry reasons make stale post-settlement refund accounting diagnosable instead of collapsing into a generic refund retry.
- Missing-SQL storefront payment projection deletion now records a stable convergence decision, making stale derivative financial removal visible during replay and recovery.
- This keeps the failure mode diagnosable during outbox replay, retry storms, and settlement recovery without granting operators a manual financial override path.

## Remaining Debt

- This pass does not introduce a new external bank-grade reconciliation engine, immutable ERP framework, or universal settlement export rewrite.
- `PaymentSettlementBatch` still carries denormalized metadata for operational convenience; the certification only freezes that metadata as non-authoritative unless current live settlement-order lineage agrees.
- CommerceHub finance dashboards, payout exports, tax/reporting snapshots, affiliate accounting exports, and future settlement repair/import utilities remain path-specific review surfaces if they begin deriving financial visibility from stale settlement snapshots.
- The storefront payment correction covers the inspected storefront order payment consumer. Affiliate/support/export/notification or future payment projection writers remain path-specific certification debt.
- Manual financial repair remains intentionally forbidden outside lifecycle/reconciliation owners.

## 2026-05-27 PASS 4 Settlement Boundaries Addendum

- A narrower write-side gap remained after the 2026-05-25 PASS 4 hardening: settlement recording validated totals and lineage, but reviewed gateway/COD entrypoints still allowed non-positive COD gross amounts or invalid settlement fees to reach settlement creation, and the immediate settlement-accounting fast path enforced lineage without reusing the full `AccountingLedgerAuthority` settlement evaluation that replay used.
- The reviewed write path now fails closed earlier. Gateway/COD settlement recording rejects non-positive gross amounts and fees outside `[0, gross]` before persisting settlement batches, enqueuing accounting, or changing `Order.PaymentStatus`.
- The reviewed post-commit fast path is now replay-symmetric. Immediate gateway/COD settlement posting reloads `AccountingLedgerAuthority.LoadCurrentSettlementAsync(...)`, requires `EvaluateSettlementPosting(...)`, and posts from the recomposed settlement snapshot instead of using only the tracked batch row plus a local lineage check.
- A second write-side asymmetry on the same PASS 4 boundary is now closed as well. If a prior refund already proved the order was refunded after settlement, later refunds must still see current settlement lineage even when the order status is already `PartiallyRefunded`; missing live settlement mappings now fail closed instead of silently downgrading to pre-settlement refund handling.
- This does not introduce a new settlement framework or operator override ceremony. It narrows two concrete runtime asymmetries between settlement/refund creation, immediate posting, and replayed accounting.

## Files And Tests

- Runtime evidence:
  - `PointOfSale.Infrastructure/Reliability/AccountingLedgerAuthority.cs`
  - `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
  - `PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs`
  - `PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs`
- Regression coverage:
  - `PointOfSale.Tests/Infrastructure/AccountingLedgerAuthorityTests.cs`
  - `PointOfSale.Tests/Accounting/AccountingOutboxProcessorTests.cs`
  - `PointOfSale.Tests/Architecture/FinancialIntegrityRiskHardeningTests.cs`
  - `PointOfSale.Tests/Infrastructure/SemanticConvergenceChaosValidationTests.cs`

## Verification On 2026-05-27

- `dotnet build PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj -nologo -v quiet --no-restore -m:1 -clp:ErrorsOnly` passed.
- `dotnet build PointOfSale.Tests/PointOfSale.Tests.csproj -nologo -v quiet --no-restore -m:1 -clp:ErrorsOnly` passed after correcting an existing test-only xUnit misuse in `ProductInventorySqlAuthorityFallbackTests` (`Assert.NotNull(...)` is void for this test dependency).
- `FinancialIntegrityRiskHardeningTests` plus `OrderPaymentLineageAuthorityTests` passed: 15 tests. This includes the storefront missing-SQL deletion and conflicting-timeline suppression freeze.
- `AccountingLedgerAuthorityTests`, `AccountingOutboxProcessorTests`, `PaymentLifecycleSettlementAuthorityTests`, and `SemanticConvergenceChaosValidationTests` passed: 36 tests.
- `PaymentLifecycleRefundAuthorityTests` plus `PaymentLifecycleSettlementAuthorityTests` passed: 3 tests.
- `EcommerceRefundLifecycleTests` remains blocked: 7 tests fail during SQLite `EnsureCreatedAsync` before lifecycle test bodies execute with `SQLite Error 1: 'near "' \t\n\r'": syntax error'`. This is recorded verification debt, not evidence of a financial convergence failure or a passing lifecycle integration proof.

## 2026-05-25 PASS 7 Replay / Worker Restart Addendum

- Payment cancellation recovery now has the same explicit worker-restart posture expected of other financial/recovery outboxes. `PaymentCancellationOutboxProcessor` claims rows through `ClaimedUntilUtc` before calling `IPaymentLifecycleService`, skips active claims, clears claims on processed/manual-review/failure outcomes, and resumes after bounded lease expiry.
- This is replay amplification containment only. `IPaymentLifecycleService` remains the payment authority; operators must not use direct payment-status mutation or manual processed-row marking to clear cancellation backlog.
- Regression coverage is in `PaymentCancellationOutboxOperationalScalabilityTests` and `ReplayRebuildChaosCertificationTests`; broader PASS 7 findings are in `docs/architecture/replay-rebuild-chaos-certification-findings.md`.
