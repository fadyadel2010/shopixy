using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.MarketplaceFooter;

namespace PointOfSale.Tests.Cms;

public sealed class MarketplaceFooterControllerTests
{
    [Fact]
    public async Task GetFooter_ReturnsEmptyResult_WhenRequestIsCanceled()
    {
        var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var footerReadService = new Mock<IMarketplaceFooterReadService>();
        footerReadService
            .Setup(service => service.GetFooterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationSource.Token));

        var controller = new MarketplaceFooterController(
            footerReadService.Object,
            NullLogger<MarketplaceFooterController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestAborted = cancellationSource.Token
                }
            }
        };

        var result = await controller.GetFooter("ar", cancellationSource.Token);

        Assert.IsType<EmptyResult>(result);
    }
}
