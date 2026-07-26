# FLOW-02 — Deferred Payment (Credit Sale) Business Flow — Implementation-Backed Canonical Architecture

Authority: implementation only. Generated 2026-07-19.

## VERDICT

There is **NO deferred-payment / credit-sale business flow** in Shopixy.

- `PaymentMethod.Credit` exists in the enum (`PointOfSale.Core/Enums/PaymentMethod.cs:4-10`) but is **never referenced anywhere** in the codebase and is **explicitly rejected** as a sale payment method.
- The POS `SalesController.CreateSale` flow **forces full payment at point of sale**; it cannot create an unpaid/partially-paid receivable at sale time.
- A customer *can* owe the merchant money, but **only as a residual of an over/under-accounted customer ledger** produced by the Statement-of-Account (SOA) subsystem — never via a "credit sale" transaction.
- The AR Aging report and "Outstanding Balances" page are **read-only analytics** over `AccountLedger` debit/credit rows; there is **no "receive payment against an open invoice" write path** from either UI or API.

## Evidence

### 1. `Credit` payment method is dead and rejected
- `PaymentMethod.cs:4-10` declares `Cash, Card, BankTransfer, Mixed, Credit`.
- `SalesController.EnsureSupportedPaymentMethod` (`PointOfSale.API/Controllers/SalesController.cs:2580-2588`) allows **only** `Mixed`, `Cash`, `Card`:
  ```csharp
  if (paymentMethod != PaymentMethod.Mixed
      && paymentMethod != PaymentMethod.Cash
      && paymentMethod != PaymentMethod.Card)
      throw new ValidationException("Unsupported payment method");
  ```
- `PaymentMethod.Credit` has **0 references** anywhere in the solution (grep: 0 matches). `BankTransfer` is likewise declared but unused in POS sales.

### 2. POS sale requires full payment — no partial, no zero
`ValidateAndNormalizePaymentAmounts` (`SalesController.cs:2590-2675`):
- Cash (`SalesController.cs:2634-2652`): `if (roundedPaidAmount < roundedTotalAmount) throw new ValidationException("Invalid cash payment");` → **underpay rejected**.
- Card (`SalesController.cs:2654-2672`): `if (roundedPaidAmount != roundedTotalAmount) throw ...` → **exact payment required**.
- Mixed: cash+card must equal total exactly.
- Fall-through (`SalesController.cs:2674`): `throw new ValidationException("Unsupported payment method");`

→ A POS sale **cannot** leave a balance outstanding. There is no `InvoiceStatus.Pending` / `PartiallyPaid` sales path. `InvoiceStatus` enum (`PointOfSale.Core/Enums/InvoiceStatus.cs`) is `Draft, Completed, Cancelled, PartiallyReturned, FullyReturned` — no "Unpaid/Open" state.

### 3. Customer debt arises only from the SOA ledger, not from sales
When a sale is posted with a `CustomerId`, `SalesController.CreateSale` (`SalesController.cs:327-398`):
- Always writes a **debit** `TransactionType.Sale` ledger entry for the full invoice total ("customer owes us").
- Then, **if** `ResolveCustomerLedgerPaymentAmount(invoice) > 0`, writes a **credit** `TransactionType.PaymentReceived`.
- Therefore the net effect of a normal paid sale is **zero outstanding** (debit = credit). Debt only persists if a later adjustment/correction leaves debit > credit — i.e., it is a *ledger residue*, not a designed "credit sale."

### 4. Receivable = a running balance over `AccountLedger`, not a dedicated aggregate
- `ILedgerService.ApplyPartyTransactionAsync` (`PointOfSale.API/Services/LedgerService.cs:196-241`) maintains `Customer.Balance` as `Σ(Debit) − Σ(Credit)` and writes `AccountLedger` rows.
- `PartyType` enum: only `Customer, Supplier` (`PointOfSale.Core/Enums/PartyType.cs`). No "Receivable" or "AR" aggregate.
- Balance query: `AccountLedgers.Where(PartyType==Customer).Sum(Debit−Credit)` (e.g. `CustomersController.cs:90-94`, `StatementOfAccountController.cs:227-230`).
- AR Aging (`ReportsController.cs:638-720`) is a **read projection**: it sums `DebitAmount−CreditAmount` per customer as of a date and buckets by age. It does **not** mutate anything.

