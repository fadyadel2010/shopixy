# CERT-1 C3 Payout Runtime Certification

Certification: CERT-1  
Phase: C  
Control: C3 - Payout Runtime Certification  
Scope: Payout runtime only  
Certification Date: 2026-07-20  
Source of Truth: Source code implementation only

## Final Certification

PASS WITH OBSERVATIONS

## Source Evidence

- `PointOfSale.API/Controllers/CommerceHubPayoutsController.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs`
- `PointOfSale.API/Controllers/VendorPayoutMethodsController.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutMethodsService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialSafetyGuardService.cs`
- `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs`
- `PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubFinancialAuditService.cs`
- `PointOfSale.API/Configuration/CommerceHubPayoutSettings.cs`
- `PointOfSale.Core/Entities/CommerceHubPayout.cs`
- `PointOfSale.Core/Entities/CommerceHubPayoutItem.cs`
- `PointOfSale.Core/Entities/CommerceHubVendorEarning.cs`
- `PointOfSale.Core/Enums/CommerceHubPayoutStatus.cs`
- `PointOfSale.Core/Enums/CommerceHubVendorEarningStatus.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/AccountingOutboxMessageConfiguration.cs`

## Executive Summary

Payout ownership is implemented in the CommerceHub vendor payout runtime.

The canonical payout lifecycle owner is `CommerceHubPayoutService`. It creates payout reservations, transitions payouts to processing, completes payouts, fails payouts, mutates reserved vendor earnings, records financial audit evidence, calls the CommerceHub financial ledger, and commits payout runtime changes in serializable SQL transactions (`CommerceHubPayoutService` lines 109-357, 359-607, and 739-822).

The active execution entry is the Super Admin payout execution endpoint, `POST /api/super-admin/payouts/{vendorId}/execute`. `SuperAdminPayoutsController.Execute` delegates to `SuperAdminPayoutOrchestrationService.ExecuteAsync`, which validates actor identity, payout amount, payout method ownership, idempotency, eligible earnings, then delegates creation, processing, and completion to `ICommerceHubPayoutService` (`SuperAdminPayoutsController` lines 80-117; `SuperAdminPayoutOrchestrationService` lines 47-134).

Vendor self-service payout creation is also implemented through `POST /api/commercehub/payouts`. That endpoint creates a pending payout reservation for the current tenant only; direct tenant-facing processing, completion, and failure mutation endpoints are disabled and return `PAYOUT_EXECUTION_WORKFLOW_REQUIRED` (`CommerceHubPayoutsController` lines 46-75 and 105-155).

Vendor money release is locally owned by the Super Admin execution workflow plus the canonical payout service. The implementation does not call an external payout rail. It requires transfer references and explicit executed-transfer confirmation, records a committed external handoff token, and recognizes completion into SQL ledger truth (`SuperAdminPayoutOrchestrationService` lines 93-124; `CommerceHubPayoutService` lines 442-458 and 518-604).

Payout runtime ends when the payout service commits durable local payout state, vendor earning state, ledger rows, accounting outbox rows, financial audit rows, and committed operation audit. It does not own checkout, payment runtime, settlement, refund runtime, accounting consumer journal posting, or marketplace reporting.

## Payout Entry

Implemented payout entries:

1. Vendor payout method registration starts at `POST /api/vendors/payout-methods`; it stores vendor-scoped payout destinations but does not release money (`VendorPayoutMethodsController` lines 44-75; `CommerceHubVendorPayoutMethodsService` lines 47-126).
2. Vendor pending payout creation starts at `POST /api/commercehub/payouts`, derives the vendor from trusted tenant context, requires request body and idempotency key, and delegates to `CommerceHubPayoutService.CreatePayoutAsync` (`CommerceHubPayoutsController` lines 46-75; `CommerceHubPayoutService` lines 109-140).
3. Super Admin execution starts at `POST /api/super-admin/payouts/{vendorId}/execute`, requires Super Admin actor identity through the base controller context, and delegates to `SuperAdminPayoutOrchestrationService.ExecuteAsync` (`SuperAdminPayoutsController` lines 80-117; `SuperAdminPayoutOrchestrationService` lines 47-78 and 295-306).
4. Payout queue visibility starts at `GET /api/super-admin/payouts`; it computes payout eligibility snapshots but does not mutate payout state (`SuperAdminPayoutsController` lines 27-78; `SuperAdminPayoutsService` lines 41-151).

