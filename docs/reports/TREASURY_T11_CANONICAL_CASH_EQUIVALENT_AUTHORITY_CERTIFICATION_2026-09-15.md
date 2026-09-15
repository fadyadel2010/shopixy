# Treasury T-11 Canonical Cash-Equivalent Authority Certification - 2026-09-15

## 1. Executive Verdict

**T-11 canonical cash-equivalent authority: PASS.**

Cash Flow now derives cash exclusively from active, non-deleted, same-tenant Asset accounts that have an active, non-deleted `TreasuryAccount` mapping of type Cashbox or Bank. Account-code prefix inference is absent. Clearing, `1020`, supplier-card funding assets, control parents, reserves, unmapped assets, and invalid mapping states remain outside cash while retaining their existing ledger and report visibility.

Focused tests passed 42/42, the broader relevant gate passed 83/83, the Release solution build passed with zero warnings and errors, the frontend production build passed, EF reports no pending model changes, authenticated API and Edge checks reconciled across the audited surfaces, and immediate read-only database before/after evidence showed no accounting-data change.

## 2. Certification Scope

T-11 changes only Cash Flow's cash-equivalent classification authority and deterministic tests. Independent certification covers opening and closing cash, per-journal movement, activity classification, clearing collection and settlement, internal transfers, supplier-card semantics, tenant and state predicates, reconciliation, and unchanged Treasury, Balance Sheet, Trial Balance, General Ledger, Account Summary, Clearing Aging, and frontend boundaries.

Chart of Accounts redesign, `1020` retirement, provider settlement enablement, clearing-lot accounting, supplier-card redesign, migrations, seeds, balance repair, historical rewrites, direct SQL business writes, manual journals, and broad UI changes remain out of scope.

## 3. Safety Boundary

Repository HEAD remained `0de1c542bd316026de06bd5cc8f7111878b1b0e6`. The worktree already contained substantial uncommitted T-06 through T-10 and unrelated work. No reset, restore, clean, staging, commit, or modification of unrelated work was performed.

The T-11 implementation stayed within `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs` and `PointOfSale.Tests/Accounting/FinancialReportServiceTests.cs`. This certification report and temporary evidence harnesses are documentation or local verification artifacts, not production changes.

## 4. Discovery Decision

Discovery found one concrete inconsistency requiring bounded remediation. Treasury operations, Treasury totals, Financial Posting, and Account Summary already obtained Treasury authority from same-tenant mappings. `FinancialReportService.GetCashFlowAsync` instead treated any `10xx` or `11xx` account as cash.

The old rule admitted chart parents, `1020`, reserves, clearing, and arbitrary unmapped assets. It could disagree with Treasury's operational Cashbox/Bank total even while the Cash Flow arithmetic reconciled internally.

## 5. Root Cause

The chart-number convention had become runtime financial authority. Account placement does not prove liquidity, and it must not substitute for a persisted Treasury mapping.

The defect affected opening cash, closing cash, journal cash lines, movement, and counterpart classification. A balance-only fix would have left activity and reconciliation semantics inconsistent, so one resolved account-ID set had to govern every Cash Flow boundary.

## 6. Canonical Authority

An account qualifies only when every predicate holds:

```text
TreasuryAccount.TenantId == requested tenant
TreasuryAccount.AccountId == LedgerAccount.Id
TreasuryAccount.IsActive
!TreasuryAccount.IsDeleted
LedgerAccount.TenantId == requested tenant
LedgerAccount.IsActive
!LedgerAccount.IsDeleted
LedgerAccount.AccountType == Asset
TreasuryAccount.Type is Cashbox or Bank
```

There is no account-code, name, parent, `IsSystem`, `IsClearing`, or `PreventNegative` fallback. A tenant without qualifying mappings receives a valid zero-cash perimeter.

## 7. Delegation and Independent Review

Implementation was delegated once to Muse with `opencode/muse-spark-1.3-contributor-free`, OpenCode `1.18.31`, session `ses_f5b198876ffeEBQoZJE05CF1Tn`. The frozen exactly 20-section contract is recorded in `docs/reports/TREASURY_T11_CASH_EQUIVALENT_DELEGATION_BRIEF_2026-09-15.md`.

Muse implemented the bounded change and tests but did not certify T-11. Independent SOL review inspected the full scoped diff, reran all required gates, performed authenticated API and browser verification, and reconciled read-only PostgreSQL evidence. No corrective production edit was required after review.

