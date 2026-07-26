using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceEventOutboxProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPublishEndpoint> _publishEndpoint = new();

    public MarketplaceEventOutboxProcessorTests()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped(_ => _publishEndpoint.Object);

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _publishEndpoint.SetReturnsDefault(Task.CompletedTask);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PublishesMessage_AndClearsClaim()
    {
        var evt = CreateEvent();
        var message = MarketplaceEventOutboxMessage.Create(evt, JsonSerializer.Serialize(evt));

        _dbContext.Set<MarketplaceEventOutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor();

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        await _dbContext.Entry(message).ReloadAsync();
        Assert.NotNull(message.PublishedAtUtc);
        Assert.Null(message.ClaimedUntilUtc);
        Assert.Equal(0, message.AttemptCount);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenMessageIsAlreadyClaimed_SkipsPublish()
    {
        var evt = CreateEvent();
        var message = MarketplaceEventOutboxMessage.Create(evt, JsonSerializer.Serialize(evt));
        Assert.True(message.TryClaim(claimDurationSeconds: 30));

        _dbContext.Set<MarketplaceEventOutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor();

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _publishEndpoint.Verify(endpoint => endpoint.Publish(
            It.IsAny<object>(),
            It.IsAny<Type>(),
            It.IsAny<IPipe<PublishContext>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenPublishFails_RecordsFailure_AndReleasesClaim()
    {
        var evt = CreateEvent();
        var message = MarketplaceEventOutboxMessage.Create(evt, JsonSerializer.Serialize(evt));

        _dbContext.Set<MarketplaceEventOutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync();

        _publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));

        var processor = CreateProcessor();

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        await _dbContext.Entry(message).ReloadAsync();
        Assert.Null(message.PublishedAtUtc);
        Assert.Null(message.ClaimedUntilUtc);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.NextRetryAtUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenEarlierEntityEventIsFailed_BlocksNewerEvent()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow.AddMinutes(-5);
        var earlierEvent = CreateEvent(tenantId, entityId, version: 1, occurredAtUtc: baseTime);
        var newerEvent = CreateEvent(tenantId, entityId, version: 2, occurredAtUtc: baseTime.AddSeconds(1));
        var earlierMessage = MarketplaceEventOutboxMessage.Create(earlierEvent, JsonSerializer.Serialize(earlierEvent));
        var newerMessage = MarketplaceEventOutboxMessage.Create(newerEvent, JsonSerializer.Serialize(newerEvent));
        earlierMessage.RecordFailure("terminal publish failure", maxRetries: 1);

        _dbContext.Set<MarketplaceEventOutboxMessage>().AddRange(earlierMessage, newerMessage);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor();

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _publishEndpoint.Verify(endpoint => endpoint.Publish(
            It.IsAny<object>(),
            It.IsAny<Type>(),
            It.IsAny<IPipe<PublishContext>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await _dbContext.Entry(newerMessage).ReloadAsync();
        Assert.Null(newerMessage.PublishedAtUtc);
        Assert.Null(newerMessage.ClaimedUntilUtc);
    }

    [Fact]
    public async Task RetryFailedMessageAsync_ReleasesTerminalDeadLetterForBoundedReplay()
    {
        var evt = CreateEvent();
        var message = MarketplaceEventOutboxMessage.Create(evt, JsonSerializer.Serialize(evt));

        _dbContext.Set<MarketplaceEventOutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync();

        _publishEndpoint
            .SetupSequence(endpoint => endpoint.Publish(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("terminal publish failure"))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(new MarketplaceEventBackboneOptions
        {
            ProjectionMaxRetryAttempts = 1
        });

        var failedAttemptCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, failedAttemptCount);
        await _dbContext.Entry(message).ReloadAsync();
        Assert.True(message.IsFailed);
        Assert.Equal(1, message.AttemptCount);
        Assert.Null(message.ClaimedUntilUtc);

        var reset = await processor.RetryFailedMessageAsync(message.Id);

        Assert.True(reset);
        await _dbContext.Entry(message).ReloadAsync();
        Assert.False(message.IsFailed);
        Assert.Equal(0, message.AttemptCount);
        Assert.Null(message.NextRetryAtUtc);
        Assert.Null(message.ClaimedUntilUtc);

        var replayedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, replayedCount);
        await _dbContext.Entry(message).ReloadAsync();
        Assert.NotNull(message.PublishedAtUtc);
        Assert.False(message.IsFailed);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private MarketplaceEventOutboxProcessor CreateProcessor()
    {
        return CreateProcessor(new MarketplaceEventBackboneOptions());
    }

    private MarketplaceEventOutboxProcessor CreateProcessor(MarketplaceEventBackboneOptions options)
    {
        return new MarketplaceEventOutboxProcessor(
            _serviceProvider,
            NullLogger<MarketplaceEventOutboxProcessor>.Instance,
            Options.Create(options));
    }

    private static OrderCreatedEvent CreateEvent(
        Guid? tenantId = null,
        Guid? entityId = null,
        int version = 1,
        DateTime? occurredAtUtc = null)
    {
        return new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            EntityId = entityId ?? Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow.AddSeconds(-5),
            Version = version,
            OrderNumber = "ORD-OUTBOX-1",
            TotalAmount = 50m,
            SubtotalAmount = 45m,
            Currency = "USD",
            IsPaid = false,
            Items =
            [
                new MarketplaceOrderItem
                {
                    ProductId = Guid.NewGuid(),
                    Sku = "SKU-1",
                    Quantity = 1,
                    UnitPrice = 45m
                }
            ]
        };
    }
}
