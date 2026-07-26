# RECOVERY-2 — SNAPSHOT & BASELINE REBUILD STRATEGY

## Architectural Decision Record (ADR-001)

- Date: 2026-07-23
- Status: **DECISION RECORD.** No implementation, no migrations, no snapshot, no SQL, no code, no database changes were performed. Only this document was written.
- Authoring roles: Principal Software Architect · Principal EF Core Architect · Principal Database Recovery Engineer · Principal Release Engineer.
- Governing constitution: `docs/recovery/RECOVERY-1_CANONICAL_MODEL_LOCK.md` (BINDING). This ADR is derived from, and does not contradict, RECOVERY-1.
- Method: **source-code driven.** Every load-bearing fact below was re-derived this pass from the live source tree. Prior audit reports and RECOVERY-1 are cited as certified evidence only, never as primary truth.

---

# Executive Summary

RECOVERY-1 locked the Runtime EF Model as the sole persistence authority and deferred the choice of *how* to materialize it into RECOVERY-2. This ADR makes that choice.

Fresh source inspection this pass produced a decisive new fact the prior reports did not capture: **the drift has not merely persisted since RECOVERY-1 — it has actively grown.** The migration chain now holds **149 classes with only 49 Designer files (100 Designer-less, hand-authored)**, up from the 50 Designer-less classes recorded in RECOVERY-1. The model snapshot is still frozen at the identical point (`20260510130500`, ProductVersion 9.0.1, 18,859 lines). Of the 100 post-freeze hand-authored migrations, **47 emit non-idempotent strongly-typed builder DDL** (`CreateTable`/`AddColumn`/`CreateIndex`/`DropColumn`/`AddForeignKey`) with no advancing Designer or snapshot. The Era-2 anti-pattern identified in RECOVERY-1 (INV-7 root cause) is not dormant; it is the team's *current daily practice* and is compounding.

Recomputed persistence topology (this pass, from raw source):

- **Runtime model authority:** 242 `DbSet<>` in `ApplicationDbContext`; 123 `IEntityTypeConfiguration<>` classes under `Data/Configurations/` (up from 108 in RECOVERY-1); loaded via `ApplyConfigurationsFromAssembly` (`ApplicationDbContext.cs:481`).
- **Snapshot:** frozen at `20260510130500`; Carrier administration runtime entities and `AffiliatePayoutRequests.PaidBy` still absent (0 snapshot hits, 0 chain-class hits, this pass).
- **History math (raw `DB/data-1784830579292.csv`):** 127 applied, 149 source classes, 69 intersection, **80 pending**, **58 orphan history rows**, **34 interleaved** (pending id below last-applied `20260511150000_S80`), first pending `20260416003507`.
- **Legacy directory:** `Data/Migrations/` — 40 classes, 38 Designers, 12 `.sql` — corresponds to the orphan history rows.

Given that (a) the snapshot is 74 days and 100 hand-authored migrations behind, (b) the chain contains an interleaved, partly out-of-band set that provably fails a linear replay at its first statement (Phase 1.75 audit, re-confirmed structurally this pass), and (c) the anti-pattern is *still active*, completing the existing chain (Candidate A) is architecturally unsound. The recommendation is **Candidate C — Hybrid: a consolidated canonical baseline that closes the chain at the frozen point, plus retention of the historical chain as a frozen archive, followed by mandatory differ-in-the-loop guardrails.**

## Verdict: **BASELINE STRATEGY CONDITIONALLY APPROVED**

(Conditions enumerated in RECOVERY-3 Entry Criteria; the binding condition is that the anti-pattern must be frozen — no new hand-authored migrations — before the baseline is cut.)

---

# Fresh Source Verification

Everything in this section was observed directly from the source tree this pass. Numbers were recomputed, not copied.

## Runtime model (authority)

- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`: **242** `public DbSet<` properties; `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)` at **line 481**.
- `PointOfSale.Infrastructure/Data/Configurations/`: **123** files containing `IEntityTypeConfiguration<>` (RECOVERY-1 recorded 108 — the model has grown 15 configurations since the lock, consistent with continued authorship).
- Entity structural half: `PointOfSale.Core/Entities/**`. The 7 Carrier administration runtime entities file `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs` **exists**.

## Snapshot (derived cache)

- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`: present, **18,859 lines**, `ProductVersion 9.0.1`.
- `grep CarrierAccount` snapshot → **0 hits**. `grep CarrierAccount` across migration `.cs` classes → **0 files**. The 7 Carrier tables remain unmaterialized in every derived artifact; they live only in the Runtime Model. (Matches RECOVERY-1.)
- `AffiliatePayoutRequests.PaidBy`: only persistence artifact is companion raw script `20260626153000_AffiliatePayoutPaidByAuditIdentity.sql` — **no wrapping migration class** (confirmed this pass).
## Migration chain (materialization / historical)

- `PointOfSale.Infrastructure/Migrations/`: **149** migration classes (excl. `.Designer.cs`, `ModelSnapshot`); **49** `.Designer.cs`; **97** `.sql` companion files.
- Earliest class `20251123190510_InitialCreate`; latest `20260722130000_HardenProviderDeliveryOutbox`.
- Last Designer file: `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped`. **The Designer chain froze here.**
- **Classes with no Designer file: 100** (RECOVERY-1 recorded 50). The hand-authored, snapshot-less era has *doubled* since the lock. This is the single most important new observation in this ADR.
- Of those 100 post-freeze classes, **47 emit strongly-typed builder DDL** (`CreateTable`, `AddColumn`, `CreateIndex`, `DropColumn`, `AddForeignKey`, `AlterColumn`). Example: `20260722130000_HardenProviderDeliveryOutbox.cs` — 5 `AddColumn`, 1 `CreateTable`, 2 `CreateIndex`, 5 `DropColumn`, all non-idempotent builder calls, no Designer, snapshot not advanced.

## SQL topology

- Active migrations tree `.sql`: **97**.
- Legacy `Data/Migrations/` `.sql`: **12**.
- `scripts/Migrations/` `.sql`: **1** (`20260117_AddPromotionSearchBoostOverride.sql`).
- `DB/*.sql` ad-hoc dumps: **21** (includes evidence `schema-23-7-2026.sql`).
- (RECOVERY-1's "190 sql" figure counted a broader/earlier set; the active migrations tree today holds 97. Difference noted, not reconciled — SQL corpus is operational, non-authoritative per RECOVERY-1 Phase-2 role #7.)

## Legacy topology

- `PointOfSale.Infrastructure/Data/Migrations/`: **40** classes, **38** Designers, **12** `.sql`. This is the pre-squash archive corresponding to the 58 orphan history rows. Frozen forensic archive (RECOVERY-1 role #11).

## History / deployment topology (recomputed from raw `DB/data-1784830579292.csv`)

| Metric | This pass | RECOVERY-1 |
|--------|-----------|------------|
| Applied history rows | 127 | 128 |
| Source classes | 149 | 149 |
| Intersection (applied ∩ source) | 69 | 69 |
| Pending (source − applied) | **80** | 80 |
| Orphan history (applied − source) | **58** | 59 |
| Interleaved (pending id < last applied) | **34** | 34 |
| Last applied id | `20260511150000_S80_AddTenantsCurrencyDefaultValue` | same |
| First pending id | `20260416003507_FixSmartOfferCoupon…` | same |

The 1-row deltas (127 vs 128 applied, 58 vs 59 orphan) reflect the CSV parse boundary (header handling); pending/interleaved/intersection are an exact match, which cross-validates the computation. **Interpretation is identical to RECOVERY-1: a plain `dotnet ef database update` replays the 80 pending migrations in MigrationId order — starting at the OLDEST pending id `20260416003507` (34 of which sit *below* the last-applied id) — and fails on the first non-idempotent statement it meets, inside its own transaction, advancing nothing.**

## Deployment topology

- No CI-embedded EF automation was found tying `database update`/idempotent-script generation into the pipeline in a differ-guarded way; migrations are hand-authored and committed directly (git log shows sequential `feat: update system improvements newNNN` commits touching the migrations dir). This is consistent with the absence of an INV-7 guardrail.

## Net fresh conclusion

The persistence architecture today is *strictly worse* than at RECOVERY-1 along the one axis that matters: the gap between the frozen snapshot and the live model has widened by 50 additional hand-authored migrations, 47 of them carrying non-idempotent DDL. No amount of chain surgery converges while the anti-pattern remains live. This directly shapes the strategy decision below.
# Current Architecture

Reconstructed from source (Phase 1). The persistence layer exists in three temporal eras, provable from the Designer freeze:

- **Era-1 (differ-backed, healthy): `20251123190510` → `20260510130500`.** 49 migrations each with a Designer file; snapshot advanced in lockstep. This is genuine `dotnet ef migrations add` history. It correctly materializes the model *as of 2026-05-10*.
- **Era-2 (hand-authored, snapshot-less): `20260511…` → `20260722130000`.** 100 migration classes, zero Designers, snapshot never advanced. 47 carry non-idempotent builder DDL; the rest are `migrationBuilder.Sql(...)` blocks and companion `.sql`. This is the drift engine. It is **still running today.**
- **Legacy (frozen archive): `Data/Migrations/`.** 40 classes / 38 Designers, orphan-history-linked, pre-squash. Read-only forensics.

Relationship of artifacts to the locked authority:

```
Runtime EF Model  (CANONICAL — 242 DbSets, 123 configs, entities/**)
    │  emits →   Snapshot   [FROZEN 2026-05-10; 384-op / 42-entity gap per RECOVERY-1]
    │  diffs →   Migration chain
    │              ├─ Era-1 (49) : differ-backed, matches model @ 2026-05-10
    │              └─ Era-2 (100): hand-authored, unverifiable, 47 non-idempotent
    │  conforms→ Database (drifted, interleaved, out-of-band)  ← 80 pending / 34 interleaved / 58 orphan
    └─ NOT materialized anywhere: 7 Carrier tables, PaidBy col, CustomerAddresses.CurrentVersionId FK, ~50 indexes
```

The architecture has exactly one healthy authority and three broken derivations. The snapshot cannot be trusted as a differ baseline; the chain cannot be linearly replayed; the DB cannot be reverse-engineered (INV-4).

---

# Candidate Strategies

## Candidate A — Complete the existing migration chain

Add hand-authored (or differ-generated) migrations on top of the current 149 to close the 384-op snapshot gap and the 64-op chain gap, then keep the full chain replayable.

- **Architectural correctness:** LOW. The snapshot is frozen at `20260510130500`, so `dotnet ef migrations add` cannot generate a correct diff — the differ baseline is 100 migrations stale. Any generated migration would attempt to re-create 74 days of already-hand-applied objects. Correctness is unrecoverable without first fixing the snapshot, at which point the "existing chain" is no longer what is being completed.
- **Risk:** HIGH. 47 non-idempotent Era-2 migrations remain in the replay path; the interleaved 34-migration set still fails first-statement (Phase 1.75). Completing the chain does not remove the un-replayable segment.
- **Complexity:** HIGH. Requires reconciling 100 snapshot-less migrations retroactively, hand-emitting Designers, or force-advancing the snapshot — each an INV-2 violation risk.
- **Recoverability:** LOW. Leaves the un-replayable interleave permanently in the canonical build path (violates P-5: canonical path must be self-contained).
- **Long-term maintenance:** POOR. Preserves the exact structure that produced the drift.
- **CI/CD compatibility:** POOR. No from-zero guarantee (SC-3 unreachable).
- **Developer workflow:** Unchanged — i.e. the anti-pattern survives.
- **Operational risk:** HIGH.
- **Future extensibility:** LOW.
- **Rollback complexity:** HIGH (long chain, mixed idempotency).

## Candidate B — Generate a new canonical baseline (greenfield squash)

Regenerate the snapshot from the Runtime Model, emit a single baseline migration encoding the full model from empty, and start a fresh chain. Discard the old chain from the *build path* (retain as archive).

- **Architectural correctness:** HIGH. The baseline is emitted directly from the locked authority (`IDesignTimeModel` → snapshot → single `CreateTable` baseline), so by construction `HasDifferences(snapshot, runtime) = FALSE` (SC-1) and the baseline materializes the model from empty (SC-2/SC-3/SC-4 reachable).
- **Risk:** MEDIUM. The risk is entirely operational (existing environments must be baselined/re-stamped, not rebuilt) — deferred to RECOVERY-4, isolated by INV-5/P-4.
- **Complexity:** MEDIUM. One regeneration + one baseline + guardrails. Mechanically simple; the hard part is discipline, not code.
- **Recoverability:** HIGH. Canonical build path is self-contained from the baseline forward (satisfies P-5).
- **Long-term maintenance:** EXCELLENT. Clean differ-backed chain from a known-good baseline.
- **CI/CD compatibility:** EXCELLENT. From-zero build is provable (SC-3/SC-4); guardrails enforceable (SC-7).
- **Developer workflow:** RESET to differ-in-the-loop (INV-7 satisfied).
- **Operational risk:** MEDIUM (existing-env baselining), fully addressable in RECOVERY-4.
- **Future extensibility:** EXCELLENT.
- **Rollback complexity:** LOW (single baseline).
- **Cost of the pure form:** loses granular per-migration history from the *build path* (retained as archive), and requires every environment to be re-stamped.

## Candidate C — Hybrid (consolidated baseline + frozen archive + guardrails)

Cut a **consolidated canonical baseline** from the Runtime Model at the current point (regenerate snapshot from authority; emit one baseline migration = full model from empty). **Freeze the entire existing 149-class chain + legacy dir as a read-only historical archive** (RECOVERY-1 roles #5/#6/#11 preserved). Re-stamp environments onto the baseline as an operational act (RECOVERY-4). Install differ-in-the-loop guardrails (SC-7) so all *future* migrations are differ-backed on top of the baseline.

- Combines Candidate B's clean self-contained build path with explicit **history preservation** (P-5: preserve history, do not depend on it) and an explicit **anti-pattern freeze** as the entry condition.
- Every RECOVERY-1 invariant is satisfiable: INV-1 (model untouched), INV-2 (snapshot regenerated by tooling), INV-3 (baseline materializes model — provable in RECOVERY-3), INV-4/5 (DB/history conformed, not imported), INV-7 (guardrails), INV-8 (schema baseline carries zero DML).
- **Risk / Complexity / CI-CD / maintenance / extensibility:** same profile as B, plus lower forensic-loss risk (chain retained) and lower governance risk (the freeze is a named gate).

## Candidate D — Discovered alternative: "Snapshot-refresh + selective chain repair"

Regenerate only the snapshot from the model (fix SC-1) but keep the full existing chain, hand-repairing the 47 non-idempotent Era-2 migrations into idempotent `Sql`-guarded form so the chain replays.

- **Architectural correctness:** MEDIUM. Fixes SC-1 but leaves a 149-migration chain whose Era-2 half is hand-maintained forever.
- **Risk:** HIGH. Manually rewriting 47 migrations to idempotent form is error-prone; each rewrite is an un-differ-backed edit (INV-7 tension).
- **Complexity:** VERY HIGH (per-migration manual idempotency surgery).
- **Recoverability / maintenance / extensibility:** MEDIUM/POOR. Does not reset the workflow; the chain stays fragile and long.
- Rejected as a strictly worse Candidate C (all the effort of a baseline, none of the clean-slate benefit).
# Decision Matrix

Weighted comparison. Weights reflect RECOVERY-1 priorities (correctness and operational safety dominate; the program exists because the workflow was unsafe). Scores 1–5 (5 best), each anchored to source evidence, not sentiment.

| Criterion | Weight | A: Complete chain | B: New baseline | C: Hybrid | D: Refresh+repair |
|-----------|:------:|:-----------------:|:---------------:|:---------:|:-----------------:|
| Correctness (materializes locked model) | 5 | 1 | 5 | 5 | 3 |
| Simplicity | 3 | 1 | 4 | 4 | 1 |
| Maintainability | 4 | 1 | 5 | 5 | 2 |
| Operational safety | 5 | 1 | 4 | 5 | 2 |
| Automation (CI/CD, SC-3/6/7) | 4 | 1 | 5 | 5 | 2 |
| Future-proofing | 4 | 1 | 5 | 5 | 2 |
| Developer experience (INV-7 reset) | 3 | 1 | 5 | 5 | 2 |
| Recovery confidence (self-contained path, P-5) | 5 | 1 | 4 | 5 | 3 |
| History preservation (P-5 forensics) | 2 | 4 | 2 | 5 | 5 |
| **Weighted total** (max 175) | | **35** | **151** | **169** | **80** |

Score derivation notes (evidence-anchored):
- **A scores 1 on correctness** because the snapshot is frozen 100 migrations back; the differ cannot produce a valid diff, so the chain cannot be validly completed (Fresh Source Verification: last Designer `20260510130500`).
- **B and C score 5 on correctness** because the baseline is emitted from the locked Runtime Model itself; SC-1 is true by construction.
- **C beats B** on operational safety, recovery confidence, and history preservation because C names the anti-pattern freeze as an explicit entry gate and retains the full chain as archive rather than implicitly discarding it.
- **D** loses on simplicity/maintainability because it mandates manual idempotency surgery on 47 migrations (Fresh Source Verification), each edit un-differ-backed.

**Winner: Candidate C (Hybrid), 169/175.**

---

# Cost Analysis

Estimates are engineering-effort orders of magnitude, not calendar commitments; they compare strategies, and assume the anti-pattern freeze is in place.

## Engineering cost

- **A:** Very high and *open-ended* — reconciling 100 snapshot-less migrations + repairing the interleave has no bounded endpoint while the model keeps moving. Effort recurs every sprint.
- **B:** Bounded — one snapshot regeneration + one baseline emit + guardrail wiring. ~days.
- **C:** Same bounded core as B + archive freeze bookkeeping + a documented environment re-stamp runbook (executed in RECOVERY-4). ~days.
- **D:** High — 47 manual migration rewrites + snapshot regen + per-migration replay testing. ~weeks, fragile.

## Technical risk

- **A / D:** HIGH — leave non-idempotent, un-replayable segments in the canonical path; first-failure on replay persists (Phase 1.75).
- **B / C:** MEDIUM and *contained* — the only residual risk is environment baselining, isolated to RECOVERY-4 by INV-5/P-4. Schema correctness is provable in RECOVERY-3 (SC-3/SC-4).

## Maintenance cost

- **A:** Permanent — a 149+ migration chain with a hand-maintained half.
- **D:** Permanent — 149 chain, 47 hand-idempotent migrations to keep correct.
- **B / C:** Minimal — short differ-backed chain from a clean baseline; guardrails prevent regression.

## Future migration cost

- **A / D:** Every new migration diffs against a compromised/long chain; risk of re-drift high.
- **B / C:** Every new migration is a clean differ-backed add on a valid snapshot. Lowest possible.

## Onboarding cost

- **A / D:** New engineers must understand 3 eras, the interleave, and manual idempotency rules — high cognitive load.
- **B / C:** New engineers see one baseline + a normal differ-backed chain + a CI guardrail that teaches the rule automatically. Lowest.

**Cost conclusion:** C and B are the only bounded-cost options. C carries marginally more bookkeeping than B but eliminates forensic-loss risk and makes the governance gate explicit — a worthwhile trade given RECOVERY-1's evidence-discipline mandate (INV-10).

---

# Recommended Strategy

## RECOMMENDATION: Candidate C — Consolidated Canonical Baseline (Hybrid)

Regenerate the model snapshot directly from the locked Runtime EF Model; emit a single consolidated baseline migration that materializes the entire model from an empty database; freeze the existing 149-class chain and the legacy `Data/Migrations/` directory as a read-only historical archive; re-stamp existing environments onto the baseline as an operational act (RECOVERY-4); and install differ-in-the-loop CI guardrails so every future migration is differ-backed on top of the baseline.

### Justification (from source evidence)

1. **It is the only strategy whose correctness is provable against the locked authority.** The baseline is emitted *from* the Runtime Model (242 DbSets, 123 configs), so `HasDifferences(snapshot, runtime) = FALSE` holds by construction (SC-1), and the baseline materializes exactly the model from empty (SC-2). Evidence: the model is the sole complete artifact (7 Carrier tables + PaidBy exist nowhere else — 0 snapshot/chain hits this pass).
2. **It removes the un-replayable segment from the canonical path.** The 34-migration interleave and 47 non-idempotent Era-2 migrations that guarantee first-statement replay failure (Phase 1.75, structurally re-confirmed) are retired to archive, not carried forward. Satisfies P-5 (preserve history, do not depend on it) and makes SC-3 reachable.
3. **It stops the bleeding.** Fresh evidence shows the anti-pattern doubled (50→100 Designer-less classes) since RECOVERY-1. Only a workflow reset + guardrails (INV-7, SC-7) prevents a RECOVERY-7. A and D leave the workflow intact.
4. **It respects every invariant.** INV-1 (model untouched), INV-2 (snapshot tool-regenerated), INV-3 (baseline materializes model — proven next phase), INV-4/INV-5 (DB/history conformed, never imported), INV-8 (baseline is pure DDL), INV-10 (every claim mechanically checkable).

### Why every other strategy is rejected

- **Candidate A — rejected.** The snapshot is frozen 100 migrations behind (last Designer `20260510130500`), so the differ cannot generate a valid completion; the interleaved/non-idempotent segment remains un-replayable; the anti-pattern is preserved. Correctness score 1/5. Fails SC-1, SC-2, SC-3.
- **Candidate B — rejected in favor of C (not on merit).** B is architecturally sound (151/175) but implicitly discards the historical chain and does not name the anti-pattern freeze as a gate. C is B plus explicit history retention (P-5) and an explicit governance gate — strictly dominant.
- **Candidate D — rejected.** All the cost of a baseline plus manual idempotency surgery on 47 migrations, while keeping a fragile 149-migration chain and *not* resetting the workflow. Strictly worse than C.
# Recovery Blueprint

Target architecture after RECOVERY-2/3 execute (described only; nothing implemented here).

```
                    ┌─────────────────────────────────────────────┐
                    │            RUNTIME EF MODEL                  │
                    │  (CANONICAL — sole authority, INV-1)         │
                    │  entities/**  +  ApplicationDbContext        │
                    │  + 123 IEntityTypeConfiguration<>            │
                    └───────────────┬─────────────────────────────┘
                                    │ dotnet ef (tooling only)
                 ┌──────────────────┼──────────────────┐
                 ▼                  ▼                  ▼
        ┌────────────────┐ ┌────────────────┐ ┌────────────────────┐
        │   SNAPSHOT     │ │   BASELINE      │ │  FUTURE CHAIN      │
        │ regenerated    │ │  MIGRATION      │ │  differ-backed     │
        │ from model     │ │  (full model    │ │  adds on top of    │
        │ HasDifferences │ │   from empty,   │ │  baseline; each    │
        │  = FALSE (SC-1)│ │  Designer+snap  │ │  has a Designer    │
        └───────┬────────┘ │  in lockstep)   │ │  (INV-7 enforced)  │
                │          └───────┬─────────┘ └─────────┬──────────┘
                │                  │                      │
                │                  ▼                      │
                │        ┌───────────────────┐           │
                │        │  DEPLOYMENT (CI/CD)│◄──────────┘
                │        │  from-zero build   │
                │        │  + baselined-env   │
                │        │  upgrade, no manual │
                │        │  SQL (SC-3/4/6)    │
                │        └─────────┬─────────┘
                ▼                  ▼
        ┌───────────────┐ ┌───────────────────┐
        │ DATA RECOVERY │ │  LIVE DATABASE     │
        │ DDL-free ETL  │ │  conformed to      │
        │ (RECOVERY-5,  │ │  baseline; history │
        │  INV-8, SC-5) │ │  re-stamped        │
        └───────────────┘ │  (RECOVERY-4)      │
                          └───────────────────┘

   FROZEN ARCHIVE (read-only forensics, P-5):
     • existing 149-class chain + 49 Designers + 97 sql
     • legacy Data/Migrations/ (40 classes / 38 Designers / 12 sql)
     • 58 orphan history rows, 34-migration interleave
```

## How the pieces interact

- **Authority → Snapshot:** the snapshot is re-emitted from the Runtime Model and becomes the differ baseline. It is disposable/regenerable and never hand-edited (INV-2).
- **Authority → Baseline:** one consolidated migration encodes the full model as `CreateTable`/index/constraint DDL from an empty DB, with its Designer equal to the new snapshot (SC-2).
- **Baseline → Future chain:** every subsequent model change flows through `dotnet ef migrations add` — differ-backed, Designer emitted, snapshot advanced in lockstep (INV-7). No hand-authored migrations, ever.
- **Baseline → Deployment:** CI builds a from-zero DB from the baseline and proves differ-zero against the model (SC-3/SC-4, RECOVERY-3); existing environments are upgraded via re-stamp (RECOVERY-4).
- **Deployment → Data recovery:** once a conformant empty schema is provable, legacy production data is moved in via a **DDL-free** data track (INV-8, SC-5, RECOVERY-5).
- **Archive:** everything retired stays in-repo, read-only, for forensics. Nothing in the canonical build path depends on it (P-5).

## Authority / Baseline / Snapshot / Chain / Deployment / Data / CI-CD roles

- **Authority:** Runtime EF Model (unchanged, immutable per INV-1).
- **Baseline:** single consolidated migration = model-from-empty.
- **Snapshot:** regenerated cache; `HasDifferences = FALSE`.
- **Migration chain:** archive (frozen) + new differ-backed chain (baseline forward).
- **Deployment:** CI executes baseline + chain; no manual SQL in the schema path.
- **Data recovery:** separate DDL-free ETL track.
- **CI/CD:** guardrail fails build on `has-pending-model-changes` or a Designer-less migration (SC-7).

---

# RECOVERY-3 Entry Criteria

Before any RECOVERY-3 (Greenfield Materialization Proof) implementation begins, ALL of the following must exist / be true. These are the conditions under which this ADR's approval is granted.

| # | Entry criterion | Verification |
|---|-----------------|--------------|
| E-1 | **Anti-pattern frozen.** No new hand-authored (Designer-less) migration has been added since this ADR; a moratorium is in force until the baseline is cut. | `ls Migrations/*.cs` without a Designer count does not exceed 100. |
| E-2 | RECOVERY-1 gates G1-1…G1-8 remain green; this ADR committed at `docs/recovery/RECOVERY-2_SNAPSHOT_BASELINE_STRATEGY.md`. | File present in repo. |
| E-3 | Clean build of `PointOfSale.Infrastructure` + design-time model loads with zero errors. | `dotnet build`; `dotnet ef dbcontext info` (design-time only). |
| E-4 | Baseline emission plan documented: exact commands to regenerate snapshot from the model and emit the consolidated baseline, with the archive-freeze step. | Runbook section in RECOVERY-3 plan. |
| E-5 | A disposable empty PostgreSQL target is available for the from-zero proof (no production DB touched). | Connection string to a scratch DB. |
| E-6 | Differ harness ready: `IMigrationsModelDiffer.HasDifferences(snapshot, runtime)` and `dotnet ef migrations has-pending-model-changes` both runnable in CI. | Harness script committed. |
| E-7 | Archive plan approved: existing 149-class chain + legacy dir marked read-only, excluded from the active chain semantics but retained in-repo. | Documented in RECOVERY-3 plan. |
| E-8 | Environment re-stamp deferred to RECOVERY-4 and explicitly OUT of RECOVERY-3 scope (INV-5/P-4). | Scope statement. |

RECOVERY-3 may not begin until E-1…E-8 are all green (INV-9 fail-closed). **E-1 is the binding condition of this ADR's conditional approval.**

---

# Final Certification

Fresh source inspection this pass confirms RECOVERY-1's authority lock and adds one decisive fact: the Era-2 hand-authored, snapshot-less anti-pattern has doubled (50→100 Designer-less migrations, 47 non-idempotent) while the snapshot remained frozen at `20260510130500`. Completing the existing chain (A) or repairing it in place (D) is architecturally unsound and unbounded in cost. A consolidated canonical baseline emitted from the locked Runtime Model, with the historical chain retained as a frozen archive and differ-in-the-loop guardrails installed (Candidate C), is the correct permanent foundation. Its correctness is provable against the authority (SC-1/SC-2 by construction; SC-3/SC-4 in RECOVERY-3).

Approval is **conditional** solely on the anti-pattern freeze (Entry Criterion E-1): the baseline must be cut against a model that is no longer being extended by hand-authored migrations, or the drift simply resumes.

## BASELINE STRATEGY CONDITIONALLY APPROVED
