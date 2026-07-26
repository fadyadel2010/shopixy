# S47 Migration Split - Performance Fix

## Problem
The original S47 migration performed all operations in a single transaction:
1. ALTER TABLE ADD COLUMN
2. UPDATE backfill (potentially millions of rows)
3. ALTER COLUMN SET NOT NULL
4. CREATE INDEX

This caused long table locks on `AccountingOutboxMessages` during the backfill operation, blocking all writes to the table.

## Solution
Split the migration into two parts:

### Part 1: S47_AccountingOutboxSourceType (20260428160000)
**Runs in a transaction (default EF Core behavior)**
- Adds nullable `SourceType` column (fast, no data copy)
- Validates that all rows have `EventType` populated
- Backfills `SourceType` values using business logic
- Leaves column nullable

**Impact:** Short lock during column addition and UPDATE. The UPDATE holds locks but completes in the same transaction.

### Part 2: S47b_AccountingOutboxSourceTypeConstraints (20260428160001)
**Runs non-transactionally (suppressTransaction: true)**
- Sets `SourceType` column to NOT NULL
- Creates index on (TenantId, SourceType, BusinessEntityId)

**Impact:** Minimal locking. Operations run outside a transaction, allowing concurrent writes.

## Benefits
1. **Reduced Lock Duration**: The backfill completes before constraint validation and index creation
2. **Better Concurrency**: Non-transactional operations in S47b don't block writes
3. **Safer Rollout**: Can monitor S47 completion before running S47b
4. **Rollback Safety**: Each migration can be rolled back independently

## Deployment Order
1. Deploy and run S47 first
2. Verify all rows have `SourceType` populated
3. Deploy and run S47b to add constraints and index

## Rollback Order
1. Rollback S47b first (removes index and NOT NULL constraint)
2. Rollback S47 (removes column entirely)

## Notes
- Both migrations use `IF NOT EXISTS` / `IF EXISTS` for idempotency
- S47b validates prerequisites (S47 must have completed)
- Designer files are not required for pure SQL migrations
