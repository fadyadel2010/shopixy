# Treasury T-06C Golden Real Business-Data Certification: AhramStore Rerun 3

Certification date: 2026-09-14  
Target tenant: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`)  
Execution mode: headed Microsoft Edge 152, rendered product controls, live API, live PostgreSQL read-only reconciliation  
Currency: EGP  
Final verdict: **PASS**

## 1. Summary & scope

Rerun 3 completed the full T-06C scenario from the verified post-Card-purchase state. It exercised one Supplier Card payment, mixed and credit purchase paths, purchase edit/reversal/repost, purchase and supplier-payment replay/conflict behavior, Card overspend rejection, historical funding replay, Card Statement filters, financial reports, protected accounts, cross-tenant isolation, and the desktop/mobile English/Arabic browser matrix.

The final state reconciles at Main Cashbox `890.00`, supplier-card asset `35.00`, aggregate stock `104.00`, and supplier balance EGP `5.00 Credit`. Trial Balance is `1,240.00 / 1,240.00`; Cash Flow reports closing cash and net cash change both `890.00` with `reconciles=true`; all seven outbox rows are processed. No SQL business write, balance patch, compensating journal, repeated capitalization, duplicate funding, source edit, or historical outbox fabrication was performed.

## 2. Preconditions and exact opening-state proof

The authoritative opening capture was taken at `2026-09-14T12:06:41.7157011Z` under `REPEATABLE READ / READ ONLY / ROLLBACK`.

| Opening state | Exact value | Result |
| --- | ---: | --- |
| Main Cashbox `1010`, cached / ledger | `900.00 / 900.00` | PASS |
| Supplier-card asset, cached / ledger | `80.00 / 80.00` | PASS |
| Supplier balance | `0.00` | PASS |
| Purchases / purchase payments | `1 / 1` | PASS |
| Aggregate stock / reserved / damaged | `101.00 / 0.00 / 0.00` | PASS |
| Selected variant stock | `11.00` | PASS |
| Journals / ledger lines | `3 / 6` | PASS |
| Trial Balance debits / credits | `1,120.00 / 1,120.00` | PASS |
| Accounting outbox total / pending / failed | `2 / 0 / 0` | PASS |

The active supplier was `T06C-SUPPLIER-20260913` (`1008e591-9a9f-4ea3-9ac6-2386c0456806`). The funded-company-card instrument was `T06C-CARD-20260913` (`cee6f007-0685-487e-b2b2-d977faff9d60`), authority version `1`, linked to dedicated account `T06C-CARD-ASSET` (`4db74e2c-97e3-4a6a-bd98-d4e4c99ddfff`) and Main Cashbox source `01a0828d-7502-71c0-b3d5-d0cd09fcbb42`.

## 3. Browser/auth/role/API-health verification

| Check | Evidence | Result |
| --- | --- | --- |
| Browser | Headed Microsoft Edge `152`, Windows | PASS |
| Desktop viewport | `1440x1000` | PASS |
| Mobile viewport | `390x844` | PASS |
| Tenant / user / role | `AhramStore` / `1bb114d3-a9a3-4a9d-ace3-3690361637a8` / `Owner` | PASS |
| Country / currency | `EG` / rendered `EGP` | PASS |
| Required routes | Supplier Card Funding, Purchase Invoice, Payments, and Card Statement rendered | PASS |
| Live API | Required reads and successful writes returned expected `2xx`; negative cases returned expected `4xx` | PASS |

Authentication used the normal Owner session. Cookies and CSRF values are redacted in retained artifacts. Mutation captures show no page errors or blocked mutations.

## 4. Fresh DB baselines and fingerprints

| Tenant | Tenant ID | Baseline fingerprint |
| --- | --- | --- |
| AhramStore | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | `AC77359B6C6EE366B939E0DD65CC0F1091C6CCA0250757EA915CF1DB25266A01` |
| Shopixy | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | `1D1AC2FF6545EEC89EF587CEA1ECF83646AD69D53752809C4589055AB5274E3C` |
| GreatOne | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | `B996685158542D7FF0FE1EA5194AF3AF7E55E597D22149AC9826921C4ACD390B` |

All captures declare `REPEATABLE READ / READ ONLY / ROLLBACK`. The AhramStore baseline contains the processed capitalization and original Card-purchase outbox rows. Historical funding predates funding-outbox remediation and is treated as the immutable legacy boundary described in section 16.

## 5. New Card purchase creation & reconciliation

The original Card purchase already present at the Rerun 3 opening was reverified rather than recreated:

| Field | Value |
| --- | --- |
| Invoice | `PUR-20260914-1010` / `17adfeb8-f5b8-4ce1-b45f-64583b2b53b1` |
| Original amount / version | `20.00 / 0` |
| Payment method | Card, instrument version `1` |
| Original request key | `purchase-create-1789384806134-ttn5bmc0` |
| Original journal | `JE-2026-000003` / `01a09fa5-536b-7eed-b166-47dc12ffce28` |
| Original outbox | `01a09fa5-520a-747e-be07-6e5bd941a3e7`, processed |

The opening database proves the original EGP `20.00` purchase, one frozen Card allocation, stock `+1`, Dr Inventory `20.00`, Cr dedicated supplier-card asset `20.00`, and Card balance `100.00 -> 80.00`. Its outbox correlation ID, business entity, aggregate version `0`, journal linkage, and completed request-idempotency row are unique. Result: **PASS**.

## 6. Card Statement reconciliation after purchase

The carried-forward opening statement sequence is Funding `+100.00`, original Card purchase `-20.00`, and closing `80.00`. The statement source document, reference, and journal IDs match the funding and invoice database rows, and the `80.00` closing balance equals both cached and ledger-derived card balances. Result: **PASS**.

## 7. New Supplier Card payment creation & reconciliation

One rendered Supplier Payment submission created EGP `20.00` Card payment `4ac7c60c-f7ed-43fc-ab7b-02d7a6b35cd4` with reference `SPAY-20260914121109` and key `supplier-payment-98b57e01-22ec-4cd1-a39e-8e6fb05be30c`; HTTP status was `201`.

Card balance moved `80.00 -> 60.00`; Main Cashbox did not move; supplier subledger moved `0.00 -> 20.00 Debit`. Processed outbox `01a09fd4-0d36-70ab-928e-993f547749dd` links to posted balanced journal `JE-2026-000004` / `01a09fd4-0df0-7e62-941a-58109361a1ff`. The journal debits Supplier Payables and credits only the dedicated supplier-card asset by `20.00`. Result: **PASS**.

## 8. Statement reconciliation after supplier payment

The statement added exactly one Supplier Payment row for `4ac7c60c-f7ed-43fc-ab7b-02d7a6b35cd4`, reference `SPAY-20260914121109`, journal `01a09fd4-0df0-7e62-941a-58109361a1ff`, credit `20.00`, and running balance `60.00`. Thus `0.00 + 100.00 - 20.00 - 20.00 = 60.00`. Result: **PASS**.

## 9. Mixed purchase & reconciliation

Rendered submission key `purchase-create-1789387935542-82irtsp8` returned HTTP `200` and created `PUR-20260914-1011` (`3867a512-4cc5-4a9f-b1d2-cb21d74427f0`) for EGP `20.00`, split exactly `10.00` Cash and `10.00` Card.

Cash moved `900.00 -> 890.00`, Card `60.00 -> 50.00`, aggregate stock `101.00 -> 102.00`, and supplier net balance was unchanged. Posted journal `JE-2026-000005` / `01a09fd5-1220-7078-87ab-7be006db7bc7` has three balanced lines: Dr Inventory `20.00`, Cr Cash `10.00`, Cr Card asset `10.00`. Outbox `01a09fd5-1173-7796-966a-3849eeca2c35` is processed and unique. Result: **PASS**.

## 10. Partial-credit + Card purchase

Rendered key `purchase-create-1789388001057-z35gtdrr` returned HTTP `200` and created `PUR-20260914-1012` (`ca2c00d9-0ce9-49ed-ac15-12ac32aef356`) for `20.00`, with Card paid `10.00` and credit remainder `10.00`.

Card moved `50.00 -> 40.00`, stock `102.00 -> 103.00`, and the supplier's prior `20.00` Debit position became `10.00 Debit`. Posted journal `JE-2026-000006` / `01a09fd6-111f-7d21-a4c4-f8e349d4ace6` is Dr Inventory `20.00`, Cr Card asset `10.00`, Cr Supplier Payables `10.00`. Outbox `01a09fd6-0ff0-76be-aed1-5c18bfef8dbf` is processed. Result: **PASS**.

## 11. Pure-credit purchase

Rendered key `purchase-create-1789388049642-wydnslpa` returned HTTP `200` and created `PUR-20260914-1013` (`ded06987-1ef1-4154-b9f1-6aa5c3b1ba58`) for `15.00`, paid `0.00`, remaining `15.00`, with no funding instrument.

Cash and Card did not move; stock moved `103.00 -> 104.00`; supplier position moved from `10.00 Debit` to `5.00 Credit`. Posted journal `JE-2026-000007` / `01a09fd6-cdc6-768e-bf9b-0ce821068130` is Dr Inventory `15.00`, Cr Supplier Payables `15.00`. Outbox `01a09fd6-cd65-7dae-b3a5-85b72fdd539f` is processed. Result: **PASS**.

## 12. Purchase edit/reversal/repost

One rendered update changed original invoice `PUR-20260914-1010` from `20.00` to `25.00`. Key `purchase-update-1789388799045-5q2stzcr` returned HTTP `200`, and mutation version advanced `0 -> 1`.

The original journal remains immutable and is marked reversed by `JE-2026-000008` (`01a09fe2-3eb3-78d4-b59d-5fc79fc4490c`). Repost journal `JE-2026-000009` (`01a09fe2-3f46-7205-99fe-75412fe0611c`) posts Dr Inventory `25.00`, Cr Card asset `25.00`. The reversal restored `20.00` before repost, producing net Card movement `40.00 -> 35.00`, net inventory-value increase `5.00`, no stock-quantity change, and versioned processed outbox `01a09fe2-3e19-7f3f-ac7c-29ecc2f429f2`. Result: **PASS**.

## 13. Same-key same-payload replay

Purchase key `purchase-create-1789384806134-ttn5bmc0` was replayed with the original EGP `20.00` payload. HTTP `200` returned the original invoice ID, number, total, and version `0`; before/after fingerprint stayed `59EADCABA15FFD8A07964EEC61E4BD68DD632B3F975D4909602300688CA99B6A`.

Supplier-payment key `supplier-payment-98b57e01-22ec-4cd1-a39e-8e6fb05be30c` was replayed with the original payload. HTTP `201` returned the original payment, while the same before/after fingerprint proved no second payment, journal, outbox row, balance movement, or supplier transaction. Result: **PASS**.

## 14. Same-key changed-payload conflict

The purchase key was reused with amount `20.01`; HTTP `409 CONFLICT` reported that the key had been used for a different purchase request. The supplier-payment key was reused with amount `20.01`; HTTP `400 INVALID_REQUEST` reported payload mismatch. Both before/after database fingerprints remained `59EADCABA15FFD8A07964EEC61E4BD68DD632B3F975D4909602300688CA99B6A`, proving zero business-data delta. Result: **PASS**.

## 15. Card overspend rejection

A rendered EGP `35.01` Card purchase was attempted against available balance `35.00`. HTTP `400 SUPPLIER_CARD_AUTHORITY_INVALID` returned `Insufficient funded card balance. Available=35.00, Required=35.01.`

No purchase, payment, stock, supplier, journal, ledger-line, outbox, cash, or Card delta occurred. The only persistence was the designed terminal request-idempotency record, so `PurchaseRequestIdempotency` count moved `5 -> 6`; this explains the whole-capture fingerprint change from `59EADC...` to final `B15656...` and is not a business mutation. Result: **PASS**.

## 16. Funding replay

Historical key `supplier-card-funding-cbb89b09-5deb-430a-87f8-a17c954c4ebe` was replayed with its original EGP `100.00` payload. HTTP `200` returned original funding `534127ff-18b3-41ce-ac0a-0de146807f8e` and journal `01a09ce1-3c2a-7153-beb2-b58385b6a22d`.

Before and after fingerprints were identical at `B15656C75937ED28EDF367428D4C6584BE3AA86D4A8D36368F51A60816F57D53`; funding rows stayed `1`, journals `9`, ledger lines `20`, and outbox `7`. No balance moved and no duplicate was created.

The historical funding predates funding-outbox remediation and has no funding outbox row. This is an immutable pre-remediation legacy boundary: Rerun 3 neither claims atomic historical outbox lineage nor fabricates/backfills a row. The replay's proven no-op behavior and all current mutations' processed outbox lineage satisfy this certification. Result: **PASS**.

## 17. Statement filter coverage

| Filter | API / rendered result | Result |
| --- | --- | --- |
| `2026-09-01` to `2026-09-15` | `200`; 7 transactions; opening `0`, inflows `120`, outflows `85`, closing `35` | PASS |
| Funding day `2026-09-13` | `200`; 1 funding transaction; closing `100` | PASS |
| Activity day `2026-09-14` | `200`; 6 transactions; opening `100`, inflows `20`, outflows `85`, closing `35` | PASS |
| Empty day `2026-09-15` | `200`; 0 transactions; opening/closing `35`; visible empty-state text | PASS |
| Invalid `2026-09-15` to `2026-09-13` | `400 INVALID_REQUEST`; visible `Statement date range is invalid.` | PASS |
| Reset to full range | `200`; restored all 7 rows and original totals | PASS |

The invalid request produced the expected browser resource-status console entry for HTTP `400`; there were no page errors. All normal filter and matrix captures had no unexpected console errors.

## 18. Running balance check

| Step | Movement | Running balance | Result |
| --- | ---: | ---: | --- |
| Opening | `0.00` | `0.00` | PASS |
| Funding | `+100.00` | `100.00` | PASS |
| Original purchase | `-20.00` | `80.00` | PASS |
| Supplier payment | `-20.00` | `60.00` | PASS |
| Mixed purchase Card share | `-10.00` | `50.00` | PASS |
| Partial-credit Card share | `-10.00` | `40.00` | PASS |
| Original-journal reversal | `+20.00` | `60.00` | PASS |
| Edited purchase repost | `-25.00` | `35.00` | PASS |

The arithmetic also equals statement totals: opening `0.00` + inflows `120.00` - outflows `85.00` = closing `35.00`.

## 19. Full success scenario

The complete sequence reached its intended terminal state. From the Rerun 3 opening, Supplier Card payment consumed `20.00`; mixed purchase consumed `10.00` Card and `10.00` Cash; partial-credit purchase consumed `10.00` Card and created `10.00` payable; pure credit created `15.00` payable; edit reversal restored `20.00` Card and repost consumed `25.00`.

Final formulas pass: Cash `900.00 - 10.00 = 890.00`; Card `80.00 - 20.00 - 10.00 - 10.00 + 20.00 - 25.00 = 35.00`; stock `101.00 + 1 + 1 + 1 = 104.00`; supplier `20.00 Debit - 10.00 - 15.00 = 5.00 Credit`. Replays were no-ops, conflicts and overspend were safely rejected, and funding replay returned the historical original without duplication. Result: **PASS**.

## 20. Post-full-success DB reconciliation & reports

The final AhramStore capture was taken after the funding replay at `2026-09-14T12:40:02.4954061Z` under `REPEATABLE READ / READ ONLY / ROLLBACK`.

| Report/state | Final result | Result |
| --- | --- | --- |
| Cashbox cached / ledger | `890.00 / 890.00` | PASS |
| Card cached / ledger / statement | `35.00 / 35.00 / 35.00` | PASS |
| Supplier | Invoiced `80.00`, paid `75.00`, balance `5.00 Credit` | PASS |
| Inventory | Account `80.00`; aggregate stock `104.00`; reserved/damaged `0.00 / 0.00` | PASS |
| Purchases / purchase payments | `4 / 4` | PASS |
| Journals / ledger lines | `9 / 20` | PASS |
| Trial Balance | Debits `1,240.00` = credits `1,240.00`; variance `0.00` | PASS |
| Balance Sheet | Assets `1,005.00` = liabilities `5.00` + equity `1,000.00` | PASS |
| P&L | Revenue, COGS, expenses, and net profit all `0.00` | PASS |
| Cash Flow | Opening `0.00`; closing `890.00`; net change `890.00`; `reconciles=true` | PASS |
| Outbox | Total `7`; pending `0`; failed `0` | PASS |

Cash Flow includes the ledger-derived non-cash settlement adjustment `65.00`; operating `-10.00`, investing `-100.00`, and financing `+1,000.00` reconcile exactly to `890.00`.

## 21. Browser matrix / language / responsive results

| Configuration | Required routes | Direction | Overflow | Errors | Result |
| --- | --- | --- | --- | --- | --- |
| Edge desktop `1440x1000`, English | 3/3 rendered | LTR | `scrollWidth <= clientWidth` | `0` console / `0` page | PASS |
| Edge desktop `1440x1000`, Arabic | 3/3 rendered | RTL | `scrollWidth <= clientWidth` | `0` console / `0` page | PASS |
| Edge mobile `390x844`, English | 3/3 rendered | LTR | `scrollWidth <= clientWidth` | `0` console / `0` page | PASS |
| Edge mobile `390x844`, Arabic | 3/3 rendered | RTL | `scrollWidth <= clientWidth` | `0` console / `0` page | PASS |

The three matrix routes were Supplier Card Funding, Purchase Invoice, and Payments; Card Statement rendering and filters were separately verified at desktop size. Desktop semantic heading capture can resolve to the application heading, so route-specific labels are certified from screenshots and rendered body text rather than from that heading field alone.

## 22. Negative-path and mutation-budget summary

| Request class | Count | Outcome |
| --- | ---: | --- |
| New business mutations | `5` | Supplier payment, mixed purchase, partial-credit purchase, pure-credit purchase, and edit committed once |
| Same-payload replays | `3` | Purchase `200`, supplier payment `201`, funding `200`; all no-op |
| Changed/invalid requests | `3` | Purchase conflict `409`, supplier-payment conflict `400`, overspend `400`; no business delta |
| New capitalization / funding | `0 / 0` | Existing authority records preserved |
| SQL business writes / balance patches / compensating journals | `0 / 0 / 0` | Prohibited paths unused |
| Historical outbox fabrication | `0` | Legacy funding boundary left immutable |

All eleven write-intent requests are accounted for. Successful mutation captures consumed exactly their allowed request; capture guards reported no blocked mutations. Result: **PASS**.

## 23. Protected-account proof

| Account | Baseline | Final | Result |
| --- | ---: | ---: | --- |
| `1020` POS Visa Account, cached / ledger / lines | `0.00 / 0.00 / 0` | `0.00 / 0.00 / 0` | PASS |
| `1110` Visa Clearing, cached / ledger / lines | `0.00 / 0.00 / 0` | `0.00 / 0.00 / 0` | PASS |
| `1030` Bank Account | `0.00 / 0.00` | `0.00 / 0.00` | PASS |
| `1120` Stripe Clearing | `0.00 / 0.00` | `0.00 / 0.00` | PASS |

The deterministic `ProtectedAccounts` hash remained `A3D753604F8B6AA52B85A8471D5C0D1FFD8CBF18271B1CA961975D7F77B8E36F`. Every Card posting used only dedicated account `T06C-CARD-ASSET`. Result: **PASS**.

## 24. Final tenant fingerprints and cross-tenant safety

| Tenant | Baseline fingerprint | Final fingerprint | Result |
| --- | --- | --- | --- |
| AhramStore | `AC77359B6C6EE366B939E0DD65CC0F1091C6CCA0250757EA915CF1DB25266A01` | `B15656C75937ED28EDF367428D4C6584BE3AA86D4A8D36368F51A60816F57D53` | PASS, expected certified deltas only |
| Shopixy | `1D1AC2FF6545EEC89EF587CEA1ECF83646AD69D53752809C4589055AB5274E3C` | `1D1AC2FF6545EEC89EF587CEA1ECF83646AD69D53752809C4589055AB5274E3C` | PASS, unchanged |
| GreatOne | `B996685158542D7FF0FE1EA5194AF3AF7E55E597D22149AC9826921C4ACD390B` | `B996685158542D7FF0FE1EA5194AF3AF7E55E597D22149AC9826921C4ACD390B` | PASS, unchanged |

Final funding replay preserved the AhramStore final fingerprint. Shopixy and GreatOne fingerprints stayed byte-identical from baseline to final, proving cross-tenant containment. Evidence is retained under `C:\Users\fadya\AppData\Local\Temp\opencode\t06c-rerun3-evidence\`.

## 25. Final PASS/FAIL matrix

| Required certification area | Result | Evidence summary |
| --- | --- | --- |
| Exact opening state | PASS | Cash `900`, Card `80`, stock `101`, supplier `0`, TB `1120/1120` |
| Browser/auth/role/live API | PASS | Headed Edge 152, AhramStore Owner, EG/EGP |
| Fresh tenant baselines | PASS | Three repeatable-read, read-only captures |
| Original Card purchase | PASS | Invoice, allocation, journal, outbox, and statement reverified |
| Supplier Card payment | PASS | One `20.00` payment; Card `80 -> 60` |
| Mixed purchase | PASS | `10.00` Cash + `10.00` Card |
| Partial-credit + Card | PASS | `10.00` Card + `10.00` credit |
| Pure-credit purchase | PASS | `15.00` payable; no Cash/Card movement |
| Edit/reversal/repost | PASS | `20 -> 25`, version `0 -> 1`, immutable reversal lineage |
| Purchase replay / conflict | PASS | `200` no-op / `409` no-op |
| Supplier-payment replay / conflict | PASS | `201` no-op / `400` no-op |
| Card overspend | PASS | `35.01` safely rejected against `35.00` |
| Funding replay | PASS | Original funding returned; no duplicate or delta |
| Statement filters / running balance | PASS | Full, boundary, empty, invalid, reset, and arithmetic |
| Trial Balance / Balance Sheet / P&L | PASS | Balanced and coherent |
| Cash Flow | PASS | Closing `890`, net change `890`, `reconciles=true` |
| Outbox | PASS | `7` total, `0` pending, `0` failed |
| Browser language/responsive matrix | PASS | 12/12 route/configuration renders |
| Protected accounts | PASS | Unchanged hash and zero balances/lines |
| Cross-tenant safety | PASS | Shopixy and GreatOne unchanged |
| Historical funding boundary | PASS | Legacy orphan disclosed and untouched |
| Full T-06C certification | **PASS** | Every required scenario and invariant passed |

## 26. Final verdict and blocking issues

**FINAL VERDICT: PASS.** Rerun 3 completed and reconciled the full real-business-data scenario across rendered UI behavior, API responses, idempotency, supplier subledger, inventory, Treasury, dedicated Card authority, journals, processed outbox, Card Statement, Trial Balance, Balance Sheet, P&L, and Cash Flow. Protected accounts and both non-target tenants remained unchanged.

Blocking issues: **none**. The historical funding's absent outbox row remains a disclosed immutable pre-remediation boundary and was neither misrepresented nor modified.

Verification count: **26/26 required report sections completed**, **22/22 final certification matrix checks passed**, **12/12 browser route/configuration renders passed**, and **11/11 write-intent requests accounted for**.

**T-06 COMPLETE.**
