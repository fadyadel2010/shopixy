# AF-30A: Final Structural Gate Audit

## Executive Summary

**GATE STATUS: 🔴 CRITICAL BLOCKERS IDENTIFIED**

**Overall Safety Score: 55/100** ❌ **NOT PRODUCTION-READY**

Three critical architectural violations discovered:
1. 🔴 **CRITICAL**: No explicit transaction wrapping (partial state corruption risk)
2. 🔴 **CRITICAL**: Missing inactive category guard in `EnableAffiliateForProductAsync()`
3. 🔴 **CRITICAL**: No projection update mechanism for category rename (silent drift)

**Recommendation:** **BLOCK FRONTEND INTEGRATION** until critical issues resolved.

---

## 🧱 SECTION 1 — TRANSACTION ATOMICITY

### ❌ CRITICAL VIOLATION: NO EXPLICIT TRANSACTION

**Endpoint:** `POST /api/admin/products/{productId}/affiliate/enable-with-category`

**Current Flow:** `AdminProductAffiliateController.cs:247-253`

```csharp
var result = await _affiliateService.EnableAffiliateWithCategoryAsync(
    productId, request.CommissionRate, request.CategoryId, request.MaxCommissionPerItem, ct);

// Publish event with category snapshot
await _eventPublisher.PublishAsync(new AffiliateProductPolicyChangedEvent { ... }, ct);
```

**Service Implementation:** `AffiliateService.cs:258-341`

```csharp
public async Task<AffiliatePolicyWithCategoryDetails> EnableAffiliateWithCategoryAsync(...)
{
    // ... validation ...
    
    var existing = await _context.AffiliateProductPolicies
        .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

    if (existing != null)
    {
        existing.UpdateRate(commissionRate, maxPerItem);
        existing.UpdateCategory(categoryId);
        existing.Enable();
        await _context.SaveChangesAsync(ct);  // ⚠️ NO TRANSACTION WRAPPER
        
        return new AffiliatePolicyWithCategoryDetails { ... };
    }

    var policy = AffiliateProductPolicy.Create(tenantId, productId, commissionRate, maxPerItem, categoryId);
    _context.AffiliateProductPolicies.Add(policy);
    await _context.SaveChangesAsync(ct);  // ⚠️ NO TRANSACTION WRAPPER
    
    return new AffiliatePolicyWithCategoryDetails { ... };
}
```

### Partial State Corruption Risk

**Scenario 1: SaveChanges Succeeds, Event Publish Fails**

```
1. UpdateCategory(categoryId) → Success
2. Enable() → Success
3. SaveChanges() → Success (DB committed)
4. PublishAsync() → FAILS (network error, RabbitMQ down)
```

**Result:**
- ✅ SQL: Policy enabled with category
- ❌ MongoDB: Projection NOT updated (stale data)
- ❌ Discover: Product NOT visible (missing from index)
- ❌ Landing: Product NOT accessible (no projection)

**Impact:** Silent data corruption, product invisible to affiliates

**Scenario 2: UpdateCategory Succeeds, Enable Fails**

```
1. UpdateCategory(categoryId) → Success (in-memory)
2. Enable() → THROWS (domain validation fails)
3. SaveChanges() → NOT CALLED
```

**Result:**
- ✅ No DB corruption (transaction rolled back by EF Core)
- ✅ Exception propagated correctly

**Impact:** Safe (EF Core implicit transaction on single SaveChanges)

**Scenario 3: Multiple SaveChanges Calls**

Current implementation: ✅ **SINGLE SaveChanges** per method

**Analysis:**
- `EnableAffiliateWithCategoryAsync()`: 1 SaveChanges (line 313 OR 338)
- `UpdateCategoryForProductAsync()`: 1 SaveChanges (line 244)
- `EnableAffiliateForProductAsync()`: 1 SaveChanges (line 160)

✅ **NO MULTIPLE SAVECHANGES RISK**

### Transaction Pattern Comparison

**Current Pattern (Implicit Transaction):**
```csharp
existing.UpdateCategory(categoryId);
existing.Enable();
await _context.SaveChangesAsync(ct);  // EF Core wraps in transaction
await _eventPublisher.PublishAsync(...);  // ⚠️ OUTSIDE TRANSACTION
```

