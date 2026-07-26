# CERT-1 C1 Payment Settlement & Financial Finalization Certification

Certification: CERT-1  
Phase: C  
Control: C1 - Payment Settlement & Financial Finalization Certification  
Scope: Payment settlement and financial finalization only  
Certification Date: 2026-07-20  
Source of Truth: Source code implementation only

## Final Certification

PASS WITH OBSERVATIONS

## Source Evidence

- `PointOfSale.API/Controllers/PaymentLifecycleController.cs`
- `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Core/Entities/PaymentSettlementBatch.cs`
- `PointOfSale.Core/Entities/PaymentSettlementOrder.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`
- `PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs`
- `PointOfSale.Infrastructure/Data/Configurations/AccountingOutboxMessageConfiguration.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs`
- `PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs`
- `PointOfSale.Core/Entities/CommerceHubVendorEarning.cs`
- `PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinancialAuditService.cs`
- `PointOfSale.Core/Entities/CommerceHubFinancialAuditLog.cs`
- `PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs`
- `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminFinanceController.cs`

## Executive Summary

Settlement ownership is implemented in two distinct source-backed boundaries.

Standard storefront settlement is owned by `PaymentLifecycleService` through `RecordOfflineSettlementAsync` and `RecordGatewaySettlementAsync`. Entry is the admin-only `PaymentLifecycleController` settlement routes. It starts from already collected orders, creates a `PaymentSettlementBatch` and `PaymentSettlementOrder` rows, transitions included orders from `PaymentStatus.Collected` to `PaymentStatus.Settled`, stages settlement accounting outbox, publishes settlement completion events, and commits.

CommerceHub financial finalization is owned by `CommerceHubFinancialWorkflowService`, entered from `CommerceHubPaymentService` after online payment collection or from `CommerceHubOrderManagementService` when a COD sub-order is marked delivered. It applies persisted commission snapshots, creates or progresses vendor earnings, records platform commission and vendor/platform financial ledger rows through `CommerceHubFinancialLedgerService`, stages accounting outbox for each ledger transaction, and records financial audit rows.

General ledger posting is not owned by settlement/finalization. The implemented boundary ends at local settlement/finance truth plus accounting outbox staging. `AccountingOutboxProcessor` later posts to accounting services and marks outbox rows processed.

## Settlement Entry

Implemented standard settlement entry:

1. Admin calls `POST /api/payments/lifecycle/settlements/cod` or `POST /api/payments/lifecycle/settlements/gateway` in `PaymentLifecycleController` (`RecordCodSettlement` lines 217-253, `RecordGatewaySettlement` lines 264-330).
2. Controller delegates to `IPaymentLifecycleService` (`RecordGatewaySettlementAsync` contract lines 102-110; COD controller calls `RecordOfflineSettlementAsync`).
3. `PaymentLifecycleService` requires settlement id, tenant, currency, positive gross amount, non-negative fees, resolved order set, matching gross amount, and `PaymentStatus.Collected` before settlement mutation (`PaymentLifecycleService` lines 330-427 and 569-686).

Implemented CommerceHub financial finalization entry:

1. Online payment success in `CommerceHubPaymentService` marks `CommerceHubPayment` paid, marks marketplace/sub-orders collected, then calls `ApplyOnlinePaymentSuccessAsync` (`CommerceHubPaymentService` lines 1973-2023).
2. COD finalization starts when `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync` moves a sub-order to `Delivered`; inside the same serializable transaction it calls `ApplyDeliveredFinancialsAsync` (`CommerceHubOrderManagementService` lines 72-73 and 190-217).
3. `CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected` marks COD sub-order payment collected during delivered financial workflow (`CommerceHubPaymentLifecycleAuthority` lines 58-84).

## Settlement Pipeline

Standard settlement pipeline:

1. Admin settlement command.
2. Settlement request validation and order resolution.
3. Existing settlement batch lookup.
4. Idempotent replay accepted only when existing batch contents match.
5. Orders must be `PaymentStatus.Collected`.
6. Collection accounting must already be posted for all included orders.
7. `PaymentSettlementBatch` and child `PaymentSettlementOrder` rows are created.
8. Included orders transition to `PaymentStatus.Settled`.
9. Settlement accounting outbox is staged.
10. Settlement completion event is published.
11. Transaction commits.
12. Best-effort post-commit accounting posting re-reads SQL truth; outbox remains retryable on failure.

CommerceHub financial finalization pipeline:

