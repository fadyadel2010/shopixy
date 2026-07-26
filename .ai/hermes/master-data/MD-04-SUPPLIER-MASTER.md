# MD-04 — Supplier Master Architecture (Implementation-Backed)

> Authority rule: implementation is the ONLY source of truth. Every claim below cites `file:line`.
> Prior markdown / comments / certification docs are secondary. Missing evidence is marked as a GAP, never fabricated.
> Scope: the **POS/ERP Supplier** (`PointOfSale.Core.Entities.Supplier`). This is treated as its own domain —
> Supplier behavior is NOT inferred from the Customer implementation unless the Supplier code explicitly does the same
> (verified independently below).

---

## 1. Executive Summary

- **What is a Supplier?** A tenant-owned master-data party record holding identity/contact attributes and a *shadow*
  balance. `Supplier : BaseEntity, ITenantOwned` (`PointOfSale.Core/Entities/Supplier.cs:9`).
- **What does Supplier own?** Its identity/contact attributes (`Name`, `Phone`, `Email`, `Address`, `TaxNumber`,
  `PaymentTermsDays`, `Notes`), opening-balance descriptor fields, and navigation collections (`PurchaseInvoices`,
  `Transactions`) — `Supplier.cs:17-62`.
- **What does Supplier NOT own?** Its financial balance authority. Balance truth is the `AccountLedgers` aggregation
  (`Supplier.cs:36` is a `Balance` shadow maintained by `LedgerService`; reads in the controller surface it but the
  authoritative balance is recomputed from the ledger — see `StatementOfAccountController.cs:1040-1042`). Supplier does
  NOT own purchase invoices — they *reference* it (`PurchaseInvoice.cs:21`, `Supplier.cs:61`).
- **Is Supplier an accounting authority?** **No.** All balance mutation flows through `LedgerService`
  (`LedgerService.cs:57-79`, `196-278`); GL posting is owned by `CommerceAccountingService` via the accounting outbox
  (`CommerceAccountingService.cs:369-421`). Supplier never writes to the ledger directly.
- **Is Supplier a purchasing authority?** **No.** Purchases are owned by `PurchasesController`
  (`PurchasesController.cs:109-377`); the supplier is loaded and stamped onto the `PurchaseInvoice.SupplierId`
  (`PurchasesController.cs:289`), but the purchase lifecycle is not part of the Supplier aggregate.
- **Source of truth:** PostgreSQL `AccountLedgers` (financial) + `Suppliers`/`SupplierTransactions` SQL rows (identity/history),
  under tenant query filters and `FOR UPDATE` row locks (`LedgerService.cs:507-540`).

---

## 2. Discovery — Implemented Supplier-Related Entities

| Entity | File | Role |
|--------|------|------|
| `Supplier` | `Supplier.cs:9-63` | Master identity + shadow balance |
| `SupplierTransaction` | `SupplierTransaction.cs:9-52` | Per-supplier transaction history (debit/credit/balance-after) |
| `SupplierPayment` | `SupplierPayment.cs:10-40` | Immutable supplier-level payment record (idempotency + outbox reconciliation) |
| `AccountLedger` (PartyType.Supplier) | `AccountLedger.cs:9-74` | **Financial authority** — unified customer/supplier ledger |
| `PartyLedgerAdjustment` | `PartyLedgerAdjustment.cs:10-27` | Durable manual adjustment (credit/debit note) record |

**NOT implemented in the Supplier domain (verified absent):**
- `SupplierCode` / supplier code field — `Supplier` has no `Code` property (`Supplier.cs:9-63`); `SupplierConfiguration.cs`
  defines no code index.
- `SupplierAddress` / `SupplierContact` separate entities — there is **no** `SupplierAddress` or `SupplierContact`
  entity (compare Customer which has `CustomerAddress`/`CustomerAddressVersion` at `Customer.cs:50-92`). Supplier `Address`
  is a free-text string column only (`Supplier.cs:27`).
- `SupplierGroup` / supplier grouping — no entity, no FK (`grep class SupplierGroup` → 0 domain hits).
- `SupplierType` — no entity/enum.
- Standalone supplier-payment allocation engine — `SupplierPayment` is a flat record; no allocation (`SupplierPayment.cs`).
- Supplier merge — no merge endpoint/service.
- Supplier domain events / Mongo / Elasticsearch projection — none (verified; see §11, §12).

> `PurchasesBySupplierReport` (`PurchasesBySupplierReportDto.cs`) is a *report*, not an entity, and reads SQL directly
> (`ReportsController.cs:1859-1992`).