Disabled direct execution entry:

- `POST /api/commercehub/payouts/{payoutId}/processing`, `/complete`, and `/fail` return `PAYOUT_EXECUTION_WORKFLOW_REQUIRED` instead of invoking `ICommerceHubPayoutService` mutation methods (`CommerceHubPayoutsController` lines 105-155).

No scheduled payout entry, automatic vendor withdrawal execution, or external payout provider callback entry was identified in the audited implementation. The implemented automatic worker is reconciliation of stale `Processing` payouts, not payout initiation (`CommerceHubPayoutReconciliationHostedService` lines 28-64 and 66-163).

## Payout Pipeline

Implemented Super Admin execution pipeline:

1. Operator calls `POST /api/super-admin/payouts/{vendorId}/execute` with amount, payout method id, transfer references, and idempotency key.
2. `SuperAdminPayoutOrchestrationService` normalizes positive amount, requires idempotency key, requires actor identity, normalizes transfer evidence, and binds payout method id to the target vendor (`SuperAdminPayoutOrchestrationService` lines 66-77, 177-204, 308-340, and 343-363).
3. The orchestration service selects canonical available EGP earning ids for the requested amount, including available negative adjustments in the target calculation (`SuperAdminPayoutOrchestrationService` lines 206-266).
4. The orchestration service calls `CommerceHubPayoutService.CreatePayoutAsync`.
5. Creation validates idempotency, governance, outward-money readiness, selected earning locks, available earning status, payout delay, single currency, EGP authority, negative adjustment netting, positive net amount, and ledger confirmation (`CommerceHubPayoutService` lines 118-290).
6. Creation writes a `CommerceHubPayout` in `Pending`, assigns `ExternalTransferIdempotencyKey`, creates `CommerceHubPayoutItem` rows, and reserves earnings by moving them to `InPayout` with `PayoutId` and reserved amounts (`CommerceHubPayoutService` lines 292-329).
7. Creation saves and commits the serializable transaction (`CommerceHubPayoutService` lines 331-356).
8. Orchestration calls `MarkProcessingAsync` with transfer evidence and the POS-issued external transfer idempotency key.
9. Processing locks the payout row, blocks terminal/idempotent replays, checks readiness, blocks refund/return overlap, captures financial state, enforces one external payout slot per vendor, stores processing evidence, commits handoff token and handoff timestamp, and commits the serializable transaction (`CommerceHubPayoutService` lines 371-459).
10. Orchestration verifies committed external handoff and calls `MarkCompletedAsync` with the handoff token and `ExternalTransferConfirmedExecuted = true` (`SuperAdminPayoutOrchestrationService` lines 100-124 and 268-280).
11. Completion locks the payout row, requires `Processing` or `ReconciliationRequired`, requires executed-transfer confirmation, validates handoff token, captures financial state, validates terminal external evidence, verifies reserved earnings still belong to the payout, and applies completed allocation to each earning (`CommerceHubPayoutService` lines 471-537).
12. Completion sets payout status to `Completed`, records completion timestamp, clears failure reason, writes completion evidence fingerprint, and records payout financial audit (`CommerceHubPayoutService` lines 539-555).
13. `CommerceHubFinancialLedgerService.RecordPayoutAsync` records three payout ledger transactions: platform cash debit, platform payout liability debit, and vendor payable debit (`CommerceHubFinancialLedgerService` lines 465-525).
14. Each ledger transaction stages a `CommerceHubLedgerPosting` accounting outbox message in the same EF context (`CommerceHubFinancialLedgerService` lines 724-755).
15. The safety guard verifies the committed state delta: platform cash decreases by payout amount, vendor payable decreases by payout amount, vendor debt is unchanged, and exactly three correlated ledger entries exist (`CommerceHubPayoutService` lines 574-586; `CommerceHubFinancialSafetyGuardService` lines 142-167 and 293-340).
16. The safety guard appends committed operation audit, then the payout service saves and commits (`CommerceHubPayoutService` lines 588-604; `CommerceHubFinancialSafetyGuardService` lines 169-207).

