# Accounting Outbox Idempotency Fix

**Date:** 2026-02-09  
**Status:** ✅ Resolved  
**Severity:** Critical (blocking all POS sales accounting)

---

## Executive Summary

The accounting outbox processor was failing to post journal entries for POS sales due to a chain of issues related to EF Core global query filters and database schema mismatches. This caused all new sales to fail accounting integration, with the outbox continuously retrying and failing.

---

## Issues Encountered

### Issue 1: `AccountNotFoundException`

**Symptom:**
```
PointOfSale.Core.Exceptions.Accounting.AccountNotFoundException: 
One or more accounts not found: [list of account IDs]
```

**Root Cause:**  
The `FinancialPostingService.ValidateJournalAsync` method was querying `LedgerAccounts` without `IgnoreQueryFilters()`. Since the outbox processor runs as a background service without tenant context, the EF Core global query filter (`e.TenantId == CurrentTenantId`) was filtering out ALL accounts.

**Fix:**  
Added `.IgnoreQueryFilters()` to the account validation query in `ValidateJournalAsync`:

```csharp
// Before
var accounts = await _db.LedgerAccounts
    .AsNoTracking()
    .Where(a => accountIds.Contains(a.Id) && a.TenantId == request.TenantId)
    .ToListAsync(ct);

// After
var accounts = await _db.LedgerAccounts
    .IgnoreQueryFilters()  // ← Added
    .AsNoTracking()
    .Where(a => accountIds.Contains(a.Id) && a.TenantId == request.TenantId)
    .ToListAsync(ct);
```

**File:** `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs` (lines 350-356)

---

### Issue 2: `duplicate key violates unique constraint "IX_LedgerJournal_Idempotency"`

**Symptom:**
```
Npgsql.PostgresException: 23505: duplicate key value violates unique constraint "IX_LedgerJournal_Idempotency"
```

**Root Cause:**  
The idempotency check in `PostJournalAsync` was also affected by the global query filter. When the outbox retried a failed message:
1. The journal entry had already been created in a previous attempt
2. But the idempotency check couldn't find it (filtered by tenant)
3. So it tried to insert a duplicate, causing the constraint violation

**Fix:**  
Added `.IgnoreQueryFilters()` to BOTH idempotency checks:

```csharp
// STEP 1: Pre-transaction idempotency check
var existingJournal = await _db.JournalEntries
    .IgnoreQueryFilters()  // ← Added
    .AsNoTracking()
    .Include(j => j.LedgerEntries)
    .FirstOrDefaultAsync(j =>
        j.TenantId == request.TenantId &&
        j.IdempotencyKey == request.IdempotencyKey,
        ct);

// STEP 4: In-transaction idempotency check (race condition protection)
var existing = await _db.JournalEntries
    .IgnoreQueryFilters()  // ← Added
    .Include(j => j.LedgerEntries)
    .FirstOrDefaultAsync(j =>
        j.TenantId == request.TenantId &&
        j.IdempotencyKey == request.IdempotencyKey,
        ct);
```

**File:** `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs` (lines 45-51, 83-88)

---

### Issue 3: `null value in column "UpdatedAt" violates not-null constraint`

**Symptom:**
```
Npgsql.PostgresException: 23502: null value in column "UpdatedAt" of relation "CommerceAccountingAudit" violates not-null constraint
```

**Root Cause:**  
Two problems:
1. The `CommerceAccountingAudit.Create()` factory method wasn't setting `CreatedAt`
2. The database column `UpdatedAt` was defined as NOT NULL, but the entity `BaseEntity.UpdatedAt` is nullable (`DateTime?`)

**Fix 1 - Entity:**  
Added `CreatedAt` initialization to the factory method:

```csharp
public static CommerceAccountingAudit Create(...)
{
    return new CommerceAccountingAudit
    {
        // ... other properties
        CreatedAt = DateTime.UtcNow,  // ← Added
    };
}
```

**File:** `PointOfSale.Core/Entities/Accounting/CommerceAccountingAudit.cs` (line 114)

**Fix 2 - Database:**  
```sql
ALTER TABLE "CommerceAccountingAudit" 
ALTER COLUMN "UpdatedAt" DROP NOT NULL;
```

---

### Issue 4: Stale Outbox Messages

**Symptom:**  
Even after code fixes, old failed messages kept retrying and failing.

**Root Cause:**  
The outbox contained messages referencing old/deleted data from failed attempts.

**Historical fix (obsolete):**  
This document originally recommended clearing stuck outbox messages directly. That is no longer safe for production accounting rows; claimed, attempted, failed, or retrying accounting outbox rows are ledger recovery evidence and must be retried, repaired, or explicitly reconciled instead of deleted.
```sql
-- Do not run against production accounting outbox rows.
-- Use the current accounting retry/reconciliation workflow instead.
```

---

## Summary of Code Changes

| File | Change |
|------|--------|
| `FinancialPostingService.cs` | Added `IgnoreQueryFilters()` to idempotency check (line 47) |
| `FinancialPostingService.cs` | Added `IgnoreQueryFilters()` to in-transaction idempotency check (line 85) |
| `FinancialPostingService.cs` | Added `IgnoreQueryFilters()` to account lookup (line 109) |
| `FinancialPostingService.cs` | Added `IgnoreQueryFilters()` to validation account lookup (line 353) |
| `CommerceAccountingAudit.cs` | Added `CreatedAt = DateTime.UtcNow` to factory method |

## Database Changes Required

```sql
-- Fix UpdatedAt constraint
ALTER TABLE "CommerceAccountingAudit" 
ALTER COLUMN "UpdatedAt" DROP NOT NULL;

-- Do not clear unprocessed accounting outbox messages by deletion.
-- Use the current accounting retry/reconciliation workflow instead.
```

---

## Verification

After applying fixes, new sales process correctly:

```
info: Posting POS sale INV-20260209-0004 for tenant ..., Amount: 250
info: Posted journal JE-2026-000008 with 2 entries
info: Posted journal JE-2026-000009 with 2 entries (COGS)
info: Successfully processed outbox message ..., Journal: JE-2026-000008
```

---

## Lessons Learned

1. **Background processors need special attention** - Any DbContext query in a background service should consider whether global query filters will work correctly without tenant context.

2. **Idempotency checks must be filter-aware** - If the main operation uses `IgnoreQueryFilters()`, the idempotency check must also use it, otherwise it won't find existing records.

3. **Entity/Database schema sync** - When entities inherit from `BaseEntity`, ensure all base properties are properly initialized and the database schema matches the nullability.

4. **Retry loops amplify issues** - The outbox retry mechanism, while essential for reliability, can amplify issues by continuously re-attempting failed operations. Consider adding dead-letter handling for messages that fail repeatedly.

---

## Related Files

- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`
- `PointOfSale.Core/Entities/Accounting/CommerceAccountingAudit.cs`
- `PointOfSale.Core/Entities/BaseEntity.cs`
