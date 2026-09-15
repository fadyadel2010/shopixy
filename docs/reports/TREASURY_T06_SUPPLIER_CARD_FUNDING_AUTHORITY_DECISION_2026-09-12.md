# Treasury T-06 Supplier Card Funding Authority Decision - 2026-09-12

## 1. Executive Verdict

T-06 is a confirmed supplier-payment authority defect, not a missing Treasury mapping and not an unexplained ledger-balance defect.

Account `1020 POS Visa Account` is an Asset account that the active purchase posting path credits whenever a supplier purchase has a Card portion. Its shopixy balance of EGP -3.80 is fully supported by immutable, balanced purchase journals. The balance is negative because the system records outgoing supplier Card activity as a credit to a debit-normal Asset without recording what actually funded the payment.

The repository and database do not establish whether those Card portions were funded by:

- A debit card linked to a Bank asset.
- A credit card creating an issuer liability.
- A personal/employee/owner card creating a reimbursement payable.
- Another evidenced funding arrangement.

The word `Card` is tender evidence only. It is not funding-account authority. Selecting Bank or Liability from current data would invent a financial fact.

**Business funding-instrument decision required before implementation.**

Until that decision is supported by external evidence:

- Keep `1020` outside Treasury.
- Do not map it as Cashbox, Bank, or Clearing.
- Do not reclassify EGP 3.80.
- Do not redirect supplier Card postings to `1030` or `1110`.
- Preserve customer Card collection and provider settlement behavior.

## 2. Decision Question

The implementation question is not "where should Card post?" It is:

> What economically controlled instrument supplied the money or credit used to pay each supplier Card amount, and what ledger account is authoritative for that instrument?

The answer determines the accounting model:

- Debit/bank card: credit the linked Bank Asset.
- Credit card: credit a dedicated credit-card Liability.
- Personal or reimbursable card: credit the appropriate reimbursement Liability.
- Prepaid or custodial card: credit a separately evidenced Asset, subject to its own reconciliation authority.

No single default is safe for all meanings of `Card`.

## 3. Scope

This report provides read-only T-06 discovery and a decision gate for:

- Supplier purchase Card portions.
- Standalone supplier Card payments.
- Account `1020` identity, history, posting, reversal, replay, reporting, provisioning, and retirement dependencies.
- Candidate future funding models.
- The evidence required before implementation or historical reclassification.

T-01 through T-05 behavior, customer payment semantics, provider settlement, unrelated reporting defects, and unrelated CommerceHub finance flows are not changed by this report.

## 4. Safety Constraints

The discovery observed these constraints:

- Read-only database access only.
- No application code changes.
- No database writes, migrations, mappings, journals, account edits, or balance edits.
- No browser financial mutations.
- No modification or reversal of pre-existing working-tree changes.
- Existing journal and reversal history remains immutable.
- Customer Card and `1110 Visa Clearing` semantics must be preserved.
- Account `1020` must not be made Treasury authority merely to expose its balance.

## 5. Evidence Method

Database evidence was read through an isolated Npgsql harness. Each query:

- Runs in a repeatable-read transaction.
- Executes `SET TRANSACTION READ ONLY`.
- Accepts only SQL beginning with `SELECT` or `WITH`.
- Explicitly rolls back.

The current source tree, migrations, tests, historical documents, `git blame`, and `git log -S` were inspected. Live SQL was correlated across ledger accounts, journals, ledger lines, purchases, purchase payments, supplier ledgers, outbox rows, Banks, BankTransactions, FinancialAccounts, FinancialTransactions, VisaTransactions, payment methods, and Treasury mappings.

## 6. Tenant Boundary

| Field | Value |
| --- | --- |
| Tenant | `shopixy` |
| Tenant ID | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Currency | EGP |
| Database | `PosEcommerceDb_Baseline` |
| Read-only state observed | 2026-09-12 |
| Other tenants inspected | `ahramstore`, `antonnewstore`, `antonstore`, `greatone`, `highquality` |

All six tenants have account `1020`. Only shopixy has `1020` ledger activity.

## 7. Account 1020 Identity