Implemented vendor-created pending payout pipeline:

- Vendor creation uses the same `CommerceHubPayoutService.CreatePayoutAsync` reservation path but does not execute money movement because direct processing/completion endpoints are disabled (`CommerceHubPayoutsController` lines 105-155).

Implemented batching behavior:

- A payout may contain multiple earning allocations through `CommerceHubPayout.Items` and `CommerceHubPayoutItem` (`CommerceHubPayout` lines 27-28; `CommerceHubPayoutItem` lines 5-9).
- The implementation does not define a separate payout batch aggregate, scheduled batch job, or batch settlement table in the audited payout runtime.

## Payout Ownership

- Payout lifecycle owner: `CommerceHubPayoutService` owns creation, reservation, processing handoff, completion, failure, earning mutation, payout audit calls, ledger calls, and payout DTO mapping.
- Super Admin execution owner: `SuperAdminPayoutOrchestrationService` owns operator execution orchestration and delegates all canonical payout mutations to `ICommerceHubPayoutService` (`SuperAdminPayoutOrchestrationService` lines 79-124).
- Vendor balance eligibility owner: `CommerceHubVendorPayoutReadService` computes vendor payout queue snapshots from tenant rows, sub-orders, refunds, active refund requests, payout rows, and CommerceHub financial transactions (`CommerceHubVendorPayoutReadService` lines 112-607).
- Vendor payout method owner: `CommerceHubVendorPayoutMethodsService` owns vendor-scoped payout method creation/listing and validates vendor existence and destination values (`CommerceHubVendorPayoutMethodsService` lines 32-126).
- Ledger owner: `CommerceHubFinancialLedgerService.RecordPayoutAsync` records payout ledger facts (`CommerceHubFinancialLedgerService` lines 465-525).
- Accounting outbox owner: `CommerceHubFinancialLedgerService.AppendTransactionIfMissingAsync` stages accounting outbox messages for each ledger transaction (`CommerceHubFinancialLedgerService` lines 724-755).
- Financial audit owner: `CommerceHubFinancialAuditService.RecordPayoutCompleted` and `RecordPayoutFailed` write payout financial audit rows (`CommerceHubFinancialAuditService` lines 332-369).
- Reconciliation owner: `CommerceHubPayoutReconciliationHostedService` quarantines stale `Processing` payouts to `ReconciliationRequired` (`CommerceHubPayoutReconciliationHostedService` lines 66-163).

## Transaction Boundaries

Creation boundary:

- `CreatePayoutAsync` opens an `IsolationLevel.Serializable` transaction, rechecks idempotency inside the transaction, checks readiness, locks selected earnings, validates eligibility, creates payout and payout items, reserves earnings, saves, and commits (`CommerceHubPayoutService` lines 140-160 and 331-356).
- On idempotency unique conflict, creation rolls back, clears the change tracker, reloads the winning payout, and returns it (`CommerceHubPayoutService` lines 336-354).

Processing boundary:

- `MarkProcessingAsync` opens an `IsolationLevel.Serializable` transaction, locks the payout row with provider-specific row locking, validates transition and readiness, commits external handoff evidence and token, saves, and commits (`CommerceHubPayoutService` lines 371-459 and 1223-1313).

Completion boundary:

- `MarkCompletedAsync` opens an `IsolationLevel.Serializable` transaction, locks the payout row, validates handoff and evidence, applies earning completion, writes audit, writes ledger and accounting outbox rows, verifies financial deltas and ledger correlation, appends committed operation audit, saves, and commits (`CommerceHubPayoutService` lines 471-604).
- `RecordPayoutAsync` detects an existing EF transaction and does not open a second transaction when called from payout completion (`CommerceHubFinancialLedgerService` lines 865-872).

