# Manual Wallet Settlement — Sidebar Cleanup Report (2026-10-21)

## A. Executive verdict

**PASS** for the implemented scope.

The standalone **Manual Wallet Settlement / تسوية المحفظة الإلكترونية** entry was removed from the POS Admin Sidebar. The wallet settlement workflow now follows the **exact Manual COD Settlement navigation pattern**: route kept, page kept, no dedicated menu entry, reachable only from **Treasury → Clearing Aging** (header button + per-`1140` Wallet-Clearing row action).

No route, permission guard, backend endpoint, accounting service, locale key, page, button, balance, order, journal, or database record was changed. No settlement was confirmed during verification and a read-only SQL check proved no new settlement batch was written.

This verdict covers the **focused selection** executed for this task (Sidebar/navigation + Treasury settlement tests, Admin typecheck, Admin production build, focused lint, browser verification). The full repository test suite was **not** executed and is not claimed.

---

## B. Scope and environment

| Item | Value |
|---|---|
| Workspace | `C:\Users\fadya\source\repos\PointOfSale` (primary dev workspace only) |
| Admin web | Vite `http://localhost:5173` (HMR served the edit; no API rebuild/restart needed) |
| Development API | `http://localhost:5010` (already running; untouched by this UI-only change) |
| Tenant | AhramStore, `c9c5c1ae-949e-498e-bc63-07242daeb49d`, EGP |
| Browser | Registered headed Edge session `opencode-ui-test` (per `AGENTS.md`; no orphan window created) |
| Authenticated actor | Owner account supplied by the user (credentials intentionally not reproduced here) |

---

## C. Changed files

Exactly **one** file was modified in this task:

| File | Change |
|---|---|
| `PointOfSale.Web/src/components/Sidebar.jsx` | Removed the single Treasury menu line `{ path: '/treasury/manual-wallet-settlements', label: t('manualWalletSettlement.title'), icon: '👛', permission: 'storefront.payments.manage' }` and left a clarifying comment: `// Manual COD/Wallet settlement are reachable from Treasury / Clearing Aging only (no dedicated menu entry).` |

Final state of the Treasury group (lines 180-191):

```jsx
children: [
    // All Ledger-based routes (no fixed account-code identities)
    { path: '/treasury', label: t('nav.financeDashboard'), icon: '📊', permission: 'reports.financial.view' },
    { path: '/treasury/hub', label: t('nav.treasuryHub'), icon: '🏦', permission: 'finance.view' },
    { path: '/treasury/clearing-aging', label: t('treasury.clearingNav', 'Clearing Aging'), icon: '⏳', permission: 'reports.financial.view' },
    // Manual COD/Wallet settlement are reachable from Treasury / Clearing Aging only (no dedicated menu entry).
    { path: '/treasury/general-ledger', label: t('treasury.generalLedgerNav', 'General Ledger'), icon: '📖', permission: 'reports.financial.view' },
    ...
```

`Select-String` over the file confirms **zero** remaining `manual-wallet` / `manualWallet` references in `Sidebar.jsx`.

### Deliberately NOT changed (preserved)

| Area | Artifact | Status |
|---|---|---|
| Route | `PointOfSale.Web/src/App.jsx:542` → `treasury/manual-wallet-settlements` | unchanged, guard `storefront.payments.manage` intact |
| Clearing Aging actions | `PointOfSale.Web/src/pages/treasury/TreasuryClearingAging.jsx` — header button + per-`1140` row action | unchanged |
| Wallet page | `PointOfSale.Web/src/pages/treasury/TreasuryManualWalletSettlement.jsx` (+ `.d.ts`, `.test.tsx`) | unchanged |
| API client | `PointOfSale.Web/src/services/manualWalletSettlementApi.js` | unchanged |
| Backend | `PointOfSale.Infrastructure/Accounting/ManualWalletSettlementAuthority.cs`, `PointOfSale.Tests/Integration/ManualWalletSettlementTests.cs` | unchanged |
| i18n | `manualWalletSettlement.*` keys in `en.json` / `ar.json` | unchanged (still used by Clearing Aging + the page) |
| COD workflow | `TreasuryManualCodSettlement.jsx` + its Clearing Aging action | unchanged |

