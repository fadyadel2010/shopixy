using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class CarrierRemittanceApiTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("not-an-actor", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", true)]
    [InlineData("a46d5a78-7a1d-408d-a51e-4336d2f2f837", false)]
    public async Task MissingAuthenticatedActor_CannotExecuteSelectOrRetrieve(string? actor, bool authenticated)
    {
        var service = new Mock<ICarrierRemittanceService>(MockBehavior.Strict);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(actor == null ? [] : [new Claim(ClaimTypes.NameIdentifier, actor)], authenticated ? "test" : null));
        var controller = new CarrierRemittancesController(service.Object, new TestTenantContext(Guid.NewGuid()))
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } } };
        Assert.IsType<ForbidResult>(await controller.Select()); Assert.IsType<ForbidResult>(await controller.Get(Guid.NewGuid()));
        Assert.IsType<ForbidResult>(await controller.Receive(new(Guid.NewGuid(), [Guid.NewGuid()], DateTimeOffset.UtcNow, "R", true)));
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConflictIsStructuredNotPosted_AndUnknownOperationIsNotSuccess()
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var service = new Mock<ICarrierRemittanceService>();
        service.Setup(s => s.ReceiveAsync(tenant, actor, It.IsAny<CarrierRemittanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CarrierRemittanceException("REMITTANCE_RECEIPT_REFERENCE_REVIEW_REQUIRED"));
        var controller = new CarrierRemittancesController(service.Object, new TestTenantContext(tenant))
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "test")) } } };
        var conflict = Assert.IsType<ConflictObjectResult>(await controller.Receive(new(Guid.NewGuid(), [Guid.NewGuid()], DateTimeOffset.UtcNow, "R", true)));
        var body = System.Text.Json.JsonSerializer.Serialize(conflict.Value);
        Assert.Contains("ReconciliationRequired", body); Assert.Contains("NotPosted", body); Assert.DoesNotContain("journalId", body);
        Assert.IsType<NotFoundResult>(await controller.Get(Guid.NewGuid()));
    }
}
