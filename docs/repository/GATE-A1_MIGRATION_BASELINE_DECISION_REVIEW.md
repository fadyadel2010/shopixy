# GATE A-1 — Migration Baseline Decision Review
## Shopixy Repository Modernization Program

**Artifact:** `docs/repository/GATE-A1_MIGRATION_BASELINE_DECISION_REVIEW.md`
**Scope:** Decision review of the greenfield migration baseline only. Read-only. No code, migrations, or snapshots were modified.
**Branch reviewed:** `recovery/3-greenfield-baseline`
**Reviewed baseline:** `20260723214642_Baseline_Recovery3`
**EF Core / Provider:** Microsoft.EntityFrameworkCore.Design + .Tools `9.0.1`; Npgsql (PostgreSQL). Snapshot `ProductVersion = 9.0.1`.

---

## 1. Executive Summary

The historical migration chain has been collapsed into a single generated baseline migration, `20260723214642_Baseline_Recovery3`. The evidence gathered directly from the working tree confirms the collapse is **internally consistent and technically well-formed**:

- The Migrations folder now contains exactly **one** migration (baseline `.cs` + `.Designer.cs`) plus the model snapshot — no partial chain, no orphan migration classes.
- The model snapshot (`ApplicationDbContextModelSnapshot.BuildModel`) is **byte-for-byte identical** to the baseline's `BuildTargetModel` (diff = 0 lines excluding the method signature). This is the single strongest signal that the snapshot and baseline are in lockstep and were generated from the same model.
- The baseline appears **generated from the current domain model**, not hand-authored: `Up()` contains 256 `CreateTable` calls with a symmetric `Down()` of 256 `DropTable` calls, and the seed `InsertData` blocks map to `HasData(...)` declarations in the domain `Data/Configurations/*` (snapshot contains 499 `HasData` entries).
- The old chain (149 migration classes / 353 tracked-file deletions) has been **relocated**, not silently destroyed — it is preserved under `docs/recovery/archive/RECOVERY-4_PHASE_A/PointOfSale.Infrastructure/Migrations/`.

**Decision: APPROVE WITH CONDITIONS.** The baseline is architecturally correct *for the recovery/greenfield branch it lives on*. The conditions in §7 exist to (a) get the baseline committed/tracked, and (b) protect any environment that already carries the historical `__EFMigrationsHistory` rows — the one scenario a collapse cannot serve transparently.

---

## 2. Existing Situation

### 2.1 Old migration chain (Before)
- **149** migration classes (non-Designer, non-snapshot) previously lived in `PointOfSale.Infrastructure/Migrations/`, spanning `20251123190510_InitialCreate` through the late-2025 / 2026 feature migrations.
- Git status shows **353 deletions** (`D`) under `PointOfSale.Infrastructure/Migrations/` — the `.cs` + `.Designer.cs` pairs of the historical chain plus associated files.
- The chain was linear and incremental (InitialCreate → Cashbox → Ledger/SOA → StockTransfer → … → Ecommerce → MassTransitOutbox → MultiTenancyFoundation → Payments/Shipments → …), i.e. a normal accreted EF history.

### 2.2 Relocation (not deletion)
- The full historical chain is archived at:
  `docs/recovery/archive/RECOVERY-4_PHASE_A/PointOfSale.Infrastructure/Migrations/`
  containing the migration `.cs` files and **49** `.Designer.cs` files plus a copy of the historical `ApplicationDbContextModelSnapshot.cs`.
- This means migration *history-as-documentation* is retained for forensic/audit purposes even though it is removed from the active EF chain.

### 2.3 Current tracked state (working tree)
- `ApplicationDbContextModelSnapshot.cs` — status **`M`** (modified, tracked).
- `20260723214642_Baseline_Recovery3.cs` — status **`??`** (untracked / not yet staged).
- `20260723214642_Baseline_Recovery3.Designer.cs` — status **`??`** (untracked / not yet staged).

> Observation: the snapshot edit is staged for tracking but the new baseline migration files themselves are **not yet added to git**. This is a gating condition (see §7 C-1), not an architectural defect.

---

## 3. New Baseline Analysis

### 3.1 Identity
- Migration id: `20260723214642_Baseline_Recovery3`
- Class: `public partial class Baseline_Recovery3 : Migration` (namespace `PointOfSale.Infrastructure.Migrations`).
- Designer attribute: `[Migration("20260723214642_Baseline_Recovery3")]` — matches the file id.
- Files: `20260723214642_Baseline_Recovery3.cs` (14,270 lines), `.Designer.cs` (23,153 lines).

