using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Tenant-scoped checkout field configuration managed from POS Admin.
/// Mutable definitions must be snapshotted onto orders when dynamic checkout values are admitted.
/// </summary>
public class CheckoutFieldDefinition : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public CheckoutFieldKind FieldKind { get; set; }

    public CheckoutFieldType FieldType { get; set; }

    public string? SystemFieldKey { get; set; }

    public string Label { get; set; } = string.Empty;

    public string? HelpText { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }

    public int ConfigurationSchemaVersion { get; set; } = 1;

    public string ConfigurationJson { get; set; } = "{}";

    public Tenant Tenant { get; set; } = null!;
}
