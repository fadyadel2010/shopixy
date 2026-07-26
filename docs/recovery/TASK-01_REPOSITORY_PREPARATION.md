# TASK-01 REPOSITORY PREPARATION — Sprint-0 Execution Readiness

Verification date: 2026-07-23
Source of truth: live `git` inspection of `C:\Users\fadya\source\repos\PointOfSale`
Scope: assessment and preparation ONLY. No commits, no branch creation, no stash,
no reset, no checkout, no migration generation, no snapshot generation, no model/persistence changes.
This document is itself the only repository change made for TASK-01 (a new untracked deliverable file).

---

# Executive Summary

The repository is NOT safe to launch Recovery execution from its current state. The three
Critical blockers imported from `PRE_EXECUTION_REMEDIATION.md` (B-01 dirty persistence worktree,
B-02 untracked recovery/audit evidence, B-03 dirty/irregular snapshot) are all still OPEN in
the live tree and were re-verified independently below.

However, the blockers are closable through ordinary preparation (commit/park in-flight work,
ratify docs, cut a clean dedicated branch). Nothing here indicates active corruption or an
unsafe irreversible operation already in flight — only that the tree must be cleaned and
ratified before RECOVERY-3 / the Recovery Program can begin.

Recommendation: **REPOSITORY PREPARATION REQUIRED**. The next task (TASK-02 or the approved
preparation action) should perform the safe repository actions identified in the
*Repository Preparation Plan* and then re-run this checklist.

---

# Current Repository State

## Branch
- Current branch: `feature/new-pr210`
- Tracking: `origin/feature/new-pr210` — "Your branch is up to date with 'origin/feature/new-pr210'".
- No in-progress merge/rebase detected.

## Working tree (raw `git status --porcelain`, 2055 entries total)
| Class | Count | Verification command |
|---|---|---|
| Staged (any first-column status) | 0 | `git diff --cached --name-only \| wc -l` → 0 |
| Unstaged modified/deleted (` M` / ` D`) | 814 | `git status --porcelain` filtered on second column M/D |
| Untracked (`??`) | 1241 | `git status --porcelain` filtered on `??` |
| **Total porcelain entries** | **2055** | `git status --porcelain \| wc -l` |

### Modified (unstaged) — dominant areas
The 814 modified files are overwhelmingly application/feature code, NOT recovery-specific:
- `PointOfSale.API/**` (controllers, DTOs, services, middleware) — large share
- `PointOfSale.Web/**` (React/JSX/TS) — large share
- `PointOfSale.CommerceHub/**` (TS/TSX)
- `PointOfSale.Tests/**`
- `PointOfSale.WhatsApp.Baileys/**`
- `PointOfSale.Infrastructure/**` + `PointOfSale.Core/**` — persistence (see B-01)
- Root/doc files: `.ai-context/*.md`, `PHASE-2-ANALYTICS-SDK.md`, `DB/New Text Document.txt` (deleted)

### Deleted (unstaged)
- `DB/New Text Document.txt`
- `PointOfSale.API/Controllers/HomepageSectionsController.cs`
- (and other deletions counted within the 814)

### Untracked (`??`) — top-level distribution
| Top-level path | Untracked entries |
|---|---|
| docs | 526 |
| PointOfSale.Tests | 410 |
| PointOfSale.Web | 101 |
| PointOfSale.API | 84 |
| PointOfSale.Infrastructure | 69 |
| PointOfSale.Core | 23 |
| PointOfSale.CommerceHub | 9 |
| DB | 6 |
| root `.md` artifacts (TIKTOK-*, TRACKING_PLATFORM_*, MIGRATION_AUDIT_REPORT.md, AFFILIATE_CROSS_DOMAIN_BOUNDARIES.md, REVERSE_ENGINEERED_*, PAYMENT_RUNTIME_*.md) | 8 |
| PointOfSale.Storefront | 1 |
| `.ai`, `.hermes`, `.md` | a few |

Note: `docs/` being 526 untracked entries confirms `docs/recovery` and `docs/audits`
are themselves untracked (B-02).

## Staged files
- **None.** `git diff --cached --name-only` returns empty. All changes are unstaged or untracked.

