# PASS 16.5E Refund & Financial Reversal Certification Findings

Date: 2026-06-02

## Verdict

CommerceHub refund and financial-reversal runtime is production-certifiable for the inspected paths under PostgreSQL and ledger authority. Public launch remains **NOT READY** until the broader PASS 15.6/16.1B deployment, retention, alert-pack, dashboard, backup/restore, SMTP/session, and PostgreSQL-backed chaos blockers are closed.

No runtime code hardening was required in this pass. Existing refund, return, payout, settlement, ledger, reconciliation, and recovery controls already enforce the required lifecycle boundaries.

## 1. Refund Inventory

| Domain object | Authority owner | Source of truth | Derivative consumers |
|---|---|---|---|
| `CommerceHubRefundRequest` | Customer request plus Super Admin review authority | PostgreSQL | Customer refund page, Super Admin refund queue/details, return workflow |
| `CommerceHubReturnRequest` | Super Admin logistics plus POS Admin receipt authority | PostgreSQL | Customer returns, POS/Admin return screens, Super Admin refund details |
| `CommerceHubRefund` | Refund execution and financial reversal authority | PostgreSQL | Finance dashboards, reconciliation, customer/Super Admin status views |
| `CommerceHubRefundExecution` | Super Admin manual payout evidence authority | PostgreSQL | Super Admin refund details, audit reconstruction, reconciliation |
| `CommerceHubRefundEvidenceMedia` | Customer evidence attachment authority | PostgreSQL metadata plus configured object storage | Customer and Super Admin refund evidence views |
| `CommerceHubFinancialTransaction` and ledger entries | Financial reporting and balance authority | PostgreSQL append-only ledger | Merchant balances, settlements, payouts, dashboards, reconciliation |
| `AuditLog` and Super Admin audit evidence | Operator/customer action evidence | PostgreSQL audit tables | Forensics, incident reconstruction, dashboards |

## 2. POS Admin Findings

POS Admin return authority is limited to tenant-owned return handling through `PosAdminReturnsController` and `ReturnManagementService`. The tenant is resolved from `_tenantContext.RequiredTenantId`, not from request payloads. `ConfirmReceiptAsync` rejects cross-vendor returns through `returnRequest.VendorId != vendorId`, runs under serializable transactions, validates transition policy, records `RETURN_CONFIRMED`, and routes inventory receipt through `IInventoryConsistencyService`.

## 3. Super Admin Findings

Super Admin owns refund review, rejection, retry preparation, logistics/override-style return actions, and manual refund execution. Execution requires a valid Super Admin actor, audited request evidence, return-trigger eligibility, vendor governance approval, outward-money readiness, serializable row locks, and financial state validation. Super Admin does not silently mutate order state outside the refund/return lifecycle.

## 4. Refund Lifecycle Findings

Customer request states are `Requested`, `UnderReview`, `Approved`, `Rejected`, `Cancelled`, `WaitingForReturn`, `AwaitingPayout`, `Processing`, `Failed`, and `Paid`. Financial refund states are `Pending`, `Processing`, `Completed`, `Failed`, and `ReconciliationRequired`. Approved refunds move into return-driven workflow; return trigger policy unlocks payout; execution idempotently collapses already-paid states.

Forbidden transitions are blocked by `CommerceHubRefundRequestWorkflowPolicy`, `CommerceHubReturnPolicy`, serializable locks, terminal-state checks, and reconciliation states.

## 5. Financial Reversal Findings

Refund execution mutates only SQL/ledger-backed financial facts: refund rows, payment refunded amount, vendor earnings/adjustments, platform cash/payables/debt effects, order/sub-order refund status, ledger entries, accounting/outbox evidence, and audit records. `RefundFinancialCalculator`, `RefundFinancialDecisionEngine`, `ICommerceHubFinancialSafetyGuardService`, and `ICommerceHubFinancialLedgerService.RecordRefundAsync` bind refund math to commission, promo discount, vendor earning, debt, platform cash, and payable deltas.

## 6. COD Refund Findings

COD cash remains merchant-held. COD refunds reverse platform commission debt instead of assuming the platform holds customer cash. Debt reduction is bounded by current vendor debt and cannot reduce debt below zero.

## 7. Online Payment Findings

Online-payment refunds reduce platform-held cash and reverse vendor payables before payout. After payout, refund impact becomes post-payout debt/adjustment authority rather than rewriting completed payout history.

## 8. Settlement Findings

Refunds before, during, or after settlement remain ledger-derived. Serializable refund transactions, financial state capture, refund-overrun validation, and reconciliation checks prevent settlement drift from becoming authority.