---

## D. Smart-navigation registry inspection (performed **before** editing)

`PointOfSale.Web/src/navigation/navigationRegistry.js` builds the smart-navigation / command-palette registry **from the Sidebar menu** (`buildNavigationRegistry` → `buildAdminMenu(...)` → `matchRoutes(routes, to)`).

Consequences verified before and after the edit:

- The registry entry for `/treasury/manual-wallet-settlements` was **derived from the Sidebar item**, not registered independently.
- **Manual COD Settlement was already in exactly the same state** — present only as a route in `App.jsx` plus a Clearing Aging button; it had **no** Sidebar entry and **no** independent command-palette registration.
- Therefore removing the Sidebar line brings the wallet workflow to parity with COD, and no *independent* command-palette destination was deleted. No compensating Sidebar or palette entry was added (per instruction).
- `navigationRegistry.test.tsx` asserts a **1:1 menu-destination ↔ registry-entry** mapping and a floor (`entries.length > 90`); it passes after the removal.
- The palette's "All pages" total is now **102** — one fewer than before, which is exactly the single removed Sidebar-derived entry. No other destination was lost.

---

## E. Automated checks

### E.1 Admin typecheck

```
npx tsc --noEmit   →  tsc-exit=0
```

### E.2 Focused Admin tests (vitest)

```
npx vitest run src/navigation/navigationRegistry.test.tsx src/navigation/CommandPalette.test.tsx \
  src/navigation/NavigationBreadcrumbs.test.tsx src/navigation/navigationPreferences.test.ts \
  src/components/Sidebar.users.test.tsx src/components/Sidebar.firstLogin.test.tsx \
  src/pages/treasury/TreasuryManualWalletSettlement.test.tsx \
  src/pages/treasury/TreasuryManualCodSettlement.test.tsx \
  src/pages/treasury/TreasuryGeneralLedger.test.tsx
```

**9 test files passed — 71/71 tests passed, 0 failed** (`success: true`).

| Test file | Result | Tests |
|---|---|---:|
| `src/navigation/navigationRegistry.test.tsx` | passed | 12 |
| `src/navigation/navigationPreferences.test.ts` | passed | 11 |
| `src/navigation/CommandPalette.test.tsx` | passed | 7 |
| `src/navigation/NavigationBreadcrumbs.test.tsx` | passed | 3 |
| `src/components/Sidebar.firstLogin.test.tsx` | passed | 18 |
| `src/components/Sidebar.users.test.tsx` | passed | 2 |
| `src/pages/treasury/TreasuryManualWalletSettlement.test.tsx` | passed | 7 |
| `src/pages/treasury/TreasuryManualCodSettlement.test.tsx` | passed | 5 |
| `src/pages/treasury/TreasuryGeneralLedger.test.tsx` | passed | 6 |

Machine-readable report: `.ai/evidence/manual-wallet-settlement-20261021/sidebar-cleanup-tests.json`.

### E.3 Admin production build

```
npm run build   →  ✓ built in 45.55s   build-exit=0
```

(The "chunks larger than 5000 kB" notice is the pre-existing bundle-size warning, not an error introduced by this change.)

### E.4 Focused lint

```
npm run lint -- src/components/Sidebar.jsx src/pages/treasury/TreasuryClearingAging.jsx \
  src/pages/treasury/TreasuryManualWalletSettlement.jsx src/App.jsx
→  ✖ 4 problems (0 errors, 4 warnings)
```

All 4 warnings are **pre-existing** and unrelated to this change:

| File | Line | Rule |
|---|---:|---|
| `src/App.jsx` | 199 | `react-refresh/only-export-components` |
| `src/components/Sidebar.jsx` | 103 | `react-refresh/only-export-components` |
| `src/components/Sidebar.jsx` | 452 | `react-hooks/exhaustive-deps` |
| `src/pages/treasury/TreasuryClearingAging.jsx` | 35 | `react-hooks/exhaustive-deps` |

