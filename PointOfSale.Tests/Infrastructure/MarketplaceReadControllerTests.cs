using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.MarketplaceTranslations;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.ReadModels.Marketplace;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceReadControllerTests
{
    [Fact]
    public async Task GetProducts_ReturnsGlobalPagedResult_WhenTenantContextIsMissing()
    {
        var vendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productRepository = new Mock<IMarketplaceProductReadRepository>();
        productRepository
            .Setup(x => x.GetDistinctVendorIdsAsync(null, null, null, vendorId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vendorId });
        productRepository
            .Setup(x => x.GetPagedAsync(null, null, null, vendorId, It.IsAny<IReadOnlyCollection<Guid>?>(), true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<MarketplaceProductReadModelDocument>
                {
                    new()
                    {
                        Id = $"global:{productId:N}",
                        ProductId = productId,
                        CategoryId = categoryId,
                        Name = "Runner Pro",
                        Slug = "runner-pro",
                        Price = 150m,
                        Currency = "EGP",
                        Rating = 4.8m,
                        ReviewCount = 23,
                        Category = "Shoes",
                        VendorId = vendorId,
                        VendorName = "Vendor One",
                        Images = ["https://cdn.shopixy.test/products/runner-pro.jpg"],
                        IsAvailable = true,
                        UpdatedAtUtc = new DateTime(2026, 3, 29, 23, 0, 0, DateTimeKind.Utc)
                    }
                },
                1L));

        var productLocalization = new Mock<IMarketplaceProductLocalizationService>();
        productLocalization
            .Setup(x => x.LocalizeSummaryAsync(It.IsAny<MarketplaceProductReadModelDocument>(), "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceLocalizedProductSummary
            {
                Language = "en",
                Name = "Runner Pro",
                Category = "Shoes",
                IsAutoTranslated = false
            });

        var controller = CreateController(
            tenantId: null,
            productRepository: productRepository.Object,
            productLocalizationService: productLocalization.Object);

        var result = await controller.GetProducts(null, null, vendorId, true);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PointOfSale.API.Controllers.PagedResult<MarketplaceProductReadDto>>(okResult.Value);
        Assert.Single(payload.Items);
        Assert.Equal("Runner Pro", payload.Items[0].Name);
    }

    [Fact]
    public async Task GetProducts_WhenProjectionAuthorityIsBlocked_Returns503_BeforeMongoRead()
    {
        var tenantId = Guid.NewGuid();
        var productRepository = new Mock<IMarketplaceProductReadRepository>(MockBehavior.Strict);
        var projectionReadiness = new Mock<ICommerceHubProjectionReadinessService>();
        projectionReadiness
            .Setup(service => service.EnsurePublicProductReadModelsReadyAsync(
                tenantId,
                "marketplace.product.list",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProjectionReadModelUnavailableException("projection blocked"));

        var controller = CreateController(
            tenantId,
            productRepository: productRepository.Object,
            projectionReadinessService: projectionReadiness.Object);

        var result = await controller.GetProducts();

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        productRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetProducts_ReturnsMongoPagedResult()
    {
        var tenantId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productRepository = new Mock<IMarketplaceProductReadRepository>();
        productRepository
            .Setup(x => x.GetDistinctVendorIdsAsync(tenantId, "runner", "Shoes", vendorId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vendorId });
        productRepository
            .Setup(x => x.GetPagedAsync(tenantId, "runner", "Shoes", vendorId, It.IsAny<IReadOnlyCollection<Guid>?>(), true, 2, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<MarketplaceProductReadModelDocument>
                {
                    new()
                    {
                        Id = $"{tenantId:N}:{Guid.NewGuid():N}",
                        TenantId = tenantId,
                        ProductId = Guid.NewGuid(),
                        CategoryId = categoryId,
                        Name = "Runner Pro",
                        Slug = "runner-pro",
                        Price = 150m,
                        Currency = "EGP",
                        Rating = 4.8m,
                        ReviewCount = 23,
                        Category = "Shoes",
                        VendorId = vendorId,
                        VendorName = "Vendor One",
                        Images = ["https://cdn.shopixy.test/products/runner-pro.jpg"],
                        IsAvailable = true,
                        UpdatedAtUtc = new DateTime(2026, 3, 29, 23, 0, 0, DateTimeKind.Utc)
                    }
                },
                1L));

        var productLocalization = new Mock<IMarketplaceProductLocalizationService>();
        productLocalization
            .Setup(x => x.LocalizeSummaryAsync(It.IsAny<MarketplaceProductReadModelDocument>(), "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceLocalizedProductSummary
            {
                Language = "en",
                Name = "Runner Pro",
                Category = "Shoes",
                IsAutoTranslated = false
            });

        var controller = CreateController(
            tenantId,
            productRepository: productRepository.Object,
            productLocalizationService: productLocalization.Object);

        var result = await controller.GetProducts("runner", "Shoes", vendorId, true, null, 2, 50);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PointOfSale.API.Controllers.PagedResult<MarketplaceProductReadDto>>(okResult.Value);

        Assert.Single(payload.Items);
        Assert.Equal("Runner Pro", payload.Items[0].Name);
        Assert.Equal(150m, payload.Items[0].Price);
        Assert.Equal("Vendor One", payload.Items[0].VendorName);
        Assert.True(payload.Items[0].IsAvailable);
        Assert.Equal(1, payload.TotalCount);
        Assert.Equal(2, payload.Page);
        Assert.Equal(50, payload.PageSize);
        Assert.Equal(1, payload.TotalPages);
    }

    [Fact]
    public async Task GetProducts_ResolvesArabicNames_WhenLanguageIsArabic()
    {
        var tenantId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productRepository = new Mock<IMarketplaceProductReadRepository>();
        productRepository
            .Setup(x => x.GetDistinctVendorIdsAsync(tenantId, null, null, vendorId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vendorId });
        productRepository
            .Setup(x => x.GetPagedAsync(tenantId, null, null, vendorId, It.IsAny<IReadOnlyCollection<Guid>?>(), true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<MarketplaceProductReadModelDocument>
                {
                    new()
                    {
                        Id = $"{tenantId:N}:{Guid.NewGuid():N}",
                        TenantId = tenantId,
                        ProductId = Guid.NewGuid(),
                        CategoryId = categoryId,
                        Name = "Runner Pro",
                        NameAr = "حذاء الجري برو",
                        Slug = "runner-pro",
                        Price = 150m,
                        Currency = "EGP",
                        Rating = 4.8m,
                        ReviewCount = 23,
                        Category = "Shoes",
                        CategoryAr = "أحذية",
                        VendorId = vendorId,
                        VendorName = "Vendor One",
                        Images = ["https://cdn.shopixy.test/products/runner-pro.jpg"],
                        IsAvailable = true,
                        UpdatedAtUtc = new DateTime(2026, 3, 29, 23, 0, 0, DateTimeKind.Utc)
                    }
                },
                1L));

        var productLocalization = new Mock<IMarketplaceProductLocalizationService>();
        productLocalization
            .Setup(x => x.LocalizeSummaryAsync(It.IsAny<MarketplaceProductReadModelDocument>(), "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceLocalizedProductSummary
            {
                Language = "ar",
                Name = "حذاء الجري برو",
                Category = "أحذية",
                IsAutoTranslated = true
            });

        var controller = CreateController(
            tenantId,
            productRepository: productRepository.Object,
            productLocalizationService: productLocalization.Object);

        var result = await controller.GetProducts(null, null, vendorId, true, "ar", 1, 20);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PointOfSale.API.Controllers.PagedResult<MarketplaceProductReadDto>>(okResult.Value);

        Assert.Equal("ar", payload.Items[0].Language);
        Assert.Equal("حذاء الجري برو", payload.Items[0].Name);
        Assert.Equal("أحذية", payload.Items[0].Category);
        Assert.True(payload.Items[0].IsAutoTranslated);
    }

    [Fact]
    public async Task GetProduct_ReturnsLocalizedDetailWithAutoTranslationMetadata()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productRepository = new Mock<IMarketplaceProductReadRepository>();
        productRepository
            .Setup(x => x.GetByProductIdAsync(tenantId, productId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceProductReadModelDocument
            {
                Id = $"{tenantId:N}:{productId:N}",
                TenantId = tenantId,
                ProductId = productId,
                CategoryId = categoryId,
                Name = "Runner Pro",
                Description = "Lightweight runner.",
                Slug = "runner-pro",
                Price = 150m,
                Currency = "EGP",
                Rating = 4.8m,
                ReviewCount = 23,
                Category = "Shoes",
                VendorId = vendorId,
                VendorName = "Vendor One",
                Images = ["https://cdn.shopixy.test/products/runner-pro.jpg"],
                IsAvailable = true,
                UpdatedAtUtc = new DateTime(2026, 3, 29, 23, 0, 0, DateTimeKind.Utc),
                ProjectionUpdatedAtUtc = new DateTime(2026, 3, 29, 23, 1, 0, DateTimeKind.Utc),
                LastEventOccurredAtUtc = new DateTime(2026, 3, 29, 22, 59, 0, DateTimeKind.Utc)
            });

        var productLocalization = new Mock<IMarketplaceProductLocalizationService>();
        productLocalization
            .Setup(x => x.LocalizeDetailAsync(It.IsAny<MarketplaceProductReadModelDocument>(), "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceLocalizedProductDetail
            {
                Language = "ar",
                Name = "حذاء الجري برو",
                Description = "خفيف للجري.",
                Features = ["خامة خفيفة"],
                Faqs =
                [
                    new MarketplaceProductFaqReadDto
                    {
                        Question = "هل هو مقاوم للماء؟",
                        Answer = "نعم"
                    }
                ],
                Tags = ["sport"],
                Category = "أحذية",
                IsAutoTranslated = true
            });

        var controller = CreateController(
            tenantId,
            productRepository: productRepository.Object,
            productLocalizationService: productLocalization.Object);

        var result = await controller.GetProduct(productId, "ar");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MarketplaceProductDetailReadDto>(okResult.Value);
        Assert.Equal(productId, payload.ProductId);
        Assert.Equal("ar", payload.Language);
        Assert.Equal("حذاء الجري برو", payload.Name);
        Assert.Equal("أحذية", payload.Category);
        Assert.True(payload.IsAutoTranslated);
    }

    [Fact]
    public async Task GetProducts_ExcludesSuppressedVendorsFromPagedPublicResults()
    {
        var tenantId = Guid.NewGuid();
        var suppressedVendorId = Guid.NewGuid();
        var visibleVendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productRepository = new Mock<IMarketplaceProductReadRepository>();
        productRepository
            .Setup(x => x.GetDistinctVendorIdsAsync(tenantId, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { suppressedVendorId, visibleVendorId });
        productRepository
            .Setup(x => x.GetPagedAsync(
                tenantId,
                null,
                null,
                null,
                It.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 1 && ids.Contains(suppressedVendorId)),
                null,
                1,
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<MarketplaceProductReadModelDocument>
                {
                    new()
                    {
                        Id = $"{tenantId:N}:{Guid.NewGuid():N}",
                        TenantId = tenantId,
                        ProductId = Guid.NewGuid(),
                        CategoryId = categoryId,
                        Name = "Visible Runner",
                        Slug = "visible-runner",
                        Price = 175m,
                        Currency = "EGP",
                        Category = "Shoes",
                        VendorId = visibleVendorId,
                        VendorName = "Visible Vendor",
                        Images = [],
                        IsAvailable = true,
                        UpdatedAtUtc = DateTime.UtcNow
                    }
                },
                1L));

        var productLocalization = new Mock<IMarketplaceProductLocalizationService>();
        productLocalization
            .Setup(x => x.LocalizeSummaryAsync(It.IsAny<MarketplaceProductReadModelDocument>(), "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceLocalizedProductSummary
            {
                Language = "en",
                Name = "Visible Runner",
                Category = "Shoes",
                IsAutoTranslated = false
            });

        var visibilityService = new Mock<ICommerceHubPublicVendorVisibilityService>();
        visibilityService
            .Setup(x => x.GetSuppressedVendorIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { suppressedVendorId });

        var controller = CreateController(
            tenantId,
            productRepository: productRepository.Object,
            productLocalizationService: productLocalization.Object,
            publicVendorVisibilityService: visibilityService.Object);

        var result = await controller.GetProducts();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PointOfSale.API.Controllers.PagedResult<MarketplaceProductReadDto>>(okResult.Value);
        Assert.Single(payload.Items);
        Assert.Equal(visibleVendorId, payload.Items[0].VendorId);
        Assert.Equal(1, payload.TotalCount);
    }

    [Fact]
    public async Task GetProduct_ReturnsNotFound_WhenVendorIsCurrentlySuppressed()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productRepository = new Mock<IMarketplaceProductReadRepository>();
        productRepository
            .Setup(x => x.GetByProductIdAsync(tenantId, productId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceProductReadModelDocument
            {
                Id = $"{tenantId:N}:{productId:N}",
                TenantId = tenantId,
                ProductId = productId,
                CategoryId = Guid.NewGuid(),
                Name = "Suppressed Product",
                Slug = "suppressed-product",
                Price = 150m,
                Currency = "EGP",
                VendorId = vendorId,
                VendorName = "Suppressed Vendor",
                Images = []
            });

        var visibilityService = new Mock<ICommerceHubPublicVendorVisibilityService>();
        visibilityService
            .Setup(x => x.IsVendorSuppressedAsync(vendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(
            tenantId,
            productRepository: productRepository.Object,
            publicVendorVisibilityService: visibilityService.Object);

        var result = await controller.GetProduct(productId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetOrders_ReturnsMongoPagedResult()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var orderRepository = new Mock<IMarketplaceOrderReadRepository>();
        orderRepository
            .Setup(x => x.GetPagedAsync(tenantId, "maya", "Delivered", 1, 20, customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<MarketplaceOrderReadModelDocument>
                {
                    new()
                    {
                        Id = $"{tenantId:N}:{orderId:N}",
                        TenantId = tenantId,
                        OrderId = orderId,
                        OrderNumber = "ORD-2026-1001",
                        CustomerName = "Maya Hassan",
                        Status = "Delivered",
                        TotalAmount = 300m,
                        Currency = "EGP",
                        Items =
                        [
                            new MarketplaceOrderReadModelItemDocument
                            {
                                ProductId = productId,
                                ProductName = "Runner Pro",
                                Sku = "RUNNER-PRO-42",
                                Quantity = 2,
                                UnitPrice = 150m,
                                Total = 300m,
                                ImageUrl = "https://cdn.shopixy.test/orders/runner-pro.jpg"
                            }
                        ],
                        CreatedAtUtc = new DateTime(2026, 3, 29, 23, 30, 0, DateTimeKind.Utc),
                        PaidAtUtc = new DateTime(2026, 3, 29, 23, 35, 0, DateTimeKind.Utc),
                        DeliveredAtUtc = new DateTime(2026, 3, 31, 10, 0, 0, DateTimeKind.Utc)
                    }
                },
                1L));

        var controller = CreateController(
            tenantId,
            orderRepository: orderRepository.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim("CustomerId", customerId.ToString())],
                        authenticationType: "Test"))
            }
        };

        var result = await controller.GetOrders("maya", "Delivered");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PointOfSale.API.Controllers.PagedResult<MarketplaceOrderReadDto>>(okResult.Value);

        Assert.Single(payload.Items);
        Assert.Equal("ORD-2026-1001", payload.Items[0].OrderNumber);
        Assert.Equal("Maya Hassan", payload.Items[0].CustomerName);
        Assert.Equal("Delivered", payload.Items[0].Status);
        Assert.Single(payload.Items[0].Items);
        Assert.Equal("Runner Pro", payload.Items[0].Items[0].ProductName);
    }

    [Fact]
    public async Task GetHomepage_ReturnsNotFound_WhenReadModelDoesNotExist()
    {
        var tenantId = Guid.NewGuid();
        var homepageRepository = new Mock<IMarketplaceHomepageReadRepository>();
        homepageRepository
            .Setup(x => x.GetByTenantOrGlobalAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketplaceHomepageReadModelDocument?)null);

        var controller = CreateController(
            tenantId,
            homepageRepository: homepageRepository.Object);

        var result = await controller.GetHomepage();

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetHomepage_ReturnsNotFound_WhenCachedProjectionFailsCurrentSqlVisibilityFilter()
    {
        var tenantId = Guid.NewGuid();
        var document = new MarketplaceHomepageReadModelDocument
        {
            Id = tenantId.ToString("N"),
            ScopeKey = tenantId.ToString("N"),
            TenantId = tenantId,
            Sections =
            [
                new MarketplaceHomepageSectionDocument
                {
                    Type = "products",
                    Order = 0,
                    DataJson = "{\"source\":\"curated\",\"productIds\":[\"b1679e69-3348-4ed9-aef6-f5f81d04fb08\"]}",
                    Version = 1,
                    SchemaVersion = 1,
                    IsActive = true
                }
            ]
        };
        var homepageRepository = new Mock<IMarketplaceHomepageReadRepository>();
        homepageRepository
            .Setup(x => x.GetByTenantOrGlobalAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        var homepageProjection = new Mock<IMarketplaceHomepageReadModelProjectionService>();
        homepageProjection
            .Setup(x => x.FilterPublicReadAsync(document, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketplaceHomepageReadModelDocument?)null);

        var controller = CreateController(
            tenantId,
            homepageRepository: homepageRepository.Object,
            homepageProjectionService: homepageProjection.Object);

        var result = await controller.GetHomepage();

        Assert.IsType<NotFoundResult>(result.Result);
        homepageProjection.Verify(
            x => x.FilterPublicReadAsync(document, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHomepage_ReturnsTenantOrGlobalReadModel()
    {
        var tenantId = Guid.NewGuid();
        var homepageRepository = new Mock<IMarketplaceHomepageReadRepository>();
        homepageRepository
            .Setup(x => x.GetByTenantOrGlobalAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new MarketplaceHomepageReadModelDocument
                {
                    Id = tenantId.ToString("N"),
                    ScopeKey = tenantId.ToString("N"),
                    TenantId = tenantId,
                    Sections =
                    [
                        new MarketplaceHomepageSectionDocument
                        {
                            Type = "hero",
                            Order = 0,
                            DataJson = "{\"title\":\"Spring Drop\"}",
                            Version = 2,
                            SchemaVersion = 1,
                            IsActive = true
                        }
                    ],
                    LastUpdated = new DateTime(2026, 3, 29, 23, 45, 0, DateTimeKind.Utc),
                    LastEventOccurredAtUtc = new DateTime(2026, 3, 29, 23, 40, 0, DateTimeKind.Utc),
                    LastEventVersion = 2
                });

        var controller = CreateController(
            tenantId,
            homepageRepository: homepageRepository.Object);

        var result = await controller.GetHomepage();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MarketplaceHomepageReadDto>(okResult.Value);

        Assert.Equal(tenantId, payload.TenantId);
        Assert.Single(payload.Sections);
        Assert.Equal("hero", payload.Sections[0].Type);
        Assert.Equal("Spring Drop", payload.Sections[0].Data.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetHomepage_ResolvesLocalizedSectionFields_WhenLanguageIsArabic()
    {
        var tenantId = Guid.NewGuid();
        var homepageRepository = new Mock<IMarketplaceHomepageReadRepository>();
        homepageRepository
            .Setup(x => x.GetByTenantOrGlobalAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new MarketplaceHomepageReadModelDocument
                {
                    Id = tenantId.ToString("N"),
                    ScopeKey = tenantId.ToString("N"),
                    TenantId = tenantId,
                    Sections =
                    [
                        new MarketplaceHomepageSectionDocument
                        {
                            Type = "hero",
                            Order = 0,
                            DataJson = "{\"title\":\"Spring Drop\",\"titleLocalized\":{\"en\":\"Spring Drop\",\"ar\":\"تخفيضات الربيع\"}}",
                            Version = 2,
                            SchemaVersion = 1,
                            IsActive = true
                        }
                    ],
                    LastUpdated = new DateTime(2026, 3, 29, 23, 45, 0, DateTimeKind.Utc),
                    LastEventOccurredAtUtc = new DateTime(2026, 3, 29, 23, 40, 0, DateTimeKind.Utc),
                    LastEventVersion = 2
                });

        var controller = CreateController(
            tenantId,
            homepageRepository: homepageRepository.Object);

        var result = await controller.GetHomepage("ar");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MarketplaceHomepageReadDto>(okResult.Value);

        Assert.Equal("ar", payload.Language);
        Assert.Equal("تخفيضات الربيع", payload.Sections[0].Data.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetHomepage_ScrubsCuratedProductsForSuppressedVendors()
    {
        var tenantId = Guid.NewGuid();
        var suppressedListingId = Guid.NewGuid();
        var visibleListingId = Guid.NewGuid();
        var homepageRepository = new Mock<IMarketplaceHomepageReadRepository>();
        homepageRepository
            .Setup(x => x.GetByTenantOrGlobalAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new MarketplaceHomepageReadModelDocument
                {
                    Id = tenantId.ToString("N"),
                    ScopeKey = tenantId.ToString("N"),
                    TenantId = tenantId,
                    Sections =
                    [
                        new MarketplaceHomepageSectionDocument
                        {
                            Type = "products",
                            Order = 0,
                            DataJson = $"{{\"source\":\"curated\",\"productIds\":[\"{suppressedListingId:D}\",\"{visibleListingId:D}\"]}}",
                            Version = 1,
                            SchemaVersion = 1,
                            IsActive = true
                        }
                    ],
                    LastUpdated = DateTime.UtcNow,
                    LastEventOccurredAtUtc = DateTime.UtcNow,
                    LastEventVersion = 1
                });

        var visibilityService = new Mock<ICommerceHubPublicVendorVisibilityService>();
        visibilityService
            .Setup(x => x.FilterVisibleListingIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { visibleListingId });

        var controller = CreateController(
            tenantId,
            homepageRepository: homepageRepository.Object,
            publicVendorVisibilityService: visibilityService.Object);

        var result = await controller.GetHomepage();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MarketplaceHomepageReadDto>(okResult.Value);
        var ids = payload.Sections[0].Data
            .GetProperty("productIds")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.Equal(new[] { visibleListingId.ToString("D") }, ids);
    }

    private static MarketplaceReadController CreateController(
        Guid? tenantId,
        IMarketplaceProductReadRepository? productRepository = null,
        IMarketplaceOrderReadRepository? orderRepository = null,
        IMarketplaceHomepageReadRepository? homepageRepository = null,
        IMarketplaceHomepageReadModelProjectionService? homepageProjectionService = null,
        IMarketplaceProductLocalizationService? productLocalizationService = null,
        ICommerceHubProjectionReadinessService? projectionReadinessService = null,
        ICommerceHubGovernanceCacheInvalidationService? governanceCacheInvalidationService = null,
        ICommerceHubPublicVendorVisibilityService? publicVendorVisibilityService = null)
    {
        return new MarketplaceReadController(
            productRepository ?? Mock.Of<IMarketplaceProductReadRepository>(),
            orderRepository ?? Mock.Of<IMarketplaceOrderReadRepository>(),
            homepageRepository ?? Mock.Of<IMarketplaceHomepageReadRepository>(),
            homepageProjectionService ?? CreatePassThroughHomepageProjectionService(),
            productLocalizationService ?? Mock.Of<IMarketplaceProductLocalizationService>(),
            projectionReadinessService ?? CreateReadyProjectionService(),
            governanceCacheInvalidationService ?? CreateGovernanceCacheInvalidationService(),
            publicVendorVisibilityService ?? CreatePublicVendorVisibilityService(),
            new StubTenantContext { TenantId = tenantId },
            new PassThroughRedisJsonCacheService(),
            Options.Create(new RedisSettings()),
            NullLogger<MarketplaceReadController>.Instance);
    }

    private static ICommerceHubProjectionReadinessService CreateReadyProjectionService()
    {
        var service = new Mock<ICommerceHubProjectionReadinessService>();
        service
            .Setup(item => item.EnsurePublicProductReadModelsReadyAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return service.Object;
    }

    private static IMarketplaceHomepageReadModelProjectionService CreatePassThroughHomepageProjectionService()
    {
        var service = new Mock<IMarketplaceHomepageReadModelProjectionService>();
        service
            .Setup(item => item.FilterPublicReadAsync(
                It.IsAny<MarketplaceHomepageReadModelDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketplaceHomepageReadModelDocument document, CancellationToken _) => document);
        return service.Object;
    }

    private static ICommerceHubGovernanceCacheInvalidationService CreateGovernanceCacheInvalidationService()
    {
        var service = new Mock<ICommerceHubGovernanceCacheInvalidationService>();
        service
            .Setup(item => item.GetVisibilityVersionTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string>("0"));
        return service.Object;
    }

    private static ICommerceHubPublicVendorVisibilityService CreatePublicVendorVisibilityService()
    {
        var service = new Mock<ICommerceHubPublicVendorVisibilityService>();
        service
            .Setup(item => item.GetSuppressedVendorIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());
        service
            .Setup(item => item.FilterVisibleListingIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => ids.Where(id => id != Guid.Empty).ToHashSet());
        service
            .Setup(item => item.IsVendorSuppressedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return service.Object;
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid? TenantId { get; init; }
    }

    private sealed class PassThroughRedisJsonCacheService : IRedisJsonCacheService
    {
        public Task<T?> GetAsync<T>(
            string key,
            string cacheType,
            TimeSpan? maxProjectionLag = null,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return Task.FromResult<T?>(null);
        }

        public Task SetAsync<T>(
            string key,
            string cacheType,
            T value,
            TimeSpan ttl,
            DateTime? projectionLastUpdatedAtUtc = null,
            DateTime? sourceEventOccurredAtUtc = null,
            int schemaVersion = 1,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return Task.CompletedTask;
        }

        public async Task<T?> GetOrCreateAsync<T>(
            string key,
            string cacheType,
            TimeSpan ttl,
            Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
            TimeSpan? maxProjectionLag = null,
            CancellationToken cancellationToken = default)
            where T : class
        {
            var population = await factory(cancellationToken);
            return population.Value;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
