# Phase 6: Deployment and Gradual Rollout - Checklist

## Overview

This checklist guides you through the safe deployment and gradual rollout of the Baileys WhatsApp service to production.

---

## Task 31: Prepare Deployment Configuration ✅

### Backend Configuration

- [x] Create `appsettings.Staging.json`
- [x] Create `appsettings.Production.json`
- [ ] Update `BaileysService:BaseUrl` with actual service URL
- [ ] Configure Redis connection string
- [ ] Set secure JWT secrets (minimum 32 characters)
- [ ] Configure RabbitMQ connection
- [ ] Review logging levels (Info for production)

### Baileys Service Configuration

- [x] Create `.env.staging` file
- [x] Create `.env.production` file
- [ ] Generate secure `AUTH_STATE_ENCRYPTION_SECRET` (32+ characters)
  ```bash
  node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"
  ```
- [ ] Configure Redis connection (host, port, password)
- [ ] Set `NODE_ENV=production`
- [ ] Configure pool settings (if using pool mode)
- [ ] Set appropriate `LOG_LEVEL` (info or warn for production)

### Deployment Scripts

- [x] Create `deploy-staging.sh`
- [x] Create `deploy-production.sh`
- [ ] Make scripts executable:
  ```bash
  chmod +x deploy-staging.sh deploy-production.sh
  ```

### SQL Scripts

- [x] Create `enable_baileys_gradual_rollout.sql`
- [ ] Review rollout strategy (10% → 25% → 50% → 100%)
- [ ] Prepare rollback scripts

---

## Task 32: Deploy Baileys Service to Staging

### Prerequisites

- [ ] Node.js 20.x or higher installed
- [ ] PM2 installed globally (`npm install -g pm2`)
- [ ] Redis server running and accessible
- [ ] Sufficient RAM (2-4 GB for testing)

### Deployment Steps

- [ ] Copy files to staging server
- [ ] Update `.env` with staging configuration
- [ ] Run deployment script:
  ```bash
  ./deploy-staging.sh
  ```
- [ ] Verify service is running:
  ```bash
  pm2 list
  pm2 logs whatsapp-baileys-staging
  ```

### Health Checks

- [ ] Verify health endpoint responds:
  ```bash
  curl http://localhost:3000/health
  ```
- [ ] Verify Redis connectivity:
  ```bash
  redis-cli -h YOUR_REDIS_HOST ping
  ```
- [ ] Check service logs for errors:
  ```bash
  pm2 logs whatsapp-baileys-staging --lines 50
  ```

### Basic Operations Test

- [ ] Connect a test session:
  ```bash
  curl -X POST http://localhost:3000/sessions/test-tenant/connect \
    -H "Content-Type: application/json" \
    -d '{"mode": "DEDICATED"}'
  ```
- [ ] Get QR code:
  ```bash
  curl http://localhost:3000/sessions/test-tenant/qr
  ```
- [ ] Scan QR code with WhatsApp
- [ ] Check session status:
  ```bash
  curl http://localhost:3000/sessions/test-tenant/status
  ```
- [ ] Send test message:
  ```bash
  curl -X POST http://localhost:3000/messages/send \
    -H "Content-Type: application/json" \
    -d '{
      "tenantId": "test-tenant",
      "phone": "+1234567890",
      "text": "Test message",
      "type": "text"
    }'
  ```
- [ ] Disconnect session:
  ```bash
  curl -X POST http://localhost:3000/sessions/test-tenant/disconnect
  ```

---

## Task 33: Deploy Backend Changes to Staging

### Prerequisites

- [ ] Baileys service is running and healthy
- [ ] Database migration applied (`add_use_baileys_feature_gate.sql`)
- [ ] All tests passing

### Deployment Steps

- [ ] Build backend:
  ```bash
  dotnet build PointOfSale.API/PointOfSale.API.csproj -c Release
  ```
- [ ] Run tests:
  ```bash
  dotnet test
  ```
- [ ] Deploy to staging server
- [ ] Update `appsettings.Staging.json` with correct `BaileysService:BaseUrl`
- [ ] Restart API service

### Verification

- [ ] Verify both transports registered in DI:
  ```bash
  # Check logs for transport registration
  grep "WhatsApp transport" /var/log/pointofsale-api.log
  ```
- [ ] Verify feature gate configuration loaded:
  ```sql
  SELECT "Id", "Name", "UseBaileys" FROM "Tenants" LIMIT 5;
  ```
- [ ] Test routing with feature gate disabled (Playwright):
  ```sql
  UPDATE "Tenants" SET "UseBaileys" = false WHERE "Id" = 'test-tenant-id';
  ```
  - Send test message
  - Verify logs show "Routing to Playwright transport"
- [ ] Test routing with feature gate enabled (Baileys):
  ```sql
  UPDATE "Tenants" SET "UseBaileys" = true WHERE "Id" = 'test-tenant-id';
  ```
  - Send test message
  - Verify logs show "Routing to Baileys transport"

