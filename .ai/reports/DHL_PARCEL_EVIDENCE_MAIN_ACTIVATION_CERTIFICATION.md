# DHL Parcel Evidence MAIN Activation Certification

## Current final verdict — 2026-10-11

**PASS — the explicitly authorized parcel migration is applied to verified MAIN, protected data matches the fresh immediate pre-migration baseline, and the user-restarted MAIN API loads the fresh normal Debug build.** This verdict covers the user's final **direct activation procedure**, which explicitly removed the backup/restore prerequisite. It is not remote DHL account certification or international-shipment authorization.

- MAIN: **`192.168.1.5:5432/PosEcommerceDb_Baseline`**, database OID `8227136`.
- Migration: **`20261011121839_ShipmentParcelRequestEvidence`**, committed at **2026-10-11T14:58:53.572837Z**. **50 applied / zero pending / no pending model changes**.
- All **44 protected business-table counts, content fingerprints and identity/`xmin` fingerprints** match the fresh immediate pre-migration baseline through final runtime verification. All eight pre-existing shipments retain NULL in all three new evidence columns.
- Final API: **PID 12660**, parent **2736**, workspace Debug executable; `Development`, `http` profile, `http://localhost:5010`, API-project working directory. Started **2026-10-11T15:23:47.8531396Z**, after the successful fresh normal Debug build.
- Fresh normal Debug API/Core/Infrastructure/Tests build: **460 warnings / 0 errors**. Final database-free selected regression run: **103 executed / 103 passed / 0 failed / 0 skipped**.
- Active API/Core/Infrastructure module paths and SHA-256 hashes match the fresh normal Debug build outputs.
- Liveness **200**; Swagger **200**, with parcel GET/PUT and quote POST contract paths present. Only GETs were sent. Aggregate health **200 / Degraded**; existing projection/certificate-provider/Caddy findings were retained.
- Read-only existing-browser session check returned **401**; authenticated same-tenant parcel GET was therefore not attempted. No login/refresh or business mutation was used to remove this limitation.
- Only the **latest** requested archive/restore pair (`...20261011145137`) was removed after exact hash/size, database/server/OID/history verification. No new backup, dump, clone, restore or test database was created after the user's direct-activation instruction.
- No real DHL request, MAIN parcel-evidence write, quote POST, shipment creation, cancellation operation, financial write, staging or commit occurred. Existing source/Git state and cancellation report/assertions remain preserved.

**Authoritative final evidence and the actual direct procedure are recorded in section 12. Sections 1–11 below preserve the earlier stopped attempt and are historical, not the current database/runtime state.** The original stopped report was also retained at `.artifacts/dhl-main-activation/continuation/previous-stopped-report.md`.

## 1. Historical first-attempt verdict and stopping point

**BLOCKED / NOT ACTIVATED — stopped before MAIN migration and runtime restart after the backup-verification harness reported a protected-content mismatch.**

Executed on 2026-10-11 in `C:\Users\fadya\source\repos\PointOfSale`, without subagents. DHL activation is the governing task; the appended cancellation closeout was explicitly excluded by the user and was not executed, repeated, or edited.

Read-only investigation established that the mismatch was a **verification-harness timezone inconsistency**, not a failed PostgreSQL restore or demonstrated business-data loss: source JSON fingerprints used `Africa/Cairo`, while restored JSON fingerprints used `UTC`. PostgreSQL timestamp-with-time-zone JSON text differs under those settings. A subsequent read-only comparison using the source timezone matched all **44** protected table content fingerprints. MAIN's protected counts, content fingerprints and identity/`xmin` fingerprints also remained identical to the backup source.

The failed backup-verification run is retained and is not relabeled as a successful end-to-end activation gate. Under the explicit instruction to stop when backup verification fails, no MAIN migration, isolated migration rehearsal, mutation-rejection test, fresh API activation build, or process restart followed that failure. No repair of MAIN or the restored business rows was attempted.

Acceptance criteria are **not met**: MAIN remains at **49 applied / 1 pending**, rather than 50/0. No fresh-build/runtime activation certification is claimed. Continuation requires a new consistently configured backup/preservation gate and the remaining authorized activation checks.

## 2. Workspace entry and preservation

| Fact | Entry and stopped closeout |
|---|---|
| Branch | `main` |
| HEAD | `256339621c0ea49f3602097cdacdf89ffe71754f` |
| Existing tracked changes | 641 paths; initial diff statistic: 3,445 insertions / 53,161 deletions |
| Existing untracked paths | 102, recorded in full in the tree artifacts |
| Staged diff | Empty |
| Tracked binary-diff SHA-256 | `92B5BB253BF40D74AE821216823BBD753F82813ABE8CB09FDA15155B5DE0C66C` |
| Empty staged-diff SHA-256 | `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855` |

Branch, HEAD, `git status --short`, tracked/staged diff statistics, relevant actual tracked diffs, and the complete untracked listing were inspected before operational changes. A subsequent full binary-diff hash and untracked-preservation comparison confirmed the existing tracked diff/index were unchanged and all original untracked paths remained present. Existing deleted tracked temporary artifacts were preserved as found.

Preserved work includes the parcel implementation and migration/Designer/snapshot; cancellation implementation, assertions and certification; tracking work; Manual COD, remittance, collection and refund lineage; frontend work; and `.artifacts/dhl-parcel` browser evidence. No staging, commit, reset, clean, stash, broad revert, artifact deletion, cancellation closeout, or cancellation-report edit occurred.

Complete evidence: `.artifacts/dhl-main-activation/preflight-tree.json`, `stop-final-tree.json`, `stop-tree-summary.json`. This report is the only newly added nonignored documentation path; operational artifacts are under the existing ignored `.artifacts` tree.

Final `git diff --check` passed. Git emitted existing LF/CRLF conversion notices, not whitespace failures. Final report status is untracked (`??`); staged diff remains empty. The preservation harness was rerun after adding the report and again confirmed the original tracked diff/index and untracked paths were preserved.

## 3. Independently verified MAIN process and database

| Identity | Verified value |
|---|---|
| Port | 5010 |
| Listener PID | **3096**, unchanged at final inspection |
| Parent PID | **3608** |
| Listener executable | `PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe` under the exact MAIN workspace |
| Parent executable/command | `C:\Program Files\dotnet\dotnet.exe`, `dotnet run` |
| Actual process working directory | `C:\Users\fadya\source\repos\PointOfSale\PointOfSale.API\` |
| Actual environment | `Development` |
| Actual launch profile | `http` |
| Actual URLs | `http://localhost:5010` |
| Effective PostgreSQL target | **`192.168.1.5:5432/PosEcommerceDb_Baseline`** |
| SQL database OID | `8227136` |
| PostgreSQL server version | `18.4 (Ubuntu 18.4-1.pgdg22.04+1)` |
| SQL transaction read-only | `on` |