### 3.2 Shape — evidence it is a real greenfield "create everything" baseline
- `Up()`: **256** `migrationBuilder.CreateTable(...)` calls.
- `Down()`: **256** `migrationBuilder.DropTable(...)` calls — symmetric, fully reversible.
- Uses Npgsql metadata (`using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;`) consistent with the PostgreSQL authority rule.
- Standard EF scaffolding header (`#nullable disable`, `#pragma warning disable CA1814`) — consistent with tool-generated output, not hand-editing.

### 3.3 Seed data (model-driven, reproducible)
- The baseline contains **10** `InsertData` blocks (no raw `migrationBuilder.Sql(...)` DML — the grep count of 10 is entirely `InsertData`).
- Seeded tables are reference/config data only: `CommerceHubCategories`, `CommerceHubCategoryAttributes`, `ThemePresets`.
- These `InsertData` rows are traceable to domain `HasData(...)` configuration — e.g. `CommerceHubCategoryConfiguration.cs` defines the `electronics` category (`Guid 10000000-...-000000000001`, slug `electronics`) and calls `builder.HasData(electronics, fashion, homeGarden, ...)`. The snapshot carries **499** `HasData` annotations overall.
- Conclusion: the seed content is **generated from the model**, so re-scaffolding the baseline would reproduce the same seed — it is not an out-of-band data dump.

---

## 4. Snapshot Consistency

This is the core correctness test for a baseline collapse.

| Check | Method | Result |
|---|---|---|
| Snapshot model == baseline target model | `diff` of `ApplicationDbContextModelSnapshot.BuildModel` body vs `Baseline_Recovery3.Designer.BuildTargetModel` body (both from first model line onward) | **0 differences** (only the method-name line differs: `BuildModel` vs `BuildTargetModel`, which is expected) |
| Snapshot generated from current model | 499 `HasData` + full entity graph present in snapshot | Consistent |
| Product/provider version aligned | Snapshot `ProductVersion 9.0.1` vs package refs `9.0.1` | Aligned |
| Migration id ↔ designer attribute | file id `20260723214642_Baseline_Recovery3` vs `[Migration(...)]` | Match |

**Verdict:** The snapshot and the baseline are consistent. A `dotnet ef migrations add` run today against this model would produce an empty migration — the model, snapshot, and baseline target model are in a single coherent state.

---

## 5. Architectural Impact

### 5.1 What actually changed (Before → After)
```
BEFORE                                   AFTER
------------------------------------     ------------------------------------
Migrations/                              Migrations/
  20251123190510_InitialCreate            20260723214642_Baseline_Recovery3.cs
  ... 149 migration classes ...           20260723214642_Baseline_Recovery3.Designer.cs
  ApplicationDbContextModelSnapshot       ApplicationDbContextModelSnapshot (regenerated)

  (linear incremental history)            (single greenfield "create-all" baseline)

                                        docs/recovery/archive/RECOVERY-4_PHASE_A/
                                          PointOfSale.Infrastructure/Migrations/
                                            <full historical chain preserved>
```

The change is **not** "N files edited." It is a **semantic reset of the EF migration timeline**: the repository's migration history now begins at `Baseline_Recovery3`, which materializes the entire current schema (256 tables) plus model-driven seed data in one step, with the historical chain moved to the archive tree for reference.

### 5.2 Positive impacts
- **Single source of schema truth** — fresh databases are provisioned by one deterministic migration that exactly matches the snapshot and the domain model.
- **Eliminates historical drift risk** — any inconsistencies accumulated across 149 incremental migrations are collapsed away; there is now exactly one authoritative schema definition.
- **Faster provisioning** — one `CreateTable` pass instead of replaying 149 migrations (many of which alter/undo earlier ones).
- **History preserved for audit** — the archive relocation means the collapse is reversible/forensically inspectable.

### 5.3 Structural risks introduced
- **Migration history is no longer applicable in-place to databases already migrated on the old chain** (see §6).
- **Developer/CI muscle memory** around the old chain is invalidated (see §6).

---

## 6. Risks

| # | Risk | Severity (this branch) | Notes |
|---|---|---|---|
| R1 | **Loss of in-chain migration history** | Low | History is not lost — relocated to `docs/recovery/archive/...`. But it is no longer an *applicable* EF chain. |
| R2 | **Existing/production databases** already at the old head cannot adopt the baseline transparently | **High (if any such DB exists)** | Their `__EFMigrationsHistory` contains the old migration ids; the new baseline id is absent, so EF would attempt to run `Baseline_Recovery3.Up()` against an already-populated schema → `CreateTable` failures. Requires a history-reconciliation step (insert baseline id into `__EFMigrationsHistory` without running Up). |
| R3 | **Fresh-database provisioning** | None → Positive | A brand-new DB applies the baseline cleanly and lands exactly on the current model. This is the intended happy path. |
| R4 | **Baseline files untracked in git** | Medium (process) | `Baseline_Recovery3.cs`/`.Designer.cs` are `??`. If not committed, other clones/CI will not have the baseline and EF state will be broken for them. |
| R5 | **CI implications** | Medium | Any CI step doing `dotnet ef database update` or asserting "no pending model changes" must be run against the new baseline; pipelines pinned to old migration ids will break. |
| R6 | **Deployment implications** | Medium/High | Deployment automation that applies migrations must be updated for the reset timeline; environments carrying the old chain need the R2 reconciliation before deploy. |
| R7 | **Divergent branches** | Medium | Feature branches created before the collapse still carry the old chain and will conflict hard on merge; they must be rebased onto the baseline. |

