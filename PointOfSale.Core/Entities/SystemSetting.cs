namespace PointOfSale.Core.Entities;

/// <summary>
/// System settings and configuration
/// </summary>
public class SystemSetting : BaseEntity
{
    /// <summary>
    /// Null means a legacy/global setting row.
    /// Tenant-facing commerce settings must populate TenantId so authority
    /// cannot leak across stores.
    /// </summary>
    public Guid? TenantId { get; set; }

    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
}
