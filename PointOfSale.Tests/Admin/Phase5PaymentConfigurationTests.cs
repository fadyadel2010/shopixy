using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class Phase5PaymentConfigurationTests
{
    [Theory]
    [InlineData("negative-fee")]
    [InlineData("negative-min")]
    [InlineData("negative-max")]
    [InlineData("inverted-limits")]
    [InlineData("unknown-fee-type")]
    [InlineData("negative-sort")]
    [InlineData("blank-name")]
    [InlineData("overlong-name")]
    public async Task InvalidMetadata_IsRejectedBeforePersistenceOrEventStaging(string scenario)
    {
        var tenant = Guid.NewGuid();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(tenant));
        using var telemetry = new CommerceReliabilityTelemetry();
        var events = new Mock<IEventPublisher>();
        events.SetReturnsDefault(Task.CompletedTask);
        var controller = new PaymentMethodsAdminController(db, events.Object, new EphemeralDataProtectionProvider(),
            telemetry, NullLogger<PaymentMethodsAdminController>.Instance, new TestTenantContext(tenant), PaymentMethodRuntimeTestStub.Ready())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var dto = new UpsertPaymentMethodDto { DisplayName = "P5QA validation" };
        switch (scenario) {
            case "negative-fee": dto.Fee = -1; break;
            case "negative-min": dto.MinOrderAmount = -1; break;
            case "negative-max": dto.MaxOrderAmount = -1; break;
            case "inverted-limits": dto.MinOrderAmount = 20; dto.MaxOrderAmount = 10; break;
            case "unknown-fee-type": dto.FeeType = "unsupported"; break;
            case "negative-sort": dto.SortOrder = -1; break;
            case "blank-name": dto.DisplayName = " "; break;
            case "overlong-name": dto.DisplayName = new string('x', 101); break;
        }
        Assert.IsType<BadRequestObjectResult>(await controller.UpsertPaymentMethod("COD", dto, default));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.TenantPaymentMethods.ToListAsync());
        events.VerifyNoOtherCalls();
    }
}
