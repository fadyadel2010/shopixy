# Shopixy Migration Replay Feasibility Audit — Phase 1.75

## Read-Only Replay Certification

- **Scope:** Determine whether the current EF Core migration chain can be safely replayed against the audited database (`DB/schema-23-7-2026.sql`, checkpoint `20260511150000_S80`).
- **Method:** Read-only mental replay. No SQL executed, no migration run, no restore, no code/snapshot/history modified. Only this report was written.
- **Authoritative inputs consulted:**
  - `DB/data-1784830579292.csv` — `__EFMigrationsHistory` export (128 applied migrations)
  - `DB/schema-23-7-2026.sql` — plain-text schema-only `pg_dump` (PostgreSQL 18.4): 214 tables, 775 indexes, 447 ADD CONSTRAINT statements, 15 sequences, 34 functions
  - `PointOfSale.Infrastructure/Migrations/` — 149 migration classes (`.cs`) + companion `.sql` scripts + `ApplicationDbContextModelSnapshot.cs`
  - `docs/audits/SCHEMA_DRIFT_AUDIT_REPORT.md` (Phase 1) and `SCHEMA_DRIFT_AUDIT_REPORT_V2.md` (Phase 1.5)
- **Cross-references** derive from mechanical set arithmetic (applied CSV vs source classes) and object-name grep of the schema dump, reproduced in this pass.

---

# Executive Summary

## Replay Certification: **FAIL**

A plain `dotnet ef database update` against the audited database is expected to **fail on the very first pending migration, on its first statement**, and cannot proceed past it. Automatic, unattended replay is not feasible.

### The three load-bearing facts

1. **80 migrations are pending** (149 source classes − 69 applied-and-still-in-source). This matches Phase 1.5 exactly.
2. **EF executes pending migrations in MigrationId (timestamp) order**, regardless of when they were authored. Because the applied history is *interleaved* (last applied = `20260511150000_S80`, yet 34 pending migrations carry earlier IDs), EF will begin replay at `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` — the lowest pending ID — **not** at the newest work.
3. **That first migration's first operation is a non-idempotent `DropCheckConstraint` against an object that does not exist in the database.** It fails immediately with `constraint "CK_SmartOffers_CouponCode_Presence" of relation "SmartOffers" does not exist`. EF wraps each migration in a transaction; the migration rolls back, `__EFMigrationsHistory` is not advanced, and the entire `database update` aborts. Nothing after it runs.

### Why this is a structural, not incidental, failure

The drift is the product of **cherry-picked / out-of-band application** (Phase 1.5, certified): 34 pending migrations sit *below* the checkpoint, 59–60 applied history rows have no source class, and at least two post-checkpoint tables (`MarketplaceProjectionOutboxMessages`, `ProductRequestIdempotency`) were created by manual SQL. Consequently the database is in a state **no linear migration path ever produced**, so replaying the linear chain hits both:

- **"Object missing" failures** — migrations that DROP/ALTER/RENAME objects a normal predecessor would have created, but which were never created here (first failure class).
- **"Object already exists" failures** — migrations that CREATE objects already present from manual SQL or from later-applied checkpoints (second failure class; e.g. `20260516090000` re-creating `MarketplaceProjectionOutboxMessages`, and the strongly-typed `CreateTable`/`AddColumn` builder operations in ≥40 pending migrations that cannot emit `IF NOT EXISTS`).

Both classes are present and abundant. This is not a single fixable collision — it is a chain-wide incompatibility.

---

# Replay Timeline

## Pending set (source classes absent from `__EFMigrationsHistory`)

