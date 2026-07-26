# MIGRATION CANONICALIZATION AUDIT — PHASE 3: CANONICAL MODEL CERTIFICATION

Date: 2026-07-23
Scope: Determine the **canonical source of truth** for the Shopixy persistence model.
Method: **READ-ONLY architectural certification.** No database execution, no migration
execution, no code/snapshot/migration/history modification. Only this report was written.
Authoritative inputs: entire source tree, `ApplicationDbContext` + `IEntityTypeConfiguration<>`
classes, entity classes, `ApplicationDbContextModelSnapshot.cs`, all 149 migration classes + 49
Designer files + 190 SQL files, and the three prior certified audits (Schema Drift Phase 1/1.5,
Replay Feasibility Phase 1.75, Migration Completeness Phase 2).

This is **not** another schema/migration/replay audit. The prior findings below are treated as
**established fact** and are not re-derived:

- Schema Drift (Phase 1/1.5): **Database ≠ Source** — 22 missing tables, 11 missing-column
  groups, interleaved history, out-of-band SQL. CERTIFIED.
- Replay Feasibility (Phase 1.75): **Replay against the current DB FAILS** at the first
  statement of the lowest-Id pending migration. CERTIFIED.
- Migration Completeness (Phase 2): **Runtime Model ≠ Snapshot ≠ Chain** — 384 differ ops, 42
  entities absent from snapshot, 64 ops migrated by no mechanism. CERTIFIED.

The mission here is different: **which artifacts represent the intended architecture, and what
SHOULD define the persistence model.**

---

# Executive Summary

The Shopixy persistence model has **exactly one authoritative definition: the runtime EF Core
model** — the C# entity classes plus `ApplicationDbContext.OnModelCreating` and the
`IEntityTypeConfiguration<>` classes. This is the only artifact that is complete, internally
consistent, and actively maintained. It is the artifact the running application binds to and the
artifact every other persistence artifact is *supposed* to be derived from.

Every other artifact is **derived, historical, or operational** — and all of them have diverged
from the runtime model:

- The **model snapshot** is a derived cache that **froze at 2026-05-10** and is now 384 operations
  and 42 entities behind the runtime model. It is **regenerable, therefore disposable** as an
  authority — it holds no information the runtime model lacks, and it is currently *wrong*.
- The **EF migration chain** is a **partially-canonical historical record**. Up to
  `20260510130500` it is genuine tool-generated history (49 Designer files, snapshot advanced in
  lockstep). After that point 50 hand-authored migrations were added without `dotnet ef migrations
  add`, so the chain became a manually-curated deployment script rather than a differ-backed
  history. It can no longer reconstruct the runtime model (Phase 2: 64 ops, incl. 7 whole tables,
  exist nowhere in it) and it can no longer replay against the live DB (Phase 1.75).
- The **190 SQL files** are **operational deployment artifacts** (business evolution + manual
  repair + infrastructure), not a canonical model definition. They are authoritative *only* for
  the objects they alone create, and even then only as inputs the runtime model has since absorbed.
- The **database schema** (audit evidence) is **operational state**, not architecture. It records
  what was deployed through a drifted, interleaved, partly out-of-band process — a state no linear
  artifact ever produced.

**A single canonical persistence model does exist conceptually — the runtime model — but it is not
currently reified in any deployable, self-consistent artifact set.** The snapshot contradicts it,
the chain cannot rebuild it, and the database does not match it. Therefore:

## FINAL CERTIFICATION: **CANONICAL MODEL PARTIALLY ESTABLISHED**

The *intended* canonical source is unambiguous and healthy (the runtime model). The *derived
canonical artifacts* that must exist for greenfield deployment, replay, and continuous delivery
(a snapshot consistent with it, and a chain that can build it) are absent or invalid. The
architecture has a canonical **definition** but not a canonical **materialization**.

---

# Artifact Classification Matrix

