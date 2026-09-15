# Treasury T-06C Golden Real Business-Data Certification: AhramStore Rerun 2

Certification date: 2026-09-14  
Target tenant: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`)  
Execution mode: headed Microsoft Edge, rendered product controls, live API, live PostgreSQL read-only reconciliation  
Currency: EGP  
Final verdict: **FAIL**

## 1. Summary & scope

This rerun started from the verified post-funding state: Main Cashbox `1010` at EGP `900.00`, dedicated supplier-card asset `T06C-CARD-ASSET` at EGP `100.00`, and Opening Balance Equity `3010` at EGP `1,000.00 Cr`. No capitalization or funding was repeated.

Exactly one new business mutation was submitted through rendered UI controls: an EGP `20.00` Card purchase. The purchase, payment allocation, supplier ledger, inventory movement, balanced accounting journal, processed purchase outbox, and Card Statement all reconciled. Immediate live Cash Flow did not reconcile: it reported closing cash `900.00` but net cash change `880.00` after applying an erroneous EGP `-20.00` inventory working-capital cash adjustment to a purchase settled from the non-cash-equivalent supplier-card asset.

The mandatory stop rule was applied immediately. The Supplier Card payment, mixed purchase, partial-credit + Card purchase, pure-credit purchase, edit/reversal/repost, replay/conflict, overspend, funding replay, filter matrix, full-success state, and browser/language/responsive matrix were not executed. No source remediation, SQL business write, cached-balance patch, compensating journal, repeated funding, repeated capitalization, or historical outbox fabrication was performed.

## 2. Preconditions and exact opening-state proof

The authoritative pre-purchase database capture was taken at `2026-09-14T11:19:47.8479109Z` under `REPEATABLE READ / READ ONLY / ROLLBACK`.

| Opening state | Exact value | Result |
| --- | ---: | --- |
| Main Cashbox `1010`, cached / ledger | `900.00 / 900.00` | PASS |
| Supplier-card asset, cached / ledger | `100.00 / 100.00` | PASS |
| Opening Balance Equity `3010` | `1,000.00 Cr` | PASS |
| Bank `1030`, cached / ledger | `0.00 / 0.00` | PASS |
| Protected `1020`, balance / lines | `0.00 / 0` | PASS |
| Protected `1110`, balance / lines | `0.00 / 0` | PASS |
| Supplier balance | `0.00` | PASS |
| Purchases / purchase payments | `0 / 0` | PASS |
| Supplier payments / supplier transactions | `0 / 0` | PASS |
| Aggregate stock / reserved / damaged | `100.00 / 0.00 / 0.00` | PASS |
| Selected variant stock | `10.00` | PASS |
| Journals / ledger lines | `2 / 4` | PASS |
| Trial Balance debits / credits | `1,100.00 / 1,100.00` | PASS |
| Accounting outbox total / pending / failed | `1 / 0 / 0` | PASS |

The active supplier was `T06C-SUPPLIER-20260913` (`1008e591-9a9f-4ea3-9ac6-2386c0456806`). The active funded-company-card instrument was `T06C-CARD-20260913` (`cee6f007-0685-487e-b2b2-d977faff9d60`), authority version `1`, linked to dedicated ledger account `4db74e2c-97e3-4a6a-bd98-d4e4c99ddfff` and source Treasury `01a0828d-7502-71c0-b3d5-d0cd09fcbb42`.

The opening Card Statement covered `2026-09-01` through `2026-09-15`: opening `0.00`, inflows `100.00`, outflows `0.00`, closing `100.00`. Live opening reports were also coherent: Trial Balance and Balance Sheet were balanced, P&L was zero, and Cash Flow had closing cash and net cash change both `900.00` with `reconciles=true`.

## 3. Browser/auth/role/API-health verification

| Check | Evidence | Result |
| --- | --- | --- |
| Browser | Headed Microsoft Edge `152`, Windows | PASS |
| Desktop viewport | `1440x1000` | PASS |
| Language/direction | `en` / `ltr` | PASS |
| Tenant | `AhramStore` | PASS |
| Tenant ID | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | PASS |
| User ID | `1bb114d3-a9a3-4a9d-ace3-3690361637a8` | PASS |
| Role | `Owner` | PASS |
| Country/currency | `EG` / rendered `EGP` | PASS |
| API liveness | `GET http://localhost:5010/health/live` returned `200` | PASS |
| Web route | `GET http://localhost:5173/supplier-card-funding` returned `200` | PASS |
| Required pages | Supplier Card Funding, Purchase Invoice, Supplier Payment, Card Statement rendered | PASS |

