# POS Customer Management (POS Admin / ERP Customer)

> Implementation-backed reverse-engineering of the POS Customer capability.
> Primary source of truth = code in `PointOfSale.API`, `PointOfSale.Core`,
> `PointOfSale.Infrastructure`. Documentation is secondary. Where docs and code
> disagree, code wins. No UI, no code generation, no refactors were performed.

---

## Purpose

POS Customer Management is the ERP-side customer master used by POS Admin. It
solves one business problem: tracking the money relationship between a tenant
(merchant) and the people/companies it sells to on credit or account.

It owns:

- The `Customer` master record (identity + contact attributes) for the ERP.
- The customer's **receivable ledger** (`AccountLedger` rows with
  `PartyType = Customer`) — the authoritative, single source of customer balance.
- The human-readable transaction mirror (`CustomerTransaction`) used for
  statement rendering.
- Opening balances that seed the receivable at system adoption time.
- Soft-delete lifecycle and delete-guard enforcement.

It is NOT a storefront login, NOT a marketing identity, NOT a marketplace buyer.
It is a financial party in the same ledger machinery that suppliers use.

---

## Business Scope

Reverse-engineered from the entities, controller, services and configuration.

### Owned
- **Customer** (`PointOfSale.Core/Entities/Customer.cs`)
  - `TenantId` (NOT NULL — multi-tenant authority key)
  - `Name` (required, max 200)
  - `Phone` (nullable, max 50, unique index)
  - `Email` (nullable, max 200, unique index)
  - `Address` (free-text, nullable)
  - `TaxNumber` (nullable, max 50, indexed)
  - `Balance` (decimal 18,2) — **compatibility shadow**, recomputed from ledger
  - `OpeningBalanceAmount`, `BalanceType` ("Debit"/"Credit"), `OpeningBalanceDate`
  - `Notes` (nullable)
  - `SalesInvoices`, `Transactions`, `Addresses` navigation collections
  - Inherits `IsDeleted` (soft-delete) from `BaseEntity`
- **CustomerAddress / CustomerAddressVersion** — address versioning with
  geographic catalog snapshotting (admission evidence). This is the only
  "address" structure the ERP customer owns.
- **CustomerTransaction** (`PointOfSale.Core/Entities/CustomerTransaction.cs`)
  - per-row mirror of ledger activity: `DebitAmount`, `CreditAmount`,
    `BalanceAfter`, `TransactionType`, `ReferenceId`, `ReferenceNumber`,
    `IsOpeningBalance`, `Notes`.
- **AccountLedger** rows where `PartyType = Customer` — the financial truth.
- **Opening Balance** capture (single occurrence enforced).
- **Receivables / Outstanding Balance** — derived = SUM(Debit - Credit) over the
  customer's ledger.
- **Customer Statement of Account** — query over `AccountLedger` by party.

### NOT owned (confirmed absent in implementation)
- **Customer Groups / Categories** — no entity, table, or code exists.
- **Customer Pricing overrides / per-customer discounts** — not on the customer;
  pricing is a separate capability.
- **Credit Limit / Credit Days** — no field, validation, or enforcement exists.
  Receivables are NOT gated by any credit ceiling.
- **Loyalty / Reward Points** — no implementation.
- **Customer Merge** — no implementation.
- **Customer Import / Export** — no implementation.
- **Customer Archive** — the `Customer` entity has **no** `IsArchived`,
  `IsActive`, or status field. Only `BaseEntity.IsDeleted` (soft delete) exists.
  The only `IsArchived` in the customer area is `CustomerAddress.IsArchived`
  (address-level), not customer-level.
- **Customer activation / deactivation** — no active/inactive status exists.
- **Attachments** — no attachment model for customers.
- **Payments as a standalone POS Customer screen** — there is no
  `CustomerPaymentController`. Customer money movement happens inside the Sales,
  Sales Return, and Statement-of-Account opening-balance flows, all routed
  through `ILedgerService`.

---

## Authority Boundaries

