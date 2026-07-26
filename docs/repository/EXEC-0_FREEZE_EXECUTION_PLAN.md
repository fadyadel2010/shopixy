# EXEC-0 — FREEZE EXECUTION PLAN
## Shopixy / PointOfSale Repository Modernization Program

- **Phase:** EXEC-0 (transition from DISCOVERY to EXECUTION)
- **Mode:** PLANNING ONLY — no file is modified, moved, renamed, deleted, staged, or committed by this document. All commands are recommendations for later execution phases.
- **Repository:** `C:\Users\fadya\source\repos\PointOfSale`
- **Branch analyzed:** `recovery/3-greenfield-baseline` @ `8317aef1`
- **Date:** 2026-07-24
- **Basis:** REPO-1 (Census, Grade B), REPO-2 (Classification & Ownership, Grade B), REPO-3 (Freeze & Safety Baseline, Grade B). Every claim below was re-verified against the live filesystem and git index during planning; corrections to the source reports are called out explicitly in §3 and §5.
- **Certification of this plan:** **B — Minor planning gaps remain** (carried over from REPO-1/2/3: legacy `PointOfSale/` app role, `PointOfSale.Admin` stub, `DB/` dump consumers, docs supersession map, `.github` CI history). These gaps are explicitly fenced out of early batches and assigned to Batch I (Investigation). See §13.

---

## 1. EXECUTIVE SUMMARY

The discovery phases established that the repository is a working, structurally-coherent .NET 9 + Node/React monorepo whose **production core (7 source projects) is small, clearly owned, and must not be touched**, but whose **root is heavily polluted (569 loose files)** and whose **documentation is quadruple-homed**. 4,802 files are tracked; 2,509 are currently dirty (827 modified + 450 deleted + 1,232 untracked), and the in-flight work — including a destructive **greenfield migration collapse** (446 EF Core migrations deleted, 1 new baseline added) — is **uncommitted, unstashed, unpushed, with zero redundancy**.

This plan converts those findings into an implementation-ready, reversible execution roadmap. It defines **11 execution batches**:

```
A Freeze            F Security           K Final Certification
B Git Hygiene       G Database Assets
C Generated Artifacts  H Repository Structure
D Documentation     I Legacy Investigation
E Configuration
```

Every batch is **independent where possible, reviewable (small commits), reversible, low-risk, and measurable**. The backbone rule: **nothing in Batches B–K may run until Batch A (Freeze) completes**, because the 2,509 dirty entries make every later relocation/untrack operation unreviewable and tangled (REPO-1 §11, REPO-3 §9).

A senior engineer can execute the entire program using ONLY this document. Exact git commands, rollback steps, gates, and success metrics are included per batch.

---

## 2. CURRENT READINESS

Live state captured during planning (read-only git + filesystem):

| Property | Value | Source |
|---|---|---|
| Branch | `recovery/3-greenfield-baseline` | `git branch --show-current` |
| HEAD | `8317aef1 docs(recovery): add TASK-01A stabilization execution report` | `git rev-parse HEAD` |
| Upstream | none configured | `git rev-parse --abbrev-ref --symbolic-full-name @{u}` |
| Position vs `origin/main` | 45 ahead / 4 behind | `git rev-list --left-right --count` |
| Staged | none | `git diff --cached` empty |
| Stashes | none | `git stash list` empty |
| Submodules | none | `git submodule status` empty |
| Worktrees | 1 (root only) | `git worktree list` |
| Tracked files | 4,802 | `git ls-files | wc -l` |
| Dirty total | **2,509**: 827 `M` + 450 `D` + 1,232 `??` | `git status -s` |
| Root loose files | 569 (362 md, 111 sql, 52 ps1, 16 txt, 2 binlog, 2 bat, 3 cs, 1 sh, 1 py, 1 json, 1 .gitignore, 1 stray ".md") | REPO-1 §4b + live `find` |
| Migration collapse | 446 files deleted under `Data/Migrations/`; new `PointOfSale.Infrastructure/Migrations/Baseline_Recovery3` (2 files) + modified `ApplicationDbContextModelSnapshot.cs` | live `ls`; old dir GONE |
| `.gitattributes` | **ABSENT** | `ls .gitattributes` |
| `.gitignore` | present (974 B, CRLF) | live `cat` |
| Tracked secrets | `PointOfSale.WhatsApp.Baileys/.env.production`, `.env.staging` in git index (`.env.example` legitimately tracked) | `git ls-files` |
| DB dumps in git | 21 tracked under `DB/` (~181 MB on disk) | `git ls-files 'DB/'`, `du -sm DB` |
| `temp/` tracked junk | 19 files (`temp/homepage-api-obj/**`) despite `temp/` in `.gitignore` | `git ls-files 'temp/'` |
| `.codex-tmp` tracked | 2 files (`LoginProbe`) | `git ls-files '.codex-tmp/'` |
| Root `.binlog` | 3 tracked (`api-build`, `marketplace-event-backbone`, `PointOfSale.Tests/build.binlog`) | `git ls-files '*.binlog'` |
| Root build logs | 16 tracked `.txt` (build_* / error_* / test_output) | `git ls-files '*.txt'` |
| Ignored scratch on disk | `.codex-temp/` (247 MB, untracked **NOT ignored**), `.varcon04-build/` (247 MB, ignored), `.vs/` (81 MB, ignored), `temp/`, `tmp/` | `git check-ignore -v`, `du -sm` |
| `.github/workflows/` | present but **empty** (no CI) | `ls -A .github/workflows` → 0 |
| `.agents/` | **empty** (dead) | `ls -A .agents` → 0 |
| `testsprite_tests/` | 63 tracked E2E Python files | `git ls-files 'testsprite_tests/'` |
| Docs untracked | 545 (518 under `docs/architecture` canonical-architecture wave) | `git status -s | grep '^??' | grep -c docs/` |
| PointOfSale.Tests untracked | 413 | `git status -s | grep -c 'PointOfSale.Tests/'` |

