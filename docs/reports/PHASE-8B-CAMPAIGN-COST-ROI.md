# Phase 8B: Campaign Cost & ROI (MVP)

## Completed: 2026-01-21

## Overview

Phase 8B implements a minimal-viable Campaign Cost & ROI system that allows merchants to:
1. **Declare marketing spend** per campaign/period
2. **View calculated ROI** for each campaign
3. **Compare profitability** across marketing channels

## Architectural Principles

### POS as Single Source of Truth
- Marketing costs are **admin-declared** (NOT auto-fetched from ad platforms)
- No Facebook/Google Ads API integration
- Simple manual entry for MVP

### Server-Side Only Calculations
- Frontend is a **PURE RENDERER**
- NO ROI calculations in JavaScript
- All formulas computed by backend

### Immutability Per Period
- Costs are immutable per (Source, Medium, Campaign, PeriodStart)
- Update replaces existing; no history loss

## Components Created

### Backend (C#)

#### Domain Models
- `PointOfSale.Core/Analytics/Attribution/CampaignCost.cs` - Entity for marketing spend
- `PointOfSale.Core/Analytics/Attribution/CampaignRoiResult.cs` - ROI calculation result model

#### Services
- `PointOfSale.Infrastructure/CampaignCost/ICampaignCostService.cs` - CRUD interface
- `PointOfSale.Infrastructure/CampaignCost/CampaignCostService.cs` - PostgreSQL implementation
- `PointOfSale.Infrastructure/CampaignCost/CampaignRoiService.cs` - ROI calculation service

#### API Controller
- `PointOfSale.Api/Controllers/AdminCampaignCostController.cs`

#### Database
- PostgreSQL table `CampaignCosts` with:
  - Unique index: (TenantId, Source, Medium, Campaign, PeriodStartUtc)
  - Query filter for tenant isolation and soft delete

### Frontend (React)

#### Pages
- `CampaignCostsPage.jsx` - CRUD UI for campaign costs
- `RoiDashboardPage.jsx` - ROI summary and table view

#### Components
- `CampaignRoiTable.jsx` - Sortable ROI table with color coding
- `RoiSummaryCards.jsx` - Summary cards showing totals

#### Hooks
- `useCampaignCost.js` - API hooks for costs and ROI

## API Endpoints

### Campaign Costs CRUD
```
GET    /api/admin/analytics/campaign-costs          # List costs
POST   /api/admin/analytics/campaign-costs          # Create/update cost
PUT    /api/admin/analytics/campaign-costs/{id}     # Update cost
DELETE /api/admin/analytics/campaign-costs/{id}     # Delete cost
```

### ROI APIs
```
GET    /api/admin/analytics/roi/summary             # ROI by source (aggregated)
GET    /api/admin/analytics/roi/campaigns           # ROI by campaign (detailed)
```

## ROI Calculation Rules

### Formulas
- **Profit** = Revenue - Cost
- **ROI%** = (Profit / Cost) × 100
- **CPA** = Cost / Orders

### Safety Rules
- If Cost = 0 → ROI = `null` (not infinity)
- Revenue-only campaigns still appear (Cost = 0)
- Cost-only campaigns still appear (Revenue = 0)

### Color Coding
- **Green**: ROI > 0 (profitable)
- **Red**: ROI < 0 (losing money)
- **Gray**: ROI = null or 0 (no data)

## Navigation

Added to sidebar under E-commerce:
- 💰 Campaign Costs → `/analytics/attribution/costs`
- 📈 Marketing ROI → `/analytics/attribution/roi`

## Translations

Added English and Arabic translations for:
- `campaignCost.*` - Cost management UI
- `roi.*` - ROI dashboard UI
- `nav.campaignCosts` / `nav.marketingRoi` - Navigation labels

## Files Modified

1. `ApplicationDbContext.cs` - Added DbSet and entity configuration
2. `Program.cs` - DI registration for services
3. `App.jsx` - Route registration
4. `Sidebar.jsx` - Navigation links
5. `i18n/config.js` - Translation keys (EN/AR)
6. `i18n/locales/en.json` - Translation values
7. `i18n/locales/ar.json` - Arabic translations
8. `modules/analytics/attribution/index.js` - Module exports

## Usage Flow

1. **Navigate** to Marketing Attribution (E-commerce menu)
2. **Click "Campaign Costs"** or "Marketing ROI"
3. **Add costs**: Enter source, medium, campaign, period, and amount
4. **View ROI**: After orders come in, see calculated ROI per campaign
5. **Analyze**: Find which campaigns are profitable vs losing money

## Future Enhancements (Out of Scope for MVP)

- [ ] CSV/Excel import for bulk cost upload
- [ ] Auto-fetch from ad platform APIs
- [ ] Cohort analysis (customer LTV vs CAC)
- [ ] Multi-currency support
- [ ] Budget alerts when spending exceeds target