| Concept | Authority | Projection | Evidence | Presentation |
|---|---|---|---|---|
| Customer identity/contact | `Customers` table (PostgreSQL) | Mongo read models (none specifically for ERP customer) | `audit_logs` | POS Admin UI |
| Customer balance | **`AccountLedger`** (`PartyType=Customer`) | `Customer.Balance` (shadow) | `CustomerTransaction` mirror | Statement of Account |
| Customer ledger | `AccountLedger` rows | — | SQL | SOA query |
| Opening Balance | `AccountLedger` OpeningBalance row + `Customer.OpeningBalanceAmount` | `Customer.OpeningBalanceAmount` | `CustomerTransaction` (IsOpeningBalance) | opening-balances screen |
| Receivables | `AccountLedger` SUM(Debit-Credit) | `Customer.Balance` | SOA | balance widget |
| Pricing Overrides | (separate pricing capability) | — | — | — |
| Loyalty | N/A (not implemented) | — | — | — |
| Analytics | `AnalyticsActor` (separate; reads `OnlineCustomer.Id` for storefront, not ERP customer) | — | — | — |
| Notifications | N/A in this capability | — | — | — |

**Key rule (from `ValidationService.CanDeleteCustomerAsync`):**
`Customer.Balance` is explicitly a *compatibility shadow*. **Deletion decisions
use ledger authority**, not the shadow column. Any balance check must read
`AccountLedger`.

`ValidationService.cs:159`:
```
// Customer.Balance is a compatibility shadow; deletion decisions use ledger authority.
if (customerBalance != 0) { ... CUSTOMER_HAS_BALANCE ... }
```

---

## Customer Lifecycle

Discovered implementation:

```
Create (POST /api/customers)
   │   Name required, Phone must be unique among non-deleted customers
   ▼
Active (soft-exists, IsDeleted=false, Balance=0)
   │
   ├── Opening Balance (POST /api/customers/opening-balances OR
   │     POST /api/statementofaccount/opening-balance)  [once only]
   ▼
Sales (Sale → ledger Debit; PaymentReceived → ledger Credit)
   │
Sales Return (SalesReturn → ledger Credit)
   │
Statement (AccountLedger query)
   │
Soft Delete (DELETE /api/customers/{id})
      only if balance == 0 AND no invoices AND no transactions
```

There is **no** Active→Archived→Inactive→Deleted state machine. The only
terminal transition is soft-delete (`IsDeleted = true`). There is no restore
endpoint in this controller (no `Restore` action exists).

---

## Implementation Discovery

### Frontend
Not documented here (UI is out of scope). Operational surface only.

### Controllers
- `CustomersController` (`PointOfSale.API/Controllers/CustomersController.cs`)
  - `GET /api/customers` — paged list + live balance computed from `AccountLedger`.
  - `GET /api/customers/{id}` — single customer + balance.
  - `POST /api/customers` — create (roles Owner/Admin/Manager/Cashier).
  - `PUT /api/customers/{id}` — update (Owner/Admin/Manager); phone uniqueness re-checked.
  - `GET /api/customers/opening-balances` — list opening-balance status.
  - `POST /api/customers/opening-balances` — batch opening balance (idempotent,
    operation `CUSTOMER_OPENING_BALANCE_BATCH`).
  - `DELETE /api/customers/{id}` — soft delete (guarded).
  - `GET /api/customers/{id}/can-delete` — pre-check delete guard.
- `StatementOfAccountController`
  - `POST /api/statementofaccount/query` — SOA with date-range filter + opening
    balance roll-forward.
  - `GET /api/statementofaccount/balance/{partyType}/{partyId}` — party balance.
  - `POST /api/statementofaccount/opening-balance` — single customer/supplier
    opening balance (operation `CUSTOMER_OPENING_BALANCE_CREATE`).

### Application Services
- `ILedgerService` / `LedgerService` (`PointOfSale.API/Services/LedgerService.cs`)
  — the **single writer** of `AccountLedger`. `ApplyCustomerTransaction` locks the
  customer row (`FOR UPDATE` on Postgres, `UPDLOCK,ROWLOCK,HOLDLOCK` on SQL Server),
  recomputes running balance, updates `Customer.Balance` shadow.
- `IValidationService` / `ValidationService` — delete guards.
- `IFinancialMutationIdempotencyService` — idempotency for opening-balance writes.
- `IAccountingOutboxService` — outbox for supplier opening balance (customer OB
  does **not** enqueue an outbox row in the SOA path; see Cross-system Effects).
- `IAccountingOutboxService.EnqueuePosSaleAsync` / `EnqueuePosRefundAsync` — sale and
  refund accounting outbox (driven from Sales / Sales Returns, not the customer controller).