---

## 3. Supplier Identity

- **Primary Key:** `Id` (Guid), `builder.HasKey(s => s.Id)` (`SupplierConfiguration.cs:11`), inherited from `BaseEntity.Id`
  (`BaseEntity.cs:8`).
- **Business Identity:** `Name` (required, max 200) — `SupplierConfiguration.cs:13-15`. There is **no supplier code** field
  (GAP: no `Code`/`SupplierCode` property on `Supplier.cs`).
- **Tenant Ownership:** `TenantId` (Guid, NOT NULL) — `Supplier.cs:15`; set from `_tenantContext.RequiredTenantId` on create
  (`SuppliersController.cs:135`). Global filter `!IsDeleted && TenantId == CurrentTenantId`
  (`ApplicationDbContext.cs` — entity global filter applied).
- **Active/Inactive:** **Not implemented as a first-class flag.** There is no `IsActive` on `Supplier`
  (`BaseEntity.cs:6-14` has only `IsDeleted`). Delete-guard messages say "set the supplier as inactive instead"
  (`ValidationService.cs:219,232,244`) but no inactive-state field exists — GAP / inherited lifecycle debt
  (mirrors Customer findings doc).
- **Soft Delete:** `IsDeleted` from `BaseEntity` (`BaseEntity.cs:13`), honored by global query filter and explicit checks
  (`SuppliersController.cs:209-210, 600-602, 642-645`).
- **Validation:** `Name` required/≤200, `Phone`≤50, `Email`≤200, `TaxNumber`≤50, `Balance` precision(18,2)
  (`SupplierConfiguration.cs:13-27`). **No duplicate-phone or duplicate-email guard** exists for Supplier (contrast Customer
  which dedupes phone — `CustomersController.cs:140-147`). Supplier indexes on `Phone`/`Email`/`TaxNumber` are **non-unique**
  (`SupplierConfiguration.cs:30-32`).
- **Relationships:** `PurchaseInvoices` (nav collection, `Supplier.cs:61`), `Transactions` (`SupplierTransaction`,
  `Supplier.cs:62`), `SupplierPayments` (FK in `SupplierPayment.cs:38`).
- **Indexes:** `TenantId`, `Phone`, `Email`, `TaxNumber` (all non-unique) — `SupplierConfiguration.cs:29-32`.

---

## 4. Supplier Ownership

| Aspect | Owned by Supplier? | Evidence |
|--------|--------------------|----------|
| Identity (Name/Phone/Email/Address/TaxNumber/Notes) | ✅ Yes | `Supplier.cs:17-58`; write path `SuppliersController.cs:132-153` |
| Free-text `Address` string | ✅ Yes (legacy, no structured addresses) | `Supplier.cs:27`; set at `SuppliersController.cs:139` |
| `PaymentTermsDays` | ✅ Yes | `Supplier.cs:56`; `SuppliersController.cs:141` |
| Opening-balance descriptor (`OpeningBalanceAmount`, `BalanceType`, `OpeningBalanceDate`) | ✅ Yes (metadata) | `Supplier.cs:41-51`; set at `SuppliersController.cs:490-492` |
| `Balance` (running) | ⚠️ Shadow only — NOT authority | `Supplier.cs:36`; `LedgerService.cs:442-445` writes it; authoritative read aggregates ledger `StatementOfAccountController.cs:1040-1042` |
| Ledger / financial history | ❌ No — owned by `AccountLedger` | `AccountLedger.cs:9`; `LedgerService.cs:196-256` |
| Transaction history rows | ❌ Not authority — derived record | `SupplierTransaction.cs:9`; `BalanceAfter` recomputable via rebuild `LedgerService.cs:409-424` |
| Payments | ❌ No — `SupplierPayment` is a separate record referencing supplier | `SupplierPayment.cs:17,38` |
| Invoices | ❌ No — invoices *reference* supplier | `Supplier.cs:61` nav only; `PurchaseInvoice.cs:21` |

**Interpretation:** Supplier owns *identity + descriptive attributes*. It does **not** own financial truth; the ledger does.
`Supplier.Balance` is a denormalized cache maintained by `LedgerService.SetTrackedPartyBalance`
(`LedgerService.cs:430-446`) and can be rebuilt from ledger rows (`RebuildPartyBalanceAsync` `LedgerService.cs:105-194`).

---

## 5. Supplier Lifecycle (implemented stages only)

```
Create ─▶ Update ─▶ (Opening Balance, one-time, batch) ─▶ Purchase (credit) ─▶ Payment (made) ─▶ Purchase Return
   │                                                                                                  │
   └────────────────────────────▶ Manual Adjustment (statement) ─▶ Soft-Delete (guarded) ◀─────────────┘
```

