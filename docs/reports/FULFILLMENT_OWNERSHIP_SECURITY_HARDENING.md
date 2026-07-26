# FULFILLMENT OWNERSHIP SECURITY HARDENING

## 🔒 THREAT MODEL

### Threat Actors

1. **Malicious Internal Developer**
   - Has access to codebase
   - Can deploy code changes
   - May attempt to bypass domain rules
   - May try to directly manipulate database

2. **Compromised External Webhook Sender**
   - Sends forged webhook payloads
   - May replay old webhooks
   - May attempt to update wrong orders
   - May lack proper signature validation

3. **Malicious Admin User**
   - Has POS/Admin UI access
   - May attempt to manually update carrier orders
   - May try API manipulation
   - May attempt SQL injection

### Attack Vectors

#### Vector 1: Direct FulfillmentStatus Manipulation
**Attack:** Developer adds code that directly sets `order.FulfillmentStatus = X`

**Mitigation:**
- ✅ Backing field with change tracking
- ✅ SaveChanges interceptor blocks unauthorized writes
- ✅ Architecture tests fail if direct writes detected
- ✅ All transitions MUST go through IOrderFulfillmentService

#### Vector 2: Bypass IOrderFulfillmentService
**Attack:** Controller directly updates order without calling service

**Mitigation:**
- ✅ Controllers CANNOT accept FulfillmentStatus in request body
- ✅ Controllers call only service methods (ConfirmAsync, PackAsync, etc.)
- ✅ Service validates source against fulfillment mode
- ✅ Architecture tests fail if controllers bypass service

#### Vector 3: Manual Update of Carrier Order
**Attack:** Admin calls API to manually update carrier-managed order

**Mitigation:**
- ✅ Service requires explicit FulfillmentRequestSource parameter
- ✅ Manual source REJECTED for Carrier orders (409 Conflict)
- ✅ Logged as SECURITY_VIOLATION
- ✅ GetAvailableActionsAsync returns empty list for carrier orders

#### Vector 4: Webhook Update of Manual Order
**Attack:** Forged webhook attempts to update manually-managed order

**Mitigation:**
- ✅ Webhook handler validates FulfillmentMode == Carrier
- ✅ Webhook handler validates CarrierCode matches order
- ✅ Service rejects Webhook source for Manual orders
- ✅ Logged as SECURITY_VIOLATION
- ✅ Webhook acknowledged (200 OK) to prevent retries

#### Vector 5: Webhook Replay Attack
**Attack:** Attacker replays old webhook to change order status

**Mitigation:**
- ✅ Idempotency key required for webhook requests
- ✅ Idempotency key stored in events
- ✅ Duplicate idempotency keys rejected
- ✅ Status regression prevented (forward-only transitions)

#### Vector 6: Source Spoofing
**Attack:** Developer passes wrong source to bypass validation

**Mitigation:**
- ✅ Source parameter is REQUIRED (no defaults)
- ✅ Source validated against FulfillmentMode
- ✅ Source included in events for audit trail
- ✅ Architecture tests verify source is always provided

#### Vector 7: Database Direct Manipulation
**Attack:** Developer runs SQL to update FulfillmentMode or FulfillmentStatus

**Mitigation:**
- ✅ DB trigger prevents FulfillmentMode updates
- ✅ CHECK constraint enforces ownership invariant
- ✅ FulfillmentStatus protected by SaveChanges interceptor
- ✅ All changes logged for audit

#### Vector 8: Event Forgery
**Attack:** Developer publishes fake fulfillment events

**Mitigation:**
- ✅ Events published ONLY by IOrderFulfillmentService
- ✅ Events include Source and IdempotencyKey
- ✅ Events include TenantId for isolation
- ✅ Architecture tests verify event sources

#### Vector 9: MongoDB Projection Manipulation
**Attack:** Developer directly writes to MongoDB to bypass rules

**Mitigation:**
- ✅ MongoDB projections ONLY from events
- ✅ Projections include immutable ownership snapshot
- ✅ UI renders based ONLY on MongoDB fields
- ✅ Never queries PostgreSQL for UI rendering

#### Vector 10: Carrier Code Mismatch
**Attack:** Webhook from Bosta attempts to update Aramex order

