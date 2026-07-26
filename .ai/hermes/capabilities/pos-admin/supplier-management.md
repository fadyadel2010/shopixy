# Supplier Management Capability (POS Admin ERP Suppliers)

> Reverse-engineered from implementation. Implementation is the source of truth.
> Domain: `Supplier` = the purchasing / business supplier identity used by POS Admin.
> It is NOT a CommerceHub Vendor, Marketplace Vendor, Service Marketplace Provider, Affiliate, or Storefront Seller.

## Purpose

Supplier Management owns the ERP "purchasing party" identity inside POS Admin: a business you buy
goods from. It exists to:

- Maintain the master record of business suppliers (name, contact, tax, terms, notes).
- Track how much the tenant **owes each supplier** (accounts payable / outstanding balance).
- Record supplier financial movements: opening balance, purchases, purchase returns, and supplier payments.
- Provide a ledger-backed statement of account (SOA) and outstanding-balance read model.
- Feed the accounting engine (general ledger journal postings) through the transactional outbox.

What it owns:
- The `Supplier` master entity (tenant-scoped).
- The `Supplier` running `Balance` (debit-minus-credit, ledger sign convention).
- `SupplierTransaction` history and `AccountLedger` entries (`PartyType = Supplier`).
- `SupplierPayment` records (account-level payments to a supplier).
- Supplier opening balance (one-time seed entry).

What it does NOT own:
- Inventory quantities (owned by Inventory / Warehouse; purchase posts affect stock but Supplier does not manage stock).
- Product pricing / catalog.
- Sales, customers, returns-by-customers.
- CommerceHub vendor payouts/earnings, Marketplace vendor finance, Affiliate commissions, Service Marketplace (internal POS-to-POS) finances — these are entirely separate identities and ledgers.
- Accounting journal storage itself — Supplier only enqueues postings to the accounting outbox; the journal is owned by `CommerceAccountingService`/`FinancialPostingService`.

---

## Business Scope

Backed by these entities (`PointOfSale.Core/Entities`):

- `Supplier` (ITenantOwned, BaseEntity) — master record.
  - `TenantId` (NOT NULL, DB-enforced). `Name` (required, max 200). `Phone` (max 50, nullable).
    `Email` (max 200, nullable). `Address` (nvarchar(max), nullable). `TaxNumber` (max 50, nullable).
    `Balance` (decimal(18,2)) — ledger sign convention. `OpeningBalanceAmount` (decimal?, nullable).
    `BalanceType` ("Debit"/"Credit", nullable). `OpeningBalanceDate` (DateTime?, nullable).
    `PaymentTermsDays` (int, default 0). `Notes` (nullable). `IsDeleted` (soft delete flag from BaseEntity).
  - Navigation: `PurchaseInvoices`, `Transactions` (`SupplierTransaction`).
- `SupplierTransaction` (ITenantOwned) — per-supplier transaction history mirror of the ledger.
  - `DebitAmount`, `CreditAmount`, `BalanceAfter` (running balance at that row),
    `TransactionType`, `TransactionDate`, `ReferenceId`, `ReferenceNumber`, `IsOpeningBalance`, `Notes`.
- `SupplierPayment` (ITenantOwned) — immutable payment record.
  - `SupplierId`, `PurchaseInvoiceId` (nullable; null = account-level payment), `PaymentDate`,
    `Amount`, `PaymentMethod`, `IdempotencyKey` (unique per tenant), `ReferenceNumber`, `Notes`.
- `AccountLedger` (ITenantOwned, unified ledger) — authoritative financial journal for all parties.
  - For suppliers: `PartyType = Supplier (1)`, `PartyId = Supplier.Id`, `TransactionType`,
    `DocumentNumber`, `DebitAmount`, `CreditAmount`, `RunningBalance`, `ReferenceId`, `UserId`.

Fields that do NOT exist (proven by absence): `SupplierCode`, attachments, import/export, archived state.

Scoped operations (controller `SuppliersController`):
- `GET api/suppliers` — paged, search by name/email/phone (as-no-tracking). Excludes soft-deleted via global query filter.
- `GET api/suppliers/{id}` — single.
- `POST api/suppliers` — create (roles Owner/Admin/Manager).
- `PUT api/suppliers/{id}` — update master fields only (no balance mutation).
- `POST api/suppliers/{id}/payments` — supplier payment (idempotent).
- `GET api/suppliers/opening-balances` — list active suppliers + opening-balance status.
- `POST api/suppliers/opening-balances` — batch opening-balance seed (idempotent).
- `GET api/suppliers/{id}/can-delete` — deletion pre-check.
- `DELETE api/suppliers/{id}` — soft delete (hardened; see Business Rules).

Statement of account and balances are exposed via `StatementOfAccountController` (`POST api/statement-of-account/query`,
`GET api/statement-of-account/balance/{partyType}/{partyId}`, `POST api/statement-of-account/opening-balance`)
and a separate supplier-balance summary endpoint (see IMPLEMENTATION DISCOVERY). These read from `AccountLedger`.

