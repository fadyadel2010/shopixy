# 📊 Phase 3: Analytics Dispatchers & Admin Realtime Visibility

## Status: ✅ COMPLETE

**Implementation Date:** 2026-01-20

---

## 🎯 Phase 3 Objective

Connect POS analytics events to external providers WITHOUT COUPLING:
- **Google Analytics 4** (Measurement Protocol - server-side only)
- **Meta Conversions API** (server-side only)
- **Admin Realtime Dashboard** (internal visibility)

The POS system owns analytics end-to-end and can broadcast events to any analytics provider without changing storefront code.

---

## 🏗️ Architecture Overview

```
Storefront SDK (Phase 2)
       ↓
POS Analytics Core (Phase 1)
       ↓
AnalyticsEventConsumer
       ↓
┌─────────────────────────────┐
│ AnalyticsDispatcherPipeline │
└─────────────────────────────┘
       ↓
   ┌────────────┬─────────────┬───────────────┐
   │ GA4        │ Meta CAPI   │ Realtime Admin │
   │ Dispatcher │ Dispatcher  │ Dashboard     │
   └────────────┴─────────────┴───────────────┘
```

---

## 🔑 Core Components

### 1️⃣ Dispatcher Abstraction

**Interface:** `IAnalyticsDispatcher`

```csharp
public interface IAnalyticsDispatcher
{
    AnalyticsProvider Provider { get; }
    int Priority { get; }
    Task<AnalyticsDispatchResult> DispatchAsync(
        AnalyticsEvent analyticsEvent,
        TenantAnalyticsSettings? settings,
        CancellationToken cancellationToken);
    bool IsEnabled(TenantAnalyticsSettings? settings);
}
```

**Rules:**
- Dispatchers are **STATELESS**
- Dispatchers **NEVER mutate** events
- Dispatchers **NEVER block** ingestion
- Dispatch failures are logged but don't throw

### 2️⃣ Dispatcher Implementations

| Dispatcher | Provider | Priority | Purpose |
|------------|----------|----------|---------|
| `AdminRealtimeDispatcher` | Internal | 0 | Updates Redis realtime metrics |
| `GoogleAnalyticsDispatcher` | GA4 | 10 | Server-side GA4 Measurement Protocol |
| `MetaConversionsApiDispatcher` | Meta | 20 | Server-side Meta CAPI |

### 3️⃣ Dispatcher Pipeline

**Class:** `AnalyticsDispatcherPipeline`

```csharp
public class AnalyticsDispatcherPipeline
{
    Task<List<AnalyticsDispatchResult>> DispatchToAllAsync(AnalyticsEvent analyticsEvent);
}
```

**Responsibilities:**
- Resolves enabled dispatchers per tenant
- Fire-and-forget dispatch (runs after storage)
- Captures failures per provider
- Never fails the main pipeline

---

## 📊 Google Analytics 4 Dispatcher

### Protocol
- **Endpoint:** `https://www.google-analytics.com/mp/collect`
- **Debug:** `https://www.google-analytics.com/debug/mp/collect`
- **Auth:** Measurement ID + API Secret

### Event Mapping

| POS Event | GA4 Event |
|-----------|-----------|
| PageView | page_view |
| ProductView | view_item |
| ProductImpression | view_item_list |
| AddToCart | add_to_cart |
| RemoveFromCart | remove_from_cart |
| BeginCheckout | begin_checkout |
| PurchaseCompleted | purchase |
| SearchPerformed | search |
| FilterApplied | select_item |
| PaymentFailed | purchase_error (custom) |

### Security Rules
- ✅ Client ID = SessionId (no cookies)
- ✅ NO gtag.js on frontend
- ✅ NO frontend GA code
- ✅ GA config stored per tenant (encrypted)

---

## 📱 Meta Conversions API Dispatcher

### Protocol
- **Endpoint:** `https://graph.facebook.com/v18.0/{pixel-id}/events`
- **Auth:** Access Token

### Event Mapping

| POS Event | Meta Event |
|-----------|------------|
| PageView | PageView |
| ProductView | ViewContent |
| AddToCart | AddToCart |
| BeginCheckout | InitiateCheckout |
| PurchaseCompleted | Purchase |

### Security Rules
- ✅ Hash PII server-side (SHA256)
- ✅ Deduplicate using EventId
- ✅ NO Meta Pixel on frontend
- ✅ Pixel ID never exposed to client

---

## 📈 Realtime Admin Dashboard

### Data Source
- Redis Hot Store (Phase 1)
- Read-only (no DB writes)
- Polling-based (API endpoints)

### Dashboard Widgets

| Widget | Data |
|--------|------|
| Active Users (last 5 min) | Session count |
| Page Views (real-time) | PageView events |
| Add To Cart (live) | AddToCart events |
| Checkout Started | BeginCheckout events |
| Purchases | PurchaseCompleted events |
| Conversion Funnel | Derived metrics |

### API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/admin/analytics/realtime` | Dashboard data |
| GET | `/api/admin/analytics/funnel` | Conversion funnel |
| GET | `/api/admin/analytics/events` | Recent events |
| GET | `/api/admin/analytics/settings` | Provider settings |
| PUT | `/api/admin/analytics/settings` | Update settings |
| PUT | `/api/admin/analytics/settings/{provider}/toggle` | Toggle provider |

---

## ⚙️ Tenant-Level Controls

### Settings Model

```json
{
  "analytics": {
    "google": {
      "enabled": true,
      "measurementId": "G-XXXX",
      "apiSecret": "********"
    },
    "meta": {
      "enabled": false,
      "pixelId": "123456",
      "accessToken": "********"
    }
  }
}
```

