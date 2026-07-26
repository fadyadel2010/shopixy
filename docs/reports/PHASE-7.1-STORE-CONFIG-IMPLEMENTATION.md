# Phase 7.1 – Theme & Store Config System Implementation

## Overview

This phase implements a **Store Theme & Configuration System** that allows the POS system (Admin) to fully control the E-commerce storefront appearance and behavior per tenant, in a config-driven, theme-ready, and frontend-agnostic way.

## Architecture

```
POS Admin
   ↓ (CRUD Store Config)
PostgreSQL (StoreConfigs table)
   ↓ (Read API)
Redis Cache (30-minute TTL)
   ↓
Storefront React App
   ↓
CSS Variables + Layout Variants
```

## Files Created

### Core Entity
- `PointOfSale.Core/Entities/StoreConfig.cs` - Main entity with nested config classes:
  - `StoreConfig` - Core configuration entity
  - `StoreThemeConfig` - Theme configuration container
  - `ColorsConfig` - Color palette settings
  - `FontConfig` - Typography settings
  - `UiConfig` - UI component settings
  - `LayoutConfig` - Layout variant settings
  - `StoreBehaviorConfig` - Feature flags

### Events
- `PointOfSale.Core/Events/StoreConfigEvents.cs`
  - `StoreConfigCreatedEvent` - Emitted when config is created
  - `StoreConfigUpdatedEvent` - Emitted when config is updated

### DTOs
- `PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs` - Admin request/response DTOs with validation
- `PointOfSale.API/DTOs/Storefront/StorefrontStoreConfigDtos.cs` - Read-only DTOs for storefront

### Controllers
- `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
  - `GET /api/admin/store-config` - Get store config (creates default if none)
  - `PUT /api/admin/store-config` - Update store config
- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`
  - `GET /api/storefront/store-config` - Read-only endpoint with cache-first pattern

### Services
- `PointOfSale.API/Services/Admin/IAdminStoreConfigService.cs` - Service interface
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs` - Service implementation

### Cache
- `PointOfSale.Infrastructure/Redis/Services/IStoreConfigCacheService.cs` - Cache interface
- `PointOfSale.Infrastructure/Redis/Services/StoreConfigCacheService.cs` - Cache implementation
- `PointOfSale.Infrastructure/Redis/CacheKeyBuilder.cs` - Added store config cache keys

### Database
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` - Added `StoreConfigs` DbSet and entity configuration
- `create_store_configs_table.sql` - SQL migration script

## API Endpoints

### Admin APIs (Requires Auth)

#### GET /api/admin/store-config
Get store configuration for tenant. Creates default if none exists.

**Headers:**
- `X-Tenant-Id: {tenant-guid}` (required)
- `Authorization: Bearer {token}` (required)

**Response:** `AdminStoreConfigResponse`

#### PUT /api/admin/store-config
Update store configuration for tenant.

**Headers:**
- `X-Tenant-Id: {tenant-guid}` (required)
- `Authorization: Bearer {token}` (required)

**Request Body:**
```json
{
  "storeName": "My Store",
  "storeDescription": "Welcome to our store",
  "themeId": "default",
  "theme": {
    "colors": {
      "primary": "#2563eb",
      "secondary": "#0f172a",
      "background": "#ffffff",
      "text": "#020617",
      "muted": "#64748b"
    },
    "font": {
      "family": "Inter",
      "baseSize": "16px"
    },
    "ui": {
      "borderRadius": "8px",
      "buttonStyle": "solid"
    },
    "layout": {
      "productCardVariant": "default",
      "headerVariant": "standard"
    }
  },
  "behavior": {
    "enableGuestCheckout": true,
    "showOutOfStockProducts": false,
    "enableSearch": true
  }
}
```

### Storefront API (Public)

#### GET /api/storefront/store-config
Get store configuration for storefront display (read-only).

**Headers:**
- `X-Tenant-Id: {tenant-guid}` (required)

**Response:** `StorefrontStoreConfigDto`

## Validation Rules

