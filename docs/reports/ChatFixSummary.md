# Chat Functionality Fix Summary

This document summarizes the chronological issues encountered while implementing and debugging the real-time chat functionality, along with the solutions applied.

## 1. SignalR Connection & Authentication (Initial Setup)

### Issue
The frontend could not establish a SignalR connection, failing with `401 Unauthorized` or `404 Not Found` errors. WebSocket connections were immediately closed.

**Root Cause:**
- **WebSockets Limitations:** standard browser `WebSocket` APIs do not support custom headers (e.g., `Authorization: Bearer <token>`).
- **Missing Tenant Context:** The backend requires a `TenantId` to Resolve the database connection, but it wasn't being passed in the initial handshake.
- **Token Extraction:** The backend's JWT middleware only looked for tokens in the `Authorization` header, not the query string.

### Solution
We modified both the frontend connection logic and the backend token retrieval.

**Frontend (`useChat.ts`):**
Updated the connection builder to pass the `accessToken` and `tenantId` in the query string.
```typescript
.withUrl(`/hubs/chat?tenantId=${tenantId}`, {
    accessTokenFactory: () => localStorage.getItem('accessToken')
})
```

**Backend (`Program.cs`):**
Configured the JWT Bearer options to extract the token from the query string when available.
```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        if (context.Request.Query.ContainsKey("access_token"))
        {
            context.Token = context.Request.Query["access_token"];
        }
        return Task.CompletedTask;
    }
};
```

---

## 2. Database Type Mismatch (`DbUpdateException`)

### Issue
Sending a chat message caused the application to crash with a `DbUpdateException`. Inner exception: `42804: column "MessageType" is of type "MessageType" but expression is of type text`.

**Root Cause:**
- The PostgreSQL columns `MessageType` and `SenderRole` were strictly typed PostgreSQL Enums.
- EF Core was configured to send these values as strings (`text`).
- PostgreSQL rejected the mismatch between the incoming string and the expected Enum type.

### Solution
We converted the database columns to `text` to align with the application's string-based handling, removing the rigid Enum dependency.

**SQL Script Applied (`fix_chat_bulletproof.sql`):**
```sql
ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "MessageType" TYPE text;
ALTER TABLE "ServiceRequestMessages" ALTER COLUMN "SenderRole" TYPE text;
DROP TYPE IF EXISTS "MessageType";
DROP TYPE IF EXISTS "SenderRole";
```

---

## 3. Database Trigger Reference Error (`42704`)

### Issue
After fixing the column types, inserting a message failed with: `42704: type "ServiceRequestStatus" does not exist`.

**Root Cause:**
- A database trigger (`validate_message_requires_active_request`) logic relied on a PL/pgSQL function.
- This function declared a variable of type `ServiceRequestStatus` (an Enum).
- The Enum type had been dropped or was missing, causing the function compilation to fail at runtime.

### Solution
We redefined the trigger function to use the flexible `text` type for status checks instead of the missing Enum.

**SQL Script Applied (`fix_chat_bulletproof.sql`):**
```sql
CREATE OR REPLACE FUNCTION validate_message_requires_active_request()
RETURNS TRIGGER AS $$
DECLARE
    request_status text; -- Changed from specific Enum type to text
BEGIN
    -- Logic to check if status is 'Pending', 'Accepted', etc.
END;
$$ LANGUAGE plpgsql;
```

---

## 4. Frontend "Unique Key" Warning

### Issue
The browser console showed `Warning: Each child in a list should have a unique "key" prop`. Debugging revealed that `message.id` was `undefined` for messages loaded from history.

**Root Cause:**
- **Property Name Mismatch:** 
    - SignalR sent messages with the property `id` (camelCase).
    - The REST API (`MarketplaceController`) explicitly projected the ID as `messageId`.
- **Frontend Logic:** The frontend code was updated to use `id`, causing historical messages (with `messageId`) to have undefined keys.

### Solution
We standardized the API response to use `id` everywhere.

**Backend (`MarketplaceController.cs`):**
Updated the anonymous object projection:
```csharp
// Changed from messageId = m.Id
id = m.Id, 
```

**Frontend (`types/index.ts` & `ChatPage.tsx`):**
Updated TypeScript definitions and component logic to consistently use `id`.

---

## Summary of Files Changed
- **Backend**: `Program.cs`, `MarketplaceController.cs`, `ChatHub.cs`
- **Frontend**: `useChat.ts`, `ChatPage.tsx`, `types/index.ts`
- **Database**: `fix_chat_bulletproof.sql` (schema migration)
