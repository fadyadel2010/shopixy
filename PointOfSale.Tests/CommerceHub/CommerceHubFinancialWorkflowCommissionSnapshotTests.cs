using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubFinancialWorkflowCommissionSnapshotTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubFinancialWorkflowCommissionSnapshotTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ApplyDeliveredFinancialsAsync_ShouldUseItemLevelCommissionSnapshot_WhenSubOrderSnapshotIsMissing()
    {
        var storeId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 3, 23, 12, 0, 0, DateTimeKind.Utc);

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CH-SNAP-001",
            FullName = "Snapshot Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Snapshot Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Subtotal = 100m,
            ShippingTotal = 10m,
            TotalAmount = 110m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = "CH-SNAP-001-A",
            StoreId = storeId,
            StoreName = "Snapshot Store",
            Currency = "EGP",
            Subtotal = 100m,
            Shipping = 10m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Total = 110m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        subOrder.Items.Add(new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Commissioned Product",
            Quantity = 1,
            UnitPrice = 100m,
            Total = 100m,
            PlatformCommissionRate = 0.25m,
            PlatformCommissionAmount = 25m
        });

        order.SubOrders.Add(subOrder);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var workflowService = new CommerceHubFinancialWorkflowService(
            _dbContext,
            Options.Create(new CommerceHubPaymentsOptions
            {
                PlatformCommissionRate = 0.05m
            }),
            Options.Create(new CommerceHubPayoutSettings
            {
                PayoutDelayDays = 7
            }),
            new CommerceHubFinancialLedgerService(
                _dbContext,
                NullLogger<CommerceHubFinancialLedgerService>.Instance),
            new CommerceHubFinancialAuditService(_dbContext),
            NullLogger<CommerceHubFinancialWorkflowService>.Instance);

        await workflowService.ApplyDeliveredFinancialsAsync(order, subOrder, nowUtc);
        await SaveCommerceHubFixtureAsync();

        var savedSubOrder = await _dbContext.CommerceHubSubOrders
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == subOrder.Id);
        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.SubOrderId == subOrder.Id);
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(item => item.ReferenceId == subOrder.Id)
            .ToListAsync();

        Assert.Equal(25m, savedSubOrder.PlatformCommissionAmount);
        Assert.Equal(0.25m, savedSubOrder.PlatformCommissionRate);
        Assert.NotEqual(27.50m, savedSubOrder.PlatformCommissionAmount);
        Assert.Equal(85m, savedSubOrder.VendorEarningAmount);
        Assert.False(string.IsNullOrWhiteSpace(savedSubOrder.CommissionSnapshotJson));
        Assert.Equal(85m, earning.Amount);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, earning.Status);
        Assert.Equal(3, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, item => item.TransactionType == CommerceHubFinancialTransactionType.OrderPayment && item.EntityType == CommerceHubFinancialEntityType.Vendor && item.Direction == CommerceHubFinancialTransactionDirection.Credit && item.Amount == 110m);
        Assert.Contains(ledgerEntries, item => item.TransactionType == CommerceHubFinancialTransactionType.Commission && item.EntityType == CommerceHubFinancialEntityType.Vendor && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 25m);
        Assert.Contains(ledgerEntries, item => item.TransactionType == CommerceHubFinancialTransactionType.Commission && item.EntityType == CommerceHubFinancialEntityType.Platform && item.Direction == CommerceHubFinancialTransactionDirection.Credit && item.Amount == 25m);

        using var json = JsonDocument.Parse(savedSubOrder.CommissionSnapshotJson!);
        Assert.Equal(0.25m, json.RootElement.GetProperty("rate").GetDecimal());
        Assert.Equal(25m, json.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(CommissionCalculator.ItemSnapshotFallbackSource, json.RootElement.GetProperty("source").GetString());
        Assert.Equal(CommissionCalculator.CommissionBase, json.RootElement.GetProperty("commissionBase").GetString());
        Assert.True(json.RootElement.GetProperty("shippingExcluded").GetBoolean());
    }

    [Fact]
    public async Task ApplyDeliveredFinancialsAsync_WhenCommissionSnapshotAndStoredValuesAreMissing_ShouldFailClosed()
    {
        var storeId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 3, 23, 12, 0, 0, DateTimeKind.Utc);

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CH-SNAP-002",
            FullName = "Snapshot Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Snapshot Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Subtotal = 100m,
            ShippingTotal = 10m,
            TotalAmount = 110m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = "CH-SNAP-002-A",
            StoreId = storeId,
            StoreName = "Snapshot Store",
            Currency = "EGP",
            Subtotal = 100m,
            Shipping = 10m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Total = 110m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        subOrder.Items.Add(new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Missing Commission Product",
            Quantity = 1,
            UnitPrice = 100m,
            Total = 100m
        });

        order.SubOrders.Add(subOrder);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var workflowService = new CommerceHubFinancialWorkflowService(
            _dbContext,
            Options.Create(new CommerceHubPaymentsOptions
            {
                PlatformCommissionRate = 0.75m
            }),
            Options.Create(new CommerceHubPayoutSettings
            {
                PayoutDelayDays = 7
            }),
            new CommerceHubFinancialLedgerService(
                _dbContext,
                NullLogger<CommerceHubFinancialLedgerService>.Instance),
            new CommerceHubFinancialAuditService(_dbContext),
            NullLogger<CommerceHubFinancialWorkflowService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflowService.ApplyDeliveredFinancialsAsync(order, subOrder, nowUtc));

        Assert.Contains("commission snapshot is incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(subOrder.IsEarningCreated);
        Assert.Equal(0m, subOrder.PlatformCommissionAmount);
        Assert.Equal(0m, subOrder.VendorEarningAmount);
        Assert.Null(subOrder.CommissionSnapshotJson);
        Assert.Empty(await _dbContext.CommerceHubVendorEarnings.ToListAsync());
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    /// <summary>
    /// S38C Regression Test: Verify that a legacy subOrder with only Subtotal > 0
    /// and all four financial fields (PlatformCommissionRate, PlatformCommissionAmount,
    /// FinalPlatformCommissionAmount, VendorEarningAmount) == 0m is NOT treated as
    /// having an existing snapshot. This prevents false positives where uninitialized
    /// orders are incorrectly considered to have valid financial data.
    /// </summary>
    [Fact]
    public async Task ApplyDeliveredFinancialsAsync_LegacySubOrderWithOnlySubtotal_ShouldNotUseLegacySnapshot()
    {
        var storeId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc);

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CH-LEGACY-001",
            FullName = "Legacy Customer",
            PhoneNumber = "01000000001",
            AddressLine = "456 Legacy Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Subtotal = 200m,
            ShippingTotal = 20m,
            TotalAmount = 220m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        // Legacy subOrder: Subtotal > 0 but all four financial fields are 0m (uninitialized)
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = "CH-LEGACY-001-A",
            StoreId = storeId,
            StoreName = "Legacy Store",
            Currency = "EGP",
            Subtotal = 200m,
            Shipping = 20m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Total = 220m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery,
            // All four financial fields are 0m (default/uninitialized)
            PlatformCommissionRate = 0m,
            PlatformCommissionAmount = 0m,
            FinalPlatformCommissionAmount = 0m,
            VendorEarningAmount = 0m
        };

        // Add item with commission snapshot (item-level fallback)
        subOrder.Items.Add(new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Legacy Product",
            Quantity = 2,
            UnitPrice = 100m,
            Total = 200m,
            PlatformCommissionRate = 0.15m,
            PlatformCommissionAmount = 30m
        });

        order.SubOrders.Add(subOrder);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var workflowService = new CommerceHubFinancialWorkflowService(
            _dbContext,
            Options.Create(new CommerceHubPaymentsOptions
            {
                PlatformCommissionRate = 0.10m // Different from item-level rate
            }),
            Options.Create(new CommerceHubPayoutSettings
            {
                PayoutDelayDays = 7
            }),
            new CommerceHubFinancialLedgerService(
                _dbContext,
                NullLogger<CommerceHubFinancialLedgerService>.Instance),
            new CommerceHubFinancialAuditService(_dbContext),
            NullLogger<CommerceHubFinancialWorkflowService>.Instance);

        await workflowService.ApplyDeliveredFinancialsAsync(order, subOrder, nowUtc);
        await SaveCommerceHubFixtureAsync();

        var savedSubOrder = await _dbContext.CommerceHubSubOrders
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == subOrder.Id);

        // Assert: Should use item-level snapshot (30m), NOT treat zero fields as valid legacy snapshot
        Assert.Equal(30m, savedSubOrder.PlatformCommissionAmount);
        Assert.Equal(0.15m, savedSubOrder.PlatformCommissionRate);
        Assert.Equal(190m, savedSubOrder.VendorEarningAmount); // 220 - 30
        Assert.False(string.IsNullOrWhiteSpace(savedSubOrder.CommissionSnapshotJson));

        // Verify snapshot source is item-level fallback, not legacy fields
        using var json = JsonDocument.Parse(savedSubOrder.CommissionSnapshotJson!);
        Assert.Equal(CommissionCalculator.ItemSnapshotFallbackSource, json.RootElement.GetProperty("source").GetString());
    }

    /// <summary>
    /// S38C Regression Test: Verify that a legacy subOrder with non-zero financial fields
    /// (PlatformCommissionRate, PlatformCommissionAmount, FinalPlatformCommissionAmount,
    /// and VendorEarningAmount) IS treated as having an existing snapshot. This ensures
    /// historical orders with valid commission data are correctly identified and preserved.
    /// The detection logic uses OR (at least one non-zero), but this test validates the
    /// complete snapshot case with all fields populated.
    /// </summary>
    [Fact]
    public async Task ApplyDeliveredFinancialsAsync_LegacySubOrderWithNonZeroFinancialField_ShouldUseLegacySnapshot()
    {
        var storeId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc);

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CH-LEGACY-002",
            FullName = "Legacy Customer 2",
            PhoneNumber = "01000000002",
            AddressLine = "789 Legacy Avenue",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Subtotal = 300m,
            ShippingTotal = 30m,
            TotalAmount = 330m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        // Legacy subOrder: Has non-zero financial fields (valid historical snapshot)
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = "CH-LEGACY-002-A",
            StoreId = storeId,
            StoreName = "Legacy Store 2",
            Currency = "EGP",
            Subtotal = 300m,
            Shipping = 30m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Total = 330m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery,
            // All four financial fields are non-zero (complete valid legacy snapshot)
            PlatformCommissionRate = 0.20m,
            PlatformCommissionAmount = 60m,
            FinalPlatformCommissionAmount = 60m,
            VendorEarningAmount = 270m
        };

        // Add item with different commission (should be ignored in favor of legacy snapshot)
        subOrder.Items.Add(new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Legacy Product 2",
            Quantity = 3,
            UnitPrice = 100m,
            Total = 300m,
            PlatformCommissionRate = 0.10m,
            PlatformCommissionAmount = 30m // Different from subOrder-level
        });

        order.SubOrders.Add(subOrder);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var workflowService = new CommerceHubFinancialWorkflowService(
            _dbContext,
            Options.Create(new CommerceHubPaymentsOptions
            {
                PlatformCommissionRate = 0.05m // Different from both legacy and item-level
            }),
            Options.Create(new CommerceHubPayoutSettings
            {
                PayoutDelayDays = 7
            }),
            new CommerceHubFinancialLedgerService(
                _dbContext,
                NullLogger<CommerceHubFinancialLedgerService>.Instance),
            new CommerceHubFinancialAuditService(_dbContext),
            NullLogger<CommerceHubFinancialWorkflowService>.Instance);

        await workflowService.ApplyDeliveredFinancialsAsync(order, subOrder, nowUtc);
        await SaveCommerceHubFixtureAsync();

        var savedSubOrder = await _dbContext.CommerceHubSubOrders
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == subOrder.Id);

        // Assert: Should preserve legacy snapshot values (60m commission, 270m earning)
        Assert.Equal(60m, savedSubOrder.PlatformCommissionAmount);
        Assert.Equal(0.20m, savedSubOrder.PlatformCommissionRate);
        Assert.Equal(270m, savedSubOrder.VendorEarningAmount);
        Assert.False(string.IsNullOrWhiteSpace(savedSubOrder.CommissionSnapshotJson));

        // Verify snapshot source is historical fields, not item-level
        using var json = JsonDocument.Parse(savedSubOrder.CommissionSnapshotJson!);
        Assert.Equal(CommissionCalculator.HistoricalFieldsSource, json.RootElement.GetProperty("source").GetString());

        // Verify earning record matches legacy snapshot
        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(e => e.SubOrderId == subOrder.Id);
        Assert.Equal(270m, earning.Amount);
    }

    [Fact]
    public async Task ApplyDeliveredFinancialsAsync_WhenCompleteSnapshotJsonDisagreesWithFields_ShouldFailClosed()
    {
        var storeId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc);
        var order = CreateDeliveredCodOrder("CH-SNAP-DRIFT-001", storeId);
        var subOrder = order.SubOrders.Single();
        subOrder.PlatformCommissionRate = 0.20m;
        subOrder.PlatformCommissionAmount = 20m;
        subOrder.FinalPlatformCommissionAmount = 20m;
        subOrder.PromoDiscountAmount = 0m;
        subOrder.VendorEarningAmount = 90m;
        subOrder.CommissionSnapshotJson = JsonSerializer.Serialize(new
        {
            rate = 0.20m,
            amount = 20m,
            grossCommissionAmount = 20m,
            finalCommissionAmount = 20m,
            promoDiscountAmount = 0m,
            vendorEarning = 95m,
            source = CommissionCalculator.VendorSource
        });

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var workflowService = CreateWorkflowService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflowService.ApplyDeliveredFinancialsAsync(order, subOrder, nowUtc));

        Assert.Contains("COMMERCEHUB_FINANCIAL_SNAPSHOT_MISMATCH", exception.Message);
        Assert.False(subOrder.IsEarningCreated);
        Assert.Empty(await _dbContext.CommerceHubVendorEarnings.ToListAsync());
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task ApplyDeliveredFinancialsAsync_WhenExistingEarningIsAvailable_ShouldNotRepriceIt()
    {
        var storeId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc);
        var order = CreateDeliveredCodOrder("CH-EARNING-LOCK-001", storeId);
        var subOrder = order.SubOrders.Single();
        subOrder.PlatformCommissionRate = 0.20m;
        subOrder.PlatformCommissionAmount = 20m;
        subOrder.FinalPlatformCommissionAmount = 20m;
        subOrder.PromoDiscountAmount = 0m;
        subOrder.VendorEarningAmount = 90m;
        subOrder.CommissionSnapshotJson = JsonSerializer.Serialize(new
        {
            rate = 0.20m,
            amount = 20m,
            grossCommissionAmount = 20m,
            finalCommissionAmount = 20m,
            promoDiscountAmount = 0m,
            vendorEarning = 90m,
            source = CommissionCalculator.VendorSource
        });

        var earning = new CommerceHubVendorEarning
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            TenantId = storeId,
            Amount = 85m,
            Status = CommerceHubVendorEarningStatus.Available,
            AvailableAtUtc = nowUtc.AddDays(-1)
        };

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubVendorEarnings.Add(earning);
        await SaveCommerceHubFixtureAsync();

        var workflowService = CreateWorkflowService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflowService.ApplyDeliveredFinancialsAsync(order, subOrder, nowUtc));

        Assert.Contains("COMMERCEHUB_EARNING_AMOUNT_LOCKED", exception.Message);
        Assert.Equal(85m, earning.Amount);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, earning.Status);
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CommerceHubFinancialWorkflowService CreateWorkflowService()
    {
        return new CommerceHubFinancialWorkflowService(
            _dbContext,
            Options.Create(new CommerceHubPaymentsOptions
            {
                PlatformCommissionRate = 0.05m
            }),
            Options.Create(new CommerceHubPayoutSettings
            {
                PayoutDelayDays = 7
            }),
            new CommerceHubFinancialLedgerService(
                _dbContext,
                NullLogger<CommerceHubFinancialLedgerService>.Instance),
            new CommerceHubFinancialAuditService(_dbContext),
            NullLogger<CommerceHubFinancialWorkflowService>.Instance);
    }

    private static CommerceHubMarketplaceOrder CreateDeliveredCodOrder(string orderNumber, Guid storeId)
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            FullName = "Financial Integrity Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Finance Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Subtotal = 100m,
            ShippingTotal = 10m,
            TotalAmount = 110m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{orderNumber}-A",
            StoreId = storeId,
            StoreName = "Financial Integrity Store",
            Currency = "EGP",
            Subtotal = 100m,
            Shipping = 10m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Total = 110m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };

        order.SubOrders.Add(subOrder);
        return order;
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            await _dbContext.SaveChangesAsync();
        }
    }
}
