# Checkout Session PostgreSQL Advisory Lock Test Coverage

## Summary

Added PostgreSQL-based test variants to `CheckoutSessionConsistencyIntegrationTests` to exercise the production advisory locking mechanism that was previously untested.

## Problem

The existing tests used `SqliteCheckoutSessionScope`, which only creates SQLite in-memory databases. The `AcquireCartScopedLockAsync` method in `CheckoutSessionService` has a conditional branch:

```csharp
private async Task<bool> AcquireCartScopedLockAsync(Guid tenantId, Guid cartId, CancellationToken ct)
{
    if (!IsNpgsqlProvider())
    {
        _logger.LogWarning(
            "Cart-scoped lock not available for non-Npgsql provider...");
        return false;
    }

    var lockKey = ComputeCartLockKey(tenantId, cartId);
    await _db.Database.ExecuteSqlRawAsync(
        "SELECT pg_advisory_xact_lock({0});",
        new object[] { lockKey },
        ct);
    return true;
}
```

Since SQLite is not an Npgsql provider, the PostgreSQL advisory lock path (`pg_advisory_xact_lock`) was never exercised in tests, leaving a critical concurrency safety mechanism untested.

## Solution

### 1. Created Common Interface

Added `ICheckoutSessionTestScope` interface to allow both SQLite and PostgreSQL scopes to be used interchangeably:

```csharp
private interface ICheckoutSessionTestScope : IAsyncDisposable
{
    Guid TenantId { get; }
    Task<ApplicationDbContext> CreateDbContextAsync();
}
```

### 2. Added PostgreSQL Scope

Created `PostgresCheckoutSessionScope` class that:
- Uses `RealPostgresTestSupport` to create real PostgreSQL test databases
- Leverages existing `TestDatabaseScope` infrastructure
- Properly initializes tenant data
- Implements the same interface as `SqliteCheckoutSessionScope`

```csharp
private sealed class PostgresCheckoutSessionScope : ICheckoutSessionTestScope
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly TestTenantContext _tenantContext;

    public static async Task<PostgresCheckoutSessionScope> CreateAsync()
    {
        if (!RealPostgresTestSupport.TryCreateScope(...))
        {
            throw new InvalidOperationException(
                "PostgreSQL test infrastructure is not available...");
        }
        // ... initialization
    }
}
```

### 3. Added PostgreSQL Test Variants

Added two new test methods that mirror the existing SQLite tests but use PostgreSQL:

1. **`Postgres_CreateOrResumeSessionAsync_WhenParallelRequestsRace_AdvisoryLockPreventsRaceCondition`**
   - Tests that parallel requests to create/resume sessions use advisory locks
   - Verifies only one session is created when racing requests occur
   - Exercises the `pg_advisory_xact_lock` code path

2. **`Postgres_RecoverByToken_WhenLegacyDuplicateSessionsExist_AdvisoryLockEnsuresConsistentReconciliation`**
   - Tests session recovery with advisory locking
   - Verifies consistent reconciliation of duplicate sessions
   - Ensures the advisory lock prevents race conditions during recovery

Both tests:
- Skip gracefully if PostgreSQL is not available (using `RealPostgresTestSupport.TryCreateScope`)
- Use the same test logic as SQLite variants
- Validate that advisory locks work correctly in production scenarios

### 4. Updated Helper Methods

Modified `SeedLegacyDuplicateSessionsAsync` to accept `ICheckoutSessionTestScope` instead of `SqliteCheckoutSessionScope`, allowing it to work with both scope types.

## Test Execution

### When PostgreSQL is Available
The PostgreSQL tests will:
- Create a real PostgreSQL test database
- Execute the advisory lock code path
- Verify concurrency safety mechanisms work correctly

### When PostgreSQL is Not Available
The PostgreSQL tests will:
- Detect the missing PostgreSQL configuration
- Skip gracefully without failing
- Return early from the test method

## Benefits

1. **Production Parity**: Tests now exercise the actual concurrency control mechanism used in production
2. **Race Condition Coverage**: Advisory lock behavior is validated under concurrent access
3. **Regression Prevention**: Changes to advisory locking logic will be caught by tests
4. **Graceful Degradation**: Tests skip when PostgreSQL is unavailable, not blocking CI/CD

## Files Modified

- `PointOfSale.Tests/Integration/CheckoutSessionConsistencyIntegrationTests.cs`
  - Added `ICheckoutSessionTestScope` interface
  - Added `PostgresCheckoutSessionScope` class
  - Added two PostgreSQL-specific test methods
  - Updated `SeedLegacyDuplicateSessionsAsync` signature

## Running the Tests

```bash
# Run all checkout session consistency tests
dotnet test --filter "FullyQualifiedName~CheckoutSessionConsistencyIntegrationTests"

# Run only PostgreSQL-specific tests
dotnet test --filter "FullyQualifiedName~Postgres_"
```

## Configuration

PostgreSQL tests require:
- `ConnectionStrings:DefaultConnection` configured in test settings
- Or set `REQUIRE_REAL_POSTGRES_TESTS=1` environment variable to enforce PostgreSQL availability

## Notes

- The existing SQLite tests continue to work as before
- SQLite tests demonstrate the fallback behavior when advisory locks are unavailable
- PostgreSQL tests validate the production concurrency safety mechanism
- Both test variants are valuable for different scenarios
