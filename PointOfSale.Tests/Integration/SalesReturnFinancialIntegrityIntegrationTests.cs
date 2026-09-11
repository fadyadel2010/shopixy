using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class SalesReturnFinancialIntegrityIntegrationTests
{
    [Fact]
    public void SalesReturnResponse_SerializesRefundMethodAsString_ForOriginalAndReplayParity()
    {
        var json = JsonSerializer.Serialize(new SalesReturnResponse
        {
            ReturnId = Guid.NewGuid(),
            RefundMethod = RefundMethod.Card
        });

        using var document = JsonDocument.Parse(json);
        Assert.Equal("Card", document.RootElement.GetProperty("RefundMethod").GetString());
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, 80, 100, 0, 80, 0)]
    [InlineData(PaymentMethod.Card, 80, 0, 100, 0, 80)]
    [InlineData(PaymentMethod.Mixed, 100, 60, 50, 50, 50)]
    [InlineData(PaymentMethod.Mixed, 100, 40, 50, 40, 50)]
    public async Task GetSalesInvoiceForReturn_CapsTenderCapacityToCollectedAmount(
        PaymentMethod paymentMethod,
        decimal paidAmount,
        decimal cashAmount,
        decimal cardAmount,
        decimal expectedCashCapacity,
        decimal expectedCardCapacity)
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using (var seedContext = scope.CreateDbContext(tenantContext))
        {
            var seeded = await SeedScenarioAsync(seedContext, tenantId);
            var invoice = await seedContext.SalesInvoices.SingleAsync(candidate => candidate.Id == seeded.InvoiceId);
            invoice.PaymentMethod = paymentMethod;
            invoice.PaidAmount = paidAmount;
            invoice.CashAmount = cashAmount;
            invoice.CardAmount = cardAmount;
            await seedContext.SaveChangesAsync();

            await using var actionContext = scope.CreateDbContext(tenantContext);
            var controller = new SalesReturnsController(
                actionContext,
                tenantContext,
                NullLogger<SalesReturnsController>.Instance,
                Mock.Of<ILedgerService>(),
                new NoOpEventPublisher(),
                Mock.Of<IAccountingOutboxService>(),
                Mock.Of<ICommerceAccountingService>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            var result = await controller.GetSalesInvoiceForReturn(seeded.InvoiceId, CancellationToken.None);
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<SalesReturnInvoiceResponseDto>(okResult.Value);

            Assert.Equal(expectedCashCapacity, response.RemainingCashRefundableAmount);
            Assert.Equal(expectedCardCapacity, response.RemainingCardRefundableAmount);
            Assert.False(response.HasLegacySettlementGap);
        }
    }

    [Fact]
    public async Task CreateSalesReturn_RejectsRefundOverAllocation_AndDoesNotPersistFinancialEffects()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId);

        var ledgerService = new Mock<ILedgerService>(MockBehavior.Strict);
        var accountingOutbox = new Mock<IAccountingOutboxService>(MockBehavior.Strict);

        await using var actionContext = scope.CreateDbContext(tenantContext);
        var controller = new SalesReturnsController(
            actionContext,
            tenantContext,
            NullLogger<SalesReturnsController>.Instance,
            ledgerService.Object,
            new NoOpEventPublisher(),
            accountingOutbox.Object,
            Mock.Of<ICommerceAccountingService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = $"sales-return-over-allocation-{Guid.NewGuid():N}";

        var result = await controller.CreateSalesReturn(new CreateSalesReturnRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            OriginalInvoiceId = seeded.InvoiceId,
            RefundMethod = RefundMethod.Cash,
            CashAmount = 120m,
            CardAmount = 0m,
            Reason = "Refund over-allocation validation",
            Items =
            [
                new SalesReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = 1m,
                    UnitPrice = 100m,
                    TaxRate = 0m,
                    Reason = "Return full line"
                }
            ]
        });

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode ?? StatusCodes.Status400BadRequest);
        Assert.Contains(
            "Refund amount cannot exceed the original invoice total",
            JsonSerializer.Serialize(badRequest.Value),
            StringComparison.Ordinal);

        ledgerService.Verify(service => service.CreateLedgerEntry(
            It.IsAny<PartyType>(),
            It.IsAny<Guid>(),
            It.IsAny<DateTime>(),
            It.IsAny<TransactionType>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>()), Times.Never);
        accountingOutbox.Verify(service => service.EnqueuePosRefundAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<RefundMethod>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(0, await verifyContext.Returns.CountAsync());
        Assert.Equal(0, await verifyContext.ReturnItems.CountAsync());
        Assert.Equal(0, await verifyContext.CustomerTransactions.CountAsync());

        var stock = await verifyContext.ProductVariantStocks.SingleAsync(stock =>
            stock.WarehouseId == seeded.WarehouseId &&
            stock.VariantId == seeded.VariantId);
        Assert.Equal(seeded.InitialStockQuantity, stock.Quantity);
    }

    private static async Task<SeededScenario> SeedScenarioAsync(ApplicationDbContext context, Guid tenantId)
    {
        var utcNow = DateTime.UtcNow;
        var categoryId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        const decimal initialStockQuantity = 5m;
        const decimal unitPrice = 100m;

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Sales Return Financial Integrity Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Refund Tests",
            CreatedAt = utcNow
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Refund Warehouse",
            Code = "WH-RET",
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            Name = "Refund Customer",
            Balance = 0m,
            CreatedAt = utcNow
        });

        context.Units.Add(new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Refund Product",
            CategoryId = categoryId,
            PurchasePrice = 60m,
            SalePrice = unitPrice,
            OnlinePrice = unitPrice,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = true,
            CreatedAt = utcNow
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "RET-INT-001",
            Price = unitPrice,
            OnlinePrice = unitPrice,
            CostPrice = 60m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            ConversionFactor = 1m,
            SellingPrice = unitPrice,
            PurchasePrice = 60m,
            IsDefault = true,
            IsBaseUnit = true,
            CreatedAt = utcNow
        });

        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = initialStockQuantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = utcNow
        });

        var invoice = new SalesInvoice
        {
            Id = invoiceId,
            TenantId = tenantId,
            InvoiceNumber = "INV-RET-FIN-001",
            InvoiceDate = utcNow,
            CustomerId = customerId,
            WarehouseId = warehouseId,
            Status = InvoiceStatus.Completed,
            Subtotal = unitPrice,
            DiscountAmount = 0m,
            DiscountPercentage = 0m,
            TaxAmount = 0m,
            TotalAmount = unitPrice,
            PaidAmount = unitPrice,
            PaymentMethod = PaymentMethod.Cash,
            CashAmount = unitPrice,
            CardAmount = 0m,
            CreatedAt = utcNow
        };

        invoice.Items.Add(new SalesInvoiceItem
        {
            Id = Guid.NewGuid(),
            SalesInvoiceId = invoiceId,
            ProductId = productId,
            ProductVariantId = variantId,
            UnitId = unitId,
            Quantity = 1m,
            ConversionFactor = 1m,
            BaseQuantity = 1m,
            UnitPrice = unitPrice,
            DiscountAmount = 0m,
            TaxRate = 0m,
            TaxAmount = 0m,
            TotalAmount = unitPrice,
            CreatedAt = utcNow
        });

        context.SalesInvoices.Add(invoice);
        await context.SaveChangesAsync();

        return new SeededScenario(
            InvoiceId: invoiceId,
            CustomerId: customerId,
            WarehouseId: warehouseId,
            ProductId: productId,
            VariantId: variantId,
            UnitId: unitId,
            InitialStockQuantity: initialStockQuantity);
    }

    private sealed record SeededScenario(
        Guid InvoiceId,
        Guid CustomerId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId,
        decimal InitialStockQuantity);

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