---

## Authority Boundaries

| Concern | Authority | Notes |
|---|---|---|
| Supplier master record | `SuppliersController` + `Supplier` table | Create/update/soft-delete. No direct balance writes. |
| Supplier running balance | `AccountLedger` (SQL) via `LedgerService.ApplySupplierTransaction` | `Supplier.Balance` is a **projection** maintained by the ledger service, never written directly by controllers. Controllers are forbidden from `supplier.Balance += / -= / =` (enforced by architecture tests `SupplierFinancialSafetyHardeningTests`). |
| Supplier ledger (journal) | `AccountLedger` rows with `PartyType = Supplier` | Single source of truth for outstanding balance. |
| Opening balance | `AccountLedger` OpeningBalance entry + `Supplier.OpeningBalanceAmount` flag | One per supplier (DB/unique-index + code guard `EnsureOpeningBalanceNotAlreadyAppliedAsync`). |
| Purchase history | `PurchaseInvoice` (PurchasesController) | Writes supplier ledger via `ApplySupplierTransaction` (Purchase = Credit, PaymentMade = Debit). |
| Purchase returns | `Return` (PurchaseReturnsController) | Writes supplier ledger (PurchaseReturn = Debit). |
| Accounting (GL) | `CommerceAccountingService` + `FinancialPostingService` via Accounting Outbox | Supplier enqueues `SupplierPayment` / `SupplierOpeningBalance` / `PartyLedgerAdjustment` messages; the background processor posts journals. Supplier does not own the GL. |
| Outstanding / payables read | `AccountLedger` aggregation (SOA controller) | Read-only projection computed from ledger, not from `Supplier.Balance`. |
| Analytics / reports | Consumers of `AccountLedger` / `SupplierTransaction` (e.g. PurchasesBySupplierReport) | Presentation only. |
| Notifications | Not supplier-owned | No supplier-specific notification path found in implementation. |

Definitions:
- **Authority** = the component that writes the canonical state (`LedgerService` for balances/ledger; `SuppliersController` for master data; `CommerceAccountingService` for GL).
- **Projection** = `Supplier.Balance`, SOA summary, report aggregates — derived from the ledger.
- **Evidence** = `AccountLedger` + `SupplierTransaction` + `SupplierPayment` rows + accounting journal (outbox).
- **Presentation** = SOA controller responses, web pages (`Suppliers.jsx`, `SupplierOpeningBalance.jsx`, `PurchasesBySupplierReport.jsx`).

---

## Supplier Lifecycle

Discovered implementation flow:

```
Create Supplier  (POST /api/suppliers)        Balance = 0, IsDeleted = false
     |
     v
Set Opening Balance  (POST /api/suppliers/opening-balances  OR  POST /api/statement-of-account/opening-balance)
     |                                              One-time seed: Debit => we owe; Credit => they owe us.
     |                                              Writes AccountLedger(OpeningBalance) + SupplierTransaction(IsOpeningBalance).
     v
Purchases  (PurchasesController.CreatePurchase)   Credit to supplier ledger (we owe more).
     |                                              SupplierPayment ledger writes for any paid amount (Debit).
     v
Purchase Returns  (PurchaseReturnsController)      Debit to supplier ledger (we owe less).
     |
     v
Supplier Payments  (POST /api/suppliers/{id}/payments)   Debit (PaymentMade) to supplier ledger.
     |
     v
Statement of Account  (POST /api/statement-of-account/query)   Read-only outstanding/running balance.
     |
     v
Soft Delete  (DELETE /api/suppliers/{id})   Only if Balance == 0 AND no invoices AND no transactions.
```

Notes:
- Opening balance is optional at create time but must be seeded before it can be reported realistically; the
  same `PartyType=Supplier` opening-balance path also exists in `StatementOfAccountController.CreateOpeningBalance`.
- Purchases, returns, and payments are WRITE paths to the ledger owned by their respective controllers,
  not by `SuppliersController`. `SuppliersController` only seeds opening balance and payments.

---

## Implementation Discovery

Frontend (`PointOfSale.Web/src/pages`):
- `Suppliers.jsx` — master grid + create/edit (operational surface only).
- `SupplierOpeningBalance.jsx` — opening balance seeding.
- `PurchasesBySupplierReport.jsx` — report (presentation).

Controllers:
- `SuppliersController` — master CRUD, payment, opening balance, can-delete, soft-delete.
- `PurchasesController` — purchase invoices → supplier ledger (Credit on Purchase, Debit on PaymentMade).
- `PurchaseReturnsController` — returns → supplier ledger (Debit on PurchaseReturn).
- `StatementOfAccountController` — SOA query, party balance, opening-balance create (supplier + customer).