## 8. Repository Files

| File | T-11 purpose |
| --- | --- |
| `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs` | Resolves and applies canonical mapped Cashbox/Bank account IDs throughout Cash Flow. |
| `PointOfSale.Tests/Accounting/FinancialReportServiceTests.cs` | Updates intended-cash fixtures and adds T-11 authority and behavior regressions. |
| `docs/reports/TREASURY_T11_CASH_EQUIVALENT_DELEGATION_BRIEF_2026-09-15.md` | Records discovery, scope, contract, and acceptance matrix. |
| `docs/reports/TREASURY_T11_CANONICAL_CASH_EQUIVALENT_AUTHORITY_CERTIFICATION_2026-09-15.md` | Records this exactly 30-section independent certification. |

No T-11 frontend, controller, DTO, entity, configuration, model snapshot, migration, seed, package, Treasury mutation, payment, or supplier-card production file changed.

## 9. Production Implementation

`GetCashFlowAsync` resolves `cashAccountIds` once inside its consistent read transaction by joining `TreasuryAccounts` to `LedgerAccounts` with explicit tenant, active, delete-state, Asset-type, and Cashbox/Bank-type predicates (`FinancialReportService.cs:420-437`).

The same set selects opening and closing balances (`FinancialReportService.cs:439-447`), journal cash lines and movement (`FinancialReportService.cs:478-485`), and non-cash Asset counterparts (`FinancialReportService.cs:925-953`). The former `IsCashEquivalent` prefix helper was removed.

## 10. Opening and Closing Cash

Opening cash sums qualifying posted/reversed ledger movement before the report start date. Closing cash sums the same qualifying accounts through the report end date. The account set is identical at both boundaries and is tenant-scoped.

Focused tests prove mapped custom nonnumeric Cashbox/Bank codes qualify, unmapped prefix accounts do not supply opening or closing cash, and no qualifying mappings produce zero opening and closing cash without fallback.

## 11. Journal Movement

For each in-period posted/reversed journal, cash movement remains `DebitAmount - CreditAmount` over qualifying mapped account IDs. Journal and ledger-line tenant predicates remain in place, and `JournalDate` continues to define period inclusion.

Internal Cashbox-to-Bank movement has equal and opposite qualifying lines and therefore nets to zero. Reversal entries contribute the exact opposite qualifying movement in the reversal period.

## 12. Activity Classification

Cash Flow classification receives the same `cashAccountIds` set used by balance and movement calculations. An Asset line is non-cash whenever its account ID is absent from that set, regardless of account code.

The synthetic working-capital bridge label now uses `Mapped Cashbox/Bank` instead of the obsolete `10xx/11xx` description (`FinancialReportService.cs:605`). Existing indirect-profit, working-capital, party-opening, aggregation, and reconciliation behavior remains intact.

## 13. Clearing Collection

Clearing is not a cash equivalent. A collection journal such as Dr Clearing / Cr Revenue has zero cash movement until a qualifying Bank or Cashbox receives funds.

Tests cover unmapped clearing metadata and mapped Treasury type Clearing. Neither form enters opening cash, closing cash, or journal cash lines.

## 14. Clearing Settlement

When a journal contains qualifying Bank cash and a clearing counterpart, the clearing metadata is recognized before generic non-cash Asset classification. Dr Bank / Cr Clearing is therefore Operating rather than Investing without granting clearing cash or mutation authority.

Focused tests cover settlement with and without fees. The Bank inflow is counted once, fee expense is retained once, the journal is not mislabeled as investing, and the report reconciles.

## 15. Double-Counting Controls

The accepted behavior is now deterministic:

| Scenario | Certified result |
| --- | --- |
| Cashbox collection | Cash inflow once |
| Clearing collection | Zero cash before settlement |
| Clearing to Bank | Operating Bank inflow once |
| Clearing to Bank with fee | Operating net inflow once; fee once |
| Cashbox to Bank | Zero net cash movement |
| Cashbox to supplier-card asset | Investing outflow once |
| Supplier-card purchase | No additional cash movement |
| `1020` supplier purchase credit | Zero cash movement |
| Reversal | Opposite qualifying movement in reversal period |

## 16. Prefix and `1020` Exclusion

Source search found no remaining `StartsWith("10")`, `StartsWith("11")`, `IsCashEquivalent`, or `10xx/11xx` cash authority in `FinancialReportService.cs`.

