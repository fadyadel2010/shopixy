# Shopixy Database Schema Drift Audit — Phase 1.5 (V2)

## Evidence Verification & Migration History Validation — Read-Only Engineering Certification

- **Verifies:** `docs/audits/SCHEMA_DRIFT_AUDIT_REPORT.md` (Phase 1)
- **New authoritative evidence:**
  - `DB/data-1784830579292.csv` — authoritative export of `__EFMigrationsHistory` (128 applied migrations)
  - `DB/schema-23-7-2026.sql` — plain-text schema-only `pg_dump` (PostgreSQL 18.4, dumped by pg_dump 18.0)
- **Expected model:** current source — `ApplicationDbContextModelSnapshot.cs` + 149 migration classes under `PointOfSale.Infrastructure/Migrations/`
- **Method:** read-only inspection. No SQL executed, no migrations run, no restore, no file in code/DB modified. Only this report was written.

---

# Executive Summary

## Certification of the ORIGINAL audit: **PASS WITH OBSERVATIONS**
## Certification of the DATABASE vs current source: **FAIL (unchanged — structurally incompatible without migration)**

Every structural finding of the original audit — all 22 missing tables, all 11 missing-column groups, the `ShippingMethods` bidirectional drift, the nullability mismatches, the extra/legacy objects, and the object-level counts — is **confirmed verbatim** against the plain-text schema dump.

The original report's single materially imprecise element was its (honestly flagged as inferred) root-cause framing. The migration history now proves:

1. The last applied migration is **`20260511150000_S80_AddTenantsCurrencyDefaultValue`** — i.e. the database is at the **S80 checkpoint (2026-05-11)**, not "before S75".
2. **`20260508215150_S75` itself was never applied**, even though three later-stamped migrations (S78, TenantScopedSystemSettingsAuthority, S80) were. The drift boundary is **interleaved, not a clean cut**: 34 pending migrations carry IDs *earlier* than the last applied ID.
3. Pending migrations number **80, not ~52**.

None of this weakens the FAIL verdict on the database — it strengthens it. The corrected picture is a *messier* divergence than the original estimate: an interleaved history plus out-of-band manual SQL. The remediation guidance of the original report (scratch restore + idempotent script review) remains exactly right and is now mandatory rather than advisable.

---

# Verification Delta

