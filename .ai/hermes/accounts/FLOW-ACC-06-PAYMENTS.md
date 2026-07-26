# FLOW-ACC-06 — Payments (POS Admin) Architecture Discovery

> Canonical, implementation-backed architecture reference for the Shopixy ERP **Payments** domain (Customer Payments, Supplier Payments, Payment Allocation, Settlement, Outstanding Balances, Party Ledger, Financial Posting).
> Every conclusion below is traced to a file:line in the live codebase. Where behavior is absent it is explicitly marked **NOT IMPLEMENTED**. No generic ERP behavior was assumed.

> **SCOPE BOUNDARY (per task):** Covers ERP party payments (money moving to/from customers and suppliers) and their financial posting. Excludes: storefront/online order payment intents (`PaymentsController` intents/COD, `PaymentLifecycleService`, gateway adapters Stripe/Paymob/Fawry, CommerceHub payments), which are a separate **Order Payment** runtime. Sales and Purchases invoice creation are out of scope (see FLOW-SALES / FLOW-PURCHASES). General Ledger internals referenced only where required (see FLOW-ACC-04). Opening Balance covered in FLOW-ACC-05.

## Canonical source files (all evidence drawn from these)

- `PointOfSale.Core/Entities/SupplierPayment.cs` — supplier payment record (table `SupplierPayments`)
- `PointOfSale.Core/Entities/PartyLedgerAdjustment.cs` — manual customer/supplier adjustment record
- `PointOfSale.Core/Entities/AccountLedger.cs` — unified party sub-ledger (table `AccountLedgers`)
- `PointOfSale.Core/Entities/CustomerTransaction.cs` / `SupplierTransaction.cs` — per-party transaction history
- `PointOfSale.Core/Enums/TransactionType.cs` — `PaymentReceived`, `PaymentMade`, `CreditNote`, `DebitNote`, etc.
- `PointOfSale.Core/Enums/PaymentMethod.cs` — `Cash`, `Card`, `BankTransfer`, `Mixed`, `Credit`
- `PointOfSale.Core/Enums/PartyType.cs` — `Customer` / `Supplier`
- `PointOfSale.API/Controllers/SuppliersController.cs` — `POST /suppliers/{id}/payments` (the only implemented party-payment write endpoint)
- `PointOfSale.API/Controllers/StatementOfAccountController.cs` — SOA query, party balance, adjustment, outstanding balances
- `PointOfSale.API/Controllers/SalesController.cs` — customer `PaymentReceived` ledger entry at invoice time
- `PointOfSale.API/Services/LedgerService.cs` — `ApplyCustomerTransaction` / `ApplySupplierTransaction` / `CreateLedgerEntry`
- `PointOfSale.API/Services/Payments/PaymentAllocationService.cs` — order-item-level allocation (NOT invoice/party allocation)
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs` — GL posting (`PostSupplierPaymentAsync`, `PostPartyLedgerAdjustmentAsync`)
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs` — `EnqueueSupplierPaymentAsync`, `EnqueuePartyLedgerAdjustmentAsync`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs` — background dispatch to posting
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs` — double-entry journal engine
- `PointOfSale.Core/Interfaces/Accounting/IAccountResolver.cs` — `AccountCodes` constants
- `PointOfSale.Infrastructure/Migrations/20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments.sql` — schema
- `PointOfSale.Web/src/pages/Payments.jsx`, `OutstandingBalances.jsx`, `StatementOfAccount.jsx`, `src/services/soaApi.js`

---

## 1. Executive Summary

**What is a Payment?** In the Shopixy ERP layer there is **no single unified "Payment" aggregate**. "Payment" is realized through several distinct implemented mechanisms:

1. **Supplier Payment** — `SupplierPayment` entity, written by `POST /suppliers/{id}/payments` (`SuppliersController.CreateSupplierPayment`, line 155-317). This is the only first-class, durable, standalone party-payment record with its own table and idempotency index.
2. **Customer Payment (at point of sale)** — NOT a standalone entity. Recorded as a `TransactionType.PaymentReceived` ledger credit **inline during sale invoice creation** (`SalesController.cs:369-397`). There is no standalone "receive customer payment" endpoint implemented in the backend.
3. **Party Ledger Adjustment** — `PartyLedgerAdjustment` entity (`CreditNote`/`DebitNote`/`CreditAdjustment`/`DebitAdjustment`) via `POST /StatementOfAccount/adjustment`. Manual balance corrections, not funded payments.

**Who owns it?** No dedicated `PaymentService`/`IPaymentLifecycleService` for ERP party payments. Ownership is split: the **controller** (`SuppliersController`, `StatementOfAccountController`, `SalesController`) orchestrates the write; `ILedgerService` owns the party sub-ledger mutation; `CommerceAccountingService` + `FinancialPostingService` own the General Ledger posting.

