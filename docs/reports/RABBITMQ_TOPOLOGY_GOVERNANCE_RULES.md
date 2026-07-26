# 🛡️ RabbitMQ Topology Governance Rules

**Quick Reference Card for Development Team**

---

## ⚠️ HARD RULES (Never Break These)

### Rule 1: No Manual ReceiveEndpoint for Projection Consumers

```csharp
// ❌ NEVER DO THIS
cfg.ReceiveEndpoint("my-queue", e =>
{
    e.ConfigureConsumer<MyProjectionConsumer>(context);
});

// ✅ ALWAYS DO THIS
cfg.ConfigureEndpoints(context);
```

**Why**: Manual overrides bypass automatic binding creation and cause topology drift.

---

### Rule 2: Always Version Queue When Adding/Removing Events

```csharp
// ✅ Adding IConsumer<NewEvent>
EndpointName = "my-queue-v2";  // Increment from v1

// ✅ Removing IConsumer<OldEvent>
EndpointName = "my-queue-v3";  // Increment from v2

// ❌ Changing consumer logic only
EndpointName = "my-queue-v3";  // Keep same version
```

**When to Increment**:
- ✅ Adding `IConsumer<T>` interface
- ✅ Removing `IConsumer<T>` interface
- ✅ Breaking event schema changes
- ❌ Updating consumer logic (non-breaking)

---

### Rule 3: ConsumerDefinition Controls Topology

```csharp
public class MyConsumerDefinition : ConsumerDefinition<MyConsumer>
{
    public MyConsumerDefinition()
    {
        // THIS is the single source of truth
        EndpointName = "my-queue-v2";
    }
}
```

**Never override in Program.cs** — let MassTransit read from `ConsumerDefinition`.

---

### Rule 4: Document Version History

```csharp
/// <summary>
/// MY CONSUMER
/// 
/// Version History:
/// - v1: Initial implementation (EventA, EventB)
/// - v2: Added EventC
/// - v3: Removed EventB, added EventD
/// </summary>
public class MyConsumer : IConsumer<EventA>, IConsumer<EventC>, IConsumer<EventD>
```

---

### Rule 5: Verify Topology in Staging

**Before Production Deployment**:
- [ ] New queue created with correct version
- [ ] All expected bindings exist in RabbitMQ UI
- [ ] Test event delivery for new events
- [ ] Monitor logs for binding confirmation

---

## 🚨 Troubleshooting: Event Not Being Consumed

### Checklist

1. **Check RabbitMQ Management UI**:
   - Does the exchange exist? (e.g., `PointOfSale.Core.Events:MyEvent`)
   - Does the queue exist? (e.g., `my-queue-v2`)
   - **Does the binding exist?** ← Most common issue

2. **Check Consumer Registration**:
   ```csharp
   // In Program.cs
   x.AddConsumer<MyConsumer, MyConsumerDefinition>();
   ```

3. **Check ConfigureEndpoints**:
   ```csharp
   // In Program.cs
   cfg.ConfigureEndpoints(context);  // No exclusions!
   ```

4. **Check for Manual Overrides**:
   ```csharp
   // Search Program.cs for:
   cfg.ReceiveEndpoint(...)  // Should NOT exist for projection consumers
   ```

5. **Check Event Publishing**:
   ```csharp
   // Use Publish(), not Send()
   await _publishEndpoint.Publish(new MyEvent {...});
   ```

---

## ✅ Adding a New Event (Safe Process)

### Step-by-Step

**1. Add IConsumer Interface**:
```csharp
public class MyConsumer :
    IConsumer<EventA>,
    IConsumer<EventB>,
    IConsumer<EventC>  // ← NEW
{
    public async Task Consume(ConsumeContext<EventC> context)
    {
        // Implementation
    }
}
```

**2. Increment Queue Version**:
```csharp
public MyConsumerDefinition()
{
    EndpointName = "my-queue-v2";  // ← Increment from v1
}
```

**3. Update Documentation**:
```csharp
/// Version History:
/// - v1: Initial (EventA, EventB)
/// - v2: Added EventC
```

**4. Deploy and Verify**:
- [ ] Deploy to staging
- [ ] Verify new queue created
- [ ] Verify bindings exist
- [ ] Test event delivery
- [ ] Deploy to production
- [ ] Clean up old queue after 24-48 hours

---

## 📊 Monitoring

### Key Metrics

```
events.published.<EventType>
events.consumed.<EventType>
queue.depth.<QueueName>
consumer.error_rate.<ConsumerName>
```

### Critical Alert

```
IF (events.published > events.consumed + 10) FOR 5 minutes
THEN alert("Events not being consumed - check bindings!")
```

---

## 🎓 Why This Matters

**Without These Rules**:
- ❌ Events published but never consumed
- ❌ Silent failures (no errors, no logs)
- ❌ Data consistency broken (write/read model divergence)
- ❌ Customer-facing bugs (stale data in UI)

**With These Rules**:
- ✅ Automatic binding creation
- ✅ Deterministic event delivery
- ✅ No topology drift
- ✅ Safe event additions
- ✅ Production confidence

---

## 📞 Need Help?

**If events aren't being consumed**:
1. Check RabbitMQ UI for missing bindings
2. Review this document
3. Check `PHASE_CLOSURE_RABBITMQ_TOPOLOGY_FIX.md` for detailed troubleshooting
4. Escalate to architecture team if still stuck

---

**Remember**: Trust MassTransit's automatic binding creation. Manual overrides cause more problems than they solve.

---

*Quick Reference v1.0 | Last Updated: January 27, 2026*
