# WhatsApp Baileys Migration - Complete Deployment Guide

## Overview

This guide walks you through deploying the WhatsApp Baileys migration from development to production. Follow these steps in order for a safe, gradual rollout.

## Prerequisites Checklist

Before starting deployment, ensure you have:

### Infrastructure
- [ ] Staging server with Node.js 20.x+
- [ ] Production server with Node.js 20.x+
- [ ] Redis server (accessible from both staging and production)
- [ ] PostgreSQL database with migration applied
- [ ] Sufficient RAM (2-4 GB for staging, 10-20 GB for production)

### Access
- [ ] SSH access to staging server
- [ ] SSH access to production server
- [ ] Database admin credentials
- [ ] Redis admin credentials
- [ ] PM2 installed on both servers

### Configuration
- [ ] `.env` files configured for staging and production
- [ ] Encryption secrets generated (32+ characters)
- [ ] `appsettings.Staging.json` configured
- [ ] `appsettings.Production.json` configured

## Phase 1: Local Development Testing

### Step 1: Run Local Tests

```bash
# Navigate to Baileys service
cd PointOfSale.WhatsApp.Baileys

# Install dependencies
npm install

# Run all tests
npm test

# Run property-based tests
npm run test:property

# Build TypeScript
npm run build
```

**Success Criteria**:
- ✅ All tests pass
- ✅ No TypeScript compilation errors
- ✅ `dist/` directory created with compiled JavaScript

### Step 2: Start Local Services

```bash
# Terminal 1: Start Redis
redis-server

# Terminal 2: Start Baileys service
cd PointOfSale.WhatsApp.Baileys
npm run dev

# Terminal 3: Start ASP.NET Core backend
cd PointOfSale.API
dotnet run
```

### Step 3: Verify Local Integration

```bash
# Run deployment verification
cd PointOfSale.WhatsApp.Baileys
chmod +x verify-deployment.sh
./verify-deployment.sh http://localhost:3000

# Run backend integration verification
cd ..
pwsh verify-backend-integration.ps1 -BackendUrl "http://localhost:5000" -BaileysUrl "http://localhost:3000"
```

**Success Criteria**:
- ✅ All verification tests pass
- ✅ Health endpoints respond
- ✅ Both transports registered in DI
- ✅ Feature gate routing works

---

## Phase 2: Staging Deployment (Task 32)

### Step 1: Prepare Staging Environment

```bash
# SSH to staging server
ssh user@staging-server

# Install Node.js 20.x (if not installed)
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt-get install -y nodejs

# Install PM2 globally
sudo npm install -g pm2

# Verify installations
node --version  # Should be 20.x
pm2 --version
```

### Step 2: Configure Staging Environment

```bash
# Create service directory
sudo mkdir -p /opt/whatsapp-baileys
sudo chown $USER:$USER /opt/whatsapp-baileys

# Copy files to staging server (from local machine)
scp -r PointOfSale.WhatsApp.Baileys/* user@staging-server:/opt/whatsapp-baileys/

# SSH to staging server
ssh user@staging-server
cd /opt/whatsapp-baileys

# Copy staging environment file
cp .env.staging .env

# IMPORTANT: Edit .env and update:
# - REDIS_HOST
# - REDIS_PORT
# - AUTH_STATE_ENCRYPTION_SECRET (generate new one)
nano .env

# Generate encryption secret
node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"
# Copy the output and paste into .env
```

### Step 3: Deploy to Staging

```bash
# Run deployment script
chmod +x deploy-staging.sh
./deploy-staging.sh
```

**Expected Output**:
```
[1/8] Checking prerequisites...
✓ Prerequisites OK

[2/8] Creating service directory...
✓ Service directory ready

[3/8] Copying service files...
✓ Files copied

[4/8] Installing dependencies...
✓ Dependencies installed

[5/8] Building TypeScript...
✓ Build complete

[6/8] Verifying Redis connectivity...
✓ Redis connection OK

[7/8] Stopping existing service...
✓ Existing service stopped

[8/8] Starting service...
✓ Service started

✓ Health check passed

Deployment successful!
```