Process identity was established through the live port listener, CIM executable/parent command, loaded module paths, and read-only Windows process-parameter/environment inspection. Configuration was independently resolved using the API's ordering: base settings, Development settings, optional local settings, the API's user-secrets registration, and the **actual process environment**; then the existing PostgreSQL connection-string normalization policy was used. SQL `current_database()`, `host(inet_server_addr())`, server port and database OID corroborated the configuration target. The database name alone was not used to identify MAIN.

Secret values were kept in memory. PostgreSQL native tools received credentials through their child-process environment, never command-line arguments. Process-environment evidence records key names only, not secret values; reports and tool logs contain no connection credentials or customer records. No LOAD-TEST, Dell, `192.168.1.50`, or other deployment target was accessed.

Evidence: `preflight-process.json`, `preflight-sql-identity.json`, `backup-process.json`, `diagnose-process.json`, `diagnose-sql-identity.json` under `.artifacts/dhl-main-activation/`.

## 4. Exact migration history before and after the stopped attempt

| Gate | Entry | Final read-only inspection |
|---|---|---|
| Applied count | 49 | 49 |
| Latest applied | `20261009182344_ManualCarrierCashRemittance` | Same |
| Pending database migrations | Exactly `20261011121839_ShipmentParcelRequestEvidence` | Same sole pending migration |
| Unknown applied migrations | None | None |
| Pending EF model changes | `false` | `false` |

**No pending model changes does not mean no pending database migration.** The current model matches its snapshot, while the parcel database migration remains unapplied.

All 49 recorded migration identities below have ProductVersion `9.0.1`. Initial and final history artifacts retain every identity/version pair; no history entry was inserted, deleted, or rewritten.

```text
20260408103000_AddPurchaseInvoiceSequence
20260723214642_Baseline_Recovery3
20260726153652_PosCartRuntime
20260726172030_PlatformCurrencyReferenceSeed
20260729120000_StorefrontProductUnitDbEnforcement
20260729150000_ProductAuthoringXminConcurrency
20260805215917_DOMAIN_2C_0_CreateDomainTables
20260805215918_DOMAIN_2C_DomainPersistence
20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics
20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze
20260809185514_SHOPIXY_CheckoutFieldDefinitions
20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots
20260809203807_SHOPIXY_CheckoutAttachments
20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement
20260810114400_SHOPIXY_CheckoutAttachmentFinalization
20260810181420_DOMAIN_Final_ForensicHardening
20260811150644_SHOPIXY_WarehouseResolutionAuthority
20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority
20260811215156_SHOPIXY_ShippingClosureRemediationStep2N
20260813140629_SHOPIXY_OrderNumberSequenceRemediationC52
20260818203950_FEED_1A_ProductFeedTokens
20260828105623_O47_OrderDeliveryEvidenceAuthority
20260831120000_M1A_UserGovernanceProfileOwnershipAuthority
20260831230000_M1C_MarketplaceOrderNumberSequenceAuthority
20260910003000_POS_SalesInvoiceNumberSequenceAuthority
20260910210000_POS_ReturnNumberSequenceAuthority
20260911120000_POS_SalesReturnSettlementSnapshots
20260911135713_POS_CustomerAccountPayments
20260911155307_RebuildPartyRunningBalances
20260911190000_POS_TaxAccountsAndPartyBalanceRepair
20260912120000_T01ShopixyLedgerBalanceCacheRepair
20260912154614_T06SupplierCardFundingAuthority
20260913182724_T06DInitialCapitalizationAuthority
20260914152619_T08_EcommerceCommerceHubCogsAuthority
20260916120000_POS_PurchaseReturnCashSettlement
20260917135014_PosUserManagementAuthority
20260922141030_TenantSlugCanonicalIdentity
20260926215531_StorefrontFooterCmsPageIdentity
20260927210427_BilingualCmsPageTranslations
20260929115948_MainWhatsAppSingleAccountPerTenant
20260929163000_MAIN_AbandonedCartCanceledStatusAuthority
20260929215702_MAIN_NativeAbandonedCartRecoveryScheduler
20260930145500_MAIN_RetireN8nRecoveryCallback
20261007154500_MAIN_BaileysOnlyTransport
20261007190000_MAIN_HomepageMediaAssets
20261008223819_ManualCodPermanentSettlementIdentity
20261009121429_ShipmentCancellationAuthority
20261009162710_CarrierObservedCodCollectionEvidence
20261009182344_ManualCarrierCashRemittance
```

Evidence: `preflight-migration-history.json`, `backup-migration-history.json`, `diagnose-migration-history.json`, and `diagnostic-restored-history.json`.

## 5. Reviewed migration/model/SQL and current schema

Read the existing integration certification and inspected:

- `PointOfSale.Infrastructure/Migrations/20261011121839_ShipmentParcelRequestEvidence.cs`
- Its matching Designer.
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`.
- Shipment mappings and relevant API registration/routes.
- The migration's exact immutability SQL.

Generated SQL using the freshly compiled current-source `IMigrator.GenerateScript` from `20261009182344_ManualCarrierCashRemittance` to **only** `20261011121839_ShipmentParcelRequestEvidence`.

| Reviewed SQL | Value |
|---|---|
| Path | `.artifacts/dhl-main-activation/authorized-migration.sql` |
| UTF-8 bytes | 1,142 |
| SHA-256 | `73B9618955FB8FFD205BB70D7762D5FC1E25878B254B0E267C24619697E5864E` |

The reviewed upward script is one transaction containing:

1. Nullable `Shipments.CarrierRequestFingerprint`, `character varying(64)`.
2. `shopixy_guard_parcel_request_evidence()`, language `plpgsql`, with immutable populated parcel/request comparisons and SQLSTATE `23514` rejection.
3. `TR_Shipments_ParcelRequestEvidence`, `BEFORE UPDATE`, `FOR EACH ROW` on `Shipments`.
4. Nullable `Shipments.CarrierRequestSnapshotJson`, `text`.
5. Nullable `Shipments.ParcelEvidenceJson`, `text`.
6. The exact authorized EF migration-history INSERT, version `9.0.1`, then COMMIT.

The trigger freezes an existing parcel JSON and freezes request JSON/fingerprint when an existing request snapshot is populated. Its SQL references the parcel/request columns before the later ADD COLUMN statements; the existing PL/pgSQL runtime-record design was retained, not rewritten. No business-row UPDATE, DELETE, backfill, invented measurement, destructive upward DDL, unrelated schema operation, or financial-authority change appears in the generated upward SQL. The migration's destructive Down method was reviewed but never generated/executed as an activation operation.

READ ONLY catalog inspection confirmed the three parcel columns and parcel trigger are absent on MAIN as expected for an unapplied migration. All modeled prior user-column types/nullability matched the catalog; PostgreSQL's system `xmin` columns were also included for modeled concurrency properties. No pending model changes were reported. Complete existing public column/index/constraint/trigger facts were captured in `preflight-schema-catalog.json` and `backup-schema-catalog.json`.

**Post-migration column/function/trigger attachment verification and actual isolated mutation-rejection testing were not reached.** No new MAIN trigger, constraint, index, function, or migration was installed by this task. The prior isolated certification is context, not a substitute for this task's unexecuted activation checks.

## 6. Backup archive, restore and failed verification gate

No PostgreSQL tools were found on PATH or at standard installed PostgreSQL 16/17/18 locations. Official PostgreSQL 18.4 Windows binaries were downloaded into the MAIN workspace from:

`https://get.enterprisedb.com/postgresql/postgresql-18.4-1-windows-x64-binaries.zip`

The binary archive size is 337,444,127 bytes; SHA-256 `7EFFE34C0BF89027B3F171447D351CBC460F4566C8D0F643DAEC67F140787858`. No PostgreSQL service was installed, started, stopped, or restarted.

Backup procedure used PostgreSQL `pg_dump --format=custom --no-password --snapshot=<exported-read-only-snapshot> --file=<archive>`. The protected source fingerprints and dump shared the same exported REPEATABLE READ source snapshot. `pg_restore --list` validated the custom archive listing; required Shipment and migration-history data entries were present.

| Backup/restore fact | Evidence |
|---|---|
| Archive | `.artifacts/dhl-main-activation/MAIN-dhl-before-20261011135218.dump` |
| Size | **9,500,592 bytes** |
| SHA-256 | **`5E43531A95C0C16A406BDD6F401B70973B169EEFE0F57A401E1FEE11B3C40E89`** |
| Listing entries | **2,347** |
| Isolated database | **`pointofsale_dhl_restore_20261011135218`** |
| Restore command | `pg_restore --no-password --exit-on-error --no-owner --no-privileges --dbname=<exact-isolated-database> <archive>` |
| Native restore result | Exit 0; no errors in restore log |
| Restored migration count/latest | 49 / `20261009182344_ManualCarrierCashRemittance` |
| Protected counts | All 44 equal the source |
| Original content-hash gate | **Failed: 27 populated-table hash differences** |
| Subsequent read-only diagnosis | All 44 match when both sides use `Africa/Cairo` |

The isolated database was created with a fresh exact name after verifying it did not already exist. Restore used a copy of the verified connection identity with only the database name replaced. MAIN and the isolated restore were not confused based on a naming prefix. The restored database and archive are retained; no cleanup/drop was performed.

**Exact cause:** the source backup transaction inherited `Africa/Cairo`, while the helper's restored READ ONLY transaction explicitly used `UTC`. `to_jsonb(row)` renders timestamptz fields in the session timezone, making the two textual hashes incomparable. The failure was in this task's verification harness. No evidence suggests the archive lost rows or changed actual timestamp instants: a read-only diagnostic set only the restored transaction's timezone to the source setting and matched every protected content hash. Restored `xmin` necessarily differs after logical restore and was deliberately excluded from cross-database content comparisons.

No new dump, restore retry, row rewrite, SQL repair, migration retry, or MAIN schema application was attempted after this gate failed. `backup-gate.json` was never produced as a successful gate. The isolated migration rehearsal was not run. Archive hash/size were verified again during diagnosis and remained unchanged.

Evidence: `postgres-tools.json`, `backup-archive-integrity.json`, `backup-archive.list`, `pg-dump.log`, `restore-rehearsal.log`, `blocker.json`, `stop-diagnostic.json`, and the fingerprint artifacts below.

## 7. Protected data: source, stopped MAIN and diagnostic restored comparison

Each inspection used a bounded READ ONLY transaction: REPEATABLE READ, statement timeout 60 seconds, lock timeout 5 seconds. SQL returned aggregate counts/hashes only, not business records. Content fingerprint: MD5 of the sorted concatenation of per-row MD5 of complete `to_jsonb(row)::text`, using `C` ordering. Row identity/version fingerprint: MD5 of sorted `Id:xmin` concatenation.

Shipment comparison explicitly excludes only the three authorized future columns, preserving every pre-existing column; `Id:xmin` is separately recorded. No merchant record was altered to create the baseline.

The following counts/content hashes are the backup source **and final stopped MAIN** values, using the source timezone `Africa/Cairo`. They also match the **diagnostic** restored comparison at that same timezone. Every MAIN identity/`xmin` hash matched its source value; full hashes for all tables are retained in the JSON artifacts. This is preservation through a stopped attempt, **not post-migration preservation**.

