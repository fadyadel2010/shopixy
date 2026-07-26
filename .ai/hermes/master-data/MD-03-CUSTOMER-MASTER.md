# MD-03 — Customer Master Architecture (Implementation-Backed)

> Authority rule: implementation is the ONLY source of truth. Every claim below cites `file:line`.
> Prior markdown / comments / certification docs are secondary. Missing evidence is marked as a GAP, never fabricated.
> Scope: the **POS/ERP Customer** (`PointOfSale.Core.Entities.Customer`). The storefront `OnlineCustomer` and `CommerceHubCustomer`
> identity aggregates are a **separate authentication domain** (`ApplicationDbContext.cs:308-310` — "Completely separate from POS Customers")
> and are explicitly OUT OF SCOPE for this Master Data document except where cross-referenced.

---

## 1. Executive Summary

- **What is a Customer?** A tenant-owned master-data party record holding identity + contact fields and a *shadow* balance.
  `Customer : BaseEntity, ITenantOwned` (`PointOfSale.Core/Entities/Customer.cs:8`).
- **What does Customer own?** Its identity/contact attributes (`Name`, `Phone`, `Email`, `Address`, `TaxNumber`, `Notes`),
  opening-balance descriptor fields, and navigation collections (`SalesInvoices`, `Transactions`, `Addresses`) — `Customer.cs:16-47`.
- **What does Customer NOT own?** Its financial balance authority. Balance truth is the `AccountLedgers` aggregation
  (`CustomersController.cs:90-95`, `123-127`; `ValidationService.cs:153-159`). `Customer.Balance` is explicitly a
  "compatibility shadow" (`ValidationService.cs:159`; findings doc line 23).
- **Is Customer an accounting authority?** **No.** All balance mutation flows through `LedgerService`
  (`LedgerService.cs:81-103`, `196-278`); GL posting is owned by `CommerceAccountingService` via the accounting outbox
  (`SalesController.cs:406-409`; `CommerceAccountingService.cs:508-514`).
- **Is Customer a sales authority?** **No.** Sales invoices reference the customer; sales are owned by `SalesController`
  (`SalesController.cs:324-357`). Customer holds a `SalesInvoices` navigation only.
- **Source of truth:** PostgreSQL `AccountLedgers` (financial) + `Customers`/`CustomerTransactions` SQL rows (identity/history),
  under tenant query filters and `FOR UPDATE` row locks (`LedgerService.cs:472-505`).

---

## 2. Discovery — Implemented Customer-Related Entities

| Entity | File | Role |
|--------|------|------|
| `Customer` | `Customer.cs:8-48` | Master identity + shadow balance |
| `CustomerAddress` | `Customer.cs:50-61` | Versioned address header (default/archived flags) |
| `CustomerAddressVersion` | `Customer.cs:63-92` | Immutable address snapshot (geo place ids + display names) |
| `CustomerTransaction` | `CustomerTransaction.cs:9-52` | Per-customer transaction history (debit/credit/balance-after) |
| `AccountLedger` (PartyType.Customer) | `AccountLedger.cs:9-74` | **Financial authority** — unified customer/supplier ledger |
| `PartyLedgerAdjustment` | `ApplicationDbContext.cs:123` | Durable manual adjustment record |
| `CustomerPayoutDetails` | `Entities/CustomerPayoutDetails.cs` | Refund/payout banking details (CommerceHub refund domain) |

**NOT implemented in the POS Customer domain (verified absent):**
- `CustomerGroup` / customer grouping — no entity, no FK (`grep class CustomerGroup` → 0 domain hits).
- `CustomerType` — no entity/enum.
- `CreditLimit` / available-credit — **no field anywhere** (`grep CreditLimit` → 0 hits in solution).
- Standalone `CustomerPayment` entity — customer payments are `CustomerTransaction` rows of type
  `PaymentReceived` written at sale time (`SalesController.cs:382-396`); no dedicated customer payment endpoint
  (findings doc line 106).

> Note: `OnlineCustomer*` and `CommerceHubCustomer*` entities exist but belong to the storefront identity domain,
> explicitly separate from POS Customers (`ApplicationDbContext.cs:308-310`).

