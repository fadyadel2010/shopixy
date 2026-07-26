# P1-5 — Repository Execution Runbook (Shopixy / PointOfSale)

> **Program:** Shopixy Project Cleanup Program → PROJECT-1 → P1-5
> **Phase type:** EXECUTION RUNBOOK ONLY. This document is the engineering playbook that a separate execution team follows during the migration. It performs **no** migration, **no** cleanup, **no** commits, **no** file movement. It converts the approved P1-4 Transition Plan into step-by-step operational procedure.
> **Inputs (authoritative — treated as the approved architecture):**
> - `docs/project/P1-1_PROJECT_CENSUS.md`
> - `docs/project/P1-2_DEAD_ASSET_DISCOVERY.md`
> - `docs/project/P1-3_REPOSITORY_TARGET_ARCHITECTURE.md`
> - `docs/project/P1-4_REPOSITORY_TRANSITION_PLAN.md`
> **Repo root:** `C:\Users\fadya\source\repos\PointOfSale`
> **Branch at authoring:** `recovery/3-greenfield-baseline`
> **Verification note:** Every blocker number and structural claim below was re-checked against the LIVE working tree on this branch at authoring time (not copied from prior markdown). Verified facts are boxed in §0 so the execution team runs against reality, not memory. Where live evidence differs from a P1-4 figure, the live figure is the source of truth and the deviation is flagged.

---

## 0. Live Verification Snapshot (run against THIS tree before any wave)

Run these exact commands at the start of Wave 0 and record the output in the Execution Log (§10). Do not trust the numbers in this document over a fresh live read.

| Fact | P1-4 claim | **Live-verified (branch `recovery/3-greenfield-baseline`)** | Command |
|---|---|---|---|
| `.gitattributes` | absent (B3) | **ABSENT** ✅ | `ls .gitattributes` |
| `.editorconfig` / `Directory.Build.props` / `LICENSE` at root | `.editorconfig`/`Directory.Build.props` absent, `LICENSE` add-if-missing (P1-3 §4) | **ALL ABSENT** (Wave 0/Wave 5 create them) | `ls .editorconfig Directory.Build.props LICENSE` |
| Tracked non-example `.env` | 2 (B4) | **2**: `PointOfSale.WhatsApp.Baileys/.env.production`, `.env.staging` ✅ | `git ls-files \| grep -Ei '\.env' \| grep -vi example` |
| In-flight migrations move (B2) | 93 staged deletions under `Data/Migrations` + `Migrations/` now tracked | **Partial move confirmed**: 354 staged total = **351 deletions** (old `PointOfSale.Infrastructure/Data/Migrations/...`) + **2 renames** + `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.{cs,Designer.cs}` staged as add. `Migrations/` already has 3 tracked baseline files. | `git diff --cached --name-only --diff-filter=D \| grep -ci migrations` ; `git diff --cached --name-only --diff-filter=R \| wc -l` |
| Local branches (B6) | 216 (204 `feature/new-pr*`) | **216** / **204** ✅ | `git branch \| wc -l` ; `git branch \| grep -c 'feature/new-pr'` |
| `temp/` tracked (B5) | 19 | **19** ✅ | `git ls-files \| grep -c '^temp/'` |
| Tracked `**/artifacts/codex-obj/**` (B5) | 25 | **22** ⚠️ (P1-4 over-counted by 3 — use live list, not the number) | `git ls-files \| grep 'artifacts/codex-obj'` |
| `.codex-tmp/LoginProbe` tracked (B5) | 2 | **2** ✅ | `git ls-files \| grep -i LoginProbe` |
| Tracked `*.binlog`/`*.trx` (B5) | 4 | **4**: `api-build.binlog`, `marketplace-event-backbone.binlog`, `PointOfSale.Tests/build.binlog`, `PointOfSale.Tests/TestResults/CurrentRun/current.trx` ✅ | `git ls-files \| grep -Ei '\.(binlog\|trx)$'` |
| `.github/` | empty (B8) | **0 files** ✅ | `find .github -type f \| wc -l` |
| Root loose files (P1-3 §4) | 569 (362 md / 111 sql / 52 ps1) | **569** (362 md / 111 sql / 52 ps1) ✅ | `find -maxdepth 1 -type f \| wc -l` |
| `scripts/ci/validate.sh` (Wave 0 output) | to be created | **ABSENT** (Wave 0 step 6 creates it) | `ls scripts/ci/validate.sh` |
| Stashes / merge conflicts | 0 / 0 | **0 / 0** ✅ | `git stash list \| wc -l` ; `git diff --diff-filter=U --name-only \| wc -l` |

**Execution rule:** if any live number differs materially from the plan (e.g. the codex-obj list, the staged migration composition), the execution lead re-derives the exact file list with the live command and uses THAT list in the step. Never type a count from this document into a loop.

---

## 1. Mission & Objective

Convert the approved P1-4 Transition Plan into an executable operations manual. An execution team (not the planners) performs the migration by following this runbook step-by-step.

- **No implementation.** No business-logic change, no refactoring, no new features (see Out-of-Scope §14).
- **No cleanup decisions.** Dispositions are pre-approved in P1-3 §10 Classification Matrix; this runbook only sequences and executes them.
- **No architectural interpretation.** When a step is ambiguous, STOP and escalate to the Architecture owner (§13). Do not guess.

---

## 2. General Rules (every operation)

1. **Atomic** — each step is one logical unit; commit/validate/revert as one.
2. **Reversible** — every structural change uses `git mv` (history-preserving) and is tagged; rollback is `git revert <commit>` (shared) or `git reset --hard <tag>` (local, pre-share only).
3. **Validated** — a step is not "done" until its validation commands pass; never proceed on an unverified wave.
4. **Documented** — every executed step is written to the Execution Log (§10) with the mandated fields.
5. **History-preserving** — never `delete`+`add` where `git mv` applies; never rewrite history during a wave (rebasing the migration branch is forbidden once Wave 3 begins — P1-4 §7).
6. **Never rely on memory** — re-run the §0 snapshot commands; cite the approved plan by wave/step number.

