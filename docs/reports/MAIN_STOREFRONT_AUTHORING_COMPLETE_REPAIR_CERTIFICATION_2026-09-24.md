# MAIN Storefront Authoring Complete Repair Certification - 2026-09-24

## 1. Executive Summary

The MAIN Storefront Authoring surface was reviewed, repaired, and exercised across
Homepage, CMS Pages, Navigation, Footer, Themes, and Colors/Typography.

The accepted authority boundaries are now explicit:

- Homepage draft/live state is owned by `StoreConfig.pages.home`.
- Navigation draft/live state is owned by the admin CMS Navigation contract.
- Footer authoring is immediate-live through the Footer contract.
- Theme preview is signed and activation is the live transition.
- Colors/Typography use local preview followed by save and publish.
- Storefront CMS Page reads use authoritative PostgreSQL state through
  `CmsPageReadService`; MongoDB projection lag is not involved in this path.

Browser acceptance passed for Homepage, Navigation, Footer, Themes, and
Colors/Typography, including restoration. CMS Page draft save, signed preview,
publish, history rollback, SQL authority, anonymous public read, and tenant
isolation also passed. The initial CMS Page publish attempt exposed a PostgreSQL
transition ordering defect in the previous API binary. The final repaired Release
binary was built and started on port 5010, then real-login browser acceptance
exercised the repaired publish and rollback against live PostgreSQL.

Final live data is clean:

- About v9 is the only published version and contains the original content/SEO.
- About v8, containing the final live certification marker, is inactive and not a
  draft; all earlier certification versions are likewise inactive.
- The anonymous AhramStore About endpoint returns `200` with `MetaTitle=About Us`.
- The same About lookup through the Shopixy tenant returns `404`.
- No certification marker remains active or public.

Verdict: **PASS**

## 2. Scope and Constraints

Certified surfaces:

| Surface | Admin operation | Live authority verified |
|---|---|---|
| Homepage | Draft, preview, publish | `StoreConfig.pages.home.live` and rendered storefront |
| CMS Pages | Draft, signed preview, live publish, history, rollback | PostgreSQL `CmsPages` and anonymous storefront read |
| Navigation | Draft, publish | `/api/storefront/navigation` |
| Footer | Immediate-live save | Storefront Footer read |
| Themes | Signed preview, activation | Storefront runtime config |
| Colors/Typography | Local preview, save, publish | Storefront runtime config |

Constraints honored:

- MAIN repository only; no subagents were used.
- Existing dirty and untracked work was preserved.
- No unrelated worktree changes were reverted or cleaned.
- No load tests, Kubernetes files, k6 files, infrastructure sizing, or timeouts
  were changed.
- API lifecycle on port 5010 was coordinated with the user; the final Release
  process was started from the API project directory.
- Port 5011 was not used.
- No transient administrator credentials were persisted or included in this
  report.
- Earlier development-only test-token acceptance runs were supplemented by a
  final real POS Admin browser login. Application authentication behavior was not
  changed.

Certification tenant:

| Property | Value |
|---|---|
| Tenant ID | `c9c5c1ae-949e-498e-bc63-07242daeb49d` |
| Tenant slug | `ahramstore` |
| Trusted host | `ahramstore.localhost` |
| StoreConfig ID | `9cd01634-f469-4b76-ad20-13a74b88cf13` |
| Isolation tenant | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` / `shopixy.localhost` |

## 3. Repairs

### 3.1 Navigation Repair Admission

`navigationAuthoringContract.js` previously rejected legacy entity links without a
backend GUID before an administrator could repair them. It now admits such a link
only when the backend supplied an exact matching invalid-link warning for the same
snapshot, menu, index, link type, and validation code.

This remains fail-closed:

- Arbitrary unresolved entity links are still rejected on load.
- A mismatched warning does not authorize admission.
- The repair candidate receives no invented target identity.
- Save/publish mapping still rejects the unresolved target until the administrator
  repairs it.

### 3.2 Canonical Homepage Seed

`CmsSeedService` previously treated marketplace `HomepageSections` as proof that
the Storefront Homepage existed. The rendered Storefront instead reads
`StoreConfig.pages.home.live`.

The seed now:

- Detects canonical Storefront Homepage state independently.
- Adds deterministic four-section draft/live Homepage state when missing.
- Preserves the separate marketplace `HomepageSections` projection.
- Appends StoreConfig version history.
- Publishes `StoreConfigPublishedEvent` for the canonical Homepage repair.
- Projects PostgreSQL `xmin` while locking the StoreConfig row with `FOR UPDATE`.
- Avoids the invalid placeholder data URL previously used for the hero image.

Live seed verification returned `200`, created the missing canonical Homepage, and
did not duplicate Pages, Navigation, Footer, or marketplace Homepage records.
Admin, preview, and runtime reads exposed the same four Homepage sections.

### 3.3 CMS Page PostgreSQL State Transitions

The About draft publication failed on PostgreSQL filtered unique index
`IX_CmsPages_PageGroupId`. EF Core sent the statement activating v2 before the
statement clearing the currently published v1, so PostgreSQL correctly rejected
the transient duplicate live slot.

The failed transaction rolled back, but the scoped EF tracker retained mutated
states. The resulting `409` response could therefore describe v2 as published even
though SQL still had v1 published and v2 draft.

`CmsPageService` now performs explicit two-phase transitions inside the existing
transaction:

- Update clears and saves the current draft slot before inserting a replacement
  draft.
- Publish clears and saves current published slots before activating the draft.
- Rollback clears and saves current draft/published slots before inserting the new
  rollback version.
- Failed `DbUpdateConcurrencyException` and `DbUpdateException` saves clear the EF
  `ChangeTracker` before returning a concurrency conflict.

These changes preserve the transaction boundary while preventing statement-level
filtered-index conflicts and false post-rollback response state.

## 4. Browser and Runtime Acceptance

Acceptance used headless Microsoft Edge through `playwright-core` against the
running Admin and Storefront applications.

| Workflow | Evidence | Result |
|---|---|---|
| Homepage | Edit hero, save draft, preview, publish, runtime API, rendered storefront, restore | PASS |
| CMS Page draft | Edit About SEO, save draft, open signed preview | PASS |
| CMS Page publish | Final Release API saved draft v8, published v8 (`200`), and anonymously exposed its temporary SEO marker on the trusted host | PASS |
| CMS Page history/rollback | Browser Page History rolled back v7, creating published v9 (`200`); original content/SEO restored | PASS |
| CMS Page browser stability | Real login; visible published/restored states; final reload stable; no spurious refresh, route loss, wrong tenant, duplicate version display, or unexpected authenticated-phase API responses | PASS |
| Navigation | Edit Home label, save, publish, runtime read, restore | PASS |
| Footer | Edit section title, save live, runtime read, restore | PASS |
| Theme | Signed preview, activate, runtime read, restore | PASS |
| Colors/Typography | Local preview, save, publish, runtime read, restore | PASS |
| Homepage reload | Original `Welcome to Shopixy` rendered before and after reload | PASS |
| Tenant isolation | Shopixy host About lookup returned `404` | PASS |

Trusted-host verification is required because `TenantResolutionMiddleware` strips
raw `X-Tenant-ID` and resolves anonymous Storefront authority from verified host
context. Earlier plain-localhost or Shopixy-host observations were not evidence for
the AhramStore tenant.

## 5. Final SQL and Public State

About page group: `04899853-5c84-5dab-b25a-f56bd7d6f6b2`

| Version | Record ID | Draft | Published | Meta title |
|---|---|---:|---:|---|
| 1 | `f419cba6-dd38-54d4-96e1-816b1fb04610` | false | false | `About Us` |
| 2 | `08ab5d3a-e6fa-4347-9eca-584720e9488e` | false | false | `About Us Certification 2026-09-24` |
| 3 | `77f1e393-6c98-4d86-8520-b4458d1fb5de` | false | false | `About Us` |
| 4-7 | Historical certification and restored snapshots | false | false | Inactive history |
| 8 | `1509eff1-4be5-4620-bef8-3ff22df6e1c5` | false | false | `About Us Final Live Certification 2026-09-24` |
| 9 | `f70260bb-a563-4c01-8ac6-290213179053` | false | true | `About Us` |

PostgreSQL aggregate on the About page group: 9 historical rows, 0 active drafts,
1 published row (v9), and no draft/live overlap. The published v9 content hash
`27f4734ff302cb26fc54e622c2585c94` and SEO-description hash
`b299d07e8635fc0eee55c7ed674ee31a` match the pre-certification v1/v3.

Anonymous public verification on `ahramstore.localhost`:

```text
HTTP status: 200
Meta title: About Us
Certification marker public: false
```

During live publish, the anonymous `ahramstore.localhost` public endpoint returned
`200` with `MetaTitle=About Us Final Live Certification 2026-09-24`. After Page
History rollback, the same anonymous endpoint returned `200` and `MetaTitle=About
Us`. The endpoint does not expose a version field; published v8 and v9 identities
were verified in the admin response, Page History, and PostgreSQL.

Isolation verification on `shopixy.localhost`:

```text
GET /api/storefront/pages/about -> 404
```

The public result came directly from PostgreSQL-backed `CmsPageReadService`; a
MongoDB projection repair or cache wait was neither required nor appropriate.

Data-safety fingerprints before and after the live sequence matched exactly:

| Authority | Before = after |
|---|---|
| Other three AhramStore CMS pages (identity, version, state, update timestamps) | `db68dca1eae65a0ddbba30e17fa778a4` |
| StoreConfig customization (Homepage, Navigation, Theme, Colors/Typography) | `f83aa53db74de2f43691e8501f02432b` |
| StoreConfig PostgreSQL `xmin` | `5951493` |
| Four marketplace Homepage sections | `9853d83d2cfcddea4199f5ce7342d804` |
| Storefront Footer section / four links | `6127a4b29dfabc2b963881036b9de822` / `5eb1fe41f81e45d88c4e63d10b1f2188` |

## 6. Test and Build Evidence

| Gate | Result |
|---|---|
| Focused frontend tests | PASS: final 5 files, 36 CMS tests (earlier 4 files, 38 tests also passed) |
| Focused backend tests | PASS: 5/5, including transition and seed guards, tenant isolation, and unsafe SEO rejection |
| Admin production build | PASS: rerun after live closure; existing chunk-size warning |
| Storefront production build | PASS: rerun after live closure; existing Browserslist/Vite warnings |
| API Release build from current source | PASS: 0 errors, 82 existing warnings |
| CMS PostgreSQL state-transition architecture guard | PASS: included in focused backend 5/5 |
| `git diff --check` | No whitespace errors; line-ending warnings only |

Focused backend tests were run from an isolated artifact path to avoid file
contention with the running API process.

Known non-blocking test limitations:

- `CmsPageWorkflowTests` currently fail in existing fixture setup before the new
  transition behavior can be asserted, including `Created CMS page could not be
  loaded.` A representative test fails the same way against the pre-change binary.
- Eleven `CmsSeedRuntimeTests` are blocked during setup by the known SQLite versus
  PostgreSQL regular-expression operator incompatibility (`~`).
- An older broad CMS architecture certification has an unrelated stale assertion
  for `InvalidatePublishedScopeAsync`; the new isolated transition guard passes.
- Frontend builds retain existing Vite/Browserslist warnings.
- The API build retains existing compiler/analyzer warnings.
- `/health` remains `503` because of an unrelated critical payout-readiness check;
  normal authoring and Storefront endpoints used in this certification functioned.

## 7. Files Changed for This Repair

- `PointOfSale.API/Services/Admin/CmsSeedService.cs`
- `PointOfSale.API/Services/Cms/CmsPageService.cs`
- `PointOfSale.Tests/Architecture/CmsEndToEndAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Cms/CmsSeedRuntimeTests.cs`
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js`
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.test.ts`
- `docs/reports/MAIN_STOREFRONT_AUTHORING_COMPLETE_REPAIR_CERTIFICATION_2026-09-24.md`

All other modified or untracked worktree files were treated as concurrent user or
agent work and were left unchanged.

## 8. Final Release Runtime and Live Closure

| Item | Evidence |
|---|---|
| Source tree | MAIN HEAD `32ffffcc2fdcd376354c19e016a67596eefa88a5` plus the preserved working-tree CMS repair |
| Build | `dotnet build PointOfSale.API/PointOfSale.API.csproj --configuration Release --no-restore`; 0 errors, 82 warnings |
| PID | `17800` |
| Binary | `C:\Users\fadya\source\repos\PointOfSale\PointOfSale.API\bin\Release\net9.0\PointOfSale.API.exe` |
| Started UTC | `2026-09-24T17:41:34.9700362Z` |
| API Release DLL SHA-256 | `4D5A8593C8557A41822069247EED5857A42364EE55BCA8096FF0C8607D5240D2` |
| Source/build parity | `CmsPageService.cs` last changed `17:10:41Z`; Release DLL built `17:38:07Z`; PID 17800 started `17:41:34Z` from that same Release path; no API source edits afterward |

Final real-login browser sequence against that process:

1. Open Content -> Pages -> About. Current published authority v7; no draft.
2. Change only the About SEO meta title to the temporary certification marker.
3. Save draft: HTTP `200`, v8 draft, v7 still published.
4. Publish: HTTP `200`, v8 published, no active draft, no false `409` or stale
   EF-tracker response. Page History showed exactly one published version.
5. Anonymous public trusted-host read: HTTP `200`, temporary marker visible.
6. Page History rollback to the original v7 snapshot: HTTP `200`, new v9
   published, v8 inactive, no active draft.
7. Anonymous trusted-host read: HTTP `200`, original SEO restored; Shopixy host:
   `404`.
8. Editor and Page History visibly restored, followed by a successful reload
   retaining the editor route, tenant session, and original SEO. No `Restoring
   session...` appeared during the mutation flow or persisted after reload; no
   unexpected authenticated-phase API failures, redirects, or duplicate history
   versions occurred.

The application's normal pre-auth bootstrap may briefly show `Restoring session...`
on an intentional full reload while `/auth/session` resolves. The final route and
session remained stable; no spurious mid-edit refresh occurred.

**FINAL VERDICT: PASS.** The repaired live publish/rollback condition is closed,
the original public About state is restored, and no unrelated authoring authority
changed.
