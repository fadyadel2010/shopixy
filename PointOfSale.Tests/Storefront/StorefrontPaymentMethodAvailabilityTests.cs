using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
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
        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(okResult.Value);
        var dtos = response.PaymentMethods;
        var dto = Assert.Single(dtos);

        Assert.Equal("AVAILABLE", response.Status);
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
        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(okResult.Value);
        var dtos = response.PaymentMethods;
        var dto = Assert.Single(dtos);

        Assert.Equal("AVAILABLE", response.Status);
        Assert.Equal("MANUALWALLET", dto.Code);
        Assert.True(dto.IsAvailable);
        Assert.Null(dto.UnavailableReasonCode);
    }

    [Fact]
    public async Task NoConfiguredPaymentMethods_ReturnsEmptyStateNotFailure()
    {
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository
            .Setup(repo => repo.GetEnabledPaymentMethodsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentMethodDocument>());

        var controller = CreateController(repository.Object);

        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(okResult.Value);

        Assert.Equal("EMPTY", response.Status);
        Assert.Empty(response.PaymentMethods);
        Assert.Equal(0, response.ConfiguredCount);
        Assert.Equal(0, response.AvailableCount);
    }

    [Fact]
    public async Task DisabledPaymentMethods_ReturnEmptyStateWithConfiguredEvidence()
    {
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository
            .Setup(repo => repo.GetEnabledPaymentMethodsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentMethodDocument>());

        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Code = "COD",
            DisplayName = "Cash on Delivery",
            IsEnabled = false
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(repository.Object, dbContext);

        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(okResult.Value);

        Assert.Equal("EMPTY", response.Status);
        Assert.Empty(response.PaymentMethods);
        Assert.Equal(1, response.ConfiguredCount);
        Assert.Equal(0, response.AvailableCount);
    }

    private StorefrontPaymentMethodsController CreateController(IPaymentMethodReadRepository repository)
    {
        return CreateController(repository, CreateDbContext());
    }

    private StorefrontPaymentMethodsController CreateController(
        IPaymentMethodReadRepository repository,
        ApplicationDbContext dbContext)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(_tenantId);

        return new StorefrontPaymentMethodsController(
            repository,
            dbContext,
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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
