namespace PointOfSale.Core.Enums;

public enum CommerceHubPaymentRecordStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Ignored = 3,
    RefundRequired = 4,
    Refunded = 5,
    RefundFailed = 6
}