---

## 3. Git Operation Policy (enforced)

| # | Rule | Rationale (source) |
|---|---|---|
| G-P1 | Use `git mv` for every relocation; never `rm` + `add`. | P1-4 §0 / P1-3 P2 — preserves `git log --follow` (G11) |
| G-P2 | **Never force-push** to any shared branch. | P1-4 §0 |
| G-P3 | **Never rewrite history** during a wave. The migration branch is merged via reviewed PR only (`git revert -m 1` to undo, P1-4 §4 Wave 7). | P1-4 §7 |
| G-P4 | **Never combine unrelated operations in one commit.** One concern per commit (e.g. `.gitattributes` separate from renormalization; each artifact group in Wave 2 separate). | P1-4 Wave 0/2 |
| G-P5 | **Never skip validation.** A wave is not tagged done until its gates pass (P1-4 §5). | P1-4 §5 |
| G-P6 | **Do not bundle secret remediation with repository restructuring.** Wave 1 is isolated and rotation-gated. | P1-4 B4 / §7 |
| G-P7 | REMOVE-from-git dispositions **untrack but keep on disk** during the program; physical deletion is deferred to a post-certification approved operation. | P1-4 §6 hard rule |
| G-P8 | DB backups (`DB/`) are **relocated, never deleted** (HIGH data-loss risk). | P1-3 §3.3 / P1-2 §3.9 |
| G-P9 | A committed `.gitattributes` (`* text=auto eol=lf`) is a **hard prerequisite** for Waves 3+ to prevent whole-tree CRLF churn. | P1-4 B3 |
| G-P10 | Tag every checkpoint (§7) before proceeding; retain all `p1-4/*` tags for the rollback window. | P1-4 §6 |

---

## 4. Checkpoints (when to create what)

| Checkpoint | When | Command pattern | Owner | Approval |
|---|---|---|---|---|
| **Pre-stabilization tag** | Before ANY Wave 0 action | `git tag -a p1-4/pre-stabilization -m "..."` | Exec lead | none (local) |
| **Wave 0 baseline tag** | After tree clean + harness green | `git tag -a p1-4/wave0-baseline -m "..."` | Exec lead | Arch/DevOps review |
| **Per sub-stage tags (Wave 3)** | After each 3a–3f commit is green | `git tag -a p1-4/wave3a-done` … `p1-4/wave3f-done` | Exec lead | per-stage harness green |
| **Backup snapshot** | Before Wave 0 (off-machine) and before the history scrub (Wave 1) | `git bundle create backup-<date>.bundle --all` + tar of working tree | DevOps | Release Mgmt |
| **Review checkpoints** | Wave 3 gate decisions (legacy MVC, Admin, orphans); Wave 4 duplicate winners | Architecture review meeting | Architecture | sign-off recorded |
| **QA approval** | Wave 6 validation report | `docs/project/P1-4_VALIDATION_REPORT.md` | QA | sign-off |
| **DevOps approval** | `deploy/` path change + CI seed + scrub window | runbook + comms | DevOps | sign-off |
| **Certification** | Wave 7 | `docs/project/P1-4_CERTIFICATION.md` + `git tag -a p1-4/migration-complete` | Release Mgmt | all 5 sign-offs |

---

## 5. Stop Points

### 5.1 Safe stopping locations (commit/tag exists, tree clean, harness green)
- After `p1-4/pre-stabilization` (nothing done yet — full reset possible).
- After `p1-4/wave0-baseline` (clean tree + harness).
- After any `p1-4/wave3<x>-done` tag (each sub-stage committed green).
- After Wave 5 (archive complete, tree clean).
- After `p1-4/migration-complete` (certified).

### 5.2 Unsafe stopping locations (DO NOT pause here)
- Mid-Wave 0 while the 354 staged changes are partially committed/untracked.
- Mid-Wave 1 having `git rm --cached` the `.env` files **before** rotation (secret now untracked but still exposed + unrotated).
- Mid-Wave 2 with `.gitignore` rewritten but artifacts still tracked (mixed state).
- Mid-Wave 3 sub-stage with broken `ProjectReference` (tree builds red).
- Between `git filter-repo` scrub and developer re-clone (clones incompatible).

### 5.3 Partial-completion risks
- **B2 half-move left partial:** EF Core migration discovery can break → Wave 0 must fully-apply-and-commit OR fully-revert (never partial).
- **CRLF churn mid-move:** without `.gitattributes`, a bulk `git add` rewrites line endings across the tree → thousands of phantom diffs bury real moves → always commit `.gitattributes` + renormalize BEFORE Wave 3.
- **Artifact noise in restructure diff:** if Wave 2 is skipped, generated files pollute Wave 3 renames → Wave 2 mandatory before Wave 3.

### 5.4 Resume procedure
1. Identify the last green tag (`git tag -l 'p1-4/*'`).
2. `git status --porcelain` — if non-empty and the wave is incomplete, decide: `git stash` (if safe) or `git reset --hard <last-green-tag>` (local only) and re-run the wave from its entry criteria.
3. Re-run the §0 snapshot to confirm nothing drifted.
4. Continue from the first incomplete step of that wave. Do NOT skip validation gates.

---

## 6. Execution Log (standard format — one row per executed step)

Every executed step records:

| Field | Value |
|---|---|
| Timestamp (UTC) | `2026-07-26T14:03Z` |
| Engineer | `<name>` |
| Wave / Step | `W0-S2` |
| Command executed | exact command string |
| Validation result | PASS / FAIL (+ gate IDs) |
| Rollback required? | Yes / No |
| Notes | deviations, live counts used, escalations |

Store the log at `docs/project/P1-5_EXECUTION_LOG.md` (created by the execution team at Wave 0 start). This runbook does not pre-fill it.

