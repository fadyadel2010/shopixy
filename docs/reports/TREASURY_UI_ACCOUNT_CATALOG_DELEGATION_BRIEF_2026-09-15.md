# Treasury UI Account Catalog and Semantic Clarity Delegation Brief - 2026-09-15

## 1. Executive Decision

**Discovery verdict: the backend Treasury authority is correct and sufficient, but the POS Admin presentation is materially misleading and requires a bounded frontend correction.** The current Treasury Hub hardcodes six chart codes and English names, the Finance Dashboard infers Cashbox/Bank/Clearing from numeric code ranges, the Account Viewer infers identity and icons from code constants, and the navigation links directly to fixed codes. These assumptions obscure the distinction between available cash, money in transit, dedicated supplier-card assets, and chart/control assets.

The approved correction is presentation-only. The UI must consume the tenant-scoped `GET /api/treasury/accounts` catalog, treat its `accountType` as the only Treasury role authority, use server-owned names and localized names, and explain non-cash accounting surfaces without granting any new operation. No backend, database, report calculation, financial fact, role authority, or settlement behavior is changed.

## 2. Scope

This task covers Treasury Hub, Finance Dashboard cash-position presentation, Treasury Account Viewer identity, Treasury Transfers selectors, Clearing Aging explanation, Treasury report boundary copy, supplier-card asset explanation, Treasury navigation, English and Arabic localization, and focused frontend tests.

Out of scope: T-01 through T-11 backend redesign, cash-equivalent calculation, account-summary contract changes, chart-of-accounts redesign, `1020` retirement or reclassification, Treasury mappings, supplier-card authority, initial capitalization, provider settlement, migrations, seeds, journals, balances, direct SQL, report math, new APIs, packages, dependencies, and unrelated UI cleanup.

## 3. Safety Boundary

The repository is heavily dirty with existing T-01 through T-11 and unrelated work. Do not reset, restore, clean, stage, commit, reformat wholesale, or overwrite existing changes. In particular, `App.jsx`, `Sidebar.jsx`, both locale files, `TreasuryTransfers.jsx`, and `TreasuryClearingAging.jsx` already contain uncommitted work that must be preserved and edited surgically.

Discovery used PostgreSQL database `PosEcommerceDb_Baseline` under `REPEATABLE READ`, `SET TRANSACTION READ ONLY`, and rollback. Authenticated API verification was GET-only after Development test-token issuance; token issuance may refresh a test session but is not financial authority. No financial/business mutation is authorized.

## 4. Canonical Authority

The UI must apply these rules exactly:

```text
Available cash account = row returned by GET /api/treasury/accounts
                         and accountType is Cashbox or Bank

Clearing account       = row returned by GET /api/treasury/accounts
                         and accountType is Clearing

Supplier-card asset    = instrument returned by the existing supplier-card API;
                         it is a dedicated Asset and is not Treasury cash

Other ledger account   = visible in Balance Sheet, Trial Balance, or General Ledger,
                         but inclusion in a report grants no Treasury or cash authority
```

There is no code-range, account-name, icon, `IsSystem`, parent, balance-sign, or fixed-code fallback for Treasury role. An empty Treasury catalog must render an honest empty state and zero totals. Unknown `accountType` values must fail closed as unavailable for transfer and must not be added to cash or clearing totals.

## 5. Account Classification Matrix

| Account class | Available cash | Pending settlement | Generic transfer | Accounting reports | Required UI meaning |
| --- | --- | --- | --- | --- | --- |
| Active mapped Cashbox | Yes | No | Yes | Yes | Physical cash available now |
| Active mapped Bank | Yes | No | Yes | Yes | Settled bank funds available now |
| Active mapped Clearing | No | Yes | No | Yes | Money in transit; provider/carrier workflow only |
| Dedicated supplier-card Asset | No | No | No | Yes | Restricted card funding for supplier purchases/payments |
| `1020 POS Visa Account` | No | No | No | Yes | Unmapped accounting asset/history; not customer-card clearing and not cash |
| Parent/control account such as `1000` or `1100` | No | No | No | Yes when report rules include it | Classification/control only |
| Reserve or other unmapped Asset such as `1040` | No | No | No | Yes | Non-cash accounting asset unless backend grants another authority |
| Invalid, inactive, deleted, foreign, non-Asset, or unknown mapping | No | No | No | Governed by backend report rules | Never infer authority in the browser |