**What does it own?** A supplier payment owns its own immutable record (amount, method, reference, idempotency key) and triggers exactly one `PaymentMade` ledger entry + one accounting-outbox message.

**What does it NOT own?** It does **not** own the General Ledger (owned by `FinancialPostingService` / `JournalEntry`), does **not** own party running balance truth (derived from `AccountLedgers`), does **not** own Statement of Account (a read model), and does **not** perform invoice-level allocation (NOT IMPLEMENTED — see §7).

**Financial source of truth:** The **General Ledger** (`JournalEntries` + `LedgerEntries` + `LedgerAccounts.CurrentBalance`) is the double-entry financial truth. The **`AccountLedgers` table** is the authoritative *party sub-ledger* (running receivable/payable per party). `Customer.Balance` / `Supplier.Balance` scalar fields are **caches** rebuilt from `AccountLedgers` (`LedgerService.RebuildTrackedBalancesAsync`, line 366-428). Statement of Account and Outstanding Balances are **read projections** over `AccountLedgers`.

---

## 2. Discovery — Implemented Components

| Component | Type | Evidence | Status |
|---|---|---|---|
| `SupplierPayment` | Entity / table | `Entities/SupplierPayment.cs`; migration `20260408193000` | **Implemented** |
| `PartyLedgerAdjustment` | Entity / table | `Entities/PartyLedgerAdjustment.cs` | **Implemented** |
| `AccountLedger` | Party sub-ledger entity | `Entities/AccountLedger.cs` | **Implemented** |
| `CustomerTransaction` / `SupplierTransaction` | Per-party history | `Entities/CustomerTransaction.cs`, `SupplierTransaction.cs` | **Implemented** |
| `POST /suppliers/{id}/payments` | API (supplier payment) | `SuppliersController.cs:155` | **Implemented** |
| `POST /StatementOfAccount/adjustment` | API (adjustment) | `StatementOfAccountController.cs:534` | **Implemented** |
| `POST /StatementOfAccount/opening-balance` | API | `StatementOfAccountController.cs:263` | **Implemented** (FLOW-ACC-05) |
| `POST /StatementOfAccount/query` (SOA) | Read model | `StatementOfAccountController.cs:63` | **Implemented** |
| `GET /StatementOfAccount/balance/{partyType}/{partyId}` | Read | `StatementOfAccountController.cs:222` | **Implemented** |
| `GET /StatementOfAccount/outstanding/{partyType}` | Read | `StatementOfAccountController.cs:925` | **Implemented** |
| Customer `PaymentReceived` (inline at sale) | Ledger write | `SalesController.cs:369-397`, `:1561-1576` | **Implemented** |
| `ILedgerService` | Sub-ledger service | `Services/LedgerService.cs` | **Implemented** |
| `CommerceAccountingService.PostSupplierPaymentAsync` | GL posting | `CommerceAccountingService.cs:369` | **Implemented** |
| `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` | GL posting | `CommerceAccountingService.cs:505` | **Implemented** |
| `IAccountingOutboxService.EnqueueSupplierPaymentAsync` | Outbox | `AccountingOutboxService.cs:167` | **Implemented** |
| `IAccountingOutboxService.EnqueuePartyLedgerAdjustmentAsync` | Outbox | `AccountingOutboxService.cs:181` | **Implemented** |
| `FinancialPostingService.PostJournalAsync` | Double-entry engine | `FinancialPostingService.cs:39` | **Implemented** |
| Standalone customer-payment endpoint `/Payments/receive` | API | frontend calls it (`soaApi.js`); **no backend route** | **NOT IMPLEMENTED** |
| Standalone supplier-payment endpoint `/Payments/make` | API | frontend calls it; **no backend route** | **NOT IMPLEMENTED** |
| Payment history `/Payments/customer/{id}` & `/Payments/supplier/{id}` | API | frontend calls them; **no backend route** | **NOT IMPLEMENTED** |
| Invoice-level payment allocation (party) | Behavior | — | **NOT IMPLEMENTED** |
| Settlement (party payment) | Behavior | only order/gateway settlement exists | **NOT IMPLEMENTED** (for party payments) |
| `ReversePayment` / `VoidPayment` / `DeletePayment` (party) | Behavior | — | **NOT IMPLEMENTED** |
| `ReceiptVoucher` / `PaymentVoucher` numbered documents | Entity | — | **NOT IMPLEMENTED** (only `ReferenceNumber` string) |

---

## 3. Payment Identity

