using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Moq;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Events;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.TestDoubles;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubCheckoutFinancialControlTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubCheckoutService _service;

    public CommerceHubCheckoutFinancialControlTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        var walletService = new CommerceHubWalletService(_dbContext);
        var ledgerService = new CommerceHubFinancialLedgerService(
            _dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);
        var debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var financialSafetyGuardService = new CommerceHubFinancialSafetyGuardService(_dbContext);
        var controlService = new CommerceHubVendorFinancialControlService(
            _dbContext,
            walletService,
            debtService,
            ledgerService,
            financialSafetyGuardService);

        _service = CreateService(controlService, new NoOpUserGovernanceService());
    }

    [Fact]
    public async Task CheckoutAsync_WhenVendorIsBelowHardLimit_ShouldRejectNewOrder()
    {
        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();

        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Suspended Vendor",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        _dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = vendorId,
            Name = "Suspended Product",
            CategoryId = Guid.NewGuid(),
            SalePrice = 100m,
            OnlinePrice = 100m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published
        });

        _dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = vendorId,
            ProductId = productId,
            Price = 100m,
            OnlinePrice = 100m,
            CostPrice = 60m,
            IsDefault = true,
            IsActive = true
        });

        var listing = CommerceHubListing.Create(
            vendorId,
            productId,
            variantId,
            commerceHubCategoryId,
            null);
        _dbContext.CommerceHubListings.Add(listing);
        _dbContext.CommerceHubCategories.Add(CreateActiveCommerceHubCategory(commerceHubCategoryId));

        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                CommerceHubFinancialTransactionType.Commission,
                CommerceHubFinancialTransactionDirection.Debit,
                1200m,
                "EGP",
                CommerceHubFinancialReferenceType.Order,
                Guid.NewGuid(),
                "Outstanding COD commission debt",
                createdAtUtc: DateTime.UtcNow.AddDays(-5)));
        _dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(vendorId, CommerceHubPaymentMethods.Cod, supportsOffline: true));

        await _dbContext.SaveChangesAsync();
        await SeedAuthenticatedCustomerCartAsync(customerId, vendorId, productId, variantId, listing.Id);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.CheckoutAsync(
                BuildCheckoutRequest(vendorId, productId, variantId, listing.Id),
                customerId,
                "corr-1",
                "idem-1"));

        Assert.Equal("VENDOR_SUSPENDED_FOR_SETTLEMENT", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CheckoutAsync_WhenVendorIsGovernanceSuspended_ShouldRejectNewOrder()
    {
        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var listingId = await SeedActiveVendorListingAsync(vendorId, productId, variantId);
        await SeedPaymentMethodAsync(vendorId, CommerceHubPaymentMethods.Cod, supportsOffline: true);
        await SeedAuthenticatedCustomerCartAsync(customerId, vendorId, productId, variantId, listingId);

        var governanceMock = new Mock<IUserGovernanceService>();
        governanceMock
            .Setup(service => service.EnsureVendorOrderAcceptanceAllowedAsync(vendorId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UserGovernanceException(
                "VENDOR_SUSPENDED",
                "Vendor is suspended and cannot receive new CommerceHub orders.",
                409));

        var service = CreateService(
            new CommerceHubVendorFinancialControlService(
                _dbContext,
                new CommerceHubWalletService(_dbContext),
                new CommerceHubVendorDebtService(_dbContext, Options.Create(new CommerceHubVendorDebtOptions())),
                new CommerceHubFinancialLedgerService(_dbContext, NullLogger<CommerceHubFinancialLedgerService>.Instance),
                new CommerceHubFinancialSafetyGuardService(_dbContext)),
            governanceMock.Object);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CheckoutAsync(
                BuildCheckoutRequest(vendorId, productId, variantId, listingId),
                customerId,
                "corr-2",
                "idem-2"));

        Assert.Equal("VENDOR_SUSPENDED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CheckoutAsync_WhenExpectedUnitPriceIsStale_ShouldRejectBeforeOrderCreation()
    {
        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var listingId = await SeedActiveVendorListingAsync(vendorId, productId, variantId);
        await SeedPaymentMethodAsync(vendorId, CommerceHubPaymentMethods.Cod, supportsOffline: true);
        await SeedAuthenticatedCustomerCartAsync(customerId, vendorId, productId, variantId, listingId, expectedUnitPrice: 90m);

        var stockRepository = new Mock<IProductVariantStockRepository>();
        stockRepository
            .Setup(repository => repository.GetAvailableQuantityAsync(
                vendorId,
                variantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(10m);

        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(
            new CommerceHubVendorFinancialControlService(
                _dbContext,
                new CommerceHubWalletService(_dbContext),
                new CommerceHubVendorDebtService(_dbContext, Options.Create(new CommerceHubVendorDebtOptions())),
                new CommerceHubFinancialLedgerService(_dbContext, NullLogger<CommerceHubFinancialLedgerService>.Instance),
                new CommerceHubFinancialSafetyGuardService(_dbContext)),
            new NoOpUserGovernanceService(),
            stockRepository.Object,
            eventPublisher: eventPublisher.Object);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CheckoutAsync(
                BuildCheckoutRequest(vendorId, productId, variantId, listingId, expectedUnitPrice: 90m),
                customerId,
                "corr-price",
                "idem-price"));

        Assert.Equal("PRICE_CHANGED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Empty(_dbContext.CommerceHubMarketplaceOrders);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenAuthoritativeStockReservationFails_ShouldRejectBeforeOrderCreation()
    {
        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var listingId = await SeedActiveVendorListingAsync(vendorId, productId, variantId);
        await SeedPaymentMethodAsync(vendorId, CommerceHubPaymentMethods.Cod, supportsOffline: true);
        await SeedAuthenticatedCustomerCartAsync(customerId, vendorId, productId, variantId, listingId);

        var stockRepository = new Mock<IProductVariantStockRepository>();
        stockRepository
            .Setup(repository => repository.GetAvailableQuantityAsync(
                vendorId,
                variantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(10m);

        var inventoryConsistencyService = new Mock<IInventoryConsistencyService>();
        inventoryConsistencyService
            .Setup(service => service.ReserveOrderItemAsync(
                It.Is<InventoryReservationRequest>(request =>
                    request.TenantId == vendorId &&
                    request.ProductId == productId &&
                    request.VariantId == variantId &&
                    request.Quantity == 1m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(
            new CommerceHubVendorFinancialControlService(
                _dbContext,
                new CommerceHubWalletService(_dbContext),
                new CommerceHubVendorDebtService(_dbContext, Options.Create(new CommerceHubVendorDebtOptions())),
                new CommerceHubFinancialLedgerService(_dbContext, NullLogger<CommerceHubFinancialLedgerService>.Instance),
                new CommerceHubFinancialSafetyGuardService(_dbContext)),
            new NoOpUserGovernanceService(),
            stockRepository.Object,
            inventoryConsistencyService.Object,
            CreateSuccessfulShippingService(),
            eventPublisher.Object);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CheckoutAsync(
                BuildCheckoutRequest(vendorId, productId, variantId, listingId),
                customerId,
                "corr-stock",
                "idem-stock"));

        Assert.Equal("OUT_OF_STOCK", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Empty(_dbContext.CommerceHubMarketplaceOrders);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenPromoExpiresBeforeFinalSubmit_ShouldRejectBeforeOrderCreation()
    {
        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var listingId = await SeedActiveVendorListingAsync(vendorId, productId, variantId);
        await SeedPaymentMethodAsync(vendorId, CommerceHubPaymentMethods.Cod, supportsOffline: true);
        await SeedAuthenticatedCustomerCartAsync(customerId, vendorId, productId, variantId, listingId);

        _dbContext.PromoCodes.Add(new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "STALEPROMO",
            DiscountType = PromoDiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            ExpiryDate = DateTime.UtcNow.AddMinutes(-1)
        });
        await _dbContext.SaveChangesAsync();

        var stockRepository = new Mock<IProductVariantStockRepository>();
        stockRepository
            .Setup(repository => repository.GetAvailableQuantityAsync(
                vendorId,
                variantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(10m);

        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(
            new CommerceHubVendorFinancialControlService(
                _dbContext,
                new CommerceHubWalletService(_dbContext),
                new CommerceHubVendorDebtService(_dbContext, Options.Create(new CommerceHubVendorDebtOptions())),
                new CommerceHubFinancialLedgerService(_dbContext, NullLogger<CommerceHubFinancialLedgerService>.Instance),
                new CommerceHubFinancialSafetyGuardService(_dbContext)),
            new NoOpUserGovernanceService(),
            stockRepository.Object,
            shippingService: CreateSuccessfulShippingService(),
            eventPublisher: eventPublisher.Object);

        var request = BuildCheckoutRequest(vendorId, productId, variantId, listingId);
        request.PromoCode = "STALEPROMO";

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CheckoutAsync(
                request,
                customerId,
                "corr-expired-promo",
                "idem-expired-promo"));

        Assert.Equal("PROMO_EXPIRED", exception.Code);
        Assert.Empty(_dbContext.CommerceHubMarketplaceOrders);
        Assert.Empty(_dbContext.PromoCodeUsages);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenSameIdempotencyKeyReplays_ShouldReturnOneCommittedOrder()
    {
        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var listingId = await SeedActiveVendorListingAsync(vendorId, productId, variantId);
        await SeedPaymentMethodAsync(vendorId, CommerceHubPaymentMethods.Cod, supportsOffline: true);
        await SeedAuthenticatedCustomerCartAsync(customerId, vendorId, productId, variantId, listingId);

        var stockRepository = new Mock<IProductVariantStockRepository>();
        stockRepository
            .Setup(repository => repository.GetAvailableQuantityAsync(
                vendorId,
                variantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(10m);

        var inventoryConsistencyService = new Mock<IInventoryConsistencyService>();
        inventoryConsistencyService
            .Setup(service => service.ReserveOrderItemAsync(
                It.IsAny<InventoryReservationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(publisher => publisher.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderCreatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            new CommerceHubVendorFinancialControlService(
                _dbContext,
                new CommerceHubWalletService(_dbContext),
                new CommerceHubVendorDebtService(_dbContext, Options.Create(new CommerceHubVendorDebtOptions())),
                new CommerceHubFinancialLedgerService(_dbContext, NullLogger<CommerceHubFinancialLedgerService>.Instance),
                new CommerceHubFinancialSafetyGuardService(_dbContext)),
            new NoOpUserGovernanceService(),
            stockRepository.Object,
            inventoryConsistencyService.Object,
            CreateSuccessfulShippingService(),
            eventPublisher.Object);

        var request = BuildCheckoutRequest(vendorId, productId, variantId, listingId);

        var first = await service.CheckoutAsync(
            request,
            customerId,
            "corr-duplicate-first",
            "idem-duplicate");
        var replay = await service.CheckoutAsync(
            request,
            customerId,
            "corr-duplicate-replay",
            "idem-duplicate");

        Assert.False(first.IsIdempotentReplay);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(first.MarketplaceOrderId, replay.MarketplaceOrderId);
        Assert.Single(_dbContext.CommerceHubMarketplaceOrders);
        inventoryConsistencyService.Verify(
            service => service.ReserveOrderItemAsync(
                It.IsAny<InventoryReservationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Validate_WhenExpectedUnitPriceIsMissing_ShouldRejectCheckoutRequest()
    {
        var request = BuildCheckoutRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            expectedUnitPrice: null);

        var validation = request.Validate();

        Assert.Equal("EXPECTED_PRICE_REQUIRED", validation.Code);
    }

    [Fact]
    public async Task CheckoutAsync_WhenStoreHasNoEnabledPaymentMethodAuthority_ShouldFailClosed()
    {
        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var listingId = await SeedActiveVendorListingAsync(vendorId, productId, variantId);
        await SeedAuthenticatedCustomerCartAsync(customerId, vendorId, productId, variantId, listingId);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.CheckoutAsync(
                BuildCheckoutRequest(vendorId, productId, variantId, listingId),
                customerId,
                "corr-no-payment-method",
                "idem-no-payment-method"));

        Assert.Equal("PAYMENT_METHOD_NOT_AVAILABLE", exception.Code);
        Assert.Equal(400, exception.StatusCode);
        Assert.Empty(_dbContext.CommerceHubMarketplaceOrders);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CommerceHubCheckoutService CreateService(
        ICommerceHubVendorFinancialControlService controlService,
        IUserGovernanceService governanceService,
        IProductVariantStockRepository? variantStockRepository = null,
        IInventoryConsistencyService? inventoryConsistencyService = null,
        ICommerceHubShippingService? shippingService = null,
        IEventPublisher? eventPublisher = null)
    {
        var lockService = new Mock<IDistributedLockService>();
        lockService
            .Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());

        return new CommerceHubCheckoutService(
            _dbContext,
            Mock.Of<ICommerceHubCartService>(),
            variantStockRepository ?? Mock.Of<IProductVariantStockRepository>(),
            inventoryConsistencyService ?? Mock.Of<IInventoryConsistencyService>(),
            shippingService ?? Mock.Of<ICommerceHubShippingService>(),
            Mock.Of<ICommissionService>(),
            new PromoService(_dbContext),
            controlService,
            Mock.Of<ICommerceHubOrderAccessService>(),
            governanceService,
            CreateNoOpReadinessGate(),
            lockService.Object,
            eventPublisher ?? Mock.Of<IEventPublisher>(),
            new CommerceHubPriceAuthority(),
            NullLogger<CommerceHubCheckoutService>.Instance);
    }

    private static ICommerceHubFinancialReadinessGate CreateNoOpReadinessGate()
    {
        var readinessGate = new Mock<ICommerceHubFinancialReadinessGate>();
        readinessGate
            .Setup(gate => gate.EnsureCommerceIntakeAllowedAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        readinessGate
            .Setup(gate => gate.EnsureOutwardMoneyMovementAllowedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return readinessGate.Object;
    }

    private static ICommerceHubShippingService CreateSuccessfulShippingService()
    {
        var shippingService = new Mock<ICommerceHubShippingService>();
        shippingService
            .Setup(service => service.CalculateAsync(
                It.IsAny<IReadOnlyCollection<CommerceHubShippingStoreRequest>>(),
                It.IsAny<IReadOnlyDictionary<Guid, Guid>?>(),
                It.IsAny<CancellationToken>(),
                true))
            .Returns((
                IReadOnlyCollection<CommerceHubShippingStoreRequest> stores,
                IReadOnlyDictionary<Guid, Guid>? requestedSelections,
                CancellationToken cancellationToken,
                bool bypassCache) => Task.FromResult(
                    new CommerceHubShippingCalculationResult
                    {
                        TotalShipping = 0m,
                        StoreSelections = stores
                            .Select(store => new CommerceHubShippingStoreSelection
                            {
                                StoreId = store.StoreId,
                                ShippingOptionId = requestedSelections?.GetValueOrDefault(store.StoreId) ?? Guid.NewGuid(),
                                ShippingPrice = 0m,
                                ShippingName = "Test shipping"
                            })
                            .ToList()
                    }));

        return shippingService.Object;
    }

    private async Task<Guid> SeedActiveVendorListingAsync(Guid vendorId, Guid productId, Guid variantId)
    {
        var commerceHubCategoryId = Guid.NewGuid();

        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Governed Vendor",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        _dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = vendorId,
            Name = "Governed Product",
            CategoryId = Guid.NewGuid(),
            SalePrice = 100m,
            OnlinePrice = 100m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published
        });

        _dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = vendorId,
            ProductId = productId,
            Price = 100m,
            OnlinePrice = 100m,
            CostPrice = 60m,
            IsDefault = true,
            IsActive = true
        });

        var listing = CommerceHubListing.Create(
            vendorId,
            productId,
            variantId,
            commerceHubCategoryId,
            null);
        _dbContext.CommerceHubListings.Add(listing);
        _dbContext.CommerceHubCategories.Add(CreateActiveCommerceHubCategory(commerceHubCategoryId));

        await _dbContext.SaveChangesAsync();
        return listing.Id;
    }

    private static CommerceHubCategory CreateActiveCommerceHubCategory(Guid categoryId) => new()
    {
        Id = categoryId,
        Name = "Checkout Test Category",
        Slug = $"checkout-test-{categoryId:N}",
        Level = 0,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private async Task SeedPaymentMethodAsync(
        Guid tenantId,
        string code,
        bool supportsOnline = false,
        bool supportsOffline = false)
    {
        _dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(tenantId, code, supportsOnline, supportsOffline));
        await _dbContext.SaveChangesAsync();
    }

    private static CommerceHubCheckoutRequestDto BuildCheckoutRequest(
        Guid vendorId,
        Guid productId,
        Guid variantId,
        Guid listingId,
        decimal? expectedUnitPrice = 100m,
        string paymentMethod = CommerceHubPaymentMethods.Cod)
    {
        return new CommerceHubCheckoutRequestDto
        {
            CartVersion = 1,
            Customer = new CommerceHubCustomerInfoDto
            {
                FullName = "Checkout Customer",
                PhoneNumber = "01000000000",
                AddressLine = "123 Test Street",
                City = "Cairo"
            },
            PaymentMethod = paymentMethod,
            ShippingTotal = 0m,
            Stores =
            {
                new CommerceHubCheckoutStoreRequestDto
                {
                    StoreId = vendorId,
                    Items =
                    {
                        new CommerceHubCheckoutItemRequestDto
                        {
                            ListingId = listingId,
                            ProductId = productId,
                            VariantId = variantId,
                            Quantity = 1,
                            ExpectedUnitPrice = expectedUnitPrice
                        }
                    }
                }
            },
            ShippingSelections =
            {
                new CommerceHubCheckoutShippingSelectionRequestDto
                {
                    StoreId = vendorId,
                    ShippingOptionId = Guid.NewGuid(),
                    ShippingPrice = 0m
                }
            }
        };
    }

    private async Task SeedAuthenticatedCustomerCartAsync(
        Guid customerId,
        Guid vendorId,
        Guid productId,
        Guid variantId,
        Guid listingId,
        decimal expectedUnitPrice = 100m)
    {
        var customer = CommerceHubCustomer.Create(
            $"{customerId:N}@example.test",
            "Checkout Customer",
            null,
            null);
        customer.Id = customerId;
        _dbContext.CommerceHubCustomers.Add(customer);

        _dbContext.CommerceHubCarts.Add(new CommerceHubCart
        {
            Id = Guid.NewGuid(),
            TenantId = CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId,
            CommerceHubCustomerId = customerId,
            OnlineCustomerId = customerId,
            Version = 1,
            ItemsJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    StoreId = vendorId,
                    ListingId = listingId,
                    ProductId = productId,
                    VariantId = variantId,
                    Quantity = 1,
                    PriceSnapshot = expectedUnitPrice
                }
            })
        });

        await _dbContext.SaveChangesAsync();
    }

    private static TenantPaymentMethod CreatePaymentMethod(
        Guid tenantId,
        string code,
        bool supportsOnline = false,
        bool supportsOffline = false)
    {
        return new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            DisplayName = code,
            IsEnabled = true,
            SupportsOnline = supportsOnline,
            SupportsOffline = supportsOffline
        };
    }
}
