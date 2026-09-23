using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Cms;

public sealed class AdminCmsConflictControllerTests
{
    [Fact]
    public async Task UpdateNavigation_WhenAdmissionFails_ReturnsStructuredUnprocessableEntity()
    {
        var tenantId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var traceId = "trace-navigation-admission";
        var cmsService = new Mock<IAdminCmsService>();
        cmsService
            .Setup(service => service.UpdateNavigationAsync(
                tenantId,
                It.IsAny<AdminNavigationRequest>(),
                7,
                traceId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NavigationAdmissionException(
                "NAVIGATION_PRODUCT_DELETED",
                "The selected product was deleted.",
                "product",
                "header",
                0,
                "targetId",
                targetId));
        var controller = CreateController(cmsService.Object, tenantId, traceId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.UpdateNavigation(
            new AdminNavigationRequest
            {
                RowVersion = "7",
                Header =
                [
                    new AdminNavigationLinkRequest
                    {
                        Type = "product",
                        Label = "Deleted product",
                        TargetId = targetId
                    }
                ],
                Footer = []
            },
            CancellationToken.None);

        var invalid = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var payload = Assert.IsType<AdminCmsApiErrorResponse>(invalid.Value);
        Assert.Equal("NAVIGATION_PRODUCT_DELETED", payload.Code);
        Assert.Equal("header", payload.Navigation?.Menu);
        Assert.Equal(0, payload.Navigation?.Index);
        Assert.Equal(targetId, payload.Navigation?.TargetId);
    }

    [Fact]
    public async Task PublishNavigation_WhenCmsPageBecameUnpublished_UsesSameStructuredErrorContract()
    {
        var tenantId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var traceId = "trace-navigation-publish-admission";
        var cmsService = new Mock<IAdminCmsService>();
        cmsService
            .Setup(service => service.PublishNavigationAsync(
                tenantId,
                9,
                traceId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NavigationAdmissionException(
                "NAVIGATION_CMS_PAGE_UNPUBLISHED",
                "The selected CMS page must be published first.",
                "cms",
                "footer",
                1,
                "targetId",
                pageId));
        var controller = CreateController(cmsService.Object, tenantId, traceId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        controller.Request.Headers["If-Match"] = "\"9\"";

        var result = await controller.PublishNavigation(CancellationToken.None);

        var invalid = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var payload = Assert.IsType<AdminCmsApiErrorResponse>(invalid.Value);
        Assert.Equal("NAVIGATION_CMS_PAGE_UNPUBLISHED", payload.Code);
        Assert.Equal("footer", payload.Navigation?.Menu);
        Assert.Equal(pageId, payload.Navigation?.TargetId);
    }

    [Fact]
    public async Task UpdateNavigation_WhenRowVersionIsZero_RejectsPreconditionBeforeServiceCall()
    {
        var tenantId = Guid.NewGuid();
        var traceId = "trace-navigation-zero-version";
        var cmsService = new Mock<IAdminCmsService>(MockBehavior.Strict);
        var controller = CreateController(cmsService.Object, tenantId, traceId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.UpdateNavigation(
            new AdminNavigationRequest { RowVersion = "0", Header = [], Footer = [] },
            CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, rejected.StatusCode);
        var payload = Assert.IsType<AdminCmsApiErrorResponse>(rejected.Value);
        Assert.Equal("PRECONDITION_REQUIRED", payload.Code);
        cmsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishNavigation_WhenRowVersionIsMissing_RejectsPreconditionBeforeServiceCall()
    {
        var tenantId = Guid.NewGuid();
        var traceId = "trace-navigation-missing-version";
        var cmsService = new Mock<IAdminCmsService>(MockBehavior.Strict);
        var controller = CreateController(cmsService.Object, tenantId, traceId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.PublishNavigation(CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, rejected.StatusCode);
        var payload = Assert.IsType<AdminCmsApiErrorResponse>(rejected.Value);
        Assert.Equal("PRECONDITION_REQUIRED", payload.Code);
        cmsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateCmsPage_WhenConcurrentEditDetected_ReturnsConflictWithLatestVersion()
    {
        var tenantId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var traceId = "trace-cms-conflict";

        var latestVersion = new AdminCmsPageResponse
        {
            Id = pageId,
            TenantId = tenantId,
            Slug = "about",
            Title = "About Us",
            ContentType = CmsPageContentType.RichText,
            Draft = new AdminCmsPageVersionResponse
            {
                RecordId = Guid.NewGuid(),
                Slug = "about",
                Title = "About Us",
                ContentType = CmsPageContentType.RichText,
                Content = JsonSerializer.SerializeToElement("<p>Latest content</p>"),
                Version = 4,
                SchemaVersion = 1,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAtUtc = DateTime.UtcNow,
            },
        };

        var cmsService = new Mock<IAdminCmsService>();
        cmsService
            .Setup(service => service.UpdateCmsPageAsync(
                tenantId,
                pageId,
                It.IsAny<AdminCmsPageRequest>(),
                traceId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("CmsPage", pageId));
        cmsService
            .Setup(service => service.GetCmsPageAsync(
                tenantId,
                pageId,
                traceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestVersion);

        var controller = new AdminCmsPagesController(
            cmsService.Object,
            Mock.Of<ICmsSeedService>(),
            new CommerceReliabilityTelemetry(),
            new TestTenantContext(tenantId),
            CreateStorefrontUrlResolver(),
            NullLogger<AdminCmsPagesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = traceId,
                },
            },
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var request = new AdminCmsPageRequest
        {
            Slug = "about",
            Title = "About Updated",
            ContentType = CmsPageContentType.RichText,
            Content = JsonSerializer.SerializeToElement("<p>Draft content</p>"),
            SchemaVersion = 1,
            ExpectedVersion = 3,
        };

        var result = await controller.UpdateCmsPage(pageId, request, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        var payload = Assert.IsType<AdminCmsApiErrorResponse>(conflict.Value);
        Assert.Equal("CONCURRENCY_CONFLICT", payload.Code);
        Assert.Equal(
            "This page was updated by another admin. Review the latest version before saving again.",
            payload.Message);

        var latestPayload = Assert.IsType<AdminCmsPageResponse>(payload.LatestVersion);
        Assert.Equal(pageId, latestPayload.Id);
        Assert.Equal("About Us", latestPayload.Title);
        Assert.Equal(4, latestPayload.Draft?.Version);
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

    [Fact]
    public async Task PublishCmsPage_WhenConcurrentEditDetected_ReturnsStructuredConflictWithLatestVersion()
    {
        var tenantId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var traceId = "trace-cms-publish-conflict";
        var latestVersion = new AdminCmsPageResponse
        {
            Id = pageId,
            TenantId = tenantId,
            Slug = "about",
            Title = "About Us",
            ContentType = CmsPageContentType.RichText,
            Draft = new AdminCmsPageVersionResponse
            {
                RecordId = Guid.NewGuid(),
                Slug = "about",
                Title = "About Us",
                ContentType = CmsPageContentType.RichText,
                Content = JsonSerializer.SerializeToElement("<p>Latest content</p>"),
                Version = 6,
                SchemaVersion = 1,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAtUtc = DateTime.UtcNow,
            },
        };
        var cmsService = new Mock<IAdminCmsService>();
        cmsService
            .Setup(service => service.PublishCmsPageAsync(
                tenantId,
                pageId,
                5,
                traceId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("CmsPage", pageId));
        cmsService
            .Setup(service => service.GetCmsPageAsync(
                tenantId,
                pageId,
                traceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestVersion);
        var controller = CreateController(cmsService.Object, tenantId, traceId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        controller.Request.Headers["If-Match"] = "\"5\"";

        var result = await controller.PublishCmsPage(pageId, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var payload = Assert.IsType<AdminCmsApiErrorResponse>(conflict.Value);
        Assert.Equal("CONCURRENCY_CONFLICT", payload.Code);
        Assert.Equal("This page changed before it could be published. Reload the latest version and confirm the publish again.", payload.Message);
        Assert.Same(latestVersion, payload.LatestVersion);
    }

    [Fact]
    public async Task PublishCmsPage_WhenValidationFails_ReturnsStructuredBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var traceId = "trace-cms-publish-validation";
        var cmsService = new Mock<IAdminCmsService>();
        cmsService
            .Setup(service => service.PublishCmsPageAsync(
                tenantId,
                pageId,
                3,
                traceId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Draft content is invalid."));
        var controller = CreateController(cmsService.Object, tenantId, traceId);
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        controller.Request.Headers["If-Match"] = "\"3\"";

        var result = await controller.PublishCmsPage(pageId, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<AdminCmsApiErrorResponse>(badRequest.Value);
        Assert.Equal("PUBLISH_FAILED", payload.Code);
        Assert.Equal("Draft content is invalid.", payload.Message);
    }

    private static AdminCmsPagesController CreateController(
        IAdminCmsService cmsService,
        Guid tenantId,
        string traceId) => new(
            cmsService,
            Mock.Of<ICmsSeedService>(),
            new CommerceReliabilityTelemetry(),
            new TestTenantContext(tenantId),
            CreateStorefrontUrlResolver(),
            NullLogger<AdminCmsPagesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = traceId
                }
            }
        };
}
