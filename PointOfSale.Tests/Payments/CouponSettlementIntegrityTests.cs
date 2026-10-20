using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Payments;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class CouponSettlementIntegrityTests
{
    [Theory]
    [InlineData(550, 27.5, 0, 522.5)]
    [InlineData(11000, 550, 0, 10450)]
    [InlineData(100, 5, 9.5, 104.5)]
    [InlineData(100, 100, 0, 0)]
    public void LineBalance_UsesAcceptedDiscountAndTaxExactlyOnce(decimal gross, decimal discount, decimal tax, decimal due)
    {
        var item = new OrderItem { UnitPrice = gross, Quantity = 1, DiscountAmount = discount, TaxAmount = tax, Total = due };
        Assert.Equal(due, item.RemainingAmount);
        item.PaidAmount = due;
        Assert.True(item.IsFullyPaid);
        Assert.Equal(0m, item.RemainingAmount);
        item.RefundedAmount = due / 2m;
        Assert.Equal(0m, item.RemainingAmount); // A refund is not a new collection obligation.
        Assert.Equal(due / 2m, item.NetPaidAmount);
    }

    [Fact]
    public void FullCouponPayment_PassesSharedGuard_AllocatesOnlyNetLines_LeavesShippingAtOrderLevel()
    {
        var order = CouponOrder();
        var guard = typeof(PaymentLifecycleService).GetMethod("ValidateSuccessfulAmount", BindingFlags.NonPublic | BindingFlags.Static)!;
        guard.Invoke(null, [order, 532.5m]);
        var allocate = typeof(PaymentLifecycleService).GetMethod("ApplySuccessfulPaymentAllocation", BindingFlags.NonPublic | BindingFlags.Static)!;
        allocate.Invoke(null, [order, 532.5m]);
        Assert.Equal(522.5m, order.TotalPaidAmount);
        Assert.All(order.Items, i => Assert.True(i.IsFullyPaid));
        Assert.Equal(0m, order.Items.Sum(i => i.RemainingAmount));
    }

    [Fact]
    public void PublicAllocator_UsesSameNetBalance_AndDoesNotChangeRefundHistory()
    {
        var order = CouponOrder();
        var allocator = new PaymentAllocationService(NullLogger<PaymentAllocationService>.Instance);
        var first = allocator.AllocatePayment(order, 100m);
        var final = allocator.AllocatePayment(order, 432.5m);
        Assert.Equal(100m, first.TotalAllocated);
        Assert.Equal(422.5m, final.TotalAllocated);
        Assert.Equal(10m, final.AmountRemaining);
        Assert.True(final.AllItemsFullyPaid);
        Assert.Equal(27.5m, order.Items.Sum(i => i.DiscountAmount));
        Assert.Equal(30m, allocator.AllocateRefund(order, 30m).TotalRefunded);
        Assert.Equal(0m, order.Items.Sum(i => i.RemainingAmount));
        Assert.Equal(492.5m, order.NetPaidAmount);
    }

    [Theory]
    [InlineData(522.5, "PAYMENT_INSUFFICIENT_AMOUNT")]
    [InlineData(532.49, "PAYMENT_INSUFFICIENT_AMOUNT")]
    [InlineData(532.5, null)]
    [InlineData(550, "PAYMENT_EXCESS_AMOUNT")]
    public async Task ManualReview_UsesFinalPayable_IncludingShipping_NotGrossItems(decimal amount, string? failureCode)
    {
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        var order = CouponOrder(tenant);
        var result = await Guard(db, order, null, amount);
        Assert.Equal(failureCode, result?.FailureCode);
        if (failureCode == "PAYMENT_INSUFFICIENT_AMOUNT") Assert.Contains("532.50", result!.ErrorMessage);
        Assert.Equal(0m, order.TotalPaidAmount);
        Assert.Empty(db.AccountingOutboxMessages.Local);
    }

    [Fact]
    public async Task ManualReview_RecognizesPriorSameTenantCurrencyPaymentsOnce_ExcludesCurrentForeignAndFailed()
    {
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        var order = CouponOrder(tenant);
        order.Items.Single().PaidAmount = 100m;
        var current = Intent(tenant, order.Id, 432.5m, PaymentIntentStatus.AwaitingVerification);
        db.PaymentIntents.AddRange(current, Intent(tenant, order.Id, 100m, PaymentIntentStatus.Paid),
            Intent(Guid.NewGuid(), order.Id, 999m, PaymentIntentStatus.Paid),
            Intent(tenant, order.Id, 999m, PaymentIntentStatus.Failed));
        await db.SaveChangesAsync();
        Assert.Null(await Guard(db, order, current, 432.5m));
        Assert.Equal("PAYMENT_INSUFFICIENT_AMOUNT", (await Guard(db, order, current, 422.5m))?.FailureCode);
        Assert.Equal(100m, order.TotalPaidAmount);
    }

    private static PaymentIntent Intent(Guid tenant, Guid orderId, decimal amount, PaymentIntentStatus status) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, OrderId = orderId, Amount = amount, Currency = "EGP", Status = status,
        PaymentReference = Guid.NewGuid().ToString("N"), Provider = PaymentProvider.ManualWallet
    };

    private static Task<PaymentTransitionResult?> Guard(ApplicationDbContext db, Order order, PaymentIntent? intent, decimal amount) =>
        (Task<PaymentTransitionResult?>)typeof(PaymentLifecycleService)
            .GetMethod("ValidateManualWalletSettlementAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ManualWalletConfiguredLifecycleTests.Lifecycle(db), [order, intent, amount, CancellationToken.None])!;

    private static Order CouponOrder(Guid tenant = default) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "SYNTHETIC-COUPON", Currency = "EGP", Subtotal = 550m,
        DiscountAmount = 27.5m, TotalDiscountAmount = 27.5m, ShippingCost = 10m, Total = 532.5m,
        Items = [new OrderItem { Id = Guid.NewGuid(), UnitPrice = 550m, Quantity = 1, DiscountAmount = 27.5m, Total = 522.5m }]
    };
}
