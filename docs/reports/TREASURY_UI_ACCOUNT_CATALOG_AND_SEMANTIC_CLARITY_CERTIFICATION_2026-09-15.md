# Treasury UI Account Catalog and Semantic Clarity Certification - 2026-09-15

## 1. Executive Verdict

**Treasury UI account catalog and semantic clarity: PASS.**

The POS Admin Treasury surfaces now use the tenant-scoped `GET /api/treasury/accounts` catalog as their presentation authority. Exact `Cashbox` and `Bank` types define available cash; exact `Clearing` defines separately disclosed money in transit. Account codes, names, icons, numeric ranges, balances, and accounting-report visibility no longer grant Treasury role or action authority.

Independent certification passed 47/47 focused frontend tests, the Vite production build, scoped whitespace validation, all authenticated business API reads, a 44/44 Microsoft Edge English/Arabic desktop/mobile matrix, and a six-tenant read-only database before/after comparison. No financial fact, role, report calculation, API contract, backend source, schema, migration, package, or dependency changed.

## 2. Certification Scope

This certification covers Treasury Hub, Finance Dashboard Treasury totals, Treasury Account Viewer identity, Treasury Transfer selectors, Clearing Aging semantics, Cash Flow, Balance Sheet, Trial Balance, General Ledger boundary copy, supplier-card asset explanation, Treasury navigation, request failure behavior, English/Arabic localization, accessibility, responsive behavior, and focused frontend tests.

T-01 through T-11 backend authority, report calculations, chart redesign, `1020` retirement, supplier-card business behavior, initial capitalization, provider settlement, migrations, seeds, journals, balances, direct SQL writes, and unrelated UI work remain outside scope.

## 3. Safety Boundary

Repository HEAD remained `0de1c542bd316026de06bd5cc8f7111878b1b0e6`. The worktree already contained substantial uncommitted Treasury and unrelated work. No reset, restore, clean, stage, commit, amend, or modification of unrelated work was performed.

Runtime certification used Development test-token issuance only for authentication. All subsequent business API requests and database evidence collection were read-only. The browser harness blocked non-GET/HEAD/OPTIONS requests under `/api/**`; no business mutation was attempted or blocked.

## 4. Discovery and Root Cause

The backend catalog and financial authority were already sufficient. Presentation code had promoted seeded chart conventions into runtime authority: Hub owned a fixed six-card catalog, Dashboard used parsed numeric ranges, Viewer owned fixed names and icon rules, and navigation linked directly to seeded account codes.

Those assumptions could make a report-only Asset appear liquid, put Clearing inside available cash, fail to display a future custom-coded Treasury account, or imply that `1020`, controls, reserves, and supplier-card assets had Treasury authority. The correct repair was to consume the existing catalog, not to create a smarter frontend code table.

## 5. Canonical UI Authority

The certified browser rule is:

```text
Available cash = catalog rows with exact accountType Cashbox or Bank
In transit     = catalog rows with exact accountType Clearing
Transferable   = catalog rows with exact accountType Cashbox or Bank
Unavailable    = absent catalog rows or unknown accountType values
```

`accountCode` remains a drill-down and display identity only. Unknown values fail closed. An empty catalog produces zero totals and guidance. A catalog failure does not fall back to report rows, fixed codes, fixed names, numeric ranges, stale cards, or inferred actions.

## 6. Account Classification

| Account class | Available cash | Pending settlement | Generic transfer | Accounting reports |
| --- | --- | --- | --- | --- |
| Exact mapped Cashbox | Yes | No | Yes | Yes |
| Exact mapped Bank | Yes | No | Yes | Yes |
| Exact mapped Clearing | No | Yes | No | Yes |
| Supplier-card Asset | No | No | No | Yes |
| Unmapped `1020` | No | No | No | Yes |
| Control, reserve, or other Asset | No | No | No | Per backend report rules |
| Unknown, absent, inactive, deleted, foreign, or invalid mapping | No | No | No | Per backend report rules |

No account name, code position, icon, balance sign, parent, `IsSystem`, or report inclusion overrides this matrix.

## 7. Delegation and Independent Review

Implementation was delegated exactly once to Muse using `opencode/muse-spark-1.3-contributor-free`, OpenCode `1.18.31`, session `ses_f5ad074afffeMPocOhvTaqai4B`. The frozen exactly 20-section contract is `docs/reports/TREASURY_UI_ACCOUNT_CATALOG_DELEGATION_BRIEF_2026-09-15.md`.

Muse implemented the bounded frontend change and focused tests but did not certify it. Independent SOL review inspected the complete scoped change, ran adversarial tests and live certification, and made corrective edits before granting this verdict.

## 8. Independent Corrective Review