Application services:
- `LedgerService` (`PointOfSale.API/Services`, implements `ILedgerService` + `IPartyBalanceMaintenanceService`)
  - `ApplySupplierTransaction(...)` → `ApplyPartyTransactionAsync` — locks the supplier row
    (`FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK`), reads current running balance, computes
    `newBalance = current + Debit - Credit`, appends an `AccountLedger` row with `RunningBalance`,
    and sets `Supplier.Balance`. Commits its own transaction when none is ambient.
  - `RebuildPartyBalanceAsync(PartyType.Supplier, supplierId, reason, ...)` — recomputes `Supplier.Balance`
    and every `SupplierTransaction.BalanceAfter` from ordered `AccountLedger` rows. Used as a repair path
    (e.g. after purchase-invoice deletion) and exposed through `IPartyBalanceMaintenanceService`.
  - `EnsureOpeningBalanceNotAlreadyAppliedAsync` — throws `DuplicateOpeningBalanceException` if an
    OpeningBalance ledger entry already exists (pending or committed) for the supplier.
- `ValidationService.CanDeleteSupplierAsync` — deletion guard (see Business Rules).
- `IFinancialMutationIdempotencyService` — `TryClaimAsync` / `CompleteAsync` for replay-safe financial mutations.

Ledger services / outbox:
- `AccountingOutboxService.EnqueueSupplierPaymentAsync`, `EnqueueSupplierOpeningBalanceAsync`,
  `EnqueuePartyLedgerAdjustmentAsync` — enqueue GL postings inside the business transaction.
- `AccountingOutboxProcessor` — background worker; handlers:
  - `ProcessSupplierPaymentAsync` → `CommerceAccountingService.PostSupplierPaymentAsync`
  - `ProcessSupplierOpeningBalanceAsync` → `CommerceAccountingService.PostSupplierOpeningBalanceAsync`
  - `ProcessPartyLedgerAdjustmentAsync` → `CommerceAccountingService.PostPartyLedgerAdjustmentAsync`

Accounting (`CommerceAccountingService`):
- `PostSupplierPaymentAsync` — journal: Debit `SupplierPayables`, Credit funding account
  (Cashbox / VisaClearing / BankAccount). Rejects `Credit`/`Mixed` funding.
- `PostSupplierOpeningBalanceAsync` — journal: if opening Debit (we owe) → Debit `SupplierPayables`,
  Credit `OpeningBalanceEquity`; if opening Credit (they owe) → Debit `OpeningBalanceEquity`,
  Credit `SupplierPayables`. Validates PartyType/TransactionType and exactly one of debit/credit > 0.

Repositories / config:
- `SupplierConfiguration` — indexes on `TenantId`, `Phone`, `Email`, `TaxNumber`.
  `Balance` precision (18,2). No uniqueness constraint is declared on Phone/Email/TaxNumber at the EF level
  (indexes only; see Business Rules).
- Global query filter: `Supplier` filtered by `!IsDeleted && TenantId == CurrentTenantId`
  (also `SupplierTransaction`, `SupplierPayment`, `PartyLedgerAdjustment` have query filters).
- Migration `20260608120000_PosCustomerSupplierAuthorityHardening` — adds UNIQUE partial indexes
  `IX_AccountLedgers_*_OpeningBalance_Once` and `IX_*Transactions_OpeningBalance_Once` (Customer only in this
  migration; Supplier opening-balance uniqueness is enforced in code + via `EnsureOpeningBalanceNotAlreadyAppliedAsync`
  and the `SUPPLIER_OPENING_BALANCE_*` idempotency operations, but the supplier-specific SQL unique index is
  applied through the same pattern — see Documentation Validation).
- Migration `20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments` — introduces `SupplierPayment` and `PartyLedgerAdjustment`.
- Migration `20260410120000_HardenSupplierOpeningBalanceIdempotency` — idempotency hardening for opening balance.

Database tables (PostgreSQL/SQL Server): `Suppliers`, `SupplierTransactions`, `SupplierPayments`,
`AccountLedgers` (party ledger), `PartyLedgerAdjustments`, `AccountingOutboxMessages` (outbox).

Mongo / Redis / MassTransit / Elasticsearch:
- No supplier-specific Mongo read model, Redis cache, MassTransit event, or Elasticsearch index was found in the
  supplier write path. Supplier financial state lives in PostgreSQL (`Suppliers`, `AccountLedgers`,
  `SupplierTransactions`, `SupplierPayments`) and the accounting GL. SOA/report reads hit PostgreSQL directly
  (`AsNoTracking`). Domain events (e.g. purchase return) are published after commit but Supplier itself owns no
  event projection.

Background jobs: `AccountingOutboxProcessor` (hosted worker) is the only background participant.

---

## Business Rules

Implemented and verified:

1. **Tenant isolation (CRITICAL):** Every supplier entity carries `TenantId` (NOT NULL). All queries go through
   `ITenantContext.RequiredTenantId`; global query filter enforces `TenantId == CurrentTenantId`. Cross-tenant
   supplier reads are a hard violation.
2. **Soft delete only:** `DELETE` sets `IsDeleted = true`; the row is never physically removed. Global query filter
   hides deleted suppliers from normal reads. There is no restore / undelete / archive endpoint
   (verified absent from `SuppliersController`).
