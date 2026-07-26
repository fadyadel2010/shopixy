using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.HealthChecks;
using PointOfSale.Infrastructure.Payment;

namespace PointOfSale.Tests.Infrastructure;

public sealed class PaymentCancellationOutboxReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenBacklogIsEmpty_ReturnsHealthy()
    {
        await using var context = CreateContext();
        var healthCheck = CreateHealthCheck(context);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0, result.Data["PendingCount"]);
        Assert.Equal(120, result.Data["ProcessingLeaseSeconds"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPendingMessageAgesPastWarningThreshold_ReturnsDegraded()
    {
        await using var context = CreateContext();
        var message = new PaymentCancellationOutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            Reason = "Lagging cancellation",
            Actor = "operator"
        };
        context.PaymentCancellationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        message.CreatedAt = DateTime.UtcNow.AddMinutes(-6);
        await context.SaveChangesAsync();

        var healthCheck = CreateHealthCheck(context);
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(1, result.Data["PendingCount"]);
        Assert.Equal(1, result.Data["WarningLagCount"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTerminalFailuresExist_ReturnsUnhealthy()
    {
        await using var context = CreateContext();
        context.PaymentCancellationOutboxMessages.Add(new PaymentCancellationOutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            Reason = "Terminal failure",
            Actor = "operator",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            AttemptCount = 10,
            LastError = "Provider timeout",
            IsFailed = true
        });
        await context.SaveChangesAsync();

        var healthCheck = CreateHealthCheck(context);
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.Data["TerminalFailureCount"]);
    }

    private static PaymentCancellationOutboxReadinessHealthCheck CreateHealthCheck(ApplicationDbContext context)
    {
        return new PaymentCancellationOutboxReadinessHealthCheck(
            context,
            Options.Create(new PaymentCancellationOutboxOptions { ProcessingLeaseSeconds = 120 }),
            NullLogger<PaymentCancellationOutboxReadinessHealthCheck>.Instance);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