- **Create:** `SuppliersController.CreateSupplier` (`SuppliersController.cs:123-153`) — `Balance=0`, tenant stamp, audit log.
  Roles: `Owner,Admin,Manager` (`:124`).
- **Update:** `SuppliersController.UpdateSupplier` (`SuppliersController.cs:342-371`) — identity/contact only; audit before/after;
  Balance never touched here. Roles: `Owner,Admin,Manager` (`:343`).
- **Opening Balance (one-time, batch):** `SaveSupplierOpeningBalances` (`SuppliersController.cs:408-590`) — writes ledger via
  `ApplySupplierTransaction` (`:497-506`) + a `SupplierTransaction` with `IsOpeningBalance=true` (`:508-523`); duplicate-guarded.
  Also reachable via `StatementOfAccountController` (`StatementOfAccountController.cs:273-384`).
- **Purchase (credit):** `PurchasesController` writes purchase ledger **credit** for full amount owed
  (`PurchasesController.cs:275-299`). This is a *Purchase* lifecycle event, not a Supplier method.
- **Payment (made):** `SuppliersController.CreateSupplierPayment` (`SuppliersController.cs:155-340`) — account-level payment;
  writes ledger **debit** (`:261-269`); records `SupplierPayment` + `SupplierTransaction`. Also at sale time
  (`PurchasesController.cs:306-320`).
- **Purchase Return:** `PurchaseReturnsController` writes ledger **debit** to reduce what we owe
  (`PurchaseReturnsController.cs:597-622`).
- **Manual Adjustment:** `StatementOfAccountController.CreateAdjustment` (`StatementOfAccountController.cs:535-728`) —
  credit/debit note via `PartyLedgerAdjustment` + ledger mutation.
- **Deactivate:** ❌ Not implemented (no inactive flag — §3).
- **Soft-Delete:** `DeleteSupplier` (`SuppliersController.cs:596-633`), gated by `CanDeleteSupplierAsync`.
- **Merge:** ❌ Not implemented (no merge endpoint/service found).
- **Archive:** ❌ Not implemented for supplier records (no archive path).

---

## 6. Aggregate Analysis

| Aggregate Root | Owns | Evidence |
|----------------|------|----------|
| **Supplier** | Identity fields, `Address` string, `PaymentTermsDays`, opening-balance descriptor, shadow `Balance` | `Supplier.cs:9-63` |
| **AccountLedger** (cross-party financial aggregate) | All customer/supplier debit/credit rows + running balance — **financial authority** | `AccountLedger.cs:9-74`; `LedgerService.cs:220-238` |
| **SupplierTransaction** | Per-supplier history projection of ledger effects (debit/credit/balance-after) | `SupplierTransaction.cs:9-52` |
| **SupplierPayment** | Immutable payment record (idempotency key, funding method, reference) | `SupplierPayment.cs:10-40`; FK `SupplierId` `Restrict` `…:65-70` |
| **PartyLedgerAdjustment** | Durable manual adjustment records (credit/debit notes) | `PartyLedgerAdjustment.cs:10-27` |
| **PurchaseInvoice** (separate aggregate) | References `SupplierId`; NOT owned by Supplier | `PurchaseInvoice.cs:9-21` |

**Boundary rule (evidence-backed):** Supplier is NOT the financial aggregate root. Balance-changing writes always go through
`LedgerService.ApplyPartyTransactionAsync` which locks the supplier row, appends an `AccountLedger` entry, and updates the
shadow balance atomically (`LedgerService.cs:196-256`). `SupplierTransaction` is written by callers *alongside* the ledger
(`SuppliersController.cs:271-286`, `PurchasesController.cs:285-299`), and its `BalanceAfter` is a derived value reconstructable
by `RebuildTrackedBalancesAsync` (`LedgerService.cs:409-424`).

---

## 7. Supplier Behavior (implemented only)

