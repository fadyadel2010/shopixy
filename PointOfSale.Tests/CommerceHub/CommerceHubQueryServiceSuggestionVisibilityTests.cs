using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubQueryServiceSuggestionVisibilityTests
{
    [Fact]
    public async Task GetProductsAsync_DropsDerivativeCard_WhenCurrentSqlListingAuthorityIsMissing()
    {
        await using var dbContext = CreateDbContext();
        var staleDocument = new PointOfSale.Infrastructure.MongoDB.Documents.CommerceHubProductDocument
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Stale public card",
            Price = 120m,
            Currency = "EGP",
            CategoryId = Guid.NewGuid(),
            IsPublished = true
        };
        var searchReadService = new Mock<ICommerceHubSearchReadService>();
        searchReadService
            .Setup(service => service.SearchPublishedProductsAsync(
                It.IsAny<CommerceHubSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubSearchReadResult
            {
                Items = [staleDocument],
                TotalCount = 1
            });

        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            searchReadService: searchReadService.Object);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery
        {
            Search = "stale"
        });

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetPopularQueriesAsync_FiltersTermsBackedOnlyBySupersededVariantListings()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var listingCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = DateTime.UtcNow.AddMinutes(-5);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Superseded Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Running",
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = listingCategoryId,
                Name = "Running",
                Slug = "running",
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Runner",
                Description = "Trail runner",
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = false,
                CreatedAt = occurredAtUtc
            },
            new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                Price = 120m,
                OnlinePrice = 120m,
                IsActive = true,
                IsDefault = true,
                SemanticLifecycleState = ProductVariantSemanticLifecycleState.SplitParent,
                SemanticSupersededAtUtc = occurredAtUtc,
                SemanticOperationId = Guid.NewGuid(),
                CreatedAt = occurredAtUtc
            },
            CreateListing(
                tenantId,
                productId,
                variantId,
                listingCategoryId,
                occurredAtUtc),
            new CommerceHubSearchQueryLog
            {
                Id = Guid.NewGuid(),
                QueryText = "runner",
                ResultCount = 4,
                Intent = "unknown",
                SearchedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>());

        var response = await service.GetPopularQueriesAsync(new CommerceHubPopularSearchesQuery
        {
            Days = 7,
            Limit = 10
        });

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetPopularQueriesAsync_FiltersTermsForVendorsSuspendedFromNewOrders()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var listingCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = DateTime.UtcNow.AddMinutes(-5);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Suspended Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Running",
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = listingCategoryId,
                Name = "Running",
                Slug = "running",
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Runner",
                Description = "Trail runner",
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = false,
                CreatedAt = occurredAtUtc
            },
            new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                Price = 120m,
                OnlinePrice = 120m,
                IsActive = true,
                IsDefault = true,
                CreatedAt = occurredAtUtc
            },
            CreateListing(
                tenantId,
                productId,
                variantId,
                listingCategoryId,
                occurredAtUtc),
            new CommerceHubSearchQueryLog
            {
                Id = Guid.NewGuid(),
                QueryText = "runner",
                ResultCount = 4,
                Intent = "unknown",
                SearchedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            suspendedVendorIds: new[] { tenantId });

        var response = await service.GetPopularQueriesAsync(new CommerceHubPopularSearchesQuery
        {
            Days = 7,
            Limit = 10
        });

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetProductsAsync_ExcludesSuspendedVendorsFromGlobalSearchRequest()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        await SeedVisibleListingAsync(dbContext, tenantId, "Runner", "Running");

        var searchReadService = new Mock<ICommerceHubSearchReadService>();

        var service = CreateService(
            dbContext,
            suspendedVendorIds: new[] { tenantId },
            searchReadService: searchReadService.Object,
            currentTenantId: null);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery
        {
            Search = "runner"
        });

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        searchReadService.Verify(
            service => service.SearchPublishedProductsAsync(
                It.IsAny<CommerceHubSearchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetProductsAsync_PaginatesAfterSqlAdmission_AndKeepsSparseValidListingsReachable()
    {
        await using var dbContext = CreateDbContext();
        var firstEligibleTenant = Guid.NewGuid();
        var secondEligibleTenant = Guid.NewGuid();
        await SeedVisibleListingAsync(dbContext, Guid.NewGuid(), "Rejected first", "Running");
        await SeedVisibleListingAsync(dbContext, firstEligibleTenant, "Eligible one", "Running");
        await SeedVisibleListingAsync(dbContext, Guid.NewGuid(), "Rejected middle", "Running");
        await SeedVisibleListingAsync(dbContext, secondEligibleTenant, "Eligible two", "Running");
        await SeedVisibleListingAsync(dbContext, Guid.NewGuid(), "Rejected last", "Running");
        AddEligibleVendorProfile(dbContext, firstEligibleTenant);
        AddEligibleVendorProfile(dbContext, secondEligibleTenant);
        await dbContext.SaveChangesAsync();

        var searchReadService = new Mock<ICommerceHubSearchReadService>(MockBehavior.Strict);
        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            searchReadService: searchReadService.Object,
            currentTenantId: null);

        var firstPage = await service.GetProductsAsync(new CommerceHubProductsQuery { Page = 1, PageSize = 1 });
        var secondPage = await service.GetProductsAsync(new CommerceHubProductsQuery { Page = 2, PageSize = 1 });

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, secondPage.TotalCount);
        Assert.Single(firstPage.Items);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].Id, secondPage.Items[0].Id);
        Assert.Contains(firstPage.Items[0].TenantId, new[] { firstEligibleTenant, secondEligibleTenant });
        Assert.Contains(secondPage.Items[0].TenantId, new[] { firstEligibleTenant, secondEligibleTenant });
    }

    [Fact]
    public async Task GetSuggestionsAsync_SuppressesGovernanceHiddenVendorProducts()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var listingId = await SeedVisibleListingAsync(dbContext, tenantId, "Runner", "Running");
        dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = tenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Banned,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var searchReadService = new Mock<ICommerceHubSearchReadService>();
        searchReadService
            .Setup(service => service.GetSuggestionsAsync(
                It.IsAny<CommerceHubSearchSuggestionsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubSearchSuggestionsResult
            {
                Query = "run",
                Products =
                [
                    new CommerceHubSearchSuggestionProduct
                    {
                        Id = listingId,
                        ProductId = Guid.NewGuid(),
                        VariantId = Guid.NewGuid(),
                        ProductName = "Runner",
                        Price = 120m,
                        Currency = "EGP",
                        CategoryId = Guid.NewGuid(),
                        CategoryName = "Running",
                        StoreName = "Hidden Vendor"
                    }
                ]
            });

        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            searchReadService: searchReadService.Object,
            currentTenantId: null);

        var response = await service.GetSuggestionsAsync(new CommerceHubSearchSuggestionsQuery
        {
            Q = "run"
        });

        Assert.Empty(response.Products);
    }

    [Fact]
    public async Task GetAutocompleteAsync_SuppressesGovernanceHiddenVendorProducts()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var listingId = await SeedVisibleListingAsync(dbContext, tenantId, "Runner", "Running");
        dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = tenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Suspended,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var searchReadService = new Mock<ICommerceHubSearchReadService>();
        searchReadService
            .Setup(service => service.GetAutocompleteAsync(
                It.IsAny<CommerceHubSearchAutocompleteRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubSearchAutocompleteResult
            {
                Query = "run",
                Products =
                [
                    new CommerceHubSearchAutocompleteProduct
                    {
                        Id = listingId,
                        ProductId = Guid.NewGuid(),
                        VariantId = Guid.NewGuid(),
                        ProductName = "Runner",
                        Price = 120m,
                        Currency = "EGP",
                        StoreName = "Hidden Vendor",
                        CategoryId = Guid.NewGuid(),
                        CategoryName = "Running"
                    }
                ]
            });

        var intentDetector = new Mock<ICommerceHubSearchIntentDetector>();
        intentDetector
            .Setup(service => service.DetectAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommerceHubSearchIntentContext.Unknown());

        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            searchReadService: searchReadService.Object,
            intentDetector: intentDetector.Object,
            currentTenantId: null);

        var response = await service.GetAutocompleteAsync(new CommerceHubSearchAutocompleteQuery
        {
            Q = "run"
        });

        Assert.Empty(response.Products);
    }

    private static CommerceHubQueryService CreateService(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> suspendedVendorIds,
        ICommerceHubSearchReadService? searchReadService = null,
        ICommerceHubSearchIntentDetector? intentDetector = null,
        Guid? currentTenantId = null)
    {
        var vendorFinancialControlService = new Mock<ICommerceHubVendorFinancialControlService>();
        vendorFinancialControlService
            .Setup(service => service.GetVendorsSuspendedFromNewOrdersAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> vendorIds, CancellationToken _) =>
                vendorIds.Where(suspendedVendorIds.Contains).Distinct().ToList());

        var governanceCacheInvalidationService = new Mock<ICommerceHubGovernanceCacheInvalidationService>();
        governanceCacheInvalidationService
            .Setup(service => service.GetVisibilityVersionTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string>("0"));

        var searchIntelligenceService = new Mock<ICommerceHubSearchIntelligenceService>();
        searchIntelligenceService
            .Setup(service => service.ExpandQueryAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var mongoDbContext = new MongoDbContext(
            Options.Create(new MongoDbSettings
            {
                ConnectionString = "mongodb://127.0.0.1:27017",
                DatabaseName = $"shopixy-commercehub-query-tests-{Guid.NewGuid():N}"
            }),
            NullLogger<MongoDbContext>.Instance);

        return new CommerceHubQueryService(
            Mock.Of<ICommerceHubReadRepository>(),
            searchReadService ?? Mock.Of<ICommerceHubSearchReadService>(),
            searchIntelligenceService.Object,
            intentDetector ?? Mock.Of<ICommerceHubSearchIntentDetector>(),
            Mock.Of<ICommerceHubAutoHealingQueue>(),
            mongoDbContext,
            dbContext,
            vendorFinancialControlService.Object,
            governanceCacheInvalidationService.Object,
            new TestTenantContext(currentTenantId),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CommerceHubQueryService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static CommerceHubListing CreateListing(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        Guid commerceHubCategoryId,
        DateTime createdAtUtc)
    {
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            commerceHubCategoryId,
            commerceHubPrice: null);
        listing.CreatedAt = createdAtUtc;
        return listing;
    }

    private static void AddEligibleVendorProfile(ApplicationDbContext dbContext, Guid tenantId)
    {
        dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = tenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Active,
            IsKycVerified = true,
            IsMarketplaceApproved = true,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static async Task<Guid> SeedVisibleListingAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string productName,
        string categoryName)
    {
        var categoryId = Guid.NewGuid();
        var listingCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = DateTime.UtcNow.AddMinutes(-5);

        var listing = CreateListing(
            tenantId,
            productId,
            variantId,
            listingCategoryId,
            occurredAtUtc);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = $"{productName} Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = categoryName,
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = listingCategoryId,
                Name = categoryName,
                Slug = categoryName.ToLowerInvariant(),
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = productName,
                Description = $"{productName} description",
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = false,
                CreatedAt = occurredAtUtc
            },
            new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                Price = 120m,
                OnlinePrice = 120m,
                IsActive = true,
                IsDefault = true,
                CreatedAt = occurredAtUtc
            },
            listing);

        await dbContext.SaveChangesAsync();
        return listing.Id;
    }
}