Independent review corrected material issues before certification:

- Dashboard Pending Clearing initially used a financial report value rather than the catalog.
- Viewer initially accepted a catalog row whose `accountType` was unknown.
- Catalog failures were initially conflated with independently loaded report failures.
- Transfer type labels, validation text, provider-workflow copy, and Dashboard title were incompletely localized.
- General Ledger's blank selector implied an active all-account query rather than a required selection.
- Fixed Bank Statement navigation remained semantically misleading.
- Mobile Cash Flow and Trial Balance controls overflowed at 390px; Trial Balance and Dashboard summaries also collided.
- The Hub supplier-card shortcut was removed because unconditional Hub access must not become a role-guard bypass.

The final tests and browser matrix cover the corrected behavior rather than the delegated intermediate state.

## 9. Repository Files

Production edits are limited to the allow-listed frontend surfaces: `App.jsx`, `Sidebar.jsx`, both locale files, `treasuryApi.js`, the Treasury Hub/Dashboard/Viewer/Transfers/Clearing Aging/Cash Flow/Balance Sheet/Trial Balance/General Ledger pages, and the existing supplier-card funding page.

One small shared helper, `PointOfSale.Web/src/services/treasuryAccountCatalog.js`, centralizes exact type partitioning, known-type checks, and localized server-name selection. Focused tests were added beside the affected pages/services and under `src/architecture-tests/`. The temporary `.opencode-tmp/treasury-ui-browser` and `.opencode-tmp/t11-discovery` projects are local evidence harnesses, not application dependencies or production code.

## 10. Catalog Service Boundary

`getTreasuryAccounts({ signal })` was added to `treasuryApi.js` and forwards cancellation to the existing API client. It returns the existing backend response shape without inventing fields or duplicating backend eligibility logic.

Hub, Dashboard, Viewer, and Transfers consume that wrapper. The presentation helper accepts only exact `Cashbox`, `Bank`, and `Clearing` values; unsupported values are retained only as untrusted input and receive no cash, clearing, viewer, or transfer authority.

## 11. Treasury Hub

Hub now renders every returned catalog row in backend order and groups exact Cashbox/Bank rows under available funds and exact Clearing rows under pending settlement. It calculates each total directly from catalog `currentBalance`; no separate fixed-code balance fetch remains.

Cards use server `nameAr` in Arabic when nonblank and otherwise server `name`, with account code shown only as secondary identity. Loading, cancellation, empty, error, retry, and refresh states are explicit. Catalog failure withholds catalog-backed rows and totals instead of fabricating a partial cash position from the independently loaded Treasury dashboard report.

## 12. Finance Dashboard

Dashboard loads the Treasury catalog alongside existing financial reports and calculates Cashbox, Bank, combined available cash, and Clearing exclusively from exact catalog types. Numeric parsing and range-based role classification were removed.

Available Cashbox plus Bank is the liquidity figure. Pending Clearing is separately labeled money in transit and excluded from available cash. Revenue, expense, profit, ratios, Balance Sheet, Trial Balance, and Clearing Aging values remain backend report values. A catalog failure withholds only catalog-backed Treasury totals while independently successful report metrics remain visible.

## 13. Account Viewer

Viewer first loads the catalog and resolves the route code against a row with a known exact type. Only then may it request `/api/treasury/balance/{code}` and `/api/treasury/movements/{code}`. It uses the server-owned localized name and an explicit Cashbox, Bank, or Clearing role explanation.

An absent row or unknown type renders the localized unavailable state and makes no balance or movement request. Live certification used `/treasury/account/1020` to prove this fail-closed ordering because `1020` is a ledger Asset but is absent from the Treasury catalog.

## 14. Treasury Transfers

Both source and destination selectors are populated only with catalog rows whose exact `accountType` is `Cashbox` or `Bank`. Clearing, unknown, supplier-card, unmapped, control, reserve, and report-only rows are excluded regardless of code or name.

Options display the localized server name, account code, and localized type label. Catalog loading is cancelable; error and retry states fail closed. The existing transfer payload and backend validation remain unchanged, and certification submitted no transfer.

## 15. Clearing Aging

Clearing Aging remains a read-only evidence surface. The UI identifies balances as money in transit and states that provider/carrier evidence and the payment lifecycle are required before backend settlement authority can be exercised.

The provider-workflow control remains disabled. There is no route to generic Treasury Transfers, settlement POST, fake evidence, manual journal, navigation state, or alternative money-movement path. Browser assertions proved the disabled control and absence of transfer navigation in all four language/viewport combinations.

## 16. Accounting Report Boundaries

