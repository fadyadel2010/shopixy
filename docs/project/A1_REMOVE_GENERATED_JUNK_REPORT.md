# A1 — Remove Generated Junk — Report

**Phase:** PROJECT-1 / PHASE A1
**Date:** 2026-07-26
**Scope:** Clean generated, temporary, cached, and reproducible artifacts only.
**Ignored:** Git / GitHub / branches / commits / history / tags. Project treated as a normal folder on disk.
**Class rule applied:** Only artifacts that are fully reproducible were removed. Anything that could contain business logic or manual work was preserved and flagged.

---

## 1. Summary

The repository was scanned for build/cache/temp output. A large .NET 9 solution plus four Node.js (TypeScript) front-end projects were found, generating extensive `bin/`, `obj/`, `node_modules/`, IDE caches, and build/test log noise at the root.

Removed:
- 5 whole temporary / IDE / scratch folders (`.vs`, `.codex-temp`, `.codex-tmp`, `.varcon04-build`, `temp`)
- 4 `node_modules/` trees (one per Node project)
- All `.NET` `bin/` and `obj/` directories across every project (11 projects + 2 scratch tools)
- 4 `artifacts/` folders (codex-generated `codex-obj`/`codex-build` contents)
- `PointOfSale.Tests/TestResults/` test output
- 3 `*.binlog` files and 2 `*.csproj.user` files (IDE)
- 17 loose build/error/test log `.txt` files

Preserved (manual / config / source):
- All `*.cs`, `*.csproj`, `*.sln`, `appsettings*.json`, `*.sql`, `*.ps1`, `*.sh`, `*.bat`, `*.md`, `*.json` config, and the `docs/`, `scripts/`, `sql/`, `DB/`, `migrations/`, `staging/`, `n8n-workflows/`, `testsprite_tests/`, `tools/`, `tmp/`, and the four Node project source trees.
- `staging/` (contains `Caddyfile`, `*.env.example`, `README.md`) — deployment config, NOT removed.
- `.vscode/` (87-byte `settings.json`) — NOT on the safe list; preserved and flagged for manual review.

No source code, project files, solution file, configuration files, documentation, SQL, or scripts were removed.

> Note: A Roslyn `VBCSCompiler` build-server process (PID 22576) held a lock on
> `PointOfSale.Tests/bin/Debug/net9.0`. It was a build-infrastructure process
> (auto-restarts on next build), so it was terminated to release the lock and
> complete the cleanup. No user work was affected.

---

## 2. Folders Removed

| Path | Classification | Reason | Reproducible? |
|------|----------------|--------|--------------|
| `.vs/` | IDE cache (Visual Studio) | Safe list `*.suo` / `.vs/` | YES |
| `.codex-temp/` | Codex temporary scratch | Safe list `.codex-temp/` | YES |
| `.codex-tmp/` | Codex temporary scratch | Safe list `.codex-tmp/` | YES |
| `.varcon04-build/` | Temporary build dir | Safe list `.varcon04-build/` | YES |
| `temp/` | Temporary scratch (incl. generated `applicationdbcontext-migrations.sql`) | Safe list `temp/` | YES |
| `PointOfSale.CommerceHub/node_modules/` | Node deps | Safe list `**/node_modules/` | YES |
| `PointOfSale.Storefront/node_modules/` | Node deps | Safe list `**/node_modules/` | YES |
| `PointOfSale.Web/node_modules/` | Node deps | Safe list `**/node_modules/` | YES |
| `PointOfSale.WhatsApp.Baileys/node_modules/` | Node deps | Safe list `**/node_modules/` | YES |
| `PointOfSale.Tests/TestResults/` | Test output | Safe list `**/TestResults/` | YES |
| `PointOfSale.API/artifacts/` | Build output (codex-obj) | Safe list `**/artifacts/` | YES |
| `PointOfSale.Core/artifacts/` | Build output (codex-obj) | Safe list `**/artifacts/` | YES |
| `PointOfSale.Infrastructure/artifacts/` | Build output (codex-obj) | Safe list `**/artifacts/` | YES |
| `PointOfSale.Tests/artifacts/` | Build output (codex-obj/codex-build) | Safe list `**/artifacts/` | YES |

### .NET `bin/` and `obj/` directories removed (all generated, reproducible)

