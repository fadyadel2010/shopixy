# PHASE A2.5 — PROJECT ROOT AUDIT

Date: 2026-07-26
Branch: recovery/3-greenfield-baseline
Mode: READ-ONLY AUDIT. No files were modified, moved, renamed, or deleted.
Method: every claim below verified against the LIVE filesystem and git index
(`ls`, `find`, `git ls-files`, `git check-ignore -v`, `git status --porcelain`,
file-content heads). Prior A1/A2 markdown was NOT used as evidence.

---

## 1. Current Root Tree (live, verified)

```
PointOfSale/
├── .agents/                      (EMPTY)
├── .ai/
├── .ai-context/
├── .ai-testing/
├── .config/                      (dotnet-tools.json only)
├── .git/
├── .github/                      (workflows/ EMPTY — zero files)
├── .gitignore
├── .hermes/
├── .md                           <- loose file, extension-only name
├── .vscode/                      (settings.json)
├── CheckMongoData.cs             <- loose source file
├── CheckOrderAttribution.cs      <- loose source file
├── DB/
├── GitHub..gitignore             <- loose duplicate gitignore
├── PointOfSale/                  (Desktop/legacy csproj, in sln)
├── PointOfSale.Admin/            (DEPRECATED — README only)
├── PointOfSale.API/              (in sln)
├── PointOfSale.CommerceHub/      (frontend, NOT in sln)
├── PointOfSale.Core/             (in sln)
├── PointOfSale.EventBackbone.Tests/ (csproj exists, NOT in sln)
├── PointOfSale.Infrastructure/   (in sln)
├── PointOfSale.Storefront/       (frontend, NOT in sln)
├── PointOfSale.Tests/            (in sln)
├── PointOfSale.Web/              (frontend, NOT in sln)
├── PointOfSale.WhatsApp.Baileys/ (Node service, NOT in sln)
├── PointOfSale.sln
├── README.md
├── deploy/
├── docs/
├── global.json
├── scripts/
├── src/                          (EMPTY)
├── test_pg.cs                    <- loose source file
├── tests/                        (integration/ python tests)
├── testsprite_tests/
├── tmp/                          (git-ignored)
└── tools/
```

Root loose files: 9 (`.gitignore`, `.md`, `CheckMongoData.cs`,
`CheckOrderAttribution.cs`, `GitHub..gitignore`, `global.json`,
`PointOfSale.sln`, `README.md`, `test_pg.cs`).

Solution membership (verified in PointOfSale.sln — 5 projects only):
PointOfSale, PointOfSale.Core, PointOfSale.Infrastructure, PointOfSale.API,
PointOfSale.Tests. `PointOfSale.EventBackbone.Tests.csproj` exists on disk but
is NOT in the solution.

---

## 2. Inventory Table