---

## 7. Wave Overview (sequence from P1-4 §3)

| Wave | Name | Clears | Owner | Duration | Personnel |
|---|---|---|---|---|---|
| 0 | Repository Stabilization | B1,B2,B3,B8 | Exec lead + Backend | 1–2 d | 1 senior + DevOps |
| 1 | Security Prep + Branch Hygiene | B4,B6 | DevOps + Exec lead | 0.5–1 d | DevOps + Release |
| 2 | Generated Artifact Cleanup | B5 | Exec lead | 0.5 d | 1 engineer |
| 3 | Repository Restructure (gated) | B7 + P1-3 §10 | Backend + Frontend + Exec lead | 2–4 d | 2–3 engineers |
| 4 | Documentation Consolidation | P1-3 §3.6/§3.9 | Docs/Architecture | 1–2 d | 1 writer + Arch |
| 5 | Historical Archive | P1-3 §3.9 | Docs steward | 0.5–1 d | 1 engineer |
| 6 | Validation | all gates §5 | QA + Exec lead | 1 d | QA + all |
| 7 | Certification | sign-off | Release Mgmt | 0.5 d | all 5 owners |

---

## 8. WAVE RUNBOOKS (per-wave: Purpose, Owner, Personnel, Duration, Tools, Permissions, Entry/Exit/Abort criteria, Rollback ref, Gates)

### WAVE 0 — Repository Stabilization
- **Purpose:** Clean, reproducible baseline: empty working tree, resolved in-flight migration move, committed line-ending policy, working local build/test harness.
- **Owner:** Execution lead. **Personnel:** 1 senior backend + DevOps. **Duration:** 1–2 days.
- **Required tools:** git, .NET 9 SDK (9.0.308 per `global.json`), Node (for SPA harness), bash.
- **Required permissions:** local commit to `recovery/3-greenfield-baseline`; off-machine backup write; DevOps sign-off for harness scope.
- **Entry criteria:** §0 snapshot recorded; stakeholders acknowledged (P1-4 §11); off-machine backup taken (§4).
- **Exit criteria:** `git status --porcelain` EMPTY; `.gitattributes` committed; migration move resolved+committed; `scripts/ci/validate.sh` present and exits 0; tag `p1-4/wave0-baseline`.
- **Abort criteria:** the 354 staged changes cannot be explained (ABORT, escalate); harness cannot build current tree (ABORT, fix build before certifying moves).
- **Rollback ref:** `git reset --hard p1-4/pre-stabilization` (local only, pre-share).
- **Related gates:** G1, G8, G12 (and G2/G3 once harness runs).

#### WAVE 0 — Steps

**W0-S1 — Tag the pre-stabilization checkpoint**
- Objective: immutable restore point before any change.
- Expected repo state: unchanged dirty tree (2,508 pending).
- Exact action: `git tag -a p1-4/pre-stabilization -m "Pre-stabilization checkpoint for P1-5 migration"`
- Expected result: tag created locally.
- Validation: `git tag -l p1-4/pre-stabilization` → prints the tag.
- Failure conditions: tag already exists → choose a unique suffix, record in log.
- Recovery: none needed (tag-only).
- Next step: W0-S2.

**W0-S2 — Triage the 354 staged changes (B2 gate)**
- Objective: confirm the staged set is the intended `Data/Migrations` → `Migrations` move and resolve it fully.
- Expected repo state: 354 staged (live: 351 deletions + 2 renames + Baseline_Recovery3 add).
- Exact action:
  1. `git diff --cached --stat | tail -20` and `git diff --cached --name-only | grep -i migrations | head` to confirm scope is migrations-only.
  2. **Decision gate (mutually exclusive, never partial):**
     - If intended → `git commit -m "chore(infra): relocate EF migrations Data/Migrations -> Migrations (W0 B2)"`
     - If NOT intended → `git restore --staged . && git checkout -- .` (revert the staged set) then re-stage only intended work.
- Expected result: the staged migration move is either fully committed OR fully reverted; no partial state remains.
- Validation: `git diff --cached --name-only | wc -l` → 0 (if reverted) or small/controlled (if committed as the migration move). `git status --porcelain` shows the rest as before.
- Failure conditions: scope includes non-migration files mixed with the move → ABORT, escalate to Architecture (do not guess).
- Recovery: `git reset --hard p1-4/pre-stabilization` returns to start.
- Next step: W0-S3.

**W0-S3 — Add root `.gitattributes` (B3)**
- Objective: pin line endings before any bulk move.
- Expected repo state: `.gitattributes` absent.
- Exact action: create `./.gitattributes` with exactly:
  ```
  * text=auto eol=lf
  *.png binary
  *.jpg binary
  *.jpeg binary
  *.pdf binary
  *.zip binary
  *.backup binary
  *.binlog binary
  *.trx binary
  ```
  then `git add .gitattributes && git commit -m "chore: add .gitattributes (* text=auto eol=lf) [P1-4 B3]"`
- Expected result: `.gitattributes` tracked in its own commit, BEFORE renormalization.
- Validation: `git ls-files .gitattributes` → prints `.gitattributes`; `git show --stat HEAD` shows only that file.
- Failure conditions: commit accidentally includes other files → `git reset --soft HEAD~1 && git commit` the single file only (G-P4).
- Recovery: `git revert HEAD` (single-file commit is safe to revert).
- Next step: W0-S4.

**W0-S4 — Renormalize line endings (isolated commit)**
- Objective: apply `eol=lf` across tree as one dedicated commit so CRLF churn never mixes with moves.
- Expected repo state: `.gitattributes` now committed.
- Exact action: `git add --renormalize . && git commit -m "chore: normalize line endings (eol=lf) [P1-4 B3]"`
- Expected result: a single large renormalization commit; working tree clean.
- Validation: `git status --porcelain` → EMPTY; fresh `git status` shows no `LF will be replaced by CRLF` warnings.
- Failure conditions: renormalize pulls in unrelated modifications → `git reset --soft HEAD~1`, `git restore --staged .`, re-stage only line-ending changes via `git add --renormalize .` again (confirm `git diff --cached --stat` is line-ending only).
- Recovery: `git revert HEAD` (large but safe, single concern).
- Next step: W0-S5.