### 5. "Outstanding Balances" page is read-only, links to a settlement UI that is orphaned
- `OutstandingBalances.jsx` reads `/StatementOfAccount/outstanding/{partyType}` and offers a "Receive Payment" button → navigates to `/payments?partyType&partyId`.
- `Payments.jsx` calls `paymentsApi.receivePayment` → `POST /Payments/receive` (`PointOfSale.Web/src/services/soaApi.js:66-71`).
- **No backend controller implements `POST /Payments/receive` or `POST /Payments/make` or `GET /Payments/customer/{id}`.** The only `PaymentsController` (`PointOfSale.API/Controllers/PaymentsController.cs:31-34`) is `[Route("api/payments")]` for e-commerce order PaymentIntents/COD only (`intents/{orderId}`, `cod/confirm/{orderId}`). The SOA settlement write path is **absent** — the frontend button points at an unimplemented endpoint (404 at runtime).

### 6. The only customer-ledger write paths are Opening Balance and Adjustments
`StatementOfAccountController.cs`:
- `POST /StatementOfAccount/opening-balance` (`:263`) — seeds an `OpeningBalance` ledger row.
- `POST /StatementOfAccount/adjustment` (`:534-755`) — CreditNote/DebitNote/CreditAdjustment/DebitAdjustment only (`StatementOfAccountController.cs:568-571`).
- `POST /StatementOfAccount/query`, `GET .../balance/{partyType}/{partyId}`, `GET .../outstanding/{partyType}` — reads.
- No `TransactionType.PaymentReceived` can be written to a customer ledger **outside** the `SalesController` sale path. grep for `TransactionType.PaymentReceived` outside Sales: only SOA *read filters* (`StatementOfAccountController.cs:1001,1004`) and tests.

## How Shopixy currently represents customer debt

- **Model:** A running signed balance on the `Customer` entity, materialized from `AccountLedger` rows (`Debit − Credit`). It is **not** an invoice-level receivable; it is a party-level net position.
- **Can a customer owe money?** Yes — but only as a net debit in `AccountLedger` (e.g., opening balance seeded as debit, or a sale recorded as debit with no matching credit because payment was never posted, or a debit adjustment).
- **Which aggregate owns it?** `Customer` (balance scalar) backed by `AccountLedger` rows, written exclusively through `ILedgerService` (`LedgerService.ApplyPartyTransactionAsync`). There is **no AccountsReceivable / Invoice / Receivable aggregate**.
- **Runtime:** Owned by the POS admin `StatementOfAccountController` + `LedgerService`. Not Treasury, not the e-commerce `PaymentService`, not CRM.

## Accounts Receivable — implementation finding

- "Receivable" = `Customer.Balance > 0` (net debit) in the SOA/Customer ledger. It is a **ledger entry / running balance**, not a journal, not a separate aggregate, not treasury, not CRM.
- The `AccountsReceivableAgingDto` (`PointOfSale.API/DTOs/AccountsReceivableAgingDto.cs`) and `ReportsController.GetAccountsReceivableAging` are **reporting-only**.

## Payment Collection — implementation finding

- **There is no implemented "receive payment against outstanding balance" workflow.**
- The e-commerce `PaymentService` (`PointOfSale.API/Services/Payments/PaymentService.cs`) handles *order* payments (PaymentIntent/COD/ManualWallet) — a different domain, not customer-ledger settlement.
- The SOA "receive payment" UI (`Payments.jsx` → `POST /Payments/receive`) has **no server implementation**.
- The only way a customer credit appears today is at *sale time* inside `SalesController.CreateSale` (`SalesController.cs:360-397`) — i.e., payment is collected at the register, not later.

## Outstanding Balance Lifecycle — reverse-engineered

```
[Invoice Created (POS sale w/ customer)]
   → Ledger: Debit Sale (full total)            SalesController.cs:330-339
   → Ledger: Credit PaymentReceived (paid amt)  SalesController.cs:369-378
   → NET = 0 (no outstanding)                   by construction
        │
        ├─(only if payment never posted / manual adjustment)
        ▼
[Outstanding Balance "exists" = Customer.Balance > 0]
   → READ ONLY via AccountLedger.Sum(Debit−Credit)
   → Shown in OutstandingBalances.jsx, AR Aging report
        │
        ▼
[Customer Pays Later]  ← NOT IMPLEMENTED
   → UI button → POST /Payments/receive → 404 (no controller)
   → No Ledger update, no Journal update, no Receivable closure
```

There is **not** a closed loop: the lifecycle stops at "read-only balance." Settlement-to-zero is not wired end-to-end.

