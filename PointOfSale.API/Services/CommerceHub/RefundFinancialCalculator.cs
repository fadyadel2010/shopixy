using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.CommerceHub;

internal sealed record RefundFinancialBreakdown(
    decimal GrossCommissionReversalAmount,
    decimal CommissionReversalAmount,
    decimal PromoDiscountReversalAmount,
    decimal VendorEarningReversalAmount);

internal static class RefundFinancialCalculator
{
    public static RefundFinancialBreakdown CalculateItemBreakdown(
        CommerceHubSubOrderItem orderItem,
        int quantity)
    {
        var refundAmount = CalculateRefundAmount(orderItem, quantity);
        if (quantity <= 0 || orderItem.Quantity <= 0 || refundAmount <= 0m)
        {
            return new RefundFinancialBreakdown(0m, 0m, 0m, 0m);
        }

        var grossCommission = Prorate(
            orderItem.PlatformCommissionAmount,
            quantity,
            orderItem.Quantity);
        var finalCommission = Prorate(
            ResolveFinalCommissionAmount(orderItem.FinalPlatformCommissionAmount, orderItem.PlatformCommissionAmount),
            quantity,
            orderItem.Quantity);
        var promoDiscount = Prorate(orderItem.PromoDiscountAmount, quantity, orderItem.Quantity);

        return new RefundFinancialBreakdown(
            GrossCommissionReversalAmount: grossCommission,
            CommissionReversalAmount: finalCommission,
            PromoDiscountReversalAmount: promoDiscount,
            VendorEarningReversalAmount: RoundCurrency(Math.Max(0m, refundAmount - grossCommission)));
    }

    public static RefundFinancialBreakdown CalculateSubOrderBreakdown(
        CommerceHubSubOrder subOrder,
        decimal refundedAmountSoFar,
        decimal refundAmount,
        bool isFullRefund)
    {
        if (refundAmount <= 0m)
        {
            return new RefundFinancialBreakdown(0m, 0m, 0m, 0m);
        }

        var grossCommission = RoundCurrency(Math.Max(0m, subOrder.PlatformCommissionAmount));
        var finalCommission = RoundCurrency(ResolveFinalCommissionAmount(
            subOrder.FinalPlatformCommissionAmount,
            subOrder.PlatformCommissionAmount));
        var promoDiscount = RoundCurrency(Math.Max(0m, subOrder.PromoDiscountAmount));

        if (grossCommission <= 0m)
        {
            return new RefundFinancialBreakdown(
                0m,
                0m,
                0m,
                RoundCurrency(refundAmount));
        }

        if (isFullRefund || subOrder.Total <= 0m)
        {
            return new RefundFinancialBreakdown(
                GrossCommissionReversalAmount: grossCommission,
                CommissionReversalAmount: finalCommission,
                PromoDiscountReversalAmount: promoDiscount,
                VendorEarningReversalAmount: RoundCurrency(Math.Max(0m, refundAmount - grossCommission)));
        }

        if (subOrder.Subtotal <= 0m)
        {
            return new RefundFinancialBreakdown(
                0m,
                0m,
                0m,
                RoundCurrency(refundAmount));
        }

        var commissionableRefundedBefore = Math.Clamp(refundedAmountSoFar - subOrder.Shipping, 0m, subOrder.Subtotal);
        var commissionableRefundedAfter = Math.Clamp(refundedAmountSoFar + refundAmount - subOrder.Shipping, 0m, subOrder.Subtotal);

        var grossBefore = RoundCurrency(grossCommission * (commissionableRefundedBefore / subOrder.Subtotal));
        var grossAfter = RoundCurrency(grossCommission * (commissionableRefundedAfter / subOrder.Subtotal));
        var finalBefore = RoundCurrency(finalCommission * (commissionableRefundedBefore / subOrder.Subtotal));
        var finalAfter = RoundCurrency(finalCommission * (commissionableRefundedAfter / subOrder.Subtotal));
        var promoBefore = RoundCurrency(promoDiscount * (commissionableRefundedBefore / subOrder.Subtotal));
        var promoAfter = RoundCurrency(promoDiscount * (commissionableRefundedAfter / subOrder.Subtotal));

        var grossDelta = Math.Max(0m, RoundCurrency(grossAfter - grossBefore));
        var finalDelta = Math.Max(0m, RoundCurrency(finalAfter - finalBefore));
        var promoDelta = Math.Max(0m, RoundCurrency(promoAfter - promoBefore));

        return new RefundFinancialBreakdown(
            GrossCommissionReversalAmount: grossDelta,
            CommissionReversalAmount: finalDelta,
            PromoDiscountReversalAmount: promoDelta,
            VendorEarningReversalAmount: RoundCurrency(Math.Max(0m, refundAmount - grossDelta)));
    }

    public static decimal CalculateCommissionReversal(
        CommerceHubSubOrderItem orderItem,
        int quantity)
    {
        return CalculateItemBreakdown(orderItem, quantity).CommissionReversalAmount;
    }

    public static decimal CalculateVendorEarningReversal(
        CommerceHubSubOrderItem orderItem,
        int quantity)
    {
        return CalculateItemBreakdown(orderItem, quantity).VendorEarningReversalAmount;
    }

    public static decimal CalculateRefundAmount(
        CommerceHubSubOrderItem orderItem,
        int quantity)
    {
        if (quantity <= 0)
        {
            return 0m;
        }

        return RoundCurrency(orderItem.UnitPrice * quantity);
    }

    public static decimal RoundCurrency(decimal amount) =>
        CommissionCalculator.RoundAmount(amount);

    private static decimal Prorate(decimal amount, int quantity, int totalQuantity)
    {
        if (quantity <= 0 || totalQuantity <= 0 || amount <= 0m)
        {
            return 0m;
        }

        if (quantity >= totalQuantity)
        {
            return RoundCurrency(amount);
        }

        return RoundCurrency(amount * quantity / totalQuantity);
    }

    private static decimal ResolveFinalCommissionAmount(decimal storedFinalAmount, decimal grossAmount)
    {
        if (storedFinalAmount > 0m)
        {
            return storedFinalAmount;
        }

        return grossAmount;
    }
}
