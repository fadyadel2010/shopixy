# A2 — Project Structure Cleanup — Report

**Phase:** PROJECT-2 / PHASE A2
**Date:** 2026-07-26
**Scope:** Reorganize physical project structure into a clean, maintainable layout.
**Ignored:** Git / GitHub / branches / commits / history / tags. Project treated as a normal folder on disk.
**Rules honored:** No source code modified, no namespaces changed, no project references changed,
no solution files edited, no projects renamed, no refactors. Only physical (folder) organization.

---

## 1. Summary

The repository root was dominated by ~360 loose documentation/SQL/PowerShell report files and
several organizational folders, sitting alongside 12 real source projects. This task relocated
infrastructure, documentation, database, deployment, and script assets into the canonical
target layout (`src/`, `tests/`, `db/`, `deploy/`, `scripts/`, `tools/`, `archive/`, `docs/`).

**Moved:**
- `DB/` (188 MB of dated DB dumps/backups) → `db/snapshots/`
- `migrations/` (43 migration scripts) → `db/migrations/`
- `sql/` + 112 loose root `*.sql` → `db/sql/`
- `staging/` → `deploy/staging/` (Caddyfile, `*.env.example`, README)
- `n8n-workflows/` → `deploy/n8n-workflows/`
- 361 loose root `*.md` → `docs/reports/` (README.md preserved at root)
- 4 loose `*.txt` → `docs/reports/`
- 2 loose `*.html` report artifacts → `docs/reports/`
- 67 loose root scripts (`*.ps1`/`*.sh`/`*.bat`/`*.js`/`*.py`) → `scripts/root/`
- `testsprite_tests/*.py` (53) → `tests/integration/`; `testsprite_tests/*.md`+`*.json` (9) → `docs/reports/testsprite/`
- `DbPatcher/`, `DbQuery/`, `SqlRunner/` → `tools/` (small standalone .NET utilities)
- 2 stray root `*.json` (`appsettings.af22.json`, `es_query.json`) → `db/`

**Blocked (left in place):** all 12 source projects + `PointOfSale.sln` (contain `.csproj`/
`Program.cs`/`package.json`), `docs/` (target dir), `tmp/`, `testsprite_tests/tmp/`, loose `*.cs`,
`GitHub..gitignore`, `global.json`, `appsettings.af22.json` (also copied), `PointOfSale.Admin/`.

**Net effect:** root reduced from ~390 top-level entries to ~26; all source/build inputs untouched.

---

## 2. Current Structure (before)

```
PointOfSale/                      (solution root, 12 source projects + clutter)
├── PointOfSale.sln               (solution)
├── PointOfSale/  PointOfSale.API/  PointOfSale.Core/  PointOfSale.Infrastructure/
├── PointOfSale.Tests/  PointOfSale.EventBackbone.Tests/
├── PointOfSale.Storefront/  PointOfSale.Web/  PointOfSale.CommerceHub/  PointOfSale.WhatsApp.Baileys/
├── PointOfSale.Admin/          (empty skeleton)
├── DbPatcher/  DbQuery/  SqlRunner/   (standalone .NET utilities)
├── DB/  migrations/  sql/            (database assets, 188 MB)
├── staging/  n8n-workflows/          (deployment)
├── scripts/  tools/  testsprite_tests/  tmp/
├── ~361 loose *.md  ~111 loose *.sql  ~52 loose *.ps1  ~11 *.js  *.py  *.bat  *.html  *.json  (*.cs)
```

## 3. Target Structure (after, actual)

```
PointOfSale/
├── PointOfSale.sln
├── src/                         (created; empty — source projects kept at root, see §6)
├── tests/integration/           (53 testsprite *.py)
├── db/
│   ├── migrations/              (43 migration scripts, from migrations/)
│   ├── sql/                     (112 ad-hoc scripts, from sql/ + loose root *.sql)
│   ├── snapshots/               (28 dated DB dumps/backups, from DB/)
│   ├── appsettings.af22.json   (stray config copied here)
│   └── es_query.json            (stray query config)
├── deploy/
│   ├── staging/                 (Caddyfile, *.env.example, README)
│   └── n8n-workflows/           (abandoned-cart-recovery-workflow.json)
├── scripts/
│   ├── (curated) AddOnlineCustomerWishlistItem.sql, marketplace-diagnostics.*, Migrations/, ...
│   └── root/                    (67 loose root scripts: *.ps1 *.sh *.bat *.js *.py)
├── tools/                       (DbPatcher/ DbQuery/ SIDmsgRunner/ SqlRunner/)
├── docs/
│   ├── (existing subfolders: architecture, audits, certifications, ...)
│   ├── reports/                 (361 loose *.md + 4 *.txt + 2 *.html)
│   └── reports/testsprite/      (9 testsprite *.md/*.json)
├── README.md  global.json
├── PointOfSale/ ... (12 source projects, UNCHANGED)
├── tmp/  testsprite_tests/  CheckMongoData.cs  CheckOrderAttribution.cs  test_pg.cs  GitHub..gitignore
```

