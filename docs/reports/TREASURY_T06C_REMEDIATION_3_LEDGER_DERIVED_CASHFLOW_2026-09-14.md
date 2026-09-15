# Treasury T-06C Remediation 3: Ledger-Derived Cash Flow

Certification date: 2026-09-14  
Target tenant: `AhramStore` (`c9c5c1ae-949e-498e-bc63-07242daeb49d`)  
Execution mode: source remediation, deterministic tests, Release build, authenticated headed Microsoft Edge GETs, and PostgreSQL repeatable-read/read-only reconciliation  
Currency: EGP  
Final verdict: **PASS**

## 1. Executive Verdict

**REMEDIATION 3 VERDICT: PASS.** Cash Flow now derives period cash activity from posted/reversed journal and ledger lines rather than treating every inventory increase as an unqualified cash outflow. The existing AhramStore Card purchase now reports opening cash `0.00`, operating cash flow `0.00`, investing cash flow `-100.00`, financing cash flow `1,000.00`, closing cash `900.00`, net cash change `900.00`, and `reconciles=true`.

The existing purchase, allocation, journal, ledger lines, balances, inventory, supplier state, and outbox were not changed. The known pre-remediation funding outbox orphan remains untouched and is not represented as repaired.

## 2. Scope and Safety Constraints

This remediation was limited to Cash Flow reporting logic, deterministic regression tests, build/static validation, authenticated read-only API checks, and a final database capture. No capitalization, funding, purchase, supplier payment, replay, conflict request, reversal, edit/repost, overspend request, direct SQL business write, cached-balance patch, journal/ledger rewrite, outbox insertion, or historical repair was performed.

The browser certification issued only `GET` requests. The database probe opened `REPEATABLE READ`, executed `SET TRANSACTION READ ONLY`, and rolled back after reading. Secrets, cookies, authorization headers, and CSRF values are not retained in evidence.

## 3. Authoritative Stopped Baseline

The authoritative source baseline is `db-stopped-final-ahramstore.json` from the Golden rerun. It contains one completed EGP `20.00` Card purchase, purchase `17adfeb8-f5b8-4ce1-b45f-64583b2b53b1`, journal `01a09fa5-536b-7eed-b166-47dc12ffce28`, and outbox row `01a09fa5-520a-747e-be07-6e5bd941a3e7`.

| Baseline invariant | Value |
| --- | ---: |
| Main Cashbox `1010` | `900.00` |
| Supplier-card asset | `80.00` |
| Inventory `1310` | `20.00` |
| Supplier balance | `0.00` |
| Selected variant / aggregate stock | `11.00 / 101.00` |
| Purchases / purchase allocations | `1 / 1` |
| Journals / ledger lines | `3 / 6` |
| Accounting outbox total / pending / failed | `2 / 0 / 0` |

## 4. Defect Reproduction

Before this remediation, Cash Flow correctly read closing cash `900.00` but reported operating cash flow `-20.00`, net cash change `880.00`, and `reconciles=false`. The EGP `20.00` purchase journal was Dr Inventory `1310` / Cr `T06C-CARD-ASSET`; it contained no `10xx` or `11xx` cash-equivalent line.

The report nevertheless applied the full inventory increase as a working-capital cash outflow. The missing non-cash settlement offset produced the exact EGP `20.00` discrepancy.

## 5. Root Cause

`FinancialReportService.GetCashFlowAsync` used boundary balance changes for indirect working-capital adjustments but did not reconcile those adjustments to the actual cash-equivalent lines in each period journal. This allowed a non-cash asset settlement to look like a cash purchase.

Document payment methods, mutable instrument balances, and custom account-name inference are not reliable accounting authority. The immutable posted/reversed ledger is the source that records whether a journal actually moved cash.

## 6. Ledger Authority Query

`PointOfSale.Infrastructure/Accounting/FinancialReportService.cs:429-450` now loads one tenant-scoped and journal-date-scoped set of posted/reversed period ledger lines. Each projected line includes journal identity and description, account identity/code/type, debit, and credit.

The query requires matching tenant identity on the ledger line, journal, and account. Cash movement is calculated per journal as `DebitAmount - CreditAmount` over cash-equivalent lines only.

