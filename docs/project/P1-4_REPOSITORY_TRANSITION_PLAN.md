# P1-4 — Repository Transition Plan (Shopixy / PointOfSale)

> **Program:** Shopixy Project Cleanup Program → PROJECT-1 → P1-4
> **Phase type:** PLANNING ONLY. No files moved, renamed, deleted, generated, staged, or committed by this document.
> **Inputs (authoritative):** `docs/project/P1-1_PROJECT_CENSUS.md`, `docs/project/P1-2_DEAD_ASSET_DISCOVERY.md`, `docs/project/P1-3_REPOSITORY_TARGET_ARCHITECTURE.md`
> **Verification:** Every blocker and number below was re-confirmed against the LIVE git index / filesystem on branch `recovery/3-greenfield-baseline` (not read from prior markdown). Where this plan CONTRADICTS or EXTENDS the P1-x docs, the live evidence is cited inline.
> **Repo root:** `C:\Users\fadya\source\repos\PointOfSale` · **Remote:** `origin → github.com/bebokoko548/SHO_PI_XY.git`

---

## 0. Purpose

This is the execution strategy that turns the P1-3 blueprint into an actual repository migration. It defines: the blockers that must clear first, an evidence-derived sequence of independently-executable and independently-reversible **waves**, validation gates, rollback for every wave, freeze rules, communication, success metrics, and out-of-scope boundaries.

Design constraints (non-negotiable):
- **Rollback** — every wave has a documented, tested undo.
- **Incremental** — each wave is executable and shippable on its own.
- **Validation after every step** — nothing proceeds on an unverified wave.
- **Minimal developer disruption** — freeze windows are short and announced.
- **Repository integrity** — history preserved (`git mv`, not delete+add), no force-push to shared branches.

A new engineering team should be able to execute this plan top-to-bottom without asking questions.

---

## 1. Live State Snapshot (re-verified for P1-4, not copied from P1-1)

| Fact | Live value | Command |
|---|---|---|
| Branch | `recovery/3-greenfield-baseline` | `git status -sb` |
| Pending changes (total) | **2,508** | `git status --porcelain \| wc -l` |
| — staged | 354 | `git diff --cached --name-only \| wc -l` |
| — modified (M) | 827 | porcelain |
| — deleted (D) | 448 | porcelain |
| — renamed (R) | 2 | porcelain |
| — untracked (??) | 1,231 | porcelain |
| Merge conflicts (U) | 0 | `git diff --diff-filter=U` |
| Stashes | 0 | `git stash list` |
| Local branches | **216** (204 named `feature/new-pr*`) | `git branch \| wc -l` |
| `.git` size | 305 MB | `du -sh .git` |
| Working tree size | **3.7 GB** | `du -sh .` |
| `.gitattributes` | **ABSENT** | `ls .gitattributes` |
| csproj on disk | 13 | `find -name *.csproj` |
| csproj in sln | 5 | `grep .sln` |

---

## 2. Execution Blockers (MUST clear before restructuring)

These are ordered by how hard they block. Each maps to a Wave that resolves it.

### B1 — Dirty working tree (2,508 pending) — BLOCKER, HIGH
A move/rename phase on a dirty tree is unsafe: `git mv` mixes with 827 unrelated modified files and 448 deletions, making any single commit unreviewable and any rollback ambiguous. **Nothing in Waves 3+ may start while the tree is dirty.** Cleared by **Wave 0**.

### B2 — In-flight migrations relocation is half-applied — BLOCKER, HIGH (NEW, not in P1-1/2/3)
Live evidence: 93 staged deletions under `PointOfSale.Infrastructure/Data/Migrations/` **and** `PointOfSale.Infrastructure/Migrations/` now exists with tracked files. A migration-folder move (`Data/Migrations/` → `Migrations/`) is **partially staged**. This must be either completed+committed or reverted as one atomic unit before any other restructuring — otherwise EF Core migration discovery can break. Cleared by **Wave 0**.

