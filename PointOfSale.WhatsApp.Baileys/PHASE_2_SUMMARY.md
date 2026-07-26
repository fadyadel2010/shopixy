# Phase 2: Observability, Control & Operability

## 🎯 Objective

Transform the Phase 1 stable foundation into a **fully observable, debuggable, and operable** system. Phase 2 ensures that operators can see exactly what's happening, trace issues end-to-end, control sessions proactively, and receive alerts before problems cascade.

---

## ✅ Completion Status

| Pillar | Status | Implementation |
|--------|--------|----------------|
| **1. Prometheus Metrics** | ✅ Complete | `MetricsService.ts` with comprehensive metrics |
| **2. Correlation & Traceability** | ✅ Complete | `sessionId`, `socketId`, `authStateHash` in logs & responses |
| **3. Health & Control Endpoints** | ✅ Complete | `/admin/*` routes + enhanced `/health` |
| **4. Operational Safety** | ✅ Complete | Alert detection in `/admin/alerts` |

---

## 📊 Pillar 1: Prometheus Metrics

### Metrics Implemented

#### QR Flow Metrics
| Metric | Type | Labels | Purpose |
|--------|------|--------|---------|
| `whatsapp_qr_generation_total` | Counter | `tenant_id` | Total QR generation attempts |
| `whatsapp_qr_success_total` | Counter | `tenant_id` | Successful QR generations |
| `whatsapp_qr_timeout_total` | Counter | `tenant_id` | QR generation timeouts |
| `whatsapp_qr_duration_seconds` | Histogram | `tenant_id`, `result` | QR generation duration |

#### Session Lifecycle Metrics
| Metric | Type | Labels | Purpose |
|--------|------|--------|---------|
| `whatsapp_session_state_total` | Gauge | `state` | Current sessions by state |
| `whatsapp_session_reconnect_attempts_total` | Counter | `tenant_id` | Total reconnect attempts |
| `whatsapp_session_poisoned_total` | Counter | `tenant_id`, `status_code` | Sessions that became poisoned |
| `whatsapp_session_lifetime_seconds` | Histogram | `tenant_id`, `disconnect_reason` | Session lifetime before disconnect |
| `whatsapp_session_reconnect_storm_total` | Gauge | - | Sessions with 3+ reconnect attempts |
| `whatsapp_session_stuck_total` | Gauge | - | Sessions stuck > 30s |

#### Socket Health Metrics
| Metric | Type | Labels | Purpose |
|--------|------|--------|---------|
| `whatsapp_socket_disconnect_total` | Counter | `tenant_id`, `reason`, `status_code` | Socket disconnections |
| `whatsapp_socket_econnreset_total` | Counter | `tenant_id` | ECONNRESET errors |
| `whatsapp_socket_replaced_during_send_total` | Counter | `tenant_id` | Socket replaced during send |
| `whatsapp_socket_active_total` | Gauge | - | Current active sockets |

#### Messaging Metrics
| Metric | Type | Labels | Purpose |
|--------|------|--------|---------|
| `whatsapp_message_send_total` | Counter | `tenant_id`, `result` | Total send attempts |
| `whatsapp_message_latency_seconds` | Histogram | `tenant_id`, `result` | Send latency |
| `whatsapp_message_failures_by_state` | Counter | `tenant_id`, `state`, `reason` | Failures by session state |

#### Operational Metrics
| Metric | Type | Labels | Purpose |
|--------|------|--------|---------|
| `whatsapp_service_uptime_seconds` | Gauge | - | Service uptime |
| `whatsapp_admin_force_reset_total` | Counter | `tenant_id` | Admin force-reset operations |

### Usage

```bash
# Scrape metrics
curl http://localhost:3001/metrics

# Example Prometheus scrape config
scrape_configs:
  - job_name: 'baileys'
    static_configs:
      - targets: ['localhost:3001']
```

---

## 🔍 Pillar 2: Correlation & Traceability

### New Correlation IDs

