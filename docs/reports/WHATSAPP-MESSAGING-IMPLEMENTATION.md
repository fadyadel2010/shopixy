# WhatsApp Web Messaging Implementation Plan

## Executive Summary

This document outlines the complete implementation plan for a **WhatsApp Web-based messaging capability** integrated into the multi-tenant POS & E-commerce platform. The feature is:

- **Fully optional** and tenant-controlled
- **Opt-in only** - zero behavioral changes if not enabled
- **WhatsApp Web-only** - no external APIs (Meta Cloud, Twilio, etc.)
- **Event-driven** using MassTransit + RabbitMQ
- **Multi-tenant isolated** with strict security boundaries

---

## 1. Architecture Overview

### 1.1 System Context

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           POS Platform                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────┐    ┌─────────────────┐    ┌───────────────────────────┐   │
│  │ Order       │───▶│ MassTransit     │───▶│ WhatsApp Messaging        │   │
│  │ Created     │    │ Event Bus       │    │ Consumer                  │   │
│  └─────────────┘    └─────────────────┘    └───────────┬───────────────┘   │
│                                                         │                    │
│  ┌─────────────┐                           ┌───────────▼───────────────┐   │
│  │ Phone       │───────────────────────────▶│ WhatsApp Web Engine      │   │
│  │ Verification│                            │ (Headless Browser)       │   │
│  └─────────────┘                            └───────────┬───────────────┘   │
│                                                         │                    │
│                     ┌───────────────────────────────────┼───────────────┐   │
│                     │                                   │               │   │
│              ┌──────▼──────┐  ┌──────────────┐  ┌──────▼──────┐        │   │
│              │ PostgreSQL  │  │ Redis        │  │ MongoDB     │        │   │
│              │ (Config)    │  │ (Sessions)   │  │ (Logs)      │        │   │
│              └─────────────┘  └──────────────┘  └─────────────┘        │   │
│                                                                         │   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Technology Stack (Aligned with Existing Platform)

| Component | Technology | Purpose |
|-----------|------------|---------|
| Event Bus | MassTransit + RabbitMQ | Event-driven messaging |
| Write Model | PostgreSQL | WhatsApp accounts, configuration |
| Session Store | Redis | Encrypted sessions, OTP, rate limits |
| Message Logs | MongoDB | Append-only message history |
| Search/Analytics | Elasticsearch | Message search, delivery metrics |
| WhatsApp Engine | Playwright/.NET | Headless browser automation |
| Background Jobs | Hangfire | Session heartbeat, cleanup |

---

## 2. Domain Model

### 2.1 PostgreSQL Entities (Write Model)

#### WhatsAppAccount (Tenant-Owned)

```csharp
namespace PointOfSale.Core.Entities.WhatsApp;

/// <summary>
/// Represents a WhatsApp account configured for a tenant.
/// Each tenant can have multiple accounts but only one active per use case.
/// </summary>
public class WhatsAppAccount : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// WhatsApp phone number in E.164 format (e.g., +201234567890)
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for admin identification
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Current account status
    /// </summary>
    public WhatsAppAccountStatus Status { get; set; } = WhatsAppAccountStatus.PendingQR;
    
    /// <summary>
    /// Whether this account is enabled for messaging
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Use for order confirmation messages
    /// </summary>
    public bool UseForOrderConfirmation { get; set; } = false;
    
    /// <summary>
    /// Use for OTP verification messages
    /// </summary>
    public bool UseForOtpVerification { get; set; } = false;
    
    /// <summary>
    /// Last successful connection timestamp
    /// </summary>
    public DateTime? LastConnectedAtUtc { get; set; }
    
    /// <summary>
    /// Last disconnection timestamp
    /// </summary>
    public DateTime? LastDisconnectedAtUtc { get; set; }
    
    /// <summary>
    /// Reason for current status (e.g., ban reason, disconnect reason)
    /// </summary>
    public string? StatusReason { get; set; }
    
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    
    // Navigation
    public virtual Tenant Tenant { get; set; } = null!;
}

public enum WhatsAppAccountStatus
{
    /// <summary>
    /// Waiting for QR code scan
    /// </summary>
    PendingQR = 0,
    
    /// <summary>
    /// Successfully connected and operational
    /// </summary>
    Connected = 1,
    
    /// <summary>
    /// Temporarily disconnected (will auto-reconnect)
    /// </summary>
    Disconnected = 2,
    
    /// <summary>
    /// Banned by WhatsApp (requires new number)
    /// </summary>
    Banned = 3,
    
    /// <summary>
    /// Manually disabled by admin
    /// </summary>
    Disabled = 4
}
```

