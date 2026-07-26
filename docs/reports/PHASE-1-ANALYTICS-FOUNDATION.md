# 📊 Phase 1: Canonical Analytics Event Foundation

## Status: ✅ COMPLETE

**Implementation Date:** 2026-01-20

---

## 🎯 Phase 1 Objective

Create a canonical, internal analytics event system where the **POS platform becomes the single source of truth** for all storefront analytics events.

This phase is **FOUNDATIONAL** - no external analytics (Google / Meta) integration is allowed.

---

## 🏗️ Architecture Overview

```
Storefront (Browser)
       ↓
POST /api/storefront/analytics/event
       ↓
StorefrontAnalyticsController
       ↓
AnalyticsEventValidator (Server-side validation & enrichment)
       ↓
EventBus (MassTransit/RabbitMQ)
       ↓
AnalyticsEventConsumer
       ├── Redis Store (Hot) - Realtime, TTL-based
       └── MongoDB Store (Cold) - Historical, append-only
```

---

## 📦 Core Deliverables

### 1️⃣ Canonical Event Model

**File:** `PointOfSale.Core/Analytics/AnalyticsEvent.cs`

```csharp
public class AnalyticsEvent
{
    public Guid EventId { get; set; }           // Idempotency
    public Guid TenantId { get; set; }          // Isolation
    public AnalyticsEventType Type { get; set; } // What happened
    public DateTime OccurredAtUtc { get; set; }  // When
    public AnalyticsActor Actor { get; set; }    // Who
    public AnalyticsContext Context { get; set; } // Where / how
    public AnalyticsPayload Payload { get; set; } // What exactly
}
```

### 2️⃣ Event Type Enumeration

**File:** `PointOfSale.Core/Analytics/AnalyticsEventType.cs`

| Event Type | Business Value |
|------------|----------------|
| `PageView` | Traffic analysis |
| `ProductImpression` | Discovery funnel |
| `ProductView` | Product interest |
| `SearchPerformed` | Search behavior |
| `FilterApplied` | Navigation patterns |
| `AddToCart` | Purchase intent |
| `RemoveFromCart` | Cart abandonment |
| `BeginCheckout` | Conversion funnel |
| `PurchaseCompleted` | Revenue tracking |
| `PaymentFailed` | Drop-off analysis |

❌ NO GA-style events  
❌ NO UI-only events  
✅ Every event has business value

### 3️⃣ Actor Model (WHO)

**File:** `PointOfSale.Core/Analytics/AnalyticsActor.cs`

```csharp
public class AnalyticsActor
{
    public Guid? CustomerId { get; set; }    // Logged-in user (SERVER-SIDE from JWT)
    public string? SessionId { get; set; }   // Guest tracking (frontend-provided)
    public bool IsAuthenticated { get; set; } // SERVER-SIDE derived
}
```

**Security Rules:**
- CustomerId is NEVER trusted from frontend
- CustomerId is resolved from JWT token server-side
- SessionId validates format but accepts value for analytics

### 4️⃣ Context Model (WHERE / HOW)

**File:** `PointOfSale.Core/Analytics/AnalyticsContext.cs`

```csharp
public class AnalyticsContext
{
    public string PageUrl { get; set; }     // From frontend
    public string? Referrer { get; set; }   // From frontend
    public string UserAgent { get; set; }   // SERVER-SIDE captured
    public string IpAddress { get; set; }   // SERVER-SIDE captured & anonymized
    public string Language { get; set; }    // Accept-Language header
    public string Currency { get; set; }    // Tenant configuration
    public string DeviceType { get; set; }  // Derived from UserAgent
}
```

### 5️⃣ Payload Model (WHAT)

**File:** `PointOfSale.Core/Analytics/AnalyticsPayload.cs`

```csharp
public class AnalyticsPayload
{
    public Guid? ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public Guid? OrderId { get; set; }
    public decimal? Value { get; set; }
    public int? Quantity { get; set; }
    public string? SearchQuery { get; set; }
    public Dictionary<string, string>? Filters { get; set; }
    public Dictionary<string, object>? Extra { get; set; }
}
```

**Rules:**
- NO provider-specific fields
- NO GA event parameters
- Everything is POS-native

---

## 🌐 Event Ingestion API

### Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/storefront/analytics/event` | Single event |
| POST | `/api/storefront/analytics/events` | Batch events (max 50) |
| GET | `/api/storefront/analytics/health` | Health check |

### Requirements Met

- ✅ Accepts AnalyticsEvent
- ✅ Requires X-Tenant-Id
- ✅ Works for guest users
- ✅ Works for authenticated users
- ✅ Idempotent by EventId
- ✅ Non-blocking (async processing)
- ✅ Never throws on downstream failure
- ✅ Rate limiting enabled (100 req/min per session)

---

## 🧠 Storage Strategy

### Hot Storage (Redis)

**Service:** `AnalyticsHotStoreService`

| Key Pattern | Purpose | TTL |
|-------------|---------|-----|
| `analytics:tenant:{id}:event:{eventId}` | Event storage | 24h |
| `analytics:tenant:{id}:processed:{eventId}` | Idempotency | 7d |
| `analytics:tenant:{id}:counter:{name}:{window}` | Realtime counters | 25h |
| `analytics:tenant:{id}:recent:{type}` | Recent events (sorted set) | 1h |

**Used for:**
- Live dashboards
- Realtime counters
- Idempotency checking

### Cold Storage (MongoDB)

**Service:** `AnalyticsColdStoreService`  
**Collection:** `analytics_events`