- **Create** — `SuppliersController.cs:123-153`. Roles `Owner,Admin,Manager` (`:124`). No duplicate-phone/email guard.
- **Update** — `SuppliersController.cs:342-371`. Roles `Owner,Admin,Manager` (`:343`). Identity/contact only.
- **Deactivate** — ❌ NOT implemented (no inactive state).
- **Delete (soft)** — `SuppliersController.cs:596-633`. Roles `Owner,Admin,Manager`. Pre-checked by `CanDeleteSupplierAsync`.
- **Can-Delete probe** — `GET {id}/can-delete` (`SuppliersController.cs:638-655`) for UI gating; UI `ValidationDeleteModal`.
- **Merge** — ❌ NOT implemented.
- **Search** — `GET /api/suppliers` (`SuppliersController.cs:73-108`): case-insensitive contains on `Name`/`Email`/`Phone`,
  paginated (`page`,`pageSize`), `AsNoTracking`, ordered by `Name`; **balance is NOT computed here** — it returns the
  denormalized `Supplier.Balance` shadow via `ToResponseDto` (`EntityExposureResponseDtos.cs:5-19`, `SafeResponseMapper.cs:10`).
  This is a **direct SQL query against Postgres**, not a Mongo/ES projection.
- **Validation** — required `Name`; opening-balance normalization (amount>0, BalanceType∈{Debit,Credit})
  (`SuppliersController.cs:815-886`, `913-932`); payment amount>0 and concrete funding method
  (`SuppliersController.cs:191-207`); opening-balance one-time (`SuppliersController.cs:484-488`).
- **Payment** — `POST {id}/payments` (`SuppliersController.cs:155-340`) — requires `Idempotency-Key` header
  (`:28-32`, `:162`), validates funding availability (`:690-755`), writes `SupplierPayment` + ledger debit + outbox.

---

## 8. Financial Behavior

| Responsibility | Owner | Evidence |
|----------------|-------|----------|
| Outstanding Balance (read) | `AccountLedgers` aggregation `Σ(Debit-Credit)` for PartyType.Supplier | `StatementOfAccountController.cs:1034-1042`; `ValidationService.cs:215` computes from `Supplier.Balance` (shadow, used by delete-guard only) |
| Running balance (mutation) | `LedgerService` (`current + debit - credit`) | `LedgerService.cs:217-218` |
| Supplier Ledger | `AccountLedger` (PartyType.Supplier) | `AccountLedger.cs:20-25`; `LedgerService.cs:57-79` |
| Opening Balance (one-time) | `LedgerService.ApplySupplierTransaction` + duplicate guard | `SuppliersController.cs:497-506`; `LedgerService.cs:319-357` |
| Purchase Liability (credit) | Purchase flow writes ledger **credit** | `PurchasesController.cs:275-283` (credit = we owe supplier) |
| Payment Recording (debit) | `SuppliersController.CreateSupplierPayment` → ledger **debit** | `SuppliersController.cs:261-269` (debit = payment made, reduces payable) |
| Purchase Return (debit) | Purchase return flow writes ledger **debit** | `PurchaseReturnsController.cs:597-605` |
| Balance Synchronization (shadow) | `LedgerService.SetTrackedPartyBalance` | `LedgerService.cs:239,430-446` |
| GL / double-entry posting | `CommerceAccountingService` via accounting outbox | `CommerceAccountingService.cs:369-421` (debit SupplierPayables, credit funding account) |
| Manual Adjustment | `PartyLedgerAdjustment` via StatementOfAccount | `StatementOfAccountController.cs:535-728`; outbox `AccountingOutboxService.cs:181-186` |

**Balance sign convention:** `Supplier.Balance` uses ledger convention `debit minus credit`
(`Supplier.cs:32-36`; `LedgerService.cs:218`). For suppliers, a **credit purchase increases what we owe** (payable),
a **debit payment/return decreases payable** (`PurchasesController.cs:281` credit; `SuppliersController.cs:266-267` debit).
Ledger `RunningBalance = Σ(DebitAmount - CreditAmount)` (`LedgerService.cs:382-387`).

**Authority summary:** Supplier domain does not decide money movement. Purchases/payments/returns/adjustments are the write
sources; `LedgerService` is the mutation authority; `AccountLedgers` SQL aggregation is the read authority; `Supplier.Balance`
is a non-authoritative cache.

---

## 9. Supplier Constraints (implementation-backed business rules)

1. **`Name` required, ≤200 chars** — `SupplierConfiguration.cs:13-15`; DTO `[Required]` (`SuppliersController.cs` `CreateSupplierRequest`).
2. **Field length caps** — Phone≤50, Email≤200, TaxNumber≤50 — `SupplierConfiguration.cs:17-24`.
3. **Tenant stamping mandatory** — `TenantId` set from `RequiredTenantId`; NOT NULL at DB (`Supplier.cs:15`;
   `SuppliersController.cs:135`).
4. **Cross-tenant access blocked** — global query filter `TenantId == CurrentTenantId`; ledger locks scoped by tenant
   (`LedgerService.cs:507-540`).
