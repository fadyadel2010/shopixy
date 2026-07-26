using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Audit;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminStoreConfigControllerHardeningTests
{
    [Fact]
    public async Task GetStoreConfigPreview_WhenHeaderDoesNotMatchTrustedTenant_UsesTenantContext()
    {
        var trustedTenantId = Guid.NewGuid();
        var staleHeaderTenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetStoreConfigPreviewAsync(
                trustedTenantId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminStoreConfigResponse
            {
                TenantId = trustedTenantId,
                StoreName = "Trusted Store"
            });

        var controller = CreateController(service.Object, trustedTenantId);

        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("TenantId", trustedTenantId.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
            },
            "TestAuth"));
        controller.Request.Headers["X-Tenant-Id"] = staleHeaderTenantId.ToString("D");

        var result = await controller.GetStoreConfigPreview(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AdminStoreConfigResponse>(ok.Value);
        Assert.Equal(trustedTenantId, payload.TenantId);
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateStoreConfig_WithoutExpectedRowVersion_ReturnsPreconditionRequired()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.UpdateStoreConfig(CreateValidRequest(), CancellationToken.None);

        var precondition = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, precondition.StatusCode);
        var error = Assert.IsType<AdminStoreConfigApiErrorResponse>(precondition.Value);
        Assert.Equal("PRECONDITION_REQUIRED", error.Code);
    }

    [Fact]
    public async Task UpdateStoreConfig_WhenServiceDetectsConcurrencyConflict_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>();
        service
            .Setup(item => item.UpdateStoreConfigAsync(
                tenantId,
                It.IsAny<AdminStoreConfigRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                7))
            .ThrowsAsync(new ConcurrencyConflictException("StoreConfig", Guid.NewGuid()));

        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var request = CreateValidRequest();
        request.RowVersion = "7";

        var result = await controller.UpdateStoreConfig(request, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<AdminStoreConfigApiErrorResponse>(conflict.Value);
        Assert.Equal("CONCURRENCY_CONFLICT", error.Code);
    }

    private static AdminStoreConfigController CreateController(IAdminStoreConfigService service, Guid tenantId)
    {
        return new AdminStoreConfigController(
            service,
            Mock.Of<IStoreConfigAuditService>(),
            new TestTenantContext(tenantId),
            NullLogger<AdminStoreConfigController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection()
                        .AddSingleton(new CommerceReliabilityTelemetry())
                        .BuildServiceProvider()
                }
            }
        };
    }

    private static AdminStoreConfigRequest CreateValidRequest() =>
        new()
        {
            StoreName = "Security Hardened Store",
            ThemeId = "default"
        };
}