| Item | Type | Purpose | Src? | Build? | Runtime? | Tooling? | IDE? | Referenced? | Relocate-safe? | Delete-safe? | Manual review? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| .gitignore | config | ignore rules | no | no | no | yes(git) | no | git | no | no | no |
| global.json | config | pins SDK 9.0.308 | no | YES | no | yes | yes | dotnet | no | NO | no |
| PointOfSale.sln | solution | 5-project sln | no | YES | no | yes | yes | dotnet/VS | no | NO | no |
| README.md | doc | repo readme (stale per .ai-context) | no | no | no | no | no | humans | no | no | content only |
| .md | doc | PAYMENT-RUNTIME-4 evidence doc, misnamed | no | no | no | no | no | untracked, none | YES | after move | no |
| CheckMongoData.cs | src (script) | one-off Mongo probe (tracked) | yes | NO (no root csproj) | no | ad-hoc | no | none found | YES | REVIEW | yes |
| CheckOrderAttribution.cs | src (script) | one-off Npgsql probe (tracked) | yes | NO | no | ad-hoc | no | none | YES | REVIEW | yes |
| test_pg.cs | src (script) | pg connectivity probe (tracked) | yes | NO | no | ad-hoc | no | none | YES | REVIEW | yes |
| GitHub..gitignore | config (dup) | second gitignore, git does NOT read this name | no | no | no | NO | no | none | n/a | YES | no |
| .agents/ | dir | EMPTY | no | no | no | no | no | none | n/a | YES | no |
| .ai/ | dir | AI canonical-architecture docs initiative | no | no | no | AI workflow | no | user workflow | no | no | no |
| .ai-context/ | dir | authoritative AI ruleset (7 tracked files) | no | no | no | AI workflow | no | yes (authority) | no | NO | no |
| .ai-testing/ | dir | test strategy docs + testsprite runbooks (21 tracked) | no | no | no | QA workflow | no | yes | maybe->docs | no | yes |
| .config/ | dir | dotnet-tools.json (dotnet local tools) | no | yes | no | YES | no | dotnet tool restore | NO | NO | no |
| .github/ | dir | workflows/ EMPTY — zero files anywhere | no | no | no | CI(none) | no | none | n/a | YES (or populate) | yes |
| .hermes/ | dir | agent session artifacts (untracked, not ignored) | no | no | no | agent | no | none | n/a | ignore-rule | no |
| .vscode/ | dir | settings.json | no | no | no | no | YES | VS Code | no | no | no |
| Item | Type | Purpose | Src? | Build? | Runtime? | Tooling? | IDE? | Referenced? | Relocate-safe? | Delete-safe? | Manual review? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| DB/ | dir | migrations, sql, snapshots, appsettings.af22.json, es_query.json (21 tracked) | sql | no | ops | YES | no | ops scripts | rename-case only | no | yes (secrets in appsettings.af22.json?) |
| deploy/ | dir | n8n-workflows, staging deploy assets | no | no | deploy | YES | no | ops | no | no | no |
| docs/ | dir | large doc tree (97 tracked + 546 untracked) | no | no | no | no | no | yes | no | no | curation later |
| PointOfSale/ | dir | legacy/desktop csproj — IN SLN | YES | YES | ? | no | yes | sln | no | NO | role review |
| PointOfSale.Admin/ | dir | DEPRECATED (README says migrated to Web, Dec 2024). Only README.md remains | no | no | no | no | no | README only | YES->archive | REVIEW | yes |
| PointOfSale.API/ | dir | main backend — IN SLN | YES | YES | YES | no | yes | yes | NO | NO | no |
| PointOfSale.CommerceHub/ | dir | frontend app (components.json, dist, index.html) | YES | frontend build | YES | no | no | yes | no | NO | dist/ is generated |
| PointOfSale.Core/ | dir | domain — IN SLN | YES | YES | YES | no | yes | yes | NO | NO | no |
| PointOfSale.EventBackbone.Tests/ | dir | test csproj NOT in sln; Marketplace/ subdir EMPTY | YES | orphan | no | no | no | not in sln | no | no | yes (add to sln or archive) |
| PointOfSale.Infrastructure/ | dir | EF/infra — IN SLN. Contains EMPTY stray subdir `PointOfSale.API/` | YES | YES | YES | no | yes | yes | NO | NO | stray empty subdir |
| PointOfSale.Storefront/ | dir | frontend app (dist, index.html, package.json) | YES | frontend | YES | no | no | yes | no | NO | dist/ generated |
| PointOfSale.Tests/ | dir | tests — IN SLN. 414 untracked entries; `Temp/` subdir EMPTY | YES | YES | no | no | yes | sln | NO | NO | untracked flood review |
| PointOfSale.Web/ | dir | main frontend (convert.cjs, dist) | YES | frontend | YES | no | no | yes | NO | NO | dist/ generated, convert*.cjs loose |
| PointOfSale.WhatsApp.Baileys/ | dir | Node WhatsApp transport service (54 tracked) | YES | node | YES | no | no | yes | NO | NO | no |
| scripts/ | dir | SQL patches + ps1/sh diagnostics + Migrations/ + root/ | sql/ps1 | no | ops | YES | no | ops | no | no | overlaps DB/ |
| src/ | dir | EMPTY | no | no | no | no | no | none | n/a | YES (or adopt as target layout) | yes |
| tests/ | dir | integration/ python (testsprite) incl. duplicate-named files | YES(py) | no | no | QA | no | testsprite | no | no | dedupe review |
| testsprite_tests/ | dir | testsprite artifacts + HTML report + tmp/ (63 tracked) | py | no | no | QA | no | testsprite | yes->tests/ | partial | yes |
| tmp/ | dir | git-ignored scratch (2 verifier csprojs, ps1, README) | YES | no | no | scratch | no | ignored | n/a | yes (ignored) | no |
| tools/ | dir | DbPatcher, DbQuery, SIDmsgRunner, SqlRunner (4 csprojs, not in sln) | YES | standalone | no | YES | no | ops use | no | no | no |

