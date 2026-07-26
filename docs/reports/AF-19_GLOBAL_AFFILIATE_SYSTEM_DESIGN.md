# AF-19: Global Affiliate System (Tenant-Agnostic)

## Vision

Transform the affiliate system from tenant-specific to a global marketplace model where:
- Affiliates register ONCE without choosing a store
- They can promote products from ANY store
- One dashboard shows commissions from ALL stores
- No concept of "home tenant" for affiliates

## Current Architecture (Tenant-Specific)

```
AffiliateAccount
├── TenantId: Guid (REQUIRED)
├── ReferralCode: string
└── DisplayName: string

Problem: Affiliate must choose a tenant during registration
```

## New Architecture (Global)

```
AffiliateAccount
├── TenantId: Guid? (NULLABLE or use GLOBAL_TENANT_ID)
├── ReferralCode: string (GLOBALLY UNIQUE)
└── DisplayName: string

AffiliateCommissionLedger
├── TenantId: Guid? (NULLABLE - commission belongs to affiliate, not tenant)
├── StoreTenantId: Guid (NEW - which store the sale came from)
├── AffiliateAccountId: Guid
├── OrderId: Guid
└── CommissionAmount: decimal
```

## Implementation Phases

### Phase 1: Database Schema Changes

#### 1.1 Make AffiliateAccount.TenantId Nullable

```sql
-- Option A: Make TenantId nullable (breaking change)
ALTER TABLE "AffiliateAccounts" 
ALTER COLUMN "TenantId" DROP NOT NULL;

-- Option B: Use a special GLOBAL tenant ID (non-breaking)
-- Create a special "Global Affiliates" tenant
INSERT INTO "Tenants" ("Id", "Name", "IsActive", "CreatedAt")
VALUES ('00000000-0000-0000-0000-000000000001', 'Global Affiliates', true, NOW())
ON CONFLICT DO NOTHING;

-- All new affiliates get this tenant ID
```

**Recommendation**: Use Option B (special tenant) to avoid breaking existing code.

#### 1.2 Add StoreTenantId to Commission Ledger

```sql
-- Track which store the commission came from
ALTER TABLE "AffiliateCommissionLedger"
ADD COLUMN "StoreTenantId" uuid NULL;

-- Backfill existing records
UPDATE "AffiliateCommissionLedger" acl
SET "StoreTenantId" = o."TenantId"
FROM "Orders" o
WHERE acl."OrderId" = o."Id"
  AND acl."StoreTenantId" IS NULL;

-- Make it required going forward
ALTER TABLE "AffiliateCommissionLedger"
ALTER COLUMN "StoreTenantId" SET NOT NULL;
```

#### 1.3 Remove Unique Constraint on (TenantId, ReferralCode)

```sql
-- Drop old constraint
ALTER TABLE "AffiliateAccounts"
DROP CONSTRAINT IF EXISTS "UQ_AffiliateAccounts_TenantId_ReferralCode";

-- Add new constraint: ReferralCode must be globally unique
ALTER TABLE "AffiliateAccounts"
ADD CONSTRAINT "UQ_AffiliateAccounts_ReferralCode" 
UNIQUE ("ReferralCode");
```

### Phase 2: Backend Code Changes

#### 2.1 Update AffiliateAuthController.Register

