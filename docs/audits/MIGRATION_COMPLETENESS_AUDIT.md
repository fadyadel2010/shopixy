# MIGRATION COMPLETENESS AUDIT — PHASE 2: RUNTIME MODEL CERTIFICATION

Date: 2026-07-23
Scope: Source code ↔ Runtime EF Model ↔ ApplicationDbContextModelSnapshot ↔ Migration chain
Method: READ-ONLY. The runtime model and the compiled snapshot model were both materialized
in-process from the current source (no database connection was opened) and compared with EF Core's
own `IMigrationsModelDiffer` (the exact component `dotnet ef migrations add` uses). Every diff
operation was then cross-referenced token-by-token against the full migration chain
(149 migration classes + 93 companion SQL files under `PointOfSale.Infrastructure/Migrations/`).
The database was NOT used as evidence anywhere in this report.

---

# Executive Summary

## Migration Completeness Certification: **FAIL**

1. **The model snapshot is severely stale.** `IMigrationsModelDiffer.HasDifferences(snapshot, runtime)`
   returns **TRUE** with **384 pending operations**. `dotnet ef migrations has-pending-model-changes`
   confirms: *"Changes have been made to the model since the last migration."*
   The snapshot contains **221 entity types**; the runtime model contains **263**. **42 entire
   entities are absent from the snapshot**, plus 134 missing columns, 149 missing indexes,
   31 missing check constraints, 4 missing FKs, 2 missing alternate keys.

2. **The snapshot has not been regenerated since ~2026-05-10.** The last migration with a
   `.Designer.cs` file is `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndex…`.
   The **100 subsequent migrations have no Designer files** — they are hand-written
   `migrationBuilder.Sql(...)`/builder migrations added without running `dotnet ef migrations add`,
   so the snapshot was never advanced.

3. **Most drift IS covered by the hand-written chain** (320 of 384 diff operations have their
   object names present somewhere in the chain — e.g. the entire Geographic subsystem is created by
   raw SQL in `20260705170009_InitialGeographicRuntimeFoundation.cs`), **but 64 operations are
   covered NOWHERE in the chain**, including **7 whole tables (Carrier administration runtime),
   1 column, 1 FK, and ~50 indexes** that exist in the runtime model but were never migrated by any
   mechanism.

4. Consequence: replaying the migration chain from zero **cannot** recreate the current runtime
   model. Certification is FAIL on both axes (snapshot consistency AND chain completeness).

---

# Runtime Model Summary

Built from `ApplicationDbContext` (242 `DbSet<>` properties, `OnModelCreating`, all
`IEntityTypeConfiguration<>` classes under `PointOfSale.Infrastructure/Data/Configurations/`).

| Dimension | Runtime model | Snapshot |
|---|---|---|
| Entity types | **263** | **221** |
| Owned types | 7 | (subset) |
| Mapped tables | 256 | ~214 |
| Properties | 4,324 | — |
| Shadow properties | 11 | — |
| Foreign keys | 272 | — |
| Indexes | 866 (230 unique) | — |
| Keys (PK + alternate) | 270 | — |
| Check constraints | 243 | — |
| Global query filters | 113 | — |
| Concurrency tokens (incl. `xmin`) | 23 | — |
| Views / Sequences / Value converters | 0 / 0 / 0 | — |
| ProductVersion | 9.0.1 | 9.0.1 |

Notable model features: PostgreSQL `xmin` concurrency tokens (Orders, Promotions, Coupons,
SmartOffers, outbox tables, Geographic releases); heavy use of filtered unique indexes
(`"IsDeleted" = false` live-row uniqueness); composite alternate keys `(TenantId, Id)` on
`PurchaseInvoices` and `LedgerAccounts` used for tenant-safe composite FKs; large behavioral check
constraints on `OutboxMessages` (critical-event integrity).

---

# Runtime vs Snapshot

EF's own differ produced **384 operations** (full listing archived during audit; summary):