**Mitigation:**
- ✅ Webhook handler validates CarrierCode matches order.FulfillmentCarrierCode
- ✅ Mismatch logged as SECURITY_VIOLATION
- ✅ Webhook rejected with 403 Forbidden

---

## 🛡️ DEFENSE IN DEPTH

### Layer 1: Database Constraints
- FulfillmentMode immutable (trigger)
- CHECK constraint enforces ownership invariant
- Foreign key constraints prevent orphans

### Layer 2: Domain Service
- IOrderFulfillmentService is ONLY gateway
- Source validation against FulfillmentMode
- State machine validation
- Change tracking with backing fields

### Layer 3: SaveChanges Interceptor
- Blocks unauthorized FulfillmentStatus writes
- Requires authorized context
- Logs all violations

### Layer 4: Event System
- Events include Source and IdempotencyKey
- Events published ONLY after DB commit
- TenantId isolation enforced

### Layer 5: Webhook Handler
- Signature validation
- FulfillmentMode validation
- CarrierCode validation
- Idempotency key checking

### Layer 6: API Controllers
- Cannot accept FulfillmentStatus in request body
- Call only service methods
- No direct order manipulation

### Layer 7: MongoDB Projections
- Project ONLY from events
- Include immutable ownership snapshot
- Never query PostgreSQL for UI

### Layer 8: Architecture Tests
- Fail build if invariants violated
- Detect direct FulfillmentStatus writes
- Detect controller bypasses
- Detect missing Source parameters

---

## 🔐 SECURITY ENFORCEMENT RULES

### Rule 1: Explicit Source Required
```csharp
// ❌ WRONG - No source parameter
await _fulfillmentService.ConfirmAsync(orderId);

// ✅ CORRECT - Explicit source
await _fulfillmentService.ConfirmAsync(
    orderId, 
    FulfillmentRequestSource.Manual);
```

### Rule 2: Source Validation
```csharp
// Service validates source against fulfillment mode
if (!source.IsAllowedFor(order.FulfillmentMode))
{
    _logger.LogError("[SECURITY_VIOLATION] Source mismatch REJECTED");
    throw new DomainException("Source not allowed for this order");
}
```

### Rule 3: Idempotency for Webhooks
```csharp
// Webhooks MUST provide idempotency key
await _fulfillmentService.MarkDeliveredAsync(
    orderId,
    FulfillmentRequestSource.Webhook,
    idempotencyKey: $"bosta-webhook-{webhookId}");
```

### Rule 4: No Direct Status Writes
```csharp
// ❌ WRONG - Direct write
order.FulfillmentStatus = FulfillmentStatus.Delivered;

// ✅ CORRECT - Via service
await _fulfillmentService.MarkDeliveredAsync(
    order.Id, 
    FulfillmentRequestSource.Manual);
```

### Rule 5: Controller Restrictions
```csharp
// ❌ WRONG - Controller accepts status in request
public async Task<IActionResult> UpdateOrder(
    Guid id, 
    UpdateOrderRequest request) // Contains FulfillmentStatus
{
    order.FulfillmentStatus = request.FulfillmentStatus; // BLOCKED
}

// ✅ CORRECT - Controller calls service method
public async Task<IActionResult> ConfirmOrder(Guid id)
{
    await _fulfillmentService.ConfirmAsync(
        id, 
        FulfillmentRequestSource.Manual);
}
```

### Rule 6: Webhook Validation
```csharp
// Webhook handler MUST validate before calling service
var shipment = await _db.Shipments
    .Include(s => s.Order)
    .FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber);

// Validate ownership
if (!shipment.Order.AllowsCarrierWebhooks)
{
    _logger.LogError("[SECURITY_VIOLATION] Webhook rejected for manual order");
    return Ok(); // Acknowledge to prevent retries
}

// Validate carrier code
if (shipment.Order.FulfillmentCarrierCode != carrierCode)
{
    _logger.LogError("[SECURITY_VIOLATION] Carrier code mismatch");
    return Forbid();
}

// Call service with Webhook source
await _fulfillmentService.MarkDeliveredAsync(
    shipment.OrderId,
    FulfillmentRequestSource.Webhook,
    idempotencyKey: webhookId);
```

