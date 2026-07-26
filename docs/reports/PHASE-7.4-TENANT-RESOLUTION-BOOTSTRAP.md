# Phase 7.4 – Tenant Resolution & Storefront Bootstrap Hardening

## Overview

This phase hardens the Storefront bootstrap process to make the frontend tenant-aware, self-resolving, and production-safe, without relying on hardcoded Tenant IDs.

## Goals Achieved

✅ Storefront loads with tenant resolved from subdomain  
✅ No API call sent without Tenant ID  
✅ Session ID persists across reloads  
✅ Invalid tenant shows proper UI (not console error)  
✅ Bootstrap logic is centralized and readable  
✅ No regression to Phase 7.2 or 7.3  
✅ Build passes with zero new warnings  

## Architecture

```
App.tsx
    ↓
bootstrap()
    ↓
┌─────────────────────────────┐
│ Step 1: resolveTenant()     │
│   - Query param (DEV only)  │
│   - Subdomain               │
│   - Env variable (DEV only) │
│   - Fail → TenantNotFound   │
└─────────────────────────────┘
    ↓
┌─────────────────────────────┐
│ Step 2: initializeSession() │
│   - Get/create session ID   │
│   - Persist to localStorage │
└─────────────────────────────┘
    ↓
┌─────────────────────────────┐
│ Step 3: loadStoreConfig()   │
│   - GET /api/storefront/... │
│   - X-Tenant-Id header      │
│   - Fail → StoreUnavailable │
└─────────────────────────────┘
    ↓
┌─────────────────────────────┐
│ Step 4: applyTheme()        │
│   - Set CSS variables       │
└─────────────────────────────┘
    ↓
renderApp() ✓
```

## Files Created

### Bootstrap Module (`src/bootstrap/`)

| File | Purpose |
|------|---------|
| `bootstrap.ts` | Main orchestrated bootstrap flow |
| `tenantResolver.ts` | Subdomain/env tenant resolution |
| `sessionManager.ts` | Guest session persistence |
| `requestContext.ts` | Centralized request headers |
| `bootstrapErrors.ts` | Error types and mapping |
| `index.ts` | Module barrel export |

### System Components (`src/components/system/`)

| File | Purpose |
|------|---------|
| `AppLoading.tsx` | Bootstrap loading screen |
| `TenantNotFound.tsx` | Tenant resolution error |
| `StoreUnavailable.tsx` | Store not found / disabled |
| `NetworkError.tsx` | Network error with retry |
| `index.ts` | System components export |

### Updated Files

| File | Changes |
|------|---------|
| `src/app/App.tsx` | Uses new bootstrap with error screens |
| `src/services/api/httpClient.ts` | Tenant/session headers, validation |
| `src/app/index.ts` | Removed old bootstrap export |
| `src/services/api/index.ts` | Updated exports |
| `.env.example` | New VITE_DEFAULT_TENANT_ID |

## Tenant Resolution Strategy

### Resolution Priority Order

1. **Query Parameter (DEV only)**
   - `?tenant=<uuid>` for testing
   - Only works in development mode

2. **Subdomain**
   - `store1.example.com` → `store1`
   - `myshop.localhost:5174` → `myshop`
   - Reserved subdomains excluded: www, api, admin, etc.

3. **Environment Variable (DEV only)**
   - `VITE_DEFAULT_TENANT_ID`
   - Fallback for local development

4. **Fail**
   - Shows `TenantNotFound` screen
   - Never silently continues

### Reserved Subdomains

These are never treated as tenant identifiers:
- www, api, admin, app
- static, cdn, mail, smtp, ftp
- localhost

### Examples

```
store1.localhost:5174     → tenant: "store1"
www.store.example.com     → no tenant (www reserved)
example.com               → no tenant (no subdomain)
localhost:5174?tenant=abc → tenant: "abc" (DEV only)
```

## Session Management

### Features

- Generates UUID v4 session ID once
- Persists in `localStorage` key: `storefront_session_id`
- Memory fallback if localStorage unavailable
- Reuses across page reloads
- Never regenerates unless explicitly cleared

### API