### Supplier Payment (`SupplierPayment.cs`)
| Attribute | Field | Evidence |
|---|---|---|
| Identity | `Id` (Guid, PK) | `SupplierPayment.cs` via `BaseEntity` |
| Tenant ownership | `TenantId` (Guid, NOT NULL) | `SupplierPayment.cs:15`; `ITenantOwned` |
| Party | `SupplierId` (Guid) | `SupplierPayment.cs:17` |
| Optional invoice link | `PurchaseInvoiceId` (Guid?) | `SupplierPayment.cs:23` — null = account-level payment |
| Effective date | `PaymentDate` (DateTime) | `SupplierPayment.cs:25` |
| Amount | `Amount` (decimal, numeric(18,2)) | `SupplierPayment.cs:26`; rounded via `RoundCurrency` (`SuppliersController.cs:250`) |
| Payment method | `PaymentMethod` enum | `SupplierPayment.cs:27` |
| Idempotency | `IdempotencyKey` (string, unique per tenant) | `SupplierPayment.cs:33`; unique idx `IX_SupplierPayments_TenantId_IdempotencyKey` |
| Reference number | `ReferenceNumber` (string?) | `SupplierPayment.cs:35`; default `SPAY-{yyyyMMddHHmmss}` (`SuppliersController.cs:222`) |
| Notes | `Notes` (string?) | `SupplierPayment.cs:36` |
| Audit | `CreatedAt`/`UpdatedAt`/`IsDeleted` | `BaseEntity` + migration |
| Relationships | `Supplier`, `PurchaseInvoice?` nav | `SupplierPayment.cs:38-39` |
| Currency | **NOT MODELED** — no currency field; single-currency per tenant assumed | — |
| Status | **NOT MODELED** — no payment status field; the record is create-only | — |

### Party Ledger Adjustment (`PartyLedgerAdjustment.cs`)
`Id`, `TenantId`, `PartyType`, `PartyId`, `TransactionType` (CreditNote/DebitNote/CreditAdjustment/DebitAdjustment), `AdjustmentDate`, `Amount`, `IdempotencyKey` (unique per tenant), `ReferenceNumber`, `Notes`. No status, no currency.

### Customer Payment (inline)
Has **no dedicated identity**. It exists only as an `AccountLedger` row (`TransactionType.PaymentReceived`) + a `CustomerTransaction` row, both keyed by `ReferenceId = invoice.Id` and `ReferenceNumber = invoiceNumber` (`SalesController.cs:369-396`). There is no `CustomerPayment` table.

---

## 4. Domain Ownership

| Concern | Owner (implementation) | Evidence |
|---|---|---|
| Supplier Payment record | `SuppliersController.CreateSupplierPayment` (write); `SupplierPayments` table | `SuppliersController.cs:155-317` |
| Customer Payment (record) | **No owner** — no entity; inline ledger write only | `SalesController.cs:369` |
| Party Ledger (`AccountLedgers`) | `LedgerService` (single writer) | `LedgerService.cs:196-278` |
| Party running balance (`RunningBalance`) | `LedgerService.ApplyPartyTransactionAsync` | `LedgerService.cs:218,231` |
| Party balance cache (`Customer/Supplier.Balance`) | `LedgerService.SetTrackedPartyBalance` / `RebuildTrackedBalancesAsync` | `LedgerService.cs:239,430` |
| Statement Of Account | `StatementOfAccountController` (read projection over `AccountLedgers`) | `StatementOfAccountController.cs:63-217` |
| Outstanding Balances | `StatementOfAccountController.GetOutstandingBalances` (read projection) | `StatementOfAccountController.cs:925` |
| Allocation (party/invoice) | **NOT IMPLEMENTED** | — |
| Settlement (party) | **NOT IMPLEMENTED** | — |
| Journal / GL posting | `CommerceAccountingService` → `FinancialPostingService` | `CommerceAccountingService.cs:369`, `FinancialPostingService.cs:39` |
| Financial truth | General Ledger (`JournalEntries`/`LedgerEntries`/`LedgerAccounts`) | `FinancialPostingService.cs:170-224` |
| Reports (aging/outstanding) | `ReportsController`, `DashboardController` (read over `AccountLedgers`) | `ReportsController.cs:717+`, `DashboardController.cs:148` |

---

## 5. Lifecycle (implemented stages only)

**Supplier Payment (`POST /suppliers/{id}/payments`):**
1. **Authorize** — `[Authorize(Roles="Owner,Admin,Manager")]` (`SuppliersController.cs:156`).
2. **Idempotency claim** — `Idempotency-Key` header required, validated `^[A-Za-z0-9-]+$` ≤100 chars; scoped key `SUPPLIER:{tenantId}:{operationId}`; `IFinancialMutationIdempotencyService.TryClaimAsync` inside a DB transaction (`:162-187`).
3. **Validate** — amount > 0 (`:191`); method must be concrete (reject `Credit`/`Mixed`, `:200`); supplier exists (`:209`).
4. **Funding availability** — `ValidateSupplierFundingAvailabilityAsync` checks the funding account is active and has sufficient balance (`:228`, `:690-755`).
5. **Persist payment** — new `SupplierPayment` added (`:243-259`).
6. **Post to party sub-ledger** — `ApplySupplierTransaction(TransactionType.PaymentMade, debit=Amount, credit=0)` (`:261`) → decreases payable.
7. **Write history** — `SupplierTransaction` row with `BalanceAfter` (`:271`).
8. **Enqueue GL posting** — `EnqueueSupplierPaymentAsync` within the same transaction (`:288`).
9. **Save + commit + audit** — `SaveChangesAsync`, `LogCreate`, `CompleteFinancialMutationAsync` (`:289-316`).