### B3 — No `.gitattributes` → whole-tree CRLF churn — BLOCKER, HIGH (NEW)
Live evidence: `git status` emits `LF will be replaced by CRLF` for hundreds of files; `.gitattributes` is absent. On this Windows host, any bulk `git mv`/`git add` will rewrite line endings across the entire tree, producing thousands of phantom diffs that bury the real moves and make review/rollback impossible. **A committed `.gitattributes` with `* text=auto eol=lf` (and binary rules) is a hard prerequisite for Waves 3+.** Cleared by **Wave 0**.

### B4 — Tracked secrets — BLOCKER for publication, HIGH
`PointOfSale.WhatsApp.Baileys/.env.production` and `.env.staging` are tracked (contain `AUTH_STATE_ENCRYPTION_SECRET` + infra host). Must be handled in an **isolated** step (verify → rotate → `git rm --cached` → ignore), never inside bulk cleanup. Cleared by **Wave 1**.

### B5 — Tracked generated artifacts — BLOCKER, MEDIUM
Live: `api-build.binlog`, `marketplace-event-backbone.binlog`, `PointOfSale.Tests/build.binlog`, `PointOfSale.Tests/TestResults/CurrentRun/current.trx`, 19 tracked files under `temp/` (ignored-but-tracked), 25 tracked `**/artifacts/codex-obj/**` build-cache files across all 5 projects (NEW — not enumerated in P1-2), 2 tracked `.codex-tmp/LoginProbe` files. These pollute every diff and must be untracked before moves. Cleared by **Wave 2**.

### B6 — 216 local branches (204 `new-pr*`) — BLOCKER for freeze discipline, MEDIUM (NEW)
A migration branch cannot become authoritative while 204 stale `feature/new-pr*` branches invite accidental merges/rebases. Branch inventory + prune policy is a prerequisite for the freeze. Addressed by **Wave 1 / Freeze Rules (§6)**.

### B7 — Unresolved UNKNOWN assets — BLOCKER for their specific moves, MEDIUM
`PointOfSale/` (legacy MVC), `PointOfSale.Admin/` (no package.json), 8 orphan csprojs, per-file root SQL applied-state. Must be investigation-gated (P1-3 §8) before their rows execute. Addressed by **Wave 3 investigation gate**.

### B8 — No CI safety net — RISK amplifier, MEDIUM
`.github/` is empty; there is no automated build/test gate to catch a bad move. Mitigated by establishing a **local validation harness** (build+test script) in Wave 0 and optionally seeding CI in Wave 6.

---


## 3. Wave Sequence (derived from evidence, NOT assumed)

The brief's example sequence is a starting hypothesis. The evidence above forces this ordering:

1. **Stabilization must be first** (B1/B2/B3) — you cannot safely move anything on a dirty tree with a half-done migration move and uncontrolled CRLF churn.
2. **Security before artifact cleanup** (B4) — a `git rm --cached` sweep of artifacts must not be the commit that also touches secret files; secrets get their own isolated, rotation-gated wave so they are never lost in a bulk change.
3. **Artifact untracking before restructure** (B5) — remove generated noise so the restructure diff is pure renames.
4. **Investigation gate is folded INTO the restructure wave**, not a separate wave — UNKNOWN assets (B7) are resolved just-in-time, per P1-3 section 8, before their specific rows execute.
5. **Restructure is split by risk** — DB/docs/scripts (low blast radius) move before source (`src/` — high blast radius, breaks sln + build).
6. **Documentation consolidation** rides after source so archive/docs moves do not collide with source moves.
7. **Validation and Certification** close the program.

Final sequence:

| Wave | Name | Clears |
|---|---|---|
| **0** | Repository Stabilization | B1, B2, B3, B8(harness) |
| **1** | Security Preparation + Branch Hygiene | B4, B6 |
| **2** | Generated Artifact Cleanup | B5 |
| **3** | Repository Restructure (investigation-gated, sub-staged) | B7 + P1-3 section 10 MOVE/ARCHIVE |
| **4** | Documentation Consolidation | P1-3 section 3.6/3.9 |
| **5** | Historical Archive | P1-3 section 3.9 |
| **6** | Validation | all gates (section 5) |
| **7** | Certification | sign-off |

Waves 4 and 5 are kept separate because **Wave 4 curates what stays in `docs/` (a judgement task)** while **Wave 5 is a mechanical bulk relocation to `archive/` (a move task)** — different owners, different rollback profiles.