### 2.2 Redis Structures (Session & Rate Limiting)

#### WhatsApp Session (Encrypted)

```
Key:    whatsapp:session:{tenantId}:{accountId}
TTL:    30 days (refreshed on activity)
Value:  {
          "encryptedPayload": "<encrypted session JSON>",
          "lastHeartbeatUtc": "2026-01-29T12:00:00Z",
          "isValid": true,
          "browserInstanceId": "guid"
        }
```

#### OTP Storage

```
Key:    whatsapp:otp:{tenantId}:{phone}:{purpose}
TTL:    5 minutes
Value:  {
          "code": "123456",
          "attempts": 0,
          "maxAttempts": 3,
          "createdAtUtc": "2026-01-29T12:00:00Z"
        }
```

#### Rate Limiting Buckets

```
# Per-tenant daily limit
Key:    whatsapp:ratelimit:{tenantId}:daily
TTL:    24 hours
Value:  current count

# Per-account hourly limit (anti-ban)
Key:    whatsapp:ratelimit:{accountId}:hourly
TTL:    1 hour
Value:  current count

# Per-phone cooldown (prevent spam to same recipient)
Key:    whatsapp:ratelimit:{tenantId}:{targetPhone}:cooldown
TTL:    60 seconds
Value:  1
```

### 2.3 MongoDB Documents (Message Logs)

#### WhatsAppMessageLog

```csharp
namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// Immutable message log document for audit and troubleshooting.
/// </summary>
public class WhatsAppMessageLogDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [BsonElement("tenantId")]
    public string TenantId { get; set; } = string.Empty;
    
    [BsonElement("accountId")]
    public string AccountId { get; set; } = string.Empty;
    
    [BsonElement("targetPhone")]
    public string TargetPhone { get; set; } = string.Empty;
    
    /// <summary>
    /// Masked phone for privacy (e.g., +20123****890)
    /// </summary>
    [BsonElement("maskedPhone")]
    public string MaskedPhone { get; set; } = string.Empty;
    
    [BsonElement("messageType")]
    public string MessageType { get; set; } = string.Empty; // ORDER_CONFIRMATION, OTP
    
    [BsonElement("relatedEntityId")]
    public string? RelatedEntityId { get; set; } // OrderId or CustomerId
    
    [BsonElement("relatedEntityType")]
    public string? RelatedEntityType { get; set; } // Order, OnlineCustomer
    
    /// <summary>
    /// Snapshot of message content (for debugging)
    /// </summary>
    [BsonElement("payloadSnapshot")]
    public BsonDocument? PayloadSnapshot { get; set; }
    
    [BsonElement("status")]
    public string Status { get; set; } = "PENDING"; // PENDING, SENT, FAILED, SKIPPED
    
    [BsonElement("failureReason")]
    public string? FailureReason { get; set; }
    
    [BsonElement("sendAttempts")]
    public int SendAttempts { get; set; } = 0;
    
    [BsonElement("lastAttemptAtUtc")]
    public DateTime? LastAttemptAtUtc { get; set; }
    
    [BsonElement("sentAtUtc")]
    public DateTime? SentAtUtc { get; set; }
    
    [BsonElement("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Idempotency key to prevent duplicate sends
    /// </summary>
    [BsonElement("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;
    
    [BsonElement("correlationId")]
    public string? CorrelationId { get; set; }
}
```

---

## 3. Event Contracts

### 3.1 WhatsApp Domain Events

```csharp
namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Triggered when order is created AND tenant has WhatsApp order confirmation enabled.
/// Domain layer checks feature flags before emitting this event.
/// </summary>
public class WhatsAppOrderConfirmationRequestedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemSnapshot> Items { get; set; } = new();
    public string? ShippingAddress { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
}

/// <summary>
/// Triggered when phone verification is requested AND tenant has WhatsApp OTP enabled.
/// Falls back to SMS/Email if WhatsApp is not available.
/// </summary>
public class WhatsAppOtpRequestedEvent : IntegrationEvent
{
    public Guid CustomerId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 5;
    public string Purpose { get; set; } = string.Empty; // PHONE_VERIFICATION, LOGIN_2FA
}

/// <summary>
/// Raised when WhatsApp account status changes.
/// Used for admin notifications and session management.
/// </summary>
public class WhatsAppAccountStatusChangedEvent : IntegrationEvent
{
    public Guid AccountId { get; set; }
    public WhatsAppAccountStatus OldStatus { get; set; }
    public WhatsAppAccountStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Raised when a message is successfully sent.
/// Used for analytics and delivery tracking.
/// </summary>
public class WhatsAppMessageSentEvent : IntegrationEvent
{
    public string MessageLogId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string TargetPhone { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
}

/// <summary>
/// Raised when a message fails after all retries.
/// Used for alerting and fallback triggering.
/// </summary>
public class WhatsAppMessageFailedEvent : IntegrationEvent
{
    public string MessageLogId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public int Attempts { get; set; }
}
```

