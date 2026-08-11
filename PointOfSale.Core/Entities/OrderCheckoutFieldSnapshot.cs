using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable checkout field definition/value snapshot captured when an order is created.
/// </summary>
public class OrderCheckoutFieldSnapshot : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>
    /// Original mutable checkout field definition id. Intentionally not a foreign key;
    /// historical snapshots must survive definition archive/deletion unchanged.
    /// </summary>
    public Guid CheckoutFieldDefinitionId { get; set; }

    public CheckoutFieldKind FieldKind { get; set; }

    public CheckoutFieldType FieldType { get; set; }

    public string? SystemFieldKey { get; set; }

    public string Label { get; set; } = string.Empty;

    public string? HelpText { get; set; }

    public bool IsRequired { get; set; }

    public bool IsEnabled { get; set; }

    public int DisplayOrder { get; set; }

    public int ConfigurationSchemaVersion { get; set; }

    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>
    /// Canonical submitted value as JSON. Scalars are preserved as JSON strings,
    /// numbers, booleans, or null according to the field type.
    /// </summary>
    public string ValueJson { get; set; } = "null";
}
