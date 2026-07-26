# PASS AF-PA1.6 Affiliate Configuration UX, Accessibility & Human Factors Certification Findings

Status: Production-certifiable after frontend UX/accessibility hardening.

Date: 2026-06-25

Scope: POS Admin Affiliate Configuration human-facing runtime only: navigation, page layout, field understanding, form interaction, validation UX, save/loading/error presentation, unsaved-change recovery, keyboard/screen-reader support, responsive behavior, consistency, and human factors. Backend business rules, persistence, and backend authority implementation were intentionally out of scope.

## UX Conclusion

Affiliate Configuration UX is production-certifiable. The runtime now gives first-time POS Admin users a discoverable Affiliate Management entry, readiness guidance when prerequisites are missing, backend-baseline-backed product affiliate settings, understandable commission/cap fields, backend-confirmed success, actionable failure messages, protected unsaved edits, and accessible labels/status messaging.

Backend authority remains explicit. The UI may explain, validate candidate input, warn about unsaved edits, and present success or failure, but it never represents frontend state as backend truth. Success is shown only after a current backend mutation response is accepted.

## Runtime UX Hardening Performed

- Associated Affiliate Marketing form labels with their controls using `htmlFor`/`id` so screen readers and keyboard users can identify category, commission rate, and max commission cap fields.
- Added `aria-describedby` helper relationships for category, commission rate, and max commission cap so units, bounds, and category context are programmatically available.
- Added `aria-invalid` for inactive or missing category selections.
- Added announced `role="alert"` error messages and `role="status" aria-live="polite"` success/dirty messages.
- Expanded the dirty message from a bare indicator to explicit recovery guidance: save to apply, reset to discard.
- Added `beforeunload` and browser-back protection while product affiliate edits are dirty so refresh, close, or browser back attempts warn before losing memory-only candidate edits.
- Changed commission/cap layout from fixed two-column to single-column on small screens and two-column on larger screens.

These are presentation and recovery controls only. They do not create edit, read, mutation, validation, authorization, tenant, financial, approval, or persistence authority.

## UX Findings

- Navigation: Affiliate Management is available from the POS sidebar for Owner/Admin presentation roles and direct routes are presentation-guarded.
- Workflow: First-time users see readiness checking, setup requirements if not ready, and affiliate account management once ready. Product-level Affiliate Marketing settings show category, commission rate, optional cap, status, save/enable/disable, reset, and explanatory commission timing.
- Layout: Related settings are grouped inside a single Affiliate Marketing card with header, category, commission inputs, informational notice, messages, dirty indicator, and actions.
- Visual hierarchy: Header/status, fields, helper text, notices, and actions are ordered predictably. Readiness full-page state uses a checklist and setup navigation.
- Information density: The product settings card is compact but understandable after hardening; the admin list is denser but remains a management table rather than the product edit form.

## Workflow Findings

- First-time entry is understandable through the sidebar label `Affiliate Management` and page title.
- Missing prerequisites are recoverable through explicit setup checklist actions.
- Product configuration is backend-baseline-based and does not render editable controls until current settings authority is available.
- Users can intentionally discard candidate edits with `Reset Changes`.
- Refresh, close, and browser back while dirty now receive a warning; broader in-app link interception remains a future UX improvement if standardized across POS forms.

## Field Understanding Findings

- `Marketplace Category *` is required and explains the selected category path or why the category is unavailable.
- `Commission Rate (%) *` includes the `%` unit, numeric bounds, and helper text explaining it is a percentage of paid amount earned by affiliates.
- `Max Commission Per Item` is optional and explains that an empty value means no cap.
- The commission timing notice tells admins that changes affect future order-time calculations only and do not alter past orders or earned commissions.
- Percent versus fixed cap is now visually and programmatically separated by label, unit, helper text, and input grouping.

## Validation Findings

- Validation is pre-submit and actionable: select a category, choose a different active category, enter a commission rate between `0.01%` and `50%`, and enter a positive max cap.
- Backend validation/failure messages are displayed as errors and do not mark success.
- Error messages are now announced with `role="alert"`.
- Validation copy is direct and non-blaming.

