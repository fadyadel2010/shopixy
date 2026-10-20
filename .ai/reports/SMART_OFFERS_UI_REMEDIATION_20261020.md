# Smart Offers UI remediation — 2026-10-20

## Verdict: CONDITIONAL

Implemented and validated the confirmed draft-reset, styling, modal-layout and preview-contract defects. The original **screenshot-only intermittent text disappearance was not reproduced** in this Edge session; it cannot honestly be attributed to one of these defects. Backend certification also retains six unrelated failing assertions. No API restart, deployment, schema change, provider-setting change, or existing-offer mutation was required.

## Baseline and causes

- Actual environment: POS Admin `http://localhost:5173/ecommerce/smart-offers`, AhramStore `http://ahramstore.localhost:5174/`; supplied accounts, existing headed `opencode-ui-test` session. Nissan was not used.
- The real initial offer list was empty. Numeric priority was `0`; optional numeric controls were empty with **example placeholders**, not populated defaults. Initial render/loading, edit, preview and reopening paths were inspected. Ordinary interaction did not demonstrate lost DOM values or unexpected control remounts.
- **Reproduced state-loss defect:** the old editor's `[offer, open]` effect reinitialized the entire form and hid preview whenever the offer object's reference changed. The pre-fix regression recorded an edited priority of **17 being overwritten by 99** after a replacement DTO arrived, with the same input DOM node. This is an initialization/state-ownership bug, not a remounting bug. The current page normally retains its edit-object reference during list refresh, so this is **not proof of the original raster flicker's cause**.
- **Confirmed CSS collision:** the modal inserted an unscoped `.input` style into the document, overriding shared Admin controls while mounted. Removed it in favor of existing shared `.input`, `.label`, `.btn-primary` and `.btn-secondary` classes. No global transition disabling, visibility tricks, timers or forced renders were added.
- The former dark marketing-style modal had two competing scrollbars, actions below the viewport, an oversized unused preview column, and inconsistent native select/input styling. Compared against `src/index.css`, the Admin user-dialog/Radix pattern, and existing form pages.
- Preview differed from Storefront: it exposed the private coupon code and fabricated an absent description/artwork section. The public API intentionally omits coupon metadata and Storefront redeems a server-verified token. Preview now reflects these public-content semantics, uses the configured color, omits absent description/artwork and explicitly labels empty title/button placeholders as preview-only.

## Implementation

| Files | Change |
|---|---|
| `PointOfSale.Web/src/pages/SmartOfferEditor.jsx` | One lazy-initialized draft per opening/record; same-record prop refresh cannot overwrite edits. Controlled empty/zero values, numeric enum mapping, real label associations, backend-derived numeric constraints, whitespace-aware coupon disabling. Existing Radix modal infrastructure supplies focus trapping/body-scroll locking/Escape handling. One content scroller, visible footer, responsive layout and portal RTL. |
| `PointOfSale.Web/src/pages/SmartOfferPreview.jsx` | Public-content preview, localization, live copy/color/artwork updates, no private code or invented description. |
| `PointOfSale.Web/src/pages/SmartOffersPage.jsx` | Shared Admin layout/buttons/controls, localized details dialog, stable statistic slots, explicit statistics/read errors and retry; failed details do not hide a successfully loaded table. Existing request-generation and mutation guards remain intact. |
| `PointOfSale.Web/src/i18n/locales/{en,ar}.json` | Feature-only labels, preview limitations, units/frequency guidance, eligibility explanation and error text. |
| `PointOfSale.Web/src/pages/SmartOfferEditor.test.tsx` | Draft refresh/reset, DOM identity, style isolation, controlled numbers/selects, validation, public preview, rapid optional-content replacement, numeric enums, saving lock and RTL regressions. |
| `PointOfSale.Web/src/pages/SmartOffersPage.workflow.test.tsx` | Delayed hydration, save/reopen, error recovery, duplicate retry suppression and failure/empty-state distinctions. |
| `PointOfSale.Web/src/pages/Offers.phase3.test.tsx` | Only the form selectors affected by the new portal were adapted; existing assertions retained. |
| `PointOfSale.Storefront/src/storefront/services/smartOfferEngine.phase3.test.ts` | Timing/scope/scroll/display-limit/session regression tests. **No Storefront production source changed.** |
| `tests/browser/smart-offers-20261020-*.js` | Scoped baseline, controls/RTL/layout, inactive persistence/readback, intercepted failure/loading and isolated Storefront evidence scripts. |

## Executed validation

