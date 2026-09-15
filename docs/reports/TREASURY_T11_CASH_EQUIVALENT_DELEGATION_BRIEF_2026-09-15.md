# Treasury T-11 Canonical Cash-Equivalent Authority Delegation Brief - 2026-09-15

## 1. Executive Decision

**Discovery verdict: the Cash Flow cash-equivalent perimeter is inconsistent with established Treasury authority and requires bounded remediation.** Treasury operations, Treasury totals, and Account Summary use active same-tenant `TreasuryAccount` mappings. Cash Flow instead treats every account code beginning with `10` or `11` as cash, which includes control parents, `1020`, reserve accounts, and clearing.

The approved T-11 decision is: cash equivalents are active, non-deleted, same-tenant Asset accounts with an active, non-deleted `TreasuryAccount` mapping whose type is Cashbox or Bank. Clearing and every unmapped asset are outside cash equivalents. No account, journal, balance, mapping, code, or historical row is changed.

## 2. Scope

T-11 changes only the Cash Flow classification authority and its deterministic tests. It verifies, but does not redesign, Treasury totals and transfers, Clearing Aging, Balance Sheet, Trial Balance, General Ledger, Account Summary, and the current Treasury frontend.

Out of scope: Chart of Accounts redesign, `1020` retirement or reclassification, provider settlement enablement, clearing-lot accounting, supplier-card authority changes, migrations, seeds, cached-balance repair, historical rewrites, direct SQL business writes, manual journals, and broad UI redesign.

## 3. Safety Boundary

Discovery used PostgreSQL database `PosEcommerceDb_Baseline` under `REPEATABLE READ`, `SET TRANSACTION READ ONLY`, and rollback. The repository was already heavily dirty with uncommitted T-06 through T-10 and unrelated work. HEAD was `0de1c542bd316026de06bd5cc8f7111878b1b0e6`.

Do not reset, restore, clean, stage, commit, reformat wholesale, or overwrite existing changes. No real financial/business mutation is authorized. Runtime certification is GET-only apart from normal authentication/session activity.

## 4. Canonical Authority

An account is a T-11 cash equivalent only when all predicates hold:

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

There is no account-code, name, parent, `IsSystem`, or `PreventNegative` fallback. If a tenant has no qualifying mappings, its Cash Flow cash perimeter is empty and reports zero cash rather than inventing authority.

## 5. Classification Matrix

| Class | Cash Flow cash equivalent | Treasury read/operation role | Balance Sheet / GL role |
| --- | --- | --- | --- |
| Active mapped Cashbox | Yes | Cashbox; transferable | Asset and full ledger history |
| Active mapped Bank | Yes | Bank; transferable | Asset and full ledger history |
| Active mapped Clearing | No | Clearing/reporting; not a generic transfer account | Asset and full ledger history |
| `IsClearing=true` without qualifying Cashbox/Bank mapping | No | Clearing report metadata only | Asset and full ledger history |
| `1020 POS Visa Account` | No | None | Existing Asset and immutable history remain visible |
| Supplier-card funding asset | No | Dedicated supplier-card authority, not Treasury cash | Existing Asset and immutable history remain visible |
| Parent `1000`/`1100`, reserve `1040`, or other unmapped `10xx`/`11xx` | No | None | Existing account-level reporting remains visible |
| Inactive/deleted/foreign/non-Asset/unsupported mapping | No | No authority | Existing eligible historical ledger reporting remains governed by each report |

## 6. Source Inventory

`FinancialReportService.GetCashFlowAsync` derives opening and closing balances at lines 417-427 and journal cash lines at lines 429-491. Both call `IsCashEquivalent`, whose current implementation at lines 892-897 is only the `10`/`11` prefix rule. `ClassifyCashFlowActivity` at lines 909-934 also uses that prefix to distinguish cash from other assets.

