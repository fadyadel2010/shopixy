# BATCH-A1 — Atomic Baseline Commit Execution Plan
## Shopixy Repository Modernization Program · Batch A

**Artifact type:** EXECUTION PLAN (read-only). No commits, no staging, no file edits performed.
**Branch:** `recovery/3-greenfield-baseline`
**Baseline migration:** `20260723214642_Baseline_Recovery3`
**Authoritative source of truth:** LIVE `git` working tree + the 3 real Migrations files — NOT the prior markdown (GATE-A1 was re-verified against the real files in this plan; see §4 evidence).

---

## 0. HEADLINE ANSWER

**Can the baseline be committed safely as a single atomic Git commit? → YES, with one mandatory constraint.**

It is safe *only if* staging is restricted to the exact path:

    git add PointOfSale.Infrastructure/Migrations/

A dry-run (`git add -n PointOfSale.Infrastructure/Migrations/`) proves this path resolves to **exactly 356 file operations** (353 deletions + 1 snapshot modification + 2 new baseline files) and **zero** leakage of unrelated files.

It is **NOT** safe to use `git add PointOfSale.Infrastructure/`, `git add .`, or `git add -A` — those would drag in 163 unrelated dirty files (incl. `ApplicationDbContext.cs` and `Data/Configurations/*`) and 199 untracked files from the archive dir. See §7 RISK.

---

## 1. STEP 1 — File Classification for THIS Commit

The baseline commit is a **semantic reset of the EF Core migration timeline**, not a general code change. Its entire footprint is the `PointOfSale.Infrastructure/Migrations/` folder.