- **67 Admin tests passed** across the editor, workflow, existing Phase 3 offers, and information-architecture suites. **6 Storefront engine tests passed.** Before implementation, the new initial regression suite had 5 failures and 2 passes; the draft-overwrite assertion specifically failed with `17 → 99`.
- **Admin build PASS; Admin `tsc --noEmit` PASS; Storefront `tsc && vite build` PASS.** Existing bundle-size/dependency warnings remain. Large initial checks hit 120-second limits; completed reruns used longer finite timeouts and isolated backend build artifacts.
- Focused Admin lint and Storefront-test lint passed with no errors. Admin retains the pre-existing `SmartOffersPage` effect-dependency warning; the legacy ESLint configuration also emits a deprecation notice. Existing Phase 3 promotion mocks log unrelated product/category-loading warnings while their assertions pass.
- Backend `FullyQualifiedName~SmartOffer`: **34 passed, 6 failed, 1 skipped**. Five failures expect historical certification-document markers; one expects the obsolete literal `[Authorize(Roles = "Owner,Admin")]` in `AuditLogsController`. The unchanged HEAD/controller uses permission policies. These files/assertions were not modified. The configured database integration test remained skipped. The first isolated-output invocation could not find the solution root; rerunning with the repository-supported `POINT_OF_SALE_SOLUTION_ROOT` resolved that harness issue.
- Headed Edge: actual values and computed text/background/visibility checked, not just screenshots. Verified rapid trigger/status/scope/discount changes, clear/restore, zero/null handling, preview visibility, coupon-disabled states, native invalid/corrected values, three repeated modal cycles, Escape, focus-controlled modal behavior, Arabic/mixed text, and footer access. Desktop/narrow layouts: **1440×1000, 1280×800, 768×900, 390×844**. Populated page/table additionally checked at widths 1440/1280/768/390 without document horizontal overflow.
- Delayed list/stat responses were held in an extra browser tab: an open draft and preview survived release. Intercepted 503 save/read failures retained input, did not masquerade as empty data, and recovered on retry. Those failing mutations **never reached the API**.
- Real UI create **201**, update **200**, admitted `If-Match` concurrency evidence, save/reopen and full-navigation readback passed. Zero priority, changed trigger/scope/depth, session flag and nullable values persisted. The first script timed out waiting for the reload's `load` event **after successful create/update assertions**; a separate read-only continuation completed navigation/readback/details validation. No second fixture was created.
- Real authenticated AhramStore API returned `[]` and excluded the inactive fixture. In an isolated copied browser context, an intercepted **public DTO** triggered the real Storefront popup: configured title/button, no invented description/private code, and once-per-session exclusion after reload all passed. This is **not** evidence of live active-coupon publication/redemption.
- Admin feature requests succeeded, with no runtime console errors; existing React Router future warnings remain. Storefront had expected guest-session `/auth/me` and `/auth/refresh` 401s **before login**, followed by successful supplied-account login. Isolated popup validation recorded **zero page errors and zero failed API responses**. Intentional intercepted 503s are separately documented.

## Safe retained test data

Exactly one new fixture was created and updated:

- ID: `74f9a885-63ad-4902-9250-73d1bec97743`
- Name: `QA Smart Offers UI 20261020 18bc094d — INACTIVE`
- Status: **Inactive**, no coupon code or generated coupon; therefore not eligible for Storefront publication/redemption.
- Retained, not deleted or activated. Final admitted row version: `11089440`. No customer, cart, checkout, order, provider configuration or existing offer was changed.

## Evidence

All evidence: [`../evidence/smart-offers-20261020/`](../evidence/smart-offers-20261020/).

Equivalent desktop viewport (**1440×1000**) before/after:

| State | Before | After |
|---|---|---|
| Empty page | [before-page.png](../evidence/smart-offers-20261020/before-page.png) | [after-page.png](../evidence/smart-offers-20261020/after-page.png) |
| Fresh creation | [before-create.png](../evidence/smart-offers-20261020/before-create.png) | [after-create.png](../evidence/smart-offers-20261020/after-create.png) |
| Edited live preview | [before-edited-preview.png](../evidence/smart-offers-20261020/before-edited-preview.png) | [after-edited-preview.png](../evidence/smart-offers-20261020/after-edited-preview.png) |

Fresh creation intentionally now enables preview by default. Edited-preview captures use matching core content/numbers but different trigger/session selections and scroll positions; they are not pixel-identical interaction states. Additional screenshots cover RTL desktop/390px, scrolled 1280/768/390px, populated tables, optional artwork, loading/post-loading, save/read errors, persisted reopen/details and isolated Storefront popup.

Key machine evidence: `regression-before.txt`, `frontend-tests-final.txt`, `storefront-tests.txt`, `admin-build-final.txt`, `admin-typecheck-final.txt`, `storefront-build.txt`, `backend-tests-verified.txt`, `browser-controls.txt`, `browser-async.txt`, `browser-readback.txt`, `browser-layout.txt`, `browser-storefront.txt`, and request/console logs. The baseline script's initial console-only output is not treated as a structured control-value result.

## Remaining limitations

1. No claim that an intermittent browser-paint-only disappearance was reproduced or definitively cured. The observed state-overwrite defect is fixed and covered, but an exact original reproduction sequence/browser trace is still needed to establish whether it explains the reported screenshots.
2. No live active-coupon activation/redemption or checkout financial testing: intentionally avoided touching real offers/customer commerce state. Public display and inactive persistence were validated as described above.
3. Six existing backend documentation/source-freeze failures and one configured integration skip prevent an unconditional cross-system PASS. No unrelated tests/docs were rewritten to hide them.
