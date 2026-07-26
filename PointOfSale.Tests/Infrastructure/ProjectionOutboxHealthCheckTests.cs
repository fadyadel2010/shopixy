using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.HealthChecks;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProjectionOutboxHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenProjectionOutboxesHaveNoBlockedMessages_ReturnsHealthy()
    {
        await using var context = CreateContext();
        var healthCheck = new ProjectionOutboxHealthCheck(
            context,
            NullLogger<ProjectionOutboxHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenStockProjectionMessageRepeatedlyFails_ReturnsUnhealthy()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var stockEvent = new ProductVariantStockChangedEvent
        {
            TenantId = tenantId,
            CorrelationId = "projection-outbox-health-test",
            OccurredAtUtc = new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc),
            VariantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Reason = "Test",
            OldQuantity = 5m,
            NewQuantity = 3m,
            TotalStockQuantity = 3m,
            StockUpdatedAtUtc = new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc)
        };
        var message = StockSyncOutboxMessage.Create(stockEvent, JsonSerializer.Serialize(stockEvent));
        message.RecordNonTerminalProjectionFailure("Projection publish failed.");
        message.RecordNonTerminalProjectionFailure("Projection publish failed.");
        message.RecordNonTerminalProjectionFailure("Projection publish failed.");
        context.StockSyncOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var healthCheck = new ProjectionOutboxHealthCheck(
            context,
            NullLogger<ProjectionOutboxHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.Data["StockRepeatedFailureCount"]);
        Assert.Equal(1, result.Data["RepeatedFailureCount"]);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