| Artifact | Role | Authority | Status |
|---|---|---|---|
| **Runtime EF Model** (entity classes + `OnModelCreating` + `IEntityTypeConfiguration<>`) | Defines intended persistence model | **CANONICAL (sole source of truth)** | Complete, internally consistent, actively maintained. 263 entities, 4,324 properties, 866 indexes (Phase 2). |
| **Entity classes** (`PointOfSale.Core/Entities/**`) | Structural half of the runtime model | Canonical (component) | Current. Includes 7 Carrier tables + `PaidBy` that exist in no other artifact. |
| **Fluent configurations** (`Data/Configurations/**`, `ApplicationDbContext`) | Mapping/constraint half of the runtime model | Canonical (component) | Current; 242 DbSets. Declares indexes/FKs/CKs/filters the chain never migrated. |
| **`ApplicationDbContextModelSnapshot.cs`** | Baseline cache for the differ | **Derived / Generated** | **STALE & INVALID** — froze ~2026-05-10; 384 ops / 42 entities behind. Regenerable → disposable as authority. |
| **Migration classes** (149 `.cs`) | Change history / deployment script | **Partially Canonical (Historical)** | Split-personality: differ-backed through `20260510130500`, hand-authored after. Cannot rebuild runtime model (64 gap ops). |
| **Designer files** (49 `.Designer.cs`) | Per-migration frozen model snapshots | Derived / Generated | Present only through `20260510130500`; **50 later classes have none** → snapshot never advanced. Historical only. |
| **Companion / standalone SQL** (190 `.sql`) | Operational DDL + data ops + repairs | **Operational / Legacy** | Authoritative only for objects they alone create (e.g. Geographic subsystem). Mix of business, infra, repair, hotfix. |
| **Database schema** (`DB/schema-23-7-2026.sql`) | Deployed operational state (audit evidence) | **Operational (non-authoritative)** | A state no linear path produced (interleaved + out-of-band). Evidence, not architecture. |
| **`__EFMigrationsHistory`** (`DB/data-*.csv`) | Applied-migration ledger | Operational / Historical | 128 applied; 59–60 orphan rows (no source class); interleaved. Records deployment reality, not intent. |
| **Prior audit reports** (Phase 1/1.5/1.75/2) | Certification evidence | Supporting evidence | Accepted as established fact; not re-derived here. |

---

# Canonical Source Analysis

Each artifact answered against the question *"is it Canonical / Derived / Generated / Historical /
Operational / Legacy / Deprecated?"* with supporting evidence.

**Runtime EF Model — CANONICAL.**
It is the only artifact that is (a) complete relative to the source, (b) internally consistent
(EF materializes and validates it in-process with zero errors — Phase 2 built it via
`IDesignTimeModel`), and (c) the actual contract the running binaries bind to. Everything else in
EF Core's design is *defined as a function of this model*: `migrations add` diffs against it,
snapshot is emitted from it, the DB is meant to conform to it. Decisive evidence of primacy: the 7
Carrier administration tables, `AffiliatePayoutRequests.PaidBy`, the
`CustomerAddresses.CurrentVersionId` FK, and ~50 indexes exist **only** in this artifact (Phase 2,
"NEVER migrated" set) — the model leads; all derived artifacts trail it.

**Entity classes / Fluent configurations — CANONICAL (components).**
Together they *are* the runtime model. No divergence between them was found by Phase 2's in-process
build (the model materialized cleanly). Authoritative for structure and mapping respectively.

**Model snapshot — DERIVED / GENERATED, currently INVALID.**
By construction it is `// <auto-generated />` output of `migrations add`, a pure projection of the
model at emit time. It carries no independent authority. Evidence it is derived-and-stale: header
`ProductVersion 9.0.1` consistent tool output (not hand-edited), yet
`IMigrationsModelDiffer.HasDifferences(snapshot, runtime) = TRUE` with 384 ops and 42 missing
entities (Phase 2). A derived cache that disagrees with its source is invalid, not authoritative.

**Migration chain — PARTIALLY CANONICAL / HISTORICAL.**
Confirmed structurally this pass: **149 classes, 49 Designer files, last Designer =
`20260510130500`, 50 classes carry a later Id with no Designer.** Through 2026-05-10 the chain is
genuine EF history (Designer + snapshot advanced together). After it, `migrations add` was
abandoned in favour of hand-written `migrationBuilder.Sql(...)` / builder migrations. So the chain
is *canonical as history for its first era* and *a curated deployment script for its second*. It
is **not** a canonical model definition: replaying it cannot reproduce the runtime model (64 gap
ops, Phase 2) and cannot replay against the live DB (Phase 1.75).

**SQL files (190) — OPERATIONAL / LEGACY.**
These encode business evolution (CommerceHub buildout `ZZ*`), infrastructure (indexes,
sequences), data migration (`Backfill*`, `Migrate*`), and manual repair (`Fix_*`, `Nuclear_Reset`,
`ReconcileExistingEfMigrationHistory`). Authoritative only as the *sole creator* of certain live
objects (e.g. the Geographic subsystem via `20260705170009`), but even those objects are now fully
represented in the runtime model — so the SQL is an operational input, not a model authority.

