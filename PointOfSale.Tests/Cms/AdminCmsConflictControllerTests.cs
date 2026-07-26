using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Cms;

public sealed class AdminCmsConflictControllerTests
{
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
            new CommerceReliabilityTelemetry(),
            new TestTenantContext(tenantId),
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
}
