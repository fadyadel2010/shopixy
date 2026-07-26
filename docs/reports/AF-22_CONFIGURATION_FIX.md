# AF-22 Configuration Fix - Missing Storefront:BaseUrl

## Problem Found ✅

The consumer is registered and running, but **failing due to missing configuration**:

```
fail: [AF-22] Missing Storefront:BaseUrl configuration
fail: [AF-22:ProductCreated] Missing StorefrontBaseUrl | TenantId: 8a9f672b-43d5-4a2c-9762-5df63410fb73
```

This is the AF-22 hardening fail-fast guard working correctly - it prevents creating broken affiliate links.

## Solution Applied ✅

Added `Storefront:BaseUrl` to `appsettings.json`:

```json
"Storefront": {
  "BaseUrl": "http://localhost:5173",
  "UrlTemplate": "http://{slug}.localhost:5174"
},
```

## What This Fixes

The consumer needs `Storefront:BaseUrl` to build affiliate links:

```
{storefrontUrl}?ref={affiliateCode}
```

Without it, the consumer fails fast and doesn't create MongoDB documents (by design).

## Next Steps

### 1. Restart Backend (REQUIRED!)

```bash
# Stop backend (Ctrl+C)
dotnet run --project PointOfSale.API
```

### 2. Create or Update a Product

The consumer will now process events successfully!

### 3. Check Logs

You should see:

```
info: [AF-22:ProductCreated] ProductId: xxx | TenantId: xxx
info: [AF-22:ProductCreated] Upserted document | ProductId: xxx
```

Instead of:

```
fail: [AF-22] Missing Storefront:BaseUrl configuration
```

### 4. Verify MongoDB

```javascript
use PosEcommer