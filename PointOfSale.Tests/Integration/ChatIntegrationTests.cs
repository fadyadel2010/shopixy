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

public class ChatIntegrationTests : IDisposable
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly ApplicationDbContext _context;
    private readonly IMarketplaceProfileService _profileService;
    private readonly IServiceCatalogService _catalogService;
    private readonly IServiceRequestService _requestService;
    private readonly IChatService _chatService;
    private readonly Guid _tenantId;
    private readonly TestTenantContext _tenantContext;

    public ChatIntegrationTests()
    {
        _databaseScope = TestDatabaseScope.Open();
        _tenantId = Guid.NewGuid();
        _tenantContext = new TestTenantContext(_tenantId);
        _context = _databaseScope.CreateDbContext(_tenantContext);
        _context.Tenants.Add(new Tenant { Id = _tenantId, Name = "Chat Test Tenant", IsActive = true, Status = TenantStatus.Active });
        _context.SaveChanges();
        var publishEndpointMock = new Mock<IEventPublisher>();

        _profileService = new MarketplaceProfileService(_context, _tenantContext);
        _catalogService = new ServiceCatalogService(_context, _tenantContext, publishEndpointMock.Object, NullLogger<ServiceCatalogService>.Instance);
        _chatService = new ChatService(_context, _tenantContext);
        _requestService = new ServiceRequestService(_context, _tenantContext, _chatService, publishEndpointMock.Object, NullLogger<ServiceRequestService>.Instance);
    }

    [Fact]
    public async Task SendMessage_WithoutServiceRequest_ThrowsException()
    {
        var userId = Guid.NewGuid();
        var fakeRequestId = Guid.NewGuid();

        await Assert.ThrowsAsync<MarketplaceDomainException>(async () =>
        {
            await _chatService.SendMessageAsync(fakeRequestId, userId, _tenantId, "Hello");
        });
    }

    [Fact]
    public async Task SendMessage_WhenStatusIsPending_ThrowsException()
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

        // Request is Pending - chat not allowed
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Hello");
        });
    }

    [Fact]
    public async Task SendMessage_AfterAccepted_Success()
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

        // Now chat is allowed
        var message = await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Hello provider!");

        Assert.NotNull(message);
        Assert.Equal(buyerId, message.SenderUserId);
        Assert.Equal(SenderRole.Buyer, message.SenderRole);
        Assert.Equal(MessageType.Text, message.MessageType);
        Assert.Equal("Hello provider!", message.Body);
    }

    [Fact]
    public async Task SendMessage_ByNonParticipant_ThrowsException()
    {
        var providerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var randomUserId = Guid.NewGuid();

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

        // Random user cannot send message
        await Assert.ThrowsAsync<MarketplaceDomainException>(async () =>
        {
            await _chatService.SendMessageAsync(request.Id, randomUserId, _tenantId, "I'm not invited!");
        });
    }

    [Fact]
    public async Task SendMessage_BuyerAndProvider_BothCanSend()
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

        // Buyer sends
        var buyerMessage = await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Hello provider!");
        Assert.Equal(SenderRole.Buyer, buyerMessage.SenderRole);

        // Provider sends
        var providerMessage = await _chatService.SendMessageAsync(request.Id, providerId, _tenantId, "Hello buyer!");
        Assert.Equal(SenderRole.Provider, providerMessage.SenderRole);

        // Get all messages
        var messages = await _chatService.GetMessagesAsync(request.Id, buyerId, _tenantId);
        var messageList = messages.ToList();

        // Should have 3 messages: system message + 2 user messages
        Assert.Equal(3, messageList.Count);
        Assert.Equal(MessageType.System, messageList[0].MessageType);
        Assert.Equal(MessageType.Text, messageList[1].MessageType);
        Assert.Equal(MessageType.Text, messageList[2].MessageType);
    }

    [Fact]
    public async Task SystemMessages_CreatedOnStatusChanges()
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

        // Accept - creates system message
        await _requestService.AcceptRequestAsync(request.Id, providerId);

        var messages1 = await _chatService.GetMessagesAsync(request.Id, buyerId, _tenantId);
        Assert.Single(messages1);
        Assert.Equal(MessageType.System, messages1.First().MessageType);
        Assert.Contains("accepted", messages1.First().Body.ToLower());

        // Mark completed by provider - creates system message
        await _requestService.MarkCompletedByProviderAsync(request.Id, providerId);

        var messages2 = await _chatService.GetMessagesAsync(request.Id, buyerId, _tenantId);
        Assert.Equal(2, messages2.Count());
        Assert.Contains("completed", messages2.Last().Body.ToLower());

        // Confirm by buyer - creates system message
        await _requestService.ConfirmCompletedByBuyerAsync(request.Id, buyerId);

        var messages3 = await _chatService.GetMessagesAsync(request.Id, buyerId, _tenantId);
        Assert.Equal(3, messages3.Count());
        Assert.Contains("confirmed", messages3.Last().Body.ToLower());
    }

    [Fact]
    public async Task CanUserParticipate_ValidatesCorrectly()
    {
        var providerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var randomUserId = Guid.NewGuid();

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

        // Pending - no one can participate
        Assert.False(await _chatService.CanUserParticipateAsync(request.Id, buyerId, _tenantId));
        Assert.False(await _chatService.CanUserParticipateAsync(request.Id, providerId, _tenantId));
        Assert.False(await _chatService.CanUserParticipateAsync(request.Id, randomUserId, _tenantId));

        // Accept
        await _requestService.AcceptRequestAsync(request.Id, providerId);

        // Accepted - participants can chat
        Assert.True(await _chatService.CanUserParticipateAsync(request.Id, buyerId, _tenantId));
        Assert.True(await _chatService.CanUserParticipateAsync(request.Id, providerId, _tenantId));
        Assert.False(await _chatService.CanUserParticipateAsync(request.Id, randomUserId, _tenantId));
    }

    [Fact]
    public async Task GetMessages_OrderedByCreatedAt()
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

        // Send multiple messages
        await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Message 1");
        await Task.Delay(10); // Ensure different timestamps
        await _chatService.SendMessageAsync(request.Id, providerId, _tenantId, "Message 2");
        await Task.Delay(10);
        await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Message 3");

        var messages = await _chatService.GetMessagesAsync(request.Id, buyerId, _tenantId);
        var messageList = messages.ToList();

        // Should be ordered by CreatedAt
        for (int i = 1; i < messageList.Count; i++)
        {
            Assert.True(messageList[i].CreatedAt >= messageList[i - 1].CreatedAt);
        }
    }

    [Fact]
    public async Task SendMessage_AfterCancelled_ThrowsException()
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

        // Can send while accepted
        await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Hello");

        // Cancel
        await _requestService.CancelRequestAsync(request.Id, buyerId, "Changed my mind");

        // Cannot send after cancelled
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Wait!");
        });
    }

    [Fact]
    public async Task SendMessage_AfterBuyerConfirmedCompletion_ThrowsButParticipantsCanViewHistory()
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
        await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Before completion");
        await _requestService.MarkCompletedByProviderAsync(request.Id, providerId);
        await _requestService.ConfirmCompletedByBuyerAsync(request.Id, buyerId);

        Assert.False(await _chatService.CanUserParticipateAsync(request.Id, buyerId, _tenantId));
        Assert.True(await _chatService.CanUserViewConversationAsync(request.Id, buyerId, _tenantId));
        Assert.True(await _chatService.CanUserViewConversationAsync(request.Id, providerId, _tenantId));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "After closure");
        });

        var messages = await _chatService.GetMessagesAsync(request.Id, buyerId, _tenantId);
        Assert.Contains(messages, message => message.Body == "Before completion");
    }

    [Fact]
    public async Task GetMessages_ByNonParticipant_ThrowsUnauthorized()
    {
        var providerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();

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

        await Assert.ThrowsAsync<MarketplaceDomainException>(async () =>
        {
            await _chatService.GetMessagesAsync(request.Id, attackerId, _tenantId);
        });
    }

    [Fact]
    public async Task SendMessage_RateLimit_EnforcedPerServiceRequest()
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

        // Send 60 messages (the limit)
        for (int i = 0; i < 60; i++)
        {
            await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, $"Message {i}");
        }

        // 61st message should fail
        await Assert.ThrowsAsync<RateLimitExceededException>(async () =>
        {
            await _chatService.SendMessageAsync(request.Id, buyerId, _tenantId, "Too many messages");
        });
    }

    public void Dispose()
    {
        _context.Dispose();
        _databaseScope.Dispose();
    }
}
