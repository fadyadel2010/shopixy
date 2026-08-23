using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class ProductAuthoringCommandMapperCharacterizationTests
{
    [Fact]
    public void CreateRequest_WithSubmittedUnitsVariantsAndHeaders_MapsIdenticalAuthoringIntent()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var firstUnitId = Guid.NewGuid();
        var secondUnitId = Guid.NewGuid();
        var winningProductUnitId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        using var aiMetadata = JsonDocument.Parse("{\"source\":\"ai\",\"score\":9}");
        var mapper = CreateMapper(tenantId, "trace-create", idempotencyKey: "idem-create", ifMatch: "\"123\"");

        var request = new CreateProductRequest(
            Name: "Mapped Product",
            NameAr: "Arabic product",
            Barcode: "BAR-1",
            SKU: "SKU-1",
            Description: "Description",
            DescriptionAr: "Arabic description",
            CategoryId: categoryId,
            PurchasePrice: 4m,
            SalePrice: 10m,
            OnlinePrice: 9m,
            PriceIncludesTax: true,
            TaxRate: 14m,
            Unit: "Box",
            MinimumStockLevel: 3m,
            Images: "ignored-by-authoring-mapper",
            IsActive: true,
            TrackStock: false,
            HasVariants: true,
            Slug: "mapped-product",
            MetaTitle: "Meta title",
            MetaDescription: "Meta description",
            MetaKeywords: "meta,keywords",
            Weight: 1.5m,
            Length: 2.5m,
            Width: 3.5m,
            Height: 4.5m,
            WeightUnit: "lb",
            DimensionUnit: "in",
            PublishStatus: (int)PublishStatus.Published,
            IsOnlineVisible: true,
            IsSearchable: false,
            IsFeatured: true,
            LowStockThreshold: 7,
            AllowBackorder: true,
            Brand: "Brand",
            Manufacturer: "Manufacturer",
            Tags: "alpha; beta, Alpha\n gamma",
            Features: [" fast ", "", "FAST", "durable"],
            Faqs:
            [
                new ProductFaqRequestItem(" Question ", " Answer "),
                new ProductFaqRequestItem(" ", " ")
            ],
            AiGenerated: true,
            AiMetadata: aiMetadata.RootElement,
            VideoUrl: "https://video.example/product",
            ProductTemplate: "template-a",
            ProductUnits:
            [
                new ProductAuthoringUnitRequest(Guid.NewGuid(), firstUnitId, 2m, 1m, 2m, "old", false, false),
                new ProductAuthoringUnitRequest(null, Guid.Empty, 9m, 9m, 9m, "ignored", true, true),
                new ProductAuthoringUnitRequest(winningProductUnitId, firstUnitId, 1m, 4m, 10m, "unit-bar", true, true),
                new ProductAuthoringUnitRequest(Guid.NewGuid(), secondUnitId, 6m, 5m, 12m, null, false, false)
            ],
            Variants:
            [
                new ProductAuthoringVariantRequest(variantId, "VAR-1", "VBAR-1", 12m, 11m, 6m, true, true, new Dictionary<string, string>
                {
                    ["Color"] = "Red"
                }),
                new ProductAuthoringVariantRequest(null, "VAR-2", null, 13m, null, 7m, false, false, null)
            ]);

        var command = mapper.FromCreateRequest(ProductAuthoringOperation.Create, productId, request, request.Variants);

        Assert.Equal(ProductAuthoringOperation.Create, command.Operation);
        Assert.Equal(tenantId, command.Identity.TenantId);
        Assert.Equal(productId, command.Identity.ProductId);
        Assert.Equal(categoryId, command.Identity.CategoryId);
        Assert.Equal("SKU-1", command.Identity.Sku);
        Assert.Equal("BAR-1", command.Identity.Barcode);
        Assert.Equal(["alpha", "beta", "gamma"], command.Content.Tags);
        Assert.Equal(["fast", "durable"], command.Content.Features);
        var faq = Assert.Single(command.Content.Faqs);
        Assert.Equal("Question", faq.Question);
        Assert.Equal("Answer", faq.Answer);
        Assert.Equal(PublishStatus.Published, command.Publication.PublishStatus);
        Assert.True(command.Publication.IsOnlineVisible);
        Assert.False(command.Publication.IsSearchable);
        Assert.True(command.Publication.IsFeatured);
        Assert.Equal("lb", command.Shipping.WeightUnit);
        Assert.Equal("in", command.Shipping.DimensionUnit);
        Assert.Equal("Box", command.Inventory.Unit);
        Assert.False(command.Inventory.TrackStock);
        Assert.True(command.Inventory.HasVariants);
        Assert.Equal(7, command.Inventory.LowStockThreshold);
        Assert.True(command.Inventory.AllowBackorder);
        Assert.Equal(firstUnitId, command.Units.BaseUnitId);
        Assert.Equal(firstUnitId, command.Units.DefaultUnitId);
        Assert.Equal(2, command.Units.Items.Count);
        var defaultUnit = command.Units.Items.Single(unit => unit.UnitId == firstUnitId);
        Assert.Equal(winningProductUnitId, defaultUnit.ProductUnitId);
        Assert.Equal(1m, defaultUnit.ConversionFactor);
        Assert.True(defaultUnit.IsBaseUnit);
        Assert.True(defaultUnit.IsDefault);
        Assert.Equal(2, command.Variants.Count);
        Assert.Equal(variantId, command.Variants[0].VariantId);
        Assert.True(command.Variants[0].IsDefault);
        Assert.Equal("Red", command.Variants[0].Attributes["Color"]);
        Assert.Empty(command.Variants[1].Attributes);
        Assert.True(command.Metadata.AiGenerated);
        Assert.Equal("{\"source\":\"ai\",\"score\":9}", command.Metadata.AiMetadataJson);
        Assert.Equal("trace-create", command.Metadata.CorrelationId);
        Assert.Equal("idem-create", command.Metadata.IdempotencyKey);
        Assert.Equal(123, command.Metadata.ExpectedVersion);
        Assert.Equal("true", command.Metadata.AdditionalMetadata["productUnitsSubmitted"]);
        Assert.Equal("true", command.Metadata.AdditionalMetadata["variantsSubmitted"]);
    }

    [Fact]
    public void UpdateRequest_WithOmittedCollectionsAndFallbackHeaders_PreservesOmittedFieldSemantics()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var mapper = CreateMapper(tenantId, "trace-update", requestId: "request-id", expectedVersion: "456");
        var request = new UpdateProductRequest(
            Name: "Draft Product",
            NameAr: null,
            Barcode: null,
            SKU: null,
            Description: null,
            DescriptionAr: null,
            CategoryId: categoryId,
            PurchasePrice: 1m,
            SalePrice: 2m,
            OnlinePrice: null,
            PriceIncludesTax: false,
            TaxRate: 0m,
            Unit: null,
            MinimumStockLevel: 0m,
            Images: null,
            IsActive: false,
            TrackStock: true,
            HasVariants: false,
            PublishStatus: (int)PublishStatus.Draft,
            IsOnlineVisible: false,
            ProductUnits: null,
            Variants: null);

        var command = mapper.FromUpdateRequest(ProductAuthoringOperation.Update, productId, request, variants: null);

        Assert.Equal(ProductAuthoringOperation.Update, command.Operation);
        Assert.Equal(productId, command.Identity.ProductId);
        Assert.Equal(PublishStatus.Draft, command.Publication.PublishStatus);
        Assert.False(command.Publication.IsOnlineVisible);
        Assert.Equal(0m, command.Shipping.Weight);
        Assert.Equal(0m, command.Shipping.Length);
        Assert.Equal(0m, command.Shipping.Width);
        Assert.Equal(0m, command.Shipping.Height);
        Assert.Equal("kg", command.Shipping.WeightUnit);
        Assert.Equal("cm", command.Shipping.DimensionUnit);
        Assert.Equal("Piece", command.Inventory.Unit);
        Assert.Equal(10, command.Inventory.LowStockThreshold);
        Assert.Empty(command.Units.Items);
        Assert.Null(command.Units.BaseUnitId);
        Assert.Null(command.Units.DefaultUnitId);
        Assert.Empty(command.Variants);
        Assert.Equal("request-id", command.Metadata.IdempotencyKey);
        Assert.Equal(456, command.Metadata.ExpectedVersion);
        Assert.Equal("false", command.Metadata.AdditionalMetadata["productUnitsSubmitted"]);
        Assert.Equal("false", command.Metadata.AdditionalMetadata["variantsSubmitted"]);
    }

    [Fact]
    public void LifecycleProduct_MapsArchiveIntentAndMarksCollectionsOmitted()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var mapper = CreateMapper(tenantId, "trace-lifecycle", idempotencyKey: "idem-life", ifMatch: "9");
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Lifecycle Product",
            NameAr = "Arabic name",
            Description = "Description",
            DescriptionAr = "Arabic description",
            Brand = "Brand",
            Manufacturer = "Maker",
            CategoryId = categoryId,
            SKU = "SKU-LIFE",
            Barcode = "BAR-LIFE",
            Tags = "one,two,one",
            FeaturesJson = JsonSerializer.Serialize(new[] { " feature ", "FEATURE", "other" }),
            FaqJson = JsonSerializer.Serialize(new[] { new ProductFaqRequestItem(" Q ", " A ") }),
            PurchasePrice = 3m,
            SalePrice = 8m,
            OnlinePrice = 7m,
            PriceIncludesTax = true,
            TaxRate = 5m,
            PublishStatus = PublishStatus.Published,
            IsActive = true,
            IsOnlineVisible = true,
            IsSearchable = false,
            IsFeatured = true,
            Unit = "Carton",
            MinimumStockLevel = 4m,
            TrackStock = false,
            HasVariants = true,
            LowStockThreshold = 6,
            AllowBackorder = true,
            Slug = "life",
            MetaTitle = "meta",
            MetaDescription = "desc",
            MetaKeywords = "keys",
            Weight = 1m,
            Length = 2m,
            Width = 3m,
            Height = 4m,
            WeightUnit = "lb",
            DimensionUnit = "in",
            AiGenerated = true,
            AiMetadataJson = "{\"kept\":true}",
            VideoUrl = "video",
            ProductTemplate = "template"
        };

        var command = mapper.FromLifecycleProduct(ProductAuthoringOperation.Remove, product);

        Assert.Equal(ProductAuthoringOperation.Remove, command.Operation);
        Assert.Equal(productId, command.Identity.ProductId);
        Assert.Equal(PublishStatus.Archived, command.Publication.PublishStatus);
        Assert.False(command.Publication.IsActive);
        Assert.False(command.Publication.IsOnlineVisible);
        Assert.Equal(["one", "two"], command.Content.Tags);
        Assert.Equal(["feature", "other"], command.Content.Features);
        Assert.Single(command.Content.Faqs);
        Assert.Empty(command.Units.Items);
        Assert.Empty(command.Variants);
        Assert.Equal("idem-life", command.Metadata.IdempotencyKey);
        Assert.Equal(9, command.Metadata.ExpectedVersion);
        Assert.Equal("false", command.Metadata.AdditionalMetadata["productUnitsSubmitted"]);
        Assert.Equal("false", command.Metadata.AdditionalMetadata["variantsSubmitted"]);
        Assert.Equal("{\"kept\":true}", command.Metadata.AiMetadataJson);
    }

    [Fact]
    public void LifecycleProduct_WithPostgresJsonbLowercaseFaqKeys_ReconstructsPersistedFaqs()
    {
        var tenantId = Guid.NewGuid();
        var mapper = CreateMapper(tenantId, "trace-jsonb-faq", idempotencyKey: "idem-jsonb-faq");
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Lifecycle product with persisted FAQs",
            CategoryId = Guid.NewGuid(),
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            FaqJson = "[{\"answer\":\" Persisted answer \",\"question\":\" Persisted question \"}]"
        };

        var command = mapper.FromLifecycleProduct(ProductAuthoringOperation.Update, product);

        var faq = Assert.Single(command.Content.Faqs);
        Assert.Equal("Persisted question", faq.Question);
        Assert.Equal("Persisted answer", faq.Answer);
    }

    [Fact]
    public void ProductUnitsUpdate_MapsProductSnapshotAndSubmittedUnitsOnly()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var productUnitId = Guid.NewGuid();
        var mapper = CreateMapper(tenantId, "trace-units", idempotencyKey: "idem-units", expectedVersion: "42");
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Unit Product",
            CategoryId = categoryId,
            SKU = "UNIT-SKU",
            Barcode = "UNIT-BAR",
            PurchasePrice = 5m,
            SalePrice = 15m,
            OnlinePrice = 14m,
            Unit = "Piece",
            MinimumStockLevel = 2m,
            IsActive = true,
            TrackStock = true,
            HasVariants = false,
            PublishStatus = PublishStatus.Published,
            IsOnlineVisible = true,
            IsSearchable = true,
            IsFeatured = false,
            LowStockThreshold = 11,
            AllowBackorder = false,
            ProductTemplate = "default"
        };

        var command = mapper.FromProductUnitsUpdate(product,
        [
            new ProductAuthoringUnitRequest(productUnitId, unitId, 1m, 5m, 15m, "UNIT-BC", IsDefault: true, IsBaseUnit: true)
        ]);

        Assert.Equal(ProductAuthoringOperation.Update, command.Operation);
        Assert.Equal(productId, command.Identity.ProductId);
        Assert.Equal(PublishStatus.Published, command.Publication.PublishStatus);
        Assert.Equal(unitId, command.Units.BaseUnitId);
        Assert.Equal(unitId, command.Units.DefaultUnitId);
        var unit = Assert.Single(command.Units.Items);
        Assert.Equal(productUnitId, unit.ProductUnitId);
        Assert.True(unit.IsDefault);
        Assert.True(unit.IsBaseUnit);
        Assert.Empty(command.Variants);
        Assert.Equal("true", command.Metadata.AdditionalMetadata["productUnitsSubmitted"]);
        Assert.Equal("false", command.Metadata.AdditionalMetadata["variantsSubmitted"]);
        Assert.Equal("idem-units", command.Metadata.IdempotencyKey);
        Assert.Equal(42, command.Metadata.ExpectedVersion);
    }

    private static ProductAuthoringCommandMapper CreateMapper(
        Guid tenantId,
        string traceIdentifier,
        string? idempotencyKey = null,
        string? requestId = null,
        string? ifMatch = null,
        string? expectedVersion = null)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = traceIdentifier
        };

        if (idempotencyKey is not null)
        {
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        if (requestId is not null)
        {
            httpContext.Request.Headers["X-Request-Id"] = requestId;
        }

        if (ifMatch is not null)
        {
            httpContext.Request.Headers["If-Match"] = ifMatch;
        }

        if (expectedVersion is not null)
        {
            httpContext.Request.Headers["Expected-Version"] = expectedVersion;
        }

        return new ProductAuthoringCommandMapper(
            new TestTenantContext(tenantId),
            new HttpContextAccessor { HttpContext = httpContext });
    }
}
