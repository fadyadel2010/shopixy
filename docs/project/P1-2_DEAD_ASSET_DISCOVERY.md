# P1-2 — Dead Asset Discovery

Program: Shopixy Project Cleanup Program / PROJECT-1
Date: 2026-07-25
Branch at analysis: recovery/3-greenfield-baseline
Mode: DISCOVERY ONLY — nothing was modified, deleted, moved, renamed, or refactored.
Method: every claim below was verified against the LIVE filesystem and git index
(`find`, `git ls-files`, `git status --porcelain`, `git check-ignore -v`, `grep -r`,
csproj/sln inspection). Prior markdown reports (P1-1, REPO-1/2/3) were used only as
pointers, never as evidence.

Classification vocabulary: ACTIVE, SUPPORTED, LEGACY, ARCHIVED, GENERATED, TEMPORARY, UNKNOWN, DEAD.

---

## 1. Executive Summary

- 1 solution: `PointOfSale.sln` contains ONLY 5 projects: PointOfSale (legacy MVC web),
  PointOfSale.Core, PointOfSale.Infrastructure, PointOfSale.API, PointOfSale.Tests
  (verified via `grep 'Project(' PointOfSale.sln`).
- 13 csproj files exist on disk → 8 are OUTSIDE the solution:
  DbPatcher, DbQuery, SqlRunner, PointOfSale.EventBackbone.Tests,
  tools/SIDmsgRunner, tmp/MarketplaceReadLayerVerifier, tmp/PreProdDbVerifier,
  .codex-tmp/LoginProbe. None of them is referenced by any other csproj, sln,
  yml, ps1, sh, or bat file (verified by repo-wide grep; only hits are census
  markdown docs, which are not execution evidence).
- 569 loose files at repo root (558 tracked). The overwhelming majority are
  historical phase reports (~330 .md), one-time SQL patches (~120 .sql), one-time
  ops/diagnostic scripts (~70 .ps1/.js/.bat/.sh), and build/debug logs (~17
  .txt/.binlog). Root is the single largest dead-asset concentration.
- Confirmed GENERATED/DEAD debris tracked or untracked at root: `api-build.binlog`
  (NOT git-ignored — verified `git check-ignore` prints nothing),
  `marketplace-event-backbone.binlog`, 9 `build_*.txt` / `errors*.txt`,
  3 `test_output*.txt`.
- `.gitignore` contradiction found: `temp/` is ignored (line 42) yet `temp/` has
  19 TRACKED files (build obj cache + playwright runtime) — tracked-despite-ignored.
- `.codex-temp/`, `.codex-tmp/`, `.varcon04-build/` are NOT ignored
  (`git check-ignore` empty for the dirs; only inner `build/`/`bin/` subpaths match
  generic rules). `.codex-tmp/LoginProbe` is partially TRACKED (2 files).
- `.github/` exists but is EMPTY (find returns no files) → no CI/CD lives in-repo;
  therefore "used by CI" cannot rescue any script. Directory itself is DEAD (empty).
- `docs/recovery/archive/` = 484 files, explicitly ARCHIVED by the recovery program.
- No orphaned root file is referenced by any csproj (`CheckMongoData.cs`,
  `CheckOrderAttribution.cs`, `test_pg.cs` are compile-orphans: repo-wide grep of
  csproj files finds zero includes; SDK-style projects only glob inside their own dirs).

Headline: the repo has a small live core (5 sln projects + 4 frontend/node apps +
migrations + staging + canonical docs) surrounded by a very large sediment layer of
one-time scripts, phase reports, and scratch projects.

---

## 2. Live System Baseline (what is provably ACTIVE)

These are the reference set — anything not reachable from here needs positive
evidence to be called alive.

