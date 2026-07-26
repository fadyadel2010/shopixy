# Multi-Tenancy Foundation Implementation

## Overview

This document describes the multi-tenancy foundation implemented for the POS system, preparing it for SaaS multi-tenancy (Shopify/Odoo style) without breaking existing functionality.

## Architecture Pattern

**"Shared Database, Shared Schema, Tenant Isolation at Entry Points"**

- Single database
- Single schema
- Tenant isolation via global query filters
- TenantId added only to critical business tables
- Events are fully tenant-aware

## Components Implemented

### 1. Tenant Core Model (`PointOfSale.Core.Entities.Tenant`)

Platform-level entity representing a Store/Company/Business:

```csharp
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? Slug { get; set; }           // For future subdomain routing
    public string? ContactEmail { get; set; }
}
```

### 2. Tenant Context Abstraction

**Interface** (`PointOfSale.Core.Interfaces.ITenantContext`):
```csharp
public interface ITenantContext
{
    Guid? TenantId { get; }
    bool HasTenant { get; }
    Guid RequiredTenantId { get; }  // Throws if no tenant
}
```

**Implementation** (`PointOfSale.Infrastructure.MultiTenancy.TenantContext`):
- Scoped service holding the current tenant ID per request
- Set by middleware, accessible everywhere via DI

### 3. Tenant Marker Interface (`ITenantOwned`)

```csharp
public interface ITenantOwned
{
    Guid? TenantId { get; set; }
}
```

Applied to core business entities:
- `Product`
- `Category`
- `Warehouse`
- `Customer`

### 4. Tenant Resolution Middleware

**Location**: `PointOfSale.API.Middleware.TenantResolutionMiddleware`

**Resolution Strategy**:
- Reads `X-Tenant-Id` HTTP header
- Validates tenant exists and is active
- Sets `TenantContext.TenantId`

**Excluded Paths** (no tenant resolution required):
- `/swagger`
- `/hangfire`
- `/health`
- `/api/auth/login`
- `/api/auth/refresh`
- `/api/tenants`

**Error Responses**:
- `400 Bad Request`: Invalid tenant ID format or tenant not found
- `403 Forbidden`: Tenant is inactive

### 5. Global Query Filters (EF Core)

Applied in `ApplicationDbContext.OnModelCreating`:

```csharp
// Tenant-aware entities: combined soft-delete + tenant isolation
modelBuilder.Entity<Customer>().HasQueryFilter(e => 
    !e.IsDeleted && 
    (CurrentTenantId == null || e.TenantId == null || e.TenantId == CurrentTenantId));
```

**Filter Logic**:
- Show entity if NOT deleted AND
  - No tenant context (system-level operation), OR
  - Entity has no tenant (legacy data), OR
  - Entity belongs to current tenant

### 6. Tenant-Aware Events

**Base Class Update** (`IntegrationEvent`):
```csharp
public abstract class IntegrationEvent
{
    // ... existing properties
    public Guid? TenantId { get; set; }  // NEW
}
```

**Automatic Enrichment** (`MassTransitEventPublisher`):
- Automatically populates `TenantId` from `ITenantContext` when publishing
- Logs warning if publishing without tenant context

## Database Migration

Migration file: `20251221200148_AddMultiTenancyFoundation.cs`

**Changes**:
1. Creates `Tenants` table with indexes on `Slug` (unique) and `IsActive`
2. Adds nullable `TenantId` column to:
   - `Products`
   - `Categories`
   - `Warehouses`
   - `Customers`

**Apply Migration**:
```bash
dotnet ef database update --project PointOfSale.Infrastructure --startup-project PointOfSale.API
```

## API Endpoints

### Tenant Management (`/api/tenants`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/tenants` | List all tenants |
| GET | `/api/tenants/{id}` | Get tenant by ID |
| POST | `/api/tenants` | Create new tenant |
| PUT | `/api/tenants/{id}` | Update tenant |
| PATCH | `/api/tenants/{id}/status` | Activate/deactivate tenant |

## Usage Examples

### Creating a Tenant

```bash
curl -X POST http://localhost:5000/api/tenants \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Electronics Store",
    "slug": "acme-electronics",
    "contactEmail": "admin@acme.com"
  }'
```

### Making Tenant-Scoped Requests

```bash
curl http://localhost:5000/api/products \
  -H "X-Tenant-Id: {tenant-id}"
```

### Accessing Tenant Context in Services

```csharp
public class MyService
{
    private readonly ITenantContext _tenantContext;
    
    public MyService(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }
    
    public void DoSomething()
    {
        if (_tenantContext.HasTenant)
        {
            var tenantId = _tenantContext.TenantId;
            // Tenant-specific logic
        }
    }
}
```

## Non-Goals (Explicitly NOT Implemented)

As per the requirements, the following were NOT implemented:

- ❌ TenantId on every table
- ❌ Separate databases per tenant
- ❌ Per-tenant billing, plans, or quotas
- ❌ MongoDB, Redis, or Elasticsearch modifications
- ❌ Subdomain-based tenant resolution
- ❌ JWT-based tenant resolution
- ❌ Existing business workflow refactoring

## Tables NOT Modified

The following tables intentionally do NOT have TenantId:

- Hangfire tables (system infrastructure)
- Outbox/Inbox tables (message infrastructure)
- System settings (global configuration)
- Financial derived tables (accounting infrastructure)
- Audit logs (cross-tenant auditing)
- All other transactional tables (phase 2)

## Future Enhancements (Phase 2+)

1. **Subdomain Resolution**: Resolve tenant from `{slug}.yourdomain.com`
2. **JWT Claims Resolution**: Extract TenantId from JWT token
3. **Tenant Switching**: Allow users to switch between tenants
4. **Per-Tenant Configuration**: Store tenant-specific settings
5. **Expand TenantId to More Entities**: SalesInvoice, PurchaseInvoice, etc.
6. **Billing & Plans**: Add subscription management
7. **Data Migration Tool**: Assign existing data to default tenant

## Testing Checklist

Before deploying:

- [ ] Create a test tenant via API
- [ ] Verify requests without `X-Tenant-Id` still work (legacy mode)
- [ ] Verify requests with valid `X-Tenant-Id` return tenant-scoped data
- [ ] Verify requests with invalid tenant return 400
- [ ] Verify requests with inactive tenant return 403
- [ ] Verify events include TenantId in RabbitMQ messages
- [ ] Run existing tests to ensure no regression

## Files Created/Modified

### New Files
- `PointOfSale.Core/Entities/Tenant.cs`
- `PointOfSale.Core/Interfaces/ITenantContext.cs`
- `PointOfSale.Core/Interfaces/ITenantOwned.cs`
- `PointOfSale.Infrastructure/MultiTenancy/TenantContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/TenantConfiguration.cs`
- `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`
- `PointOfSale.API/Controllers/TenantsController.cs`

### Modified Files
- `PointOfSale.Core/Entities/Product.cs` - Added ITenantOwned
- `PointOfSale.Core/Entities/Category.cs` - Added ITenantOwned
- `PointOfSale.Core/Entities/Warehouse.cs` - Added ITenantOwned
- `PointOfSale.Core/Entities/Customer.cs` - Added ITenantOwned
- `PointOfSale.Core/Events/IntegrationEvent.cs` - Added TenantId
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` - Tenant filters
- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs` - Tenant enrichment
- `PointOfSale.API/Program.cs` - Service & middleware registration
