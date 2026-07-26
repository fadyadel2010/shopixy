using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubDataAuthorityBoundaryTests
{
    [Fact]
    public async Task MarketplaceOrderStatus_CannotBeChangedOutsideOrderLifecycleBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        order.Status = OrderStatus.Processing;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("CommerceHub marketplace order lifecycle fields can only be changed", exception.Message);
    }

    [Fact]
    public async Task MarketplaceOrderStatus_CanBeChangedInsideOrderLifecycleBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            order.Status = OrderStatus.Processing;
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(OrderStatus.Processing, order.Status);
    }

    [Fact]
    public async Task SubOrderFinancialSnapshot_CannotBeChangedOutsideFinancialWorkflowBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            subOrder.CommissionSnapshotJson = """{"rate":0.20}""";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("CommerceHub sub-order financial snapshot fields can only be changed", exception.Message);
        }
    }

    [Fact]
    public async Task SubOrderFinancialSnapshot_CanBeChangedInsideFinancialWorkflowBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            subOrder.PlatformCommissionAmount = 12.50m;
            subOrder.FinalPlatformCommissionAmount = 12.50m;
            subOrder.VendorEarningAmount = 87.50m;
            subOrder.CommissionSnapshotJson = """{"rate":0.125}""";
            subOrder.IsEarningCreated = true;
            subOrder.EarningCreatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        Assert.True(subOrder.IsEarningCreated);
        Assert.Equal(87.50m, subOrder.VendorEarningAmount);
    }

    [Fact]
    public async Task SubOrderShippingAssignment_CannotBeChangedOutsideShippingAssignmentBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        subOrder.ShippingMethodId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("CommerceHub sub-order shipping assignment fields can only be changed", exception.Message);
    }

    [Fact]
    public async Task SubOrderShippingAssignment_CannotBeChangedEvenInsideShippingAssignmentBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await dbContext.SaveChangesAsync();
        }

        var replacementMethodId = Guid.NewGuid();
        using (CommerceHubBoundaryMutationScope.BeginShippingAssignment())
        {
            subOrder.ShippingMethodId = replacementMethodId;
            subOrder.Shipping = 25m;
            subOrder.ShippingMethodName = "Backend-resolved shipping";
            subOrder.ShippingSnapshotJson = "{\"shippingMethodId\":\"backend\"}";
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("CommerceHubSubOrder records are immutable", exception.Message);
        }
    }

    [Fact]
    public async Task PaymentFinancialIdentity_CannotBeChangedEvenInsidePaymentBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var payment = CreatePayment(order.Id);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubPayments.Add(payment);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            payment.Amount = 10m;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("CommerceHubPayment records are immutable", exception.Message);
            Assert.Contains(nameof(CommerceHubPayment.Amount), exception.Message);
        }
    }

    [Fact]
    public async Task PaymentLifecycleFields_CanBeChangedInsidePaymentBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var payment = CreatePayment(order.Id);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubPayments.Add(payment);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            payment.Status = CommerceHubPaymentRecordStatus.Paid;
            payment.ProviderIntentId = "pi-safe-flow";
            payment.ProviderTransactionId = "txn-safe-flow";
            payment.PaidAtUtc = DateTime.UtcNow;
            payment.IsProcessed = true;
            payment.ProcessedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(CommerceHubPaymentRecordStatus.Paid, payment.Status);
        Assert.True(payment.IsProcessed);
        Assert.Equal("txn-safe-flow", payment.ProviderTransactionId);
    }

    [Fact]
    public async Task PaymentCannotBeCreatedAsPaidOutsidePaymentBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var payment = CreatePayment(order.Id);
        payment.Status = CommerceHubPaymentRecordStatus.Paid;
        payment.ProviderTransactionId = "txn-boundary-bypass";
        payment.IsProcessed = true;
        payment.PaidAtUtc = DateTime.UtcNow;

        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubPayments.Add(payment);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("CommerceHub payments cannot be created outside the CommerceHub payment boundary", exception.Message);
        }
    }

    [Fact]
    public async Task PaymentCanBeCreatedAsPaidInsidePaymentBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var payment = CreatePayment(order.Id);
        payment.Status = CommerceHubPaymentRecordStatus.Paid;
        payment.ProviderTransactionId = "txn-boundary-safe";
        payment.IsProcessed = true;
        payment.PaidAtUtc = DateTime.UtcNow;

        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubPayments.Add(payment);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(CommerceHubPaymentRecordStatus.Paid, payment.Status);
    }

    [Fact]
    public async Task PaymentRefundFields_CanBeChangedInsideRefundBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var payment = CreatePayment(order.Id);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubPayments.Add(payment);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            payment.RefundedAmount = 25m;
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(25m, payment.RefundedAmount);
    }

    [Fact]
    public async Task RefundFinancialIdentity_CannotBeChangedEvenInsideRefundBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        var refund = CreateRefund(subOrder);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubRefunds.Add(refund);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            refund.Amount = 1m;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("CommerceHubRefund records are immutable", exception.Message);
            Assert.Contains(nameof(CommerceHubRefund.Amount), exception.Message);
        }
    }

    [Fact]
    public async Task RefundLifecycleFields_CanBeChangedInsideRefundBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        var refund = CreateRefund(subOrder);
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubRefunds.Add(refund);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            refund.Status = CommerceHubRefundStatus.Completed;
            refund.Reference = "refund-safe-flow";
            refund.GatewayStatus = "confirmed";
            refund.IsPostPayoutAdjustment = true;
            refund.ProcessingStartedAtUtc = DateTime.UtcNow.AddMinutes(-1);
            refund.ProcessedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(CommerceHubRefundStatus.Completed, refund.Status);
        Assert.True(refund.IsPostPayoutAdjustment);
        Assert.Equal("refund-safe-flow", refund.Reference);
    }

    [Fact]
    public async Task RefundCannotBeCreatedAsCompletedOutsideRefundBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        var refund = CreateRefund(subOrder);
        refund.Status = CommerceHubRefundStatus.Completed;
        refund.Reference = "refund-boundary-bypass";
        refund.ProcessedAtUtc = DateTime.UtcNow;

        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubRefunds.Add(refund);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("CommerceHub refunds cannot be created outside the CommerceHub refund boundary", exception.Message);
        }
    }

    [Fact]
    public async Task RefundCanBeCreatedAsCompletedInsideRefundBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        var refund = CreateRefund(subOrder);
        refund.Status = CommerceHubRefundStatus.Completed;
        refund.Reference = "refund-boundary-safe";
        refund.GatewayStatus = "confirmed";
        refund.ProcessedAtUtc = DateTime.UtcNow;

        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubRefunds.Add(refund);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(CommerceHubRefundStatus.Completed, refund.Status);
        Assert.Equal("refund-boundary-safe", refund.Reference);
    }

    [Fact]
    public async Task PayoutFinancialIdentity_CannotBeChangedEvenInsidePayoutBoundary()
    {
        await using var dbContext = CreateContext();
        var payout = CreatePayout();
        dbContext.CommerceHubPayouts.Add(payout);
        using (CommerceHubBoundaryMutationScope.BeginPayout())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginPayout())
        {
            payout.TenantId = Guid.NewGuid();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("CommerceHubPayout records are immutable", exception.Message);
            Assert.Contains(nameof(CommerceHubPayout.TenantId), exception.Message);
        }
    }

    [Fact]
    public async Task PayoutLifecycleFields_CanBeChangedInsidePayoutBoundary()
    {
        await using var dbContext = CreateContext();
        var payout = CreatePayout();
        dbContext.CommerceHubPayouts.Add(payout);
        using (CommerceHubBoundaryMutationScope.BeginPayout())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginPayout())
        {
            payout.Status = CommerceHubPayoutStatus.Completed;
            payout.Reference = "payout-safe-flow";
            payout.ExternalReferenceId = "external-safe-flow";
            payout.BankTransactionId = "bank-safe-flow";
            payout.ProcessedBy = "operator@example.test";
            payout.ProcessedAtUtc = DateTime.UtcNow;
            payout.CompletedAtUtc = DateTime.UtcNow;
            payout.CompletionEvidenceFingerprint = "fingerprint-safe-flow";
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(CommerceHubPayoutStatus.Completed, payout.Status);
        Assert.Equal("external-safe-flow", payout.ExternalReferenceId);
        Assert.Equal("fingerprint-safe-flow", payout.CompletionEvidenceFingerprint);
    }

    [Fact]
    public async Task VendorEarningPayoutReservation_CannotBeChangedOutsidePayoutBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        var earning = CreateVendorEarning(subOrder);

        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubVendorEarnings.Add(earning);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            earning.PayoutReservedAmount = 25m;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("payout reservation amount can only be changed", exception.Message);
        }
    }

    [Fact]
    public async Task VendorEarningPayoutReservation_CanBeChangedInsidePayoutBoundary()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        var earning = CreateVendorEarning(subOrder);

        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubVendorEarnings.Add(earning);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginPayout())
        {
            earning.PayoutReservedAmount = 25m;
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(25m, earning.PayoutReservedAmount);
    }

    [Fact]
    public async Task PayoutCannotBeCreatedAsCompletedOutsidePayoutBoundary()
    {
        await using var dbContext = CreateContext();
        var payout = CreatePayout();
        payout.Status = CommerceHubPayoutStatus.Completed;
        payout.ExternalReferenceId = "external-boundary-bypass";
        payout.ProcessedAtUtc = DateTime.UtcNow;
        payout.CompletedAtUtc = DateTime.UtcNow;

        dbContext.CommerceHubPayouts.Add(payout);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("CommerceHub payouts cannot be created outside the CommerceHub payout boundary", exception.Message);
    }

    [Fact]
    public async Task PayoutCanBeCreatedAsCompletedInsidePayoutBoundary()
    {
        await using var dbContext = CreateContext();
        var payout = CreatePayout();
        payout.Status = CommerceHubPayoutStatus.Completed;
        payout.ExternalReferenceId = "external-boundary-safe";
        payout.BankTransactionId = "bank-boundary-safe";
        payout.ProcessedAtUtc = DateTime.UtcNow;
        payout.CompletedAtUtc = DateTime.UtcNow;

        dbContext.CommerceHubPayouts.Add(payout);

        using (CommerceHubBoundaryMutationScope.BeginPayout())
        {
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(CommerceHubPayoutStatus.Completed, payout.Status);
        Assert.Equal("external-boundary-safe", payout.ExternalReferenceId);
    }

    [Fact]
    public async Task PaymentItemAllocation_CannotBeChangedAfterCreation()
    {
        await using var dbContext = CreateContext();
        var order = CreateMarketplaceOrder();
        var subOrder = CreateSubOrder(order.Id);
        var payment = CreatePayment(order.Id);
        var paymentItem = new CommerceHubPaymentItem
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            SubOrderId = subOrder.Id,
            Amount = 100m
        };
        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubPayments.Add(payment);
        dbContext.CommerceHubPaymentItems.Add(paymentItem);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await dbContext.SaveChangesAsync();
        }

        paymentItem.Amount = 90m;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("CommerceHubPaymentItem records are immutable", exception.Message);
        Assert.Contains(nameof(CommerceHubPaymentItem.Amount), exception.Message);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static CommerceHubMarketplaceOrder CreateMarketplaceOrder() =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = $"CH-{Guid.NewGuid():N}",
            FullName = "Authority Test",
            PhoneNumber = "01000000000",
            AddressLine = "1 Authority Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "PAYMOB",
            PaymentMethodName = "Paymob",
            Subtotal = 100m,
            ShippingTotal = 0m,
            TotalAmount = 100m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

    private static CommerceHubSubOrder CreateSubOrder(Guid marketplaceOrderId) =>
        new()
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = marketplaceOrderId,
            OrderNumber = $"CH-SUB-{Guid.NewGuid():N}",
            StoreId = Guid.NewGuid(),
            StoreName = "Authority Store",
            Currency = "EGP",
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "PAYMOB",
            PaymentMethodName = "Paymob",
            Subtotal = 100m,
            Shipping = 0m,
            Total = 100m,
            PlatformCommissionRate = 0.10m,
            PlatformCommissionAmount = 10m,
            FinalPlatformCommissionAmount = 10m,
            VendorEarningAmount = 90m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

    private static CommerceHubPayment CreatePayment(Guid orderId) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = 100m,
            Currency = "EGP",
            Status = CommerceHubPaymentRecordStatus.Pending,
            Provider = PaymentProvider.Paymob,
            ProviderReference = $"pay-{Guid.NewGuid():N}"
        };

    private static CommerceHubRefund CreateRefund(CommerceHubSubOrder subOrder) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 20m,
            CommissionReversalAmount = 2m,
            VendorEarningReversalAmount = 18m,
            Currency = subOrder.Currency,
            Status = CommerceHubRefundStatus.Pending,
            Reason = "Boundary test",
            IdempotencyKey = $"refund-{Guid.NewGuid():N}"
        };

    private static CommerceHubVendorEarning CreateVendorEarning(CommerceHubSubOrder subOrder) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 90m,
            Status = CommerceHubVendorEarningStatus.Available,
            AvailableAtUtc = DateTime.UtcNow
        };

    private static CommerceHubPayout CreatePayout() =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Amount = 100m,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.Pending,
            Method = CommerceHubPayoutMethod.Manual,
            IdempotencyKey = $"payout-{Guid.NewGuid():N}"
        };
}