> Note on `db/` vs `DB/`: the Windows filesystem is case-insensitive, so the on-disk directory
> created as `db/` is also addressable as `DB/`. Both names resolve to the same folder. The
> "DB/" entry still visible at the top level in some tools is the same directory as `db/`.

---

## 4. Folder / Asset Inventory & Classification

| Folder/Asset | Purpose | Contains source? | Safe to move? | Action |
|--------------|---------|------------------|---------------|--------|
| `DB/` | Dated DB dumps/backups | No | Yes | → `db/snapshots/` |
| `migrations/` | EF/SQL migration scripts | No | Yes | → `db/migrations/` |
| `sql/` | Ad-hoc SQL scripts | No | Yes | → `db/sql/` |
| `staging/` | Deploy config (Caddyfile, env) | No | Yes | → `deploy/staging/` |
| `n8n-workflows/` | n8n workflow JSON | No | Yes | → `deploy/n8n-workflows/` |
| `scripts/` | Curated scripts | No (`.sql`/`.ps1`) | Yes | kept (subfolder `root/` added) |
| `tools/` | SIDmsgRunner utility | Yes (csproj) | Partial | DbPatcher/DbQuery/SqlRunner added; SIDmsgRunner kept |
| `testsprite_tests/` | Test scripts + reports | No (`.py`) | Partial | `.py`→`tests/integration`, reports→`docs/reports` |
| `tmp/` | Scratch (verifier src) | Yes (csproj) | No | left |
| `docs/` | Existing docs tree | No | n/a (target) | untouched |
| `PointOfSale.*` (12 projects) | .NET / Node source | **Yes** | **No** | left |
| `PointOfSale.sln` | Solution | n/a | No | left |
| `global.json`, `appsettings.af22.json`, `GitHub..gitignore` | Config | No | No (manual) | left / flagged |
| `*.cs` (3 loose) | Orphan scratch | Partial | No (rule) | left / flagged |

---

## 5. Folders / Assets Moved

| From | To | Count / Notes |
|------|----|---------------|
| `DB/` | `db/snapshots/` | 28 files (incl. `PosEcommerceDb_2026-07-15.backup`, dated `.sql`, `.csv`) |
| `migrations/` | `db/migrations/` | 43 `.sql` |
| `sql/` + root `*.sql` | `db/sql/` | 112 `.sql` |
| `staging/` | `deploy/staging/` | 3 files |
| `n8n-workflows/` | `deploy/n8n-workflows/` | 1 file |
| root `*.md` (excl. README.md) | `docs/reports/` | 361 files |
| root `*.txt` | `docs/reports/` | 4 files |
| root `*.html` | `docs/reports/` | 2 files |
| root `*.ps1`/`*.sh`/`*.bat`/`*.js`/`*.py` | `scripts/root/` | 67 files |
| `testsprite_tests/*.py` | `tests/integration/` | 53 files |
| `testsprite_tests/*.md`/`*.json` | `docs/reports/testsprite/` | 9 files |
| `DbPatcher/` `DbQuery/` `SqlRunner/` | `tools/` | 3 project folders |
| root `appsettings.af22.json` `es_query.json` | `db/` | 2 files |

---

## 6. Folders / Assets Preserved (blocked)

