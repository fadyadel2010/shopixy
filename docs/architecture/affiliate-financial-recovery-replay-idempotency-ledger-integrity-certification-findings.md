# PASS AF-PA6.4 Financial Recovery, Replay, Idempotency & Ledger Integrity Certification Findings

## Certification Scope

- Reviewed required context files, prior AF-PA1.1 through AF-PA6.3 certification summaries/findings, and runtime before tests.
- Certified only Financial Recovery, Replay, Idempotency, Ledger Integrity, Crash Recovery, Settlement Recovery, Outbox Recovery, Balance Rebuild, and Ledger Rebuild boundaries.
- Did not recertify business rules already certified by AF-PA1.1 through AF-PA6.3.

## Recovery Findings

- Financial Recovery is backend-owned by current SQL plus owning backend services.
- Recovery authority begins when runtime re-enters `AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync`, `AffiliatePayoutService.GetAvailableBalanceCoreAsync`, `AffiliatePayoutService.RequestPayoutCoreAsync`, or commission recovery methods from current SQL state.
- Application/worker restart cannot continue from memory-only state; every payment, request, ledger, evidence, and balance path reloads current SQL state.
- Runtime hardening added a persistence-boundary guard blocking modified or deleted `AffiliateCommissionLedger` rows, closing the remaining append-only enforcement gap at SaveChanges.

## Replay Findings

- Duplicate HTTP payout execution re-enters `AdminAffiliateController.MarkPayoutPaid`, reloads current `AffiliatePayoutRequest`, requires `Approved`, and passes execution into the orchestrator.
- Duplicate queue/outbox delivery can retry event or accounting delivery, but payout settlement itself is fenced by transfer evidence allocation idempotency, balance-ledger withdrawal idempotency, commission paid idempotency, payout-request status, and serializable transaction retry.
- Replay with matching authority fields no-ops or returns `WasIdempotentSkip`; replay with mismatched fields fails closed as an idempotency conflict.

## Idempotency Findings

- Withdrawal request idempotency boundary: serialized `AffiliatePayoutService.RequestPayoutCoreAsync` balance lock plus active same-affiliate/same-store/same-amount duplicate return.
- Approval boundary: row-locked `AffiliatePayoutRequest` status transition from `Pending` only.
- Settlement boundary: `AffiliateSettlementOrchestrator` serializable transaction, `AffiliateBalanceLedger` withdrawal reference/idempotency, `AffiliatePayoutTransferAllocation` idempotency key, `AffiliatePayoutTransferEvidence` normalized reference uniqueness, `AffiliateCommissionLedger.Paid` idempotency key, paid accounting idempotency key, and request `MarkPaid` status guard.
- Accounting boundary: paid journals use deterministic idempotency keys keyed by store/order/settlement reference.

## Ledger Findings

- `AffiliateCommissionLedger` is append-only, globally idempotent by `IdempotencyKey`, and now persistence-guarded against updates/deletes.
- `AffiliateBalanceLedger` is append-only, globally idempotent by `IdempotencyKey`, and persistence-guarded against updates/deletes.
- `AffiliatePayoutTransferAllocation` is append-only and unique by idempotency key.
- `AffiliatePayoutTransferEvidence` identity/reference/amount/currency fields are immutable after creation; only allocation totals may advance.
- Ledger drift is blocked by exact FIFO payout planning, reversal netting, amount-exactness checks, unique paid entries, withdrawal reference idempotency, and paid accounting readiness.

## Outbox Findings

- Outbox recovery does not own payout truth.
- Before outward affiliate money movement, the orchestrator blocks when critical accounting, integration, MassTransit, marketplace, payment-compensation, unresolved-payment, unresolved-refund, or stock outbox/backbone state is failed, stale, ambiguous, or scope-unproven.
- Duplicate outbox delivery can re-deliver derivative events/accounting work, but cannot duplicate settlement because settlement is already committed or blocked at SQL idempotency boundaries.

## Balance Rebuild Findings

- Balance rebuild is not a writer of financial truth.
- Rebuild/read convergence comes from `GetAvailableBalanceCoreAsync`: net `Approved - Reversed`, negative non-withdrawal balance movements, withdrawal ledger rows, and active payout reservations.
- `Earned` rows, projections, frontend totals, Redis, Mongo, and browser cache are not withdrawable balance authority.
- Rebuild cannot fabricate balances because positive adjustments/bonuses are explicitly excluded from payout authority.

## Failure Findings

- Crash before commit: no ledger/request/evidence/accounting mutation is durable.
- Crash after commit with lost response: retry re-enters SQL, sees withdrawal/evidence/request/paid-ledger state, and no-ops or fails closed if the replay fields differ.
- Transaction rollback removes staged withdrawal, paid ledger, allocation, paid request transition, events, and accounting writes together.
- Lost acknowledgement or duplicate browser retry cannot create duplicate money because frontend submits only reference/proof candidates and backend idempotency controls decide.
- Partial settlement is blocked because request-backed settlement marks `AffiliateBalanceLedger.Withdrawal`, `AffiliateCommissionLedger.Paid`, accounting, transfer allocation, events, and `AffiliatePayoutRequest.Paid` inside the same serializable transaction.

## Adversarial Findings

- Replaying HTTP cannot duplicate settlement/withdrawal because current SQL request status and withdrawal/evidence idempotency are decisive.
- Replaying queue/outbox cannot duplicate payment because outbox is transport/retry only and settlement cannot be reconstructed from events.
- Replaying settlement with a changed amount, tenant, affiliate, currency, reference, or allocation conflicts and fails closed.
- Modifying events, outbox rows, frontend DTOs, browser state, Redis, Mongo, projections, or cache cannot create financial truth.
- Modifying ledger rows through EF is now blocked for both commission and balance ledgers at the persistence boundary.

## Recovery Pipeline

Crash -> application/worker restart -> backend SQL reload -> Recovery Authority begins at owning backend service/orchestrator -> replay detection through current request/evidence/ledger/accounting rows -> idempotency check through status guards and unique keys -> ledger validation through append-only ledgers and exact amount/currency/tenant/affiliate checks -> settlement validation through verified evidence allocation, readiness gates, accounting idempotency, and paid request transition -> Financial Truth is restored by current SQL ledgers/request/evidence/accounting rows -> recovery complete.

## Runtime Hardening Performed

- Added `AffiliateCommissionLedger` modified/deleted row rejection to `ApplicationDbContext.GuardAffiliatePayoutAuthority`.
- Added AF-PA6.4 architecture regression freezes for recovery authority, replay/idempotency, ledger immutability, balance rebuild convergence, outbox readiness, and frontend non-authority.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Tests/Architecture/AffiliateFinancialRecoveryReplayLedgerIntegrityCertificationTests.cs`
- `docs/architecture/affiliate-financial-recovery-replay-idempotency-ledger-integrity-certification-findings.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Certification Decision

- Financial Recovery is production-certifiable after runtime append-only persistence hardening for `AffiliateCommissionLedger`.
- Recovery never creates or destroys money.
- Replay never duplicates settlement, withdrawal, ledger entries, or payment.
- Outbox replay is idempotent/derivative only.
- Ledger and balance rebuild converge to current SQL financial truth.
