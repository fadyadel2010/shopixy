# Storefront Architecture Documentation

> **Phase 9 Final Documentation** - Production Readiness Gate

## Overview

The Storefront is a **read-only, headless e-commerce frontend** that serves customers. It is fully managed by the POS System (admin) and follows a strict CQRS (Command Query Responsibility Segregation) architecture.

## Core Principles

### 1. POS is the ONLY Admin
- All content creation, editing, and publishing happens in POS (`PointOfSale.Web`)
- Storefront has **ZERO write capabilities**
- No mutations, no direct database writes

### 2. Strict Data Source Separation

| Layer | Write Model (POS) | Read Model (Storefront) |
|-------|-------------------|-------------------------|
| Database | PostgreSQL | MongoDB |
| Cache | N/A | Redis |
| Access | Full CRUD | Read-only |

### 3. Draft vs Live Separation (Sacred Rule)

```
Draft Content → PostgreSQL ONLY (never exposed to Storefront)
Live Content  → MongoDB projections (Storefront reads from here)
```

- Draft is NEVER cached in Redis
- Draft is NEVER projected to MongoDB
- Only `Publish` action triggers projection to MongoDB

## Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                         POS ADMIN                               │
│  (PointOfSale.Web)                                              │
│                                                                 │
│  Create/Edit → Save Draft → PostgreSQL                          │
│                     │                                           │
│                     ▼                                           │
│              [PUBLISH ACTION]                                   │
│                     │                                           │
│                     ▼                                           │
│              Emit Event (MassTransit)                           │
└─────────────────────────────────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                    EVENT BUS (RabbitMQ)                         │
└─────────────────────────────────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CONSUMERS                                    │
│  (PointOfSale.Infrastructure/Consumers)                         │
│                                                                 │
│  • CmsPageProjectionConsumer                                    │
│  • CategoryProjectionConsumer                                   │
│  • ProductProjectionConsumer                                    │
│  • NavigationProjectionConsumer                                 │
│                                                                 │
│  Actions:                                                       │
│  1. Validate TenantId                                           │
│  2. Read LIVE content from PostgreSQL                           │
│  3. Upsert to MongoDB (idempotent)                              │
│  4. Invalidate Redis cache                                      │
└─────────────────────────────────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                    STOREFRONT                                   │
│  (PointOfSale.Storefront + PointOfSale.API/Storefront)          │
│                                                                 │
│  Read Flow:                                                     │
│  1. Check Redis cache (cache-first)                             │
│  2. On miss → Read from MongoDB                                 │
│  3. Populate Redis cache                                        │
│  4. Return to customer                                          │
│                                                                 │
│  ❌ NEVER reads from PostgreSQL (except tenant resolution)      │
│  ❌ NEVER writes to any database                                │
└─────────────────────────────────────────────────────────────────┘
```

## DbContext in Storefront Controllers

### Why DbContext Exists

DbContext is injected into Storefront controllers for **ONE purpose only**:

**Tenant Resolution (Slug → TenantId)**

When a request comes with `X-Tenant-Id: mystore` (subdomain slug), we need to resolve it to the actual `TenantId` (GUID). This metadata lookup is the ONLY acceptable PostgreSQL access in Storefront.

### Affected Controllers

| Controller | DbContext Usage |
|------------|-----------------|
| `StorefrontPagesController` | Tenant resolution only |
| `StorefrontNavigationController` | Tenant resolution only |
| `StorefrontStoreConfigController` | Tenant resolution only |

### Mandatory Comment (Enforced)

Every Storefront controller with DbContext MUST have this comment:

```csharp
// ⚠️ IMPORTANT: ARCHITECTURAL BOUNDARY
// DbContext is used ONLY for tenant resolution (Slug → TenantId).
// NO business data is read from PostgreSQL in Storefront controllers.
// All content reads MUST go through MongoDB or Redis cache.
// Violating this boundary breaks the CQRS architecture.
```

## Tenant Isolation

### PostgreSQL
- All entities have `TenantId` property
- Global query filters enforce tenant isolation
- `TenantSaveChangesInterceptor` blocks cross-tenant writes

### MongoDB
- All documents have `TenantId` field
- All queries filter by `TenantId`
- No shared/global documents

### Redis
- All cache keys are tenant-namespaced
- Pattern: `{instance}:tenant:{tenantId}:{resource}`
- Example: `pos:tenant:abc123:categories:tree`

### Events
- All events carry `TenantId`
- Consumers reject events with missing `TenantId`

## Forbidden Patterns

These patterns are **NEVER allowed** in Storefront:

| Pattern | Reason |
|---------|--------|
| `DbContext.Products.ToList()` | Business data read from SQL |
| `DbContext.SaveChanges()` | Write operation |
| `IgnoreQueryFilters()` in services | Tenant isolation bypass |
| Hardcoded CMS pages | Breaks CMS-driven architecture |
| Default tenant fallback | Security violation |
| Shared cache keys | Cross-tenant data leak |

### Allowed Exceptions

| Pattern | Where | Reason |
|---------|-------|--------|
| `IgnoreQueryFilters()` | Tenant resolution | Need to query Tenants table without filter |
| `IgnoreQueryFilters()` | Consumers | Reading source data for projection |
| `IgnoreQueryFilters()` | Auth | User authentication |

## Consumer Retry Policy

All projection consumers use exponential backoff:

```
Retry 1: 1 second
Retry 2: 2 seconds
Retry 3: 4 seconds
Retry 4: 8 seconds
Retry 5: 16 seconds
Total: ~31 seconds before DLQ
```

Handles:
- `MongoException` (MongoDB failures)
- `RedisException` (Redis failures)
- `TimeoutException` (Network timeouts)

## Category System

### Root Category Detection

Categories are considered "root" if:
- `ParentId == null`
- `ParentId == Guid.Empty`

This handles edge cases from MongoDB/SQL serialization differences.

### Orphan Handling

If a category's `ParentId` references a non-existent parent:
- Category is treated as root
- Warning is logged
- No data loss occurs

## Cache Strategy

| Resource | TTL | Invalidation |
|----------|-----|--------------|
| Category Tree | 30 min | On category sync event |
| CMS Pages | 30 min | On publish/rollback event |
| Navigation | 60 min | On navigation update event |
| Store Config | 60 min | On config update event |

## Verification Checklist

Before any release, verify:

- [ ] No SQL reads in Storefront services (except tenant resolution)
- [ ] All MongoDB queries filter by TenantId
- [ ] All Redis keys are tenant-namespaced
- [ ] No hardcoded CMS pages in frontend
- [ ] All consumers are idempotent
- [ ] Retry policies are configured
- [ ] Cache invalidation works end-to-end

---

**Document Version**: 1.0  
**Phase**: 9 (Final Hardening)  
**Status**: Production Ready  
**Last Updated**: December 2024