Account codes remain visible identity and drill-down keys only. They must not determine role, color, totals, status, or available actions.

## 6. Six-Tenant Evidence

The database has six active tenants. The authenticated `GET /api/treasury/accounts` response and read-only SQL evidence agree for every tenant:

| Tenant | Tenant ID | Active accounts | Catalog shape | Available Cashbox + Bank | Clearing | Supplier-card Asset |
| --- | --- | ---: | --- | ---: | ---: | ---: |
| AhramStore | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | 33 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 890.00 | EGP 0.00 | EGP 35.00 |
| AntonNewStore | `d49a1ccd-e8d6-4edb-82d1-9d4629ec672e` | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 0.00 | EGP 0.00 | None |
| AntonStore | `3f59725a-01dd-48cc-9399-cdbb4006678e` | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 0.00 | EGP 0.00 | None |
| greatone | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 5,234.84 | EGP 14.00 | None |
| highquality | `a6c9e71e-dd48-49cb-947a-6f460423360e` | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 13,300.03 | EGP 0.00 | None |
| shopixy | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 10,484.02 | EGP 55.00 | None |

Every catalog response contains only `1010:Cashbox`, `1030:Bank`, and `1110` through `1140:Clearing` in current data. That common shape is evidence, not permission to retain fixed frontend codes. A future tenant-specific custom code must render correctly without a frontend release.

## 7. UI Surface Inventory

| Surface | Current source | Current defect | Required boundary |
| --- | --- | --- | --- |
| `TreasuryHub.jsx` | Fixed `ACCOUNT_CARDS` plus bulk balances | Fixed codes, English names, code-owned role and totals | Dynamic authoritative catalog grouped into available funds and clearing |
| `TreasuryDashboard.jsx` | Balance Sheet asset lines | Numeric ranges classify cash/bank/clearing | Catalog `accountType` only; report metrics remain unchanged |
| `TreasuryAccountViewer.jsx` | Route code plus `ACCOUNT_NAMES` and code switch | Code owns identity, name, icon | Resolve route against catalog; use server name/type; fail closed if absent |
| `TreasuryTransfers.jsx` | `/treasury/accounts` then Cashbox/Bank filter | Authority is correct; copy/localized naming can be clearer | Preserve Cashbox/Bank-only source/destination and show role clearly |
| `TreasuryClearingAging.jsx` | Ledger clearing report | Disabled action is correct; non-cash/evidence boundary needs stronger presentation | State money is in transit and cannot be manually transferred/settled |
| `TreasuryCashFlow.jsx` | Certified T-11 backend report | Correct values but little perimeter explanation | State Cash Flow cash means mapped Cashbox/Bank only |
| `TreasuryBalanceSheet.jsx` | Ledger report | Assets can be mistaken for available cash | State Assets include clearing, supplier-card, controls, and other non-cash assets |
| `TreasuryTrialBalance.jsx` | Ledger report | Treasury route name can imply all rows are cash/Treasury | State this is the complete ledger, not available-cash catalog |
| `TreasuryGeneralLedger.jsx` | Trial Balance account list and ledger report | Selector has no semantic boundary copy | Call it all ledger accounts and preserve complete history |
| `SupplierCardFundingInstruments.jsx` | Dedicated supplier-card API | Accounting flow is present but available-cash exclusion is implicit | Explicitly label card balance as restricted non-cash Treasury asset |
| `Sidebar.jsx` and legacy redirects in `App.jsx` | Fixed account-code routes | Navigation assumes seeded codes | Navigate users to dynamic Hub/reports, not hardcoded account identities |

