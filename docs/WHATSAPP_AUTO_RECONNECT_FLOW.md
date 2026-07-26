# WhatsApp Baileys Auto-Reconnect Flow

## Overview

When a WhatsApp session is idle-closed (to save memory), the system automatically reconnects when a message needs to be sent. **No QR scan is required** because credentials are preserved in Redis.

---

## The Flow

### 1. Session Becomes Idle (24 hours of no activity)

```
┌─────────────────────────────────────────────────────────────┐
│  SessionManager.cleanupIdleSessions()                       │
│                                                             │
│  1. Detects idle session (lastUsedAt > 24 hours ago)        │
│  2. Calls closeIdleSession(tenantId)                        │
│     - socket.end() - closes WebSocket (doesn't logout!)     │
│     - session.status = DISCONNECTED                         │
│     - Saves metadata to Redis                               │
│     - Removes session from memory                           │
│  3. Auth state STAYS in Redis (not deleted!)                │
│                                                             │
│  Result: Memory freed, credentials preserved                │
└─────────────────────────────────────────────────────────────┘
```

### 2. OTP/Order Message Requested

```
┌─────────────────────────────────────────────────────────────┐
│  .NET: WhatsAppMessageDispatcher.DispatchAsync()            │
│                                                             │
│  1. Routes to BaileysWhatsAppTransport                      │
│  2. Calls POST /messages/send                               │
│  3. Request goes to Baileys MessageSender                   │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│  Baileys: MessageSender.sendTextMessage()                   │
│                                                             │
│  1. Check: session in memory?                               │
│     → NO: Call getOrCreateSession()                         │
│        → Restores session metadata from Redis               │
│        → Session has status: DISCONNECTED                   │
│                                                             │
│  2. Check: session.status === CONNECTED?                    │
│     → NO: Call connectSession(tenantId)                     │
│        → Loads auth state from Redis                        │
│        → Creates new Baileys socket                         │
│        → Socket auto-authenticates with stored credentials  │
│        → Status becomes CONNECTED                           │
│                                                             │
│  3. Wait 2 seconds for stabilization                        │
│                                                             │
│  4. Send message via connected socket                       │
│                                                             │
│  Result: Message sent successfully!                         │
└─────────────────────────────────────────────────────────────┘
```

---

## Key Code Locations

### 1. MessageSender Auto-Reconnect (Lines 268-309)
```typescript
// file: src/services/MessageSender.ts

// If session exists but not connected, try to connect
if (session.status !== ConnectionStatus.CONNECTED) {
    sessionLogger.info({ status: currentStatus }, 'Session not connected, attempting to connect...');
    try {
        await this.sessionManager.connectSession(tenantId);
        
        // Wait a moment for the connection to stabilize
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        // Re-get the session to check status
        session = this.sessionManager.getSession(tenantId);
        
        if (session?.status === ConnectionStatus.CONNECTED) {
            sessionLogger.info('Session reconnected successfully');
        }
    } catch (connectError) {
        // Handle error
    }
}
```

### 2. SessionManager.connectSession (Auth Restoration)
```typescript
// file: src/services/SessionManager.ts

// PILLAR 1: SINGLE SOURCE OF TRUTH ENFORCEMENT
const redisAuthState = await this.authPersistence.loadAuthState(tenantId);

if (redisAuthState) {
    // Restore credentials from Redis (authoritative source)
    Object.assign(state.creds, redisAuthState.creds);
    Object.assign(state.keys, redisAuthState.keys);
    sessionLogger.info('PILLAR 1: Restored auth state from Redis');
}

// Create Baileys socket with restored credentials
const socket = makeWASocket({
    auth: state,  // Uses restored credentials
    // ... other options
});
```

### 3. Idle Session Cleanup (Preserves Auth)
```typescript
// file: src/services/SessionManager.ts

private async closeIdleSession(tenantId: string): Promise<void> {
    // Close socket but DON'T logout - preserve auth
    session.socket.end(undefined);  // Not logout()!
    session.socket = null;
    
    // Update status
    this.updateSessionStatus(session, ConnectionStatus.DISCONNECTED);
    
    // Save metadata (auth state stays in Redis)
    await this.saveSessionMetadata(session);
    
    // Remove from memory only
    this.sessions.delete(tenantId);
    
    sessionLogger.info('Idle session closed, auth state preserved for restoration');
}
```

---

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `SESSION_IDLE_TIMEOUT_MINUTES` | 1440 (24h) | Time before idle socket closes |
| `SESSION_CLEANUP_INTERVAL_MINUTES` | 30 | How often to check for idle sessions |

---

## What Triggers Auto-Reconnect?

1. **OTP Request** - Customer requests phone verification
2. **Order Confirmation** - New order placed
3. **Shipment Update** - Order shipped/delivered
4. **Any WhatsApp message** - Any message dispatch

---

## Expected Logs During Auto-Reconnect

```
INFO: Session not in memory, attempting to restore...
INFO: Restored session from Redis | status: DISCONNECTED
INFO: Session not connected, attempting to connect...
INFO: PILLAR 1: Found valid auth state in Redis
INFO: PILLAR 1: Restored auth state from Redis (authoritative source)
INFO: opened connection to WA
INFO: Session reconnected successfully
INFO: Message sent successfully
```

---

## Failure Scenarios

| Scenario | What Happens |
|----------|-------------|
| Auth expired (14+ days offline) | WhatsApp invalidates credentials → POISONED/LOGGED_OUT status → QR required |
| Redis down | Cannot restore auth → Error returned |
| Rate limited | Returns error with `retryAfterMs` |
| Admission control | System at capacity → Returns error |

---

## Summary

✅ **Sessions auto-reconnect from stored credentials**  
✅ **No QR scan needed unless WhatsApp logs out the device**  
✅ **Memory saved by closing idle sockets**  
✅ **Messages still work after idle timeout**  
✅ **~2 second delay on first message after idle**
