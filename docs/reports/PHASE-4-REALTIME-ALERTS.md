# 🚨 Phase 4: Realtime Business Alerts

## Status: ✅ COMPLETE

**Implementation Date:** 2026-01-20

---

## 🎯 Phase 4 Objective

Detect business-critical situations in near-realtime and notify admins automatically.

> "Something important is happening right now — you should know."

---

## 🏗️ Architecture Overview

```
Analytics Events (Phase 1)
        ↓
Redis Realtime Counters
        ↓
AnalyticsAlertEvaluator (Background Job - 60s)
        ↓
Triggered Alerts
        ├── Redis (active alerts)
        └── MongoDB (audit log)
        ↓
Admin API (read-only)
```

### No Frontend Changes Required ✅
### No GA/Meta Dependency ✅
### No AI/ML ✅

---

## 🧠 Alert Types

All alerts are **threshold-based signals**, not insights or predictions.

| Alert | Condition | Severity |
|-------|-----------|----------|
| Zero Sales | `PurchaseCompleted == 0` over 30 min | Critical |
| Payment Failures | `PaymentFailed > 5` over 5 min | Critical |
| High Abandonment | `CartAbandonmentRate > 80%` over 15 min | Warning |
| No Traffic | `PageViews == 0` over 10 min | Critical |
| Low Conversion | `ConversionRate < 1%` over 60 min | Warning |

---

## 📦 Core Domain Models

### 1️⃣ Alert Rule

```csharp
public class AnalyticsAlertRule
{
    public Guid RuleId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; }
    public AnalyticsMetric Metric { get; set; }
    public AnalyticsComparisonOperator Operator { get; set; }
    public decimal Threshold { get; set; }
    public int WindowMinutes { get; set; }
    public AnalyticsAlertSeverity Severity { get; set; }
    public bool IsEnabled { get; set; }
    public int CooldownMinutes { get; set; }
}
```

### 2️⃣ Metric Enumeration

```csharp
public enum AnalyticsMetric
{
    PageViews,
    ProductViews,
    AddToCart,
    BeginCheckout,
    PurchaseCompleted,
    PaymentFailed,
    CartAbandonmentRate,    // Derived
    ConversionRate,         // Derived
    ProductToCartRate       // Derived
}
```

### 3️⃣ Comparison Operators

```csharp
public enum AnalyticsComparisonOperator
{
    GreaterThan,
    LessThan,
    Equal,
    GreaterThanOrEqual,
    LessThanOrEqual
}
```

### 4️⃣ Triggered Alert

```csharp
public class AnalyticsAlert
{
    public Guid AlertId { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public AnalyticsMetric Metric { get; set; }
    public decimal ActualValue { get; set; }
    public decimal Threshold { get; set; }
    public DateTime TriggeredAtUtc { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
```

---

## 🔢 Metrics Source

All metrics computed from Redis counters written in Phase 1:

```
analytics:tenant:{id}:counter:pageview:{hour}
analytics:tenant:{id}:counter:addtocart:{hour}
analytics:tenant:{id}:counter:purchasecompleted:{hour}
analytics:tenant:{id}:counter:paymentfailed:{hour}
```

### Rules
- ✅ Sliding time windows ONLY
- ❌ NO MongoDB reads during evaluation
- ❌ NO recomputation from raw events

---

## ⚙️ Alert Evaluation Engine

**Service:** `AnalyticsAlertEvaluatorService`

| Property | Value |
|----------|-------|
| Interval | 60 seconds |
| Scope | All tenants with enabled rules |
| Behavior | Stateless & idempotent |
| Failure Mode | Graceful (log and continue) |

### Evaluation Pseudocode

```csharp
foreach (tenant in tenantsWithRules)
{
    foreach (rule in enabledRules)
    {
        var value = redis.GetMetric(rule.Metric, rule.Window);

        if (Compare(value, rule.Operator, rule.Threshold))
        {
            if (!AlertAlreadyActive(rule) && !InCooldown(rule))
            {
                TriggerAlert(rule, value);
                SetCooldown(rule);
            }
        }
        else
        {
            ResolveAlertIfExists(rule);
        }
    }
}
```

---

## 🌐 Admin API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/admin/analytics/alerts/active` | Current active alerts |
| GET | `/api/admin/analytics/alerts/history` | Alert history |
| GET | `/api/admin/analytics/alerts/summary` | Alert counts by severity |
| GET | `/api/admin/analytics/alerts/rules` | Get all rules |
| GET | `/api/admin/analytics/alerts/rules/{id}` | Get specific rule |
| POST | `/api/admin/analytics/alerts/rules` | Create rule |
| PUT | `/api/admin/analytics/alerts/rules/{id}` | Update rule |
| DELETE | `/api/admin/analytics/alerts/rules/{id}` | Delete rule |
| PUT | `/api/admin/analytics/alerts/rules/{id}/toggle` | Enable/disable rule |
| GET | `/api/admin/analytics/alerts/metrics` | Get current metric values |

**Security:**
- 🔒 Admin authentication required
- 🔒 Tenant-isolated

