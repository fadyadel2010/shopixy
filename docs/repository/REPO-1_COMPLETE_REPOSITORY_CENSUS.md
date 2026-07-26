# REPO-1 — COMPLETE REPOSITORY CENSUS

Repository: C:\Users\fadya\source\repos\PointOfSale (Shopixy / PointOfSale platform)
Branch at census time: recovery/3-greenfield-baseline
Date: 2026-07-24
Mode: READ-ONLY. No files were modified, moved, renamed, or deleted.
Method: recursive filesystem scan (excl. .git object store), git index inspection, per-directory sampling. All numbers are actual tool output, not estimates, unless marked "~".

---

## 1. EXECUTIVE SUMMARY

The repository is a working, startup-stable multi-project .NET 9 + Node/React monorepo, but it is severely polluted at the root and carries large volumes of tracked build artifacts, historical AI-generated reports, and ad-hoc scripts.

Key findings:

- 606 top-level entries: 37 directories (+.git) and 569 loose files at the repository root.
- 569 loose root files: 362 markdown reports, 111 SQL patch scripts, 52 PowerShell scripts, 16 build/test output logs, plus binlogs, one-off C# probes, and HTML debug pages. Nearly all are historical work-session artifacts (AF-xx, WHATSAPP-*, phase reports) that belong in docs/ or nowhere.
- 4,802 tracked files; 2,508 dirty entries (1,277 modified + 1,231 untracked) at census time.
- Tracked garbage confirmed: build logs (build_error.txt etc. — 14 files), .binlog files (3), a .trx test result, temp/homepage-api-obj/** (compiled obj output committed to git), and 21 SQL database dumps under DB/ (~181 MB directory).
- 6 temp/scratch top-level dirs (.codex-temp 247 MB, .varcon04-build 247 MB, .codex-tmp, temp/, tmp/, .vs 81 MB) — roughly 580+ MB of disposable weight.
- One solution (PointOfSale.sln) referencing only 5 of the 13 .csproj files found; the rest are utilities/scratch (DbPatcher, DbQuery, SqlRunner, tmp/* verifiers, tools/SIDmsgRunner, .codex-tmp/LoginProbe, EventBackbone.Tests).
- Secrets exposure: PointOfSale.WhatsApp.Baileys/.env.production and .env.staging are TRACKED in git (root .gitignore says .env* but they were committed before/around the rule). A local .env also exists (untracked).
- Documentation is triple-homed: root (362 md), docs/ (662 md, of which 596 in docs/architecture), and .ai/hermes (128 md canonical-architecture docs). Massive duplication and superseded phase reports.

Certification: B — mostly understood; small unknown areas remain (see §13).

---

## 2. REPOSITORY STATISTICS

Counts exclude .git object store. node_modules IS included where noted (it dominates js/ts counts).

| Metric | Count |
|---|---|
| Solutions (.sln) | 1 (PointOfSale.sln) |
| C# projects (.csproj) | 13 (5 in solution) |
| C# source files (.cs) | 3,181 |
| JS files (incl. node_modules) | 38,549 |
| TS files (incl. node_modules) | 10,351 |
| Markdown files (.md) | 3,735 total (mostly node_modules READMEs); 362 root + 662 docs/ + 128 .ai/ = 1,152 project docs |
| SQL files | 403 (111 loose at root, 43 migrations/, 21 DB/ dumps, rest scattered) |
| PowerShell (.ps1) | 244 (52 at root) |
| Shell (.sh) | 82 |
| Batch (.cmd/.bat) | 133 + 2 root .bat |
| Python (.py) | 57 (testsprite tests + utilities) |
| JSON | 2,905 |
| YAML (.yml/.yaml) | 366 (mostly node_modules; .github workflows: none found) |
| Dockerfiles | 1 (inside node_modules — effectively ZERO project Dockerfiles) |
| docker-compose | 1 (inside node_modules — effectively ZERO) |
| Images (png/jpg/svg/gif) | 100 |
| PDFs | 0 |
| Archives (zip/tar/gz) | 6 (+66 .gz in node_modules count) |
| DLLs on disk | 2,008 (bin/obj + .codex-temp/.varcon04-build) |
| Hidden top-level folders | 13 (.agents .ai .ai-context .ai-testing .codex-temp .codex-tmp .config .git .github .hermes .varcon04-build .vs .vscode) |
| AI-related folders | 4 (.ai, .ai-context, .ai-testing, .agents; plus .hermes prompt file) |
| Temp folders (top-level) | 6 (.codex-temp, .codex-tmp, .varcon04-build, temp, tmp, + .vs cache) |
| Tracked files in git | 4,802 |
| Loose files at repo root | 569 |

Approximate directory sizes (MB, excl. node_modules): PointOfSale.Tests 288, PointOfSale.API 265, .varcon04-build 247, .codex-temp 247, DB 181, PointOfSale.Infrastructure 138, .vs 81, docs 46, PointOfSale (legacy web) 42, EventBackbone.Tests 32, PointOfSale.Web 16, Core 15, .ai 7, Storefront 5. (Size is dominated by bin/obj/TestResults, i.e. regenerable output.)

---

## 3. TOP-LEVEL INVENTORY & 4. DIRECTORY CLASSIFICATION

Legend: Build = used by build; RT = used at runtime; Test = used by tests; Gen = generated; Risk = risk if deleted.

### 3.1 Production source (in PointOfSale.sln)

| Folder | Purpose | Build | RT | Test | Risk | Recommendation |
|---|---|---|---|---|---|---|
| PointOfSale.API | Main ASP.NET Core 9 backend host (controllers, Program.cs, appsettings) | Yes | Yes | Yes | CRITICAL | Keep. Clean bin/obj/temp inside it only. |
| PointOfSale.Core | Domain layer (entities, interfaces, contracts) | Yes | Yes | Yes | CRITICAL | Keep. |
| PointOfSale.Infrastructure | Data access, EF Core, services, integrations (Postgres/Mongo/Redis/ES) | Yes | Yes | Yes | CRITICAL | Keep. |
| PointOfSale | LEGACY ASP.NET web app (own appsettings, launchSettings; 42 MB) — predecessor of API; still in .sln | Yes (sln) | Unclear | No | HIGH-UNKNOWN | Keep for now. Verify whether anything deploys it; likely legacy candidate for later archive phase. |
| PointOfSale.Tests | Main test suite (288 MB incl. TestResults, bin/obj, tracked .trx/.binlog) | No | No | Yes | HIGH | Keep source; the tracked TestResults/current.trx and build.binlog are cleanup targets. |

### 3.2 Production source (NOT in .sln — Node/frontends & satellites)

| Folder | Purpose | Risk | Recommendation |
|---|---|---|---|
| PointOfSale.Web | POS Admin React frontend (pointofsale-web) | CRITICAL | Keep. |
| PointOfSale.Storefront | Customer storefront frontend (pointofsale-storefront) | CRITICAL | Keep. |
| PointOfSale.CommerceHub | CommerceHub frontend (pointofsale-commercehub) | CRITICAL | Keep. |
| PointOfSale.WhatsApp.Baileys | Node WhatsApp transport service (Baileys) | CRITICAL | Keep. Contains tracked .env.production/.env.staging — SECURITY issue, remediate in later phase. |
| PointOfSale.Admin | Stub — contains only README.md | LOW | Investigate: placeholder or dead. |
| PointOfSale.EventBackbone.Tests | Event backbone test project (csproj, not in sln) | MEDIUM | Keep; consider adding to sln or documenting why separate. |

### 3.3 Database & migrations

| Folder | Purpose | Risk | Recommendation |
|---|---|---|---|
| migrations/ | 43 dated, named SQL migrations (WhatsApp, ServicesMarketplace, SmartOffers…) — appears to be the intentional manual-migration home | HIGH | Keep. Canonical location for the 111 loose root SQL patches. |
| DB/ | 21 tracked full database dumps named by date (11-4-2026.sql … 24-7-2026_oldData.sql), ~181 MB | MEDIUM (data value) | Backups do not belong in git. Move out of repo in cleanup phase (verify no restore scripts reference them). |
| sql/ | 1 file (phase_lcr_block_legacy_writes.sql) | LOW | Merge into migrations/ later. |
| scripts/ | 8 mixed SQL + diagnostics (marketplace-diagnostics.ps1/.sh, seed scripts, Migrations subdir) | LOW-MED | Keep, consolidate later. |
| DbPatcher/, DbQuery/, SqlRunner/ | Ad-hoc C# console utilities for applying/querying SQL | LOW | Dev tooling. Keep or fold into tools/. |

### 3.4 Documentation

| Folder | Purpose | Risk | Recommendation |
|---|---|---|---|
| docs/ | 662 md. docs/architecture (596 — certification findings, ADRs, reverse-engineering docs), docs/recovery (19), certifications (14), audits, runtime, security, fixes, operations, observability | HIGH (institutional knowledge) | Keep. Needs internal curation (§5). |
| .ai/ | Hermes canonical-architecture program output: .ai/hermes/{platform, architecture, capabilities, certification, systems, business-flows, audits, accounts, master-data, browser-knowledge, _scratch} — 128 md, 7 MB | HIGH (active program) | Keep. This is the user's active FINAL CANONICAL ARCHITECTURE initiative. |
| .ai-context/ | Authority rules for AI agents (ARCHITECTURE_TRUTH.md, CODEX_RULES.md, KNOWN_RISKS.md, PROJECT_CONTEXT.md) | HIGH (governs AI work) | Keep. |
| .ai-testing/ | AI test-strategy docs + testsprite host scripts | MEDIUM | Keep for now. |

### 3.5 Tooling / Dev environment

| Folder | Purpose | Risk | Recommendation |
|---|---|---|---|
| tools/SIDmsgRunner | Single utility runner (csproj) | LOW | Keep. |
| tmp/ | "Temporary Utilities" (its README says so): MarketplaceReadLayerVerifier, PreProdDbVerifier csprojs, start-testsprite-api.ps1 | LOW | Self-declared temporary. Cleanup candidate after verification they're not referenced. |
| testsprite_tests/ | TestSprite-generated Python E2E tests + reports (63 tracked) | MEDIUM | Keep if TestSprite still used; else archive. |
| n8n-workflows/ | 1 JSON: abandoned-cart-recovery n8n workflow | MEDIUM | Keep (runtime automation asset). |
| staging/ | Staging topology: Caddyfile, env example, README | MEDIUM | Keep (deployment config). |
| .config/ | dotnet-tools.json (dotnet local tool manifest) | MEDIUM | Keep (build tooling). |
| .github/ | EMPTY of files (no workflows found) | NONE | Investigate/remove later — no CI definitions present. |
| .vscode/, .vs/ | Editor config / VS cache (81 MB) | NONE (.vs) | .vscode keep; .vs is regenerable cache, ignored. |
| .hermes/ | 1 file: migration-audit-prompt.md | LOW | Keep or fold into .ai. |
| .agents/ | EMPTY | NONE | Dead; remove in cleanup phase. |

### 3.6 Temporary / generated (top-level)

| Folder | Purpose | Gen | Risk | Recommendation |
|---|---|---|---|---|
| .codex-temp/ | 247 MB: Codex build sandbox (build/tests with copied appsettings incl. Production), startup5 debug logs | Yes | NONE | Prime deletion candidate (untracked). |
| .codex-tmp/ | LoginProbe scratch csproj (2 files tracked) | Yes | NONE | Deletion candidate; untrack first. |
| .varcon04-build/ | 247 MB bin output of a variant build | Yes | NONE | Prime deletion candidate. |
| temp/ | Build outputs: homepage-api-build/obj, pos-test-out, stripe-provider1-tests, test-environment. temp/homepage-api-obj/** IS TRACKED (19 files) despite temp/ in .gitignore | Yes | NONE | Deletion candidate; must `git rm --cached` tracked obj files in cleanup phase. |
| tmp/ | (see 3.5 — semi-tooling) | Partly | LOW | Verify then remove. |

---

## 4b. REPOSITORY ROOT AUDIT — the 569 loose files

Breakdown by extension: 362 .md, 111 .sql, 52 .ps1, 16 .txt, 11 .js, 3 .json, 3 .cs, 2 .html, 2 .binlog, 2 .bat, 1 each .sln/.sh/.py/.gitignore(+1 stray), 1 literally named ".md".

Classification of root items:

PRODUCTION (keep at root):
- PointOfSale.sln — the solution
- global.json — SDK pin
- .gitignore

DEVELOPMENT / TOOLING (misplaced at root):
- 111 .sql one-off schema patches (add_*.sql, Add*.sql) — historical manual migrations; canonical home is migrations/. 111 are tracked.
- 52 .ps1 helper/diagnostic scripts (AF-22 backfills, reindexers, check-* scripts); 51 tracked. Belong in scripts/.
- restart-api.bat, setup-database.bat, fix-elasticsearch-directly.sh, fix_arabic.py, es_query.json, appsettings.af22.json
- One-off C# probes: CheckMongoData.cs, CheckOrderAttribution.cs, test_pg.cs (loose .cs files not in any project)

TEMPORARY / GENERATED (should not exist, several are TRACKED):
- 16 build/test logs: build-output.txt, build_error*.txt, build_output*.txt, errors.txt, errors2.txt, test_output*.txt — ALL 14 log-type files tracked in git
- api-build.binlog, marketplace-event-backbone.binlog — tracked MSBuild binary logs
- .md (zero-context filename, 4 KB) — stray

DOCUMENTATION (historical, misplaced at root): ~360 markdown reports
- Work-item phase reports: AF-12_* (28 files), AF-16*/AF-17/AF-19/AF-22/AF-24 series, ABANDONED_CART_RECOVERY_PHASE_1..3, WHATSAPP-* series, TIKTOK-*, PAYMENT_RUNTIME_*, TRACKING_PLATFORM_*
- Debug/diagnostic writeups: REDIS_STARTUP_HANGS_DEBUG_REPORT.md, MIGRATION_AUDIT_REPORT.md, various *_FIX_COMPLETE / *_SUMMARY / *_CERTIFICATION
- AF-12_DEBUG_AFFILIATE_AUTH.html, landing_page_enhanced.html
- 352 of the 362 root .md are tracked. These are session/phase artifacts: 100% relocation candidates into docs/history/ (no deletion needed in this phase).

