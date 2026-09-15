# Treasury T-06C Golden Real Business-Data Certification Rerun: AhramStore

Certification date: 2026-09-14  
Target tenant: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`)  
Execution mode: headed Microsoft Edge, live API, live PostgreSQL, read-only SQL reconciliation  
Final verdict: **FAIL**

## 1. Executive Verdict

**FAIL.** The first real supplier-card funding action was submitted once through rendered controls and returned HTTP `200`. Its funding document and balanced journal committed correctly: Dr `T06C-CARD-ASSET` EGP `100.00`, Cr `1010` EGP `100.00`. Card balance became `100.00` and Main Cashbox became `900.00`.

Immediate reconciliation found a mandatory outbox discrepancy. The AhramStore accounting outbox remained at one row, and that row is the pre-existing T-06D capitalization event. No T-06C `SupplierCardFunding` outbox event or outbox ID exists. Live Cash Flow also returned `reconciles=false`: closing cash was `900.00`, while reported net cash change remained `1,000.00`.

Execution stopped at that point. No replay, purchase, supplier payment, mixed purchase, partial-credit purchase, pure-credit purchase, edit/repost, purchase replay, or overspend request was sent. No repair or compensating mutation was attempted.

## 2. Tenant / Session

| Check | Evidence | Result |
| --- | --- | --- |
| Browser | Microsoft Edge `152`, headed, `1440x1000` | PASS |
| Tenant | `AhramStore` | PASS |
| Tenant ID | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | PASS |
| User ID | `1bb114d3-a9a3-4a9d-ace3-3690361637a8` | PASS |
| Role | `Owner` | PASS |
| Country / currency | `EG` / rendered `EGP` | PASS |
| API liveness | `GET http://localhost:5010/health/live` returned `200` | PASS |
| Web route | `GET http://localhost:5173/supplier-card-funding` returned `200` | PASS |

Authentication used the supplied normal AhramStore Owner login. No fabricated token or tenant override was used. Credentials, cookies, refresh tokens, CSRF values, and authorization headers are excluded or redacted from evidence.

## 3. Baseline

The baseline was captured under `REPEATABLE READ / READ ONLY / ROLLBACK` at `2026-09-13T22:16:54.3955208Z`. Its deterministic fingerprint was `C2BBC3292AC7C7D894111423538487B021D409A9F97349791279574D8AD3D3A3`, exactly matching the certified immediate post-T-06D fingerprint.

| State | Baseline |
| --- | ---: |
| `1010` cached / ledger | `1,000.00 / 1,000.00` |
| `1030` cached / ledger | `0.00 / 0.00` |
| `3010` credit-normal balance | `1,000.00` |
| `1020` balance / lines | `0.00 / 0` |
| `1110` balance / lines | `0.00 / 0` |
| Card funding asset | `0.00` |
| Supplier balance | `0.00` |
| Customers / aggregate balance | `0 / 0.00` |
| Purchases / purchase payments | `0 / 0` |
| Supplier payments / transactions | `0 / 0` |
| Supplier-card fundings | `0` |
| Capitalizations / allocations | `1 / 1` |
| Stock / reserved / damaged | `100.00 / 0.00 / 0.00` |
| Journals / ledger lines | `1 / 2` |
| Trial debits / credits | `1,000.00 / 1,000.00` |
| Accounting outbox total / pending / failed | `1 / 0 / 0` |

The existing supplier `T06C-SUPPLIER-20260913` had balance `0.00`. Instrument `T06C-CARD-20260913` was active at authority version `1`, linked to dedicated asset `T06C-CARD-ASSET`, and configured to source Main Cashbox `1010`. T-06D capitalization was not repeated.

## 4. Card Funding

One rendered-UI action was admitted. All other non-read API methods were blocked by the browser route guard.

| Field | Value |
| --- | --- |
| Reference | `T06C-FUND-001-RERUN` |
| Amount | EGP `100.00` |
| Funding ID | `534127ff-18b3-41ce-ac0a-0de146807f8e` |
| Journal ID | `01a09ce1-3c2a-7153-beb2-b58385b6a22d` |
| Journal number | `JE-2026-000002` |
| Idempotency key | `supplier-card-funding-cbb89b09-5deb-430a-87f8-a17c954c4ebe` |
| Request / trace identity | `supplier-card-funding-cbb89b09-5deb-430a-87f8-a17c954c4ebe` |
| HTTP result | `200` |
| Outbox ID | **None: blocking discrepancy** |

The funding row freezes instrument `cee6f007-0685-487e-b2b2-d977faff9d60`, version `1`, source Treasury `01a0828d-7502-71c0-b3d5-d0cd09fcbb42`, source ledger `1010`, card ledger `T06C-CARD-ASSET`, request hash `5B33D76F978A43DCA8B30BC673D2F537D809610749534B5EA137868ECFBFAD99`, and EGP `100.00`.

The browser/API and database agree on `1010: 1,000.00 -> 900.00` and card asset `0.00 -> 100.00`. Supplier, inventory, `3010`, `1030`, `1020`, and `1110` were unchanged.

## 5. Funding Replay

