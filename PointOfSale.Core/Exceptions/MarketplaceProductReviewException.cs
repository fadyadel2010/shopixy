namespace PointOfSale.Core.Exceptions;

public sealed class MarketplaceProductReviewException : Exception
{
    public MarketplaceProductReviewException(
        string code,
        string message,
        int statusCode,
        object? dataPayload = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        DataPayload = dataPayload;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public object? DataPayload { get; }
}
