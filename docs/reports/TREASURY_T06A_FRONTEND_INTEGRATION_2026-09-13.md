# Treasury T-06A Frontend Integration - 2026-09-13

## 1. Executive Verdict

T-06A is frontend-integrated and source-verified for the approved `FundedCompanyCard` authority model. Supplier Card purchases and standalone supplier Card payments now require an explicit active funding instrument and submit its ID and authority version. The UI does not infer authority from the tender label `Card`, does not silently select the first configured instrument, and does not introduce a fallback to `1020`, `1110`, `1030`, `FinancialAccount`, or legacy Visa data.

Verdict: **SOURCE PASS / AUTHENTICATED BROWSER MUTATION NOT EXECUTED / PRODUCTION FINANCIAL MUTATION NOT AUTHORIZED**.

## 2. Scope

This integration covers:

- Supplier card instrument discovery, creation, metadata editing, activation, and deactivation.
- Explicit funding from the instrument's backend-authorized Cashbox or Bank Treasury source.
- Ledger-derived instrument balances and statements.
- Purchase `Card`, `Mixed`, and partial-`Credit` initial Card allocations.
- Standalone supplier Card payments.
- Route, navigation, role, localization, RTL, responsive overflow, error, and retry behavior.
- Removal of active navigation into conflicting legacy Visa and `FinancialAccount` transfer surfaces.

## 3. Safety And Exclusions

No real instrument, funding, purchase, supplier payment, reversal, journal, historical reclassification, account edit, or balance edit was performed. The work does not change customer Card behavior, provider settlement, the `1110 Visa Clearing` authority, historical `1020` entries, or the active generic `FinancialAccounts` configuration screens. Existing unrelated working-tree changes were preserved, and no commit was created.

## 4. Backend Authority Baseline

