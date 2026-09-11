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
        var readRepository = new Mock<ICommerceHubReadRepository>();
        readRepository
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
            readRepository: readRepository.Object);

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

        var readRepository = new Mock<ICommerceHubReadRepository>();
        readRepository
            .Setup(service => service.SearchPublishedProductsAsync(
                It.IsAny<CommerceHubSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommerceHubSearchRequest request, CancellationToken _) =>
            {
                Assert.Contains(tenantId, request.ExcludedTenantIds);
                return new CommerceHubSearchReadResult();
            });

        var service = CreateService(
            dbContext,
            suspendedVendorIds: new[] { tenantId },
            readRepository: readRepository.Object);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery
        {
            Search = "runner"
        });

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        readRepository.Verify(
            service => service.SearchPublishedProductsAsync(
                It.IsAny<CommerceHubSearchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductsAsync_PreservesProjectionPaging_AndRevalidatesEachPageAgainstSqlAuthority()
    {
        await using var dbContext = CreateDbContext();
        var firstEligibleTenant = Guid.NewGuid();
        var secondEligibleTenant = Guid.NewGuid();
        var firstListingId = await SeedVisibleListingAsync(dbContext, firstEligibleTenant, "Eligible one", "Running");
        var secondListingId = await SeedVisibleListingAsync(dbContext, secondEligibleTenant, "Eligible two", "Running");
        AddEligibleVendorProfile(dbContext, firstEligibleTenant);
        AddEligibleVendorProfile(dbContext, secondEligibleTenant);
        await dbContext.SaveChangesAsync();

        var readRepository = new Mock<ICommerceHubReadRepository>(MockBehavior.Strict);
        readRepository
            .Setup(service => service.SearchPublishedProductsAsync(
                It.IsAny<CommerceHubSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommerceHubSearchRequest request, CancellationToken _) => new CommerceHubSearchReadResult
            {
                Items =
                [
                    Projection(
                        request.Page == 1 ? firstListingId : secondListingId,
                        request.Page == 1 ? firstEligibleTenant : secondEligibleTenant)
                ],
                TotalCount = 2
            });
        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            readRepository: readRepository.Object);

        var firstPage = await service.GetProductsAsync(new CommerceHubProductsQuery { Page = 1, PageSize = 1 });
        var secondPage = await service.GetProductsAsync(new CommerceHubProductsQuery { Page = 2, PageSize = 1 });

        Assert.Equal(1, firstPage.TotalCount);
        Assert.Equal(1, secondPage.TotalCount);
        Assert.Single(firstPage.Items);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].Id, secondPage.Items[0].Id);
        Assert.Contains(firstPage.Items[0].TenantId, new[] { firstEligibleTenant, secondEligibleTenant });
        Assert.Contains(secondPage.Items[0].TenantId, new[] { firstEligibleTenant, secondEligibleTenant });
    }

    [Fact]
    public async Task GetProductsAsync_FiltersByCurrentSqlListingCategory()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var listingId = await SeedVisibleListingAsync(dbContext, tenantId, "Runner", "Running");
        AddEligibleVendorProfile(dbContext, tenantId);
        await dbContext.SaveChangesAsync();
        var categoryId = await dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .Where(listing => listing.Id == listingId)
            .Select(listing => listing.CommerceHubCategoryId)
            .SingleAsync();
        var readRepository = RepositoryReturning(Projection(listingId, tenantId));
        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            readRepository: readRepository.Object);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery
        {
            CategoryId = categoryId
        });

        var item = Assert.Single(response.Items);
        Assert.Equal(listingId, item.Id);
        Assert.Equal(categoryId, item.CategoryId);
        readRepository.Verify(service => service.SearchPublishedProductsAsync(
            It.Is<CommerceHubSearchRequest>(request => request.CategoryId == categoryId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProductsAsync_MatchesCurrentSqlVariantSku()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var listingId = await SeedVisibleListingAsync(dbContext, tenantId, "Runner", "Running");
        AddEligibleVendorProfile(dbContext, tenantId);
        var listing = await dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == listingId);
        var variant = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == listing.VariantId);
        variant.SKU = "RUNNER-BLUE-42";
        await dbContext.SaveChangesAsync();
        var readRepository = RepositoryReturning(Projection(listingId, tenantId));
        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            readRepository: readRepository.Object);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery
        {
            Search = "RUNNER-BLUE-42"
        });

        Assert.Equal(listingId, Assert.Single(response.Items).Id);
        readRepository.Verify(service => service.SearchPublishedProductsAsync(
            It.Is<CommerceHubSearchRequest>(request => !string.IsNullOrWhiteSpace(request.Query)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProductsAsync_FiltersByRequestedTenant()
    {
        await using var dbContext = CreateDbContext();
        var requestedTenantId = Guid.NewGuid();
        var requestedListingId = await SeedVisibleListingAsync(dbContext, requestedTenantId, "Runner", "Running");
        var otherTenantId = Guid.NewGuid();
        await SeedVisibleListingAsync(dbContext, otherTenantId, "Other runner", "Running");
        AddEligibleVendorProfile(dbContext, requestedTenantId);
        AddEligibleVendorProfile(dbContext, otherTenantId);
        await dbContext.SaveChangesAsync();
        var readRepository = RepositoryReturning(Projection(requestedListingId, requestedTenantId));
        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            readRepository: readRepository.Object);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery
        {
            TenantId = requestedTenantId
        });

        var item = Assert.Single(response.Items);
        Assert.Equal(requestedListingId, item.Id);
        Assert.Equal(requestedTenantId, item.TenantId);
        readRepository.Verify(service => service.SearchPublishedProductsAsync(
            It.Is<CommerceHubSearchRequest>(request => request.TenantId == requestedTenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProductsAsync_IgnoresAmbientTenantForGlobalCatalog()
    {
        var ambientTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(ambientTenantId);
        var ambientListingId = await SeedVisibleListingAsync(dbContext, ambientTenantId, "Ambient runner", "Running");
        var otherListingId = await SeedVisibleListingAsync(dbContext, otherTenantId, "Other runner", "Running");
        AddEligibleVendorProfile(dbContext, ambientTenantId);
        AddEligibleVendorProfile(dbContext, otherTenantId);
        await dbContext.SaveChangesAsync();
        var readRepository = RepositoryReturning(
            Projection(ambientListingId, ambientTenantId),
            Projection(otherListingId, otherTenantId));
        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            readRepository: readRepository.Object);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery());

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(
            new[] { ambientTenantId, otherTenantId }.Order(),
            response.Items.Select(item => item.TenantId).Order());
    }

    [Fact]
    public async Task GetProductsAsync_ReturnsEmptyForIneligibleRequestedTenant()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var listingId = await SeedVisibleListingAsync(dbContext, tenantId, "Runner", "Running");
        var readRepository = RepositoryReturning(Projection(listingId, tenantId));
        var service = CreateService(
            dbContext,
            suspendedVendorIds: Array.Empty<Guid>(),
            readRepository: readRepository.Object);

        var response = await service.GetProductsAsync(new CommerceHubProductsQuery
        {
            TenantId = tenantId
        });

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
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
            searchReadService: searchReadService.Object);

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
            intentDetector: intentDetector.Object);

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
        ICommerceHubReadRepository? readRepository = null)
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
            readRepository ?? Mock.Of<ICommerceHubReadRepository>(),
            searchReadService ?? Mock.Of<ICommerceHubSearchReadService>(),
            searchIntelligenceService.Object,
            intentDetector ?? Mock.Of<ICommerceHubSearchIntentDetector>(),
            Mock.Of<ICommerceHubAutoHealingQueue>(),
            mongoDbContext,
            dbContext,
            vendorFinancialControlService.Object,
            governanceCacheInvalidationService.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CommerceHubQueryService>.Instance);
    }

    private static Mock<ICommerceHubReadRepository> RepositoryReturning(
        params PointOfSale.Infrastructure.MongoDB.Documents.CommerceHubProductDocument[] documents)
    {
        var service = new Mock<ICommerceHubReadRepository>();
        service
            .Setup(item => item.SearchPublishedProductsAsync(
                It.IsAny<CommerceHubSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubSearchReadResult
            {
                Items = documents.ToList(),
                TotalCount = documents.Length
            });
        return service;
    }

    private static PointOfSale.Infrastructure.MongoDB.Documents.CommerceHubProductDocument Projection(
        Guid listingId,
        Guid tenantId) => new()
        {
            Id = listingId,
            TenantId = tenantId,
            IsPublished = true
        };

    private static ApplicationDbContext CreateDbContext(Guid? currentTenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return currentTenantId.HasValue
            ? new ApplicationDbContext(options, new TestTenantContext(currentTenantId))
            : new ApplicationDbContext(options);
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
