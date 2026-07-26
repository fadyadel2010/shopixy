# PASS AF-PA2.4 Affiliate Product Eligibility Decision Commitment Certification Findings

Status: Production-certifiable after frontend stale-read commitment fencing.

Date: 2026-06-25

Scope: POS Admin Affiliate Product frontend decision commitment and backend decision acceptance boundary only: enable, disable, save, mutation hooks, payloads, loading state, response handling, baseline replacement, current badge, double actions, stale responses, lifecycle, failures, and adversarial frontend manipulation. Backend persistence internals and backend business-rule implementation were intentionally not reviewed.

## Authority Conclusion

Affiliate Product Decision Commitment is backend-owned. Backend product affiliate endpoints, trusted tenant/store context, product ownership/scope, role authorization, category/commission validation, mutation acceptance, persistence, and backend response evidence decide committed eligibility.

Frontend owns only candidate intent, submission, loading, dirty tracking, temporary form state, response rendering, and accepted-response baseline presentation. Frontend cannot commit eligibility, fabricate committed authority, or persist mutation results.

## Runtime Hardening Performed

One frontend commitment-ordering gap was found and closed.

- Problem: a settings GET started before or during a mutation could resolve after an accepted mutation response and replace the newly accepted mutation baseline/current badge with older backend-read presentation evidence.
- Hardening: `ProductAffiliateSettingsCard` now invalidates outstanding settings reads when a mutation begins and again immediately before accepting a mutation response into the frontend baseline.
- Regression: added coverage proving a delayed stale settings read cannot overwrite an accepted enable response or change `Current: Active` back to stale inactive presentation.

This hardening remains presentation-only. Backend acceptance is still the only commitment point.

## Decision Commitment Findings

- `Current: Active` / `Current: Inactive` derives from `authoritySettings`, the accepted backend baseline, not editable candidate state.
- Enable, Save, and Disable submit backend-bound candidate intent only.
- Local candidate state is dirty-tracked and memory-only; it cannot become committed state.
- Success messages are set only after a current backend mutation response is admitted.
- Failed mutations do not update `authoritySettings`, clear dirty state, or display committed success.

## Payload Findings

- Save/enable payload shape is allowlisted to `commissionRate`, `categoryId`, and `maxCommissionPerItem`.
- Disable sends no frontend request body.
- Payloads do not include tenant, store, owner, actor, permission, readonly, version, approval, persistence, created/updated, or hidden business authority fields.
- Empty max cap serializes as `null`, which is candidate no-cap intent only.
- The frontend service does not spread arbitrary form objects, preventing frontend over-posting in the inspected path.

## Response Commitment Findings

- Backend mutation responses carry `ProductId` evidence and are rejected if they do not match the current product.
- Current mutation sequence fencing prevents stale mutation completions from patching state after product/reset authority changes.
- Accepted mutation responses force-replace the frontend baseline and clear dirty state.
- Outstanding settings reads are now invalidated before mutation dispatch and before mutation response commitment, so stale reads cannot overwrite a newer accepted decision.
- Current badge and action branch follow the accepted backend baseline only.

## Concurrent Decision Findings

- Multiple tabs hold independent memory-only candidate state.
- Another administrator can change backend settings while the current tab is dirty; lifecycle refetch updates the backend baseline/current badge without promoting dirty candidate inputs.
- Reset restores the latest accepted backend baseline in the tab.
- A stale dirty candidate can be submitted later only as a backend-bound request; backend acceptance or rejection decides commitment.
- Refresh/remount clears memory-only candidate state and re-enters backend session/read authority.

## Double Action Findings

- Save spam and same-tick duplicate clicks are blocked by a synchronous in-flight mutation fence before request dispatch.
- Enable then immediate Disable, and Disable then immediate Enable, cannot dispatch as overlapping UI actions in one component because action branching follows accepted backend baseline and `saving`/in-flight fences block concurrent mutation dispatch.
- Keyboard submit is not a separate path because the card buttons are `type="button"` and no form submit handler was found.
- Network-level replay remains backend-bound candidate traffic and cannot create frontend commitment.

## Lifecycle Findings

- Product changes clear accepted baseline, dirty fields, success/error, saving state, and mutation in-flight state before refetch.
- Focus, visibility, and online lifecycle events re-enter backend reads.
- Browser refresh/remount re-enters backend session and product affiliate reads.
- Browser back/refresh/close while dirty warns that candidate edits are unsaved and memory-only.
- Authority loss through 401/403 re-enters the previously certified POS authority-loss revalidation path.

## Failure Findings

- 400/422 backend validation failures remain errors and do not update committed state.
- 401/403 backend denials trigger protected-route/session revalidation and cannot display success.
- 404 product/scope failures fail closed and cannot repair from old product state.
- 409, 500, timeout, and offline failures do not set success, do not clear dirty state as committed, and refetch backend evidence where implemented.
- Backend rejection cannot update the current badge because `authoritySettings` is not replaced on failed mutation.

## Adversarial Findings

- Replay Save/Enable/Disable can only send backend-bound request candidates; backend acceptance remains decisive.
- Modified DTOs are adversarial requests, not frontend authority, and the certified frontend builder does not emit authority fields.
- Modified responses cannot create backend persistence; product-mismatched responses are rejected before baseline admission.
- Sequence reordering is fenced across mutations and, after this pass, across stale settings reads versus accepted mutation responses.
- Frontend cannot fabricate committed business decisions through DOM edits, React state edits, disabled-control tampering, hidden fields, browser storage, route history, or cache manipulation.

## Decision Commitment Timeline

```text
Backend Accepted Baseline
↓
Frontend authoritySettings / authoritySettingsRef
↓
Candidate Decision
↓
Enable / Disable / Save submission intent
↓
Pending mutation with in-flight and sequence fences
↓
Backend Acceptance
↓
Committed Decision
↓
Accepted backend response admitted for current product and current mutation
↓
Current Badge updates from accepted baseline
↓
New Baseline replaces frontend authoritySettings and dirty state resets
```

Exact commitment point: backend mutation acceptance. The frontend baseline/current badge updates only after the accepted backend response is admitted.

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

- `npm test -- --run src/components/ProductAffiliateSettingsCard.test.tsx src/services/adminAffiliateApi.test.ts src/components/ProductForm.test.tsx src/auth/posVendorSessionAuthority.test.ts src/components/PosRoleRouteGuard.test.tsx`

Result: 5 test files passed, 25 tests passed. Existing React Router future-flag/act warnings, existing product option key warning, and intentional fail-closed affiliate-settings read logs remain unrelated.

## Explicit Certification Answers

- Who owns Affiliate Product Decision Commitment? Backend product affiliate mutation authority and backend acceptance.
- Can frontend commit eligibility? No.
- Can payload fabricate committed authority? No; inspected frontend payloads are allowlisted candidate fields only.
- Can stale responses overwrite committed decisions? No after stale-read commitment fencing and existing mutation sequence/product binding.
- Can Enable/Disable races corrupt commitment? No inspected frontend race remains; in-flight fences, accepted-baseline action branching, and backend acceptance are decisive.
- Can replay create committed eligibility? No. Replay is backend-bound candidate traffic only.
- Can backend rejection update current state? No.
- Where does commitment occur? At backend mutation acceptance; frontend presentation updates only when the accepted backend response is admitted.
- Were runtime changes required? Yes, frontend stale-read commitment fencing was required.
- Is Affiliate Product Decision Commitment production-certifiable? Yes.