**Customer Payment:** only the **Create** stage exists, and only as a side-effect of sale invoice creation (`SalesController.cs:360-397`). No standalone create/read/search/update/delete lifecycle.

**Adjustment (`POST /StatementOfAccount/adjustment`):** Authorize → idempotency claim → validate type & amount & party → persist `PartyLedgerAdjustment` → `Apply{Supplier|Customer}Transaction` → history row → `EnqueuePartyLedgerAdjustmentAsync` → commit (`StatementOfAccountController.cs:534-760`).

**Read / Search:** SOA `POST /query`, balance, outstanding (§2). **Update / Delete / Reverse / Void / Refund / Cancel of a party payment:** **NOT IMPLEMENTED.** Records are create-only; the only reversal primitive is at GL level (`FinancialPostingService.ReverseJournalAsync`) and is not wired to any party-payment endpoint.

---

## 6. Aggregate Analysis

- **Is Payment an aggregate?** Weakly. `SupplierPayment` is an isolated, immutable record with an idempotency boundary, but it does **not** encapsulate its own ledger/GL invariants — those are enforced by `LedgerService` and `FinancialPostingService`. It behaves as an **evidence/idempotency record**, not a rich aggregate root.
- **Is Allocation a separate aggregate?** No party/invoice allocation aggregate exists. `PaymentAllocationService` allocates across **OrderItems** for storefront orders (`PaymentAllocationService.cs:84-236`) — a different bounded context (order money tracking), NOT party-payment allocation.
- **Party Ledger** (`AccountLedger`) is the true consistency boundary for running balance; single-writer is `LedgerService`.
- **Journal** (`JournalEntry` + `LedgerEntry`) is the GL aggregate — immutable, POSTED→REVERSED only (`JournalEntry.cs:9-16,98,119`).
- **Outstanding Balance / Statement** are **not aggregates** — they are derived read models over `AccountLedgers`.

| Item | Owning boundary |
|---|---|
| Payment (supplier) | `SupplierPayment` record (create-only) |
| Allocation | OrderItem (storefront only); party allocation NOT IMPLEMENTED |
| Settlement | NOT IMPLEMENTED (party) |
| Party Ledger | `AccountLedgers` (via `LedgerService`) |
| Journal | `JournalEntries`/`LedgerEntries` (via `FinancialPostingService`) |
| Outstanding Balance | derived (read model) |
| Statement | derived (read model) |

---

## 7. Payment Behavior (implementation-backed)

- **Customer Payment:** Recorded inline at sale as `PaymentReceived` credit to the customer sub-ledger (reduces receivable) — `SalesController.cs:369-397`, `:1561-1576`. Amount = `ResolveCustomerLedgerPaymentAmount(invoice)` (excludes returned cash change). **No standalone customer-payment collection endpoint exists.**
- **Supplier Payment:** `PaymentMade` debit to supplier sub-ledger (reduces payable) — `SuppliersController.cs:261-269`.
- **Partial Payment:** Supported implicitly at the *balance* level — any amount reduces the running party balance; there is no invoice-linked partial tracking for party payments. `PurchaseInvoiceId` is always set `null` in the implemented path (`SuppliersController.cs:248`), so payments are **account-level only**, never invoice-allocated.
- **Multiple Payments:** Supported — each call creates a new `SupplierPayment` + ledger entry; balance is additive (`LedgerService.GetCurrentBalanceAsync`, `:280-317`).
- **Payment Allocation (party/invoice):** **NOT IMPLEMENTED.** `PaymentAllocationService` is order-item allocation for storefront orders, unrelated to party payments.
- **Running Balance:** `AccountLedger.RunningBalance` computed as `currentBalance + debit - credit` per entry (`LedgerService.cs:218`); mirrored to `CustomerTransaction/SupplierTransaction.BalanceAfter`.
- **Outstanding Balance:** Derived — `Σ(DebitAmount - CreditAmount)` per party over `AccountLedgers`, filtered `Balance != 0` (`StatementOfAccountController.cs:949-958`).
- **Settlement:** **NOT IMPLEMENTED** for party payments (settlement exists only for gateway/COD order flows — `EnqueueGatewaySettlementAsync`/`EnqueueCodSettlementAsync`, `AccountingOutboxService.cs:461-473`).
- **Receipt Generation:** **NOT IMPLEMENTED** — no receipt/voucher document. Only a free-text `ReferenceNumber` (auto `SPAY-{timestamp}` if omitted).
- **Reference Numbers:** Supplier payment `SPAY-{yyyyMMddHHmmss}`; adjustments `CN/DN/CA/DA-{timestamp}` (`StatementOfAccountController.cs:623-634`). Not gap-free sequences.
- **Payment Status:** **NOT MODELED** — party payments have no status field; they are terminal on creation.

