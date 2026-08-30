using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// Admin order list item DTO.
/// Used for paginated order lists in admin panel.
/// Phase 6.1: Admin Order Management
/// </summary>
public class AdminOrderListItemDto
{
    /// <summary>
    /// Current tenant/vendor authority evidence for frontend scope validation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Human-readable order number.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Customer ID (null for guest orders).
    /// </summary>
    public Guid? CustomerId { get; set; }
    
    /// <summary>
    /// Customer name (for display).
    /// </summary>
    public string? CustomerName { get; set; }
    
    /// <summary>
    /// Order total amount.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Backend-owned currency for the displayed order total.
    /// </summary>
    public string Currency { get; set; } = string.Empty;
    
    /// <summary>
    /// Current order status.
    /// </summary>
    public OrderStatus Status { get; set; }
    
    /// <summary>
    /// Current payment status.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; }

    /// <summary>
    /// Order source channel.
    /// </summary>
    public OrderSource Source { get; set; }

    /// <summary>
    /// Backend-owned external/source order identity when the order entered through another channel.
    /// </summary>
    public string? SourceOrderId { get; set; }

    /// <summary>
    /// Friendly source label for UI badges.
    /// </summary>
    public string? SourceLabel { get; set; }

    /// <summary>
    /// Backend-admitted affiliate attribution evidence for storefront-origin orders.
    /// Null when the order has no canonical affiliate attribution.
    /// </summary>
    public AdminAffiliateAttributionDto? AffiliateAttribution { get; set; }

    /// <summary>
    /// Backend-admitted CommerceHub Marketplace lineage evidence.
    /// Null for non-Marketplace orders.
    /// </summary>
    public AdminMarketplaceLineageDto? MarketplaceLineage { get; set; }

    /// <summary>
    /// True when this row represents a CommerceHub vendor sub-order.
    /// </summary>
    public bool IsMarketplaceOrder { get; set; }
    
    /// <summary>
    /// Order creation timestamp.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Admin order details DTO.
/// Used for viewing full order details including items, payments, and shipment.
/// Phase 6.1: Admin Order Management
/// </summary>
public class AdminOrderDetailsDto
{
    /// <summary>
    /// Current tenant/vendor authority evidence for frontend scope validation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Backend-issued optimistic concurrency evidence required by lifecycle mutations.
    /// </summary>
    public string RowVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable order number.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Current order status.
    /// </summary>
    public OrderStatus Status { get; set; }
    
    /// <summary>
    /// Current payment status.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; }

    /// <summary>
    /// Current authoritative fulfillment lifecycle status.
    /// </summary>
    public FulfillmentStatus? FulfillmentStatus { get; set; }
    
    /// <summary>
    /// Shipment status (null if no shipment exists).
    /// </summary>
    public ShipmentStatus? ShipmentStatus { get; set; }
    
    /// <summary>
    /// Order items.
    /// </summary>
    public List<AdminOrderItemDto> Items { get; set; } = new();

    /// <summary>
    /// Immutable checkout field definition/value snapshots captured at order creation.
    /// </summary>
    public List<AdminOrderCheckoutFieldSnapshotDto> CheckoutFieldSnapshots { get; set; } = new();
    
    /// <summary>
    /// Payment intents for this order.
    /// </summary>
    public List<AdminPaymentIntentDto> Payments { get; set; } = new();
    
    /// <summary>
    /// Shipment details (null if no shipment exists).
    /// </summary>
    public AdminShipmentDto? Shipment { get; set; }

    /// <summary>
    /// Backend-authoritative order commands available to the current POS admin
    /// route. The UI must not reconstruct these lifecycle rules from status.
    /// </summary>
    public List<AdminOrderActionCapabilityDto> AvailableOrderActions { get; set; } = new();

    /// <summary>
    /// Backend-authoritative shipment commands available from this order view.
    /// Shipment cancellation remains distinct from order cancellation.
    /// </summary>
    public List<AdminOrderActionCapabilityDto> AvailableShipmentActions { get; set; } = new();
    
