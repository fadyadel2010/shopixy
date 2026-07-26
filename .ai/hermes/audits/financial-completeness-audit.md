# Shopixy Financial Architecture Completeness Audit

Independent reviewer role: Principal ERP Financial Architect, Enterprise Auditor, and Reverse Engineering Reviewer.

Reviewed document: `.ai/hermes/architecture/financial-architecture.md`.
Produced audit: `.ai/hermes/audits/financial-completeness-audit.md`.

Audit rule: implementation is the only source of truth. The reviewed document was treated as a hypothesis, not as authority.

---

## 1. Executive Summary

Verdict: **FAIL**

The reviewed financial architecture document is directionally correct on the modern ledger-first architecture, but it does **not** completely represent the implemented financial architecture of Shopixy.

The document correctly identifies these major authorities:

- PostgreSQL General Ledger (`JournalEntry`, `LedgerEntry`, `Account`) guarded by `FinancialPostingService`.
- `ApplicationDbContext.GuardTreasuryCashAuthority()` blocking direct `JournalEntry`, `LedgerEntry`, and `Account.CurrentBalance` mutation unless `LedgerMutationScope` is authorized.
- `PaymentLifecycleService` as the main storefront/payment lifecycle authority.
- `AccountingOutboxProcessor` as the durable accounting posting bridge.
- Cashbox balance as ledger-derived, not `Cashbox.CurrentBalance`.
- CommerceHub finance via `CommerceHubFinancialTransaction`, settlements, suspense, and reconciliation.
- Affiliate commission sub-ledger and payout flows.

However, the document overstates completeness and misses several implemented money paths and authority boundaries:

1. **Legacy party ledger is real financial implementation and is not represented as a distinct financial authority.** `LedgerService` writes `AccountLedger`, mutates `Customer.Balance` and `Supplier.Balance`, and recalculates `CustomerTransactions`/`SupplierTransactions` running balances. This bypasses `FinancialPostingService` because it is not the GL ledger. It is still a live party receivable/payable sub-ledger and must be documented.
2. **Not every `PostJournalAsync` caller is through `CommerceAccountingService`.** Additional GL posting authorities exist: `TreasuryTransferService`, `TreasuryOperationService`, `AffiliateAccountingConsumer`, `AffiliateSettlementOrchestrator`, and `FinancialPostingService.ReverseJournalAsync` itself.
3. **Treasury transfer sub-authority is under-documented.** `TreasuryTransferService` implements `TransferAsync`, `SettleClearingAsync`, and `DepositToBankAsync` directly through `FinancialPostingService` and is separate from `TreasuryOperationService`.
4. **Affiliate accounting is under-documented and internally inconsistent in the reviewed document.** The document says affiliate earned commissions are only an affiliate ledger liability and says payout is handled by orchestrator; implementation also has MassTransit `AffiliateCommissionEarnedAccountingConsumer` and `AffiliateCommissionPaidAccountingConsumer` which post GL journals for earned and paid commission.
5. **Cashbox opening-balance behavior is inaccurately simplified.** Current implementation rejects non-zero cashbox opening balance up front (`OPENING_BALANCE_AUTHORITY_REQUIRED`), even though unreachable code below would call `CashInAsync` for positive amounts. The reviewed document describes opening balance as a money source without this final fail-closed runtime behavior.
6. **Bank opening-balance writes are explicitly disabled.** `BanksController.SaveOpeningBalances` returns 410 Gone. The reviewed document lists opening balances broadly but does not document this disabled bank-opening path.
7. **AccountingOutboxProcessor coverage is incomplete.** The reviewed document omits `SaleReversal`, `PurchaseReversal`, `ExpenseReversal`, and `PartyLedgerAdjustment` from its main flow coverage, though they are critical financial truth events.
8. **Manual payment / wallet review security and feature flags are under-covered.** Implementation contains `ManualPaymentOptions`, manual review certification flags, `AdminManualPaymentController`, manual wallet approval/rejection flows, proof cleanup, and runtime application services.
9. **Payment runtime feature flags contradict the wording that the new payment runtime is always authoritative.** `PaymentRuntimeFeatureFlags.Defaults` set `NewPaymentRuntime`, `NewPolicyEngine`, `DualRunPayments`, `ReplayEngine`, and `ProviderAdapterV2` to false by default. The runtime exists, but activation is feature-gated.
10. **Financial reporting/projection coverage is incomplete.** CommerceHub daily financial aggregate refresh, dashboard balances, projection repair, and super-admin finance read services are implemented and not fully represented.

The architecture document is useful as a modern ledger-first map, but it cannot prove that "one Egyptian Pound cannot move anywhere without being explained." One EGP can move through at least the party AccountLedger/customer/supplier balance path and several reversal/adjustment paths without being fully explained by the document.