- `IAuditService` — create/update/delete audit logs.

### Repositories / DbContext
- `ApplicationDbContext` exposes `Customers`, `CustomerTransactions`,
  `AccountLedgers`, `SalesInvoices`, `Returns` (via `CustomerId` FKs).

### Entities
- `Customer`, `CustomerAddress`, `CustomerAddressVersion`, `CustomerTransaction`,
  `AccountLedger` (shared with Supplier via `PartyType`).

### Database Tables
- `Customers` (incl. `TenantId`, `Phone`, `Email`, `TaxNumber` indexed),
  `CustomerTransactions`, `AccountLedgers` (`PartyType`, `PartyId` indexed),
  `CustomerAddresses` / `CustomerAddressVersions`.

### Ledger / Accounting
- `AccountLedger` is the unified party ledger. Customer rows use `PartyType=0`
  (Customer enum ordinal). Running balance maintained incrementally by
  `LedgerService`; `RebuildPartyBalanceAsync` can recompute from ledger if drift
  is suspected (`RebuildTrackedBalancesAsync`).

### Sales Integration
- `SalesController` posts ledger on sale (`TransactionType.Sale`, Debit = full
  invoice total) and on payment (`TransactionType.PaymentReceived`, Credit =
  amount actually paid to customer ledger, excluding cash change). Mirrors both
  into `CustomerTransaction` with `BalanceAfter = customer.Balance` read AFTER
  the ledger write.

### Sales Return Integration
- `SalesReturnsController` posts `TransactionType.SalesReturn` (Credit =
  `creditToCustomerAmount`) to the customer ledger when a return credits the
  customer account.

### Pricing / Warehouse / Inventory
- No customer-owned pricing. Inventory/warehouse are untouched by customer
  management except indirectly via the sales that reference a customer.

### Background Jobs / Outbox / Mongo / Redis / MassTransit / Elasticsearch
- No customer-specific background job, Mongo projection, Redis cache, or
  MassTransit event exists for the ERP `Customer`. The `OnlineCustomer` domain
  has projections/consumers; the ERP `Customer` does **not**.
- Elasticsearch: no customer search index discovered for ERP customers.
- Analytics: `AnalyticsActor` keys on `OnlineCustomer.Id`, not ERP `Customer.Id`.

---

## Business Rules (implementation-backed)

1. **Tenant isolation is mandatory.** Every customer row carries `TenantId`
   (NOT NULL, DB-enforced). All queries filter by `_tenantContext.RequiredTenantId`.
   Cross-tenant customer reads/writes are not permitted.
2. **Name required.** `CreateCustomerRequest.Name` is required; EF
   `HasMaxLength(200).IsRequired()`.
3. **Phone uniqueness (soft-delete aware).** Create and update reject a phone
   that already exists on another non-deleted customer → `400 "A customer with
   this phone number already exists"`.
4. **Balance starts at zero.** New customers are created with `Balance = 0`.
5. **Opening Balance is one-time only.** Two independent guards:
   - `Customer.OpeningBalanceAmount.HasValue` OR an existing
     `AccountLedger` OpeningBalance row → reject (`CUSTOMER_HAS_BALANCE`-style
     conflict / `409`).
   - `LedgerService.EnsureOpeningBalanceNotAlreadyAppliedAsync` throws
     `DuplicateOpeningBalanceException` (→ 409) if a second OpeningBalance is
     attempted (checked in ChangeTracker + DB).
   - Migration `20260608120000_PosCustomerSupplierAuthorityHardening` adds unique
     indexes `IX_AccountLedgers_CustomerOpeningBalance_Once` and
     `IX_CustomerTransactions_OpeningBalance_Once` to harden this at DB level.
6. **Opening Balance amount must be > 0**, rounded to 2 decimals
   (`MidpointRounding.AwayFromZero`), `BalanceType` ∈ {Debit, Credit}.
7. **Idempotency on opening balance.** Batch and single OB writes claim an
   idempotency key (`CUSTOMER_OPENING_BALANCE_BATCH`,
   `CUSTOMER_OPENING_BALANCE_CREATE`); duplicate identical requests short-circuit
   to the prior result. Conflicting entries for the same customer in one batch →
   `409`.
