using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class PurchasePaymentIntegrityIntegrationTests
{
    [Fact]
    public async Task CreatePurchase_RejectsOverpayment_AndDoesNotPersistInvoice()
    {
        var setup = await SetupPurchaseTestAsync("purchase-overpayment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 120m,
                unitCost: 100m));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(
            "Paid amount cannot exceed total amount.",
            JsonSerializer.Serialize(badRequest.Value),
            StringComparison.Ordinal);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(0, await verifyContext.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task CreatePurchase_AllowsExactPayment()
    {
        var setup = await SetupPurchaseTestAsync("purchase-exact-payment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100m,
                unitCost: 100m));

        var ok = AssertObjectResult(result, StatusCodes.Status200OK);
        var response = ReadPurchaseResponse(ok);
        Assert.Equal(100m, response.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var invoice = await verifyContext.PurchaseInvoices.SingleAsync();
        Assert.Equal(100m, invoice.TotalAmount);
        Assert.Equal(100m, invoice.PaidAmount);
    }

    [Fact]
    public async Task CreatePurchase_AllowsPartialPayment()
    {
        var setup = await SetupPurchaseTestAsync("purchase-partial-payment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Credit,
                paidAmount: 60m,
                unitCost: 100m,
                initialPaymentMethod: PaymentMethod.Card));

        var ok = AssertObjectResult(result, StatusCodes.Status200OK);
        var response = ReadPurchaseResponse(ok);
        Assert.Equal(100m, response.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var invoice = await verifyContext.PurchaseInvoices.SingleAsync();
        Assert.Equal(100m, invoice.TotalAmount);
        Assert.Equal(60m, invoice.PaidAmount);
    }

    [Fact]
    public async Task CreatePurchase_AllowsRoundedExactPayment()
    {
        var setup = await SetupPurchaseTestAsync("purchase-rounding-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100.01m,
                unitCost: 100.005m));

        var ok = AssertObjectResult(result, StatusCodes.Status200OK);
        var response = ReadPurchaseResponse(ok);
        Assert.Equal(100.01m, response.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var invoice = await verifyContext.PurchaseInvoices.SingleAsync();
        Assert.Equal(100.01m, invoice.TotalAmount);
        Assert.Equal(100.01m, invoice.PaidAmount);
    }

    [Fact]
    public async Task CreatePurchase_ReplayingSameIdempotencyKey_DoesNotCreateDuplicatePersistence()
    {
        var setup = await SetupPurchaseTestAsync("purchase-idempotent-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var firstController = setup.Controller;
        var firstResult = await firstController.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100m,
                unitCost: 100m));

        var firstResponse = ReadPurchaseResponse(firstResult);

        await using var replayContext = scope.CreateDbContext(tenantContext);
        var replayController = CreateController(replayContext, tenantContext);
        replayController.Request.Headers["Idempotency-Key"] = setup.IdempotencyKey;

        var replayResult = await replayController.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100m,
                unitCost: 100m));

        var replayObjectResult = Assert.IsAssignableFrom<ObjectResult>(replayResult.Result);
        Assert.NotNull(replayObjectResult.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, replayObjectResult.StatusCode.Value);

        var replayResponse = ReadPurchaseResponse(replayResult);
        Assert.Equal(firstResponse.InvoiceId, replayResponse.InvoiceId);
        Assert.Equal(firstResponse.InvoiceNumber, replayResponse.InvoiceNumber);
        Assert.Equal(firstResponse.TotalAmount, replayResponse.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(1, await verifyContext.PurchaseInvoices.CountAsync());
        Assert.Equal(1, await verifyContext.PurchaseInvoiceItems.CountAsync());
        Assert.Equal(1, await verifyContext.PurchaseRequestIdempotencyRecords.CountAsync());

        var stock = await verifyContext.ProductVariantStocks.SingleAsync();
        Assert.Equal(1m, stock.Quantity);
    }

    private static async Task<PurchaseTestSetup> SetupPurchaseTestAsync(string idempotencyKey)
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            Assert.Fail("Failed to create test database scope. Ensure PostgreSQL is available and configured correctly.");
            return null!;
        }

        await using var seedContext = databaseScope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId);

        var actionContext = databaseScope.CreateDbContext(tenantContext);
        var controller = CreateController(actionContext, tenantContext);
        var effectiveIdempotencyKey = $"{idempotencyKey}-{Guid.NewGuid():N}";
        controller.Request.Headers["Idempotency-Key"] = effectiveIdempotencyKey;

        return new PurchaseTestSetup(databaseScope, actionContext, controller, seeded, tenantContext, effectiveIdempotencyKey);
    }

    private static PurchasesController CreateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext)
    {
        var ledgerService = new Mock<ILedgerService>();
        ledgerService
            .Setup(service => service.ApplySupplierTransaction(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<TransactionType>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>()))
            .ReturnsAsync(0m);

        var auditService = new Mock<IAuditService>();
        auditService.SetReturnsDefault(Task.CompletedTask);

        var outbox = new Mock<IAccountingOutboxService>();
        outbox.SetReturnsDefault(Task.CompletedTask);
        outbox
            .Setup(service => service.EnqueuePosPurchaseAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, int, decimal, decimal, CancellationToken>(async (tenantId, invoiceId, mutationVersion, cashAmount, cardAmount, cancellationToken) =>
            {
                context.AccountingOutboxMessages.Add(new PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EventType = "PosPurchase",
                    SourceType = "PosPurchase",
                    BusinessEntityId = invoiceId,
                    PayloadJson = "{}",
                    IdempotencyKey = BuildPurchaseAccountingOutboxIdempotencyKey(
                        tenantId,
                        invoiceId,
                        cashAmount,
                        cardAmount,
                        mutationVersion),
                    AggregateVersion = mutationVersion,
                    OccurredAtUtc = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                });
                await context.SaveChangesAsync(cancellationToken);
            });

        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostPosPurchaseAsync(
                It.IsAny<PurchaseInvoice>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<decimal?>(),
                It.IsAny<decimal?>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "JRN-PUR-TEST"));

        return new PurchasesController(
            context,
            tenantContext,
            NullLogger<PurchasesController>.Instance,
            ledgerService.Object,
            Mock.Of<IPartyBalanceMaintenanceService>(),
            Mock.Of<IValidationService>(),
            auditService.Object,
            new NoOpEventPublisher(),
            outbox.Object,
            commerceAccounting.Object,
            Mock.Of<IAccountResolver>(),
            Mock.Of<ITreasuryReadService>(),
            Mock.Of<ICashboxLedgerAccountService>(),
            Mock.Of<IPurchaseFinancialIntegrityService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static string BuildPurchaseAccountingOutboxIdempotencyKey(
        Guid tenantId,
        Guid invoiceId,
        decimal cashAmount,
        decimal cardAmount,
        int mutationVersion) =>
        $"PURCHASE:{tenantId}:{invoiceId}:C{cashAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:D{cardAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:V{mutationVersion}";

    private static CreatePurchaseRequest BuildRequest(
        SeededPurchaseScenario seeded,
        PaymentMethod paymentMethod,
        decimal paidAmount,
        decimal unitCost,
        PaymentMethod? initialPaymentMethod = null)
    {
        return new CreatePurchaseRequest
        {
            SupplierId = seeded.SupplierId,
            WarehouseId = seeded.WarehouseId,
            InvoiceDate = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            PaymentMethod = paymentMethod,
            InitialPaymentMethod = initialPaymentMethod,
            PaidAmount = paidAmount,
            Items =
            [
                new PurchaseItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    Quantity = 1m,
                    UnitCost = unitCost,
                    TaxRate = 0m
                }
            ]
        };
    }

    private static async Task<SeededPurchaseScenario> SeedScenarioAsync(
        ApplicationDbContext context,
        Guid tenantId)
    {
        var utcNow = DateTime.UtcNow;
        var categoryId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Purchase Payment Integrity Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Purchase Tests",
            CreatedAt = utcNow
        });

        context.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Primary Supplier",
            Balance = 0m,
            CreatedAt = utcNow
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main Warehouse",
            Code = "WH-PUR",
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Purchase Test Product",
            CategoryId = categoryId,
            SalePrice = 150m,
            OnlinePrice = 150m,
            PurchasePrice = 80m,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "PUR-TEST-001",
            Price = 150m,
            OnlinePrice = 150m,
            CostPrice = 80m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = 0m,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = utcNow
        });

        await context.SaveChangesAsync();

        return new SeededPurchaseScenario(supplierId, warehouseId, productId, variantId);
    }

    private static PurchaseResponse ReadPurchaseResponse(ActionResult<PurchaseResponse> result)
    {
        if (result.Result is ObjectResult objectResult)
        {
            return ReadPurchaseResponse(objectResult);
        }

        Assert.Fail($"Expected an object result carrying a purchase response, but received '{result.Result?.GetType().Name ?? "null"}'.");
        return null!;
    }

    private static ObjectResult AssertObjectResult(ActionResult<PurchaseResponse> result, int expectedStatusCode)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode ?? StatusCodes.Status200OK);
        return objectResult;
    }

    private static PurchaseResponse ReadPurchaseResponse(ObjectResult result)
    {
        if (result.Value is PurchaseResponse response)
        {
            return response;
        }

        if (result.Value is JsonElement payload)
        {
            var deserialized = payload.Deserialize<PurchaseResponse>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(deserialized);
            return deserialized!;
        }

        var serialized = JsonSerializer.Serialize(result.Value);
        var fallback = JsonSerializer.Deserialize<PurchaseResponse>(serialized, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(fallback);
        return fallback!;
    }

    private sealed record PurchaseTestSetup(
        TestDatabaseScope Scope,
        ApplicationDbContext ActionContext,
        PurchasesController Controller,
        SeededPurchaseScenario Seeded,
        TestTenantContext TenantContext,
        string IdempotencyKey);

    private sealed record SeededPurchaseScenario(
        Guid SupplierId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId);

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            Task.CompletedTask;
    }
}
