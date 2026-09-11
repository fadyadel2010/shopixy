using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.TestDoubles;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.Tests.CommerceHub;

public sealed class SuperAdminPayoutsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubFinancialLedgerService _ledgerService;
    private readonly CommerceHubVendorPayoutReadService _readService;
    private readonly CommerceHubWalletService _walletService;
    private readonly SuperAdminPayoutsService _service;
    private readonly Guid _actorId = Guid.NewGuid();

    public SuperAdminPayoutsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _ledgerService = new CommerceHubFinancialLedgerService(
            _dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);
        _readService = new CommerceHubVendorPayoutReadService(
            _dbContext,
            Options.Create(new RefundSettings
            {
                RefundWindowDays = 7
            }),
            Options.Create(new CommerceHubPayoutSettings
            {
                PayoutDelayDays = 3,
                LargePayableDelayDays = 14,
                LargePayableAlertAmount = 1000m
            }),
            NullLogger<CommerceHubVendorPayoutReadService>.Instance);
        _walletService = new CommerceHubWalletService(_dbContext);
        _service = CreateService();
    }

    [Fact]
    public async Task GetQueueAsync_ShouldExposeCommittedPayoutForAuthorizedReconciliation()
    {
        var vendorId = await SeedVendorAsync("Queue Reconciliation Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);
        var graph = await SeedEligibleOnlineOrderAsync(vendorId, "CH-PAYOUT-QUEUE-RECON");
        await _ledgerService.RecordOrderAsync(graph.Order, graph.SubOrder);
        await SeedReconciliationRequiredPayoutAsync(vendorId, 20m);

        var result = await _service.GetQueueAsync(1, 20);
        var item = Assert.Single(result.Items);

        Assert.Equal(vendorId, item.VendorId);
        Assert.Equal(70m, item.EligibleAmount);
        Assert.False(item.CanPreparePayout);
        Assert.True(item.CanCompletePayout);
        Assert.NotNull(item.ActivePayout);
        Assert.Null(item.ExecutionBlockedCode);
        Assert.Equal("ReconciliationRequired", item.ActivePayout.Status);
    }

    [Fact]
    public async Task GetQueueAsync_ShouldExposePendingPayoutForIdempotentHandoffResume()
    {
        var vendorId = await SeedVendorAsync("Queue Pending Vendor");
        var payoutMethodId = await SeedDefaultPayoutMethodAsync(vendorId);
        var graph = await SeedEligibleOnlineOrderAsync(vendorId, "CH-PAYOUT-QUEUE-PENDING");
        await _ledgerService.RecordOrderAsync(graph.Order, graph.SubOrder);
        await SeedPendingPayoutAsync(vendorId, payoutMethodId, 20m);

        var result = await _service.GetQueueAsync(1, 20);
        var item = Assert.Single(result.Items);

        Assert.True(item.CanPreparePayout);
        Assert.False(item.CanCompletePayout);
        Assert.NotNull(item.ActivePayout);
        Assert.Equal("Pending", item.ActivePayout.Status);
        Assert.Equal(payoutMethodId, item.ActivePayout.PayoutMethodId);
        Assert.Null(item.ActivePayout.ExternalTransferHandoffToken);
        Assert.Null(item.ActivePayout.ExternalTransferHandoffCommittedAtUtc);
    }

    [Fact]
    public async Task GetQueueAsync_ShouldReturnGlobalTotalsEvenWhenItemsArePaged()
    {
        var firstVendorId = await SeedVendorAsync("Paged Queue Vendor A");
        await SeedDefaultPayoutMethodAsync(firstVendorId);
        var firstGraph = await SeedEligibleOnlineOrderAsync(firstVendorId, "CH-PAYOUT-PAGE-A");
        await _ledgerService.RecordOrderAsync(firstGraph.Order, firstGraph.SubOrder);

        var secondVendorId = await SeedVendorAsync("Paged Queue Vendor B");
        await SeedDefaultPayoutMethodAsync(secondVendorId);
        var secondGraph = await SeedEligibleOnlineOrderAsync(secondVendorId, "CH-PAYOUT-PAGE-B");
        await _ledgerService.RecordOrderAsync(secondGraph.Order, secondGraph.SubOrder);

        await SeedReconciliationRequiredPayoutAsync(secondVendorId, 20m);

        var result = await _service.GetQueueAsync(1, 1);

        Assert.Single(result.Items);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(160m, result.TotalPayableAmount);
        Assert.Equal(160m, result.TotalEligibleAmount);
        Assert.Equal(0m, result.TotalHeldAmount);
    }

    [Fact]
    public async Task GetQueueAsync_ShouldRejectNonEgpOrderAtPersistenceBoundary()
    {
        var egpVendorId = await SeedVendorAsync("EGP Queue Vendor", "EGP");
        await SeedDefaultPayoutMethodAsync(egpVendorId);
        var egpGraph = await SeedEligibleOnlineOrderAsync(egpVendorId, "CH-PAYOUT-MULTI-EGP", "EGP");
        await _ledgerService.RecordOrderAsync(egpGraph.Order, egpGraph.SubOrder);

        var usdVendorId = await SeedVendorAsync("USD Queue Vendor", "USD");
        await SeedDefaultPayoutMethodAsync(usdVendorId);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeedEligibleOnlineOrderAsync(usdVendorId, "CH-PAYOUT-MULTI-USD", "USD"));

        Assert.Contains("CommerceHub currency sovereignty violation", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private SuperAdminPayoutsService CreateService()
    {
        return new SuperAdminPayoutsService(
            _dbContext,
            _readService,
            NullLogger<SuperAdminPayoutsService>.Instance);
    }

    private async Task<Guid> SeedVendorAsync(string name, string currency = "EGP")
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = name,
            Currency = currency,
            IsActive = true,
            Status = TenantStatus.Active
        });

        await _dbContext.SaveChangesAsync();
        return vendorId;
    }

    private async Task<Guid> SeedDefaultPayoutMethodAsync(Guid vendorId)
    {
        var methodId = Guid.NewGuid();
        _dbContext.CommerceHubVendorPayoutMethods.Add(new CommerceHubVendorPayoutMethod
        {
            Id = methodId,
            VendorId = vendorId,
            MethodType = CommerceHubPayoutMethod.BankTransfer,
            DestinationValue = "EG-EXEC-001",
            IsDefault = true,
            IsVerified = true,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
        return methodId;
    }

    private async Task SeedReconciliationRequiredPayoutAsync(Guid vendorId, decimal amount)
    {
        using var payoutBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        _dbContext.CommerceHubPayouts.Add(new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            Amount = amount,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.ReconciliationRequired,
            Method = CommerceHubPayoutMethod.BankTransfer,
            DestinationValueSnapshot = "EG-EXEC-001",
            ExternalTransferIdempotencyKey = $"CH-PAYOUT:{Guid.NewGuid():N}",
            ExternalTransferHandoffToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ExternalTransferHandoffCommittedAtUtc = DateTime.UtcNow.AddHours(-4),
            FailureReason = "External transfer outcome is unknown after the processing handoff exceeded the safe window.",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow.AddHours(-4),
            ProcessedAtUtc = DateTime.UtcNow.AddHours(-4)
        });

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedPendingPayoutAsync(Guid vendorId, Guid payoutMethodId, decimal amount)
    {
        using var payoutBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        _dbContext.CommerceHubPayouts.Add(new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            PayoutMethodId = payoutMethodId,
            Amount = amount,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.Pending,
            Method = CommerceHubPayoutMethod.BankTransfer,
            DestinationValueSnapshot = "EG-EXEC-001",
            IdempotencyKey = $"pending-{Guid.NewGuid():N}",
            ExternalTransferIdempotencyKey = $"CH-PAYOUT:{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    private async Task<PayoutOrderGraph> SeedEligibleOnlineOrderAsync(Guid vendorId, string orderNumber, string currency = "EGP")
    {
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            CustomerId = Guid.NewGuid(),
            FullName = "Payout Customer",
            PhoneNumber = "01000000000",
            AddressLine = "90 Treasury Street",
            City = "Cairo",
            Currency = currency,
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            Subtotal = 100m,
            ShippingTotal = 0m,
            TotalAmount = 100m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            PaidAt = DateTime.UtcNow.AddDays(-10),
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{orderNumber}-1",
            StoreId = vendorId,
            StoreName = "Payout Vendor",
            Currency = currency,
            Subtotal = 100m,
            Shipping = 0m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            PlatformCommissionAmount = 10m,
            VendorEarningAmount = 90m,
            PlatformCommissionRate = 0.1m,
            Total = 100m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            PaidAt = order.PaidAt,
            CreatedAt = order.CreatedAt
        };

        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Payout Product",
            Quantity = 1,
            UnitPrice = 100m,
            PlatformCommissionRate = 0.1m,
            PlatformCommissionAmount = 10m,
            Total = 100m,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = DateTime.UtcNow.AddDays(-9),
            CreatedAt = order.CreatedAt
        };

        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await _dbContext.SaveChangesAsync();

        return new PayoutOrderGraph(order, subOrder);
    }

    private void SeedPlatformCashOutflow(decimal amount)
    {
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Settlement,
                CommerceHubFinancialTransactionDirection.Debit,
                amount,
                "EGP",
                CommerceHubFinancialReferenceType.Payout,
                Guid.NewGuid(),
                "Synthetic platform cash outflow for payout safety test."));
    }

    private void SeedSyntheticLedgerBalanceWithoutSubOrderConfirmation(
        Guid vendorId,
        decimal vendorPayable,
        decimal platformCash,
        Guid orderId,
        Guid subOrderId)
    {
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                CommerceHubFinancialTransactionType.OrderPayment,
                CommerceHubFinancialTransactionDirection.Credit,
                vendorPayable,
                "EGP",
                CommerceHubFinancialReferenceType.Order,
                orderId,
                "Synthetic vendor payable without matching sub-order confirmation.",
                financialBucketOverride: CommerceHubFinancialBucket.Payable));

        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.OrderPayment,
                CommerceHubFinancialTransactionDirection.Credit,
                platformCash,
                "EGP",
                CommerceHubFinancialReferenceType.Order,
                orderId,
                "Synthetic platform cash without matching sub-order confirmation.",
                financialBucketOverride: CommerceHubFinancialBucket.Cash));
    }

    private sealed record PayoutOrderGraph(
        CommerceHubMarketplaceOrder Order,
        CommerceHubSubOrder SubOrder);

    private sealed class NoOpCommerceHubFinancialReadinessGate : ICommerceHubFinancialReadinessGate
    {
        public Task EnsureOutwardMoneyMovementAllowedAsync(
            Guid tenantId,
            string operationName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnsureCommerceIntakeAllowedAsync(
            IReadOnlyCollection<Guid> tenantIds,
            string operationName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
