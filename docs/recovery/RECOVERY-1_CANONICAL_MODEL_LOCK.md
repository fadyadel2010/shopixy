# RECOVERY-1.0 — CANONICAL MODEL LOCK

## Architectural Baseline Certification — Constitutional Document of the Shopixy Recovery Program

- Date: 2026-07-23
- Status: **BINDING.** Every subsequent recovery phase (RECOVERY-2 … RECOVERY-N) must be derivable from, and must not contradict, this document.
- Method: architectural governance only. No implementation, no migrations, no SQL, no code changes, no database changes were performed. Only this report was written.
- Certified inputs (treated as established fact, not re-derived):
  - Schema Drift Audit (Phase 1) — `docs/audits/SCHEMA_DRIFT_AUDIT_REPORT.md`
  - Evidence Verification (Phase 1.5) — `docs/audits/SCHEMA_DRIFT_AUDIT_REPORT_V2.md`
  - Migration Replay Feasibility Audit (Phase 1.75) — `docs/audits/MIGRATION_REPLAY_FEASIBILITY_AUDIT.md`
  - Migration Completeness Audit (Phase 2) — `docs/audits/MIGRATION_COMPLETENESS_AUDIT.md`
  - Migration Canonicalization Audit (Phase 3) — `docs/audits/MIGRATION_CANONICALIZATION_AUDIT.md`
