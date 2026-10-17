# Pending EF migrations and Release restart readiness — 2026-10-14

**All genuinely pending migrations have been applied to the existing configured database. Final pending count: 0.** The required canonical-address column is present and matches the current EF model. The intended Release artifacts are built and identified below. The API was **not stopped, restarted, launched or redeployed**; the user owns the restart. This does not confer global shipping certification.

Evidence: `C:\Users\fadya\AppData\Local\Temp\opencode\ef-migrations-20261014\`. Recovery archives and decoded backup SQL contain sensitive database data; retain them privately and do not publish or attach them to public reports. Credentials and session material are not included here.

## Exact target and complete migration sequence

- DbContext: `PointOfSale.Infrastructure.Data.ApplicationDbContext`.
- Migration project: `PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj`.
- Startup project: `PointOfSale.API/PointOfSale.API.csproj`.
- Normal design-time mechanism: unchanged `ApplicationDbContextFactory`, run from the Infrastructure directory. It loads the API's normal Development JSON/local/user-secrets configuration and existing environment connection override policy. No connection configuration was changed or credential placed on a command line.
- Configured and independently observed server: **192.168.1.5:5432**, database **PosEcommerceDb_Baseline**, schema **public**, PostgreSQL **18.4**. Observation transactions enforced READ ONLY.
- Source/discovered migration list: **53**; database history before: **52**. All source migration files were discovered; no historical IDs were missing from source and no out-of-order pending gaps existed. EF CLI, `GetPendingMigrationsAsync()` and direct `__EFMigrationsHistory` agreed.
- Complete ordered pending sequence, and exact applied sequence:
  1. **`20261013210000_CanonicalAddressEvidenceFoundation`**.
- The earlier52 migrations were already genuinely applied, including the parcel, FedEx postal and webhook-claim migrations. They were not reapplied, skipped, rewritten or manually history-marked.

## SQL review and recovery backup

Both the normal EF CLI script and `IMigrator.GenerateScript` were generated and inspected. Their contents match after normalizing encoding/line endings; differing raw hashes reflect those serialization differences.

The pending migration executes in a transaction and adds:

- Nullable `AddressAdmissionEvidence.CanonicalAddressEvidenceJson`, PostgreSQL `text`, without a default or backfill.
- Four new PL/pgSQL validation/retention functions: `shopixy_guard_canonical_admission`, `shopixy_validate_canonical_projection`, `shopixy_guard_canonical_order`, `shopixy_guard_canonical_shipment`.
- Three new row-level guards on AddressAdmissionEvidence, Orders and Shipments.
- Its ordinary EF history entry after executing schema changes.

There are **no DROP/TRUNCATE operations, destructive column alterations, business INSERT/UPDATE/DELETE statements, financial data transformations or history-repair statements**. The trigger bodies reject invalid future changes; they do not update existing records. Existing prerequisite columns and compatible types were inspected; the new column, functions and trigger names were absent before application. No schema-drift correction was required. The migration's `Down` is deliberately unsupported; no downgrade was attempted.

The existing custom-format PostgreSQL backup workflow was reused with its installed `pg_dump`/`pg_restore` binaries. The dump and284 public-table fingerprints shared an exported REPEATABLE READ, READ ONLY snapshot. The archive listing was checked for business and migration-history data, and **every archive entry decoded successfully to offline SQL**, without executing that SQL against any database.

- Recovery archive: `C:\Users\fadya\AppData\Local\Temp\opencode\ef-migrations-20261014\PosEcommerceDb_Baseline-pre-migration.dump`.
- Size: **10,117,335 bytes**.
- SHA256: **`13A4ACD616F1A6514143EB87393AAA3428683373031F422DE7C1506EDABB5FC6`**.
- Full decoded SQL: **47,652,261 bytes**; decode completed with exit0.
- No new database, infrastructure, restore rehearsal database, restore over the current database or backup overwrite was performed. Validation establishes a complete readable recovery archive, **not a database restore rehearsal**, which the task prohibited.

## Application and final schema/history verification

The unchanged normal EF mechanism was used with no connection override and no individual migration target, so it applied the entire pending sequence:

```powershell
# Working directory: PointOfSale.Infrastructure
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project PointOfSale.Infrastructure.csproj --startup-project ../PointOfSale.API/PointOfSale.API.csproj --context ApplicationDbContext --configuration Release --no-build
```

EF acquired its migration lock, reported application of `20261013210000_CanonicalAddressEvidenceFoundation`, and completed with **exit0**. **No migration execution failed**, and no migration repair/resolution was necessary. Temporary preflight observer issues—inet address `/32` text formatting and root-relative hash paths—were corrected before migration execution; their refusals caused no schema/history changes. An artifact-reading helper was also changed to use PE metadata rather than duplicate-identity reflection loading; no application source was affected.

Final verification:

| Check | Result |
|---|---|
| `GetPendingMigrationsAsync()` and EF CLI pending count | **0** |
| Ordered database history vs complete source sequence | **53/53 exact IDs**, with all entries applied |
| Original history rows | **52/52 IDs and ProductVersion fields unchanged** |
| Required column | **text**, nullable **YES**, no default, no maximum length, not generated, not identity |
| Current EF model | **text /IsNullable=true /MaxLength=null**, matching the actual column |
| Existing admission/order/shipment column definitions | **Unchanged**, excluding the one intended addition |
| New guard triggers | **3/3 installed and enabled**, each BEFORE INSERT/DELETE/UPDATE, row-level, with expected function binding |
| Canonical evidence backfill | **None**, no populated canonical payloads introduced |

Evidence: `migration-plan.json`, `ef-pending-sequence.sql`, `sql-review.json`, `ef-database-update.log`, `ef-migrations-list-after.json`, `migration-sequence-final.json`, `verification-final.json`, `diagnose-live-data-history.json`, `live-data-diagnosis-gate.json`.

## Business and financial preservation — live-runtime exceptions disclosed

The baseline covers **284 public tables /97,206 rows**, excluding the intentionally changed EF history table. All284 table row counts remain unchanged. **282/284 tables match exact content and xmin-derived row-version fingerprints**. Orders (**20 rows**), Shipments (**12 rows**), journals, ledger lines/accounts, payment, settlement, remittance and collection records are unchanged. **39/39 explicitly selected business/financial tables match counts, content and row-version fingerprints**; the broader282-table check also covers other financial/business tables outside that named selection.

The strict all-public-table comparison correctly returned a nonzero result because the old API remained active. It was not turned into a blanket pass or rebaselined:

- `CheckoutSessions`:65 rows before/after, identical row identity set, **one** record changed only `Status` **Active(0) → Abandoned(1)** and `AbandonedAtUtc`. Other checkout customer/address/cart/payment-choice fields are unchanged.
- `UserGovernanceProfiles`:16 rows before/after, identical identity set, **one** record changed only `UpdatedAtUtc` and `LastRiskEvaluatedAtUtc`. Risk, permissions, financial locks and other profile fields are unchanged.
- No row addition/deletion occurred in either changed table. Archived COPY rows were compared field-by-field to a fresh read-only COPY export; sensitive values were not printed.
- These field changes match the unchanged existing writers at `CheckoutSessionService.cs:436–452` /`AbandonedCheckoutScannerJob.cs:64` and `UserGovernanceService.Evaluation.cs:344–368`. The migration SQL has no business DML. This supports attribution to concurrent live-runtime activity, **not an audit trace proving the exact writer process**.

Evidence: `data-before.json`, `data-after.json`, `data-preservation.json`, `business-financial-preservation.json`, `live-data-change-diagnosis.json`. No business or financial record was changed by the agent to manufacture a preservation pass, and no financial workflow was invoked.

## Release artifacts and the exact manual launch target

The intended normal Release outputs—not the earlier private shipping-test folder and not the running Debug build—were compiled:

```powershell
dotnet build PointOfSale.API/PointOfSale.API.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
```

Initial current Release compilation: **176 warnings /0 errors**, exit0. Final incremental verification: **0 warnings /0 errors**, exit0; no warning cleanup is claimed. Both API and its Infrastructure dependency were built. Metadata inspection checks the actual files without executing either assembly.

| Artifact | Exact MVID | SHA256 |
|---|---|---|
| `PointOfSale.API/bin/Release/net9.0/PointOfSale.API.dll` | **`f1bd4ae3-979f-4491-afad-c972be594560`** | `9C411CEB08BE2BD7A640B33324F5E14BB6B8F94D706343E373A529F96AFE4464` |
| `PointOfSale.API/bin/Release/net9.0/PointOfSale.Infrastructure.dll` | **`fa027fdf-454f-4c3a-9305-a62ca0711111`** | `8873AD9C8432352E3BCA8E1E214652BEE8E81D1E03E55BCDA4CD71E5ABC0995D` |
| `PointOfSale.Infrastructure/bin/Release/net9.0/PointOfSale.Infrastructure.dll` | **`fa027fdf-454f-4c3a-9305-a62ca0711111`** | `8873AD9C8432352E3BCA8E1E214652BEE8E81D1E03E55BCDA4CD71E5ABC0995D` |

The Infrastructure file in the API launch directory is byte-identical to the standalone Infrastructure Release output. Full absolute paths and sizes: `release-artifacts-final.json`.

**For the user's manual restart**, after the user stops the old instance, run from `C:\Users\fadya\source\repos\PointOfSale`:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet .\PointOfSale.API\bin\Release\net9.0\PointOfSale.API.dll --contentRoot "C:\Users\fadya\source\repos\PointOfSale\PointOfSale.API" --urls "http://localhost:5010"
```

