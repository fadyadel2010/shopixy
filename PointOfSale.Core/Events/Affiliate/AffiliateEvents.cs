using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Core.Events.Affiliate;

/// <summary>
/// Emitted when an order is attributed to an affiliate.
/// </summary>
public class AffiliateAttributedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid AttributionId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public Guid? PromotionLinkId { get; set; }
    public AttributionSource Source { get; set; }
    public DateTime AttributedAtUtc { get; set; }

    /// <summary>
    /// AF-36: Snapshot of the payment method code at attribution time.
    /// Used by conversion/ranking consumers to gate signals on payment verification.
    /// Examples: "COD", "MANUALWALLET", "VODAFONE_CASH", "STRIPE"
    /// </summary>
    public string PaymentMethodCode { get; set; } = string.Empty;

    /// <summary>
    /// AF-36: True if this order requires admin payment verification before
    /// commission, conversion, and ranking signals should be triggered.
    /// Manual wallet payments (Vodafone Cash, InstaPay, etc.) return true.
    /// </summary>
    public bool RequiresPaymentVerification { get; set; }

    /// <summary>
    /// Store tenant that owns the commercial order and accounting state.
    /// </summary>
    public Guid StoreTenantId { get; set; }

    /// <summary>
    /// Tenant that owns the affiliate identity, when the affiliate is store-scoped.
    /// Null means the affiliate identity is global/unscoped.
    /// </summary>
    public Guid? AffiliateAuthorityTenantId { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() =>
        AffiliateEventTenantScope.BuildAffectedTenantIds(StoreTenantId, AffiliateAuthorityTenantId);
}

/// <summary>
/// Emitted when commission is earned for an order (aggregated across items).
/// AF-19B: TenantId is Guid.Empty for global affiliates, StoreTenantId contains the store's tenant.
/// </summary>
public class AffiliateCommissionEarnedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public decimal TotalCommission { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public List<CommissionLineItem> Items { get; set; } = new();
    
    /// <summary>
    /// AF-19B: The store's tenant ID where the sale occurred (for accounting).
    /// This is ALWAYS set, even for global affiliates.
    /// </summary>
    public Guid StoreTenantId { get; set; }

    /// <summary>
    /// Tenant that owns the affiliate identity, when the affiliate is store-scoped.
    /// Null means the affiliate identity is global/unscoped.
    /// </summary>
    public Guid? AffiliateAuthorityTenantId { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() =>
        AffiliateEventTenantScope.BuildAffectedTenantIds(StoreTenantId, AffiliateAuthorityTenantId);
}

public class CommissionLineItem
{
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal CommissionAmount { get; set; }
}

/// <summary>
/// Emitted when commission is reversed (refund scenario).
/// AF-19C: Added StoreTenantId for correct MongoDB projection.
/// </summary>
public class AffiliateCommissionReversedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid AffiliateAccountId { get; set; }
    public Guid OriginalLedgerEntryId { get; set; }
    public Guid ReversalLedgerEntryId { get; set; }
    public decimal ReversedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    
    /// <summary>
    /// AF-19C: The store's tenant ID where the sale occurred.
    /// Used for MongoDB projection TenantId stamping.
    /// </summary>
    public Guid StoreTenantId { get; set; }

    /// <summary>
    /// Tenant that owns the affiliate identity, when the affiliate is store-scoped.
    /// Null means the affiliate identity is global/unscoped.
    /// </summary>
    public Guid? AffiliateAuthorityTenantId { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() =>
        AffiliateEventTenantScope.BuildAffectedTenantIds(StoreTenantId, AffiliateAuthorityTenantId);
}

// ═══════════════════════════════════════════════════════════════════
// SETTLEMENT EVENTS (AF-7)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Emitted when earned commissions for an order are approved (typically on delivery).
/// One event per order (aggregated across items, like EarnedEvent).
/// AF-19C: Added StoreTenantId for correct MongoDB projection.
/// </summary>
public class AffiliateCommissionApprovedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public decimal TotalApproved { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    
    /// <summary>
    /// AF-19C: The store's tenant ID where the sale occurred.
    /// Used for MongoDB projection TenantId stamping.
    /// </summary>
    public Guid StoreTenantId { get; set; }

