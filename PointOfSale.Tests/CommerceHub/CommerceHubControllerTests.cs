using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PointOfSale.API.CommerceHub.Services;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Platform;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubControllerTests
{
    [Fact]
    public async Task GetProducts_ShouldApplyNoStoreHeaders()
    {
        var queryService = new Mock<ICommerceHubQueryService>();
        queryService
            .Setup(service => service.GetProductsAsync(It.IsAny<CommerceHubProductsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubProductListResponse());

        var controller = CreateController(queryService.Object);

        var result = await controller.GetProducts(new CommerceHubProductsQuery(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        AssertCacheHeaders(controller.Response);
    }

    [Fact]
    public async Task Search_ShouldApplyNoStoreHeaders()
    {
        var queryService = new Mock<ICommerceHubQueryService>();
        queryService
            .Setup(service => service.SearchAsync(
                It.IsAny<CommerceHubSearchQuery>(),
                It.IsAny<IReadOnlyCollection<CommerceHubSearchFilter>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubSearchResponse());

        var parser = new Mock<ICommerceHubSearchQueryParser>();
        parser
            .Setup(service => service.Parse(It.IsAny<IQueryCollection>(), It.IsAny<string?>()))
            .Returns(Array.Empty<CommerceHubSearchFilter>());

        var controller = CreateController(queryService.Object, parser.Object);

        var result = await controller.Search(new CommerceHubSearchQuery(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        AssertCacheHeaders(controller.Response);
    }

    [Fact]
    public async Task GetProductById_ShouldApplyNoStoreHeaders()
    {
        var queryService = new Mock<ICommerceHubQueryService>();
        queryService
            .Setup(service => service.GetProductByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubProductDetailDto());

        var controller = CreateController(queryService.Object);

        var result = await controller.GetProductById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        AssertCacheHeaders(controller.Response);
    }

    private static CommerceHubController CreateController(
        ICommerceHubQueryService queryService,
        ICommerceHubSearchQueryParser? parser = null)
    {
        var rateLimitService = new Mock<IRateLimitService>();
        rateLimitService
            .Setup(service => service.CheckRateLimitAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .ReturnsAsync(new RateLimitResult
            {
                Allowed = true,
                CurrentCount = 1,
                Limit = 60
            });

        return new CommerceHubController(
            queryService,
            parser ?? Mock.Of<ICommerceHubSearchQueryParser>(),
            Mock.Of<ICommerceHubSearchIntelligenceService>(),
            rateLimitService.Object,
            dbContext: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static void AssertCacheHeaders(HttpResponse response)
    {
        Assert.Equal("no-store, no-cache, max-age=0, must-revalidate", response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", response.Headers.Pragma.ToString());
        Assert.Equal("0", response.Headers.Expires.ToString());
        Assert.Equal("no-store", response.Headers["Surrogate-Control"].ToString());
    }
}
