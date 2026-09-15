using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Integration;

/// <summary>
/// T-08 Part 21: Storefront refund → return → COGS-reversal linkage proofs.
/// Post-commit refunds restore returnable stock and stage exactly one reversal at
/// the original snapshot cost; pre-commit refunds release reservations and stage
/// no reversal; replays are idempotent; over-reversals fail closed.
/// </summary>
public sealed class T08StorefrontCogsLifecycleTests
{
    private const decimal UnitCost = 50m;
    private const int Quantity = 2;
    private const decimal UnitPrice = 100m;
    private const decimal OrderTotal = Quantity * UnitPrice;
    private const decimal PostedCogs = UnitCost * Quantity;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public T08StorefrontCogsLifecycleTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FullPostCommitRefund_RestoresStock_AndStagesOneReversal()
    {
        var tenantId = Guid.NewGuid();
        var (connection, context) = await CreateContextAsync(tenantId);
        await using var connectionDispose = connection;
        await using var contextDispose = context;
        var service = CreateService(context);
        var seed = await SeedCommittedOrderAsync(context, tenantId, cogsProcessed: true);

        var result = await service.RecordRefundAsync(seed.OrderId, OrderTotal, "refund-full-1", "T-08 full return");

        Assert.True(result.Success, result.ErrorMessage);

        var reversal = await context.AccountingOutboxMessages
            .SingleOrDefaultAsync(m => m.IdempotencyKey == $"ECOM_COGS_REV:{tenantId}:{seed.OrderId}:refund-full-1");
        Assert.NotNull(reversal);
        Assert.Equal("EcommerceCogsReversal", reversal!.EventType);
        var reversalPayload = JsonSerializer.Deserialize<EcommerceCogsReversalOutboxPayload>(reversal.PayloadJson);
        Assert.NotNull(reversalPayload);
        Assert.Equal(PostedCogs, reversalPayload!.TotalReversal);

        var stock = await context.ProductVariantStocks.SingleAsync();
        Assert.Equal(seed.StockBefore + Quantity, stock.Quantity);
        var reservation = await context.InventoryReservations.SingleAsync();
        Assert.Equal(Quantity, reservation.ReturnedQuantity);

        var movement = await context.StockMovements
            .Where(m => m.MovementType == StockMovementType.SalesReturn)
            .SingleOrDefaultAsync();
        Assert.NotNull(movement);
        Assert.Equal(Quantity, movement!.QuantityChange);
    }

    [Fact]
    public async Task PartialPostCommitRefund_ReversesProportionalShare()
    {
        var tenantId = Guid.NewGuid();
        var (connection, context) = await CreateContextAsync(tenantId);
        await using var connectionDispose = connection;
        await using var contextDispose = context;
        var service = CreateService(context);
        var seed = await SeedCommittedOrderAsync(context, tenantId, cogsProcessed: true);

        var result = await service.RecordRefundAsync(seed.OrderId, OrderTotal / 2m, "refund-half-1", "T-08 half return");

        Assert.True(result.Success, result.ErrorMessage);

        var reversal = await context.AccountingOutboxMessages
            .SingleOrDefaultAsync(m => m.IdempotencyKey == $"ECOM_COGS_REV:{tenantId}:{seed.OrderId}:refund-half-1");
        Assert.NotNull(reversal);
        var payload = JsonSerializer.Deserialize<EcommerceCogsReversalOutboxPayload>(reversal!.PayloadJson);
        Assert.Equal(PostedCogs / 2m, payload!.TotalReversal);

        var stock = await context.ProductVariantStocks.SingleAsync();
        Assert.Equal(seed.StockBefore + Quantity / 2m, stock.Quantity);
    }

    [Fact]
    public async Task SequentialHalfRefunds_RestoreEntireCommittedQuantity_WhileFirstReversalIsPending()
    {
        var tenantId = Guid.NewGuid();
        var (connection, context) = await CreateContextAsync(tenantId);
        await using var connectionDispose = connection;
        await using var contextDispose = context;
        var service = CreateService(context);
        var seed = await SeedCommittedOrderAsync(context, tenantId, cogsProcessed: true);

        var first = await service.RecordRefundAsync(seed.OrderId, OrderTotal / 2m, "refund-half-a", "T-08 first half");
        var second = await service.RecordRefundAsync(seed.OrderId, OrderTotal / 2m, "refund-half-b", "T-08 second half");

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal(seed.StockBefore + Quantity, (await context.ProductVariantStocks.SingleAsync()).Quantity);
        Assert.Equal(Quantity, (await context.InventoryReservations.SingleAsync()).ReturnedQuantity);

        var reversals = await context.AccountingOutboxMessages
            .Where(message => message.EventType == EcommerceCogsPolicy.StorefrontCogsReversalEventType)
            .ToListAsync();
        Assert.Equal(2, reversals.Count);
        Assert.Equal(PostedCogs, reversals.Sum(message =>
            JsonSerializer.Deserialize<EcommerceCogsReversalOutboxPayload>(message.PayloadJson)!.TotalReversal));
    }