Live Shopixy evidence demonstrates the material difference. Cash Flow and Treasury Cashbox/Bank equal EGP `10,484.02`; the former prefix perimeter would equal EGP `10,535.22`. The excluded EGP `51.20` is clearing EGP `55.00` plus `1020` EGP `-3.80`.

## 17. Supplier-Card Boundary

AhramStore's `T06C-CARD-ASSET` remains EGP `35.00` and `isTreasury=false`. Cash Flow closes at EGP `890.00`, exactly matching mapped Cashbox/Bank, while supplier-card funding remains an Investing outflow of EGP `100.00` and the full report reconciles.

The supplier-card asset remains visible on Balance Sheet and Trial Balance. T-06C funding, purchase, and reversal semantics are covered without changing supplier-card authority.

## 18. Mapping State and Tenant Isolation

Focused tests reject inactive or deleted mappings, inactive or deleted linked accounts, foreign-tenant mappings and linked accounts, non-Asset linked accounts, Clearing mappings, and unsupported enum values. Explicit predicates protect the boundary independently of global query filters.

The three authenticated runtime reports each returned their requested tenant ID and reconciled only to that tenant's Treasury total. No cross-tenant authority or ledger contribution was observed.

## 19. Treasury Boundary

Treasury reads and operations remain unchanged. Generic transfer eligibility remains mapped Cashbox/Bank-only, and clearing settlement remains a distinct evidence-controlled path under T-10 containment.

Account Summary's existing `isTreasury` flag continues to indicate a Treasury mapping, including Clearing; it is not used as Cash Flow cash authority. Live Treasury dashboard totals remained separately reported as Cashbox, Bank, total Cash and Bank, and Pending Clearing.

## 20. Balance Sheet Boundary

Balance Sheet continues to expose every nonzero Asset, including Cashbox, Bank, clearing, `1020`, and supplier-card funding assets. It does not adopt the Cash Flow liquidity filter.

Live results were balanced with zero variance: Shopixy Assets and Liabilities plus Equity both EGP `11,430.34`; AhramStore both EGP `1,005.00`; greatone both EGP `2,763.00`. Shopixy retained `1020 = -3.80` and `1110 = 55.00`; AhramStore retained `T06C-CARD-ASSET = 35.00`.

## 21. Trial Balance Boundary

Trial Balance remains ledger-complete and balanced. Live Shopixy debits and credits both equaled EGP `20,727.35`, AhramStore both equaled EGP `1,240.00`, and greatone both equaled EGP `23,751.34`; every variance was zero.

Shopixy retained `1020` debits EGP `2.20`, credits EGP `6.00`, balance EGP `-3.80`, and `1110` debits EGP `165.00`, credits EGP `110.00`, balance EGP `55.00`. AhramStore retained the supplier-card line at EGP `35.00`.

## 22. General Ledger and Account Summary

Authenticated Shopixy GL reads retained full facts. `1020` returned eight entries, EGP `2.20` debits, EGP `6.00` credits, and EGP `-3.80` closing balance. `1110` returned three entries, EGP `165.00` debits, EGP `110.00` credits, and EGP `55.00` closing balance.

Account Summary returned 32 Shopixy, 33 AhramStore, and 32 greatone accounts. Control parents, reserves, `1020`, clearing, and supplier-card rows were not hidden or rewritten.

## 23. Authenticated API Runtime

The freshly built Development API listened on `5010`; Vite listened on `5173`. Development-only `POST /api/test-auth/token` issued DB-bound Admin JWTs for the three real active tenants. Authentication/session activity was the only authorized non-GET behavior.

Subsequent runtime certification used GET-only calls to Cash Flow, Treasury dashboard, Balance Sheet, Trial Balance, Account Summary, Clearing Aging, and selected Shopixy General Ledgers. Every audited financial GET returned successfully. Aggregate `/health` returned HTTP 503 because an external dependency was unhealthy, but this did not affect authenticated SQL-backed report routes.

## 24. Cross-Tenant Runtime Reconciliation

| Tenant | Cash Flow closing | Cashbox | Bank | Treasury Cash and Bank | Pending Clearing | Cash variance | Reconciles |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| shopixy | EGP 10,484.02 | EGP 73.92 | EGP 10,410.10 | EGP 10,484.02 | EGP 55.00 | EGP 0.00 | true |
| AhramStore | EGP 890.00 | EGP 890.00 | EGP 0.00 | EGP 890.00 | EGP 0.00 | EGP 0.00 | true |
| greatone | EGP 5,234.84 | EGP 5,234.84 | EGP 0.00 | EGP 5,234.84 | EGP 14.00 | EGP 0.00 | true |

