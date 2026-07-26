# RECOVERY-3 — GREENFIELD MATERIALIZATION EXECUTION PLAN

- Date: 2026-07-23
- Status: **EXECUTION RUNBOOK (PLANNING ARTIFACT).** No implementation, no migrations, no snapshots, no file moves/deletes, no database commands were performed while authoring this document. Only this document was written.
- Authoring roles: Principal Software Architect · Principal EF Core Architect · Principal Database Recovery Engineer · Principal Release Engineer · Principal DevOps Engineer.
- Governing documents: `docs/recovery/RECOVERY-1_CANONICAL_MODEL_LOCK.md` (constitution, BINDING) and `docs/recovery/RECOVERY-2_SNAPSHOT_BASELINE_STRATEGY.md` (ADR-001, Hybrid Baseline — Candidate C, conditionally approved).
- Method: source-code driven. Every load-bearing fact in "Fresh Source Verification" was re-derived from the live tree during this pass. Prior reports are supporting evidence only.

---

# Executive Summary

RECOVERY-2 selected **Candidate C — Hybrid**: regenerate the snapshot from the Runtime EF Model (sole authority per RECOVERY-1 INV-1), emit one consolidated baseline migration materializing the full model from empty, freeze the existing 149-class chain plus the legacy `Data/Migrations/` directory as a read-only archive, and install differ-in-the-loop guardrails. Environment re-stamping is explicitly OUT of scope here (RECOVERY-4, per E-8).

This document converts that decision into a step-by-step, zero-ambiguity runbook. It defines:

- 12 preconditions (P-01…P-12) with verification commands and stop conditions.
- A 14-step execution sequence (S-01…S-14), each with objective, inputs, described commands, expected outputs, verification, rollback, and exit gate.
- Exact plans for baseline generation, archive freeze, greenfield validation, a per-step failure matrix, and the artifact set RECOVERY-4 requires.

Nothing in this plan touches any existing environment's database. The only database used is a **disposable scratch PostgreSQL instance** created and destroyed inside this program. All repository mutation happens on a dedicated recovery branch and is reversible by `git` alone at every step until the final merge gate.

Fresh verification this pass confirms the binding condition E-1 still holds: the chain remains at **149 classes / 49 Designers (100 Designer-less)** — no new hand-authored migration has landed since the ADR. The plan is therefore executable as written.

## Verdict: **EXECUTION PLAN APPROVED**

(Approval is of the plan itself. Execution remains gated on the preconditions in this document; P-01/E-1 re-verification is mandatory at execution time because the count can move between now and then.)

---

# Fresh Source Verification

All numbers re-derived this pass, directly from the tree (commands shown so the executor can re-run them identically):

| Fact | Observed this pass | Command used |
|------|--------------------|--------------|
| Active migration classes (excl. Designer/Snapshot) | **149** | `ls PointOfSale.Infrastructure/Migrations/*.cs \| grep -v Designer \| grep -v ModelSnapshot \| wc -l` |
| Designer files | **49** | `ls PointOfSale.Infrastructure/Migrations/*.Designer.cs \| wc -l` |
| Designer-less (anti-pattern) classes | **100** (= 149 − 49; E-1 threshold intact) | arithmetic on the above |
| Companion `.sql` in active tree | **97** | `ls PointOfSale.Infrastructure/Migrations/*.sql \| wc -l` |
| Latest class | `20260722130000_HardenProviderDeliveryOutbox.cs` | `ls … \| sort \| tail -1` |
| Last Designer (snapshot freeze point) | `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped` | `ls *.Designer.cs \| sort \| tail -1` |
| Snapshot | present, **18,859 lines**, `ProductVersion 9.0.1` | `wc -l ApplicationDbContextModelSnapshot.cs` |
| `CarrierAccount` in snapshot | **0 hits** (Carrier runtime entities still unmaterialized — model-only) | `grep -c CarrierAccount …ModelSnapshot.cs` |
| `DbSet<>` in `ApplicationDbContext` | **242** | `grep -c 'public DbSet<' PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` |
| `IEntityTypeConfiguration<>` files | **123** under `Data/Configurations/` | `grep -rl 'IEntityTypeConfiguration<' … \| wc -l` |
| Legacy dir `Data/Migrations/` | **40 classes / 38 Designers** | `ls` counts |
| History CSV | `DB/data-1784830579292.csv`, 129 lines (128 rows + header) | `wc -l` |
| EF Core packages | `Microsoft.EntityFrameworkCore.Design` **9.0.1**, `Tools` **9.0.1**, `Npgsql.EntityFrameworkCore.PostgreSQL` **9.0.2** | `grep PackageReference PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj` |
| dotnet-ef local tool | **9.0.1**, `rollForward: false`, in `.config/dotnet-tools.json` | `cat .config/dotnet-tools.json` |
| .NET SDK on executor machine | **9.0.308** | `dotnet --version` |
| Startup project (design-time host) | `PointOfSale.API` (contains `UseNpgsql` in `Program.cs`) | `grep -rl UseNpgsql PointOfSale.API` |
| Current branch | `feature/new-pr210` (dirty: large modified/untracked set) | `git branch --show-current`, `git status` |