3. **Delete guard (`CanDeleteSupplierAsync`):**
   - Blocked if `Supplier.Balance != 0` → `SUPPLIER_HAS_BALANCE`.
   - Blocked if any `PurchaseInvoice` exists for the supplier → `SUPPLIER_HAS_INVOICES`.
   - Blocked if any `SupplierTransaction` exists → `SUPPLIER_HAS_TRANSACTIONS`.
   - Otherwise deletable. Deletion does NOT cascade; it requires a clean supplier.
4. **No direct balance writes:** Controllers must never assign `supplier.Balance`. Balance is mutated exclusively
   through `LedgerService.ApplySupplierTransaction`/`RebuildPartyBalanceAsync` (enforced by
   `SupplierFinancialSafetyHardeningTests` asserting `DoesNotContain("supplier.Balance +=")` etc.).
5. **Opening balance uniqueness:** Exactly one opening balance per supplier. Enforced by
   `EnsureOpeningBalanceNotAlreadyAppliedAsync` (throws `DuplicateOpeningBalanceException`) and by
   `supplier.OpeningBalanceAmount.HasValue` checks before writing. Replayed/duplicate requests are short-circuited
   by the financial-mutation idempotency layer (`SUPPLIER_OPENING_BALANCE_BATCH` / `SUPPLIER_OPENING_BALANCE_CREATE`).
6. **Opening balance sign convention:** `Debit` ⇒ we owe supplier ⇒ positive balance (liability). `Credit` ⇒
   supplier owes us ⇒ negative balance (asset). For suppliers this is the inverse of customer semantics.
7. **Idempotency (payments & opening balance):** Supplier payments require an `Idempotency-Key` header
   (`^[A-Za-z0-9-]+$`, max 100). Request hash (supplierId + amount + method + reference + notes) is claimed via
   `IFinancialMutationIdempotencyService.TryClaimAsync`; replays return the original result. Opening-balance batch
   supports optional idempotency header (falls back to a request-hash-derived key).
8. **Payment funding rules:** `PaymentMethod` must be a concrete source — `Cash`, `Card`, or `BankTransfer`.
   `Credit` and `Mixed` are rejected (`BadRequest`). Cash requires an active cashbox account
   (`NO_CASHBOX_CONFIGURED`); the funding account must be active (`ACCOUNT_INACTIVE`); sufficient funds required
   (`INSUFFICIENT_FUNDS`). Amount must be > 0.
9. **Ledger locking / concurrency:** `ApplyPartyTransactionAsync` locks the supplier row (`FOR UPDATE` on Postgres,
   `UPDLOCK,ROWLOCK,HOLDLOCK` on SQL Server) before mutating balance — prevents lost updates on concurrent
   purchases/payments/returns.
10. **Validation (master fields):** `Name` required (max 200). `Phone`/`Email`/`TaxNumber` optional with max lengths.
    No enforced uniqueness on Phone/Email/TaxNumber (indexes exist but no unique constraint — see failure scenarios).
11. **Outbox/GL posting is async & authoritative:** Ledger writes commit synchronously; GL journal posts are
    enqueued to the outbox and processed by `AccountingOutboxProcessor`. If the processor fails, the ledger already
    reflects the movement but the GL may lag — reconcile via outbox.
12. **Fail-closed:** Payment/opening-balance operations roll back the transaction on any exception (e.g.
    `NoCashboxAccountException`, `DuplicateOpeningBalanceException`) and return error status; they do not leave a
    partial ledger state.

---

## Supplier State Machine

Implementation-backed states (all derived from `IsDeleted` + balance/invoice/transaction presence — there is no
explicit status enum on `Supplier`):

```
[Created]  IsDeleted=false, Balance=0, no opening balance yet
     |
     |  (opening balance seeded)
     v
[Active / Trading]  IsDeleted=false, may have Balance != 0, invoices/transactions
     |
     |  (DELETE attempted)
     v
[Soft-Deleted]  IsDeleted=true  — ONLY if Balance==0 AND no invoices AND no transactions
```

- Created → Active: implicit once opening balance and/or purchases occur.
- Active → Soft-Deleted: only via `DELETE` after passing `CanDeleteSupplierAsync`.
- Soft-Deleted is terminal for normal operations (query filter hides it). No restore/archive state exists.
- There is NO "Inactive" flag, NO "Archived" state, NO "Blocked" state on `Supplier`.

---

## Supplier Ledger

`AccountLedger` is the authoritative supplier ledger. Each supplier movement is one `AccountLedger` row with
`PartyType = Supplier`, plus a mirrored `SupplierTransaction` row for the supplier-specific history view.

Ledger sign convention (debit minus credit = balance):
- **Opening Balance** — Debit (we owe, +) or Credit (they owe, −). One-time seed.
- **Purchase** (`TransactionType.Purchase`) — Credit = invoice total (increases what we owe). Debit = 0.
- **Payment Made** (`TransactionType.PaymentMade`) — Debit = amount paid (reduces what we owe). Credit = 0.
  Occurs both from `POST /api/suppliers/{id}/payments` and from the paid portion of a purchase invoice.