---

## 3. Customer Identity

- **Primary Key:** `Id` (Guid), `builder.HasKey(c => c.Id)` (`CustomerConfiguration.cs:11`), inherited from `BaseEntity.Id` (`BaseEntity.cs:8`).
- **Business Identity:** `Name` (required, max 200) — `CustomerConfiguration.cs:13-15`. There is **no customer code** field
  (GAP: no `Code`/`CustomerCode` property on `Customer.cs`).
- **Tenant Ownership:** `TenantId` (Guid, NOT NULL) — `Customer.cs:14`; set from `_tenantContext.RequiredTenantId` on create
  (`CustomersController.cs:152`). Global filter `!IsDeleted && TenantId == CurrentTenantId` (`ApplicationDbContext.cs:502-503`).
- **Active/Inactive:** **Not implemented as a first-class flag.** There is no `IsActive` on `Customer` (`BaseEntity.cs:6-14`
  has only `IsDeleted`). UI copy says "set the customer as inactive" (`ValidationService.cs:164`) but no inactive-state field exists — GAP / inherited lifecycle debt (findings doc line 31 notes same for Supplier).
- **Soft Delete:** `IsDeleted` from `BaseEntity` (`BaseEntity.cs:13`), honored by global query filter and explicit checks
  (`CustomersController.cs:181`, `416`).
- **Validation:** `Name` required/≤200, `Phone`≤50, `Email`≤200, `TaxNumber`≤50, `Balance` precision(18,2)
  (`CustomerConfiguration.cs:13-27`). Duplicate-phone guard at create/update (`CustomersController.cs:140-147`, `185-192`).
- **Relationships:** `SalesInvoices`, `Transactions` (`CustomerTransaction`), `Addresses` (`CustomerAddress`) — `Customer.cs:45-47`.
- **Indexes:** `TenantId`, `Phone`, `Email`, `TaxNumber` (non-unique) — `CustomerConfiguration.cs:29-32`.
  Uniqueness is **application-enforced on phone only**, not a DB unique index.

---

## 4. Customer Ownership

| Aspect | Owned by Customer? | Evidence |
|--------|--------------------|----------|
| Identity (Name/Phone/Email/TaxNumber/Notes) | ✅ Yes | `Customer.cs:16-42`; write path `CustomersController.cs:149-205` |
| Contact / free-text `Address` string | ✅ Yes (legacy) | `Customer.cs:19` |
| Structured versioned addresses | ✅ Yes (as aggregate parts) | `CustomerAddress`/`CustomerAddressVersion` reference `CustomerId` `Customer.cs:53,66` |
| Opening-balance descriptor (`OpeningBalanceAmount`, `BalanceType`, `OpeningBalanceDate`) | ✅ Yes (metadata) | `Customer.cs:30-40`; set at `CustomersController.cs:328-330` |
| `Balance` (running) | ⚠️ Shadow only — NOT authority | `ValidationService.cs:159`; `LedgerService.cs:436` writes it; reads ignore it (`CustomersController.cs:90-95`) |
| Ledger / financial history | ❌ No — owned by `AccountLedger` | `AccountLedger.cs:9`; `LedgerService.cs:196-256` |
| Transaction history rows | ❌ Not authority — derived record | `CustomerTransaction.cs:9`; `BalanceAfter` recomputable via rebuild `LedgerService.cs:391-405` |
| Invoices | ❌ No — invoices *reference* customer | `SalesInvoices` nav only `Customer.cs:45` |

**Interpretation:** Customer owns *identity + descriptive attributes + address aggregate*. It does **not** own financial truth;
the ledger does. `Customer.Balance` is a denormalized cache maintained by `LedgerService.SetTrackedPartyBalance`
(`LedgerService.cs:430-446`) and can be rebuilt from ledger rows (`RebuildPartyBalanceAsync` `LedgerService.cs:105-194`).

---

## 5. Customer Lifecycle (implemented stages only)