### Step 4: Verify Staging Deployment

```bash
# Run verification script
chmod +x verify-deployment.sh
./verify-deployment.sh http://localhost:3000

# Check service status
pm2 status
pm2 logs whatsapp-baileys-staging --lines 50

# Monitor memory usage
pm2 monit
```

### Step 5: Integration Test with Real WhatsApp

```bash
# Run integration test (requires manual QR scan)
chmod +x test-integration.sh
./test-integration.sh http://localhost:3000 +1234567890

# Follow prompts to scan QR code
# Verify message received on test phone
```

**Success Criteria**:
- ✅ Service running without errors
- ✅ Health endpoint responds
- ✅ QR code generated successfully
- ✅ Message sent and received
- ✅ Memory usage 20-40 MB per session

---

## Phase 3: Backend Staging Deployment (Task 33)

### Step 1: Apply Database Migration

```bash
# Connect to staging database
psql -h staging-db-host -U postgres -d PosEcommerceDb_Staging

# Run migration
\i migrations/add_use_baileys_feature_gate.sql

# Verify migration
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'Tenants' AND column_name = 'UseBaileys';

# Expected output:
# column_name | data_type
# UseBaileys  | boolean
```

### Step 2: Deploy Backend to Staging

```bash
# Build backend in Release mode
cd PointOfSale.API
dotnet build -c Release

# Run tests
dotnet test

# Publish
dotnet publish -c Release -o ./publish

# Copy to staging server
scp -r ./publish/* user@staging-server:/opt/pointofsale-api/

# SSH to staging server
ssh user@staging-server

# Update appsettings.Staging.json
cd /opt/pointofsale-api
nano appsettings.Staging.json

# Update BaileysService:BaseUrl to http://localhost:3000

# Restart API service
sudo systemctl restart pointofsale-api
# OR if using PM2:
pm2 restart pointofsale-api
```

### Step 3: Verify Backend Integration

```bash
# From local machine, run verification script
pwsh verify-backend-integration.ps1 -BackendUrl "http://staging-server:5000" -BaileysUrl "http://staging-server:3000"

# Follow prompts for manual verification steps
```

### Step 4: Test Feature Gate Routing

```bash
# Connect to staging database
psql -h staging-db-host -U postgres -d PosEcommerceDb_Staging

# Create test tenant (if not exists)
INSERT INTO "Tenants" ("Id", "Name", "UseBaileys", "IsActive", "CreatedAtUtc")
VALUES (gen_random_uuid(), 'Test Tenant', false, true, NOW());

# Get test tenant ID
SELECT "Id", "Name", "UseBaileys" FROM "Tenants" WHERE "Name" = 'Test Tenant';

# Test Playwright routing (UseBaileys = false)
# Send a WhatsApp message via backend API
# Check logs for: "Routing to Playwright transport"

# Enable Baileys for test tenant
UPDATE "Tenants" SET "UseBaileys" = true WHERE "Name" = 'Test Tenant';

# Test Baileys routing (UseBaileys = true)
# Send another WhatsApp message
# Check logs for: "Routing to Baileys transport"
```

**Success Criteria**:
- ✅ Database migration applied
- ✅ Backend deployed successfully
- ✅ Both transports registered
- ✅ Playwright routing works (UseBaileys = false)
- ✅ Baileys routing works (UseBaileys = true)
- ✅ No automatic fallback

---

## Phase 4: Pilot Testing (Task 34)

### Step 1: Select Pilot Tenants

Criteria for pilot tenants:
- Low to medium message volume (< 100 messages/day)
- Active WhatsApp usage
- Responsive to issues
- Willing to provide feedback

