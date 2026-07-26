# P1-3 — Repository Target Architecture (Shopixy / PointOfSale)

> **Program:** Shopixy Project Cleanup Program → PROJECT-1 → P1-3
> **Phase type:** ARCHITECTURE DESIGN ONLY. Nothing was modified, moved, renamed, deleted, or generated.
> **Inputs (authoritative):** `docs/project/P1-1_PROJECT_CENSUS.md`, `docs/project/P1-2_DEAD_ASSET_DISCOVERY.md`
> **Verification:** Every structural claim below was cross-checked against the LIVE tree on branch `recovery/3-greenfield-baseline` (top-level dirs, `.github` emptiness, `migrations/`=43, `DB/`=28, root 361 `.md`/111 `.sql`/52 `.ps1`, `PointOfSale.Admin` has no `package.json`, `PointOfSale.sln`=5 projects). Census numbers are treated as evidence, not gospel.
> **Repo root:** `C:\Users\fadya\source\repos\PointOfSale`

---

## 0. Purpose & Status

This document is the **architectural blueprint** for building the *new* repository layout. It is the successor contract to P1-1 (what exists) and P1-2 (what is dead). It does **NOT** execute any move/rename/delete. A later phase (P1-4 execution) will translate the Classification Matrix (§10) into git operations.

The single design thesis, drawn from P1-1 §1 and P1-2 §1:

> The codebase is healthy; the **repository hygiene** is the disease. A small live core — 5 solution projects + 4 node/React apps + `migrations/` + `staging/` + canonical docs — is buried under a **sediment layer** of 569 root files, 8 orphan csprojs, and ~1.9 GB of scratch dumps.

The target architecture separates **live core** from **sediment** at the directory level, so the sediment can be archived/removed without touching anything that builds, tests, or deploys.

---

## 1. Design Principles (enforced)

| # | Principle | Source finding it answers |
|---|---|---|
| P1 | **Minimal root.** Root holds only navigation + build-anchor files. Zero reports, zero scripts, zero loose SQL. | P1-1 §1: 569 loose root files; P1-2 §4. |
| P2 | **Exactly one logical home** per asset class. No directory may overlap another's category. | P1-1 §5: docs spread across root + `docs/` + `.ai/`; P1-2 §5. |
| P3 | **No category duplication.** `deploy/`, `scripts/`, `db/`, `docs/` are mutually exclusive by content type. | P1-1 §6/§7: deploy scattered (`.ps1` root, `staging/`, `n8n-workflows/`); SQL in root + `sql/` + `migrations/` + `DB/`. |
| P4 | **Historical ≠ active.** Completed-phase reports NEVER live beside implementation or active docs. | P1-2 §5.2: ~330 root `.md` are historical; governance rule demotes prior markdown. |
| P5 | **Generated artifacts never beside source.** Build logs, `.binlog`, `.trx`, LLM scratch are quarantined + git-ignored. | P1-1 §2: tracked `.binlog`/`.trx`; P1-2 §4.1. |
| P6 | **Temporary work never beside production.** Scratch dirs are isolated + ignored. | P1-1 §3: `tmp/`, `temp/`, `.codex-*`, `.varcon04-build/` = 583+160 MB scratch. |
| P7 | **Secrets never committed.** `.env` (non-example) is a tracked-secret finding, handled in an isolated step, never in bulk cleanup. | P1-1 §2, P1-2 §3.2 implicit; `.env.production`/`.env.staging` in `PointOfSale.WhatsApp.Baileys/`. |
| P8 | **Evidence over assumption.** "Unknown" dirs (P1-2 §6) are not assigned a destructive disposition; they get an investigation gate. | P1-2 §6 UNKNOWN list. |
| P9 | **Authoritative DB = migrations; dumps = archive.** Live schema lives in ordered migrations; point-in-time dumps are retention data, relocated not deleted. | P1-1 §7, P1-2 §3.9. |
| P10 | **AI knowledge is first-class, not clutter.** Canonical architecture KB has a permanent, owned home. | P1-1 §3: `.ai/hermes` is the active canonical-architecture program. |

---

## 2. Target Top-Level Directory Map

