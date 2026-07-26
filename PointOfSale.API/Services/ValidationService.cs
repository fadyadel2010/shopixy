using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

/// <summary>
/// Centralized validation service for protecting financial and inventory data integrity
/// </summary>
public class ValidationService : IValidationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ValidationService> _logger;

    public ValidationService(ApplicationDbContext context, ILogger<ValidationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Product Validations

    /// <summary>
    /// Validates if a product can be deleted
    /// Products cannot be deleted if they have stock or have been used in any invoice
    /// </summary>
    public async Task<ValidationResult> CanDeleteProductAsync(Guid productId)
    {
        // Check if product has any stock in any warehouse
        var hasStock = await _context.ProductVariantStocks
            .AnyAsync(stock => stock.Variant.ProductId == productId && stock.Quantity != 0);

        if (hasStock)
        {
            return ValidationResult.Failure(
                "PRODUCT_HAS_STOCK",
                "Cannot delete product: stock exists in warehouses. Please set the product as inactive instead.",
                "لا يمكن حذف الصنف لوجود رصيد بالمخازن. يرجى تعطيل الصنف بدلاً من حذفه.");
        }

        // Check if product is used in any purchase invoice
        var usedInPurchase = await _context.PurchaseInvoiceItems
            .AnyAsync(pii => pii.ProductId == productId);

        if (usedInPurchase)
        {
            return ValidationResult.Failure(
                "PRODUCT_USED_IN_PURCHASES",
                "Product is used in purchase transactions and cannot be deleted. Please set the product as inactive instead.",
                "الصنف مستخدم في فواتير الشراء ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه.");
        }

        // Check if product is used in any sales invoice
        var usedInSales = await _context.SalesInvoiceItems
            .AnyAsync(sii => sii.ProductId == productId);

        if (usedInSales)
        {
            return ValidationResult.Failure(
                "PRODUCT_USED_IN_SALES",
                "Product is used in sales transactions and cannot be deleted. Please set the product as inactive instead.",
                "الصنف مستخدم في فواتير البيع ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه.");
        }

        // Check if product is used in any returns
        var usedInReturns = await _context.ReturnItems
            .AnyAsync(ri => ri.ProductId == productId);

        if (usedInReturns)
        {
            return ValidationResult.Failure(
                "PRODUCT_USED_IN_RETURNS",
                "Product is used in return transactions and cannot be deleted. Please set the product as inactive instead.",
                "الصنف مستخدم في فواتير المرتجعات ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه.");
        }

        // Check if product is used in stock transfers
        var usedInTransfers = await _context.StockTransferItems
            .AnyAsync(sti => sti.ProductId == productId);

        if (usedInTransfers)
        {
            return ValidationResult.Failure(
                "PRODUCT_USED_IN_TRANSFERS",
                "Product is used in stock transfers and cannot be deleted. Please set the product as inactive instead.",
                "الصنف مستخدم في عمليات نقل المخزون ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه.");
        }

        // Check if product has stock movements
        var hasMovements = await _context.StockMovements
            .AnyAsync(sm => sm.ProductId == productId);

        if (hasMovements)
        {
            return ValidationResult.Failure(
                "PRODUCT_HAS_MOVEMENTS",
                "Product has stock movement history and cannot be deleted. Please set the product as inactive instead.",
                "الصنف له سجل حركة مخزون ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه.");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates if a product unit conversion factor can be modified
    /// Cannot modify if the unit has been used in historical transactions
    /// </summary>
    public async Task<ValidationResult> CanModifyProductUnitConversionAsync(Guid productId, Guid unitId, decimal newConversionFactor)
    {
        // Check if this unit is used in purchase invoices
        var usedInPurchases = await _context.PurchaseInvoiceItems
            .AnyAsync(pii => pii.ProductId == productId && pii.UnitId == unitId);

        // Check if this unit is used in sales invoices
        var usedInSales = await _context.SalesInvoiceItems
            .AnyAsync(sii => sii.ProductId == productId && sii.UnitId == unitId);

        // Check if this unit is used in stock movements
        var usedInMovements = await _context.StockMovements
            .AnyAsync(sm => sm.ProductId == productId && sm.UnitId == unitId);

        if (usedInPurchases || usedInSales || usedInMovements)
        {
            return ValidationResult.Failure(
                "UNIT_USED_IN_TRANSACTIONS",
                "Cannot modify conversion factor: this unit has been used in historical transactions. The conversion factor is locked to maintain data integrity.",
                "لا يمكن تعديل معامل التحويل: هذه الوحدة مستخدمة في معاملات سابقة. معامل التحويل مقفل للحفاظ على سلامة البيانات.");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Customer Validations

    /// <summary>
    /// Validates if a customer can be deleted
    /// Customers cannot be deleted if they have a balance or any invoice/payment history
    /// </summary>
    public async Task<ValidationResult> CanDeleteCustomerAsync(Guid customerId)
    {
        var customer = await _context.Customers.FindAsync(customerId);
        if (customer == null)
        {
            return ValidationResult.Failure(
                "CUSTOMER_NOT_FOUND",
                "Customer not found.",
                "العميل غير موجود.");
        }

        var customerBalance = await _context.AccountLedgers
            .Where(entry =>
                entry.PartyType == PartyType.Customer &&
                entry.PartyId == customerId)
            .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount)) ?? 0m;

        // Customer.Balance is a compatibility shadow; deletion decisions use ledger authority.
        if (customerBalance != 0)
        {
            return ValidationResult.Failure(
                "CUSTOMER_HAS_BALANCE",
                $"Cannot delete customer: has outstanding balance of {customerBalance:N2}. Please settle the balance first or set the customer as inactive.",
                $"لا يمكن حذف العميل: لديه رصيد معلق بقيمة {customerBalance:N2}. يرجى تسوية الرصيد أولاً أو تعطيل العميل.");
        }

        // Check if customer has any sales invoices
        var hasSalesInvoices = await _context.SalesInvoices
            .AnyAsync(si => si.CustomerId == customerId);

        if (hasSalesInvoices)
        {
            return ValidationResult.Failure(
                "CUSTOMER_HAS_INVOICES",
                "Cannot delete customer: has invoice history. Please set the customer as inactive instead.",
                "لا يمكن حذف العميل: لديه سجل فواتير. يرجى تعطيل العميل بدلاً من حذفه.");
        }

        // Check if customer has any transactions
        var hasTransactions = await _context.CustomerTransactions
            .AnyAsync(ct => ct.CustomerId == customerId);

        if (hasTransactions)
        {
            return ValidationResult.Failure(
                "CUSTOMER_HAS_TRANSACTIONS",
                "Cannot delete customer: has transaction history. Please set the customer as inactive instead.",
                "لا يمكن حذف العميل: لديه سجل معاملات. يرجى تعطيل العميل بدلاً من حذفه.");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Supplier Validations

    /// <summary>
    /// Validates if a supplier can be deleted
    /// Suppliers cannot be deleted if they have a balance or any invoice/payment history
    /// </summary>
    public async Task<ValidationResult> CanDeleteSupplierAsync(Guid supplierId)
    {
        var supplier = await _context.Suppliers.FindAsync(supplierId);
        if (supplier == null)
        {
            return ValidationResult.Failure(
                "SUPPLIER_NOT_FOUND",
                "Supplier not found.",
                "المورد غير موجود.");
        }

        // Check if supplier has balance
        if (supplier.Balance != 0)
        {
            return ValidationResult.Failure(
                "SUPPLIER_HAS_BALANCE",
                $"Cannot delete supplier: has outstanding balance of {supplier.Balance:N2}. Please settle the balance first or set the supplier as inactive.",
                $"لا يمكن حذف المورد: لديه رصيد معلق بقيمة {supplier.Balance:N2}. يرجى تسوية الرصيد أولاً أو تعطيل المورد.");
        }

        // Check if supplier has any purchase invoices
        var hasPurchaseInvoices = await _context.PurchaseInvoices
            .AnyAsync(pi => pi.SupplierId == supplierId);

        if (hasPurchaseInvoices)
        {
            return ValidationResult.Failure(
                "SUPPLIER_HAS_INVOICES",
                "Cannot delete supplier: has invoice history. Please set the supplier as inactive instead.",
                "لا يمكن حذف المورد: لديه سجل فواتير. يرجى تعطيل المورد بدلاً من حذفه.");
        }

        // Check if supplier has any transactions
        var hasTransactions = await _context.SupplierTransactions
            .AnyAsync(st => st.SupplierId == supplierId);

        if (hasTransactions)
        {
            return ValidationResult.Failure(
                "SUPPLIER_HAS_TRANSACTIONS",
                "Cannot delete supplier: has transaction history. Please set the supplier as inactive instead.",
                "لا يمكن حذف المورد: لديه سجل معاملات. يرجى تعطيل المورد بدلاً من حذفه.");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Purchase Invoice Validations

    /// <summary>
    /// Validates if a purchase invoice can be deleted
    /// Posted/completed invoices cannot be deleted - use returns instead
    /// </summary>
    public async Task<ValidationResult> CanDeletePurchaseInvoiceAsync(Guid invoiceId)
    {
        var invoice = await _context.PurchaseInvoices.FindAsync(invoiceId);
        if (invoice == null)
        {
            return ValidationResult.Failure(
                "INVOICE_NOT_FOUND",
                "Purchase invoice not found.",
                "فاتورة الشراء غير موجودة.");
        }

        // Check if invoice is completed/posted
        if (invoice.Status == InvoiceStatus.Completed)
        {
            return ValidationResult.Failure(
                "INVOICE_POSTED",
                "Posted invoice cannot be deleted. Please issue a purchase return to reverse this transaction.",
                "الفاتورة مُرحّلة ولا يمكن حذفها. قم بعمل مرتجع مشتريات لعكس هذه العملية.");
        }

        // Check if invoice has any associated returns
        var hasReturns = await _context.Returns
            .AnyAsync(r => r.OriginalPurchaseInvoiceId == invoiceId && r.ReturnType == ReturnType.PurchaseReturn);

        if (hasReturns)
        {
            return ValidationResult.Failure(
                "INVOICE_HAS_RETURNS",
                "Invoice has associated returns and cannot be deleted.",
                "الفاتورة لها مرتجعات مرتبطة ولا يمكن حذفها.");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a purchase invoice request
    /// </summary>
    public async Task<ValidationResult> ValidatePurchaseInvoiceAsync(CreatePurchaseValidationRequest request)
    {
        // Validate supplier exists
        var supplier = await _context.Suppliers.FindAsync(request.SupplierId);
        if (supplier == null)
        {
            return ValidationResult.Failure(
                "SUPPLIER_NOT_FOUND",
                "Supplier not found.",
                "المورد غير موجود.");
        }

        // Validate warehouse exists
        var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId);
        if (warehouse == null)
        {
            return ValidationResult.Failure(
                "WAREHOUSE_NOT_FOUND",
                "Warehouse not found.",
                "المخزن غير موجود.");
        }

        // Validate payment method - only one allowed
        // Payment method validation is implicit in the PaymentMethod enum

        // Validate items
        if (request.Items == null || !request.Items.Any())
        {
            return ValidationResult.Failure(
                "NO_ITEMS",
                "Please add at least one item to the invoice.",
                "يرجى إضافة صنف واحد على الأقل إلى الفاتورة.");
        }

        foreach (var item in request.Items)
        {
            // Validate quantity > 0
            if (item.Quantity <= 0)
            {
                return ValidationResult.Failure(
                    "INVALID_QUANTITY",
                    "Quantity must be greater than 0.",
                    "الكمية يجب أن تكون أكبر من صفر.");
            }

            // Validate price > 0
            if (item.UnitCost <= 0)
            {
                return ValidationResult.Failure(
                    "INVALID_PRICE",
                    "Unit cost must be greater than 0.",
                    "سعر الوحدة يجب أن يكون أكبر من صفر.");
            }

            // Validate product exists
            var product = await _context.Products.FindAsync(item.ProductId);
            if (product == null)
            {
                return ValidationResult.Failure(
                    "PRODUCT_NOT_FOUND",
                    "One or more products not found.",
                    "منتج واحد أو أكثر غير موجود.");
            }
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Sales Invoice Validations

    /// <summary>
    /// Validates if a sales invoice can be deleted
    /// Posted/completed invoices cannot be deleted - use returns instead
    /// </summary>
    public async Task<ValidationResult> CanDeleteSalesInvoiceAsync(Guid invoiceId)
    {
        var invoice = await _context.SalesInvoices.FindAsync(invoiceId);
        if (invoice == null)
        {
            return ValidationResult.Failure(
                "INVOICE_NOT_FOUND",
                "Sales invoice not found.",
                "فاتورة البيع غير موجودة.");
        }

        // Check if invoice is completed/posted
        if (invoice.Status == InvoiceStatus.Completed)
        {
            return ValidationResult.Failure(
                "INVOICE_POSTED",
                "Posted invoice cannot be deleted. Please issue a sales return to reverse this transaction.",
                "الفاتورة مُرحّلة ولا يمكن حذفها. قم بعمل مرتجع مبيعات لعكس هذه العملية.");
        }

        // Check if invoice has any associated returns
        var hasReturns = await _context.Returns
            .AnyAsync(r => r.OriginalInvoiceId == invoiceId && r.ReturnType == ReturnType.SalesReturn);

        if (hasReturns)
        {
            return ValidationResult.Failure(
                "INVOICE_HAS_RETURNS",
                "Invoice has associated returns and cannot be deleted.",
                "الفاتورة لها مرتجعات مرتبطة ولا يمكن حذفها.");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a sales invoice request
    /// </summary>
    public async Task<ValidationResult> ValidateSalesInvoiceAsync(CreateSalesValidationRequest request)
    {
        // Validate warehouse exists
        var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId);
        if (warehouse == null)
        {
            return ValidationResult.Failure(
                "WAREHOUSE_NOT_FOUND",
                "Warehouse not found.",
                "المخزن غير موجود.");
        }

        // Validate customer if provided
        if (request.CustomerId.HasValue)
        {
            var customer = await _context.Customers.FindAsync(request.CustomerId.Value);
            if (customer == null)
            {
                return ValidationResult.Failure(
                    "CUSTOMER_NOT_FOUND",
                    "Customer not found.",
                    "العميل غير موجود.");
            }
        }

        // Validate items
        if (request.Items == null || !request.Items.Any())
        {
            return ValidationResult.Failure(
                "NO_ITEMS",
                "Please add at least one item to the invoice.",
                "يرجى إضافة صنف واحد على الأقل إلى الفاتورة.");
        }

        foreach (var item in request.Items)
        {
            // Validate quantity > 0
            if (item.Quantity <= 0)
            {
                return ValidationResult.Failure(
                    "INVALID_QUANTITY",
                    "Quantity must be greater than 0.",
                    "الكمية يجب أن تكون أكبر من صفر.");
            }

            // Validate price > 0
            if (item.UnitPrice <= 0)
            {
                return ValidationResult.Failure(
                    "INVALID_PRICE",
                    "Selling price is required and must be greater than 0.",
                    "سعر البيع مطلوب ويجب أن يكون أكبر من صفر.");
            }

            // Validate product exists
            var product = await _context.Products.FindAsync(item.ProductId);
            if (product == null)
            {
                return ValidationResult.Failure(
                    "PRODUCT_NOT_FOUND",
                    "One or more products not found.",
                    "منتج واحد أو أكثر غير موجود.");
            }

            // Check stock availability
            var stockResult = await CheckStockAvailabilityAsync(
                item.ProductId, 
                request.WarehouseId, 
                item.Quantity, 
                item.UnitId);

            if (!stockResult.IsValid)
            {
                return stockResult;
            }
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Return Validations

    /// <summary>
    /// Validates a sales return request
    /// </summary>
    public async Task<ValidationResult> ValidateSalesReturnAsync(SalesReturnValidationRequest request)
    {
        if (request.Items == null || !request.Items.Any())
        {
            return ValidationResult.Failure(
                "NO_ITEMS",
                "Please specify items to return.",
                "يرجى تحديد الأصناف للإرجاع.");
        }

        foreach (var item in request.Items)
        {
            // Validate return quantity doesn't exceed original
            if (item.ReturnQuantity > item.OriginalQuantity)
            {
                return ValidationResult.Failure(
                    "RETURN_EXCEEDS_ORIGINAL",
                    "Return quantity cannot exceed the original sold quantity.",
                    "كمية الإرجاع لا يمكن أن تتجاوز الكمية المباعة الأصلية.");
            }

            if (item.ReturnQuantity <= 0)
            {
                return ValidationResult.Failure(
                    "INVALID_RETURN_QUANTITY",
                    "Return quantity must be greater than 0.",
                    "كمية الإرجاع يجب أن تكون أكبر من صفر.");
            }
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a purchase return request
    /// </summary>
    public async Task<ValidationResult> ValidatePurchaseReturnAsync(PurchaseReturnValidationRequest request)
    {
        if (request.Items == null || !request.Items.Any())
        {
            return ValidationResult.Failure(
                "NO_ITEMS",
                "Please specify items to return.",
                "يرجى تحديد الأصناف للإرجاع.");
        }

        foreach (var item in request.Items)
        {
            // Validate return quantity doesn't exceed original
            if (item.ReturnQuantity > item.OriginalQuantity)
            {
                return ValidationResult.Failure(
                    "RETURN_EXCEEDS_ORIGINAL",
                    "Return quantity cannot exceed the original purchased quantity.",
                    "كمية الإرجاع لا يمكن أن تتجاوز الكمية المشتراة الأصلية.");
            }

            if (item.ReturnQuantity <= 0)
            {
                return ValidationResult.Failure(
                    "INVALID_RETURN_QUANTITY",
                    "Return quantity must be greater than 0.",
                    "كمية الإرجاع يجب أن تكون أكبر من صفر.");
            }
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Stock Validations

    /// <summary>
    /// Checks if sufficient stock is available for a sale
    /// </summary>
    public async Task<ValidationResult> CheckStockAvailabilityAsync(
        Guid productId, 
        Guid warehouseId, 
        decimal requestedQuantity, 
        Guid? unitId = null)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return ValidationResult.Failure(
                "PRODUCT_NOT_FOUND",
                "Product not found.",
                "المنتج غير موجود.");
        }

        // Get conversion factor
        decimal conversionFactor = 1;
        if (unitId.HasValue)
        {
            var productUnit = await _context.ProductUnits
                .FirstOrDefaultAsync(pu => pu.ProductId == productId && pu.UnitId == unitId);
            
            if (productUnit != null)
            {
                conversionFactor = productUnit.ConversionFactor;
            }
        }
        else
        {
            // Get default unit
            var defaultUnit = await _context.ProductUnits
                .FirstOrDefaultAsync(pu => pu.ProductId == productId && pu.IsDefault);
            
            if (defaultUnit != null)
            {
                conversionFactor = defaultUnit.ConversionFactor;
            }
        }

        // Calculate base quantity
        var baseQuantity = requestedQuantity * conversionFactor;

        // Inventory source of truth is ProductVariantStocks, not WarehouseStocks.
        var availableQuantity = await _context.ProductVariantStocks
            .Where(stock =>
                stock.WarehouseId == warehouseId &&
                stock.Variant.ProductId == productId)
            .SumAsync(stock => (decimal?)(stock.Quantity - stock.ReservedQuantity)) ?? 0;

        if (availableQuantity < baseQuantity)
        {
            return ValidationResult.Failure(
                "INSUFFICIENT_STOCK",
                $"Cannot sell with negative stock. Available: {availableQuantity} base units, Requested: {baseQuantity} base units for {product.Name}.",
                $"لا يمكن البيع برصيد سالب. المتوفر: {availableQuantity} وحدة أساسية، المطلوب: {baseQuantity} وحدة أساسية لـ {product.Name}.",
                new Dictionary<string, object>
                {
                    { "ProductId", productId },
                    { "ProductName", product.Name },
                    { "AvailableQuantity", availableQuantity },
                    { "RequestedQuantity", baseQuantity }
                });
        }

        return ValidationResult.Success();
    }

    #endregion
}