| Property | Value |
| --- | --- |
| Account ID | `019f9f7b-1567-701b-bc70-7deda2e00781` |
| Code | `1020` |
| Name | `POS Visa Account` |
| Description | `POS card terminal transactions` |
| Parent | `1000 Cash and Bank` |
| Type | Asset |
| Normal balance | Debit |
| Current balance | EGP -3.8000 |
| Ledger debits | EGP 2.2000 |
| Ledger credits | EGP 6.0000 |
| Ledger lines | 8 |
| System | Yes |
| Clearing | No |
| Prevent negative | No |
| Treasury mapping | None |
| Payment-method mapping | None |

The cached balance and ledger-derived balance both equal EGP -3.80. T-06 is therefore not cache drift.

## 8. Creation And Historical Intent

`SystemAccountTemplates.PosVisaAccount` creates `1020` as an Asset under `1000` (`SystemAccountTemplate.cs:104-115`). `AccountCodes.PosVisaAccount` fixes the code (`IAccountResolver.cs:44-54`). The template, constant, and supplier purchase posting originated together in initial commit `3fb3d5ccb94db256c8b98ea5e9449d97b02f352e` on 2026-07-26.

Older Visa documents describe a pooled model in which customer Card receipts increased one Visa balance and supplier Card purchases decreased it. The stated balance represented "amount received via card payments minus card purchases" (`VISA-ACCOUNT-IMPLEMENTATION.md:200-206`). That was a net wallet-style model, not proof of a bank deposit account or a credit-card liability.

The active architecture later separated incoming customer Card receivables into `1110 Visa Clearing`, but supplier purchase Card outflows remained on `1020`. The result is a stranded half of the legacy pooled model.

## 9. Current Purchase Contract

`CreatePurchaseRequest` carries:

- `PaymentMethod`
- Optional `InitialPaymentMethod`
- `PaidAmount`
- Optional `CashAmount`
- Optional `CardAmount`
- Free-text `Notes`

It carries no Bank ID, ledger Account ID, card instrument ID, issuer, masked number, last four digits, card type, credit limit, or reimbursement owner (`PurchasesController.cs:2067-2088`).

`PurchasePaymentSplitter.Resolve` reduces every supported purchase settlement to Cash and Card amounts. It rejects `BankTransfer` and does not resolve an instrument (`PurchasePaymentSplitter.cs:23-106`).

## 10. Purchase UI Authority Gap

The purchase UI offers `Cash`, `Card`, `Mixed`, and `Credit`. A Credit purchase with an initial payment can use Cash, Card, or Mixed. The form captures amounts but no funding instrument (`PurchaseInvoice.jsx:1032-1157`).

Consequences:

- A user cannot identify which bank-funded card was used.
- A user cannot distinguish debit card from credit card.
- A user cannot identify an issuer liability.
- A user cannot identify a personal or reimbursable card.
- The API cannot validate a fact the UI never supplies.

## 11. Purchase Payment Persistence Gap

`PurchaseInvoice` persists only the aggregate `PaymentMethod` and amounts implied by related payment rows (`PurchaseInvoice.cs:17-73`). `PurchaseInvoicePayment` persists:

- Purchase invoice ID.
- Date.
- Amount.
- `PaymentMethod` enum.
- Reference number.
- Notes.

It has no funding authority field (`PurchaseInvoicePayment.cs:8-20`). The controller generates the Card payment reference from the purchase invoice number and generates generic notes (`PurchasesController.cs:1988-2020`). These fields cannot be treated as external card evidence.

## 12. Purchase Outbox And Replay Contract

Purchase outbox payloads carry only:

- `InvoiceId`.
- `CashAmount`.
- `CardAmount`.
- `MutationVersion`.
- `OperationKey` for mutations.

The idempotency key encodes tenant, invoice, amounts, and mutation version, but not funding instrument (`AccountingOutboxService.cs:85-165`). The processor reloads and locks the current purchase, checks mutation version, then calls `PostPosPurchaseAsync` with only Cash and Card amounts (`AccountingOutboxProcessor.cs:1619-1685`).

