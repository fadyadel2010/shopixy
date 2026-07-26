# WhatsApp Phone Verification - Bug Fix Summary

**Date**: January 30, 2026  
**Phase**: WhatsApp Integration - Checkout Phone OTP  
**Status**: ✅ RESOLVED

---

## Overview

This document summarizes the bugs encountered and fixes implemented for the WhatsApp OTP phone verification feature during checkout. The feature requires customers to verify their phone number via WhatsApp OTP before placing an order (when enabled by the tenant).

---

## Architecture Overview

### Flow Diagram

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Storefront    │────▶│   API Server    │────▶│   RabbitMQ      │
│   (React/TS)    │     │   (ASP.NET)     │     │   (MassTransit) │
└─────────────────┘     └─────────────────┘     └─────────────────┘
        │                       │                       │
        │                       ▼                       ▼
        │               ┌─────────────────┐     ┌─────────────────┐
        │               │   Redis Cache   │     │  WhatsApp OTP   │
        │               │ (Verification)  │     │    Consumer     │
        │               └─────────────────┘     └─────────────────┘
        │                                               │
        │                                               ▼
        │                                       ┌─────────────────┐
        └──────────────────────────────────────│   Playwright    │
                                               │  WhatsApp Web   │
                                               └─────────────────┘
```

### Key Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `PhoneOtpVerification.tsx` | Storefront | UI for OTP input and verification |
| `StorefrontPhoneVerificationService.cs` | API | Handles OTP generation, storage, and verification |
| `CheckoutController.cs` | API | Exposes endpoints for phone verification |
| `CheckoutService.cs` | API | Validates phone verification during checkout |
| `WhatsAppOtpConsumer.cs` | Infrastructure | Consumes OTP events and sends via WhatsApp |
| `PlaywrightWhatsAppEngine.cs` | Infrastructure | Sends messages via WhatsApp Web automation |

---

## Issues Encountered & Fixes

### Issue #1: OTP Event Not Being Published

**Symptom**: OTP was not being sent to the customer's WhatsApp.

**Root Cause**: The `StorefrontPhoneVerificationService` was using `IPublishEndpoint` which had routing issues for `WhatsAppOtpRequestedEvent`.

**Fix**: Switched from `IPublishEndpoint` to `IEventPublisher` for consistent message routing.

**File Modified**: `StorefrontPhoneVerificationService.cs`

```csharp
// Before
private readonly IPublishEndpoint _publishEndpoint;

// After
private readonly IEventPublisher _eventPublisher;
```

---

### Issue #2: Verification Not Found During Checkout (Session ID vs Customer ID)

**Symptom**: Phone verification was successful, but checkout still rejected with "PHONE_VERIFICATION_REQUIRED".

**Root Cause**: 
- `VerifyOtpAsync` stored verification keyed by `sessionId`
- `CheckoutService.ProcessCheckoutAsync` looked up verification using `ownerId` (customer ID)

**Investigation Logs**:
```
[CheckoutPhoneOtp] Phone verified successfully | SessionId: e6376975-c9f1-48f8-a02d-dfb953d7da38
[CheckoutPhoneOtp] Verification lookup | SessionId: 00329651f39c4827b901d4ac05ad85a3 | Found: False
```

**Fix**: Store verification keyed by **both** session ID and customer ID (if logged in).

**File Modified**: `StorefrontPhoneVerificationService.cs`

```csharp
// Store for session (always)
var verifiedKey = $"checkout_phone_verified:{tenantId}:{sessionId}:{phone}";
await _redis.StringSetAsync(verifiedKey, "1", TimeSpan.FromHours(SESSION_VERIFICATION_TTL_HOURS));

// ALSO store for customer ID if logged in
if (customerId.HasValue && customerId.Value != Guid.Empty)
{
    var customerVerifiedKey = $"checkout_phone_verified:{tenantId}:{customerId.Value}:{phone}";
    await _redis.StringSetAsync(customerVerifiedKey, "1", TimeSpan.FromHours(SESSION_VERIFICATION_TTL_HOURS));
}
```

---

### Issue #3: Customer ID Not Extracted from Header

**Symptom**: Customer ID was always `null` in the verification endpoint.

**Root Cause**: The `VerifyPhoneOtp` endpoint was only checking JWT claims for customer ID, but the storefront uses header-based authentication (`X-Customer-Id`).

**Fix**: Check `X-Customer-Id` header first, then fallback to JWT claims.

**File Modified**: `CheckoutController.cs`

```csharp
// Get customer ID from header or JWT claims (if logged in)
Guid? customerId = null;