---

## 4. Wave Specifications

Each wave documents: Purpose, Prerequisites, Inputs, Outputs, Actions, Validation, Rollback, Risk, Complexity, Duration, Blocking conditions.

### WAVE 0 — Repository Stabilization
- **Purpose:** Reach a clean, reproducible baseline: empty working tree, resolved in-flight migration move, committed line-ending policy, and a working local build/test harness.
- **Prerequisites:** None (entry wave).
- **Inputs:** Current dirty tree (2,508 pending), `PointOfSale.sln`, branch `recovery/3-greenfield-baseline`.
- **Outputs:** `git status` clean; `.gitattributes` committed; migration-folder move resolved; `scripts/ci/validate.sh` present; tag `p1-4/wave0-baseline`.
- **Actions:**
  1. Tag exact current state first: `git tag p1-4/pre-stabilization` (immutable checkpoint).
  2. Triage the 354 staged changes: confirm they are the intended migration relocation. Commit as one reviewed commit OR unstage+revert if unintended. Decision gate: the B2 half-move ends fully-applied-and-committed or fully-reverted — never partial.
  3. Add root `.gitattributes`: `* text=auto eol=lf` plus binary rules (`*.png -text`, `*.backup -text`, `*.binlog -text`, `*.trx -text`, `*.jpg`, `*.pdf`, `*.zip`). Commit as its own commit BEFORE renormalization.
  4. Run `git add --renormalize .` as a dedicated isolated commit `chore: normalize line endings` so CRLF churn never mixes with structural moves.
  5. Classify the 1,231 untracked files: commit intended work, add the rest to `.gitignore`. Zero surprise untracked files remain.
  6. Author `scripts/ci/validate.sh`: `dotnet restore && dotnet build PointOfSale.sln -c Release && dotnet test` + per-app `npm ci && npm run build`. This is the harness every later wave runs.
  7. Tag `p1-4/wave0-baseline` once tree is clean and harness passes.
- **Validation (exit gate):** `git status --porcelain` EMPTY; `git ls-files | wc -l` recorded as baseline; `scripts/ci/validate.sh` exits 0 (solution + all 3 SPAs + Baileys build, tests pass); no CRLF warnings on fresh `git status`.
- **Rollback:** `git reset --hard p1-4/pre-stabilization`. No data loss because untracked files were classified; verify with dry-run `git clean -nxd` before any real clean.
- **Risk:** MEDIUM (renormalization commit is large; mitigated by isolation + pre-tag).
- **Complexity:** HIGH (2,508 items to triage — largest judgement load of the program).
- **Duration:** 1–2 days.
- **Blocking conditions:** ABORT if the 354 staged changes cannot be explained; ABORT if the harness cannot build the current tree (fix build before certifying moves).

### WAVE 1 — Security Preparation + Branch Hygiene
- **Purpose:** Remove tracked secrets safely (with rotation) and inventory/prune branches so a migration branch can become authoritative.
- **Prerequisites:** Wave 0 complete.
- **Inputs:** `PointOfSale.WhatsApp.Baileys/.env.production`, `.env.staging`; 216 local branches; `origin`.
- **Outputs:** Secrets untracked + rotated + ignored; `.env.example` verified secret-free; `docs/project/P1-4_BRANCH_INVENTORY.md`; pruned/tagged stale branches.
- **Actions:**
  1. Secret rotation FIRST (before untracking): DevOps rotates `AUTH_STATE_ENCRYPTION_SECRET` and the exposed Redis credential in the live environment. Untracking does not unexpose (secret stays in history) — rotation is the real fix.
  2. `git rm --cached` the two `.env` files (keep on disk).
  3. Add `**/.env` and `**/.env.*` to `.gitignore` with `!**/*.env.example`. Verify a committed `.env.example` with placeholders only.
  4. History scrub (SCHEDULED, not silent): plan a `git filter-repo` scrub as a discrete announced operation (see section 6) — rewrites history, requires re-clone. Document; do not run inside a bulk wave.
  5. Branch hygiene: dump `git branch --format '%(refname:short) %(committerdate:iso)'` into the inventory doc. Tag keepers as `archive/<name>`, then `git branch -d` merged/stale `feature/new-pr*` locally. Never delete remote branches without owner sign-off.