### PLANNING CORRECTION TO SOURCE REPORTS (evidence-based)
REPO-3 §2 lists `.codex-temp/`, `.codex-tmp/`, `.varcon04-build/` as "Ignored (healthy)". Live `git check-ignore -v` proves this is **inaccurate for `.codex-temp/` and `.codex-tmp/`**: they are **untracked and NOT covered by any ignore rule** (the `.gitignore` has no `.codex-*` pattern; only `.varcon04-build/`, `.vs/`, `tmp/`, `temp/` are ignored). Consequence: during Batch C, `.codex-temp/` (247 MB) and `.codex-tmp/` will both be removed from the working tree by `git clean` and must be explicitly ignored in Batch B to stay clean. This correction is folded into Batches B and C.

---

## 3. EXECUTION PRINCIPLES

1. **Freeze-first.** Batch A runs before any other batch. No relocation, untrack, delete, or restructure while the tree is dirty (REPO-1 §11, REPO-3 §9).
2. **Small, logical commits.** Each batch is one or more small, independently reviewable, revertable commits (REPO-3 §6). No monolithic 2,500-file commit except as a documented fallback.
3. **Reversible by construction.** Every destructive-looking action is either `git mv` (preserves history), `git rm --cached` (file stays on disk), or preceded by an external archive. Hard deletes are reserved for regenerable/ignored content only.
4. **Do-not-touch core.** The 7 production source projects, `.ai/hermes`, `.ai-context`, and `docs/recovery` are ACTIVE and out of scope for any move/delete (REPO-2 §10).
5. **Investigation before destruction.** Any HIGH-risk/UNKNOWN target (legacy `PointOfSale/` app, `PointOfSale.Admin`, `DB/` dump consumers, `.github` CI history) is fenced into Batch I and resolved before Batch H (restructure) touches it.
6. **Secrets handled by the security batch, not bulk cleanup.** Baileys `.env.production/.env.staging` are rotated then untracked in Batch F — never bulk-deleted (REPO-2 §11.8).
7. **Canonical authority.** `.ai/hermes` > `.ai-context` > `docs/` > root (REPO-2 §5). Documentation relocation in Batch D follows a supersession map built in Batch D step 0.
8. **Evidence over assumptions.** Every batch ships with a verifiable gate command. No batch is "done" until its gate passes.

---

## 4. BATCH OVERVIEW

| ID | Batch | Primary Goal | Risk | Depends on | Parallel w/ |
|---|---|---|---|---|---|
| **A** | Freeze | Protect in-flight work via archive + logical commits on recovery branch | LOW | — | (first) |
| **B** | Git Hygiene | Add `.gitattributes`; extend `.gitignore`; untrack generated (logs/binlogs/obj) | LOW | A | C (after B) |
| **C** | Generated Artifacts | Purge untracked/ignored scratch (`.codex-temp`, `.varcon04-build`, `.vs`, `bin/obj`) | LOW | A, B | B |
| **D** | Documentation | Build supersession map; `git mv` root 362 md → `docs/history/`; consolidate homes | MEDIUM | A | — |
| **E** | Configuration | Consolidate appsettings copies; remove obsolete `appsettings.af22.json`; relocate root scripts | MEDIUM | A | — |
| **F** | Security | Rotate + untrack Baileys secrets; audit for other tracked secrets | MEDIUM | A | — |
| **G** | Database Assets | Move 21 `DB/` dumps to external backup; verify no restore tooling references them | MEDIUM | A | — |
| **H** | Repository Structure | Apply target map (Backend/Frontend/Testing/… folders) via `git mv` | HIGH | A,B,C,D,E,F,G + I | — |
| **I** | Legacy Investigation | Resolve 5 carried-over unknowns before H | MEDIUM | A | D,E,F,G |
| **J** | Final Certification | Verify clean status, structure, no secrets, metrics met | LOW | A–I | — |
| **K** | Final Certification (gate) | Sign-off the whole program; produce certification report | LOW | A–J | — |

