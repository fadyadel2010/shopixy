using PointOfSale.API.Services.Payments.Paymob;

namespace PointOfSale.Tests.Payments;

public sealed class PaymobTransactionFinalityTests
{
    [Theory]
    [InlineData(true, false, false, false, false, false, false, false, PaymobTransactionFinality.Pending)]
    [InlineData(false, true, true, false, false, false, false, false, PaymobTransactionFinality.Authorized)]
    [InlineData(false, true, true, true, false, false, false, false, PaymobTransactionFinality.Captured)]
    [InlineData(false, true, false, false, true, false, false, false, PaymobTransactionFinality.Captured)]
    [InlineData(false, false, false, false, false, false, false, false, PaymobTransactionFinality.Failed)]
    [InlineData(false, true, false, false, true, false, false, true, PaymobTransactionFinality.Failed)]
    [InlineData(false, true, false, false, true, true, false, false, PaymobTransactionFinality.Voided)]
    [InlineData(false, true, false, false, true, false, true, false, PaymobTransactionFinality.Refunded)]
    public void Classify_MustPreserveProviderMoneyFinality(
        bool pending,
        bool success,
        bool isAuth,
        bool isCapture,
        bool isStandalonePayment,
        bool isVoided,
        bool isRefunded,
        bool errorOccurred,
        PaymobTransactionFinality expected)
    {
        var actual = PaymobTransactionFinalityClassifier.Classify(
            pending,
            success,
            isAuth,
            isCapture,
            isStandalonePayment,
            isVoided,
            isRefunded,
            errorOccurred);

        Assert.Equal(expected, actual);
    }
}
