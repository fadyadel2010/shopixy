# GATE A-2 — Homepage Architecture Reduction: Decision Review

Program: Shopixy Repository Modernization
Gate: A-2 (read-only architectural decision review)
Branch reviewed: `recovery/3-greenfield-baseline` (uncommitted working tree vs HEAD `8317aef1`)
Method: live working-tree inspection + `git diff HEAD` + `git show HEAD:` for deleted artifacts. No repository state was modified.

---

## 1. Executive Summary

The removal of `PointOfSale.API/Controllers/HomepageSectionsController.cs` is **NOT an accidental deletion and NOT a feature removal**. It is a **read-path relocation/redesign** in which every former consumer of the deleted endpoint was migrated in the same change-set:

- The **tenant Storefront** homepage no longer calls `GET /api/homepage/sections`; it now renders sections from the tenant `StoreConfig` live page layout (`config.pages.home.sections`) — evidence: `PointOfSale.Storefront/src/pages/HomePage.tsx` diff (hook `useHomepageSections` removed, `selectConfig` added).
- The **CommerceHub marketplace SPA** homepage no longer calls the deleted API; it now calls the Mongo-projected marketplace read model at `GET /api/read/homepage` (`MarketplaceReadController.cs:360`) — evidence: `PointOfSale.CommerceHub/src/api/endpoints.ts:23` (`homepage: "/api/read/homepage"`) and the rewritten `homepage-service.ts`.
- The **SuperAdmin homepage builder write side is fully retained**: controller, management service, publish event, Mongo projection consumer, and Web admin UI all remain intact and wired.

Verdict summary: the change is internally consistent with only **two dead-code leftovers and one publish-flow behavioral change** to acknowledge. Decision: **APPROVE WITH CONDITIONS** (Section 10).

---

## 2. Scope

In scope: everything named `*Homepage*` / `*homepage*` across `PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.Tests`, `PointOfSale.Web` (SuperAdmin UI), `PointOfSale.Storefront`, `PointOfSale.CommerceHub`, plus DB baseline migration and appsettings. Out of scope: feature quality, UI behavior, bug hunting.

---

## 3. Discovery (current working tree)

### 3.1 Backend — retained
| Component | Path | Status |
|---|---|---|
| Domain entity | `PointOfSale.Core/Entities/HomepageSection.cs` | present |
| EF configuration | `PointOfSale.Infrastructure/Data/Configurations/HomepageSectionConfiguration.cs` | present |
| DB table | `Migrations/20260723214642_Baseline_Recovery3.cs:1505-1532, 11085-11101` (`HomepageSections` + CHECK constraints + unique draft/publish indexes); live schema dump `DB/schema-23-7-2026.sql:4349-4373` | present |
| Events | `PointOfSale.Core/Events/HomepageSectionEvents.cs` (`HomepageSectionsPublishedEvent`) | present |
| Projection consumer | `PointOfSale.Infrastructure/Consumers/Marketplace/HomepageProjectionConsumer.cs:11` (`IConsumer<HomepageSectionsPublishedEvent>`, event fence at `:35`) | present |
| Mongo read model | `PointOfSale.Infrastructure/MongoDB/Documents/Marketplace/MarketplaceHomepageReadModelDocument.cs`, `MongoDB/Services/Marketplace/MarketplaceHomepageReadRepository.cs` | present |
| Public read endpoint | `PointOfSale.API/Controllers/MarketplaceReadController.cs:360` `GET api/read/homepage` (Mongo-backed, tenant-or-global) | present |
| SuperAdmin controller | `PointOfSale.API/SuperAdmin/Controllers/SuperAdminHomepageSectionsController.cs` (`api/super-admin/homepage/sections`) | present |
| SuperAdmin service | `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs` | present (modified) |
| Read service | `PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs` (+ `HomepageSectionCacheService`, `HomepageSectionSchemaRegistry`, `HomepageSectionModes`, `HomepageTemplateSeeder`) | present (modified) |
| DTOs | `PointOfSale.API/DTOs/Homepage/HomepageSectionDtos.cs` | present |
| DI | `Program.cs:225-226, 430-432, 927` | registered |
| Cache config | `PointOfSale.API/appsettings.json:160-163` (`HomepageSections:Cache:PublishedTtlMinutes`) | present |
| Tests | `PointOfSale.Tests/SuperAdmin/SuperAdminHomepageSectionManagementServiceTests.cs` (modified, +78/-…), `PointOfSale.Tests/Architecture/HomepageDiscoveryRecommendationCertificationTests.cs` | present |