**NOT EXECUTED.** Immediate funding reconciliation failed the mandatory outbox invariant. Sending either the exact replay or changed-payload replay after that finding would have violated the stop rule. Idempotency behavior is therefore not certified by this rerun.

## 6. Card Statement

The read-only rendered Card Statement was opened after the stop condition to preserve the committed state. It displayed:

| Measure | Value |
| --- | ---: |
| Opening | `0.00` |
| Funding | `+100.00` |
| Purchases | `0.00` |
| Supplier payments | `0.00` |
| Closing | `100.00` |

Its sole row references funding `534127ff-18b3-41ce-ac0a-0de146807f8e` and journal `01a09ce1-3c2a-7153-beb2-b58385b6a22d`. The statement matches the immutable ledger for the stopped state.

## 7. Card Purchase

**NOT EXECUTED.** Certification stopped before the first purchase. There is no purchase, purchase payment, supplier delta, stock movement, purchase journal, or purchase outbox row to certify.

## 8. Purchase Statement

**NOT EXECUTED.** The statement contains no purchase outflow because no purchase was attempted after the stop condition.

## 9. Supplier Card Payment

**NOT EXECUTED.** Supplier payments and supplier transactions remained zero.

## 10. Mixed Purchase

**NOT EXECUTED.** No cash/card split request was sent. Cashbox remains `900.00`, reflecting only capitalization less card funding.

## 11. Partial Credit + Card

**NOT EXECUTED.** No partial-card or credit remainder was created.

## 12. Pure Credit

**NOT EXECUTED.** The pure-credit null-instrument regression was not exercised after the stop condition.

## 13. Purchase Edit/Repost

**NOT EXECUTED.** No eligible card purchase exists. Original-journal immutability, frozen-authority reversal, repost, and mutation-version behavior are not certified by this rerun.

## 14. Purchase Replay

**NOT EXECUTED.** No purchase request exists to replay. Duplicate invoice, payment, journal, and stock protections are not certified by this rerun.

## 15. Overspend

**NOT EXECUTED.** The required real-browser `available + 0.01` negative path was not attempted after the mandatory stop condition.

## 16. Final Card Statement

The only valid final statement for the stopped run is `+100.00 funding = 100.00 closing`. It agrees with the card ledger and API. The requested full purchase/payment/reversal sequence was not run and is not certified.

## 17. Treasury Reconciliation

| Account | Before | Stopped final | Delta | Source | Result |
| --- | ---: | ---: | ---: | --- | --- |
| `1010` Main Cashbox | `1,000.00` | `900.00` | `-100.00` | `T06C-FUND-001-RERUN` | PASS |
| Card Funding Asset | `0.00` | `100.00` | `+100.00` | `T06C-FUND-001-RERUN` | PASS |
| `1030` Bank | `0.00` | `0.00` | `0.00` | None | PASS |
| `3010` Opening Balance Equity | `1,000.00 Cr` | `1,000.00 Cr` | `0.00` | T-06D only | PASS |
| `1020` POS Visa | `0.00` | `0.00` | `0.00` | None | PASS |
| `1110` Visa Clearing | `0.00` | `0.00` | `0.00` | None | PASS |

Cached and ledger-derived balances agree. The funding transfer is the only T-06C Treasury delta.

## 18. Supplier Reconciliation

Supplier `T06C-SUPPLIER-20260913` remained at `0.00`; supplier transactions, purchase payable, supplier payments, credit remainder, and returns all remained zero. These values agree because execution stopped before supplier activity, but the requested supplier lifecycle is not certified.

## 19. Inventory Reconciliation

Aggregate stock remained `100.00`, reserved `0.00`, damaged `0.00`, with the same inventory hash `BFAC42CC7B6533F88E19C5D4A8E3C02FC15A55F21E8D77E59AB43D79AA3CE2C8`. No purchase or return stock movement was created. The full requested inventory scenario is not certified.

## 20. Journal Reconciliation

The sole T-06C journal is:

| Journal | Purpose | Debit | Credit | Status | Source |
| --- | --- | ---: | ---: | --- | --- |
| `01a09ce1-3c2a-7153-beb2-b58385b6a22d` / `JE-2026-000002` | Supplier card funding | `100.00` | `100.00` | Posted | Funding `534127ff-18b3-41ce-ac0a-0de146807f8e` |

| Line | Account | Debit | Credit | Balance after |
| ---: | --- | ---: | ---: | ---: |
| 1 | `T06C-CARD-ASSET` | `100.00` | `0.00` | `100.00` |
| 2 | `1010` | `0.00` | `100.00` | `900.00` |

Tenant Trial Balance totals became `1,100.00 / 1,100.00`, variance `0.00`. No failed or unbalanced journal exists. Supplier payable, inventory, and tax lines are absent because no purchase occurred.

## 21. Outbox Reconciliation

**FAIL.** Baseline and stopped-final AccountingOutbox state are both total `1`, pending `0`, failed `0`, with unchanged hash `B205FF2FF0B175F214AFC4F73A2555B19FF561AA8366597F20FC0362FED82B0A`.