8. **Ledger is the balance authority.** `Customer.Balance` is a shadow; all
   balance reads in list/detail/delete use `AccountLedger` SUM(Debit - Credit).
9. **Delete guard (fail-closed).** Soft delete is rejected when ANY of:
   - Ledger balance != 0 → `CUSTOMER_HAS_BALANCE`.
   - Has any `SalesInvoice` → `CUSTOMER_HAS_INVOICES`.
   - Has any `CustomerTransaction` → `CUSTOMER_HAS_TRANSACTIONS`.
   Otherwise `IsDeleted = true`. Error messages are bilingual (En/Ar) and advise
   setting the customer inactive instead. (Note: there is no inactive flag, so
   the advice is advisory only.)
10. **Reads exclude soft-deleted.** `GetCustomer` and list/opening-balance queries
    filter `!IsDeleted`.
11. **Row locking on ledger write.** `ApplyCustomerTransaction` locks the customer
    row for update to keep running balance consistent under concurrency
    (Postgres `FOR UPDATE`, SQL Server `UPDLOCK,ROWLOCK,HOLDLOCK`).
12. **Role gating.** Create = Owner/Admin/Manager/Cashier; Update/Delete/
    Opening-balance = Owner/Admin/Manager.

---

## Customer State Machine

No explicit enum/status machine exists. Effective states derived from data:

```
[None] ──Create──▶ Active (IsDeleted=false, Balance=0)
   Active ──OpeningBalance──▶ Active+OB (OpeningBalanceAmount set, ledger OB row)
   Active(+OB) ──Sale──▶ Active (ledger Debit, Balance rises)
   Active ──PaymentReceived──▶ Active (ledger Credit, Balance falls)
   Active ──SalesReturn──▶ Active (ledger Credit)
   Active ──SoftDelete (guarded)──▶ Deleted (IsDeleted=true)
```

States: **Created/Active**, **Active with Opening Balance**, **Deleted
(soft)**. There is **no** Archived, Inactive, Merged, or Restored state in code.

---

## Ledger

The customer ledger is `AccountLedger` filtered by `PartyType = Customer`.

- **Opening Balance:** Debit (customer owes) or Credit (we owe) posted once via
  `ApplyCustomerTransaction(TransactionType.OpeningBalance)`. `RunningBalance`
  seeded. Mirrored to `CustomerTransaction` with `IsOpeningBalance=true`,
  reference `OB-CUS-<first8ofGuid>`.
- **Invoice Posting (Sale):** `TransactionType.Sale`, Debit = full invoice total.
  Increases receivable.
- **Payment Posting:** `TransactionType.PaymentReceived`, Credit = amount applied
  to customer account (excludes returned cash change). Decreases receivable.
- **Refund / Return Posting:** `TransactionType.SalesReturn`, Credit =
  `creditToCustomerAmount`. Decreases receivable.
- **Running Balance:** `newBalance = currentBalance + Debit - Credit`, stored per
  row. `GetCurrentBalanceAsync` prefers in-flight tracked entries then the last
  ledger row.
- **Outstanding Balance:** `SUM(DebitAmount - CreditAmount)` over all non-deleted
  ledger rows for the party. Positive = customer owes; the SOA response takes
  `Math.Abs` and reports a `BalanceType` (Debit/Credit) for presentation.
- **Statement Generation:** `StatementOfAccountController.query` paginates ledger
  rows by party + date range, computes opening balance roll-forward
  (`RunningBalance` of last row before range start + OB adjustment), and
  aggregates TotalDebit/TotalCredit excluding OpeningBalance rows.
- **Aging:** No aging report implementation discovered for customers.
- **Credit Limit:** Not enforced (no field). Receivables can exceed any implicit
  bound.
- **Rebuild:** `RebuildPartyBalanceAsync` recomputes `RunningBalance` from ledger
  order and re-syncs `Customer.Balance` shadow + `CustomerTransaction.BalanceAfter`.
  Used when drift is suspected; logs an audit `RebuildBalance` action.

`Customer.Balance` (shadow) is updated by `LedgerService.SetTrackedPartyBalance`
on every ledger write, but the authoritative balance for decisions (delete,
display, SOA) is always recomputed from `AccountLedger`.

---

## Cross-System Effects