LEGACY: WhatsAppMigration.sql, WhatsAppTemplatesMigration.sql and similar early-era scripts (superseded by migrations/2026*).

UNKNOWN: none material — every root file class was identified.

---

## 5. DOCUMENTATION CENSUS

Total project documentation: ~1,152 markdown files across three homes.

1) Root (362): historical engineering/phase reports as listed above. Status: superseded session artifacts. Many duplicate each other (e.g., AF-12 has FINAL_SUMMARY, COMPLETE_FINAL_SUMMARY, FINAL_EXECUTIVE_SUMMARY, COMPLETION_SUMMARY — 4+ overlapping "final" docs for one work item).

2) docs/ (662):
   - docs/architecture (596): certification-findings docs (admin-*-authority-certification-findings.md pattern), ADRs (ADR-001 payout, ADR-GEO-001), AFFILIATE_CHECKOUT_0..4 series, reverse-engineering outputs. Largest and most valuable set; contains both current-truth and dated certifications.
   - docs/recovery (19): current recovery-program governance (TASK-01A report etc.) — ACTIVE.
   - docs/certifications (14), docs/audits (5), docs/runtime (5), docs/operations (2), docs/fixes (2), docs/security (1), docs/issues (1); docs/observability and docs/production-readiness are EMPTY.
   - Loose in docs/ root: WhatsApp audits, PAYMENT_LIFECYCLE_ARCHITECTURE.md, FINANCIAL_ARCHITECTURE_CERTIFIED.md, STOREFRONT-ARCHITECTURE.md etc.

