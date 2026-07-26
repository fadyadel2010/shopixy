# REPO-2 — REPOSITORY CLASSIFICATION & CANONICAL OWNERSHIP

Repository: C:\Users\fadya\source\repos\PointOfSale (Shopixy / PointOfSale platform)
Branch: recovery/3-greenfield-baseline
Date: 2026-07-24
Mode: READ-ONLY. No files were modified, moved, renamed, staged, or committed.
Basis: Built on REPO-1_COMPLETE_REPOSITORY_CENSUS.md and re-verified against the live filesystem and git index (`git ls-files`, per-directory inspection, `.csproj`/`package.json`/config reads). Every classification below traces to a REPO-1 finding and was confirmed against real files.
Scope: Classification and ownership only. No implementation, no deletion, no move recommendations executed.

---

## 1. EXECUTIVE SUMMARY

The repository is a working .NET 9 + Node/React monorepo (Shopixy) whose **production core is small and clearly owned**, but whose **root is heavily polluted** by ~569 loose files (362 markdown reports, 111 SQL patches, 52 PowerShell scripts, tracked build logs/binlogs) and whose **documentation is quadruple-homed** (root, docs/, .ai/hermes, .ai-context). REPO-1 established the census; this phase assigns to every significant artifact a **category, canonical owner, lifecycle, and cleanup disposition** so ownership is unambiguous.

Ownership model in one line: **7 production source projects + 3 backend .csproj in the solution are owned by Backend/Frontend engineering; everything else is Database, Deployment, Documentation, AI-Program, Tooling, Testing, or Temporary — and the root is a Temporary/Historical dumping ground owned by no one.**

Confirmed against live files (deltas vs REPO-1 noted):
- 25 top-level real directories + 13 hidden dirs (incl. .git). Solution references 5 of 13 on-disk `.csproj`.
- Frontends confirmed by package.json name: pointofsale-web, pointofsale-storefront, pointofsale-commercehub, pointofsale-whatsapp-baileys.
- `.github/workflows/` **exists but is empty** (REPO-1 said `.github` empty — refined: the workflows subdir is present, zero workflow files → CI never materialized).
- `.agents/` empty (dead). `.hermes/` = 1 prompt file. `.config/dotnet-tools.json` pins dotnet-ef 9.0.1. `global.json` pins SDK 9.0.308.
- Tracked garbage confirmed via git index: 3 `.binlog`, 11 root log `.txt`, `temp/homepage-api-obj/**`, 21 `DB/*` dumps.
- **Secrets confirmed tracked**: `PointOfSale.WhatsApp.Baileys/.env.production` and `.env.staging` are in the git index (`.env.example` legitimately tracked).

Certification (this phase): **B — Ownership mostly established; minor ambiguity remains** (legacy `PointOfSale/` runtime role, `PointOfSale.Admin` stub, DB-dump/n8n consumers, docs supersession chain). See §11–13.

---

## 2. REPOSITORY OWNERSHIP MODEL

Canonical owners (roles, not people):

