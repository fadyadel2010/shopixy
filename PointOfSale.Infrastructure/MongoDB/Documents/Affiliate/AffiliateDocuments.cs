using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

/// <summary>
/// AF-3: MongoDB read model for the Affiliate Portal dashboard.
///
/// Updated via:
/// - AffiliateAttributedEvent    → initial creation (ensures document exists)
/// - AffiliateCommissionEarnedEvent  → increments TotalEarned, TotalOrders
/// - AffiliateCommissionReversedEvent → increments TotalReversed, adjusts net
///
/// IDEMPOTENCY: ProcessedEventIds tracks which EventIds have been applied.
/// Duplicate events are silently skipped.
///
/// INDEXES: (TenantId, AffiliateAccountId) unique compound index.
/// </summary>
public class AffiliateDashboardDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("affiliateAccountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AffiliateAccountId { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonElement("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [BsonElement("referralCode")]
    public string ReferralCode { get; set; } = string.Empty;

    [BsonElement("totalEarned")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalEarned { get; set; }

    [BsonElement("totalReversed")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalReversed { get; set; }

    [BsonElement("netEarnings")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal NetEarnings { get; set; }

    [BsonElement("totalOrders")]
    public int TotalOrders { get; set; }

    [BsonElement("totalApproved")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalApproved { get; set; }

    [BsonElement("totalPaid")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalPaid { get; set; }

    [BsonElement("lastCommissionAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastCommissionAtUtc { get; set; }

    [BsonElement("updatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Idempotency guard for dashboard counter mutations.
    /// </summary>
    [BsonElement("processedEventIds")]
    [BsonIgnoreIfNull]
    public List<string>? ProcessedEventIds { get; set; }
}

/// <summary>
/// AF-3: MongoDB read model for per-order commission details.
///
/// Updated via:
/// - AffiliateCommissionEarnedEvent  → creates/upserts with items
/// - AffiliateCommissionReversedEvent → marks item as reversed, adjusts totals
///
/// Status transitions: AttributedPendingPayment → Earned → PartiallyReversed → Reversed
/// </summary>
public class AffiliateOrderDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.String)]
    public Guid OrderId { get; set; }

    [BsonElement("orderNumber")]
    public string OrderNumber { get; set; } = string.Empty;

    [BsonElement("affiliateAccountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AffiliateAccountId { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = string.Empty;

    [BsonElement("totalCommission")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalCommission { get; set; }

    [BsonElement("totalReversed")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalReversed { get; set; }

    [BsonElement("totalApproved")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalApproved { get; set; }

    [BsonElement("totalPaid")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalPaid { get; set; }

    /// <summary>
    /// AttributedPendingPayment | Earned | PartiallyReversed | Reversed | Approved | Paid
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "Earned";

    // ═══════════════════════════════════════════════════════════════════
    // AF-21: ORDER LIFECYCLE STATUS FIELDS
    // Populated by OrderLifecycleAffiliateProjectionConsumer
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-21: Order status from POS Admin (Pending, Confirmed, Completed, Cancelled, etc.)
    /// Updated via OrderStatusChangedEvent, OrderCompletedEvent, OrderCancelledEvent.
    /// </summary>
    [BsonElement("orderStatus")]
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>
    /// AF-21: Fulfillment status from POS Admin (Created, Confirmed, Packed, OutForDelivery, Delivered, Completed, etc.)
    /// Updated via FulfillmentDeliveredEvent, OrderCompletedEvent, OrderFulfillmentCancelledEvent.
    /// </summary>
    [BsonElement("fulfillmentStatus")]
    public string FulfillmentStatus { get; set; } = string.Empty;

    [BsonElement("lastOrderStatusEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastOrderStatusEventOccurredAtUtc { get; set; }

    [BsonElement("lastOrderStatusEventVersion")]
    public int LastOrderStatusEventVersion { get; set; }

    [BsonElement("lastFulfillmentStatusEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastFulfillmentStatusEventOccurredAtUtc { get; set; }

    [BsonElement("lastFulfillmentStatusEventVersion")]
    public int LastFulfillmentStatusEventVersion { get; set; }

    [BsonElement("lastPaymentStatusEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastPaymentStatusEventOccurredAtUtc { get; set; }

    [BsonElement("lastPaymentStatusEventVersion")]
    public int LastPaymentStatusEventVersion { get; set; }

    [BsonElement("lastCommissionEarnedEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastCommissionEarnedEventOccurredAtUtc { get; set; }

    [BsonElement("lastCommissionEarnedEventVersion")]
    public int LastCommissionEarnedEventVersion { get; set; }

    [BsonElement("lastCommissionReversedEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastCommissionReversedEventOccurredAtUtc { get; set; }

    [BsonElement("lastCommissionReversedEventVersion")]
    public int LastCommissionReversedEventVersion { get; set; }

    [BsonElement("lastCommissionApprovedEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastCommissionApprovedEventOccurredAtUtc { get; set; }

    [BsonElement("lastCommissionApprovedEventVersion")]
    public int LastCommissionApprovedEventVersion { get; set; }

    [BsonElement("lastCommissionPaidEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastCommissionPaidEventOccurredAtUtc { get; set; }

    [BsonElement("lastCommissionPaidEventVersion")]
    public int LastCommissionPaidEventVersion { get; set; }

    /// <summary>
    /// AF-21: Payment method code (COD, Card, Wallet, etc.)
    /// Stamped from OrderCreatedEvent.PaymentMethodCode.
    /// </summary>
    [BsonElement("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// AF-21: Total amount paid by customer (from payment/commission projections).
    /// This is the order total, NOT the commission amount.
    /// Attribution placeholders keep this at 0 until payment is actually captured.
    /// </summary>
    [BsonElement("paidAmount")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal PaidAmount { get; set; }

    [BsonElement("items")]
    public List<AffiliateOrderItemSubDocument> Items { get; set; } = new();

    [BsonElement("createdAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("updatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Idempotency guard.
    /// </summary>
    [BsonElement("processedEventIds")]
    public List<string> ProcessedEventIds { get; set; } = new();
}

public class AffiliateOrderItemSubDocument
{
    [BsonElement("orderItemId")]
    [BsonRepresentation(BsonType.String)]
    public Guid OrderItemId { get; set; }

    [BsonElement("productId")]
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonElement("productName")]
    public string ProductName { get; set; } = string.Empty;

    [BsonElement("variantName")]
    public string? VariantName { get; set; }

    [BsonElement("paidAmount")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal PaidAmount { get; set; }

    [BsonElement("commissionAmount")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal CommissionAmount { get; set; }

    [BsonElement("isReversed")]
    public bool IsReversed { get; set; }

    /// <summary>
    /// Settlement state: Earned | Approved | Paid | Reversed
    /// </summary>
    [BsonElement("settlementStatus")]
    public string SettlementStatus { get; set; } = "Earned";
}

/// <summary>
/// AF-3: MongoDB read model for per-affiliate, per-currency earnings summary.
///
/// Separate from dashboard to handle multi-currency tenants cleanly.
/// Key: (AffiliateAccountId, TenantId, Currency)
/// </summary>
public class AffiliateEarningsSummaryDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("affiliateAccountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AffiliateAccountId { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = string.Empty;

    [BsonElement("totalEarned")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalEarned { get; set; }

    [BsonElement("totalReversed")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalReversed { get; set; }

    [BsonElement("netEarnings")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal NetEarnings { get; set; }

    [BsonElement("totalApproved")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalApproved { get; set; }

    [BsonElement("totalPaid")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalPaid { get; set; }

    [BsonElement("updatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Idempotency guard for summary counter mutations.
    /// </summary>
    [BsonElement("processedEventIds")]
    [BsonIgnoreIfNull]
    public List<string>? ProcessedEventIds { get; set; }
}
