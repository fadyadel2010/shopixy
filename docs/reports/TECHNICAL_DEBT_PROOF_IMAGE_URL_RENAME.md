# Technical Debt: Rename ProofImageUrl to ProofFileKey

## Issue
The `PaymentIntent.ProofImageUrl` property now stores a normalized object key (e.g., `tenants/{tenantId}/manual-wallet-proofs/...`) rather than a URL. This naming mismatch may confuse future maintainers.

## Current State
- Property name: `ProofImageUrl`
- Actual content: Normalized S3/R2 object key
- Status: Working correctly at runtime, but semantically misleading

## Proposed Change
Rename `PaymentIntent.ProofImageUrl` to `PaymentIntent.ProofFileKey` (or add new property and deprecate old one)

## Impact Analysis

### Files Requiring Updates

#### Core Entity
- `PointOfSale.Core/Entities/PaymentIntent.cs` - Property definition

#### Controllers
- `PointOfSale.API/Controllers/AdminManualPaymentController.cs` - Lines 367, 373, 407, 409, 414, 421
- `PointOfSale.API/Controllers/StorefrontManualWalletController.cs` - Line 156

#### Services
- `PointOfSale.API/Services/Payments/PaymentService.cs` - Lines 221, 333, 1685
- `PointOfSale.API/Services/Payments/Adapters/ManualWalletPaymentAdapter.cs` - Line 34
- `PointOfSale.API/Services/Checkout/CheckoutService.cs` - Line 2284
- `PointOfSale.API/Services/Admin/AdminOrderService.cs` - Line 481
- `PointOfSale.API/Services/ManualWalletProofStorage.cs` - Method parameter names (lines 9, 14, 89, 91)

#### Jobs
- `PointOfSale.API/Jobs/ManualWalletProofCleanupJob.cs` - Multiple references (lines 81, 92, 114, 119, 122, 124, 127, 180, 187, 205, 226, 233, 236, 238, 239, 257, 308, 315)

#### Database
- `scripts/AddPaymentIntentColumns_FawryManualWallet.sql` - Column definition and comments
- `check-whatsapp-payment-setup.sql` - Query reference
- New migration needed to rename column: `ALTER TABLE "PaymentIntents" RENAME COLUMN "ProofImageUrl" TO "ProofFileKey"`

#### CheckoutSession Entity
- Also has `ProofImageUrl` property that should be renamed for consistency

### Migration Strategy

#### Option 1: Direct Rename (Breaking Change)
1. Create migration to rename database column
2. Update entity property name
3. Update all code references
4. Deploy with downtime window

#### Option 2: Gradual Migration (Non-Breaking)
1. Add new `ProofFileKey` property to entity
2. Create migration to add new column
3. Update code to write to both columns
4. Backfill existing data: `UPDATE "PaymentIntents" SET "ProofFileKey" = "ProofImageUrl" WHERE "ProofFileKey" IS NULL`
5. Update code to read from `ProofFileKey`
6. Mark `ProofImageUrl` as `[Obsolete]`
7. After grace period, drop old column

### Recommended Approach
**Option 2 (Gradual Migration)** is recommended for production safety.

## Effort Estimate
- Medium-Large (affects ~15+ files, requires database migration)
- Estimated time: 4-6 hours including testing

## Priority
- **Low-Medium**: Code works correctly, but naming clarity improves maintainability
- Can be scheduled for next maintenance window or refactoring sprint

## Related Issues
- Similar naming issue exists in `CheckoutSession.ProofImageUrl`
- Consider renaming both entities together for consistency

## Testing Requirements
- Unit tests for all updated services
- Integration tests for checkout flow with manual wallet
- Verify admin proof image download still works
- Verify cleanup job still processes proofs correctly
- Database migration rollback testing

## Notes
- This is a semantic improvement, not a functional fix
- Current code is working correctly despite the naming mismatch
- The property was originally designed for URLs but evolved to store object keys