**Database schema + history — OPERATIONAL, NON-AUTHORITATIVE.**
Phase 1.5 proved the DB occupies an interleaved, partly out-of-band state (34 pending Ids below
the S80 checkpoint; 59–60 orphan history rows; `MarketplaceProjectionOutboxMessages` created by
unapplied migration). A state no artifact intended cannot define intended architecture.

---

# Migration Chain Classification

**Verdict: PARTIALLY CANONICAL — canonical history through `20260510130500`; historical-reference /
deployment-script only thereafter.**

Why:

1. **Era 1 (InitialCreate `20251123190510` → `20260510130500`): Canonical History.** 49 Designer
   files, snapshot advanced in lockstep, differ-backed. This segment is a faithful, tool-generated
   record and is internally consistent with the snapshots of its own era.
2. **Era 2 (`20260516090000` → `20260722130000`, 50 classes): Historical Reference / Curated
   Script.** No Designer files; hand-authored builder + raw SQL. The snapshot was never advanced,
   so these migrations are *not* differ-backed and were never reconciled to the model. They are a
   deployment script, not canonical history.
3. **Chain is not self-contained.** 59–60 applied history rows have no source class (Phase 1.5),
   so the chain cannot even reproduce the *applied* history, let alone the runtime model.
4. **Chain is incomplete vs source.** 64 differ ops — 7 whole tables, 1 property, 1 FK, ~50
   indexes — are created by no migration and no SQL (Phase 2). A from-zero replay stops short of
   the runtime model by construction.

Therefore the chain may be trusted as a **record of how the schema evolved**, but must **not** be
treated as an authoritative definition of the target model.

---

# Snapshot Classification

**Verdict: DISPOSABLE as an authority / REGENERABLE from the runtime model / currently INVALID.**

- **Not canonical:** it is `// <auto-generated />` derived output; it holds no information the
  runtime model lacks.
- **Not recoverable-as-is:** it is 384 ops and 42 entities behind and would mislead any future
  `migrations add` into emitting a hybrid catch-up mixing applied and never-applied changes
  (Phase 2).
- **Regenerable:** because it is a pure projection of the runtime model, an authoritative snapshot
  is fully reconstructible *from the model* at any time. Its architectural value is zero beyond
  being a correct cache.
- **Historical value:** the frozen-at-2026-05-10 content is a useful *marker* of when the
  differ-backed workflow stopped — but that is forensic, not architectural.

The correct architectural stance: the snapshot is a **cache that must equal `diff(∅ → runtime
model)`**; it currently does not, so it is invalid and should be regarded as disposable/regenerable
rather than authoritative.

---

# Runtime Model Classification

**Verdict: CANONICAL — the sole source of truth.**

- **Complete:** contains every entity/property/index/constraint the application uses, including
  objects present in no other artifact (Carrier admin tables, `PaidBy`, `CurrentVersionId` FK,
  ~50 indexes).
- **Internally consistent:** EF materialized and validated it in-process with no errors (Phase 2).
- **Authoritative by EF design:** snapshot, chain, and DB are all *defined as derivations of it*.
- **Actively maintained:** the divergence direction is always "source ahead of snapshot/chain/DB,"
  which is the signature of a model that is the living authority while its derivations lag.

This is the artifact that SHOULD define the persistence model, and in practice already does define
what the running system expects.

---

# SQL Migration Classification

Every major raw-SQL group classified by intent (evidence = filenames + prior-audit content
references):

