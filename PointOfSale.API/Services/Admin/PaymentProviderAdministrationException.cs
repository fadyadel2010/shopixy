namespace PointOfSale.API.Services.Admin;

public sealed class PaymentProviderAdministrationException : Exception
{
    public PaymentProviderAdministrationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