| Operation type | Count |
|---|---|
| CreateTable (entities missing from snapshot) | 42 |
| AddColumn (properties missing from snapshot) | 134 |
| CreateIndex | 149 |
| AddCheckConstraint | 31 |
| DropIndex / DropCheckConstraint / DropFK / DropColumn (snapshot-only shapes) | 19 |
| AlterColumn | 3 |
| AddForeignKey | 4 |
| AddUniqueConstraint (alternate keys) | 2 |
| **Total** | **384** |

## Entities entirely absent from the snapshot (42)

- **Geographic subsystem (26):** GeographicPlaces, GeographicPlaceNames, GeographicPlaceAliases,
  GeographicPlaceLevels, GeographicPlaceHierarchyEdges, GeographicCatalogReleases,
  GeographicCatalogReleasePlaces, GeographicCatalogReleaseEdges, GeographicCatalogPublications,
  GeographicCountryProfiles, GeographicAudit, PlaceDeprecations, PlaceMergeHistory,
  AddressAdmissionEvidence, AddressNormalizationRules, CustomerAddresses, CustomerAddressVersions,
  CarrierProviders, CarrierGeographicMappings, CarrierGeographicMappingGroups,
  CarrierGeographicMappingReleases, CarrierGeographicMappingPublications, CarrierGeographicOverrides,
  CarrierGeographicSyncRuns, CarrierGeographicSyncCandidates, and the release-edge join table.
- **Carrier administration runtime (7):** CarrierAccounts, CarrierAccountPolicies,
  CarrierAdministrationAudit, CarrierConnectionTestRecords, CarrierCredentialReferences,
  CarrierServiceLevelEnablements, CarrierWarehouseAssignments
  (source: `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`,
  configured in `ApplicationDbContext`).
- **Customs administration (4):** CustomsAdministrationAudit, CustomsEvidenceApprovals,
  CustomsEvidencePublications, CustomsPublicationLocks.
- **Affiliate identity (3):** AffiliateEmailVerificationTokens, AffiliatePasswordResetTokens,
  AffiliateRefreshTokenReplayEntries.
- **Other (2):** FawryWebhookReplayRecords, MarketplaceProjectionOutboxMessages,
  ProductRequestIdempotency. (Stripe/Paymob replay records ARE in the snapshot; Fawry is not.)

## Representative property/constraint drift (missing from snapshot)

- Orders: 40 Billing*/Shipping* geographic snapshot columns + `xmin` + unique
  `IX_Orders_TenantId_Source_SourceOrderId` (replacing `IX_Orders_SourceOrderId`).
- Shipments: 48 Origin*/Destination* geographic columns + 5 geo indexes.
- ServiceRequests: 8 snapshot columns (ProviderProfileId, Service*Snapshot…) + reshaped indexes
  (`UX_ServiceRequests_Active_Service_Buyer` filtered unique).
- UserGovernanceProfiles: 7 KYC/marketplace-approval columns +
  `CK_UserGovernanceProfiles_MarketplaceApprovalRequiresKyc`.
- ProductVariants: 6 Semantic* lifecycle columns + 2 indexes.
- AffiliatePayoutRequests: ApprovedBy, PaidBy, RejectedBy, 4 PayoutWallet*Snapshot columns.
- AffiliatePayoutTransferEvidence: VerificationFingerprint/Source/VerifiedAtUtc.
- AffiliateAccounts: ClosedAtUtc, ClosureReason + `CK_AffiliateAccounts_Status IN (0,1,2,3)`
  (snapshot has an older string-set variant — dropped/re-added by the differ).
- Tenants.Currency default changed **'USD' → 'EGP'** (AlterColumn).
- StoreConfigs.ThemePresetKey text → varchar(100); + ThemeLivePresetKey.
- CommerceHub: 13 `Currency = 'EGP'` check constraints;
  `UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live` filtered unique.
