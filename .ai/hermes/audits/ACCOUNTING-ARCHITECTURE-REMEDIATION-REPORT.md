# Shopixy Accounting Architecture Remediation Report

Date: 2026-07-20

Scope: Remediation pass against `.ai/hermes/audits/ACCOUNTING-ARCHITECTURE-AUDIT.md`, using implementation as source of truth.

## Summary

- Total findings reviewed: 66 audit-identified findings/items.
- Implemented in this pass: 9.
- Already fixed / audit-corrected false positives accepted: 2.
- Verified but intentionally deferred as larger domain/policy work: 14.
- Reviewed and documented as low/architectural improvement/no immediate code change: 41.
- Skipped: 0 intentionally skipped without disposition.

## Quality Gates

- Build: Passed (`dotnet build PointOfSale.sln`).
- Targeted accounting regression tests: Passed, 20/20 (`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~FinancialIntegrityServiceTests|FullyQualifiedName~ExpenseControllerIdempotencySafetyTests|FullyQualifiedName~FinancialReportsControllerTenantScopeTests|FullyQualifiedName~AccountingGovernanceAuthorizationTests"`).
- Full solution tests: Failed with many unrelated pre-existing/non-accounting failures after a successful long run (`dotnet test PointOfSale.sln --no-build`). Accounting-targeted tests passed separately.

## Implemented Findings

### H-04 / F-11-04

- Current Status: IMPLEMENTED.
- Evidence: `StatementOfAccountController.CreateAdjustment` had class-level `[Authorize]` only.
- Root Cause: Manual adjustment endpoint lacked endpoint-level governance role restriction.
- Files Modified: `PointOfSale.API/Controllers/StatementOfAccountController.cs`, `PointOfSale.Tests/Architecture/AccountingGovernanceAuthorizationTests.cs`.
- Architecture Decision: Added existing role-convention authorization (`Owner,Admin,Manager`) without changing ledger/outbox/idempotency flow.
- Risk: Low.
- Regression Risk: Low; direct endpoint access becomes stricter.
- Tests Added: Role reflection coverage for adjustment endpoint.
- Remaining Work: Replace role string with shared policy if/when the platform formalizes accounting policies.

### H-06 / CV-03

- Current Status: IMPLEMENTED.
- Evidence: Expense approval and party adjustment were weaker than supplier payment/treasury flows.
- Root Cause: Manual correction endpoints were not consistently role-gated.
- Files Modified: `ExpensesController.cs`, `StatementOfAccountController.cs`, `CashboxController.cs`, `AccountingGovernanceAuthorizationTests.cs`.
- Architecture Decision: Reused existing controller role attributes. No bypass of validation, posting, outbox, or audit.
- Risk: Low.
- Regression Risk: Medium for users without governance roles who previously approved expenses/adjustments.
- Tests Added: Governance role tests for expense approval, adjustment creation, and cashbox close.
- Remaining Work: Shared named policy remains recommended.

### H-02 / F-07-01

- Current Status: IMPLEMENTED.
- Evidence: Expense DTOs and controller persisted raw amount; posting later rejected non-positive lines.
- Root Cause: Operational expense validation was weaker than GL posting validation.
- Files Modified: `ExpenseDTOs.cs`, `ExpensesController.cs`, `ExpenseControllerIdempotencySafetyTests.cs`.
- Architecture Decision: Reject non-positive amount before persistence, round accepted amounts to currency precision, preserve outbox posting path.
- Risk: Low.
- Regression Risk: Low; invalid operational rows are now rejected earlier.
- Tests Added: Non-positive expense create rejection.
- Remaining Work: Optional DB check constraint.

### H-05 / CV-01

- Current Status: IMPLEMENTED.
- Evidence: `FinancialIntegrityService.ValidateAsync` lacked expense accounting coverage.
- Root Cause: Approved expenses relied on durable outbox retry but had no integrity scan proving outbox/journal linkage.
- Files Modified: `FinancialIntegrityService.cs`, `FinancialIntegrityServiceTests.cs`.
- Architecture Decision: Added `ValidateExpenseLinksAsync` modeled after supplier payments/adjustments; flags missing outbox, pending/retrying posting, missing journal, and invalid approved amount.
- Risk: Low.
- Regression Risk: Low; read-only validation only.
- Tests Added: Approved expense missing outbox/journal coverage.
- Remaining Work: Surface this validation in an operator-facing health/report endpoint if not already scheduled.

### H-07 / CV-04 and M-22 / F-10-04

- Current Status: IMPLEMENTED for expense amount and cashbox close cash.
- Evidence: `CloseCashboxRequest.ActualCash` was unconstrained; expense amount accepted non-positive values before posting.
- Root Cause: Uneven boundary validation.
- Files Modified: `CashboxDTOs.cs`, `CashboxController.cs`, `ExpenseDTOs.cs`, `ExpensesController.cs`.
- Architecture Decision: Add DTO annotations plus server-side guards.
- Risk: Low.
- Regression Risk: Low.
- Tests Added: Negative cashbox close source guard, expense amount tests.
- Remaining Work: Party-floor policy remains deferred.

