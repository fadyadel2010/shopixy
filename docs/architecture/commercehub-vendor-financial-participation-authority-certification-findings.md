# CommerceHub Vendor Financial Participation Authority Certification Findings

Date: 2026-06-16

## Verdict

CommerceHub Vendor Financial Participation Authority is production-certifiable for inspected POS/Admin vendor financial runtime paths after runtime hardening.

## Findings

- Financial findings: balances, totals, transaction rows, statements, commission rows, refund debt impacts, and current balance presentation now require current backend vendor identity, finite backend values, canonical transaction direction/type, backend currency, backend ids, and backend timestamps. Missing or malformed financial DTOs fail closed instead of being formatted as zero-valued truth.
- Dashboard findings: CommerceHub Finance, Earnings, and Payments dashboards now route through a dedicated frontend authority boundary and withhold stale dashboard data during current refetch. Dashboard metrics cannot contradict backend truth through cache replay or frontend recomputation on inspected paths.
- Debt findings: debt status, amount owed, debt suspension/grace state, required action, and payment eligibility derive from backend finance/payment DTOs plus current backend session lifecycle/capability evidence. Debt-blocked or suspended evidence cannot be converted into healthy presentation by fallback labels or boolean repair.
- Payout findings: payout-method rows require current vendor ownership, canonical method type, valid backend id/date, and verified/default booleans. Selected payment methods are derived from current accepted backend rows only and are cleared when the current backend row is absent. Payout history requires canonical payout status, backend id/date, amount, and currency.
- Settlement findings: statement and settlement/financial transaction presentation requires backend `entityType=Vendor`, same-vendor `entityId`, canonical financial transaction type/direction, backend reference ids, backend timestamps, and finite running balances. Settlement eligibility is not calculated or fabricated by the frontend.
- Restriction findings: finance/payment/payout actions remain gated by current backend-issued POS vendor capabilities, lifecycle/debt block flags, and current backend payment-dashboard `canSubmitPayment` evidence. Restricted vendors cannot preserve payment submission authority from route state, stale cache, or mutation state.
- Bootstrap findings: focus, visibility, online, popstate, BFCache pageshow, interval, and protected authority-loss events re-enter `/auth/session`, purge scoped CommerceHub vendor queries, and withhold route presentation during validation. Bootstrap cannot resurrect financial authority from storage or cache.
- Cache findings: React Query keys remain current tenant/user scoped under `commercehub-vendor-session`; actor/tenant transitions clear query state; financial pages withhold data during current refetch; payment/payout mutations invalidate/refetch instead of patching balances, debts, payouts, settlements, or statements into cache.
- Failure findings: protected financial endpoints, including `/api/vendors/payout-methods`, participate in POS vendor authority-loss detection. 401/403 failures purge/revalidate vendor authority, malformed DTOs fail closed, and proof responses require backend proof DTO shape before opening a proof URL.
- Adversarial findings: local storage, session storage, React state, route/history state, contexts, caches, mutation responses, selected rows, proof responses, cross-tab events, feature flags, fallback formatting, and frontend arithmetic cannot fabricate balances, debt status, payout authority, settlement eligibility, payment eligibility, or tenant scope on inspected paths.

## Runtime Hardening Performed

- Added `commerceHubVendorFinancialParticipationAuthority` frontend boundary.
- Added backend `VendorId` evidence to CommerceHub vendor earnings responses.
- Routed Finance, Earnings, and Payments dashboards/statements/proofs/payment methods through the boundary.
- Withheld financial presentation during active backend refetch.
- Fenced selected payment method state to current accepted backend rows.
- Added payout-method endpoint authority-loss detection.

## Runtime Files Modified

- `PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs`
- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/pages/CommerceHubFinance.jsx`
- `PointOfSale.Web/src/pages/CommerceHubEarnings.jsx`
- `PointOfSale.Web/src/pages/CommerceHubPayments.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts`
- Updated `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns vendor financial participation authority? Backend financial, settlement, payout, commission, debt, governance, lifecycle, vendor, tenant, and capability authorities own truth; frontend owns derivative presentation only.
- Can frontend fabricate balances? No. Balances require current backend DTO authority and finite backend values; malformed/missing values fail closed.
- Can debt-blocked vendors appear financially healthy? No on inspected paths. Debt and restriction presentation derives from backend debt/lifecycle/payment evidence and rejects contradictions.
- Can payout authority be bypassed? No on inspected paths. Payout methods require current vendor ownership and actions remain backend-authorized.
- Can settlement eligibility be fabricated? No. Settlement/statement presentation is same-vendor backend transaction evidence only; eligibility is not computed by frontend.
- Can stale balances survive refresh? No on hardened paths. Financial presentation is withheld during current refetch.
- Can cache restore financial authority? No. Cache is scoped, purged on actor/tenant/authority loss, and cannot patch financial truth.
- Can tenant leakage occur? No on inspected paths. Financial DTOs and rows require current vendor identity and mixed/foreign evidence fails closed.
- Were runtime changes required? Yes.
- Is Vendor Financial Participation Authority production-certifiable? Yes, for inspected POS/Admin vendor financial runtime paths after the listed hardening and freezes.

## Verification

- `npm test -- --run src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts src/auth/posVendorSessionAuthority.test.ts`
