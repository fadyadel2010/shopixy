# PASS SF-PA1.4 Store Configuration Mutation Authority Certification

Store Configuration Mutation Authority is production-certifiable after save-workflow hardening. Backend `AdminStoreConfigController.UpdateStoreConfig`, trusted tenant/session context, `AdminStoreConfigService.UpdateStoreConfigAsync`, EF `StoreConfig.RowVersion` concurrency authority, SQL `StoreConfigs`, SQL persistence, accepted `AdminStoreConfigResponse`, and audit/outbox staging own mutation authority, backend validation, row-version validation, persistence, accepted snapshot, baseline replacement evidence, recovery, and audit truth.

Frontend Storefront Settings owns candidate submission, save request presentation, loading/error/success presentation, validation hints, and confirmation only. Candidate state is submitted as intent with backend row-version evidence. React state, disabled buttons, dirty state, retry, duplicate clicks, network recovery, browser refresh, tabs, and mutation responses cannot commit business truth or replace the accepted baseline unless the response is admitted through the current backend Store Configuration presentation boundary.

## Mutation Findings

- Mutation authority begins at `AdminStoreConfigController.UpdateStoreConfig` after authenticated Owner/Admin authorization, trusted tenant context, request validation, and mandatory `RowVersion`/`If-Match` precondition admission.
- Backend validation remains decisive for store name, theme identity, storefront theme, status, maintenance message, theme shape, behavior shape, email-verification gate for `live`, tenant scope, and row-version conflict detection.
- Frontend Storefront Settings now calls `storeConfigService.updateStoreConfig` instead of direct `api.put`, submits `rowVersion: candidateConfig.rowVersion`, and the shared service mirrors that version into `If-Match`.

## Submission Findings

- Every settings mutation carries backend evidence: trusted tenant scope from the POS session/tenant context plus the candidate's accepted backend `rowVersion`.
- The frontend cannot submit accepted configuration or audit truth; it submits a candidate DTO only. Missing, malformed, stale, or fabricated row-version evidence fails closed at the backend precondition/concurrency boundary.

## Baseline Replacement Findings

- Baseline replacement is frontend presentation only after backend acceptance. Storefront Settings now admits the mutation response through `deriveCurrentStoreIdentityPresentation(acceptedResponse, user)` before calling `setBaselineConfig(acceptedConfig)` and `setCandidateConfig(acceptedConfig)`.
- Failed responses, malformed accepted responses, foreign-tenant responses, missing row versions, or network errors leave the prior baseline intact and keep candidate edits dirty.

## Concurrency And RowVersion Findings

- `RequireExpectedRowVersion` rejects missing or invalid mutation preconditions with 428.
- `ApplyExpectedRowVersion` sets EF original `StoreConfig.RowVersion`; `DbUpdateConcurrencyException` is converted to a 409 conflict. Stale candidates cannot silently overwrite newer backend configuration.
- Candidate edits preserve their original baseline row version; later backend reads may update presentation baseline, but an older candidate still conflicts instead of silently winning.

## Duplicate Submission Findings

- Storefront Settings has `saveInFlightRef` plus the disabled saving state to suppress duplicate clicks while one mutation is in flight.
- Backend remains the decisive duplicate/concurrency guard. A replay with the already-consumed row version cannot commit twice because the row version changes after the first accepted persistence.

## Failure Findings

- 400/401/403/404/409/412/422/429/500/timeouts/offline paths cannot replace baseline. Storefront Settings updates baseline only after an admitted accepted response.
- Backend rejects invalid config, missing tenant/session authority, unauthorized roles, missing preconditions, stale row versions, failed email-verification gates, and persistence conflicts.

## Recovery Findings

- Browser refresh, remount, reconnect, server restart, and application restart recover from backend GET authority. Failed candidate state is memory-only and is discarded on remount.
- Timeout-after-commit may leave the browser uncertain, but it cannot fabricate acceptance. Recovery re-enters backend read authority and converges to SQL truth.

## Adversarial Findings

- Modified mutation DTOs cannot supply tenant/store authority; backend derives tenant from trusted context.
- Modified row versions fail as 428/409 when missing, malformed, or stale.
- Modified accepted responses cannot replace baseline unless they pass current tenant, store name, status, theme, and row-version admission.
- Dirty-state tampering cannot create acceptance because dirty state is presentation only.

## Cross-System Findings

- POS, Storefront, Checkout, Commerce AI, Affiliate, Reports, Analytics, Theme, CMS, and Navigation consume accepted backend configuration or derived projections only after backend persistence/event/read re-entry.
- Failed mutations cannot leak to other modules because candidate state stays in Storefront Settings memory and no cross-module state is updated before accepted backend response admission.
- Concurrent updates converge through row-version conflict detection and backend read recovery.

## Boundary Review

- Candidate Boundary: frontend owns draft values; backend is recovery source; failure keeps candidate dirty.
- Mutation Boundary: backend controller/service own acceptance; frontend submits versioned intent; failure returns 4xx/5xx and no baseline replacement.
- Concurrency Boundary: backend `RowVersion`/EF original value own conflict detection; frontend presents conflict/failure only.
- Persistence Boundary: SQL `StoreConfigs` and EF SaveChanges own committed truth; failed persistence leaves prior truth.
- Acceptance Boundary: accepted `AdminStoreConfigResponse` with current tenant and row-version evidence begins accepted snapshot presentation.
- Presentation Boundary: Storefront Settings renders baseline and candidates separately; candidate controls resume after success/failure.
- Recovery Boundary: backend GET restores truth; browser/React state is not recovery authority.
- Audit Boundary: backend audit/outbox staging owns audit truth; frontend cannot create audit acceptance.

## Mutation Pipeline

Accepted Baseline -> Candidate -> Frontend Validation Hints -> Versioned Mutation Request -> Backend Validation -> RowVersion Validation -> SQL Persistence -> Accepted Response -> Frontend Admission -> Baseline Replacement -> Presentation.

- Mutation Authority begins at `AdminStoreConfigController.UpdateStoreConfig`.
- Accepted Snapshot begins at the backend accepted `AdminStoreConfigResponse` after SQL persistence.
- Baseline Replacement occurs in Storefront Settings only after `deriveCurrentStoreIdentityPresentation` admits that response.
- Presentation resumes from accepted baseline plus memory-only candidate state.

## Runtime Hardening Performed

- Routed Storefront Settings saves through `storeConfigService.updateStoreConfig`.
- Added `rowVersion` to the mutation payload and `If-Match` to the service request options.
- Added `saveInFlightRef` to suppress duplicate save entry while a mutation is pending.
- Replaced refresh-after-save baseline convergence with immediate accepted-response admission, then baseline/candidate replacement only from admitted backend evidence.
- Preserved failure behavior so failed/malformed mutation responses keep the prior baseline and dirty candidate.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

## Regression Freezes Added

- `PointOfSale.Web/src/pages/StorefrontSettings.test.tsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.test.ts`
- `PointOfSale.Tests/Architecture/StorefrontConfigurationAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this SF-PA1.4 findings document.
- Updated all five AI authority context files with SF-PA1.4 truth, risk closure, hardening, and freeze rules.