Cash Flow now explains that its certified cash perimeter is mapped Cashbox and Bank only. Balance Sheet explains that Asset rows are broader and can include Clearing, supplier-card funding assets, controls, reserves, and other non-cash assets.

Trial Balance and General Ledger explain that they preserve complete accounting history, including `1020`, Clearing, controls, reserves, supplier-card assets, and other non-cash rows; report inclusion grants no transfer, settlement, or available-cash authority. Report values, exports, rows, date behavior, and drill-down behavior were not recalculated or removed. General Ledger retains its required account selection instead of falsely presenting a blank selection as an all-accounts query.

## 17. Supplier-Card Boundary

The supplier-card funding page now labels card balances as dedicated restricted Assets for supplier purchases and payments. They are not available Cashbox/Bank funds, customer-card Clearing, or generic Treasury transfer destinations.

Existing access roles remain `Owner`, `Admin`, and `Manager`; administration remains `Owner` and `Admin`. Existing create, update, fund, statement, and API behavior was preserved. Supplier-card rows are not merged into the Treasury catalog, Hub total, Dashboard cash position, or transfer selectors.

## 18. Navigation and Roles

The Treasury sidebar now uses authority-neutral destinations such as Hub, Clearing Aging, reports, and Transfers rather than fixed `1010`, `1030`, or `1110` account links. The misleading Bank Statement child entry was removed.

Legacy cashbox and bank-statement routes redirect to `/treasury/hub`; the legacy Visa statement redirects to `/treasury/clearing-aging`. Existing supplier-card and initial-capitalization route guards remain unchanged. No catalog, report, supplier-card, settlement, or administration role was broadened.

## 19. Failure and Cancellation Semantics

Catalog consumers use abort signals and do not surface request cancellation as an error. Loading, empty, error, retry, and successful states remain distinct. Unknown type and absent mapping behavior is fail closed rather than permissive.

Hub and Dashboard use independently settled requests so an unrelated report failure cannot erase a successful catalog, while a catalog failure cannot manufacture Treasury totals from report data. Transfers expose no selector options on catalog failure. Viewer performs no downstream account reads until catalog identity and type are validated.

## 20. Localization

All new headings, account types, explanations, statuses, empty/error states, transfer validation, provider workflow text, report boundaries, and navigation labels have English and business-quality Arabic entries. Dashboard uses the dedicated `treasury.financeTitle` value: `Treasury & Finance` in English and `الخزينة والمالية` in Arabic.

Localized account identity prefers nonblank server `nameAr` in Arabic and falls back to server `name`; it does not fall back to a code-owned frontend name. Architecture tests parse both locale files and verify Treasury key parity.

## 21. Accessibility and Responsive Behavior

Dynamic account cards are keyboard-operable controls with accessible names and visible role text; color and icon are not the only role signal. Existing route direction changes are preserved, and touched alignment uses direction-aware presentation.

Microsoft Edge verified every certified surface at `1440x1000` and `390x844` in LTR English and RTL Arabic. All 44 cases reported `noVisiblePageOverflow=true`; tables may retain intentional internal scrolling. Cash Flow controls, Trial Balance controls/status, and Dashboard summaries no longer create mobile page overflow or overlap.

## 22. Focused Tests

The final focused command ran 11 files and passed **47/47** tests. Coverage includes arbitrary account codes, exact type partitioning, Arabic server-name fallback, report/catalog disagreement, independent request failure behavior, empty catalogs, unknown types, Viewer request ordering, transfer eligibility, disabled clearing workflow, supplier-card disclosure, navigation decoupling, cancellation forwarding, locale parity, and preserved initial-capitalization authority.

The final assertion repair selected the Dashboard title by accessible heading name rather than brittle raw text, accommodating the decorative heading icon without weakening behavior.

## 23. Build, Lint, and Diff Gates

| Gate | Result |
| --- | --- |
| Focused Vitest slice | PASS, 11 files and 47 tests |
| `npm run build` | PASS, 5,217 modules transformed |
| Scoped `git diff --check` | PASS; existing LF-to-CRLF notices only |
| Scoped ESLint | UNAVAILABLE: ESLint 9.39.4 found no `eslint.config.js`, `.mjs`, or `.cjs` |
| Browser harness build | PASS, 0 warnings and 0 errors |

The Vite build retained the repository's existing large-chunk warning. No ESLint configuration was added because doing so was explicitly outside this task.

## 24. Six-Tenant SQL and Catalog Evidence

Read-only SQL and authenticated catalog evidence agree across every active tenant:

