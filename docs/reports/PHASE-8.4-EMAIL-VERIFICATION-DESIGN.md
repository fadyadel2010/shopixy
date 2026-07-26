# Phase 8.4 – Email Verification & Account Trust

## Design Specification

**Date:** 2025-12-26  
**Phase:** 8.4 – Email Verification & Account Trust  
**Prerequisites:** Phase 8.0–8.3 (Tenant Registration System)

---

## A. Trust Model

### EmailVerificationStatus Enum

```csharp
public enum EmailVerificationStatus
{
    Unverified = 0,
    Verified = 1
}
```

### Data Location

**Decision:** Extend the `User` entity directly.

**Justification:**
- Email verification is a user-level concern (not tenant-level)
- Owner verification gates tenant-level actions (storefront publishing)
- Simple, single source of truth
- No additional join needed for enforcement checks

**New User Fields:**
```csharp
public class User
{
    // ... existing fields ...
    
    public EmailVerificationStatus EmailVerificationStatus { get; set; } = EmailVerificationStatus.Unverified;
    public DateTime? EmailVerifiedAtUtc { get; set; }
}
```

### EmailVerificationToken Entity (Separate Table)

```csharp
public class EmailVerificationToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; }      // SHA256 of token
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    
    // Navigation
    public User User { get; set; }
}
```

**Why separate table for tokens?**
- Tokens are transient (expire, get used)
- Multiple tokens can exist (resends)
- Easy to clean up expired tokens
- No User table bloat

---

## B. Behavioral Rules

| Action | Unverified | Verified |
|--------|------------|----------|
| Login to POS | ✅ Yes | ✅ Yes |
| Use POS features (products, sales, etc.) | ✅ Yes | ✅ Yes |
| Enable Storefront (`StorefrontStatus = live`) | ❌ No | ✅ Yes |
| Receive system emails (orders, alerts) | ⚠️ Limited (only critical) | ✅ Yes |
| Invite team members | ❌ No | ✅ Yes |
| Access Checkout webhook config | ❌ No | ✅ Yes |

### Enforcement Points

1. **AdminStoreConfigController** - Block `StorefrontStatus` = `live` if owner unverified
2. **User invitation endpoints** - Block if inviter unverified
3. **Webhook config endpoints** - Block if owner unverified

---

## C. Verification Flow

```
┌─────────────────────────────────────────────────────────────────┐
│  REGISTRATION                                                    │
├─────────────────────────────────────────────────────────────────┤
│  Register → Create User (Unverified) → Auto-login → Dashboard   │
│                     ↓                                            │
│            Send Verification Email (async)                       │
└─────────────────────────────────────────────────────────────────┘
                             ↓
┌─────────────────────────────────────────────────────────────────┐
│  POS EXPERIENCE (Unverified)                                     │
├─────────────────────────────────────────────────────────────────┤
│  User sees banner: "Verify email to publish storefront"         │
│  Can use all POS features                                        │
│  Cannot enable storefront                                        │
└─────────────────────────────────────────────────────────────────┘
                             ↓
┌─────────────────────────────────────────────────────────────────┐
│  VERIFICATION                                                    │
├─────────────────────────────────────────────────────────────────┤
│  User receives email → Clicks link → /verify-email?token=xxx    │
│             ↓                                                    │
│  Backend validates token → Marks user verified → Success page   │
└─────────────────────────────────────────────────────────────────┘
                             ↓
┌─────────────────────────────────────────────────────────────────┐
│  POS EXPERIENCE (Verified)                                       │
├─────────────────────────────────────────────────────────────────┤
│  No verification banner                                          │
│  Full access to all features                                     │
│  Can enable storefront                                           │
└─────────────────────────────────────────────────────────────────┘
```

---

## D. Token Strategy

| Property | Value |
|----------|-------|
| Token format | 64-char Base64URL (cryptographically secure) |
| Token storage | SHA256 hash in database (never plain) |
| Token lifetime | 24 hours |
| Token reuse | Single-use (UsedAtUtc set on use) |
| Token per user | Multiple allowed (for resends) |

### Token Generation

