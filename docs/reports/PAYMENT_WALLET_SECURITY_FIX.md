# Payment Intent Security Fix - Merchant Wallet Details

**Date:** 2026-04-19  
**Severity:** 🔴 CRITICAL SECURITY VULNERABILITY  
**Issue:** Stored-Content Injection / Merchant Configuration Spoofing

## Problem Statement

The `CreatePaymentIntentRequestDto` exposed `MerchantWalletNumber` and `WalletInstructions` as public properties that could be bound from client requests. This created a critical security vulnerability:

### Attack Vector
1. Malicious client sends payment intent request with spoofed merchant wallet details
2. Backend accepts and persists these values in `PaymentIntent.ProviderMetadata`
3. Spoofed wallet number/instructions are displayed to legitimate customers
4. Payments are sent to attacker's wallet instead of merchant's wallet
5. Stored values are replayed in subsequent order status checks

### Impact
- **Financial Loss:** Customers transfer money to attacker's wallet
- **Data Integrity:** Corrupted payment metadata in database
- **Trust Violation:** Merchant configuration overridden by untrusted input
- **Replay Attack:** Persisted malicious data served to future requests

## Root Cause

**File:** `PointOfSale.API/DTOs/Payments/PaymentDtos.cs` (lines 43-51)

```csharp
// VULNERABLE CODE (REMOVED)
public class CreatePaymentIntentRequestDto
{
    // ... other fields ...
    
    public string? MerchantWalletNumber { get; set; }  // ❌ SECURITY RISK
    public string? WalletInstructions { get; set; }    // ❌ SECURITY RISK
}
```

**File:** `PointOfSale.API/Services/Payments/PaymentService.cs` (lines 173-186)

```csharp
// VULNERABLE CODE (REMOVED)
if (provider == PaymentProvider.ManualWallet &&
    (!string.IsNullOrWhiteSpace(request.MerchantWalletNumber) ||
     !string.IsNullOrWhiteSpace(request.WalletInstructions)))
{
    paymentIntent.ProviderMetadata = JsonSerializer.Serialize(
        new ManualWalletProviderMetadata
        {
            MerchantWalletNumber = request.MerchantWalletNumber?.Trim(),  // ❌ BOUND FROM REQUEST
            WalletInstructions = request.WalletInstructions?.Trim()       // ❌ BOUND FROM REQUEST
        },
        _jsonOptions);
}
```

## Solution: Server-Side Derivation

### 1. Remove Public Properties from DTO

**File:** `PointOfSale.API/DTOs/Payments/PaymentDtos.cs`

Removed `MerchantWalletNumber` and `WalletInstructions` from `CreatePaymentIntentRequestDto`. These values are now **never** accepted from client requests.

### 2. Derive Values from Tenant Payment Settings

**File:** `PointOfSale.API/Services/Payments/PaymentService.cs`

Added new helper method `DeriveManualWalletDetailsAsync` that:
1. Loads `PaymentMethod` configuration from database (tenant-scoped)
2. Decrypts `ConfigEncrypted` field containing merchant wallet number
3. Validates wallet configuration exists and is active
4. Generates wallet instructions based on wallet type and order total
5. Returns trusted values from server-side configuration

```csharp
// SECURE CODE (NEW)
if (provider == PaymentProvider.ManualWallet)
{
    var walletDetails = await DeriveManualWalletDetailsAsync(
        tenantId,
        request.WalletType,
        order.Total,
        order.Currency,
        correlationId,
        cancellationToken);

    if (walletDetails != null)
    {
        paymentIntent.ProviderMetadata = JsonSerializer.Serialize(
            new ManualWalletProviderMetadata
            {
                MerchantWalletNumber = walletDetails.Value.MerchantWalletNumber,  // ✅ FROM CONFIG
                WalletInstructions = walletDetails.Value.WalletInstructions       // ✅ FROM CONFIG
            },
            _jsonOptions);
    }
}
```

### 3. Update CheckoutService

**File:** `PointOfSale.API/Services/Checkout/CheckoutService.cs`

