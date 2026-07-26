using System.Text.Json;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Mappers;

public static class SafeResponseMapper
{
    public static SupplierResponseDto ToResponseDto(this Supplier supplier) => new()
    {
        Id = supplier.Id,
        Name = supplier.Name,
        Phone = supplier.Phone,
        Email = supplier.Email,
        Address = supplier.Address,
        TaxNumber = supplier.TaxNumber,
        Balance = supplier.Balance,
        OpeningBalanceAmount = supplier.OpeningBalanceAmount,
        BalanceType = supplier.BalanceType,
        OpeningBalanceDate = supplier.OpeningBalanceDate,
        PaymentTermsDays = supplier.PaymentTermsDays,
        Notes = supplier.Notes
    };

    public static CustomerResponseDto ToResponseDto(this Customer customer, decimal authoritativeBalance) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Phone = customer.Phone,
        Email = customer.Email,
        Address = customer.Address,
        TaxNumber = customer.TaxNumber,
        Balance = authoritativeBalance,
        OpeningBalanceAmount = customer.OpeningBalanceAmount,
        BalanceType = customer.BalanceType,
        OpeningBalanceDate = customer.OpeningBalanceDate,
        Notes = customer.Notes
    };

    public static SalesRepresentativeResponseDto ToResponseDto(this SalesRepresentative salesRepresentative) => new()
    {
        Id = salesRepresentative.Id,
        Name = salesRepresentative.Name,
        Phone = salesRepresentative.Phone,
        IsActive = salesRepresentative.IsActive,
        MonthlyTarget = salesRepresentative.MonthlyTarget,
        CommissionPercentage = salesRepresentative.CommissionPercentage,
        Notes = salesRepresentative.Notes
    };

    public static ProductResponseDto ToResponseDto(this Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        NameAr = product.NameAr,
        Barcode = product.Barcode,
        SKU = product.SKU,
        Description = product.Description,
        DescriptionAr = product.DescriptionAr,
        CategoryId = product.CategoryId,
        CategoryName = product.Category?.Name,
        PurchasePrice = product.PurchasePrice,
        SalePrice = product.SalePrice,
        OnlinePrice = product.OnlinePrice,
        PriceIncludesTax = product.PriceIncludesTax,
        TaxRate = product.TaxRate,
        Unit = product.Unit,
        MinimumStockLevel = product.MinimumStockLevel,
        Images = product.Images,
        IsActive = product.IsActive,
        TrackStock = product.TrackStock,
        HasVariants = product.HasVariants,
        Notes = product.Notes,
        Slug = product.Slug,
        MetaTitle = product.MetaTitle,
        MetaDescription = product.MetaDescription,
        MetaKeywords = product.MetaKeywords,
        Weight = product.Weight,
        Length = product.Length,
        Width = product.Width,
        Height = product.Height,
        WeightUnit = product.WeightUnit,
        DimensionUnit = product.DimensionUnit,
        PublishStatus = product.PublishStatus,
        IsOnlineVisible = product.IsOnlineVisible,
        IsSearchable = product.IsSearchable,
        IsFeatured = product.IsFeatured,
        LowStockThreshold = product.LowStockThreshold,
        AllowBackorder = product.AllowBackorder,
        Brand = product.Brand,
        Manufacturer = product.Manufacturer,
        Tags = product.Tags,
        Features = DeserializeStringList(product.FeaturesJson),
        Faqs = DeserializeFaqs(product.FaqJson),
        AiGenerated = product.AiGenerated,
        VideoUrl = product.VideoUrl,
        ProductTemplate = product.ProductTemplate,
        WarehouseStocks = ToDerivedProductWarehouseStockResponseDtos(product),
        Units = product.ProductUnits
            .Where(unit => !unit.IsDeleted)
            .Select(ToProductUnitResponseDto)
            .ToList(),
        Variants = product.Variants
            .Where(variant => !variant.IsDeleted)
            .Select(variant => ToProductVariantReferenceDto(variant)!)
            .ToList()
    };

    public static ProductBarcodeResponseDto ToBarcodeResponseDto(this Product product, Guid? matchedVariantId) => new()
    {
        MatchedVariantId = matchedVariantId,
        Id = product.Id,
        Name = product.Name,
        NameAr = product.NameAr,
        Barcode = product.Barcode,
        SKU = product.SKU,
        Description = product.Description,
        DescriptionAr = product.DescriptionAr,
        CategoryId = product.CategoryId,
        CategoryName = product.Category?.Name,
        PurchasePrice = product.PurchasePrice,
        SalePrice = product.SalePrice,
        OnlinePrice = product.OnlinePrice,
        PriceIncludesTax = product.PriceIncludesTax,
        TaxRate = product.TaxRate,
        Unit = product.Unit,
        MinimumStockLevel = product.MinimumStockLevel,
        Images = product.Images,
        IsActive = product.IsActive,
        TrackStock = product.TrackStock,
        HasVariants = product.HasVariants,
        Notes = product.Notes,
        Slug = product.Slug,
        MetaTitle = product.MetaTitle,
        MetaDescription = product.MetaDescription,
        MetaKeywords = product.MetaKeywords,
        Weight = product.Weight,
        Length = product.Length,
        Width = product.Width,
        Height = product.Height,
        WeightUnit = product.WeightUnit,
        DimensionUnit = product.DimensionUnit,
        PublishStatus = product.PublishStatus,
        IsOnlineVisible = product.IsOnlineVisible,
        IsSearchable = product.IsSearchable,
        IsFeatured = product.IsFeatured,
        LowStockThreshold = product.LowStockThreshold,
        AllowBackorder = product.AllowBackorder,
        Brand = product.Brand,
        Manufacturer = product.Manufacturer,
        Tags = product.Tags,
        Features = DeserializeStringList(product.FeaturesJson),
        Faqs = DeserializeFaqs(product.FaqJson),
        AiGenerated = product.AiGenerated,
        VideoUrl = product.VideoUrl,
        ProductTemplate = product.ProductTemplate,
        WarehouseStocks = ToDerivedProductWarehouseStockResponseDtos(product),
        Units = product.ProductUnits
            .Where(unit => !unit.IsDeleted)
            .Select(ToProductUnitResponseDto)
            .ToList(),
        Variants = product.Variants
            .Where(variant => !variant.IsDeleted)
            .Select(variant => ToProductVariantReferenceDto(variant)!)
            .ToList()
    };

    public static SalesInvoiceResponseDto ToResponseDto(this SalesInvoice invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        InvoiceDate = invoice.InvoiceDate,
        Status = invoice.Status,
        Subtotal = invoice.Subtotal,
        DiscountAmount = invoice.DiscountAmount,
        DiscountPercentage = invoice.DiscountPercentage,
        TaxAmount = invoice.TaxAmount,
        TotalAmount = invoice.TotalAmount,
        PaidAmount = invoice.PaidAmount,
        RemainingAmount = invoice.RemainingAmount,
        PaymentMethod = invoice.PaymentMethod,
        CashAmount = invoice.CashAmount,
        CardAmount = invoice.CardAmount,
        Notes = invoice.Notes,
        MutationVersion = invoice.AccountingMutationVersion,
        Customer = invoice.Customer == null ? null : ToContactReferenceDto(invoice.Customer),
        Warehouse = ToWarehouseReferenceDto(invoice.Warehouse),
        SalesRepresentative = invoice.SalesRepresentative == null ? null : ToSalesRepresentativeReferenceDto(invoice.SalesRepresentative),
        Items = invoice.Items
            .Where(item => !item.IsDeleted)
            .Select(ToSalesInvoiceItemResponseDto)
            .ToList()
    };

    public static PurchaseInvoiceResponseDto ToResponseDto(this PurchaseInvoice invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        InvoiceDate = invoice.InvoiceDate,
        DueDate = invoice.DueDate,
        Status = invoice.Status,
        Subtotal = invoice.Subtotal,
        DiscountAmount = invoice.DiscountAmount,
        DiscountPercentage = invoice.DiscountPercentage,
        TaxAmount = invoice.TaxAmount,
        TotalAmount = invoice.TotalAmount,
        PaidAmount = invoice.PaidAmount,
        RemainingAmount = invoice.RemainingAmount,
        PaymentMethod = invoice.PaymentMethod,
        Notes = invoice.Notes,
        MutationVersion = invoice.AccountingMutationVersion,
        Supplier = ToContactReferenceDto(invoice.Supplier),
        Warehouse = ToWarehouseReferenceDto(invoice.Warehouse),
        Items = invoice.Items
            .Where(item => !item.IsDeleted)
            .Select(ToPurchaseInvoiceItemResponseDto)
            .ToList()
    };

    public static ReturnResponseDto ToResponseDto(this Return entity) => new()
    {
        Id = entity.Id,
        ReturnNumber = entity.ReturnNumber,
        ReturnDate = entity.ReturnDate,
        ReturnType = entity.ReturnType,
        OriginalInvoiceId = entity.ReturnType == ReturnType.PurchaseReturn
            ? entity.OriginalPurchaseInvoiceId
            : entity.OriginalInvoiceId,
        OriginalInvoiceNumber = entity.OriginalInvoiceNumber,
        Customer = entity.Customer == null ? null : ToContactReferenceDto(entity.Customer),
        Supplier = entity.Supplier == null ? null : ToContactReferenceDto(entity.Supplier),
        Warehouse = ToWarehouseReferenceDto(entity.Warehouse),
        Subtotal = entity.Subtotal,
        TaxAmount = entity.TaxAmount,
        TotalAmount = entity.TotalAmount,
        Reason = entity.Reason,
        Notes = entity.Notes,
        Items = entity.Items
            .Where(item => !item.IsDeleted)
            .Select(ToReturnItemResponseDto)
            .ToList()
    };

    public static StockTransferResponseDto ToResponseDto(this StockTransfer transfer) => new()
    {
        Id = transfer.Id,
        TransferNumber = transfer.TransferNumber,
        TransferDate = transfer.TransferDate,
        Status = transfer.Status,
        Notes = transfer.Notes,
        FromWarehouse = ToWarehouseReferenceDto(transfer.FromWarehouse),
        ToWarehouse = ToWarehouseReferenceDto(transfer.ToWarehouse),
        RequestedBy = transfer.RequestedBy == null ? null : ToUserReferenceDto(transfer.RequestedBy),
        ApprovedBy = transfer.ApprovedBy == null ? null : ToUserReferenceDto(transfer.ApprovedBy),
        ApprovedAt = transfer.ApprovedAt,
        ApprovalNotes = transfer.ApprovalNotes,
        CompletedBy = transfer.CompletedBy == null ? null : ToUserReferenceDto(transfer.CompletedBy),
        CompletedAt = transfer.CompletedAt,
        Items = transfer.Items
            .Where(item => !item.IsDeleted)
            .Select(ToStockTransferItemResponseDto)
            .ToList()
    };

    private static ContactReferenceDto ToContactReferenceDto(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Phone = customer.Phone,
        Email = customer.Email
    };

    private static ContactReferenceDto ToContactReferenceDto(Supplier supplier) => new()
    {
        Id = supplier.Id,
        Name = supplier.Name,
        Phone = supplier.Phone,
        Email = supplier.Email
    };

    private static WarehouseReferenceDto ToWarehouseReferenceDto(Warehouse warehouse) => new()
    {
        Id = warehouse.Id,
        Name = warehouse.Name,
        Code = warehouse.Code,
        Address = warehouse.Address
    };

    private static SalesRepresentativeReferenceDto ToSalesRepresentativeReferenceDto(SalesRepresentative salesRepresentative) => new()
    {
        Id = salesRepresentative.Id,
        Name = salesRepresentative.Name,
        Phone = salesRepresentative.Phone
    };

    private static UserReferenceDto ToUserReferenceDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName
    };

    private static UnitReferenceDto? ToUnitReferenceDto(Unit? unit)
    {
        if (unit == null)
        {
            return null;
        }

        return new UnitReferenceDto
        {
            Id = unit.Id,
            Name = unit.Name,
            Symbol = unit.Symbol
        };
    }

    private static ProductVariantReferenceDto? ToProductVariantReferenceDto(ProductVariant? variant)
    {
        if (variant == null)
        {
            return null;
        }

        return new ProductVariantReferenceDto
        {
            Id = variant.Id,
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            EffectiveOnlinePrice = variant.EffectiveOnlinePrice,
            IsDefault = variant.IsDefault,
            IsActive = variant.IsActive,
            ImageUrl = variant.ImageUrl,
            SortOrder = variant.SortOrder,
            TotalStock = variant.Stocks
                .Where(stock => !stock.IsDeleted)
                .Sum(stock => stock.Quantity - stock.ReservedQuantity),
            Attributes = variant.Attributes
                .Where(attribute => !attribute.IsDeleted)
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => new ProductVariantAttributeResponseDto
                {
                    Name = attribute.Name,
                    Value = attribute.Value
                })
                .ToList()
        };
    }

    private static List<ProductWarehouseStockResponseDto> ToDerivedProductWarehouseStockResponseDtos(Product product) =>
        product.Variants
            .Where(variant => !variant.IsDeleted)
            .SelectMany(variant => variant.Stocks.Where(stock => !stock.IsDeleted))
            .GroupBy(stock => stock.WarehouseId)
            .Select(group => new ProductWarehouseStockResponseDto
            {
                WarehouseId = group.Key,
                WarehouseName = group
                    .Select(s => s.Warehouse?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .GroupBy(n => n)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .Select(g => g.Key)
                    .FirstOrDefault(),
                Quantity = group.Sum(stock => stock.Quantity),
                ReservedQuantity = group.Sum(stock => stock.ReservedQuantity),
                AvailableQuantity = group.Sum(stock => stock.AvailableQuantity)
            })
            .OrderBy(stock => stock.WarehouseName)
            .ThenBy(stock => stock.WarehouseId)
            .ToList();

    private static ProductUnitResponseDto ToProductUnitResponseDto(ProductUnit unit) => new()
    {
        Id = unit.Id,
        UnitId = unit.UnitId,
        UnitName = unit.Unit?.Name ?? string.Empty,
        UnitSymbol = unit.Unit?.Symbol,
        ConversionFactor = unit.ConversionFactor,
        SellingPrice = unit.SellingPrice,
        Barcode = unit.Barcode,
        IsDefault = unit.IsDefault,
        IsBaseUnit = unit.IsBaseUnit
    };

    private static SalesInvoiceItemResponseDto ToSalesInvoiceItemResponseDto(SalesInvoiceItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductName = item.Product.Name,
        ProductVariantId = item.ProductVariantId,
        ProductVariant = ToProductVariantReferenceDto(item.ProductVariant),
        UnitId = item.UnitId,
        Unit = ToUnitReferenceDto(item.Unit),
        Quantity = item.Quantity,
        ConversionFactor = item.ConversionFactor,
        BaseQuantity = item.BaseQuantity,
        UnitPrice = item.UnitPrice,
        DiscountAmount = item.DiscountAmount,
        TaxRate = item.TaxRate,
        PriceIncludesTax = item.Product.PriceIncludesTax,
        TaxAmount = item.TaxAmount,
        TotalAmount = item.TotalAmount
    };

    private static PurchaseInvoiceItemResponseDto ToPurchaseInvoiceItemResponseDto(PurchaseInvoiceItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductName = item.Product.Name,
        ProductVariantId = item.ProductVariantId,
        ProductVariant = ToProductVariantReferenceDto(item.ProductVariant),
        UnitId = item.UnitId,
        Unit = ToUnitReferenceDto(item.Unit),
        Quantity = item.Quantity,
        ConversionFactor = item.ConversionFactor,
        BaseQuantity = item.BaseQuantity,
        UnitPrice = item.UnitPrice,
        DiscountAmount = item.DiscountAmount,
        TaxRate = item.TaxRate,
        PriceIncludesTax = item.Product.PriceIncludesTax,
        TaxAmount = item.TaxAmount,
        TotalAmount = item.TotalAmount
    };

    private static ReturnItemResponseDto ToReturnItemResponseDto(ReturnItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductName = item.Product.Name,
        ProductVariantId = item.ProductVariantId,
        ProductVariant = ToProductVariantReferenceDto(item.ProductVariant),
        UnitId = item.UnitId,
        Unit = ToUnitReferenceDto(item.Unit),
        Quantity = item.Quantity,
        ConversionFactor = item.ConversionFactor,
        BaseQuantity = item.BaseQuantity,
        UnitPrice = item.UnitPrice,
        TaxRate = item.TaxRate,
        TaxAmount = item.TaxAmount,
        TotalAmount = item.TotalAmount,
        Reason = item.Reason
    };

    private static StockTransferItemResponseDto ToStockTransferItemResponseDto(StockTransferItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductName = item.Product.Name,
        ProductVariantId = item.ProductVariantId,
        ProductVariant = ToProductVariantReferenceDto(item.ProductVariant),
        Quantity = item.Quantity,
        Notes = item.Notes
    };

    private static IReadOnlyList<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<ProductFaqResponseDto> DeserializeFaqs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ProductFaqResponseDto>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ProductFaqResponseDto>>(json) ?? new List<ProductFaqResponseDto>();
        }
        catch
        {
            return Array.Empty<ProductFaqResponseDto>();
        }
    }
}