```sql
-- Identify potential pilot tenants
SELECT 
    t."Id",
    t."Name",
    COUNT(wml."Id") AS message_count_last_7_days
FROM "Tenants" t
LEFT JOIN "WhatsAppMessageLogs" wml ON wml."TenantId" = t."Id"::text
WHERE t."IsActive" = true
  AND wml."CreatedAtUtc" >= NOW() - INTERVAL '7 days'
GROUP BY t."Id", t."Name"
HAVING COUNT(wml."Id") BETWEEN 10 AND 100
ORDER BY message_count_last_7_days ASC
LIMIT 10;
```

### Step 2: Enable Baileys for Pilot Tenants

```sql
-- Enable Baileys for selected pilot tenants
UPDATE "Tenants"
SET "UseBaileys" = true,
    "UpdatedAtUtc" = NOW()
WHERE "Id" IN (
    'pilot-tenant-id-1',
    'pilot-tenant-id-2',
    'pilot-tenant-id-3'
);

-- Verify
SELECT "Id", "Name", "UseBaileys", "UpdatedAtUtc"
FROM "Tenants"
WHERE "UseBaileys" = true;
```

### Step 3: Monitor Pilot Tenants (24-48 hours)

```sql
-- Monitor message delivery success rate
SELECT 
    t."UseBaileys",
    COUNT(*) AS total_messages,
    COUNT(*) FILTER (WHERE wml."Status" = 'SENT') AS sent_count,
    COUNT(*) FILTER (WHERE wml."Status" = 'FAILED') AS failed_count,
    ROUND(COUNT(*) FILTER (WHERE wml."Status" = 'SENT') * 100.0 / COUNT(*), 2) AS success_rate
FROM "WhatsAppMessageLogs" wml
JOIN "Tenants" t ON t."Id"::text = wml."TenantId"
WHERE wml."CreatedAtUtc" >= NOW() - INTERVAL '24 hours'
GROUP BY t."UseBaileys";

-- Monitor latency
SELECT 
    t."UseBaileys",
    AVG(EXTRACT(EPOCH FROM (wml."UpdatedAtUtc" - wml."CreatedAtUtc"))) AS avg_latency_seconds,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (wml."UpdatedAtUtc" - wml."CreatedAtUtc"))) AS p95_latency_seconds
FROM "WhatsAppMessageLogs" wml
JOIN "Tenants" t ON t."Id"::text = wml."TenantId"
WHERE wml."CreatedAtUtc" >= NOW() - INTERVAL '24 hours'
  AND wml."Status" = 'SENT'
GROUP BY t."UseBaileys";

-- Monitor error rates by tenant
SELECT 
    t."Id",
    t."Name",
    t."UseBaileys",
    COUNT(*) AS total_messages,
    COUNT(*) FILTER (WHERE wml."Status" = 'FAILED') AS failed_count,
    ROUND(COUNT(*) FILTER (WHERE wml."Status" = 'FAILED') * 100.0 / COUNT(*), 2) AS error_rate
FROM "WhatsAppMessageLogs" wml
JOIN "Tenants" t ON t."Id"::text = wml."TenantId"
WHERE wml."CreatedAtUtc" >= NOW() - INTERVAL '24 hours'
GROUP BY t."Id", t."Name", t."UseBaileys"
HAVING COUNT(*) FILTER (WHERE wml."Status" = 'FAILED') * 100.0 / COUNT(*) > 5
ORDER BY error_rate DESC;
```

```bash
# Monitor Baileys service
pm2 monit

# Check logs
pm2 logs whatsapp-baileys --lines 100

# Monitor memory usage
watch -n 5 'pm2 info whatsapp-baileys | grep memory'
```

**Success Criteria**:
- ✅ Message delivery success rate ≥ 95%
- ✅ Latency < 5 seconds (p95)
- ✅ No increase in error rate vs Playwright
- ✅ Memory usage 20-40 MB per session
- ✅ No critical errors in logs
- ✅ Positive feedback from pilot tenants

### Step 4: Rollback (if needed)

If issues occur:

```sql
-- Disable Baileys for pilot tenants
UPDATE "Tenants"
SET "UseBaileys" = false,
    "UpdatedAtUtc" = NOW()
WHERE "Id" IN ('pilot-tenant-id-1', 'pilot-tenant-id-2', 'pilot-tenant-id-3');
```

