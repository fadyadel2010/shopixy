using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;
using Xunit;


namespace PointOfSale.Tests.Integration;

public class TrustEngineIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ApplicationDbContext _context;
    private readonly IMarketplaceProfileService _profileService;
    private readonly IServiceCatalogService _catalogService;
    private readonly IServiceRequestService _requestService;
    private readonly IServiceReviewService _reviewService;
    private readonly IProviderTrustService _trustService;
    private readonly IChatService _chatService;
    private readonly Guid _tenantId;
    private readonly TestTenantContext _tenantContext;
    private readonly Mock<IEventPublisher> _publishEndpointMock;

    public TrustEngineIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _tenantId = _fixture.TenantId;
        _tenantContext = _fixture.TenantContext;
        _context = _fixture.Context;
        _publishEndpointMock = _fixture.PublishEndpointMock;
        if (!_context.Tenants.IgnoreQueryFilters().Any(tenant => tenant.Id == _tenantId))
        {
            _context.Tenants.Add(new Tenant
            {
                Id = _tenantId,
                Name = "Trust Engine Test Tenant",
                Slug = $"trust-{_tenantId:N}",
                IsActive = true,
                Status = TenantStatus.Active
            });
            _context.SaveChanges();
        }

        _publishEndpointMock
            .Setup(publisher => publisher.PublishAsync(It.IsAny<ProviderTrustUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _profileService = new MarketplaceProfileService(_context, _tenantContext);
        _catalogService = new ServiceCatalogService(_context, _tenantContext, _publishEndpointMock.Object, NullLogger<ServiceCatalogService>.Instance);
        _chatService = new ChatService(_context, _tenantContext);
        _requestService = new ServiceRequestService(_context, _tenantContext, _chatService, _publishEndpointMock.Object, NullLogger<ServiceRequestService>.Instance);
        _reviewService = new ServiceReviewService(_context, _tenantContext, _publishEndpointMock.Object, NullLogger<ServiceReviewService>.Instance);
        _trustService = new ProviderTrustService(_context);
    }

    [Fact]
    public async Task CreateReview_PublishesServiceReviewCreatedEvent()
    {
        var providerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        var profile = await _profileService.CreateProfileAsync(
            providerId,
            new CreateMarketplaceProfileDto("Provider", "Bio")
        );

        var service = await _catalogService.CreateServiceAsync(
            profile.Id,
            providerId,
            new CreateServiceDto("Service", "Desc")
        );

        var request = await _requestService.RequestServiceAsync(
            buyerId,
            new RequestServiceDto(service.Id)
        );

        await _requestService.AcceptRequestAsync(request.Id, providerId);
        await _requestService.MarkCompletedByProviderAsync(request.Id, providerId);
        await _requestService.ConfirmCompletedByBuyerAsync(request.Id, buyerId);

        await _reviewService.CreateReviewAsync(
            buyerId,
            new CreateReviewDto(request.Id, 5, "Great!")
        );

        // Verify event was published
        _publishEndpointMock.Verify(
            x => x.PublishAsync(
                It.Is<ServiceReviewCreatedEvent>(e =>
                    e.ProviderProfileId == profile.Id &&
                    e.Rating == 5
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task ProviderTrustConsumer_CreatesAggregateOnFirstReview()
    {
        var providerProfile = await SeedMarketplaceProfileAsync();
        var providerProfileId = providerProfile.Id;
        var authority = await SeedCompletedReviewAsync(providerProfile, 5);

        var consumer = new ProviderTrustConsumer(_context, _publishEndpointMock.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<ProviderTrustConsumer>>());

        var @event = new ServiceReviewCreatedEvent
        {
            ReviewId = authority.Review.Id,
            TenantId = _tenantId,
            ServiceRequestId = authority.Request.Id,
            ServiceId = authority.Request.ServiceId,
            ProviderUserId = authority.Request.ProviderUserId,
            ProviderProfileId = providerProfileId,
            ReviewerUserId = authority.Request.BuyerUserId,
            Rating = 5,
            CreatedAt = DateTime.UtcNow
        };

        var contextMock = Mock.Of<ConsumeContext<ServiceReviewCreatedEvent>>(c => c.Message == @event);
        await consumer.Consume(contextMock);

        var aggregate = await _context.Set<ProviderTrustAggregate>()
            .FirstOrDefaultAsync(t => t.ProviderProfileId == providerProfileId);

        Assert.NotNull(aggregate);
        Assert.Equal(1, aggregate!.TotalReviews);
        Assert.Equal(5, aggregate.SumRatings);
        Assert.Equal(5.00m, aggregate.AverageRating);
        Assert.Equal(1, aggregate.FiveStarCount);
    }

    [Fact]
    public async Task ProviderTrustConsumer_UpdatesAggregateOnSubsequentReviews()
    {
        var providerProfile = await SeedMarketplaceProfileAsync();
        var providerProfileId = providerProfile.Id;
        var firstAuthority = await SeedCompletedReviewAsync(providerProfile, 5);
        var secondAuthority = await SeedCompletedReviewAsync(providerProfile, 3);

        // Create initial aggregate
        var aggregate = ProviderTrustAggregate.Create(_tenantId, providerProfileId);
        _context.Set<ProviderTrustAggregate>().Add(aggregate);
        await _context.SaveChangesAsync();

        var consumer = new ProviderTrustConsumer(_context, _publishEndpointMock.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<ProviderTrustConsumer>>());

        // Apply first review
        var event1 = new ServiceReviewCreatedEvent
        {
            ReviewId = firstAuthority.Review.Id,
            TenantId = _tenantId,
            ServiceRequestId = firstAuthority.Request.Id,
            ServiceId = firstAuthority.Request.ServiceId,
            ProviderUserId = firstAuthority.Request.ProviderUserId,
            ProviderProfileId = providerProfileId,
            ReviewerUserId = firstAuthority.Request.BuyerUserId,
            Rating = 5,
            CreatedAt = DateTime.UtcNow
        };

        await consumer.Consume(Mock.Of<ConsumeContext<ServiceReviewCreatedEvent>>(c => c.Message == event1));

        // Apply second review
        var event2 = new ServiceReviewCreatedEvent
        {
            ReviewId = secondAuthority.Review.Id,
            TenantId = _tenantId,
            ServiceRequestId = secondAuthority.Request.Id,
            ServiceId = secondAuthority.Request.ServiceId,
            ProviderUserId = secondAuthority.Request.ProviderUserId,
            ProviderProfileId = providerProfileId,
            ReviewerUserId = secondAuthority.Request.BuyerUserId,
            Rating = 3,
            CreatedAt = DateTime.UtcNow
        };

        await consumer.Consume(Mock.Of<ConsumeContext<ServiceReviewCreatedEvent>>(c => c.Message == event2));

        var updatedAggregate = await _context.Set<ProviderTrustAggregate>()
            .FirstOrDefaultAsync(t => t.ProviderProfileId == providerProfileId);

        Assert.NotNull(updatedAggregate);
        Assert.Equal(2, updatedAggregate!.TotalReviews);
        Assert.Equal(8, updatedAggregate.SumRatings);
        Assert.Equal(4.00m, updatedAggregate.AverageRating);
        Assert.Equal(1, updatedAggregate.FiveStarCount);
        Assert.Equal(1, updatedAggregate.ThreeStarCount);
    }

    [Fact]
    public async Task ComputeBadge_ReturnsCorrectBadge()
    {
        var providerProfileId = Guid.NewGuid();
        var aggregate = ProviderTrustAggregate.Create(_tenantId, providerProfileId);

        // New provider
        Assert.Equal(TrustBadge.New, aggregate.ComputeBadge());

        // Rising (5 reviews, 4.0+ avg)
        for (int i = 0; i < 5; i++)
        {
            aggregate.ApplyReview(4);
        }
        Assert.Equal(TrustBadge.Rising, aggregate.ComputeBadge());

        // Trusted (20 reviews, 4.3+ avg)
        for (int i = 0; i < 15; i++)
        {
            aggregate.ApplyReview(5);
        }
        var badge = aggregate.ComputeBadge();
        Assert.True(badge == TrustBadge.Trusted || badge == TrustBadge.TopRated);

        // TopRated (50 reviews, 4.6+ avg)
        for (int i = 0; i < 30; i++)
        {
            aggregate.ApplyReview(5);
        }
        Assert.Equal(TrustBadge.TopRated, aggregate.ComputeBadge());
    }

    [Fact]
    public async Task GetTrustAggregate_ReturnsCorrectData()
    {
        var providerId = Guid.NewGuid();

        var profile = await _profileService.CreateProfileAsync(
            providerId,
            new CreateMarketplaceProfileDto("Provider", "Bio")
        );

        await SeedCompletedReviewAsync(profile, 5);
        await SeedCompletedReviewAsync(profile, 4);
        await SeedCompletedReviewAsync(profile, 5);

        var retrieved = await _trustService.GetTrustAggregateAsync(profile.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(3, retrieved!.TotalReviews);
        Assert.Equal(4.67m, retrieved.AverageRating);
    }

    [Fact]
    public async Task GetTopRatedProviders_ReturnsOrderedByRating()
    {
        // Create multiple providers with different ratings
        var provider1 = await _profileService.CreateProfileAsync(Guid.NewGuid(), new CreateMarketplaceProfileDto("Provider 1", "Bio"));
        var provider2 = await _profileService.CreateProfileAsync(Guid.NewGuid(), new CreateMarketplaceProfileDto("Provider 2", "Bio"));
        var provider3 = await _profileService.CreateProfileAsync(Guid.NewGuid(), new CreateMarketplaceProfileDto("Provider 3", "Bio"));

        for (int i = 0; i < 10; i++) await SeedCompletedReviewAsync(provider1, 4);
        for (int i = 0; i < 10; i++) await SeedCompletedReviewAsync(provider2, 5);
        for (int i = 0; i < 10; i++) await SeedCompletedReviewAsync(provider3, 3);

        var topProviders = await _trustService.GetTopRatedProvidersAsync(10);
        var list = topProviders.ToList();

        Assert.Equal(3, list.Count);
        Assert.Equal(provider2.Id, list[0].ProviderProfileId); // 5.0 avg
        Assert.Equal(provider1.Id, list[1].ProviderProfileId); // 4.0 avg
        Assert.Equal(provider3.Id, list[2].ProviderProfileId); // 3.0 avg
    }

    [Fact]
    public async Task ProviderTrustConsumer_PublishesProviderTrustUpdatedEvent()
    {
        var providerProfile = await SeedMarketplaceProfileAsync();
        var providerProfileId = providerProfile.Id;
        var authority = await SeedCompletedReviewAsync(providerProfile, 5);

        var consumer = new ProviderTrustConsumer(_context, _publishEndpointMock.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<ProviderTrustConsumer>>());

        var @event = new ServiceReviewCreatedEvent
        {
            ReviewId = authority.Review.Id,
            TenantId = _tenantId,
            ServiceRequestId = authority.Request.Id,
            ServiceId = authority.Request.ServiceId,
            ProviderUserId = authority.Request.ProviderUserId,
            ProviderProfileId = providerProfileId,
            ReviewerUserId = authority.Request.BuyerUserId,
            Rating = 5,
            CreatedAt = DateTime.UtcNow
        };

        await consumer.Consume(Mock.Of<ConsumeContext<ServiceReviewCreatedEvent>>(c => c.Message == @event));

        _publishEndpointMock.Verify(
            x => x.PublishAsync(
                It.Is<ProviderTrustUpdatedEvent>(e =>
                    e.ProviderProfileId == providerProfileId &&
                    e.TotalReviews == 1 &&
                    e.AverageRating == 5.00m &&
                    e.TrustBadge == TrustBadge.New
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    public void Dispose()
    {
        // Clear mock invocations to ensure clean state for next test
        // Using Invocations.Clear() instead of Reset() to preserve setups and event handlers
        _publishEndpointMock.Invocations.Clear();
        
        // Fixture handles disposal of shared resources
    }

    private async Task<MarketplaceProfile> SeedMarketplaceProfileAsync()
    {
        var profile = MarketplaceProfile.Create(_tenantId, Guid.NewGuid(), "Trust Provider", "Bio");
        _context.Set<MarketplaceProfile>().Add(profile);
        await _context.SaveChangesAsync();
        return profile;
    }

    private async Task<(ServiceRequest Request, ServiceReview Review)> SeedCompletedReviewAsync(
        MarketplaceProfile profile,
        int rating)
    {
        var service = Service.Create(_tenantId, profile.Id, "Trust Service", "Trust service description");
        var request = ServiceRequest.Create(
            _tenantId,
            service.Id,
            Guid.NewGuid(),
            profile.UserId,
            profile.Id,
            service.Title,
            service.Category,
            service.StartingPrice,
            service.PricingType,
            service.Currency,
            service.Packages);
        request.Accept();
        request.MarkCompletedByProvider();
        request.ConfirmCompletedByBuyer();

        var review = ServiceReview.Create(
            _tenantId,
            request.Id,
            service.Id,
            request.BuyerUserId,
            request.ProviderUserId,
            rating);

        _context.Set<Service>().Add(service);
        _context.Set<ServiceRequest>().Add(request);
        _context.Set<ServiceReview>().Add(review);
        await _context.SaveChangesAsync();
        return (request, review);
    }
}