5. **Opening balance is one-time** — app guard (`SuppliersController.cs:484-488`) + service guard
   (`LedgerService.cs:319-357`, `DuplicateOpeningBalanceException`). Also enforced by `StatementOfAccountController.cs:370-381`
   and partial unique index on live opening-balance ledger rows (`20260608120000_PosCustomerSupplierAuthorityHardening.cs`).
6. **Opening balance amount > 0 and BalanceType ∈ {Debit,Credit}** — `SuppliersController.cs:840-855,848-855`.
7. **Cannot delete with non-zero balance** — `SUPPLIER_HAS_BALANCE` (`ValidationService.cs:215-221`), computed from
   `Supplier.Balance` shadow (delete-guard path).
8. **Cannot delete with purchase-invoice history** — `SUPPLIER_HAS_INVOICES` (`ValidationService.cs:223-233`).
9. **Cannot delete with transaction history** — `SUPPLIER_HAS_TRANSACTIONS` (`ValidationService.cs:235-245`).
10. **Payment amount must be > 0** — `SuppliersController.cs:191-198`.
11. **Payment must use a concrete funding method** — `Credit`/`Mixed` rejected (`SuppliersController.cs:200-207`);
    also at GL post (`CommerceAccountingService.cs:380-382`).
12. **Funding account must exist, be active, and have sufficient balance** — `SuppliersController.cs:690-755`
    (cashbox/visa/bank resolution + `INSUFFICIENT_FUNDS` / `ACCOUNT_INACTIVE` / `NO_CASHBOX_CONFIGURED`).
13. **Deleted suppliers excluded** from mutation paths (`SuppliersController.cs:209-210`; `LedgerService.cs:516`).
14. **Duplicate-phone / duplicate-email guard** — ❌ NO such rule exists for Supplier (verified; contrast Customer).
15. **Credit limit / credit-purchasing block** — ❌ NO such rule exists (Supplier has `PaymentTermsDays` only; no credit limit).
16. **Supplier merge / deactivate** — ❌ NOT implemented (no endpoints/services).

---

## 10. SQL Transactions

- **Transaction boundaries:** `LedgerService.ApplyPartyTransactionAsync` opens a `ReadCommitted` transaction *only if none is
  already active* (`LedgerService.cs:208-211`) — it enlists in a caller's transaction when present (composability).
  Payment and opening-balance batch open their own transaction (`SuppliersController.cs:174,429`).
- **Concurrency / Locks:** supplier row is pessimistically locked before balance read/mutation —
  Postgres `SELECT … FOR UPDATE` (`LedgerService.cs:511-519`), SQL Server `WITH (UPDLOCK, ROWLOCK, HOLDLOCK)` (`:522-531`).
- **In-flight additivity:** multiple ledger writes in one unit of work read the pending `RunningBalance` from the change tracker
  first (`LedgerService.cs:285-300`) so successive entries stay additive.
- **Rollback:** explicit `RollbackAsync` on `DuplicateOpeningBalanceException` and generic exceptions when the service owns the
  transaction (`LedgerService.cs:258-277`); payment path rolls back on failure (`SuppliersController.cs:333-339`);
  opening-balance batch rolls back (`SuppliersController.cs:584-589`).
- **Outbox:** supplier payment / opening balance / adjustment enqueue an accounting outbox row **inside** the business
  transaction (`SuppliersController.cs:288,542,721`; `AccountingOutboxService.cs:167-179,181-186`). The outbox worker is a
  recovery path, not a second authority (`AccountingOutboxProcessor.cs:1651-1710`).
- **Retry / Idempotency:** payment and opening-balance require durable idempotency claims + request-hash
  (`SuppliersController.cs:175-185,430-434`; `IFinancialMutationIdempotencyService`), with stored-response replay
  (`FinancialMutationIdempotencyService.cs:34-145` `TryClaimAsync`, `:147+` `CompleteAsync`). Idempotency key scoped
  `SUPPLIER:{TenantId}:{operationId}` (`SuppliersController.cs:782-783`).
- **Atomicity:** ledger row + shadow-balance update + `SupplierTransaction` (+ `SupplierPayment` for payments) are saved in the
  same transaction/unit of work (`LedgerService.cs:238-245`; `SuppliersController.cs:259-289`).
- **GL posting isolation:** outbox consumer posts GL inside its own execution-strategy transaction
  (`AccountingOutboxProcessor.cs:1657-1682`).

---

## 11. Events

- **Supplier domain emits NO domain/integration events.** `SuppliersController` performs no `IEventPublisher.PublishAsync`
  (verified: create/update/delete/write-audit only — `SuppliersController.cs:150,305,368,630`).
