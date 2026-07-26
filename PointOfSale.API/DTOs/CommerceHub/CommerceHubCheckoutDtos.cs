using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.CommerceHub;

public class CommerceHubCheckoutRequestDto
{
    [StringLength(128)]
    public string? IdempotencyKey { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int? CartVersion { get; set; }

    [StringLength(64)]
    public string? PromoCode { get; set; }

    [Required]
    public CommerceHubCustomerInfoDto Customer { get; set; } = new();

    [Required(AllowEmptyStrings = false)]
    [StringLength(32)]
    public string PaymentMethod { get; set; } = string.Empty;

    [Required]
    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal? ShippingTotal { get; set; }

    [Required]
    [MinLength(1)]
    public List<CommerceHubCheckoutStoreRequestDto> Stores { get; set; } = new();

    [Required]
    [MinLength(1)]
    public List<CommerceHubCheckoutShippingSelectionRequestDto> ShippingSelections { get; set; } = new();

    public (string? Code, string? Message) Validate()
    {
        if (Customer == null)
        {
            return ("CUSTOMER_REQUIRED", "Customer information is required.");
        }

        var customerValidation = Customer.Validate();
        if (customerValidation.Code != null)
        {
            return customerValidation;
        }

        if (!CommerceHubPaymentMethods.IsSupportedForCheckout(PaymentMethod))
        {
            return ("PAYMENT_METHOD_INVALID", "Payment method must be COD, PAYMOB, or STRIPE.");
        }

        if (!CartVersion.HasValue)
        {
            return ("CART_VERSION_REQUIRED", "Checkout requires the latest server cart version.");
        }

        if (CartVersion.Value < 0)
        {
            return ("CART_VERSION_INVALID", "Cart version must be zero or greater.");
        }

        if (Stores == null || Stores.Count == 0)
        {
            return ("CART_EMPTY", "Your cart is empty.");
        }

        if (!ShippingTotal.HasValue || ShippingTotal.Value < 0)
        {
            return ("SHIPPING_TOTAL_INVALID", "Shipping total is required and must be zero or greater.");
        }

        if (ShippingSelections == null || ShippingSelections.Count == 0)
        {
            return ("SHIPPING_SELECTIONS_REQUIRED", "Shipping selections are required for checkout.");
        }

        var storeIds = new HashSet<Guid>();

        foreach (var store in Stores)
        {
            if (store.StoreId == Guid.Empty)
            {
                return ("STORE_INVALID", "Each store must have a valid identifier.");
            }

            storeIds.Add(store.StoreId);

            if (store.Items == null || store.Items.Count == 0)
            {
                return ("STORE_EMPTY", "Each store must include at least one item.");
            }

            foreach (var item in store.Items)
            {
                if (item.ListingId == Guid.Empty || item.ProductId == Guid.Empty || item.VariantId == Guid.Empty)
                {
                    return ("ITEM_INVALID", "Each checkout item must include valid listing, product, and variant identifiers.");
                }

                if (item.Quantity < 1)
                {
                    return ("QUANTITY_INVALID", "Each checkout item must have a quantity of at least 1.");
                }

                if (!item.ExpectedUnitPrice.HasValue || item.ExpectedUnitPrice.Value < 0)
                {
                    return ("EXPECTED_PRICE_REQUIRED", "Each checkout item must include the unit price shown to the customer.");
                }
            }
        }

        var shippingSelectionStoreIds = new HashSet<Guid>();

        foreach (var selection in ShippingSelections)
        {
            if (selection.StoreId == Guid.Empty || selection.ShippingOptionId == Guid.Empty)
            {
                return ("SHIPPING_SELECTION_INVALID", "Each shipping selection must include a valid store and shipping option.");
            }

            if (selection.ShippingPrice < 0)
            {
                return ("SHIPPING_SELECTION_INVALID", "Each shipping selection must include a shipping price that is zero or greater.");
            }

            shippingSelectionStoreIds.Add(selection.StoreId);
        }

        if (!storeIds.SetEquals(shippingSelectionStoreIds))
        {
            return ("SHIPPING_SELECTIONS_MISMATCH", "Shipping selections must be provided for every store in the cart.");
        }

        return (null, null);
    }
}

public class CommerceHubCheckoutShippingSelectionRequestDto : CommerceHubShippingSelectionRequestDto
{
    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal ShippingPrice { get; set; }
}

public class CommerceHubCustomerInfoDto
{
    private static readonly Regex PhonePattern = new(@"^\+?[0-9()\-\s]{8,20}$", RegexOptions.Compiled);

    [Required(AllowEmptyStrings = false)]
    [StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(32)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(200)]
    public string AddressLine { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(120)]
    public string City { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Area { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public (string? Code, string? Message) Validate()
    {
        if (string.IsNullOrWhiteSpace(FullName))
        {
            return ("FULL_NAME_REQUIRED", "Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            return ("PHONE_REQUIRED", "Phone number is required.");
        }

        if (!PhonePattern.IsMatch(PhoneNumber.Trim()))
        {
            return ("PHONE_INVALID", "Phone number format is invalid.");
        }

        if (string.IsNullOrWhiteSpace(AddressLine))
        {
            return ("ADDRESS_REQUIRED", "Address is required.");
        }

        if (string.IsNullOrWhiteSpace(City))
        {
            return ("CITY_REQUIRED", "City is required.");
        }

        return (null, null);
    }
}

public class CommerceHubCheckoutStoreRequestDto
{
    public Guid StoreId { get; set; }

    [Required]
    public List<CommerceHubCheckoutItemRequestDto> Items { get; set; } = new();
}

public class CommerceHubCheckoutItemRequestDto
{
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }

    [Range(1, CommerceHubCartLimits.MaxLineQuantity)]
    public int Quantity { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal? ExpectedUnitPrice { get; set; }
}

public class CommerceHubCheckoutResponseDto
{
    public Guid MarketplaceOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public bool IsIdempotentReplay { get; set; }
    public string Currency { get; set; } = "EGP";
    public string PaymentMethod { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentReferenceId { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal Total { get; set; }
    public string? PromoCode { get; set; }
    public decimal PromoDiscountTotal { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OrderAccessToken { get; set; }
    public List<CommerceHubCheckoutSubOrderDto> SubOrders { get; set; } = new();
    public CommerceHubCheckoutPaymentSessionDto? PaymentSession { get; set; }
}

public class CommerceHubCheckoutSubOrderDto
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
    public List<CommerceHubCheckoutSubOrderItemDto> Items { get; set; } = new();
}

public class CommerceHubCheckoutSubOrderItemDto
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

public class CommerceHubCheckoutPaymentSessionDto
{
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSimulated { get; set; }
    public string MerchantReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public static class CommerceHubPaymentMethods
{
    public const string Cod = "COD";
    public const string Paymob = "PAYMOB";
    public const string Stripe = "STRIPE";

    public static bool IsSupportedForCheckout(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return false;
        }

        return string.Equals(paymentMethod, Cod, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethod, Paymob, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethod, Stripe, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCod(string? paymentMethod) =>
        string.Equals(paymentMethod, Cod, StringComparison.OrdinalIgnoreCase);

    public static bool IsOnline(string? paymentMethod) =>
        IsSupportedForCheckout(paymentMethod) && !IsCod(paymentMethod);
}