**0 lint errors.**

---

## F. Browser verification (real headed Edge, `opencode-ui-test`, Admin `http://localhost:5173`)

### F.1 Sidebar — English

Expanded Treasury group, sampled from the live DOM:

```
hasWallet: false
treasury links:
  /treasury/initial-capitalization  💵Treasury Initial Capitalization
  /treasury                        📊Finance Dashboard
  /treasury/hub                    🏦Treasury Hub
  /treasury/clearing-aging         ⏳Clearing Aging
  /treasury/general-ledger         📖General Ledger
  /treasury/transfers              💸Treasury Transfers
(+ Finance Control Center group: Dashboard, Trial Balance, Profit & Loss, Balance Sheet, Cash Flow, General Ledger, Clearing Aging, Transfers)
```

No `manual-wallet` href in the nav; no "Manual Wallet Settlement" text. All other Treasury / Finance destinations are intact.
Evidence: `sidebar-en-no-wallet-entry.png`.

### F.2 Sidebar — Arabic (`lang=ar`, `i18nextLng=ar`)

```
hasWalletHref: false
walletText (المحفظة): []      codText (عند الاستلام): []
treasury links: 💵الرسملة الأولية للخزينة · 📊لوحة التحكم المالية · 🏦مركز الخزينة ·
                ⏳أعمار التسويات المعلقة · 📖دفتر الأستاذ العام · 💸تحويلات الخزينة
```

No wallet (or COD) settlement entry in Arabic; every other Treasury destination present.
Evidence: `sidebar-ar-no-wallet-entry.png`.

### F.3 Clearing Aging — actions preserved

English (`main` buttons):

```
Help · Soon · ← Back · 📊 Excel · 📄 PDF
Manual COD Settlement · Manual Wallet Settlement · Manual Carrier Remittance
COD Clearing → Provider workflow (disabled)   Wallet Clearing → Manual Wallet Settlement
```

Arabic (`main` buttons):

```
تسوية التحصيل اليدوي عند الاستلام · تسوية المحفظة الإلكترونية · توريد نقدي يدوي من شركة الشحن
COD Clearing → مسار عمل المزود        Wallet Clearing → تسوية المحفظة الإلكترونية
```

Evidence: `aging-en-wallet-action-after-cleanup.png`, `aging-ar-wallet-action.png`.

### F.4 Clicking the wallet action → page loads, eligible payments + server preview

- EN: URL → `http://localhost:5173/treasury/manual-wallet-settlements`, H1 **"Manual Wallet Settlement"**, **10** eligible payment checkboxes rendered.
- AR: same route, H1 **"تسوية المحفظة الإلكترونية"**; on-screen balances read `حساب تسوية المحفظة 1140: ٣٬٣٩٠٫٠٠ ج.م.` and `صندوق النقدية الرئيسي 1010: ٢١٢٫٣٥ ج.م.` (consistent with the authoritative ledger state — nothing was mutated).
- Selecting `ORD-C9C5-2026-01059` and clicking **Review selected wallet payments / مراجعة مدفوعات المحفظة المحددة** produced the **server-verified posting preview**:
  - EN: `Debit Main Cashbox 1010: EGP 35.00` / `Credit Wallet Clearing 1140: EGP 35.00`
  - AR: `مدين صندوق النقدية الرئيسي 1010: ٣٥٫٠٠ ج.م.` / `دائن حساب تسوية المحفظة 1140: ٣٥٫٠٠ ج.م.`
- The preview was **cancelled** in both languages. **No settlement was confirmed**; the confirm step was never executed.

Evidence: `wallet-page-en-from-aging.png`, `wallet-preview-en-from-aging.png`, `wallet-page-ar-from-aging.png`, `wallet-preview-ar-from-aging.png`.

### F.5 Manual COD Settlement unchanged

