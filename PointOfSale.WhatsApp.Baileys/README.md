# PointOfSale WhatsApp Baileys Service

Baileys-based WhatsApp microservice for the PointOfSale system. This service replaces the Playwright-based WhatsApp engine with a lightweight, scalable solution using the Baileys library.

## Features

- **Native WhatsApp Web Protocol**: Uses Baileys library for direct WhatsApp Web API communication
- **Low Memory Footprint**: 20-40 MB per session (vs 300-600 MB with Playwright)
- **Fast QR Generation**: 1-3 seconds (vs 20-30 seconds with Playwright)
- **Scalable**: Supports 100+ concurrent sessions per instance
- **Dual Session Modes**:
  - **Dedicated**: One session per tenant (paid/enabled tenants)
  - **Pool**: Shared sessions for multiple tenants (free/trial tenants)
- **Redis-Backed State**: Persistent authentication state survives restarts
- **RESTful API**: Simple HTTP endpoints for session and message management

## Prerequisites

- Node.js >= 20.0.0
- Redis server
- TypeScript

## Installation

```bash
npm install
```

## Configuration

Copy `.env.example` to `.env` and configure:

```env
# Server
PORT=3000
NODE_ENV=development

# Redis
REDIS_HOST=localhost
REDIS_PORT=6379
REDIS_PASSWORD=
REDIS_DB=0

# Session
SESSION_IDLE_TIMEOUT_MINUTES=10
SESSION_CLEANUP_INTERVAL_MINUTES=2

# Pool
POOL_SIZE=10
POOL_MAX_CONNECTIONS_PER_MEMBER=50

# Logging
LOG_LEVEL=info
```

## Development

```bash
# Run in development mode with auto-reload
npm run dev

# Build TypeScript
npm run build

# Run production build
npm start

# Run tests
npm test
```

## API Endpoints

### Session Management

#### Connect Session
```http
POST /sessions/:tenantId/connect
Content-Type: application/json

{
  "mode": "dedicated" | "pool"
}
```

#### Get QR Code
```http
GET /sessions/:tenantId/qr
```

#### Get Status
```http
GET /sessions/:tenantId/status
```

#### Disconnect Session
```http
POST /sessions/:tenantId/disconnect
```

### Message Delivery

#### Send Message
```http
POST /messages/send
Content-Type: application/json

{
  "tenantId": "string",
  "phone": "+1234567890",
  "text": "message content",
  "type": "text" | "media" | "template"
}
```

### Health Check

```http
GET /health
```

## Architecture

```
┌─────────────────────────────────────────┐
│   Express HTTP Server                    │
├─────────────────────────────────────────┤
│   Session Manager                        │
│   - Dedicated sessions                   │
│   - Pool sessions                        │
│   - Lifecycle management                 │
├─────────────────────────────────────────┤
│   Message Sender                         │
│   - Text, media, templates               │
│   - Phone validation                     │
├─────────────────────────────────────────┤
│   Redis Persistence                      │
│   - Auth state storage                   │
│   - Session metadata                     │
└─────────────────────────────────────────┘
```

## Session Modes

### Dedicated Mode
- One Baileys session per tenant
- Lazy initialized on first use
- Auto-closed after 10 minutes of inactivity
- Auth state persisted for fast restoration
- Suitable for paid/enabled tenants

### Pool Mode
- Fixed pool of shared WhatsApp sessions
- Tenants mapped to least-loaded pool member
- Messages branded with store name
- No per-tenant QR codes
- Suitable for free/trial tenants

## Memory Usage

| Metric | Playwright | Baileys |
|--------|-----------|---------|
| RAM per session | 300-600 MB | 20-40 MB |
| QR generation | 20-30s | 1-3s |
| 100 tenants | ❌ Impossible | ✅ Stable |
| Browser dependency | Yes ❌ | No ✅ |

## Deployment

### Staging

```bash
./deploy-staging.sh
```

### Production

```bash
./deploy-production.sh
```

See [DEPLOYMENT.md](./DEPLOYMENT.md) for detailed deployment instructions and [PHASE_6_DEPLOYMENT_CHECKLIST.md](./PHASE_6_DEPLOYMENT_CHECKLIST.md) for the complete deployment checklist.

## Documentation

- [DEPLOYMENT.md](./DEPLOYMENT.md) - Comprehensive deployment guide
- [PHASE_6_DEPLOYMENT_CHECKLIST.md](./PHASE_6_DEPLOYMENT_CHECKLIST.md) - Step-by-step deployment checklist
- [PHASE_6_SUMMARY.md](./PHASE_6_SUMMARY.md) - Phase 6 summary and status
- [PHASE_2_VERIFICATION.md](./PHASE_2_VERIFICATION.md) - Phase 2 verification report
- [PHASE_3_VERIFICATION.md](./PHASE_3_VERIFICATION.md) - Phase 3 verification report
- [PHASE_4_VERIFICATION.md](./PHASE_4_VERIFICATION.md) - Phase 4 verification report
- [PHASE_5_VERIFICATION.md](./PHASE_5_VERIFICATION.md) - Phase 5 verification report

## Project Status

- ✅ Phase 1: Transport Abstraction Layer - Complete
- ✅ Phase 2: Baileys Node.js Microservice - Complete
- ✅ Phase 3: Session Strategy Implementation - Complete
- ✅ Phase 4: Backend Integration - Complete
- ✅ Phase 5: Backward Compatibility Validation - Complete
- 🚀 Phase 6: Deployment and Gradual Rollout - In Progress (Task 31 Complete)

## License

MIT
