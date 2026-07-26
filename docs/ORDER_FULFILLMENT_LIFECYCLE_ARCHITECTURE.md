# Phase OFL: Order Fulfillment Lifecycle Architecture

## Executive Summary

This document describes the Order Fulfillment Lifecycle (OFL) architecture, a formal state machine that controls the physical journey of orders from creation to delivery. The OFL integrates with the existing Payment Lifecycle to create a sealed, auditable Order-to-Cash pipeline.

## Core Objective

Introduce a formal Order Fulfillment State Machine with event-driven integration into the existing Payment Lifecycle.

**Key Integration**: When an order is marked as **Delivered**, the system automatically triggers payment lifecycle actions for COD orders, creating the required ledger journal entries.

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      ORDER FULFILLMENT LIFECYCLE (OFL)                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────┐  ┌───────────┐  ┌────────┐  ┌──────────────┐  ┌───────────┐  │
│   │ CREATED │→│ CONFIRMED │→│ PACKED │→│ OUT_FOR_DELIVERY │→│ DELIVERED │  │
│   └─────────┘  └───────────┘  └────────┘  └──────────────┘  └─────┬─────┘  │
│                                     ↓                             │         │
│                              ┌────────────────┐                   ↓         │
│                              │ FAILED_DELIVERY │         ┌───────────────┐  │
│                              └────────────────┘         │   COMPLETED   │  │
│                                                          └───────────────┘  │
│                              ┌─────────────┐                    │           │
│                              │  CANCELLED  │                    ↓           │
│                              └─────────────┘            ┌───────────────┐  │
│                                                          │   RETURNED    │  │
│                                                          └───────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      │ FulfillmentDeliveredEvent
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      PAYMENT LIFECYCLE INTEGRATION                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   IF Order.PaymentMethod == COD                                             │
│   AND Order.PaymentStatus == Pending                                        │
│   THEN                                                                      │
│      → PaymentLifecycleService.MarkCodDeliveredAsync()                      │
│         → AccountingOutbox.EnqueueCodDeliveredAsync()                       │
│            → LedgerJournal:                                                 │
│               DEBIT  1130 COD Clearing                                      │
│               CREDIT 4020 E-commerce Revenue                                │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Absolute Laws

| # | Rule | Enforcement |
|---|------|-------------|
| 1 | Order fulfillment status must NEVER be changed directly | `FulfillmentStatusSaveChangesInterceptor` |
| 2 | UI must NEVER write FulfillmentStatus | Architecture tests |
| 3 | Controllers must NEVER write FulfillmentStatus | Architecture tests |
| 4 | All order state transitions must go through `IOrderFulfillmentService` | Interceptor + Tests |
| 5 | Delivery events MUST emit `FulfillmentDeliveredEvent` | Code review + Tests |
| 6 | FulfillmentDeliveredEventConsumer MUST call PaymentLifecycleService for COD | Architecture tests |
| 7 | Money recognition MUST still happen only inside PaymentLifecycle → Outbox → Ledger | Existing enforcement |

---

## Domain Layer

### 1. FulfillmentStatus Enum

**Location**: `PointOfSale.Core.Enums.FulfillmentStatus`

```csharp
public enum FulfillmentStatus
{
    Created = 0,
    Confirmed = 1,
    Packed = 2,
    OutForDelivery = 3,
    Delivered = 4,
    Completed = 5,
    Returned = 6,
    FailedDelivery = 7,
    Cancelled = 8
}
```

### 2. State Machine Transitions

| From | Allowed Transitions |
|------|---------------------|
| Created | Confirmed, Cancelled |
| Confirmed | Packed, Cancelled |
| Packed | OutForDelivery, Cancelled |
| OutForDelivery | Delivered, FailedDelivery |
| Delivered | Completed, Returned |
| FailedDelivery | OutForDelivery (retry), Cancelled |
| Completed | (terminal) |
| Returned | (terminal) |
| Cancelled | (terminal) |

### 3. Domain Events

| Event | Published When | Payment Integration |
|-------|----------------|---------------------|
| `FulfillmentConfirmedEvent` | Order confirmed for fulfillment | None |
| `OrderPackedEvent` | Order packed and ready | None |
| `OrderOutForDeliveryEvent` | Order handed to carrier | None |
| `FulfillmentDeliveredEvent` | Order delivered to customer | **COD → MarkCodDeliveredAsync** |
| `OrderCompletedEvent` | Order lifecycle complete | None |
| `OrderReturnedEvent` | Order returned by customer | May trigger refund |
| `OrderDeliveryFailedEvent` | Delivery attempt failed | None |
| `OrderFulfillmentCancelledEvent` | Order cancelled | Release inventory |

---

## Service Layer

### IOrderFulfillmentService

**Location**: `PointOfSale.Core.Interfaces.Fulfillment.IOrderFulfillmentService`

