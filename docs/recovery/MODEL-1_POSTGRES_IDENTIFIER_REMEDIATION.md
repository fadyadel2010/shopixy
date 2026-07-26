# MODEL-1 PostgreSQL Identifier Remediation

Date: 2026-07-24

## Executive Summary

The RECOVERY-5 scratch database failure is caused by Runtime Model index names that are longer than PostgreSQL's 63-byte identifier limit and collide after PostgreSQL truncation.

The conflicting names do not originate from EF Core default index-name conventions. They are explicitly assigned in `CommerceHubFinancialTransactionConfiguration` through `HasDatabaseName(...)`.

Final certification: READY FOR IMPLEMENTATION

## Root Cause

PostgreSQL stores identifiers with a maximum length of 63 bytes. Both generated index names share the same first 63 characters:

```text
IX_CommerceHubFinancialTransactions_EntityType_EntityId_Created
```

The Runtime Model explicitly sets two index names longer than that limit:

```text
IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedAtUtc
IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedBy_CreatedAtUtc
```

When EF Core emits `CREATE INDEX`, PostgreSQL truncates both names to the same effective identifier and rejects the second one with `42P07`.

This is a Runtime Model naming issue.

## Source Location

Entity:

```text
PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs
```

Relevant properties:

- Line 12: `EntityType`
- Line 13: `EntityId`
- Line 25: `CreatedAtUtc`
- Line 26: `CreatedBy`

Runtime configuration:

```text
PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs
```

The configuration is loaded by:

```text
PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:481
modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
```

Conflicting index source:

```text
PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs:17-18
builder.HasIndex(entry => new { entry.EntityType, entry.EntityId, entry.CreatedAtUtc })
    .HasDatabaseName("IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedAtUtc");
```

```text
PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs:42-44
builder.HasIndex(entry => new { entry.EntityType, entry.EntityId, entry.CreatedBy, entry.CreatedAtUtc })
    .HasFilter("\"EntityType\" = 1 AND \"EntityId\" IS NOT NULL AND \"CreatedBy\" IS NOT NULL")
    .HasDatabaseName("IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedBy_CreatedAtUtc");
```

No shared index naming helper or model-finalizing naming convention was found in the inspected Runtime Model path. Current naming is mostly explicit per configuration.

## Conflicting Index Analysis

Index 1:

- Entity: `CommerceHubFinancialTransaction`
- Table: `CommerceHubFinancialTransactions`
- Properties: `EntityType`, `EntityId`, `CreatedAtUtc`
- Filter: none
- Generated name: `IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedAtUtc`
- Generated name length: 68 characters
- PostgreSQL effective name: `IX_CommerceHubFinancialTransactions_EntityType_EntityId_Created`

Index 2:

- Entity: `CommerceHubFinancialTransaction`
- Table: `CommerceHubFinancialTransactions`
- Properties: `EntityType`, `EntityId`, `CreatedBy`, `CreatedAtUtc`
- Filter: `"EntityType" = 1 AND "EntityId" IS NOT NULL AND "CreatedBy" IS NOT NULL`
- Generated name: `IX_CommerceHubFinancialTransactions_EntityType_EntityId_CreatedBy_CreatedAtUtc`
- Generated name length: 78 characters
- PostgreSQL effective name: `IX_CommerceHubFinancialTransactions_EntityType_EntityId_Created`

Collision explanation:

- The first 63 characters are identical.
- The distinguishing suffixes begin after PostgreSQL's identifier limit.
- PostgreSQL sees both as the same relation name.
- EF Core correctly emits the configured names; PostgreSQL rejects the duplicate effective name.

A scan of the generated baseline's `CreateIndex` names found this as the only duplicate PostgreSQL-effective index identifier among 866 generated `CreateIndex` operations.

## Candidate Fixes

### Candidate 1: Rename Only The Two Conflicting Indexes

Example names:

```text
IX_CHFinTx_EntityType_EntityId_CreatedAtUtc
IX_CHFinTx_EntityType_EntityId_CreatedBy_CreatedAtUtc
```

Pros:

- Smallest code change.
- Preserves index definitions and query behavior.
- Deterministic and readable.
- PostgreSQL-safe for the known failure.

Cons:

- Does not prevent future long-name collisions elsewhere.
- Depends on manual discipline for every future `HasDatabaseName`.

### Candidate 2: Remove Explicit HasDatabaseName Calls

Pros:

- Lets EF Core conventions own the names.

Cons:

- Does not fix this collision. EF Core's default convention would still use table and property names and produce materially the same long identifiers.
- Loses intentional naming without adding PostgreSQL safety.

### Candidate 3: Local Abbreviation Policy For CommerceHubFinancialTransaction

Rename all long `CommerceHubFinancialTransactions` indexes in this entity configuration to use a stable short table prefix, for example `CHFinTx`.

Pros:

- Keeps the remediation localized to the failing aggregate.
- Reduces future collision risk inside the same high-risk entity.
- Names remain readable.

Cons:

- Still does not protect other entities.
- Requires manual review for future long names outside this configuration.

### Candidate 4: Runtime Model PostgreSQL-Safe Identifier Helper

Add a shared Runtime Model helper that assigns or validates relational names for indexes and constraints before migrations are generated. The helper should:

- Preserve configured names when they are already <= 63 bytes and collision-free.
- For names over 63 bytes, produce a deterministic shortened name with a stable hash suffix.
- Detect effective-name collisions across indexes on the same PostgreSQL namespace.
- Fail fast during model construction or design-time validation if two names still collide.

Example output pattern:

```text
IX_CHFinTx_EntityType_EntityId_CreatedAtUtc
IX_CHFinTx_EntityType_EntityId_CreatedBy_CreatedAtUtc
```

or, for general fallback:

```text
IX_<ReadablePrefix>_<8-12-char-stable-hash>
```

Pros:

- Applies automatically to future migrations.
- Handles explicit names and convention-generated names.
- Prevents recurrence beyond this entity.
- Deterministic and PostgreSQL-safe.

Cons:

- Larger implementation than a two-line local rename.
- Can change many long object names if applied broadly to all existing model identifiers.
- Needs focused tests to lock hash stability and collision behavior.

## Recommended Fix

Implement Candidate 4, with Candidate 1 as the immediate local naming input for the known pair.

Recommended minimal implementation shape:

1. Add a small PostgreSQL identifier helper under `PointOfSale.Infrastructure/Data` or `PointOfSale.Infrastructure/Data/Conventions`.
2. Apply it at the end of `ApplicationDbContext.OnModelCreating`, after `ApplyConfigurationsFromAssembly(...)` and after all inline model configuration.
3. For every relational index name:
   - Compute the PostgreSQL effective identifier using a 63-byte limit.
   - If the configured name is over the limit, replace it with a deterministic shortened name.
   - If two effective names collide, fail model construction with a clear error unless the helper can safely shorten both deterministically.
4. Add targeted tests that build the model and assert no PostgreSQL-effective index-name collisions exist.
5. Explicitly shorten the two known `CommerceHubFinancialTransaction` names to:

```text
IX_CHFinTx_EntityType_EntityId_CreatedAtUtc
IX_CHFinTx_EntityType_EntityId_CreatedBy_CreatedAtUtc
```

This preserves uniqueness, readability, determinism, and query intent while making the Runtime Model PostgreSQL-safe for future baseline generation.

## Risk Assessment

Behavioral risk:

- Low. Index renaming does not change indexed columns, filters, uniqueness, or query behavior.

Migration risk:

- Medium. A global helper may change other long explicit names in generated migrations. This should be reviewed once after implementation.

Operational risk:

- Low for a greenfield baseline. No production migration chain should be edited.

Future recurrence risk:

- Low if the shared helper or model validation is implemented.
- Medium if only the two names are manually renamed.

## Files That Require Modification

Recommended implementation files:

- `PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- New helper or convention file, for example:
  - `PointOfSale.Infrastructure/Data/Conventions/PostgresSafeIdentifierConvention.cs`
  - or `PointOfSale.Infrastructure/Data/Conventions/PostgresIdentifierNameHelper.cs`

Recommended test coverage:

- Add or update an infrastructure/model test that builds `ApplicationDbContext` metadata and asserts all index database names are PostgreSQL-safe and collision-free after 63-byte truncation.

## Files That Must NOT Be Modified

Do not modify:

- `PointOfSale.Infrastructure/Migrations/20260723213101_Baseline_Recovery3.cs`
- `PointOfSale.Infrastructure/Migrations/20260723213101_Baseline_Recovery3.Designer.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Any archived migration chain
- Any manual SQL artifact

Do not regenerate migrations as part of MODEL-1.

## Final Certification

READY FOR IMPLEMENTATION