## Runtime Collaboration

| Concern | Runtime / Owner | Evidence |
|---|---|---|
| POS sale + immediate customer payment | `SalesController` + `LedgerService` | `SalesController.cs:327-398` |
| Customer/Supplier net balance | `LedgerService.ApplyPartyTransactionAsync` | `LedgerService.cs:196-241` |
| Opening balance / adjustments | `StatementOfAccountController` | `StatementOfAccountController.cs:263,534` |
| AR aging / outstanding reads | `ReportsController`, `StatementOfAccountController` | `ReportsController.cs:638`, `StatementOfAccountController.cs:222,263` |
| E-commerce order payment | `PaymentService` (separate) | `PaymentService.cs` |
| Treasury / Accounting journal | CommerceAccounting (sale journal only) | `SalesController.cs:411` `PostPosSaleAsync` |
| **Deferred payment / later settlement** | **NONE** | `POST /Payments/receive` has no controller |

## Hermes Browser Test Specification (proves NON-existence)

Deterministic assertions that must hold:

1. **Credit method rejected at API** — `POST /api/Sales` with `paymentMethod: "Credit"` returns 400 `Unsupported payment method`. (Confirmed by `EnsureSupportedPaymentMethod`, `SalesController.cs:2580`.)
2. **No underpaid cash sale** — `POST /api/Sales` cash with `paidAmount < total` returns 400 `Invalid cash payment`. (`SalesController.cs:2646`.)
3. **No zero-payment sale** — cash `paidAmount: 0` returns 400. (Same path.)
4. **POS cash sale leaves no outstanding** — after a paid cash sale to a customer, `GET /StatementOfAccount/balance/Customer/{id}` returns balance ~0.
5. **Orphaned settlement endpoint 404** — `POST /api/Payments/receive` returns 404 (no controller). The "Receive Payment" button in `OutstandingBalances.jsx` points at an unimplemented route.
6. **AR aging is read-only** — `GET /api/Reports/accounts-receivable-aging` returns data but mutates nothing; no matching "settle" verb.
7. **No CreditSale/Receivable aggregate** — codebase grep for `CreditSale`, `AccountsReceivable` (as aggregate), `OpenInvoice`, `InvoiceSettlement`, `ReceivePayment` (impl) returns only reporting/DTOs, no write path.

## Documentation Validation (assumptions corrected)

- Docs such as `SOA-QUICKSTART.md:119` ("Customer bought goods on credit"), `SOA-COMPLETE-IMPLEMENTATION.md:58-60` ("Sales invoices (on credit)"), and `POS-TESTING-GUIDE.md:153-154` ("Customer balance is updated (if credit sale)") describe the *customer-ledger accounting model*, **not** a deferred-payment feature. "On credit" there means "a customer is attached to the invoice and a ledger debit is recorded," not "payment deferred." These docs are **misleading** and should be amended: there is no credit-sale transaction type; debt is a ledger residue.
- `PaymentMethod.Credit` in the enum is dead code and must not be inferred as a feature.

## Final Architecture Answer

**How does Shopixy implement deferred customer payments, accounts receivable, and payment settlement?**

It does **not** implement deferred customer payments or a later-settlement workflow.

- **Deferred payments:** absent. The POS sale enforces full payment (`Cash`/`Card`/`Mixed`) and rejects `Credit`. No "pay later" path exists.
- **Accounts receivable:** implemented as a *party-level net debit* on `Customer.Balance`, derived from `AccountLedger` (`Σ Debit − Σ Credit`), written only through `ILedgerService`. There is no Receivable/Invoice aggregate, no open-invoice state, no treasury ownership.
- **Payment settlement:** the *only* customer payment recorded against the ledger is captured **at POS sale time** inside `SalesController.CreateSale`. The intended later-settlement UI (`Payments.jsx` → `POST /Payments/receive`) is **orphaned — no backend controller exists**, so outstanding balances shown in `OutstandingBalances.jsx` / AR Aging cannot be closed through the application today.

**Why:** the system was built as a cash/immediate-payment POS with a customer-ledger *accounting* subsystem for balances, adjustments, and aging reports — not as an AR/credit-sales ERP. Deferred payment would require (a) accepting `PaymentMethod.Credit` or a "defer" flag in `SalesController`, (b) a new `POST /Payments/{receive|make}` controller that writes `TransactionType.PaymentReceived` via `LedgerService`, and (c) wiring the currently-orphaned `Payments.jsx` endpoint.
