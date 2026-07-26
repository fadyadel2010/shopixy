using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using UnitEntity = PointOfSale.Core.Entities.Unit;

namespace PointOfSale.Tests.Integration;

public sealed class PurchaseReturnsConcurrencyIntegrationTests
{
    [Fact]
    public async Task PurchaseReturns_Concurrent_Returns_Are_Serialized_And_Deterministic()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using var seedContext = scope.CreateDbContext(tenantContext);
        await EnsurePurchaseReturnConcurrencySchemaAsync(seedContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId, [2m, 3m]);

        var progressEntries = new ConcurrentQueue<string>();
        using var startBarrier = new Barrier(participantCount: 3);

        var taskA = ExecuteReturnAsync(
            scope,
            tenantContext,
            seeded,
            seeded.Invoices[0],
            returnQuantity: 2m,
            idempotencyKey: "purchase-return-concurrency-a",
            userId: Guid.Parse("30000000-0000-0000-0000-000000000001"),
            progressEntries,
            startBarrier);

        var taskB = ExecuteReturnAsync(
            scope,
            tenantContext,
            seeded,
            seeded.Invoices[1],
            returnQuantity: 3m,
            idempotencyKey: "purchase-return-concurrency-b",
            userId: Guid.Parse("30000000-0000-0000-0000-000000000002"),
            progressEntries,
            startBarrier);

        startBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
        var results = await AwaitAllOrThrowAsync(
            [taskA, taskB],
            progressEntries,
            TimeSpan.FromSeconds(90));

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var finalStock = await verifyContext.ProductVariantStocks.SingleAsync(stock =>
            stock.WarehouseId == seeded.WarehouseId &&
            stock.VariantId == seeded.VariantId);

        Assert.Equal(seeded.InitialStockQuantity - 5m, finalStock.Quantity);

        var returnIds = results.Select(result => result.ReturnId).ToList();
        var movements = await verifyContext.StockMovements
            .Where(movement =>
                movement.ProductVariantId == seeded.VariantId &&
                movement.MovementType == StockMovementType.PurchaseReturn &&
                movement.ReferenceId.HasValue &&
                returnIds.Contains(movement.ReferenceId.Value))
            .OrderByDescending(movement => movement.QuantityAfter)
            .ThenBy(movement => movement.Id)
            .ToListAsync();

        Assert.Equal(2, movements.Count);
        Assert.Equal(seeded.InitialStockQuantity + movements[0].QuantityChange, movements[0].QuantityAfter);
        Assert.Equal(movements[0].QuantityAfter + movements[1].QuantityChange, movements[1].QuantityAfter);
        Assert.Equal(finalStock.Quantity, movements[1].QuantityAfter);

        var validSequence =
            (movements[0].QuantityChange, movements[0].QuantityAfter, movements[1].QuantityChange, movements[1].QuantityAfter) switch
            {
                (-2m, 13m, -3m, 10m) => true,
                (-3m, 12m, -2m, 10m) => true,
                _ => false
            };

        Assert.True(
            validSequence,
            $"Unexpected QuantityAfter sequence: [{string.Join(", ", movements.Select(m => $"{m.QuantityChange}->{m.QuantityAfter}"))}]");

        var outboxMessages = await verifyContext.StockSyncOutboxMessages
            .Where(message => message.TenantId == tenantId && message.VariantId == seeded.VariantId)
            .ToListAsync();