---

## Task 34: Enable Baileys for Pilot Tenants

### Select Pilot Tenants

- [ ] Identify 2-3 pilot tenants with:
  - Low to medium message volume
  - Active WhatsApp usage
  - Responsive to issues
  - Willing to provide feedback

### Enable Feature Gate

- [ ] Enable Baileys for pilot tenants:
  ```sql
  UPDATE "Tenants"
  SET "UseBaileys" = true, "UpdatedAtUtc" = NOW()
  WHERE "Id" IN ('pilot-tenant-1', 'pilot-tenant-2', 'pilot-tenant-3');
  ```
- [ ] Verify feature gate:
  ```sql
  SELECT "Id", "Name", "UseBaileys" FROM "Tenants" WHERE "UseBaileys" = true;
  ```

### Monitoring (24-48 hours)

- [ ] Monitor message delivery success rate:
  ```sql
  SELECT 
    t."UseBaileys",
    COUNT(*) AS total,
    COUNT(*) FILTER (WHERE wml."Status" = 'SENT') AS sent,
    ROUND(COUNT(*) FILTER (WHERE wml."Status" = 'SENT') * 100.0 / COUNT(*), 2) AS success_rate
  FROM "WhatsAppMessageLogs" wml
  JOIN "Tenants" t ON t."Id"::text = wml."TenantId"
  WHERE wml."CreatedAtUtc" >= NOW() - INTERVAL '24 hours'
  GROUP BY t."UseBaileys";
  ```
- [ ] Monitor latency:
  ```sql
  SELECT 
    t."UseBaileys",
    AVG(EXTRACT(EPOCH FROM (wml."UpdatedAtUtc" - wml."CreatedAtUtc"))) AS avg_latency_seconds
  FROM "WhatsAppMessageLogs" wml
  JOIN "Tenants" t ON t."Id"::text = wml."TenantId"
  WHERE wml."CreatedAtUtc" >= NOW() - INTERVAL '24 hours'
    AND wml."Status" = 'SENT'
  GROUP BY t."UseBaileys";
  ```
- [ ] Monitor error rates:
  ```sql
  SELECT 
    t."Id", t."Name", t."UseBaileys",
    COUNT(*) AS total,
    COUNT(*) FILTER (WHERE wml."Status" = 'FAILED') AS failed,
    ROUND(COUNT(*) FILTER (WHERE wml."Status" = 'FAILED') * 100.0 / COUNT(*), 2) AS error_rate
  FROM "WhatsAppMessageLogs" wml
  JOIN "Tenants" t ON t."Id"::text = wml."TenantId"
  WHERE wml."CreatedAtUtc" >= NOW() - INTERVAL '24 hours'
  GROUP BY t."Id", t."Name", t."UseBaileys"
  HAVING COUNT(*) FILTER (WHERE wml."Status" = 'FAILED') * 100.0 / COUNT(*) > 5
  ORDER BY error_rate DESC;
  ```
- [ ] Check Baileys service memory usage:
  ```bash
  pm2 monit
  ```
- [ ] Review logs for errors:
  ```bash
  pm2 logs whatsapp-baileys --lines 100
  ```

### Success Criteria

- [ ] Message delivery success rate ≥ 95%
- [ ] No increase in error rate vs Playwright baseline
- [ ] Latency comparable to Playwright (< 5 seconds p95)
- [ ] No critical errors in logs
- [ ] Pilot tenants report no issues

---

## Task 35: Gradual Rollout to Production

### Phase 1: 10% Rollout

- [ ] Enable Baileys for 10% of tenants:
  ```sql
  -- See enable_baileys_gradual_rollout.sql
  ```
- [ ] Monitor for 48 hours
- [ ] Verify success criteria (same as pilot)
- [ ] Document any issues

### Phase 2: 25% Rollout

- [ ] Enable Baileys for additional 15% (total 25%):
  ```sql
  -- See enable_baileys_gradual_rollout.sql
  ```
- [ ] Monitor for 48 hours
- [ ] Verify success criteria
- [ ] Document any issues

### Phase 3: 50% Rollout

- [ ] Enable Baileys for additional 25% (total 50%):
  ```sql
  -- See enable_baileys_gradual_rollout.sql
  ```
- [ ] Monitor for 48 hours
- [ ] Verify success criteria
- [ ] Document any issues

### Phase 4: 100% Rollout

- [ ] Enable Baileys for remaining 50% (total 100%):
  ```sql
  -- See enable_baileys_gradual_rollout.sql
  ```
- [ ] Monitor for 2-4 weeks
- [ ] Verify success criteria
- [ ] Document any issues

---

## Task 36: Validate Production Metrics

### Message Delivery

- [ ] Verify message delivery success rate ≥ 95%:
  ```sql
  SELECT 
    COUNT(*) FILTER (WHERE "Status" = 'SENT') * 100.0 / COUNT(*) AS success_rate
  FROM "WhatsAppMessageLogs"
  WHERE "CreatedAtUtc" >= NOW() - INTERVAL '7 days';
  ```