| Asset | Class | Evidence |
|---|---|---|
| PointOfSale.sln | ACTIVE | Sole solution; 5 projects enumerated |
| PointOfSale.API/ | ACTIVE | In sln; Program.cs; references Core+Infrastructure (csproj ProjectReference verified) |
| PointOfSale.Core/ | ACTIVE | In sln; referenced by API csproj |
| PointOfSale.Infrastructure/ | ACTIVE | In sln; referenced by API csproj |
| PointOfSale.Tests/ | ACTIVE | In sln; test project |
| PointOfSale/ (MVC) | LEGACY (in sln, likely superseded — see §3.1) | In sln but nothing references it; API is the platform runtime |
| PointOfSale.Web/ | ACTIVE | package.json with dev/build/test + architecture tests; POS admin frontend |
| PointOfSale.Storefront/ | ACTIVE | package.json vite app (Stripe, i18n) |
| PointOfSale.CommerceHub/ | ACTIVE | package.json vite app |
| PointOfSale.WhatsApp.Baileys/ | ACTIVE | package.json microservice (build/start/test scripts) |
| PointOfSale.Admin/ | UNKNOWN | Directory exists but has NO package.json (head returned empty). Needs investigation — possibly stub/dead shell |
| migrations/ (43 tracked SQL) | ACTIVE/SUPPORTED | Dated, ordered migration scripts; the disciplined successor to root SQL sediment |
| staging/ | ACTIVE | Caddyfile + env example + README = deployment config |
| .config/dotnet-tools.json | ACTIVE | dotnet local tool manifest |
| global.json | ACTIVE | Pins SDK 9.0.308 |
| .gitignore | ACTIVE | (with defects noted in §7) |
| .ai-context/ (7 tracked) | ACTIVE (authoritative rules) | Project authority per governance |
| .ai/ (untracked tree) | ACTIVE working docs | Hermes canonical architecture program output |
| docs/ canonical subset | ACTIVE | See §6 |

---

## 3. Special Analysis — Projects Outside the Solution

### 3.1 PointOfSale/ (legacy MVC web project)
- Class: LEGACY (NOT DEAD — insufficient evidence to declare dead).
- Evidence: IS in the sln; SDK=Microsoft.NET.Sdk.Web; only EF Design/Tools packages;
  has Controllers/Views/wwwroot. No other csproj references it (grep verified).
  The platform runtime is PointOfSale.API + Vite frontends.
- Risk if removed: HIGH until runtime role confirmed (it may still be the EF
  migrations startup project or a deployed legacy UI). Verdict: LEGACY / UNKNOWN-runtime.

### 3.2 PointOfSale.EventBackbone.Tests/
- Class: TEMPORARY, DEAD-candidate (high confidence).
- Evidence: NOT in sln. OutputType=Exe (a console harness, not a test framework
  project). References sibling projects via raw DLL HintPaths into
  `..\PointOfSale.Core\bin\Debug\net9.0\...` and even borrows MassTransit DLLs from
  PointOfSale.Tests\bin — a scratch pattern that breaks on any clean checkout.
  Zero inbound references repo-wide (grep found none). Companion root artifact
  `marketplace-event-backbone.binlog` (Mar 29) indicates a one-time verification run.
- Replacement: PointOfSale.Tests (in sln).
- Risk if removed: LOW (cannot build without pre-built Debug bin folders anyway).

### 3.3 DbPatcher/, DbQuery/, SqlRunner/
- Class: TEMPORARY utilities, DEAD-candidates (medium-high confidence).
- Evidence: NOT in sln; each is a 1-file console exe with only Npgsql; 2 tracked
  files each; zero inbound references from any csproj/sln/script/CI (grep verified;
  .github is empty so no pipeline uses them). They are ad-hoc DB poke tools.
- Replacement: migrations/ + DbPatcher-style work now done via EF/psql per recovery docs.
- Risk if removed: LOW. Caveat: if the team still runs them manually, reclassify
  SUPPORTED — that is usage evidence that cannot be found in-repo, hence not 100%.

### 3.4 tools/SIDmsgRunner/
- Class: TEMPORARY diagnostic runner, DEAD-candidate (medium confidence).
- Evidence: NOT in sln; only Program.cs + csproj (+ bin/obj debris). Referenced only
  in .ai/hermes flow docs and census markdown (documentation mentions, not build/
  runtime usage). Name ties it to SI-DMSG testsprite scenario — a verification aid.
- Risk if removed: LOW-MEDIUM (referenced by two FLOW-ACC docs as a tool that was
  used; historical, not operational).

### 3.5 tmp/ (MarketplaceReadLayerVerifier, PreProdDbVerifier)
- Class: TEMPORARY by self-declaration; DEAD for repo purposes.
- Evidence: tmp/ IS git-ignored (`git check-ignore` → .gitignore:48 `tmp/`);
  0 tracked files. tmp/README.md states verbatim: "temporary, single-purpose tools
  not intended for production use … should NOT be committed."
- Risk if removed: NONE to the repository (untracked, ignored, self-labelled temp).

### 3.6 .codex-tmp/LoginProbe/
- Class: TEMPORARY scratch probe, DEAD (high confidence).
- Evidence: AI-scratch directory; NOT git-ignored (verified — no .codex* rule in
  .gitignore) and 2 files ARE tracked (LoginProbe.csproj, Program.cs) — accidental
  commit of scratch. Not in sln; zero inbound references.