```typescript
import { getSessionId, initializeSession, clearSession } from '@/bootstrap';

// Get or create session
const sessionId = getSessionId();

// Initialize (called by bootstrap)
initializeSession();

// Clear session (for logout)
clearSession();
```

## HTTP Client Hardening

### Automatic Headers

All API requests include:
- `X-Tenant-Id`: Resolved tenant ID
- `X-Session-Id`: Guest session ID
- `Content-Type: application/json`

### Request Validation

- Requests rejected if tenant not resolved
- Exception: Store config fetch during bootstrap
- Clear error messages for developers

### Error Normalization

```typescript
interface NormalizedError {
    status: number | undefined;
    code?: string;
    message?: string;
    isNetworkError: boolean;
    isServerError: boolean;
    isClientError: boolean;
    retryable: boolean;
}
```

## Error Handling

### Bootstrap Error Codes

| Code | Screen | Retryable |
|------|--------|-----------|
| `TENANT_RESOLUTION_FAILED` | TenantNotFound | No |
| `STORE_NOT_FOUND` | StoreUnavailable | No |
| `STORE_UNAVAILABLE` | StoreUnavailable | No |
| `NETWORK_ERROR` | NetworkError | Yes |
| `UNKNOWN_ERROR` | NetworkError | Yes |

### Error Mapping

| Backend Response | Bootstrap Error |
|-----------------|-----------------|
| Network failure (no status) | NETWORK_ERROR |
| 404 Not Found | STORE_NOT_FOUND |
| 400 TENANT_HEADER_MISSING | TENANT_RESOLUTION_FAILED |
| 400 INVALID_TENANT_ID | TENANT_RESOLUTION_FAILED |
| 5xx Server Error | NETWORK_ERROR (retryable) |

## System Error Screens

### TenantNotFound

- Shown when subdomain cannot resolve to a tenant
- Displays attempted hostname
- Clean, helpful error message

### StoreUnavailable

- Shown when store config returns 404
- Lists possible reasons
- Includes retry button

### NetworkError

- Shown on connection failures
- Includes troubleshooting tips
- Retry button with loading state

### AppLoading

- Bootstrap loading screen
- Animated spinner
- Clean, minimal design

## Environment Variables

```bash
# API base URL (leave empty to use Vite proxy)
VITE_API_URL=

# Default Tenant ID for development (DEV only)
# Used as fallback when subdomain cannot be resolved
VITE_DEFAULT_TENANT_ID=00000000-0000-0000-0000-000000000001
```

## Testing Tenant Resolution

### Local Development

1. **Using query param:**
   ```
   http://localhost:5174?tenant=00000000-0000-0000-0000-000000000001
   ```

2. **Using subdomain (requires hosts file):**
   ```
   # Add to C:\Windows\System32\drivers\etc\hosts
   127.0.0.1 mystore.localhost
   
   # Then access:
   http://mystore.localhost:5174
   ```

3. **Using env variable:**
   ```bash
   # In .env.local
   VITE_DEFAULT_TENANT_ID=00000000-0000-0000-0000-000000000001
   ```

### Production

In production, tenant is ALWAYS resolved from subdomain:
- `nikeshop.yourplatform.com` → tenant: `nikeshop`
- No fallbacks, no query params

## Build Verification

```bash
cd PointOfSale.Storefront
npm run build
# ✓ built in ~1.8s
```

## Non-Goals (Not Implemented)

Per specification:
- ❌ No authentication
- ❌ No cart UI
- ❌ No checkout UI
- ❌ No backend edits
- ❌ No multi-theme system
- ❌ No SEO changes

## Key Design Decisions

### Single Source of Truth

Tenant ID is resolved ONCE during bootstrap and stored in a module-level variable. All subsequent access goes through `getTenantId()`.

### Fail-Fast Strategy

If tenant cannot be resolved, the app shows an error screen immediately. No API calls are made without a valid tenant.

### Idempotent Bootstrap

`bootstrap()` can be called multiple times safely. If already bootstrapped, it returns immediately.

### Session Persistence

Session ID survives page reloads via localStorage. This enables consistent tracking without authentication.

## Next Steps

Potential future enhancements:
- Tenant resolution from custom domain mapping
- Tenant-specific feature flags
- Session expiry handling
- Analytics integration