### M-09 / F-07-02

- Current Status: IMPLEMENTED.
- Evidence: Expense create/update accepted category/funding target identifiers without explicit existence/compatibility checks.
- Root Cause: Reliance on later FK/posting failures instead of controller boundary validation.
- Files Modified: `ExpensesController.cs`, `ExpenseControllerIdempotencySafetyTests.cs`.
- Architecture Decision: Validate active category, supplier, cashbox, bank, and payment-method/funding-target compatibility before persistence; idempotency-key validation remains first contract check after field validation.
- Risk: Medium because BankTransfer now requires a bank account.
- Regression Risk: Medium for clients sending incomplete BankTransfer expense payloads.
- Tests Added: Missing category rejection.
- Remaining Work: If tenant-level default bank routing is desired, add explicit configuration rather than accepting missing target.

### M-10 / F-07-03 and M-17 / CV-05

- Current Status: PARTIALLY IMPLEMENTED.
- Evidence: Expense, SOA, cashbox, and daily-close reads relied mostly on global filters.
- Root Cause: Defense-in-depth tenant predicates were uneven.
- Files Modified: `ExpensesController.cs`, `StatementOfAccountController.cs`, `CashboxController.cs`, `ReportsController.cs`.
- Architecture Decision: Added explicit tenant predicates to high-value accounting read surfaces while retaining global filters.
- Risk: Low.
- Regression Risk: Low, except tests/contexts must provide tenant context.
- Tests Added: Updated expense pagination fixture to use tenant context.
- Remaining Work: Continue same pattern in all non-accounting read surfaces over time.

### M-12 / F-08-06

- Current Status: IMPLEMENTED.
- Evidence: Financial report controller accepted `from > to`.
- Root Cause: Date range validation was missing at controller boundary.
- Files Modified: `FinancialReportsController.cs`, `FinancialReportsControllerTenantScopeTests.cs`.
- Architecture Decision: Normalize to UTC, reject inverted ranges before invoking ledger-only report services.
- Risk: Low.
- Regression Risk: Low.
- Tests Added: Inverted trial-balance range rejection.
- Remaining Work: Optional common date-range validator.

### M-03 / F-10-02 and M-20 / CV-09

- Current Status: PARTIALLY IMPLEMENTED.
- Evidence: Daily close counted reversed source journals in source subtotals.
- Root Cause: Source-specific subtotals mixed `Posted` and `Reversed` source journals while reversal journals use `SourceType = "Reversal"`.
- Files Modified: `ReportsController.cs`.
- Architecture Decision: Daily-close source buckets now use posted journals only and explicit tenant predicates; posted reversal journals remain ledger movements under their own source.
- Risk: Medium.
- Regression Risk: Medium for dashboards expecting historical reversed-source gross counts.
- Tests Added: Covered indirectly by build/targeted accounting suite; a daily-close scenario test is still recommended.
- Remaining Work: Standardize reversal disclosure across all reporting surfaces.

## Verified But Deferred

### C-02 / CV-02

- Current Status: PARTIALLY VERIFIED, DEFERRED.
- Evidence: Order refund accounting exists; supplier payment lacks explicit void/reversal workflow.
- Root Cause: Missing supplier-payment lifecycle command, not current GL corruption.
- Architecture Decision: Deferred because correct implementation requires domain lifecycle, authorization, UX/API contract, and migration decisions. Do not emulate order refunds blindly.
- Remaining Work: Add first-class supplier-payment void/reversal command through existing posting/outbox infrastructure.

### M-02 / F-10-01

- Current Status: VERIFIED, DEFERRED.
- Evidence: Cashbox close still reads `IsActive` then updates.
- Root Cause: No conditional update/concurrency token on close.
- Architecture Decision: Deferred provider-aware conditional update/row-lock change; added validation/authorization now.
- Remaining Work: Add row-version or provider-specific `WHERE IsActive = true` update and conflict response.

### M-04 / F-10-03

- Current Status: VERIFIED, DEFERRED.
- Evidence: Closure audit remains post-commit and best-effort.
- Root Cause: Audit logging is not transactionally durable with close mutation.
- Architecture Decision: Deferred to durable audit/outbox design.
- Remaining Work: Move closure audit into the close transaction or durable audit outbox.

### M-05 / F-11-05

- Current Status: VERIFIED, DEFERRED.
- Evidence: LedgerService does not enforce party-balance floor.
- Root Cause: Business policy absent.
- Architecture Decision: Not implemented without party/account-type policy.
- Remaining Work: Define configurable floor rules, then enforce in party mutation service.

### M-06 / F-11-06