- `ThemeId` must be non-empty
- Color values must be valid hex (#RGB or #RRGGBB)
- Font family must be whitelisted: Inter, Roboto, Open Sans, Lato, Poppins, Montserrat, Outfit, Cairo, Tajawal, Noto Sans Arabic
- Button style must be: solid, outline
- Product card variant must be: default, compact, detailed
- Header variant must be: standard, centered, minimal
- Invalid config → 400 INVALID_STORE_CONFIG

## Cache Strategy

- **Redis Key Pattern:** `{instance}:tenant:{tenantId}:store:config`
- **TTL:** 30 minutes
- **Invalidation:** On admin update
- **Graceful degradation:** Cache failure → DB fallback

## Events

When store config is created or updated, events are emitted:

```csharp
// Created
StoreConfigCreatedEvent {
    TenantId, StoreConfigId, ThemeId, StoreName, CreatedAtUtc
}

// Updated
StoreConfigUpdatedEvent {
    TenantId, StoreConfigId, ThemeId, StoreName, UpdatedAtUtc
}
```

## Database Schema

The schema is managed by EF Core Migration `20251224210903_AddStoreConfigs.cs`.

Uses EF Core Owned Entities pattern - nested configuration objects are mapped to individual columns:

```sql
CREATE TABLE "StoreConfigs" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL UNIQUE,
    "StoreName" varchar(100) NOT NULL,
    "StoreDescription" varchar(500) NULL,
    "ThemeId" varchar(50) NOT NULL,
    
    -- Theme.Colors (owned entity)
    "Theme_Colors_Primary" text NOT NULL,
    "Theme_Colors_Secondary" text NOT NULL,
    "Theme_Colors_Background" text NOT NULL,
    "Theme_Colors_Text" text NOT NULL,
    "Theme_Colors_Muted" text NOT NULL,
    
    -- Theme.Font (owned entity)
    "Theme_Font_Family" text NOT NULL,
    "Theme_Font_BaseSize" text NOT NULL,
    
    -- Theme.Ui (owned entity)
    "Theme_Ui_BorderRadius" text NOT NULL,
    "Theme_Ui_ButtonStyle" text NOT NULL,
    
    -- Theme.Layout (owned entity)
    "Theme_Layout_ProductCardVariant" text NOT NULL,
    "Theme_Layout_HeaderVariant" text NOT NULL,
    
    -- Behavior (owned entity)
    "Behavior_EnableGuestCheckout" boolean NOT NULL,
    "Behavior_ShowOutOfStockProducts" boolean NOT NULL,
    "Behavior_EnableSearch" boolean NOT NULL,
    
    "UpdatedAtUtc" timestamptz NOT NULL,
    "CreatedAtUtc" timestamptz NOT NULL
);

-- Indexes
CREATE UNIQUE INDEX "IX_StoreConfigs_TenantId" ON "StoreConfigs" ("TenantId");
CREATE INDEX "IX_StoreConfigs_ThemeId" ON "StoreConfigs" ("ThemeId");
```

## Logging

All operations log:
- TenantId
- ThemeId
- CacheHit (true/false) for reads
- TraceId/CorrelationId

## Infrastructure Notes

### Schema Management
- **EF Core migration is the single source of truth** for the `StoreConfigs` schema
- Migration: `20251224210903_AddStoreConfigs.cs`
- Manual SQL file (`create_store_configs_table.sql`) is kept for reference only

### Entity Configuration
- Uses EF Core **Owned Entities** pattern for nested types
- Consistent behavior across PostgreSQL (production) and InMemory (tests)
- No provider-specific branching or conditional logic

### Test Infrastructure
- EF Core InMemory provider does not support transactions
- `TransactionIgnoredWarning` is explicitly suppressed in unit tests
- This suppression is documented in `InventoryServiceTests.cs`

### Cache Layer Boundaries
- Infrastructure cache services work with raw JSON strings
- **No API DTOs are referenced from Infrastructure**
- Serialization/deserialization happens in API/Service layer only

## What's NOT Implemented (Per Spec)

- ❌ Multiple themes
- ❌ Theme marketplace
- ❌ CSS generation in backend
- ❌ Per-page overrides
- ❌ Frontend implementation
- ❌ Dynamic JS injection

## Next Steps (Phase 7.2)

- Theme variants support
- Section-specific configuration
- Dynamic CSS variable generation for frontend