// First try X-Customer-Id header (primary approach for storefront)
if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerHeaderValue) &&
    Guid.TryParse(customerHeaderValue, out var headerCustomerId))
{
    customerId = headerCustomerId;
}
// Fallback to JWT claims
else
{
    var customerIdClaim = User?.FindFirst("customer_id")?.Value 
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (Guid.TryParse(customerIdClaim, out var parsedCustomerId))
    {
        customerId = parsedCustomerId;
    }
}
```

---

### Issue #4: GUID Format Mismatch (Dashes vs No Dashes)

**Symptom**: Verification still not found even with correct customer ID logic.

**Root Cause**: 
- Storage used: `customerId.Value.ToString()` → `93e82d01-5d6a-4bcf-96b6-7a10d6a92c8d` (WITH dashes)
- Lookup used: `ownerId` → `93e82d015d6a4bcf96b67a10d6a92c8d` (WITHOUT dashes)

**Investigation Logs**:
```
[CheckoutPhoneOtp] Phone verified successfully | CustomerId: 93e82d01-5d6a-4bcf-96b6-7a10d6a92c8d
[CheckoutPhoneOtp] Verification lookup | SessionId: 93e82d015d6a4bcf96b67a10d6a92c8d | Found: False
                                                    ^^^^ No dashes!               ^^^^ With dashes in storage
```

**Fix**: Use `ToString("N")` format (no dashes) to match the `ownerId` format.

**File Modified**: `StorefrontPhoneVerificationService.cs`

```csharp
// IMPORTANT: Format GUID without dashes ("N") to match checkout's ownerId format
if (customerId.HasValue && customerId.Value != Guid.Empty)
{
    var customerIdNoDashes = customerId.Value.ToString("N"); // No dashes format
    var customerVerifiedKey = $"checkout_phone_verified:{tenantId}:{customerIdNoDashes}:{phone}";
    await _redis.StringSetAsync(customerVerifiedKey, "1", TimeSpan.FromHours(SESSION_VERIFICATION_TTL_HOURS));
}
```

---

### Issue #5: Frontend Stale Closure Bug

**Symptom**: "Please enter the complete 6-digit code" error appeared even after entering all 6 digits.

**Root Cause**: React stale closure - the `handleVerifyOtp` function captured an old `otpCode` value when called from `setTimeout`.

**Code Before**:
```tsx
// Auto-verify when all digits entered
if (value.length === OTP_LENGTH) {
    setTimeout(() => {
        handleVerifyOtp(); // ← Uses stale otpCode from closure!
    }, 300);
}
```

**Fix**: Pass the OTP value directly to avoid closure issues.

**File Modified**: `PhoneOtpVerification.tsx`

```tsx
// Verify OTP - accepts optional codeOverride to avoid stale closure issues
const handleVerifyOtp = useCallback(async (codeOverride?: string) => {
    const codeToVerify = codeOverride ?? otpCode;
    
    if (codeToVerify.length !== OTP_LENGTH) {
        setError('Please enter the complete 6-digit code');
        return;
    }
    // ... rest of verification logic using codeToVerify
}, [phone, otpCode, onVerified]);