- OutboxMessages: 3 critical-event check constraints redefined (event allow-list expanded).
- Alternate keys `AK_PurchaseInvoices_TenantId_Id`, `AK_LedgerAccounts_TenantId_Id` and composite
  FKs `FK_Returns_PurchaseInvoices_TenantId_OriginalPurchaseInvoiceId`,
  `FK_TreasuryAccounts_LedgerAccounts_TenantId_AccountId` (replacing single-column FK).
- Concurrency: `xmin` on Orders, Promotions, Coupons, SmartOffers,
  PaymentCancellationOutboxMessages (note: `xmin` is a PostgreSQL system column — no DDL is needed
  at runtime, but the snapshot still must record it; severity of these specific rows is Low).

Severity of snapshot drift overall: **CRITICAL** — the snapshot no longer serves as a valid
baseline; any future `dotnet ef migrations add` would emit a giant catch-up migration mixing
already-applied and never-applied changes.

---

# Runtime vs Migration Chain

Chain inventory: **149 migration classes** (`*.cs` excluding Designer/Snapshot), **49 Designer
files** (last: 2026-05-10), **93 companion SQL files** under `Migrations/Sql/`.

Every one of the 384 differ operations was token-matched against the full chain text
(builder calls + raw SQL):

| Coverage | Ops |
|---|---|
| Object name found somewhere in chain (covered by hand-written migrations) | **320** |
| Object name found NOWHERE in chain (**never migrated by any mechanism**) | **64** |

The 320 "covered" operations exist as drift only because the snapshot lags; the objects themselves
are created by hand-written migrations, e.g.:
- Geographic tables: raw SQL (63 `IF NOT EXISTS` guards) in
  `20260705170009_InitialGeographicRuntimeFoundation.cs`.
- Order/Shipment geo columns: `20260705183000_Geo8OrderShipmentGeographicPersistence.cs`,
  `20260706120000_ShipOrigin1CanonicalShipmentOrigin.cs`.
- Fawry replay: `20260715120000_FawryContractClosure2WebhookReplay.cs`.
- Affiliate tokens: `20260603120000_AffiliateIdentityAuthorityHardening.cs`,
  `20260721121000/122000_AffiliateRefreshReplay*`.
- Customs: `20260706143000_Customs2EvidenceRuntimeFoundation.cs`,
  `20260706210000_Customs4AdministrationRuntimeWorkflow.cs`.
- MarketplaceProjectionOutbox / ProductRequestIdempotency:
  `20260516090000_StorefrontPromotionScopeReliabilityHardening.cs`.
- CommerceHub EGP constraints: `20260327_ZZX_HardenCommerceHubFinancialDashboardAggregates.sql`
  + 20260510130500 migration.

## NEVER migrated (64 operations) — the true completeness gaps

**Entities existing in source but absent from the ENTIRE chain (7 tables — Critical):**
- `CarrierAccounts`
- `CarrierAccountPolicies`
- `CarrierAdministrationAudit`
- `CarrierConnectionTestRecords`
- `CarrierCredentialReferences`
- `CarrierServiceLevelEnablements`
- `CarrierWarehouseAssignments`

Source evidence: entity classes in
`PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`; mapped and
indexed in `ApplicationDbContext`. No migration class and no SQL file anywhere in
`Migrations/` mentions the string `CarrierAccounts` (or their unique indexes such as
`IX_CarrierAccounts_TenantId_CarrierCode`). These entities are pure model-only: the chain cannot
create them.

**Property existing in source but never migrated (High):**
- `AffiliatePayoutRequests.PaidBy` (Guid?, `AffiliatePayoutRequest.cs:64`). Sibling columns
  ApprovedBy/RejectedBy appear in chain SQL; `PaidBy` appears in no migration or SQL file.

**Relationship never migrated (High):**
- `FK_CustomerAddresses_CustomerAddressVersions_CurrentVersionId` (Restrict). The
  CustomerAddresses/CustomerAddressVersions tables are created in
  `20260705183000_Geo8OrderShipmentGeographicPersistence.cs`, but this FK name appears nowhere in
  the chain.

