namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminRefreshRequest
{
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Token version received from the login/refresh response.
    /// Used for replay detection — if it doesn't match the current
    /// server-side version, the session is revoked.
    /// Optional for backward compatibility with older clients.
    /// </summary>
    public int? TokenVersion { get; set; }
}
