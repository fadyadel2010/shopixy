# Phase 10.3.4 - Storefront Refresh Tokens & Token Revocation

## ✅ Implementation Complete

This phase implements a production-grade refresh token system for Storefront authentication with token rotation and reuse detection.

---

## 🎯 Objectives Achieved

| Objective | Status |
|-----------|--------|
| Refresh token generation (64 bytes crypto-random) | ✅ |
| Token hashing (SHA256) | ✅ |
| PostgreSQL as source of truth | ✅ |
| Token rotation with chain tracking | ✅ |
| Token reuse detection (SECURITY) | ✅ |
| Soft revoke only (no deletes) | ✅ |
| Tenant isolation | ✅ |
| IP/User-Agent tracking | ✅ |
| POS auth unaffected | ✅ |

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     Refresh Token Flow with Rotation                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. Login / Register                                                         │
│     ├─ Generate 64-byte crypto-random token                                  │
│     ├─ Hash with SHA256                                                      │
│     ├─ Store hash in PostgreSQL                                              │
│     └─ Return raw token ONCE                                                 │
│                                                                              │
│  2. Refresh Token Request                                                    │
│     ├─ Hash incoming token                                                   │
│     ├─ Find by (TenantId, TokenHash)                                         │
│     └─ Check: exists, not expired, not revoked, customer active              │
│                                                                              │
│  3. Token Rotation (CRITICAL)                                                │
│     ├─ Revoke old token                                                      │
│     ├─ Generate new token                                                    │
│     ├─ Link: old.ReplacedByTokenId = new.Id                                  │
│     └─ Return new access + refresh tokens                                    │
│                                                                              │
│  4. Token Reuse Detection (SECURITY EVENT)                                   │
│     If revoked token is reused:                                              │
│     ├─ Revoke ALL tokens for customer                                        │
│     ├─ Emit OnlineCustomerTokenReuseDetectedEvent                            │
│     ├─ Log at SECURITY/WARNING level                                         │
│     └─ Return "TOKEN_REUSE_DETECTED" - force re-login                        │
│                                                                              │
│  5. Logout                                                                   │
│     ├─ Revoke refresh token                                                  │
│     └─ (TODO: Blacklist access token in Redis)                               │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 📁 Files Created/Modified

### New Files

| File | Description |
|------|-------------|
| `PointOfSale.Core/Entities/OnlineCustomerRefreshToken.cs` | Refresh token entity |
| `PointOfSale.Core/Security/RefreshTokenSecurity.cs` | Token generator & hasher |
| `PointOfSale.Core/Events/OnlineCustomerRefreshTokenEvents.cs` | Domain events |
| `PointOfSale.Infrastructure/Data/Configurations/OnlineCustomerRefreshTokenConfiguration.cs` | EF Core config |
| `PointOfSale.API/Services/Storefront/StorefrontRefreshTokenService.cs` | Core service |
| `create_refresh_tokens_table.sql` | SQL migration script |

### Modified Files

| File | Changes |
|------|---------|
| `ApplicationDbContext.cs` | Added DbSet |
| `Program.cs` | Service registration |
| `StorefrontAuthController.cs` | Integrated refresh token operations |

---

## 🔐 Security Features

### 1. Token Generation
```csharp
// 64 bytes of cryptographically secure random data
var tokenBytes = RandomNumberGenerator.GetBytes(64);
return Convert.ToBase64String(tokenBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
```

### 2. Token Hashing
```csharp
// SHA256 hash stored in database - raw token never persisted
var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
return Convert.ToHexString(hash).ToLowerInvariant();
```

### 3. Token Rotation Chain
```csharp
// On rotation: old token links to new for reuse detection
existingToken.ReplacedByTokenId = newToken.Id;
existingToken.Revoke("Token rotation");
```

### 4. Token Reuse Detection
```csharp
// If revoked token is reused - SECURITY EVENT
if (existingToken.IsRevoked)
{
    // Revoke ALL customer tokens
    await RevokeAllTokensAsync(tenantId, customerId, "Token reuse detected");
    
    // Emit security event
    await _publishEndpoint.Publish(new OnlineCustomerTokenReuseDetectedEvent { ... });
    
    return Error("TOKEN_REUSE_DETECTED");
}
```

### 5. Soft Revoke Only
- Tokens are NEVER deleted
- `IsRevoked = true`, `RevokedAtUtc`, `RevokedReason` set on revocation
- Allows audit trail and reuse detection

---

## 📡 API Endpoints

### Login
```http
POST /api/storefront/auth/login
X-Tenant-Id: {tenant-guid}

Response includes PostgreSQL-backed refresh token
```

### Refresh
```http
POST /api/storefront/auth/refresh
X-Tenant-Id: {tenant-guid}
Content-Type: application/json

{
  "refreshToken": "url-safe-base64-token"
}
```

