using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontPaymentMethodAvailabilityTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task InvalidPaymentMethod_IsSuppressedFromStorefrontResponse()
    {
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository
            .Setup(repo => repo.GetEnabledPaymentMethodsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentMethodDocument>
            {
                new()
                {
                    PaymentMethodId = Guid.NewGuid(),
                    TenantId = _tenantId,
                    Code = "MANUALWALLET",
                    DisplayName = "Manual Wallet",
                    IsEnabled = true,
                    IsConfigurationValid = false,
                    PublicConfiguration = new Dictionary<string, string>()
                },
                new()
                {
                    PaymentMethodId = Guid.NewGuid(),
                    TenantId = _tenantId,
                    Code = "COD",
                    DisplayName = "Cash on Delivery",
                    IsEnabled = true,
                    IsConfigurationValid = true
                }
            });

        var controller = CreateController(repository.Object);

        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var dtos = Assert.IsAssignableFrom<List<StorefrontPaymentMethodDto>>(okResult.Value);
        var dto = Assert.Single(dtos);

        Assert.Equal("COD", dto.Code);
        Assert.True(dto.IsAvailable);
        Assert.Null(dto.UnavailableReasonCode);
    }

    [Fact]
    public async Task ValidManualWallet_RemainsVisible()
    {
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository
            .Setup(repo => repo.GetEnabledPaymentMethodsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentMethodDocument>
            {
                new()
                {
                    PaymentMethodId = Guid.NewGuid(),
                    TenantId = _tenantId,
                    Code = "MANUALWALLET",
                    DisplayName = "Manual Wallet",
                    IsEnabled = true,
                    IsConfigurationValid = true,
                    PublicConfiguration = new Dictionary<string, string>
                    {
                        ["walletNumber"] = "01099998888"
                    }
                }
            });

        var controller = CreateController(repository.Object);

        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var dtos = Assert.IsAssignableFrom<List<StorefrontPaymentMethodDto>>(okResult.Value);
        var dto = Assert.Single(dtos);

        Assert.Equal("MANUALWALLET", dto.Code);
        Assert.True(dto.IsAvailable);
        Assert.Null(dto.UnavailableReasonCode);
    }

    private StorefrontPaymentMethodsController CreateController(IPaymentMethodReadRepository repository)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(_tenantId);

        return new StorefrontPaymentMethodsController(
            repository,
            new CommerceReliabilityTelemetry(),
            tenantContext,
            NullLogger<StorefrontPaymentMethodsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
