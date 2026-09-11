using System.ComponentModel.DataAnnotations;

using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.CommerceHub;

public class CommerceHubOrderDetailsDto
{
    public Guid MarketplaceOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string? Notes { get; set; }
    public string Currency { get; set; } = "EGP";
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public string? PaymentReferenceId { get; set; }
    public string? PaymentGateway { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool CanRetryPayment { get; set; }
    public string? PaymentRecoveryMessage { get; set; }
    public List<CommerceHubOrderSubOrderDto> SubOrders { get; set; } = new();
}

public class CommerceHubOrderSubOrderDto
{
    public Guid SubOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? ShippingMethodName { get; set; }
    public string? ShippingSnapshotJson { get; set; }
    public CommerceHubPaymentMethod PaymentMethod { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? PaymentSnapshotJson { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? PaymentReferenceId { get; set; }
    public DateTime? PaidAt { get; set; }
    public List<CommerceHubOrderItemDto> Items { get; set; } = new();
}

public class CommerceHubOrderItemDto
{
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? VariantAttributesJson { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

public class CommerceHubUpdateSubOrderStatusRequestDto
{
    public OrderStatus Status { get; set; }

    [Range(1, long.MaxValue)]
    public long? ExpectedVersion { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed class CommerceHubVendorOrderActionAuthorityDto
{
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
    public Guid MarketplaceOrderId { get; set; }
    public Guid SubOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus CurrentStatus { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public long LifecycleVersion { get; set; }
    public bool PaymentReady { get; set; }
    public bool InventoryReady { get; set; }
    public string? PaymentReadinessMessage { get; set; }
    public string? InventoryReadinessMessage { get; set; }
    public string? LifecycleMessage { get; set; }
    public List<CommerceHubVendorOrderActionDto> AvailableActions { get; set; } = new();
}

public sealed class CommerceHubVendorOrderActionDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public OrderStatus TargetStatus { get; set; }
    public bool RequiresReason { get; set; }
}

public class CommerceHubPaymentInitiateRequestDto
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
    public string OrderId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(32)]
    public string Provider { get; set; } = CommerceHubPaymentMethods.Paymob;

    [StringLength(256)]
    public string? OrderAccessToken { get; set; }

    public (string? Code, string? Message) Validate()
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(this);
        
        if (!Validator.TryValidateObject(this, context, results, validateAllProperties: true))
        {
            var firstError = results.FirstOrDefault();
            if (firstError != null)
            {
                // Map validation errors to appropriate error codes
                var memberName = firstError.MemberNames.FirstOrDefault();
                if (memberName == nameof(OrderId))
                {
                    // Distinguish between missing and invalid values
                    if (string.IsNullOrWhiteSpace(OrderId))
                    {
                        return ("ORDER_ID_REQUIRED", firstError.ErrorMessage ?? "Order ID or order number is required.");
                    }
                    else
                    {
                        return ("ORDER_ID_INVALID", firstError.ErrorMessage ?? "Order ID format is invalid.");
                    }
                }
                if (memberName == nameof(Provider))
                {
                    // Distinguish between missing and invalid values
                    if (string.IsNullOrWhiteSpace(Provider))
                    {
                        return ("PAYMENT_PROVIDER_REQUIRED", firstError.ErrorMessage ?? "Payment provider is required.");
                    }
                    else
                    {
                        return ("PAYMENT_PROVIDER_INVALID", firstError.ErrorMessage ?? "Payment provider format is invalid.");
                    }
                }
                
                // Generic validation error
                return ("VALIDATION_ERROR", firstError.ErrorMessage ?? "Validation failed.");
            }
        }

        if (!CommerceHubPaymentMethods.IsOnline(Provider))
        {
            return ("PAYMENT_PROVIDER_INVALID", "Payment provider must be PAYMOB or STRIPE.");
        }

        return (null, null);
    }
}

public class CommerceHubPaymentInitiateResponseDto
{
    public Guid PaymentId { get; set; }
    public Guid MarketplaceOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string HandoffType { get; set; } = "redirect_url";
    public string? PaymentUrl { get; set; }
    public string? PaymentToken { get; set; }
    public string? PublishableKey { get; set; }
    public string ProviderReference { get; set; } = string.Empty;
    public string? ProviderIntentId { get; set; }
    public bool IsReusable { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CommerceHubPaymentWebhookResultDto
{
    public bool Processed { get; set; }
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates whether the webhook should be retried by the payment provider.
    /// When true, the controller returns HTTP 500 to signal the provider to retry.
    /// When false, the controller returns HTTP 200 to acknowledge receipt without retry.
    /// </summary>
    public bool ShouldRetry { get; set; }
    
    public Guid? PaymentId { get; set; }
    public Guid? MarketplaceOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string? Provider { get; set; }
    public string? ProviderReference { get; set; }
    public string? PaymentReferenceId { get; set; }
    public string? TransactionId { get; set; }
}

public class CommerceHubPaymentStatusDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public bool IsPending { get; set; }
    public bool RequiresAction { get; set; }
    public string? FailureReason { get; set; }
    public string? TransactionId { get; set; }
}

public class CommerceHubPaymentConfigurationStatusDto
{
    public string Provider { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConfigured { get; set; }
    public bool IsSandbox { get; set; }
    public string? ValidationMessage { get; set; }
}

public class CommerceHubPaymentCallbackResultDto
{
    public bool IsValid { get; set; }
    public bool IsSuccess { get; set; }
    public bool IsTerminalFailure { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public string? StorefrontBaseUrl { get; set; }
    public string? TransactionId { get; set; }
    public string? Message { get; set; }
}

public class CommerceHubVendorEarningsResponseDto
{
    public Guid VendorId { get; set; }
    public decimal PendingTotal { get; set; }
    public decimal AvailableTotal { get; set; }
    public decimal PaidOutTotal { get; set; }
    public decimal InPayoutTotal { get; set; }
    public decimal ReversedTotal { get; set; }
    public decimal TotalEarnings { get; set; }
    public decimal TotalPayable { get; set; }
    public decimal EligiblePayoutAmount { get; set; }
    public decimal HoldbackAmount { get; set; }
    public decimal CompletedPayoutsTotal { get; set; }
    public decimal PendingPayoutsTotal { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime? LastPayoutAtUtc { get; set; }
    public CommerceHubVendorGovernanceStatusDto Governance { get; set; } = new();
    public List<string> RiskFlags { get; set; } = new();
    public List<CommerceHubPayoutDto> PayoutHistory { get; set; } = new();
    public List<CommerceHubVendorEarningItemDto> Items { get; set; } = new();
}

public class CommerceHubVendorGovernanceStatusDto
{
    public string Status { get; set; } = "Active";
    public string OperationalMode { get; set; } = "Active";
    public bool ProductsHidden { get; set; }
    public bool BlocksNewOrders { get; set; }
    public bool BlocksListingChanges { get; set; }
    public bool BlocksPayouts { get; set; }
    public bool BlocksLogin { get; set; }
    public string? Reason { get; set; }
    public string? BannerMessage { get; set; }
}

public class CommerceHubVendorEarningItemDto
{
    public Guid TenantId { get; set; }
    public Guid EarningId { get; set; }
    public Guid SubOrderId { get; set; }
    public Guid? OriginalEarningId { get; set; }
    public Guid? RefundId { get; set; }
    public Guid? PayoutId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? AvailableAtUtc { get; set; }
    public DateTime? PaidOutAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
}
