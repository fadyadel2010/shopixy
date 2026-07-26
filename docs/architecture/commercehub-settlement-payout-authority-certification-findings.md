# PASS M5 CommerceHub Settlement & Payout Authority Certification

Date: 2026-06-10

Verdict: CommerceHub settlement and payout authority is production-certifiable for inspected runtime paths after disabling direct vendor-scoped payout execution HTTP mutation.

## Settlement Findings

Settlement identity is owned by PostgreSQL settlement and financial-ledger rows. `CommerceHubVendorSettlement` records vendor scope, idempotency key, external reference, actor evidence, amount, currency, and mirrored ledger correlation. Settlement replay must match the original SQL record, external references are unique, and ledger state deltas are verified before committed operation audit evidence is appended.

## Balance Findings

Vendor balances derive from `CommerceHubFinancialTransactions` plus payout/refund/settlement SQL rows. `CommerceHubVendorPayoutReadService` caps operational payable/eligible values by ledger payable truth, fails closed on authority conflicts, and reports currency/ledger drift as blocking risk. Stored dashboard totals, DTO totals, projections, and payout queue summaries are not authority.

## Readiness Findings

Settlement and outward payout readiness is owned by SQL readiness gates. `CommerceHubFinancialReadinessGate` blocks outward money movement for unhealthy accounting outbox, compensation, unresolved captured money, unresolved refunds, stale/reconciliation-required payouts, missing GL coverage, stale or unscoped reconciliation, and unhealthy integration backbone. Readiness is rechecked inside payout execution transactions before money recognition.

## Escrow Findings

Escrow release is not independently created by settlement code. Payout eligibility consumes payment/order/sub-order earning truth, delivery/refund-window state, active refund requests, payout delays, and ledger-confirmed vendor payable. Settlement authority may release earned value; it cannot create earned value without SQL payment/order/ledger backing.

## Payout Findings

Payout creation reserves only available, ledger-confirmed earnings under serializable SQL, row locks, idempotency, one-processing/reconciliation slot, single currency, negative adjustment netting, payout delay, and refund locks. Super Admin completed-payout recognition binds vendor, method, amount, actor, external evidence, eligible payable, platform cash, ledger posting, and operation audit in one transaction.

Runtime hardening performed in this pass: direct vendor-scoped `api/commercehub/payouts/{id}/processing`, `/complete`, and `/fail` mutations now return `PAYOUT_EXECUTION_WORKFLOW_REQUIRED` and no longer call `MarkProcessingAsync`, `MarkCompletedAsync`, or `MarkFailedAsync`. Payout execution, failure, and transfer-evidence recognition must use the Super Admin workflow.

## Transfer-Evidence Findings

Completed payouts require external reference or bank transaction evidence, global uniqueness, immutable completion fingerprint, idempotency payload matching, and ledger verification. Evidence cannot be accepted from the vendor CommerceHub route after this pass; Super Admin execution remains the reviewed recognition boundary.

## Treasury Findings

Treasury/platform-cash authority remains SQL ledger authority. Payout execution checks captured platform cash through `CaptureStateForUpdateAsync` and verifies `PlatformCashDelta` after `RecordPayoutAsync`. Payouts cannot bypass treasury cash constraints or readiness gates.

## Accounting Findings

Accounting authority remains PostgreSQL ledger/GL authority. Outward movement is blocked until CommerceHub operational ledger entries are covered by posted journal entries or pending accounting outbox authority. Payout completion writes ledger entries and verifies expected financial state deltas before operation audit commit.

## Refund And Recovery Findings

Refund interaction is fail-closed. Active refund requests, refund windows, processing refund locks, stale external refund outcomes, and payout/refund overlap block payout eligibility or execution. Post-payout refunds become debt/negative adjustment authority rather than rewriting completed payout history.

## Currency Findings

CommerceHub payout currency is EGP/platform-base scoped on inspected paths. Mixed operational, payout, refund, or ledger currencies set authority-blocking risk and zero eligible payout amounts. Super Admin execution rejects unsupported, missing, or mismatched currency.

