# PASS AF-PA1.3 Affiliate Configuration Edit Authority Certification Findings

Status: Production-certifiable after frontend edit-state hardening.

Date: 2026-06-25

Scope: POS Admin Affiliate Configuration frontend edit authority only: product affiliate configuration form initialization, controlled inputs, category select, numeric inputs, default/empty/null handling, dirty state, reset, disabled/read-only behavior, lifecycle refetch, browser refresh, navigation away/return, multi-tab edit state, UX indicators, and adversarial DOM/React-state edits. Save logic, mutation APIs, backend persistence, DTO submission, and backend validation were intentionally not reviewed.

## Authority Conclusion

Affiliate Configuration Edit Authority is backend-owned. The backend owns editable policy, field authority, default values, read-only rules, tenant scope, financial commission rules, approval rules, and persisted configuration truth. The frontend owns only temporary input collection, validation feedback, dirty tracking, focus/selection state, and presentation.

Frontend edits are non-authoritative candidate intent. Edited values can be typed, reset, lost on refresh, or changed in DevTools, but they do not become configuration truth unless a later backend-authorized mutation accepts them. This pass did not certify mutation or persistence; it certified that the edit surface itself cannot become business authority.

## Runtime Hardening Performed

The product affiliate settings form previously initialized controls from local empty/false values before a backend settings payload was accepted, had no explicit backend baseline for dirty/reset, and lifecycle refetch could overwrite unsaved edits. Those were edit-state/default-value certification gaps, not backend authority bypasses.

Hardening:

- Added an accepted backend settings baseline separate from editable form state.
- Suppressed editable controls when current backend settings authority is unavailable.
- Added field-level dirty tracking against the accepted backend baseline.
- Added a Reset Changes action that restores the accepted backend baseline.
- Changed focus/visibility/online refetch to update the backend baseline without overwriting dirty in-progress edits.
- Forced post-action response reconciliation into the backend baseline and cleared dirty state.

## Form Authority Findings

- The only true Affiliate Configuration edit form found in the inspected frontend is `ProductAffiliateSettingsCard`.
- Displayed editable values now require an accepted backend settings payload before the form renders.
- Controlled inputs hold temporary candidate intent only: category ID, commission rate percentage string, and max commission cap string.
- Frontend role checks and readiness checks are presentation guards only; backend role authorization, tenant context, readiness, product ownership, category rules, and commission rules remain decisive.
- The admin affiliate list/detail filters, settlement checkboxes, suspend reason, withdrawal notes, and payment reference fields are action/filter intent surfaces, not configuration truth.

## Default Value Findings

- Backend GET returns the authoritative default shape for products without a policy: disabled, null commission/cap/category, and backend template default.
- The frontend no longer promotes local initial `false`/empty values into an editable form when settings authority is unavailable.
- Empty commission and cap fields are presentation of backend nulls or unsaved user intent; they are not defaults authority.
- Stale defaults cannot become authority because refresh/remount re-enters backend GETs and reset restores only the accepted backend baseline.

## Dirty State Findings

- Dirty state is field-level and compared to the accepted backend baseline.
- Dirty state is memory-only and does not persist to localStorage, sessionStorage, React Query, or POS runtime cache.
- Focus/visibility/online refetch updates the backend baseline but preserves dirty input values until reset or successful backend response reconciliation.
- Refresh, remount, navigation away, and another tab do not preserve dirty state; unsaved edits are lost rather than promoted.
- Dirty state cannot become authority; it only shows an unsaved-change indicator and reset affordance.

## Field Locking Findings

- Product affiliate edit fields are controlled and disabled while saving; category select is also disabled while category authority is loading.
- Inactive categories render disabled options and are blocked again by frontend validation, but backend category validation remains decisive.
- Read-only affiliate identity, referral code, wallet, totals, orders, earnings, and payout/settlement values render as text/table data, not editable configuration controls.
- Removing `disabled` or changing DOM values in DevTools can alter local candidate intent only. It cannot unlock backend field authority, tenant authority, or financial authority.

## Reset Findings

- Reset Changes restores the last accepted backend settings baseline, not local defaults.
- Reset clears dirty state and validation/success presentation.
- If a newer backend read arrives while the form is dirty, reset restores that newer accepted backend baseline.
- There is no hidden preserved edit state after cancel/close because the edit form state is component memory only.

## Edit State Findings

- Unsaved edits are temporary React state and are not stored in browser storage or shared cache.
- Navigating away, hard refresh, remount, logout, or tenant/actor replacement clears unsaved edit state and requires backend re-entry.
- Multiple tabs keep independent memory-only edit state. One tab cannot overwrite another tab's edit state through storage or cache.
- A tab can show stale unsaved candidate input while open, but it cannot make that input authoritative without backend mutation authority, which is outside this pass.

## UX Findings

- Editable fields show required markers and validation messages for category/rate/cap input.
- Runtime now shows an Unsaved changes indicator and Reset Changes action.
- Disabled/loading states prevent the normal UI from implying editable authority while backend/category evidence is unresolved.
- The informational notice correctly states that commission changes do not affect past orders, aligning with P3-AB commission snapshot authority.

## Failure Findings

- Initial settings read failure now fails closed by withholding editable controls.
- Refresh during edit drops unsaved memory state and reloads backend authority.
- Logout, session expiration, role loss, and tenant replacement rely on AF-PA1.1 route/session authority and AF-PA1.2 read revalidation; edit state is not persisted across those transitions.
- Permission loss during edit cannot preserve frontend authority because backend endpoint authorization remains decisive and frontend state is only candidate intent.

## Adversarial Findings

- Modifying React state, DOM values, disabled attributes, select options, hidden fields, or local variables can only change local candidate input.
- The frontend cannot fabricate editable policy authority, tenant scope, category authority, financial commission authority, approval authority, or read-only rules.
- Hidden fields cannot become authoritative because inspected configuration form submission uses only visible controlled candidate fields and backend authorization/rules remain decisive.
- Browser storage and multiple tabs do not carry Affiliate Configuration edit truth.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.test.tsx`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Verification

- `npm test -- --run src/components/ProductAffiliateSettingsCard.test.tsx src/services/adminAffiliateApi.test.ts src/hooks/useAffiliateReadiness.test.tsx`

Result: 3 test files passed, 6 tests passed.

## Explicit Certification Answers

- Who owns Affiliate Configuration Edit Authority? Backend runtime: trusted tenant context, backend role authorization, product/category/policy services, PostgreSQL configuration state, and backend affiliate configuration rules.
- Can frontend editing become authority? No.
- Can frontend fabricate editable values? It can fabricate local candidate input only; backend authority remains decisive.
- Can frontend fabricate default values? No. Editable controls now require accepted backend settings authority, and reset uses the backend baseline.
- Can dirty state become authority? No.
- Can readonly fields become editable? They can be locally tampered with in DevTools only; they cannot become backend authority.
- Can disabled controls bypass authority? No.
- Can hidden fields become editable? No authoritative hidden configuration field path was found.
- Can reset restore stale values? Reset restores the latest accepted backend baseline in that component instance; refresh/remount re-enters backend authority.
- Can unsaved edits become authoritative? No.
- Can multiple tabs corrupt edit state? No inspected path; tab edit state is independent memory and not shared through storage/cache.
- Were runtime changes required? Yes, frontend edit-state/default-value hardening was required.
- Is Affiliate Configuration Edit Authority production-certifiable? Yes.
