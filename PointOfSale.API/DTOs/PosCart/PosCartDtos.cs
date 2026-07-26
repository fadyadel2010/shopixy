using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.PosCart;

public sealed record CreatePosCartRequest
{
    [Required]
    public Guid WarehouseId { get; init; }

    public Guid? CustomerId { get; init; }

    [Range(5, 1440)]
    public int ExpiresInMinutes { get; init; } = 480;
}

public sealed record UpsertPosCartItemRequest
{
    [Required]
    public Guid ProductId { get; init; }

    public Guid? ProductVariantId { get; init; }
    public Guid? UnitId { get; init; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal DiscountAmount { get; init; }

    [Required]
    public int ExpectedVersion { get; init; }
}

public sealed record UpdatePosCartItemRequest
{
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal DiscountAmount { get; init; }

    [Required]
    public int ExpectedVersion { get; init; }
}

public sealed record PosCartLifecycleRequest
{
    [Required]
    public int ExpectedVersion { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}

public sealed record PosCartResponse
{
    public Guid CartId { get; init; }
    public Guid TenantId { get; init; }
    public Guid CashierId { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid? CustomerId { get; init; }
    public PosCartStatus Status { get; init; }
    public int Version { get; init; }
    public decimal Subtotal { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public DateTime LastActivityAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public Guid? CompletedSaleId { get; init; }
    public IReadOnlyList<PosCartItemResponse> Items { get; init; } = Array.Empty<PosCartItemResponse>();
}

public sealed record PosCartItemResponse
{
    public Guid ItemId { get; init; }
    public Guid ProductId { get; init; }
    public Guid ProductVariantId { get; init; }
    public Guid? UnitId { get; init; }
    public decimal Quantity { get; init; }
    public decimal ConversionFactor { get; init; }
    public decimal BaseQuantity { get; init; }
    public decimal UnitPriceSnapshot { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TaxRateSnapshot { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal CostPriceSnapshot { get; init; }
    public decimal CostAmountSnapshot { get; init; }
    public string ProductNameSnapshot { get; init; } = string.Empty;
    public string? VariantSkuSnapshot { get; init; }
    public int LineNumber { get; init; }
}
