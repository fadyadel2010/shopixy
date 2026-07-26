# Security Fix: Tenant Resolution Header Spoofing

## Vulnerability
The `TenantResolutionMiddleware` was trusting client-controlled headers (Origin and Referer) for tenant resolution, allowing attackers to spoof these headers and access other tenants' data.

## Impact
- Cross-tenant data access
- Tenant isolation bypass
- Unauthorized access to sensitive tenant-specific resources

## Fix Applied
Removed Origin and Referer headers from tenant resolution logic in `GetTenantCandidateUris` method.

### Changes Made:
1. **Removed client-controlled headers**: Origin and Referer are no longer used for tenant resolution
2. **Host header only**: Only the server-controlled Host header is now trusted
3. **Updated documentation**: Added security notes explaining why these headers are not trusted

### Verified Sources for Tenant Resolution:
1. **Authenticated tenant claim** (from JWT token) - Most authoritative
2. **Host header** (server-controlled) - Trusted for request-based resolution
3. **Custom domain mapping** - StorefrontBaseUrl from database
4. **Tenant slug extraction** - From subdomain pattern in Host header

### What Was Removed:
```csharp
// REMOVED: Client-controlled headers that can be spoofed
foreach (var headerName in new[] { "Origin", "Referer" })
{
    if (context.Request.Headers.TryGetValue(headerName, out var headerValues) &&
        TryBuildHeaderUri(headerValues, out var headerUri) &&
        seenAuthorities.Add(headerUri.Authority))
    {
        yield return headerUri;
    }
}
```

## Security Guarantee
The middleware now only resolves tenants from:
- Token-bound tenant claims (authenticated users)
- Server-controlled Host header (cannot be spoofed)
- Database-verified custom domains
- Validated subdomain patterns

All client-controlled inputs are rejected for tenant resolution.
