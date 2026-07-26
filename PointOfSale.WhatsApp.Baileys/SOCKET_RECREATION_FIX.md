# Socket Recreation on Restore - Implementation Summary

## Problem

When the Baileys WhatsApp service restarts, sessions stored in Redis show as "connected" but the actual WebSocket connections are `null`. This causes message sending to fail with:

```
WARN: PILLAR 4: Socket safety check failed
socketId: null
status: "connected"
error: "PILLAR 4: Socket is null"
```

## Root Cause

**State Desynchronization**: When restoring a session from Redis, we load the session metadata (including status="connected") but we don't recreate the actual Baileys socket connection. The socket object cannot be serialized to Redis, so it's lost on service restart.

## Solution

Implemented **automatic socket recreation** when restoring a connected session from Redis.

### Implementation Details

#### 1. Modified `getOrCreateSession` Method

Added logic to detect when a restored session was previously "connected" and trigger socket recreation:

```typescript
if (metadata.status === ConnectionStatus.CONNECTED) {
  logger.info('Restored session from Redis - recreating socket for connected session');
  
  // Recreate socket asynchronously (don't block the restore)
  const restoredSession = session;
  this.recreateSocketForRestoredSession(tenantId, restoredSession).catch((error) => {
    // Handle failure gracefully - mark as disconnected
    restoredSession.status = ConnectionStatus.DISCONNECTED;
    restoredSession.lastError = `Socket recreation failed: ${error.message}`;
  });
}
```

#### 2. New Method: `recreateSocketForRestoredSession`

Created a dedicated method to recreate the socket from persisted auth state:

**Key Steps**:
1. Load auth state from Redis (authoritative source)
2. Restore auth state to disk (Baileys multi-file format)
3. Create new Baileys socket with restored auth
4. Attach event handlers (connection.update, creds.update)
5. Update session metadata with new socket ID and version

**Safety Features**:
- Socket ownership tracking (socketVersion)
- Auth state hash tracking for debugging
- Graceful failure handling (marks session as disconnected)
- Non-blocking execution (doesn't block session restore)

## Benefits

✅ **Seamless Service Restarts**: Sessions automatically reconnect after service restart  
✅ **No Manual Intervention**: No need to disconnect/reconnect from Admin UI  
✅ **Maintains Session Continuity**: Users don't experience disruption  
✅ **Graceful Degradation**: If socket recreation fails, session is marked as disconnected  
✅ **Audit Trail**: All socket recreation attempts are logged with correlation IDs  

## Testing

### Before Fix
```bash
# Service restart
npm run dev

# Try to send message
curl -X POST http://localhost:3000/messages/send \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"xxx","phone":"+xxx","text":"test"}'

# Result: FAIL - Socket is null
```

### After Fix
```bash
# Service restart
npm run dev
# Logs show: "Recreating socket for restored connected session"

# Try to send message
curl -X POST http://localhost:3000/messages/send \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"xxx","phone":"+xxx","text":"test"}'

# Result: SUCCESS - Message sent
```

## Logs to Monitor

**Successful Socket Recreation**:
```
INFO: Restored session from Redis - recreating socket for connected session
INFO: Found auth state in Redis
INFO: Auth state restored from Redis
INFO: Socket recreated successfully - waiting for connection
INFO: Session connected successfully
```

**Failed Socket Recreation**:
```
ERROR: Failed to recreate socket for restored session
INFO: Session status transition from: connected to: disconnected
```

## Edge Cases Handled

1. **No Auth State in Redis**: Throws error, session marked as disconnected
2. **Corrupted Auth State**: Throws error, session marked as disconnected
3. **Socket Connection Fails**: Handled by existing connection.update logic
4. **Multiple Restore Attempts**: Socket version prevents race conditions
5. **Service Restart During Connection**: Existing stale session cleanup handles this

## Architecture

```
Service Restart
     ↓
Load Session from Redis (status="connected", socket=null)
     ↓
Detect Connected Status
     ↓
Trigger Socket Recreation (async)
     ↓
Load Auth State from Redis
     ↓
Create Baileys Socket
     ↓
Attach Event Handlers
     ↓
Wait for Connection
     ↓
Session Fully Restored ✓
```

## Files Modified

- `PointOfSale.WhatsApp.Baileys/src/services/SessionManager.ts`
  - Modified `getOrCreateSession` method (lines ~290-320)
  - Added `recreateSocketForRestoredSession` method (lines ~640-750)

## Deployment

1. **Build**:
```bash
cd PointOfSale.WhatsApp.Baileys
npm run build
```

2. **Restart Service**:
```bash
npm run dev  # Development
# OR
pm2 restart baileys-whatsapp  # Production
```

3. **Verify**:
- Check logs for "Recreating socket for restored connected session"
- Test message sending to previously connected sessions
- Monitor metrics for socket recreation success/failure

## Rollback Plan

If issues occur, revert the changes:

```bash
git revert <commit-hash>
npm run build
pm2 restart baileys-whatsapp
```

Sessions will behave as before (require manual reconnect after service restart).

## Future Enhancements

- Add metrics for socket recreation success/failure rate
- Add admin endpoint to trigger manual socket recreation
- Add configurable timeout for socket recreation attempts
- Add retry logic for failed socket recreations

## Related Issues

- Fixes: "Socket is null" error after service restart
- Improves: Service restart resilience
- Maintains: Session continuity across restarts
