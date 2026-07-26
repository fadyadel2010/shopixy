# Shopixy Financial Architecture — Reverse Engineered from Implementation

> Authoritative source: implementation only. Every claim below cites the file that proves it.
> Read this document once and you will know where money comes from, who owns it, and where it ends — without reading source code.

---

## 1. Executive Summary

Shopixy does NOT have a single "Cashbox" module. Money moves through a **layered financial stack**, and Cashbox is only the physical-cash leaf of it. The canonical General Ledger authority is a **double-entry Ledger in PostgreSQL**. Canonical GL money mutations — POS sale, storefront order, COD, gateway settlement, refund, affiliate payout, CommerceHub vendor payout, expense, purchase — are expressed as balanced `JournalEntry`/`LedgerEntry` rows posted through `FinancialPostingService`. Implementation also contains a live party sub-ledger (`AccountLedger` via `LedgerService`) for customer/supplier balances; that sub-ledger directly maintains `Customer.Balance`/`Supplier.Balance` and is documented below as a separate financial path, not as the GL authority.

Key discovered facts:

- **PostgreSQL General Ledger is the single source of GL financial truth.** Cashbox, Treasury, Wallets, CommerceHub vendor balances, and Affiliate GL balances are *derivative* of the Ledger (`.ai-context/PROJECT_CONTEXT.md` Financial Authority Boundaries; `FinancialPostingService.cs`).
- **Party receivable/payable balances have a separate live sub-ledger.** `LedgerService` writes `AccountLedger`, directly sets `Customer.Balance`/`Supplier.Balance`, and rebuilds `CustomerTransactions`/`SupplierTransactions` running balances. These are implementation-approved sub-ledger mutations outside `FinancialPostingService`; they must be reconciled/debugged alongside GL.
- **Treasury GL routes through posting services.** `TreasuryOperationService` has explicit "NO DIRECT DATABASE WRITES TO LEGACY TABLES" and CashIn/CashOut/Expense are *disabled* — outflows must originate from a certified liability/payment/accounting authority (`TreasuryOperationService.cs`). `TreasuryTransferService` separately posts transfer, clearing-settlement, and bank-deposit journals directly through `FinancialPostingService`.
- **Cashbox has no money of its own.** Each cashbox gets a *dedicated ledger account* under `CashboxMain`; its legacy `CurrentBalance`/`ExpectedBalance` fields are `[Obsolete]` "shadow balances" — use the ledger (`Cashbox.cs`, `CashboxLedgerAccountService.cs`).
- **Payment state is sealed behind lifecycle authorities** (`PaymentLifecycleService`, plus CommerceHub-specific payment lifecycle authority for marketplace/sub-order payment status). The new payment runtime exists as deterministic domain code (`StateTransitionRuntime`, `PaymentSettlementRuntime`) but is feature-gated by `PaymentRuntimeFeatureFlags` defaults (`NewPaymentRuntime`, `NewPolicyEngine`, `DualRunPayments`, `ReplayEngine`, `ProviderAdapterV2` default false).
- **Affiliate commissions are an append-only global ledger** (`AffiliateCommissionLedger`) with lifecycle Earned → Approved → Paid, keyed by `StoreTenantId` (the store that owes), never the affiliate's home tenant. Affiliate GL posting is bridged by MassTransit consumers (`AffiliateCommissionEarnedAccountingConsumer`, `AffiliateCommissionPaidAccountingConsumer`) and payout execution is handled by `AffiliateSettlementOrchestrator`; these paths must remain idempotent and non-overlapping.
- **CommerceHub finance posts to GL** through `CommerceHubFinancialTransaction` → suspense/clearing with vendor-payable, vendor-receivable, and platform-commission buckets.
- **Unresolved/external money is contained via suspense** (`TreasurySuspense`) and hosted reconciliation workers, never silently overwritten.

---

## 2. Financial Architecture Overview

```
                         BUSINESS EVENT (sale/order/return/expense/payout)
                                       │
                                       ▼
                  PAYMENT RUNTIME  (StateTransitionRuntime / PaymentSettlementRuntime)
                  - canonical payment state machine (core domain, no DB)
                  - settlement decision (deterministic idempotent id)
                                       │
                                       ▼
                  PAYMENT LIFECYCLE SERVICE  (PaymentLifecycleService — canonical authority)
                  - mutates PaymentIntent / Order.PaymentStatus in a serializable tx
                  - enqueues AccountingOutboxMessage + integration events
                                       │
                  ┌────────────────────┴─────────────────────┐
                  ▼                                           ▼
        ACCOUNTING OUTBOX  (PostgreSQL, durable)      INTEGRATION EVENTS (MassTransit/RabbitMQ)
        processed by AccountingOutboxProcessor         → Mongo/Redis/ES projections (derivative)
                  │
                  ▼
        COMMERCE ACCOUNTING SERVICE  (CommerceAccountingService)
        translates business op → PostingRequest (Debit/Credit lines)
                  │
                  ▼
        FINANCIAL POSTING SERVICE  (FinancialPostingService — the ledger engine)
        - idempotency key (dual check: pre-tx + in-tx)
        - SERIALIZABLE isolation
        - balanced validation (debits == credits)
        - negative-balance prevention on flagged accounts
        - immutable journals (no update/delete)
                  │
                  ▼
        LEDGER ACCOUNTS  (Chart of Accounts, PostgreSQL)  ← CANONICAL MONEY
                  │
        ┌─────────┼─────────────────────────┬──────────────┐
        ▼         ▼                          ▼              ▼
   Cashbox    Treasury Accounts          CommerceHub     Affiliate
   ledger     (Cash/Bank type,           vendor          CommissionLedger
   account    all Ledger-based)           payable/        + BalanceLedger
              (TreasuryOperationService)  receivable      (PayoutWallet = external rail only)
```

Implementation caveat: the diagram is the canonical GL path. There are additional implementation-approved financial paths that must be considered with it:

- `LedgerService` / `AccountLedger` is a live party sub-ledger for customer and supplier balances. It writes `AccountLedger` and directly mutates `Customer.Balance` / `Supplier.Balance`; it is not the GL `LedgerEntry` store and does not use `FinancialPostingService`.
- `TreasuryTransferService` posts transfer, clearing-settlement, and bank-deposit journals directly through `FinancialPostingService`, outside `CommerceAccountingService`.
- `AffiliateAccountingConsumer` posts affiliate earned/paid GL journals from MassTransit events, while `AffiliateSettlementOrchestrator` executes payout settlement.
- `AccountingOutboxProcessor` critical financial truth events include reversal and adjustment event types beyond the main sales/payment events listed in the diagram.

Monetary unit rule: **EGP is enforceable currency** for CommerceHub and vendor settlements (`FinancialCurrencyGuard.RequireEgpCurrency`); the platform base currency is EGP. Storefront/CommerceHub customer pricing may carry other currencies but CommerceHub finance rows are EGP-only.

---

## 3. Money Authority Matrix (who owns what)