### POS Customer → Sales
A sale referencing a customer posts `Sale` (Debit) + `PaymentReceived` (Credit) to
the customer ledger. The customer is a financial party only; sales logic lives in
`SalesController`. No customer-side state change beyond ledger + `Balance` shadow.

### POS Customer → Sales Returns
A return that credits the customer account posts `SalesReturn` (Credit) to the
ledger. Customer is referenced by `CustomerId` on the return.

### POS Customer → Payments
No standalone payment screen. Customer money movement is embedded in Sale /
SalesReturn / Opening-Balance flows, all through `ILedgerService`.

### POS Customer → Accounting
- Sale/Refund enqueue accounting outbox rows
  (`EnqueuePosSaleAsync` / `EnqueuePosRefundAsync`) — the outbox is staged inside
  the sale/return transaction; the synchronous post is the executor and the
  background worker is a recovery path only.
- Customer opening balance (SOA path) does **not** enqueue an accounting outbox
  row for customers (only the supplier OB path calls
  `EnqueueSupplierOpeningBalanceAsync`). Customer OB affects the party ledger and
  `Customer.Balance` only.

### POS Customer → Pricing
No effect. Pricing is a separate capability; customers carry no price overrides.

### POS Customer → Inventory / Warehouse
No direct effect. Inventory changes come from the sales/returns that happen to
reference a customer.

### POS Customer → Analytics
`AnalyticsActor` is keyed on `OnlineCustomer.Id` (storefront), not ERP
`Customer.Id`. POS Customer contributes no dedicated analytics identity.

### POS Customer → Notifications
No customer-driven notification in this capability.

### POS Customer → Identity / Storefront / CommerceHub / Affiliate / Online Customer / Orders / Checkout
- **No shared identity.** `Order.OnlineCustomerId` references `OnlineCustomers`
  (storefront). ERP `Customer` is a different table and different identity.
- There is **no FK, mapping table, or code** linking ERP `Customer` ↔
  `OnlineCustomer` ↔ `CommerceHubCustomer` ↔ `Affiliate`. The only relationship
  discovered is the conceptual one: a POS sale *may* reference an ERP `Customer`
  by `CustomerId`, while storefront/commercehub orders reference `OnlineCustomerId`.
- CommerceHub `CustomerPayoutDetails` / `CustomerRefund*` services belong to the
  **CommerceHub marketplace customer**, NOT the ERP POS Customer. Out of scope.

---

## Identity Boundaries

| Identity | Table / Entity | Tenant-scoped? | Purpose |
|---|---|---|---|
| **POS Customer (ERP)** | `Customers` | Yes | Financial party / receivable for in-store/ERP sales |
| **Online Customer (Storefront)** | `OnlineCustomers` | Yes | Storefront login/shop identity; orders, wishlist, carts |
| **Marketplace Customer (CommerceHub)** | `CommerceHubCustomers` | Yes | CommerceHub marketplace buyer; payouts, refunds |
| **Affiliate** | separate | Yes | Affiliate program identity |

**Explicit separation:** POS Customer is NOT the same identity as Online Customer.
The implementation keeps them in separate tables with separate auth/JWT flows
(`StorefrontJwt`, `CustomerJwt` for CommerceHub). No merge/mapping code exists
between them. Do not confuse `Customer.Balance` (ERP receivable) with any
storefront wallet/loyalty.

---

## Validation Checklist (operational)

- **After customer created:** `Balance == 0`, `IsDeleted == false`, `TenantId`
  set, phone not colliding with another active customer.
- **After customer edited:** phone still unique among active customers; `Balance`
  shadow unchanged by edit (only ledger changes balance).
- **After opening balance:** exactly ONE `AccountLedger` OpeningBalance row for
  the customer; `Customer.OpeningBalanceAmount` set; `CustomerTransaction`
  `IsOpeningBalance=true` created; ledger `RunningBalance` reflects it; second OB
  attempt returns 409.
- **After first sale:** ledger has `Sale` Debit = invoice total; `BalanceAfter`
  increased; `Customer.Balance` shadow updated.
- **After payment:** ledger `PaymentReceived` Credit reduces receivable; cash
  change excluded.
- **After sales return (credited):** ledger `SalesReturn` Credit reduces
  receivable.
- **After archive:** N/A — no archive exists.
- **After soft delete:** only allowed when balance == 0, no invoices, no
  transactions; `IsDeleted=true`; subsequent reads exclude it.