Authentication used the supplied normal Owner session. No fabricated token or tenant override was used. Credential values, cookies, CSRF values, and authorization headers were excluded or redacted from evidence. The mutation capture has no console errors, page errors, or unexpected blocked mutation.

## 4. Fresh DB baselines and fingerprints

Fresh tenant captures were taken before the mutation under read-only repeatable-read transactions:

| Tenant | Tenant ID | Baseline fingerprint |
| --- | --- | --- |
| AhramStore | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | `4899E21764D9C448EB1A7313177260093854B361A2B46F6F6FEC76EAB652ECC2` |
| Shopixy | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | `1D1AC2FF6545EEC89EF587CEA1ECF83646AD69D53752809C4589055AB5274E3C` |
| GreatOne | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | `B996685158542D7FF0FE1EA5194AF3AF7E55E597D22149AC9826921C4ACD390B` |

The focused immediate pre-purchase AhramStore capture reproduced the same counts, balances, and hashes as the fresh AhramStore baseline. Its one AccountingOutbox row was the legitimate processed T-06D capitalization event `d08f1dbd-fbd6-46d7-820e-5f749b0a7ce2`.

Historical funding `534127ff-18b3-41ce-ac0a-0de146807f8e`, journal `01a09ce1-3c2a-7153-beb2-b58385b6a22d`, and reference `T06C-FUND-001-RERUN` remained immutable. They predate the outbox remediation and still have no historical funding outbox row. This is documented as immutable legacy evidence; no row was fabricated, backfilled, or repaired.

## 5. New Card purchase creation & reconciliation

One normal rendered Purchase Invoice submission was made at desktop size. The browser generated one POST to `/api/purchases`; the write guard admitted that mutation and no other mutation.

| Field | Captured value |
| --- | --- |
| Supplier | `T06C-SUPPLIER-20260913` |
| Warehouse | `Main Warehouse` (`2bca8d91-2c09-450e-a713-e783b9cf3d7a`) |
| Product | `iPhone 17 Pro Max` (`cc20abdf-0a4f-0278-3c59-c4f10d17bc96`) |
| Variant | `IPHONE-17-PRO-MAX-BLUE-256` (`60f8b79a-42a0-4f44-8271-507054044ba9`) |
| Quantity / unit cost / tax | `1 / 20.00 / 0%` |
| Payment method | `Card` |
| Funding instrument/version | `cee6f007-0685-487e-b2b2-d977faff9d60 / 1` |
| Browser idempotency key | `purchase-create-1789384806134-ttn5bmc0` |
| Payment attempt ID | `purchase-create-attempt-1789384806134-mv4ep5fl` |
| HTTP result | `200` |
| Invoice ID | `17adfeb8-f5b8-4ce1-b45f-64583b2b53b1` |
| Invoice number | `PUR-20260914-1010` |
| Invoice mutation version | `0` |
| Purchase payment ID | `1bea198b-3755-4616-8411-56490298a022` |
| Purchase journal ID | `01a09fa5-536b-7eed-b166-47dc12ffce28` |
| Purchase outbox ID | `01a09fa5-520a-747e-be07-6e5bd941a3e7` |

The invoice is `Completed`, total `20.00`, paid `20.00`, remaining `0.00`, with exactly one item and one Card payment allocation. The allocation freezes the instrument ID, instrument authority version `1`, and dedicated funding ledger account.

Immediate API and PostgreSQL reconciliation proved:

| Invariant | Before | After | Expected delta | Result |
| --- | ---: | ---: | ---: | --- |
| Card asset | `100.00` | `80.00` | `-20.00` | PASS |
| Main Cashbox `1010` | `900.00` | `900.00` | `0.00` | PASS |
| Inventory account `1310` | `0.00` | `20.00` | `+20.00` | PASS |
| Selected variant stock | `10.00` | `11.00` | `+1.00` | PASS |
| Aggregate stock | `100.00` | `101.00` | `+1.00` | PASS |
| Supplier balance | `0.00` | `0.00` | `0.00` | PASS |
| Purchases / allocations | `0 / 0` | `1 / 1` | `+1 / +1` | PASS |
| Journals / ledger lines | `2 / 4` | `3 / 6` | `+1 / +2` | PASS |
| Accounting outbox | `1` | `2` | `+1` | PASS |
| Pending / failed outbox | `0 / 0` | `0 / 0` | `0 / 0` | PASS |

The purchase journal `JE-2026-000003` is posted and balanced: Dr Inventory `1310` EGP `20.00`; Cr `T06C-CARD-ASSET` EGP `20.00`. The outbox is `PosPurchase` / `PurchaseInvoice`, has business entity `17adfeb8-f5b8-4ce1-b45f-64583b2b53b1`, aggregate version `0`, correlation ID `purchase-create-1789384806134-ttn5bmc0`, processed journal linkage `01a09fa5-536b-7eed-b166-47dc12ffce28`, attempt count `0`, no error, and no duplicate source event.

The purchase request idempotency row is `COMPLETED`, status `200`, and links the single request key to the single invoice. Supplier subledger and account-ledger evidence each contain the invoice credit `20.00` followed by the Card settlement debit `20.00`, ending at zero.

## 6. Card Statement reconciliation after purchase

The read-only Card Statement API and the loaded rendered dialog agree:

| Statement measure | Value |
| --- | ---: |
| Opening Balance | `0.00` |
| Funding | `100.00` |
| Supplier Purchases | `20.00` |
| Supplier Payments | `0.00` |
| Returns | `0.00` |
| Reversals | `0.00` |
| Closing Balance | `80.00` |

The two rendered rows are:

| Date | Type | Reference | Source document | Journal | Debit | Credit | Running balance |
| --- | --- | --- | --- | --- | ---: | ---: | ---: |
| 2026-09-13 | Funding | `T06C-FUND-001-RERUN` | `534127ff-18b3-41ce-ac0a-0de146807f8e` | `01a09ce1-3c2a-7153-beb2-b58385b6a22d` | `100.00` | `0.00` | `100.00` |
| 2026-09-14 | Supplier Purchase | `PUR-20260914-1010` | `17adfeb8-f5b8-4ce1-b45f-64583b2b53b1` | `01a09fa5-536b-7eed-b166-47dc12ffce28` | `0.00` | `20.00` | `80.00` |

The statement, dedicated asset cached balance, ledger-derived balance, purchase allocation, and posted journal all equal EGP `80.00` final available balance. Result: **PASS** for the reached state.

## 7. New Supplier Card payment creation & reconciliation

**NOT EXECUTED / NOT CERTIFIED.** The mandatory Cash Flow invariant failed immediately after the first Card purchase. No Supplier Card payment POST was sent, so there is no payment ID, payment journal ID, outbox ID, supplier-payment row, card balance delta, or supplier balance delta to report.

## 8. Statement reconciliation after supplier payment

**NOT EXECUTED / NOT CERTIFIED.** Because section 7 was stopped, the statement has no Supplier Payment row. The valid stopped-state statement remains funding `+100.00`, purchase `-20.00`, closing `80.00`.

## 9. Mixed purchase & reconciliation

**NOT EXECUTED / NOT CERTIFIED.** No EGP `30.00` mixed Cash `10.00` / Card `20.00` purchase was submitted after the stop condition. Mixed allocation equality, source balances, journal splits, supplier treatment, outbox, and inventory effects were not tested.

## 10. Partial-credit + Card purchase

**NOT EXECUTED / NOT CERTIFIED.** No EGP `25.00` purchase with Card `10.00` and credit remainder `15.00` was submitted. Supplier-payable and partial-payment invariants were not tested.

## 11. Pure-credit purchase

**NOT EXECUTED / NOT CERTIFIED.** No EGP `15.00` pure-credit purchase was submitted. The null-instrument path, supplier payable, Card/Cashbox non-movement, journal, outbox, and inventory effects were not tested.

