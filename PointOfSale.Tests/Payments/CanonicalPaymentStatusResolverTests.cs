using PointOfSale.API.Services.Payments;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Payments;

public sealed class CanonicalPaymentStatusResolverTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending, null, CanonicalPaymentStatusResolver.Pending)]
    [InlineData(PaymentStatus.Authorized, null, CanonicalPaymentStatusResolver.Processing)]
    [InlineData(PaymentStatus.Failed, null, CanonicalPaymentStatusResolver.Failed)]
    [InlineData(PaymentStatus.Collected, null, CanonicalPaymentStatusResolver.Paid)]
    [InlineData(PaymentStatus.AmountMismatch, null, CanonicalPaymentStatusResolver.ManualReview)]
    [InlineData(PaymentStatus.Refunded, null, CanonicalPaymentStatusResolver.Failed)]
    [InlineData(PaymentStatus.PartiallyRefunded, null, CanonicalPaymentStatusResolver.Failed)]
    public void Resolve_Maps_OrderPaymentStates_ToCanonicalContract(
        PaymentStatus paymentStatus,
        PaymentIntentStatus? paymentIntentStatus,
        string expected)
    {
        var actual = CanonicalPaymentStatusResolver.Resolve(paymentStatus, paymentIntentStatus);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentIntentStatus.Authorized, CanonicalPaymentStatusResolver.Processing)]
    [InlineData(PaymentStatus.Pending, PaymentIntentStatus.Failed, CanonicalPaymentStatusResolver.Failed)]
    [InlineData(PaymentStatus.Pending, PaymentIntentStatus.Cancelled, CanonicalPaymentStatusResolver.Failed)]
    [InlineData(PaymentStatus.Pending, PaymentIntentStatus.Paid, CanonicalPaymentStatusResolver.Pending)]
    [InlineData(PaymentStatus.Pending, PaymentIntentStatus.Suspicious, CanonicalPaymentStatusResolver.ManualReview)]
    [InlineData(PaymentStatus.Expired, PaymentIntentStatus.Suspicious, CanonicalPaymentStatusResolver.ManualReview)]
    [InlineData(PaymentStatus.Pending, PaymentIntentStatus.AmountMismatch, CanonicalPaymentStatusResolver.ManualReview)]
    public void Resolve_Uses_PaymentIntentState_ForNonSuccessIntermediateStates(
        PaymentStatus paymentStatus,
        PaymentIntentStatus paymentIntentStatus,
        string expected)
    {
        var actual = CanonicalPaymentStatusResolver.Resolve(paymentStatus, paymentIntentStatus);

        Assert.Equal(expected, actual);
    }
}