Conclusions binding on this plan:

1. **E-1 (anti-pattern freeze) currently holds** — Designer-less count is exactly 100, unchanged since RECOVERY-2. It MUST be re-verified at execution start (P-01).
2. The differ baseline is invalid (snapshot frozen 100 migrations back); therefore **no step in this plan may ever call `dotnet ef migrations add` against the existing `Migrations/` folder** — the baseline is cut only after the old chain is moved to archive (S-05) and the stale snapshot removed (S-06), so the differ sees "no prior migrations" and emits a full-model baseline.
3. The working tree is heavily dirty on a feature branch. Execution MUST occur on a dedicated, clean recovery branch (P-02); a dirty tree is a stop condition.

---
# Preconditions

Every precondition has: requirement, verification (exact command), and stop condition. ALL must be green before S-01. Fail-closed (INV-9): any red precondition halts the program before it starts.

| # | Requirement | Verification command | Stop condition |
|---|-------------|----------------------|----------------|
| P-01 | **E-1 anti-pattern freeze re-verified at execution time.** Designer-less migration count in `PointOfSale.Infrastructure/Migrations/` ≤ 100. | `c=$(ls PointOfSale.Infrastructure/Migrations/*.cs \| grep -v Designer \| grep -v ModelSnapshot \| wc -l); d=$(ls PointOfSale.Infrastructure/Migrations/*.Designer.cs \| wc -l); echo $((c-d))` → must print ≤ 100 | Count > 100 → STOP. New hand-authored migrations landed after ADR-001; RECOVERY-2 must re-certify before proceeding. |
| P-02 | **Clean dedicated branch.** A new branch (name: `recovery/3-greenfield-baseline`) cut from the commit the team designates as the recovery base; `git status` clean (no modified, no untracked in `PointOfSale.Infrastructure/**`, `PointOfSale.Core/**`). | `git status --porcelain -- PointOfSale.Infrastructure PointOfSale.Core \| wc -l` → 0 | Non-zero → STOP. Stash/commit/park the in-flight work first. Baseline must be attributable to one commit. |
| P-03 | **Repository backup.** A full repo backup exists outside the working copy: `git bundle create ../pointofsale-pre-recovery3.bundle --all` plus a plain copy of `PointOfSale.Infrastructure/Migrations/` and `PointOfSale.Infrastructure/Data/Migrations/` to a read-only location. | `git bundle verify ../pointofsale-pre-recovery3.bundle` → "The bundle is valid"; `diff -rq` of the folder copies → no differences | Bundle invalid or copies differ → STOP. |
| P-04 | **Toolchain pinned.** .NET SDK 9.x present; local tool manifest restores dotnet-ef 9.0.1 (must match EF packages 9.0.1 — snapshot/baseline must carry `ProductVersion 9.0.1`). | `dotnet --version` (9.x); `dotnet tool restore`; `dotnet ef --version` → `9.0.1` | dotnet-ef version ≠ 9.0.1 → STOP (a different tool version would re-emit the snapshot with a different ProductVersion and pollute the diff review). |
| P-05 | **Solution builds clean.** | `dotnet build PointOfSale.sln -c Release` → exit 0, 0 errors | Build errors → STOP. The design-time model cannot be trusted from a non-compiling tree. |
| P-06 | **Design-time model loads.** The EF design-time host resolves `ApplicationDbContext` without touching a database. | `dotnet ef dbcontext info --project PointOfSale.Infrastructure --startup-project PointOfSale.API` → prints provider `Npgsql.EntityFrameworkCore.PostgreSQL`, no exception | Any design-time exception → STOP; fix the DbContext factory/startup wiring first. |
| P-07 | **Scratch PostgreSQL available and DISPOSABLE.** A local/container PostgreSQL (major version matching production) with an empty database, e.g. `docker run -d --name r3-scratch -e POSTGRES_PASSWORD=r3 -p 55432:5432 postgres:<prod-major>`. Connection string recorded in an untracked local file (e.g. `docs/recovery/.r3-scratch.env`, gitignored). MUST NOT be any shared/staging/production host. | `psql "<scratch conn>" -c 'select version();'` succeeds; `\dt` shows zero tables | Scratch not reachable, or conn string resolves to a non-disposable host → STOP. |
| P-08 | **No production/staging connectivity in the execution shell.** Environment variables / appsettings used by the design-time host must not point at a real environment during S-08+. | Inspect `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__*` env vars, and the appsettings the API design-time factory reads; confirm they resolve to the scratch instance or a dummy | Real-environment conn string reachable → STOP. |
| P-09 | **History evidence preserved.** `DB/data-1784830579292.csv` (applied-history export) and `DB/schema-23-7-2026.sql` present and copied into the archive evidence folder plan (S-05). | `ls DB/data-1784830579292.csv DB/schema-23-7-2026.sql` | Missing → STOP; re-export from the source environment before proceeding (read-only export, allowed). |
| P-10 | **Differ harness source ready (not yet run).** The throwaway console-app harness described in "Baseline Generation Plan §4" is written and committed to the recovery branch under `tools/Recovery3.DifferHarness/` (code exists; execution happens in S-09). | `dotnet build tools/Recovery3.DifferHarness` → exit 0 | Harness does not compile → STOP. |
| P-11 | **Team moratorium communicated.** Written notice that `PointOfSale.Infrastructure/Migrations/**` is frozen for the duration; no PR may touch entities/configurations/migrations until S-14 completes. | Link to the notice recorded in the execution log | No moratorium → STOP (a mid-execution model change invalidates the baseline). |
| P-12 | **Execution log started.** `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` created; every step's actual command, output hash, and operator initials will be appended. | File exists on the branch | — |