## 12. Purchase edit/reversal/repost

**NOT EXECUTED / NOT CERTIFIED.** The reached Card purchase was not edited. No reversal or repost was requested, the original journal remained immutable, and no mutation-version increment or replacement journal/outbox lineage was created.

## 13. Same-key same-payload replay

**NOT EXECUTED / NOT CERTIFIED.** The original browser request was sent once. Its idempotency row is complete and unique, but the same key and payload were not replayed after the financial stop condition. Replay response equality and no-duplicate invoice/payment/journal/stock behavior are therefore not certified by this rerun.

## 14. Same-key changed-payload conflict

**NOT EXECUTED / NOT CERTIFIED.** No changed-payload request was sent with `purchase-create-1789384806134-ttn5bmc0`. Conflict status and zero-delta behavior were not tested in this live rerun.

## 15. Card overspend rejection

**NOT EXECUTED / NOT CERTIFIED.** The stopped available balance is EGP `80.00`, but no EGP `80.01` mutation was attempted. Safe rejection and no-delta invariants were not tested after the mandatory stop.

## 16. Funding replay

**NOT EXECUTED / NOT CERTIFIED.** Historical funding was not replayed, changed, backfilled, or repaired. Its immutable row and balanced journal remained unchanged, and no new funding row or funding outbox row was created.

## 17. Statement filter coverage

**PARTIAL ONLY.** The inclusive `2026-09-01` through `2026-09-15` range was exercised and rendered both expected rows in chronological order. Boundary exclusion, narrow day ranges, empty ranges, invalid ranges, reset behavior, and post-payment/reversal combinations were not run because certification had stopped.

## 18. Running balance check

The reached Card Statement passes its running-balance arithmetic:

| Step | Arithmetic | Running balance | Result |
| --- | --- | ---: | --- |
| Opening | `0.00` | `0.00` | PASS |
| Funding | `0.00 + 100.00` | `100.00` | PASS |
| Card purchase | `100.00 - 20.00` | `80.00` | PASS |
| Closing | `100.00 - 20.00` | `80.00` | PASS |

This check certifies only the stopped two-row sequence; the planned Supplier Card payment, mixed purchase, edit reversal, and repost rows do not exist.

## 19. Full success scenario

**NOT REACHED / FAIL.** The intended full scenario required all happy-path events, replay/conflict checks, overspend rejection, statement filters, browser matrix, final balance formulas, and all reports to pass. Execution stopped after the first purchase because live Cash Flow returned `reconciles=false`.

The valid committed stopped state consists of the pre-existing capitalization, immutable historical card funding, and one new EGP `20.00` Card purchase. It is not a full-success certification state and `T-06 COMPLETE` is not declared.

## 20. Post-full-success DB reconciliation & reports

There is no post-full-success state. Read-only reconciliation was instead captured immediately after the mandatory stop:

| Report/state | Stopped-state result |
| --- | --- |
| Trial Balance | Debits `1,120.00` = credits `1,120.00`; variance `0.00`; balanced |
| Balance Sheet | Assets `1,000.00` = liabilities `0.00` + equity `1,000.00`; variance `0.00`; balanced |
| P&L | Revenue, COGS, expenses, and net profit all `0.00` |
| Treasury summary | Current balance `900.00` |
| Supplier | Invoiced `20.00`, paid `20.00`, balance `0.00` |
| Card Statement | Inflows `100.00`, outflows `20.00`, closing `80.00` |
| Outbox | Total `2`, pending `0`, failed `0`; purchase event processed |
| Cash Flow | Opening cash `0.00`, closing cash `900.00`, net cash change `880.00`, `reconciles=false` |

**Blocking discrepancy:** Cash Flow reports net profit `0.00`, Change in Inventory `-20.00`, operating cash flow `-20.00`, supplier-card funding investing cash flow `-100.00`, owner-equity financing cash flow `+1,000.00`, and net cash change `880.00`. Ledger closing cash is `900.00`, so the report is short by EGP `20.00`.