| Concern | Owner (implementation) | Evidence |
|---|---|---|
| GL financial truth / statutory-style reporting | Immutable double-entry **Ledger** (PostgreSQL) | `FinancialPostingService.cs`; `PROJECT_CONTEXT.md` Financial Authority Boundaries |
| GL mutation enforcement | `ApplicationDbContext.GuardTreasuryCashAuthority` + `LedgerMutationScope.Authorize()` | `ApplicationDbContext.cs`, `LedgerMutationScope.cs` |
| Journal posting engine | `FinancialPostingService` (atomic, serializable, idempotent, balanced, immutable) | `FinancialPostingService.cs` |
| Party receivable/payable sub-ledger | `LedgerService` (`AccountLedger`, `Customer.Balance`, `Supplier.Balance`) | `LedgerService.cs`; Sales/Purchases/Statement controllers |
| Chart of accounts / account resolution | `AccountResolver` + `SystemAccountTemplate` (seeded per tenant) | `SystemAccountTemplate.cs`, `IAccountResolver.cs` |
| Cashbox money (physical cash) | **Dedicated cashbox Ledger account** under `CashboxMain` | `CashboxLedgerAccountService.cs`; `Cashbox.cs` (obsolete shadow balance) |
| Treasury deposits/withdrawals/transfers | `TreasuryOperationService` → posts journal; direct CashIn/CashOut/Expense disabled | `TreasuryOperationService.cs` |
| Treasury transfer / clearing settlement / bank deposit | `TreasuryTransferService` → `FinancialPostingService.PostJournalAsync` | `TreasuryTransferService.cs` |
| Payment state machine (domain) | `StateTransitionRuntime` + `PaymentSettlementRuntime` (core, pure) | `PaymentStateTransitionRuntime.cs`, `PaymentSettlementRuntime.cs` |
| Payment lifecycle authority (storefront DB mutation) | `PaymentLifecycleService.IPaymentLifecycleService` | `PaymentLifecycleService.cs` |
| CommerceHub payment lifecycle authority | `CommerceHubPaymentLifecycleAuthority` mutates marketplace/sub-order payment status | `CommerceHubPaymentLifecycleAuthority.cs` |
| Payment runtime feature gates | `PaymentRuntimeFeatureFlags.Defaults` default new runtime features to false | `PaymentRuntimeFeatureFlags.cs` |
| COD / Gateway / Online settlement | `PaymentLifecycleService.RecordCodSettlementAsync` / `RecordGatewaySettlementAsync` → `PaymentSettlementBatch` | `PaymentLifecycleService.cs:287,543` |
| Refunds | `PaymentLifecycleService.RecordRefundAsync` → `OrderRefund` row + ledger | `PaymentLifecycleService.cs:828` |
| Commerce→Ledger translation | `CommerceAccountingService.ICommerceAccountingService` | `CommerceAccountingService.cs` |
| Accounting outbox / async posting | `AccountingOutboxService` + `AccountingOutboxProcessor` | `AccountingOutboxProcessor.cs` |
| CommerceHub vendor earnings / payouts / reconciliation | `CommerceHubFinancialTransaction`, `CommerceHubVendorSettlement`, `CommerceHubFinanceReconciliationJob` | `CommerceHubFinancialTransaction.cs`, `CommerceHubFinanceReconciliationJob.cs` |
| Affiliate commission authority | `AffiliateCommissionLedger` (append-only) + `AffiliateBalanceLedger` | `AffiliateCommissionLedger.cs`, `AffiliateBalanceLedger.cs` |
| Affiliate GL bridge | MassTransit `AffiliateCommissionEarnedAccountingConsumer` / `AffiliateCommissionPaidAccountingConsumer` | `AffiliateAccountingConsumer.cs` |
| Affiliate payout execution | `AffiliateSettlementOrchestrator` (serializable, balance-checked) | `AffiliateSettlementOrchestrator.cs` |
| Manual wallet/payment review | Manual payment application services + `AdminManualPaymentController`; feature/certification gates | `ManualPaymentOptions.cs`, `AdminManualPaymentController.cs` |
| Unresolved/external money containment | `TreasurySuspense` + hosted recovery workers | `CommerceAccountingService.PostResolvedUnresolvedPaymentAsync`, `CommerceHubFinanceReconciliationJob.cs` |
| Accounting replay authority (what may post) | `AccountingLedgerAuthority` (SQL-truth gate) | `AccountingLedgerAuthority.cs` |
| CommerceHub finance dashboards / aggregates | Derivative read/aggregate services, not GL authority | `CommerceHubFinancialDashboardService.cs`, `CommerceHubDailyFinancialAggregateRefreshService.cs` |
| Projections (Mongo/Redis/ES/caches) | **Never** financial authority — derivative only | `PROJECT_CONTEXT.md`; all consumers |

**Fail-closed rule:** external gateways/banks are *execution* authorities; unresolved outcomes are modeled explicitly and reconciled before any further outward money (`PROJECT_CONTEXT.md`).

---

## 4. Financial Domains Discovered (implementation-backed)

| Domain | Implementation artifact | Role |
|---|---|---|
| Ledger / Journal | `JournalEntry`, `LedgerEntry`, `FinancialPostingService`, `LedgerMutationScope` | Canonical double-entry GL store |
| Party Sub-Ledger | `AccountLedger`, `LedgerService`, `Customer.Balance`, `Supplier.Balance`, `CustomerTransactions`, `SupplierTransactions` | Customer/supplier statement and running-balance authority outside GL |
| Chart of Accounts | `Account`, `AccountCodes`, `SystemAccountTemplate`, `TenantAccountSeeder`, `ChartOfAccountsStartupSeeder` | Per-tenant GL |
| Cashbox | `Cashbox`, `CashboxTransaction`, `CashboxLedgerAccountService`, `ICashboxLedgerAccountService` | Physical cash drawer; has dedicated ledger account |
| Treasury | `TreasuryAccount` (Cash/Bank type), `TreasuryOperationService`, `TreasuryReadService`, `TreasuryTransferService` | Manages cash/bank ledger accounts; routes through posting |
| Payment Runtime (domain) | `StateTransitionRuntime`, `PaymentSettlementRuntime`, `PaymentDecisionRuntime`, `PaymentStateTransitionRuntime` | Pure state machines, no DB |
| Payment Lifecycle (authority) | `PaymentLifecycleService`, `IPaymentLifecycleService`, `PaymentStatusSaveChangesInterceptor` | Sealed payment-state mutation |
| Settlement | `PaymentSettlementBatch`, `PaymentSettlementOrders`, `RecordCodSettlementAsync`, `RecordGatewaySettlementAsync` | Batch settlement to bank |
| Receivables / Payables | `AccountsReceivable`, `SupplierPayables`, `MarketplaceVendorReceivable`, `MarketplaceVendorPayable`, `AffiliatePayable`; plus party `AccountLedger` | GL control accounts + customer/supplier sub-ledger |
| Commerce→Ledger | `CommerceAccountingService` (POS sale, purchase, supplier payment, refund, COD, wallet, settlement, CommerceHub transaction, party adjustment, reversal events) | Business op → journal |
| Direct GL Posting Bridges | `TreasuryTransferService`, `TreasuryOperationService`, `AffiliateAccountingConsumer`, `AffiliateSettlementOrchestrator`, `FinancialPostingService.ReverseJournalAsync` | Non-`CommerceAccountingService` callers of `PostJournalAsync` |
| Affiliate | `AffiliateCommissionLedger`, `AffiliateBalanceLedger`, `PayoutWallet`, `AffiliatePayoutRequest`, `AffiliateSettlementOrchestrator`, `AffiliatePayoutService`, `AffiliateAccountingConsumer`, `IAffiliateSettlementOrchestrator` | Commission lifecycle + payout + GL bridge |
| CommerceHub Finance | `CommerceHubFinancialTransaction`, `CommerceHubVendorSettlement`, `CommerceHubFinancialClassification`, `CommerceHubVendorPaymentRequest/Proof/Method`, `CommerceHubFinanceReconciliationJob` | Multi-vendor marketplace money |
| Refunds | `OrderRefund`, `RecordRefundAsync`, `PostEcommerceRefundAsync`, `RefundAllocation` | Customer refund → ledger |
| Expenses | `PostPosExpenseAsync` (CommerceAccountingService), `ResolveExpenseCashboxAccountCodeAsync` (expense→cashbox ledger) | Outflow to expense/GL |
| Opening Balance | `PostSupplierOpeningBalanceAsync`, `LedgerService.ApplyCustomerTransaction/ApplySupplierTransaction`, `Cashbox.OpeningBalance`, `CommerceHubVendorPaymentRequest` | Customer/supplier opening balances; cashbox/bank legacy opening writes are fail-closed/disabled |
| Suspense / Reconciliation | `TreasurySuspense`, `CommerceHubUnresolvedPaymentWebhook`, `StorefrontUnresolvedPaymentReconciliationJob`, `CommerceHubFinanceReconciliationJob`, `CommerceHubPayoutReconciliationHostedService` | Contain external-money drift |
| Reports | `FinancialReportService`, `IFinancialReportService`, `GeneralLedgerReport`, `TrialBalanceReport`, `BalanceSheetReport`, `ProfitAndLossReport`, `CashFlowReport`, `ClearingAgingReport` | Read from GL ledger |
| Finance Dashboards / Aggregates | `CommerceHubFinancialDashboardService`, `CommerceHubDailyFinancialAggregateRefreshService`, super-admin vendor finance read services | Derivative projections/aggregates; never GL authority |
| Outbox / Events | `AccountingOutboxMessage`, `AccountingOutboxService`, `AccountingOutboxProcessor`, `IEventPublisher`, `MarketplaceEventOutboxService` | Durable async financial posting + integration |

---

## 5. Money Sources & Destinations (Step 2 / Step 3)

