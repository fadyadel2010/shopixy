namespace PointOfSale.Core.Events;

#region Inventory Events

/// <summary>
/// Event raised when stock is adjusted
/// </summary>
public class StockAdjustedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal OldQuantity { get; set; }
    public decimal NewQuantity { get; set; }
    public decimal Adjustment { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>
/// Event raised when stock is transferred between warehouses
/// </summary>
public class StockTransferredEvent : IntegrationEvent
{
    public Guid TransferId { get; set; }
    public string? TransferNumber { get; set; }
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public Guid FromWarehouseId { get; set; }
    public string? FromWarehouseName { get; set; }
    public Guid ToWarehouseId { get; set; }
    public string? ToWarehouseName { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>
/// Event raised when stock is reserved for an order
/// </summary>
public class StockReservedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>
/// Event raised when reserved stock is released
/// </summary>
public class StockReleasedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when stock is received from a purchase
/// </summary>
public class StockReceivedEvent : IntegrationEvent
{
    public Guid PurchaseInvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
}

#endregion
