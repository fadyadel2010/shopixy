# Phase 10.3.2 - Storefront Login / Register

## ✅ Implementation Complete

This phase implements Storefront customer authentication with complete separation from POS authentication.

---

## 🎯 What Was Implemented

### 1. Separate JWT Authentication System

**Key Principle:** Storefront JWTs are COMPLETELY SEPARATE from POS JWTs.

| Property | POS JWT | Storefront JWT |
|----------|---------|----------------|
| Entity | `User` | `OnlineCustomer` |
| Issuer | `PointOfSaleAPI` | `StorefrontAPI` |
| Audience | `PointOfSaleClient` | `StorefrontClient` |
| Secret | Separate key | Different key |
| Scheme | `Bearer` (default) | `StorefrontJwt` |
| Token Expiration | 60 minutes | 30 minutes |

### 2. StorefrontJwtSettings Configuration

**File:** `PointOfSale.API/Models/StorefrontJwtSettings.cs`

```csharp
public class StorefrontJwtSettings
{
    public string Secret { get; set; }      // Separate from POS
    public string Issuer { get; set; }      // "StorefrontAPI"
    public string Audience { get; set; }    // "StorefrontClient"
    public int AccessTokenExpirationMinutes { get; set; }  // 30
    public int RefreshTokenExpirationDays { get; set; }    // 7
}
```

**Configuration:** `appsettings.json`
```json
"StorefrontJwt": {
  "Secret": "StorefrontSecretKeyForJWTTokensMustBeAtLeast32CharactersLong!",
  "Issuer": "StorefrontAPI",
  "Audience": "StorefrontClient",
  "AccessTokenExpirationMinutes": 30,
  "RefreshTokenExpirationDays": 7
}
```

### 3. StorefrontAuthService

**File:** `PointOfSale.API/Services/Storefront/StorefrontAuthService.cs`

Implements:
- `RegisterAsync` - Customer registration with password hashing
- `LoginAsync` - Customer authentication with IsActive check
- `RefreshTokenAsync` - Token refresh (stub for future implementation)
- `ValidateTokenAsync` - JWT token validation

**Security Features:**
- Email normalization using `EmailNormalizer.Normalize()`
- Password hashing using ASP.NET Core Identity `PasswordHasher<OnlineCustomer>`
- IsActive check blocks deactivated customers
- Login attempts for wrong auth provider rejected
- Password strength validation (minimum 8 characters)
- Structured logging with masked emails

### 4. StorefrontAuthController

**File:** `PointOfSale.API/Controllers/StorefrontAuthController.cs`

**Endpoints:**

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/storefront/auth/register` | POST | Public | Register new customer |
| `/api/storefront/auth/login` | POST | Public | Login and get tokens |
| `/api/storefront/auth/refresh` | POST | Public | Refresh access token |
| `/api/storefront/auth/me` | GET | StorefrontJwt | Get current customer |
| `/api/storefront/auth/logout` | POST | Public | Logout (client-side) |

### 5. JWT Claims Structure

Storefront JWT tokens include:
```json
{
  "CustomerId": "guid",
  "TenantId": "guid",
  "email": "customer@example.com",
  "Provider": "local",
  "EmailVerified": "true/false",
  "DisplayName": "Customer Name",
  "sub": "guid",
  "jti": "unique-token-id",
  "iat": 1234567890,
  "exp": 1234567890,
  "iss": "StorefrontAPI",
  "aud": "StorefrontClient"
}
```

### 6. Dual Authentication Scheme

**Program.cs Configuration:**
```csharp
builder.Services.AddAuthentication()
    // POS JWT (default scheme)
    .AddJwtBearer(...)
    // Storefront JWT (separate scheme)
    .AddJwtBearer("StorefrontJwt", ...);
```

**Usage in Controllers:**
```csharp
// POS API (default)
[Authorize]
public class PosController { }

// Storefront API
[Authorize(AuthenticationSchemes = "StorefrontJwt")]
public class StorefrontController { }
```

---

## 🏗️ Architecture Compliance

| Rule | Status |
|------|--------|
| POS and Storefront auth are separate | ✅ |
| Different JWT issuer/audience | ✅ |
| Different JWT secret | ✅ |
| OnlineCustomer entity (not User) | ✅ |
| IsActive blocks deactivated customers | ✅ |
| Password hashing (ASP.NET Core Identity) | ✅ |
| Email normalization | ✅ |
| PostgreSQL is source of truth | ✅ |
| Events published for MongoDB projection | ✅ |
| Tenant isolation enforced | ✅ |

---

## 📡 API Usage Examples

### Register
```http
POST /api/storefront/auth/register
X-Tenant-Id: {tenant-guid}
Content-Type: application/json