---

## 4. Service Boundaries

### 4.1 Service Decomposition

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           WhatsApp Module                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Admin API Layer (PointOfSale.API)                                     │  │
│  │                                                                        │  │
│  │  • WhatsAppAdminController                                            │  │
│  │    - GET  /api/admin/whatsapp/accounts                                │  │
│  │    - POST /api/admin/whatsapp/accounts                                │  │
│  │    - PUT  /api/admin/whatsapp/accounts/{id}/enable                    │  │
│  │    - PUT  /api/admin/whatsapp/accounts/{id}/disable                   │  │
│  │    - GET  /api/admin/whatsapp/accounts/{id}/qr                        │  │
│  │    - GET  /api/admin/whatsapp/accounts/{id}/status                    │  │
│  │    - GET  /api/admin/whatsapp/logs                                    │  │
│  │    - GET  /api/admin/whatsapp/analytics                               │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Application Services (PointOfSale.API/Services/WhatsApp)              │  │
│  │                                                                        │  │
│  │  • IWhatsAppAccountService                                            │  │
│  │    - Account CRUD, Enable/Disable                                     │  │
│  │                                                                        │  │
│  │  • IWhatsAppFeatureGateService                                        │  │
│  │    - Feature flag checks at domain level                              │  │
│  │    - Determines if WhatsApp is enabled for specific use cases         │  │
│  │                                                                        │  │
│  │  • IWhatsAppMessageDispatcher                                         │  │
│  │    - Routes messages to WhatsApp engine                               │  │
│  │    - Applies rate limiting and delay jitter                           │  │
│  │    - Last line of defense for feature flags                           │  │
│  │                                                                        │  │
│  │  • IWhatsAppOtpService                                                │  │
│  │    - OTP generation, storage (Redis), validation                      │  │
│  │                                                                        │  │
│  │  • IWhatsAppMessageLogService                                         │  │
│  │    - MongoDB operations for message logs                              │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Infrastructure Layer (PointOfSale.Infrastructure/WhatsApp)            │  │
│  │                                                                        │  │
│  │  • IWhatsAppWebEngine                                                 │  │
│  │    - Headless browser management (Playwright)                         │  │
│  │    - QR code generation                                               │  │
│  │    - Session persistence                                              │  │
│  │    - Message sending                                                  │  │
│  │    - Connection health monitoring                                     │  │
│  │                                                                        │  │
│  │  • IWhatsAppSessionManager                                            │  │
│  │    - Encrypted session storage (Redis)                                │  │
│  │    - Session validation and refresh                                   │  │
│  │    - Auto-reconnect logic                                             │  │
│  │                                                                        │  │
│  │  • IWhatsAppRateLimiter                                               │  │
│  │    - Per-tenant limits                                                │  │
│  │    - Per-account limits (anti-ban)                                    │  │
│  │    - Per-recipient cooldowns                                          │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Event Consumers (PointOfSale.Infrastructure/Consumers/WhatsApp)       │  │
│  │                                                                        │  │
│  │  • WhatsAppOrderConfirmationConsumer                                  │  │
│  │    - Consumes WhatsAppOrderConfirmationRequestedEvent                 │  │
│  │    - Validates feature flags (second check)                           │  │
│  │    - Sends order confirmation via WhatsApp                            │  │
│  │                                                                        │  │
│  │  • WhatsAppOtpConsumer                                                │  │
│  │    - Consumes WhatsAppOtpRequestedEvent                               │  │
│  │    - Sends OTP via WhatsApp                                           │  │
│  │                                                                        │  │
│  │  • WhatsAppSessionMonitorConsumer                                     │  │
│  │    - Handles connection status events                                 │  │
│  │    - Triggers reconnection attempts                                   │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ Background Jobs (Hangfire)                                            │  │
│  │                                                                        │  │
│  │  • WhatsAppSessionHeartbeatJob (every 30 seconds)                     │  │
│  │    - Validates active sessions                                        │  │
│  │    - Triggers status transitions                                      │  │
│  │                                                                        │  │
│  │  • WhatsAppMessageRetryJob (every 60 seconds)                         │  │
│  │    - Retries failed messages (up to 3 attempts)                       │  │
│  │                                                                        │  │
│  │  • WhatsAppSessionCleanupJob (daily)                                  │  │
│  │    - Cleans up stale sessions                                         │  │
│  │    - Archives old message logs                                        │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Feature Gating Strategy (CRITICAL)