---

## 2. Audit Score

Overall score: **68 / 100**

| Area | Score | Reason |
|---|---:|---|
| Ledger authority | 85 | GL guard and `FinancialPostingService` accurately described, but non-CommerceAccounting callers and party ledger exception are missing. |
| Money-source coverage | 65 | Major sale/payment/settlement sources covered; party ledger, reversal, adjustment, bank opening disablement, and feature-gated manual flows incomplete. |
| Money-destination coverage | 70 | Cashbox/bank/suspense/vendor/affiliate mostly covered; AccountLedger party balances and dashboard aggregates missed. |
| State-machine coverage | 68 | Payment/Affiliate/CommerceHub broadly covered; payout request, manual payment, party ledger, bank/cash opening disabled states incomplete. |
| Event coverage | 62 | Accounting outbox covered, but several critical event types and hosted workers omitted. |
| Security/approval coverage | 60 | High-level auth described; manual payment flags, payout wallet snapshot validation, role-specific finance endpoints, proof cleanup under-covered. |
| Consistency/idempotency coverage | 72 | Strong for GL; incomplete for legacy party ledger and background compensation/recovery paths. |
| Documentation drift identification | 70 | Some good hidden behavior captured; misses several implementation contradictions found in this audit. |

Final verdict: **FAIL** — not critical because the core ledger-first architecture is mostly right, but it is not complete enough to satisfy the stated requirement that every authority, source, destination, mutation, journal, event, payout, projection, and lifecycle is documented correctly.

---

## 3. Coverage Matrix

| Reviewed section | Rating | Audit finding |
|---|---|---|
| Executive Summary | Partially Complete | Correct ledger-first direction, but overclaims "every money mutation" goes through `FinancialPostingService`; party sub-ledger and direct balance fields exist. |
| Financial Architecture Overview | Mostly Complete | Modern GL path is correct; missing parallel party AccountLedger path, TreasuryTransferService, affiliate MassTransit accounting consumers, and feature-gated runtime caveat. |
| Money Authority Matrix | Partially Complete | Major authorities are listed, but `LedgerService`/AccountLedger party balance authority, `TreasuryTransferService`, manual payment review authority, and CommerceHub payment lifecycle authority are missing or under-specified. |
| Financial Domains Discovered | Partially Complete | Good GL/Treasury/Payment/Affiliate/CommerceHub coverage; missing bank opening disabled path, party ledger, reversal events, manual payment review, coupon/promotion financial discount effect as checkout inputs, dashboard aggregate projections. |
| Money Sources & Destinations | Partially Complete | POS/storefront/COD/CommerceHub/affiliate/purchase/refund covered; customer/supplier opening balances and adjustments are not accurately mapped as AccountLedger + GL outbox dual paths; cashbox/bank opening disabled behavior missing. |
| Money Authority Graph | Mostly Complete | Correct downward authority model for GL, but incomplete because party balances are maintained by `LedgerService`, not derived solely from GL. |
| Financial Journey | Partially Complete | Covers main journeys; misses party ledger side effects, sale/purchase/expense reversal events, manual payment review workflow detail, treasury transfer service methods. |
| Money State Machines | Partially Complete | Payment and affiliate covered; manual payment runtime, payout request states, party ledger duplicate-opening-balance lifecycle, CommerceHub payment lifecycle authority not complete. |
| Canonical Money Source & Data Ownership | Partially Complete | Correct for GL, but statement that all financial truth is GL ignores live AccountLedger + Customer/Supplier Balance sub-ledger used by controllers and statements of account. |
| Integration Events | Partially Complete | Accounting outbox and some workers covered; critical event list is incomplete and multiple hosted compensation/recovery/aggregate refresh services are missing. |
| Security Model | Partially Complete | Roles/audit/idempotency are listed; manual payment config gates, payout wallet snapshot validation, role-specific finance endpoints, proof/evidence cleanup, and party ledger audit not complete. |
| Financial Consistency Audit | Mostly Complete for GL / Partially Complete overall | GL controls are real; non-GL party ledger uses ReadCommitted + row locks and direct balance mutation; needs separate risk treatment. |
| Cross-Domain Dependencies | Mostly Complete | Main domains covered; coupon/promotion discount and projection repair/analytics aggregates under-represented. |
| Common Failure Scenarios | Partially Complete | Good GL-first guide; misses party AccountLedger/customer/supplier statement mismatches and disabled opening-balance endpoints. |
| Debugging Guide | Partially Complete | Good for ledger-first flows; incomplete because party statements and customer/supplier balances may require `AccountLedgers`, `CustomerTransactions`, `SupplierTransactions`, and rebuild routines. |
| Documentation Drift Report | Partially Complete | Captures some hidden behavior; misses new contradictions above. |
| Hidden Implementation Behavior | Partially Complete | Good items, but misses AccountLedger, TreasuryTransferService, affiliate accounting consumers, feature flags, disabled bank/cashbox opening behavior, critical-event omissions. |
| Architectural Risks | Mostly Complete | Identifies many risks; must add live party ledger bypass and event-list drift with concrete missing events. |