**Methods**:
- `ConfirmAsync(orderId)` - Created → Confirmed
- `PackAsync(orderId)` - Confirmed → Packed
- `MarkOutForDeliveryAsync(orderId)` - Packed → OutForDelivery
- `MarkDeliveredAsync(orderId)` - OutForDelivery → Delivered (triggers payment pipeline)
- `MarkCompletedAsync(orderId)` - Delivered → Completed
- `MarkReturnedAsync(orderId, reason)` - Delivered → Returned
- `MarkDeliveryFailedAsync(orderId, reason)` - OutForDelivery → FailedDelivery
- `CancelAsync(orderId, reason)` - Various → Cancelled
- `GetAvailableActionsAsync(orderId)` - Returns available state transitions

### Implementation

**Location**: `PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService`

Key responsibilities:
1. Load order from database
2. Validate state transition is allowed
3. Update `Order.FulfillmentStatus` (within authorized context)
4. Persist changes
5. Publish domain event
6. Return result

---

## Payment Integration Rules

### Rule 1: COD Orders

```
IF Order.PaymentMethod == COD
AND Order.PaymentStatus == Pending
AND FulfillmentDeliveredEvent occurs
THEN
  → IPaymentLifecycleService.MarkCodDeliveredAsync(orderId)
```

**Resulting Journal**:
```
DEBIT  1130 COD Clearing      [OrderTotal]
CREDIT 4020 E-commerce Revenue [OrderTotal]
```

### Rule 2: Wallet Orders (Manual Verification)

```
IF Order.PaymentMethod == Wallet
AND Order.PaymentStatus == AwaitingVerification
AND FulfillmentDeliveredEvent occurs
THEN
  → Log warning (manual approval still required)
  → (Optional) Auto-approve if business rules allow
```

### Rule 3: Online Paid Orders (Stripe/Paymob)

```
IF Order.PaymentStatus IN (Collected, Settled)
AND OrderDeliveredEvent occurs
THEN
  → No financial action (already collected via webhook)
  → Delivery affects fulfillment only
```

---

## API Layer

### OrderFulfillmentController

**Base Route**: `/api/orders/{orderId}/fulfillment`

| Endpoint | Method | Transition |
|----------|--------|------------|
| `/actions` | GET | Get available actions |
| `/confirm` | POST | Created → Confirmed |
| `/pack` | POST | Confirmed → Packed |
| `/out-for-delivery` | POST | Packed → OutForDelivery |
| `/delivered` | POST | OutForDelivery → Delivered |
| `/complete` | POST | Delivered → Completed |
| `/returned` | POST | Delivered → Returned |
| `/failed-delivery` | POST | OutForDelivery → FailedDelivery |
| `/retry-delivery` | POST | FailedDelivery → OutForDelivery |
| `/cancel` | POST | Various → Cancelled |

### Get Available Actions Response

```json
{
  "orderId": "ab00cbc4-eca7-4c71-a412-2f320f0583a7",
  "orderNumber": "ORD-905D-2026-00002",
  "currentStatus": "Packed",
  "currentStatusValue": 2,
  "canConfirm": false,
  "canPack": false,
  "canOutForDelivery": true,
  "canDeliver": false,
  "canComplete": false,
  "canMarkFailed": false,
  "canCancel": true,
  "availableActions": [
    { "code": "out-for-delivery", "label": "Out for Delivery", "targetStatus": "OutForDelivery" },
    { "code": "cancel", "label": "Cancel Order", "targetStatus": "Cancelled", "requiresConfirmation": true }
  ]
}
```

---

## Frontend Integration

**Location**: `PointOfSale.Web/src/pages/Orders.jsx`

### Features Added

1. **Fulfillment Status Display**
   - Purple-bordered section showing current fulfillment status
   - Status badge with icon and color coding

2. **Action Buttons** (dynamically shown based on `canX` flags)
   - ✓ Confirm, 📦 Pack, 🚚 Out for Delivery
   - 🏠 Delivered (with COD payment hint), ✓✓ Complete
   - ❌ Failed Delivery (with reason modal)

3. **COD Payment Integration**
   - "Delivered" button shows `(+💵)` indicator for COD orders
   - Success message indicates payment collection

### State Variables
```javascript
const [fulfillmentActions, setFulfillmentActions] = useState(null)
const [fulfillmentLoading, setFulfillmentLoading] = useState(false)
const [showDeliveryFailedModal, setShowDeliveryFailedModal] = useState(false)
const [deliveryFailedReason, setDeliveryFailedReason] = useState('')
```

### API Calls
```javascript
// Fetch available actions
api.get(`/orders/${orderId}/fulfillment/actions`)

// Execute transitions
api.post(`/orders/${orderId}/fulfillment/confirm`, {})
api.post(`/orders/${orderId}/fulfillment/pack`, {})
api.post(`/orders/${orderId}/fulfillment/out-for-delivery`, {})
api.post(`/orders/${orderId}/fulfillment/delivered`, {})
api.post(`/orders/${orderId}/fulfillment/complete`, {})
api.post(`/orders/${orderId}/fulfillment/failed-delivery`, { reason })
```

---

## Runtime Protection