### 5.1 Activation Requirements

For WhatsApp messaging to be active, ALL of the following must be true:

1. **Tenant has at least one WhatsAppAccount** with:
   - `Enabled = true`
   - `Status = Connected`
   - Appropriate use case flag (`UseForOrderConfirmation` or `UseForOtpVerification`)

2. **Feature is explicitly enabled** - There is no platform-wide toggle; control is per-account.

### 5.2 Check Points

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     Feature Gate Check Points                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  CHECK POINT 1: Domain Service (Before Event Emission)                      │
│  ────────────────────────────────────────────────────────────────────────── │
│  Location: OrderService.CreateOrderAsync()                                  │
│                                                                              │
│  if (await _whatsAppFeatureGate.IsOrderConfirmationEnabled(tenantId))       │
│  {                                                                           │
│      await _eventPublisher.PublishAsync(                                    │
│          new WhatsAppOrderConfirmationRequestedEvent { ... });              │
│  }                                                                           │
│  // ELSE: Silent exit, no event, no error                                   │
│                                                                              │
│  CHECK POINT 2: Consumer (Before Message Dispatch)                          │
│  ────────────────────────────────────────────────────────────────────────── │
│  Location: WhatsAppOrderConfirmationConsumer.Consume()                      │
│                                                                              │
│  // Double-check: State may have changed since event was published          │
│  if (!await _whatsAppFeatureGate.IsOrderConfirmationEnabled(msg.TenantId))  │
│  {                                                                           │
│      _logger.LogInformation("WhatsApp disabled, skipping message");         │
│      return; // ACK message, no error                                       │
│  }                                                                           │
│                                                                              │
│  CHECK POINT 3: Message Dispatcher (Last Line of Defense)                   │
│  ────────────────────────────────────────────────────────────────────────── │
│  Location: WhatsAppMessageDispatcher.DispatchAsync()                        │
│                                                                              │
│  var account = await _accountRepo.GetActiveAccountForUseCase(...);          │
│  if (account == null)                                                        │
│  {                                                                           │
│      await LogSkippedMessage(reason: "No active WhatsApp account");         │
│      return;                                                                 │
│  }                                                                           │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.3 IWhatsAppFeatureGateService

```csharp
public interface IWhatsAppFeatureGateService
{
    /// <summary>
    /// Checks if WhatsApp order confirmation is enabled for the tenant.
    /// Returns true ONLY if there's an active, connected account with UseForOrderConfirmation=true.
    /// </summary>
    Task<bool> IsOrderConfirmationEnabledAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if WhatsApp OTP is enabled for the tenant.
    /// Returns true ONLY if there's an active, connected account with UseForOtpVerification=true.
    /// </summary>
    Task<bool> IsOtpVerificationEnabledAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the active WhatsApp account for the specified use case.
    /// Returns null if no suitable account is available.
    /// </summary>
    Task<WhatsAppAccount?> GetActiveAccountAsync(
        Guid tenantId, 
        WhatsAppUseCase useCase, 
        CancellationToken ct = default);
}

public enum WhatsAppUseCase
{
    OrderConfirmation,
    OtpVerification
}
```

---

## 6. Message Flows

### 6.1 Order Confirmation Flow

```
┌────────────────────────────────────────────────────────────────────────────────┐
│                     Order Confirmation via WhatsApp                             │
├────────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│  1. OrderService.CreateOrderAsync()                                            │
│     │                                                                           │
│     ▼                                                                           │
│  2. Check Feature Gate                                                         │
│     ├── NOT ENABLED ──▶ Exit silently (no event, existing flow unchanged)     │
│     │                                                                           │
│     └── ENABLED ──▶                                                            │
│                                                                                 │
│  3. Publish WhatsAppOrderConfirmationRequestedEvent                            │
│     │                                                                           │
│     ▼                                                                           │
│  4. WhatsAppOrderConfirmationConsumer receives event                           │
│     │                                                                           │
│     ▼                                                                           │
│  5. Double-check Feature Gate (state may have changed)                         │
│     ├── NOT ENABLED ──▶ ACK message, log skip, return                          │
│     │                                                                           │
│     └── ENABLED ──▶                                                            │
│                                                                                 │
│  6. Check Rate Limits                                                          │
│     ├── EXCEEDED ──▶ Delay + retry later (requeue with backoff)               │
│     │                                                                           │
│     └── OK ──▶                                                                 │
│                                                                                 │
│  7. Apply Random Delay (human-like behavior, 2-8 seconds)                      │
│     │                                                                           │
│     ▼                                                                           │
│  8. Create Message Log (MongoDB, status: PENDING)                              │
│     │                                                                           │
│     ▼                                                                           │
│  9. Get Active Session from Redis                                              │
│     ├── NO SESSION ──▶ Mark FAILED, trigger reconnect flow                    │
│     │                                                                           │
│     └── HAS SESSION ──▶                                                        │
│                                                                                 │
│  10. Send via WhatsApp Web Engine                                              │
│      ├── SUCCESS ──▶ Update log to SENT, publish WhatsAppMessageSentEvent     │
│      │                                                                          │
│      └── FAILURE ──▶ Increment attempts, retry or mark FAILED                 │
│                                                                                 │
│  11. Index to Elasticsearch (async, best-effort)                               │
│                                                                                 │
└────────────────────────────────────────────────────────────────────────────────┘
```