Failure boundary:

- `MarkFailedAsync` opens an `IsolationLevel.Serializable` transaction, locks the payout row, blocks failure of completed payouts, requires non-execution proof for externally locked payouts, releases reserved earnings, writes failure audit, saves, and commits (`CommerceHubPayoutService` lines 747-820).

Reconciliation boundary:

- `CommerceHubPayoutReconciliationHostedService` selects stale processing candidates outside mutation, then opens a serializable transaction, reloads each payout with row lock, revalidates current status and age, updates to `ReconciliationRequired`, writes `AuditLog`, saves, and commits (`CommerceHubPayoutReconciliationHostedService` lines 72-82 and 91-163).

Super Admin orchestration boundary:

- `SuperAdminPayoutOrchestrationService.ExecuteAsync` is an orchestration sequence across canonical service calls. It does not open one transaction across create, processing, and completion; each canonical service call owns its own serializable transaction (`SuperAdminPayoutOrchestrationService` lines 79-124; `CommerceHubPayoutService` lines 140, 371, and 471).

## State Machine

Implemented payout states are `Pending`, `Processing`, `Completed`, `Failed`, and `ReconciliationRequired` (`CommerceHubPayoutStatus` lines 3-10).

Implemented earning states used by payout are `Pending`, `Available`, `PaidOut`, `InPayout`, and `Reversed` (`CommerceHubVendorEarningStatus` lines 3-10).

Implemented payout transitions:

- Create: new payout is `Pending`; selected earnings become `InPayout` and point to the payout (`CommerceHubPayoutService` lines 292-329).
- Pending to Processing: `MarkProcessingAsync` requires outward readiness, no blocking refund/return lock, valid ledger-backed financial readiness, single processing/reconciliation slot per vendor, external transfer idempotency key, and a handoff reference (`CommerceHubPayoutService` lines 408-459 and 875-896).
- Processing or ReconciliationRequired to Completed: `MarkCompletedAsync` requires executed-transfer confirmation, handoff token match, terminal evidence, reserved earnings, ledger write, and verified financial state transition (`CommerceHubPayoutService` lines 504-604).
- Pending to Failed: `MarkFailedAsync` releases reserved earnings and marks payout failed (`CommerceHubPayoutService` lines 788-820).
- Processing or ReconciliationRequired to Failed: `MarkFailedAsync` requires handoff token match, failure reason, non-execution reference, explicit non-execution confirmation, and no external execution evidence (`CommerceHubPayoutService` lines 764-786 and 861-873).
- Processing to ReconciliationRequired: the hosted reconciliation worker quarantines stale processing payouts after the configured safe window (`CommerceHubPayoutReconciliationHostedService` lines 66-163).

Invalid or blocked transitions:

- Completed payouts cannot be failed or rolled back (`CommerceHubPayoutService` lines 751-757).
- Failed payouts cannot be completed (`CommerceHubPayoutService` lines 495-502).
- Completion cannot start from `Pending`; it requires `Processing` or `ReconciliationRequired` (`CommerceHubPayoutService` lines 504-510).
- Processing cannot claim an already executed transfer; executed evidence must go through completion (`CommerceHubPayoutService` lines 835-846).

Partial and full payout behavior:

- Vendor-level partial payout is implemented by selecting a subset of eligible earnings for a requested amount in Super Admin orchestration (`SuperAdminPayoutOrchestrationService` lines 206-266).
- Individual positive earning allocation is based on `Amount - PaidOutAmount - PayoutReservedAmount`; completion increments `PaidOutAmount` and returns the earning to `Available` if it is not fully paid out, otherwise marks it `PaidOut` (`CommerceHubPayoutService` lines 1038-1076).
- Full payout of selected allocations completes the payout and reduces vendor payable by the payout amount (`CommerceHubPayoutService` lines 539-604).

