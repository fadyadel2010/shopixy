using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Consumers;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Homepage;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.API.Services.MarketplaceFooter;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.DTOs.MarketplaceReviews;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace PointOfSale.Tests.CommerceHub;

public sealed class MarketplaceReviewInfrastructureTests
{
    [Fact]
    public void CommerceHubSearchDocumentMapper_ShouldMapRatingFields()
    {
        var source = new CommerceHubProductDocument
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Mapped Product",
            CategoryId = Guid.NewGuid(),
            Currency = "EGP",
            Price = 120m,
            AvailableStock = 5m,
            IsPublished = true,
            AverageRating = 4.4m,
            ReviewCount = 37,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var result = CommerceHubSearchDocumentMapper.Map(source);

        Assert.Equal(4.4d, result.Rating, 1);
        Assert.Equal(37, result.ReviewCount);
    }

    [Fact]
    public async Task RedisInvalidationConsumer_WhenReviewApproved_ShouldRemoveReviewCaches()
    {
        await using var dbContext = CreateDbContext();
        var cacheService = new Mock<IRedisJsonCacheService>();
        var consumer = new RedisInvalidationConsumer(
            Mock.Of<IHomepageSectionCacheService>(),
            Mock.Of<ICmsPageCacheService>(),
            Mock.Of<IMarketplaceCmsPageCacheService>(),
            Mock.Of<IStorefrontFooterCacheService>(),
            Mock.Of<IMarketplaceFooterCacheService>(),
            cacheService.Object,
            dbContext,
            Options.Create(new RedisSettings { InstanceName = "test" }),
            NullLogger<RedisInvalidationConsumer>.Instance);

        var productId = Guid.NewGuid();
        var context = Mock.Of<MassTransit.ConsumeContext<ReviewApprovedEvent>>(item =>
            item.Message == new ReviewApprovedEvent
            {
                CorrelationId = "corr",
                EntityId = Guid.NewGuid(),
                ProductId = productId,
                UserId = Guid.NewGuid(),
                Rating = 5,
                IsVerifiedPurchase = true,
                ApprovedAtUtc = DateTime.UtcNow
            } &&
            item.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        cacheService.Verify(
            item => item.RemoveAsync(
                CacheKeyBuilder.MarketplaceProductRatingSummaryKey("test", productId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        cacheService.Verify(
            item => item.RemoveByPatternAsync(
                CacheKeyBuilder.MarketplaceProductReviewsPatternKey("test", productId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RedisInvalidationConsumer_WhenReviewReplayIsDuplicated_ShouldCollapseBeforeCacheSideEffects()
    {
        await using var dbContext = CreateDbContext();
        var cacheService = new Mock<IRedisJsonCacheService>();
        var consumer = new RedisInvalidationConsumer(
            Mock.Of<IHomepageSectionCacheService>(),
            Mock.Of<ICmsPageCacheService>(),
            Mock.Of<IMarketplaceCmsPageCacheService>(),
            Mock.Of<IStorefrontFooterCacheService>(),
            Mock.Of<IMarketplaceFooterCacheService>(),
            cacheService.Object,
            dbContext,
            Options.Create(new RedisSettings { InstanceName = "test" }),
            NullLogger<RedisInvalidationConsumer>.Instance);

        var productId = Guid.NewGuid();
        var @event = new ReviewApprovedEvent
        {
            CorrelationId = "corr",
            EntityId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProductId = productId,
            UserId = Guid.NewGuid(),
            Rating = 5,
            IsVerifiedPurchase = true,
            ApprovedAtUtc = DateTime.UtcNow
        };

        var context = Mock.Of<MassTransit.ConsumeContext<ReviewApprovedEvent>>(item =>
            item.Message == @event &&
            item.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);
        await consumer.Consume(context);

        cacheService.Verify(
            item => item.RemoveAsync(
                CacheKeyBuilder.MarketplaceProductRatingSummaryKey("test", productId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        cacheService.Verify(
            item => item.RemoveByPatternAsync(
                CacheKeyBuilder.MarketplaceProductReviewsPatternKey("test", productId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Equal(1, await dbContext.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task SuperAdminProductReviewService_ApproveAsync_ShouldPublishApprovedEvent()
    {
        await using var dbContext = CreateDbContext();
        var outbox = new Mock<IMarketplaceEventOutboxService>();
        var service = new SuperAdminProductReviewService(dbContext, outbox.Object);

        var customer = CommerceHubCustomer.Create("admin@test.local", "Admin Review Customer", "01000000000", "hash");
        var productId = Guid.NewGuid();

        dbContext.CommerceHubCustomers.Add(customer);
        dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = Guid.NewGuid(),
            Name = "Admin Review Product",
            CategoryId = Guid.NewGuid(),
            SalePrice = 50m,
            OnlinePrice = 50m,
            IsActive = true,
            IsOnlineVisible = true
        });

        var review = ProductReview.Create(productId, customer.Id, 5, "Pending", "Looks good", true);
        dbContext.ProductReviews.Add(review);
        await dbContext.SaveChangesAsync();

        var result = await service.ApproveAsync(review.Id, "corr-admin");

        Assert.Equal(ProductReviewStatus.Approved, result.Status);
        outbox.Verify(
            item => item.EnqueueAsync(
                It.Is<ReviewApprovedEvent>(evt => evt.EntityId == review.Id && evt.ProductId == productId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