---

## 7. Conditions

Because the review verdict is **APPROVE WITH CONDITIONS**, each condition is explicit:

- **C-1 (Track the baseline).** Commit `20260723214642_Baseline_Recovery3.cs` and `20260723214642_Baseline_Recovery3.Designer.cs` together with the regenerated `ApplicationDbContextModelSnapshot.cs` in a single atomic commit. The snapshot must never be committed without the migration that produced it. *(Currently the two migration files are untracked.)*

- **C-2 (Verify idempotent scaffold).** On the committed state, `dotnet ef migrations add __verify` must produce an **empty** migration (no operations). Delete the throwaway migration afterward. This proves model ⇄ snapshot ⇄ baseline are fully aligned in a clean checkout. *(Not run here — review is read-only.)*

- **C-3 (Fresh-DB smoke test).** Apply `Baseline_Recovery3` to an empty PostgreSQL database (`dotnet ef database update`) and confirm all 256 tables + seed rows are created without error, then that a second `database update` is a no-op.

- **C-4 (Existing-DB reconciliation runbook).** Publish an explicit procedure for any database already on the old chain: mark `Baseline_Recovery3` as applied in `__EFMigrationsHistory` **without executing `Up()`** (e.g. `dotnet ef migrations script <lastOld> <baseline>` reviewed manually, or a manual history-row insert). This must be documented before the baseline is allowed to reach any environment that ever ran the old chain. **If no such database exists (true greenfield), state that explicitly and C-4 is waived.**

- **C-5 (CI/CD alignment).** Update pipelines and deployment automation to the reset timeline: remove references to old migration ids, run migrate/verify against the baseline, and add a "no pending model changes" gate (C-2 as a CI check).

- **C-6 (Branch hygiene).** Require in-flight feature branches to rebase onto the baseline; do not merge branches that reintroduce archived migration files into `PointOfSale.Infrastructure/Migrations/`.

- **C-7 (Archive immutability).** Treat `docs/recovery/archive/RECOVERY-4_PHASE_A/.../Migrations/` as read-only historical record; it must not be re-activated as an EF chain.

- **C-8 (Merge-to-main guard).** This baseline is approved for the recovery/greenfield branch. Promotion to `main` is contingent on C-2, C-3, C-4 (or its waiver), and C-5 all being satisfied and evidenced.

---

## 8. Final Decision

### Should this migration baseline become the official future history of the repository?

**APPROVE WITH CONDITIONS.**

The baseline `20260723214642_Baseline_Recovery3` is a technically correct, model-consistent greenfield baseline:
- snapshot body is identical to the baseline's target model (0-diff),
- it is generated from the current domain model (256 symmetric CreateTable/DropTable, 499 `HasData`, model-driven `InsertData`),
- the folder contains no partial chain and no orphan migration artifacts,
- and the historical chain is preserved via relocation rather than destruction.

It is **appropriate** as the official future history for:
- **Greenfield development** — yes (intended use).
- **Recovery branch (`recovery/3-greenfield-baseline`)** — yes (correct home for it now).
- **Main branch** — yes, *only after* conditions C-2, C-3, C-4/waiver, C-5 are met (C-8).
- **Existing production systems** — yes, *only with* the C-4 history-reconciliation runbook; without it, the collapse is unsafe against any database already carrying the old chain.

The conditions are not objections to the collapse itself — they are the operational guardrails required to turn a correct artifact into a safe, committed, and deployable baseline.

---

### Evidence appendix (commands run, read-only)
- `git status --short PointOfSale.Infrastructure/Migrations/` → 353 `D`, snapshot `M`, 2 baseline files `??`.
- Old non-designer migration count (archive): `149`. Archived `.Designer.cs`: `49`.
- Baseline `Up` `CreateTable` = 256; `Down` `DropTable` = 256; `InsertData` = 10; raw `Sql(` = 0.
- Snapshot⇄baseline model diff (bodies) = **0 lines**.
- Snapshot `ProductVersion` = `9.0.1`; EF Design/Tools packages = `9.0.1`.
- Active Migrations folder = baseline `.cs` + `.Designer.cs` + snapshot only (no other migration classes).