## Save Experience Findings

- Editing is visible through dirty state.
- Saving/enabling/disabling is visible through disabled controls, spinner text, and in-flight duplicate-submission fences from AF-PA1.4.
- Saved state is visible only after backend mutation response acceptance.
- Failed save leaves candidate input available for correction and re-enters backend reads where implemented.

## Unsaved Changes Findings

- Dirty state is field-level, memory-only, and compared with the accepted backend baseline.
- Dirty state now explicitly tells users how to save or discard.
- Refresh/close is protected by `beforeunload` while dirty, and browser back is protected by a history guard.
- Reset restores the latest accepted backend baseline and clears dirty/error/success presentation.

## Loading Findings

- Readiness loading and settings loading use skeleton/pulse states instead of presenting empty success.
- Settings read failure withholds editable controls and shows a clear unavailable-settings message.
- Readiness-not-ready state is not mistaken for success because it is a warning checklist with required setup actions.

## Failure UX Findings

- 401/403 authority loss is handled by AF-PA1.5 protected-route revalidation and presentation withholding.
- 404/detail absence clears or withholds presentation through prior read-authority hardening.
- 409/422/backend validation failures remain errors and preserve candidate input for correction.
- 500/offline/timeout failures do not fabricate success and leave retry through the same backend-bound save/read actions.
- Retry is recoverable but not always shown as a dedicated button in every failure state; existing recovery is through correction, save retry, lifecycle refetch, and browser refresh.

## Accessibility Findings

- Keyboard users can tab through category, commission rate, max cap, reset, disable, enable, and save controls.
- Labels are now programmatically associated with form controls.
- Helper text is now programmatically associated with controls.
- Error/success/dirty messages are now announced to assistive technology.
- Focus styles rely on shared POS input/button classes and remain visible through existing focus ring classes where present.
- The commission/cap inputs now stack on mobile, improving zoom and narrow-screen readability.
- The admin affiliate table rows remain mouse-clickable without row-level keyboard activation; because this pass certifies configuration editing, this is noted as a residual management-list UX improvement, not a product configuration blocker.

## Human Factors Findings

- No irreversible product affiliate mutation is presented without backend confirmation.
- Disable Affiliate is visibly distinct and red-accented, but it does not currently ask for confirmation. This is acceptable because backend authority remains decisive and the action can be re-enabled, but a future confirmation could reduce accidental disablement.
- Users do not need to remember prior values because `Reset Changes` returns to the backend baseline.
- Commission snapshot wording prevents the misleading expectation that past commissions will change.

## Consistency Findings

- Buttons, cards, input classes, loading states, and status colors match existing POS Admin patterns.
- Affiliate readiness checklist reuses the same warning component for full-page and inline contexts.
- Runtime messages now align with existing POS success/error alert patterns while adding accessibility roles.

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

- `npm test -- --run src/components/ProductAffiliateSettingsCard.test.tsx src/services/adminAffiliateApi.test.ts src/hooks/useAffiliateReadiness.test.tsx src/auth/posVendorSessionAuthority.test.ts src/components/PosRoleRouteGuard.test.tsx`

Result: 5 test files passed, 22 tests passed.

## Explicit Certification Answers

- Can first-time users understand the workflow? Yes.
- Can users distinguish editing from saving? Yes; dirty state and disabled/saving indicators distinguish them.
- Can users distinguish saving from saved? Yes; saving uses spinner text and saved uses backend-confirmed success.
- Can users recover from mistakes? Yes; validation messages guide correction and reset discards candidate edits.
- Can users accidentally lose changes? Refresh, close, and browser back are now warned while dirty; broader in-app link interception remains future enhancement.
- Can validation messages guide correction? Yes.
- Can keyboard-only users complete configuration? Yes for the product configuration card.
- Can screen readers understand the page? Yes for the product configuration card after label/helper/status hardening.
- Can users misunderstand backend authority? No inspected production blocker remains; success is backend-confirmed and frontend validation is candidate guidance only.
- Were runtime UX changes required? Yes.
- Is Affiliate Configuration UX production-certifiable? Yes.
