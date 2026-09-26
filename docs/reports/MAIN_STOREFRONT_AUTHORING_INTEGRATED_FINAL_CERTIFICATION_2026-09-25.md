# MAIN Storefront Authoring Integrated Final Certification

## Verdict

**PASS for the MAIN/PRIMARY Storefront Authoring integrated scope.**

The certified AhramStore tenant passed the end-to-end authoring matrix for Homepage, Theme, Colors/Typography, CMS Pages, Navigation, and Dedicated Footer. The final browser audit has no integrated-scope blockers and exact content-authority restoration passed.

This is not a blanket certification that every unrelated repository-wide test or operational dependency is green. Residual items are recorded below.

## Certification Identity

| Item | Value |
| --- | --- |
| Requested artifact date | 2026-09-25 |
| Final execution window | 2026-09-26 UTC |
| Certified tenant | `c9c5c1ae-949e-498e-bc63-07242daeb49d` (`AhramStore`) |
| Isolation tenant | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` (`shopixy.localhost`) |
| Admin | `http://localhost:5173` |
| API | `http://localhost:5010` |
| Storefront | `http://ahramstore.localhost:5174/` |
| Final audit status | `PASS` |
| Integrated blockers | `0` |
| Restoration | `PASS_EXACT_CONTENT_AUTHORITY` |

Credentials were supplied interactively and used only as process environment values. They are not present in the harness, evidence, screenshots, or this report.

## Baseline

The post-migration immutable baseline was captured before final mutation testing.

| Evidence | Value |
| --- | --- |
| Baseline file | `.opencode-tmp/integrated-authoring-cert/evidence/immutable-baseline.json` |
| SHA-256 | `1A20F55CF9C1D63AEFE6249599DB5DA447AC04380799A84FA458489BBD4B9EA7` |
| Read-only attribute | Applied |
| Protected data | StoreConfig, CMS, Footer, Navigation, public responses, products, categories, and tenant B fingerprints |

The baseline records stable `cmsPageGroupId` values for all existing tenant CMS Footer links.

## Authority Model

| Surface | Draft/live behavior | Persistence and concurrency authority |
| --- | --- | --- |
| Theme identity | Activation changes live theme identity | PostgreSQL `StoreConfig`; shared `RowVersion` |
| Colors/Typography | Draft remains private until publish | PostgreSQL `StoreConfig`; shared `RowVersion` |
| Homepage | Draft remains private until publish | PostgreSQL `StoreConfig`; shared `RowVersion` |
| Navigation | Draft/live with publish | Navigation payload in `StoreConfig`; shared `RowVersion` |
| CMS Pages | Versioned draft and published rows | PostgreSQL `CmsPages`; independent expected version |
| Dedicated Footer | Immediate-live replacement | Footer SQL tables; independent SHA-256 revision |

StoreConfig surfaces retain separate payload domains while correctly sharing one optimistic-concurrency token. CMS and Footer remain independently writable and independently conflict-checked.

## Footer Repair

The first integrated run reproduced a cross-surface defect: Navigation followed a CMS page rename through `PageGroupId`, while Dedicated Footer stored only `CmsPageSlug` and dropped the stale link.

The repair adds stable CMS page identity to tenant Footer links:

- `StorefrontFooterLink.CmsPageGroupId` persists the stable version-group identity.
- Tenant Footer saves resolve the admitted published slug to `PageGroupId` on the server.
- Admin Footer reads recompose the current canonical slug from the persisted identity.
- Public Footer runtime resolves by `PageGroupId` first and fails closed if an identified page is not published.
- Legacy and global Footer rows retain slug fallback. Global Footer definitions cannot persist one tenant-specific page-group identity because they span tenants.
- MongoDB derivative Footer documents now carry `CmsPageGroupId`.
- CMS seed-created Footer links persist their known page-group identities.
- Migration `20260926215531_StorefrontFooterCmsPageIdentity` adds, indexes, and backfills the column for published tenant pages.

The migration was successfully applied to the certification database.

## Integrated Browser Result

Final command result: `FINAL_INTEGRATED_BROWSER_CERTIFICATION PASS`.

The final audit followed two additional consecutive complete PASS runs after the harness began waiting for the Appearance editor's visible `Unsaved changes` state before dispatching lifecycle events. This removed a harness race between `FillAsync` and React state admission; no additional application-code change was required.

The audit proved:

- Pre-mutation Admin and public content matched the immutable baseline.
- Theme activation changed only theme identity.
- Colors/Typography draft remained private; publish changed CSS authority without changing theme identity.
- Homepage draft/live behavior preserved Navigation, Footer, and CMS.
- Navigation mutation preserved Dedicated Footer.
- Dedicated Footer immediate-live mutation preserved Header Navigation.
- A published CMS page resolved from both Navigation and Footer with new-tab intent.
- Navigation and Footer both followed the current slug after CMS rename through stable `PageGroupId` authority.
- CMS deletion caused both Navigation and Footer dependencies to fail closed.
- Shared StoreConfig stale writes returned `409` across surfaces.
- Stale Footer and CMS revisions returned `409`.
- Independent Footer and CMS writes survived StoreConfig mutations.
- Two fresh public contexts and a second Admin read converged.
- A forged tenant header returned `403`.
- The trusted isolation host remained tenant B and received no tenant A marker.
- Homepage, CMS, Navigation, and Appearance retained unsaved edits across focus, visibility, clipboard, tab, and failed session-revalidation events.
- No destructive `Restoring session` state or unsafe browser-storage draft persistence was detected.

## Cross-Surface Matrix