3) .ai/hermes (128): the FINAL CANONICAL ARCHITECTURE program (platform/, architecture/, capabilities/, certification/, systems/, business-flows/, audits/, master-data/, browser-knowledge/, _scratch/). Per project convention, implementation-backed and CURRENT. This supersedes overlapping root and docs/ claims where they conflict.

4) .ai-context (7): authority/rules docs — governance, current.
5) .ai-testing (10+ md): test strategy docs.

Duplicate/superseded classes detected:
- Multi-"final" phase reports (AF-12 example above) — superseded chains.
- Root WHATSAPP-* vs docs/WHATSAPP_* — same subject, two homes.
- Root certifications vs docs/certifications vs docs/architecture *certification-findings vs .ai/hermes/certification — four homes for certification content.
- README.md: root README known stale (per .ai-context authority).

AI-generated share: essentially all root reports, docs/architecture certification-findings, and .ai/ content are AI-produced engineering reports. User guides: none found. Deployment docs: staging/README.md only.

---

## 6. CONFIGURATION CENSUS (read-only; nothing changed)

.NET appsettings (canonical):
- PointOfSale.API/: appsettings.json, Development, Development.local, Production, Staging, Test, Test.local — the real config family (local variants are gitignore-matched via appsettings.*.local.json).
- PointOfSale/ (legacy app): appsettings.json + Development.
- launchSettings.json: PointOfSale.API/Properties, PointOfSale/Properties.