```
PointOfSale/                      (repository root — minimal; see §4)
├── README.md                     navigation + index only
├── PointOfSale.sln               solution (5 projects)
├── global.json                   SDK pin (9.0.308)
├── .editorconfig                 (NEW — currently ABSENT, see §4)
├── Directory.Build.props         (NEW — shared MSBuild conventions)
├── .gitignore                    (rewritten — see §4)
├── LICENSE
├── src/                          ALL buildable source (C# + React + Node)
├── tests/                        ALL test suites (replaces scattered test dirs)
├── db/                           database: migrations + ops + verify + backups + exports
├── deploy/                       deployment + IaC + proxy + env templates
├── scripts/                      bootstrap/ops/diagnostic one-time scripts
├── docs/                         active curated documentation (by class)
├── ai/                          AI engineering knowledge base (canonical)
├── tools/                       OPTIONAL: promoted internal utilities (investigation-gated)
├── archive/                     historical/completed artifacts (read-only retention)
├── .github/                     CI/CD workflows (currently EMPTY — to be populated)
├── .config/                     tooling manifests (dotnet-tools.json — keep)
└── (generated/temp are OUT of tree — see §9/§10)
```

**Explicitly rejected as top-level homes:** `assets/` (no asset pipeline exists — P1-1 shows no design-system/asset dir), `temp/`/`tmp/` (quarantined, see §9), `DB/`/`sql/`/`migrations/` (folded into `db/`), `staging/` (folded into `deploy/`), `n8n-workflows/` (folded into `deploy/automation/`), `testsprite_tests/` (folded into `tests/generated/` or `archive/`), `.ai-testing/` (folded into `tests/` or `ai/`), `PointOfSale.Admin/` (see §8 Unknown), `PointOfSale/` legacy (see §8).

---

## 3. Directory Specifications

For each target directory: **Purpose · Allowed · Forbidden · Owner · Lifecycle · Real examples from current repo that belong here.**

