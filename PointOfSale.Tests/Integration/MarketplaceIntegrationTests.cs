using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;
using Xunit;


namespace PointOfSale.Tests.Integration;

public class MarketplaceIntegrationTests : IDisposable
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly ApplicationDbContext _context;
    private readonly IMarketplaceProfileService _profileService;
    private readonly IServiceCatalogService _catalogService;
    private readonly IServiceRequestService _requestService;
    private readonly IServiceReviewService _reviewService;
    private readonly Guid _tenantId;
    private readonly TestTenantContext _tenantContext;

    public MarketplaceIntegrationTests()
    {
        _databaseScope = TestDatabaseScope.Open();
        _tenantId = Guid.NewGuid();
        _tenantContext = new TestTenantContext(_tenantId);
        _context = _databaseScope.CreateDbContext(_tenantContext);
        _context.Tenants.Add(new Tenant { Id = _tenantId, Name = "Marketplace Test Tenant", IsActive = true, Status = TenantStatus.Active });
        _context.SaveChanges();
        var eventPublisherMock = new Mock<IEventPublisher>();

        _profileService = new MarketplaceProfileService(_context, _tenantContext);
        _catalogService = new ServiceCatalogService(_context, _tenantContext, eventPublisherMock.Object, NullLogger<ServiceCatalogService>.Instance);
        var chatService = new ChatService(_context, _tenantContext);
        _requestService = new ServiceRequestService(_context, _tenantContext, chatService, eventPublisherMock.Object, NullLogger<ServiceRequestService>.Instance);
        _reviewService = new ServiceReviewService(_context, _tenantContext, eventPublisherMock.Object, NullLogger<ServiceReviewService>.Instance);
    }

    [Fact]
    public async Task FullWorkflow_CreateProfileServiceRequestAndReview_Success()
    {
        var providerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        var profile = await _profileService.CreateProfileAsync(
            providerId,
            new CreateMarketplaceProfileDto("Test Provider", "Bio")
        );

        Assert.NotNull(profile);
        Assert.Equal(providerId, profile.UserId);

        var service = await _catalogService.CreateServiceAsync(
            profile.Id,
            providerId,
            new CreateServiceDto("Test Service", "Description", "Category")
        );

        Assert.NotNull(service);

        var request = await _requestService.RequestServiceAsync(
            buyerId,
            new RequestServiceDto(service.Id, "Please help")
        );

        Assert.Equal(ServiceRequestStatus.Pending, request.Status);

        await _requestService.AcceptRequestAsync(request.Id, providerId);
        var acceptedRequest = await _requestService.GetRequestByIdAsync(request.Id);
        Assert.Equal(ServiceRequestStatus.Accepted, acceptedRequest!.Status);

        await _requestService.MarkCompletedByProviderAsync(request.Id, providerId);
        var completedByProvider = await _requestService.GetRequestByIdAsync(request.Id);
        Assert.Equal(ServiceRequestStatus.CompletedByProvider, completedByProvider!.Status);

        await _requestService.ConfirmCompletedByBuyerAsync(request.Id, buyerId);
        var completedByBuyer = await _requestService.GetRequestByIdAsync(request.Id);
        Assert.Equal(ServiceRequestStatus.CompletedByBuyer, completedByBuyer!.Status);

        var review = await _reviewService.CreateReviewAsync(
            buyerId,
            new CreateReviewDto(request.Id, 5, "Great service!")
        );

        Assert.NotNull(review);
        Assert.Equal(5, review.Rating);
    }

    [Fact]
    public async Task CreateReview_WithoutCompletedRequest_ThrowsException()
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

        await Assert.ThrowsAsync<MarketplaceDomainException>(async () =>
        {
            await _reviewService.CreateReviewAsync(
                buyerId,
                new CreateReviewDto(request.Id, 5)
            );
        });
    }

    [Fact]
    public async Task CreateReview_Twice_ThrowsException()
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
            new CreateReviewDto(request.Id, 5)
        );

        await Assert.ThrowsAsync<MarketplaceDomainException>(async () =>
        {
            await _reviewService.CreateReviewAsync(
                buyerId,
                new CreateReviewDto(request.Id, 4)
            );
        });
    }

    [Fact]
    public async Task CreateProfile_Twice_ThrowsException()
    {
        var userId = Guid.NewGuid();

        await _profileService.CreateProfileAsync(
            userId,
            new CreateMarketplaceProfileDto("Provider", "Bio")
        );

        await Assert.ThrowsAsync<MarketplaceDomainException>(async () =>
        {
            await _profileService.CreateProfileAsync(
                userId,
                new CreateMarketplaceProfileDto("Provider 2", "Bio 2")
            );
        });
    }

    [Fact]
    public async Task CreateService_ByNonOwner_ThrowsException()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var profile = await _profileService.CreateProfileAsync(
            ownerId,
            new CreateMarketplaceProfileDto("Provider", "Bio")
        );

        await Assert.ThrowsAsync<MarketplaceDomainException>(async () =>
        {
            await _catalogService.CreateServiceAsync(
                profile.Id,
                otherId,
                new CreateServiceDto("Service", "Desc")
            );
        });
    }

    public void Dispose()
    {
        _context.Dispose();
        _databaseScope.Dispose();
    }
}