- **Validation:** `git ls-files | grep -E '.env' | grep -vi example` EMPTY; `.env.example` present + secret-free; branch count reduced + documented; rotation confirmed by DevOps.
- **Rollback:** Untracking reversible via `git checkout p1-4/wave0-baseline -- <file>`. Branch deletions recoverable from reflog / `archive/<name>` tags. Rotation is NOT rolled back.
- **Risk:** HIGH (secrets) — isolated and rotation-gated.
- **Complexity:** MEDIUM.
- **Duration:** 0.5–1 day (excluding separately-scheduled history scrub).
- **Blocking conditions:** ABORT untracking if rotation has NOT happened. Do NOT bundle any other change into the secret commit.

### WAVE 2 — Generated Artifact Cleanup
- **Purpose:** Untrack all machine-generated artifacts so the Wave 3 restructure diff is pure renames.
- **Prerequisites:** Waves 0–1 complete.
- **Inputs:** `api-build.binlog`, `marketplace-event-backbone.binlog`, `PointOfSale.Tests/build.binlog`, `.../TestResults/CurrentRun/current.trx`, 19 tracked `temp/**`, 25 tracked `**/artifacts/codex-obj/**` (NEW — not enumerated in P1-2), 2 `.codex-tmp/LoginProbe` files, 16 root `build_*.txt`/`errors*.txt`/`test_output*.txt`.
- **Outputs:** All untracked; `.gitignore` rewritten per P1-3 section 12; clean rebuild verified.
- **Actions:**
  1. `git rm --cached` each group in separate labelled commits (binlogs; trx/TestResults; `temp/`; `codex-obj`; `.codex-tmp/LoginProbe`; root logs) so any group is independently revertible.
  2. Rewrite `.gitignore`: `**/bin/`, `**/obj/`, `**/artifacts/`, `**/node_modules/`, `**/.playwright/`, `**/TestResults/`, `**/*.binlog`, `**/*.trx`, scratch dirs (`.codex-temp/`, `.codex-tmp/`, `.varcon04-build/`, `.hermes/`), root build logs.
  3. Run the Wave 0 harness to confirm a clean rebuild regenerates everything removed.
- **Validation:** `git ls-files | grep -Ei '.(binlog|trx)$'` EMPTY; `git ls-files temp/ | wc -l` = 0; codex-obj count 0; harness build exits 0; `git status` clean after build (proves ignores work).
- **Rollback:** Per-group `git revert`, or restore from `p1-4/wave0-baseline`. No data loss — all regenerable.
- **Risk:** LOW (all proven GENERATED/DEAD in P1-2 section 4.1/6).
- **Complexity:** LOW.
- **Duration:** 0.5 day.
- **Blocking conditions:** ABORT a group if grep shows it is build-referenced (none expected).

### WAVE 3 — Repository Restructure (investigation-gated, sub-staged)
- **Purpose:** Execute the P1-3 section 10 Classification Matrix as history-preserving `git mv` operations, moving the live tree into the target layout (`src/`, `tests/`, `db/`, `deploy/`, `scripts/`, `ai/`, `archive/`).
- **Prerequisites:** Waves 0–2 complete; `.gitattributes` committed (so moves do not trigger CRLF churn); investigation gates (below) resolved for the specific rows being executed.
- **Investigation gate (must resolve BEFORE the matching sub-stage runs):**
  - `PointOfSale/` (legacy MVC): confirm runtime/deploy role. If it is the EF migrations startup or a deployed UI → `src/backend/PointOfSale/`; if proven dead → `archive/`. HIGH risk if removed unverified (P1-2 section 3.1). Do NOT move destructively until resolved.
  - `PointOfSale.Admin/`: no `package.json` (verified). Real admin UI → keep; else `archive/`. Do NOT delete unverified.
  - 8 orphan csprojs: P1-2 verified 0 inbound refs → default `archive/scratch-projects/`; `tools/SIDmsgRunner/` promoted to `tools/` only if manual usage confirmed.
  - Root SQL applied-state: per-file DB-side check → applied to `db/migrations` or `db/ops`, history kept → `archive/db/`.