```
Create ─▶ Update ─▶ (Opening Balance, one-time) ─▶ Sell (credit sale) ─▶ Payment-at-sale ─▶ Sales Return / Credit ─▶ Soft-Delete (guarded)
```

- **Create:** `CustomersController.CreateCustomer` (`CustomersController.cs:135-171`) — phone dedupe, `Balance=0`, tenant stamp, audit log.
- **Update:** `UpdateCustomer` (`CustomersController.cs:176-209`) — identity/contact only; phone dedupe; audit before/after. Balance never touched here.
- **Opening Balance (one-time):** `SaveCustomerOpeningBalances` (`CustomersController.cs:246-405`) — writes ledger via
  `ApplyCustomerTransaction` (`:336-344`) + a `CustomerTransaction` with `IsOpeningBalance=true` (`:346-361`); duplicate-guarded.
- **Sell / Credit Sale:** `SalesController` writes `Sale` ledger debit for full amount (`SalesController.cs:330-339`).
- **Payment-at-sale:** `PaymentReceived` ledger credit for paid portion (`SalesController.cs:369-378`). No standalone payment endpoint (findings line 106).
- **Sales Return / Credit:** `SalesReturn` ledger credit when `creditToCustomerAmount > 0` (`SalesReturnsController.cs:868-876`).
- **Deactivate:** ❌ Not implemented (no inactive flag — §3).
- **Soft-Delete:** `DeleteCustomer` (`CustomersController.cs:411-445`), gated by `CanDeleteCustomerAsync`.
- **Merge:** ❌ Not implemented (no merge endpoint/service found).
- **Archive:** Applies to addresses only (`CustomerAddress.IsArchived` `Customer.cs:58`), not the customer record.

---

## 6. Aggregate Analysis

| Aggregate Root | Owns | Evidence |
|----------------|------|----------|
| **Customer** | Identity fields, `Address` string, opening-balance descriptor, shadow `Balance`, and its `CustomerAddress` children | `Customer.cs:8-61` |
| **CustomerAddress** (sub-aggregate under Customer) | Ordered `CustomerAddressVersion` snapshots, default/archived state, version pointer | `Customer.cs:50-92`; unique `(TenantId,CustomerAddressId,Version)` `ApplicationDbContext.cs:528-529` |
| **AccountLedger** (cross-party financial aggregate) | All customer/supplier debit/credit rows + running balance — **financial authority** | `AccountLedger.cs:9-74`; `LedgerService.cs:220-238` |
| **CustomerTransaction** | Per-customer history projection of ledger effects (debit/credit/balance-after) | `CustomerTransaction.cs:9-52` |
| **PartyLedgerAdjustment** | Durable manual adjustment records | `ApplicationDbContext.cs:123` |

**Boundary rule (evidence-backed):** Customer is NOT the financial aggregate root. Balance-changing writes always go through
`LedgerService.ApplyPartyTransactionAsync` which locks the customer row, appends an `AccountLedger` entry, and updates the
shadow balance atomically (`LedgerService.cs:196-256`). `CustomerTransaction` is written by callers *alongside* the ledger
(`CustomersController.cs:346-361`, `SalesController.cs:343-357`), and its `BalanceAfter` is a derived value reconstructable
by `RebuildTrackedBalancesAsync` (`LedgerService.cs:391-405`).

---

## 7. Customer Behavior (implemented only)

- **Create** — `CustomersController.cs:135-171`. Roles: `Owner,Admin,Manager,Cashier` (`:136`). Phone dedupe (`:140-147`).
- **Update** — `CustomersController.cs:176-209`. Roles: `Owner,Admin,Manager` (`:177`). Coalesces optional fields (`:200-202`).
- **Deactivate** — ❌ NOT implemented (no inactive state).
- **Delete (soft)** — `CustomersController.cs:411-445`. Roles: `Owner,Admin,Manager`. Pre-checked by `CanDeleteCustomerAsync`.
- **Can-Delete probe** — `GET {id}/can-delete` (`CustomersController.cs:450-467`) for UI gating.
- **Merge** — ❌ NOT implemented.
- **Search** — `GET /api/customers` (`CustomersController.cs:61-108`): case-insensitive contains on `Name`/`Phone`/`Email`,
  paginated, `AsNoTracking`, ordered by `CreatedAt desc`; per-row balance computed from `AccountLedgers` (`:90-95`).
  This is a **direct SQL query against Postgres**, not a Mongo/ES projection.