- **Purchase Return** (`TransactionType.PurchaseReturn`) — Debit = return total (reduces what we owe). Credit = 0.
- **Party Ledger Adjustment** (`TransactionType.DebitAdjustment`/`CreditAdjustment` via `PartyLedgerAdjustment`)
  — manual correction enqueued to outbox.

`RunningBalance` = previous `RunningBalance` + `DebitAmount` − `CreditAmount`, computed per row by
`ApplyPartyTransactionAsync` (preferring in-flight tracked entries so multiple mutations in one UoW stay additive).

`Supplier.Balance` is maintained as a parallel projection by the ledger service (set to the latest
`RunningBalance`). `SupplierTransaction.BalanceAfter` mirrors it per row.

**Rebuild:** `RebuildPartyBalanceAsync(PartyType.Supplier, supplierId, reason)` replays all `AccountLedger` rows
in `(TransactionDate, CreatedAt, Id)` order, recomputes every `RunningBalance` and `SupplierTransaction.BalanceAfter`,
and resets `Supplier.Balance`. Used as a repair tool (e.g. after purchase-invoice deletion that strips ledger history).

**Statement of Account (SOA):** `POST /api/statement-of-account/query` with `PartyType = Supplier` and optional
`PartyId`/date range. Computes:
- `openingBalance` = `RunningBalance` of the latest ledger row before `StartDate` (else 0).
- `openingBalanceAdjustment` = sum(Debit − Credit) over OpeningBalance rows in range.
- `effectiveOpeningBalance = openingBalance + openingBalanceAdjustment`.
- `closingBalance = effectiveOpeningBalance + totalDebit − totalCredit` (OpeningBalance rows excluded from the
  debit/credit totals).
- `BalanceType` derived by `DetermineBalanceType(balance, PartyType)` → `balance > 0 ? "Debit" : "Credit"`.
  Returned `OpeningBalance`/`ClosingBalance` are `Math.Abs(...)` (presentation sign-stripped).
- `GET /api/statement-of-account/balance/{partyType}/{partyId}` returns the sum(Debit − Credit) over the ledger
  (absolute value + balance type).

This read path is SQL-ledger-authoritative; it does NOT trust `Supplier.Balance` for aggregates
(asserted by `PosCustomerSupplierAuthorityCertificationTests.StatementOfAccount_MustUseSqlLedgerForBalances...`).

---

## Cross-System Effects

| System | Effect from Supplier | Changes? | Notes |
|---|---|---|---|
| Purchases | Each purchase credits the supplier ledger (we owe more); paid amount debits it. | YES (ledger + balance) | Via `ApplySupplierTransaction`. |
| Purchase Returns | Debits the supplier ledger (we owe less). | YES (ledger + balance) | Via `ApplySupplierTransaction(PurchaseReturn)`. |
| Inventory / Warehouse | Purchase receipt increases stock; return decreases stock. | YES (stock) | Supplier is the counterparty, not the stock owner. |
| Accounting (GL) | Supplier payments/opening balances enqueue journal postings (SupplierPayables, funding account, OpeningBalanceEquity). | YES (GL, async) | Through Accounting Outbox → `CommerceAccountingService`. |
| Products / Pricing | None directly. | NO | Supplier is not a product or price authority. |
| Payments (treasury) | Supplier payment validates/consumes funding account balance (cashbox/card/bank). | YES (treasury balance) | Funds availability checked before posting. |
| Analytics / Reports | PurchasesBySupplierReport etc. read supplier ledger/aggregates. | Read-only | Presentation. |
| Notifications | None found. | NO | No supplier-specific notification path. |
| Identity (auth/tenants) | Supplier is tenant-scoped (`TenantId`). | NO (only scoping) | No identity linking beyond TenantId. |
| Storefront | None. | NO | Supplier is internal POS Admin only. |
| CommerceHub | NONE. | NO | CommerceHub has its own `CommerceHubVendor` identity & finance — separate tables, controllers, ledgers. |
| Affiliate | NONE. | NO | Affiliate has `AffiliateAccount`/`AffiliateCommissionLedger` — separate. |
| Marketplace Vendor / Service Marketplace Provider | NONE. | NO | Distinct identities; Supplier does not participate. |
| Sales / Customers | None. | NO | Supplier is the AP side; customers are the AR side (separate `PartyType.Customer`). |

**Explicit:** Supplier is NOT a Marketplace Vendor, NOT a CommerceHub Vendor, NOT a Service Marketplace Provider,
NOT an Affiliate, NOT a Storefront Seller. They share no ledger, no table, no balance, and no controller.

---

## Identity Boundaries

Implementation-backed separation (do NOT merge):

