using System.ComponentModel.DataAnnotations;
using PointOfSale.API.DTOs;

namespace PointOfSale.API.DTOs.CommerceHub;

public static class CommerceHubCartLimits
{
    public const int MaxLineQuantity = 100;
    public const int MaxDistinctItems = 100;
}

public class CommerceHubCartUpsertRequestDto
{
    [Range(0, int.MaxValue)]
    public int Version { get; set; }

    [Required]
    [MinLength(1)]
    public List<CommerceHubCartSnapshotItemRequestDto> Items { get; set; } = null!;

    public (string? Code, string? Message) Validate()
    {
        if (Items == null || Items.Count == 0)
        {
            return ("INVALID_REQUEST", "Items collection is required and must not be empty.");
        }

        if (Version < 0)
        {
            return ("INVALID_VERSION", "Cart version must be zero or greater.");
        }

        return (null, null);
    }
}

public class CommerceHubCartSnapshotItemRequestDto
{
    public Guid? StoreId { get; set; }
    public Guid? ListingId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? VariantId { get; set; }

    [Range(1, CommerceHubCartLimits.MaxLineQuantity)]
    public int Quantity { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal? PriceSnapshot { get; set; }

    [StringLength(256)]
    public string? Name { get; set; }

    [StringLength(2048)]
    public string? ImageUrl { get; set; }
}

public class CommerceHubCartVariantAttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CommerceHubCartNoticeDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? StoreId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public int? RequestedQuantity { get; set; }
    public int? AppliedQuantity { get; set; }
}

public class CommerceHubCartStateItemDto
{
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal PriceSnapshot { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? VariantLabel { get; set; }
    public List<CommerceHubCartVariantAttributeDto> VariantAttributes { get; set; } = new();
    public int? MaxQuantity { get; set; }
    public bool IsInStock { get; set; }
    public string? InventoryNotice { get; set; }
}

public class CommerceHubCartStateDto
{
    public Guid? CartId { get; set; }
    public bool IsServerBacked { get; set; }
    public bool IsAuthenticatedOwner { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<CommerceHubCartStateItemDto> Items { get; set; } = new();
    public List<CommerceHubCartNoticeDto> Notices { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class CommerceHubShippingSelectionRequestDto
{
    public Guid StoreId { get; set; }
    public Guid ShippingOptionId { get; set; }
}

public class CommerceHubStoreShippingSelectionDto : CommerceHubShippingSelectionRequestDto
{
    public decimal ShippingPrice { get; set; }
    public string ShippingName { get; set; } = string.Empty;
}

public class CommerceHubCartPricingRequestDto
{
    [Required]
    public List<CommerceHubCartPricingStoreRequestDto> Stores { get; set; } = new();

    public List<CommerceHubShippingSelectionRequestDto> ShippingSelections { get; set; } = new();

    [StringLength(64)]
    public string? PromoCode { get; set; }

    public (string? Code, string? Message) Validate()
    {
        if (Stores == null || Stores.Count == 0)
        {
            return ("CART_EMPTY", "Your cart is empty.");
        }

        foreach (var store in Stores)
        {
            if (store.StoreId == Guid.Empty)
            {
                return ("STORE_INVALID", "Each store must have a valid identifier.");
            }

            if (store.Items == null || store.Items.Count == 0)
            {
                return ("STORE_EMPTY", "Each store must include at least one item.");
            }

            if (store.Items.Count > CommerceHubCartLimits.MaxDistinctItems)
            {
                return ("CART_TOO_LARGE", $"Each cart may contain up to {CommerceHubCartLimits.MaxDistinctItems} items.");
            }

            foreach (var item in store.Items)
            {
                if (item.ListingId == Guid.Empty || item.ProductId == Guid.Empty || item.VariantId == Guid.Empty)
                {
                    return ("ITEM_INVALID", "Each cart item must include valid listing, product, and variant identifiers.");
                }

                if (item.Quantity < 1 || item.Quantity > CommerceHubCartLimits.MaxLineQuantity)
                {
                    return ("INVALID_QUANTITY", $"Quantity must be between 1 and {CommerceHubCartLimits.MaxLineQuantity}.");
                }
            }
        }

        return (null, null);
    }
}

public class CommerceHubCartPricingStoreRequestDto
{
    public Guid StoreId { get; set; }

    [Required]
    public List<CommerceHubCartPricingItemRequestDto> Items { get; set; } = new();
}

public class CommerceHubCartPricingItemRequestDto
{
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }

    [Range(1, CommerceHubCartLimits.MaxLineQuantity)]
    public int Quantity { get; set; }
}

public class CommerceHubCartPricingResponseDto
{
    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal Total { get; set; }
    public CommerceHubCartPricingPromoDto? Promo { get; set; }
    public List<CommerceHubStoreShippingSelectionDto> StoreSelections { get; set; } = new();
    public List<CommerceHubCartPaymentMethodAvailabilityDto> PaymentMethods { get; set; } = new();
}

public class CommerceHubCartPricingPromoDto
{
    public string Code { get; set; } = string.Empty;
    public decimal DiscountTotal { get; set; }
    public bool IsApplied { get; set; }
}

public class CommerceHubCartPaymentMethodAvailabilityDto
{
    public string Code { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string? UnavailableReasonCode { get; set; }
    public string? UnavailableReason { get; set; }
}