### Storage
- **PostgreSQL:** Source of truth (encrypted secrets)
- **Redis:** Cache for fast reads (5-minute TTL)
- **Encryption:** Data Protection API

### Rules
- ✅ Secrets encrypted at rest
- ✅ Toggleable per provider
- ✅ Changes take effect immediately

---

## 🔄 Event Pipeline (Updated)

```
StorefrontAnalyticsController
       ↓
AnalyticsEventValidator
       ↓
EventBus (MassTransit) → Publish(AnalyticsEventReceivedEvent)
       ↓
AnalyticsEventConsumer
       │
       ├── Redis Store (Hot) - Always attempted first
       ├── MongoDB Store (Cold) - Always attempted
       │
       ↓ (Fire-and-forget, after storage)
AnalyticsDispatcherPipeline
       │
       ├── AdminRealtimeDispatcher (priority 0)
       ├── GoogleAnalyticsDispatcher (priority 10)
       └── MetaConversionsApiDispatcher (priority 20)
```

---

## 📁 Files Created

### Core Layer
- `PointOfSale.Core/Analytics/AnalyticsProvider.cs`
- `PointOfSale.Core/Analytics/AnalyticsDispatchResult.cs`
- `PointOfSale.Core/Analytics/TenantAnalyticsSettings.cs`

### Infrastructure Layer
- `PointOfSale.Infrastructure/Analytics/Dispatchers/IAnalyticsDispatcher.cs`
- `PointOfSale.Infrastructure/Analytics/Dispatchers/AdminRealtimeDispatcher.cs`
- `PointOfSale.Infrastructure/Analytics/Dispatchers/GoogleAnalyticsDispatcher.cs`
- `PointOfSale.Infrastructure/Analytics/Dispatchers/MetaConversionsApiDispatcher.cs`
- `PointOfSale.Infrastructure/Analytics/Dispatchers/AnalyticsDispatcherPipeline.cs`
- `PointOfSale.Infrastructure/Analytics/Dispatchers/IAnalyticsSettingsService.cs`
- `PointOfSale.Infrastructure/Analytics/Dispatchers/AnalyticsSettingsService.cs`

### API Layer
- `PointOfSale.API/Services/Analytics/IAnalyticsRealtimeService.cs`
- `PointOfSale.API/Services/Analytics/AnalyticsRealtimeService.cs`
- `PointOfSale.API/Controllers/AdminAnalyticsController.cs`

### Updated
- `PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs` (integrated pipeline)
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` (TenantAnalyticsConfiguration)
- `PointOfSale.API/Program.cs` (service registrations)

---

## 🔐 Security Guarantees

| Rule | Enforced |
|------|----------|
| No GA script on storefront | ✅ |
| No Meta Pixel on storefront | ✅ |
| No provider sees raw IP | ✅ (anonymized) |
| No provider sees auth tokens | ✅ |
| Tenant isolation | ✅ |
| Idempotency preserved | ✅ (EventId) |
| Secrets encrypted at rest | ✅ |

---

## 🧪 Acceptance Criteria

| Criteria | Status |
|----------|--------|
| Google Analytics receives events | ✅ |
| Meta receives events | ✅ |
| Admin sees realtime analytics | ✅ |
| Turning off GA stops GA events | ✅ |
| Frontend unchanged | ✅ |
| No provider logic in SDK | ✅ |
| EventId deduplication works | ✅ |
| Zero coupling introduced | ✅ |

---

## 🚫 Hard Fail Conditions - VERIFIED CLEAN

- ❌ gtag.js added → **NOT PRESENT**
- ❌ Meta Pixel added → **NOT PRESENT**
- ❌ Frontend talks to GA/Meta → **NOT PRESENT**
- ❌ Provider-specific fields in core models → **NOT PRESENT**
- ❌ Dispatch failures breaking ingestion → **NOT PRESENT**

---

## 🏁 Phase 3 Success Statement

> **"The POS system owns analytics end-to-end and can broadcast events to any analytics provider or internal dashboard without changing storefront code."**

---

## 📊 Usage Examples

### Enable Google Analytics for Tenant

```bash
curl -X PUT https://api.example.com/api/admin/analytics/settings \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -d '{
    "google": {
      "enabled": true,
      "measurementId": "G-XXXXXXXXXX",
      "apiSecret": "your-api-secret"
    }
  }'
```

### Get Realtime Dashboard

```bash
curl https://api.example.com/api/admin/analytics/realtime \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"
```

### Response

```json
{
  "generatedAtUtc": "2026-01-20T15:30:00Z",
  "activeUsersLast5Min": 47,
  "pageViewsThisHour": 1234,
  "addToCartsThisHour": 89,
  "checkoutsStartedThisHour": 34,
  "purchasesThisHour": 12,
  "conversionRate": 35.29,
  "eventCountsByType": {
    "PageView": 1234,
    "ProductView": 567,
    "AddToCart": 89,
    "BeginCheckout": 34,
    "PurchaseCompleted": 12
  },
  "recentEvents": [...]
}
```

---

## ⏭️ Phase 4 Preview (OPTIONAL - NOT IMPLEMENTED)

**Future Enhancements:**
- AI-based funnel anomaly detection
- Predictive churn alerts
- Heatmap generation (server-side)
- Campaign ROI attribution per tenant
- Fraud detection using analytics signals

**DO NOT IMPLEMENT PHASE 4 without explicit approval.**

---

**Phase 3 Complete. Analytics system now supports external provider dispatch with full admin visibility.**