## Recovery documents (`docs/recovery/`)
Present (5):
1. `PRE_EXECUTION_REMEDIATION.md` — blocker-closure report (imports blockers from readiness review)
2. `PRE_RECOVERY_EXECUTION_READINESS_REVIEW.md` — source of the blocker inventory
3. `RECOVERY-1_CANONICAL_MODEL_LOCK.md`
4. `RECOVERY-2_SNAPSHOT_BASELINE_STRATEGY.md`
5. `RECOVERY-3_GREENFIELD_EXECUTION_PLAN.md`

**Missing (documented as required):**
- `RECOVERY-3_EXECUTION_LOG.md` — required by B-05 and referenced throughout RECOVERY-3; `Test-Path` returns absent.

## Audit documents (`docs/audits/`)
Present (5):
1. `MIGRATION_CANONICALIZATION_AUDIT.md`
2. `MIGRATION_COMPLETENESS_AUDIT.md`
3. `MIGRATION_REPLAY_FEASIBILITY_AUDIT.md`
4. `SCHEMA_DRIFT_AUDIT_REPORT_V2.md`
5. `SCHEMA_DRIFT_AUDIT_REPORT.md`

No referenced-but-missing audit file detected from the documents reviewed.

---

# Branch Assessment

- Dedicated execution branch: **does NOT exist.**
  `git branch -a | grep -i recovery` returns nothing.
  No `recovery/*`, `sprint-0/*`, or equivalent branch is present locally or remotely.
- The only active branch is the feature branch `feature/new-pr210`, whose HEAD is clean vs origin
  but whose working tree is dirty with 2055 uncommitted entries.
- Blocker B-01 (in `PRE_EXECUTION_REMEDIATION.md`) prescribes cutting the baseline from an AGREED
  base SHA **only after the status is clean**: branch name `recovery/3-greenfield-baseline`.

### Recommended exact branch-creation command (for the next approved task)
Do NOT run during TASK-01. Run only after B-01..B-03 are closed and the base SHA is agreed:

    git checkout -b recovery/3-greenfield-baseline <AGREED_BASE_SHA>

Where `<AGREED_BASE_SHA>` must be the SHA selected by the Release Engineer as the stable
persistence base (current `feature/new-pr210` HEAD is NOT a valid base while the tree is dirty
and contains 277 unrelated persistence modifications).

---

# Working Tree Assessment

The working tree is **NOT suitable** for Recovery execution. Exactly why:

1. **B-01 Dirty persistence worktree (Critical, OPEN)**
   `git status --porcelain -- PointOfSale.Infrastructure PointOfSale.Core` → **277 entries**.
   A baseline cut from this tree would inherit unreviewed persistence changes and corrupt
   attribution. Recovery MUST start from a stable persistence base.

2. **B-02 Recovery evidence not committed (Critical, OPEN)**
   `git status --porcelain -- docs/recovery docs/audits` → `?? docs/audits/`, `?? docs/recovery/`.
   The approval/audit chain is not reviewable source truth while untracked.

3. **B-03 Dirty, irregular snapshot (Critical, OPEN)**
   `git status --porcelain -- .../ApplicationDbContextModelSnapshot.cs` → ` M` (modified).
   Live facts: 18,859 lines; 0 `CarrierAccount` hits; 0 `PaidBy` hits.
   The snapshot is stale/irregular relative to the runtime model and must NOT be used as a differ
   baseline. (See also the data-quality note below about its own documentation.)

4. **B-04 Missing differ harness (Critical, OPEN)** — `tools/Recovery3.DifferHarness` absent.
5. **B-05 Missing execution log (Critical, OPEN)** — `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` absent.

Additionally re-verified as still open from the readiness review: B-06 guardrail contradiction,
B-07 PowerShell/Unix command mismatch, B-08 EF-discoverability gaps, B-09 undefined CI path,
B-10 obsolete `dotnet ef migrations add InitialCreate` in setup scripts, B-11 factual doc errors,
B-12..B-15 environmental/prereq proofs.