**Indexes configured in code but never migrated (~50 — Medium, performance/uniqueness gaps):**
- `IX_StockSyncOutbox_GlobalPendingOrdering` (filtered, ordering-critical; configured in
  `StockSyncOutboxMessageConfiguration.cs:74`).
- `IX_AffiliateAttributions_Tenant_Order_Active`
  (`AffiliateAttributionConfiguration.cs:20`).
- `IX_CustomerAddresses_CurrentVersionId`, `IX_CustomerAddresses_CustomerId`,
  `IX_CustomerAddressVersions_CustomerAddressId`,
  `IX_CustomerAddressVersions_GeographicCatalogReleaseId_CountryP…`.
- 5 `IX_AddressAdmissionEvidence_*PlaceId` FK-side indexes.
- ~35 EF-convention FK indexes across the Geographic subsystem
  (`IX_GeographicPlaces_IntroducedCatalogReleaseId`, `IX_GeographicPlaceAliases_PlaceId`,
  `IX_CarrierGeographicMappings_*`, `IX_PlaceDeprecations_*`, `IX_PlaceMergeHistory_*`, …).
  The raw-SQL foundation migration created the tables and business-unique indexes but not the
  convention indexes the runtime model declares.
- Carrier* unique indexes (subsumed by the missing tables above).

**Check constraints (Medium):**
- `CK_CommerceHubDailyFinancialAggregates_BaseCurrencyCode_EGP` / `_CurrencyCode_EGP` — the family
  prefix exists in `20260327_ZZX_…sql` but these two exact constraint names could not be located
  verbatim in the chain (possible naming variance in the SQL — flagged as a gap, not certainty).

---

# Would EF Generate A Migration?

## YES — deterministically.

Evidence (both from source-only tooling, no DB):
1. `dotnet ef migrations has-pending-model-changes` (run against the design-time factory,
   build-only): *"Changes have been made to the model since the last migration. Add a new
   migration."*
2. In-process `IMigrationsModelDiffer.GetDifferences(snapshotModel, runtimeModel)` → 384 operations:
   42 CreateTable, 134 AddColumn, 149 CreateIndex, 31 AddCheckConstraint, 4 AddForeignKey,
   2 AddUniqueConstraint, 3 AlterColumn, 19 Drop* (snapshot-only shapes being replaced).

The generated migration would be a hybrid catch-up: ~83% of its operations would re-create objects
already created by hand-written migrations (guaranteed collisions if ever executed against a
current database), and ~17% would create objects that genuinely exist nowhere (Carrier* tables,
PaidBy, the missing indexes/FK).

---

# Coverage Matrix (condensed; 263 entities)

| Entity group | Snapshot | Chain | Coverage class | Risk |
|---|---|---|---|---|
| 214 legacy/core entities (Orders*, Products*, Payments, CommerceHub, Affiliate core, …) | Yes (drifted columns) | Yes | Partially represented (snapshot stale on ~30 of them) | Medium |
| Geographic subsystem (26 entities) | **No** | Yes (raw SQL 20260705170009 + follow-ons) | Partially represented (tables yes; ~40 convention indexes no) | High |
| Customs administration (4) | **No** | Yes (20260706143000/210000) | Chain-represented, snapshot-missing | Medium |
| Affiliate identity tokens (3) | **No** | Yes (20260603120000, 20260721121000/122000) | Chain-represented, snapshot-missing | Medium |
| FawryWebhookReplayRecords, MarketplaceProjectionOutboxMessages, ProductRequestIdempotency | **No** | Yes | Chain-represented, snapshot-missing | Medium |
| CustomerAddresses / CustomerAddressVersions | **No** | Tables yes; CurrentVersionId FK + 4 indexes **no** | Partially represented | High |
| **Carrier administration runtime (7 entities)** | **No** | **No** | **Not represented** | **Critical** |
| AffiliatePayoutRequests.PaidBy | No | **No** | Not represented (property) | High |
| Code-configured indexes: StockSyncOutbox ordering, AffiliateAttributions active | No | **No** | Not represented | Medium |