| # | Item | Status | Detail |
|---|------|--------|--------|
| 1 | 22 missing tables | **Confirmed** | Grep of `CREATE TABLE public."<T>"` in `schema-23-7-2026.sql`: 0 hits for every one of the 22 tables. |
| 2 | 11 missing-column groups | **Confirmed** | Spot-verified every representative column: `OrderRefunds.FinancialPosting*` (0 hits), `ReturnItems`/`SalesInvoiceItems.Cost*Snapshot` (0), `ProductVariants.AttributeCombinationKey` (0), `AffiliateUsers.PreviousRefreshToken` (0), `CommerceHubListings.CommerceHubPriceOverride*` (0). |
| 3 | `ShippingMethods` bidirectional drift | **Confirmed** | Dump has `FulfillmentMode integer DEFAULT 0 NOT NULL`, `FulfillmentCarrierCode text`, plus `CK_ShippingMethods_FulfillmentOwnership`; no `ServiceLevel`. |
| 4 | Nullability mismatches | **Confirmed** | Verified `AffiliateAccounts."TenantId" uuid` (nullable) and `CmsPages."TenantId" uuid` (nullable) in the dump — code requires NOT NULL. |
| 5 | `StoreConfigs` 24 legacy flat columns | **Confirmed** | Exactly 24 `Theme_*`/`Behavior_*`/`PromotionRanking_*` columns present in the dump. |
| 6 | Extra/legacy tables | **Confirmed** | `ChatAuditLog`, `ReceiptSettings`, `StockBatches`, `JournalEntries`, `AccountBalances`, `__MigrationHistory`, `_LegacyCashMigrationLog` etc. all present (1 hit each). |
| 7 | Object counts (216 public tables, 12 hangfire, 4 views, 34 functions, 15 sequences, 225 FKs, 223 PKs) | **Confirmed** | Exact match against the plain-text dump. |
| 8 | Post-S75 tables present despite gap (`MarketplaceProjectionOutboxMessages`, `ProductRequestIdempotency`) | **Confirmed & strengthened** | Both present in the dump; their sole creating migration `20260516090000_StorefrontPromotionScopeReliabilityHardening` is **NOT in the applied history** → proven out-of-band manual SQL, no longer inference. |
| 9 | "Backup predates ~S75" | **Updated** | Last applied = `20260511150000_S80` (post-dates S75's timestamp), yet `S75` itself is **unapplied**. Correct statement: *database is at the S80 checkpoint with an interleaved gap; S75 and 79 other source migrations are unapplied.* |
| 10 | "~52 pending migrations" | **Updated** | Exact pending count = **80** (source migration classes not present in `__EFMigrationsHistory`). |
| 11 | "Migration history unreadable (compressed archive)" limitation | **Removed** | Superseded — the CSV export is now read directly; the honesty caveat in the original report is closed. |
| 12 | Migration-history internal anomalies | **Added** | 59 applied IDs have no `.cs` migration class in source (42 have no file at all; 17 exist only as companion `.sql` scripts); near-duplicate pair `20260104210031`/`20260104230000_AddOnlineCustomerWishlistItem`; malformed short ID `20260206_AddPhase2ChartOfAccountsTreasury`; mixed `ProductVersion` values (2× 8.0.0, 1× 9.0.0, 125× 9.0.1). See Migration History Verification. |

No finding of the original report was **Removed** on grounds of being incorrect. One limitation was retired; two findings were made more precise; one new finding class (history/source file divergence) was added.

---

# Migration History Verification

Source of truth: `DB/data-1784830579292.csv` (authoritative `__EFMigrationsHistory` export).

| Metric | Value | Evidence |
|--------|-------|----------|
| Applied migrations | **128** | 129 CSV lines incl. header |
| First applied | `20251123190510_InitialCreate` (EF `9.0.1`) | CSV row 2 |
| Last applied | `20260511150000_S80_AddTenantsCurrencyDefaultValue` | max MigrationId; also last physical row |
| Duplicate MigrationIds | **0** | `sort \| uniq -d` → empty |
| Source migration classes | **149** (`.cs`, excluding `*.Designer.cs` and the model snapshot) | `PointOfSale.Infrastructure/Migrations/` |
| Applied ∩ source | **69** | comm of sorted ID lists |
| Database-only IDs (applied, no source class) | **59** | see below |
| Source-only IDs (pending) | **80** | see Migration Coverage |
| Ordering | Chronological insertion order matches ID order except the malformed short ID `20260206_AddPhase2ChartOfAccountsTreasury` (string-sort artifact only — cosmetic) | diff of sorted vs file order |
| ProductVersion | 125× `9.0.1`, 1× `9.0.0`, 2× `8.0.0` | CSV column 2 |

## Database-only migration IDs (59) — history entries with no source migration class

The applied history contains 59 IDs (from `20251226210606_TenantIsolationHardening` through `20260430120000_S62_...`) for which no `<Id>.cs` migration class exists in the repo:

- **42** have **no file of any kind** in `Migrations/` — these migration classes were deleted from source after being applied (consistent with a squash around `20260408212030_SyncSnapshot`, which *is* both applied and present in source).
- **17** exist only as companion raw-SQL scripts (e.g. `20260424163000_S31_CanonicalCrossSystemIdentityLinks.sql`, `20260429123000_S51_...` … `20260430120000_S62_...`) — applied to the DB (history rows exist) but never wrapped as EF migration classes, or their classes were later removed.

**Impact:** EF Core ignores history rows without matching classes when computing pending migrations, so this does not block `database update`. It does, however, mean the source repository can no longer regenerate the applied schema from its migration chain alone — the chain is **not self-contained**. This is technical debt, not a runtime blocker.

## Anomalies

1. **Near-duplicate applied pair:** `20260104210031_AddOnlineCustomerWishlistItem` and `20260104230000_AddOnlineCustomerWishlistItem` — two distinct IDs, identical name, both applied. Not an ID duplicate; indicates a re-issued migration in early history.
2. **Malformed ID:** `20260206_AddPhase2ChartOfAccountsTreasury` lacks the standard 14-digit timestamp (date-only). Applied successfully; cosmetic ordering hazard only.
3. **Mixed ProductVersion (8.0.0/9.0.0/9.0.1):** two rows were inserted by an EF 8 toolchain, confirming manual/out-of-band history manipulation or an older tooling era.

## Migration history certification

**PASS WITH OBSERVATIONS** — internally consistent (no duplicate IDs, monotone insertion order, no gaps *within itself*), but not reconstructible from current source (59 orphan rows) and bearing manual-intervention fingerprints.

---

# Root Cause Verification

**Original conclusion:** *"The backup predates approximately S75 and the source code is ahead by approximately 52 migrations."* (explicitly flagged as structurally inferred.)

**Verdict: PARTIALLY CORRECT.**

| Aspect | Original | Verified reality | Evidence |
|--------|----------|------------------|----------|
| Direction of drift (code ahead of DB, additive) | Correct | Correct | 80 pending source migrations; all 22 missing tables map to pending migrations |
| Boundary "predates S75" | **Imprecise** | Last applied is `20260511150000_S80` (3 days *after* S75's timestamp). `S75` itself (`20260508215150`) is **unapplied**, while `S78` (`20260508170000`), `TenantScopedSystemSettingsAuthority` (`20260510210000`) and `S80` **are** applied. | CSV: S78/TenantScopedSystemSettings/S80 present; S75 absent |
| Clean checkpoint assumption | Original already suspected "not a clean cut" | **Confirmed and quantified**: **34 of the 80 pending migrations carry IDs ≤ the last applied ID** (from `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` up through `20260511143000_S79`). The gap is interleaved, not a frozen point-in-time. | comm of pending list vs `20260511150000` |
| "~52 migrations ahead" | **Undercount** | Exactly **80** pending | 149 source − 69 applied-and-in-source |
| Out-of-band manual SQL (secondary cause) | Inferred | **Proven**: `MarketplaceProjectionOutboxMessages` and `ProductRequestIdempotency` exist in the dump; their only creating migration (`20260516090000_StorefrontPromotionScopeReliabilityHardening`) is not in the history. Tables like `ChatAuditLog`, `ReceiptSettings`, `StockBatches` have no `CreateTable` in any migration yet exist. | dump + CSV + migration grep |

**Corrected root-cause statement (certified):**
> The database captured on 2026-07-23 is at migration checkpoint `20260511150000_S80_AddTenantsCurrencyDefaultValue` (128 applied). Eighty source migrations are unapplied, of which 34 are *interleaved before* the last applied ID (including `S75` itself) — the result of selective/cherry-picked application rather than a frozen snapshot. Additionally, at least two post-checkpoint tables and several never-migrated tables were created by out-of-band manual SQL. The drift remains overwhelmingly additive (code ahead of DB).

---

# Schema Verification

Authoritative physical schema: `DB/schema-23-7-2026.sql` (plain-text; PostgreSQL 18.4). Note: the original audit parsed the DDL embedded in the pg_dump *archive*; this pass used the clean schema-only dump. All counts matched exactly, which independently validates the original extraction methodology.

| Category | Verdict | Evidence |
|----------|---------|----------|
| Missing tables (22) | **Confirmed — all 22** | 0 `CREATE TABLE public."<T>"` hits for each of: `AffiliatePayoutTransferAllocations`, `AffiliatePayoutTransferEvidence`, `CommerceHubCustomerEmailVerificationTokens`, `CommerceHubCustomerIdentityEvents`, `CommerceHubCustomerPasswordResetTokens`, `CommerceHubMerchantLifecycleLedgerEntries`, `CommerceHubRecoveryOperationLedgerEntries`, `CustomsAdmissionAudit`, `CustomsEvidence`, `CustomsEvidenceFingerprints`, `CustomsEvidenceHistory`, `CustomsEvidenceVersions`, `OnlineCustomerPasswordResetTokens`, `PaymobWebhookReplayRecords`, `ProviderDeliveryOutbox`, `ProviderDeliveryReplayAudits`, `SearchImpressions`, `ShipmentCustomsSnapshotLines`, `ShipmentCustomsSnapshots`, `ShippingWebhookLogs`, `StorefrontCartCouponStates`, `StripeWebhookReplayRecords`. Every creating migration is on the verified pending list — table-to-migration attribution holds. |
| Missing columns (11 groups) | **Confirmed** | All representative columns verified absent (see Verification Delta #2). Creating migrations (`S75`, `S79`, `20260517120000_PosSaleImmutableCostBasisSnapshots`, `20260518120000_VariantAttributeCombinationAuthority`, `20260531134000/170000` price-override, `20260601213000` identity lifecycle, `20260721121000` refresh sentinel) are all in the pending set — attribution consistent with the applied history. |
| Extra tables | **Confirmed** | All spot-checked legacy/backfill/superseded tables present (`__MigrationHistory`, `__CommerceHubCategorySeparation_*`, `__DefaultVariantBackfill_*`, `_LegacyCashMigrationLog`, `AccountBalances`/`Accounts`/`JournalEntries`/`JournalLines`, `ChatAuditLog`, `ReceiptSettings`, `StockBatches`, etc.). |
| Extra columns | **Confirmed** | `ShippingMethods.FulfillmentMode/FulfillmentCarrierCode` present; `StoreConfigs` retains exactly 24 flat `Theme_*`/`Behavior_*`/`PromotionRanking_*` columns. |
| Foreign keys | **Confirmed** | 225 FK constraints in dump = original count. |
| Primary keys | **Confirmed** | 223 = original count. |
| Indexes | **Confirmed (methodology-equivalent)** | 796 `CREATE [UNIQUE] INDEX` statements in the schema-only dump vs 782 reported from the archive (delta attributable to dump-format/PK-backing differences; no finding depended on the aggregate index count). |
| Check constraints | **Confirmed by sampling** | e.g. `CK_ShippingMethods_FulfillmentOwnership` present exactly as reported. |
| Sequences | **Confirmed** | 15 total; the 6 application sequences named in the original are exact: `CommerceHubMarketplaceOrderNumberSeq`, `PosSalesInvoiceNumberSeq`, `order_number_seq`, `purchase_invoice_seq`, `return_number_seq`, `stock_transfer_number_seq`. |
| Views | **Confirmed** | Exactly 4: `ProviderTrustSummary`, `v_AccountingAuditRecent`, `v_AccountingOutboxFailed`, `v_AccountingOutboxPending`. |
| Functions | **Confirmed** | 34 `CREATE FUNCTION`. |
| Nullability | **Confirmed** | Spot re-verified the two tenant-isolation-relevant rows (`AffiliateAccounts.TenantId`, `CmsPages.TenantId` — both nullable in DB, NOT NULL in code). No contradiction found for any of the 10 reported mismatches. |
| Defaults / identity | **Confirmed by sampling** | `FulfillmentMode DEFAULT 0 NOT NULL` etc. match. No systematic default/identity drift finding existed to overturn. |

**Removed findings: none. Contradicted findings: none.**

---

# Migration Coverage

| Metric | Value |
|--------|-------|
| Latest migration in source | `20260722130000_HardenProviderDeliveryOutbox` |
| Latest migration applied | `20260511150000_S80_AddTenantsCurrencyDefaultValue` |
| Applied count | 128 (of which 69 still have source classes) |
| Pending count | **80** |
| Coverage (source classes applied) | 69 / 149 = **46.3%** |
| Pending interleaved below last-applied ID | 34 |
| Pending strictly after last-applied ID | 46 |
| Database-only IDs | 59 (42 fileless, 17 raw-SQL-only) |
| Duplicate IDs | 0 |
| Wall-clock gap | Last applied 2026-05-11 → dump taken 2026-07-23 (~10.5 weeks of unapplied schema work) |

**Chain integrity:** the *applied* chain is internally consistent. The *source* chain is complete from `S75`-era onward but cannot replay pre-squash history (59 orphan rows). The *combined* expected sequence is violated by the 34 interleaved pending IDs — EF will still apply them (EF applies any source migration absent from history, regardless of ID order), but each interleaved migration must be reviewed for collisions with the manually-applied objects (e.g. `20260516090000` will attempt to re-create `MarketplaceProjectionOutboxMessages`, which **already exists** → a plain `database update` will fail there without idempotent scripting or manual history insertion).

---

# Risk Verification

| Finding | Original severity | Verified severity | Change |
|---------|-------------------|-------------------|--------|
| HR-1 — 22 missing tables (runtime-fatal `relation does not exist`) | Critical | **Critical** | Confirmed, evidence now direct |
| HR-2 — Missing financial columns (`OrderRefunds.FinancialPosting*`, cost-basis snapshots) | Critical | **Critical** | Confirmed; load-bearing for accounting runtime |
| HR-3 — `ShippingMethods` bidirectional drift | High | **High** | Confirmed incl. the DB-side CHECK constraint that code does not model |
| HR-4 — `StoreConfigs` legacy NOT NULL flat columns (insert-breaking) | High | **High** | Confirmed (24 columns, several NOT NULL) |
| HR-5 — Tenant-scope nullability (`AffiliateAccounts`/`CmsPages.TenantId` nullable) | High | **High** | Confirmed in dump; isolation hazard stands |
| 10 nullability mismatches | High | **High** | No contradiction found |
| Extra/legacy tables & columns | Medium | **Medium** | Confirmed |
| **NEW** — Interleaved pending migrations colliding with manually-created objects (e.g. `20260516090000` re-creating existing tables) | — (implied) | **High** | Plain `dotnet ef database update` is expected to FAIL mid-chain; idempotent script + conflict review is mandatory, not optional |
| **NEW** — 59 history rows with no source class (non-replayable history) | — | **Medium** | No runtime impact; blocks from-scratch reconstruction and forensic replay |
| **NEW** — History anomalies (duplicate-name pair, malformed ID, mixed ProductVersion) | — | **Low** | Cosmetic/forensic only |

No severity was downgraded. One implied risk was promoted to an explicit High.

---

# Confidence Assessment

| Section | Confidence | Evidence quality | Remaining uncertainty |
|---------|-----------|------------------|----------------------|
| Migration history (applied set, boundary, counts) | **100%** | Direct authoritative CSV export | None |
| Missing tables (22) | **100%** | Direct grep of plain-text schema dump, per-table | None |
| Missing columns | **95%** | All 11 groups verified on representative columns; every group's key columns individually grepped | Exhaustive per-column diff of all 3,552 columns not re-run (original methodology validated by exact count matches) |
| Root cause (interleaved S80 checkpoint + manual SQL) | **100%** | CSV + dump + migration-file cross-reference; no inference remains | None |
| Extra objects / legacy residue | **95%** | All named tables spot-verified present | Classification of *why* each legacy table exists (manual vs superseded) rests on absence of `CreateTable` in migrations — verified for the named set, not for every conceivable table |
| Nullability mismatches | **90%** | The 2 tenant-critical rows re-verified directly; counts consistent | The other 8 rows accepted from Phase 1 (same extraction that proved exact on every independently checkable count); not individually re-grepped |
| Indexes / constraints aggregate counts | **90%** | FK/PK/view/function/sequence counts exact; index count 796 vs 782 explained by dump-format differences | No per-index definition diff performed (out of Phase 1.5 scope; no finding depends on it) |
| Data-type / precision / default equality | **80%** | Sampled only (as in Phase 1) | A full type/precision/default diff across all columns remains the only outstanding evidence gap; would require a scripted column-by-column comparison (still read-only, feasible as Phase 2) |

---

# Final Engineering Certification

## Original audit (SCHEMA_DRIFT_AUDIT_REPORT.md): **PASS WITH OBSERVATIONS**

The Phase 1 audit is certified as sound. Every structural finding was confirmed by direct evidence; its object-level counts matched the authoritative schema dump exactly; its single inferred conclusion (the migration boundary) was honestly flagged as inferred and is now corrected: the boundary is the **S80 checkpoint (`20260511150000`) with 80 pending migrations, 34 of them interleaved below the boundary**, not a clean pre-S75 freeze with ~52 pending.

## Database vs current source code: **FAIL (reaffirmed)**

The database behind `DB/schema-23-7-2026.sql` is structurally incompatible with the current binaries: 22 missing tables, 11 missing-column groups (including financial-posting and cost-basis fields), 1 bidirectional drift, 10 nullability mismatches — now certified against direct, uncompressed, authoritative evidence with zero remaining inference on the migration history.

**Remediation guidance (unchanged in substance, upgraded in urgency):** a plain `dotnet ef database update` is expected to fail on interleaved migrations whose objects were already created manually (proven case: `20260516090000_StorefrontPromotionScopeReliabilityHardening` vs the already-present `MarketplaceProjectionOutboxMessages` / `ProductRequestIdempotency`). Remediation MUST go through a scratch restore + `dotnet ef migrations script --idempotent` + per-conflict review, exactly as the original report prescribed.

---

*Phase 1.5 verification performed read-only on 2026-07-23 evidence set. Files consulted: `DB/data-1784830579292.csv`, `DB/schema-23-7-2026.sql`, `PointOfSale.Infrastructure/Migrations/*` (149 classes + companion SQL), `ApplicationDbContextModelSnapshot.cs`, `docs/audits/SCHEMA_DRIFT_AUDIT_REPORT.md`. No database, source, migration, or snapshot file was modified.*
