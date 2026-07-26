# Launch Notes - E-commerce Storefront + POS

## Last Updated: 2025-12-25 (Phase 7.11)

---

## Quick Start

### Backend API

```bash
cd PointOfSale.API
dotnet run
```

Default: `http://localhost:5000` (HTTPS: `https://localhost:5001`)

### POS Admin Frontend

```bash
cd PointOfSale.Web
npm install
npm run dev
```

Default: `http://localhost:5173`

### Storefront Frontend

```bash
cd PointOfSale.Storefront
npm install
npm run dev
```

Default: `http://localhost:5174`

---

## Required Environment Variables

### Backend (appsettings.json / Environment)

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ConnectionStrings:DefaultConnection` | ✅ | - | PostgreSQL connection string |
| `MongoDB:ConnectionString` | ✅ | - | MongoDB connection string |
| `MongoDB:DatabaseName` | ✅ | `pos_storefront` | MongoDB database name |
| `Redis:ConnectionString` | ⚠️ | `localhost:6379` | Redis connection (graceful fallback if unavailable) |
| `RabbitMQ:Host` | ⚠️ | `localhost` | RabbitMQ host (required for events) |
| `Elasticsearch:Uri` | ❌ | - | Optional search (falls back to MongoDB) |
| `JwtSettings:Secret` | ✅ | - | JWT signing key (min 32 chars) |
| `Cors:AllowedOrigins` | ✅ | - | Array of allowed frontend origins |

### POS Web (.env)

```env
VITE_API_BASE_URL=http://localhost:5000
VITE_STOREFRONT_DOMAIN=localhost:5174
```

### Storefront (.env)

```env
VITE_API_BASE_URL=http://localhost:5000
VITE_TENANT_ID=11111111-1111-1111-1111-111111111111
```

---

## How to Simulate a Tenant Locally

1. **Start all infrastructure** (PostgreSQL, MongoDB, Redis, RabbitMQ)

2. **Create a tenant in the database** (if not exists):
   ```sql
   INSERT INTO "Tenants" ("Id", "Name", "Slug", "IsActive", "CreatedAtUtc")
   VALUES ('11111111-1111-1111-1111-111111111111', 'Demo Store', 'default', true, NOW());
   ```

3. **Configure Storefront** with tenant ID:
   ```env
   VITE_TENANT_ID=11111111-1111-1111-1111-111111111111
   ```

4. **Start all applications** and access:
   - POS: http://localhost:5173
   - Storefront: http://localhost:5174

---

## Storefront Status Control

The POS controls the Storefront status:

| Status | What Customers See |
|--------|-------------------|
| `live` | Normal shopping experience |
| `maintenance` | Branded maintenance page with custom message |
| `disabled` | Branded disabled page (store offline) |

**To change status:**
1. Go to POS → Settings → Online Store
2. Select status and save
3. Storefront reflects change on next load

---

## Demo Flow (End-to-End)

1. **POS Setup:**
   - Login to POS
   - Create categories
   - Create products (mark as "Online Visible")
   - Set shipping methods
   - Configure store (Settings → Online Store → Live)

2. **Storefront Experience:**
   - Browse products
   - Add to cart
   - Proceed to checkout
   - Enter shipping address
   - Select shipping method
   - Select COD payment
   - Place order

3. **POS Verification:**
   - View order in POS → E-commerce → Orders
   - Update order status
   - Update shipment status

---

## Known Limitations

| Area | Limitation | Planned For |
|------|------------|-------------|
| Authentication | Guest checkout only, no user accounts | Phase 8 |
| Payments | COD only, no online payment | Phase 8+ |
| Multi-domain | Single domain per storefront | Phase 8+ |
| Search | Basic text search, no fuzzy matching | Future |
| Images | No image optimization/CDN | Future |
| Cart | Ephemeral (Redis TTL), no persistence after expiry | Design choice |
| Inventory | No real-time stock sync on storefront | Design choice |

---

## Error Handling Summary

### Storefront

| Scenario | User Sees | Recoverable |
|----------|-----------|-------------|
| Tenant not found | TenantNotFound page | ❌ (config error) |
| Store disabled | StoreDisabled page | ❌ (admin action) |
| Store maintenance | MaintenancePage | ❌ (admin action) |
| Network error | NetworkError page | ✅ (retry button) |
| Store config missing | StoreUnavailable | ✅ (retry) |
| Empty cart → checkout | EmptyState | ✅ (shop link) |
| Checkout error | Error message + form | ✅ (retry) |

### POS

| Scenario | Admin Sees | Recoverable |
|----------|------------|-------------|
| API error | Toast notification | ✅ |
| Storefront not configured | "Not configured" card | ✅ (setup link) |
| Store disabled | Warning banner | ✅ (change status) |

---

## Security Checklist

- ✅ All Storefront APIs require `X-Tenant-Id` header
- ✅ All Storefront APIs require `X-Session-Id` header
- ✅ Storefront APIs are read-only (except cart/checkout)
- ✅ Admin APIs require JWT authentication
- ✅ Tenant isolation via global query filters
- ✅ No cross-tenant data access possible
- ✅ POS cannot modify another tenant's config

---

## Health Indicators

### Backend Startup Logs (Expected)

```
[MongoDB] Connected successfully
[Redis] Connected successfully (or: Redis unavailable, using degraded mode)
[Elasticsearch] Connected (or: Elasticsearch unavailable, using MongoDB fallback)
[MassTransit] RabbitMQ connected
```

### Storefront Console (Expected in Dev)

```
[Bootstrap] Starting bootstrap flow...
[Bootstrap] Tenant resolved: 11111111-...
[Bootstrap] Session initialized: abc123...
[Bootstrap] StoreConfig loaded
[Bootstrap] Theme applied
[Bootstrap] Bootstrap complete ✓
```

---

## Troubleshooting

### "Tenant not found" on Storefront

1. Check `VITE_TENANT_ID` in `.env`
2. Verify tenant exists in database
3. Verify tenant `IsActive = true`

### Products not appearing on Storefront

1. Check product `IsActive = true`
2. Check product `IsOnlineVisible = true`
3. Check MongoDB sync (product must be published)
4. Clear Redis cache if needed

### Checkout fails

1. Check cart is not empty
2. Check shipping methods exist
3. Check API logs for errors
4. Verify `X-Request-Id` for idempotency issues

### POS can't reach Storefront

1. Check `VITE_STOREFRONT_DOMAIN` in POS `.env`
2. Check CORS settings in backend

---

## Build Verification

```bash
# Backend
cd PointOfSale.API && dotnet build
# ✅ Expected: Build succeeded

# POS Web
cd PointOfSale.Web && npm run build
# ✅ Expected: ✓ built in Xs

# Storefront
cd PointOfSale.Storefront && npm run build
# ✅ Expected: ✓ built in Xs
```

---

## Definition of Done (Phase 7.11)

- ✅ No blank screens possible
- ✅ No unhandled errors
- ✅ All flows recover gracefully
- ✅ POS clearly controls Storefront
- ✅ Storefront can be demoed safely
- ✅ All builds pass
- ✅ Documentation complete

---

## Launch Status: ✅ READY

*The e-commerce system is ready for demo and first tenant onboarding.*