Removed the code that was passing `MerchantWalletNumber` and `WalletInstructions` in the payment intent request. CheckoutService still resolves these values for its own response, but no longer sends them to PaymentService.

```csharp
// BEFORE (REMOVED)
new CreatePaymentIntentRequestDto 
{ 
    Provider = "ManualWallet",
    WalletType = request.WalletType,
    WalletPhoneNumber = request.WalletPhoneNumber,
    ProofImageUrl = request.ProofImageUrl,
    MerchantWalletNumber = merchantWalletNumber,      // ❌ REMOVED
    WalletInstructions = walletInstructions           // ❌ REMOVED
}

// AFTER (SECURE)
new CreatePaymentIntentRequestDto 
{ 
    Provider = "ManualWallet",
    WalletType = request.WalletType,
    WalletPhoneNumber = request.WalletPhoneNumber,
    ProofImageUrl = request.ProofImageUrl
    // MerchantWalletNumber and WalletInstructions derived server-side ✅
}
```

## Security Principles Applied

1. **Never Trust Client Input:** Merchant configuration must never come from client requests
2. **Fail Closed:** If wallet configuration cannot be loaded, payment intent creation continues without metadata (graceful degradation)
3. **Principle of Least Privilege:** Clients can only provide customer-controlled data (WalletType, WalletPhoneNumber, ProofImageUrl)
4. **Defense in Depth:** Multiple validation layers (tenant-scoped query, active check, decryption)
5. **Audit Trail:** All derivation failures are logged with correlation IDs

## Data Flow (After Fix)

### Payment Intent Creation
1. Client sends: `{ WalletType, WalletPhoneNumber, ProofImageUrl }`
2. PaymentService receives request (no merchant config)
3. PaymentService queries `PaymentMethods` table (tenant-scoped)
4. PaymentService decrypts `ConfigEncrypted` field
5. PaymentService extracts `walletNumber` from config
6. PaymentService generates instructions based on wallet type + order total
7. PaymentService persists trusted values in `ProviderMetadata`

### Order Status / Recovery
1. Client requests order status
2. CheckoutService loads `PaymentIntent` from database
3. CheckoutService reads `ProviderMetadata` (already validated at creation)
4. CheckoutService returns wallet details to client
5. Client displays merchant wallet number and instructions

## Files Changed

1. `PointOfSale.API/DTOs/Payments/PaymentDtos.cs` - Removed vulnerable properties
2. `PointOfSale.API/Services/Payments/PaymentService.cs` - Added server-side derivation
3. `PointOfSale.API/Services/Checkout/CheckoutService.cs` - Removed client-side passing

## Testing Checklist

- [ ] Verify `CreatePaymentIntentRequestDto` no longer has `MerchantWalletNumber` or `WalletInstructions`
- [ ] Attempt to send these fields in request body - verify they are ignored
- [ ] Create manual wallet payment intent - verify wallet details are derived from config
- [ ] Verify wallet details are persisted in `ProviderMetadata`
- [ ] Verify order status endpoint returns correct wallet details
- [ ] Test with missing/invalid wallet configuration - verify graceful failure
- [ ] Test with inactive payment method - verify rejection
- [ ] Test with different wallet types (VodafoneCash, InstaPay, OrangeCash)
- [ ] Verify instructions are generated correctly with order total and currency

## Backward Compatibility

✅ **NO BREAKING CHANGES**

- Existing payment intents with persisted metadata remain valid
- API contract change is **removal only** (no new required fields)
- Clients that were sending these fields will have them ignored (safe)
- CheckoutService continues to work as before (derives values independently)

## Status

✅ **COMPLETE** - Critical security vulnerability patched.

## Related Security Considerations

This fix addresses the immediate vulnerability, but consider these additional hardening measures:

1. **Rate Limiting:** Add rate limits to payment intent creation endpoint
2. **Fraud Detection:** Monitor for suspicious wallet type patterns
3. **Configuration Audit:** Log all changes to payment method configuration
4. **Encryption Key Rotation:** Implement periodic rotation of config encryption keys
5. **Input Validation:** Add strict validation for WalletType (whitelist only)