1. Payment success or COD delivery enters financial workflow.
2. Payment lifecycle is marked collected where applicable.
3. Financial snapshot is applied or validated.
4. Vendor earning is created if no original earning exists.
5. Pending earning is progressed to available on delivery.
6. Commission, earning, and collection audit rows are appended.
7. Ledger service records vendor and platform financial transactions.
8. Ledger service stages `CommerceHubLedgerPosting` accounting outbox rows for each transaction.
9. Caller-owned transaction saves and commits.

## Financial Ownership

- Settlement owner: `PaymentLifecycleService` for standard `PaymentSettlementBatch`/`PaymentSettlementOrder` settlement; `CommerceHubFinancialWorkflowService` for CommerceHub payment-collected financial finalization.
- Ledger owner: `CommerceHubFinancialLedgerService` creates `CommerceHubFinancialTransaction` rows and accounting outbox rows.
- Vendor earnings owner: `CommerceHubFinancialWorkflowService.EnsureEarningRecord` creates and progresses `CommerceHubVendorEarning`.
- Commission owner: CommerceHub commission facts are persisted on sub-order snapshot fields by the financial workflow and recorded as platform/vendor ledger transactions by `CommerceHubFinancialLedgerService.RecordOrderAsync`.
- Accounting outbox owner: `PaymentLifecycleService` calls `IAccountingOutboxService` for standard settlement outbox; `CommerceHubFinancialLedgerService` directly adds `AccountingOutboxMessage` rows for CommerceHub ledger postings.
- Financial audit owner: `CommerceHubFinancialAuditService` appends `CommerceHubFinancialAuditLog` rows for payment, COD collection, commission, earning, refund, payout, and reconciliation events.

## Transaction Boundaries

Standard settlement owns an explicit lifecycle transaction via `BeginLifecycleTransactionAsync` (`PaymentLifecycleService` lines 2618-2637). The transaction is owned or joined with a savepoint where an ambient transaction exists (`PaymentLifecycleService` lines 2639-2735).

Within that boundary, settlement batch creation, order status mutation, settlement outbox staging, event publishing, and `SaveChangesAsync` occur before `CommitAsync` (`PaymentLifecycleService` lines 429-492 and 688-764). Exceptions trigger rollback, with rollback failures logged without masking the original failure (`PaymentLifecycleService` lines 494-509 and 766-781).

CommerceHub online payment finalization runs inside the serializable transaction opened by `CommerceHubPaymentService`; payment state, financial workflow effects, canonical events, `SaveChangesAsync`, and commit are ordered in one transaction (`CommerceHubPaymentService` lines 1656, 1973-2023, and 2068-2156).

CommerceHub COD delivered finalization runs inside the serializable transaction opened by `CommerceHubOrderManagementService`; delivery inventory commits, sub-order status mutation, history, financial workflow, events, `SaveChangesAsync`, and commit are atomic (`CommerceHubOrderManagementService` lines 72-73 and 135-217). Catch blocks rollback on any failure (`CommerceHubOrderManagementService` lines 219-225).

`AccountingOutboxService` enforces that accounting outbox enqueue happens inside an active transaction and fails if business mutations are pending without one (`AccountingOutboxService` lines 867-889). Its enqueue method saves the outbox row while the caller transaction is active (`AccountingOutboxService` lines 747-752).

## Idempotency

Standard settlement duplicate prevention is implemented by:

- Existing batch replay check: same settlement id returns success only if batch contents match; mismatched duplicate settlement id is rejected (`PaymentLifecycleService` lines 386-412 and 644-670).
- Database uniqueness on `(TenantId, SettlementType, SettlementId)` for active `PaymentSettlementBatch` rows (`ApplicationDbContext` lines 806-820).
- Database uniqueness preventing an order from appearing in multiple active settlement orders by `(TenantId, OrderId)` and within a batch by `(TenantId, SettlementBatchId, OrderId)` (`ApplicationDbContext` lines 829-841).
- Settlement outbox idempotency keys `COD_SETTLEMENT:{tenantId}:{settlementId}` and `GATEWAY_SETTLEMENT:{tenantId}:{settlementId}` (`AccountingOutboxService` lines 461-472).
- Accounting outbox unique index `(TenantId, IdempotencyKey)` (`AccountingOutboxMessageConfiguration` lines 54-57).

CommerceHub finalization duplicate prevention is implemented by:

