using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Application.ProductAuthoring;

public sealed record ProductAuthoringCommand(
    ProductAuthoringOperation Operation,
    ProductAuthoringIdentity Identity,
    ProductAuthoringContent Content,
    ProductAuthoringPricing Pricing,
    ProductAuthoringPublication Publication,
    ProductAuthoringSeo Seo,
    ProductAuthoringShippingProfile Shipping,
    ProductAuthoringInventoryProfile Inventory,
    ProductAuthoringUnits Units,
    IReadOnlyList<ProductAuthoringVariant> Variants,
    ProductAuthoringMetadata Metadata);

public enum ProductAuthoringOperation
{
    Create = 0,
    Update = 1,
    Archive = 2,
    Remove = 3
}

public sealed record ProductAuthoringIdentity(
    Guid TenantId,
    Guid? ProductId,
    Guid CategoryId,
    string? Sku,
    string? Barcode);

public sealed record ProductAuthoringContent(
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    string? Brand,
    string? Manufacturer,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Features,
    IReadOnlyList<ProductAuthoringFaq> Faqs);

public sealed record ProductAuthoringFaq(
    string Question,
    string Answer);

public sealed record ProductAuthoringPricing(
    decimal PurchasePrice,
    decimal SalePrice,
    decimal? OnlinePrice,
    bool PriceIncludesTax,
    decimal TaxRate);

public sealed record ProductAuthoringPublication(
    PublishStatus PublishStatus,
    bool IsActive,
    bool IsOnlineVisible,
    bool IsSearchable,
    bool IsFeatured);

public sealed record ProductAuthoringSeo(
    string? Slug,
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords);

public sealed record ProductAuthoringShippingProfile(
    decimal Weight,
    decimal Length,
    decimal Width,
    decimal Height,
    string WeightUnit,
    string DimensionUnit);

public sealed record ProductAuthoringInventoryProfile(
    string Unit,
    decimal MinimumStockLevel,
    bool TrackStock,
    bool HasVariants,
    int LowStockThreshold,
    bool AllowBackorder);

public sealed record ProductAuthoringUnits(
    Guid? BaseUnitId,
    Guid? DefaultUnitId,
    IReadOnlyList<ProductAuthoringUnit> Items);

public sealed record ProductAuthoringUnit(
    Guid? ProductUnitId,
    Guid UnitId,
    decimal ConversionFactor,
    decimal? PurchasePrice,
    decimal? SellingPrice,
    string? Barcode,
    bool IsDefault,
    bool IsBaseUnit);

public sealed record ProductAuthoringVariant(
    Guid? VariantId,
    string? Sku,
    string? Barcode,
    decimal Price,
    decimal? OnlinePrice,
    decimal CostPrice,
    bool IsActive,
    bool IsDefault,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record ProductAuthoringMetadata(
    bool AiGenerated,
    string? AiMetadataJson,
    string? VideoUrl,
    string? ProductTemplate,
    string CorrelationId,
    string? IdempotencyKey,
    long? ExpectedVersion,
    IReadOnlyDictionary<string, string> AdditionalMetadata);