**Expected Pattern (Explicit Transaction):**
```csharp
using var tx = await _context.Database.BeginTransactionAsync(ct);

existing.UpdateCategory(categoryId);
existing.Enable();
await _context.SaveChangesAsync(ct);

await tx.CommitAsync(ct);

// Event published AFTER commit (at-least-once delivery)
await _eventPublisher.PublishAsync(...);
```

**OR Outbox Pattern:**
```csharp
using var tx = await _context.Database.BeginTransactionAsync(ct);

existing.UpdateCategory(categoryId);
existing.Enable();

// Store event in outbox table (same transaction)
_context.OutboxEvents.Add(new OutboxEvent { ... });

await _context.SaveChangesAsync(ct);
await tx.CommitAsync(ct);

// Background worker publishes from outbox
```

### Evidence from Codebase

**Other services use explicit transactions:**

- `CheckoutService.cs:2148` - `BeginTransactionAsync()` for order creation
- `SalesController.cs:54` - `BeginTransactionAsync()` for sale creation
- `FinancialPostingService.cs:78` - `BeginTransactionAsync(IsolationLevel.Serializable)`
- `PaymentLifecycleService.cs:52` - `BeginTransactionAsync()` for payment state changes

**AffiliateService does NOT use explicit transactions** ❌

### Transaction Atomicity Score: 🔴 **30/100**

**Critical Issues:**
- ❌ No explicit transaction wrapping
- ❌ Event published outside transaction boundary
- ❌ Risk of partial state corruption (DB committed, projection stale)

**Mitigating Factors:**
- ✅ Single SaveChanges per method (EF Core implicit transaction)
- ✅ No multiple SaveChanges calls
- ⚠️ Event publish failure leaves system inconsistent

**Recommendation:**
1. Add explicit transaction wrapping
2. Commit transaction before event publish
3. Consider outbox pattern for guaranteed delivery
4. Add retry logic for event publish failures

---

## 🧱 SECTION 2 — INACTIVE CATEGORY ENABLE GUARD

### ❌ CRITICAL VIOLATION: MISSING INACTIVE GUARD

**Method:** `EnableAffiliateForProductAsync()` (legacy endpoint)

**Location:** `AffiliateService.cs:130-175`

```csharp
public async Task<AffiliateProductPolicy> EnableAffiliateForProductAsync(
    Guid productId, decimal commissionRate, decimal? maxPerItem = null,
    CancellationToken ct = default)
{
    var existing = await _context.AffiliateProductPolicies
        .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

    if (existing != null)
    {
        existing.UpdateRate(commissionRate, maxPerItem);
        
        // AF-30A: Enforce category requirement before enabling
        if (existing.AffiliateCategoryId == null)
        {
            throw new InvalidOperationException(
                "Affiliate category is required. Assign a category before enabling affiliate marketing.");
        }
        
        existing.Enable();  // ⚠️ NO VALIDATION OF category.IsActive
        await _context.SaveChangesAsync(ct);
        
        return existing;
    }
    
    throw new InvalidOperationException(
        "Affiliate category is required. Use UpdateCategoryForProductAsync() first to assign a category.");
}
```

### Attack Scenario

**Setup:**
1. Product has `AffiliateCategoryId = "electronics-guid"`
2. Category "Electronics" is active
3. SuperAdmin deactivates "Electronics" category
4. POS Admin calls `POST /api/admin/products/{id}/affiliate/enable`

**Current Behavior:**
```csharp
if (existing.AffiliateCategoryId == null)  // ✅ Passes (category exists)
    throw new InvalidOperationException(...);

existing.Enable();  // ❌ SUCCEEDS (no IsActive check)
```

**Result:**
- ✅ Domain validation passes (category not null)
- ❌ Service validation MISSING (no IsActive check)
- ❌ Product enabled with inactive category
- ❌ Product appears in Discover with deactivated taxonomy

**Impact:** Violates business rule that inactive categories cannot be used

### Comparison with Correct Implementation

**EnableAffiliateWithCategoryAsync() (NEW endpoint):** ✅ **CORRECT**

```csharp
// Validate category
var category = await _context.AffiliateCategories
    .FirstOrDefaultAsync(c => c.Id == categoryId, ct);

if (category == null)
    throw new InvalidOperationException($"Affiliate category {categoryId} not found");

if (!category.IsActive)  // ✅ VALIDATES IsActive
    throw new InvalidOperationException($"Affiliate category {categoryId} is not active");
```

**UpdateCategoryForProductAsync():** ✅ **CORRECT**

