using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Payments;

public static class PaymentStatusAuthority
{
    public static bool IsPaidState(PaymentStatus paymentStatus)
    {
        return paymentStatus is PaymentStatus.Collected
            or PaymentStatus.Paid
            or PaymentStatus.Settled;
    }
}
