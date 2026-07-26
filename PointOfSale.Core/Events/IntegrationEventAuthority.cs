namespace PointOfSale.Core.Events;

/// <summary>
/// Canonical authority rules for integration events that can mutate downstream business state.
/// Keep publishers, legacy outboxes, and database constraints aligned with these fragments.
/// </summary>
public static class IntegrationEventAuthority
{
    public static readonly IReadOnlyList<string> CriticalEventNameFragments = Array.AsReadOnly(new[]
    {
        "Order",
        "Payment",
        "Inventory",
        "Stock",
        "Product",
        "Variant",
        "CommerceHub",
        "Marketplace",
        "Affiliate",
        "Coupon",
        "Promotion",
        "Tenant",
        "StoreConfig",
        "ShippingMethod",
        "Shipment",
        "Customer",
        "Checkout",
        "Refund",
        "Payout",
        "Settlement",
        "Ledger",
        "Accounting",
        "Wallet",
        "Cod",
        "Currency",
        "ProviderTrust"
    });

    public static readonly IReadOnlyList<string> NonAuthoritativeEventTypeNames = Array.AsReadOnly(new[]
    {
        "AffiliateLinkClickedEvent",
        "LowStockAlertEvent",
        "OnlineCustomerEmailVerificationRequestedEvent",
        "PromotionSeenEvent",
        "PromotionClickedEvent",
        "PromotionConvertedEvent",
        "PromotionBoostAppliedEvent",
        "WhatsAppOrderConfirmationRequestedEvent",
        "WhatsAppOtpRequestedEvent",
        "WhatsAppPaymentApprovalRequestedEvent",
        "WhatsAppPaymentApprovedNotificationEvent",
        "WhatsAppPaymentRejectedNotificationEvent"
    });

    private static readonly IReadOnlyList<string> AggregateIdPropertyNames = Array.AsReadOnly(new[]
    {
        "MarketplaceOrderId",
        "SubOrderId",
        "MarketplacePaymentId",
        "CommerceHubPaymentId",
        "ListingId",
        "OrderId",
        "PaymentId",
        "PaymentIntentId",
        "ProductId",
        "VariantId",
        "StockId",
        "CategoryId",
        "CouponId",
        "PromotionId",
        "ShippingMethodId",
        "ShipmentId",
        "CustomerId",
        "OnlineCustomerId",
        "AffiliateId",
        "AffiliateAccountId",
        "PayoutId",
        "RefundId",
        "SettlementBatchId",
        "CartId",
        "CheckoutSessionId",
        "StoreConfigId",
        "StorefrontConfigId",
        "PageId",
        "TenantId",
        "StoreId",
        "StoreTenantId",
        "VendorId",
        "ProviderId",
        "ProviderProfileId",
        "EntityId",
        "BusinessEntityId"
    });

    public static bool IsAuthoritativeEventType(Type eventType)
    {
        if (typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType) &&
            typeof(INonAuthoritativeIntegrationEvent).IsAssignableFrom(eventType))
        {
            throw new InvalidOperationException(
                $"Event type {eventType.FullName} cannot be both authoritative and non-authoritative. " +
                "Cross-system truth events must have a single explicit authority contract.");
        }

        return !typeof(INonAuthoritativeIntegrationEvent).IsAssignableFrom(eventType) &&
            !IsKnownNonAuthoritativeEventTypeName(eventType.Name) &&
            (typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType) ||
            IsCriticalEventTypeName(eventType.Name));
    }

    public static bool IsCriticalEventTypeName(string eventTypeName) =>
        !IsKnownNonAuthoritativeEventTypeName(eventTypeName) &&
        CriticalEventNameFragments.Any(fragment =>
            eventTypeName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownNonAuthoritativeEventTypeName(string eventTypeName) =>
        NonAuthoritativeEventTypeNames.Any(suffix =>
            eventTypeName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public static Guid? TryGetAggregateId(IntegrationEvent @event)
    {
        if (@event is IAggregateScopedIntegrationEvent scopedEvent)
        {
            var aggregateId = scopedEvent.GetAuthoritativeAggregateId();
            if (aggregateId != Guid.Empty)
            {
                return aggregateId;
            }
        }

        var eventType = @event.GetType();

        foreach (var propertyName in AggregateIdPropertyNames)
        {
            var property = eventType.GetProperty(propertyName);
            var value = property?.GetValue(@event);
            if (value is Guid id && id != Guid.Empty)
            {
                return id;
            }
        }

        return null;
    }

    public static void ValidateAuthoritativeEventContract(Type eventType)
    {
        if (!IsAuthoritativeEventType(eventType))
        {
            return;
        }

        if (!HasAuthoritativeAggregateIdContract(eventType))
        {
            throw new InvalidOperationException(
                $"Authoritative event type {eventType.FullName} must expose a deterministic aggregate ordering key " +
                "by implementing IAggregateScopedIntegrationEvent or declaring a supported non-empty Guid aggregate id property.");
        }

        if (HasAffectedTenantScopeShape(eventType) &&
            !typeof(IAffectedTenantScopedIntegrationEvent).IsAssignableFrom(eventType))
        {
            throw new InvalidOperationException(
                $"Authoritative event type {eventType.FullName} carries multi-tenant scope fields but does not implement " +
                "IAffectedTenantScopedIntegrationEvent. Cross-system readiness cannot safely determine all affected tenants.");
        }
    }

    public static bool HasAuthoritativeAggregateIdContract(Type eventType)
    {
        if (typeof(IAggregateScopedIntegrationEvent).IsAssignableFrom(eventType))
        {
            return true;
        }

        return AggregateIdPropertyNames.Any(propertyName =>
        {
            var property = eventType.GetProperty(propertyName);
            return property?.PropertyType == typeof(Guid) ||
                   property?.PropertyType == typeof(Guid?);
        });
    }

    private static bool HasAffectedTenantScopeShape(Type eventType) =>
        eventType.GetProperties().Any(property =>
            string.Equals(property.Name, "AffectedTenantIds", StringComparison.Ordinal) ||
            string.Equals(property.Name, "AffectedStoreIds", StringComparison.Ordinal) ||
            string.Equals(property.Name, "AffectedVendorIds", StringComparison.Ordinal));
}