| Protected table | Rows: source / final MAIN / diagnostic restore | Content fingerprint, all three |
|---|---:|---|
| AccountingOutboxMessages | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierCodShipmentBindings | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierCollectionEvidence | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierConfigurations | 1 / 1 / 1 | `63db2c5b53952b254311301a8d7b3ea8` |
| CarrierRemittanceOrders | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierRemittances | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CommerceHubFinancialTransactions | 45 / 45 / 45 | `f565644e21f4f4d53ff2a1fd573ba6d4` |
| CommerceHubPaymentAttempts | 11 / 11 / 11 | `38a930d15d578ebae3049207125fd208` |
| CommerceHubPaymentCompensationOutboxMessages | 1 / 1 / 1 | `48bddc6c87fbab5b811d52febeacfff5` |
| CommerceHubPaymentItems | 14 / 14 / 14 | `f5ddcb1b298c5e503084318a62213516` |
| CommerceHubPaymentLogs | 16 / 16 / 16 | `f9d6ee027be0636f56ed616f20a09338` |
| CommerceHubPayments | 11 / 11 / 11 | `de8f6e12a512dffabf9df31642e32433` |
| CommerceHubPayoutItems | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CommerceHubPayouts | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CommerceHubRefundExecutions | 5 / 5 / 5 | `f25e51068e81d45015f0bd7ca3648cbd` |
| CommerceHubRefundRequests | 6 / 6 / 6 | `33c77213e100286a962af1338963678e` |
| CommerceHubRefunds | 5 / 5 / 5 | `415a795f43d67c489cb09a207ac8d200` |
| CommerceHubVendorEarnings | 11 / 11 / 11 | `257d2ea3f0ec70509a65968c4b5da590` |
| LedgerAccounts | 353 / 353 / 353 | `2bb515735025cd473d090d47e400e952` |
| LedgerJournalEntries | 190 / 190 / 190 | `1622a88c46665eeb1833267da6e6a368` |
| LedgerLines | 399 / 399 / 399 | `17d15f41e68946f2f1d98fdc1307656d` |
| MarketplaceEventOutboxDeadLetterArchives | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| MarketplaceEventOutboxMessages | 25 / 25 / 25 | `9758d94c4b03b5a90c4735aa301f91bb` |
| MarketplaceProjectionOutboxMessages | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| OrderItems | 15 / 15 / 15 | `82ceb8fb0dd8cee293babcc9391e8225` |
| OrderRefunds | 6 / 6 / 6 | `88520c8028f170852afd4b4e0b541834` |
| Orders | 15 / 15 / 15 | `b3fee90496d01f82e028026148a9be04` |
| OutboxMessage | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| OutboxMessages | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| OutboxState | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| PaymentCancellationOutboxMessages | 1 / 1 / 1 | `f78811951dbc06c6144b29b36b3396e6` |
| PaymentIntents | 7 / 7 / 7 | `21b599c40dde887268a6ca6c32f863ff` |
| PaymentSettlementBatches | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| PaymentSettlementOrders | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| PosCartOutboxMessages | 261 / 261 / 261 | `677d4c000d48abfa75023d13438e3015` |
| ProductVariants | 192 / 192 / 192 | `2477b1babaf749d2eeb6deff288679b6` |
| Products | 138 / 138 / 138 | `8c0a58e12db2059e2659c87f1f8e461d` |
| ProviderDeliveryOutbox | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| ShipmentCancellationOperations | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| Shipments | 8 / 8 / 8 | `a2645f49ec68b9359fef3eb3d55e5979` |
| ShippingWebhookLogs | 0 / 0 / 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| StockMovements | 197 / 197 / 197 | `3bfcf3be51d1bf22eae6aac0c5a92d7a` |
| StockSyncOutboxMessages | 196 / 196 / 196 | `2db39e586985d88c10dfc7fd3cbf9d41` |
| Tenants | 11 / 11 / 11 | `80d01cc65a1b45c81923cf1a7bf8c2df` |

Shipment identity/`xmin` fingerprint remained **`92c0a0706b175f265a4f0455bd77d9c7`** for all 8 rows. Cancellation-operation table remains intact and empty. All financial history listed above is preserved. No prior shipment received invented evidence; no parcel columns were added on MAIN.

Evidence:

- `backup-source-fingerprints.json`: source snapshot shared with pg_dump.
- `restored-fingerprints.json`: original failed, UTC-based comparison; intentionally retained.
- `diagnostic-same-timezone-restored-fingerprints.json`: diagnostic source-timezone comparison.
- `stop-final-main-fingerprints.json`: source-timezone MAIN content/count/version preservation.

## 8. Builds, process activation and loaded modules

Only the operational harness and its Core/Infrastructure dependency chain were built into isolated paths. Running MAIN Debug outputs were not replaced.

```powershell
dotnet build ".artifacts/dhl-main-activation/harness/Activation.csproj" --artifacts-path ".artifacts/dhl-main-activation/build" --no-incremental -v minimal "-clp:ErrorsOnly;Summary"
```

Initial fresh build: **111 warnings, 0 errors**, 16.09 seconds. Harness build output: `.artifacts/dhl-main-activation/build/bin/Activation/debug/Activation.dll`. Subsequent harness-only edits used the same isolated artifacts path with `--no-restore`; builds passed with **0 warnings / 0 errors**. Latest incremental harness build: 2.23 seconds. No warnings were suppressed.

Harness modes executed: `preflight 3096`, `tools`, `backup 3096`, `diagnose 3096`, and `closeout`. `backup` stopped at its content-comparison gate before its isolated migration code. No MAIN database-update mode was implemented or executed.

Earlier harness-only findings were corrected before the successful read-only preflight: PostgreSQL `inet::text` includes `/32`, so address identity uses `host(inet_server_addr())`; modeled `xmin` is a system column and must be included in catalog lookup. One preflight command timed out while Git stderr/stdout were read serially; concurrent bounded stream handling corrected it. Those attempts made no database changes. The later backup timezone failure was not converted into a migration/activation retry.

**Affected API/Core/Infrastructure/Tests activation build was not reached.** No new backend/frontend regression execution or Chromium write workflow was performed in this stopped activation attempt, and earlier certifications are not counted as fresh results.

PID **3096** was preserved. Loaded module inspection confirmed the live API/Core/Infrastructure DLL paths under `PointOfSale.API/bin/Debug/net9.0`. Their on-disk hashes at stopped closeout are:

| Existing runtime DLL | SHA-256 |
|---|---|
| PointOfSale.API.dll | `7BEAC77A729CBABAC9C3B7C6A000F2D33B3258F1B850F5614C0400409724B345` |
| PointOfSale.Infrastructure.dll | `449C2439AAB63708EF4C1C64AA5DBEF1C200157A001B40C101316ABC4B5F263D` |
| PointOfSale.Core.dll | `B91965C7454911F342274091DB4F4F27FB1F51D26EFD3E9847B4DE662CC97764` |

These identify the preserved runtime files, not newly activated build outputs. No process was stopped/restarted. PostgreSQL, Redis, MongoDB, Caddy and shared services were not restarted or reconfigured.

## 9. Safe read-only endpoint observations and limitations

| Request | Actual result |
|---|---|
| `GET http://localhost:5010/health/live` | **200** |
| `GET http://localhost:5010/swagger/v1/swagger.json` | **200**; parcel-evidence and parcel-quote route paths present |

The user-started API already exposes `/api/admin/shipments/{shipmentId}/parcel-evidence` and `/api/admin/shipments/{shipmentId}/parcel-quote` in Swagger. This observation corrects any assumption that those routes must still be absent from the current process; route declaration is not proof of applied schema or successful evidence reads.