- **Sub-stages (each a separate commit + harness run — ordered low→high blast radius):**
  1. **3a DB:** `migrations/`→`db/migrations/`, `sql/`→`db/ops/`, `DB/`→`db/backups/`+`db/exports/`, root `*.sql`→`db/{migrations,ops,verify}/` or `archive/db/`. (No build impact.)
  2. **3b Deploy:** `staging/`→`deploy/{proxy,env}/`, `n8n-workflows/`→`deploy/automation/`, `setup-database.*`→`deploy/bootstrap/`. (No build impact.)
  3. **3c Scripts:** curated `scripts/` → `scripts/{ops,lib}/`; dead one-timers → `archive/scripts/`. (No build impact.)
  4. **3d AI KB:** `.ai/hermes/**`→`ai/knowledge/`, `.ai-context/**`→`ai/context/`, `.ai-testing/`→`ai/testing/`. (No build impact — but update any tool/skill paths that reference `.ai-context/`.)
  5. **3e Tests:** `PointOfSale.Tests/`→`tests/PointOfSale.Tests/`, `testsprite_tests/`→`tests/generated/testsprite/`. **Updates sln paths + test csproj ProjectReferences.**
  6. **3f Source (HIGHEST risk):** `PointOfSale.Core/.Infrastructure/.API/`→`src/backend/`, SPAs→`src/web/*`, Baileys→`src/services/whatsapp-baileys/`. **Rewrites `PointOfSale.sln` project paths, all inter-project `ProjectReference` relative paths, `global.json` scope, CI/script path references.**
- **Actions (per sub-stage):** use `git mv` (never delete+add) to preserve history; after each move, fix references (sln paths, `ProjectReference`, `<ProjectReference Include>`, import paths, script constants, `.gitignore` path anchors); run the Wave 0 harness; commit only if green; tag `p1-4/wave3<x>-done`.
- **Validation (per sub-stage):** `git status` shows only renames (R) + intended reference edits, zero content churn; harness exits 0 (build + tests + all SPAs); `dotnet sln list` matches new paths; no dangling `ProjectReference`; grep for old path strings returns zero live hits.
- **Rollback:** Each sub-stage is a single commit → `git revert <commit>` or `git reset --hard p1-4/wave3<prev>-done`. Because moves are `git mv`, revert restores exact prior paths with history intact. The highest-risk 3f is last, so a rollback there does not unwind low-risk DB/docs moves.
- **Risk:** 3a–3d LOW; 3e MEDIUM; 3f HIGH (build-breaking if references missed).
- **Complexity:** HIGH overall (reference-fixing in 3e/3f).
- **Duration:** 2–4 days (3f dominates).
- **Blocking conditions:** ABORT a sub-stage if the harness fails post-move and references cannot be fixed within the stage; revert and re-plan. Do NOT proceed to 3f until 3a–3e are green and committed.

### WAVE 4 — Documentation Consolidation
- **Purpose:** Curate active docs into `docs/` by class and promote rule/architecture docs from root; resolve duplicate chains.
- **Prerequisites:** Wave 3 complete (source settled so doc moves do not race).
- **Inputs:** ~330 root historical `.md`, root promotion docs (`RABBITMQ_TOPOLOGY_GOVERNANCE_RULES.md`, `AFFILIATE_CROSS_DOMAIN_BOUNDARIES.md`, `ECOMMERCE-ARCHITECTURE.md`, `UNIFIED-FINANCIAL-ENGINE-ARCHITECTURE.md`), `docs/audits|fixes|issues` vs root duplicates, `QUICKSTART.md` vs `QUICK_START_GUIDE.md`.
- **Outputs:** `docs/` organized by class (architecture/operations/security/observability/certifications/runtime/developer/recovery/project); promoted rule docs in `docs/architecture/`; duplicate winners chosen.
- **Actions:** `git mv` promotion docs → `docs/architecture/`; diff duplicate chains, keep canonical, mark loser for archive (Wave 5); pick one quickstart; update README as a navigation index.
- **Validation:** No broken relative links (link-check pass); every doc class has one home; no doc duplicated across `docs/` and root; README index resolves.
- **Rollback:** `git revert` the doc-move commit(s); pure renames, fully reversible.
- **Risk:** LOW (docs have no build role).
- **Complexity:** MEDIUM (per-file judgement on promote vs archive).
- **Duration:** 1–2 days.
- **Blocking conditions:** If canonical winner of a duplicate chain is ambiguous, defer that file to a follow-up, do not guess-delete.

