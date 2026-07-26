using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MassTransit;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Tests.Infrastructure;

[Collection("TelemetryIsolation")]
public sealed class StockSyncOutboxProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPublishEndpoint> _publishEndpoint = new();
    private readonly CommerceReliabilityTelemetry _telemetry = new();

    public StockSyncOutboxProcessorTests()
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
    public async Task ProcessPendingMessagesAsync_PublishesMessage_AndMarksItPublished()
    {
        var evt = CreateEvent();
        var message = StockSyncOutboxMessage.Create(
            evt,
            payloadJson: CreatePayloadJson(evt));

        _dbContext.StockSyncOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor();

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);

        await _dbContext.Entry(message).ReloadAsync();
        Assert.NotNull(message.PublishedAtUtc);
        Assert.Null(message.LastError);
        Assert.Null(message.NextRetryAtUtc);
        Assert.Equal(0, message.AttemptCount);
        Assert.False(message.IsFailed);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenPayloadCannotBeResolved_RecordsNonTerminalFailure_AndMetrics()
    {
        var evt = CreateEvent();
        var message = StockSyncOutboxMessage.Create(
            evt,
            payloadJson: CreatePayloadJson(evt));

        SetMessageProperty(message, nameof(StockSyncOutboxMessage.ClrType), "Missing.Namespace.MissingType, MissingAssembly");

        _dbContext.StockSyncOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        long retryCount = 0;
        long failureCount = 0;
        using var retryListener = CreateCounterListener(
            "stock_sync_retry_total",
            measurement => Interlocked.Add(ref retryCount, measurement));
        using var failureListener = CreateCounterListener(
            "stock_sync_failure_total",
            measurement => Interlocked.Add(ref failureCount, measurement));

        var processor = CreateProcessor();

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);

        await _dbContext.Entry(message).ReloadAsync();
        Assert.Null(message.PublishedAtUtc);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.NextRetryAtUtc);
        Assert.False(message.IsFailed);
        Assert.Contains("Unable to resolve", message.LastError, StringComparison.Ordinal);
        Assert.Equal(1, retryCount);
        Assert.Equal(1, failureCount);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenEarlierVariantEventIsFailed_BlocksNewerEvent()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow.AddMinutes(-5);
        var earlierEvent = CreateEvent(
            tenantId,
            productId,
            variantId,
            warehouseId: warehouseId,
            stockUpdatedAtUtc: baseTime,
            occurredAtUtc: baseTime.AddSeconds(1));
        var newerEvent = CreateEvent(
            tenantId,
            productId,
            variantId,
            warehouseId: warehouseId,
            stockUpdatedAtUtc: baseTime.AddSeconds(30),
            occurredAtUtc: baseTime.AddSeconds(31));
        var earlierMessage = StockSyncOutboxMessage.Create(earlierEvent, CreatePayloadJson(earlierEvent));
        var newerMessage = StockSyncOutboxMessage.Create(newerEvent, CreatePayloadJson(newerEvent));
        earlierMessage.RecordFailure("terminal stock sync failure", maxRetries: 1);

        _dbContext.StockSyncOutboxMessages.AddRange(earlierMessage, newerMessage);
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
    public async Task ProcessPendingMessagesAsync_WhenEarlierEventIsDifferentWarehouse_PublishesNewerEvent()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var earlierWarehouseId = Guid.NewGuid();
        var newerWarehouseId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow.AddMinutes(-5);
        var earlierEvent = CreateEvent(
            tenantId,
            productId,
            variantId,
            warehouseId: earlierWarehouseId,
            stockUpdatedAtUtc: baseTime,
            occurredAtUtc: baseTime.AddSeconds(1));
        var newerEvent = CreateEvent(
            tenantId,
            productId,
            variantId,
            warehouseId: newerWarehouseId,
            stockUpdatedAtUtc: baseTime.AddSeconds(30),
            occurredAtUtc: baseTime.AddSeconds(31));
        var earlierMessage = StockSyncOutboxMessage.Create(earlierEvent, CreatePayloadJson(earlierEvent));
        var newerMessage = StockSyncOutboxMessage.Create(newerEvent, CreatePayloadJson(newerEvent));
        earlierMessage.RecordFailure("terminal stock sync failure", maxRetries: 1);

        _dbContext.StockSyncOutboxMessages.AddRange(earlierMessage, newerMessage);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor();

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        await _dbContext.Entry(newerMessage).ReloadAsync();
        Assert.NotNull(newerMessage.PublishedAtUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WithCustomMaxRetryAttempts_RespectsConfiguredLimit()
    {
        var evt = CreateEvent();
        var message = StockSyncOutboxMessage.Create(
            evt,
            payloadJson: CreatePayloadJson(evt));

        // Force a type resolution failure to trigger retries
        SetMessageProperty(message, nameof(StockSyncOutboxMessage.ClrType), "Missing.Namespace.MissingType, MissingAssembly");

        _dbContext.StockSyncOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        // Configure custom MaxRetryAttempts (lower than default 8)
        var customOptions = new StockSyncOutboxOptions
        {
            MaxRetryAttempts = 3,
            PollIntervalSeconds = 5,
            ClaimLeaseSeconds = 60
        };

        var processor = CreateProcessor(customOptions);

        // Process multiple times to exceed custom retry limit
        // After each attempt, manually clear NextRetryAtUtc to allow immediate reprocessing
        for (int i = 0; i < customOptions.MaxRetryAttempts; i++)
        {
            await processor.ProcessPendingMessagesAsync();
            await _dbContext.Entry(message).ReloadAsync();
            
            // Clear NextRetryAtUtc to allow immediate retry (simulating time passage)
            if (message.NextRetryAtUtc.HasValue && !message.IsFailed)
            {
                SetMessageProperty(message, nameof(StockSyncOutboxMessage.NextRetryAtUtc), null);
                await _dbContext.SaveChangesAsync();
            }
        }

        // After MaxRetryAttempts, message should be marked as failed
        await _dbContext.Entry(message).ReloadAsync();
        Assert.Equal(customOptions.MaxRetryAttempts, message.AttemptCount);
        Assert.True(message.IsFailed, "Expected message to be marked as failed after exceeding custom MaxRetryAttempts");
        Assert.Null(message.NextRetryAtUtc);
        Assert.NotNull(message.LastError);
    }

    [Fact]
    public async Task HandleTerminalFailureAsync_WhenVariantIsMissing_MarksMessageCompensatedToUnblockStream()
    {
        var evt = CreateEvent();
        var message = StockSyncOutboxMessage.Create(evt, CreatePayloadJson(evt));
        _dbContext.StockSyncOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var compensation = new ProjectionFailureCompensationService(
            _dbContext,
            NullLogger<ProjectionFailureCompensationService>.Instance);

        await compensation.HandleTerminalFailureAsync(new ProjectionOutboxTerminalFailure(
            "stock-sync-outbox",
            message.Id,
            message.EventId,
            message.EventType,
            message.TenantId,
            "simulated terminal failure",
            DateTime.UtcNow,
            3));

        await _dbContext.Entry(message).ReloadAsync();

        Assert.True(message.IsFailed);
        Assert.NotNull(message.PublishedAtUtc);
        Assert.Contains("without replay", message.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await _dbContext.StockSyncOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task HandleTerminalFailureAsync_WhenWarehouseStockRowIsMissing_EmitsZeroSnapshotReplay()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var evt = CreateEvent(tenantId, productId, variantId, warehouseId);
        var message = StockSyncOutboxMessage.Create(evt, CreatePayloadJson(evt));

        _dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "COMP-001",
            Price = 25m,
            CostPrice = 10m,
            IsDefault = true,
            IsActive = true
        });
        _dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main Warehouse",
            IsActive = true
        });
        _dbContext.StockSyncOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var compensation = new ProjectionFailureCompensationService(
            _dbContext,
            NullLogger<ProjectionFailureCompensationService>.Instance);

        await compensation.HandleTerminalFailureAsync(new ProjectionOutboxTerminalFailure(
            "stock-sync-outbox",
            message.Id,
            message.EventId,
            message.EventType,
            message.TenantId,
            "simulated terminal failure",
            DateTime.UtcNow,
            3));

        await _dbContext.Entry(message).ReloadAsync();
        var replayMessage = await _dbContext.StockSyncOutboxMessages
            .Where(item => item.Id != message.Id)
            .SingleAsync();

        var replayEvent = System.Text.Json.JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(
            replayMessage.PayloadJson);

        Assert.True(message.IsFailed);
        Assert.NotNull(message.PublishedAtUtc);
        Assert.NotNull(replayEvent);
        Assert.Equal(0m, replayEvent!.NewQuantity);
        Assert.Equal(0m, replayEvent.TotalStockQuantity);
        Assert.Equal(warehouseId, replayEvent.WarehouseId);
        Assert.Equal("Main Warehouse", replayEvent.WarehouseName);
    }

    [Fact]
    public async Task HandleTerminalFailureAsync_WhenOnlyInactiveWarehouseStockRemains_EmitsZeroAuthoritativeSnapshotReplay()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var evt = CreateEvent(tenantId, productId, variantId, warehouseId);
        var message = StockSyncOutboxMessage.Create(evt, CreatePayloadJson(evt));

        _dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Compensation Product",
            CategoryId = Guid.Empty,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "COMP-002",
            Price = 25m,
            CostPrice = 10m,
            IsDefault = true,
            IsActive = true
        });
        _dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Inactive Warehouse",
            IsActive = false
        });
        _dbContext.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = 9m,
            ReservedQuantity = 1m,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.StockSyncOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var compensation = new ProjectionFailureCompensationService(
            _dbContext,
            NullLogger<ProjectionFailureCompensationService>.Instance);

        await compensation.HandleTerminalFailureAsync(new ProjectionOutboxTerminalFailure(
            "stock-sync-outbox",
            message.Id,
            message.EventId,
            message.EventType,
            message.TenantId,
            "simulated terminal failure",
            DateTime.UtcNow,
            3));

        await _dbContext.Entry(message).ReloadAsync();
        var replayMessage = await _dbContext.StockSyncOutboxMessages
            .Where(item => item.Id != message.Id)
            .SingleAsync();

        var replayEvent = System.Text.Json.JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(
            replayMessage.PayloadJson);

        Assert.True(message.IsFailed);
        Assert.NotNull(message.PublishedAtUtc);
        Assert.NotNull(replayEvent);
        Assert.Equal(0m, replayEvent!.NewQuantity);
        Assert.Equal(0m, replayEvent.TotalStockQuantity);
        Assert.Equal(warehouseId, replayEvent.WarehouseId);
        Assert.Equal("Inactive Warehouse", replayEvent.WarehouseName);
    }

    public void Dispose()
    {
        _telemetry.Dispose();
        _serviceProvider.Dispose();
    }

    private StockSyncOutboxProcessor CreateProcessor()
    {
        return CreateProcessor(new StockSyncOutboxOptions());
    }

    private StockSyncOutboxProcessor CreateProcessor(StockSyncOutboxOptions options)
    {
        return new StockSyncOutboxProcessor(
            _serviceProvider,
            _telemetry,
            NullLogger<StockSyncOutboxProcessor>.Instance,
            Options.Create(options));
    }

    private static ProductVariantStockChangedEvent CreateEvent(
        Guid? tenantId = null,
        Guid? productId = null,
        Guid? variantId = null,
        Guid? warehouseId = null,
        DateTime? stockUpdatedAtUtc = null,
        DateTime? occurredAtUtc = null)
    {
        return new ProductVariantStockChangedEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            ProductId = productId ?? Guid.NewGuid(),
            VariantId = variantId ?? Guid.NewGuid(),
            WarehouseId = warehouseId ?? Guid.NewGuid(),
            SKU = "OUTBOX-TEST-001",
            OldQuantity = 5m,
            NewQuantity = 4m,
            TotalStockQuantity = 4m,
            Reason = "Test",
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow.AddSeconds(-5),
            StockUpdatedAtUtc = stockUpdatedAtUtc ?? DateTime.UtcNow
        };
    }

    private static string CreatePayloadJson(ProductVariantStockChangedEvent evt)
    {
        return System.Text.Json.JsonSerializer.Serialize(evt, typeof(ProductVariantStockChangedEvent));
    }

    private static void SetMessageProperty(StockSyncOutboxMessage message, string propertyName, object? value)
    {
        var property = typeof(StockSyncOutboxMessage).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found on StockSyncOutboxMessage.");

        property.SetValue(message, value);
    }

    private static MeterListener CreateCounterListener(string instrumentName, Action<long> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == CommerceReliabilityTelemetry.MeterName &&
                    instrument.Name == instrumentName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            onMeasurement(measurement);
        });
        listener.Start();
        return listener;
    }
}

/// <summary>
/// Collection definition to isolate telemetry-sensitive tests from parallel execution.
/// MeterListener subscribes globally by counter name, so running multiple tests that
/// emit the same counters in parallel can cause cross-test interference.
/// </summary>
[CollectionDefinition("TelemetryIsolation", DisableParallelization = true)]
public class TelemetryIsolationCollection
{
}