**W0-S5 — Classify the 1,231 untracked files**
- Objective: zero surprise untracked files remain (B1).
- Expected repo state: `git status --porcelain` shows only `??` untracked (modified/staged resolved by S2/S4).
- Exact action:
  1. `git status --porcelain | grep '^??' | wc -l` → confirm count (~1,231).
  2. For intended work: `git add <paths> && git commit -m "chore: commit intended untracked work (W0 B1)"`.
  3. For scratch/debris: add to `.gitignore` (append, separate commit `git commit -m "chore: ignore scratch debris (W0 B1)"`).
  4. Dry-run before any `git clean`: `git clean -nxd` (lists what WOULD be removed; never auto-clean).
- Expected result: `git status --porcelain` → EMPTY.
- Validation: `git status --porcelain | wc -l` → 0.
- Failure conditions: an untracked file is actually needed source → do not ignore; commit it. When unsure → escalate, do not guess.
- Recovery: `git reset --hard p1-4/pre-stabilization` (untracked classified, not cleaned, so safe).
- Next step: W0-S6.

**W0-S6 — Author the validation harness `scripts/ci/validate.sh`**
- Objective: a single command every later wave runs (G1/G2/G3).
- Expected repo state: no harness present.
- Exact action: create `scripts/ci/validate.sh` containing (per P1-4 Wave 0 action 6):
  ```bash
  #!/usr/bin/env bash
  set -euo pipefail
  dotnet restore PointOfSale.sln
  dotnet build PointOfSale.sln -c Release
  dotnet test PointOfSale.sln -c Release --no-build
  for app in PointOfSale.Web PointOfSale.Storefront PointOfSale.CommerceHub PointOfSale.WhatsApp.Baileys; do
    (cd "$app" && npm ci && npm run build)
  done
  ```
  `chmod +x scripts/ci/validate.sh && git add scripts/ci/validate.sh && git commit -m "ci: add local validation harness (build+test+SPA) [P1-4 W0]"`
- Expected result: harness file tracked; runs green.
- Validation: `bash scripts/ci/validate.sh` → exit 0; solution + 3 SPAs + Baileys build, tests pass.
- Failure conditions: a SPA lacks `build` script, or Baileys lacks `npm ci` support → record in log, fix harness scope with DevOps sign-off (P1-4 §11); do NOT alter app code.
- Recovery: `git revert HEAD` (harness-only commit).
- Next step: W0-S7.

**W0-S7 — Tag the baseline**
- Objective: wave-complete checkpoint.
- Exact action: `git tag -a p1-4/wave0-baseline -m "Clean baseline: tree clean, .gitattributes committed, harness green [P1-4 W0]"`
- Validation: `git tag -l p1-4/wave0-baseline`; `git status --porcelain` EMPTY; `scripts/ci/validate.sh` exit 0.
- Next step: WAVE 1.

---

### WAVE 1 — Security Preparation + Branch Hygiene
- **Purpose:** Remove tracked secrets safely (with rotation) and inventory/prune branches.
- **Owner:** DevOps + Exec lead. **Personnel:** DevOps + Release. **Duration:** 0.5–1 d (excl. separate scrub).
- **Tools:** git, secret manager access, GitHub CLI (for remote branch policy only).
- **Permissions:** live-env secret rotation rights; local branch delete; **no remote branch delete without owner sign-off**.
- **Entry criteria:** Wave 0 complete; DevOps ready to rotate.
- **Exit criteria:** secrets untracked+rotated+ignored; `.env.example` secret-free; `docs/project/P1-4_BRANCH_INVENTORY.md` written; stale branches pruned/tagged.
- **Abort criteria:** rotation NOT done → ABORT untracking (do not bundle any other change into the secret commit).
- **Rollback ref:** `git checkout p1-4/wave0-baseline -- <file>` for secrets; branch reflog / `archive/<name>` tags for branches.
- **Related gates:** G7.

#### WAVE 1 — Steps

**W1-S1 — Rotate secrets FIRST (before untracking)**
- Objective: real fix is rotation; untracking does not unexpose (secret stays in history).
- Exact action: DevOps rotates `AUTH_STATE_ENCRYPTION_SECRET` and the exposed Redis credential in the live environment; confirm new values live.
- Validation: DevOps sign-off recorded in Execution Log; old value confirmed no longer authoritative.
- Failure/abort: if rotation impossible → STOP, do not proceed to W1-S2.
- Next step: W1-S2.

**W1-S2 — Untrack the two `.env` files**
- Objective: remove from git index, keep on disk.
- Exact action: `git rm --cached PointOfSale.WhatsApp.Baileys/.env.production PointOfSale.WhatsApp.Baileys/.env.staging`
- Expected result: files removed from index, present on disk.
- Validation: `git ls-files | grep -Ei '\.env' | grep -vi example` → EMPTY.
- Next step: W1-S3.

**W1-S3 — Ignore env secrets + verify example is clean**
- Objective: prevent re-commit (P1-3 §11).
- Exact action: append to `.gitignore`:
  ```
  **/.env
  **/.env.*
  !**/*.env.example
  ```
  Verify a committed `PointOfSale.WhatsApp.Baileys/.env.example` (or create placeholders) contains NO real secrets. `git add .gitignore && git commit -m "security: untrack + ignore secret .env files (rotate done) [P1-4 B4]"` — **this commit contains ONLY the secret change**.
- Validation: `git ls-files | grep -Ei '\.env' | grep -vi example` EMPTY; `.env.example` reviewed secret-free.
- Next step: W1-S4.