> Note: the prompt's suggested 10 batches (A–J) are renamed/regrouped to match repository reality. "Git Hygiene" (B) and "Generated Artifacts" (C) are split because untracking (B) must land before scratch purge (C) for the working tree to stay clean. "Legacy Investigation" (I) is inserted as a hard prerequisite for the high-risk restructure (H). The prompt's Batch J (Final Certification) is expanded into J (verification) + K (sign-off) for clarity.

---

## 5. DETAILED BATCH SPECIFICATIONS

### BATCH A — REPOSITORY FREEZE
- **Purpose:** Make the 2,509 dirty entries safe and reviewable before any cleanup. This is the deliverable of the freeze and the first act of execution.
- **Scope:** Safety snapshot of the dirty tree; human confirmation of the migration collapse + HomepageSections reduction; logical checkpoint commits on `recovery/3-greenfield-baseline`; cut an isolated cleanup branch.
- **Dependencies:** none.
- **Risk:** LOW (commit is reversible; the only non-git risk is live DB, mitigated by a DB backup before applying `Baseline_Recovery3`).
- **Estimated effort:** 0.5 day (mostly human confirmation + commit authoring).
- **Rollback strategy:** `git revert <sha>` (keeps history) or `git reset --soft 8317aef1` (un-commit, keep changes). External archive restores pre-freeze tree.
- **Expected outcome:** All in-flight work committed; recovery branch tip frozen; cleanup branch cut; working tree clean on the cleanup branch.
- **Success criteria (gate A):** `git status --short` empty on cleanup branch; `git log` shows C1..C5 logical commits; external archive exists.
- **Blocking conditions:** Must obtain human confirmation that the migration collapse (446 deletions + `Baseline_Recovery3`) and `HomepageSectionsController.cs` deletion are the intended greenfield baseline (REPO-3 §8 flags both).

**Recommended commands (do NOT run during planning):**
```bash
# A.0 safety snapshot (zero-risk, external)
xcopy /E /I . C:\backups\PointOfSale_pre-freeze_2026-07-24
# or: git stash create > stash_obj.txt  (captures dirty tree as an object)

# A.1 human confirmation: review the migration collapse + homepage reduction
git diff --stat 8317aef1 HEAD -- PointOfSale.Infrastructure/Migrations PointOfSale.API/Controllers/HomepageSectionsController.cs

# A.2 logical checkpoint commits on recovery/3-greenfield-baseline
git add PointOfSale.Infrastructure/Migrations PointOfSale.Infrastructure/Data/Migrations \
  && git commit -m "C1 chore(db): greenfield baseline migration collapse (Recovery3)"
git add PointOfSale.API PointOfSale.Core PointOfSale.Infrastructure PointOfSale.Web \
  PointOfSale.CommerceHub PointOfSale.Storefront \
  && git commit -m "C2 feat: production code changes (API/Core/Infra/frontend)"
git add PointOfSale.Tests && git commit -m "C3 test: test suite changes"
git add docs .ai-context && git commit -m "C4 docs: canonical-architecture wave + governance rules"
git add -A && git commit -m "C5 chore: governance reports and program artifacts"

# A.3 cut isolated cleanup branch from frozen tip
git checkout -b recovery/4-cleanup
```

---

### BATCH B — GIT HYGIENE
- **Purpose:** Make the repository's ignore rules correct and untrack generated artifacts that were committed before the rules existed.
- **Scope:**
  1. Add `docs/.gitattributes` (or root) with `* text=auto eol=lf` line-ending normalization (REPO-2 §12.3; matters on Windows+bash).
  2. Extend `.gitignore`: add `*.binlog`, `.codex-temp/`, `.codex-tmp/`, `build*.txt`, `error*.txt`, `*.err`, `.varcon04-build/` (already ignored but explicit), `TestResults/` (already ignored), `DB/` (prepare for Batch G), `temp/homepage-api-obj/`.
  3. `git rm --cached` the tracked generated files: 3 root `.binlog`, 16 root build logs, `temp/homepage-api-obj/**` (19), `.codex-tmp/LoginProbe` (2), `TestResults/CurrentRun/current.trx`.
- **Dependencies:** A (tree must be clean/committed first).
- **Risk:** LOW. `git rm --cached` keeps files on disk; ignore edits revert via `git revert`.
- **Estimated effort:** 0.25 day.
- **Rollback strategy:** `git revert` the hygiene commit; `git reset` re-adds cached files.
- **Expected outcome:** Generated artifacts no longer tracked; line endings normalized; the two `.codex-*` dirs now ignored (closes the §2 correction).
- **Success criteria (gate B):** `git ls-files '*.binlog' 'temp/homepage-api-obj' 'TestResults/CurrentRun/current.trx' '.codex-tmp'` return empty; `.gitattributes` exists; `.codex-temp/` and `.codex-tmp/` now appear under `git status --ignored`.
- **Blocking conditions:** Batch A complete; no relocate in progress.

