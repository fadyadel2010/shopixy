# Paymob Integration Summary

## Phase 10.4.D5: Paymob Payment Gateway Integration

This document summarizes the production-grade Paymob payment integration implemented for the multi-tenant POS + Storefront system.

---

## 1. Architecture Overview

### Flow Diagram
```
┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│  Storefront  │──▶│   Checkout   │──▶│  Paymob API  │──▶│   Webhook    │
│              │   │   Service    │   │              │   │   Handler    │
└──────────────┘   └──────────────┘   └──────────────┘   └──────────────┘
       │                  │                  │                  │
       │                  │                  │                  │
       ▼                  ▼                  ▼                  ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│    Cart      │   │    Order     │   │   Payment    │   │   Order      │
│  (Redis)     │   │    (SQL)     │   │   Intent     │   │  Status      │
└──────────────┘   └──────────────┘   │    (SQL)     │   │  Update      │
                                      └──────────────┘   └──────────────┘
```

### Key Principles (Inherited from Stripe Pattern)
1. **Order-First Architecture**: Orders are created BEFORE payment
2. **Webhook-Authoritative**: Only webhooks update payment status to Paid
3. **Tenant Isolation**: Each tenant has their own Paymob configuration
4. **Encrypted Secrets**: API keys stored encrypted via Data Protection API
5. **Idempotent Operations**: Redis + DB uniqueness constraints

---

## 2. Backend Components

### 2.1 Service Layer

#### `IPaymobPaymentService.cs`
Interface defining the Paymob payment contract:
- `CreatePaymentIntentAsync()` - Creates payment intention with Paymob
- `GetPaymentStatusAsync()` - Returns current payment status for polling
- `ProcessWebhookAsync()` - Handles Paymob webhook events
- `ValidateCallbackAsync()` - Validates customer redirect callback
- `GetConfigurationStatusAsync()` - Checks tenant Paymob configuration

#### `PaymobPaymentService.cs`
Full implementation including:
- Paymob API authentication (token-based)
- Order creation on Paymob platform
- Payment key generation
- HMAC-SHA512 signature verification
- Idempotency via Redis
- Domain event publishing

### 2.2 Controllers

#### `PaymobWebhooksController.cs`
Handles Paymob server-to-server callbacks:
- `POST /api/webhooks/paymob?tenant={tenantId}` - Main webhook endpoint
- `GET /api/webhooks/paymob/callback` - Customer redirect handler
- `GET /api/webhooks/paymob` - Health check for Paymob verification

#### `StorefrontPaymobPaymentsController.cs`
Storefront-facing APIs:
- `GET /api/storefront/payments/paymob/status/{orderId}` - Poll payment status
- `GET /api/storefront/payments/paymob/status` - Check Paymob availability
- `POST /api/storefront/payments/paymob/intent/{orderId}` - Create intent manually

### 2.3 Configuration

#### `TenantPaymobConfiguration`
Encrypted configuration stored in `TenantPaymentMethod.ConfigEncrypted`:
```csharp
public class TenantPaymobConfiguration
{
    public string ApiKey { get; set; }        // From Paymob dashboard
    public string IntegrationId { get; set; } // Payment integration ID
    public string IframeId { get; set; }      // iFrame ID for embedding
    public string HmacSecret { get; set; }    // For webhook verification
}
```

---

## 3. Paymob API Integration

### API Endpoints Used
| Endpoint | Purpose |
|----------|---------|
| `POST /api/auth/tokens` | Get authentication token |
| `POST /api/ecommerce/orders` | Create order on Paymob |
| `POST /api/acceptance/payment_keys` | Generate payment key |

### iFrame URL Format
```
https://accept.paymob.com/api/acceptance/iframes/{iframeId}?payment_token={paymentKey}
```

### HMAC Verification
Paymob uses HMAC-SHA512 for webhook signature verification:
```csharp
// Concatenate specific transaction fields in order
var fields = new[] {
    amount_cents, created_at, currency, error_occured,
    has_parent_transaction, id, integration_id, is_3d_secure,
    is_auth, is_capture, is_refunded, is_standalone_payment,
    is_voided, order_id, owner, pending,
    source_data.pan, source_data.sub_type, source_data.type, success
};
var concatenated = string.Join("", fields);
var hmac = HMACSHA512(hmacSecret, concatenated);
```

---

## 4. Storefront Integration

### 4.1 PaymobPaymentForm Component
Located at: `PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx`

Features:
- Redirect-based payment flow
- Payment status polling after return
- Clean UI with loading states
- RTL support for Arabic

### 4.2 Checkout Flow
1. Customer selects Paymob payment method
2. CheckoutService creates order and calls `PaymobPaymentService.CreatePaymentIntentAsync()`
3. Response includes `paymobIframeUrl` and `paymobPaymentKey`
4. Customer is redirected to Paymob payment page
5. After payment, customer returns to storefront
6. Storefront polls `/api/storefront/payments/paymob/status/{orderId}`
7. Webhook confirms payment and updates order