- **Validation** — duplicate phone (create/update); required `Name`; opening-balance normalization
  (amount>0, BalanceType∈{Debit,Credit}) `CustomersController.cs:501-537`.

---

## 8. Financial Behavior

| Responsibility | Owner | Evidence |
|----------------|-------|----------|
| Credit Limit | ❌ NOT implemented | no `CreditLimit` field anywhere in solution |
| Credit-sale gating | ❌ NOT implemented | `SalesController.cs:327-339` posts full-amount debit unconditionally; no credit check |
| Outstanding Balance (read) | `AccountLedgers` aggregation `Σ(Debit-Credit)` | `CustomersController.cs:90-95,123-127`; `ValidationService.cs:153-157` |
| Running balance (mutation) | `LedgerService` (`current + debit - credit`) | `LedgerService.cs:217-218` |
| Customer Ledger | `AccountLedger` (PartyType.Customer) | `AccountLedger.cs:20-25`; `LedgerService.cs:81-103` |
| Payment allocation | ❌ No allocation engine — payment recorded as flat `PaymentReceived` credit | `SalesController.cs:369-396` |
| Shadow balance sync | `LedgerService.SetTrackedPartyBalance` | `LedgerService.cs:239,430-446` |
| Opening balance (one-time) | `LedgerService.ApplyCustomerTransaction` + duplicate guard | `CustomersController.cs:336-361`; `LedgerService.cs:319-357` |
| GL posting (double-entry) | `CommerceAccountingService` via accounting outbox | `SalesController.cs:406-409`; `CommerceAccountingService.cs:508-514,670-686` |

**Balance sign convention:** positive = customer owes us (debit), negative = we owe customer (`Customer.cs:23-25`).
Ledger `RunningBalance = Σ(DebitAmount - CreditAmount)` (`LedgerService.cs:382-387`).

**Authority summary:** Customer domain does not decide credit exposure at all. Sales/returns are the write sources;
`LedgerService` is the mutation authority; `AccountLedgers` SQL aggregation is the read authority; `Customer.Balance`
is a non-authoritative cache.

---

## 9. Customer Constraints (implementation-backed business rules)

1. **Duplicate phone rejected** on create and update — `CustomersController.cs:140-147`, `185-192` (409-style BadRequest).
2. **`Name` required, ≤200 chars** — `CustomerConfiguration.cs:13-15`.
3. **Field length caps** — Phone≤50, Email≤200, TaxNumber≤50 — `CustomerConfiguration.cs:17-24`.
4. **Tenant stamping mandatory** — `TenantId` set from `RequiredTenantId`; NOT NULL at DB (`Customer.cs:11-14`; `CustomersController.cs:152`).
5. **Cross-tenant access blocked** — global query filter `TenantId == CurrentTenantId` (`ApplicationDbContext.cs:502-503`);
   ledger locks scoped by tenant (`LedgerService.cs:472-505`).
6. **Opening balance is one-time** — app guard (`CustomersController.cs:322-326`) + service guard
   (`LedgerService.cs:319-357`) + **partial unique indexes** on live opening-balance ledger and transaction rows
   (`20260608120000_PosCustomerSupplierAuthorityHardening.cs:42-51`).
7. **Opening balance amount > 0 and BalanceType ∈ {Debit,Credit}** — `CustomersController.cs:501-517`.
8. **Cannot delete with non-zero ledger balance** — `CUSTOMER_HAS_BALANCE` (`ValidationService.cs:160-166`), computed from ledger.
9. **Cannot delete with invoice history** — `CUSTOMER_HAS_INVOICES` (`ValidationService.cs:168-178`).
10. **Cannot delete with transaction history** — `CUSTOMER_HAS_TRANSACTIONS` (`ValidationService.cs:180-187`).
11. **Deleted customers excluded** from mutation paths (`CustomersController.cs:290`; `LedgerService.cs:481,494,504`).
12. **Credit limit / credit-sale block** — ❌ NO such rule exists (verified absent).