- **After merge:** N/A — no merge exists.

---

## Common Failure Scenarios

1. **"Duplicate customer" on create/update**
   - Cause: phone already used by another active customer.
   - Investigate: `Customers` table for `Phone == value AND IsDeleted=false`.
2. **Ledger mismatch / `Customer.Balance` disagrees with SOA**
   - Cause: shadow column drift, or a write bypassed `LedgerService`.
   - Investigate: compare `Customer.Balance` vs `SUM(Debit-Credit)` on
     `AccountLedgers`. Run/inspect `RebuildPartyBalanceAsync` logic. Check whether
     any code wrote ledger without `ILedgerService`.
3. **Opening balance mismatch / double OB**
   - Cause: concurrent OB posts or a bypass of the one-time guard.
   - Investigate: count `AccountLedger` rows with `PartyType=Customer AND
     TransactionType=OpeningBalance` for the party (should be 1). Check unique
     indexes added by migration `20260608120000_...`.
4. **Credit limit ignored**
   - Cause: by design — **no credit limit exists**. Not a bug.
5. **Wrong customer selected on sale**
   - Cause: UI passing wrong `CustomerId`; logic uses whatever id is supplied.
   - Investigate: `SalesInvoice.CustomerId` vs intended customer.
6. **"Archived customer used"**
   - Cause: misunderstanding — there is no archive. A customer is either active or
     soft-deleted. Soft-deleted customers are filtered from reads.
7. **Deleted customer referenced**
   - Cause: a sale/return referencing a soft-deleted customer (delete guard should
     prevent deletion if invoices/transactions exist).
   - Investigate: confirm delete guard passed (balance 0, no invoices, no
     transactions). If referenced post-delete, the guard was bypassed.
8. **Statement mismatch (totals vs list)**
   - Cause: OpeningBalance row included in list but excluded from debit/credit
     aggregates; `effectiveOpeningBalance` adds it back. Expected behavior, not a
     bug (see `StatementOfAccountController` comments).

---

## Debugging Guide

If a POS Customer bug arrives:

1. **Start at the entity + controller.** `CustomersController.cs` is the entry
   point for create/update/delete/opening-balance. `StatementOfAccountController`
   for balance/SOA.
2. **Check the Customer entity** when the issue is identity/contact data
   (name/phone/email/tax/address), uniqueness, or soft-delete state.
3. **Check the Ledger (`AccountLedger`)** when the issue is balance, receivable,
   opening balance, or SOA totals. Always trust ledger over `Customer.Balance`.
4. **Check `LedgerService`** when running balance is wrong, or a concurrent write
   produced drift. Verify the row-lock path and `RebuildPartyBalanceAsync`.
5. **Check Sales / Sales Returns** when a specific invoice/return produced wrong
   customer debits/credits. Trace `ApplyCustomerTransaction` calls there.
6. **Check Payments** — there is no standalone path; payments are inside Sales and
   SOA opening-balance. Confirm which flow posted the credit.
7. **Check Accounting outbox** only for Sale/Refund money movement
   (`EnqueuePosSaleAsync`/`EnqueuePosRefundAsync`). Customer opening balance does
   not enqueue an outbox row.
8. **Check idempotency** for duplicate opening-balance reports:
   `IFinancialMutationIdempotencyService` + operations
   `CUSTOMER_OPENING_BALANCE_BATCH` / `CUSTOMER_OPENING_BALANCE_CREATE`.
9. **Check tenant context** (`ITenantContext.RequiredTenantId`) for any
   cross-tenant or missing-tenant symptom.

---

## Related Knowledge

Core Guides:
- `PointOfSale.Core/Entities/Customer.cs`
- `PointOfSale.Core/Entities/CustomerTransaction.cs`
- `PointOfSale.Core/Entities/AccountLedger.cs`
- `PointOfSale.Core/Enums/TransactionType.cs`

