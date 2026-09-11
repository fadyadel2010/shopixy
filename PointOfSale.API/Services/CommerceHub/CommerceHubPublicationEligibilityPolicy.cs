using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.CommerceHub;

public static class CommerceHubPublicationEligibilityPolicy
{
    public const string ProductDeletedMessage =
        "This product is deleted and cannot be published to CommerceHub.";
    public const string ProductInactiveMessage =
        "Activate this product in Products before publishing it to CommerceHub.";
    public const string ProductNotPublishedMessage =
        "Set this product's publish status to Published in Products before publishing it to CommerceHub.";
    public const string ProductNotOnlineVisibleMessage =
        "Enable online visibility for this product in Products before publishing it to CommerceHub.";
    public const string VariantDeletedMessage =
        "This product variant is deleted and cannot be published to CommerceHub.";
    public const string VariantInactiveMessage =
        "Activate this product variant in Products before publishing it to CommerceHub.";
    public const string VariantNotCurrentMessage =
        "This product variant has been replaced. Publish its current replacement variant instead.";

    public static string? GetBlockingMessage(ProductVariant variant)
    {
        if (variant.Product.IsDeleted)
        {
            return ProductDeletedMessage;
        }

        if (!variant.Product.IsActive)
        {
            return ProductInactiveMessage;
        }

        if (variant.Product.PublishStatus != PublishStatus.Published)
        {
            return ProductNotPublishedMessage;
        }

        if (!variant.Product.IsOnlineVisible)
        {
            return ProductNotOnlineVisibleMessage;
        }

        if (variant.IsDeleted)
        {
            return VariantDeletedMessage;
        }

        if (!variant.IsActive)
        {
            return VariantInactiveMessage;
        }

        if (variant.SemanticLifecycleState != ProductVariantSemanticLifecycleState.Current ||
            variant.SemanticSupersededAtUtc != null ||
            variant.SemanticReplacementVariantId != null)
        {
            return VariantNotCurrentMessage;
        }

        return null;
    }
}