The only row is T-06D outbox `d08f1dbd-fbd6-46d7-820e-5f749b0a7ce2`, source/event `TreasuryInitialCapitalization`, linked to journal `01a09cc3-fe61-749a-b7b1-5746d4e353ba`. There is no row for funding ID `534127ff-18b3-41ce-ac0a-0de146807f8e`, journal `01a09ce1-3c2a-7153-beb2-b58385b6a22d`, reference `T06C-FUND-001-RERUN`, or source type `SupplierCardFunding`.

Consequently, T-06C cannot satisfy processed outbox lineage, no-duplicate source-event verification, or the requested outbox ID capture. No outbox row was manually inserted.

## 22. Cross-Tenant Safety

Read-only fingerprints were captured before and after the AhramStore action:

| Tenant | Before | Final | Result |
| --- | --- | --- | --- |
| Shopixy | `7ACEF5ED3DA0EBBC8F0EC674C08F743F300AF941157A215CA12CA9932BEC3EFE` | same | PASS |
| GreatOne | `96FFD97DC1027F68BF23ABA6027D5976C316526E0DB490AF81409A1B696536AC` | same | PASS |

Their journal, ledger-line, outbox, Treasury, supplier, purchase, and inventory metrics and hashes also matched. AhramStore was the only intentionally mutated tenant.

## 23. Browser Certification

Headed Edge at `1440x1000`, English/LTR, verified the authenticated funding workspace, active card, rendered `EGP 0.00` baseline, completed funding form, HTTP `200` result, rendered `EGP 100.00` card state, and Card Statement. There were no page exceptions, blocked unexpected writes, console errors, or unexpected network failures in the successful captures.

Evidence screenshots:

- `verify-desktop-en.png`
- `fund-before-submit.png`
- `fund-after-submit.png`
- `statement-desktop-en.png`

Arabic, RTL, `390x844` mobile, and downstream funding/purchase/payment/edit/replay/overspend screens were not certified after the stop condition.

## 24. Reporting

Live authenticated report results in the stopped state:

| Report | Result |
| --- | --- |
| Trial Balance | `1,100.00` debits = `1,100.00` credits; balanced | 
| Balance Sheet | Assets `1,000.00` = liabilities `0.00` + equity `1,000.00`; balanced |
| P&L | Revenue, COGS, expenses, and net profit all `0.00` |
| Cash Flow | Closing cash `900.00`; financing cash flow/net cash change `1,000.00`; `reconciles=false` |
| Treasury summary | Current balance `900.00` |
| Card Statement | Opening `0.00`, inflow `100.00`, closing `100.00`; ledger match |

**Cash Flow is a second blocking financial discrepancy.** It does not account for the EGP `100.00` transfer from cash into the supplier-card asset when reconciling reported closing cash to net cash change.

## 25. Final Database Fingerprint

Stopped-final AhramStore fingerprint: `394AD2C1711D8A3A882FC1E86DE642CD684C3424B67B239CDFCABDAF8E5CCA0D`. It exactly matches the immediate post-funding capture, proving no later hidden AhramStore mutation.

| Entity | Before | After | Intended Delta | Actual Delta | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| `1010` | `1,000.00` | `900.00` | `-100.00` | `-100.00` | PASS |
| `1030` | `0.00` | `0.00` | `0.00` | `0.00` | PASS |
| `3010` | `1,000.00 Cr` | `1,000.00 Cr` | `0.00` | `0.00` | PASS |
| `1020` | `0.00` | `0.00` | `0.00` | `0.00` | PASS |
| `1110` | `0.00` | `0.00` | `0.00` | `0.00` | PASS |
| Card Funding Asset | `0.00` | `100.00` | `+100.00` | `+100.00` | PASS |
| Supplier balance | `0.00` | `0.00` | `0.00` | `0.00` | PASS |
| Purchases | `0` | `0` | `0` at stop | `0` | PASS |
| PurchasePayments | `0` | `0` | `0` at stop | `0` | PASS |
| SupplierPayments | `0` | `0` | `0` at stop | `0` | PASS |
| Stock | `100.00` | `100.00` | `0.00` at stop | `0.00` | PASS |
| SupplierCardFundings | `0` | `1` | `+1` | `+1` | PASS |
| Journals | `1` | `2` | `+1` | `+1` | PASS |
| LedgerLines | `2` | `4` | `+2` | `+2` | PASS |
| Outbox | `1` | `1` | `+1` | `0` | **FAIL** |

Evidence is retained under `C:\Users\fadya\AppData\Local\Temp\opencode\t06c-rerun-evidence\`. Database captures use read-only repeatable-read transactions and rollback. No SQL business write was performed.

## 26. Final Verdict

**FINAL VERDICT: FAIL.** Real browser funding and its balanced journal succeeded, the funding/Card/Cashbox balances reconcile, the Card Statement matches the ledger, cross-tenant fingerprints are unchanged, and protected accounts `1020` and `1110` are unchanged. However, the mandatory funding outbox event is absent and live Cash Flow reports `reconciles=false`.

The certification stopped immediately after those discrepancies. All later T-06C scenarios are **NOT EXECUTED / NOT CERTIFIED**. No repair was made while certification was running.

`T-06 COMPLETE` is not declared.