### 6.2 OTP via WhatsApp Flow

```
┌────────────────────────────────────────────────────────────────────────────────┐
│                     Phone Verification OTP via WhatsApp                         │
├────────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│  1. PhoneVerificationService.RequestVerificationAsync()                        │
│     │                                                                           │
│     ▼                                                                           │
│  2. Check Feature Gate                                                         │
│     ├── NOT ENABLED ──▶ Fallback to SMS or Email (existing behavior)          │
│     │                                                                           │
│     └── ENABLED ──▶                                                            │
│                                                                                 │
│  3. Generate OTP (6-digit, cryptographically random)                           │
│     │                                                                           │
│     ▼                                                                           │
│  4. Store OTP in Redis                                                         │
│     Key: whatsapp:otp:{tenantId}:{phone}:PHONE_VERIFICATION                    │
│     TTL: 5 minutes                                                             │
│     Value: { code, attempts: 0, maxAttempts: 3 }                               │
│     │                                                                           │
│     ▼                                                                           │
│  5. Publish WhatsAppOtpRequestedEvent                                          │
│     │                                                                           │
│     ▼                                                                           │
│  6. WhatsAppOtpConsumer receives event                                         │
│     │                                                                           │
│     ▼                                                                           │
│  7. Send OTP via WhatsApp Web Engine                                           │
│     │                                                                           │
│     ▼                                                                           │
│  8. User receives OTP on WhatsApp, enters in UI                                │
│     │                                                                           │
│     ▼                                                                           │
│  9. VerifyOtpAsync() - Validate against Redis                                  │
│     ├── VALID ──▶ Delete from Redis, return success                           │
│     ├── INVALID ──▶ Increment attempts, return failure                        │
│     └── EXPIRED/MAX_ATTEMPTS ──▶ Delete from Redis, return failure            │
│                                                                                 │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## 7. WhatsApp Web Engine

### 7.1 Architecture

```csharp
public interface IWhatsAppWebEngine
{
    /// <summary>
    /// Generates a QR code for account authentication.
    /// Returns base64-encoded PNG image.
    /// </summary>
    Task<QrCodeResult> GenerateQrCodeAsync(Guid accountId, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if the account is authenticated and ready.
    /// </summary>
    Task<ConnectionStatus> CheckConnectionAsync(Guid accountId, CancellationToken ct = default);
    
    /// <summary>
    /// Sends a text message to the specified phone number.
    /// </summary>
    Task<SendResult> SendMessageAsync(
        Guid accountId, 
        string phoneNumber, 
        string message, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Disconnects and clears session for the account.
    /// </summary>
    Task DisconnectAsync(Guid accountId, CancellationToken ct = default);
}

public record QrCodeResult(bool Success, string? QrImageBase64, string? Error, int? ExpiresInSeconds);
public record ConnectionStatus(bool IsConnected, WhatsAppAccountStatus Status, DateTime? LastActivity);
public record SendResult(bool Success, string? MessageId, string? Error);
```

### 7.2 Playwright-Based Implementation

```csharp
/// <summary>
/// Headless browser-based WhatsApp Web automation.
/// 
/// CRITICAL NOTES:
/// - Session data is encrypted before storage
/// - One browser context per account (isolated)
/// - Automatic reconnection on disconnection
/// - Randomized delays to avoid detection
/// </summary>
public class PlaywrightWhatsAppEngine : IWhatsAppWebEngine, IAsyncDisposable
{
    private readonly IWhatsAppSessionManager _sessionManager;
    private readonly IDataProtector _protector;
    private readonly ILogger<PlaywrightWhatsAppEngine> _logger;
    private readonly ConcurrentDictionary<Guid, BrowserContext> _contexts = new();
    
    // Implementation details...
}
```

### 7.3 Session Persistence

Sessions are stored in Redis, encrypted using ASP.NET Core Data Protection:

```csharp
public class WhatsAppSessionManager : IWhatsAppSessionManager
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDataProtector _protector;
    
    public async Task SaveSessionAsync(Guid accountId, string sessionData, CancellationToken ct)
    {
        var encrypted = _protector.Protect(sessionData);
        var key = $"whatsapp:session:{accountId}";
        
        await _redis.GetDatabase().StringSetAsync(
            key, 
            encrypted, 
            TimeSpan.FromDays(30));
    }
    
    public async Task<string?> GetSessionAsync(Guid accountId, CancellationToken ct)
    {
        var key = $"whatsapp:session:{accountId}";
        var encrypted = await _redis.GetDatabase().StringGetAsync(key);
        
        if (encrypted.IsNullOrEmpty) 
            return null;
            
        return _protector.Unprotect(encrypted);
    }
}
```

---

## 8. Anti-Ban & Stability Rules

### 8.1 Rate Limiting Configuration

```csharp
public class WhatsAppRateLimitSettings
{
    /// <summary>
    /// Maximum messages per tenant per day (all accounts combined)
    /// </summary>
    public int TenantDailyLimit { get; set; } = 1000;
    