No `Cancelled` payout state and no explicit `Retry` payout state are implemented. Retry behavior is implemented as idempotent replay, failed payout release, and stale processing quarantine.

## Eligibility

Implemented eligibility checks:

- Vendor id must be present for snapshot and payout method operations (`CommerceHubVendorPayoutReadService` lines 76-98; `CommerceHubVendorPayoutMethodsService` lines 155-164).
- Vendor payout creation and processing require vendor payout governance to allow payout before new payout authority is created or handed off (`CommerceHubPayoutService` lines 138 and 367-369).
- Super Admin execution requires a positive amount (`SuperAdminPayoutOrchestrationService` lines 308-319).
- Super Admin execution requires the selected payout method id to belong to the target vendor and not be deleted (`SuperAdminPayoutOrchestrationService` lines 177-204).
- Creation requires at least one selected earning id and a payout idempotency key (`CommerceHubPayoutService` lines 118-130).
- Selected earnings must exist for the vendor, be `Available`, not deleted, and have elapsed `AvailableAtUtc` payout delay (`CommerceHubPayoutService` lines 162-208).
- A payout can only include one currency, and the currency must be EGP (`CommerceHubPayoutService` lines 210-227; `ApplicationDbContext` lines 2554-2556).
- Available negative vendor adjustments are automatically included in payout creation and netted against selected positive earnings (`CommerceHubPayoutService` lines 228-266).
- Net payout amount must be positive after negative adjustments (`CommerceHubPayoutService` lines 277-284).
- Every selected positive earning must have an order-payment credit ledger row and every negative refund adjustment must have a refund debit ledger row before payout proceeds (`CommerceHubPayoutService` lines 286-290 and 963-1035).
- Processing requires payout amount not to exceed ledger-backed vendor payable and requires platform cash to be sufficient (`CommerceHubPayoutService` lines 898-930).
- Read-side eligibility uses delivery completion, refund window, active refund requests, settlement delay, ledger payable, pending payouts, completed payouts, currency consistency, reconciliation flags, governance flags, and payout method visibility (`CommerceHubVendorPayoutReadService` lines 392-604).

Minimum payout behavior:

- No configured minimum payout threshold was identified beyond `Amount > 0` and positive net amount checks (`SuperAdminPayoutOrchestrationService` lines 308-319; `CommerceHubPayoutService` lines 277-284; `ApplicationDbContext` lines 2551-2553).

Payout method verification behavior:

- Payout method `IsVerified` is stored and surfaced to the queue, but Super Admin execution only enforces vendor/method binding and not-deleted status; no execution-time `IsVerified` gate was identified (`SuperAdminPayoutOrchestrationService` lines 177-204; `SuperAdminPayoutsService` lines 86-103 and 174-180).

## Idempotency

Duplicate payout prevention:

- Payout creation requires an idempotency key and returns an existing active payout for the same vendor and key (`CommerceHubPayoutService` lines 118-136 and 1090-1105).
- Creation rechecks idempotency inside the serializable transaction and handles database unique conflicts by rollback and replay of the winning payout (`CommerceHubPayoutService` lines 140-149 and 336-354).
- Database uniqueness enforces `{ TenantId, IdempotencyKey }` for payouts (`ApplicationDbContext` line 2582).

Duplicate execution prevention:

- Payout state transitions load and lock the payout row before mutation (`CommerceHubPayoutService` lines 1223-1313).
- One externally locked payout per vendor is enforced by service checks and a filtered unique index covering `Processing` and `ReconciliationRequired` (`CommerceHubPayoutService` lines 178-191 and 424-440; `ApplicationDbContext` lines 2577-2581).
- `ExternalTransferIdempotencyKey` is POS-issued and must be echoed by processing; a unique filtered index enforces uniqueness per tenant (`CommerceHubPayoutService` lines 641-673; `ApplicationDbContext` lines 2592-2595).
- `ExternalTransferHandoffToken` is POS-issued during committed processing handoff, must be echoed during completion/failure reconciliation, and is unique per tenant (`CommerceHubPayoutService` lines 1372-1414; `ApplicationDbContext` lines 2596-2599).