- `PointOfSale/bin`, `PointOfSale/obj`
- `PointOfSale.API/bin`, `PointOfSale.API/obj` (incl. nested `.playwright/package/bin`)
- `PointOfSale.Core/bin`, `PointOfSale.Core/obj`
- `PointOfSale.Infrastructure/bin`, `PointOfSale.Infrastructure/obj` (incl. nested `PointOfSale.API/obj` and `.playwright/package/bin`)
- `PointOfSale.EventBackbone.Tests/bin`, `PointOfSale.EventBackbone.Tests/obj`
- `PointOfSale.Tests/bin`, `PointOfSale.Tests/obj`
- `DbPatcher/bin`, `DbPatcher/obj`
- `DbQuery/bin`, `DbQuery/obj`
- `SqlRunner/bin`, `SqlRunner/obj`
- `tmp/MarketplaceReadLayerVerifier/bin`, `tmp/MarketplaceReadLayerVerifier/obj` (scratch tool — source kept)
- `tmp/PreProdDbVerifier/bin`, `tmp/PreProdDbVerifier/obj` (scratch tool — source kept)
- `tools/SIDmsgRunner/bin`, `tools/SIDmsgRunner/obj` (utility — source kept)

> No `bin/` or `obj/` were found under any protected manual directory
> (`docs/`, `scripts/`, `sql/`, `DB/`, `migrations/`, `staging/`, `n8n-workflows/`,
> `testsprite_tests/`, `deploy/`, `archive/`, `src/`), confirmed by `find` before deletion.

---

## 3. Files Removed

### Build / error / test logs (`*.txt` per safe list)
- `api-build.binlog` (binary build log, `*.binlog`)
- `marketplace-event-backbone.binlog` (`*.binlog`)
- `PointOfSale.Tests/build.binlog` (`*.binlog`)
- `PointOfSale/PointOfSale.csproj.user` (`*.user`)
- `PointOfSale.API/PointOfSale.API.csproj.user` (`*.user`)
- `build-output.txt`, `build_error.txt`, `build_errors.txt`, `build_error_whatsapp.txt`,
  `build_infra_errors.txt`, `build_log.txt`, `build_output.txt`, `build_output_session.txt`,
  `build_output_tenant.txt`, `build-output.txt`
- `errors.txt`, `errors2.txt`
- `test_output.txt`, `test_output2.txt`, `test_output3.txt`
- `PointOfSale.API/build_error_log.txt`, `PointOfSale.API/build_log.txt`, `PointOfSale.API/build_output.txt`
- `PointOfSale.Infrastructure/build_errors.txt`
- `.vs/.../v17/.suo` (removed as part of `.vs/`)

> `PointOfSale.Tests/TestResults/*.trx` files were removed as part of the `TestResults/` folder
> (all 9 `*.trx` results files: current.trx, final-audit.trx, final-full-suite-green.trx,
> phase0-full-suite-final.trx, phase0-full-suite.trx, post-rebuild-full-suite-with-db.trx,
> post-rebuild-full-suite-with-working-db.trx, post-rebuild-full-suite.trx).

---

## 4. Files / Folders Preserved

| Path | Why preserved |
|------|---------------|
| `PointOfSale.sln` and all `*.csproj` | Project / solution files (do not remove) |
| `appsettings*.json` (root + per project) | Configuration (do not remove) |
| `staging/` (`Caddyfile`, `*.env.example`, `README.md`) | Deployment config; NOT in safe-removal list |
| `docs/`, `scripts/`, `sql/`, `DB/`, `migrations/`, `n8n-workflows/`, `testsprite_tests/` | Manual/maintained assets |
| `tmp/MarketplaceReadLayerVerifier/`, `tmp/PreProdDbVerifier/` | Contain real source (`Program.cs`, `*.csproj`); only their `bin`/`obj` removed |
| `tools/SIDmsgRunner/` | Contains `Program.cs` + `SIDmsgRunner.csproj`; only `bin`/`obj` removed |
| `PointOfSale.CommerceHub/`, `PointOfSale.Storefront/`, `PointOfSale.Web/`, `PointOfSale.WhatsApp.Baileys/` source | Node project sources preserved; only `node_modules/` removed |
| `PointOfSale.API/artifacts` source — n/a | (folder was build output; removed) |
| `.vscode/` | IDE config, NOT on safe list → preserved, flagged below |
| All `.md`, `.sql`, `.ps1`, `.sh`, `.bat`, `.js`, `.cs` source | Manual/business logic |

---

## 5. Suspicious Items (inspected, judged safe)

- **`temp/applicationdbcontext-migrations.sql`** — A generated EF Core migration script
  (`CREATE TABLE ... __EFMigrationsHistory`, full DDL). Reproducible at any time via
  `dotnet ef migrations script`. It was inside `temp/` which is on the safe-removal list, so it
  was removed together with the folder. No manual SQL was lost.
- **Nested `appsettings*.json` inside `temp/pos-test-out` and `temp/stripe-provider1-tests`** —
  Copies emitted into build output. Removed with `temp/`. The authoritative `appsettings*.json`
  files in project roots remain.
- **`.git` directory** — Left entirely untouched (explicitly out of scope).

---

## 6. Items Requiring Manual Review