```csharp
[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] AffiliateRegisterDto request, CancellationToken ct)
{
    // REMOVE: Tenant resolution
    // var tenantId = await ResolveStoreTenantId(request.StoreSlug);
    
    // NEW: Use global tenant ID
    var globalTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    
    var email = request.Email.Trim().ToLowerInvariant();

    // Check for duplicate email (GLOBALLY, not per-tenant)
    var existingUser = await _dbContext.AffiliateUsers
        .IgnoreQueryFilters()
        .AnyAsync(u => u.Email == email && !u.IsDeleted, ct);

    if (existingUser)
        return Conflict(new { message = "An affiliate account with this email already exists" });

    // Generate GLOBALLY unique referral code
    string referralCode = string.Empty;
    const int maxCodeAttempts = 5;
    for (var attempt = 0; attempt < maxCodeAttempts; attempt++)
    {
        referralCode = $"{codeSeed}{Random.Shared.Next(100000, 999999)}";
        
        // Check globally (no TenantId filter)
        var codeExists = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.ReferralCode == referralCode, ct);
            
        if (!codeExists) break;

        if (attempt == maxCodeAttempts - 1)
        {
            referralCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        }
    }

    var affiliateAccount = AffiliateAccount.Create(
        globalTenantId, // Use global tenant
        Guid.NewGuid(),
        referralCode,
        request.DisplayName);

    affiliateAccount.Activate();
    _dbContext.AffiliateAccounts.Add(affiliateAccount);

    var affiliateUser = AffiliateUser.Create(
        globalTenantId, // Use global tenant
        email,
        "TEMP_HASH",
        affiliateAccount.Id);

    affiliateUser.SetPasswordHash(
        _passwordHasher.HashPassword(affiliateUser, request.Password));

    _dbContext.AffiliateUsers.Add(affiliateUser);
    affiliateAccount.SetUserId(affiliateUser.Id);

    await _dbContext.SaveChangesAsync(ct);

    _logger.LogInformation(
        "[Affiliate:Auth] Global affiliate registered | AffiliateUserId: {UserId} | " +
        "AffiliateAccountId: {AccountId} | Email: {Email} | ReferralCode: {ReferralCode}",
        affiliateUser.Id, affiliateAccount.Id, email, referralCode);

    var (accessToken, refreshToken) = GenerateTokens(affiliateUser, affiliateAccount);

    affiliateUser.RefreshToken = refreshToken;
    affiliateUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);
    await _dbContext.SaveChangesAsync(ct);

    return Ok(new AffiliateAuthResponse
    {
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        AffiliateAccountId = affiliateAccount.Id,
        ReferralCode = affiliateAccount.ReferralCode,
        DisplayName = affiliateAccount.DisplayName,
        Email = email,
        TenantId = globalTenantId // Return global tenant ID
    });
}
```

#### 2.2 Update AffiliateRegisterDto

```csharp
public class AffiliateRegisterDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    
    // REMOVE: StoreSlug (no longer needed)
    // public string? StoreSlug { get; set; }
}
```

#### 2.3 Update Commission Recording