- **Supplier** (`PointOfSale.Core/Entities/Supplier`, `PartyType.Supplier = 1`)
  - Purchasing/business party for POS Admin. Lives in `Suppliers` + `AccountLedgers(PartyType=Supplier)`.
  - Financial lifecycle: opening balance, purchases, returns, payments, SOA.
- **CommerceHub Vendor** (`CommerceHubVendor*`, `CommerceHubVendorEarning`, `CommerceHubVendorPayment*`)
  - A seller on the CommerceHub marketplace. Separate finance: earnings, payouts, debt (`CommerceHubVendorDebtService`).
  - Governed by CommerceHub Super Admin, NOT POS Admin Supplier.
- **Marketplace Vendor** — marketplace seller identity (separate from both above).
- **Service Marketplace Provider** — internal POS-to-POS B2B provider inside POS Admin only; never owns
  payments/accounting/inventory/orders. Distinct from Supplier.
- **Affiliate** (`AffiliateAccount`, `AffiliateCommissionLedger`) — commission/attribution identity.
- **Customer** (`PartyType.Customer = 0`) — AR counterparty; shares the same `AccountLedger`/`*Transaction`
  ledger machinery but is a different `PartyType` and different balance semantics (opposite sign convention).

There is no shared key linking Supplier to any of these vendor identities. Supplier truth is fully contained in
the Supplier + AccountLedger(PartyType=Supplier) tables.

---

## Validation Checklist

- After supplier created: `Supplier` row exists, `TenantId` set, `Balance = 0`, `IsDeleted = false`. Master fields
  trimmed. Audit `LogCreate` emitted.
- After opening balance: exactly one `AccountLedger` OpeningBalance row (PartyType=Supplier), one
  `SupplierTransaction` with `IsOpeningBalance=true`, `Supplier.OpeningBalanceAmount`/`BalanceType`/`OpeningBalanceDate`
  set, `Supplier.Balance` updated (Debit +, Credit −), accounting outbox has a `SupplierOpeningBalance` message.
- After purchase: `AccountLedger` Purchase (Credit=total) + optional PaymentMade (Debit=paid). `SupplierTransaction`
  mirror rows. `Supplier.Balance` increased by (total − paid). Purchase invoice links via `ReferenceId`.
- After purchase return: `AccountLedger` PurchaseReturn (Debit=return total). `Supplier.Balance` decreased.
  `SupplierTransaction` mirror. Original invoice status set to Partially/FullyReturned.
- After supplier payment: `SupplierPayment` row (idempotency key), `AccountLedger` PaymentMade (Debit=amount),
  `SupplierTransaction` mirror, `Supplier.Balance` decreased, funding account balance reduced, outbox
  `SupplierPayment` message enqueued.
- After statement query: SOA aggregates match ledger sum(Debit − Credit); closing balance reconciles to
  `Supplier.Balance` (modulo sign-stripped presentation). Opening-balance rows excluded from debit/credit totals.
- After delete: `IsDeleted = true`; row hidden by query filter; only allowed when Balance==0, no invoices, no
  transactions.

---

## Common Failure Scenarios

1. **Duplicate supplier (same name/phone/tax):** Allowed by implementation — there is NO uniqueness constraint on
   Name/Phone/Email/TaxNumber (only non-unique indexes). Symptom: two suppliers with identical details. Investigate:
   `Suppliers` table filtered by `TenantId` + field. Fix: merge manually (no built-in dedupe).
2. **Ledger mismatch (Supplier.Balance != ledger sum):** Caused by a missed/failed ledger write or a concurrent
   update race. Investigate: compare `Supplier.Balance` to `SUM(Debit−Credit)` over `AccountLedgers` where
   `PartyType=Supplier AND PartyId=supplierId`. If mismatch, run `RebuildPartyBalanceAsync(PartyType.Supplier, id, reason)`.
3. **Opening balance mismatch / duplicate rejected (409 Conflict):** `EnsureOpeningBalanceNotAlreadyAppliedAsync`
   throws `DuplicateOpeningBalanceException` if an OpeningBalance ledger row already exists, or
   `supplier.OpeningBalanceAmount.HasValue`. Investigate: `AccountLedgers` for `PartyType=Supplier,
   TransactionType=OpeningBalance, PartyId`. Re-seeding is blocked by design.
4. **Wrong supplier charged:** Ledger rows reference `PartyId`; verify `ReferenceId` points to the correct
   invoice/payment/return. Investigate `AccountLedgers` + `SupplierTransactions` for the supplier.
5. **Delete blocked:** `CanDeleteSupplierAsync` returns `SUPPLIER_HAS_BALANCE` / `SUPPLIER_HAS_INVOICES` /
   `SUPPLIER_HAS_TRANSACTIONS`. Message instructs to settle balance or set inactive (no inactive flag exists —
   settle via payment/return or delete invoices/transactions first).
6. **Statement mismatch:** SOA uses SQL ledger aggregation; if it disagrees with `Supplier.Balance`, the ledger is
   authoritative. Check date-range handling (opening balance before StartDate) and that OpeningBalance rows are
   excluded from debit/credit totals but added to `effectiveOpeningBalance`.
