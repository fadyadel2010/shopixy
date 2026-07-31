using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs;

public sealed class SupplierResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public decimal Balance { get; set; }
    public decimal? OpeningBalanceAmount { get; set; }
    public string? BalanceType { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public int PaymentTermsDays { get; set; }
    public string? Notes { get; set; }
}

public sealed class CustomerResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public decimal Balance { get; set; }
    public decimal? OpeningBalanceAmount { get; set; }
    public string? BalanceType { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public string? Notes { get; set; }
}

public sealed class SalesRepresentativeResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public decimal? MonthlyTarget { get; set; }
    public decimal? CommissionPercentage { get; set; }
    public string? Notes { get; set; }
}

public sealed class ContactReferenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

public sealed class WarehouseReferenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Address { get; set; }
}

public sealed class SalesRepresentativeReferenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public sealed class UserReferenceDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public sealed class UnitReferenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
}

public sealed class ProductVariantAttributeResponseDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class ProductVariantReferenceDto
{
    public Guid Id { get; set; }
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public decimal? OnlinePrice { get; set; }
    public decimal EffectiveOnlinePrice { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public decimal TotalStock { get; set; }
    public IReadOnlyList<ProductVariantAttributeResponseDto> Attributes { get; set; } = Array.Empty<ProductVariantAttributeResponseDto>();
}

public sealed class ProductWarehouseStockResponseDto
{
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
}

public sealed class ProductUnitResponseDto
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public string? UnitSymbol { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? SellingPrice { get; set; }
    public string? Barcode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsBaseUnit { get; set; }
}

public sealed class ProductFaqResponseDto
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public class ProductResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Barcode { get; set; }
    public string? SKU { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal? OnlinePrice { get; set; }
    public bool PriceIncludesTax { get; set; }
    public decimal TaxRate { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal MinimumStockLevel { get; set; }
    public string? Images { get; set; }
    public bool IsActive { get; set; }
    public bool TrackStock { get; set; }
    public bool HasVariants { get; set; }
    public string? Notes { get; set; }
    public string? Slug { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public decimal Weight { get; set; }
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public string WeightUnit { get; set; } = string.Empty;
    public string DimensionUnit { get; set; } = string.Empty;
    public PublishStatus PublishStatus { get; set; }
    public bool IsOnlineVisible { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsFeatured { get; set; }
    public int LowStockThreshold { get; set; }
    public bool AllowBackorder { get; set; }
    public string? Brand { get; set; }
    public string? Manufacturer { get; set; }
    public string? Tags { get; set; }
    public IReadOnlyList<string> Features { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ProductFaqResponseDto> Faqs { get; set; } = Array.Empty<ProductFaqResponseDto>();
    public bool AiGenerated { get; set; }
    public string? VideoUrl { get; set; }
    public string? ProductTemplate { get; set; }
    public IReadOnlyList<ProductWarehouseStockResponseDto> WarehouseStocks { get; set; } = Array.Empty<ProductWarehouseStockResponseDto>();
    public IReadOnlyList<ProductUnitResponseDto> Units { get; set; } = Array.Empty<ProductUnitResponseDto>();
    public IReadOnlyList<ProductVariantReferenceDto> Variants { get; set; } = Array.Empty<ProductVariantReferenceDto>();
    public string RowVersion { get; set; } = "0";
}

public sealed class ProductBarcodeResponseDto : ProductResponseDto
{
    public Guid? MatchedVariantId { get; set; }
}

public sealed class SalesInvoiceItemResponseDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid? ProductVariantId { get; set; }
    public ProductVariantReferenceDto? ProductVariant { get; set; }
    public Guid? UnitId { get; set; }
    public UnitReferenceDto? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public bool PriceIncludesTax { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class SalesInvoiceResponseDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public decimal? CashAmount { get; set; }
    public decimal? CardAmount { get; set; }
    public string? Notes { get; set; }
    public int MutationVersion { get; set; }
    public ContactReferenceDto? Customer { get; set; }
    public WarehouseReferenceDto Warehouse { get; set; } = new();
    public SalesRepresentativeReferenceDto? SalesRepresentative { get; set; }
    public IReadOnlyList<SalesInvoiceItemResponseDto> Items { get; set; } = Array.Empty<SalesInvoiceItemResponseDto>();
}

public sealed class PurchaseInvoiceItemResponseDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid? ProductVariantId { get; set; }
    public ProductVariantReferenceDto? ProductVariant { get; set; }
    public Guid? UnitId { get; set; }
    public UnitReferenceDto? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public bool PriceIncludesTax { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class PurchaseInvoiceResponseDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? Notes { get; set; }
    public int MutationVersion { get; set; }
    public ContactReferenceDto Supplier { get; set; } = new();
    public WarehouseReferenceDto Warehouse { get; set; } = new();
    public IReadOnlyList<PurchaseInvoiceItemResponseDto> Items { get; set; } = Array.Empty<PurchaseInvoiceItemResponseDto>();
}

public sealed class ReturnItemResponseDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid? ProductVariantId { get; set; }
    public ProductVariantReferenceDto? ProductVariant { get; set; }
    public Guid? UnitId { get; set; }
    public UnitReferenceDto? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Reason { get; set; }
}

public sealed class ReturnResponseDto
{
    public Guid Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public ReturnType ReturnType { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    public string? OriginalInvoiceNumber { get; set; }
    public ContactReferenceDto? Customer { get; set; }
    public ContactReferenceDto? Supplier { get; set; }
    public WarehouseReferenceDto Warehouse { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyList<ReturnItemResponseDto> Items { get; set; } = Array.Empty<ReturnItemResponseDto>();
}

public sealed class StockTransferItemResponseDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid? ProductVariantId { get; set; }
    public ProductVariantReferenceDto? ProductVariant { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public sealed class StockTransferResponseDto
{
    public Guid Id { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public TransferStatus Status { get; set; }
    public string? Notes { get; set; }
    public WarehouseReferenceDto FromWarehouse { get; set; } = new();
    public WarehouseReferenceDto ToWarehouse { get; set; } = new();
    public UserReferenceDto? RequestedBy { get; set; }
    public UserReferenceDto? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalNotes { get; set; }
    public UserReferenceDto? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public IReadOnlyList<StockTransferItemResponseDto> Items { get; set; } = Array.Empty<StockTransferItemResponseDto>();
}