No authenticated parcel GET was attempted after the backup gate failed with MAIN's required parcel schema still absent. No PUT, quote POST, create-shipment request, cancellation POST, provider request, or other MAIN business mutation was sent. Post-activation aggregate health and authenticated contract verification were not reached. Redis/projection/disabled certificate-provider/Caddy checks were neither suppressed nor changed; this report makes no aggregate-health-green claim.

Evidence: `stop-read-only-runtime-checks.json`, `stop-runtime-dll-hashes.json`, and live process identity artifacts.

## 10. Provider and authority containment

The task made **zero DHL sandbox/production calls** and **zero MAIN business-write operations**. The only database writes were creation of the exact isolated restore database and native logical restoration into it. MAIN inspection remained read-only; even the authorized MAIN schema migration was not executed.

Source inspection confirmed existing `DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED` and `DHL_CUSTOMS_VALUE_SEMANTICS_UNPROVEN` guards. No application source was edited. DHL COD, collection, automatic remittance, pickup, shipment cancellation, insurance and dangerous-goods restrictions remain as found. Remote account certification and international shipment execution remain blocked.

Registration/recovery/cancellation coordination, tenant isolation, payment/ledger/refund authorities, Manual COD/refund lineage, existing carrier-remittance controls and generic Clearing rejection were not altered. Existing Redis/projection/certificate/Caddy health behavior was preserved. No unsupported provider capability, new carrier, fabricated PASS marker, cancellation assertion edit, or cancellation closeout was introduced.

## 11. File-by-file intentional changes and remaining blocker

| File or artifact | Purpose |
|---|---|
| `.ai/reports/DHL_PARCEL_EVIDENCE_MAIN_ACTIVATION_CERTIFICATION.md` | New stopped-state activation certification; no PASS claim. |
| `.artifacts/dhl-main-activation/harness/Activation.csproj` | Isolated operational executable referencing current Infrastructure. |
| `.artifacts/dhl-main-activation/harness/Program.cs` | Verified process/configuration/SQL identity; read-only migration/model/catalog checks; SQL generation; diagnostic/preservation closeout. |
| `.artifacts/dhl-main-activation/harness/ProcessIdentity.cs` | Read-only Windows process working-directory/environment inspection. |
| `.artifacts/dhl-main-activation/harness/Operations.cs` | Official tool acquisition, bounded aggregate fingerprints, exported-snapshot backup and exact isolated restore procedure. |
| `.artifacts/dhl-main-activation/build/**` | Isolated build/restore outputs; not active MAIN binaries. |
| `.artifacts/dhl-main-activation/tools/**` | Official binary ZIP and extracted tools; no service installation. |
| `.artifacts/dhl-main-activation/authorized-migration.sql`, `preflight-sql.json` | Exact authorized reviewed upward SQL and hash. |
| `.artifacts/dhl-main-activation/MAIN-dhl-before-20261011135218.dump` | Fresh custom-format MAIN backup, retained. |
| `.artifacts/dhl-main-activation/backup-archive.list`, integrity/tool/log JSON/log files | Archive integrity, native dump/restore evidence. |
| `.artifacts/dhl-main-activation/*-process.json`, `*-sql-identity.json`, `*-migration-history.json`, `*-schema-catalog.json`, `preflight-column-drift.json` | Sanitized process/target/history/model/catalog evidence. |
| `.artifacts/dhl-main-activation/*fingerprints.json`, `stop-diagnostic.json`, `blocker.json` | Original failed comparison and read-only diagnosis/preservation evidence. |
| `.artifacts/dhl-main-activation/*tree*.json`, runtime check/hash JSON files | Working-tree preservation and preserved runtime evidence. |

No product, existing migration, model snapshot, test assertion, frontend, provider configuration, runtime configuration or existing certification report was edited.

**Remaining blocker:** the end-to-end backup verification run failed because the operational harness did not use identical timezone settings for source/restored fingerprints. Read-only diagnosis establishes content equivalence, but the original failed gate remains recorded. A future continuation must standardize source/restore fingerprint settings consistently, establish a fresh verified backup/rehearsal and immediate pre-migration preservation baseline, then complete exactly-one-migration application, schema/immutability/data checks, fresh API/Core/Infrastructure/Tests build and verified MAIN loaded-runtime activation.

**Historical first-attempt final state:** MAIN schema/runtime preserved; 49 applied migrations, sole pending `20261011121839_ShipmentParcelRequestEvidence`; process PID 3096; 44 protected MAIN table count/content/version fingerprints unchanged; no DHL call or MAIN business-write operation; no staging/commit.

## 12. Completed direct activation — authoritative final evidence

### 12.1 User-directed procedure change and exact removal

The user first authorized a timezone-corrected backup continuation. Both source and restored read-only sessions were changed to explicit `Africa/Cairo`, with a guard rejecting any other fingerprint-session timezone. Original failed comparison artifacts were retained. That continuation created archive `MAIN-dhl-before-20261011145137.dump` and database `pointofsale_dhl_restore_20261011145137`; restore returned exit 0 and its protected-data comparison matched, but a separate schema-catalog comparison stopped with **32 changed/removed and 32 added facts** before any migration rehearsal. The cause of that catalog difference was not investigated further after the user changed procedure. Neither that run nor the original timezone-failed run is claimed as a successful backup/rehearsal gate.

The user then explicitly instructed direct activation on existing MAIN, with **no backup prerequisite and no additional backup/database copy**, and requested removal of only the latest pair. No prior safety gate was silently bypassed: this amended execution scope is the basis of the final verdict.

Exact removal evidence:

| Identity | Verified value before removal |
|---|---|
| Archive | `.artifacts/dhl-main-activation/continuation/MAIN-dhl-before-20261011145137.dump` |
| Size | `9,566,167` bytes |
| SHA-256 | `98EEC7BD7936063897400BF811A91A80D6A33EED43556FCA91DF16B386B17A2D` |
| Database | `pointofsale_dhl_restore_20261011145137` |
| Actual server | `192.168.1.5:5432` |
| Restore database OID | `34208239`, distinct from live MAIN OID `8227136` |
| Restore history | 49, latest `20261009182344_ManualCarrierCashRemittance`; parcel columns absent |
| Active sessions immediately before DROP | Zero |
| Removal result | Exact database and archive removed at `2026-10-11T14:58:23.5483837Z` |