### Sources (where money originates)
1. **POS Sale** — `PostPosSaleAsync` (Cash/Card/Mixed/Credit) → revenue recognized immediately; debit cashbox/clearing/AR, credit `PosSales`.
2. **Storefront Online Payment** — `PostOnlinePaymentCapturedAsync` → debit clearing, credit `EcommerceSales` on `Collected`.
3. **Storefront COD** — `PostCodDeliveredAsync` on delivery → debit `CodClearing`, credit `EcommerceSales`.
4. **Storefront Wallet (ManualWallet approval)** — `PostWalletApprovedAsync` → debit `WalletClearing`, credit `EcommerceSales`.
5. **Affiliate commission earned** — `AffiliateCommissionLedger.CreateEarned` (per SKU); *this is a liability owed by store, not cash yet*.
6. **CommerceHub vendor sale / commission** — `CommerceHubFinancialTransaction` (vendor payable, platform commission to `EcommerceSales`).
7. **Purchase from supplier** — `PostPosPurchaseAsync` → debit inventory/COGS, credit supplier payable / cash / bank.
8. **Supplier payment** — `PostSupplierPaymentAsync` → clears supplier payable.
9. **Supplier opening balance** — `PostSupplierOpeningBalanceAsync`.
10. **Manual cash receipt / cash adjustment** — via Treasury `DepositAsync`/`TransferAsync` (only debit/credit between active cash/bank treasury accounts).
11. **Resolved unresolved payment** — `PostResolvedUnresolvedPaymentAsync` clears `TreasurySuspense` → `EcommerceSales`.
12. **Refund reversals / purchase returns** — `PostPosRefundAsync`, `PostPurchaseReturnAsync` (negative money, offsets).
13. **Customer/supplier opening balance** — `LedgerService.ApplyCustomerTransaction` / `ApplySupplierTransaction` writes `AccountLedger` and party balance; accounting outbox can also enqueue `SupplierOpeningBalance` / related GL postings.
14. **Party ledger adjustment** — `StatementOfAccountController.CreateAdjustment` creates `PartyLedgerAdjustment`, updates `AccountLedger`/party balance, then enqueues `PartyLedgerAdjustment` accounting.
15. **Sale/Purchase/Expense reversal events** — `AccountingOutboxService.EnqueueSaleReversalAsync`, `EnqueuePurchaseReversalAsync`, `EnqueueExpenseReversalAsync`; critical outbox events processed by `AccountingOutboxProcessor`.
16. **Treasury transfer / clearing settlement / bank deposit** — `TreasuryTransferService.TransferAsync`, `SettleClearingAsync`, `DepositToBankAsync` post journals directly through `FinancialPostingService`.
17. **Affiliate GL earned/paid events** — `AffiliateCommissionEarnedAccountingConsumer` posts `AffiliateMarketingExpense` → `AffiliatePayable`; `AffiliateCommissionPaidAccountingConsumer` clears `AffiliatePayable` to cash/cashbox.
18. **Manual wallet/payment approval or rejection** — `AdminManualPaymentController` and manual payment application services admit, approve, or reject manual wallet proof under feature/certification gates; approved wallet payment flows to `WalletApproved` accounting.

### Destinations (where money ends)
- **Cashbox ledger account** (physical cash sales, cash expenses, cash deposits) — `CashboxLedgerAccountService`.
- **Bank account** (`BankAccount`) — via `PostGatewaySettlementAsync` (net of fees) and `PostCodSettlementAsync`.
- **CommerceHub vendor payable / bank** — `CommerceHubVendorSettlement` + `PostCommerceHubFinancialTransactionAsync`.
- **Affiliate payout wallet** (external rail: bank/wallet number) — `AffiliateBalanceLedger.Withdrawal` + `AffiliateSettlementOrchestrator`; **the GL entry is an `AffiliatePayable` debit, the external transfer is evidence only**.
- **TreasurySuspense** — holding account for unresolved/external money until reconciled.
- **GL control accounts** — `AccountsReceivable`, `SupplierPayables`, `MarketplaceVendorReceivable`, `MarketplaceVendorPayable`, `AffiliatePayable`, `GatewayFees`, `ShippingFees`, `CostOfGoodsSold`, `Inventory`, `Expenses`, `AccountsReceivableAdjustments`, `AccountsPayableAdjustments`, `BankFees`, `OpeningBalanceEquity`.
- **Party sub-ledger balances** — `Customer.Balance` / `Supplier.Balance` and `AccountLedger.RunningBalance` are destinations for customer/supplier statement flows; they are not GL `LedgerEntry` balances and must be reconciled separately.
- **Derivative dashboards/aggregates** — CommerceHub dashboard/current-balance views and daily aggregate refresh outputs are read/projection destinations, not authoritative stores.
- **Disabled/fail-closed opening paths** — `CashboxController.SetOpeningBalance` rejects non-zero cashbox opening balance (`OPENING_BALANCE_AUTHORITY_REQUIRED`); `BanksController.SaveOpeningBalances` returns 410 Gone for legacy bank opening-balance writes.

---

## 6. Money Authority Graph (Step 7)

```
SALES / ORDERS (POS, Storefront, CommerceHub)
        │ business event
        ▼
PAYMENT RUNTIME (StateTransitionRuntime)            ← DOMAIN STATE AUTHORITY (pure, deterministic)
        │ legal transition + deterministic id
        ▼
PAYMENT LIFECYCLE SERVICE (IPaymentLifecycleService) ← PAYMENT AUTHORITY (sealed DB mutation)
        │ serializable tx: PaymentIntent + Order.PaymentStatus + outbox
        ▼
ACCOUNTING OUTBOX (PostgreSQL, durable)             ← POSTING TRIGGER (not truth)
        │ processed by AccountingOutboxProcessor (batch, backoff)
        ▼
COMMERCE ACCOUNTING SERVICE (ICommerceAccountingService) ← TRANSLATION AUTHORITY (op → journal)
        │ PostingRequest (balanced Debit/Credit)
        ▼
FINANCIAL POSTING SERVICE (IFinancialPostingService)   ← LEDGER AUTHORITY (immutable journals)
        │
        ▼
LEDGER ACCOUNTS (Chart of Accounts)                 ← CANONICAL MONEY (PostgreSQL)
   ├── Cashbox dedicated account  (CASH AUTHORITY leaf)
   ├── Treasury Cash/Bank accounts (TREASURY AUTHORITY — routes through posting)
   ├── CommerceHub Vendor Payable/Receivable (MARKETPLACE VENDOR AUTHORITY)
   ├── AffiliatePayable (AFFILIATE COMMISSION AUTHORITY — GL side)
   └── Revenue / Clearing / Suspense / Expense (REPORTING AUTHORITY)

AFFILIATE:  CommissionLedger(Earned→Approved→Paid) ──► BalanceLedger.Withdrawal ──► PayoutWallet (external)
COMMERCEHUB: CommerceHubFinancialTransaction ──► VendorSettlement/Reconciliation ──► Bank/Vendor payable
REPORTS:     FinancialReportService reads GL LEDGER; CommerceHub dashboards/aggregates are derivative projections

PARALLEL PARTY SUB-LEDGER:
SALES/PURCHASES/STATEMENT CONTROLLERS ──► LedgerService ──► AccountLedger + Customer.Balance/Supplier.Balance
                                      └─► AccountingOutbox ──► CommerceAccountingService ──► FinancialPostingService
```

Authority inheritance: **downward only for GL.** The GL never asks Cashbox/Treasury/Affiliate/CommerceHub projections for truth. Those systems read or feed the Ledger; they never *own* GL balances. The explicit exception is the customer/supplier party sub-ledger: `LedgerService` owns `AccountLedger` and party `Balance` fields as a separate statement-of-account authority that must be reconciled with GL postings.

---

## 7. Financial Journey (Step 5) — traced per scenario

### 7.1 POS Sale (cash)
`PostPosSaleAsync` (Cash branch) → resolve warehouse cashbox ledger account → `PostingRequest`:
- `DEBIT <CashboxAccount>` (TotalAmount), `CREDIT PosSales` (TotalAmount).
- If items have cost: second journal `DEBIT CostOfGoodsSold`, `CREDIT Inventory`.
- Idempotency key `SALE:<tenant>:<invoiceId>[:opKey]`. Posted inside `FinancialPostingService` serializable tx.

### 7.2 Storefront — Online (Stripe/Paymob/Wallet)
Provider webhook → `HandlePaymentResultAsync(Succeeded)` → `HandleSuccessAsync` → if `Collected`: enqueue `OnlinePayment` accounting outbox → `PostOnlinePaymentCapturedAsync` → `DEBIT <StripeClearing/VisaClearing/WalletClearing>`, `CREDIT EcommerceSales`. Settlement later: `RecordGatewaySettlementAsync` → `PaymentSettlementBatch` → `PostGatewaySettlementAsync`: `DEBIT BankAccount (net)`, `CREDIT <clearing> (gross)`, `DEBIT GatewayFees (fees)`.

### 7.3 Storefront — COD
Order created `Pending`/`CashOnDelivery`. On delivery: `MarkCodDeliveredAsync` → `PostCodDeliveredAsync`: `DEBIT CodClearing`, `CREDIT EcommerceSales`. Settlement: `RecordCodSettlementAsync` → `PaymentSettlementBatch` (COD) → `PostCodSettlementAsync`: `DEBIT BankAccount (net)`, `CREDIT CodClearing (gross)`, `DEBIT ShippingFees (fees)`.