---

## 8. Financial Behavior

**Two-tier ledger model:**
1. **Party sub-ledger** (`AccountLedgers`, via `LedgerService`) — the debit/credit running balance per customer/supplier. Written synchronously in the request transaction.
2. **General Ledger** (`JournalEntries`/`LedgerEntries`, via `FinancialPostingService`) — double-entry financial truth. Posted **asynchronously** by the accounting outbox processor.

**Supplier Payment journal** (`CommerceAccountingService.PostSupplierPaymentAsync`, `:369-421`):
- **Debit** `SupplierPayables` (2010) — reduce payables.
- **Credit** funding account — `Cash` → tenant default cashbox account; `Card` → `VisaClearing` (1110); `BankTransfer` → `BankAccount` (1030). `Credit`/`Mixed` throw (`:380-382`).
- Idempotency key `SUPPLIER_PAYMENT:{tenantId}:{paymentId}`.

**Party Ledger Adjustment journal** (`PostPartyLedgerAdjustmentAsync`, `:505-559`):
- Customer → control `AccountsReceivable` (1410), offset `AccountsReceivableAdjustments` (6120).
- Supplier → control `SupplierPayables` (2010), offset `AccountsPayableAdjustments` (6130).
- Debit vs credit direction from `IsDebitAdjustment(TransactionType)` (`:514`).

**Debit/Credit conventions:**
- Customer sub-ledger: Debit increases receivable (Sale); Credit decreases (PaymentReceived) — `CustomerTransaction.cs:23-30`.
- Supplier sub-ledger: Credit increases payable (Purchase); Debit decreases (PaymentMade) — `SupplierTransaction.cs:23-30`.

**BalanceAfter:** snapshot on each `CustomerTransaction`/`SupplierTransaction` row; rebuildable deterministically by ordered replay (`LedgerService.RebuildTrackedBalancesAsync`, `:366-428`).

**Financial truth:** GL. **Party balance** = cache over `AccountLedgers`. **Statement/Outstanding** = read models. General Ledger participation is guaranteed via the transactional outbox (§11).

---

## 9. Constraints (implementation-backed)

| Rule | Enforcement | Evidence |
|---|---|---|
| Positive amount | `Amount <= 0` → 400 | `SuppliersController.cs:191`; SOA adjustment `:580`; opening balance `:268` |
| Concrete funding method | reject `Credit`/`Mixed` for supplier payment | `SuppliersController.cs:200`; GL throws `:380` |
| Existing party | supplier/customer must exist & not deleted | `SuppliersController.cs:209`; SOA `:594-607` |
| Sufficient funds | funding account balance ≥ amount | `SuppliersController.cs:741` |
| Active funding account | inactive account → 400 | `SuppliersController.cs:724` |
| Idempotency (party payment) | `Idempotency-Key` header required; `SUPPLIER:{tenant}:{op}`; unique DB index | `SuppliersController.cs:162-187`; migration `IX_SupplierPayments_TenantId_IdempotencyKey` |
| Idempotency (GL journal) | unique on `(TenantId, IdempotencyKey)`, double-checked in serializable tx | `FinancialPostingService.cs:56-121` |
| Tenant isolation | `TenantId` NOT NULL on all rows; `RequiredTenantId`; global query filters | `AccountLedger.cs:15`; `LedgerService.cs:223` |
| Overpayment | **NOT VALIDATED** — payments are account-level, no cap against invoice/balance | (absence in `SuppliersController.cs:191-269`) |
| Existing invoice link | `PurchaseInvoiceId` always null in implemented path — no invoice-existence check | `SuppliersController.cs:248` |
| Allocation rules | **NOT IMPLEMENTED** | — |
| Settlement rules | **NOT IMPLEMENTED** (party) | — |
| Opening-balance uniqueness | one opening balance per party | `LedgerService.EnsureOpeningBalanceNotAlreadyAppliedAsync:319-358` |
| Balanced journal (debits=credits) | `UnbalancedJournalException` | `FinancialPostingService.cs:437-441` |
| Journal ≥ 2 lines, one side > 0 | validation | `FinancialPostingService.cs:389-435` |

---

## 10. SQL Transactions

**Party sub-ledger write (`LedgerService.ApplyPartyTransactionAsync`, `:196-278`):**
- **Isolation:** `ReadCommitted` when it owns the transaction (`:210`); otherwise joins the caller's ambient transaction (`ownsTransaction` flag).
- **Atomicity:** ledger row insert + party balance cache update committed together (`:238-245`).
- **Concurrency:** pessimistic row lock on the party — Postgres `SELECT ... FOR UPDATE`, SQL Server `WITH (UPDLOCK, ROWLOCK, HOLDLOCK)` (`LoadCustomerForUpdateAsync:472-497`, supplier equivalent).
- **In-flight additivity:** reads pending `AccountLedger` change-tracker entries so multiple entries in one unit of work chain correctly (`GetCurrentBalanceAsync:285-300`).
- **Rollback:** on any exception (`:268-277`).