Live state contains six Card-bearing purchase messages, all processed, none pending, and none failed. A future implementation still needs an explicit legacy replay rule because retained old payloads have no funding authority. They must not silently acquire a newly configured default account.

## 13. Purchase Posting Authority

For a positive Card amount, `CommerceAccountingService.PostPosPurchaseAsync`:

1. Resolves `AccountCodes.PosVisaAccount`.
2. Debits Inventory and Input Tax Receivable as applicable.
3. Credits `1020` for the Card amount.
4. Credits Supplier Payables for any unpaid remainder.
5. Posts an immutable journal (`CommerceAccountingService.cs:294-377`).

The `1020` destination is hard-coded. No source-document field can override or justify it.

## 14. Outflow Control Boundary

`FinancialPostingService.ValidateTreasuryOutflowAuthorityAsync` calculates a cash decrease only across accounts with active Cashbox or Bank Treasury mappings (`FinancialPostingService.cs:735-760`). For a mapped Bank outflow it validates current SQL source amount and prior posted decrease (`FinancialPostingService.cs:762-871`).

Because `1020` is unmapped, a purchase Card credit is not recognized as a Treasury outflow and receives no balance-capacity validation. This is consistent with `1020` not being Bank authority, but it leaves the economic funding source uncontrolled.

Purchase create performs a pre-check only for the Cash portion (`PurchasesController.cs:1764-1817`). Card portions have no equivalent Bank availability, issuer-limit, or configured-instrument check.

## 15. Complete 1020 Ledger Lineage

| Journal | Status | Source/reference | Debit | Credit | Balance after |
| --- | --- | --- | ---: | ---: | ---: |
| `JE-2026-000047` | Posted | PurchaseInvoice / `PUR-20260910-1000` | 0.00 | 1.10 | -1.10 |
| `JE-2026-000048` | Reversed | PurchaseInvoice / `PUR-20260910-1001` | 0.00 | 1.10 | -2.20 |
| `JE-2026-000051` | Posted | PurchaseInvoice / `PUR-20260910-1004` | 0.00 | 1.10 | -3.30 |
| `JE-2026-000052` | Posted | PurchaseInvoice / `PUR-20260910-1005` | 0.00 | 0.50 | -3.80 |
| `JE-2026-000053` | Reversed | PurchaseInvoice / `PUR-20260910-1001` | 0.00 | 1.10 | -4.90 |
| `JE-2026-000054` | Posted | Reversal / `JE-2026-000048` | 1.10 | 0.00 | -3.80 |
| `JE-2026-000055` | Posted | Reversal / `JE-2026-000053` | 1.10 | 0.00 | -2.70 |
| `JE-2026-000056` | Posted | PurchaseInvoice / `PUR-20260910-1001` | 0.00 | 1.10 | -3.80 |
| **Total** | | | **2.20** | **6.00** | **-3.80** |

The two reversed journals remain in immutable history and are offset by posted reversal journals. The final EGP -3.80 is exactly reproducible.

## 16. Current Source Documents

| Purchase | ID | Stored method | Card portion | Current net 1020 credit | Source note |
| --- | --- | --- | ---: | ---: | --- |
| `PUR-20260910-1000` | `9f507b53-faba-46ec-9f6e-0dec01130b80` | Mixed | 1.10 | 1.10 | `AUDIT-MIXED-1789063183469` |
| `PUR-20260910-1001` | `a3137eca-32b1-48a1-b584-f2fe3960c5e5` | Mixed | 1.10 | 1.10 | `AUDIT-MIXED-EDITED-1789068311353` |
| `PUR-20260910-1004` | `58f26b9b-36c2-4a59-9b23-202ec2ecba19` | Card | 1.10 | 1.10 | `AUDIT-CARD-1789063213922` |
| `PUR-20260910-1005` | `94b43c19-4fba-42a5-9489-64a2e925c483` | Credit with initial Card | 0.50 | 0.50 | `AUDIT-PARTIAL-CREDIT-1789063213922` |
| **Total** | | | **3.80** | **3.80** | |

All four use supplier `amer` (`81ed383e-511f-4647-84a7-d4ed36177894`). Their `PurchaseInvoicePayment.ReferenceNumber` values are the generated purchase numbers. Their notes and references identify audit scenarios, not a real bank account, issuer statement, or card instrument.