    /// <summary>
    /// Order totals breakdown.
    /// </summary>
    public AdminOrderTotalsDto Totals { get; set; } = new();
    
    /// <summary>
    /// Customer information.
    /// </summary>
    public AdminOrderCustomerDto Customer { get; set; } = new();
    
    /// <summary>
    /// Order creation timestamp.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
    
    /// <summary>
    /// Order source (e.g., Ecommerce, Amazon).
    /// </summary>
    public OrderSource Source { get; set; }
    
    /// <summary>
    /// External order ID from source.
    /// </summary>
    public string? SourceOrderId { get; set; }

    /// <summary>
    /// Friendly source label for UI badges.
    /// </summary>
    public string? SourceLabel { get; set; }

    /// <summary>
    /// True when this record represents a CommerceHub vendor sub-order.
    /// </summary>
    public bool IsMarketplaceOrder { get; set; }

    /// <summary>
    /// Backend-admitted affiliate attribution evidence for storefront-origin orders.
    /// Null when the order has no canonical affiliate attribution.
    /// </summary>
    public AdminAffiliateAttributionDto? AffiliateAttribution { get; set; }

    /// <summary>
    /// Backend-admitted CommerceHub Marketplace lineage evidence.
    /// Null for non-Marketplace orders.
    /// </summary>
    public AdminMarketplaceLineageDto? MarketplaceLineage { get; set; }
    
    /// <summary>
    /// Customer notes.
    /// </summary>
    public string? CustomerNotes { get; set; }
    
    /// <summary>
    /// Internal admin notes.
    /// </summary>
    public string? AdminNotes { get; set; }

    /// <summary>
    /// Reason for order cancellation (if cancelled).
    /// </summary>
    public string? CancellationReason { get; set; }
    
    /// <summary>
    /// Shipping address details.
    /// </summary>
    public AdminAddressDto ShippingAddress { get; set; } = new();
    
    /// <summary>
    /// Billing address details (if different from shipping).
    /// </summary>
    public AdminAddressDto? BillingAddress { get; set; }
}

public sealed class AdminOrderActionCapabilityDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? TargetStatus { get; set; }
    public bool RequiresConfirmation { get; set; }
    public List<string> RequiredFields { get; set; } = new();
}