    [Fact]
    public async Task ZeroCostPostCommitRefund_RestoresStock_AndStagesNoReversalJournalEvent()
    {
        var tenantId = Guid.NewGuid();
        var (connection, context) = await CreateContextAsync(tenantId);
        await using var connectionDispose = connection;
        await using var contextDispose = context;
        var service = CreateService(context);
        var seed = await SeedCommittedOrderAsync(context, tenantId, cogsProcessed: true, unitCost: 0m);

        var result = await service.RecordRefundAsync(seed.OrderId, OrderTotal, "refund-zero-cost", "T-08 zero-cost return");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(seed.StockBefore + Quantity, (await context.ProductVariantStocks.SingleAsync()).Quantity);
        Assert.Equal(Quantity, (await context.InventoryReservations.SingleAsync()).ReturnedQuantity);
        Assert.Empty(context.AccountingOutboxMessages
            .Where(message => message.EventType == EcommerceCogsPolicy.StorefrontCogsReversalEventType));
    }

    [Fact]
    public async Task PreCommitRefund_ReleasesReservations_AndStagesNoReversal()
    {
        var tenantId = Guid.NewGuid();
        var (connection, context) = await CreateContextAsync(tenantId);
        await using var connectionDispose = connection;
        await using var contextDispose = context;
        var stockRepository = new Mock<IProductVariantStockRepository>();
        var service = CreateService(context, stockRepositoryMock: stockRepository);
        var seed = await SeedCommittedOrderAsync(context, tenantId, cogsProcessed: false, committed: false);

        var preStock = await context.ProductVariantStocks.SingleAsync();
        Assert.Equal(10m, preStock.Quantity);
        Assert.Equal(2m, preStock.ReservedQuantity);

        var result = await service.RecordRefundAsync(seed.OrderId, OrderTotal, "refund-pre-1", "T-08 pre-commit refund");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(context.AccountingOutboxMessages
            .Where(m => m.EventType == EcommerceCogsPolicy.StorefrontCogsReversalEventType));
        stockRepository.Verify(
            x => x.ReleaseOrderReservationsAsync(tenantId, seed.OrderId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefundReplay_StagesExactlyOneReversal()
    {
        var tenantId = Guid.NewGuid();
        var (connection, context) = await CreateContextAsync(tenantId);
        await using var connectionDispose = connection;
        await using var contextDispose = context;
        var service = CreateService(context);
        var seed = await SeedCommittedOrderAsync(context, tenantId, cogsProcessed: true);

        var first = await service.RecordRefundAsync(seed.OrderId, OrderTotal, "refund-replay-1", "T-08 replay");
        var replay = await service.RecordRefundAsync(seed.OrderId, OrderTotal, "refund-replay-1", "T-08 replay retry");

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(replay.Success);
        Assert.True(replay.WasIdempotent);
        Assert.Equal(
            1,
            await context.AccountingOutboxMessages.CountAsync(m =>
                m.IdempotencyKey == $"ECOM_COGS_REV:{tenantId}:{seed.OrderId}:refund-replay-1"));
        var reservation = await context.InventoryReservations.SingleAsync();
        Assert.Equal(Quantity, reservation.ReturnedQuantity);
    }

    [Fact]
    public async Task SecondRefundBeyondPostedCogs_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var (connection, context) = await CreateContextAsync(tenantId);
        await using var connectionDispose = connection;
        await using var contextDispose = context;
        var service = CreateService(context);
        var seed = await SeedCommittedOrderAsync(context, tenantId, cogsProcessed: true);

        var first = await service.RecordRefundAsync(seed.OrderId, OrderTotal, "refund-cap-1", "T-08 first");
        Assert.True(first.Success, first.ErrorMessage);

        // Mark the first reversal processed so cumulative caps can observe it,
        // then attempt another full refund: refundable-amount guards reject it
        // before any second reversal can be staged.
        var firstReversal = await context.AccountingOutboxMessages
            .SingleAsync(m => m.IdempotencyKey == $"ECOM_COGS_REV:{tenantId}:{seed.OrderId}:refund-cap-1");
        firstReversal.MarkProcessed(Guid.NewGuid());
        await context.SaveChangesAsync();

        var second = await service.RecordRefundAsync(seed.OrderId, OrderTotal, "refund-cap-2", "T-08 over return");
        Assert.False(second.Success);
        Assert.Empty(context.AccountingOutboxMessages
            .Where(m => m.IdempotencyKey == $"ECOM_COGS_REV:{tenantId}:{seed.OrderId}:refund-cap-2"));
    }

    // NOTE: SQLite is used (relational transactions for the lifecycle service).
    // A test-only model shim drops the pre-existing T-06 Postgres regex check
    // constraint that SQLite cannot create; T-06 production code stays untouched.
    private static async Task<(SqliteConnection Connection, ApplicationDbContext Context)> CreateContextAsync(
        Guid? tenantId = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;
        var context = new T08LifecycleTestDbContext(
            options,
            new TestTenantContext(tenantId ?? Guid.NewGuid()));
        await context.Database.EnsureCreatedAsync();
        return (connection, context);
    }

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
        connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (value, characters) => value?.Trim((characters ?? " ").ToCharArray()));
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = System.Text.RegularExpressions.RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                }