Duplicated/obsolete copies (generated, should not be authoritative):
- .codex-temp/build/tests/appsettings*.json (6 files, incl. Production copy) — sandbox copies.
- temp/pos-test-out/appsettings*.json (7) and temp/stripe-provider1-tests/appsettings*.json (7) — publish-output copies.
- appsettings.af22.json at root — one-off experiment config, obsolete.

.env files:
- Examples (fine): CommerceHub/.env.example, Storefront/.env.example + .env.staging.example, Web/.env.staging.example, Baileys/.env.example.
- RISK: PointOfSale.WhatsApp.Baileys/.env.production and .env.staging are TRACKED in git; local .env exists untracked. Potential committed secrets — flag for the security phase (rotate + untrack). NOT changed in this phase.

Docker/Kubernetes: no project Dockerfiles, no compose files, no k8s manifests (the 1 Dockerfile/compose hits are inside node_modules). Deployment appears to be Caddy-fronted (staging/Caddyfile) + manual.
Certificates: none found (.pfx/.pem/.crt/.key = 0 outside node_modules). secrets.json: none on disk (gitignored name).
Other: .config/dotnet-tools.json (dotnet tool manifest), global.json (SDK pin), n8n workflow JSON.

Production configs = PointOfSale.API/appsettings.Production.json, Baileys/.env.production, staging/Caddyfile (staging). Development = *.Development*, launchSettings, examples. Obsolete = all copies under .codex-temp/ and temp/, appsettings.af22.json.