The observed implementation computes every `13xx` balance increase as a cash outflow in `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs:536-545`, while cash equivalents are restricted to `10xx` and `11xx` codes at `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs:882-887`. The purchase moved value from custom non-cash-equivalent card asset `T06C-CARD-ASSET` to Inventory and did not move `10xx`/`11xx` cash. Applying the inventory adjustment without a non-cash settlement offset causes the live mismatch. This is diagnosis only; no source was changed.

## 21. Browser matrix / language / responsive results

| Browser check | Result |
| --- | --- |
| Headed Edge desktop `1440x1000`, English/LTR | PASS for funding, purchase, payment, and statement page rendering |
| Rendered Card purchase submission | PASS |
| Rendered loaded Card Statement after purchase | PASS |
| Browser console errors / page errors | `0 / 0` |
| Arabic/RTL | NOT EXECUTED |
| Mobile `390x844` | NOT EXECUTED |
| Additional browser engines | NOT EXECUTED |
| Downstream payment/mixed/edit/replay/overspend screens | NOT EXECUTED |

The rendered post-stop Card Statement visibly showed EGP `0.00` opening, `100.00` funding, `20.00` Supplier Purchases, and `80.00` closing. The Supplier Payment selector visibly showed `T06C-CARD-20260913` with EGP `80.00` available, but no payment mutation was submitted.

## 22. Negative-path and mutation-budget summary

The live mutation budget was one successful rendered purchase POST. No second mutation was allowed after the Cash Flow failure.

| Mutation/path | Count/status |
| --- | --- |
| New Card purchase | `1` submitted, `1` committed |
| Capitalization | `0` new |
| Card funding | `0` new |
| Supplier Card payment | `0` |
| Mixed purchase | `0` |
| Partial-credit + Card | `0` |
| Pure credit | `0` |
| Edit/reversal/repost | `0` |
| Same-payload replay | `0` |
| Changed-payload conflict | `0` |
| Overspend | `0` |
| Direct SQL business writes | `0` |
| Balance patches/compensating journals | `0` |
| Historical outbox fabrication | `0` |

Negative-path outcomes are not inferred from source or prior runs; they remain not certified in this rerun. The stop rule took precedence over completing the remaining test matrix.

## 23. Protected-account proof

Protected and unrelated Treasury accounts remained unchanged from fresh baseline through stopped-final capture:

| Account | Before cached / ledger / lines | Final cached / ledger / lines | Result |
| --- | --- | --- | --- |
| `1020` POS Visa Account | `0.00 / 0.00 / 0` | `0.00 / 0.00 / 0` | PASS |
| `1110` Visa Clearing | `0.00 / 0.00 / 0` | `0.00 / 0.00 / 0` | PASS |
| `1030` Bank Account | `0.00 / 0.00` | `0.00 / 0.00` | PASS |
| `1120` Stripe Clearing | `0.00 / 0.00` | `0.00 / 0.00` | PASS |

The deterministic `ProtectedAccounts` hash stayed `A3D753604F8B6AA52B85A8471D5C0D1FFD8CBF18271B1CA961975D7F77B8E36F`. The EGP `20.00` purchase credited only the dedicated supplier-card asset and did not touch POS Visa or Visa Clearing.

## 24. Final tenant fingerprints and cross-tenant safety

| Tenant | Baseline fingerprint | Stopped-final fingerprint | Expected | Result |
| --- | --- | --- | --- | --- |
| AhramStore | `4899E21764D9C448EB1A7313177260093854B361A2B46F6F6FEC76EAB652ECC2` | `AC77359B6C6EE366B939E0DD65CC0F1091C6CCA0250757EA915CF1DB25266A01` | Change only for one purchase | PASS |
| Shopixy | `1D1AC2FF6545EEC89EF587CEA1ECF83646AD69D53752809C4589055AB5274E3C` | `1D1AC2FF6545EEC89EF587CEA1ECF83646AD69D53752809C4589055AB5274E3C` | Unchanged | PASS |
| GreatOne | `B996685158542D7FF0FE1EA5194AF3AF7E55E597D22149AC9826921C4ACD390B` | `B996685158542D7FF0FE1EA5194AF3AF7E55E597D22149AC9826921C4ACD390B` | Unchanged | PASS |

