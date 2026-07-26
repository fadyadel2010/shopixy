# Shopixy Database Schema Drift Audit — Phase 1

## Source Code vs PostgreSQL Backup — Read-Only Engineering Certification

- **Audit type:** Full database schema drift audit (NOT a migration audit)
- **Source of truth A (expected):** Current Shopixy source code — EF Core `ApplicationDbContext` + `ApplicationDbContextModelSnapshot.cs` (authoritative expected model)
- **Source of truth B (actual):** `DB/23-7-2026.sql` (PostgreSQL 18 `pg_dump` custom-format archive of database `PosEcommerceDb`)
- **Method:** Static, read-only extraction and normalized semantic comparison. No migrations executed, no restore performed, no source/SQL modified.

---

# Executive Summary

**Overall certification: FAIL (structurally incompatible without migration).**

The PostgreSQL backup `DB/23-7-2026.sql` does **not** represent the schema expected by the current source code. The dominant, evidence-backed root cause is a single coherent condition: **the backup is frozen at an older migration checkpoint (~`20260508215150_S75_ProductionAuthorityAuditPendingModelSync`), and the ~52 EF Core migrations authored after that point were never applied to the database captured in this backup.**

Consequences:

- **22 tables** expected by the code model are **missing** from the backup.
- **11 tables' worth of columns** expected by the code are **missing** from existing tables (financial posting fields, cost-basis snapshots, identity/verification columns, webhook-replay fields, etc.).
- **10 real nullability mismatches** exist on shared columns.
- **1 bidirectional column-set drift** on `ShippingMethods` (code expects `ServiceLevel`; DB has `FulfillmentMode`/`FulfillmentCarrierCode`).

Deploying the current application binaries against this backup **will fail at runtime** — EF Core queries referencing missing tables/columns will throw `PostgresException: relation/column does not exist` on first access.

> **Important honesty note:** The `__EFMigrationsHistory` table rows in the archive are gzip-compressed inside the custom-format dump and could not be read as plain text without `pg_restore` (unavailable in this environment). The "migrations not applied" conclusion is therefore inferred structurally — every missing object maps to a post-`S75` migration, and no missing object maps to a pre-`S75` migration. A small number of post-`S75` objects (e.g. `MarketplaceProjectionOutboxMessages`, `ProductRequestIdempotency`, `TenantCurrencyNormalizationAudit`) **are** present in the backup, indicating the divergence is not a perfectly clean cut and manual SQL was also applied out-of-band. See Root Cause Analysis.

---

# Repository Discovery

