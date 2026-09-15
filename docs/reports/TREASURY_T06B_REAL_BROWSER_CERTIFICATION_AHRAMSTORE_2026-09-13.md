# Treasury T-06B Real Browser Certification - AhramStore - 2026-09-13

## 1. Executive Verdict

**FAIL**

The authenticated headed Microsoft Edge browser was proven to be operating as `AhramStore`, tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`, with the legitimate `Owner` account. The browser reached `/supplier-card-funding`, the empty workspace, create UI, purchase Card/Mixed/partial-Credit presentation, Arabic/English, RTL/LTR, desktop/mobile, legacy redirects, error state, and navigation. Two T-06A frontend defects were found, fixed, and browser-verified.

The strict T-06B gate cannot pass because AhramStore has zero funding instruments and zero suppliers. Creating either record was prohibited. Consequently, edit, activate/deactivate, the funding dialog and its field validation, an instrument statement, the supplier-payment Card modal, stale/inactive-instrument behavior, and first-instrument behavior were not reachable as a real end user. Only a legitimate Owner session was available, so Admin, Manager, Cashier, and Warehouse browser sessions were not certified.

No business or financial mutation occurred. The protected BEFORE and final AFTER SHA-256 fingerprints are identical: `7A6535EC7D409270296098E32FA469341EB1B69B166F1AF6DFB13F8D91602CC1`.

## 2. Tenant Identity

| Evidence | Value |
| --- | --- |
| Store name in authenticated header | `AhramStore` |
| Tenant name from browser `GET /api/settings/default-country` | `AhramStore` |
| Tenant ID from browser `GET /api/auth/session` | `c9c5c1ae-949e-498e-bc63-07242daeb49d` |
| Tenant ID from read-only PostgreSQL | `c9c5c1ae-949e-498e-bc63-07242daeb49d` |
| Tenant slug | `ahramstore` |
| Tenant active/status | `true` / `Active` |
| Authenticated role | `Owner` |
| Current user role shown in browser | `Owner` |
| Authenticated username/email | `ahramco.software@gmail.com` |
| Browser URL after login | `http://localhost:5173/` |
| Certification route | `http://localhost:5173/supplier-card-funding` |

The browser tenant ID exactly matched the independently queried AhramStore tenant row before certification routes were used. No evidence from the pre-existing Shopixy Edge window was used.

## 3. Database Baseline

The baseline was captured at `2026-09-13T14:59:06.0664493Z` in PostgreSQL database `PosEcommerceDb_Baseline`. The harness used `REPEATABLE READ`, executed `SET TRANSACTION READ ONLY`, and rolled back.

| Entity or value | Baseline |
| --- | ---: |
| SupplierCardFundingInstruments | 0 |
| SupplierCardFundings | 0 |
| PurchaseInvoices | 0 |
| PurchaseInvoicePayments | 0 |
| SupplierPayments | 0 |
| Journals | 0 |
| LedgerLines | 0 |
| AccountingOutbox | 0 |
| Pending outbox | 0 |
| Failed outbox | 0 |
| Customers / aggregate balance | 0 / EGP 0.00 |
| Suppliers / aggregate balance | 0 / EGP 0.00 |
| Products / variants | 1 / 4 |
| ProductVariantStocks / quantity / reserved / damaged | 4 / 100.00 / 0.00 / 0.00 |
| WarehouseStocks / quantity | 0 / 0.00 |
| Treasury accounts | 6 |
| Treasury cached and ledger-derived balances | All EGP 0.00 |
| `1020` cached / ledger / lines | EGP 0.00 / EGP 0.00 / 0 |
| `1110` cached / ledger / lines | EGP 0.00 / EGP 0.00 / 0 |

Baseline fingerprint: `7A6535EC7D409270296098E32FA469341EB1B69B166F1AF6DFB13F8D91602CC1`.

## 4. Authentication

Normal POS login was used with the supplied AhramStore credentials. No test token or fabricated role token was issued. The authenticated layout, sidebar, store name, user role, and dashboard loaded. There was no login loop or redirect loop.

After identity proof, a browser routing guard aborted any attempted post-login `POST`, `PUT`, `PATCH`, or `DELETE` under `/api/**`. The guard recorded zero mutation attempts. Normal login itself rotated authentication-session state and wrote ordinary authentication audit metadata; those are not business or financial records and are outside the protected fingerprint.

## 5. Supplier Card Workspace