Profit and Loss does not present asset liquidity and requires no production change.

## 8. Existing API Contract

`GET /api/treasury/accounts` is the presentation authority and already returns:

```text
accountCode
name
nameAr
currentBalance
accountType: Cashbox | Bank | Clearing
isClearing
preventNegative
```

The backend query uses active same-tenant Treasury mappings over active same-tenant Asset accounts, ordered by default/display order/code. The frontend may add a `getTreasuryAccounts({ signal })` wrapper in `treasuryApi.js`; it must not duplicate backend eligibility logic. `currentBalance` from this response is sufficient for Hub and Dashboard account totals. Existing financial-report and supplier-card endpoints remain unchanged.

## 9. Root Cause

Frontend seed conventions were promoted into runtime authority. `TreasuryHub.jsx` owns a fixed six-card catalog, `TreasuryDashboard.jsx` converts account codes to integers and uses numeric ranges, `TreasuryAccountViewer.jsx` owns names/icons by fixed code, and the sidebar directly links to three fixed codes. This works only for the current seed and creates false semantics: code placement appears to mean liquidity, Clearing appears inside a Cash Position chart, and an Asset visible in reports can appear Treasury-authorized.

The fix is not another smarter code table. It is to consume the backend catalog and make each screen disclose whether it shows available cash, pending settlement, restricted card assets, or the complete ledger.

## 10. Treasury Hub Target

Replace the fixed `ACCOUNT_CARDS` and bulk code-balance request with the tenant catalog. Render server-returned accounts in deterministic backend order, grouped as:

1. Available now: Cashbox and Bank. Show separate type labels and a combined available-cash total.
2. In transit: Clearing. Show a separate pending-settlement total and evidence-required explanation.

Use `nameAr` when the active language is Arabic and it is nonblank; otherwise use `name`. Account code remains secondary text. Styling and icons may be selected by `accountType`, never by code. Clearing cards can show pending status when nonzero but must never imply spendable balance. Empty/error/loading/refresh states must remain usable. Do not fetch balances separately when catalog rows already contain them.

## 11. Finance Dashboard Target

Load the Treasury catalog alongside the existing financial reports. Compute the displayed Cashbox, Bank, available Cashbox+Bank, and Clearing values exclusively by `accountType`. Delete `parseInt(accountCode)` and every numeric-range classifier.

Do not put Clearing inside a chart or heading called Cash Position without explicit separation. The primary liquidity number is available Cashbox + Bank. Clearing is a separate in-transit amount. Revenue, expenses, profit, ratios, Balance Sheet, Trial Balance, and Clearing Aging calculations remain backend report values and must not be recomputed or changed.

## 12. Viewer and Report Target

The Account Viewer must load the catalog, resolve the route code to a returned row, and use server-owned localized name and `accountType`. It must show a concise role badge and explanation: Cashbox is physical cash, Bank is settled bank funds, Clearing is money in transit. If the code is absent, do not call balance/movement endpoints and do not invent `Account {code}`; render a localized unavailable/not-authorized state with navigation back to the Hub or General Ledger.

Cash Flow must explain that its certified cash perimeter is mapped Cashbox and Bank only. Balance Sheet must explain that Asset rows are broader than available cash. Trial Balance and General Ledger must explain that they contain complete accounting history, including clearing, supplier-card assets, `1020`, control parents, reserves, and other non-cash accounts, and that report inclusion grants no transfer or settlement authority. Preserve all report rows, amounts, exports, selectors, and drill-downs.

## 13. Transfer and Settlement Boundary

Treasury Transfers must continue to expose only catalog rows whose exact `accountType` is `Cashbox` or `Bank` for both source and destination. Preserve backend validation and operation payloads. Use the localized server name in selectors and identify each option as Cashbox or Bank. Unknown, Clearing, supplier-card, unmapped, and report-only accounts must never appear even if their code or name resembles cash.