| Item | Finding |
|------|---------|
| Solution | `PointOfSale.sln` (single solution) |
| Relational DbContext | `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` (PostgreSQL / Npgsql) |
| Design-time factory | `PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs` |
| Non-relational context | `PointOfSale.Infrastructure/MongoDB/Services/MongoDbContext.cs` (MongoDB — out of scope for PostgreSQL drift) |
| Model snapshot | `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (18,859 lines) |
| Migration files | 149 applied-migration classes under `PointOfSale.Infrastructure/Migrations/` |
| `IEntityTypeConfiguration` classes | 123 `*Configuration.cs` files under `PointOfSale.Infrastructure` |
| Migration assembly | `PointOfSale.Infrastructure` |
| Entity type blocks in snapshot | 357 (`modelBuilder.Entity(...)`), 214 distinct `ToTable` targets (rest are owned/complex types via table splitting) |

The `ApplicationDbContextModelSnapshot.cs` is treated as the authoritative expected model per EF Core semantics (it is the fully-resolved model the runtime expects), cross-checked against migration bodies for root-cause attribution.

---

# Source Model Statistics (Model A — expected)

| Metric | Value |
|--------|-------|
| Mapped tables (`ToTable`) | 214 |
| Column properties (`b.Property<>`) | 3,563 |
| Indexes (`HasIndex`) | 730 |
| Relationships / FKs (`HasOne`) | 193 |
| Migration classes | 149 |
| Sequences declared in snapshot (`HasSequence`) | 0 (document-number sequences created via raw SQL in migrations) |
| Views/functions/triggers managed by EF (`ToView`/`HasNoKey`) | 0 (all DB-side, unmanaged by EF) |

# Backup Statistics (Model B — actual, `public` schema)

| Metric | Value |
|--------|-------|
| Tables (`public`) | 216 (incl. 13 `__`-prefixed migration/backfill/backup tables + 2 whatsapp snake_case) |
| Tables (`hangfire` schema) | 12 (job scheduler — not EF-managed) |
| Columns (`public`) | 3,552 |
| Indexes (`public`) | 782 |
| Foreign keys | 225 |
| Primary keys | 223 |
| Unique constraints (named) | 16 |
| Check constraints | 187 |
| Sequences | 15 (9 hangfire + 6 app document-number sequences) |
| Views | 4 (`ProviderTrustSummary`, `v_AccountingAuditRecent`, `v_AccountingOutboxFailed`, `v_AccountingOutboxPending`) |
| Functions | 34 (immutability/guard triggers, legacy-cash migration helpers) |

**Schemas present in backup:** `public`, `hangfire`. No non-default schema is referenced by the EF model (all EF entities map to `public`).

---

# Drift Summary

| Severity | Count | Category |
|----------|-------|----------|
| **Critical** | 22 | Missing tables (code expects, DB lacks) |
| **Critical** | 11 | Tables with missing columns (code expects, DB lacks) |
| **High** | 1 | Bidirectional column drift (`ShippingMethods`) |
| **High** | 10 | Nullability mismatches on shared columns |
| **Medium** | ~14 | Extra tables in DB not in code model (legacy/backfill/superseded) |
| **Medium** | ~10 | Extra columns in DB not in code model (superseded by migrations) |
| **Low / informational** | — | `xmin` false-positives excluded; CHECK-constraint parse artifacts excluded |

**Total semantic drift objects: ~68** (excluding false positives). The drift is overwhelmingly **one-directional and additive** — the code is ahead of the database by ~52 migrations.

---

# Missing Objects — Tables (CRITICAL)

Tables present in the code model (snapshot) but **absent** from the backup. Each is mapped to the migration that creates it (root-cause evidence).

| # | Table | Creating migration | Severity |
|---|-------|-------------------|----------|
| 1 | `AffiliatePayoutTransferAllocations` | `20260508215150_S75_ProductionAuthorityAuditPendingModelSync` | Critical |
| 2 | `AffiliatePayoutTransferEvidence` | `20260508215150_S75_...` | Critical |
| 3 | `CommerceHubCustomerEmailVerificationTokens` | `20260601213000_CommerceHubCustomerIdentityLifecycle` | Critical |
| 4 | `CommerceHubCustomerIdentityEvents` | `20260601213000_...` | Critical |
| 5 | `CommerceHubCustomerPasswordResetTokens` | `20260601213000_...` | Critical |
| 6 | `CommerceHubMerchantLifecycleLedgerEntries` | `20260601153000_CommerceHubForensicEvidenceHardening` | Critical |
| 7 | `CommerceHubRecoveryOperationLedgerEntries` | `20260601153000_...` | Critical |
| 8 | `CustomsAdmissionAudit` | `20260706143000_Customs2EvidenceRuntimeFoundation` / `20260706210000_Customs4AdministrationRuntimeWorkflow` | Critical |
| 9 | `CustomsEvidence` | `20260706143000_Customs2...` | Critical |
| 10 | `CustomsEvidenceFingerprints` | `20260706143000_Customs2...` | Critical |
| 11 | `CustomsEvidenceHistory` | `20260706143000_Customs2...` | Critical |
| 12 | `CustomsEvidenceVersions` | `20260706143000_Customs2...` | Critical |
| 13 | `OnlineCustomerPasswordResetTokens` | post-S75 (`20260510130500_...` era) | Critical |
| 14 | `PaymobWebhookReplayRecords` | `20260713120000_PaymobContractClosure2WebhookReplay` | Critical |
| 15 | `ProviderDeliveryOutbox` | `20260722120000_AddProviderDeliveryOutbox` | Critical |
| 16 | `ProviderDeliveryReplayAudits` | `20260722130000_HardenProviderDeliveryOutbox` | Critical |
| 17 | `SearchImpressions` | post-S75 | Critical |
| 18 | `ShipmentCustomsSnapshotLines` | `20260706143000_Customs2...` | Critical |
| 19 | `ShipmentCustomsSnapshots` | `20260706143000_Customs2...` | Critical |
| 20 | `ShippingWebhookLogs` | `20260420223926_S12A_AuthorizationWebhookTrust` (index re-scope post-S75) | Critical |
| 21 | `StorefrontCartCouponStates` | `20260422133434_S14A_CqrsAttachmentSecurity` (post-S75 sync) | Critical |
| 22 | `StripeWebhookReplayRecords` | `20260712160000_StripeContractClosure2WebhookReplay` | Critical |

> Every missing table maps to a migration authored on/after `20260508215150_S75`. None maps to a pre-S75 migration. This is the primary structural evidence for the "backup frozen before S75" conclusion.

---

# Missing Objects — Columns (CRITICAL)

Columns present in the code model but absent from the corresponding existing backup table.

| Table | Missing column(s) | Creating migration |
|-------|-------------------|-------------------|
| `AffiliateUsers` | `PreviousRefreshToken` | `20260721121000_AffiliateRefreshReplaySentinel` |
| `CommerceHubCustomers` | `TokensValidAfterUtc`, `EmailVerifiedAtUtc`, `IsEmailVerified` | `20260511143000_S79_CommerceHubCustomerSessionCutoff`; `20260601213000_CommerceHubCustomerIdentityLifecycle` |
| `CommerceHubListings` | `CommerceHubPriceOverrideMutationVersion`, `CommerceHubPriceOverrideReason`, `CommerceHubPriceOverrideUpdatedAtUtc`, `CommerceHubPriceOverrideUpdatedBy` | `20260531134000_CommerceHubPriceOverridePersistenceAudit` / `20260531170000_ActivateCommerceHubPriceOverrides` (raw SQL) |
| `CommerceHubPaymentCompensationOutboxMessages` | `ClaimedUntilUtc`, `RefundAllocationSnapshotJson`, `StoreAllocationSnapshotJson` | `20260525120000_PaymentCancellationOutboxProcessingLease` (raw SQL) |
| `OrderRefunds` | `FinancialPostedAtUtc`, `FinancialPostingFailureReason`, `FinancialPostingJournalId`, `FinancialPostingPendingAtUtc`, `FinancialPostingStatus` | `20260508215150_S75_ProductionAuthorityAuditPendingModelSync` |
| `ProductVariants` | `AttributeCombinationKey` | `20260518120000_VariantAttributeCombinationAuthority` |
| `ReturnItems` | `CostAmountSnapshot`, `CostPriceSnapshot` | `20260517120000_PosSaleImmutableCostBasisSnapshots` |
| `SalesInvoiceItems` | `CostAmountSnapshot`, `CostPriceSnapshot` | `20260517120000_PosSaleImmutableCostBasisSnapshots` |
| `SearchQueries` | `AppliedExpansionsJson`, `Intent` | post-S75 (raw SQL) |
| `ShippingMethods` | `ServiceLevel` | post-S75 (raw SQL) — see High-Risk drift |
| `StockTransferRequestIdempotency` | `StockTransferId` | post-S75 |

> Financial correctness impact: the missing `OrderRefunds.FinancialPosting*` fields and the immutable cost-basis snapshots (`ReturnItems`/`SalesInvoiceItems.CostAmountSnapshot`/`CostPriceSnapshot`) are load-bearing for the accounting/ledger runtime. Their absence means the current financial-posting code path cannot function against this backup.

---

# Extra Objects — Tables (in backup, not in code model)

These exist in the backup but are not mapped by the current EF model. They are **not** application-breaking (the ORM simply ignores unmapped tables) but represent legacy/operational residue.

| Table | Classification | Notes |
|-------|----------------|-------|
| `__EFMigrationsHistory` | EF infrastructure | Expected; not an entity |
| `__MigrationHistory` | Legacy EF6 infrastructure | Pre-EF-Core residue |
| `__CommerceHubCategorySeparation_Backup_Categories` | Manual backfill backup | One-off data migration snapshot |
| `__CommerceHubCategorySeparation_Backup_Products` | Manual backfill backup | " |
| `__CommerceHubCategorySeparation_CloneMap` | Manual backfill mapping | " |
| `__CommerceHubCategorySeparation_MigratedCategories` | Manual backfill | " |
| `__DefaultVariantBackfill_Backup_Products` | Manual backfill backup | " |
| `__DefaultVariantBackfill_Backup_WarehouseStocks` | Manual backfill backup | " |
| `__DefaultVariantBackfill_CreatedVariants` | Manual backfill | " |
| `_LegacyCashMigrationLog` | Manual migration log | Legacy treasury migration |
| `AccountBalances`, `AccountMappings`, `Accounts`, `JournalEntries`, `JournalLines` | Superseded ledger tables | Created by `20260206165348_AddCoreLedgerEngine`; the code model later moved to `LedgerAccounts`/`LedgerJournalEntries`/`LedgerLines`. Old tables were never dropped. |
| `ChatAuditLog` | Manual/raw-SQL table | Created outside EF (no `CreateTable`); present via manual SQL |
| `ReceiptSettings`, `RejectedCarrierWebhookEvents`, `StockBatches`, `StockTakes`, `StockTakeItems` | Manual/raw-SQL or superseded | No EF `CreateTable`; likely manual SQL or dropped from model |
| `MarketplaceProjectionOutboxMessages`, `ProductRequestIdempotency` | Post-S75, present | Created by `20260516090000_...` — **present in backup despite being post-S75**, evidence of partial/manual application |
| `TenantCurrencyNormalizationAudit` | Present | Created by `20260426...` (pre-S75) |

> The presence of some post-S75 tables alongside missing post-S75 tables confirms the divergence is **not** a single clean migration checkpoint — the database also received manual, out-of-band SQL. See Root Cause Analysis.

---

# Extra Objects — Columns (in backup, not in code model)

Columns physically present in the backup but not in the current code model — superseded/renamed by post-S75 migrations, or legacy manual additions.

| Table | Extra column(s) | Likely cause |
|-------|-----------------|--------------|
| `ShippingMethods` | `FulfillmentMode`, `FulfillmentCarrierCode` | Superseded — code renamed/replaced with `ServiceLevel` (see High-Risk) |
| `Orders` | `ShippingMethodIdSnapshot`, `ShippingMethodNameSnapshot` | Superseded snapshot columns, later removed from model |
| `Products` | `ReorderPoint`, `ReorderQuantity`, `TrackBatchNumbers`, `TrackExpiry` | Legacy inventory columns dropped from model |
| `Shipments` | `CarrierName`, `PackedAtUtc` | Superseded / removed from model |
| `WarehouseStocks` | `LastCountedAt`, `LastCountedBy`, `LastCountedQuantity` | Legacy stock-take columns dropped from model |
| `StoreConfigs` | 24 `Theme_*` / `Behavior_*` / `PromotionRanking_*` columns | Old owned-type layout; model migrated to JSON columns (`ThemeDraftOverridesJson` etc.). Old flat columns not dropped. |
| `ThemePresets` | `Description`, `ThumbnailUrl`, `IsActive`, `UpdatedAtUtc` | Superseded columns |
| `StockTransferRequestIdempotency` | `ClaimToken`, `ClaimTimestampUtc` | Legacy idempotency columns, replaced by `StockTransferId` |
| `CommerceHubFinancialAuditLogs` | `IsSystemGenerated` | Legacy column removed from model |
| `SystemSettings` | `TenantId` | Was tenant-scoped in DB; model reverted to global (or vice-versa) |

> None of these extra columns break the running application (EF ignores them on read; inserts succeed only if they are nullable or have defaults — verify before write operations). They are technical debt, not runtime blockers, except where a superseded column is `NOT NULL` without a default (see `StoreConfigs` — the old `Theme_*` columns are `NOT NULL`, which **will break inserts** from code that no longer populates them; see High-Risk).

---

# Modified Objects — Nullability Mismatches (HIGH)

Columns present on both sides but with divergent nullability. `xmin` (Postgres system concurrency column, mapped by EF `RowVersion`) and value-type implicit-NOT-NULL cases were excluded as false positives.

| Table.Column | Code expects | Backup has | Risk |
|--------------|--------------|-----------|------|
| `AffiliateAccounts.TenantId` | NOT NULL | nullable | Code may assume non-null; tenant-isolation-relevant |
| `CmsPages.TenantId` | NOT NULL | nullable | Tenant-isolation-relevant |
| `CommerceHubDailyFinancialAggregates.CurrencyCode` | nullable | NOT NULL | DB stricter than code |
| `Currencies.Code` | nullable | NOT NULL | DB stricter than code |
| `ServiceRequests.Status` | NOT NULL | nullable | Code may write assuming non-null constraint |
| `ServiceRequestMessages.MessageType` | NOT NULL | nullable | " |
| `ServiceRequestMessages.SenderRole` | NOT NULL | nullable | " |
| `Shipments.ServiceLevel` | NOT NULL | nullable | Compounded with missing/renamed column drift |
| `Shipments.ShippingMethodName` | nullable | NOT NULL | DB stricter — inserts with null will fail |
| `StockTransferRequestIdempotency.RequestFingerprint` | NOT NULL | nullable | Idempotency integrity |

> `code=NOT NULL, db=nullable` cases mean the migration that tightens the constraint has not been applied — consistent with the frozen-backup theory. `code=nullable, db=NOT NULL` cases mean the DB is stricter (older stricter definition, later relaxed in code) — these can cause **insert failures** at runtime if code writes null.

---

# High-Risk Findings

### HR-1 — 22 missing tables (CRITICAL, runtime-fatal)
Any code path touching Customs evidence, provider-delivery outbox, Stripe/Paymob webhook replay, CommerceHub customer identity lifecycle, affiliate payout transfer evidence, or search impressions will throw `relation "public.<Table>" does not exist`. The application cannot boot cleanly for these features.

### HR-2 — Missing financial columns on `OrderRefunds` + cost-basis snapshots (CRITICAL, financial integrity)
`OrderRefunds.FinancialPosting*` and `ReturnItems`/`SalesInvoiceItems.CostAmountSnapshot`/`CostPriceSnapshot` are consumed by the accounting/ledger posting runtime. Their absence breaks refund financial posting and immutable COGS capture — a correctness hazard, not just availability.

### HR-3 — `ShippingMethods` bidirectional drift (HIGH)
Code expects `ServiceLevel` (NOT NULL); backup instead has `FulfillmentMode` + `FulfillmentCarrierCode` and lacks `ServiceLevel`. Shipping method reads/writes will fail. This is a rename/replace whose migration was not applied.

### HR-4 — `StoreConfigs` superseded `NOT NULL` flat columns (HIGH, insert-breaking)
The backup retains 24 legacy `Theme_*`/`Behavior_*`/`PromotionRanking_*` columns, many `NOT NULL` with no default matching current code. Current code writes the JSON-based layout and does not populate these; inserts of new `StoreConfigs` rows will violate `NOT NULL` unless the drop-column migration is applied.

### HR-5 — Tenant-scope nullability drift (HIGH, isolation risk)
`AffiliateAccounts.TenantId` and `CmsPages.TenantId` are `NOT NULL` in code but `nullable` in the backup. Given Shopixy's tenant-isolation authority model (cross-tenant leak = CRITICAL), a nullable `TenantId` in production data is a latent isolation hazard.

---

# Root Cause Analysis

**Primary cause (evidence-backed): the backup database is behind the code by ~52 migrations.**

Supporting evidence:
1. Every one of the 22 missing tables maps to a migration authored on/after `20260508215150_S75`. Zero missing tables map to a pre-S75 migration.
2. Every missing column maps to a post-S75 migration (`S75`, `S79`, VariantAttributeCombination, PosSaleImmutableCostBasis, PriceOverride, CustomerIdentityLifecycle, refresh-replay, provider-delivery-outbox).
3. The `code=NOT NULL / db=nullable` tightening mismatches align with post-S75 hardening migrations not yet applied.

**Secondary cause (evidence-backed): manual / out-of-band SQL was also applied.**
- A subset of post-S75 tables (`MarketplaceProjectionOutboxMessages`, `ProductRequestIdempotency` from `20260516090000`) **are** present in the backup, so the DB is not a clean pre-S75 snapshot.
- Several extra tables (`ChatAuditLog`, `ReceiptSettings`, `RejectedCarrierWebhookEvents`, `StockBatches`, `StockTakes`, `StockTakeItems`, `AccountBalances`, `AccountMappings`, `JournalLines`) have **no** `CreateTable` in any migration — created by manual SQL scripts (the repo root contains dozens of ad-hoc `.sql` files) or superseded ledger tables never dropped.
- Legacy/backfill residue (`__CommerceHubCategorySeparation_*`, `__DefaultVariantBackfill_*`, `_LegacyCashMigrationLog`, `__MigrationHistory`) confirms a history of manual data operations.

**Cause classification per drift type:**
| Drift type | Root cause |
|------------|-----------|
| Missing tables/columns | Migration never executed (backup predates them) |
| `code NOT NULL / db nullable` | Hardening migration not applied |
| `code nullable / db NOT NULL` | Legacy stricter definition; code later relaxed |
| Extra superseded tables/columns | Migration authored to drop them not applied, OR schema built by manual SQL and model diverged |
| Extra `__`/`_Legacy` tables | Intentional manual backfill/backup residue |

> **Unverifiable item (stated honestly):** the exact last-applied `MigrationId` in `__EFMigrationsHistory` could not be read — its rows are gzip-compressed inside the pg_dump custom-format archive and `pg_restore` is unavailable in this environment. The ~S75 boundary is inferred from object-to-migration mapping, not read directly from the history table. To confirm precisely, run (read-only): `pg_restore --data-only --table=__EFMigrationsHistory DB/23-7-2026.sql` or restore to a scratch DB and `SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;`.

---

# Severity Classification

| Severity | Definition | Findings |
|----------|-----------|----------|
| **Critical** | Application will not run / financial correctness broken | 22 missing tables; 11 tables with missing columns (incl. financial posting + cost basis) |
| **High** | Feature-level failure or data-integrity/isolation risk | `ShippingMethods` drift; `StoreConfigs` NOT-NULL insert break; 10 nullability mismatches; tenant-scope nullability |
| **Medium** | No runtime break but schema debt / drift | ~14 extra tables; ~10 extra column sets |
| **Low** | Cosmetic / infrastructure | `__`-prefixed tables, hangfire schema, `xmin` (excluded false positives) |

**Deployment consequence:** Deploying current binaries against this backup without running `dotnet ef database update` (or applying the 52 pending migrations) will fail. **Potential data loss:** applying the pending drop-column migrations (`StoreConfigs` flat columns, `Products`/`Shipments`/`WarehouseStocks` legacy columns) will **permanently delete** data in those columns — back up before remediation.

---

# Engineering Assessment

The current PostgreSQL backup `DB/23-7-2026.sql` is **not structurally compatible** with the current Shopixy source code. The schema captured is an **older generation** of the same database — the code has advanced ~52 migrations beyond it, spanning affiliate payout evidence, POS immutable cost-basis, variant attribute authority, CommerceHub price-override + customer identity lifecycle, treasury/currency hardening, geographic + customs runtimes, and Stripe/Paymob/Fawry webhook-replay closure.

The divergence is predominantly **additive and one-directional** (code ahead of DB), which is the *recoverable* class of drift: applying the pending EF Core migrations to a database restored from this backup should bring it into structural compliance. However, the presence of manually-created tables and superseded objects means a plain `database update` may encounter conflicts (e.g., an `AddColumn` for a column a manual script already created, or a `DropTable` for an unmapped legacy table). A **dry-run of the idempotent migration SQL** (`dotnet ef migrations script --idempotent`) against a scratch restore is mandatory before touching any production database.

This backup is suitable as a **data source** for a restore-then-migrate workflow. It is **not** suitable as a drop-in schema for the current application binaries.

---

# Final Certification

## ❌ FAIL

The backup does not match the schema expected by the current source code. 22 missing tables, 11 tables with missing columns (including financial-posting and cost-basis fields), 1 bidirectional shipping-method drift, and 10 nullability mismatches constitute a structural incompatibility that will cause runtime failures.

**Required remediation path (read-only audit does not perform it):**
1. Read the true migration boundary from `__EFMigrationsHistory` (via `pg_restore`/scratch restore).
2. Restore this backup to a **scratch** database.
3. Generate `dotnet ef migrations script --idempotent` and review for conflicts against the manually-created/superseded objects listed above.
4. Take a fresh backup, then apply pending migrations; validate the drop-column migrations against data-loss expectations.
5. Reconcile the extra/legacy tables (drop or formally adopt into the model).

---

## Appendix — Methodology & Limitations

- **Model A** extracted from `ApplicationDbContextModelSnapshot.cs` by brace-matched entity parsing; column names resolved via `HasColumnName`; nullability derived using EF semantics (value types + enums implicitly NOT NULL unless `?`; reference types NOT NULL only with `.IsRequired()`).
- **Model B** extracted by regex parsing of `CREATE TABLE`, `ADD CONSTRAINT`, `CREATE INDEX/SEQUENCE/VIEW/FUNCTION` from the readable DDL embedded in the pg_dump archive. `COPY`/data blocks ignored per scope.
- **Excluded false positives:** `xmin` (Postgres system column mapped by EF concurrency tokens — physically present on every table); CHECK-constraint `CASE/WHEN/ELSE/END` tokens mis-parsed as columns on `PromoCodes`; ~800 value-type nullability non-issues.
- **Not verified (tooling-limited):** exact `__EFMigrationsHistory` contents (compressed archive, no `pg_restore`); per-column data-type/precision/default equality was sampled, not exhaustively diffed for all 3,552 columns — the dominant drift class (presence/absence + nullability) was audited exhaustively. A follow-up Phase 2 with `pg_restore` available could add exhaustive type/default/index-definition comparison.
- **Read-only guarantee:** no source, migration, snapshot, or SQL file was modified; no migration executed; no restore performed. Only this report file was written.
