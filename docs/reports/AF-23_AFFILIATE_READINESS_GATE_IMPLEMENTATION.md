# AF-23: Affiliate Readiness Gate Implementation

**Status:** ✅ COMPLETE  
**Date:** 2026-02-25  
**Phase:** System-Level Safety Layer

---

## 🎯 OBJECTIVE

Implement a hard system-level gate that prevents usage of Affiliate features unless the tenant has completed required prerequisites:

1. **Storefront Status** = Live
2. **At least one ACTIVE Payment Method**
3. **At least one ACTIVE Shipping Method**

This rule is enforced:
- ✅ On backend (hard guard)
- ✅ On frontend (UX guidance via readiness endpoint)

---

## 🏗️ ARCHITECTURE

### Core Principles (MANDATORY)
- ✅ Additive only (no breaking changes)
- ✅ No refactors of existing AF-19 → AF-22 logic
- ✅ No schema changes
- ✅ No MongoDB write logic added
- ✅ Centralized logic (single source of truth)
- ✅ Backend-first enforcement

---

## 📦 DELIVERABLES

### 1. Core Model
**File:** `PointOfSale.Core/Models/Affiliate/AffiliateReadinessResult.cs`

```csharp
public class AffiliateReadinessResult
{
    public bool IsReady { get; set; }
    public bool StorefrontLive { get; set; }
    public bool HasPaymentMethod { get; set; }
    public bool HasShippingMethod { get; set; }
    public List<string> MissingRequirements { get; set; } = new();
}
```

### 2. Service Interface
**File:** `PointOfSale.Core/Interfaces/Affiliate/IAffiliateReadinessService.cs`

```csharp
public interface IAffiliateReadinessService
{
    Task<AffiliateReadinessResult> CheckAsync(Guid tenantId, CancellationToken ct = default);
}
```

### 3. Service Implementation
**File:** `PointOfSale.Infrastructure/Services/Affiliate/AffiliateReadinessService.cs`

**Logic:**
```csharp
// Check 1: Storefront Live (MongoDB)
storefrontConfig.StorefrontStatus?.ToLowerInvariant() == "live"

// Check 2: Active Payment Method (PostgreSQL)
_dbContext.PaymentMethods.Where(pm => pm.TenantId == tenantId && pm.IsActive).AnyAsync()

// Check 3: Active Shipping Method (PostgreSQL)
_dbContext.ShippingMethods.Where(sm => sm.TenantId == tenantId && sm.IsActive).AnyAsync()

// Final Readiness
IsReady = StorefrontLive && HasPaymentMethod && HasShippingMethod
```

**Logging:**
```
[AF-23] Affiliate readiness failed | TenantId: {TenantId} | Missing: {Missing}
```

### 4. Backend Enforcement Points

#### A) AdminProductAffiliateController
**File:** `PointOfSale.API/Controllers/AdminProductAffiliateController.cs`

**Guarded Endpoint:**
- `POST /api/admin/products/{productId}/affiliate/enable`

**Guard Logic:**
```csharp
// ─── AF-23: READINESS GATE ──────────────────────────────────────
var readiness = await _affiliateReadiness.CheckAsync(tenantId, ct);
if (!readiness.IsReady)
{
    return BadRequest(new
    {
        code = "AFFILIATE_NOT_READY",
        message = "Affiliate system is not ready",
        missing = readiness.MissingRequirements
    });
}
```

#### B) AdminAffiliateController
**File:** `PointOfSale.API/Controllers/AdminAffiliateController.cs`

**Guarded Endpoint:**
- `GET /api/admin/affiliates` (List affiliates)

**Guard Logic:** Same as above

### 5. Frontend Readiness Endpoint
**File:** `PointOfSale.API/Controllers/AdminAffiliateReadinessController.cs`

**Endpoint:**
```
GET /api/admin/affiliate/readiness
```

**Response:**
```json
{
  "isReady": false,
  "storefrontLive": true,
  "hasPaymentMethod": false,
  "hasShippingMethod": false,
  "missingRequirements": [
    "At least one active payment method required",
    "At least one active shipping method required"
  ]
}
```

### 6. Service Registration
**File:** `PointOfSale.API/Program.cs`

```csharp
// AF-23: Affiliate Readiness Gate Service
builder.Services.AddScoped<IAffiliateReadinessService, AffiliateReadinessService>();
```

---

## 🔐 BACKEND ENFORCEMENT SUMMARY

### Guarded Operations
1. ✅ **Enable Affiliate on Product** → `POST /api/admin/products/{productId}/affiliate/enable`
2. ✅ **List Affiliates** → `GET /api/admin/affiliates`

### Error Response
```json
{
  "code": "AFFILIATE_NOT_READY",
  "message": "Affiliate system is not ready",
  "missing": [
    "Storefront must be live",
    "At least one active payment method required",
    "At least one active shipping method required"
  ]
}
```

### Security Guarantees
- ❌ No Postman bypass
- ❌ No frontend bypass
- ❌ No corrupted states
- ✅ Backend authority enforced

---

## 🎨 FRONTEND INTEGRATION GUIDE