| Index | Fields | Purpose |
|-------|--------|---------|
| Unique | (TenantId, EventId) | Idempotency |
| Compound | (TenantId, OccurredAtUtc) | Time-range queries |
| Compound | (TenantId, EventType, OccurredAtUtc) | Type-filtered queries |
| Compound | (TenantId, CustomerId, OccurredAtUtc) | Customer journey |
| Compound | (TenantId, SessionId, OccurredAtUtc) | Session tracking |

**Used for:**
- Historical analytics
- Aggregation queries
- BI/ML pipelines

---

## 🔄 Event Pipeline

```
StorefrontAnalyticsController
       ↓
AnalyticsEventValidator
       • Validate event type
       • Validate timestamp
       • Resolve CustomerId from JWT
       • Capture IP/UserAgent server-side
       • Anonymize IP address
       • Derive device type
       • Sanitize all inputs
       ↓
EventBus (MassTransit) → Publish(AnalyticsEventReceivedEvent)
       ↓
AnalyticsEventConsumer
       • Check idempotency (Redis)
       • Store in Redis (Hot)
       • Increment realtime counter
       • Store in MongoDB (Cold)
       • Mark as processed
       ↓
✅ Event stored in both stores
```

**Consumer Rules:**
- Controller NEVER writes directly to DB
- Consumer is idempotent
- Failure in MongoDB does NOT block Redis write
- Failure in Redis does NOT block MongoDB write
- Both stores are best-effort

---

## 🔐 Security & Safety

| Requirement | Implementation |
|-------------|----------------|
| TenantId validated | ✅ Rejected if empty |
| Actor identity server-side | ✅ CustomerId from JWT |
| IP captured server-side | ✅ From HttpContext |
| IP anonymized | ✅ Last octet zeroed |
| Rate limiting | ✅ 100 req/min per session |
| Payload size limits | ✅ Input sanitization |
| String length limits | ✅ Max lengths enforced |

---

## 📁 Files Created

### Core Layer
- `PointOfSale.Core/Analytics/AnalyticsEvent.cs`
- `PointOfSale.Core/Analytics/AnalyticsEventType.cs`
- `PointOfSale.Core/Analytics/AnalyticsActor.cs`
- `PointOfSale.Core/Analytics/AnalyticsContext.cs`
- `PointOfSale.Core/Analytics/AnalyticsPayload.cs`
- `PointOfSale.Core/Events/AnalyticsEvents.cs`

### Infrastructure Layer
- `PointOfSale.Infrastructure/MongoDB/Documents/AnalyticsEventDocument.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/IAnalyticsColdStoreService.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/AnalyticsColdStoreService.cs`
- `PointOfSale.Infrastructure/Redis/Services/IAnalyticsHotStoreService.cs`
- `PointOfSale.Infrastructure/Redis/Services/AnalyticsHotStoreService.cs`
- `PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs`

### API Layer
- `PointOfSale.API/DTOs/Analytics/AnalyticsEventDtos.cs`
- `PointOfSale.API/Services/Analytics/IAnalyticsEventValidator.cs`
- `PointOfSale.API/Services/Analytics/AnalyticsEventValidator.cs`
- `PointOfSale.API/Controllers/StorefrontAnalyticsController.cs`

### Updated
- `PointOfSale.API/Program.cs` (service registrations, consumer, rate limiting)

---

## 🧪 Acceptance Criteria

| Criteria | Status |
|----------|--------|
| Storefront can send analytics events | ✅ |
| Events are validated & accepted | ✅ |
| Events stored in Redis | ✅ |
| Events stored in MongoDB | ✅ |
| No Google Analytics code exists | ✅ |
| No Meta/Facebook code exists | ✅ |
| No frontend business logic added | ✅ |
| Multi-tenant isolation verified | ✅ |
| EventId idempotency verified | ✅ |

---

## 🚫 Hard Fail Conditions - VERIFIED CLEAN

- ❌ Any GA/Meta schema in core models → **NOT PRESENT**
- ❌ Frontend calling GA directly → **NOT IMPLEMENTED**
- ❌ Business logic in frontend → **NOT IMPLEMENTED**
- ❌ Tight coupling to analytics provider → **NOT PRESENT**
- ❌ Silent failures without logging → **ALL FAILURES LOGGED**

---

## 🏁 Phase 1 Success Statement

> **"The POS system fully owns analytics events and can later distribute them to any analytics provider without refactoring core logic."**

---

## ⏭️ Phase 2 Preview (NOT IMPLEMENTED YET)

**Storefront Analytics SDK**

Phase 2 will create a frontend SDK that:
- Generates EventIds for idempotency
- Manages SessionId in localStorage
- Buffers events for batching
- Handles offline/retry logic
- Provides typed event builders

**DO NOT IMPLEMENT PHASE 2 without explicit approval.**

---

## 📊 Usage Example

### Single Event (cURL)

```bash
curl -X POST https://api.example.com/api/storefront/analytics/event \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" \
  -d '{
    "eventId": "11111111-1111-1111-1111-111111111111",
    "type": "ProductView",
    "context": {
      "pageUrl": "https://store.example.com/products/widget"
    },
    "actor": {
      "sessionId": "sess_abc123"
    },
    "payload": {
      "productId": "22222222-2222-2222-2222-222222222222",
      "productName": "Premium Widget"
    }
  }'
```

### Response

```json
{
  "accepted": true,
  "eventId": "11111111-1111-1111-1111-111111111111"
}
```

---

## 📈 Metrics Available (Post-Implementation)

With Phase 1 complete, the following queries are now possible:

1. **Events by type per tenant** (MongoDB aggregation)
2. **Realtime counters** (Redis)
3. **Recent events stream** (Redis sorted sets)
4. **Customer journey** (MongoDB by CustomerId)
5. **Session analysis** (MongoDB by SessionId)
6. **Conversion funnel** (MongoDB by EventType sequence)

---

**Phase 1 Complete. Awaiting Phase 2 approval.**