### REQUIRED (must be in the commit)
| File | Status (live) | Why required |
|---|---|---|
| `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs` | `??` untracked | The new greenfield "create-all" migration (Up = 256 CreateTable). Without it, EF has no migration to apply. |
| `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.Designer.cs` | `??` untracked | Designer snapshot carrying the target model + `[Migration("20260723214642_Baseline_Recovery3")]` attribute. EF requires the Designer file to resolve the migration. |
| `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | `M` modified | Must be committed **with** the migration that produced it. Diff is a pure EF-9 re-scaffold (`ToTable("X", (string)null)` → `ToTable("X")`, +10661/−5266 lines). Committed alone, the snapshot would describe a model no migration matches. |
| 353 deleted old-chain files under `PointOfSale.Infrastructure/Migrations/` | `D` | The historical 149-migration chain + `Sql/` helper scripts + 2 READMEs. Their deletion is what collapses the chain to the single baseline. Omitting the deletions leaves orphaned, unreferenced migration classes in git. |

### OPTIONAL (not needed for this commit)
| Item | Reason |
|---|---|
| The archive relocation `docs/recovery/archive/RECOVERY-4_PHASE_A/.../Migrations/` | It is the *destination* of the history (preserved for audit). It must be committed **separately, later** (or is already intended as its own commit). It is currently `??` and must NOT ride along with the baseline. |

### FORBIDDEN (must NOT enter this commit)
| File group | Live status | Reason |
|---|---|---|
| `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` | `M` | Broader recovery refactor; not part of the baseline. |
| `PointOfSale.Infrastructure/Data/Configurations/*` (16+ dirty) | `M` | Domain model configs being hardened separately; out of scope. |
| `PointOfSale.Infrastructure/Accounting/...`, `Consumers/...`, `Analytics/...` (163 dirty total excl. Migrations) | `M` | Unrelated recovery work. |
| `PointOfSale.API/...` (~200 controllers dirty), `PointOfSale.Tests/...`, `.ai-context/*.md` | `M` | Separate batches. |
| `docs/recovery/archive/...` (199 untracked .cs) | `??` | Archive commit only. |
| Any other untracked file in the tree (1232 total) | `??` | Later batches / ignore. |

---

## 2. STEP 2 — Dirty-File Audit (every dirty file accounted for)

Live counts from the working tree at plan time:

- **Total modified (tracked, `M`):** 827
- **Total untracked (`??`):** 1232
- **Total staged:** 0

### Disposition of the 827 modified files
| Bucket | Count | Disposition |
|---|---|---|
| `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | 1 | **BASELINE COMMIT** |
| `PointOfSale.Infrastructure/` excl. Migrations (Accounting, Consumers, Analytics, Data/ApplicationDbContext, Data/Configurations, …) | 163 | **LATER BATCH** |
| `PointOfSale.API/` (controllers, services, middleware) | ~200 | **LATER BATCH** |
| `PointOfSale.Tests/` (architecture, security, certification) | ~180 | **LATER BATCH** |
| `.ai-context/*.md` + root `*.md` (PHASE-2, etc.) | ~15 | **LATER BATCH** / docs |
| Remaining (Core, EventBackbone, tools, etc.) | rest | **LATER BATCH** |

### Disposition of the 1232 untracked files
| Bucket | Count | Disposition |
|---|---|---|
| `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs` + `.Designer.cs` | 2 | **BASELINE COMMIT** |
| `docs/recovery/archive/RECOVERY-4_PHASE_A/.../Migrations/` (199 .cs + 49 .Designer + 1 snapshot) | 249 | **LATER BATCH** (archive commit) |
| Other untracked (new feature files, generated, etc.) | ~981 | **LATER BATCH** / ignore |

**Conclusion:** No unrelated file is forced into the baseline commit by the working tree; isolation is achieved purely by path-scoped staging (§3).

---

## 3. STEP 3 — Exact Staging Order

Staging is path-scoped. Order within the single path does not affect the atomic commit, but the recommended sequence makes intent auditable:

1. `git add PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs`
2. `git add PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.Designer.cs`
3. `git add PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
4. `git add PointOfSale.Infrastructure/Migrations/`  ← stages all 353 `D` deletions of the old chain

**Single-command equivalent (preferred, deterministic):**
    git add PointOfSale.Infrastructure/Migrations/

**Verification before commit (mandatory guard):**
    git status --short PointOfSale.Infrastructure/Migrations/   # expect: 353 D, 1 M→staged, 2 ?? →staged
    git status --short                                          # expect: ONLY Migrations/ entries staged; everything else still unstaged
    git diff --cached --stat                                    # expect: 356 ops, ~35k lines, exclusively under .../Migrations/

If `git diff --cached --stat` shows **any** path outside `.../Migrations/`, abort and re-stage with the explicit path form above (do NOT use `git add -A` or `git add .`).

---

## 4. STEP 4 — Exact Commit Contents (and why each belongs)

**Commit message (suggested):**
```
chore(migrations): collapse EF chain to Baseline_Recovery3 greenfield baseline

Replace the 149-migration historical chain with a single generated
greenfield baseline (256 CreateTable / 256 DropTable / 10 InsertData).
Regenerated ApplicationDbContextModelSnapshot is committed together
with the migration that produced it. Historical chain relocated (not
destroyed) to docs/recovery/archive/RECOVERY-4_PHASE_A/.../Migrations/.

Verification: snapshot BuildModel body is byte-identical (0-diff) to
baseline BuildTargetModel body; ProductVersion 9.0.1 aligned.
```

**Contents (356 file operations):**
- ADD `20260723214642_Baseline_Recovery3.cs` — the migration.
- ADD `20260723214642_Baseline_Recovery3.Designer.cs` — its designer.
- MODIFY `ApplicationDbContextModelSnapshot.cs` — re-scaffolded to match.
- DELETE ×353 — old chain `.cs`/`.Designer.cs`, `Sql/` scripts, and 2 READMEs.

**Why every file belongs (the invariant):** A baseline migration is meaningless unless the snapshot that pins its target model is committed in the *same* commit. The 353 deletions are the other half of the same semantic operation (collapse). Committing fewer than all four groups leaves the repo in an internally inconsistent EF state (orphaned migrations, or snapshot-without-migration). Committing more (the forbidden groups in §1) pollutes the baseline with unrelated recovery work and breaks atomicity/bisectability.

### EVIDENCE RE-VERIFIED AGAINST REAL FILES (this plan, read-only)
- `Baseline_Recovery3` class: `public partial class Baseline_Recovery3 : Migration` (line 12, `.cs`).
- Designer id: `[Migration("20260723214642_Baseline_Recovery3")]` (line 15) — **matches filename**.
- Snapshot `ProductVersion`: `9.0.1` (line 20) — **aligned** with EF Design/Tools 9.0.1.
- Baseline shape: `CreateTable` = **256**, `DropTable` = **256**, `InsertData` = **10**, raw `migrationBuilder.Sql` = **0**.
- **Snapshot⇄baseline model body diff = 0 lines** (real Python difflib of `BuildModel` body vs `BuildTargetModel` body, both 23,132 lines). The baseline is genuinely generated from the current model.
- Archive relocation is REAL, not destruction: `docs/recovery/archive/RECOVERY-4_PHASE_A/PointOfSale.Infrastructure/Migrations/` contains 199 `.cs`, 49 `.Designer.cs`, 1 snapshot copy (verified present on disk).

---

## 5. STEP 5 — Rollback Strategy (if commit is aborted / reverted)

The plan is read-only; nothing is committed yet. If the commit is made and later must be undone:

- **Soft undo (keep changes in tree):** `git reset --soft HEAD~1` → all 356 file operations return to unstaged/dirty state exactly as they are now. Nothing is lost; the working tree is unchanged from pre-commit.
- **Hard undo (restore committed tree):** `git revert <sha>` (preferred for shared history) or `git reset --hard HEAD~1` (only if not pushed). Note `--hard` would also discard the *other* 827 dirty files if they were somehow committed — another reason to keep staging path-scoped.

**What remains unchanged if we abort now:** the entire working tree (all 827 `M`, 1232 `??`) is untouched; the repo is exactly as observed in §2. No migration, snapshot, or archive file is modified by this plan.

---

## 6. STEP 6 — Post-Commit Verification (run AFTER the commit, by the executor)

1. `git status` — confirm clean staging area; only the baseline files are now committed; the 827/1232 other dirty files remain as before (untouched).
2. `git log --oneline -1` — confirm the single baseline commit with the message from §4.
3. `git show --stat HEAD` — confirm 356 ops, all under `PointOfSale.Infrastructure/Migrations/`, nothing else.
4. **Migration chain:** `git ls-files PointOfSale.Infrastructure/Migrations/ | grep -v Snapshot` → exactly 2 files (`Baseline_Recovery3.cs`, `Baseline_Recovery3.Designer.cs`).
5. **Snapshot consistency (C-2, idempotent scaffold):**
       dotnet ef migrations add __verify --project PointOfSale.Infrastructure --startup-project PointOfSale.API
   Expect an **empty** migration (no operations) → proves model ⇄ snapshot ⇄ baseline aligned. Then `git checkout -- .` / delete `__verify` and do **not** commit it.
6. **Fresh-DB smoke (C-3):** `dotnet ef database update` against an empty PostgreSQL DB → all 256 tables + seed rows created; a second run is a no-op.
7. **Existing-DB reconciliation (C-4):** If any environment already carries the old `__EFMigrationsHistory`, mark `Baseline_Recovery3` applied **without** running `Up()` before deploy. If truly greenfield, state the waiver explicitly.

---

## 7. STEP 7 — Risk Review

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| R1 | **Broad `git add` drags in 163 unrelated Infrastructure files** (incl. `ApplicationDbContext.cs`, `Configurations/*`) | HIGH if path not scoped | Use **only** `git add PointOfSale.Infrastructure/Migrations/`. Guard with `git diff --cached --stat` (§3). |
| R2 | **`git add -A` / `git add .` pulls 199 untracked archive `.cs` files** (`docs/recovery/archive/...` is `??`) | HIGH if broad | Same as R1; never use `-A`/`.`. Archive is a separate later commit. |
| R3 | **Snapshot committed without its migration (or vice-versa)** | Medium | The 4-group invariant in §4; verify all 4 groups staged before commit. |
| R4 | **Existing/production DB already on old chain** rejects baseline `Up()` (R2 of GATE-A1) | High *if such DB exists* | C-4 reconciliation runbook (§6.7) before any deploy; greenfield → waiver. |
| R5 | **CI pinned to old migration ids** breaks | Medium | C-5: update pipelines to baseline; add "no pending model changes" gate (C-2 as CI check). |
| R6 | **Feature branches carrying old chain** conflict on merge | Medium | C-6: rebase onto baseline; forbid archived files re-entering `Migrations/`. |
| R7 | **`ApplicationDbContext.cs`/`Configurations` are dirty** — the committed snapshot may not match the *current* model after those land | Low–Med | The baseline is internally consistent *as committed* (0-diff proven). C-2 (§6.5) catches any drift introduced later; re-baseline then if needed. |

**Accidental-file-in-commit risk:** Eliminated by construction. The path `PointOfSale.Infrastructure/Migrations/` is a clean subtree:
- Only 1 `M` (snapshot) + 0 other modifications + exactly 2 `??` (baselines) live inside it.
- The 353 `D` are confined to it.
- `git add -n PointOfSale.Infrastructure/Migrations/` returns **356 operations, 0 outside the folder**.

---

## 8. SUCCESS CRITERION (final)

> **Can the baseline be committed safely as a single atomic Git commit?**

**YES — provided the executor stages exclusively via `git add PointOfSale.Infrastructure/Migrations/` (or the 4 explicit paths in §3) and confirms `git diff --cached --stat` shows nothing outside that folder.** The real files are verified consistent (0-diff snapshot⇄baseline, id/version aligned, archive preserved). The only real hazard is operator error via a broad `git add`, which is fully mitigated by the path-scoped staging guard.

This plan is read-only and does not perform the commit. Execution belongs to the Batch A-1 executor, who must satisfy §6 verification before marking the baseline promoted (C-8 of GATE-A1).
