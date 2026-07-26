# 📊 Phase 4B: Realtime Conversion Funnels

## Status: ✅ COMPLETE

**Implementation Date:** 2026-01-20

---

## 🎯 Objective

Build **Realtime Conversion Funnels** that allow POS Admins to understand where users drop off in the buying journey, in near-realtime, without relying on Google Analytics UI.

> "Users entered here… how many reached the next step?"

---

## 🏗️ Architecture Overview

```
Analytics Events (Phase 1)
        ↓
Redis Realtime Counters
        ↓
AnalyticsFunnelService
        ↓
Admin API (Read-only)
        ↓
Admin Dashboard (Charts / Tables)
```

### Key Constraints
- ❌ NO MongoDB reads during calculation
- ❌ NO raw event reprocessing
- ❌ NO frontend funnel logic
- ✅ Redis-only for performance
- ✅ Server-side computation only

---

## 📦 Pre-Defined Funnels

### Funnel A — Purchase Funnel
```
PageView → ProductView → AddToCart → BeginCheckout → PurchaseCompleted
```
**Purpose:** Track the complete journey from page view to purchase.

### Funnel B — Discovery Funnel
```
PageView → ProductImpression → ProductView → AddToCart
```
**Purpose:** Track how users discover and engage with products.

### Funnel C — Checkout Funnel
```
BeginCheckout → PurchaseCompleted
```
**Purpose:** Track checkout completion rate.

### Funnel D — Cart Funnel
```
ProductView → AddToCart → BeginCheckout
```
**Purpose:** Track product-to-cart conversion.

### Funnel E — Search Funnel
```
SearchPerformed → ProductView → AddToCart → PurchaseCompleted
```
**Purpose:** Track search-to-purchase journey.

---

## 📦 Domain Models

### Funnel Step Definition
```csharp
public class AnalyticsFunnelStep
{
    public int Order { get; set; }              // Step order
    public AnalyticsEventType EventType { get; set; }
    public string Name { get; set; }            // Display name
}
```

### Funnel Definition
```csharp
public class AnalyticsFunnelDefinition
{
    public string FunnelKey { get; set; }       // e.g. "purchase_funnel"
    public string Name { get; set; }            // "Purchase Funnel"
    public IReadOnlyList<AnalyticsFunnelStep> Steps { get; set; }
    public bool IsSystem { get; set; }
}
```

### Funnel Result
```csharp
public class AnalyticsFunnelResult
{
    public string FunnelKey { get; set; }
    public int WindowMinutes { get; set; }
    public List<AnalyticsFunnelStepResult> Steps { get; set; }
    public decimal OverallConversionRate { get; set; }
}

public class AnalyticsFunnelStepResult
{
    public string Name { get; set; }
    public long Count { get; set; }
    public decimal ConversionFromPrevious { get; set; }  // %
    public decimal DropOffFromPrevious { get; set; }     // %
    public decimal ConversionFromEntry { get; set; }     // %
    public long DropOffCount { get; set; }
}
```

---

## ⚙️ Computation Rules

### Time Windows
| Window | Label |
|--------|-------|
| 15 | Last 15 minutes |
| 30 | Last 30 minutes |
| 60 | Last hour |
| 180 | Last 3 hours |
| 360 | Last 6 hours |
| 720 | Last 12 hours |
| 1440 | Last 24 hours |

### Metrics Source
Each step reads from Redis counters:
```
analytics:tenant:{id}:counter:{eventType}:{hour}
```

### Conversion Rate Formula
```
conversion_rate = (step_n / step_(n-1)) * 100
```

### Drop-off Rate Formula
```
drop_off_rate = 100 - conversion_rate
```

### First Step
- Conversion: 100% (entry point)
- Drop-off: 0%

---

## 🌐 Admin API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/admin/analytics/funnels` | List all funnel definitions |
| GET | `/api/admin/analytics/funnels/{key}` | Get funnel definition |
| GET | `/api/admin/analytics/funnels/{key}/realtime` | Compute funnel metrics |
| GET | `/api/admin/analytics/funnels/all/realtime` | Compute all funnels |
| GET | `/api/admin/analytics/funnels/{key}/compare` | Compare across windows |

### Query Parameters
- `windowMinutes`: 15, 30, 60, 180, 360, 720, or 1440

### Security
- 🔒 Admin authentication required
- 🔒 Tenant isolation via X-Tenant-Id
- 🔒 Read-only (no data mutation)

---

## 📁 Files Created

### Core Layer (2 files)
- `PointOfSale.Core/Analytics/AnalyticsFunnelDefinition.cs`
- `PointOfSale.Core/Analytics/AnalyticsFunnelResult.cs`

### Infrastructure Layer (2 files)
- `PointOfSale.Infrastructure/Analytics/Funnels/IAnalyticsFunnelService.cs`
- `PointOfSale.Infrastructure/Analytics/Funnels/AnalyticsFunnelService.cs`

