# Executive Summary

This document is the pre-execution blocker-closure report for the Shopixy Persistence Recovery Program.

Scope is limited to the blockers identified by `docs/recovery/PRE_RECOVERY_EXECUTION_READINESS_REVIEW.md`. No new recovery phases, no new architecture, no migrations, no snapshots, no implementation, no database commands, and no changes to existing recovery documents were performed.

Source verification was performed on 2026-07-23 from `C:\Users\fadya\source\repos\PointOfSale`.

Result: execution is still blocked. The readiness review's Critical blockers remain open in the current source tree:

- Core/Infrastructure persistence worktree is dirty: 277 `git status --porcelain -- PointOfSale.Infrastructure PointOfSale.Core` entries.
- `docs/audits/` and `docs/recovery/` remain untracked.
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` remains modified and irregular.
- `tools/Recovery3.DifferHarness/` does not exist.
- `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` does not exist.
- RECOVERY-3 guardrail wording still conflicts with the intentional snapshot-without-Designer baseline artifact set.

Therefore RECOVERY-4 cannot begin. RECOVERY-3 execution also cannot begin from the current tree. The correct recommendation is fail-closed until the Critical blockers below are closed and re-verified from a clean dedicated recovery branch.

# Blocker Inventory

The definitive blocker list below is imported only from `PRE_RECOVERY_EXECUTION_READINESS_REVIEW.md`.

| ID | Severity | Blocker imported from readiness review |
|---|---|---|
| B-01 | Critical | Dirty persistence worktree. |
| B-02 | Critical | Recovery evidence not committed. |
| B-03 | Critical | Dirty, irregular snapshot. |
| B-04 | Critical | Missing differ harness. |
| B-05 | Critical | Missing execution log. |
| B-06 | Critical | Guardrail contradiction: every `*.cs` requires Designer, but snapshot intentionally has no Designer. |
| B-07 | High | RECOVERY-3 commands are Unix-oriented while execution shell is Windows PowerShell. |
| B-08 | High | EF-discoverability checks are insufficient compared with filename counts. |
| B-09 | High | CI or merge-blocking guardrail path is undefined. |
| B-10 | High | Old setup scripts still teach `dotnet ef migrations add InitialCreate`. |
| B-11 | High | RECOVERY-1/2/3 require factual wording corrections. |
| B-12 | Medium | Build and design-time model load must be re-run on a clean branch. |
| B-13 | Medium | Scratch PostgreSQL version and isolation must be verified. |
| B-14 | Medium | No real environment connection strings may be reachable from the execution shell. |
| B-15 | Low | Repository bundle and archive copies must exist before S-01. |

# Source Verification

| Blocker | Validation status | Source evidence |
|---|---|---|
| B-01 | Still Open | `git status --porcelain -- PointOfSale.Infrastructure PointOfSale.Core` returns 277 entries. Modified persistence files include `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`, configuration files, migration files, and `ApplicationDbContextModelSnapshot.cs`. |
| B-02 | Still Open | `git status --porcelain -- docs\audits docs\recovery` returns `?? docs/audits/` and `?? docs/recovery/`. |
| B-03 | Still Open | `git status --porcelain -- PointOfSale.Infrastructure\Migrations\ApplicationDbContextModelSnapshot.cs` returns modified. Current snapshot line count is 13,564 and `ProductVersion` is 9.0.1. Snapshot contains later entities including `ProviderDeliveryOutboxMessage`, `StripeWebhookReplayRecord`, and `PaymobWebhookReplayRecord`, but has 0 `CarrierAccount` hits and 0 `PaidBy` hits. Runtime source has `AffiliatePayoutRequest.PaidBy` and Carrier runtime DbSets/configuration. |
| B-04 | Still Open | `Test-Path tools\Recovery3.DifferHarness` returns `False`. |
| B-05 | Still Open | `Test-Path docs\recovery\RECOVERY-3_EXECUTION_LOG.md` returns `False`. |
| B-06 | Still Open | RECOVERY-3 S-07 says every `*.cs` under the new `Migrations/` folder must have a sibling `.Designer.cs`; RECOVERY-3 S-08 expects exactly three baseline files including `ApplicationDbContextModelSnapshot.cs`, which correctly has no Designer. |
| B-07 | Still Open | RECOVERY-3 uses `ls`, `grep`, `wc`, `find`, `sha256sum`, `bash`, and `dropdb`. Current shell from environment context is PowerShell. |
| B-08 | Partially Resolved | RECOVERY-3 includes `dotnet ef migrations list --no-connect` after archive and mentions source scans in the failure matrix, but the preconditions and guardrails still rely heavily on filename counts. Active source counts are 149 migration classes, 49 Designers, and 97 active `.sql` files; legacy `Data/Migrations` has 40 classes, 38 Designers, and 12 `.sql` files. |
| B-09 | Still Open | Root `.github` directory is absent. Filename search found no root CI pipeline equivalent. RECOVERY-3 says to wire guardrails into CI but does not define an existing pipeline target. |
| B-10 | Still Open | `setup-database.ps1:7` and `setup-database.bat:7` both call `dotnet ef migrations add InitialCreate --project PointOfSale.Infrastructure --startup-project PointOfSale.API --context ApplicationDbContext`. |
| B-11 | Still Open | RECOVERY-1 still states active migration SQL as 190 and configuration count as 108. RECOVERY-2 still says snapshot is frozen at the identical point and treats Designer-less files as post-freeze. RECOVERY-3 still says the plan is executable as written despite missing P-10/P-12 and Unix command assumptions. |
| B-12 | Still Open | `dotnet build` and `dotnet ef dbcontext info` were not run for this closure report because the tree is dirty and the quality bar prohibits mutation/build artifact churn. They must be run after B-01 is closed. |
| B-13 | Still Open | No scratch PostgreSQL connection string, version proof, empty schema proof, or isolation evidence exists in `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` because the log file is absent. |
| B-14 | Partially Resolved | Process-level `ASPNETCORE_ENVIRONMENT` and `ConnectionStrings__*` overrides were empty. `PointOfSale.API/appsettings.json` has an empty `ConnectionStrings:DefaultConnection`. Full closure still requires execution-shell proof and reachable-host validation immediately before RECOVERY-3 S-08+. |
| B-15 | Still Open | No repository bundle verification or external archive-copy evidence was found. RECOVERY-3 P-03 remains a future prerequisite. |

Additional source facts used for validation:

| Fact | Source-verified value |
|---|---:|
| Active migration classes, excluding Designers and snapshot | 149 |
| Active Designer files | 49 |
| Active migration `.sql` files | 97 |
| Legacy `Data/Migrations` classes | 40 |
| Legacy `Data/Migrations` Designers | 38 |
| Legacy `Data/Migrations` `.sql` files | 12 |
| `ApplicationDbContext` DbSets | 242 |
| Configuration files with `IEntityTypeConfiguration<>` | 123 |
| `.NET SDK` | 9.0.308 |
| `dotnet-ef` | 9.0.1 |
| EF Design package | 9.0.1 |
| Npgsql EF provider | 9.0.2 |
| Current branch | `feature/new-pr210` |
| History CSV and schema dump | `DB/data-1784830579292.csv` and `DB/schema-23-7-2026.sql` both present |

# Remediation Plan

| Blocker | Objective | Files | Exact change | Verification | Rollback | Risk | Owner | Completion criteria |
|---|---|---|---|---|---|---|---|---|
| B-01 | Start execution from a stable persistence base. | Git worktree; `PointOfSale.Core/**`; `PointOfSale.Infrastructure/**` | Commit, shelve, or move unrelated work outside the recovery branch. Cut `recovery/3-greenfield-baseline` from the agreed base SHA only after status is clean. | `git status --porcelain -- PointOfSale.Infrastructure PointOfSale.Core` returns no output. | Restore parked branch/stash or delete the recovery branch. | Critical: cutting baseline from dirty model corrupts attribution. | Release Engineer | Clean dedicated branch recorded in execution log. |
| B-02 | Ratify recovery evidence as source truth. | `docs/audits/**`; `docs/recovery/**` | Commit the completed audit/recovery docs before RECOVERY-3 execution. | `git status --porcelain -- docs\audits docs\recovery` shows no untracked docs after commit. | Revert doc commit if ratification is rejected. | Critical: untracked approval chain is not reviewable source truth. | Release Engineer | Recovery documents are committed and referenced by SHA. |
| B-03 | Remove reliance on dirty snapshot as an execution fact. | `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | Do not hand-edit. Park/commit current unrelated snapshot changes before execution; archive current migrations in S-05; allow EF tooling to emit the new snapshot only after old chain is retired. Correct docs to describe current snapshot as dirty/irregular. | Before S-08, no active snapshot remains after archive. After S-08, new snapshot contains Carrier entities and `PaidBy`; `has-pending-model-changes` reports no changes. | Reset to pre-S-05 commit. | Critical: stale or manually advanced snapshot poisons the differ. | Principal EF Core Architect | Current dirty snapshot is not used as baseline; new snapshot is tool-generated. |
| B-04 | Provide independent runtime/snapshot differ proof. | `tools/Recovery3.DifferHarness/**` | Add the console harness described by RECOVERY-3 Baseline Generation Plan section 4. It must use EF services to compare `IMigrationsAssembly.ModelSnapshot` with the runtime design-time model and print operation count. | `dotnet build tools/Recovery3.DifferHarness` succeeds; S-09 output is `diff count: 0`. | Delete/revert harness commit. | Critical: single EF CLI check is insufficient evidence discipline. | Principal EF Core Architect | Harness source committed, builds, and produces zero-diff output after baseline. |
| B-05 | Create immutable execution evidence trail. | `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` | Add the log before S-01 with sections for preconditions, step commands, output hashes, SHAs, operator initials, and stop decisions. | File exists and is committed before S-01; every S-01..S-14 step appends evidence. | Revert log commit before execution starts. | Critical: no audit trail for irreversible merge gate. | Release Engineer | Execution log exists before S-01 and is updated at each gate. |
| B-06 | Make guardrails compatible with intended baseline artifact set. | RECOVERY-3 guardrail spec; `tools/recovery/check-migrations-guardrails.*` | Change rule from "every `*.cs` has a Designer" to "every migration class excluding `ApplicationDbContextModelSnapshot.cs` has a sibling `.Designer.cs`; exactly one model snapshot must exist." | Positive run passes with baseline `.cs`, `.Designer.cs`, and snapshot. Negative run fails for a dummy Designer-less migration class. | Revert guardrail commit. | Critical: incorrect guardrail blocks the approved baseline shape. | DevOps Engineer | Guardrail accepts exactly the intended three-file baseline and rejects Designer-less migrations. |
| B-07 | Make RECOVERY-3 executable in the actual shell. | `docs/recovery/RECOVERY-3_GREENFIELD_EXECUTION_PLAN.md`; guardrail scripts | Replace or supplement Unix commands with PowerShell-native commands, or explicitly require Git Bash/WSL and prove availability. Preferred correction is PowerShell-native because current shell is PowerShell. | Each precondition command has a tested PowerShell form: counts via `Get-ChildItem`/`Measure-Object`, hashes via `Get-FileHash`, diffs via `Compare-Object`, file moves via `git mv`, scratch cleanup via PostgreSQL tooling available on Windows. | Revert runbook correction commit. | High: ambiguous shell commands cause operator error. | DevOps Engineer | RECOVERY-3 contains zero ambiguous shell assumptions for this environment. |
| B-08 | Verify EF-discoverable migrations, not only filenames. | RECOVERY-3 preconditions, S-04/S-06/S-13, guardrail script | Add pre/post `dotnet ef migrations list --no-connect` evidence. Add source scans for `[Migration]` and `[DbContext(typeof(ApplicationDbContext))]` outside the intended active baseline folder after archive. State that legacy `Data/Migrations` is compiled until moved/excluded. | Pre-archive inventory includes filename and attribute/discoverability counts. Post-archive `migrations list --no-connect` is empty. Post-baseline it lists exactly one baseline migration. | Revert guardrail/runbook commit. | High: filename counts can miss compiled migration metadata. | Principal EF Core Architect | EF discovery proof is recorded before archive, after archive, and after baseline. |
| B-09 | Define the merge-blocking guardrail runner. | CI config or documented external CI system | If repository CI is used, add a real workflow file. If external CI is authoritative, RECOVERY-3 must name it, link the job, list exact commands, and define merge blocking. Vague "wire into CI" wording is not sufficient. | CI job exists and runs `dotnet build`, `has-pending-model-changes`, differ harness, and migration Designer/snapshot guardrails. | Revert CI config or disable external job through release approval. | High: guardrails not merge-blocking allow drift recurrence. | Principal DevOps Engineer | Guardrail path is observable and required before merge. |
| B-10 | Remove obsolete migration-generation instructions. | `setup-database.ps1`; `setup-database.bat`; any deployment docs that reference them | Replace `dotnet ef migrations add InitialCreate` with recovery-safe instructions or mark scripts obsolete/quarantined during RECOVERY-3. Do not generate migrations from setup scripts. | `rg "migrations add InitialCreate|dotnet ef migrations add" setup-database.ps1 setup-database.bat` returns no unsafe setup invocation. | Restore original scripts if rejected before execution. | High: old scripts can recreate the Era-2 failure mode. | Release Engineer | Setup scripts no longer teach manual InitialCreate migration generation. |
| B-11 | Correct factual recovery-document inaccuracies without redesign. | `RECOVERY-1_CANONICAL_MODEL_LOCK.md`; `RECOVERY-2_SNAPSHOT_BASELINE_STRATEGY.md`; `RECOVERY-3_GREENFIELD_EXECUTION_PLAN.md` | Apply only factual corrections listed below in Documentation Corrections and Runbook Corrections. Do not change Candidate C or the authority hierarchy. | `rg` checks confirm corrected counts/wording; reviewer signs correction commit. | Revert doc correction commit. | High: execution logs citing false facts weaken release evidence. | Principal Software Architect | RECOVERY-1/2/3 facts match current source and readiness review corrections. |
| B-12 | Prove build and design-time model on clean branch. | Solution and EF toolchain | After B-01, run `dotnet build PointOfSale.sln -c Release` and `dotnet ef dbcontext info --project PointOfSale.Infrastructure --startup-project PointOfSale.API`. | Both commands exit 0 and output is logged. | Fix on separate commit or abort branch. | Medium: a non-building tree cannot produce trustworthy EF artifacts. | Principal EF Core Architect | Build and design-time context proof recorded on clean branch. |
| B-13 | Prove scratch DB is disposable, empty, and version-compatible. | Scratch DB environment; execution log | Provision local/container PostgreSQL matching production major version. Record version, connection target, database name, and empty schema proof. | `select version()` logged; table list is empty before S-10; host is local/container/disposable. | Drop scratch DB/container. | Medium: accidental real DB targeting would violate scope. | DevOps Engineer | Scratch DB proof recorded before any EF database command. |
| B-14 | Prove no real environment connection string is reachable. | Environment variables; `PointOfSale.API/appsettings*.json`; user secrets if used by execution shell | Immediately before S-08+, record process env, appsettings resolution, and design-time factory resolution. Use scratch/dummy connection only. | No `ConnectionStrings__*` process variables point to real hosts; appsettings default is empty or scratch; any user secret is disabled or points to scratch. | Clear env vars and restart shell. | Medium: design-time host could connect to real infrastructure. | DevOps Engineer | Execution shell has only scratch/dummy persistence connectivity. |
| B-15 | Preserve rollback evidence before file moves. | Git bundle; external archive copies; execution log | Create `pointofsale-pre-recovery3.bundle` and read-only copies of active and legacy migrations before S-01/S-04. | `git bundle verify` succeeds; copied folders hash-identical to source. | Delete invalid bundle/copies and recreate. | Low but mandatory: archive move rollback evidence must exist. | Release Engineer | Bundle path, hash, and archive-copy hash proof recorded. |

# Runbook Corrections

Only corrections required for RECOVERY-3 execution readiness are listed here. The approved strategy is unchanged.

1. PowerShell compatibility
   - Replace Unix count commands with PowerShell equivalents using `Get-ChildItem`, `Where-Object`, and `Measure-Object`.
   - Replace `find | sort | xargs sha256sum` with `Get-ChildItem -Recurse -File | Sort-Object FullName | Get-FileHash -Algorithm SHA256`.
   - Replace `diff -rq`/text `diff` assumptions with `Compare-Object` or a committed cross-platform helper.
   - Replace `bash tools/recovery/check-migrations-guardrails.sh` with either a PowerShell script or an explicit Git Bash/WSL prerequisite.
   - Replace `dropdb` rollback wording with a Windows-available PostgreSQL command path, container removal command, or SQL `DROP DATABASE` command against scratch only.

2. Guardrail wording
   - Correct S-07 guardrail rule to exclude `ApplicationDbContextModelSnapshot.cs`.
   - Require exactly one snapshot file under the active `PointOfSale.Infrastructure/Migrations/` folder after baseline.
   - Require every non-snapshot migration class to have a sibling `.Designer.cs`.
   - Add a negative test for a dummy Designer-less migration class, not for the snapshot.

3. Snapshot exception
   - State explicitly that the snapshot is a generated cache and intentionally has no Designer.
   - State that the expected RECOVERY-3 baseline folder contains exactly one migration class, exactly one migration Designer, and exactly one snapshot.

4. Differ harness prerequisites
   - P-10 must specify project path, target framework, project references, EF services used, and build command.
   - The harness must be committed before S-01 or P-10 must no longer be a precondition. The readiness review requires it before execution, so the correct correction is to add it before S-01.

5. Execution log prerequisite
   - P-12 must include a concrete log template and require commit before S-01.
   - Every command output used as evidence must be recorded with command, timestamp, operator initials, exit code, and output hash.

6. CI wording
   - Replace "wire into CI" with the actual CI mechanism.
   - If root repository CI is to be introduced, name the file and job.
   - If external CI is authoritative, link or name the external merge-blocking job and list exact commands.
   - Do not claim CI guardrails are present until the job exists and is enforced.

7. EF-discoverability
   - Add `dotnet ef migrations list --no-connect` before archive, after archive, and after baseline.
   - Add source scans for `[Migration]` and `[DbContext(typeof(ApplicationDbContext))]` outside the active baseline folder after archive.
   - State that `PointOfSale.Infrastructure/Data/Migrations` is currently compiled by SDK default inclusion until it is moved outside the project or explicitly excluded.

8. Execution Log and snapshot facts
   - Correct RECOVERY-3's claim that the current plan is executable as written.
   - Correct snapshot line-count wording so the runbook does not rely on the stale 18,859-line figure as current truth; the current dirty snapshot observed in this pass is 13,564 lines.

9. Old setup scripts
   - Add a RECOVERY-3 task before merge to quarantine or correct `setup-database.ps1` and `setup-database.bat`.
   - The corrected scripts must not run `dotnet ef migrations add InitialCreate`.

# Documentation Corrections

Only factual corrections are required. Do not rewrite the architecture, authority hierarchy, ADR choice, or recovery strategy.

## RECOVERY-1

- Correct active migration SQL count from 190 to the current scoped value of 97, or state that 190 was an older/broader corpus count and explicitly scope it.
- Correct configuration count from 108 to 123 if RECOVERY-1 is cited as current source fact.
- Clarify that `PointOfSale.Infrastructure/Data/Migrations/` is not inert today by folder name alone; it is inside the SDK project tree and is compiled/discoverable until moved or excluded.
- Distinguish filename migration-class inventory from EF-discoverable migration inventory.
- Correct "50 later classes with no Designer" wording to the source-verified nuance: 100 active Designer-less migration classes total, with 50 after the last Designer as stated by the readiness review.

## RECOVERY-2

- Replace "snapshot is still frozen at the identical point" with "the snapshot is dirty/irregular and cannot be trusted as a current differ baseline."
- Replace "100 post-freeze hand-authored migrations" with "100 active Designer-less migration classes total; 50 are after the last Designer."
- Separate filename counts from EF-discoverable migration counts.
- Correct CI claims to say no repository CI pipeline was found in this checkout.
- Align history row wording with the readiness review's source-verified 128 history rows unless a fresh parser and scope are recorded.

## RECOVERY-3

- Remove or correct "the plan is therefore executable as written."
- Correct all Unix shell examples or state a proven shell prerequisite.
- Correct guardrail wording for the snapshot exception.
- Add P-10/P-12 closure as true pre-execution prerequisites, not future assumptions.
- Add explicit CI/merge-blocking path.
- Add setup script quarantine/correction.
- Add EF-discoverability checks as first-class gates, not only filename counts.
- Correct snapshot current-state wording: observed snapshot is modified and irregular; this pass observed 13,564 lines, not the prior 18,859-line value.

# Execution Readiness Matrix

Current source state is not green. "Ready" below means ready to begin RECOVERY-3 execution or RECOVERY-4 handoff from the current repository state.

| Blocker | Status | Evidence | Ready |
|---|---|---|---|
| B-01 Dirty persistence worktree | Still Open | 277 Core/Infrastructure status entries | RED |
| B-02 Recovery evidence not committed | Still Open | `?? docs/audits/`, `?? docs/recovery/` | RED |
| B-03 Dirty irregular snapshot | Still Open | Snapshot modified, misses `CarrierAccount` and `PaidBy`, contains later artifacts | RED |
| B-04 Missing differ harness | Still Open | `tools/Recovery3.DifferHarness` absent | RED |
| B-05 Missing execution log | Still Open | `RECOVERY-3_EXECUTION_LOG.md` absent | RED |
| B-06 Guardrail contradiction | Still Open | S-07 conflicts with S-08 baseline artifact set | RED |
| B-07 PowerShell compatibility | Still Open | Runbook uses Unix commands in PowerShell environment | RED |
| B-08 EF-discoverability checks | Partially Resolved | `migrations list --no-connect` appears, but filename counts still dominate gates | RED |
| B-09 CI path undefined | Still Open | Root `.github` absent; no pipeline file found | RED |
| B-10 Old setup scripts | Still Open | Both setup scripts call `dotnet ef migrations add InitialCreate` | RED |
| B-11 Factual doc corrections | Still Open | RECOVERY-1/2/3 contain source-inaccurate wording | RED |
| B-12 Build/design-time proof | Still Open | Not run on clean branch; clean branch absent | RED |
| B-13 Scratch DB proof | Still Open | No scratch version/isolation evidence | RED |
| B-14 Real connection-string exclusion | Partially Resolved | Process env clean and appsettings default empty; full execution-shell proof pending | RED |
| B-15 Backup/archive proof | Still Open | No bundle/copy verification evidence | RED |

# Final Recommendation

RECOVERY-4 cannot begin.

RECOVERY-3 implementation cannot begin from the current repository state.

Required next action is not architectural redesign. It is blocker closure in the approved RECOVERY-3 path:

1. Clean and ratify the recovery branch and recovery documents.
2. Correct RECOVERY-1/2/3 factual and runbook issues listed above.
3. Add the missing differ harness and execution log.
4. Define the actual CI/merge-blocking guardrail path.
5. Quarantine or correct obsolete setup scripts.
6. Re-run build, design-time, EF-discoverability, scratch DB, environment isolation, and backup prerequisites from the clean branch.

No Critical blocker is eligible for waiver.

# Final Certification

NOT READY
