using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Shipping;

public sealed class AdminShipmentListFilterContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_PropagatesCodAndSearchToTenantScopedService(bool cod)
    {
        var tenantId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetTenantId(tenantId);
        var service = new Mock<IAdminShipmentService>();
        service.Setup(x => x.ListShipmentsAsync(
                tenantId,
                It.Is<AdminShipmentListQuery>(query => query.IsCod == cod && query.Search == "ORDER-42"
                    && query.Page == 2 && query.Carrier == "manual"),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminPagedResult<AdminShipmentListItemDto>([], 2, 20, 0));

        var controller = new AdminShipmentsController(service.Object, tenant, NullLogger<AdminShipmentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = Assert.IsType<OkObjectResult>(await controller.ListShipments(
            page: 2, carrier: "manual", isCod: cod, search: "ORDER-42"));
        Assert.IsType<AdminPagedResult<AdminShipmentListItemDto>>(result.Value);
        service.VerifyAll();
    }
}
