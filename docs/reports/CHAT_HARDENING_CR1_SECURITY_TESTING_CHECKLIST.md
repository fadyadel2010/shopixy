# 🔒 Chat Hardening CR-1 - Security Testing Checklist

**Date:** February 13, 2026  
**Phase:** CR-1 Security Verification  
**Status:** PENDING EXECUTION

---

## 🎯 Objective

Verify that Phase CR-1 hardening successfully eliminates all identified vulnerabilities through manual security testing and penetration testing.

---

## ✅ Pre-Testing Verification

- [x] Database migration applied successfully
- [x] Backend code deployed
- [x] Frontend code deployed
- [x] Architecture tests passing (25/25)
- [x] Application builds without errors
- [x] No breaking changes introduced

---

## 🔍 Test Suite 1: Token Security

### Test 1.1: JWT Not in URLs
**Objective:** Verify tokens never appear in URLs or logs

**Steps:**
1. Open browser DevTools → Network tab
2. Login to application
3. Connect to SignalR chat
4. Send a message
5. Check all network requests

**Expected:**
- ✅ No `access_token` in query strings
- ✅ No `Authorization` header in WebSocket upgrade
- ✅ Cookie `auth_token` present in requests

**Verification:**
```bash
# Check server logs for token leakage
grep -r "eyJ" /var/log/nginx/access.log
# Should return NO results
```

### Test 1.2: HttpOnly Cookie Protection
**Objective:** Verify cookie cannot be accessed via JavaScript

**Steps:**
1. Login to application
2. Open browser console
3. Try to read cookie: `document.cookie`

**Expected:**
- ✅ `auth_token` NOT visible in `document.cookie`
- ✅ Cookie only sent via HTTP requests

### Test 1.3: Secure Flag Enforcement
**Objective:** Verify cookie only sent over HTTPS

**Steps:**
1. Try to access application via HTTP (not HTTPS)
2. Check if cookie is sent

**Expected:**
- ✅ Cookie NOT sent over HTTP
- ✅ Application redirects to HTTPS

### Test 1.4: SameSite Protection
**Objective:** Verify CSRF protection

**Steps:**
1. Create malicious page on different domain
2. Try to make request to chat API
3. Check if cookie is sent

**Expected:**
- ✅ Cookie NOT sent from different origin
- ✅ Request fails with 401 Unauthorized

---

## 🔍 Test Suite 2: Tenant Isolation

### Test 2.1: Tenant from JWT Only
**Objective:** Verify tenant cannot be spoofed

**Steps:**
1. Login as Tenant A user
2. Intercept SignalR connection
3. Try to add `?tenantId=TENANT_B_ID` to URL
4. Try to send message

**Expected:**
- ✅ Tenant extracted from JWT claims only
- ✅ Query string parameter ignored
- ✅ Message saved with correct tenant from JWT

**Verification:**
```sql
-- Check message has correct tenant
SELECT "TenantId", "SenderUserId", "Body" 
FROM "ServiceRequestMessages" 
ORDER BY "CreatedAt" DESC 
LIMIT 1;
```

### Test 2.2: Cross-Tenant Access Prevention
**Objective:** Verify users cannot access other tenants' chats

**Steps:**
1. Login as Tenant A user
2. Get ServiceRequest ID from Tenant B
3. Try to join conversation
4. Try to send message

**Expected:**
- ✅ JoinConversation fails with "Unauthorized"
- ✅ SendMessage fails with "Unauthorized"
- ✅ Connection aborted

### Test 2.3: Tenant Claim Validation
**Objective:** Verify JWT without tenant claim is rejected

**Steps:**
1. Create JWT without `tenant_id` claim
2. Set as cookie
3. Try to connect to SignalR

**Expected:**
- ✅ Connection rejected
- ✅ Error: "Missing tenant_id claim in JWT"

---

## 🔍 Test Suite 3: Database Constraints

### Test 3.1: Invalid SenderRole Rejection
**Objective:** Verify database rejects invalid enum values

**Steps:**
```sql
-- Try to insert invalid SenderRole
INSERT INTO "ServiceRequestMessages" (
    "Id", "TenantId", "ServiceRequestId", "SenderUserId",
    "SenderRole", "MessageType", "Body", "CreatedAt"
) VALUES (
    gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
    'InvalidRole', 'Text', 'Test', NOW()
);
```

**Expected:**
- ✅ INSERT fails
- ✅ Error: `violates check constraint "CHK_SenderRole_Valid"`

### Test 3.2: Invalid MessageType Rejection
**Objective:** Verify database rejects invalid enum values

**Steps:**
```sql
-- Try to insert invalid MessageType
INSERT INTO "ServiceRequestMessages" (
    "Id", "TenantId", "ServiceRequestId", "SenderUserId",
    "SenderRole", "MessageType", "Body", "CreatedAt"
) VALUES (
    gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
    'Buyer', 'InvalidType', 'Test', NOW()
);
```

**Expected:**
- ✅ INSERT fails
- ✅ Error: `violates check constraint "CHK_MessageType_Valid"`

### Test 3.3: Message Immutability
**Objective:** Verify messages cannot be modified