---

## 7. TEMPORARY ARTIFACT CENSUS

| Location | Contents | Size | Tracked? |
|---|---|---|---|
| .codex-temp/ | Codex build sandbox (build/, run-all-projects/, startup5-* debug logs: api.err/out, dotnet-stack, http-runtime, redis-disabled-runtime, stack-runtime) | 247 MB | No |
| .varcon04-build/ | bin/ output of variant build | 247 MB | No |
| .codex-tmp/ | LoginProbe scratch project | 3 MB | Yes (2 files) |
| temp/ | homepage-api-build/obj, pos-test-out, stripe-provider1-tests(+obj), test-environment, applicationdbcontext-migrations.sql | large | Yes (19 obj files) despite temp/ in .gitignore |
| tmp/ | MarketplaceReadLayerVerifier, PreProdDbVerifier, start-testsprite-api.ps1, README declaring itself temporary | small | Partially |
| .vs/ | Visual Studio cache | 81 MB | No |
| bin/ + obj/ | Present in all 10 C# project dirs (correctly ignored) | dominates API 265 MB / Tests 288 MB / Infra 138 MB | No |
| node_modules/ | 5 locations: Web, Storefront, CommerceHub, WhatsApp.Baileys (+nested) | multi-GB | No |
| TestResults | PointOfSale.Tests/TestResults/CurrentRun/current.trx | — | YES (tracked) |
| Root logs | 16 build/test .txt logs, 2 .binlog | ~ | 16 tracked |
| Crash/profiling dumps | none found beyond startup5 logs in .codex-temp | — | — |
| testsprite_tests/tmp | testsprite scratch | small | partial |

Total disposable weight: ~580 MB scratch dirs + ~700 MB bin/obj/TestResults + node_modules (multi-GB). Actual source+docs are a small fraction of on-disk size.