**W1-S4 — Schedule (do NOT run) history scrub**
- Objective: plan `git filter-repo` as a discrete announced op (P1-4 §6).
- Exact action: document the scrub plan in `docs/project/P1-4_BRANCH_INVENTORY.md` (or a dedicated note); book a 48h-announced maintenance window; communicate re-clone requirement. **Do not execute here.**
- Validation: plan recorded; window booked.
- Next step: W1-S5.

**W1-S5 — Branch inventory + prune**
- Objective: migration branch can become authoritative (B6).
- Exact action:
  1. `git branch --format '%(refname:short) %(committerdate:iso)' > docs/project/P1-4_BRANCH_INVENTORY.md` (append, then commit the doc).
  2. Tag keepers: `git tag archive/<name> <name>` for branches to retain.
  3. `git branch -d <merged/stale feature/new-pr*>` locally. **Never delete remote without owner sign-off.**
- Validation: branch count reduced + documented; `git tag -l 'archive/*'` lists retained.
- Failure: a branch refuses `-d` (unmerged) → use `-D` ONLY after Architecture confirms it is dead; record justification.
- Recovery: reflog / `archive/<name>` tags restore deleted branches.
- Next step: WAVE 2.

---

### WAVE 2 — Generated Artifact Cleanup
- **Purpose:** untrack all machine-generated artifacts so Wave 3 diff is pure renames (B5).
- **Owner:** Exec lead. **Personnel:** 1 engineer. **Duration:** 0.5 d. **Tools:** git.
- **Entry criteria:** Waves 0–1 complete. **Exit:** all untracked; `.gitignore` rewritten; clean rebuild verified.
- **Abort:** a group grep shows it is build-referenced → ABORT that group.
- **Rollback ref:** per-group `git revert`, or `p1-4/wave0-baseline`.
- **Gates:** G1, G8.

#### WAVE 2 — Steps (each group = separate labelled commit)

**W2-S1 — Build binlogs** (`api-build.binlog`, `marketplace-event-backbone.binlog`)
- Action: `git rm --cached api-build.binlog marketplace-event-backbone.binlog` → commit `chore: untrack root binlogs (W2 B5)`.
- Validation: `git ls-files | grep -Ei '\.binlog$' | grep -v 'PointOfSale.Tests' | wc -l` → 0.

**W2-S2 — Test artifacts** (`PointOfSale.Tests/build.binlog`, `PointOfSale.Tests/TestResults/CurrentRun/current.trx`)
- Action: `git rm --cached PointOfSale.Tests/build.binlog PointOfSale.Tests/TestResults/CurrentRun/current.trx` → commit `chore: untrack test binlog/trx (W2 B5)`.

**W2-S3 — `temp/` tracked (19 files)**
- Action: `git rm --cached -r temp/` → commit `chore: untrack ignored temp/ content (W2 B5)`.

**W2-S4 — `**/artifacts/codex-obj/**` (LIVE = 22 files — use `git ls-files | grep artifacts/codex-obj`, NOT the P1-4 "25")**
- Action: `git ls-files | grep 'artifacts/codex-obj' | xargs git rm --cached` → commit `chore: untrack codex-obj build cache (W2 B5)`.
- Validation: `git ls-files | grep -c 'artifacts/codex-obj'` → 0.

**W2-S5 — `.codex-tmp/LoginProbe` (2 files)**
- Action: `git rm --cached .codex-tmp/LoginProbe/LoginProbe.csproj .codex-tmp/LoginProbe/Program.cs` → commit `chore: untrack accidental LoginProbe (W2 B5)`.

**W2-S6 — Root build/error/test logs (16 files: `build_*.txt`, `errors*.txt`, `test_output*.txt`, etc.)**
- Action: `git ls-files | grep -Ei '^(build_|errors|test_output|build-output|build_output|build_log)' | xargs git rm --cached` → commit `chore: untrack root build logs (W2 B5)`.

**W2-S7 — Rewrite `.gitignore` (per P1-3 §12)**
- Append:
  ```
  **/bin/
  **/obj/
  **/artifacts/
  **/node_modules/
  **/.playwright/
  **/TestResults/
  **/*.binlog
  **/*.trx
  .codex-temp/
  .codex-tmp/
  .varcon04-build/
  .hermes/
  db/backups/
  db/exports/
  ```
  `git add .gitignore && git commit -m "chore: rewrite .gitignore for generated/temp/secret (W2 B5)"`.
- Validation: `git ls-files | grep -Ei '\.(binlog|trx)$'` EMPTY; `git ls-files temp/ | wc -l` → 0; codex-obj → 0; `bash scripts/ci/validate.sh` exit 0; `git status` clean AFTER a build (proves ignores work).
- Next step: WAVE 3.

---

### WAVE 3 — Repository Restructure (investigation-gated, sub-staged)
- **Purpose:** execute P1-3 §10 Matrix as `git mv` (history-preserving).
- **Owner:** Backend + Frontend + Exec lead. **Personnel:** 2–3 engineers. **Duration:** 2–4 d. **Tools:** git, dotnet, npm.
- **Entry criteria:** Waves 0–2 complete; `.gitattributes` committed; investigation gates resolved for the rows being executed.
- **Exit:** every sub-stage green + committed + tagged; `git status` shows only renames + intended reference edits.
- **Abort:** harness fails post-move and references can't be fixed in-stage → revert and re-plan; do NOT proceed to 3f until 3a–3e green.
- **Rollback ref:** per sub-stage `git revert <commit>` or `git reset --hard p1-4/wave3<prev>-done`. 3f is LAST so its rollback doesn't unwind low-risk moves.
- **Gates:** G1, G2, G3, G4, G6, G11.