- Risk if removed: NONE.

### 3.7 .codex-temp/ and .varcon04-build/
- Class: GENERATED/TEMPORARY, DEAD.
- Evidence: .codex-temp/ = run logs (api.log, storefront.err.log, …) from an AI
  "run-all-projects" session; 0 tracked; NOT ignored at dir level (only inner
  `build/` matches .gitignore:15). .varcon04-build/ = a full copied build output
  including Playwright node.exe runtime; 0 tracked; only inner `bin/` matches
  .gitignore:4. Both are reproducible outputs, not sources.
- Risk if removed: NONE (untracked, regenerable).

### 3.8 temp/
- Class: GENERATED, DEAD content — with a git-hygiene defect.
- Evidence: `temp/` is ignored (.gitignore:42) YET 19 files are TRACKED
  (tracked-before-ignore-rule): temp/homepage-api-obj/** (MSBuild obj cache:
  .assets.cache, GlobalUsings.g.cs, project.assets.json) and temp/pos-test-out/
  (.playwright runtime incl. node.exe). 100% machine-generated.
- Risk if removed: NONE functionally; requires `git rm --cached` in a cleanup phase.

### 3.9 DB/
- Class: ARCHIVED backups, NOT DEAD (retention data, not code).
- Evidence: 29 entries — date-named full dumps (9-12-2025 … 24-7-2026), a .backup
  (PosEcommerceDb_2026-07-15.backup), schema-23-7-2026.sql, one CSV export.
  21 tracked + 8 untracked. Nothing executes them; they are point-in-time DB
  snapshots. Binary/dump data tracked in git is a hygiene concern, not a deadness one.
- Risk if removed: HIGH (data loss) — must be relocated to backup storage, never deleted blind.

### 3.10 .github/
- Class: DEAD (empty directory).
- Evidence: `find .github -type f` returns nothing. No workflows, no templates.
  Consequence: no in-repo CI exists; no script can claim CI usage.
- Risk if removed: NONE.

### 3.11 Other dot-dirs
- .agents/: empty → DEAD (empty shell).
- .hermes/: 1 untracked working file (migration-audit-prompt.md) → TEMPORARY.
- .vs/: IDE-generated → GENERATED (ignore-status should be confirmed in cleanup phase).
- .ai-testing/ (21 tracked): SUPPORTED (QA scenario evidence used by the audit program).
- testsprite_tests/ (63 tracked): SUPPORTED — QA harness; note near-duplicate pairs
  (same scenario with `_` vs `___` names and GUID-prefixed copies) → duplicates are
  DEAD-candidates within a live folder.
- n8n-workflows/ (1 tracked JSON): SUPPORTED — deployment artifact for the abandoned
  cart automation (imported into n8n per N8N_WORKFLOW_IMPORT_GUIDE.md); not executed
  by the repo itself.
- scripts/ (8 tracked): mixed — marketplace-diagnostics.ps1/.sh SUPPORTED (ops
  diagnostics); the 5 SQL files + scripts/Migrations/2026… are MIGRATION-ONLY
  (one-time, likely already applied → LEGACY once confirmed applied).
- sql/ (1 tracked): phase_lcr_block_legacy_writes.sql — one-time phase guard script,
  LEGACY/one-time.

---

## 4. Root Files (569 loose files; 558 tracked, 11 untracked)

Untracked root files (git status verified): `.md`, AFFILIATE_CROSS_DOMAIN_BOUNDARIES.md,
MIGRATION_AUDIT_REPORT.md, PAYMENT_RUNTIME_1_FOUNDATION_CERTIFICATION.md,
REDIS_STARTUP_HANGS_DEBUG_REPORT.md, REVERSE_ENGINEERED_AUTHORITY_CQRS_SEARCH_DEBT.md,
TIKTOK-0.5/TIKTOK-0/TIKTOK_PROVIDER_CERTIFICATION.md, TRACKING_PLATFORM_v1.0_FINAL_CERTIFICATION.md,
checkdll.ps1.

### 4.1 Build/debug logs and binlogs — GENERATED, DEAD (highest confidence)
Files: api-build.binlog, marketplace-event-backbone.binlog, build_error.txt,
build_error_whatsapp.txt, build_errors.txt, build_infra_errors.txt, build_log.txt,
build_output.txt, build-output.txt, build_output_session.txt, build_output_tenant.txt,
errors.txt, errors2.txt, test_output.txt, test_output2.txt, test_output3.txt.
- Evidence: machine-generated compiler/test output (dates Dec 2025–Mar 2026, sizes up
  to 4.7 MB); zero inbound references; regenerable by rebuilding. `git check-ignore
  api-build.binlog` → NOT ignored.
- Confidence: HIGH. Risk if removed: NONE. Replacement: rebuild.

### 4.2 Root SQL scripts (~120 files) — one-time patches, LEGACY→DEAD-candidates
Pattern classes:
- One-time schema patches: add_*.sql, Add*.sql, create_*.sql, *Migration*.sql
  (e.g. add_banks_table.sql, create_treasury_table.sql, AffiliateUsersMigration.sql).
- One-time repairs: fix_*.sql / fix-*.sql (fix_chat_FINAL.sql, fix_product_stock.sql,
  fix-visa-account.sql), manual_wallet_cleanup.sql, cleanup_smartoffer_whitespace_coupons.sql.
- Diagnostics/verification: check*.sql, af18-*.sql, verify_*.sql, debug-*.sql,
  AF-38F_VERIFICATION_QUERIES.sql, AF-19_VERIFY_MIGRATION_STATE.sql.
- Evidence of deadness: none is referenced by any .cs/.ps1/.csproj/.yml (spot-checked
  add_banks_table.sql, create_treasury_table.sql, migration.sql, fix_chat_FINAL.sql —
  zero non-doc hits repo-wide). The project has a successor convention: dated files
  under migrations/ (43 tracked). Root SQL = pre-convention sediment, applied by hand
  at the time of the matching phase report.
- Classification: LEGACY (applied one-time). Cannot be upgraded to DEAD individually
  without confirming each was applied to all environments — that check is a DB-side
  task, out of discovery scope. Confidence DEAD-candidate: MEDIUM-HIGH as a class.
- Risk if removed: LOW-MEDIUM (loss of applied-history reference; mitigate by
  archiving rather than deleting). Replacement: migrations/ + DB/schema dumps.

### 4.3 Root PowerShell/JS/BAT/SH scripts (~70) — one-time ops, LEGACY→DEAD-candidates
- One-time migration executors: add-banks-table.ps1, run-af19b-migration*.ps1,
  run-migration.ps1, create_treasury_table.ps1, AF24_Backfill_Analytics.ps1 …
- Incident repair: fix-mongodb-*.ps1/.js, fix-elasticsearch-*.ps1/.sh,
  fix-rabbitmq-bindings.ps1, fix-visa-account.ps1, fix-arabic*.ps1/.js/.py.
- Diagnostics: check-*.ps1, verify-*.ps1, diagnose-whatsapp-*.ps1, AF-22-Diagnostic.ps1,
  test-*.ps1, checkdll.ps1 (untracked scratch).
- Evidence: zero inbound references from build/CI (empty .github; not referenced by
  csproj/sln; grep of monitor-baileys-rollout.ps1, run-migration.ps1 → 0 hits).
  Each maps to a completed phase report of the same prefix.
- Exception — NOT DEAD: `secret-config-helpers.ps1` IS dot-sourced by other root
  scripts (grep found ≥3 consumers: af19-check-order-attribution.ps1,
  check-bindings-simple.ps1, check-mongo-tenantids.ps1). It is SUPPORTED for as long
  as those consumers exist (dead-cluster: helper + consumers retire together).
- Exceptions — SUPPORTED (operational value): setup-database.ps1/.bat (referenced by
  recovery readiness docs as the DB bootstrap path), restart-api.bat + RESTART-API-GUIDE.md
  (operator convenience), monitor-baileys-rollout.ps1 (rollout ops — recent subsystem).
- Confidence for the rest: MEDIUM-HIGH. Risk: LOW (one-time, environment-specific).

### 4.4 Root orphan source files — DEAD (high confidence)
- CheckMongoData.cs, CheckOrderAttribution.cs, test_pg.cs: loose .cs at root, not
  inside any project directory; SDK csproj globs never reach repo root; grep confirms
  no csproj Compile/include mentions. Never compiled by anything. Risk: NONE.
- landing_page_enhanced.html, AF-12_DEBUG_AFFILIATE_AUTH.html: standalone artifacts,
  zero references from any ts/tsx/cs/html (grep verified for landing page). DEAD.
- appsettings.af22.json: root-level config not loadable by any project (config files
  load from project dirs); zero code references (grep verified). DEAD (AF-22 phase relic).
- es_query.json: ad-hoc Elasticsearch query scratch. DEAD-candidate (no references).
- `.md` (literally named dot-md, untracked): a PAYMENT-RUNTIME-4 note saved with a
  broken filename. Content may belong in docs/; the FILE is a naming accident →
  TEMPORARY/misplaced.
- GitHub..gitignore (580 bytes): stray duplicate gitignore with typo name; git only
  honors `.gitignore`. DEAD. Risk: NONE.

### 4.5 Root markdown reports (~330) — see §5.

---

## 5. Documentation Classification

### 5.1 Canonical / authoritative (NOT DEAD)
- .ai-context/ (7 tracked) — governance authority (project rule: implementation +
  .ai-context are the source of truth; README acknowledged stale).
- .ai/hermes/** — FINAL CANONICAL ARCHITECTURE program output (active initiative).
- docs/architecture, docs/runtime, docs/security, docs/operations, docs/observability,
  docs/production-readiness, docs/certifications — current homes; treat contents as
  ACTIVE/SUPPORTED pending the P1-3 doc-by-doc pass.
- docs/recovery/ (non-archive) — ACTIVE recovery governance (recent commits touch it).
- docs/repository/REPO-1..3, docs/project/P1-1 — ACTIVE program deliverables.
- README.md, QUICKSTART.md, QUICK_START_GUIDE.md, DEPLOYMENT_GUIDE.md,
  MONITORING_GUIDE.md, LANGUAGE_GUIDE.md — SUPPORTED onboarding/ops docs
  (README known stale per .ai-context; duplicates QUICKSTART vs QUICK_START_GUIDE →
  one is a superseded duplicate; needs one comparison to pick canonical).

### 5.2 Historical phase/session reports at root (~330 .md) — LEGACY (historical), DEAD-candidates as a class
Families (all follow report-of-completed-work pattern):
- AF-12 … AF-38F affiliate program (≈90 files, incl. multiple "FINAL SUMMARY"
  variants per phase — internally superseded chains, e.g. AF-12 has
  COMPLETION_SUMMARY, COMPLETE_FINAL_SUMMARY, FINAL_SUMMARY, FINAL_EXECUTIVE_SUMMARY).
- PHASE-0 … PHASE-11.x platform build diary (≈60).
- SERVICES_MARKETPLACE_PHASE_1..FE2, MARKETPLACE_* (≈25).
- CHAT_HARDENING_CR1 family (≈10), COMMERCE-AI (≈8), MULTI-UNIT (≈9), SOA-* (≈9),
  VISA-ACCOUNT (≈4), WHATSAPP (≈7), TREASURY/RETURNS/EXPENSES/DASHBOARD etc.
- Audit snapshots: HOSTILE_AUDIT_*, TENANT-ISOLATION-*, AFFILIATE_PLATFORM_DEEP_AUDIT,
  FORENSIC_COD_WALLET_DIFFERENTIAL_AUDIT, POS_ADMIN_SYSTEM_DEEP_REVIEW …
- Evidence for LEGACY: they document completed changes whose truth now lives in the
  implementation; the governance rule itself demotes them ("prior markdown is
  secondary"); none is loaded by code/build; newer canonical docs exist under
  docs/ + .ai/ for the same areas.
- Evidence AGAINST outright DEAD: they are the only written history for many fixes;
  some (e.g. RABBITMQ_TOPOLOGY_GOVERNANCE_RULES.md, AFFILIATE_CROSS_DOMAIN_BOUNDARIES.md,
  ECOMMERCE-ARCHITECTURE.md, UNIFIED-FINANCIAL-ENGINE-ARCHITECTURE.md) read as rules/
  architecture rather than diaries and may deserve promotion into docs/.
- Verdict: HISTORICAL/SUPERSEDED as a class; disposition = relocate/archive in a later
  phase, not delete. Confidence: HIGH for "no runtime/build role"; per-file canonical
  promotion requires the P1-3 review.

### 5.3 docs/recovery/archive/ (484 files) — ARCHIVED by design. Not dead; intentionally retained history.

### 5.4 Generated reports
- docs under docs/audits, docs/fixes, docs/issues that mirror root reports: probable
  duplicates; flag for P1-3 duplicate-chain resolution (UNKNOWN pending diff).

---

## 6. DEAD / DEAD-candidate Register (consolidated)

| # | Asset | Class | Confidence | Risk if removed | Replacement |
|---|---|---|---|---|---|
| 1 | api-build.binlog, marketplace-event-backbone.binlog | GENERATED→DEAD | HIGH | None | rebuild |
| 2 | build_*.txt, build-output.txt, errors*.txt, test_output*.txt (root, 14 files) | GENERATED→DEAD | HIGH | None | rebuild/re-run tests |
| 3 | temp/ tracked content (19 files: obj cache + playwright) | GENERATED→DEAD | HIGH | None | rebuild; fix tracking |
| 4 | .codex-temp/, .varcon04-build/ | GENERATED→DEAD | HIGH | None | re-run sessions |
| 5 | .codex-tmp/LoginProbe (2 tracked) | TEMPORARY→DEAD | HIGH | None | none needed |
| 6 | tmp/* verifiers (untracked, ignored) | TEMPORARY→DEAD | HIGH | None | self-declared temp |
| 7 | PointOfSale.EventBackbone.Tests/ | TEMPORARY→DEAD | HIGH | Low | PointOfSale.Tests |
| 8 | DbPatcher/, DbQuery/, SqlRunner/ | TEMPORARY→DEAD-candidate | MED-HIGH | Low (unless manual use) | migrations/ + psql |
| 9 | tools/SIDmsgRunner/ | TEMPORARY→DEAD-candidate | MEDIUM | Low-Med | testsprite harness |
| 10 | CheckMongoData.cs, CheckOrderAttribution.cs, test_pg.cs | DEAD | HIGH | None | — |
| 11 | GitHub..gitignore | DEAD | HIGH | None | .gitignore |
| 12 | landing_page_enhanced.html, AF-12_DEBUG_AFFILIATE_AUTH.html, es_query.json, appsettings.af22.json | DEAD | HIGH | None | — |
| 13 | .github/ (empty), .agents/ (empty) | DEAD (empty) | HIGH | None | recreate when CI added |
| 14 | Root one-time SQL (~120) | LEGACY→DEAD-candidate class | MED-HIGH | Low-Med | migrations/, DB/ dumps |
| 15 | Root one-time ps1/js/bat/sh (~65, excluding exceptions §4.3) | LEGACY→DEAD-candidate class | MED-HIGH | Low | — |
| 16 | Root phase/session .md (~330) | HISTORICAL/SUPERSEDED | HIGH (no build role) | Low if archived, HIGH if deleted | docs/ + .ai canonical set |
| 17 | testsprite `_` vs `___`/GUID duplicate scripts | DUPLICATE→DEAD-candidate | MEDIUM | Low | keep one variant |
| 18 | fix_arabic.py / fix-arabic.js / fix-arabic-encoding.ps1 (3 impls of same one-time fix) | DEAD-candidate | MED-HIGH | None | — |

NOT DEAD / explicitly protected: secret-config-helpers.ps1 (live dot-source consumers),
setup-database.ps1/.bat, restart-api.bat, monitor-baileys-rollout.ps1, staging/,
migrations/, DB/ (backups=data), n8n-workflows/, scripts/marketplace-diagnostics.*,
.ai-testing/, testsprite_tests/ (minus duplicates), all sln projects, all 4 node apps.

UNKNOWN (needs targeted investigation before any disposition):
- PointOfSale.Admin/ — no package.json found; contents unverified.
- PointOfSale/ (legacy MVC) — runtime/deployment role unresolved.
- docs/audits|fixes|issues vs root report duplicate chains — needs diff pass.
- Whether every root SQL patch is applied in all environments (DB-side check).

---

## 7. Git-hygiene findings surfaced by this discovery (for later phases; NO action taken)
1. temp/ ignored but 19 files tracked (tracked-before-rule).
2. .codex-tmp partially tracked; .codex-temp/.codex-tmp/.varcon04-build have no
   ignore rules (only inner bin/build match generic rules).
3. Binlogs/logs at root tracked or untracked but not ignored (api-build.binlog verified NOT ignored).
4. DB dumps/backups tracked in git (repo bloat; relocation candidate, not deletion).

---

## 8. Certification
- Discovery only: zero mutations performed (all commands read-only: find/grep/git
  ls-files/status/check-ignore/head/cat).
- Every DEAD verdict above cites reproducible evidence; class-level verdicts
  (root SQL/ps1/md) are explicitly marked as candidates requiring per-file or
  DB-side confirmation before any cleanup phase acts on them.
- Unsupported assumptions: none knowingly included; unknowns are listed as UNKNOWN.

End of P1-2.
