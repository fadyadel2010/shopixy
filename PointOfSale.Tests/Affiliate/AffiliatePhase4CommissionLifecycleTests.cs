using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Tests.Affiliate;

/// <summary>Executable domain/service tests, isolated from the configured development database.
/// InMemory proves decisions and replay calculations, not relational locking or rollback.</summary>
public sealed class AffiliatePhase4CommissionLifecycleTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly TenantContext Tenant = new();
        public readonly ApplicationDbContext Db;
        public readonly Mock<IEventPublisher> Events = new();
        public readonly AffiliateAccount Affiliate;
        public readonly Order Order;
        public readonly OrderItem Item;
        public readonly AffiliateService Service;

        public Fixture(PaymentStatus payment = PaymentStatus.Collected, bool delivered = false, bool foreignAffiliate = false,
            decimal price = 100m, int quantity = 1, decimal rate = .10m, decimal? cap = null, decimal discount = 0m, decimal tax = 0m,
            decimal? forgedCommission = null, bool unattributed = false, bool cancelled = false, int lines = 1, bool manualDispatched = false,
            bool foreignPolicy = false)
        {
            Tenant.SetTenantId(Guid.NewGuid());
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options, Tenant);
            Affiliate = AffiliateAccount.Create(Guid.Empty, Guid.NewGuid(), "PHASE4QA", "Synthetic Phase 4");
            Affiliate.Id = Guid.NewGuid();
            Affiliate.Activate();
            Order = new Order { Id = Guid.NewGuid(), TenantId = Tenant.TenantId!.Value, OrderNumber = "AFFILIATE_PHASE4_20261021_TEST",
                AffiliateAccountId = foreignAffiliate ? Guid.NewGuid() : Affiliate.Id, AffiliateReferralCode = Affiliate.ReferralCode, Currency = "EGP",
                PaymentMethodCode = "COD", PaymentStatus = payment, Total = 100m,
                FinancialPostingStatus = FinancialPostingStatus.Posted, FulfillmentMode = FulfillmentMode.Carrier,
                FulfillmentStatus = delivered ? FulfillmentStatus.Delivered : FulfillmentStatus.Created,
                Status = delivered ? OrderStatus.Delivered : OrderStatus.Pending };
            if (cancelled) Order.Status = OrderStatus.Cancelled;
            if (unattributed) { Order.AffiliateAccountId = null; Order.AffiliateReferralCode = null; }
            if (manualDispatched) { Order.FulfillmentMode = FulfillmentMode.Manual; Order.FulfillmentStatus = FulfillmentStatus.OutForDelivery; Order.Status = OrderStatus.Shipped; }
            var product = new Product { Id = Guid.NewGuid(), TenantId = Order.TenantId, Name = "Synthetic Phase 4" };
            var policy = AffiliateProductPolicy.Create(foreignPolicy ? Guid.NewGuid() : Order.TenantId, product.Id, rate, cap, Guid.NewGuid());
            policy.Id = Guid.NewGuid();
            var total = Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero) - discount + tax;
            Item = new OrderItem { Id = Guid.NewGuid(), OrderId = Order.Id, ProductId = product.Id, Quantity = quantity,
                UnitPrice = price, DiscountAmount = discount, TaxAmount = tax, Total = total,
                PaidAmount = payment == PaymentStatus.Collected ? total : 0m,
                AffiliateCommissionEligible = true, AffiliateCommissionPolicyIdSnapshot = policy.Id,
                AffiliateCommissionRateSnapshot = rate, AffiliateMaxCommissionPerItemSnapshot = cap,
                AffiliateCommissionBaseAmountSnapshot = total,
                AffiliateCommissionAmountSnapshot = forgedCommission ?? Math.Round(cap.HasValue ? Math.Min(total * rate, cap.Value * quantity) : total * rate, 4, MidpointRounding.ToZero),
                AffiliateCommissionSnapshotCapturedAtUtc = DateTime.UtcNow };
            Order.Items.Add(Item);
            for (var i = 1; i < lines; i++)
                Order.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = Order.Id, ProductId = product.Id, Quantity = quantity,
                    UnitPrice = price, Total = total, DiscountAmount = discount, TaxAmount = tax,
                    AffiliateCommissionEligible = true, AffiliateCommissionPolicyIdSnapshot = policy.Id,
                    AffiliateCommissionRateSnapshot = rate, AffiliateMaxCommissionPerItemSnapshot = cap,
                    AffiliateCommissionBaseAmountSnapshot = total, AffiliateCommissionAmountSnapshot = Item.AffiliateCommissionAmountSnapshot,
                    AffiliateCommissionSnapshotCapturedAtUtc = DateTime.UtcNow });
            Db.AddRange(product, policy, Affiliate, Order);
            if (!unattributed) Db.Add(AffiliateAttribution.Create(Order.TenantId, Order.Id, Affiliate.Id, Affiliate.ReferralCode, AttributionSource.ReferralLink));
            Db.SaveChanges();
            Events.SetReturnsDefault(Task.CompletedTask);
            Service = new AffiliateService(Db, Tenant, Events.Object, new AffiliateTelemetry(), NullLogger<AffiliateService>.Instance,
                Mock.Of<IAffiliatePayoutService>(MockBehavior.Strict), Mock.Of<IAffiliateSettlementOrchestrator>(MockBehavior.Strict),
                Options.Create(new AffiliateSettlementOptions()));
        }

        public Task<List<AffiliateCommissionLedger>> Ledger() => Db.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, false)]
    [InlineData(PaymentStatus.Pending, true)]
    [InlineData(PaymentStatus.CashOnDelivery, false)]
    [InlineData(PaymentStatus.AwaitingVerification, false)]
    [InlineData(PaymentStatus.Authorized, false)]
    [InlineData(PaymentStatus.Rejected, true)]
    [InlineData(PaymentStatus.Refunded, true)]
    [InlineData(PaymentStatus.Expired, false)]
    [InlineData(PaymentStatus.AmountMismatch, true)]
    public async Task UncollectedOrder_NeverEarns(PaymentStatus status, bool delivered)
    {
        await using var f = new Fixture(status, delivered);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        Assert.Empty(await f.Ledger());
    }

    [Fact]
    public async Task CollectedUndelivered_IsPendingNotAvailable_AndReplayIsStable()
    {
        await using var f = new Fixture();
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        var earned = Assert.Single(await f.Ledger());
        Assert.Equal(CommissionEntryType.Earned, earned.EntryType);
        Assert.Equal(10m, earned.CommissionAmount);
        var balance = await new AffiliatePayoutService(f.Db, NullLogger<AffiliatePayoutService>.Instance).GetAvailableBalanceAsync(f.Affiliate.Id);
        Assert.Equal(0m, balance.RawAvailable);
        Assert.Empty(await f.Db.AffiliateBalanceLedger.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task DeliveredCollected_ApprovesExactlyOnce_WithLinkedSqlAvailability()
    {
        await using var f = new Fixture(delivered: true);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        var rows = await f.Ledger();
        var earned = Assert.Single(rows.Where(e => e.EntryType == CommissionEntryType.Earned));
        var approved = Assert.Single(rows.Where(e => e.EntryType == CommissionEntryType.Approved));
        Assert.Equal(earned.Id, approved.OriginalEntryId);
        Assert.Equal(f.Order.TenantId, earned.StoreTenantId);
        Assert.Equal(f.Item.Id, earned.OrderItemId);
        Assert.Equal(f.Affiliate.Id, earned.AffiliateAccountId);
        Assert.Null(earned.TenantId);
        var balance = await new AffiliatePayoutService(f.Db, NullLogger<AffiliatePayoutService>.Instance).GetAvailableBalanceAsync(f.Affiliate.Id);
        Assert.Equal(10m, balance.RawAvailable);
        Assert.Empty(await f.Db.AffiliateBalanceLedger.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await f.Db.AffiliatePayoutRequests.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task PartialRefund_ReconciliationReplay_DoesNotTreatCumulativeAsAnotherDelta()
    {
        await using var f = new Fixture(delivered: true);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        f.Item.RefundedAmount = 25m;
        await f.Db.SaveChangesAsync();
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        var reversal = Assert.Single((await f.Ledger()).Where(e => e.EntryType == CommissionEntryType.Reversed));
        Assert.Equal(25m, reversal.BaseAmount);
        Assert.Equal(2.5m, reversal.CommissionAmount);
        var balance = await new AffiliatePayoutService(f.Db, NullLogger<AffiliatePayoutService>.Instance).GetAvailableBalanceAsync(f.Affiliate.Id);
        Assert.Equal(7.5m, balance.RawAvailable);
    }

    [Fact]
    public async Task RefundEventAfterSynchronousReversal_CannotReverseAgainUnderAnotherEventKey()
    {
        await using var f = new Fixture(delivered: true);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        f.Item.RefundedAmount = 25m;
        await f.Db.SaveChangesAsync();
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.ReverseCommissionForOrderItemAsync(f.Order.TenantId, f.Order.Id, f.Item.Id,
            refundedBaseAmount: 25m, refundIdempotencyKey: "different-delivery-of-the-same-refund");
        Assert.Equal(2.5m, (await f.Ledger()).Where(e => e.EntryType == CommissionEntryType.Reversed).Sum(e => e.CommissionAmount));
    }

    [Fact]
    public async Task ForgedRefundWithoutSqlRefundAuthority_CannotReduceBalance()
    {
        await using var f = new Fixture(delivered: true);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.ReverseCommissionForOrderItemAsync(f.Order.TenantId, f.Order.Id, f.Item.Id,
            refundedBaseAmount: 100m, refundIdempotencyKey: "forged-event");
        Assert.DoesNotContain(await f.Ledger(), e => e.EntryType == CommissionEntryType.Reversed);
    }

    [Fact]
    public void AttributionCannotRedirectCommissionToForeignAffiliate()
    {
        // The established save boundary rejects this before a commission operation can run.
        var error = Assert.Throws<InvalidOperationException>(() => new Fixture(foreignAffiliate: true));
        Assert.Contains("does not match its tenant-owned order authority snapshot", error.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnattributedOrCancelled_CannotEarn(bool unattributed, bool cancelled)
    {
        await using var f = new Fixture(unattributed: unattributed, cancelled: cancelled);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        Assert.Empty(await f.Ledger());
    }

    [Theory]
    [InlineData(12.37, 3, .123456, null, 2.11, 1.27, 4.4777)]
    [InlineData(100, 3, .10, 4.0, 0, 0, 12)]
    [InlineData(100, 1, .10, null, 25, 7, 8.2)]
    public async Task CheckoutFormula_UsesQuantitiesAllocatedDiscountTaxAndPerUnitCap(decimal price, int quantity,
        decimal rate, double? cap, decimal discount, decimal tax, decimal expected)
    {
        await using var f = new Fixture(price: price, quantity: quantity, rate: rate, cap: (decimal?)cap, discount: discount, tax: tax);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        Assert.Equal(expected, Assert.Single(await f.Ledger()).CommissionAmount);
    }

    [Fact]
    public async Task MultipleLines_RecognizeOncePerItem_NotOncePerOrder()
    {
        await using var f = new Fixture(delivered: true, lines: 2);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        var rows = await f.Ledger();
        Assert.Equal(2, rows.Count(e => e.EntryType == CommissionEntryType.Earned));
        Assert.Equal(20m, rows.Where(e => e.EntryType == CommissionEntryType.Approved).Sum(e => e.CommissionAmount));
    }

    [Fact]
    public async Task SnapshotAmountsCannotOverrideFormula()
    {
        await using var f = new Fixture(forgedCommission: 50m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RecordCommissionForOrderAsync(f.Order.Id));
        Assert.Empty(await f.Ledger());
    }

    [Fact]
    public async Task CurrentPolicyAndLaterSuspension_DoNotRewriteHistoricalAdmission()
    {
        await using var f = new Fixture();
        var policy = await f.Db.AffiliateProductPolicies.IgnoreQueryFilters().SingleAsync();
        policy.UpdateRate(.20m, 1m); policy.Disable();
        f.Affiliate.Suspend("Synthetic post-checkout suspension");
        await f.Db.SaveChangesAsync();
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        Assert.Equal(10m, Assert.Single(await f.Ledger()).CommissionAmount);
    }

    [Fact]
    public async Task FullRefundAfterPartial_ConvergesToOriginalAmountAndCannotReactivate()
    {
        await using var f = new Fixture(delivered: true);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        f.Item.RefundedAmount = 25m; await f.Db.SaveChangesAsync();
        await f.Service.ReverseCommissionForOrderItemAsync(f.Order.TenantId, f.Order.Id, f.Item.Id);
        f.Item.RefundedAmount = 100m; await f.Db.SaveChangesAsync();
        await f.Service.ReverseCommissionForOrderItemAsync(f.Order.TenantId, f.Order.Id, f.Item.Id);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.ReverseCommissionForOrderItemAsync(f.Order.TenantId, f.Order.Id, f.Item.Id);
        var rows = await f.Ledger();
        Assert.Equal(10m, rows.Where(e => e.EntryType == CommissionEntryType.Reversed).Sum(e => e.CommissionAmount));
        var balance = await new AffiliatePayoutService(f.Db, NullLogger<AffiliatePayoutService>.Instance).GetAvailableBalanceAsync(f.Affiliate.Id);
        Assert.Equal(0m, balance.RawAvailable);
    }

    [Fact]
    public async Task ForeignRefundTenantOrItem_CannotTouchOriginalCommission()
    {
        await using var f = new Fixture(delivered: true);
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        f.Item.RefundedAmount = 100m; await f.Db.SaveChangesAsync();
        await f.Service.ReverseCommissionForOrderItemAsync(Guid.NewGuid(), f.Order.Id, f.Item.Id);
        await f.Service.ReverseCommissionForOrderItemAsync(f.Order.TenantId, f.Order.Id, Guid.NewGuid());
        Assert.DoesNotContain(await f.Ledger(), e => e.EntryType == CommissionEntryType.Reversed);
    }

    [Fact]
    public async Task ManualDeliveryEvidence_SurvivesReload_AndCannotBeReplaced()
    {
        await using var f = new Fixture(unattributed: true, payment: PaymentStatus.Pending, manualDispatched: true);
        var service = new OrderFulfillmentService(f.Db, f.Events.Object, NullLogger<OrderFulfillmentService>.Instance);
        var result = await service.MarkDeliveredAsync(f.Order.Id, FulfillmentRequestSource.Manual,
            "  AFFILIATE_PHASE4_20261021_PROOF  ", "  https://example.invalid/phase4-proof  ");
        Assert.True(result.Success, result.ErrorMessage);
        f.Db.ChangeTracker.Clear();
        var persisted = await f.Db.Orders.SingleAsync(o => o.Id == f.Order.Id);
        Assert.Equal("AFFILIATE_PHASE4_20261021_PROOF", persisted.DeliveryReference);
        Assert.Equal("https://example.invalid/phase4-proof", persisted.ProofOfDeliveryUrl);
        var conflict = await service.MarkDeliveredAsync(f.Order.Id, FulfillmentRequestSource.Manual, "changed", "https://example.invalid/changed");
        Assert.False(conflict.Success);
        Assert.Contains("EVIDENCE_CONFLICT", conflict.ErrorMessage);
        Assert.Empty(await f.Ledger());
    }

    [Fact]
    public async Task ForeignPolicyCannotBeUsedForOriginatingMerchantsLine()
    {
        await using var f = new Fixture(foreignPolicy: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RecordCommissionForOrderAsync(f.Order.Id));
        Assert.Empty(await f.Ledger());
    }

    [Fact]
    public async Task DeletedProfile_CannotCreateNewEarnings()
    {
        await using var f = new Fixture();
        f.Affiliate.IsDeleted = true;
        await f.Db.SaveChangesAsync();
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        Assert.Empty(await f.Ledger());
    }

    [Fact]
    public async Task GlobalAffiliateAggregatesDifferentMerchants_WithoutMergingTheirLiability()
    {
        await using var f = new Fixture(delivered: true);
        var secondTenant = Guid.NewGuid();
        f.Db.Tenants.AddRange(new Tenant { Id = f.Order.TenantId, Name = "First", Slug = "first", Currency = "EGP" },
            new Tenant { Id = secondTenant, Name = "Second", Slug = "second", Currency = "EGP" });
        var product = new Product { Id = Guid.NewGuid(), TenantId = secondTenant, Name = "Second merchant product" };
        var policy = AffiliateProductPolicy.Create(secondTenant, product.Id, .20m, null, Guid.NewGuid()); policy.Id = Guid.NewGuid();
        var order = new Order { Id = Guid.NewGuid(), TenantId = secondTenant, OrderNumber = "AFFILIATE_PHASE4_20261021_SECOND",
            AffiliateAccountId = f.Affiliate.Id, AffiliateReferralCode = f.Affiliate.ReferralCode,
            PaymentStatus = PaymentStatus.Collected, FinancialPostingStatus = FinancialPostingStatus.Posted,
            FulfillmentMode = FulfillmentMode.Carrier, FulfillmentStatus = FulfillmentStatus.Delivered, Status = OrderStatus.Delivered,
            Currency = "EGP", Total = 150m };
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = product.Id, Quantity = 1, UnitPrice = 150m, Total = 150m,
            AffiliateCommissionEligible = true, AffiliateCommissionPolicyIdSnapshot = policy.Id, AffiliateCommissionRateSnapshot = .20m,
            AffiliateCommissionBaseAmountSnapshot = 150m, AffiliateCommissionAmountSnapshot = 30m, AffiliateCommissionSnapshotCapturedAtUtc = DateTime.UtcNow });
        f.Db.AddRange(product, policy, order, AffiliateAttribution.Create(secondTenant, order.Id, f.Affiliate.Id, f.Affiliate.ReferralCode, AttributionSource.ReferralLink));
        await f.Db.SaveChangesAsync();
        await f.Service.RecordCommissionForOrderAsync(f.Order.Id);
        await f.Service.RecordCommissionForOrderAsync(order.Id);
        var balance = new AffiliatePayoutService(f.Db, NullLogger<AffiliatePayoutService>.Instance);
        Assert.Equal(40m, (await balance.GetAvailableBalanceAsync(f.Affiliate.Id)).RawAvailable);
        Assert.Equal(10m, (await balance.GetAvailableBalanceAsync(f.Affiliate.Id, f.Order.TenantId)).RawAvailable);
        Assert.Equal(30m, (await balance.GetAvailableBalanceAsync(f.Affiliate.Id, secondTenant)).RawAvailable);
        Assert.Equal(0m, (await balance.GetAvailableBalanceAsync(Guid.NewGuid())).RawAvailable);
        Assert.All(await f.Ledger(), e => Assert.Equal(f.Affiliate.Id, e.AffiliateAccountId));
    }

    [Fact]
    public void CheckoutDiscountAllocation_UsesDecimalLargestRemainders_AndPreservesExactTotal()
    {
        var method = typeof(PointOfSale.API.Services.Checkout.CheckoutService).GetMethod("AllocateAmountsByLineSubtotal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var items = new List<PointOfSale.API.DTOs.Checkout.CartItemSnapshot>
        {
            new() { CurrentPrice = 1m, Quantity = 1 }, new() { CurrentPrice = 1m, Quantity = 1 }, new() { CurrentPrice = 1m, Quantity = 1 }
        };
        var allocation = Assert.IsAssignableFrom<IReadOnlyList<decimal>>(method.Invoke(null, new object[] { items, 1m, true }));
        Assert.Equal(new[] { .34m, .33m, .33m }, allocation);
        Assert.Equal(1m, allocation.Sum());
    }
}