---

# Root Cause Analysis

1. **Process change → snapshot drift (primary).** Up to `20260510130500_…` (49 migrations),
   migrations were tool-generated (`.Designer.cs` present, snapshot advanced). Every one of the
   subsequent **100 migrations** was hand-authored (no Designer file, heavy
   `migrationBuilder.Sql` with `IF NOT EXISTS` guards). `dotnet ef migrations add` was abandoned,
   so `ApplicationDbContextModelSnapshot.cs` froze at the 2026-05-10 model while the runtime model
   gained 42 entities and hundreds of members. This is snapshot drift by workflow, not by manual
   snapshot editing (snapshot content is consistent tool output, ProductVersion 9.0.1).

2. **Hand-written SQL under-specifies the EF model (secondary).** The raw-SQL foundation
   migrations created tables and business indexes but omitted EF-convention FK indexes and at least
   one FK (`CustomerAddresses.CurrentVersionId`), because nothing forced parity — there was no
   differ in the loop.

3. **Entities added with no migration at all (tertiary — the Critical gap).** The Carrier
   administration runtime entities (`CarrierAccountRuntimeEntities.cs`) were registered in
   `ApplicationDbContext` but no corresponding migration (builder or SQL) was ever authored.
   Similarly `AffiliatePayoutRequests.PaidBy` was added to the entity while its sibling columns'
   migration was authored without it. Classic "developer forgot the migration" under a manual
   workflow with no drift gate.

4. Not observed: manual snapshot edits, deleted generated migrations (no orphan Designer files),
   merge/cherry-pick damage to the chain itself.

---

# Final Engineering Certification

## Can the current migration chain fully recreate the runtime model?

# **NO — Certification: FAIL**

- Snapshot layer: **inconsistent** with the runtime model (384 pending operations; 42 entities
  absent). The migration baseline is invalid for future tool-generated migrations.
- Chain layer: **incomplete** relative to the source. At minimum the following can never be
  produced by replaying the chain: 7 Carrier administration tables (+ their PK/unique
  indexes/audit), `AffiliatePayoutRequests.PaidBy`,
  `FK_CustomerAddresses_CustomerAddressVersions_CurrentVersionId`, and ~50 model-declared indexes
  (including the operationally significant `IX_StockSyncOutbox_GlobalPendingOrdering`).
- The three layers (source → snapshot → chain) are mutually inconsistent; the source is ahead of
  both, and the chain is ahead of the snapshot.

Per scope, no remediation is recommended here; this report certifies completeness only.

---

## Appendix — Method & Evidence Trail

- Runtime and snapshot models materialized in-process via `IDesignTimeModel`,
  `IMigrationsAssembly.ModelSnapshot`, `IModelRuntimeInitializer` (design-time, validation off),
  diffed with `IMigrationsModelDiffer` from the project's own EF Core 9.0.1 / Npgsql 9.0.2 stack.
  No database connection was opened at any point.
- `dotnet ef migrations has-pending-model-changes` executed from
  `PointOfSale.Infrastructure` (build-only; source-vs-snapshot comparison, no DB).
- Chain cross-reference: exact-token search of all 384 diff operations' object names
  (tables, columns, index/constraint/FK names) across 149 migration classes and 93
  `Migrations/Sql/*` files. Truncated differ names (`…~`) matched by prefix.
- Key files: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` (242 DbSets),
  `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (18,859 lines,
  221 entities), `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`,
  `PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs:64`,
  `Data/Configurations/StockSyncOutboxMessageConfiguration.cs:74`,
  `Data/Configurations/Affiliate/AffiliateAttributionConfiguration.cs:20`.