### 7.4 CommerceHub (multi-vendor marketplace)
Sale → `CommerceHubFinancialTransaction` (vendor payable bucket → `MarketplaceVendorPayable`; platform commission → `EcommerceSales`; promotion → `Expenses`). On payout: `CommerceHubVendorSettlement` (vendor bank/cash) → `PostCommerceHubFinancialTransactionAsync` clears `MarketplaceVendorPayable`, offsets `TreasurySuspense`. Unresolved provider captures route to `TreasurySuspense` then `CommerceHubFinanceReconciliationJob.ResolveLinkedUnresolvedCapturedPaymentsAsync` rebuilds allocations and clears suspense into vendor payable / revenue per tenant.

### 7.5 Affiliate commission + payout
Order collected with attribution → `AffiliateCommissionLedger.CreateEarned` (per SKU, `StoreTenantId`, append-only). Admin approves → `CreateApproved`. Payout executed (`AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync`): serializable tx → idempotency check on `BalanceLedger.Withdrawal` → `AffiliateBalanceLedger.CreateWithdrawal` (money leaves wallet) → mark `CommissionLedger` entries `Paid` (net of reversals) → `PostJournalAsync` (debit `AffiliatePayable`, credit bank/wallet clearing) → external transfer evidence stored. Payout wallet number/type is the **external rail only**; the GL is `AffiliatePayable`.

### 7.6 Purchase + Supplier payment
`PostPosPurchaseAsync` → debit `Inventory`/`CostOfGoodsSold`, credit `SupplierPayables` (or cash/bank). `PostSupplierPaymentAsync` clears `SupplierPayables`. `PostSupplierOpeningBalanceAsync` seeds payable.

### 7.7 Refund
`RecordRefundAsync` (lifecycle) → `OrderRefund` row (`IsPostSettlementRefund` flag) → `EnqueueSettledEcommerceRefundAsync`/`PostCommittedRefundAccountingFromSqlTruthAsync` → `PostEcommerceRefundAsync`: `DEBIT EcommerceSales`, `CREDIT <payment account or BankAccount if post-settlement>`. Pre-settlement refund credits the original clearing; post-settlement refund credits `BankAccount`. Affiliate commission reversals handled by `ReconcileAffiliateCommissionReversalsForRefundAsync`.

### 7.8 Manual cash / transfer / deposit
`TreasuryOperationService.DepositAsync/WithdrawAsync/TransferAsync` → build `PostingRequest` between two active same-tenant Cash/Bank treasury accounts → `PostJournalAsync`. `RecordExpenseAsync/CashInAsync/CashOutAsync` are **explicitly disabled** (throw) — outflows must come from certified liability/payment/accounting authority.

### 7.9 Expense
`PostPosExpenseAsync` → resolves cashbox ledger (or default) → journal to `Expenses`/`GeneralExpenses` with cashbox debit/credit. Expense reversal is a separate critical outbox event (`ExpenseReversal`) and must remain live until posted/reconciled.

### 7.10 Party AccountLedger / customer-supplier statements
Sales, purchases, returns, supplier payments, customer/supplier opening balances, and statement adjustments can call `LedgerService.CreateLedgerEntry`, `ApplyCustomerTransaction`, or `ApplySupplierTransaction`. `LedgerService` locks the party row, creates an `AccountLedger` row, calculates `RunningBalance`, and directly sets `Customer.Balance` or `Supplier.Balance`. This is not a GL `LedgerEntry`; it is the party statement sub-ledger. When the same business operation also needs GL accounting, controllers enqueue `AccountingOutboxMessage` (e.g., `SupplierOpeningBalance`, `PartyLedgerAdjustment`) so `CommerceAccountingService` can post the corresponding journal.

### 7.11 Party ledger adjustment
`StatementOfAccountController.CreateAdjustment` creates `PartyLedgerAdjustment`, mutates the party sub-ledger through `LedgerService`, and enqueues `PartyLedgerAdjustment` into the accounting outbox. GL side: `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` posts to receivable/payable adjustment accounts according to debit/credit adjustment type. Debug both `AccountLedger` and `JournalEntry` for adjustment mismatches.

### 7.12 Treasury transfer / clearing settlement / bank deposit
`TreasuryTransferService.TransferAsync` posts `DEBIT toAccount`, `CREDIT fromAccount`; `SettleClearingAsync` posts clearing → bank; `DepositToBankAsync` posts cashbox → bank. These are direct `FinancialPostingService.PostJournalAsync` callers, separate from `CommerceAccountingService`. They validate active same-tenant treasury account types (`Cashbox`, `Bank`, `Clearing`) before posting.

### 7.13 Affiliate accounting consumers
Affiliate commission events also bridge to GL through MassTransit consumers. `AffiliateCommissionEarnedAccountingConsumer` posts `DEBIT AffiliateMarketingExpense`, `CREDIT AffiliatePayable`. `AffiliateCommissionPaidAccountingConsumer` requires the earned journal to exist, then posts `DEBIT AffiliatePayable`, `CREDIT cash/cashbox`. This sits beside `AffiliateSettlementOrchestrator`; idempotency keys and out-of-order guards prevent replay/double-posting.

### 7.14 Manual wallet/payment review
Manual wallet payment is not simply a payment method flag. `AdminManualPaymentController` and manual payment application services admit proof, approve/reject under `Owner/Admin` authority, and are governed by `ManualPaymentOptions` feature/certification gates. Approval reaches the `WalletApproved` accounting path (`WalletClearing` → `EcommerceSales`); rejection blocks the payment state rather than recognizing revenue. `ManualWalletProofCleanupJob` handles stale proof evidence.

### 7.15 Opening balances and disabled legacy paths
Customer/supplier opening balances use `LedgerService` and `AccountLedger` plus accounting outbox. Cashbox and bank opening-balance legacy paths are fail-closed: `CashboxController.SetOpeningBalance` rejects non-zero opening balances with `OPENING_BALANCE_AUTHORITY_REQUIRED`; `BanksController.SaveOpeningBalances` returns 410 Gone and instructs operators to use ledger-backed treasury operations. `PostInventoryAdjustmentAsync` is also fail-closed until an authoritative SQL adjustment workflow exists.

---

## 8. Money State Machines (Step 6)

### 8.1 Payment status (e-commerce) — `PaymentStatus.cs`
`Pending → Authorized → Collected → Settled`; `Pending/CashOnDelivery → Collected`; `Collected → Refunded/PartiallyRefunded`; `Any → Failed`; `Pending → AwaitingVerification → (Verified→)Approved→Collected`; `Pending/Authorized → Expired`; `AmountMismatch` blocks fulfillment. **Only `Collected` recognizes revenue; only `Settled` means money hit the bank.**

### 8.2 Payment runtime transition machine — `StateTransitionRuntime.LegalTransitions`
Deterministic map: `Created→Pending/Cancelled/Expired`; `Pending→AwaitingCustomer/AwaitingProvider/Rejected/Cancelled/Expired/ManualReview/Recovery/Quarantined`; ... `Completed→SettlementPending/RefundPending/Settled`; `SettlementPending→Settled/Recovery/Quarantined`; `Settled→RefundPending`; `RefundPending→Refunded`. Validation enforces `TransitionFailure` set: `CurrentStateInvalid, DecisionInvalid, TransitionIllegal, OwnershipInvalid, DuplicateTransition, ReplayDetected`. Replays collapse to `Replayed`; illegal → `Rejected`.

### 8.3 Settlement runtime — `PaymentSettlementRuntime`
`Produce(transaction)` returns `SettlementStatus.Pending/Duplicate/Rejected/Replayed`. Guards: `ReplayDetected` (same transaction/payment), `DuplicateSettlement` (same reference), `OwnershipInvalid` (different session). Deterministic settlement id = hash(transactionId, type).

### 8.4 Affiliate commission ledger — `AffiliateCommissionLedger`
Append-only, no updates/deletes (DB trigger). Lifecycle `Earned → Approved → Paid`; `Reversed` cancels `Earned` and blocks approval/payment. One entry per `(OrderItemId, EntryType)` (unique idempotency key, no TenantId — global). `CommissionAmount = min(BaseAmount × Rate, MaxPerItem)`, rounded to 4 dp, always > 0.

### 8.5 Affiliate balance — `AffiliateBalanceLedger`
`CreateWithdrawal` (negative) is the *only* mutation that reduces available balance. CommissionLedger.Paid is a lifecycle flag and does **not** reduce available balance — only `BalanceLedger.Withdrawal` does (`AffiliatePayoutService.GetAvailableBalanceAsync` comment, lines 138-141).