    /// <summary>
    /// Maximum messages per account per hour (anti-ban)
    /// </summary>
    public int AccountHourlyLimit { get; set; } = 100;
    
    /// <summary>
    /// Cooldown period between messages to the same recipient (seconds)
    /// </summary>
    public int RecipientCooldownSeconds { get; set; } = 60;
    
    /// <summary>
    /// Minimum delay between messages (milliseconds)
    /// </summary>
    public int MinDelayMs { get; set; } = 2000;
    
    /// <summary>
    /// Maximum delay between messages (milliseconds)
    /// </summary>
    public int MaxDelayMs { get; set; } = 8000;
    
    /// <summary>
    /// Separate limits for OTP messages (more restrictive)
    /// </summary>
    public int OtpAccountHourlyLimit { get; set; } = 50;
}
```

### 8.2 Kill Switch Implementation

```csharp
/// <summary>
/// Emergency kill switch for WhatsApp accounts.
/// When triggered, all pending messages are skipped immediately.
/// </summary>
public async Task DisableAccountAsync(Guid accountId, string reason, CancellationToken ct)
{
    // 1. Update PostgreSQL
    var account = await _context.WhatsAppAccounts.FindAsync(accountId);
    account.Enabled = false;
    account.Status = WhatsAppAccountStatus.Disabled;
    account.StatusReason = reason;
    account.UpdatedAtUtc = DateTime.UtcNow;
    await _context.SaveChangesAsync(ct);
    
    // 2. Clear Redis session (immediate effect)
    await _sessionManager.ClearSessionAsync(accountId, ct);
    
    // 3. Publish status change event
    await _eventPublisher.PublishAsync(new WhatsAppAccountStatusChangedEvent
    {
        AccountId = accountId,
        OldStatus = account.Status,
        NewStatus = WhatsAppAccountStatus.Disabled,
        Reason = reason
    });
    
    _logger.LogWarning("WhatsApp account {AccountId} disabled: {Reason}", accountId, reason);
}
```

---

## 9. Database Schemas

### 9.1 PostgreSQL Migration

```sql
-- WhatsApp Accounts Table
CREATE TABLE whatsapp_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id),
    phone_number VARCHAR(20) NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    status SMALLINT NOT NULL DEFAULT 0, -- PendingQR=0, Connected=1, Disconnected=2, Banned=3, Disabled=4
    enabled BOOLEAN NOT NULL DEFAULT FALSE,
    use_for_order_confirmation BOOLEAN NOT NULL DEFAULT FALSE,
    use_for_otp_verification BOOLEAN NOT NULL DEFAULT FALSE,
    last_connected_at_utc TIMESTAMP WITH TIME ZONE,
    last_disconnected_at_utc TIMESTAMP WITH TIME ZONE,
    status_reason TEXT,
    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMP WITH TIME ZONE,
    
    CONSTRAINT uq_whatsapp_account_phone_tenant UNIQUE (tenant_id, phone_number)
);

-- Indexes
CREATE INDEX ix_whatsapp_accounts_tenant_id ON whatsapp_accounts(tenant_id);
CREATE INDEX ix_whatsapp_accounts_status ON whatsapp_accounts(status) WHERE enabled = TRUE;
```

### 9.2 MongoDB Indexes

```javascript
// Message Logs Collection
db.whatsapp_message_logs.createIndex(
    { "tenantId": 1, "createdAtUtc": -1 },
    { name: "ix_tenant_created" }
);