---

## 10. SQL Transactions

- **Transaction boundaries:** `LedgerService.ApplyPartyTransactionAsync` opens a `ReadCommitted` transaction *only if none is
  already active* (`LedgerService.cs:208-211`) — it enlists in a caller's transaction when present (composability).
  Opening-balance batch opens its own transaction (`CustomersController.cs:267`).
- **Concurrency / Locks:** party row is pessimistically locked before balance read/mutation —
  Postgres `SELECT … FOR UPDATE` (`LedgerService.cs:474-484`), SQL Server `WITH (UPDLOCK, ROWLOCK, HOLDLOCK)` (`:487-497`).
- **In-flight additivity:** multiple ledger writes in one unit of work read the pending `RunningBalance` from the change tracker
  first (`LedgerService.cs:285-300`) so successive entries stay additive.
- **Rollback:** explicit `RollbackAsync` on `DuplicateOpeningBalanceException` and generic exceptions when the service owns the
  transaction (`LedgerService.cs:258-277`); batch path rolls back on failure (`CustomersController.cs:399-404`).
- **Outbox:** POS sale enqueues an accounting outbox row inside the sale transaction; the synchronous post is the executor and the
  outbox worker is a recovery path, not a second authority (`SalesController.cs:400-409`).
- **Retry / Idempotency:** opening-balance and adjustments require durable idempotency claims + request-hash
  (`CustomersController.cs:266-281,578-598`; `IFinancialMutationIdempotencyService`), with stored-response replay
  (`CustomersController.cs:469-481,635-655`).
- **Atomicity:** ledger row + shadow-balance update + `CustomerTransaction` are saved in the same transaction/unit of work
  (`LedgerService.cs:238-245`; `CustomersController.cs:346-381`).

---

## 11. Events

- **POS Customer domain emits NO domain/integration events.** `CustomersController` performs no `IEventPublisher.PublishAsync`
  (verified: create/update/delete only write audit-log entries `CustomersController.cs:168,206,442`).
- **Financial effects are propagated via the accounting OUTBOX**, not customer events: `EnqueuePosSaleAsync`
  (`SalesController.cs:406-409`) → `CommerceAccountingService` posts GL entries (`CommerceAccountingService.cs:508-514,670-686`).
- **Audit events:** `IAuditService.LogCreate/LogUpdate/LogDelete` (`CustomersController.cs:168,206,442`) and
  `LogAction("RebuildBalance", …)` (`LedgerService.cs:148-169`).
- **Storefront customer events (SEPARATE DOMAIN, cross-reference only):** `OnlineCustomerUpdatedEvent` etc. drive Mongo
  projection + Redis invalidation via `OnlineCustomerProjectionConsumer` (`OnlineCustomerEvents.cs:166`;
  `OnlineCustomerProjectionConsumer.cs:32-39,460`). These are NOT POS Customer events.
- **Projection events for POS Customer:** ❌ None. POS Customer is not projected to Mongo/ES.

---

## 12. Runtime Architecture

**POS Customer (this domain) — SQL-only, no projection pipeline:**

```
HTTP (CustomersController)
   │
   ▼
PostgreSQL  ── Customers / CustomerTransactions / AccountLedgers  (authoritative reads + writes)
   │
   └─(sale/return only)▶ Accounting Outbox ─▶ CommerceAccountingService ─▶ GL (double-entry)
```

- Customer list/detail read straight from Postgres with ledger-aggregated balance (`CustomersController.cs:83-96,116-127`).
- **No MassTransit → Mongo → Search → Cache pipeline exists for POS Customer** (verified: no `CustomerDocument`,
  no projection consumer; the only `*CustomerDocument` types are `OnlineCustomerDocument` in the storefront domain
  `MongoDB/Documents/OnlineCustomerDocument.cs`).