---

## 4. Missing Financial Flows

### 4.1 Party AccountLedger / customer and supplier balance flow — **missing**

Evidence:

- `PointOfSale.API/Services/LedgerService.cs:32-55` exposes `CreateLedgerEntry`.
- `LedgerService.cs:57-79` exposes `ApplySupplierTransaction`.
- `LedgerService.cs:81-103` exposes `ApplyCustomerTransaction`.
- `LedgerService.cs:196-240` creates `AccountLedger` rows and directly sets tracked customer/supplier balance.
- `LedgerService.cs:366-427` rebuilds `AccountLedger.RunningBalance`, `CustomerTransactions.BalanceAfter`, `SupplierTransactions.BalanceAfter`, and party `Balance`.
- `SalesController`, `PurchasesController`, `SalesReturnsController`, `PurchaseReturnsController`, `CustomersController`, `SuppliersController`, and `StatementOfAccountController` call these methods.

This is a financial sub-ledger path. It is not the GL `LedgerEntry` path and does not use `FinancialPostingService`. The architecture document treats receivables/payables as GL-only and does not document this live implementation path.

Impact:

- Customer/supplier balances and statements can change through AccountLedger before/alongside accounting outbox posting.
- Debugging "my money is wrong" must inspect AccountLedger and party balances, not only GL.

### 4.2 Party ledger adjustments — **missing / under-covered**

Evidence:

- `AccountingOutboxService` has `EnqueuePartyLedgerAdjustmentAsync`.
- `AccountingOutboxProcessor` critical list includes `PartyLedgerAdjustment`.
- `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` exists.
- `StatementOfAccountController.CreateAdjustment` creates a `PartyLedgerAdjustment`, updates party AccountLedger, and enqueues GL accounting.

The reviewed document mentions `PostPartyLedgerAdjustmentAsync` only indirectly through a source list and does not trace its dual AccountLedger + GL behavior.

### 4.3 Sale, purchase, and expense reversal events — **missing**

Evidence:

- `AccountingOutboxService` enqueues `SaleReversal`, `PurchaseReversal`, and `ExpenseReversal`.
- `AccountingOutboxProcessor._criticalFinancialTruthEventTypes` includes `SaleReversal`, `PurchaseReversal`, `ExpenseReversal`.
- `AccountingOutboxProcessor.DispatchMessageAsync` has cases for these events.

The architecture document lists refunds and reversals generally, but does not document these event types as separate financial flows.

### 4.4 TreasuryTransferService paths — **missing**

Evidence:

- `PointOfSale.Infrastructure/Accounting/TreasuryTransferService.cs:40-90` implements `TransferAsync`.
- `TreasuryTransferService.cs:93-145` implements `SettleClearingAsync`.
- `TreasuryTransferService.cs:148-199` implements `DepositToBankAsync`.

These methods post directly through `IFinancialPostingService`, not through `CommerceAccountingService`. The document's claim that Treasury is represented by `TreasuryOperationService` is incomplete.

### 4.5 Affiliate MassTransit accounting consumers — **missing / contradicts simplified affiliate flow**

Evidence:

- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateAccountingConsumer.cs:46-188` posts earned commission journal: debit `AffiliateMarketingExpense`, credit `AffiliatePayable`.
- `AffiliateAccountingConsumer.cs:205-388` posts paid commission journal: debit `AffiliatePayable`, credit cash/cashbox account.
- These consumers use `IFinancialPostingService` directly and rely on MassTransit retry.

The reviewed document says affiliate commissions are append-only and payout occurs through the orchestrator. That is incomplete because implementation also has event consumers bridging affiliate sub-ledger to GL. It also creates a possible duplicate/legacy path relationship with `AffiliateSettlementOrchestrator` that must be documented.

### 4.6 Cashbox opening balance — **wrongly generalized**

Evidence:

- `CashboxController.SetOpeningBalance` rejects any non-zero amount with `OPENING_BALANCE_AUTHORITY_REQUIRED` at lines 1222-1228.
- Later code that would call `_treasuryOperationService.CashInAsync` for positive amounts is unreachable after that guard.

The document lists opening balance as a money source and implies cashbox opening balance can be ledger-posted. Current implementation does not allow non-zero cashbox opening balance through this endpoint.

### 4.7 Bank opening balances — **missing disabled behavior**

Evidence:

- `BanksController.SaveOpeningBalances` returns HTTP 410 Gone with message: "Legacy bank opening-balance writes are disabled. Use ledger-backed treasury operations." (`BanksController.cs:396-402`).

The reviewed document lists opening balances broadly but does not explain that bank opening balance writes are disabled.

### 4.8 Manual payment / wallet review — **under-covered**

Evidence:

- `ManualPaymentOptions.cs` has approval/rejection execution flags and certification checks.
- `AdminManualPaymentController` has owner/admin approval/rejection endpoints and routes through manual payment application services.
- `ManualWalletProofCleanupJob` is registered by Hangfire.

The architecture document mentions wallet approval, but does not document the manual-payment authority, feature gates, review certification, proof lifecycle, or cleanup path.

### 4.9 Coupons/promotions/discounts as financial inputs — **under-covered**

Evidence:

- Core discount/coupon authority models contain applied discount amounts.
- Checkout and CommerceHub checkout code performs idempotent replay and validation around prices/discounts.
- Promotion expiration and discount read-model repair services exist.

These are not direct money storage authorities, but they change invoice/order totals and therefore affect revenue recognition. The reviewed document barely mentions them and does not trace their relationship to stale-client total recomputation.

### 4.10 Financial aggregates and dashboards — **under-covered**

Evidence:

- `CommerceHubDailyFinancialAggregateRefreshService` is a hosted service.
- `CommerceHubFinancialDashboardService` computes current balances and running balances for finance dashboards.
- Super-admin vendor finance service exposes current balance views.

The document describes reports as ledger reads, but CommerceHub dashboards/aggregates/projections are more nuanced and should be called out as derivative projections.

---

## 5. Missing Authorities

| Authority | Status in reviewed document | Audit result |
|---|---|---|
| `FinancialPostingService` | Documented | Confirmed as GL authority. |
| `ApplicationDbContext.GuardTreasuryCashAuthority` | Partially documented via `LedgerMutationScope` | Confirmed; should be explicitly called out as enforcement boundary. |
| `CommerceAccountingService` | Documented | Confirmed as main commerce-to-GL translator. |
| `AccountingOutboxProcessor` | Documented | Confirmed; event list incomplete in document. |
| `PaymentLifecycleService` | Documented | Mostly confirmed for storefront payment lifecycle. |
| `CommerceHubPaymentLifecycleAuthority` | Missing | Implementation mutates marketplace/sub-order payment status; must be documented separately from generic payment lifecycle. |
| `LedgerService` / party AccountLedger | Missing | Live party sub-ledger and balance authority for customer/supplier balances. |
| `TreasuryTransferService` | Missing | Direct GL posting service for transfers, clearing settlements, and bank deposits. |
| `TreasuryOperationService` | Documented | Confirmed but document misses interaction/overlap with `TreasuryTransferService`. |
| `AffiliateAccountingConsumer` | Missing | MassTransit GL posting authority for affiliate earned/paid events. |
| `AffiliateSettlementOrchestrator` | Documented | Confirmed; should be reconciled with consumer path. |
| Manual payment review application services | Under-covered | Approval/rejection authority is feature-gated and certified by configuration. |
| Cashbox opening-balance endpoint | Misrepresented | Non-zero opening balance rejected; not a money creation authority. |
| Bank opening-balance endpoint | Missing | Legacy write endpoint disabled with 410. |
| Projection/aggregate repair services | Under-covered | Derivative but operationally important for finance visibility. |

---

## 6. Missing State Machines

1. **Affiliate payout request state machine**
   - `AffiliatePayoutRequest` has `Pending`, `Approved`, `Rejected`, `Paid` and wallet snapshot checks.
   - The document focuses on commission ledger Earned/Approved/Paid and under-documents payout request approval/rejection.

2. **Manual payment review state machine**
   - Manual/wallet proof review has approval/rejection flow, certification flags, and cleanup job.
   - The document mentions wallet approval but not the runtime state machine and flags.

3. **Party opening-balance duplicate lifecycle**
   - `LedgerService.EnsureOpeningBalanceNotAlreadyAppliedAsync` rejects duplicate AccountLedger opening balances.
   - `CustomersController`, `SuppliersController`, and `StatementOfAccountController` implement customer/supplier opening balance workflows.

4. **Cashbox/bank opening disabled lifecycle**
   - Cashbox non-zero opening balance rejected.
   - Bank legacy opening balance endpoint returns 410.

5. **CommerceHub payment lifecycle authority**
   - `CommerceHubPaymentLifecycleAuthority` mutates marketplace and sub-order payment states, including aggregate status computation.
   - Document under-represents this distinct lifecycle.

6. **Payment runtime feature flag lifecycle**
   - `PaymentRuntimeFeatureFlags.Defaults` are false for new runtime components.
   - The runtime exists, but the document should not imply universal activation without configuration evidence.

---

## 7. Missing Ledger Paths

### 7.1 GL ledger mutation sites

Search result confirms `JournalEntry`/`LedgerEntry` mutations are tightly guarded:

- `FinancialPostingService` adds journals and ledger entries and calls `Account.ApplyEntry`.
- `ApplicationDbContext.GuardTreasuryCashAuthority` rejects direct ledger mutation unless `LedgerMutationScope.IsAuthorized`.

GL posting callers discovered:

- `CommerceAccountingService` — main business accounting translator.
- `FinancialPostingService.ReverseJournalAsync` — reversal journal path.
- `TreasuryTransferService` — transfer, clearing settlement, bank deposit.
- `TreasuryOperationService` — deposit/withdraw/transfer-like treasury operation paths.
- `AffiliateAccountingConsumer` — MassTransit affiliate earned/paid bridge.
- `AffiliateSettlementOrchestrator` — payout journal.

The reviewed document lists only some of these.

### 7.2 Non-GL financial ledger path

`LedgerService` writes `AccountLedger` and mutates party balances directly. This is not an unauthorized GL mutation, but it is a direct financial-balance mutation outside `FinancialPostingService`.

This means the statement "every money mutation goes through FinancialPostingService" is false unless the document explicitly scopes it to **GL money mutations only**.

---

## 8. Direct Mutation Audit

| Mutation pattern | Evidence | Implementation approved? | Audit finding |
|---|---|---|---|
| `Account.CurrentBalance +=/-=` | `Account.ApplyEntry`, called from `FinancialPostingService` under `LedgerMutationScope` | Approved | Valid GL mutation path. |
| `JournalEntries.Add`, `LedgerEntries.Add` | `FinancialPostingService` only in search results | Approved | Guarded by DbContext. |
| `ApplicationDbContext` guard for direct GL mutation | `GuardTreasuryCashAuthority` | Approved | Strong enforcement. |
| `Customer.Balance = balance` | `LedgerService.SetTrackedPartyBalance` | Approved by current implementation, but undocumented | Direct party balance mutation outside GL. |
| `Supplier.Balance = balance` | `LedgerService.SetTrackedPartyBalance` | Approved by current implementation, but undocumented | Direct party balance mutation outside GL. |
| `AccountLedgers.Add` | `LedgerService.ApplyPartyTransactionAsync` | Approved by current implementation, but undocumented | Live party sub-ledger. |
| `CustomerTransactions.BalanceAfter` / `SupplierTransactions.BalanceAfter` recalculation | `LedgerService.RebuildTrackedBalancesAsync` | Approved repair path, but undocumented | Must be in debugging guide. |
| Cashbox `OpeningBalanceAmount`, `OpeningBalance` assignment | `CashboxController.SetOpeningBalance` | Only zero amount allowed | Non-zero money creation blocked; document must state. |
| Bank opening balance write | `BanksController.SaveOpeningBalances` | Disabled | Not a live money path. |
| CommerceHub dashboard running balances | `CommerceHubFinancialDashboardService` | Projection/read model | Derivative; not authority. |

---

## 9. Event Coverage Audit

### Confirmed financial outbox critical event types

`AccountingOutboxProcessor._criticalFinancialTruthEventTypes` includes:

- `PosSale`
- `SaleReversal`
- `PosPurchase`
- `PurchaseReversal`
- `SupplierPayment`
- `SupplierOpeningBalance`
- `PartyLedgerAdjustment`
- `PosRefund`
- `PurchaseReturn`
- `PosExpense`
- `ExpenseReversal`
- `OnlinePayment`
- `CodDelivered`
- `WalletApproved`
- `EcommerceRefund`
- `UnresolvedOnlinePayment`
- `ResolvedUnresolvedOnlinePayment`
- `ResolvedUnresolvedCommerceHubPayment`
- `GatewaySettlement`
- `CodSettlement`
- `CommerceHubLedgerPosting`

The reviewed document explicitly covers many of these but misses or underplays:

- `SaleReversal`
- `PurchaseReversal`
- `ExpenseReversal`
- `PartyLedgerAdjustment`
- `SupplierOpeningBalance` as dual party ledger + GL

### Hosted/background financial workers discovered

Relevant financial/reliability workers include:

- `AccountingOutboxWorker`
- `CommerceHubFinanceReconciliationJob`
- `CommerceHubUnresolvedPaymentReconciliationHostedService`
- `CommerceHubPayoutReconciliationHostedService`
- `CommerceHubRefundRecoveryHostedService`
- `CommerceHubDailyFinancialAggregateRefreshService`
- `CommerceHubPaymentCompensationOutboxWorker`
- `StorefrontUnresolvedPaymentReconciliationHostedService`
- `CheckoutPaymentIntentRecoveryHostedService`
- `ManualWalletProofCleanupJob`
- `RefundEvidenceCleanupJob`
- `AffiliateAttributionReconciliationHostedService`
- Projection repair/reconciliation services for storefront and CommerceHub orders.

The reviewed document lists only a subset.

---

## 10. Security Coverage Audit

Confirmed controls:

- `[Authorize]` and role policies protect finance/admin surfaces.
- `AdminCommerceHubFinanceController` and `AdminCommerceHubPaymentsController` are restricted to `Owner,Admin,Accountant`.
- Manual payment approval/rejection requires `Owner,Admin` on write endpoints.
- `ITenantContext.RequiredTenantId` is widely used.
- GL mutation requires `LedgerMutationScope` and DbContext guard.
- Affiliate payout request has wallet snapshot validation and approved-status requirement.
- MassTransit affiliate paid consumer blocks out-of-order paid journal until earned journal exists.
- Account/journal immutability guarded in DbContext.

Missing/under-covered in reviewed document:

- Manual payment approval/rejection feature flags and certification gates.
- Payout wallet snapshot validation and external transfer evidence allocation limits.
- Party ledger audit (`AuditService.LogAction`) and balance rebuild audit.
- Role split for finance endpoints (`Owner,Admin,Accountant`) vs broader admin endpoints.
- Bank/cashbox opening-balance fail-closed behavior.
- Proof/evidence cleanup jobs.

---

## 11. Accounting Coverage Audit

Covered adequately:

- Cashbox/cash
- Bank
- Clearing accounts: Visa, Stripe, COD, Wallet
- Inventory and COGS for POS sale and returns
- Revenue: POS sales, e-commerce sales, shipping revenue exists in chart
- Gateway fees and shipping fees
- Supplier payables
- Marketplace vendor payable/receivable
- Treasury suspense
- Affiliate payable
- Affiliate marketing expense
- Opening balance equity exists in chart

Incomplete or missing:

- Party AccountLedger receivable/payable balances outside GL.
- Accounts receivable/payable adjustment accounts (`AccountsReceivableAdjustments`, `AccountsPayableAdjustments`) and `PartyLedgerAdjustment` flow.
- Bank fees chart account exists but is not traced in journeys.
- Tax is not represented in the account template search results reviewed; if tax exists elsewhere, it was not covered in the architecture document.
- Discounts/promotions/coupons affect net sale totals but are not traced to revenue/discount accounts; no clear discount contra-revenue account was found in the inspected account template output.
- Shipping revenue exists but storefront journey mainly discusses e-commerce sales and shipping fees; revenue-side shipping treatment needs implementation trace.
- Inventory adjustment posting is explicitly rejected until an authoritative SQL adjustment workflow exists (`PostInventoryAdjustmentAsync` returns failed). The document says inventory valuation/adjustment is present but should state this path is disabled/fail-closed.

---

## 12. Runtime Integrity Audit

Confirmed controls:

- GL posting uses serializable isolation and idempotency in `FinancialPostingService`.
- `LedgerMutationScope` + DbContext guards block direct GL mutation.
- `AccountingOutboxProcessor` keeps critical financial truth events live for retry.
- `LedgerService` uses row locks (`FOR UPDATE` on PostgreSQL) for party balance mutation, but isolation is `ReadCommitted`, not serializable.
- `AffiliateCommissionPaidAccountingConsumer` blocks paid journal if earned journal is missing.
- Treasury transfer services validate same-tenant active treasury accounts by type.
- Cashbox and bank legacy opening write paths are fail-closed.

Unresolved risks:

- Party ledger is not covered by GL immutability/idempotency discipline in the same way as `FinancialPostingService`.
- Party ledger and GL can drift because controllers update AccountLedger/party balances and enqueue accounting outbox separately.
- Duplicate or overlapping affiliate paid posting paths may exist between `AffiliateAccountingConsumer` and `AffiliateSettlementOrchestrator`; implementation comments describe one as legacy fallback, but the architecture document does not explain the relationship.
- Feature-gated payment runtime means runtime integrity depends on configuration not shown in the document.
- Critical event list must remain in lockstep with dispatch; missing event types can become silent financial risks.

---

## 13. Hidden Implementation Report

Hidden or insufficiently documented implementation behavior:

1. `LedgerService` is a live financial sub-ledger service for customer/supplier balances.
2. `Customer.Balance` and `Supplier.Balance` are directly set by `LedgerService`.
3. `AccountLedgers` are inserted directly by `LedgerService`.
4. Party balance rebuild recalculates `AccountLedger.RunningBalance`, `CustomerTransactions.BalanceAfter`, `SupplierTransactions.BalanceAfter`.
5. `TreasuryTransferService` separately posts transfer, clearing settlement, and bank-deposit journals.
6. Affiliate earned and paid MassTransit consumers post GL journals directly.
7. Cashbox non-zero opening balance is currently rejected despite dead positive-amount branch below the guard.
8. Bank opening balance legacy endpoint is disabled with 410 Gone.
9. `PaymentRuntimeFeatureFlags.Defaults` disables new runtime features by default.
10. Accounting outbox critical event list includes reversal and adjustment events omitted from the document.
11. Manual payment review has certification/feature flags and proof lifecycle cleanup.
12. CommerceHub daily financial aggregate refresh is a derivative financial projection service not covered by the reviewed document.
13. Inventory adjustment accounting is explicitly rejected without authoritative SQL workflow.

---

## 14. Contradictions

| Reviewed document statement / implication | Audit result |
|---|---|
| "Every money mutation ... is expressed as a balanced JournalEntry posted through one posting engine." | Refuted as written. GL mutations are guarded this way, but party AccountLedger and Customer/Supplier balances are direct financial mutations outside `FinancialPostingService`. |
| "CommerceAccountingService translates business op → PostingRequest" as the accounting path | Incomplete. TreasuryTransferService, TreasuryOperationService, AffiliateAccountingConsumer, AffiliateSettlementOrchestrator also call `PostJournalAsync`. |
| "Affiliate commission earned is a liability but not cash yet" without GL earned consumer | Incomplete. Earned consumer debits affiliate marketing expense and credits AffiliatePayable. |
| "Opening balance" as a broad money source | Too broad. Cashbox non-zero opening is rejected; bank opening writes are disabled; customer/supplier opening balances use AccountLedger + outbox. |
| Payment runtime as canonical current state machine | Needs caveat. New runtime feature flags default false. |
| "Reports read ledger only" | Over-generalized. Core financial reports may read GL, but CommerceHub dashboards and daily aggregates are derivative projections/aggregates. |
| "Inventory adjustment" as a possible accounting path | Current `PostInventoryAdjustmentAsync` fails closed unless authoritative SQL workflow exists. |

---

## 15. Architectural Risks

1. **Dual-ledger mental model risk** — GL ledger and AccountLedger party ledger coexist. Documentation that only mentions GL will mislead debugging and reconciliation.
2. **Party balance drift risk** — AccountLedger/Customer/Supplier balance update and GL outbox posting can diverge if one succeeds and the other fails.
3. **Affiliate duplicate-path risk** — MassTransit accounting consumers and payout orchestrator both interact with affiliate GL posting; exact production ownership must be documented to avoid double posting.
4. **Feature-flag risk** — Payment runtime defaults false; architecture claims must distinguish implemented code from active runtime behavior.
5. **Reversal-event omission risk** — Sale/purchase/expense reversals are critical financial truth events; not documenting them hides rollback/compensation behavior.
6. **Opening-balance misunderstanding risk** — cashbox and bank opening balances are intentionally disabled/fail-closed; operators may expect them to create money.
7. **Discount/tax/accounting opacity risk** — discounts/coupons affect money but lack a traced accounting account in the reviewed document.
8. **Aggregate/projection authority risk** — CommerceHub finance dashboards expose balances but are derivative; incomplete documentation can cause operators to trust projection balances over GL.
9. **Inventory adjustment gap** — accounting method is present but disabled; inventory valuation coverage must not imply operational support.

---

## 16. Recommendations

1. Amend the architecture document later — not in this task — to separate:
   - General Ledger (`JournalEntry`/`LedgerEntry`/`Account`) authority.
   - Party sub-ledger (`AccountLedger`, `Customer.Balance`, `Supplier.Balance`) authority.
   - Projection/reporting/aggregate surfaces.
2. Add a "Known Direct Financial Mutations Outside GL" section for `LedgerService` and party balances.
3. Add all `PostJournalAsync` callers and identify which are primary, legacy fallback, or reversal-only.
4. Add all critical accounting outbox event types from `AccountingOutboxProcessor`.
5. Add explicit disabled/fail-closed paths:
   - Cashbox non-zero opening balance rejected.
   - Bank opening balance writes disabled.
   - Inventory adjustment accounting rejected without SQL authority workflow.
6. Add manual payment/wallet review architecture, feature flags, proof cleanup, approval roles, and rejection paths.
7. Add affiliate accounting consumer bridge and reconcile it with `AffiliateSettlementOrchestrator`.
8. Add CommerceHub payment lifecycle authority and compensation outbox worker.
9. Add debugging steps for AccountLedger and party balance rebuilds.
10. Add explicit caveat: `FinancialPostingService` owns GL, not every financial sub-ledger row in the solution.

---

## 17. Financial Path Coverage

| Scenario | Coverage in reviewed document | Audit result |
|---|---|---|
| POS Sale | Fully Covered for GL; missing AccountLedger side effects | Partially Covered |
| POS Refund | Covered at GL/refund level; party ledger/reversal detail incomplete | Partially Covered |
| POS Purchase | Covered at GL level; supplier AccountLedger path missing | Partially Covered |
| Supplier Payment | Covered at GL level; supplier AccountLedger path missing | Partially Covered |
| Supplier Refund | Purchase return covered; supplier sub-ledger incomplete | Partially Covered |
| Storefront COD | Mostly covered | Mostly Covered |
| Storefront Gateway | Mostly covered | Mostly Covered |
| Storefront Wallet | Under-covered manual review/security flags | Partially Covered |
| Affiliate | Commission ledger covered; MassTransit accounting and payout request states missing | Partially Covered |
| CommerceHub | Broadly covered; payment lifecycle authority, compensation worker, aggregates incomplete | Partially Covered |
| Expense | Covered; ExpenseReversal missing | Partially Covered |
| Treasury Transfer | TreasuryOperation covered; TreasuryTransferService missing | Partially Covered |
| Cash Deposit | Under-covered; TreasuryTransferService `DepositToBankAsync` missing | Partially Covered |
| Cash Withdrawal | Treasury direct outflows disabled in one service; exact live withdrawal paths need clearer doc | Partially Covered |
| Opening Balance | Customer/supplier AccountLedger path missing; cashbox/bank disabled behavior missing | Incomplete |
| Closing Balance | Not found as a canonical money mutation in inspected implementation; reports derive balances | Mostly Missing |
| Inventory valuation | POS COGS covered; inventory adjustment accounting disabled not documented | Partially Covered |
| COGS | Mostly covered | Mostly Covered |
| Refund | Storefront refund covered; CommerceHub refund recovery/compensation incomplete | Partially Covered |
| Settlement | Gateway/COD covered; TreasuryTransferService clearing settlement missing | Partially Covered |
| Bank Deposit | `TreasuryTransferService.DepositToBankAsync` missing | Partially Covered |
| Bank Withdrawal | Not fully traced in reviewed document | Partially Covered / Needs trace |
| Manual Adjustment | PartyLedgerAdjustment missing | Missing |

---

## 18. Final Question

Question: Can one Egyptian Pound move anywhere inside Shopixy WITHOUT being explained by `financial-architecture.md`?

Answer: **YES**.

Missing or insufficiently explained paths:

1. **Customer/supplier AccountLedger path** — `LedgerService` can create party ledger entries and mutate `Customer.Balance`/`Supplier.Balance` outside `FinancialPostingService`.
2. **Party ledger adjustments** — `StatementOfAccountController` creates adjustments that affect party balances and enqueue accounting.
3. **Customer/supplier opening balances** — implemented through AccountLedger and outbox, not just GL.
4. **TreasuryTransferService bank deposit / clearing settlement / transfer** — direct GL posting not described.
5. **Affiliate MassTransit earned/paid accounting consumers** — direct GL posting not described.
6. **Sale/purchase/expense reversal outbox events** — critical financial truth paths not traced.
7. **Manual payment review approval/rejection** — wallet money admission/denial path not sufficiently documented.
8. **Cashbox/bank opening-balance fail-closed behavior** — document implies opening balance as a possible source but implementation blocks important legacy paths.

Therefore the document cannot yet be used as a complete source to answer every money movement question without reading source code.

---

## 19. Final Verdict

**FAIL**

Reason: the document is a strong ledger-first architecture summary, but it is incomplete and contains overbroad claims. It does not completely document every authority, source, destination, ledger mutation, journal path, settlement, commission, payout, reconciliation, projection, event, accounting path, or lifecycle implemented in Shopixy.

This is not a critical failure because the primary GL architecture is correctly identified and many major flows are accurately represented. It fails the user’s specified completeness bar because live implementation-backed financial paths remain undocumented.

---

## 20. Final Confirmation

Files created:

- `.ai/hermes/audits/financial-completeness-audit.md`

Files modified:

- None intentionally modified other than the created audit file.
- Existing `.ai/hermes/architecture/financial-architecture.md` was read only and was not modified.

Memory writes:

- None.

Code generated:

- None.

Implementation changed:

- No.