The frontend follows the implemented backend contract in `PointOfSale.API/Controllers/SupplierCardFundingInstrumentsController.cs`:

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/api/supplier-card-funding-instruments` | List tenant instruments and ledger-derived balances |
| `GET` | `/api/supplier-card-funding-instruments/funding-sources` | List active Cashbox/Bank Treasury sources |
| `POST` | `/api/supplier-card-funding-instruments` | Create an instrument and dedicated Asset account |
| `PUT` | `/api/supplier-card-funding-instruments/{id}` | Edit metadata or active state with expected authority version |
| `POST` | `/api/supplier-card-funding-instruments/{id}/fundings` | Fund through explicit instrument/source/version authority |
| `GET` | `/api/supplier-card-funding-instruments/{id}/statement` | Read the ledger-derived statement |

Purchase and supplier-payment requests accept `fundingInstrumentId` and `fundingInstrumentVersion`. The backend resolves and freezes the ledger-account snapshot; the frontend does not submit or choose a ledger account for an outflow.

## 5. Frontend Surface Inventory

| Surface or reference | Pre-integration classification | T-06A result |
| --- | --- | --- |
| `PurchaseInvoice.jsx` supplier `Card`, `Mixed`, and partial-`Credit` Card allocations | MUST CHANGE: amount-only tender without instrument authority | ACTIVE: explicit instrument ID/version required |
| `Payments.jsx` and `SOAPaymentModal.jsx` supplier Card payment | MUST CHANGE: Card tender without instrument authority | ACTIVE: explicit instrument ID/version required |
| `/supplier-card-funding` | Missing | ACTIVE: role-guarded instrument, funding, and statement workspace |
| `PaymentModal.jsx` and POS customer Card collection | ACTIVE customer collection authority | PRESERVED: no supplier instrument fields added |
| `/treasury/account/1110` | ACTIVE customer Card clearing | PRESERVED: remains customer clearing only |
| `/financial-accounts` and `/api/financialaccounts` | ACTIVE generic financial-account configuration | PRESERVED but isolated from T-06 authority |
| `/balance-transfer` and `BalanceTransfer.jsx` | LEGACY generic `FinancialAccount` transfer lane | RETIRED FROM NAVIGATION; route redirects to `/treasury/transfers` |
| `/reports/visa-statement` and `VisaAccountStatement.jsx` | LEGACY Visa adapter | RETIRED FROM NAVIGATION; route redirects to `/treasury/account/1110` |
| `/treasury-movements` and `TreasuryMovements.jsx` | Existing legacy movement surface | REMAINS redirected to `/treasury/hub` |
| `treasuryApi.ACCOUNT_CODES.VISA_ACCOUNT` (`1020`) | Unused legacy constant | DEAD REFERENCE; not used by T-06A and not revived |
| Card labels in sales, returns, and payment-method reports | ACTIVE customer/reporting tender labels | OUT OF SCOPE and unchanged |

## 6. API Client Integration

`PointOfSale.Web/src/services/supplierCardFundingApi.js` centralizes the six supplier-card endpoints. It returns arrays defensively for list calls, passes abort signals to read requests, preserves caller-owned `Idempotency-Key` headers for funding, and exposes common authority, role, label, retry, and localized error helpers. Active payment flows request `activeOnly=true` from the backend.

No supplier-card component calls the legacy `/visastatement` or `/financialaccounts` APIs, and no supplier-card helper contains `1020`, `1110`, or `1030` fallback logic.

## 7. Route And Navigation

`PointOfSale.Web/src/App.jsx` adds `/supplier-card-funding` inside the authenticated layout and wraps it in `PosRoleRouteGuard` for `Owner`, `Admin`, and `Manager`. `PointOfSale.Web/src/components/Sidebar.jsx` exposes the same route under Treasury and applies the same role metadata before rendering.

The legacy Visa statement link and generic balance-transfer link were removed from the sidebar. Their URLs remain safe deep links through redirects, preserving bookmarks without reviving conflicting data authority.

## 8. Role Matrix

| Capability | Owner | Admin | Manager | Cashier/Warehouse |
| --- | ---: | ---: | ---: | ---: |
| Open supplier-card workspace | Yes | Yes | Yes | No |
| List instruments and funding sources | Yes | Yes | Yes | No |
| Create instrument | Yes | Yes | No | No |
| Edit metadata or active state | Yes | Yes | No | No |
| Fund active instrument | Yes | Yes | Yes | No |
| View statement | Yes | Yes | Yes | No |

This mirrors `[Authorize(Roles = "Owner,Admin,Manager")]` at controller level and the narrower `Owner,Admin` create/update policy. Frontend hiding is convenience only; backend authorization remains authoritative.

## 9. Instrument List And Identity

`PointOfSale.Web/src/pages/SupplierCardFundingInstruments.jsx` displays the backend identity and authority evidence for each instrument: display name, immutable `FundedCompanyCard` type, masked last four digits, issuer, dedicated ledger account code/name, configured funding source/type, active state, authority version-derived mutation behavior, and ledger-derived available balance.

Only the optional last four digits are displayed. The frontend neither asks for nor stores a full card number, expiry, CVV, or credential.

## 10. Create And Edit Lifecycle

Owners and Admins can create an instrument with display name, optional issuer and last four digits, a new dedicated ledger account code/name, an explicit backend-listed Treasury funding source, and active state. The type is fixed to the only supported model and is not user-selectable.

Editing is limited to display metadata and active state. Ledger account, funding source, type, and tenant authority are shown as immutable. Update requests include `expectedAuthorityVersion`, and stale or concurrent edits are surfaced as localized reload/retry errors. No delete action is exposed.

## 11. Funding Workflow

Funding requires an explicit active instrument and an explicit source. The source selector is constrained to the instrument's configured `fundingSourceTreasuryAccountId`; it is not a free account picker. The request includes instrument authority version, source Treasury account ID, positive amount, UTC funding date, optional reference, and optional notes.

The UI describes the direction as Treasury source to company card. It never calculates or updates balances locally; after success it reloads backend projections.

## 12. Funding Idempotency

The page owns the funding request identity. `resolveSupplierCardAttempt` reuses one generated `Idempotency-Key` while the complete request fingerprint is unchanged and creates a new key when any logical request field changes. A failed unchanged submission therefore retries the same operation rather than creating an accidental duplicate. A successful operation clears the retained attempt before reloading data.

## 13. Statement And Balance Projection

The statement dialog calls the backend statement endpoint with explicit `from` and `to` dates. It displays opening balance, categorized funding, supplier purchase, supplier payment, return, reversal, and closing totals, followed by date, transaction type, reference, source document ID, journal ID, description, debit, credit, and backend-provided running balance.

Categorized summary totals are presentation-only sums over returned ledger rows. Opening, closing, debit, credit, net movement, and running balance remain backend projections; no mutable frontend balance model exists.

## 14. Purchase Card Integration

`PointOfSale.Web/src/pages/PurchaseInvoice.jsx` loads active instruments alongside initial warehouse data without allowing an instrument failure to erase a successful warehouse response. Selecting purchase payment method `Card` reveals a required instrument selector. The selector starts blank even when exactly one active instrument exists, shows masked identity and current backend balance, and adds the selected instrument ID/version to the purchase create or update payload.

Submitting Card without a current listed instrument fails in the UI before the API mutation. Backend validation remains the final authority for stale version, inactive instrument, tenant mismatch, or insufficient funded balance.

## 15. Mixed Purchase Integration

A `Mixed` purchase continues to require positive Cash and Card portions whose sum equals the invoice total. Because the Card portion is positive, the same explicit supplier-card selector is required and the selected ID/version applies only to the Card allocation. Cash behavior and amount splitting remain unchanged.

## 16. Partial Credit Purchase Integration

A `Credit` purchase with a positive initial payment requires supplier-card authority only when the initial method is `Card`, or when it is `Mixed` with a positive Card amount. A zero initial payment and a Cash-only initial payment do not send supplier-card authority. Editing restores the instrument ID from the persisted Card payment row when available, but does not substitute another configured instrument if that authority is absent or no longer active.

## 17. Standalone Supplier Payment Integration

On the supplier `make` tab, `Payments.jsx` loads active instruments and passes loading, error, and list state into `SOAPaymentModal.jsx`. Choosing supplier `Card` reveals a blank required selector and adds the exact selected ID/version to the request. The selected balance and configured source are informational backend projections.

The page also owns a stable supplier-payment idempotency key keyed by supplier and sanitized request content. An unchanged failed retry reuses the key; a changed request receives a new key. This applies the backend-required idempotency contract without changing the existing API service's fallback behavior for other callers.

## 18. Preserved Cash And Customer Card Behavior

Supplier `Cash` and `BankTransfer` submissions do not include supplier-card authority. Customer receive-payment Card submissions do not render the supplier-card selector and do not include `fundingInstrumentId` or `fundingInstrumentVersion`. Existing POS customer Card handling in `PaymentModal.jsx` remains untouched and continues through the separate customer clearing/settlement lane.

Automated component tests explicitly cover supplier Cash and customer Card preservation.

## 19. Fail-Closed And Error UX

The purchase and supplier-payment forms show clear blocked states when no active instrument exists or instrument discovery fails. They do not select a default or submit an empty authority. The management page handles missing source, non-positive amount, inactive instrument, stale authority, duplicate request, wrong account authority, permission denial, and not-found responses through localized messages while retaining backend text only through the repository's safe API error fallback.

Client checks improve feedback but do not replace backend tenant, role, account, version, balance, concurrency, and idempotency enforcement.

## 20. English, Arabic, And RTL

English and Arabic resource files contain navigation, instrument lifecycle, funding, statement, role/error, purchase selector, and supplier-payment explanatory text. Both JSON files parse successfully. The new workspace applies the current document direction, uses logical `text-start` alignment for tables, reverses action alignment where required, and preserves the existing RTL behavior in purchase and payment forms.

## 21. Responsive And Accessibility Review

The management and statement tables are wrapped in bounded `overflow-x-auto` containers with explicit minimum widths, preventing page-level horizontal expansion. Forms collapse to one column on small screens and expand at `sm`, `md`, or `xl` breakpoints. Dialogs are viewport-height bounded and vertically scrollable.

Inputs have associated labels, dialogs expose `role="dialog"` and `aria-modal="true"`, errors use `role="alert"`, and mutation buttons expose loading/disabled states. Source review and production compilation passed. Pixel-level visual inspection was not executed because this session has no browser automation interface.

## 22. Verification Evidence

| Check | Result |
| --- | --- |
| `npm test -- --run src/services/supplierCardFundingApi.test.ts src/pages/SupplierCardFundingInstruments.test.tsx src/pages/PurchaseInvoice.test.tsx src/components/SOAPaymentModal.test.tsx` | PASS: 4 files, 25 tests, 0 failed |
| `npm run build` in `PointOfSale.Web` | PASS: 5,215 modules transformed; existing large-chunk warning only |
| `dotnet build PointOfSale.API/PointOfSale.API.csproj --configuration Release --no-restore` | PASS: 0 warnings, 0 errors |
| Focused `SupplierCardFundingAuthorityIntegrationTests` | PASS: 2 passed, 0 failed, 0 skipped |
| `git diff --check` | PASS: no whitespace errors; line-ending notices only |
| English and Arabic JSON parse | PASS |
| Static default-selection search | PASS: no `cardInstruments[0]` or equivalent implicit selection |
| Supplier-card fallback search | PASS: no `1020`, `1110`, `1030`, Visa, or `FinancialAccount` fallback in new service/page |
| Dev-server deep link `GET http://localhost:5173/supplier-card-funding` | PASS: HTTP 200 and React root document returned |
| Unauthenticated API probe | EXPECTED FAIL-CLOSED: HTTP 400 `TENANT_REQUIRED` without a tenant-bound session |
| Authenticated browser workflow | NOT EXECUTED: no browser automation/session interface available |
| Financial mutation | NOT EXECUTED |

ESLint remains repository-tooling-blocked because ESLint 9 has no `eslint.config.js`, `eslint.config.mjs`, or `eslint.config.cjs`; the established Vite build and focused Vitest suites were used instead.

## 23. Final Gate

T-06A passes source-level frontend integration for future funded company cards. The frontend now makes the backend authority explicit, preserves the customer Card boundary, retires active navigation to conflicting legacy surfaces, and fails closed when supplier Card authority is absent.

This report does not certify an authenticated browser mutation or authorize deployment/business data changes. Controlled environment use still requires an approved tenant session, an approved configured instrument, and separately authorized financial test instructions. Historical shopixy `1020 = EGP -3.80` remains unchanged and outside this frontend integration.
