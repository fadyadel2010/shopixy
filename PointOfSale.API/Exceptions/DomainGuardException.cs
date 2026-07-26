using System.Diagnostics.CodeAnalysis;

namespace PointOfSale.API.Exceptions;

public sealed class DomainGuardException : Exception
{
    public DomainGuardException(string code, string message, int statusCode = 409, object? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "DOMAIN_GUARD_VIOLATION" : code.Trim();
        StatusCode = statusCode;
        Details = details;
    }

    public string Code { get; }

    public int StatusCode { get; }

    public object? Details { get; }
}

public static class DomainGuard
{
    public static void Against([DoesNotReturnIf(true)] bool condition, string code, string message, int statusCode = 409, object? details = null)
    {
        if (condition)
        {
            throw new DomainGuardException(code, message, statusCode, details);
        }
    }

    public static string Required(string? value, string code, string message, object? details = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainGuardException(code, message, statusCode: 400, details);
        }

        return value.Trim();
    }

    public static Guid NonEmpty(Guid value, string code, string message, object? details = null)
    {
        if (value == Guid.Empty)
        {
            throw new DomainGuardException(code, message, statusCode: 400, details);
        }

        return value;
    }
}
