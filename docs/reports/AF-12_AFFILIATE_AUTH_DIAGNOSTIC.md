# AF-12: Affiliate Portal 403 Error - Diagnostic Guide

## 🔍 Issue
Getting 403 Forbidden errors when accessing `/affiliate/portal`:
```
GET http://localhost:5173/api/affiliate/profile 403 (Forbidden)
GET http://localhost:5173/api/affiliate/dashboard 403 (Forbidden)
```

## ✅ Root Cause
The affiliate portal requires authentication with an **Affiliate JWT token**. The 403 error means:
1. User is not logged in as an affiliate, OR
2. JWT token doesn't have the required "Affiliate" role claim, OR
3. Token has expired

## 🔐 Authentication Flow

### How Affiliate Auth Works
1. **Separate from POS Auth**: Affiliate portal uses its own authentication system
2. **Token Storage**: Tokens stored in localStorage with `affiliate_` prefix
3. **Required Claim**: JWT must have `role=Affiliate` claim
4. **Policy**: Backend enforces `[Authorize(Policy = "AffiliateOnly")]`

### Token Storage Keys
```javascript
affiliate_accessToken      // JWT access token
affiliate_refreshToken     // Refresh token
affiliate_tenantId         // Tenant ID
affiliate_accountId        // Affiliate account ID
affiliate_email            // Affiliate email
affiliate_displayName      // Display name
affiliate_referralCode     // Referral code (e.g., AFFLIA9689)
affiliate_storeSlug        // Store slug
```

## 🛠 SOLUTION: Login as Affiliate

### Step 1: Check if Logged In
Open browser console and run:
```javascript
// Check if affiliate token exists
console.log('Access Token:', localStorage.getItem('affiliate_accessToken'))
console.log('Referral Code:', localStorage.getItem('affiliate_referralCode'))
console.log('Is Authenticated:', !!localStorage.getItem('affiliate_accessToken'))
```

**If tokens are missing** → You need to login

### Step 2: Navigate to Affiliate Login
Go to: `http://localhost:5174/affiliate/login`

**Note**: Affiliate portal runs on port **5174** (storefront), not 5173 (admin)

### Step 3: Login or Register

#### Option A: Login with Existing Account
If you already have an affiliate account:
1. Enter email
2. Enter password
3. Select store (if multi-tenant)
4. Click "Login"

#### Option B: Register New Affiliate Account
If you don't have an account:
1. Go to: `http://localhost:5174/affiliate/register`
2. Fill in:
   - Email
   - Password (min 8 chars, uppercase, lowercase, number, special char)
   - Display Name
   - Store Slug
3. Click "Register"
4. You'll be auto-logged in

### Step 4: Verify Authentication
After login, check console:
```javascript
// Should now show tokens
console.log('Access Token:', localStorage.getItem('affiliate_accessToken'))
console.log('Referral Code:', localStorage.getItem('affiliate_referralCode'))
```

### Step 5: Access Portal
Now navigate to: `http://localhost:5174/affiliate/portal`

Should work without 403 errors!

## 🔍 Debugging 403 Errors

### Check 1: Verify Token Exists
```javascript
const token = localStorage.getItem('affiliate_accessToken')
if (!token) {
  console.error('❌ No affiliate token found - need to login')
} else {
  console.log('✅ Token exists:', token.substring(0, 20) + '...')
}
```

### Check 2: Decode JWT to Check Claims
```javascript
const token = localStorage.getItem('affiliate_accessToken')
if (token) {
  const payload = JSON.parse(atob(token.split('.')[1]))
  console.log('JWT Payload:', payload)
  console.log('Role Claim:', payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'])
  console.log('Affiliate Account ID:', payload.affiliate_account_id)
}
```

**Expected Claims**:
- `role`: "Affiliate"
- `affiliate_account_id`: GUID
- `tenant_id`: GUID
- `sub`: AffiliateUser ID

### Check 3: Test with Debug Endpoint
The backend has a debug endpoint to check claims:
```javascript
fetch('/api/affiliate/debug-claims', {
  headers: {
    'Authorization': `Bearer ${localStorage.getItem('affiliate_accessToken')}`,
    'X-Tenant-Id': localStorage.getItem('affiliate_tenantId')
  }
})
.then(r => r.json())
.then(data => console.log('Debug Claims:', data))
```

### Check 4: Verify Token Not Expired
```javascript
const token = localStorage.getItem('affiliate_accessToken')
if (token) {
  const payload = JSON.parse(atob(token.split('.')[1]))
  const exp = payload.exp * 1000 // Convert to milliseconds
  const now = Date.now()
  
  if (exp < now) {
    console.error('❌ Token expired:', new Date(exp))
    console.log('Need to refresh or re-login')
  } else {
    console.log('✅ Token valid until:', new Date(exp))
  }
}
```