---

## 📁 Files Created

### Core Layer
- `PointOfSale.Core/Analytics/AnalyticsAlertEnums.cs`
- `PointOfSale.Core/Analytics/AnalyticsAlertRule.cs`
- `PointOfSale.Core/Analytics/AnalyticsAlert.cs`

### Infrastructure Layer
- `PointOfSale.Infrastructure/Analytics/Alerts/IAnalyticsMetricReader.cs`
- `PointOfSale.Infrastructure/Analytics/Alerts/AnalyticsMetricReader.cs`
- `PointOfSale.Infrastructure/Analytics/Alerts/IAnalyticsAlertStore.cs`
- `PointOfSale.Infrastructure/Analytics/Alerts/AnalyticsAlertStore.cs`
- `PointOfSale.Infrastructure/Analytics/Alerts/IAnalyticsAlertRuleService.cs`
- `PointOfSale.Infrastructure/Analytics/Alerts/AnalyticsAlertRuleService.cs`
- `PointOfSale.Infrastructure/Analytics/Alerts/AnalyticsAlertEvaluatorService.cs`

### API Layer
- `PointOfSale.API/Controllers/AdminAnalyticsAlertsController.cs`

### Database
- `PointOfSale.Infrastructure/add_analytics_alert_rules.sql`

---

## 🧪 Acceptance Criteria

| Criteria | Status |
|----------|--------|
| Alerts trigger within 60-120 seconds | ✅ |
| Alerts do NOT duplicate | ✅ (via cooldown) |
| Alerts auto-resolve when condition clears | ✅ |
| No frontend deployment required | ✅ |
| Redis failure does not crash system | ✅ |
| MongoDB failure does not block evaluation | ✅ |
| System is multi-tenant safe | ✅ |
| Metrics are reproducible from counters | ✅ |

---

## 🚫 Hard Fail Conditions - VERIFIED CLEAN

| Condition | Status |
|-----------|--------|
| Alerts derived from GA/Meta | ❌ NOT PRESENT |
| Alerts computed in frontend | ❌ NOT PRESENT |
| AI/prediction logic | ❌ NOT PRESENT |
| Websocket/push dependency | ❌ NOT PRESENT |
| Alerts without audit trail | ❌ NOT PRESENT |

---

## 📊 Usage Examples

### Create a Zero Sales Alert

```bash
curl -X POST https://api.example.com/api/admin/analytics/alerts/rules \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -d '{
    "name": "Zero Sales Alert",
    "description": "Alert when no purchases in 30 minutes",
    "metric": 4,
    "operator": 3,
    "threshold": 0,
    "windowMinutes": 30,
    "severity": 3,
    "cooldownMinutes": 30,
    "isEnabled": true
  }'
```

### Get Active Alerts

```bash
curl https://api.example.com/api/admin/analytics/alerts/active \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"
```

### Response

```json
[
  {
    "alertId": "abc123...",
    "ruleId": "def456...",
    "ruleName": "Zero Sales Alert",
    "metric": "PurchaseCompleted",
    "actualValue": 0,
    "threshold": 0,
    "operator": "Equal",
    "severity": "Critical",
    "conditionDescription": "PurchaseCompleted == 0 over 30 minutes",
    "triggeredAtUtc": "2026-01-20T17:30:00Z",
    "isResolved": false,
    "isActive": true
  }
]
```

### Get Current Metrics (for rule testing)

```bash
curl "https://api.example.com/api/admin/analytics/alerts/metrics?windowMinutes=60" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"
```

### Response

```json
{
  "windowMinutes": 60,
  "metrics": {
    "PageViews": 1234,
    "ProductViews": 567,
    "AddToCart": 89,
    "BeginCheckout": 34,
    "PurchaseCompleted": 12,
    "PaymentFailed": 2,
    "CartAbandonmentRate": 64.71,
    "ConversionRate": 0.97,
    "ProductToCartRate": 15.70
  }
}
```

---

## 📋 Recommended Default Rules

| Rule Name | Metric | Operator | Threshold | Window | Severity |
|-----------|--------|----------|-----------|--------|----------|
| Zero Sales | PurchaseCompleted | == | 0 | 30 min | Critical |
| Payment Failures | PaymentFailed | > | 5 | 5 min | Critical |
| High Abandonment | CartAbandonmentRate | > | 80 | 15 min | Warning |
| No Traffic | PageViews | == | 0 | 10 min | Critical |
| Low Conversion | ConversionRate | < | 1 | 60 min | Warning |

---

## 🏁 Phase 4 Success Statement

> **"The POS system can now proactively warn merchants when business-critical situations occur — in real time — without relying on any external analytics provider."**

---

## ⏭️ Future Enhancements (NOT IMPLEMENTED)

- Email/SMS notifications for critical alerts
- Slack/Discord webhooks
- Alert escalation rules
- Alert acknowledgment workflow
- Historical trend analysis

**DO NOT IMPLEMENT without explicit approval.**

---

**Phase 4 Complete. Merchants now have proactive business intelligence. ✅**
