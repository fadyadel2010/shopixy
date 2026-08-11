namespace PointOfSale.API.Services.Admin;

public sealed class CheckoutFieldDefinitionException : Exception
{
    public CheckoutFieldDefinitionException(string code, string message, int statusCode = StatusCodes.Status400BadRequest)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