- **Financial effects are propagated via the accounting OUTBOX**, not supplier events:
  - `EnqueueSupplierPaymentAsync` (`AccountingOutboxService.cs:167-172`) → `ProcessSupplierPaymentAsync`
    (`AccountingOutboxProcessor.cs:1651-1683`) → `CommerceAccountingService.PostSupplierPaymentAsync`
    (`CommerceAccountingService.cs:369-421`).
  - `EnqueueSupplierOpeningBalanceAsync` (`AccountingOutboxService.cs:174-179`) → `ProcessSupplierOpeningBalanceAsync`
    (`AccountingOutboxProcessor.cs:1685+`).
  - `EnqueuePartyLedgerAdjustmentAsync` (`AccountingOutboxService.cs:181-186`) → GL posting
    (`CommerceAccountingService.cs:124+` `PostPartyLedgerAdjustmentAsync`).
- **Audit events:** `IAuditService.LogCreate/LogUpdate/LogDelete` (`SuppliersController.cs:150,368,630`) and
  `LogAction("RebuildBalance", …)` (`LedgerService.cs:146-169`) when balance is rebuilt.
- **Purchase / Purchase-Return events** (separate domains) also drive the supplier ledger via the same outbox family
  (`EnqueuePosPurchaseAsync` `:83-119`, `EnqueuePurchaseReturnAsync` `:221-239`) — these mutate the supplier ledger but are
  owned by the Purchase/PurchaseReturn controllers, not Supplier.
- **Projection events for Supplier:** ❌ None. Supplier is not projected to Mongo/ES.

---

## 12. Runtime Architecture

**Supplier (this domain) — SQL-only, no projection pipeline:**

```
HTTP (SuppliersController / PurchasesController / PurchaseReturnsController / StatementOfAccountController)
   │
   ▼
PostgreSQL  ── Suppliers / SupplierTransactions / SupplierPayments / AccountLedgers / PartyLedgerAdjustments
   │            (authoritative reads + writes; row-locked, tenant-scoped)
   │
   └─(payment / opening-balance / adjustment / purchase / return)▶ Accounting Outbox
                                                                      │
                                                                      ▼
                                                       CommerceAccountingService ─▶ GL (double-entry)
```

- Supplier list/detail reads straight from Postgres (`SuppliersController.cs:73-121`); search is a direct SQL `WHERE`
  contains query (`SuppliersController.cs:83-90`).
- **No MassTransit → Mongo → Search → Cache pipeline exists for Supplier** (verified: no `SupplierDocument`, no
  supplier projection consumer; the only `*Document` types for parties are storefront `OnlineCustomerDocument`).
- The `SQL → Outbox → MassTransit → Mongo → Search → Cache` pattern belongs to the storefront `OnlineCustomer` and product
  domains — NOT to the POS Supplier master.
- GL posting (CommerceHub financial) is reached *via the outbox consumer*, not via a supplier event bus publish.

---

## 13. Browser Test Specification

**Scenario A — Create**
1. POST create supplier (Name only) → expect 201, `balance=0`, `openingBalanceAmount=null`.
2. POST create with same phone as an existing supplier → expect 201 (NO duplicate-phone block — constraint gap, §9.14).
   Assertion: two active suppliers may share a phone.

**Scenario B — Update contact fields**
1. PUT update Name/Email/Address → 204; GET reflects change; balance unchanged.
2. Failure case: missing `Name` → 400 (ModelState).

**Scenario C — Opening balance one-time**
1. POST `/suppliers/opening-balances` Debit 500 for supplier S → success, `balanceAfter=500`.
2. Repeat same supplier → "Opening balance already exists" (`SuppliersController.cs:484-488`).
3. Replay identical request with same `Idempotency-Key` → stored response replay, no double posting.
   Assertion: exactly one `OpeningBalance` ledger row + one `SupplierTransaction(IsOpeningBalance=true)`
   (unique index `20260608120000_…`).

**Scenario D — Purchase updates ledger payable**
1. Create supplier S, create purchase invoice of 1000 (no payment).
2. GET supplier → `Balance` shadow may show 1000; authoritative payable from statement = 1000 (credit).
3. Record supplier payment 400 → balance 600.
   Assertion: payable = `Σ(Debit - Credit)`; never trust `Supplier.Balance` shadow alone — verify via statement ledger.