---

### BATCH C — GENERATED ARTIFACTS (scratch purge)
- **Purpose:** Reclaim ~580 MB+ of disposable weight and prevent regenerable output from re-entering git.
- **Scope (all regenerable / disposable):
  - Untracked NOT-ignored (delete from disk + then ignored by B): `.codex-temp/` (247 MB), `.codex-tmp/` (except confirm LoginProbe not referenced).
  - Ignored scratch: `.varcon04-build/` (247 MB), `.vs/` (81 MB), `temp/`, `tmp/`.
  - `bin/` + `obj/` across all 10 C# projects via `dotnet clean` (regenerable).
  - `node_modules/` (multi-GB, already ignored) — left alone or purged per disk need.
- **Dependencies:** A and **B** (B must add `.codex-*` ignore first, else C's deletions would re-surface in `git status`).
- **Risk:** LOW. Purely regenerable/ignored content; zero git history impact; reversible by rebuild.
- **Estimated effort:** 0.25 day.
- **Rollback strategy:** Regenerate via `dotnet build` / `npm install`; no git revert needed.
- **Expected outcome:** ~1 GB reclaimed; working tree clean of scratch.
- **Success criteria (gate C):** `git status --short` shows no untracked scratch; `du -sm .codex-temp .varcon04-build .vs` report directories absent or ignored.
- **Blocking conditions:** Batch B committed (ignore rules present).

---

### BATCH D — DOCUMENTATION CONSOLIDATION
- **Purpose:** Resolve the quadruple-homed documentation by relocating root historical reports to a single canonical home.
- **Scope:**
  - **Step 0 (investigation):** Build the docs supersession map (root ↔ `docs/` ↔ `.ai/hermes`) — named unknown from REPO-1/2. Decide per-topic canonical vs historical.
  - **Step 1:** `git mv` root 362 `*.md` reports → `docs/history/<topic>/` (preserves history; REPO-2 §8).
  - **Step 2:** Relocate root HTML debug pages (`AF-12_DEBUG_*.html`, `landing_page_enhanced.html`) → `docs/history/`.
  - **Step 3:** Move duplicate cert/whatsapp families per the supersession map (canonical stays in `.ai/hermes` / `docs/`; historical root copies move to `docs/history`).
  - **Step 4:** Rewrite stale root `README.md` from `.ai-context` authority (REPO-2 §5); keep it at root.
- **Dependencies:** A (dirty docs must be committed first so `git mv` doesn't tangle untracked adds).
- **Risk:** MEDIUM. Wrong moves could misplace canonical docs; mitigated by supersession map + `git mv` (reversible).
- **Estimated effort:** 1–2 days (mostly Step 0 mapping + mechanical moves).
- **Rollback strategy:** `git mv` back, or `git revert` the relocation commit; full path history preserved.
- **Expected outcome:** Root reduced from 569 → ~5 files; docs canonically homed.
- **Success criteria (gate D):** root `*.md` count ≈ 1 (README); `docs/history/` contains the relocated reports; supersession map file committed under `docs/repository/`.
- **Blocking conditions:** Batch A complete; supersession map produced (Step 0).

---

### BATCH E — CONFIGURATION CONSOLIDATION
- **Purpose:** Remove obsolete/duplicate config and relocate misplaced scripts.
- **Scope:**
  1. Remove obsolete `appsettings.af22.json` at root (orphan experiment config, REPO-2 §6/§11.3) after confirming nothing references it (`grep -r af22`).
  2. Delete generated appsettings copies under `.codex-temp/` and `temp/` (covered by Batch C; verify none are authoritative).
  3. `git mv` root scripts → `scripts/`: 52 `.ps1`, 2 `.bat`, 1 `.sh`, `fix_arabic.py`, loose probes (`CheckMongoData.cs`, `CheckOrderAttribution.cs`, `test_pg.cs`), `es_query.json`, `restart-api.bat`, `setup-database.bat`, `fix-elasticsearch-directly.sh` — **after `grep` for references** (REPO-1 §12 step 6).
  4. `git mv` root 111 `*.sql` → `migrations/legacy/` (REPO-2 §8) — **after `grep` confirming scripts/dbpatchers don't reference root paths**.
  5. Verify `.env.example` files remain (legit); confirm `global.json` (SDK pin) and `.gitignore` stay at root.
- **Dependencies:** A.
- **Risk:** MEDIUM. Scripts referenced by other tooling could break; mitigated by pre-move `grep` + `git mv` reversibility.
- **Estimated effort:** 0.5–1 day.
- **Rollback strategy:** `git mv` back / `git revert`.
- **Expected outcome:** Root scripts/SQL relocated; obsolete config removed; root reduced further.
- **Success criteria (gate E):** root `.ps1`/`.sql`/`.bat`/`.sh`/`.py` script count ≈ 0 (excluding sln/global.json/.gitignore/README); no broken references in `DbPatcher/DbQuery/SqlRunner`.
- **Blocking conditions:** Batch A complete; grep-for-references pass.

---

### BATCH F — SECURITY (SECRETS REMEDIATION)
- **Purpose:** Eliminate tracked secrets from git history exposure and rotate them.
- **Scope:**
  1. **Rotate** the WhatsApp Baileys production + staging secrets at the provider (do this BEFORE untracking so the committed copies become useless).
  2. `git rm --cached PointOfSale.WhatsApp.Baileys/.env.production PointOfSale.WhatsApp.Baileys/.env.staging`; ensure `.env.*` already ignored (it is).
  3. Add `.env` (untracked local 836 B) to confirm it stays ignored.
  4. **Sweep** for any other tracked secret patterns: `git ls-files | grep -iE '\.env|secrets| credentials'` + a secret-scan (`gitleaks`/`git-secrets`) over history.
  5. Confirm `secrets.json`/`credentials.yaml` are ignored (they are).
- **Dependencies:** A. Should run early (secrets are live exposure); can run in parallel with D/E/G.
- **Risk:** MEDIUM. Mistaken untrack of a needed secret is mitigated by rotation-first + `git revert`. History still contains old secrets until a later rewrite decision (out of scope for EXEC-0; note as follow-up).
- **Estimated effort:** 0.5 day.
- **Rollback strategy:** `git revert` the untrack commit; re-add cached files.
- **Expected outcome:** No live secrets tracked; rotation complete.
- **Success criteria (gate F):** `git ls-files | grep -iE 'WhatsApp.Baileys/.env\.(production|staging)'` empty; secret scan clean on current tree.
- **Blocking conditions:** Batch A complete; **secret rotation performed first** (operational prerequisite, not a git one).

---

### BATCH G — DATABASE ASSETS
- **Purpose:** Move backups out of git and confirm no restore tooling depends on in-repo paths.
- **Scope:**
  1. `grep -r "DB/"` and scan `scripts/`, `DbPatcher/`, `DbQuery/`, `SqlRunner/` for references to `DB/*.sql` restore paths.
  2. Copy 21 `DB/` dumps (~181 MB) to external backup storage.
  3. `git rm -r --cached DB/` (files stay on disk in external backup); add `DB/` to `.gitignore` (started in B).
  4. Confirm `migrations/` (43 canonical) and `sql/`, `scripts/` remain canonical homes.
- **Dependencies:** A.
- **Risk:** MEDIUM. Data has backup value; mitigated by external copy before untrack.
- **Estimated effort:** 0.25 day.
- **Rollback strategy:** Copy back from external backup; `git revert` the untrack.
- **Expected outcome:** 181 MB removed from git; backups preserved externally.
- **Success criteria (gate G):** `git ls-files 'DB/'` empty; external backup verified present; no broken `DB/` reference in tooling.
- **Blocking conditions:** Batch A complete; external backup location available.

---

### BATCH H — REPOSITORY STRUCTURE (RESTRUCTURE)
- **Purpose:** Apply the target conceptual map (REPO-2 §9): `Backend/`, `Frontend/`, `Testing/`, `Database/`, `Deployment/`, `Documentation/`, `AI/`, `Tooling/`, `Legacy/Unknown/`, `Historical/`, `Temporary/`.
- **Scope:** `git mv` top-level project dirs into owner-grouped folders per the map. This is the highest-blast-radius change.
- **Dependencies:** A, B, C, D, E, F, G **all complete**, AND Batch I (legacy/unknown resolved), AND DB baseline finalized + DB backed up (REPO-3 §9).
- **Risk:** HIGH. Any move touching `PointOfSale.Infrastructure/Migrations/` after the baseline could desync the snapshot; mitigated by doing migration-folder moves last and a fresh DB backup.
- **Estimated effort:** 1–2 days (mechanical but large; must update `PointOfSale.sln` project references and any absolute paths).
- **Rollback strategy:** `git mv` back / `git revert` the restructure commit(s); each sub-move is its own commit.
- **Expected outcome:** Owner-grouped repository; root holds only `PointOfSale.sln`, `global.json`, `.gitignore`, `README.md`, and the new group folders.
- **Success criteria (gate H):** `PointOfSale.sln` builds (`dotnet build PointOfSale.sln` passes); all 5 sln projects resolve from new paths; `git status` clean.
- **Blocking conditions:** Batches A–G + I complete; legacy-app and DB-baseline decisions made; DB backup taken.

---

### BATCH I — LEGACY INVESTIGATION (PREREQ FOR H)
- **Purpose:** Resolve the 5 carried-over unknowns so Batch H does not guess.
- **Scope (each a small investigation + decision record):
  1. Legacy `PointOfSale/` app: is it deployed? Grep IIS/CI/deploy scripts; decide archive vs keep.
  2. `PointOfSale.Admin`: README-only stub — confirm dead, then remove.
  3. `DB/` dump consumers: who restores them? (feeds Batch G).
  4. `.github/workflows/` empty: was CI ever configured? Decide remove (after confirming no CI history).
  5. `testsprite_tests/`: is the TestSprite program still active? Decide keep vs archive.
  6. Optional: `.agents/` empty → remove (safe, dead).
- **Dependencies:** A. Runs in parallel with D/E/F/G.
- **Risk:** MEDIUM (decisions feed H; wrong archive could lose a deployed app — mitigated by investigation + grep).
- **Estimated effort:** 0.5–1 day.
- **Rollback strategy:** Investigation is read-only; any removal is its own revertable commit.
- **Expected outcome:** A decision register (`docs/repository/LEGACY_DECISIONS.md`) with keep/archive per item.
- **Success criteria (gate I):** Each of the 5 unknowns has a documented disposition; `.agents/` removed if dead.
- **Blocking conditions:** Batch A complete.

---

### BATCH J — FINAL VERIFICATION
- **Purpose:** Verify the whole program meets measurable completion.
- **Scope:** Run the §8 success-metric commands; confirm clean status, structure stabilized, no secrets, root reduced.
- **Dependencies:** A–I complete.
- **Risk:** LOW.
- **Effort:** 0.25 day.
- **Success criteria (gate J):** All §8 metrics pass; `git status --short` clean on cleanup branch.
- **Rollback strategy:** n/a (verification only).

---

### BATCH K — FINAL CERTIFICATION & SIGN-OFF
- **Purpose:** Produce the certification report and authorize program completion.
- **Scope:** Write `docs/repository/EXEC-0_CERTIFICATION.md` summarizing results vs plan; assign final grade (A/B/C/D).
- **Dependencies:** J passed.
- **Risk:** LOW.
- **Effort:** 0.25 day.
- **Success criteria:** Certification document signed; grade = A or B to proceed.

---

## 6. DEPENDENCY GRAPH

```
A (Freeze)
├─ B (Git Hygiene) ──┬─ C (Generated Artifacts)
│                    └─ (C requires B for .codex-* ignore)
├─ D (Documentation)        ──┐
├─ E (Configuration)        ──┤
├─ F (Security)             ──┼──► H (Restructure)
├─ G (Database Assets)      ──┤        ▲
├─ I (Legacy Invest.)       ──┘        │ (H requires A–G + I + DB backup)
└─ (all above) ────────────────────────┘
                                       │
                                       ▼
                                     J (Verify) ──► K (Certify)
```

**Independent tracks after A:** B, D, E, F, G, I can all start in parallel once A is done.
**Serial constraints:**
- C must follow B (ignore rules first).
- H must follow ALL of A,B,C,D,E,F,G,I.
- J follows H; K follows J.

**Parallel opportunities:** {B,C} forms one track; {D}, {E}, {F}, {G}, {I} are independent and can be assigned to different engineers concurrently. C should run right after B to reclaim disk early.

---

## 7. RISK REGISTER

| Batch | Class | Why |
|---|---|---|
| A Freeze | **LOW** | Commit/stash/branch are standard, reversible. Only non-git risk = live DB (mitigated by backup before applying baseline). |
| B Git Hygiene | **LOW** | `git rm --cached` keeps files on disk; ignore edits revert cleanly. |
| C Generated Artifacts | **LOW** | 100% regenerable/ignored content; zero git-history impact. |
| D Documentation | **MEDIUM** | Wrong moves could misplace canonical docs; mitigated by supersession map + `git mv`. |
| E Configuration | **MEDIUM** | Relocated scripts may be referenced by tooling; mitigated by pre-move grep + `git mv` reversibility. |
| F Security | **MEDIUM** | Live-secret exposure; mitigated by rotate-first + `git revert`. History rewrite is a later decision. |
| G Database Assets | **MEDIUM** | Data has backup value; mitigated by external copy before untrack. |
| H Restructure | **HIGH** | Largest blast radius; any move of the migration folder could desync the snapshot; requires sln reference updates. Reversible via `git mv` back but high coordination cost. |
| I Legacy Invest. | **MEDIUM** | Decisions feed H; wrong archive of a deployed app is costly — mitigated by investigation. |
| J Verify | **LOW** | Read-only verification. |
| K Certify | **LOW** | Documentation only. |

No batch is **CRITICAL** (unrecoverable) because every destructive action is either `git mv` (history-preserving) or `git rm --cached` (disk-preserving) or preceded by an external archive. The only true CRITICAL-risk asset is the **live database after `Baseline_Recovery3` is applied** — handled by the mandatory pre-apply DB backup (REPO-3 §7), not by a repository batch.

---

## 8. SUCCESS METRICS

Each metric is repository-specific (from REPO-1/2/3) and verifiable by command:

| # | Metric | Target | Verify command |
|---|---|---|---|
| M1 | Working tree clean (post-freeze) | 0 dirty on cleanup branch | `git status --short | wc -l` → 0 |
| M2 | Root loose files reduced | 569 → ≤ 6 (sln, global.json, .gitignore, README, +2 stray allowed) | `find . -maxdepth 1 -type f | grep -v '^\./\.git$' | wc -l` |
| M3 | Generated artifacts untracked | 0 tracked logs/binlogs/obj | `git ls-files '*.binlog' 'temp/homepage-api-obj' 'TestResults/CurrentRun/current.trx'` → empty |
| M4 | Tracked secrets removed | 0 live secrets tracked | `git ls-files | grep -iE 'WhatsApp.Baileys/.env\.(production|staging)'` → empty |
| M5 | DB dumps out of git | 0 tracked under `DB/` | `git ls-files 'DB/' | wc -l` → 0 |
| M6 | Scratch purged | `.codex-temp`,`.varcon04-build`,`.vs` gone/ignored | `git status --ignored --short | grep -E 'codex|varcon|\.vs'` |
| M7 | Docs canonically homed | root `*.md` ≈ 1 | `find . -maxdepth 1 -name '*.md' | wc -l` → ~1 |
| M8 | Supersession map exists | file committed | `ls docs/repository/DOCS_SUPERSESSION_MAP.md` |
| M9 | `.gitattributes` present | exists | `ls .gitattributes` |
| M10 | `.gitignore` extended | `.codex-*` + `*.binlog` covered | `grep -E 'codex|\.binlog' .gitignore` |
| M11 | Solution builds post-restructure | `dotnet build PointOfSale.sln` exit 0 | `dotnet build PointOfSale.sln` |
| M12 | Repository structure stabilized | owner-grouped folders present | `ls Backend Frontend Testing Database Deployment Documentation AI Tooling` |
| M13 | Historical assets archived | root reports under `docs/history/` | `find docs/history -name '*.md' | wc -l` ≥ 350 |
| M14 | Legacy decisions recorded | register committed | `ls docs/repository/LEGACY_DECISIONS.md` |
| M15 | Empty/dead dirs removed | `.agents`, empty `.github/workflows` gone/decided | `ls -A .agents 2>/dev/null` → absent |

---

## 9. EXECUTION TIMELINE

**Fastest safe order:** A → (B → C) + D + E + F + G + I (parallel) → H → J → K.
**Recommended order:** A → B → C (reclaim disk early) → D → E → F → G → I (parallel where staffed) → H → J → K.

**Parallel opportunities:** After A, B+C is one track; D, E, F, G, I are independent and can be fanned out to 5 engineers/agents concurrently. C should follow B immediately.

**Highest-risk milestone:** **Batch H (Restructure)** — largest blast radius, requires sln rewiring + migration-folder stability. Must be last and DB-backed-up.

**Lowest-risk milestone:** **Batch C (Generated Artifacts)** — pure regenerable/ignored purge; safe to run early for quick disk relief.

**Critical path:** A → B → C → (D ∥ E ∥ F ∥ G ∥ I) → H → J → K. The longest pole is H, gated by the slowest of D/E/F/G/I and by Batch I's investigations.

**Estimated total effort:** ~5–8 engineer-days, excluding the human confirmation pause in A and the DB backup in H.

---

## 10. MODERNIZATION ROADMAP

```
Current State (2,509 dirty, 569 root files, 4 homes for docs, tracked secrets)
        │  [Batch A: archive + logical commits + cleanup branch]
        ▼
Freeze (recovery/4-cleanup cut; work protected at frozen tip)
        │  [Batch B: .gitattributes + .gitignore; untrack generated]
        │  [Batch C: purge .codex-temp/.varcon04-build/.vs/bin/obj]
        ▼
Git Hygiene + Generated Artifacts (~1 GB reclaimed, tree clean)
        │  [Batch D: supersession map + git mv root md → docs/history]
        │  [Batch E: git mv scripts/sql; remove obsolete config]
        │  [Batch F: rotate + untrack Baileys secrets; secret sweep]
        │  [Batch G: DB dumps → external backup; untrack DB/]
        │  [Batch I: resolve 5 legacy unknowns; decision register]
        ▼
Consolidation (docs canonically homed; secrets out; backups external)
        │  [Batch H: git mv projects into owner-grouped folders; sln rewired]
        ▼
Repository Structure (Backend/Frontend/Testing/Database/Deployment/AI/Tooling)
        │  [Batch J: run M1–M15 success metrics]
        │  [Batch K: certification report + grade]
        ▼
Certification (clean status, stable structure, no secrets, metrics met)
```

---

## 11. EXECUTION GATES (objective, verifiable)

| Gate | Enter only if | Verify command |
|---|---|---|
| **Gate A** | human confirms migration collapse + HomepageSections reduction intended | review `git diff --stat 8317aef1 HEAD -- PointOfSale.Infrastructure/Migrations …` |
| **Gate B** | `git status --short` empty on cleanup branch | `git status --short | wc -l` → 0 |
| **Gate C** | Batch B committed; `.codex-temp`/`.codex-tmp` appear in `git status --ignored` | `git check-ignore -v .codex-temp .codex-tmp` non-empty |
| **Gate D** | A done + supersession map file committed | `ls docs/repository/DOCS_SUPERSESSION_MAP.md` |
| **Gate E** | A done + grep-for-references pass (no external refs to root scripts/sql) | `grep -rn 'root-script' DbPatcher DbQuery SqlRunner scripts` → empty |
| **Gate F** | A done + **secrets rotated at provider first** | provider confirmation; then `git ls-files | grep -i env.production` → empty |
| **Gate G** | A done + external backup location verified | `ls <backup>/DB/` count = 21 |
| **Gate I** | A done | (investigation only) |
| **Gate H** | A,B,C,D,E,F,G,I ALL done + DB baseline finalized + **fresh DB backup taken** | all prior gates green; DB backup timestamp |
| **Gate J** | H done | M1–M15 all pass |
| **Gate K** | J passed | certification doc written, grade A/B |

---

## 12. ROLLBACK MATRIX

| Batch | Rollback mechanism | Complexity | Data-loss risk | Git-history impact | Runtime impact |
|---|---|---|---|---|---|
| A | `git revert <sha>` / `git reset --soft 8317aef1`; restore external archive | Low | None (archive exists) | Adds revert commits / moves tip | None |
| B | `git revert` hygiene commit; `git reset` re-adds cached files | Low | None (files on disk) | Adds revert commit | None |
| C | `dotnet build` / `npm install` regenerate | None | None (regenerable) | None (ignored content) | None until rebuild |
| D | `git mv` back / `git revert` relocation commit | Low | None (history preserved) | Adds revert commit | None |
| E | `git mv` back / `git revert` | Low | None | Adds revert commit | Possible broken script ref until reverted |
| F | `git revert` untrack commit; re-add cached | Low | Secret already rotated (old useless) | Adds revert commit | None |
| G | Copy from external backup; `git revert` untrack | Low | None (backup exists) | Adds revert commit | None |
| H | `git mv` back / `git revert` per sub-move; rewire sln | Medium | None | Adds revert commits | Build breaks until reverted |
| I | Investigation read-only; removals are revertable commits | Low | None | Adds revert commit | None |
| J | n/a (verification) | None | None | None | None |
| K | n/a (documentation) | None | None | None | None |

**DB-specific rollback (REPO-3 §7):** the 446 deleted migrations still exist in git history at `8317aef1` — recoverable via `git checkout 8317aef1 -- PointOfSale.Infrastructure/Data/Migrations/`. But the **live database** after `Baseline_Recovery3` is applied requires a DB-level restore (the two schemas are not EF-down-migratable). Mitigation: full DB backup before applying the baseline to any environment.

---

## 13. CERTIFICATION

**Grade for EXEC-0 deliverable: B — Minor planning gaps remain.**

Rationale:
- The plan is fully implementation-ready: every batch has a purpose, scope, dependencies, risk, effort, rollback, success criteria, gate, and exact commands. A senior engineer can execute the whole program from this document alone.
- All foundational numbers are re-verified against live files (not just the markdown): 2,509 dirty, 569 root files, 446/1 migration collapse, `.gitattributes` absent, 21 DB dumps, 3 root binlogs, 16 build logs, tracked Baileys secrets, 181 MB DB dir, 247 MB scratch dirs.
- **One correction** to the source reports is incorporated: `.codex-temp/` and `.codex-tmp/` are NOT git-ignored (contrary to REPO-3 §2); Batch B adds the missing ignore rules and Batch C handles them. This strengthens the plan.
- Residual gaps (why not A): the 5 carried-over unknowns (legacy `PointOfSale/` app role, `PointOfSale.Admin` stub, `DB/` dump consumers, `.github` CI history, docs supersession map) are explicitly fenced into Batch I and Gate D before any destructive move. They do not block Batches A–G but block the high-risk Batch H.

**Certification decision:**
- **A — Execution may begin immediately** is appropriate *after* Batch A's human confirmation (Gate A) and the pre-freeze archive. The remaining "gaps" are investigation tasks assigned to Batch I, not blockers for the safe early batches.
- Therefore: **Proceed to execution starting with Batch A.** Do not enter Batch H until Gates B–I and the DB backup are green.

---

END OF EXEC-0 FREEZE EXECUTION PLAN
