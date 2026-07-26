# Stripe Client Secret Security Fix - Implementation Summary

## Issue
The application was storing Stripe's sensitive `client_secret` in the `PaymentIntent.RedirectUrl` field, which is:
1. **Insecure**: Mixing sensitive credentials with non-sensitive redirect URLs
2. **Confusing**: RedirectUrl should be for hosted payment pages (like Paymob), not secrets
3. **Unencrypted**: The client_secret was stored in plain text in the database

## Solution Implemented

### 1. Database Schema Changes
- **Added**: `ClientSecretEncrypted` column to `PaymentIntents` table
- **Migration**: `add-client-secret-encrypted-column.sql`
- **Type**: `text NULL` (encrypted at application layer)

### 2. New Encryption Service
- **File**: `PointOfSale.API/Services/Payments/PaymentIntentClientSecretProtector.cs`
- **Interface**: `IPaymentIntentClientSecretProtector`
- **Implementation**: Uses ASP.NET Core Data Protection API with purpose `"PaymentIntent.ClientSecret"`
- **Registration**: Added to `Program.cs` as Singleton

### 3. Updated Services

#### StripePaymentService
- **Constructor**: Added `IPaymentIntentClientSecretProtector` dependency
- **Helper Methods**:
  - `SetClientSecret(paymentIntent, clientSecret)`: Encrypts and stores client_secret
  - `GetClientSecret(paymentIntent)`: Decrypts and retrieves client_secret
  - Includes backward compatibility for existing RedirectUrl values
- **Updated Locations**:
  - Line ~215: Recovery path - now uses `SetClientSecret()`
  - Line ~298: Existing intent return - now uses `GetClientSecret()`
  - Line ~355: Claimed intent return - now uses `GetClientSecret()`
  - Line ~389: Concurrent claim recovery - now uses `SetClientSecret()`
  - Line ~487: New intent creation - now uses `SetClientSecret()`
  - Line ~538: Retry persistence - now uses `SetClientSecret()`

#### RedirectUrlRecoveryHostedService
- **Constructor**: Added `IPaymentIntentClientSecretProtector` dependency
- **Updated**: Line ~219 - Now stores encrypted client_secret instead of RedirectUrl
- **Check**: Line ~186 - Now checks `ClientSecretEncrypted` instead of `RedirectUrl`

### 4. Remaining Work

#### CheckoutService Updates (✅ COMPLETED)
The CheckoutService has been updated to use encrypted client secrets:
- ✅ Injected `IPaymentIntentClientSecretProtector` into CheckoutService constructor
- ✅ Created helper method `GetStripeClientSecret()` with backward compatibility
- ✅ Updated Line 1543: Now uses `GetStripeClientSecret(paymentIntent)`
- ✅ Updated Line 1645: Now uses `GetStripeClientSecret(paymentIntent)`

#### Other Services (Need Investigation)
Search results showed `RedirectUrl` is used in multiple places:
- `PaymentService.cs` - May need updates if it handles Stripe
- Various test files - May need updates to use new encrypted field

### 5. Migration Path

The implementation includes backward compatibility:
- `GetClientSecret()` first checks `ClientSecretEncrypted`
- Falls back to `RedirectUrl` if encrypted field is empty
- Logs a warning when fallback is used
- This allows gradual migration of existing data

### 6. Data Migration Strategy

**Option A: Lazy Migration** (Current Implementation)
- New payment intents use `ClientSecretEncrypted`
- Existing payment intents continue using `RedirectUrl` until accessed
- Recovery service will update them when needed

**Option B: Batch Migration** (Recommended for Production)
- Create a one-time migration script to:
  1. Find all PaymentIntents with Stripe provider and non-null RedirectUrl
  2. Encrypt the RedirectUrl value into ClientSecretEncrypted
  3. Clear the RedirectUrl field
  4. Run during maintenance window

### 7. Testing Requirements

1. **Unit Tests**: Test encryption/decryption of client secrets
2. **Integration Tests**: Test Stripe payment flow with new encrypted field
3. **Migration Tests**: Test backward compatibility with existing RedirectUrl values
4. **Recovery Tests**: Test RedirectUrlRecoveryHostedService with new field

### 8. Security Improvements

✅ **Achieved**:
- Client secrets now encrypted at rest using Data Protection API
- Dedicated field separates concerns (secrets vs URLs)
- Clear documentation in code comments

✅ **Best Practices**:
- Purpose-specific data protector
- Encryption keys managed by ASP.NET Core Data Protection
- Keys can be rotated using standard Data Protection key management

### 9. Performance Impact

- **Minimal**: Encryption/decryption happens in-memory
- **No additional database calls**: Same number of queries
- **Negligible CPU overhead**: Data Protection API is optimized

### 10. Deployment Checklist

- [ ] Run database migration to add `ClientSecretEncrypted` column
- [ ] Deploy updated application code
- [ ] Monitor logs for backward compatibility warnings
- [ ] (Optional) Run batch migration script to encrypt existing secrets
- [ ] Update monitoring/alerting for new field
- [ ] Update documentation for payment integration

## Files Modified

1. ✅ `PointOfSale.Core/Entities/PaymentIntent.cs` - Added ClientSecretEncrypted property
2. ✅ `add-client-secret-encrypted-column.sql` - Database migration
3. ✅ `PointOfSale.API/Services/Payments/PaymentIntentClientSecretProtector.cs` - New encryption service
4. ✅ `PointOfSale.API/Program.cs` - Service registration
5. ✅ `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs` - Updated to use encryption
6. ✅ `PointOfSale.API/Services/BackgroundTasks/RedirectUrlRecoveryHostedService.cs` - Updated to use encryption
7. ✅ `PointOfSale.API/Services/Checkout/CheckoutService.cs` - Updated to decrypt client secrets

## Files Still Needing Updates

1. Test files that reference `RedirectUrl` for Stripe
2. Any other services that read Stripe client_secret from PaymentIntent (needs investigation)

## Verification Steps

1. Run the SQL migration
2. Build the solution to check for compilation errors
3. Run existing tests to ensure no regressions
4. Test Stripe payment flow end-to-end
5. Verify client_secret is encrypted in database
6. Test recovery service with new encrypted field