## 17. Related Database Evidence

For shopixy, live counts are:

| Evidence set | Count |
| --- | ---: |
| Banks | 0 |
| BankTransactions | 0 |
| VisaTransactions | 0 |
| FinancialTransactions | 0 |
| Standalone SupplierPayments | 0 |
| Standalone supplier Card payments | 0 |
| Purchase Card payments with a reference different from the generated invoice number | 0 |

The legacy `FinancialAccounts` table contains a zero-balance `Visa Account` with account number `VISA-001` and a zero-balance Cash account. It has no transaction evidence linking the four purchases to a real card or bank.

## 18. Schema Authority Search

A database-wide column search found no supplier-card authority fields for:

- Payment instrument.
- Funding account.
- Card issuer.
- Masked card number.
- Last four digits.
- Credit-card limit.
- Credit-card liability account.

Relevant existing identifiers belong to other domains, such as `Expenses.BankId`, customer Card amount allocation, provider settlement Bank references, and CommerceHub payout evidence. They do not authorize supplier purchase Card funding.

No dedicated credit-card Liability exists in the seeded chart. Existing liabilities are for supplier payables, customer deposits, Treasury suspense, marketplace vendors, tax, and affiliates. Reusing one would corrupt its meaning.

## 19. Standalone Supplier Payment Inconsistency

The standalone supplier payment request also carries only amount, `PaymentMethod`, reference, and notes (`SuppliersController.cs:1062-1068`). Its entity has no funding account or instrument (`SupplierPayment.cs:10-39`).

For Card, the controller validates balance against `1110 Visa Clearing` (`SuppliersController.cs:728-792`) and `CommerceAccountingService.PostSupplierPaymentAsync` credits `1110` (`CommerceAccountingService.cs:391-430`). This is inconsistent with purchase Card, which credits `1020`.

Both rules are unsafe:

- `1020` does not identify the actual supplier funding source.
- `1110` is incoming customer processor receivable, not an outgoing supplier card.

The future decision must cover both workflows so the same business instrument has one authority model.

## 20. Customer Card And Settlement Boundary

Customer Card behavior is economically distinct and must remain unchanged:

- POS/customer Card collection debits `1110 Visa Clearing` as an incoming provider receivable.
- The seeded `CARD` payment method maps to `1110` as deferred settlement (`TenantAccountSeeder.cs:166-174`).
- `1110` is an Asset, `IsClearing=true`, with gateway metadata and an active Clearing Treasury mapping.
- Certified provider settlement debits Bank and credits the provider clearing account.
- Retired Visa endpoints explicitly redirect statement and balance reads to Treasury `1110` (`LegacyVisaStatementController.cs:9-49`).

Supplier Card funding must not be merged into `1110`; doing so would net outgoing supplier payments against incoming unsettled customer receipts and weaken settlement reconciliation.

## 21. Candidate Decision Table

| Candidate model | External fact required | Purchase posting | Later settlement | Treasury treatment | Decision |
| --- | --- | --- | --- | --- | --- |
| Debit card linked to Bank | Card is tied to an identified business bank account and charge is debited there | Dr purchase accounts / Cr identified Bank Asset | Reconcile against bank evidence; no card liability | Bank must be active same-tenant Bank Treasury authority | Valid only with bank/card evidence |
| Business credit card | Issuer extends credit; identified card/issuer and liability owner exist | Dr purchase accounts / Cr dedicated Credit Card Liability | Dr card liability / Cr identified Bank when issuer is paid | Liability is not a Treasury account | Valid only with issuer/credit evidence |
| Personal/employee/owner card | Named party paid personally and business owes reimbursement | Dr purchase accounts / Cr dedicated reimbursement payable | Dr payable / Cr Bank/Cash when reimbursed | Payable is not a Treasury account | Valid only with approved reimbursement evidence |
| Prepaid/custodial card Asset | Business owns a separately reconciled stored-value balance | Dr purchase accounts / Cr identified prepaid Asset | Top-ups and statement reconciliation use source-owned workflows | Map only if Treasury policy explicitly supports this account type | Valid only with custody and statement evidence |
| `1110 Visa Clearing` | Would require supplier outflow to be settlement of customer processor receivable | Dr purchase accounts / Cr incoming clearing | Nets unrelated inward/outward flows | Already Clearing | Rejected by current domain evidence |
| Map existing `1020` as Bank | Would require proof that `1020` represents a real bank account | No source-specific instrument | No bank records exist | Would manufacture Treasury authority | Rejected |
| Map existing `1020` as Clearing | Would require an external party owing settlement to the business | Current credits create negative receivable | No matching settlement lots/provider | Would duplicate `1110` and invert clearing semantics | Rejected |
| Keep `1020` as generic Card Asset | Assumes all incoming and outgoing Card activity belongs in one net pool | Continues negative Asset behavior | No authoritative reconciliation | Remains outside Treasury | Rejected for future postings |