`FinancialReportService.GetAccountSummariesAsync` already obtains active same-tenant Treasury IDs through mappings. `TreasuryReadService.GetDashboardAsync`, `TreasuryTransferService`, `TreasuryOperationService`, `FinancialPostingService`, supplier-card authority, and initial capitalization independently use Cashbox/Bank mapping types for liquid Treasury authority.

## 7. Root Cause

The original chart convention was used as runtime financial authority. Prefixes describe chart placement, not liquidity. This conflates three independent concepts: seeded/protected account metadata (`IsSystem`), clearing/report metadata (`IsClearing`), and operational Treasury authority (`TreasuryAccount`).

The defect affects both balance boundaries and per-journal movement. Fixing only opening/closing values would leave activity classification inconsistent and could break reconciliation. The same resolved account-ID set must govern opening cash, closing cash, journal cash movement, and non-cash-asset classification.

## 8. Read-Only Data Evidence

The audited tenants are AhramStore `c9c5c1ae-949e-498e-bc63-07242daeb49d`, greatone `c43aebf2-4cc4-406a-a2db-4df06d40bce5`, and shopixy `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`. Every captured account cache equaled its posted/reversed ledger-derived balance.

Each tenant has the common active mapping shape: `1010` Cashbox, `1030` Bank, and `1110`-`1140` Clearing. Shopixy currently has `1010 = 73.92`, `1030 = 1,333.00`, `1020 = -3.80`, and clearing balances `14.00`, `0.00`, `15.00`, and `26.00`. The current prefix perimeter is therefore EGP `1,458.12`; canonical Cashbox/Bank is EGP `1,406.92`. The EGP `51.20` difference is exactly clearing EGP `55.00` plus unmapped `1020` EGP `-3.80`.

AhramStore additionally has `T06C-CARD-ASSET = 35.00`, explicitly marked supplier-card funding, with no Treasury mapping. Its funding source is mapped Cashbox `1010`. This confirms that Asset type and business proximity to cash do not grant cash-equivalent authority.

## 9. Cash Flow Target

Resolve qualifying cash-equivalent account IDs once within the report's consistent read transaction. Use that same set to:

- sum opening balances;
- sum closing balances;
- select each journal's cash lines;
- compute `DebitAmount - CreditAmount` movement;
- identify other non-cash assets during activity classification.

Remove the `10xx`/`11xx` cash-equivalent predicate and update the synthetic settlement-adjustment account label so it does not advertise the removed prefixes. Keep the report ledger-derived, tenant-scoped, journal-date-scoped, posted/reversed-inclusive, and reconciled.

## 10. Collection and Settlement Semantics

Collection into Clearing is not yet cash: Dr Clearing / Cr Revenue has zero cash movement, and the ledger-derived bridge offsets indirect profit so operating cash remains zero. A later authenticated settlement to Bank is when cash enters the perimeter.

For Dr Bank net / Dr fee expense / Cr Clearing gross, cash movement is the Bank debit. The report must classify that movement as operating, produce net Bank inflow once, and retain fee expense once. A mapped Clearing counterpart must not cause a no-fee clearing-to-Bank settlement to be mislabeled as investing. Clearing detection may use the existing `Account.IsClearing` reporting metadata; it must not make clearing a cash equivalent.

## 11. Double-Counting Rules

| Scenario | Required cash result |
| --- | --- |
| Cashbox collection/sale | Cash inflow once |
| Clearing collection | Zero cash until settlement |
| Clearing to Bank settlement without fee | Operating Bank inflow once |
| Clearing to Bank settlement with fee | Operating net Bank inflow once; fee not duplicated |
| Cashbox to Bank transfer | Zero net cash movement |
| Cashbox to supplier-card asset funding | Investing cash outflow once |
| Supplier-card purchase | Zero additional cash movement |
| `1020` supplier purchase credit | Zero cash movement |
| Unmapped `10xx`/`11xx` movement | Non-cash behavior determined by counterpart; never cash by prefix |
| Reversal | Exact opposite of the original qualifying cash lines in the reversal period |

## 12. Treasury Boundary