Duplicate evidence prevention:

- Active payout external reference id and bank transaction id uniqueness are checked across non-deleted, non-failed payouts with `IgnoreQueryFilters` (`CommerceHubPayoutService` lines 692-721).
- Database unique filtered indexes enforce live external reference id and live bank transaction id uniqueness (`ApplicationDbContext` lines 2584-2591).
- Completed payout evidence requires external reference id or bank transaction id, and completed state requires timestamps (`ApplicationDbContext` lines 2558-2562).
- Completed payout evidence fingerprint is computed from tenant, payout id, method, amount, currency, external reference id, and bank transaction id; replay with divergent evidence is rejected (`CommerceHubPayoutService` lines 675-689 and 724-737).

Duplicate ledger and outbox prevention:

- Ledger duplicate detection checks idempotency key and full financial fingerprint before adding a transaction (`CommerceHubFinancialLedgerService` lines 773-848).
- Ledger uniqueness is enforced by idempotency and financial fingerprint indexes (`CommerceHubFinancialTransactionConfiguration` lines 49-80).
- Duplicate ledger race handling rolls back, verifies the duplicate is exact, and detaches both pending transaction and associated accounting outbox message (`CommerceHubFinancialLedgerService` lines 865-984).
- Accounting outbox idempotency is unique per tenant and idempotency key (`AccountingOutboxMessageConfiguration` lines 54-57).

Replay protection:

- Completed payout replay returns the existing completed payout only after evidence matches stored values and completion fingerprint is valid (`CommerceHubPayoutService` lines 474-492).
- Processing replay for an already externally locked payout validates transfer idempotency key, unique evidence, stored evidence consistency, and handoff reference before returning (`CommerceHubPayoutService` lines 381-405).
- Failed payout replay returns the existing failed payout (`CommerceHubPayoutService` lines 759-762).

## Audit Evidence

Implemented payout evidence:

- `CommerceHubPayout` stores tenant id, payout method id, amount, currency, status, method, destination snapshot, reference, external reference id, bank transaction id, external transfer idempotency key, handoff token, handoff commit timestamp, processed by, failure reason, idempotency key, processed timestamp, completed timestamp, and completion evidence fingerprint (`CommerceHubPayout` lines 5-27).
- `CommerceHubPayoutItem` stores payout-to-earning allocation evidence and amount (`CommerceHubPayoutItem` lines 5-9).
- Completion requires terminal bank/provider/manual evidence through external reference id or bank transaction id (`CommerceHubPayoutService` lines 1424-1439).
- Processing requires a provider, bank, or operator handoff reference before external money movement starts (`CommerceHubPayoutService` lines 875-896).
- Super Admin execution persists operator identity into `ProcessedBy` through the orchestration service (`SuperAdminPayoutOrchestrationService` lines 68 and 362-363; `CommerceHubPayoutService` lines 454-455 and 543-546).
- `SuperAdminPayoutsController.Execute` writes a Super Admin audit action after successful orchestration (`SuperAdminPayoutsController` lines 99-109).
- `CommerceHubFinancialAuditService.RecordPayoutCompleted` and `RecordPayoutFailed` write payout financial audit rows (`CommerceHubFinancialAuditService` lines 332-369).
- `CommerceHubFinancialSafetyGuardService.AppendCommittedOperationAudit` records before/after platform cash, vendor payable, vendor debt, correlation id, idempotency key, reference type, reference id, outcome, and notes (`CommerceHubFinancialSafetyGuardService` lines 169-207).
- Reconciliation quarantine writes `AuditLog` with payout id, tenant id, amount, currency, references, handoff idempotency key, handoff token, handoff timestamp, prior status, new status, processed timestamp, and created timestamp (`CommerceHubPayoutReconciliationHostedService` lines 122-149).
- Ledger evidence is persisted as `CommerceHubFinancialTransaction` rows with reference type `Payout`, payout id, entity scope, transaction type, direction, bucket, amount, currency, idempotency key, correlation id, and description through `RecordPayoutAsync` and `AppendTransactionIfMissingAsync` (`CommerceHubFinancialLedgerService` lines 465-525 and 696-755).
- Accounting evidence is staged as `AccountingOutboxMessage` rows with event type `CommerceHubLedgerPosting`, ledger transaction id, payload, idempotency key, correlation id, and source type (`CommerceHubFinancialLedgerService` lines 724-755).

