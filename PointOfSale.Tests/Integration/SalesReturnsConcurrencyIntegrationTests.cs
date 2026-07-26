using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Sdk;
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

public sealed class SalesReturnsConcurrencyIntegrationTests
{
    [Fact]
    public async Task SalesReturns_Concurrent_Returns_Are_Serialized_And_Deterministic()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId, [2m, 3m]);

        var logEntries = new ConcurrentQueue<LogEntry>();
        var progressEntries = new ConcurrentQueue<string>();
        using var startBarrier = new Barrier(participantCount: 3);

        var taskA = ExecuteReturnAsync(
            scope,
            tenantContext,
            tenantId,
            seeded,
            seeded.Invoices[0],
            refundQuantity: 2m,
            idempotencyKey: "sales-return-concurrency-a",
            userId: Guid.Parse("10000000-0000-0000-0000-000000000001"),
            logEntries,
            progressEntries,
            startBarrier);

        var taskB = ExecuteReturnAsync(
            scope,
            tenantContext,
            tenantId,
            seeded,
            seeded.Invoices[1],
            refundQuantity: 3m,
            idempotencyKey: "sales-return-concurrency-b",
            userId: Guid.Parse("10000000-0000-0000-0000-000000000002"),
            logEntries,
            progressEntries,
            startBarrier);

        var barrierTimeout = TimeSpan.FromSeconds(10);
        if (!startBarrier.SignalAndWait(barrierTimeout))
        {
            throw new TimeoutException(
                $"Start barrier timed out after {barrierTimeout.TotalSeconds}s. " +
                $"One or more workers failed to reach the barrier. Progress: {string.Join(", ", progressEntries)}");
        }
        
        var results = await AwaitAllOrThrowAsync(
            [taskA, taskB],
            progressEntries,
            TimeSpan.FromSeconds(90));

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var finalStock = await verifyContext.ProductVariantStocks.SingleAsync(stock =>
            stock.WarehouseId == seeded.WarehouseId &&
            stock.VariantId == seeded.VariantId);

        Assert.Equal(seeded.InitialStockQuantity + 5m, finalStock.Quantity);

        var returnIds = results.Select(result => result.ReturnId).ToList();
        var movements = await verifyContext.StockMovements
            .Where(movement =>
                movement.ProductVariantId == seeded.VariantId &&
                movement.MovementType == StockMovementType.SalesReturn &&
                movement.ReferenceId.HasValue &&
                returnIds.Contains(movement.ReferenceId.Value))
            .OrderBy(movement => movement.QuantityAfter)
            .ThenBy(movement => movement.Id)
            .ToListAsync();

        Assert.Equal(2, movements.Count);

        Assert.Equal(seeded.InitialStockQuantity + movements[0].QuantityChange, movements[0].QuantityAfter);
        Assert.Equal(movements[0].QuantityAfter + movements[1].QuantityChange, movements[1].QuantityAfter);
        Assert.Equal(finalStock.Quantity, movements[1].QuantityAfter);

        var validSequence =
            (movements[0].QuantityChange, movements[0].QuantityAfter, movements[1].QuantityChange, movements[1].QuantityAfter) switch
            {
                (2m, 12m, 3m, 15m) => true,
                (3m, 13m, 2m, 15m) => true,
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
        Assert.DoesNotContain(logEntries, entry => entry.LogLevel >= LogLevel.Warning);
    }

    [Fact]
    public async Task SalesReturns_Stress_Returns_Maintain_Monotonic_Audit_Trail()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        var requestedQuantities = new decimal[] { 1m, 2m, 3m, 2m, 4m, 1m, 3m, 2m, 1m, 4m };

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId, requestedQuantities);

        var logEntries = new ConcurrentQueue<LogEntry>();
        var progressEntries = new ConcurrentQueue<string>();
        using var startBarrier = new Barrier(participantCount: requestedQuantities.Length + 1);

        var tasks = seeded.Invoices
            .Select((invoice, index) => ExecuteReturnAsync(
                scope,
                tenantContext,
                tenantId,
                seeded,
                invoice,
                requestedQuantities[index],
                $"sales-return-stress-{index:D2}",
                Guid.Parse($"20000000-0000-0000-0000-{index + 1:D12}"),
                logEntries,
                progressEntries,
                startBarrier))
            .ToArray();

        startBarrier.SignalAndWait();
        var results = await AwaitAllOrThrowAsync(
            tasks,
            progressEntries,
            TimeSpan.FromSeconds(180));

        var totalReturnedQuantity = requestedQuantities.Sum();

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var finalStock = await verifyContext.ProductVariantStocks.SingleAsync(stock =>
            stock.WarehouseId == seeded.WarehouseId &&
            stock.VariantId == seeded.VariantId);

        Assert.Equal(seeded.InitialStockQuantity + totalReturnedQuantity, finalStock.Quantity);

        var returnIds = results.Select(result => result.ReturnId).ToList();
        var movements = await verifyContext.StockMovements
            .Where(movement =>
                movement.ProductVariantId == seeded.VariantId &&
                movement.MovementType == StockMovementType.SalesReturn &&
                movement.ReferenceId.HasValue &&
                returnIds.Contains(movement.ReferenceId.Value))
            .OrderBy(movement => movement.QuantityAfter)
            .ThenBy(movement => movement.Id)
            .ToListAsync();

        Assert.Equal(requestedQuantities.Length, movements.Count);

        var expectedQuantity = seeded.InitialStockQuantity;
        foreach (var movement in movements)
        {
            Assert.Equal(expectedQuantity + movement.QuantityChange, movement.QuantityAfter);
            expectedQuantity = movement.QuantityAfter;
        }

        Assert.Equal(finalStock.Quantity, expectedQuantity);
        Assert.Equal(movements.Count, movements.Select(movement => movement.QuantityAfter).Distinct().Count());
        var outboxMessages = await verifyContext.StockSyncOutboxMessages
            .Where(message => message.TenantId == tenantId && message.VariantId == seeded.VariantId)
            .ToListAsync();

        Assert.Equal(requestedQuantities.Length, outboxMessages.Count);
        Assert.All(outboxMessages, message =>
        {
            Assert.Equal(seeded.ProductId, message.ProductId);
            Assert.Equal(nameof(ProductVariantStockChangedEvent), message.EventType);
            Assert.Null(message.PublishedAtUtc);
            Assert.False(message.IsFailed);
        });
        Assert.DoesNotContain(logEntries, entry => entry.LogLevel >= LogLevel.Warning);
    }

    private static async Task<ReturnExecutionResult> ExecuteReturnAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid tenantId,
        SeededScenario seeded,
        SeededInvoice invoice,
        decimal refundQuantity,
        string idempotencyKey,
        Guid userId,
        ConcurrentQueue<LogEntry> logEntries,
        ConcurrentQueue<string> progressEntries,
        Barrier startBarrier)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        await ConfigureSessionTimeoutsAsync(context);

        progressEntries.Enqueue($"{idempotencyKey}: context-ready");

        var logger = new TestLogger<SalesReturnsController>(logEntries);
        var ledgerService = new Mock<ILedgerService>(MockBehavior.Strict);
        var controller = new SalesReturnsController(
            context,
            tenantContext,
            logger,
            ledgerService.Object,
            Mock.Of<IEventPublisher>(),
            CreateAccountingOutboxMock().Object,
            Mock.Of<ICommerceAccountingService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(userId)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = idempotencyKey;
        controller.Request.Headers["User-Agent"] = "SalesReturnsConcurrencyIntegrationTests";

        var request = new CreateSalesReturnRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = DateTime.UtcNow,
            OriginalInvoiceId = invoice.InvoiceId,
            RefundMethod = RefundMethod.Cash,
            CashAmount = invoice.UnitPrice * refundQuantity,
            CardAmount = 0m,
            Reason = "Concurrency validation",
            Notes = $"Invoice {invoice.InvoiceNumber}",
            Items =
            [
                new SalesReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = refundQuantity,
                    UnitPrice = invoice.UnitPrice,
                    TaxRate = 0m,
                    Reason = "Concurrent return"
                }
            ]
        };

        progressEntries.Enqueue($"{idempotencyKey}: request-ready");
        
        var barrierTimeout = TimeSpan.FromSeconds(10);
        if (!startBarrier.SignalAndWait(barrierTimeout))
        {
            var errorMsg = $"{idempotencyKey}: barrier timeout after {barrierTimeout.TotalSeconds}s";
            progressEntries.Enqueue(errorMsg);
            throw new XunitException(errorMsg);
        }
        
        progressEntries.Enqueue($"{idempotencyKey}: invoking-controller");

        ActionResult<SalesReturnResponse> actionResult;
        try
        {
            actionResult = await controller.CreateSalesReturn(request);
        }
        catch (Exception ex)
        {
            progressEntries.Enqueue($"{idempotencyKey}: failed:{ex.GetType().Name}");
            throw;
        }

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<SalesReturnResponse>(okResult.Value);
        progressEntries.Enqueue($"{idempotencyKey}: completed:{response.ReturnId}");

        return new ReturnExecutionResult(response.ReturnId, invoice.InvoiceId);
    }

    private static async Task<SeededScenario> SeedScenarioAsync(
        ApplicationDbContext context,
        Guid tenantId,
        IReadOnlyList<decimal> invoiceQuantities)
    {
        var utcNow = DateTime.UtcNow;
        var categoryId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        const decimal initialStockQuantity = 10m;
        const decimal unitPrice = 10m;

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Sales Returns Concurrency Tenant",
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
            Code = "WH-1",
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            Name = "Concurrency Customer",
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
            Name = "Concurrency Product",
            CategoryId = categoryId,
            PurchasePrice = 6m,
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
            SKU = "RET-CONC-001",
            Price = unitPrice,
            OnlinePrice = unitPrice,
            CostPrice = 6m,
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
            PurchasePrice = 6m,
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
            var invoiceNumber = $"INV-RET-TEST-{index + 1:D2}";
            var invoiceDate = utcNow.AddMinutes(-(index + 1));
            var totalAmount = quantity * unitPrice;

            var invoice = new SalesInvoice
            {
                Id = invoiceId,
                TenantId = tenantId,
                InvoiceNumber = invoiceNumber,
                InvoiceDate = invoiceDate,
                CustomerId = customerId,
                WarehouseId = warehouseId,
                Status = InvoiceStatus.Completed,
                Subtotal = totalAmount,
                DiscountAmount = 0m,
                DiscountPercentage = 0m,
                TaxAmount = 0m,
                TotalAmount = totalAmount,
                PaidAmount = totalAmount,
                PaymentMethod = PaymentMethod.Cash,
                CashAmount = totalAmount,
                CardAmount = 0m,
                CreatedAt = invoiceDate
            };

            invoice.Items.Add(new SalesInvoiceItem
            {
                Id = Guid.NewGuid(),
                SalesInvoiceId = invoiceId,
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

            context.SalesInvoices.Add(invoice);
            invoices.Add(new SeededInvoice(invoiceId, invoiceNumber, unitPrice));
        }

        await context.SaveChangesAsync();

        return new SeededScenario(
            tenantId,
            warehouseId,
            customerId,
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

    private static Mock<IAccountingOutboxService> CreateAccountingOutboxMock()
    {
        var mock = new Mock<IAccountingOutboxService>(MockBehavior.Strict);
        mock.Setup(service => service.EnqueuePosRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<RefundMethod>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static async Task ConfigureSessionTimeoutsAsync(ApplicationDbContext context)
    {
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET statement_timeout = '30s'; SET lock_timeout = '30s';");
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
            throw new XunitException(
                $"Sales return concurrency execution exceeded {timeout.TotalSeconds:F0}s. Progress: {string.Join(" | ", progressEntries)}");
        }
    }

    private sealed record SeededScenario(
        Guid TenantId,
        Guid WarehouseId,
        Guid CustomerId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId,
        decimal InitialStockQuantity,
        IReadOnlyList<SeededInvoice> Invoices);

    private sealed record SeededInvoice(
        Guid InvoiceId,
        string InvoiceNumber,
        decimal UnitPrice);

    private sealed record ReturnExecutionResult(
        Guid ReturnId,
        Guid OriginalInvoiceId);

    private sealed record LogEntry(
        LogLevel LogLevel,
        string Message);

    private sealed class TestLogger<T>(ConcurrentQueue<LogEntry> entries) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