#### Wave 3 — Investigation Gate (resolve BEFORE the matching sub-stage)
Run these BEFORE executing the rows they govern; record verdicts in the log.
- **IG-1 `PointOfSale/` (legacy MVC):** confirm runtime/deploy role (EF migrations startup? deployed legacy UI?). If live → `src/backend/PointOfSale/`; if proven dead → `archive/`. HIGH risk if removed unverified (P1-2 §3.1). Do NOT move destructively until resolved.
- **IG-2 `PointOfSale.Admin/`:** no `package.json` (verified). Real admin UI → `src/web/admin/`; else `archive/`. Do NOT delete unverified.
- **IG-3 8 orphan csprojs:** P1-2 verified 0 inbound refs → default `archive/scratch-projects/`; `tools/SIDmsgRunner/` promoted to `tools/` only if manual usage confirmed (P1-2 §3.4).
- **IG-4 Root SQL applied-state:** per-file DB-side check → applied → `db/migrations`/`db/ops`; history kept → `archive/db/`.

#### WAVE 3 — Sub-stages (each: `git mv` → fix references → harness → commit → tag)

**3a — DB (LOW risk, no build impact)**
- Moves (approved P1-3 §10 rows 12–15):
  - `git mv migrations db/migrations`
  - `git mv sql db/ops` (see IG-4 for per-file split)
  - `git mv DB db/backups` (relocate; `db/backups/` is git-ignored per W2-S7 — verify it is NOT re-tracked)
  - root `*.sql` → `git mv` into `db/migrations|ops|verify` per IG-4; applied history → `archive/db/`.
