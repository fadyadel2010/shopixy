using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

public static class ProductSearchDocumentMapper
{
    public static ProductSearchDocument MapFromProductDocument(
        ProductDocument product,
        Guid? eventId = null,
        bool? hasVariantsOverride = null)
    {
        var activeVariants = (product.Variants ?? new List<ProductVariantDocument>())
            .Where(v => v.IsActive)
            .ToList();

        int stockQuantity;
        bool isInStock;

        if (activeVariants.Count > 0)
        {
            stockQuantity = (int)activeVariants.Sum(v => v.TotalStock);
            isInStock = activeVariants.Any(v => v.TotalStock > 0);
        }
        else
        {
            stockQuantity = product.StockQuantity;
            isInStock = product.InStock;
        }

        var document = new ProductSearchDocument
        {
            TenantId = product.TenantId,
            ProductId = product.ProductId,
            ProductGeneration = product.ProductGeneration,
            IsTombstone = false,
            IsDeleted = false,
            ReadModelSchemaVersion = ProductSearchDocument.CurrentReadModelSchemaVersion,
            ProjectionVersion = ProductSearchDocument.CurrentProjectionVersion,
            ProjectionConvergedAtUtc = product.ProjectionConvergedAtUtc,
            EntityType = "product",
            Name = product.Name,
            NameAr = product.NameAr,
            SKU = product.SKU,
            Barcode = product.Barcode,
            CategoryId = product.CategoryId,
            CategoryName = product.CategoryName,
            Description = product.Description,
            Price = (double)product.StorefrontVisiblePrice,
            IsActive = product.IsActive,
            IsOnlineVisible = product.IsOnlineVisible,
            IsSearchable = product.IsSearchable,
            Slug = product.Slug,
            StorefrontLayout = product.ProductTemplate,
            IsFeatured = product.IsFeatured,
            CreatedAtUtc = product.CreatedAtUtc,
            IndexedAtUtc = DateTime.UtcNow,
            LastEventId = eventId ?? product.LastEventId,
            HasActivePromotion = product.HasActivePromotion,
            PromotionDiscountValue = product.PromotionDiscountValue.HasValue
                ? (double)product.PromotionDiscountValue.Value
                : 0,
            PromotionDiscountType = product.PromotionDiscountType,
            ActivePromotionId = product.ActivePromotionId,
            PromotionStartDate = product.PromotionStartDate,
            PromotionEndDate = product.PromotionEndDate,
            SearchBoostOverride = product.SearchBoostOverride,
            Brand = product.Brand,
            IsInStock = isInStock,
            StockQuantity = stockQuantity,
            HasVariants = hasVariantsOverride ?? product.HasVariants,
            VariantAttributes = activeVariants
                .SelectMany(variant => variant.Attributes ?? new List<VariantAttributeDocument>())
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Name) && !string.IsNullOrWhiteSpace(attribute.Value))
                .Select(attribute => NormalizeAttribute(attribute.Name, attribute.Value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList(),
            IsAffiliateEnabled = product.IsAffiliateEnabled,
            AffiliateEnabledAtUtc = product.AffiliateEnabledAtUtc,
            ImageUrl = GetPrimaryImageUrl(product)
        };

        document.BuildSearchText();
        return document;
    }

    private static string NormalizeAttribute(string name, string value) =>
        $"{name.Trim().ToLowerInvariant()}={value.Trim().ToLowerInvariant()}";

    public static string? GetPrimaryImageUrl(ProductDocument product)
    {
        var mediaImage = (product.Media ?? new List<ProductMediaDocument>())
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .ThenBy(media => media.Id)
            .Select(media => media.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

        if (!string.IsNullOrWhiteSpace(mediaImage))
        {
            return mediaImage;
        }

        if (string.IsNullOrWhiteSpace(product.Images))
        {
            return null;
        }

        if (product.Images.StartsWith("["))
        {
            try
            {
                var images = System.Text.Json.JsonSerializer.Deserialize<List<string>>(product.Images);
                return images?.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
            }
            catch
            {
                var commaIdx = product.Images.IndexOf(',');
                return commaIdx > 0
                    ? product.Images[..commaIdx].Trim()
                    : product.Images.Trim().TrimStart('[').TrimEnd(']');
            }
        }

        var fallbackCommaIdx = product.Images.IndexOf(',');
        return fallbackCommaIdx > 0
            ? product.Images[..fallbackCommaIdx].Trim()
            : product.Images.Trim();
    }
}
