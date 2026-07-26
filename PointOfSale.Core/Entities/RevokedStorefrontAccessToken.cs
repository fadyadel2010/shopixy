namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable revocation record for storefront access tokens.
/// Redis is used only as a cache; PostgreSQL is the source of truth.
/// </summary>
public class RevokedStorefrontAccessToken
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? OnlineCustomerId { get; set; }

    /// <summary>
    /// The revoked JWT identifier (jti).
    /// </summary>
    public string Jti { get; set; } = null!;

    public DateTime RevokedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public string? Reason { get; set; }

    public OnlineCustomer? OnlineCustomer { get; set; }
}