### FulfillmentStatusSaveChangesInterceptor

**Location**: `PointOfSale.Infrastructure.Data.FulfillmentStatusSaveChangesInterceptor`

- Blocks any direct `order.FulfillmentStatus = ...` changes
- Uses `AsyncLocal<bool>` for authorized context management
- Only `OrderFulfillmentService` can enable authorized context
- Throws `InvalidOperationException` for unauthorized changes

### Order Entity Enhancement

```csharp
// Backing field for change tracking
private FulfillmentStatus _fulfillmentStatus = FulfillmentStatus.Created;

// Property with change detection
public FulfillmentStatus FulfillmentStatus 
{ 
    get => _fulfillmentStatus;
    set
    {
        if (_fulfillmentStatus != value)
        {
            _originalFulfillmentStatus ??= _fulfillmentStatus;
            _fulfillmentStatus = value;
        }
    }
}

// Change tracking
public bool FulfillmentStatusChanged => ...;
public void AcceptFulfillmentStatusChange() => ...;

// Computed property for queries
public bool IsPhysicallyDelivered => ...;
```

---

## Architecture Tests

**Location**: `PointOfSale.Tests.Architecture.OrderFulfillmentArchitectureTests`

| Test | Purpose |
|------|---------|
| `OrderFulfillmentService_ShouldExist` | Verify service implementation exists |
| `IOrderFulfillmentService_ShouldExist` | Verify interface exists |
| `FulfillmentStatus_ShouldHaveRequiredValues` | Verify all states exist |
| `FulfillmentDeliveredEvent_ShouldExist` | Verify event exists |
| `FulfillmentDeliveredEvent_ShouldHavePaymentIntegrationProperties` | Verify event has PaymentMethod, PaymentStatus |
| `FulfillmentDeliveredEventConsumer_ShouldExist` | Verify consumer exists |
| `Controllers_ShouldNotDirectlySetFulfillmentStatus` | Block direct writes in controllers |
| `NoClass_ShouldSet_FulfillmentStatus_Directly_ExceptOrderFulfillmentService` | Block direct writes everywhere |
| `FulfillmentDeliveredEventConsumer_ShouldCallPaymentLifecycleService` | Verify integration |
| `DeliveredState_MustTriggerPaymentLifecycle_ForCodOrders` | Verify COD pipeline |

---

## Database Migration

### SQL Migration

**Location**: `PointOfSale.Infrastructure/Migrations/Sql/PhaseOFL_AddFulfillmentStatus.sql`

1. Add `FulfillmentStatus` column (default: Confirmed)
2. Backfill from legacy `OrderStatus`:
   - Delivered → Delivered
   - Completed → Completed
   - Shipped → OutForDelivery
   - Processing → Packed
   - Cancelled → Cancelled
   - Refunded → Returned
   - Pending → Created

---

## Invariants

| Rule | Enforcement |
|------|-------------|
| Order Delivered ALWAYS means FulfillmentStatus = Delivered | State machine |
| No Delivered order may remain PaymentStatus = Pending (for COD) | Event handler |
| No PaymentStatus transition may occur without ledger journal | PaymentLifecycleService |
| COD revenue recognized exactly at delivery | OrderDeliveredEvent → MarkCodDeliveredAsync |
| Online payment already collected - delivery is fulfillment only | Event handler check |

---

## File Manifest

| File | Purpose |
|------|---------|
| `PointOfSale.Core/Enums/FulfillmentStatus.cs` | Enum + extensions |
| `PointOfSale.Core/Events/OrderFulfillmentEvents.cs` | Domain events |
| `PointOfSale.Core/Interfaces/Fulfillment/IOrderFulfillmentService.cs` | Service interface |
| `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs` | Service implementation |
| `PointOfSale.Infrastructure/Data/FulfillmentStatusSaveChangesInterceptor.cs` | Runtime protection |
| `PointOfSale.Infrastructure/Consumers/FulfillmentDeliveredEventConsumer.cs` | Payment integration |
| `PointOfSale.API/Controllers/OrderFulfillmentController.cs` | API endpoints |
| `PointOfSale.Tests/Architecture/OrderFulfillmentArchitectureTests.cs` | Enforcement tests |
| `PointOfSale.Infrastructure/Migrations/Sql/PhaseOFL_AddFulfillmentStatus.sql` | Database migration |
| `PointOfSale.Web/src/pages/Orders.jsx` | Frontend fulfillment UI |
| `PointOfSale.Infrastructure/Migrations/Sql/PhaseOFL_AddFulfillmentStatus.sql` | Database migration |

---

## Certification

After Phase OFL:

✅ Orders move only through controlled lifecycle  
✅ Manual fulfillment becomes safe  
✅ COD revenue recognized exactly at delivery  
✅ Wallet approvals tied to delivery  
✅ Ledger remains the only financial truth  
✅ Zero silent money movements  
✅ Enterprise-grade order-to-cash pipeline  

**Order → Fulfillment → Payment → Ledger = SEALED PIPELINE**