`Manual COD Settlement` button → `/treasury/manual-cod-settlements`, H1 **"Manual COD Settlement"**, prior settlement readback `JE-2026-000043` (EGP 35.00 · 1 order · Accounting posted) and **"No eligible collected Manual COD orders"** (the synthetic COD order was already settled in the prior task). Behaviour identical to before the cleanup.
Evidence: `cod-screen-after-cleanup.png`.

### F.6 Command palette (no unrelated destination lost)

| Query | Results |
|---|---|
| `settle` | **1** — "Customer & Supplier Settlement" |
| `wallet` | **0** — "No matching pages" |
| `cod` | **0** — "No matching pages" |
| All pages | **102** |

`wallet` and `cod` now behave identically, confirming the palette is derived from the Sidebar and that COD never had a palette destination either — so no pre-existing independent palette destination was removed by this change.
Evidence: `command-palette-settle-search.png`, `command-palette-wallet-search.png`.

### F.7 No financial / database writes

Read-only SQL (helper forces `default_transaction_read_only=on`; credentials never printed) over `PaymentSettlementBatches` for this tenant with `SettledAtUtc > '2026-10-21T16:15:00+03:00'` returned **`[]`** — no settlement batch was created during verification.
Evidence: `no-new-writes.sql`, `no-new-writes.json`.

---

## G. Scope compliance

| Requirement | Status |
|---|---|
| Only the Sidebar menu item removed | ✅ one line in `Sidebar.jsx` |
| Route `/treasury/manual-wallet-settlements` + guards unchanged | ✅ `App.jsx` untouched |
| Wallet page / endpoints / accounting services / permissions preserved | ✅ no backend or page file modified |
| Clearing Aging header + per-Wallet-Clearing-row wallet actions kept | ✅ verified EN + AR |
| Manual COD Settlement workflow and navigation behaviour preserved | ✅ verified in browser + tests |
| Smart-navigation registry inspected before editing; no other destination broken; no new Sidebar entry | ✅ registry derived from `buildAdminMenu`; COD parity confirmed; palette "All pages" = 102 |
| No payment config / balances / orders / journals / DB records changed | ✅ read-only SQL returned `[]` |
| Only the primary dev workspace used | ✅ |
| Unrelated working-tree changes and existing evidence preserved | ✅ nothing staged/committed; prior evidence untouched |
| No staging, no commit, no destructive Git operations | ✅ |
| No database writes / financial operations | ✅ preview only, cancelled |

---

## H. Evidence index

`.ai/evidence/manual-wallet-settlement-20261021/`

| File | Purpose |
|---|---|
| `sidebar-en-no-wallet-entry.png` | EN expanded Sidebar without the wallet entry |
| `sidebar-ar-no-wallet-entry.png` | AR expanded Sidebar without the wallet entry |
| `aging-en-wallet-action-after-cleanup.png` | EN Clearing Aging keeps both settlement actions |
| `aging-ar-wallet-action.png` | AR Clearing Aging keeps both settlement actions |
| `wallet-page-en-from-aging.png` | EN wallet page opened from Clearing Aging (10 eligible) |
| `wallet-preview-en-from-aging.png` | EN server-verified preview (Dr 1010 / Cr 1140) |
| `wallet-page-ar-from-aging.png` | AR wallet page opened from Clearing Aging |
| `wallet-preview-ar-from-aging.png` | AR server-verified preview |
| `cod-screen-after-cleanup.png` | COD settlement screen unchanged |
| `command-palette-settle-search.png` | Palette query "settle" |
| `command-palette-wallet-search.png` | Palette query "wallet" |
| `sidebar-cleanup-tests.json` | Vitest machine-readable report (71/71) |
| `no-new-writes.sql` / `no-new-writes.json` | Read-only proof of zero new settlement batches |

---

## I. Known pre-existing issues (out of scope, not addressed)

- Storefront checkout still labels Manual Wallet as "Pay on Delivery".
- COD / other clearing ageing still uses the oldest-historic-line age logic.
- `ORD-F577-2026-01039` does not exist in the configured database.
