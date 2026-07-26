using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services.MarketplaceReviews;

namespace PointOfSale.Tests.CommerceHub;

public sealed class MarketplaceProductReviewModerationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IMarketplaceEventOutboxService> _eventOutbox = new();
    private readonly MarketplaceProductReviewModerationService _service;

    public MarketplaceProductReviewModerationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new MarketplaceProductReviewModerationService(
            _dbContext,
            _eventOutbox.Object,
            NullLogger<MarketplaceProductReviewModerationService>.Instance);
    }

    [Fact]
    public async Task HandleCreatedAsync_WhenVerifiedAndClean_ShouldApprove()
    {
        var review = ProductReview.Create(Guid.NewGuid(), Guid.NewGuid(), 5, "Great", "Loved it.", true);
        _dbContext.ProductReviews.Add(review);
        await _dbContext.SaveChangesAsync();

        await _service.HandleCreatedAsync(new ReviewCreatedEvent
        {
            CorrelationId = "corr-approve",
            EntityId = review.Id,
            ProductId = review.ProductId,
            UserId = review.UserId,
            Rating = review.Rating,
            Title = review.Title,
            IsVerifiedPurchase = review.IsVerifiedPurchase
        });

        var updated = await _dbContext.ProductReviews.FindAsync(review.Id);
        Assert.NotNull(updated);
        Assert.Equal(ProductReviewStatus.Approved, updated!.Status);
        _eventOutbox.Verify(
            item => item.EnqueueAsync(
                It.Is<ReviewApprovedEvent>(evt => evt.EntityId == review.Id && evt.ProductId == review.ProductId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleCreatedAsync_WhenBurstPatternDetected_ShouldRemainPending()
    {
        var userId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            var existing = ProductReview.Create(Guid.NewGuid(), userId, 5, "Spam", "Repeated content", true);
            _dbContext.ProductReviews.Add(existing);
        }

        var review = ProductReview.Create(Guid.NewGuid(), userId, 5, "Spam", "Repeated content", true);
        _dbContext.ProductReviews.Add(review);
        await _dbContext.SaveChangesAsync();

        await _service.HandleCreatedAsync(new ReviewCreatedEvent
        {
            CorrelationId = "corr-pending",
            EntityId = review.Id,
            ProductId = review.ProductId,
            UserId = review.UserId,
            Rating = review.Rating,
            Title = review.Title,
            IsVerifiedPurchase = review.IsVerifiedPurchase
        });

        var updated = await _dbContext.ProductReviews.FindAsync(review.Id);
        Assert.NotNull(updated);
        Assert.Equal(ProductReviewStatus.Pending, updated!.Status);
        _eventOutbox.Verify(
            item => item.EnqueueAsync(It.IsAny<ReviewApprovedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
