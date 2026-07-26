# Security Fix: Cart Merge Customer ID Spoofing

## Vulnerability
The `MergeCart` endpoint in `StorefrontCartController` was accepting an arbitrary `X-Customer-Id` header and allowing merging into any customer's cart without validating against the authenticated user's claims.

## Impact
- Unauthorized cart merging: An attacker could merge their session cart into any customer's cart
- Cart poisoning: Malicious items could be injected into victim's cart
- Privacy violation: Attacker could manipulate another customer's shopping experience
- Potential financial impact: Unwanted items added to victim's cart

## Attack Scenario
1. Attacker authenticates as Customer A (gets valid JWT)
2. Attacker sends merge request with their session cart
3. Attacker spoofs `X-Customer-Id` header to Customer B's ID
4. System merges attacker's cart into Customer B's cart
5. Customer B sees unexpected items in their cart

## Fix Applied
Changed `MergeCart` endpoint to derive the target customer ID from the authenticated `CustomerId` claim instead of trusting the `X-Customer-Id` header.

### Changes Made:

1. **Removed header-based customer ID resolution**:
   - No longer trusts `X-Customer-Id` header as the source of truth
   - Derives customer ID from authenticated JWT claim only

2. **Added claim-based authentication**:
   ```csharp
   // SECURITY - Derive customer ID from authenticated claim, NOT from header
   var customerIdClaim = User.FindFirst("CustomerId")?.Value;
   if (string.IsNullOrWhiteSpace(customerIdClaim) || !Guid.TryParse(customerIdClaim, out var customerId))
   {
       return Unauthorized(new ApiErrorResponse(
           "INVALID_TOKEN",
           "Authentication token is missing or invalid customer ID claim",
           traceId));
   }
   ```

3. **Added header validation (if present)**:
   ```csharp
   // SECURITY - If X-Customer-Id header is present, it must match the authenticated claim
   if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerHeaderValue) &&
       !string.IsNullOrWhiteSpace(customerHeaderValue))
   {
       if (!Guid.TryParse(customerHeaderValue, out var headerCustomerId) || headerCustomerId != customerId)
       {
           return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(
               "CUSTOMER_ID_MISMATCH",
               "X-Customer-Id header does not match authenticated customer",
               traceId));
       }
   }
   ```

4. **Updated documentation**: Added security notes explaining the claim-based approach

### Behavior After Fix:

#### Required:
- Valid StorefrontJwt authentication with `CustomerId` claim
- `X-Tenant-Id` header
- `X-Session-Id` header (source cart)

#### Optional:
- `X-Customer-Id` header (if present, must match authenticated claim)

#### Security Guarantees:
- Customer ID is ALWAYS derived from authenticated JWT claim
- X-Customer-Id header (if present) is validated against claim
- Mismatch between header and claim results in 403 Forbidden
- No way to merge into another customer's cart

### Consistency with Other Endpoints:
This fix aligns `MergeCart` with the existing `ValidateHeaders` method used by all other cart endpoints, which already implements claim-based customer ID resolution.

## Testing Recommendations:
1. Test merge with valid authentication (should succeed)
2. Test merge with missing CustomerId claim (should return 401)
3. Test merge with mismatched X-Customer-Id header (should return 403)
4. Test merge without X-Customer-Id header (should succeed using claim)
5. Test merge with matching X-Customer-Id header (should succeed)
