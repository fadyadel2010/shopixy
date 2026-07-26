# Phase 7: Attribution Engine (Marketing ROI) — MVP

**Status:** ✅ COMPLETE  
**Date:** 2026-01-21

## 🎯 Objective

Build a deterministic Attribution Engine that answers: **"This order came from where?"**

With:
- ✅ POS as the single source of truth
- ✅ Server-side only
- ❌ No cookies
- ❌ No GA UI dependency
- ❌ No Meta UI dependency
- ❌ No frontend business logic

## 🧠 Core Attribution Principle

Attribution is derived from **POS-owned analytics events**, NOT from external providers.

| System | Role |
|--------|------|
| Google Analytics & Meta | Receive events (downstream) |
| POS | **Owns truth**, **Owns sessions**, **Owns orders** |

## 🏗️ Architecture Overview

```
Storefront
   ↓
Analytics SDK (Phase 2)
   ↓
POS Analytics Core (Phase 1)
   ↓
AnalyticsEventConsumer
   ├── AttributionContextResolver (PageView → UTM capture)
   └── OrderAttributionResolver (PurchaseCompleted → order attribution)
   ↓
Attribution Stores (Redis + MongoDB)
   ↓
Admin Attribution API (Read-only)
```

## 📁 Files Created

### Core Models (`PointOfSale.Core/Analytics/Attribution/`)

| File | Description |
|------|-------------|
| `AttributionModel.cs` | Enum: FirstTouch, LastTouch |
| `AttributionContext.cs` | Session-level UTM capture model |
| `OrderAttribution.cs` | Order-level attribution model |

### MongoDB Documents (`PointOfSale.Infrastructure/MongoDB/Documents/`)

| File | Description |
|------|-------------|
| `AttributionContextDocument.cs` | MongoDB document for attribution contexts |
| `OrderAttributionDocument.cs` | MongoDB document for order attributions |

### Attribution Infrastructure (`PointOfSale.Infrastructure/Attribution/`)

| File | Description |
|------|-------------|
| `IAttributionContextStore.cs` | Interface for context storage (first/last touch) |
| `AttributionContextStore.cs` | Implementation with Redis (hot) + MongoDB (cold) |
| `IOrderAttributionStore.cs` | Interface for order attribution storage |
| `OrderAttributionStore.cs` | Implementation with MongoDB + Redis cache |
| `AttributionContextResolver.cs` | Extracts UTM params from PageView events |
| `OrderAttributionResolver.cs` | Links orders to marketing sources |

### API Controller (`PointOfSale.API/Controllers/`)

| File | Description |
|------|-------------|
| `AdminAnalyticsAttributionController.cs` | Admin API for attribution data |

### Modified Files

| File | Change |
|------|--------|
| `AnalyticsEventConsumer.cs` | Added attribution processing hook |
| `Program.cs` | Registered attribution services |

## 🔄 Attribution Flow

### 1️⃣ PageView Event (UTM Capture)

```
PageView event received
       ↓
AttributionContextResolver
       ↓
Extract UTM params from URL:
  - utm_source → Source
  - utm_medium → Medium
  - utm_campaign → Campaign
  - utm_content → Content
  - utm_term → Term
       ↓
Store AttributionContext:
  - Redis: first/last touch (TTL: 30 days)
  - MongoDB: append-only (permanent)
```

### 2️⃣ PurchaseCompleted Event (Order Attribution)

```
PurchaseCompleted event received
       ↓
OrderAttributionResolver
       ↓
Get session attribution (both models):
  - FirstTouch: earliest context for session
  - LastTouch: latest context for session
       ↓
Create OrderAttribution records
       ↓
Persist to MongoDB (immutable)
Cache in Redis (7 days)
```

## 📡 Admin API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/admin/analytics/attribution/orders` | Orders with attribution |
| GET | `/api/admin/analytics/attribution/summary` | Revenue by source |
| GET | `/api/admin/analytics/attribution/campaigns` | Campaign ROI |
| GET | `/api/admin/analytics/attribution/models` | Compare First vs Last touch |
| GET | `/api/admin/analytics/attribution/sessions/{sessionId}` | Session touchpoints |

### Example: Revenue Summary Response