| Metric | Value | Evidence |
|--------|-------|----------|
| Applied migrations | 128 | CSV rows (excl. header) |
| Source migration classes | 149 | `Migrations/*.cs` (excl. `.Designer.cs`, `ModelSnapshot`) |
| Applied ∩ source | 69 | set intersection |
| **Pending** | **80** | source − applied |
| Orphan history (applied, no source class) | 59–60 | applied − source |
| Last applied by ID | `20260511150000_S80_AddTenantsCurrencyDefaultValue` | max applied MigrationId |
| Pending **interleaved** (ID ≤ last applied) | **34** | IDs from `20260416003507` … `20260511143000_S79` |
| Pending **after** boundary (ID > last applied) | **46** | `20260516090000` … `20260722130000` |

## Execution order (EF sorts by MigrationId ascending)

EF will attempt the 80 pending migrations in strict timestamp order beginning at:

```
 1  20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints   [INTERLEAVED]  <-- FIRST TO RUN, FIRST TO FAIL
 2  20260417155219_AddPaymentWebhookLogUniqueConstraint           [INTERLEAVED]
 3  20260419113000_AddOrderRequestIdempotencySnapshotHash         [INTERLEAVED]
 ...
34  20260511143000_S79_CommerceHubCustomerSessionCutoff           [INTERLEAVED / last below checkpoint]
35  20260516090000_StorefrontPromotionScopeReliabilityHardening   [after boundary]
 ...
80  20260722130000_HardenProviderDeliveryOutbox                   [after boundary / newest]
```

**Key consequence:** the 46 "newest" migrations that motivated the drift are *last* in EF's queue. Because migration #1 aborts the run, none of the 80 ever commit. The interleaving guarantees the failure surfaces at the *oldest* pending ID, not at the drift frontier.

---

# Replay Simulation

Every pending migration was inspected: `Up()` MigrationBuilder operations, raw `migrationBuilder.Sql()` blocks, and companion SQL. Operations were cross-referenced against the dump's object inventory. Representative results (full per-op collision matrix reproduced from this pass):

| # | Migration | Key operations | Expected result | Evidence |
|---|-----------|----------------|-----------------|----------|
| 1 | `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` | `DropCheckConstraint CK_SmartOffers_CouponCode_Presence`, `DropCheckConstraint CK_SmartOffers_CouponDiscountValue_Presence`, then 5 raw `Sql()` re-adding NOT VALID constraints | **FAIL** at statement 1 | Both check constraints **absent** from dump (0 hits in 447 ADD CONSTRAINT). `DropCheckConstraint` emits bare `ALTER TABLE ... DROP CONSTRAINT` (no `IF EXISTS`). |
| 2 | `20260417155219_AddPaymentWebhookLogUniqueConstraint` | dedup `Sql()`, then `CreateIndex IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId (unique)` | **FAIL** (already exists) | Index **present** in dump → `relation already exists`. |
| 3 | `20260419113000_AddOrderRequestIdempotencySnapshotHash` | `AddColumn OrderRequestIdempotency.SnapshotHash` | **FAIL** (already exists) | Column **present** in dump → `column already exists`. |
| 4 | `20260419190000_AddCheckoutSessionManualWalletRecovery` | `AddColumn` ×3 on `CheckoutSessions` (ProofImageUrl, WalletPhoneNumber, WalletType) | **FAIL** (already exist) | All three columns present. |
| 6/7 | `...AddStockSyncOutboxReliability` / `S12A_AuthorizationWebhookTrust` | `CreateTable StockSyncOutboxMessages`, `AddColumn ProductVariants.StockUpdatedAtUtc`, multiple `CreateIndex` | **FAIL** (already exist) | Table + columns + indexes present. |
| 16 | `20260423173000_S21_DiscountAuthorityLifecycleFix` | `RenameColumn CouponReservations.ConsumedAtUtc → ConfirmedAtUtc` | **FAIL** (source col gone) | Dump already has `ConfirmedAtUtc`, no `ConsumedAtUtc` → `column "ConsumedAtUtc" does not exist`. |
| 17 | `20260424090000_S22_OrderRefundLifecycleHardening` | `CreateTable OrderRefunds`, `OrderRefundLines` + 7 indexes | **FAIL** (already exist) | Both tables present. |
| 26 | `20260501200756_S66_...UnfilteredUniqueness` | 79 raw `Sql()`, mixed 27× `IF NOT EXISTS`, 52× `IF EXISTS`, plus 22 strongly-typed `CreateIndex`/`CreateTable` (no guard) | **PARTIAL/FAIL** | Guarded raw blocks survive; the 22 builder ops collide with existing objects. |
| 31 | `20260508215150_S75_ProductionAuthorityAuditPendingModelSync` | `AddCheckConstraint` ×N, `CreateTable MarketplaceEventOutboxDeadLetterArchives`, `AddColumn CommerceHubPayments.ProvisionalRefundedAmount` … | **FAIL** (already exist) | Cherry-pick target; its objects exist from later-applied checkpoints/manual SQL. |
| 32 | `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped` | ~200 `CreateTable` + hundreds of `CreateIndex` (full-model re-emit) | **FAIL** (mass collision) | Virtually every table/index already exists — this migration re-creates the whole schema. |
| 35 | `20260516090000_StorefrontPromotionScopeReliabilityHardening` | 11 raw `Sql()`, 14× `IF NOT EXISTS`, 0 builder DDL | **PARTIAL / likely PASS** | Idempotent-authored; creates `MarketplaceProjectionOutboxMessages`/`ProductRequestIdempotency` guarded. Would survive *if reached* — it is not. |
| 56 | `20260610140000_AffiliatePayoutVerifiedEvidenceAuthority` | `AddColumn` ×3 on `AffiliatePayoutTransferEvidence` | **FAIL** (table missing) | `AffiliatePayoutTransferEvidence` is one of the 22 missing tables → `relation does not exist`. |
| 79/80 | `...AddProviderDeliveryOutbox` / `HardenProviderDeliveryOutbox` | `CreateTable ProviderDeliveryOutbox` (no guard); then `AddColumn` ×5 | Would **PASS then FAIL-chain** | `ProviderDeliveryOutbox` is missing (create would succeed); but unreachable. #80's AddColumns depend on #79. |

