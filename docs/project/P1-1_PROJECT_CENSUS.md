# P1-1 — Project Census (Shopixy / PointOfSale)

> **Program:** Shopixy Project Cleanup Program → PROJECT-1 → P1-1
> **Phase type:** DISCOVERY ONLY. Nothing modified, deleted, moved, renamed, or generated.
> **Method:** Every number below is taken from live filesystem / git output (`find`, `git ls-files`, `git status`, `du`, `grep`), not from prior markdown. Estimates are marked `~`.
> **Repo root:** `C:\Users\fadya\source\repos\PointOfSale`
> **Branch:** `recovery/3-greenfield-baseline` · **HEAD:** `8317aef1`
> **Working tree at census time:** dirty (354 staged, 923 modified, 1230 untracked) — a *blocker* for any later move/rename phase.

---

## 1. Executive Summary

Shopixy is a large **multi-tenant e‑commerce / affiliate / POS platform** built as a **.NET 9 monolithic-modular backend** (single solution, C#) with **four React/Vite frontends** and one **Node/Express WhatsApp microservice**. The backend is organized in classic layered projects (Core → Infrastructure → API) plus a dedicated test project. Total **4,451 git-tracked files**.

The codebase itself is healthy and coherent. The **repository hygiene is the problem**:

- **569 loose files in the repo root** — 362 `.md`, 111 `.sql`, 52 `.ps1`, 16 `.txt`, plus build logs and probes. This is the single headline finding.
- **8 orphan C# projects** (13 `.csproj` on disk vs **only 5 in the solution**) living in scratch dirs (`tmp/`, `tools/`, `DbPatcher/`, `DbQuery/`, `SqlRunner/`, `.codex-tmp/`).
- **Large non-source payloads tracked or resident**: `DB/` = 181 MB (SQL dumps + a `.backup` + CSV), `temp/` = 583 MB, `tmp/` = 160 MB, `tools/` = 95 MB.
- **Tracked build artifacts**: `PointOfSale.Tests/TestResults/CurrentRun/current.trx`, three `.binlog` files, `PointOfSale.Tests/build.binlog`.
- **SECURITY — tracked non-example env files**: `PointOfSale.WhatsApp.Baileys/.env.production` and `.env.staging` are committed (not `.example`). `.env.staging` contains at least `AUTH_STATE_ENCRYPTION_SECRET=...` and `.env.production` references a Redis host IP. Must be treated as a secret-exposure finding (verify + rotate in a dedicated step; NOT bulk cleanup).
- **Documentation sprawl**: ~362 root `.md` phase/audit reports plus `docs/` (97 tracked, ~544 untracked) — `docs/architecture/` (596 files) and `docs/recovery/` (508 files) dominate.

No CI is configured: `.github/` contains **no workflow files**. Deployment is script-based (PowerShell + a `staging/Caddyfile`), not container/orchestrator based (no Dockerfile/compose/k8s/terraform found in source).

**Overall census grade: B** — the *system* is well-structured and understandable; the *repository* carries heavy accreted scratch/doc/artifact debt and a real tracked-secret exposure that a downstream phase must address carefully.

---

## 2. Statistics

| Metric | Value | Source |
|---|---|---|
| Git-tracked files (total) | 4,451 | `git ls-files \| wc -l` |
| Top-level directories | 37 | `find -maxdepth 1 -type d` |
| Loose files in repo root | 569 | `find -maxdepth 1 -type f` |
| — root `.md` | 362 | ext histogram |
| — root `.sql` | 111 | ext histogram |
| — root `.ps1` | 52 | ext histogram |
| — root `.txt` | 16 | ext histogram |
| `.sln` files | 1 (`PointOfSale.sln`) | `find` |
| `.csproj` on disk | 13 | `find` |
| `.csproj` in solution | 5 | `grep ^Project PointOfSale.sln` |
| Orphan `.csproj` (not in sln) | 8 | derived |
| Frontend `package.json` | 4 | `find -maxdepth 2` |
| Tracked garbage (bin/obj/TestResults) | 1 | `git ls-files \| grep` |
| Tracked binaries (.binlog/.trx) | 4 | `git ls-files \| grep` |
| Tracked non-example `.env` | 2 | `git ls-files` |
| CI workflow files | 0 | `find .github` |
| .NET SDK pinned | 9.0.308 (net9.0) | `global.json` |

**Approx C# source counts (excl bin/obj):** Core ~516 · Infrastructure ~574 · API Services ~414 · API Controllers 162 · Tests ~836.

---

## 3. Top-Level Directory Inventory (37 dirs)

| Directory | Purpose | Technology | ~Size (excl node_modules) | Status |
|---|---|---|---|---|
| `PointOfSale.API/` | ASP.NET Core Web API — main backend host | .NET 9 / C# | 265 MB (incl bin/obj) | ACTIVE |
| `PointOfSale.Core/` | Domain layer (entities, interfaces, contracts) | .NET 9 / C# | 15 MB | ACTIVE |
| `PointOfSale.Infrastructure/` | Data access, EF Core, external integrations | .NET 9 / C# | 138 MB (incl bin/obj) | ACTIVE |
| `PointOfSale/` | Legacy/original project (in sln, role unclear) | .NET 9 / C# | 42 MB | SUPPORTED (needs investigation) |
| `PointOfSale.Tests/` | xUnit test suite (unit/integration/arch/security) | .NET 9 / xUnit / Moq / FsCheck | 288 MB (incl bin/obj/TestResults) | ACTIVE |
| `PointOfSale.EventBackbone.Tests/` | Event-backbone tests — **NOT in solution** | .NET 9 / C# | small | SUPPORTED (orphan from sln) |
| `PointOfSale.Web/` | Admin / POS Admin SPA | React 18 + Vite 5 | 16 MB | ACTIVE |
| `PointOfSale.Storefront/` | Customer storefront SPA | React 18 + Vite 5 + TS | 5 MB | ACTIVE |
| `PointOfSale.CommerceHub/` | CommerceHub SPA | React 18 + Vite 8 | 2 MB | ACTIVE |
| `PointOfSale.WhatsApp.Baileys/` | WhatsApp integration microservice | Node.js / Express 4 | 2 MB | ACTIVE |
| `docs/` | Structured documentation tree (13 subdirs) | Markdown | 46 MB | ACTIVE (sprawling) |
| `DB/` | Dated SQL dumps, a `.backup`, a CSV export | SQL / PG backup | 181 MB | ARCHIVE (heavy) |
| `migrations/` | 43 migration files | SQL / EF | 1 MB | SUPPORTED |
| `sql/` | Ad-hoc SQL | SQL | 1 MB | SUPPORTED |
| `temp/` | Build outputs, test env, scratch projects | mixed | 583 MB | TEMPORARY |
| `tmp/` | 2 scratch verifier csproj + scripts | .NET / mixed | 160 MB | TEMPORARY |
| `staging/` | Caddyfile + api.env.example + README | Caddy / config | 1 MB | SUPPORTED (deploy) |
| `tools/` | `SIDmsgRunner` utility (orphan csproj) | .NET / C# | 95 MB (incl bin/obj) | SUPPORTED |
| `scripts/` | Helper scripts | mixed | 1 MB | SUPPORTED |
| `testsprite_tests/` | TestSprite MCP generated tests | mixed | 1 MB | GENERATED |
| `n8n-workflows/` | Abandoned-cart recovery workflow export | JSON (n8n) | 1 MB | SUPPORTED |
| `DbPatcher/` | Scratch DB utility (orphan csproj) | .NET / C# | small | EXPERIMENTAL |
| `DbQuery/` | Scratch DB utility (orphan csproj) | .NET / C# | small | EXPERIMENTAL |
| `SqlRunner/` | Scratch SQL runner (orphan csproj) | .NET / C# | small | EXPERIMENTAL |
| `.ai/` | Hermes canonical-architecture knowledge base | Markdown/YAML | 7 MB | ACTIVE (authoritative KB) |
| `.ai-context/` | Project authority rules (source of truth) | Markdown | 2 MB | ACTIVE (authoritative) |
| `.ai-testing/` | AI-driven testing artifacts | mixed | 1 MB | SUPPORTED |
| `.agents/` | Agent config | config | small | SUPPORTED |
| `.codex-temp/` | Codex scratch (git-ignored: .gitignore:56) | mixed | small | TEMPORARY |
| `.codex-tmp/` | Codex scratch incl `LoginProbe` csproj (2 tracked) | .NET / mixed | small | TEMPORARY |
| `.hermes/` | Hermes session/plan state | config | small | SUPPORTED |
| `.varcon04-build/` | Build variant scratch | mixed | small | TEMPORARY |
| `.config/` | Tooling config | config | small | SUPPORTED |
| `.github/` | GitHub dir — **empty, no workflows** | — | small | UNKNOWN (stub) |
| `.vs/` `.vscode/` | IDE state | IDE | small | GENERATED |
| `.git/` | Git internals | git | (n/a) | ACTIVE |

---

## 4. Project Inventory

### 4.1 Solution members (`PointOfSale.sln` — 5 projects)

| Name | Framework | Language | Role | References | Referenced By | Entry Point | Build Target |
|---|---|---|---|---|---|---|---|
| **PointOfSale.Core** | net9.0 | C# | Domain / contracts / entities | (base layer) | Infrastructure, API, Tests | n/a | class library |
| **PointOfSale.Infrastructure** | net9.0 | C# | EF Core, repositories, external integrations, MassTransit/RabbitMQ | Core | API, Tests | n/a | class library |
| **PointOfSale.API** | net9.0 | C# | ASP.NET Core Web API host (162 controllers, ~414 services) | Core, Infrastructure | (host) | `Program.cs` | web app |
| **PointOfSale** | net9.0 | C# | Original/legacy project; role unclear, needs investigation | TBD | TBD | TBD | TBD |
| **PointOfSale.Tests** | net9.0 | C# | xUnit 2.9 + Moq + FsCheck suite (~836 cs) | Core, Infrastructure, API | (none) | test host | test dll |

### 4.2 Orphan C# projects (on disk, NOT in solution — 8)

| Path | Apparent role | Status |
|---|---|---|
| `PointOfSale.EventBackbone.Tests/` | Event backbone tests | Real tests excluded from sln — investigate |
| `tools/SIDmsgRunner/` | Message-runner utility | Utility |
| `DbPatcher/` | DB patch applier | Scratch utility |
| `DbQuery/` | DB query utility | Scratch utility |
| `SqlRunner/` | SQL runner | Scratch utility |
| `tmp/MarketplaceReadLayerVerifier/` | One-off verifier | Scratch |
| `tmp/PreProdDbVerifier/` | One-off verifier | Scratch |
| `.codex-tmp/LoginProbe/` | Auth probe | Scratch (2 files tracked) |

### 4.3 Frontend / service projects (JS)

| Path | Stack | Role |
|---|---|---|
| `PointOfSale.Web/` | React 18 + Vite 5 | Admin / POS Admin SPA |
| `PointOfSale.Storefront/` | React 18 + Vite 5 + TS | Customer storefront |
| `PointOfSale.CommerceHub/` | React 18 + Vite 8 | CommerceHub SPA |
| `PointOfSale.WhatsApp.Baileys/` | Node.js + Express 4 (Baileys) | WhatsApp messaging microservice |

---

## 5. Documentation Inventory

**Two documentation homes plus root sprawl:**

1. **Repo root** — ~362 `.md`: phase/audit/fix reports accreted over time (prefixes `AF-*`, `PHASE-*`, `SERVICES_MARKETPLACE_*`, `CHAT_*`, `SECURITY_*`, `WHATSAPP*`, `MULTI-UNIT-*`, `SOA-*`, `TENANT-ISOLATION-*`). Many are zero-byte stubs (`AF-12_TENANT_ID_DIAGNOSTIC.md`, `AF-18_ROOT_CAUSE_ANALYSIS.md`, `MONITORING_GUIDE.md`).

2. **`docs/` tree** — 97 tracked (~544 untracked):

| Subdir | Files | Class |
|---|---|---|
| `docs/architecture/` | 596 | Architecture / certification-findings |
| `docs/recovery/` | 508 | Recovery program (+ `archive/`) |
| `docs/certifications/` | 14 | Certifications |
| `docs/repository/` | 8 | Repo audit (REPO-1/2/3, EXEC) |
| `docs/audits/` | 5 | Audits |
| `docs/runtime/` | 5 | Runtime |
| `docs/fixes/` | 2 | Fixes |
| `docs/observability/` | 2 | Observability |
| `docs/operations/` | 2 | Operations |
| `docs/issues/` | 1 | Issues |
| `docs/security/` | 1 | Security |
| `docs/production-readiness/` | 0 | empty stub |

3. **`.ai-context/`** (authoritative rules) and **`.ai/hermes/`** (canonical-architecture KB: architecture, audits, business-flows, capabilities, certification, master-data, browser-knowledge).

**Doc classes present:** Architecture · Audits · Certifications · Guides · Deployment · API mapping · Temporary notes · Generated reports. Authoritative and personal/scratch notes are heavily intermixed at root.

---

## 6. Deployment Inventory

| Capability | Found? | Evidence |
|---|---|---|
| Docker | No | no Dockerfile |
| Docker Compose | No | none (yaml hit is testsprite false-positive) |
| Kubernetes / Helm | No | none |
| Terraform | No | none |
| GitHub Actions / CI | **No** | `.github/` empty |
| Azure / AWS | No | none |
| Reverse proxy | Yes | `staging/Caddyfile` |
| Bootstrap scripts | Yes | `setup-database.ps1/.bat`, `restart-api.bat`, root `*.ps1` |
| Validation scripts | Yes | `verify-*.ps1`, `check-*.ps1` |
| Env templates | Yes | `staging/pointofsale.api.env.example`, per-frontend `.env.example` |
| Automation workflows | Yes | `n8n-workflows/abandoned-cart-recovery-workflow.json` |

Deployment model = **script-driven (PowerShell) + Caddy**; no container/orchestration committed.

---

## 7. Database Inventory

| Engine | Role (per `.ai-context`) | Evidence |
|---|---|---|
| **PostgreSQL** | Authoritative | `DB/*.sql`, `DB/PosEcommerceDb_2026-07-15.backup`, `migrations/` (43), `sql/`, root `*.sql` (111) |
| **MongoDB** | Derivative | root `*.js` mongo scripts (`add_affiliate_category_mongo_indexes.js`, `check-mongodb-*.js`) |
| **Redis** | Derivative (cache/session) | `clear-redis-cache.ps1`, Baileys `.env` REDIS_* |
| **RabbitMQ** | Messaging (MassTransit) | `check-rabbitmq-*.ps1`, `MASSTRANSIT_RABBITMQ_TOPOLOGY_ANALYSIS.md` |
| **OpenSearch/Elasticsearch** | Derivative search | `fix-elasticsearch-*.ps1/.sh`, `es_query.json` |

- **SQL scripts:** 111 root + `sql/` + `migrations/` (43) — fragmented dated ad-hoc patches (`add_*.sql`, `fix_*.sql`, `af18-*.sql`).
- **Seed data:** `DB/data-1784830579292.csv`, `seed-units.ps1`, `AF-22-Insert-Test-Data.js`.
- **Backups:** `DB/PosEcommerceDb_2026-07-15.backup`, `DB/24-7-2026_oldData.sql`, `DB/24-7-2026_NewDataEmpty.sql`.
- **`DB/`:** 28 files, ~181 MB — archive of dated dumps, not a managed set.

---

## 8. Frontend Inventory

| Frontend | Project | Stack | Notes |
|---|---|---|---|
| Storefront (customer) | `PointOfSale.Storefront/` | React 18 + Vite 5 + TS | `src/`, `public/`, tailwind |
| Admin / POS Admin | `PointOfSale.Web/` | React 18 + Vite 5 | stray `.cjs` scripts present |
| CommerceHub | `PointOfSale.CommerceHub/` | React 18 + Vite 8 | newest Vite |
| Super Admin | (no separate project) | — | backend area `PointOfSale.API/SuperAdmin/`, surfaced in Admin |
| Landing pages | root `landing_page_enhanced.html` | static HTML | one-off |
| Shared UI libs | none standalone | — | not identified |

---

## 9. Testing Inventory

**Framework:** xUnit 2.9.2 + Microsoft.NET.Test.Sdk 17.12.0 + Moq 4.20.72 + FsCheck.Xunit 3.3.2. **~836 `.cs`**.

| Requested category | Present as |
|---|---|
| Unit | `Unit/`, `Core/`, `Services/` |
| Integration | `Integration/` |
| Architecture | `Architecture/` |
| Security | `Security/`, `Middleware/` |
| Performance | none (no dedicated dir) |
| Certification | via `docs/certifications/` (docs) |
| Utilities / doubles | `TestDoubles/`, `artifacts/`, `Temp/` |

Domain suites: `Affiliate/`, `Accounting/`, `Payments/`, `Shipping/`, `Analytics/`, `Cms/`, `CommerceHub/`, `Storefront/`, `SuperAdmin/`, `WhatsApp/`, `AbandonedCart/`, `PhoneRisk/`, `Admin/`, `Controllers/`.

Additional: `PointOfSale.EventBackbone.Tests/` (orphan, not in sln), `testsprite_tests/` (63 tracked, AI-generated).

**Tracked test artifacts (garbage):** `PointOfSale.Tests/TestResults/CurrentRun/current.trx`, `PointOfSale.Tests/build.binlog`.

---

## 10. Technology Inventory

- **Backend:** .NET 9 (SDK 9.0.308), C#, ASP.NET Core, EF Core, MassTransit + RabbitMQ, SignalR (`Hubs/`), OpenTelemetry/Prometheus (`Instrumentation/`).
- **Frontends:** React 18, Vite 5/8, TypeScript, Tailwind.
- **Microservice:** Node.js + Express 4 + Baileys.
- **Data:** PostgreSQL (authoritative), MongoDB, Redis, Elasticsearch/OpenSearch.
- **Automation:** n8n. **Proxy:** Caddy.
- **Scripts:** PowerShell (52 root `.ps1`), bash, Python (`fix_arabic.py`), JS mongo scripts.

---

## 11. Git Hygiene Census

| Check | Result |
|---|---|
| Tracked files total | 4,451 |
| Tracked bin/obj/node_modules/TestResults | 1 (`PointOfSale.Tests/TestResults/CurrentRun/current.trx`) |
| Tracked binaries (.binlog/.trx) | 4 (`current.trx`, `PointOfSale.Tests/build.binlog`, `api-build.binlog`, `marketplace-event-backbone.binlog`) |
| Tracked non-example `.env` | **2** (`Baileys/.env.production`, `Baileys/.env.staging`) |
| Working tree | DIRTY: 354 staged, 923 modified, 1230 untracked |
| Largest untracked backlog | `docs/` (544), `PointOfSale.Tests/` (413), `PointOfSale.Web/` (101), `PointOfSale.API/` (84) |
| `.codex-temp/` ignore | **IGNORED** — verified `.gitignore:56` |
| `tmp/` ignore | **IGNORED** — verified `.gitignore:48` |

Unlike the skill's cautionary note, `git check-ignore -v` here **confirms** `.codex-temp/` and `tmp/` ARE ignored. `.codex-tmp/` (with `m`) still has 2 tracked files (`LoginProbe`).

---

## 12. ASCII Architecture Map

```
                         +-----------------------------------+
   React SPAs            |        PointOfSale.API (net9)     |
 +---------------+       |  162 controllers . ~414 services  |
 | Web (Admin)   |------>|  Middleware . Hubs(SignalR)       |
 | Storefront    |------>|  Consumers(MassTransit)           |
 | CommerceHub   |------>|  SuperAdmin . CommerceHub areas   |
 +---------------+       +-------+---------------+-----------+
                                 | refs          | refs
                         +-------v------+  +------v------------+
                         |Infrastructure|  |   Core (domain)   |
                         | EF/repos/    |->| entities/contracts|
                         | integrations |  +-------------------+
                         +--+---+---+----+--+---------+
                            |   |   |      |         |
                      Postgres Mongo Redis RabbitMQ  ES/OpenSearch
                      (auth)  (deriv)(cache)(msg)    (search)

 Node microservice:  PointOfSale.WhatsApp.Baileys (Express + Baileys -> Redis)
 Automation: n8n-workflows/   Proxy: staging/Caddyfile   Tests: PointOfSale.Tests (xUnit)
```

---

## 13. Risk Inventory

| Risk | Level | Detail |
|---|---|---|
| Tracked production/staging `.env` secrets | **HIGH** | Baileys `.env.production`+`.env.staging` committed; contain encryption secret + infra host |
| Dirty working tree | **HIGH (blocker)** | 354+923+1230 pending — blocks safe move/rename |
| Root-file sprawl (569) | Medium | Hard to navigate; mixes authoritative + scratch |
| Orphan csprojs (8) unbuilt by sln | Medium | Live utilities vs dead scratch unclear |
| Large tracked/resident payloads | Medium | `DB/` 181MB, `temp/` 583MB, `tmp/` 160MB, `tools/` 95MB |
| Tracked build artifacts (.binlog/.trx) | Low-Med | Should be ignored; not secret |
| No CI (`.github/` empty) | Medium | No automated build/test gate |
| Zero-byte doc stubs | Low | Empty `.md` reports mislead readers |
| `PointOfSale/` legacy project role | Unknown | In sln but purpose unclear |

---

## 14. Unknown Areas (named)

- **`PointOfSale/`** — original project in sln; role vs `PointOfSale.API` unestablished.
- **`.github/`** — empty; intent (planned CI?) unknown.
- **`PointOfSale.EventBackbone.Tests/`** — real tests excluded from sln; reason unknown.
- **`.varcon04-build/`** — build-variant scratch of unknown provenance.
- **Which of the 8 orphan csprojs are live** vs abandoned one-offs.
- **`DB/` dated dumps** — which file is the current authoritative schema/seed.

---

## 15. Cleanup Opportunities (DISCOVERY ONLY — no action taken)

Observations only; no deletions/moves performed:

- 569 loose root files (362 md / 111 sql / 52 ps1) are relocation candidates (later phase).
- Numerous zero-byte `.md` stubs at root.
- Tracked `.binlog`/`.trx` artifacts and 2 tracked non-example `.env` files stand out.
- `temp/`, `tmp/`, `.varcon04-build/`, `.codex-temp/` hold large scratch payloads.
- `DB/` holds an archive of dated dumps + `.backup` + CSV.
- 8 orphan csprojs sit outside the solution.

**Blocker for any later cleanup phase:** commit/stash the dirty working tree first, and handle the tracked-secret exposure in a dedicated isolated step (verify + rotate + untrack), never inside a bulk cleanup.

---

*End of P1-1 Project Census. Discovery only — nothing was modified, deleted, moved, renamed, or generated.*