The old prefix totals were EGP `10,535.22`, EGP `890.00`, and EGP `5,248.84`, respectively. The differences are exactly the excluded non-cash prefix balances present in each tenant.

## 25. Desktop Browser Certification

Headless Microsoft Edge at `1440x1000` authenticated with a real DB-bound greatone Admin JWT and loaded `/treasury/cash-flow` and `/treasury/hub` through the running Web/API stack.

Cash Flow returned HTTP 200, rendered EGP `5,234.84`, displayed `RECONCILED`, and rendered the ledger-derived non-cash settlement adjustment. The Hub separately rendered Total Cash and Bank EGP `5,234.84` and Pending Clearing EGP `14.00`. No API request failed.

## 26. Mobile Browser Certification

The same checks passed at `390x844`. Cash Flow rendered, reconciled, and displayed the EGP `5,234.84` closing value and adjustment. The Hub retained the distinct EGP `5,234.84` Cash and Bank and EGP `14.00` Pending Clearing values. No API request failed.

Each viewport logged only Chromium's known CSP diagnostic that `frame-ancestors` is ignored when delivered through a meta element. It is unrelated to T-11 and did not produce a page exception or failed response.

## 27. Database Before and After

Immediate evidence was captured in `PosEcommerceDb_Baseline` at `2026-09-15T15:28:03.326932+03:00` before the final authenticated API/browser pass and at `2026-09-15T15:37:41.859806+03:00` after it. Both transactions used `REPEATABLE READ`, `SET TRANSACTION READ ONLY`, and rollback.

`git diff --no-index` between `.opencode-tmp/t11-db-before-runtime.json` and `.opencode-tmp/t11-db-after-browser.json` showed only the expected `serverTime` change. Tenants, accounts, mappings, cached and ledger balances, recent ledger usage, payment-method authority, supplier-card instruments, legacy finance counts, and legacy account rows were byte-for-byte identical.

## 28. Tests and Builds

| Gate | Result |
| --- | --- |
| Baseline focused `FinancialReportServiceTests` before T-11 | 31 passed, 0 failed |
| Independent focused `FinancialReportServiceTests` after T-11 | 42 passed, 0 failed |
| Broader Financial Report, Treasury transfer/settlement, supplier-card, and T-10 slice | 83 passed, 0 failed |
| Existing focused frontend Treasury slice before implementation | 3 passed, 0 failed |
| `dotnet build PointOfSale.sln -c Release --no-restore` | Passed, 0 warnings, 0 errors |
| `npm run build` | Passed, 5,216 modules transformed |
| Desktop and mobile Edge harness | Passed both viewports |

The frontend build retained its existing large-chunk warning for the approximately 5,171 kB main bundle. No frontend source changed for T-11.

## 29. Migration, Diff, and Residual Risks

`dotnet ef migrations has-pending-model-changes` reported no model changes since the last migration. T-11 added no migration and did not modify the model snapshot. `git diff --check` reported no whitespace error; it emitted only LF-to-CRLF working-copy notices across the already dirty worktree.

Live Shopixy Bank activity changed between the earlier discovery snapshot and the immediate certification pair. The stale discovery value is not presented as a no-mutation baseline; the fresh pair surrounding final runtime verification is the authoritative before/after evidence. Concurrent worktree changes and aggregate external-service health remain outside T-11. Dynamic multi-Cashbox/Bank Hub cards remain a separate scalability improvement because the current Hub presentation still uses its established fixed account-code cards.

## 30. Final Classification and Readiness

**Final T-11 classification: PASS.**

Only qualifying mapped Cashbox/Bank Asset IDs define Cash Flow cash. Prefix inference is absent; `1020`, clearing, supplier-card assets, parents, reserves, unmapped assets, and invalid mapping states are excluded. Clearing collection remains non-cash; clearing settlement becomes Operating once and retains fees once; internal transfers net to zero; supplier-card and reversal behavior remains reconciled; tenant boundaries hold; Balance Sheet, Trial Balance, GL, Account Summary, Treasury, Clearing Aging, and T-10 boundaries remain intact.

The implementation is eligible to proceed: focused and broader tests pass, API and Web builds pass, no migration is required, authenticated desktop/mobile and cross-tenant runtime values reconcile, and protected accounting evidence is unchanged before and after final certification. No staging or commit was performed.
