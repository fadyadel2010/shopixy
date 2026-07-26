# PASS M1-F CommerceHub Vendor Governance Authority Certification Findings

Date: 2026-06-16

## Certification Result

CommerceHub Vendor Governance Authority is production-certifiable for inspected frontend runtime paths after Super Admin governance DTO boundary hardening and stale selected-governance state removal.

Backend governance authority, Super Admin authority, vendor authority, lifecycle authority, capability authority, debt authority, visibility authority, participation authority, and tenant authority own governance truth. The frontend owns derivative presentation and workflow UX only.

## Governance Findings

- Vendor governance decisions remain backend-owned through SQL governance, Super Admin, vendor, lifecycle, debt, participation, visibility, and tenant authority.
- POS vendor presentation already requires backend-issued lifecycle/capability/eligibility evidence; blocked, suspended, debt-blocked, rejected, or disabled lifecycle states cannot carry operating capabilities.
- Super Admin governance dashboard/listing DTOs now require explicit frontend boundary validation before presentation.

## Action Findings

- Suppress, restore, suspend, and tenant restore workflow messages now require mutation responses to match the initiated entity and backend action.
- Mutation responses are not written into governance caches as truth; they trigger invalidation/refetch of current backend governance authority.
- Super Admin route and API authority remains capability/session-gated, with 401/403 authority loss purging Super Admin presentation and query caches.

## Presentation Findings

- Dashboard metrics require SQL authority-owner evidence, bounded non-negative counts, and backend timestamps.
- Listing rows require valid backend GUIDs, canonical vendor governance statuses, canonical tenant statuses, canonical visibility states, suppression/orphan consistency, and no hidden-state visibility contradictions.
- Governance aliases and malformed DTOs fail closed rather than being normalized into presentation.

## Transition Findings

- Governance refresh no longer retains previous listing data as current authority.
- Selected listing/action context is derived from current accepted query rows only; stale selected React state cannot survive a refresh as action authority.
- Action controls are disabled during current governance refresh and in-flight governance mutations.

## Consistency Findings

- Vendor/tenant suspended, banned, deleted, or missing states cannot appear with published visibility in accepted frontend listing presentation.
- Suppression state must agree with suppression owner/reason/timestamp evidence.
- Orphan state must agree with orphan reason evidence.

## Bootstrap Findings

- Super Admin governance routes enter through current Super Admin session/capability authority.
- Initial load, refresh, focus, visibility, reconnect, and session revalidation can refetch backend governance only; they cannot restore governance truth from browser state.

## Cache Findings

- React Query caches remain derivative and are purged on Super Admin authority loss.
- Governance list query placeholder data was removed so stale listings are withheld during current authority refresh.
- Selected row state no longer preserves a stale governance row outside current backend query authority.

## Failure Findings

- 401/403 protected Super Admin failures redirect/purge Super Admin session and governance caches.
- Malformed dashboard, listing, or action DTOs throw and fail closed.
- 404/409 action failures do not repair governance presentation; they surface failure and require backend authority refetch.

## Adversarial Findings

- Governance replay, action replay, dashboard replay, cache replay, selected-row replay, placeholder-data replay, capability replay, visibility replay, actor replay, tenant replay, and malformed DTO injection cannot become frontend governance authority on inspected paths.
- Blocked, suspended, banned, deleted, missing, or debt-restricted vendors cannot be represented as approved/active operating vendors by accepted frontend boundaries.
- Backend governance authority cannot be bypassed by frontend state or mutation responses.

## Runtime Hardening Performed

- Added `marketplaceGovernanceAuthority` for Super Admin marketplace governance dashboard, listing, and action DTO validation.
- Routed `marketplaceGovernanceService` reads and mutations through target-validated authority derivation.
- Removed marketplace governance listing `placeholderData` during refresh.
- Changed `MarketplaceGovernancePage` selected listing/action context to derive from current accepted backend rows and disabled actions during refresh/mutation.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/super-admin/marketplace-governance/marketplaceGovernanceAuthority.ts`
- `PointOfSale.Web/src/super-admin/marketplace-governance/marketplaceGovernanceService.ts`
- `PointOfSale.Web/src/super-admin/marketplace-governance/MarketplaceGovernancePage.tsx`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/marketplace-governance/marketplaceGovernanceAuthority.test.ts`

## Architecture Updates

- Updated all five authority context files with M1-F truth, risk, hardening, and freeze rules.
- Added this findings document.

## Explicit Answers

- Who owns vendor governance authority? Backend governance, Super Admin, vendor, lifecycle, capability, debt, visibility, participation, and tenant authorities own vendor governance truth.
- Can frontend fabricate governance? No.
- Can governance actions bypass Super Admin authority? No inspected path permits this; actions require Super Admin route/API authority and backend mutation acceptance.
- Can blocked vendors appear approved? No on inspected paths; blocked/suspended/banned/deleted/missing states fail closed or suppress visibility.
- Can capabilities survive governance revocation? No on inspected POS vendor paths; lifecycle/capability contradiction rejection prevents operating capabilities in restricted states.
- Can visibility survive governance suppression? No on inspected paths; backend suppression owns visibility and frontend rejects suppressed/published contradictions.
- Can stale governance survive refresh? No; listing placeholder authority was removed and selected rows derive from current query authority.
- Can cache restore governance? No.
- Can tenant leakage occur? No inspected tenant leakage path remains; governance rows require backend tenant/listing identity and Super Admin authority.
- Were runtime changes required? Yes.
- Is Vendor Governance Authority production-certifiable? Yes, with residual deployed-browser chaos coverage retained for multi-tab, BFCache, delayed storage, and live governance-transition scenarios.