**Steps:**
```sql
-- Try to update message body
UPDATE "ServiceRequestMessages"
SET "Body" = 'Modified'
WHERE "Id" = (SELECT "Id" FROM "ServiceRequestMessages" LIMIT 1);
```

**Expected:**
- ✅ UPDATE fails
- ✅ Error: `Messages are immutable`

---

## 🔍 Test Suite 4: Business Logic Authority

### Test 4.1: ChatService is Authority
**Objective:** Verify trigger does NOT enforce business logic

**Steps:**
1. Check trigger function definition
2. Verify it only logs to audit table

**Verification:**
```sql
SELECT routine_definition 
FROM information_schema.routines 
WHERE routine_name = 'audit_message_insert';
```

**Expected:**
- ✅ Contains `INSERT INTO "ChatAuditLog"`
- ✅ Does NOT contain `IF request_status`
- ✅ Does NOT contain `RAISE EXCEPTION 'Cannot send message'`

### Test 4.2: Pessimistic Locking
**Objective:** Verify race conditions prevented

**Steps:**
1. Open two browser tabs
2. Login as buyer and provider
3. Simultaneously send messages
4. Check database for consistency

**Expected:**
- ✅ Both messages saved
- ✅ No duplicate messages
- ✅ Correct order maintained

### Test 4.3: Rate Limiting
**Objective:** Verify rate limiting enforced

**Steps:**
1. Send 60 messages in quick succession
2. Try to send 61st message

**Expected:**
- ✅ First 60 messages succeed
- ✅ 61st message fails
- ✅ Error: "Rate limit exceeded"

---

## 🔍 Test Suite 5: Re-Authentication

### Test 5.1: Periodic Re-Authentication
**Objective:** Verify connections revalidate every 15 minutes

**Steps:**
1. Connect to chat
2. Wait 16 minutes (or mock time)
3. Send message

**Expected:**
- ✅ Message triggers revalidation
- ✅ CanUserParticipateAsync called
- ✅ Message sent if still authorized

### Test 5.2: Permission Revocation
**Objective:** Verify connection aborted if permissions lost

**Steps:**
1. Connect to chat as buyer
2. Admin cancels ServiceRequest
3. Wait 16 minutes
4. Try to send message

**Expected:**
- ✅ Revalidation fails
- ✅ Connection aborted
- ✅ Error: "Participation rights revoked"

---

## 🔍 Test Suite 6: Penetration Testing

### Test 6.1: XSS Attack
**Objective:** Verify XSS protection

**Steps:**
1. Send message with XSS payload: `<script>alert('XSS')</script>`
2. Check if script executes

**Expected:**
- ✅ Script does NOT execute
- ✅ Message displayed as text
- ✅ HTML escaped

### Test 6.2: SQL Injection
**Objective:** Verify SQL injection protection

**Steps:**
1. Send message with SQL payload: `'; DROP TABLE "ServiceRequestMessages"; --`
2. Check database

**Expected:**
- ✅ Message saved as text
- ✅ No SQL execution
- ✅ Table still exists

### Test 6.3: JWT Tampering
**Objective:** Verify JWT signature validation

**Steps:**
1. Get valid JWT
2. Modify `tenant_id` claim
3. Try to connect

**Expected:**
- ✅ Connection rejected
- ✅ Error: "Invalid signature"

### Test 6.4: Replay Attack
**Objective:** Verify token expiration

**Steps:**
1. Capture valid JWT
2. Wait for expiration (8 hours)
3. Try to use expired token

**Expected:**
- ✅ Connection rejected
- ✅ Error: "Token expired"

---

## 🔍 Test Suite 7: Load Testing

### Test 7.1: Concurrent Connections
**Objective:** Verify system handles load

**Steps:**
1. Simulate 100 concurrent SignalR connections
2. Each sends 10 messages
3. Monitor performance

**Expected:**
- ✅ All connections succeed
- ✅ All messages delivered
- ✅ Response time < 100ms

### Test 7.2: Rate Limiting Under Load
**Objective:** Verify rate limiting scales

**Steps:**
1. Simulate 50 users
2. Each tries to send 100 messages
3. Monitor rate limiting

**Expected:**
- ✅ Each user limited to 60 messages/hour
- ✅ No rate limit bypass
- ✅ System remains stable

---

## 📊 Test Results Template

```markdown
## Test Execution Report

**Date:** [DATE]
**Tester:** [NAME]
**Environment:** [STAGING/PRODUCTION]

### Summary
- Total Tests: 20
- Passed: __
- Failed: __
- Blocked: __

### Failed Tests
| Test ID | Description | Severity | Notes |
|---------|-------------|----------|-------|
| [ID] | [Description] | [HIGH/MEDIUM/LOW] | [Details] |

### Recommendations
1. [Recommendation 1]
2. [Recommendation 2]

### Sign-Off
- [ ] All critical tests passed
- [ ] All high-severity issues resolved
- [ ] System ready for production

**Tester Signature:** _______________
**Date:** _______________
```

---

## 🚀 Deployment Checklist

After all tests pass:

- [ ] Database migration applied to production
- [ ] Backend deployed to production
- [ ] Frontend deployed to production
- [ ] Monitoring alerts configured
- [ ] Rollback plan documented
- [ ] Team notified of changes
- [ ] Documentation updated

---

**END OF CHECKLIST**
