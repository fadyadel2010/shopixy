namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminLoginResponse
{
    public Guid SuperAdminId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = "Bearer";
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
    public IReadOnlyList<string> NavigationCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Current token version. Must be sent back during refresh
    /// for replay detection. If version mismatches, session is revoked.
    /// </summary>
    public int TokenVersion { get; init; }
}