## 🚨 Common Issues

### Issue 1: Wrong Port
**Problem**: Trying to access affiliate portal on admin port (5173)  
**Solution**: Use storefront port (5174)
- Admin: `http://localhost:5173` (POS management)
- Storefront: `http://localhost:5174` (customer-facing + affiliate portal)

### Issue 2: Using POS Token
**Problem**: Logged in as POS admin, trying to access affiliate portal  
**Solution**: Affiliate portal requires separate affiliate login
- POS tokens stored as: `accessToken`, `refreshToken`
- Affiliate tokens stored as: `affiliate_accessToken`, `affiliate_refreshToken`

### Issue 3: Token Expired
**Problem**: Token expired (default: 1 hour)  
**Solution**: Refresh token or re-login
```javascript
// Clear expired tokens
Object.keys(localStorage)
  .filter(key => key.startsWith('affiliate_'))
  .forEach(key => localStorage.removeItem(key))

// Then login again
window.location.href = '/affiliate/login'
```

### Issue 4: No Affiliate Account
**Problem**: User exists in POS but not as affiliate  
**Solution**: Register as affiliate separately
- POS users and affiliate users are separate entities
- Need to register at `/affiliate/register`

## 📋 Quick Checklist

Before accessing `/affiliate/portal`, verify:

- [ ] On correct port (5174 for storefront)
- [ ] Logged in as affiliate (not POS admin)
- [ ] Token exists in localStorage (`affiliate_accessToken`)
- [ ] Token not expired (check `exp` claim)
- [ ] Token has `role=Affiliate` claim
- [ ] Tenant ID stored (`affiliate_tenantId`)
- [ ] Referral code stored (`affiliate_referralCode`)

## 🎯 Quick Fix Script

Run this in browser console to diagnose:

```javascript
// Affiliate Auth Diagnostic Script
console.log('=== AFFILIATE AUTH DIAGNOSTIC ===')

const token = localStorage.getItem('affiliate_accessToken')
const tenantId = localStorage.getItem('affiliate_tenantId')
const referralCode = localStorage.getItem('affiliate_referralCode')

console.log('1. Token exists:', !!token)
console.log('2. Tenant ID:', tenantId || '❌ MISSING')
console.log('3. Referral Code:', referralCode || '❌ MISSING')

if (token) {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    const exp = new Date(payload.exp * 1000)
    const now = new Date()
    
    console.log('4. Token expires:', exp.toLocaleString())
    console.log('5. Token valid:', exp > now ? '✅ YES' : '❌ EXPIRED')
    console.log('6. Role claim:', payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || '❌ MISSING')
    console.log('7. Affiliate Account ID:', payload.affiliate_account_id || '❌ MISSING')
    
    if (exp < now) {
      console.log('\n⚠️ TOKEN EXPIRED - Need to re-login')
      console.log('Run: window.location.href = "/affiliate/login"')
    } else if (!payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']) {
      console.log('\n⚠️ MISSING ROLE CLAIM - Invalid token')
      console.log('Run: window.location.href = "/affiliate/login"')
    } else {
      console.log('\n✅ AUTH LOOKS GOOD - Should be able to access portal')
    }
  } catch (e) {
    console.error('❌ Invalid token format:', e)
  }
} else {
  console.log('\n❌ NOT LOGGED IN')
  console.log('Go to: http://localhost:5174/affiliate/login')
}

console.log('=================================')
```

## 🔗 Related Files

### Backend
- `PointOfSale.API/Controllers/AffiliatePortalController.cs` - Portal endpoints
- `PointOfSale.API/Controllers/AffiliateAuthController.cs` - Auth endpoints
- `PointOfSale.API/Program.cs` - Authorization policy configuration

### Frontend
- `PointOfSale.Web/src/services/affiliateAuthApi.js` - Auth service
- `PointOfSale.Web/src/services/affiliateApi.js` - Portal API service
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx` - Portal page

## ✅ Success Criteria

After following this guide, you should be able to:
1. Login as affiliate successfully
2. See tokens in localStorage
3. Access `/affiliate/portal` without 403 errors
4. View dashboard, orders, earnings, and profile
5. Copy affiliate links with your referral code

---

**Note**: This is unrelated to the AF-12 navigation fix. The navigation fix ensures the `?ref=` parameter is preserved when customers navigate the storefront. This auth issue is about affiliate portal access for affiliates themselves.
