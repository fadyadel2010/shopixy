namespace PointOfSale.API.Services.Payments.Paymob;

public enum PaymobTransactionFinality
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Failed = 3,
    Voided = 4,
    Refunded = 5
}

public static class PaymobTransactionFinalityClassifier
{
    public static PaymobTransactionFinality Classify(
        bool pending,
        bool success,
        bool isAuth,
        bool isCapture,
        bool isStandalonePayment,
        bool isVoided,
        bool isRefunded,
        bool errorOccurred)
    {
        if (isRefunded)
        {
            return PaymobTransactionFinality.Refunded;
        }

        if (isVoided)
        {
            return PaymobTransactionFinality.Voided;
        }

        if (errorOccurred)
        {
            return PaymobTransactionFinality.Failed;
        }

        if (pending)
        {
            return PaymobTransactionFinality.Pending;
        }

        if (success && isAuth && !isCapture && !isStandalonePayment)
        {
            return PaymobTransactionFinality.Authorized;
        }

        return success
            ? PaymobTransactionFinality.Captured
            : PaymobTransactionFinality.Failed;
    }
}