**Scenario E — Payment funding validation**
1. POST payment with amount 0 → 400 "Payment amount must be greater than zero" (`SuppliersController.cs:191-198`).
2. POST payment with `PaymentMethod=Credit` → 400 "must use a concrete funding method" (`SuppliersController.cs:200-207`).
3. POST payment with `PaymentMethod=Cash` when no cashbox configured → 400 `NO_CASHBOX_CONFIGURED`
   (`SuppliersController.cs:318-330`).
4. POST payment exceeding account balance → 400 `INSUFFICIENT_FUNDS` (`SuppliersController.cs:741-752`).

**Scenario F — Delete guards**
1. Supplier with outstanding balance → `can-delete=false`, `SUPPLIER_HAS_BALANCE`.
2. Supplier with purchase-invoice history → `SUPPLIER_HAS_INVOICES`.
3. Supplier with transaction history → `SUPPLIER_HAS_TRANSACTIONS`.
4. Clean supplier → soft-deleted, disappears from list (query filter).
   UI: `Suppliers.jsx` `handleDelete` → `ValidationDeleteModal` shows the guard reason.

**Scenario G — Cross-tenant isolation**
1. Auth as Tenant B, GET Tenant A supplier id → 404 (query filter).

**Scenario H — Supplier opening balance UI**
1. `SupplierOpeningBalance.jsx` loads `/suppliers/opening-balances`, pre-fills existing balances, POSTs batch.
   Assertion: saving twice for same supplier does not create a second opening balance.

**Business validation:** credit-limit enforcement, duplicate-phone rejection, and supplier deactivation are NOT testable —
those features do not exist for Supplier; do not assert on them.

---

## 14. Domain Ownership Matrix

| Capability | Owner | Evidence |
|------------|-------|----------|
| Supplier Identity | **Supplier** | `Supplier.cs:17-42` |
| Supplier Code | ❌ Not implemented | no field |
| Supplier Addresses (structured) | ❌ Not implemented | `Address` is free-text string only `Supplier.cs:27` |
| Supplier Contacts (separate) | ❌ Not implemented | free-text `Phone`/`Email` columns only |
| Supplier Groups / Types | ❌ Not implemented | no entity |
| Outstanding Balance (read, authoritative) | **AccountLedger** aggregation | `StatementOfAccountController.cs:1034-1042` |
| Running Balance (mutation) | **LedgerService** | `LedgerService.cs:217-256` |
| Shadow `Supplier.Balance` | Cache written by LedgerService (NOT authority) | `LedgerService.cs:442-445` |
| Supplier Ledger | **AccountLedger** (PartyType.Supplier) | `AccountLedger.cs:9-74` |
| Transaction History | **SupplierTransaction** (derived) | `SupplierTransaction.cs:9-52` |
| Payment recording | **SuppliersController.CreateSupplierPayment** (debit) | `SuppliersController.cs:155-340` |
| Purchase Liability | **PurchasesController** (credit) | `PurchasesController.cs:275-299` |
| Purchase Return | **PurchaseReturnsController** (debit) | `PurchaseReturnsController.cs:597-622` |
| Manual Adjustment | **PartyLedgerAdjustment** via StatementOfAccount | `StatementOfAccountController.cs:535-728` |
| GL / double-entry posting | **CommerceAccountingService** (outbox) | `CommerceAccountingService.cs:369-421` |
| Search | **Direct Postgres query** (no projection) | `SuppliersController.cs:73-108` |
| Tenant isolation | **Query filter + tenant context + row locks** | `LedgerService.cs:507-540` |
| Opening Balance | **LedgerService** (one-time, idempotent) | `SuppliersController.cs:408-590` |

---

## 15. Canonical Pattern

```
Supplier            → Identity + contact + payment-terms (master data, tenant-owned)
AccountLedger       → Financial authority (running balance = Σ Debit - Credit, PartyType.Supplier)
LedgerService       → Sole balance-mutation authority (row-locked, transaction-composable)
SupplierTransaction → Derived per-supplier history (BalanceAfter rebuildable)
SupplierPayment     → Immutable payment record (idempotency + outbox reconciliation)
PartyLedgerAdjustment → Durable manual adjustment (credit/debit note)
Purchases / Returns → Financial write sources (credit purchase, debit payment/return; never mutate Supplier directly)
Accounting Outbox   → CommerceAccountingService → GL double-entry
Search              → Direct SQL (no Mongo/ES projection for Supplier)
```

Supplier is an **identity aggregate**, not a financial aggregate. Money truth lives in the ledger; the supplier record
carries a convenience shadow only. Purchases reference the supplier; they do not belong to it.

---

## 16. Documentation Validation (misconception corrections)