public class AdminOrderCheckoutFieldSnapshotDto
{
    public Guid SnapshotId { get; set; }
    public Guid FieldId { get; set; }
    public string FieldKind { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public string? SystemFieldKey { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public bool IsRequired { get; set; }
    public bool IsVisible { get; set; }
    public int DisplayOrder { get; set; }
    public int ConfigurationSchemaVersion { get; set; }

    public AdminOrderCheckoutFieldConfigurationPresentationDto Configuration { get; set; } = new();
    public AdminOrderCheckoutFieldSubmittedValueDto SubmittedValue { get; set; } = new();

    /// <summary>
    /// Backward-compatible raw snapshot configuration. New admin clients should use Configuration.
    /// </summary>
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>
    /// Backward-compatible raw snapshot value. New admin clients should use SubmittedValue.
    /// </summary>
    public string ValueJson { get; set; } = "null";
}

public sealed class AdminOrderCheckoutFieldConfigurationPresentationDto
{
    public int SchemaVersion { get; set; }
    public List<AdminOrderCheckoutFieldOptionDto> Options { get; set; } = new();
    public AdminOrderCheckoutFieldUploadPolicyDto? UploadPolicy { get; set; }
    public List<AdminOrderCheckoutFieldConditionalRuleDto> ConditionalRules { get; set; } = new();
    public bool HasAdditionalConfiguration { get; set; }
}

public sealed class AdminOrderCheckoutFieldOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class AdminOrderCheckoutFieldUploadPolicyDto
{
    public List<string> AllowedMimeTypes { get; set; } = new();
    public long? MaxFileSize { get; set; }
    public int? MaxFiles { get; set; }
}

public sealed class AdminOrderCheckoutFieldConditionalRuleDto
{
    public string RuleId { get; set; } = string.Empty;
    public string ConditionMode { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = new();
    public List<AdminOrderCheckoutFieldRuleConditionDto> Conditions { get; set; } = new();
}

public sealed class AdminOrderCheckoutFieldRuleConditionDto
{
    public Guid SourceFieldId { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string? ComparisonValue { get; set; }
}

public sealed class AdminOrderCheckoutFieldSubmittedValueDto
{
    public string Kind { get; set; } = "EMPTY";
    public string DisplayValue { get; set; } = "Not provided";
    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string? DateValue { get; set; }
    public string? OptionValue { get; set; }
    public string? OptionLabel { get; set; }
    public List<AdminOrderCheckoutAttachmentPresentationDto> Attachments { get; set; } = new();
}

public sealed class AdminOrderCheckoutAttachmentPresentationDto
{
    public Guid AttachmentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? MediaType { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public string? FinalETag { get; set; }
    public string? FinalChecksumSha256 { get; set; }
}

public sealed class AdminOrderAttachmentDownloadDto
{
    public Guid AttachmentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

/// <summary>
/// Affiliate attribution evidence admitted by the backend from SQL order and attribution authority.
/// </summary>
public class AdminAffiliateAttributionDto
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid AttributionId { get; set; }
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public Guid? PromotionLinkId { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime AttributedAtUtc { get; set; }
}

/// <summary>
/// CommerceHub Marketplace lineage evidence admitted from SQL marketplace order and vendor sub-order authority.
/// </summary>
public class AdminMarketplaceLineageDto
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid MarketplaceOrderId { get; set; }
    public string MarketplaceName { get; set; } = "CommerceHub";
    public string MarketplaceOrderNumber { get; set; } = string.Empty;
    public string MarketplaceSubOrderNumber { get; set; } = string.Empty;
    public Guid? ExternalCustomerId { get; set; }
    public Guid VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string MarketplaceSourceType { get; set; } = "Marketplace";
    public OrderStatus MarketplaceStatus { get; set; }
    public PaymentStatus MarketplacePaymentStatus { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Total { get; set; }
    public string? PaymentReferenceId { get; set; }
    public string? PaymentSnapshotJson { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? ShippingMethodName { get; set; }
    public string? ShippingSnapshotJson { get; set; }
    public string? CommissionSnapshotJson { get; set; }
}

/// <summary>
/// Order item DTO for admin view.
/// </summary>
public class AdminOrderItemDto
{
    public Guid ItemId { get; set; }
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public List<AdminOrderRefundEvidenceDto> Refunds { get; set; } = new();
}

public class AdminOrderRefundEvidenceDto
{
    public Guid RefundId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public CommerceHubRefundRequestStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RefundReasonCode ReasonCode { get; set; }
    public string? ReasonText { get; set; }
    public CommerceHubReturnDetailsDto? ReturnRequest { get; set; }
    public List<string> MediaUrls { get; set; } = new();
    public List<RefundEvidenceMediaDto> Media { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Payment intent DTO for admin view.
/// </summary>
public class AdminPaymentIntentDto
{
    public Guid PaymentIntentId { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentIntentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CapturedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Shipment DTO for admin view.
/// </summary>
public class AdminShipmentDto
{
    public Guid ShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; }
    public string ShippingMethodName { get; set; } = string.Empty;
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}

/// <summary>
/// Order totals breakdown.
/// </summary>
public class AdminOrderTotalsDto
{
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CouponCode { get; set; }
}

/// <summary>
/// Customer information for admin view.
/// </summary>
public class AdminOrderCustomerDto
{
    public Guid? CustomerId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsGuest { get; set; }
}

/// <summary>
/// Address DTO for admin view.
/// </summary>
public class AdminAddressDto
{
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
}

/// <summary>
/// Request DTO for updating order status (admin override).
/// </summary>
public class AdminUpdateOrderStatusRequest
{
    /// <summary>
    /// New order status.
    /// </summary>
    public OrderStatus NewStatus { get; set; }
    
    /// <summary>
    /// Reason for status change (required for some transitions).
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Response DTO for order status update.
/// </summary>
public class AdminOrderStatusUpdateResponse
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus OldStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Request DTO for cancelling an order.
/// </summary>
public class AdminCancelOrderRequest
{
    /// <summary>
    /// Cancellation reason (required).
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO for order cancellation.
/// </summary>
public class AdminOrderCancellationResponse
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus NewStatus { get; set; }
    public bool InventoryReleased { get; set; }
    public bool ShipmentCancelled { get; set; }
    
    /// <summary>
    /// Indicates whether payment cancellation has been confirmed/completed.
    /// This is false when cancellation is queued but not yet processed.
    /// </summary>
    public bool PaymentCancelled { get; set; }
    
    /// <summary>
    /// Indicates whether payment cancellation has been queued for background processing.
    /// When true, a PaymentCancellationOutboxMessage was written and will be processed asynchronously.
    /// </summary>
    public bool PaymentCancellationQueued { get; set; }
    
    public DateTime CancelledAtUtc { get; set; }
}

public class AdminConfirmReturnReceiptRequest
{
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? Notes { get; set; }
    public int? ResellableQuantity { get; set; }
    public int? DamagedQuantity { get; set; }
}

public class AdminReturnStatusUpdateRequest
{
    public DateTime? TimestampUtc { get; set; }
    public string? TrackingNumber { get; set; }
    public string? CourierName { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Query parameters for admin order list.
/// </summary>
public class AdminOrderListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public OrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Search { get; set; }
    public string SortBy { get; set; } = DefaultSortBy;
    public string SortDirection { get; set; } = DefaultSortDirection;

    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;
    public const string DefaultSortBy = "createdAt";
    public const string DefaultSortDirection = "desc";

    private static readonly HashSet<string> SupportedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt",
        "orderNumber",
        "customerName",
        "totalAmount",
        "status",
        "paymentStatus"
    };

    private static readonly HashSet<string> SupportedSortDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc",
        "desc"
    };

    /// <summary>
    /// Validates and returns error message if invalid.
    /// </summary>
    public string? Validate()
    {
        if (Page < MinPage)
            return $"Page must be at least {MinPage}";
        
        if (PageSize < MinPageSize || PageSize > MaxPageSize)
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}";

        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            return "FromDate cannot be after ToDate";

        if (!string.IsNullOrWhiteSpace(Search) && Search.Trim().Length > 100)
            return "Search must be 100 characters or fewer";

        if (!SupportedSortFields.Contains(SortBy))
            return "SortBy is not supported";

        if (!SupportedSortDirections.Contains(SortDirection))
            return "SortDirection must be asc or desc";

        return null;
    }
}

/// <summary>
/// Admin API error response.
/// </summary>
public class AdminApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TraceId { get; set; }

    public AdminApiErrorResponse() { }

    public AdminApiErrorResponse(string code, string message, string? traceId = null)
    {
        Code = code;
        Message = message;
        TraceId = traceId;
    }

    // Factory methods for common errors
    public static AdminApiErrorResponse TenantHeaderMissing(string? traceId = null) =>
        new("TENANT_HEADER_MISSING", "X-Tenant-Id header is required", traceId);

    public static AdminApiErrorResponse TenantHeaderInvalid(string? traceId = null) =>
        new("TENANT_HEADER_INVALID", "X-Tenant-Id must be a valid GUID", traceId);

    public static AdminApiErrorResponse OrderNotFound(string? traceId = null) =>
        new("ORDER_NOT_FOUND", "Order not found", traceId);

    public static AdminApiErrorResponse InvalidOrderState(string message, string? traceId = null) =>
        new("INVALID_ORDER_STATE", message, traceId);

    public static AdminApiErrorResponse OrderImmutable(string? traceId = null) =>
        new("ORDER_IMMUTABLE", "Delivered orders cannot be modified", traceId);

    public static AdminApiErrorResponse InternalError(string? traceId = null) =>
        new("INTERNAL_ERROR", "An internal error occurred", traceId);
}

/// <summary>
/// Paginated result wrapper for admin order list.
/// </summary>
public class AdminPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }

    public AdminPagedResult() { }

    public AdminPagedResult(List<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        HasNextPage = page < TotalPages;
        HasPreviousPage = page > 1;
    }
}