| ID | Format | Purpose | Persisted |
|----|--------|---------|-----------|
| `sessionId` | UUID | Unique per connect attempt, enables end-to-end tracing | ✅ Redis |
| `socketId` | `sock_<timestamp>_<random>` | Unique per socket instance | ❌ Memory only |
| `authStateHash` | First 8 chars of SHA256 | Track auth state changes without exposing secrets | ✅ Redis |

### Enhanced Logging

All session operations now include correlation IDs:

```json
{
  "level": "info",
  "time": "2024-01-15T10:30:00.000Z",
  "tenantId": "tenant-123",
  "sessionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "socketId": "sock_l9k8j7h6_abc123",
  "socketVersion": 3,
  "msg": "Session connected successfully"
}
```

### API Response Enhancements

All session-related endpoints now return correlation IDs:

```json
// GET /sessions/:tenantId/status
{
  "success": true,
  "status": "connected",
  "sessionId": "a1b2c3d4-...",
  "socketId": "sock_l9k8j7h6_abc123",
  "authStateHash": "a1b2c3d4",
  "lastError": null,
  "connectedAt": "2024-01-15T10:30:00.000Z"
}
```

---

## 🎛️ Pillar 3: Health & Control Endpoints

### New Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/health` | GET | Enhanced health check with diagnostics |
| `/ready` | GET | Kubernetes readiness probe |
| `/live` | GET | Kubernetes liveness probe |
| `/metrics` | GET | Prometheus metrics |
| `/admin/sessions` | GET | List all sessions with details |
| `/admin/sessions/:tenantId` | GET | Get detailed session info |
| `/admin/sessions/:tenantId/force-reset` | POST | Force reset session |
| `/admin/diagnostics` | GET | System diagnostics |
| `/admin/alerts` | GET | Current alert conditions |

### Enhanced Health Check (`/health`)

```json
{
  "status": "healthy",
  "version": "PHASE_2_OBSERVABLE",
  "activeSessions": 5,
  "sessionsByStatus": {
    "connected": 3,
    "disconnected": 1,
    "qr_pending": 1
  },
  "poolSessions": 0,
  "poolMembers": 0,
  "uptime": 3600.123,
  "stuckSessions": 0,
  "reconnectStormsDetected": 0,
  "poisonedSessions": 0,
  "activeSockets": 3
}
```

### Admin Session Listing (`/admin/sessions`)

```json
{
  "success": true,
  "totalSessions": 2,
  "sessionsByStatus": { "connected": 1, "disconnected": 1 },
  "stuckSessions": 0,
  "reconnectStormsDetected": 0,
  "poisonedSessions": 0,
  "sessions": [
    {
      "tenantId": "tenant-123",
      "sessionId": "a1b2c3d4-...",
      "socketId": "sock_...",
      "status": "connected",
      "ageSeconds": 3600,
      "stuckSeconds": 0,
      "reconnectAttempts": 0,
      "lastError": null,
      "authStateHash": "abc12345",
      "hasSocket": true,
      "connectedAt": "2024-01-15T10:30:00.000Z"
    }
  ]
}
```

### Force Reset (`/admin/sessions/:tenantId/force-reset`)

Nuclear option for stuck/poisoned sessions:

```bash
curl -X POST http://localhost:3001/admin/sessions/tenant-123/force-reset \
  -H "X-API-Key: your-api-key"
```

Response:
```json
{
  "success": true,
  "message": "Session force reset complete. Next /connect will require fresh QR scan.",
  "previousStatus": "poisoned",
  "previousSessionId": "a1b2c3d4-...",
  "tenantId": "tenant-123"
}
```

---

## 🚨 Pillar 4: Operational Safety & Alerts

### Alert Thresholds

| Condition | Threshold | Severity |
|-----------|-----------|----------|
| QR Timeout Rate | > 5% | Warning |
| Reconnect Attempts | >= 3 | Warning |
| Reconnect Attempts (multiple sessions) | > 3 sessions | Critical |
| Stuck Sessions | > 30 seconds | Warning |
| Poisoned Sessions | > 0 | Warning |
| Poisoned Sessions | > 5 | Critical |