| SQL group (representative) | Classification | Evidence / reasoning |
|---|---|---|
| CommerceHub buildout `Sql/20260322–20260331_ZZ*/AA*` (payments, ledger, refunds, payouts, categories, CMS, search) | **Business evolution** | Feature construction of the CommerceHub platform area; creates domain tables/columns absent from earlier EF chain. |
| `InitialGeographicRuntimeFoundation` (`20260705170009`) + `Geo8/Geo9/ShipOrigin1` | **Business + Model evolution** | Raw-SQL creation (63 `IF NOT EXISTS` guards) of the entire Geographic subsystem now in the model; the *only* creator of those live objects (Phase 2). |
| Customs / Carrier-geographic (`Customs2/4`, `Geo9CarrierGeographicResolution`) | **Business evolution** | New domain subsystems added via SQL. |
| `AddDashboardPerformanceIndexes*`, `AddTenantScopedOrderNumberIndex`, `Add*Sequence`, `*WalletQueryIndexes` | **Performance optimization / Infrastructure** | Indexes + sequences for query/throughput; no model semantics. |
| `*RequestIdempotency`, `*Outbox*`, `*MutationVersion`, `*Lease`, webhook-replay (`Stripe/Paymob/Fawry ...ContractClosure2`) | **Infrastructure (reliability)** | Idempotency/outbox/concurrency plumbing for exactly-once + event ordering. |
| `Backfill*`, `Migrate*` (`BackfillDefaultProductVariants`, `MigrateLegacyProductImagesToProductMedia`, `PhaseCOD_MigratePaymentStatus`) | **Data migration (model evolution support)** | One-time data reshaping accompanying schema change; embeds business data mutations. |
| `Fix_*`, `Fix_Tenant_Mena_Nuclear_Reset`, `Fix_AccountType_Zero`, `Fix_AccountingOutboxMessages_MissingColumns`, `PhaseTOJ_BlockLegacyTreasuryWrites` | **Emergency hotfix / Manual repair** | Names denote incident-driven corrective SQL applied out of band. |
| `20260408_ReconcileExistingEfMigrationHistory`, `20260409214000_..._PgAdmin(_Idempotent)`, `_SqlServer` variants | **Manual repair / Legacy compatibility** | History reconciliation + pgAdmin/SqlServer variants = operational bridging, a root cause of the interleaved history (Phase 1.5). |
| `Seed_LedgerAccounts_ChartOfAccounts`, `AF14_Seed_AffiliateAccounts`, `AF15_PromotionLinks_Migration` | **Business evolution (seed/reference data)** | Reference/seed data, not schema authority. |

Net: the SQL corpus is a legitimate operational history spanning business evolution, infrastructure,
performance, data migration, and emergency repair — but as an *authority* it is fragmentary and
partly incident-driven (the `Fix_*`/`Reconcile*`/`_PgAdmin` groups are precisely what produced the
non-linear DB state). It defines no canonical model.

---

# Architectural Findings

1. **The canonical model is defined but not materialized.** The runtime model is the single source
   of truth, healthy and complete; but no deployable derived artifact set (snapshot + chain)
   currently equals it. Authority and materialization are decoupled.
2. **A workflow regime change on 2026-05-10 is the pivot.** Before: differ-backed EF migrations
   (49 Designer files, snapshot in lockstep). After: 50 hand-authored migrations + heavy raw SQL,
   no snapshot advance. Every downstream inconsistency (stale snapshot, incomplete chain, drifted
   DB) traces to this regime change and the absence of a differ in the loop.
3. **Divergence is strictly directional.** Source ⟶ (ahead of) ⟶ chain ⟶ (ahead of) ⟶ snapshot;
   and source vs DB is orthogonally drifted via out-of-band SQL. The consistent "source ahead"
   direction confirms the model is the leading authority and everything else trails it.
4. **The chain is dual-purpose and this is the core defect.** It is simultaneously asked to be
   (a) canonical history and (b) a deployment script, and in Era 2 it abandoned (a) for (b). An
   EF chain that is not differ-backed loses its ability to reconstruct the model — which is exactly
   what Phase 2's 64-op gap measures.
5. **The database is a non-authoritative operational artifact.** It reflects an interleaved,
   partly manually-repaired deployment history and matches neither the chain (Phase 1.75) nor the
   model (Phase 1). It must never be used as a model authority.
6. **No corruption, only drift.** No hand-edited snapshot, no deleted generated migrations, no
   merge damage (Phase 2 root-cause). The artifacts are individually well-formed; their
   *relationships* are broken.

---

# Canonical Reconstruction Model

*(Architectural state required — NOT repair steps, NOT commands, per scope.)*

To possess a fully canonical persistence model, the artifact set would need to reach the following
**architectural state**:

1. **Single authority affirmed:** the runtime EF model (entity classes + configurations) is
   accepted as the sole definitional source. (Already true — no change of authority required.)
2. **Snapshot ≡ model:** a model snapshot that satisfies
   `HasDifferences(snapshot, runtime) = FALSE` — i.e. a snapshot regenerated from the current
   runtime model, so the differ baseline is valid again.