                return System.Text.RegularExpressions.Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }

    private sealed class XunitLogger<T>(Xunit.Abstractions.ITestOutputHelper output) : ILogger<T>
    {
        IDisposable ILogger.BeginScope<TState>(TState state) => NullLogger<T>.Instance.BeginScope(state);

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                output.WriteLine($"[{logLevel}] {formatter(state, exception)}{(exception == null ? string.Empty : " | " + exception)}");
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private sealed class T08LifecycleTestDbContext(
        DbContextOptions<ApplicationDbContext> options,
        TestTenantContext tenantContext)
        : ApplicationDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property<uint>("xmin").HasDefaultValue(1u);
            modelBuilder.Entity<Product>().Property(product => product.RowVersion).HasDefaultValue(1u);
            modelBuilder.Entity<ProductVariant>().Property(variant => variant.RowVersion).HasDefaultValue(1u);
            modelBuilder.Entity<PaymentIntent>().Property(intent => intent.RowVersion).HasDefaultValue(1u);
            modelBuilder.Entity<OrderItem>().Property(item => item.PaidAmount).HasConversion<double>();
            modelBuilder.Entity<OrderItem>().Property(item => item.RefundedAmount).HasConversion<double>();

            // SQLite compatibility (repo convention): drop Postgres-only check
            // constraints (e.g. the T-06 `~` regex) and normalize xmin columns.
            // T-06 production code stays untouched. Note SQLite stores decimals
            // as TEXT, so numeric check constraints would misfire on values
            // like Quantity=10 vs ReservedQuantity=2 ('10' < '2').
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties()
                             .Where(property => string.Equals(
                                 property.GetColumnName(),
                                 "xmin",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                    property.SetDefaultValue(0u);
                }
            }
        }
    }

    private PaymentLifecycleService CreateService(
        ApplicationDbContext context,
        Mock<IProductVariantStockRepository>? stockRepositoryMock = null)
    {
        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor.Setup(a => a.GetCorrelationId()).Returns("t08-lifecycle-test");

        var accountingOutbox = new AccountingOutboxService(
            context,
            correlationIdAccessor.Object,
            NullLogger<AccountingOutboxService>.Instance);

        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(s => s.PostEcommerceRefundAsync(
                It.IsAny<Order>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "REFUND-JOURNAL-T08"));

        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(s => s.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IProductVariantStockRepository stockRepository = stockRepositoryMock?.Object
            ?? new ProductVariantStockRepository(
                context,
                NullLogger<ProductVariantStockRepository>.Instance);

        return new PaymentLifecycleService(
            context,
            accountingOutbox,
            commerceAccounting.Object,
            eventPublisher.Object,
            Mock.Of<IMarketplaceEventOutboxService>(),
            correlationIdAccessor.Object,
            stockRepository,
            Mock.Of<IInventoryReservationMetricsService>(),
            Mock.Of<ICouponValidationService>(),
            new XunitLogger<PaymentLifecycleService>(_output),
            Microsoft.Extensions.Options.Options.Create(new PaymentLifecycleSettings { RequireRowLevelLocking = false }));
    }

    private sealed record SeedResult(Guid OrderId, Guid ItemId, decimal StockBefore);

    private static async Task<SeedResult> SeedCommittedOrderAsync(
        ApplicationDbContext context,
        Guid tenantId,
        bool cogsProcessed,
        bool committed = true,
        decimal unitCost = UnitCost)
    {
        var utcNow = DateTime.UtcNow;
        var warehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "T-08 Warehouse",
            IsActive = true,
            CreatedAt = utcNow
        });
        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "T-08 Category",
            CreatedAt = utcNow
        });
        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "T-08 Product",
            CategoryId = categoryId,
            SalePrice = UnitPrice,
            PurchasePrice = unitCost,
            CreatedAt = utcNow
        });
        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = $"T08-{Guid.NewGuid():N}"[..12],
            Price = UnitPrice,
            OnlinePrice = UnitPrice,
            CostPrice = unitCost,
            IsDefault = true,
            IsActive = true,
            CreatedAt = utcNow
        });

        const decimal stockBefore = 10m;
        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = committed ? stockBefore - Quantity : stockBefore,
            ReservedQuantity = committed ? 0m : Quantity,
            DamagedQuantity = 0m,
            CreatedAt = utcNow
        });

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-T08-{Guid.NewGuid():N}"[..14],
            PaymentMethodCode = "CARD",
            PaymentGateway = "stripe",
            PaymentStatus = PaymentStatus.Collected,
            FulfillmentStatus = FulfillmentStatus.Delivered,
            FulfillmentMode = FulfillmentMode.Manual,
            Total = OrderTotal,
            Currency = "EGP",
            Status = OrderStatus.Confirmed,
            IsPaid = true,
            PaidAt = utcNow,
            FinancialPostingStatus = FinancialPostingStatus.Posted,
            FinancialPostedAtUtc = utcNow,
            CreatedAt = utcNow
        };
        order.Items.Add(new OrderItem
        {
            Id = itemId,
            ProductId = productId,
            VariantId = variantId,
            ProductName = "T-08 Product",
            SKU = "T08-SKU",
            Quantity = Quantity,
            UnitPrice = UnitPrice,
            Total = OrderTotal,
            CostPrice = unitCost,
            PaidAmount = OrderTotal,
            CreatedAt = utcNow
        });

        var paymentIntent = new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Amount = OrderTotal,
            Currency = "EGP",
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Provider = PaymentProvider.Stripe,
            Status = PaymentIntentStatus.Paid,
            PaymentReference = $"PAY-{Guid.NewGuid():N}"[..20],
            ProviderIntentId = $"pi_{Guid.NewGuid():N}",
            ProviderPaymentId = $"ch_{Guid.NewGuid():N}",
            CapturedAt = utcNow,
            CreatedAt = utcNow
        };
        var collectionJournal = JournalEntry.Create(
            tenantId,
            $"JE-{Guid.NewGuid():N}"[..20],
            utcNow,
            "OnlinePayment",
            orderId,
            order.OrderNumber,
            "T-08 collection",
            $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntent.Id}",
            userId: null);
        order.FinancialPostingJournalId = collectionJournal.Id;

        context.Orders.Add(order);
        context.PaymentIntents.Add(paymentIntent);
        context.JournalEntries.Add(collectionJournal);

        context.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            OrderItemId = itemId,
            ProductId = productId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            ReservedQuantity = Quantity,
            CommittedQuantity = committed ? Quantity : 0m,
            ReleasedQuantity = 0m,
            ReturnedQuantity = 0m,
            DamagedReturnedQuantity = 0m,
            ReservationEventId = Guid.NewGuid(),
            CreatedAt = utcNow
        });

        if (cogsProcessed)
        {
            var cogsMessage = AccountingOutboxMessage.Create(
                tenantId,
                EcommerceCogsPolicy.StorefrontCogsEventType,
                orderId,
                nameof(EcommerceCogsOutboxPayload),
                JsonSerializer.Serialize(new EcommerceCogsOutboxPayload(
                    orderId, "pay-commit-1", "EGP", utcNow,
                    new List<EcommerceCogsOutboxLine>
                    {
                        new(itemId, variantId, Quantity, unitCost, unitCost * Quantity)
                    },
                    unitCost * Quantity)),
                EcommerceCogsPolicy.BuildStorefrontCogsKey(tenantId, orderId, "pay-commit-1"));
            cogsMessage.MarkProcessed(Guid.NewGuid());
            context.AccountingOutboxMessages.Add(cogsMessage);
        }

        using var ledgerAuthority = LedgerMutationScope.Authorize();
        await context.SaveChangesAsync();
        return new SeedResult(orderId, itemId, committed ? stockBefore - Quantity : stockBefore);
    }

}
