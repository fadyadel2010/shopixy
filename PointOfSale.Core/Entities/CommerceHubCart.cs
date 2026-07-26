namespace PointOfSale.Core.Entities;

public class CommerceHubCart : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// CommerceHub customer that owns this marketplace cart.
    /// This is intentionally separate from Storefront OnlineCustomer identity.
    /// </summary>
    public Guid? CommerceHubCustomerId { get; set; }

    /// <summary>
    /// Legacy storage column retained for backward compatibility. New CommerceHub
    /// flows must bind ownership through CommerceHubCustomerId.
    /// </summary>
    public Guid OnlineCustomerId { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public int Version { get; set; } = 1;
    public bool IsLocked { get; set; }
    public DateTime? LockedAtUtc { get; set; }
}