## Tenant-Isolation Findings

Vendor reads and payout identities are tenant/vendor scoped through trusted context or Super Admin route authority. Vendor payout lists filter `TenantId == tenantId`; payout methods bind `method.VendorId == vendorId`; Super Admin can view cross-vendor payout queue only as platform execution visibility. Direct vendor payout execution mutation is disabled to prevent route/DTO evidence from becoming payout authority.

## Concurrency Findings

Payout creation and completion use serializable transactions, earning row locks, idempotency keys, one-processing-per-tenant uniqueness, external transfer idempotency, unique external evidence, completion fingerprints, and ledger fingerprints. Replay either returns matching SQL truth or fails closed; it cannot create duplicate value.

## Historical-Consistency Findings

Payout and settlement history is reconstructable from payout rows, payout items, earning allocations, settlement rows, financial transactions, audit rows, Super Admin audit, reconciliation reports, and immutable evidence fingerprints. Reruns converge on SQL idempotency/evidence records and do not rebuild authority from stale projections.

## Adversarial Findings

Settlement replay, payout replay, balance inflation/suppression, escrow bypass, refund-after-payout abuse, treasury bypass, accounting bypass, currency manipulation, tenant guessing, DTO tampering, route tampering, projection poisoning, and stale snapshot resurrection are blocked or routed to fail-closed/reconciliation-required states on inspected runtime paths. The active M5 runtime gap was direct vendor HTTP payout execution mutation; it is now disabled.

## Runtime Files Modified

- `PointOfSale.API/Controllers/CommerceHubPayoutsController.cs`

## Regression Freezes Added

- `CommerceHubPayoutCertificationTests.PayoutHttpMutation_MustRequireSuperAdminExecutionWorkflow`

## Architecture Updates

- Added this M5 findings document.
- Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md` with the M5 authority rule.

## Required Answers

1. Who owns settlement authority? PostgreSQL settlement, payout, earning, payment, refund, ledger, accounting, audit, and reconciliation rows own internal settlement/payout authority; Super Admin owns payout execution recognition; external banks/providers own external transfer side effects only.
2. Can balances diverge from ledger truth? Not on inspected paths; payout eligibility is capped by ledger payable and authority conflicts fail closed.
3. Can readiness be bypassed? Not on inspected paths; readiness gates run before outward payout movement and inside execution transactions.
4. Can escrow release early? Not on inspected paths; payout eligibility requires delivery, refund-window, active-refund, payout-delay, and ledger-confirmed earning truth.
5. Can payouts replay? Replay cannot create duplicate value; idempotency, external evidence uniqueness, one-processing slot, fingerprints, and ledger checks collapse or reject replay.
6. Can transfer evidence be forged? Vendor-route DTO evidence can no longer complete/fail/process payouts. Super Admin recognition still requires external reference or bank transaction evidence, uniqueness, actor authority, and immutable fingerprinting.
7. Can treasury be bypassed? No inspected path bypasses platform-cash and ledger state verification.
8. Can accounting be bypassed? No inspected path bypasses ledger/GL readiness and payout ledger posting.
9. Can refunds corrupt payouts? No inspected path permits that; refunds block overlap and post-payout impact becomes debt/negative adjustment.
10. Can tenant leakage occur? No inspected tenant-scoped vendor payout read/execution path leaks or mutates foreign vendor authority.
11. Can currency corruption occur? No inspected path accepts mixed or unsupported payout currencies; conflicts block eligibility/execution.
12. Were runtime code changes required? Yes. Direct vendor-scoped payout execution HTTP mutation was disabled.
13. Is CommerceHub Settlement & Payout Authority production-certifiable? Yes, for inspected runtime authority paths. Broader public-launch operational blockers around deployment evidence, alert packs, WORM/SIEM retention, backup/restore rehearsal, query-plan review, and PostgreSQL-backed chaos proof remain inherited operational work.