---

## Phase 5: Production Deployment (Tasks 35-36)

### Step 1: Deploy to Production

Follow the same steps as staging deployment, but use production configuration:

```bash
# SSH to production server
ssh user@production-server

# Deploy Baileys service
cd /opt/whatsapp-baileys
cp .env.production .env
# Edit .env with production values
./deploy-production.sh

# Deploy backend
# (Same as staging, but use appsettings.Production.json)
```

### Step 2: Gradual Rollout

See `migrations/enable_baileys_gradual_rollout.sql` for detailed SQL scripts.

**Rollout Schedule**:
1. **10% Rollout**: Enable for 10% of tenants, monitor 48 hours
2. **25% Rollout**: Enable for additional 15% (total 25%), monitor 48 hours
3. **50% Rollout**: Enable for additional 25% (total 50%), monitor 48 hours
4. **100% Rollout**: Enable for remaining 50% (total 100%), monitor 2-4 weeks

**Monitoring at Each Phase**:
- Message delivery success rate
- Latency (p95)
- Error rates by tenant
- Memory usage
- Circuit breaker activations

**Rollback at Any Phase**:
```sql
-- Emergency rollback (all tenants)
UPDATE "Tenants" SET "UseBaileys" = false;
```

---

## Phase 6: Playwright Decommission (Task 37)

**Prerequisites**:
- 100% rollout stable for 2-4 weeks
- All metrics within acceptable ranges
- Stakeholder approval

### Step 1: Remove Playwright Transport

```csharp
// In PointOfSale.API/Program.cs
// Comment out or remove:
// services.AddKeyedSingleton<IWhatsAppTransport, PlaywrightTransport>("playwright");
```

### Step 2: Simplify Dispatcher

```csharp
// In WhatsAppMessageDispatcher.cs
// Replace feature gate routing with direct Baileys usage:
var transport = _baileysTransport;
var transportName = "Baileys";
```

### Step 3: Remove Dependencies

```xml
<!-- In PointOfSale.Infrastructure.csproj -->
<!-- Remove: -->
<!-- <PackageReference Include="Microsoft.Playwright" Version="..." /> -->
```

### Step 4: Deploy Changes

```bash
# Build and deploy
dotnet build -c Release
dotnet publish -c Release -o ./publish

# Deploy to production
# Restart services
```

---

## Troubleshooting

### Baileys Service Won't Start

1. Check Node.js version: `node --version` (should be 20.x+)
2. Check Redis: `redis-cli ping`
3. Check logs: `pm2 logs whatsapp-baileys`
4. Verify `.env` configuration

### High Memory Usage

1. Monitor: `pm2 monit`
2. Check active sessions: `curl http://localhost:3000/health`
3. Adjust `SESSION_IDLE_TIMEOUT_MINUTES` in `.env`

### Connection Issues

1. Verify WhatsApp servers accessible: `ping web.whatsapp.com`
2. Check firewall rules
3. Test QR generation: `curl http://localhost:3000/sessions/test/qr`

### Backend Integration Issues

1. Check both services running: `pm2 status`
2. Verify `BaileysService:BaseUrl` in appsettings
3. Check backend logs for transport registration
4. Verify database migration applied

---

## Support

For issues or questions:
- Check service logs: `pm2 logs whatsapp-baileys`
- Review health endpoint: `curl http://localhost:3000/health`
- Verify Redis: `redis-cli ping`
- Check backend logs
- Review deployment checklist: `PHASE_6_DEPLOYMENT_CHECKLIST.md`

---

## Summary

This deployment guide covers the complete migration from development to production. Follow each phase carefully, monitor metrics at every stage, and maintain rollback capability throughout the process.

**Estimated Timeline**:
- Local testing: 1 day
- Staging deployment: 2-3 days
- Pilot testing: 2-3 days
- Gradual rollout: 1-2 weeks
- Stability monitoring: 2-4 weeks
- Playwright decommission: 1 day

**Total**: 4-6 weeks for complete migration
