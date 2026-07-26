using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services;

/// <summary>
/// Centralized validation service for protecting data integrity
/// </summary>
public interface IValidationService
{
    // Product Validations
    Task<ValidationResult> CanDeleteProductAsync(Guid productId);
    Task<ValidationResult> CanModifyProductUnitConversionAsync(Guid productId, Guid unitId, decimal newConversionFactor);
    
    // Customer Validations
    Task<ValidationResult> CanDeleteCustomerAsync(Guid customerId);
    
    // Supplier Validations
    Task<ValidationResult> CanDeleteSupplierAsync(Guid supplierId);
    
    // Invoice Validations
    Task<ValidationResult> CanDeletePurchaseInvoiceAsync(Guid invoiceId);
    Task<ValidationResult> CanDeleteSalesInvoiceAsync(Guid invoiceId);
    Task<ValidationResult> ValidatePurchaseInvoiceAsync(CreatePurchaseValidationRequest request);
    Task<ValidationResult> ValidateSalesInvoiceAsync(CreateSalesValidationRequest request);
    
    // Return Validations
    Task<ValidationResult> ValidateSalesReturnAsync(SalesReturnValidationRequest request);
    Task<ValidationResult> ValidatePurchaseReturnAsync(PurchaseReturnValidationRequest request);
    
    // Stock Validations
    Task<ValidationResult> CheckStockAvailabilityAsync(Guid productId, Guid warehouseId, decimal requestedQuantity, Guid? unitId = null);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorCode { get; set; }
    public string MessageEn { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();

    public static ValidationResult Success() => new() { IsValid = true };
    
    public static ValidationResult Failure(string errorCode, string messageEn, string messageAr, Dictionary<string, object>? data = null)
    {
        return new ValidationResult
        {
            IsValid = false,
            ErrorCode = errorCode,
            MessageEn = messageEn,
            MessageAr = messageAr,
            Data = data ?? new Dictionary<string, object>()
        };
    }
}

// Validation Request DTOs
public class CreatePurchaseValidationRequest
{
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal PaidAmount { get; set; }
    public List<PurchaseItemValidation> Items { get; set; } = new();
}

public class PurchaseItemValidation
{
    public Guid ProductId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class CreateSalesValidationRequest
{
    public Guid? CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal? CashAmount { get; set; }
    public decimal? CardAmount { get; set; }
    public List<SalesItemValidation> Items { get; set; } = new();
}

public class SalesItemValidation
{
    public Guid ProductId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class SalesReturnValidationRequest
{
    public Guid? OriginalInvoiceId { get; set; }
    public List<ReturnItemValidation> Items { get; set; } = new();
}

public class PurchaseReturnValidationRequest
{
    public Guid? OriginalInvoiceId { get; set; }
    public List<ReturnItemValidation> Items { get; set; } = new();
}

public class ReturnItemValidation
{
    public Guid ProductId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal OriginalQuantity { get; set; }
}
