# PASS SF-ADM1.3 Store Configuration Candidate Authority Certification

PointOfSale.Web Store Administration Store Configuration Candidate Authority is production-certifiable with no runtime code change required. Backend SQL `StoreConfigs`, SQL `Tenants`, trusted tenant/session context, `AdminStoreConfigController.GetStoreConfig`, `AdminStoreConfigService.GetStoreConfigAsync`, `AdminStoreConfigController.UpdateStoreConfig`, `AdminStoreConfigService.UpdateStoreConfigAsync`, EF `StoreConfig.RowVersion`, and accepted `AdminStoreConfigResponse` own accepted Store Configuration, Store Identity, Business Identity, Theme Assignment, Currency, Visibility, SEO/runtime metadata where modeled, persistence, recovery, projection, mutation acceptance, and baseline replacement evidence.

Storefront Settings owns only an editable memory candidate, form controls, dirty tracking, reset presentation, validation hints, loading/error/success presentation, and temporary preview. Frontend state never establishes accepted Store Configuration.

## Pages Reviewed

- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`
- `PointOfSale.Web/src/components/StorefrontStatusCard.jsx`
- `PointOfSale.Web/src/contexts/SettingsContext.jsx` as adjacent shared settings presentation context

## Backend Endpoints Reviewed

- `GET /api/admin/store-config`
- `PUT /api/admin/store-config`
- `GET /api/storefront/store-config` only where consumed by Store Administration preview/runtime helpers

## Backend Services Reviewed

- `AdminStoreConfigService.GetStoreConfigAsync`
- `AdminStoreConfigService.UpdateStoreConfigAsync`
- `AdminStoreConfigService.MapToResponseAsync`
- `AdminStoreConfigService.ApplyExpectedRowVersion`
- `StorefrontDraftConfigMapper.MapLive` only for public runtime read consistency

## Accepted Baseline Findings

Accepted configuration begins in backend SQL `StoreConfigs`/`Tenants` through trusted tenant context and backend service/controller admission. Storefront Settings creates `baselineConfig` only after `storeConfigService.getStoreConfig()` returns backend evidence and `deriveCurrentStoreIdentityPresentation(response, user)` admits current tenant, row-version, store name, valid status, and theme evidence.

Baseline presentation is cleared or withheld on missing tenant, backend failure, malformed DTO, foreign tenant evidence, missing row-version, missing theme, blank store name, or invalid status. Frontend defaults, browser storage, React state, query cache, route state, Redis, Mongo, or search projection do not create baseline authority.

## Candidate Findings

Candidate begins only when an admitted backend baseline is copied into `candidateConfig`. Storefront Settings edits mutate `candidateConfig` only. Current status, store name, active theme, and last-updated summary remain baseline-derived while candidate controls remain editable. Refresh/remount/browser restart discards the memory candidate and re-enters backend read authority.

## Dirty Tracking Findings

Dirty tracking compares candidate fields with accepted baseline fields and drives the unsaved-changes banner only. `dirtyRef` prevents a later backend read from silently overwriting active candidate edits, but it does not turn the candidate into accepted truth. Dirty state cannot create authority, repair a stale baseline, or bypass backend mutation acceptance.

## Reset Findings

Reset calls `resetCandidateToBaseline` and restores candidate state from the current admitted backend baseline held in memory. Reset does not use localStorage, sessionStorage, URL/history state, React Query persisted data, mutation response patches, or projection state.

## Preview Findings

Storefront Settings preview/open-store behavior is presentation/navigation only. Candidate controls can affect the local editing surface before save, but preview cannot publish, persist, project, or establish accepted configuration. Public Storefront runtime continues to read accepted backend/live configuration only.

## Cache Findings

Admin/public config reads are no-store at endpoint and service boundaries. Storefront Settings has no localStorage/sessionStorage candidate persistence, no React Query persisted draft, no `initialData`, no browser-cache fallback, and no cross-module candidate store. Cached candidates cannot become accepted configuration.

## Recovery Findings

Refresh, browser close/reopen, another browser, another admin update, server restart, Redis loss, and Mongo/search rebuild converge through backend read authority. Unsaved candidate state is memory-only and cannot survive recovery as accepted Store Configuration. Timeout-after-commit or network loss is resolved by backend reread, not by frontend acceptance.

## Failure Findings

400, 401, 403, 404, 409, 422, 428, 500, timeout, offline, malformed DTO, foreign tenant response, missing row-version, invalid status, and failed mutation paths cannot replace accepted baseline. Failed edits remain candidate-only. Baseline/candidate replacement after save happens only when the accepted backend response passes `deriveCurrentStoreIdentityPresentation`.

## Concurrency Findings

Two admins, multiple tabs, stale candidates, delayed reads, duplicate clicks, replayed candidates, and concurrent edits converge through backend row-version authority. Storefront Settings submits `rowVersion` and the service sends `If-Match`; backend missing/stale/malformed evidence fails with precondition or conflict behavior before persistence.

## Cross-System Findings

Candidate state is local to PointOfSale.Web Store Administration Storefront Settings. PointOfSale.Storefront, Affiliate, CommerceHub, Checkout, Orders, Analytics, Theme/CMS/Navigation, and other modules consume accepted backend configuration or derivative backend projections. Unsaved candidates do not leak into Storefront before backend acceptance.

## Adversarial Findings

Modifying React state, dirty flags, theme, currency, store name, status, DTO fields, or replaying/injecting candidates cannot fabricate accepted Store Configuration. Backend derives tenant/store authority from trusted context, requires version evidence for mutation, validates request shape/status, persists in SQL, and returns accepted evidence that the frontend must admit before baseline replacement.

## Candidate Pipeline

Backend SQL `StoreConfigs`/`Tenants`
-> `AdminStoreConfigService.GetStoreConfigAsync`
-> accepted `AdminStoreConfigResponse`
-> no-store `storeConfigService.getStoreConfig`
-> DTO admission through `deriveCurrentStoreIdentityPresentation`
-> accepted `baselineConfig`
-> clone into editable `candidateConfig`
-> dirty comparison
-> reset/validation hints/temporary preview
-> versioned `storeConfigService.updateStoreConfig` intent
-> backend row-version validation and SQL persistence
-> accepted response admission
-> baseline/candidate replacement.

Accepted Configuration begins at SQL/backend service admission. Candidate begins when Storefront Settings clones an admitted baseline into `candidateConfig`. Dirty Tracking begins when candidate fields are compared to baseline fields. Presentation begins after DTO admission in Storefront Settings and remains separated into baseline display and candidate controls.

## Boundary Review

- Accepted Boundary: backend owns; recovery source is SQL/backend GET; failure withholds or keeps prior admitted baseline.
- Candidate Boundary: frontend component memory owns; recovery source is admitted baseline clone; refresh discards.
- Dirty Boundary: frontend presentation owns; recovery source is recomputation; failure cannot promote dirty state.
- Preview Boundary: frontend presentation/navigation owns; recovery source is backend accepted runtime; failure withholds.
- Recovery Boundary: backend read authority owns; browser/cache/projection state is derivative only; failure clears/withholds.
- Presentation Boundary: Storefront Settings owns rendering only; accepted values remain backend-owned.

## Runtime Hardening Performed

No runtime code change was required. Existing SF-PA1.3/SF-PA1.4 hardening already keeps baseline/candidate state separated, resets candidates from accepted baseline, discards candidates on remount, submits versioned intent, suppresses duplicate save entry, and replaces baseline only after backend accepted-response admission.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontConfigurationAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this ADM-scoped findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

Did you review ONLY PointOfSale.Web Store Administration runtime? Yes.

Which frontend files were modified? None.

Which backend files were modified? None.

Who owns accepted Store Configuration? Backend SQL `StoreConfigs`/`Tenants` and backend store configuration services.

Who owns editable candidate? Storefront Settings component memory only.

Can frontend create accepted configuration? No.

Can dirty state become authority? No.

Can Preview become authority? No.

Can React State become authority? No.

Can LocalStorage become authority? No.

Can stale candidate survive recovery? No.

Can candidate leak into Storefront before backend acceptance? No.

Where does accepted configuration begin? SQL `StoreConfigs`/`Tenants` through backend controller/service admission.

Where does candidate begin? `candidateConfig`, cloned from admitted `baselineConfig`.

Where does dirty tracking begin? The comparison between `baselineConfig` and `candidateConfig`.

Where does presentation begin? Storefront Settings after DTO admission succeeds.

Were runtime changes required? No.

Is Store Configuration Candidate Authority production-certifiable? Yes.