## 7. Cash-Equivalent Boundary

The existing Cash Flow definition is preserved: account codes beginning `10` or `11` are cash equivalents. The dedicated `T06C-CARD-ASSET` account is an asset but is not a cash equivalent.

Consequently, the purchase journal's card credit has zero direct cash movement. The historical funding journal's `1010` credit has EGP `-100.00` direct cash movement.

## 8. Activity Classification

`ClassifyCashFlowActivity` at `FinancialReportService.cs:909-934` classifies journals from their ledger counterparts:

| Ledger counterpart | Classification |
| --- | --- |
| Equity | Financing |
| Revenue, COGS, expense, inventory, receivable, or operating liability | Operating |
| Other non-cash asset | Investing |
| Other liability | Financing |
| Remaining cash-only movement | Operating |

This classifies the EGP `1,000.00` capitalization as financing, the EGP `100.00` cash-to-card funding as investing, and the Card purchase as operating with zero cash movement.

## 9. Non-Cash Settlement Bridge

The report retains the existing indirect presentation for net profit and working-capital changes. It now calculates actual operating cash movement from journal cash lines and adds `Ledger-derived non-cash settlement adjustment` for the difference between that authority and the indirect subtotal.

For AhramStore, `Change in Inventory` remains EGP `-20.00` and the ledger-derived bridge is EGP `+20.00`. They net to operating cash flow `0.00`, matching the purchase journal's absence of cash-equivalent lines.

## 10. Reversals, Transfers, Tenant, and Date Semantics

The implementation includes posted and reversed journals and therefore uses the reversal's own ledger direction. A cash purchase reversal returns cash, while a non-cash Card purchase reversal has no cash effect. Cashbox-to-bank transfers aggregate to zero because both sides are cash equivalents.

Tests also prove tenant isolation and use of `JournalDate`, not posting timestamp, for period inclusion. Party-opening exclusions remain in place so non-cash opening receivables/payables do not become financing cash flow.

## 11. Deterministic Regression Coverage

Coverage includes Cash, Bank, Supplier Card, Credit, and mixed purchase settlement; Cash, Supplier Card, and mixed reversals; supplier-card funding and reversal; the exact EGP `20.00` AhramStore defect; foreign-tenant exclusion; cashbox-to-bank transfer; and journal-date boundaries.

The exact-defect test asserts opening `0.00`, closing `900.00`, operating `0.00`, investing `-100.00`, financing `1,000.00`, net change `900.00`, inventory adjustment `-20.00`, ledger bridge `+20.00`, and successful reconciliation.

## 12. Test Results

| Test set | Result |
| --- | --- |
| `FinancialReportServiceTests` | PASS, `31/31` |
| Focused Financial Report, Treasury, supplier-card, and accounting set | PASS, `103/103` |

No test was weakened, skipped, or converted to accept the prior `880.00` result.

## 13. Build and Static Validation

| Check | Result |
| --- | --- |
| API Release build | PASS, `0` warnings, `0` errors |
| `git diff --check` | PASS, no whitespace errors; existing LF-to-CRLF notices only |
| `GET http://localhost:5010/health/live` | PASS, HTTP `200` |

The local API was restarted from the Release output before live certification.

## 14. Authenticated Live API Coverage

The final headed Edge run used the existing authenticated AhramStore session. All twelve requested resources returned HTTP `200`: Cash Flow, Trial Balance, Balance Sheet, Profit and Loss, Treasury summary, Card Statement, funding instruments, purchases, suppliers, and the Cashbox, Inventory, and card-asset General Ledgers.

The final API evidence was captured at `2026-09-14T11:57:12.070Z`. No business mutation endpoint was invoked.

## 15. Live Cash Flow Result

| Measure | Live result | Result |
| --- | ---: | --- |
| Opening cash | `0.00` | PASS |
| Net profit | `0.00` | PASS |
| Inventory working-capital change | `-20.00` | PASS |
| Ledger-derived non-cash bridge | `+20.00` | PASS |
| Operating cash flow | `0.00` | PASS |
| Investing cash flow | `-100.00` | PASS |
| Financing cash flow | `+1,000.00` | PASS |
| Closing cash | `900.00` | PASS |
| Net cash change | `900.00` | PASS |
| `reconciles` | `true` | PASS |