db.whatsapp_message_logs.createIndex(
    { "tenantId": 1, "messageType": 1, "status": 1 },
    { name: "ix_tenant_type_status" }
);

db.whatsapp_message_logs.createIndex(
    { "idempotencyKey": 1 },
    { name: "ix_idempotency", unique: true }
);

db.whatsapp_message_logs.createIndex(
    { "relatedEntityId": 1 },
    { name: "ix_related_entity" }
);

// TTL Index for automatic cleanup (90 days)
db.whatsapp_message_logs.createIndex(
    { "createdAtUtc": 1 },
    { name: "ix_ttl", expireAfterSeconds: 7776000 }
);
```

### 9.3 Elasticsearch Mapping

```json
{
  "mappings": {
    "properties": {
      "tenantId": { "type": "keyword" },
      "accountId": { "type": "keyword" },
      "targetPhone": { "type": "keyword" },
      "maskedPhone": { "type": "keyword" },
      "messageType": { "type": "keyword" },
      "relatedEntityId": { "type": "keyword" },
      "relatedEntityType": { "type": "keyword" },
      "status": { "type": "keyword" },
      "failureReason": { "type": "text" },
      "createdAtUtc": { "type": "date" },
      "sentAtUtc": { "type": "date" }
    }
  }
}
```

---

## 10. Security Considerations

### 10.1 Data Protection

| Data | Protection Method |
|------|-------------------|
| Session Payload | ASP.NET Core Data Protection (encryption at rest) |
| Phone Numbers | Masked in logs (e.g., +20123****890) |
| OTP Codes | Stored in Redis with TTL, never logged |
| Message Content | Stored encrypted, masked in Elasticsearch |

### 10.2 Tenant Isolation

- All queries include `TenantId` filter
- `TenantSaveChangesInterceptor` enforces tenant ownership
- Redis keys are prefixed with `{tenantId}:`
- MongoDB queries require tenant context

### 10.3 Authentication & Authorization

- WhatsApp admin endpoints require POS admin JWT
- All endpoints require valid tenant context
- No public endpoints exposed

---

## 11. Observability

### 11.1 Metrics (Prometheus-style)

```
# Messages sent per tenant per type
whatsapp_messages_sent_total{tenant_id, message_type, status}

# Message latency
whatsapp_message_send_duration_seconds{tenant_id, message_type}

# Active sessions per tenant
whatsapp_active_sessions{tenant_id}

# Rate limit hits
whatsapp_rate_limit_exceeded_total{tenant_id, limit_type}

# Connection status changes
whatsapp_connection_status_changes_total{tenant_id, from_status, to_status}
```

### 11.2 Logging

```csharp
// Structured logging examples
_logger.LogInformation(
    "WhatsApp message sent | TenantId: {TenantId} | AccountId: {AccountId} | " +
    "Type: {MessageType} | Phone: {MaskedPhone} | Duration: {DurationMs}ms",
    tenantId, accountId, messageType, maskedPhone, duration);

_logger.LogWarning(
    "WhatsApp rate limit exceeded | TenantId: {TenantId} | " +
    "LimitType: {LimitType} | Current: {Current} | Max: {Max}",
    tenantId, limitType, current, max);
```

---

## 12. Rollout Plan

### Phase 1: Foundation (Week 1-2)
- [ ] Create PostgreSQL entities and migrations
- [ ] Implement WhatsAppAccount service (CRUD)
- [ ] Create MongoDB document and indexes
- [ ] Implement Redis session manager
- [ ] Set up feature gate service

### Phase 2: Engine (Week 2-3)
- [ ] Implement Playwright-based WhatsApp Web engine
- [ ] Create session persistence layer
- [ ] Build QR code generation flow
- [ ] Implement connection monitoring

### Phase 3: Messaging (Week 3-4)
- [ ] Create event contracts
- [ ] Implement consumers
- [ ] Build message dispatcher with rate limiting
- [ ] Add OTP flow integration

### Phase 4: Admin UI (Week 4-5)
- [ ] Create admin API endpoints
- [ ] Build POS admin UI for account management
- [ ] Implement QR code display
- [ ] Add message log viewer

### Phase 5: Hardening (Week 5-6)
- [ ] Add comprehensive tests
- [ ] Implement retry logic
- [ ] Add Elasticsearch indexing
- [ ] Create admin analytics dashboard
- [ ] Performance testing

---

## 13. File Structure

```
PointOfSale.Core/
├── Entities/
│   └── WhatsApp/
│       ├── WhatsAppAccount.cs
│       └── WhatsAppAccountStatus.cs
├── Events/
│   └── WhatsApp/
│       ├── WhatsAppOrderConfirmationRequestedEvent.cs
│       ├── WhatsAppOtpRequestedEvent.cs
│       ├── WhatsAppAccountStatusChangedEvent.cs
│       ├── WhatsAppMessageSentEvent.cs
│       └── WhatsAppMessageFailedEvent.cs
└── Interfaces/
    └── WhatsApp/
        ├── IWhatsAppFeatureGateService.cs
        └── IWhatsAppUseCase.cs

