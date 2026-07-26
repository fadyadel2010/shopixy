# Shipping Module - Carrier-Agnostic Architecture

## Phase: Carrier-Agnostic Shipping Module

**Created:** 2026-01-21  
**Status:** In Progress

---

## Executive Summary

Design and implement a **carrier-agnostic Shipping Module** where:
- ✅ POS is the single source of truth
- ✅ Orders are NOT coupled to shipping providers
- ✅ Any shipping company can be plugged in or removed
- ✅ Supports COD, tracking, labels, rate calculation
- ❌ No provider-specific logic leaks into Order domain
- ❌ No hardcoding per carrier

---

## Core Architecture Principle (NON-NEGOTIABLE)

```
Order ≠ Shipment ≠ Carrier

Order
 └── Shipment (abstract)
       └── Carrier Adapter (pluggable)
```

1. **Order** requests shipping
2. **Shipping module** decides HOW
3. **Carrier adapter** executes

---

## High-Level Architecture

```
Checkout / Order Placement
        ↓
ShippingQuoteService
        ↓
ShippingRuleEngine (optional)
        ↓
CarrierSelector
        ↓
IShippingCarrierAdapter
        ├── AramexAdapter
        ├── BostaAdapter
        ├── MylerzAdapter
        └── DHLAdapter
        ↓
Shipment Created
        ↓
Tracking & Status Sync (Webhook / Polling)
```

---

## Implementation Plan

### Phase 1: Core Infrastructure
1. Update `ShipmentStatus` enum with new statuses
2. Update `Shipment` entity with carrier-agnostic fields
3. Create carrier adapter interface `IShippingCarrierAdapter`
4. Create normalized DTOs (ShippingQuoteResult, CreateShipmentResult, etc.)
5. Create carrier configuration entity `CarrierConfiguration`

### Phase 2: Adapter Infrastructure
1. Create base adapter class
2. Create carrier registry
3. Create carrier selector service
4. Create shipping quote service

### Phase 3: Real Adapter Implementation
1. Implement Bosta adapter (Egypt market)
2. Add webhook endpoint for carrier callbacks
3. Add tracking polling background job

### Phase 4: API Endpoints
1. Storefront Quote API
2. Admin carrier management
3. Webhook receivers

### Phase 5: Analytics Integration
1. ShipmentCreated event
2. ShipmentDelivered event
3. ShipmentFailed event

---

## Files to Create/Modify

### Core Layer (PointOfSale.Core)
- `Enums/ShipmentStatus.cs` - Update with new statuses
- `Entities/Shipment.cs` - Update with carrier fields
- `Entities/CarrierConfiguration.cs` - NEW
- `Events/ShipmentEvents.cs` - Update with new events

### Infrastructure Layer (PointOfSale.Infrastructure)
- `Shipping/` - NEW directory
  - `IShippingCarrierAdapter.cs`
  - `ShippingCarrierRegistry.cs`
  - `CarrierSelectorService.cs`
  - `ShippingQuoteService.cs`
  - `Adapters/`
    - `BostaAdapter.cs`
    - `AramexAdapter.cs` (stub)
  - `Configuration/`
    - `CarrierConfigurationSettings.cs`
  - `BackgroundJobs/`
    - `TrackingSyncJob.cs`

### API Layer (PointOfSale.API)
- `DTOs/Shipping/` - Update
  - `ShippingQuoteDtos.cs` - NEW
  - `CarrierConfigDtos.cs` - NEW
- `Controllers/`
  - `StorefrontShippingQuotesController.cs` - NEW
  - `AdminCarrierConfigController.cs` - NEW
  - `ShippingWebhooksController.cs` - NEW
- `Services/Shipping/`
  - `IShippingQuoteService.cs` - NEW
  - `ShippingQuoteService.cs` - NEW

---

## Deliverables Checklist

- [ ] Shipping domain models (updated)
- [ ] Carrier adapter interface
- [ ] At least 1 real adapter (Bosta)
- [ ] Quote API (storefront)
- [ ] Shipment creation flow (carrier-integrated)
- [ ] Tracking sync (webhook + polling)
- [ ] Admin UI for carrier configuration
- [ ] Analytics events integration