| Tenant | Active accounts | Catalog shape | Available cash | Clearing | Supplier-card Asset |
| --- | ---: | --- | ---: | ---: | ---: |
| AhramStore | 33 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 890.00 | EGP 0.00 | EGP 35.00 |
| AntonNewStore | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 0.00 | EGP 0.00 | None |
| AntonStore | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 0.00 | EGP 0.00 | None |
| greatone | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 5,234.84 | EGP 14.00 | None |
| highquality | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 13,300.03 | EGP 0.00 | None |
| shopixy | 32 | 1 Cashbox, 1 Bank, 4 Clearing | EGP 10,484.02 | EGP 55.00 | None |

The currently common codes are evidence only. Custom-code tests prove the frontend does not depend on those seeded identities.

## 25. Authenticated API Runtime

The live API on `http://localhost:5010` and Vite application on `http://localhost:5173` served the certified flow. `/health/live` and Treasury Hub returned HTTP 200. Authenticated requests to the Treasury catalog, Treasury dashboard, financial dashboard, Cash Flow, Balance Sheet, Trial Balance, General Ledger support data, Clearing Aging, Viewer balance/movements, and supplier-card reads succeeded.

`/health/ready` returned HTTP 503 because unrelated configured dependencies were unhealthy. This did not affect the SQL-backed authenticated business routes and is not relabeled as a pass.

## 26. Desktop Browser Certification

Headless Microsoft Edge certified 22 desktop cases at `1440x1000`: eleven routes in English and the same eleven in Arabic. Hub totals matched the live greatone catalog at EGP `5,234.84` available and EGP `14.00` Clearing; Dashboard matched those values and separated non-cash Clearing.

Viewer proved catalog-first request order and `1020` fail-closed behavior. Both transfer selectors contained available types only. Clearing Aging remained disabled. All report notes and the AhramStore supplier-card restricted-asset boundary rendered. Every route was authenticated and directionally correct, with no visible overflow, failed API response, request failure, unexpected console error, or business mutation.

## 27. Mobile Browser Certification

The same 22 cases passed at `390x844` in English LTR and Arabic RTL. Dynamic account cards, totals, report controls, status panels, selector options, long boundary text, and supplier-card disclosure remained usable without visible page overflow.

The complete matrix result was **44 passed, 0 failed**. Each page emitted only Chromium's known CSP diagnostic that `frame-ancestors` is ignored when delivered through a meta element; the harness classified it as a known baseline diagnostic, not a page exception.

## 28. Database Before and After

The pre-runtime snapshot was captured in `PosEcommerceDb_Baseline` at `2026-09-15T16:02:40.599137+03:00`; the post-browser snapshot was captured at `2026-09-15T17:42:59.132641+03:00`. Both used `REPEATABLE READ`, `SET TRANSACTION READ ONLY`, and rollback.

`git diff --no-index` between `C:\Users\fadya\AppData\Local\Temp\opencode\treasury-ui-all-tenants-evidence.json` and `C:\Users\fadya\AppData\Local\Temp\opencode\treasury-ui-all-tenants-evidence-post-browser.json` showed only `serverTime`. Tenants, accounts, Treasury mappings, cached and ledger balances, recent ledger usage, payment-method authority, supplier-card instruments, legacy finance counts, and legacy account rows were otherwise byte-for-byte identical.

## 29. Preservation and Residual Risks

Pre-delegation SHA-256 anchors in the frozen brief recorded the dirty handoff for every likely production file. Final review inspected all task diffs against that boundary: production edits stayed within the allow-list; test, report, and temporary evidence additions stayed within their approved locations; no backend, Core, Infrastructure, model snapshot, migration, generated `dist`, package, or lockfile change was introduced by this task. Existing unrelated changes remain untouched.

Residual environment issues are limited to the unavailable ESLint 9 flat configuration, the existing Vite large-chunk warning, the known meta-delivered CSP diagnostic, and aggregate readiness HTTP 503 from unrelated dependencies. Current live tenants have the same seeded catalog shape, so arbitrary-code runtime display is proved deterministically by tests rather than a production custom-coded tenant.

## 30. Final Classification and Readiness

**Final classification: PASS.**

The tenant Treasury catalog now owns frontend Treasury identity and classification. Exact Cashbox/Bank rows alone define available cash and generic transfer choices; exact Clearing rows remain separate money in transit; unknown and absent rows fail closed; `1020`, controls, reserves, supplier-card Assets, and report-only accounts gain no Treasury authority. Reports retain complete accounting visibility, provider settlement remains evidence-controlled and disabled in this UI, supplier-card and capitalization roles remain intact, and English/Arabic desktop/mobile behavior is certified.

The change is eligible to proceed. Focused tests and production build pass, 44/44 authenticated browser cases pass, all six tenant catalog values reconcile to read-only SQL, and the final database snapshot differs from the pre-run evidence only by timestamp. No staging or commit was performed.
