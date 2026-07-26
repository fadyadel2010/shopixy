# PASS SF-PA1.3 Store Configuration Candidate Authority Certification

Store Configuration Candidate Authority is production-certifiable after runtime baseline/candidate separation hardening. Backend SQL `StoreConfig`, SQL `Tenant`, trusted tenant/session context, `AdminStoreConfigService`, and Store Config read admission own accepted configuration, baseline configuration, store identity, currency, branding, company identity, version evidence, persistence, and recovery truth. Frontend Storefront Settings owns only memory-only candidate configuration, form state, dirty tracking, reset presentation, validation hints, navigation, and temporary preview.

## Baseline Findings

Baseline originates only from `storeConfigService.getStoreConfig()` after backend `AdminStoreConfigController.GetStoreConfig`, trusted tenant context, service admission, DTO tenant evidence, `rowVersion`, `themeId`, valid store name, and valid status pass frontend read admission. Frontend cannot create a baseline. A backend replacement, server restart, browser refresh, or React remount replaces presentation by re-reading backend truth.

## Candidate Findings

Storefront Settings now keeps `candidateConfig` separate from `baselineConfig`. Candidate state begins only after a backend-admitted baseline has been cloned into memory. Candidate edits may change form controls and temporary preview copy only; they do not replace baseline, store identity, currency, branding, company identity, row version, persistence, or recovery truth.

## Dirty Tracking Findings

Dirty state is computed by comparing candidate fields against the accepted baseline fields. Dirty state is presentation-only and cannot replace accepted state. Background reads update baseline evidence but do not overwrite dirty in-progress candidate edits.

## Reset Findings

Reset restores candidate state from the latest accepted backend baseline held in memory. Reset never restores a stale draft from browser storage, URL state, history state, React Query persistence, or local/session storage.

## Refresh Findings

Browser refresh, React reload, and component remount lose memory-only candidate edits and re-enter backend config GET authority. Refresh converges to backend baseline and cannot preserve unsaved candidate state as accepted configuration.

## Recovery Findings

Browser restart, application restart, server restart, lost draft, lost cache, and React restart converge by re-entering backend session/config read authority. Stale drafts do not survive recovery because this surface does not persist candidate state outside component memory.

## Failure Findings

400, 401, 403, 404, 409, 422, 500, timeout, malformed DTO, and validation failures withhold or clear accepted presentation. Failed editing remains candidate-only and cannot become accepted configuration without backend acceptance. 401/403 remain covered by POS authority-loss revalidation from earlier passes.

## Concurrency Findings

Another administrator can change backend configuration while a candidate exists. A later accepted backend read updates baseline evidence; dirty candidate edits remain isolated until reset/discard or later backend mutation acceptance in a future pass. RowVersion remains backend version evidence and is not frontend-owned.

## Adversarial Findings

Modified candidate, baseline, currency, branding, company, dirty state, injected draft, replayed candidate, and React state edits cannot fabricate accepted configuration. Frontend admission still rejects missing/mismatched tenant, missing rowVersion, missing themeId, blank store name, and invalid status before baseline creation.

## Cross-System Findings

Candidate state is local to Storefront Settings. POS dashboard/status, Storefront runtime, Checkout, Commerce AI, Affiliate, Reports, Analytics, Theme, CMS, and Navigation consume backend/API-derived accepted evidence. Unsaved edits cannot leak into Storefront or other modules before a future backend mutation/publish acceptance boundary.

## Boundary Review

Baseline Boundary: backend owns accepted baseline; frontend presents admitted evidence; recovery source is backend GET; failures clear/withhold presentation.

Candidate Boundary: frontend owns memory-only candidate form state; backend owns no authority here; recovery source is baseline clone; refresh discards candidate.

Validation Boundary: frontend may show hints; backend validation remains authoritative in future mutation passes; failures do not promote candidate.

Presentation Boundary: frontend renders baseline current state and candidate controls separately; backend remains authority owner; malformed state fails closed.

Navigation Boundary: browser back/forward and route navigation can remount presentation only; backend read re-entry restores baseline; history cannot restore authority.

Recovery Boundary: backend SQL/session/config read authority restores truth; frontend memory is discarded or rehydrated from backend.

Cache Boundary: no-store reads from SF-PA1.2 remain; browser/React cache is presentation only.

Draft Boundary: this settings candidate is memory-only. Theme, Builder, CMS, and publish drafts remain outside this pass.

## Candidate Pipeline

Backend SQL `StoreConfig`/`Tenant` -> Admin accepted DTO -> frontend baseline admission through `deriveCurrentStoreIdentityPresentation` -> `baselineConfig` -> memory clone into `candidateConfig` -> editing -> dirty comparison -> validation/presentation -> await future backend mutation acceptance.

Baseline begins after backend GET authority and frontend DTO admission. Candidate begins when that admitted baseline is cloned into component memory. Authority ends at the backend DTO/baseline admission boundary; presentation begins in React state and form controls.

## Runtime Hardening Performed

- Split Storefront Settings accepted backend baseline from editable candidate state.
- Added candidate-only dirty banner and reset-to-baseline behavior.
- Kept current status/store summary baseline-derived while candidate controls remain editable.
- Preserved dirty candidate edits during later baseline reads without letting the candidate replace baseline.
- Ensured remount/refresh recovery discards memory-only candidates and re-enters backend baseline reads.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/pages/StorefrontSettings.test.tsx`
- `PointOfSale.Tests/Architecture/StorefrontConfigurationAuthorityCertificationTests.cs`

## Architecture Documentation Updates

This document records PASS SF-PA1.3.

## AI Context Updates

Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Explicit Certification Answers

Who owns accepted configuration? Backend SQL `StoreConfig`/`Tenant` plus owning backend services.

Who owns candidate configuration? Frontend Storefront Settings component memory only.

Can frontend create baseline? No.

Can candidate become authority? No.

Can dirty state replace accepted state? No.

Can browser refresh preserve candidate? No; refresh re-enters backend truth.

Can browser history restore stale drafts? No authority; history can only remount presentation.

Can React state become authority? No.

Can candidate affect Storefront before save? No.

Can draft leak into other modules? No inspected path leaks this memory-only candidate.

Where does baseline begin? Backend GET authority plus frontend DTO admission.

Where does candidate begin? Cloning admitted baseline into `candidateConfig`.

Were runtime changes required? Yes.

Is Store Configuration Candidate Authority production-certifiable? Yes.