PointOfSale.Infrastructure/
├── Data/
│   └── Configurations/
│       └── WhatsAppAccountConfiguration.cs
├── WhatsApp/
│   ├── Engine/
│   │   ├── IWhatsAppWebEngine.cs
│   │   ├── PlaywrightWhatsAppEngine.cs
│   │   └── WhatsAppSessionManager.cs
│   ├── RateLimiting/
│   │   ├── IWhatsAppRateLimiter.cs
│   │   └── WhatsAppRateLimiter.cs
│   └── Services/
│       ├── WhatsAppAccountService.cs
│       ├── WhatsAppFeatureGateService.cs
│       ├── WhatsAppMessageDispatcher.cs
│       └── WhatsAppOtpService.cs
├── Consumers/
│   └── WhatsApp/
│       ├── WhatsAppOrderConfirmationConsumer.cs
│       ├── WhatsAppOtpConsumer.cs
│       └── WhatsAppSessionMonitorConsumer.cs
└── MongoDB/
    ├── Documents/
    │   └── WhatsAppMessageLogDocument.cs
    └── Services/
        └── WhatsAppMessageLogService.cs

PointOfSale.API/
├── Controllers/
│   └── Admin/
│       └── WhatsAppAdminController.cs
├── Services/
│   └── WhatsApp/
│       └── [Orchestration services if needed]
└── Jobs/
    └── WhatsApp/
        ├── WhatsAppSessionHeartbeatJob.cs
        ├── WhatsAppMessageRetryJob.cs
        └── WhatsAppSessionCleanupJob.cs
```

---

## 14. Backward Compatibility

This feature is 100% opt-in:

1. **No schema breaking changes** - New tables only
2. **No event contract changes** - New events only
3. **No existing flow modifications** - Feature gates wrap new behavior
4. **No runtime impact if disabled** - Zero overhead when not enabled

### Fallback Behavior

| Scenario | WhatsApp Enabled | WhatsApp Disabled |
|----------|------------------|-------------------|
| Order Confirmation | WhatsApp message sent | No action (silent) |
| Phone OTP | WhatsApp OTP sent | Fallback to SMS/Email |
| Account Banned | Auto-disable + admin notification | N/A |

---

## 15. Dependencies

### NuGet Packages

```xml
<!-- Playwright for headless browser -->
<PackageReference Include="Microsoft.Playwright" Version="1.40.0" />

<!-- QR Code generation for display -->
<PackageReference Include="QRCoder" Version="1.4.3" />
```

### Configuration

```json
{
  "WhatsApp": {
    "Enabled": true,
    "RateLimits": {
      "TenantDailyLimit": 1000,
      "AccountHourlyLimit": 100,
      "RecipientCooldownSeconds": 60,
      "MinDelayMs": 2000,
      "MaxDelayMs": 8000,
      "OtpAccountHourlyLimit": 50
    },
    "Session": {
      "ExpiryDays": 30,
      "HeartbeatIntervalSeconds": 30
    },
    "Playwright": {
      "Headless": true,
      "Timeout": 30000
    }
  }
}
```

---

## 16. Testing Strategy

### Unit Tests
- Feature gate logic
- Rate limiting calculations
- Message formatting
- OTP generation and validation

### Integration Tests
- Account CRUD operations
- Event publishing and consumption
- MongoDB logging
- Redis session persistence

### E2E Tests (Manual)
- QR code scan flow
- Message delivery verification
- Reconnection after disconnect
- Kill switch activation

---

## Appendix A: Message Templates

### Order Confirmation Template

```
🛒 *Order Confirmed!*

Order #: {OrderNumber}
Date: {OrderDate}

*Items:*
{ItemList}

*Summary:*
Subtotal: {Subtotal}
Shipping: {ShippingCost}
*Total: {Total}*

{ShippingAddress}

Track your order:
{TrackingUrl}

Thank you for shopping with {StoreName}! 💚
```

### OTP Template

```
🔐 *Verification Code*

Your code is: *{OtpCode}*

This code expires in {ExpiryMinutes} minutes.

If you didn't request this, please ignore this message.

- {StoreName}
```

---

*Document Version: 1.0*
*Last Updated: 2026-01-29*
*Author: System Architect*