DROP did not use FORCE or terminate a backend. Archive identity was rehashed before its deletion. All other reports, logs, listings and artifacts were retained, including the original first-attempt archive/database pair from `20261011135218`; there was no broad cleanup.

Evidence under `.artifacts/dhl-main-activation/direct/`: `requested-removal-identity.json`, `requested-removal-result.json`. Original failed evidence remains in the parent directory, and the intermediate continuation's logs/catalogs remain in `continuation/`.

### 12.2 Migration admission, application and post-state

Live process/configuration/SQL identity and the precise migration/model state were rechecked at mutation admission. Before application there were **49 applied migrations**, latest `20261009182344_ManualCarrierCashRemittance`, and exactly one pending migration: `20261011121839_ShipmentParcelRequestEvidence`. No unknown applied migration or pending model change was present.

The reviewed upward SQL is unchanged: **1,142 UTF-8 bytes**, SHA-256 **`73B9618955FB8FFD205BB70D7762D5FC1E25878B254B0E267C24619697E5864E`**. It was regenerated from current source using the explicit previous/target migration bounds and required to match that exact reviewed hash. No general `database update to latest` operation was used.

After requested removal, a **fresh immediate pre-migration READ ONLY baseline** captured all 44 protected table counts, full pre-existing row content and `Id:xmin` signatures, plus complete public columns/defaults/indexes/constraints/triggers/functions. Sessions explicitly used `Africa/Cairo`; inspections had bounded statement and lock timeouts.

The controlled one-migration transaction then:

1. Locked migration history and protected tables, including an exclusive Shipment schema lock, with finite lock/statement deadlines.
2. Reverified exact MAIN identity, OID, 49/one pending list, fresh baseline signatures and unchanged catalog before executing DDL.
3. Executed **only the reviewed migration body** inside the controlled transaction, retaining its authorized three ADD COLUMNs, function, trigger and single EF history INSERT.
4. Verified 50/zero pending, expected new schema and unchanged protected content/count/version signatures **before COMMIT**.
5. Committed once. No partial-migration repair, history deletion or migration retry occurred.

Independent READ ONLY inspections after commit, before activation, after user restart and at final closeout confirmed:

| Gate | Before | Final |
|---|---|---|
| Applied migrations | 49 | **50** |
| Latest applied | `20261009182344_ManualCarrierCashRemittance` | **`20261011121839_ShipmentParcelRequestEvidence`** |
| Pending database migrations | Exact authorized one | **Zero** |
| Pending EF model changes | `false` | **`false`** |
| Unknown migration history | None | None |

The original 49 identities listed in historical section 4 remain present and unchanged. The only additional history entry is the authorized parcel migration, ProductVersion `9.0.1`. Complete before/after identity/version lists are retained in `immediate-before-migration-history.json`, `after-migration-history.json`, and `direct-final-migration-history.json`.

### 12.3 Actual schema, trigger and immutable-evidence boundary

| MAIN Shipment column | Actual type | Nullable | Default | Populated historical rows |
|---|---|---|---|---:|
| `CarrierRequestFingerprint` | `character varying(64)` | YES | None | 0 |
| `CarrierRequestSnapshotJson` | `text` | YES | None | 0 |
| `ParcelEvidenceJson` | `text` | YES | None | 0 |

`TR_Shipments_ParcelRequestEvidence` is enabled (`tgenabled = O`) and attached **BEFORE UPDATE / FOR EACH ROW** to `public."Shipments"`. It invokes `shopixy_guard_parcel_request_evidence()`, language `plpgsql`. Actual catalog source matches the reviewed write-once comparisons for populated parcel JSON and populated request JSON/fingerprint, with exception SQLSTATE **23514**. The exact function body and attachment are recorded in `main-parcel-schema.json`.

Complete pre/post public catalog comparison found exactly **three additional columns, one additional trigger and one additional function**, with no removed/changed existing column/default/index/constraint/trigger/function. No unrelated index or constraint was added. All eight prior shipments have NULL in every new column; their pre-existing content and identity/version evidence remain identical.

No mutation-rejection write was sent to MAIN. The final direct procedure created no test database and did not repeat isolated PostgreSQL mutation tests; actual isolated rejection evidence remains in the previously completed parcel integration certification. This activation independently verifies the installed function/trigger definition and attachment, without claiming a fresh SQL write test.

Evidence: `immediate-pre-main-schema.json`, `post-main-schema.json`, `direct-final-schema.json`, `main-parcel-schema.json`, `migration-result.json`.

### 12.4 Fresh immediate baseline versus final protected data

**All 44 rows below have equal before/after table counts, content MD5 and `Id:xmin` MD5.** Shipment content comparison excludes only the three newly added columns; all pre-existing Shipment values and identities are included. The source and final sessions use explicit `Africa/Cairo` and deterministic `C` ordering. No customer records are printed.

The older first-attempt baseline is **not** substituted for this fresh baseline. Before direct activation, prior activity had changed Product/ProductVariant fingerprints and increased StockMovements, StockSyncOutboxMessages and MarketplaceEventOutboxMessages counts relative to that older snapshot. This task issued no operation to create those earlier changes. The fresh direct-activation baseline captures them as existing data, and every signature below remained unchanged from immediate pre-migration through final activated-runtime inspection.

