using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class ProductMedia : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Product Product { get; set; } = null!;
}