```csharp
if (categoryId.HasValue)
{
    var category = await _context.AffiliateCategories
        .FirstOrDefaultAsync(c => c.Id == categoryId.Value, ct);

    if (category == null)
        throw new InvalidOperationException($"Affiliate category {categoryId.Value} not found");

    if (!category.IsActive)  // ✅ VALIDATES IsActive
    {
        _logger.LogWarning(...);
        throw new InvalidOperationException($"Affiliate category {categoryId.Value} is not active");
    }
}
```

### Guard Coverage Matrix

| Method | Checks Null | Checks Exists | Checks IsActive | Status |
|--------|------------|---------------|-----------------|--------|
| `EnableAffiliateWithCategoryAsync()` | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Safe |
| `UpdateCategoryForProductAsync()` | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Safe |
| `EnableAffiliateForProductAsync()` | ✅ Yes | ❌ No | ❌ No | 🔴 Unsafe |

### Inactive Category Enable Guard Score: 🔴 **40/100**

**Critical Issues:**
- ❌ Legacy enable endpoint missing IsActive validation
- ❌ Can enable affiliate with deactivated category
- ❌ Violates business rule

**Mitigating Factors:**
- ✅ New atomic endpoint validates correctly
- ✅ Update category endpoint validates correctly
- ⚠️ Legacy endpoint still exposed and vulnerable

**Recommendation:**
1. Add IsActive validation to `EnableAffiliateForProductAsync()`
2. Fetch category and validate before calling `Enable()`
3. Consider deprecating legacy endpoint in favor of atomic endpoint

---

## 🧱 SECTION 3 — PROJECTION SNAPSHOT CONSISTENCY ON CATEGORY RENAME

### ❌ CRITICAL VIOLATION: NO PROJECTION UPDATE MECHANISM

**Scenario:**
1. SuperAdmin creates category "Electronics" (slug: `electronics`)
2. 100 products assigned to "Electronics"
3. MongoDB projection contains `AffiliateCategoryName: "Electronics"`, `AffiliateCategorySlug: "electronics"`
4. SuperAdmin renames category to "Consumer Electronics" (slug: `consumer-electronics`)
5. **WHAT HAPPENS TO EXISTING PROJECTIONS?**

### Current Implementation Analysis

**Category Update Endpoint:** `AdminAffiliateCategoryController.cs:243-299`

```csharp
[HttpPut("{id:guid}")]
public async Task<ActionResult<CategoryResponse>> UpdateCategory(
    Guid id, [FromBody] UpdateAffiliateCategoryDetailsRequest request, CancellationToken ct)
{
    var category = await _dbContext.AffiliateCategories
        .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

    if (category == null)
        return NotFound(new { message = "Category not found" });

    if (!string.IsNullOrWhiteSpace(request.Name))
        category.UpdateName(request.Name);

    if (!string.IsNullOrWhiteSpace(request.Slug))
        category.UpdateSlug(request.Slug);

    if (request.DisplayOrder.HasValue)
        category.UpdateDisplayOrder(request.DisplayOrder.Value);

    await _dbContext.SaveChangesAsync(ct);  // ⚠️ NO EVENT EMITTED

    _logger.LogInformation(
        "[AF-30] Category updated: {Id} → {Name} ({Slug})",
        id, category.Name, category.Slug);

    return Ok(new CategoryResponse { ... });
}
```

**Analysis:**
- ✅ Category updated in SQL
- ❌ NO event emitted
- ❌ NO projection update triggered
- ❌ MongoDB documents remain stale

### Event System Analysis

**Existing Events:** `AffiliateEvents.cs`

```csharp
// ✅ EXISTS
public class AffiliateProductPolicyChangedEvent : IntegrationEvent { ... }

// ❌ MISSING
public class AffiliateCategoryUpdatedEvent : IntegrationEvent { ... }
public class AffiliateCategoryRenamedEvent : IntegrationEvent { ... }
public class AffiliateCategoryDeactivatedEvent : IntegrationEvent { ... }
```

**Evidence from Codebase:**

`CacheKeyBuilder.cs:168-172` (comment):
```csharp
/// RESERVED FOR FUTURE USE:
/// - This pattern key is NOT used in Phase 2.4 (Read-Only)
/// - Intended for future cache invalidation scenarios (e.g., when CategoryUpdatedEvent is implemented)
/// - Do NOT use in storefront-facing code until cache invalidation feature is implemented
```