7. **Outstanding mismatch:** Outstanding/payables compute from `AccountLedger`, not `Supplier.Balance`. If a GL
   posting is missing, check `AccountingOutboxMessages` for stuck/failed `SupplierPayment`/`SupplierOpeningBalance`.
8. **Payment rejected `NO_CASHBOX_CONFIGURED` / `ACCOUNT_INACTIVE` / `INSUFFICIENT_FUNDS`:** Treasury-side guard in
   `ValidateSupplierFundingAvailabilityAsync`. Investigate cashbox config and funding-account balance.
9. **Idempotency replay:** Same `Idempotency-Key` returns prior result; different key with same payload is safe
   (request-hash claim). Missing header on payment → `BadRequest`.

---

## Debugging Guide

Where to investigate a Supplier bug, in order:

1. **Supplier entity / master data** — `Suppliers` table: existence, `TenantId`, `IsDeleted`, master fields,
   `OpeningBalanceAmount`/`BalanceType`. Start here for "supplier not found", "can't edit", delete issues.
2. **Ledger** — `AccountLedgers` where `PartyType=Supplier AND PartyId=@supplierId`: running balance, transaction
   types, dates, `ReferenceId`. Authoritative for balance. Use for mismatch/outstanding bugs.
3. **SupplierTransaction** — `SupplierTransactions` mirror for the human-readable history / `BalanceAfter` per row.
4. **Purchase** — `PurchaseInvoices` + `PurchasesController`: Purchase (Credit) and paid (Debit) ledger writes.
   Check for purchase-integration bugs.
5. **Purchase Return** — `Returns` (ReturnType=PurchaseReturn) + `PurchaseReturnsController`: Debit ledger writes.
6. **Supplier Payment** — `SupplierPayments` + `SuppliersController.CreateSupplierPayment`: idempotency key,
   funding method, amount, GL enqueue.
7. **Accounting** — `CommerceAccountingService.PostSupplierPaymentAsync` / `PostSupplierOpeningBalanceAsync` and the
   GL journals. Check when ledger is correct but financial reports/GL are wrong.
8. **Outbox** — `AccountingOutboxMessages` for `SupplierPayment` / `SupplierOpeningBalance` / `PartyLedgerAdjustment`:
   stuck/failed postings cause GL lag. `AccountingOutboxProcessor` logs.
9. **Repair** — `LedgerService.RebuildPartyBalanceAsync(PartyType.Supplier, id, reason)` to recompute balance from ledger.

---

## Related Knowledge

Core Guides / System Guides (implementation-backed):
- Ledger engine: `PointOfSale.API/Services/LedgerService.cs`, `ILedgerService.cs`, `IPartyBalanceMaintenanceService.cs`.
- Accounting outbox: `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs`,
  `AccountingOutboxProcessor.cs`, `CommerceAccountingService.cs`, `FinancialPostingService.cs`.
- Purchases: `PointOfSale.API/Controllers/PurchasesController.cs`.
- Purchase Returns: `PointOfSale.API/Controllers/PurchaseReturnsController.cs`.
- Statement of Account: `PointOfSale.API/Controllers/StatementOfAccountController.cs`.
- Validation: `PointOfSale.API/Services/ValidationService.cs` (`CanDeleteSupplierAsync`).
- Authority tests: `PointOfSale.Tests/Architecture/SupplierFinancialSafetyHardeningTests.cs`,
  `PosCustomerSupplierAuthorityCertificationTests.cs`, `PointOfSale.Tests/SupplierFinancialIdempotencyTests.cs`,
  `PointOfSale.Tests/Architecture/SupplierFinancialSafetyHardeningTests.cs`.

Related Capabilities / Domains:
- Customer (AR counterparty, shared ledger machinery, opposite sign convention).
- Purchase Invoice / Purchase Return (supplier ledger writers).
- Treasury / Cashbox (funding accounts for supplier payments).
- Accounting / General Ledger (consumer of supplier postings).
- CommerceHub Vendor, Marketplace Vendor, Affiliate, Service Marketplace — explicitly out of scope (separate identities).

---

## Documentation Validation

Findings (implementation vs existing docs). Only implementation-backed:

1. **Missing documentation of the no-restore/no-archive rule:** Existing `SUPPLIER-OPENING-BALANCE-IMPLEMENTATION.md`
   implies "locked after creation" and lists "Edit Capability" as a future enhancement, but never states that there
   is NO restore/undelete/archive endpoint for suppliers. Implementation confirms only soft-delete exists.
2. **Incorrect/overstated uniqueness in prose:** The opening-balance summary says "Prevents duplicate opening
   balances" (true) but the broader docs imply supplier-level uniqueness that does not exist — `Name`, `Phone`,
   `Email`, `TaxNumber` have indexes only, NO unique constraint. Duplicate suppliers are possible.