### API Layer (1 file)
- `PointOfSale.API/Controllers/AdminAnalyticsFunnelsController.cs`

---

## 📊 Usage Examples

### List Available Funnels

```bash
curl https://api.example.com/api/admin/analytics/funnels \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"
```

**Response:**
```json
[
  {
    "funnelKey": "purchase_funnel",
    "name": "Purchase Funnel",
    "description": "Track the complete journey from page view to purchase completion",
    "isSystem": true,
    "stepCount": 5,
    "steps": [
      { "order": 1, "name": "Page View", "eventType": "PageView" },
      { "order": 2, "name": "View Product", "eventType": "ProductView" },
      { "order": 3, "name": "Add to Cart", "eventType": "AddToCart" },
      { "order": 4, "name": "Begin Checkout", "eventType": "BeginCheckout" },
      { "order": 5, "name": "Purchase Completed", "eventType": "PurchaseCompleted" }
    ]
  }
]
```

### Compute Funnel (Last Hour)

```bash
curl "https://api.example.com/api/admin/analytics/funnels/purchase_funnel/realtime?windowMinutes=60" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"
```

**Response:**
```json
{
  "funnelKey": "purchase_funnel",
  "funnelName": "Purchase Funnel",
  "windowMinutes": 60,
  "windowLabel": "Last hour",
  "computedAtUtc": "2026-01-20T18:30:00Z",
  "entryCount": 1500,
  "completionCount": 25,
  "overallConversionRate": 1.67,
  "overallDropOffRate": 98.33,
  "steps": [
    {
      "order": 1,
      "name": "Page View",
      "eventType": "PageView",
      "count": 1500,
      "conversionFromPrevious": 100,
      "dropOffFromPrevious": 0,
      "conversionFromEntry": 100,
      "dropOffCount": 0
    },
    {
      "order": 2,
      "name": "View Product",
      "eventType": "ProductView",
      "count": 450,
      "conversionFromPrevious": 30,
      "dropOffFromPrevious": 70,
      "conversionFromEntry": 30,
      "dropOffCount": 1050
    },
    {
      "order": 3,
      "name": "Add to Cart",
      "eventType": "AddToCart",
      "count": 120,
      "conversionFromPrevious": 26.67,
      "dropOffFromPrevious": 73.33,
      "conversionFromEntry": 8,
      "dropOffCount": 330
    },
    {
      "order": 4,
      "name": "Begin Checkout",
      "eventType": "BeginCheckout",
      "count": 45,
      "conversionFromPrevious": 37.5,
      "dropOffFromPrevious": 62.5,
      "conversionFromEntry": 3,
      "dropOffCount": 75
    },
    {
      "order": 5,
      "name": "Purchase Completed",
      "eventType": "PurchaseCompleted",
      "count": 25,
      "conversionFromPrevious": 55.56,
      "dropOffFromPrevious": 44.44,
      "conversionFromEntry": 1.67,
      "dropOffCount": 20
    }
  ]
}
```

### Compare Across Windows

```bash
curl "https://api.example.com/api/admin/analytics/funnels/purchase_funnel/compare" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"
```

**Response:**
```json
{
  "funnelKey": "purchase_funnel",
  "funnelName": "Purchase Funnel",
  "windows": [
    { "windowMinutes": 15, "overallConversionRate": 2.5 },
    { "windowMinutes": 60, "overallConversionRate": 1.67 },
    { "windowMinutes": 1440, "overallConversionRate": 1.85 }
  ]
}
```

---

## 🧪 Acceptance Criteria

| Criteria | Status |
|----------|--------|
| Funnels computed from Redis only | ✅ |
| Supports multiple time windows | ✅ |
| Conversion & drop-off accurate | ✅ |
| Works for multiple tenants | ✅ |
| No frontend dependency | ✅ |
| No provider coupling | ✅ |

---

## 🚫 Hard Fail Conditions - VERIFIED CLEAN

| Condition | Status |
|-----------|--------|
| Using Google Analytics data | ❌ NOT PRESENT |
| Computing funnels in frontend | ❌ NOT PRESENT |
| Querying MongoDB | ❌ NOT PRESENT |
| Event reprocessing | ❌ NOT PRESENT |
| Hardcoded math in controllers | ❌ NOT PRESENT |

---

## 🏁 Success Statement

> **"Admins can now see exactly where customers drop off in the conversion journey — in real time — using POS-owned analytics, without relying on Google Analytics UI."**

---

## ⏭️ NOT IMPLEMENTED (Future)

- UI Dashboard with funnel visualizations
- Custom funnel creation API
- Funnel-based alerts
- A/B test funnel comparison
- Cohort-based funnels

**DO NOT IMPLEMENT without explicit approval.**

---

**Phase 4B Complete. Conversion funnels now available in realtime. ✅**