### WAVE 5 — Historical Archive
- **Purpose:** Mechanically relocate all completed-phase historical artifacts to read-only `archive/`.
- **Prerequisites:** Wave 4 complete (so promoted docs are already pulled out and will NOT be archived).
- **Inputs:** ~330 root historical `.md` (AF-*, PHASE-*, SERVICES_MARKETPLACE_*, etc.), orphan HTML, applied root SQL history, dead scripts, orphan scratch projects (from Wave 3 gate).
- **Outputs:** `archive/{reports,db,scripts,scratch-projects,recovery}/` populated; repo root reduced to the P1-3 section 4 allowlist only.
- **Actions:** `git mv` historical `.md`→`archive/reports/`; applied SQL history→`archive/db/`; dead scripts→`archive/scripts/`; gated orphan csprojs→`archive/scratch-projects/`. Add an `archive/README.md` stating read-only retention policy.
- **Validation:** Root contains ONLY the P1-3 section 4 allowlist (`README.md`, `PointOfSale.sln`, `global.json`, `.editorconfig`, `Directory.Build.props`, `.gitignore`, `LICENSE`, and the allowed dirs); `find . -maxdepth 1 -type f | wc -l` at architectural minimum; harness still green (archive is inert).
- **Rollback:** `git revert` the archive commit(s); pure renames.
- **Risk:** LOW (P1-2 classifies these as relocate-not-delete; nothing build-referenced moves here).
- **Complexity:** LOW (mechanical bulk move).
- **Duration:** 0.5–1 day.
- **Blocking conditions:** ABORT moving any file that grep shows is still referenced by build/source (should be none after Wave 3).

### WAVE 6 — Validation
- **Purpose:** Prove the restructured repository is functionally identical to the pre-migration baseline and matches the P1-3 blueprint.
- **Prerequisites:** Waves 0–5 complete.
- **Inputs:** Full restructured tree; `p1-4/wave0-baseline` tag; harness.
- **Outputs:** A validation report `docs/project/P1-4_VALIDATION_REPORT.md`; optionally seeded `.github/workflows/ci.yml`.
- **Actions:** run every gate in section 5; diff `git ls-files` count vs baseline (accounting for intended removals) to prove zero unintended file loss; full clean build + full test suite + all SPA builds; broken-reference sweep; structure-vs-blueprint check; secret-exposure re-scan.
- **Validation:** all section 5 gates PASS.
- **Rollback:** if a gate fails, revert the offending wave's commit; the tree returns to the last green tag.
- **Risk:** LOW (read-only verification).
- **Complexity:** MEDIUM.
- **Duration:** 1 day.
- **Blocking conditions:** ANY gate failure blocks Certification.

### WAVE 7 — Certification
- **Purpose:** Formal sign-off that the migration is complete, reversible history is preserved, and the repo matches the blueprint.
- **Prerequisites:** Wave 6 all-green.
- **Inputs:** Validation report; stakeholder sign-offs.
- **Outputs:** `docs/project/P1-4_CERTIFICATION.md`; migration branch merged to the authoritative line; final tag `p1-4/migration-complete`; freeze lifted.
- **Actions:** collect sign-offs (Dev/QA/DevOps/Architecture/Release); merge migration branch via reviewed PR (no force-push); tag; announce freeze lift; retain all `p1-4/*` tags for the rollback window.
- **Validation:** signed certification present; authoritative branch builds green in CI; tags pushed.
- **Rollback:** the migration merge can be reverted as a single merge commit (`git revert -m 1`) during the retention window; all wave tags remain as anchors.
- **Risk:** LOW.
- **Complexity:** LOW.
- **Duration:** 0.5 day.
- **Blocking conditions:** no unresolved gate; no missing sign-off.