**Conclusion:** Category update events are **PLANNED BUT NOT IMPLEMENTED**

### Projection Drift Analysis

**After Category Rename:**

| Data Store | Category Name | Category Slug | Status |
|------------|--------------|---------------|--------|
| SQL (AffiliateCategories) | "Consumer Electronics" | `consumer-electronics` | ✅ Updated |
| MongoDB (Product 1) | "Electronics" | `electronics` | ❌ Stale |
| MongoDB (Product 2) | "Electronics" | `electronics` | ❌ Stale |
| MongoDB (Product N) | "Electronics" | `electronics` | ❌ Stale |

**Impact:**

1. **Discover Filtering Broken:**
   - User filters by `consumer-electronics` slug
   - MongoDB query: `AffiliateCategorySlug == "consumer-electronics"`
   - Result: ❌ **ZERO PRODUCTS** (all still have old slug)

2. **Landing Page Display:**
   - Product landing shows "Electronics" (stale snapshot)
   - Category tree shows "Consumer Electronics" (current)
   - Result: ❌ **INCONSISTENT UX**

3. **SEO Impact:**
   - Old slug `electronics` in URLs
   - New slug `consumer-electronics` in sitemap
   - Result: ❌ **BROKEN LINKS**

### Consistency Model Options

**Option A: Event-Driven Projection Update** (Recommended)

```csharp
// 1. Emit event when category updated
public async Task<ActionResult<CategoryResponse>> UpdateCategory(...)
{
    category.UpdateName(request.Name);
    category.UpdateSlug(request.Slug);
    await _dbContext.SaveChangesAsync(ct);

    // Emit event
    await _eventPublisher.PublishAsync(new AffiliateCategoryUpdatedEvent
    {
        CategoryId = category.Id,
        NewName = category.Name,
        NewSlug = category.Slug,
        OldName = oldName,  // captured before update
        OldSlug = oldSlug,
        ParentCategoryName = parentName,
        ParentCategorySlug = parentSlug
    }, ct);
}

// 2. Consumer updates all affected products
public class AffiliateCategoryUpdatedConsumer : IConsumer<AffiliateCategoryUpdatedEvent>
{
    public async Task Consume(ConsumeContext<AffiliateCategoryUpdatedEvent> context)
    {
        var evt = context.Message;
        
        // Update all products with this category
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(
            d => d.AffiliateCategoryId, evt.CategoryId);
        
        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .Set(d => d.AffiliateCategoryName, evt.NewName)
            .Set(d => d.AffiliateCategorySlug, evt.NewSlug)
            .Set(d => d.AffiliateParentCategoryName, evt.ParentCategoryName)
            .Set(d => d.AffiliateParentCategorySlug, evt.ParentCategorySlug)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow);
        
        await _mongoDbContext.AffiliateProductIntelligence.UpdateManyAsync(
            filter, update, cancellationToken: context.CancellationToken);
    }
}
```

**Pros:**
- ✅ Eventual consistency maintained
- ✅ All projections updated automatically
- ✅ Discover filtering works correctly
- ✅ SEO links remain valid

**Cons:**
- ⚠️ Requires new event + consumer
- ⚠️ Eventual consistency delay (seconds)

**Option B: Snapshot Model (Accept Drift)** (NOT RECOMMENDED)

Document as intentional behavior:
- Category name/slug frozen in product projection
- Rename does NOT propagate to existing products
- Only new policy changes update category snapshot

**Pros:**
- ✅ No code changes needed
- ✅ Historical accuracy preserved

**Cons:**
- ❌ Discover filtering broken after rename
- ❌ Inconsistent UX
- ❌ SEO links broken
- ❌ Violates user expectations

**Option C: Disallow Rename** (NOT RECOMMENDED)

Prevent category name/slug changes after creation.

**Pros:**
- ✅ No projection drift possible

**Cons:**
- ❌ Poor UX (typos cannot be fixed)
- ❌ Inflexible taxonomy
- ❌ Requires category deletion + recreation

### Projection Consistency Score: 🔴 **20/100**

**Critical Issues:**
- ❌ No event emitted on category update
- ❌ No projection update mechanism
- ❌ Silent drift after category rename
- ❌ Discover filtering breaks after rename
- ❌ No documented consistency model

**Mitigating Factors:**
- ⚠️ Category renames expected to be rare
- ⚠️ Manual projection rebuild possible (but not automated)