**Supplier payment endpoint:** wraps the whole operation (idempotency claim → payment insert → ledger apply → outbox enqueue → save) in a single `BeginTransactionAsync` (`SuppliersController.cs:174`), committed via `CompleteFinancialMutationAsync`. The nested `ApplySupplierTransaction` detects the ambient transaction and does **not** open a second one.

**GL posting (`FinancialPostingService.PostJournalInternalAsync`, `:90-253`):**
- **Isolation:** `Serializable` (`:97`).
- **Retries:** EF Core execution strategy (`CreateExecutionStrategy`) wraps the transaction for transient-fault retry (`:84-87`).
- **Concurrency:** accounts locked/loaded ordered by Id to prevent deadlocks (`:126-136`); idempotency re-checked inside the transaction (`:106-121`).
- **Ledger mutation guard:** `LedgerMutationScope.Authorize()` gates all balance mutations (`:95`).

**Outbox claim (`AccountingOutboxProcessor`):** per-message transaction with Postgres `pg_advisory_xact_lock` on `(tenant, businessEntity)` (`:226-227`) and aggregate-version ordering (`:230-234`) so earlier events post first.

---

## 11. Events

- **Transactional outbox** (`AccountingOutboxMessages` table) is the sole bridge from party-payment writes to GL posting. Enqueue happens **inside** the business transaction (`SuppliersController.cs:288`, `StatementOfAccountController.cs:721`).
- **Supplier Payment** → event type `"SupplierPayment"`, idempotency `SUPPLIER_PAYMENT:{tenant}:{paymentId}` (`AccountingOutboxService.cs:167-172`).
- **Party Ledger Adjustment** → event type `"PartyLedgerAdjustment"`, key `PARTY_ADJUSTMENT:{tenant}:{adjustmentId}` (`:181-186`).
- **Supplier Opening Balance** → `"SupplierOpeningBalance"` (`:174-179`).
- **Consumers / dispatch:** `AccountingOutboxProcessor.DispatchMessageAsync` (`:706-724`) routes `"SupplierPayment"` → `ProcessSupplierPaymentAsync` → `CommerceAccountingService.PostSupplierPaymentAsync`; `"PartyLedgerAdjustment"` → `ProcessPartyLedgerAdjustmentAsync` → `PostPartyLedgerAdjustmentAsync`.
- **Critical financial-truth guarantee:** these event types are in `_criticalFinancialTruthEventTypes` (`:50-73`), so they remain live/re-tried until posted or explicitly reconciled — they are never silently dropped even if failed.
- **Settlement / posting events for party payments:** beyond the outbox posting above, **no domain events (e.g. `PaymentReceivedEvent`) are published** for ERP party payments. (Order-payment lifecycle has its own events — out of scope.)

---

## 12. Runtime Architecture (implementation-backed)

**Supplier Payment (fully implemented):**
```
POST /suppliers/{id}/payments  (Owner/Admin/Manager, Idempotency-Key)
        ↓
BeginTransaction + Financial idempotency claim
        ↓
Validate (amount>0, method concrete, supplier exists, funding sufficient/active)
        ↓
Insert SupplierPayment
        ↓
LedgerService.ApplySupplierTransaction(PaymentMade, debit=Amount)  → AccountLedgers + Supplier.Balance cache
        ↓
Insert SupplierTransaction (BalanceAfter)
        ↓
AccountingOutbox.EnqueueSupplierPaymentAsync   (same tx)
        ↓
SaveChanges + Commit + Audit
        ⋮  (async, background)
AccountingOutboxProcessor → ProcessSupplierPaymentAsync
        ↓
CommerceAccountingService.PostSupplierPaymentAsync
        ↓
FinancialPostingService.PostJournalAsync  (Serializable)
   Dr SupplierPayables (2010) / Cr funding account (cashbox|1110|1030)
        ↓
JournalEntry + LedgerEntries + LedgerAccount.CurrentBalance   (General Ledger = truth)
        ↓
(Read models) StatementOfAccount / Outstanding / Aging reports over AccountLedgers
```

**Customer Payment (partial):** only the inline sale path exists —
```
POST /sales  → LedgerService.CreateLedgerEntry(PaymentReceived, credit) → AccountLedgers + CustomerTransaction
```
There is **no** `POST /Payments/receive` runtime (frontend references a non-existent endpoint — §16).

---

## 13. Browser Test Specification

**Environment note (BLOCKER):** The `Payments.jsx` page (route `/payments`) is wired to `paymentsApi` which calls `/Payments/receive`, `/Payments/make`, `/Payments/customer/{id}`, `/Payments/supplier/{id}` — **none of these backend routes exist** (`soaApi.js:61-98` vs `PaymentsController.cs` which only exposes `intents`, `cod/confirm`, `status`). Expect the ERP Payments UI to fail with 404/405. Tests must assert this until either the backend endpoints are added or the frontend is repointed to `/suppliers/{id}/payments`.

