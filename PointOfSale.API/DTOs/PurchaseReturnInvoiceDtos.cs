using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs;

public sealed class PurchaseReturnEligibleInvoiceResponseDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public InvoiceStatus Status { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
}

public sealed class PurchaseReturnInvoiceResponseDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public IReadOnlyList<PurchaseReturnInvoiceItemResponseDto> Items { get; set; } =
        Array.Empty<PurchaseReturnInvoiceItemResponseDto>();
}

public sealed class PurchaseReturnInvoiceItemResponseDto
{
    public IReadOnlyList<Guid> OriginalInvoiceItemIds { get; set; } = Array.Empty<Guid>();
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid? ProductVariantId { get; set; }
    public string? VariantSku { get; set; }
    public IReadOnlyList<ProductVariantAttributeResponseDto> VariantAttributes { get; set; } =
        Array.Empty<ProductVariantAttributeResponseDto>();
    public Guid? UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? UnitSymbol { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal PurchasedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
}