AhramStore's final fingerprint exactly matches its immediate post-purchase capture. This proves no hidden later AhramStore mutation occurred while the read-only stopped-state statement and report evidence were collected. Shopixy and GreatOne metrics and hashes remained identical across accounting outbox, journals, ledger lines, Treasury, suppliers, purchases, inventory, products, protected accounts, and idempotency records.

Evidence is retained under `C:\Users\fadya\AppData\Local\Temp\opencode\t06c-rerun2-evidence\`. Primary artifacts are `purchase-card.json`, `db-before-card-purchase.json`, `db-after-card-purchase.json`, `db-stopped-final-ahramstore.json`, `db-stopped-final-shopixy.json`, `db-stopped-final-greatone.json`, `statement-post-stop.json`, and their rendered screenshots. Database captures declare `REPEATABLE READ / READ ONLY / ROLLBACK`.

## 25. Final PASS/FAIL matrix

| Required certification area | Result | Evidence/result summary |
| --- | --- | --- |
| Exact opening state | PASS | `1010=900.00`, Card `100.00`, `3010=1,000.00 Cr` |
| Browser/auth/role/API health | PASS | Headed Edge, Owner, AhramStore, EG/EGP, health `200` |
| Fresh tenant baselines | PASS | Three read-only deterministic captures |
| Card purchase | PASS | One EGP `20.00` completed purchase |
| Card purchase allocations | PASS | Exactly one Card allocation, frozen authority version `1` |
| Card purchase journal | PASS | Dr `1310` `20.00`; Cr dedicated card asset `20.00` |
| Card purchase outbox | PASS | One processed `PosPurchase`, no pending/failed/duplicate |
| Supplier reconciliation | PASS for reached state | `20.00` invoiced, `20.00` paid, balance `0.00` |
| Inventory reconciliation | PASS for reached state | Selected variant `10 -> 11`; aggregate `100 -> 101` |
| Card Statement after purchase | PASS | `0 + 100 - 20 = 80` |
| Trial Balance | PASS | `1,120.00 = 1,120.00` |
| Balance Sheet | PASS | `1,000.00 = 0.00 + 1,000.00` |
| P&L | PASS | Net profit `0.00` |
| Cash Flow | **FAIL** | Closing `900.00`, net cash change `880.00`, `reconciles=false` |
| Supplier Card payment | NOT EXECUTED | Stopped after Cash Flow failure |
| Mixed / partial-credit / pure-credit purchases | NOT EXECUTED | Stopped after Cash Flow failure |
| Edit/reversal/repost | NOT EXECUTED | Stopped after Cash Flow failure |
| Replay / conflict / overspend / funding replay | NOT EXECUTED | Stopped after Cash Flow failure |
| Full statement filter coverage | NOT EXECUTED | Only inclusive full range certified |
| Browser language/responsive matrix | NOT EXECUTED | Desktop English/LTR only |
| Protected accounts | PASS | `1020`, `1110`, `1030`, `1120` unchanged |
| Cross-tenant safety | PASS | Shopixy and GreatOne fingerprints unchanged |
| Historical funding boundary | PASS | Legacy orphan untouched; no fabricated lineage |
| Full T-06C certification | **FAIL** | Mandatory financial invariant failed; later scope stopped |

## 26. Final verdict and blocking issues

**FINAL VERDICT: FAIL.** The real browser Card purchase is correctly committed and reconciles across business rows, authority snapshots, supplier ledger, inventory, journal, processed outbox, dedicated card asset, Card Statement, Trial Balance, Balance Sheet, and P&L. Protected accounts and both non-target tenants remained unchanged.

The blocking issue is live Cash Flow. It reports closing cash EGP `900.00` but net cash change EGP `880.00` and `reconciles=false`, because the EGP `20.00` inventory increase is treated as an operating cash outflow even though this purchase was settled by reducing the non-cash-equivalent supplier-card asset from EGP `100.00` to EGP `80.00` without changing cash.

Certification stopped immediately after that failed financial invariant. All subsequent required scenarios remain **NOT EXECUTED / NOT CERTIFIED**. The historical funding outbox orphan also remains an immutable historical boundary and was not altered. No source remediation was attempted during certification, and `T-06 COMPLETE` is not declared.
