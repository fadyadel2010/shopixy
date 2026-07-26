# Visa Account Automatic Creation - Solution Summary

## Your Question
> "If we created manually by SQL query now, if I make a Database Reset again, will the Visa Account be created automatically or will I need to use this SQL query again?"

## Answer: **It Will Be Created Automatically! ✅**

## How It Works

### 1. Database Seed Data (Initial Creation)
**File**: `PointOfSale.Infrastructure\Data\ApplicationDbContext.cs`

The Visa Account is defined in the seed data:
```csharp
// Seed Visa Account (حساب الفيزا)
modelBuilder.Entity<FinancialAccount>().HasData(
    new FinancialAccount
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Name = "حساب الفيزا",
        AccountNumber = "VISA-001",
        IsSystem = true,
        Balance = 0,
        IsDeleted = false,
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    }
);
```

**When it runs**: Only when the database is first created (initial migration)

### 2. Database Reset Logic (Automatic Recreation)
**File**: `PointOfSale.API\Controllers\MaintenanceController.cs`

We've added logic to automatically recreate the Visa Account after database reset:

```csharp
[HttpPost("reset-database")]
public async Task<IActionResult> ResetDatabase()
{
    try
    {
        // 1. Truncate all tables
        await _context.Database.ExecuteSqlRawAsync(truncateCommand);
        
        // 2. Recreate system accounts (Visa Account)
        await RecreateSystemAccounts();
        
        return Ok(new { message = "Database has been reset successfully." });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { message = "Error resetting database" });
    }
}

private async Task RecreateSystemAccounts()
{
    var visaAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    
    var visaAccountExists = await _context.FinancialAccounts
        .IgnoreQueryFilters()
        .AnyAsync(a => a.Id == visaAccountId);

    if (!visaAccountExists)
    {
        var visaAccount = new Core.Entities.FinancialAccount
        {
            Id = visaAccountId,
            Name = "حساب الفيزا",
            AccountNumber = "VISA-001",
            IsSystem = true,
            Balance = 0,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.FinancialAccounts.Add(visaAccount);
        await _context.SaveChangesAsync();
    }
}
```

**When it runs**: Every time you click "Database Reset" in the Settings screen

## Complete Flow

### Scenario 1: Fresh Database Installation
```
1. Run migrations (dotnet ef database update)
   → Database created
   → Seed data runs
   → Visa Account created automatically ✅

2. Application starts
   → Visa Account exists
   → Ready to use
```

### Scenario 2: Database Reset
```
1. User clicks "Database Reset" in Settings
   → POST /api/maintenance/reset-database
   
2. MaintenanceController.ResetDatabase() executes:
   a. Truncates all tables (including FinancialAccounts)
   b. Calls RecreateSystemAccounts()
   c. Visa Account is recreated automatically ✅
   
3. Result:
   → Visa Account exists
   → Ready to use
   → No manual SQL needed!
```

### Scenario 3: API Startup (Future Enhancement)
We also created `FinancialAccountService` which can ensure the Visa Account exists on startup:

```csharp
public async Task<FinancialAccount> EnsureVisaAccountExistsAsync()
{
    var visaAccount = await _context.FinancialAccounts
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(a => a.Id == VisaAccountId);

    if (visaAccount == null)
    {
        // Create it automatically
        visaAccount = new FinancialAccount { ... };
        _context.FinancialAccounts.Add(visaAccount);
        await _context.SaveChangesAsync();
    }
    
    return visaAccount;
}
```

## Current Issue & Fix

### The Problem
The Visa Account might not be visible in the Main & Sub Accounts screen because:
1. The seed data didn't explicitly set `IsDeleted = false`
2. There's a soft delete filter: `modelBuilder.Entity<FinancialAccount>().HasQueryFilter(e => !e.IsDeleted);`
3. If `IsDeleted` is `NULL`, the filter might hide it

### The Fix (One-Time)
Run the SQL script once to fix the existing record:

```sql
-- Fix existing Visa Account
UPDATE "FinancialAccounts"
SET 
    "IsDeleted" = false,
    "Name" = 'حساب الفيزا',
    "AccountNumber" = 'VISA-001',
    "IsSystem" = true
WHERE "Id" = '11111111-1111-1111-1111-111111111111';
```

**OR** just do a Database Reset from the Settings screen, and it will be recreated correctly!

### After the Fix
- ✅ Visa Account visible in Main & Sub Accounts
- ✅ Database Reset will always recreate it
- ✅ No manual SQL needed in the future
- ✅ Fully automatic

## Summary

| Action | Visa Account Created? | How? |
|--------|----------------------|------|
| **Fresh Install** | ✅ Yes | Database seed data |
| **Database Reset** | ✅ Yes | MaintenanceController.RecreateSystemAccounts() |
| **Manual SQL** | ✅ Yes | One-time fix for existing database |
| **Future Resets** | ✅ Yes | Automatic via MaintenanceController |

## Recommendation

**For your current database:**
1. Run the SQL fix script once (or do a Database Reset)
2. From now on, it will be automatic!

**For future installations:**
- No action needed
- Everything is automatic ✅

## Files Modified

1. ✅ `ApplicationDbContext.cs` - Added `IsDeleted = false` to seed data
2. ✅ `MaintenanceController.cs` - Added `RecreateSystemAccounts()` method
3. ✅ `FinancialAccountService.cs` - Created service for ensuring account exists

## Conclusion

**You will NOT need to run the SQL query again!** 

The Visa Account will be created automatically:
- ✅ On fresh database installation
- ✅ After every Database Reset
- ✅ Protected from deletion (IsSystem = true)
- ✅ Always available for Card payments

Just run the SQL fix once for your current database, and from then on, it's fully automatic! 🎉
