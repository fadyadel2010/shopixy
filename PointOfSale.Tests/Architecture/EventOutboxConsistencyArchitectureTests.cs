using System.Text.RegularExpressions;
using PointOfSale.Core.Events;

namespace PointOfSale.Tests.Architecture;

public sealed class EventOutboxConsistencyArchitectureTests
{
    [Fact]
    public void MassTransitEventPublisher_MustUseOutboxForNormalEvents_AndDirectBusOnlyForSensitiveEvents()
    {
        var source = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));

        Assert.Contains("IPublishEndpoint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IBusControl", source, StringComparison.Ordinal);
        Assert.Contains("PublishSensitiveAsync", source, StringComparison.Ordinal);
        Assert.Contains("_bus.Publish(", source, StringComparison.Ordinal);
        Assert.Contains("_dbContext.SaveChangesAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalPublishBoundary", source, StringComparison.Ordinal);
        Assert.Contains("IsAuthoritativeEventType", source, StringComparison.Ordinal);

        var sensitivePublishIndex = source.IndexOf("PublishSensitiveAsync", StringComparison.Ordinal);
        var rawBusPublishIndex = source.IndexOf("_bus.Publish(", StringComparison.Ordinal);

        Assert.True(sensitivePublishIndex >= 0, "PublishSensitiveAsync should exist.");
        Assert.True(rawBusPublishIndex > sensitivePublishIndex, "Direct bus publish must be isolated to PublishSensitiveAsync.");
    }

    [Fact]
    public void Program_MustEnableEntityFrameworkBusOutbox()
    {
        var source = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("AddEntityFrameworkOutbox<ApplicationDbContext>", source, StringComparison.Ordinal);
        Assert.Contains("UseBusOutbox();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MassTransitBusOutbox_MustBeReadinessGatedForAuthoritativeEvents()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var healthChecks = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));
        var observability = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));

        Assert.Contains("MassTransitOutboxMessage = MassTransit.EntityFrameworkCoreIntegration.OutboxMessage", gate, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Set<MassTransitOutboxMessage>()", gate, StringComparison.Ordinal);
        Assert.Contains("message.SentTime < staleCutoffUtc", gate, StringComparison.Ordinal);
        Assert.Contains("message.SequenceNumber > state.LastSequenceNumber", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("state.Delivered == null", gate, StringComparison.Ordinal);
        Assert.Contains("EventMayAffectTenant(null, message.Body, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("busMessagesInspected", gate, StringComparison.Ordinal);
        Assert.Contains("LogLargeTenantScopeInspection(", gate, StringComparison.Ordinal);
        
        // Verify that tenant-scoped MassTransit checks keep the normal error code; ambiguous
        // marketplace aggregate scope has its own global fail-closed path.
        var busOutboxErrorCodeIndex = gate.IndexOf("MASS_TRANSIT_BUS_OUTBOX_NOT_READY", StringComparison.Ordinal);
        Assert.True(busOutboxErrorCodeIndex >= 0, "Expected error code MASS_TRANSIT_BUS_OUTBOX_NOT_READY should exist.");
        Assert.Contains("MASS_TRANSIT_BUS_OUTBOX_SCOPE_UNDETERMINED", gate, StringComparison.Ordinal);
        
        Assert.DoesNotContain(".Take(maxMessagesToCheck)", gate, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.ContainsTenant(baseTenantId, eventPayload, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.Resolve(baseTenantId, eventPayload).Count == 0", gate, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidEventPayloadException)", gate, StringComparison.Ordinal);
        
        // Ensure "return true;" is within the InvalidEventPayloadException catch block
        var catchIndex = gate.IndexOf("catch (InvalidEventPayloadException)", StringComparison.Ordinal);
        Assert.True(catchIndex >= 0, "catch (InvalidEventPayloadException) should exist.");
        
        // Extract the catch block by finding the opening brace and matching closing brace
        var catchBlockStart = gate.IndexOf('{', catchIndex);
        Assert.True(catchBlockStart >= 0, "catch block should have an opening brace.");
        
        var braceCount = 1;
        var catchBlockEnd = catchBlockStart + 1;
        while (catchBlockEnd < gate.Length && braceCount > 0)
        {
            if (gate[catchBlockEnd] == '{') braceCount++;
            else if (gate[catchBlockEnd] == '}') braceCount--;
            catchBlockEnd++;
        }
        
        var catchBlock = gate.Substring(catchBlockStart, catchBlockEnd - catchBlockStart);
        Assert.Contains("return true;", catchBlock, StringComparison.Ordinal);
        
        Assert.Contains("BuildCriticalMassTransitOutboxMessageTypeFilter", gate, StringComparison.Ordinal);
        Assert.Contains("MASS_TRANSIT_BUS_OUTBOX_NOT_READY", gate, StringComparison.Ordinal);

        Assert.Contains("MassTransitBusOutboxReadinessHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("BuildCriticalMassTransitOutboxMessageTypeFilter", healthChecks, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.CriticalEventNameFragments", healthChecks, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.NonAuthoritativeEventTypeNames", healthChecks, StringComparison.Ordinal);
        Assert.Contains("AddCheck<MassTransitBusOutboxReadinessHealthCheck>(\"mass_transit_bus_outbox_readiness\"", observability, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoritativeEventClassification_MustBeSharedAcrossPublishersAndLegacyOutbox()
    {
        var authority = File.ReadAllText(CorePath("Events", "IntegrationEventAuthority.cs"));
        var massTransitPublisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));
        var inMemoryPublisher = File.ReadAllText(InfrastructurePath("EventBus", "InMemoryEventPublisher.cs"));
        var legacyOutbox = File.ReadAllText(InfrastructurePath("Jobs", "OutboxPublisherJob.cs"));
        var tenantConstraint = ReadInfrastructureMigrationEvidence("20260429133000_S52_DataAuthorityBoundaryHardening.sql");
        var aggregateConstraint = ReadInfrastructureMigrationEvidence("20260429143000_S54_EventOrderingHardening.sql");
        var versionConstraint = ReadInfrastructureMigrationEvidence("20260429153000_S56_DataAuthorityAndEventOrderingClosure.sql");

        foreach (var fragment in new[]
        {
            "Product",
            "Variant",
            "Coupon",
            "Promotion",
            "Tenant",
            "StoreConfig",
            "ShippingMethod",
            "Customer",
            "Checkout",
            "Ledger",
            "Accounting",
            "Wallet",
            "Cod",
            "Currency",
            "ProviderTrust"
        })
        {
            Assert.Contains($"\"{fragment}\"", authority, StringComparison.Ordinal);
            Assert.Contains($"%{fragment.ToLowerInvariant()}%", tenantConstraint, StringComparison.Ordinal);
            Assert.Contains($"%{fragment.ToLowerInvariant()}%", aggregateConstraint, StringComparison.Ordinal);
            Assert.Contains($"%{fragment.ToLowerInvariant()}%", versionConstraint, StringComparison.Ordinal);
        }

        Assert.Contains("IntegrationEventAuthority.IsAuthoritativeEventType(eventType)", massTransitPublisher, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.IsAuthoritativeEventType(eventType)", inMemoryPublisher, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.IsCriticalEventTypeName(eventTypeName)", legacyOutbox, StringComparison.Ordinal);
        Assert.Contains("INonAuthoritativeIntegrationEvent", authority, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_OutboxMessages_CriticalEvents_TenantId_Required\"", tenantConstraint, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_OutboxMessages_CriticalEvents_AggregateId_Required\"", aggregateConstraint, StringComparison.Ordinal);
        Assert.DoesNotContain("var authoritativeSubstrings = new[]", inMemoryPublisher, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationAndRequestEvents_MustNotBeClassifiedAsBusinessTruth()
    {
        var authority = File.ReadAllText(CorePath("Events", "IntegrationEventAuthority.cs"));
        var integrationEvent = File.ReadAllText(CorePath("Events", "IntegrationEvent.cs"));
        var outboxConfiguration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "OutboxMessageConfiguration.cs"));
        var s62 = ReadInfrastructureMigrationEvidence("20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure.sql");

        Assert.Contains("public interface INonAuthoritativeIntegrationEvent", integrationEvent, StringComparison.Ordinal);
        Assert.Contains("!typeof(INonAuthoritativeIntegrationEvent).IsAssignableFrom(eventType)", authority, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.NonAuthoritativeEventTypeNames", outboxConfiguration, StringComparison.Ordinal);

        foreach (var eventTypeName in new[]
        {
            "LowStockAlertEvent",
            "OnlineCustomerEmailVerificationRequestedEvent",
            "WhatsAppOrderConfirmationRequestedEvent",
            "WhatsAppPaymentApprovalRequestedEvent",
            "WhatsAppPaymentApprovedNotificationEvent",
            "WhatsAppPaymentRejectedNotificationEvent"
        })
        {
            Assert.Contains($"\"{eventTypeName}\"", authority, StringComparison.Ordinal);
            Assert.Contains($"{eventTypeName} : IntegrationEvent, INonAuthoritativeIntegrationEvent", ReadEventSource(eventTypeName), StringComparison.Ordinal);
            Assert.Contains($"%{eventTypeName.ToLowerInvariant()}%", s62, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AuthoritativeEventAggregateResolver_MustCoverAllBoundaryOwners()
    {
        var authority = File.ReadAllText(CorePath("Events", "IntegrationEventAuthority.cs"));
        var massTransitPublisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));
        var inMemoryPublisher = File.ReadAllText(InfrastructurePath("EventBus", "InMemoryEventPublisher.cs"));

        foreach (var propertyName in new[]
        {
            "MarketplaceOrderId",
            "SubOrderId",
            "MarketplacePaymentId",
            "CommerceHubPaymentId",
            "ListingId",
            "OrderId",
            "PaymentId",
            "ProductId",
            "VariantId",
            "CategoryId",
            "CouponId",
            "PromotionId",
            "ShippingMethodId",
            "CustomerId",
            "OnlineCustomerId",
            "AffiliateAccountId",
            "CheckoutSessionId",
            "StoreConfigId",
            "PageId",
            "SettlementBatchId",
            "TenantId",
            "StoreTenantId",
            "ProviderId",
            "ProviderProfileId",
            "EntityId",
            "BusinessEntityId"
        })
        {
            Assert.Contains($"\"{propertyName}\"", authority, StringComparison.Ordinal);
        }

        Assert.Contains("IAggregateScopedIntegrationEvent", authority, StringComparison.Ordinal);
        Assert.Contains("GetAuthoritativeAggregateId", authority, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthoritativeEventContract", authority, StringComparison.Ordinal);
        Assert.Contains("HasAuthoritativeAggregateIdContract", authority, StringComparison.Ordinal);
        Assert.Contains("HasAffectedTenantScopeShape", authority, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthoritativeEventContract(eventType)", massTransitPublisher, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthoritativeEventContract(@event.GetType())", inMemoryPublisher, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.TryGetAggregateId(@event)", massTransitPublisher, StringComparison.Ordinal);
    }

    [Fact]
    public void AllAuthoritativeIntegrationEvents_MustSatisfyRuntimeContract()
    {
        var violations = typeof(IntegrationEvent).Assembly
            .GetTypes()
            .Where(type => typeof(IntegrationEvent).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type =>
            {
                try
                {
                    IntegrationEventAuthority.ValidateAuthoritativeEventContract(type);
                    return null;
                }
                catch (InvalidOperationException ex)
                {
                    return $"{type.FullName}: {ex.Message}";
                }
            })
            .Where(message => message != null)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Authoritative integration events must expose aggregate ordering and affected-tenant contracts. Violations: " +
            string.Join("; ", violations));
    }

    [Fact]
    public void CommerceHubStatusEvents_MustUseMarketplaceOrderAsTheAuthoritativeAggregate()
    {
        var events = File.ReadAllText(CorePath("Events", "CommerceHubEvents.cs"));

        Assert.Contains("CommerceHubMarketplaceOrderCreatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrderStatusUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSubOrderStatusUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Matches(
            @"CommerceHubSubOrderStatusUpdatedEvent[\s\S]+GetAuthoritativeAggregateId\(\)\s*=>\s*MarketplaceOrderId",
            events);
    }

    [Fact]
    public void CommerceHubStatusEvents_MustBindIdempotencyToAggregateEventVersion()
    {
        var metadata = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderEventMetadata.cs"));
        var orderManagement = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderManagementService.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));

        Assert.Contains("ResolveAggregateEventVersion", metadata, StringComparison.Ordinal);
        Assert.Contains("ProductEventVersionHelper.ToProductEventVersion(maxVersion)", metadata, StringComparison.Ordinal);
        Assert.Contains(":v{eventVersion}:", metadata, StringComparison.Ordinal);
        Assert.Contains("BuildMarketplaceStatusIdempotencyKey", metadata, StringComparison.Ordinal);
        Assert.Contains("BuildSubOrderStatusIdempotencyKey", metadata, StringComparison.Ordinal);
        Assert.Contains("BuildMarketplacePaymentIdempotencyKey", metadata, StringComparison.Ordinal);
        Assert.Contains("BuildSubOrderPaymentIdempotencyKey", metadata, StringComparison.Ordinal);

        foreach (var source in new[] { orderManagement, paymentService, refundService, refundExecution })
        {
            Assert.Contains("CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder)", source, StringComparison.Ordinal);
            Assert.Contains("EventVersion = eventVersion", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IdempotencyKey = $\"{marketplaceOrder.Id:N}:{marketplaceOrder.Status}", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IdempotencyKey = $\"{subOrder.Id:N}:{subOrder.Status}", source, StringComparison.Ordinal);
        }

        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildMarketplaceStatusIdempotencyKey(",
            orderManagement,
            StringComparison.Ordinal);
        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildSubOrderStatusIdempotencyKey(",
            orderManagement,
            StringComparison.Ordinal);

        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildMarketplacePaymentIdempotencyKey(",
            paymentService,
            StringComparison.Ordinal);
        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildSubOrderPaymentIdempotencyKey(",
            paymentService,
            StringComparison.Ordinal);

        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildMarketplaceStatusIdempotencyKey(",
            refundService,
            StringComparison.Ordinal);
        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildSubOrderStatusIdempotencyKey(",
            refundService,
            StringComparison.Ordinal);

        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildMarketplaceStatusIdempotencyKey(",
            refundExecution,
            StringComparison.Ordinal);
        Assert.Contains(
            "CommerceHubOrderEventMetadata.BuildSubOrderStatusIdempotencyKey(",
            refundExecution,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AffectedTenantScopedAuthoritativeEvents_MustBeRuntimeValidated()
    {
        var events = File.ReadAllText(CorePath("Events", "CommerceHubEvents.cs"));
        var massTransitPublisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));
        var inMemoryPublisher = File.ReadAllText(InfrastructurePath("EventBus", "InMemoryEventPublisher.cs"));

        Assert.Contains("IAffectedTenantScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyCollection<Guid> GetAffectedTenantIds()", events, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrderCreatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrderStatusUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSubOrderStatusUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent", events, StringComparison.Ordinal);
        Assert.Contains("GetAffectedTenantIds() => AffectedStoreIds", events, StringComparison.Ordinal);
        Assert.Contains("StoreId == Guid.Empty ? Array.Empty<Guid>() : new[] { StoreId }", events, StringComparison.Ordinal);

        foreach (var publisher in new[] { massTransitPublisher, inMemoryPublisher })
        {
            Assert.Contains("EnsureAffectedTenantScope", publisher, StringComparison.Ordinal);
            Assert.Contains("affectedTenantEvent.GetAffectedTenantIds()", publisher, StringComparison.Ordinal);
            Assert.Contains("affectedTenantIds.Count == 0", publisher, StringComparison.Ordinal);
            Assert.Contains("affectedTenantIds.Contains(@event.TenantId.Value)", publisher, StringComparison.Ordinal);
            Assert.Contains("TenantId must be one of the affected tenant ids", publisher, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AuthoritativePublisher_MustRejectCallerSuppliedVersionMismatch()
    {
        var publisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));

        Assert.DoesNotContain("|| @event.EventVersion > 1", publisher, StringComparison.Ordinal);
        Assert.Contains("ApplyResolvedAuthoritativeVersion", publisher, StringComparison.Ordinal);
        Assert.Contains("@event.EventVersion != resolvedVersion", publisher, StringComparison.Ordinal);
        Assert.Contains("ResolvedVersion={resolvedVersion}", publisher, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoritativeEvents_MustProveAggregateOrderingKey()
    {
        var massTransitPublisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));
        var inMemoryPublisher = File.ReadAllText(InfrastructurePath("EventBus", "InMemoryEventPublisher.cs"));
        var legacyOutbox = File.ReadAllText(InfrastructurePath("Jobs", "OutboxPublisherJob.cs"));

        Assert.Contains("must expose a non-empty aggregate ordering key", massTransitPublisher, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.TryGetAggregateId(@event)", massTransitPublisher, StringComparison.Ordinal);
        Assert.Contains("requires a non-empty aggregate ordering key", inMemoryPublisher, StringComparison.Ordinal);
        Assert.Contains("payloadAggregateId = IntegrationEventAuthority.TryGetAggregateId(@event)", legacyOutbox, StringComparison.Ordinal);
        Assert.Contains("OutboxAggregateId={message.AggregateId}; PayloadAggregateId={payloadAggregateId}", legacyOutbox, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionExpiryAndDiscountProjectionRepair_MustNotBypassAuthoritativeOutboxBoundary()
    {
        var expirationService = File.ReadAllText(ApiPath("Services", "Background", "PromotionExpirationService.cs"));
        var couponsController = File.ReadAllText(ApiPath("Controllers", "CouponsController.cs"));
        var promotionsController = File.ReadAllText(ApiPath("Controllers", "PromotionsController.cs"));

        var deactivateIndex = expirationService.IndexOf("promotion.IsActive = false;", StringComparison.Ordinal);
        var publishIndex = expirationService.IndexOf("await eventPublisher.PublishAsync(expiredEvent, stoppingToken);", StringComparison.Ordinal);

        Assert.True(deactivateIndex >= 0, "PromotionExpirationService must mark the SQL row inactive before publishing.");
        Assert.True(publishIndex > deactivateIndex, "PromotionExpirationService must stage the SQL mutation before publishing the authoritative expiry event.");
        Assert.Contains("dbContext.Entry(promotion).State = EntityState.Unchanged;", expirationService, StringComparison.Ordinal);

        Assert.Contains("RebuildCouponsAsync", couponsController, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishCouponCreatedEventInIsolatedScopeAsync", couponsController, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsyncScope()", couponsController, StringComparison.Ordinal);

        Assert.Contains("RebuildPromotionsAsync", promotionsController, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishPromotionCreatedEventInIsolatedScopeAsync", promotionsController, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsyncScope()", promotionsController, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualPaymentIntentOutbox_MustUseOrderMutationVersion()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var emitMethod = ExtractMethodBody(checkout, "EmitPaymentIntentCreatedEventAsync");

        Assert.NotNull(emitMethod);
        Assert.Contains("int eventVersion", emitMethod!, StringComparison.Ordinal);
        Assert.Contains("EventVersion = eventVersion", emitMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("EventVersion = 1 // Required for OutboxMessages constraint", emitMethod!, StringComparison.Ordinal);
        Assert.Contains("ProductEventVersionHelper.ToProductEventVersion(order.ProjectionMutationVersion + 1)", checkout, StringComparison.Ordinal);
        Assert.Contains("ProductEventVersionHelper.ToProductEventVersion(Math.Max(1L, order.ProjectionMutationVersion))", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountingOutbox_MustOrderFinancialLifecycleByAggregateVersion()
    {
        var entity = File.ReadAllText(CorePath("Entities", "Accounting", "AccountingOutboxMessage.cs"));
        var service = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxService.cs"));
        var processor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "AccountingOutboxMessageConfiguration.cs"));
        var migration = ReadInfrastructureMigrationEvidence("20260424183000_S35_EventOrderingBoundaryHardening.sql");
        var s56 = ReadInfrastructureMigrationEvidence("20260429153000_S56_DataAuthorityAndEventOrderingClosure.sql");

        Assert.Contains("AggregateVersion", entity, StringComparison.Ordinal);
        Assert.Contains("TryExtractAggregateVersion", service, StringComparison.Ordinal);
        Assert.Contains("ResolveRequiredOrderAggregateVersionAsync", service, StringComparison.Ordinal);
        Assert.Contains("MutationVersion = aggregateVersion", service, StringComparison.Ordinal);
        Assert.Contains("HasUnprocessedEarlierAggregateVersionAsync", processor, StringComparison.Ordinal);
        Assert.Contains("ThenBy(m => m.AggregateVersion ?? int.MaxValue)", processor, StringComparison.Ordinal);
        Assert.Contains("candidate.AggregateVersion.Value == message.AggregateVersion.Value", processor, StringComparison.Ordinal);
        Assert.Contains("!candidate.AggregateVersion.HasValue", processor, StringComparison.Ordinal);
        Assert.Contains("candidate.Id.CompareTo(message.Id) < 0", processor, StringComparison.Ordinal);
        Assert.Contains("IX_AccountingOutbox_AggregateVersionOrdering", configuration, StringComparison.Ordinal);
        Assert.Contains("IX_AccountingOutbox_AggregateVersionBlockers", configuration, StringComparison.Ordinal);
        Assert.Contains("IX_AccountingOutbox_AggregateVersionBlockers", migration, StringComparison.Ordinal);
        Assert.Contains("\"AggregateVersion\", \"OccurredAtUtc\", \"CreatedAt\", \"Id\"", s56, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion\"", s56, StringComparison.Ordinal);
        Assert.Contains("ComputeStableAdvisoryLockKey", processor, StringComparison.Ordinal);
        Assert.DoesNotContain("BusinessEntityId.GetHashCode()", processor, StringComparison.Ordinal);
        Assert.Contains("IsCriticalFinancialTruthEvent", processor, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"AggregateVersion\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationPublishers_MustUseEventPublisher_NotRawPublishEndpoint()
    {
        // Restrict scope to payment/checkout related files only
        var publisherFiles = Directory
            .GetFiles(ApiPath("Services", "Payments"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(ApiPath("Services", "Checkout"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(InfrastructurePath("Payments"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith("IPaymentService.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("MassTransitEventPublisher.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.Contains("Payment", StringComparison.OrdinalIgnoreCase) || 
                          path.Contains("Checkout", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(publisherFiles);

        foreach (var file in publisherFiles)
        {
            var source = File.ReadAllText(file);

            Assert.DoesNotContain("IPublishEndpoint", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_publishEndpoint.Publish", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetRequiredService<IPublishEndpoint>", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ISendEndpointProvider", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MarketplaceOutboxProcessor_MustClaimBeforePublish()
    {
        var source = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventOutboxProcessor.cs"));

        // Extract the processor method that handles outbox messages
        var processorMethod = ExtractMethodBody(source, "ProcessMessageAsync") ??
                              ExtractMethodBody(source, "ProcessPendingMessagesAsync");

        Assert.NotNull(processorMethod); // Fail if neither ProcessPendingMessagesAsync nor ProcessMessageAsync is found
        
        Assert.Contains("message.TryClaim", processorMethod, StringComparison.Ordinal);
        Assert.Matches(
            @"message\.TryClaim[\s\S]+SaveChangesAsync[\s\S]+PublishAsync",
            processorMethod);
    }

    [Fact]
    public void MarketplaceAndStockOutboxes_MustNotPublishNewerAggregateEventsBeforeEarlierOnes()
    {
        var marketplaceProcessor = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventOutboxProcessor.cs"));
        var stockProcessor = File.ReadAllText(InfrastructurePath("Eventing", "StockSyncOutboxProcessor.cs"));
        var marketplaceOptions = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventBackboneOptions.cs"));
        var stockOptions = File.ReadAllText(InfrastructurePath("Eventing", "StockSyncOutboxOptions.cs"));
        var marketplaceConfiguration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "MarketplaceEventOutboxMessageConfiguration.cs"));
        var stockConfiguration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "StockSyncOutboxMessageConfiguration.cs"));
        var migration = ReadInfrastructureMigrationEvidence("20260424183000_S35_EventOrderingBoundaryHardening.sql");
        var s56 = ReadInfrastructureMigrationEvidence("20260429153000_S56_DataAuthorityAndEventOrderingClosure.sql");
        var s68 = ReadInfrastructureMigrationEvidence("20260501213000_S68_StockSyncOutboxWarehouseScopedEventIdentity.sql");

        // Extract helper method bodies to ensure failed earlier critical messages remain blockers.
        var marketplaceHelperBody = ExtractMethodBody(marketplaceProcessor, "HasUnpublishedEarlierEntityEventAsync");
        var stockHelperBody = ExtractMethodBody(stockProcessor, "HasUnpublishedEarlierVariantEventAsync");

        Assert.NotNull(marketplaceHelperBody);
        Assert.NotNull(stockHelperBody);

        Assert.Contains("HasUnpublishedEarlierEntityEventAsync", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("other.AggregateRootId", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("other.TenantId == message.TenantId", marketplaceHelperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("!other.IsFailed", marketplaceHelperBody, StringComparison.Ordinal);
        Assert.Contains("Failed critical marketplace messages remain blockers", marketplaceHelperBody, StringComparison.Ordinal);
        Assert.Contains("Global version ordering", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("OrderBy(message => message.OccurredAtUtc)", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("ThenBy(message => message.CreatedAt)", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("ThenBy(message => message.Id)", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("other.Version < message.Version", marketplaceHelperBody, StringComparison.Ordinal);
        Assert.Contains("other.OccurredAtUtc < message.OccurredAtUtc", marketplaceHelperBody, StringComparison.Ordinal);
        Assert.Contains("other.Id.CompareTo(message.Id) < 0", marketplaceHelperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll", marketplaceProcessor, StringComparison.Ordinal);
        Assert.DoesNotContain(".Chunk(", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("IX_MarketplaceEventOutbox_AggregateOrdering", marketplaceConfiguration, StringComparison.Ordinal);
        Assert.Contains("IX_MarketplaceEventOutbox_AggregateOrderingBlockers", marketplaceConfiguration, StringComparison.Ordinal);
        Assert.Contains("IX_MarketplaceEventOutbox_AggregateOrdering", s56, StringComparison.Ordinal);
        Assert.Contains("IX_MarketplaceEventOutbox_AggregateOrderingBlockers", s56, StringComparison.Ordinal);
        Assert.Contains("ComputeStableAdvisoryLockKey", marketplaceProcessor, StringComparison.Ordinal);
        Assert.Contains("ClaimLeaseSeconds { get; set; } = 300", marketplaceOptions, StringComparison.Ordinal);
        Assert.Contains("message.TryClaim(claimDurationSeconds: _options.ClaimLeaseSeconds)", marketplaceProcessor, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHashCode()", marketplaceProcessor, StringComparison.Ordinal);

        Assert.Contains("HasUnpublishedEarlierVariantEventAsync", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("other.VariantId == message.VariantId", stockHelperBody, StringComparison.Ordinal);
        Assert.Contains("other.WarehouseId == message.WarehouseId", stockHelperBody, StringComparison.Ordinal);
        Assert.Contains("other.TenantId == message.TenantId", stockHelperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("!other.IsFailed", stockHelperBody, StringComparison.Ordinal);
        Assert.Contains("Failed critical stock messages remain blockers", stockHelperBody, StringComparison.Ordinal);
        Assert.Contains("OrderBy(message => message.EventVersion)", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("ThenBy(message => message.StockUpdatedAtUtc)", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("ThenBy(message => message.Id)", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("other.Id.CompareTo(message.Id) < 0", stockHelperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll", stockProcessor, StringComparison.Ordinal);
        Assert.DoesNotContain(".Chunk(", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("ComputeStableAdvisoryLockKey", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("freshMessage.WarehouseId", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("Guid warehouseId", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("ClaimLeaseSeconds { get; set; } = 300", stockOptions, StringComparison.Ordinal);
        Assert.Contains("freshMessage.TryClaim(claimDurationSeconds: _options.ClaimLeaseSeconds)", stockProcessor, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHashCode()", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("IX_StockSyncOutbox_VariantOrdering", stockConfiguration, StringComparison.Ordinal);
        Assert.Contains("IX_StockSyncOutbox_VariantOrderingBlockers", stockConfiguration, StringComparison.Ordinal);
        Assert.Contains("IX_StockSyncOutbox_VariantOrdering", migration, StringComparison.Ordinal);
        Assert.Contains("IX_StockSyncOutbox_VariantOrderingBlockers", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE \"PublishedAtUtc\" IS NULL;", migration, StringComparison.Ordinal);
        
        // Verify warehouse-scoped ordering: indexes must include WarehouseId to prevent
        // cross-warehouse event ordering violations
        Assert.Contains("WarehouseId", stockConfiguration, StringComparison.Ordinal);
        Assert.Contains("x.WarehouseId", stockConfiguration, StringComparison.Ordinal);
        Assert.Contains("WarehouseId", s68, StringComparison.Ordinal);
        Assert.Contains("\"WarehouseId\"", s68, StringComparison.Ordinal);
        
        // Verify the ordering index includes WarehouseId in the key columns
        // Extract the HasIndex block for IX_StockSyncOutbox_VariantOrdering and verify WarehouseId is in it
        var variantOrderingIndexMatch = Regex.Match(stockConfiguration, 
            @"HasIndex\([^)]*\)\s*\.HasDatabaseName\(""IX_StockSyncOutbox_VariantOrdering""\)",
            RegexOptions.Singleline);
        Assert.True(variantOrderingIndexMatch.Success, "IX_StockSyncOutbox_VariantOrdering index declaration not found");
        Assert.Contains("WarehouseId", variantOrderingIndexMatch.Value, StringComparison.Ordinal);
        
        var variantOrderingBlockersIndexMatch = Regex.Match(stockConfiguration,
            @"HasIndex\([^)]*\)\s*\.HasDatabaseName\(""IX_StockSyncOutbox_VariantOrderingBlockers""\)",
            RegexOptions.Singleline);
        Assert.True(variantOrderingBlockersIndexMatch.Success, "IX_StockSyncOutbox_VariantOrderingBlockers index declaration not found");
        Assert.Contains("WarehouseId", variantOrderingBlockersIndexMatch.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentCompensationOutbox_MustClaimAndOrderBeforeGatewayRefund()
    {
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentCompensationOutboxWorker.cs"));
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubPaymentCompensationOutboxMessage.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubPaymentCompensationOutboxMessageConfiguration.cs"));
        var migration = ReadInfrastructureMigrationEvidence("20260428143000_S46_CommerceHubPaymentCompensationOutbox.sql");

        Assert.Contains("ProcessingLeaseId", entity, StringComparison.Ordinal);
        Assert.Contains("ProcessingLeaseExpiresAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("TryClaimMessageAsync", worker, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", worker, StringComparison.Ordinal);
        Assert.Contains("HasUnprocessedEarlierPaymentCompensationAsync", worker, StringComparison.Ordinal);
        Assert.Contains("other.PaymentId == message.PaymentId", worker, StringComparison.Ordinal);
        Assert.Contains("other.ProcessedAtUtc == null", worker, StringComparison.Ordinal);
        Assert.Contains("other.Id.CompareTo(message.Id) < 0", worker, StringComparison.Ordinal);
        Assert.Contains("ThenBy(message => message.Id)", worker, StringComparison.Ordinal);
        Assert.Contains("ProcessingLeaseId = null", worker, StringComparison.Ordinal);
        Assert.Contains("ProcessingLeaseExpiresAtUtc = null", worker, StringComparison.Ordinal);

        var claimIndex = worker.IndexOf("TryClaimMessageAsync(dbContext, initialMessage", StringComparison.Ordinal);
        var refundIndex = worker.IndexOf("refundGateway.RefundAsync", StringComparison.Ordinal);
        Assert.True(claimIndex >= 0 && refundIndex > claimIndex, "Compensation outbox must claim and check ordering before any external refund call.");

        Assert.Contains("IX_CHPaymentCompOutbox_ProcessingLease", configuration, StringComparison.Ordinal);
        Assert.Contains("IX_CHPaymentCompOutbox_PaymentOrdering", configuration, StringComparison.Ordinal);
        Assert.Contains("\"ProcessingLeaseId\" uuid NULL", migration, StringComparison.Ordinal);
        Assert.Contains("\"ProcessingLeaseExpiresAtUtc\" timestamp with time zone NULL", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CHPaymentCompOutbox_ProcessingLease", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CHPaymentCompOutbox_PaymentOrdering", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOutboxMessages_MustRejectInvalidOrderingMetadata()
    {
        var message = File.ReadAllText(CorePath("Entities", "MarketplaceEventOutboxMessage.cs"));

        Assert.Contains("ValidateEventMetadata", message, StringComparison.Ordinal);
        Assert.Contains("EventId == Guid.Empty", message, StringComparison.Ordinal);
        Assert.Contains("EntityId == Guid.Empty", message, StringComparison.Ordinal);
        Assert.Contains("TenantId == Guid.Empty", message, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(@event.CorrelationId)", message, StringComparison.Ordinal);
        Assert.Contains("OccurredAtUtc", message, StringComparison.Ordinal);
        Assert.Contains("DateTimeKind.Utc", message, StringComparison.Ordinal);
        Assert.Contains("@event.Version <= 0", message, StringComparison.Ordinal);
        Assert.Contains("@event.EventVersion <= 0", message, StringComparison.Ordinal);
        Assert.Contains("@event.Version != @event.EventVersion", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderProjection_MustRejectStaleEventMetadata()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubOrderProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "CommerceHubOrderDocument.cs"));

        Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("staleSafeFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Lt(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc)", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Lt(x => x.LastEventVersion, incomingEventVersion)", consumer, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", consumer, StringComparison.Ordinal);
        Assert.Contains("IX_MarketplaceOrder_EventOrdering", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubListingProjection_MustUseFieldScopedFreshnessForCrossDomainEvents()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "CommerceHubProductDocument.cs"));

        foreach (var marker in new[]
        {
            "LastProductProjectionEventOccurredAtUtc",
            "LastProductLifecycleEventOccurredAtUtc",
            "LastStoreMetadataEventOccurredAtUtc",
            "LastCurrencyEventOccurredAtUtc",
            "LastShippingEventOccurredAtUtc",
            "LastPriceEventVersion"
        })
        {
            Assert.Contains(marker, document, StringComparison.Ordinal);
        }

        Assert.Contains("UpdateStockProjectionIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("UpdateProductProjectionIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("UpdatePriceProjectionIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("UnpublishListingIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ListingLifecycleConsumerName", consumer, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCommerceHubListingAuthorityAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlVariantPriceAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshMetadataEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Lte(x => x.LastPriceEventTimestamp, eventTimestamp)", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Lt(x => x.LastPriceEventVersion, eventVersion)", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Lte(x => x.LastListingEventVersion, eventVersion)", consumer, StringComparison.Ordinal);
        Assert.Contains("UpdateOneAsync", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveDocumentAsync", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscountAndVariantProjections_MustRejectStaleEventsAndTombstoneDeletes()
    {
        var couponConsumer = File.ReadAllText(InfrastructurePath("Consumers", "CouponProjectionConsumer.cs"));
        var promotionConsumer = File.ReadAllText(InfrastructurePath("Consumers", "PromotionProjectionConsumer.cs"));
        var variantConsumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductVariantProjectionConsumer.cs"));
        var couponDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "CouponDocument.cs"));
        var promotionDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "PromotionDocument.cs"));
        var variantDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductVariantDocument.cs"));
        var productDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductDocument.cs"));

        foreach (var document in new[] { couponDocument, promotionDocument, variantDocument })
        {
            Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
            Assert.Contains("LastEventVersion", document, StringComparison.Ordinal);
        }

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", couponConsumer, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCouponAuthorityAsync", couponConsumer, StringComparison.Ordinal);
        Assert.Contains("MapSqlAuthorityToDocument(sqlCoupon, @event)", couponConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildTombstoneFromSqlAuthorityAsync", couponConsumer, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CouponUsages", couponConsumer, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.CurrentUsage, currentUsage)", couponConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscountValue = @event.DiscountValue", couponConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain(".Set(x => x.CurrentUsage, @event.CurrentUsage)", couponConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain(".Set(x => x.CurrentUsage, @event.NewTotalUsageCount)", couponConsumer, StringComparison.Ordinal);

        foreach (var consumer in new[] { couponConsumer, promotionConsumer })
        {
            Assert.Contains("BuildFreshnessFilter", consumer, StringComparison.Ordinal);
            Assert.Contains("Builders<", consumer, StringComparison.Ordinal);
            Assert.Contains("Filter.Ne(x => x.LastEventId, @event.EventId)", consumer, StringComparison.Ordinal);
            Assert.Contains("Filter.Lt(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)", consumer, StringComparison.Ordinal);
            Assert.Contains("Filter.Lt(x => x.LastEventVersion, @event.Version)", consumer, StringComparison.Ordinal);
            Assert.Contains("ServerErrorCategory.DuplicateKey", consumer, StringComparison.Ordinal);
            Assert.Contains("IsActive = false", consumer, StringComparison.Ordinal);
        }

        Assert.Contains("EnsureIndexes", promotionConsumer, StringComparison.Ordinal);

        Assert.Contains("v.LastEventOccurredAtUtc < @event.OccurredAtUtc", variantConsumer, StringComparison.Ordinal);
        Assert.Contains("v.LastEventVersion < @event.EventVersion", variantConsumer, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", variantConsumer, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", variantConsumer, StringComparison.Ordinal);
        Assert.Contains("DeletedVariantTombstones", productDocument, StringComparison.Ordinal);
        Assert.Contains("ProductVariantDeletionTombstoneDocument", variantDocument, StringComparison.Ordinal);
        Assert.Contains("BuildNoNewerVariantDeleteTombstoneFilter", variantConsumer, StringComparison.Ordinal);
        Assert.Contains("ProductVariantDeletedEvent stale delete ignored", variantConsumer, StringComparison.Ordinal);
        Assert.Contains("ProductVariantCreatedEvent duplicate or active replay collapsed before side effects", variantConsumer, StringComparison.Ordinal);
        Assert.Contains("UpdateOptions { ArrayFilters = arrayFilters }", variantConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductAndStorefrontProjections_MustRejectStaleEvents()
    {
        var productDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductDocument.cs"));
        var productRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ProductReadRepository.cs"));
        var productProjectionWriter = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ProductSqlAuthorityProjectionWriter.cs"));
        var productMutationBoundary = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "StorefrontProductProjectionMutationBoundary.cs"));
        var productCreatedConsumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductCreatedEventConsumer.cs"));
        var productUpdatedConsumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductUpdatedEventConsumer.cs"));
        var storefrontConfigDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "StorefrontConfigDocument.cs"));
        var storefrontConfigConsumer = File.ReadAllText(InfrastructurePath("Consumers", "StorefrontConfigProjectionConsumer.cs"));
        var storefrontFooterDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "StorefrontFooterDocument.cs"));
        var storefrontFooterConsumer = File.ReadAllText(InfrastructurePath("Consumers", "StorefrontFooterProjectionConsumer.cs"));
        var productsController = File.ReadAllText(ApiPath("Controllers", "ProductsController.cs"));
        var productVariantsController = File.ReadAllText(ApiPath("Controllers", "ProductVariantsController.cs"));

        Assert.Contains("LastEventOccurredAtUtc", productDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", productDocument, StringComparison.Ordinal);
        Assert.Contains("UpdateIfFreshAsync", productRepository, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", productRepository, StringComparison.Ordinal);
        Assert.Contains("filter.Lte(p => p.LastEventOccurredAtUtc, eventOccurredAtUtc)", productRepository, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(p => p.LastEventVersion, eventVersion)", productRepository, StringComparison.Ordinal);
        Assert.Contains("sqlProjection.LastEventOccurredAtUtc = request.EventOccurredAtUtc", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("ProductEventVersionHelper.ToProductEventVersion(sqlProjection.ProductGeneration)", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("sqlProjection.LastEventVersion = authoritativeVersion", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("IStorefrontProductProjectionMutationBoundary", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("StorefrontProductProjectionMutationKind.ReviewedVisibilityTombstone", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("ReviewedFullDocumentUpsert", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", productCreatedConsumer, StringComparison.Ordinal);
        Assert.Contains("_projectionWriter.ApplyAsync", productCreatedConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", productCreatedConsumer, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", productUpdatedConsumer, StringComparison.Ordinal);
        Assert.Contains("_projectionWriter.ApplyAsync", productUpdatedConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", productUpdatedConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductUpdatedEvent arrived before ProductCreatedEvent", productUpdatedConsumer, StringComparison.Ordinal);
        Assert.Contains("ProductUpdatedEvent ignored because SQL-recomposed projection already has same or newer product event", productUpdatedConsumer, StringComparison.Ordinal);
        Assert.Contains("EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion)", productsController, StringComparison.Ordinal);
        Assert.Contains("EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion)", productVariantsController, StringComparison.Ordinal);

        foreach (var document in new[] { storefrontConfigDocument, storefrontFooterDocument })
        {
            Assert.Contains("LastEventId", document, StringComparison.Ordinal);
            Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
            Assert.Contains("LastEventVersion", document, StringComparison.Ordinal);
        }

        Assert.Contains("BuildFreshStoreConfigEventFilter", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("filter.Ne(x => x.LastEventId, eventId)", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc)", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LiveVersion, eventVersion)", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("LiveVersion = sqlConfig.LiveVersion", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("ReplaceOneAsync", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("new ReplaceOptions { IsUpsert = false }", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("replaceResult.IsAcknowledged && replaceResult.MatchedCount == 0", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("InsertOneAsync", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateKey", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("StoreConfig projection ignored stale concurrent event", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion = eventVersion", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionFreshnessHelper.IsExistingEventNewerOrEqual", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("Storefront footer projection ignored stale event", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshFooterEventFilter", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("FailClosedFooterProjectionAsync", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.TenantInactiveOrDeleted", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("ReplaceOneAsync", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("new ReplaceOptions { IsUpsert = false }", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("InsertOneAsync", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateKey", storefrontFooterConsumer, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion = context.Message.EventVersion", storefrontFooterConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderTrustProjections_MustRejectStaleAggregateEvents()
    {
        var trustEvent = File.ReadAllText(CorePath("Events", "ProviderTrustUpdatedEvent.cs"));
        var trustConsumer = File.ReadAllText(InfrastructurePath("Consumers", "ProviderTrustConsumer.cs"));
        var trustProjection = File.ReadAllText(InfrastructurePath("Consumers", "ProviderTrustProjectionConsumer.cs"));
        var trustDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProviderTrustSnapshotDocument.cs"));
        var discoveryProjection = File.ReadAllText(InfrastructurePath("Consumers", "ServiceDiscoveryProjectionConsumer.cs"));
        var discoveryDocument = File.ReadAllText(InfrastructurePath("Elasticsearch", "Documents", "MarketplaceServiceDocument.cs"));

        Assert.Contains("ProviderTrustUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent", trustEvent, StringComparison.Ordinal);
        Assert.Contains("GetAuthoritativeAggregateId() => ProviderProfileId", trustEvent, StringComparison.Ordinal);
        Assert.Contains("ProviderProfileId", File.ReadAllText(CorePath("Events", "IntegrationEventAuthority.cs")), StringComparison.Ordinal);
        Assert.Contains("IEventPublisher", trustConsumer, StringComparison.Ordinal);
        Assert.Contains("EventVersion = aggregate.Version", trustConsumer, StringComparison.Ordinal);
        Assert.Contains("OccurredAtUtc = aggregate.UpdatedAt", trustConsumer, StringComparison.Ordinal);
        AssertContainsInOrder(
            trustConsumer,
            "_eventPublisher.PublishAsync(new ProviderTrustUpdatedEvent",
            "await _context.SaveChangesAsync(context.CancellationToken)");
        Assert.DoesNotContain("context.Publish(new ProviderTrustUpdatedEvent", trustConsumer, StringComparison.Ordinal);

        Assert.Contains("LastEventId", trustDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", trustDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", trustDocument, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter", trustProjection, StringComparison.Ordinal);
        Assert.Contains("filter.Ne(d => d.LastEventId, eventId)", trustProjection, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(d => d.LastEventOccurredAtUtc, occurredAtUtc)", trustProjection, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(d => d.LastEventVersion, eventVersion)", trustProjection, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", trustProjection, StringComparison.Ordinal);

        Assert.Contains("LastEventOccurredAtUtc", discoveryDocument, StringComparison.Ordinal);
        Assert.Contains("lastEventOccurredAtUtc", discoveryProjection, StringComparison.Ordinal);
        Assert.Contains("ctx.op = 'noop'", discoveryProjection, StringComparison.Ordinal);
        Assert.Contains(".Add(\"eventVersion\", message.EventVersion)", discoveryProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantCurrencyProjection_MustUseIndependentCurrencyFreshnessFence()
    {
        var settingsController = File.ReadAllText(ApiPath("Controllers", "SettingsController.cs"));
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "TenantCurrencyProjectionConsumer.cs"));
        var storefrontConfigConsumer = File.ReadAllText(InfrastructurePath("Consumers", "StorefrontConfigProjectionConsumer.cs"));
        var commerceHubConsumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var affiliateIntelligenceConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));
        var affiliatePolicyConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "StorefrontConfigDocument.cs"));

        Assert.Contains("LastCurrencyEventId", document, StringComparison.Ordinal);
        Assert.Contains("LastCurrencyEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastCurrencyEventVersion", document, StringComparison.Ordinal);

        Assert.Contains("OccurredAtUtc = currencyChangedAtUtc", settingsController, StringComparison.Ordinal);
        Assert.Contains("EventVersion = Math.Max", settingsController, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlTenantCurrencyAsync", consumer, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.Currency, sqlCurrency)", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain(".Set(x => x.Currency, @event.Currency)", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshCurrencyEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Ne(x => x.LastCurrencyEventId, eventId)", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastCurrencyEventOccurredAtUtc, occurredAtUtc)", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastCurrencyEventVersion, eventVersion)", consumer, StringComparison.Ordinal);
        Assert.Contains("TenantCurrencyChangedEvent ignored as stale or duplicate", consumer, StringComparison.Ordinal);

        Assert.Contains("Preserve the independent currency freshness fence", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("mongoDoc.LastCurrencyEventId = existingProjection.LastCurrencyEventId", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("mongoDoc.LastCurrencyEventOccurredAtUtc = existingProjection.LastCurrencyEventOccurredAtUtc", storefrontConfigConsumer, StringComparison.Ordinal);
        Assert.Contains("mongoDoc.LastCurrencyEventVersion = existingProjection.LastCurrencyEventVersion", storefrontConfigConsumer, StringComparison.Ordinal);

        Assert.Contains("private static string ResolveCurrency()", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", commerceHubConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveCurrentSqlTenantCurrencyAsync", commerceHubConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateCurrencyAsync(\r\n        Guid tenantId,\r\n        string? currency", commerceHubConsumer, StringComparison.Ordinal);

        Assert.Contains("var currency = await ResolveTenantCurrencyAsync(tenantId, ct);", affiliateIntelligenceConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("context.Message.Currency,\r\n            $\"Consumer: AffiliateProductIntelligenceProjectionConsumer | Event: TenantCurrencyChanged", affiliateIntelligenceConsumer, StringComparison.Ordinal);
        Assert.Contains("LoadActiveAffiliateTenantProfileAsync(tenantId, ct)", affiliatePolicyConsumer, StringComparison.Ordinal);
        Assert.Contains("tenantProfile.Currency", affiliatePolicyConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("context.Message.Currency, \r\n            $\"Consumer: AffiliateProductPolicyProjectionConsumer | Event: TenantCurrencyChanged", affiliatePolicyConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductMediaProjection_MustBeTransactionalAndFieldScopedOrdered()
    {
        var service = File.ReadAllText(ApiPath("Services", "ProductMediaService.cs"));
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductMediaChangedEventConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductDocument.cs"));
        var commerceHubConsumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var affiliatePolicyConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var affiliateIntelligenceConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));

        Assert.Contains("LastMediaEventId", document, StringComparison.Ordinal);
        Assert.Contains("LastMediaEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastMediaEventVersion", document, StringComparison.Ordinal);

        Assert.Contains("TouchProductForMediaProjection(product, changedAtUtc)", service, StringComparison.Ordinal);
        Assert.Contains("MarketplaceProductProjectionVersion.AdvanceAsync", service, StringComparison.Ordinal);
        Assert.Contains("await PublishMediaChangedEventAsync(product", service, StringComparison.Ordinal);
        Assert.Matches(
            @"PublishMediaChangedEventAsync[\s\S]+SaveChangesAsync",
            ExtractMethodBody(service, "AddMediaAsync") ?? string.Empty);
        Assert.Matches(
            @"PublishMediaChangedEventAsync[\s\S]+SaveChangesAsync",
            ExtractMethodBody(service, "RemoveMediaAsync") ?? string.Empty);
        Assert.Matches(
            @"PublishMediaChangedEventAsync[\s\S]+SaveChangesAsync",
            ExtractMethodBody(service, "ReorderMediaAsync") ?? string.Empty);

        Assert.Contains("BuildFreshMediaEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Ne(p => p.LastMediaEventId, eventId)", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(p => p.LastMediaEventOccurredAtUtc, occurredAtUtc)", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(p => p.LastMediaEventVersion, eventVersion)", consumer, StringComparison.Ordinal);
        Assert.Contains("Set(p => p.LastMediaEventOccurredAtUtc, @event.OccurredAtUtc)", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("Set(p => p.LastEventOccurredAtUtc, @event.OccurredAtUtc)", consumer, StringComparison.Ordinal);

        Assert.Contains("IConsumer<ProductMediaChangedEvent>", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("IsCurrentSqlProductMediaEventAsync", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("UpdateListingImageProjectionIfFreshAsync", commerceHubConsumer, StringComparison.Ordinal);

        Assert.Contains("IConsumer<ProductMediaChangedEvent>", affiliatePolicyConsumer, StringComparison.Ordinal);
        Assert.Contains("ResolvePrimaryProductImage(product)", affiliatePolicyConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("ThumbnailUrl = product.Images?.Split(',').FirstOrDefault() ?? string.Empty", affiliatePolicyConsumer, StringComparison.Ordinal);

        Assert.Contains("IConsumer<ProductMediaChangedEvent>", affiliateIntelligenceConsumer, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentSqlProductImagesForMediaEventAsync", affiliateIntelligenceConsumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", affiliateIntelligenceConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutSessionProjection_MustKeepConvertedTerminalAgainstOutOfOrderEvents()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CheckoutSessionProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "AbandonedCheckoutDocument.cs"));

        Assert.Contains("LastEventId", document, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("BuildFreshLifecycleFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("SetOnInsert(x => x.Status, \"Active\")", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Ne(x => x.Status, \"ConvertedToOrder\")", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Ne(x => x.LastEventId, eventId)", consumer, StringComparison.Ordinal);
        Assert.Contains("Set(x => x.Status, \"ConvertedToOrder\")", consumer, StringComparison.Ordinal);
        Assert.Contains("Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCriticalPaymentAndShippingProjections_MustRejectStaleEvents()
    {
        var paymentConsumer = File.ReadAllText(InfrastructurePath("Consumers", "PaymentMethodProjectionConsumer.cs"));
        var paymentRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "PaymentMethodReadRepository.cs"));
        var paymentDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "PaymentMethodDocument.cs"));
        var shippingConsumer = File.ReadAllText(InfrastructurePath("Consumers", "ShippingMethodProjectionConsumer.cs"));
        var shippingRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ShippingMethodReadRepository.cs"));
        var shippingDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ShippingMethodDocument.cs"));

        Assert.Contains("LastEventOccurredAtUtc", paymentDocument, StringComparison.Ordinal);
        Assert.Contains("IsDeleted", paymentDocument, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("LoadSqlPaymentMethodAuthorityAsync", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("MapSqlAuthorityToDocument", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("_dataProtector.Unprotect", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Ne(d => d.LastEventId, eventId)", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Lt(d => d.LastEventOccurredAtUtc, occurredAtUtc)", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("IsDeleted = true", paymentConsumer, StringComparison.Ordinal);
        Assert.Contains("Ne(x => x.IsDeleted, true)", paymentRepository, StringComparison.Ordinal);

        Assert.Contains("LastEventOccurredAtUtc", shippingDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", shippingDocument, StringComparison.Ordinal);
        Assert.Contains("IsDeleted", shippingDocument, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", shippingConsumer, StringComparison.Ordinal);
        Assert.Contains("UpsertIfFreshAsync", shippingConsumer, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", shippingConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter", shippingRepository, StringComparison.Ordinal);
        Assert.Contains("Filter.Ne(x => x.LastEventId, eventId)", shippingRepository, StringComparison.Ordinal);
        Assert.Contains("Filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc)", shippingRepository, StringComparison.Ordinal);
        Assert.Contains("Filter.Lt(x => x.LastEventVersion, eventVersion)", shippingRepository, StringComparison.Ordinal);
        Assert.Contains("Ne(x => x.IsDeleted, true)", shippingRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateLifecycleProjection_MustRejectOutOfOrderStatusEvents()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProjectionConsumers.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "Affiliate", "AffiliateDocuments.cs"));

        Assert.Contains("LastOrderStatusEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastFulfillmentStatusEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("BuildOrderStatusLifecycleFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFulfillmentLifecycleFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildOrderAndFulfillmentLifecycleFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("LastOrderStatusEventOccurredAtUtc, evt.OccurredAtUtc", consumer, StringComparison.Ordinal);
        Assert.Contains("LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlineCustomerIdentityProjections_MustRejectStaleLifecycleAndSessionEvents()
    {
        var customerConsumer = File.ReadAllText(InfrastructurePath("Consumers", "OnlineCustomerProjectionConsumer.cs"));
        var sessionConsumer = File.ReadAllText(InfrastructurePath("Consumers", "OnlineCustomerSessionProjectionConsumer.cs"));
        var customerDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "OnlineCustomerDocument.cs"));
        var sessionDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "OnlineCustomerSessionDocument.cs"));

        Assert.Contains("LastEventId", customerDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", customerDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", customerDocument, StringComparison.Ordinal);
        Assert.Contains("BuildFreshCustomerEventFilter", customerConsumer, StringComparison.Ordinal);
        Assert.Contains("filter.Ne(x => x.LastEventId, eventId)", customerConsumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc)", customerConsumer, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", customerConsumer, StringComparison.Ordinal);
        
        // Ban ReplaceOneAsync with IsUpsert = true (formatting-agnostic check)
        var normalizedCustomerConsumer = System.Text.RegularExpressions.Regex.Replace(customerConsumer, @"\s+", " ");
        Assert.DoesNotMatch(@"ReplaceOneAsync\s*\([^)]*\)\s*.*?IsUpsert\s*=\s*true", normalizedCustomerConsumer);

        Assert.Contains("LastEventId", sessionDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", sessionDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", sessionDocument, StringComparison.Ordinal);
        Assert.Contains("LastSessionRevocationEventId", customerDocument, StringComparison.Ordinal);
        Assert.Contains("LastSessionRevokedAtUtc", customerDocument, StringComparison.Ordinal);
        Assert.Contains("BuildFreshSessionEventFilter", sessionConsumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshCustomerSessionEventFilter", sessionConsumer, StringComparison.Ordinal);
        Assert.Contains("ApplyCustomerSessionRevocationWatermarkAsync", sessionConsumer, StringComparison.Ordinal);
        Assert.Contains("@event.IssuedAtUtc <= customerState.LastSessionRevokedAtUtc.Value", sessionConsumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lte(x => x.CreatedAtUtc, includeOnlyExistingAtUtc.Value)", sessionConsumer, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", sessionConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlineCustomerWishlistProjection_MustUsePerItemOrderingAndClearWatermark()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "OnlineCustomerWishlistProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "OnlineCustomerWishlistDocument.cs"));
        var service = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontWishlistService.cs"));

        Assert.Contains("LastClearEventId", document, StringComparison.Ordinal);
        Assert.Contains("LastClearEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastClearEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("IsRemoved", document, StringComparison.Ordinal);
        Assert.Contains("LastEventId", document, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", document, StringComparison.Ordinal);

        Assert.Contains("BuildNoNewerClearEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshClearEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshWishlistItemArrayFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("UpdateExistingWishlistItemIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("PushWishlistItemIfMissingAndFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", consumer, StringComparison.Ordinal);
        Assert.Contains("IX_Wishlist_ClearEventOrdering", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain(".PullFilter(x => x.Items", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain(".Inc(x => x.TotalCount, -1)", consumer, StringComparison.Ordinal);

        Assert.Contains(".Where(i => !i.IsRemoved)", service, StringComparison.Ordinal);
        Assert.Contains("i.ProductId == productId && !i.IsRemoved", service, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCmsPageProjection_MustTombstoneDeletesAndRejectStaleEvents()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CmsPageProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "StorefrontPageDocument.cs"));

        Assert.Contains("LastEventId", document, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("BuildFreshPageEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("ReplacePageIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("TombstoneProjectionIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("Set(item => item.IsPublished, false)", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(item => item.LastEventOccurredAtUtc, occurredAtUtc)", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(item => item.LastEventVersion, eventVersion)", consumer, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteOneAsync(", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteManyAsync(", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentService_DirectFlows_MustStageEventsBeforeSaveChanges()
    {
        var source = File.ReadAllText(ApiPath("Services", "Payments", "PaymentService.cs"));

        var createPaymentIntentMethod = ExtractMethodBody(source, "CreatePaymentIntentAsync");
        Assert.NotNull(createPaymentIntentMethod); // Fail if method not found
        Assert.Matches(
            @"EmitPaymentIntentCreatedEventAsync[\s\S]+SaveChangesAsync",
            createPaymentIntentMethod);

        var confirmCodMethod = ExtractMethodBody(source, "ConfirmCodOrderAsync");
        Assert.NotNull(confirmCodMethod); // Fail if method not found
        Assert.Matches(
            @"EmitPaymentSkippedEventAsync[\s\S]+EmitOrderConfirmedEventAsync[\s\S]+SaveChangesAsync",
            confirmCodMethod);
    }

    [Fact]
    public void PaymentLifecycleService_MustStageLifecycleEventsBeforeCommit()
    {
        var source = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));

        var handleAuthorizedMethod = ExtractMethodBody(source, "private async Task<PaymentTransitionResult> HandleAuthorizedAsync");
        Assert.NotNull(handleAuthorizedMethod);
        AssertContainsInOrder(
            handleAuthorizedMethod!,
            "PublishCanonicalAuthorizedOutcomeEventAsync",
            "await _db.SaveChangesAsync(ct);",
            "await transaction.CommitAsync(ct);");

        var handleSuccessMethod = ExtractMethodBody(source, "private async Task<PaymentTransitionResult> HandleSuccessAsync");
        Assert.NotNull(handleSuccessMethod); // Fail if method not found
        AssertContainsInOrder(
            handleSuccessMethod!,
            "PublishSuccessDomainEventAsync",
            "PublishCanonicalSuccessOutcomeEventsAsync",
            "await _db.SaveChangesAsync(ct);",
            "await transaction.CommitAsync(ct);");
        Assert.DoesNotContain(
            "PublishCanonical",
            SectionBetween(handleSuccessMethod!, "// Post-commit work", "return PaymentTransitionResult.Succeeded"),
            StringComparison.Ordinal);

        var handleFailureMethod = ExtractMethodBody(source, "private async Task<PaymentTransitionResult> HandleFailureAsync");
        Assert.NotNull(handleFailureMethod);
        AssertContainsInOrder(
            handleFailureMethod!,
            "PublishFailureDomainEventAsync",
            "PublishCanonicalFailureOutcomeEventsAsync",
            "await _db.SaveChangesAsync(ct);",
            "await transaction.CommitAsync(ct);");
        Assert.DoesNotContain(
            "PublishCanonical",
            SectionBetween(handleFailureMethod!, "// Post-commit work", "return PaymentTransitionResult.Succeeded"),
            StringComparison.Ordinal);

        var recordCodSettlementMethod = ExtractMethodBody(source, "RecordOfflineSettlementAsync");
        Assert.NotNull(recordCodSettlementMethod); // Fail if method not found
        Assert.Matches(
            @"OfflineSettlementCompletedEvent[\s\S]+SaveChangesAsync[\s\S]+CommitAsync",
            recordCodSettlementMethod);

        var recordGatewaySettlementSignature = "public async Task<PaymentTransitionResult> RecordGatewaySettlementAsync(";
        var recordGatewaySettlementIndex = source.IndexOf(recordGatewaySettlementSignature, StringComparison.Ordinal);
        Assert.True(recordGatewaySettlementIndex >= 0, "RecordGatewaySettlementAsync should exist.");
        var recordGatewaySettlementSection = source[recordGatewaySettlementIndex..];
        AssertContainsInOrder(
            recordGatewaySettlementSection,
            "GatewaySettlementCompletedEvent",
            "await _db.SaveChangesAsync(ct);",
            "await transaction.CommitAsync(ct);");

        var recordRefundMethod = ExtractMethodBody(source, "RecordRefundAsync");
        Assert.NotNull(recordRefundMethod); // Fail if method not found
        Assert.Matches(
            @"OrderRefundedEvent[\s\S]+SaveChangesAsync[\s\S]+CommitAsync",
            recordRefundMethod);
    }

    [Fact]
    public void CommerceHubCheckoutService_MustStageOrderCreatedEventBeforeCommit()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));

        var checkoutMethod = ExtractMethodBody(source, "CheckoutAsync");
        Assert.NotNull(checkoutMethod);
        Assert.Matches(
            @"StageMarketplaceOrderCreatedAsync[\s\S]+SaveChangesAsync[\s\S]+CommitAsync",
            checkoutMethod);

        var stageMethod = ExtractMethodBody(source, "StageMarketplaceOrderCreatedAsync");
        Assert.NotNull(stageMethod);
        Assert.Contains("_eventPublisher.PublishAsync", stageMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("catch", stageMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishMarketplaceOrderCreatedAsync", source, StringComparison.Ordinal);
    }

    private static void AssertContainsInOrder(string source, params string[] markers)
    {
        var previousIndex = -1;
        foreach (var marker in markers)
        {
            var index = source.IndexOf(marker, previousIndex + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected marker '{marker}' after index {previousIndex}.");
            previousIndex = index;
        }
    }

    private static string SectionBetween(string source, string startMarker, string endMarker)
    {
        var startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidOperationException(
                $"Start marker not found: '{startMarker}'. " +
                $"The expected marker is missing from the source code, which indicates the guarded section may have been removed or renamed.");
        }

        var endIndex = source.IndexOf(endMarker, startIndex + startMarker.Length, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new InvalidOperationException(
                $"End marker not found: '{endMarker}'. " +
                $"The expected marker is missing from the source code after the start marker '{startMarker}', " +
                $"which indicates the guarded section may be incomplete or the marker was removed.");
        }

        return source[startIndex..endIndex];
    }

    private static string ReadEventSource(string eventTypeName)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var coreEventsRoot = Path.Combine(solutionRoot, "PointOfSale.Core", "Events");
        var file = Directory
            .GetFiles(coreEventsRoot, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.ReadAllText(path).Contains($"class {eventTypeName}", StringComparison.Ordinal));

        if (file == null)
        {
            throw new FileNotFoundException($"Could not find event source for {eventTypeName}.");
        }

        return File.ReadAllText(file);
    }

    /// <summary>
    /// Extracts a method body from source code by finding the method signature and its closing brace.
    /// This is a simple heuristic-based extraction that handles most common cases.
    /// </summary>
    private static string? ExtractMethodBody(string source, string methodName)
    {
        var methodPattern = methodName.Contains(' ', StringComparison.Ordinal)
            ? Regex.Escape(methodName) + @"\s*\("
            : $@"\b{Regex.Escape(methodName)}\s*\(";
        foreach (Match match in Regex.Matches(source, methodPattern))
        {
            var openParenIndex = source.IndexOf('(', match.Index);
            if (openParenIndex == -1)
            {
                continue;
            }

            var closeParenIndex = FindMatchingParen(source, openParenIndex);
            if (closeParenIndex == -1)
            {
                continue;
            }

            var openBraceIndex = FindMethodOpeningBrace(source, closeParenIndex + 1);
            if (openBraceIndex == -1)
            {
                continue;
            }

            var closingBraceIndex = FindMatchingBrace(source, openBraceIndex);
            if (closingBraceIndex != -1)
            {
                return source.Substring(match.Index, closingBraceIndex - match.Index + 1);
            }
        }

        return null;
    }

    private static int FindMatchingParen(string source, int openParenIndex)
    {
        var depth = 1;
        for (var i = openParenIndex + 1; i < source.Length; i++)
        {
            if (source[i] == '(')
            {
                depth++;
            }
            else if (source[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindMethodOpeningBrace(string source, int startIndex)
    {
        for (var i = startIndex; i < source.Length; i++)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                continue;
            }

            if (source[i] == '{')
            {
                return i;
            }

            if (source[i] == ';' || source[i] == ',' || source[i] == ')' || source[i] == '=')
            {
                return -1;
            }

            if (source.AsSpan(i).StartsWith("where".AsSpan(), StringComparison.Ordinal))
            {
                var braceIndex = source.IndexOf('{', i);
                var semicolonIndex = source.IndexOf(';', i);
                return braceIndex >= 0 && (semicolonIndex == -1 || braceIndex < semicolonIndex)
                    ? braceIndex
                    : -1;
            }

            return -1;
        }

        return -1;
    }

    private static int FindMatchingBrace(string source, int openBraceIndex)
    {
        var braceCount = 1;
        var currentIndex = openBraceIndex + 1;

        while (currentIndex < source.Length && braceCount > 0)
        {
            var c = source[currentIndex];

            // Handle string literals
            if (c == '"' || c == '\'')
            {
                var quote = c;
                
                // Check for verbatim string (@"..." or $@"...")
                var isVerbatim = false;
                if (quote == '"' && currentIndex > 0)
                {
                    if (source[currentIndex - 1] == '@')
                    {
                        isVerbatim = true;
                    }
                    else if (currentIndex > 1 && source[currentIndex - 1] == '@' && source[currentIndex - 2] == '$')
                    {
                        isVerbatim = true;
                    }
                }
                
                // Check for raw string literal ("""...""" or more quotes)
                var rawQuoteCount = 0;
                if (quote == '"')
                {
                    var tempIndex = currentIndex;
                    while (tempIndex < source.Length && source[tempIndex] == '"')
                    {
                        rawQuoteCount++;
                        tempIndex++;
                    }
                }
                
                // Raw strings must have 3+ quotes AND not be verbatim strings
                var isRawString = rawQuoteCount >= 3 && !isVerbatim;
                
                if (isRawString)
                {
                    // Raw string literal: find matching sequence of quotes
                    currentIndex += rawQuoteCount;
                    while (currentIndex < source.Length)
                    {
                        if (source[currentIndex] == '"')
                        {
                            var closingQuoteCount = 0;
                            var tempIndex = currentIndex;
                            while (tempIndex < source.Length && source[tempIndex] == '"')
                            {
                                closingQuoteCount++;
                                tempIndex++;
                            }
                            
                            if (closingQuoteCount == rawQuoteCount)
                            {
                                currentIndex = tempIndex - 1;
                                break;
                            }
                        }
                        currentIndex++;
                    }
                }
                else if (isVerbatim)
                {
                    // Verbatim string: "" is escape for "
                    currentIndex++;
                    while (currentIndex < source.Length)
                    {
                        if (source[currentIndex] == '"')
                        {
                            // Check if it's a doubled quote (escape)
                            if (currentIndex + 1 < source.Length && source[currentIndex + 1] == '"')
                            {
                                currentIndex += 2; // Skip both quotes
                                continue;
                            }
                            break; // Found closing quote
                        }
                        currentIndex++;
                    }
                }
                else
                {
                    // Normal string or char literal: backslash escapes
                    currentIndex++;
                    while (currentIndex < source.Length)
                    {
                        if (source[currentIndex] == quote)
                        {
                            // Count consecutive backslashes before the quote
                            var backslashCount = 0;
                            var tempIndex = currentIndex - 1;
                            while (tempIndex >= 0 && source[tempIndex] == '\\')
                            {
                                backslashCount++;
                                tempIndex--;
                            }
                            
                            // Odd number of backslashes means the quote is escaped
                            if (backslashCount % 2 == 0)
                            {
                                break; // Found unescaped closing quote
                            }
                        }
                        currentIndex++;
                    }
                }
            }
            else if (c == '{')
            {
                braceCount++;
            }
            else if (c == '}')
            {
                braceCount--;
            }

            currentIndex++;
        }

        return braceCount == 0
            ? currentIndex - 1
            : -1;
    }

    private static string ApiPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.API" }
                .Concat(parts)
                .ToArray()));
    }

    private static string InfrastructurePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.Infrastructure" }
                .Concat(parts)
                .ToArray()));
    }

    private static string ReadInfrastructureMigrationEvidence(string fileName)
    {
        var activePath = InfrastructurePath("Migrations", fileName);
        if (File.Exists(activePath))
        {
            return File.ReadAllText(activePath);
        }

        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var archivedPath = Path.Combine(
            solutionRoot,
            "docs",
            "recovery",
            "archive",
            "RECOVERY-4_PHASE_A",
            "PointOfSale.Infrastructure",
            "Migrations",
            fileName);

        if (File.Exists(archivedPath))
        {
            return File.ReadAllText(archivedPath);
        }

        throw new FileNotFoundException(
            $"Migration evidence '{fileName}' was not found in active migrations or recovery archive.",
            activePath);
    }

    private static string CorePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.Core" }
                .Concat(parts)
                .ToArray()));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