### 8.6 CommerceHub vendor settlement
`CommerceHubVendorSettlement` (immutable create, EGP-only, idempotency + external-reference keys, `legacy:` prefix rejected). Status flow driven by reconciliation/hold-release workers (`CommerceHubFinanceReconciliationJob`, `CommerceHubPayoutReconciliationHostedService`).

### 8.7 Unresolved payment suspense
`CommerceHubUnresolvedPaymentWebhook` states: `PendingTenantResolution`, `RoutedToSuspense`, `ResolvedToPayment`, `SuspenseMatchedPaymentPendingClearance`. Recovery worker reloads locked rows in serializable tx and only then queues suspense clearance / releases marketplace holds.

### 8.8 Journal lifecycle (Ledger)
`JournalEntry`: created → (never updated/deleted) → `Reversed` (via `ReverseJournalAsync`, which posts a swapped journal and marks original `Reversed`). Immutable by design (`FinancialPostingService.ReverseJournalAsync`).

### 8.9 Party AccountLedger lifecycle
`LedgerService` creates `AccountLedger` entries and maintains `RunningBalance`. Opening balance entries are guarded by `EnsureOpeningBalanceNotAlreadyAppliedAsync`; duplicate opening balance throws `DuplicateOpeningBalanceException`. `RebuildPartyBalanceAsync` recalculates ordered `AccountLedger.RunningBalance`, `CustomerTransactions.BalanceAfter` / `SupplierTransactions.BalanceAfter`, and the party `Balance` field.

### 8.10 Affiliate payout request lifecycle
`AffiliatePayoutRequest` has `Pending → Approved/Rejected → Paid`; payment requires approved status, snapshot wallet details, and external evidence/allocation checks. `AffiliateBalanceLedger.Withdrawal` is the balance mutation; payout request status is lifecycle evidence.

### 8.11 Manual payment review lifecycle
Manual payment proof flows through admission/review, then approval or rejection. `ManualPaymentOptions` gates review/approval/rejection execution and certification. Approval enters wallet accounting; rejection leaves revenue unrecognized. Proof cleanup is handled by `ManualWalletProofCleanupJob`.

### 8.12 CommerceHub payment lifecycle authority
`CommerceHubPaymentLifecycleAuthority` mutates marketplace order and sub-order payment status (`Collected`, `Pending`, `Refunded`, `PartiallyRefunded`, `Settled`, `Failed`, `Rejected`) and aggregates sub-order statuses to marketplace status. This is distinct from storefront `PaymentLifecycleService`.

### 8.13 Payment runtime feature gates
`PaymentRuntimeFeatureFlags.Defaults` sets `NewPaymentRuntime`, `NewPolicyEngine`, `DualRunPayments`, `ReplayEngine`, and `ProviderAdapterV2` to `false`. The runtime state-machine code is implemented and authoritative when invoked, but activation is configuration/feature-gated.

### 8.14 Disabled/fail-closed money lifecycles
Cashbox non-zero opening balance is rejected before money can be created; bank opening-balance writes return 410 Gone; inventory adjustment accounting returns failed unless an authoritative SQL adjustment workflow exists.

---

## 9. Canonical Money Source & Data Ownership (Step 10)

- **Canonical GL financial source:** PostgreSQL `JournalEntry`/`LedgerEntry`/`Account` (the General Ledger). GL balances are mutated only by `FinancialPostingService` under `LedgerMutationScope` and `ApplicationDbContext.GuardTreasuryCashAuthority`.
- **Canonical party statement source:** `AccountLedger` plus `Customer.Balance`/`Supplier.Balance`, maintained by `LedgerService`, is a separate customer/supplier receivable/payable sub-ledger. It is not a GL `LedgerEntry` and is not posted by `FinancialPostingService`; debug it alongside GL when party balances differ.
- **Derived financial sources (never authoritative):**
  - MongoDB — order/affiliate/commercehub *read models* (customer visibility only).
  - Redis — caches, locks, session, rate-limit; non-authoritative, best-effort invalidation.
  - Elasticsearch — search/discovery only.
  - Cashbox `CurrentBalance`/`ExpectedBalance` — `[Obsolete]` shadow fields; must not be used.
  - `CommerceHubFinancialTransaction` rows — these ARE authoritative GL input (posted to ledger), but the *balances* live in Ledger accounts, not in the transaction rows.
  - CommerceHub financial dashboard/current-balance views and daily aggregate refresh outputs — derivative projections/aggregates, never GL authority.
- **Projections / caches:** rebuilt/repaired from SQL; tombstone/delete when SQL authority gone (`TenantLineageAuthority`, `ProductVariantLifecycleAuthority`, `AccountingLedgerAuthority`).
- **Outbox:** `AccountingOutboxMessage` is the durable bridge — messages are *triggers*; the processor reloads SQL truth (`AccountingLedgerAuthority`) before posting.
- **Authority boundaries:** cross-tenant FK edges rejected pre-commit (`ApplicationDbContext`); tenant co-ownership required for financial rows.

---

## 10. Integration Events (Step 9)

- **Durable outbox:** `AccountingOutboxMessage` (PostgreSQL) — critical types enumerated in `AccountingOutboxProcessor._criticalFinancialTruthEventTypes`: `PosSale`, `SaleReversal`, `PosPurchase`, `PurchaseReversal`, `SupplierPayment`, `SupplierOpeningBalance`, `PartyLedgerAdjustment`, `PosRefund`, `PurchaseReturn`, `PosExpense`, `ExpenseReversal`, `OnlinePayment`, `CodDelivered`, `WalletApproved`, `EcommerceRefund`, `UnresolvedOnlinePayment`, `ResolvedUnresolvedOnlinePayment`, `ResolvedUnresolvedCommerceHubPayment`, `GatewaySettlement`, `CodSettlement`, `CommerceHubLedgerPosting`. Failed critical messages are NOT dropped — they stay live for retry.
- **Processing:** `AccountingOutboxProcessor.ProcessPendingMessagesAsync` — batched, ordered by tenant/entity/version/occurredAt, exponential backoff, idempotency double-check against `PostingRequest.IdempotencyKey`.
- **Integration events:** `IEventPublisher` (MassTransit/RabbitMQ) — `GatewaySettlementCompletedEvent`, `OrderPaymentStatusChangedEvent`, `AffiliateEvents`, `CommerceHubEvents`, `WhatsAppPaymentApprovalRequestedEvent`. These fan out to Mongo/Redis/ES projections.
- **Marketplace outbox:** `IMarketplaceEventOutboxService` for CommerceHub-specific projections.
- **Background/hosted jobs (reconciliation/repair):**
  - `CommerceHubFinanceReconciliationJob` — unresolved captured payments → suspense clearance.
  - `StorefrontUnresolvedPaymentReconciliationJob` — storefront suspense recovery.
  - `CommerceHubPayoutReconciliationHostedService` — quarantined `Processing` payouts.
  - `CommerceHubRefundRecoveryHostedService` — refund recovery/compensation.
  - `CommerceHubPaymentCompensationOutboxWorker` — payment compensation outbox execution.
  - `CommerceHubDailyFinancialAggregateRefreshService` — derivative daily financial aggregate refresh.
  - `CheckoutPaymentIntentRecoveryHostedService` — repairs orders that committed before PaymentIntent creation completed.
  - `ManualWalletProofCleanupJob` and `RefundEvidenceCleanupJob` — cleanup stale financial proof/evidence.
  - `AffiliateAttributionReconciliationHostedService` — affiliate attribution reconciliation.
  - Storefront/CommerceHub order projection repair/reconciliation services — rebuild derivative Mongo/search views from SQL.
  - `InventoryReservationCleanupJob` — reservation release (recomposes SQL truth first).
  - `AccountingOutboxWorker` — drains accounting outbox.
- **Idempotency:** every journal uses a deterministic `IdempotencyKey`; posting checks pre-tx AND inside serializable tx (handles race). Settlement ids and affiliate ledger entries use deterministic hashes.
- **Compensation / rollback:** `ReverseJournalAsync` posts a reversal journal (swap debit/credit), marks original `Reversed`. Payment quarantine path (`QuarantineLateSuccessfulPaymentAsync`, `EnqueueQuarantinedCapturedPaymentSuspenseAsync`) holds mismatched provider money in suspense rather than overwriting.
- **Replay collapse:** `AccountingLedgerAuthority` gates replayed posting — collapses when current SQL payment/fulfillment/refund/settlement truth disagrees (no double post).

---

## 11. Security Model (Step 11)