### Latency

- [ ] Verify QR generation latency < 3 seconds (p95):
  ```sql
  SELECT 
    PERCENTILE_CONT(0.95) WITHIN GROUP (
      ORDER BY EXTRACT(EPOCH FROM ("UpdatedAtUtc" - "CreatedAtUtc"))
    ) AS p95_latency_seconds
  FROM "WhatsAppMessageLogs"
  WHERE "CreatedAtUtc" >= NOW() - INTERVAL '7 days'
    AND "MessageType" = 'QR';
  ```
- [ ] Verify message send latency < 5 seconds (p95):
  ```sql
  SELECT 
    PERCENTILE_CONT(0.95) WITHIN GROUP (
      ORDER BY EXTRACT(EPOCH FROM ("UpdatedAtUtc" - "CreatedAtUtc"))
    ) AS p95_latency_seconds
  FROM "WhatsAppMessageLogs"
  WHERE "CreatedAtUtc" >= NOW() - INTERVAL '7 days'
    AND "Status" = 'SENT';
  ```

### Resource Usage

- [ ] Verify memory usage 20-40 MB per session:
  ```bash
  pm2 monit
  # Check memory usage per process
  ```
- [ ] Verify no Chromium processes running:
  ```bash
  ps aux | grep chromium
  # Should return no results
  ```
- [ ] Calculate RAM savings:
  ```
  Active sessions: ___
  Playwright RAM: ___ sessions × 400 MB = ___ GB
  Baileys RAM: ___ sessions × 30 MB = ___ GB
  Savings: ___ GB (___ %)
  ```

---

## Task 37: Decommission Playwright (After 2-4 Weeks Stability)

### Prerequisites

- [ ] 100% of tenants on Baileys for 2-4 weeks
- [ ] No critical issues reported
- [ ] All metrics within acceptable ranges
- [ ] Stakeholder approval obtained

### Decommissioning Steps

- [ ] Create backup of current system
- [ ] Remove Playwright transport from DI configuration:
  ```csharp
  // In Program.cs, comment out or remove:
  // services.AddKeyedSingleton<IWhatsAppTransport, PlaywrightTransport>("playwright");
  ```
- [ ] Remove Playwright dependencies from `PointOfSale.Infrastructure.csproj`:
  ```xml
  <!-- Remove these packages -->
  <!-- <PackageReference Include="Microsoft.Playwright" Version="..." /> -->
  ```
- [ ] Remove Chromium installation scripts
- [ ] Remove PlaywrightTransport code:
  ```bash
  rm PointOfSale.Infrastructure/WhatsApp/Transport/PlaywrightTransport.cs
  ```
- [ ] Remove UseBaileys feature gate (always use Baileys):
  ```csharp
  // In WhatsAppMessageDispatcher.cs, simplify to:
  var transport = _baileysTransport;
  ```
- [ ] Remove UseBaileys column from Tenants table (optional):
  ```sql
  ALTER TABLE "Tenants" DROP COLUMN "UseBaileys";
  ```
- [ ] Update documentation
- [ ] Deploy changes
- [ ] Verify system still works correctly

---

## Rollback Procedures

### Emergency Rollback (All Tenants)

If critical issues occur:

1. Disable Baileys for all tenants:
   ```sql
   UPDATE "Tenants" SET "UseBaileys" = false;
   ```
2. Verify Playwright transport is working
3. Investigate and fix issues
4. Re-enable Baileys after fixes

### Partial Rollback (Specific Tenants)

If issues affect specific tenants:

1. Disable Baileys for affected tenants:
   ```sql
   UPDATE "Tenants" SET "UseBaileys" = false WHERE "Id" = 'tenant-id';
   ```
2. Investigate tenant-specific issues
3. Re-enable after fixes

### Service Rollback

If Baileys service has critical issues:

1. Stop Baileys service:
   ```bash
   pm2 stop whatsapp-baileys
   ```
2. Disable Baileys for all tenants (SQL above)
3. Fix service issues
4. Restart service and re-enable gradually

---

## Success Criteria Summary

- ✅ Message delivery success rate ≥ 95%
- ✅ QR generation latency < 3 seconds (p95)
- ✅ Message send latency < 5 seconds (p95)
- ✅ Memory usage 20-40 MB per session
- ✅ No Chromium processes running
- ✅ Zero critical errors
- ✅ Positive feedback from pilot tenants
- ✅ RAM savings of 80-90% vs Playwright

---

## Documentation

- [x] Deployment guide created
- [x] Configuration templates created
- [x] Deployment scripts created
- [x] SQL rollout scripts created
- [ ] Update main README with Baileys information
- [ ] Document lessons learned
- [ ] Create runbook for operations team

---

## Sign-off

- [ ] Technical lead approval
- [ ] Operations team trained
- [ ] Monitoring dashboards configured
- [ ] Incident response plan documented
- [ ] Stakeholders notified of completion