**Responses:**
- `200 OK` - New access + refresh tokens
- `401 TOKEN_EXPIRED` - Token expired, re-login required
- `401 TOKEN_REUSE_DETECTED` - Security alert, all tokens revoked

### Logout
```http
POST /api/storefront/auth/logout
X-Tenant-Id: {tenant-guid}
Content-Type: application/json

{
  "refreshToken": "token-to-revoke"
}
```

---

## 🗃️ Database Schema

### OnlineCustomerRefreshTokens Table

| Column | Type | Description |
|--------|------|-------------|
| Id | uuid | Primary key |
| TenantId | uuid | NOT NULL, tenant isolation |
| OnlineCustomerId | uuid | FK to OnlineCustomers |
| TokenHash | varchar(128) | SHA256 hash |
| ExpiresAtUtc | timestamp | Token expiration |
| IsRevoked | boolean | Revocation flag |
| RevokedAtUtc | timestamp | When revoked |
| RevokedReason | varchar(256) | Revocation reason |
| ReplacedByTokenId | uuid | Rotation chain link |
| CreatedAtUtc | timestamp | Creation time |
| CreatedByIp | varchar(45) | Client IP for auditing |
| CreatedByUserAgent | varchar(512) | User agent for auditing |

### Indexes

| Index | Columns | Type |
|-------|---------|------|
| IX_TenantId_TokenHash | TenantId, TokenHash | Unique |
| IX_TenantId_CustomerId | TenantId, OnlineCustomerId | Regular |
| IX_TenantId_CustomerId_IsRevoked | TenantId, OnlineCustomerId, IsRevoked | Regular |
| IX_ExpiresAtUtc | ExpiresAtUtc | Regular |

---

## 🔄 Domain Events

### OnlineCustomerRefreshTokenIssuedEvent
Published when a refresh token is created (login/register).

### OnlineCustomerRefreshTokenRevokedEvent
Published when a single token is revoked (rotation/logout).

### OnlineCustomerAllTokensRevokedEvent
Published when ALL tokens for a customer are revoked (password change, deactivation, reuse detection).

### OnlineCustomerTokenReuseDetectedEvent (SECURITY)
Published when a revoked token is reused. Triggers:
- Revocation of all customer tokens
- Security alerting
- Force re-authentication

---

## ✅ Definition of Done Checklist

| Requirement | Status |
|-------------|--------|
| Token generation is secure (64 bytes crypto-random) | ✅ |
| Tokens are hashed (SHA256) | ✅ |
| Raw token never stored | ✅ |
| PostgreSQL is source of truth | ✅ |
| No MongoDB used for tokens | ✅ |
| Token rotation implemented | ✅ |
| Rotation chain tracking | ✅ |
| Token reuse detection | ✅ |
| Reuse triggers full revocation | ✅ |
| Soft revoke only (no deletes) | ✅ |
| TenantId enforced everywhere | ✅ |
| IP/User-Agent tracked | ✅ |
| POS auth unaffected | ✅ |
| Build passes | ✅ |

---

## 🚀 Next Steps

### Immediate (After Migration)
1. Run SQL migration: `create_refresh_tokens_table.sql`
2. Test login → refresh → refresh flow
3. Test token reuse detection (use old token after rotation)

### Future Phases
- **Phase 10.3.3** - Social Login (Google/Facebook)
- **Phase 10.3.5** - Password Reset
- **Redis JWT Blacklist** - Invalidate access tokens on logout/revocation

---

## 📝 Usage Example

### Frontend Flow

```javascript
// 1. Login
const loginResponse = await fetch('/api/storefront/auth/login', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Tenant-Id': tenantId
  },
  body: JSON.stringify({ email, password })
});
const { accessToken, refreshToken } = await loginResponse.json();

// Store refresh token securely (httpOnly cookie recommended)
localStorage.setItem('refreshToken', refreshToken);

// 2. Refresh when access token expires
const refreshResponse = await fetch('/api/storefront/auth/refresh', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Tenant-Id': tenantId
  },
  body: JSON.stringify({ refreshToken: localStorage.getItem('refreshToken') })
});

if (refreshResponse.ok) {
  const { accessToken, refreshToken: newRefreshToken } = await refreshResponse.json();
  localStorage.setItem('refreshToken', newRefreshToken); // IMPORTANT: Store new token
} else if (refreshResponse.status === 401) {
  // Token expired or reuse detected - force re-login
  logout();
  redirectToLogin();
}

// 3. Logout
await fetch('/api/storefront/auth/logout', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Tenant-Id': tenantId
  },
  body: JSON.stringify({ refreshToken: localStorage.getItem('refreshToken') })
});
localStorage.removeItem('refreshToken');
```

### IMPORTANT: Token Rotation Warning

When refreshing tokens, the frontend MUST update the stored refresh token with the new one. Using the old token after rotation will trigger the **Token Reuse Detection** security mechanism and revoke ALL tokens for the customer.