**Scenario A — Supplier payment (working path, via API):**
- Assert: `POST /suppliers/{id}/payments` with `Idempotency-Key` header, `{amount>0, paymentMethod: Cash|Card|BankTransfer}` returns 201 with `BalanceAfter` reduced by amount.
- Assert: supplier running balance (`GET /StatementOfAccount/balance/Supplier/{id}`) decreased by amount.
- Assert (eventual): a `JournalEntry` with `SourceType="SupplierPayment"` posts Dr 2010 / Cr funding account.

**Negative cases:**
- Amount ≤ 0 → 400 "must be greater than zero".
- `paymentMethod = Credit|Mixed` → 400 "concrete funding method".
- Missing/invalid `Idempotency-Key` → 400.
- Duplicate `Idempotency-Key` (same request) → idempotent replay (no double ledger entry / single journal).
- Insufficient funds in funding account → 400 `INSUFFICIENT_FUNDS`.
- Inactive funding account → 400 `ACCOUNT_INACTIVE`.
- No cashbox configured (Cash) → 400 `NO_CASHBOX_CONFIGURED`.

**Outstanding validation:** `GET /StatementOfAccount/outstanding/Supplier` — assert party appears only when balance ≠ 0; `x-total-debit`/`x-total-credit` headers correct.

**Journal validation:** every posted journal balances (Σdebit = Σcredit); immutable (no PUT/DELETE); reversal only via `ReverseJournalAsync`.

**Settlement validation:** N/A for party payments — assert no settlement endpoint exists (NOT IMPLEMENTED).

**Adjustment:** `POST /StatementOfAccount/adjustment` — CreditNote reduces receivable/increases payable per direction; invalid `AdjustmentType` → 400.

---

## 14. Domain Ownership Matrix

| Object | Write Authority | Financial Truth | Read Model | Aggregate? |
|---|---|---|---|---|
| Supplier Payment | `SuppliersController` + `SupplierPayments` | GL (posted async) | payment list (NOT IMPLEMENTED endpoint) | Weak (evidence record) |
| Customer Payment | `SalesController` (inline only) | GL via sale posting | SOA | No (no entity) |
| Party Ledger Adjustment | `StatementOfAccountController` | GL (posted async) | SOA | Weak |
| Party Ledger (`AccountLedgers`) | `LedgerService` (sole writer) | authoritative sub-ledger | — | Yes (consistency boundary) |
| Party Balance (`Customer/Supplier.Balance`) | `LedgerService` | cache (rebuildable) | — | No |
| Statement Of Account | — (read only) | — | over `AccountLedgers` | No |
| Outstanding Balance | — (read only) | — | over `AccountLedgers` | No |
| Journal / GL | `FinancialPostingService` | **the** financial truth | GL reports | Yes (immutable) |
| Allocation (party) | — | — | — | NOT IMPLEMENTED |
| Settlement (party) | — | — | — | NOT IMPLEMENTED |

---

## 15. Canonical Pattern

Shopixy party payments follow a **two-tier ledger + transactional-outbox** pattern:

1. **Synchronous tier** — the controller, inside one serialized DB transaction, writes the payment evidence record, mutates the **party sub-ledger** (`AccountLedgers`, pessimistically locked per party), snapshots per-party history (`*Transaction.BalanceAfter`), and enqueues an accounting-outbox message. Financial idempotency is enforced by a claim table + unique DB indices.
2. **Asynchronous tier** — the `AccountingOutboxProcessor` picks up the message under an advisory lock in aggregate-version order and posts an immutable, balanced double-entry **journal** to the General Ledger via `FinancialPostingService` (Serializable, idempotent by key).

The **General Ledger is the financial truth**; the party sub-ledger is the authoritative *receivable/payable* store; `Customer/Supplier.Balance` is a rebuildable cache; Statement of Account, Outstanding Balances, and aging are pure read projections. There is **no unified Payment aggregate, no invoice-level allocation, and no party-payment settlement/reversal/void** in the implemented ERP layer.

---

## 16. Documentation Validation (corrections)

