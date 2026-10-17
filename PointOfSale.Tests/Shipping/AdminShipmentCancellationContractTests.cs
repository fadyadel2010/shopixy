using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Shipping;

public sealed class AdminShipmentCancellationContractTests
{
    [Theory]
    [InlineData("Confirmed", 200)]
    [InlineData("Requested", 202)]
    [InlineData("Unsupported", 409)]
    [InlineData("ReconciliationRequired", 409)]
    public async Task Controller_UsesOutcome_NotHttpPromise_AndOverridesBodyActor(string outcome, int status)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var shipment = Guid.NewGuid();
        var context = new TenantContext(); context.SetTenantId(tenant);
        var service = new Mock<IAdminShipmentService>();
        service.Setup(x => x.CancelShipmentAsync(tenant, shipment, It.Is<AdminCancelShipmentRequest>(r => r.ActorUserId == actor), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminShipmentCancellationResponse { ShipmentId = shipment, Outcome = outcome, CurrentStatus = outcome == "Confirmed" ? ShipmentStatus.Cancelled : ShipmentStatus.ReadyForPickup });
        var controller = new AdminShipmentsController(service.Object, context, NullLogger<AdminShipmentsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "isolated-pos")) } } };
        var result = Assert.IsType<ObjectResult>(await controller.CancelShipment(shipment, new AdminCancelShipmentRequest { Reason = "test", ActorUserId = Guid.NewGuid() }));
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(outcome, Assert.IsType<AdminShipmentCancellationResponse>(result.Value).Outcome);
        service.VerifyAll();
    }

    [Fact]
    public async Task MissingOrForeignShipment_Returns404_AndUnauthorizedReturnsForbidden()
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid();
        var context = new TenantContext(); context.SetTenantId(tenant);
        var service = new Mock<IAdminShipmentService>();
        service.Setup(x => x.CancelShipmentAsync(tenant, It.IsAny<Guid>(), It.IsAny<AdminCancelShipmentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AdminShipmentNotFoundException(Guid.NewGuid()));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "isolated-pos")) };
        var controller = new AdminShipmentsController(service.Object, context, NullLogger<AdminShipmentsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http } };
        Assert.IsType<NotFoundObjectResult>(await controller.CancelShipment(Guid.NewGuid(), new() { Reason = "test" }));
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.IsType<ForbidResult>(await controller.CancelShipment(Guid.NewGuid(), new() { Reason = "test" }));
    }
}