This selects the verified Release DLL while retaining the same API project content root and normal Development configuration. Do not use default `dotnet run` without Release selection, which would select Debug. After restart, the existing startup runtime-identity log should report the two MVIDs above. **Those new modules are not yet claimed to be loaded**, because no restart/launch occurred during this task.

## Existing runtime health and preservation boundary

- Old API **PID7632**, same start time and `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe` path, remains running. It was not stopped, restarted or replaced.
- Read-only health observation after migration: liveness **HTTP200 /Healthy**; readiness **HTTP200 /Degraded**, with only disabled certificate-provider execution and disabled Caddy edge integration reported nonhealthy. This is the old Debug runtime's observation, not validation of the unlaunched Release runtime.
- **94/94 protected source/configuration/migration file hashes remain unchanged**, including all migrations, DbContext/factory, connection configuration and the deferred financial source/assertion. Full tracked-diff and staged-diff hashes are unchanged; **977/977 pre-existing git status entries remain present**, `preservation-final.json`. Only this report was added in the workspace; helper/evidence files are in the approved temporary directory. No reset, stash, clean, staging or commit occurred.
- No additional database, data reset/truncation, table drop, restore over current data, historical migration rewrite, manual migration-history insertion, shipping-provider request, carrier activation, label purchase, COD clearing, remittance, settlement or financial logic modification occurred.

**Stopped after migration, schema/data verification and Release artifact preparation. The schema blocker is resolved; manual loaded-module equivalence, post-restart runtime behavior and global shipping certification remain separate verification gates.**
