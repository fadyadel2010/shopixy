using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.ProductAuthoring;

public sealed class ProductAuthoringCommandMapper : IProductAuthoringCommandMapper
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";

    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProductAuthoringCommandMapper(
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public ProductAuthoringCommand FromCreateRequest(
        ProductAuthoringOperation operation,
        Guid? productId,
        CreateProductRequest request,
        IReadOnlyList<ProductAuthoringVariantRequest>? variants) =>
        BuildProductAuthoringCommand(
            operation,
            productId,
            request.Name,
            request.NameAr,
            request.Description,
            request.DescriptionAr,
            request.Brand,
            request.Manufacturer,
            request.Tags,
            request.Features,
            request.Faqs,
            request.CategoryId,
            request.SKU,
            request.Barcode,
            request.PurchasePrice,
            request.SalePrice,
            request.OnlinePrice,
            request.PriceIncludesTax,
            request.TaxRate,
            request.PublishStatus,
            request.IsActive,
            request.IsOnlineVisible,
            request.IsSearchable,
            request.IsFeatured,
            request.Slug,
            request.MetaTitle,
            request.MetaDescription,
            request.MetaKeywords,
            request.Weight,
            request.Length,
            request.Width,
            request.Height,
            request.WeightUnit,
            request.DimensionUnit,
            request.Unit,
            request.MinimumStockLevel,
            request.TrackStock,
            request.HasVariants,
            request.LowStockThreshold,
            request.AllowBackorder,
            request.ProductUnits,
            variants,
            request.AiGenerated,
            request.AiMetadata,
            request.VideoUrl,
            request.ProductTemplate);

    public ProductAuthoringCommand FromUpdateRequest(
        ProductAuthoringOperation operation,
        Guid? productId,
        UpdateProductRequest request,
        IReadOnlyList<ProductAuthoringVariantRequest>? variants) =>
        BuildProductAuthoringCommand(
            operation,
            productId,
            request.Name,
            request.NameAr,
            request.Description,
            request.DescriptionAr,
            request.Brand,
            request.Manufacturer,
            request.Tags,
            request.Features,
            request.Faqs,
            request.CategoryId,
            request.SKU,
            request.Barcode,
            request.PurchasePrice,
            request.SalePrice,
            request.OnlinePrice,
            request.PriceIncludesTax,
            request.TaxRate,
            request.PublishStatus,
            request.IsActive,
            request.IsOnlineVisible,
            request.IsSearchable,
            request.IsFeatured,
            request.Slug,
            request.MetaTitle,
            request.MetaDescription,
            request.MetaKeywords,
            request.Weight,
            request.Length,
            request.Width,
            request.Height,
            request.WeightUnit,
            request.DimensionUnit,
            request.Unit,
            request.MinimumStockLevel,
            request.TrackStock,
            request.HasVariants,
            request.LowStockThreshold,
            request.AllowBackorder,
            request.ProductUnits,
            variants,
            request.AiGenerated,
            request.AiMetadata,
            request.VideoUrl,
            request.ProductTemplate);

    public ProductAuthoringCommand FromLifecycleProduct(
        ProductAuthoringOperation operation,
        Product product) =>
        new(
            operation,
            new ProductAuthoringIdentity(
                _tenantContext.RequiredTenantId,
                product.Id,
                product.CategoryId,
                product.SKU,
                product.Barcode),
            new ProductAuthoringContent(
                product.Name,
                product.NameAr,
                product.Description,
                product.DescriptionAr,
                product.Brand,
                product.Manufacturer,
                NormalizeTags(product.Tags),
                DeserializeStringList(product.FeaturesJson),
                DeserializeFaqs(product.FaqJson)
                    .Select(item => new ProductAuthoringFaq(item.Question, item.Answer))
                    .ToList()),
            new ProductAuthoringPricing(
                0m,
                0m,
                null,
                product.PriceIncludesTax,
                product.TaxRate),
            new ProductAuthoringPublication(
                PublishStatus.Archived,
                IsActive: false,
                IsOnlineVisible: false,
                product.IsSearchable,
                product.IsFeatured),
            new ProductAuthoringSeo(
                product.Slug,
                product.MetaTitle,
                product.MetaDescription,
                product.MetaKeywords),
            new ProductAuthoringShippingProfile(
                product.Weight,
                product.Length,
                product.Width,
                product.Height,
                product.WeightUnit,
                product.DimensionUnit),
            new ProductAuthoringInventoryProfile(
                product.Unit,
                product.MinimumStockLevel,
                product.TrackStock,
                product.HasVariants,
                product.LowStockThreshold,
                product.AllowBackorder),
            new ProductAuthoringUnits(null, null, Array.Empty<ProductAuthoringUnit>()),
            Array.Empty<ProductAuthoringVariant>(),
            new ProductAuthoringMetadata(
                product.AiGenerated,
                product.AiMetadataJson,
                product.VideoUrl,
                product.ProductTemplate,
                CurrentTraceIdentifier(),
                ReadIdempotencyKey(),
                ReadExpectedVersion(),
                new Dictionary<string, string>
                {
                    ["productUnitsSubmitted"] = "false",
                    ["variantsSubmitted"] = "false"
                }));

    public ProductAuthoringCommand FromProductUnitsUpdate(
        Product product,
        List<ProductAuthoringUnitRequest> productUnits)
    {
        var defaultUnit = productUnits.FirstOrDefault(unit => unit.IsDefault)
            ?? productUnits.FirstOrDefault();

        var request = new UpdateProductRequest(
            product.Name,
            product.NameAr,
            product.Barcode,
            product.SKU,
            product.Description,
            product.DescriptionAr,
            product.CategoryId,
            defaultUnit?.PurchasePrice ?? 0m,
            defaultUnit?.SellingPrice ?? 0m,
            null,
            product.PriceIncludesTax,
            product.TaxRate,
            product.Unit,
            product.MinimumStockLevel,
            product.Images,
            product.IsActive,
            product.TrackStock,
            product.HasVariants,
            product.Slug,
            product.MetaTitle,
            product.MetaDescription,
            product.MetaKeywords,
            product.Weight,
            product.Length,
            product.Width,
            product.Height,
            product.WeightUnit,
            product.DimensionUnit,
            (int)product.PublishStatus,
            product.IsOnlineVisible,
            product.IsSearchable,
            product.IsFeatured,
            product.LowStockThreshold,
            product.AllowBackorder,
            product.Brand,
            product.Manufacturer,
            product.Tags,
            DeserializeStringList(product.FeaturesJson),
            DeserializeFaqs(product.FaqJson),
            product.AiGenerated,
            string.IsNullOrWhiteSpace(product.AiMetadataJson)
                ? null
                : JsonSerializer.Deserialize<JsonElement>(product.AiMetadataJson),
            product.VideoUrl,
            product.ProductTemplate,
            productUnits,
            Variants: null);

        return FromUpdateRequest(
            ProductAuthoringOperation.Update,
            product.Id,
            request,
            variants: null);
    }

    private ProductAuthoringCommand BuildProductAuthoringCommand(
        ProductAuthoringOperation operation,
        Guid? productId,
        string name,
        string? nameAr,
        string? description,
        string? descriptionAr,
        string? brand,
        string? manufacturer,
        string? tags,
        List<string>? features,
        List<ProductFaqRequestItem>? faqs,
        Guid categoryId,
        string? sku,
        string? barcode,
        decimal purchasePrice,
        decimal salePrice,
        decimal? onlinePrice,
        bool priceIncludesTax,
        decimal taxRate,
        int publishStatus,
        bool isActive,
        bool isOnlineVisible,
        bool isSearchable,
        bool isFeatured,
        string? slug,
        string? metaTitle,
        string? metaDescription,
        string? metaKeywords,
        decimal? weight,
        decimal? length,
        decimal? width,
        decimal? height,
        string? weightUnit,
        string? dimensionUnit,
        string? unit,
        decimal minimumStockLevel,
        bool trackStock,
        bool hasVariants,
        int? lowStockThreshold,
        bool allowBackorder,
        List<ProductAuthoringUnitRequest>? productUnits,
        IReadOnlyList<ProductAuthoringVariantRequest>? variants,
        bool aiGenerated,
        JsonElement? aiMetadata,
        string? videoUrl,
        string? productTemplate)
    {
        var authoredUnits = ApplyLegacySimplePricingToDefaultUnit(
            NormalizeAuthoredProductUnits(productUnits),
            hasVariants,
            purchasePrice,
            salePrice);
        var unitItems = authoredUnits
            .Select(item => new ProductAuthoringUnit(
                item.ProductUnitId,
                item.UnitId,
                item.ConversionFactor,
                item.PurchasePrice,
                item.SellingPrice,
                item.Barcode,
                item.IsDefault,
                item.IsBaseUnit))
            .ToList();

        return new ProductAuthoringCommand(
            operation,
            new ProductAuthoringIdentity(
                _tenantContext.RequiredTenantId,
                productId,
                categoryId,
                sku,
                barcode),
            new ProductAuthoringContent(
                name,
                nameAr,
                description,
                descriptionAr,
                brand,
                manufacturer,
                NormalizeTags(tags),
                NormalizeStructuredList(features),
                NormalizeFaqs(faqs)
                    .Select(item => new ProductAuthoringFaq(item.Question, item.Answer))
                    .ToList()),
            new ProductAuthoringPricing(
                purchasePrice,
                salePrice,
                onlinePrice,
                priceIncludesTax,
                taxRate),
            new ProductAuthoringPublication(
                (PublishStatus)publishStatus,
                isActive,
                isOnlineVisible,
                isSearchable,
                isFeatured),
            new ProductAuthoringSeo(
                slug,
                metaTitle,
                metaDescription,
                metaKeywords),
            new ProductAuthoringShippingProfile(
                weight ?? 0m,
                length ?? 0m,
                width ?? 0m,
                height ?? 0m,
                weightUnit ?? "kg",
                dimensionUnit ?? "cm"),
            new ProductAuthoringInventoryProfile(
                unit ?? "Piece",
                minimumStockLevel,
                trackStock,
                hasVariants,
                lowStockThreshold ?? 10,
                allowBackorder),
            new ProductAuthoringUnits(
                unitItems.FirstOrDefault(item => item.IsBaseUnit)?.UnitId,
                unitItems.FirstOrDefault(item => item.IsDefault)?.UnitId,
                unitItems),
            (variants ?? Array.Empty<ProductAuthoringVariantRequest>())
                .Select(variant => new ProductAuthoringVariant(
                    variant.VariantId,
                    variant.Sku,
                    variant.Barcode,
                    variant.Price,
                    variant.OnlinePrice,
                    variant.CostPrice,
                    variant.IsActive,
                    variant.IsDefault,
                    variant.Attributes ?? new Dictionary<string, string>(),
                    variant.Weight,
                    variant.MediaId,
                    variant.ImageUrl))
                .ToList(),
            new ProductAuthoringMetadata(
                aiGenerated,
                SerializeJsonElement(aiMetadata),
                videoUrl,
                productTemplate,
                CurrentTraceIdentifier(),
                ReadIdempotencyKey(),
                ReadExpectedVersion(),
                new Dictionary<string, string>
                {
                    ["productUnitsSubmitted"] = productUnits is null ? "false" : "true",
                    ["variantsSubmitted"] = variants is null ? "false" : "true"
                }));
    }

    private string CurrentTraceIdentifier() =>
        _httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");

    private string? ReadIdempotencyKey()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return null;
        }

        if (request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey) &&
            !string.IsNullOrWhiteSpace(idempotencyKey.FirstOrDefault()))
        {
            return idempotencyKey.FirstOrDefault();
        }

        if (request.Headers.TryGetValue(RequestIdHeader, out var requestId) &&
            !string.IsNullOrWhiteSpace(requestId.FirstOrDefault()))
        {
            return requestId.FirstOrDefault();
        }

        return null;
    }

    private long? ReadExpectedVersion()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return null;
        }

        if (request.Headers.TryGetValue("If-Match", out var ifMatch) &&
            TryParseExpectedVersion(ifMatch.FirstOrDefault(), out var ifMatchVersion))
        {
            return ifMatchVersion;
        }

        if (request.Headers.TryGetValue("Expected-Version", out var expectedVersion) &&
            TryParseExpectedVersion(expectedVersion.FirstOrDefault(), out var headerVersion))
        {
            return headerVersion;
        }

        return null;
    }

    private static bool TryParseExpectedVersion(string? value, out long version)
    {
        version = 0;
        var normalized = value?.Trim().Trim('"');
        return long.TryParse(normalized, out version) && version >= 0 && version <= uint.MaxValue;
    }

    private static List<ProductAuthoringUnitRequest> NormalizeAuthoredProductUnits(
        List<ProductAuthoringUnitRequest>? units) =>
        units?
            .Where(unit => unit.UnitId != Guid.Empty)
            .GroupBy(unit => unit.UnitId)
            .Select(group => group.Last())
            .ToList() ?? new List<ProductAuthoringUnitRequest>();

    private static List<ProductAuthoringUnitRequest> ApplyLegacySimplePricingToDefaultUnit(
        List<ProductAuthoringUnitRequest> units,
        bool hasVariants,
        decimal purchasePrice,
        decimal salePrice)
    {
        if (hasVariants || units.Count == 0)
        {
            return units;
        }

        return units
            .Select(unit =>
                unit.IsDefault
                    ? unit with
                    {
                        PurchasePrice = unit.PurchasePrice ?? purchasePrice,
                        SellingPrice = unit.SellingPrice ?? salePrice
                    }
                    : unit)
            .ToList();
    }

    private static List<string> NormalizeStructuredList(IEnumerable<string>? values)
    {
        return values?
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();
    }

    private static List<string> NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return new List<string>();
        }

        return tags
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ProductFaqRequestItem> NormalizeFaqs(IEnumerable<ProductFaqRequestItem>? items)
    {
        return items?
            .Select(item => new ProductFaqRequestItem(
                item.Question.Trim(),
                item.Answer.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Question) || !string.IsNullOrWhiteSpace(item.Answer))
            .ToList()
            ?? new List<ProductFaqRequestItem>();
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return NormalizeStructuredList(JsonSerializer.Deserialize<List<string>>(json));
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<ProductFaqRequestItem> DeserializeFaqs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ProductFaqRequestItem>();
        }

        try
        {
            return NormalizeFaqs(JsonSerializer.Deserialize<List<ProductFaqRequestItem>>(json));
        }
        catch (JsonException)
        {
            return new List<ProductFaqRequestItem>();
        }
    }

    private static string? SerializeJsonElement(JsonElement? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : value.Value.GetRawText();
    }
}