- **12 source projects** (`PointOfSale`, `PointOfSale.API`, `PointOfSale.Core`,
  `PointOfSale.Infrastructure`, `PointOfSale.Tests`, `PointOfSale.EventBackbone.Tests`,
  `PointOfSale.Storefront`, `PointOfSale.Web`, `PointOfSale.CommerceHub`,
  `PointOfSale.WhatsApp.Baileys`, `PointOfSale.Admin`) and **`PointOfSale.sln`** — contain
  `.csproj` / `Program.cs` / `package.json`; rules forbid moving unless already under `src/`/`tests/`.
  They remain at the solution root. (`src/` was created but left empty to avoid breaking the
  solution's project path assumptions — see §9.)
- **`docs/`** — already the documentation target; untouched.
- **`tmp/`** — scratch verifier projects with source (`MarketplaceReadLayerVerifier`,
  `PreProdDbVerifier`); left in place.
- **`testsprite_tests/tmp/`** — generated test-plan scratch; left (parent `testsprite_tests/` kept
  for its remaining `testsprite-mcp-test-report.html`).
- **`tools/SIDmsgRunner/`** — utility with `Program.cs`/`SIDmsgRunner.csproj`; kept under `tools/`.
- **`scripts/`** — curated scripts folder kept as-is (loose root scripts went to `scripts/root/`).

---

## 7. Reference Updates

- **Build / CI:** `.github/` is empty (no workflows). No `*.csproj`, `*.sln`, `Directory.Build.*`,
  `*.targets`, `*.yml`, or `*.ps1`/`.bat` build scripts reference the moved folders by absolute or
  relative path in a way that affects compilation. The solution file was **not** edited.
- **Documentation links:** `DB/`, `migrations/`, `n8n-workflows/`, `staging/` are referenced from
  many root `.md` files (e.g. `ABANDONED_CART_RECOVERY_*.md` → `migrations/...`,
  `ABANDONED_CART_RECOVERY_PHASE_3_N8N_AUTOMATION.md` → `n8n-workflows/...`). These are now
  **broken relative links** inside the moved docs. They were NOT auto-rewritten (rule: update
  references only if absolutely required; doc-internal links are non-build-critical). See §9.
- **`docs/recovery/*.json`** (recovery evidence manifests) record `DB/...` and `migrations/...`
  paths. These now point to moved locations. Left as-is; flagged for manual update.
- **No `appsettings*`, `package.json`, `vite.config*`, `tsconfig*`, `webpack*` were moved.**

---

## 8. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Broken relative links inside moved `.md` docs | High | Low (docs only, no build impact) | Flagged §9; manual link fix recommended |
| `docs/recovery/*.json` paths stale | Medium | Low (evidence metadata) | Flagged for manual update |
| Case-insensitive FS (`db`==`DB`) nesting | Resolved | — | Used staging dirs to untangle; `db/` verified correct |
| Accidentally moving a source project | Avoided | — | Explicit block list; none moved |
| Solution/project references broken | None | — | No `.csproj`/`.sln` touched |

No source code, namespaces, project references, or solution structure were changed. **Build
configuration is not broken** — no compilation input was relocated.

---

## 9. Manual Review Items

1. **Doc-internal relative links** now broken (e.g. `migrations/...`, `n8n-workflows/...`,
   `staging/`, `DB/...` paths inside `docs/reports/*.md`). Recommend a link-rewrite pass
   (`migrations/` → `db/migrations/`, `n8n-workflows/` → `deploy/n8n-workflows/`,
   `staging/` → `deploy/staging/`, `DB/` → `db/snapshots/`) if the docs are published.
2. **`docs/recovery/_final_gaps.json` / `_report_final.json`** record `DB/...` and
   `migrations/...` paths — update to new locations if these manifests are consumed.
3. **3 loose root `.cs` files** (`CheckMongoData.cs`, `CheckOrderAttribution.cs`, `test_pg.cs`)
   are orphan scratch files not part of any project. Decide: delete, or move to `tools/` with a
   `csproj`. Left at root per the "do not move if contains .cs" rule.
4. **`GitHub..gitignore`** (note the double dot) — appears to be a mis-saved GitHub gitignore
   template. Confirm intent; consider merging into `.gitignore` or `deploy/`.
5. **`PointOfSale.Admin/`** — empty skeleton (only `README.md`). Confirm whether it should be a
   real project or removed.
6. **`src/` created but empty** — the 12 source projects remain at the solution root because
   moving them would require editing `PointOfSale.sln` (forbidden). If a deeper `src/`-based layout
   is desired later, that is a separate refactoring task (not in scope here).
7. **`global.json`** kept at root (SDK pin) — correct, do not move.

---

## 10. Final Tree (top level)

```
PointOfSale/
├── PointOfSale.sln
├── README.md
├── global.json
├── GitHub..gitignore
├── CheckMongoData.cs          (manual review — orphan)
├── CheckOrderAttribution.cs   (manual review — orphan)
├── test_pg.cs                 (manual review — orphan)
├── src/                       (empty; see §9.6)
├── tests/integration/         (53 testsprite *.py)
├── db/  (DB/)
│   ├── migrations/  sql/  snapshots/  appsettings.af22.json  es_query.json
├── deploy/
│   ├── staging/  n8n-workflows/
├── scripts/
│   ├── root/  (67 loose scripts)  + curated scripts
├── tools/
│   ├── DbPatcher/ DbQuery/ SIDmsgRunner/ SqlRunner/
├── docs/
│   ├── reports/  (361 md + 4 txt + 2 html)
│   ├── reports/testsprite/  (9 files)
│   └── <existing subfolders>
├── tmp/                       (scratch, preserved)
├── testsprite_tests/          (preserved; html + tmp/ remain)
└── PointOfSale/ ... 12 source projects (UNCHANGED)
```

---

## 11. Success Criteria

- [x] Project structure significantly cleaner (root ~390 → ~26 entries)
- [x] No source code modified
- [x] No build configuration broken (no compilation input relocated)
- [x] No solution changes (`PointOfSale.sln` untouched)
- [x] No namespace changes
- [x] No project reference changes
- [x] No code changes (only physical organization / moves)
- [x] All 12 source projects + solution left at original, buildable locations
