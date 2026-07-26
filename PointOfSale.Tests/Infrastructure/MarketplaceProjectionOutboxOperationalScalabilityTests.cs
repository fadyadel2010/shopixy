using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceProjectionOutboxOperationalScalabilityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly RecordingMarketplaceEventPipeline _pipeline = new();

    public MarketplaceProjectionOutboxOperationalScalabilityTests()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IMarketplaceEventPipeline>(_ => _pipeline);

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ApplicationDbContext>();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_BoundsProjectionWorkToBatchSize_AndLeavesResidualQueueStable()
    {
        var tenantId = Guid.NewGuid();
        var events = Enumerable.Range(1, 3)
            .Select(index => CreateOrderCreatedEvent(tenantId, Guid.NewGuid(), index))
            .ToArray();

        _dbContext.Set<MarketplaceProjectionOutboxMessage>().AddRange(
            events.Select(@event => MarketplaceProjectionOutboxMessage.Create(
                @event,
                "scalability-test-consumer")));
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor();

        var processed = await processor.ProcessPendingMessagesAsync(batchSize: 2);

        Assert.Equal(2, processed);
        Assert.Equal(2, _pipeline.OrderCreatedCount);
        Assert.Equal(2, await _dbContext.Set<ProcessedEvent>().CountAsync());
        Assert.Equal(1, await _dbContext.Set<MarketplaceProjectionOutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenCompletionAlreadyExists_CleansResidualOutboxWithoutRedispatch()
    {
        var tenantId = Guid.NewGuid();
        var @event = CreateOrderCreatedEvent(tenantId, Guid.NewGuid(), version: 1);
        var message = MarketplaceProjectionOutboxMessage.Create(@event, "duplicate-completion-consumer");

        _dbContext.Set<MarketplaceProjectionOutboxMessage>().Add(message);
        _dbContext.Set<ProcessedEvent>().Add(
            ProcessedEvent.Create(
                message.EventId,
                message.ConsumerName,
                message.EventType,
                message.EntityId,
                message.TenantId,
                message.CorrelationId));
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor();

        var processed = await processor.ProcessPendingMessagesAsync(batchSize: 10);

        Assert.Equal(0, processed);
        Assert.Equal(0, _pipeline.OrderCreatedCount);
        Assert.Empty(await _dbContext.Set<MarketplaceProjectionOutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task MarketplaceProjectionOutbox_ConcurrentClaimsCollapseThroughRowVersion()
    {
        var tenantId = Guid.NewGuid();
        var @event = CreateOrderCreatedEvent(tenantId, Guid.NewGuid(), version: 1);
        var message = MarketplaceProjectionOutboxMessage.Create(@event, "concurrent-claim-consumer");

        _dbContext.Set<MarketplaceProjectionOutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync();

        using var firstScope = _serviceProvider.CreateScope();
        using var secondScope = _serviceProvider.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstClaim = await firstDb.Set<MarketplaceProjectionOutboxMessage>().SingleAsync();
        var secondClaim = await secondDb.Set<MarketplaceProjectionOutboxMessage>().SingleAsync();

        Assert.True(firstClaim.TryClaim(30));
        Assert.True(secondClaim.TryClaim(30));

        await firstDb.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Set<MarketplaceProjectionOutboxMessage>().SingleAsync();
        Assert.NotEqual(0u, reloaded.RowVersion);
        Assert.NotNull(reloaded.ClaimedUntilUtc);
        Assert.Null(reloaded.ProcessedAtUtc);
    }

    [Fact]
    public void MarketplaceOutboxWorkers_MustUseOperationalThroughputOptions()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var eventWorker = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Eventing", "MarketplaceEventOutboxWorker.cs"));
        var projectionWorker = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Eventing", "MarketplaceProjectionOutboxProcessor.cs"));
        var stockWorker = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Eventing", "StockSyncOutboxWorker.cs"));
        var eventProcessor = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Eventing", "MarketplaceEventOutboxProcessor.cs"));
        var projectionProcessor = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Eventing", "MarketplaceProjectionOutboxProcessor.cs"));
        var stockProcessor = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Eventing", "StockSyncOutboxProcessor.cs"));

        Assert.Contains("_options.EventOutboxBatchSize", eventWorker, StringComparison.Ordinal);
        Assert.Contains("_options.ProjectionOutboxBatchSize", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("_options.BatchSize", stockWorker, StringComparison.Ordinal);
        Assert.Contains("_options.MaxBatchesPerPoll", eventWorker, StringComparison.Ordinal);
        Assert.Contains("_options.MaxBatchesPerPoll", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("_options.MaxBatchesPerPoll", stockWorker, StringComparison.Ordinal);
        Assert.Contains("_options.InterBatchDelayMilliseconds", eventWorker, StringComparison.Ordinal);
        Assert.Contains("_options.OutboxPollIntervalSeconds", eventWorker, StringComparison.Ordinal);
        Assert.Contains("_options.OutboxPollIntervalSeconds", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("_options.PollIntervalSeconds", stockWorker, StringComparison.Ordinal);
        Assert.Contains("_options.ClaimLeaseSeconds", eventProcessor, StringComparison.Ordinal);
        Assert.Contains("_options.ClaimLeaseSeconds", projectionProcessor, StringComparison.Ordinal);
        Assert.Contains("_options.ClaimLeaseSeconds", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.ActiveDeliveryOverlap", projectionProcessor, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private MarketplaceProjectionOutboxProcessor CreateProcessor()
    {
        return new MarketplaceProjectionOutboxProcessor(
            _serviceProvider,
            NullLogger<MarketplaceProjectionOutboxProcessor>.Instance,
            Options.Create(new MarketplaceEventBackboneOptions
            {
                ClaimLeaseSeconds = 15,
                ProjectionMaxRetryAttempts = 3
            }));
    }

    private static MarketplaceEvents.OrderCreatedEvent CreateOrderCreatedEvent(
        Guid tenantId,
        Guid orderId,
        int version)
    {
        return new MarketplaceEvents.OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = tenantId,
            EntityId = orderId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-version),
            Version = version,
            EventVersion = version,
            OrderNumber = $"ORD-SCALE-{version}",
            TotalAmount = 25m,
            SubtotalAmount = 25m,
            Currency = "EGP",
            IsPaid = false,
            Items =
            [
                new MarketplaceEvents.MarketplaceOrderItem
                {
                    ProductId = Guid.NewGuid(),
                    Sku = $"SKU-SCALE-{version}",
                    Quantity = 1,
                    UnitPrice = 25m
                }
            ]
        };
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }

    private sealed class RecordingMarketplaceEventPipeline : IMarketplaceEventPipeline
    {
        public int OrderCreatedCount { get; private set; }

        public Task ProcessAsync(MarketplaceEvents.OrderCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            OrderCreatedCount++;
            return Task.CompletedTask;
        }

        public Task ProcessAsync(MarketplaceEvents.PaymentCompletedEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessAsync(MarketplaceEvents.OrderDeliveredEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessAsync(MarketplaceEvents.RefundProcessedEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessAsync(MarketplaceEvents.ProductUpdatedEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