| Protected table | Immediate before → final count | Matching full-content fingerprint |
|---|---:|---|
| AccountingOutboxMessages | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierCodShipmentBindings | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierCollectionEvidence | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierConfigurations | 1 → 1 | `63db2c5b53952b254311301a8d7b3ea8` |
| CarrierRemittanceOrders | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CarrierRemittances | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CommerceHubFinancialTransactions | 45 → 45 | `f565644e21f4f4d53ff2a1fd573ba6d4` |
| CommerceHubPaymentAttempts | 11 → 11 | `38a930d15d578ebae3049207125fd208` |
| CommerceHubPaymentCompensationOutboxMessages | 1 → 1 | `48bddc6c87fbab5b811d52febeacfff5` |
| CommerceHubPaymentItems | 14 → 14 | `f5ddcb1b298c5e503084318a62213516` |
| CommerceHubPaymentLogs | 16 → 16 | `f9d6ee027be0636f56ed616f20a09338` |
| CommerceHubPayments | 11 → 11 | `de8f6e12a512dffabf9df31642e32433` |
| CommerceHubPayoutItems | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CommerceHubPayouts | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| CommerceHubRefundExecutions | 5 → 5 | `f25e51068e81d45015f0bd7ca3648cbd` |
| CommerceHubRefundRequests | 6 → 6 | `33c77213e100286a962af1338963678e` |
| CommerceHubRefunds | 5 → 5 | `415a795f43d67c489cb09a207ac8d200` |
| CommerceHubVendorEarnings | 11 → 11 | `257d2ea3f0ec70509a65968c4b5da590` |
| LedgerAccounts | 353 → 353 | `2bb515735025cd473d090d47e400e952` |
| LedgerJournalEntries | 190 → 190 | `1622a88c46665eeb1833267da6e6a368` |
| LedgerLines | 399 → 399 | `17d15f41e68946f2f1d98fdc1307656d` |
| MarketplaceEventOutboxDeadLetterArchives | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| MarketplaceEventOutboxMessages | 26 → 26 | `9352fc895942c7c2b8bfb437c3f73e18` |
| MarketplaceProjectionOutboxMessages | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| OrderItems | 15 → 15 | `82ceb8fb0dd8cee293babcc9391e8225` |
| OrderRefunds | 6 → 6 | `88520c8028f170852afd4b4e0b541834` |
| Orders | 15 → 15 | `b3fee90496d01f82e028026148a9be04` |
| OutboxMessage | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| OutboxMessages | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| OutboxState | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| PaymentCancellationOutboxMessages | 1 → 1 | `f78811951dbc06c6144b29b36b3396e6` |
| PaymentIntents | 7 → 7 | `21b599c40dde887268a6ca6c32f863ff` |
| PaymentSettlementBatches | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| PaymentSettlementOrders | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| PosCartOutboxMessages | 261 → 261 | `677d4c000d48abfa75023d13438e3015` |
| ProductVariants | 192 → 192 | `1e3ef396ba936035cec51bfe47af65fe` |
| Products | 138 → 138 | `eec45b772365053578707dbeb9e6f060` |
| ProviderDeliveryOutbox | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| ShipmentCancellationOperations | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| Shipments | 8 → 8 | `a2645f49ec68b9359fef3eb3d55e5979` |
| ShippingWebhookLogs | 0 → 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| StockMovements | 198 → 198 | `67fe86ce6771f076a3a392f78218f7ad` |
| StockSyncOutboxMessages | 197 → 197 | `240ae390320235626d8bd4bd3dd4a647` |
| Tenants | 11 → 11 | `80d01cc65a1b45c81923cf1a7bf8c2df` |

Shipment `Id:xmin` fingerprint: **`92c0a0706b175f265a4f0455bd77d9c7`**, unchanged. Complete version fingerprints for all tables are retained in `immediate-pre-main-fingerprints.json`, `post-main-fingerprints.json`, `direct-final-fingerprints.json`, and `preservation-summary.json`.

### 12.5 Fresh builds, regression results and runtime provenance

The initial fresh application build used isolated artifacts while PID 3096 was running:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" --artifacts-path ".artifacts/dhl-main-activation/direct/application" --no-incremental -v minimal "-clp:ErrorsOnly;Summary"
```

Result: **460 warnings / 0 errors**, console time **26.88 seconds**. API/Core/Infrastructure/Tests built from current source. Actual test discovery used this fresh Test DLL with the filter `FullyQualifiedName~ParcelMeasurementTests|FullyQualifiedName~DhlOfficialContractCompatibilityTests`. Its 103-case mock/unit run passed; it created no database and used HTTP recording handlers rather than DHL endpoints.

A controlled activation invocation for verified PID 3096 did not produce a completed activation certificate before the user restarted the API. The user-started PID 7832, and then PID 2700 after an isolated-output promotion, had Debug hashes that did not match the isolated-build manifest. Each failed verification stopped further automated runtime mutation. Normal `dotnet run` startup repopulated normal Debug output rather than retaining the isolated output manifest. No success claim relies on those attempts; their mismatch/promotion/preactivation evidence is retained.

After coordinating with the user to stop **only MAIN**, and independently verifying that port 5010 had no listener, the normal MAIN Debug dependency chain was rebuilt with:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" --no-incremental -v minimal "-clp:ErrorsOnly;Summary"
```

**Succeeded: 460 warnings / 0 errors, console time 55.02 seconds**, completed at **2026-10-11T15:22:54.246024Z**. This rebuilt API, Core, Infrastructure and Tests in their normal `bin/Debug/net9.0` directories, including normal intermediate outputs, so the established startup procedure uses the recorded fresh build. Warning counts are retained; none were suppressed. No product source fix or regression assertion modification was required.

Final execution from those freshly rebuilt normal Debug outputs:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --no-build --no-restore --filter "FullyQualifiedName~ParcelMeasurementTests|FullyQualifiedName~DhlOfficialContractCompatibilityTests" --logger "trx;LogFileName=parcel-and-dhl-main-debug.trx" --results-directory ".artifacts/dhl-main-activation/direct/results" -v minimal
```

Inspected final TRX: **Completed; total 103 / executed 103 / passed 103 / failed 0**, with error/timeout/aborted/notExecuted/pending counters all zero. No skips. The final test DLL is `PointOfSale.Tests/bin/Debug/net9.0/PointOfSale.Tests.dll`, SHA-256 **`95140A6B613DA2D72D94177A5206EEE9ACCB6DADD78B29D26E5F6712E3BD0F14`**. This is the same 103 selected cases rerun against the normal Debug output, **not 206 distinct cases**.

The user restarted MAIN after that build. Final process and loaded-module inspection:

| Runtime fact | Actual final value |
|---|---|
| Port owner | **12660** |
| Parent launcher | **2736**, `C:\Program Files\dotnet\dotnet.exe`, `dotnet run` |
| Executable | `C:\Users\fadya\source\repos\PointOfSale\PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe` |
| Working directory | `C:\Users\fadya\source\repos\PointOfSale\PointOfSale.API\` |
| Environment / profile | **Development / http** |
| URLs | `http://localhost:5010` |
| Actual startup UTC | **2026-10-11T15:23:47.8531396Z**, after the fresh normal build |
| Effective DB configuration + independent SQL | `192.168.1.5:5432/PosEcommerceDb_Baseline`, OID `8227136` |

| Loaded application DLL under API `bin/Debug/net9.0` | Loaded-path file SHA-256 = fresh normal build SHA-256 |
|---|---|
| PointOfSale.API.dll | `7BEAC77A729CBABAC9C3B7C6A000F2D33B3258F1B850F5614C0400409724B345` |
| PointOfSale.Infrastructure.dll | `449C2439AAB63708EF4C1C64AA5DBEF1C200157A001B40C101316ABC4B5F263D` |
| PointOfSale.Core.dll | `B91965C7454911F342274091DB4F4F27FB1F51D26EFD3E9847B4DE662CC97764` |