| Surface | Result | Isolation evidence |
| --- | --- | --- |
| Theme | PASS | Homepage, Appearance payload, Navigation, and Footer preserved |
| Colors/Typography | PASS | Theme identity and unrelated payloads preserved |
| Homepage | PASS | Navigation, Footer, and CMS preserved |
| Navigation | PASS | Dedicated Footer preserved |
| Dedicated Footer | PASS | Header Navigation preserved |
| CMS canonical rename | PASS | Navigation and Footer followed current page identity |

## Theme Matrix

All `20/20` required cells passed in the final run: English desktop and Arabic mobile for each active theme.

| Theme | English desktop | Arabic mobile |
| --- | --- | --- |
| `modern` | PASS | PASS |
| `minimal` | PASS | PASS |
| `fashion` | PASS | PASS |
| `pearl` | PASS | PASS |
| `phoenix` | PASS | PASS |
| `hype` | PASS | PASS |
| `alizon` | PASS | PASS |
| `sharp` | PASS | PASS |
| `cosmetics` | PASS | PASS |
| `seven` | PASS | PASS |

Every cell had one Header, one Footer, correct language direction, no horizontal overflow, and the expected runtime theme identity. `electronics` remained excluded from active authority.

## Automated Tests

| Suite | Result | Notes |
| --- | --- | --- |
| Footer repair and integrated backend tests | `20/20` PASS | Includes persistence, canonical rename, runtime fail-closed behavior, recovery, and architecture contracts |
| Admin Storefront Authoring focused set | `81/81` PASS across 14 files | Homepage runtime loop, Theme, CMS, Navigation, Footer, authoring guard, and session stability |
| Admin Footer focused set | `12/12` PASS across 2 files | Included in the focused authoring result; also run separately |
| Storefront full suite | `377/377` PASS across 41 files | Full Storefront Vitest run |
| Admin architecture rerun | `32/32` PASS | Confirms three full-suite timeout failures were parallel-run timing artifacts |
| Appearance/auth lifecycle focused rerun | `24/24` PASS across 2 files | Unit-level focus, visibility, failed revalidation, and unsaved-state coverage; confirmed by three consecutive complete browser PASS runs |

The broader backend name-based selection produced `166/168`; both failures are existing assertions outside the changed authoring authority:

- An affiliate attribution certification artifact does not contain `PASS P2-GC`.
- A Homepage recommendation read path does not contain the expected product-search SQL overlay call.

The full Admin suite produced `1495/1499`. Three failures were timeouts and passed in the isolated `32/32` rerun. One locale-structure test remains genuinely failing because 52 `storefront.homepageEditor.fields/options` keys exist in Arabic but not English. No locale files were changed by this repair. This residual prevents claiming a repository-wide all-tests-green state, but it did not fail the integrated authoring behavior, English Admin interactions, Arabic Storefront runtime, or restoration matrix certified here.

## Builds

| Build | Result | Warnings |
| --- | --- | --- |
| API Release | PASS, `0 errors`, `0 warnings` | None in final incremental Release build |
| Admin production | PASS | Existing chunk over `5000 kB` warning |
| Storefront production | PASS | Existing Browserslist age, mixed static/dynamic import, and chunk-size warnings |

`git diff --check` found no whitespace errors. Git emitted existing LF-to-CRLF working-copy warnings.

## Runtime Health

| Endpoint | Result |
| --- | --- |
| `/health/live` | `200 Healthy` |
| `/health/ready` | `503 Unhealthy` |

Readiness is blocked by the known unrelated `payout_external_truth_readiness` check. `certificate_provider` and `caddy_edge` are degraded because those integrations are disabled. PostgreSQL, Redis, MongoDB, Elasticsearch, RabbitMQ, projection outboxes, integration outbox, and canonical identity authority were healthy in the detailed readiness response.

The readiness exception is not caused by Storefront Authoring and is not classified as an integrated authoring blocker.

## Restoration

Final restoration result: `PASS_EXACT_CONTENT_AUTHORITY`.

- Disposable CMS artifacts were deleted.
- Admin and public StoreConfig, CMS, Navigation, and Footer content returned to baseline.
- Tenant B database fingerprint remained exact.
- Tenant A product and category fingerprints remained exact.
- The immutable baseline hash remained unchanged during certification.
- No certification marker remained in protected content authority.

## Evidence

- Final machine-readable audit: `.opencode-tmp/integrated-authoring-cert/evidence/final-audit.json`
- Immutable baseline: `.opencode-tmp/integrated-authoring-cert/evidence/immutable-baseline.json`
- Browser screenshots: `.opencode-tmp/integrated-authoring-cert/evidence/screenshots/`
- Repeatable harness: `.opencode-tmp/integrated-authoring-cert/Program.cs`
- Integrated architecture tests: `PointOfSale.Tests/Architecture/StorefrontAuthoringIntegratedCertificationTests.cs`
- Footer migration: `PointOfSale.Infrastructure/Migrations/20260926215531_StorefrontFooterCmsPageIdentity.cs`

## Blockers

Integrated MAIN/PRIMARY Storefront Authoring blockers: **none**.

The earlier Appearance failures were resolved as a harness-admission race by requiring visible dirty-state evidence before lifecycle stimulation. Three consecutive complete runs then retained the candidate and passed the full matrix without an application-code waiver.

Repository-wide residuals not waived by this report:

- Admin locale structural mismatch for 52 Homepage editor field/option keys.
- Affiliate P2-GC certification artifact assertion.
- Homepage recommendation SQL-overlay architecture assertion.
- Payout external-truth operational readiness failure.

## Final Decision

The MAIN/PRIMARY Storefront Authoring system is certified **PASS** for the defined integrated scope. The Footer canonical-identity defect is repaired, migration-compatible, covered by regression tests, and proven end-to-end through CMS rename, publication, deletion, tenant isolation, concurrency, theme rendering, session stability, and exact restoration.
