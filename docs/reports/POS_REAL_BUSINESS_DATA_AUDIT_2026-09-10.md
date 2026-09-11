# POS Real Business Data Audit - 2026-09-10

## Result

The real-data POS sale pipeline passed inventory, payment routing, customer balance, accounting, outbox, idempotency, rollback, receipt, and responsive-layout checks for Cash, Card, and Mixed tenders.

Persisted Draft editing could not be exercised legitimately. The live tenant had no Draft invoices, new POS sales are created directly as Completed, and Hold Sale stores a browser-local snapshot after cancelling the server cart. Creating or converting a Draft directly in PostgreSQL was intentionally not performed.

## Scope

- Tenant: `shopixy` (`83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`), currency EGP
- Warehouse: `Main Warehouse` (`4a50f767-ce97-478f-af24-66e8e5e14c7f`)
- Product: `Samsung 50-inch Smart TV`, SKU `samsung-tv-50`
- Variant: `810d8aa8-979c-467b-a1ef-954cd07bbe1e`
- Unit: `Piece`, conversion factor `1`
- Unit price: `100.00`; tax: `10%`; cost: `50.00`
- Customer: `عميل نقدي` (`0fd87fd8-c455-4f23-90ff-80f5f0090b71`)
- UI: `http://localhost:5173/pos`
- API: `http://localhost:5010`
- PostgreSQL authority: configured remote Ubuntu PostgreSQL instance

Browser workflows used the normal login form, real cookie-backed session authority, rendered POS controls, real cart lifecycle endpoints, and the real `POST /api/Sales` endpoint. Database verification used repeatable-read, read-only transactions that were always rolled back.

## Findings

### 1. Persisted Draft editing is unreachable for newly created POS sales

Severity: High for the advertised existing-sale editing workflow.

- `PointOfSale.API/Controllers/SalesController.cs:222` creates every new POS invoice with `InvoiceStatus.Completed`.
- `PointOfSale.API/Controllers/SalesController.cs:1383` permits rewriting only `InvoiceStatus.Draft` invoices.
- `PointOfSale.Web/src/pages/POS.jsx:831` handles Hold Sale by disposing the server cart and calling the local hold implementation.
- `PointOfSale.Web/src/hooks/usePOSCart.js:531` saves held sales through browser local storage, not `SalesInvoices`.
- Read-only live query at `2026-09-10T18:21:22.910947+03:00` returned `drafts: []`.

Impact: a newly created POS sale cannot enter the only status accepted by the existing update endpoint. The UI can retrieve local held carts, but this does not certify persisted invoice editing, concurrency, accounting reversal/repost, or stock-restoration behavior against a legitimate Draft.

Recommendation: define one authoritative lifecycle. Either create persisted Draft invoices before checkout and complete them explicitly, or remove/replace the Draft-only invoice editing path with a supported correction workflow.

### 2. Normal absence of an open cart emits a browser error

Severity: Low.

- `PointOfSale.API/Services/Pos/PosCartRuntimeService.cs:147` returns Not Found when no open cart exists.
- `PointOfSale.Web/src/services/posCartApi.js:51` expects and catches that `404` as normal control flow.
- Fresh Card and Mixed browser runs both recorded `GET /api/pos-carts/open` as `404` and Chromium logged `Failed to load resource: the server responded with a status of 404 (Not Found)`.

Impact: successful POS sessions do not have a fully clean browser console/network-error record, and expected control flow is harder to distinguish from genuine missing-resource defects.

Recommendation: represent “no current open cart” as `200` with an explicit nullable result or `204`, then handle that response without an exception path.

## Transactions

| Tender | Invoice | Invoice ID | Cash | Card | Total | Viewport |
| --- | --- | --- | ---: | ---: | ---: | --- |
| Cash | `INV-20260910-0024` | `5b6277a4-9730-4e2b-b957-3459de5b21e4` | 110.00 | 0.00 | 110.00 | 1440x1000 |
| Card | `INV-20260910-0025` | `ee63f773-3a23-41ab-8d10-23dc1763926c` | 0.00 | 110.00 | 110.00 | 390x844 |
| Mixed | `INV-20260910-0026` | `6812fe5e-d681-4ba2-a172-b3c35cbeb4f4` | 55.00 | 55.00 | 110.00 | 1440x1000 |

Every transaction persisted exactly one item with quantity/base quantity `1`, conversion factor `1`, subtotal `100.00`, tax `10.00`, total `110.00`, cost price snapshot `50.00`, and cost amount snapshot `50.00`.

## Aggregate Proof

