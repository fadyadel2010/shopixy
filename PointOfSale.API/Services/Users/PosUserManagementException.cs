namespace PointOfSale.API.Services.Users;

/// <summary>
/// Bounded failure for tenant POS user-management operations.
/// Carries the public ApiErrorResponse code and HTTP status; internal
/// exception details are never exposed to clients.
/// </summary>
public sealed class PosUserManagementException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public PosUserManagementException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public static PosUserManagementException Validation(string message) =>
        new("VALIDATION_ERROR", StatusCodes.Status400BadRequest, message);

    public static PosUserManagementException NotFound() =>
        new("USER_NOT_FOUND", StatusCodes.Status404NotFound, "User not found.");

    public static PosUserManagementException IdentityExists() =>
        new("IDENTITY_ALREADY_EXISTS", StatusCodes.Status409Conflict, "Username or email is already in use.");

    public static PosUserManagementException OwnerInvariant(string message) =>
        new("OWNER_INVARIANT_VIOLATION", StatusCodes.Status409Conflict, message);

    public static PosUserManagementException TargetForbidden(string message) =>
        new("TARGET_ROLE_FORBIDDEN", StatusCodes.Status409Conflict, message);

    public static PosUserManagementException VersionConflict() =>
        new("USER_VERSION_CONFLICT", StatusCodes.Status412PreconditionFailed, "The user changed since you loaded it. Reload and retry.");

    public static PosUserManagementException TokenExpired() =>
        new("SECURITY_TOKEN_EXPIRED", StatusCodes.Status410Gone, "This link has expired. Ask your administrator for a new one.");

    public static PosUserManagementException InvalidToken() =>
        new("VALIDATION_ERROR", StatusCodes.Status400BadRequest, "This link is invalid or has already been used.");
}