## Recovery

Implemented recovery and reconciliation behavior:

- Payout creation, processing, completion, and failure implement idempotent replay handling for duplicate requests and terminal states (`CommerceHubPayoutService` lines 132-136, 381-405, 474-492, and 759-762).
- Failed payouts release `InPayout` earnings back to `Available` when external non-execution is proven or the payout was not externally locked (`CommerceHubPayoutService` lines 793-820 and 1078-1088).
- Processing payouts older than the safe processing window are moved to `ReconciliationRequired` by `CommerceHubPayoutReconciliationHostedService` (`CommerceHubPayoutReconciliationHostedService` lines 66-163).
- `ReconciliationRequired` payouts remain externally locked and block new payout creation/processing for the vendor until completed or failed with non-execution proof (`CommerceHubPayoutService` lines 178-191, 424-440, and 832-833).
- Completion from `ReconciliationRequired` is allowed as external truth recognition when executed transfer evidence and handoff token match (`CommerceHubPayoutService` lines 504-524).
- Failure from `Processing` or `ReconciliationRequired` requires explicit `ExternalTransferConfirmedNotExecuted`, failure reason, non-execution reference, and matching handoff token (`CommerceHubPayoutService` lines 764-786 and 861-873).

No automatic compensation flow that reverses an already completed external payout was identified. Completed payouts cannot be failed or rolled back in the implemented state machine (`CommerceHubPayoutService` lines 751-757).

No automatic stale `Pending` payout recovery worker was identified. Pending payout recovery is implemented through idempotent retry of the same create/execute path or explicit failure through the canonical service where reachable.

## Runtime Boundary

Payout runtime owns:

- Vendor payout method registration and vendor-scoped payout destination storage.
- Vendor payout queue eligibility visibility.
- Pending payout creation and earning reservation.
- Super Admin payout execution orchestration.
- Processing handoff token and transfer idempotency evidence.
- Payout completion evidence recognition.
- Failed payout release when non-execution is proven.
- Payout ledger entries.
- Accounting outbox staging for payout ledger rows.
- Payout financial audit and committed operation audit.
- Reconciliation quarantine for stale processing payouts.

Payout runtime does not own:

- Checkout.
- Payment runtime.
- Settlement runtime.
- Refund runtime.
- Pricing.
- Inventory.
- External bank/provider rail execution.
- General ledger consumer posting.
- Marketplace reporting.

The implemented payout responsibility ends at durable local SQL payout state, earning state, ledger facts, accounting outbox staging, audit evidence, reconciliation status, and transaction commit. Accounting consumers and reporting operate downstream of this boundary.

## Critical Invariants

Implementation-backed invariants:

- Payout creation requires at least one earning id and a payout idempotency key.
- Payout amount must be positive at the database level.
- Payout currency must be EGP at service and database levels.
- Selected earnings must belong to the vendor, be available, not deleted, and past the payout delay.
- Pending earnings cannot be paid because creation requires `CommerceHubVendorEarningStatus.Available`.
- Available negative adjustments in the payout currency are netted into payout creation.
- Net payout amount must remain positive after negative adjustments.
- Payout cannot proceed until selected positive earnings and negative refund adjustments have confirming ledger rows.
- Payout processing cannot exceed ledger-backed vendor payable.
- Payout processing cannot proceed when platform cash is insufficient.
- Payout processing is blocked while active refund or return workflows create a payout lock.
- Only one `Processing` or `ReconciliationRequired` payout per vendor is allowed.
- Processing requires the POS-issued external transfer idempotency key to be echoed.
- Completion or external failure reconciliation requires the POS-issued handoff token to be echoed.
- Completed payout evidence requires an external reference id or bank transaction id.
- Active external reference id and active bank transaction id cannot be reused on another non-failed payout.
- Completed payout evidence cannot diverge from its stored fingerprint on replay.
- Completed payouts cannot be failed or rolled back.
- Failed payouts cannot be completed.
- Completion cannot start from `Pending`.
- Failed payouts release reserved earnings back to available when allowed.
- Completed positive earning allocations reduce reserved amount, increase paid out amount, and mark the earning `PaidOut` only when paid out amount reaches earning amount.
- Ledger payout recognition creates exactly three correlated payout ledger rows.
- Ledger replay is accepted only for matching idempotency key or matching financial fingerprint.
- Accounting outbox messages for ledger postings are staged in the same EF context as the corresponding ledger transaction.
- Financial state after payout completion must preserve non-negative platform cash, vendor payable, and vendor debt.

## Architecture Boundaries

Implemented ownership:

- Payout lifecycle: `CommerceHubPayoutService`.
- Payout execution entry: `SuperAdminPayoutsController` and `SuperAdminPayoutOrchestrationService`.
- Vendor pending payout creation entry: `CommerceHubPayoutsController` plus `CommerceHubPayoutService`.
- Vendor payout method ownership: `VendorPayoutMethodsController` and `CommerceHubVendorPayoutMethodsService`.
- Vendor balance eligibility: `CommerceHubVendorPayoutReadService` and `CommerceHubFinancialSafetyGuardService`.
- Reservation: `CommerceHubPayoutService.CreatePayoutAsync`.
- Execution handoff and completion: `CommerceHubPayoutService.MarkProcessingAsync` and `MarkCompletedAsync`.
- Payout ledger: `CommerceHubFinancialLedgerService`.
- Accounting outbox: `CommerceHubFinancialLedgerService`.
- Audit: `CommerceHubFinancialAuditService`, `CommerceHubFinancialSafetyGuardService`, Super Admin audit service call in `SuperAdminPayoutsController`, and reconciliation `AuditLog` write.
- Recovery/reconciliation: `CommerceHubPayoutReconciliationHostedService` plus idempotent canonical service transitions.

Implemented non-ownership:

- Payout does not own checkout.
- Payout does not own payment runtime or provider payment callbacks.
- Payout does not own settlement runtime.
- Payout does not own refund runtime.
- Payout does not own pricing authority.
- Payout does not own inventory authority.
- Payout does not own final accounting journal posting by consumers.
- Payout does not own marketplace reporting.

## Architecture Observations

- Super Admin payout execution is implemented as an orchestration of three canonical payout service transactions, not a single database transaction spanning create, processing, and completion.
- Vendor-facing payout creation can create a `Pending` reservation, but tenant-facing processing, completion, and failure endpoints are disabled; execution is routed to Super Admin.
- Payout batching is implemented as multiple `CommerceHubPayoutItem` allocations within one payout, not as a separate scheduled batch aggregate or batch worker.
- No `Cancelled` payout state is implemented.
- No explicit `Retry` payout state is implemented; retry is idempotent replay or reconciliation/failure handling.
- Payout method verification is stored and displayed, but execution validation binds method id to vendor and does not require `IsVerified`.
- No automatic external payout rail call was identified; the runtime records and reconciles operator/external transfer evidence.
- No automatic compensation flow for completed external payouts was identified.

## Architecture Risks

No architectural violations were identified.

## Certification Checklist

| Area | Result |
| --- | --- |
| Payout Entry | PASS |
| Payout Pipeline | PASS |
| Payout Ownership | PASS |
| Transaction Boundaries | PASS |
| State Machine | PASS |
| Eligibility | PASS |
| Idempotency | PASS |
| Audit Evidence | PASS |
| Recovery | PASS |
| Runtime Boundary | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS WITH OBSERVATIONS |

## Final Certification Result

PASS WITH OBSERVATIONS