Generic transfers remain Cashbox/Bank-only on both sides. Clearing settlement remains a separate evidence-authorized path and T-10 containment remains unchanged. `1020`, clearing, supplier-card assets, chart parents, and reserve accounts must not gain transfer or mutation authority.

No change is approved in `TreasuryReadService`, `TreasuryTransferService`, `TreasuryOperationService`, `FinancialPostingService`, initial capitalization, supplier-card authority, or payment lifecycle. Their existing behavior is regression evidence for the selected Cash Flow rule.

## 13. Balance Sheet Boundary

Balance Sheet continues to report all nonzero Assets, including Cashbox, Bank, Clearing, `1020`, inventory, receivables, and supplier-card assets. T-11 does not hide, rename, net, or reclassify any asset. Current-asset and current-ratio conventions remain unchanged.

The required reconciliation is conceptual and numeric: canonical Cash Flow closing cash equals qualifying Cashbox plus Bank balances, while Balance Sheet total Assets still includes non-cash current assets. Balance Sheet remains balanced before and after because T-11 writes no ledger fact.

## 14. Trial Balance and General Ledger Boundary

Trial Balance continues to include every posted/reversed line in period and must retain zero debit/credit variance. General Ledger continues to expose full immutable history by same-tenant account ID, including `1020` and clearing.

Neither report assigns cash-equivalent authority. No filtering, new badge, account suppression, running-balance rewrite, or account-code change is approved.

## 15. Frontend Boundary

The current Treasury Hub separately computes `Total Cash & Bank` from `1010` and `1030`, and computes Pending Clearing from `1110`-`1140`; it does not add `1020` to either total. For the audited tenants this agrees exactly with the active mapping set. The Cash Flow page renders backend report values, while Balance Sheet, Trial Balance, and GL render account-level report rows.

No frontend production change is required for this bounded correction. Browser certification must prove the changed Cash Flow values and retained separation. Dynamic multi-Cashbox/Bank Hub cards are a separate scalability improvement and must not be smuggled into T-11.

## 16. Tenant, State, and Failure Semantics

Foreign-tenant mappings and ledger rows must never affect the requested tenant. Inactive or deleted mappings, inactive or deleted linked accounts, non-Asset linked accounts, Clearing mappings, and unknown enum values do not qualify. Query filters must not be the only defense where `IgnoreQueryFilters()` is used.

The report remains read-only. Empty qualifying authority returns a valid zero-cash perimeter, not a prefix fallback. Cancellation and database failures retain normal report-service behavior; no partial state exists because no writes occur.

## 17. Required Tests

Update existing Cash Flow fixtures to create explicit Cashbox/Bank mappings where they intend cash. Replace the obsolete test that asserts clearing is cash. Add focused behavioral coverage for:

- custom nonnumeric mapped Cashbox and Bank accounts qualify;
- `1020`, parent `1000`, reserve `1040`, mapped Clearing, supplier-card asset, and arbitrary `10xx`/`11xx` assets do not qualify;
- inactive/deleted/foreign/non-Asset/unsupported mappings do not qualify;
- collection into clearing is zero until settlement;
- clearing-to-Bank settlement, including fees, is operating and counted once;
- Cashbox-to-Bank transfer nets to zero;
- supplier-card funding/purchase/reversal and the T-06C exact defect remain correct;
- tenant isolation, journal date, posted/reversed handling, working-capital bridge, and `Reconciles` remain correct;
- Balance Sheet, Trial Balance, GL, Account Summary, Treasury transfer eligibility, and T-10 containment regressions remain green.

## 18. Allowed Scope and Verification

Allowed production file: `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`. Allowed test file: `PointOfSale.Tests/Accounting/FinancialReportServiceTests.cs`. A new focused backend test file is allowed only if it materially reduces risk; prefer the existing file. No other production or frontend file is authorized.

Run at minimum:

```text
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~FinancialReportServiceTests"
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~FinancialReport|FullyQualifiedName~TreasuryTransfer|FullyQualifiedName~TreasurySettlement|FullyQualifiedName~SupplierCard|FullyQualifiedName~T10Settlement"
dotnet build "PointOfSale.sln" -c Release --no-restore
dotnet ef migrations has-pending-model-changes --project "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj" --startup-project "PointOfSale.API/PointOfSale.API.csproj" --no-build
git diff --check
```

Baseline `FinancialReportServiceTests` result before T-11 is 31 passed, 0 failed. Do not claim a broader gate passed unless it was actually run and its result captured.

## 19. Implementation Contract

Muse must implement exactly this contract and must not certify T-11.

1. In `GetCashFlowAsync`, resolve once the active, non-deleted, same-tenant Asset account IDs mapped by active, non-deleted `TreasuryAccount` rows of type Cashbox or Bank.
2. Use only those IDs for opening cash, closing cash, per-journal cash lines, cash movement, and non-cash-asset classification. Delete the `10`/`11` prefix cash-equivalent behavior; add no fallback.
3. Preserve tenant predicates on mapping, account, ledger line, and journal. Explicitly enforce mapping/account active and delete state and Asset type.
4. Keep Clearing outside cash equivalents. Ensure a clearing-to-Bank settlement is Operating rather than Investing, using existing clearing metadata without granting Treasury mutation authority.
5. Change the synthetic `Ledger-derived non-cash settlement adjustment` account label from `10xx/11xx` to a truthful non-prefix label.
6. Preserve posted/reversed journal inclusion, `JournalDate` boundaries, indirect working-capital logic, party-opening exclusions, activity aggregation, and reconciliation.
7. Modify only `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs` and `PointOfSale.Tests/Accounting/FinancialReportServiceTests.cs`; prefer the existing test file over adding one. Add no migration, schema, seed, account, mapping, report DTO, API contract, frontend change, journal, data write, package, or dependency.
8. Update existing Cash Flow fixtures to map intended Cashbox/Bank accounts. Add deterministic tests for custom nonnumeric mapped Cashbox/Bank; exclusion of `1020`, `1000`, `1040`, mapped Clearing, supplier-card and arbitrary prefix assets; inactive/deleted/foreign/non-Asset/unsupported mappings; clearing collection and settlement with/without fees; internal transfer; supplier-card funding/purchase/reversal; tenant/date/status boundaries; and reconciliation. Do not weaken T-06C, T-10, transfer, tenant, reversal, or reconciliation assertions.
9. Stop if correct behavior requires changing Treasury/payment/supplier-card authority, persisted data, or files outside the allow-list.
10. Report root cause, exact files and edits, tests/builds with counts, migration result, diff result, limitations, and any deviation. Do not stage or commit.

## 20. Acceptance Matrix

| Gate | Required result |
| --- | --- |
| Canonical cash authority | Only qualifying mapped Cashbox/Bank Asset IDs |
| Prefix inference | Absent from Cash Flow cash classification |
| `1020` | Visible in BS/TB/GL, excluded from cash and Treasury |
| Clearing collection | Zero cash before settlement |
| Clearing settlement | Operating Bank inflow once; fees once |
| Supplier-card asset | Excluded from cash; T-06C flow remains reconciled |
| Internal transfer | Cashbox-to-Bank net zero |
| Mapping state | Inactive/deleted/foreign/non-Asset/unsupported excluded |
| Tenant isolation | No cross-tenant authority or ledger contribution |
| Historical integrity | No journal/account/balance/mapping mutation |
| Balance Sheet | All assets retained and equation balanced |
| Trial Balance / GL | Full immutable facts retained; variance zero |
| Treasury/T-10 | Operation gates and settlement containment unchanged |
| Tests/build | Focused and broader relevant gates pass; Release build passes |
| Migration | None added; no pending model change |
| Runtime/database | Authenticated GET and read-only before/after evidence reconcile |
| Final readiness | Eligible for PASS only after independent SOL review and certification |
