using System.Text.Json;

namespace PointOfSale.API.DTOs.Marketplace;

public sealed class MarketplaceProductReadDto
{
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }
    public string Language { get; set; } = "en";
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EGP";
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public string? Category { get; set; }
    public Guid VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public bool IsAvailable { get; set; }
    public bool IsAutoTranslated { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class MarketplaceProductDetailReadDto
{
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }
    public string Language { get; set; } = "en";
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Features { get; set; } = new();
    public List<MarketplaceProductFaqReadDto> Faqs { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? Slug { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EGP";
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public string? Category { get; set; }
    public Guid VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public bool IsAvailable { get; set; }
    public bool IsAutoTranslated { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class MarketplaceProductFaqReadDto
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public sealed class MarketplaceOrderReadDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public List<MarketplaceOrderReadItemDto> Items { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}

public sealed class MarketplaceOrderReadItemDto
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class MarketplaceHomepageReadDto
{
    public Guid? TenantId { get; set; }
    public string Language { get; set; } = "en";
    public List<MarketplaceHomepageSectionReadDto> Sections { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public sealed class MarketplaceHomepageSectionReadDto
{
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public JsonElement Data { get; set; }
    public int Version { get; set; }
    public int SchemaVersion { get; set; }
    public bool IsActive { get; set; }
}