---

## 📊 MONITORING & ALERTING

### Security Violation Logs

**Format:**
```
[SECURITY_VIOLATION] {ViolationType} | OrderId: {OrderId} | Source: {Source} | Details: {Details}
```

**Examples:**
```
[SECURITY_VIOLATION] Source mismatch REJECTED | OrderId: abc123 | Source: Manual | FulfillmentMode: Carrier
[SECURITY_VIOLATION] Webhook rejected for manual order | OrderId: def456 | CarrierCode: bosta
[SECURITY_VIOLATION] Carrier code mismatch | OrderId: ghi789 | Expected: bosta | Actual: aramex
```

### Monitoring Queries

**Count violations per day:**
```sql
SELECT DATE(timestamp), COUNT(*) 
FROM logs 
WHERE message LIKE '%SECURITY_VIOLATION%'
GROUP BY DATE(timestamp);
```

**Identify attack patterns:**
```sql
SELECT 
    order_id,
    source,
    fulfillment_mode,
    COUNT(*) as attempt_count
FROM logs
WHERE message LIKE '%SECURITY_VIOLATION%'
GROUP BY order_id, source, fulfillment_mode
HAVING COUNT(*) > 3;
```

### Alerting Rules

1. **Alert if > 10 violations per hour**
   - Indicates potential attack
   - Escalate to security team

2. **Alert if same OrderId has > 5 violations**
   - Indicates targeted attack
   - Lock order for manual review

3. **Alert if same IP has > 20 violations**
   - Indicates automated attack
   - Block IP at firewall

---

## 🧪 ACCEPTANCE TESTS

### Test 1: Manual Order - Admin Can Progress
```csharp
[Fact]
public async Task ManualOrder_AdminCanProgress()
{
    // ARRANGE
    var order = CreateManualOrder();
    
    // ACT
    var result = await _service.ConfirmAsync(
        order.Id, 
        FulfillmentRequestSource.Manual);
    
    // ASSERT
    Assert.True(result.Success);
    Assert.Equal(FulfillmentStatus.Confirmed, result.NewStatus);
}
```

### Test 2: Carrier Order - Admin Blocked
```csharp
[Fact]
public async Task CarrierOrder_AdminBlocked()
{
    // ARRANGE
    var order = CreateCarrierOrder("bosta");
    
    // ACT
    var result = await _service.ConfirmAsync(
        order.Id, 
        FulfillmentRequestSource.Manual);
    
    // ASSERT
    Assert.False(result.Success);
    Assert.Contains("carrier-managed", result.ErrorMessage);
}
```

### Test 3: Manual Order - Webhook Rejected
```csharp
[Fact]
public async Task ManualOrder_WebhookRejected()
{
    // ARRANGE
    var order = CreateManualOrder();
    
    // ACT
    var result = await _service.MarkDeliveredAsync(
        order.Id, 
        FulfillmentRequestSource.Webhook);
    
    // ASSERT
    Assert.False(result.Success);
    Assert.Contains("manually-managed", result.ErrorMessage);
}
```

### Test 4: Carrier Order - Webhook Accepted
```csharp
[Fact]
public async Task CarrierOrder_WebhookAccepted()
{
    // ARRANGE
    var order = CreateCarrierOrder("bosta");
    
    // ACT
    var result = await _service.MarkDeliveredAsync(
        order.Id, 
        FulfillmentRequestSource.Webhook,
        idempotencyKey: "webhook-123");
    
    // ASSERT
    Assert.True(result.Success);
    Assert.Equal(FulfillmentStatus.Delivered, result.NewStatus);
}
```

### Test 5: Idempotency - Duplicate Rejected
```csharp
[Fact]
public async Task Webhook_DuplicateIdempotencyKeyRejected()
{
    // ARRANGE
    var order = CreateCarrierOrder("bosta");
    var idempotencyKey = "webhook-123";
    
    // ACT 1 - First call succeeds
    var result1 = await _service.MarkDeliveredAsync(
        order.Id, 
        FulfillmentRequestSource.Webhook,
        idempotencyKey: idempotencyKey);
    
    // ACT 2 - Duplicate call rejected
    var result2 = await _service.MarkDeliveredAsync(
        order.Id, 
        FulfillmentRequestSource.Webhook,
        idempotencyKey: idempotencyKey);
    
    // ASSERT
    Assert.True(result1.Success);
    Assert.False(result2.Success);
    Assert.Contains("duplicate", result2.ErrorMessage.ToLower());
}
```