`GET http://localhost:5173/supplier-card-funding` returned HTTP 200 and rendered the authenticated React route. The English title was `Supplier Card Funding`; the browser API returned zero instruments and two eligible funding sources. The table settled to `No supplier card funding instruments are configured.` and did not remain in Loading.

No workspace text exposed `1020`, `1110`, a legacy Visa Account, or a fallback account. The role-filtered sidebar exposed `Supplier Card Funding` for the Owner.

## 6. Create Instrument UI

The Owner opened `Create Funding Instrument` without submitting it. The dialog had `role="dialog"`, `aria-modal="true"`, an accessible name, viewport containment, and native label associations for all controls.

Verified fields were Instrument Name, fixed Type, Issuer, Last 4 Digits, Card Funding Account Code, Card Funding Account Name, Funding Source, and active state. Type was disabled and fixed to `FundedCompanyCard`. Funding Source started blank and offered only `Main Cashbox (Cashbox)` and `Primary Bank Account (Bank)` from the backend response. It did not offer `1020` or `1110`, and `1030` was not silently selected.

Clicking Save with an empty form was a validation-only action. Five required controls were invalid and the browser displayed `Please fill out this field.` No API mutation request was emitted. The dialog does not provide an arbitrary existing GL account picker; it collects a dedicated new account code/name whose final authority remains backend-controlled.

## 7. Funding UI

The browser API proved that the available source set contains only backend-returned Cashbox/Bank Treasury sources. The top-level Fund Card button was disabled because the tenant has zero active instruments, which is a safe fail-closed state.

The funding dialog, blank instrument/source controls, zero/negative/invalid amount validation, notes/reference behavior, and stale/inactive instrument responses could not be opened or reproduced without first creating a prohibited funding instrument. This is a certification blocker, not permission to create one.

## 8. Purchase Card

On `/purchase-invoice`, selecting `Card` rendered a required Funding Instrument selector. It started blank, contained only the no-instrument placeholder, was disabled for the zero-instrument state, and displayed `No active supplier card funding instrument is configured.` No `1020` or `1110` label appeared.

The tenant has no supplier and no purchase item was selected. A validation-only Create Purchase click produced no mutation request, but it stopped at the earlier supplier/item requirements. Therefore, the instrument-specific final submission block was not fully proven through a complete real form. Current backend balance/masked identity could not be displayed because no instrument exists.

## 9. Mixed Purchase

Selecting `Cash + Card` rendered exactly one Cash Amount and one Card Amount control. After setting a positive Card allocation, one blank Funding Instrument selector appeared. The Cash field remained independent, and no instrument was attached to Cash. No first item or fallback instrument was selected.

Submission was not attempted past missing supplier/item validation and no purchase was created.

## 10. Partial Credit

Selecting Credit with zero initial payment did not render an instrument selector. A positive Cash-only initial payment did not render one. A positive Card initial payment rendered a blank required Funding Instrument selector. A Mixed initial payment rendered independent Cash/Card controls and the blank instrument selector when the Card amount was positive.

No invoice was submitted. The presentation rules matched the required authority boundaries.

## 11. Supplier Card Payment

`/payments` loaded and the browser switched to `Make Payment (Supplier)`. AhramStore has zero suppliers, so the supplier selector contained only its placeholder. The payment modal and its Card selector are reachable only after selecting a real supplier.

No supplier was fabricated. As a result, blank Card instrument behavior, Card required validation, informational balance/source, and Cash/BankTransfer field exclusion were not real-browser certified. The empty route exposed no `1020` or `1110` fallback.

## 12. Statement

The workspace clearly reported that no instruments are configured. With no selectable instrument, the statement dialog/route and empty transaction table could not be opened. No instrument was fabricated merely to obtain a statement screen.

## 13. Legacy UI Containment

The rendered sidebar had no link to `/reports/visa-statement` and no link to `/balance-transfer`. The Supplier Card workspace did not call legacy Visa or FinancialAccount APIs.

The existing `Visa Clearing` navigation remains the separate customer-card clearing route at `/treasury/account/1110`; it was not presented as supplier-card authority. The old Visa Statement deep link redirected to that controlled customer-clearing route, and the old Balance Transfer deep link redirected to the canonical Treasury transfer route.

## 14. Roles

| Role | Browser evidence | Result |
| --- | --- | --- |
| Owner | Legitimate AhramStore session; workspace, create administration, read surface visible | PASS |
| Admin | No legitimate AhramStore session available | NOT CERTIFIED |
| Manager | No legitimate AhramStore session available | NOT CERTIFIED |
| Cashier | No legitimate AhramStore session available | NOT CERTIFIED |
| Warehouse | No legitimate AhramStore session available | NOT CERTIFIED |

