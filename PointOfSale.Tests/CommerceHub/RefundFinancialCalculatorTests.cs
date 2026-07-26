using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.CommerceHub;

public sealed class RefundFinancialCalculatorTests
{
    [Fact]
    public void CalculateItemBreakdown_ShouldProratePromoDiscountByRefundedQuantity()
    {
        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            Quantity = 5,
            UnitPrice = 20m,
            Total = 100m,
            PlatformCommissionAmount = 10m,
            FinalPlatformCommissionAmount = 7.5m,
            PromoDiscountAmount = 2.5m
        };

        var breakdown = RefundFinancialCalculator.CalculateItemBreakdown(item, quantity: 2);

        Assert.Equal(4m, breakdown.GrossCommissionReversalAmount);
        Assert.Equal(3m, breakdown.CommissionReversalAmount);
        Assert.Equal(1m, breakdown.PromoDiscountReversalAmount);
        Assert.Equal(36m, breakdown.VendorEarningReversalAmount);
    }

    [Fact]
    public void CalculateSubOrderBreakdown_ShouldReversePromoDiscountProportionallyAcrossPartialRefunds()
    {
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CH-REF-PROP-1-A",
            Currency = "EGP",
            Subtotal = 100m,
            Shipping = 0m,
            Total = 100m,
            PlatformCommissionAmount = 10m,
            FinalPlatformCommissionAmount = 7m,
            PromoDiscountAmount = 3m,
            VendorEarningAmount = 90m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Paid
        };

        var first = RefundFinancialCalculator.CalculateSubOrderBreakdown(
            subOrder,
            refundedAmountSoFar: 0m,
            refundAmount: 30m,
            isFullRefund: false);
        var second = RefundFinancialCalculator.CalculateSubOrderBreakdown(
            subOrder,
            refundedAmountSoFar: 30m,
            refundAmount: 20m,
            isFullRefund: false);

        Assert.Equal(3m, first.GrossCommissionReversalAmount);
        Assert.Equal(2.1m, first.CommissionReversalAmount);
        Assert.Equal(0.9m, first.PromoDiscountReversalAmount);
        Assert.Equal(27m, first.VendorEarningReversalAmount);

        Assert.Equal(2m, second.GrossCommissionReversalAmount);
        Assert.Equal(1.4m, second.CommissionReversalAmount);
        Assert.Equal(0.6m, second.PromoDiscountReversalAmount);
        Assert.Equal(18m, second.VendorEarningReversalAmount);
    }
}