No candidate can be selected from current repository or SQL evidence.

## 22. Why 1020 Must Not Be Reclassified In Place

Ledger account code and account type are immutable. `ApplicationDbContext` rejects modification of an Account tenant, code, or classification (`ApplicationDbContext.cs:7033-7058`). This is the correct safeguard.

Changing `1020` from Asset to Liability in place would also reinterpret all historical lines and report periods. Mapping it as Bank or Clearing would grant operational authority unsupported by evidence. Renaming it would not repair its accounting meaning.

The safe pattern is a new evidenced account/instrument for future authority plus a normal reclassification journal for historical balance.

## 23. Historical EGP 3.80 Treatment

No historical mutation is authorized now. The four purchase Card portions may not all share one real funding source.

After approved external evidence identifies each source, reclassify through dated balanced journal authority. For each evidenced allocation, the accounting shape is:

```text
Dr 1020 POS Visa Account                 evidenced amount
    Cr evidenced Bank / card Liability  evidenced amount
```

If different purchases used different instruments, split the reclassification by source. Attach or reference the approved bank statement, issuer statement, reimbursement evidence, or other source schedule. Do not:

- Edit existing journals or ledger lines.
- Change `BalanceAfter` values.
- Set `CurrentBalance` directly.
- Treat audit labels as financial evidence.
- Force the entire EGP 3.80 into one account for convenience.

## 24. Future Domain Authority Contract

Implementation should begin only after the business selects supported instrument types. The minimum safe contract is:

- A tenant-owned configured supplier funding instrument.
- An immutable instrument type such as BankDebitCard, CreditCard, ReimbursementCard, or approved equivalent.
- An exact linked ledger account.
- For BankDebitCard, an active same-tenant Bank Treasury mapping.
- For a credit/reimbursement model, an active same-tenant Liability of the approved class.
- A stable display identity that avoids storing prohibited full card data; issuer/name and masked identifier are sufficient where required.
- Active/inactive lifecycle and optional limits or reconciliation metadata defined by business policy.
- A required instrument reference on every supplier Card payment allocation.
- Frozen funding account/type snapshots or equivalent immutable resolution evidence at posting time.

`PaymentMethod.Card` should remain a tender category. It should not itself resolve an accounting account.

## 25. Posting, Reversal, And Replay Requirements

The implementation must satisfy these rules:

- Purchase create and standalone supplier payment fail closed when Card has no instrument authority.
- Posting credits the exact account frozen by the source payment allocation.
- Debit-card Bank outflows pass existing Treasury Bank amount/capacity authority.
- Liability-funded purchases do not pretend to reduce Treasury cash; paying the issuer is a separate Bank outflow against the liability.
- Reversal uses the original journal/account identity, not the instrument's current configuration.
- Purchase update stages a new version and cannot reinterpret old versions through a changed default.
- New outbox payloads carry an instrument/account authority version or an immutable source reference sufficient for exact reload.
- Legacy Card payloads without authority fail closed or enter an explicit migration/reconciliation lane.
- No fallback routes a missing instrument to `1020`, `1110`, or `1030`.
- Idempotency remains tenant/source/version bound.

The live outbox currently has no pending or failed Card purchase message, which lowers immediate migration risk but does not remove the need for a deterministic retained-message policy.