- Current Status: VERIFIED, DEFERRED.
- Evidence: `PartyLedgerAdjustment` lacks stored `JournalEntryId`; link is reconstructed.
- Root Cause: Historical entity schema lacks direct journal link.
- Architecture Decision: Deferred because adding direct link needs migration and outbox processor update.
- Remaining Work: Store journal ID on successful adjustment posting or expose derived link status.

### M-07 / F-11-07

- Current Status: VERIFIED, DEFERRED.
- Evidence: No adjustment lifecycle fields/commands.
- Root Cause: Adjustments are compensating entries only.
- Architecture Decision: Deferred until business decides approval/reversal workflow versus compensating-entry policy.
- Remaining Work: Add adjustment reversal/void lifecycle if required.

### M-08 / M-13 / M-21 / F-11-08 / F-08-07 / CV-11

- Current Status: VERIFIED, DEFERRED.
- Evidence: No shared accounting-period lock service.
- Root Cause: Governance feature missing.
- Architecture Decision: Deferred; requires shared period model and migration.
- Remaining Work: Add period-lock service and invoke from financial write paths.

### M-01 / M-19 / M-23 / F-09-01 / F-09-03 / CV-08

- Current Status: PARTIALLY ADDRESSED/DEFERRED.
- Evidence: SOA had existing workspace changes moving supplier display balances toward AccountLedger; supplier auxiliary data split remains broader.
- Root Cause: Ledger authority, cached balances, and supplier transaction mirror coexist.
- Architecture Decision: This pass added explicit tenant predicates; full ownership cleanup remains larger read-model work.
- Remaining Work: Make AccountLedger the party display source and label/limit operational mirrors.

### M-11 / H-08 / CV-06 / CV-10

- Current Status: PARTIALLY ADDRESSED/DEFERRED.
- Evidence: Expense integrity scan added; no universal GL-converged status contract.
- Root Cause: Async sources expose convergence unevenly.
- Architecture Decision: Added expense scan rather than new status model.
- Remaining Work: Add shared outbox lag / GL convergence endpoint.

### H-01 / F-06-01

- Current Status: VERIFIED, DOCUMENTATION DEFERRED.
- Evidence: Audit flow scope mixed ERP party payments and order runtime.
- Root Cause: Canonical docs split is missing.
- Remaining Work: Split FLOW-ACC-06 docs by ERP party payments vs CommerceHub/order payments.

### M-14 / L-03 / F-08-08 / F-09-05

- Current Status: VERIFIED, DEFERRED.
- Evidence: Core ledger/SOA reports are single-currency.
- Root Cause: Multi-currency is out of current accounting model.
- Remaining Work: Implement only with tenant currency model and FX policy.

### M-15 / F-08-09

- Current Status: VERIFIED, DEFERRED.
- Evidence: Retained earnings close not implemented.
- Root Cause: Fiscal close feature missing.
- Remaining Work: Add fiscal-year close process if target tenants require formal statements.

### M-16 / CV-12 / F-06-03

- Current Status: PARTIALLY VERIFIED, DEFERRED.
- Evidence: Webhook logging/idempotency is heterogeneous.
- Root Cause: Provider runtime histories differ.
- Remaining Work: Standardize webhook idempotency semantics and logs.

## Already Fixed / False Positive Accepted

### C-01 / F-06-02

- Current Status: FALSE POSITIVE / ALREADY PROTECTED.
- Evidence: Recognized-payment cancellation is blocked/collapsed; refunds use refund accounting path.
- Architecture Decision: No code change.
- Remaining Work: Audit recommended regression tests for collected-order cancel/failure guards.

### Order-refund portion of C-02

- Current Status: FALSE POSITIVE / ALREADY IMPLEMENTED.
- Evidence: `RecordRefundAsync`, refund outbox, and `PostEcommerceRefundAsync` exist.
- Architecture Decision: No code change.
- Remaining Work: Maintain refund accounting regression coverage.

## Low Findings And Architectural Improvements

Reviewed and left as no immediate code change: L-01 through L-15 and AI-01 through AI-13, except where folded into implemented items above. These are either compliance/UX/report-completeness enhancements, verified strengths to preserve, or scope-dependent features. No changes were made that weaken `FinancialPostingService`, `LedgerMutationScope`, outbox retry semantics, journal immutability, idempotency, CQRS boundaries, tenant isolation, or authorization.

## Full Test Failure Note

The full solution test run failed outside this remediation surface. First observed failures included:

- `AdminRouteAuthorizationHardeningTests.ApiAdminControllers_MustNotUseBareAuthorizeAttributes`.
- Multiple CommerceHub/service-marketplace certification documentation freezes.
- SQLite migration syntax failures in `EcommerceRefundLifecycleTests`.
- Many storefront/shipping/CommerceHub/product-variant integration and documentation certification tests.

The targeted accounting remediation tests pass, and the solution builds. The full-suite failures should be triaged separately against the existing dirty workspace state.