// In handleOtpChange:
if (value.length === OTP_LENGTH) {
    setTimeout(() => {
        verifyOtpCode(value); // ← Pass value directly!
    }, 300);
}
```

---

## Phone Number Normalization

Phone numbers are normalized to E.164 format for consistent storage and lookup:

```csharp
private static string NormalizePhone(string phone)
{
    if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
    
    // Remove spaces, dashes, and other common separators
    var normalized = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
    
    // Ensure it starts with + for international format
    if (!normalized.StartsWith('+') && normalized.Length >= 10)
    {
        // Assume Egyptian number if no country code
        normalized = "+2" + normalized;
    }
    
    return normalized;
}
```

**Examples**:
| Input | Normalized |
|-------|------------|
| `01022174803` | `+201022174803` |
| `+201022174803` | `+201022174803` |
| `010-2217-4803` | `+201022174803` |

---

## Redis Key Structure

### OTP Storage
```
checkout_otp:{tenantId}:{phone} = "{6-digit OTP}"
TTL: 5 minutes
```

### Verification Cache
```
checkout_phone_verified:{tenantId}:{sessionId}:{phone} = "1"
checkout_phone_verified:{tenantId}:{customerId}:{phone} = "1"
TTL: 4 hours
```

### Rate Limiting
```
checkout_otp_ratelimit:{tenantId}:{phone} = "1"
TTL: 30 seconds (between OTP requests)
```

---

## API Endpoints

### Check Verification Status
```http
GET /api/storefront/checkout/phone-verification-status?phone={phone}
Headers: X-Tenant-Id, X-Session-Id, X-Customer-Id (optional)
Response: { isRequired: bool, isVerified: bool, formattedPhone: string }
```

### Request OTP
```http
POST /api/storefront/checkout/request-phone-otp
Headers: X-Tenant-Id, X-Session-Id
Body: { "phone": "01022174803" }
Response: { success: bool, message: string }
```

### Verify OTP
```http
POST /api/storefront/checkout/verify-phone-otp
Headers: X-Tenant-Id, X-Session-Id, X-Customer-Id (optional)
Body: { "phone": "01022174803", "otpCode": "123456" }
Response: { success: bool, message: string }
```

---

## Event Flow

1. **OTP Request**:
   ```
   StorefrontPhoneVerificationService.RequestOtpAsync()
       → Generates 6-digit OTP
       → Stores in Redis with 5-minute TTL
       → Publishes WhatsAppOtpRequestedEvent
   ```

2. **WhatsApp Send**:
   ```
   WhatsAppOtpConsumer.Consume()
       → Checks if OTP feature enabled
       → Gets connected WhatsApp account
       → Sends message via PlaywrightWhatsAppEngine
       → Logs message to database
   ```

3. **OTP Verification**:
   ```
   StorefrontPhoneVerificationService.VerifyOtpAsync()
       → Retrieves OTP from Redis
       → Compares with user input
       → Stores verification in Redis (both sessionId and customerId keys)
       → Deletes OTP from Redis
   ```

4. **Checkout Validation**:
   ```
   CheckoutService.ProcessCheckoutAsync()
       → Calls IsPhoneVerifiedForSessionAsync(ownerId)
       → Checks Redis for verification key
       → Proceeds if found, rejects if not
   ```

---

## Feature Gate

Phone OTP verification is controlled by tenant settings:

```csharp
// In TenantFeatureGate
public async Task<bool> IsOtpVerificationEnabledAsync(Guid tenantId)
{
    var settings = await GetTenantSettingsAsync(tenantId);
    return settings?.EnableWhatsAppOtp ?? false;
}
```

If disabled, `IsPhoneVerifiedForSessionAsync` returns `true` immediately (bypass).

---

## Known Warnings

### WhatsAppMessageSentEvent Without TenantId
```
Publishing event without TenantId | EventType: WhatsAppMessageSentEvent
```

This warning appears because the WhatsApp message log is created without tenant context. This is informational and doesn't affect functionality.

---

## Testing Checklist

- [ ] Guest checkout with phone verification
- [ ] Logged-in customer checkout with phone verification
- [ ] OTP expiration (5 minutes)
- [ ] Rate limiting (30 seconds between requests)
- [ ] Invalid OTP rejection
- [ ] Auto-verify on 6th digit (frontend)
- [ ] Resend OTP functionality
- [ ] Verification status persistence (4 hours)

---

## Files Modified

| File | Changes |
|------|---------|
| `StorefrontPhoneVerificationService.cs` | Event publishing, dual-key storage, GUID format |
| `CheckoutController.cs` | Customer ID extraction from header |
| `PhoneOtpVerification.tsx` | Stale closure fix for auto-verify |

---

## Lessons Learned

1. **GUID Formatting**: Always be consistent with GUID formatting (`"D"` with dashes vs `"N"` without). Check both sides of storage/lookup.

2. **Authentication Context**: Storefront uses header-based auth (`X-Customer-Id`), not JWT claims. Always check the actual authentication mechanism.

3. **React Closures**: When using `setTimeout` or `setInterval` with callbacks, pass values directly instead of relying on state from closures.

4. **Dual-Key Storage**: When an entity can be identified by multiple IDs (session vs customer), store verification under both keys.

5. **Logging is Essential**: Detailed logging with actual key values made debugging the GUID format mismatch possible.

---

*Document created: January 30, 2026*  
*Last updated: January 30, 2026*