No role token was fabricated. Existing focused source/component tests continue to assert Owner/Admin administration, Manager read/fund, and Cashier/Warehouse exclusion, but those tests do not replace the missing real-browser sessions.

## 15. Arabic

The browser switched through the visible `AR` control. The Supplier Card navigation, title, create button/dialog, labels, fixed card type, no-instrument text, funding-source terminology, authority wording, and retry action rendered in Arabic with no visible translation keys.

The first synthetic 500 probe exposed a defect: the service-unavailable message remained English. The T-06A-specific error mapper and Arabic resource were fixed. The rerun rendered `خدمة بطاقات الموردين غير متاحة مؤقتًا. يرجى المحاولة مرة أخرى.` and the Arabic retry action.

Purchase and supplier-payment selector translations exist and the Purchase selector was exercised in English. The supplier-payment selector could not be opened in Arabic because no supplier exists.

## 16. RTL

In Arabic, `document.documentElement.lang` was `ar` and direction was `rtl`. At `1440x1000`, document and client widths were both 1440 pixels. The workspace, sidebar, create dialog, labels, controls, action alignment, table containment, and error state remained usable. No page-level horizontal overflow was observed.

## 17. English

Switching back through `EN` restored the English workspace, controls, and empty state. No stale Arabic or visible `supplierCards.*` key remained. The localized 500 message rendered as `Supplier card service is temporarily unavailable. Please try again.`

## 18. LTR

After returning to English, `document.documentElement.lang` was `en` and direction was `ltr`. At `1440x1000`, document and client widths were both 1440 pixels. Layout and action alignment returned to LTR without stale RTL positioning.

## 19. Desktop

The `1440x1000` browser exercised the authenticated dashboard, navigation, workspace, empty instrument table, create dialog, Purchase Card, Mixed Purchase, partial Credit, supplier-payments empty route, localized error state, Arabic/RTL, English/LTR, and deep links.

The workspace had no document-wide overflow. Wide tables used contained horizontal scrolling. Instrument edit, funding, statement, and supplier-payment modal screens remained blocked by the empty tenant data.

## 20. Mobile

At `390x844`, the Supplier Card workspace had a 390-pixel document/client width with no page-level overflow. The create dialog remained inside the viewport and used internal vertical scrolling. Fields and buttons were reachable.

The Purchase Card selector was visible, blank, and reachable. The purchase document width was 375 pixels against a 375-pixel client width, so no document-wide overflow occurred. Instrument-dependent statement/funding tables and the supplier-payment modal could not be opened.

## 21. Loading/Empty/Error

The real clean-network audit observed both supplier-card endpoints return HTTP 200, the empty state settle, no Loading text after 10 seconds, no page errors, no functional console errors, and no mutation request. Development React Strict Mode caused one duplicate initial effect, yielding four total GETs for two endpoints; it did not produce a refresh loop.

The apparent repeated failure seen during the earlier run was created by the certification harness. It intentionally returned browser-local synthetic 500 responses for both GET endpoints. Axios retried each idempotent GET and Development Strict Mode duplicated the initial load, so multiple failures preceded the final alert. Those requests never reached the API or database and are not an application refresh-loop defect.

Verified states were real zero instruments, synthetic zero funding sources, controlled 500 rejection, retry action, and final loading termination. The harness did not conclusively capture the transient Loading row during its 1.5-second delay even though the route settled correctly, so slow-loading presentation remains partial. Expired authority, stale authority, and inactive-instrument states require an existing instrument and were not fabricated.

## 22. Console/Network Audit

| Observation | Classification | Disposition |
| --- | --- | --- |
| Real supplier-card GETs | Expected | Four Development requests, all HTTP 200; no loop |
| Pre-login `/api/auth/session` HTTP 401 | Expected | Anonymous bootstrap before normal login |
| Synthetic supplier-card HTTP 500 responses | Expected | Browser-local error-state probe; did not reach API |
| `frame-ancestors` ignored from meta CSP | Environment/tooling | Existing Vite/meta delivery warning; unrelated to T-06A behavior |
| React Router v7 future-flag warnings | Unrelated | Existing React Router development warnings |
| `net::ERR_ABORTED` GETs | Environment/tooling | Navigation canceled in-flight reads during rapid route changes |
| Page exceptions | Expected clean result | 0 |
| Real post-auth supplier-card 4xx/5xx | Expected clean result | 0 |
| Post-login mutation requests | Expected clean result | 0 |

