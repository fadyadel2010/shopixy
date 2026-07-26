using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Consumers.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.ReadModels.Marketplace;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceReadModelProjectionTests
{
    [Fact]
    public async Task ProductProjection_RebuildAsync_MapsMarketplaceSnapshot()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 29, 18, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Vendor One",
            Currency = "EGP",
            Status = TenantStatus.Active
        };

        var commerceHubCategory = new CommerceHubCategory
        {
            Id = commerceHubCategoryId,
            Name = "Marketplace Shoes",
            Slug = "marketplace-shoes",
            IsActive = true,
            CreatedAtUtc = occurredAtUtc
        };

        var category = new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Shoes",
            CreatedAt = occurredAtUtc
        };

        var warehouse = new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main Warehouse",
            CreatedAt = occurredAtUtc
        };

        var productMedia = new ProductMedia
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = productId,
            MediaType = "image",
            Url = "https://cdn.shopixy.test/products/runner-pro-cover.jpg",
            Position = 1,
            CreatedAtUtc = occurredAtUtc
        };
        var variantMedia = new ProductMedia
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = productId,
            MediaType = "image",
            Url = "https://cdn.shopixy.test/products/runner-pro-variant.jpg",
            Position = 0,
            CreatedAtUtc = occurredAtUtc
        };

        var variantId = Guid.NewGuid();
        var variantStock = new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = 7,
            ReservedQuantity = 2,
            CreatedAt = occurredAtUtc
        };

        var variant = new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "RUNNER-PRO-42",
            Price = 130m,
            OnlinePrice = 150m,
            IsDefault = true,
            IsActive = true,
            ImageUrl = "https://cdn.shopixy.test/products/stale-legacy-variant.jpg",
            MediaId = variantMedia.Id,
            Media = variantMedia,
            SortOrder = 0,
            CreatedAt = occurredAtUtc,
            Stocks = [variantStock]
        };

        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Category = category,
            Name = "Runner Pro",
            NameAr = "Runner Pro AR",
            Description = "Lightweight runner.",
            DescriptionAr = "Lightweight runner AR.",
            FeaturesJson = "[\"Breathable upper\",\"Cushioned sole\"]",
            FaqJson = "[{\"question\":\"Is it waterproof?\",\"answer\":\"No\"}]",
            TagsJson = "[\"sport\",\"runner\"]",
            Slug = "runner-pro",
            SalePrice = 120m,
            OnlinePrice = 0m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            TrackStock = true,
            AllowBackorder = false,
            CreatedAt = occurredAtUtc,
            UpdatedAt = occurredAtUtc.AddMinutes(5),
            Images = "https://cdn.shopixy.test/products/stale-legacy-product.jpg",
            Media = [productMedia, variantMedia],
            Variants = [variant]
        };

        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            commerceHubCategoryId,
            commerceHubPrice: null);

        dbContext.AddRange(tenant, category, commerceHubCategory, warehouse, product, listing);
        await dbContext.SaveChangesAsync();

        MarketplaceProductReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceProductReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(It.IsAny<MarketplaceProductReadModelDocument>(), occurredAtUtc, 3, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<MarketplaceProductReadModelDocument, DateTime, int, long, CancellationToken>((document, _, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceProductReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(productId, tenantId, eventId, occurredAtUtc, 3);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        Assert.Equal(productId, capturedDocument!.ProductId);
        Assert.Equal(commerceHubCategoryId, capturedDocument.CategoryId);
        Assert.Equal("Runner Pro", capturedDocument.Name);
        Assert.Equal("Runner Pro AR", capturedDocument.NameAr);
        Assert.Equal("Lightweight runner.", capturedDocument.Description);
        Assert.Equal("Lightweight runner AR.", capturedDocument.DescriptionAr);
        Assert.Equal("[\"Breathable upper\",\"Cushioned sole\"]", capturedDocument.FeaturesJson);
        Assert.Equal("[{\"question\":\"Is it waterproof?\",\"answer\":\"No\"}]", capturedDocument.FaqJson);
        Assert.Equal("[\"sport\",\"runner\"]", capturedDocument.TagsJson);
        Assert.Equal("runner-pro", capturedDocument.Slug);
        Assert.Equal(150m, capturedDocument.Price);
        Assert.Equal("EGP", capturedDocument.Currency);
        Assert.Equal("Marketplace Shoes", capturedDocument.Category);
        Assert.Equal(tenantId, capturedDocument.VendorId);
        Assert.Equal("Vendor One", capturedDocument.VendorName);
        Assert.True(capturedDocument.IsAvailable);
        Assert.Equal(
            [
                "https://cdn.shopixy.test/products/runner-pro-variant.jpg",
                "https://cdn.shopixy.test/products/runner-pro-cover.jpg"
            ],
            capturedDocument.Images);
        Assert.Equal(eventId, capturedDocument.LastEventId);
        Assert.Equal(occurredAtUtc, capturedDocument.LastEventOccurredAtUtc);
        Assert.Equal(3, capturedDocument.LastEventVersion);
        Assert.True(capturedDocument.LastEntityMutationVersion > 0);
    }

    [Fact]
    public async Task ProductProjection_RebuildAsync_ReturnsSkippedStale_WhenRepositoryRejectsOlderEvent()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 29, 19, 0, 0, DateTimeKind.Utc);
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            commerceHubCategoryId,
            commerceHubPrice: null);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Vendor Two",
                Currency = "USD",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Accessories",
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = commerceHubCategoryId,
                Name = "Marketplace Accessories",
                Slug = "marketplace-accessories",
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Travel Bag",
                SalePrice = 80m,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = false,
                CreatedAt = occurredAtUtc,
                Variants =
                [
                    new ProductVariant
                    {
                        Id = variantId,
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 80m,
                        OnlinePrice = 80m,
                        IsActive = true,
                        IsDefault = true,
                        CreatedAt = occurredAtUtc
                    }
                ]
            },
            listing);

        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceProductReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(It.IsAny<MarketplaceProductReadModelDocument>(), occurredAtUtc, 1, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.SkippedStale);

        var service = new MarketplaceProductReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(productId, tenantId, Guid.NewGuid(), occurredAtUtc, 1);

        Assert.Equal(MarketplaceProjectionResult.SkippedStale, result);
    }

    [Fact]
    public async Task ProductProjection_RebuildAsync_DeletesProjection_WhenTenantLineageIsInactive()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 22, 13, 0, 0, DateTimeKind.Utc);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Inactive Vendor",
                IsActive = false,
                Currency = "EGP"
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Stale category",
                CreatedAt = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Stale tenant product",
                SalePrice = 80m,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                CreatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceProductReadRepository>();
        repository
            .Setup(x => x.DeleteIfFreshAsync(
                tenantId,
                productId,
                occurredAtUtc,
                6,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceProductReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(productId, tenantId, Guid.NewGuid(), occurredAtUtc, 6);

        Assert.Equal(MarketplaceProjectionResult.Deleted, result);
        repository.Verify(
            x => x.UpsertAsync(
                It.IsAny<MarketplaceProductReadModelDocument>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProductProjection_RebuildAsync_DeletesProjection_WhenSqlListingIsNotPublished()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Marketplace Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Local Category",
                CreatedAt = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Unlisted marketplace product",
                SalePrice = 55m,
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                CreatedAt = occurredAtUtc,
                Variants =
                [
                    new ProductVariant
                    {
                        Id = variantId,
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 55m,
                        OnlinePrice = 55m,
                        IsActive = true,
                        IsDefault = true,
                        CreatedAt = occurredAtUtc
                    }
                ]
            });
        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceProductReadRepository>();
        repository
            .Setup(x => x.DeleteIfFreshAsync(
                tenantId,
                productId,
                occurredAtUtc,
                13,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceProductReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(productId, tenantId, Guid.NewGuid(), occurredAtUtc, 13);

        Assert.Equal(MarketplaceProjectionResult.Deleted, result);
        repository.Verify(
            x => x.UpsertAsync(
                It.IsAny<MarketplaceProductReadModelDocument>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProductProjection_RebuildAsync_DeletesProjection_WhenCommerceHubCategoryLineageIsInvalid()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var parentCommerceHubCategoryId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc);
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            commerceHubCategoryId,
            commerceHubPrice: null);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Invalid category vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Local Category",
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = parentCommerceHubCategoryId,
                Name = "Inactive Parent",
                Slug = "inactive-parent",
                IsActive = false,
                CreatedAtUtc = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = commerceHubCategoryId,
                Name = "Active Child With Stale Parent",
                Slug = "active-child-stale-parent",
                ParentId = parentCommerceHubCategoryId,
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Category lineage replay",
                SalePrice = 55m,
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                CreatedAt = occurredAtUtc,
                Variants =
                [
                    new ProductVariant
                    {
                        Id = variantId,
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 55m,
                        OnlinePrice = 55m,
                        IsActive = true,
                        IsDefault = true,
                        CreatedAt = occurredAtUtc
                    }
                ]
            },
            listing);
        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceProductReadRepository>();
        repository
            .Setup(x => x.DeleteIfFreshAsync(
                tenantId,
                productId,
                occurredAtUtc,
                14,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceProductReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(productId, tenantId, Guid.NewGuid(), occurredAtUtc, 14);

        Assert.Equal(MarketplaceProjectionResult.Deleted, result);
        repository.Verify(
            x => x.UpsertAsync(
                It.IsAny<MarketplaceProductReadModelDocument>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProductProjection_RebuildAsync_DeletesVariantProduct_WhenReplayTargetsSupersededSemanticOwner()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 22, 9, 0, 0, DateTimeKind.Utc);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Recommendation Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Replay-sensitive category",
                CreatedAt = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Merged listing replay",
                SalePrice = 75m,
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                CreatedAt = occurredAtUtc,
                Variants =
                [
                    new ProductVariant
                    {
                        Id = variantId,
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 75m,
                        OnlinePrice = 75m,
                        IsActive = true,
                        IsDefault = true,
                        SemanticLifecycleState = ProductVariantSemanticLifecycleState.Merged,
                        SemanticSupersededAtUtc = occurredAtUtc,
                        SemanticOperationId = Guid.NewGuid(),
                        CreatedAt = occurredAtUtc
                    }
                ]
            });
        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceProductReadRepository>();
        repository
            .Setup(x => x.DeleteIfFreshAsync(
                tenantId,
                productId,
                occurredAtUtc,
                11,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceProductReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(
            productId,
            tenantId,
            Guid.NewGuid(),
            occurredAtUtc,
            11);

        Assert.Equal(MarketplaceProjectionResult.Deleted, result);
        repository.Verify(
            x => x.UpsertAsync(
                It.IsAny<MarketplaceProductReadModelDocument>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OrderProjection_RebuildAsync_UsesOnlineCustomerAndMapsLineItems()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 29, 20, 0, 0, DateTimeKind.Utc);
        var paidAtUtc = occurredAtUtc.AddMinutes(10);
        var deliveredAtUtc = occurredAtUtc.AddDays(2);
        var financialPostedAtUtc = occurredAtUtc.AddDays(1);
        var onlineCustomerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-2026-1001",
            CustomerId = customerId,
            OnlineCustomerId = onlineCustomerId,
            Customer = new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                Name = "Fallback POS Customer",
                CreatedAt = occurredAtUtc
            },
            OnlineCustomer = new OnlineCustomer
            {
                Id = onlineCustomerId,
                TenantId = tenantId,
                Email = "maya@example.com",
                NormalizedEmail = "maya@example.com",
                DisplayName = "Maya Hassan",
                CreatedAtUtc = occurredAtUtc
            },
            Status = OrderStatus.Delivered,
            FinancialPostingStatus = FinancialPostingStatus.Posted,
            FinancialPostedAtUtc = financialPostedAtUtc,
            Total = 300m,
            Currency = "EGP",
            PaidAt = paidAtUtc,
            DeliveredAt = deliveredAtUtc,
            CreatedAt = occurredAtUtc,
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = productId,
                    VariantId = variantId,
                    ProductName = "Runner Pro",
                    VariantName = "Black / 42",
                    SKU = "RUNNER-PRO-42",
                    Quantity = 2,
                    UnitPrice = 150m,
                    Total = 300m,
                    ImageUrl = "https://cdn.shopixy.test/orders/runner-pro.jpg",
                    CreatedAt = occurredAtUtc
                }
            ]
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        MarketplaceOrderReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceOrderReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(It.IsAny<MarketplaceOrderReadModelDocument>(), occurredAtUtc, 2, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<MarketplaceOrderReadModelDocument, DateTime, int, long, CancellationToken>((document, _, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceOrderReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceOrderReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(orderId, tenantId, eventId, occurredAtUtc, 2);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        Assert.Equal("Maya Hassan", capturedDocument!.CustomerName);
        Assert.Equal("Delivered", capturedDocument.Status);
        Assert.Equal(300m, capturedDocument.TotalAmount);
        Assert.Equal("EGP", capturedDocument.Currency);
        Assert.Equal(paidAtUtc, capturedDocument.PaidAtUtc);
        Assert.Equal(deliveredAtUtc, capturedDocument.DeliveredAtUtc);
        
        // Financial posting assertions
        Assert.Equal("Posted", capturedDocument.FinancialPostingStatus);
        Assert.True(capturedDocument.IsFinanciallyPosted);
        Assert.Equal(financialPostedAtUtc, capturedDocument.FinancialPostedAtUtc);
        
        Assert.Single(capturedDocument.Items);
        Assert.Equal(productId, capturedDocument.Items[0].ProductId);
        Assert.Equal("Runner Pro", capturedDocument.Items[0].ProductName);
        Assert.Equal("RUNNER-PRO-42", capturedDocument.Items[0].Sku);
        Assert.Equal(eventId, capturedDocument.LastEventId);
        Assert.True(capturedDocument.LastEntityMutationVersion > 0);
    }

    [Fact]
    public async Task OrderProjection_RebuildAsync_MapsFinancialPostingStatus_NotRequired()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-2026-1002",
            CustomerId = Guid.NewGuid(),
            Customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Test Customer",
                CreatedAt = occurredAtUtc
            },
            Status = OrderStatus.Completed,
            FinancialPostingStatus = FinancialPostingStatus.NotRequired,
            FinancialPostedAtUtc = null,
            Total = 100m,
            Currency = "USD",
            CreatedAt = occurredAtUtc,
            Items = []
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        MarketplaceOrderReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceOrderReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(It.IsAny<MarketplaceOrderReadModelDocument>(), occurredAtUtc, 1, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<MarketplaceOrderReadModelDocument, DateTime, int, long, CancellationToken>((document, _, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceOrderReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceOrderReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(orderId, tenantId, eventId, occurredAtUtc, 1);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        
        // Financial posting assertions for NotRequired status
        Assert.Equal("NotRequired", capturedDocument!.FinancialPostingStatus);
        Assert.True(capturedDocument.IsFinanciallyPosted); // NotRequired is considered "posted"
        Assert.Null(capturedDocument.FinancialPostedAtUtc);
    }

    [Fact]
    public async Task OrderProjection_RebuildAsync_MapsFinancialPostingStatus_Pending()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 30, 11, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-2026-1003",
            CustomerId = Guid.NewGuid(),
            Customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Test Customer",
                CreatedAt = occurredAtUtc
            },
            Status = OrderStatus.Pending,
            FinancialPostingStatus = FinancialPostingStatus.Pending,
            FinancialPostedAtUtc = null,
            Total = 200m,
            Currency = "EUR",
            CreatedAt = occurredAtUtc,
            Items = []
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        MarketplaceOrderReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceOrderReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(It.IsAny<MarketplaceOrderReadModelDocument>(), occurredAtUtc, 1, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<MarketplaceOrderReadModelDocument, DateTime, int, long, CancellationToken>((document, _, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceOrderReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceOrderReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(orderId, tenantId, eventId, occurredAtUtc, 1);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        
        // Financial posting assertions for Pending status
        Assert.Equal("Pending", capturedDocument!.FinancialPostingStatus);
        Assert.False(capturedDocument.IsFinanciallyPosted); // Pending is NOT considered "posted"
        Assert.Null(capturedDocument.FinancialPostedAtUtc);
    }

    [Fact]
    public async Task OrderProjection_RebuildAsync_MapsFinancialPostingStatus_PostingFailed()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 30, 12, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-2026-1004",
            CustomerId = Guid.NewGuid(),
            Customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Test Customer",
                CreatedAt = occurredAtUtc
            },
            Status = OrderStatus.Completed,
            FinancialPostingStatus = FinancialPostingStatus.PostingFailed,
            FinancialPostedAtUtc = null,
            Total = 150m,
            Currency = "GBP",
            CreatedAt = occurredAtUtc,
            Items = []
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        MarketplaceOrderReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceOrderReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(It.IsAny<MarketplaceOrderReadModelDocument>(), occurredAtUtc, 1, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<MarketplaceOrderReadModelDocument, DateTime, int, long, CancellationToken>((document, _, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceOrderReadModelProjectionService(
            dbContext,
            repository.Object,
            NullLogger<MarketplaceOrderReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(orderId, tenantId, eventId, occurredAtUtc, 1);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        
        // Financial posting assertions for PostingFailed status
        Assert.Equal("PostingFailed", capturedDocument!.FinancialPostingStatus);
        Assert.False(capturedDocument.IsFinanciallyPosted); // PostingFailed is NOT considered "posted"
        Assert.Null(capturedDocument.FinancialPostedAtUtc);
    }

    [Fact]
    public async Task HomepageProjection_RebuildAsync_DeletesTenantDocument_WhenTenantHasNoPublishedSections()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 29, 21, 0, 0, DateTimeKind.Utc);

        dbContext.HomepageSections.Add(
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                Type = "hero",
                Order = 0,
                Data = "{\"title\":\"Global Hero\"}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceHomepageReadRepository>();
        repository
            .Setup(x => x.DeleteIfFreshAsync(
                tenantId,
                occurredAtUtc,
                4,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceHomepageReadModelProjectionService(dbContext, repository.Object);

        var result = await service.RebuildAsync(tenantId, Guid.NewGuid(), occurredAtUtc, 4);

        Assert.Equal(MarketplaceProjectionResult.Deleted, result);
        repository.Verify(
            x => x.DeleteIfFreshAsync(
                tenantId,
                occurredAtUtc,
                4,
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            x => x.UpsertAsync(It.IsAny<MarketplaceHomepageReadModelDocument>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HomepageProjection_RebuildAsync_DeletesTenantDocument_WhenTenantLineageIsInactive()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 22, 13, 30, 0, DateTimeKind.Utc);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Inactive homepage vendor",
                IsActive = false
            },
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "hero",
                Order = 0,
                Data = "{\"title\":\"Stale tenant hero\"}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceHomepageReadRepository>();
        repository
            .Setup(x => x.DeleteIfFreshAsync(
                tenantId,
                occurredAtUtc,
                7,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceHomepageReadModelProjectionService(dbContext, repository.Object);

        var result = await service.RebuildAsync(tenantId, Guid.NewGuid(), occurredAtUtc, 7);

        Assert.Equal(MarketplaceProjectionResult.Deleted, result);
        repository.Verify(
            x => x.UpsertAsync(
                It.IsAny<MarketplaceHomepageReadModelDocument>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HomepageProjection_RebuildAsync_ReturnsSkippedStale_WhenDeleteIfFreshRejectsOlderEvent()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 3, 29, 21, 0, 0, DateTimeKind.Utc);

        dbContext.HomepageSections.Add(
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                Type = "hero",
                Order = 0,
                Data = "{\"title\":\"Global Hero\"}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        var repository = new Mock<IMarketplaceHomepageReadRepository>();
        repository
            .Setup(x => x.DeleteIfFreshAsync(
                tenantId,
                occurredAtUtc,
                4,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.SkippedStale);

        var service = new MarketplaceHomepageReadModelProjectionService(dbContext, repository.Object);

        var result = await service.RebuildAsync(tenantId, Guid.NewGuid(), occurredAtUtc, 4);

        Assert.Equal(MarketplaceProjectionResult.SkippedStale, result);
        repository.Verify(
            x => x.DeleteIfFreshAsync(
                tenantId,
                occurredAtUtc,
                4,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HomepageProjection_RebuildAsync_RemovesCuratedProducts_WhenSqlNoLongerPublishesListingVisibility()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            commerceHubCategoryId,
            commerceHubPrice: null);
        var occurredAtUtc = new DateTime(2026, 5, 21, 9, 0, 0, DateTimeKind.Utc);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Shoes",
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = commerceHubCategoryId,
                Name = "Global Shoes",
                Slug = "global-shoes",
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Hidden Runner",
                SalePrice = 100m,
                IsActive = true,
                IsOnlineVisible = false,
                PublishStatus = PublishStatus.Published,
                CreatedAt = occurredAtUtc,
                Variants =
                [
                    new ProductVariant
                    {
                        Id = variantId,
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 100m,
                        OnlinePrice = 100m,
                        IsActive = true,
                        IsDefault = true,
                        CreatedAt = occurredAtUtc
                    }
                ]
            },
            listing,
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "hero",
                Order = 0,
                Data = "{\"title\":\"Hero\"}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            },
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "products",
                Order = 1,
                Data = $"{{\"title\":\"Featured\",\"source\":\"curated\",\"productIds\":[\"{listing.Id:D}\"],\"layout\":\"grid\"}}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        MarketplaceHomepageReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceHomepageReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(It.IsAny<MarketplaceHomepageReadModelDocument>(), occurredAtUtc, 8, It.IsAny<CancellationToken>()))
            .Callback<MarketplaceHomepageReadModelDocument, DateTime, int, CancellationToken>((document, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceHomepageReadModelProjectionService(dbContext, repository.Object);

        var result = await service.RebuildAsync(tenantId, Guid.NewGuid(), occurredAtUtc, 8);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        Assert.Single(capturedDocument!.Sections);
        Assert.Equal("hero", capturedDocument.Sections[0].Type);
    }

    [Fact]
    public async Task HomepageProjection_RebuildAsync_RemovesCuratedRecommendation_WhenListingVariantWasSuperseded()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 22, 9, 30, 0, DateTimeKind.Utc);
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            commerceHubCategoryId,
            commerceHubPrice: null);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Recommendation Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Recommendations",
                CreatedAt = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = commerceHubCategoryId,
                Name = "Global",
                Slug = "global",
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Superseded recommendation",
                SalePrice = 40m,
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                CreatedAt = occurredAtUtc,
                Variants =
                [
                    new ProductVariant
                    {
                        Id = variantId,
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 40m,
                        OnlinePrice = 40m,
                        IsActive = true,
                        IsDefault = true,
                        SemanticLifecycleState = ProductVariantSemanticLifecycleState.SplitParent,
                        SemanticSupersededAtUtc = occurredAtUtc,
                        SemanticOperationId = Guid.NewGuid(),
                        CreatedAt = occurredAtUtc
                    }
                ]
            },
            listing,
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "hero",
                Order = 0,
                Data = "{\"title\":\"Hero\"}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            },
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "products",
                Order = 1,
                Data = $"{{\"title\":\"Replay\",\"source\":\"curated\",\"productIds\":[\"{listing.Id:D}\"]}}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        MarketplaceHomepageReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceHomepageReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(
                It.IsAny<MarketplaceHomepageReadModelDocument>(),
                occurredAtUtc,
                12,
                It.IsAny<CancellationToken>()))
            .Callback<MarketplaceHomepageReadModelDocument, DateTime, int, CancellationToken>(
                (document, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceHomepageReadModelProjectionService(dbContext, repository.Object);

        var result = await service.RebuildAsync(tenantId, Guid.NewGuid(), occurredAtUtc, 12);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        Assert.Single(capturedDocument!.Sections);
        Assert.Equal("hero", capturedDocument.Sections[0].Type);
    }

    [Fact]
    public async Task HomepageProjection_RebuildAsync_RemovesCuratedCategories_WhenCommerceHubCategoryLineageIsInvalid()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var parentCommerceHubCategoryId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 25, 10, 30, 0, DateTimeKind.Utc);

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Homepage Vendor",
                Currency = "EGP",
                Status = TenantStatus.Active
            },
            new CommerceHubCategory
            {
                Id = parentCommerceHubCategoryId,
                Name = "Inactive Marketplace Parent",
                Slug = "inactive-marketplace-parent",
                IsActive = false,
                CreatedAtUtc = occurredAtUtc
            },
            new CommerceHubCategory
            {
                Id = commerceHubCategoryId,
                Name = "Active Child Category",
                Slug = "active-child-category",
                ParentId = parentCommerceHubCategoryId,
                IsActive = true,
                CreatedAtUtc = occurredAtUtc
            },
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "hero",
                Order = 0,
                Data = "{\"title\":\"Hero\"}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            },
            new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                TenantId = tenantId,
                Type = "categories",
                Order = 1,
                Data = $"{{\"title\":\"Stale Categories\",\"source\":\"curated\",\"categoryIds\":[\"{commerceHubCategoryId:D}\"]}}",
                Version = 1,
                SchemaVersion = 1,
                IsActive = true,
                IsPublished = true,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc
            });
        await dbContext.SaveChangesAsync();

        MarketplaceHomepageReadModelDocument? capturedDocument = null;
        var repository = new Mock<IMarketplaceHomepageReadRepository>();
        repository
            .Setup(x => x.UpsertAsync(
                It.IsAny<MarketplaceHomepageReadModelDocument>(),
                occurredAtUtc,
                15,
                It.IsAny<CancellationToken>()))
            .Callback<MarketplaceHomepageReadModelDocument, DateTime, int, CancellationToken>(
                (document, _, _, _) => capturedDocument = document)
            .ReturnsAsync(ProjectionWriteResult.Applied);

        var service = new MarketplaceHomepageReadModelProjectionService(dbContext, repository.Object);

        var result = await service.RebuildAsync(tenantId, Guid.NewGuid(), occurredAtUtc, 15);

        Assert.Equal(MarketplaceProjectionResult.Applied, result);
        Assert.NotNull(capturedDocument);
        Assert.Single(capturedDocument!.Sections);
        Assert.Equal("hero", capturedDocument.Sections[0].Type);
    }

    [Fact]
    public async Task HomepageProjection_FilterPublicReadAsync_RemovesCachedCuratedReference_WhenSqlAuthorityIsMissing()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 5, 27, 12, 0, 0, DateTimeKind.Utc);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Marketplace Vendor",
            Currency = "EGP",
            Status = TenantStatus.Active
        });
        await dbContext.SaveChangesAsync();

        var document = new MarketplaceHomepageReadModelDocument
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
                    DataJson = "{\"title\":\"Still public\"}",
                    Version = 1,
                    SchemaVersion = 1,
                    IsActive = true
                },
                new MarketplaceHomepageSectionDocument
                {
                    Type = "products",
                    Order = 1,
                    DataJson = $"{{\"source\":\"curated\",\"productIds\":[\"{Guid.NewGuid():D}\"]}}",
                    Version = 1,
                    SchemaVersion = 1,
                    IsActive = true
                }
            ],
            LastUpdated = occurredAtUtc,
            LastEventOccurredAtUtc = occurredAtUtc,
            LastEventVersion = 1
        };

        var service = new MarketplaceHomepageReadModelProjectionService(
            dbContext,
            Mock.Of<IMarketplaceHomepageReadRepository>());

        var filtered = await service.FilterPublicReadAsync(document);

        Assert.NotNull(filtered);
        Assert.Single(filtered!.Sections);
        Assert.Equal("hero", filtered.Sections[0].Type);
    }

    [Fact]
    public async Task HomepageProjectionConsumer_IgnoresDuplicateEvents()
    {
        await using var dbContext = CreateDbContext();
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        dbContext.Set<ProcessedEvent>().Add(
            ProcessedEvent.Create(
                eventId,
                nameof(HomepageProjectionConsumer),
                nameof(HomepageSectionsPublishedEvent),
                tenantId,
                tenantId,
                "corr-1"));
        await dbContext.SaveChangesAsync();

        var projectionService = new Mock<IMarketplaceHomepageReadModelProjectionService>();
        var consumer = new HomepageProjectionConsumer(
            dbContext,
            projectionService.Object,
            NullLogger<HomepageProjectionConsumer>.Instance);

        var context = CreateConsumeContext(
            new HomepageSectionsPublishedEvent
            {
                EventId = eventId,
                TenantId = tenantId,
                CorrelationId = "corr-1",
                PublishedAtUtc = new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc),
                EventVersion = 2
            });

        await consumer.Consume(context.Object);

        projectionService.Verify(
            x => x.RebuildAsync(It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HomepageProjectionConsumer_PersistsProcessedEvent_AfterSuccessfulProjection()
    {
        await using var dbContext = CreateDbContext();
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var projectionService = new Mock<IMarketplaceHomepageReadModelProjectionService>();
        projectionService
            .Setup(x => x.RebuildAsync(tenantId, eventId, It.IsAny<DateTime>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MarketplaceProjectionResult.Applied);

        var consumer = new HomepageProjectionConsumer(
            dbContext,
            projectionService.Object,
            NullLogger<HomepageProjectionConsumer>.Instance);

        var context = CreateConsumeContext(
            new HomepageSectionsPublishedEvent
            {
                EventId = eventId,
                TenantId = tenantId,
                CorrelationId = "corr-2",
                PublishedAtUtc = new DateTime(2026, 3, 29, 22, 30, 0, DateTimeKind.Utc),
                EventVersion = 5
            });

        await consumer.Consume(context.Object);

        var processed = await dbContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.EventId == eventId && x.ConsumerName == nameof(HomepageProjectionConsumer));

        Assert.Equal(nameof(HomepageSectionsPublishedEvent), processed.EventType);
        Assert.Equal(tenantId, processed.TenantId);
        Assert.Equal("corr-2", processed.CorrelationId);
    }

    [Fact]
    public void IntegrationEvent_EventVersion_AliasesVersion()
    {
        var @event = new HomepageSectionsPublishedEvent
        {
            EventVersion = 7
        };

        Assert.Equal(7, @event.Version);

        @event.Version = 9;

        Assert.Equal(9, @event.EventVersion);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static Mock<ConsumeContext<HomepageSectionsPublishedEvent>> CreateConsumeContext(HomepageSectionsPublishedEvent message)
    {
        var context = new Mock<ConsumeContext<HomepageSectionsPublishedEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(x => x.CorrelationId).Returns(Guid.NewGuid());
        return context;
    }
}