**Aggregate:** of 80 pending migrations, ≥40 use strongly-typed builder DDL (`CreateTable`/`AddColumn`/`CreateIndex`/`DropCheckConstraint`/`RenameColumn`) that **cannot** emit `IF [NOT] EXISTS` and therefore collide deterministically with the existing/absent state. The remaining raw-SQL migrations are a mix of idempotent (`IF NOT EXISTS`) and non-idempotent blocks.

---

# First Failure Analysis

| Field | Value |
|-------|-------|
| **First migration to run** | `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` (lowest pending MigrationId) |
| **First statement to fail** | `migrationBuilder.DropCheckConstraint(name: "CK_SmartOffers_CouponCode_Presence", table: "SmartOffers")` → `ALTER TABLE "SmartOffers" DROP CONSTRAINT "CK_SmartOffers_CouponCode_Presence";` |
| **Reason** | The check constraint does not exist in the audited database (0 occurrences in the 447 ADD CONSTRAINT statements of `schema-23-7-2026.sql`). EF's `DropCheckConstraint` does not emit `IF EXISTS`. |
| **PostgreSQL error** | `ERROR: constraint "CK_SmartOffers_CouponCode_Presence" of relation "SmartOffers" does not exist` |
| **Database object** | `public."SmartOffers"` (table exists) / constraint `CK_SmartOffers_CouponCode_Presence` (absent) |
| **Transaction behavior** | EF runs each migration in its own transaction; the failed statement rolls the migration back, `__EFMigrationsHistory` is **not** updated, and `database update` exits non-zero. No later migration executes. |
| **Confidence** | **100%** — constraint absence directly grepped; `DropCheckConstraint` non-idempotency is EF-invariant. |

## All confirmed failure sites (not just the first)

Because the run halts at #1, the following are the failures a hypothetical "skip-and-continue" replay would hit next — they define the full blast radius:

- **Object already exists** (would fail if reached): #2 (unique index), #3 & #4 (columns), #6/#7 (StockSyncOutbox table/cols/indexes), #17 (OrderRefunds tables), #31 (S75 tables/cols), #32 (full-model re-create), and every builder-DDL migration whose target is already present.
- **Object missing** (would fail if reached): #16 (`RenameColumn` on already-renamed `CouponReservations`), #56 (`AddColumn` on missing `AffiliatePayoutTransferEvidence`), and all pending migrations that ALTER/DROP the 22 missing tables / 11 missing-column groups.
- **Already removed / already renamed** (would silently misbehave or error): `DropIndex` on `IX_ShippingWebhookLogs_*` (already gone), `DropForeignKey FK_TreasuryAccounts_LedgerAccounts_AccountId` (already gone), `RenameColumn CouponReservations.ConsumedAtUtc` (already renamed).

---

# Conflict Matrix

| Conflict type | Representative evidence | Severity | Count / scope |
|---------------|------------------------|----------|---------------|
| **Constraint missing on DROP** | `CK_SmartOffers_CouponCode_Presence` / `_CouponDiscountValue_Presence` absent (#1) | **Critical** (root blocker) | ≥2 at first migration; more downstream |
| **Index already exists** | `IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId` present (#2); hundreds in #32 | High | Pervasive |
| **Column already exists** | `OrderRequestIdempotency.SnapshotHash` (#3); `CheckoutSessions.Proof*` (#4); `ProductVariants.StockUpdatedAtUtc` (#6/#7) | High | Many |
| **Table already exists** | `StockSyncOutboxMessages` (#6/#7), `OrderRefunds`/`OrderRefundLines` (#17), `MarketplaceProjectionOutboxMessages` (#35 target, manual SQL), full set in #32 | High | Many |
| **Table missing (ALTER/AddColumn target)** | `AffiliatePayoutTransferEvidence` (#56) — one of 22 missing tables | High | 22 missing tables + dependents |
| **Rename conflict (already renamed)** | `CouponReservations.ConsumedAtUtc → ConfirmedAtUtc` (#16) | High | ≥1 confirmed |
| **Index/FK already removed on DROP** | `IX_ShippingWebhookLogs_*` (#11), `FK_TreasuryAccounts_LedgerAccounts_AccountId` (#61) | Medium | Several |
| **History conflict (orphan rows)** | 59–60 applied IDs with no source class | Medium (blocks from-scratch reconstruction, not `update`) | 59–60 |
| **Out-of-order dependency (interleaved)** | 34 pending IDs below the checkpoint; EF runs them first | Critical (causes first-failure location) | 34 |
| **Manual SQL conflict** | `MarketplaceProjectionOutboxMessages`, `ProductRequestIdempotency` created out-of-band; `20260516090000` (their only creating migration) unapplied | High | ≥2 tables + legacy set |
| **Raw SQL ambiguity** | Non-idempotent `Sql()` blocks (dedup DELETE in #2; NOT VALID constraint re-add in #1) assume prior state | Medium | Multiple |
| **Full-model re-emit migration** | `#32` emits ~200 `CreateTable` + all indexes (a regenerated snapshot-catch-up migration) | Critical if reached | 1 |

---

# Dependency Graph

```
ROOT BLOCKER
  R1: Interleaved history (34 pending IDs < checkpoint 20260511150000_S80)
        │  forces EF to start replay at the OLDEST pending ID, not the drift frontier
        ▼
  R2: 20260416003507 statement #1  DropCheckConstraint CK_SmartOffers_CouponCode_Presence
        │  constraint absent  →  transaction rollback  →  __EFMigrationsHistory not advanced
        ▼
  ┌─────────────────────────────────────────────────────────────┐
  │  HARD STOP — `dotnet ef database update` aborts here.        │
  │  All 79 subsequent pending migrations are BLOCKED (unreached)│
  └─────────────────────────────────────────────────────────────┘

INDEPENDENT ROOT CAUSES (would each independently break replay even if R2 were bypassed)
  R3: Manual/out-of-band SQL created objects (MarketplaceProjectionOutboxMessages,
        ProductRequestIdempotency, legacy tables) → "already exists" on CREATE migrations
  R4: 22 missing tables + 11 missing-column groups → "does not exist" on ALTER/AddColumn
  R5: Cherry-picked application (S75 unapplied while S78/S80 applied) → checkpoint objects
        exist without their migration having run → collisions in S75, S66, #32
  R6: 59–60 orphan history rows → chain not self-contained; from-scratch replay impossible
```

- **Root blockers (not symptoms):** R1 (interleaving) + R2 (first non-idempotent drop). R3–R6 are structural root causes of the *drift* that guarantee failure at multiple additional sites.
- **Blocked migrations:** 79 of 80 (all except the one that fails).
- **Cascade:** single-point cascade — one transaction failure blocks the entire queue. There is no partial-progress path under `database update`.

---

# Recovery Strategy Evaluation

*(Evaluation only — no recommendation, per scope.)*

### 1. Idempotent replay (`dotnet ef migrations script --idempotent`)
- **Risk:** High. **Complexity:** Medium.
- **Advantages:** EF's idempotent script guards each migration with a history-table existence check (`IF NOT EXISTS (SELECT ... FROM __EFMigrationsHistory WHERE MigrationId = ...)`), so it skips nothing structurally but wraps each migration body. Handles the *history* dimension.
- **Disadvantages:** The guard is **per-migration, keyed on history rows** — it does **not** make the individual DDL statements object-safe. Because these migrations were never applied (no history rows), the idempotent script will still *run* them, and the same `DropCheckConstraint`/`CreateTable`/`AddColumn` collisions occur. Idempotency at the migration level ≠ idempotency at the object level. Does not solve R2–R5.

### 2. Replay after manual history alignment
- **Risk:** High. **Complexity:** High.
- **Advantages:** Inserting history rows for migrations whose objects already exist (e.g. #2, #3, #4, #6/#7, #17, #31, #32) would make EF *skip* them, sidestepping "already exists" collisions.
- **Disadvantages:** Requires per-migration human adjudication of "is this migration's effect already present?" across 34+ interleaved migrations, several of which are *partially* applied (some objects yes, some no — e.g. #32 mixes present and absent). Mis-marking loses a needed change silently. Does not fix R4 (genuinely missing tables/columns still need their migrations to run) or R2 (missing-constraint drops). Fragile and audit-heavy.

### 3. Replay after object reconciliation
- **Risk:** High. **Complexity:** Very High.
- **Advantages:** Manually create the 22 missing tables / 11 missing-column groups and drop/rename to match each migration's expected precondition, so the chain finds the state it assumes.
- **Disadvantages:** Effectively hand-rebuilding the intermediate schema states the linear chain expects — for a database that never passed through them. Enormous surface, high chance of subtle divergence (defaults, FKs, check constraints, data backfills embedded in raw `Sql()`). Data-loss risk where reconciliation drops/renames populated columns.

### 4. Fresh database + data migration
- **Risk:** Medium (process) / High (data). **Complexity:** High.
- **Advantages:** `dotnet ef database update` against an **empty** database replays all 149→current cleanly (the chain is internally consistent from `InitialCreate` forward for the classes that exist). Produces a schema that exactly matches the model snapshot. Eliminates R1–R5 entirely.
- **Disadvantages:** R6 bites — 59–60 applied migrations have **no source class**, so a from-empty replay of *source* cannot reproduce every object the live DB has (e.g. legacy `JournalEntries`, `AccountBalances`, `ChatAuditLog`, `ReceiptSettings`, `StockBatches`). Requires a bespoke, tenant-aware **data ETL** from old→new schema (216 tables, 225 FKs, multi-tenant isolation invariants). Highest data-integrity engineering burden; longest cutover.

### 5. Hybrid recovery
- **Risk:** Medium. **Complexity:** High.
- **Advantages:** Combine (2)+(3) selectively: mark clearly-already-applied migrations in history, hand-reconcile the genuinely-missing objects, then run the tail (post-checkpoint, mostly `IF NOT EXISTS`-authored migrations like #35+) which are largely idempotent. Preserves live data (no full rebuild).
- **Disadvantages:** Requires the same per-migration adjudication as (2)+(3) but across the whole interleaved band; correctness depends on exhaustive object-by-object diffing (the "80% confidence" full type/precision/default diff flagged as outstanding in Phase 1.5 is a prerequisite). Bespoke, not repeatable, and only as trustworthy as the reconciliation review.

---

# Engineering Risk

| Risk dimension | Assessment | Evidence |
|----------------|------------|----------|
| **Runtime risk** | **Critical** — the running binaries expect 22 tables + 11 column groups absent from the DB; queries throw `relation/column does not exist` at runtime independent of migration state. | Phase 1.5 HR-1/HR-2 (Critical), reconfirmed |
| **Deployment risk** | **Critical** — automated `database update` in a deploy pipeline aborts on the first migration and leaves the DB unchanged (transactional rollback), i.e. a hard deploy failure, not a partial upgrade. | First-failure analysis |
| **Data integrity risk** | **High** — several pending migrations embed data mutations in raw `Sql()` (dedup DELETE in #2, NOT VALID constraint re-add + intended backfill in #1). Any reconciliation/history-alignment strategy that skips or re-runs these risks silent data divergence or loss; tenant-scope nullability drift (`AffiliateAccounts`/`CmsPages.TenantId` nullable in DB) is a cross-tenant isolation hazard. | #1/#2 raw SQL; Phase 1.5 HR-5 |
| **Recovery risk** | **High** — the chain is **not self-contained** (59–60 orphan history rows), so neither from-source nor from-history replay alone reconstructs the live schema. Every viable strategy requires bespoke, manually-adjudicated, non-repeatable work with a full column-level diff as a prerequisite. | Phase 1.5 Migration History Verification; strategy evaluation |

---

# Final Certification

## Replay feasibility: **FAIL**

The current migration chain **cannot** be safely replayed against the audited database by any automatic means. Certified findings:

1. EF replays the 80 pending migrations in MigrationId order and **begins at `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints`** — an interleaved migration below the S80 checkpoint — not at the drift frontier.
2. That migration's **first statement** (`DropCheckConstraint CK_SmartOffers_CouponCode_Presence`) fails immediately because the constraint is absent from the database and `DropCheckConstraint` is not idempotent. The migration transaction rolls back and the entire `database update` aborts. **Confidence: 100%.**
3. Even if that first failure were bypassed, replay would fail again at a large number of downstream sites split across two irreconcilable classes — **"object already exists"** (manual-SQL / cherry-picked checkpoint objects; ≥40 builder-DDL migrations that cannot emit `IF NOT EXISTS`) and **"object missing"** (22 missing tables, 11 missing-column groups, an already-performed rename). There is no linear order in which the chain succeeds.
4. The database occupies a state **no linear migration path ever produced** (interleaved cherry-picking + out-of-band SQL + 59–60 orphan history rows). The chain is therefore **not recoverable without surprises** under `dotnet ef database update`.

Five recovery strategies were evaluated (idempotent replay, history alignment, object reconciliation, fresh DB + data migration, hybrid). **No recommendation is issued in this phase**; each carries the risk/complexity profile documented above, and all require bespoke, manually-adjudicated work with a full column-level diff as a prerequisite.

---

*Phase 1.75 replay certification performed read-only on the 2026-07-23 evidence set. No database, source, migration class, snapshot, or history file was modified. Object presence/absence was verified by direct grep of `DB/schema-23-7-2026.sql`; pending-set arithmetic against `DB/data-1784830579292.csv` and `PointOfSale.Infrastructure/Migrations/*.cs`.*