Rollback prerequisite (global): at every step S-01…S-13, full rollback = `git reset --hard <step-start-commit>` + (if scratch DB touched) `DROP DATABASE` + recreate on the scratch instance. Nothing irreversible occurs before the merge gate S-14.

---

# Execution Sequence

Conventions: every step is committed atomically on `recovery/3-greenfield-baseline` with message `R3-S<nn>: <objective>`. Record the commit SHA in the execution log. "Verification" must pass before the commit; "Exit gate" must pass before the next step starts. Commands are DESCRIBED here; none were executed while writing this plan.

### Step S-01 — Freeze point & branch cut
- Objective: create the recovery branch and record the exact freeze commit.
- Inputs: designated base commit; P-01…P-03 green.
- Commands: `git checkout -b recovery/3-greenfield-baseline <base-sha>`; append base SHA + P-01 count to the execution log.
- Expected output: branch exists, clean tree.
- Verification: `git status --porcelain | wc -l` → 0; P-01 command re-run → ≤ 100.
- Rollback: delete branch (`git branch -D`).
- Exit gate: log entry with SHA + counts committed.

### Step S-02 — Toolchain lock proof
- Objective: prove tool/SDK versions on the executing machine match the pins.
- Inputs: `.config/dotnet-tools.json`, csproj package refs.
- Commands: `dotnet --version`, `dotnet tool restore`, `dotnet ef --version`; record outputs.
- Expected output: SDK 9.x, dotnet-ef 9.0.1.
- Verification: outputs match P-04.
- Rollback: none needed (read-only step).
- Exit gate: versions logged.

### Step S-03 — Full build + design-time model load
- Objective: prove the Runtime Model (authority) compiles and loads.
- Inputs: solution source.
- Commands: `dotnet build PointOfSale.sln -c Release`; `dotnet ef dbcontext info --project PointOfSale.Infrastructure --startup-project PointOfSale.API`.
- Expected output: 0 errors; context info printed (provider Npgsql, context `ApplicationDbContext`).
- Verification: exit codes 0.
- Rollback: none (read-only).
- Exit gate: both outputs logged.

### Step S-04 — Pre-archive inventory manifest
- Objective: cryptographically fix the archive contents BEFORE moving anything.
- Inputs: `PointOfSale.Infrastructure/Migrations/**`, `PointOfSale.Infrastructure/Data/Migrations/**`, `scripts/Migrations/**`, `DB/data-1784830579292.csv`, `DB/schema-23-7-2026.sql`.
- Commands: generate `docs/recovery/archive/ARCHIVE_MANIFEST.sha256` via `find <each dir> -type f | sort | xargs sha256sum`; count files per class (expect 149 classes / 49 Designers / 1 snapshot / 97 sql; legacy 40/38/12).
- Expected output: manifest file listing every path + hash; counts matching Fresh Source Verification.
- Verification: recomputed counts equal the table above; manifest committed.
- Rollback: `git reset --hard` to S-03 commit.
- Exit gate: manifest committed; counts logged.