```csharp
// In AffiliateService.RecordCommissionForOrderAsync
public async Task RecordCommissionForOrderAsync(Guid orderId, CancellationToken ct = default)
{
    var order = await _context.Orders
        .IgnoreQueryFilters()
        .Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct);

    if (order == null) return;

    var storeTenantId = order.TenantId; // Store where sale happened

    var attribution = await _context.AffiliateAttributions
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(a => a.OrderId == orderId && !a.IsDeleted, ct);

    if (attribution == null) return;

    // Affiliate lookup (no tenant filter - they're global)
    var affiliate = await _context.AffiliateAccounts
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(a => a.Id == attribution.AffiliateAccountId && !a.IsDeleted, ct);

    if (affiliate == null || !affiliate.IsActive) return;

    var globalTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Load product policies from the STORE's tenant (not affiliate's tenant)
    var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
    var policies = await _context.AffiliateProductPolicies
        .IgnoreQueryFilters()
        .Where(p => p.TenantId == storeTenantId && productIds.Contains(p.ProductId) && p.IsEnabled && !p.IsDeleted)
        .ToDictionaryAsync(p => p.ProductId, ct);

    var earnedEntries = new List<AffiliateCommissionLedger>();

    foreach (var item in order.Items)
    {
        var commissionableAmount = item.Total;
        
        if (commissionableAmount <= 0) continue;

        if (!policies.TryGetValue(item.ProductId, out var policy)) continue;

        // Create commission entry with BOTH global tenant and store tenant
        var entry = new AffiliateCommissionLedger
        {
            Id = Guid.NewGuid(),
            TenantId = globalTenantId, // Global affiliate tenant
            StoreTenantId = storeTenantId, // Store where sale happened
            AttributionId = attribution.Id,
            AffiliateAccountId = affiliate.Id,
            OrderId = orderId,
            OrderItemId = item.Id,
            BaseAmount = commissionableAmount,
            CommissionRate = policy.CommissionRate,
            CommissionAmount = Math.Round(commissionableAmount * policy.CommissionRate, 4),
            Currency = order.Currency,
            EntryType = CommissionEntryType.Earned,
            IdempotencyKey = $"AFFILIATE_COMM:EARNED:{globalTenantId}:{item.Id}",
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _context.AffiliateCommissionLedger.Add(entry);
            await _context.SaveChangesAsync(ct);
            earnedEntries.Add(entry);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _context.Entry(entry).State = EntityState.Detached;
            _logger.LogInformation(
                "Commission already recorded for OrderItem {OrderItemId} — idempotent skip",
                item.Id);
        }
    }

    if (earnedEntries.Count > 0)
    {
        await _eventPublisher.PublishAsync(new AffiliateCommissionEarnedEvent
        {
            TenantId = globalTenantId, // Global tenant
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            AffiliateAccountId = affiliate.Id,
            ReferralCode = affiliate.ReferralCode,
            TotalCommission = earnedEntries.Sum(e => e.CommissionAmount),
            Currency = order.Currency,
            ItemCount = earnedEntries.Count,
            Items = earnedEntries.Select(e =>
            {
                var item = order.Items.First(i => i.Id == e.OrderItemId);
                return new CommissionLineItem
                {
                    OrderItemId = e.OrderItemId,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    VariantName = item.VariantName,
                    PaidAmount = e.BaseAmount,
                    CommissionRate = e.CommissionRate,
                    CommissionAmount = e.CommissionAmount
                };
            }).ToList(),
            CorrelationId = orderId.ToString()
        }, ct);

        _logger.LogInformation(
            "[AF-19:Global] Recorded commission | AffiliateId: {AffiliateId} | " +
            "StoreTenant: {StoreTenant} | OrderId: {OrderId} | Items: {Count}, Total: {Total} {Currency}",
            affiliate.Id, storeTenantId, orderId, earnedEntries.Count,
            earnedEntries.Sum(e => e.CommissionAmount), order.Currency);
    }
}
```

### Phase 3: Frontend Changes

#### 3.1 Remove Store Selection from Registration

```typescript
// Before (with store selection)
interface AffiliateRegisterRequest {
  email: string;
  password: string;
  displayName: string;
  storeSlug: string; // ← REMOVE THIS
}

// After (global registration)
interface AffiliateRegisterRequest {
  email: string;
  password: string;
  displayName: string;
  // No store selection needed
}
```

#### 3.2 Update Product Discovery

Affiliates should see products from ALL stores:

```typescript
// Affiliate product discovery endpoint
GET /api/affiliate/discovery/products
  ?page=1
  &pageSize=20
  &q=search+term
  &categoryId=optional
  &storeTenantId=optional  // NEW: Filter by specific store (optional)

Response:
{
  "products": [
    {
      "productId": "...",
      "name": "Product Name",
      "price": 100.00,
      "currency": "EGP",
      "storeName": "Anatico",        // NEW: Show which store
      "storeTenantId": "...",         // NEW: Store identifier
      "commissionRate": 0.10,
      "estimatedCommission": 10.00,
      "affiliateLink": "https://anatico.store.com/products/xyz?ref=ZEZO906409"
    }
  ]
}
```

#### 3.3 Update Dashboard

Show commissions grouped by store:

```typescript
// Dashboard endpoint
GET /api/affiliate/portal/dashboard

Response:
{
  "totalEarned": 1000.00,
  "totalApproved": 500.00,
  "totalPaid": 200.00,
  "netEarnings": 800.00,
  "totalOrders": 50,
  
  // NEW: Breakdown by store
  "byStore": [
    {
      "storeName": "Anatico",
      "storeTenantId": "...",
      "earned": 600.00,
      "orders": 30,
      "currency": "EGP"
    },
    {
      "storeName": "Mena",
      "storeTenantId": "...",
      "earned": 400.00,
      "orders": 20,
      "currency": "EGP"
    }
  ]
}
```

### Phase 4: Migration Strategy

#### 4.1 Migrate Existing Affiliates

```sql
-- Create global tenant
INSERT INTO "Tenants" ("Id", "Name", "IsActive", "CreatedAt")
VALUES ('00000000-0000-0000-0000-000000000001', 'Global Affiliates', true, NOW())
ON CONFLICT DO NOTHING;

-- Option A: Keep existing affiliates in their current tenants
-- (No migration needed - they continue working as before)

-- Option B: Migrate all affiliates to global tenant
UPDATE "AffiliateAccounts"
SET "TenantId" = '00000000-0000-0000-0000-000000000001';

UPDATE "AffiliateUsers"
SET "TenantId" = '00000000-0000-0000-0000-000000000001';

UPDATE "AffiliateCommissionLedger"
SET "TenantId" = '00000000-0000-0000-0000-000000000001';
```

**Recommendation**: Use Option A (keep existing affiliates) to avoid disruption.

## Benefits

1. **Simplified Registration**: No store selection needed
2. **Maximum Flexibility**: Affiliates promote ANY product from ANY store
3. **Unified Dashboard**: One place to see all earnings
4. **Scalability**: Easy to add new stores without affiliate re-registration
5. **Marketplace Model**: True multi-vendor affiliate system

## Considerations

### Accounting

- Commission liability is in the **store's tenant** (where the sale happened)
- Commission payout is from the **global affiliate pool** (or per-store settlement)
- Need clear inter-tenant accounting for commission expenses

### Product Policies

- Each store sets its own commission rates per product
- Affiliates see estimated commission when browsing products
- Commission rate comes from the **store's** `AffiliateProductPolicies` table

### Payout Management

- Payouts can be:
  - **Option A**: Centralized (one payout from global pool)
  - **Option B**: Per-store (separate payouts from each store)

**Recommendation**: Option A (centralized) for simplicity.

## Implementation Checklist

- [ ] Create global tenant in database
- [ ] Add `StoreTenantId` column to `AffiliateCommissionLedger`
- [ ] Update unique constraint on `AffiliateAccounts.ReferralCode`
- [ ] Update `AffiliateAuthController.Register` to use global tenant
- [ ] Update `AffiliateService.RecordCommissionForOrderAsync` to track store tenant
- [ ] Update affiliate dashboard to show per-store breakdown
- [ ] Update product discovery to show all stores
- [ ] Test cross-store commission recording
- [ ] Update admin interfaces to show global affiliates
- [ ] Document new affiliate onboarding flow

## Testing Scenarios

1. **New Affiliate Registration**
   - Register without selecting a store
   - Verify `TenantId = GLOBAL_TENANT_ID`
   - Verify referral code is globally unique

2. **Cross-Store Product Promotion**
   - Affiliate browses products from Store A and Store B
   - Generates referral links for both
   - Verify both links work

3. **Commission Recording**
   - Order from Store A with affiliate ref
   - Verify commission recorded with `StoreTenantId = Store A`
   - Order from Store B with same affiliate ref
   - Verify commission recorded with `StoreTenantId = Store B`

4. **Dashboard**
   - Verify dashboard shows combined earnings
   - Verify per-store breakdown is correct
   - Verify orders from all stores appear

## Rollout Plan

1. **Phase 1**: Database changes (non-breaking)
2. **Phase 2**: Backend code updates
3. **Phase 3**: Frontend updates
4. **Phase 4**: Testing with new affiliates
5. **Phase 5**: Migrate existing affiliates (optional)
6. **Phase 6**: Full production rollout

## Status

🔄 **DESIGN COMPLETE** - Ready for implementation approval