## 26. Provisioning, Reports, Controls, And Retirement

Implementation sequencing should be:

1. Obtain the signed business decision and historical evidence.
2. Add the selected funding-instrument/account model and tenant configuration.
3. Update purchase and standalone supplier payment contracts, UI, persistence, idempotency fingerprint, outbox, posting, and reversal behavior together.
4. Add a deployment gate for legacy Card payloads and existing `1020` balances.
5. Redirect future supplier Card postings only after authority tests pass.
6. Post approved historical reclassification journals.
7. Confirm every tenant has zero `1020` balance and no pending/replay path can post it.
8. Remove `1020` from new-tenant seeding and deactivate existing `1020` accounts for new entries.
9. Preserve historical account and General Ledger visibility.
10. Remove or replace the legacy FinancialAccount Visa adapter and dead frontend `VISA_ACCOUNT` constant.

Reporting must also stop classifying cash equivalents solely by `10xx`/`11xx` code prefix. Current Cash Flow includes `1020` because `IsCashEquivalent` uses those prefixes (`FinancialReportService.cs:790-792`), even though Treasury correctly excludes it. Cash-equivalent reporting needs explicit policy independent of this funding decision.

Required controls and tests include:

- Bank debit card credits the selected mapped Bank and enforces available funds.
- Credit card credits only the selected dedicated Liability.
- Reimbursement card credits only the approved payable.
- Missing, inactive, deleted, foreign-tenant, wrong-type, or unmapped Bank instruments are rejected.
- Customer Card still debits `1110` and provider settlement behavior is unchanged.
- Standalone supplier and purchase Card flows use the same instrument authority.
- Mixed and partial-credit allocations retain exact instrument identity.
- Mutation reversal/repost preserves original-account history and current-version authority.
- Legacy unversioned outbox payloads cannot post through a new default.
- No new production reference posts supplier Card to `1020` or `1110`.
- Historical `1020` journals remain readable after account deactivation.
- Trial Balance, Balance Sheet, Cash Flow, and Treasury boundaries remain reconciled.

## 27. Final Gate And Verdict

| Gate | Result | Evidence |
| --- | --- | --- |
| 1020 balance explained | PASS | Eight immutable lines; EGP 2.20 debits, EGP 6.00 credits, net -3.80 |
| Cache equals ledger | PASS | Both EGP -3.8000 |
| Source purchases identified | PASS | Four live purchase Card allocations total EGP 3.80 |
| Reversal chain complete | PASS | Two reversed purchase journals, two posted reversals, final repost |
| Pending/failed Card purchase outbox | PASS | 0 pending, 0 failed |
| Specific card instrument captured | FAIL | No instrument field in UI, API, entity, payment row, or outbox |
| Bank funding proved | FAIL | 0 shopixy Banks and 0 BankTransactions; no external bank reference |
| Credit-card liability proved | FAIL | No issuer/card-liability model or evidence |
| Reimbursement funding proved | FAIL | No payer/reimbursement evidence |
| 1110 valid for supplier Card | FAIL | It is incoming customer provider clearing |
| 1020 safe to map into Treasury | FAIL | No Bank/Clearing authority; semantics conflict with active model |
| Historical reclassification target known | FAIL | Bank versus Liability versus other funding is unresolved |
| Customer Card behavior can remain isolated | PASS | Existing `1110` collection and settlement lane is distinct |
| Implementation ready | **BLOCKED** | Business and external funding evidence are required |
| Financial data changed | NO | Discovery was read-only |
| Application code changed | NO | Report only |

Final T-06 classification:

- `1020` is a legacy generic Card Asset with valid historical ledger entries but no valid future supplier-funding authority.
- The negative balance is an accounting classification symptom, not missing journal history.
- Mapping `1020` to Treasury, redirecting supplier Card to `1110`, or assuming `1030` would each invent unsupported authority.
- Future supplier Card posting must resolve an explicit configured funding instrument.
- Existing EGP 3.80 must remain unchanged until external evidence authorizes one or more reclassification targets.

**Business funding-instrument decision required before implementation.**