| ❌ Misconception | ✅ Implementation reality |
|---|---|
| There is a unified `Payment` aggregate / `PaymentService`. | No. Supplier payments (`SuppliersController`), inline customer payments (`SalesController`), and adjustments (`StatementOfAccountController`) are separate paths; no shared aggregate/service. |
| Payment directly edits balances. | Payment posts through `LedgerService` (sub-ledger) and, async, `FinancialPostingService` (GL). |
| Outstanding Balance is financial truth. | It is a read projection (`Σ debit-credit`) over `AccountLedgers`. |
| Party Balance is financial truth. | `Customer/Supplier.Balance` is a cache, rebuildable via `RebuildTrackedBalancesAsync`. |
| Statement Of Account owns balances. | SOA is a read model; owns nothing. |
| Payments are allocated to invoices. | NOT IMPLEMENTED — `PurchaseInvoiceId` is always null; payments are account-level. |
| The `/payments` UI collects customer & supplier payments. | The UI calls `/Payments/receive` & `/Payments/make` which **do not exist** in the backend; only `POST /suppliers/{id}/payments` works. This is a live frontend/backend contract gap. |
| Payments can be reversed/voided/deleted. | NOT IMPLEMENTED at party-payment level; only GL `ReverseJournalAsync` exists and is not wired. |
| Payments become immutable after posting. | The party-payment *records* are effectively immutable (create-only, no update endpoints); GL journals are explicitly immutable (POSTED→REVERSED only). |
| `PaymentAllocationService` allocates party payments. | It allocates storefront **order-item** money — different context. |

---

## 17. Final Architecture Conclusion

- **Payment Authority:** Distributed across controllers (`SuppliersController` for supplier payments; `SalesController` for inline customer payments; `StatementOfAccountController` for adjustments). No central payment authority/service.
- **Settlement Authority:** NOT IMPLEMENTED for party payments (gateway/COD settlement exists only in the order-payment runtime).
- **Financial Truth:** The General Ledger (`JournalEntries`/`LedgerEntries`/`LedgerAccounts.CurrentBalance`), posted via `FinancialPostingService`. Party sub-ledger (`AccountLedgers`) is authoritative for receivable/payable; party `Balance` fields are caches.
- **Runtime:** Synchronous sub-ledger write + transactional outbox → asynchronous double-entry GL posting.
- **Aggregate Ownership:** `AccountLedgers` (party balance, single-writer `LedgerService`) and `JournalEntry` (GL, immutable) are the real aggregates; `SupplierPayment`/`PartyLedgerAdjustment` are idempotent evidence records.
- **Transaction Model:** Per-party pessimistic locking at `ReadCommitted` for the sub-ledger; `Serializable` + execution-strategy retries for GL posting; advisory-locked, version-ordered outbox dispatch.
- **Projection Strategy:** SOA, Outstanding, aging, dashboard = read projections over `AccountLedgers`; `BalanceAfter`/`RunningBalance` snapshots rebuildable by ordered replay.
- **Integrity Strategy:** Financial idempotency claim table + unique DB indices (`IX_SupplierPayments_TenantId_IdempotencyKey`, journal `(TenantId, IdempotencyKey)`); balanced-journal enforcement; critical-financial-truth outbox events never dropped; fail-closed tenant isolation (`RequiredTenantId`, NOT NULL `TenantId`).
- **Audit Strategy:** `IAuditService.LogCreate` on payment/adjustment creation; balance rebuilds logged with before/after; immutable journals + reversal-only GL provide the accounting audit trail.

### Investigation goal answers
- **Is Payment an Aggregate?** No unified aggregate; `SupplierPayment` is a weak evidence record. The real aggregates are `AccountLedger` and `JournalEntry`.
- **Is Allocation a separate Aggregate?** No party allocation exists; order-item allocation is a different context.
- **Does Payment generate JournalEntry?** Yes — supplier payments & adjustments, async via outbox → `FinancialPostingService`.
- **Does Payment use FinancialPostingService?** Yes (via `CommerceAccountingService`).
- **Does Payment use AccountingOutbox?** Yes.
- **Does Payment update Party Ledger?** Yes (`AccountLedgers` synchronously).
- **Does Payment update Outstanding Balance?** Indirectly — Outstanding is derived from the ledger it updates.
- **Does Payment update Statement Of Account?** No — SOA is read-only over the ledger.
- **Does Payment update General Ledger?** Yes, asynchronously.
- **Can Payment be reversed?** NOT IMPLEMENTED at party level (only GL `ReverseJournalAsync`, unwired).
- **Can Payment be deleted?** No delete endpoint (soft-delete flag exists on entity but no path).
- **Can Payment be edited?** No — create-only.
- **Can Payment be partially allocated?** No (account-level only; no invoice allocation).
- **Is Overpayment allowed?** Yes — not validated against any invoice/balance cap.
- **Is Multi-payment supported?** Yes — additive.
- **Are Payment Methods extensible?** Limited to the `PaymentMethod` enum (`Cash`, `Card`, `BankTransfer`, `Mixed`, `Credit`); supplier payments accept only the first three. Adding a method requires enum + GL funding-account mapping changes.
- **Is Idempotency enforced?** Yes — header-required claim + unique indices + GL idempotency key.
- **Does Payment become immutable after posting?** Yes in practice — records are create-only; GL journals are immutable.

---

*End of FLOW-ACC-06-PAYMENTS.md — all conclusions traced to implementation; absent behavior explicitly marked NOT IMPLEMENTED.*