### Data-quality note (internal inconsistency in the remediation doc)
`PRE_EXECUTION_REMEDIATION.md` states the snapshot "line count is 13,564" (and again "this pass
observed 13,564 lines"). The live file is **18,859 lines** (`wc -l`). This is a factual error
inside the blocker-closure report itself and should be corrected as part of B-11 (factual doc
corrections) before these documents are ratified as source truth.

### What is NOT wrong
- No staged changes to lose.
- Branch HEAD is consistent with origin (no half-merged state).
- No EF command has been run; no snapshot/model mutation has occurred for TASK-01.

---

# Recovery Document Assessment

Question: are `docs/recovery` and `docs/audits` complete enough to become committed source truth?

| Set | Verdict | Missing / gaps |
|---|---|---|
| `docs/recovery/` | **Not yet** | `RECOVERY-3_EXECUTION_LOG.md` is required by B-05 but absent. The 5 present docs are coherent and cross-reference correctly, but the execution-evidence trail they depend on does not exist. |
| `docs/audits/` | **Present / appears complete** | All 5 audit files exist; no referenced-but-missing audit artifact detected. Content depth was not exhaustively graded, but presence and internal cross-links are intact. |

Additional gating issue for ratification: B-11 — the recovery docs contain source-inaccurate
wording (e.g. snapshot line count 13,564 vs actual 18,859; migration SQL count 190 vs scoped 97;
config count 108 vs 123; "snapshot frozen at identical point" vs dirty). These should be corrected
before committing the docs as authoritative source truth.

---

# Audit Document Assessment

- 5 audit documents present under `docs/audits/`.
- They cover: migration canonicalization, migration completeness, migration replay feasibility,
  and two schema-drift reports (V1 and V2).
- No missing referenced file detected.
- They are currently untracked (B-02) and therefore not yet reviewable source truth.
- Recommendation: ratify (commit) after B-11 wording corrections and after the working tree is
  clean enough that the commit contains only the evidence docs, not unrelated feature code.

---

# Repository Preparation Plan

## Classification of current work
- **Belongs to the Recovery Program (blocks execution):**
  - Persistence worktree dirtiness: `PointOfSale.Infrastructure/**` + `PointOfSale.Core/**` (277 entries).
  - Dirty snapshot: `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`.
  - Untracked recovery/audit evidence: `docs/recovery/**`, `docs/audits/**`.
  - Missing recovery tools/docs: `tools/Recovery3.DifferHarness`, `docs/recovery/RECOVERY-3_EXECUTION_LOG.md`.
- **Unrelated to Recovery (feature/new-pr210 in-flight work — MUST be preserved, not destroyed):**
  - The ~814 modified application files (API controllers/services, Web, CommerceHub, Tests, WhatsApp).
  - The bulk of the 1241 untracked entries (app/test/web/docs not related to recovery).
  - Root `.md` artifacts and `DB/` scratch files.

## What must be committed
- The recovery/audit evidence documents (`docs/recovery/**`, `docs/audits/**`) — once B-11 wording
  is corrected — as a dedicated "ratify recovery evidence" commit on the recovery branch.
- The recovery differ-harness tool (`tools/Recovery3.DifferHarness`) and execution log
  (`RECOVERY-3_EXECUTION_LOG.md`) once produced (B-04/B-05).

## What must be stashed / parked
- The unrelated feature work (814 modified + most untracked app/web/test files) must NOT be mixed
  into the recovery branch. Safe options (to be executed by the next task, not here):
  - Stash unrelated changes before cutting the recovery branch, OR
  - Commit them to `feature/new-pr210` (or a parked `feature/new-pr210-wip` branch) so the recovery
    branch can be cut from a clean base.

## What must move to another branch
- All non-recovery in-flight changes should live on `feature/new-pr210` (or a WIP parking branch),
  never on `recovery/3-greenfield-baseline`.

## Safety judgement
- Committing/parking/stashing the above is **safe** and is the intended closure path for B-01/B-02.
- These actions are explicitly deferred to the next task per the TASK-01 quality bar
  ("Do NOT perform those actions automatically unless they are completely safe" — and even safe
  actions are reserved for the approved preparation step).

---

# Repository Readiness Checklist

Every item is independently verifiable from a clean shell. All items below are currently FAILING
unless marked PASS.

| # | Item | Verify with | Current result |
|---|---|---|---|
| 1 | Persistence worktree clean | `git status --porcelain -- PointOfSale.Infrastructure PointOfSale.Core \| wc -l` → 0 | **FAIL (277)** |
| 2 | No staged changes to lose | `git diff --cached --name-only \| wc -l` → 0 | PASS (0) |
| 3 | Recovery docs tracked/committed | `git status --porcelain -- docs/recovery \| wc -l` → 0 (after commit) | **FAIL (untracked)** |
| 4 | Audit docs tracked/committed | `git status --porcelain -- docs/audits \| wc -l` → 0 (after commit) | **FAIL (untracked)** |
| 5 | Snapshot not modified | `git status --porcelain -- .../ApplicationDbContextModelSnapshot.cs` empty (pre-S08) | **FAIL ( M)** |
| 6 | Differ harness exists | `Test-Path tools/Recovery3.DifferHarness` → True | **FAIL (absent)** |
| 7 | Execution log exists | `Test-Path docs/recovery/RECOVERY-3_EXECUTION_LOG.md` → True | **FAIL (absent)** |
| 8 | Dedicated recovery branch exists | `git branch -a \| grep -i recovery` non-empty | **FAIL (none)** |
| 9 | Guardrail compatible with baseline shape | RECOVERY-3 S-07 excludes snapshot from Designer rule | **FAIL (B-06)** |
| 10 | Runbook shell-native | RECOVERY-3 has PowerShell form for every command | **FAIL (B-07)** |
| 11 | EF-discoverability gates present | `dotnet ef migrations list --no-connect` in pre/archive/post steps | **FAIL (B-08)** |
| 12 | CI/merge-blocking path defined | named CI file or external job | **FAIL (B-09)** |
| 13 | Setup scripts safe | `rg "migrations add InitialCreate" setup-database.*` → empty | **FAIL (B-10)** |
| 14 | Recovery doc facts accurate | snapshot lines = actual 18,859; counts match source | **FAIL (B-11)** |
| 15 | Build + design-time proven on clean branch | `dotnet build` + `dotnet ef dbcontext info` exit 0, logged | **FAIL (B-12)** |
| 16 | Scratch DB proven disposable/empty | version() + empty schema logged | **FAIL (B-13)** |
| 17 | No real connection string reachable | process env + appsettings scan clean | PARTIAL (B-14) |
| 18 | Backup/archive bundle exists | `git bundle verify` + copy hashes | **FAIL (B-15)** |

---

# Next Actions

(For the approved follow-up task — NOT executed in TASK-01.)

1. Close B-01: park/commit unrelated feature work off the recovery path; cut
   `recovery/3-greenfield-baseline` from the agreed clean base SHA.
2. Close B-02: commit `docs/recovery/**` + `docs/audits/**` as ratified evidence.
3. Close B-03: do NOT hand-edit snapshot; archive current migrations (S-05) and let EF emit a new
   tool-generated snapshot after the old chain is retired.
4. Close B-04: add and commit `tools/Recovery3.DifferHarness`; confirm `dotnet build` + zero-diff.
5. Close B-05: create and commit `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` before S-01.
6. Close B-06..B-11: correct guardrail wording, PowerShell runbook, EF-discoverability gates,
   CI path, setup scripts, and factual doc errors (including the 13,564-vs-18,859 snapshot line count).
7. Close B-12..B-15: re-run build/design-time, scratch-DB, environment-isolation, and backup proofs
   from the clean branch and record them in the execution log.
8. Re-run this readiness checklist; only proceed to RECOVERY-3 when all 18 items PASS.

---

# Final Certification

**REPOSITORY PREPARATION REQUIRED**

Rationale: Critical blockers B-01, B-02, and B-03 are open and independently re-verified in the
live tree. The repository is not safe to begin Recovery execution from `feature/new-pr210` as-is.
The blockers are closable through standard, non-destructive preparation (park in-flight feature
work, ratify evidence docs, cut a clean `recovery/3-greenfield-baseline` branch, add the missing
harness/log, correct doc facts). No irreversible or destructive action is currently in progress,
so the stronger "NOT SAFE TO PROCEED" certification is not warranted — but execution MUST NOT start
until the above readiness checklist is fully green.