### Alert Endpoint (`/admin/alerts`)

```json
{
  "success": true,
  "status": "warning",
  "alertCount": 2,
  "alerts": [
    {
      "severity": "warning",
      "type": "STUCK_SESSIONS",
      "message": "2 session(s) stuck in CONNECTING/QR_PENDING for > 30s",
      "affectedSessions": ["tenant-123", "tenant-456"],
      "count": 2
    },
    {
      "severity": "warning",
      "type": "RECONNECT_STORM",
      "message": "1 session(s) with >= 3 reconnect attempts",
      "affectedSessions": ["tenant-789"],
      "count": 1
    }
  ],
  "checkedAt": "2024-01-15T10:30:00.000Z"
}
```

---

## 📁 Files Modified/Created

### Created
- `src/services/MetricsService.ts` - Prometheus metrics service
- `src/routes/admin.ts` - Admin control endpoints

### Modified
- `src/models/Session.ts` - Added correlation ID fields
- `src/services/SessionManager.ts` - Metrics integration, correlation IDs, health diagnostics
- `src/services/MessageSender.ts` - Message send metrics
- `src/routes/sessions.ts` - QR metrics, correlation IDs in responses
- `src/index.ts` - /metrics, /ready, /live endpoints, admin routes
- `src/middleware/auth.ts` - Exempted observability endpoints from auth

---

## 🧪 Verification Checklist

### TypeScript Compilation
```bash
npx tsc --noEmit  # ✅ Passes
```

### Manual Test Scenarios

1. **Metrics Scraping**
   ```bash
   curl http://localhost:3001/metrics
   # Should return Prometheus text format
   ```

2. **Health Check**
   ```bash
   curl http://localhost:3001/health
   # Should include sessionsByStatus, stuckSessions, etc.
   ```

3. **Admin Sessions**
   ```bash
   curl -H "X-API-Key: $BAILEYS_API_KEY" http://localhost:3001/admin/sessions
   # Should list all sessions with correlation IDs
   ```

4. **Force Reset**
   ```bash
   curl -X POST -H "X-API-Key: $BAILEYS_API_KEY" \
     http://localhost:3001/admin/sessions/test-tenant/force-reset
   # Should reset session and require fresh QR
   ```

5. **Alerts Check**
   ```bash
   curl -H "X-API-Key: $BAILEYS_API_KEY" http://localhost:3001/admin/alerts
   # Should return alert status
   ```

---

## 📈 Grafana Dashboard Queries

### QR Success Rate
```promql
rate(whatsapp_qr_success_total[5m]) / rate(whatsapp_qr_generation_total[5m]) * 100
```

### Message Success Rate
```promql
rate(whatsapp_message_send_total{result="success"}[5m]) / 
rate(whatsapp_message_send_total[5m]) * 100
```

### Reconnect Storm Detection
```promql
whatsapp_session_reconnect_storm_total > 0
```

### Average Message Latency (p95)
```promql
histogram_quantile(0.95, rate(whatsapp_message_latency_seconds_bucket[5m]))
```

### Session State Distribution
```promql
whatsapp_session_state_total
```

---

## 🔮 Next Steps (Phase 3 - Future)

1. **Distributed Tracing** - OpenTelemetry integration for cross-service traces
2. **Real-time WebSocket Events** - Push notifications for session state changes
3. **Automated Recovery** - Self-healing based on alert conditions
4. **Rate Limiting** - Per-tenant message rate limits with metrics
5. **Audit Logging** - Full audit trail for compliance

---

## 📋 Dependencies Added (Phase 2)

```json
{
  "dependencies": {
    "prom-client": "^15.1.0",
    "uuid": "^9.0.0"
  },
  "devDependencies": {
    "@types/uuid": "^9.0.0"
  }
}
```

---

## 🏷️ Version Marker

- **Phase**: `PHASE_2_OBSERVABLE`
- **Health Check Version**: `/health` returns `"version": "PHASE_2_OBSERVABLE"`
- **Startup Log**: Service logs `Phase: PHASE_2_OBSERVABLE` on startup