Clearing Aging remains read-only. Its action stays disabled and must explain that authenticated provider/carrier evidence and the payment lifecycle are required. Do not add navigation state to Transfers, settlement buttons, POST calls, fake evidence, manual journals, or alternate money movement.

## 14. Supplier-Card Boundary

Preserve existing supplier-card access roles `Owner`, `Admin`, `Manager` and administration roles `Owner`, `Admin`. Preserve all create/update/fund/statement behavior and existing API calls. Add only concise semantic copy/badging that a card balance is a dedicated restricted Asset for supplier purchases/payments, not available Cashbox/Bank, customer-card Clearing, or a generic Treasury transfer destination.

Do not merge supplier-card instruments into `GET /api/treasury/accounts`, Hub available-cash totals, Dashboard cash position, or Treasury Transfers. A link from the Hub to the existing supplier-card page is allowed if role-safe and clearly presented as a separate restricted asset workspace.

## 15. Navigation, Roles, and Failure Semantics

Remove fixed `1010`, `1030`, and `1110` child links from the Treasury sidebar or replace them with authority-neutral destinations such as Hub, Clearing Aging, and General Ledger. Redirect legacy cashbox/bank/Visa statement routes to an appropriate dynamic read surface rather than a fixed account code. Preserve the existing supplier-card and initial-capitalization route guards and do not broaden any role.

Do not redesign backend authorization. Treasury catalog operations remain server-enforced `Owner,Admin,Manager`; financial reports retain their current authenticated policy. A catalog fetch failure must not fall back to codes, names, stale hardcoded cards, Balance Sheet ranges, or report rows. Show a localized retry/error state. Request cancellation must not produce user-facing errors or stale state.

## 16. Localization, Accessibility, and Responsive Behavior

Add complete English and business-quality Arabic strings for every new heading, type label, explanation, empty state, error, badge, and navigation label. Do not rely on English fallback strings as the Arabic implementation. Use logical alignment (`text-start`/`text-end`) where touched and preserve `dir`/RTL behavior.

Cards and summaries must work at 390px mobile width and desktop width without hidden meaning or horizontal page overflow. Tables may retain intentional internal horizontal scrolling. Interactive cards/buttons need keyboard semantics, visible focus behavior, and accessible labels; do not use clickable `div` elements without equivalent keyboard support. Do not use color or icons as the only account-role signal.

## 17. Required Tests

Add focused Vitest/Testing Library coverage that proves:

1. Hub cards and totals come from arbitrary custom account codes and `accountType`, not the seeded constants.
2. Hub separates Cashbox/Bank from Clearing and uses Arabic `nameAr` with English fallback.
3. Dashboard classification contains no numeric code parsing/ranges and excludes Clearing from available cash.
4. Viewer uses catalog identity, shows role semantics, and makes no balance/movement call for an absent mapping.
5. Transfers offers only exact Cashbox/Bank rows and excludes Clearing/unknown rows.
6. Clearing Aging retains the disabled evidence-required action and no transfer/settlement execution path.
7. Supplier-card copy states restricted non-cash semantics while existing role and API tests remain green.
8. Navigation no longer directly depends on fixed Treasury account codes.
9. English/Arabic locale keys remain structurally valid.

Prefer a small shared presentation helper only if it removes duplicated role/localized-name logic. Do not create a frontend shadow authority catalog.

## 18. Allowed Scope and Verification

Allowed production files:

```text
PointOfSale.Web/src/App.jsx
PointOfSale.Web/src/components/Sidebar.jsx
PointOfSale.Web/src/i18n/locales/en.json
PointOfSale.Web/src/i18n/locales/ar.json
PointOfSale.Web/src/services/treasuryApi.js
PointOfSale.Web/src/pages/treasury/TreasuryHub.jsx
PointOfSale.Web/src/pages/treasury/TreasuryDashboard.jsx
PointOfSale.Web/src/pages/treasury/TreasuryAccountViewer.jsx
PointOfSale.Web/src/pages/treasury/TreasuryTransfers.jsx
PointOfSale.Web/src/pages/treasury/TreasuryClearingAging.jsx
PointOfSale.Web/src/pages/treasury/TreasuryCashFlow.jsx
PointOfSale.Web/src/pages/treasury/TreasuryBalanceSheet.jsx
PointOfSale.Web/src/pages/treasury/TreasuryTrialBalance.jsx
PointOfSale.Web/src/pages/treasury/TreasuryGeneralLedger.jsx
PointOfSale.Web/src/pages/SupplierCardFundingInstruments.jsx
```

One new helper under `PointOfSale.Web/src/pages/treasury/` or `PointOfSale.Web/src/services/` is allowed only if justified. New or updated focused frontend tests adjacent to these files and under `src/architecture-tests/` are allowed. Do not modify any backend, Core, Infrastructure, migration, database, generated `dist`, package, lockfile, or unrelated source.

Run and make green before finishing:

```text
npm test -- --run <all focused Treasury UI tests plus existing SupplierCardFundingInstruments.test.tsx, supplierCardFundingApi.test.ts, and treasuryInitialCapitalizationAuthority.test.ts>
npm run build
git diff --check -- PointOfSale.Web/src/App.jsx PointOfSale.Web/src/components/Sidebar.jsx PointOfSale.Web/src/i18n/locales/en.json PointOfSale.Web/src/i18n/locales/ar.json PointOfSale.Web/src/pages/treasury PointOfSale.Web/src/pages/SupplierCardFundingInstruments.jsx PointOfSale.Web/src/services/treasuryApi.js
```

The scoped ESLint baseline is unavailable because the repository has ESLint 9 but no `eslint.config.js`; report that fact and do not add/configure ESLint under this task. Pre-delegation baseline: Vite build passed with 5,216 modules and only the existing large-chunk warning; three relevant test files passed, 21 tests total; scoped `git diff --check` passed.

## 19. Implementation Contract

Muse must implement exactly this contract and must not certify the result.

1. Add a cancelable `getTreasuryAccounts` service wrapper and make Hub, Dashboard, Viewer, and Transfers consume the same backend-owned account shape. Do not add backend fields or infer missing ones.
2. Delete the Hub fixed card catalog and Dashboard numeric-range classification. Role-based UI treatment may use only exact `accountType` values.
3. Use localized server-owned account names: Arabic `nameAr` when active and nonblank, otherwise `name`. Keep account code as secondary identity only.
4. Render Available Cashbox + Bank and Pending Clearing as distinct sections/totals. Never include Clearing or supplier-card assets in available cash.
5. Make the Viewer validate the route against the catalog before requesting balance/movements; show truthful fail-closed state for absent mappings.
6. Preserve Transfers Cashbox/Bank-only semantics and Clearing Aging T-10 fail-closed evidence boundary. Add no financial action or endpoint.
7. Add concise Cash Flow, Balance Sheet, Trial Balance, General Ledger, and supplier-card explanations from sections 12 through 14 without changing report data or operations.
8. Remove fixed Treasury account-code navigation dependencies while preserving existing route guards and unrelated route behavior.
9. Add complete English/Arabic localization, accessible interactions, responsive layout, and deterministic focused tests from section 17.
10. Stop if correct completion requires backend/API/schema/report-calculation/role-authority changes, data mutation, files outside the allow-list, or weakening existing tests. Report exact files, changes, test/build counts, lint baseline, diff check, limitations, and deviations. Do not stage or commit.

Pre-delegation SHA-256 anchors for production files most likely to change:

