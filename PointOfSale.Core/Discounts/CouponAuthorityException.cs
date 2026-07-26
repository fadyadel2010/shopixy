namespace PointOfSale.Core.Discounts;

public sealed class CouponAuthorityException : InvalidOperationException
{
    public CouponAuthorityException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