System Guides:
- `.ai-context/ARCHITECTURE_TRUTH.md` (authority, tenant isolation, outbox)
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/KNOWN_RISKS.md`

Related Capabilities (same `pos-admin` set):
- `sales-management.md`, `sales-return-management.md`, `payment-management.md`,
  `order-management.md`, `pricing-management.md`, `purchase-management.md`,
  `inventory-management.md`, `product-management.md`, `checkout-management.md`,
  `POS_ADMIN_CAPABILITIES.md`

Related Domains:
- Supplier (mirror party in `AccountLedger`, same ledger machinery).
- Online Customer / CommerceHub Customer / Affiliate — separate identities (see
  Identity Boundaries).

---

## Documentation Validation (implementation vs existing docs)

Findings (implementation-backed; no docs rewritten):

- **Missing documentation:** No prior doc described the one-time opening-balance
  DB-level uniqueness guard (`IX_AccountLedgers_CustomerOpeningBalance_Once`,
  migration `20260608120000_PosCustomerSupplierAuthorityHardening`), nor the
  idempotency operations for opening balance.
- **Outdated/incorrect assumptions to watch for:** Any doc implying POS Customer
  has Customer Groups, Credit Limit, Credit Days, Loyalty, Merge, Import/Export,
  or Archive/Inactive status would be **wrong** — none exist in code.
- **Architecture inconsistencies:** The phrase "set the customer as inactive
  instead" appears in delete-guard messages but there is **no inactive/archive
  status** on `Customer`. The advice cannot be actioned as described.
- **Missing authority boundaries:** `Customer.Balance` is a compatibility shadow;
  the authoritative balance is `AccountLedger`. Any doc treating `Customer.Balance`
  as truth is incorrect.
- **Identity confusion risk:** POS `Customer` must not be conflated with
  `OnlineCustomer` (storefront) or `CommerceHubCustomer` (marketplace). No mapping
  code exists between them.
- **Hidden implementation behavior:** Customer opening balance (SOA path) does NOT
  enqueue an accounting outbox row, unlike the supplier opening-balance path. Sale
  and Refund do enqueue outbox rows. This asymmetry is easy to miss.
- **Incorrect lifecycle assumptions:** There is no Active→Archived→Inactive→Deleted
  machine. Only Create → (OpeningBalance) → Sales/Returns → Soft Delete.

---

## Appendix: Summary of Discoveries

- **Customer lifecycle discovered:** Create → Active → (Opening Balance, once) →
  Sales/SalesReturn/Payments via ledger → Soft Delete (guarded). No archive,
  inactive, merge, or restore states exist.
- **Customer state machine discovered:** Active (IsDeleted=false) and Deleted
  (IsDeleted=true). Opening-balance is a data flag, not a state. No enum/status.
- **Customer ledger behavior discovered:** Single `AccountLedger` party ledger
  (`PartyType=Customer`). Running balance maintained incrementally with row
  locking; `Customer.Balance` is a shadow recomputed from ledger. Outstanding =
  SUM(Debit-Credit). Opening balance one-time, idempotent, DB-unique-guarded.
- **Identity boundaries discovered:** POS `Customer` ≠ `OnlineCustomer` (storefront)
  ≠ `CommerceHubCustomer` (marketplace) ≠ `Affiliate`. No mapping/merge code.
- **Business rules discovered:** tenant isolation; name required; phone uniqueness
  (soft-delete aware); balance starts 0; one-time opening balance (>0, rounded,
  Debit/Credit); ledger is authority; delete guard (balance 0 + no invoices + no
  transactions); role gating; row-locked ledger writes; idempotent OB.
- **Cross-system dependencies discovered:** Sales posts Sale/PaymentReceived to
  ledger; Sales Returns post SalesReturn; Sale/Refund enqueue accounting outbox;
  Customer OB does NOT enqueue outbox; no effect on pricing/inventory/analytics;
  no Mongo/Redis/ES/MassTransit for ERP customer.
- **Documentation inconsistencies found:** "set inactive instead" advice is
  unactionable (no inactive flag); any groups/credit-limit/loyalty/archive doc
  would be false; `Customer.Balance` shadow vs ledger authority; customer OB
  outbox asymmetry undocumented.
- **Hidden implementation behavior found:** one-time OB DB unique indexes; customer
  OB path skips accounting outbox while supplier OB enqueues it; SOA aggregates
  exclude OpeningBalance rows from debit/credit but re-add via opening roll-forward;
  delete decision uses ledger not shadow.
- **Confirmation:** Only this file
  (`.ai/hermes/capabilities/pos-admin/pos-customer-management.md`) was created.
- **Confirmation:** No other files were modified, created, or deleted.
- **Confirmation:** No memory writes occurred.