- Validation: `git status` shows only R; `bash scripts/ci/validate.sh` exit 0 (DB moves don't break build).
- Tag: `p1-4/wave3a-done`.

**3b — Deploy (LOW risk)**
- Moves (rows 17,22,23): `staging/Caddyfile`→`deploy/proxy/Caddyfile`; `staging/pointofsale.api.env.example`→`deploy/env/`; `n8n-workflows/`→`deploy/automation/`; `setup-database.ps1/.bat`→`deploy/bootstrap/`.
- Validation: build green; grep old `staging/` paths → 0 live hits in scripts/docs.
- Tag: `p1-4/wave3b-done`.

**3c — Scripts (LOW risk)**
- Moves (rows 16,18–21): curated `scripts/` → `scripts/{ops,lib}` (`secret-config-helpers.ps1`→`scripts/lib/`); dead one-timers → `archive/scripts/`; update dot-source references in consumers (P1-2 §4.3).
- Validation: any script dot-sourcing `secret-config-helpers.ps1` still resolves; build green.
- Tag: `p1-4/wave3c-done`.

**3d — AI KB (LOW risk, no build impact)**
- Moves (rows 40–42): `.ai/hermes/**`→`ai/knowledge/`; `.ai-context/**`→`ai/context/`; `.ai-testing/`→`ai/testing/`.
- Action: **update any tool/skill paths that reference `.ai-context/`** (grep live references; this includes Hermes skills, scripts, docs).
- Validation: `grep -r '\.ai-context' --include=*.md --include=*.ps1 --include=*.sh . | grep -v '^docs/project/'` → 0 live hits outside this program's own docs.
- Tag: `p1-4/wave3d-done`.

**3e — Tests (MEDIUM risk)**
- Moves (rows 5,38): `PointOfSale.Tests/`→`tests/PointOfSale.Tests/`; `testsprite_tests/`→`tests/generated/testsprite/` (dedupe `_`/`___` per P1-2 §3.11).
- Action: update `PointOfSale.sln` test project path; update test csproj `ProjectReference` relative paths.
- Validation: `dotnet sln list` matches new path; `dotnet test` green (G2); no dangling `ProjectReference` (G4).
- Tag: `p1-4/wave3e-done`.

**3f — Source (HIGHEST risk, LAST)**
- Moves (rows 3,4,7–10, IG-1/IG-2):
  - `PointOfSale.Core/`→`src/backend/PointOfSale.Core/`
  - `PointOfSale.Infrastructure/`→`src/backend/PointOfSale.Infrastructure/`
  - `PointOfSale.API/`→`src/backend/PointOfSale.API/`
  - `PointOfSale/` (legacy, per IG-1)→`src/backend/PointOfSale/` OR `archive/`
  - `PointOfSale.Web/`→`src/web/admin/`; `PointOfSale.Storefront/`→`src/web/storefront/`; `PointOfSale.CommerceHub/`→`src/web/commercehub/`
  - `PointOfSale.WhatsApp.Baileys/`→`src/services/whatsapp-baileys/`
  - `PointOfSale.Admin/` (per IG-2)→`src/web/admin/` OR `archive/`
- Action: **rewrite `PointOfSale.sln` project paths, all inter-project `ProjectReference` relative paths, `global.json` scope, CI/script path references.** Use `dotnet sln remove`+`dotnet sln add` for path changes; manually fix `<ProjectReference Include>` relative paths in each csproj.
- Validation: `git status` shows only R + intended reference edits, zero content churn; `bash scripts/ci/validate.sh` exit 0 (build+tests+all SPAs); `dotnet sln list` matches; `grep` for old path strings (e.g. `PointOfSale.API/`, `src`-forbidden old roots) → 0 live hits (G4); `git log --follow` on a moved file traces the rename (G11).
- Tag: `p1-4/wave3f-done`.
- Next step: WAVE 4.

---

### WAVE 4 — Documentation Consolidation
- **Purpose:** curate active docs into `docs/` by class; promote rule/architecture docs from root; resolve duplicate chains.
- **Owner:** Docs/Architecture. **Personnel:** 1 writer + Arch. **Duration:** 1–2 d. **Gates:** G5, G6.
- **Entry:** Wave 3 complete. **Exit:** `docs/` by class; promoted rule docs in `docs/architecture/`; duplicate winners chosen; README is navigation index.
- **Abort:** canonical winner of a duplicate chain ambiguous → defer that file to a follow-up, do NOT guess-delete.
- **Rollback:** `git revert` doc-move commit(s).
- **Steps:**
  - W4-S1: `git mv` promotion docs (`RABBITMQ_TOPOLOGY_GOVERNANCE_RULES.md`, `AFFILIATE_CROSS_DOMAIN_BOUNDARIES.md`, `ECOMMERCE-ARCHITECTURE.md`, `UNIFIED-FINANCIAL-ENGINE-ARCHITECTURE.md`) → `docs/architecture/`.
  - W4-S2: diff duplicate chains (e.g. `QUICKSTART.md` vs `QUICK_START_GUIDE.md`, `docs/audits|fixes|issues` vs root reports); keep canonical, mark loser for Wave 5 archive.
  - W4-S3: rewrite `README.md` as a navigation index only (P1-3 §4).
  - Validation: link-check pass (no broken relative links); every doc class has one home; no doc duplicated across `docs/` and root; README index resolves.
  - Tag: `p1-4/wave4-done`.

### WAVE 5 — Historical Archive
- **Purpose:** mechanically relocate completed-phase historical artifacts to read-only `archive/`.
- **Owner:** Docs steward. **Personnel:** 1 engineer. **Duration:** 0.5–1 d. **Gates:** G6, G9.
- **Entry:** Wave 4 complete (promoted docs already pulled out). **Exit:** `archive/{reports,db,scripts,scratch-projects,recovery}/` populated; root reduced to P1-3 §4 allowlist.
- **Abort:** any file grep shows still build/source-referenced → do NOT move (should be none after Wave 3).
- **Rollback:** `git revert` archive commit(s).
- **Steps:**
  - W5-S1: `git mv` historical root `.md` (AF-*, PHASE-*, SERVICES_MARKETPLACE_*, etc.) → `archive/reports/`.
  - W5-S2: applied root SQL history → `archive/db/`; dead scripts → `archive/scripts/`; gated orphan csprojs → `archive/scratch-projects/`.
  - W5-S3: add `archive/README.md` stating read-only retention policy (P1-3 §3.9).
  - Validation: root contains ONLY the P1-3 §4 allowlist; `find . -maxdepth 1 -type f | wc -l` at architectural minimum; harness still green.
  - Tag: `p1-4/wave5-done`.

### WAVE 6 — Validation
- **Purpose:** prove restructured repo is functionally identical to baseline and matches blueprint.
- **Owner:** QA + Exec lead. **Personnel:** QA + all. **Duration:** 1 d. **Gates:** all G1–G12.
- **Steps:** run every gate in §9; diff `git ls-files` count vs `p1-4/wave0-baseline` (account for intended removals) → prove zero unintended loss (G10); full clean build + full test + all SPA builds; broken-reference sweep; structure-vs-blueprint check (G9); secret re-scan (G7). Write `docs/project/P1-4_VALIDATION_REPORT.md`.
- **Abort:** ANY gate failure blocks Certification.
- **Rollback:** revert offending wave's commit; tree returns to last green tag.
- **Tag:** `p1-4/wave6-validated`.

### WAVE 7 — Certification
- **Purpose:** formal sign-off; merge migration branch; final tag; lift freeze.
- **Owner:** Release Mgmt. **Personnel:** all 5 owners. **Duration:** 0.5 d. **Gates:** G1, G8, G9, G10, G11.
- **Steps:** collect sign-offs (Dev/QA/DevOps/Architecture/Release); merge migration branch via reviewed PR (no force-push); `git tag -a p1-4/migration-complete`; announce freeze lift; retain all `p1-4/*` tags for rollback window.
- **Rollback:** `git revert -m 1 <merge>` during retention window; wave tags remain anchors.

---

## 9. Validation Playbook (per gate, from P1-4 §5)

| Gate | Commands | Expected output | Failure interpretation | Escalation | Approver |
|---|---|---|---|---|---|
| G1 Build | `dotnet build PointOfSale.sln -c Release` | exit 0 | missing/updated `ProjectReference` after 3e/3f | Exec lead | Dev |
| G2 Tests | `dotnet test PointOfSale.sln -c Release --no-build` + frontend unit tests | green; pass count == baseline | reference/path break | QA | QA |
| G3 Frontends | `npm ci && npm run build` (admin, storefront, commercehub, Baileys) | exit 0 each | wrong SPA path after 3f | Frontend lead | Dev |
| G4 No broken refs | `git status` (R only); `dotnet sln list`; grep old paths→0; no dangling `ProjectReference` | clean | missed relative path | Exec lead | Arch |
| G5 Docs | link-check; one home per class | no broken links | duplicate/orphan doc | Docs | Arch |
| G6 No orphan moves | `git status` shows only R + intended edits; every file at P1-3 target | clean | file landed wrong home | Exec lead | Arch |
| G7 No secrets | `git ls-files \| grep -Ei '\.env' \| grep -vi example` → EMPTY; rotated | empty | secret re-committed | DevOps | DevOps |
| G8 Git clean | `git status --porcelain` → EMPTY after build | empty | untracked/modified leak | Exec lead | Dev |
| G9 Structure | root = P1-3 §4 allowlist; top-level dirs = target map | match | stray root file | Exec lead | Arch |
| G10 No file loss | `git ls-files \| wc -l` reconciles vs baseline minus intended removals | reconciled | unintended deletion | Exec lead | Release |
| G11 History | `git log --follow -- <moved-file>` traces rename | traces | `git mv` not used | Exec lead | Arch |
| G12 Line endings | fresh `git status` shows no `LF will be replaced by CRLF` | no warnings | `.gitattributes` missing/renormalize skipped | Exec lead | Dev |

---

## 10. Incident Response (procedure per incident)

| Incident | Procedure |
|---|---|
| **Build failure** | STOP the wave. `git status` to see if only renames + intended edits. Fix references (sln/ProjectReference/imports). If unfixable in-stage → `git revert <sub-stage>` or `git reset --hard p1-4/wave3<prev>-done`; re-plan. Never force-push. |
| **Test failure** | Compare pass count to `p1-4/wave0-baseline`. If regression tied to a move → fix path; if test itself references old path → update test path (not test logic). If real product bug → OUT OF SCOPE, file ticket, do not fix in-wave. |
| **Broken references** | `grep -rn '<old-path>' --include=*.csproj --include=*.sln .` to enumerate; fix each `ProjectReference`/`HintPath`. Re-run G4. |
| **Missing files** | `git ls-files \| wc -l` vs baseline (G10). If a needed file vanished → `git checkout p1-4/wave0-baseline -- <file>` to restore; investigate why it was untracked/ignored. |
| **Unexpected Git changes** | `git diff --stat` to scope. If a commit bundled unrelated work (violates G-P4) → `git reset --soft HEAD~1`, split into per-concern commits. |
| **Failed rollback** | If `git revert` conflicts → `git reset --hard <last-green-tag>` (local only, pre-share). If already shared → `git revert -m 1` the merge; escalate to Release. |
| **Secret exposure** | If a secret re-committed → immediately `git rm --cached` + re-ignore + re-rotate; open incident to DevOps; schedule history scrub (W1-S4). Do NOT proceed. |
| **Merge conflict** | Current tree has 0 conflicts (verified §0). If one appears mid-wave → abort the operation, `git merge --abort`, return to last green tag, re-base plan via merge (never rebase the migration branch — P1-4 §7). |
| **Path mismatch** | A file landed outside its P1-3 §10 target → `git mv` to correct home; re-run G6/G9. |
| **Unexpected generated files** | New `bin/obj/artifacts` appear tracked → confirm `.gitignore` (W2-S7) covers them; `git rm --cached -r` the path; commit ignore fix. |

---

## 11. Rollback Playbook (per wave)

| Wave | Trigger | Scope | Commands | Validate after | Approver before retry |
|---|---|---|---|---|---|
| 0 | tree unstable / unexplained staged | whole wave | `git reset --hard p1-4/pre-stabilization` | `git status` clean; `git clean -nxd` dry-run reviewed | Arch |
| 1 | secret untracked pre-rotation / wrong branch prune | secrets or branches | `git checkout p1-4/wave0-baseline -- <file>`; restore branch from reflog/`archive/<name>` | `git ls-files` secret empty; branch list restored | DevOps |
| 2 | regen proves item was needed | per group | `git revert <group-commit>` | harness rebuild regenerates item | Exec lead |
| 3 | broken build/post-move | sub-stage | `git revert <sub-stage>` or `git reset --hard p1-4/wave3<prev>-done` | harness green; G4 clean | Arch |
| 4 | wrong doc move | doc commit | `git revert <doc-commit>` | link-check pass | Docs |
| 5 | wrong archive move | archive commit | `git revert <archive-commit>` | root allowlist holds | Docs |
| 6 | gate fail | offending wave | revert offending wave commit | last green tag restored | QA |
| 7 | cert rejected | merge | `git revert -m 1 <merge>` | tree at `wave6-validated` | Release |

**Hard rule (P1-4 §6):** REMOVE-from-git dispositions untrack but keep on disk during the program; physical deletion is a separate approved post-certification operation. DB backups relocated NEVER deleted.

---

## 12. Communication Checklist

- **Before execution:** announce freeze start/end, re-clone requirement (post-scrub), new path map (`src/`, `tests/`, …) to Developers; secret-rotation ask to DevOps; sign-off collection to Release (P1-4 §11).
- **During execution:** daily status in Waves 0/3 (highest load); wave-boundary summary otherwise; 48h notice before history scrub.
- **After each wave:** wave-boundary summary; tag pushed; gate status in Execution Log.
- **Before certification:** validation report circulated; all 5 sign-offs requested.
- **After certification:** announce freeze lift; publish `p1-4/migration-complete`; communicate new canonical paths + archive location.

---

## 13. Final Acceptance Checklist (P1-4 §9)

- [ ] **Repository structure** — root = P1-3 §4 allowlist only; top-level dirs = target map (G9).
- [ ] **Root policy** — exactly the allowed files/dirs present; no loose reports/scripts/SQL (G9).
- [ ] **Build** — G1 green on restructured tree, identical to baseline (G1/G3).
- [ ] **Tests** — G2 pass count == baseline pass count.
- [ ] **Documentation** — `docs/` by class; no broken links; no cross-root duplicate (G5).
- [ ] **Secrets** — 0 tracked non-example `.env` (G7) AND rotated AND history scrubbed.
- [ ] **Git status** — `git status --porcelain` EMPTY after build (G8).
- [ ] **History preservation** — `git log --follow` traces a moved file (G11).
- [ ] **Archive verification** — `archive/{reports,db,scripts,scratch-projects,recovery}/` populated; DB backups relocated not deleted (G8-adjacent).
- [ ] **Blueprint compliance** — every P1-3 §10 matrix row landed at target or gated archive; no orphan moves (G6); `git ls-files` reconciles vs baseline minus intended removals (G10).

---

## 14. Out-of-Scope (P1-4 §10 — explicitly NOT done by this runbook)

Business-logic refactoring/bug-fixing; architecture redesign of the running system; feature implementation; DB schema redesign (migrations relocated, not re-authored); frontend redesign/dependency upgrades; performance tuning; authoring full CI beyond the optional build+test seed; physical deletion of REMOVE-classified assets (deferred post-certification); rewriting/curating CONTENT of historical reports (relocated as-is).

---

*End of P1-5 Repository Execution Runbook. Runbook only — no migration, cleanup, commits, or file movement was performed by this document. All blocker numbers were re-verified against the live tree on `recovery/3-greenfield-baseline`; the codex-obj count is corrected to 22 (live) vs the 25 cited in P1-4, and the B2 staged set is characterized as 351 deletions + 2 renames + Baseline_Recovery3 add = 354.*
