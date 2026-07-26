using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers.Admin;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;

public sealed class AbandonedCartRecoveryTelemetryTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AbandonedCartRecoveryTelemetryTests()
    {
        // Use SQLite in-memory to enforce relational behavior like query translation,
        // unique indexes, and constraint failures. InMemory provider silently ignores
        // these database semantics, which can give false confidence.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Create the database schema once during test fixture setup
        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsRealCounts()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();

        var firstCart = new AbandonedCart
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = Guid.NewGuid(),
            PhoneNumber = "+201000000001",
            CartSnapshotJson = "{}",
            AbandonedAtUtc = DateTime.UtcNow.AddHours(-2),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-3),
            UpdatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        var secondCart = new AbandonedCart
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = Guid.NewGuid(),
            PhoneNumber = "+201000000002",
            CartSnapshotJson = "{}",
            AbandonedAtUtc = DateTime.UtcNow.AddHours(-5),
            RecoveredAtUtc = DateTime.UtcNow.AddHours(-1),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-6),
            UpdatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        dbContext.AbandonedCarts.AddRange(firstCart, secondCart);
        dbContext.AbandonedCartRecoveryLogs.AddRange(
            new AbandonedCartRecoveryLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AbandonedCartId = firstCart.Id,
                CartId = firstCart.CartId,
                PhoneNumber = firstCart.PhoneNumber,
                FirstMessageSent = true,
                SecondMessageSent = false,
                WorkflowStartedAtUtc = DateTime.UtcNow.AddHours(-2)
            },
            new AbandonedCartRecoveryLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AbandonedCartId = secondCart.Id,
                CartId = secondCart.CartId,
                PhoneNumber = secondCart.PhoneNumber,
                FirstMessageSent = true,
                SecondMessageSent = true,
                WorkflowStartedAtUtc = DateTime.UtcNow.AddHours(-5)
            });
        await dbContext.SaveChangesAsync();

        var service = new AbandonedCartRecoveryTelemetryService(
            dbContext,
            NullLogger<AbandonedCartRecoveryTelemetryService>.Instance);

        var stats = await service.GetStatsAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, stats.TotalAbandoned);
        Assert.Equal(3, stats.MessagesSent);
        Assert.Equal(1, stats.Recovered);
        Assert.Equal(50m, stats.RecoveryRate);
    }

    [Fact]
    public async Task LogRecoveryResult_RequiresApiKey_WhenAnonymous()
    {
        var tenantId = Guid.NewGuid();
        var settingsService = new Mock<ITenantAutomationSettingsService>();
        settingsService
            .Setup(service => service.GetSettingsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAutomationSettings
            {
                TenantId = tenantId,
                N8nWebhookApiKey = "expected-key"
            });

        var controller = CreateController(
            settingsService.Object,
            Mock.Of<IAbandonedCartRecoveryTelemetryService>());

        var result = await controller.LogRecoveryResult(new AbandonedCartRecoveryLogRequest
        {
            TenantId = tenantId,
            AbandonedCartId = Guid.NewGuid(),
            ApiKey = "wrong-key"
        }, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var payload = Assert.IsType<AbandonedCartRecoveryLogResponse>(unauthorized.Value);
        Assert.False(payload.Success);
        Assert.Equal("Invalid API key", payload.Message);
    }

    [Fact]
    public async Task LogRecoveryResult_ReturnsServerError_WhenPersistenceFails()
    {
        var tenantId = Guid.NewGuid();
        var settingsService = new Mock<ITenantAutomationSettingsService>();
        settingsService
            .Setup(service => service.GetSettingsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAutomationSettings
            {
                TenantId = tenantId,
                N8nWebhookApiKey = "expected-key"
            });

        var telemetryService = new Mock<IAbandonedCartRecoveryTelemetryService>();
        telemetryService
            .Setup(service => service.RecordWorkflowResultAsync(It.IsAny<AbandonedCartRecoveryLogCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db write failed"));

        var controller = CreateController(settingsService.Object, telemetryService.Object);

        var result = await controller.LogRecoveryResult(new AbandonedCartRecoveryLogRequest
        {
            TenantId = tenantId,
            AbandonedCartId = Guid.NewGuid(),
            ApiKey = "expected-key"
        }, CancellationToken.None);

        var serverError = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, serverError.StatusCode);
        var payload = Assert.IsType<AbandonedCartRecoveryLogResponse>(serverError.Value);
        Assert.False(payload.Success);
        Assert.Equal("Recovery log processing failed", payload.Message);
    }

    private static AdminAbandonedCartRecoveryController CreateController(
        ITenantAutomationSettingsService settingsService,
        IAbandonedCartRecoveryTelemetryService telemetryService,
        ITenantContext? tenantContext = null)
    {
        tenantContext ??= Mock.Of<ITenantContext>();
        return new AdminAbandonedCartRecoveryController(
            tenantContext,
            settingsService,
            Mock.Of<IAbandonedCartEligibilityService>(),
            telemetryService,
            new AbandonedCartRecoveryTelemetry(),
            NullLogger<AdminAbandonedCartRecoveryController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