        Assert.Equal(2, outboxMessages.Count);
        Assert.All(outboxMessages, message =>
        {
            Assert.Equal(seeded.ProductId, message.ProductId);
            Assert.Equal(nameof(ProductVariantStockChangedEvent), message.EventType);
            Assert.Null(message.PublishedAtUtc);
            Assert.False(message.IsFailed);
        });
    }

    private static async Task<ReturnExecutionResult> ExecuteReturnAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SeededInvoice invoice,
        decimal returnQuantity,
        string idempotencyKey,
        Guid userId,
        ConcurrentQueue<string> progressEntries,
        Barrier startBarrier)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        await ConfigureSessionTimeoutsAsync(context);

        var controller = new PurchaseReturnsController(
            context,
            tenantContext,
            NullLogger<PurchaseReturnsController>.Instance,
            CreateLedgerServiceMock().Object,
            Mock.Of<IEventPublisher>(),
            CreateAccountingOutboxMock().Object,
            CreateCommerceAccountingMock().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(userId)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var request = new CreatePurchaseReturnRequest
        {
            SupplierId = seeded.SupplierId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = DateTime.UtcNow,
            OriginalInvoiceId = invoice.InvoiceId,
            RefundAmount = invoice.UnitPrice * returnQuantity,
            Reason = "Concurrency validation",
            Notes = $"Invoice {invoice.InvoiceNumber}",
            Items =
            [
                new PurchaseReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = returnQuantity,
                    UnitPrice = invoice.UnitPrice,
                    TaxRate = 0m,
                    Reason = "Concurrent return"
                }
            ]
        };

        progressEntries.Enqueue($"{idempotencyKey}: request-ready");
        startBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
        progressEntries.Enqueue($"{idempotencyKey}: invoking-controller");

        var actionResult = await controller.CreatePurchaseReturn(request);
        var okResult = Assert.IsAssignableFrom<ObjectResult>(actionResult.Result);
        Assert.True(
            okResult.StatusCode is null or StatusCodes.Status200OK,
            $"Expected purchase return creation to return HTTP 200, got {okResult.StatusCode}.");
        var response = Assert.IsType<PurchaseReturnResponse>(okResult.Value);

        progressEntries.Enqueue($"{idempotencyKey}: completed:{response.ReturnId}");
        return new ReturnExecutionResult(response.ReturnId);
    }

    private static async Task<SeededScenario> SeedScenarioAsync(
        ApplicationDbContext context,
        Guid tenantId,
        IReadOnlyList<decimal> invoiceQuantities)
    {
        var utcNow = DateTime.UtcNow;
        var categoryId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        const decimal initialStockQuantity = 15m;
        const decimal unitPrice = 6m;

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Purchase Returns Concurrency Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Integration Tests",
            CreatedAt = utcNow
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Primary Warehouse",
            Code = "WH-PUR-1",
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Concurrency Supplier",
            Balance = 0m,
            CreatedAt = utcNow
        });

        context.Units.Add(new UnitEntity
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
            Name = "Purchase Return Product",
            CategoryId = categoryId,
            PurchasePrice = unitPrice,
            SalePrice = unitPrice * 2m,
            OnlinePrice = unitPrice * 2m,
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
            SKU = "PUR-RET-CONC-001",
            Price = unitPrice * 2m,
            OnlinePrice = unitPrice * 2m,
            CostPrice = unitPrice,
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
            SellingPrice = unitPrice * 2m,
            PurchasePrice = unitPrice,
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

        var invoices = new List<SeededInvoice>();
        for (var index = 0; index < invoiceQuantities.Count; index++)
        {
            var quantity = invoiceQuantities[index];
            var invoiceId = Guid.NewGuid();
            var invoiceNumber = $"PUR-RET-TEST-{index + 1:D2}";
            var invoiceDate = utcNow.AddMinutes(-(index + 1));
            var totalAmount = quantity * unitPrice;

            var invoice = new PurchaseInvoice
            {
                Id = invoiceId,
                TenantId = tenantId,
                InvoiceNumber = invoiceNumber,
                InvoiceDate = invoiceDate,
                SupplierId = supplierId,
                WarehouseId = warehouseId,
                Status = InvoiceStatus.Completed,
                Subtotal = totalAmount,
                DiscountAmount = 0m,
                DiscountPercentage = 0m,
                TaxAmount = 0m,
                TotalAmount = totalAmount,
                PaidAmount = totalAmount,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAt = invoiceDate
            };

            invoice.Items.Add(new PurchaseInvoiceItem
            {
                Id = Guid.NewGuid(),
                PurchaseInvoiceId = invoiceId,
                ProductId = productId,
                ProductVariantId = variantId,
                UnitId = unitId,
                Quantity = quantity,
                ConversionFactor = 1m,
                BaseQuantity = quantity,
                UnitPrice = unitPrice,
                DiscountAmount = 0m,
                TaxRate = 0m,
                TaxAmount = 0m,
                TotalAmount = totalAmount,
                CreatedAt = invoiceDate
            });

            context.PurchaseInvoices.Add(invoice);
            invoices.Add(new SeededInvoice(invoiceId, invoiceNumber, unitPrice));
        }

        await context.SaveChangesAsync();

        return new SeededScenario(
            warehouseId,
            supplierId,
            productId,
            variantId,
            unitId,
            initialStockQuantity,
            invoices);
    }

    private static DefaultHttpContext BuildHttpContext(Guid userId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, $"tester-{userId:N}")
        ], "TestAuth");

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }

    private static Mock<ILedgerService> CreateLedgerServiceMock()
    {
        var mock = new Mock<ILedgerService>(MockBehavior.Strict);
        mock.Setup(service => service.ApplySupplierTransaction(
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
        return mock;
    }

    private static Mock<IAccountingOutboxService> CreateAccountingOutboxMock()
    {
        var mock = new Mock<IAccountingOutboxService>(MockBehavior.Strict);
        mock.Setup(service => service.EnqueuePurchaseReturnAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<ICommerceAccountingService> CreateCommerceAccountingMock()
    {
        var mock = new Mock<ICommerceAccountingService>(MockBehavior.Strict);
        mock.Setup(service => service.PostPurchaseReturnAsync(
                It.IsAny<Return>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "PR-CONC-MOCK"));
        return mock;
    }

    private static async Task ConfigureSessionTimeoutsAsync(ApplicationDbContext context)
    {
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET statement_timeout = '30s'; SET lock_timeout = '30s';");
    }

    private static async Task EnsurePurchaseReturnConcurrencySchemaAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "PurchaseInvoices"
                ADD COLUMN IF NOT EXISTS "AccountingMutationVersion" integer NOT NULL DEFAULT 0;
            """);
    }

    private static async Task<T[]> AwaitAllOrThrowAsync<T>(
        IReadOnlyCollection<Task<T>> tasks,
        ConcurrentQueue<string> progressEntries,
        TimeSpan timeout)
    {
        try
        {
            return await Task.WhenAll(tasks).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"Purchase return concurrency execution exceeded {timeout.TotalSeconds:F0}s. Progress: {string.Join(" | ", progressEntries)}");
            return Array.Empty<T>();
        }
    }

    private sealed record SeededScenario(
        Guid WarehouseId,
        Guid SupplierId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId,
        decimal InitialStockQuantity,
        IReadOnlyList<SeededInvoice> Invoices);

    private sealed record SeededInvoice(
        Guid InvoiceId,
        string InvoiceNumber,
        decimal UnitPrice);

    private sealed record ReturnExecutionResult(Guid ReturnId);

}