| Measure | Before | After | Expected delta | Actual delta |
| --- | ---: | ---: | ---: | ---: |
| Variant quantity | 44.00 | 41.00 | -3.00 | -3.00 |
| Variant movement count | 1 | 4 | +3 | +3 |
| Variant projection mutation version | 1 | 4 | +3 | +3 |
| Variant stock outbox rows | 1 | 4 | +3 | +3 |
| Completed sales | 1 | 4 | +3 | +3 |
| Draft sales | 0 | 0 | 0 | 0 |
| Cashbox `1010` | 22.22 | 187.22 | +165.00 | +165.00 |
| Visa Clearing `1110` | 0.00 | 165.00 | +165.00 | +165.00 |
| POS Sales `4010` | 22.22 | 352.22 | +330.00 | +330.00 |
| COGS `5010` | 6.00 | 156.00 | +150.00 | +150.00 |
| Inventory `1310` | -6.00 | -156.00 | -150.00 | -150.00 |
| Customer balance | 0.00 | 0.00 | 0.00 | 0.00 |
| Customer account-ledger rows | 0 | 6 | +6 | +6 |
| Customer account-ledger net | 0.00 | 0.00 | 0.00 | 0.00 |
| Customer transaction rows | 0 | 6 | +6 | +6 |
| Customer transaction net | 0.00 | 0.00 | 0.00 | 0.00 |
| Posted journals | 28 | 34 | +6 | +6 |
| Reversed journals | 2 | 2 | 0 | 0 |
| Accounting outbox rows | 23 | 26 | +3 | +3 |
| Processed accounting outbox rows | 23 | 26 | +3 | +3 |
| Stock outbox rows | 48 | 51 | +3 | +3 |
| Published stock outbox rows | 48 | 51 | +3 | +3 |

Pending and failed counts remained zero in both accounting and stock outboxes.

## Per-Invoice Accounting

Each invoice produced two posted, balanced journals:

- Revenue journal: debit the tender authority and credit POS Sales `110.00`.
- COGS journal: debit COGS and credit Inventory `50.00`.

Tender debit authority was correct:

- Cash: Cashbox `1010` debit `110.00`.
- Card: Visa Clearing `1110` debit `110.00`.
- Mixed: Cashbox `1010` debit `55.00` and Visa Clearing `1110` debit `55.00`.

Each customer sale generated a `110.00` debit and a `110.00` payment credit in both customer-ledger representations, leaving the running and persisted balance at zero.

## Duplicate And Rollback Proof

### Duplicate replay

The completed Mixed cart `0c3d9e9b-ef44-4733-98e1-272ab90145d8` was replayed through an authenticated browser session:

- Identical payload: HTTP `200`, same invoice ID `6812fe5e-d681-4ba2-a172-b3c35cbeb4f4`.
- Changed cash/card split with the same cart authority: HTTP `400`, `Idempotency key reuse with different payload`.

The before/after snapshots were identical: sales `4`, stock `41`, movements `4`, journals `36`, customer ledger rows `6`, accounting outbox `26`, and stock outbox `51`.

### Transaction rollback

A real active cart `16de7ba7-473b-424d-bddb-fb018d6fdd3f` containing one selected product was submitted with an invalid Mixed split. Server payment validation occurs after invoice item and stock mutation state is staged in `SalesController.CreateSale`.

- Invalid sale: HTTP `400`, `Sale request contains invalid payment or pricing data.`
- Cart after rejection: still `Active`, version `2`.
- Cleanup: official cart `cancel` transition returned HTTP `200`, status `Cancelled`, version `3`.

The final business snapshot remained unchanged from the pre-failure snapshot across stock, movements, sales, customer ledgers, treasury balances, journals, and sale outboxes. This proves the staged sale effects rolled back atomically.

## Browser Evidence

- Cash receipt: `C:\Users\fadya\AppData\Local\Temp\opencode\pos-real-cash-receipt.png`
- Card mobile receipt: `C:\Users\fadya\AppData\Local\Temp\opencode\pos-real-card-mobile-receipt.png`
- Mixed receipt: `C:\Users\fadya\AppData\Local\Temp\opencode\pos-real-mixed-receipt.png`

All three receipts displayed the correct invoice, customer, SKU, unit, quantity, subtotal, tax, total, tender, and paid amount. Desktop and 390px mobile runs reported no document-level horizontal overflow. No page exceptions occurred. The Cash run had no post-login console or HTTP error responses; fresh Card and Mixed runs had only the expected open-cart `404` described above. Request cancellation for a trailing quick-access refresh occurred when the audit context closed after evidence capture and was not treated as an application failure.

## Final State

Final read-only snapshot: `2026-09-10T18:20:49.802849+03:00`.

- Stock: `41.00`, reserved `0.00`, damaged `0.00`, available `41.00`.
- Sales: `4` total, `4` Completed, `0` Draft.
- Customer balance and both customer ledger nets: `0.00`.
- Cashbox: `187.22`; Visa Clearing: `165.00`.
- Journals: `36` total, `34` posted, `2` reversed.
- Accounting outbox: `26/26` processed, `0` pending, `0` failed.
- Stock outbox: `51/51` published, `0` pending, `0` failed.

No application source code, persisted balance, invoice status, or database row was manually edited. No commit was created.
