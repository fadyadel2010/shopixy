# PASS M4 CommerceHub Refund Authority Certification Findings

Date: 2026-06-10

Status: CommerceHub Refund Authority is production-certifiable for the inspected runtime paths after runtime hardening.

## Findings

- Refund identity findings: PostgreSQL `CommerceHubRefundRequests`, `CommerceHubRefunds`, and `CommerceHubRefundExecutions` own request, financial refund, execution, and lineage identity. SQL uniqueness on request idempotency, refund idempotency, refund-request linkage, and execution linkage prevents duplicate identity on inspected paths.
- Request findings: customer requests require authenticated customer JWT authority, SQL order-item ownership, serializable item row locking, idempotency replay-shape matching, current eligibility, owned media, and payout snapshot capture. Customers cannot request foreign order items.
- Eligibility findings: eligibility derives from current SQL order item delivery/status, purchased quantity, existing active refund requests, settings, sub-order/payment states, and cumulative SQL refund totals. DTO quantities and amounts are intent only.
- Approval findings: SuperAdmin review owns approval/rejection/retry preparation. Approved refunds move into return-trigger workflow and cannot execute from request DTOs alone.
- Execution findings: execution requires SuperAdmin actor authority, locked refund request/refund/execution rows, return-trigger eligibility, vendor governance, outward-money readiness, current payment/order/earning reloads, payout-overlap checks, overrun validation, ledger posting, and reconciliation-required handling.
- Allocation findings: allocations derive from immutable marketplace order/sub-order/item financial snapshots and paid payment rows. Completion validates cumulative sub-order and payment refund totals before ledger mutation.
- Commission/earnings findings: refund reversals are bounded by SQL refund amount and order/sub-order commission/earning snapshots. Vendor earning reversals append adjustment rows or mark unpaid earnings reversed; paid payout history is not rewritten.
- Settlement/payout findings: processing or reconciliation-required payouts block refund execution. Completed payouts remain immutable; post-payout refund impact is modeled as debt/adjustment authority.
- Reconciliation findings: recovery reloads current SQL candidates and converges through status, idempotency, ledger fingerprints, and reconciliation-required states. Reconciliation cannot create value without re-entering refund execution authority.
- Tenant-isolation findings: customer APIs filter by authenticated customer id; vendor/tenant reads use trusted tenant context; SuperAdmin surfaces are platform-scoped. Runtime mutation reloads SQL order/sub-order/vendor lineage before changing refund or financial state.
- Concurrency findings: request creation and execution run under serializable transactions with row locks and database uniqueness. Concurrent replay collapses to existing SQL rows or fails closed.
- Historical-consistency findings: refund request, execution, financial refund, evidence, audit, return, ledger, and reconciliation rows form durable SQL lineage. Reruns use current SQL and idempotency rather than projections or stale snapshots.
- Adversarial findings: direct DTO refund mutation was the active approval-bypass risk and is now disabled. Refund replay, duplicate execution, over-refund, tenant guessing, route tampering, payout corruption, settlement drift, projection poisoning, and reconciliation-created value are blocked or routed to fail-closed/reconciliation-required states on inspected paths.

## Runtime Hardening Performed

- Disabled direct external `api/commercehub/refunds` create/confirm/fail mutation with `REFUND_WORKFLOW_REQUIRED` responses.
- Preserved `CommerceHubRefundService` for existing internal bounded refund/recovery controls while preventing HTTP callers from using tenant context plus DTO amount/reference to bypass customer request approval and SuperAdmin execution.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/CommerceHubRefundsController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubRefundCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/commercehub-refund-authority-certification-findings.md`

## Explicit Answers

- Who owns refund authority? PostgreSQL refund workflow truth: refund requests, refund rows, execution rows, evidence, return workflow, marketplace order/sub-order/item snapshots, payment rows, vendor earnings, ledger rows, payout state, and reconciliation records under the refund services.
- Can refunds be replayed? Replay cannot create extra refund value on inspected paths; it collapses through idempotency, row locks, terminal states, ledger fingerprints, or reconciliation-required handling.
- Can over-refunds occur? No inspected path allows over-refunds; request quantity and completion amount are bounded by SQL item, sub-order, and payment totals.
- Can approval be bypassed? The active direct HTTP bypass was closed; external execution now must go through customer request plus SuperAdmin approval/execution workflow.
- Can execution be bypassed? No inspected path remains; direct create/confirm/fail HTTP mutation is disabled and execution is routed through `RefundExecutionService` checks.
- Can allocations diverge from payment truth? No on inspected paths; allocation and completion use SQL order/sub-order/item snapshots plus paid payment rows and cumulative refund validation.
- Can refunds corrupt earnings? No on inspected paths; earnings reversals are SQL-derived, bounded, idempotent, and append adjustment/reversal truth.
- Can refunds corrupt payouts? No on inspected paths; processing payouts block refunds and completed payouts are immutable.
- Can reconciliation create value? No; reconciliation can recover current SQL candidates or mark ambiguity, not create value outside refund authority.
- Can tenant leakage occur? No inspected path exposes or mutates foreign refund authority after SQL/customer/vendor/SuperAdmin scoping.
- Were runtime code changes required? Yes. Direct CommerceHub refund HTTP mutation was disabled.
- Is CommerceHub Refund Authority production-certifiable? Yes, for inspected runtime authority paths. Broader public-launch operations evidence remains outside this runtime certification.
