namespace PointOfSale.Core.Entities;

// Immutable private object registration. Publication is owned by StoreConfig,
// not by this row. Retain replaced assets for published-version rollback.
public sealed class HomepageMediaAsset
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
