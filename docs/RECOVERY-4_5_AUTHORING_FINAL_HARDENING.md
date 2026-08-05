# Storefront CMS Authoring Recovery v1.0

## Recovery 4.5 - Authoring Final Hardening

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Scope:** Footer Authoring and remaining Authoring UX only  
**Certification:** **PASS WITH OBSERVATIONS**

## Executive Summary

Recovery 4.5 completes Footer Authoring behind one canonical frontend and backend pipeline while preserving all frozen Homepage, Navigation, CMS Seed, StoreConfig, Theme, Builder, Projection, Runtime, Cache, and Marketplace CMS version contracts.

Tenant, global Storefront-default, and Marketplace footers now share:

- one frontend save transport owner;
- one frontend DTO mapper;
- one backend authoring service and persistence owner;
- one backend validation owner;
- one backend entity/response mapper;
- one authoritative response-adoption path;
- one backend-issued semantic revision contract;
- one serializable transaction around full replacement, event/outbox staging, and commit.

The remaining tenant and Marketplace CMS editor UX was hardened without changing its backend architecture. Dirty forms are guarded, clean saves are disabled, publish saves visible changes first, conflict retry uses refreshed backend evidence, and Preview refuses unsaved candidates rather than saving or changing versions.

All focused tests and production builds pass. The result is `PASS WITH OBSERVATIONS` because no live PostgreSQL two-writer integration environment was available, and frozen Builder Preview intentionally persists a dirty layout through its already certified canonical save owner before opening Preview. That frozen behavior was verified and not changed in this recovery.

## Verified Root Cause

The primary certification root causes were confirmed in source:

- Footer persistence was split across three services with duplicated mapping and validation.
- Tenant concurrency compared a maximum child timestamp without a locked compare-and-swap boundary.
- Global and Marketplace Footer had no effective stale-write precondition.
- Footer SQL committed before event publication, allowing a failed request after truth changed.
- Marketplace Footer accepted draft-only CMS targets even though public reads require publication.
- Footer DTO validation did not reject null collections, invalid ordering, collection overflow, duplicate social links, or several unsafe URL forms before database constraints.
- Global Storefront Footer did not round-trip localized section titles through the frontend contract.
- Footer load/refresh could replace dirty state or expose an editable empty fallback after load failure.
- Tenant and Marketplace CMS editors had no semantic dirty baseline and Preview/Publish could ignore visible form changes.
- Expected authoring failures were presented through inconsistent alerts or generic failure messages.

## Footer Recovery

### Canonical pipeline

```text
GET tenant/global/marketplace Footer
  -> FooterAuthoringService.GetConfigurationAsync
  -> FooterAuthoringMapper
  -> complete canonical response + semantic revision
  -> frontend response admission
  -> FooterConfigScreen accepted baseline + draft

Save and publish immediately
  -> FooterConfigScreen.handleSave
  -> validateFooterDraft (advisory client checks)
  -> mapFooterDraftToRequest (single frontend DTO mapper)
  -> saveFooterConfiguration (single frontend transport owner)
  -> FooterAuthoringService.SaveConfigurationAsync
  -> serializable transaction
  -> compare backend semantic revision
  -> FooterAuthoringValidator (authoritative admission)
  -> FooterAuthoringMapper (entity mapping)
  -> replace complete graph
  -> flush source state
  -> stage existing FooterUpdated event/outbox
  -> flush outbox state
  -> reload complete response
  -> commit once
  -> FooterConfigScreen.adoptFooterResponse
```

### Concurrency

The response `revision` is a backend SHA-256 fingerprint of the complete persisted Footer graph for one scope. IDs, localized values, order, activation, link behavior, and social configuration participate. Clients never generate revisions.

Every save requires `expectedRevision`:

- missing evidence returns `428/FOOTER_REVISION_REQUIRED`;
- stale evidence returns `409/FOOTER_CONCURRENCY_CONFLICT`;
- accepted replacement returns a successor revision;
- every subsequent save uses the adopted successor response;
- relational writes execute at serializable isolation, translating PostgreSQL serialization/unique races to the same conflict contract.