### 3.1 `src/` — Source of truth
- **Purpose:** Every buildable, shippable source artifact (C# projects, React apps, Node microservice).
- **Allowed:** `.csproj`, `.cs`, `.tsx/.ts`, `.jsx`, `package.json`, `Program.cs`, `wwwroot`, `appsettings*.json` that belong to a project.
- **Forbidden:** docs, reports, ad-hoc SQL, build logs, scratch csprojs, `.env` (non-example).
- **Owner:** Backend / Frontend guild leads.
- **Lifecycle:** Permanent. Every file here is compiled/tested by CI.
- **Current examples → target:**
  - `PointOfSale.Core/` → `src/PointOfSale.Core/`
  - `PointOfSale.Infrastructure/` → `src/PointOfSale.Infrastructure/`
  - `PointOfSale.API/` → `src/PointOfSale.API/`
  - `PointOfSale/` (legacy MVC, UNKNOWN runtime) → `src/PointOfSale/` **pending investigation (§8)**, NOT deleted
  - `PointOfSale.Web/` → `src/web/admin/`
  - `PointOfSale.Storefront/` → `src/web/storefront/`
  - `PointOfSale.CommerceHub/` → `src/web/commercehub/`
  - `PointOfSale.WhatsApp.Baileys/` → `src/services/whatsapp-baileys/`

### 3.2 `tests/` — Verification
- **Purpose:** All automated test suites (unit/integration/arch/security/perf) + AI-generated test harnesses.
- **Allowed:** test projects, `*Tests.csproj`, spec files, fixtures, test data, LLM-generated tests.
- **Forbidden:** production source, build logs, ad-hoc scripts.
- **Owner:** QA guild.
- **Lifecycle:** Permanent; runs in CI gate.
- **Current examples → target:**
  - `PointOfSale.Tests/` → `tests/PointOfSale.Tests/`
  - `PointOfSale.EventBackbone.Tests/` (orphan, P1-2 §3.2 DEAD-candidate) → fold real coverage into `tests/PointOfSale.Tests/`, then **archive/remove** the orphan (see Matrix §10 row 7)
  - `testsprite_tests/` (63 tracked, P1-2 §3.11 SUPPORTED minus `_`/`___` duplicates) → `tests/generated/testsprite/`
  - `.ai-testing/` (21 tracked, SUPPORTED) → `tests/ai/` or `ai/knowledge/qa-scenarios/`
  - tracked test artifacts (`PointOfSale.Tests/TestResults/CurrentRun/current.trx`, `build.binlog`) → **git-ignored + relocated to generated area (§9)**, never in `tests/`.

### 3.3 `db/` — Database (replaces `migrations/`, `sql/`, `DB/`, root `*.sql`)
- **Purpose:** Authoritative schema + operational/verification scripts + backup retention + exports + scratch SQL, cleanly separated (per P1-1 §7 and Principle P9).
- **Subdirs:**
  - `db/migrations/` — **authoritative, ordered, applied** SQL (successor convention; the 43 files currently in `migrations/` + promoted root `add_*.sql`/`fix_*.sql` that are confirmed-applied).
  - `db/ops/` — operational one-time scripts (backfills, index fixes) that are environment-specific but may still be needed.
  - `db/verify/` — read-only diagnostics/verification queries (`check*.sql`, `verify_*.sql`, `af18-*.sql`).
  - `db/backups/` — point-in-time dumps + `.backup` (the 28 `DB/` entries; **relocation, never deletion** — P1-2 §3.9 HIGH risk if deleted).
  - `db/exports/` — CSV/data exports (`DB/data-*.csv`).
  - `db/scratch/` — throwaway SQL, git-ignored.
- **Forbidden:** anything non-DB; build logs.
- **Owner:** Data/platform guild.
- **Lifecycle:** `migrations/` permanent + linear; `backups/` rotated by retention policy (ideally pulled OUT of git to object storage — see §9 note).
- **Current examples → target:**
  - `migrations/` (43) → `db/migrations/`
  - `sql/phase_lcr_block_legacy_writes.sql` → `db/ops/` (one-time guard)
  - `scripts/*sql` (SeedFawry*, AddTenantPaymentMethods*, etc.) → `db/migrations/` or `db/ops/` by nature
  - root `*.sql` (111) → **promote-to-migrations / ops / verify by per-file DB check (P1-2 §4.2)**; applied-history = archive to `archive/db/`
  - `DB/` (28) → `db/backups/` + `db/exports/`
  - Mongo scripts (`add_affiliate_category_mongo_indexes.js`, `check-mongodb-*.js`) → `db/ops/` (derivative store)

### 3.4 `deploy/` — Deployment & IaC (replaces `staging/`, `n8n-workflows/`, root deploy `*.ps1`)
- **Purpose:** Everything needed to stand up an environment: proxy config, env templates, IaC, automation-workflow exports, bootstrap/deploy scripts.
- **Subdirs:**
  - `deploy/proxy/` — `Caddyfile` (+ future nginx/traefik)
  - `deploy/env/` — `*.env.example` templates (from `staging/pointofsale.api.env.example`, per-frontend `.env.example`)
  - `deploy/iac/` — future Dockerfile/compose/k8s/terraform (P1-1 §6: currently NONE — this dir is the landing zone when adopted)
  - `deploy/automation/` — `n8n-workflows/abandoned-cart-recovery-workflow.json`
  - `deploy/bootstrap/` — `setup-database.ps1/.bat` (P1-2 §4.3 SUPPORTED, NOT dead)
- **Forbidden:** application source, test code, historical reports.
- **Owner:** DevOps.
- **Lifecycle:** Permanent; versioned with env changes.
- **Current examples → target:**
  - `staging/Caddyfile` → `deploy/proxy/Caddyfile`
  - `staging/pointofsale.api.env.example` → `deploy/env/`
  - `n8n-workflows/` → `deploy/automation/`
  - `setup-database.ps1` → `deploy/bootstrap/`

### 3.5 `scripts/` — Operational scripts (curated)
- **Purpose:** Reusable bootstrap / ops / diagnostics / one-time-migration executors that are NOT deployment-resident and NOT dead.
- **Allowed:** Powershell/bash/py that is referenced by other scripts or docs (e.g. `secret-config-helpers.ps1` with ≥3 consumers — P1-2 §4.3; `marketplace-diagnostics.ps1/.sh`; `restart-api.bat`; `monitor-baileys-rollout.ps1`).
- **Forbidden:** generated logs, one-time dead `fix-*` scratch, build artifacts.
- **Owner:** DevOps / guild leads.
- **Lifecycle:** Permanent but reviewed; dead one-timers are demoted to `archive/`.
- **Current examples → target:**
  - `scripts/marketplace-diagnostics.*` → KEEP `scripts/ops/`
  - `secret-config-helpers.ps1` → KEEP `scripts/lib/` (shared, dot-sourced)
  - `restart-api.bat` + `RESTART-API-GUIDE.md` → `scripts/ops/`
  - `monitor-baileys-rollout.ps1` → `scripts/ops/`

### 3.6 `docs/` — Active curated documentation (by class)
- **Purpose:** Living, onboarding/ops/architecture docs that are NOT historical reports.
- **Subdirs (aligns with P1-1 §5 `docs/` tree, kept + rationalized):**
  - `docs/architecture/` — system/component architecture (current)
  - `docs/operations/` — runbooks, monitoring, deployment guides
  - `docs/security/` — security posture
  - `docs/observability/` — telemetry/OTel/Prometheus
  - `docs/certifications/` — certification evidence
  - `docs/runtime/` — runtime behavior notes
  - `docs/developer/` — onboarding, language guide, quickstart
  - `docs/recovery/` — active recovery-program governance (non-archive)
  - `docs/recovery/archive/` — 484 intentionally-archived recovery files (P1-2 §5.3) — **kept as-is**
  - `docs/audits/`, `docs/fixes/`, `docs/issues/` — curated; duplicate chains vs root reports resolved in P1-4 (P1-2 §5.4 UNKNOWN)
  - `docs/project/` — program deliverables (THIS document lives here)
- **Forbidden:** historical phase diaries (those go to `archive/`), build logs, source.
- **Owner:** Docs guild / architecture owners.
- **Lifecycle:** Permanent; edited as system evolves. Superseded docs are moved to `archive/`, not deleted.
- **Promotion from root (P1-2 §5.2 evidence-against-dead):** rule/architecture docs like `RABBITMQ_TOPOLOGY_GOVERNANCE_RULES.md`, `AFFILIATE_CROSS_DOMAIN_BOUNDARIES.md`, `ECOMMERCE-ARCHITECTURE.md`, `UNIFIED-FINANCIAL-ENGINE-ARCHITECTURE.md` → `docs/architecture/` (promote, not archive).

### 3.7 `ai/` — AI engineering knowledge base (replaces `.ai/`, `.ai-context/`)
- **Purpose:** First-class home for machine- and human-readable AI context: governance authority + canonical-architecture KB. (Principle P10.)
- **Subdirs:**
  - `ai/context/` — the authoritative project rules (currently `.ai-context/`: `ARCHITECTURE_TRUTH.md`, `CODEX_RULES.md`, `CROSS_SYSTEM_AUTHORITY_HARDENING.md`, `KNOWN_RISKS.md`, `LESSON_UPDATE_RULES.md`, `PROJECT_CONTEXT.md`, etc.). **Source of truth per governance.**
  - `ai/knowledge/` — canonical-architecture program output (currently `.ai/hermes/`: `architecture/`, `audits/`, `business-flows/`, `capabilities/`, `certification/`, `master-data/`, `platform/`, `systems/`, `browser-knowledge/`, `accounts/`, `_scratch/`).
  - `ai/testing/` — QA scenario evidence (folds `.ai-testing/`).
- **Forbidden:** generated session logs, build artifacts, secrets.
- **Owner:** AI-engineering / architecture guild.
- **Lifecycle:** Permanent; grows as canonical docs are produced.
- **Note:** `.ai/hermes/_scratch/` is AI working scratch → quarantine to generated area (§9), not committed as knowledge.

### 3.8 `tools/` — Promoted internal utilities (INVESTIGATION-GATED)
- **Purpose:** Only utilities that survive investigation and are proven live get a home here. Default disposition for orphan csprojs is **archive/remove**, not `tools/`.
- **Allowed (only after P1-2 §6 UNKNOWN resolved):** e.g. `tools/SIDmsgRunner/` IF manual usage is confirmed (P1-2 §3.4 MEDIUM confidence).
- **Forbidden:** `DbPatcher/`, `DbQuery/`, `SqlRunner/`, `tmp/*`, `.codex-tmp/LoginProbe` (these are DEAD-candidates → `archive/` or delete per Matrix).
- **Owner:** Guild that owns the utility.
- **Lifecycle:** Permanent only if justified; otherwise one-way to `archive/`.

### 3.9 `archive/` — Historical / completed retention (READ-ONLY)
- **Purpose:** Completed-phase reports, legacy SQL applied-history, legacy scripts, snapshots, recovery archives, dead scratch. The permanent resting place for non-active evidence. (Principle P4.)
- **Subdirs:**
  - `archive/reports/` — the ~330 root historical `.md` (AF-*, PHASE-*, SERVICES_MARKETPLACE_*, etc.) + orphan HTML (`AF-12_DEBUG_AFFILIATE_AUTH.html`, `landing_page_enhanced.html`)
  - `archive/db/` — applied root SQL patches whose history we keep (mitigates P1-2 §4.2 risk of losing applied-history)
  - `archive/scripts/` — dead one-time `fix-*`/`check-*`/diagnostics
  - `archive/scratch-projects/` — `DbPatcher/`, `DbQuery/`, `SqlRunner/`, `PointOfSale.EventBackbone.Tests/`, `tmp/*`, `.codex-tmp/LoginProbe/`, `tools/SIDmsgRunner/` (if not promoted)
  - `archive/recovery/` — mirrors `docs/recovery/archive/` intent if separated
- **Forbidden:** anything still referenced by build/CI/source; secrets.
- **Owner:** Docs/archival steward.
- **Lifecycle:** Append-only; pruning requires explicit approval. **Never deleted blindly** — P1-2 treats most of this as "relocate, not delete."

### 3.10 `.github/` — CI/CD (currently EMPTY)
- **Purpose:** GitHub Actions workflows, templates, issue forms. P1-1 §6 / P1-2 §3.10: **0 files today** — no CI exists.
- **Allowed:** `workflows/*.yml`, `PULL_REQUEST_TEMPLATE.md`, etc.
- **Forbidden:** source, docs.
- **Owner:** DevOps.
- **Lifecycle:** Populated when CI is adopted (a stated objective: CI/CD support).
- **Note:** The emptiness is a finding (P1-2 §3.10 DEAD-dir), but the *directory* is the future CI home — keep it, populate it; do not treat as removable clutter once CI lands.

### 3.11 `.config/` — Tooling manifests
- **Purpose:** dotnet local tools, editor/analyzer config.
- **Allowed:** `dotnet-tools.json` (currently present, P1-2 §2 ACTIVE).
- **Forbidden:** source, scripts.
- **Owner:** Platform guild.
- **Lifecycle:** Permanent.

---

## 4. Root Policy (exactly what may live at repo root)

Per Principle P1, root is **minimal and navigational only**. Allowed root entries:

| Allowed root item | Status today | Notes |
|---|---|---|
| `README.md` | present (stale per `.ai-context`) | becomes navigation index only |
| `PointOfSale.sln` | present | solution anchor |
| `global.json` | present | pins SDK 9.0.308 |
| `LICENSE` | — | add if missing |
| `.editorconfig` | **ABSENT** (verified) | **NEW** — add for uniform C#/TS style |
| `Directory.Build.props` | **ABSENT** (verified) | **NEW** — shared MSBuild/analyzer config (kills per-project duplication) |
| `.gitignore` | present (defective) | **REWRITE** per §11 |
| `docs/` `src/` `tests/` `db/` `deploy/` `scripts/` `ai/` `archive/` `.github/` `.config/` `tools/` | dirs | the only directories allowed at root |

**Everything else currently in root is disallowed** and is disposed via the Matrix (§10): 361 `.md` → `archive/reports/` or `docs/`; 111 `.sql` → `db/`; 52 `.ps1`/`11 .js` → `scripts/`/`db/ops/`/`archive/`; 3 orphan `.cs` (`CheckMongoData.cs`, `CheckOrderAttribution.cs`, `test_pg.cs`) → `archive/scratch-projects/` (P1-2 §4.4 DEAD); 2 `.binlog` + 16 `.txt` logs → git-ignored generated area (§9); `GitHub..gitignore` (typo name) → delete (P1-2 §4.4 DEAD); `.md` (broken-filename dot-md) → `docs/` or delete.

---

## 5. Source Layout (detail)

```
src/
├── backend/                      (C# .NET 9 solution projects)
│   ├── PointOfSale.Core/
│   ├── PointOfSale.Infrastructure/
│   ├── PointOfSale.API/
│   └── PointOfSale/              (legacy MVC — INVESTIGATION-GATED, §8)
├── web/                          (React/Vite SPAs)
│   ├── admin/                    (PointOfSale.Web)
│   ├── storefront/               (PointOfSale.Storefront)
│   └── commercehub/              (PointOfSale.CommerceHub)
└── services/                     (Node microservices)
    └── whatsapp-baileys/         (PointOfSale.WhatsApp.Baileys)
```
Rationale: single `src/` makes "where does code live" answerable in one path; backend/web/services split mirrors the three tech stacks. `PointOfSale/` legacy kept under `src/backend/` pending §8 verdict (no silent deletion).

---

## 6. Documentation Layout (detail)

See §3.6. Curated `docs/` by class; historical reports exiled to `archive/reports/`; canonical AI authority under `ai/`. This ends the three-way doc sprawl (root + `docs/` + `.ai/`) by giving each class exactly one home.

---

## 7. Deployment Layout (detail)

See §3.4. `deploy/{proxy,env,iac,automation,bootstrap}/`. Today only `proxy` (Caddyfile) + `env` (example) + `automation` (n8n) exist; `iac/` is the empty landing zone for the container/orchestration that P1-1 §6 says is absent — the target architecture provisions the home before the tech is adopted.

---

## 8. Unknown Areas — Investigation Gates (no destructive disposition)

From P1-2 §6 UNKNOWN. The blueprint defers these; P1-4 MUST resolve before acting:

| Unknown | Gate before disposition | Current placement in target |
|---|---|---|
| `PointOfSale.Admin/` | No `package.json` (verified). Is it a stub/dead shell or future SuperAdmin UI? | Hold in `src/web/admin/` **only if** it is the real admin UI; else `archive/`. Do NOT delete unverified. |
| `PointOfSale/` (legacy MVC) | Confirm runtime/deploy role (EF migrations startup? deployed legacy UI?). P1-2 §3.1: HIGH risk if removed. | `src/backend/PointOfSale/` pending gate. |
| `docs/audits|fixes|issues` vs root report duplicates | Diff pass to pick canonical (P1-2 §5.4) | resolve in `docs/`, duplicates → `archive/`. |
| Every root SQL applied in all envs? | DB-side check (P1-2 §4.2) | applied → `db/migrations/`; history kept → `archive/db/`. |
| Which of 8 orphan csprojs are live? | repo-wide usage grep (P1-2 already did: 0 inbound refs) | all DEAD-candidates → `archive/scratch-projects/`. |

---

## 9. Generated Output Policy & Temporary Workspace Policy

Per P5/P6. **Generated + temporary content lives OUTSIDE the tracked source tree.**

- **Build artifacts** (`.binlog`, `TestResults/*.trx`, `bin/`, `obj/`, `node_modules/`, `.playwright`) → git-ignored; never committed. `api-build.binlog` (verified NOT ignored today — P1-2 §4.1) must be added to `.gitignore`.
- **AI session scratch** (`.codex-temp/`, `.codex-tmp/`, `.varcon04-build/`, `.ai/hermes/_scratch/`, `.hermes/`) → git-ignored + local-only; `.codex-tmp/LoginProbe` (2 tracked files) → `git rm --cached` + `archive/`.
- **Temp runtime dirs** (`tmp/`, `temp/`) → `temp/` is ignored yet 19 files tracked (P1-2 §3.8 defect) → `git rm --cached` those; `tmp/` already ignored.
- **DB dumps** (`db/backups/`) ideally relocated to **object storage / off-git** (P1-2 §7.4: repo bloat). If kept in-git, must be git-ignored + pulled on demand, never committed blindly.

No generated/temp directory may sit at repo root or inside `src/`, `tests/`, `db/`, `deploy/`, `docs/`, `ai/`.

---

## 10. Classification Matrix (current dir → target home + disposition)

Disposition vocabulary: **KEEP** (stays, maybe relocated), **MOVE** (relocate to target), **ARCHIVE** (relocate to `archive/`), **REMOVE** (delete — only for proven-dead + safe), **UNKNOWN** (investigation gate, §8). All "REMOVE" rows are gated per P1-2 evidence/risk.

| # | Current Location | Target Location | Disposition | Reason (evidence) |
|---|---|---|---|---|
| 1 | `PointOfSale.sln` | root | KEEP | sole solution (P1-1 §2) |
| 2 | `global.json` | root | KEEP | SDK pin 9.0.308 |
| 3 | `PointOfSale.Core/`, `.Infrastructure/`, `.API/` | `src/backend/` | MOVE | active sln projects |
| 4 | `PointOfSale/` (MVC) | `src/backend/PointOfSale/` | UNKNOWN | legacy, unresolved runtime (P1-2 §3.1) |
| 5 | `PointOfSale.Tests/` | `tests/PointOfSale.Tests/` | MOVE | active test project |
| 6 | `PointOfSale.EventBackbone.Tests/` | fold → `tests/PointOfSale.Tests/` then `archive/scratch-projects/` | ARCHIVE | orphan, exe-harness, 0 refs (P1-2 §3.2) |
| 7 | `PointOfSale.Web/` | `src/web/admin/` | MOVE | active admin SPA |
| 8 | `PointOfSale.Storefront/` | `src/web/storefront/` | MOVE | active storefront |
| 9 | `PointOfSale.CommerceHub/` | `src/web/commercehub/` | MOVE | active CH SPA |
| 10 | `PointOfSale.WhatsApp.Baileys/` | `src/services/whatsapp-baileys/` | MOVE | active microservice; `.env.production/.staging` = SECRET finding (§11) |
| 11 | `PointOfSale.Admin/` | `src/web/admin/` or `archive/` | UNKNOWN | no package.json (verified) |
| 12 | `migrations/` (43) | `db/migrations/` | MOVE | authoritative migrations |
| 13 | `sql/` | `db/ops/` | MOVE | one-time guard script |
| 14 | `DB/` (28, 181MB) | `db/backups/` + `db/exports/` | MOVE→relocate off-git | retention data, HIGH risk if deleted (P1-2 §3.9) |
| 15 | root `*.sql` (111) | `db/{migrations,ops,verify}/` + applied→`archive/db/` | MOVE/ARCHIVE | one-time sediment (P1-2 §4.2) |
| 16 | `scripts/` (curated) | `scripts/{ops,lib}/` | KEEP/MOVE | diagnostics + shared helper |
| 17 | root `setup-database.ps1/.bat` | `deploy/bootstrap/` | MOVE | SUPPORTED bootstrap (P1-2 §4.3) |
| 18 | root `secret-config-helpers.ps1` | `scripts/lib/` | KEEP | ≥3 dot-source consumers (P1-2 §4.3) |
| 19 | root `restart-api.bat` + guide | `scripts/ops/` | MOVE | operator convenience |
| 20 | root `monitor-baileys-rollout.ps1` | `scripts/ops/` | MOVE | recent ops |
| 21 | root other `*.ps1/*.js/*.bat/*.sh` (~65) | `scripts/ops/` or `archive/scripts/` | MOVE/ARCHIVE | one-time dead-candidates (P1-2 §4.3) |
| 22 | `staging/` (Caddyfile, env.example) | `deploy/{proxy,env}/` | MOVE | deployment config |
| 23 | `n8n-workflows/` | `deploy/automation/` | MOVE | deployment artifact |
| 24 | root `*.md` (~330 historical) | `archive/reports/` (or `docs/` if rule/architecture) | ARCHIVE | historical/superseded (P1-2 §5.2) |
| 25 | root promotion docs (`RABBITMQ_TOPOLOGY_*` etc.) | `docs/architecture/` | MOVE | rule/architecture, not diary (P1-2 §5.2) |
| 26 | root `CheckMongoData.cs` etc. (3) | `archive/scratch-projects/` | REMOVE | compile-orphans, DEAD (P1-2 §4.4) |
| 27 | `landing_page_enhanced.html`, `AF-12_DEBUG_*.html`, `es_query.json`, `appsettings.af22.json` | `archive/reports/` or REMOVE | ARCHIVE/REMOVE | DEAD standalone (P1-2 §4.4) |
| 28 | `GitHub..gitignore` (typo) | — | REMOVE | stray duplicate (P1-2 §4.4) |
| 29 | `.md` (broken filename) | `docs/` or REMOVE | MOVE/REMOVE | naming accident |
| 30 | `api-build.binlog`, `marketplace-event-backbone.binlog` | generated (git-ignored) | REMOVE-from-git | GENERATED→DEAD (P1-2 §4.1) |
| 31 | root `build_*.txt`/`errors*.txt`/`test_output*.txt` (16) | generated (git-ignored) | REMOVE-from-git | GENERATED→DEAD |
| 32 | `DbPatcher/`, `DbQuery/`, `SqlRunner/` | `archive/scratch-projects/` | ARCHIVE | orphan utilities, 0 refs (P1-2 §3.3) |
| 33 | `tools/SIDmsgRunner/` | `tools/` (if proven) else `archive/` | UNKNOWN | MED confidence (P1-2 §3.4) |
| 34 | `tmp/*` verifiers | (ignored) → drop | REMOVE | self-declared temp (P1-2 §3.5) |
| 35 | `.codex-temp/`, `.varcon04-build/` | git-ignored local | REMOVE-from-git | GENERATED (P1-2 §3.7) |
| 36 | `.codex-tmp/LoginProbe/` (2 tracked) | `git rm --cached` + `archive/` | REMOVE-from-git | accidental scratch commit (P1-2 §3.6) |
| 37 | `temp/` (19 tracked) | `git rm --cached` | REMOVE-from-git | tracked-despite-ignored defect (P1-2 §3.8) |
| 38 | `testsprite_tests/` (63) | `tests/generated/testsprite/` (dedupe `_`/`___`) | MOVE | QA harness (P1-2 §3.11) |
| 39 | `.ai-testing/` (21) | `ai/testing/` or `tests/ai/` | MOVE | QA scenario evidence |
| 40 | `.ai/hermes/**` | `ai/knowledge/` | MOVE | canonical KB |
| 41 | `.ai-context/**` | `ai/context/` | MOVE | authoritative rules |
| 42 | `.ai/hermes/_scratch/` | generated (git-ignored) | REMOVE-from-git | AI working scratch |
| 43 | `.hermes/` | local-only (git-ignored) | REMOVE-from-git | session state |
| 44 | `docs/` (curated tree) | `docs/` | KEEP | active docs home |
| 45 | `docs/recovery/archive/` (484) | `docs/recovery/archive/` | KEEP | intentional archive |
| 46 | `.github/` (empty) | `.github/` + populate CI | KEEP | future CI home (P1-2 §3.10) |
| 47 | `.config/` | `.config/` | KEEP | dotnet-tools.json |
| 48 | `.agents/` (empty) | remove or repurpose | REMOVE/UNKNOWN | empty shell (P1-2 §3.11) |
| 49 | `.vs/`, `.vscode/` | git-ignored local | REMOVE-from-git | IDE state |

---

## 11. Secret-Handling Note (cross-cutting, isolated step)

`PointOfSale.WhatsApp.Baileys/.env.production` and `.env.staging` are **tracked non-example env files** containing `AUTH_STATE_ENCRYPTION_SECRET` and a Redis host IP (P1-1 §2, P1-2 §3 implicit). This is a **security finding, not cleanup debris**. The target architecture mandates: env secrets are NEVER in git. In `deploy/env/` only `*.env.example` (no secrets) are committed. The real `.env` files must be: (1) verified + rotated by a dedicated isolated step, (2) `git rm --cached`, (3) added to `.gitignore` — **never** bulk-cleaned alongside other files.

---

## 12. Target `.gitignore` Requirements (for P1-4)

Rewrite to guarantee P5/P6/P7:
- Ignore `**/bin/`, `**/obj/`, `**/node_modules/`, `**/.playwright/`, `**/TestResults/`, `**/*.binlog`, `**/*.trx`.
- Ignore scratch dirs at root: `.codex-temp/`, `.codex-tmp/`, `.varcon04-build/`, `.hermes/`, `tmp/` (already), `temp/` (already, but fix 19 tracked files).
- Ignore `db/backups/` and `db/exports/` (off-git retention) — or relocate to object storage.
- Ignore `**/.env` and `**/.env.*` EXCEPT `*.env.example`.
- Keep `migrations/` and `db/migrations/` tracked (authoritative).
- Resolve `migrations/` vs `db/migrations/` path once MOVE in P1-4 lands (avoid double-ignore).

---

## 13. How This Blueprint Supports the Stated Objectives

| Objective | How the target delivers it |
|---|---|
| Enterprise dev / large teams | `src/` + `tests/` + clear ownership (§3) → monocytes of responsibility; no root scavenger hunt |
| CI/CD | `.github/` home provisioned; generated artifacts ignored so CI cache is clean (§9, §12) |
| Testing | `tests/` consolidates scattered suites + AI-generated; `tests/generated/` segregated |
| Documentation | `docs/` by class + `ai/` authority ends 3-way sprawl; history in `archive/` |
| Deployment | `deploy/{proxy,env,iac,automation,bootstrap}` is the single deploy surface |
| AI-assisted engineering | `ai/context` + `ai/knowledge` first-class, owned, versioned |
| Long-term maintainability | one-home-per-asset (P2/P3); historical quarantined (P4); generated quarantined (P5) |

---

## 14. Success Criteria for P1-3

- [x] Target top-level dirs defined with purpose/allowed/forbidden/owner/lifecycle (§3).
- [x] Every major current directory mapped to a future home with disposition + reason (§10).
- [x] Root policy enumerates exactly what may live at root (§4).
- [x] Archive / DB / Docs / Script policies separated per the brief (§3.3, §3.6, §3.5, §9).
- [x] Unknown areas gated, not silently deleted (§8).
- [x] No file was moved, renamed, deleted, or generated. **Design only.**

---

*End of P1-3 Repository Target Architecture. Architecture blueprint only — no implementation, no cleanup.*
