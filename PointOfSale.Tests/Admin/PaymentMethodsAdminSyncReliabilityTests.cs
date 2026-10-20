using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Admin;

public sealed class PaymentMethodsAdminSyncReliabilityTests
{
    [Fact]
    public async Task SyncPaymentMethods_PersistsOutboxBeforeReturningSuccess()
    {
        var tenantId = Guid.NewGuid();
        var callOrder = new List<string>();
        await using var dbContext = CreateDbContext(tenantId, callOrder);
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(tenantId, "COD"));
        await dbContext.SaveChangesAsync();
        dbContext.ResetTracking();

        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("publish"))
            .Returns(Task.CompletedTask);

        var controller = CreateController(dbContext, publishEndpoint.Object, tenantId);

        var result = await controller.SyncPaymentMethods(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));

        Assert.Equal(1, payload.RootElement.GetProperty("synced").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("committed").GetInt32());
        Assert.Equal(new[] { "publish", "save" }, callOrder);
        Assert.Equal(1, dbContext.TrackedSaveChangesCalls);
    }

    [Fact]
    public async Task SyncPaymentMethods_WhenCommitFails_DoesNotReturnSuccess()
    {
        var tenantId = Guid.NewGuid();
        var callOrder = new List<string>();
        await using var dbContext = CreateDbContext(tenantId, callOrder);
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(tenantId, "COD"));
        await dbContext.SaveChangesAsync();
        dbContext.ResetTracking();
        dbContext.ThrowOnSave = true;

        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("publish"))
            .Returns(Task.CompletedTask);

        var controller = CreateController(dbContext, publishEndpoint.Object, tenantId);

        await Assert.ThrowsAsync<DbUpdateException>(() => controller.SyncPaymentMethods(CancellationToken.None));

        Assert.Equal(new[] { "publish", "save" }, callOrder);
        Assert.Equal(1, dbContext.TrackedSaveChangesCalls);
    }

    private static PaymentMethodsAdminController CreateController(
        TrackingApplicationDbContext dbContext,
        IEventPublisher publishEndpoint,
        Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new PaymentMethodsAdminController(
            dbContext,
            publishEndpoint,
            new EphemeralDataProtectionProvider(),
            new CommerceReliabilityTelemetry(),
            NullLogger<PaymentMethodsAdminController>.Instance,
            tenantContext,
            PointOfSale.Tests.Infrastructure.PaymentMethodRuntimeTestStub.Ready())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static TrackingApplicationDbContext CreateDbContext(Guid tenantId, List<string> callOrder)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new TrackingApplicationDbContext(options, tenantContext, callOrder);
    }

    private static TenantPaymentMethod CreatePaymentMethod(Guid tenantId, string code)
    {
        return new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            DisplayName = "Cash on Delivery",
            IsEnabled = true,
            SortOrder = 1,
            SupportsOnline = false,
            SupportsOffline = true,
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class TrackingApplicationDbContext : ApplicationDbContext
    {
        private readonly List<string> _callOrder;

        public TrackingApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext,
            List<string> callOrder)
            : base(options, tenantContext)
        {
            _callOrder = callOrder;
        }

        public bool ThrowOnSave { get; set; }
        public int TrackedSaveChangesCalls { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            TrackedSaveChangesCalls++;
            _callOrder.Add("save");

            if (ThrowOnSave)
            {
                throw new DbUpdateException("Simulated commit failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        public void ResetTracking()
        {
            TrackedSaveChangesCalls = 0;
            _callOrder.Clear();
        }
    }
}