### Atomicity

Source replacement and existing `StorefrontFooterUpdatedEvent` or `MarketplaceFooterUpdatedEvent` staging occur before one transaction commit. Event failure rolls back sections, links, and social links. The public Footer readers, existing event types, projection consumers, and cache owners were not changed.

### Footer content

The canonical DTO round-trips:

- ordered Footer sections;
- English and Arabic section titles;
- ordered Internal, External, and CMS links;
- English and Arabic link labels;
- active state and new-tab behavior;
- ordered social links, platform, URL, icon, and active state.

Marketplace CMS targets must now be published, not merely drafted. Tenant targets remain tenant-owned published CMS pages. Global fallback CMS slugs remain unresolved until each tenant runtime resolves them through the frozen public reader.

## Authoring UX Recovery

### Footer editor

`FooterConfigScreen` now owns the complete Footer authoring workspace:

- fail-closed initial load and explicit retry;
- accepted response baseline and separate candidate draft;
- semantic dirty detection excluding revision/timestamp/preview metadata;
- unload warning while dirty;
- Save disabled when clean, invalid, loading, or saving;
- Cancel confirmation and authoritative baseline restoration;
- Refresh confirmation before dirty replacement;
- visible loading and immediate-publication indicators;
- confirmation before deleting sections, links, or social links;
- success and structured failure feedback;
- conflict preservation of the visible draft;
- explicit Load Latest and Retry My Draft choices;
- retry only against a separately fetched latest backend revision;
- complete accepted-response adoption after save or retry.

### CMS pages

Tenant CMS and Marketplace CMS editors now use semantic form fingerprints and accepted response baselines. Both:

- guard browser unload and page navigation;
- disable clean saves;
- expose Cancel and Refresh behavior;
- save dirty visible content before Publish;
- immediately adopt save and publish responses;
- retain structured conflict recovery and refreshed version evidence;
- fail closed when initial authority cannot load;
- keep Preview read-only by rejecting unsaved candidates;
- present success and structured failure feedback.

The Marketplace CMS semantic version transport recovered in Sprint 0 remains unchanged: updates use the latest adopted `expectedVersion`; publish/delete retain their existing backend-issued version evidence.

### Authoring consistency

| Editor | Load | Draft/dirty | Validation | Save owner | Response adoption | Refresh/conflict |
|---|---|---|---|---|---|---|
| Homepage | Frozen Recovery 1 snapshot admission | Semantic workspace fingerprint | Client schema hints; backend authority | Canonical bulk draft owner | `adoptAuthoritativeSnapshot` | Confirmed refresh and exact conflict reload |
| Navigation | Frozen Recovery 2 snapshot admission | Zustand draft/dirty state | Client shape checks; canonical backend admission | One `updateNavigation` initiator | Complete admitted snapshot | Fail-closed load and RowVersion recovery |
| Footer | Canonical scoped load | Accepted fingerprint vs candidate | Shared client validator; one backend validator | `FooterConfigScreen.handleSave` to `FooterAuthoringService` | `adoptFooterResponse` | Confirmed refresh, latest load, explicit retry |
| CMS Pages | Identity-admitted detail load | Semantic form fingerprint | Required fields/client URL hints; backend authority | Editor `handleSave` | `adoptAuthoritativePage` | Confirmed refresh and latest-version retry |
| StoreConfig | Frozen StoreConfig accepted/candidate model | Field/layout dirty comparison | Frozen strict admission | Existing StoreConfig save owners | Complete response plus rotated RowVersion | Existing explicit conflict/refresh contract |
| Marketplace CMS | Frozen Sprint 0 detail/version admission | Semantic form fingerprint | Required fields/client hints; backend authority | Existing editor `handleSave` | Complete save/publish response | Confirmed refresh and backend version recovery |

## Validation Strategy

### Frontend advisory validation

No Footer mutation HTTP request is issued while obvious authoring errors exist:

- missing required English section title, link label, social platform, URL, or CMS target;
- too many sections, links, or social links;
- unsafe or malformed Internal paths;
- External/social URLs without `http://` or `https://`;
- credential-bearing, control-character, or oversized URLs;
- unsupported/malformed email and phone link schemes;
- duplicate Footer targets;
- duplicate social platforms or URLs;
- unsupported link types;
- CMS selections absent from the loaded published target set;
- malformed backend response ordering or missing revision evidence.

Array position is the frontend order authority. The single DTO mapper emits consecutive zero-based order values.

### Backend authoritative validation

`FooterAuthoringValidator` remains authoritative for:

- null and count admission;
- required/localized length rules;
- supported enum values;
- exact consecutive ordering;
- URL safety and database length bounds;
- duplicate Footer targets;
- duplicate social platform/URL admission before unique indexes;
- tenant and Marketplace published CMS target existence;
- global CMS slug syntax;
- scope correctness.

Client validation intentionally does not decide tenant ownership, deletion, publication, or database concurrency.

## Error Mapping

Footer backend failures use structured status/code/message responses. Shared frontend feedback classifies:

| Failure | UX |
|---|---|
| Validation / invalid request | Field-review feedback; candidate retained |
| Concurrency | Latest revision fetch plus Load Latest / Retry My Draft |
| Already exists | Explicit already-exists message |
| Already published | Explicit already-published message |
| Duplicate | Duplicate-content guidance |
| Missing resource | Referenced-resource unavailable guidance |
| Forbidden | Permission-required guidance |
| Unexpected failure | Retry guidance; candidate retained |

There is no generic `Save Failed` Footer message and no silent mutation failure.

## User Experience Improvements

- Clear clean/dirty state labels.
- Disabled clean or invalid Save actions.
- `beforeunload` guards for dirty Footer and CMS forms.
- Confirmed Cancel, Refresh, navigation, destructive block deletion, Footer section deletion, link deletion, and social deletion.
- Separate saving/publishing indicators.
- Immediate-live Footer wording.
- Accepted-response success feedback.
- Draft-preserving failure feedback.
- Conflict retry with explicit overwrite confirmation.
- Fail-closed initial load instead of an editable destructive fallback.
- Preview controls that cannot persist unsaved CMS candidates.

## Preview

Footer has no Preview command. Footer response admission ignores unrelated preview metadata, and preview-only metadata cannot alter its dirty fingerprint.

Tenant and Marketplace CMS Preview now require a clean persisted draft. Preview only requests backend preview access and opens a separate URL. It does not call save, publish, response adoption, or local draft mutation. Historical CMS snapshots are labeled `Load snapshot`, not Preview, because loading one intentionally creates candidate authoring state.

Homepage preview isolation remains covered by frozen Recovery 1 tests. Storefront browser preview isolation remains covered by the complete Storefront suite.

## Regression Tests

### Frontend

- `footerAuthoringContract.test.ts`: response admission, one DTO mapping, ordering, invalid URLs, protocol, required fields, duplicate links/socials, target references, structured error classes, and preview-metadata dirty isolation.
- `FooterConfigScreen.test.tsx`: dirty detection, unload guard, Cancel, delete confirmation, client validation before HTTP, response adoption, repeated-save revision adoption, conflict preservation/retry, feedback, and fail-closed load.
- `CmsPageEditor.test.tsx`: dirty detection, unload guard, save-before-publish, response adoption, and Preview isolation.
- `MarketplaceCmsPageEditor.test.tsx`: retained Sprint 0 version propagation plus semantic dirty and Preview isolation coverage.

### Backend

- `FooterAuthoringRecoveryTests`: complete save, revision rotation, repeated response adoption, stale conflict, validation, invalid order/target, duplicate social admission, event-failure rollback, transaction-active event staging, and global/Marketplace localization round-trip.
- Existing Footer public read tests continue to prove tenant/global fallback and Marketplace separation.
- The Footer section of `StorefrontNavigationAuthorityCertificationTests` now asserts the recovered revision, serializable transaction, validation owner, and published-target contract.

## Validation