### 3.2 Backend — deleted (git status `D`)
- `PointOfSale.API/Controllers/HomepageSectionsController.cs` (120 lines; was `[AllowAnonymous]` `api/homepage/sections`, tenant-required, published/preview modes — recovered via `git show HEAD:`)
- `PointOfSale.Infrastructure/Migrations/Sql/20260327_ZZZ_AddHomepageSections.sql`
- `PointOfSale.Infrastructure/Migrations/Sql/20260328_AAA_HardenHomepageSectionsDraftPublishing.sql`

### 3.3 Frontend — deleted
- `PointOfSale.Storefront/src/hooks/useHomepageSections.ts` (117 lines)
- `PointOfSale.Storefront/src/types/homepage.ts` (13 lines)

### 3.4 Frontend — retained/modified
- Storefront: `pages/HomePage.tsx` (rewritten to StoreConfig source), `hooks/useHomepageSectionProducts.ts` (kept; +`pricing` field), `components/sections/SectionRenderer.tsx` + `homepageSections.ts` (kept — now typed against `StorefrontSection` from `types/storeConfig.ts:44`).
- CommerceHub: `homepage/queries.ts`, `homepage/types.ts`, `services/homepage-service.ts`, `pages/HomePage.tsx` (all modified to the `/api/read/homepage` contract; tenant query param dropped).
- Web SuperAdmin builder: `super-admin/homepage/*` (Builder/Manager pages, service, utils, tests) unchanged; routed in `App.jsx:155-156, 220-221` behind capability `super-admin.homepage.manage`; nav entry `SuperAdminLayout.tsx:133`.

---

## 4. Before vs After

| Concern | Before (HEAD) | After (working tree) | Evidence |
|---|---|---|---|
| Tenant storefront homepage read | `GET /api/homepage/sections` via `useHomepageSections` hook | `StoreConfig.pages.home.sections` (POS Admin storefront builder layout) | `HomePage.tsx` diff: header comment "Homepage sections come from the tenant StoreConfig live page layout"; `const homeSections = config.pages?.home?.sections ?? []` |
| Marketplace (CommerceHub) homepage read | `GET /api/homepage/sections?tenantId=` (SQL read service) | `GET /api/read/homepage` (Mongo `MarketplaceHomepageReadModelDocument`, tenant-or-global fallback) | `endpoints.ts:23`; `MarketplaceReadController.cs:360-398`; `homepage-service.ts` diff maps `MarketplaceHomepageResponse.sections` |
| SuperAdmin authoring (draft/publish/reorder/template) | Present | Present, unchanged surface | `SuperAdminHomepageSectionsController.cs`; `Web/src/super-admin/homepage/homepageService.ts:128-226` |
| Publish flow | Save → commit → invalidate cache → reload published from DB → publish event | Compute published list in-memory → publish event (inside txn, outbox-consistent) → save → commit → invalidate cache | `SuperAdminHomepageSectionManagementService.cs` diff @376-405 |
| Tenant-active gating | `tenant.IsActive` | `tenant.Status == TenantStatus.Active` | `HomepageSectionReadService.cs:336` diff; same change in SuperAdmin service `:680` |
| Schema provisioning | 2 supplemental SQL scripts | Baseline EF migration `20260723214642_Baseline_Recovery3` carries the table, CHECKs, and unique draft/publish indexes | baseline `:1505-1532, 11085-11101` |

