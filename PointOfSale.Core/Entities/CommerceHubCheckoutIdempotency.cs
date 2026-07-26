namespace PointOfSale.Core.Entities;

public class CommerceHubCheckoutIdempotency
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = null!;
    public Guid? TenantId { get; set; }

    /// <summary>
    /// CommerceHub customer that owns the checkout idempotency claim.
    /// </summary>
    public Guid? CommerceHubCustomerId { get; set; }

    /// <summary>
    /// Legacy Storefront identity column retained for compatibility.
    /// </summary>
    public Guid? OnlineCustomerId { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