- Source-tree verification performed for this lock (fresh, this pass, against the real code):
  - `PointOfSale.Infrastructure/Migrations/`: 149 migration classes, 49 `.Designer.cs`, last Designer `20260510130500`, 50 later classes with no Designer, 190 `.sql` files, `ApplicationDbContextModelSnapshot.cs` (auto-generated, ProductVersion 9.0.1, 18,859 lines).
  - `ApplicationDbContext` (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`): 242 `DbSet<>` properties; `ApplyConfigurationsFromAssembly` (line 481); 108 `IEntityTypeConfiguration<>` classes under `Data/Configurations/`.
  - 7 Carrier administration runtime entities exist ONLY in `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs` (CarrierAccount, CarrierCredentialReference, CarrierWarehouseAssignment, CarrierAccountPolicy, CarrierServiceLevelEnablement, CarrierConnectionTestRecord, CarrierAdministrationAudit); zero hits in the snapshot and zero hits anywhere in the migration chain.
  - `AffiliatePayoutRequest.PaidBy` exists as an entity property; its only persistence artifact is a companion raw script `20260626153000_AffiliatePayoutPaidByAuditIdentity.sql` with no wrapping migration class.
  - History math recomputed from the raw `DB/data-1784830579292.csv`: 128 applied, 149 source, 69 intersection, **80 pending**, **59 orphan history rows**, **34 interleaved** (pending Id ≤ last applied `20260511150000_S80_AddTenantsCurrencyDefaultValue`), first pending `20260416003507`. Exact match with the certified audits.
  - Additional artifact classified this pass: legacy directory `PointOfSale.Infrastructure/Data/Migrations/` (pre-squash classes + Designers, e.g. `20251226210606_TenantIsolationHardening`) corresponding to orphan history rows.

---

# Executive Summary

The investigation program (Phases 1 → 3) proved a single architectural truth: **Shopixy has exactly one healthy, complete, internally consistent definition of its persistence model — the Runtime EF Model** (entity classes + `ApplicationDbContext.OnModelCreating` + the `IEntityTypeConfiguration<>` classes). Every other persistence artifact is a derivation, a historical record, or an operational trace — and every one of them has drifted from the authority:

- The model snapshot froze at 2026-05-10 and is 384 operations / 42 entities behind.
- The migration chain is genuine differ-backed history only through `20260510130500`; the 50 later classes are hand-authored deployment scripting; 64 model operations (7 whole tables, 1 column, 1 FK, ~50 indexes) exist in no migration and no SQL.
- The database is an interleaved, partly out-of-band operational state that no linear artifact ever produced; a plain replay against it fails on the first statement of the first pending migration.

This document does not fix any of that. It does something more important: it **locks the authority hierarchy permanently**, so that every future recovery action is a mechanical consequence of a settled constitution rather than a fresh debate. From this point forward:

1. The Runtime EF Model is the sole architectural authority.
2. All other artifacts are assigned exactly one role each (Phase 2 of this document).
3. Ten invariants (Phase 3) bind every future recovery step.
4. Five recovery principles (Phase 4) govern the program.
5. Success criteria and measurable exit gates (Phases 5–6) define completion.
6. The official roadmap (Phase 7) sequences RECOVERY-2 … RECOVERY-6.

## Verdict: CANONICAL MODEL LOCKED

# Canonical Authority

## PHASE 1 — The Runtime EF Model is the ONLY architectural authority

**Definition.** The Canonical Persistence Model of Shopixy is the Runtime EF Model: the in-process
`IModel` that EF Core materializes from —

1. the entity classes under `PointOfSale.Core/Entities/**` (structure), and
2. `ApplicationDbContext` (242 `DbSet<>`s, `OnModelCreating`) plus the 108
   `IEntityTypeConfiguration<>` classes under `PointOfSale.Infrastructure/Data/Configurations/`
   loaded via `ApplyConfigurationsFromAssembly` (`ApplicationDbContext.cs:481`) (mapping,
   constraints, indexes, filters).

**Why it is the authority — architectural evidence:**

1. **Completeness.** It is the only artifact containing the full model: 263 entity types, 4,324
   properties, 866 indexes, 243 check constraints, 113 global query filters (Phase 2, materialized
   in-process). Objects such as the 7 Carrier administration tables
   (`CarrierAccountRuntimeEntities.cs`), `AffiliatePayoutRequests.PaidBy` (class-less companion
   SQL only), the `CustomerAddresses.CurrentVersionId` FK, and ~50 indexes exist in NO other
   artifact. Verified this pass: zero snapshot hits, zero chain hits for the Carrier entities.
2. **Internal consistency.** EF Core built and validated the model in-process with zero errors
   (Phase 2, via `IDesignTimeModel`). No other artifact passes an equivalent self-consistency test.
3. **Primacy by EF Core design.** Every other artifact is *defined as a function of this model*:
   `migrations add` diffs against it, the snapshot is emitted from it, the database is meant to
   conform to it. The tooling's dependency graph is unidirectional — model → derivations — never
   the reverse.
4. **Directional drift signature.** All measured divergence points one way: source ahead of
   snapshot, ahead of chain, ahead of DB. That is the signature of a living authority whose
   derivations lag — not of a corrupted source (Phase 3: "no corruption, only drift").
5. **Runtime binding.** It is the contract the compiled application actually executes against.
   Whatever the DB or chain say, the running system requires exactly this model.

**Consequences (binding):**

- No question of the form "what should the schema be?" may ever again be answered by consulting
  the snapshot, the chain, the SQL corpus, the database, or the history. The answer is always:
  build the Runtime Model and read it.
- Any conflict between the Runtime Model and any other artifact is, by definition, a defect in the
  OTHER artifact.
- The Recovery Program's job is to make the derived artifacts equal the Runtime Model — never to
  adjust the Runtime Model to match a derived artifact.

---

# Artifact Responsibilities

## PHASE 2 — One artifact, one permanent role

| # | Artifact | Permanent architectural role | Authority class |
|---|----------|------------------------------|-----------------|
| 1 | **Runtime EF Model** (entities + `OnModelCreating` + configurations) | DEFINES the persistence model. Sole source of truth. | **CANONICAL** |
| 2 | **Entity classes** (`PointOfSale.Core/Entities/**`) | Structural half of the Runtime Model. | Canonical component |
| 3 | **Fluent configurations** (`Data/Configurations/**` + `ApplicationDbContext`) | Mapping/constraint half of the Runtime Model. | Canonical component |
| 4 | **Model Snapshot** (`ApplicationDbContextModelSnapshot.cs`) | Generated differ-baseline CACHE. Must satisfy `HasDifferences(snapshot, runtime) = FALSE`. Regenerable at will; disposable; never hand-edited; never authoritative. | Derived / Generated |
| 5 | **Migration Chain** (`PointOfSale.Infrastructure/Migrations/*.cs`) | MATERIALIZATION script: the ordered transformation that builds the Runtime Model from an empty database, and the historical record of how it evolved. Never a model definition. | Derived / Historical |
| 6 | **Migration Designer files** (`*.Designer.cs`) | Per-migration frozen snapshots, emitted by tooling only. Historical forensics; never authored or edited by hand. | Derived / Generated |
| 7 | **Raw SQL corpus** (190 `.sql` in the migrations tree) | OPERATIONAL record: business evolution, infrastructure, data migration, seed data, emergency repair. Never defines the model; may only ever be invoked as data/operational scripting, not as schema authority. | Operational / Legacy |
| 8 | **Database Schema** (live DB; `DB/schema-23-7-2026.sql` as evidence) | Deployed operational STATE. Evidence of what was deployed; never evidence of what is intended. May never be reverse-engineered into the model. | Operational, non-authoritative |
| 9 | **`__EFMigrationsHistory`** (`DB/data-1784830579292.csv` as evidence) | Deployment LEDGER: records what was applied where. Records reality, not intent. Never supersedes architecture. | Operational / Historical |
| 10 | **Deployment scripts / CI-CD pipelines** | DELIVERY mechanism only. Execute materialization artifacts; contain no model knowledge of their own. | Operational |
| 11 | **Legacy directory** (`PointOfSale.Infrastructure/Data/Migrations/` — pre-squash classes + Designers) | FROZEN historical archive corresponding to orphan history rows. Read-only forensics. Never part of the active chain; never compiled into the authoritative migration assembly's chain semantics going forward. | Historical only |
| 12 | **`scripts/Migrations/` and root-level ad-hoc SQL (`DB/*.sql` dumps)** | Operational evidence and tooling inputs. Never model authority. | Operational only |
| 13 | **Prior audit reports** (`docs/audits/*.md`, Phases 1–3) | Certified evidence base of the Recovery Program. Facts, not authority over the model. | Evidence |

Rule: an artifact has EXACTLY the role in this table. Any use of an artifact outside its role
(e.g. hand-editing the snapshot, treating a DB dump as the target schema, encoding new model
semantics in raw SQL) is an architectural violation, regardless of expedience.
# Architectural Invariants

## PHASE 3 — Invariants every future recovery step must preserve

- **INV-1 — Immutable authority.** The Runtime EF Model is the immutable architectural authority.
  Recovery work may never modify entity classes or configurations *in order to make a derived
  artifact or the database "match"*. (Normal feature development continues to evolve the model —
  that is authorship, not recovery. Recovery flows model → artifacts, never artifacts → model.)
- **INV-2 — Snapshots are generated.** `ApplicationDbContextModelSnapshot.cs` and all
  `*.Designer.cs` files are tool-emitted. Hand-editing any of them is prohibited, permanently.
- **INV-3 — The chain must materialize the model.** The migration chain's sole success condition
  is: applied in order to an empty database, it produces a schema to which EF's differ reports
  zero differences against the Runtime Model. Any chain that cannot do this is defective.
- **INV-4 — Database state never supersedes source.** No schema shape observed in any database
  (live, backup, dump) may be imported into the model as authority. The DB is conformed TO the
  model; the model is never conformed to the DB.
- **INV-5 — History never supersedes architecture.** `__EFMigrationsHistory` (including its 59
  orphan rows and interleaving) is a deployment ledger. It may be reconciled, baselined, or
  re-stamped as an OPERATIONAL act, but it never dictates what the model or the chain must be.
- **INV-6 — No manual SQL may redefine the model.** Raw SQL may perform data operations, seeding,
  and infrastructure tasks; it may never introduce schema shapes absent from the Runtime Model.
  Every schema object in production must be traceable to the model.
- **INV-7 — Differ-in-the-loop.** After recovery, every model change reaches persistence
  exclusively through `dotnet ef migrations add` (differ-backed, Designer emitted, snapshot
  advanced in lockstep). The Era-2 practice (hand-authored migrations without snapshot advance)
  is permanently prohibited — it is the proven root cause of the entire drift (regime change of
  2026-05-10, Phase 3 Finding 2).
- **INV-8 — Schema recovery and data recovery are separate concerns.** No recovery artifact may
  mix DDL that materializes the model with DML that moves legacy data. They are separate phases
  with separate artifacts and separate rollback semantics.
- **INV-9 — Fail closed.** Any recovery step whose verification gate fails halts the program at
  that gate. No "continue and fix later." (Consistent with platform-wide Shopixy fail-closed
  doctrine.)
- **INV-10 — Evidence discipline.** Every recovery claim ("snapshot now equals model", "empty DB
  equals runtime") must be proven by a mechanical check (differ output, scripted comparison), not
  by assertion. The check and its result are recorded in the phase report.

---

# Recovery Principles

## PHASE 4 — Governing principles of the Recovery Program

- **P-1 — Recover the artifacts, never the authority.** The authority (Runtime Model) already
  exists, is healthy, and is complete. Nothing about it needs recovering. What is broken is its
  MATERIALIZATION (snapshot, chain, DB conformance). The program repairs materialization, not
  definition.
- **P-2 — Repair materialization, not definition.** Every recovery action must be expressible as
  "make artifact X equal to (a projection of) the Runtime Model." Any proposed action not
  expressible in that form is out of scope.
- **P-3 — Separate schema recovery from data recovery.** First make an empty database buildable
  and provably equal to the model (schema track). Only then move legacy data into a conformant
  schema (data track). The two tracks never share artifacts (INV-8).
- **P-4 — Separate deployment history from architecture.** Reconciling `__EFMigrationsHistory`,
  baselining environments, and re-stamping applied rows are operational deployment tasks. They
  happen AFTER the architecture (model + snapshot + chain) is internally whole, and they never
  feed back into it (INV-5).
- **P-5 — Preserve history, do not depend on it.** Era-1 migrations, the legacy
  `Data/Migrations/` archive, the 190 SQL files, and the orphan history rows are retained as
  forensic record. No recovery outcome may REQUIRE them to be replayable; the canonical build path
  must be self-contained from the recovery baseline forward.
# Success Criteria

## PHASE 5 — What "recovered" means

The Recovery Program is successful when ALL of the following hold, each proven mechanically
(INV-10):

| # | Criterion | Mechanical proof |
|---|-----------|------------------|
| SC-1 | **Runtime == Snapshot** | `IMigrationsModelDiffer.HasDifferences(snapshot, runtime) = FALSE`; `dotnet ef migrations has-pending-model-changes` reports none. |
| SC-2 | **Runtime == Migration Baseline** | The recovery baseline migration (plus any subsequent differ-backed migrations) cumulatively encodes exactly the Runtime Model; its Designer snapshot equals the current snapshot. |
| SC-3 | **Baseline builds an empty database** | `dotnet ef database update` (or the generated idempotent script) runs to completion on an empty PostgreSQL database with zero errors. |
| SC-4 | **Empty database == Runtime** | Schema comparison of the freshly built DB against the Runtime Model (EF scaffold/differ or schema-diff tooling) yields zero differences. |
| SC-5 | **Legacy data migrates independently** | A data-only migration path (ETL / DML scripts) moves production data from the drifted legacy DB into a conformant schema, with row-count and integrity verification, and with zero DDL content (INV-8). |
| SC-6 | **CI/CD deploys automatically** | The pipeline can deploy from source to a fresh environment AND upgrade a baselined environment unattended, using only artifacts in their locked roles; no manual SQL, no hand-holding. |
| SC-7 | **Workflow invariant enforced** | Guardrails exist (CI check) that fail the build if `has-pending-model-changes` is true or a migration lacks a Designer file — making Era-2 recurrence impossible (INV-7). |

---

# Exit Gates

## PHASE 6 — Measurable gates to leave RECOVERY-1

RECOVERY-1 is a governance phase; its gates are about ratification, not implementation.

| Gate | Requirement | Pass condition |
|------|-------------|----------------|
| G1-1 | Constitutional document exists | `docs/recovery/RECOVERY-1_CANONICAL_MODEL_LOCK.md` committed to the repository. |
| G1-2 | Authority locked | Runtime EF Model declared sole authority; certification line reads CANONICAL MODEL LOCKED. |
| G1-3 | Every persistence artifact has exactly one role | Phase-2 table covers all 13 artifact classes present in the repo, including the legacy `Data/Migrations/` archive and `scripts/Migrations/`; no artifact is unclassified or dual-role. |
| G1-4 | Invariants explicit and complete | INV-1 … INV-10 enumerated; each future phase can cite the invariant it preserves. |
| G1-5 | Principles explicit | P-1 … P-5 enumerated. |
| G1-6 | Success criteria mechanical | SC-1 … SC-7 each paired with an objective, tool-verifiable proof. |
| G1-7 | Roadmap ratified | RECOVERY-2 … RECOVERY-6 defined with objective + entry/exit dependency on prior phase. |
| G1-8 | Zero side effects | This phase changed no code, no migrations, no snapshot, no SQL, no database, no history. |

All eight gates are satisfied by this document. Subsequent phases each define their own gates and
may not begin until the preceding phase's gates are green (INV-9).

---

# Recovery Roadmap

## PHASE 7 — Official roadmap (architecture only; no implementation here)

```
RECOVERY-1  Canonical Model Lock                 (this document)
    |
RECOVERY-2  Snapshot & Baseline Rebuild          (schema-definition track)
    |
RECOVERY-3  Greenfield Materialization Proof     (schema-verification track)
    |
RECOVERY-4  History & Environment Reconciliation (deployment-ledger track)
    |
RECOVERY-5  Legacy Data Recovery                 (data track)
    |
RECOVERY-6  Continuous Delivery Hardening        (workflow-guarantee track)
```

- **RECOVERY-2 — Snapshot & Baseline Rebuild.** Objective: produce derived artifacts that equal
  the Runtime Model — a regenerated snapshot satisfying SC-1 and a migration baseline satisfying
  SC-2 (closing the 384-op / 42-entity snapshot gap and the 64-op chain gap, including the 7
  Carrier tables and `PaidBy`). Strategy selection (consolidated baseline vs chain completion) is
  decided INSIDE this phase under INV-1..7; this document does not pre-decide it. Exit: SC-1 and
  SC-2 proven.
- **RECOVERY-3 — Greenfield Materialization Proof.** Objective: prove the baseline builds an empty
  database identical to the Runtime Model (SC-3, SC-4), and that the application boots against it.
  Exit: differ-zero certification against a from-zero database.
- **RECOVERY-4 — History & Environment Reconciliation.** Objective: bring
  `__EFMigrationsHistory` and each real environment onto the new baseline as an OPERATIONAL act
  (re-stamp/baseline), resolving the 59 orphan rows and 34-migration interleave without touching
  architecture (INV-5, P-4). Exit: every environment's history is linear from the baseline; a
  no-op `database update` succeeds unattended.
- **RECOVERY-5 — Legacy Data Recovery.** Objective: migrate production data from the drifted
  legacy schema into a conformant schema via a DDL-free data track (SC-5, INV-8), with integrity
  verification and rollback plan. Exit: data parity certified.
- **RECOVERY-6 — Continuous Delivery Hardening.** Objective: automate deployment (SC-6) and
  install the permanent guardrails (SC-7) that enforce INV-7 forever (CI failure on pending model
  changes or Designer-less migrations). Exit: unattended pipeline deploy to fresh + existing
  environments; guardrails demonstrated to fail a violating commit.

---

# Final Certification

The Runtime EF Model (entity classes + `ApplicationDbContext.OnModelCreating` + the
`IEntityTypeConfiguration<>` classes) is hereby locked as the sole, permanent architectural
authority for the Shopixy persistence model. All other artifacts hold exactly the single role
assigned in this document. The invariants, principles, success criteria, exit gates, and roadmap
above are binding on every subsequent recovery phase.

## CANONICAL MODEL LOCKED
