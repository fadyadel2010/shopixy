# Baileys WhatsApp Service - Deployment Guide

## Overview

This guide covers deploying the Baileys WhatsApp microservice to staging and production environments.

## Prerequisites

- Node.js 20.x or higher
- Redis server (for session persistence)
- Network access to WhatsApp servers
- Sufficient RAM (20-40 MB per session)

## Environment Configuration

### 1. Staging Environment

Copy `.env.staging` to `.env`:

```bash
cp .env.staging .env
```

Update the following values:
- `REDIS_HOST`: Your Redis server hostname
- `REDIS_PORT`: Redis port (default: 6379)
- `REDIS_PASSWORD`: Redis password (if required)
- `AUTH_STATE_ENCRYPTION_SECRET`: Generate a secure random string (32+ characters)

Generate encryption secret:
```bash
node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"
```

### 2. Production Environment

Copy `.env.production` to `.env`:

```bash
cp .env.production .env
```

**CRITICAL**: Update all security-sensitive values:
- `AUTH_STATE_ENCRYPTION_SECRET`: Use a unique, secure random string
- `REDIS_PASSWORD`: Use a strong password
- `LOG_LEVEL`: Set to `info` or `warn` for production

## Installation

### 1. Install Dependencies

```bash
npm install --production
```

### 2. Build TypeScript

```bash
npm run build
```

### 3. Verify Build

```bash
ls -la dist/
```

You should see compiled JavaScript files in the `dist/` directory.

## Running the Service

### Development Mode (with hot reload)

```bash
npm run dev
```

### Production Mode

```bash
npm start
```

### Using PM2 (Recommended for Production)

Install PM2 globally:
```bash
npm install -g pm2
```

Start the service:
```bash
pm2 start dist/index.js --name whatsapp-baileys
```

Configure PM2 to start on system boot:
```bash
pm2 startup
pm2 save
```

Monitor the service:
```bash
pm2 monit
pm2 logs whatsapp-baileys
```

## Health Checks

### Verify Service is Running

```bash
curl http://localhost:3000/health
```

Expected response:
```json
{
  "status": "ok",
  "timestamp": "2026-02-01T12:00:00.000Z",
  "uptime": 123.456,
  "activeSessions": 0
}
```

### Verify Redis Connectivity

```bash
redis-cli -h YOUR_REDIS_HOST ping
```

Expected response: `PONG`

## Testing the Deployment

### 1. Connect a Session

```bash
curl -X POST http://localhost:3000/sessions/test-tenant-123/connect \
  -H "Content-Type: application/json" \
  -d '{"mode": "DEDICATED"}'
```

### 2. Get QR Code

```bash
curl http://localhost:3000/sessions/test-tenant-123/qr
```

### 3. Check Session Status

```bash
curl http://localhost:3000/sessions/test-tenant-123/status
```

### 4. Send Test Message (after QR scan)

```bash
curl -X POST http://localhost:3000/messages/send \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "test-tenant-123",
    "phone": "+1234567890",
    "text": "Test message from Baileys",
    "type": "text"
  }'
```

### 5. Disconnect Session

```bash
curl -X POST http://localhost:3000/sessions/test-tenant-123/disconnect
```

## Monitoring

### Key Metrics to Monitor

1. **Memory Usage**: Should be 20-40 MB per active session
2. **Active Sessions**: Track via `/health` endpoint
3. **Message Send Latency**: Should be < 5 seconds (p95)
4. **Error Rate**: Monitor failed sends
5. **Redis Connectivity**: Ensure Redis is always available

### PM2 Monitoring

```bash
# View logs
pm2 logs whatsapp-baileys

# View metrics
pm2 monit

# View process info
pm2 info whatsapp-baileys
```

### Log Files

Logs are written to stdout/stderr. Configure log rotation with PM2:

```bash
pm2 install pm2-logrotate
pm2 set pm2-logrotate:max_size 10M
pm2 set pm2-logrotate:retain 7
```

## Troubleshooting

### Service Won't Start

1. Check Node.js version:
   ```bash
   node --version  # Should be 20.x or higher
   ```

2. Check Redis connectivity:
   ```bash
   redis-cli -h YOUR_REDIS_HOST ping
   ```

3. Check logs:
   ```bash
   pm2 logs whatsapp-baileys --lines 100
   ```

### High Memory Usage