3. **Outdated authority boundary:** Older docs suggest balances are managed on `Supplier`. Implementation (enforced by
   `SupplierFinancialSafetyHardeningTests`) proves `Supplier.Balance` is a ledger projection; controllers must NOT
   write it directly. SOA computes from `AccountLedger`, not `Supplier.Balance`.
4. **Identity confusion risk in docs:** `SUPPLIER-OPENING-BALANCE-IMPLEMENTATION.md` is clean, but the repo contains
   extensive CommerceHub Vendor documentation. Nothing in Supplier docs warns that Supplier ≠ CommerceHub Vendor;
   this capability doc adds that explicit boundary (implementation: separate tables/controllers/ledgers).
5. **Hidden implementation behavior:** The same supplier opening-balance write path exists in BOTH
   `SuppliersController` (`SUPPLIER_OPENING_BALANCE_BATCH`) and `StatementOfAccountController`
   (`SUPPLIER_OPENING_BALANCE_CREATE`). Docs describe only the SuppliersController path. Both are valid; the SOA path
   also supports `IsDebit` boolean and per-party idempotency.
6. **Architecture inconsistency (index scope):** Migration `20260608120000_PosCustomerSupplierAuthorityHardening`
   adds the UNIQUE partial opening-balance index for **Customer** only (`IX_AccountLedgers_CustomerOpeningBalance_Once`
   / `IX_CustomerTransactions_OpeningBalance_Once`). The Supplier opening-balance uniqueness is enforced in CODE
   (`EnsureOpeningBalanceNotAlreadyAppliedAsync` + idempotency), not by an equivalent SQL unique index in this
   migration. This is an asymmetry worth noting: supplier opening-balance uniqueness lacks the DB-level unique-index
   backstop that customers received. (Recommend adding a Supplier-scoped partial unique index for parity.)
7. **Lifecycle assumption:** Some onboarding docs imply opening balance is mandatory at create. Implementation shows
   it is optional; `Balance` starts at 0 and opening balance is a separate one-time seed.

(No documentation was rewritten; only findings reported per instructions.)

---

## Summary of Discoveries

- **Supplier lifecycle discovered:** Create (Balance=0) → optional Opening Balance seed → Purchases (Credit) /
  Purchase Returns (Debit) / Supplier Payments (Debit) → Statement of Account (read) → Soft Delete (only if
  Balance==0, no invoices, no transactions).
- **Supplier state machine discovered:** Created → Active/Trading → Soft-Deleted (terminal). No Inactive/Archived/
  Blocked/restore states exist.
- **Supplier ledger behavior discovered:** `AccountLedger` (PartyType=Supplier) is authoritative; each movement is a
  row with RunningBalance = prev + Debit − Credit. Mirrored in `SupplierTransaction`. `Supplier.Balance` is a ledger
  projection maintained only by `LedgerService`. Rebuild via `RebuildPartyBalanceAsync`. SOA aggregates from SQL ledger.
- **Identity boundaries discovered:** Supplier is fully separate from CommerceHub Vendor, Marketplace Vendor, Service
  Marketplace Provider, Affiliate, and Storefront Seller. Shares only the generic `AccountLedger`/`*Transaction`
  machinery with Customer (different PartyType, opposite sign convention). No shared keys.
- **Business rules discovered:** tenant isolation (TenantId NOT NULL + query filter), soft-delete only, delete guard
  (balance/invoices/transactions), no direct balance writes, single opening balance (code-enforced), Debit=we owe /
  Credit=they owe, idempotent payments (Idempotency-Key) and opening balance, concrete funding methods only, row-level
  ledger locking, fail-closed rollbacks. NO uniqueness on Name/Phone/Email/TaxNumber.
- **Cross-system dependencies discovered:** Purchases & Returns write the supplier ledger; Treasury funds supplier
  payments; Accounting GL receives async postings via Outbox; Inventory stock moves on purchase/return; Reports read
  ledger. No effect on CommerceHub/Affiliate/Marketplace/Storefront/Sales.
- **Documentation inconsistencies found:** (a) no documentation of missing restore/archive; (b) overstated supplier
  field uniqueness; (c) stale "balance owned by Supplier" authority; (d) missing Supplier≠Vendor warning; (e) hidden
  dual opening-balance write path (SOA controller); (f) Supplier opening-balance uniqueness lacks the DB unique-index
  backstop that Customer has; (g) opening balance is optional, not mandatory.
- **Hidden implementation behavior found:** dual opening-balance endpoints; `PartyLedgerAdjustment` manual correction
  path enqueued to outbox; `RebuildPartyBalanceAsync` repair tool; SOA strips sign for presentation (`Math.Abs`) while
  keeping internal Debit/Credit convention; ledger locking is provider-aware (Postgres `FOR UPDATE` / SQL Server
  `UPDLOCK`).
- **Confirmation:** Only this file (`.ai/hermes/capabilities/pos-admin/supplier-management.md`) was created.
- **Confirmation:** No other files were modified, created, or deleted.
- **Confirmation:** No memory writes occurred.