```json
{
  "period": "last_7_days",
  "startDate": "2026-01-14T00:00:00Z",
  "endDate": "2026-01-21T00:00:00Z",
  "model": "LastTouch",
  "totalRevenue": 23000.00,
  "totalOrders": 92,
  "revenueBySource": [
    { "source": "facebook", "medium": "cpc", "revenue": 12000, "orders": 45, "averageOrderValue": 266.67, "percentageOfTotal": 52.17 },
    { "source": "google", "medium": "organic", "revenue": 8000, "orders": 32, "averageOrderValue": 250.00, "percentageOfTotal": 34.78 },
    { "source": "direct", "medium": "none", "revenue": 3000, "orders": 15, "averageOrderValue": 200.00, "percentageOfTotal": 13.04 }
  ]
}
```

### Example: Model Comparison Response

```json
{
  "period": "last_30_days",
  "firstTouch": {
    "model": "FirstTouch",
    "totalRevenue": 50000.00,
    "totalOrders": 200,
    "topSources": [
      { "source": "google", "medium": "cpc", "revenue": 25000, "orders": 100 },
      { "source": "facebook", "medium": "social", "revenue": 15000, "orders": 60 }
    ]
  },
  "lastTouch": {
    "model": "LastTouch",
    "totalRevenue": 50000.00,
    "totalOrders": 200,
    "topSources": [
      { "source": "direct", "medium": "none", "revenue": 20000, "orders": 80 },
      { "source": "google", "medium": "cpc", "revenue": 18000, "orders": 72 }
    ]
  }
}
```

## 🧠 Storage Strategy

| Store | Purpose | TTL |
|-------|---------|-----|
| Redis (Hot) | Session lookup, realtime dashboards | 30 days (context), 7 days (orders) |
| MongoDB (Cold) | Historical record, aggregations | Permanent |

### Redis Key Patterns

```
attribution:tenant:{tenantId}:session:{sessionId}:first
attribution:tenant:{tenantId}:session:{sessionId}:last
attribution:tenant:{tenantId}:order:{orderId}:{model}
```

### MongoDB Collections

- `attribution_contexts` - Session UTM captures (append-only)
- `order_attributions` - Order-level attributions (immutable)

## 🔐 Security Guarantees

| Requirement | Status |
|-------------|--------|
| No cookies used | ✅ |
| No frontend attribution logic | ✅ |
| SessionId is POS-owned | ✅ |
| Attribution immutable after order | ✅ |
| Tenant isolation | ✅ |
| Provider-independent | ✅ |

## 🚫 Hard Fail Conditions (STRICT)

- ❌ Attribution based on GA data
- ❌ Reading Meta reports
- ❌ Frontend calculating attribution
- ❌ Editing attribution after order completion
- ❌ Silent fallback logic

## ✅ Acceptance Criteria

| Check | Status |
|-------|--------|
| Order shows correct source | ✅ |
| First/Last models differ correctly | ✅ |
| Direct traffic handled | ✅ |
| Multiple campaigns tracked | ✅ |
| GA/Meta toggling does not affect attribution | ✅ |
| Attribution works without GA/Meta enabled | ✅ |

## 🧪 Testing Instructions

### 1. Simulate Campaign Traffic

Visit storefront with UTM parameters:
```
https://your-store.com/?utm_source=facebook&utm_medium=cpc&utm_campaign=summer_sale
```

### 2. Complete a Purchase

Add items to cart and complete checkout.

### 3. Verify Attribution

Call the API:
```bash
curl -X GET "https://api.your-store.com/api/admin/analytics/attribution/orders?period=today" \
  -H "Authorization: Bearer {admin_token}" \
  -H "X-Tenant-Id: {tenantId}"
```

### 4. Compare Models

```bash
curl -X GET "https://api.your-store.com/api/admin/analytics/attribution/models?period=last_7_days" \
  -H "Authorization: Bearer {admin_token}" \
  -H "X-Tenant-Id: {tenantId}"
```

## 🏁 MVP Success Statement

> "The POS system can accurately answer where every order came from, using its own data, without relying on external analytics providers."

## ⏭️ Phase 8 (Optional – NOT IMPLEMENTED)

- Multi-touch attribution
- Campaign cost input (ROI %)
- Funnel-based attribution
- AI insights
- Export to accounting systems

---

**SUCCESS STATEMENT:**  
"The Attribution Engine provides deterministic, server-side marketing attribution that is completely independent of GA/Meta, uses POS-owned session IDs, and creates immutable order attribution records for accurate ROI analysis."