---

## 5. Validation Gates (applied in Wave 6, and per-wave where noted)

| # | Gate | Check | Applies |
|---|---|---|---|
| G1 | Repository builds | `dotnet build PointOfSale.sln -c Release` exits 0 | every wave |
| G2 | Tests pass | `dotnet test` + frontend unit tests green | every wave touching code/tests |
| G3 | Frontends build | `npm ci && npm run build` for admin, storefront, commercehub, Baileys | Wave 3e/3f, 6 |
| G4 | No broken references | grep old paths → 0 live hits; `dotnet sln list` matches; no dangling `ProjectReference` | Wave 3, 6 |
| G5 | No missing documentation | link-check passes; every doc class has a home | Wave 4, 6 |
| G6 | No orphan moves | every moved file lands at its P1-3 target; `git status` shows only R + intended edits | Wave 3–5 |
| G7 | No secret exposure | `git ls-files | grep .env | grep -vi example` EMPTY; secrets rotated | Wave 1, 6 |
| G8 | Git status clean | `git status --porcelain` EMPTY after build | every wave exit |
| G9 | Structure matches blueprint | root = P1-3 section 4 allowlist; top-level dirs = target map | Wave 5, 6 |
| G10 | Zero unintended file loss | `git ls-files` count reconciles with baseline minus intended removals | Wave 6 |
| G11 | History preserved | `git log --follow` traces a sample moved file across the rename | Wave 6 |
| G12 | Line-ending stability | fresh `git status` shows no CRLF warnings | Wave 0, 6 |

A wave may not be tagged done until its listed gates pass.

---

## 6. Rollback Strategy

**Global mechanism:** every wave ends in a labelled tag (`p1-4/pre-stabilization`, `p1-4/wave0-baseline`, `p1-4/wave3f-done`, ...). All structural changes use `git mv` (history-preserving) and land as reviewable commits, so any wave is reversible by `git revert <commit>` (safe on shared branches) or `git reset --hard <tag>` (local only, pre-share).

| Wave | How to undo | What data could be lost | How to prevent loss |
|---|---|---|---|
| 0 | `git reset --hard p1-4/pre-stabilization` | untracked files if cleaned | classify untracked first; `git clean -nxd` dry-run before any clean |
| 1 | restore `.env` from `p1-4/wave0-baseline`; branch reflog / `archive/<name>` tags | deleted branches; rotated secret (intentional) | tag keepers before delete; rotation is one-way by design |
| 2 | per-group `git revert` | none (all regenerable) | verify rebuild regenerates each item |
| 3 | `git revert` sub-stage commit or `git reset --hard p1-4/wave3<prev>-done` | none (renames) | sub-stage + harness per stage; 3f last |
| 4 | `git revert` doc-move commit | none (renames) | defer ambiguous duplicates instead of deleting |
| 5 | `git revert` archive commit | none (renames) | archive is relocate-not-delete |
| 6 | revert offending wave | none | read-only |
| 7 | `git revert -m 1` the merge | none within retention window | retain all `p1-4/*` tags |

**Hard rule:** REMOVE-from-git dispositions (P1-3 section 10) untrack but keep files on disk during the program; physical deletion (if any) happens only AFTER certification + retention window, as a separate approved operation. The DB backups (`DB/`, HIGH data-loss risk) are relocated to `db/backups/` or object storage, NEVER deleted.

---

## 7. Repository Freeze Rules

- **Feature work stops** at the start of Wave 3 (restructure). Waves 0–2 (stabilization/security/artifacts) can overlap a short code-freeze notice but SHOULD also run under freeze to keep the baseline stable.
- **Merges to the migration line stop** during Waves 3–7 except the migration commits themselves. No `feature/new-pr*` may merge into the migration branch.
- **Rebasing is forbidden** on the migration branch once Wave 3 begins (rebase rewrites the rename history that rollback depends on). Use merge/revert only.
- **The migration branch becomes authoritative** at Wave 7 certification, replacing `recovery/3-greenfield-baseline` as the base for new work.
- **History-scrub window:** the `git filter-repo` secret scrub (from Wave 1) executes in a single announced maintenance window; after it, ALL developers must re-clone (old clones are incompatible). This is scheduled separately and communicated 48h ahead.
- **Branch prune freeze:** no new `feature/new-pr*` branches during the program; the 204 stale branches are inventoried (Wave 1) and pruned post-certification.