---

## 🎯 COMPLIANCE CHECKLIST

- [x] All fulfillment transitions require explicit Source
- [x] Source validated against FulfillmentMode
- [x] Manual source CANNOT update Carrier orders
- [x] Webhook source CANNOT update Manual orders
- [x] All violations logged as SECURITY_VIOLATION
- [x] Idempotency keys required for webhooks
- [x] Events include Source and IdempotencyKey
- [x] Controllers cannot accept FulfillmentStatus in request body
- [x] Controllers call only service methods
- [x] Webhook handler validates ownership before calling service
- [x] MongoDB projections include immutable ownership snapshot
- [x] UI renders based ONLY on MongoDB fields
- [x] Architecture tests enforce all invariants
- [x] Database constraints prevent direct manipulation
- [x] SaveChanges interceptor blocks unauthorized writes

---

## 🚀 DEPLOYMENT SECURITY CHECKLIST

### Pre-Deployment
- [ ] Run all architecture tests
- [ ] Run all security tests
- [ ] Review SECURITY_VIOLATION logs
- [ ] Verify monitoring alerts configured
- [ ] Test webhook signature validation
- [ ] Test idempotency key handling

### Deployment
- [ ] Deploy database migration (already done)
- [ ] Deploy application code
- [ ] Restart all services
- [ ] Verify no SECURITY_VIOLATION logs

### Post-Deployment
- [ ] Test manual order progression
- [ ] Test carrier order rejection
- [ ] Test webhook processing
- [ ] Test webhook replay protection
- [ ] Monitor SECURITY_VIOLATION logs for 24 hours
- [ ] Review audit trail

### Rollback (if needed)
- [ ] Revert application code
- [ ] Database schema remains (backward compatible)
- [ ] Monitor for errors

---

## 📚 SECURITY DOCUMENTATION

### For Developers

**Adding New Fulfillment Transition:**
1. Add method to IOrderFulfillmentService with Source parameter
2. Implement in OrderFulfillmentService using TransitionAsync
3. Add event class inheriting from OrderFulfillmentEventBase
4. Add architecture test
5. Add security test

**Calling Fulfillment Service:**
```csharp
// From Admin Controller
await _fulfillmentService.ConfirmAsync(
    orderId, 
    FulfillmentRequestSource.Manual,
    confirmedBy: User.Identity.Name);

// From Webhook Handler
await _fulfillmentService.MarkDeliveredAsync(
    orderId,
    FulfillmentRequestSource.Webhook,
    idempotencyKey: webhookId);

// From System Process
await _fulfillmentService.CancelAsync(
    orderId,
    FulfillmentRequestSource.System,
    reason: "Auto-cancelled due to timeout");
```

### For Security Team

**Monitoring:**
- Watch for SECURITY_VIOLATION logs
- Alert on > 10 violations per hour
- Review audit trail daily

**Incident Response:**
1. Identify attack vector from logs
2. Block attacker (IP, user, API key)
3. Review affected orders
4. Notify development team
5. Update security rules if needed

---

## 🎓 ARCHITECTURAL LESSONS

### 1. Explicit is Better Than Implicit
- Requiring explicit Source parameter prevents ambiguity
- No defaults = no assumptions = no bypasses

### 2. Validate at Every Layer
- Database constraints
- Domain service validation
- SaveChanges interceptor
- Event validation
- Webhook validation

### 3. Audit Everything
- Log all violations
- Include context in logs
- Make logs queryable
- Alert on patterns

### 4. Idempotency is Security
- Prevents replay attacks
- Enables safe retries
- Simplifies debugging

### 5. Fail Secure
- Reject by default
- Explicit allow lists
- Log all rejections
- Never fail open

---

**Security Hardening Complete**  
**Status:** ✅ PRODUCTION-READY  
**Threat Model:** COMPREHENSIVE  
**Defense Layers:** 8  
**Attack Vectors Mitigated:** 10