- Earning lookup by sub-order before creation (`CommerceHubFinancialWorkflowService` lines 144-153 and 482-535).
- Sub-order lock `IsEarningCreated` preventing recreation when a lock is already set without an earning row (`CommerceHubFinancialWorkflowService` lines 502-512).
- Filtered unique index on `CommerceHubVendorEarning.SubOrderId` for active original earnings (`ApplicationDbContext` lines 2491-2511).
- Ledger replay checks by idempotency key and by financial fingerprint before adding a transaction (`CommerceHubFinancialLedgerService` lines 712-724 and 773-833).
- Unique ledger indexes on idempotency key and vendor/platform financial fingerprints (`CommerceHubFinancialTransactionConfiguration` lines 49-80).
- Duplicate ledger races are handled by rollback, fingerprint verification, and detaching duplicate pending transaction/outbox entities (`CommerceHubFinancialLedgerService` lines 881-961).

## Audit Evidence

Standard settlement evidence:

- `PaymentSettlementBatch` records settlement type, settlement id, provider, payout batch id, gross amount, fees, currency, bank reference, order count, and settled timestamp (`PaymentSettlementBatch` lines 9-33).
- `PaymentSettlementOrder` records immutable order-to-settlement membership, payment intent id, order total, and currency (`PaymentSettlementOrder` lines 9-27).
- Settlement completion events include correlation id and idempotency key (`PaymentLifecycleService` lines 468-483 and 734-755).
- Accounting outbox rows capture event type, source type, business entity id, payload, idempotency key, occurred time, optional aggregate version, processing state, journal id, retry state, and correlation id (`AccountingOutboxMessage` lines 22-153).

CommerceHub financial evidence:

- `CommerceHubFinancialAuditLog` stores tenant, marketplace order, sub-order, payment, earning, payout, refund, type, debit, credit, currency, description, correlation id, and created timestamp (`CommerceHubFinancialAuditLog` lines 8-24).
- `CommerceHubFinancialAuditService` records payment collected, commission deducted, vendor earning created, vendor earning available, COD collected, refund, payout, and finance reconciliation audit rows (`CommerceHubFinancialAuditService` lines 140-230, 310-329, and 448-466).
- `CommerceHubFinancialTransaction` requires positive amount, EGP currency, entity scope, reference id, idempotency key, correlation id, and created timestamp (`CommerceHubFinancialTransaction` lines 32-150).
- CommerceHub ledger outbox payload includes transaction id, type, direction, entity type, bucket, amount, currency, description, and occurred timestamp (`CommerceHubFinancialLedgerService` lines 724-755).

Observation: `CommerceHubFinancialWorkflowService` passes `null` correlation id for several financial audit calls (`RecordPaymentCollected`, `RecordCommissionDeducted`, `RecordVendorEarningCreated`, `RecordVendorEarningAvailable`, `RecordCodCollected`; lines 108-123, 185-204, 209-215, 546-553). Ledger transactions still receive default correlation ids through `CommerceHubFinancialTransaction.Create`, but financial audit log correlation can be absent for those workflow-generated audit rows.

## Recovery

Standard settlement recovery is implemented through retryable accounting outbox state and SQL-truth re-entry:

- Post-commit accounting failure is logged and settlement remains committed; outbox remains retryable (`PaymentLifecycleService` lines 512-529 and 784-800).
- Settlement accounting posting re-loads the settlement batch and required outbox by idempotency key before posting, then marks the outbox processed (`PaymentLifecycleService` lines 5468-5571 and 5880-5894).
- `AccountingOutboxProcessor` leases pending messages, respects earlier aggregate ordering blockers, dispatches settlement and CommerceHub ledger events, marks processed on success, and retries critical financial truth events non-terminally on failure (`AccountingOutboxProcessor` lines 130-300, 335-372, 515-569, and 706-740).
- `AccountingOutboxMessage` supports claim, processed marking, exponential backoff, and non-terminal financial failure retry state (`AccountingOutboxMessage` lines 200-260).

CommerceHub reconciliation entry points are implemented:

- Hourly Hangfire job `commercehub-finance-reconciliation` runs `CommerceHubFinanceReconciliationJob.ExecuteAsync` (`HangfireInitializationHostedService` lines 290-293).
- The job calls `IFinancialReconciliationService.RunAsync`, logs OK/mismatch, and resolves linked unresolved captured payments under serializable transactions (`CommerceHubFinanceReconciliationJob` lines 71-105 and 107-220).
- `FinancialReconciliationService.RunAsync` validates marketplace orders, sub-orders, promos, refunds, orphan ledgers, external money lifecycle, aggregate consistency, writes a reconciliation report, records reconciliation audit, saves, and triggers alert hooks for warning/critical reports (`FinancialReconciliationService` lines 68-130).
- Super Admin reconciliation read endpoint exposes the current reconciliation snapshot (`SuperAdminFinanceController` lines 365-382; `SuperAdminFinanceOverviewService` lines 270-292).