- **Permissions/roles:** tenant-admin endpoints consume `ITenantContext.RequiredTenantId`; super-admin gated by `SuperAdminPlatformAuthority` (current SQL `SuperAdmins` row, `TokenVersion`); POS admin bound to `PosSessionAuthority` (current SQL session hash); CommerceHub finance/payment admin surfaces use `Owner,Admin,Accountant` roles while manual wallet approval/rejection write endpoints require `Owner,Admin`.
- **Approval:** wallet payments require manual payment admission/review plus approval (`AdminManualPaymentController`, manual payment application services) and are governed by `ManualPaymentOptions` execution/certification gates; affiliate payouts require `paidByUserId` (admin identity), approved payout request status, payout wallet snapshot validation, and external transfer evidence/allocation checks.
- **Audit:** `CommerceAccountingAudit`, `CommerceHubFinancialOperationAudit`, `CommerceHubVendorPaymentAuditLog`, `SuperAdminOperationalAuditMiddleware`, `PaymentStatusSaveChangesInterceptor`, party ledger rebuild/adjustment audit through `AuditService.LogAction`. Audit references appended on every state transition (`StateTransition.AuditReferences`).
- **Immutable records:** Ledger journals (no update/delete), `AffiliateCommissionLedger` (DB trigger + append-only), `CommerceHubFinancialTransaction`/`CommerceHubVendorSettlement` (immutable create), `CashboxTransaction` immutable history.
- **Tamper protection:** `LedgerMutationScope.Authorize()` — only the posting engine may mutate ledger balances; `PreventNegative` flag on accounts blocks negative balances; treasury operations require active same-tenant Cash/Bank treasury account.
- **Duplicate protection:** idempotency keys everywhere; `DuplicateTransition`, `ReplayDetected`, `DuplicateSettlement` failure codes; `EnsureSettlementBatchRetainsCurrentOrderLineageAsync` rejects settlement posting when batch header count drifts from live `PaymentSettlementOrders`.
- **Double-posting protection:** dual idempotency check (pre-tx + in-tx serializable) in `FinancialPostingService`; outbox processor re-checks before posting.
- **Financial validation:** balanced journals (`UnbalancedJournalException`), positive amounts only, active accounts only, EGP enforcement for CommerceHub, commission rate ∈ (0, 0.50], commission > 0.

---

## 12. Financial Consistency Audit (Step 12)

| Risk | Implementation control | Evidence |
|---|---|---|
| Double posting | Idempotency key checked pre-tx and inside serializable tx | `FinancialPostingService.cs` |
| Duplicate settlement | `DuplicateSettlement` + batch lineage check | `PaymentSettlementRuntime.cs`, `PaymentLifecycleService.EnsureSettlementBatchRetainsCurrentOrderLineageAsync` |
| Duplicate cash (cashbox) | Dedicated ledger account per cashbox; immutable `CashboxTransaction` | `CashboxLedgerAccountService.cs` |
| Duplicate commission | One entry per (OrderItemId, EntryType); idempotency key | `AffiliateCommissionLedger.cs` |
| Lost money / suspense | `TreasurySuspense` holds unresolved; hosted recovery workers | `CommerceAccountingService.PostResolvedUnresolvedPaymentAsync`, `CommerceHubFinanceReconciliationJob.cs` |
| Negative balance | `PreventNegative` on accounts; `ValidateNoNegativeBalances` | `FinancialPostingService.cs` |
| Race conditions | `Serializable` isolation on posting, payout, settlement, reconciliation | multiple files |
| Rollback | `LifecycleTransactionScope` / `BeginTransactionAsync` with rollback; outbox row stays unmodified so retry continues | `PaymentLifecycleService.cs` |
| Transaction scope | Every business op wraps mutation + outbox + events in one commit | `RecordGatewaySettlementAsync`, etc. |
| Idempotency | Deterministic ids/keys for settlement, journal, commission, payout | see above |
| Isolation | `IgnoreQueryFilters()` in outbox/background (no tenant filter reliance) | `FinancialPostingService.cs`, `AccountingOutboxProcessor.cs` |
| Atomicity | Posting is all-or-nothing; reversal posts a new balanced journal | `FinancialPostingService.cs` |
| Party sub-ledger consistency | `LedgerService` locks party row (`FOR UPDATE` on PostgreSQL), creates `AccountLedger`, sets party balance, and can rebuild running balances | `LedgerService.cs` |
| Party/GL drift | Party sub-ledger mutations and GL outbox postings are separate; debug both when statements differ from GL | `LedgerService.cs`, `AccountingOutboxService.cs` |
| Affiliate out-of-order paid posting | Paid consumer requires earned journal before clearing payable | `AffiliateAccountingConsumer.cs` |
| Disabled money-creation paths | Cashbox non-zero opening balance rejected; bank opening write returns 410; inventory adjustment accounting returns failed without SQL authority workflow | `CashboxController.cs`, `BanksController.cs`, `CommerceAccountingService.PostInventoryAdjustmentAsync` |
| Cross-tenant leak | `ApplicationDbContext` rejects cross-tenant financial FK edges; affiliate ledger uses `StoreTenantId` explicitly | `.ai-context/PROJECT_CONTEXT.md`; `AffiliateCommissionLedger.cs` |

---

## 13. Cross-Domain Dependencies (Step 8)

- **Sales → Payment → Ledger → Cashbox/Treasury:** a POS sale posts a journal that debits the warehouse cashbox ledger account.
- **Purchases → Supplier Payable → Ledger → Bank:** purchase posts to payable; supplier payment clears it. Supplier statements also update `AccountLedger`/`Supplier.Balance` through `LedgerService`.
- **Returns → Refund → Ledger → (Affiliate reversal):** `RecordRefundAsync` triggers `ReconcileAffiliateCommissionReversalsForRefundAsync`.
- **Warehouse/Inventory → COGS journal:** `PostPosSaleAsync` second journal moves `Inventory → CostOfGoodsSold` using immutable cost snapshot.
- **Customer/Order → Payment lifecycle:** `PaymentLifecycleService` finalizes inventory + coupons in the same tx as payment state. Coupon/promotion/discount authorities affect order totals before revenue recognition; they are financial inputs, not balance stores.
- **Customer/Supplier → Party sub-ledger → GL outbox:** `LedgerService` maintains `AccountLedger` and party balances; accounting outbox posts GL mirrors for supported operations (`SupplierOpeningBalance`, `PartyLedgerAdjustment`, etc.).
- **Affiliate → Commission ledger → Balance ledger → Payout wallet (external):** commission lifecycle feeds payout orchestrator.
- **CommerceHub → Vendor payable/receivable → Bank/vendor settlement → Reconciliation:** multi-vendor money is contained via suspense and per-tenant allocation.
- **Checkout → Payment Runtime → Settlement → Ledger:** serializable checkout refreshes current totals before commit.
- **Reporting/Analytics → Ledger or derivative aggregates:** `FinancialReportService` reads GL; CommerceHub dashboards/daily aggregates and analytics dashboards are derivative and must not outrank GL or SQL party sub-ledger authority.
- **Notifications/Search/Background jobs:** consumers of integration events; never financial authority.
- **Events:** `IEventPublisher` + `MarketplaceEventOutboxService` fan out; `AccountingOutboxProcessor` is the financial posting pipeline.

---

## 14. Common Failure Scenarios & Investigation Order (Step 13)

| Symptom | Likely cause | Where to look first |
|---|---|---|
| "Money is wrong" generally | Stale projection, GL mismatch, party sub-ledger drift, or unresolved outbox | Start with GL (`JournalEntry`/`LedgerEntry`) and, if customer/supplier money is involved, `AccountLedger`/party balances; projections last |
| Wrong cashbox balance | Reading obsolete `Cashbox.CurrentBalance` instead of ledger account | `CashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync` |
| Missing settlement | `PaymentSettlementBatch` not created or outbox failed | `PaymentLifecycleService.RecordGatewaySettlementAsync`; `AccountingOutboxMessages` |
| Refund mismatch | Pre/post-settlement credit account wrong; affiliate reversal missed | `PostEcommerceRefundAsync`, `ReconcileAffiliateCommissionReversalsForRefundAsync` |
| Affiliate commission missing/wrong | Attribution missing, `StoreTenantId` scoping, or lifecycle stuck at Earned | `AffiliateCommissionLedger`, `AffiliateBalanceLedger`, attribution |
| CommerceHub vendor mismatch | Suspense not cleared, per-tenant allocation drift | `CommerceHubFinanceReconciliationJob`, `CommerceHubUnresolvedPaymentWebhook` |
| Ledger mismatch | Direct GL write bypassed posting engine | `LedgerMutationScope`, `ApplicationDbContext.GuardTreasuryCashAuthority`; enumerate all `PostJournalAsync` callers |
| Customer/supplier statement mismatch | Party sub-ledger drift or missing GL mirror | `AccountLedger`, `Customer.Balance`, `Supplier.Balance`, `LedgerService.RebuildPartyBalanceAsync`, then `AccountingOutboxMessages` |
| Cashbox mismatch | Direct table write instead of treasury posting, or legacy opening-balance expectation | `CashboxLedgerAccountService`, `TreasuryOperationService` (CashIn/Out disabled), `CashboxController.SetOpeningBalance` non-zero rejection |
| Payment state stuck | Quarantined/AmountMismatch/Expired | `StateTransitionRuntime`, `PaymentStatusSaveChangesInterceptor` |
| Report mismatch | Report reads projection, not ledger | `FinancialReportService` |