    /// <summary>
    /// Tenant that owns the affiliate identity, when the affiliate is store-scoped.
    /// Null means the affiliate identity is global/unscoped.
    /// </summary>
    public Guid? AffiliateAuthorityTenantId { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() =>
        AffiliateEventTenantScope.BuildAffectedTenantIds(StoreTenantId, AffiliateAuthorityTenantId);
}

/// <summary>
/// Emitted when approved commissions for an order are marked as paid.
/// AF-19C: Added StoreTenantId for correct MongoDB projection.
/// </summary>
public class AffiliateCommissionPaidEvent : IntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public Guid SettlementReferenceId { get; set; }
    public decimal TotalPaid { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    
    /// <summary>
    /// AF-19C: The store's tenant ID where the sale occurred.
    /// Used for MongoDB projection TenantId stamping.
    /// </summary>
    public Guid StoreTenantId { get; set; }

    /// <summary>
    /// Tenant that owns the affiliate identity, when the affiliate is store-scoped.
    /// Null means the affiliate identity is global/unscoped.
    /// </summary>
    public Guid? AffiliateAuthorityTenantId { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() =>
        AffiliateEventTenantScope.BuildAffectedTenantIds(StoreTenantId, AffiliateAuthorityTenantId);
}

// ═══════════════════════════════════════════════════════════════════
// PRODUCT POLICY EVENTS (AF-4C)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Emitted when an affiliate product policy is enabled, disabled, or updated.
/// Consumers should update search index / MongoDB projections.
///
/// CRITICAL:
/// - CommissionRate and MaxCommissionPerItem are ONLY for backend consumers
/// - These MUST NOT be indexed in Elasticsearch or exposed in public APIs
/// - Only IsEnabled should be projected to search/storefront
/// </summary>
public class AffiliateProductPolicyChangedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public bool IsEnabled { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal? MaxCommissionPerItem { get; set; }
    public string TemplateType { get; set; } = PointOfSale.Core.Constants.LandingTemplates.ClassicV1;
    public Guid? AffiliateCategoryId { get; set; }
    public string? AffiliateCategoryName { get; set; }
    public string? AffiliateCategorySlug { get; set; }
    public string? AffiliateParentCategoryName { get; set; }
    public string? AffiliateParentCategorySlug { get; set; }
}

// ═══════════════════════════════════════════════════════════════════
// CATEGORY EVENTS (AF-30A)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// AF-30A: Emitted when an affiliate category is updated (name, slug, or parent).
/// Consumers should update MongoDB projections to maintain consistency.
///
/// PROJECTION STRATEGY:
/// - UpdateMany on AffiliateProductIntelligence collection
/// - Filter by AffiliateCategoryId
/// - Update category name/slug fields for all matching products
/// - Eventual consistency model (seconds delay acceptable)
///
/// IDEMPOTENCY:
/// - Event can be replayed safely
/// - UpdateMany is idempotent
/// </summary>
public class AffiliateCategoryUpdatedEvent : IntegrationEvent, IGlobalPlatformIntegrationEvent
{
    public Guid CategoryId { get; set; }
    public string NewName { get; set; } = string.Empty;
    public string NewSlug { get; set; } = string.Empty;
    public string? OldName { get; set; }
    public string? OldSlug { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentCategoryName { get; set; }
    public string? ParentCategorySlug { get; set; }
}

/// <summary>
/// AF-30A FIX P-2: Emitted when an affiliate category is deactivated.
/// Consumers should nullify category fields on matching products so they
/// are hidden from category-based discovery.
///
/// PROJECTION STRATEGY:
/// - UpdateMany on AffiliateProductIntelligence collection
/// - Filter by AffiliateCategoryId
/// - Nullify category name/slug fields (products remain, but lose category facet)
///
/// IDEMPOTENCY:
/// - Event can be replayed safely (setting null is idempotent)
/// </summary>
public class AffiliateCategoryDeactivatedEvent : IntegrationEvent, IGlobalPlatformIntegrationEvent
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategorySlug { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════════
// CLICK TRACKING & ANALYTICS EVENTS (AF-8)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Emitted when a storefront page is visited via an affiliate referral link (?ref=CODE).
///
/// RULES:
/// - No PII stored (ipHash is SHA-256 of IP, not the IP itself)
/// - No commission logic — analytics only
/// - Append-only: never updates, never deletes
/// - Idempotency key: (TenantId, SessionId, ProductId)
/// </summary>
public class AffiliateLinkClickedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public Guid? ProductId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? IpHash { get; set; }
    public string? UserAgent { get; set; }
}

// ═══════════════════════════════════════════════════════════════════
// ORDER ATTRIBUTION EVENT (AF-11)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// AF-11: Emitted by CheckoutService when an affiliate-referred order is created.
///
/// This event bridges the gap between order creation (API layer) and
/// attribution recording (Infrastructure/Consumer layer).
///
/// CONSUMER: AffiliateOrderAttributionConsumer
///   → calls AffiliateService.AttributeOrderToAffiliateAsync()
///   → creates AffiliateAttribution row
///   → emits AffiliateAttributedEvent (triggers projection consumers)
///
/// CRITICAL: Only emitted when AffiliateAccountId is present on the order
/// (i.e., checkout happened in Affiliate Mode with a valid referral).
/// </summary>
public class AffiliateOrderPlacedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public bool IsGuestCheckout { get; set; }

    /// <summary>
    /// AF-15: PromotionLinkId if the order was attributed via a promotion link (v2 flow).
    /// NULL for legacy v1 attributions.
    /// </summary>
    public Guid? PromotionLinkId { get; set; }

    /// <summary>
    /// AF-15: The global affiliate user (resolved from PromotionLink).
    /// NULL for legacy v1 attributions.
    /// </summary>
    public Guid? AffiliateUserId { get; set; }

    /// <summary>
    /// Store tenant that owns the commercial order, inventory, and accounting truth.
    /// </summary>
    public Guid StoreTenantId { get; set; }

    /// <summary>
    /// Tenant that owns the affiliate identity, when the affiliate is store-scoped.
    /// Null means the affiliate identity is global/unscoped.
    /// </summary>
    public Guid? AffiliateAuthorityTenantId { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() =>
        AffiliateEventTenantScope.BuildAffectedTenantIds(StoreTenantId, AffiliateAuthorityTenantId);
}

file static class AffiliateEventTenantScope
{
    public static IReadOnlyCollection<Guid> BuildAffectedTenantIds(Guid storeTenantId, Guid? affiliateAuthorityTenantId)
    {
        var tenantIds = new List<Guid>(capacity: 2);
        if (storeTenantId != Guid.Empty)
        {
            tenantIds.Add(storeTenantId);
        }

        if (affiliateAuthorityTenantId is { } affiliateTenantId &&
            affiliateTenantId != Guid.Empty &&
            affiliateTenantId != storeTenantId)
        {
            tenantIds.Add(affiliateTenantId);
        }

        return tenantIds;
    }
}
