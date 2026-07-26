# PAYMENT-FULFILLMENT COUPLING HARDENING PLAN
## Hostile Domain-Integrity Audit Results

**Date**: 2026-02-12  
**Auditor**: Principal Distributed Systems Architect  
**Scope**: Fulfillment → Payment Lifecycle Coupling  
**Objective**: Make it IMPOSSIBLE to recognize COD revenue before physical delivery

---

## EXECUTIVE SUMMARY

The system has **PARTIAL** hardening with critical gaps:

✅ **DONE**:
- PaymentLifecycleService with state machine
- FulfillmentDeliveredEvent → Consumer → MarkCodDeliveredAsync
- SaveChanges interceptors for both Payment and Fulfillment
- Ledger-first accounting via outbox
- Architecture tests (incomplete)

❌ **CRITICAL GAPS**:
1. **Webhook handlers bypass PaymentLifecycleService** (direct writes)
2. **Payment services bypass interceptor** (StripePaymentService, PaymentService)
3. **No COD webhook blocking** (COD webhooks should never exist)
4. **CollectCodAsync doesn't exist** (only MarkCodDeliveredAsync)
5. **Architecture tests incomplete** (don't catch service-layer bypasses)
6. **No MongoDB projection validation** (PaymentStatus could be stale)

---

## THREAT MODEL

### Attack Vectors

**1. Malicious Admin**
- Calls `MarkCodDeliveredAsync` before actual delivery
- **Mitigation**: Validate `FulfillmentStatus == Delivered` inside method

**2. Compromised Webhook**
- Fake COD delivery webhook from attacker
- **Mitigation**: Block ALL COD webhooks at gateway

**3. Service Layer Bypass**
- Developer adds code that writes PaymentStatus directly
- **Mitigation**: Strengthen interceptor to block ALL writes except from PaymentLifecycleService

**4. Controller Bypass**
- New controller endpoint that sets PaymentStatus
- **Mitigation**: Architecture tests + code review

**5. Database Direct Write**
- SQL injection or direct DB access
- **Mitigation**: Database-level constraints (out of scope)

---

## HARDENING PHASES

### PHASE 1: CORE INVARIANT ENFORCEMENT (CRITICAL)

**Objective**: Make COD collection physically impossible before delivery

#### 1.1 Harden `MarkCodDeliveredAsync`

**File**: `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`

**Changes**:
```csharp
public async Task<PaymentTransitionResult> MarkCodDeliveredAsync(
    Guid orderId,
    string? deliveryReference = null,
    string? confirmedBy = null,
    CancellationToken ct = default)
{
    using var transaction = await _db.Database.BeginTransactionAsync(ct);
    
    try
    {
        var order = await _db.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct);
        
        if (order == null)
            return PaymentTransitionResult.Failed("Order not found");
        
        // ═══════════════════════════════════════════════════════════════
        // HARD INVARIANT 1: Must be COD order
        // ═══════════════════════════════════════════════════════════════
        if (!IsCodOrder(order))
        {
            _logger.LogCritical(
                "[SECURITY_VIOLATION] Attempted to collect COD for non-COD order | " +
                "OrderId: {OrderId} | PaymentMethod: {PaymentMethod}",
                orderId, order.PaymentMethodCode);
            return PaymentTransitionResult.Failed("Order is not a COD order");
        }
        
        // ═══════════════════════════════════════════════════════════════
        // HARD INVARIANT 2: FulfillmentStatus MUST be Delivered
        // ═══════════════════════════════════════════════════════════════
        if (order.FulfillmentStatus != FulfillmentStatus.Delivered)
        {
            _logger.LogCritical(
                "[SECURITY_VIOLATION] Attempted to collect COD before delivery | " +
                "OrderId: {OrderId} | FulfillmentStatus: {FulfillmentStatus}",
                orderId, order.FulfillmentStatus);
            return PaymentTransitionResult.Failed(
                $"Cannot collect COD: order not delivered (current status: {order.FulfillmentStatus})");
        }
        
        // ═══════════════════════════════════════════════════════════════
        // HARD INVARIANT 3: PaymentStatus MUST be Pending
        // ═══════════════════════════════════════════════════════════════
        if (order.PaymentStatus != PaymentStatus.Pending)
        {
            _logger.LogWarning(
                "[IDEMPOTENCY] COD already collected | OrderId: {OrderId} | PaymentStatus: {PaymentStatus}",
                orderId, order.PaymentStatus);
            return PaymentTransitionResult.Succeeded(order.PaymentStatus);
        }
        
        // ... rest of method unchanged
    }
}
```

**Acceptance Test**:
```csharp
[Fact]
public async Task MarkCodDelivered_WhenNotDelivered_ShouldBlock()
{
    // Arrange: COD order in OutForDelivery status
    var order = CreateCodOrder(FulfillmentStatus.OutForDelivery);
    
    // Act
    var result = await _lifecycleService.MarkCodDeliveredAsync(order.Id);
    
    // Assert
    Assert.False(result.Success);
    Assert.Contains("not delivered", result.ErrorMessage);
    Assert.Equal(PaymentStatus.Pending, order.PaymentStatus); // Unchanged
}
```

#### 1.2 Rename Method for Clarity

**Rename**: `MarkCodDeliveredAsync` → `CollectCodAsync`

**Rationale**: 
- "Collect" is clearer than "MarkDelivered" for payment context
- Matches domain language (COD = Cash On Delivery = Collection event)

**Implementation**: Use `semanticRename` tool

---

### PHASE 2: WEBHOOK HARDENING (CRITICAL)

**Objective**: Block ALL COD webhook attempts

#### 2.1 Add COD Webhook Blocker

**File**: `PointOfSale.API/Services/Payments/PaymentService.cs`

**Method**: `ProcessWebhookAsync`

**Add at start**:
```csharp
public async Task<WebhookResponseDto> ProcessWebhookAsync(
    Guid tenantId,
    string provider,
    string? signature,
    string rawPayload,
    string traceId,
    string? sourceIp,
    string? userAgent,
    CancellationToken cancellationToken)
{
    // ═══════════════════════════════════════════════════════════════
    // SECURITY: Block COD webhooks (COD has no gateway)
    // ═══════════════════════════════════════════════════════════════
    if (provider.Equals("cod", StringComparison.OrdinalIgnoreCase) ||
        provider.Contains("cash", StringComparison.OrdinalIgnoreCase))
    {
        _logger.LogCritical(
            "[SECURITY_VIOLATION] Attempted COD webhook | Provider: {Provider} | " +
            "SourceIP: {SourceIP} | TraceId: {TraceId}",
            provider, sourceIp, traceId);
        
        return new WebhookResponseDto
        {
            Processed = false,
            Message = "COD webhooks are not supported"
        };
    }
    
    // ... rest of method
}
```

**Acceptance Test**:
```csharp
[Fact]
public async Task ProcessWebhook_CodProvider_ShouldBlock()
{
    // Act
    var result = await _paymentService.ProcessWebhookAsync(
        tenantId: _tenantId,
        provider: "cod",
        signature: "fake",
        rawPayload: "{}",
        traceId: "test",
        sourceIp: "1.2.3.4",
        userAgent: "attacker",
        CancellationToken.None);
    
    // Assert
    Assert.False(result.Processed);
    Assert.Contains("not supported", result.Message);
}
```

#### 2.2 Migrate Webhook Handlers to PaymentLifecycleService

**Files to Update**:
- `StripePaymentService.cs` (line 577)
- `PaymentService.cs` (lines 682, 919, 1030)

**Pattern**:
```csharp
// ❌ OLD (BYPASS)
order.PaymentStatus = PaymentStatus.Collected;
await _dbContext.SaveChangesAsync();

// ✅ NEW (HARDENED)
var result = await _paymentLifecycleService.CaptureOnlinePaymentAsync(
    orderId: order.Id,
    gateway: "stripe",
    paymentIntentId: paymentIntent.Id,
    amount: paymentIntent.Amount,
    ct: cancellationToken);

if (!result.Success)
{
    _logger.LogError("Failed to capture payment: {Error}", result.ErrorMessage);
    throw new PaymentProcessingException(result.ErrorMessage);
}
```

**Acceptance Test**:
```csharp
[Fact]
public async Task StripeWebhook_ShouldUsePaymentLifecycleService()
{
    // Arrange: Mock webhook payload
    var payload = CreateStripeWebhookPayload("payment_intent.succeeded");
    
    // Act
    await _stripeService.ProcessWebhookAsync(payload);
    
    // Assert
    _mockLifecycleService.Verify(
        x => x.CaptureOnlinePaymentAsync(
            It.IsAny<Guid>(),
            "stripe",
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
        Times.Once);
}
```

---

### PHASE 3: INTERCEPTOR STRENGTHENING (CRITICAL)

**Objective**: Block ALL PaymentStatus writes except from PaymentLifecycleService

#### 3.1 Strengthen Interceptor Logic

**File**: `PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs`

**Current Issue**: Services bypass by not setting authorized context

**Solution**: Make interceptor stricter

```csharp
private void ValidatePaymentStatusChanges(DbContext? context)
{
    if (context == null) return;
    
    // If we're in an authorized context (PaymentLifecycleService), allow changes
    if (IsAuthorizedContext) return;

    var modifiedOrders = context.ChangeTracker.Entries<Order>()
        .Where(e => e.State == EntityState.Modified)
        .Select(e => e.Entity)
        .Where(o => o.PaymentStatusChanged)
        .ToList();

    if (modifiedOrders.Any())
    {
        var orderIds = string.Join(", ", modifiedOrders.Select(o => o.Id));
        var orderNumbers = string.Join(", ", modifiedOrders.Select(o => o.OrderNumber));
        
        // ═══════════════════════════════════════════════════════════════
        // SECURITY: Get stack trace to identify caller
        // ═══════════════════════════════════════════════════════════════
        var stackTrace = new System.Diagnostics.StackTrace(true);
        var callerInfo = stackTrace.ToString();
        
        _logger.LogCritical(
            "[SECURITY_VIOLATION] Unauthorized PaymentStatus change blocked | " +
            "OrderIds: {OrderIds} | OrderNumbers: {OrderNumbers} | " +
            "Caller: {Caller}",
            orderIds, orderNumbers, callerInfo);

        throw new InvalidOperationException(
            $"PaymentStatus can only be changed via IPaymentLifecycleService. " +
            $"Direct PaymentStatus changes are blocked to enforce ledger-first accounting. " +
            $"Affected Orders: {orderNumbers}. " +
            $"Use IPaymentLifecycleService.CollectCodAsync(), CaptureOnlinePaymentAsync(), or ApproveWalletPaymentAsync() instead.");
    }
}
```

**Acceptance Test**:
```csharp
[Fact]
public async Task DirectPaymentStatusWrite_ShouldThrow()
{
    // Arrange
    var order = await _db.Orders.FirstAsync();
    
    // Act & Assert
    order.PaymentStatus = PaymentStatus.Collected;
    
    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _db.SaveChangesAsync());
    
    Assert.Contains("IPaymentLifecycleService", ex.Message);
}
```

---

### PHASE 4: ARCHITECTURE TESTS (MANDATORY)

**Objective**: Fail build if coupling rules are violated

#### 4.1 Expand PaymentLifecycleTests

**File**: `PointOfSale.Tests/Architecture/PaymentLifecycleTests.cs`

**Add Tests**:

```csharp
[Fact]
public void Services_ShouldNotDirectlySetPaymentStatus()
{
    // Arrange
    var servicesPath = Path.Combine(GetSolutionDirectory(), "PointOfSale.API", "Services");
    var serviceFiles = Directory.GetFiles(servicesPath, "*.cs", SearchOption.AllDirectories);
    
    var violations = new List<string>();
    
    foreach (var file in serviceFiles)
    {
        var sourceCode = File.ReadAllText(file);
        
        // Skip if it's PaymentLifecycleService itself
        if (file.Contains("PaymentLifecycleService.cs"))
            continue;
        
        // Check for direct PaymentStatus writes
        if (Regex.IsMatch(sourceCode, @"order\.PaymentStatus\s*=\s*PaymentStatus\."))
        {
            violations.Add(Path.GetFileName(file));
        }
    }
    
    // Assert
    Assert.Empty(violations);
}

[Fact]
public void OnlyFulfillmentDeliveredConsumer_CanCallCollectCodAsync()
{
    // Arrange
    var solutionDir = GetSolutionDirectory();
    var allCsFiles = Directory.GetFiles(solutionDir, "*.cs", SearchOption.AllDirectories);
    
    var violations = new List<string>();
    
    foreach (var file in allCsFiles)
    {
        var fileName = Path.GetFileName(file);
        
        // Skip the consumer itself and the service definition
        if (fileName == "FulfillmentDeliveredEventConsumer.cs" ||
            fileName == "IPaymentLifecycleService.cs" ||
            fileName == "PaymentLifecycleService.cs")
            continue;
        
        var sourceCode = File.ReadAllText(file);
        
        // Check for calls to CollectCodAsync
        if (sourceCode.Contains("CollectCodAsync"))
        {
            violations.Add(fileName);
        }
    }
    
    // Assert
    Assert.Empty(violations);
}

[Fact]
public void Webhooks_MustNotTouchCodPaymentStatus()
{
    // Arrange
    var webhookFiles = Directory.GetFiles(
        Path.Combine(GetSolutionDirectory(), "PointOfSale.API", "Controllers"),
        "*Webhook*.cs",
        SearchOption.AllDirectories);
    
    var violations = new List<string>();
    
    foreach (var file in webhookFiles)
    {
        var sourceCode = File.ReadAllText(file);
        
        // Check for COD-related logic in webhooks
        if (sourceCode.Contains("COD", StringComparison.OrdinalIgnoreCase) &&
            sourceCode.Contains("PaymentStatus"))
        {
            violations.Add(Path.GetFileName(file));
        }
    }
    
    // Assert
    Assert.Empty(violations);
}
```

---

### PHASE 5: MONGODB PROJECTION HARDENING

**Objective**: Ensure MongoDB OrderDocument reflects ONLY event-sourced PaymentStatus

#### 5.1 Audit OrderProjectionConsumer

**File**: `PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs`

**Verify**:
- PaymentStatus is ONLY updated from `OrderPaymentStatusChangedEvent`
- Never from direct PostgreSQL reads

**Current Implementation**: ✅ CORRECT (event-driven)

#### 5.2 Add Projection Validation Test

```csharp
[Fact]
public async Task OrderProjection_PaymentStatus_ShouldMatchEvents()
{
    // Arrange: Create order and emit events
    var order = await CreateTestOrder();
    await EmitOrderCreatedEvent(order);
    await EmitPaymentStatusChangedEvent(order.Id, PaymentStatus.Collected);
    
    // Wait for projection
    await Task.Delay(500);
    
    // Act: Read from MongoDB
    var mongoOrder = await _mongoCollection
        .Find(x => x.OrderId == order.Id)
        .FirstOrDefaultAsync();
    
    // Assert
    Assert.NotNull(mongoOrder);
    Assert.Equal(PaymentStatus.Collected, mongoOrder.PaymentStatus);
}
```

---

## ACCEPTANCE CRITERIA

### Functional Tests

✅ **COD-1**: COD order + not delivered → attempt collect → blocked  
✅ **COD-2**: COD order + delivered → payment auto-collected  
✅ **COD-3**: Manual API call to set PaymentStatus → blocked  
✅ **COD-4**: Webhook attempt to collect COD → blocked  
✅ **COD-5**: Ledger shows entry only after delivery  

### Architecture Tests

✅ **ARCH-1**: No controller sets PaymentStatus  
✅ **ARCH-2**: No service sets PaymentStatus except PaymentLifecycleService  
✅ **ARCH-3**: No code calls CollectCodAsync except FulfillmentDeliveredConsumer  
✅ **ARCH-4**: No webhook touches COD PaymentStatus  

### Security Tests

✅ **SEC-1**: Interceptor blocks all unauthorized writes  
✅ **SEC-2**: Stack trace logged for violation attempts  
✅ **SEC-3**: COD webhook returns 200 but logs CRITICAL  

---

## IMPLEMENTATION ORDER

1. **Phase 1.1** - Harden MarkCodDeliveredAsync (30 min)
2. **Phase 2.1** - Add COD webhook blocker (15 min)
3. **Phase 3.1** - Strengthen interceptor (20 min)
4. **Phase 4.1** - Add architecture tests (45 min)
5. **Phase 2.2** - Migrate webhook handlers (60 min)
6. **Phase 1.2** - Rename method (10 min)
7. **Phase 5.2** - Add projection tests (20 min)

**Total Estimated Time**: 3.5 hours

---

## ROLLOUT STRATEGY

### Step 1: Deploy Hardening (Non-Breaking)
- Deploy Phase 1.1, 2.1, 3.1, 4.1
- Monitor logs for SECURITY_VIOLATION events
- Fix any legitimate code that triggers violations

### Step 2: Migrate Services (Breaking)
- Deploy Phase 2.2 (webhook handler migration)
- Test all payment flows end-to-end
- Monitor error rates

### Step 3: Rename (Optional)
- Deploy Phase 1.2 (method rename)
- Update all call sites
- Update documentation

---

## SUCCESS METRICS

**Before Hardening**:
- 4 direct PaymentStatus writes in services
- 0 COD webhook blocks
- 2 architecture tests

**After Hardening**:
- 0 direct PaymentStatus writes (all via PaymentLifecycleService)
- 100% COD webhook block rate
- 7 architecture tests
- 100% test coverage for payment lifecycle

**System Properties**:
- ✅ Physically-backed order-to-cash pipeline
- ✅ Ledger-first revenue recognition
- ✅ Abuse-resistant COD collection
- ✅ Event-driven payment state machine
- ✅ Zero-trust controller layer

---

## APPENDIX: THREAT MATRIX

| Threat | Current State | After Hardening |
|--------|---------------|-----------------|
| Admin marks COD collected before delivery | ⚠️ Possible (no validation) | ✅ Blocked (FulfillmentStatus check) |
| Fake COD webhook | ⚠️ Possible (no blocker) | ✅ Blocked (provider check) |
| Service bypasses interceptor | ❌ Happening now | ✅ Blocked (stricter interceptor) |
| Controller sets PaymentStatus | ✅ Blocked (interceptor) | ✅ Blocked (interceptor + tests) |
| MongoDB projection stale | ⚠️ Possible (no validation) | ✅ Validated (projection tests) |

---

**END OF AUDIT REPORT**