3. **Chain-completeness relative to the model:** a migration sequence whose cumulative `Up()`
   effect, applied to an empty database, yields exactly the runtime model — closing the 64-op gap
   (7 Carrier tables, `PaidBy`, `CustomerAddresses.CurrentVersionId` FK, ~50 indexes) that
   currently exists nowhere.
4. **Self-containment:** every migration Id required to build the model exists as a source class
   (no orphan-history dependence), so the chain can reconstruct the target from zero without the
   live DB.
5. **A defined baseline boundary:** a single, unambiguous starting point (either the existing
   chain made whole, or a consolidated baseline that captures the current model as the new
   `InitialCreate`) from which all future differ-backed migrations extend.
6. **Reunified workflow invariant:** the architectural guarantee that every future model change
   flows through the differ (snapshot advances with each migration), eliminating the Era-1/Era-2
   split that caused the divergence.

Reaching states 2–5 is what would convert the certification from **PARTIALLY** to **ESTABLISHED**.
State 1 and 6 are governance/authority conditions; state 1 already holds.

---

# Recovery Readiness

| Capability | Ready? | Supporting evidence |
|---|---|---|
| **Greenfield deployment** | **NO** | A from-empty replay of the chain cannot produce the runtime model — 64 ops incl. 7 whole tables exist in no migration/SQL (Phase 2). The greenfield result would be missing tables/columns/indexes the running app requires. |
| **Fresh database creation** | **NO** | Same root cause: `dotnet ef database update` on an empty DB builds only what the chain contains, which is a strict subset of the model. Would boot into runtime `relation/column does not exist` errors. |
| **Long-term maintenance** | **NO** | The snapshot baseline is invalid (384 pending ops); the next `migrations add` emits a hybrid catch-up mixing applied and never-applied changes (Phase 2), unusable without hand-surgery. |
| **Future migrations** | **NO** | Requires a valid snapshot baseline; none exists. Any generated migration is untrustworthy until the snapshot equals the model. |
| **Continuous delivery** | **NO** | Automated `database update` against the live DB aborts on the first pending migration's first statement (Phase 1.75, 100% confidence). Pipeline deploys are a hard-fail, not a partial upgrade. |

**Readiness summary:** the project is **not currently ready** for any of the five capabilities. The
blocker is uniform and singular in nature — the derived artifacts (snapshot, chain) do not equal
the canonical runtime model — even though the canonical model itself is healthy. This is a
*materialization* deficit, not a *definition* deficit, which is why it is recoverable in principle
(the authority to rebuild from is intact and complete).

---

# Final Certification

The Shopixy persistence model **has a clear, healthy, single source of truth — the runtime EF
model** (entity classes + `OnModelCreating` + `IEntityTypeConfiguration<>` classes). The snapshot
is a stale, regenerable, disposable derivation; the migration chain is canonical history only
through 2026-05-10 and a deployment script thereafter, incapable of reconstructing the model; the
SQL corpus is operational (business/infra/repair); and the database is non-authoritative
operational state. No deployable, self-consistent derived artifact set currently equals the
canonical model, so the model is authoritatively *defined* but not authoritatively *materialized*.

## CANONICAL MODEL PARTIALLY ESTABLISHED

- **Established:** the canonical source of truth (runtime model) is unambiguous, complete, and
  internally consistent. Authority is not in doubt.
- **Not established:** the derived canonical artifacts required for deployment, replay, and CD — a
  snapshot consistent with the model and a chain that can rebuild it — are absent or invalid.

Per scope, **no repairs are recommended and nothing was modified.** This report certifies
architectural truth only.

---

## Appendix — Structural Facts Verified This Pass (read-only)

- Migration classes: **149** (`.cs` excl. Designer/Snapshot). Designer files: **49**. Last Designer
  Id: **`20260510130500`**. Classes with an Id after the last Designer: **50** (no Designer →
  snapshot never advanced). SQL files: **190** (97 root + 93 `Sql/`). Snapshot header:
  `ProductVersion 9.0.1`, 18,859 lines.
- Established facts imported from prior certified audits (not re-derived): 384 differ ops / 42
  entities absent from snapshot / 64 ops migrated nowhere (Phase 2); replay FAIL at first statement
  (Phase 1.75); DB ≠ source with interleaved history + 59–60 orphan rows (Phase 1/1.5).
- No database connection was opened; no SQL, migration, snapshot, history, or source file was
  executed or modified.