**Recommendation:**
1. Implement `AffiliateCategoryUpdatedEvent`
2. Create `AffiliateCategoryUpdatedConsumer` to update projections
3. Add integration test for rename scenario
4. Document consistency model in architecture docs

---

## 🧱 SECTION 4 — FINAL SAFETY DECLARATION

### Scoring Summary

| Section | Score | Status | Blocker |
|---------|-------|--------|---------|
| 1. Transaction Atomicity | 30/100 | 🔴 | YES |
| 2. Inactive Category Enable Guard | 40/100 | 🔴 | YES |
| 3. Projection Consistency on Rename | 20/100 | 🔴 | YES |
| **Overall Safety** | **55/100** | 🔴 | **YES** |

### Critical Blockers

🔴 **3 CRITICAL BLOCKERS IDENTIFIED**

**Blocker #1: Transaction Atomicity**
- **Risk:** Partial state corruption (DB committed, projection stale)
- **Severity:** HIGH
- **Likelihood:** MEDIUM (event publish failures)
- **Impact:** Silent data corruption, product invisible to affiliates
- **Fix Effort:** 2-4 hours (add explicit transaction + retry logic)

**Blocker #2: Inactive Category Enable Guard**
- **Risk:** Can enable affiliate with deactivated category
- **Severity:** HIGH
- **Likelihood:** LOW (requires specific sequence)
- **Impact:** Violates business rule, inconsistent taxonomy
- **Fix Effort:** 1-2 hours (add IsActive validation)

**Blocker #3: Projection Consistency on Rename**
- **Risk:** Silent drift after category rename
- **Severity:** CRITICAL
- **Likelihood:** HIGH (category renames will happen)
- **Impact:** Discover broken, SEO broken, inconsistent UX
- **Fix Effort:** 4-8 hours (implement event + consumer + tests)

### Regression Risk Assessment

**Checkout Pipeline:** ✅ **ZERO RISK**
- No checkout code modified
- No category fields in Order entity

**Commission Engine:** ✅ **ZERO RISK**
- Commission calculation unchanged
- Category used only for marketing

**Ledger System:** ✅ **ZERO RISK**
- No ledger code modified
- No category fields in ledger entries

**Order Pipeline:** ✅ **ZERO RISK**
- No order processing modified
- Category not used in order flow

**Existing Affiliate Features:** ⚠️ **MEDIUM RISK**
- Legacy enable endpoint vulnerable to inactive category
- Event publish failures cause projection drift

### Go/No-Go Decision

**DECISION: 🔴 NO-GO**

**Rationale:**
- 3 critical blockers identified
- High risk of data corruption
- High risk of projection drift
- Medium risk of business rule violation

**Required Fixes Before Frontend Integration:**