- The `SQL → Outbox → MassTransit → Mongo → Search → Cache` pattern described in the platform canon applies to the
  storefront `OnlineCustomer`, NOT to the POS Customer master.

---

## 13. Browser Test Specification

**Scenario A — Create + duplicate-phone guard**
1. POST create customer with phone `X` → expect 201, balance 0.
2. POST create another with phone `X` → expect 400 "A customer with this phone number already exists"
   (`CustomersController.cs:146`).
- Assertion: only one non-deleted customer with phone `X`.

**Scenario B — Update contact fields**
1. PUT update Name/Email → 204; GET reflects change; balance unchanged.
- Failure case: change phone to an existing customer's phone → 400 (`CustomersController.cs:190-191`).

**Scenario C — Opening balance one-time**
1. POST `/opening-balances` Debit 500 → success, `balanceAfter=500`.
2. Repeat for same customer → "Opening balance already exists" (`CustomersController.cs:324`).
3. Replay identical request with same `Idempotency-Key` → stored response replay, no double posting.
- Assertion: exactly one `OpeningBalance` ledger row + one `CustomerTransaction` (unique indexes
  `20260608120000_…:42-51`).

**Scenario D — Credit sale updates ledger balance**
1. Create customer, make a credit sale of 1000 (no payment).
2. GET customer → balance 1000 (from `AccountLedgers`, `CustomersController.cs:123-127`).
3. Partial payment-at-sale 400 → balance 600.
- Assertion: balance card equals `Σ(Debit-Credit)`; never trust `Customer.Balance` shadow.

**Scenario E — Delete guards**
1. Customer with outstanding balance → `can-delete=false`, `CUSTOMER_HAS_BALANCE`.
2. Customer with invoice history → `CUSTOMER_HAS_INVOICES`.
3. Clean customer → soft-deleted, disappears from list (query filter).

**Scenario F — Cross-tenant isolation**
1. Auth as Tenant B, GET Tenant A customer id → 404 (query filter `ApplicationDbContext.cs:502-503`).

**Business validation:** credit-limit enforcement is NOT testable — feature does not exist; do not assert on it.

---

## 14. Domain Ownership Matrix

| Capability | Owner | Evidence |
|------------|-------|----------|
| Customer Identity | **Customer** | `Customer.cs:16-42` |
| Customer Code | ❌ Not implemented | no field |
| Customer Groups / Types | ❌ Not implemented | no entity |
| Free-text Address string | **Customer** | `Customer.cs:19` |
| Structured Addresses | **CustomerAddress / CustomerAddressVersion** (under Customer) | `Customer.cs:50-92` |
| Credit Limit | ❌ Not implemented | verified absent |
| Outstanding Balance (read) | **AccountLedger** aggregation | `CustomersController.cs:90-95` |
| Running Balance (mutation) | **LedgerService** | `LedgerService.cs:217-256` |
| Shadow `Customer.Balance` | Cache written by LedgerService (NOT authority) | `LedgerService.cs:436`; `ValidationService.cs:159` |
| Customer Ledger | **AccountLedger** | `AccountLedger.cs:9-74` |
| Transaction History | **CustomerTransaction** (derived) | `CustomerTransaction.cs:9-52` |
| Payment recording | **Sales flow** (`PaymentReceived` credit) | `SalesController.cs:369-396` |
| Manual Adjustment | **PartyLedgerAdjustment** via StatementOfAccount | `ApplicationDbContext.cs:123`; findings 58-62 |
| GL / double-entry posting | **CommerceAccountingService** (outbox) | `CommerceAccountingService.cs:508-514` |
| Search | **Direct Postgres query** (no projection) | `CustomersController.cs:61-108` |
| Tenant isolation | **Query filter + tenant context + row locks** | `ApplicationDbContext.cs:502-503`; `LedgerService.cs:472-505` |

---

## 15. Canonical Pattern