No unclassified real supplier-card 4xx/5xx remains. The only console errors tied to supplier-card endpoint failures were intentionally generated by the synthetic 500 probes. The clean non-intercepted audit had zero functional console errors and zero page errors.

## 23. Database Non-Mutation

The final AFTER capture was taken at `2026-09-13T15:24:06.4021088Z` after the clean browser audit, using the same read-only repeatable-read transaction and rollback.

| Entity | Before | After | Difference |
| --- | ---: | ---: | ---: |
| SupplierCardFundingInstruments | 0 | 0 | 0 |
| SupplierCardFundings | 0 | 0 | 0 |
| PurchaseInvoices | 0 | 0 | 0 |
| PurchaseInvoicePayments | 0 | 0 | 0 |
| SupplierPayments | 0 | 0 | 0 |
| Journals | 0 | 0 | 0 |
| LedgerLines | 0 | 0 | 0 |
| AccountingOutbox | 0 | 0 | 0 |
| Customer balances | EGP 0.00 | EGP 0.00 | 0 |
| Supplier balances | EGP 0.00 | EGP 0.00 | 0 |
| Inventory stock rows | 4 | 4 | 0 |
| Inventory quantity | 100.00 | 100.00 | 0.00 |
| Inventory reserved | 0.00 | 0.00 | 0.00 |
| Inventory damaged | 0.00 | 0.00 | 0.00 |
| Treasury accounts | 6 | 6 | 0 |
| Treasury cached/ledger balances | All EGP 0.00 | All EGP 0.00 | 0.00 |
| `1020` cached/ledger balance | EGP 0.00 / EGP 0.00 | EGP 0.00 / EGP 0.00 | 0.00 |
| `1110` cached/ledger balance | EGP 0.00 / EGP 0.00 | EGP 0.00 / EGP 0.00 | 0.00 |

| Fingerprint | Value |
| --- | --- |
| BEFORE | `7A6535EC7D409270296098E32FA469341EB1B69B166F1AF6DFB13F8D91602CC1` |
| AFTER | `7A6535EC7D409270296098E32FA469341EB1B69B166F1AF6DFB13F8D91602CC1` |
| Difference | ZERO |

No instrument, funding, purchase, purchase payment, supplier payment, journal, ledger line, outbox message, customer/supplier balance, inventory quantity, Treasury balance, `1020`, or `1110` changed.

## 24. Deep Links

| Route | Browser result |
| --- | --- |
| `/supplier-card-funding` | HTTP 200; authenticated title and empty state rendered; no blank shell |
| `/reports/visa-statement` | HTTP 200 SPA document; controlled client redirect to `/treasury/account/1110` |
| `/balance-transfer` | HTTP 200 SPA document; controlled client redirect to `/treasury/transfers` |

No endless loading or uncontrolled redirect was observed.

## 25. Accessibility

The create dialog exposed dialog/modal semantics and an accessible name. Every create input/select/textarea had a native HTML label association. Required controls surfaced native invalid state. Keyboard Tab navigation moved between controls and focus had a visible outline/ring. Error containers used `role="alert"`; disabled controls communicated disabled state.

The real browser found that the newly introduced Purchase Funding Instrument label was not associated with its selector. `htmlFor="purchase-funding-instrument"` and the matching select `id` were added. The rerun resolved the selector by accessible label and passed.

The supplier-payment dialog accessibility could not be browser-tested because no supplier exists. Source shows an explicit `htmlFor`/`id` association for its new instrument selector, but source evidence is not treated as real-browser certification.

## 26. Tests/Build

| Command | Result |
| --- | --- |
| `npm test -- --run src/services/supplierCardFundingApi.test.ts src/pages/SupplierCardFundingInstruments.test.tsx src/pages/PurchaseInvoice.test.tsx src/components/SOAPaymentModal.test.tsx` | PASS: 4 files, 25 tests |
| `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SupplierCardFundingAuthorityIntegrationTests"` | PASS: 2 tests |
| `npm run build` | PASS: 5,215 modules transformed |
| `git diff --check` | PASS: no whitespace errors; line-ending notices only |

The production build emitted only the existing large-chunk warning. No backend source was changed by T-06B, so a separate API build was not required; the focused backend test command built Core, Infrastructure, API, and Tests successfully.

Exact T-06B fixes:

- `PointOfSale.Web/src/pages/PurchaseInvoice.jsx`: associated the new Funding Instrument label and selector.
- `PointOfSale.Web/src/services/supplierCardFundingApi.js`: localized supplier-card HTTP 5xx errors through `supplierCards.errors.serviceUnavailable`.
- `PointOfSale.Web/src/services/supplierCardFundingApi.test.ts`: added the 5xx localized-key assertion.
- `PointOfSale.Web/src/i18n/locales/en.json`: added the English supplier-card service-unavailable message.
- `PointOfSale.Web/src/i18n/locales/ar.json`: added the Arabic supplier-card service-unavailable message.

## 27. Browser Certification Matrix

`PASS` means real-browser evidence was obtained for the available state. `PARTIAL` means only presentation or a subset was reachable. `BLOCKED` means completing the check required prohibited data creation or an unavailable legitimate role session.

| Area | Desktop | Mobile | Arabic | English | Result |
| --- | --- | --- | --- | --- | --- |
| Tenant verification | PASS | PASS | PASS | PASS | PASS |
| Navigation | PASS | PASS | PASS | PASS | PASS |
| Instrument list | PASS | PASS | PASS | PASS | PASS empty state |
| Instrument create UI | PASS | PASS | PASS | PASS | PASS, not submitted |
| Instrument edit UI | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED: zero instruments |
| Funding UI | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED: zero active instruments |
| Purchase Card | PASS | PASS | PARTIAL | PASS | PARTIAL: presentation passed; complete validation path lacks supplier/item |
| Mixed Purchase | PASS | PASS | PARTIAL | PASS | PARTIAL: presentation passed; not submitted |
| Partial Credit | PASS | PASS | PARTIAL | PASS | PARTIAL: presentation passed; not submitted |
| Supplier Card Payment | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED: zero suppliers |
| Statement | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED: zero instruments |
| Error handling | PASS | PASS | PASS | PASS | PASS for reachable/general 5xx; stale/inactive blocked |
| Loading | PARTIAL | PARTIAL | PARTIAL | PARTIAL | PARTIAL: clean termination passed; slow transient not conclusively captured |
| Empty state | PASS | PASS | PASS | PASS | PASS |
| Permissions | PARTIAL | PARTIAL | PARTIAL | PARTIAL | PARTIAL: Owner only |
| Legacy containment | PASS | PASS | PASS | PASS | PASS |
| Deep links | PASS | PASS | PASS | PASS | PASS |
| Console | PASS | PASS | PASS | PASS | PASS with all messages classified |
| Network | PASS | PASS | PASS | PASS | PASS with synthetic failures excluded from real traffic |
| Accessibility | PASS | PASS | PASS | PASS | PASS for reachable T-06A controls after fix |
| No-overflow | PASS | PASS | PASS | PASS | PASS |

## 28. Final Verdict

**FAIL**

Browser tenant: `AhramStore`, tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`, legitimate `Owner` session.

Evidence quality: high for tenant identity, reachable read-only UI, create presentation, purchase allocation presentation, localization, direction, responsive containment, deep links, clean network behavior, and database non-mutation; insufficient for instrument-dependent, supplier-dependent, and non-Owner role flows.

Exact defects found: the Purchase Funding Instrument selector lacked a programmatic label association; supplier-card HTTP 5xx text remained English in Arabic mode.

Exact fixes made: added matching `htmlFor`/`id` to the Purchase selector; added T-06-specific localized 5xx mapping, English/Arabic resources, and a focused test assertion. Both fixes passed the headed-browser rerun.

Database mutation status: zero business/financial mutation. BEFORE and AFTER fingerprints are identical. `SupplierCardFundingInstruments`, `SupplierCardFundings`, purchases, payments, supplier payments, journals, ledger lines, outbox, party balances, inventory, Treasury, `1020`, and `1110` are unchanged.

Remaining limitations: zero instruments block edit, activation/deactivation, funding UI, statement UI, stale/inactive authority, and first-instrument behavior; zero suppliers block supplier Card payment; missing legitimate Admin/Manager/Cashier/Warehouse sessions block full role certification; the slow transient Loading row was not conclusively captured.

Readiness for T-06C: **CONDITIONAL ONLY**. The reachable T-06A UI, fixes, builds, and non-mutation controls are sound, but T-06B cannot issue PASS under the strict matrix. T-06C was not executed and requires separate explicit authorization for any instrument, supplier, funding, purchase, payment, or journal creation.