| Owner | Mandate | Primary artifacts |
|---|---|---|
| **Backend** | .NET 9 server: API host, domain, data/integrations, backend tests | PointOfSale.API, PointOfSale.Core, PointOfSale.Infrastructure, PointOfSale.Tests, PointOfSale.EventBackbone.Tests |
| **Frontend** | React/Node UIs + Node transport | PointOfSale.Web, PointOfSale.Storefront, PointOfSale.CommerceHub, PointOfSale.WhatsApp.Baileys |
| **Database** | Schema evolution & data | migrations/, sql/, scripts/ (SQL), DB/ dumps, root loose *.sql |
| **Operations / Deployment** | Deploy & runtime topology | staging/, n8n-workflows/, .config/, global.json, .github/ |
| **AI Program** | Canonical architecture initiative + AI governance | .ai/hermes/, .ai-context/, .ai-testing/, .hermes/ |
| **Platform / Docs** | Institutional documentation | docs/ |
| **Developer Tooling** | Dev-only utilities | DbPatcher/, DbQuery/, SqlRunner/, tools/SIDmsgRunner, root *.ps1/*.bat/*.sh/*.py/*.cs probes |
| **Historical Archive** | Superseded session artifacts | root 362 *.md, superseded phase docs |
| **Testing** | Automated E2E harness | testsprite_tests/ |
| **No owner (disposable)** | Regenerable/scratch | .codex-temp/, .codex-tmp/, .varcon04-build/, temp/, tmp/, .vs/, bin/obj, root logs/binlogs |

Rule of authority (from project convention / .ai-context): **implementation is the only source of truth; `.ai/hermes` supersedes overlapping root and docs/ claims; root README is stale.**

---

## 3. DIRECTORY CLASSIFICATION MATRIX

Each directory assigned exactly ONE primary category.

| Directory | Category | Owner | Verified |
|---|---|---|---|
| PointOfSale.API | Backend | Backend | csproj in sln; appsettings family present |
| PointOfSale.Core | Backend | Backend | csproj in sln |
| PointOfSale.Infrastructure | Backend | Backend | csproj in sln |
| PointOfSale.Tests | Testing | Backend | csproj in sln |
| PointOfSale.EventBackbone.Tests | Testing | Backend | csproj on disk, NOT in sln |
| PointOfSale.Web | Frontend | Frontend | package.json: pointofsale-web |
| PointOfSale.Storefront | Frontend | Frontend | package.json: pointofsale-storefront |
| PointOfSale.CommerceHub | Frontend | Frontend | package.json: pointofsale-commercehub |
| PointOfSale.WhatsApp.Baileys | Runtime Component | Frontend/Node | package.json: pointofsale-whatsapp-baileys; **tracked .env secrets** |
| PointOfSale (no suffix) | Legacy | Backend | csproj in sln; legacy ASP.NET app |
| PointOfSale.Admin | Unknown | Backend (?) | contains only README.md (2.2 KB) — stub |
| migrations/ | Database | Database | 43 dated SQL migrations |
| DB/ | Database | Database | 21 tracked dumps (27 entries on disk) — backups in git |
| sql/ | Database | Database | 1 file (phase_lcr_block_legacy_writes.sql) |
| scripts/ | Database | Database | SQL seeds + Migrations/ + 2 diagnostics |
| DbPatcher/ | Tooling | Developer Tooling | csproj console utility |
| DbQuery/ | Tooling | Developer Tooling | csproj console utility |
| SqlRunner/ | Tooling | Developer Tooling | csproj console utility |
| tools/ | Tooling | Developer Tooling | SIDmsgRunner csproj |
| tmp/ | Temporary | Developer Tooling | self-declared temporary (README); 2 verifier csproj |
| staging/ | Deployment | Operations | Caddyfile, env example, README |
| n8n-workflows/ | Deployment | Operations | 1 automation workflow JSON |
| testsprite_tests/ | Testing | Testing | 63 tracked Python E2E files |
| docs/ | Documentation | Platform/Docs | 662 md; architecture/recovery/certifications |
| .ai/ (hermes) | AI Governance | AI Program | canonical-architecture program (128 md) |
| .ai-context/ | AI Governance | AI Program | 7 authority-rule md |
| .ai-testing/ | AI Governance | AI Program | test strategy + testsprite host |
| .hermes/ | AI Governance | AI Program | 1 prompt file |
| .config/ | Infrastructure | Operations | dotnet-tools.json (dotnet-ef pin) |
| .github/ | Infrastructure | Operations | workflows/ present but **empty** (no CI) |
| .vscode/ | Tooling | Developer Tooling | editor config |
| .agents/ | Historical | — | **empty (dead)** |
| .codex-temp/ | Temporary | No owner | 247 MB Codex sandbox (untracked) |
| .codex-tmp/ | Temporary | No owner | LoginProbe scratch (2 tracked files) |
| .varcon04-build/ | Generated | No owner | 247 MB variant build output (untracked) |
| temp/ | Generated | No owner | build outputs; temp/homepage-api-obj/** tracked |
| .vs/ | Generated | No owner | 81 MB VS cache (untracked) |
| **root loose files (569)** | Historical / Temporary | Historical Archive / No owner | 362 md + 111 sql + 52 ps1 + logs (see §8) |

---

## 4. LIFECYCLE MATRIX

Exactly one lifecycle per directory.

| Lifecycle | Directories |
|---|---|
| **ACTIVE** | PointOfSale.API, PointOfSale.Core, PointOfSale.Infrastructure, PointOfSale.Web, PointOfSale.Storefront, PointOfSale.CommerceHub, PointOfSale.WhatsApp.Baileys, PointOfSale.Tests, migrations/, .ai/ (hermes), .ai-context/, docs/ (recovery subset) |
| **SUPPORTED** | scripts/, sql/, staging/, n8n-workflows/, .config/, docs/ (architecture/certifications), tools/SIDmsgRunner |
| **MAINTENANCE** | PointOfSale.EventBackbone.Tests, DbPatcher/, DbQuery/, SqlRunner/, .ai-testing/, .hermes/, .vscode/ |
| **ARCHIVED** (candidate) | root 362 *.md (historical reports), superseded docs chains |
| **TEMPORARY** | tmp/, .codex-tmp/, root *.ps1/*.sql/probe files |
| **GENERATED** | .codex-temp/, .varcon04-build/, temp/, .vs/, bin/obj everywhere, root logs/binlogs |
| **EXPERIMENTAL** | testsprite_tests/ (TestSprite program status unconfirmed), appsettings.af22.json |
| **LEGACY** | PointOfSale/ (old ASP.NET app), WhatsAppMigration.sql / WhatsAppTemplatesMigration.sql era scripts |
| **UNKNOWN** | PointOfSale.Admin (stub), .github/workflows/ (empty), .agents/ (empty/dead), DB/ dump consumers |

---

## 5. DOCUMENTATION OWNERSHIP MATRIX

Four documentation homes exist. Authority precedence: **.ai/hermes > .ai-context (governance) > docs/ > root**.

| Family / Location | Status | Owner | Rationale |
|---|---|---|---|
| **.ai/hermes/** (128 md, canonical architecture) | **CANONICAL** | AI Program | Implementation-backed FINAL CANONICAL ARCHITECTURE; supersedes conflicting claims elsewhere |
| **.ai-context/** (7 authority md) | **CANONICAL (governance)** | AI Program | ARCHITECTURE_TRUTH, CODEX_RULES, KNOWN_RISKS, PROJECT_CONTEXT — govern all AI work |
| **docs/recovery/** (19) | **CANONICAL (active program)** | Platform/Docs | Current recovery governance (this REPO-x series lives in docs/repository/) |
| **docs/architecture/** (596 certification-findings, ADRs) | **CANONICAL where not superseded**; else Historical | Platform/Docs | Largest valuable set; supersession map vs .ai/hermes not yet built |
| **docs/certifications, audits, runtime, security, ...** | SUPPORTED | Platform/Docs | Topical institutional docs |
| **docs/ loose** (PAYMENT_LIFECYCLE_ARCHITECTURE, FINANCIAL_ARCHITECTURE_CERTIFIED, STOREFRONT-ARCHITECTURE, WhatsApp audits) | CANONICAL per topic | Platform/Docs | Subject-authoritative single copies |
| **Root 362 *.md** (AF-*, PHASE-*, WHATSAPP-*, *_SUMMARY, *_FIX_COMPLETE) | **HISTORICAL / SUPERSEDED** | Historical Archive | Session/phase artifacts; multi-"final" chains (AF-12 has 4+ "final" docs) |
| **Root README.md** | **SUPERSEDED (stale)** | Historical Archive | Declared stale by .ai-context authority |
| **.ai-testing/** md | SUPPORTED | AI Program | Test strategy / invariants |
| **node_modules READMEs** (~2,583 md) | GENERATED | — | Dependency noise, ignore |

Conflict families (four homes for one subject):
- Certification content: root *_CERTIFICATION.md vs docs/certifications vs docs/architecture *-certification-findings vs .ai/hermes/certification. → **Canonical = .ai/hermes/certification; docs/architecture = supporting; root = historical.**
- WhatsApp: root WHATSAPP-* vs docs/WHATSAPP_*. → **Canonical = docs/ + .ai/hermes; root = historical.**

---

## 6. CONFIGURATION OWNERSHIP MATRIX

| Config class | Canonical location | Duplicate/Generated/Historical copies | Owner |
|---|---|---|---|
| **appsettings (backend)** | PointOfSale.API/appsettings{.json,.Development,.Development.local,.Production,.Staging,.Test,.Test.local} | Generated copies: .codex-temp/build/tests/appsettings*.json, temp/pos-test-out/*, temp/stripe-provider1-tests/*. Historical/obsolete: appsettings.af22.json (root) | Backend |
| **appsettings (legacy)** | PointOfSale/appsettings.json + .Development | — | Backend (legacy) |
| **launchSettings** | PointOfSale.API/Properties/launchSettings.json; PointOfSale/Properties/launchSettings.json | — | Backend |
| **.env (frontends/Node)** | *.env.example files (Web, Storefront, CommerceHub, Baileys) | **RISK: Baileys/.env.production + .env.staging TRACKED (git index confirmed)** — canonical location is local/untracked; committed copies are a security exposure | Frontend / Operations |
| **staging config** | staging/Caddyfile + staging/pointofsale.api.env.example | — | Operations |
| **global.json** | root global.json (SDK 9.0.308) | — | Operations |
| **tool manifest** | .config/dotnet-tools.json (dotnet-ef 9.0.1) | — | Operations |
| **local-only settings** | appsettings.*.local.json (gitignore-matched) | — | Backend (per-dev) |
| **production settings** | PointOfSale.API/appsettings.Production.json; Baileys/.env.production | sandbox copies under .codex-temp/ | Backend / Frontend |

Canonical rule: **PointOfSale.API/appsettings.* is the ONLY authoritative backend config family.** All copies under temp/ and .codex-temp/ are generated publish/sandbox output and non-authoritative.

---

## 7. TOOLING OWNERSHIP MATRIX

| Tool | Classification | Owner | Evidence |
|---|---|---|---|
| **DbPatcher/** | Permanent tool | Developer Tooling | csproj; applies SQL patches |
| **DbQuery/** | Permanent tool | Developer Tooling | csproj; queries DB |
| **SqlRunner/** | Permanent tool | Developer Tooling | csproj; runs SQL |
| **tools/SIDmsgRunner** | Permanent tool | Developer Tooling | csproj utility runner |
| **tmp/MarketplaceReadLayerVerifier, tmp/PreProdDbVerifier** | Temporary tool | Developer Tooling | tmp/README self-declares temporary, "not part of main build" |
| **.codex-tmp/LoginProbe** | Experimental/scratch | No owner | scratch probe csproj (2 tracked files) |
| **testsprite_tests/ + .ai-testing/testsprite** | Experimental (status unconfirmed) | Testing / AI Program | 63 tracked files; TestSprite program activity not verified |
| **n8n-workflows/** | Permanent (runtime automation asset) | Operations | abandoned-cart-recovery workflow JSON |
| **.hermes/, .ai/, .ai-context/, .ai-testing/** | Permanent (AI program) | AI Program | active canonical-architecture initiative |
| **Codex folders (.codex-temp/, .codex-tmp/)** | Historical/scratch | No owner | Codex sandbox output, regenerable |
| **Root one-off verifiers/probes** (CheckMongoData.cs, CheckOrderAttribution.cs, test_pg.cs, fix_arabic.py, es_query.json) | Temporary | Developer Tooling | loose, not in any project |

---

## 8. REPOSITORY ROOT OWNERSHIP

The 569 loose root files, by disposition class (classification only — no action taken):

| Root artifact class | Count | Should… | Owner |
|---|---|---|---|
| PointOfSale.sln | 1 | **remain in root** | Backend |
| global.json | 1 | **remain in root** | Operations |
| .gitignore | 1 | **remain in root** | Operations |
| README.md | 1 | remain in root (rewrite; currently stale) | Platform/Docs |
| Historical phase reports (*.md) | 362 | **archive** (→ docs/history/) | Historical Archive |
| One-off SQL patches (*.sql) | 111 | **move** (→ migrations/legacy/) | Database |
| Helper/diagnostic scripts (*.ps1) | 52 | **move** (→ scripts/) | Developer Tooling |
| Loose *.bat/*.sh/*.py/*.cs probes | ~7 | **move** (→ scripts/) | Developer Tooling |
| Build/test logs (*.txt) | 11 tracked | **should leave git** (untrack) then delete | No owner |
| Binlogs (*.binlog) | 2 root (+1 in Tests) | **should leave git** (untrack) then delete | No owner |
| appsettings.af22.json | 1 | **become generated / remove** (obsolete) | Backend |
| stray ".md" (4 KB, no context) | 1 | archive/remove | Historical Archive |
| HTML debug pages (AF-12_DEBUG_*, landing_page_enhanced) | 2 | archive | Historical Archive |

Disposition summary: **3 files legitimately own the root (sln, global.json, .gitignore) + README.** Everything else is misplaced (move/archive) or garbage (leave git).

---

## 9. CANONICAL REPOSITORY MAP (target conceptual structure)

Ownership boundaries of the TARGET repository (conceptual — no folders invented beyond REPO-1's own recommendations; nothing moved here):

```
PointOfSale (Shopixy monorepo)
│
├── Backend/                         [owner: Backend]
│   ├── PointOfSale.API              backend host        [sln]
│   ├── PointOfSale.Core             domain              [sln]
│   └── PointOfSale.Infrastructure   data/integrations   [sln]
│
├── Frontend/                        [owner: Frontend]
│   ├── PointOfSale.Web              POS Admin (React)
│   ├── PointOfSale.Storefront       storefront
│   ├── PointOfSale.CommerceHub      CommerceHub
│   └── PointOfSale.WhatsApp.Baileys WhatsApp transport (Node)
│
├── Testing/                         [owner: Backend / Testing]
│   ├── PointOfSale.Tests            [sln]
│   ├── PointOfSale.EventBackbone.Tests
│   └── testsprite_tests             E2E (Python)
│
├── Database/                        [owner: Database]
│   ├── migrations/                  canonical dated SQL
│   ├── scripts/, sql/               seeds & one-offs
│   ├── DB/                          dumps (→ external backup; not git)
│   └── DbPatcher, DbQuery, SqlRunner  DB utilities
│
├── Deployment/                      [owner: Operations]
│   ├── staging/                     Caddyfile + env
│   ├── n8n-workflows/               automation
│   ├── .config/, global.json        build tooling / SDK pin
│   └── .github/                     CI (currently empty)
│
├── Documentation/                   [owner: Platform/Docs]
│   └── docs/  (architecture, certifications, recovery, repository)
│
├── AI/                              [owner: AI Program]
│   ├── .ai/hermes/                  CANONICAL architecture program
│   ├── .ai-context/                 authority rules
│   ├── .ai-testing/                 test strategy
│   └── .hermes/
│
├── Tooling/                         [owner: Developer Tooling]
│   ├── tools/SIDmsgRunner, .vscode/
│   └── tmp/ (temporary verifiers)
│
├── Legacy / Unknown/                [owner: Backend, unverified]
│   ├── PointOfSale                  old ASP.NET app  [sln]
│   └── PointOfSale.Admin            README-only stub
│
├── Historical/                      [owner: Historical Archive]
│   └── (root 362 reports → docs/history/)
│
└── Temporary / Generated/           [owner: none — disposable]
    ├── .codex-temp/, .codex-tmp/, .varcon04-build/
    ├── temp/, tmp/, .vs/
    ├── bin/, obj/, node_modules/, TestResults
    └── root build logs + binlogs
```

---

## 10. CLEANUP READINESS MATRIX

| Category | Readiness |
|---|---|
| Temporary/Generated (.codex-temp, .varcon04-build, .vs, bin/obj) | **Safe immediately** (untracked, regenerable) |
| Root logs/binlogs, temp/homepage-api-obj/**, .codex-tmp, TestResults .trx | **Safe immediately** after `git rm --cached` (needs git history preservation = no; just untrack) |
| Root 362 *.md → docs/history/ | **Needs migration** (git mv to preserve history) |
| Root 111 *.sql → migrations/legacy/ | **Needs investigation** (grep for script references) then migration |
| Root 52 *.ps1 + probes → scripts/ | **Needs investigation** then migration |
| DB/ dumps (21 tracked, ~181 MB) | **Needs archive** (move to external backup) + **needs git history preservation** decision (size) |
| Baileys .env.production / .env.staging | **Needs security review** (rotate secrets, then untrack) |
| PointOfSale/ legacy app | **Needs investigation** (runtime/deploy role unverified) |
| PointOfSale.Admin stub | **Needs investigation** (purpose unknown) |
| .github/workflows/ (empty), .agents/ (empty) | **Needs investigation** (was CI ever configured?) then safe removal |
| testsprite_tests/ | **Needs investigation** (program still active?) |
| docs/architecture supersession chains | **Needs investigation** (build supersession map vs .ai/hermes) |
| Production source (7 folders) + .ai/ + .ai-context + docs/recovery | **Do not touch** (active) |

---

## 11. OWNERSHIP CONFLICTS

Detected conflicts requiring resolution before implementation:

1. **Multiple canonical documentation locations** — certification content lives in 4 homes (root, docs/certifications, docs/architecture/*-certification-findings, .ai/hermes/certification). Resolution rule set: .ai/hermes canonical, docs/ supporting, root historical — but the explicit supersession map is **not yet built**.
2. **Duplicate configuration ownership** — appsettings exists authoritatively in PointOfSale.API AND as generated copies under temp/ and .codex-temp/ (incl. a Production copy in the Codex sandbox). No ambiguity of authority, but the sandbox Production copy is a **latent exposure**.
3. **Conflicting/duplicated configs** — appsettings.af22.json at root is an orphan experiment config with no owning environment.
4. **Legacy overlap** — PointOfSale/ (legacy app) still shares the solution with the modern API; both have appsettings + launchSettings; deploy ownership of the legacy app is **unverified**.
5. **AI-governance overlap** — .ai/, .ai-context/, .ai-testing/, .hermes/ are four AI-owned homes; boundaries are clear (canonical vs rules vs testing vs prompt) but consolidation is possible.
6. **Root-pollution ownership** — 569 root files have **no canonical owner**; they are the single largest ambiguity. Ownership assigned in §8 (Historical Archive / Database / Developer Tooling / No owner) but not yet enacted.
7. **Database dispersion** — SQL lives in 5 places (migrations/, sql/, scripts/, DB/, 111 root *.sql). Canonical = migrations/; the rest need consolidation.
8. **Tracked secrets** — Baileys .env.production/.env.staging owned by Frontend but violate Operations/security policy by being in git.

---

## 12. RECOMMENDED REPOSITORY GOVERNANCE

Classification-level governance rules (no implementation):

1. **One canonical home per artifact class**: backend config → PointOfSale.API; SQL → migrations/; docs → docs/ (+ .ai/hermes canonical); scripts → scripts/. Enforce "no new artifacts at repo root except sln/global.json/.gitignore/README."
2. **Authority precedence for docs**: .ai/hermes (canonical) > .ai-context (governance) > docs/ > root (historical). Any conflict resolves upward.
3. **Generated/scratch never tracked**: .codex-*, .varcon04-build/, temp/, *.binlog, build*.txt, TestResults belong in .gitignore + .gitattributes (add missing .gitattributes).
4. **Secrets never tracked**: .env.production/.env.staging must be untracked + rotated (security phase); only .env.example is committable.
5. **Data backups leave git**: DB/ dumps → external backup storage.
6. **Every directory carries an owner**: adopt the §2 owner map; empty/dead dirs (.agents, empty .github/workflows) are flagged for removal after CI-history investigation.
7. **Freeze before cleanup**: 2,508 dirty entries must be committed/stashed before any relocation (REPO-1 §11 blocker).

---

## 13. CERTIFICATION

**GRADE: B — Ownership mostly established; minor ambiguity remains.**

Justification:
- Every top-level directory has a category (§3), owner (§2), lifecycle (§4), and cleanup disposition (§10), all re-verified against the live filesystem and git index (not just the census).
- Documentation (§5), configuration (§6), tooling (§7), and root (§8) ownership matrices are complete; canonical vs historical vs generated is decided for each family.
- The canonical target map (§9) and conflict register (§11) give a new engineer answers to: Who owns this? Why does it exist? Is it active? Is it canonical? Can it move? Can it leave git?

Residual ambiguity (why not A):
1. Runtime/deploy role of legacy `PointOfSale/` app unverified (in sln, but is it deployed?).
2. `PointOfSale.Admin` stub purpose unknown (README only).
3. Consumers of `DB/` dumps and the n8n workflow not traced.
4. `.github/workflows/` present but empty — was CI ever configured?
5. Docs supersession chain (root ↔ docs/ ↔ .ai/hermes) named but not yet mapped file-by-file.

None of these block a REPO-3 implementation-planning phase for the LOW-risk, clearly-owned classes. They must be resolved before touching Legacy/Unknown categories.

No file was modified, moved, renamed, staged, or committed during this phase.

--- END OF REPO-2 ---
