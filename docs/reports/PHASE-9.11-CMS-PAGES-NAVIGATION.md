# Phase 9.11: Static CMS Pages, Footer & Navigation Engine

## Implementation Summary

This phase implements a fully dynamic Static CMS Pages system for the Storefront, managed entirely from the POS System (Admin) and consumed by the Storefront via MongoDB + Redis only.

## Architecture Overview

### Core Principles Followed

✅ **POS is the ONLY Admin** - All editing happens inside POS Admin APIs
✅ **Strict CQRS** - PostgreSQL = Write model, MongoDB = Read model, Redis = Hot cache
✅ **Draft / Live Separation** - Draft content never affects customers
✅ **Tenant Isolation** - Every entity includes TenantId, collections are tenant-scoped
✅ **No Auth Scope Creep** - Public pages only (content & SEO)

### Data Flow

```
POS Admin → PostgreSQL (Draft + Live in CustomizationJson)
     ↓
Publish Action (Draft → Live)
     ↓
Event Emitted (CmsPagePublishedEvent / NavigationPublishedEvent)
     ↓
MassTransit Consumer
     ↓
MongoDB Projection (LIVE ONLY)
     ↓
Redis Cache Invalidation
     ↓
Storefront API (Redis → MongoDB)
```

## Files Created

### Backend - Core Events
- `PointOfSale.Core/Events/CmsPageEvents.cs`
  - `CmsPagePublishedEvent` - Emitted when CMS page is published
  - `CmsPageRolledBackEvent` - Emitted when CMS page is rolled back
  - `NavigationPublishedEvent` - Emitted when navigation is published

### Backend - MongoDB Documents
- `PointOfSale.Infrastructure/MongoDB/Documents/StorefrontPageDocument.cs`
  - Collection: `storefront_pages`
  - Indexes: `(tenantId, slug) UNIQUE`, `(tenantId)`
  - Contains: slug, title, SEO metadata, sections

- `PointOfSale.Infrastructure/MongoDB/Documents/StorefrontNavigationDocument.cs`
  - Collection: `storefront_navigation`
  - Index: `(tenantId) UNIQUE`
  - Contains: header links, footer links

### Backend - Projection Consumers
- `PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs`
  - Handles: `CmsPagePublishedEvent`, `CmsPageRolledBackEvent`
  - Projects: Live CMS pages from SQL to MongoDB
  - Invalidates: Redis cache keys

- `PointOfSale.Infrastructure/Consumers/NavigationProjectionConsumer.cs`
  - Handles: `NavigationPublishedEvent`
  - Projects: Live navigation from SQL to MongoDB
  - Invalidates: Redis cache keys

### Backend - DTOs
- `PointOfSale.API/DTOs/Storefront/CmsPageDtos.cs`
  - `CmsPageDto` - Read-only page response
  - `CmsPageSeoDto` - SEO metadata
  - `CmsPageSectionDto` - Content section
  - `NavigationDto` - Header + Footer navigation
  - `NavigationLinkDto` - Navigation link

- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
  - `AdminCmsPageRequest` - Create/update page
  - `AdminCmsPageResponse` - Draft + Live page
  - `AdminNavigationRequest` - Update navigation
  - `AdminNavigationResponse` - Draft + Live navigation

### Backend - Storefront Controllers (Read-Only)
- `PointOfSale.API/Controllers/StorefrontPagesController.cs`
  - `GET /api/storefront/pages/{slug}` - Fetch CMS page
  - `GET /api/storefront/pages` - List all pages
  - `GET /api/storefront/contact` - Contact page shortcut

- `PointOfSale.API/Controllers/StorefrontNavigationController.cs`
  - `GET /api/storefront/navigation` - Header + Footer

### Backend - Admin Controller (POS-Managed)
- `PointOfSale.API/Controllers/AdminCmsPagesController.cs`
  - `GET /api/admin/cms/pages` - List pages
  - `GET /api/admin/cms/pages/{slug}` - Get page
  - `PUT /api/admin/cms/pages/{slug}` - Update page (Draft)
  - `POST /api/admin/cms/pages/{slug}/publish` - Publish page
  - `DELETE /api/admin/cms/pages/{slug}` - Delete page
  - `GET /api/admin/cms/navigation` - Get navigation
  - `PUT /api/admin/cms/navigation` - Update navigation (Draft)
  - `POST /api/admin/cms/navigation/publish` - Publish navigation

### Backend - Admin Service
- `PointOfSale.API/Services/Admin/IAdminCmsService.cs` - Interface
- `PointOfSale.API/Services/Admin/AdminCmsService.cs` - Implementation