| Gate | Result |
|---|---|
| Focused Footer/CMS frontend recovery tests | **PASS: 10/10** |
| Broad selected Web authoring gate | **PASS: 15 files, 73/73** |
| Final Marketplace CMS version/UX gate | **PASS: 10/10** |
| Footer backend recovery tests | **PASS: 5/5** |
| Footer backend/read/architecture subset | **PASS: 6/6** |
| Frozen Homepage, Navigation, Marketplace CMS, and CMS Seed backend recovery gate | **PASS: 54/54** |
| Complete Storefront frontend suite | **PASS: 25 files, 234 tests** |
| `PointOfSale.Web` production build | **PASS: 5068 modules transformed** |
| `PointOfSale.Storefront` production build | **PASS: 513 modules transformed** |
| `dotnet build PointOfSale.sln -c Release --no-restore` | **PASS: 0 errors** |
| `git diff --check` | **PASS; repository line-ending conversion notices only** |

Existing non-blocking warnings remain: React Router future flags, expected conflict-test console output, Storefront Browserslist age, mixed static/dynamic imports, and chunk-size notices.

## Search Proof

Production-source searches returned:

```text
frontend_footer_save_transport_owners=1
  PointOfSale.Web/src/shared/content/footerAuthoringService.js
  export async function saveFooterConfiguration

frontend_footer_dto_mappers=1
  PointOfSale.Web/src/shared/content/footerAuthoringContract.js
  export function mapFooterDraftToRequest

backend_footer_mapper_owners=1
  PointOfSale.API/Services/Admin/FooterAuthoringMapper.cs
  public sealed class FooterAuthoringMapper

backend_footer_validation_owners=1
  PointOfSale.API/Services/Admin/FooterAuthoringValidator.cs
  public sealed partial class FooterAuthoringValidator

backend_footer_persistence_owners=1
  PointOfSale.API/Services/Admin/FooterAuthoringService.cs
  public sealed class FooterAuthoringService : IFooterAuthoringService

frontend_footer_response_adoption_owners=1
  PointOfSale.Web/src/shared/content/FooterConfigScreen.jsx
  const adoptFooterResponse

legacy_footer_service_owners=0
legacy_footer_timestamp_preconditions=0
legacy_footer_frontend_mutation_wrappers=0
```

The three controllers are scope-specific HTTP adapters to the same owner. They contain no Footer mapping, validation, or persistence logic.

## Observations

1. The relational behavior tests use SQLite with PostgreSQL-compatible helper functions. Production serializable isolation and PostgreSQL serialization translation compile and are source-verified, but a live PostgreSQL two-admin race was not available in this workspace.
2. Frozen Storefront Builder Preview persists a dirty layout through its already certified canonical save owner before opening Preview. It therefore can rotate StoreConfig RowVersion as documented by the frozen authoring certification. This recovery did not reopen Builder or StoreConfig to change that contract.
3. The frozen public Footer runtime supports safe storefront-relative paths and HTTP(S) external links. `mailto:` and `tel:` are rejected with explicit authoring feedback rather than admitted into a runtime that would hide them.

## Certification

**PASS WITH OBSERVATIONS**

Footer Authoring is recovered:

- one save pipeline;
- one DTO mapper per language boundary;
- one backend validation owner;
- one backend persistence owner;
- one response-adoption path;
- effective backend-issued stale-write evidence;
- atomic source and event/outbox persistence;
- localized section/link round-trip;
- published target admission;
- advisory client validation before HTTP;
- structured feedback without generic save failure;
- dirty, Cancel, Refresh, retry, loading, publishing, delete, success, and failure UX;
- Preview isolation for the recovered CMS editors;
- passing frontend, backend, Storefront, Web, solution-build, search, and diff gates.

No customer Runtime, Theme, Builder, Projection, Cache, StoreConfig, Homepage, Navigation, or CMS Seed architecture was changed. The Marketplace CMS semantic version contract remains the frozen Sprint 0 authority.

This recovery stops here. `CMS_AUTHORING_FINAL_CERTIFICATION` was not started.