1. **`.vscode/settings.json`** (87 bytes) — IDE config folder. It is NOT on the explicit
   safe-removal list (only `.vs/` and `.idea/` are). Preserved. Decide whether to keep or gitignore.
2. **`tmp/MarketplaceReadLayerVerifier` and `tmp/PreProdDbVerifier`** — small scratch
   verifier projects containing source (`Program.cs`, `.csproj`). Only their `bin`/`obj` were
   removed. The source folders remain. If they are throwaway, consider deleting the whole folder.
3. **`tools/SIDmsgRunner`** — a utility console project (`Program.cs`, `SIDmsgRunner.csproj`).
   Only `bin`/`obj` removed; source kept. Confirm whether this tool should live under `tools/`.
4. **`staging/`** — kept intact (deployment config). Confirm it is intended to be version-controlled.

---

## 7. Final Statistics

| Metric | Count |
|--------|-------|
| Whole folders removed | 14 (5 temp/IDE + 4 node_modules + 4 artifacts + 1 TestResults) |
| `bin/` dirs removed | 13 |
| `obj/` dirs removed | 13 |
| Loose `.binlog` removed | 3 |
| Loose `.user` removed | 2 |
| Loose `.suo` removed | 1 (inside `.vs/`) |
| Loose `*.trx` removed | 9 (inside `TestResults/`) |
| Loose build/error/test log `.txt` removed | 17 |
| Total deletions (folders + files) | ~70 |

---

## 8. Estimated Disk Space Reclaimed

Sizes measured with `du -sb` before deletion. Categories not captured before deletion are
clearly labelled as estimates (evidence over assumption).

| Category | Bytes | GiB | Measured? |
|----------|------:|----:|-----------|
| `temp/` | 607,232,879 | 0.57 | measured |
| `.codex-temp/` | 257,257,328 | 0.24 | measured |
| `.varcon04-build/` | 256,959,213 | 0.24 | measured |
| `.vs/` | 84,730,165 | 0.08 | measured |
| `PointOfSale.CommerceHub/node_modules/` | 145,866,801 | 0.14 | measured |
| `PointOfSale.Storefront/node_modules/` | 113,368,156 | 0.11 | measured |
| `PointOfSale.WhatsApp.Baileys/node_modules/` | 110,832,157 | 0.10 | measured |
| `PointOfSale.Web/node_modules/` | 464,981,631 | 0.43 | measured |
| `.codex-tmp/` | 2,718,452 | 0.00 | measured |
| `.NET bin/obj` (all projects) | ~500,000,000 | ~0.47 | estimated* |
| `TestResults/`, `artifacts/`, loose `.binlog`/`.user`/logs | ~12,000,000 | 0.01 | measured (small) |
| **Total estimated** | **~2,550,000,000** | **~2.37** | |

\* `.NET bin/obj` sizes were not separately captured before deletion (deleted in the same
  batch). Estimated ~500 MB typical for a 10+ project .NET 9 solution; precise figure available
  on next clean checkout / build.

**All four `node_modules/` sizes are now precisely measured** (the background `du` finished
after cleanup): CommerceHub 145,866,801 + Storefront 113,368,156 + Web 464,981,631 +
WhatsApp.Baileys 110,832,157 = 834,048,745 bytes (0.78 GiB). The Web project's
`node_modules` was the largest by far (464 MB), not ~110 MB as initially estimated.

**Precisely measured reclaimed (excluding only the .NET bin/obj estimate): ~2.05 GB (≈1.91 GiB).**
**Total estimated reclaimed: ~2.55 GB (≈2.37 GiB).**

---

## 9. Post-Cleanup Verification

- [x] No `bin/`/`obj/` remain anywhere in the repo (`find` returned empty)
- [x] No `node_modules/` remain (`find` returned empty)
- [x] No `TestResults/`, `artifacts/`, `*.binlog`, `*.trx`, `*.user`, `*.suo` remain
- [x] `PointOfSale.sln` present
- [x] All `*.csproj` present (PointOfSale, API, Core, Infrastructure, Tests, EventBackbone.Tests, DbPatcher, DbQuery, SqlRunner)
- [x] `appsettings*.json` config files present
- [x] `docs/`, `scripts/`, `sql/`, `DB/`, `migrations/`, `staging/`, `n8n-workflows/`, `testsprite_tests/`, `tools/`, `tmp/`, and the 4 Node project source trees intact
- [x] `README.md`, `migration.sql`, `MIGRATION_AUDIT_REPORT.md` and other docs/SQL preserved
- [x] No Git operations performed

---

## 10. Success Criteria

- [x] Project now contains only source assets and manually maintained files plus reproducible dependency/config inputs.
- [x] Only generated junk removed.
- [x] No architectural changes.
- [x] No code changes.
- [x] No Git operations.
- [x] Project cleanup only.