## Runtime Boundary

Settlement and financial finalization do not own checkout, provider protocol execution, refund execution, payout execution, GL posting, or marketplace reporting.

Implemented boundaries:

- Checkout and payment initiation are outside this C1 scope.
- Provider webhook processing and payment completion occur before CommerceHub financial workflow entry; C1 starts only after payment success is admitted or COD delivery is recorded.
- Standard settlement starts only after orders are already `PaymentStatus.Collected`.
- General ledger posting is asynchronous consumer work through `AccountingOutboxProcessor`, not the settlement mutation boundary.
- Refund and payout services are separate runtime boundaries; C1 only records financial facts that become inputs to those workflows.
- Marketplace reporting and Super Admin dashboards consume finance/reconciliation read models; they do not own settlement or finalization mutation.

## Critical Invariants

Implementation-backed invariants:

- Standard settlement rejects orders that are not `PaymentStatus.Collected` before settlement mutation.
- Standard settlement rejects settlement gross amount mismatches against resolved order totals.
- Standard settlement requires posted collection accounting journals before settlement.
- Standard settlement id is unique per tenant and settlement type.
- A standard order can belong to only one active settlement order row.
- Existing standard settlement replay is accepted only when settlement contents match exactly.
- Settlement changes order status to `PaymentStatus.Settled` inside an authorized lifecycle mutation context.
- CommerceHub financial workflow does not recreate payment; it receives an already paid `CommerceHubPayment` or COD delivered sub-order.
- CommerceHub financial workflow fails closed when no complete stored financial snapshot or recoverable stored/item financial fields exist.
- CommerceHub financial workflow validates commission snapshot JSON against persisted financial fields before creating earnings or ledger entries.
- Available/non-pending earnings cannot be repriced or reassigned to a different payment.
- CommerceHub cannot create more than one active original vendor earning per sub-order.
- CommerceHub ledger transactions require positive EGP amounts and valid entity scope.
- CommerceHub ledger duplicates are collapsed only when idempotency key or financial fingerprint matches.
- Every CommerceHub financial transaction created through the ledger service stages a `CommerceHubLedgerPosting` accounting outbox row in the same database context.

## Architecture Boundaries

Implemented ownership:

- Standard settlement owns `PaymentSettlementBatch`, `PaymentSettlementOrder`, standard order transition to `PaymentStatus.Settled`, standard settlement accounting outbox, and standard settlement completion events.
- CommerceHub financial finalization owns payment-collected financial effects, COD collected financial effects, vendor earnings, platform commission ledger facts, vendor/platform financial ledger rows, CommerceHub ledger accounting outbox staging, and financial audit evidence.
- Accounting outbox processing owns asynchronous GL posting and retry/claim state after outbox creation.
- Reconciliation owns mismatch detection, reconciliation reports, finance reconciliation audit, and unresolved captured-payment recovery entry points.

Implemented non-ownership:

- Settlement does not own checkout.
- Settlement does not own pricing recalculation or coupon authority.
- Settlement does not own inventory authority.
- Settlement does not own provider communication or webhook verification.
- Settlement does not own refund execution.
- Settlement does not own payout execution.
- Settlement does not own final GL journal posting.
- Settlement does not own marketplace reporting.

## Architecture Risks

Implementation-backed observations:

- CommerceHub financial audit correlation is partial. The audit service supports `CorrelationId`, but workflow-created payment, commission, COD collection, and earning audit calls pass `null`; incident reconstruction still has entity ids and ledger/outbox correlation, but not every audit row has request correlation.
- Settlement/finalization is intentionally split between standard `PaymentLifecycleService` settlement and CommerceHub financial workflow. This is implemented behavior and not a violation, but downstream consumers must not assume a single settlement owner for both standard orders and CommerceHub vendor earnings.

## Certification Checklist

| Area | Result |
| --- | --- |
| Settlement Entry | PASS |
| Settlement Pipeline | PASS |
| Financial Ownership | PASS |
| Transaction Boundaries | PASS |
| Idempotency | PASS |
| Audit Evidence | PASS |
| Recovery | PASS |
| Runtime Boundary | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS WITH OBSERVATIONS |

## Final Certification Result

PASS WITH OBSERVATIONS