---

## 15. Debugging Guide (Step 14) — "My money is wrong"

**Investigate in this order (truth flows downward):**

1. **General Ledger (PostgreSQL) is the GL truth.** Open `JournalEntry`/`LedgerEntry` for the entity. If the journal is correct, the GL is correct and the UI/projection may be wrong.
2. **Party sub-ledger when customer/supplier money is involved.** Inspect `AccountLedger`, `Customer.Balance`, `Supplier.Balance`, `CustomerTransactions.BalanceAfter`, `SupplierTransactions.BalanceAfter`, and `LedgerService.RebuildPartyBalanceAsync`. Customer/supplier statements are not derived solely from GL.
3. **Accounting Outbox.** Check `AccountingOutboxMessages` for the business event (PosSale, SaleReversal, PosPurchase, PurchaseReversal, SupplierPayment, SupplierOpeningBalance, PartyLedgerAdjustment, PosExpense, ExpenseReversal, OnlinePayment, GatewaySettlement, EcommerceRefund, CommerceHubLedgerPosting). If `ProcessedAtUtc` is null and it's a critical type, the posting worker hasn't run or is failing — check `AccountingOutboxProcessor` logs.
4. **Payment lifecycle.** Check `PaymentIntent` + `Order.PaymentStatus`. Only `Collected` recognizes revenue; only `Settled` means bank receipt. Use `StateTransitionRuntime` legal transitions to confirm the state is reachable, but remember new runtime execution is feature-gated.
5. **Settlement.** For gateway/COD, confirm `PaymentSettlementBatch` + `PaymentSettlementOrders` exist and batch header count matches live mappings (`EnsureSettlementBatchRetainsCurrentOrderLineageAsync`).
6. **Cashbox / Treasury.** Resolve the dedicated cashbox ledger account (`CashboxLedgerAccountService`). Never read `Cashbox.CurrentBalance`. Treasury mutations must have a corresponding journal; transfer/settlement/deposit paths may come from `TreasuryTransferService` as well as `TreasuryOperationService`.
7. **Affiliate.** Check `AffiliateCommissionLedger` (Earned/Approved/Paid), `AffiliateBalanceLedger.Withdrawal`, `AffiliatePayoutRequest`, `AffiliateAccountingConsumer` journals, and `AffiliateSettlementOrchestrator` payout evidence. Commission is per-SKU, keyed by `StoreTenantId`.
8. **CommerceHub.** Check `CommerceHubFinancialTransaction` + `CommerceHubVendorSettlement` + `TreasurySuspense` (unresolved money), then CommerceHub payment lifecycle/compensation workers and daily aggregate projections. Run/inspect `CommerceHubFinanceReconciliationJob`.
9. **Manual wallet/payment review.** For wallet/manual proof issues, inspect manual payment admission/review services, `ManualPaymentOptions`, `AdminManualPaymentController`, proof cleanup, and `WalletApproved` outbox posting.
10. **Disabled/fail-closed paths.** If the issue is opening balance or inventory adjustment, confirm whether the path is intentionally blocked: non-zero cashbox opening balance is rejected, bank opening writes return 410, and inventory adjustment accounting returns failed without authoritative SQL workflow.
11. **Projections last.** Only after GL + party sub-ledger + outbox + payment are confirmed correct, reconcile Mongo/Redis/ES/dashboards/aggregates (they are derivative; rebuild from SQL).

**Which system owns the truth?** → GL truth is owned by the **Ledger** (`FinancialPostingService`). Customer/supplier statement truth is owned by `LedgerService`/`AccountLedger`. Projections and dashboards are views.

---

## 16. Documentation Validation / Drift Report (Step 15)

Compared implementation against `.ai-context/*` and `docs/`. Findings:

- **README is stale** (per memory) but `.ai-context/PROJECT_CONTEXT.md` and `CROSS_SYSTEM_AUTHORITY_HARDENING.md` ARE current and match implementation (ledger-first, fail-closed, suspense containment). ✓ aligned.
- **Hidden behavior #1:** `TreasuryOperationService.RecordExpenseAsync/CashInAsync/CashOutAsync` throw — they are **disabled by design**. Docs say "treasury operations journal everything"; the caveat (direct cash-in/out/expense disabled) is only in code comments, not in user docs.
- **Hidden behavior #2:** `Cashbox.CurrentBalance`/`ExpectedBalance` are `[Obsolete]` shadow fields; any code/doc relying on them is wrong.
- **Hidden behavior #3:** Affiliate commissions are **global** (no `TenantId`, `StoreTenantId` instead). A doc implying "affiliate balance is tenant-scoped" is wrong.
- **Hidden behavior #4:** `AffiliateCommissionLedger.Paid` does NOT reduce available balance; only `AffiliateBalanceLedger.Withdrawal` does. Easy to mis-model.
- **Hidden behavior #5:** CommerceHub finance is **EGP-only** enforced (`FinancialCurrencyGuard`); multi-currency customer pricing is separate from CommerceHub GL.
- **Hidden behavior #6:** `Paid` enum value on `PaymentStatus` is `[Obsolete]` → maps to `Collected`; any doc using `Paid` is legacy.
- **Wrong assumption risk:** treating Mongo order/affiliate/commercehub documents as financial truth — they are derivative (explicitly stated in `.ai-context` but worth repeating).
- **Missing projection note:** `CommissionLedger.Paid` without matching `BalanceLedger.Withdrawal` is treated as a data-integrity anomaly by `AffiliatePayoutService` (suggests AF-38E backfill) — a real drift-detection signal.
- **Settlement header drift:** `PaymentSettlementBatch.OrderCount` is denormalized; posting now requires live `PaymentSettlementOrders` lineage (`SettlementOrderLineageMismatch` telemetry). Docs that trust the header alone are wrong.
- **Party sub-ledger caveat:** `LedgerService` writes `AccountLedger` and directly mutates `Customer.Balance` / `Supplier.Balance`; GL-only documentation is incomplete for receivable/payable statements.
- **Additional GL callers:** `TreasuryTransferService`, `TreasuryOperationService`, `AffiliateAccountingConsumer`, `AffiliateSettlementOrchestrator`, and `ReverseJournalAsync` call `PostJournalAsync` outside `CommerceAccountingService`.
- **Disabled opening balance caveat:** cashbox non-zero opening balance and legacy bank opening-balance writes are fail-closed/disabled.
- **Feature-gated runtime caveat:** new payment runtime components exist but default to disabled in `PaymentRuntimeFeatureFlags.Defaults`.
- **Inventory adjustment caveat:** `PostInventoryAdjustmentAsync` rejects accounting without an authoritative SQL adjustment workflow.

---

## 17. Hidden Implementation Behavior (synthesis)

1. **Two-phase posting:** lifecycle commits local state + outbox in one tx; accounting journal is posted *post-commit* from SQL truth (`PostCommitted*FromSqlTruthAsync`). Failure there leaves outbox unmodified → retry.
2. **Suspense-first for external money:** any provider capture that can't be safely matched is routed to `TreasurySuspense`, never silently posted.
3. **Deterministic ids:** settlement ids, journal idempotency keys, affiliate ledger entries all derive from content hashes → safe retries.
4. **Append-only commission ledger with DB trigger** — no update/delete path exists at the API surface (public setters removed).
5. **Affiliate payout is serializable + balance-checked + idempotency-conflict-detecting** — a conflicting withdrawal throws a hard financial-inconsistency error rather than overwriting.
6. **`CommerceHubFinancialTransaction` always offsets `TreasurySuspense`** (primary vs offset account swap by direction) — suspense is the universal holding pen.
7. **Refund accounting differs pre vs post settlement:** pre-settlement credits the original clearing; post-settlement credits `BankAccount` (money already left the platform).
8. **Outbox critical-event list is hand-maintained** — any new money-posting event type must be added to `_criticalFinancialTruthEventTypes` or it may be dropped on failure.
9. **`LedgerMutationScope.Authorize()`** gates all GL balance mutation to the posting engine — a guard, not just convention.
10. **`LedgerService` is a live party sub-ledger authority** — it inserts `AccountLedger` and directly sets `Customer.Balance` / `Supplier.Balance` outside `FinancialPostingService`.
11. **Party balance rebuild mutates historical running balances** — `LedgerService.RebuildPartyBalanceAsync` recalculates `AccountLedger.RunningBalance`, `CustomerTransactions.BalanceAfter`, and `SupplierTransactions.BalanceAfter`.
12. **`TreasuryTransferService` is a separate GL posting bridge** — transfer, clearing settlement, and bank deposit do not go through `CommerceAccountingService`.
13. **Affiliate MassTransit consumers post GL** — earned commission debits `AffiliateMarketingExpense` and credits `AffiliatePayable`; paid commission clears payable to cash/cashbox and blocks out-of-order delivery until earned journal exists.
14. **Cashbox opening balance is fail-closed** — non-zero `SetOpeningBalance` returns `OPENING_BALANCE_AUTHORITY_REQUIRED`; positive CashIn branch below the guard is unreachable in current implementation.
15. **Bank opening-balance writes are disabled** — `BanksController.SaveOpeningBalances` returns 410 Gone.
16. **New payment runtime defaults are disabled** — runtime code is present but feature-gated.
17. **Outbox reversal/adjustment events are critical** — `SaleReversal`, `PurchaseReversal`, `ExpenseReversal`, and `PartyLedgerAdjustment` are financial truth events.
18. **Manual payment review is feature/certification-gated** — approval/rejection cannot be modeled as simple status updates.
19. **CommerceHub daily financial aggregates are derivative** — useful for dashboards, never GL authority.
20. **Inventory adjustment accounting is intentionally rejected** until an authoritative SQL adjustment workflow exists.