The prior EGP `20.00` shortfall is eliminated without modifying the purchase or its accounting records.

## 16. Live Financial Report Reconciliation

| Report | Live result | Result |
| --- | --- | --- |
| Trial Balance | Debits `1,120.00` = credits `1,120.00`; variance `0.00` | PASS |
| Balance Sheet | Assets `1,000.00` = liabilities `0.00` + equity `1,000.00`; variance `0.00` | PASS |
| Profit and Loss | Revenue, COGS, expenses, and net profit all `0.00` | PASS |
| Treasury summary | Current balance `900.00` | PASS |

The Cash Flow closing balance agrees with the Trial Balance `1010` balance, Treasury summary, and General Ledger.

## 17. Purchase, Journal, and Statement Reconciliation

Purchase `PUR-20260914-1010` remains completed at EGP `20.00`, paid `20.00`, remaining `0.00`, mutation version `0`, with one Card allocation to instrument `cee6f007-0685-487e-b2b2-d977faff9d60` at authority version `1`.

Journal `JE-2026-000003` remains posted and balanced: Dr `1310` EGP `20.00`; Cr `T06C-CARD-ASSET` EGP `20.00`. The Card Statement remains opening `0.00`, funding `+100.00`, purchase `-20.00`, and closing `80.00`.

## 18. Final Database Non-Mutation Proof

The final database capture occurred at `2026-09-14T14:57:18.462253+03:00` under `REPEATABLE READ / READ ONLY / ROLLBACK`. It matches the stopped baseline on all targeted business invariants:

| Final invariant | Value | Result |
| --- | ---: | --- |
| Purchases / allocations | `1 / 1` | PASS |
| Fundings / instruments | `1 / 1` | PASS |
| Journals / ledger lines | `3 / 6` | PASS |
| Main Cashbox cached / ledger | `900.00 / 900.00` | PASS |
| Inventory cached / ledger | `20.00 / 20.00` | PASS |
| Card asset cached / ledger | `80.00 / 80.00` | PASS |
| Supplier balance | `0.00` | PASS |
| Selected variant / aggregate stock | `11.00 / 101.00` | PASS |
| Accounting outbox total / pending / failed | `2 / 0 / 0` | PASS |
| Protected `1020`, `1030`, `1110`, `1120` balances / lines | `0.00 / 0` | PASS |

The purchase outbox remains processed against the same journal with attempt count `0` and no failure. No hidden target-tenant mutation was found.

## 19. Evidence and Historical Boundary

Evidence is retained at:

| File | Purpose |
| --- | --- |
| `C:\Users\fadya\AppData\Local\Temp\opencode\t06c-r3-live-readonly.json` | Final authenticated GET-only API and report results |
| `C:\Users\fadya\AppData\Local\Temp\opencode\t06c-r3-final-state.json` | Final repeatable-read/read-only database state |
| `C:\Users\fadya\AppData\Local\Temp\opencode\t06c-rerun2-evidence\db-stopped-final-ahramstore.json` | Authoritative pre-remediation stopped baseline |

Historical funding `534127ff-18b3-41ce-ac0a-0de146807f8e` still has no funding outbox row. The final query confirms zero outbox rows linked to that funding or journal `01a09ce1-3c2a-7153-beb2-b58385b6a22d`. This immutable legacy boundary is intentionally unchanged; this report certifies the ledger-derived Cash Flow remediation and does not claim a historical outbox repair or completion of the stopped downstream Golden scenario matrix.

## 20. Final Verdict

**FINAL VERDICT: PASS.** The implementation derives actual cash movement from tenant-scoped, date-scoped posted/reversed ledger lines, correctly bridges non-cash operating settlements, preserves reversal and transfer semantics, and passes deterministic regression, Release build, live financial-report, and read-only database checks.

The existing AhramStore Card purchase now reconciles at closing cash and net cash change EGP `900.00` with no business-data mutation. The historical funding outbox orphan remains untouched and explicitly outside this remediation's PASS claim.
