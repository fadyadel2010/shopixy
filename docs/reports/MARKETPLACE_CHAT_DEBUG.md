# Marketplace Chat Debug Guide

## Issue
Chat messages fail to send with error: "Failed to invoke 'SendMessage' due to an error on the server"

## Debugging Steps

### 1. Check Backend Console Logs
Look at your backend console output (where you ran `dotnet run` or the API). When you try to send a message, you should see an error with a stack trace. The error message will tell us exactly what's failing.

Look for lines containing:
- `Error sending message from user`
- `Exception`
- `StackTrace`

### 2. Check ServiceRequest Data
Run this query in PgAdmin4 to verify the service request exists and check its tenant:

```sql
SELECT 
    "Id",
    "ServiceId",
    "BuyerUserId",
    "ProviderUserId",
    "TenantId",
    "Status",
    "IsDeleted",
    "CreatedAt"
FROM "ServiceRequests"
WHERE "Id" = 'YOUR_REQUEST_ID_HERE'  -- Replace with actual request ID from URL
AND "IsDeleted" = FALSE;
```

### 3. Check User's Tenant
The current user's tenant might not match the ServiceRequest's tenant. This is expected in marketplace scenarios but we need to verify.

### 4. Potential Issues

#### Issue A: TenantId Mismatch in Rate Limiting
The `ValidateRateLimitAsync` method checks:
```csharp
m.TenantId == tenantId
```

If the user's current tenantId doesn't match the ServiceRequest's tenantId, the rate limit check might fail or behave unexpectedly.

#### Issue B: Transaction Isolation
The `SendMessageAsync` uses a transaction with FOR UPDATE lock. If there's a deadlock or the transaction fails, it will throw an error.

#### Issue C: AllowsInteraction Check
The request status might not allow interaction. Check if the request status is "Accepted" or another status that allows chat.

### 5. Quick Fix to Try

If the backend logs show a TenantId-related error, we need to update the rate limiting to use the ServiceRequest's TenantId instead of the current user's TenantId.

### 6. Get Request ID from Browser
In the browser console, when you're on the chat page, the URL should be:
```
/marketplace/chat/{requestId}
```

Copy that requestId and use it in the SQL query above.

## Expected Backend Error Messages

Common errors you might see:

1. **"Service request not found"** - The FOR UPDATE query couldn't find the request
2. **"Cannot send message: ServiceRequest is in status X"** - The request status doesn't allow chat
3. **"Unauthorized to send messages"** - User is not buyer or provider
4. **Rate limit exceeded** - Too many messages sent
5. **Transaction deadlock** - Database locking issue

## Next Steps

Please share:
1. The exact error from the backend console
2. The ServiceRequest ID from the URL
3. The result of the SQL query above

This will help us identify the exact issue and fix it.