### Step S-05 — Archive move (chain retirement)
- Objective: move the historical chain OUT of the EF-discovered namespace into the frozen archive, preserving history.
- Inputs: S-04 manifest.
- Commands (described): `git mv PointOfSale.Infrastructure/Migrations docs/recovery/archive/migrations-era1-era2` and `git mv PointOfSale.Infrastructure/Data/Migrations docs/recovery/archive/migrations-legacy`; copy (not move) `DB/data-1784830579292.csv` and `DB/schema-23-7-2026.sql` into `docs/recovery/archive/evidence/`; add `docs/recovery/archive/README.md` stating the archive is read-only forensics (RECOVERY-1 roles #5/#6/#11) and MUST never be compiled or replayed. Add an entry to the Infrastructure csproj ONLY if the moved files would otherwise still compile (they are now outside the project directory, so no csproj change is expected — verify).
- Expected output: `PointOfSale.Infrastructure/Migrations/` no longer exists; archive folders populated; `git mv` preserves history (`git log --follow` works).
- Verification: re-run S-04 hashing over the new locations and `diff` against `ARCHIVE_MANIFEST.sha256` (paths remapped) → all hashes identical; `dotnet build PointOfSale.sln` still succeeds (archived .cs are outside all project dirs → not compiled). CRITICAL: if the build FAILS or the archive is still compiled, stop.
- Rollback: `git reset --hard` to S-04 commit.
- Exit gate: hash-identical archive + green build.

### Step S-06 — Remove stale snapshot from the active path
- Objective: ensure the differ sees an empty migrations assembly so the next `migrations add` emits the FULL model.
- Inputs: S-05 result.
- Commands: confirm `ApplicationDbContextModelSnapshot.cs` traveled with the archive move (it lived inside `Migrations/`); confirm no other `*ModelSnapshot.cs` remains under `PointOfSale.Infrastructure/`: `grep -rl 'ModelSnapshot' PointOfSale.Infrastructure --include='*.cs'` → 0 hits outside archive.
- Expected output: zero snapshot/migration classes in the compiled Infrastructure assembly.
- Verification: `dotnet ef migrations list --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-connect` → prints no migrations.
- Rollback: `git reset --hard` to S-05 commit.
- Exit gate: `migrations list` empty; build green.

### Step S-07 — Guardrail scaffolding (pre-baseline)
- Objective: commit the CI guardrail scripts BEFORE cutting the baseline so the baseline lands into a guarded world (SC-7).
- Inputs: RECOVERY-2 SC-7 definition.
- Commands: add `tools/recovery/check-migrations-guardrails.sh` implementing: (a) `dotnet ef migrations has-pending-model-changes` must return "no changes" on CI; (b) every `*.cs` under the new `Migrations/` folder must have a sibling `.Designer.cs` (fail otherwise); wire into CI pipeline definition (file only; pipeline activation may follow the merge).
- Expected output: script committed and locally runnable (it will FAIL at this moment — no snapshot exists yet; that is expected and documented in the log).
- Verification: `bash tools/recovery/check-migrations-guardrails.sh` runs and reports its checks (red allowed pre-baseline).
- Rollback: `git reset --hard` to S-06.
- Exit gate: script committed.

### Step S-08 — Baseline emission (THE materialization step)
- Objective: emit the consolidated baseline migration + regenerated snapshot from the Runtime Model.
- Inputs: S-06 empty-chain state; P-06 design-time load.
- Commands (described): `dotnet ef migrations add Baseline_Recovery3 --project PointOfSale.Infrastructure --startup-project PointOfSale.API --output-dir Migrations`. No database is contacted by `migrations add`.
- Expected output: exactly three new files in `PointOfSale.Infrastructure/Migrations/`: `<ts>_Baseline_Recovery3.cs`, `<ts>_Baseline_Recovery3.Designer.cs`, `ApplicationDbContextModelSnapshot.cs`.
- Verification (all must pass):
  1. `Up()` contains ONLY creative DDL (`CreateTable`/`CreateIndex`/`AddForeignKey`/`AddCheckConstraint`/`EnsureSchema`/sequence ops) — zero `Drop*`, zero `Sql(`, zero DML (INV-8).
  2. Table count in `Up()` ≈ entity count of the runtime model (expect ~242+ tables incl. owned/join tables; exact number recorded, not assumed).
  3. `grep CarrierAccount` on the new snapshot → > 0 hits (the previously unmaterialized Carrier entities are now present); `grep PaidBy` on baseline → present.
  4. New snapshot `ProductVersion` = 9.0.1.
  5. `dotnet build` green.
- Rollback: `git reset --hard` to S-07 commit (removes all three files).
- Exit gate: all five checks logged with outputs.

### Step S-09 — Differ-zero proof (runtime equality)
- Objective: prove `HasDifferences(snapshot, runtime) == FALSE` (SC-1) by two independent mechanisms.
- Inputs: S-08 artifacts; P-10 harness.
- Commands: (a) `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API` → expect "No changes have been made to the model…"; (b) run `tools/Recovery3.DifferHarness` (IModelRuntimeInitializer + IMigrationsModelDiffer, fake conn string, see Baseline Generation Plan §4) → expect `diff count: 0`.
- Expected output: both report zero differences.
- Verification: exit code 0 from both; outputs logged verbatim.
- Rollback: none needed (read-only); on failure → Failure Matrix F-09.
- Exit gate: double-zero proof logged.

### Step S-10 — From-zero materialization on scratch (SC-3)
- Objective: prove the baseline builds the full schema on an empty database.
- Inputs: P-07 scratch instance (empty DB `r3_fromzero`).
- Commands: `dotnet ef database update --project PointOfSale.Infrastructure --startup-project PointOfSale.API --connection "<scratch conn>"`.
- Expected output: single migration applied; `__EFMigrationsHistory` contains exactly one row (`<ts>_Baseline_Recovery3`, `9.0.1`).
- Verification: `psql`: `select count(*) from information_schema.tables where table_schema='public';` → equals the S-08 table count (+1 for history); spot-check the 7 Carrier tables and `AffiliatePayoutRequests.PaidBy` column exist; zero rows in any business table (schema-only, INV-8).
- Rollback: `dropdb r3_fromzero` on scratch; repo untouched.
- Exit gate: counts + spot checks logged.

### Step S-11 — Post-apply differ & script verification (SC-4)
- Objective: prove the applied scratch schema still differs zero from the model, and the idempotent script generates cleanly.
- Commands: re-run `has-pending-model-changes` (expect no changes); `dotnet ef migrations script --idempotent -o docs/recovery/archive/evidence/baseline-idempotent.sql` (artifact for RECOVERY-4); optional `pg_dump --schema-only` of scratch for the evidence folder.
- Expected output: no pending changes; script generated; pg_dump captured.
- Verification: files exist; `has-pending-model-changes` output logged.
- Rollback: delete generated evidence files (git clean).
- Exit gate: SC-4 evidence committed.

### Step S-12 — Application startup + integration smoke
- Objective: prove the application boots against the baseline-built schema.
- Commands: run `PointOfSale.API` with connection pointed at scratch `r3_fromzero` (environment override only; no config file edits); hit health endpoint(s); run the fastest DB-touching test suite subset (`dotnet test PointOfSale.Tests --filter <smoke category>` if such category exists — executor records which filter was used).
- Expected output: clean startup, no `PendingModelChangesWarning`, no missing-relation exceptions; smoke tests green.
- Verification: startup log excerpt + test summary appended to execution log.
- Rollback: stop process; drop scratch DB.
- Exit gate: startup + smoke evidence logged.

### Step S-13 — Guardrail activation proof
- Objective: prove the S-07 guardrails now pass and actually catch violations.
- Commands: run `check-migrations-guardrails.sh` → must pass. Negative test: temporarily add a dummy Designer-less `.cs` under `Migrations/` in the working tree (NOT committed), re-run script → must fail; delete the dummy.
- Expected output: green on real tree; red on sabotage.
- Verification: both outputs logged; `git status` clean afterward.
- Rollback: `git checkout -- .` if needed.
- Exit gate: positive + negative guardrail proof.

### Step S-14 — Certification commit & merge gate
- Objective: finalize the branch for review; hand off to RECOVERY-4.
- Commands: append final certification section to the execution log; ensure the RECOVERY-4 handoff artifact list (below) is complete; open PR from `recovery/3-greenfield-baseline`; require review by at least one engineer who did NOT execute the runbook.
- Verification: PR checklist mirrors the Handoff section; CI guardrail runs green in the pipeline.
- Rollback: PR can be closed; branch deleted; bundle P-03 restores everything.
- Exit gate: PR approved & merged = RECOVERY-3 complete.

---
# Baseline Generation Plan

Nothing here was generated. This section specifies EXACTLY how S-06…S-09 will produce and prove the artifacts.

## 1. Snapshot regeneration

- Mechanism: the snapshot is NEVER hand-edited (INV-2). It is re-emitted as a side effect of `dotnet ef migrations add Baseline_Recovery3` (S-08). Because S-05/S-06 removed every prior migration class and the frozen snapshot from the compiled Infrastructure assembly, EF's `IMigrationsAssembly.ModelSnapshot` resolves to `null`, so the differ compares the design-time Runtime Model against an EMPTY model — the emitted snapshot is therefore a pure function of the authority (242 DbSets + 123 configurations + entity CLR shapes), with zero inheritance from the stale 18,859-line snapshot.
- Pin: dotnet-ef 9.0.1 (local tool, `rollForward:false`) + EFCore.Design 9.0.1 + Npgsql provider 9.0.2, guaranteeing `ProductVersion 9.0.1` in the new snapshot and byte-stable emission for review.
- Design-time host: `--startup-project PointOfSale.API` (verified: the only project wiring `UseNpgsql` in `Program.cs`). `migrations add` does not open a database connection; the connection string may be a dummy.

## 2. Baseline migration production

- Same single command as the snapshot (they are emitted atomically by design — Designer + snapshot in lockstep, which IS the INV-7 workflow being restored).
- Expected shape of `Up()`: `EnsureSchema`/`CreateSequence` ops (if the model declares any), then `CreateTable` for every entity/owned/join table, then `CreateIndex`, with FKs either inline in `CreateTable` or as `AddForeignKey`. Expected shape of `Down()`: mirrored `DropTable`s. Any `migrationBuilder.Sql(`, any DML, or any `Drop*` in `Up()` is a hard failure (INV-8) — see F-08.
- Naming: `Baseline_Recovery3` — the timestamp prefix is assigned by the tool at execution time and recorded in the log; this document deliberately does not predict it.

## 3. Designer verification

- The emitted `<ts>_Baseline_Recovery3.Designer.cs` must (a) declare `[Migration("<ts>_Baseline_Recovery3")]`, (b) carry `ProductVersion 9.0.1`, (c) have a `BuildTargetModel` body textually equivalent to the new snapshot's `BuildModel` body (same entity blocks in the same order). Verification: normalize both (strip class/method headers) and `diff` — expect zero semantic difference. A helper comparison is included in the differ harness.

## 4. Runtime Model equality proof (SC-1)

Two independent mechanisms, both must return zero:

1. **Tool-level:** `dotnet ef migrations has-pending-model-changes` (build-only, no DB). Zero pending = tool agrees snapshot == model.
2. **API-level harness** (`tools/Recovery3.DifferHarness`, committed under P-10): a throwaway console app referencing Infrastructure + API that
   - obtains `IDesignTimeModel.Model` (runtime authority) and `IMigrationsAssembly.ModelSnapshot.Model` (the snapshot class is internal — resolved via DI, never `new`ed),
   - runs both through `IModelRuntimeInitializer.Initialize(model, designTime: true, validationLogger: null)`,
   - calls `IMigrationsModelDiffer.GetDifferences(snapshot.GetRelationalModel(), runtime.GetRelationalModel())`,
   - prints the operation count and each operation if non-zero. Uses a fake connection string; nothing connects.
- Acceptance: BOTH report zero. If they disagree, the harness's op list is the diagnostic; execution stops (F-09).

---

# Archive Plan

## What becomes archive

| Artifact | From | To (frozen) |
|----------|------|-------------|
| Era-1+Era-2 chain: 149 classes, 49 Designers, frozen snapshot, 97 `.sql` | `PointOfSale.Infrastructure/Migrations/` | `docs/recovery/archive/migrations-era1-era2/` |
| Legacy chain: 40 classes, 38 Designers, 12 `.sql` | `PointOfSale.Infrastructure/Data/Migrations/` | `docs/recovery/archive/migrations-legacy/` |
| Stray script | `scripts/Migrations/20260117_AddPromotionSearchBoostOverride.sql` | copied to `docs/recovery/archive/evidence/` (original left in place — referenced by ops tooling until RECOVERY-4 rules on it) |
| History export | `DB/data-1784830579292.csv` | copied to `docs/recovery/archive/evidence/` |
| Schema evidence | `DB/schema-23-7-2026.sql` | copied to `docs/recovery/archive/evidence/` |

## How preservation works

- Moves use `git mv` so `git log --follow` retains full per-file history; nothing is deleted, squashed, or rewritten.
- The archive lives under `docs/` — outside every `.csproj` directory — so the classes are no longer compiled into the migrations assembly. This is the mechanism that retires the chain from the canonical path (P-5: preserve, don't depend) without destroying it.
- `docs/recovery/archive/README.md` (added in S-05) states: read-only forensics; never compile, replay, or `database update` from these files; the 58 orphan history rows and the 34-migration interleave are documented context, not executable state.
- Optional hardening after merge: a CI check that fails any PR modifying `docs/recovery/archive/**` (content is append-never); plus `git tag recovery3-archive-freeze` on the S-05 commit.

## Integrity verification

- S-04 produces `ARCHIVE_MANIFEST.sha256` over every file BEFORE the move; S-05 recomputes hashes AFTER the move and diffs — byte-identical or stop.
- Counts re-asserted post-move: 149/49/1-snapshot/97 and 40/38/12.
- The P-03 bundle + external folder copy is the disaster-recovery copy independent of the repo itself.

---

# Validation Plan

Executed as S-09…S-13; enumerated here as discrete validations with pass criteria.

| # | Validation | Mechanism | Pass criterion |
|---|-----------|-----------|----------------|
| V-1 | Empty-database materialization | `dotnet ef database update` against empty scratch `r3_fromzero` | Exit 0; exactly 1 history row |
| V-2 | Differ verification (tool) | `has-pending-model-changes` after S-08 and again after S-10 | "No changes" both times |
| V-3 | Runtime equality (API) | DifferHarness `GetDifferences` count | 0 operations |
| V-4 | Designer/snapshot lockstep | Normalized diff of Designer `BuildTargetModel` vs snapshot `BuildModel` | Semantically identical |
| V-5 | Schema verification | `information_schema` counts + spot checks (7 Carrier tables, `AffiliatePayoutRequests.PaidBy`, tenant-scoped unique indexes) vs S-08 recorded counts | All present; counts match |
| V-6 | Purity (INV-8) | grep baseline `Up()` for `Sql(`, `Drop`, INSERT/UPDATE/DELETE | 0 hits |
| V-7 | Idempotent script generation | `migrations script --idempotent` | Generates without error; guarded by history-row checks |
| V-8 | Application startup | API boot against scratch | Healthy; no PendingModelChangesWarning; no missing relation/column errors |
| V-9 | Integration smoke | DB-touching smoke test subset | Green (filter recorded in log) |
| V-10 | Performance sanity | Time `database update` on scratch; count indexes created | Completes in minutes-order; index count matches snapshot's `HasIndex` count (recorded, compared) |
| V-11 | Guardrail efficacy | Positive + negative run of guardrail script (S-13) | Green on clean tree, red on sabotage |

All validations run against SCRATCH only. No existing environment database is read from or written to anywhere in RECOVERY-3 (INV-5, E-8).

---

# Failure Matrix

Stop = execution halts, fail-closed (INV-9). Continue = remediate in place, re-verify, proceed.

| Step | Possible failure | Detection | Recovery | Rollback | Stop? |
|------|------------------|-----------|----------|----------|-------|
| P-01 | Designer-less count > 100 | precondition command | Return to RECOVERY-2 governance; re-certify E-1 | n/a | STOP |
| S-01 | Dirty tree on branch cut | `git status` | Park in-flight work; re-cut | delete branch | STOP until clean |
| S-02 | dotnet-ef ≠ 9.0.1 | `dotnet ef --version` | `dotnet tool restore`; fix manifest/PATH | none | STOP until pinned |
| S-03 | Build errors / design-time load exception | exit codes | Fix compile or DbContext factory on a SEPARATE commit; re-run | reset to S-01 | STOP |
| S-04 | Manifest counts ≠ expected (149/49/97, 40/38/12) | count comparison | Investigate delta (new migration landed? → P-01 breach) | reset | STOP |
| S-05 | Build fails after move (archive still compiled, e.g. via csproj glob) | `dotnet build` | Locate the include (`grep -r 'Migrations' *.csproj`); the archive must live outside compile globs; adjust archive location, never csproj excludes on live path | reset to S-04 | STOP until green |
| S-05 | Hash mismatch post-move | manifest diff | Restore from P-03 copy; redo move | reset | STOP |
| S-06 | `migrations list` still shows migrations | command output | A second migrations dir/assembly exists — find with `grep -rl ': Migration' --include='*.cs' PointOfSale.Infrastructure`; archive it too | reset to S-05 | STOP |
| S-08 | `migrations add` throws (design-time factory demands live DB, model validation error) | tool output | Fix factory to use dummy conn; model validation errors go back to S-03 class of fixes | reset to S-07 | STOP |
| S-08 | Baseline contains `Sql(`/`Drop*`/DML | V-6 grep | Model uses raw-SQL constructs? Investigate config source; do NOT hand-edit the migration — fix the model/config and re-emit | reset to S-07, re-run S-08 | STOP until pure |
| S-08 | Missing expected objects (e.g. Carrier tables absent) | V-5 spot grep | Entities not registered in model — configuration bug; fix under INV-1 review, rebuild, re-emit | reset to S-07 | STOP |
| S-09 | Tool says zero but harness says N>0 (or vice versa) | outputs disagree | Harness op list is diagnostic; usually a designTime-initialization mismatch in the harness; fix harness OR uncover a real provider-annotation drift | none (read-only) | STOP until both zero |
| S-10 | `database update` fails mid-baseline (provider type error, index name > 63 chars, collation) | psql/tool error | Baseline rolls back atomically (single tx per migration); scratch left with 0 or partial? → drop DB, fix root cause in model/config, re-emit from S-08 | dropdb; reset if code changed | STOP |
| S-10 | Table count mismatch vs S-08 record | V-5 counts | Recount incl. owned/join tables; if genuinely missing → S-08 failure class | dropdb | STOP |
| S-11 | Post-apply pending changes non-zero | V-2 | Something mutated the model mid-run (moratorium breach) → back to S-03 | dropdb | STOP |
| S-12 | Startup fails / missing relation | app logs | Distinguish schema gap (→ S-08 class) vs config/runtime issue (env var, seed expectations). Seed-data expectations are NOT schema failures — record and defer to RECOVERY-5 (data track) | stop app; dropdb | STOP if schema gap; CONTINUE if pure data-seed expectation, logged |
| S-13 | Guardrail passes the sabotage (false green) | negative test | Fix script logic before merge | checkout | STOP |
| S-14 | Reviewer finds discrepancy | PR review | Address on branch; re-run affected steps | branch remains | STOP until approved |
| any | Scratch instance dies | connection errors | Recreate container; re-run the step's DB portion (all DB steps are re-runnable from empty) | dropdb/recreate | CONTINUE after recreate |
| any | Mid-execution commit lands on main touching entities/configs | git log watch | Moratorium breach → abort, rebase decision at governance level | bundle restore available | STOP |

---

# RECOVERY-4 Handoff

RECOVERY-4 (environment re-stamp/conform) may begin ONLY when ALL of the following exist on the merged mainline:

1. `PointOfSale.Infrastructure/Migrations/` containing EXACTLY three files: `<ts>_Baseline_Recovery3.cs`, its `.Designer.cs`, `ApplicationDbContextModelSnapshot.cs` — all ProductVersion 9.0.1.
2. `docs/recovery/archive/` with `migrations-era1-era2/` (149/49/1/97), `migrations-legacy/` (40/38/12), `evidence/` (history CSV, schema dump, `baseline-idempotent.sql`, scratch pg_dump), `ARCHIVE_MANIFEST.sha256`, and `README.md` — plus tag `recovery3-archive-freeze`.
3. `docs/recovery/RECOVERY-3_EXECUTION_LOG.md` — every step S-01…S-14 with commands, verbatim key outputs, SHAs, operator initials; V-1…V-11 all green.
4. `tools/Recovery3.DifferHarness/` committed, building, last run output `0 differences` recorded.
5. `tools/recovery/check-migrations-guardrails.sh` wired into CI and green on mainline (SC-7 live).
6. `docs/recovery/archive/evidence/baseline-idempotent.sql` — the artifact RECOVERY-4 will study for the re-stamp design (INSERT-into-`__EFMigrationsHistory` strategy vs guarded apply).
7. The P-03 bundle location + hash recorded in the execution log.
8. A scope statement reconfirming: no existing environment database was modified in RECOVERY-3; re-stamp strategy, per-environment sequencing, and production cutover are RECOVERY-4 deliverables; data recovery/ETL is RECOVERY-5 (INV-8).

Absence of ANY item = RECOVERY-4 entry blocked (fail-closed).

---

# Final Certification

This runbook was authored strictly as a planning artifact: zero code changes, zero migrations, zero snapshots, zero file moves, zero database commands. Every load-bearing fact (149/49/100 chain state, frozen snapshot at `20260510130500` with 18,859 lines and 0 Carrier hits, 242 DbSets, 123 configurations, legacy 40/38, EF/dotnet-ef 9.0.1 pins, SDK 9.0.308, `PointOfSale.API` as the design-time host, history CSV present) was re-verified against the live source tree during this pass, not quoted from prior reports. The binding RECOVERY-2 condition E-1 currently holds (Designer-less count = 100, unchanged) and is re-checked as executable precondition P-01.

The plan is executable end-to-end by a senior engineer with no additional context: every step defines purpose, inputs, commands, expected outputs, verification, rollback, and an exit gate; every failure mode maps to a stop/continue decision; every validation is mechanical; and the entire program is reversible via git + disposable scratch database up to the final merge gate.

## EXECUTION PLAN APPROVED