- Each session uses 20-40 MB RAM
- For 100 sessions: expect 2-4 GB RAM usage
- Monitor with: `pm2 monit`
- Adjust `SESSION_IDLE_TIMEOUT_MINUTES` to close idle sessions faster

### Connection Issues

1. Verify WhatsApp servers are accessible:
   ```bash
   ping web.whatsapp.com
   ```

2. Check firewall rules (allow outbound HTTPS)

3. Verify QR code generation:
   ```bash
   curl http://localhost:3000/sessions/test/qr
   ```

### Redis Connection Errors

1. Verify Redis is running:
   ```bash
   redis-cli -h YOUR_REDIS_HOST ping
   ```

2. Check Redis logs:
   ```bash
   tail -f /var/log/redis/redis-server.log
   ```

3. Verify Redis configuration in `.env`

## Security Considerations

### 1. Encryption Secret

- **NEVER** commit `.env` file with real secrets
- Use a unique secret for each environment
- Rotate secrets periodically (every 90 days)

### 2. Redis Security

- Use Redis password authentication
- Enable Redis TLS if possible
- Restrict Redis network access (firewall rules)

### 3. Network Security

- Run service behind a reverse proxy (nginx)
- Use HTTPS for external access
- Implement rate limiting at proxy level

### 4. Session Data

- Auth state is encrypted in Redis
- Sessions are isolated by tenant ID
- Idle sessions are automatically cleaned up

## Scaling

### Vertical Scaling

- Increase server RAM to support more sessions
- 100 sessions ≈ 2-4 GB RAM
- 500 sessions ≈ 10-20 GB RAM

### Horizontal Scaling

- Run multiple instances behind a load balancer
- Use sticky sessions (route by tenant ID)
- Share Redis instance across all instances

### Pool Mode (for high-volume tenants)

Configure pool members in `.env`:
```bash
POOL_SIZE=20
POOL_MEMBERS=member1:+1234567890,member2:+9876543210
```

## Backup and Recovery

### Backup Redis Data

```bash
# Create Redis snapshot
redis-cli -h YOUR_REDIS_HOST BGSAVE

# Copy RDB file
cp /var/lib/redis/dump.rdb /backup/redis-$(date +%Y%m%d).rdb
```

### Restore Redis Data

```bash
# Stop Redis
systemctl stop redis

# Restore RDB file
cp /backup/redis-20260201.rdb /var/lib/redis/dump.rdb

# Start Redis
systemctl start redis
```

### Session Recovery

- Auth state is persisted in Redis
- Sessions automatically restore after service restart
- No QR code re-scan required (if auth state is valid)

## Rollback Procedure

If issues occur after deployment:

1. Stop Baileys service:
   ```bash
   pm2 stop whatsapp-baileys
   ```

2. Update tenant feature gates (disable Baileys):
   ```sql
   UPDATE "Tenants" SET "UseBaileys" = false;
   ```

3. Verify Playwright transport is working:
   ```bash
   # Check backend logs
   tail -f /var/log/pointofsale-api.log | grep WhatsApp
   ```

4. Re-enable Baileys after fixing issues:
   ```bash
   pm2 start whatsapp-baileys
   ```

## Performance Tuning

### Optimize for High Volume

1. Increase concurrent sends:
   ```bash
   MAX_CONCURRENT_SENDS=20
   ```

2. Reduce idle timeout for faster cleanup:
   ```bash
   SESSION_IDLE_TIMEOUT_MINUTES=5
   ```

3. Increase pool size:
   ```bash
   POOL_SIZE=30
   ```

### Optimize for Low Latency

1. Reduce message send timeout:
   ```bash
   MESSAGE_SEND_TIMEOUT_MS=3000
   ```

2. Use dedicated sessions (not pool):
   - Faster connection (no routing overhead)
   - Lower latency (direct connection)

## Support

For issues or questions:
- Check logs: `pm2 logs whatsapp-baileys`
- Review health endpoint: `curl http://localhost:3000/health`
- Verify Redis: `redis-cli -h YOUR_REDIS_HOST ping`
- Check backend integration: Review ASP.NET Core logs

## Changelog

### Version 1.0.0 (2026-02-01)
- Initial production release
- Dedicated session mode
- Pool session mode
- Redis persistence
- Auth state encryption
- Health check endpoint
- Metrics endpoint