```csharp
// Generate
var tokenBytes = RandomNumberGenerator.GetBytes(48);
var token = Base64UrlEncoder.Encode(tokenBytes); // 64 chars

// Store hash
var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
var tokenHashString = Convert.ToHexString(tokenHash);
```

### Link Format

```
https://pos.myplatform.com/verify-email?token={token}
```

**Security:**
- No tenant ID in URL
- No user ID in URL
- Token is the only identifier
- Token reveals nothing about the account

---

## E. API Endpoints

### 1. Verify Email

```
GET /api/auth/verify-email?token={token}
```

**Responses:**

| Status | Body | Meaning |
|--------|------|---------|
| 200 | `{ success: true, message: "Email verified!" }` | Success |
| 400 | `{ error: "INVALID_TOKEN" }` | Token not found |
| 400 | `{ error: "TOKEN_EXPIRED" }` | Token expired |
| 400 | `{ error: "TOKEN_USED" }` | Token already used |
| 400 | `{ error: "ALREADY_VERIFIED" }` | User already verified |

### 2. Resend Verification Email

```
POST /api/auth/resend-verification
Authorization: Bearer {token}
```

**Responses:**

| Status | Body | Meaning |
|--------|------|---------|
| 200 | `{ success: true, message: "Email sent!" }` | Success |
| 400 | `{ error: "ALREADY_VERIFIED" }` | User already verified |
| 429 | `{ error: "RATE_LIMITED" }` | Too many resends |

**Rate Limit:** 3 resends per hour per user

### 3. Get Verification Status

```
GET /api/auth/verification-status
Authorization: Bearer {token}
```

**Response:**
```json
{
  "emailVerified": false,
  "email": "j***n@***.com"
}
```

---

## F. Email Template

### Subject
```
Verify your email for {StoreName}
```

### HTML Body (Minimal)
```html
<div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
  <h1>Verify your email</h1>
  <p>Hi {FirstName},</p>
  <p>Please click the button below to verify your email address.</p>
  <a href="{VerificationLink}" 
     style="display: inline-block; padding: 12px 24px; background: #4F46E5; 
            color: white; text-decoration: none; border-radius: 6px;">
    Verify Email
  </a>
  <p style="margin-top: 20px; color: #666;">
    This link expires in 24 hours.
  </p>
  <p style="color: #999; font-size: 12px;">
    If you didn't create an account, you can safely ignore this email.
  </p>
</div>
```

### Plain Text Fallback
```
Verify your email for {StoreName}

Hi {FirstName},

Please click the link below to verify your email address:

{VerificationLink}

This link expires in 24 hours.

If you didn't create an account, you can safely ignore this email.
```

---

## G. Migration Strategy

### Existing Users

All existing users will be set to `EmailVerificationStatus = Verified`.

**Rationale:**
- They're already using the system
- We can't retroactively verify them
- Breaking existing users is unacceptable

### Migration SQL

```sql
ALTER TABLE "Users" 
  ADD "EmailVerificationStatus" integer NOT NULL DEFAULT 1,
  ADD "EmailVerifiedAtUtc" timestamp with time zone NULL;

-- Set all existing users to Verified
UPDATE "Users" SET "EmailVerificationStatus" = 1 WHERE "EmailVerificationStatus" = 0;
```

---

## H. Audit Events

| Event | Data |
|-------|------|
| `VerificationEmailSent` | UserId, Email (masked), TokenId |
| `EmailVerified` | UserId, Email (masked), TokenId |
| `VerificationFailed` | Reason, TokenHash (partial) |
| `ResendRateLimited` | UserId, AttemptCount |
| `StorefrontBlockedUnverified` | TenantId, UserId |

---

## I. Configuration

```json
{
  "EmailVerification": {
    "Enabled": true,
    "TokenExpirationHours": 24,
    "MaxResendsPerHour": 3,
    "BaseUrl": "https://pos.myplatform.com"
  }
}
```

---

## J. Implementation Order

1. ✅ Data model (User extension + EmailVerificationToken entity)
2. ✅ Token generation service
3. ✅ API endpoints
4. ✅ Email service (logging mock, real SMTP optional)
5. ✅ Enforcement guards
6. ✅ Frontend banner
7. ✅ Frontend verification page
8. ✅ Migration
9. ✅ Audit logging