1. **MUST FIX (Blocker #1):**
   - Add explicit transaction wrapping to `EnableAffiliateWithCategoryAsync()`
   - Commit transaction before event publish
   - Add retry logic for event publish failures

2. **MUST FIX (Blocker #2):**
   - Add IsActive validation to `EnableAffiliateForProductAsync()`
   - Fetch category and validate before calling `Enable()`

3. **MUST FIX (Blocker #3):**
   - Implement `AffiliateCategoryUpdatedEvent`
   - Create `AffiliateCategoryUpdatedConsumer` to update projections
   - Add integration test for rename scenario

**Estimated Fix Time:** 8-14 hours

**Re-Audit Required:** YES (after fixes applied)

---

## 📋 REQUIRED FIXES CHECKLIST

### Fix #1: Transaction Atomicity (4 hours)

**File:** `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

**Method:** `EnableAffiliateWithCategoryAsync()`

```csharp
public async Task<AffiliatePolicyWithCategoryDetails> EnableAffiliateWithCategoryAsync(...)
{
    var tenantId = TenantId;

    // Validate category (before transaction)
    var category = await _context.AffiliateCategories
        .FirstOrDefaultAsync(c => c.Id == categoryId, ct);
    
    if (category == null)
        throw new InvalidOperationException($"Affiliate category {categoryId} not found");
    
    if (!category.IsActive)
        throw new InvalidOperationException($"Affiliate category {categoryId} is not active");

    // BEGIN TRANSACTION
    await using var transaction = await _context.Database.BeginTransactionAsync(ct);
    
    try
    {
        var existing = await _context.AffiliateProductPolicies
            .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

        if (existing != null)
        {
            existing.UpdateRate(commissionRate, maxPerItem);
            existing.UpdateCategory(categoryId);
            existing.Enable();
        }
        else
        {
            var policy = AffiliateProductPolicy.Create(tenantId, productId, commissionRate, maxPerItem, categoryId);
            _context.AffiliateProductPolicies.Add(policy);
        }

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        
        // Event published AFTER commit (at-least-once delivery)
        // NOTE: This should be moved to controller layer
        
        return new AffiliatePolicyWithCategoryDetails { ... };
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}
```

**Testing:**
- Unit test: Verify transaction rollback on Enable() failure
- Integration test: Verify event publish failure doesn't corrupt DB

### Fix #2: Inactive Category Enable Guard (2 hours)

**File:** `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

**Method:** `EnableAffiliateForProductAsync()`

```csharp
public async Task<AffiliateProductPolicy> EnableAffiliateForProductAsync(...)
{
    var existing = await _context.AffiliateProductPolicies
        .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

    if (existing != null)
    {
        existing.UpdateRate(commissionRate, maxPerItem);
        
        // AF-30A: Enforce category requirement before enabling
        if (existing.AffiliateCategoryId == null)
        {
            throw new InvalidOperationException(
                "Affiliate category is required. Assign a category before enabling affiliate marketing.");
        }
        
        // AF-30A: Validate category is active
        var category = await _context.AffiliateCategories
            .FirstOrDefaultAsync(c => c.Id == existing.AffiliateCategoryId.Value, ct);
        
        if (category == null)
            throw new InvalidOperationException($"Affiliate category {existing.AffiliateCategoryId.Value} not found");
        
        if (!category.IsActive)
        {
            _logger.LogWarning(
                "[AF-30A:InactiveCategory] Cannot enable affiliate for Product {ProductId} with inactive category {CategoryId}",
                productId, existing.AffiliateCategoryId.Value);
            throw new InvalidOperationException(
                $"Affiliate category {existing.AffiliateCategoryId.Value} is not active. " +
                "Assign an active category before enabling affiliate marketing.");
        }
        
        existing.Enable();
        await _context.SaveChangesAsync(ct);
        
        return existing;
    }
    
    throw new InvalidOperationException(
        "Affiliate category is required. Use UpdateCategoryForProductAsync() first to assign a category.");
}
```

**Testing:**
- Unit test: Verify enable fails with inactive category
- Integration test: Verify deactivate → enable sequence fails

### Fix #3: Projection Consistency on Rename (8 hours)

**Step 1: Create Event** (1 hour)

**File:** `PointOfSale.Core/Events/Affiliate/AffiliateEvents.cs`

```csharp
/// <summary>
/// AF-30A: Emitted when an affiliate category is updated (name, slug, or parent).
/// Consumers should update MongoDB projections to maintain consistency.
/// </summary>
public class AffiliateCategoryUpdatedEvent : IntegrationEvent
{
    public Guid CategoryId { get; set; }
    public string NewName { get; set; } = string.Empty;
    public string NewSlug { get; set; } = string.Empty;
    public string? OldName { get; set; }
    public string? OldSlug { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentCategoryName { get; set; }
    public string? ParentCategorySlug { get; set; }
}
```

**Step 2: Emit Event** (2 hours)

**File:** `PointOfSale.API/Controllers/AdminAffiliateCategoryController.cs`

```csharp
[HttpPut("{id:guid}")]
public async Task<ActionResult<CategoryResponse>> UpdateCategory(
    Guid id, [FromBody] UpdateAffiliateCategoryDetailsRequest request, CancellationToken ct)
{
    var category = await _dbContext.AffiliateCategories
        .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

    if (category == null)
        return NotFound(new { message = "Category not found" });

    // Capture old values for event
    var oldName = category.Name;
    var oldSlug = category.Slug;

    try
    {
        if (!string.IsNullOrWhiteSpace(request.Name))
            category.UpdateName(request.Name);

        if (!string.IsNullOrWhiteSpace(request.Slug))
            category.UpdateSlug(request.Slug);

        if (request.DisplayOrder.HasValue)
            category.UpdateDisplayOrder(request.DisplayOrder.Value);

        await _dbContext.SaveChangesAsync(ct);

        // Fetch parent details if subcategory
        string? parentName = null;
        string? parentSlug = null;
        if (category.ParentId.HasValue)
        {
            var parent = await _dbContext.AffiliateCategories
                .Where(c => c.Id == category.ParentId.Value)
                .Select(c => new { c.Name, c.Slug })
                .FirstOrDefaultAsync(ct);

            if (parent != null)
            {
                parentName = parent.Name;
                parentSlug = parent.Slug;
            }
        }

        // Emit event for projection update
        await _eventPublisher.PublishAsync(new AffiliateCategoryUpdatedEvent
        {
            CategoryId = category.Id,
            NewName = category.Name,
            NewSlug = category.Slug,
            OldName = oldName,
            OldSlug = oldSlug,
            ParentId = category.ParentId,
            ParentCategoryName = parentName,
            ParentCategorySlug = parentSlug
        }, ct);

        _logger.LogInformation(
            "[AF-30A] Category updated: {Id} → {Name} ({Slug})",
            id, category.Name, category.Slug);

        return Ok(new CategoryResponse { ... });
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}
```

**Step 3: Create Consumer** (3 hours)

**File:** `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateCategoryUpdatedConsumer.cs`

```csharp
public class AffiliateCategoryUpdatedConsumer : IConsumer<AffiliateCategoryUpdatedEvent>
{
    private readonly IMongoDbContext _mongoDbContext;
    private readonly ILogger<AffiliateCategoryUpdatedConsumer> _logger;

    public async Task Consume(ConsumeContext<AffiliateCategoryUpdatedEvent> context)
    {
        var evt = context.Message;
        
        _logger.LogInformation(
            "[AF-30A:CategoryUpdated] Updating projections for category {CategoryId}: {OldName} → {NewName}",
            evt.CategoryId, evt.OldName, evt.NewName);

        // Update all products with this category
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(
            d => d.AffiliateCategoryId, evt.CategoryId);
        
        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .Set(d => d.AffiliateCategoryName, evt.NewName)
            .Set(d => d.AffiliateCategorySlug, evt.NewSlug)
            .Set(d => d.AffiliateParentCategoryName, evt.ParentCategoryName)
            .Set(d => d.AffiliateParentCategorySlug, evt.ParentCategorySlug)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow);
        
        var result = await _mongoDbContext.AffiliateProductIntelligence.UpdateManyAsync(
            filter, update, cancellationToken: context.CancellationToken);
        
        _logger.LogInformation(
            "[AF-30A:CategoryUpdated] Updated {Count} product projections for category {CategoryId}",
            result.ModifiedCount, evt.CategoryId);
    }
}
```

**Step 4: Integration Test** (2 hours)

```csharp
[Fact]
public async Task CategoryRename_UpdatesAllProductProjections()
{
    // Arrange: Create category and assign to products
    var categoryId = await CreateCategory("Electronics", "electronics");
    var product1Id = await CreateProductWithCategory(categoryId);
    var product2Id = await CreateProductWithCategory(categoryId);
    
    // Act: Rename category
    await UpdateCategory(categoryId, "Consumer Electronics", "consumer-electronics");
    await Task.Delay(1000); // Wait for eventual consistency
    
    // Assert: Verify projections updated
    var projection1 = await GetMongoProjection(product1Id);
    var projection2 = await GetMongoProjection(product2Id);
    
    Assert.Equal("Consumer Electronics", projection1.AffiliateCategoryName);
    Assert.Equal("consumer-electronics", projection1.AffiliateCategorySlug);
    Assert.Equal("Consumer Electronics", projection2.AffiliateCategoryName);
    Assert.Equal("consumer-electronics", projection2.AffiliateCategorySlug);
}
```

---

## 🎯 CONCLUSION

AF-30A has 3 critical architectural violations that MUST be fixed before frontend integration:

1. **Transaction Atomicity:** No explicit transaction wrapping (partial state corruption risk)
2. **Inactive Category Guard:** Missing IsActive validation in legacy enable endpoint
3. **Projection Consistency:** No event-driven update mechanism for category rename

**Gate Status:** 🔴 **BLOCKED**

**Estimated Fix Time:** 8-14 hours

**Next Steps:**
1. Apply required fixes (checklist above)
2. Run integration tests
3. Re-audit with AF-30A Final Structural Gate Audit v2
4. Only then proceed to AF-30B Frontend Integration

---

**Report Generated:** 2026-02-27  
**Phase:** AF-30A Final Structural Gate Audit  
**Status:** 🔴 BLOCKED  
**Next Phase:** Fix critical issues, then re-audit
