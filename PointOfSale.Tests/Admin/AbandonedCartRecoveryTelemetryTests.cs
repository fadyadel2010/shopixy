using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers.Admin;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
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
        _connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
        _connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (value, characters) => value?.Trim(characters?.ToCharArray() ?? []));
        _connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        _connection.CreateFunction<string?, string, string, string, string?>(
            "regexp_replace",
            (value, pattern, replacement, flags) => value == null
                ? null
                : Regex.Replace(
                    value,
                    pattern,
                    replacement,
                    flags.Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None));

        // Create the database schema once during test fixture setup
        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsRealCounts()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Recovery Store",
            Slug = $"recovery-{tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });

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
            RecoveredOrderId = Guid.NewGuid(),
            RecoveryStatus = AbandonedCartRecoveryStatus.Recovered,
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
    public async Task UpdateSettings_WhenNativeSchedulerIsUnavailable_FailsClosedWithStableCode()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(context => context.TenantId).Returns(tenantId);
        tenantContext.SetupGet(context => context.RequiredTenantId).Returns(tenantId);

        var settingsService = new Mock<ITenantAutomationSettingsService>(MockBehavior.Strict);
        var eligibilityService = new Mock<IAbandonedCartEligibilityService>(MockBehavior.Strict);
        eligibilityService
            .Setup(service => service.GetEligibilityStatusAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbandonedCartEligibilityStatus
            {
                HasWhatsAppAccount = true,
                WhatsAppAccountEnabled = true,
                WhatsAppAccountConnected = true,
                NativeSchedulerOperational = false,
                NativeDispatcherOperational = true,
                IneligibilityCode = "native_scheduler_unavailable",
                IneligibilityReason = "No healthy native recovery worker is available"
            });

        var controller = CreateController(
            settingsService.Object,
            Mock.Of<IAbandonedCartRecoveryTelemetryService>(),
            tenantContext.Object,
            eligibilityService.Object);

        var result = await controller.UpdateSettings(new UpdateAutomationSettingsRequest
        {
            EnableAbandonedRecovery = true,
            FirstMessageDelayMinutes = 60,
            SecondMessageDelayMinutes = 1440,
            WorkingHoursFrom = "09:00",
            WorkingHoursTo = "21:00"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<AutomationApiErrorResponse>(badRequest.Value);
        Assert.Equal("native_scheduler_unavailable", payload.Code);
        Assert.Equal("No healthy native recovery worker is available", payload.Details);
        settingsService.Verify(
            service => service.UpdateSettingsAsync(
                It.IsAny<Guid>(),
                It.IsAny<TenantAutomationSettings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        eligibilityService.VerifyAll();
    }

    private static AdminAbandonedCartRecoveryController CreateController(
        ITenantAutomationSettingsService settingsService,
        IAbandonedCartRecoveryTelemetryService telemetryService,
        ITenantContext? tenantContext = null,
        IAbandonedCartEligibilityService? eligibilityService = null)
    {
        tenantContext ??= Mock.Of<ITenantContext>();
        return new AdminAbandonedCartRecoveryController(
            tenantContext,
            settingsService,
            eligibilityService ?? Mock.Of<IAbandonedCartEligibilityService>(),
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