{
  "email": "customer@example.com",
  "password": "SecurePassword123",
  "displayName": "John Doe",
  "phone": "+1234567890"
}
```

### Login
```http
POST /api/storefront/auth/login
X-Tenant-Id: {tenant-guid}
Content-Type: application/json

{
  "email": "customer@example.com",
  "password": "SecurePassword123"
}
```

### Response
```json
{
  "customer": {
    "id": "guid",
    "email": "customer@example.com",
    "displayName": "John Doe",
    "phone": "+1234567890",
    "provider": "local",
    "emailVerified": false,
    "createdAtUtc": "2026-01-02T15:00:00Z"
  },
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "base64-encoded-refresh-token",
  "expiresAt": "2026-01-02T15:30:00Z"
}
```

### Get Current Customer (Authenticated)
```http
GET /api/storefront/auth/me
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

---

## 📁 Files Created/Modified

### New Files
| File | Description |
|------|-------------|
| `PointOfSale.API/Models/StorefrontJwtSettings.cs` | JWT settings for Storefront |
| `PointOfSale.API/Services/Storefront/StorefrontAuthService.cs` | Auth service with DTOs |
| `PointOfSale.API/Controllers/StorefrontAuthController.cs` | Auth endpoints |

### Modified Files
| File | Changes |
|------|---------|
| `PointOfSale.API/Program.cs` | Added dual JWT schemes, service registration |
| `PointOfSale.API/appsettings.json` | Added StorefrontJwt section |
| `PointOfSale.API/Controllers/StorefrontOnlineCustomerController.cs` | Updated to use StorefrontJwt scheme |

---

## 🔐 Security Notes

1. **Password Hashing**: Uses ASP.NET Core Identity's `PasswordHasher<T>` which implements PBKDF2 with HMAC-SHA256, 128-bit salt, and automatic rehashing on algorithm upgrades.

2. **IsActive Check**: Deactivated customers cannot login:
   ```csharp
   if (!customer.IsActive)
   {
       return Error("ACCOUNT_DEACTIVATED");
   }
   ```

3. **Email Normalization**: All emails are normalized using `EmailNormalizer.Normalize()` before comparison and storage.

4. **Separate Secrets**: POS and Storefront JWT secrets are different - tokens cannot be interchanged.

5. **Shorter Token Lifetime**: Storefront tokens expire in 30 minutes (vs 60 for POS) for security on public networks.

---

## 🚫 Not Yet Implemented

| Feature | Phase |
|---------|-------|
| Refresh Token Storage | 10.3.2.5 |
| Email Verification Flow | 10.3.2.5 |
| Password Reset | 10.3.3 |
| Social Login (Google/Facebook) | 10.3.3 |
| Rate Limiting | Future |
| Token Blacklist (Redis) | Future |

---

## ✅ Build Status

```
Build: ✅ SUCCEEDED
Errors: 0
Warnings: 18 (pre-existing)
```

---

## 🎯 Ready for Testing

The Storefront authentication system is ready for testing. Key test scenarios:

1. **Registration**
   - Valid email/password → Success
   - Duplicate email → EMAIL_EXISTS error
   - Weak password → WEAK_PASSWORD error

2. **Login**
   - Valid credentials → JWT tokens returned
   - Wrong password → INVALID_CREDENTIALS error
   - Deactivated account → ACCOUNT_DEACTIVATED error
   - Social login account → WRONG_AUTH_METHOD error

3. **Profile Access**
   - Valid JWT → Profile returned
   - Expired JWT → 401 Unauthorized
   - POS JWT → 401 (wrong scheme)

---

## 🔄 Next Steps

1. **Phase 10.3.2.5** - Email Verification Flow
   - Email verification tokens
   - Verification email sending
   - Rate-limited resend

2. **Phase 10.3.3** - Social Login
   - Google OAuth
   - Facebook OAuth

3. **Phase 10.3.4** - Refresh Token Storage
   - PostgreSQL or Redis storage
   - Token revocation on deactivation