### Frontend - Types
- `PointOfSale.Storefront/src/types/cms.ts`
  - TypeScript types matching backend DTOs
  - `resolveNavigationLinkUrl` helper function

### Frontend - Stores
- `PointOfSale.Storefront/src/stores/navigationStore.ts`
  - Zustand store for navigation state
  - `useNavigationStore` hook
  - `selectHeaderLinks`, `selectFooterLinks` selectors
  - `resolveNavLinkUrl` helper function

### Frontend - Components
- `PointOfSale.Storefront/src/components/cms/CmsPage.tsx`
  - Main CMS page renderer
  - Dynamic section rendering (text-block, rich-text, contact-form, image, heading)
  - SEO meta tag updates
  - Loading and error states

- `PointOfSale.Storefront/src/components/cms/CmsPage.css`
  - CMS page styling with skeleton loading, form styling, responsive design

### Frontend - Pages
- `PointOfSale.Storefront/src/pages/CmsPage.tsx`
  - Route wrapper component for `/pages/:slug`
  - Integrates with React Router

### Frontend - API
- Updated `PointOfSale.Storefront/src/services/api/endpoints.ts`
- Updated `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
  - `fetchCmsPage(slug)` - Fetch single page
  - `fetchCmsPages()` - List all pages
  - `fetchContactPage()` - Contact page shortcut
  - `fetchNavigation()` - Header + Footer

### Frontend - Layout Updates
- Updated `PointOfSale.Storefront/src/components/layout/Footer.tsx`
  - Dynamic footer navigation from Zustand store
  - Privacy/Terms links now point to CMS pages

### Frontend - Router
- Updated `PointOfSale.Storefront/src/app/router.tsx`
  - Added `/pages/:slug` route for CMS pages

## Redis Caching Strategy

| Key Pattern | TTL |
|-------------|-----|
| `storefront:page:{tenantSlug}:{slug}` | 30 min |
| `storefront:navigation:{tenantSlug}` | 60 min |
| `storefront:footer:{tenantSlug}` | 60 min |
| `storefront:menu:{tenantSlug}` | 60 min |

Cache invalidation is **event-driven only** - no manual invalidation.

## CustomizationJson Structure

```json
{
  "pages": {
    "home": { "draft": {}, "live": {} },
    "product": { "draft": {}, "live": {} },
    "category": { "draft": {}, "live": {} },
    "cms": {
      "contact": {
        "draft": {
          "title": "Contact Us",
          "seo": { "metaTitle": "...", "metaDescription": "..." },
          "sections": [
            { "type": "text-block", "settings": { "text": "..." } },
            { "type": "contact-form", "settings": { "email": "..." } }
          ]
        },
        "live": { /* Published content */ }
      },
      "about": { "draft": {}, "live": {} },
      "privacy": { "draft": {}, "live": {} },
      "terms": { "draft": {}, "live": {} }
    }
  },
  "navigation": {
    "draft": {
      "header": {
        "links": [
          { "label": "Home", "type": "internal", "path": "/" }
        ]
      },
      "footer": {
        "links": [
          { "label": "About Us", "type": "cms", "slug": "about" }
        ]
      }
    },
    "live": { /* Published navigation */ }
  }
}
```

## Security & Safety Checks

✅ Draft content **NEVER** reaches MongoDB
✅ Draft content **NEVER** cached in Redis
✅ Publish **required** to affect live data
✅ TenantId validated at **every layer**
✅ Rollback supported via Phase 9.6 engine
✅ Event replay is **idempotent**

## Supported CMS Page Types

| Page Type | Slug | Required |
|-----------|------|----------|
| Contact Us | `contact` | ✅ |
| About Us | `about` | ✅ |
| Privacy Policy | `privacy` | ✅ |
| Terms & Conditions | `terms` | ✅ |
| Custom Pages | Any slug | ✅ (future-proof) |

## Supported Section Types

- `text-block` - Rich text content
- `contact-form` - Contact form with email
- `image` - Image section
- `rich-text` - HTML content
- Custom types supported via `settings` object

## Definition of Done

- [x] CMS pages editable from POS
- [x] Draft / Live separation enforced
- [x] MongoDB contains LIVE ONLY
- [x] Footer links fully dynamic
- [x] Storefront reads ZERO SQL
- [x] Tenant isolation verified
- [x] Event replay is idempotent
- [x] Redis invalidates correctly
- [x] SEO metadata dynamic
- [x] No auth logic introduced

## Out of Scope (Phase 10.x)

❌ Login / Register
❌ Wishlist
❌ Social Auth
❌ Customer accounts
❌ Orders history