The normal build is deterministic and reproduces the entry Debug hashes; this does not imply stale binaries were used as fresh-build evidence. The fresh `--no-incremental` build log, normal-output manifest, matching dependency copies, subsequent process startup time, and loaded module paths/hashes establish provenance. Isolated-build hashes differ and are not substituted for the normal Debug manifest.

Only confirmed MAIN API lifecycle was coordinated. No unrelated process or shared PostgreSQL/Redis/MongoDB/Caddy service was stopped or restarted. Existing runtime settings/profile and source configuration were preserved.

Evidence: `application-build.json/.log`, `main-debug-build.json/.log`, both TRX files, `immediate-preactivation-process.json`, `user-restarted-runtime.json`, `user-coordinated-promotion.json`, `verified-active-runtime.json`, `direct-final-process.json`, `direct-final-sql-identity.json`.

### 12.6 Actual read-only contract/health checks and limitations

| Actual request | Result |
|---|---|
| `GET /health/live` | **200** |
| `GET /swagger/v1/swagger.json` | **200**; `/api/admin/shipments/{shipmentId}/parcel-evidence` exposes GET/PUT; `/parcel-quote` exposes POST |
| `GET /health` | **200 / Degraded** |
| `GET /api/auth/session`, existing isolated browser cookie session | **401**; no login/refresh mutation attempted |

The final aggregate sample has PostgreSQL, CommerceHubPlatform, financial readiness, Redis/Redis latency, outboxes and other dependencies Healthy. Remaining Degraded entries are **`projection_drift`**, **`certificate_provider`**, **`caddy_edge`**. Existing findings and disabled execution behavior were not suppressed or altered; no uniformly-green health claim is made.

`playwright-cli list` confirmed the existing headed in-memory `opencode-ui-test` session, which was reused. The read-only script used Playwright's direct APIRequestContext GET, bypassing the page's earlier synthetic interception. Because its existing session was unauthenticated, it did not select a tenant/shipment or call the parcel-evidence GET. Authenticated same-tenant route execution therefore remains a documented verification limitation; Swagger declaration and fresh loaded-runtime activation are proven. No credentials were printed, no login/refresh POST was performed, and no evidence PUT/quote POST/provider request occurred.

Final READ ONLY SQL after these checks again passed 50/0/no-model-change, exact schema/catalog and all 44 protected count/content/version comparisons. Evidence: `fresh-runtime-read-only-checks.json`, `authenticated-read.js`, `authenticated-read-result.json`, `direct-final-migration-history.json`, `direct-final-fingerprints.json`.

### 12.7 Final source/Git/artifact inventory and containment

The entry branch/HEAD and original tracked binary-diff hash remain as recorded in historical section 2. Existing untracked files remain present, index empty; only this report and operational artifacts are intentional changes. No application, migration, model snapshot, test assertion, frontend, carrier/runtime configuration, or cancellation certification source was edited.

Final post-report preservation check confirmed the original tracked binary-diff SHA-256 `92B5BB253BF40D74AE821216823BBD753F82813ABE8CB09FDA15155B5DE0C66C`, empty staged diff, and all original untracked paths retained. `git diff --check` passed with existing LF/CRLF notices only. Final branch is `main`, HEAD remains `256339621c0ea49f3602097cdacdf89ffe71754f`; this report is untracked, not staged. Evidence: `direct/stop-final-tree.json` and `direct/stop-tree-summary.json` (historical helper filenames reused for the final preservation check).

| Intentional file/path | Final change |
|---|---|
| `.ai/reports/DHL_PARCEL_EVIDENCE_MAIN_ACTIVATION_CERTIFICATION.md` | Current final verdict and direct activation evidence added; earlier failures retained as history. |
| `.artifacts/dhl-main-activation/harness/Operations.cs` | Explicit same-timezone fingerprint guard and catalog/preservation helpers; earlier continuation artifacts retained. |
| `.artifacts/dhl-main-activation/harness/Program.cs` | Exact direct target/history/model controls, before/after verification, preserved source/Git evidence. |
| `.artifacts/dhl-main-activation/harness/DirectActivation.cs` | Exact requested-pair verification/removal and atomic reviewed one-migration harness. |
| `.artifacts/dhl-main-activation/harness/RuntimeActivation.cs` | Fresh isolated/normal builds, finite process/port/module verification and coordinated MAIN activation evidence. |
| `.artifacts/dhl-main-activation/harness/ProcessIdentity.cs` | Existing read-only process environment/directory helper retained. |
| `.artifacts/dhl-main-activation/continuation/**` | Intermediate continuation evidence and archived stopped report preserved; only its exact requested dump removed. |
| `.artifacts/dhl-main-activation/direct/**` | Direct SQL/baseline/catalog/removal/build/test/runtime/health and sanitized auth-read evidence. |
| `PointOfSale.{API,Core,Infrastructure,Tests}/bin/Debug/net9.0` and matching `obj` | Fresh normal Debug build outputs; no source/Git authority change. |

**Zero real DHL calls and zero MAIN business-write requests were made by this task.** MAIN writes were confined to the authorized schema/function/trigger and EF history insertion. Exact removal touched only the user-authorized latest isolated database/archive. No financial, parcel, quote, shipment-creation, cancellation, merchant-product or tenant mutation was issued as a smoke test.

`DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED` and `DHL_CUSTOMS_VALUE_SEMANTICS_UNPROVEN` remain in unchanged source. Unsupported COD, collection, automatic remittance, pickup, cancellation, insurance and dangerous-goods capabilities remain blocked. Registration/recovery/cancellation coordination, tenant isolation, Manual COD/refund lineage and all payment/ledger/refund authorities are unchanged. The cancellation closeout was not executed, assertions/report were not edited, and no financial containment barrier was opened.

**Final acceptance:** authorized migration PASS; installed schema/model/catalog PASS; immediate-baseline protected-data preservation PASS; fresh current-source normal build PASS; active MAIN loaded-build identity PASS; safe liveness/Swagger PASS. Remaining limitations are expired browser authentication for the optional same-tenant parcel GET, the preserved aggregate Degraded checks, and continuing remote/international/unsupported DHL certification barriers. No remaining local schema/runtime activation blocker is present.
