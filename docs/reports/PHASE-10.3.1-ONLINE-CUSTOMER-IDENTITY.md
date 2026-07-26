# Phase 10.3.1 - Online Customer Identity (Core)

## 🎯 Implementation Summary

This phase implements a clean, scalable, and tenant-safe **Online Customer Identity** system for the Storefront, fully managed by POS, following strict CQRS, event-driven architecture.

## ✅ What Was Implemented

### 1. PostgreSQL Entity (Write Model)

**File:** `PointOfSale.Core/Entities/OnlineCustomer.cs`

```csharp
public class OnlineCustomer : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }           // MANDATORY
    public string Email { get; set; }            // Unique per tenant
    public string? PasswordHash { get; set; }    // NULL for social login
    public AuthProvider Provider { get; set; }   // Local, Google, Facebook
    public string? ProviderUserId { get; set; }  // For social login
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }           // Login enabled/disabled
    public bool EmailVerified { get; set; }      // Email confirmation status
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
```

**Key Rules:**
- Unique index on `(TenantId, Email)`
- Email is case-insensitive (normalized to lowercase)
- `PasswordHash` is NULL for social login providers
- Completely **SEPARATE** from POS `Customer` entity

### 2. Domain Events

**File:** `PointOfSale.Core/Events/OnlineCustomerEvents.cs`

| Event | Purpose |
|-------|---------|
| `OnlineCustomerRegisteredEvent` | Customer signed up via Storefront |
| `OnlineCustomerActivatedEvent` | Account enabled by POS admin |
| `OnlineCustomerDeactivatedEvent` | Account disabled by POS admin |
| `OnlineCustomerEmailVerifiedEvent` | Email confirmed |
| `OnlineCustomerLoggedInEvent` | Successful login (updates LastLoginAtUtc) |

**Event Rules:**
- All events extend `IntegrationEvent`
- All events include `TenantId` (MANDATORY)
- Minimal payload - no sensitive data
- Idempotent-safe for replay

### 3. MongoDB Read Model

**File:** `PointOfSale.Infrastructure/MongoDB/Documents/OnlineCustomerDocument.cs`

```javascript
// Collection: online_customers
{
    "_id": ObjectId,
    "tenantId": "guid",          // MANDATORY
    "customerId": "guid",        // From PostgreSQL
    "email": "string",           // Lowercase normalized
    "provider": "local|google|facebook",
    "displayName": "string?",
    "phone": "string?",
    "isActive": true/false,
    "emailVerified": true/false,
    "createdAtUtc": "datetime",
    "lastLoginAtUtc": "datetime?",
    "updatedAtUtc": "datetime",
    "schemaVersion": 1
}
```

**Indexes:**
- Unique: `(tenantId, customerId)`
- Lookup: `(tenantId, email)`
- Filter: `tenantId`

### 4. Event Projection Consumer

**File:** `PointOfSale.Infrastructure/Consumers/OnlineCustomerProjectionConsumer.cs`

- Consumes all OnlineCustomer events
- Projects PostgreSQL → MongoDB idempotently
- Invalidates Redis cache on changes
- Proper retry policy with exponential backoff

### 5. POS Admin API

**File:** `PointOfSale.API/Controllers/OnlineCustomersAdminController.cs`

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/admin/online-customers` | GET | List online customers (paginated, searchable) |
| `/api/admin/online-customers/{id}` | GET | Get customer details |
| `/api/admin/online-customers/{id}/activate` | POST | Enable customer login |
| `/api/admin/online-customers/{id}/deactivate` | POST | Disable customer login |

**Authorization:** `Admin`, `Owner`, `Manager` roles only

### 6. Storefront API (Read-Only)

**File:** `PointOfSale.API/Controllers/StorefrontOnlineCustomerController.cs`

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/storefront/customer/profile` | GET | Get current customer profile |
| `/api/storefront/customer/email-exists` | GET | Check if email is registered |

**Rules:**
- READ-ONLY - no writes to PostgreSQL
- Requires `X-Tenant-Id` header
- Returns cached data from Redis → MongoDB

### 7. Redis Caching

**Cache Key Format:**
```
storefront:tenant:{tenantId}:online-customer:{customerId}
```

**TTL:** 15 minutes

**Invalidation Events:**
- Registration
- Activation/Deactivation
- Email Verification
- Login

### 8. EF Core Configuration

**File:** `PointOfSale.Infrastructure/Data/Configurations/OnlineCustomerConfiguration.cs`

- Primary key on `Id`
- Unique index on `(TenantId, Email)`
- Required constraints with proper lengths
- Provider enum converted to string

## 🏗️ Architecture Compliance

| Rule | Status |
|------|--------|
| POS is ONLY Admin | ✅ |
| Storefront customers separate from POS | ✅ |
| No shared tables | ✅ |
| No shared authentication | ✅ |
| No shared JWT | ✅ (Phase 10.3.2) |
| PostgreSQL = Write Model | ✅ |
| MongoDB = Read Model | ✅ |
| Redis = Cache Only | ✅ |
| Storefront NEVER reads PostgreSQL | ✅ |
| TenantId MANDATORY everywhere | ✅ |
| Guid.Empty rejected | ✅ |

## 🗄️ Database Migration

Run the SQL migration script:
```sql
-- File: create_online_customers_table.sql
psql -U postgres -d pointofsale -f create_online_customers_table.sql
```

Or use EF Core migration (if tools work):
```bash
dotnet ef migrations add AddOnlineCustomersTable --context ApplicationDbContext
dotnet ef database update
```

## 📁 Files Created

| File | Description |
|------|-------------|
| `PointOfSale.Core/Entities/OnlineCustomer.cs` | Entity with AuthProvider enum |
| `PointOfSale.Core/Events/OnlineCustomerEvents.cs` | Domain events |
| `PointOfSale.Infrastructure/MongoDB/Documents/OnlineCustomerDocument.cs` | MongoDB document |
| `PointOfSale.Infrastructure/Data/Configurations/OnlineCustomerConfiguration.cs` | EF Core config |
| `PointOfSale.Infrastructure/Consumers/OnlineCustomerProjectionConsumer.cs` | Event consumer |
| `PointOfSale.Infrastructure/MongoDB/Services/OnlineCustomerReadRepository.cs` | MongoDB repository |
| `PointOfSale.API/Controllers/OnlineCustomersAdminController.cs` | POS Admin API |
| `PointOfSale.API/Controllers/StorefrontOnlineCustomerController.cs` | Storefront API |
| `PointOfSale.API/Services/Storefront/StorefrontOnlineCustomerService.cs` | Storefront service |
| `create_online_customers_table.sql` | SQL migration script |

## 📁 Files Modified

| File | Change |
|------|--------|
| `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` | Added DbSet + query filter |
| `PointOfSale.API/Program.cs` | Registered services + consumer |

## 🚫 Out of Scope (Next Phases)

| Feature | Phase |
|---------|-------|
| Login / Register UI | 10.3.2 |
| Email Verification Flow | 10.3.2.5 |
| Social Login (Google/Facebook) | 10.3.3 |
| Wishlist | 10.3.6 |
| Orders | Future |
| Separate JWT Issuer | 10.3.2 |

## 🔐 Security Notes

1. **Password hashing** is handled in registration (Phase 10.3.2)
2. **JWT issuer** will be separate for Storefront (Phase 10.3.2)
3. **Token invalidation** on deactivation emits event for downstream handling
4. **Tenant isolation** enforced at all layers

## ✅ Build Status

```
Build succeeded.
    36 Warning(s)
    0 Error(s)
```

All implementations compile and follow existing patterns.