---

## 18. Architectural Risks

- **Manual financial repair is a dangerous primitive** (per `.ai-context`): direct SQL/bulk mutation, full Mongo replace, raw ES writes, global repair — all require reviewed boundaries; drift here can corrupt ledger truth.
- **Outbox critical-type list drift:** adding a money event without registering it in `AccountingOutboxProcessor._criticalFinancialTruthEventTypes` risks silent loss on failure.
- **CommerceHub vendor reconciliation is complex** (per-tenant suspense allocation, hold release) — a fragile area; depends on hosted workers running.
- **Legacy shadow fields** (`Cashbox.CurrentBalance`, `PaymentStatus.Paid`) remain in the model — a trap for new code.
- **Affiliate global scoping** (`StoreTenantId`, no TenantId) is a deliberate exception to the tenant model; cross-tenant reporting must use `StoreTenantId` correctly or commissions mis-attribute.
- **EGP enforcement** on CommerceHub finance means non-EGP marketplace money must be converted/contained before GL posting — a conversion risk if rates differ.
- **Tenant co-ownership** for financial FK edges is enforced pre-commit; a missed guard is a cross-tenant leak (CRITICAL, per memory).
- **Dual-ledger mental model risk:** GL ledger and party `AccountLedger` coexist; treating GL as the only place to inspect customer/supplier statements will miss live party balance mutations.
- **Party/GL drift risk:** controllers can update party sub-ledger and enqueue GL accounting; failed outbox processing can leave party statement and GL temporarily divergent.
- **Affiliate duplicate-path risk:** MassTransit accounting consumers and payout orchestrator both touch affiliate GL posting; idempotency and production ownership must remain explicit.
- **Feature-flag risk:** payment runtime code being present does not prove it is active; operational behavior depends on feature configuration.
- **Opening-balance misunderstanding risk:** cashbox and bank legacy opening-balance writes are disabled/fail-closed; operators must use certified ledger-backed authority.
- **Discount/tax/accounting opacity risk:** coupons/promotions/discounts change order totals before revenue recognition, but no dedicated discount contra-revenue account was confirmed in the architecture evidence.
- **Inventory adjustment gap:** accounting method exists but fails closed; do not claim inventory adjustment posting is operational without an authoritative SQL adjustment workflow.

---

## 19. FINAL DELIVERABLES

### Executive findings
- Shopixy money is **ledger-centric**, not cashbox-centric. Cashbox is a leaf.
- All canonical GL money flows through one immutable double-entry engine (`FinancialPostingService`); customer/supplier statements also use the `LedgerService` / `AccountLedger` party sub-ledger.
- Payment, Treasury, Affiliate, CommerceHub are *authorities over their own state* but **not over GL balances** — those live in the General Ledger; customer/supplier statement balances live in the party `AccountLedger` sub-ledger.

### Canonical financial authority
**The PostgreSQL General Ledger** (`JournalEntry`/`LedgerEntry`/`Account`), posted exclusively by `FinancialPostingService`, translated primarily by `CommerceAccountingService` and also fed by `TreasuryTransferService`, `TreasuryOperationService`, affiliate accounting consumers, `AffiliateSettlementOrchestrator`, and reversal posting; gated by `AccountingLedgerAuthority`, `LedgerMutationScope`, and `ApplicationDbContext.GuardTreasuryCashAuthority`. Customer/supplier statement balances are separately owned by `LedgerService` / `AccountLedger`.

### Money lifecycle discovered
Business event → Payment Runtime or lifecycle authority (feature-gated where applicable) → Payment/party/CommerceHub lifecycle mutation + outbox → Commerce Accounting or direct GL bridge → Ledger (immutable, balanced) and/or AccountLedger party sub-ledger → Cashbox/Treasury/CommerceHub/Affiliate/party statements → reports, dashboards, projections (derivative).

### Financial domains discovered
Ledger, Party Sub-Ledger, Chart of Accounts, Cashbox, Treasury, TreasuryTransferService, Payment Runtime, Payment Lifecycle, Manual Payment Review, Settlement, Receivables/Payables, Commerce→Ledger, Affiliate, Affiliate Accounting Consumers, CommerceHub Finance, CommerceHub Payment Lifecycle, Refunds, Expenses, Opening Balance (including disabled/fail-closed paths), Suspense/Reconciliation, Reports, Dashboards/Aggregates, Outbox/Events.

### Money journey discovered
POS sale, Storefront online, Storefront COD, Storefront wallet/manual review, CommerceHub multi-vendor, Affiliate commission+payout+accounting consumers, Purchase+supplier payment, customer/supplier AccountLedger, party ledger adjustment, refund, sale/purchase/expense reversals, treasury transfer/clearing settlement/bank deposit, opening-balance disabled paths, manual treasury transfer, expense — all traced in §7.

### Financial authority graph discovered
See §6 — downward-only authority for GL; Ledger at the root for GL balances, with explicit side-lane party sub-ledger authority (`LedgerService` / `AccountLedger`) for customer/supplier statements.

### Runtime integrity findings
Serializable isolation, dual idempotency check, balanced-journal validation, negative-balance prevention, append-only ledgers, suspense containment, replay-collapse via `AccountingLedgerAuthority`, deterministic ids, DbContext GL mutation guards, affiliate out-of-order paid blocking, and party sub-ledger row locking/rebuild routines. Strong against GL double-posting; party/GL drift remains a documented reconciliation risk.

### Hidden implementation behavior
Disabled treasury CashIn/Out/Expense; obsolete cashbox shadow balances; global affiliate ledger (`StoreTenantId`); `CommissionLedger.Paid` ≠ balance reduction; EGP-only CommerceHub finance; obsolete `PaymentStatus.Paid`; suspense-offset CommerceHub postings; two-phase post-commit accounting; live party `AccountLedger`; direct `Customer.Balance`/`Supplier.Balance`; `TreasuryTransferService`; affiliate accounting consumers; disabled cashbox/bank opening writes; feature-gated payment runtime; critical reversal/adjustment outbox events; fail-closed inventory adjustment accounting.

### Documentation inconsistencies
`.ai-context` docs are current and aligned on ledger-first authority; README is stale (known). Code-only caveats (disabled treasury methods, shadow balances, global affiliate scoping, settlement header drift, party sub-ledger, disabled opening-balance writes, runtime feature gates, affiliate accounting consumers, reversal/adjustment events, inventory adjustment fail-closed behavior) are not consistently surfaced in user-facing docs.

### Architectural risks
Dangerous-primitive manual repair, outbox critical-type list drift, CommerceHub reconciliation fragility, legacy shadow fields, affiliate global-scoping pitfalls, EGP conversion risk, tenant co-ownership guard completeness, dual GL/party-ledger mental model, party/GL drift, affiliate duplicate-path risk, feature-flag ambiguity, disabled opening-balance misunderstanding, discount/tax accounting opacity, inventory adjustment fail-closed gap.

### Confirmation
- **Canonical file:** `C:\Users\fadya\source\repos\PointOfSale\.ai\hermes\architecture\financial-architecture.md`
- **Audit findings merged:** verified findings from `.ai/hermes/audits/financial-completeness-audit.md` are now incorporated so this file remains the single canonical financial reference.
- **No implementation behavior is implied beyond cited implementation evidence.**