## 9. Payout Findings

Refund completion blocks overlap with processing payouts through `EnsureNoProcessingPayoutForRefundEarningsAsync`. Completed payouts are immutable history; post-payout refund impact is modeled as debt or adjustment authority.

## 10. Reconciliation Findings

`FinancialReconciliationService` validates orders, sub-orders, refunds, orphan ledgers, external money lifecycle, payout ledger debits, platform cash debits, vendor payout debits, and aggregate consistency. The provable chain is original order/payment/earning fact plus refund ledger fact equals current financial truth.

## 11. Dashboard Findings

Customer, POS Admin, Super Admin, and finance views consume SQL-backed refund/return/ledger state. They are not authority. Dashboard drift remains operationally possible only as display lag; certified mutation paths re-read SQL before action.

## 12. Recovery Findings

`CommerceHubRefundRecoveryHostedService`, `CommerceHubRefundService.ReconcileProcessingRefundsAsync`, and `RefundExecutionService.RecoverReconciliationRequiredRefundsAsync` recover only current SQL candidates. Duplicate recovery collapses through status checks, idempotency keys, row locks, ledger fingerprints, and reconciliation-required states.

## 13. Auditability Findings

Customer request creation, payout-detail snapshot use, Super Admin approval/rejection/retry/execution, return transitions, refund financial audit, execution evidence, and ledger rows provide reconstructable incident history. Evidence is SQL-first; external object storage supports media payloads.

## 14. Chaos Findings

Inspected controls cover double refund, double reversal, refund resurrection, return resurrection, refund during payout, post-payout refund, settlement drift, negative debt/payable/platform cash checks, cross-tenant return authority, replay duplication, recovery duplication, dashboard mismatch, and external outcome uncertainty.

## 15. Monitoring Findings

Runtime contains reconciliation jobs, financial audit records, payout/refund recovery workers, and health/reconciliation surfaces. Remaining launch debt is deployment-verified refund alert packs, external-truth dashboards, and a PostgreSQL-backed chaos harness.

## 16. Runtime Hardening Performed

No runtime code hardening was required. The inspected runtime already contained the required serializable locks, authority boundaries, readiness gates, idempotency, payout overlap blocking, ledger fingerprinting, and reconciliation states.

## 17. Regression Freeze Added

Added:

- `CommerceHubRefundCertificationTests`
- `CommerceHubFinancialReversalCertificationTests`
- `CommerceHubReturnAuthorityCertificationTests`

These freeze refund authority, return authority, execution idempotency, financial reversal behavior, refund/payout interaction, refund/settlement interaction, reconciliation, recovery, auditability, and dashboard authority assumptions.

## 18. Architecture Updates

Updated `.ai-context` with PASS 16.5E refund/reversal authority, risk, hardening, and freeze rules.

## 19. Remaining Debt

- Deployment-verified refund/reversal alert packs and dashboards.
- PostgreSQL-backed chaos tests for concurrent refund, return, payout, settlement, replay, and recovery races.
- Production query-plan/index review for refund queues and reconciliation reports.
- External WORM/SIEM retention and evidence delivery from broader PASS 15.6.
- Backup/restore/operator rehearsal evidence from broader production readiness.

## Required Answers

1. Return authority is owned by Super Admin for review/logistics/override actions and POS Admin for tenant-owned receipt confirmation.
2. Refund authority is owned by Super Admin for customer-request approval/rejection/manual execution and by backend SQL refund services for financial execution.
3. Refunds cannot execute twice on certified paths without collapsing through idempotency, terminal-state checks, row locks, and ledger fingerprinting.
4. Refunds cannot corrupt merchant balances on certified paths because reversal deltas are SQL/ledger-derived and financial state is validated.
5. Refunds cannot corrupt settlements on certified paths because reconciliation and ledger authority prove refund impacts independently of derivative state.
6. Refunds cannot corrupt payouts on certified paths because processing payout overlap is blocked and completed payouts are not rewritten.
7. Replay/recovery cannot duplicate refunds on certified paths because recovery reloads current SQL and uses status/idempotency/ledger guards.
8. Refund incidents can be reconstructed from refund request, return request, refund execution, audit, evidence, ledger, and reconciliation records.
9. Refund authority is explicit in controllers, services, workflow policies, boundary scopes, and regression freezes.
10. CommerceHub refund lifecycle runtime is production-certifiable for inspected paths, but CommerceHub public launch remains **NOT READY** until broader deployment and operations blockers are closed.
