# PASS AF-PA2.3 Affiliate Product Eligibility Candidate Decision Certification Findings

Status: Production-certifiable after frontend current-eligibility display hardening.

Date: 2026-06-25

Scope: POS Admin frontend candidate decision lifecycle for Affiliate Product Eligibility in `ProductAffiliateSettingsCard`: affiliate enable/disable presentation, commission input, maximum commission input, category selector, dirty state, reset, current backend baseline, displayed eligibility status, lifecycle refetch, product changes, route changes, failures, and adversarial frontend manipulation. Backend save logic, backend business rules, and persistence were intentionally out of scope.

## Authority Conclusion

Affiliate Product Eligibility Decision is backend-owned. Backend product affiliate endpoints, trusted tenant/store context, product ownership/scope checks, role authorization, category/rule validation, commission validation, mutation acceptance, and persistence decide eligibility truth.

Frontend candidate decisions are temporary intent only. Controlled inputs, dirty fields, reset, local validation, status text, buttons, warnings, browser history, component state, and DevTools edits cannot create Affiliate Product Eligibility, commission authority, category authority, approval authority, or persistence authority.

## Runtime Hardening Performed

- `ProductAffiliateSettingsCard` now derives displayed current eligibility status and enable/save/disable action branching from the accepted backend baseline (`authoritySettings`) instead of editable candidate form state.
- The status badge now explicitly labels backend-accepted state as `Current: Active` or `Current: Inactive`.
- Added a regression proving a lifecycle refetch can update the backend baseline to inactive while dirty candidate inputs remain visible, and the displayed status/action branch follows the backend baseline rather than stale candidate state.

This hardening does not make frontend state authoritative. It prevents candidate state from appearing authoritative when dirty edits are intentionally preserved across lifecycle refetch.

## Candidate Decision Findings

- Commission rate, maximum commission cap, and category selector are controlled React inputs representing unsaved candidate intent.
- Enable/save/disable controls dispatch backend-bound intent only; no local storage, React Query persistence, or client cache writes promote candidate decisions.
- Candidate fields are dirty-tracked against the accepted backend baseline and remain memory-only.
- Frontend validation is guidance only; backend validation and mutation acceptance remain decisive.
- Candidate decisions cannot become eligibility authority without backend acceptance.

## Eligibility State Findings

- Backend Accepted State is stored separately as `authoritySettings`/`authoritySettingsRef` after product-bound response admission.
- The displayed eligibility badge now reads from that backend baseline, not from editable candidate state.
- Stale candidate state cannot display as current eligibility after lifecycle refetch.
- Missing, failed, or product-mismatched settings withhold editable controls and current status presentation.

## Field Dependency Findings

- Category is required before enable/save candidate submission and inactive/missing categories are surfaced as invalid presentation.
- Commission must be between `0.01%` and `50%`; cap must be positive when present.
- Empty cap is candidate intent for no cap, not financial authority.
- Frontend does not silently alter related fields as authority; reset explicitly restores the backend baseline.

## Decision Consistency Findings

- Reset restores the latest accepted backend baseline, including baselines updated by lifecycle refetch while the form is dirty.
- Product changes clear baseline, dirty fields, success/error, saving state, mutation in-flight state, and refetch current product settings.
- Candidate state is component memory only and does not leak through storage/cache across products.

## Lifecycle Findings

- Refresh/remount re-enters backend session and product affiliate read authority.
- Focus, visibility, and online lifecycle events refetch settings/category/readiness authority.
- Browser back/refresh/close while dirty warns that edits are memory-only and unsaved.
- Product route changes invalidate in-flight reads and clear prior product candidate/baseline state.

## UX Findings

- Users can distinguish current accepted eligibility from pending edits through `Current: Active`/`Current: Inactive` plus the unsaved-changes message.
- Dirty state explicitly says to save to apply or reset to discard.
- Success messages are backend-response-confirmed only.
- Error states withhold editable controls when current backend settings authority is unavailable.

## Failure Findings

- Failed reads do not preserve prior-product settings as current state.
- 401/403 denials trigger the previously certified POS authority-loss revalidation path.
- Network/timeout/read failures fail closed when no current accepted settings exist.
- Failed mutations preserve candidate input for correction but do not update the accepted backend baseline or show success.

## Adversarial Findings

- DOM edits, disabled-control tampering, React state edits, and intercepted frontend values can fabricate only local candidate intent.
- The frontend cannot fabricate eligibility truth, commission authority, category authority, tenant/store authority, product identity, or persistence.
- Replayed or mismatched product settings responses are rejected before baseline or status admission.
- Manipulated UI can send a backend-bound request candidate only; business authority begins at backend authentication/authorization and acceptance.

## Decision Lifecycle Map

```text
Backend Accepted State
↓
Frontend Baseline (`authoritySettings` after product-bound backend evidence)
↓
Candidate Decision (controlled commission/category/cap inputs and backend-bound action intent)
↓
Dirty State (memory-only comparison to baseline)
↓
Backend Submission (allowlisted request intent)
↓
Backend Accepted Decision (backend authentication, authorization, product/category/commission validation, persistence)
↓
New Baseline (accepted backend response/read evidence)
```

Business authority begins only at backend request authentication/authorization and backend decision acceptance. Everything before that is frontend presentation or candidate intent.

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

- `npm test -- --run src/components/ProductAffiliateSettingsCard.test.tsx`
- `npm test -- --run src/components/ProductAffiliateSettingsCard.test.tsx src/services/adminAffiliateApi.test.ts src/components/ProductForm.test.tsx src/auth/posVendorSessionAuthority.test.ts src/components/PosRoleRouteGuard.test.tsx`

Result: focused component suite passed, 8 tests passed. Related frontend authority freeze suite passed, 5 test files and 24 tests passed. Existing React Router future-flag, act, product option key, and intentional fail-closed read logs remain unrelated repository warnings.

## Explicit Certification Answers

- Who owns Affiliate Product Eligibility Decision? Backend runtime: product affiliate endpoints, trusted tenant/store context, product ownership/scope, role authorization, business validation, mutation acceptance, and persistence.
- Can frontend fabricate eligibility? No.
- Can frontend fabricate commission authority? No.
- Can frontend fabricate category authority? No.
- Can candidate decisions become business truth? No, not without backend acceptance.
- Can reset restore stale decisions? No inspected path after hardening; reset restores the latest accepted backend baseline.
- Can product switching preserve previous candidate decisions? No; product changes clear candidate/baseline state and re-enter backend read authority.
- Can UI misrepresent eligibility? No inspected path remains after current-status hardening; current status derives from backend baseline.
- Where does business authority begin? At backend authentication/authorization and backend decision acceptance for the submitted request.
- Were runtime changes required? Yes, frontend current-eligibility display/action branching hardening was required.
- Is Affiliate Product Eligibility Candidate Decision production-certifiable? Yes.