### Step 1: Check Readiness
```typescript
const response = await fetch('/api/admin/affiliate/readiness');
const readiness = await response.json();
```

### Step 2: Product Edit Modal
```typescript
if (!readiness.isReady) {
  // Disable affiliate toggle
  // Show warning card:
  // "Affiliate Marketing requires:"
  //   ✓ Storefront enabled
  //   ✗ Payment method
  //   ✗ Shipping method
  // 
  // Buttons:
  //   - Go to Storefront Settings
  //   - Go to Payment Methods
  //   - Go to Shipping Methods
}
```

### Step 3: Affiliate Management Page
```typescript
if (!readiness.isReady) {
  // Instead of table, show:
  // "Affiliate system is not ready"
  // "Complete the setup to start using affiliates"
  // Checklist + action buttons
}
```

---

## 🧪 TEST SCENARIOS

### Case 1: Storefront Not Live
```
Input:
  - Storefront Live = false
  - Payment exists
  - Shipping exists

Expected:
  - IsReady = false
  - Missing: ["Storefront must be live"]
  - Enable affiliate → 400 AFFILIATE_NOT_READY
```

### Case 2: No Payment Method
```
Input:
  - Storefront Live = true
  - No active payment
  - Shipping exists

Expected:
  - IsReady = false
  - Missing: ["At least one active payment method required"]
  - Enable affiliate → 400 AFFILIATE_NOT_READY
```

### Case 3: No Shipping Method
```
Input:
  - Storefront Live = true
  - Payment exists
  - No active shipping

Expected:
  - IsReady = false
  - Missing: ["At least one active shipping method required"]
  - Enable affiliate → 400 AFFILIATE_NOT_READY
```

### Case 4: All Prerequisites Met
```
Input:
  - Storefront Live = true
  - Payment exists
  - Shipping exists

Expected:
  - IsReady = true
  - Missing: []
  - Enable affiliate → 200 OK
```

### Case 5: Postman Bypass Attempt
```
Input:
  - Direct POST to /api/admin/products/{id}/affiliate/enable
  - Prerequisites not met

Expected:
  - 400 AFFILIATE_NOT_READY
  - No policy created
  - No event published
```

---

## 📊 LOGGING

### Success Case
```
[AF-23] Readiness check | TenantId: {TenantId} | IsReady: true
```

### Failure Case
```
[AF-23] Affiliate readiness failed | TenantId: {TenantId} | Missing: Payment, Shipping
```

---

## 🚫 EXPLICITLY FORBIDDEN

- ❌ Logic only in frontend
- ❌ Duplicate checks in multiple services
- ❌ Modify existing affiliate pipelines (AF-19 → AF-22)
- ❌ Change MongoDB projections
- ❌ Schema changes

---

## ✅ SUCCESS CRITERIA

1. ✅ Admin cannot enable affiliate unless system ready
2. ✅ Clear UI guidance via readiness endpoint
3. ✅ No regression in AF-19 → AF-22
4. ✅ No breaking changes
5. ✅ All guards enforced server-side
6. ✅ Centralized logic (single source of truth)
7. ✅ Backend-first enforcement

---

## 🔄 FUTURE ENHANCEMENTS (OPTIONAL)

### Global Kill-Switch
Add `TenantSetting`:
```csharp
public bool IsAffiliateFeatureEnabled { get; set; }
```

Final readiness:
```csharp
IsReady = IsReady && IsAffiliateFeatureEnabled
```

Allows global disable of affiliate features per tenant.

---

## 📝 IMPLEMENTATION NOTES

### Why This Design?

1. **Centralized:** Single service, single source of truth
2. **Reusable:** Any controller can inject and check
3. **Backend Authoritative:** No frontend bypass possible
4. **Prevents Corrupted States:** No partial affiliate setup
5. **Scales:** Easy to add more prerequisites later

### Data Sources

- **Storefront Status:** MongoDB (`StorefrontConfigDocument`)
- **Payment Methods:** PostgreSQL (`PaymentMethods` table)
- **Shipping Methods:** PostgreSQL (`ShippingMethods` table)

### No Caching

Readiness check is NOT cached because:
- Prerequisites can change frequently
- Check is fast (3 simple queries)
- Consistency is more important than performance

---

## 🎯 PHASE CLOSURE

AF-23 is a **system safety layer**, not a UI feature. It prevents:
- Incomplete affiliate setup
- Corrupted commission states
- Poor user experience
- Support tickets from broken flows

This feature is implemented with the same rigor as AF-20 and AF-22.

**Status:** ✅ PRODUCTION READY

---

## 📚 RELATED PHASES

- **AF-19:** Global Affiliate Identity
- **AF-20:** Affiliate Visibility Logic
- **AF-21:** Affiliate Order Lifecycle Projection
- **AF-22:** Affiliate Product Intelligence (MongoDB)
- **AF-23:** Affiliate Readiness Gate (THIS PHASE)

---

**Implementation Date:** 2026-02-25  
**Implemented By:** Kiro AI Assistant  
**Review Status:** Ready for Code Review
