using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs;

public sealed class SalesReturnEligibleInvoiceResponseDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public InvoiceStatus Status { get; set; }
    public ContactReferenceDto? Customer { get; set; }
    public WarehouseReferenceDto Warehouse { get; set; } = new();
}

public sealed class SalesReturnInvoiceResponseDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal? CashAmount { get; set; }
    public decimal? CardAmount { get; set; }
    public decimal RemainingCashRefundableAmount { get; set; }
    public decimal RemainingCardRefundableAmount { get; set; }
    public bool HasLegacySettlementGap { get; set; }
    public int MutationVersion { get; set; }
    public ContactReferenceDto? Customer { get; set; }
    public WarehouseReferenceDto Warehouse { get; set; } = new();
    public IReadOnlyList<SalesReturnInvoiceItemResponseDto> Items { get; set; } =
        Array.Empty<SalesReturnInvoiceItemResponseDto>();
}

public sealed class SalesReturnInvoiceItemResponseDto
{
    public IReadOnlyList<Guid> OriginalInvoiceItemIds { get; set; } = Array.Empty<Guid>();
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid ProductVariantId { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public IReadOnlyList<ProductVariantAttributeResponseDto> VariantAttributes { get; set; } =
        Array.Empty<ProductVariantAttributeResponseDto>();
    public Guid? UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? UnitSymbol { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal SoldQuantity { get; set; }
    public decimal PreviouslyReturnedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal SoldBaseQuantity { get; set; }
    public decimal PreviouslyReturnedBaseQuantity { get; set; }
    public decimal RemainingBaseQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}