- ❌ Supplier owns its balance. ✅ Balance is derived from `AccountLedgers`; `Supplier.Balance` is a shadow
  (`Supplier.cs:36`; authoritative read `StatementOfAccountController.cs:1040-1042`).
- ❌ Supplier balance can be edited directly. ✅ Balance changes only via `LedgerService` ledger writes
  (`LedgerService.cs:196-256`); update endpoint never touches balance (`SuppliersController.cs:358-365`).
- ❌ Supplier owns purchase invoices. ✅ Invoices *reference* the supplier (`Supplier.cs:61` nav;
  `PurchaseInvoice.cs:21` FK); purchases are owned by `PurchasesController`.
- ❌ Supplier has a credit limit that blocks purchasing. ✅ No credit-limit feature exists; `PaymentTermsDays` is descriptive
  only (`Supplier.cs:56`).
- ❌ Supplier can be deactivated. ✅ No inactive flag exists — only soft delete (`BaseEntity.cs:13`); "inactive" is UI copy only
  (`ValidationService.cs:219,232,244`).
- ❌ Supplier changes fan out through MassTransit → Mongo → Search. ✅ Supplier is SQL-only with no projection pipeline;
  that pattern belongs to the separate `OnlineCustomer` storefront domain.
- ❌ Supplier opening balance can be set repeatedly. ✅ One-time — guarded by app + service + partial unique indexes
  (`SuppliersController.cs:484-488`; `LedgerService.cs:319-357`; migration `20260608120000_PosCustomerSupplierAuthorityHardening.cs`).
- ❌ Supplier rejects duplicate phone/email like Customer. ✅ Supplier has **no** duplicate-phone/email guard
  (`SupplierConfiguration.cs:30-32` non-unique indexes; no dedupe code — contrast `CustomersController.cs:140-147`).
- ❌ Supplier merge is supported. ✅ No merge endpoint/service exists.
- ❌ Supplier payments are editable after posting. ✅ `SupplierPayment` is immutable; mutation is idempotent via
  `Idempotency-Key` + `SalesRequestIdempotency` record (`SuppliersController.cs:175-185`;
  `FinancialMutationIdempotencyService.cs:34-145`).

---

## 17. Final Architecture Conclusion

- **Supplier Authority:** Supplier is the tenant-owned authority for *identity, contact, and payment-terms data only*
  (`Supplier.cs:9-63`). No code, groups, types, credit limits, structured addresses/contacts, or inactive lifecycle.
- **Financial Authority:** `AccountLedger` + `LedgerService` are the exclusive financial authority; reads aggregate ledger rows,
  writes are row-locked and transaction-composable (`LedgerService.cs:196-540`). `Supplier.Balance` is a non-authoritative shadow.
- **Runtime Flow:** HTTP → PostgreSQL (authoritative) → (on payment/opening-balance/adjustment/purchase/return) accounting
  outbox → `CommerceAccountingService` GL. No Mongo/Search/Cache projection for Supplier.
- **Aggregate Ownership:** Supplier (identity); AccountLedger (financial); SupplierTransaction (derived history);
  SupplierPayment (immutable payment record); PartyLedgerAdjustment (manual adjustments); PurchaseInvoice (separate aggregate
  referencing supplier).
- **Transaction Model:** ReadCommitted, pessimistic party-row locks, change-tracker additivity, durable idempotency +
  request-hash for financial mutations, outbox for GL, explicit rollback and unique-index guards.
- **Projection Strategy:** None for Supplier — search and balance are computed directly from SQL. Projection pipelines exist
  only for the separate storefront `OnlineCustomer` identity domain.

### Implementation Status Legend
- **Implemented:** identity CRUD, tenant isolation, soft delete + delete guards, opening balance (one-time, idempotent),
  ledger-backed balance, supplier payment (idempotent, funding-validated), purchase/purchase-return ledger effects, manual
  adjustment (credit/debit note), audit logging, GL posting via outbox, `PurchasesBySupplier` report (SQL).
- **Partially Implemented:** `Supplier.Balance` shadow (maintained but not authoritative; rebuildable); "inactive" (UI language
  only, no field).
- **NOT Implemented:** Supplier Code, Supplier Address/Contact entities (free-text only), Supplier Groups, Supplier Types,
  Credit Limit / available credit, credit-purchasing gating, duplicate phone/email guard, supplier deactivate, supplier merge,
  standalone supplier payment allocation engine, POS Supplier projections/events.

---

*Generated by implementation reverse-engineering. Every section cites `file:line`. Gaps are marked explicitly; nothing is assumed.*