Note — GHOST INDEX ENTRIES (tracked in git index but MISSING on disk;
they show as the 832 unstaged `D` entries): `migrations/` (43), `temp/` (19),
`SqlRunner/`, `DbQuery/`, `DbPatcher/` (2 each), `.codex-tmp/` (2),
`staging/` (3), `WhatsAppTemplatesMigration.sql` (1). These are prior root
items already physically removed/relocated by A2 but whose deletions are not
yet recorded in the index. They are index state, not root residents.

---

## 3. Classification

### KEEP (root residents, justified)
| Item | Justification |
|---|---|
| .gitignore | git requires it at root |
| global.json | pins .NET SDK 9.0.308 — build critical |
| PointOfSale.sln | solution entry point |
| README.md | standard root doc (content is stale — update, don't move) |
| .config/ | dotnet local tool manifest; dotnet looks for it at root |
| .vscode/ | IDE settings, conventional at root |
| .ai/, .ai-context/ | active AI-governance authority for this repo |
| .github/ | conventional location IF populated; currently empty (see REVIEW) |
| .hermes/ | agent workspace; should be gitignored, stays at root |
| PointOfSale, .API, .Core, .Infrastructure, .Tests | sln members |
| PointOfSale.CommerceHub, .Storefront, .Web, .WhatsApp.Baileys | active runtime apps (until a src/ migration is executed) |
| docs/, DB/, deploy/, scripts/, tools/, tests/ | legitimate root categories |
| tmp/ | git-ignored scratch, harmless; conventionally acceptable |

### MOVE
| Item | Target | Justification |
|---|---|---|
| .md | docs/certifications/PAYMENT-RUNTIME-4-EVIDENCE.md | it is a PAYMENT-RUNTIME-4 evidence doc misnamed as bare extension; untracked; zero references |
| CheckMongoData.cs | tools/probes/ (or tools/DbQuery) | one-off Mongo diagnostic; not part of any csproj; tracked so move via git mv next phase |
| CheckOrderAttribution.cs | tools/probes/ | same class: ad-hoc Npgsql probe |
| test_pg.cs | tools/probes/ | same class: pg connectivity probe |
| testsprite_tests/ | tests/testsprite/ | consolidate all test assets under tests/; removes second top-level test dir |
| .ai-testing/ | docs/testing/ or keep | optional consolidation; low priority (REVIEW-lite) |

### DELETE
| Item | Justification |
|---|---|
| GitHub..gitignore | duplicate gitignore under a name git never reads ("GitHub..gitignore"); real .gitignore exists; tracked -> git rm next phase |
| .agents/ | verified empty, untracked, no references |
| src/ | verified empty. DELETE only if the target architecture (P1-3) does NOT adopt a src/ layout; otherwise it is a placeholder — confirm before deleting |
| PointOfSale.Infrastructure/PointOfSale.API/ | empty stray nested dir (likely bad paste) |
| PointOfSale.Tests/Temp/ | empty temp dir inside test project |
| PointOfSale.EventBackbone.Tests/Marketplace/ | empty subdir |
| docs/production-readiness/ | empty dir under docs |
| .github/workflows/ | empty — delete .github entirely OR populate with CI (see REVIEW) |

### REVIEW (needs a human decision)
| Item | Question |
|---|---|
| PointOfSale.Admin/ | deprecated Dec 2024, only README remains. Options: move README to docs/legacy/ and delete dir, or keep as tombstone. Recommend: relocate README, remove dir |
| PointOfSale.EventBackbone.Tests/ | orphan csproj not in sln. Add to sln (if tests are live) or archive |
| PointOfSale/ (legacy project) | in sln but role unclear vs API/Web split — confirm still built/shipped |
| .github/ | empty = no CI at all. Decide: add CI or remove dir |
| src/ | empty placeholder vs deletion — depends on approved target layout |
| DB/appsettings.af22.json | environment config inside DB/ — check for credentials before it travels anywhere |
| Ghost index entries (migrations/, temp/, .codex-tmp/, DbPatcher/ etc.) | stage the deletions (git add -A on those paths) in the next commit phase to make index match disk |
| scripts/ vs DB/sql overlap | both hold SQL patches; pick one home (recommend DB/sql for schema patches, scripts/ for operational ps1/sh) |
| tests/integration duplicate files | e.g. SI-DMSG-01_..._Replay_Convergence.py exists twice with `_` vs `___` naming; dedupe |
| dist/ in Web/Storefront/CommerceHub | generated output inside project dirs — confirm gitignore coverage next phase |
---

## 4. Empty Folders (verified with `find -type d -empty`, depth 2)

| Path | Verdict |
|---|---|
| .agents/ | DELETE |
| .github/workflows/ | DELETE or populate with CI |
| docs/production-readiness/ | DELETE |
| src/ | DELETE or reserve per target architecture |
| PointOfSale.Infrastructure/PointOfSale.API/ | DELETE (stray) |
| PointOfSale.Tests/Temp/ | DELETE |
| PointOfSale.EventBackbone.Tests/Marketplace/ | DELETE |

## 5. Duplicate Content

1. `GitHub..gitignore` vs `.gitignore` — two ignore files; only `.gitignore` is functional. Duplicate is dead weight.
2. `scripts/*.sql` + `scripts/Migrations/` vs `DB/sql` + `DB/migrations` — two homes for SQL patch/migration assets.
3. `tests/` vs `testsprite_tests/` (which itself contains a `tmp/`) — two top-level test-artifact dirs for the same testsprite tooling.
4. `tests/integration/` duplicate scripts: `SI-DMSG-01_..._Replay_Convergence.py` appears twice (single vs triple underscore variants); same for GUID-prefixed vs plain-name variants of SI-HP-01 / SI-RDS-01 / SI-REP-01.
5. Ghost tracked copies: index still tracks `SqlRunner/`, `DbQuery/`, `DbPatcher/` at ROOT while the live files exist under `tools/` — duplication at index level until deletions are staged.

## 6. Legacy Artifacts

- PointOfSale.Admin/ — explicitly deprecated (README, Dec 2024), migrated into PointOfSale.Web.
- Loose probe files CheckMongoData.cs / CheckOrderAttribution.cs / test_pg.cs — Apr 2026 one-off diagnostics, compiled by nothing.
- PointOfSale.EventBackbone.Tests — orphan test project outside the sln.
- Ghost index entries (migrations/, temp/, .codex-tmp/, staging/, WhatsAppTemplatesMigration.sql) — remnants of pre-A2 layout.
- README.md content is stale relative to .ai-context (known project fact) — legacy content, not legacy file.

## 7. Naming Issues

- `.md` — a file named ONLY by its extension; invisible intent, sorts oddly, easy to overwrite.
- `GitHub..gitignore` — double dot, non-functional name.
- Case: live dir is `DB/` and git index agrees (`git ls-files` shows only `DB/`); no db/DB case conflict exists today — but on case-insensitive NTFS, avoid ever introducing `db/`. Convention mismatch: all other root dirs are lowercase (docs, deploy, scripts, tools, tests) — `DB/` is the outlier; consider lowercasing to `db/` in a dedicated two-step rename (git mv DB tmpname && git mv tmpname db) if standardizing.
- tests/integration duplicate names differ only by underscore count — fragile.
- `PointOfSale.WhatsApp.Baileys` double-dot project naming is intentional (.NET-style) — fine.

## 8. Structural Issues

1. INDEX/DISK DIVERGENCE (dominant issue): 832 unstaged deletions + 351 staged deletions + 1225 untracked. The A2 physical reorganization was never committed. Until this is frozen, every subsequent audit's git numbers are noise.
2. 5-project sln vs 11 top-level project dirs — sln does not describe the system (4 frontend/node apps + EventBackbone.Tests + 4 tools csprojs + 2 tmp csprojs live outside it).
3. No CI: .github contains zero files.
4. Generated output (`dist/`) inside CommerceHub/Storefront/Web project dirs; verify ignore coverage.
5. 546 untracked files under docs/ and 414 under PointOfSale.Tests — large unfrozen surface.
6. One untracked doc with a non-ASCII em-dash filename: `docs/architecture/PCC — Official-Provider-Documentation-Authority-Audit.md` — works, but shell-hostile.

## 9. Recommendations (for next execution phase — nothing done now)

R1. FREEZE FIRST: commit/stash the current tree (stage the 832 ghost deletions so index matches disk) before any move/delete.
R2. Delete GitHub..gitignore (git rm).
R3. Move .md -> docs/certifications/PAYMENT-RUNTIME-4-EVIDENCE.md.
R4. git mv the 3 loose .cs probes -> tools/probes/ (with a short README).
R5. Remove empty dirs (.agents, docs/production-readiness, stray nested empties); decide src/ and .github/ per R8/R9.
R6. Resolve PointOfSale.Admin: README -> docs/legacy/, remove dir.
R7. Decide EventBackbone.Tests: add to sln or archive.
R8. Decide src/ layout adoption (P1-3 target architecture) — keep placeholder only if migration is approved.
R9. Add minimal CI to .github/workflows or delete .github.
R10. Consolidate testsprite_tests/ under tests/; dedupe tests/integration variants.
R11. Unify SQL homes: schema/migrations under DB/ (or db/), operational scripts under scripts/.
R12. Review DB/appsettings.af22.json for credentials before relocation; treat as security check.
R13. Update README.md content to match .ai-context reality.

## 10. Proposed Final Root Layout

```
PointOfSale/
├── .ai/  .ai-context/  .config/  .github/(with CI)  .vscode/  .hermes/(ignored)
├── .gitignore  global.json  PointOfSale.sln  README.md (rewritten)
├── PointOfSale/  PointOfSale.API/  PointOfSale.Core/
├── PointOfSale.Infrastructure/  PointOfSale.Tests/
├── PointOfSale.CommerceHub/  PointOfSale.Storefront/  PointOfSale.Web/
├── PointOfSale.WhatsApp.Baileys/
├── db/            (migrations, sql, snapshots)
├── deploy/
├── docs/          (incl. certifications/, legacy/, testing/)
├── scripts/       (operational ps1/sh only)
├── tests/         (integration/ + testsprite/)
├── tools/         (DbPatcher, DbQuery, SIDmsgRunner, SqlRunner, probes/)
└── tmp/           (ignored scratch)
```
Removed vs today: .agents, .ai-testing (->docs/testing), .md, Check*.cs, test_pg.cs,
GitHub..gitignore, PointOfSale.Admin, PointOfSale.EventBackbone.Tests (into sln or archive),
src (unless adopted), testsprite_tests.

## 11. Risk Assessment

| Action | Risk | Notes |
|---|---|---|
| Delete GitHub..gitignore | LOW | non-functional name, real .gitignore intact |
| Move .md | LOW | untracked, unreferenced |
| Move loose .cs probes | LOW | no csproj compiles them (verified: no root csproj; sln has 5 projects elsewhere) |
| Delete empty dirs | LOW | verified empty |
| Remove PointOfSale.Admin | LOW-MED | deprecated per its own README; keep README in docs/legacy |
| Stage ghost deletions / freeze | MED | large commit; must be its own atomic step |
| DB->db case rename | MED | case-insensitive FS; needs two-step git mv; check script references first |
| src/ decision, sln membership changes | MED | depends on approved target architecture |
| testsprite/tests consolidation | MED | testsprite MCP config may hardcode paths — grep before moving |
| dist/ handling | LOW-MED | verify build pipelines regenerate |

## 12. Executive Summary

The root is close to healthy: 9 loose files, of which 4 are junk-class
(.md, GitHub..gitignore, and 3 orphan probe .cs files) and 5 are legitimate
(.gitignore, global.json, sln, README, plus tracked configs). The dominant
problem is NOT the root layout — it is that the A2 reorganization was never
frozen in git: 832 tracked paths are deleted on disk but still in the index
(old root migrations/, temp/, tool dirs), 351 deletions are staged, and 1225
files are untracked. Second-order findings: an empty .github (no CI), a
5-project sln that omits 6 active top-level apps/projects, a deprecated
PointOfSale.Admin tombstone, duplicated SQL/test homes (scripts vs DB,
tests vs testsprite_tests), and 7 empty directories. All items are classified
KEEP/MOVE/DELETE/REVIEW above; recommended execution order is R1 (freeze)
first, then the LOW-risk moves/deletes, then the MED-risk structural
decisions. No files were modified during this audit.
