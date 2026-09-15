using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Accounting;

/// <summary>
/// T-08 Part 21: durable COGS event + processor proofs.
/// Enqueue is atomic/idempotent with fail-closed payload conflicts; the processor
/// re-verifies frozen snapshots and cumulative caps before posting exactly one
/// balanced journal per event through FinancialPostingService.
/// </summary>
public sealed class T08EcommerceCogsOutboxProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _db;
    private readonly AccountingOutboxProcessor _processor;
    private readonly Mock<IAccountResolver> _resolver = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _cogsId = Guid.NewGuid();
    private readonly Guid _inventoryId = Guid.NewGuid();

    public T08EcommerceCogsOutboxProcessorTests()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IFinancialPostingService>(provider =>
            new FinancialPostingService(
                provider.GetRequiredService<ApplicationDbContext>(),
                NullLogger<FinancialPostingService>.Instance));
        services.AddScoped<ICommerceAccountingService>(provider =>
            new CommerceAccountingService(
                provider.GetRequiredService<IFinancialPostingService>(),
                _resolver.Object,
                Mock.Of<ICashboxLedgerAccountService>(),
                NullLogger<CommerceAccountingService>.Instance));
        services.AddScoped(_ => Mock.Of<IEventPublisher>());
        services.AddScoped(_ => Mock.Of<IMarketplaceEventOutboxService>());

        var cogs = Account.Create(_tenantId, AccountCodes.CostOfGoodsSold, "COGS", AccountType.CostOfGoodsSold);
        cogs.Id = _cogsId;
        var inventory = Account.Create(_tenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset);
        inventory.Id = _inventoryId;
        _resolver
            .Setup(x => x.GetByCodesAsync(_tenantId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenant, IEnumerable<string> codes, CancellationToken ct) =>
                codes.ToDictionary(code => code, code => code == AccountCodes.CostOfGoodsSold ? cogs : inventory));

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _db.LedgerAccounts.AddRange(cogs, inventory);
        _db.SaveChanges();

        _processor = new AccountingOutboxProcessor(
            _serviceProvider,
            NullLogger<AccountingOutboxProcessor>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _serviceProvider.Dispose();
    }

    // NOTE: enqueue tests run on SQLite (relational transactions) because the
    // InMemory provider cannot surface an ambient transaction to the outbox
    // boundary. A test-only model shim drops the pre-existing T-06 Postgres
    // regex check constraint that SQLite cannot create; T-06 itself is untouched.
    [Fact]
    public async Task EnqueueEcommerceCogs_DuplicateSamePayload_IsIdempotent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateRelationalContext(connection);
        var order = SeedOrder(db, costPrice: 50m, quantity: 2);
        await db.SaveChangesAsync();

        await using var tx = await db.Database.BeginTransactionAsync();
        var outbox = new AccountingOutboxService(
            db, StubCorrelation(), NullLogger<AccountingOutboxService>.Instance);
        var payload = CogsPayload(order.Id, order.Items.Single().Id, 2m, 50m, 100m, "commit-1");

        var first = await outbox.EnqueueEcommerceCogsAsync(_tenantId, order.Id, "commit-1", payload);
        var second = await outbox.EnqueueEcommerceCogsAsync(_tenantId, order.Id, "commit-1", payload);
        await tx.CommitAsync();

        Assert.True(first.WasCreated);
        Assert.True(second.AlreadyExists);
        Assert.Equal(
            1,
            await db.AccountingOutboxMessages.CountAsync(m =>
                m.IdempotencyKey == $"ECOM_COGS:{_tenantId}:{order.Id}:commit-1"));
    }

    [Fact]
    public async Task EnqueueEcommerceCogs_ChangedPayload_ConflictsFailClosed()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateRelationalContext(connection);
        var order = SeedOrder(db, costPrice: 50m, quantity: 2);
        await db.SaveChangesAsync();

        await using var tx = await db.Database.BeginTransactionAsync();
        var outbox = new AccountingOutboxService(
            db, StubCorrelation(), NullLogger<AccountingOutboxService>.Instance);
        await outbox.EnqueueEcommerceCogsAsync(
            _tenantId, order.Id, "commit-1",
            CogsPayload(order.Id, order.Items.Single().Id, 2m, 50m, 100m, "commit-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.EnqueueEcommerceCogsAsync(
            _tenantId, order.Id, "commit-1",
            CogsPayload(order.Id, order.Items.Single().Id, 2m, 60m, 120m, "commit-1")));
        await tx.RollbackAsync();
    }

    private static ApplicationDbContext CreateRelationalContext(SqliteConnection connection)
    {
        RegisterSqliteFunctions(connection);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new T08RelationalTestDbContext(options);
        context.Database.EnsureCreated();
        return context;
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

    private sealed class T08RelationalTestDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property<uint>("xmin").HasDefaultValue(1u);
            modelBuilder.Entity<Product>().Property(product => product.RowVersion).HasDefaultValue(1u);
            modelBuilder.Entity<OrderItem>().Property(item => item.PaidAmount).HasConversion<double>();
            modelBuilder.Entity<OrderItem>().Property(item => item.RefundedAmount).HasConversion<double>();

            // SQLite compatibility (repo convention): drop Postgres-only check
            // constraints and normalize xmin columns. Production code untouched.
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

    [Fact]
    public async Task ProcessEcommerceCogs_PostsExactlyOneBalancedJournal()
    {
        var order = SeedOrder(_db, costPrice: 50m, quantity: 2);
        StageCogsMessage(order, "commit-1", 2m, 50m, 100m);
        await _db.SaveChangesAsync();

        var processed = await _processor.ProcessPendingMessagesAsync();
        Assert.Equal(1, processed);

        var journals = await _db.JournalEntries.IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .Where(j => j.SourceType == "Order_COGS")
            .ToListAsync();
        var journal = Assert.Single(journals);
        Assert.Equal($"ECOM_COGS:{_tenantId}:{order.Id}:commit-1", journal.IdempotencyKey);
        Assert.Equal(100m, journal.LedgerEntries.Sum(l => l.DebitAmount));
        Assert.Equal(100m, journal.LedgerEntries.Sum(l => l.CreditAmount));
        var debit = journal.LedgerEntries.Single(l => l.DebitAmount > 0m);
        var credit = journal.LedgerEntries.Single(l => l.CreditAmount > 0m);
        Assert.Equal(_cogsId, debit.AccountId);
        Assert.Equal(_inventoryId, credit.AccountId);

        // Replay is idempotent: no second journal.
        var replayed = await _processor.ProcessPendingMessagesAsync();
        Assert.Equal(0, replayed);
        Assert.Equal(1, await _db.JournalEntries.IgnoreQueryFilters().CountAsync(j => j.SourceType == "Order_COGS"));
    }

    [Fact]
    public async Task ProcessEcommerceCogs_DistinctEvents_CannotExceedCommittedQuantity()
    {
        var order = SeedOrder(_db, costPrice: 50m, quantity: 2);
        StageCogsMessage(order, "commit-1", 2m, 50m, 100m);
        StageCogsMessage(order, "commit-2", 2m, 50m, 100m);
        await _db.SaveChangesAsync();

        await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, await _db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(journal => journal.SourceType == EcommerceCogsPolicy.StorefrontCogsSourceType));
        var rejected = await _db.AccountingOutboxMessages.IgnoreQueryFilters()
            .SingleAsync(message => message.IdempotencyKey.EndsWith(":commit-2"));
        Assert.Null(rejected.ProcessedAtUtc);
        Assert.False(rejected.IsFailed);
    }

    [Fact]
    public async Task ProcessEcommerceCogs_TamperedSnapshot_FailsClosedWithoutJournal()
    {
        var order = SeedOrder(_db, costPrice: 50m, quantity: 2);
        // Payload claims 60 while the immutable line carries 50.
        StageCogsMessage(order, "commit-1", 2m, 60m, 120m);
        await _db.SaveChangesAsync();

        await _processor.ProcessPendingMessagesAsync();

        Assert.Empty(_db.JournalEntries.IgnoreQueryFilters());
        _db.ChangeTracker.Clear();
        var message = await _db.AccountingOutboxMessages.IgnoreQueryFilters().SingleAsync();
        Assert.Null(message.ProcessedAtUtc);
        Assert.False(message.IsFailed);
    }

    [Fact]
    public async Task ProcessEcommerceCogs_ZeroCost_SkipsJournalButMarksProcessed()
    {
        var order = SeedOrder(_db, costPrice: 0m, quantity: 2);
        StageCogsMessage(order, "commit-1", 2m, 0m, 0m);
        await _db.SaveChangesAsync();

        var processed = await _processor.ProcessPendingMessagesAsync();
        Assert.Equal(1, processed);

        Assert.Empty(_db.JournalEntries.IgnoreQueryFilters());
        _db.ChangeTracker.Clear();
        var message = await _db.AccountingOutboxMessages.IgnoreQueryFilters().SingleAsync();
        Assert.True(message.ProcessedAtUtc.HasValue, message.LastError ?? "no error recorded");
    }

    [Fact]
    public async Task ProcessEcommerceCogsReversal_WithoutPostedCogs_FailsClosed()
    {
        var order = SeedOrder(_db, costPrice: 50m, quantity: 2);
        var refund = SeedRefund(order, "refund-1", 200m);
        StageReversalMessage(order, refund.RefundId, 2m, 100m);
        await _db.SaveChangesAsync();

        await _processor.ProcessPendingMessagesAsync();

        Assert.Empty(_db.JournalEntries.IgnoreQueryFilters().Where(j => j.SourceType == "Order_COGS_Reversal"));
    }

    [Fact]
    public async Task ProcessEcommerceCogsReversal_PostsReversal_AndCapsCumulativeOverReversal()
    {
        var order = SeedOrder(_db, costPrice: 50m, quantity: 2);
        StageCogsMessage(order, "commit-1", 2m, 50m, 100m, processed: true);
        var refund = SeedRefund(order, "refund-1", 200m);
        StageReversalMessage(order, refund.RefundId, 2m, 100m);
        await _db.SaveChangesAsync();

        var processed = await _processor.ProcessPendingMessagesAsync();
        Assert.Equal(1, processed);

        var journal = await _db.JournalEntries.IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .SingleAsync(j => j.SourceType == "Order_COGS_Reversal");
        var debit = journal.LedgerEntries.Single(l => l.DebitAmount > 0m);
        var credit = journal.LedgerEntries.Single(l => l.CreditAmount > 0m);
        Assert.Equal(_inventoryId, debit.AccountId);
        Assert.Equal(_cogsId, credit.AccountId);
        Assert.Equal(100m, debit.DebitAmount);

        // A second reversal for the same fully-reversed line must fail closed.
        var refund2 = SeedRefund(order, "refund-2", 200m);
        StageReversalMessage(order, refund2.RefundId, 2m, 100m);
        await _db.SaveChangesAsync();

        await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, await _db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(j => j.SourceType == "Order_COGS_Reversal"));
    }

    [Fact]
    public async Task ProcessCommerceHubCogs_PostsFromSnapshot_AndRejectsMissingSnapshot()
    {
        var item = SeedCommerceHubItem(_db, unitCost: 60m, quantity: 3, captured: true);
        StageCommerceHubCogsMessage(item, "evt-delivery-1", 3m, 60m, 180m);
        await SaveCommerceHubAsync();

        var processed = await _processor.ProcessPendingMessagesAsync();
        Assert.Equal(1, processed);

        var journal = await _db.JournalEntries.IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .SingleAsync(j => j.SourceType == "CommerceHub_COGS");
        Assert.Equal(180m, journal.LedgerEntries.Sum(l => l.DebitAmount));
        Assert.Equal(180m, journal.LedgerEntries.Sum(l => l.CreditAmount));

        // Cost-unknown historical line: no journal, message stays retryable, never dead-letters.
        var legacy = SeedCommerceHubItem(_db, unitCost: 0m, quantity: 1, captured: false);
        StageCommerceHubCogsMessage(legacy, "evt-delivery-2", 1m, 0m, 0m);
        await SaveCommerceHubAsync();

        await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, await _db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(j => j.SourceType == "CommerceHub_COGS"));
        var legacyMessage = await _db.AccountingOutboxMessages.IgnoreQueryFilters()
            .SingleAsync(m => m.IdempotencyKey.Contains("evt-delivery-2"));
        Assert.Null(legacyMessage.ProcessedAtUtc);
        Assert.False(legacyMessage.IsFailed);
    }

    [Fact]
    public async Task ProcessCommerceHubCogs_TenantMismatch_FailsClosed()
    {
        var item = SeedCommerceHubItem(_db, unitCost: 60m, quantity: 1, captured: true);
        var payload = new CommerceHubCogsOutboxPayload(
            item.SubOrder.MarketplaceOrderId, item.SubOrderId, item.Id,
            "evt-x", "EGP", DateTime.UtcNow, 1m, 60m, 60m);
        var foreignTenant = Guid.NewGuid();
        var message = AccountingOutboxMessage.Create(
            foreignTenant, "CommerceHubCogs", item.SubOrderId,
            nameof(CommerceHubCogsOutboxPayload), JsonSerializer.Serialize(payload),
            EcommerceCogsPolicy.BuildCommerceHubCogsKey(foreignTenant, item.SubOrderId, "evt-x"));
        _db.AccountingOutboxMessages.Add(message);
        await SaveCommerceHubAsync();

        await _processor.ProcessPendingMessagesAsync();

        Assert.Empty(_db.JournalEntries.IgnoreQueryFilters());
    }

    [Fact]
    public async Task ProcessCommerceHubCogs_InconsistentCapturedAmount_FailsClosed()
    {
        var item = SeedCommerceHubItem(_db, unitCost: 60m, quantity: 3, captured: true);
        item.CostAmountSnapshot = 999m;
        StageCommerceHubCogsMessage(item, "evt-delivery-tampered", 3m, 60m, 180m);
        await SaveCommerceHubAsync();

        await _processor.ProcessPendingMessagesAsync();

        Assert.Empty(_db.JournalEntries.IgnoreQueryFilters());
        _db.ChangeTracker.Clear();
        var message = await _db.AccountingOutboxMessages.IgnoreQueryFilters().SingleAsync();
        Assert.Null(message.ProcessedAtUtc);
        Assert.False(message.IsFailed);
    }

    private static EcommerceCogsOutboxPayload CogsPayload(
        Guid orderId, Guid itemId, decimal qty, decimal unitCost, decimal total, string commitEventId) =>
        new(orderId, commitEventId, "EGP", DateTime.UtcNow,
            new List<EcommerceCogsOutboxLine>
            {
                new(itemId, null, qty, unitCost, total)
            },
            total);

    private Order SeedOrder(ApplicationDbContext db, decimal costPrice, int quantity)
    {
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "T-08 Category",
            CreatedAt = DateTime.UtcNow
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "T-08 Product",
            CategoryId = category.Id,
            SalePrice = 100m,
            PurchasePrice = costPrice,
            CreatedAt = DateTime.UtcNow
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = $"ORD-T08-{Guid.NewGuid():N}"[..14],
            Status = OrderStatus.Confirmed,
            FulfillmentStatus = FulfillmentStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected,
            Total = 100m * quantity,
            Currency = "EGP",
            PaymentMethodCode = "CARD",
            PaidAt = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductName = "T-08 Item",
            SKU = $"T08-{Guid.NewGuid():N}"[..10],
            Quantity = quantity,
            UnitPrice = 100m,
            Total = 100m * quantity,
            CostPrice = costPrice,
            PaidAmount = 100m * quantity
        });
        db.Categories.Add(category);
        db.Products.Add(product);
        db.Orders.Add(order);
        db.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderId = order.Id,
            OrderItemId = order.Items.Single().Id,
            ProductId = product.Id,
            VariantId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            ReservedQuantity = quantity,
            CommittedQuantity = quantity,
            ReservationEventId = Guid.NewGuid()
        });
        return order;
    }

    private void StageCogsMessage(Order order, string commitEventId, decimal qty, decimal unitCost, decimal total, bool processed = false)
    {
        var item = order.Items.Single();
        var payload = new EcommerceCogsOutboxPayload(
            order.Id, commitEventId, "EGP", DateTime.UtcNow,
            new List<EcommerceCogsOutboxLine>
            {
                new(item.Id, item.VariantId, qty, unitCost, total)
            },
            total);
        var message = AccountingOutboxMessage.Create(
            _tenantId, "EcommerceCogs", order.Id,
            nameof(EcommerceCogsOutboxPayload), JsonSerializer.Serialize(payload),
            EcommerceCogsPolicy.BuildStorefrontCogsKey(_tenantId, order.Id, commitEventId),
            aggregateVersion: 1);
        if (processed)
        {
            message.MarkProcessed(Guid.NewGuid());
        }

        _db.AccountingOutboxMessages.Add(message);
    }

    private OrderRefund SeedRefund(Order order, string refundId, decimal amount)
    {
        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderId = order.Id,
            RefundId = refundId,
            Amount = amount,
            Currency = "EGP",
            Reason = "T-08 test",
            PaymentMethod = "stripe",
            FinancialPostingStatus = FinancialPostingStatus.Pending,
            RefundedAtUtc = DateTime.UtcNow
        };
        _db.OrderRefunds.Add(refund);
        return refund;
    }

    private void StageReversalMessage(Order order, string refundId, decimal returnQty, decimal reversal)
    {
        var reservation = _db.InventoryReservations.Local.Single(item => item.OrderItemId == order.Items.Single().Id);
        reservation.ReturnedQuantity += returnQty;
        var payload = new EcommerceCogsReversalOutboxPayload(
            order.Id, refundId, "EGP", DateTime.UtcNow,
            new List<EcommerceCogsReversalOutboxLine>
            {
                new(order.Items.Single().Id, returnQty, reversal)
            },
            reversal);
        _db.AccountingOutboxMessages.Add(AccountingOutboxMessage.Create(
            _tenantId, "EcommerceCogsReversal", order.Id,
            nameof(EcommerceCogsReversalOutboxPayload), JsonSerializer.Serialize(payload),
            EcommerceCogsPolicy.BuildStorefrontCogsReversalKey(_tenantId, order.Id, refundId),
            aggregateVersion: 1));
    }

    private CommerceHubSubOrderItem SeedCommerceHubItem(
        ApplicationDbContext db, decimal unitCost, int quantity, bool captured)
    {
        var marketplaceOrder = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"CH-T08-{Guid.NewGuid():N}"[..12],
            Currency = "EGP",
            Status = OrderStatus.Shipped,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = marketplaceOrder.Id,
            MarketplaceOrder = marketplaceOrder,
            OrderNumber = $"SUB-T08-{Guid.NewGuid():N}"[..12],
            StoreId = _tenantId,
            StoreName = "T-08 Store",
            Currency = "EGP",
            Status = OrderStatus.Shipped
        };
        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            ProductName = "T-08 CH Item",
            Quantity = quantity,
            UnitPrice = 100m,
            Total = 100m * quantity,
            UnitCostSnapshot = unitCost,
            CostAmountSnapshot = unitCost * quantity,
            CostSnapshotCapturedAtUtc = captured ? DateTime.UtcNow : null
        };
        subOrder.Items.Add(item);
        marketplaceOrder.SubOrders.Add(subOrder);
        db.CommerceHubMarketplaceOrders.Add(marketplaceOrder);
        db.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderId = marketplaceOrder.Id,
            SubOrderId = subOrder.Id,
            OrderItemId = item.Id,
            ProductId = item.ProductId,
            VariantId = item.VariantId,
            WarehouseId = Guid.NewGuid(),
            ReservedQuantity = quantity,
            CommittedQuantity = quantity,
            ReservationEventId = Guid.NewGuid()
        });
        return item;
    }

    private async Task SaveCommerceHubAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await _db.SaveChangesAsync();
        }
    }

    private void StageCommerceHubCogsMessage(
        CommerceHubSubOrderItem item, string deliveryEventId, decimal qty, decimal unitCost, decimal cost)
    {
        var payload = new CommerceHubCogsOutboxPayload(
            item.SubOrder.MarketplaceOrderId, item.SubOrderId, item.Id,
            deliveryEventId, "EGP", DateTime.UtcNow, qty, unitCost, cost);
        _db.AccountingOutboxMessages.Add(AccountingOutboxMessage.Create(
            _tenantId, "CommerceHubCogs", item.SubOrderId,
            nameof(CommerceHubCogsOutboxPayload), JsonSerializer.Serialize(payload),
            EcommerceCogsPolicy.BuildCommerceHubCogsKey(_tenantId, item.SubOrderId, deliveryEventId),
            aggregateVersion: 1));
    }

    private static ICorrelationIdAccessor StubCorrelation()
    {
        var mock = new Mock<ICorrelationIdAccessor>();
        mock.Setup(x => x.GetCorrelationId()).Returns("t08-test");
        return mock.Object;
    }
}