What disappeared: only the anonymous public API read path and its two direct frontend clients. What was relocated: marketplace read → CQRS Mongo read model; storefront read → StoreConfig. What was redesigned: publish/event ordering (event now inside the transactional boundary — consistent with the repo's IEventPublisher outbox rule).

---

## 5. Consistency Review

- Frontend references to deleted API: **NONE.** `grep "api/homepage"` across Storefront/CommerceHub/Web/API/Tests returns zero hits.
- Deleted controller in routing/DI: **N/A** — attribute-routed; no `Program.cs` reference existed; no residual references (`grep HomepageSectionsController` in API/Tests = 0).
- Deleted hook/type residue in Storefront: **NONE** — `useHomepageSections` and `types/homepage` have zero remaining importers; `hooks/index.ts` exports only `useHomepageSectionProducts`. Storefront `tsc --noEmit` passes (0 errors).
- DI of survivors: `IHomepageSectionReadService` still registered (`Program.cs:432`) but its ONLY production consumer was the deleted controller. Current consumers: DI registration + integration tests only (`SuperAdminHomepageSectionManagementServiceTests.cs:47-537`). The SuperAdmin controller does NOT inject it. → **dead-in-production service kept alive by tests** (finding C-1).
- Deleted SQL scripts still named in test infra: `TestDatabaseScope.cs:147-148` lists both deleted filenames in `EarlySupplementalSqlMigrations`. The loader enumerates files that exist on disk and filters (`TestDatabaseScope.cs:654-658`), so missing files are silently skipped — **no test crash**, but the entries are dead config, and schema coverage now depends entirely on the baseline migration (finding C-2).
- Baseline schema equivalence: baseline carries `UQ_HomepageSections_SectionGroupId_Version`, `IX_..._TenantId_IsDraft_Order`, `IX_..._TenantId_IsPublished_Order`, CHECKs incl. `CK_..._State` (`NOT (IsDraft AND IsPublished)`); live DB dump confirms `CK_..._DraftGroupId`, `CK_..._PublishedAt` (`DB/schema-23-7-2026.sql:4366-4373`). No evidence of a lost constraint, though a column-by-column diff vs the deleted harden script was not exhaustively performed here (see C-2 action).
- Navigation/menus: SuperAdmin nav (`SuperAdminLayout.tsx:133`) and routes (`App.jsx:220-221`) point at retained pages — no broken links.
- Permissions: capability `super-admin.homepage.manage` still guards retained routes — no orphaned permission.
- Feature flags: none exist for homepage (no hits in appsettings feature blocks).
- CommerceHub client-side contract shim: `homepage-service.ts` synthesizes `id = "${type}-${order}-${version}"` and casts through `unknown` because `/api/read/homepage` sections carry no `id`/`createdAt` — a deliberate adapter, flagged as debt (finding C-3).

No broken imports, no broken dependency chains, no dead ViewModels/validators specific to this change were found beyond C-1/C-2.

---

## 6. Architectural Intent

**Chosen explanation: B — Feature Relocation** (with elements of C — Redesign of the read path).

Evidence:
1. Both former clients were migrated in the SAME uncommitted change-set — not left dangling (`git status`: controller `D` + both clients `M` together).
2. The Storefront's replacement source is explicitly documented in code: "Homepage sections come from the tenant StoreConfig live page layout so storefront rendering matches the POS Admin storefront builder" (`HomePage.tsx` header diff).
3. The CommerceHub replacement endpoint is a pre-existing, projection-fed CQRS read path (`MarketplaceReadController.cs:360`, fed by `HomepageProjectionConsumer` consuming `HomepageSectionsPublishedEvent`), aligning marketplace homepage reads with the platform's Mongo-read-model convention.
4. The write/authoring side (SuperAdmin builder → SQL → event → projection) is untouched in surface and improved in transactional ordering — the OPPOSITE of a removal.
5. Accidental deletion is excluded: an accidental delete would leave `useHomepageSections`, `types/homepage.ts`, endpoint constants, and the tenant query param behind. All were cleaned up.

Confidence: HIGH.

---

## 7. Impact Analysis

- **API**: public anonymous surface shrinks by one endpoint; marketplace homepage read consolidates on `api/read/homepage`. Anyone (external integrators) calling `GET /api/homepage/sections` gets 404 — no evidence of such consumers in-repo.
- **Frontend**: Storefront homepage is now coupled to StoreConfig (POS Admin builder), which means the SuperAdmin homepage draft/publish system NO LONGER drives the tenant storefront — it now drives only the marketplace (CommerceHub) homepage via projection. This is the single largest semantic shift and should be an explicitly ratified product decision (Condition 1).
- **Domain**: unchanged (`HomepageSection` entity, events intact).
- **Application layer**: `IHomepageSectionReadService` becomes production-dead (C-1); SuperAdmin management service publish flow reordered to event-inside-transaction (consistent with outbox/fail-closed rules).
- **Infrastructure**: projection consumer + Mongo repo unchanged; supplemental SQL scripts folded into the recovery baseline.
- **Authorization**: preview-mode authorization logic that lived in the deleted controller is gone with it; SuperAdmin endpoints keep their own gating. No orphaned permission strings.
- **Navigation**: intact.
- **Testing**: `SuperAdminHomepageSectionManagementServiceTests` still exercises `HomepageSectionReadService.GetActiveSectionsAsync` (published + preview modes) — tests keep the dead service compiling and green; certification test `HomepageDiscoveryRecommendationCertificationTests.cs` has no reference to the deleted controller.
- **Documentation**: numerous docs (`docs/architecture/homepage-discovery-recommendation-certification-findings.md`, recovery audits) still describe the old `api/homepage/sections` path — stale after this change.
- **Deployment**: no DI/startup breakage; API compiles logically (zero residual symbol references); Storefront typechecks clean.
- **Future maintenance**: CommerceHub adapter shim (C-3) and dead read-service (C-1) are the carry-forward costs.

---

## 8. Risk Register

| ID | Finding | Risk | Rationale |
|---|---|---|---|
| R-1 | Storefront homepage source switched from SuperAdmin draft/publish system to tenant StoreConfig | MEDIUM | Behavioral/product change, not a defect; if the SuperAdmin homepage builder was intended to drive tenant storefronts, that capability is now marketplace-only. Must be a ratified decision, not an implicit one. |
| R-2 | `IHomepageSectionReadService` + cache service + schema registry remain DI-registered with no production consumer | LOW | Dead-in-production code kept alive by tests; compile-safe, but misleading surface for future maintainers. |
| R-3 | `TestDatabaseScope.cs:147-148` references two deleted SQL files | LOW | Loader skips missing files silently (`:654-658`); dead config only. Residual risk: if the baseline migration ever misses a constraint the harden script provided, tests would not catch it. |
| R-4 | CommerceHub `homepage-service.ts` synthesizes ids and casts `as unknown as HomepageSectionRecord`; `createdAt/updatedAt` made optional | LOW | Type-safety shim over the new read-model contract; works, but weakens the type contract. |
| R-5 | Loss of `preview` mode on a public/tenant path | LOW | Preview capability still exists via SuperAdmin controller (`mode` query param); only the anonymous preview path was removed — likely intentional hardening. |
| R-6 | External callers of `GET /api/homepage/sections` | LOW | No in-repo consumers remain; no evidence of external contract. |

No CRITICAL or HIGH findings. No tenant-isolation regression: the new marketplace read path resolves tenancy server-side (`_tenantContext.TenantId`, `MarketplaceReadController.cs:379`) and the CommerceHub client REMOVED the client-supplied `?tenant=` param (`HomePage.tsx` diff) — an improvement over the old client-chosen tenantId.

---

## 9. Conditions

**Condition 1 — Ratify the storefront homepage source-of-truth change.**
- Reason: the tenant storefront no longer renders SuperAdmin-published homepage sections; it renders StoreConfig layout.
- Evidence: `PointOfSale.Storefront/src/pages/HomePage.tsx` diff; zero Storefront references to any homepage-sections API.
- Risk: MEDIUM (R-1) — silent scope reduction of the SuperAdmin homepage builder to marketplace-only.
- Required action: explicit product/architecture sign-off recorded in `docs/` stating the SuperAdmin homepage builder governs the CommerceHub marketplace homepage ONLY.

**Condition 2 — Disposition the production-dead read stack.**
- Reason: `IHomepageSectionReadService` (+ `HomepageSectionCacheService`, `HomepageSectionSchemaRegistry` read path, `HomepageSectionCacheOptions`) has no production caller after the controller deletion.
- Evidence: consumers = `Program.cs:430-432` registration + `SuperAdminHomepageSectionManagementServiceTests.cs` only.
- Risk: LOW (R-2).
- Required action: decide keep-as-internal-verification-surface vs remove in a follow-up gate; document the decision. (Do NOT fix inside this gate — read-only.)

**Condition 3 — Clean and verify test schema provisioning.**
- Reason: `TestDatabaseScope.cs:147-148` names deleted SQL files; homepage schema now depends solely on `20260723214642_Baseline_Recovery3`.
- Evidence: loader skip behavior `TestDatabaseScope.cs:654-658`; baseline constraints `:1505-1532, 11085-11101`; live dump `DB/schema-23-7-2026.sql:4349-4373`.
- Risk: LOW (R-3).
- Required action: one-time column/constraint/index equivalence check of baseline vs the deleted `20260328_AAA` harden script, then remove the two stale entries in a follow-up commit.

**Condition 4 — Stale documentation flag.**
- Reason: architecture/certification docs still describe `api/homepage/sections` as the storefront read path.
- Evidence: `docs/architecture/homepage-discovery-recommendation-certification-findings.md` and recovery audit docs matched in discovery grep.
- Risk: LOW.
- Required action: mark affected docs superseded in the next documentation gate.

---

## 10. Final Decision

**APPROVE WITH CONDITIONS.**

The Homepage reduction is an intentional, internally consistent read-path relocation: the anonymous SQL-backed sections endpoint was retired, the marketplace homepage moved onto the existing CQRS Mongo read model, the tenant storefront moved onto StoreConfig, and the SuperAdmin authoring/write/projection pipeline was preserved and transactionally tightened. No broken references, routes, imports, DI failures, or permission orphans were found. The four conditions above (one MEDIUM product ratification, three LOW hygiene items) are prerequisites for accepting this state as the official architectural baseline.

Can the Homepage architecture be officially accepted as the new architectural baseline? **Yes — once Condition 1 is ratified; Conditions 2-4 may trail in follow-up gates.**

---

## 11. Evidence Appendix

- Deleted controller (recovered): `git show HEAD:PointOfSale.API/Controllers/HomepageSectionsController.cs` — `[AllowAnonymous]`, `Route("api/homepage/sections")`, published/preview modes, TENANT_REQUIRED guard.
- Diffstat of the change-set: 12 files, +131/-444 (`git diff --stat HEAD -- '*Homepage*' '*homepage*'`).
- Working-tree status (homepage slice): D controller, D 2 SQL scripts, D `useHomepageSections.ts`, D `types/homepage.ts`; M read service, M SuperAdmin service, M CommerceHub queries/types/service/HomePage, M Storefront HomePage/hook, M tests.
- New marketplace read path: `MarketplaceReadController.cs:360` (`[HttpGet("homepage")]`), `:379` tenant-or-global Mongo fetch, `:398` public filtering.
- Projection pipeline: `HomepageSectionEvents.cs` → `HomepageProjectionConsumer.cs:11,27,35` (event fence) → `MarketplaceHomepageReadModelDocument.cs` / `MarketplaceHomepageReadRepository.cs`.
- DI registrations: `Program.cs:225-226, 430-432, 927`.
- SuperAdmin UI wiring: `App.jsx:155-156, 220-221`; `SuperAdminLayout.tsx:133`; `homepageService.ts:128-226`.
- Publish-flow reorder: `SuperAdminHomepageSectionManagementService.cs` diff @376-405 (event published inside transaction before commit).
- Tenant gating modernization: `tenant.IsActive` → `tenant.Status == TenantStatus.Active` (`HomepageSectionReadService.cs:336`; SuperAdmin service `:680`).
- Test infra skip-safety: `TestDatabaseScope.cs:636-677`.
- Storefront typecheck: `npx tsc --noEmit` in `PointOfSale.Storefront` — 0 errors.
- Confidence levels: Section 6 intent = HIGH (multi-axis working-tree evidence); consistency findings = HIGH (grep + typecheck); baseline schema equivalence = MEDIUM (spot-checked constraints/indexes, not exhaustively diffed — see Condition 3).