---

## 8. Communication Plan

| Audience | What they need | When | Channel |
|---|---|---|---|
| Developers | Freeze start/end, re-clone requirement after history scrub, new path map (`src/`, `tests/`, ...) | before Wave 0; at each freeze boundary; 48h before scrub | team channel + pinned migration guide |
| QA | Test-suite relocation (`tests/`), harness command, validation gate status | before Wave 3e; at Wave 6 | QA channel + validation report |
| DevOps | Secret rotation ask (Wave 1), deploy path changes (`deploy/`), CI seeding (`.github/`), history-scrub window | before Wave 1; before Wave 3b; before scrub | DevOps channel + runbook |
| Architecture | Blueprint adherence, investigation-gate decisions (legacy MVC, Admin, orphans), doc promotion choices | Wave 3 gate; Wave 4 | architecture review |
| Release Management | Freeze windows, migration-branch authority switch, certification sign-off | program start; Wave 7 | release calendar + certification |

Cadence: a short daily status during Waves 0/3 (highest-load), a wave-boundary summary otherwise.

---

## 9. Success Metrics

- **Zero data loss** — `git ls-files` reconciles to baseline minus intended removals (G10); no file physically deleted before certification.
- **Zero missing files** — every P1-3 matrix row landed at its target or a gated archive; no orphan moves (G6).
- **Zero build regressions** — G1/G3 green on the restructured tree, identical to baseline build result.
- **Zero test regressions** — G2 pass count equals baseline pass count.
- **Root reduced to architectural minimum** — root = P1-3 section 4 allowlist only (G9); from 569 loose files to ~7 allowed files + allowed dirs.
- **Generated artifacts removed** — G7-adjacent: 0 tracked `.binlog`/`.trx`/`codex-obj`/`temp` (Wave 2 validation).
- **Historical assets archived** — ~330 historical `.md` + legacy SQL/scripts relocated to `archive/`, not deleted.
- **Secrets remediated** — 0 tracked non-example `.env` (G7) AND rotation confirmed AND history scrubbed.
- **History preserved** — `git log --follow` works across renames (G11).
- **Line-ending stability** — no CRLF churn on fresh status (G12).

---

## 10. Out-of-Scope

This program is repository restructuring only. Explicitly NOT included:
- Business logic refactoring or bug fixing.
- Architecture redesign of the running system (module boundaries, service decomposition).
- Feature implementation or new endpoints.
- Database schema redesign (migrations are relocated, not re-authored).
- Frontend redesign or dependency upgrades.
- Performance tuning.
- Writing the actual CI pipeline logic beyond an optional build+test seed (full CI is a follow-on program).
- Physical deletion of REMOVE-classified assets (deferred to a post-certification approved operation).
- Rewriting or curating the CONTENT of historical reports (they are relocated as-is).

---

## 11. Execution Readiness Checklist (pre-Wave-0)

- [ ] All stakeholders (Dev/QA/DevOps/Architecture/Release) acknowledged the plan and freeze windows.
- [ ] DevOps ready to rotate `AUTH_STATE_ENCRYPTION_SECRET` + Redis credential (Wave 1).
- [ ] Backup of `.git` + working tree taken off-machine (defense beyond tags).
- [ ] Maintenance window booked for the history scrub.
- [ ] `scripts/ci/validate.sh` requirements agreed (build + test + SPA builds).
- [ ] Owner assigned per wave.

---

*End of P1-4 Repository Transition Plan. Planning only — no files were moved, renamed, deleted, generated, staged, or committed by this document. Every blocker (B1–B8) and live number was re-verified against the working tree on `recovery/3-greenfield-baseline`; where this plan extends P1-1/P1-2/P1-3 (`.gitattributes` absence/CRLF churn B3, in-flight migrations move B2, 216-branch sprawl B6, tracked `codex-obj` artifacts B5), the live evidence is cited inline.*
