using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Coupons;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class OrdersControllerFinancialIntegrityTests
{
    private static readonly JsonSerializerOptions ReplayJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateOrder_ReplaysStoredResponse_ForSameIdempotencyKeyAndPayload()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");
        var request = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);

        var firstResult = await harness.Controller.CreateOrder(request, CancellationToken.None);
        var secondResult = await harness.Controller.CreateOrder(request, CancellationToken.None);

        var firstCreated = Assert.IsType<CreatedAtActionResult>(firstResult.Result);
        var secondCreated = Assert.IsType<CreatedAtActionResult>(secondResult.Result);
        var first = Assert.IsType<OrderDetailDto>(firstCreated.Value);
        var second = Assert.IsType<OrderDetailDto>(secondCreated.Value);

        Assert.NotEqual(default(DateTime), first.CreatedAt);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(
            JsonSerializer.Serialize(first, ReplayJsonOptions),
            JsonSerializer.Serialize(second, ReplayJsonOptions));
        Assert.Equal(1, await harness.Context.Orders.CountAsync());
        Assert.Equal(1, await harness.Context.OrderRequestIdempotencyRecords.CountAsync());

        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(
                It.Is<OrderCreatedEvent>(evt =>
                    evt.TenantId == harness.TenantId &&
                    evt.OrderNumber.Length > 0 &&
                    evt.CorrelationId == harness.Controller.HttpContext.TraceIdentifier),
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrder_WhenReplayPayloadIsMissingAfterCommit_ReconstructsExistingOrder()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");
        var request = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);

        var firstResult = await harness.Controller.CreateOrder(request, CancellationToken.None);
        var firstCreated = Assert.IsType<CreatedAtActionResult>(firstResult.Result);
        var first = Assert.IsType<OrderDetailDto>(firstCreated.Value);

        var record = await harness.Context.OrderRequestIdempotencyRecords.SingleAsync();
        record.ResponseStatusCode = null;
        record.ResponsePayload = null;
        record.CompletedAtUtc ??= DateTime.UtcNow;
        await harness.Context.SaveChangesAsync();

        harness.Context.ChangeTracker.Clear();

        var secondResult = await harness.Controller.CreateOrder(request, CancellationToken.None);
        var secondCreated = Assert.IsType<CreatedAtActionResult>(secondResult.Result);
        var second = Assert.IsType<OrderDetailDto>(secondCreated.Value);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.OrderNumber, second.OrderNumber);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(1, await harness.Context.Orders.CountAsync());
        Assert.Equal(1, await harness.Context.OrderRequestIdempotencyRecords.CountAsync());

        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CleanupOrderIdempotencyAsync_WhenOrderBindingIsDurable_PreservesOrderAuthority()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");
        var request = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);

        var result = await harness.Controller.CreateOrder(request, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<OrderDetailDto>(created.Value);
        var record = await harness.Context.OrderRequestIdempotencyRecords.SingleAsync();

        await InvokeCleanupOrderIdempotencyAsync(harness.Controller, record);

        Assert.Equal(1, await harness.Context.Orders.CountAsync(order => order.Id == response.Id));
        Assert.Equal(1, await harness.Context.OrderRequestIdempotencyRecords.CountAsync(item => item.Id == record.Id));
    }

    [Fact]
    public async Task CreateOrder_RejectsConflictingPayloadReuse_ForSameIdempotencyKey()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        var firstRequest = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);

        var conflictingRequest = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);
        conflictingRequest.GuestName = "Different Guest";

        var firstResult = await harness.Controller.CreateOrder(firstRequest, CancellationToken.None);
        var secondResult = await harness.Controller.CreateOrder(conflictingRequest, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(firstResult.Result);
        var conflict = Assert.IsType<ConflictObjectResult>(secondResult.Result);
        Assert.Contains(
            "different order payload",
            JsonSerializer.Serialize(conflict.Value),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await harness.Context.Orders.CountAsync());
        Assert.Equal(1, await harness.Context.OrderRequestIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task CreateOrder_Cancellation_RollsBackOrderAndIdempotencyClaim()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");
        using var cancellationSource = new CancellationTokenSource();

        harness.MarketplaceOutbox
            .Setup(outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent, CancellationToken>((_, token) =>
            {
                cancellationSource.Cancel();
                return Task.FromCanceled(token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Controller.CreateOrder(
            BuildRequest(
                harness.Product,
                harness.ShippingMethod,
                unitPrice: 25m,
                subtotal: 50m,
                taxAmount: 7m,
                shippingCost: 9m),
            cancellationSource.Token));

        Assert.Equal(0, await harness.Context.Orders.CountAsync());
        Assert.Equal(0, await harness.Context.OrderRequestIdempotencyRecords.CountAsync());
        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrder_RejectsClientManipulatedFinancials()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        var result = await harness.Controller.CreateOrder(BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 1m,
            subtotal: 0m,
            taxAmount: 0m,
            shippingCost: 0m),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var payload = JsonSerializer.Serialize(badRequest.Value);

        Assert.Contains("Client-supplied financials do not match server-calculated values.", payload);
        Assert.Contains("\"subtotal\":50", payload, StringComparison.Ordinal);
        Assert.Contains("\"shippingCost\":9", payload, StringComparison.Ordinal);
        Assert.Equal(0, await harness.Context.Orders.CountAsync());
        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrder_RejectsCommerceSourcesBeforeIdempotencyClaim()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        var request = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);
        request.Source = OrderSource.Ecommerce;

        var result = await harness.Controller.CreateOrder(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var payload = JsonSerializer.Serialize(badRequest.Value);

        Assert.Contains("CommerceHub checkout pipeline", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await harness.Context.Orders.CountAsync());
        Assert.Equal(0, await harness.Context.OrderRequestIdempotencyRecords.CountAsync());
        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrder_RejectsMarketplaceSourceBeforeIdempotencyClaim()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        var request = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);
        request.Source = OrderSource.Marketplace;

        var result = await harness.Controller.CreateOrder(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var payload = JsonSerializer.Serialize(badRequest.Value);

        Assert.Contains("CommerceHub checkout pipeline", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await harness.Context.Orders.CountAsync());
        Assert.Equal(0, await harness.Context.OrderRequestIdempotencyRecords.CountAsync());
        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrder_WhenCurrencyIsMissing_ReturnsBadRequestAndDoesNotPersistOrder()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        var request = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);
        request.Currency = null;

        var result = await harness.Controller.CreateOrder(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var payload = JsonSerializer.Serialize(badRequest.Value);

        Assert.Contains("Order currency is required", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await harness.Context.Orders.CountAsync());
        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrder_WhenCurrencyDoesNotMatchTenant_ReturnsBadRequestAndDoesNotPersistOrder()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        var request = BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m);
        request.Currency = "EUR";

        var result = await harness.Controller.CreateOrder(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var payload = JsonSerializer.Serialize(badRequest.Value);

        Assert.Contains("must match the tenant currency", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await harness.Context.Orders.CountAsync());
        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrder_PersistsServerCalculatedTotalsForValidOrder()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        var result = await harness.Controller.CreateOrder(BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<OrderDetailDto>(created.Value);

        Assert.Equal(50m, response.Subtotal);
        Assert.Equal(7m, response.TaxAmount);
        Assert.Equal(9m, response.ShippingCost);
        Assert.Equal(66m, response.Total);

        var storedOrder = await harness.Context.Orders
            .Include(order => order.Items)
            .SingleAsync();

        Assert.Equal(50m, storedOrder.Subtotal);
        Assert.Equal(7m, storedOrder.TaxAmount);
        Assert.Equal(9m, storedOrder.ShippingCost);
        Assert.Equal(66m, storedOrder.Total);
        Assert.Equal(0m, storedOrder.DiscountAmount);

        var storedItem = Assert.Single(storedOrder.Items);
        Assert.Equal(25m, storedItem.UnitPrice);
        Assert.Equal(7m, storedItem.TaxAmount);
        Assert.Equal(57m, storedItem.Total);

        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(
                It.Is<OrderCreatedEvent>(evt =>
                    evt.TenantId == harness.TenantId &&
                    evt.IdempotencyKey != null &&
                    evt.IdempotencyKey.Contains("orders:create:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrder_WhenOrderCreatedOutboxPublishFails_RollsBackOrderAndIdempotencyClaim()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");

        harness.EventPublisher
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));

        var result = await harness.Controller.CreateOrder(BuildRequest(
            harness.Product,
            harness.ShippingMethod,
            unitPrice: 25m,
            subtotal: 50m,
            taxAmount: 7m,
            shippingCost: 9m),
            CancellationToken.None);

        var failure = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
        Assert.Equal(0, await harness.Context.Orders.CountAsync());
        Assert.Equal(0, await harness.Context.OrderRequestIdempotencyRecords.CountAsync());
        
        harness.EventPublisher.Verify(
            endpoint => endpoint.PublishAsync(
                It.IsAny<OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.MarketplaceOutbox.Verify(
            outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static CreateOrderDto BuildRequest(
        Product product,
        ShippingMethod shippingMethod,
        decimal unitPrice,
        decimal subtotal,
        decimal taxAmount,
        decimal shippingCost)
    {
        return new CreateOrderDto
        {
            GuestName = "Guest Customer",
            GuestEmail = "guest@example.com",
            GuestPhone = "201001234567",
            ShippingAddress = "123 Market Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            ShippingMethodId = shippingMethod.Id,
            PaymentMethod = PaymentMethod.Cash,
            Currency = "USD",
            Subtotal = subtotal,
            DiscountAmount = 0m,
            TaxAmount = taxAmount,
            ShippingCost = shippingCost,
            Items =
            [
                new CreateOrderItemDto
                {
                    ProductId = product.Id,
                    Quantity = 2,
                    UnitPrice = unitPrice,
                    DiscountAmount = 0m,
                    TaxAmount = taxAmount
                }
            ]
        };
    }

    private static async Task<OrdersControllerHarness> CreateHarnessAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options, tenantContext);

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Currency = "USD",
            IsActive = true,
            Status = TenantStatus.Active,
            Slug = $"tenant-{tenantId:N}"
        };

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Authoritative Product",
            SKU = "AUTH-001",
            CategoryId = Guid.NewGuid(),
            PurchasePrice = 10m,
            SalePrice = 20m,
            OnlinePrice = 25m,
            TaxRate = 14m,
            PriceIncludesTax = false,
            Weight = 1m
        };

        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "AUTH-001-DEFAULT",
            Price = 20m,
            OnlinePrice = 25m,
            CostPrice = 10m,
            IsDefault = true,
            IsActive = true
        };

        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true
        };

        var productUnit = new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UnitId = unit.Id,
            ConversionFactor = 1m,
            SellingPrice = 25m,
            PurchasePrice = 10m,
            IsDefault = true,
            IsBaseUnit = true
        };

        var shippingMethod = new ShippingMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Standard Shipping",
            BaseRate = 5m,
            PerKgRate = 2m,
            IsActive = true
        };

        context.Tenants.Add(tenant);
        context.Products.Add(product);
        context.ProductVariants.Add(variant);
        context.Units.Add(unit);
        context.ProductUnits.Add(productUnit);
        context.ShippingMethods.Add(shippingMethod);
        await context.SaveChangesAsync();

        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();
        marketplaceOutbox
            .Setup(outbox => outbox.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.OrderCreatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orderAttributionStore = new Mock<IOrderAttributionStore>();
        orderAttributionStore
            .Setup(store => store.GetAttributionAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<AttributionModel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderAttribution?)null);

        var phoneRiskProfileService = new Mock<IPhoneRiskProfileService>();
        phoneRiskProfileService
            .Setup(service => service.NormalizePhoneAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string phoneNumber, CancellationToken _) => phoneNumber);
        phoneRiskProfileService
            .Setup(service => service.GetStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PointOfSale.API.DTOs.Admin.AdminPhoneRiskStatusDto?)null);
        phoneRiskProfileService
            .Setup(service => service.GetGlobalSpamReportsAsync(
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        phoneRiskProfileService
            .Setup(service => service.HasReportedSpamAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var orderVelocityService = new Mock<IOrderVelocityService>();
        orderVelocityService
            .Setup(service => service.GetCurrentAssessmentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderVelocityAssessment());

        var couponValidationService = new Mock<ICouponValidationService>();
        var stockRepository = new Mock<IProductVariantStockRepository>();
        stockRepository
            .Setup(repository => repository.ReserveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<decimal>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        stockRepository
            .Setup(repository => repository.BindOrderReservationsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<List<OrderItem>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = new OrdersController(
            context,
            tenantContext,
            eventPublisher.Object,
            marketplaceOutbox.Object,
            orderAttributionStore.Object,
            phoneRiskProfileService.Object,
            orderVelocityService.Object,
            Mock.Of<IRefundEvidenceService>(),
            couponValidationService.Object,
            new ShippingCalculationService(),
            stockRepository.Object,
            NullLogger<OrdersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = Guid.NewGuid().ToString("N")
                }
            }
        };

        return new OrdersControllerHarness(
            context,
            controller,
            product,
            shippingMethod,
            tenantId,
            eventPublisher,
            marketplaceOutbox);
    }

    private static async Task InvokeCleanupOrderIdempotencyAsync(
        OrdersController controller,
        OrderRequestIdempotency record)
    {
        var method = typeof(OrdersController).GetMethod(
            "CleanupOrderIdempotencyAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(controller, [record, CancellationToken.None])!;
        await task;
    }

    private sealed record OrdersControllerHarness(
        ApplicationDbContext Context,
        OrdersController Controller,
        Product Product,
        ShippingMethod ShippingMethod,
        Guid TenantId,
        Mock<IEventPublisher> EventPublisher,
        Mock<IMarketplaceEventOutboxService> MarketplaceOutbox) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
