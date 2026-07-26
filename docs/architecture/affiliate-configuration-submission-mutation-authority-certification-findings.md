# PASS AF-PA1.4 Affiliate Configuration Submission & Mutation Authority Certification Findings

Status: Production-certifiable after frontend duplicate-submission hardening.

Date: 2026-06-25

Scope: POS Admin Affiliate Configuration frontend submission and mutation authority only: product affiliate settings save/enable/disable buttons, mutation functions, API client payload construction, serialization, loading state, success/failure state, response reconciliation, duplicate submission prevention, retry/error behavior, and adversarial payload/response tampering. Backend business rules, backend persistence internals, and database validation were intentionally out of scope.

## Authority Conclusion

Affiliate Configuration Mutation Authority is backend-owned. Backend runtime, trusted tenant context, role authorization, product/category/policy services, backend validation, commission and approval rules, persistence, and backend mutation responses own all mutation truth and mutation result authority.

Frontend submission transports candidate intent only. The frontend may collect category, commission-rate, and optional max-cap input, serialize an allowlisted DTO, dispatch a request, show saving/error/success state, and reconcile accepted backend responses into presentation state. It cannot decide mutation success, fabricate persisted configuration truth, inject tenant/store/owner/actor/version authority, or make a hidden field authoritative.

## Runtime Hardening Performed

The save/enable/disable buttons already disabled during `saving`, and mutation payloads were already allowlisted. A rapid same-tick duplicate click could still enter a mutation handler twice before React committed the disabled button state. That was a frontend duplicate-dispatch gap, not a backend authority bypass.

Hardening:

- Added a synchronous `mutationInFlightRef` fence before request dispatch.
- Added a mutation sequence fence so stale mutation completions cannot patch state or clear loading after a newer product/reset boundary.
- Reset mutation in-flight state when product/admin authority changes and the component re-enters backend read authority.
- Added regression coverage proving rapid duplicate save dispatch sends one request.
- Added regression coverage proving product affiliate mutation DTOs serialize only allowlisted candidate fields and no tenant/store/owner/actor/version fields.

## Mutation Authority Findings

- The inspected product affiliate save flow dispatches to backend endpoints only; it does not write local persistence or React Query authority.
- Frontend validation is preflight UX only. Backend validation remains decisive for readiness, product ownership, category validity, commission ranges, role authorization, tenant scope, and persistence.
- The frontend sets success only after the awaited backend mutation resolves and the current mutation fence still matches.
- Failed mutations set error presentation and re-enter backend reads; they do not mark local success.
- Mutation responses are treated as backend evidence and forced into the accepted presentation baseline only for the current mutation sequence.

## DTO Findings

- Save/enable DTO shape is exactly `commissionRate`, `categoryId`, and `maxCommissionPerItem`.
- Disable sends no request body from the frontend service.
- Frontend DTO construction does not include `tenantId`, `storeId`, `ownerId`, `createdBy`, `updatedBy`, `version`, approval fields, permission fields, readonly display fields, template authority, created/updated timestamps, or hidden business fields.
- Null `maxCommissionPerItem` is candidate intent for no cap, not authority; backend validation remains decisive.
- Under-posting cannot corrupt frontend authority because missing required fields fail frontend preflight or backend validation; frontend never supplies fallback authority fields.

## Payload Findings

- Payloads are constructed from controlled in-memory candidate fields, not hidden DOM inputs or browser storage.
- DevTools can tamper with local candidate values or intercepted JSON, but tampered payload fields are not frontend authority and must be rejected or ignored by backend authority.
- The frontend service does not spread arbitrary form objects into mutation bodies, which prevents accidental over-posting in the inspected path.

## Submission Findings

- Save/enable/disable use `type="button"`; no browser form submit path or keyboard submit path was found for this card.
- Loading state is presentation only and now has a synchronous in-flight fence before request dispatch.
- Normal UI duplicate clicks are blocked by both the synchronous fence and disabled presentation state.

## Response Reconciliation Findings

- Accepted backend mutation responses become the only source used to reset the frontend baseline after success.
- Dirty state is cleared only after the current backend mutation response is accepted.
- Stale mutation completions from an invalidated sequence cannot patch state or clear loading.
- Lifecycle reads still re-enter backend GET authority and can update the backend baseline without promoting dirty local edits.

## Failure Findings

- 400/422 validation failures remain failed mutations and show backend error presentation when available.
- 401/403 remain backend authorization failures; frontend cannot continue as success.
- 404/409/500/timeout/offline failures do not mark success and trigger backend read re-entry where implemented.
- Failed requests leave candidate input visible for correction but do not update the accepted backend baseline.
- Retrying after a terminal failure creates a new candidate request; the previous failed request cannot become success locally.

## Duplicate Submission Findings

- Rapid click/double click is now blocked synchronously before duplicate request dispatch.
- The disabled button remains a UX signal, not the sole protection.
- Browser/network-level retry remains transport behavior; frontend mutation authority still depends on backend response and backend idempotency/validation.
- Duplicate or replayed requests cannot create frontend authority; backend remains mutation authority.

## Security Findings

- Frontend cannot inject tenant, store, owner, identity, permissions, readonly fields, configuration version, financial decisions, approval decisions, or persistence authority through the inspected DTO builder.
- Hidden fields cannot become authoritative because the mutation builder does not read hidden DOM fields or spread form state.
- Modified/intercepted payloads are adversarial client requests only; backend authority must accept, reject, ignore, or validate them.

## Adversarial Findings

- DevTools DOM edits can change local candidate fields only.
- Extra JSON fields in intercepted requests are not generated by frontend and cannot become frontend authority.
- Removed fields cause failed preflight or backend failure, not frontend success.
- Changed JSON can submit different candidate intent but cannot select tenant/store/actor/version authority from the certified frontend path.
- Replay and duplicate request attempts cannot create frontend configuration authority; frontend accepts only current backend responses for presentation.
- Modified responses can affect local presentation only if the browser/network is compromised; they do not create backend persistence or backend mutation authority.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.test.tsx`
- `PointOfSale.Web/src/services/adminAffiliateApi.test.ts`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Who owns Affiliate Configuration Mutation Authority? Backend runtime: trusted tenant context, role authorization, product/category/policy services, backend validation, commission/approval rules, persistence, and backend mutation responses.
- Can frontend fabricate successful mutation? No. Success is shown only after a current backend mutation response resolves.
- Can frontend inject TenantId? No inspected frontend DTO path sends `tenantId`.
- Can frontend inject StoreId? No inspected frontend DTO path sends `storeId`.
- Can frontend inject OwnerId? No inspected frontend DTO path sends `ownerId`.
- Can frontend inject readonly fields? No inspected mutation builder sends readonly fields.
- Can DTO over-posting occur? No in the inspected frontend path; payloads are explicit allowlists.
- Can hidden fields become authoritative? No.
- Can duplicate submission corrupt authority? No frontend authority corruption path remains after the synchronous in-flight fence; backend remains decisive for duplicate/replay handling.
- Can replay create configuration authority? No. Replay is a backend-bound request candidate only.
- Can stale responses overwrite backend truth? No inspected stale frontend mutation completion can patch state after its mutation sequence is invalidated; backend reads and backend mutation responses remain the accepted evidence.
- Were runtime changes required? Yes, duplicate-submission mutation lifecycle hardening was required.
- Is Affiliate Configuration Submission & Mutation Authority production-certifiable? Yes.

## Verification

- `npm test -- --run src/components/ProductAffiliateSettingsCard.test.tsx src/services/adminAffiliateApi.test.ts src/hooks/useAffiliateReadiness.test.tsx`