| File | SHA-256 |
| --- | --- |
| `App.jsx` | `ACA324785B01D7C07AD5F4965334DAE7E19ECA2011A5C4AF2A317C4D44639B0A` |
| `Sidebar.jsx` | `E24829D5EE609476AA1FFF54FB741EA5CB6A6AD697A2DCBFEBB39F0D239D6066` |
| `en.json` | `177BBC668AECCE7429D99F7C7FB5A41ACFE0F5EEF333E86C1E3CE4DCB15AE132` |
| `ar.json` | `C144883C4984C4EE2038AF91C85DDF44AB3B3FC3B7B01E1DF07F4EE424A556C6` |
| `TreasuryHub.jsx` | `9E1AE666A7AD8FA708643DA3BEADBEC5DAA2604C50BE55D296BFB68646A38A4E` |
| `TreasuryDashboard.jsx` | `49F69CE7EEBC48B095C4D10537B648003FC0AA7737B785EB239C8C6137DF02EB` |
| `TreasuryAccountViewer.jsx` | `136E89012F618EA34E588EC5BBE9D59879378FC87D6E2A04624BD14A5BED639D` |
| `TreasuryTransfers.jsx` | `A1BAC3E87F0D5BFCAC5356AD0D8ED2F4CD0C7C06CA0B61BED8F7DB0DF3404AF8` |
| `TreasuryClearingAging.jsx` | `46A47E125F3F1A152342332985CEC64DD7E6F80A3D4F5534234060E7D89A7B09` |
| `TreasuryGeneralLedger.jsx` | `DAC8522D37B1F5EC9A8C4785C68FC393BE700C5CC5C24EA314D2FD2C015DC7E2` |
| `TreasuryBalanceSheet.jsx` | `71569A2998768696BA9629DFCCEA05157E17AA26681F9C02AE2C3891EF27E64B` |
| `TreasuryCashFlow.jsx` | `92F7D54782654A3ED729A91F174E44FDEE90C08BEA8817E097F2189C7C5AB514` |
| `TreasuryTrialBalance.jsx` | `C58694A9E6B8E1CF9C4BED15F5D00DD02A635F9EBCC63B3957FE7D7A58D305A1` |
| `treasuryApi.js` | `17DD86A9F4AA1E2916F22AF10AF327AECD0482E70DC27184E0990D79F7E80062` |
| `SupplierCardFundingInstruments.jsx` | `4FEED6555567F53894841855A2B6D099FFA0F7EAC345CB3BCA0F7CFC726E9ED0` |

## 20. Acceptance Matrix

| Gate | Required result |
| --- | --- |
| Treasury catalog | Runtime rows come from `GET /api/treasury/accounts` |
| Cash authority | Exact `Cashbox`/`Bank` account types only |
| Clearing | Separate in-transit section; never available cash or generic transfer |
| Supplier card | Separate restricted Asset; never Cashbox/Bank/Clearing |
| `1020` and controls | Report history retained; no Treasury/cash implication |
| Code inference | No fixed catalog, numeric ranges, name matching, or code-owned icons/actions |
| Localized identity | Server `nameAr` in Arabic with server `name` fallback |
| Hub | Dynamic arbitrary-code cards, truthful totals, empty/error states |
| Dashboard | Cash position agrees with catalog; financial report metrics unchanged |
| Viewer | Catalog-backed identity; absent mappings fail closed before balance/movement GETs |
| Transfers | Only exact Cashbox/Bank options on both sides |
| Settlement | T-10 disabled evidence-required boundary unchanged |
| Reports | All rows/amounts/exports retained with clear non-authority explanation |
| Navigation | No direct dependency on fixed Treasury account codes |
| Roles | No broadened access or administration authority |
| RTL/mobile/a11y | Arabic and English usable at 390px and desktop; keyboard semantics retained |
| Tests/build | Focused tests and Vite build pass; existing relevant tests remain green |
| Migration/data | No backend, migration, schema, journal, balance, or business-data change |
| Worktree | Existing unrelated changes preserved; only allow-listed task files added/edited |
| Final readiness | Eligible for PASS only after independent SOL diff review, gates, API/DB reconciliation, and browser certification |
