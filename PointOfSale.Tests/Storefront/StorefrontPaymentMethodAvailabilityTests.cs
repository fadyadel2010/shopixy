using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.Payments;
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
    public async Task EnabledValidWallet_WithMissingRuntimeCapability_IsSuppressedWithoutHidingCod()
    {
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository.Setup(repo => repo.GetEnabledPaymentMethodsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateDocument("COD", "COD"), CreateDocument("MANUALWALLET", "Wallet")]);
        var runtime = CreateAvailableRuntime();
        runtime.Setup(service => service.EvaluateAsync(_tenantId, "MANUALWALLET", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontPaymentRuntimeReadiness.Blocked("MANUAL_PAYMENT_CAPABILITY_DISABLED"));
        var controller = CreateController(repository.Object, CreateDbContext(), runtime.Object);
        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(Assert.IsType<OkObjectResult>(
            await controller.GetPaymentMethods(default)).Value);
        Assert.Equal("COD", Assert.Single(response.PaymentMethods).Code);
        Assert.Equal(2, response.EnabledCount);
        Assert.Equal(1, response.AvailableCount);
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

    [Fact]
    public async Task ConfiguredPaymob_IsSuppressedWhenExecutionRuntimeIsUnavailable()
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
                    Code = "PAYMOB",
                    DisplayName = "Online Payment",
                    IsEnabled = true,
                    IsConfigurationValid = true
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

        var runtime = CreateAvailableRuntime();
        runtime
            .Setup(service => service.EvaluateAsync(
                _tenantId,
                "PAYMOB",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_RUNTIME_DISABLED"));

        var controller = CreateController(repository.Object, CreateDbContext(), runtime.Object);
        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(okResult.Value);
        Assert.Equal("COD", Assert.Single(response.PaymentMethods).Code);
        Assert.Equal(1, response.AvailableCount);
    }

    [Fact]
    public async Task ConfiguredEnabledStripe_IsIncludedWithoutChangingOtherMethods()
    {
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository
            .Setup(repo => repo.GetEnabledPaymentMethodsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentMethodDocument>
            {
                CreateDocument("COD", "Cash on Delivery"),
                CreateDocument("STRIPE", "ST Credit/Debit Card"),
                CreateDocument("PAYMOB", "Online Payment"),
                CreateDocument("MANUALWALLET", "Manual Wallet")
            });

        var controller = CreateController(repository.Object);
        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(["COD", "STRIPE", "PAYMOB", "MANUALWALLET"], response.PaymentMethods.Select(method => method.Code));
        Assert.All(response.PaymentMethods, method => Assert.True(method.IsAvailable));
    }

    [Theory]
    [InlineData("STRIPE_RUNTIME_DISABLED")]
    [InlineData("STRIPE_SANDBOX_DISABLED")]
    [InlineData("STRIPE_PAYMENT_INTENT_CAPABILITY_DISABLED")]
    [InlineData("STRIPE_WEBHOOK_CAPABILITY_DISABLED")]
    [InlineData("STRIPE_NOT_CONFIGURED")]
    public async Task Stripe_IsSuppressedWhenCanonicalRuntimeReadinessFails(string gate)
    {
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository
            .Setup(repo => repo.GetEnabledPaymentMethodsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentMethodDocument>
            {
                CreateDocument("STRIPE", "ST Credit/Debit Card"),
                CreateDocument("COD", "Cash on Delivery")
            });
        var runtime = CreateAvailableRuntime();
        runtime
            .Setup(service => service.EvaluateAsync(_tenantId, "STRIPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontPaymentRuntimeReadiness.Blocked(gate));

        var controller = CreateController(repository.Object, CreateDbContext(), runtime.Object);
        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var response = Assert.IsType<StorefrontPaymentMethodsResponseDto>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("COD", Assert.Single(response.PaymentMethods).Code);
    }

    private StorefrontPaymentMethodsController CreateController(IPaymentMethodReadRepository repository)
    {
        return CreateController(repository, CreateDbContext(), CreateAvailableRuntime().Object);
    }

    private StorefrontPaymentMethodsController CreateController(
        IPaymentMethodReadRepository repository,
        ApplicationDbContext dbContext,
        IStorefrontPaymentRuntimeReadinessService? runtimeReadinessService = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(_tenantId);

        return new StorefrontPaymentMethodsController(
            repository,
            dbContext,
            runtimeReadinessService ?? CreateAvailableRuntime().Object,
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

    private Mock<IStorefrontPaymentRuntimeReadinessService> CreateAvailableRuntime()
    {
        var runtime = new Mock<IStorefrontPaymentRuntimeReadinessService>();
        runtime
            .Setup(service => service.EvaluateAsync(
                _tenantId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontPaymentRuntimeReadiness.Ready);
        return runtime;
    }

    private PaymentMethodDocument CreateDocument(string code, string displayName) => new()
    {
        PaymentMethodId = Guid.NewGuid(),
        TenantId = _tenantId,
        Code = code,
        DisplayName = displayName,
        IsEnabled = true,
        IsConfigurationValid = true,
        SupportsOnline = code is "STRIPE" or "PAYMOB",
        SupportsOffline = code is "COD" or "MANUALWALLET"
    };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
