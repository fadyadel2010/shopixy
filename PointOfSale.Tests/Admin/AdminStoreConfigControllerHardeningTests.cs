using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services;
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

    [Fact]
    public async Task UpdateStorefrontStatus_WithoutExpectedRowVersion_ReturnsPreconditionRequired()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.UpdateStorefrontStatus(new AdminStorefrontStatusRequest
        {
            StorefrontStatus = "live"
        }, CancellationToken.None);

        var precondition = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, precondition.StatusCode);
        var error = Assert.IsType<AdminStoreConfigApiErrorResponse>(precondition.Value);
        Assert.Equal("PRECONDITION_REQUIRED", error.Code);
    }

    [Fact]
    public async Task UpdateStorefrontStatus_WithInvalidStatus_ReturnsValidationErrorWithoutFullConfigValidation()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.UpdateStorefrontStatus(new AdminStorefrontStatusRequest
        {
            StorefrontStatus = "archived",
            RowVersion = "7"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<AdminStoreConfigApiErrorResponse>(badRequest.Value);
        Assert.Equal("INVALID_STORE_CONFIG", error.Code);
        Assert.Contains("StorefrontStatus", error.Message, StringComparison.Ordinal);
        service.Verify(
            item => item.UpdateStoreConfigAsync(
                It.IsAny<Guid>(),
                It.IsAny<AdminStoreConfigRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<uint?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateStorefrontStatus_ValidRequest_UsesStatusOnlyServiceContract()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateStorefrontStatusAsync(
                tenantId,
                It.Is<AdminStorefrontStatusRequest>(request =>
                    request.StorefrontStatus == "live" &&
                    request.MaintenanceMessage == string.Empty &&
                    request.RowVersion == "7"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                7))
            .ReturnsAsync(new AdminStoreConfigResponse
            {
                TenantId = tenantId,
                RowVersion = "8",
                StoreName = "Status Store",
                ThemeId = "default",
                StorefrontStatus = "live"
            });

        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.UpdateStorefrontStatus(new AdminStorefrontStatusRequest
        {
            StorefrontStatus = "live",
            MaintenanceMessage = string.Empty,
            RowVersion = "7"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminStoreConfigResponse>(ok.Value);
        Assert.Equal("live", response.StorefrontStatus);
        service.VerifyAll();
        service.Verify(
            item => item.UpdateStoreConfigAsync(
                It.IsAny<Guid>(),
                It.IsAny<AdminStoreConfigRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<uint?>()),
            Times.Never);
    }

    [Fact]
    public async Task SelectThemePreset_WhenPresetIsUnavailable_ReturnsInvalidConfig()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        service
            .Setup(item => item.SelectThemePresetAsync(
                tenantId,
                "missing-theme",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                9))
            .ThrowsAsync(new InvalidOperationException("Theme preset 'missing-theme' not found"));

        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["If-Match"] = "\"9\"";

        var result = await controller.SelectThemePreset("missing-theme", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<AdminStoreConfigApiErrorResponse>(badRequest.Value);
        Assert.Equal("INVALID_STORE_CONFIG", error.Code);
        Assert.Contains("missing-theme", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateThemeOverrides_WhenPayloadIsInvalid_ReturnsInvalidConfig()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateThemeOverridesAsync(
                tenantId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                10))
            .ThrowsAsync(new ArgumentException("Theme override 'currency' is not supported."));

        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["If-Match"] = "\"10\"";

        var result = await controller.UpdateThemeOverrides(new { currency = "USD" }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<AdminStoreConfigApiErrorResponse>(badRequest.Value);
        Assert.Equal("INVALID_STORE_CONFIG", error.Code);
        Assert.Contains("currency", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishTheme_WhenPublishLockIsContended_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var service = new Mock<IAdminStoreConfigService>(MockBehavior.Strict);
        service
            .Setup(item => item.PublishThemeAsync(
                tenantId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                11))
            .ThrowsAsync(new ConcurrencyConflictException("StoreConfigThemePublish", tenantId));

        var controller = CreateController(service.Object, tenantId);
        controller.Request.Headers["If-Match"] = "\"11\"";

        var result = await controller.PublishTheme(CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<AdminStoreConfigApiErrorResponse>(conflict.Value);
        Assert.Equal("CONCURRENCY_CONFLICT", error.Code);
    }

    private static AdminStoreConfigController CreateController(IAdminStoreConfigService service, Guid tenantId)
    {
        return new AdminStoreConfigController(
            service,
            Mock.Of<IAdminThemeActivationService>(),
            Mock.Of<IStoreConfigAuditService>(),
            new TestTenantContext(tenantId),
            NullLogger<AdminStoreConfigController>.Instance,
            CreateStorefrontUrlResolver())
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

    private static StorefrontUrlResolver CreateStorefrontUrlResolver() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.test"
            })
            .Build(),
        NullLogger<StorefrontUrlResolver>.Instance,
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

    private static AdminStoreConfigRequest CreateValidRequest() =>
        new()
        {
            StoreName = "Security Hardened Store",
            ThemeId = "default"
        };
}