---

## 8. GIT HYGIENE CENSUS

- .gitignore: single root file, reasonable coverage (bin/obj, dll/exe/pdb, node_modules, dist/build, .vs, trx/TestResults, .env/.env.*, *.log, appsettings.*.local.json, temp/, tmp/, data-protection-keys, Baileys auth_states, secrets.json). CRLF, emoji-sectioned.
- .gitattributes: NONE at repo level (line-ending normalization unmanaged — matters on this Windows+bash setup).
- Ignore rules added AFTER files were committed (tracked-despite-ignored):
  * temp/homepage-api-obj/** (19 compiled obj artifacts)
  * PointOfSale.Tests/TestResults/CurrentRun/current.trx
  * Baileys .env.production / .env.staging (matches .env.* rule but tracked)
- Tracked files that should not be committed:
  * 14 root build/test log .txt files
  * 3 .binlog (api-build, marketplace-event-backbone, PointOfSale.Tests/build.binlog)
  * 21 DB dumps in DB/ (~181 MB category — largest tracked payload)
  * .codex-tmp/LoginProbe (2 files)
  * Arguably: 352 root .md, 111 root .sql, 51 root .ps1 (tracked, but belong elsewhere, not deleted)
- Tracked-file distribution (top): Infrastructure 985, API 827, Web 611, Core 484, Tests 434, Storefront 341, CommerceHub 187, docs 97, testsprite 63, Baileys 54, migrations 43, root loose ~530.
- Dirty state: 1,277 modified + 1,231 untracked (543 untracked under docs/, 413 under PointOfSale.Tests). Large in-flight documentation wave not yet committed.
- Large files: DB dumps are the standout; no >20 MB single source files surfaced outside DB/ and build output.

---

## 9. REPOSITORY ARCHITECTURE MAP

```
PointOfSale (Shopixy monorepo)
│
├── SOURCE (production)
│   ├── PointOfSale.API            .NET 9 backend host        [sln]
│   ├── PointOfSale.Core           domain layer               [sln]
│   ├── PointOfSale.Infrastructure data/integrations          [sln]
│   ├── PointOfSale.Web            POS Admin frontend (React)
│   ├── PointOfSale.Storefront     customer storefront
│   ├── PointOfSale.CommerceHub    CommerceHub frontend
│   └── PointOfSale.WhatsApp.Baileys  WhatsApp transport (Node)
│
├── LEGACY / UNCLEAR
│   ├── PointOfSale                old ASP.NET app             [sln]
│   └── PointOfSale.Admin          README-only stub
│
├── TESTS
│   ├── PointOfSale.Tests                                       [sln]
│   ├── PointOfSale.EventBackbone.Tests
│   └── testsprite_tests           TestSprite E2E (Python)
│
├── DATABASE
│   ├── migrations/                canonical dated SQL migrations
│   ├── scripts/, sql/             seeds & one-offs
│   ├── DB/                        dated full dumps (backups in git!)
│   └── DbPatcher, DbQuery, SqlRunner   DB console utilities
│
├── DEPLOYMENT
│   ├── staging/                   Caddyfile + env example
│   └── n8n-workflows/             automation workflow
│
├── DOCUMENTATION
│   ├── docs/                      architecture, certifications, recovery…
│   ├── .ai/hermes/                FINAL CANONICAL ARCHITECTURE program
│   ├── .ai-context/               AI authority rules
│   ├── .ai-testing/               test strategy
│   └── (root: 362 stray reports)  ← misplaced
│
├── TOOLING
│   ├── tools/SIDmsgRunner, .config/, .vscode/, .hermes/
│   └── (root: 52 ps1 + misc scripts) ← misplaced
│
└── TEMPORARY / GENERATED
    ├── .codex-temp/, .codex-tmp/, .varcon04-build/
    ├── temp/, tmp/, .vs/
    ├── bin/, obj/, node_modules/, TestResults
    └── root build logs + binlogs
```

---

## 10. RISK ASSESSMENT (classification only — nothing deleted)

LOW RISK (safe cleanup candidates once approved):
- .codex-temp/, .varcon04-build/, .vs/ — untracked regenerable scratch (~575 MB)
- bin/, obj/, TestResults contents (regenerable)
- Root build/test logs (16 .txt) and 3 .binlog — untrack + delete
- .codex-tmp/ (untrack 2 files first)
- .agents/ (empty), stray root ".md" file
- appsettings copies under temp/ and .codex-temp/

LOW-MEDIUM RISK (relocation, not deletion):
- 362 root .md → docs/history/ (preserve git history via git mv in cleanup phase)
- 111 root .sql → migrations/legacy/ or archive (verify none referenced by scripts first)
- 52 root .ps1 + loose .bat/.sh/.py/.cs probes → scripts/ or archive
- sql/ single file → migrations/

MEDIUM RISK:
- DB/ dumps (181 MB): data has backup value; move OUT of git to backup storage; confirm no restore tooling references paths
- temp/ tracked obj files: git rm --cached (files stay on disk)
- tmp/ verifier projects: confirm unused, then remove
- testsprite_tests/: confirm whether TestSprite program still active
- Baileys tracked .env.production/.env.staging: SECURITY — rotate secrets then untrack (handle in dedicated security step, not bulk cleanup)

HIGH RISK (do NOT touch without investigation):
- PointOfSale/ legacy app — still in .sln; runtime role unverified
- PointOfSale.Admin stub — ownership unknown
- docs/architecture certification docs — some superseded by .ai/hermes but supersession map not yet built
- .ai/, .ai-context/, docs/recovery — ACTIVE governance/program content
- Anything under the 7 production source folders

UNKNOWN:
- .github/ empty (was CI ever configured?); who consumes n8n workflow JSON; whether staging/Caddyfile matches live staging.

---

## 11. CLEANUP READINESS

Ready: temp/scratch purge, log/binlog untracking, root doc relocation plan, root SQL/PS1 relocation plan, .gitignore/.gitattributes hardening.
Not ready (needs investigation first): PointOfSale legacy app disposition, PointOfSale.Admin, DB/ dump consumers, docs supersession mapping, secret rotation for Baileys env files.
Blockers: 2,508 dirty working-tree entries — commit or stash the in-flight documentation wave BEFORE any relocation, or moves will tangle with pending changes.

## 12. RECOMMENDED CLEANUP ORDER

1. Freeze: commit/stash current in-flight work (esp. 543 untracked docs/) so cleanup diffs are isolated.
2. Untracked scratch purge (zero git impact): .codex-temp, .varcon04-build, .vs, bin/obj via `dotnet clean` — recovers ~1 GB.
3. Git untrack generated: root logs/binlogs, temp/homepage-api-obj/**, TestResults .trx, .codex-tmp (git rm --cached + delete).
4. Add .gitattributes; extend .gitignore (*.binlog, .codex-*, .varcon04-build/, build*.txt patterns).
5. Root documentation relocation: 362 .md → docs/history/<topic>/ via git mv.
6. Root scripts relocation: .sql → migrations/legacy/, .ps1/.bat/.sh/.py/.cs probes → scripts/ (after grep for references).
7. DB/ dumps → external backup location; remove from git (consider history rewrite later for size — separate decision).
8. Security remediation: rotate WhatsApp secrets, untrack .env.production/.env.staging.
9. Investigations: PointOfSale legacy app, PointOfSale.Admin, .github, tmp/ verifiers, testsprite program status.
10. Documentation curation: build supersession map (root history vs docs/ vs .ai/hermes), mark canonical set.

## 13. CERTIFICATION

GRADE: B — Mostly understood. Small unknown areas remain.

Justification:
- Every top-level directory identified, classified, and sized; every root-file class identified; git hygiene issues enumerated with exact file lists; configuration landscape mapped.
- Remaining unknowns are narrow and named: (1) runtime role of legacy PointOfSale/ app, (2) PointOfSale.Admin stub purpose, (3) consumers of DB/ dumps and n8n workflow, (4) empty .github, (5) exact docs supersession chains.
- None of the unknowns block Phase-order steps 1–6; steps 7–10 require the listed investigations.

Cleanup MAY begin for LOW-risk classes immediately after the working tree is frozen (step 1). No deletion, move, rename, or git modification was performed during this census.

--- END OF CENSUS ---
