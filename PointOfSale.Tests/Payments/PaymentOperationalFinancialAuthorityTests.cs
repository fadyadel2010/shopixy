using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentOperationalFinancialAuthorityTests
{
    [Fact]
    public void PartiallyRefundedPayment_WithPostedCollectionAccounting_RemainsEligibleForFulfillment()
    {
        var order = new Order
        {
            Total = 100m,
            PaymentMethodCode = "STRIPE",
            PaymentStatus = PaymentStatus.PartiallyRefunded,
            FinancialPostingStatus = FinancialPostingStatus.Posted
        };

        Assert.False(OrderOperationalFinancialAuthority.RequiresRecognizedPaymentBeforeOutboundProgress(order));
        Assert.False(OrderOperationalFinancialAuthority.RequiresPostedCollectionAccountingBeforeOutboundProgress(order));
        Assert.Null(OrderOperationalFinancialAuthority.GetOutboundProgressBlockReason(order));
        Assert.Null(OrderOperationalFinancialAuthority.GetCompletionBlockReason(order));
    }

    [Fact]
    public void FullyRefundedPayment_RemainsBlockedFromOutboundFulfillment()
    {
        var order = new Order
        {
            Total = 100m,
            PaymentMethodCode = "STRIPE",
            PaymentStatus = PaymentStatus.Refunded,
            FinancialPostingStatus = FinancialPostingStatus.Posted
        };

        Assert.True(OrderOperationalFinancialAuthority.RequiresRecognizedPaymentBeforeOutboundProgress(order));
        Assert.NotNull(OrderOperationalFinancialAuthority.GetCompletionBlockReason(order));
    }
}