```
Customer            → Identity + contact + address aggregate (master data, tenant-owned)
AccountLedger       → Financial authority (running balance = Σ Debit - Credit)
LedgerService       → Sole balance-mutation authority (row-locked, transaction-composable)
CustomerTransaction → Derived per-customer history (BalanceAfter rebuildable)
Customer.Balance    → Non-authoritative denormalized shadow/cache
Sales / Returns     → Financial write sources (never mutate balance directly)
Accounting Outbox   → CommerceAccountingService → GL double-entry
Search              → Direct SQL (no Mongo/ES projection for POS Customer)
```

Customer is an **identity aggregate**, not a financial aggregate. Money truth lives in the ledger; the customer record
carries a convenience shadow only.

---

## 16. Documentation Validation (misconception corrections)

- ❌ Customer owns its balance. ✅ Balance is derived from `AccountLedgers`; `Customer.Balance` is a shadow
  (`ValidationService.cs:159`; `CustomersController.cs:90-95`).
- ❌ Customer balance can be edited directly. ✅ Balance changes only via `LedgerService` ledger writes
  (`LedgerService.cs:196-256`); update endpoint never touches balance (`CustomersController.cs:197-205`).
- ❌ Customer owns invoices. ✅ Invoices *reference* the customer (`SalesInvoices` navigation, `Customer.cs:45`);
  sales are owned by `SalesController`.
- ❌ Customer has a credit limit that blocks credit sales. ✅ No credit-limit feature exists; credit sales post
  unconditionally (`SalesController.cs:327-339`).
- ❌ Customer can be deactivated. ✅ No inactive flag exists — only soft delete (`BaseEntity.cs:13`); "inactive" is UI copy only
  (`ValidationService.cs:164`).
- ❌ Customer changes fan out through MassTransit → Mongo → Search. ✅ POS Customer is SQL-only with no projection pipeline;
  that pattern belongs to the separate `OnlineCustomer` storefront domain (`ApplicationDbContext.cs:308-310`).
- ❌ Customer opening balance can be set repeatedly. ✅ One-time — guarded by app + service + partial unique indexes
  (`CustomersController.cs:322-326`; `LedgerService.cs:319-357`; migration `…:42-51`).

---

## 17. Final Architecture Conclusion

- **Customer Authority:** Customer is the tenant-owned authority for *identity, contact, and address data only*
  (`Customer.cs:8-92`). No code, groups, types, credit limits, or inactive lifecycle.
- **Financial Authority:** `AccountLedger` + `LedgerService` are the exclusive financial authority; reads aggregate ledger rows,
  writes are row-locked and transaction-composable (`LedgerService.cs:196-505`). `Customer.Balance` is a non-authoritative shadow.
- **Runtime Flow:** HTTP → PostgreSQL (authoritative) → (on sale/return) accounting outbox → `CommerceAccountingService` GL.
  No Mongo/Search/Cache projection for POS Customer.
- **Aggregate Ownership:** Customer (identity + address sub-aggregate); AccountLedger (financial); CustomerTransaction (derived
  history); PartyLedgerAdjustment (manual adjustments).
- **Transaction Model:** ReadCommitted, pessimistic party-row locks, change-tracker additivity, durable idempotency +
  request-hash for financial mutations, outbox for GL, explicit rollback and unique-index guards.
- **Projection Strategy:** None for POS Customer — search and balance are computed directly from SQL. Projection pipelines exist
  only for the separate storefront `OnlineCustomer` identity domain.

### Implementation Status Legend
- **Implemented:** identity CRUD, phone dedupe, tenant isolation, soft delete + delete guards, opening balance (one-time,
  idempotent), ledger-backed balance, credit sale/payment-at-sale/return ledger effects, versioned addresses, audit logging,
  GL posting via outbox.
- **Partially Implemented:** `Customer.Balance` shadow (maintained but not authoritative; rebuildable); "inactive" (UI language
  only, no field).
- **NOT Implemented:** Customer Code, Customer Groups, Customer Types, Credit Limit / available credit, credit-sale gating,
  standalone customer payment endpoint, payment allocation engine, customer merge, POS Customer projections/events.

---

*Generated by implementation reverse-engineering. Every section cites `file:line`. Gaps are marked explicitly; nothing is assumed.*