---

## 5. Admin Configuration

### Payment Methods Page
Location: `PointOfSale.Web/src/pages/PaymentMethods.jsx`

Paymob configuration fields:
- **API Key** (sensitive - masked)
- **Integration ID**
- **iFrame ID**
- **HMAC Secret** (sensitive - masked)

Configuration is encrypted and stored in `TenantPaymentMethod.ConfigEncrypted`.

---

## 6. Events & Observability

### Domain Events Published
| Event | When Published |
|-------|---------------|
| `PaymentIntentCreatedEvent` | After creating Paymob payment intention |
| `PaymentCapturedEvent` | After webhook confirms successful payment |
| `PaymentFailedEvent` | After webhook reports payment failure |
| `OrderPaidEvent` | After payment is confirmed |

### Logging
All operations include structured logs with:
- `TenantId`
- `OrderId`
- `CorrelationId`
- `PaymobOrderId`
- `TransactionId`

**Never logged**: API keys, secrets, or tokens

---

## 7. Security Considerations

### Secrets Management
- All Paymob credentials stored encrypted in PostgreSQL
- Decryption only happens server-side, on-demand
- Secrets never exposed to:
  - Frontend
  - MongoDB
  - Redis
  - Logs

### Webhook Security
- HMAC-SHA512 signature verification required
- Invalid signatures return 401 Unauthorized
- All webhooks logged for audit

### Idempotency
- Payment intent creation: Redis key `paymob:intent:{tenantId}:{orderId}`
- Webhook processing: Redis key `paymob:webhook:{transactionId}`
- TTL: 24-72 hours

---

## 8. Error Handling

### Exception Types
| Exception | HTTP Code | Meaning |
|-----------|-----------|---------|
| `PaymobNotConfiguredException` | 400 | Tenant hasn't configured Paymob |
| `PaymobApiException` | 502 | Paymob API call failed |
| `PaymobWebhookSignatureException` | 401 | Invalid HMAC signature |
| `PaymobAuthenticationException` | 401 | Failed to authenticate with Paymob |
| `PaymentOrderNotFoundException` | 404 | Order doesn't exist |
| `PaymentOrderInvalidStateException` | 409 | Order not in expected state |

---

## 9. Webhook URL Configuration

Configure in Paymob Dashboard:
```
https://your-domain.com/api/webhooks/paymob?tenant={TENANT_ID}
```

The tenant ID must be included as a query parameter for multi-tenant routing.

---

## 10. Testing Checklist

### Configuration
- [ ] Paymob payment method exists in database
- [ ] API Key, Integration ID, iFrame ID configured
- [ ] HMAC Secret configured for webhook verification
- [ ] IsEnabled = true for the payment method

### Payment Flow
- [ ] Checkout with Paymob creates order first
- [ ] iFrame URL generated correctly
- [ ] Customer can complete payment on Paymob page
- [ ] Webhook received and processed
- [ ] Order status updated to Paid
- [ ] Storefront shows success after polling

### Edge Cases
- [ ] Duplicate webhook handling (idempotent)
- [ ] Customer closes browser mid-payment
- [ ] Webhook arrives before redirect
- [ ] Invalid HMAC signature rejected
- [ ] Missing configuration handled gracefully

---

## 11. Comparison with Stripe Integration

| Feature | Stripe | Paymob |
|---------|--------|--------|
| API Style | Payment Intents API | Order/Key Flow |
| UI Integration | Embedded (Elements) | Redirect (iFrame) |
| Signature Algorithm | Stripe-Signature header | HMAC-SHA512 |
| Amount Format | Cents | Piasters (smallest unit) |
| Service Location | `/Services/Payments/Stripe/` | `/Services/Payments/Paymob/` |

Both integrations follow identical architectural patterns:
- Order-first flow
- Webhook-authoritative status
- Tenant isolation
- Encrypted secrets
- Idempotent operations
- Event-driven updates

---

## 12. Files Created/Modified

### New Files
- `PointOfSale.API/Services/Payments/Paymob/IPaymobPaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Controllers/PaymobWebhooksController.cs`
- `PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs`
- `PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx`
- `PAYMOB_INTEGRATION_SUMMARY.md` (this file)

### Modified Files
- `PointOfSale.API/Program.cs` - Added Paymob service registration
- `PointOfSale.API/Services/Checkout/CheckoutService.cs` - Added PAYMOB handling
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs` - Added Paymob response fields
- `PointOfSale.Web/src/pages/PaymentMethods.jsx` - Added HMAC Secret field

---

## 13. Next Steps

1. **Storefront Integration**: Wire up `PaymobPaymentForm` in checkout flow
2. **Refund Support**: Add refund functionality (future phase)
3. **Additional Payment Methods**: Support for mobile wallets via Paymob
4. **Sandbox Testing**: Verify with Paymob test credentials
5. **Production Deployment**: Configure production webhook URLs
